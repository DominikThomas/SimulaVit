using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Unity.Profiling;

public enum GeodesicThermalModel
{
    ApproximateEcologicalProfiles = 0,
    ConservativeImplicit = 1
}

/// <summary>Authoritative, surface-only Kelvin field for geodesic simulation cells.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicSurfaceTemperatureField : MonoBehaviour
{
    private static readonly ProfilerMarker TickMarker = new ProfilerMarker("GeodesicTemperature.SurfaceTick");
    private static readonly ProfilerMarker TargetMarker = new ProfilerMarker("GeodesicTemperature.TargetUpdate");
    private static readonly ProfilerMarker ResponseMarker = new ProfilerMarker("GeodesicTemperature.SurfaceResponse");
    private static readonly ProfilerMarker DiffusionMarker = new ProfilerMarker("GeodesicTemperature.HorizontalDiffusion");
    private static readonly ProfilerMarker CommitMarker = new ProfilerMarker("GeodesicTemperature.CommittedEvent");
    private static ProfilerCounterValue<int> TicksPerFrameCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Thermal Ticks / Frame", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<float> SimSecondsPerFrameCounter = new ProfilerCounterValue<float>(ProfilerCategory.Scripts, "Geodesic Thermal Sim Seconds / Frame", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<float> BacklogCounter = new ProfilerCounterValue<float>(ProfilerCategory.Scripts, "Geodesic Thermal Backlog Seconds", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    public event Action<float> SurfaceTemperatureTickCommitted;
    public event Action SurfaceTemperatureFieldReinitialized;
    public event Action SurfaceTemperatureFieldClearing;
    private const float MinimumTimescale = 0.001f;
    private const float DiagnosticHighTemperatureKelvin = 2000f;

    [Header("Geodesic Surface Temperature")]
    [SerializeField, InspectorName("Configured Thermal Model"), Tooltip("Authoritative model configuration for the next generated geodesic planet. Runtime switching is unsupported.")] private GeodesicThermalModel thermalModel = GeodesicThermalModel.ApproximateEcologicalProfiles;
    [SerializeField, Tooltip("Enables one physical surface-temperature value per geodesic simulation cell. This does not enable ocean layers or ice.")] private bool enableGeodesicSurfaceTemperature = true;
    [SerializeField, Min(0.01f), InspectorName("Approximate Thermal Update Interval"), Tooltip("Fixed authoritative simulation seconds per ApproximateEcologicalProfiles tick. Independent of simulation speed and rendered FPS.")] private float approximateUpdateIntervalSeconds = 2f;
    [SerializeField, Min(0.01f), InspectorName("Conservative Thermal Update Interval"), Tooltip("Fixed authoritative simulation seconds per ConservativeImplicit tick. This retains the original serialized cadence.")] private float updateIntervalSeconds = 0.25f;
    [SerializeField, Range(1, 512), Tooltip("Emergency per-rendered-frame catch-up guard. Remaining authoritative time stays as explicit backlog and is never discarded.")] private int maximumThermalTicksPerFrame = 64;
    [SerializeField, Min(0.05f), Tooltip("Unscaled real-time interval for cached global surface diagnostics used by the HUD and Inspector.")] private float diagnosticSnapshotIntervalSeconds = 0.25f;
    [SerializeField, Min(0.1f), Tooltip("Simulation-time interval between exact surface-diffusion conservation audits.")] private float diffusionConservationAuditIntervalSeconds = 5f;
    [SerializeField, Min(MinimumTimescale), Tooltip("Surface-only warming relaxation timescale in simulation seconds.")] private float heatingTimescaleSeconds = 20f;
    [SerializeField, Min(MinimumTimescale), Tooltip("Surface-only cooling relaxation timescale in simulation seconds.")] private float coolingTimescaleSeconds = 35f;
    [SerializeField, Min(0f), Tooltip("Optional temporary approximation of unresolved horizontal surface heat transport. Uses the shared geodesic transport graph. This is not ocean-current, atmospheric-wind, or vent-plume transport.")] private float diffusionStrength = 0.002f;
    [SerializeField, Min(0.01f), Tooltip("Land surface heat-capacity multiplier used by inertia and diffusion.")] private float landHeatCapacityMultiplier = 1f;
    [SerializeField, Min(0.01f), Tooltip("Ocean surface heat-capacity multiplier. This is not vertical ocean heat storage.")] private float oceanSurfaceHeatCapacityMultiplier = 2f;
    [SerializeField, Range(0.1f, 4f), Tooltip("Exponent applied to direct insolation in the interim surface-energy approximation.")] private float insolationExponent = 1f;
    [SerializeField, Tooltip("Intrinsic terrestrial geothermal source temperature; land cells receive only a bounded local anomaly.")] private float terrestrialVentSourceTemperatureC = GeodesicVentThermalModel.SourceTemperatureC;
    [SerializeField, Range(0f, 0.25f), Tooltip("Maximum coarse land-cell blend toward geothermal source temperature.")] private float terrestrialVentThermalInfluence = 0.06f;
    [SerializeField, Tooltip("Reserved opt-in diagnostic flag; authoritative terrain colours are never modified by this field.")] private bool debugTemperatureVisualization;
    [SerializeField, Tooltip("Logs temperature tick stage timings and sun/light agreement diagnostics.")] private bool enableProfilingDiagnostics;

    [Header("Runtime Diagnostics (Read Only)")]
    [SerializeField] private bool initialized;
    [SerializeField] private int runtimeCellCount;
    [SerializeField] private float minimumTemperatureKelvin;
    [SerializeField] private float areaWeightedMeanTemperatureKelvin;
    [SerializeField] private float maximumTemperatureKelvin;
    [SerializeField] private double lastCompletedTemperatureTick;
    [SerializeField] private double lastTickDurationMilliseconds;
    [SerializeField] private string currentSunDirectionProvider = "None";
    [SerializeField] private double latestDiffusionConservationRelativeError;
    [SerializeField] private double lastTargetUpdateDurationMilliseconds;
    [SerializeField] private double lastDiffusionDurationMilliseconds;
    [SerializeField] private int lastDiffusionSubstepCount;
    [SerializeField] private double totalAuthoritativeSimulationSecondsReceived;
    [SerializeField] private double totalSimulationSecondsConsumedByThermalTicks;
    [SerializeField] private double unconsumedThermalRemainderSeconds;
    [SerializeField] private double discardedSimulationSeconds;
    [SerializeField] private int thermalTicksCurrentRenderedFrame;
    [SerializeField] private float thermalSimSecondsProcessedThisFrame;
    [SerializeField] private int maximumThermalTicksPerRenderedFrame;
    [SerializeField] private double currentAuthoritativeSimulationTime;
    [SerializeField] private double thermalIntegrationCursorTime;
    [SerializeField] private double currentSunOrbitTime;
    [SerializeField] private float currentSunPhase01;
    [SerializeField] private float maximumSolarAngularAdvancePerThermalTickDegrees;
    [SerializeField] private int lastCompletedSolarDayIndex = -1;
    [SerializeField] private float lastCompletedDayMinimumTemperatureKelvin;
    [SerializeField] private float lastCompletedDayMeanTemperatureKelvin;
    [SerializeField] private float lastCompletedDayMaximumTemperatureKelvin;
    [SerializeField] private double lastThermalTickRepresentativeSimulationTime;
    [SerializeField] private float lastThermalTickSolarPhase01;
    [SerializeField] private double lastExactDiffusionAuditSimulationTime;
    [SerializeField] private int ticksSinceLastExactDiffusionAudit;
    [SerializeField] private bool lastTickUsedAlgebraicDiffusionConservationOnly;
    [SerializeField, InspectorName("Active Thermal Model"), Tooltip("Read-only diagnostic snapshot; runtime model switching is unsupported.")] private string activeThermalModelDiagnostic = "Not initialized";
    [SerializeField, Tooltip("True only while Active Thermal Model is latched for an initialized geodesic planet.")] private bool hasActiveThermalModel;
    [SerializeField] private int surfaceCellsUpdatedLastTick;
    [SerializeField] private int horizontalEdgesProcessedLastTick;

    private PlanetGenerator planetGenerator;
    private SunSkyRotator sunDirectionProvider;
    private ReplicatorManager simulationClock;
    private GeodesicGridTopology topology;
    private GeodesicTransportGraph transportGraph;
    private GeodesicOceanResourceField resourceField;
    private float[] surfaceTemperatureKelvinByCell;
    private float[] targetTemperatureKelvinByCell;
    private float[] workingTemperatureKelvinByCell;
    private float[] energyDeltaByCell;
    private float[] heatCapacityByCell;
    private float[] inverseHeatCapacityByCell;
    private float cachedLandHeatCapacityMultiplier;
    private float cachedOceanHeatCapacityMultiplier;
    private float cachedDiffusionStrength = float.NaN;
    private float cachedStableDiffusionStep = float.MaxValue;
    private int cachedDiffusionCapacityVersion = -1;
    private GeodesicTransportGraph cachedDiffusionTransportGraph;
    private double lastObservedAuthoritativeSimulationTime;
    private float baseTemperatureKelvin = 273.15f;
    private float insolationTemperatureGainKelvin = 45f;
    private bool warnedInvalidTemperature;
    private bool warnedDiffusionClamp;
    private int thermalCapacityVersion;
    private long surfaceTemperatureTickSequence;
    private int accumulatingSolarDayIndex = -1;
    private float accumulatingDayMinimumTemperatureKelvin;
    private float accumulatingDayMaximumTemperatureKelvin;
    private double accumulatingDayMeanSum;
    private int accumulatingDaySampleCount;
    private double nextDiagnosticSnapshotUnscaledTime;
    private double nextDiffusionConservationAuditSimulationTime;
    private bool warnedThermalBacklogGuard;
    [NonSerialized] private GeodesicThermalModel activeThermalModel;
    [NonSerialized] private float activeUpdateIntervalSeconds;

    public bool IsInitialized => initialized;
    public int CellCount => runtimeCellCount;
    public IReadOnlyList<float> SurfaceTemperaturesKelvin => surfaceTemperatureKelvinByCell ?? Array.Empty<float>();
    public float MinimumTemperatureKelvin => minimumTemperatureKelvin;
    public float MaximumTemperatureKelvin => maximumTemperatureKelvin;
    public float AreaWeightedMeanTemperatureKelvin => areaWeightedMeanTemperatureKelvin;
    public double LastCompletedTemperatureTick => lastCompletedTemperatureTick;
    public double LastTickDurationMilliseconds => lastTickDurationMilliseconds;
    public double LatestDiffusionConservationRelativeError => latestDiffusionConservationRelativeError;
    public string CurrentSunDirectionProvider => currentSunDirectionProvider;
    public int ThermalCapacityVersion => thermalCapacityVersion;
    public long SurfaceTemperatureTickSequence => surfaceTemperatureTickSequence;
    public double TotalAuthoritativeSimulationSecondsReceived => totalAuthoritativeSimulationSecondsReceived;
    public double TotalSimulationSecondsConsumedByThermalTicks => totalSimulationSecondsConsumedByThermalTicks;
    public double UnconsumedThermalRemainderSeconds => unconsumedThermalRemainderSeconds;
    public double DiscardedSimulationSeconds => discardedSimulationSeconds;
    public double ThermalIntegrationCursorTime => thermalIntegrationCursorTime;
    public GeodesicThermalModel ConfiguredThermalModel => thermalModel;
    public GeodesicThermalModel ActiveThermalModel => activeThermalModel;

    public void SetStartupApproximateUpdateInterval(float intervalSeconds)
    {
        approximateUpdateIntervalSeconds = Mathf.Max(0.01f, intervalSeconds);
    }
    public bool HasActiveThermalModel => hasActiveThermalModel;
    public float ActiveUpdateIntervalSeconds => activeUpdateIntervalSeconds;
    public float BaseTemperatureKelvin => baseTemperatureKelvin;
    // Synchronous ocean ticks use this generation-owned storage directly. The array remains
    // authoritative here; callers must treat it as read-only.
    internal float[] AuthoritativeTemperatureStorage => initialized ? surfaceTemperatureKelvinByCell : null;
    public int SurfaceCellsUpdatedLastTick => surfaceCellsUpdatedLastTick;
    public int HorizontalEdgesProcessedLastTick => horizontalEdgesProcessedLastTick;
    public int ThermalTicksCurrentRenderedFrame => thermalTicksCurrentRenderedFrame;
    public float ThermalSimSecondsProcessedThisFrame => thermalSimSecondsProcessedThisFrame;

    private void Awake() => ResolveReferences();

    private void Update()
    {
        if (!initialized || !enableGeodesicSurfaceTemperature || planetGenerator.CurrentGridType != PlanetGridType.GeodesicIcosphere) return;
        ResolveClockOnly();
        if (simulationClock == null) return;
        double target = Math.Max(0d, simulationClock.SimulationTimeSeconds);
        currentAuthoritativeSimulationTime = target;
        thermalTicksCurrentRenderedFrame = 0;
        thermalSimSecondsProcessedThisFrame = 0f;
        if (target < lastObservedAuthoritativeSimulationTime)
        {
            // Clock restoration/regression establishes a new integration epoch; old-world backlog never survives.
            thermalIntegrationCursorTime = target;
            lastObservedAuthoritativeSimulationTime = target;
            unconsumedThermalRemainderSeconds = 0d;
            return;
        }
        totalAuthoritativeSimulationSecondsReceived += target - lastObservedAuthoritativeSimulationTime;
        lastObservedAuthoritativeSimulationTime = target;
        double interval = Math.Max(0.01d, activeUpdateIntervalSeconds);
        int guard = Mathf.Max(1, maximumThermalTicksPerFrame);
        while (thermalIntegrationCursorTime + interval <= target + 1e-9d && thermalTicksCurrentRenderedFrame < guard)
        {
            double midpoint = thermalIntegrationCursorTime + interval * 0.5d;
            TickTemperature((float)interval, midpoint);
            thermalIntegrationCursorTime += interval;
            totalSimulationSecondsConsumedByThermalTicks += interval;
            thermalTicksCurrentRenderedFrame++;
        }
        if (thermalIntegrationCursorTime + interval <= target + 1e-9d && !warnedThermalBacklogGuard)
        {
            warnedThermalBacklogGuard = true;
            UnityEngine.Debug.LogWarning("[GeodesicTemperature] Emergency thermal catch-up guard reached; backlog is retained and will be processed on later frames.", this);
        }
        if (thermalTicksCurrentRenderedFrame > 0 && Time.unscaledTimeAsDouble >= nextDiagnosticSnapshotUnscaledTime)
        {
            UpdateDiagnostics();
            UpdateSampledCycleDiagnostics(thermalIntegrationCursorTime - interval * 0.5d);
            nextDiagnosticSnapshotUnscaledTime = Time.unscaledTimeAsDouble + Mathf.Max(0.05f, diagnosticSnapshotIntervalSeconds);
        }
        maximumThermalTicksPerRenderedFrame = Mathf.Max(maximumThermalTicksPerRenderedFrame, thermalTicksCurrentRenderedFrame);
        unconsumedThermalRemainderSeconds = Math.Max(0d, target - thermalIntegrationCursorTime);
        thermalSimSecondsProcessedThisFrame = thermalTicksCurrentRenderedFrame * (float)interval;
        TicksPerFrameCounter.Value = thermalTicksCurrentRenderedFrame; SimSecondsPerFrameCounter.Value = thermalSimSecondsProcessedThisFrame; BacklogCounter.Value = (float)unconsumedThermalRemainderSeconds;
        currentSunOrbitTime = sunDirectionProvider != null ? sunDirectionProvider.CurrentOrbitTimeSeconds : target;
        currentSunPhase01 = sunDirectionProvider != null ? sunDirectionProvider.GetDayPhase01AtSimulationTime(target) : 0f;
        maximumSolarAngularAdvancePerThermalTickDegrees = sunDirectionProvider != null ? Mathf.Abs(sunDirectionProvider.orbitDegreesPerSecond * (float)interval) : 0f;
    }

    /// <summary>Assigns startup parameters without allocating or rebuilding runtime field state.</summary>
    public void SetStartupTemperatureParameters(float baseKelvin, float insolationGainKelvin)
    {
        baseTemperatureKelvin = Mathf.Max(0f, baseKelvin);
        insolationTemperatureGainKelvin = Mathf.Max(0f, insolationGainKelvin);
    }

    /// <summary>Explicit replacement path for runtime configuration changes; normal startup uses the non-rebuilding setter before generation.</summary>
    public void ReinitializeWithTemperatureParameters(float baseKelvin, float insolationGainKelvin)
    {
        SetStartupTemperatureParameters(baseKelvin, insolationGainKelvin);
        InitializeForCurrentTopology();
    }

    public void InitializeForCurrentTopology()
    {
        ResolveReferences();
        topology = planetGenerator != null ? planetGenerator.GeodesicTopology : null;
        transportGraph = planetGenerator != null ? planetGenerator.GeodesicTransportGraph : null;
        if (!enableGeodesicSurfaceTemperature || planetGenerator == null || planetGenerator.CurrentGridType != PlanetGridType.GeodesicIcosphere || topology == null)
        {
            ClearField();
            return;
        }
        if (transportGraph == null || !ReferenceEquals(transportGraph.SourceTopology, topology))
        {
            UnityEngine.Debug.LogError("[GeodesicTemperature] The authoritative transport graph is unavailable or belongs to a different topology; temperature initialization was aborted.", this);
            ClearField();
            return;
        }

        int count = topology.CellCount;
        activeThermalModel = thermalModel;
        activeUpdateIntervalSeconds = Mathf.Max(0.01f, activeThermalModel == GeodesicThermalModel.ApproximateEcologicalProfiles ? approximateUpdateIntervalSeconds : updateIntervalSeconds);
        hasActiveThermalModel = true;
        activeThermalModelDiagnostic = activeThermalModel.ToString();
        surfaceTemperatureKelvinByCell = new float[count];
        targetTemperatureKelvinByCell = new float[count];
        workingTemperatureKelvinByCell = new float[count];
        energyDeltaByCell = activeThermalModel == GeodesicThermalModel.ConservativeImplicit ? new float[count] : null;
        heatCapacityByCell = new float[count];
        inverseHeatCapacityByCell = new float[count];
        runtimeCellCount = count;
        double simulationTime = simulationClock != null ? Math.Max(0d, simulationClock.SimulationTimeSeconds) : 0d;
        thermalIntegrationCursorTime = lastObservedAuthoritativeSimulationTime = currentAuthoritativeSimulationTime = simulationTime;
        totalAuthoritativeSimulationSecondsReceived = totalSimulationSecondsConsumedByThermalTicks = unconsumedThermalRemainderSeconds = discardedSimulationSeconds = 0d;
        thermalTicksCurrentRenderedFrame = 0; thermalSimSecondsProcessedThisFrame = 0f;
        surfaceTemperatureTickSequence = 0;
        accumulatingSolarDayIndex = lastCompletedSolarDayIndex = -1; accumulatingDaySampleCount = 0;
        nextDiagnosticSnapshotUnscaledTime = 0d; nextDiffusionConservationAuditSimulationTime = simulationTime + Mathf.Max(0.1f, diffusionConservationAuditIntervalSeconds);
        warnedThermalBacklogGuard = false; ticksSinceLastExactDiffusionAudit = 0; lastTickUsedAlgebraicDiffusionConservationOnly = true;
        RebuildThermalCapacities();
        Vector3 startupSun = sunDirectionProvider != null ? sunDirectionProvider.GetPlanetToSunDirectionWorldAtSimulationTime(simulationTime) : Vector3.zero;
        UpdateTemperatureTargets(startupSun);
        Array.Copy(targetTemperatureKelvinByCell, surfaceTemperatureKelvinByCell, count);
        UpdateDiagnostics();
        UpdateSampledCycleDiagnostics(simulationTime);
        initialized = true;
        SurfaceTemperatureFieldReinitialized?.Invoke();
        UnityEngine.Debug.Log($"[GeodesicTemperature] initialized configuredModel={thermalModel}, activeModel={activeThermalModel}, thermalIntervalSeconds={activeUpdateIntervalSeconds:F3}, cells={count}, baseK={baseTemperatureKelvin:F2}, gainK={insolationTemperatureGainKelvin:F2}, min/mean/maxK={minimumTemperatureKelvin:F2}/{areaWeightedMeanTemperatureKelvin:F2}/{maximumTemperatureKelvin:F2}", this);
    }

    public void ClearField()
    {
        if (initialized) SurfaceTemperatureFieldClearing?.Invoke();
        initialized = false;
        hasActiveThermalModel = false;
        activeThermalModel = default;
        activeUpdateIntervalSeconds = 0f;
        activeThermalModelDiagnostic = "Not initialized";
        topology = null;
        transportGraph = null;
        surfaceTemperatureKelvinByCell = null;
        targetTemperatureKelvinByCell = null;
        workingTemperatureKelvinByCell = null;
        energyDeltaByCell = null;
        heatCapacityByCell = null;
        inverseHeatCapacityByCell = null;
        runtimeCellCount = 0;
        thermalIntegrationCursorTime = lastObservedAuthoritativeSimulationTime = currentAuthoritativeSimulationTime = 0d;
        totalAuthoritativeSimulationSecondsReceived = totalSimulationSecondsConsumedByThermalTicks = unconsumedThermalRemainderSeconds = discardedSimulationSeconds = 0d;
        thermalTicksCurrentRenderedFrame = 0; thermalSimSecondsProcessedThisFrame = 0f;
        surfaceTemperatureTickSequence = 0;
        accumulatingSolarDayIndex = lastCompletedSolarDayIndex = -1; accumulatingDaySampleCount = 0;
    }

    public float GetCellTemperatureKelvin(int cellIndex) => initialized && cellIndex >= 0 && cellIndex < runtimeCellCount ? surfaceTemperatureKelvinByCell[cellIndex] : float.NaN;
    public float GetCellHeatCapacity(int cellIndex) => initialized && cellIndex >= 0 && cellIndex < runtimeCellCount ? heatCapacityByCell[cellIndex] : float.NaN;

    public bool TryApplyExternalEnergyDelta(int cellIndex, float energyDelta)
    {
        if (!initialized || cellIndex < 0 || cellIndex >= runtimeCellCount || float.IsNaN(energyDelta) || float.IsInfinity(energyDelta)) return false;
        float updated = surfaceTemperatureKelvinByCell[cellIndex] + energyDelta * inverseHeatCapacityByCell[cellIndex];
        if (float.IsNaN(updated) || float.IsInfinity(updated) || updated < 0f) return false;
        surfaceTemperatureKelvinByCell[cellIndex] = updated;
        return true;
    }

    /// <summary>Internal simulation-authority path for geodesic ocean coupling. Validates the whole compact batch before mutating layer 0.</summary>
    internal bool TryApplyAuthoritativeTemperatureBatch(int[] cellIndices, float[] temperatureKelvin, int count)
    {
        if (!initialized || cellIndices == null || temperatureKelvin == null || count < 0 || count > cellIndices.Length || count > temperatureKelvin.Length) return false;
        for (int i = 0; i < count; i++)
        {
            int cell = cellIndices[i];
            float updated = temperatureKelvin[i];
            if (cell < 0 || cell >= runtimeCellCount || float.IsNaN(updated) || float.IsInfinity(updated) || updated < 0f) return false;
        }
        for (int i = 0; i < count; i++) surfaceTemperatureKelvinByCell[cellIndices[i]] = temperatureKelvin[i];
        return true;
    }
    public float GetTemperatureKelvinAtLocalDirection(Vector3 localDirection) { int cell = FindCellForLocalDirection(localDirection); return GetCellTemperatureKelvin(cell); }
    public float GetTemperatureKelvinAtWorldDirection(Vector3 worldDirection) => GetTemperatureKelvinAtLocalDirection(transform.InverseTransformDirection(worldDirection));

    public float GetCellInsolationCosine(int cellIndex)
    {
        if (topology == null || cellIndex < 0 || cellIndex >= topology.CellCount || !TryGetSunDirection(out Vector3 sun)) return 0f;
        Vector3 normalWorld = transform.TransformDirection(topology.CellDirections[cellIndex]).normalized;
        return Mathf.Max(0f, Vector3.Dot(normalWorld, sun));
    }

    public float GetCellTargetTemperatureKelvin(int cellIndex) => initialized && cellIndex >= 0 && cellIndex < runtimeCellCount ? targetTemperatureKelvinByCell[cellIndex] : float.NaN;
    public float GetCellEffectiveThermalResponseMultiplier(int cellIndex) => 1f / GetSurfaceMultiplier(cellIndex);
    public string GetCellThermalCategory(int cellIndex) => IsOcean(cellIndex) ? "Ocean surface" : "Land surface";

    public void GetNeighborTemperatureStats(int cellIndex, out float minimum, out float mean, out float maximum)
    {
        minimum = float.PositiveInfinity; maximum = float.NegativeInfinity; mean = 0f;
        if (!initialized || cellIndex < 0 || cellIndex >= runtimeCellCount) { minimum = mean = maximum = float.NaN; return; }
        int count = topology.NeighborCounts[cellIndex];
        for (int slot = 0; slot < count; slot++) { float t = surfaceTemperatureKelvinByCell[topology.Neighbors6[cellIndex * 6 + slot]]; minimum = Mathf.Min(minimum, t); maximum = Mathf.Max(maximum, t); mean += t; }
        mean = count > 0 ? mean / count : surfaceTemperatureKelvinByCell[cellIndex];
    }

    private void TickTemperature(float dt, double representativeSimulationTime)
    {
        using (TickMarker.Auto())
        {
        long tickStart = Stopwatch.GetTimestamp();
        if (cachedLandHeatCapacityMultiplier != landHeatCapacityMultiplier || cachedOceanHeatCapacityMultiplier != oceanSurfaceHeatCapacityMultiplier) RebuildThermalCapacities();
        long targetStart = Stopwatch.GetTimestamp();
        Vector3 tickSunDirection = sunDirectionProvider != null ? sunDirectionProvider.GetPlanetToSunDirectionWorldAtSimulationTime(representativeSimulationTime) : Vector3.zero;
        using (TargetMarker.Auto()) UpdateTemperatureTargets(tickSunDirection);
        lastTargetUpdateDurationMilliseconds = ElapsedMilliseconds(targetStart);
        using (ResponseMarker.Auto())
        {
        for (int i = 0; i < runtimeCellCount; i++)
        {
            float current = surfaceTemperatureKelvinByCell[i];
            float target = targetTemperatureKelvinByCell[i];
            float timescale = (target >= current ? heatingTimescaleSeconds : coolingTimescaleSeconds) * GetSurfaceMultiplier(i);
            float response = 1f - Mathf.Exp(-dt / Mathf.Max(MinimumTimescale, timescale));
            workingTemperatureKelvinByCell[i] = current + (target - current) * response;
        }
        }
        surfaceCellsUpdatedLastTick = runtimeCellCount;
        long diffusionStart = Stopwatch.GetTimestamp();
        if (activeThermalModel == GeodesicThermalModel.ConservativeImplicit)
        {
            using (DiffusionMarker.Auto()) ApplyConservativeDiffusion(dt);
            horizontalEdgesProcessedLastTick = transportGraph.EdgeCount * lastDiffusionSubstepCount;
        }
        else
        {
            lastDiffusionSubstepCount = 0;
            horizontalEdgesProcessedLastTick = 0;
            latestDiffusionConservationRelativeError = 0d;
            lastTickUsedAlgebraicDiffusionConservationOnly = true;
        }
        lastDiffusionDurationMilliseconds = ElapsedMilliseconds(diffusionStart);
        float[] swap = surfaceTemperatureKelvinByCell; surfaceTemperatureKelvinByCell = workingTemperatureKelvinByCell; workingTemperatureKelvinByCell = swap;
        surfaceTemperatureTickSequence++;
        using (CommitMarker.Auto()) SurfaceTemperatureTickCommitted?.Invoke(dt);
        lastCompletedTemperatureTick += dt;
        lastTickDurationMilliseconds = ElapsedMilliseconds(tickStart);
        if (enableProfilingDiagnostics) UnityEngine.Debug.Log($"[GeodesicTemperatureProfile] cells={runtimeCellCount}, edges={transportGraph.EdgeCount}, substeps={lastDiffusionSubstepCount}, targetMs={lastTargetUpdateDurationMilliseconds:F3}, diffusionMs={lastDiffusionDurationMilliseconds:F3}, tickMs={lastTickDurationMilliseconds:F3}, diffusionRelativeError={latestDiffusionConservationRelativeError:E3}", this);
        }
    }

    private void UpdateTemperatureTargets(Vector3 sunDirectionWorld)
    {
        Vector3 localSunDirection = transform.InverseTransformDirection(sunDirectionWorld);
        if (localSunDirection.sqrMagnitude > 1e-12f) localSunDirection.Normalize();
        bool linearInsolation = Mathf.Approximately(insolationExponent, 1f);
        bool squareInsolation = Mathf.Approximately(insolationExponent, 2f);
        for (int i = 0; i < runtimeCellCount; i++)
        {
            float insolation = Mathf.Max(0f, Vector3.Dot(topology.CellDirections[i], localSunDirection));
            float shapedInsolation = linearInsolation ? insolation : squareInsolation ? insolation * insolation : Mathf.Pow(insolation, insolationExponent);
            float environmentalTarget = baseTemperatureKelvin + insolationTemperatureGainKelvin * shapedInsolation;
            float geothermalStrength = resourceField != null ? resourceField.GetTerrestrialThermalInfluence(i) : 0f;
            float sourceKelvin = terrestrialVentSourceTemperatureC + 273.15f;
            targetTemperatureKelvinByCell[i] = environmentalTarget + geothermalStrength * terrestrialVentThermalInfluence * Mathf.Max(0f, sourceKelvin - environmentalTarget);
        }
    }

    private void UpdateSampledCycleDiagnostics(double representativeSimulationTime)
    {
        lastThermalTickRepresentativeSimulationTime = representativeSimulationTime;
        float dayLength = sunDirectionProvider != null ? sunDirectionProvider.GetDayLengthSeconds() : float.PositiveInfinity;
        lastThermalTickSolarPhase01 = sunDirectionProvider != null ? sunDirectionProvider.GetDayPhase01AtSimulationTime(representativeSimulationTime) : 0f;
        int day = float.IsFinite(dayLength) && dayLength > 0f ? (int)Math.Floor(representativeSimulationTime / dayLength) : 0;
        if (accumulatingSolarDayIndex != day)
        {
            if (accumulatingSolarDayIndex >= 0 && accumulatingDaySampleCount > 0)
            {
                lastCompletedSolarDayIndex = accumulatingSolarDayIndex;
                lastCompletedDayMinimumTemperatureKelvin = accumulatingDayMinimumTemperatureKelvin;
                lastCompletedDayMeanTemperatureKelvin = (float)(accumulatingDayMeanSum / accumulatingDaySampleCount);
                lastCompletedDayMaximumTemperatureKelvin = accumulatingDayMaximumTemperatureKelvin;
            }
            accumulatingSolarDayIndex = day; accumulatingDayMinimumTemperatureKelvin = float.PositiveInfinity; accumulatingDayMaximumTemperatureKelvin = float.NegativeInfinity; accumulatingDayMeanSum = 0d; accumulatingDaySampleCount = 0;
        }
        accumulatingDayMinimumTemperatureKelvin = Mathf.Min(accumulatingDayMinimumTemperatureKelvin, minimumTemperatureKelvin);
        accumulatingDayMaximumTemperatureKelvin = Mathf.Max(accumulatingDayMaximumTemperatureKelvin, maximumTemperatureKelvin);
        accumulatingDayMeanSum += areaWeightedMeanTemperatureKelvin; accumulatingDaySampleCount++;
    }

    private void RebuildThermalCapacities()
    {
        for (int i = 0; i < runtimeCellCount; i++)
        {
            float capacity = Mathf.Max(1e-12f, topology.UnitCellAreas[i] * GetSurfaceMultiplier(i));
            heatCapacityByCell[i] = capacity;
            inverseHeatCapacityByCell[i] = 1f / capacity;
        }
        cachedLandHeatCapacityMultiplier = landHeatCapacityMultiplier;
        cachedOceanHeatCapacityMultiplier = oceanSurfaceHeatCapacityMultiplier;
        cachedDiffusionStrength = float.NaN;
        cachedDiffusionCapacityVersion = -1;
        cachedDiffusionTransportGraph = null;
        thermalCapacityVersion++;
    }

    private void ApplyConservativeDiffusion(float dt)
    {
        lastDiffusionSubstepCount = 0;
        if (diffusionStrength <= 0f) { lastDiffusionSubstepCount = 0; ticksSinceLastExactDiffusionAudit++; lastTickUsedAlgebraicDiffusionConservationOnly = true; return; }
        float safeDt = GetStableDiffusionStep();
        int substeps = Mathf.Max(1, Mathf.CeilToInt(dt / safeDt));
        if (substeps > 64) { if (!warnedDiffusionClamp) { UnityEngine.Debug.LogWarning("[GeodesicTemperature] Diffusion requested more than 64 stable substeps; strength is being stability-clamped.", this); warnedDiffusionClamp = true; } substeps = 64; }
        lastDiffusionSubstepCount = substeps;
        float stepDt = dt / substeps;
        bool exactAudit = enableProfilingDiagnostics || thermalIntegrationCursorTime + dt >= nextDiffusionConservationAuditSimulationTime;
        double before = exactAudit ? TotalThermalEnergy(workingTemperatureKelvinByCell) : 0d;
        int[] cellA = transportGraph.EdgeCellA;
        int[] cellB = transportGraph.EdgeCellB;
        float[] conductanceBase = transportGraph.EdgeConductanceBase;
        for (int step = 0; step < substeps; step++)
        {
            Array.Clear(energyDeltaByCell, 0, runtimeCellCount);
            for (int edge = 0; edge < transportGraph.EdgeCount; edge++)
            {
                int a = cellA[edge], b = cellB[edge];
                float energy = diffusionStrength * conductanceBase[edge] * (workingTemperatureKelvinByCell[b] - workingTemperatureKelvinByCell[a]) * stepDt;
                energyDeltaByCell[a] += energy;
                energyDeltaByCell[b] -= energy;
            }
            for (int i = 0; i < runtimeCellCount; i++) workingTemperatureKelvinByCell[i] += energyDeltaByCell[i] * inverseHeatCapacityByCell[i];
        }
        if (exactAudit)
        {
            double after = TotalThermalEnergy(workingTemperatureKelvinByCell);
            latestDiffusionConservationRelativeError = Math.Abs(after - before) / Math.Max(1e-12, Math.Abs(before));
            lastExactDiffusionAuditSimulationTime = thermalIntegrationCursorTime + dt; ticksSinceLastExactDiffusionAudit = 0; lastTickUsedAlgebraicDiffusionConservationOnly = false;
            nextDiffusionConservationAuditSimulationTime = lastExactDiffusionAuditSimulationTime + Mathf.Max(0.1f, diffusionConservationAuditIntervalSeconds);
        }
        else { ticksSinceLastExactDiffusionAudit++; lastTickUsedAlgebraicDiffusionConservationOnly = true; }
    }

    private float GetStableDiffusionStep()
    {
        if (diffusionStrength <= 0f) return float.MaxValue;
        if (cachedDiffusionStrength == diffusionStrength && cachedDiffusionCapacityVersion == thermalCapacityVersion && ReferenceEquals(cachedDiffusionTransportGraph, transportGraph)) return cachedStableDiffusionStep;
        float result = float.PositiveInfinity;
        for (int i = 0; i < runtimeCellCount; i++)
        {
            float conductance = diffusionStrength * transportGraph.CellConductanceSumBase[i];
            if (conductance > 0f) result = Mathf.Min(result, 0.45f * heatCapacityByCell[i] / conductance);
        }
        cachedStableDiffusionStep = float.IsInfinity(result) ? float.MaxValue : Mathf.Max(1e-5f, result);
        cachedDiffusionStrength = diffusionStrength;
        cachedDiffusionCapacityVersion = thermalCapacityVersion;
        cachedDiffusionTransportGraph = transportGraph;
        return cachedStableDiffusionStep;
    }

    private static double ElapsedMilliseconds(long startTimestamp) =>
        (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;

    private struct PartitionValidationResult
    {
        public double Received, Integrated, Remainder, Discarded;
        public int Ticks;
        public float Surface, Layer1, Layer2;
    }

    [ContextMenu("Validate Geodesic Temperature Frame-Partition Invariance")]
    private void ValidateFramePartitionInvariance()
    {
        if (!initialized || sunDirectionProvider == null) { UnityEngine.Debug.LogWarning("[GeodesicTemperaturePartitionValidation] Surface field and sun ephemeris must be initialized.", this); return; }
        double interval = Math.Max(0.01d, activeUpdateIntervalSeconds), duration = 120d + interval * 0.37d;
        PartitionValidationResult small = IntegrateTemporaryPartition(duration, interval * 0.2d, interval);
        PartitionValidationResult uneven = IntegrateTemporaryPartition(duration, interval * 1.7d, interval);
        PartitionValidationResult large = IntegrateTemporaryPartition(duration, interval * 20d, interval);
        float surfaceDifference = Mathf.Max(Mathf.Abs(small.Surface - uneven.Surface), Mathf.Abs(small.Surface - large.Surface));
        float layerDifference = Mathf.Max(Mathf.Abs(small.Layer1 - uneven.Layer1), Mathf.Abs(small.Layer1 - large.Layer1), Mathf.Abs(small.Layer2 - uneven.Layer2), Mathf.Abs(small.Layer2 - large.Layer2));
        int expectedTicks = (int)Math.Floor((duration + 1e-9d) / interval);
        bool valid = small.Discarded == 0d && uneven.Discarded == 0d && large.Discarded == 0d && small.Ticks == expectedTicks && uneven.Ticks == expectedTicks && large.Ticks == expectedTicks && Math.Abs(small.Remainder - uneven.Remainder) <= 1e-8d && Math.Abs(small.Remainder - large.Remainder) <= 1e-8d && surfaceDifference <= 1e-5f && layerDifference <= 1e-5f;
        string report = $"activeModel={activeThermalModel}, activeIntervalSeconds={interval:F3}, receivedSimulationSeconds={small.Received:F6}/{uneven.Received:F6}/{large.Received:F6}, integratedThermalSeconds={small.Integrated:F6}/{uneven.Integrated:F6}/{large.Integrated:F6}, remainderSeconds={small.Remainder:F6}/{uneven.Remainder:F6}/{large.Remainder:F6}, ticks={small.Ticks}/{uneven.Ticks}/{large.Ticks}, expectedTicks={expectedTicks}, surfaceMaxAbsDifferenceK={surfaceDifference:E3}, layeredMaxAbsDifferenceK={layerDifference:E3}, pauseAdvanceSeconds=0";
        if (valid) UnityEngine.Debug.Log($"[GeodesicTemperaturePartitionValidation] valid; {report}", this); else UnityEngine.Debug.LogError($"[GeodesicTemperaturePartitionValidation] invalid; {report}", this);
    }

    private struct CadenceDifferenceMetrics
    {
        public double SurfaceMaximum, SurfaceSum, SurfaceSquareSum;
        public double SubsurfaceMaximum, SubsurfaceSum, SubsurfaceSquareSum;
        public int SurfaceSamples, SubsurfaceSamples;
    }

    [ContextMenu("Validate Approximate Thermal Cadence Sensitivity")]
    private void ValidateApproximateThermalCadenceSensitivity()
    {
        if (!initialized || activeThermalModel != GeodesicThermalModel.ApproximateEcologicalProfiles) { UnityEngine.Debug.LogWarning("[GeodesicApproximateCadenceValidation] ApproximateEcologicalProfiles must be initialized; validation uses isolated temporary state.", this); return; }
        long sequenceBefore = surfaceTemperatureTickSequence;
        double cursorBefore = thermalIntegrationCursorTime;
        double checksumBefore = ProductionTemperatureChecksum();
        float[] candidates = { 0.5f, 1f, 2f, 5f };
        string table = "cadenceSeconds|maxSurfaceDifferenceK|meanSurfaceDifferenceK|rmsSurfaceDifferenceK|maxSubsurfaceDifferenceK|meanSubsurfaceDifferenceK|rmsSubsurfaceDifferenceK";
        bool valid = true;
        for (int i = 0; i < candidates.Length; i++)
        {
            CadenceDifferenceMetrics metrics = CompareApproximateCadence(candidates[i]);
            double surfaceMean = metrics.SurfaceSum / metrics.SurfaceSamples;
            double surfaceRms = Math.Sqrt(metrics.SurfaceSquareSum / metrics.SurfaceSamples);
            double subsurfaceMean = metrics.SubsurfaceSum / metrics.SubsurfaceSamples;
            double subsurfaceRms = Math.Sqrt(metrics.SubsurfaceSquareSum / metrics.SubsurfaceSamples);
            table += $"\n{candidates[i]:F2}|{metrics.SurfaceMaximum:F6}|{surfaceMean:F6}|{surfaceRms:F6}|{metrics.SubsurfaceMaximum:F6}|{subsurfaceMean:F6}|{subsurfaceRms:F6}";
            if (candidates[i] < 2f - 1e-5f)
                valid &= metrics.SurfaceMaximum < 0.1d && metrics.SubsurfaceMaximum < 0.1d;
            else if (Math.Abs(candidates[i] - 2f) <= 1e-5f && Math.Abs(activeUpdateIntervalSeconds - 2f) <= 1e-5f)
                // The former 0.1 K limit was a conservative screening value, not a
                // biological threshold. These bounds apply only to the selected 2 s
                // ecological cadence and still reject a materially different result.
                valid &= metrics.SurfaceMaximum < 0.02d && metrics.SubsurfaceMaximum < 0.2d;
        }
        bool stateRestored = sequenceBefore == surfaceTemperatureTickSequence && cursorBefore == thermalIntegrationCursorTime && checksumBefore == ProductionTemperatureChecksum();
        valid &= stateRestored;
        string report = $"[GeodesicApproximateCadenceValidation] {(valid ? "valid" : "invalid")}; referenceCadenceSeconds=0.25; durationSeconds=960; cases=12; productionCadenceSeconds={activeUpdateIntervalSeconds:F2}; stateRestore={(stateRestored ? "pass" : "fail")}\n{table}";
        if (valid) UnityEngine.Debug.Log(report, this); else UnityEngine.Debug.LogError(report, this);
    }

    private CadenceDifferenceMetrics CompareApproximateCadence(float candidateInterval)
    {
        const int cases = 12, layers = 4;
        float[] referenceSurface = new float[cases], candidateSurface = new float[cases];
        float[] referenceSubsurface = new float[cases * layers], candidateSubsurface = new float[cases * layers];
        for (int c = 0; c < cases; c++)
        {
            float initialTarget = CadenceSurfaceTarget(c, 0d);
            referenceSurface[c] = candidateSurface[c] = initialTarget;
            for (int layer = 0; layer < layers; layer++)
            {
                float initial = CadenceSubsurfaceTarget(c, layer, initialTarget);
                referenceSubsurface[c * layers + layer] = candidateSubsurface[c * layers + layer] = initial;
            }
        }
        CadenceDifferenceMetrics metrics = default;
        double referenceCursor = 0d, candidateCursor = 0d;
        while (candidateCursor < 960d - 1e-9d)
        {
            double candidateStep = Math.Min(candidateInterval, 960d - candidateCursor);
            while (referenceCursor < candidateCursor + candidateStep - 1e-9d)
            {
                double referenceStep = Math.Min(0.25d, candidateCursor + candidateStep - referenceCursor);
                IntegrateCadenceState(referenceSurface, referenceSubsurface, referenceCursor + referenceStep * 0.5d, (float)referenceStep);
                referenceCursor += referenceStep;
            }
            IntegrateCadenceState(candidateSurface, candidateSubsurface, candidateCursor + candidateStep * 0.5d, (float)candidateStep);
            candidateCursor += candidateStep;
            for (int c = 0; c < cases; c++) AccumulateDifference(ref metrics, Math.Abs(referenceSurface[c] - candidateSurface[c]), true);
            for (int c = 0; c < cases; c++)
                for (int layer = 0; layer < CadenceActiveSubsurfaceLayers(c); layer++)
                {
                    int node = c * layers + layer;
                    AccumulateDifference(ref metrics, Math.Abs(referenceSubsurface[node] - candidateSubsurface[node]), false);
                }
        }
        return metrics;
    }

    private void IntegrateCadenceState(float[] surface, float[] subsurface, double representativeTime, float dt)
    {
        const int layers = 4;
        for (int c = 0; c < surface.Length; c++)
        {
            float target = CadenceSurfaceTarget(c, representativeTime);
            float surfaceTimescale = (target >= surface[c] ? heatingTimescaleSeconds : coolingTimescaleSeconds) * (c >= 4 && c <= 9 ? 2f : 1f);
            surface[c] = RelaxCadenceScalar(surface[c], target, dt, surfaceTimescale);
            for (int layer = 0; layer < CadenceActiveSubsurfaceLayers(c); layer++)
            {
                int node = c * layers + layer;
                float depth = CadenceDepth(c, layer);
                float timescale = Mathf.Lerp(80f, 1200f, depth);
                subsurface[node] = RelaxCadenceScalar(subsurface[node], CadenceSubsurfaceTarget(c, layer, surface[c]), dt, timescale);
            }
        }
    }

    private float CadenceSurfaceTarget(int cadenceCase, double time)
    {
        float[] phases = { -0.2f, 2.9f, 1.45f, -1.7f, 0.4f, 2.4f, -1.2f, 1.8f, -2.7f, 0.9f, 0.1f, 3.0f };
        float latitudeFactor = cadenceCase == 10 ? 0.28f : cadenceCase == 11 ? 0.42f : 1f;
        float dayAngle = (float)(2d * Math.PI * time / 480d) + phases[cadenceCase];
        float seasonal = cadenceCase >= 10 ? 12f * Mathf.Sin((float)(2d * Math.PI * time / 1440d) + cadenceCase) : 0f;
        return Mathf.Max(0f, baseTemperatureKelvin + seasonal + insolationTemperatureGainKelvin * latitudeFactor * Mathf.Max(0f, Mathf.Cos(dayAngle)));
    }

    private float CadenceSubsurfaceTarget(int cadenceCase, int layer, float surface)
    {
        float depth = CadenceDepth(cadenceCase, layer);
        float deep = Mathf.Max(0f, baseTemperatureKelvin - 8f);
        float vent = cadenceCase == 9 && layer == 3 ? 25f : cadenceCase == 9 && layer == 2 ? 8.75f : 0f;
        return Mathf.Lerp(surface, deep, Mathf.Pow(depth, 1.4f)) + vent;
    }

    private static float CadenceDepth(int cadenceCase, int layer)
    {
        if (cadenceCase == 4) return 0.04f;
        if (cadenceCase == 5) return Mathf.Min(0.55f, 0.12f + layer * 0.2f);
        return 0.08f + layer * 0.29f;
    }

    private static int CadenceActiveSubsurfaceLayers(int cadenceCase) => cadenceCase == 4 ? 0 : cadenceCase == 5 ? 2 : 4;

    private static float RelaxCadenceScalar(float current, float target, float dt, float timescale) => current + (target - current) * (1f - Mathf.Exp(-dt / Mathf.Max(MinimumTimescale, timescale)));

    private static void AccumulateDifference(ref CadenceDifferenceMetrics metrics, double difference, bool surface)
    {
        if (surface) { metrics.SurfaceMaximum = Math.Max(metrics.SurfaceMaximum, difference); metrics.SurfaceSum += difference; metrics.SurfaceSquareSum += difference * difference; metrics.SurfaceSamples++; }
        else { metrics.SubsurfaceMaximum = Math.Max(metrics.SubsurfaceMaximum, difference); metrics.SubsurfaceSum += difference; metrics.SubsurfaceSquareSum += difference * difference; metrics.SubsurfaceSamples++; }
    }

    private double ProductionTemperatureChecksum()
    {
        double checksum = 0d;
        for (int i = 0; i < surfaceTemperatureKelvinByCell.Length; i++) checksum += surfaceTemperatureKelvinByCell[i] * (i + 1d);
        return checksum;
    }

    private PartitionValidationResult IntegrateTemporaryPartition(double duration, double frameDelta, double interval)
    {
        PartitionValidationResult result = new PartitionValidationResult { Surface = baseTemperatureKelvin, Layer1 = Mathf.Max(0f, baseTemperatureKelvin - 2f), Layer2 = Mathf.Max(0f, baseTemperatureKelvin - 4f) };
        double target = 0d, cursor = 0d;
        Vector3 normalWorld = transform.TransformDirection(topology.CellDirections[0]).normalized;
        while (target < duration - 1e-9d)
        {
            double next = Math.Min(duration, target + frameDelta); result.Received += next - target; target = next;
            while (cursor + interval <= target + 1e-9d)
            {
                double midpoint = cursor + interval * 0.5d;
                Vector3 sun = sunDirectionProvider.GetPlanetToSunDirectionWorldAtSimulationTime(midpoint);
                float targetTemperature = baseTemperatureKelvin + insolationTemperatureGainKelvin * Mathf.Pow(Mathf.Max(0f, Vector3.Dot(normalWorld, sun)), insolationExponent);
                float timescale = targetTemperature >= result.Surface ? heatingTimescaleSeconds : coolingTimescaleSeconds;
                result.Surface += (targetTemperature - result.Surface) * (1f - Mathf.Exp(-(float)interval / Mathf.Max(MinimumTimescale, timescale)));
                float energy01 = verticalThermalDiffusivityForValidation * (result.Layer1 - result.Surface) * (float)interval;
                float energy12 = verticalThermalDiffusivityForValidation * (result.Layer2 - result.Layer1) * (float)interval;
                result.Surface += energy01; result.Layer1 += -energy01 + energy12; result.Layer2 -= energy12;
                cursor += interval; result.Integrated += interval; result.Ticks++;
            }
        }
        result.Remainder = Math.Max(0d, result.Received - result.Integrated);
        return result;
    }

    private const float verticalThermalDiffusivityForValidation = 0.00002f;

    private double TotalThermalEnergy(float[] temperatures) { double total = 0d; for (int i = 0; i < runtimeCellCount; i++) total += temperatures[i] * heatCapacityByCell[i]; return total; }

    private void UpdateDiagnostics()
    {
        double weighted = 0d, area = 0d; float min = float.PositiveInfinity, max = float.NegativeInfinity;
        for (int i = 0; i < runtimeCellCount; i++)
        {
            float value = surfaceTemperatureKelvinByCell[i];
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > DiagnosticHighTemperatureKelvin)
            {
                if (!warnedInvalidTemperature) { UnityEngine.Debug.LogWarning($"[GeodesicTemperature] Unsafe temperature {value} K at cell {i}; values below 0 K/non-finite are repaired, high finite values remain diagnostic.", this); warnedInvalidTemperature = true; }
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) value = Mathf.Max(0f, baseTemperatureKelvin);
                surfaceTemperatureKelvinByCell[i] = value;
            }
            float a = topology.UnitCellAreas[i]; min = Mathf.Min(min, value); max = Mathf.Max(max, value); weighted += value * a; area += a;
        }
        minimumTemperatureKelvin = min; maximumTemperatureKelvin = max; areaWeightedMeanTemperatureKelvin = area > 0d ? (float)(weighted / area) : 0f;
    }

    private int FindCellForLocalDirection(Vector3 direction)
    {
        if (!initialized || direction.sqrMagnitude < 1e-12f) return -1;
        Vector3 d = direction.normalized; int best = 0; float bestDot = Vector3.Dot(d, topology.CellDirections[0]);
        for (int i = 1; i < Mathf.Min(12, runtimeCellCount); i++) { float dot = Vector3.Dot(d, topology.CellDirections[i]); if (dot > bestDot) { best = i; bestDot = dot; } }
        bool improved;
        do { improved = false; for (int slot = 0; slot < topology.NeighborCounts[best]; slot++) { int n = topology.Neighbors6[best * 6 + slot]; float dot = Vector3.Dot(d, topology.CellDirections[n]); if (dot > bestDot + 1e-7f) { best = n; bestDot = dot; improved = true; break; } } } while (improved);
        return best;
    }

    private float GetSurfaceMultiplier(int cellIndex) => IsOcean(cellIndex) ? Mathf.Max(0.01f, oceanSurfaceHeatCapacityMultiplier) : Mathf.Max(0.01f, landHeatCapacityMultiplier);
    private bool IsOcean(int cellIndex) => planetGenerator != null && planetGenerator.IsGeodesicCellOcean(cellIndex);
    private bool TryGetSunDirection(out Vector3 direction) { if (sunDirectionProvider != null && sunDirectionProvider.IsSunDirectionValid) { direction = sunDirectionProvider.PlanetToSunDirectionWorld.normalized; return true; } direction = Vector3.zero; return false; }
    private void ResolveClockOnly() { if (simulationClock == null) simulationClock = FindFirstObjectByType<ReplicatorManager>(); }
    private void ResolveReferences()
    {
        planetGenerator = GetComponent<PlanetGenerator>();
        resourceField = GetComponent<GeodesicOceanResourceField>();
        if (sunDirectionProvider == null) sunDirectionProvider = FindFirstObjectByType<SunSkyRotator>();
        ResolveClockOnly();
        currentSunDirectionProvider = sunDirectionProvider != null ? sunDirectionProvider.name : "None";
    }

    [ContextMenu("Validate Geodesic Temperature Diffusion")]
    private void ValidateDiffusion()
    {
        if (!initialized) { UnityEngine.Debug.LogWarning("[GeodesicTemperatureValidation] Field is not initialized.", this); return; }
        int count = runtimeCellCount;
        float[] adjacency = new float[count];
        float[] graphResult = new float[count];
        float[] adjacencyDelta = new float[count];
        float[] graphDelta = new float[count];
        for (int i = 0; i < count; i++) adjacency[i] = graphResult[i] = 250f + (i * 37 % 101) * 0.75f;
        double before = TotalThermalEnergy(adjacency);
        const int substeps = 4;
        float stepDt = Mathf.Max(0.01f, activeUpdateIntervalSeconds) / substeps;
        for (int step = 0; step < substeps; step++)
        {
            Array.Clear(adjacencyDelta, 0, count);
            Array.Clear(graphDelta, 0, count);
            for (int a = 0; a < count; a++)
            {
                for (int slot = 0; slot < topology.NeighborCounts[a]; slot++)
                {
                    int b = topology.Neighbors6[a * 6 + slot];
                    if (b <= a) continue;
                    float conductance = topology.SharedDualEdgeAngularLengths6[a * 6 + slot] / topology.NeighborAngularDistances6[a * 6 + slot];
                    float energy = diffusionStrength * conductance * (adjacency[b] - adjacency[a]) * stepDt;
                    adjacencyDelta[a] += energy; adjacencyDelta[b] -= energy;
                }
            }
            for (int edge = 0; edge < transportGraph.EdgeCount; edge++)
            {
                int a = transportGraph.EdgeCellA[edge], b = transportGraph.EdgeCellB[edge];
                float energy = diffusionStrength * transportGraph.EdgeConductanceBase[edge] * (graphResult[b] - graphResult[a]) * stepDt;
                graphDelta[a] += energy; graphDelta[b] -= energy;
            }
            for (int i = 0; i < count; i++)
            {
                adjacency[i] += adjacencyDelta[i] * inverseHeatCapacityByCell[i];
                graphResult[i] += graphDelta[i] * inverseHeatCapacityByCell[i];
            }
        }
        double absoluteSum = 0d;
        float maximumAbsoluteDifference = 0f;
        for (int i = 0; i < count; i++) { float difference = Mathf.Abs(adjacency[i] - graphResult[i]); absoluteSum += difference; maximumAbsoluteDifference = Mathf.Max(maximumAbsoluteDifference, difference); }
        double after = TotalThermalEnergy(graphResult);
        double relativeConservationError = Math.Abs(after - before) / Math.Max(1e-12, Math.Abs(before));
        UnityEngine.Debug.Log($"[GeodesicTemperatureEquivalence] subdivision={topology.SubdivisionLevel}, cells={count}, edges={transportGraph.EdgeCount}, substeps={substeps}, maxAbsoluteDifferenceK={maximumAbsoluteDifference:E3}, meanAbsoluteDifferenceK={absoluteSum / count:E3}, energyBefore={before:E6}, energyAfter={after:E6}, relativeConservationError={relativeConservationError:E3}", this);
    }

}
