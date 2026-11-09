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
public sealed partial class GeodesicRiverSystem : MonoBehaviour
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
    [Min(1f)] public float maximumWidthMultiplier = 6f;
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
    [Tooltip("Observe final runoff delivery and generation fingerprints. No routing or rendering changes.")]
    public bool logDrainageDelivery = true;
    public GeodesicDrainageDelivery DrainageDelivery { get; private set; }
    private bool collectDelivery;
    private string deliveryOceanMaskHash;
    private double deliveryMaskHashMilliseconds;
    private int[] deliveryOceanComponentByVertex;
    [Range(0, 30)] public int gradeBreakExampleLimit = 6;
    public GeodesicRiverGradeReport GradeDiagnostics { get; private set; }

    public bool UsesDedicatedRenderHydrology => renderHydrology;
    public int HydrologySubdivision => TinDrainage != null ? TinDrainage.SubdivisionLevel : topology != null ? topology.SubdivisionLevel : -1;
    public int RiverMeshVertexCount => riverMesh != null ? riverMesh.vertexCount : 0;
    public int LakeMeshVertexCount => lakeMesh != null ? lakeMesh.vertexCount : 0;
    public int VisibleRiverReaches => visibleReaches;
    public double GenerationMilliseconds => topologyMilliseconds + visualMilliseconds;
    // Retained for the historical comparison and cell diagnostic APIs.
    public GeodesicDrainageGraph Drainage { get; private set; }
    public GeodesicTinHydrology TinDrainage { get; private set; }
    public GeodesicTinBasins TinLakeBasins { get; private set; }
    public GeodesicTinLakeGeometry TinLakeGeometry { get; private set; }
    public GameObject TinLakeVisualRoot => renderHydrology ? lakeVisualRoot : null;
    [Tooltip("Debug A/B switch. Uses only rendered TIN lake bodies; source detection and raw TIN accumulation are unchanged.")]
    public bool integrateTinLakeRivers = true;
    public GeodesicTinLakeRouting TinLakeRouting { get; private set; }
    public TinHydrologyDiagnostics TinDiagnostics => TinDrainage?.Diagnostics;
    public string HydrologyTerrainSummary { get; private set; }
    public float[] HydrologyTerrainInputHeights { get; private set; }
    [Header("Continuous triangle drainage")]
    [Range(0f, .1f), Tooltip("Optional weak single pass over the selected drainage input. Zero uses that input unchanged. Automatically reduced on visible disagreement.")]
    public float tinFilterStrength = 0f;
    [Range(0f, .002f), Tooltip("Maximum micro bump excursion as a fraction of completed terrain relief. Never changes hydrological epsilon.")]
    public float visibleMicroBumpFraction = .001f;
    [Tooltip("Replace river ribbons with thin raw gradient centrelines for routing inspection.")]
    public bool drawRawTrianglePaths;
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
    private GeodesicRiverWidthProfile WidthProfile => new GeodesicRiverWidthProfile(
        ResolvedDrainageAreaThreshold, meanCellArea * Math.Max(.01f, majorRiverThreshold),
        planet.BasePlanetRadius * Mathf.Max(.00001f, widthAtThreshold), maximumWidthMultiplier);
    private double lakeMilliseconds;
    private readonly Dictionary<int, GeodesicRiverReachPlan> reachPlans = new Dictionary<int, GeodesicRiverReachPlan>();
    private GameObject lakeVisualRoot;
    private Mesh lakeMesh;
    private Material lakeMaterial;
    private GeodesicTinBasins routingLakeBasins;
    private GeodesicTinLakeGeometry routingLakeGeometry;
    private GeodesicTinLakeSediment lakeSediment;
    public GeodesicTinSedimentCorridors SedimentCorridors { get; private set; }
    private GeodesicHydrologyTopology corridorTopology;
    private GeodesicRiverTerrain corridorTerrain;
    private Mesh sourceTerrainMesh, sedimentTerrainMesh, sourceColliderMesh;
    private double extractedTinThreshold = double.NaN;
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
    private bool[] retainedCarvingOcean;
    private GeodesicHydrologyTopology carvingTopology;
    private bool intermediateTinCarving;
    public GeodesicValleyCarving.Result ValleyCarving { get; private set; }
    public IReadOnlyList<GeodesicValleyCarving.Result> ValleyCarvingResults { get; private set; } = Array.Empty<GeodesicValleyCarving.Result>();

    public void Initialize(PlanetGenerator owner, IcosphereRenderGeometry geometry, Mesh surfaceMesh)
    {
        foreach (var progress in InitializeStages(owner, geometry, surfaceMesh)) { }
    }

    public IEnumerable<PlanetGenerationProgress> InitializeStages(PlanetGenerator owner, IcosphereRenderGeometry geometry, Mesh surfaceMesh) =>
        InitializeStages(owner, geometry, surfaceMesh, null);

    internal IEnumerable<PlanetGenerationProgress> InitializeStages(PlanetGenerator owner, IcosphereRenderGeometry geometry,
        Mesh surfaceMesh, Action<GeodesicCarvingCheckpoint, byte[], int> captureSnapshot)
    {
        bool enabled = logDrainageDelivery && owner != null && surfaceMesh != null &&
            owner.CurrentGridType == PlanetGridType.GeodesicIcosphere && owner.GeodesicTopology != null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        GeodesicGenerationFingerprint fingerprint = null; string pre = null, prePositions = null;
        double fingerprintMs = 0;
        if (enabled)
        {
            fingerprint = new GeodesicGenerationFingerprint();
            fingerprint.AddFields("planet", owner); fingerprint.AddFields("river", this);
            fingerprint.AddFields("effectiveTerrain", owner.GetGeodesicTerrainSettings());
            fingerprint.Add("effective.inlandElevationMagnitude", owner.inlandElevationMagnitude);
            fingerprint.Add("effective.infillExcludedInlandBasins", owner.infillExcludedInlandBasins);
            fingerprint.Add("effective.basinInfillLimits", owner.basinInfillLimits.Normalized());
            fingerprint.Add("effective.baseRadius", owner.BasePlanetRadius);
            fingerprint.Add("effective.seaLevel", owner.GeodesicSeaLevelRadius);
            fingerprint.Add("effective.terrainSeed", owner.DerivedTerrainSeed);
            fingerprint.Add("effective.visualSeed", owner.DerivedVisualSeed);
            fingerprint.Add("effective.bathymetrySeed", owner.DerivedBathymetrySeed);
            fingerprint.Add("effective.generationVersion", PlanetGenerator.GenerationVersion);
            fingerprint.Add("effective.oceanConnectivityPlanVersion", GeodesicOceanDrainagePlan.Version);
            foreach (PlanetSeedDomain domain in Enum.GetValues(typeof(PlanetSeedDomain)))
                fingerprint.Add("derivedDomains." + domain, PlanetSeedUtility.DeriveSeed(owner.randomSeed, domain, PlanetGenerator.GenerationVersion));
            fingerprint.Add("effective.carvingAlreadyAttempted", owner.GeodesicValleyCarvingAttempted);
            fingerprint.Add("effective.carveDepth", GeodesicValleyCarving.Normalize(owner.maximumValleyCarveDepth, GeodesicValleyCarving.DefaultDepth, 1f));
            fingerprint.Add("effective.carveRunoff", GeodesicValleyCarving.Normalize(owner.minimumValleyCarveRunoff, GeodesicValleyCarving.DefaultRunoff, 1000000f));
            fingerprint.Add("effective.carveWidth", GeodesicValleyCarving.Normalize(owner.valleyCarveWidth, GeodesicValleyCarving.DefaultWidth, 1f));
            fingerprint.Add("effective.carvePasses", Mathf.Clamp(owner.valleyCarvingPasses, 1, 5));
            fingerprint.Add("runtime.unity", Application.unityVersion);
            fingerprint.Add("runtime.platform", Application.platform);
            fingerprint.Add("runtime.assemblyMvid", typeof(GeodesicRiverSystem).Module.ModuleVersionId.ToString());
            fingerprint.Add("mesh.indicesHash", GeodesicGenerationFingerprint.Indices(geometry.Triangles));
            fingerprint.Add("mesh.directionsHash", GeodesicGenerationFingerprint.Positions(geometry.UnitVertices));
            fingerprint.Add("inputs.rawRadiiHash", GeodesicGenerationFingerprint.Heights(owner.GeodesicRawRenderRadii));
            fingerprint.Add("inputs.hydrologicalRadiiHash", GeodesicGenerationFingerprint.Heights(owner.GeodesicHydrologicalRenderRadii));
            fingerprint.Add("inputs.simulationOceanMaskHash", GeodesicGenerationFingerprint.Mask(owner.GeodesicOceanMaskData));
            var initialSurface = surfaceMesh.vertices;
            pre = GeodesicGenerationFingerprint.SurfaceHeights(initialSurface);
            prePositions = GeodesicGenerationFingerprint.Positions(initialSurface);
            fingerprintMs = watch.Elapsed.TotalMilliseconds;
        }
        collectDelivery = enabled;
        try
        {
            foreach (var progress in InitializeCoreStages(owner, geometry, surfaceMesh, captureSnapshot))
                yield return progress;
        }
        finally { collectDelivery = false; }
        if (!enabled || DrainageDelivery == null) { TinDrainage?.ReleaseParcelDestinations(); yield break; }
        watch.Restart();
        fingerprint.Add("effective.epsilon", TinDrainage.Epsilon);
        fingerprint.Add("effective.visibleBumpTolerance", TinDrainage.VisibleBumpTolerance);
        fingerprint.Add("effective.filterStrength", TinDiagnostics.FilterStrength);
        fingerprint.Add("effective.threshold", ResolvedDrainageAreaThreshold);
        fingerprint.Add("effective.runoffDensity", Mathf.Max(0f, uniformRunoffPerArea));
        string parameterHash = fingerprint.ParametersHash;
        string post = GeodesicGenerationFingerprint.Heights(TinDrainage.VisibleHeight);
        string hydrology = GeodesicGenerationFingerprint.Heights(TinDrainage.HydrologyHeight);
        string runoff = GeodesicGenerationFingerprint.Runoff(TinDrainage);
        string postPositions = GeodesicGenerationFingerprint.Positions(surfaceMesh.vertices);
        fingerprintMs += watch.Elapsed.TotalMilliseconds + deliveryMaskHashMilliseconds;
        Debug.Log(FormattableString.Invariant($"[GeodesicGenerationFingerprint] schema=1 seed={owner.randomSeed} terrainSeed={owner.DerivedTerrainSeed} visualSeed={owner.DerivedVisualSeed} bathymetrySeed={owner.DerivedBathymetrySeed} terrainParametersHash={parameterHash} preCarveTerrainHash={pre} postCarveTerrainHash={post} preCarvePositionsHash={prePositions} finalHydrologyHash={hydrology} retainedOceanMaskHash={deliveryOceanMaskHash} seaLevel={owner.GeodesicSeaLevelRadius:R} fingerprintMs={fingerprintMs:F3} hash=SHA256-little-endian-IEEE754-index-order"), this);
        Debug.Log("[GeodesicGenerationFingerprint] parameters=" + fingerprint.Manifest, this);
        Debug.Log(FormattableString.Invariant($"[GeodesicGenerationFingerprint] postCarvePositionsHash={postPositions} independentRunoffHash={runoff} captureMs={TinDrainage.ParcelCaptureMilliseconds:F3} diagnosticWorkMs={fingerprintMs + DrainageDelivery.AnalysisMilliseconds + TinDrainage.ParcelCaptureMilliseconds:F3} durationScope=capture+analysis+fingerprints-excludes-log-output-and-clock-overhead"), this);
        Debug.Log(DrainageDelivery.Summary(), this);
        if (owner.excludeSmallDisconnectedSeas || owner.infillExcludedInlandBasins)
            Debug.Log(FormattableString.Invariant($"[GeodesicBasinInfillDrainage] enabled={owner.infillExcludedInlandBasins} closedBasinTerminations={TinDiagnostics.ClosedBasinTerminations} tinyNoiseTerminations={TinDiagnostics.NoiseDepressionTerminations} primaryOceanRunoff={DrainageDelivery.PrimaryOcean:R} secondaryOceanRunoff={DrainageDelivery.SecondaryOcean:R} unattributedOceanRunoff={DrainageDelivery.UnattributedOcean:R} inlandTrappedRunoff={DrainageDelivery.InlandClosed:R} unresolvedRunoff={DrainageDelivery.Unresolved:R} rawBasins={TinLakeBasins?.Basins.Count ?? 0} renderedLakes={TinLakeGeometry?.Bodies.Count ?? 0} riverNetworks={TinDiagnostics.RiverNetworks} unexpectedBrokenNetworks={TinDiagnostics.UnexpectedBrokenNetworks} unexpectedInlandBreaks={TinDiagnostics.UnexpectedInlandBreaks} conservationResidual={DrainageDelivery.Residual:R} materialUphillSegments={TinDiagnostics.VisibleMaterialUphillSegments}"), this);
        Debug.Log(DrainageDelivery.Connectivity(TinDiagnostics.UnexpectedBrokenNetworks), this);
        foreach (string line in DrainageDelivery.CatchmentLogs(CarvingEvidence)) Debug.Log(line, this);
        try
        {
            var mapping = owner.GeodesicCurrentRenderMapping;
            bool hasMapping = mapping != null && mapping.SampleCount == TinDrainage.BasinVertexCount;
            var topology = owner.GeodesicTopology;
            bool hasExcludedCells = owner.OceanConnectivity != null && owner.OceanConnectivity.ExcludedCells > 0;
            Func<int, bool> excludedAtVertex = hasExcludedCells && hasMapping
                ? vertex => owner.IsGeodesicCellPotentialLakeBasin(mapping.Samples[vertex].NearestCell) : null;
            Func<int, bool> initiallyBelowSeaAtVertex = hasMapping && owner.OceanConnectivity != null
                ? vertex => owner.IsGeodesicCellBelowSeaLevel(mapping.Samples[vertex].NearestCell) : null;
            var classifiedCells = hasExcludedCells && topology != null && TinDrainage.BasinOnSphere
                ? new GeodesicBasinOverflowAudit.ClassifiedCells(topology, owner.IsGeodesicCellPotentialLakeBasin,
                    vertex => hasMapping ? mapping.Samples[vertex].NearestCell : 0) : null;
            var overflow = GeodesicBasinOverflowAudit.Analyze(TinDrainage, TinLakeBasins.Basins, DrainageDelivery,
                TinLakeGeometry, excludedAtVertex, hasExcludedCells && !hasMapping, classifiedCells, owner.BasePlanetRadius, initiallyBelowSeaAtVertex);
            Debug.Log(overflow.Summary(), this);
            Debug.Log(overflow.WitnessSummary(), this);
            foreach (string line in overflow.Opportunities) Debug.Log(line, this);
            foreach (string line in overflow.FreshwaterCandidates) Debug.Log(line, this);
            if (owner.excludeSmallDisconnectedSeas || owner.infillExcludedInlandBasins)
            {
                try
                {
                    foreach (string line in GeodesicInfillDrainageAudit.Logs(owner, TinDrainage, TinLakeBasins, DrainageDelivery,
                        TinLakeRouting, surfaceMesh.vertices, deliveryOceanComponentByVertex)) Debug.Log(line, this);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GeodesicInfillDrainageAudit] unavailable={ex.GetType().Name} detail={ex.Message}", this);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GeodesicBasinOverflowAudit] unavailable={ex.GetType().Name} detail={ex.Message}", this);
        }
        finally
        {
            if (!UseSedimentCorridors) deliveryOceanComponentByVertex = null;
            TinDrainage.ReleaseParcelDestinations();
        }
    }

    private string CarvingEvidence(TinBasinDescriptor basin)
    {
        if (basin == null) return "unknown-no-basin-match";
        if (ValleyCarving == null) return "no-carving-pass";
        // Last-pass IDs are floor vertex IDs, but topology may have changed after cutting.
        // Report historical evidence, never assert that it proves a final failure's cause.
        var evidence = new List<string>();
        foreach (var c in ValleyCarving.Candidates)
            if (c.BasinId == basin.Id)
                evidence.Add(FormattableString.Invariant($"last-pass:{c.Rejection ?? "accepted"},coastal:{c.CoastalAttempt},plannedMaxCut:{c.MaximumCut:R},preCarveBarrier:{c.OriginalSpillBarrier:R}"));
        return evidence.Count == 0 ? "unknown-no-last-pass-candidate" : "[" + string.Join("|", evidence) + ";finalCause=unproven]";
    }

    private IEnumerable<PlanetGenerationProgress> InitializeCoreStages(PlanetGenerator owner, IcosphereRenderGeometry geometry,
        Mesh surfaceMesh, Action<GeodesicCarvingCheckpoint, byte[], int> captureSnapshot)
    {
        Clear();
        if (owner == null || owner.CurrentGridType != PlanetGridType.GeodesicIcosphere ||
            owner.GeodesicTopology == null || surfaceMesh == null) yield break;
        planet = owner; topology = owner.GeodesicTopology; sourceTerrainMesh = surfaceMesh;
        bool willCarve = owner.enableTerminationValleyCarving && !owner.GeodesicValleyCarvingAttempted &&
            owner.hydrologyTerrainMode == HydrologyTerrainMode.Visible && tinFilterStrength == 0f;
        byte[] checkpointKey = null;
        bool[] inputOcean = null;
        Vector3[] input = null;
        GeodesicHydrologyTopology inputOceanTopology = null;
        int passes = Mathf.Clamp(owner.valleyCarvingPasses, 1, 5);
        float carveDepth = GeodesicValleyCarving.Normalize(owner.maximumValleyCarveDepth, GeodesicValleyCarving.DefaultDepth, 1f);
        float carveWidth = GeodesicValleyCarving.Normalize(owner.valleyCarveWidth, GeodesicValleyCarving.DefaultWidth, 1f);
        double carveRunoff = SimulationMeanCellArea(owner) *
            GeodesicValleyCarving.Normalize(owner.minimumValleyCarveRunoff, GeodesicValleyCarving.DefaultRunoff, 1000000f);
        if (willCarve && captureSnapshot != null)
        {
            yield return new PlanetGenerationProgress(PlanetGenerationStage.CheckingSavedPlanet);
            input = surfaceMesh.vertices;
            inputOceanTopology = GeodesicHydrologyTopology.Build(geometry);
            inputOcean = owner.GeodesicInfillOceanMask ??
                owner.MapGeodesicRenderOcean(inputOceanTopology, input, owner.GeodesicCurrentRenderMapping);
            double epsilon = RoutingEpsilon(owner.BasePlanetRadius, uphillTolerance);
            checkpointKey = GeodesicCarvingCheckpoint.BuildKey(owner, geometry, input, inputOcean,
                passes, carveDepth, carveWidth, carveRunoff, SimulationMeanCellArea(owner) * Math.Max(.01f, riverFlowThreshold),
                Mathf.Max(0f, uniformRunoffPerArea), epsilon, Mathf.Clamp(visibleMicroBumpFraction, 0f, .002f));
            string path = GeodesicCarvingCheckpoint.PathFor(checkpointKey);
            if (System.IO.File.Exists(path))
            {
                yield return new PlanetGenerationProgress(PlanetGenerationStage.LoadingSavedPlanet);
                var snapshot = GeodesicCarvingCheckpoint.Load(path, checkpointKey, input, inputOcean,
                    geometry.TriangleCount, passes, passes * ((double)carveDepth + epsilon) + .00001d);
                if (snapshot != null)
                {
                    yield return new PlanetGenerationProgress(PlanetGenerationStage.RestoringSavedPlanet);
                    var restoreWatch = System.Diagnostics.Stopwatch.StartNew();
                    long managedBefore = GC.GetTotalMemory(false);
                    if (owner.TryRestoreGeodesicCarvingSurface(geometry, surfaceMesh, input, snapshot))
                    {
                        Debug.Log(FormattableString.Invariant($"[GeodesicPlanetCache] restored skippedCarvingPasses={passes} restoreMs={restoreWatch.Elapsed.TotalMilliseconds:F2} restoreManagedHeapDeltaBytes={GC.GetTotalMemory(false) - managedBefore}"), this);
                        yield return new PlanetGenerationProgress(PlanetGenerationStage.RoutingRivers);
                        foreach (var progress in InitializeRenderHydrologyStages(owner, geometry, snapshot.Positions,
                            System.Diagnostics.Stopwatch.StartNew(), snapshot.Ocean, oceanTopology: inputOceanTopology))
                            yield return progress;
                        LogCarvingPassRebuild(snapshot.Passes[passes - 1], passes, passes);
                        CompleteCarving(snapshot.Passes, collectDelivery ? snapshot.InitialCatchments : null, carveRunoff);
                        yield break;
                    }
                }
            }
            else Debug.Log($"[GeodesicPlanetCache] miss path={path}", this);
        }
        yield return new PlanetGenerationProgress(PlanetGenerationStage.RoutingRivers);
        foreach (var progress in InitializeRenderHydrologyStages(owner, geometry, input ?? surfaceMesh.vertices,
            System.Diagnostics.Stopwatch.StartNew(), inputOcean, intermediateCarving: willCarve,
            oceanTopology: inputOceanTopology)) yield return progress;
        if (!owner.enableTerminationValleyCarving)
        { retainedCarvingOcean = null; carvingTopology = null; Debug.Log("[GeodesicValleyCarving] enabled=False terrainChanged=False", this); yield break; }
        if (owner.GeodesicValleyCarvingAttempted) yield break;
        owner.GeodesicValleyCarvingAttempted = true;
        if (owner.hydrologyTerrainMode != HydrologyTerrainMode.Visible || tinFilterStrength != 0f)
        {
            Debug.LogWarning("[GeodesicValleyCarving] enabled=True skipped=requires-visible-terrain-and-zero-TIN-filter terrainChanged=False", this);
            yield break;
        }
        var results = new List<GeodesicValleyCarving.Result>(passes);
        var frozenOcean = retainedCarvingOcean;
        var initialHeights = (float[])TinDrainage.VisibleHeight.Clone();
        var initialCatchments = collectDelivery || checkpointKey != null
            ? GeodesicOceanDrainagePlan.CaptureCatchments(TinDrainage, TinLakeBasins) : null;
        for (int pass = 1; pass <= passes; pass++)
        {
            yield return new PlanetGenerationProgress(PlanetGenerationStage.CarvingValleys, pass, passes);
            LogCarvingHydrology($"pass-{pass}-of-{passes}-before");
            // Only raw basin topology guides erosion. Future lake eligibility must not
            // supply protected footprints, lake entries or lake-outlet targets.
            var result = GeodesicValleyCarving.Plan(TinDrainage, carvingTopology, frozenOcean,
                null, null, owner.BasePlanetRadius, owner.GeodesicSeaLevelRadius,
                carveDepth, carveRunoff, carveWidth,
                TinLakeBasins, surfaceMesh.vertices, visibleMicroBumpFraction);
            // Existing incision setting is per pass. Its cumulative limit is the original
            // completed surface minus pass * depth; no new excavation allowance is added.
            for (int v = 0; v < result.Heights.Length; v++)
                if (initialHeights[v] - (double)result.Heights[v] > pass * (double)carveDepth + pass * TinDrainage.Epsilon)
                    throw new InvalidOperationException("Cumulative valley excavation limit exceeded.");
            if (result.AffectedVertices > 0) owner.ApplyGeodesicValleySurface(geometry, surfaceMesh, result.Heights);
            results.Add(result);
            Debug.Log(result.Summary(pass, passes), this);
            if (pass == passes && checkpointKey != null)
            {
                var snapshot = owner.CaptureGeodesicCarvingSurface(surfaceMesh);
                snapshot.Ocean = frozenOcean;
                snapshot.Passes = results;
                snapshot.InitialCatchments = initialCatchments;
                captureSnapshot(snapshot, checkpointKey, geometry.TriangleCount);
            }
            // Rebuild even when no cuts were accepted. The next plan must observe fresh
            // TIN flow, accumulation, networks and raw basin descriptors. Only the rebuild
            // after the last carve qualifies lakes, integrates them and creates visuals.
            Clear(); planet = owner; topology = owner.GeodesicTopology; sourceTerrainMesh = surfaceMesh;
            foreach (var progress in InitializeRenderHydrologyStages(owner, geometry, surfaceMesh.vertices, System.Diagnostics.Stopwatch.StartNew(),
                frozenOcean, intermediateCarving: pass < passes)) yield return progress;
            LogCarvingPassRebuild(result, pass, passes);
        }
        CompleteCarving(results, collectDelivery ? initialCatchments : null, carveRunoff);
    }

    private void LogCarvingPassRebuild(GeodesicValleyCarving.Result result, int pass, int passes)
    {
        LogCarvingHydrology($"pass-{pass}-of-{passes}-after");
        Debug.Log(result.CoastalRebuildSummary(TinDrainage, pass, passes), this);
        Debug.Log(result.OceanRebuildSummary(TinDrainage, pass, passes), this);
    }

    private void CompleteCarving(List<GeodesicValleyCarving.Result> results,
        IReadOnlyList<GeodesicOceanDrainagePlan.Catchment> initialCatchments, double carveRunoff)
    {
        ValleyCarvingResults = results.AsReadOnly();
        ValleyCarving = results[results.Count - 1];
        if (initialCatchments != null && DrainageDelivery != null)
        {
            Debug.Log(GeodesicOceanDrainagePlan.FinalCatchmentSummary(initialCatchments, TinDrainage, DrainageDelivery, carveRunoff), this);
            var chainParcels = new HashSet<int>();
            foreach (var result in results) chainParcels.UnionWith(result.CompleteMultiChainParcels());
            Debug.Log(GeodesicOceanDrainagePlan.FinalChainSummary(chainParcels, TinDrainage, DrainageDelivery), this);
        }
        retainedCarvingOcean = null; carvingTopology = null;
    }

    private void LogCarvingHydrology(string stage)
    {
        var d = TinDiagnostics;
        Debug.Log($"[GeodesicValleyCarving] hydrology={stage} networks={d.RiverNetworks} oceanTerminations={d.OceanTerminations} closedBasinTerminations={d.ClosedBasinTerminations} " +
            $"meanLength={d.MeanNetworkLength:G6} maxLength={d.MaxNetworkLength:G6} confluences={d.Confluences} unexpectedBroken={d.UnexpectedBrokenNetworks} " +
            $"unexpectedInland={d.UnexpectedInlandBreaks} rawBasins={TinLakeBasins?.Basins.Count ?? 0} " +
            $"basinTopology={(intermediateTinCarving ? "intermediate-carving" : "final")} renderedLakes={TinLakeGeometry?.Bodies.Count ?? 0}", this);
    }

    /// <summary>Null restores uniform density; otherwise input is a nonnegative volume/time per cell.
    /// Recalculates discharge/widths only. Reuses receivers, areas, anchors, terrain, and refined paths.</summary>
    public void UpdateRunoff(double[] localRunoff)
    {
        if (TinDrainage != null)
        {
            if (localRunoff != null && localRunoff.Length == planet.GeodesicTopology.CellCount)
                localRunoff = MapSimulationRunoffToTriangles(localRunoff);
            collectDelivery = logDrainageDelivery;
            TinDrainage.CaptureParcelDestinations = collectDelivery || UseSedimentCorridors;
            try
            {
                TinDrainage.Accumulate(Mathf.Max(0f, uniformRunoffPerArea), localRunoff);
                RebuildRenderHydrologyVisuals();
            }
            finally { collectDelivery = false; TinDrainage.ReleaseParcelDestinations(); }
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
        Mesh surface = filter != null ? filter.sharedMesh : null;
        if (surface != null && surface == sedimentTerrainMesh) surface = sourceTerrainMesh;
        Initialize(owner, IcosphereRenderGeometryCache.GetOrBuild(owner.geodesicRenderSubdivisionLevel), surface);
    }

    [ContextMenu("Apply Uniform Runoff")]
    private void ApplyUniformRunoff() => UpdateRunoff(null);

    [ContextMenu("Refresh River Thresholds And Visuals")]
    public void RebuildVisuals()
    {
        if (renderHydrology) { RebuildRenderHydrologyVisuals(extractedTinThreshold == ResolvedDrainageAreaThreshold); return; }
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

    private IEnumerable<PlanetGenerationProgress> InitializeRenderHydrologyStages(PlanetGenerator owner, IcosphereRenderGeometry geometry,
        Vector3[] completedSurface, System.Diagnostics.Stopwatch watch, bool[] frozenOcean = null,
        bool intermediateCarving = false, GeodesicHydrologyTopology oceanTopology = null)
    {
        renderHydrology = true;
        intermediateTinCarving = intermediateCarving;
        // This lightweight vertex adjacency is used ONLY for retained-ocean component mapping.
        // River geometry, gradients, connectivity and runoff all use the render triangles.
        oceanTopology ??= GeodesicHydrologyTopology.Build(geometry);
        bool[] ocean = frozenOcean ?? owner.GeodesicInfillOceanMask ?? owner.MapGeodesicRenderOcean(oceanTopology, completedSurface, owner.GeodesicCurrentRenderMapping);
        retainedCarvingOcean = ocean; carvingTopology = oceanTopology;
        if (!intermediateCarving && owner.generateHydrologicalLakes) corridorTopology = oceanTopology;
        if ((collectDelivery || UseSedimentCorridors || owner.RiverShoreline != null) && !intermediateCarving)
        {
            var hashWatch = System.Diagnostics.Stopwatch.StartNew();
            deliveryOceanMaskHash = GeodesicGenerationFingerprint.Mask(ocean);
            deliveryOceanComponentByVertex = UseSedimentCorridors || owner.RiverShoreline != null ||
                owner.excludeSmallDisconnectedSeas || owner.infillExcludedInlandBasins
                ? LabelOceanComponents(owner, oceanTopology, ocean) : null;
            deliveryMaskHashMilliseconds = hashWatch.Elapsed.TotalMilliseconds;
        }
        var visible = new float[geometry.VertexCount];
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        for (int i = 0; i < visible.Length; i++)
        { visible[i] = completedSurface[i].magnitude; min = Mathf.Min(min, visible[i]); max = Mathf.Max(max, visible[i]); }
        // Numerical precision only, capped independently of terrain bump allowance.
        double epsilon = RoutingEpsilon(owner.BasePlanetRadius, uphillTolerance);
        double bump = (max - (double)min) * Mathf.Clamp(visibleMicroBumpFraction, 0f, .002f);
        meanCellArea = SimulationMeanCellArea(owner);
        var terrainSettings = owner.GetGeodesicTerrainSettings();
        float[] hydrologyTerrainHeight = GeodesicHydrologyTerrain.Build(geometry.UnitVertices, visible, ocean,
            owner.DerivedTerrainSeed, terrainSettings, owner.BasePlanetRadius,
            owner.hydrologyTerrainMode, owner.enableGeodesicTerrainDisplacement,
            owner.GeodesicHydrologicalRenderRadii, owner.GeodesicRawRenderRadii, owner.GeodesicInfilledRenderVertices);
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
                completedSurface: completedSurface, hydrologyTerrainHeights: hydrologyTerrainHeight,
                captureParcelDestinations: CaptureGenerationParcels(owner, intermediateCarving), requireSeaSphereCrossing: intermediateCarving);
        else
            TinDrainage = GeodesicTinHydrology.BuildAdaptive(geometry.UnitVertices, geometry.Triangles, visible, ocean,
            owner.GeodesicSeaLevelRadius, owner.BasePlanetRadius, epsilon, bump, tinFilterStrength,
            ResolvedDrainageAreaThreshold, Mathf.Max(0f, uniformRunoffPerArea), geometry.SubdivisionLevel, completedSurface: completedSurface,
            captureParcelDestinations: CaptureGenerationParcels(owner, intermediateCarving), requireSeaSphereCrossing: intermediateCarving);
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
        // BuildAdaptive already computes raw flow, accumulation and networks. Intermediate
        // passes need that data and basin topology, but no shoreline routing or ribbon meshes.
        topologyMilliseconds = watch.Elapsed.TotalMilliseconds;
        if (intermediateTinCarving)
        {
            TinLakeBasins = BuildUnqualifiedTinBasins();
            Debug.Log("[GeodesicTIN] basinTopology=intermediate-carving lakeConstruction=deferred lakeRouting=deferred riverVisuals=deferred " +
                TinLakeBasins.Summary(), this);
        }
        else
            foreach (var progress in RebuildRenderHydrologyVisualStages()) yield return progress;
        Debug.Log($"[GeodesicTIN] simulationSubdivision={owner.GeodesicTopology.SubdivisionLevel} renderSubdivision={geometry.SubdivisionLevel} " +
            $"vertices={geometry.VertexCount} configuredThreshold={riverFlowThreshold:G6} resolvedAreaThreshold={ResolvedDrainageAreaThreshold:G9} " +
            $"epsilon={TinDrainage.Epsilon:G9} visibleMicroBumpThreshold={TinDrainage.VisibleBumpTolerance:G9} " +
            $"heightSource={owner.hydrologyTerrainMode} routing=continuous-facet-gradient rawDebug=" + drawRawTrianglePaths, this);
        Debug.Log(TinDiagnostics.Summary(), this);
        if (!intermediateTinCarving)
            Debug.Log("[GeodesicTIN] Lake routing uses exact shoreline entries and unique TIN runoff parcels.", this);
    }

    internal static double RoutingEpsilon(float radius, float tolerance) =>
        Math.Min(Math.Max(0f, tolerance), Math.Min(.000002d, radius * .00000025d));

    private static double SimulationMeanCellArea(PlanetGenerator owner) =>
        4d * Math.PI * owner.BasePlanetRadius * owner.BasePlanetRadius / owner.GeodesicTopology.CellCount;

    private bool CaptureGenerationParcels(PlanetGenerator owner, bool intermediate) =>
        intermediate || collectDelivery || UseSedimentCorridors || (owner.enableTerminationValleyCarving &&
            owner.hydrologyTerrainMode == HydrologyTerrainMode.Visible && tinFilterStrength == 0f);

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

    [ContextMenu("Refresh TIN Lake River Integration Only")]
    public void RefreshTinLakeRiverIntegration() { if (renderHydrology) RebuildRenderHydrologyVisuals(true); }

    private void RebuildRenderHydrologyVisuals(bool reuseRawNetworks = false)
    {
        foreach (var progress in RebuildRenderHydrologyVisualStages(reuseRawNetworks)) { }
    }

    private static int[] LabelOceanComponents(PlanetGenerator owner, GeodesicHydrologyTopology graph, bool[] ocean)
    {
        var mapping = owner.GeodesicCurrentRenderMapping;
        return mapping != null && mapping.SampleCount == ocean.Length && owner.OceanConnectivity != null
            ? GeodesicVisibleHydrology.LabelRetainedComponents(graph, ocean, mapping, owner.OceanConnectivity) : null;
    }

    private int DeliveryOceanComponent(TinPoint endpoint) =>
        GeodesicVisibleHydrology.OceanComponentAt(TinDrainage, deliveryOceanComponentByVertex, endpoint);

    private IEnumerable<PlanetGenerationProgress> RebuildRenderHydrologyVisualStages(bool reuseRawNetworks = false)
    {
        if (intermediateTinCarving) yield break;
        ClearVisual(); paths.Clear(); mouths.Clear();
        visibleReaches = riverCells = riverMouths = 0;
        if (TinDrainage == null || planet == null) yield break;
        if (colourRenderedRiversByVisibleTerrainGrade || showRiverTerminationReasons)
        {
            VisibleGradeAudit = new GeodesicRiverVisibleGradeAudit();
            riverTerminationPoints = new List<RiverTerminationDebugPoint>();
        }
        rejectedUphill = showRejectedUphillRiverSegments ? new List<RiverUphillDebugReach>() : null;
        if (!reuseRawNetworks)
            yield return new PlanetGenerationProgress(PlanetGenerationStage.RoutingRivers);
        if (!reuseRawNetworks)
        {
            TinDrainage.ExtractNetworks(ResolvedDrainageAreaThreshold, planet.BasePlanetRadius,
                showRejectedUphillRiverSegments ? CaptureRejectedTinPath : (Action<int, TinPoint[]>)null);
            extractedTinThreshold = ResolvedDrainageAreaThreshold;
            TinLakeRouting = null;
            DrainageDelivery = null;
            SedimentCorridors = null;
        }
        if (planet.generateHydrologicalLakes) EnsureRoutingLakeGeometry();
        if (integrateTinLakeRivers && planet.generateHydrologicalLakes && routingLakeGeometry.Bodies.Count > 0)
        {
            if (TinLakeRouting == null)
            {
                yield return new PlanetGenerationProgress(PlanetGenerationStage.RoutingLakes);
                TinLakeRouting = GeodesicTinLakeRouting.Build(TinDrainage, routingLakeBasins, routingLakeGeometry, ResolvedDrainageAreaThreshold);
            }
        }
        else TinLakeRouting = null;
        var delivery = ResolveSedimentCorridors();
        if (planet.generateHydrologicalLakes) yield return new PlanetGenerationProgress(PlanetGenerationStage.FillingLakes);
        RefreshTinLakeVisuals();
        if (delivery == null && collectDelivery)
            delivery = GeodesicDrainageDelivery.Analyze(TinDrainage, routingLakeBasins ?? TinLakeBasins, TinLakeRouting?.Lakes, TinLakeRouting?.RiverReaches,
                TinLakeRouting == null && TinLakeGeometry != null && TinLakeGeometry.Bodies.Count > 0,
                deliveryOceanComponentByVertex != null ? DeliveryOceanComponent : (Func<TinPoint, int>)null,
                planet.OceanConnectivity?.PrimaryRetainedComponent ?? -1);
        if (delivery != null) DrainageDelivery = delivery;
        RefreshSedimentSurface();
        riverColourDelivery = DrainageDelivery;
        if (ColourRiversByUltimateDestination && riverColourDelivery == null)
            riverColourDelivery = AnalyzeRiverColours();
        BeginOutletAudit();
        yield return new PlanetGenerationProgress(PlanetGenerationStage.BuildingRiverRibbons);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        double threshold = ResolvedDrainageAreaThreshold;
        for (int t = 0; t < RiverStrength.Length; t++) RiverStrength[t] = (float)(TinDrainage.AccumulatedRunoff[t] / threshold);
        var vertices = new List<Vector3>(); var indices = new List<int>(); var colors = new List<Color>();
        var rendered = new bool[TinDrainage.Networks.Count];
        var corridorReceivers = CorridorWidthReceivers();
        for (int id = 0; id < TinDrainage.Networks.Count; id++)
        {
            var network = TinDrainage.Networks[id]; Vector3[] raw = network.Centreline;
            var lakeReach = TinLakeRouting != null ? TinLakeRouting.RiverReaches[id] : null;
            bool lakeAffected = lakeReach != null && (lakeReach.Changed || TinLakeRouting.HasAddedDischarge(id));
            if (lakeAffected) raw = TinLakeRouting.Directions(lakeReach.Samples);
            if (raw.Length < 2) { delivery?.ObserveShortLink(lakeReach != null && lakeReach.ReceivingLakeId >= 0); continue; }
            // Resample all crossed TIN facets after float conversion, interpolation and
            // projection. Optional aesthetics must retain this validated raw fallback.
            if (!terrain.IsCentrelineValid(raw, TinDrainage.Epsilon, TinDrainage.VisibleBumpTolerance))
            {
                TinDiagnostics.RejectedPostProcessing++; TinDiagnostics.UnexpectedBrokenNetworks++;
                delivery?.ObserveVisualBreak(id, "centreline");
                TinDiagnostics.UnexpectedInlandBreaks++;
                CaptureRejectedPath(network.SourceTriangle, raw, RiverDebugRejection.CentrelineGrade); continue;
            }
            Vector3[] path = raw;
            if (!drawRawTrianglePaths && !lakeAffected)
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
                    delivery?.ObserveVisualBreak(id, "projection");
                    CaptureRejectedPath(network.SourceTriangle, projectedDirections, RiverDebugRejection.ProjectionGrade); continue; }
            }
            paths[network.SourceTriangle] = path;
            double[] discharge = null;
            if (!drawRawTrianglePaths)
            {
                var samples = lakeReach?.Samples ?? network.Samples;
                var joinedNetwork = lakeReach?.JunctionNetwork ?? network.JunctionNetwork;
                discharge = DistinctReachDischarge(samples, lakeAffected ? TinLakeRouting.RawReachDischarge(id) : null, joinedNetwork >= 0);
                if (corridorReceivers != null && corridorReceivers.TryGetValue(id, out var inflows))
                    foreach (var inflow in inflows)
                    {
                        int join = Array.IndexOf(raw, inflow.Point);
                        // A terminal catchment's total can be larger than the receiving
                        // facet's flow. Keep that lower bound downstream without adding
                        // the same parcels twice to the existing facet accumulation.
                        for (int i = join; join >= 0 && i < discharge.Length; i++)
                            discharge[i] = Math.Max(discharge[i], inflow.Runoff);
                    }
                if (path != raw) discharge = InterpolateReachDischarge(discharge, path.Length);
            }
            int firstVertex = vertices.Count, firstIndex = indices.Count;
            bool lakeInlet = lakeReach != null && lakeReach.ReceivingLakeId >= 0;
            string route = "network:" + id;
            var termination = lakeReach?.Termination ?? network.Termination;
            if (!lakeInlet && termination == TinTermination.OceanCoast)
            {
                var coastSamples = lakeReach?.Samples ?? network.Samples;
                FitOceanOutlet(route, coastSamples[coastSamples.Length - 1], ref path, ref discharge);
            }
            float? endLevel = lakeInlet ? lakeSediment?.Level(lakeReach.ReceivingLakeId, lakeReach.Samples[lakeReach.Samples.Length - 1]) : null;
            if (drawRawTrianglePaths) AddRawTinLine(path, vertices, indices, colors, endLevel: endLevel);
            else AddTinRibbon(path, network.SourceTriangle, vertices, indices, colors, discharge, lakeInlet && !endLevel.HasValue, endLevel: endLevel);
            OutletAudit?.Record(route, vertices, firstVertex, drawRawTrianglePaths);
            VisibleGradeAudit?.ObserveEmittedPath(network.SourceTriangle, vertices, firstVertex, path.Length,
                drawRawTrianglePaths, firstIndex, terrain, planet.BasePlanetRadius);
            visibleReaches++; rendered[id] = true;
            RecordRiverColour("network:" + id, firstVertex, vertices.Count);
            if (!reuseRawNetworks && TinLakeRouting == null && network.Termination == TinTermination.Confluence && network.JunctionNetwork >= 0 && !rendered[network.JunctionNetwork])
            {
                TinDiagnostics.Confluences--; TinDiagnostics.UnexpectedBrokenNetworks++; TinDiagnostics.UnexpectedInlandBreaks++;
                delivery?.ObserveVisualBreak(id, "missing-trunk");
                if (SedimentCorridors == null) { network.Termination = TinTermination.RoutingFailure; network.JunctionNetwork = -1; }
            }
            if (riverTerminationPoints != null)
            {
                int finalCentre = firstVertex + (path.Length - 1) * (drawRawTrianglePaths ? 1 : 3) + (drawRawTrianglePaths ? 0 : 1);
                riverTerminationPoints.Add(new RiverTerminationDebugPoint(network.SourceTriangle, network.Termination, vertices[finalCentre]));
            }
            if (termination == TinTermination.OceanCoast && !lakeInlet)
            {
                var coastSamples = lakeReach?.Samples ?? network.Samples;
                int terminalTriangle = coastSamples[coastSamples.Length - 1].Triangle;
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
        var corridorInletLakes = new HashSet<int>();
        if (SedimentCorridors != null)
            foreach (var connection in SedimentCorridors.Connections.Values) corridorInletLakes.UnionWith(connection.InletLakes);
        var renderedLakeOutlets = new HashSet<int>();
        if (TinLakeRouting != null)
        {
            var crossings = lakeSediment != null ? GeodesicTinLakeCrossings.Build(TinDrainage, TinLakeRouting, lakeSediment, rendered) : null;
            foreach (var lake in TinLakeRouting.Lakes)
            {
                if (lake.OutletReachId < 0 && crossings?.VisibleOutlets.Contains(lake.BasinId) != true && !corridorInletLakes.Contains(lake.BasinId)) continue;
                var path = TinLakeRouting.Directions(lake.OutletPath);
                if (path.Length < 2) continue;
                string route = "lake:" + lake.BasinId;
                double[] discharge = drawRawTrianglePaths ? null : Array.ConvertAll(path, _ => lake.Discharge);
                if (lake.OutletTermination == TinTermination.OceanCoast && lake.DownstreamLakeId < 0 && lake.JoinNetwork < 0)
                    FitOceanOutlet(route, lake.OutletPath[lake.OutletPath.Length - 1], ref path, ref discharge);
                float? startLevel = lakeSediment?.Level(lake.BasinId, lake.OutletPath[0]);
                float? endLevel = lake.DownstreamLakeEntry.HasValue ? lakeSediment?.Level(lake.DownstreamLakeId, lake.DownstreamLakeEntry.Value) : null;
                int firstVertex = vertices.Count;
                if (drawRawTrianglePaths) AddRawTinLine(path, vertices, indices, colors, startLevel, endLevel);
                else AddTinRibbon(path, lake.OutletPath[0].Triangle, vertices, indices, colors,
                    discharge, lake.DownstreamLakeId >= 0 && lake.JoinNetwork < 0 && !endLevel.HasValue, !startLevel.HasValue, startLevel, endLevel);
                OutletAudit?.Record(route, vertices, firstVertex, drawRawTrianglePaths);
                visibleReaches++;
                renderedLakeOutlets.Add(lake.BasinId);
                RecordRiverColour("lake:" + lake.BasinId, firstVertex, vertices.Count);
            }
            if (crossings != null)
            {
                foreach (var crossing in crossings.Paths)
                {
                    float strength = (float)(crossing.Discharge / threshold);
                    var widthProfile = WidthProfile;
                    float width = widthProfile.Width(crossing.Discharge);
                    float inletWidth = widthProfile.Width(crossing.DischargeAtInlet);
                    Color color = strength * riverFlowThreshold >= Math.Max(riverFlowThreshold, majorRiverThreshold)
                        ? Color.Lerp(riverColor, Color.cyan, .3f) : riverColor;
                    int firstIndex = indices.Count, firstVertex = vertices.Count;
                    GeodesicTinLakeCrossings.AddRibbon(crossing, inletWidth / crossing.Bed.Level, width / crossing.Bed.Level,
                        Mathf.Max(.000001f, surfaceOffset), color, drawRawTrianglePaths, vertices, indices, colors);
                    if (indices.Count > firstIndex)
                    {
                        visibleReaches++;
                        OutletAudit?.RecordBed("bed:" + crossing.Bed.Id, vertices, firstVertex, indices, firstIndex,
                            crossing, Mathf.Max(.000001f, surfaceOffset), drawRawTrianglePaths);
                    }
                    else crossings.Fail(crossing.Bed.Id, "interior path produced no visible ribbon geometry");
                    RecordRiverColour("lake:" + crossing.Bed.Id, firstVertex, vertices.Count);
                }
                LogSediment(crossings);
            }
            Debug.Log(TinLakeRouting.Summary(), this);
        }
        DrawSedimentCorridors(rendered, renderedLakeOutlets, vertices, indices, colors);
        CompleteOutletAudit();
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
            normalRiverColours = colors;
            ApplyRiverColours();
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

    private void AddRawTinLine(Vector3[] path, List<Vector3> vertices, List<int> indices, List<Color> colors,
        float? startLevel = null, float? endLevel = null, float minimumRadius = 0)
    {
        int first = vertices.Count;
        for (int i = 0; i < path.Length; i++)
        {
            float? level = i == 0 ? startLevel : i == path.Length - 1 ? endLevel : null;
            vertices.Add(path[i] * (Mathf.Max(minimumRadius, level ?? RenderRadius(path[i])) + Mathf.Max(.000001f, surfaceOffset))); colors.Add(riverColor);
            if (i > 0) { indices.Add(first + i - 1); indices.Add(first + i); }
        }
    }

    private double[] DistinctReachDischarge(TinPoint[] samples, double[] discharge, bool joinsRiver)
    {
        // At a confluence the final facet already contains the trunk. Preserve
        // the incoming tributary's flow until its exact contact with that trunk.
        int join = samples.Length;
        if (joinsRiver)
        {
            join--;
            while (join > 0 && samples[join - 1].Triangle == samples[join].Triangle) join--;
            if (join == 0) join = 1;
        }
        var result = new List<double>(samples.Length);
        Vector3 previous = default;
        for (int i = 0; i < samples.Length; i++)
        {
            int flowSample = Math.Min(i, join - 1);
            double flow = discharge != null ? discharge[flowSample] : TinDrainage.AccumulatedRunoff[samples[flowSample].Triangle];
            var direction = TinDrainage.Direction(samples[i]);
            if (i == 0 || !previous.Equals(direction)) result.Add(flow);
            else result[result.Count - 1] = Math.Max(result[result.Count - 1], flow);
            previous = direction;
        }
        return result.ToArray();
    }

    private static double[] InterpolateReachDischarge(double[] discharge, int count)
    {
        // Same three samples per segment as the display-only centreline resampler.
        var result = new double[count];
        for (int i = 1; i < discharge.Length; i++)
            for (int step = 0; step < 3; step++)
                result[(i - 1) * 3 + step] = discharge[i - 1] + (discharge[i] - discharge[i - 1]) * (step / 3d);
        result[count - 1] = discharge[discharge.Length - 1];
        return result;
    }

    private void AddTinRibbon(Vector3[] path, int source, List<Vector3> vertices, List<int> indices, List<Color> colors,
        double[] discharge = null, bool shorelineEnd = false, bool spillStart = false, float? startLevel = null, float? endLevel = null,
        float minimumRadius = 0)
    {
        // A short lake-to-lake reach needs an interior cross-section when both ends
        // taper to exact shore points; two collapsed caps alone have zero area.
        if (path.Length == 2 && shorelineEnd && spillStart)
        {
            path = new[] { path[0], (path[0] + path[1]).normalized, path[1] };
            if (discharge != null) discharge = new[] { discharge[0], (discharge[0] + discharge[1]) * .5, discharge[1] };
        }
        var widthProfile = WidthProfile;
        double flow = discharge == null ? TinDrainage.AccumulatedRunoff[source] : 0d;
        int first = vertices.Count;
        for (int i = 0; i < path.Length; i++)
        {
            double runoff = discharge != null ? discharge[i] : TinDrainage.AccumulatedRunoff[terrain.FindTriangle(path[i])];
            flow = Math.Max(flow, runoff);
            float width = widthProfile.Width(flow);
            Vector3 forward = path[Math.Min(i + 1, path.Length - 1)] - path[Math.Max(0, i - 1)];
            Vector3 side = Vector3.Cross(path[i], forward).normalized;
            float angle = width / Mathf.Max(.001f, RenderRadius(path[i]));
            Vector3 left = (path[i] - side * angle * .5f).normalized;
            Vector3 right = (path[i] + side * angle * .5f).normalized;
            // A zero-width cap meets the exact shoreline/spill witness; banks cannot
            // extend past that endpoint into the water.
            if ((shorelineEnd && i == path.Length - 1) || (spillStart && i == 0)) left = right = path[i];
            float? level = i == 0 ? startLevel : i == path.Length - 1 ? endLevel : null;
            float offset = Mathf.Max(.000001f, surfaceOffset);
            // Three columns retain the EXACT checked centreline as mesh vertices. Projecting
            // unequal banks must not move the river's middle onto a different routing path.
            float centreRadius = level ?? RenderRadius(path[i]);
            // At a sediment shore the cross-section can straddle raised dry
            // terrain. Keep its banks above that terrain and its centre on the seam.
            float bankMinimum = Mathf.Max(minimumRadius, level.GetValueOrDefault());
            vertices.Add(left * (Mathf.Max(bankMinimum, RenderRadius(left)) + offset));
            vertices.Add(path[i] * (Mathf.Max(minimumRadius, centreRadius) + offset));
            vertices.Add(right * (Mathf.Max(bankMinimum, RenderRadius(right)) + offset));
            Color color = flow >= meanCellArea * Math.Max(riverFlowThreshold, majorRiverThreshold)
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

    // The detector retains every raw depression regardless of its thresholds. A finite
    // unreachable depth defers all lake eligibility without changing basin detection or
    // reading the configured lake thresholds. Floor/spill/parent/downstream data is intact.
    private GeodesicTinBasins BuildUnqualifiedTinBasins() =>
        GeodesicTinBasins.Build(TinDrainage, minimumAreaFraction: 0d, minimumDepth: double.MaxValue);

    private void EnsureRoutingLakeGeometry()
    {
        if (routingLakeGeometry != null) return;
        routingLakeBasins = GeodesicTinBasins.Build(TinDrainage, 0, 0);
        routingLakeGeometry = GeodesicTinLakeGeometry.Build(TinDrainage, routingLakeBasins);
    }

    private void RefreshSedimentSurface()
    {
        ClearSedimentSurface();
        if (sourceTerrainMesh == null) return;
        if (TinLakeRouting != null) lakeSediment = new GeodesicTinLakeSediment(TinDrainage, routingLakeGeometry, TinLakeGeometry);
        Mesh corridorMesh = BuildCorridorSurface();
        sedimentTerrainMesh = lakeSediment?.BuildSurface(corridorMesh ?? sourceTerrainMesh) ?? corridorMesh;
        if (corridorMesh != sedimentTerrainMesh) Release(corridorMesh);
        var filter = planet.GetComponent<MeshFilter>();
        if (sedimentTerrainMesh != null && filter != null && filter.sharedMesh == sourceTerrainMesh)
            filter.sharedMesh = sedimentTerrainMesh;
        ApplySedimentCollider();
    }

    // Also called after generation assigns its base collider, which may use a different
    // subdivision. The clipped terrain uses identical facets for rendering and collision.
    internal void ApplySedimentCollider()
    {
        if (sedimentTerrainMesh == null) return;
        var collider = planet.GetComponent<MeshCollider>();
        if (collider == null || collider.sharedMesh == null || collider.sharedMesh == sedimentTerrainMesh) return;
        sourceColliderMesh = collider.sharedMesh;
        collider.sharedMesh = sedimentTerrainMesh;
    }

    private void ClearSedimentSurface()
    {
        if (sedimentTerrainMesh != null && planet != null)
        {
            var filter = planet.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh == sedimentTerrainMesh) filter.sharedMesh = sourceTerrainMesh;
            var collider = planet.GetComponent<MeshCollider>();
            if (collider != null && collider.sharedMesh == sedimentTerrainMesh) collider.sharedMesh = sourceColliderMesh;
        }
        Release(sedimentTerrainMesh);
        sedimentTerrainMesh = sourceColliderMesh = null;
        lakeSediment = null;
        corridorTerrain = null;
    }

    private void LogSediment(GeodesicTinLakeCrossings crossings)
    {
        var delivery = DrainageDelivery;
        // Both presentations share this one delivery result from the authoritative graph.
        string ocean = delivery == null ? "oceanRunoff=unavailable" : FormattableString.Invariant(
            $"originalPrimaryOcean={delivery.PrimaryOcean:R} finalPrimaryOcean={delivery.PrimaryOcean:R} originalTotalOcean={delivery.DirectOcean + delivery.ViaLakesOcean:R} finalTotalOcean={delivery.DirectOcean + delivery.ViaLakesOcean:R}");
        Debug.Log($"[GeodesicTinLakeSediment] routedLakes={TinLakeRouting.Lakes.Count} visibleLakes={TinLakeGeometry.Bodies.Count} sedimentLakes={lakeSediment.LakeCount} " +
            $"directCrossings={crossings.Direct} interiorCrossings={crossings.Interior} multipleInletJunctions={crossings.Junctions} " +
            $"failedOrUnsupportedCrossings={crossings.Failures.Count} logicalConnectionsPreserved={crossings.LogicalConnections} remainingVisualGaps={crossings.Gaps} {ocean}", this);
        foreach (string failure in crossings.Failures) Debug.LogWarning("[GeodesicTinLakeSediment] " + failure, this);
    }

    [ContextMenu("Refresh TIN Lake Surfaces Only")]
    public void RefreshTinLakeVisuals()
    {
        if (intermediateTinCarving || !renderHydrology || TinDrainage == null || planet == null) return;
        if (lakeVisualRoot != null) lakeVisualRoot.SetActive(false);
        Release(lakeVisualRoot); Release(lakeMesh); Release(lakeMaterial);
        lakeVisualRoot = null; lakeMesh = null; lakeMaterial = null;
        TinLakeBasins = null; TinLakeGeometry = null;
        if (!planet.generateHydrologicalLakes)
        {
            TinLakeBasins = routingLakeBasins?.WithVisibility(0, double.MaxValue) ?? BuildUnqualifiedTinBasins();
            Debug.Log("[GeodesicTIN] basinTopology=final lakeConstruction=disabled " + TinLakeBasins.Summary(), this);
            return;
        }
        EnsureRoutingLakeGeometry();
        TinLakeBasins = routingLakeBasins.WithVisibility(planet.minimumLakeBasinAreaFraction, planet.minimumLakeDepth);
        Debug.Log("[GeodesicTIN] basinTopology=final lakeConstruction=final " + TinLakeBasins.Summary(), this);
        TinLakeGeometry = TinLakeBasins == routingLakeBasins ? routingLakeGeometry : GeodesicTinLakeGeometry.Build(TinDrainage, TinLakeBasins);
        Debug.Log(TinLakeGeometry.Summary() + " lakeConstruction=final", this);
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
    private void AddRibbon(Vector3[] path, int cell, List<Vector3> vertices, List<int> triangles, List<Color> colors)
    {
        double runoff = TinDrainage != null ? TinDrainage.AccumulatedRunoff[cell] : Drainage.AccumulatedRunoff[cell];
        float width = WidthProfile.Width(runoff);
        Color color = runoff >= meanCellArea * Math.Max(riverFlowThreshold, majorRiverThreshold)
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

    private void OnDrawGizmos()
    {
        if (planet == null || !planet.showValleyCarvingDebug || ValleyCarving == null || terrain == null) return;
        float offset = Mathf.Max(surfaceOffset * 3, planet.BasePlanetRadius * .0005f);
        Vector3 Project(Vector3 p) => transform.TransformPoint(p.normalized * (terrain.Radius(p.normalized) + offset));
        float marker = planet.BasePlanetRadius * .0015f * transform.lossyScale.x;
        foreach (var result in ValleyCarvingResults)
        foreach (var c in result.Candidates)
        {
            Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.TransformPoint(c.Origin), marker);
            if (c.Path.Length == 0) { Gizmos.color = Color.red; Gizmos.DrawWireSphere(Project(c.Origin), marker * 1.4f); continue; }
            Gizmos.color = c.Accepted ? Color.green : Color.red;
            for (int i = 1; i < c.Path.Length; i++) Gizmos.DrawLine(Project(c.Path[i - 1]), Project(c.Path[i]));
            Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(Project(c.TargetPoint), marker);
        }
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
        ClearSedimentSurface();
        SedimentCorridors = null; corridorTopology = shorelineTopology = null;
        sourceTerrainMesh = null;
        routingLakeBasins = null; routingLakeGeometry = null; extractedTinThreshold = double.NaN;
        ValleyCarving = null; retainedCarvingOcean = null; carvingTopology = null;
        DrainageDelivery = null; deliveryOceanMaskHash = null; deliveryMaskHashMilliseconds = 0;
        deliveryOceanComponentByVertex = null;
        ValleyCarvingResults = Array.Empty<GeodesicValleyCarving.Result>();
        if (lakeVisualRoot != null) lakeVisualRoot.SetActive(false);
        Release(lakeVisualRoot); Release(lakeMesh); Release(lakeMaterial); lakeVisualRoot = null; lakeMesh = null; lakeMaterial = null;
        GradeDiagnostics = null;
        Lakes = null; LakeGeometry = null; TinLakeBasins = null; TinLakeGeometry = null; TinLakeRouting = null; reachPlans.Clear(); lakeMilliseconds = 0d;
        LakeConnectedReaches = LakeBridgedReaches = LakeInlets = LakeOutlets = SuppressedProjectionMismatch = SuppressedCorridorFailure = UnrenderedSmallDepression = TrueTopologyFailure = InlandVisibleTerminations = OceanConnectedChains = 0;
        ClearVisual(); paths.Clear(); mouths.Clear(); Drainage = null; TinDrainage = null; tinSimulationCells = null; terrain = null; topology = null; planet = null;
        ChannelAnchors = Array.Empty<Vector3>(); RiverStrength = Array.Empty<float>();
        renderHydrology = false; intermediateTinCarving = false; routingHeights = null;
        HydrologyTerrainSummary = null; HydrologyTerrainInputHeights = null;
        landCells = riverCells = visibleReaches = suppressedUphillReaches = riverMouths = unresolvedSinks = filledCells = majorBasinCells = 0;
        topologyMilliseconds = visualMilliseconds = 0d;
    }
    private void ClearVisual()
    {
        riverColourSpans.Clear();
        normalRiverColours = destinationRiverColours = null;
        riverColourDelivery = null;
        if (terminationDebugRoot != null) terminationDebugRoot.SetActive(false);
        Release(terminationDebugRoot); Release(terminationDebugMesh); Release(terminationDebugMaterial);
        terminationDebugRoot = null; terminationDebugMesh = null; terminationDebugMaterial = null;
        VisibleGradeAudit = null; riverTerminationPoints = null;
        OutletAudit = null;
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
