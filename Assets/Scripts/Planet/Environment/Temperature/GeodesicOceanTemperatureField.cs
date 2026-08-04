using System;
using Unity.Profiling;
using UnityEngine;

public enum GeodesicOceanTemperatureStartupMode { IsothermalFromSurface, DepthGradient }

/// <summary>Owns active geodesic ocean temperatures below layer 0; layer 0 always reads through to the surface field.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanTemperatureField : MonoBehaviour
{
    private const float StabilitySafetyFactor = 0.45f;
    private const double ConservationTolerance = 2e-5;
    private const int SurfaceEndpointSentinel = -1;
    private static readonly ProfilerMarker CallbackMarker = new ProfilerMarker("GeodesicOceanTemperature.Callback");
    private static readonly ProfilerMarker StabilityMarker = new ProfilerMarker("GeodesicOceanTemperature.ResolveStability");
    private static readonly ProfilerMarker ClearMarker = new ProfilerMarker("GeodesicOceanTemperature.ClearActiveDeltas");
    private static readonly ProfilerMarker FluxMarker = new ProfilerMarker("GeodesicOceanTemperature.AccumulateVerticalFlux");
    private static readonly ProfilerMarker SurfaceApplyMarker = new ProfilerMarker("GeodesicOceanTemperature.ApplySurfaceDeltas");
    private static readonly ProfilerMarker SubsurfaceApplyMarker = new ProfilerMarker("GeodesicOceanTemperature.ApplySubsurfaceDeltas");
    private static readonly ProfilerMarker ConservationMarker = new ProfilerMarker("GeodesicOceanTemperature.ExactConservationAudit");
    private static readonly ProfilerMarker DiagnosticsMarker = new ProfilerMarker("GeodesicOceanTemperature.DiagnosticSnapshot");

    [Header("Geodesic Ocean Temperature")]
    [SerializeField, Tooltip("Enables persistent geodesic subsurface ocean temperatures. Legacy cube-sphere simulation is unaffected.")] private bool enableGeodesicOceanTemperature = true;
    [SerializeField, Min(1e-8f), Tooltip("Thermal capacity per unit ocean-layer volume.")] private float subsurfaceHeatCapacityPerVolume = 1f;
    [SerializeField, Min(0f), Tooltip("Simulation-unit vertical diffusivity; this is not SI calibrated and will be tuned later.")] private float verticalThermalDiffusivity = 0.00002f;
    [SerializeField, Tooltip("Initializes subsurface layers from the surface or with a per-layer depth gradient.")] private GeodesicOceanTemperatureStartupMode startupMode = GeodesicOceanTemperatureStartupMode.DepthGradient;
    [SerializeField, Min(0f), Tooltip("Initial Kelvin decrease per layer index when Depth Gradient is selected.")] private float initialTemperatureDropPerLayerKelvin = 2f;
    [SerializeField, Range(1, 256), Tooltip("Maximum stable explicit vertical-exchange substeps per surface tick.")] private int maximumVerticalSubsteps = 64;
    [SerializeField, Min(0.1f), Tooltip("Unscaled real-time interval between serialized Inspector diagnostic snapshots.")] private float inspectorSnapshotIntervalSeconds = 1f;
    [SerializeField, Min(0.1f), Tooltip("Simulation-time interval between exact coupled-energy conservation audits. Profiling diagnostics audits every tick.")] private float exactConservationAuditIntervalSeconds = 1f;
    [SerializeField, Tooltip("Enables per-tick exact conservation auditing and periodic profiling logs.")] private bool enableProfilingDiagnostics;

    [Header("Runtime Diagnostics (Throttled Inspector Snapshot)")]
    [SerializeField] private bool initialized;
    [SerializeField] private int activeSubsurfaceNodeCount;
    [SerializeField] private int participatingSurfaceCellCount;
    [SerializeField] private double totalSubsurfaceThermalCapacity;
    [SerializeField] private string sourceGridSummary = "None";
    [SerializeField] private double lastCompletedOceanTemperatureTick;
    [SerializeField] private float lastVerticalExchangeSimulationDelta;
    [SerializeField] private int lastVerticalSubstepCount;
    [SerializeField] private double lastVerticalExchangeDurationMilliseconds;
    [SerializeField] private double latestVerticalConservationRelativeError;
    [SerializeField] private double lastExactConservationAuditSimulationTime;
    [SerializeField] private int ticksSinceLastExactConservationAudit;
    [SerializeField] private bool lastTickUsedAlgebraicConservationOnly;
    [SerializeField] private double lastAbsoluteEnergyTransferred;
    [SerializeField] private bool lastStabilityClampingOccurred;
    [SerializeField] private float maximumSurfaceToBottomTemperatureDifference;
    [SerializeField] private long approximateRuntimeMemoryBytes;
    [SerializeField] private int oceanCallbacksLastRenderedFrame;
    [SerializeField] private int maximumOceanCallbacksPerRenderedFrame;
    [SerializeField] private int verticalSubstepsLastRenderedFrame;
    [SerializeField] private long verticalLinksProcessedLastRenderedFrame;
    [SerializeField] private double oceanTemperatureMillisecondsLastRenderedFrame;
    [SerializeField] private int duplicateOrStaleCallbackCount;
    [SerializeField] private int[] layerActiveCellCount = new int[5];
    [SerializeField] private float[] layerMinimumTemperatureKelvin = new float[5];
    [SerializeField] private float[] layerMeanTemperatureKelvin = new float[5];
    [SerializeField] private float[] layerMaximumTemperatureKelvin = new float[5];

    private PlanetGenerator generator;
    private GeodesicOceanLayerDomain domain;
    private GeodesicSurfaceTemperatureField surfaceField;
    private GeodesicOceanLayerGrid sourceGrid;
    private float[] subsurfaceTemperatureKelvinByNode;
    private float[] heatCapacityByNode;
    private float[] inverseHeatCapacityByNode;
    private float[] energyDeltaByNode;
    private float[] surfaceEnergyDeltaByCell;
    private int[] activeSubsurfaceNodeIndices;
    private int[] participatingSurfaceCells;
    private int[] linkLowerNode;
    private int[] linkUpperSubsurfaceNode;
    private int[] linkSurfaceCell;
    private float[] linkConductanceBase;
    private float[] compactSubsurfaceConductanceSumBase;
    private float[] compactSurfaceConductanceSumBase;
    private readonly double[] diagnosticWeightedTemperature = new double[5];
    private readonly double[] diagnosticWeight = new double[5];
    private float cachedCapacityPerVolume = float.NaN;
    private float cachedVerticalDiffusivity = float.NaN;
    private int cachedMaximumVerticalSubsteps = -1;
    private int cachedSurfaceCapacityVersion = -1;
    private float cachedStableVerticalStep = float.MaxValue;
    private bool stabilityCacheValid;
    private bool subscribed;
    private bool warnedStabilityClamp;
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
    public int LastVerticalSubstepCount => liveLastVerticalSubstepCount;
    public double LastVerticalExchangeDurationMilliseconds => liveLastVerticalExchangeDurationMilliseconds;
    public double LatestVerticalConservationRelativeError => liveLatestVerticalConservationRelativeError;
    public double LastAbsoluteEnergyTransferred => liveLastAbsoluteEnergyTransferred;
    public bool LastStabilityClampingOccurred => liveLastStabilityClampingOccurred;
    public float MaximumSurfaceToBottomTemperatureDifference => liveMaximumSurfaceToBottomTemperatureDifference;
    public int ActiveSubsurfaceNodeCount => activeSubsurfaceNodeCount;
    public int ParticipatingSurfaceCellCount => participatingSurfaceCellCount;
    public double TotalSubsurfaceThermalCapacity => totalSubsurfaceThermalCapacity;
    public double LastCompletedOceanTemperatureTick => liveCompletedOceanTemperatureTick;
    public long ApproximateRuntimeMemoryBytes => approximateRuntimeMemoryBytes;

    private void Awake() { generator = GetComponent<PlanetGenerator>(); domain = GetComponent<GeodesicOceanLayerDomain>(); surfaceField = GetComponent<GeodesicSurfaceTemperatureField>(); }
    private void OnDestroy() => ClearField();

    public void InitializeForCurrentDomain()
    {
        Unsubscribe();
        GeodesicOceanLayerGrid grid = domain != null ? domain.Grid : null;
        if (!enableGeodesicOceanTemperature || generator == null || generator.CurrentGridType != PlanetGridType.GeodesicIcosphere || grid == null || surfaceField == null || !surfaceField.IsInitialized || !ReferenceEquals(grid.SourceTopology, generator.GeodesicTopology)) { ClearState(); return; }
        sourceGrid = grid;
        AllocateState();
        BuildCompactParticipationTables();
        RebuildCapacities();
        for (int i = 0; i < activeSubsurfaceNodeIndices.Length; i++)
        {
            int node = activeSubsurfaceNodeIndices[i], cell = node / sourceGrid.MaximumLayerCount, layer = node - cell * sourceGrid.MaximumLayerCount;
            float surface = surfaceField.GetCellTemperatureKelvin(cell);
            subsurfaceTemperatureKelvinByNode[node] = Mathf.Max(0f, surface - (startupMode == GeodesicOceanTemperatureStartupMode.DepthGradient ? initialTemperatureDropPerLayerKelvin * layer : 0f));
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
        subsurfaceTemperatureKelvinByNode = new float[nodes]; heatCapacityByNode = new float[nodes]; inverseHeatCapacityByNode = new float[nodes]; energyDeltaByNode = new float[nodes]; surfaceEnergyDeltaByCell = new float[cells];
    }

    private void BuildCompactParticipationTables()
    {
        int subsurfaceCount = 0;
        for (int cell = 0; cell < sourceGrid.CellCount; cell++) subsurfaceCount += Mathf.Max(0, sourceGrid.ActiveLayerCountByCell[cell] - 1);
        activeSubsurfaceNodeIndices = new int[subsurfaceCount];
        int activeIndex = 0, surfaceCount = 0;
        for (int cell = 0; cell < sourceGrid.CellCount; cell++)
        {
            int layers = sourceGrid.ActiveLayerCountByCell[cell];
            if (layers > 1) surfaceCount++;
            for (int layer = 1; layer < layers; layer++) activeSubsurfaceNodeIndices[activeIndex++] = sourceGrid.GetNodeIndex(cell, layer);
        }
        participatingSurfaceCells = new int[surfaceCount];
        int surfaceIndex = 0;
        for (int cell = 0; cell < sourceGrid.CellCount; cell++) if (sourceGrid.ActiveLayerCountByCell[cell] > 1) participatingSurfaceCells[surfaceIndex++] = cell;
        int links = sourceGrid.VerticalLinkCount;
        linkLowerNode = new int[links]; linkUpperSubsurfaceNode = new int[links]; linkSurfaceCell = new int[links]; linkConductanceBase = new float[links];
        compactSubsurfaceConductanceSumBase = new float[subsurfaceCount]; compactSurfaceConductanceSumBase = new float[surfaceCount];
        int compactUpper = 0, compactLower = 0, compactSurface = 0;
        for (int link = 0; link < links; link++)
        {
            int upper = sourceGrid.VerticalUpperNode[link], lower = sourceGrid.VerticalLowerNode[link];
            int cell = lower / sourceGrid.MaximumLayerCount;
            bool upperIsSurface = upper == sourceGrid.GetNodeIndex(cell, 0);
            float conductanceBase = sourceGrid.VerticalInterfaceArea[link] / sourceGrid.VerticalCenterDistance[link];
            linkLowerNode[link] = lower; linkUpperSubsurfaceNode[link] = upperIsSurface ? SurfaceEndpointSentinel : upper; linkSurfaceCell[link] = upperIsSurface ? cell : -1; linkConductanceBase[link] = conductanceBase;
            while (activeSubsurfaceNodeIndices[compactLower] != lower) compactLower++;
            compactSubsurfaceConductanceSumBase[compactLower] += conductanceBase;
            if (upperIsSurface)
            {
                while (participatingSurfaceCells[compactSurface] != cell) compactSurface++;
                compactSurfaceConductanceSumBase[compactSurface] += conductanceBase;
            }
            else
            {
                while (activeSubsurfaceNodeIndices[compactUpper] != upper) compactUpper++;
                compactSubsurfaceConductanceSumBase[compactUpper] += conductanceBase;
            }
        }
        activeSubsurfaceNodeCount = subsurfaceCount; participatingSurfaceCellCount = surfaceCount;
    }

    private void RebuildCapacities()
    {
        totalSubsurfaceThermalCapacity = 0d;
        float density = Mathf.Max(1e-8f, subsurfaceHeatCapacityPerVolume);
        for (int i = 0; i < activeSubsurfaceNodeIndices.Length; i++) { int node = activeSubsurfaceNodeIndices[i]; float capacity = sourceGrid.LayerVolume[node] * density; heatCapacityByNode[node] = capacity; inverseHeatCapacityByNode[node] = 1f / capacity; totalSubsurfaceThermalCapacity += capacity; }
        cachedCapacityPerVolume = subsurfaceHeatCapacityPerVolume; stabilityCacheValid = false;
        sourceGridSummary = $"cells={sourceGrid.CellCount}, nodes={sourceGrid.NodeCapacity}, active={sourceGrid.ActiveNodeCount}, verticalLinks={sourceGrid.VerticalLinkCount}";
        approximateRuntimeMemoryBytes = (long)sourceGrid.NodeCapacity * sizeof(float) * 4L + (long)sourceGrid.CellCount * sizeof(float) + (long)activeSubsurfaceNodeIndices.Length * (sizeof(int) + sizeof(float)) + (long)participatingSurfaceCells.Length * (sizeof(int) + sizeof(float)) + (long)sourceGrid.VerticalLinkCount * (sizeof(int) * 3L + sizeof(float));
    }

    public void ClearField() { Unsubscribe(); ClearState(); }
    private void ClearState()
    {
        initialized = false; sourceGrid = null; subsurfaceTemperatureKelvinByNode = heatCapacityByNode = inverseHeatCapacityByNode = energyDeltaByNode = surfaceEnergyDeltaByCell = null;
        activeSubsurfaceNodeIndices = participatingSurfaceCells = linkLowerNode = linkUpperSubsurfaceNode = linkSurfaceCell = null; linkConductanceBase = compactSubsurfaceConductanceSumBase = compactSurfaceConductanceSumBase = null;
        activeSubsurfaceNodeCount = participatingSurfaceCellCount = 0; totalSubsurfaceThermalCapacity = 0d; sourceGridSummary = "None"; approximateRuntimeMemoryBytes = 0; stabilityCacheValid = false; lastProcessedSurfaceTickSequence = -1; liveCompletedOceanTemperatureTick = 0d;
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
        ExchangeVerticalHeat(dt);
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
            bool collectTickDiagnostics = enableProfilingDiagnostics || Time.unscaledTimeAsDouble >= nextInspectorSnapshotUnscaledTime;
            liveLastVerticalExchangeSimulationDelta = dt; liveLastStabilityClampingOccurred = false; if (collectTickDiagnostics) liveLastAbsoluteEnergyTransferred = 0d;
            if (cachedCapacityPerVolume != subsurfaceHeatCapacityPerVolume) RebuildCapacities();
            bool exactAudit = enableProfilingDiagnostics || liveCompletedOceanTemperatureTick + dt >= nextExactAuditSimulationTime;
            double before = 0d;
            if (exactAudit) using (ConservationMarker.Auto()) before = TotalParticipatingEnergy();
            if (verticalThermalDiffusivity <= 0f || dt <= 0f)
            {
                liveLastVerticalSubstepCount = 0; CompleteAudit(exactAudit, before, dt); FinishTick(start); return;
            }
            int needed; float effectiveDiffusivity;
            using (StabilityMarker.Auto()) ResolveStableSubsteps(dt, out needed, out effectiveDiffusivity);
            int substeps = Mathf.Min(needed, Mathf.Max(1, maximumVerticalSubsteps));
            if (needed > substeps) { effectiveDiffusivity *= (float)substeps / needed; liveLastStabilityClampingOccurred = true; if (!warnedStabilityClamp) { UnityEngine.Debug.LogWarning("[GeodesicOceanTemperature] Vertical diffusivity stability-clamped for a capped tick.", this); warnedStabilityClamp = true; } }
            liveLastVerticalSubstepCount = substeps; substepsThisFrame += substeps; float stepDt = dt / substeps;
            for (int step = 0; step < substeps; step++)
            {
                using (ClearMarker.Auto()) { for (int i = 0; i < activeSubsurfaceNodeIndices.Length; i++) energyDeltaByNode[activeSubsurfaceNodeIndices[i]] = 0f; for (int i = 0; i < participatingSurfaceCells.Length; i++) surfaceEnergyDeltaByCell[participatingSurfaceCells[i]] = 0f; }
                using (FluxMarker.Auto()) for (int link = 0; link < linkLowerNode.Length; link++)
                {
                    int lower = linkLowerNode[link], upper = linkUpperSubsurfaceNode[link], surfaceCell = linkSurfaceCell[link];
                    float upperTemperature = upper == SurfaceEndpointSentinel ? surfaceField.GetCellTemperatureKelvin(surfaceCell) : subsurfaceTemperatureKelvinByNode[upper];
                    float energy = effectiveDiffusivity * linkConductanceBase[link] * (subsurfaceTemperatureKelvinByNode[lower] - upperTemperature) * stepDt;
                    if (upper == SurfaceEndpointSentinel) surfaceEnergyDeltaByCell[surfaceCell] += energy; else energyDeltaByNode[upper] += energy;
                    energyDeltaByNode[lower] -= energy; if (collectTickDiagnostics) liveLastAbsoluteEnergyTransferred += Math.Abs(energy);
                }
                linksThisFrame += linkLowerNode.Length;
                using (SurfaceApplyMarker.Auto()) for (int i = 0; i < participatingSurfaceCells.Length; i++) { int cell = participatingSurfaceCells[i]; float delta = surfaceEnergyDeltaByCell[cell]; if (delta != 0f) surfaceField.TryApplyExternalEnergyDelta(cell, delta); }
                using (SubsurfaceApplyMarker.Auto()) for (int i = 0; i < activeSubsurfaceNodeIndices.Length; i++) { int node = activeSubsurfaceNodeIndices[i]; subsurfaceTemperatureKelvinByNode[node] += energyDeltaByNode[node] * inverseHeatCapacityByNode[node]; }
            }
            CompleteAudit(exactAudit, before, dt); FinishTick(start);
        }
    }

    private void ResolveStableSubsteps(float dt, out int needed, out float effectiveDiffusivity)
    {
        int surfaceVersion = surfaceField.ThermalCapacityVersion;
        if (!stabilityCacheValid || cachedVerticalDiffusivity != verticalThermalDiffusivity || cachedMaximumVerticalSubsteps != maximumVerticalSubsteps || cachedSurfaceCapacityVersion != surfaceVersion)
        {
            float stable = float.PositiveInfinity;
            if (verticalThermalDiffusivity > 0f)
            {
                for (int i = 0; i < participatingSurfaceCells.Length; i++) if (compactSurfaceConductanceSumBase[i] > 0f) stable = Mathf.Min(stable, StabilitySafetyFactor * surfaceField.GetCellHeatCapacity(participatingSurfaceCells[i]) / (verticalThermalDiffusivity * compactSurfaceConductanceSumBase[i]));
                for (int i = 0; i < activeSubsurfaceNodeIndices.Length; i++) if (compactSubsurfaceConductanceSumBase[i] > 0f) stable = Mathf.Min(stable, StabilitySafetyFactor * heatCapacityByNode[activeSubsurfaceNodeIndices[i]] / (verticalThermalDiffusivity * compactSubsurfaceConductanceSumBase[i]));
            }
            cachedStableVerticalStep = float.IsInfinity(stable) ? float.MaxValue : Mathf.Max(1e-8f, stable); cachedVerticalDiffusivity = verticalThermalDiffusivity; cachedMaximumVerticalSubsteps = maximumVerticalSubsteps; cachedSurfaceCapacityVersion = surfaceVersion; stabilityCacheValid = true;
        }
        needed = Mathf.Max(1, Mathf.CeilToInt(dt / cachedStableVerticalStep)); effectiveDiffusivity = verticalThermalDiffusivity;
    }

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
        RefreshInspectorSnapshot(false);
        if (enableProfilingDiagnostics && Time.frameCount % 120 == 0) UnityEngine.Debug.Log($"[GeodesicOceanTemperatureProfile] callbacks={callbacksThisFrame}, substeps={substepsThisFrame}, links={linksThisFrame}, frameMs={millisecondsThisFrame:F3}, conservation={liveLatestVerticalConservationRelativeError:E3}", this);
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
            lastCompletedOceanTemperatureTick = liveCompletedOceanTemperatureTick; lastVerticalExchangeSimulationDelta = liveLastVerticalExchangeSimulationDelta; lastVerticalSubstepCount = liveLastVerticalSubstepCount; lastVerticalExchangeDurationMilliseconds = liveLastVerticalExchangeDurationMilliseconds; latestVerticalConservationRelativeError = liveLatestVerticalConservationRelativeError; lastExactConservationAuditSimulationTime = liveLastExactConservationAuditSimulationTime; ticksSinceLastExactConservationAudit = liveTicksSinceLastExactConservationAudit; lastTickUsedAlgebraicConservationOnly = liveLastTickUsedAlgebraicConservationOnly; lastAbsoluteEnergyTransferred = liveLastAbsoluteEnergyTransferred; lastStabilityClampingOccurred = liveLastStabilityClampingOccurred; maximumSurfaceToBottomTemperatureDifference = liveMaximumSurfaceToBottomTemperatureDifference; oceanCallbacksLastRenderedFrame = liveCallbacksLastFrame; maximumOceanCallbacksPerRenderedFrame = liveMaximumCallbacksPerFrame; verticalSubstepsLastRenderedFrame = liveSubstepsLastFrame; verticalLinksProcessedLastRenderedFrame = liveLinksLastFrame; oceanTemperatureMillisecondsLastRenderedFrame = liveMillisecondsLastFrame;
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
            for (int link = 0; link < linkLowerNode.Length; link++) if (!Finite(linkConductanceBase[link]) || linkConductanceBase[link] <= 0f || !sourceGrid.IsNodeActive(linkLowerNode[link] / sourceGrid.MaximumLayerCount, linkLowerNode[link] % sourceGrid.MaximumLayerCount)) fail("invalid compact vertical link/conductance");
            float[] t = { 300f, 300f }, c = { 2f, 3f }, d = new float[2]; TestExchange(t, c, d, 1f); if (t[0] != 300f || t[1] != 300f) fail("uniform-column test changed"); t[0] = 310f; t[1] = 290f; double before = t[0] * c[0] + t[1] * c[1]; TestExchange(t, c, d, 0.01f); double after = t[0] * c[0] + t[1] * c[1]; if (t[1] <= 290f) fail("warm-surface test did not transfer downward"); if (Math.Abs(after - before) > 1e-5 * Math.Abs(before)) fail("temporary column did not conserve energy");
            double liveEnergy = TotalParticipatingEnergy(); if (!Finite(liveEnergy) || !Finite(liveLatestVerticalConservationRelativeError) || liveLatestVerticalConservationRelativeError > ConservationTolerance) fail("runtime energy/conservation tolerance invalid");
        }
        if (errors == 0) UnityEngine.Debug.Log($"[GeodesicOceanTemperatureValidation] valid; {sourceGridSummary}; subsurface={activeSubsurfaceNodeCount}; surfaces={participatingSurfaceCellCount}; conservation={liveLatestVerticalConservationRelativeError:E3}", this); else UnityEngine.Debug.LogError($"[GeodesicOceanTemperatureValidation] invalid; errors={errors}; first={first}", this);
    }
    private static void TestExchange(float[] t, float[] c, float[] d, float dt) { d[0] = d[1] = 0f; float e = (t[1] - t[0]) * dt; d[0] += e; d[1] -= e; t[0] += d[0] / c[0]; t[1] += d[1] / c[1]; }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
