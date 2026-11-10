using System;
using UnityEngine;

/// <summary>Efficient Geodesic-only seabed tint sampled onto the completed visible terrain mesh.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanSedimentVisual : MonoBehaviour
{
    private const double TintFloorFraction = 0.01d;

    [SerializeField] private Color sulfurTint = new Color(0.88f, 0.72f, 0.12f, 1f);
    [SerializeField] private Color oxidizedIronTint = new Color(0.62f, 0.20f, 0.06f, 1f);
    [SerializeField] private Color ironSulphideTint = new Color(0.035f, 0.04f, 0.045f, 1f);
    [SerializeField, Min(1e-8f), Tooltip("Minimum full-tint reference inventory. Each deposit uses its own peak column inventory when higher.")]
    private float inventoryAtFullTint = 5f;
    [SerializeField, Min(0.1f)] private float refreshIntervalSeconds = 1f;

    private GeodesicOceanSedimentField sediments;
    private Mesh mesh;
    private IcosphereDirectionMapping mapping;
    private bool[] oceanMask;
    private float[] surfaceRadii;
    private float seaLevelRadius;
    private Color[] baseColours;
    private Color[] workingColours;
    private Vector3[] tintWeightsByCell;
    private float nextRefresh;
    private ulong lastAppliedRevision;

    public bool IsInitialized => mesh != null && workingColours != null && sediments != null && sediments.IsInitialized;
    public ulong LastAppliedRevision => lastAppliedRevision;
    public ulong FullVisualRefreshCount { get; private set; }
    public double FeSTintFloor { get; private set; }
    public float RefreshIntervalSeconds => Mathf.Max(0.1f, refreshIntervalSeconds);

    public void Initialize(PlanetGenerator owner, GeodesicOceanSedimentField field, Mesh terrainMesh,
        IcosphereDirectionMapping terrainMapping, bool[] underwaterCells, float[] terrainRadii)
    {
        ClearVisual();
        if (owner == null || owner.CurrentGridType != PlanetGridType.GeodesicIcosphere) return;
        if (field == null || !field.IsInitialized || terrainMesh == null || !terrainMesh.isReadable ||
            terrainMapping?.SampleCount != terrainMesh.vertexCount || terrainRadii?.Length != terrainMesh.vertexCount ||
            underwaterCells?.Length != field.CellCount)
        {
            Debug.LogWarning("[GeodesicSedimentVisual] Missing sediment field or compatible terrain data.", this);
            return;
        }

        Shader shader = owner.GetComponent<MeshRenderer>()?.sharedMaterial?.shader;
        Color[] colours = terrainMesh.colors;
        if (shader == null || !shader.isSupported || shader.name != "SimulaVit/GeodesicVertexColorURP" ||
            colours.Length != terrainMesh.vertexCount)
        {
            Debug.LogWarning("[GeodesicSedimentVisual] Seabed tint requires the geodesic vertex-colour terrain material and base colours.", this);
            return;
        }

        sediments = field;
        mesh = terrainMesh;
        mapping = terrainMapping;
        oceanMask = underwaterCells;
        surfaceRadii = terrainRadii;
        seaLevelRadius = owner.GeodesicSeaLevelRadius;
        baseColours = colours;
        workingColours = new Color[baseColours.Length];
        Refresh();
        nextRefresh = Time.unscaledTime + RefreshIntervalSeconds;
        enabled = true;
        Debug.Log($"[GeodesicSedimentVisual] initialized vertices={workingColours.Length}, mapping=completed terrain, minimumFullTintInventory={inventoryAtFullTint:G6}, tintFloorFraction={TintFloorFraction:G6}", this);
    }

    private void Update()
    {
        if (sediments == null || !sediments.IsInitialized || sediments.VisualRevision == lastAppliedRevision) return;
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshIntervalSeconds;
        Refresh();
    }

    private void Refresh()
    {
        if (!IsInitialized || mapping == null || mapping.SampleCount != workingColours.Length) return;
        UpdateColumnTintWeights();
        Array.Copy(baseColours, workingColours, baseColours.Length);
        for (int vertex = 0; vertex < workingColours.Length; vertex++)
        {
            if (surfaceRadii != null && surfaceRadii[vertex] >= seaLevelRadius) continue;
            int cell = mapping.Samples[vertex].NearestCell;
            if (cell < 0 || cell >= tintWeightsByCell.Length) continue;
            Vector3 weights = tintWeightsByCell[cell];
            if (weights.x > 0f || weights.y > 0f || weights.z > 0f)
                workingColours[vertex] = BlendSedimentTints(baseColours[vertex], weights, sulfurTint, oxidizedIronTint, ironSulphideTint);
        }
        mesh.colors = workingColours;
        lastAppliedRevision = sediments.VisualRevision;
        FullVisualRefreshCount++;
    }

    private void UpdateColumnTintWeights()
    {
        int count = sediments.CellCount;
        tintWeightsByCell ??= new Vector3[count];
        double s0Reference = Math.Max(1e-8d, inventoryAtFullTint);
        double fe3Reference = s0Reference, feSReference = s0Reference;
        for (int cell = 0; cell < count; cell++)
        {
            if (!oceanMask[cell]) continue;
            s0Reference = Math.Max(s0Reference, sediments.GetElementalSulfurInventory(cell));
            fe3Reference = Math.Max(fe3Reference, sediments.GetOxidizedIronInventory(cell));
            feSReference = Math.Max(feSReference, sediments.GetIronSulphideInventory(cell));
        }
        FeSTintFloor = feSReference * TintFloorFraction;

        // Scale each deposit independently so abundant FeS does not hide the other products.
        for (int cell = 0; cell < count; cell++)
        {
            if (!oceanMask[cell]) continue;
            tintWeightsByCell[cell] = new Vector3(
                GetTintWeight(sediments.GetElementalSulfurInventory(cell), s0Reference),
                GetTintWeight(sediments.GetOxidizedIronInventory(cell), fe3Reference),
                GetTintWeight(sediments.GetIronSulphideInventory(cell), feSReference));
        }
    }

    private static float GetTintWeight(double inventory, double reference)
    {
        double floor = reference * TintFloorFraction;
        return Mathf.Sqrt(Mathf.Clamp01((float)((inventory - floor) / (reference - floor))));
    }

    public static Color BlendSediments(Color baseColour, double s0, double oxidizedIron, double feS, float fullTint, Color s0Tint, Color rustTint, Color feSTint)
    {
        float scale = Mathf.Max(1e-8f, fullTint);
        var weights = new Vector3(Mathf.Clamp01((float)(s0 / scale)), Mathf.Clamp01((float)(oxidizedIron / scale)), Mathf.Clamp01((float)(feS / scale)));
        return BlendSedimentTints(baseColour, weights, s0Tint, rustTint, feSTint);
    }

    private static Color BlendSedimentTints(Color baseColour, Vector3 weights, Color s0Tint, Color rustTint, Color feSTint)
    {
        Color result = Color.Lerp(baseColour, s0Tint, weights.x);
        result = Color.Lerp(result, rustTint, weights.y);
        return Color.Lerp(result, feSTint, weights.z);
    }

    public void ClearVisual()
    {
        if (mesh != null && baseColours != null && baseColours.Length == mesh.vertexCount) mesh.colors = baseColours;
        sediments = null; mesh = null; mapping = null; oceanMask = null; surfaceRadii = null;
        baseColours = null; workingColours = null; tintWeightsByCell = null;
        FeSTintFloor = 0d;
        nextRefresh = 0f; lastAppliedRevision = 0UL; FullVisualRefreshCount = 0UL; enabled = false;
    }
    private void OnDestroy() => ClearVisual();
}
