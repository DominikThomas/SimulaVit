using System;
using System.Diagnostics;
using UnityEngine;

public enum GeodesicOceanTemperatureStartupMode { IsothermalFromSurface, DepthGradient }

/// <summary>Authoritative persistent temperature for active geodesic ocean layers 1-4; layer 0 reads through to the surface field.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanTemperatureField : MonoBehaviour
{
    private const float StabilitySafetyFactor = 0.45f;
    private const double ConservationTolerance = 2e-5;

    [Header("Geodesic Ocean Temperature")]
    [SerializeField, Tooltip("Enables persistent geodesic subsurface ocean temperatures. Legacy cube-sphere simulation is unaffected.")] private bool enableGeodesicOceanTemperature = true;
    [SerializeField, Min(1e-8f), Tooltip("Thermal capacity per unit ocean-layer volume.")] private float subsurfaceHeatCapacityPerVolume = 1f;
    [SerializeField, Min(0f), Tooltip("Simulation-unit vertical diffusivity; this is not SI calibrated and will be tuned later.")] private float verticalThermalDiffusivity = 0.00002f;
    [SerializeField, Tooltip("Initializes subsurface layers from the surface or with a per-layer depth gradient.")] private GeodesicOceanTemperatureStartupMode startupMode = GeodesicOceanTemperatureStartupMode.DepthGradient;
    [SerializeField, Min(0f), Tooltip("Initial Kelvin decrease per layer index when Depth Gradient is selected.")] private float initialTemperatureDropPerLayerKelvin = 2f;
    [SerializeField, Range(1, 256), Tooltip("Maximum stable explicit vertical-exchange substeps per surface tick.")] private int maximumVerticalSubsteps = 64;
    [SerializeField, Tooltip("Logs vertical tick timing and conservation diagnostics.")] private bool enableProfilingDiagnostics;

    [Header("Runtime Diagnostics (Read Only)")]
    [SerializeField] private bool initialized;
    [SerializeField] private int activeSubsurfaceNodeCount;
    [SerializeField] private double totalSubsurfaceThermalCapacity;
    [SerializeField] private string sourceGridSummary = "None";
    [SerializeField] private double lastCompletedOceanTemperatureTick;
    [SerializeField] private float lastVerticalExchangeSimulationDelta;
    [SerializeField] private int lastVerticalSubstepCount;
    [SerializeField] private double lastVerticalExchangeDurationMilliseconds;
    [SerializeField] private double latestVerticalConservationRelativeError;
    [SerializeField] private double lastAbsoluteEnergyTransferred;
    [SerializeField] private bool lastStabilityClampingOccurred;
    [SerializeField] private float maximumSurfaceToBottomTemperatureDifference;
    [SerializeField] private long approximateRuntimeMemoryBytes;
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
    private float[] conductanceSumBaseByNode;
    private float[] energyDeltaByNode;
    private float[] surfaceEnergyDeltaByCell;
    private float[] surfaceConductanceSumBaseByCell;
    private float cachedCapacityPerVolume;
    private bool warnedStabilityClamp;
    private readonly double[] diagnosticWeightedTemperature = new double[5];
    private readonly double[] diagnosticWeight = new double[5];

    public bool IsInitialized => initialized;
    public GeodesicOceanLayerGrid SourceGrid => sourceGrid;
    public float LastVerticalExchangeSimulationDelta => lastVerticalExchangeSimulationDelta;
    public int LastVerticalSubstepCount => lastVerticalSubstepCount;
    public double LastVerticalExchangeDurationMilliseconds => lastVerticalExchangeDurationMilliseconds;
    public double LatestVerticalConservationRelativeError => latestVerticalConservationRelativeError;
    public double LastAbsoluteEnergyTransferred => lastAbsoluteEnergyTransferred;
    public bool LastStabilityClampingOccurred => lastStabilityClampingOccurred;
    public float MaximumSurfaceToBottomTemperatureDifference => maximumSurfaceToBottomTemperatureDifference;
    public int ActiveSubsurfaceNodeCount => activeSubsurfaceNodeCount;
    public double TotalSubsurfaceThermalCapacity => totalSubsurfaceThermalCapacity;
    public double LastCompletedOceanTemperatureTick => lastCompletedOceanTemperatureTick;
    public long ApproximateRuntimeMemoryBytes => approximateRuntimeMemoryBytes;

    private void Awake() { generator = GetComponent<PlanetGenerator>(); domain = GetComponent<GeodesicOceanLayerDomain>(); surfaceField = GetComponent<GeodesicSurfaceTemperatureField>(); }
    private void OnDestroy() => ClearField();

    public void InitializeForCurrentDomain()
    {
        Unsubscribe();
        sourceGrid = domain != null ? domain.Grid : null;
        if (!enableGeodesicOceanTemperature || generator == null || generator.CurrentGridType != PlanetGridType.GeodesicIcosphere || sourceGrid == null || surfaceField == null || !surfaceField.IsInitialized || !ReferenceEquals(sourceGrid.SourceTopology, generator.GeodesicTopology)) { ClearState(); return; }
        int capacity = sourceGrid.NodeCapacity;
        subsurfaceTemperatureKelvinByNode = new float[capacity];
        heatCapacityByNode = new float[capacity]; inverseHeatCapacityByNode = new float[capacity]; conductanceSumBaseByNode = new float[capacity]; energyDeltaByNode = new float[capacity];
        surfaceEnergyDeltaByCell = new float[sourceGrid.CellCount]; surfaceConductanceSumBaseByCell = new float[sourceGrid.CellCount];
        RebuildCapacitiesAndConductance();
        for (int cell = 0; cell < sourceGrid.CellCount; cell++)
        {
            float surface = surfaceField.GetCellTemperatureKelvin(cell);
            for (int layer = 1; layer < sourceGrid.ActiveLayerCountByCell[cell]; layer++)
                subsurfaceTemperatureKelvinByNode[sourceGrid.GetNodeIndex(cell, layer)] = Mathf.Max(0f, surface - (startupMode == GeodesicOceanTemperatureStartupMode.DepthGradient ? initialTemperatureDropPerLayerKelvin * layer : 0f));
        }
        initialized = true; Subscribe(); UpdateDiagnostics();
        UnityEngine.Debug.Log($"[GeodesicOceanTemperature] initialized {sourceGridSummary}, subsurfaceNodes={activeSubsurfaceNodeCount}, capacity={totalSubsurfaceThermalCapacity:E4}, memory={approximateRuntimeMemoryBytes} bytes", this);
    }

    public void ClearField() { Unsubscribe(); ClearState(); }
    private void ClearState()
    {
        initialized = false; sourceGrid = null; subsurfaceTemperatureKelvinByNode = heatCapacityByNode = inverseHeatCapacityByNode = conductanceSumBaseByNode = energyDeltaByNode = surfaceEnergyDeltaByCell = surfaceConductanceSumBaseByCell = null;
        activeSubsurfaceNodeCount = 0; totalSubsurfaceThermalCapacity = 0d; sourceGridSummary = "None"; approximateRuntimeMemoryBytes = 0;
    }
    private void Subscribe() { surfaceField.SurfaceTemperatureTickCommitted -= OnSurfaceTickCommitted; surfaceField.SurfaceTemperatureFieldReinitialized -= OnSurfaceReinitialized; surfaceField.SurfaceTemperatureFieldClearing -= OnSurfaceClearing; surfaceField.SurfaceTemperatureTickCommitted += OnSurfaceTickCommitted; surfaceField.SurfaceTemperatureFieldReinitialized += OnSurfaceReinitialized; surfaceField.SurfaceTemperatureFieldClearing += OnSurfaceClearing; }
    private void Unsubscribe() { if (surfaceField == null) return; surfaceField.SurfaceTemperatureTickCommitted -= OnSurfaceTickCommitted; surfaceField.SurfaceTemperatureFieldReinitialized -= OnSurfaceReinitialized; surfaceField.SurfaceTemperatureFieldClearing -= OnSurfaceClearing; }
    private void OnSurfaceReinitialized() => InitializeForCurrentDomain();
    private void OnSurfaceClearing() => ClearField();
    private void OnSurfaceTickCommitted(float dt) { if (initialized) ExchangeVerticalHeat(dt); }

    public float GetLayerTemperatureKelvin(int cellIndex, int layerIndex) => TryGetLayerTemperatureKelvin(cellIndex, layerIndex, out float value) ? value : float.NaN;
    public bool TryGetLayerTemperatureKelvin(int cellIndex, int layerIndex, out float temperatureKelvin)
    {
        temperatureKelvin = float.NaN;
        if (!initialized || !sourceGrid.IsNodeActive(cellIndex, layerIndex)) return false;
        temperatureKelvin = layerIndex == 0 ? surfaceField.GetCellTemperatureKelvin(cellIndex) : subsurfaceTemperatureKelvinByNode[sourceGrid.GetNodeIndex(cellIndex, layerIndex)];
        return Finite(temperatureKelvin);
    }
    public float GetBottomLayerTemperatureKelvin(int cellIndex) { if (!initialized || cellIndex < 0 || cellIndex >= sourceGrid.CellCount) return float.NaN; return GetLayerTemperatureKelvin(cellIndex, sourceGrid.GetBottomLayerIndex(cellIndex)); }
    public float GetEffectiveOceanTemperatureKelvin(int cellIndex, int preferredLayer) { if (TryGetLayerTemperatureKelvin(cellIndex, preferredLayer, out float value)) return value; return GetLayerTemperatureKelvin(cellIndex, 0); }
    public float GetLayerHeatCapacity(int cellIndex, int layerIndex) { if (!initialized || !sourceGrid.IsNodeActive(cellIndex, layerIndex)) return float.NaN; return layerIndex == 0 ? surfaceField.GetCellHeatCapacity(cellIndex) : heatCapacityByNode[sourceGrid.GetNodeIndex(cellIndex, layerIndex)]; }

    private void RebuildCapacitiesAndConductance()
    {
        Array.Clear(heatCapacityByNode, 0, heatCapacityByNode.Length); Array.Clear(inverseHeatCapacityByNode, 0, inverseHeatCapacityByNode.Length); Array.Clear(conductanceSumBaseByNode, 0, conductanceSumBaseByNode.Length); Array.Clear(surfaceConductanceSumBaseByCell, 0, surfaceConductanceSumBaseByCell.Length);
        activeSubsurfaceNodeCount = 0; totalSubsurfaceThermalCapacity = 0d;
        float density = Mathf.Max(1e-8f, subsurfaceHeatCapacityPerVolume);
        for (int cell = 0; cell < sourceGrid.CellCount; cell++) for (int layer = 1; layer < sourceGrid.ActiveLayerCountByCell[cell]; layer++) { int node = sourceGrid.GetNodeIndex(cell, layer); float c = sourceGrid.LayerVolume[node] * density; heatCapacityByNode[node] = c; inverseHeatCapacityByNode[node] = 1f / c; totalSubsurfaceThermalCapacity += c; activeSubsurfaceNodeCount++; }
        for (int link = 0; link < sourceGrid.VerticalLinkCount; link++) { int upper = sourceGrid.VerticalUpperNode[link], lower = sourceGrid.VerticalLowerNode[link]; float b = sourceGrid.VerticalInterfaceArea[link] / sourceGrid.VerticalCenterDistance[link]; int cell = upper / sourceGrid.MaximumLayerCount; if (upper % sourceGrid.MaximumLayerCount == 0) surfaceConductanceSumBaseByCell[cell] += b; else conductanceSumBaseByNode[upper] += b; conductanceSumBaseByNode[lower] += b; }
        cachedCapacityPerVolume = subsurfaceHeatCapacityPerVolume;
        sourceGridSummary = $"cells={sourceGrid.CellCount}, nodes={sourceGrid.NodeCapacity}, active={sourceGrid.ActiveNodeCount}, verticalLinks={sourceGrid.VerticalLinkCount}";
        approximateRuntimeMemoryBytes = (long)sourceGrid.NodeCapacity * sizeof(float) * 4L + (long)sourceGrid.CellCount * sizeof(float) * 2L;
    }

    private void ExchangeVerticalHeat(float dt)
    {
        long start = Stopwatch.GetTimestamp(); lastVerticalExchangeSimulationDelta = dt; lastStabilityClampingOccurred = false; lastAbsoluteEnergyTransferred = 0d;
        if (cachedCapacityPerVolume != subsurfaceHeatCapacityPerVolume) RebuildCapacitiesAndConductance();
        double before = TotalEnergy();
        if (verticalThermalDiffusivity <= 0f || dt <= 0f) { lastVerticalSubstepCount = 0; latestVerticalConservationRelativeError = 0d; lastCompletedOceanTemperatureTick += dt; lastVerticalExchangeDurationMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency; UpdateDiagnostics(); return; }
        float stableDt = float.PositiveInfinity;
        for (int cell = 0; cell < sourceGrid.CellCount; cell++) if (surfaceConductanceSumBaseByCell[cell] > 0f) stableDt = Mathf.Min(stableDt, StabilitySafetyFactor * surfaceField.GetCellHeatCapacity(cell) / (verticalThermalDiffusivity * surfaceConductanceSumBaseByCell[cell]));
        for (int cell = 0; cell < sourceGrid.CellCount; cell++) for (int layer = 1; layer < sourceGrid.ActiveLayerCountByCell[cell]; layer++) { int node = sourceGrid.GetNodeIndex(cell, layer); if (conductanceSumBaseByNode[node] > 0f) stableDt = Mathf.Min(stableDt, StabilitySafetyFactor * heatCapacityByNode[node] / (verticalThermalDiffusivity * conductanceSumBaseByNode[node])); }
        int needed = float.IsInfinity(stableDt) ? 1 : Mathf.Max(1, Mathf.CeilToInt(dt / Mathf.Max(1e-8f, stableDt)));
        int substeps = Mathf.Min(needed, Mathf.Max(1, maximumVerticalSubsteps)); float diffusivity = verticalThermalDiffusivity;
        if (needed > substeps) { diffusivity *= (float)substeps / needed; lastStabilityClampingOccurred = true; if (!warnedStabilityClamp) { UnityEngine.Debug.LogWarning("[GeodesicOceanTemperature] Vertical diffusivity stability-clamped for a capped tick.", this); warnedStabilityClamp = true; } }
        lastVerticalSubstepCount = substeps; float stepDt = dt / substeps;
        for (int step = 0; step < substeps; step++)
        {
            Array.Clear(energyDeltaByNode, 0, energyDeltaByNode.Length); Array.Clear(surfaceEnergyDeltaByCell, 0, surfaceEnergyDeltaByCell.Length);
            for (int link = 0; link < sourceGrid.VerticalLinkCount; link++)
            {
                int upper = sourceGrid.VerticalUpperNode[link], lower = sourceGrid.VerticalLowerNode[link], cell = upper / sourceGrid.MaximumLayerCount;
                float upperT = upper % sourceGrid.MaximumLayerCount == 0 ? surfaceField.GetCellTemperatureKelvin(cell) : subsurfaceTemperatureKelvinByNode[upper];
                float energy = diffusivity * (sourceGrid.VerticalInterfaceArea[link] / sourceGrid.VerticalCenterDistance[link]) * (subsurfaceTemperatureKelvinByNode[lower] - upperT) * stepDt;
                if (upper % sourceGrid.MaximumLayerCount == 0) surfaceEnergyDeltaByCell[cell] += energy; else energyDeltaByNode[upper] += energy;
                energyDeltaByNode[lower] -= energy; lastAbsoluteEnergyTransferred += Math.Abs(energy);
            }
            for (int cell = 0; cell < sourceGrid.CellCount; cell++) if (surfaceEnergyDeltaByCell[cell] != 0f) surfaceField.TryApplyExternalEnergyDelta(cell, surfaceEnergyDeltaByCell[cell]);
            for (int cell = 0; cell < sourceGrid.CellCount; cell++) for (int layer = 1; layer < sourceGrid.ActiveLayerCountByCell[cell]; layer++) { int node = sourceGrid.GetNodeIndex(cell, layer); subsurfaceTemperatureKelvinByNode[node] += energyDeltaByNode[node] * inverseHeatCapacityByNode[node]; }
        }
        double after = TotalEnergy(); latestVerticalConservationRelativeError = Math.Abs(after - before) / Math.Max(1e-12, Math.Abs(before)); lastCompletedOceanTemperatureTick += dt; lastVerticalExchangeDurationMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency; UpdateDiagnostics();
        if (enableProfilingDiagnostics) UnityEngine.Debug.Log($"[GeodesicOceanTemperatureProfile] dt={dt:F4}, substeps={substeps}, clamp={lastStabilityClampingOccurred}, links={sourceGrid.VerticalLinkCount}, ms={lastVerticalExchangeDurationMilliseconds:F3}, conservation={latestVerticalConservationRelativeError:E3}", this);
    }

    private double TotalEnergy() { double total = 0d; for (int cell = 0; cell < sourceGrid.CellCount; cell++) { if (sourceGrid.ActiveLayerCountByCell[cell] == 0) continue; total += surfaceField.GetCellTemperatureKelvin(cell) * surfaceField.GetCellHeatCapacity(cell); for (int layer = 1; layer < sourceGrid.ActiveLayerCountByCell[cell]; layer++) { int node = sourceGrid.GetNodeIndex(cell, layer); total += subsurfaceTemperatureKelvinByNode[node] * heatCapacityByNode[node]; } } return total; }
    private void UpdateDiagnostics()
    {
        for (int layer = 0; layer < 5; layer++) { layerActiveCellCount[layer] = 0; layerMinimumTemperatureKelvin[layer] = float.PositiveInfinity; layerMaximumTemperatureKelvin[layer] = float.NegativeInfinity; layerMeanTemperatureKelvin[layer] = 0f; }
        Array.Clear(diagnosticWeightedTemperature, 0, 5); Array.Clear(diagnosticWeight, 0, 5); maximumSurfaceToBottomTemperatureDifference = 0f;
        for (int cell = 0; cell < sourceGrid.CellCount; cell++) { int count = sourceGrid.ActiveLayerCountByCell[cell]; if (count == 0) continue; float surface = surfaceField.GetCellTemperatureKelvin(cell); for (int layer = 0; layer < count; layer++) { int node = sourceGrid.GetNodeIndex(cell, layer); float t = layer == 0 ? surface : subsurfaceTemperatureKelvinByNode[node]; float c = layer == 0 ? surfaceField.GetCellHeatCapacity(cell) : heatCapacityByNode[node]; layerActiveCellCount[layer]++; layerMinimumTemperatureKelvin[layer] = Mathf.Min(layerMinimumTemperatureKelvin[layer], t); layerMaximumTemperatureKelvin[layer] = Mathf.Max(layerMaximumTemperatureKelvin[layer], t); diagnosticWeightedTemperature[layer] += t * c; diagnosticWeight[layer] += c; } maximumSurfaceToBottomTemperatureDifference = Mathf.Max(maximumSurfaceToBottomTemperatureDifference, Mathf.Abs(surface - GetBottomLayerTemperatureKelvin(cell))); }
        for (int layer = 0; layer < 5; layer++) { if (diagnosticWeight[layer] > 0d) layerMeanTemperatureKelvin[layer] = (float)(diagnosticWeightedTemperature[layer] / diagnosticWeight[layer]); else layerMinimumTemperatureKelvin[layer] = layerMaximumTemperatureKelvin[layer] = float.NaN; }
    }

    [ContextMenu("Validate Geodesic Ocean Temperature Field")]
    private void ValidateField()
    {
        int errors = 0; string first = null; Action<string> fail = message => { errors++; if (first == null) first = message; };
        if (!initialized || sourceGrid == null || surfaceField == null || !surfaceField.IsInitialized) fail("dependencies are not initialized");
        else
        {
            if (!ReferenceEquals(sourceGrid.SourceTopology, generator.GeodesicTopology) || subsurfaceTemperatureKelvinByNode.Length != sourceGrid.NodeCapacity || heatCapacityByNode.Length != sourceGrid.NodeCapacity) fail("stale topology or array length");
            for (int cell = 0; cell < sourceGrid.CellCount; cell++) for (int layer = 0; layer < sourceGrid.MaximumLayerCount; layer++) { bool active = sourceGrid.IsNodeActive(cell, layer); if (!active && TryGetLayerTemperatureKelvin(cell, layer, out _)) fail("inactive/land node exposes temperature"); if (active && layer == 0 && GetLayerTemperatureKelvin(cell, 0) != surfaceField.GetCellTemperatureKelvin(cell)) fail("layer 0 is not exact read-through"); if (active && layer > 0) { int node = sourceGrid.GetNodeIndex(cell, layer); if (!Finite(subsurfaceTemperatureKelvinByNode[node]) || subsurfaceTemperatureKelvinByNode[node] < 0f || !Finite(heatCapacityByNode[node]) || heatCapacityByNode[node] <= 0f) fail("invalid active subsurface state"); } }
            for (int link = 0; link < sourceGrid.VerticalLinkCount; link++) { int u = sourceGrid.VerticalUpperNode[link], l = sourceGrid.VerticalLowerNode[link]; float b = sourceGrid.VerticalInterfaceArea[link] / sourceGrid.VerticalCenterDistance[link]; if (l != u + 1 || !sourceGrid.IsNodeActive(u / sourceGrid.MaximumLayerCount, u % sourceGrid.MaximumLayerCount) || !sourceGrid.IsNodeActive(l / sourceGrid.MaximumLayerCount, l % sourceGrid.MaximumLayerCount) || !Finite(b) || b <= 0f) fail("invalid vertical link/conductance"); }
            float[] t = { 300f, 300f }, c = { 2f, 3f }, d = new float[2]; TestExchange(t, c, d, 1f); if (t[0] != 300f || t[1] != 300f) fail("uniform-column test changed"); t[0] = 310f; t[1] = 290f; double before = t[0] * c[0] + t[1] * c[1]; TestExchange(t, c, d, 0.01f); double after = t[0] * c[0] + t[1] * c[1]; if (t[1] <= 290f) fail("warm-surface test did not transfer downward"); if (Math.Abs(after - before) > 1e-5 * Math.Abs(before)) fail("temporary column did not conserve energy");
            if (!Finite(latestVerticalConservationRelativeError) || latestVerticalConservationRelativeError > ConservationTolerance) fail("runtime conservation tolerance exceeded");
        }
        if (errors == 0) UnityEngine.Debug.Log($"[GeodesicOceanTemperatureValidation] valid; {sourceGridSummary}; conservation={latestVerticalConservationRelativeError:E3}", this); else UnityEngine.Debug.LogError($"[GeodesicOceanTemperatureValidation] invalid; errors={errors}; first={first}", this);
    }
    private static void TestExchange(float[] t, float[] c, float[] d, float dt) { d[0] = d[1] = 0f; float e = (t[1] - t[0]) * dt; d[0] += e; d[1] -= e; t[0] += d[0] / c[0]; t[1] += d[1] / c[1]; }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
