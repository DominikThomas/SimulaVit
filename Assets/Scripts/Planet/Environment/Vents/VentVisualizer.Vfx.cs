using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public partial class VentVisualizer
{
    private static readonly Unity.Profiling.ProfilerMarker VentUpdateMarker = new Unity.Profiling.ProfilerMarker("VentVisualizer.LateUpdate");
    public enum SmokerAppearance { Black, White, Mixed }

    [Header("Hydrothermal Effects")]
    public bool enableSmoke = true;
    public bool enableHeatDistortion = true;
    [Range(VentVisualSettings.MinGlowSize, VentVisualSettings.MaxGlowSize)] public float ventGlowSizeMultiplier = 0.85f;
    [Range(0f, VentVisualSettings.MaxSmokeIntensity)] public float smokeIntensityMultiplier = 1f;
    [Range(0f, VentVisualSettings.MaxSmokeHeight)] public float smokeHeightMultiplier = 1f;
    [Range(0f, VentVisualSettings.MaxSmokeWidth), InspectorName("Smoke Base Width Multiplier")]
    public float smokeWidthMultiplier = 1.75f;
    [Min(0.01f)] public float smokeUpwardSpeedMultiplier = 1f;
    [Range(0f, 1f)] public float smokeTurbulenceStrength = 0.25f;
    public SmokerAppearance smokerAppearance = SmokerAppearance.Mixed;
    [Range(0f, 0.2f), Tooltip("URP particle distortion strength; deliberately very small for underwater mixing.")]
    public float distortionStrength = 0.03f;
    [Min(0f), Tooltip("Rendering distance in world units. Smoke keeps simulating and emitting beyond this distance.")]
    public float vfxVisibilityDistance = 6f;
    [Min(1), Tooltip("Maximum rendered outlets. All smoke emitters continue simulating, with at most 96 particles each.")]
    public int maxActiveVentEffects = 48;
    [Tooltip("Shared URP Particles/Unlit material with distortion, normal mapping and soft particles enabled.")]
    public Material heatDistortionMaterial;

    private sealed class VentEffect
    {
        public Transform Anchor;
        public ParticleSystem Smoke, Heat;
        public ParticleSystemRenderer SmokeRenderer, HeatRenderer;
        public readonly MaterialPropertyBlock FadeProperties = new MaterialPropertyBlock();
        public float Strength, Depth, Radius, CoreRadius, SmokeRate, HeatRate, DistanceSquared;
        public int Cell;
        public bool Selected;
    }

    private sealed class EffectDistanceComparer : IComparer<VentEffect>
    {
        public static readonly EffectDistanceComparer Instance = new EffectDistanceComparer();
        public int Compare(VentEffect a, VentEffect b) => a.DistanceSquared.CompareTo(b.DistanceSquared);
    }

    private readonly List<VentEffect> ventEffects = new List<VentEffect>();
    private readonly List<VentEffect> visibleEffects = new List<VentEffect>();
    private readonly List<(Transform marker, float baseSize)> glowVisuals = new List<(Transform, float)>();
    private SimulationStartupConfig boundStartupConfig;
    private GameObject effectsRoot;
    private Material sharedSootMaterial, sharedHeatMaterial;
    private Texture2D plumeMask, mixingNormals;
    private bool geodesicEffects, appearanceDirty, renderingDormant = true;
    private float nextVisibilityUpdate, maximumEffectRadius;

    // The root follows planet translation/rotation, but never inherits marker or parent scale.
    // Local simulation keeps already emitted particles attached to a moving/rotating planet.
    public void BeginGeodesicEffects(PlanetGenerator generator)
    {
        ClearVentVisuals("geodesic outlet effects initialized");
        planetGenerator = generator;
        geodesicEffects = true;
        EnsureEffectsRoot();
    }

    public void AddGeodesicOutletEffect(GeodesicVentOutlet outlet)
    {
        if (planetGenerator == null || outlet.Habitat != GeodesicVentHabitat.Submarine || !showUnderwaterVents) return;
        Vector3 direction = outlet.PlanetLocalPosition.normalized;
        float floor = outlet.PlanetLocalPosition.magnitude;
        float top = planetGenerator.GeodesicSeaLevelRadius;
        float lift = Mathf.Min(Mathf.Max(0f, surfaceOffset), Mathf.Max(0f, top - floor) * 0.03f);
        Vector3 position = planetGenerator.transform.TransformPoint(outlet.PlanetLocalPosition + direction * lift);
        float radialScale = planetGenerator.transform.TransformVector(direction).magnitude;
        AddOutletEffect(position, (top - floor - lift) * radialScale, floor * radialScale,
            outlet.HotCoreRadius * radialScale, outlet.Strength01, outlet.CellIndex);
    }

    private void AttachSoot(Vector3 position, VentAnchor anchor, float strength, int cell)
    {
        // Land markers remain available; underwater smokers require an actual water column.
        if (!anchor.IsOcean) return;
        float radius = Vector3.Distance(position, planetGenerator.transform.position);
        AddOutletEffect(position, anchor.OceanTopRadius - radius, radius,
            Mathf.Lerp(glowRadiusMin, glowRadiusMax, strength) * 0.5f, strength, cell);
    }

    private void EnsureEffectsRoot()
    {
        if (effectsRoot != null || planetGenerator == null) return;
        effectsRoot = new GameObject("Hydrothermal Vent Effects") { layer = gameObject.layer };
        effectsRoot.transform.SetPositionAndRotation(planetGenerator.transform.position, planetGenerator.transform.rotation);
        effectsRoot.transform.localScale = Vector3.one;
        effectsRoot.SetActive(isActiveAndEnabled);
        EnsureSharedMaterials();
    }

    private void AddOutletEffect(Vector3 position, float depth, float radius, float coreRadius, float strength, int cell)
    {
        if (!float.IsFinite(depth) || depth <= 0.002f || radius <= 0f) return;
        EnsureEffectsRoot();
        if (effectsRoot == null) return;
        var anchor = new GameObject($"Vent VFX {cell}") { layer = gameObject.layer };
        anchor.transform.SetParent(effectsRoot.transform, false);
        anchor.transform.SetPositionAndRotation(position, RadialEmissionRotation(position, planetGenerator.transform.position));
        anchor.transform.localScale = Vector3.one;
        // Instantiate under an inactive anchor: the prefab cannot emit before its transform/modules are corrected.
        anchor.SetActive(false);
        var effect = new VentEffect
        {
            Anchor = anchor.transform, Strength = Mathf.Clamp01(strength), Depth = depth,
            Radius = radius, CoreRadius = coreRadius, Cell = cell
        };
        effect.Smoke = sootPrefab != null ? Instantiate(sootPrefab, anchor.transform, false) : CreateRuntimeSoot(anchor.transform);
        effect.Heat = CreateRuntimeSoot(anchor.transform);
        effect.Heat.name = "Thermal Mixing";
        effect.SmokeRenderer = effect.Smoke.GetComponent<ParticleSystemRenderer>();
        effect.HeatRenderer = effect.Heat.GetComponent<ParticleSystemRenderer>();
        ConfigureEffect(effect);
        ventEffects.Add(effect);
        anchor.SetActive(true);
        ApplySmokeSimulation(effect);
        maximumEffectRadius = Mathf.Max(maximumEffectRadius, radius + depth);
        nextVisibilityUpdate = 0f;
    }

    public static Quaternion RadialEmissionRotation(Vector3 ventPosition, Vector3 planetCenter)
    {
        Vector3 radialUp = (ventPosition - planetCenter).normalized;
        if (radialUp.sqrMagnitude < 0.5f) return Quaternion.identity;
        // A cone emits along local +Z. This reference only fixes roll, including at both poles.
        Vector3 tangent = Vector3.Cross(radialUp, Vector3.right);
        if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(radialUp, Vector3.forward);
        return Quaternion.LookRotation(radialUp, tangent.normalized);
    }

    public static bool UsesWhiteSmoker(SmokerAppearance appearance, int cell, int seed)
    {
        if (appearance != SmokerAppearance.Mixed) return appearance == SmokerAppearance.White;
        // Visual-only hash, independent of Unity's simulation random state and all chemistry.
        uint hash = unchecked((uint)cell * 747796405u + (uint)seed * 2891336453u + 277803737u);
        hash = (hash ^ (hash >> 16)) * 2246822519u;
        return (hash & 3u) == 0u;
    }

    private ParticleSystem CreateRuntimeSoot(Transform anchor)
    {
        var go = new GameObject("Soot") { layer = gameObject.layer };
        go.transform.SetParent(anchor, false);
        return go.AddComponent<ParticleSystem>();
    }

    private void ConfigureEffect(VentEffect effect)
    {
        bool white = UsesWhiteSmoker(smokerAppearance, effect.Cell, planetGenerator.randomSeed);
        float height = Mathf.Min(effect.Radius * 0.05f * Mathf.Max(0f, smokeHeightMultiplier), effect.Depth * 0.55f);
        // Size growth, shape radius and lateral noise together stay inside the remaining water column.
        float growth = white ? 3.6f : 2.8f;
        float size = Mathf.Min(Mathf.Min(effect.Radius * 0.0025f, effect.CoreRadius * 0.6f)
            * Mathf.Max(0f, smokeWidthMultiplier) * (white ? 1.2f : 1f), effect.Depth * 0.24f / growth);
        size = Mathf.Max(0.00001f, size);
        float speed = Mathf.Min(effect.Radius * 0.012f * Mathf.Max(0.01f, smokeUpwardSpeedMultiplier), Mathf.Max(0.00001f, height / 0.05f));
        float lifetime = Mathf.Min(6f, height / (speed * 1.08f));
        effect.SmokeRate = Mathf.Min(40f, Mathf.Lerp(12f, 22f, effect.Strength) * Mathf.Max(0f, smokeIntensityMultiplier));
        ConfigureParticles(effect.Smoke, size, speed, lifetime, effect.SmokeRate, 96, sharedSootMaterial, true);
        var main = effect.Smoke.main;
        main.startColor = white ? new Color(0.72f, 0.77f, 0.79f, 0.45f) : new Color(0.10f, 0.12f, 0.13f, 0.72f);
        var sizeOverLifetime = effect.Smoke.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.65f), new Keyframe(0.25f, 1f), new Keyframe(1f, growth)));
        var noise = effect.Smoke.noise;
        noise.enabled = smokeTurbulenceStrength > 0f;
        noise.separateAxes = true;
        var ageNoise = new ParticleSystem.MinMaxCurve(size * Mathf.Clamp01(smokeTurbulenceStrength), new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.2f, 0f), new Keyframe(1f, 1f)));
        noise.strengthX = ageNoise;
        noise.strengthY = ageNoise;
        noise.strengthZ = 0f; // Never disturb the radial velocity.
        noise.frequency = 0.7f;
        noise.scrollSpeed = 0.08f;
        noise.damping = false;
        noise.octaveCount = 1;
        noise.quality = ParticleSystemNoiseQuality.Low;
        noise.positionAmount = 0.35f;
        noise.rotationAmount = 0.15f;
        noise.sizeAmount = 0.12f;

        // Heat is confined to the lower third, with a few broad, overlapping soft patches.
        float heatSize = Mathf.Min(effect.Radius * 0.006f, effect.CoreRadius * 0.9f, effect.Depth * 0.12f);
        float heatHeight = Mathf.Min(effect.Radius * 0.015f, effect.Depth * 0.18f);
        float heatSpeed = Mathf.Min(effect.Radius * 0.0042f, heatHeight / 0.05f);
        float heatLifetime = Mathf.Min(2.5f, heatHeight / (heatSpeed * 1.08f));
        effect.HeatRate = Mathf.Lerp(2f, 4f, effect.Strength);
        ConfigureParticles(effect.Heat, heatSize, heatSpeed, heatLifetime, effect.HeatRate, 12, sharedHeatMaterial);
        main = effect.Heat.main;
        main.startColor = new Color(1f, 1f, 1f, 0.22f);
        var heatGrowth = effect.Heat.sizeOverLifetime;
        heatGrowth.enabled = true;
        heatGrowth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
        var rotation = effect.Heat.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.18f, 0.18f);
        ApplySmokeSimulation(effect);
    }

    private static void ConfigureParticles(ParticleSystem ps, float size, float speed, float lifetime, float rate, int limit, Material material, bool continuous = false)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.gameObject.layer = ps.transform.parent.gameObject.layer;
        ps.transform.localPosition = Vector3.zero;
        ps.transform.localRotation = Quaternion.identity;
        ps.transform.localScale = Vector3.one;
        var main = ps.main;
        main.loop = true;
        main.prewarm = continuous;
        main.playOnAwake = false;
        main.startDelay = 0f;
        main.duration = 6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Max(0.01f, lifetime * 0.85f), Mathf.Max(0.01f, lifetime));
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed, speed * 1.08f);
        main.startSize3D = false;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size);
        main.startRotation3D = false;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.simulationSpeed = 1f;
        main.useUnscaledTime = true;
        main.maxParticles = limit;
        main.stopAction = ParticleSystemStopAction.None;
        main.cullingMode = continuous ? ParticleSystemCullingMode.AlwaysSimulate : ParticleSystemCullingMode.Pause;
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = rate;
        emission.rateOverDistance = 0f;
        emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 0f;
        shape.radius = size * 0.15f;
        shape.radiusThickness = 1f;
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;
        shape.scale = Vector3.one;
        shape.randomDirectionAmount = 0f;
        shape.sphericalDirectionAmount = 0f;
        shape.randomPositionAmount = 0f;
        shape.alignToDirection = false;
        // Explicitly remove prefab motion modules that could reintroduce sideways jets.
        var velocity = ps.velocityOverLifetime; velocity.enabled = false;
        var force = ps.forceOverLifetime; force.enabled = false;
        var inherit = ps.inheritVelocity; inherit.enabled = false;
        var limitVelocity = ps.limitVelocityOverLifetime; limitVelocity.enabled = false;
        var external = ps.externalForces; external.enabled = false;
        var collision = ps.collision; collision.enabled = false;
        var subEmitters = ps.subEmitters; subEmitters.enabled = false;
        var noise = ps.noise; noise.enabled = false;
        var sizeLife = ps.sizeOverLifetime; sizeLife.enabled = false;
        var sizeSpeed = ps.sizeBySpeed; sizeSpeed.enabled = false;
        var colorSpeed = ps.colorBySpeed; colorSpeed.enabled = false;
        var rotation = ps.rotationOverLifetime; rotation.enabled = false;
        var rotationSpeed = ps.rotationBySpeed; rotationSpeed.enabled = false;
        var textureSheet = ps.textureSheetAnimation; textureSheet.enabled = false;
        var trails = ps.trails; trails.enabled = false;
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(0.65f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.enabled = false;
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.minParticleSize = 0f;
        renderer.maxParticleSize = 0.1f;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.Tangent });
    }

    private void EnsureSharedMaterials()
    {
        if (sharedSootMaterial != null) return;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return;
        BuildMixingTextures();
        var prefabRenderer = sootPrefab != null ? sootPrefab.GetComponent<ParticleSystemRenderer>() : null;
        Material source = prefabRenderer != null ? prefabRenderer.sharedMaterial : null;
        sharedSootMaterial = source != null ? new Material(source) : new Material(shader);
        sharedSootMaterial.name = "Vent Soot (Shared Runtime)";
        sharedSootMaterial.shader = shader;
        ConfigureTransparentMaterial(sharedSootMaterial, 2990);
        sharedSootMaterial.SetColor("_BaseColor", Color.white);
        sharedSootMaterial.SetTexture("_BaseMap", plumeMask);
        sharedSootMaterial.DisableKeyword("_DISTORTION_ON");
        sharedSootMaterial.DisableKeyword("_NORMALMAP");
        sharedSootMaterial.DisableKeyword("_SOFTPARTICLES_ON");
        sharedSootMaterial.DisableKeyword("_FLIPBOOKBLENDING_ON");
        sharedHeatMaterial = heatDistortionMaterial != null ? new Material(heatDistortionMaterial) : new Material(shader);
        sharedHeatMaterial.name = "Vent Thermal Mixing (Shared Runtime)";
        ConfigureTransparentMaterial(sharedHeatMaterial, 2980);
        sharedHeatMaterial.SetTexture("_BaseMap", plumeMask);
        sharedHeatMaterial.SetTexture("_BumpMap", mixingNormals);
        sharedHeatMaterial.SetColor("_BaseColor", Color.white);
        sharedHeatMaterial.SetFloat("_DistortionBlend", 1f); // Refraction only, no visible colored bubble.
        sharedHeatMaterial.EnableKeyword("_DISTORTION_ON");
        sharedHeatMaterial.EnableKeyword("_NORMALMAP");
        sharedHeatMaterial.EnableKeyword("_SOFTPARTICLES_ON");
        sharedHeatMaterial.SetVector("_SoftParticleFadeParams", new Vector4(0f, 50f, 0f, 0f));
    }

    private static void ConfigureTransparentMaterial(Material material, int queue)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.DisableKeyword("_EMISSION");
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.renderQueue = queue;
    }

    private void BuildMixingTextures()
    {
        const int resolution = 64;
        plumeMask = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            { name = "Vent Soft Irregular Mask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        mixingNormals = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            { name = "Vent Mixing Normals", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var mask = new Color[resolution * resolution];
        var normals = new Color[mask.Length];
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            float u = x / (resolution - 1f), v = y / (resolution - 1f);
            float noise = Mathf.PerlinNoise(u * 5f + 4.1f, v * 5f + 1.7f);
            float r = new Vector2(u * 2f - 1f, v * 2f - 1f).magnitude;
            float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - r) * 1.8f)) * Mathf.Lerp(0.45f, 1f, noise);
            mask[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
            float nx = (noise - 0.5f) * 0.7f;
            float ny = (Mathf.PerlinNoise(u * 5f + 11.8f, v * 5f + 7.3f) - 0.5f) * 0.7f;
            // RG/AG-compatible normal encoding. These are data textures, never imported/compressed normals.
            normals[y * resolution + x] = new Color(nx * 0.5f + 0.5f, ny * 0.5f + 0.5f,
                Mathf.Sqrt(1f - nx * nx - ny * ny) * 0.5f + 0.5f, 1f);
        }
        plumeMask.SetPixels(mask); plumeMask.Apply(false, true);
        mixingNormals.SetPixels(normals); mixingNormals.Apply(false, true);
    }

    private void OnValidate()
    {
        // The Inspector edits the same pending startup values as the GUI, without frame-by-frame copying.
        if (boundStartupConfig != null)
        {
            boundStartupConfig.ventVisuals = CaptureVisualSettings().Normalized();
            AssignVisualSettings(boundStartupConfig.ventVisuals);
        }
        appearanceDirty = true;
        nextVisibilityUpdate = 0f;
    }

    private void LateUpdate()
    {
        using var performanceScope = VentUpdateMarker.Auto();
        if (appearanceDirty) RefreshVisualConfiguration();
        if (effectsRoot == null || planetGenerator == null) return;
        if (effectsRoot.transform.position != planetGenerator.transform.position || effectsRoot.transform.rotation != planetGenerator.transform.rotation)
            effectsRoot.transform.SetPositionAndRotation(planetGenerator.transform.position, planetGenerator.transform.rotation);
        if (Time.unscaledTime < nextVisibilityUpdate) return;
        nextVisibilityUpdate = Time.unscaledTime + 0.25f;
        UpdateEffectVisibility(Camera.main);
    }

    private void UpdateEffectVisibility(Camera camera)
    {
        visibleEffects.Clear();
        float distanceLimit = Mathf.Max(0f, vfxVisibilityDistance);
        bool canView = camera != null && distanceLimit > 0f && (enableSmoke || enableHeatDistortion);
        if (canView && Vector3.Distance(camera.transform.position, planetGenerator.transform.position)
            > maximumEffectRadius + distanceLimit) canView = false;
        // Whole-planet view: renderers stay off, but every smoke emitter keeps its complete plume.
        if (!canView && renderingDormant) return;
        bool canRefract = canView && SupportsRefraction(camera);
        if (sharedHeatMaterial != null)
        {
            sharedHeatMaterial.SetFloat("_DistortionStrength", distortionStrength);
            sharedHeatMaterial.SetFloat("_DistortionStrengthScaled", Mathf.Clamp(distortionStrength, 0f, 0.2f) * 0.1f);
        }
        for (int i = 0; i < ventEffects.Count; i++)
        {
            VentEffect effect = ventEffects[i];
            effect.Selected = false;
            if (!canView || (camera.cullingMask & (1 << effect.Anchor.gameObject.layer)) == 0) continue;
            Vector3 toCamera = camera.transform.position - effect.Anchor.position;
            effect.DistanceSquared = toCamera.sqrMagnitude;
            if (effect.DistanceSquared > distanceLimit * distanceLimit) continue;
            Vector3 radial = effect.Anchor.position - planetGenerator.transform.position;
            // A small limb margin avoids abrupt disappearance of the upper plume.
            if (Vector3.Dot(toCamera, radial.normalized) < -effect.Depth) continue;
            Vector3 viewport = camera.WorldToViewportPoint(effect.Anchor.position);
            if (viewport.z <= 0f || viewport.x < -0.1f || viewport.x > 1.1f || viewport.y < -0.1f || viewport.y > 1.1f) continue;
            visibleEffects.Add(effect);
        }
        visibleEffects.Sort(EffectDistanceComparer.Instance);
        int activeCount = Mathf.Min(Mathf.Max(1, maxActiveVentEffects), visibleEffects.Count);
        for (int i = 0; i < activeCount; i++) visibleEffects[i].Selected = true;
        renderingDormant = true;
        for (int i = 0; i < ventEffects.Count; i++)
        {
            VentEffect effect = ventEffects[i];
            float fade = effect.Selected ? 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(distanceLimit * 0.5f, distanceLimit, Mathf.Sqrt(effect.DistanceSquared))) : 0f;
            bool smoke = fade > 0.01f && enableSmoke && smokeIntensityMultiplier > 0f && smokeHeightMultiplier > 0f && smokeWidthMultiplier > 0f;
            bool heat = fade > 0.01f && enableHeatDistortion && canRefract && distortionStrength > 0f;
            if (!smoke && !heat && !effect.SmokeRenderer.enabled && !effect.HeatRenderer.enabled) continue;
            if (smoke || heat) renderingDormant = false;
            effect.FadeProperties.SetColor("_BaseColor", new Color(1f, 1f, 1f, fade));
            effect.SmokeRenderer.SetPropertyBlock(effect.FadeProperties);
            effect.HeatRenderer.SetPropertyBlock(effect.FadeProperties);
            // Camera visibility never changes smoke emission, simulation state or particle age.
            effect.SmokeRenderer.enabled = smoke && sharedSootMaterial != null;
            UpdateParticleVisibility(effect.Heat, effect.HeatRenderer, heat, effect.HeatRate * fade);
        }
    }

    private static void UpdateParticleVisibility(ParticleSystem ps, ParticleSystemRenderer renderer, bool visible, float rate)
    {
        var emission = ps.emission;
        emission.rateOverTime = rate;
        renderer.enabled = visible && renderer.sharedMaterial != null;
        if (visible && !ps.isPlaying) ps.Play(false);
        else if (!visible && !ps.isStopped) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private bool SmokeSimulationEnabled => enableSmoke && smokeIntensityMultiplier > 0f && smokeHeightMultiplier > 0f && smokeWidthMultiplier > 0f;

    private void ApplySmokeSimulation(VentEffect effect)
    {
        if (SmokeSimulationEnabled)
        {
            if (effect.Smoke.gameObject.activeInHierarchy && !effect.Smoke.isPlaying) effect.Smoke.Play(false);
        }
        else if (!effect.Smoke.isStopped) effect.Smoke.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void ResumeSmokeSimulation()
    {
        for (int i = 0; i < ventEffects.Count; i++) ApplySmokeSimulation(ventEffects[i]);
    }

    private static bool SupportsRefraction(Camera camera)
    {
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return false;
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        return data != null ? data.requiresColorTexture && data.requiresDepthTexture
            : pipeline.supportsCameraOpaqueTexture && pipeline.supportsCameraDepthTexture;
    }

    private void ClearEffects()
    {
        if (effectsRoot != null)
        {
            effectsRoot.SetActive(false);
            DestroyVisualObject(effectsRoot);
        }
        effectsRoot = null;
        ventEffects.Clear(); visibleEffects.Clear();
        glowVisuals.Clear();
        geodesicEffects = false;
        renderingDormant = true;
        appearanceDirty = false; // Rebuilt emitters will use the current values directly.
        maximumEffectRadius = 0f;
    }

    private void OnDestroy()
    {
        ClearEffects();
        DestroyVisualObject(sharedSootMaterial); DestroyVisualObject(sharedHeatMaterial);
        DestroyVisualObject(plumeMask); DestroyVisualObject(mixingNormals);
    }

    private static void DestroyVisualObject(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
