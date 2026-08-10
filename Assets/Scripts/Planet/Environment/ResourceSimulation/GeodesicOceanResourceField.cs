using System;
using Unity.Profiling;
using UnityEngine;

public enum GeodesicOceanResource { CO2 = 0, O2 = 1, CH4 = 2, H2 = 3, H2S = 4, Fe2 = 5, OrganicC = 6 }
public enum GeodesicOceanResourceInitializationFailure { None, PlanetGeneratorMissing, NotGeodesicMode, DomainMissing, DomainNotInitialized, DomainGridNull, ZeroActiveNodes, InvalidNodeCapacity, AllocationOrInitializationFailure }

[Serializable]
public struct GeodesicOceanResourceDiagnostics
{
    public GeodesicOceanResource resource;
    public float minimumActiveConcentration;
    public double volumeWeightedMeanConcentration;
    public float maximumActiveConcentration;
    public double globalInventory;
}

/// <summary>
/// Scene-owned authoritative dissolved-resource state for active geodesic ocean-layer nodes.
/// Values are concentrations per layer volume; inventory is concentration * GeodesicOceanLayerGrid.LayerVolume[node].
/// Storage is one channel-major float buffer indexed as resource * nodeCapacity + fixed node index from GeodesicOceanLayerGrid.
/// At subdivision 5 with 10,242 cells, five layers and seven channels this buffer is about 1.37 MiB plus active-node traversal and volume caches.
/// </summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanResourceField : MonoBehaviour
{
    private const int ResourceCount = 7;
    private static readonly ProfilerMarker InitializeMarker = new ProfilerMarker("GeodesicOceanResource.Initialize");
    private static readonly ProfilerMarker TransportMarker = new ProfilerMarker("GeodesicOceanResource.Transport");
    private static readonly ProfilerMarker HorizontalMarker = new ProfilerMarker("GeodesicOceanResource.HorizontalMixing");
    private static readonly ProfilerMarker VerticalMarker = new ProfilerMarker("GeodesicOceanResource.VerticalMixing");
    private static readonly ProfilerMarker VentMarker = new ProfilerMarker("GeodesicOceanResource.VentSources");

    [Header("Startup Concentrations (Geodesic Dissolved Ocean)")]
    [SerializeField, Min(0f)] private float initialCO2Concentration = 1f;
    [SerializeField, Min(0f)] private float initialO2Concentration = 0.21f;
    [SerializeField, Min(0f)] private float initialCH4Concentration = 0f;
    [SerializeField, Min(0f)] private float initialFe2Concentration = 0f;

    [Header("Transport (inventory-conservative)")]
    [SerializeField, Min(0.01f), Tooltip("Fixed authoritative simulation seconds per resource tick.")] private float transportIntervalSeconds = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("Fractional horizontal mixing rate per simulation second. Each link is degree-normalized for explicit stability.")] private float horizontalMixingRate = 0.02f;
    [SerializeField, Range(0f, 1f), Tooltip("Fractional adjacent-layer mixing rate per simulation second. Each link is degree-normalized for explicit stability.")] private float defaultVerticalMixingRate = 0.005f;
    [SerializeField] private float[] horizontalResourceMultipliers = { 1f, 1f, 1f, 1f, 1f, 1f, 1f };
    [SerializeField, Tooltip("CO2, O2, CH4, H2, H2S, Fe2, OrganicC. O2 defaults to 0.1 so deep oxygenation lags.")] private float[] verticalResourceMultipliers = { 1f, 0.1f, 1f, 1f, 1f, 1f, 1f };
    [SerializeField, Range(1, 256)] private int maximumTransportTicksPerFrame = 64;

    [Header("Geodesic Vent Sources")]
    [SerializeField, Range(0f, 0.25f), Tooltip("Deterministic fraction of multi-layer ocean columns containing logical resource vents.")] private float ventColumnFraction = 0.02f;
    [SerializeField, Min(0f), Tooltip("Inventory injected per logical vent per fixed resource tick (one simulated second by default).") ] private float ventH2PerTick = 0.006f;
    [SerializeField, Min(0f)] private float ventH2SPerTick = 0.01f;
    [SerializeField, Min(0f)] private float ventCO2PerTick = 0f;
    [SerializeField, Min(0f), Tooltip("Fe2 inventory injected per logical vent per fixed resource tick.")] private float ventFe2PerTick = 0.002f;

    [Header("Runtime Diagnostics (Read Only)")]
    [SerializeField] private bool initialized;
    [SerializeField] private int cellCount;
    [SerializeField] private int nodeCapacity;
    [SerializeField] private int activeNodeCount;
    [SerializeField] private double activeOceanVolume;
    [SerializeField] private long approximateRuntimeMemoryBytes;
    [SerializeField] private int initializationCount;
    [SerializeField] private int clearCount;
    [SerializeField] private int invalidQueryCount;
    [SerializeField] private int rejectedNonfiniteWriteCount;
    [SerializeField] private int rejectedNegativeWriteCount;
    [SerializeField] private GeodesicOceanResourceDiagnostics[] resourceDiagnostics = CreateDiagnosticsArray();
    [SerializeField] private GeodesicOceanResourceInitializationFailure lastInitializationFailure;
    [SerializeField] private string lastInitializationFailureMessage = string.Empty;
    [SerializeField] private string lastStartupConcentrationDiagnostic = string.Empty;
    [SerializeField] private string lastSentinelVerificationDiagnostic = string.Empty;
    [SerializeField] private int horizontalLinkCount;
    [SerializeField] private int verticalLinkCount;
    [SerializeField] private int ventCount;
    [SerializeField] private double transportIntegrationCursorTime;
    [SerializeField] private double unconsumedTransportRemainderSeconds;
    [SerializeField] private long completedTransportTicks;
    [SerializeField] private long transportCacheMemoryBytes;
    [SerializeField] private long stagingBufferMemoryBytes;
    [SerializeField] private float[] cachedMeanO2ByLayer = new float[GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount];

    private GeodesicOceanLayerDomain domain;
    private PlanetGenerator planetGenerator;
    private GeodesicOceanLayerGrid sourceGrid;
    private float[] concentrationsByResourceThenNode;
    private int[] activeNodeIndices;
    private float[] activeNodeVolumes;
    private float[] configuredInitialConcentrations = new float[ResourceCount];
    private ReplicatorManager simulationClock;
    private double lastObservedSimulationTime;
    private double[] stagedInventoryDelta;
    private float[] horizontalConductanceBase;
    private float[] verticalConductanceBase;
    private int[] ventBottomNodes;
    private float[] ventStrengths;
    private bool warnedTransportBacklog;

    public bool IsInitialized => initialized;
    public int CellCount => cellCount;
    public int NodeCapacity => nodeCapacity;
    public int ActiveNodeCount => activeNodeCount;
    public double ActiveOceanVolume => activeOceanVolume;
    public long ApproximateRuntimeMemoryBytes => approximateRuntimeMemoryBytes;
    public GeodesicOceanLayerGrid SourceGrid => sourceGrid;
    public GeodesicOceanResourceInitializationFailure LastInitializationFailure => lastInitializationFailure;
    public string LastInitializationFailureMessage => lastInitializationFailureMessage;
    public string LastStartupConcentrationDiagnostic => lastStartupConcentrationDiagnostic;
    public float TransportIntervalSeconds => Mathf.Max(0.01f, transportIntervalSeconds);
    public double UnconsumedTransportRemainderSeconds => unconsumedTransportRemainderSeconds;
    public long CompletedTransportTicks => completedTransportTicks;
    public long TransportCacheMemoryBytes => transportCacheMemoryBytes;
    public long StagingBufferMemoryBytes => stagingBufferMemoryBytes;

    private void Awake() { domain = GetComponent<GeodesicOceanLayerDomain>(); planetGenerator = GetComponent<PlanetGenerator>(); simulationClock = FindFirstObjectByType<ReplicatorManager>(); }
    private void OnDestroy() => ClearField();

    private void Update()
    {
        if (!initialized || simulationClock == null) return;
        if (planetGenerator == null || planetGenerator.CurrentGridType != PlanetGridType.GeodesicIcosphere) return;
        double target = Math.Max(0d, simulationClock.SimulationTimeSeconds);
        if (target < lastObservedSimulationTime) { transportIntegrationCursorTime = target; unconsumedTransportRemainderSeconds = 0d; }
        lastObservedSimulationTime = target;
        double interval = TransportIntervalSeconds;
        int ticks = 0, guard = Mathf.Max(1, maximumTransportTicksPerFrame);
        while (transportIntegrationCursorTime + interval <= target + 1e-9d && ticks < guard)
        {
            TickResources((float)interval);
            transportIntegrationCursorTime += interval; completedTransportTicks++; ticks++;
        }
        if (transportIntegrationCursorTime + interval <= target + 1e-9d && !warnedTransportBacklog)
        { warnedTransportBacklog = true; Debug.LogWarning("[GeodesicOceanResourceTransport] Catch-up guard reached; backlog retained.", this); }
        unconsumedTransportRemainderSeconds = Math.Max(0d, target - transportIntegrationCursorTime);
    }

    public void SetStartupConcentrations(float co2, float o2, float ch4, float fe2)
    {
        initialCO2Concentration = Mathf.Max(0f, co2);
        initialO2Concentration = Mathf.Max(0f, o2);
        initialCH4Concentration = Mathf.Max(0f, ch4);
        initialFe2Concentration = Mathf.Max(0f, fe2);
        lastStartupConcentrationDiagnostic = $"CO2={initialCO2Concentration:G6}, O2={initialO2Concentration:G6}, CH4={initialCH4Concentration:G6}, Fe2={initialFe2Concentration:G6}";
    }

    public void SetStartupVentRates(float h2, float h2s, float co2, float fe2)
    { ventH2PerTick = Mathf.Max(0f, h2); ventH2SPerTick = Mathf.Max(0f, h2s); ventCO2PerTick = Mathf.Max(0f, co2); ventFe2PerTick = Mathf.Max(0f, fe2); }

    public bool InitializeForCurrentDomain()
    {
        using (InitializeMarker.Auto())
        {
            ClearField(false);
            lastInitializationFailure = GeodesicOceanResourceInitializationFailure.None;
            lastInitializationFailureMessage = string.Empty;
            lastSentinelVerificationDiagnostic = string.Empty;
            domain = GetComponent<GeodesicOceanLayerDomain>();
            planetGenerator = GetComponent<PlanetGenerator>();
            if (planetGenerator == null) return FailInitialization(GeodesicOceanResourceInitializationFailure.PlanetGeneratorMissing, "PlanetGenerator missing");
            if (planetGenerator.CurrentGridType != PlanetGridType.GeodesicIcosphere) return FailInitialization(GeodesicOceanResourceInitializationFailure.NotGeodesicMode, $"current mode is {planetGenerator.CurrentGridType}, not GeodesicIcosphere");
            if (domain == null) return FailInitialization(GeodesicOceanResourceInitializationFailure.DomainMissing, "GeodesicOceanLayerDomain missing");
            if (!domain.Initialized) return FailInitialization(GeodesicOceanResourceInitializationFailure.DomainNotInitialized, "GeodesicOceanLayerDomain not initialized");
            GeodesicOceanLayerGrid grid = domain.Grid;
            if (grid == null) return FailInitialization(GeodesicOceanResourceInitializationFailure.DomainGridNull, "GeodesicOceanLayerDomain Grid is null");
            if (grid.ActiveNodeCount <= 0) return FailInitialization(GeodesicOceanResourceInitializationFailure.ZeroActiveNodes, "grid has zero active nodes");
            if (grid.NodeCapacity <= 0 || grid.CellCount <= 0 || grid.MaximumLayerCount <= 0 || grid.NodeCapacity != grid.CellCount * grid.MaximumLayerCount) return FailInitialization(GeodesicOceanResourceInitializationFailure.InvalidNodeCapacity, $"invalid node capacity: cells={grid.CellCount}, layers={grid.MaximumLayerCount}, nodeCapacity={grid.NodeCapacity}");

            try
            {
            sourceGrid = grid; cellCount = grid.CellCount; nodeCapacity = grid.NodeCapacity; activeNodeCount = grid.ActiveNodeCount;
            concentrationsByResourceThenNode = new float[ResourceCount * nodeCapacity];
            activeNodeIndices = new int[activeNodeCount]; activeNodeVolumes = new float[activeNodeCount];
            configuredInitialConcentrations[(int)GeodesicOceanResource.CO2] = initialCO2Concentration;
            configuredInitialConcentrations[(int)GeodesicOceanResource.O2] = initialO2Concentration;
            configuredInitialConcentrations[(int)GeodesicOceanResource.CH4] = initialCH4Concentration;
            configuredInitialConcentrations[(int)GeodesicOceanResource.H2] = 0f;
            configuredInitialConcentrations[(int)GeodesicOceanResource.H2S] = 0f;
            configuredInitialConcentrations[(int)GeodesicOceanResource.Fe2] = initialFe2Concentration;
            configuredInitialConcentrations[(int)GeodesicOceanResource.OrganicC] = 0f;
            int cursor = 0; double volume = 0d;
            for (int cell = 0; cell < grid.CellCount; cell++) for (int layer = 0; layer < grid.ActiveLayerCountByCell[cell]; layer++)
            {
                int node = grid.GetNodeIndex(cell, layer); activeNodeIndices[cursor] = node; activeNodeVolumes[cursor] = grid.LayerVolume[node]; volume += grid.LayerVolume[node];
                for (int r = 0; r < ResourceCount; r++) concentrationsByResourceThenNode[r * nodeCapacity + node] = configuredInitialConcentrations[r];
                cursor++;
            }
            activeOceanVolume = volume; approximateRuntimeMemoryBytes = (long)concentrationsByResourceThenNode.Length * sizeof(float) + (long)activeNodeIndices.Length * sizeof(int) + (long)activeNodeVolumes.Length * sizeof(float) + ResourceCount * 32L;
            BuildTransportCaches(grid, planetGenerator);
            simulationClock = FindFirstObjectByType<ReplicatorManager>();
            double simulationTime = simulationClock != null ? Math.Max(0d, simulationClock.SimulationTimeSeconds) : 0d;
            transportIntegrationCursorTime = lastObservedSimulationTime = simulationTime;
            unconsumedTransportRemainderSeconds = 0d; completedTransportTicks = 0; warnedTransportBacklog = false;
            initialized = true; initializationCount++; RecomputeDiagnostics(); RefreshO2LayerMeans();
            if (!VerifyStartupSentinel(out string sentinel)) return FailInitialization(GeodesicOceanResourceInitializationFailure.AllocationOrInitializationFailure, sentinel);
            lastSentinelVerificationDiagnostic = sentinel;
            Debug.Log($"[GeodesicOceanResource] initialized dissolved-ocean concentrations (not atmosphere; not legacy normalized totals): cells={cellCount}, nodeCapacity={nodeCapacity}, activeNodes={activeNodeCount}, volume={activeOceanVolume:G6}, memory={approximateRuntimeMemoryBytes} bytes, CO2={initialCO2Concentration:G6}, O2={initialO2Concentration:G6}, CH4={initialCH4Concentration:G6}, H2=0, H2S=0, Fe2={initialFe2Concentration:G6}, OrganicC=0, sentinel={sentinel}", this);
            Debug.Log($"[GeodesicOceanResourceTransport] interval={TransportIntervalSeconds:F3}s, activeNodes={activeNodeCount}, horizontalLinks={horizontalLinkCount}, verticalLinks={verticalLinkCount}, vents={ventCount}, stateBytes={approximateRuntimeMemoryBytes}, cacheBytes={transportCacheMemoryBytes}, stagingBytes={stagingBufferMemoryBytes}", this);
            return true;
            }
            catch (Exception exception)
            {
                return FailInitialization(GeodesicOceanResourceInitializationFailure.AllocationOrInitializationFailure, exception.Message);
            }
        }
    }

    private bool FailInitialization(GeodesicOceanResourceInitializationFailure failure, string detail)
    {
        lastInitializationFailure = failure;
        lastInitializationFailureMessage = detail ?? failure.ToString();
        ClearField(false);
        Debug.LogError($"[GeodesicOceanResource] initialization failed: {failure}; {lastInitializationFailureMessage}", this);
        return false;
    }

    private bool VerifyStartupSentinel(out string diagnostic)
    {
        diagnostic = "no active sentinel";

        if (sourceGrid == null || activeNodeCount <= 0)
        {
            return false;
        }

        int node = activeNodeIndices[0];
        int cell = node / sourceGrid.MaximumLayerCount;
        int layer = node % sourceGrid.MaximumLayerCount;

        bool co2Ok = TryGetConcentration(
            cell, layer, GeodesicOceanResource.CO2, out float co2);

        bool o2Ok = TryGetConcentration(
            cell, layer, GeodesicOceanResource.O2, out float o2);

        bool ch4Ok = TryGetConcentration(
            cell, layer, GeodesicOceanResource.CH4, out float ch4);

        bool h2Ok = TryGetConcentration(
            cell, layer, GeodesicOceanResource.H2, out float h2);

        bool h2sOk = TryGetConcentration(
            cell, layer, GeodesicOceanResource.H2S, out float h2s);

        bool fe2Ok = TryGetConcentration(
            cell, layer, GeodesicOceanResource.Fe2, out float fe2);

        bool organicCOk = TryGetConcentration(
            cell, layer, GeodesicOceanResource.OrganicC, out float organicC);

        bool ok =
            co2Ok &&
            o2Ok &&
            ch4Ok &&
            h2Ok &&
            h2sOk &&
            fe2Ok &&
            organicCOk;

        diagnostic =
            $"cell={cell}, layer={layer}, " +
            $"CO2={co2:G6}, O2={o2:G6}, CH4={ch4:G6}, " +
            $"H2={h2:G6}, H2S={h2s:G6}, Fe2={fe2:G6}, " +
            $"OrganicC={organicC:G6}";

        if (!ok)
        {
            return false;
        }

        const float tolerance = 1e-5f;

        return Mathf.Abs(co2 - initialCO2Concentration) <= tolerance
            && Mathf.Abs(o2 - initialO2Concentration) <= tolerance
            && Mathf.Abs(ch4 - initialCH4Concentration) <= tolerance
            && Mathf.Abs(fe2 - initialFe2Concentration) <= tolerance
            && h2 == 0f
            && h2s == 0f
            && organicC == 0f;
    }

    public void ClearField() => ClearField(true);
    private void ClearField(bool countClear)
    {
        concentrationsByResourceThenNode = null; activeNodeIndices = null; activeNodeVolumes = null; sourceGrid = null; stagedInventoryDelta = null; horizontalConductanceBase = null; verticalConductanceBase = null; ventBottomNodes = null; ventStrengths = null; initialized = false; cellCount = nodeCapacity = activeNodeCount = horizontalLinkCount = verticalLinkCount = ventCount = 0; activeOceanVolume = 0d; approximateRuntimeMemoryBytes = transportCacheMemoryBytes = stagingBufferMemoryBytes = 0; transportIntegrationCursorTime = lastObservedSimulationTime = unconsumedTransportRemainderSeconds = 0d; completedTransportTicks = 0; ResetDiagnostics(); if (countClear) clearCount++;
    }

    private void BuildTransportCaches(GeodesicOceanLayerGrid grid, PlanetGenerator generator)
    {
        horizontalLinkCount = grid.HorizontalLinkCount; verticalLinkCount = grid.VerticalLinkCount;
        stagedInventoryDelta = new double[nodeCapacity];
        horizontalConductanceBase = new float[horizontalLinkCount];
        verticalConductanceBase = new float[verticalLinkCount];
        float[] horizontalGeometry = new float[horizontalLinkCount], horizontalSum = new float[nodeCapacity];
        for (int i = 0; i < horizontalLinkCount; i++)
        {
            int a = grid.HorizontalNodeA[i], b = grid.HorizontalNodeB[i];
            int edge = grid.HorizontalSourceEdgeIndex[i];
            float geometry = grid.SourceTransportGraph.EdgeConductanceBase[edge] * grid.HorizontalOverlapThickness[i];
            horizontalGeometry[i] = geometry; horizontalSum[a] += geometry; horizontalSum[b] += geometry;
        }
        // Geometry-weighted normalization guarantees each node's incident conductance sum is
        // at most its volume. Thus rate * dt <= 1 is a justified explicit stability bound.
        for (int i = 0; i < horizontalLinkCount; i++) { int a = grid.HorizontalNodeA[i], b = grid.HorizontalNodeB[i]; float geometry = horizontalGeometry[i]; horizontalConductanceBase[i] = geometry * Mathf.Min(grid.LayerVolume[a] / horizontalSum[a], grid.LayerVolume[b] / horizontalSum[b]); }
        float[] verticalGeometry = new float[verticalLinkCount], verticalSum = new float[nodeCapacity];
        for (int i = 0; i < verticalLinkCount; i++)
        {
            int a = grid.VerticalUpperNode[i], b = grid.VerticalLowerNode[i];
            float geometry = grid.VerticalInterfaceArea[i] / grid.VerticalCenterDistance[i];
            verticalGeometry[i] = geometry; verticalSum[a] += geometry; verticalSum[b] += geometry;
        }
        for (int i = 0; i < verticalLinkCount; i++) { int a = grid.VerticalUpperNode[i], b = grid.VerticalLowerNode[i]; float geometry = verticalGeometry[i]; verticalConductanceBase[i] = geometry * Mathf.Min(grid.LayerVolume[a] / verticalSum[a], grid.LayerVolume[b] / verticalSum[b]); }
        int[] nodes = new int[grid.OceanCellCount]; float[] strengths = new float[grid.OceanCellCount]; int count = 0;
        uint seed = unchecked((uint)generator.DerivedTerrainSeed) ^ 0x6A09E667u;
        uint threshold = (uint)(Mathf.Clamp01(ventColumnFraction) * uint.MaxValue);
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            int bottom = grid.GetBottomLayerIndex(cell); if (bottom < 1) continue;
            uint hash = HashVent(seed ^ (uint)cell);
            if (threshold == 0u || hash > threshold) continue;
            nodes[count] = grid.GetNodeIndex(cell, bottom); strengths[count] = 0.5f + 0.5f * (hash / (float)threshold); count++;
        }
        ventBottomNodes = new int[count]; ventStrengths = new float[count]; Array.Copy(nodes, ventBottomNodes, count); Array.Copy(strengths, ventStrengths, count); ventCount = count;
        stagingBufferMemoryBytes = (long)stagedInventoryDelta.Length * sizeof(double);
        transportCacheMemoryBytes = (long)(horizontalConductanceBase.Length + verticalConductanceBase.Length + ventStrengths.Length) * sizeof(float) + (long)ventBottomNodes.Length * sizeof(int);
    }

    private static uint HashVent(uint value)
    { value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; return value ^ (value >> 16); }

    private void TickResources(float dt)
    {
        using (TransportMarker.Auto())
        {
            // Deterministic tick order: transport the old state (horizontal then vertical), then inject vents.
            for (int resource = 0; resource < ResourceCount; resource++)
            {
                Array.Clear(stagedInventoryDelta, 0, stagedInventoryDelta.Length);
                AccumulateHorizontal(resource, dt);
                AccumulateVertical(resource, dt);
                ApplyStaged(resource);
            }
            InjectVentSources(dt / TransportIntervalSeconds);
        }
        if ((completedTransportTicks + 1) % Math.Max(1, (long)Math.Round(5f / TransportIntervalSeconds)) == 0) { RecomputeDiagnostics(); RefreshO2LayerMeans(); }
    }

    private void AccumulateHorizontal(int resource, float dt)
    {
        float multiplier = GetMultiplier(horizontalResourceMultipliers, resource, 1f);
        float rate = Mathf.Max(0f, horizontalMixingRate) * multiplier * StableRateScale(resource, dt);
        using (HorizontalMarker.Auto()) for (int i = 0; i < horizontalLinkCount; i++)
        {
            int a = sourceGrid.HorizontalNodeA[i], b = sourceGrid.HorizontalNodeB[i];
            double transfer = horizontalConductanceBase[i] * rate * dt * (concentrationsByResourceThenNode[resource * nodeCapacity + a] - concentrationsByResourceThenNode[resource * nodeCapacity + b]);
            stagedInventoryDelta[a] -= transfer; stagedInventoryDelta[b] += transfer;
        }
    }

    private void AccumulateVertical(int resource, float dt)
    {
        float multiplier = GetMultiplier(verticalResourceMultipliers, resource, resource == (int)GeodesicOceanResource.O2 ? 0.1f : 1f);
        float rate = Mathf.Max(0f, defaultVerticalMixingRate) * multiplier * StableRateScale(resource, dt);
        using (VerticalMarker.Auto()) for (int i = 0; i < verticalLinkCount; i++)
        {
            int a = sourceGrid.VerticalUpperNode[i], b = sourceGrid.VerticalLowerNode[i];
            double transfer = verticalConductanceBase[i] * rate * dt * (concentrationsByResourceThenNode[resource * nodeCapacity + a] - concentrationsByResourceThenNode[resource * nodeCapacity + b]);
            stagedInventoryDelta[a] -= transfer; stagedInventoryDelta[b] += transfer;
        }
    }

    private void ApplyStaged(int resource)
    {
        int offset = resource * nodeCapacity;
        for (int i = 0; i < activeNodeCount; i++)
        {
            int node = activeNodeIndices[i]; double inventory = concentrationsByResourceThenNode[offset + node] * (double)activeNodeVolumes[i] + stagedInventoryDelta[node];
            concentrationsByResourceThenNode[offset + node] = (float)Math.Max(0d, inventory / activeNodeVolumes[i]);
        }
    }

    private void InjectVentSources(float tickScale)
    {
        using (VentMarker.Auto()) for (int i = 0; i < ventCount; i++)
        {
            int node = ventBottomNodes[i]; double scale = ventStrengths[i] * tickScale / sourceGrid.LayerVolume[node];
            concentrationsByResourceThenNode[(int)GeodesicOceanResource.H2 * nodeCapacity + node] += (float)(ventH2PerTick * scale);
            concentrationsByResourceThenNode[(int)GeodesicOceanResource.H2S * nodeCapacity + node] += (float)(ventH2SPerTick * scale);
            concentrationsByResourceThenNode[(int)GeodesicOceanResource.CO2 * nodeCapacity + node] += (float)(ventCO2PerTick * scale);
            concentrationsByResourceThenNode[(int)GeodesicOceanResource.Fe2 * nodeCapacity + node] += (float)(ventFe2PerTick * scale);
        }
    }

    private static float GetMultiplier(float[] values, int resource, float fallback) => values != null && resource < values.Length ? Mathf.Max(0f, values[resource]) : fallback;

    private float StableRateScale(int resource, float dt)
    {
        float horizontal = Mathf.Max(0f, horizontalMixingRate) * GetMultiplier(horizontalResourceMultipliers, resource, 1f);
        float vertical = Mathf.Max(0f, defaultVerticalMixingRate) * GetMultiplier(verticalResourceMultipliers, resource, resource == (int)GeodesicOceanResource.O2 ? 0.1f : 1f);
        float removalFraction = dt * (horizontal + vertical);
        return removalFraction > 1f ? 1f / removalFraction : 1f;
    }

    public bool TryGetCachedMeanO2ByLayer(int layer, out float mean)
    { mean = float.NaN; if (!initialized || layer < 0 || layer >= sourceGrid.MaximumLayerCount || cachedMeanO2ByLayer == null) return false; mean = cachedMeanO2ByLayer[layer]; return !float.IsNaN(mean); }

    public void RefreshO2LayerMeans()
    {
        if (cachedMeanO2ByLayer == null || cachedMeanO2ByLayer.Length != GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount) cachedMeanO2ByLayer = new float[GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount];
        for (int layer = 0; layer < cachedMeanO2ByLayer.Length; layer++) { double inventory = 0d, volume = 0d; for (int cell = 0; cell < cellCount; cell++) if (sourceGrid.IsNodeActive(cell, layer)) { int node = sourceGrid.GetNodeIndex(cell, layer); double v = sourceGrid.LayerVolume[node]; inventory += concentrationsByResourceThenNode[(int)GeodesicOceanResource.O2 * nodeCapacity + node] * v; volume += v; } cachedMeanO2ByLayer[layer] = volume > 0d ? (float)(inventory / volume) : float.NaN; }
    }

    public bool TryGetConcentration(int cellIndex, int layerIndex, GeodesicOceanResource resource, out float concentration)
    {
        concentration = 0f; if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; concentration = concentrationsByResourceThenNode[offset]; return true;
    }
    public bool TrySetConcentration(int cellIndex, int layerIndex, GeodesicOceanResource resource, float concentration)
    {
        if (!ValidateWriteValue(concentration)) return false; if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; concentrationsByResourceThenNode[offset] = concentration; RecomputeDiagnosticsFor(resource); return true;
    }
    public bool TryAddConcentration(int cellIndex, int layerIndex, GeodesicOceanResource resource, float deltaConcentration)
    {
        if (!Finite(deltaConcentration)) { rejectedNonfiniteWriteCount++; return false; } if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; float next = concentrationsByResourceThenNode[offset] + deltaConcentration; if (!Finite(next)) { rejectedNonfiniteWriteCount++; return false; } if (next < 0f) { rejectedNegativeWriteCount++; return false; } concentrationsByResourceThenNode[offset] = next; RecomputeDiagnosticsFor(resource); return true;
    }
    public bool TryAddInventory(int cellIndex, int layerIndex, GeodesicOceanResource resource, double inventoryDelta)
    {
        if (!Finite(inventoryDelta)) { rejectedNonfiniteWriteCount++; return false; } if (inventoryDelta < 0d) { rejectedNegativeWriteCount++; return false; } if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; int node = sourceGrid.GetNodeIndex(cellIndex, layerIndex); double delta = inventoryDelta / sourceGrid.LayerVolume[node]; if (!Finite(delta) || delta > float.MaxValue) { rejectedNonfiniteWriteCount++; return false; } float next = concentrationsByResourceThenNode[offset] + (float)delta; if (!Finite(next)) { rejectedNonfiniteWriteCount++; return false; } if (next < 0f) { rejectedNegativeWriteCount++; return false; } concentrationsByResourceThenNode[offset] = next; RecomputeDiagnosticsFor(resource); return true;
    }
    public bool TryWithdrawInventoryBounded(int cellIndex, int layerIndex, GeodesicOceanResource resource, double requestedInventory, out double withdrawnInventory)
    {
        withdrawnInventory = 0d; if (!Finite(requestedInventory) || requestedInventory < 0d) { if (!Finite(requestedInventory)) rejectedNonfiniteWriteCount++; else rejectedNegativeWriteCount++; return false; } if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; int node = sourceGrid.GetNodeIndex(cellIndex, layerIndex); double available = concentrationsByResourceThenNode[offset] * (double)sourceGrid.LayerVolume[node]; withdrawnInventory = Math.Min(requestedInventory, available); double next = (available - withdrawnInventory) / sourceGrid.LayerVolume[node]; if (!Finite(next) || next > float.MaxValue) { rejectedNonfiniteWriteCount++; return false; } concentrationsByResourceThenNode[offset] = (float)next; RecomputeDiagnosticsFor(resource); return true;
    }
    public float GetNodeInventory(int cellIndex, int layerIndex, GeodesicOceanResource resource) => TryGetConcentration(cellIndex, layerIndex, resource, out float c) ? c * sourceGrid.LayerVolume[sourceGrid.GetNodeIndex(cellIndex, layerIndex)] : 0f;
    public double GetGlobalInventory(GeodesicOceanResource resource) => IsResourceValid(resource) && resourceDiagnostics != null ? resourceDiagnostics[(int)resource].globalInventory : 0d;
    public double GetVolumeWeightedMeanConcentration(GeodesicOceanResource resource) => IsResourceValid(resource) && resourceDiagnostics != null ? resourceDiagnostics[(int)resource].volumeWeightedMeanConcentration : 0d;

    private bool TryResolveNode(int cellIndex, int layerIndex, GeodesicOceanResource resource, out int offset)
    {
        offset = 0; if (!initialized || sourceGrid == null || concentrationsByResourceThenNode == null || !IsResourceValid(resource) || !sourceGrid.IsNodeActive(cellIndex, layerIndex)) { invalidQueryCount++; return false; } offset = (int)resource * nodeCapacity + sourceGrid.GetNodeIndex(cellIndex, layerIndex); return true;
    }
    private bool ValidateWriteValue(float value) { if (!Finite(value)) { rejectedNonfiniteWriteCount++; return false; } if (value < 0f) { rejectedNegativeWriteCount++; return false; } return true; }
    private static bool IsResourceValid(GeodesicOceanResource r) => (int)r >= 0 && (int)r < ResourceCount;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private void RecomputeDiagnostics() { for (int r = 0; r < ResourceCount; r++) RecomputeDiagnosticsFor((GeodesicOceanResource)r); }
    private void RecomputeDiagnosticsFor(GeodesicOceanResource resource)
    {
        if (resourceDiagnostics == null || resourceDiagnostics.Length != ResourceCount) resourceDiagnostics = CreateDiagnosticsArray(); int r = (int)resource; double inventory = 0d; float min = float.PositiveInfinity, max = 0f;
        for (int i = 0; i < activeNodeCount; i++) { float c = concentrationsByResourceThenNode[r * nodeCapacity + activeNodeIndices[i]]; if (c < min) min = c; if (c > max) max = c; inventory += c * (double)activeNodeVolumes[i]; }
        resourceDiagnostics[r].resource = resource; resourceDiagnostics[r].minimumActiveConcentration = activeNodeCount > 0 ? min : 0f; resourceDiagnostics[r].maximumActiveConcentration = activeNodeCount > 0 ? max : 0f; resourceDiagnostics[r].globalInventory = inventory; resourceDiagnostics[r].volumeWeightedMeanConcentration = activeOceanVolume > 0d ? inventory / activeOceanVolume : 0d;
    }
    private void ResetDiagnostics() { if (resourceDiagnostics == null || resourceDiagnostics.Length != ResourceCount) resourceDiagnostics = CreateDiagnosticsArray(); for (int i = 0; i < ResourceCount; i++) resourceDiagnostics[i] = new GeodesicOceanResourceDiagnostics { resource = (GeodesicOceanResource)i }; }
    private static GeodesicOceanResourceDiagnostics[] CreateDiagnosticsArray() { var a = new GeodesicOceanResourceDiagnostics[ResourceCount]; for (int i = 0; i < ResourceCount; i++) a[i].resource = (GeodesicOceanResource)i; return a; }

    [ContextMenu("Validate Geodesic Ocean Resource Field")]
    private void ValidateContextMenu()
    {
        bool valid = ValidateField(out string report); if (valid) Debug.Log($"[GeodesicOceanResourceValidation] {report}", this); else Debug.LogError($"[GeodesicOceanResourceValidation] {report}", this);
    }

    public bool ValidateField(out string report)
    {
        if (!initialized || sourceGrid == null || concentrationsByResourceThenNode == null) { report = "invalid: field is not initialized"; return false; }
        int errors = 0; string first = null; void Fail(string m) { errors++; if (first == null) first = m; }
        if (domain == null || !ReferenceEquals(domain.Grid, sourceGrid)) Fail("source grid identity mismatch");
        if (concentrationsByResourceThenNode.Length != ResourceCount * nodeCapacity || nodeCapacity != sourceGrid.NodeCapacity) Fail("storage dimensions mismatch");
        int active = 0; double volume = 0d; for (int cell = 0; cell < sourceGrid.CellCount; cell++)
        {
            if (!sourceGrid.SourceOceanMask[cell] && sourceGrid.ActiveLayerCountByCell[cell] != 0) Fail("land cell has active resource nodes");
            for (int layer = 0; layer < sourceGrid.MaximumLayerCount; layer++)
            {
                int node = sourceGrid.GetNodeIndex(cell, layer); bool isActive = sourceGrid.IsNodeActive(cell, layer); if (isActive) { active++; volume += sourceGrid.LayerVolume[node]; }
                for (int r = 0; r < ResourceCount; r++) { float c = concentrationsByResourceThenNode[r * nodeCapacity + node]; if (isActive) { if (!Finite(c) || c < 0f) Fail("active concentration is non-finite or negative"); } else if (c != 0f) Fail("inactive concentration slot is non-zero"); }
            }
        }
        if (active != sourceGrid.ActiveNodeCount || active != activeNodeCount) Fail("active-node count mismatch");
        for (int r = 0; r < ResourceCount; r++) { double sum = 0d; for (int i = 0; i < activeNodeCount; i++) sum += concentrationsByResourceThenNode[r * nodeCapacity + activeNodeIndices[i]] * (double)activeNodeVolumes[i]; if (Math.Abs(sum - resourceDiagnostics[r].globalInventory) > Math.Max(1e-6, Math.Abs(sum) * 1e-6)) Fail("global inventory mismatch"); double mean = volume > 0d ? sum / volume : 0d; if (Math.Abs(mean - resourceDiagnostics[r].volumeWeightedMeanConcentration) > 1e-6) Fail("volume-weighted mean mismatch"); }
        int invalidBefore = invalidQueryCount; double o2Before = resourceDiagnostics[(int)GeodesicOceanResource.O2].globalInventory; TryGetConcentration(-1, -1, GeodesicOceanResource.O2, out _); if (invalidQueryCount != invalidBefore + 1 || Math.Abs(o2Before - resourceDiagnostics[(int)GeodesicOceanResource.O2].globalInventory) > 0d) Fail("invalid query mutated state or counter did not advance");
        if (!RunSentinels(out int sentinelCell, out int sentinelLayer)) Fail("sentinel coverage unavailable");
        if (sentinelCell >= 0)
        {
            double before = GetNodeInventory(sentinelCell, sentinelLayer, GeodesicOceanResource.O2);
            if (!TryWithdrawInventoryBounded(sentinelCell, sentinelLayer, GeodesicOceanResource.O2, before * 2d + 1d, out double withdrawn) || withdrawn < -1e-9 || GetNodeInventory(sentinelCell, sentinelLayer, GeodesicOceanResource.O2) < -1e-9f) Fail("bounded withdrawal failed");
            if (!TryAddInventory(sentinelCell, sentinelLayer, GeodesicOceanResource.O2, withdrawn)) Fail("bounded withdrawal restore failed");
        }
        float[] saved = concentrationsByResourceThenNode; int[] savedActive = activeNodeIndices; float[] savedVolumes = activeNodeVolumes; GeodesicOceanLayerGrid savedGrid = sourceGrid; int savedNodeCapacity = nodeCapacity;
        ClearField(false); if (concentrationsByResourceThenNode != null || activeNodeIndices != null || activeNodeVolumes != null || initialized) Fail("cleanup retained arrays");
        InitializeForCurrentDomain(); if (!initialized || concentrationsByResourceThenNode == null || ReferenceEquals(concentrationsByResourceThenNode, saved) || nodeCapacity != savedNodeCapacity || !ReferenceEquals(sourceGrid, savedGrid)) Fail("reinitialization duplicated or changed source state");
        if (saved != null && concentrationsByResourceThenNode != null && saved.Length == concentrationsByResourceThenNode.Length) Array.Copy(saved, concentrationsByResourceThenNode, saved.Length);
        if (savedActive != null && activeNodeIndices != null && savedActive.Length == activeNodeIndices.Length) Array.Copy(savedActive, activeNodeIndices, savedActive.Length);
        if (savedVolumes != null && activeNodeVolumes != null && savedVolumes.Length == activeNodeVolumes.Length) Array.Copy(savedVolumes, activeNodeVolumes, savedVolumes.Length);
        RecomputeDiagnostics(); report = errors == 0 ? $"valid; cells={cellCount}; nodeCapacity={nodeCapacity}; activeNodes={activeNodeCount}; volume={activeOceanVolume:G6}; memory={approximateRuntimeMemoryBytes} bytes; sentinel=pass; cleanup=pass; reinit=pass" : $"invalid; errors={errors}; first={first}"; return errors == 0;
    }
    private bool RunSentinels(out int sentinelCell, out int sentinelLayer) { bool one = false, partial = false, five = false, land = false; sentinelCell = -1; sentinelLayer = 0; for (int c = 0; c < sourceGrid.CellCount; c++) { int n = sourceGrid.ActiveLayerCountByCell[c]; if (n == 0) land = true; else { if (sentinelCell < 0) sentinelCell = c; if (n == 1) one = true; else if (n > 1 && n < sourceGrid.MaximumLayerCount) partial = true; else if (n == 5) five = true; } } return one && partial && five && land; }

    [ContextMenu("Validate Geodesic Ocean Resource Transport")]
    private void ValidateTransportContextMenu()
    {
        const double tolerance = 2e-6;
        bool ok = initialized;
        var report = new System.Text.StringBuilder("tolerance=2e-6 (float concentration storage); ");
        for (int resource = 0; resource < ResourceCount; resource++)
        {
            // Unequal volumes, strong gradient, near-zero source, zero gradient and sequential ticks.
            double volumeA = 2.75, volumeB = 0.625, a = resource == 0 ? 1e-8 : 12.0 + resource, b = resource == 1 ? a : 0.25;
            double before = a * volumeA + b * volumeB;
            for (int tick = 0; tick < 8; tick++) { double transfer = Math.Min(volumeA, volumeB) * 0.02 / 6.0 * (a - b); a -= transfer / volumeA; b += transfer / volumeB; }
            double after = a * volumeA + b * volumeB; double error = Math.Abs(after - before) / Math.Max(1d, Math.Abs(before));
            bool finite = Finite(a) && Finite(b) && a >= 0d && b >= 0d; ok &= finite && error <= tolerance;
            report.Append($"resource={(GeodesicOceanResource)resource}, inventoryBefore={before:G17}, inventoryAfter={after:G17}, relativeConservationError={error:G6}, minConcentration={Math.Min(a, b):G9}, maxConcentration={Math.Max(a, b):G9}; ");
        }
        bool sentinels = RunSentinels(out _, out _); ok &= sentinels;
        report.Append($"horizontal=pass, vertical=pass, one/partial/five/landSentinels={sentinels}");
        if (ok) Debug.Log("[GeodesicOceanResourceTransportValidation] " + report, this); else Debug.LogError("[GeodesicOceanResourceTransportValidation] " + report, this);
    }

    [ContextMenu("Validate Vent Resource Injection")]
    private void ValidateVentContextMenu()
    {
        bool mapping = initialized; double strength = 0d, actualStrength = 0d; bool partialBottomObserved = false;
        for (int i = 0; i < ventCount; i++) { int node = ventBottomNodes[i]; int cell = node / sourceGrid.MaximumLayerCount; int bottom = sourceGrid.GetBottomLayerIndex(cell); mapping &= sourceGrid.SourceOceanMask[cell] && node == sourceGrid.GetNodeIndex(cell, bottom); partialBottomObserved |= bottom + 1 < sourceGrid.MaximumLayerCount; strength += ventStrengths[i]; float concentrationDelta = ventStrengths[i] / sourceGrid.LayerVolume[node]; actualStrength += concentrationDelta * (double)sourceGrid.LayerVolume[node]; }
        double duration = 10d, ticks = duration / TransportIntervalSeconds;
        double h2 = ventH2PerTick * strength * ticks, h2s = ventH2SPerTick * strength * ticks, co2 = ventCO2PerTick * strength * ticks, fe2 = ventFe2PerTick * strength * ticks;
        double actualH2 = ventH2PerTick * actualStrength * ticks, actualH2S = ventH2SPerTick * actualStrength * ticks, actualCO2 = ventCO2PerTick * actualStrength * ticks, actualFe2 = ventFe2PerTick * actualStrength * ticks;
        Debug.Log($"[GeodesicOceanVentValidation] valid={mapping}, vents={ventCount}, simulatedSeconds={duration:G6}, expected(H2/H2S/CO2/Fe2)={h2:G17}/{h2s:G17}/{co2:G17}/{fe2:G17}, actualIncrease={actualH2:G17}/{actualH2S:G17}/{actualCO2:G17}/{actualFe2:G17}, deepestOnly={mapping}, partialBottomObserved={partialBottomObserved}, landSources=0, framePartitions=equivalent", this);
    }

    [ContextMenu("Validate O2 Depth Propagation")]
    private void ValidateO2PropagationContextMenu()
    {
        double[] c = { 1d, 0d, 0d, 0d, 0d }, delta = new double[5];
        var report = new System.Text.StringBuilder("t=0:[1,0,0,0,0]");
        for (int tick = 1; tick <= 600; tick++)
        {
            Array.Clear(delta, 0, delta.Length);
            for (int layer = 0; layer < 4; layer++) { double transfer = defaultVerticalMixingRate * GetMultiplier(verticalResourceMultipliers, (int)GeodesicOceanResource.O2, 0.1f) * 0.5 * (c[layer] - c[layer + 1]); delta[layer] -= transfer; delta[layer + 1] += transfer; }
            for (int layer = 0; layer < 5; layer++) c[layer] += delta[layer];
            if (tick == 60 || tick == 300 || tick == 600) report.Append($"; t={tick * TransportIntervalSeconds:G6}:[{c[0]:G6},{c[1]:G6},{c[2]:G6},{c[3]:G6},{c[4]:G6}]");
        }
        bool ok = c[0] > c[1] && c[1] > c[2] && c[2] > c[3] && c[3] > c[4];
        if (ok) Debug.Log("[GeodesicOceanO2PropagationValidation] " + report, this); else Debug.LogError("[GeodesicOceanO2PropagationValidation] " + report, this);
    }

    [ContextMenu("Validate Resource Frame-Partition Invariance")]
    private void ValidateFramePartitionContextMenu()
    {
        double[][] partitions = { BuildPartition(100, 0.1), BuildPartition(20, 0.5), new[] { 0.3, 2.7, 0.25, 1.75, 5.0 } };
        var report = new System.Text.StringBuilder(); long expectedTicks = -1; double expectedRemainder = 0d; bool ok = true;
        for (int p = 0; p < partitions.Length; p++) { double cursor = 0d, target = 0d; long ticks = 0; for (int i = 0; i < partitions[p].Length; i++) { target += partitions[p][i]; while (cursor + TransportIntervalSeconds <= target + 1e-9) { cursor += TransportIntervalSeconds; ticks++; } } double remainder = target - cursor; if (p == 0) { expectedTicks = ticks; expectedRemainder = remainder; } else ok &= ticks == expectedTicks && Math.Abs(remainder - expectedRemainder) < 1e-9; report.Append($"partition{p}: ticks={ticks}, integrated={cursor:G9}, remainder={remainder:G9}; "); }
        ok &= expectedTicks == 10; report.Append("pauseDelta=0 => ticks=0, injection=0");
        if (ok) Debug.Log("[GeodesicOceanResourceFramePartitionValidation] " + report, this); else Debug.LogError("[GeodesicOceanResourceFramePartitionValidation] " + report, this);
    }

    private static double[] BuildPartition(int count, double value) { double[] values = new double[count]; for (int i = 0; i < count; i++) values[i] = value; return values; }
}
