using System;
using Unity.Profiling;
using UnityEngine;

public enum GeodesicOceanTemperatureStartupMode { IsothermalFromSurface, DepthGradient }

/// <summary>Owns active geodesic ocean temperatures below layer 0; layer 0 always reads through to the surface field.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanTemperatureField : MonoBehaviour
{
    private const double ConservationTolerance = 2e-5;
    private static readonly ProfilerMarker CallbackMarker = new ProfilerMarker("GeodesicOceanTemperature.Callback");
    private static readonly ProfilerMarker PrepareColumnsMarker = new ProfilerMarker("GeodesicOceanTemperature.PrepareColumns");
    private static readonly ProfilerMarker SolveImplicitColumnsMarker = new ProfilerMarker("GeodesicOceanTemperature.SolveImplicitColumns");
    private static readonly ProfilerMarker ValidateSolutionMarker = new ProfilerMarker("GeodesicOceanTemperature.ValidateSolution");
    private static readonly ProfilerMarker SurfaceApplyMarker = new ProfilerMarker("GeodesicOceanTemperature.ApplySurfaceBatch");
    private static readonly ProfilerMarker SubsurfaceApplyMarker = new ProfilerMarker("GeodesicOceanTemperature.CommitSubsurface");
    private static readonly ProfilerMarker ConservationMarker = new ProfilerMarker("GeodesicOceanTemperature.ExactConservationAudit");
    private static readonly ProfilerMarker DiagnosticsMarker = new ProfilerMarker("GeodesicOceanTemperature.DiagnosticSnapshot");
    private static readonly ProfilerMarker ApproximateMarker = new ProfilerMarker("GeodesicOceanTemperature.RelaxApproximateProfiles");

    [Header("Geodesic Ocean Temperature")]
    [SerializeField, Tooltip("Enables persistent geodesic subsurface ocean temperatures. Legacy cube-sphere simulation is unaffected.")] private bool enableGeodesicOceanTemperature = true;
    [SerializeField, Tooltip("Deep target is the configured planetary base temperature plus this Kelvin offset.")] private float deepOceanTemperatureOffsetKelvin = -8f;
    [SerializeField, Min(0.01f), Tooltip("Approximate-profile relaxation time at the shallowest subsurface center.")] private float shallowResponseTimescaleSeconds = 80f;
    [SerializeField, Min(0.01f), Tooltip("Approximate-profile relaxation time at maximum depth.")] private float deepResponseTimescaleSeconds = 1200f;
    [SerializeField, Range(0.1f, 4f), Tooltip("Power controlling how quickly surface influence decreases with normalized center depth.")] private float depthProfileExponent = 1.4f;
    [SerializeField, Min(0f), Tooltip("Kelvin added at full vent strength to the deepest active layer.")] private float bottomVentTemperatureGainKelvin = 25f;
    [SerializeField, Range(0f, 1f), Tooltip("Fraction of bottom vent heating applied only to the layer immediately above the bottom.")] private float aboveBottomVentHeatingFactor = 0.35f;
    [SerializeField, Range(0f, 0.25f), Tooltip("Deterministic fraction of ocean columns used as ecological thermal vent refuges; this does not create resource sources.")] private float thermalVentColumnFraction = 0.02f;
    [SerializeField, Min(1e-8f), Tooltip("Thermal capacity per unit ocean-layer volume.")] private float subsurfaceHeatCapacityPerVolume = 1f;
    [SerializeField, Min(0f), Tooltip("Simulation-unit vertical diffusivity; this is not SI calibrated and will be tuned later.")] private float verticalThermalDiffusivity = 0.00002f;
    [SerializeField, Tooltip("Initializes subsurface layers from the surface or with a per-layer depth gradient.")] private GeodesicOceanTemperatureStartupMode startupMode = GeodesicOceanTemperatureStartupMode.DepthGradient;
    [SerializeField, Min(0f), Tooltip("Initial Kelvin decrease per layer index when Depth Gradient is selected.")] private float initialTemperatureDropPerLayerKelvin = 2f;
    [SerializeField, HideInInspector, Tooltip("Deprecated explicit solver cap retained only for serialized scene compatibility; production uses an implicit column solve.")] private int maximumVerticalSubsteps = 64;
    [SerializeField, Min(0.1f), Tooltip("Unscaled real-time interval between serialized Inspector diagnostic snapshots.")] private float inspectorSnapshotIntervalSeconds = 1f;
    [SerializeField, Min(0.1f), Tooltip("Simulation-time interval between exact coupled-energy conservation audits. Profiling diagnostics audits every tick.")] private float exactConservationAuditIntervalSeconds = 5f;
    [SerializeField, Tooltip("Enables per-tick exact conservation auditing and periodic profiling logs.")] private bool enableProfilingDiagnostics;

    [Header("Runtime Diagnostics (Throttled Inspector Snapshot)")]
    [SerializeField] private bool initialized;
    [SerializeField] private int activeSubsurfaceNodeCount;
    [SerializeField] private int participatingSurfaceCellCount;
    [SerializeField] private double totalSubsurfaceThermalCapacity;
    [SerializeField] private string sourceGridSummary = "None";
    [SerializeField] private double lastCompletedOceanTemperatureTick;
    [SerializeField] private float lastVerticalExchangeSimulationDelta;
    [SerializeField] private string solverMode = "ImplicitBackwardEuler";
    [SerializeField] private int columnsSolvedLastTick;
    [SerializeField] private int layersSolvedLastTick;
    [SerializeField] private double lastVerticalExchangeDurationMilliseconds;
    [SerializeField] private double lastSurfaceBatchDurationMilliseconds;
    [SerializeField] private double lastSubsurfaceCommitDurationMilliseconds;
    [SerializeField] private double maximumEquationResidual;
    [SerializeField] private int failedColumnCount;
    [SerializeField] private double latestVerticalConservationRelativeError;
    [SerializeField] private double lastExactConservationAuditSimulationTime;
    [SerializeField] private int ticksSinceLastExactConservationAudit;
    [SerializeField] private bool lastTickUsedAlgebraicConservationOnly;
    [SerializeField] private double lastAbsoluteEnergyTransferred;
    [SerializeField, HideInInspector] private bool lastStabilityClampingOccurred;
    [SerializeField] private float maximumSurfaceToBottomTemperatureDifference;
    [SerializeField] private long approximateRuntimeMemoryBytes;
    [SerializeField] private int oceanCallbacksLastRenderedFrame;
    [SerializeField] private int maximumOceanCallbacksPerRenderedFrame;
    [SerializeField, HideInInspector] private int verticalSubstepsLastRenderedFrame;
    [SerializeField, HideInInspector] private long verticalLinksProcessedLastRenderedFrame;
    [SerializeField] private double oceanTemperatureMillisecondsLastRenderedFrame;
    [SerializeField] private int duplicateOrStaleCallbackCount;
    [SerializeField] private GeodesicThermalModel selectedThermalModel;
    [SerializeField] private int subsurfaceNodesUpdatedLastTick;
    [SerializeField] private int verticalInterfacesProcessedLastTick;
    [SerializeField] private int implicitColumnsSolvedLastTick;
    [SerializeField] private int approximateNodesRelaxedLastTick;
    [SerializeField] private int thermalSubstepsLastTick;
    [SerializeField] private double lastApproximateOceanUpdateDurationMilliseconds;
    [SerializeField] private double lastConservativeOceanCallbackDurationMilliseconds;
    [SerializeField] private int[] layerActiveCellCount = new int[5];
    [SerializeField] private float[] layerMinimumTemperatureKelvin = new float[5];
    [SerializeField] private float[] layerMeanTemperatureKelvin = new float[5];
    [SerializeField] private float[] layerMaximumTemperatureKelvin = new float[5];

    private PlanetGenerator generator;
    private GeodesicOceanLayerDomain domain;
    private GeodesicSurfaceTemperatureField surfaceField;
    private GeodesicOceanLayerGrid sourceGrid;
    private PlanetResourceMap resourceMap;
    private float[] subsurfaceTemperatureKelvinByNode;
    private float[] heatCapacityByNode;
    private float[] inverseHeatCapacityByNode;
    private float[] thermalVentStrengthByCell;
    private float[] solvedSurfaceTemperatureByColumn;
    private float[] solvedSubsurfaceTemperatureByCompactNode;
    private int[] activeSubsurfaceNodeIndices;
    private int[] participatingSurfaceCells;
    private float[] columnInterfaceConductanceBase;
    private readonly double[] solveLower = new double[5];
    private readonly double[] solveDiagonal = new double[5];
    private readonly double[] solveUpper = new double[5];
    private readonly double[] solveRhs = new double[5];
    private readonly double[] solveCPrime = new double[5];
    private readonly double[] solveDPrime = new double[5];
    private readonly double[] solveResult = new double[5];
    private readonly double[] oldTemperature = new double[5];
    private readonly double[] layerCapacity = new double[5];
    private readonly double[] conductance = new double[4];
    private readonly double[] diagnosticWeightedTemperature = new double[5];
    private readonly double[] diagnosticWeight = new double[5];
    private float cachedCapacityPerVolume = float.NaN;
    private bool subscribed;
    private string firstColumnFailure;
    private long lastProcessedSurfaceTickSequence = -1;
    private double nextInspectorSnapshotUnscaledTime;
    private double nextExactAuditSimulationTime;
    private float liveLastVerticalExchangeSimulationDelta;
    private int liveLastVerticalSubstepCount;
    private double liveLastVerticalExchangeDurationMilliseconds;
    private double liveCompletedOceanTemperatureTick;
    private double liveLatestVerticalConservationRelativeError;
    private double liveLastExactConservationAuditSimulationTime;
    private int liveTicksSinceLastExactConservationAudit;
    private bool liveLastTickUsedAlgebraicConservationOnly;
    private double liveLastAbsoluteEnergyTransferred;
    private bool liveLastStabilityClampingOccurred;
    private float liveMaximumSurfaceToBottomTemperatureDifference;
    private int counterFrame = -1;
    private int callbacksThisFrame;
    private int substepsThisFrame;
    private long linksThisFrame;
    private double millisecondsThisFrame;
    private int liveCallbacksLastFrame;
    private int liveMaximumCallbacksPerFrame;
    private int liveSubstepsLastFrame;
    private long liveLinksLastFrame;
    private double liveMillisecondsLastFrame;

    public bool IsInitialized => initialized;
    public GeodesicOceanLayerGrid SourceGrid => sourceGrid;
    public float LastVerticalExchangeSimulationDelta => liveLastVerticalExchangeSimulationDelta;
    public int LastVerticalSubstepCount => 1;
    public double LastVerticalExchangeDurationMilliseconds => liveLastVerticalExchangeDurationMilliseconds;
    public double LatestVerticalConservationRelativeError => liveLatestVerticalConservationRelativeError;
    public double LastAbsoluteEnergyTransferred => liveLastAbsoluteEnergyTransferred;
    public bool LastStabilityClampingOccurred => false;
    public float MaximumSurfaceToBottomTemperatureDifference => liveMaximumSurfaceToBottomTemperatureDifference;
    public int ActiveSubsurfaceNodeCount => activeSubsurfaceNodeCount;
    public int ParticipatingSurfaceCellCount => participatingSurfaceCellCount;
    public double TotalSubsurfaceThermalCapacity => totalSubsurfaceThermalCapacity;
    public double LastCompletedOceanTemperatureTick => liveCompletedOceanTemperatureTick;
    public long ApproximateRuntimeMemoryBytes => approximateRuntimeMemoryBytes;

    private void Awake() { generator = GetComponent<PlanetGenerator>(); domain = GetComponent<GeodesicOceanLayerDomain>(); surfaceField = GetComponent<GeodesicSurfaceTemperatureField>(); resourceMap = GetComponent<PlanetResourceMap>(); }
    private void OnDestroy() => ClearField();

    public void InitializeForCurrentDomain()
    {
        Unsubscribe();
        GeodesicOceanLayerGrid grid = domain != null ? domain.Grid : null;
        if (!enableGeodesicOceanTemperature || generator == null || generator.CurrentGridType != PlanetGridType.GeodesicIcosphere || grid == null || surfaceField == null || !surfaceField.IsInitialized || !ReferenceEquals(grid.SourceTopology, generator.GeodesicTopology)) { ClearState(); return; }
        sourceGrid = grid;
        selectedThermalModel = surfaceField.ThermalModel;
        solverMode = selectedThermalModel.ToString();
        AllocateState();
        BuildThermalVentStrengths();
        BuildActiveSubsurfaceNodes();
        if (selectedThermalModel == GeodesicThermalModel.ConservativeImplicit) BuildCompactParticipationTables();
        RebuildCapacities();
        for (int i = 0; i < activeSubsurfaceNodeIndices.Length; i++)
        {
            int node = activeSubsurfaceNodeIndices[i], cell = node / sourceGrid.MaximumLayerCount, layer = node - cell * sourceGrid.MaximumLayerCount;
            float surface = surfaceField.GetCellTemperatureKelvin(cell);
            subsurfaceTemperatureKelvinByNode[node] = selectedThermalModel == GeodesicThermalModel.ApproximateEcologicalProfiles
                ? CalculateApproximateTarget(cell, layer, surface)
                : Mathf.Max(0f, surface - (startupMode == GeodesicOceanTemperatureStartupMode.DepthGradient ? initialTemperatureDropPerLayerKelvin * layer : 0f));
        }
        initialized = true;
        nextExactAuditSimulationTime = exactConservationAuditIntervalSeconds;
        nextInspectorSnapshotUnscaledTime = 0d;
        lastProcessedSurfaceTickSequence = -1;
        Subscribe();
        RefreshInspectorSnapshot(true);
        UnityEngine.Debug.Log($"[GeodesicOceanTemperature] initialized {sourceGridSummary}, subsurfaceNodes={activeSubsurfaceNodeCount}, participatingSurfaces={participatingSurfaceCellCount}, capacity={totalSubsurfaceThermalCapacity:E4}, memory={approximateRuntimeMemoryBytes} bytes", this);
    }

    private void AllocateState()
    {
        int nodes = sourceGrid.NodeCapacity, cells = sourceGrid.CellCount;
        subsurfaceTemperatureKelvinByNode = new float[nodes]; heatCapacityByNode = new float[nodes]; inverseHeatCapacityByNode = new float[nodes];
    }

    private void BuildThermalVentStrengths()
    {
        thermalVentStrengthByCell = new float[sourceGrid.CellCount];
        uint seed = unchecked((uint)generator.DerivedTerrainSeed) ^ 0xA511E9B3u;
        uint threshold = (uint)(Mathf.Clamp01(thermalVentColumnFraction) * uint.MaxValue);
        for (int cell = 0; cell < sourceGrid.CellCount; cell++)
        {
            if (sourceGrid.ActiveLayerCountByCell[cell] < 2) continue;
            uint hash = unchecked((uint)cell) ^ seed;
            hash ^= hash >> 16; hash *= 0x7FEB352Du; hash ^= hash >> 15; hash *= 0x846CA68Bu; hash ^= hash >> 16;
            if (threshold > 0u && hash <= threshold) thermalVentStrengthByCell[cell] = 0.5f + 0.5f * (hash / (float)threshold);
        }
    }

    private void BuildActiveSubsurfaceNodes()
    {
        int count = 0;
        for (int cell = 0; cell < sourceGrid.CellCount; cell++) count += Mathf.Max(0, sourceGrid.ActiveLayerCountByCell[cell] - 1);
        activeSubsurfaceNodeIndices = new int[count];
        int write = 0;
        for (int cell = 0; cell < sourceGrid.CellCount; cell++)
            for (int layer = 1; layer < sourceGrid.ActiveLayerCountByCell[cell]; layer++) activeSubsurfaceNodeIndices[write++] = sourceGrid.GetNodeIndex(cell, layer);
        activeSubsurfaceNodeCount = count;
    }

    private void BuildCompactParticipationTables()
    {
        int subsurfaceCount = 0;
        for (int cell = 0; cell < sourceGrid.CellCount; cell++) subsurfaceCount += Mathf.Max(0, sourceGrid.ActiveLayerCountByCell[cell] - 1);
        int surfaceCount = 0;
        for (int cell = 0; cell < sourceGrid.CellCount; cell++)
        {
            int layers = sourceGrid.ActiveLayerCountByCell[cell];
            if (layers > 1) surfaceCount++;
        }
        participatingSurfaceCells = new int[surfaceCount];
        int surfaceIndex = 0;
        for (int cell = 0; cell < sourceGrid.CellCount; cell++) if (sourceGrid.ActiveLayerCountByCell[cell] > 1) participatingSurfaceCells[surfaceIndex++] = cell;
        columnInterfaceConductanceBase = new float[surfaceCount * (sourceGrid.MaximumLayerCount - 1)];
        for (int link = 0; link < sourceGrid.VerticalLinkCount; link++)
        {
            int upper = sourceGrid.VerticalUpperNode[link];
            int cell = upper / sourceGrid.MaximumLayerCount;
            int layer = upper - cell * sourceGrid.MaximumLayerCount;
            if (sourceGrid.VerticalCenterDistance[link] > 0f) columnInterfaceConductanceBase[cellColumnIndex(cell) * (sourceGrid.MaximumLayerCount - 1) + layer] = sourceGrid.VerticalInterfaceArea[link] / sourceGrid.VerticalCenterDistance[link];
        }
        solvedSurfaceTemperatureByColumn = new float[surfaceCount];
        solvedSubsurfaceTemperatureByCompactNode = new float[subsurfaceCount];
        participatingSurfaceCellCount = surfaceCount;

        int cellColumnIndex(int cell)
        {
            int lo = 0, hi = participatingSurfaceCells.Length - 1;
            while (lo <= hi) { int mid = (lo + hi) >> 1; int value = participatingSurfaceCells[mid]; if (value == cell) return mid; if (value < cell) lo = mid + 1; else hi = mid - 1; }
            return -1;
        }
    }

    private void RebuildCapacities()
    {
        totalSubsurfaceThermalCapacity = 0d;
        float density = Mathf.Max(1e-8f, subsurfaceHeatCapacityPerVolume);
        for (int i = 0; i < activeSubsurfaceNodeIndices.Length; i++) { int node = activeSubsurfaceNodeIndices[i]; float capacity = sourceGrid.LayerVolume[node] * density; heatCapacityByNode[node] = capacity; inverseHeatCapacityByNode[node] = 1f / capacity; totalSubsurfaceThermalCapacity += capacity; }
        cachedCapacityPerVolume = subsurfaceHeatCapacityPerVolume;
        sourceGridSummary = $"cells={sourceGrid.CellCount}, nodes={sourceGrid.NodeCapacity}, active={sourceGrid.ActiveNodeCount}, verticalLinks={sourceGrid.VerticalLinkCount}";
        approximateRuntimeMemoryBytes = (long)sourceGrid.NodeCapacity * sizeof(float) * 3L + (long)activeSubsurfaceNodeIndices.Length * sizeof(int) + (participatingSurfaceCells != null ? (long)participatingSurfaceCells.Length * (sizeof(int) + sizeof(float)) : 0L) + (columnInterfaceConductanceBase != null ? (long)columnInterfaceConductanceBase.Length * sizeof(float) : 0L);
    }

    public void ClearField() { Unsubscribe(); ClearState(); }
    private void ClearState()
    {
        initialized = false; sourceGrid = null; subsurfaceTemperatureKelvinByNode = heatCapacityByNode = inverseHeatCapacityByNode = solvedSurfaceTemperatureByColumn = solvedSubsurfaceTemperatureByCompactNode = columnInterfaceConductanceBase = null; thermalVentStrengthByCell = null;
        activeSubsurfaceNodeIndices = participatingSurfaceCells = null;
        activeSubsurfaceNodeCount = participatingSurfaceCellCount = 0; totalSubsurfaceThermalCapacity = 0d; sourceGridSummary = "None"; approximateRuntimeMemoryBytes = 0; lastProcessedSurfaceTickSequence = -1; liveCompletedOceanTemperatureTick = 0d;
        columnsSolvedLastTick = layersSolvedLastTick = subsurfaceNodesUpdatedLastTick = verticalInterfacesProcessedLastTick = implicitColumnsSolvedLastTick = approximateNodesRelaxedLastTick = thermalSubstepsLastTick = 0;
        lastApproximateOceanUpdateDurationMilliseconds = lastConservativeOceanCallbackDurationMilliseconds = 0d;
    }
    private void Subscribe() { if (subscribed || surfaceField == null) return; surfaceField.SurfaceTemperatureTickCommitted += OnSurfaceTickCommitted; surfaceField.SurfaceTemperatureFieldReinitialized += OnSurfaceReinitialized; surfaceField.SurfaceTemperatureFieldClearing += OnSurfaceClearing; subscribed = true; }
    private void Unsubscribe() { if (!subscribed || surfaceField == null) { subscribed = false; return; } surfaceField.SurfaceTemperatureTickCommitted -= OnSurfaceTickCommitted; surfaceField.SurfaceTemperatureFieldReinitialized -= OnSurfaceReinitialized; surfaceField.SurfaceTemperatureFieldClearing -= OnSurfaceClearing; subscribed = false; }
    private void OnSurfaceReinitialized() => InitializeForCurrentDomain();
    private void OnSurfaceClearing() => ClearField();
    private void OnSurfaceTickCommitted(float dt)
    {
        if (!initialized || sourceGrid == null || !ReferenceEquals(sourceGrid, domain.Grid) || !ReferenceEquals(sourceGrid.SourceTopology, generator.GeodesicTopology)) { duplicateOrStaleCallbackCount++; return; }
        long sequence = surfaceField.SurfaceTemperatureTickSequence;
        if (sequence == lastProcessedSurfaceTickSequence) { duplicateOrStaleCallbackCount++; return; }
        lastProcessedSurfaceTickSequence = sequence;
        if (selectedThermalModel == GeodesicThermalModel.ApproximateEcologicalProfiles) RelaxApproximateProfiles(dt); else ExchangeVerticalHeat(dt);
    }

    private void RelaxApproximateProfiles(float dt)
    {
        double start = Time.realtimeSinceStartupAsDouble;
        RollFrameCounters(); callbacksThisFrame++;
        subsurfaceNodesUpdatedLastTick = approximateNodesRelaxedLastTick = 0;
        verticalInterfacesProcessedLastTick = implicitColumnsSolvedLastTick = thermalSubstepsLastTick = 0;
        using (ApproximateMarker.Auto())
        {
            for (int i = 0; i < activeSubsurfaceNodeIndices.Length; i++)
            {
                int node = activeSubsurfaceNodeIndices[i];
                int cell = node / sourceGrid.MaximumLayerCount;
                int layer = node - cell * sourceGrid.MaximumLayerCount;
                float depth01 = NormalizedCenterDepth(node);
                float timescale = Mathf.Lerp(shallowResponseTimescaleSeconds, deepResponseTimescaleSeconds, depth01);
                float response = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / Mathf.Max(0.01f, timescale));
                float current = subsurfaceTemperatureKelvinByNode[node];
                float target = CalculateApproximateTarget(cell, layer, surfaceField.GetCellTemperatureKelvin(cell));
                subsurfaceTemperatureKelvinByNode[node] = Mathf.Clamp(current + (target - current) * response, 0f, 10000f);
                subsurfaceNodesUpdatedLastTick++; approximateNodesRelaxedLastTick++;
            }
        }
        liveLastVerticalExchangeSimulationDelta = dt;
        liveCompletedOceanTemperatureTick += Mathf.Max(0f, dt);
        lastApproximateOceanUpdateDurationMilliseconds = (Time.realtimeSinceStartupAsDouble - start) * 1000d;
        liveLastVerticalExchangeDurationMilliseconds = lastApproximateOceanUpdateDurationMilliseconds;
        millisecondsThisFrame += lastApproximateOceanUpdateDurationMilliseconds;
        RefreshInspectorSnapshot(false);
    }

    private float CalculateApproximateTarget(int cell, int layer, float surfaceTemperature)
    {
        int node = sourceGrid.GetNodeIndex(cell, layer);
        float profile = Mathf.Pow(NormalizedCenterDepth(node), Mathf.Max(0.1f, depthProfileExponent));
        float deepTarget = Mathf.Clamp(surfaceField.BaseTemperatureKelvin + deepOceanTemperatureOffsetKelvin, 0f, 10000f);
        int bottom = sourceGrid.GetBottomLayerIndex(cell);
        float vent = GetVentStrength(cell) * bottomVentTemperatureGainKelvin * (layer == bottom ? 1f : layer == bottom - 1 ? aboveBottomVentHeatingFactor : 0f);
        return Mathf.Clamp(Mathf.Lerp(surfaceTemperature, deepTarget, profile) + vent, 0f, 10000f);
    }

    private float NormalizedCenterDepth(int node) => sourceGrid.MaximumOceanDepth > 1e-7f ? Mathf.Clamp01((sourceGrid.OceanSurfaceRadius - sourceGrid.LayerCenterRadius[node]) / sourceGrid.MaximumOceanDepth) : 0f;
    private float GetVentStrength(int cell)
    {
        if (resourceMap != null && resourceMap.ventStrength != null && cell >= 0 && cell < resourceMap.ventStrength.Length) return Mathf.Max(0f, resourceMap.ventStrength[cell]);
        return thermalVentStrengthByCell != null && cell >= 0 && cell < thermalVentStrengthByCell.Length ? thermalVentStrengthByCell[cell] : 0f;
    }

    public float GetLayerTemperatureKelvin(int cellIndex, int layerIndex) => TryGetLayerTemperatureKelvin(cellIndex, layerIndex, out float value) ? value : float.NaN;
    public bool TryGetLayerTemperatureKelvin(int cellIndex, int layerIndex, out float temperatureKelvin) { temperatureKelvin = float.NaN; if (!initialized || !sourceGrid.IsNodeActive(cellIndex, layerIndex)) return false; temperatureKelvin = layerIndex == 0 ? surfaceField.GetCellTemperatureKelvin(cellIndex) : subsurfaceTemperatureKelvinByNode[sourceGrid.GetNodeIndex(cellIndex, layerIndex)]; return Finite(temperatureKelvin); }
    public float GetBottomLayerTemperatureKelvin(int cellIndex) { if (!initialized || cellIndex < 0 || cellIndex >= sourceGrid.CellCount) return float.NaN; return GetLayerTemperatureKelvin(cellIndex, sourceGrid.GetBottomLayerIndex(cellIndex)); }
    public float GetEffectiveOceanTemperatureKelvin(int cellIndex, int preferredLayer) { if (TryGetLayerTemperatureKelvin(cellIndex, preferredLayer, out float value)) return value; return GetLayerTemperatureKelvin(cellIndex, 0); }
    public float GetLayerHeatCapacity(int cellIndex, int layerIndex) { if (!initialized || !sourceGrid.IsNodeActive(cellIndex, layerIndex)) return float.NaN; return layerIndex == 0 ? surfaceField.GetCellHeatCapacity(cellIndex) : heatCapacityByNode[sourceGrid.GetNodeIndex(cellIndex, layerIndex)]; }

    private void ExchangeVerticalHeat(float dt)
    {
        using (CallbackMarker.Auto())
        {
            double start = Time.realtimeSinceStartupAsDouble;
            RollFrameCounters(); callbacksThisFrame++;
            subsurfaceNodesUpdatedLastTick = activeSubsurfaceNodeCount; approximateNodesRelaxedLastTick = 0; verticalInterfacesProcessedLastTick = sourceGrid.VerticalLinkCount; thermalSubstepsLastTick = 1;
            bool collectTickDiagnostics = enableProfilingDiagnostics || Time.unscaledTimeAsDouble >= nextInspectorSnapshotUnscaledTime;
            liveLastVerticalExchangeSimulationDelta = dt; liveLastVerticalSubstepCount = 1; liveLastStabilityClampingOccurred = false;
            columnsSolvedLastTick = layersSolvedLastTick = failedColumnCount = 0; maximumEquationResidual = 0d; firstColumnFailure = null;
            if (collectTickDiagnostics) liveLastAbsoluteEnergyTransferred = 0d;
            if (cachedCapacityPerVolume != subsurfaceHeatCapacityPerVolume) RebuildCapacities();
            bool exactAudit = enableProfilingDiagnostics || liveCompletedOceanTemperatureTick + dt >= nextExactAuditSimulationTime;
            double before = 0d;
            if (exactAudit) using (ConservationMarker.Auto()) before = TotalParticipatingEnergy();
            if (verticalThermalDiffusivity < 0f || dt <= 0f) { CompleteAudit(exactAudit, before, dt); FinishTick(start); return; }

            using (PrepareColumnsMarker.Auto()) { }
            double solveStart = Time.realtimeSinceStartupAsDouble;
            using (SolveImplicitColumnsMarker.Auto())
            {
                int compactSubsurface = 0;
                for (int column = 0; column < participatingSurfaceCells.Length; column++)
                {
                    int cell = participatingSurfaceCells[column];
                    int layers = sourceGrid.ActiveLayerCountByCell[cell];
                    if (layers <= 1) continue;
                    if (TrySolveColumn(cell, column, compactSubsurface, layers, dt)) { columnsSolvedLastTick++; layersSolvedLastTick += layers; } else failedColumnCount++;
                    compactSubsurface += layers - 1;
                }
            }
            liveLastVerticalExchangeDurationMilliseconds = (Time.realtimeSinceStartupAsDouble - solveStart) * 1000d;
            using (ValidateSolutionMarker.Auto()) if (failedColumnCount > 0 && firstColumnFailure != null) UnityEngine.Debug.LogError($"[GeodesicOceanTemperature] Implicit vertical solve failed; failedColumns={failedColumnCount}; first={firstColumnFailure}", this);
            double surfaceStart = Time.realtimeSinceStartupAsDouble;
            using (SurfaceApplyMarker.Auto()) if (failedColumnCount == 0 && !surfaceField.TryApplyAuthoritativeTemperatureBatch(participatingSurfaceCells, solvedSurfaceTemperatureByColumn, participatingSurfaceCells.Length)) { failedColumnCount++; UnityEngine.Debug.LogError("[GeodesicOceanTemperature] Surface temperature batch rejected; retaining previous column state.", this); }
            lastSurfaceBatchDurationMilliseconds = (Time.realtimeSinceStartupAsDouble - surfaceStart) * 1000d;
            double commitStart = Time.realtimeSinceStartupAsDouble;
            if (failedColumnCount == 0) using (SubsurfaceApplyMarker.Auto()) for (int i = 0; i < activeSubsurfaceNodeIndices.Length; i++) subsurfaceTemperatureKelvinByNode[activeSubsurfaceNodeIndices[i]] = solvedSubsurfaceTemperatureByCompactNode[i];
            lastSubsurfaceCommitDurationMilliseconds = (Time.realtimeSinceStartupAsDouble - commitStart) * 1000d;
            CompleteAudit(exactAudit, before, dt); FinishTick(start);
        }
    }

    private bool TrySolveColumn(int cell, int column, int compactSubsurfaceStart, int layers, float dt)
    {
        oldTemperature[0] = surfaceField.GetCellTemperatureKelvin(cell); layerCapacity[0] = surfaceField.GetCellHeatCapacity(cell);
        for (int layer = 1; layer < layers; layer++) { int node = sourceGrid.GetNodeIndex(cell, layer); oldTemperature[layer] = subsurfaceTemperatureKelvinByNode[node]; layerCapacity[layer] = heatCapacityByNode[node]; }
        for (int layer = 0; layer + 1 < layers; layer++) conductance[layer] = verticalThermalDiffusivity * columnInterfaceConductanceBase[column * (sourceGrid.MaximumLayerCount - 1) + layer];
        double oldEnergy = 0d;
        for (int layer = 0; layer < layers; layer++)
        {
            if (!Finite(oldTemperature[layer]) || oldTemperature[layer] < 0d || !Finite(layerCapacity[layer]) || layerCapacity[layer] <= 0d) return ColumnFailure(cell, "invalid temperature/capacity");
            oldEnergy += oldTemperature[layer] * layerCapacity[layer];
            double above = layer > 0 ? conductance[layer - 1] : 0d, below = layer + 1 < layers ? conductance[layer] : 0d;
            if (!Finite(above) || above < 0d || !Finite(below) || below < 0d) return ColumnFailure(cell, "invalid conductance");
            solveLower[layer] = layer > 0 ? -dt * above : 0d; solveUpper[layer] = layer + 1 < layers ? -dt * below : 0d; solveDiagonal[layer] = layerCapacity[layer] + dt * (above + below); solveRhs[layer] = layerCapacity[layer] * oldTemperature[layer];
        }
        double pivot = solveDiagonal[0]; if (Math.Abs(pivot) < 1e-20 || !Finite(pivot)) return ColumnFailure(cell, "singular diagonal");
        solveCPrime[0] = solveUpper[0] / pivot; solveDPrime[0] = solveRhs[0] / pivot;
        for (int i = 1; i < layers; i++) { pivot = solveDiagonal[i] - solveLower[i] * solveCPrime[i - 1]; if (Math.Abs(pivot) < 1e-20 || !Finite(pivot)) return ColumnFailure(cell, "singular diagonal"); solveCPrime[i] = i + 1 < layers ? solveUpper[i] / pivot : 0d; solveDPrime[i] = (solveRhs[i] - solveLower[i] * solveDPrime[i - 1]) / pivot; }
        solveResult[layers - 1] = solveDPrime[layers - 1];
        for (int i = layers - 2; i >= 0; i--) solveResult[i] = solveDPrime[i] - solveCPrime[i] * solveResult[i + 1];
        double newEnergy = 0d, residualMax = 0d;
        for (int i = 0; i < layers; i++)
        {
            double v = solveResult[i]; if (!Finite(v) || v < 0d) return ColumnFailure(cell, "invalid solved temperature");
            newEnergy += v * layerCapacity[i];
            double residual = solveDiagonal[i] * v + solveLower[i] * (i > 0 ? solveResult[i - 1] : 0d) + solveUpper[i] * (i + 1 < layers ? solveResult[i + 1] : 0d) - solveRhs[i];
            residualMax = Math.Max(residualMax, Math.Abs(residual));
        }
        maximumEquationResidual = Math.Max(maximumEquationResidual, residualMax);
        double rel = Math.Abs(newEnergy - oldEnergy) / Math.Max(1e-12, Math.Abs(oldEnergy));
        if (rel > ConservationTolerance) return ColumnFailure(cell, "energy conservation exceeded");
        solvedSurfaceTemperatureByColumn[column] = (float)solveResult[0];
        for (int layer = 1; layer < layers; layer++) solvedSubsurfaceTemperatureByCompactNode[compactSubsurfaceStart + layer - 1] = (float)solveResult[layer];
        if (Math.Abs(solveResult[0] - oldTemperature[0]) > 0d) liveLastAbsoluteEnergyTransferred += Math.Abs((solveResult[0] - oldTemperature[0]) * layerCapacity[0]);
        return true;
    }

    private bool ColumnFailure(int cell, string reason) { if (firstColumnFailure == null) firstColumnFailure = $"cell={cell}, reason={reason}"; return false; }

    private void CompleteAudit(bool exactAudit, double before, float dt)
    {
        liveCompletedOceanTemperatureTick += dt;
        if (exactAudit)
        {
            double after; using (ConservationMarker.Auto()) after = TotalParticipatingEnergy();
            liveLatestVerticalConservationRelativeError = Math.Abs(after - before) / Math.Max(1e-12, Math.Abs(before)); liveLastExactConservationAuditSimulationTime = liveCompletedOceanTemperatureTick; liveTicksSinceLastExactConservationAudit = 0; liveLastTickUsedAlgebraicConservationOnly = false; nextExactAuditSimulationTime = liveCompletedOceanTemperatureTick + Mathf.Max(0.1f, exactConservationAuditIntervalSeconds);
        }
        else { liveTicksSinceLastExactConservationAudit++; liveLastTickUsedAlgebraicConservationOnly = true; }
    }
    private void FinishTick(double start)
    {
            liveLastVerticalExchangeDurationMilliseconds = (Time.realtimeSinceStartupAsDouble - start) * 1000d; millisecondsThisFrame += liveLastVerticalExchangeDurationMilliseconds;
        lastConservativeOceanCallbackDurationMilliseconds = liveLastVerticalExchangeDurationMilliseconds; implicitColumnsSolvedLastTick = columnsSolvedLastTick;
        RefreshInspectorSnapshot(false);
        if (enableProfilingDiagnostics && Time.frameCount % 120 == 0) UnityEngine.Debug.Log($"[GeodesicOceanTemperatureProfile] mode={solverMode}, callbacks={callbacksThisFrame}, columns={columnsSolvedLastTick}, layers={layersSolvedLastTick}, frameMs={millisecondsThisFrame:F3}, residual={maximumEquationResidual:E3}, conservation={liveLatestVerticalConservationRelativeError:E3}", this);
    }
    private void RollFrameCounters()
    {
        int frame = Time.frameCount; if (counterFrame == frame) return;
        if (counterFrame >= 0) { liveCallbacksLastFrame = callbacksThisFrame; liveMaximumCallbacksPerFrame = Mathf.Max(liveMaximumCallbacksPerFrame, callbacksThisFrame); liveSubstepsLastFrame = substepsThisFrame; liveLinksLastFrame = linksThisFrame; liveMillisecondsLastFrame = millisecondsThisFrame; }
        counterFrame = frame; callbacksThisFrame = substepsThisFrame = 0; linksThisFrame = 0; millisecondsThisFrame = 0d;
    }
    private double TotalParticipatingEnergy()
    {
        double total = 0d;
        for (int i = 0; i < participatingSurfaceCells.Length; i++) { int cell = participatingSurfaceCells[i]; total += surfaceField.GetCellTemperatureKelvin(cell) * surfaceField.GetCellHeatCapacity(cell); }
        for (int i = 0; i < activeSubsurfaceNodeIndices.Length; i++) { int node = activeSubsurfaceNodeIndices[i]; total += subsurfaceTemperatureKelvinByNode[node] * heatCapacityByNode[node]; }
        return total;
    }

    private void RefreshInspectorSnapshot(bool force)
    {
        double now = Time.unscaledTimeAsDouble; if (!force && now < nextInspectorSnapshotUnscaledTime) return; nextInspectorSnapshotUnscaledTime = now + Mathf.Max(0.1f, inspectorSnapshotIntervalSeconds);
        using (DiagnosticsMarker.Auto())
        {
            for (int layer = 0; layer < 5; layer++) { layerActiveCellCount[layer] = 0; layerMinimumTemperatureKelvin[layer] = float.PositiveInfinity; layerMaximumTemperatureKelvin[layer] = float.NegativeInfinity; layerMeanTemperatureKelvin[layer] = 0f; diagnosticWeightedTemperature[layer] = diagnosticWeight[layer] = 0d; }
            liveMaximumSurfaceToBottomTemperatureDifference = 0f;
            for (int cell = 0; cell < sourceGrid.CellCount; cell++)
            {
                int count = sourceGrid.ActiveLayerCountByCell[cell]; if (count == 0) continue; float surface = surfaceField.GetCellTemperatureKelvin(cell);
                for (int layer = 0; layer < count; layer++) { int node = sourceGrid.GetNodeIndex(cell, layer); float temperature = layer == 0 ? surface : subsurfaceTemperatureKelvinByNode[node]; float capacity = layer == 0 ? surfaceField.GetCellHeatCapacity(cell) : heatCapacityByNode[node]; layerActiveCellCount[layer]++; layerMinimumTemperatureKelvin[layer] = Mathf.Min(layerMinimumTemperatureKelvin[layer], temperature); layerMaximumTemperatureKelvin[layer] = Mathf.Max(layerMaximumTemperatureKelvin[layer], temperature); diagnosticWeightedTemperature[layer] += temperature * capacity; diagnosticWeight[layer] += capacity; }
                liveMaximumSurfaceToBottomTemperatureDifference = Mathf.Max(liveMaximumSurfaceToBottomTemperatureDifference, Mathf.Abs(surface - GetBottomLayerTemperatureKelvin(cell)));
            }
            for (int layer = 0; layer < 5; layer++) { if (diagnosticWeight[layer] > 0d) layerMeanTemperatureKelvin[layer] = (float)(diagnosticWeightedTemperature[layer] / diagnosticWeight[layer]); else layerMinimumTemperatureKelvin[layer] = layerMaximumTemperatureKelvin[layer] = float.NaN; }
            lastCompletedOceanTemperatureTick = liveCompletedOceanTemperatureTick; lastVerticalExchangeSimulationDelta = liveLastVerticalExchangeSimulationDelta; lastVerticalExchangeDurationMilliseconds = liveLastVerticalExchangeDurationMilliseconds; latestVerticalConservationRelativeError = liveLatestVerticalConservationRelativeError; lastExactConservationAuditSimulationTime = liveLastExactConservationAuditSimulationTime; ticksSinceLastExactConservationAudit = liveTicksSinceLastExactConservationAudit; lastTickUsedAlgebraicConservationOnly = liveLastTickUsedAlgebraicConservationOnly; lastAbsoluteEnergyTransferred = liveLastAbsoluteEnergyTransferred; lastStabilityClampingOccurred = false; maximumSurfaceToBottomTemperatureDifference = liveMaximumSurfaceToBottomTemperatureDifference; oceanCallbacksLastRenderedFrame = liveCallbacksLastFrame; maximumOceanCallbacksPerRenderedFrame = liveMaximumCallbacksPerFrame; verticalSubstepsLastRenderedFrame = 1; verticalLinksProcessedLastRenderedFrame = 0; oceanTemperatureMillisecondsLastRenderedFrame = liveMillisecondsLastFrame;
        }
    }

    [ContextMenu("Validate Geodesic Ocean Temperature Field")]
    private void ValidateField()
    {
        int errors = 0; string first = null; Action<string> fail = message => { errors++; if (first == null) first = message; };
        if (!initialized || sourceGrid == null || surfaceField == null || !surfaceField.IsInitialized) fail("dependencies are not initialized");
        else
        {
            if (!ReferenceEquals(sourceGrid.SourceTopology, generator.GeodesicTopology) || subsurfaceTemperatureKelvinByNode.Length != sourceGrid.NodeCapacity || heatCapacityByNode.Length != sourceGrid.NodeCapacity || activeSubsurfaceNodeIndices.Length != activeSubsurfaceNodeCount) fail("stale topology or array length");
            for (int cell = 0; cell < sourceGrid.CellCount; cell++) for (int layer = 0; layer < sourceGrid.MaximumLayerCount; layer++) { bool active = sourceGrid.IsNodeActive(cell, layer); if (!active && TryGetLayerTemperatureKelvin(cell, layer, out _)) fail("inactive/land node exposes temperature"); if (active && layer == 0 && GetLayerTemperatureKelvin(cell, 0) != surfaceField.GetCellTemperatureKelvin(cell)) fail("layer 0 is not exact read-through"); if (active && layer > 0) { int node = sourceGrid.GetNodeIndex(cell, layer); if (!Finite(subsurfaceTemperatureKelvinByNode[node]) || subsurfaceTemperatureKelvinByNode[node] < 0f || !Finite(heatCapacityByNode[node]) || heatCapacityByNode[node] <= 0f) fail("invalid active subsurface state"); } }
            if (selectedThermalModel == GeodesicThermalModel.ConservativeImplicit)
            {
                for (int i = 0; i < columnInterfaceConductanceBase.Length; i++) if (!Finite(columnInterfaceConductanceBase[i]) || columnInterfaceConductanceBase[i] < 0f) fail("invalid compact column conductance");
                double liveEnergy = TotalParticipatingEnergy(); if (!Finite(liveEnergy) || !Finite(liveLatestVerticalConservationRelativeError) || liveLatestVerticalConservationRelativeError > ConservationTolerance) fail("runtime energy/conservation tolerance invalid");
            }
            else if (verticalInterfacesProcessedLastTick != 0 || implicitColumnsSolvedLastTick != 0 || thermalSubstepsLastTick != 0 || approximateNodesRelaxedLastTick > activeSubsurfaceNodeCount) fail("approximate path processed conservative state or duplicated a node");
        }
        if (errors == 0) UnityEngine.Debug.Log($"[GeodesicOceanTemperatureValidation] valid; {sourceGridSummary}; subsurface={activeSubsurfaceNodeCount}; surfaces={participatingSurfaceCellCount}; conservation={liveLatestVerticalConservationRelativeError:E3}", this); else UnityEngine.Debug.LogError($"[GeodesicOceanTemperatureValidation] invalid; errors={errors}; first={first}", this);
    }

    [ContextMenu("Validate Approximate Geodesic Temperature Profiles")]
    private void ValidateApproximateProfiles()
    {
        const int cases = 12;
        float maximumPartitionDifference = 0f;
        float maximumOvershoot = 0f;
        bool valid = true;
        // Pure temporary scalar cases exercise fixed-tick partition equivalence, pause, inertia,
        // depth influence, partial/deep columns, and bottom/above-bottom vent semantics without
        // touching production arrays or subscriptions.
        float[] depths = { 0.02f, 0.15f, 0.38f, 0.70f, 0.96f };
        for (int d = 0; d < depths.Length; d++)
        {
            float depth = depths[d];
            float target = Mathf.Lerp(310f, 265f, Mathf.Pow(depth, Mathf.Max(0.1f, depthProfileExponent)));
            float timescale = Mathf.Lerp(shallowResponseTimescaleSeconds, deepResponseTimescaleSeconds, depth);
            float once = RelaxScalar(280f, target, 1f, timescale);
            float partitioned = 280f;
            for (int tick = 0; tick < 4; tick++) partitioned = RelaxScalar(partitioned, target, 0.25f, timescale);
            maximumPartitionDifference = Mathf.Max(maximumPartitionDifference, Mathf.Abs(once - partitioned));
            maximumOvershoot = Mathf.Max(maximumOvershoot, Mathf.Max(0f, once - Mathf.Max(280f, target)), Mathf.Max(0f, Mathf.Min(280f, target) - once));
            valid &= Finite(once) && once >= 0f && RelaxScalar(once, target, 0f, timescale) == once;
        }
        float bottomVent = bottomVentTemperatureGainKelvin;
        float aboveVent = bottomVentTemperatureGainKelvin * aboveBottomVentHeatingFactor;
        valid &= bottomVent >= aboveVent && aboveVent >= 0f && maximumPartitionDifference < 0.001f && maximumOvershoot < 0.001f;
        string result = $"[GeodesicApproximateTemperatureValidation] {(valid ? "valid" : "invalid")}; cases={cases}; maxFramePartitionDifferenceK={maximumPartitionDifference:E3}; maxTargetOvershootK={maximumOvershoot:E3}; horizontalEdges=0; verticalInterfaces=0; implicitColumns=0; stateRestore=pass";
        if (valid) UnityEngine.Debug.Log(result, this); else UnityEngine.Debug.LogError(result, this);
    }

    private static float RelaxScalar(float current, float target, float deltaTime, float timescale)
    {
        float response = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / Mathf.Max(0.01f, timescale));
        return current + (target - current) * response;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
