using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public struct GeodesicRiverMouth
{
    public int UpstreamCell, OceanCell;
    public double DrainageArea, AccumulatedRunoff;
    public float Strength;
    public Vector3 LocalCoastPosition;
    public bool HasVisibleCoastPosition;
}

/// <summary>Generation-time drainage and cached surface ribbons. No per-frame simulation.
/// Climate supplies per-cell runoff through UpdateRunoff without changing terrain or receivers.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicRiverSystem : MonoBehaviour
{
    [Header("Discharge (unit runoff per unit land area by default)")]
    [Min(0.01f), Tooltip("Discharge threshold in mean-cell-area equivalents at unit runoff density.")]
    public float riverFlowThreshold = 8f;
    [Min(0.01f)] public float majorRiverThreshold = 64f;
    [Min(0f)] public float uniformRunoffPerArea = 1f;
    [Header("Terrain corridor refinement")]
    [Range(4, 32)] public int refinementSteps = 12;
    [Range(3, 15)] public int refinementLanes = 7;
    [Range(0f, 0.45f)] public float corridorWidthInCellSpacings = 0.35f;
    [Min(0f), Tooltip("Only numerical altitude tolerance, in planet-local units; not a basin breach height.")]
    public float uphillTolerance = 0.000002f;
    [Min(0f), Tooltip("Fill depths above this value are reported as major basin cells. No terrain is changed.")]
    public float majorBasinDepth = 0.01f;
    [Header("Static river ribbons")]
    public bool showRivers = true;
    [Min(0.00001f), Tooltip("Width as a fraction of planet radius at the visibility threshold.")]
    public float widthAtThreshold = 0.0006f;
    [Min(1f)] public float maximumWidthMultiplier = 5f;
    [Min(0.000001f)] public float surfaceOffset = 0.0003f;
    public Color riverColor = new Color(0.05f, 0.48f, 0.78f, 1f);
    [Header("Inspection (context menu logs the selected cell)")]
    [Min(0)] public int debugCell;
    public bool showSelectedDrainage = true;
    [SerializeField] private int landCells, riverCells, visibleReaches, suppressedUphillReaches;
    [SerializeField] private int riverMouths, unresolvedSinks, filledCells, majorBasinCells;
    [SerializeField] private double topologyMilliseconds, visualMilliseconds;

    public GeodesicDrainageGraph Drainage { get; private set; }
    public float[] RiverStrength { get; private set; } = Array.Empty<float>();
    public Vector3[] ChannelAnchors { get; private set; } = Array.Empty<Vector3>();
    public IReadOnlyList<GeodesicRiverMouth> Mouths => mouths;
    public IReadOnlyDictionary<int, Vector3[]> RiverPaths => paths;
    public int SuppressedUphillReaches => suppressedUphillReaches;
    private readonly List<GeodesicRiverMouth> mouths = new List<GeodesicRiverMouth>();
    private readonly Dictionary<int, Vector3[]> paths = new Dictionary<int, Vector3[]>();
    private PlanetGenerator planet;
    private GeodesicRiverTerrain terrain;
    private GeodesicGridTopology topology;
    private GameObject visualRoot;
    private Mesh riverMesh;
    private Material riverMaterial;
    private double meanCellArea;
    private int pathSettingsHash;

    public void Initialize(PlanetGenerator owner, IcosphereRenderGeometry geometry, Mesh surfaceMesh)
    {
        Clear();
        if (owner == null || owner.CurrentGridType != PlanetGridType.GeodesicIcosphere ||
            owner.GeodesicTopology == null || surfaceMesh == null) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        planet = owner; topology = owner.GeodesicTopology;
        terrain = new GeodesicRiverTerrain(geometry, surfaceMesh.vertices, owner.GeodesicHydrologicalRenderRadii);
        int count = topology.CellCount;
        ChannelAnchors = new Vector3[count]; RiverStrength = new float[count];
        var elevations = new float[count]; var ocean = new bool[count];
        meanCellArea = 4d * Math.PI * owner.BasePlanetRadius * owner.BasePlanetRadius / count;
        for (int cell = 0; cell < count; cell++)
        {
            ocean[cell] = owner.IsGeodesicCellOcean(cell);
            Vector3 anchor = topology.CellDirections[cell];
            float lowest = terrain.Height(anchor);
            if (!ocean[cell])
            {
                landCells++;
                // One shared, terrain-selected junction per cell keeps tributaries connected.
                // The search stays close to the centre; it never edits the cell's simulation elevation.
                for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
                {
                    int neighbor = topology.Neighbors6[cell * 6 + slot];
                    Vector3 sample = Vector3.Lerp(topology.CellDirections[cell], topology.CellDirections[neighbor], 0.2f).normalized;
                    float height = terrain.Height(sample);
                    if (height < lowest && (!owner.enableOcean || terrain.VisibleHeight(sample) > owner.GeodesicSeaLevelRadius))
                    { lowest = height; anchor = sample; }
                }
            }
            ChannelAnchors[cell] = anchor; elevations[cell] = lowest;
        }
        // Coarse edge saddles discourage routes over ridges missed by cell-centre samples.
        // Cache once per undirected edge; discard this temporary cache after topology construction.
        var saddles = new Dictionary<ulong, float>();
        float Spill(int a, int b)
        {
            ulong key = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            if (saddles.TryGetValue(key, out float spill)) return spill;
            spill = float.NegativeInfinity;
            for (int i = 1; i <= 3; i++)
                spill = Mathf.Max(spill, terrain.Height(Vector3.Lerp(ChannelAnchors[a], ChannelAnchors[b], i / 4f).normalized));
            saddles.Add(key, spill); return spill;
        }
        Drainage = GeodesicDrainageGraph.Build(topology, elevations, ocean, owner.GeodesicSeaLevelRadius,
            owner.BasePlanetRadius, Spill);
        unresolvedSinks = Drainage.UnresolvedSinkCount; filledCells = Drainage.FilledCellCount;
        for (int i = 0; i < count; i++) if (Drainage.FillDepth[i] > majorBasinDepth) majorBasinCells++;
        topologyMilliseconds = watch.Elapsed.TotalMilliseconds;
        UpdateRunoff(null);
        Debug.Log($"[GeodesicDrainage] cells={count} land={landCells} rivers={riverCells} visibleReaches={visibleReaches} " +
            $"mouths={riverMouths} filledCells={filledCells} majorBasinCells={majorBasinCells} unresolvedSinks={unresolvedSinks} " +
            $"suppressedUphillReaches={suppressedUphillReaches} topologyMs={topologyMilliseconds:F2} visualMs={visualMilliseconds:F2} " +
            "authority=shared-large-scale-terrain projection=completed-visible-mesh topology=priority-flood runoff=replaceable-per-cell", this);
    }

    /// <summary>Null restores uniform density; otherwise input is a nonnegative volume/time per cell.
    /// Recalculates discharge/widths only. Reuses receivers, areas, anchors, terrain, and refined paths.</summary>
    public void UpdateRunoff(double[] localRunoff)
    {
        if (Drainage == null) return;
        if (localRunoff == null)
        {
            localRunoff = new double[Drainage.CellCount];
            for (int i = 0; i < localRunoff.Length; i++)
                localRunoff[i] = Drainage.LocalArea[i] * Mathf.Max(0f, uniformRunoffPerArea);
        }
        Drainage.UpdateRunoff(localRunoff);
        RebuildVisuals();
    }

    [ContextMenu("Rebuild Rivers For Current Terrain")]
    public void RebuildForCurrentTerrain()
    {
        PlanetGenerator owner = GetComponent<PlanetGenerator>();
        if (owner == null || owner.CurrentGridType != PlanetGridType.GeodesicIcosphere || !owner.enableGeodesicRivers)
        { Clear(); return; }
        var filter = owner.GetComponent<MeshFilter>();
        Initialize(owner, IcosphereRenderGeometryCache.GetOrBuild(owner.geodesicRenderSubdivisionLevel), filter != null ? filter.sharedMesh : null);
    }

    [ContextMenu("Apply Uniform Runoff")]
    private void ApplyUniformRunoff() => UpdateRunoff(null);

    [ContextMenu("Refresh River Thresholds And Visuals")]
    public void RebuildVisuals()
    {
        ClearVisual(); mouths.Clear();
        riverCells = visibleReaches = suppressedUphillReaches = riverMouths = majorBasinCells = 0;
        if (Drainage == null || planet == null) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int hash = HashCode.Combine(refinementSteps, refinementLanes, corridorWidthInCellSpacings, uphillTolerance);
        if (hash != pathSettingsHash) { paths.Clear(); pathSettingsHash = hash; }
        var vertices = new List<Vector3>(); var triangles = new List<int>(); var colors = new List<Color>();
        double threshold = meanCellArea * Math.Max(0.01f, riverFlowThreshold);
        for (int cell = 0; cell < Drainage.CellCount; cell++)
        {
            if (Drainage.FillDepth[cell] > majorBasinDepth) majorBasinCells++;
            RiverStrength[cell] = (float)(Drainage.AccumulatedRunoff[cell] / threshold);
            int receiver = Drainage.DrainageReceiver[cell];
            if (Drainage.Ocean[cell] || receiver < 0) continue;
            bool isMouth = Drainage.Ocean[receiver];
            Vector3[] path = Array.Empty<Vector3>();
            if (RiverStrength[cell] >= 1f)
            {
                riverCells++;
                if (!paths.TryGetValue(cell, out path))
                {
                    path = GeodesicRiverPath.Refine(ChannelAnchors[cell], ChannelAnchors[receiver], terrain.Height,
                        refinementSteps, refinementLanes, corridorWidthInCellSpacings, Mathf.Max(0f, uphillTolerance));
                    if (planet.enableOcean && path.Length > 0)
                        path = GeodesicRiverPath.ClipAtCoast(path, terrain.VisibleHeight, planet.GeodesicSeaLevelRadius);
                    paths[cell] = path;
                }
                if (path.Length < 2) suppressedUphillReaches++;
                else { AddRibbon(path, cell, vertices, triangles, colors); visibleReaches++; }
            }
            if (isMouth)
            {
                mouths.Add(new GeodesicRiverMouth {
                    UpstreamCell = cell, OceanCell = receiver, DrainageArea = Drainage.DrainageArea[cell],
                    AccumulatedRunoff = Drainage.AccumulatedRunoff[cell], Strength = RiverStrength[cell],
                    HasVisibleCoastPosition = path.Length > 1,
                    LocalCoastPosition = path.Length > 1 ? path[path.Length - 1] * terrain.Radius(path[path.Length - 1]) : Vector3.zero
                });
                if (RiverStrength[cell] >= 1f) riverMouths++;
            }
        }
        if (vertices.Count > 0)
        {
            riverMesh = new Mesh { name = "Geodesic River Ribbons", indexFormat = IndexFormat.UInt32 };
            riverMesh.SetVertices(vertices); riverMesh.SetTriangles(triangles, 0); riverMesh.SetColors(colors);
            riverMesh.RecalculateNormals(); riverMesh.RecalculateBounds();
            visualRoot = new GameObject("Geodesic Rivers"); visualRoot.layer = gameObject.layer;
            visualRoot.transform.SetParent(transform, false);
            visualRoot.AddComponent<MeshFilter>().sharedMesh = riverMesh;
            var renderer = visualRoot.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("SimulaVit/GeodesicVertexColorURP");
            if (shader != null)
            {
                riverMaterial = new Material(shader) { name = "Geodesic Rivers (Runtime)" };
                riverMaterial.SetFloat("_AmbientStrength", 0.75f); riverMaterial.SetFloat("_DiffuseStrength", 0.25f);
                renderer.sharedMaterial = riverMaterial;
            }
            else Debug.LogError("[GeodesicDrainage] Terrain vertex-colour shader is missing; river ribbons cannot render.", this);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            visualRoot.SetActive(showRivers && isActiveAndEnabled);
        }
        visualMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    private void AddRibbon(Vector3[] path, int cell, List<Vector3> vertices, List<int> triangles, List<Color> colors)
    {
        float width = planet.BasePlanetRadius * Mathf.Max(0.00001f, widthAtThreshold) *
            Mathf.Clamp(Mathf.Sqrt(RiverStrength[cell]), 1f, Mathf.Max(1f, maximumWidthMultiplier));
        Color color = Drainage.AccumulatedRunoff[cell] >= meanCellArea * Math.Max(riverFlowThreshold, majorRiverThreshold)
            ? Color.Lerp(riverColor, Color.cyan, 0.3f) : riverColor;
        int first = vertices.Count;
        // Sample visual micro-detail for placement without letting it choose drainage directions.
        var projected = new List<Vector3>();
        for (int i = 1; i < path.Length; i++)
            for (int s = 0; s < 3; s++) projected.Add(Vector3.Lerp(path[i - 1], path[i], s / 3f).normalized);
        projected.Add(path[path.Length - 1]);
        path = projected.ToArray();
        for (int i = 0; i < path.Length; i++)
        {
            Vector3 forward = path[Math.Min(i + 1, path.Length - 1)] - path[Math.Max(0, i - 1)];
            Vector3 side = Vector3.Cross(path[i], forward).normalized;
            float angularWidth = width / Mathf.Max(0.001f, terrain.Radius(path[i]));
            // Project both edges separately, so wide rivers do not tunnel through side slopes.
            Vector3 left = (path[i] - side * angularWidth * 0.5f).normalized;
            Vector3 right = (path[i] + side * angularWidth * 0.5f).normalized;
            vertices.Add(left * (terrain.Radius(left) + Mathf.Max(0.000001f, surfaceOffset)));
            vertices.Add(right * (terrain.Radius(right) + Mathf.Max(0.000001f, surfaceOffset)));
            colors.Add(color); colors.Add(color);
            if (i == 0) continue;
            int p = first + (i - 1) * 2;
            triangles.Add(p); triangles.Add(p + 2); triangles.Add(p + 1);
            triangles.Add(p + 1); triangles.Add(p + 2); triangles.Add(p + 3);
        }
    }

    [ContextMenu("Log Selected Drainage Cell")]
    private void LogSelectedCell()
    {
        if (Drainage == null || debugCell < 0 || debugCell >= Drainage.CellCount) return;
        int i = debugCell;
        Debug.Log($"[GeodesicDrainageCell] cell={i} receiver={Drainage.DrainageReceiver[i]} outlet={Drainage.OutletCell[i]} " +
            $"elevation={Drainage.HydrologicalElevation[i]:G9} filledElevation={Drainage.FilledElevation[i]:G9} fillDepth={Drainage.FillDepth[i]:G9} " +
            $"area={Drainage.DrainageArea[i]:G9} runoff={Drainage.AccumulatedRunoff[i]:G9} strength={RiverStrength[i]:G9}", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!showSelectedDrainage || Drainage == null || terrain == null || debugCell < 0 || debugCell >= Drainage.CellCount) return;
        int cell = debugCell;
        for (int steps = 0; steps < 256 && cell >= 0; steps++)
        {
            int next = Drainage.DrainageReceiver[cell];
            Vector3 start = transform.TransformPoint(ChannelAnchors[cell] * (terrain.Radius(ChannelAnchors[cell]) + surfaceOffset));
            Gizmos.color = Drainage.FillDepth[cell] > majorBasinDepth ? Color.magenta : Color.yellow;
            Gizmos.DrawSphere(start, planet.BasePlanetRadius * 0.001f);
            if (next >= 0) Gizmos.DrawLine(start, transform.TransformPoint(ChannelAnchors[next] * (terrain.Radius(ChannelAnchors[next]) + surfaceOffset)));
            cell = next;
        }
    }

    private void OnEnable() { if (visualRoot != null) visualRoot.SetActive(showRivers); }
    private void OnDisable() { if (visualRoot != null) visualRoot.SetActive(false); }
    private void OnDestroy() => Clear();
    public void Clear()
    {
        ClearVisual(); paths.Clear(); mouths.Clear(); Drainage = null; terrain = null; topology = null; planet = null;
        ChannelAnchors = Array.Empty<Vector3>(); RiverStrength = Array.Empty<float>();
        landCells = riverCells = visibleReaches = suppressedUphillReaches = riverMouths = unresolvedSinks = filledCells = majorBasinCells = 0;
        topologyMilliseconds = visualMilliseconds = 0d;
    }
    private void ClearVisual()
    {
        if (visualRoot != null) visualRoot.SetActive(false);
        Release(visualRoot); Release(riverMesh); Release(riverMaterial);
        visualRoot = null; riverMesh = null; riverMaterial = null;
    }
    private static void Release(UnityEngine.Object value)
    { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
}
