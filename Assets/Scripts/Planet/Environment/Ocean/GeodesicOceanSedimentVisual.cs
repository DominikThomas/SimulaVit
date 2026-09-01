using UnityEngine;

/// <summary>Efficient Geodesic-only seabed tint sampled onto the completed visible terrain mesh.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanSedimentVisual : MonoBehaviour
{
    [SerializeField] private Color sulfurTint = new Color(0.88f, 0.72f, 0.12f, 1f);
    [SerializeField] private Color oxidizedIronTint = new Color(0.62f, 0.20f, 0.06f, 1f);
    [SerializeField] private Color ironSulphideTint = new Color(0.035f, 0.04f, 0.045f, 1f);
    [SerializeField, Min(1e-8f)] private float inventoryAtFullTint = 5f;
    [SerializeField, Min(0.1f)] private float refreshIntervalSeconds = 1f;

    private PlanetGenerator generator;
    private GeodesicOceanSedimentField sediments;
    private Mesh mesh;
    private IcosphereDirectionMapping mapping;
    private bool[] oceanMask;
    private Color[] baseColours;
    private Color[] workingColours;
    private float nextRefresh;
    private ulong lastAppliedRevision;

    public ulong LastAppliedRevision => lastAppliedRevision;
    public ulong FullVisualRefreshCount { get; private set; }
    public float RefreshIntervalSeconds => Mathf.Max(0.1f, refreshIntervalSeconds);

    public void Initialize(PlanetGenerator owner, GeodesicOceanSedimentField field, Mesh terrainMesh, IcosphereDirectionMapping terrainMapping, bool[] underwaterCells)
    {
        ClearVisual();
        generator = owner; sediments = field; mesh = terrainMesh; mapping = terrainMapping; oceanMask = underwaterCells;
        if (generator == null || generator.CurrentGridType != PlanetGridType.GeodesicIcosphere || sediments == null || !sediments.IsInitialized || mesh == null || mapping == null) return;
        baseColours = mesh.colors;
        if (baseColours == null || baseColours.Length != mesh.vertexCount) return;
        workingColours = new Color[baseColours.Length];
        System.Array.Copy(baseColours, workingColours, baseColours.Length);
        Refresh();
        nextRefresh = Time.unscaledTime + RefreshIntervalSeconds;
        enabled = true;
        Debug.Log($"[GeodesicSedimentVisual] vertices={workingColours.Length}, colourBytes~={(long)workingColours.Length * 16L}, mapping=completed visible terrain mapping/anchor authority, threshold={inventoryAtFullTint:G6}", this);
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
        if (workingColours == null || mapping == null || mapping.Samples.Length != workingColours.Length || !sediments.IsInitialized) return;
        for (int vertex = 0; vertex < workingColours.Length; vertex++)
        {
            int cell = mapping.Samples[vertex].NearestCell;
            Color colour = baseColours[vertex];
            if (cell >= 0 && cell < sediments.CellCount && oceanMask != null && cell < oceanMask.Length && oceanMask[cell])
                colour = BlendSediments(colour, sediments.GetElementalSulfurInventory(cell), sediments.GetOxidizedIronInventory(cell), sediments.GetIronSulphideInventory(cell), inventoryAtFullTint, sulfurTint, oxidizedIronTint, ironSulphideTint);
            workingColours[vertex] = colour;
        }
        mesh.colors = workingColours;
        lastAppliedRevision = sediments.VisualRevision;
        FullVisualRefreshCount++;
    }

    public static Color BlendSediments(Color baseColour, double s0, double oxidizedIron, double feS, float fullTint, Color s0Tint, Color rustTint, Color feSTint)
    {
        float scale = Mathf.Max(1e-8f, fullTint);
        float s = Mathf.Clamp01((float)(s0 / scale));
        float rust = Mathf.Clamp01((float)(oxidizedIron / scale));
        float dark = Mathf.Clamp01((float)(feS / scale));
        Color result = Color.Lerp(baseColour, s0Tint, s);
        result = Color.Lerp(result, rustTint, rust);
        return Color.Lerp(result, feSTint, dark);
    }

    public void ClearVisual()
    {
        if (mesh != null && baseColours != null && baseColours.Length == mesh.vertexCount) mesh.colors = baseColours;
        generator = null; sediments = null; mesh = null; mapping = null; oceanMask = null; baseColours = null; workingColours = null;
        nextRefresh = 0f; lastAppliedRevision = 0UL; FullVisualRefreshCount = 0UL; enabled = false;
    }
    private void OnDestroy() => ClearVisual();
}
