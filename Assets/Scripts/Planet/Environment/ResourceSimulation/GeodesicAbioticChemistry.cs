using System;
using Unity.Profiling;
using UnityEngine;

[Serializable]
public struct GeodesicAbioticReactionResult
{
    public double reactedH2;
    public double reactedH2S;
    public double reactedFe2;
    public double consumedO2;
    public double precipitatedFeS;
}

/// <summary>Local, inventory-conservative oxidation for authoritative Geodesic ocean nodes.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicAbioticChemistry : MonoBehaviour
{
    private static readonly ProfilerMarker ChemistryMarker = new ProfilerMarker("GeodesicOceanResource.AbioticChemistry");
    private static readonly ProfilerMarker ChemistryScanMarker = new ProfilerMarker("GeodesicOceanResource.Chemistry.Scan");
    private static ProfilerCounterValue<int> ActiveNodesCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Chemistry Active Nodes Total", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<int> CandidateCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Chemistry Candidates", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<int> ProcessedCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Chemistry Nodes Processed", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<int> OxidationCandidateCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Chemistry Oxidation Candidates", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<int> FeSCandidateCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Chemistry FeS Candidates", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);
    private static ProfilerCounterValue<int> SedimentCounter = new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Geodesic Chemistry Sediment Nodes", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);

    [Header("Abiotic oxidation half-lives (simulated seconds)")]
    [SerializeField, Min(0f)] private float h2OxidationHalfLifeSeconds = 60f;
    [SerializeField, Min(0f)] private float h2sOxidationHalfLifeSeconds = 120f;
    [SerializeField, Min(0f)] private float fe2OxidationHalfLifeSeconds = 180f;
    [SerializeField, Min(0f), Tooltip("Fe2 + H2S -> FeS(s) half-life. Non-positive disables the reaction.")]
    private float feSPrecipitationHalfLifeSeconds = 90f;

    [Header("Visual-only rusty water memory")]
    [SerializeField, Min(0.01f)] private float rustyWaterMemoryHalfLifeSeconds = 30f;

    [Header("Cumulative inventories (read only)")]
    [SerializeField] private double reactedH2;
    [SerializeField] private double reactedH2S;
    [SerializeField] private double reactedFe2;
    [SerializeField] private double consumedO2;
    [SerializeField] private double depositedS0;
    [SerializeField] private double depositedFe3;
    [SerializeField] private double depositedFeS;
    [Header("Last chemistry tick diagnostics (read only)")]
    [SerializeField] private int activeNodesTotal;
    [SerializeField] private int chemistryCandidateNodes;
    [SerializeField] private int nodesProcessed;
    [SerializeField] private int oxidationCandidateNodes;
    [SerializeField] private int feSCandidateNodes;
    [SerializeField] private int sedimentProducingNodes;
    private float[] recentOxidizedIronByCell;

    public float H2OxidationHalfLifeSeconds => h2OxidationHalfLifeSeconds;
    public float H2SOxidationHalfLifeSeconds => h2sOxidationHalfLifeSeconds;
    public float Fe2OxidationHalfLifeSeconds => fe2OxidationHalfLifeSeconds;
    public float FeSPrecipitationHalfLifeSeconds => feSPrecipitationHalfLifeSeconds;
    public double ReactedH2Inventory => reactedH2;
    public double ReactedH2SInventory => reactedH2S;
    public double ReactedFe2Inventory => reactedFe2;
    public double ConsumedO2Inventory => consumedO2;
    public double DepositedS0Inventory => depositedS0;
    public double DepositedFe3Inventory => depositedFe3;
    public double DepositedFeSInventory => depositedFeS;
    public int ActiveNodesTotal => activeNodesTotal;
    public int ChemistryCandidateNodes => chemistryCandidateNodes;
    public int NodesProcessed => nodesProcessed;
    // Compatibility diagnostics: chemistry now visits only candidates and performs no inactive-node rejection.
    public int ActiveNodesVisited => nodesProcessed;
    public int FastRejectedNodes => 0;
    public int OxidationCandidateNodes => oxidationCandidateNodes;
    public int FeSCandidateNodes => feSCandidateNodes;
    public int SedimentProducingNodes => sedimentProducingNodes;
    public float GetRecentOxidizedIronSignal(int cell) => recentOxidizedIronByCell != null && cell >= 0 && cell < recentOxidizedIronByCell.Length ? recentOxidizedIronByCell[cell] : 0f;

    public void ResetCounters()
    { reactedH2 = reactedH2S = reactedFe2 = consumedO2 = depositedS0 = depositedFe3 = depositedFeS = 0d; activeNodesTotal = chemistryCandidateNodes = nodesProcessed = oxidationCandidateNodes = feSCandidateNodes = sedimentProducingNodes = 0; recentOxidizedIronByCell = null; }

    internal void Step(GeodesicOceanResourceField resources, GeodesicOceanSedimentField sediments, float simulatedDeltaTime)
    {
        if (resources == null || sediments == null || !resources.IsInitialized || !sediments.IsInitialized || simulatedDeltaTime <= 0f) return;
        double h2Fraction = ReactionFraction(simulatedDeltaTime, h2OxidationHalfLifeSeconds);
        double h2sFraction = ReactionFraction(simulatedDeltaTime, h2sOxidationHalfLifeSeconds);
        double fe2Fraction = ReactionFraction(simulatedDeltaTime, fe2OxidationHalfLifeSeconds);
        double feSFraction = ReactionFraction(simulatedDeltaTime, feSPrecipitationHalfLifeSeconds);
        activeNodesTotal = resources.ActiveNodeCount;
        chemistryCandidateNodes = resources.ChemistryCandidateCount;
        nodesProcessed = oxidationCandidateNodes = feSCandidateNodes = sedimentProducingNodes = 0;
        if (recentOxidizedIronByCell == null || recentOxidizedIronByCell.Length != resources.CellCount) recentOxidizedIronByCell = new float[resources.CellCount];
        float memoryRetention = (float)(1d - ReactionFraction(simulatedDeltaTime, rustyWaterMemoryHalfLifeSeconds));
        if (h2Fraction <= 0d && h2sFraction <= 0d && fe2Fraction <= 0d && feSFraction <= 0d) return;
        // Rusty-water memory is visual only. Decay once per column rather than retaining
        // the former full layered-node chemistry scan solely for this proxy.
        for (int cell = 0; cell < recentOxidizedIronByCell.Length; cell++) recentOxidizedIronByCell[cell] *= memoryRetention;

        using (ChemistryMarker.Auto())
        {
            int[] nodes = resources.ChemistryCandidateNodes;
            int candidateCount = resources.ChemistryCandidateCount;
            float[] concentrations = resources.ConcentrationsForChemistry;
            int capacity = resources.NodeCapacityForChemistry;
            int o2Offset = (int)GeodesicOceanResource.O2 * capacity;
            int h2Offset = (int)GeodesicOceanResource.H2 * capacity;
            int h2sOffset = (int)GeodesicOceanResource.H2S * capacity;
            int fe2Offset = (int)GeodesicOceanResource.Fe2 * capacity;
            int layersPerCell = resources.SourceGrid.MaximumLayerCount;
            int oxidationCandidates = 0, feSCandidates = 0, sedimentNodes = 0;
            using (ChemistryScanMarker.Auto()) for (int i = 0; i < candidateCount; i++)
            {
                int node = nodes[i];
                int cell = node / layersPerCell;
                float h2Concentration = concentrations[h2Offset + node];
                float h2sConcentration = concentrations[h2sOffset + node];
                float fe2Concentration = concentrations[fe2Offset + node];
                double volume = resources.SourceGrid.LayerVolume[node];
                double o2 = concentrations[o2Offset + node] * volume;
                double h2 = h2Concentration * volume;
                double h2s = h2sConcentration * volume;
                double fe2 = fe2Concentration * volume;
                if (o2 > 0d) oxidationCandidates++;
                if (h2s > 0d && fe2 > 0d) feSCandidates++;
                GeodesicAbioticReactionResult result = ReactNode(ref o2, ref h2, ref h2s, ref fe2, h2Fraction, h2sFraction, fe2Fraction);
                // Deliberate operator split: oxidation consumes first; FeS then uses only remaining Fe2/H2S.
                result.precipitatedFeS = PrecipitateFeS(ref fe2, ref h2s, feSFraction);
                if (result.consumedO2 <= 0d && result.precipitatedFeS <= 0d) continue;
                if (result.reactedH2S > 0d || result.reactedFe2 > 0d || result.precipitatedFeS > 0d) sedimentNodes++;
                concentrations[o2Offset + node] = (float)(Math.Max(0d, o2) / volume);
                concentrations[h2Offset + node] = (float)(Math.Max(0d, h2) / volume);
                concentrations[h2sOffset + node] = (float)(Math.Max(0d, h2s) / volume);
                concentrations[fe2Offset + node] = (float)(Math.Max(0d, fe2) / volume);
                sediments.DepositSameColumn(cell, result.reactedH2S, result.reactedFe2, result.precipitatedFeS);
                recentOxidizedIronByCell[cell] += (float)(result.reactedFe2 / Math.Max(1e-12d, volume));
                reactedH2 += result.reactedH2; reactedH2S += result.reactedH2S; reactedFe2 += result.reactedFe2;
                consumedO2 += result.consumedO2; depositedS0 += result.reactedH2S; depositedFe3 += result.reactedFe2;
                depositedFeS += result.precipitatedFeS;
            }
            nodesProcessed = candidateCount; oxidationCandidateNodes = oxidationCandidates; feSCandidateNodes = feSCandidates; sedimentProducingNodes = sedimentNodes;
            ActiveNodesCounter.Value = activeNodesTotal; CandidateCounter.Value = chemistryCandidateNodes; ProcessedCounter.Value = nodesProcessed; OxidationCandidateCounter.Value = oxidationCandidateNodes; FeSCandidateCounter.Value = feSCandidateNodes; SedimentCounter.Value = sedimentProducingNodes;
        }
    }

    public static double PrecipitateFeS(ref double fe2, ref double h2s, double reactionFraction)
    {
        fe2 = SafeInventory(fe2); h2s = SafeInventory(h2s);
        double extent = Math.Min(fe2, h2s) * Clamp01(reactionFraction);
        if (!(extent > 0d)) return 0d;
        fe2 = Math.Max(0d, fe2 - extent); h2s = Math.Max(0d, h2s - extent);
        return extent;
    }

    public static double ReactionFraction(double simulatedDeltaTime, double halfLifeSeconds)
    {
        if (!(simulatedDeltaTime > 0d) || !(halfLifeSeconds > 0d) || !Finite(simulatedDeltaTime) || !Finite(halfLifeSeconds)) return 0d;
        return 1d - Math.Exp(-Math.Log(2d) * simulatedDeltaTime / halfLifeSeconds);
    }

    public static bool HasReducedReactants(float h2, float h2s, float fe2)
    { return h2 > 0f || h2s > 0f || fe2 > 0f; }

    public static GeodesicAbioticReactionResult ReactNode(ref double o2, ref double h2, ref double h2s, ref double fe2, double h2Fraction, double h2sFraction, double fe2Fraction)
    {
        o2 = SafeInventory(o2); h2 = SafeInventory(h2); h2s = SafeInventory(h2s); fe2 = SafeInventory(fe2);
        double requestedH2 = h2 * Clamp01(h2Fraction);
        double requestedH2S = h2s * Clamp01(h2sFraction);
        double requestedFe2 = fe2 * Clamp01(fe2Fraction);
        double demand = 0.5d * requestedH2 + 0.5d * requestedH2S + 0.25d * requestedFe2;
        if (!(demand > 0d) || !(o2 > 0d)) return default;
        double oxygenScale = demand > o2 ? Math.Max(0d, Math.Min(1d, o2 / demand)) : 1d;
        var result = new GeodesicAbioticReactionResult
        {
            reactedH2 = requestedH2 * oxygenScale,
            reactedH2S = requestedH2S * oxygenScale,
            reactedFe2 = requestedFe2 * oxygenScale
        };
        result.consumedO2 = 0.5d * result.reactedH2 + 0.5d * result.reactedH2S + 0.25d * result.reactedFe2;
        h2 = Math.Max(0d, h2 - result.reactedH2); h2s = Math.Max(0d, h2s - result.reactedH2S); fe2 = Math.Max(0d, fe2 - result.reactedFe2); o2 = Math.Max(0d, o2 - result.consumedO2);
        return result;
    }

    private static double SafeInventory(double value) => Finite(value) && value > 0d ? value : 0d;
    private static double Clamp01(double value) => Finite(value) ? Math.Max(0d, Math.Min(1d, value)) : 0d;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
