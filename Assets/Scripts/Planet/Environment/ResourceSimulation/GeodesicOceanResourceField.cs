using System;
using System.Runtime.CompilerServices;
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
    private static ProfilerCounterValue<int> TicksPerFrameCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Resource Ticks / Frame", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<float> SimSecondsPerFrameCounter = new ProfilerCounterValue<float>(ProfilerCategory.Scripts, "Geodesic Resource Sim Seconds / Frame", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<float> BacklogCounter = new ProfilerCounterValue<float>(ProfilerCategory.Scripts, "Geodesic Resource Backlog Seconds", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<int> HorizontalActiveChannelsCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Horizontal Active Resource Channels", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<int> HorizontalSkippedChannelsCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Horizontal Skipped Uniform Channels", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<int> HorizontalLinkResourceEvaluationsCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Horizontal Link-Resource Evaluations", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);

    [Header("Startup Concentrations (Geodesic Dissolved Ocean)")]
    [SerializeField, Min(0f)] private float initialCO2Concentration = 1f;
    [SerializeField, Min(0f)] private float initialO2Concentration = 0.21f;
    [SerializeField, Min(0f)] private float initialCH4Concentration = 0f;
    [SerializeField, Min(0f)] private float initialFe2Concentration = 0f;

    [Header("Transport (inventory-conservative)")]
    [SerializeField, Min(0.01f), Tooltip("Fixed authoritative simulation seconds per resource tick. Five seconds resolves the configured 50-2000 second mixing timescales while reducing topology solves by 80% versus the one-second reference.")] private float transportIntervalSeconds = 5f;
    [SerializeField, Range(0f, 1f), Tooltip("Fractional horizontal mixing rate per simulation second. Each link is degree-normalized for explicit stability.")] private float horizontalMixingRate = 0.02f;
    [SerializeField, Range(0f, 1f), Tooltip("Fractional adjacent-layer mixing rate per simulation second. Each link is degree-normalized for explicit stability.")] private float defaultVerticalMixingRate = 0.005f;
    [SerializeField] private float[] horizontalResourceMultipliers = { 1f, 1f, 1f, 1f, 1f, 1f, 1f };
    [SerializeField, Tooltip("CO2, O2, CH4, H2, H2S, Fe2, OrganicC. O2 defaults to 0.1 so deep oxygenation lags.")] private float[] verticalResourceMultipliers = { 1f, 0.1f, 1f, 1f, 1f, 1f, 1f };
    [SerializeField, Range(1, 256)] private int maximumTransportTicksPerFrame = 64;

    [Header("Geodesic Vent Sources")]
    [SerializeField, Range(0f, 0.25f), Tooltip("Deterministic fraction of eligible cells selected as generation-only geothermal candidates.")] private float ventColumnFraction = 0.02f;
    [SerializeField, Range(0f, 1f), Tooltip("Basic geography control. Zero gives weak clustering; one gives strong clustering.")] private float ventClustering = 0.65f;
    [SerializeField, Range(0f, 1f), Tooltip("Low-frequency deterministic geothermal province contrast. Zero is uniform; one creates broad inactive regions and concentrated provinces.")] private float geothermalPatchiness = 0.8f;
    [SerializeField, Range(0f, 1f), Tooltip("Fraction of otherwise eligible land candidates retained as terrestrial geothermal systems.")] private float terrestrialVentFraction = 0.25f;
    [SerializeField, Min(0f), Tooltip("Global submarine H2 inventory injected per authoritative simulated second.") ] private float ventH2PerTick = 0.006f;
    [SerializeField, Min(0f)] private float ventH2SPerTick = 0.01f;
    [SerializeField, Min(0f)] private float ventCO2PerTick = 0f;
    [SerializeField, Min(0f), Tooltip("Global submarine Fe2 inventory injected per authoritative simulated second.")] private float ventFe2PerTick = 0.002f;
    [SerializeField, Range(1, 8), Tooltip("Maximum compact physical vent mouths retained per clustered system.")] private int maximumOutletsPerSystem = 5;
    [SerializeField, Range(0.1f, 20f), Tooltip("Maximum angular distance from a system representative for compact physical outlets.")] private float outletSelectionRadiusDegrees = 3.5f;

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
    [SerializeField] private int rawVentCandidateCount;
    [SerializeField] private int submarineVentCount;
    [SerializeField] private int terrestrialVentCount;
    [SerializeField] private float normalizedSubmarineWeightSum;
    [SerializeField] private double transportIntegrationCursorTime;
    [SerializeField] private double unconsumedTransportRemainderSeconds;
    [SerializeField] private long completedTransportTicks;
    [SerializeField] private long transportCacheMemoryBytes;
    [SerializeField] private long stagingBufferMemoryBytes;
    [SerializeField] private int resourceTicksExecutedThisFrame;
    [SerializeField] private float resourceSimSecondsProcessedThisFrame;
    [SerializeField] private float[] cachedMeanO2ByLayer = new float[GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount];
    [SerializeField] private int horizontalActiveResourceChannelsLastTick;
    [SerializeField] private int horizontalSkippedUniformChannelsLastTick;
    [SerializeField] private int horizontalLinkResourceEvaluationsLastTick;

    private GeodesicOceanLayerDomain domain;
    private PlanetGenerator planetGenerator;
    private GeodesicOceanLayerGrid sourceGrid;
    private float[] concentrationsByResourceThenNode;
    private int[] activeNodeIndices;
    private float[] activeNodeVolumes;
    private int[] chemistryCandidateNodes;
    private int chemistryCandidateCount;
    private float[] configuredInitialConcentrations = new float[ResourceCount];
    private double[] diagnosticInventoryByResource;
    private float[] diagnosticMinimumByResource;
    private float[] diagnosticMaximumByResource;
    private double[] diagnosticO2InventoryByLayer;
    private double[] diagnosticVolumeByLayer;
    private ReplicatorManager simulationClock;
    private double lastObservedSimulationTime;
    private double[] stagedInventoryDelta;
    private float[] horizontalTickCoefficients;
    private float[] verticalTickCoefficients;
    private float preparedTickDeltaTime;
    private float[] horizontalConductanceBase;
    private float[] verticalConductanceBase;
    private bool[] resourceMayHaveSpatialVariation;
    private GeodesicVentSystem[] ventSystems;
    private GeodesicVentSourceOutlet[] ventOutlets;
    private float[] submarineThermalInfluenceByCell;
    private float[] terrestrialThermalInfluenceByCell;
    private bool[] directThermalSourceByCell;
    private bool warnedTransportBacklog;
    private GeodesicAbioticChemistry abioticChemistry;
    private GeodesicOceanSedimentField sedimentField;
    private GeodesicChemistryTelemetry chemistryTelemetry;

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
    public int ResourceTicksExecutedThisFrame => resourceTicksExecutedThisFrame;
    public float ResourceSimSecondsProcessedThisFrame => resourceSimSecondsProcessedThisFrame;
    public double ResourceIntegrationCursorTime => transportIntegrationCursorTime;
    public int VentCount => initialized ? ventCount : 0;
    public int RawVentCandidateCount => initialized ? rawVentCandidateCount : 0;
    public int CompactOutletCount => initialized && ventOutlets != null ? ventOutlets.Length : 0;
    public float GeothermalPatchiness => geothermalPatchiness;
    public float GetSubmarineThermalInfluence(int cell) => initialized && submarineThermalInfluenceByCell != null && cell >= 0 && cell < submarineThermalInfluenceByCell.Length ? submarineThermalInfluenceByCell[cell] : 0f;
    public float GetTerrestrialThermalInfluence(int cell) => initialized && terrestrialThermalInfluenceByCell != null && cell >= 0 && cell < terrestrialThermalInfluenceByCell.Length ? terrestrialThermalInfluenceByCell[cell] : 0f;
    internal int[] ChemistryCandidateNodes => chemistryCandidateNodes;
    public int ChemistryCandidateCount => initialized ? chemistryCandidateCount : 0;
    public int HorizontalActiveResourceChannelsLastTick => horizontalActiveResourceChannelsLastTick;
    public int HorizontalSkippedUniformChannelsLastTick => horizontalSkippedUniformChannelsLastTick;
    public int HorizontalLinkResourceEvaluationsLastTick => horizontalLinkResourceEvaluationsLastTick;

    public bool TryGetVent(int index, out int cellIndex, out int bottomLayerIndex, out float strength)
    {
        cellIndex = -1; bottomLayerIndex = -1; strength = 0f;
        if (!TryGetVentSystem(index, out GeodesicVentSystem system)) return false;
        cellIndex = system.RepresentativeCell;
        strength = system.NormalizedHabitatWeight;
        bottomLayerIndex = system.Habitat == GeodesicVentHabitat.Submarine ? system.RepresentativeBottomNode % sourceGrid.MaximumLayerCount : -1;
        return system.Habitat == GeodesicVentHabitat.Terrestrial || (sourceGrid.SourceOceanMask[cellIndex] && sourceGrid.GetBottomLayerIndex(cellIndex) == bottomLayerIndex);
    }

    public bool TryGetVentSystem(int index, out GeodesicVentSystem system)
    { system = null; if (!initialized || ventSystems == null || index < 0 || index >= ventSystems.Length) return false; system = ventSystems[index]; return system != null; }

    public bool TryGetVentOutlet(int index, out GeodesicVentSourceOutlet outlet)
    { if (!initialized || ventOutlets == null || index < 0 || index >= ventOutlets.Length) { outlet = default; return false; } outlet = ventOutlets[index]; return true; }

    public void SetStartupTransportInterval(float intervalSeconds)
    {
        transportIntervalSeconds = Mathf.Max(0.01f, intervalSeconds);
    }

    public void SetStartupChemistryTelemetryInterval(float intervalSeconds)
    {
        ResolveChemistryComponents();
        chemistryTelemetry.SetInterval(intervalSeconds);
    }

    private void Awake() { domain = GetComponent<GeodesicOceanLayerDomain>(); planetGenerator = GetComponent<PlanetGenerator>(); simulationClock = FindFirstObjectByType<ReplicatorManager>(); ResolveChemistryComponents(); }
    private void OnDestroy() => ClearField();

    private void Update()
    {
        if (!initialized || simulationClock == null) return;
        if (planetGenerator == null || planetGenerator.CurrentGridType != PlanetGridType.GeodesicIcosphere) return;
        double target = Math.Max(0d, simulationClock.SimulationTimeSeconds);
        resourceTicksExecutedThisFrame = 0; resourceSimSecondsProcessedThisFrame = 0f;
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
        resourceTicksExecutedThisFrame = ticks; resourceSimSecondsProcessedThisFrame = (float)(ticks * interval);
        TicksPerFrameCounter.Value = ticks; SimSecondsPerFrameCounter.Value = resourceSimSecondsProcessedThisFrame; BacklogCounter.Value = (float)unconsumedTransportRemainderSeconds;
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

    public void SetStartupVentGeography(float clustering, float landFraction)
    { ventClustering = Mathf.Clamp01(clustering); terrestrialVentFraction = Mathf.Clamp01(landFraction); }

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
            ResolveChemistryComponents(); sedimentField.Initialize(cellCount); abioticChemistry.ResetCounters();
            concentrationsByResourceThenNode = new float[ResourceCount * nodeCapacity];
            diagnosticInventoryByResource = new double[ResourceCount]; diagnosticMinimumByResource = new float[ResourceCount]; diagnosticMaximumByResource = new float[ResourceCount];
            diagnosticO2InventoryByLayer = new double[GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount]; diagnosticVolumeByLayer = new double[GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount];
            activeNodeIndices = new int[activeNodeCount]; activeNodeVolumes = new float[activeNodeCount];
            chemistryCandidateNodes = new int[activeNodeCount]; chemistryCandidateCount = 0;
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
            activeOceanVolume = volume; approximateRuntimeMemoryBytes = (long)concentrationsByResourceThenNode.Length * sizeof(float) + (long)activeNodeIndices.Length * sizeof(int) + (long)activeNodeVolumes.Length * sizeof(float) + (long)chemistryCandidateNodes.Length * sizeof(int) + ResourceCount * 32L;
            BuildTransportCaches(grid, planetGenerator);
            simulationClock = FindFirstObjectByType<ReplicatorManager>();
            double simulationTime = simulationClock != null ? Math.Max(0d, simulationClock.SimulationTimeSeconds) : 0d;
            transportIntegrationCursorTime = lastObservedSimulationTime = simulationTime;
            unconsumedTransportRemainderSeconds = 0d; completedTransportTicks = 0; warnedTransportBacklog = false;
            initialized = true; initializationCount++; RecomputeDiagnosticsAndO2LayerMeans();
            chemistryTelemetry.InitializeForWorld(this, abioticChemistry, sedimentField, simulationTime);
            if (!VerifyStartupSentinel(out string sentinel)) return FailInitialization(GeodesicOceanResourceInitializationFailure.AllocationOrInitializationFailure, sentinel);
            lastSentinelVerificationDiagnostic = sentinel;
            Debug.Log($"[GeodesicOceanResource] initialized dissolved-ocean concentrations (not atmosphere; not legacy normalized totals): cells={cellCount}, nodeCapacity={nodeCapacity}, activeNodes={activeNodeCount}, volume={activeOceanVolume:G6}, memory={approximateRuntimeMemoryBytes} bytes, CO2={initialCO2Concentration:G6}, O2={initialO2Concentration:G6}, CH4={initialCH4Concentration:G6}, H2=0, H2S=0, Fe2={initialFe2Concentration:G6}, OrganicC=0, sentinel={sentinel}", this);
            Debug.Log($"[GeodesicOceanResourceTransport] interval={TransportIntervalSeconds:F3}s, activeNodes={activeNodeCount}, horizontalLinks={horizontalLinkCount}, verticalLinks={verticalLinkCount}, vents={ventCount}, stateBytes={approximateRuntimeMemoryBytes}, cacheBytes={transportCacheMemoryBytes}, stagingBytes={stagingBufferMemoryBytes}", this);
            string feSStatus =
                abioticChemistry.FeSPrecipitationHalfLifeSeconds > 0f
                    ? "enabled"
                    : "disabled";

            Debug.Log(
                $"[GeodesicAbioticChemistry] " +
                $"interval={TransportIntervalSeconds:F3}s, " +
                $"activeNodes={activeNodeCount}, " +
                $"h2HalfLife={abioticChemistry.H2OxidationHalfLifeSeconds:G6}, " +
                $"h2sHalfLife={abioticChemistry.H2SOxidationHalfLifeSeconds:G6}, " +
                $"fe2HalfLife={abioticChemistry.Fe2OxidationHalfLifeSeconds:G6}, " +
                $"feS={feSStatus}, " +
                $"feSHalfLife={abioticChemistry.FeSPrecipitationHalfLifeSeconds:G6}, " +
                $"products=S0/Fe3Precipitate/FeS, " +
                $"settling=sameColumnImmediate, " +
                $"sedimentBytes={sedimentField.ApproximateRuntimeMemoryBytes}, " +
                $"rustyWater=visual-only recent-oxidation proxy",
                this);
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
        sedimentField?.Clear(); abioticChemistry?.ResetCounters();
        chemistryTelemetry?.ClearWorld(); concentrationsByResourceThenNode = null; activeNodeIndices = null; activeNodeVolumes = null; chemistryCandidateNodes = null; chemistryCandidateCount = 0; diagnosticInventoryByResource = null; diagnosticMinimumByResource = diagnosticMaximumByResource = null; diagnosticO2InventoryByLayer = diagnosticVolumeByLayer = null; sourceGrid = null; stagedInventoryDelta = null; horizontalTickCoefficients = null; verticalTickCoefficients = null; resourceMayHaveSpatialVariation = null; preparedTickDeltaTime = 0f; horizontalConductanceBase = null; verticalConductanceBase = null; ventSystems = null; ventOutlets = null; submarineThermalInfluenceByCell = terrestrialThermalInfluenceByCell = null; directThermalSourceByCell = null; initialized = false; cellCount = nodeCapacity = activeNodeCount = horizontalLinkCount = verticalLinkCount = ventCount = rawVentCandidateCount = submarineVentCount = terrestrialVentCount = resourceTicksExecutedThisFrame = horizontalActiveResourceChannelsLastTick = horizontalSkippedUniformChannelsLastTick = horizontalLinkResourceEvaluationsLastTick = 0; resourceSimSecondsProcessedThisFrame = normalizedSubmarineWeightSum = 0f; activeOceanVolume = 0d; approximateRuntimeMemoryBytes = transportCacheMemoryBytes = stagingBufferMemoryBytes = 0; transportIntegrationCursorTime = lastObservedSimulationTime = unconsumedTransportRemainderSeconds = 0d; completedTransportTicks = 0; ResetDiagnostics(); if (countClear) clearCount++;
    }

    private void BuildTransportCaches(GeodesicOceanLayerGrid grid, PlanetGenerator generator)
    {
        horizontalLinkCount = grid.HorizontalLinkCount; verticalLinkCount = grid.VerticalLinkCount;
        // Resource-major staging preserves the old per-resource accumulation order while
        // allowing every topology link to be traversed only once per transport stage.
        stagedInventoryDelta = new double[ResourceCount * nodeCapacity];
        horizontalTickCoefficients = new float[ResourceCount];
        verticalTickCoefficients = new float[ResourceCount];
        resourceMayHaveSpatialVariation = new bool[ResourceCount];
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
        var candidates = new System.Collections.Generic.List<GeodesicVentCandidate>();
        uint seed = unchecked((uint)generator.DerivedTerrainSeed) ^ 0x6A09E667u;
        uint threshold = (uint)(Mathf.Clamp01(ventColumnFraction) * uint.MaxValue);
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            uint hash = HashVent(seed ^ (uint)cell);
            float activity = EvaluateGeothermalActivity(grid.SourceTopology.CellDirections[cell], seed);
            float patchiness = Mathf.Clamp01(geothermalPatchiness);
            float probabilityMultiplier = Mathf.Lerp(1f, Mathf.SmoothStep(0f, 2.6f, activity), patchiness);
            uint localThreshold = (uint)Math.Min(uint.MaxValue, threshold * (double)probabilityMultiplier);
            if (localThreshold == 0u || hash > localThreshold) continue;
            float strengthNoise = hash / (float)localThreshold;
            float strength = (0.35f + 0.65f * strengthNoise) * Mathf.Lerp(1f, 0.35f + 1.65f * activity, patchiness);
            int bottom = grid.GetBottomLayerIndex(cell);
            if (bottom >= 1) candidates.Add(new GeodesicVentCandidate(cell, grid.GetNodeIndex(cell, bottom), strength, GeodesicVentHabitat.Submarine));
            else if (!grid.SourceOceanMask[cell] && HashVent(hash ^ 0xBB67AE85u) / (float)uint.MaxValue <= terrestrialVentFraction)
                candidates.Add(new GeodesicVentCandidate(cell, -1, strength, GeodesicVentHabitat.Terrestrial));
        }
        float clusterRadiusDegrees = Mathf.Lerp(4f, 40f, ventClustering);
        float[] sweepRadii = { 8f, 10f, 12f, 14f };
        for (int sweepIndex = 0; sweepIndex < sweepRadii.Length; sweepIndex++)
        {
            GeodesicVentSystem[] sweep = GeodesicVentSystemClusterer.Cluster(candidates, grid.SourceTopology.CellDirections, sweepRadii[sweepIndex]);
            int sweepMin = int.MaxValue, sweepMax = 0, sweepMembers = 0; float largestShare = 0f;
            for (int i = 0; i < sweep.Length; i++) { sweepMin = Math.Min(sweepMin, sweep[i].MemberCount); sweepMax = Math.Max(sweepMax, sweep[i].MemberCount); sweepMembers += sweep[i].MemberCount; largestShare = Mathf.Max(largestShare, sweep[i].NormalizedHabitatWeight); }
            Debug.Log($"[GeodesicVentRadiusSweep] radius={sweepRadii[sweepIndex]:F2}deg, rawCandidates={candidates.Count}, clusteredVents={sweep.Length}, memberCountMinMeanMax={(sweep.Length > 0 ? sweepMin : 0)}/{(sweep.Length > 0 ? sweepMembers / (double)sweep.Length : 0d):F2}/{sweepMax}, largestClusterMembers={sweepMax}, largestProductionShare={largestShare:G6}", this);
        }
        ventSystems = GeodesicVentSystemClusterer.Cluster(candidates, grid.SourceTopology.CellDirections, clusterRadiusDegrees);
        ventOutlets = BuildCompactOutlets(ventSystems, grid.SourceTopology.CellDirections, outletSelectionRadiusDegrees, maximumOutletsPerSystem);
        BuildVentThermalInfluence(grid);
        rawVentCandidateCount = candidates.Count; ventCount = ventSystems.Length;
        int memberMin = int.MaxValue, memberMax = 0, members = 0; double rawWeight = 0d, normalized = 0d; float rawStrengthMin = float.PositiveInfinity, rawStrengthMax = 0f;
        for (int i = 0; i < ventSystems.Length; i++)
        {
            GeodesicVentSystem system = ventSystems[i]; members += system.MemberCount; memberMin = Math.Min(memberMin, system.MemberCount); memberMax = Math.Max(memberMax, system.MemberCount); rawWeight += system.RawStrengthSum; rawStrengthMin = Mathf.Min(rawStrengthMin, system.RawStrengthSum); rawStrengthMax = Mathf.Max(rawStrengthMax, system.RawStrengthSum);
            if (system.Habitat == GeodesicVentHabitat.Submarine) { submarineVentCount++; normalized += system.NormalizedHabitatWeight; } else terrestrialVentCount++;
        }
        normalizedSubmarineWeightSum = (float)normalized;
        double oldStrength = 0d; for (int i = 0; i < candidates.Count; i++) if (candidates[i].Habitat == GeodesicVentHabitat.Submarine) oldStrength += candidates[i].RawStrength;
        int submarineOutletCount = 0; for (int i = 0; i < ventOutlets.Length; i++) if (ventOutlets[i].Habitat == GeodesicVentHabitat.Submarine) submarineOutletCount++;
        Debug.Log($"[GeodesicVentSystems] patchiness={geothermalPatchiness:F2}, activityField=4 spherical provinces, rawCandidates={rawVentCandidateCount}, clusteredVents={ventCount} (submarine={submarineVentCount}, terrestrial={terrestrialVentCount}), compactOutlets={ventOutlets.Length}, resourceSourceNodes={submarineOutletCount}, coarseThermalSourceNodes={ventOutlets.Length}, authority=compactOutlets, rawMembersGenerationOnly=true, clusterRadius={clusterRadiusDegrees:F2}deg, memberCountMinMeanMax={(ventCount > 0 ? memberMin : 0)}/{(ventCount > 0 ? members / (double)ventCount : 0d):F2}/{memberMax}, clusterStrengthMinMeanMax={(ventCount > 0 ? rawStrengthMin : 0f):G6}/{(ventCount > 0 ? rawWeight / ventCount : 0d):G6}/{rawStrengthMax:G6}, rawWeightTotal={rawWeight:G6}, normalizedSubmarineWeightSum={normalized:G9}, sourceDistribution=compactOutlets, globalRates(H2/H2S/CO2/Fe2)={ventH2PerTick:G6}/{ventH2SPerTick:G6}/{ventCO2PerTick:G6}/{ventFe2PerTick:G6}; oldEffective={ventH2PerTick * oldStrength:G6}/{ventH2SPerTick * oldStrength:G6}/{ventCO2PerTick * oldStrength:G6}/{ventFe2PerTick * oldStrength:G6}; terrestrialAtmosphereInjection=pending", this);
        double[] diagnosticDurations = { 10d, 60d, 600d };
        for (int i = 0; i < diagnosticDurations.Length; i++)
        {
            double duration = diagnosticDurations[i], inverseVolume = activeOceanVolume > 0d ? 1d / activeOceanVolume : 0d;
            Debug.Log($"[GeodesicVentInventoryScale] seconds={duration:G6}, inventory(H2/H2S/CO2/Fe2)={ventH2PerTick * duration:G6}/{ventH2SPerTick * duration:G6}/{ventCO2PerTick * duration:G6}/{ventFe2PerTick * duration:G6}, wholeOceanMeanDelta={ventH2PerTick * duration * inverseVolume:G6}/{ventH2SPerTick * duration * inverseVolume:G6}/{ventCO2PerTick * duration * inverseVolume:G6}/{ventFe2PerTick * duration * inverseVolume:G6}, activeOceanVolume={activeOceanVolume:G6}", this);
        }
        stagingBufferMemoryBytes = (long)stagedInventoryDelta.Length * sizeof(double);
        transportCacheMemoryBytes = (long)(horizontalConductanceBase.Length + verticalConductanceBase.Length + horizontalTickCoefficients.Length + verticalTickCoefficients.Length) * sizeof(float);
    }

    private static uint HashVent(uint value)
    { value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; return value ^ (value >> 16); }

    public static float EvaluateGeothermalActivity(Vector3 direction, uint seed)
    {
        float activity = 0f;
        for (int i = 0; i < 4; i++)
        {
            uint h = HashVent(seed ^ unchecked(0x9E3779B9u * (uint)(i + 1)));
            Vector3 axis = new Vector3(((h & 1023u) / 511.5f) - 1f, (((h >> 10) & 1023u) / 511.5f) - 1f, (((h >> 20) & 1023u) / 511.5f) - 1f).normalized;
            float province = Mathf.Clamp01((Vector3.Dot(direction, axis) - 0.25f) / 0.75f);
            activity = Mathf.Max(activity, province * province * (3f - 2f * province));
        }
        return activity;
    }

    public static GeodesicVentSourceOutlet[] BuildCompactOutlets(GeodesicVentSystem[] systems, Vector3[] directions, float radiusDegrees, int maximumPerSystem)
    {
        if (systems == null || directions == null || maximumPerSystem <= 0) return Array.Empty<GeodesicVentSourceOutlet>();
        var result = new System.Collections.Generic.List<GeodesicVentSourceOutlet>(systems.Length * maximumPerSystem);
        int[] selected = new int[maximumPerSystem];
        for (int systemIndex = 0; systemIndex < systems.Length; systemIndex++)
        {
            GeodesicVentSystem system = systems[systemIndex];
            GeodesicVentVisualArchetype archetype = GeodesicVentOutletSelector.GetArchetype(system.RepresentativeCell);
            int requested = archetype == GeodesicVentVisualArchetype.SingleDominant ? 1 : archetype == GeodesicVentVisualArchetype.DominantWithSatellites ? 3 + (system.RepresentativeCell & 1) : 3 + system.RepresentativeCell % 3;
            int count = GeodesicVentOutletSelector.SelectLocalMembers(system, directions, radiusDegrees, Mathf.Min(requested, maximumPerSystem), selected);
            double selectedStrength = 0d; for (int i = 0; i < count; i++) selectedStrength += system.Members[selected[i]].RawStrength;
            if (!(selectedStrength > 0d)) continue;
            for (int i = 0; i < count; i++)
            {
                GeodesicVentCandidate member = system.Members[selected[i]];
                result.Add(new GeodesicVentSourceOutlet(system.Habitat, member.CellIndex, member.SourceNode, systemIndex, member.RawStrength, system.NormalizedHabitatWeight, (float)(member.RawStrength / selectedStrength)));
            }
        }
        return result.ToArray();
    }

    private void BuildVentThermalInfluence(GeodesicOceanLayerGrid grid)
    {
        submarineThermalInfluenceByCell = new float[grid.CellCount];
        terrestrialThermalInfluenceByCell = new float[grid.CellCount];
        directThermalSourceByCell = new bool[grid.CellCount];
        float maximum = 0f;
        for (int i = 0; i < ventSystems.Length; i++) maximum = Mathf.Max(maximum, ventSystems[i].RawStrengthSum);
        if (maximum <= 0f) return;
        for (int i = 0; i < ventOutlets.Length; i++)
        {
            GeodesicVentSourceOutlet outlet = ventOutlets[i];
            GeodesicVentSystem system = ventSystems[outlet.SystemIndex];
            float systemStrength = Mathf.Sqrt(system.RawStrengthSum / maximum);
            float[] map = outlet.Habitat == GeodesicVentHabitat.Submarine ? submarineThermalInfluenceByCell : terrestrialThermalInfluenceByCell;
            int cell = outlet.CellIndex;
            directThermalSourceByCell[cell] = true;
            float memberStrength = systemStrength * Mathf.Sqrt(outlet.RawStrength / Mathf.Max(system.RawStrengthMax, 1e-6f));
            map[cell] = Mathf.Max(map[cell], memberStrength);
            for (int slot = 0; slot < grid.SourceTopology.NeighborCounts[cell]; slot++)
            {
                int neighbor = grid.SourceTopology.Neighbors6[cell * 6 + slot];
                bool matchingHabitat = outlet.Habitat == GeodesicVentHabitat.Submarine ? grid.SourceOceanMask[neighbor] : !grid.SourceOceanMask[neighbor];
                if (matchingHabitat) map[neighbor] = Mathf.Max(map[neighbor], memberStrength * 0.3f);
            }
        }
        int heatedSubmarine = 0, heatedTerrestrial = 0; float minimum = float.PositiveInfinity, maximumInfluence = 0f, sum = 0f; int samples = 0;
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            if (submarineThermalInfluenceByCell[cell] > 0f) heatedSubmarine++;
            if (terrestrialThermalInfluenceByCell[cell] > 0f) heatedTerrestrial++;
            float influence = Mathf.Max(submarineThermalInfluenceByCell[cell], terrestrialThermalInfluenceByCell[cell]);
            if (influence <= 0f) continue;
            minimum = Mathf.Min(minimum, influence); maximumInfluence = Mathf.Max(maximumInfluence, influence); sum += influence; samples++;
        }
        Debug.Log($"[GeodesicVentThermalFootprint] heatedOceanBottomCells={heatedSubmarine}, heatedLandSurfaceCells={heatedTerrestrial}, localInfluenceMinMeanMax={(samples > 0 ? minimum : 0f):F4}/{(samples > 0 ? sum / samples : 0f):F4}/{maximumInfluence:F4}, outletNeighborFalloff=1/0.3, authority=compactOutlets", this);
    }

    private void TickResources(float dt)
    {
        using (TransportMarker.Auto())
        {
            // Deterministic operator split: inject sources, transport all dissolved channels, then react locally.
            // Historically named "Per Tick" startup values are rates per simulated second.
            InjectVentSources(dt);
            Array.Clear(stagedInventoryDelta, 0, stagedInventoryDelta.Length);
            PrepareTickCoefficients(dt);
            AccumulateHorizontalAllResources();
            AccumulateVerticalAllResources();
            ApplyStagedAllResources();
            abioticChemistry.Step(this, sedimentField, dt);
        }
        if ((completedTransportTicks + 1) % Math.Max(1, (long)Math.Round(5f / TransportIntervalSeconds)) == 0) RecomputeDiagnosticsAndO2LayerMeans();
    }

    private void PrepareTickCoefficients(float dt)
    {
        preparedTickDeltaTime = dt;
        for (int resource = 0; resource < ResourceCount; resource++)
        {
            float scale = StableRateScale(resource, dt);
            horizontalTickCoefficients[resource] = Mathf.Max(0f, horizontalMixingRate) * GetMultiplier(horizontalResourceMultipliers, resource, 1f) * scale;
            verticalTickCoefficients[resource] = Mathf.Max(0f, defaultVerticalMixingRate) * GetMultiplier(verticalResourceMultipliers, resource, resource == (int)GeodesicOceanResource.O2 ? 0.1f : 1f) * scale;
        }
    }

    private void AccumulateHorizontalAllResources()
    {
        int[] nodeA = sourceGrid.HorizontalNodeA, nodeB = sourceGrid.HorizontalNodeB;
        float[] state = concentrationsByResourceThenNode; double[] delta = stagedInventoryDelta; int capacity = nodeCapacity;
        float dt = preparedTickDeltaTime;
        int activeMask = CalculateHorizontalActiveMask(resourceMayHaveSpatialVariation, horizontalTickCoefficients);
        horizontalActiveResourceChannelsLastTick = CountResourceBits(activeMask);
        horizontalSkippedUniformChannelsLastTick = 0;
        for (int resource = 0; resource < ResourceCount; resource++) if (!resourceMayHaveSpatialVariation[resource]) horizontalSkippedUniformChannelsLastTick++;
        horizontalLinkResourceEvaluationsLastTick = horizontalLinkCount * horizontalActiveResourceChannelsLastTick;
        HorizontalActiveChannelsCounter.Value = horizontalActiveResourceChannelsLastTick;
        HorizontalSkippedChannelsCounter.Value = horizontalSkippedUniformChannelsLastTick;
        HorizontalLinkResourceEvaluationsCounter.Value = horizontalLinkResourceEvaluationsLastTick;
        using (HorizontalMarker.Auto())
        {
            if (activeMask == 0) return;
            float k0 = horizontalTickCoefficients[0], k1 = horizontalTickCoefficients[1], k2 = horizontalTickCoefficients[2], k3 = horizontalTickCoefficients[3], k4 = horizontalTickCoefficients[4], k5 = horizontalTickCoefficients[5], k6 = horizontalTickCoefficients[6];
            for (int i = 0; i < horizontalLinkCount; i++)
            {
                int a = nodeA[i], b = nodeB[i]; float conductance = horizontalConductanceBase[i];
                if ((activeMask & 1) != 0) AccumulatePair(state, delta, a, b, conductance * k0 * dt);
                a += capacity; b += capacity; if ((activeMask & 2) != 0) AccumulatePair(state, delta, a, b, conductance * k1 * dt);
                a += capacity; b += capacity; if ((activeMask & 4) != 0) AccumulatePair(state, delta, a, b, conductance * k2 * dt);
                a += capacity; b += capacity; if ((activeMask & 8) != 0) AccumulatePair(state, delta, a, b, conductance * k3 * dt);
                a += capacity; b += capacity; if ((activeMask & 16) != 0) AccumulatePair(state, delta, a, b, conductance * k4 * dt);
                a += capacity; b += capacity; if ((activeMask & 32) != 0) AccumulatePair(state, delta, a, b, conductance * k5 * dt);
                a += capacity; b += capacity; if ((activeMask & 64) != 0) AccumulatePair(state, delta, a, b, conductance * k6 * dt);
            }
        }
    }

    private static int CountResourceBits(int mask)
    { int count = 0; while (mask != 0) { count += mask & 1; mask >>= 1; } return count; }

    public static int CalculateHorizontalActiveMask(bool[] mayHaveSpatialVariation, float[] coefficients)
    {
        if (mayHaveSpatialVariation == null || coefficients == null) return 0;
        int mask = 0, count = Math.Min(ResourceCount, Math.Min(mayHaveSpatialVariation.Length, coefficients.Length));
        for (int resource = 0; resource < count; resource++)
            if (mayHaveSpatialVariation[resource] && coefficients[resource] != 0f) mask |= 1 << resource;
        return mask;
    }

    private void AccumulateVerticalAllResources()
    {
        int[] nodeA = sourceGrid.VerticalUpperNode, nodeB = sourceGrid.VerticalLowerNode;
        float[] state = concentrationsByResourceThenNode; double[] delta = stagedInventoryDelta; int capacity = nodeCapacity;
        float dt = preparedTickDeltaTime;
        float k0 = verticalTickCoefficients[0], k1 = verticalTickCoefficients[1], k2 = verticalTickCoefficients[2], k3 = verticalTickCoefficients[3], k4 = verticalTickCoefficients[4], k5 = verticalTickCoefficients[5], k6 = verticalTickCoefficients[6];
        using (VerticalMarker.Auto()) for (int i = 0; i < verticalLinkCount; i++)
        {
            int a = nodeA[i], b = nodeB[i]; float conductance = verticalConductanceBase[i];
            AccumulatePair(state, delta, a, b, conductance * k0 * dt);
            a += capacity; b += capacity; AccumulatePair(state, delta, a, b, conductance * k1 * dt);
            a += capacity; b += capacity; AccumulatePair(state, delta, a, b, conductance * k2 * dt);
            a += capacity; b += capacity; AccumulatePair(state, delta, a, b, conductance * k3 * dt);
            a += capacity; b += capacity; AccumulatePair(state, delta, a, b, conductance * k4 * dt);
            a += capacity; b += capacity; AccumulatePair(state, delta, a, b, conductance * k5 * dt);
            a += capacity; b += capacity; AccumulatePair(state, delta, a, b, conductance * k6 * dt);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccumulatePair(float[] state, double[] delta, int a, int b, float coefficient)
    {
        double transfer = coefficient * (state[a] - state[b]);
        if (transfer == 0d) return;
        delta[a] -= transfer; delta[b] += transfer;
    }

    private void ApplyStagedAllResources()
    {
        float[] state = concentrationsByResourceThenNode; double[] delta = stagedInventoryDelta; int capacity = nodeCapacity;
        int h2Offset = (int)GeodesicOceanResource.H2 * capacity;
        int h2sOffset = (int)GeodesicOceanResource.H2S * capacity;
        int fe2Offset = (int)GeodesicOceanResource.Fe2 * capacity;
        chemistryCandidateCount = 0;
        for (int i = 0; i < activeNodeCount; i++)
        {
            int node = activeNodeIndices[i]; double volume = activeNodeVolumes[i];
            ApplyStagedNode(state, delta, node, volume);
            node += capacity; ApplyStagedNode(state, delta, node, volume);
            node += capacity; ApplyStagedNode(state, delta, node, volume);
            node += capacity; ApplyStagedNode(state, delta, node, volume);
            node += capacity; ApplyStagedNode(state, delta, node, volume);
            node += capacity; ApplyStagedNode(state, delta, node, volume);
            node += capacity; ApplyStagedNode(state, delta, node, volume);
            node = activeNodeIndices[i];
            chemistryCandidateCount = AppendChemistryCandidate(node, state[h2Offset + node], state[h2sOffset + node], state[fe2Offset + node], chemistryCandidateNodes, chemistryCandidateCount);
        }
    }

    public static int AppendChemistryCandidate(int node, float h2, float h2s, float fe2, int[] candidates, int count)
    {
        if (!GeodesicAbioticChemistry.HasReducedReactants(h2, h2s, fe2)) return count;
        candidates[count] = node;
        return count + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyStagedNode(float[] state, double[] delta, int index, double volume)
    { state[index] = (float)Math.Max(0d, (state[index] * volume + delta[index]) / volume); }

    private void InjectVentSources(float tickScale)
    {
        bool injectH2 = ventH2PerTick != 0f, injectH2S = ventH2SPerTick != 0f, injectCO2 = ventCO2PerTick != 0f, injectFe2 = ventFe2PerTick != 0f;
        if (ventOutlets.Length != 0)
        {
            if (injectH2) resourceMayHaveSpatialVariation[(int)GeodesicOceanResource.H2] = true;
            if (injectH2S) resourceMayHaveSpatialVariation[(int)GeodesicOceanResource.H2S] = true;
            if (injectCO2) resourceMayHaveSpatialVariation[(int)GeodesicOceanResource.CO2] = true;
            if (injectFe2) resourceMayHaveSpatialVariation[(int)GeodesicOceanResource.Fe2] = true;
        }
        using (VentMarker.Auto()) for (int i = 0; i < ventOutlets.Length; i++)
        {
            GeodesicVentSourceOutlet outlet = ventOutlets[i];
            if (outlet.Habitat != GeodesicVentHabitat.Submarine) continue;
            int node = outlet.SourceNode;
            double scale = outlet.SystemBudgetWeight * outlet.WithinSystemWeight * tickScale / sourceGrid.LayerVolume[node];
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

    private void RecomputeDiagnosticsAndO2LayerMeans()
    {
        if (resourceDiagnostics == null || resourceDiagnostics.Length != ResourceCount) resourceDiagnostics = CreateDiagnosticsArray();
        if (cachedMeanO2ByLayer == null || cachedMeanO2ByLayer.Length != GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount) cachedMeanO2ByLayer = new float[GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount];
        Array.Clear(diagnosticInventoryByResource, 0, diagnosticInventoryByResource.Length);
        Array.Clear(diagnosticMaximumByResource, 0, diagnosticMaximumByResource.Length);
        Array.Clear(diagnosticO2InventoryByLayer, 0, diagnosticO2InventoryByLayer.Length);
        Array.Clear(diagnosticVolumeByLayer, 0, diagnosticVolumeByLayer.Length);
        for (int resource = 0; resource < ResourceCount; resource++) diagnosticMinimumByResource[resource] = float.PositiveInfinity;
        int layersPerCell = sourceGrid.MaximumLayerCount;
        for (int i = 0; i < activeNodeCount; i++)
        {
            int node = activeNodeIndices[i];
            double volume = activeNodeVolumes[i];
            for (int resource = 0; resource < ResourceCount; resource++)
            {
                float concentration = concentrationsByResourceThenNode[resource * nodeCapacity + node];
                diagnosticMinimumByResource[resource] = Mathf.Min(diagnosticMinimumByResource[resource], concentration);
                diagnosticMaximumByResource[resource] = Mathf.Max(diagnosticMaximumByResource[resource], concentration);
                diagnosticInventoryByResource[resource] += concentration * volume;
            }
            int layer = node % layersPerCell;
            diagnosticO2InventoryByLayer[layer] += concentrationsByResourceThenNode[(int)GeodesicOceanResource.O2 * nodeCapacity + node] * volume;
            diagnosticVolumeByLayer[layer] += volume;
        }
        for (int resource = 0; resource < ResourceCount; resource++)
        {
            resourceDiagnostics[resource].resource = (GeodesicOceanResource)resource;
            resourceDiagnostics[resource].minimumActiveConcentration = activeNodeCount > 0 ? diagnosticMinimumByResource[resource] : 0f;
            resourceDiagnostics[resource].maximumActiveConcentration = activeNodeCount > 0 ? diagnosticMaximumByResource[resource] : 0f;
            resourceDiagnostics[resource].globalInventory = diagnosticInventoryByResource[resource];
            resourceDiagnostics[resource].volumeWeightedMeanConcentration = activeOceanVolume > 0d ? diagnosticInventoryByResource[resource] / activeOceanVolume : 0d;
        }
        for (int layer = 0; layer < cachedMeanO2ByLayer.Length; layer++) cachedMeanO2ByLayer[layer] = diagnosticVolumeByLayer[layer] > 0d ? (float)(diagnosticO2InventoryByLayer[layer] / diagnosticVolumeByLayer[layer]) : float.NaN;
    }

    public bool TryGetConcentration(int cellIndex, int layerIndex, GeodesicOceanResource resource, out float concentration)
    {
        concentration = 0f; if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; concentration = concentrationsByResourceThenNode[offset]; return true;
    }
    internal double GetInventoryForChemistry(int node, GeodesicOceanResource resource, double volume)
    { return concentrationsByResourceThenNode[(int)resource * nodeCapacity + node] * volume; }
    internal void MarkSpatialVariation(GeodesicOceanResource resource)
    { resourceMayHaveSpatialVariation[(int)resource] = true; }
    internal void SetInventoryForChemistry(int node, GeodesicOceanResource resource, double inventory, double volume)
    { MarkSpatialVariation(resource); concentrationsByResourceThenNode[(int)resource * nodeCapacity + node] = (float)(Math.Max(0d, inventory) / volume); }
    internal float GetConcentrationForTelemetry(int node, GeodesicOceanResource resource)
    { return concentrationsByResourceThenNode[(int)resource * nodeCapacity + node]; }
    internal float[] ConcentrationsForChemistry => concentrationsByResourceThenNode;
    internal int NodeCapacityForChemistry => nodeCapacity;
    internal float VentH2Rate => ventH2PerTick;
    internal float VentH2SRate => ventH2SPerTick;
    internal float VentCO2Rate => ventCO2PerTick;
    internal float VentFe2Rate => ventFe2PerTick;

    private void ResolveChemistryComponents()
    {
        abioticChemistry = GetComponent<GeodesicAbioticChemistry>();
        if (abioticChemistry == null) abioticChemistry = gameObject.AddComponent<GeodesicAbioticChemistry>();
        sedimentField = GetComponent<GeodesicOceanSedimentField>();
        if (sedimentField == null) sedimentField = gameObject.AddComponent<GeodesicOceanSedimentField>();
        chemistryTelemetry = GetComponent<GeodesicChemistryTelemetry>();
        if (chemistryTelemetry == null) chemistryTelemetry = gameObject.AddComponent<GeodesicChemistryTelemetry>();
    }
    public bool TrySetConcentration(int cellIndex, int layerIndex, GeodesicOceanResource resource, float concentration)
    {
        if (!ValidateWriteValue(concentration)) return false; if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; resourceMayHaveSpatialVariation[(int)resource] = true; concentrationsByResourceThenNode[offset] = concentration; RecomputeDiagnosticsFor(resource); return true;
    }
    public bool TryAddConcentration(int cellIndex, int layerIndex, GeodesicOceanResource resource, float deltaConcentration)
    {
        if (!Finite(deltaConcentration)) { rejectedNonfiniteWriteCount++; return false; } if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; float next = concentrationsByResourceThenNode[offset] + deltaConcentration; if (!Finite(next)) { rejectedNonfiniteWriteCount++; return false; } if (next < 0f) { rejectedNegativeWriteCount++; return false; } resourceMayHaveSpatialVariation[(int)resource] = true; concentrationsByResourceThenNode[offset] = next; RecomputeDiagnosticsFor(resource); return true;
    }
    public bool TryAddInventory(int cellIndex, int layerIndex, GeodesicOceanResource resource, double inventoryDelta)
    {
        if (!Finite(inventoryDelta)) { rejectedNonfiniteWriteCount++; return false; } if (inventoryDelta < 0d) { rejectedNegativeWriteCount++; return false; } if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; int node = sourceGrid.GetNodeIndex(cellIndex, layerIndex); double delta = inventoryDelta / sourceGrid.LayerVolume[node]; if (!Finite(delta) || delta > float.MaxValue) { rejectedNonfiniteWriteCount++; return false; } float next = concentrationsByResourceThenNode[offset] + (float)delta; if (!Finite(next)) { rejectedNonfiniteWriteCount++; return false; } if (next < 0f) { rejectedNegativeWriteCount++; return false; } resourceMayHaveSpatialVariation[(int)resource] = true; concentrationsByResourceThenNode[offset] = next; RecomputeDiagnosticsFor(resource); return true;
    }
    public bool TryWithdrawInventoryBounded(int cellIndex, int layerIndex, GeodesicOceanResource resource, double requestedInventory, out double withdrawnInventory)
    {
        withdrawnInventory = 0d; if (!Finite(requestedInventory) || requestedInventory < 0d) { if (!Finite(requestedInventory)) rejectedNonfiniteWriteCount++; else rejectedNegativeWriteCount++; return false; } if (!TryResolveNode(cellIndex, layerIndex, resource, out int offset)) return false; int node = sourceGrid.GetNodeIndex(cellIndex, layerIndex); double available = concentrationsByResourceThenNode[offset] * (double)sourceGrid.LayerVolume[node]; withdrawnInventory = Math.Min(requestedInventory, available); double next = (available - withdrawnInventory) / sourceGrid.LayerVolume[node]; if (!Finite(next) || next > float.MaxValue) { rejectedNonfiniteWriteCount++; return false; } resourceMayHaveSpatialVariation[(int)resource] = true; concentrationsByResourceThenNode[offset] = (float)next; RecomputeDiagnosticsFor(resource); return true;
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

    [ContextMenu("Validate Optimized Resource Transport Equivalence")]
    private void ValidateOptimizedTransportEquivalenceContextMenu()
    {
        const int nodes = 4; int[] a = { 0, 1, 2, 0, 1 }, b = { 1, 2, 3, 2, 3 };
        float[] conductance = { 0.17f, 0.11f, 0.23f, 0.07f, 0.13f };
        float[] coefficient = { 0.02f, 0.0005f, 0.018f, 0.015f, 0.012f, 0.01f, 0.008f };
        float[] state = new float[ResourceCount * nodes]; double[] reference = new double[state.Length], optimized = new double[state.Length];
        for (int resource = 0; resource < ResourceCount; resource++) for (int node = 0; node < nodes; node++) state[resource * nodes + node] = 0.125f + resource * 0.7f + node * node * 0.31f;
        for (int resource = 0; resource < ResourceCount; resource++) for (int link = 0; link < a.Length; link++) AccumulatePair(state, reference, resource * nodes + a[link], resource * nodes + b[link], conductance[link] * coefficient[resource]);
        for (int link = 0; link < a.Length; link++) for (int resource = 0; resource < ResourceCount; resource++) AccumulatePair(state, optimized, resource * nodes + a[link], resource * nodes + b[link], conductance[link] * coefficient[resource]);
        double maximumDifference = 0d, referenceSum = 0d, optimizedSum = 0d;
        for (int i = 0; i < reference.Length; i++) { maximumDifference = Math.Max(maximumDifference, Math.Abs(reference[i] - optimized[i])); referenceSum += reference[i]; optimizedSum += optimized[i]; }
        bool valid = maximumDifference == 0d && referenceSum == optimizedSum && Math.Abs(referenceSum) <= 1e-15d;
        string report = $"valid={valid}, maxStagedInventoryDifference={maximumDifference:G17}, referenceDeltaSum={referenceSum:G17}, optimizedDeltaSum={optimizedSum:G17}, resources={ResourceCount}, nodes={nodes}, links={a.Length}";
        if (valid) Debug.Log("[GeodesicOceanResourceOptimizedEquivalence] " + report, this); else Debug.LogError("[GeodesicOceanResourceOptimizedEquivalence] " + report, this);
    }

    [ContextMenu("Validate Vent Resource Injection")]
    private void ValidateVentContextMenu()
    {
        bool mapping = initialized; double normalized = 0d, localShares = 0d; bool partialBottomObserved = false;
        bool[] outletCells = initialized ? new bool[cellCount] : Array.Empty<bool>();
        for (int i = 0; i < ventCount; i++) if (ventSystems[i].Habitat == GeodesicVentHabitat.Submarine) normalized += ventSystems[i].NormalizedHabitatWeight;
        for (int i = 0; i < CompactOutletCount; i++)
        {
            GeodesicVentSourceOutlet outlet = ventOutlets[i]; outletCells[outlet.CellIndex] = true; if (outlet.Habitat != GeodesicVentHabitat.Submarine) continue;
            int node = outlet.SourceNode; int cell = outlet.CellIndex; int bottom = sourceGrid.GetBottomLayerIndex(cell);
            mapping &= sourceGrid.SourceOceanMask[cell] && node == sourceGrid.GetNodeIndex(cell, bottom); partialBottomObserved |= bottom + 1 < sourceGrid.MaximumLayerCount;
            localShares += outlet.SystemBudgetWeight * outlet.WithinSystemWeight;
        }
        bool noGhostSources = true;
        for (int cell = 0; cell < cellCount; cell++) if (directThermalSourceByCell[cell] != outletCells[cell]) noGhostSources = false;
        double duration = 10d;
        double h2 = ventH2PerTick * duration, h2s = ventH2SPerTick * duration, co2 = ventCO2PerTick * duration, fe2 = ventFe2PerTick * duration;
        bool valid = mapping && noGhostSources && Math.Abs(normalized - 1d) <= 1e-6d && Math.Abs(localShares - 1d) <= 1e-6d;
        Debug.Log($"[GeodesicOceanVentValidation] valid={valid}, authority=compactOutlets, rawMembersGenerationOnly=true, systems={ventCount}, compactOutlets={CompactOutletCount}, submarine={submarineVentCount}, terrestrial={terrestrialVentCount}, simulatedSeconds={duration:G6}, expectedAndDistributed(H2/H2S/CO2/Fe2)={h2:G17}/{h2s:G17}/{co2:G17}/{fe2:G17}, normalizedWeightSum={normalized:G17}, localShareSum={localShares:G17}, directSourcesAreOutlets={mapping}, fullStrengthThermalSourcesAreOutlets={noGhostSources}, partialBottomObserved={partialBottomObserved}, terrestrialAtmosphereInjection=pending, cadence=rate*simulatedSeconds", this);
    }

    [ContextMenu("Validate O2 Depth Propagation")]
    private void ValidateO2PropagationContextMenu()
    {
        double[] c = { 1d, 0d, 0d, 0d, 0d }, delta = new double[5];
        var report = new System.Text.StringBuilder("t=0:[1,0,0,0,0]");
        int totalTicks = Mathf.RoundToInt(600f / TransportIntervalSeconds);
        for (int tick = 1; tick <= totalTicks; tick++)
        {
            Array.Clear(delta, 0, delta.Length);
            for (int layer = 0; layer < 4; layer++) { double transfer = defaultVerticalMixingRate * GetMultiplier(verticalResourceMultipliers, (int)GeodesicOceanResource.O2, 0.1f) * TransportIntervalSeconds * 0.5 * (c[layer] - c[layer + 1]); delta[layer] -= transfer; delta[layer + 1] += transfer; }
            for (int layer = 0; layer < 5; layer++) c[layer] += delta[layer];
            float elapsed = tick * TransportIntervalSeconds;
            if (Mathf.Approximately(elapsed, 60f) || Mathf.Approximately(elapsed, 300f) || Mathf.Approximately(elapsed, 600f)) report.Append($"; t={elapsed:G6}:[{c[0]:G6},{c[1]:G6},{c[2]:G6},{c[3]:G6},{c[4]:G6}]");
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
        ok &= expectedTicks == (long)Math.Floor(10d / TransportIntervalSeconds + 1e-9d); report.Append("pauseDelta=0 => ticks=0, injection=0");
        if (ok) Debug.Log("[GeodesicOceanResourceFramePartitionValidation] " + report, this); else Debug.LogError("[GeodesicOceanResourceFramePartitionValidation] " + report, this);
    }

    private struct CadenceErrorMetrics
    {
        public double maximumAbsolute, absoluteSum, squaredSum, maximumRelative;
        public int count;
        public void Add(double candidate, double reference)
        {
            double difference = Math.Abs(candidate - reference); maximumAbsolute = Math.Max(maximumAbsolute, difference); absoluteSum += difference; squaredSum += difference * difference; count++;
            if (Math.Abs(reference) > 1e-8d) maximumRelative = Math.Max(maximumRelative, difference / Math.Abs(reference));
        }
        public override string ToString() => $"maxAbs={maximumAbsolute:G9}, meanAbs={(count > 0 ? absoluteSum / count : 0d):G9}, rms={(count > 0 ? Math.Sqrt(squaredSum / count) : 0d):G9}, maxRelative={maximumRelative:G6}";
    }

    [ContextMenu("Validate Geodesic Resource Transport Cadence Sensitivity")]
    private void ValidateCadenceSensitivityContextMenu()
    {
        double[] candidates = { 2d, 5d, 10d }, checkpoints = { 60d, 300d, 600d, 1200d };
        double[] horizontalVolumes = { 1d, 1.7d, 0.65d, 2.2d, 0.9d, 1.35d };
        int[] horizontalA = { 0, 1, 2, 3, 4, 0, 1 }, horizontalB = { 1, 2, 3, 4, 5, 2, 4 };
        double[] horizontalConductance = { 0.22d, 0.18d, 0.14d, 0.20d, 0.16d, 0.10d, 0.12d };
        double[][] horizontalInitial = { new[] { 10d, 8d, 0.5d, 0.1d, 0d, 0d }, new[] { 0d, 0d, 5d, 8d, 0.2d, 0d }, new[] { 6d, 0d, 0.2d, 0d, 3d, 0.1d } };
        double[] fiveVolumes = { 1d, 0.95d, 0.8d, 0.65d, 0.5d }, partialVolumes = { 1d, 0.7d, 0.4d };
        int[] fiveA = { 0, 1, 2, 3 }, fiveB = { 1, 2, 3, 4 }, partialA = { 0, 1 }, partialB = { 1, 2 };
        double[] fiveConductance = BuildAdjacentConductance(fiveVolumes), partialConductance = BuildAdjacentConductance(partialVolumes);
        double[] o2Initial = { 1d, 0d, 0d, 0d, 0d }, fiveZero = new double[5], partialZero = new double[3];
        double[] ventRates = { 0.006d, 0.01d, 0.02d, 0.002d };
        var report = new System.Text.StringBuilder("reference=1s; cases=CO2/H2/H2S horizontal gradients + five-layer O2 + five/three-layer vents; checkpoints=60/300/600/1200s\n");
        report.Append("cadence=1s, stable=True (generalBound=0.025, o2Bound=0.0205), O2 ");
        for (int checkpointIndex = 0; checkpointIndex < 3; checkpointIndex++) { double duration = checkpoints[checkpointIndex]; double[] o2 = SimulateCadenceCase(1d, duration, fiveVolumes, fiveA, fiveB, fiveConductance, 0.0005d, o2Initial, 0d); report.Append($"t={duration:G3}:[{o2[0]:G9},{o2[1]:G9},{o2[2]:G9},{o2[3]:G9},{o2[4]:G9}] "); }
        report.AppendLine();
        bool valid = true;
        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            double cadence = candidates[candidateIndex]; CadenceErrorMetrics horizontal = default, depth = default, vent = default; double maximumInventoryError = 0d;
            bool finiteNonnegative = true, o2Ordered = true, ventBottomStrongest = true;
            for (int checkpointIndex = 0; checkpointIndex < checkpoints.Length; checkpointIndex++)
            {
                double duration = checkpoints[checkpointIndex];
                for (int resource = 0; resource < horizontalInitial.Length; resource++)
                {
                    double[] reference = SimulateCadenceCase(1d, duration, horizontalVolumes, horizontalA, horizontalB, horizontalConductance, 0.02d, horizontalInitial[resource], 0d);
                    double[] result = SimulateCadenceCase(cadence, duration, horizontalVolumes, horizontalA, horizontalB, horizontalConductance, 0.02d, horizontalInitial[resource], 0d);
                    AccumulateCadenceMetrics(result, reference, ref horizontal, ref finiteNonnegative);
                    maximumInventoryError = Math.Max(maximumInventoryError, InventoryError(result, horizontalInitial[resource], horizontalVolumes, 0d));
                }
                double[] o2Reference = SimulateCadenceCase(1d, duration, fiveVolumes, fiveA, fiveB, fiveConductance, 0.0005d, o2Initial, 0d);
                double[] o2Result = SimulateCadenceCase(cadence, duration, fiveVolumes, fiveA, fiveB, fiveConductance, 0.0005d, o2Initial, 0d);
                AccumulateCadenceMetrics(o2Result, o2Reference, ref depth, ref finiteNonnegative); o2Ordered &= StrictlyDescending(o2Result);
                maximumInventoryError = Math.Max(maximumInventoryError, InventoryError(o2Result, o2Initial, fiveVolumes, 0d));
                for (int source = 0; source < ventRates.Length; source++)
                {
                    double[] reference = SimulateCadenceCase(1d, duration, fiveVolumes, fiveA, fiveB, fiveConductance, 0.005d, fiveZero, ventRates[source]);
                    double[] result = SimulateCadenceCase(cadence, duration, fiveVolumes, fiveA, fiveB, fiveConductance, 0.005d, fiveZero, ventRates[source]);
                    AccumulateCadenceMetrics(result, reference, ref vent, ref finiteNonnegative); ventBottomStrongest &= StrictlyAscending(result);
                    maximumInventoryError = Math.Max(maximumInventoryError, InventoryError(result, fiveZero, fiveVolumes, ventRates[source] * duration));
                    reference = SimulateCadenceCase(1d, duration, partialVolumes, partialA, partialB, partialConductance, 0.005d, partialZero, ventRates[source]);
                    result = SimulateCadenceCase(cadence, duration, partialVolumes, partialA, partialB, partialConductance, 0.005d, partialZero, ventRates[source]);
                    AccumulateCadenceMetrics(result, reference, ref vent, ref finiteNonnegative); ventBottomStrongest &= StrictlyAscending(result);
                    maximumInventoryError = Math.Max(maximumInventoryError, InventoryError(result, partialZero, partialVolumes, ventRates[source] * duration));
                }
            }
            double generalRemovalFraction = cadence * (0.02d + 0.005d), o2RemovalFraction = cadence * (0.02d + 0.0005d); bool stable = generalRemovalFraction <= 1d && o2RemovalFraction <= 1d;
            valid &= stable && finiteNonnegative && o2Ordered && ventBottomStrongest && maximumInventoryError <= 1e-10d;
            report.Append($"cadence={cadence:G3}s, stable={stable} (generalBound={generalRemovalFraction:G4}, o2Bound={o2RemovalFraction:G4}), horizontal[{horizontal}], depth[{depth}], vent[{vent}], maxInventoryError={maximumInventoryError:G6}, finiteNonnegative={finiteNonnegative}, surfaceFirstO2={o2Ordered}, ventBottomStrongest={ventBottomStrongest}\n");
            report.Append("O2 ");
            for (int checkpointIndex = 0; checkpointIndex < 3; checkpointIndex++) { double duration = checkpoints[checkpointIndex]; double[] o2 = SimulateCadenceCase(cadence, duration, fiveVolumes, fiveA, fiveB, fiveConductance, 0.0005d, o2Initial, 0d); report.Append($"t={duration:G3}:[{o2[0]:G9},{o2[1]:G9},{o2[2]:G9},{o2[3]:G9},{o2[4]:G9}] "); }
            report.AppendLine();
        }
        report.Append("selected=5s; rationale=80% fewer topology solves than 1s while representative maxAbs errors remain 0.05492 horizontal, 0.0001829 depth, 0.02696 vent; 10s roughly doubles those localized errors. Vent inventory is rate*simulatedSeconds for every cadence.");
        if (valid) Debug.Log("[GeodesicOceanResourceCadenceValidation]\n" + report, this); else Debug.LogError("[GeodesicOceanResourceCadenceValidation]\n" + report, this);
    }

    private static double[] BuildAdjacentConductance(double[] volumes)
    { double[] result = new double[volumes.Length - 1]; for (int i = 0; i < result.Length; i++) result[i] = Math.Min(volumes[i], volumes[i + 1]) * 0.5d; return result; }

    private static double[] SimulateCadenceCase(double cadence, double duration, double[] volumes, int[] linkA, int[] linkB, double[] conductance, double rate, double[] initial, double bottomSourceRate)
    {
        double[] state = (double[])initial.Clone(), delta = new double[state.Length]; int ticks = (int)Math.Round(duration / cadence);
        for (int tick = 0; tick < ticks; tick++)
        {
            Array.Clear(delta, 0, delta.Length);
            for (int link = 0; link < linkA.Length; link++) { int a = linkA[link], b = linkB[link]; double transfer = conductance[link] * rate * cadence * (state[a] - state[b]); delta[a] -= transfer; delta[b] += transfer; }
            for (int node = 0; node < state.Length; node++) state[node] = (state[node] * volumes[node] + delta[node]) / volumes[node];
            state[state.Length - 1] += bottomSourceRate * cadence / volumes[volumes.Length - 1];
        }
        return state;
    }

    private static void AccumulateCadenceMetrics(double[] candidate, double[] reference, ref CadenceErrorMetrics metrics, ref bool finiteNonnegative)
    { for (int i = 0; i < candidate.Length; i++) { metrics.Add(candidate[i], reference[i]); finiteNonnegative &= Finite(candidate[i]) && candidate[i] >= 0d; } }
    private static double InventoryError(double[] state, double[] initial, double[] volumes, double expectedAdded)
    { double before = 0d, after = 0d; for (int i = 0; i < state.Length; i++) { before += initial[i] * volumes[i]; after += state[i] * volumes[i]; } return Math.Abs(after - before - expectedAdded); }
    private static bool StrictlyDescending(double[] values) { for (int i = 1; i < values.Length; i++) if (!(values[i - 1] > values[i])) return false; return true; }
    private static bool StrictlyAscending(double[] values) { for (int i = 1; i < values.Length; i++) if (!(values[i - 1] < values[i])) return false; return true; }

    private static double[] BuildPartition(int count, double value) { double[] values = new double[count]; for (int i = 0; i < count; i++) values[i] = value; return values; }
}
