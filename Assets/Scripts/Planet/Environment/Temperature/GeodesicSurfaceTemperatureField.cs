using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>Authoritative, surface-only Kelvin field for geodesic simulation cells.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicSurfaceTemperatureField : MonoBehaviour
{
    public event Action<float> SurfaceTemperatureTickCommitted;
    public event Action SurfaceTemperatureFieldReinitialized;
    public event Action SurfaceTemperatureFieldClearing;
    private const float MinimumTimescale = 0.001f;
    private const float DiagnosticHighTemperatureKelvin = 2000f;

    [Header("Geodesic Surface Temperature")]
    [SerializeField, Tooltip("Enables one physical surface-temperature value per geodesic simulation cell. This does not enable ocean layers or ice.")] private bool enableGeodesicSurfaceTemperature = true;
    [SerializeField, Min(0.01f), Tooltip("Authoritative simulation seconds accumulated between temperature ticks.")] private float updateIntervalSeconds = 0.25f;
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
    private float accumulatedSimulationSeconds;
    private float baseTemperatureKelvin = 273.15f;
    private float insolationTemperatureGainKelvin = 45f;
    private bool warnedInvalidTemperature;
    private bool warnedDiffusionClamp;

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

    private void Awake() => ResolveReferences();

    private void Update()
    {
        if (!initialized || !enableGeodesicSurfaceTemperature || planetGenerator.CurrentGridType != PlanetGridType.GeodesicIcosphere) return;
        ResolveClockOnly();
        float elapsed = simulationClock != null ? Mathf.Max(0f, simulationClock.FrameSimulationDeltaTime) : 0f;
        if (elapsed <= 0f) return;
        accumulatedSimulationSeconds = Mathf.Min(accumulatedSimulationSeconds + elapsed, Mathf.Max(updateIntervalSeconds, 0.01f) * 4f);
        float interval = Mathf.Max(0.01f, updateIntervalSeconds);
        while (accumulatedSimulationSeconds >= interval)
        {
            TickTemperature(interval);
            accumulatedSimulationSeconds -= interval;
        }
    }

    public void ConfigureStartupTemperatures(float baseKelvin, float insolationGainKelvin)
    {
        baseTemperatureKelvin = Mathf.Max(0f, baseKelvin);
        insolationTemperatureGainKelvin = Mathf.Max(0f, insolationGainKelvin);
        if (planetGenerator != null && planetGenerator.CurrentGridType == PlanetGridType.GeodesicIcosphere) InitializeForCurrentTopology();
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
        accumulatedSimulationSeconds = 0f;
        RebuildThermalCapacities();
        UpdateTemperatureTargets();
        Array.Copy(targetTemperatureKelvinByCell, surfaceTemperatureKelvinByCell, count);
        UpdateDiagnostics();
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
        accumulatedSimulationSeconds = 0f;
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

    private void TickTemperature(float dt)
    {
        long tickStart = Stopwatch.GetTimestamp();
        if (cachedLandHeatCapacityMultiplier != landHeatCapacityMultiplier || cachedOceanHeatCapacityMultiplier != oceanSurfaceHeatCapacityMultiplier) RebuildThermalCapacities();
        long targetStart = Stopwatch.GetTimestamp();
        UpdateTemperatureTargets();
        lastTargetUpdateDurationMilliseconds = ElapsedMilliseconds(targetStart);
        for (int i = 0; i < runtimeCellCount; i++)
        {
            float current = surfaceTemperatureKelvinByCell[i];
            float target = targetTemperatureKelvinByCell[i];
            float timescale = (target >= current ? heatingTimescaleSeconds : coolingTimescaleSeconds) * GetSurfaceMultiplier(i);
            float response = 1f - Mathf.Exp(-dt / Mathf.Max(MinimumTimescale, timescale));
            workingTemperatureKelvinByCell[i] = current + (target - current) * response;
        }
        long diffusionStart = Stopwatch.GetTimestamp();
        ApplyConservativeDiffusion(dt);
        lastDiffusionDurationMilliseconds = ElapsedMilliseconds(diffusionStart);
        float[] swap = surfaceTemperatureKelvinByCell; surfaceTemperatureKelvinByCell = workingTemperatureKelvinByCell; workingTemperatureKelvinByCell = swap;
        SurfaceTemperatureTickCommitted?.Invoke(dt);
        UpdateDiagnostics();
        lastCompletedTemperatureTick += dt;
        lastTickDurationMilliseconds = ElapsedMilliseconds(tickStart);
        if (enableProfilingDiagnostics) UnityEngine.Debug.Log($"[GeodesicTemperatureProfile] cells={runtimeCellCount}, edges={transportGraph.EdgeCount}, substeps={lastDiffusionSubstepCount}, targetMs={lastTargetUpdateDurationMilliseconds:F3}, diffusionMs={lastDiffusionDurationMilliseconds:F3}, tickMs={lastTickDurationMilliseconds:F3}, diffusionRelativeError={latestDiffusionConservationRelativeError:E3}", this);
    }

    private void UpdateTemperatureTargets()
    {
        for (int i = 0; i < runtimeCellCount; i++)
        {
            float insolation = GetCellInsolationCosine(i);
            targetTemperatureKelvinByCell[i] = baseTemperatureKelvin + insolationTemperatureGainKelvin * Mathf.Pow(insolation, insolationExponent);
        }
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
    }

    private void ApplyConservativeDiffusion(float dt)
    {
        lastDiffusionSubstepCount = 0;
        if (diffusionStrength <= 0f) { latestDiffusionConservationRelativeError = 0d; return; }
        float safeDt = GetStableDiffusionStep();
        int substeps = Mathf.Max(1, Mathf.CeilToInt(dt / safeDt));
        if (substeps > 64) { if (!warnedDiffusionClamp) { UnityEngine.Debug.LogWarning("[GeodesicTemperature] Diffusion requested more than 64 stable substeps; strength is being stability-clamped.", this); warnedDiffusionClamp = true; } substeps = 64; }
        lastDiffusionSubstepCount = substeps;
        float stepDt = dt / substeps;
        double before = TotalThermalEnergy(workingTemperatureKelvinByCell);
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
        double after = TotalThermalEnergy(workingTemperatureKelvinByCell);
        latestDiffusionConservationRelativeError = Math.Abs(after - before) / Math.Max(1e-12, Math.Abs(before));
    }

    private float GetStableDiffusionStep()
    {
        if (diffusionStrength <= 0f) return float.MaxValue;
        if (cachedDiffusionStrength == diffusionStrength) return cachedStableDiffusionStep;
        float result = float.PositiveInfinity;
        for (int i = 0; i < runtimeCellCount; i++)
        {
            float conductance = diffusionStrength * transportGraph.CellConductanceSumBase[i];
            if (conductance > 0f) result = Mathf.Min(result, 0.45f * heatCapacityByCell[i] / conductance);
        }
        cachedStableDiffusionStep = float.IsInfinity(result) ? float.MaxValue : Mathf.Max(1e-5f, result);
        cachedDiffusionStrength = diffusionStrength;
        return cachedStableDiffusionStep;
    }

    private static double ElapsedMilliseconds(long startTimestamp) =>
        (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;

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
