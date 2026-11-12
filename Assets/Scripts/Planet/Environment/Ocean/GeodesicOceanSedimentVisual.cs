using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Samples accumulated ocean sediment onto the live geodesic terrain.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanSedimentVisual : MonoBehaviour
{
    private const int TintUvChannel = 2;
    // Fixed per-species display scales for gradually deposited inventories, not ocean-wide peak normalization.
    private const double TintFloorFraction = 0.002d;
    private const float DefaultSulfurReference = 50f;
    private const float DefaultOxidizedIronReference = 50f;
    private const float DefaultIronSulphideReference = 500f;
    private static readonly int SeaLevelId = Shader.PropertyToID("_SedimentSeaLevelRadius");

    [SerializeField] private Color sulfurTint = new Color(0.88f, 0.72f, 0.12f, 1f);
    [SerializeField] private Color oxidizedIronTint = new Color(0.62f, 0.20f, 0.06f, 1f);
    [SerializeField] private Color ironSulphideTint = new Color(0.035f, 0.04f, 0.045f, 1f);
    [SerializeField, Min(1e-8f), Tooltip("Deposited S0 inventory per column at full tint (concentration*km3). Tint starts above 0.2% of this value.")]
    private float sulfurReference = DefaultSulfurReference;
    [SerializeField, Min(1e-8f), Tooltip("Deposited Fe(III) inventory per column at full tint (concentration*km3). Tint starts above 0.2% of this value.")]
    private float oxidizedIronReference = DefaultOxidizedIronReference;
    [SerializeField, Min(1e-8f), Tooltip("Deposited FeS inventory per column at full tint (concentration*km3). Tint starts above 0.2% of this value.")]
    private float ironSulphideReference = DefaultIronSulphideReference;
    [SerializeField, HideInInspector] private int tintCalibrationVersion;
    [SerializeField, Min(0.1f)] private float refreshIntervalSeconds = 1f;

    private GeodesicOceanSedimentField sediments;
    private MeshFilter terrainFilter;
    private MeshRenderer terrainRenderer;
    private MeshRenderer oceanRenderer;
    private MaterialPropertyBlock terrainProperties;
    private Mesh mesh;
    private GeodesicGridTopology topology;
    private IcosphereDirectionMapping baseMapping;
    private bool[] oceanMask;
    private float seaLevelRadius;
    private readonly List<(int vertex, int a, int b, int c, Vector3 weights)> submergedVertices = new();
    private List<Vector4> tintStream;
    private Vector3[] tintWeightsByCell;
    private int[] tintedVerticesByCell;
    private RenderedColumnTint[] renderedTintsByCell;
    private float nextRefresh;
    private ulong lastAppliedRevision;
    private bool refreshRequested;
    private bool previousOceanRenderingOff;

    public bool IsInitialized => mesh != null && tintStream != null && sediments != null && sediments.IsInitialized;
    public bool TintUploadedToLiveMesh => IsInitialized && terrainFilter != null && terrainFilter.sharedMesh == mesh &&
        mesh.HasVertexAttribute(VertexAttribute.TexCoord2);
    public bool CanShowSeabed => oceanRenderer != null && (IsInitialized || SeabedViewEnabled);
    public bool SeabedViewEnabled { get; private set; }
    public ulong LastAppliedRevision => lastAppliedRevision;
    public ulong FullVisualRefreshCount { get; private set; }
    public double SulfurFullTintReference { get; private set; }
    public double OxidizedIronFullTintReference { get; private set; }
    public double IronSulphideFullTintReference { get; private set; }
    public double SulfurTintFloor => SulfurFullTintReference * TintFloorFraction;
    public double OxidizedIronTintFloor => OxidizedIronFullTintReference * TintFloorFraction;
    public double FeSTintFloor => IronSulphideFullTintReference * TintFloorFraction;
    public float RefreshIntervalSeconds => Mathf.Max(0.1f, refreshIntervalSeconds);

    /// <summary>Statistics of the last uploaded tint at seabed vertices whose nearest simulation cell is this column.</summary>
    public struct RenderedColumnTint
    {
        public int VertexCount;
        // Contributions sum to coverage, matching the premultiplied colour sent to the shader.
        public Vector3 MeanMineralCoverage;
        public float MinimumCoverage, MeanCoverage, MaximumCoverage;
    }

    public bool TerrainTintRenderingActive
    {
        get
        {
            if (!TintUploadedToLiveMesh || terrainRenderer == null || !terrainRenderer.enabled ||
                terrainRenderer.forceRenderingOff || !terrainRenderer.gameObject.activeInHierarchy) return false;
            Material material = terrainRenderer.sharedMaterial;
            if (material == null || !material.HasProperty(SeaLevelId) || !material.shader.isSupported) return false;
            terrainProperties ??= new MaterialPropertyBlock();
            terrainRenderer.GetPropertyBlock(terrainProperties);
            return terrainProperties.GetFloat(SeaLevelId) > 0f;
        }
    }

    public void Initialize(PlanetGenerator owner, GeodesicOceanSedimentField field,
        IcosphereDirectionMapping terrainMapping, bool[] underwaterCells, MeshRenderer waterRenderer)
    {
        UpgradeLegacyTintReferences();
        ClearVisual();
        if (owner == null || owner.CurrentGridType != PlanetGridType.GeodesicIcosphere) return;
        if (field == null || !field.IsInitialized || owner.GeodesicTopology?.CellCount != field.CellCount ||
            underwaterCells?.Length != field.CellCount || terrainMapping == null)
        {
            Debug.LogWarning("[GeodesicSedimentVisual] Missing sediment field or compatible terrain mapping.", this);
            return;
        }

        terrainFilter = owner.GetComponent<MeshFilter>();
        terrainRenderer = owner.GetComponent<MeshRenderer>();
        oceanRenderer = waterRenderer;
        sediments = field;
        topology = owner.GeodesicTopology;
        baseMapping = terrainMapping;
        oceanMask = underwaterCells;
        seaLevelRadius = owner.GeodesicSeaLevelRadius;
        BindTerrainMesh(terrainFilter != null ? terrainFilter.sharedMesh : null);
        enabled = true;
    }

    private void BindTerrainMesh(Mesh liveMesh)
    {
        ClearTerrainTint();
        mesh = liveMesh;
        Material material = terrainRenderer != null ? terrainRenderer.sharedMaterial : null;
        if (mesh == null || !mesh.isReadable || material == null || !material.shader.isSupported ||
            !material.HasProperty(SeaLevelId) || !mesh.HasVertexAttribute(VertexAttribute.Color))
        {
            Debug.LogWarning("[GeodesicSedimentVisual] Seabed tint requires readable live terrain and the geodesic vertex-colour material.", this);
            return;
        }

        Vector3[] vertices = mesh.vertices;
        // Clipped lake surfaces keep the original vertex prefix and may leave some vertices unused.
        bool[] referenced = new bool[vertices.Length];
        foreach (int vertex in mesh.triangles) referenced[vertex] = true;
        float seaLevelSquared = seaLevelRadius * seaLevelRadius;
        int nearest = 0;
        for (int vertex = 0; vertex < vertices.Length; vertex++)
        {
            if (!referenced[vertex] || vertices[vertex].sqrMagnitude >= seaLevelSquared) continue;
            Vector3 direction = vertices[vertex].normalized;
            int seed = vertex < baseMapping.SampleCount ? baseMapping.Samples[vertex].NearestCell : nearest;
            nearest = GeodesicOceanConnectivity.FindNearestCell(topology, direction, seed);
            if (!oceanMask[nearest]) continue;
            GetTriangleWeights(direction, nearest, out int b, out int c, out Vector3 weights);
            submergedVertices.Add((vertex, nearest, b, c, weights));
        }

        tintStream = new List<Vector4>(vertices.Length);
        for (int vertex = 0; vertex < vertices.Length; vertex++) tintStream.Add(Vector4.zero);
        Refresh();
        SetTerrainTintRadius(seaLevelRadius);
        nextRefresh = Time.unscaledTime + RefreshIntervalSeconds;
        Debug.Log($"[GeodesicSedimentVisual] bound live mesh={mesh.name} vertices={vertices.Length} seabedSamples={submergedVertices.Count} inventoryFloor/fullTint S0={SulfurTintFloor:G6}/{SulfurFullTintReference:G6} Fe3={OxidizedIronTintFloor:G6}/{OxidizedIronFullTintReference:G6} FeS={FeSTintFloor:G6}/{IronSulphideFullTintReference:G6}", this);
    }

    // Project the render direction onto its simulation triangle for a continuous tint across cell boundaries.
    private void GetTriangleWeights(Vector3 direction, int nearest, out int b, out int c, out Vector3 weights)
    {
        Vector3 aDirection = topology.CellDirections[nearest];
        Vector3 offset = direction - aDirection;
        b = c = nearest;
        weights = Vector3.right;
        if (offset.sqrMagnitude < 1e-12f) return;

        int count = topology.NeighborCounts[nearest];
        for (int slot = 0; slot < count; slot++)
        {
            int next = topology.Neighbors6[nearest * 6 + slot];
            int afterNext = topology.Neighbors6[nearest * 6 + (slot + 1) % count];
            Vector3 ab = topology.CellDirections[next] - aDirection;
            Vector3 ac = topology.CellDirections[afterNext] - aDirection;
            Vector3 normal = Vector3.Cross(ab, ac);
            float determinant = Vector3.Dot(aDirection, normal);
            if (determinant == 0f) continue;
            float wb = Vector3.Dot(aDirection, Vector3.Cross(offset, ac)) / determinant;
            float wc = Vector3.Dot(aDirection, Vector3.Cross(ab, offset)) / determinant;
            float wa = Vector3.Dot(direction, normal) / determinant - wb - wc;
            if (wa < -1e-5f || wb < -1e-5f || wc < -1e-5f) continue;
            b = next;
            c = afterNext;
            weights = new Vector3(Mathf.Max(0f, wa), Mathf.Max(0f, wb), Mathf.Max(0f, wc));
            weights /= weights.x + weights.y + weights.z;
            return;
        }
    }

    private void UpgradeLegacyTintReferences()
    {
        if (tintCalibrationVersion >= 1) return;
        // Keep serialized field names and custom references. Migrate only the previous stock calibration.
        if (sulfurReference == 10000f) sulfurReference = DefaultSulfurReference;
        if (oxidizedIronReference == 10000f) oxidizedIronReference = DefaultOxidizedIronReference;
        if (ironSulphideReference == 1000000f) ironSulphideReference = DefaultIronSulphideReference;
        tintCalibrationVersion = 1;
    }

    private void OnValidate()
    {
        UpgradeLegacyTintReferences();
        refreshRequested = true;
    }

    private void Update()
    {
        if (sediments == null || !sediments.IsInitialized) return;
        Mesh liveMesh = terrainFilter != null ? terrainFilter.sharedMesh : null;
        if (liveMesh != mesh || (IsInitialized && mesh.vertexCount != tintStream.Count))
        {
            BindTerrainMesh(liveMesh);
            return;
        }
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshIntervalSeconds;
        if (refreshRequested || sediments.VisualRevision != lastAppliedRevision || !TintUploadedToLiveMesh)
            Refresh();
    }

    private void Refresh()
    {
        if (!IsInitialized) return;
        UpdateColumnTintWeights();
        Array.Clear(tintedVerticesByCell, 0, tintedVerticesByCell.Length);
        Array.Clear(renderedTintsByCell, 0, renderedTintsByCell.Length);
        foreach (var sample in submergedVertices)
        {
            Vector3 a = tintWeightsByCell[sample.a] * sample.weights.x;
            Vector3 b = tintWeightsByCell[sample.b] * sample.weights.y;
            Vector3 c = tintWeightsByCell[sample.c] * sample.weights.z;
            Vector3 mixedWeights = a + b + c;
            Vector4 tint = ComposeTint(mixedWeights, sulfurTint, oxidizedIronTint, ironSulphideTint);
            tintStream[sample.vertex] = tint;
            // Diagnose the actual interpolated upload, including zero-coverage vertices.
            // Source weights alone overstate local coverage and each mineral's share of a mixed deposit.
            ref RenderedColumnTint rendered = ref renderedTintsByCell[sample.a];
            rendered.MinimumCoverage = rendered.VertexCount == 0 ? tint.x : Mathf.Min(rendered.MinimumCoverage, tint.x);
            rendered.MaximumCoverage = Mathf.Max(rendered.MaximumCoverage, tint.x);
            rendered.MeanCoverage += tint.x;
            float sum = mixedWeights.x + mixedWeights.y + mixedWeights.z;
            if (sum > 0f) rendered.MeanMineralCoverage += mixedWeights * (tint.x / sum);
            rendered.VertexCount++;
            if (a != Vector3.zero) tintedVerticesByCell[sample.a]++;
            if (b != Vector3.zero) tintedVerticesByCell[sample.b]++;
            if (c != Vector3.zero) tintedVerticesByCell[sample.c]++;
        }
        for (int cell = 0; cell < renderedTintsByCell.Length; cell++)
        {
            ref RenderedColumnTint rendered = ref renderedTintsByCell[cell];
            if (rendered.VertexCount == 0) continue;
            rendered.MeanCoverage /= rendered.VertexCount;
            rendered.MeanMineralCoverage /= rendered.VertexCount;
        }
        // Keep the source palette intact when another system clones or clips the terrain mesh.
        mesh.SetUVs(TintUvChannel, tintStream);
        lastAppliedRevision = sediments.VisualRevision;
        FullVisualRefreshCount++;
        refreshRequested = false;
    }

    private void UpdateColumnTintWeights()
    {
        int count = sediments.CellCount;
        tintWeightsByCell ??= new Vector3[count];
        tintedVerticesByCell ??= new int[count];
        renderedTintsByCell ??= new RenderedColumnTint[count];
        double s0Reference = TintReference(sulfurReference, DefaultSulfurReference);
        double fe3Reference = TintReference(oxidizedIronReference, DefaultOxidizedIronReference);
        double feSReference = TintReference(ironSulphideReference, DefaultIronSulphideReference);
        SulfurFullTintReference = s0Reference;
        OxidizedIronFullTintReference = fe3Reference;
        IronSulphideFullTintReference = feSReference;
        for (int cell = 0; cell < count; cell++)
        {
            if (!oceanMask[cell]) continue;
            tintWeightsByCell[cell] = new Vector3(
                GetTintWeight(sediments.GetElementalSulfurInventory(cell), s0Reference),
                GetTintWeight(sediments.GetOxidizedIronInventory(cell), fe3Reference),
                GetTintWeight(sediments.GetIronSulphideInventory(cell), feSReference));
        }
    }

    private static double TintReference(float value, float fallback) =>
        float.IsFinite(value) && value > 0f ? value : fallback;

    private static float GetTintWeight(double inventory, double reference)
    {
        double floor = reference * TintFloorFraction;
        return Mathf.Sqrt(Mathf.Clamp01((float)((inventory - floor) / (reference - floor))));
    }

    private static float Coverage(Vector3 weights) => Mathf.Clamp01(1f - (1f - weights.x) * (1f - weights.y) * (1f - weights.z));

    internal static Vector4 ComposeTint(Vector3 weights, Color sulfur, Color rust, Color sulphide)
    {
        float sum = weights.x + weights.y + weights.z;
        if (sum <= 0f) return Vector4.zero;
        float coverage = Coverage(weights);
        Color colour = (sulfur * weights.x + rust * weights.y + sulphide * weights.z) * (coverage / sum);
        // The shader interpolates coverage and premultiplied RGB separately.
        return new Vector4(coverage, colour.r, colour.g, colour.b);
    }

    public bool TryGetColumnTint(int cell, out Vector3 weights, out float coverage, out int affectedVertices)
    {
        weights = Vector3.zero;
        coverage = 0f;
        affectedVertices = 0;
        if (!TintUploadedToLiveMesh || tintWeightsByCell == null || cell < 0 || cell >= tintWeightsByCell.Length) return false;
        weights = tintWeightsByCell[cell];
        coverage = Coverage(weights);
        affectedVertices = tintedVerticesByCell[cell];
        return true;
    }

    public bool TryGetRenderedColumnTint(int cell, out RenderedColumnTint tint)
    {
        tint = default;
        if (!TintUploadedToLiveMesh || renderedTintsByCell == null || cell < 0 || cell >= renderedTintsByCell.Length) return false;
        tint = renderedTintsByCell[cell];
        return tint.VertexCount > 0;
    }

    public void SetSeabedView(bool show)
    {
        if (show == SeabedViewEnabled) return;
        if (show && !CanShowSeabed) return;
        if (oceanRenderer != null)
        {
            if (show) previousOceanRenderingOff = oceanRenderer.forceRenderingOff;
            oceanRenderer.forceRenderingOff = show || previousOceanRenderingOff;
        }
        SeabedViewEnabled = show;
    }

    private void SetTerrainTintRadius(float radius)
    {
        if (terrainRenderer == null) return;
        terrainProperties ??= new MaterialPropertyBlock();
        terrainRenderer.GetPropertyBlock(terrainProperties);
        terrainProperties.SetFloat(SeaLevelId, radius);
        terrainRenderer.SetPropertyBlock(terrainProperties);
    }

    private void ClearTerrainTint()
    {
        if (tintStream != null)
        {
            SetTerrainTintRadius(0f);
            tintStream.Clear();
            if (mesh != null) mesh.SetUVs(TintUvChannel, tintStream);
        }
        mesh = null;
        tintStream = null;
        submergedVertices.Clear();
    }

    public void ClearVisual()
    {
        SetSeabedView(false);
        ClearTerrainTint();
        terrainFilter = null; terrainRenderer = null; oceanRenderer = null;
        sediments = null; topology = null; baseMapping = null; oceanMask = null;
        tintWeightsByCell = null; tintedVerticesByCell = null; renderedTintsByCell = null;
        SulfurFullTintReference = OxidizedIronFullTintReference = IronSulphideFullTintReference = 0d;
        nextRefresh = 0f; lastAppliedRevision = 0UL; FullVisualRefreshCount = 0UL; enabled = false;
    }

    private void OnDisable() => SetSeabedView(false);
    private void OnDestroy() => ClearVisual();
}
