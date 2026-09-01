using System;
using System.Text;
using UnityEngine;

/// <summary>Diagnostic-only periodic summaries of authoritative Geodesic ocean chemistry.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicChemistryTelemetry : MonoBehaviour
{
    private const int ResourceCount = 7;
    private const int LayerCount = GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount;

    [Header("Periodic chemistry telemetry (diagnostic only)")]
    [SerializeField, Tooltip("Authoritative simulated seconds between summaries. Zero or less disables periodic telemetry.")]
    private float chemistryTelemetryIntervalSimSeconds = 60f;
    [SerializeField, Tooltip("Enables detailed chemistry summaries. Disabled updates do not scan ocean or sediment state.")]
    private bool telemetryEnabled = true;
    [SerializeField, Min(0f), Tooltip("Minimum unscaled real seconds between expensive whole-ocean telemetry snapshots.")]
    private float minimumRealIntervalSeconds = 5f;
    [SerializeField, Min(0f), Tooltip("Concentration at or below which local water volume is classified as anoxic for telemetry only. This does not gate chemistry or biology.")]
    private float telemetryAnoxicO2Threshold = 0.000001f;

    private GeodesicOceanResourceField resources;
    private GeodesicAbioticChemistry chemistry;
    private GeodesicOceanSedimentField sediments;
    private GeodesicAtmosphereField atmosphere;
    private ReplicatorManager simulationClock;
    private ChemistryTelemetrySchedule schedule;
    private ChemistryCounters previousCounters;
    private bool initialized;
    private readonly WeightedChemistryStatistics ocean = new WeightedChemistryStatistics();
    private readonly WeightedChemistryStatistics[] layers = CreateLayerStatistics();
    private readonly WeightedChemistryStatistics bottom = new WeightedChemistryStatistics();
    private readonly WeightedChemistryStatistics ventBottom = new WeightedChemistryStatistics();
    private bool[] ventFootprintCells;

    public float IntervalSimSeconds => chemistryTelemetryIntervalSimSeconds;
    public float AnoxicO2Threshold => telemetryAnoxicO2Threshold;
    public bool TelemetryEnabled => telemetryEnabled;
    public float EffectiveMinimumRealIntervalSeconds => Mathf.Max(0f, minimumRealIntervalSeconds);
    public long FullTelemetrySnapshotCount => schedule.FullSnapshotCount;
    public double LastTelemetrySimulationTime => schedule.LastSnapshotSimulationTime;
    public double LastTelemetryRealTime => schedule.LastSnapshotRealTime;

    public void SetInterval(float intervalSeconds) => chemistryTelemetryIntervalSimSeconds = float.IsFinite(intervalSeconds) ? intervalSeconds : 0f;
    public void SetTelemetryEnabled(bool enabled) => telemetryEnabled = enabled;
    public void SetMinimumRealInterval(float intervalSeconds) => minimumRealIntervalSeconds = float.IsFinite(intervalSeconds) ? Mathf.Max(0f, intervalSeconds) : 0f;

    internal void InitializeForWorld(GeodesicOceanResourceField resourceField, GeodesicAbioticChemistry chemistryField, GeodesicOceanSedimentField sedimentField, double simulationTime)
    {
        ClearWorld();
        resources = resourceField; chemistry = chemistryField; sediments = sedimentField;
        atmosphere = GetComponent<GeodesicAtmosphereField>();
        simulationClock = FindFirstObjectByType<ReplicatorManager>();
        initialized = resources != null && resources.IsInitialized;
        previousCounters = ReadCounters();
        schedule.Reset(simulationTime, Time.unscaledTimeAsDouble, chemistryTelemetryIntervalSimSeconds, EffectiveMinimumRealIntervalSeconds);
        BuildVentFootprint();
        Debug.Log($"[GeodesicChemistryTelemetry] initialized atmosphere=global-authoritative telemetryInterval={chemistryTelemetryIntervalSimSeconds:G6}s anoxicO2Threshold={telemetryAnoxicO2Threshold:G6} concentrationUnits physicalVentInventoryRates={{H2={resources.VentH2Rate:G6},H2S={resources.VentH2SRate:G6},CO2={resources.VentCO2Rate:G6},Fe2={resources.VentFe2Rate:G6}}} inventoryUnit=concentration*km3/s", this);
    }

    internal void ClearWorld()
    {
        initialized = false; resources = null; chemistry = null; sediments = null; atmosphere = null; simulationClock = null;
        schedule.Clear(); previousCounters = default; ventFootprintCells = null;
    }

    private void Update()
    {
        if (!telemetryEnabled || !initialized || simulationClock == null) return;
        double now = Math.Max(0d, simulationClock.SimulationTimeSeconds);
        if (!schedule.TryTakeSnapshot(true, chemistryTelemetryIntervalSimSeconds, EffectiveMinimumRealIntervalSeconds, now, Time.unscaledTimeAsDouble)) return;
        Emit(now);
    }

    public static bool IsDue(double interval, double simulationTime, double nextTime)
    { return interval > 0d && simulationTime + 1e-9d >= nextTime; }

    internal string BuildCurrentRecord(double simulationTime)
    {
        ResetStatistics();
        GeodesicOceanLayerGrid grid = resources.SourceGrid;
        double sedimentS0 = 0d, sedimentFe3 = 0d, sedimentFeS = 0d;
        int columnsWithS0 = 0, columnsWithFe3 = 0, columnsWithFeS = 0;
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            double s0 = sediments.GetElementalSulfurInventory(cell), fe3 = sediments.GetOxidizedIronInventory(cell), feS = sediments.GetIronSulphideInventory(cell);
            sedimentS0 += s0; sedimentFe3 += fe3; sedimentFeS += feS;
            if (s0 > 0d) columnsWithS0++;
            if (fe3 > 0d) columnsWithFe3++;
            if (feS > 0d) columnsWithFeS++;
            int activeLayers = grid.ActiveLayerCountByCell[cell];
            for (int layer = 0; layer < activeLayers; layer++)
            {
                int node = grid.GetNodeIndex(cell, layer);
                double volume = grid.PhysicalLayerVolumeKm3[node];
                float co2 = resources.GetConcentrationForTelemetry(node, GeodesicOceanResource.CO2);
                float o2 = resources.GetConcentrationForTelemetry(node, GeodesicOceanResource.O2);
                float ch4 = resources.GetConcentrationForTelemetry(node, GeodesicOceanResource.CH4);
                float h2 = resources.GetConcentrationForTelemetry(node, GeodesicOceanResource.H2);
                float h2s = resources.GetConcentrationForTelemetry(node, GeodesicOceanResource.H2S);
                float fe2 = resources.GetConcentrationForTelemetry(node, GeodesicOceanResource.Fe2);
                float organicC = resources.GetConcentrationForTelemetry(node, GeodesicOceanResource.OrganicC);
                ocean.Add(volume, co2, o2, ch4, h2, h2s, fe2, organicC, telemetryAnoxicO2Threshold);
                layers[layer].Add(volume, co2, o2, ch4, h2, h2s, fe2, organicC, telemetryAnoxicO2Threshold);
                if (layer == activeLayers - 1)
                {
                    bottom.Add(volume, co2, o2, ch4, h2, h2s, fe2, organicC, telemetryAnoxicO2Threshold);
                    if (ventFootprintCells != null && ventFootprintCells[cell]) ventBottom.Add(volume, co2, o2, ch4, h2, h2s, fe2, organicC, telemetryAnoxicO2Threshold);
                }
            }
        }

        ChemistryCounters current = ReadCounters();
        ChemistryCounters delta = current - previousCounters;
        previousCounters = current;
        var text = new StringBuilder(1800);
        text.Append("[GeodesicChemistryTelemetry] simTime=").Append(simulationTime.ToString("G9"));
        text.Append(" resourceTicks=").Append(resources.CompletedTransportTicks);
        text.Append(" chemistryInterval=").Append(resources.TransportIntervalSeconds.ToString("G6"));
        text.Append(" telemetryInterval=").Append(chemistryTelemetryIntervalSimSeconds.ToString("G6"));
        AppendAllResources(text, " oceanMean", ocean);
        text.Append(" layers={");
        for (int layer = 0; layer < LayerCount; layer++) { if (layer > 0) text.Append(','); AppendLayer(text, layer, layers[layer]); }
        text.Append('}');
        AppendReducingSummary(text, " bottom", bottom);
        if (ventBottom.NodeCount > 0) AppendReducingSummary(text, " ventBottom", ventBottom); else text.Append(" ventBottom={activeNodes=0}");
        AppendCounters(text, " chemistryDelta", delta);
        AppendCounters(text, " chemistryTotal", current);
        if (chemistry != null)
        {
            text.Append(" chemistryScan={ticks=").Append(chemistry.ChemistryTicks)
                .Append(",nodesVisited=").Append(chemistry.NodesProcessed)
                .Append(",candidateNodes=").Append(chemistry.ChemistryCandidateNodes)
                .Append(",reactiveNodes=").Append(chemistry.ReactiveNodes)
                .Append(",reactionsApplied=").Append(chemistry.ReactionsApplied)
                .Append(",skippedNoReactants=").Append(chemistry.SkippedNoReactants)
                .Append(",sparseCandidateCount=").Append(chemistry.ChemistryCandidateNodes)
                .Append(",denseFallbackTicks=").Append(chemistry.DenseFallbackTicks)
                .Append('}');
        }
        text.Append(" sediment={S0=").Append(sedimentS0.ToString("G9")).Append(",Fe3=").Append(sedimentFe3.ToString("G9")).Append(",FeS=").Append(sedimentFeS.ToString("G9"));
        text.Append(",columnsWithS0=").Append(columnsWithS0).Append(",columnsWithFe3=").Append(columnsWithFe3).Append(",columnsWithFeS=").Append(columnsWithFeS).Append('}');
        AppendAtmosphere(text);
        return text.ToString();
    }

    private void Emit(double simulationTime) => Debug.Log(BuildCurrentRecord(simulationTime), this);

    private void BuildVentFootprint()
    {
        ventFootprintCells = resources.CellCount > 0 ? new bool[resources.CellCount] : null;
        for (int i = 0; i < resources.CompactOutletCount; i++)
            if (resources.TryGetVentOutlet(i, out GeodesicVentSourceOutlet outlet) && outlet.Habitat == GeodesicVentHabitat.Submarine) ventFootprintCells[outlet.CellIndex] = true;
    }

    private void ResetStatistics()
    { ocean.Reset(); bottom.Reset(); ventBottom.Reset(); for (int i = 0; i < layers.Length; i++) layers[i].Reset(); }

    private ChemistryCounters ReadCounters() => chemistry == null ? default : new ChemistryCounters(chemistry.ReactedH2Inventory, chemistry.ReactedH2SInventory, chemistry.ReactedFe2Inventory, chemistry.ConsumedO2Inventory, chemistry.DepositedS0Inventory, chemistry.DepositedFe3Inventory, chemistry.DepositedFeSInventory);

    private static void AppendAllResources(StringBuilder text, string name, WeightedChemistryStatistics value)
    {
        text.Append(name).Append("={CO2=").Append(value.Mean(0).ToString("G6")).Append(",O2=").Append(value.Mean(1).ToString("G6"));
        text.Append(",CH4=").Append(value.Mean(2).ToString("G6")).Append(",H2=").Append(value.Mean(3).ToString("G6"));
        text.Append(",H2S=").Append(value.Mean(4).ToString("G6")).Append(",Fe2=").Append(value.Mean(5).ToString("G6"));
        text.Append(",OrganicC=").Append(value.Mean(6).ToString("G6")).Append('}');
    }

    private static void AppendLayer(StringBuilder text, int layer, WeightedChemistryStatistics value)
    {
        text.Append('L').Append(layer).Append("={activeNodes=").Append(value.NodeCount).Append(",physicalVolumeKm3=").Append(value.Volume.ToString("G6"));
        text.Append(",O2Mean=").Append(value.Mean(1).ToString("G6")).Append(",O2Min=").Append(value.O2Minimum.ToString("G6")).Append(",O2Max=").Append(value.O2Maximum.ToString("G6"));
        text.Append(",anoxicFraction=").Append(value.AnoxicFraction.ToString("G6")).Append(",CO2=").Append(value.Mean(0).ToString("G6")).Append(",CH4=").Append(value.Mean(2).ToString("G6"));
        text.Append(",H2=").Append(value.Mean(3).ToString("G6")).Append(",H2S=").Append(value.Mean(4).ToString("G6")).Append(",Fe2=").Append(value.Mean(5).ToString("G6")).Append(",OrganicC=").Append(value.Mean(6).ToString("G6")).Append('}');
    }

    private static void AppendReducingSummary(StringBuilder text, string name, WeightedChemistryStatistics value)
    { text.Append(name).Append("={activeNodes=").Append(value.NodeCount).Append(",physicalVolumeKm3=").Append(value.Volume.ToString("G6")).Append(",O2Mean=").Append(value.Mean(1).ToString("G6")).Append(",H2Mean=").Append(value.Mean(3).ToString("G6")).Append(",H2SMean=").Append(value.Mean(4).ToString("G6")).Append(",Fe2Mean=").Append(value.Mean(5).ToString("G6")).Append(",anoxicFraction=").Append(value.AnoxicFraction.ToString("G6")).Append('}'); }

    private static void AppendCounters(StringBuilder text, string name, ChemistryCounters value)
    { text.Append(name).Append("={reactedH2=").Append(value.H2.ToString("G9")).Append(",reactedH2S=").Append(value.H2S.ToString("G9")).Append(",reactedFe2=").Append(value.Fe2.ToString("G9")).Append(",consumedO2=").Append(value.O2.ToString("G9")).Append(",depositedS0=").Append(value.S0.ToString("G9")).Append(",depositedFe3=").Append(value.Fe3.ToString("G9")).Append(",depositedFeS=").Append(value.FeS.ToString("G9")).Append('}'); }

    private void AppendAtmosphere(StringBuilder text)
    {
        text.Append(" atmosphere={");
        if (atmosphere == null || !atmosphere.IsInitialized) { text.Append("unavailable}"); return; }
        text.Append("pressure=").Append(atmosphere.TotalPressureBar.ToString("G9"))
            .Append(",N2=").Append(atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.N2).ToString("G9"))
            .Append(",CO2=").Append(atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.CO2).ToString("G9"))
            .Append(",O2=").Append(atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.O2).ToString("G9"))
            .Append(",CH4=").Append(atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.CH4).ToString("G9"))
            .Append(",H2=").Append(atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.H2).ToString("G9"))
            .Append(",H2S=").Append(atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.H2S).ToString("G9"))
            .Append(",exchangeTicks=").Append(atmosphere.CompletedExchangeTicks)
            .Append(",transferCO2=").Append(atmosphere.GetCumulativeNetTransferToOcean(GeodesicAtmosphericGas.CO2).ToString("G9"))
            .Append(",transferO2=").Append(atmosphere.GetCumulativeNetTransferToOcean(GeodesicAtmosphericGas.O2).ToString("G9"))
            .Append(",transferCH4=").Append(atmosphere.GetCumulativeNetTransferToOcean(GeodesicAtmosphericGas.CH4).ToString("G9"))
            .Append(",transferH2=").Append(atmosphere.GetCumulativeNetTransferToOcean(GeodesicAtmosphericGas.H2).ToString("G9"))
            .Append(",transferH2S=").Append(atmosphere.GetCumulativeNetTransferToOcean(GeodesicAtmosphericGas.H2S).ToString("G9")).Append('}');
    }

    private static WeightedChemistryStatistics[] CreateLayerStatistics()
    { var result = new WeightedChemistryStatistics[LayerCount]; for (int i = 0; i < result.Length; i++) result[i] = new WeightedChemistryStatistics(); return result; }
}

