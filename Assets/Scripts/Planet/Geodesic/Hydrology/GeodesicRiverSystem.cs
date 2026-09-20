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
    public GeodesicLakeBasins Lakes { get; private set; }
    public GeodesicLakeGeometry LakeGeometry { get; private set; }
    public int LakeConnectedReaches { get; private set; }
    public int LakeBridgedReaches { get; private set; }
    public int LakeInlets { get; private set; }
    public int LakeOutlets { get; private set; }
    public int SuppressedProjectionMismatch { get; private set; }
    public int SuppressedCorridorFailure { get; private set; }
    public int UnrenderedSmallDepression { get; private set; }
    public int TrueTopologyFailure { get; private set; }
    public int InlandVisibleTerminations { get; private set; }
    public int OceanConnectedChains { get; private set; }
    private double lakeMilliseconds;
    private readonly Dictionary<int, GeodesicRiverReachPlan> reachPlans = new Dictionary<int, GeodesicRiverReachPlan>();
    private GameObject lakeVisualRoot;
    private Mesh lakeMesh;
    private Material lakeMaterial;
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
        Vector3[] completedSurface = surfaceMesh.vertices;
        terrain = new GeodesicRiverTerrain(geometry, completedSurface, owner.GeodesicHydrologicalRenderRadii);
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
                    if (height < lowest && (!owner.enableOcean || (owner.IsGeodesicOceanConnectivityFilteringActive && !owner.IsGeodesicDirectionInOceanMask(sample)) || terrain.VisibleHeight(sample) > owner.GeodesicSeaLevelRadius))
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
        var lakeWatch = System.Diagnostics.Stopwatch.StartNew();
        Lakes = GeodesicLakeBasins.Build(topology, Drainage, owner.BasePlanetRadius, owner.generateHydrologicalLakes,
            owner.minimumLakeBasinAreaFraction, owner.minimumLakeDepth, Spill);
        if (owner.generateHydrologicalLakes)
        {
            LakeGeometry = GeodesicLakeGeometry.Build(topology, Drainage, Lakes, geometry, completedSurface, terrain,
                owner.BasePlanetRadius, owner.minimumLakeBasinAreaFraction);
            Drainage.ApplyLakeReceivers(Lakes.CreateReceivers(topology, Drainage, Spill));
            BuildLakeVisual();
        }
        foreach (var basin in Lakes.Basins)
        {
            if (basin.SpillFromCell >= 0) basin.CatchmentArea = Drainage.DrainageArea[basin.SpillFromCell];
            if (basin.SpillCell >= 0) basin.DownstreamReceiver = Drainage.DrainageReceiver[basin.SpillCell];
        }
        lakeWatch.Stop(); lakeMilliseconds = lakeWatch.Elapsed.TotalMilliseconds;
        unresolvedSinks = Drainage.UnresolvedSinkCount; filledCells = Drainage.FilledCellCount;
        for (int i = 0; i < count; i++) if (Drainage.FillDepth[i] > majorBasinDepth) majorBasinCells++;
        topologyMilliseconds = watch.Elapsed.TotalMilliseconds;
        UpdateRunoff(null);
        Debug.Log($"[GeodesicDrainage] cells={count} land={landCells} rivers={riverCells} visibleReaches={visibleReaches} " +
            $"mouths={riverMouths} filledCells={filledCells} majorBasinCells={majorBasinCells} unresolvedSinks={unresolvedSinks} " +
            $"suppressedUphillReaches={suppressedUphillReaches} topologyMs={topologyMilliseconds:F2} visualMs={visualMilliseconds:F2} " +
            $"lakeConnectedReaches={LakeConnectedReaches} suppressedProjectionMismatch={SuppressedProjectionMismatch} suppressedCorridorFailure={SuppressedCorridorFailure} unresolvedSmallDepression={UnrenderedSmallDepression} trueTopologyFailure={TrueTopologyFailure} inlandVisibleTerminations={InlandVisibleTerminations} oceanConnectedChains={OceanConnectedChains} " +
            "authority=shared-large-scale-terrain projection=completed-visible-mesh topology=priority-flood runoff=replaceable-per-cell", this);
        LogLakeDiagnostics();
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
        if (owner == null || owner.CurrentGridType != PlanetGridType.GeodesicIcosphere || (!owner.enableGeodesicRivers && !owner.generateHydrologicalLakes))
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
        LakeConnectedReaches = LakeBridgedReaches = LakeInlets = LakeOutlets = SuppressedProjectionMismatch = SuppressedCorridorFailure = UnrenderedSmallDepression = TrueTopologyFailure = InlandVisibleTerminations = OceanConnectedChains = 0;
        if (Lakes != null) foreach (var basin in Lakes.Basins) basin.IncomingRiverCount = 0;
        if (Drainage == null || planet == null) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int hash = HashCode.Combine(refinementSteps, refinementLanes, corridorWidthInCellSpacings, uphillTolerance);
        if (hash != pathSettingsHash) { paths.Clear(); reachPlans.Clear(); pathSettingsHash = hash; }
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
                if (!reachPlans.TryGetValue(cell, out var plan))
                {
                    plan = GeodesicLakeRiverRouting.Build(cell, Drainage, ChannelAnchors, terrain, Lakes, LakeGeometry,
                        refinementSteps, refinementLanes, corridorWidthInCellSpacings, Mathf.Max(0f, uphillTolerance),
                        planet.enableOcean, planet.GeodesicSeaLevelRadius,
                        planet.IsGeodesicOceanConnectivityFilteringActive ? planet.IsGeodesicDirectionInOceanMask : (Func<Vector3, bool>)null);
                    reachPlans[cell] = plan; paths[cell] = plan.Path;
                }
                path = plan.Path;
                if (plan.LakeConnected) { LakeConnectedReaches++; if (plan.Path.Length < 2) LakeBridgedReaches++; }
                if (plan.Failure != GeodesicRiverReachFailure.None)
                {
                    suppressedUphillReaches++;
                    if (plan.Failure == GeodesicRiverReachFailure.ProjectionMismatch) SuppressedProjectionMismatch++;
                    else if (plan.Failure == GeodesicRiverReachFailure.CorridorFailure) SuppressedCorridorFailure++;
                    else if (plan.Failure == GeodesicRiverReachFailure.UnresolvedDepression) UnrenderedSmallDepression++;
                    else TrueTopologyFailure++;
                }
                if (path.Length > 1)
                {
                    AddRibbon(path, cell, vertices, triangles, colors); visibleReaches++;
                    if (plan.InletBasin >= 0) { LakeInlets++; Lakes.Basins[plan.InletBasin].IncomingRiverCount++; }
                    if (plan.OutletBasin >= 0) LakeOutlets++;
                }
                if (plan.InletBasin >= 0) isMouth = false;
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
        UpdateContinuityDiagnostics();
        if (vertices.Count > 0 && planet.enableGeodesicRivers)
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
            visualRoot.SetActive(showRivers && planet.enableGeodesicRivers && isActiveAndEnabled);
        }
        visualMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    public float VisibleTerrainRadius(Vector3 localDirection) => terrain != null ? terrain.Radius(localDirection) : 0f;
    public int LakeAtDirection(Vector3 localDirection) => LakeGeometry != null ? LakeGeometry.BasinAtDirection(localDirection) : -1;

    private void BuildLakeVisual()
    {
        if (LakeGeometry == null || LakeGeometry.Triangles.Length == 0) return;
        lakeMesh = new Mesh { name = "Geodesic Static Lakes", indexFormat = IndexFormat.UInt32 };
        lakeMesh.vertices = LakeGeometry.Vertices; lakeMesh.triangles = LakeGeometry.Triangles;
        var normals = new Vector3[LakeGeometry.Vertices.Length];
        for (int i = 0; i < normals.Length; i++) normals[i] = LakeGeometry.Vertices[i].normalized;
        lakeMesh.normals = normals; lakeMesh.RecalculateBounds();
        lakeVisualRoot = new GameObject("Geodesic Lakes"); lakeVisualRoot.layer = gameObject.layer;
        lakeVisualRoot.transform.SetParent(transform, false);
        lakeVisualRoot.AddComponent<MeshFilter>().sharedMesh = lakeMesh;
        var renderer = lakeVisualRoot.AddComponent<MeshRenderer>();
        Shader shader = Resources.Load<Shader>("GeodesicLakeURP");
        if (shader != null)
        { lakeMaterial = new Material(shader) { name = "Geodesic Lakes (Runtime)" }; renderer.sharedMaterial = lakeMaterial; }
        else Debug.LogError("[GeodesicLakes] Lake shader is missing.", this);
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        lakeVisualRoot.SetActive(isActiveAndEnabled);
    }

    private void UpdateContinuityDiagnostics()
    {
        var reachesOcean = new bool[Drainage.CellCount]; var incoming = new bool[Drainage.CellCount];
        foreach (int cell in Drainage.UpstreamToDownstream)
        {
            int receiver = Drainage.DrainageReceiver[cell];
            if (receiver >= 0 && RiverStrength[cell] >= 1f) incoming[receiver] = true;
        }
        for (int i = Drainage.CellCount - 1; i >= 0; i--)
        {
            int cell = Drainage.UpstreamToDownstream[i], receiver = Drainage.DrainageReceiver[cell];
            if (Drainage.Ocean[cell]) { reachesOcean[cell] = true; continue; }
            if (receiver < 0 || RiverStrength[cell] < 1f || !reachPlans.TryGetValue(cell, out var plan)) continue;
            bool visible = plan.Path.Length > 1;
            reachesOcean[cell] = (visible || plan.LakeConnected) && reachesOcean[receiver];
            if (reachesOcean[cell] && !incoming[cell]) OceanConnectedChains++;
            if (visible && plan.InletBasin < 0 && !Drainage.Ocean[receiver] &&
                (!reachPlans.TryGetValue(receiver, out var next) || (next.Path.Length < 2 && !next.LakeConnected))) InlandVisibleTerminations++;
        }
    }

    private void LogLakeDiagnostics()
    {
        if (Lakes == null) return;
        int visible = 0, terminal = 0, lakeCells = 0; double area = 0d, largest = 0d; float maxDepth = 0f;
        foreach (var basin in Lakes.Basins)
        {
            if (basin.Terminal) terminal++;
            if (!basin.Selected) continue;
            visible++; area += LakeGeometry.VisibleAreas[basin.Id]; largest = Math.Max(largest, LakeGeometry.VisibleAreas[basin.Id]); maxDepth = Mathf.Max(maxDepth, basin.MaximumDepth);
        }
        foreach (bool lake in Lakes.LakeMask) if (lake) lakeCells++;
        double planetArea = 4d * Math.PI * planet.BasePlanetRadius * planet.BasePlanetRadius;
        Debug.Log($"[GeodesicLakes] enabled={Lakes.Enabled} candidateBasins={Lakes.Basins.Length} visibleLakes={visible} silentlyResolvedBasins={Lakes.Basins.Length - visible - terminal - (LakeGeometry != null ? LakeGeometry.RejectedProjectionBasins : 0)} terminalBasins={terminal} rejectedProjectionBasins={(LakeGeometry != null ? LakeGeometry.RejectedProjectionBasins : 0)} lakeCells={lakeCells} totalLakeAreaFraction={area / planetArea:F8} largestLakeAreaFraction={largest / planetArea:F8} maxLakeDepth={maxDepth:F6} lakeInlets={LakeInlets} lakeOutlets={LakeOutlets} lakeBridgedRiverReaches={LakeBridgedReaches} generationMs={lakeMilliseconds:F2}", this);
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

    private void OnEnable() { if (visualRoot != null) visualRoot.SetActive(showRivers && planet != null && planet.enableGeodesicRivers); if (lakeVisualRoot != null) lakeVisualRoot.SetActive(true); }
    private void OnDisable() { if (visualRoot != null) visualRoot.SetActive(false); if (lakeVisualRoot != null) lakeVisualRoot.SetActive(false); }
    private void OnDestroy() => Clear();
    public void Clear()
    {
        if (lakeVisualRoot != null) lakeVisualRoot.SetActive(false);
        Release(lakeVisualRoot); Release(lakeMesh); Release(lakeMaterial); lakeVisualRoot = null; lakeMesh = null; lakeMaterial = null;
        Lakes = null; LakeGeometry = null; reachPlans.Clear(); lakeMilliseconds = 0d;
        LakeConnectedReaches = LakeBridgedReaches = LakeInlets = LakeOutlets = SuppressedProjectionMismatch = SuppressedCorridorFailure = UnrenderedSmallDepression = TrueTopologyFailure = InlandVisibleTerminations = OceanConnectedChains = 0;
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
