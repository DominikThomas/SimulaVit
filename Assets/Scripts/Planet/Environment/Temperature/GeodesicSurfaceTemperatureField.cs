using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Unity.Profiling;

/// <summary>Authoritative, surface-only Kelvin field for geodesic simulation cells.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicSurfaceTemperatureField : MonoBehaviour
{
    private static readonly ProfilerMarker TickMarker = new ProfilerMarker("GeodesicTemperature.SurfaceTick");
    private static readonly ProfilerMarker TargetMarker = new ProfilerMarker("GeodesicTemperature.TargetUpdate");
    private static readonly ProfilerMarker ResponseMarker = new ProfilerMarker("GeodesicTemperature.SurfaceResponse");
    private static readonly ProfilerMarker DiffusionMarker = new ProfilerMarker("GeodesicTemperature.HorizontalDiffusion");
    private static readonly ProfilerMarker CommitMarker = new ProfilerMarker("GeodesicTemperature.CommittedEvent");
    public event Action<float> SurfaceTemperatureTickCommitted;
    public event Action SurfaceTemperatureFieldReinitialized;
    public event Action SurfaceTemperatureFieldClearing;
    private const float MinimumTimescale = 0.001f;
    private const float DiagnosticHighTemperatureKelvin = 2000f;

    [Header("Geodesic Surface Temperature")]
    [SerializeField, Tooltip("Enables one physical surface-temperature value per geodesic simulation cell. This does not enable ocean layers or ice.")] private bool enableGeodesicSurfaceTemperature = true;
    [SerializeField, Min(0.01f), Tooltip("Fixed authoritative simulation seconds per temperature tick. Independent of simulation speed and rendered FPS.")] private float updateIntervalSeconds = 1f;
    [SerializeField, Range(1, 512), Tooltip("Emergency per-rendered-frame catch-up guard. Remaining authoritative time stays as explicit backlog and is never discarded.")] private int maximumThermalTicksPerFrame = 64;
    [SerializeField, Min(0.05f), Tooltip("Unscaled real-time interval for cached global surface diagnostics used by the HUD and Inspector.")] private float diagnosticSnapshotIntervalSeconds = 0.25f;
    [SerializeField, Min(0.1f), Tooltip("Simulation-time interval between exact surface-diffusion conservation audits.")] private float diffusionConservationAuditIntervalSeconds = 5f;
    [SerializeField, Min(MinimumTimescale), Tooltip("Surface-only warming relaxation timescale in simulation seconds.")] private float heatingTimescaleSeconds = 20f;
    [SerializeField, Min(MinimumTimescale), Tooltip("Surface-only cooling relaxation timescale in simulation seconds.")] private float coolingTimescaleSeconds = 35f;
    [SerializeField, Min(0f), Tooltip("Optional temporary approximation of unresolved horizontal surface heat transport. Uses the shared geodesic transport graph. This is not ocean-current, atmospheric-wind, or vent-plume transport.")] private float diffusionStrength = 0.002f;
    [SerializeField, Min(0.01f), Tooltip("Land surface heat-capacity multiplier used by inertia and diffusion.")] private float landHeatCapacityMultiplier = 1f;
    [SerializeField, Min(0.01f), Tooltip("Ocean surface heat-capacity multiplier. This is not vertical ocean heat storage.")] private float oceanSurfaceHeatCapacityMultiplier = 2f;
    [SerializeField, Range(0.1f, 4f), Tooltip("Exponent applied to direct insolation in the interim surface-energy approximation.")] private float insolationExponent = 1f;
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

    private PlanetGenerator planetGenerator;
    private SunSkyRotator sunDirectionProvider;
    private ReplicatorManager simulationClock;
    private GeodesicGridTopology topology;
    private GeodesicTransportGraph transportGraph;
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

    private void Awake() => ResolveReferences();

    private void Update()
    {
        if (!initialized || !enableGeodesicSurfaceTemperature || planetGenerator.CurrentGridType != PlanetGridType.GeodesicIcosphere) return;
        ResolveClockOnly();
        if (simulationClock == null) return;
        double target = Math.Max(0d, simulationClock.SimulationTimeSeconds);
        currentAuthoritativeSimulationTime = target;
        thermalTicksCurrentRenderedFrame = 0;
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
        double interval = Math.Max(0.01d, updateIntervalSeconds);
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
        surfaceTemperatureKelvinByCell = new float[count];
        targetTemperatureKelvinByCell = new float[count];
        workingTemperatureKelvinByCell = new float[count];
        energyDeltaByCell = new float[count];
        heatCapacityByCell = new float[count];
        inverseHeatCapacityByCell = new float[count];
        runtimeCellCount = count;
        double simulationTime = simulationClock != null ? Math.Max(0d, simulationClock.SimulationTimeSeconds) : 0d;
        thermalIntegrationCursorTime = lastObservedAuthoritativeSimulationTime = currentAuthoritativeSimulationTime = simulationTime;
        totalAuthoritativeSimulationSecondsReceived = totalSimulationSecondsConsumedByThermalTicks = unconsumedThermalRemainderSeconds = discardedSimulationSeconds = 0d;
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
        UnityEngine.Debug.Log($"[GeodesicTemperature] initialized cells={count}, baseK={baseTemperatureKelvin:F2}, gainK={insolationTemperatureGainKelvin:F2}, min/mean/maxK={minimumTemperatureKelvin:F2}/{areaWeightedMeanTemperatureKelvin:F2}/{maximumTemperatureKelvin:F2}", this);
    }

    public void ClearField()
    {
        if (initialized) SurfaceTemperatureFieldClearing?.Invoke();
        initialized = false;
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
        long diffusionStart = Stopwatch.GetTimestamp();
        using (DiffusionMarker.Auto()) ApplyConservativeDiffusion(dt);
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
            targetTemperatureKelvinByCell[i] = baseTemperatureKelvin + insolationTemperatureGainKelvin * shapedInsolation;
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
        public double Received, Integrated, Discarded;
        public int Ticks;
        public float Surface, Layer1, Layer2;
    }

    [ContextMenu("Validate Geodesic Temperature Frame-Partition Invariance")]
    private void ValidateFramePartitionInvariance()
    {
        if (!initialized || sunDirectionProvider == null) { UnityEngine.Debug.LogWarning("[GeodesicTemperaturePartitionValidation] Surface field and sun ephemeris must be initialized.", this); return; }
        double interval = Math.Max(0.01d, updateIntervalSeconds), duration = interval * 480d;
        PartitionValidationResult small = IntegrateTemporaryPartition(duration, interval * 0.2d, interval);
        PartitionValidationResult large = IntegrateTemporaryPartition(duration, interval * 20d, interval);
        float surfaceDifference = Mathf.Abs(small.Surface - large.Surface);
        float layerDifference = Mathf.Max(Mathf.Abs(small.Layer1 - large.Layer1), Mathf.Abs(small.Layer2 - large.Layer2));
        float maximumDifference = Mathf.Max(surfaceDifference, layerDifference);
        bool valid = small.Discarded == 0d && large.Discarded == 0d && small.Ticks == large.Ticks && maximumDifference <= 0.01f;
        string report = $"received={small.Received:F6}/{large.Received:F6}, integrated={small.Integrated:F6}/{large.Integrated:F6}, discarded={small.Discarded:F6}/{large.Discarded:F6}, ticks={small.Ticks}/{large.Ticks}, surfaceMaxAbsK={surfaceDifference:E3}, layeredMaxAbsK={layerDifference:E3}";
        if (valid) UnityEngine.Debug.Log($"[GeodesicTemperaturePartitionValidation] valid; {report}", this); else UnityEngine.Debug.LogError($"[GeodesicTemperaturePartitionValidation] invalid; {report}", this);
    }

    [ContextMenu("Compare Representative Geodesic Thermal Intervals")]
    private void CompareThermalIntervals()
    {
        if (!initialized || sunDirectionProvider == null) { UnityEngine.Debug.LogWarning("[GeodesicThermalIntervalValidation] Surface field and sun ephemeris must be initialized.", this); return; }
        double dayLength = sunDirectionProvider.GetDayLengthSeconds();
        double duration = double.IsInfinity(dayLength) ? 480d : Math.Max(120d, dayLength * 3d);
        PartitionValidationResult reference = IntegrateTemporaryPartition(duration, 0.25d, 0.25d);
        PartitionValidationResult halfSecond = IntegrateTemporaryPartition(duration, 0.5d, 0.5d);
        PartitionValidationResult oneSecond = IntegrateTemporaryPartition(duration, 1d, 1d);
        float halfError = MaximumRepresentativeDifference(reference, halfSecond);
        float oneError = MaximumRepresentativeDifference(reference, oneSecond);
        bool halfPass = halfError <= 0.1f, onePass = oneError <= 0.1f;
        UnityEngine.Debug.Log($"[GeodesicRepresentativeThermalIntervalValidation] lightweight representative-state comparison only; referenceTicks={reference.Ticks}, 0.5sTicks={halfSecond.Ticks}, 1.0sTicks={oneSecond.Ticks}, maxRepresentativeDifferenceK(0.5/1.0)={halfError:F4}/{oneError:F4}, accepted(<=0.1K)={halfPass}/{onePass}, productionInterval={updateIntervalSeconds:F2}s. Full-field Unity comparison remains authoritative.", this);
    }

    private static float MaximumRepresentativeDifference(PartitionValidationResult a, PartitionValidationResult b)
    {
        return Mathf.Max(Mathf.Abs(a.Surface - b.Surface), Mathf.Abs(a.Layer1 - b.Layer1), Mathf.Abs(a.Layer2 - b.Layer2));
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
        float stepDt = Mathf.Max(0.01f, updateIntervalSeconds) / substeps;
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
