using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Light))]
public class SunSkyRotator : MonoBehaviour
{
    [Header("Orbit")]
    public float orbitDegreesPerSecond = 0.75f;
    public Vector3 orbitAxis = Vector3.up;
    public bool keepOrbitOnEquator = true;

    [Header("Sun Visual")]
    public float sunDistance = 250f;
    public float sunScale = 8f;
    public Color sunColor = new Color(1f, 0.9f, 0.6f, 1f);
    [Min(0f)] public float sunEmissionIntensity = 4f;

    [Header("Sun Disc Shape")]
    [Range(0.01f, 1f)] public float coreRadius = 0.16f;
    [Range(0.01f, 2f)] public float glowRadius = 0.9f;
    [Range(0.5f, 16f)] public float glowFalloff = 3.5f;
    [Range(64, 512)] public int generatedTextureSize = 256;

    [Header("Material Template")]
    [Tooltip("Assign a Material asset using URP/Unlit, Surface Type Transparent.")]
    public Material sunMaterialTemplate;

    [Header("Camera-Relative Color Shift")]
    public Transform planetCenter;
    public Transform viewer;
    [Min(0.001f)] public float planetRadius = 8f;
    public float horizonTriggerOffsetDegrees = 0f;
    [Range(0.1f, 25f)] public float horizonTransitionDegrees = 6f;
    public Color horizonColor = new Color(1f, 0.45f, 0.2f, 1f);
    public Color dayColor = new Color(1f, 0.95f, 0.75f, 1f);
    [Range(0f, 1f)] public float colorShiftStrength = 1f;

    [Header("Emission Balancing")]
    [Range(0f, 2f)] public float dayEmissionMultiplier = 1f;
    [Range(0f, 2f)] public float behindPlanetEmissionMultiplier = 0.8f;
    [Range(0f, 2f)] public float horizonEmissionBoost = 0.3f;

    [Header("Skybox")]
    public bool rotateSkybox = true;
    public float skyboxRotationMultiplier = -1f;
    public Material skyboxOverride;

    [Header("Simulation Speed Coupling")]
    public bool scaleRotationWithSimulationSpeed = true;
    public ReplicatorManager replicatorManager;

    private Quaternion initialRotation;
    private float accumulatedOrbitAngle;
    private Vector3 initialOrbitForward;

    private Material originalSkybox;
    private Material runtimeSkybox;

    private GameObject generatedSunObject;
    private Material runtimeSunMaterial;
    private Texture2D runtimeSunTexture;
    private MeshFilter sunMeshFilter;
    private MeshRenderer sunMeshRenderer;

    void Start()
    {
        initialRotation = transform.rotation;
        CacheInitialOrbitForward();
        SetupViewerReference();
        ResolveReplicatorManagerReference();
        ResolvePlanetRadius();
        SetupSkybox();
        CreateSunVisual();
        UpdateSunVisualPosition();
        UpdateSunVisualAppearance();
    }

    void Update()
    {
        float dt = GetSimulationDeltaTime();
        Vector3 axis = GetOrbitAxis();

        accumulatedOrbitAngle += orbitDegreesPerSecond * dt;
        Quaternion orbitRotation = Quaternion.AngleAxis(accumulatedOrbitAngle, axis);

        if (keepOrbitOnEquator)
        {
            Vector3 orbitForward = orbitRotation * initialOrbitForward;
            transform.rotation = Quaternion.LookRotation(orbitForward, axis);
        }
        else
        {
            transform.rotation = orbitRotation * initialRotation;
        }

        if (rotateSkybox && runtimeSkybox != null && runtimeSkybox.HasFloat("_Rotation"))
        {
            float current = runtimeSkybox.GetFloat("_Rotation");
            runtimeSkybox.SetFloat("_Rotation", current + orbitDegreesPerSecond * skyboxRotationMultiplier * dt);
        }

        UpdateSunVisualPosition();
        UpdateSunVisualAppearance();
    }

    void LateUpdate()
    {
        BillboardSunVisual();
    }