/// <summary>Allocation-free, deterministic scheduling for expensive diagnostic snapshots.</summary>
public struct ChemistryTelemetrySchedule
{
    public double NextSimulationTime { get; private set; }
    public double NextEligibleRealTime { get; private set; }
    public long FullSnapshotCount { get; private set; }
    public double LastSnapshotSimulationTime { get; private set; }
    public double LastSnapshotRealTime { get; private set; }

    public void Reset(double simulationTime, double realTime, double simulatedInterval, double minimumRealInterval)
    {
        NextSimulationTime = Nonnegative(simulationTime) + Nonnegative(simulatedInterval);
        NextEligibleRealTime = Nonnegative(realTime) + Nonnegative(minimumRealInterval);
        FullSnapshotCount = 0;
        LastSnapshotSimulationTime = LastSnapshotRealTime = 0d;
    }

    public void Clear()
    {
        NextSimulationTime = NextEligibleRealTime = 0d;
        FullSnapshotCount = 0;
        LastSnapshotSimulationTime = LastSnapshotRealTime = 0d;
    }

    public bool TryTakeSnapshot(bool enabled, double simulatedInterval, double minimumRealInterval, double simulationTime, double realTime)
    {
        if (!enabled || !GeodesicChemistryTelemetry.IsDue(simulatedInterval, simulationTime, NextSimulationTime) || realTime + 1e-9d < NextEligibleRealTime) return false;
        simulationTime = Nonnegative(simulationTime);
        realTime = Nonnegative(realTime);
        LastSnapshotSimulationTime = simulationTime;
        LastSnapshotRealTime = realTime;
        FullSnapshotCount++;
        // Crossed simulated-time boundaries are deliberately coalesced; no backlog is retained.
        NextSimulationTime = simulationTime + Nonnegative(simulatedInterval);
        NextEligibleRealTime = realTime + Nonnegative(minimumRealInterval);
        return true;
    }

