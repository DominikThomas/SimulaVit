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
    private int boundMeshInstanceId;
    private int boundVertexCount;
    private Vector3[] meshVertices;
    private int[] vertexResourceCells;
    private Color[] meshVertexColors;
    private bool legacySurfaceBindingSuspended;
    private bool appliedAfterCurrentMeshBinding;

    public int BoundMeshInstanceId => boundMeshInstanceId;
    public int BoundVertexCount => boundVertexCount;
    public bool HasAppliedAfterCurrentMeshBinding => appliedAfterCurrentMeshBinding;
    public bool IsLegacySurfaceBindingSuspended => legacySurfaceBindingSuspended;

    private double lastSimulationTime = double.NegativeInfinity;
    private double nextUpdateSimulationTime;

    private static readonly int IceColorId = Shader.PropertyToID("_IceColor");
    private static readonly int IceStrengthId = Shader.PropertyToID("_IceStrength");
    private static readonly int ForceVertexIcePreviewId = Shader.PropertyToID("_ForceVertexIcePreview");

    private void Awake()
    {
        ResolveReferences();
        SubscribeResourceReadyEvent();
        TryBindPlanetVisuals();
        EnsureMeshBuffers();
        PushStaticMaterialParams();
        RefreshNow();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeResourceReadyEvent();
        TryBindPlanetVisuals();
        EnsureMeshBuffers();
        PushStaticMaterialParams();
        RefreshNow();
    }

    private void OnDisable()
    {
        UnsubscribeResourceReadyEvent();
    }

    private void OnDestroy()
    {
        UnsubscribeResourceReadyEvent();
    }

    private void Update()
    {
        if (!enableTemperatureLandIce || planetResourceMap == null || planetGenerator == null || legacySurfaceBindingSuspended || planetGenerator.CurrentGridType != PlanetGridType.LegacyCubeSphere)
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
        RebindAndRefreshLegacySurface("RefreshNow");
    }

    public void InvalidateMeshBinding()
    {
        planetMesh = null;
        boundMeshInstanceId = 0;
        boundVertexCount = 0;
        meshVertices = null;
        vertexResourceCells = null;
        meshVertexColors = null;
        appliedAfterCurrentMeshBinding = false;
    }

    public void ClearForGeodesicMode()
    {
        legacySurfaceBindingSuspended = true;
        InvalidateMeshBinding();
    }

    public void RebindAndRefreshLegacySurface(string reason)
    {
        legacySurfaceBindingSuspended = false;
        ResolveReferences();
        SubscribeResourceReadyEvent();
        if (planetGenerator == null || planetGenerator.CurrentGridType != PlanetGridType.LegacyCubeSphere)
        {
            ClearForGeodesicMode();
            return;
        }

        TryBindPlanetVisuals();
        bool rebound = EnsureMeshBuffers();
        PushStaticMaterialParams();

        if (planetMesh == null || meshVertices == null || meshVertexColors == null)
        {
            return;
        }

        if (planetResourceMap != null && enableTemperatureLandIce)
        {
            UpdateLandIceVertexColors();
        }
        else
        {
            ApplyNeutralNoIceVertexColors();
        }

        appliedAfterCurrentMeshBinding = true;
        lastSimulationTime = GetSimulationTimeSeconds();
        nextUpdateSimulationTime = lastSimulationTime + iceVisualUpdateIntervalSeconds;
        LogLegacyIceDiagnostics(reason, rebound);
    }


    private void SubscribeResourceReadyEvent()
    {
        if (planetResourceMap == null)
        {
            return;
        }

        planetResourceMap.ResourcesReadyForVisualization -= HandleResourcesReadyForVisualization;
        planetResourceMap.ResourcesReadyForVisualization += HandleResourcesReadyForVisualization;
    }

    private void UnsubscribeResourceReadyEvent()
    {
        if (planetResourceMap != null)
        {
            planetResourceMap.ResourcesReadyForVisualization -= HandleResourcesReadyForVisualization;
        }
    }

    private void HandleResourcesReadyForVisualization(PlanetResourceMap source, string reason)
    {
        if (source != planetResourceMap)
        {
            return;
        }

        vertexResourceCells = null;
        RebindAndRefreshLegacySurface($"resources ready - {reason}");
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

    private bool EnsureMeshBuffers()
    {
        if (meshFilter == null)
        {
            InvalidateMeshBinding();
            return false;
        }

        Mesh currentMesh = meshFilter.sharedMesh;
        if (currentMesh == null)
        {
            InvalidateMeshBinding();
            return false;
        }

        int currentInstanceId = currentMesh.GetInstanceID();
        int currentVertexCount = currentMesh.vertexCount;
        bool meshChanged = planetMesh != currentMesh || boundMeshInstanceId != currentInstanceId || boundVertexCount != currentVertexCount;

        if (meshChanged)
        {
            planetMesh = currentMesh;
            boundMeshInstanceId = currentInstanceId;
            boundVertexCount = currentVertexCount;
            meshVertices = null;
            vertexResourceCells = null;
            meshVertexColors = null;
            appliedAfterCurrentMeshBinding = false;
        }

        if (meshVertices == null || meshVertices.Length != currentVertexCount)
        {
            meshVertices = currentMesh.vertices;
        }

        if (vertexResourceCells == null || vertexResourceCells.Length != currentVertexCount)
        {
            vertexResourceCells = new int[currentVertexCount];
            for (int i = 0; i < currentVertexCount; i++)
            {
                vertexResourceCells[i] = planetResourceMap != null ? planetResourceMap.GetCellIndexFromDirection(meshVertices[i].normalized) : -1;
            }
        }

        if (meshVertexColors == null || meshVertexColors.Length != currentVertexCount)
        {
            meshVertexColors = new Color[currentVertexCount];
            ApplyNeutralNoIceBaseline(meshVertexColors);
        }

        return meshChanged;
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

    private static void ApplyNeutralNoIceBaseline(Color[] colors)
    {
        if (colors == null) return;
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = new Color(0f, 0f, 0f, 0f);
        }
    }

    private void ApplyNeutralNoIceVertexColors()
    {
        if (planetMesh == null || meshVertexColors == null) return;
        ApplyNeutralNoIceBaseline(meshVertexColors);
        planetMesh.colors = meshVertexColors;
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
                int cell = vertexResourceCells != null && i < vertexResourceCells.Length ? vertexResourceCells[i] : planetResourceMap.GetCellIndexFromDirection(dir);
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

    private void LogLegacyIceDiagnostics(string reason, bool rebound)
    {
        if (planetMesh == null)
        {
            Debug.LogWarning($"[LegacyIceVisualDiagnostics] reason={reason}, no bound legacy terrain mesh.", this);
            return;
        }

        Color[] colors = planetMesh.colors;
        int colorCount = colors != null ? colors.Length : 0;
        float minA = float.PositiveInfinity, maxA = float.NegativeInfinity, sumA = 0f;
        for (int i = 0; i < colorCount; i++)
        {
            float a = colors[i].a;
            minA = Mathf.Min(minA, a);
            maxA = Mathf.Max(maxA, a);
            sumA += a;
        }

        if (colorCount == 0)
        {
            minA = maxA = 0f;
        }

        Debug.Log($"[LegacyIceVisualDiagnostics] reason={reason}, rebound={rebound}, enabled={enabled}, suspended={legacySurfaceBindingSuspended}, meshInstanceId={boundMeshInstanceId}, vertexCount={boundVertexCount}, resourceCellMappingCount={(vertexResourceCells != null ? vertexResourceCells.Length : 0)}, colorCount={colorCount}, alphaMinMaxMean={minA:F4}/{maxA:F4}/{(colorCount > 0 ? sumA / colorCount : 0f):F4}, appliedAfterCurrentMeshBinding={appliedAfterCurrentMeshBinding}", this);
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
