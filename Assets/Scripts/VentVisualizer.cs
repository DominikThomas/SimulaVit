using UnityEngine;

[DisallowMultipleComponent]
public class VentVisualizer : MonoBehaviour
{
    [Header("References")]
    public PlanetResourceMap resourceMap;
    public PlanetGenerator planetGenerator;
    public Transform parent;
    public Material ventGlowMaterial;
    public ParticleSystem sootPrefab;

    [Header("Glow Settings")]
    public float glowRadiusMin = 0.05f;
    public float glowRadiusMax = 0.15f;
    public float glowEmissionMin = 0.5f;
    public float glowEmissionMax = 3.0f;
    public float surfaceOffset = 0.01f;
    public bool showLandVents = true;
    public bool showUnderwaterVents = true;

    private static Mesh markerMesh;

    private void Awake()
    {
        if (resourceMap == null)
        {
            resourceMap = GetComponent<PlanetResourceMap>();
        }

        if (planetGenerator == null)
        {
            planetGenerator = GetComponent<PlanetGenerator>();
        }

        if (parent == null)
        {
            parent = transform;
        }
    }

    private void Start()
    {
        BuildVentVisuals();
    }

    public void BuildVentVisuals()
    {
        if (resourceMap == null || planetGenerator == null || resourceMap.ventStrength == null)
        {
            return;
        }

        int[] vents = resourceMap.VentCells;
        Vector3[] cellDirs = resourceMap.CellDirs;
        if (cellDirs == null || cellDirs.Length == 0)
        {
            return;
        }

        float oceanRadius = planetGenerator.GetOceanRadius();
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        if (vents != null && vents.Length > 0)
        {
            for (int i = 0; i < vents.Length; i++)
            {
                BuildVentVisual(vents[i], oceanRadius, cellDirs, propertyBlock);
            }
            return;
        }

        for (int cell = 0; cell < resourceMap.ventStrength.Length; cell++)
        {
            if (resourceMap.ventStrength[cell] > 0f)
            {
                BuildVentVisual(cell, oceanRadius, cellDirs, propertyBlock);
            }
        }
    }

    private void BuildVentVisual(int cell, float oceanRadius, Vector3[] cellDirs, MaterialPropertyBlock propertyBlock)
    {
        if (cell < 0 || cell >= cellDirs.Length || cell >= resourceMap.ventStrength.Length)
        {
            return;
        }

        float strength = resourceMap.ventStrength[cell];
        if (strength <= 0f)
        {
            return;
        }

        Vector3 dir = cellDirs[cell].normalized;
        float surfaceRadius = planetGenerator.GetSurfaceRadius(dir);
        bool underwater = surfaceRadius < oceanRadius;

        if ((!underwater && !showLandVents) || (underwater && !showUnderwaterVents))
        {
            return;
        }

        float normalizedStrength = Mathf.InverseLerp(resourceMap.ventStrengthMin, resourceMap.ventStrengthMax, strength);
        float scale = Mathf.Lerp(glowRadiusMin, glowRadiusMax, normalizedStrength);
        float emission = Mathf.Lerp(glowEmissionMin, glowEmissionMax, normalizedStrength);

        Vector3 worldPos = planetGenerator.transform.position + dir * (surfaceRadius + surfaceOffset);

        GameObject marker = new GameObject($"Vent_{cell}");
        marker.transform.SetParent(parent, true);
        marker.transform.position = worldPos;
        marker.transform.up = dir;
        marker.transform.localScale = Vector3.one * scale;

        MeshFilter filter = marker.AddComponent<MeshFilter>();
        filter.sharedMesh = GetMarkerMesh();

        MeshRenderer renderer = marker.AddComponent<MeshRenderer>();
        if (ventGlowMaterial != null)
        {
            renderer.sharedMaterial = ventGlowMaterial;
        }

        propertyBlock.Clear();
        propertyBlock.SetColor("_EmissionColor", Color.red * emission);
        renderer.SetPropertyBlock(propertyBlock);

        if (underwater)
        {
            AttachSoot(marker.transform, normalizedStrength);
        }
    }

    private void AttachSoot(Transform marker, float normalizedStrength)
    {
        ParticleSystem soot = sootPrefab != null
            ? Instantiate(sootPrefab, marker)
            : CreateRuntimeSoot(marker);

        soot.transform.localPosition = Vector3.zero;
        soot.transform.localRotation = Quaternion.identity;

        ParticleSystem.EmissionModule emission = soot.emission;
        emission.rateOverTime = Mathf.Lerp(3f, 18f, normalizedStrength);

        ParticleSystem.MainModule main = soot.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0.05f, 0.2f, normalizedStrength));

        if (!soot.isPlaying)
        {
            soot.Play(true);
        }
    }

    private ParticleSystem CreateRuntimeSoot(Transform marker)
    {
        GameObject sootObj = new GameObject("Soot");
        sootObj.transform.SetParent(marker, false);

        ParticleSystem ps = sootObj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer psRenderer = sootObj.GetComponent<ParticleSystemRenderer>();
        psRenderer.renderMode = ParticleSystemRenderMode.Billboard;

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.05f, 0.05f, 0.05f, 0.55f), new Color(0.15f, 0.15f, 0.15f, 0.35f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 8f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = 0.03f;
        shape.angle = 20f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fadeGradient = new Gradient();
        fadeGradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.08f, 0.08f, 0.08f), 0f),
                new GradientColorKey(new Color(0.18f, 0.18f, 0.18f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.55f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = fadeGradient;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.8f),
            new Keyframe(1f, 1.35f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.08f;
        noise.frequency = 0.35f;

        return ps;
    }

    private static Mesh GetMarkerMesh()
    {
        if (markerMesh != null)
        {
            return markerMesh;
        }

        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        MeshFilter source = primitive.GetComponent<MeshFilter>();
        markerMesh = source != null ? source.sharedMesh : null;
        Object.DestroyImmediate(primitive);
        return markerMesh;
    }
}
