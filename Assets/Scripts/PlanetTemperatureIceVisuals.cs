using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class PlanetTemperatureIceVisuals : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlanetGenerator planetGenerator;
    [SerializeField] private PlanetResourceMap planetResourceMap;
    [SerializeField] private ReplicatorManager replicatorManager;

    [Header("Land Ice Visuals")]
    public bool enableTemperatureLandIce = true;
    [Min(50f)] public float landIceThresholdKelvin = 273.15f;
    [Min(0.01f)] public float landIceFadeKelvin = 3f;
    [Min(0.05f)] public float iceVisualUpdateIntervalSeconds = 1.5f;
    public Color landIceColor = new Color(0.88f, 0.93f, 0.98f, 1f);
    [Range(0f, 2f)] public float landIceStrength = 1f;

    [Header("Land Ice Debug")]
    public bool forceVertexIcePreview = false;

    [Header("Sea Ice Scaffold (visual only / future)")]
    public bool enableTemperatureSeaIce = false;
    [Min(50f)] public float seaIceThresholdKelvin = 269.15f;
    [Min(0.01f)] public float seaIceFadeKelvin = 3f;
    [Range(1.0001f, 1.05f)] public float seaIceRadiusMultiplier = 1.002f;

    private Material planetMaterial;
    private MeshFilter meshFilter;
    private Mesh planetMesh;
    private Vector3[] meshVertices;
    private Color[] meshVertexColors;

    private double lastSimulationTime = double.NegativeInfinity;
    private double nextUpdateSimulationTime;

    private static readonly int IceColorId = Shader.PropertyToID("_IceColor");
    private static readonly int IceStrengthId = Shader.PropertyToID("_IceStrength");
    private static readonly int ForceVertexIcePreviewId = Shader.PropertyToID("_ForceVertexIcePreview");

    private void Awake()
    {
        ResolveReferences();
        TryBindPlanetVisuals();
        EnsureMeshBuffers();
        PushStaticMaterialParams();
        RefreshNow();
    }

    private void OnEnable()
    {
        ResolveReferences();
        TryBindPlanetVisuals();
        EnsureMeshBuffers();
        PushStaticMaterialParams();
        RefreshNow();
    }

    private void Update()
    {
        if (!enableTemperatureLandIce || planetResourceMap == null || planetGenerator == null)
        {
            return;
        }

        double simTime = GetSimulationTimeSeconds();
        if (!double.IsFinite(simTime))
        {
            return;
        }

        if (simTime <= lastSimulationTime)
        {
            return;
        }

        if (simTime < nextUpdateSimulationTime)
        {
            return;
        }

        RefreshNow();
    }

    private void OnValidate()
    {
        landIceFadeKelvin = Mathf.Max(0.01f, landIceFadeKelvin);
        seaIceFadeKelvin = Mathf.Max(0.01f, seaIceFadeKelvin);
        iceVisualUpdateIntervalSeconds = Mathf.Max(0.05f, iceVisualUpdateIntervalSeconds);

        if (!Application.isPlaying)
        {
            return;
        }

        TryBindPlanetVisuals();
        EnsureMeshBuffers();
        PushStaticMaterialParams();
    }

    [ContextMenu("Refresh Vertex Ice Now")]
    public void RefreshNow()
    {
        ResolveReferences();
        if (planetResourceMap == null || planetGenerator == null)
        {
            return;
        }

        TryBindPlanetVisuals();
        EnsureMeshBuffers();
        PushStaticMaterialParams();

        UpdateLandIceVertexColors();

        lastSimulationTime = GetSimulationTimeSeconds();
        nextUpdateSimulationTime = lastSimulationTime + iceVisualUpdateIntervalSeconds;
    }

    private void ResolveReferences()
    {
        if (planetGenerator == null)
        {
            planetGenerator = GetComponent<PlanetGenerator>();
        }

        if (planetResourceMap == null)
        {
            planetResourceMap = GetComponent<PlanetResourceMap>();
            if (planetResourceMap == null)
            {
                planetResourceMap = FindFirstObjectByType<PlanetResourceMap>();
            }
        }

        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }
    }

    private void TryBindPlanetVisuals()
    {
        if (planetGenerator == null)
        {
            return;
        }

        MeshRenderer renderer = planetGenerator.GetComponent<MeshRenderer>();
        planetMaterial = renderer != null ? renderer.sharedMaterial : null;

        meshFilter = planetGenerator.GetComponent<MeshFilter>();
    }

    private void EnsureMeshBuffers()
    {
        if (meshFilter == null)
        {
            return;
        }

        if (planetMesh == null)
        {
            planetMesh = meshFilter.mesh;
        }

        if (planetMesh == null)
        {
            return;
        }

        if (meshVertices == null || meshVertices.Length != planetMesh.vertexCount)
        {
            meshVertices = planetMesh.vertices;
        }

        if (meshVertexColors == null || meshVertexColors.Length != planetMesh.vertexCount)
        {
            meshVertexColors = new Color[planetMesh.vertexCount];
            for (int i = 0; i < meshVertexColors.Length; i++)
            {
                meshVertexColors[i] = new Color(0f, 0f, 0f, 0f);
            }
        }

    }

    private void PushStaticMaterialParams()
    {
        if (planetMaterial == null)
        {
            TryBindPlanetVisuals();
            if (planetMaterial == null)
            {
                return;
            }
        }

        planetMaterial.SetColor(IceColorId, landIceColor);
        planetMaterial.SetFloat(IceStrengthId, landIceStrength);
        planetMaterial.SetFloat(ForceVertexIcePreviewId, forceVertexIcePreview ? 1f : 0f);
    }

    private void UpdateLandIceVertexColors()
    {
        if (planetMesh == null || meshVertices == null || meshVertexColors == null)
        {
            return;
        }

        float threshold = landIceThresholdKelvin;
        float fade = Mathf.Max(0.01f, landIceFadeKelvin);

        for (int i = 0; i < meshVertices.Length; i++)
        {
            Vector3 dir = meshVertices[i].normalized;
            float iceValue = 0f;

            if (!planetGenerator.IsOceanAtDirection(dir))
            {
                int cell = planetResourceMap.GetCellIndexFromDirection(dir);
                float tempKelvin = planetResourceMap.GetTemperature(dir, cell);
                iceValue = Mathf.Clamp01(Mathf.InverseLerp(threshold + fade, threshold - fade, tempKelvin));
            }

            Color c = meshVertexColors[i];
            c.a = iceValue;
            c.r = iceValue;
            meshVertexColors[i] = c;
        }

        planetMesh.colors = meshVertexColors;
    }

    private double GetSimulationTimeSeconds()
    {
        if (replicatorManager == null)
        {
            return Time.timeSinceLevelLoad;
        }

        return replicatorManager.SimulationTimeSeconds;
    }
}