    private static double Nonnegative(double value) => double.IsNaN(value) || value < 0d ? 0d : value;
}

public sealed class WeightedChemistryStatistics
{
    private readonly double[] inventories = new double[7];
    public double Volume { get; private set; }
    public double AnoxicVolume { get; private set; }
    private float o2Minimum;
    private float o2Maximum;
    public float O2Minimum => NodeCount > 0 ? o2Minimum : 0f;
    public float O2Maximum => NodeCount > 0 ? o2Maximum : 0f;
    public int NodeCount { get; private set; }
    public double AnoxicFraction => Volume > 0d ? AnoxicVolume / Volume : 0d;

    public void Reset()
    { Array.Clear(inventories, 0, inventories.Length); Volume = AnoxicVolume = 0d; o2Minimum = float.PositiveInfinity; o2Maximum = float.NegativeInfinity; NodeCount = 0; }

    public void Add(double volume, float co2, float o2, float ch4, float h2, float h2s, float fe2, float organicC, float anoxicThreshold)
    {
        inventories[0] += co2 * volume; inventories[1] += o2 * volume; inventories[2] += ch4 * volume; inventories[3] += h2 * volume;
        inventories[4] += h2s * volume; inventories[5] += fe2 * volume; inventories[6] += organicC * volume;
        Volume += volume; if (o2 <= anoxicThreshold) AnoxicVolume += volume;
        o2Minimum = Mathf.Min(o2Minimum, o2); o2Maximum = Mathf.Max(o2Maximum, o2); NodeCount++;
    }

    public double Mean(int resource) => Volume > 0d ? inventories[resource] / Volume : 0d;
}

public readonly struct ChemistryCounters
{
    public readonly double H2, H2S, Fe2, O2, S0, Fe3, FeS;
    public ChemistryCounters(double h2, double h2s, double fe2, double o2, double s0, double fe3) : this(h2, h2s, fe2, o2, s0, fe3, 0d) { }
    public ChemistryCounters(double h2, double h2s, double fe2, double o2, double s0, double fe3, double feS) { H2 = h2; H2S = h2s; Fe2 = fe2; O2 = o2; S0 = s0; Fe3 = fe3; FeS = feS; }
    public static ChemistryCounters operator -(ChemistryCounters a, ChemistryCounters b) => new ChemistryCounters(a.H2 - b.H2, a.H2S - b.H2S, a.Fe2 - b.Fe2, a.O2 - b.O2, a.S0 - b.S0, a.Fe3 - b.Fe3, a.FeS - b.FeS);
}