    void CacheInitialOrbitForward()
    {
        Vector3 axis = GetOrbitAxis();
        Vector3 projectedForward = Vector3.ProjectOnPlane(initialRotation * Vector3.forward, axis);

        if (projectedForward.sqrMagnitude < 0.0001f)
        {
            projectedForward = Vector3.Cross(axis, Vector3.right);
            if (projectedForward.sqrMagnitude < 0.0001f)
            {
                projectedForward = Vector3.Cross(axis, Vector3.forward);
            }
        }

        initialOrbitForward = projectedForward.normalized;
    }

    Vector3 GetOrbitAxis()
    {
        if (orbitAxis.sqrMagnitude < 0.0001f)
        {
            return Vector3.up;
        }

        return orbitAxis.normalized;
    }

    void OnDestroy()
    {
        if (runtimeSkybox != null && RenderSettings.skybox == runtimeSkybox)
        {
            RenderSettings.skybox = originalSkybox;
        }

        DestroyRuntimeObject(generatedSunObject);
        DestroyRuntimeObject(runtimeSunMaterial);
        DestroyRuntimeObject(runtimeSunTexture);
        DestroyRuntimeObject(runtimeSkybox);
    }

    void SetupViewerReference()
    {
        if (viewer == null && Camera.main != null)
        {
            viewer = Camera.main.transform;
        }
    }

