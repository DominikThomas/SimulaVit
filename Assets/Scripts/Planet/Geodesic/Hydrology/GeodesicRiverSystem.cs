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
    public float riverFlowThreshold = DefaultRiverFlowThreshold;
    [Min(0.01f)] public float majorRiverThreshold = DefaultMajorRiverThreshold;

    public const float DefaultRiverFlowThreshold = 8f;
    public const float DefaultMajorRiverThreshold = 64f;
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

    [Header("Debug Settings / River Debug")]
    public bool showRejectedUphillRiverSegments;
    public bool colourRenderedRiversByVisibleTerrainGrade;
    public bool showRiverTerminationReasons;
    public GeodesicRiverVisibleGradeAudit VisibleGradeAudit { get; private set; }
    public IReadOnlyList<RiverTerminationDebugPoint> RiverTerminationPoints => riverTerminationPoints;
    private List<RiverTerminationDebugPoint> riverTerminationPoints;
    private GameObject terminationDebugRoot;
    private Mesh terminationDebugMesh;
    private Material terminationDebugMaterial;
    public int TerminationDebugVertexCount => terminationDebugMesh != null ? terminationDebugMesh.vertexCount : 0;
    private List<RiverUphillDebugReach> rejectedUphill;
    public IReadOnlyList<RiverUphillDebugReach> RejectedUphillDebug => rejectedUphill;
    private GameObject uphillDebugRoot;
    private Mesh uphillDebugMesh;
    private Material uphillDebugMaterial;
    public int UphillDebugVertexCount => uphillDebugMesh != null ? uphillDebugMesh.vertexCount : 0;

    [Header("Grade diagnostics (observation only)")]
    public bool logGradeBreakExamples;
    [Range(0, 30)] public int gradeBreakExampleLimit = 6;
    public GeodesicRiverGradeReport GradeDiagnostics { get; private set; }

    // Only the historical graph-dependent lake path remains paused. TIN water surfaces
    // use the startup setting independently and never rewrite river receivers.
    public bool HydrologicalLakesPausedForBaselineValidation => true;
    public bool UsesDedicatedRenderHydrology => renderHydrology;
    public int HydrologySubdivision => TinDrainage != null ? TinDrainage.SubdivisionLevel : topology != null ? topology.SubdivisionLevel : -1;
    public int RiverMeshVertexCount => riverMesh != null ? riverMesh.vertexCount : 0;
    public int LakeMeshVertexCount => lakeMesh != null ? lakeMesh.vertexCount : 0;
    public int VisibleRiverReaches => visibleReaches;
    public double GenerationMilliseconds => topologyMilliseconds + visualMilliseconds;
    // Historical vertex drainage remains available only to the preserved reference method.
    public GeodesicDrainageGraph Drainage { get; private set; }
    public GeodesicTinHydrology TinDrainage { get; private set; }
    public GeodesicTinBasins TinLakeBasins { get; private set; }
    public GeodesicTinLakeGeometry TinLakeGeometry { get; private set; }
    public GameObject TinLakeVisualRoot => renderHydrology ? lakeVisualRoot : null;
    public TinHydrologyDiagnostics TinDiagnostics => TinDrainage?.Diagnostics;
    public string HydrologyTerrainSummary { get; private set; }
    public float[] HydrologyTerrainInputHeights { get; private set; }
    [Header("Continuous triangle drainage")]
    [Range(0f, .1f), Tooltip("Optional weak single pass over the selected drainage input. Zero uses that input unchanged. Automatically reduced on visible disagreement.")]
    public float tinFilterStrength = 0f;
    [Range(0f, .002f), Tooltip("Maximum micro bump excursion as a fraction of completed terrain relief. Never changes hydrological epsilon.")]
    public float visibleMicroBumpFraction = .001f;
    [Tooltip("Inspect thin raw gradient paths first. Disable only after licensed Unity visual acceptance.")]
    public bool drawRawTrianglePaths = true;
    private int[] tinSimulationCells;
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
    public int VisibleRiverChains => visibleRiverChains;
    public int BasinTerminatedChains => basinTerminatedChains;
    public int UnexpectedBrokenChains => unexpectedBrokenChains;
    public int Confluences => confluences;
    public int VisibleUphillSegments => visibleUphillSegments;
    public int VisibleRidgeCrossings => ridgeCrossings;
    public double ResolvedDrainageAreaThreshold => meanCellArea * Math.Max(.01f, riverFlowThreshold);
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
    private IGeodesicHydrologyTopology topology;
    private bool renderHydrology;
    private float[] routingHeights;
    private int visibleRiverChains, basinTerminatedChains, unexpectedBrokenChains, confluences;
    private int visibleUphillSegments, ridgeCrossings;
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
        planet = owner; topology = owner.GeodesicTopology;
        InitializeRenderHydrology(owner, geometry, surfaceMesh.vertices, System.Diagnostics.Stopwatch.StartNew());
    }

    // Preserved, inactive reference for the historical L6 corridor experiments.
    private void InitializeLegacyReference(PlanetGenerator owner, IcosphereRenderGeometry geometry, Mesh surfaceMesh)
    {
        Clear();
        if (owner == null || owner.CurrentGridType != PlanetGridType.GeodesicIcosphere ||
            owner.GeodesicTopology == null || surfaceMesh == null) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        planet = owner; topology = owner.GeodesicTopology;
        Vector3[] completedSurface = surfaceMesh.vertices;
        if (geometry.SubdivisionLevel > owner.geodesicSimulationSubdivisionLevel)
        {
            InitializeRenderHydrology(owner, geometry, completedSurface, watch);
            return;
        }
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
        Lakes = GeodesicLakeBasins.Build(topology, Drainage, owner.BasePlanetRadius, !HydrologicalLakesPausedForBaselineValidation && owner.generateHydrologicalLakes,
            owner.minimumLakeBasinAreaFraction, owner.minimumLakeDepth, Spill);
        if (!HydrologicalLakesPausedForBaselineValidation && owner.generateHydrologicalLakes)
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
        Debug.Log("[GeodesicRiverBaseline] reference=6f48085 routing=simulation-terrain-corridor lakes=OFF; Unity A/B visual acceptance pending.", this);
        if (owner.generateHydrologicalLakes)
            Debug.LogWarning("[GeodesicRiverBaseline] Saved lake generation is temporarily paused for river baseline validation. The saved lake setting has not been changed.", this);
        LogLakeDiagnostics();
        LogGradeDiagnostics();
    }

    /// <summary>Null restores uniform density; otherwise input is a nonnegative volume/time per cell.
    /// Recalculates discharge/widths only. Reuses receivers, areas, anchors, terrain, and refined paths.</summary>
    public void UpdateRunoff(double[] localRunoff)
    {
        if (TinDrainage != null)
        {
            if (localRunoff != null && localRunoff.Length == planet.GeodesicTopology.CellCount)
                localRunoff = MapSimulationRunoffToTriangles(localRunoff);
            TinDrainage.Accumulate(Mathf.Max(0f, uniformRunoffPerArea), localRunoff);
            RebuildRenderHydrologyVisuals();
            return;
        }
        if (Drainage == null) return;
        if (localRunoff == null)
        {
            localRunoff = new double[Drainage.CellCount];
            for (int i = 0; i < localRunoff.Length; i++)
                localRunoff[i] = Drainage.LocalArea[i] * Mathf.Max(0f, uniformRunoffPerArea);
        }
        if (renderHydrology && localRunoff.Length == planet.GeodesicTopology.CellCount)
        {
            if (planet.GeodesicCurrentRenderMapping != null)
                localRunoff = GeodesicHydrologyMapping.DistributeSimulationRunoff(
                    planet.GeodesicCurrentRenderMapping, localRunoff, Drainage.LocalArea, Drainage.Ocean);
            else
            {
                var mapped = new double[Drainage.CellCount];
                for (int i = 0; i < mapped.Length; i++)
                {
                    int simulation = GeodesicOceanConnectivity.FindNearestCell(planet.GeodesicTopology, topology.CellDirections[i]);
                    double simulationArea = planet.GeodesicTopology.UnitCellAreas[simulation] * (double)planet.BasePlanetRadius * planet.BasePlanetRadius;
                    mapped[i] = simulationArea > 0d ? localRunoff[simulation] * Drainage.LocalArea[i] / simulationArea : 0d;
                }
                localRunoff = mapped;
            }
        }
        Drainage.UpdateRunoff(localRunoff);
        RebuildVisuals();
    }

    /// <summary>The restored routing graph uses simulation cells directly.</summary>
    public void UpdateSimulationRunoff(double[] simulationLocalRunoff) => UpdateRunoff(simulationLocalRunoff);

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
        if (renderHydrology) { RebuildRenderHydrologyVisuals(); RefreshTinLakeVisuals(); return; }
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
        GradeDiagnostics = GeodesicRiverGradeAudit.Build(Drainage, ChannelAnchors, terrain, Lakes, reachPlans, threshold, planet.BasePlanetRadius, uphillTolerance);
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

    private void InitializeRenderHydrology(PlanetGenerator owner, IcosphereRenderGeometry geometry,
        Vector3[] completedSurface, System.Diagnostics.Stopwatch watch)
    {
        renderHydrology = true;
        // This lightweight vertex adjacency is used ONLY for retained-ocean component mapping.
        // River geometry, gradients, connectivity and runoff all use the render triangles.
        var oceanTopology = GeodesicHydrologyTopology.Build(geometry);
        bool[] ocean = owner.enableOcean
            ? GeodesicVisibleHydrology.MapRetainedOcean(oceanTopology, completedSurface, owner.GeodesicSeaLevelRadius,
                owner.IsGeodesicDirectionInOceanMask, owner.GeodesicCurrentRenderMapping,
                owner.GeodesicOceanMaskData, owner.IsGeodesicCellPotentialLakeBasin)
            : new bool[geometry.VertexCount];
        var visible = new float[geometry.VertexCount];
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        for (int i = 0; i < visible.Length; i++)
        { visible[i] = completedSurface[i].magnitude; min = Mathf.Min(min, visible[i]); max = Mathf.Max(max, visible[i]); }
        // Numerical precision only, capped independently of terrain bump allowance.
        double epsilon = Math.Min(Math.Max(0f, uphillTolerance), Math.Min(.000002d, owner.BasePlanetRadius * .00000025d));
        double bump = (max - (double)min) * Mathf.Clamp(visibleMicroBumpFraction, 0f, .002f);
        meanCellArea = 4d * Math.PI * owner.BasePlanetRadius * owner.BasePlanetRadius / owner.GeodesicTopology.CellCount;
        var terrainSettings = owner.GetGeodesicTerrainSettings();
        float[] hydrologyTerrainHeight = GeodesicHydrologyTerrain.Build(geometry.UnitVertices, visible, ocean,
            owner.DerivedTerrainSeed, terrainSettings, owner.BasePlanetRadius,
            owner.hydrologyTerrainMode, owner.enableGeodesicTerrainDisplacement,
            owner.GeodesicHydrologicalRenderRadii, owner.GeodesicRawRenderRadii);
        HydrologyTerrainInputHeights = hydrologyTerrainHeight;
        HydrologyTerrainSummary = GeodesicHydrologyTerrain.Summary(visible, hydrologyTerrainHeight, geometry.Triangles,
            owner.hydrologyTerrainMode, terrainSettings, owner.enableGeodesicTerrainDisplacement,
            owner.hydrologyTerrainMode == HydrologyTerrainMode.FineDetailRemoved && owner.GeodesicHydrologicalRenderRadii?.Length == visible.Length
                && owner.GeodesicRawRenderRadii?.Length == visible.Length);
        Debug.Log(HydrologyTerrainSummary, this);
        if (owner.hydrologyTerrainMode != HydrologyTerrainMode.Visible)
            TinDrainage = GeodesicTinHydrology.BuildAdaptive(geometry.UnitVertices, geometry.Triangles, visible, ocean,
                owner.GeodesicSeaLevelRadius, owner.BasePlanetRadius, epsilon, bump, tinFilterStrength,
                ResolvedDrainageAreaThreshold, Mathf.Max(0f, uniformRunoffPerArea), geometry.SubdivisionLevel,
                completedSurface: completedSurface, hydrologyTerrainHeights: hydrologyTerrainHeight);
        else
            TinDrainage = GeodesicTinHydrology.BuildAdaptive(geometry.UnitVertices, geometry.Triangles, visible, ocean,
            owner.GeodesicSeaLevelRadius, owner.BasePlanetRadius, epsilon, bump, tinFilterStrength,
            ResolvedDrainageAreaThreshold, Mathf.Max(0f, uniformRunoffPerArea), geometry.SubdivisionLevel, completedSurface: completedSurface);
        routingHeights = TinDrainage.HydrologyHeight;
        terrain = new GeodesicRiverTerrain(geometry, completedSurface, routingHeights, true);
        int count = TinDrainage.TriangleCount;
        RiverStrength = new float[count]; ChannelAnchors = new Vector3[count]; tinSimulationCells = new int[count];
        for (int t = 0; t < count; t++)
        {
            ChannelAnchors[t] = TinDrainage.Direction(TinDrainage.Centroid(t));
            int seed = owner.GeodesicCurrentRenderMapping != null
                ? owner.GeodesicCurrentRenderMapping.Samples[geometry.Triangles[t * 3]].NearestCell : 0;
            tinSimulationCells[t] = GeodesicOceanConnectivity.FindNearestCell(owner.GeodesicTopology, ChannelAnchors[t], seed);
            if (!TinDrainage.IsOcean(TinDrainage.Centroid(t))) landCells++;
        }
        // Static lake surfaces observe the completed TIN; river routing remains independent.
        topologyMilliseconds = watch.Elapsed.TotalMilliseconds;
        RebuildRenderHydrologyVisuals();
        RefreshTinLakeVisuals();
        Debug.Log($"[GeodesicTIN] simulationSubdivision={owner.GeodesicTopology.SubdivisionLevel} renderSubdivision={geometry.SubdivisionLevel} " +
            $"vertices={geometry.VertexCount} configuredThreshold={riverFlowThreshold:G6} resolvedAreaThreshold={ResolvedDrainageAreaThreshold:G9} " +
            $"epsilon={TinDrainage.Epsilon:G9} visibleMicroBumpThreshold={TinDrainage.VisibleBumpTolerance:G9} " +
            $"heightSource={owner.hydrologyTerrainMode} routing=continuous-facet-gradient rawDebug=" + drawRawTrianglePaths, this);
        Debug.Log(TinDiagnostics.Summary(), this);
        Debug.Log("[GeodesicTIN] Static lake rendering is independent of river paths; river/lake integration remains disabled.", this);
    }

    private double[] MapSimulationRunoffToTriangles(double[] supplied)
    {
        var area = new double[supplied.Length]; var result = new double[TinDrainage.TriangleCount];
        for (int t = 0; t < result.Length; t++)
            if (!TinDrainage.IsOcean(TinDrainage.Centroid(t))) area[tinSimulationCells[t]] += TinDrainage.LocalArea[t];
        for (int t = 0; t < result.Length; t++)
        {
            int cell = tinSimulationCells[t];
            if (area[cell] > 0d && !TinDrainage.IsOcean(TinDrainage.Centroid(t)))
                result[t] = supplied[cell] * TinDrainage.LocalArea[t] / area[cell];
        }
        return result;
    }

    private void RebuildRenderHydrologyVisuals()
    {
        ClearVisual(); paths.Clear(); mouths.Clear();
        visibleReaches = riverCells = riverMouths = 0;
        if (TinDrainage == null || planet == null) return;
        if (colourRenderedRiversByVisibleTerrainGrade || showRiverTerminationReasons)
        {
            VisibleGradeAudit = new GeodesicRiverVisibleGradeAudit();
            riverTerminationPoints = new List<RiverTerminationDebugPoint>();
        }
        rejectedUphill = showRejectedUphillRiverSegments ? new List<RiverUphillDebugReach>() : null;
        TinDrainage.ExtractNetworks(ResolvedDrainageAreaThreshold, planet.BasePlanetRadius,
            showRejectedUphillRiverSegments ? CaptureRejectedTinPath : (Action<int, TinPoint[]>)null);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        double threshold = ResolvedDrainageAreaThreshold;
        for (int t = 0; t < RiverStrength.Length; t++) RiverStrength[t] = (float)(TinDrainage.AccumulatedRunoff[t] / threshold);
        var vertices = new List<Vector3>(); var indices = new List<int>(); var colors = new List<Color>();
        var rendered = new bool[TinDrainage.Networks.Count];
        for (int id = 0; id < TinDrainage.Networks.Count; id++)
        {
            var network = TinDrainage.Networks[id]; Vector3[] raw = network.Centreline;
            if (raw.Length < 2) continue;
            // Resample all crossed TIN facets after float conversion, interpolation and
            // projection. Optional aesthetics must retain this validated raw fallback.
            if (!terrain.IsCentrelineValid(raw, TinDrainage.Epsilon, TinDrainage.VisibleBumpTolerance))
            {
                TinDiagnostics.RejectedPostProcessing++; TinDiagnostics.UnexpectedBrokenNetworks++;
                TinDiagnostics.UnexpectedInlandBreaks++;
                CaptureRejectedPath(network.SourceTriangle, raw, RiverDebugRejection.CentrelineGrade); continue;
            }
            Vector3[] path = raw;
            if (!drawRawTrianglePaths)
            {
                var interpolated = new List<Vector3>(raw.Length * 3);
                for (int i = 1; i < raw.Length; i++)
                    for (int s = 0; s < 3; s++) interpolated.Add(Vector3.Lerp(raw[i - 1], raw[i], s / 3f).normalized);
                interpolated.Add(raw[raw.Length - 1]);
                var candidate = interpolated.ToArray();
                if (terrain.IsCentrelineValid(candidate, TinDrainage.Epsilon, TinDrainage.VisibleBumpTolerance)) path = candidate;
                else TinDiagnostics.RejectedPostProcessing++;
            }
            var projectedDirections = new Vector3[path.Length];
            for (int i = 0; i < path.Length; i++) projectedDirections[i] =
                (path[i] * (terrain.Radius(path[i]) + Mathf.Max(.000001f, surfaceOffset))).normalized;
            if (!terrain.IsCentrelineValid(projectedDirections, TinDrainage.Epsilon, TinDrainage.VisibleBumpTolerance))
            {
                // Fall back to projection of the unsmoothed path, then fail closed if even
                // that cannot be represented at float precision. No later reach is restarted.
                path = raw; projectedDirections = new Vector3[raw.Length];
                for (int i = 0; i < raw.Length; i++) projectedDirections[i] =
                    (raw[i] * (terrain.Radius(raw[i]) + Mathf.Max(.000001f, surfaceOffset))).normalized;
                TinDiagnostics.RejectedPostProcessing++;
                if (!terrain.IsCentrelineValid(projectedDirections, TinDrainage.Epsilon, TinDrainage.VisibleBumpTolerance))
                { TinDiagnostics.UnexpectedBrokenNetworks++; TinDiagnostics.UnexpectedInlandBreaks++;
                    CaptureRejectedPath(network.SourceTriangle, projectedDirections, RiverDebugRejection.ProjectionGrade); continue; }
            }
            paths[network.SourceTriangle] = path;
            int firstVertex = vertices.Count, firstIndex = indices.Count;
            if (drawRawTrianglePaths) AddRawTinLine(path, vertices, indices, colors);
            else AddTinRibbon(path, network.SourceTriangle, vertices, indices, colors);
            VisibleGradeAudit?.ObserveEmittedPath(network.SourceTriangle, vertices, firstVertex, path.Length,
                drawRawTrianglePaths, firstIndex, terrain, planet.BasePlanetRadius);
            visibleReaches++; rendered[id] = true;
            if (network.Termination == TinTermination.Confluence && network.JunctionNetwork >= 0 && !rendered[network.JunctionNetwork])
            {
                TinDiagnostics.Confluences--; TinDiagnostics.UnexpectedBrokenNetworks++; TinDiagnostics.UnexpectedInlandBreaks++;
                network.Termination = TinTermination.RoutingFailure; network.JunctionNetwork = -1;
            }
            if (riverTerminationPoints != null)
            {
                int finalCentre = firstVertex + (path.Length - 1) * (drawRawTrianglePaths ? 1 : 3) + (drawRawTrianglePaths ? 0 : 1);
                riverTerminationPoints.Add(new RiverTerminationDebugPoint(network.SourceTriangle, network.Termination, vertices[finalCentre]));
            }
            if (network.Termination == TinTermination.OceanCoast)
            {
                int terminalTriangle = network.Samples[network.Samples.Length - 1].Triangle;
                int cell = tinSimulationCells[terminalTriangle];
                Vector3 coast = path[path.Length - 1];
                int oceanCell = RetainedOceanCellAtCoast(coast, cell);
                mouths.Add(new GeodesicRiverMouth { UpstreamCell = cell, OceanCell = oceanCell,
                    DrainageArea = TinDrainage.DrainageArea[terminalTriangle],
                    AccumulatedRunoff = TinDrainage.AccumulatedRunoff[terminalTriangle], Strength = RiverStrength[terminalTriangle],
                    HasVisibleCoastPosition = true, LocalCoastPosition = coast * terrain.Radius(coast) });
                riverMouths++;
            }
        }
        if (VisibleGradeAudit != null)
        {
            VisibleGradeAudit.Resolve(planet.BasePlanetRadius);
            VisibleGradeAudit.ClosedBasinTerminations = TinDiagnostics.ClosedBasinTerminations;
            VisibleGradeAudit.UnexpectedTerminations = TinDiagnostics.UnexpectedBrokenNetworks;
            foreach (var end in riverTerminationPoints)
            {
                if (end.Reason == TinTermination.TrueClosedBasin) VisibleGradeAudit.ClosedBasinMarkers++;
                if (end.Reason == TinTermination.RoutingFailure) VisibleGradeAudit.UnexpectedMarkers++;
            }
            if (colourRenderedRiversByVisibleTerrainGrade) VisibleGradeAudit.ApplyColours(vertices, indices, colors);
        }
        if (vertices.Count > 0 && planet.enableGeodesicRivers)
        {
            riverMesh = new Mesh { name = drawRawTrianglePaths ? "Raw TIN Gradient Rivers" : "TIN River Ribbons", indexFormat = IndexFormat.UInt32 };
            riverMesh.SetVertices(vertices); riverMesh.SetIndices(indices.ToArray(), drawRawTrianglePaths ? MeshTopology.Lines : MeshTopology.Triangles, 0);
            riverMesh.SetColors(colors);
            if (!drawRawTrianglePaths) riverMesh.RecalculateNormals();
            else riverMesh.SetNormals(vertices.ConvertAll(v => v.normalized));
            riverMesh.RecalculateBounds();
            visualRoot = new GameObject(drawRawTrianglePaths ? "Raw TIN Gradient Rivers" : "TIN River Ribbons");
            visualRoot.layer = gameObject.layer; visualRoot.transform.SetParent(transform, false);
            visualRoot.AddComponent<MeshFilter>().sharedMesh = riverMesh;
            var renderer = visualRoot.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("SimulaVit/GeodesicVertexColorURP");
            if (shader != null)
            {
                riverMaterial = new Material(shader) { name = "TIN Rivers (Runtime)" };
                riverMaterial.SetFloat("_AmbientStrength", 1f); riverMaterial.SetFloat("_DiffuseStrength", 0f);
                renderer.sharedMaterial = riverMaterial;
            }
            else Debug.LogError("[GeodesicTIN] River shader is missing.", this);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            visualRoot.SetActive(showRivers && planet.enableGeodesicRivers && isActiveAndEnabled);
        }
        if (showRejectedUphillRiverSegments) BuildUphillDebugVisual();
        if (showRiverTerminationReasons) BuildTerminationDebugVisual();
        if (VisibleGradeAudit != null) Debug.Log(VisibleGradeAudit.Summary(), this);
        TinDiagnostics.RibbonMilliseconds = watch.Elapsed.TotalMilliseconds;
        visualMilliseconds = TinDiagnostics.TracingMilliseconds + TinDiagnostics.RibbonMilliseconds;
        riverCells = TinDiagnostics.RiverTriangles; visibleRiverChains = TinDiagnostics.RiverNetworks;
        confluences = TinDiagnostics.Confluences; unexpectedBrokenChains = TinDiagnostics.UnexpectedBrokenNetworks;
        basinTerminatedChains = TinDiagnostics.ClosedBasinTerminations; OceanConnectedChains = TinDiagnostics.OceanTerminations;
        visibleUphillSegments = TinDiagnostics.VisibleMaterialUphillSegments; ridgeCrossings = TinDiagnostics.VisibleRidgeCrossings;
    }

    private void BuildTerminationDebugVisual()
    {
        var vertices = new List<Vector3>(); var indices = new List<int>(); var colors = new List<Color>();
        float size = planet.BasePlanetRadius * .009f;
        foreach (var end in riverTerminationPoints)
        {
            if (end.Reason != TinTermination.TrueClosedBasin && end.Reason != TinTermination.RoutingFailure) continue;
            Color color = end.Reason == TinTermination.TrueClosedBasin ? new Color(.7f, .15f, 1f, 1f) : Color.white;
            Vector3 up = end.Position.normalized;
            Vector3 side = Vector3.Cross(up, Math.Abs(up.y) < .9f ? Vector3.up : Vector3.right).normalized;
            Vector3 forward = Vector3.Cross(up, side);
            Vector3 centre = end.Position + up * (size + Mathf.Max(.000001f, surfaceOffset));
            int first = vertices.Count;
            vertices.Add(centre + up * size); vertices.Add(centre - up * size);
            vertices.Add(centre + side * size); vertices.Add(centre + forward * size);
            vertices.Add(centre - side * size); vertices.Add(centre - forward * size);
            for (int i = 0; i < 6; i++) colors.Add(color);
            for (int i = 0; i < 4; i++)
            {
                int a = first + 2 + i, b = first + 2 + (i + 1) % 4;
                indices.Add(first); indices.Add(a); indices.Add(b);
                indices.Add(first + 1); indices.Add(b); indices.Add(a);
            }
        }
        if (vertices.Count == 0 || !planet.enableGeodesicRivers) return;
        terminationDebugMesh = new Mesh { name = "River termination reasons", indexFormat = IndexFormat.UInt32 };
        terminationDebugMesh.SetVertices(vertices); terminationDebugMesh.SetTriangles(indices, 0);
        terminationDebugMesh.SetColors(colors); terminationDebugMesh.RecalculateNormals(); terminationDebugMesh.RecalculateBounds();
        terminationDebugRoot = new GameObject("River termination reasons"); terminationDebugRoot.layer = gameObject.layer;
        terminationDebugRoot.transform.SetParent(transform, false);
        terminationDebugRoot.AddComponent<MeshFilter>().sharedMesh = terminationDebugMesh;
        var renderer = terminationDebugRoot.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("SimulaVit/GeodesicVertexColorURP");
        if (shader != null)
        {
            terminationDebugMaterial = new Material(shader);
            terminationDebugMaterial.SetFloat("_AmbientStrength", 1f); terminationDebugMaterial.SetFloat("_DiffuseStrength", 0f);
            renderer.sharedMaterial = terminationDebugMaterial;
        }
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        terminationDebugRoot.SetActive(showRivers && isActiveAndEnabled);
    }

    private void CaptureRejectedTinPath(int source, TinPoint[] samples)
    {
        var path = new Vector3[samples.Length];
        for (int i = 0; i < samples.Length; i++) path[i] = TinDrainage.Direction(samples[i]);
        var reach = CaptureRejectedPath(source, new[] { path[path.Length - 2], path[path.Length - 1] }, RiverDebugRejection.VisibleGrade);
        if (reach == null) return;
        // Retain the original double-precision facet grades as well as projected grades.
        var heights = new double[samples.Length];
        for (int i = 0; i < samples.Length; i++) heights[i] = TinDrainage.Height(samples[i]);
        GeodesicRiverUphillDebug.Measure(reach, path, heights, planet.BasePlanetRadius, TinDrainage.Epsilon);
        double low = heights[0];
        foreach (double height in heights)
        {
            if (height > low + TinDrainage.Epsilon) reach.Reason = RiverDebugRejection.HydrologyGrade;
            low = Math.Min(low, height);
        }
    }

    private RiverUphillDebugReach CaptureRejectedPath(int source, Vector3[] path, RiverDebugRejection reason)
    {
        if (rejectedUphill == null || path == null || path.Length < 2) return null;
        foreach (Vector3 point in path)
            if (float.IsNaN(point.sqrMagnitude) || float.IsInfinity(point.sqrMagnitude) || point.sqrMagnitude == 0) return null;
        var reach = new RiverUphillDebugReach { Source = source, Receiver = terrain.FindTriangle(path[path.Length - 1]),
            Path = path, Reason = reason };
        terrain.MeasureRejectedPath(reach, planet.BasePlanetRadius, TinDrainage.Epsilon);
        rejectedUphill.Add(reach);
        return reach;
    }

    private void BuildUphillDebugVisual()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var vertices = new List<Vector3>(); var indices = new List<int>(); var colors = new List<Color>();
        foreach (var reach in rejectedUphill)
        {
            int firstColor = colors.Count;
            // Match the production display mode: pixel lines or flow-derived ribbon widths.
            if (drawRawTrianglePaths) AddRawTinLine(reach.Path, vertices, indices, colors);
            else AddTinRibbon(reach.Path, reach.Source, vertices, indices, colors);
            Color color = GeodesicRiverUphillDebug.SeverityColor(reach.MaximumSlope);
            for (int i = firstColor; i < colors.Count; i++) colors[i] = color;
        }
        if (vertices.Count > 0 && planet.enableGeodesicRivers)
        {
            uphillDebugMesh = new Mesh { name = "Rejected uphill rivers (diagnostic only)", indexFormat = IndexFormat.UInt32 };
            uphillDebugMesh.SetVertices(vertices);
            uphillDebugMesh.SetIndices(indices.ToArray(), drawRawTrianglePaths ? MeshTopology.Lines : MeshTopology.Triangles, 0);
            uphillDebugMesh.SetColors(colors);
            if (drawRawTrianglePaths) uphillDebugMesh.SetNormals(vertices.ConvertAll(v => v.normalized));
            else uphillDebugMesh.RecalculateNormals();
            uphillDebugMesh.RecalculateBounds();
            uphillDebugRoot = new GameObject("Rejected uphill rivers (diagnostic only)");
            uphillDebugRoot.layer = gameObject.layer; uphillDebugRoot.transform.SetParent(transform, false);
            uphillDebugRoot.AddComponent<MeshFilter>().sharedMesh = uphillDebugMesh;
            var renderer = uphillDebugRoot.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("SimulaVit/GeodesicVertexColorURP");
            if (shader != null)
            {
                uphillDebugMaterial = new Material(shader);
                uphillDebugMaterial.SetFloat("_AmbientStrength", 1f); uphillDebugMaterial.SetFloat("_DiffuseStrength", 0f);
                renderer.sharedMaterial = uphillDebugMaterial;
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            uphillDebugRoot.SetActive(showRivers && isActiveAndEnabled);
        }
        Debug.Log(GeodesicRiverUphillDebug.Summary(rejectedUphill, UphillDebugVertexCount, watch.Elapsed.TotalMilliseconds), this);
    }

    private void AddRawTinLine(Vector3[] path, List<Vector3> vertices, List<int> indices, List<Color> colors)
    {
        int first = vertices.Count;
        for (int i = 0; i < path.Length; i++)
        {
            vertices.Add(path[i] * (terrain.Radius(path[i]) + Mathf.Max(.000001f, surfaceOffset))); colors.Add(riverColor);
            if (i > 0) { indices.Add(first + i - 1); indices.Add(first + i); }
        }
    }

    private void AddTinRibbon(Vector3[] path, int source, List<Vector3> vertices, List<int> indices, List<Color> colors)
    {
        int first = vertices.Count; float strength = RiverStrength[source];
        for (int i = 0; i < path.Length; i++)
        {
            int triangle = terrain.FindTriangle(path[i]);
            strength = Mathf.Max(strength, (float)(TinDrainage.AccumulatedRunoff[triangle] / ResolvedDrainageAreaThreshold));
            float width = planet.BasePlanetRadius * Mathf.Max(.00001f, widthAtThreshold) *
                Mathf.Clamp(Mathf.Sqrt(strength), 1f, Mathf.Max(1f, maximumWidthMultiplier));
            Vector3 forward = path[Math.Min(i + 1, path.Length - 1)] - path[Math.Max(0, i - 1)];
            Vector3 side = Vector3.Cross(path[i], forward).normalized;
            float angle = width / Mathf.Max(.001f, terrain.Radius(path[i]));
            Vector3 left = (path[i] - side * angle * .5f).normalized;
            Vector3 right = (path[i] + side * angle * .5f).normalized;
            float offset = Mathf.Max(.000001f, surfaceOffset);
            // Three columns retain the EXACT checked centreline as mesh vertices. Projecting
            // unequal banks must not move the river's middle onto a different routing path.
            vertices.Add(left * (terrain.Radius(left) + offset));
            vertices.Add(path[i] * (terrain.Radius(path[i]) + offset));
            vertices.Add(right * (terrain.Radius(right) + offset));
            Color color = strength * riverFlowThreshold >= Math.Max(riverFlowThreshold, majorRiverThreshold)
                ? Color.Lerp(riverColor, Color.cyan, .3f) : riverColor;
            colors.Add(color); colors.Add(color); colors.Add(color);
            if (i == 0) continue;
            int p = first + (i - 1) * 3;
            indices.Add(p); indices.Add(p + 3); indices.Add(p + 1);
            indices.Add(p + 1); indices.Add(p + 3); indices.Add(p + 4);
            indices.Add(p + 1); indices.Add(p + 4); indices.Add(p + 2);
            indices.Add(p + 2); indices.Add(p + 4); indices.Add(p + 5);
        }
    }

    private int RetainedOceanCellAtCoast(Vector3 coast, int seed)
    {
        // Mouth metadata must refer to a retained simulation ocean cell even when the
        // exact L7 shoreline is nearer an L6 land centre. This does not change the path.
        var simulation = planet.GeodesicTopology;
        int nearest = GeodesicOceanConnectivity.FindNearestCell(simulation, coast, seed);
        if (planet.IsGeodesicCellOcean(nearest)) return nearest;
        var queue = new Queue<int>(); var seen = new HashSet<int> { nearest }; queue.Enqueue(nearest);
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            for (int k = 0; k < simulation.NeighborCounts[cell]; k++)
            {
                int next = simulation.Neighbors6[cell * 6 + k];
                if (!seen.Add(next)) continue;
                if (planet.IsGeodesicCellOcean(next)) return next;
                queue.Enqueue(next);
            }
        }
        return -1;
    }

    [ContextMenu("Refresh TIN Lake Surfaces Only")]
    public void RefreshTinLakeVisuals()
    {
        if (!renderHydrology || TinDrainage == null || planet == null) return;
        if (lakeVisualRoot != null) lakeVisualRoot.SetActive(false);
        Release(lakeVisualRoot); Release(lakeMesh); Release(lakeMaterial);
        lakeVisualRoot = null; lakeMesh = null; lakeMaterial = null;
        TinLakeBasins = null; TinLakeGeometry = null;
        if (!planet.generateHydrologicalLakes) return;
        TinLakeBasins = GeodesicTinBasins.Build(TinDrainage, planet.minimumLakeBasinAreaFraction, planet.minimumLakeDepth);
        TinLakeGeometry = GeodesicTinLakeGeometry.Build(TinDrainage, TinLakeBasins);
        Debug.Log(TinLakeGeometry.Summary(), this);
        if (TinLakeGeometry.Triangles.Length == 0) return;
        lakeMesh = new Mesh { name = "Geodesic TIN Lake Surfaces", indexFormat = IndexFormat.UInt32 };
        lakeMesh.vertices = TinLakeGeometry.Vertices; lakeMesh.triangles = TinLakeGeometry.Triangles;
        var normals = new Vector3[TinLakeGeometry.Vertices.Length];
        for (int i = 0; i < normals.Length; i++) normals[i] = TinLakeGeometry.Vertices[i].normalized;
        lakeMesh.normals = normals; lakeMesh.RecalculateBounds();
        lakeVisualRoot = new GameObject("Geodesic TIN Lakes"); lakeVisualRoot.layer = gameObject.layer;
        lakeVisualRoot.transform.SetParent(transform, false);
        lakeVisualRoot.AddComponent<MeshFilter>().sharedMesh = lakeMesh;
        var renderer = lakeVisualRoot.AddComponent<MeshRenderer>();
        Shader shader = Resources.Load<Shader>("GeodesicLakeURP");
        if (shader != null)
        { lakeMaterial = new Material(shader) { name = "Geodesic TIN Lakes (Runtime)" }; renderer.sharedMaterial = lakeMaterial; }
        else Debug.LogError("[GeodesicTinLakes] Lake shader is missing.", this);
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        lakeVisualRoot.SetActive(isActiveAndEnabled);
    }

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

    [Header("Optional TIN basin inspection (context actions only)")]
    public int selectedTinBasinId;

    [ContextMenu("Audit TIN Basin Topology")]
    public void AuditTinBasins()
    {
        if (TinDrainage == null) return;
        var basins = GeodesicTinBasins.Build(TinDrainage,
            planet != null ? planet.minimumLakeBasinAreaFraction : .0001d,
            planet != null ? planet.minimumLakeDepth : .005d);
        Debug.Log(basins.Summary(), this);
    }

    [ContextMenu("Log Selected TIN Basin And Termination Matches")]
    public void LogSelectedTinBasin()
    {
        if (TinDrainage == null) return;
        var basins = GeodesicTinBasins.Build(TinDrainage,
            planet != null ? planet.minimumLakeBasinAreaFraction : .0001d,
            planet != null ? planet.minimumLakeDepth : .005d);
        Debug.Log(GeodesicTinBasinInspection.SelectedReport(basins, TinDrainage, selectedTinBasinId), this);
    }

    [ContextMenu("Log River Grade Diagnostics")]
    public void LogGradeDiagnostics()
    {
        if (TinDiagnostics != null) { Debug.Log(TinDiagnostics.Summary(), this); return; }
        if (GradeDiagnostics == null) return;
        Debug.Log(GradeDiagnostics.Summary(), this);
        if (logGradeBreakExamples)
            foreach (string example in GradeDiagnostics.BreakExamples(gradeBreakExampleLimit)) Debug.Log(example, this);
    }

    [ContextMenu("Audit Suppressed River Corridors (Detailed)")]
    public void AuditSuppressedRiverCorridors()
    {
        if (TinDiagnostics != null) { Debug.Log(TinDiagnostics.Summary(), this); return; }
        if (Drainage == null || planet == null) return;
        // Explicit diagnostic action only. Replays the sampled corridor as a minimax problem;
        // no route, geometry, accumulation or production tolerance is modified.
        GradeDiagnostics = GeodesicRiverGradeAudit.Build(Drainage, ChannelAnchors, terrain, Lakes, reachPlans,
            meanCellArea * Math.Max(.01f, riverFlowThreshold), planet.BasePlanetRadius, uphillTolerance,
            true, refinementSteps, refinementLanes, corridorWidthInCellSpacings);
        LogGradeDiagnostics();
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
        Color color = (TinDrainage != null ? TinDrainage.AccumulatedRunoff[cell] : Drainage.AccumulatedRunoff[cell]) >= meanCellArea * Math.Max(riverFlowThreshold, majorRiverThreshold)
            ? Color.Lerp(riverColor, Color.cyan, 0.3f) : riverColor;
        int first = vertices.Count;
        // Sample visual micro-detail for placement without letting it choose drainage directions.
        var projected = new List<Vector3>();
        for (int i = 1; i < path.Length; i++)
            for (int s = 0; s < 3; s++) projected.Add(Vector3.Lerp(path[i - 1], path[i], s / 3f).normalized);
        projected.Add(path[path.Length - 1]);
        var candidate = projected.ToArray();
        if (!renderHydrology || terrain.IsCentrelineValid(candidate, TinDrainage.Epsilon, TinDrainage.VisibleBumpTolerance)) path = candidate;
        else TinDiagnostics.RejectedPostProcessing++;
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
        if (TinDrainage != null)
        {
            if (debugCell < 0 || debugCell >= TinDrainage.TriangleCount) return;
            TinPoint point = TinDrainage.Centroid(debugCell);
            Debug.Log($"[GeodesicTinTriangle] triangle={debugCell} receiver={TinDrainage.TriangleReceiver[debugCell]} " +
                $"H={TinDrainage.Height(point):G9} visible={TinDrainage.Height(point, true):G9} " +
                $"gradient={TinDrainage.DownhillGradient(debugCell)} localArea={TinDrainage.LocalArea[debugCell]:G9} " +
                $"drainageArea={TinDrainage.DrainageArea[debugCell]:G9} runoff={TinDrainage.AccumulatedRunoff[debugCell]:G9}", this);
            return;
        }
        if (Drainage == null || debugCell < 0 || debugCell >= Drainage.CellCount) return;
        int i = debugCell;
        Debug.Log($"[GeodesicDrainageCell] cell={i} receiver={Drainage.DrainageReceiver[i]} outlet={Drainage.OutletCell[i]} " +
            $"elevation={Drainage.HydrologicalElevation[i]:G9} filledElevation={Drainage.FilledElevation[i]:G9} fillDepth={Drainage.FillDepth[i]:G9} " +
            $"area={Drainage.DrainageArea[i]:G9} runoff={Drainage.AccumulatedRunoff[i]:G9} strength={RiverStrength[i]:G9}", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (TinDrainage != null)
        {
            if (!showSelectedDrainage || !paths.TryGetValue(debugCell, out var path)) return;
            Gizmos.color = Color.yellow;
            for (int i = 1; i < path.Length; i++)
                Gizmos.DrawLine(transform.TransformPoint(path[i - 1] * (terrain.Radius(path[i - 1]) + surfaceOffset)),
                    transform.TransformPoint(path[i] * (terrain.Radius(path[i]) + surfaceOffset)));
            return;
        }
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

    private void OnEnable() { if (terminationDebugRoot != null) terminationDebugRoot.SetActive(showRiverTerminationReasons && showRivers); if (uphillDebugRoot != null) uphillDebugRoot.SetActive(showRejectedUphillRiverSegments && showRivers); if (visualRoot != null) visualRoot.SetActive(showRivers && planet != null && planet.enableGeodesicRivers); if (lakeVisualRoot != null) lakeVisualRoot.SetActive(true); }
    private void OnDisable() { if (terminationDebugRoot != null) terminationDebugRoot.SetActive(false); if (uphillDebugRoot != null) uphillDebugRoot.SetActive(false); if (visualRoot != null) visualRoot.SetActive(false); if (lakeVisualRoot != null) lakeVisualRoot.SetActive(false); }
    private void OnDestroy() => Clear();
    public void Clear()
    {
        if (lakeVisualRoot != null) lakeVisualRoot.SetActive(false);
        Release(lakeVisualRoot); Release(lakeMesh); Release(lakeMaterial); lakeVisualRoot = null; lakeMesh = null; lakeMaterial = null;
        GradeDiagnostics = null;
        Lakes = null; LakeGeometry = null; TinLakeBasins = null; TinLakeGeometry = null; reachPlans.Clear(); lakeMilliseconds = 0d;
        LakeConnectedReaches = LakeBridgedReaches = LakeInlets = LakeOutlets = SuppressedProjectionMismatch = SuppressedCorridorFailure = UnrenderedSmallDepression = TrueTopologyFailure = InlandVisibleTerminations = OceanConnectedChains = 0;
        ClearVisual(); paths.Clear(); mouths.Clear(); Drainage = null; TinDrainage = null; tinSimulationCells = null; terrain = null; topology = null; planet = null;
        ChannelAnchors = Array.Empty<Vector3>(); RiverStrength = Array.Empty<float>();
        renderHydrology = false; routingHeights = null;
        HydrologyTerrainSummary = null; HydrologyTerrainInputHeights = null;
        landCells = riverCells = visibleReaches = suppressedUphillReaches = riverMouths = unresolvedSinks = filledCells = majorBasinCells = 0;
        topologyMilliseconds = visualMilliseconds = 0d;
    }
    private void ClearVisual()
    {
        if (terminationDebugRoot != null) terminationDebugRoot.SetActive(false);
        Release(terminationDebugRoot); Release(terminationDebugMesh); Release(terminationDebugMaterial);
        terminationDebugRoot = null; terminationDebugMesh = null; terminationDebugMaterial = null;
        VisibleGradeAudit = null; riverTerminationPoints = null;
        if (uphillDebugRoot != null) uphillDebugRoot.SetActive(false);
        Release(uphillDebugRoot); Release(uphillDebugMesh); Release(uphillDebugMaterial);
        uphillDebugRoot = null; uphillDebugMesh = null; uphillDebugMaterial = null; rejectedUphill = null;
        if (visualRoot != null) visualRoot.SetActive(false);
        Release(visualRoot); Release(riverMesh); Release(riverMaterial);
        visualRoot = null; riverMesh = null; riverMaterial = null;
    }
    private static void Release(UnityEngine.Object value)
    { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
}