    void ResolveReplicatorManagerReference()
    {
        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }
    }

    float GetSimulationDeltaTime()
    {
        if (!scaleRotationWithSimulationSpeed)
        {
            return Time.unscaledDeltaTime;
        }

        ResolveReplicatorManagerReference();
        if (replicatorManager == null)
        {
            return Time.unscaledDeltaTime;
        }

        return Mathf.Max(0f, replicatorManager.FrameSimulationDeltaTime);
    }

    void ResolvePlanetRadius()
    {
        if (planetCenter == null) return;

        PlanetGenerator generator = planetCenter.GetComponent<PlanetGenerator>();
        if (generator != null)
        {
            planetRadius = Mathf.Max(0.001f, generator.radius);
        }
    }

    void SetupSkybox()
    {
        originalSkybox = RenderSettings.skybox;
        Material source = skyboxOverride != null ? skyboxOverride : RenderSettings.skybox;
        if (source == null) return;

        runtimeSkybox = new Material(source);
        RenderSettings.skybox = runtimeSkybox;
    }

    void CreateSunVisual()
    {
        generatedSunObject = new GameObject("Sun Visual");
        sunMeshFilter = generatedSunObject.AddComponent<MeshFilter>();
        sunMeshRenderer = generatedSunObject.AddComponent<MeshRenderer>();

        sunMeshFilter.sharedMesh = BuildQuadMesh();
        runtimeSunTexture = BuildSunTexture();
        runtimeSunMaterial = BuildSunMaterial();

        if (runtimeSunMaterial != null)
        {
            sunMeshRenderer.sharedMaterial = runtimeSunMaterial;
        }

        sunMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        sunMeshRenderer.receiveShadows = false;
        sunMeshRenderer.lightProbeUsage = LightProbeUsage.Off;
        sunMeshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        sunMeshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        generatedSunObject.transform.localScale = Vector3.one * sunScale;
        BillboardSunVisual();
    }

    Mesh BuildQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "SunQuad";

        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f)
        };

        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };

        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    Texture2D BuildSunTexture()
    {
        int size = Mathf.Clamp(generatedTextureSize, 64, 512);
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        texture.name = "RuntimeSunDisc";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        float inner = Mathf.Clamp01(coreRadius);
        float outer = Mathf.Max(inner + 0.001f, glowRadius);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);

                float alpha;
                if (r <= inner)
                {
                    alpha = 1f;
                }
                else if (r >= outer)
                {
                    alpha = 0f;
                }
                else
                {
                    float t = 1f - Mathf.InverseLerp(inner, outer, r);
                    alpha = Mathf.Pow(t, glowFalloff);
                }

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply(false, false);
        return texture;
    }

    Material BuildSunMaterial()
    {
        if (sunMaterialTemplate == null)
        {
            Debug.LogError("SunSkyRotator: sunMaterialTemplate is not assigned.");
            return null;
        }

        Material material = new Material(sunMaterialTemplate);
        material.name = "Runtime Sun Material";

        if (material.HasProperty("_BaseMap") && runtimeSunTexture != null)
        {
            material.SetTexture("_BaseMap", runtimeSunTexture);
        }

        if (material.HasProperty("_MainTex") && runtimeSunTexture != null)
        {
            material.SetTexture("_MainTex", runtimeSunTexture);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }

    void UpdateSunVisualPosition()
    {
        if (generatedSunObject == null) return;

        Vector3 center = planetCenter != null ? planetCenter.position : Vector3.zero;
        generatedSunObject.transform.position = center - transform.forward * sunDistance;
        generatedSunObject.transform.localScale = Vector3.one * sunScale;
        BillboardSunVisual();
    }

    void BillboardSunVisual()
    {
        if (generatedSunObject == null) return;

        SetupViewerReference();
        if (viewer == null) return;

        Vector3 toCamera = viewer.position - generatedSunObject.transform.position;
        if (toCamera.sqrMagnitude < 0.000001f) return;

        generatedSunObject.transform.rotation = Quaternion.LookRotation(toCamera.normalized, viewer.up);
    }

    void UpdateSunVisualAppearance()
    {
        if (runtimeSunMaterial == null) return;

        EvaluateSunAppearance(out Color shiftedColor, out float emissionMultiplier);

        Color finalColor = shiftedColor * Mathf.Max(0f, sunEmissionIntensity * emissionMultiplier);
        finalColor.a = 1f;

        if (runtimeSunMaterial.HasProperty("_BaseColor"))
        {
            runtimeSunMaterial.SetColor("_BaseColor", finalColor);
        }

        if (runtimeSunMaterial.HasProperty("_Color"))
        {
            runtimeSunMaterial.SetColor("_Color", finalColor);
        }
    }

    void EvaluateSunAppearance(out Color shiftedColor, out float emissionMultiplier)
    {
        SetupViewerReference();

        Vector3 center = planetCenter != null ? planetCenter.position : Vector3.zero;
        if (viewer == null)
        {
            shiftedColor = Color.Lerp(sunColor, dayColor * sunColor, colorShiftStrength);
            emissionMultiplier = dayEmissionMultiplier;
            return;
        }

        Vector3 cameraPos = viewer.position;
        Vector3 toCenter = center - cameraPos;
        float distanceToCenter = toCenter.magnitude;
        Vector3 centerDir = toCenter.normalized;

        Vector3 sunPosition = center - transform.forward * sunDistance;
        Vector3 sunDir = (sunPosition - cameraPos).normalized;

        float angleToSunFromCenterDir = Vector3.Angle(centerDir, sunDir);
        float planetAngularRadius = Mathf.Asin(Mathf.Clamp01(planetRadius / Mathf.Max(distanceToCenter, planetRadius + 0.001f))) * Mathf.Rad2Deg;
        float horizonAngle = planetAngularRadius + horizonTriggerOffsetDegrees;
        float deltaFromHorizon = angleToSunFromCenterDir - horizonAngle;

        bool behindPlanet = deltaFromHorizon < 0f;
        float transition = Mathf.Max(0.1f, horizonTransitionDegrees);

        float horizonFactor = 1f - Mathf.Clamp01(Mathf.Abs(deltaFromHorizon) / transition);
        float dayAmount = Mathf.Clamp01(deltaFromHorizon / transition);

        Color visibleColor = Color.Lerp(dayColor, horizonColor, horizonFactor);
        Color finalColor = behindPlanet ? horizonColor : Color.Lerp(horizonColor, visibleColor, dayAmount);

        shiftedColor = Color.Lerp(sunColor, finalColor * sunColor, colorShiftStrength);

        emissionMultiplier = behindPlanet
            ? behindPlanetEmissionMultiplier
            : Mathf.Lerp(behindPlanetEmissionMultiplier, dayEmissionMultiplier, dayAmount);

        emissionMultiplier += horizonFactor * horizonEmissionBoost;
    }

    void DestroyRuntimeObject(Object obj)
    {
        if (obj == null) return;

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }
}