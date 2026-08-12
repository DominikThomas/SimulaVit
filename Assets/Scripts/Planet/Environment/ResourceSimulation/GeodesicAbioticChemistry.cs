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
}

/// <summary>Local, inventory-conservative oxidation for authoritative Geodesic ocean nodes.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicAbioticChemistry : MonoBehaviour
{
    private static readonly ProfilerMarker ChemistryMarker = new ProfilerMarker("GeodesicOceanResource.AbioticChemistry");

    [Header("Abiotic oxidation half-lives (simulated seconds)")]
    [SerializeField, Min(0f)] private float h2OxidationHalfLifeSeconds = 60f;
    [SerializeField, Min(0f)] private float h2sOxidationHalfLifeSeconds = 120f;
    [SerializeField, Min(0f)] private float fe2OxidationHalfLifeSeconds = 180f;

    [Header("Cumulative inventories (read only)")]
    [SerializeField] private double reactedH2;
    [SerializeField] private double reactedH2S;
    [SerializeField] private double reactedFe2;
    [SerializeField] private double consumedO2;
    [SerializeField] private double depositedS0;
    [SerializeField] private double depositedFe3;

    public float H2OxidationHalfLifeSeconds => h2OxidationHalfLifeSeconds;
    public float H2SOxidationHalfLifeSeconds => h2sOxidationHalfLifeSeconds;
    public float Fe2OxidationHalfLifeSeconds => fe2OxidationHalfLifeSeconds;
    public double ReactedH2Inventory => reactedH2;
    public double ReactedH2SInventory => reactedH2S;
    public double ReactedFe2Inventory => reactedFe2;
    public double ConsumedO2Inventory => consumedO2;

    public void ResetCounters()
    { reactedH2 = reactedH2S = reactedFe2 = consumedO2 = depositedS0 = depositedFe3 = 0d; }

    internal void Step(GeodesicOceanResourceField resources, GeodesicOceanSedimentField sediments, float simulatedDeltaTime)
    {
        if (resources == null || sediments == null || !resources.IsInitialized || !sediments.IsInitialized || simulatedDeltaTime <= 0f) return;
        double h2Fraction = ReactionFraction(simulatedDeltaTime, h2OxidationHalfLifeSeconds);
        double h2sFraction = ReactionFraction(simulatedDeltaTime, h2sOxidationHalfLifeSeconds);
        double fe2Fraction = ReactionFraction(simulatedDeltaTime, fe2OxidationHalfLifeSeconds);
        if (h2Fraction <= 0d && h2sFraction <= 0d && fe2Fraction <= 0d) return;

        using (ChemistryMarker.Auto())
        {
            int[] nodes = resources.ActiveNodeIndicesForChemistry;
            float[] volumes = resources.ActiveNodeVolumesForChemistry;
            for (int i = 0; i < nodes.Length; i++)
            {
                int node = nodes[i];
                double volume = volumes[i];
                double o2 = resources.GetInventoryForChemistry(node, GeodesicOceanResource.O2, volume);
                double h2 = resources.GetInventoryForChemistry(node, GeodesicOceanResource.H2, volume);
                double h2s = resources.GetInventoryForChemistry(node, GeodesicOceanResource.H2S, volume);
                double fe2 = resources.GetInventoryForChemistry(node, GeodesicOceanResource.Fe2, volume);
                GeodesicAbioticReactionResult result = ReactNode(ref o2, ref h2, ref h2s, ref fe2, h2Fraction, h2sFraction, fe2Fraction);
                if (result.consumedO2 <= 0d) continue;
                resources.SetInventoryForChemistry(node, GeodesicOceanResource.O2, o2, volume);
                resources.SetInventoryForChemistry(node, GeodesicOceanResource.H2, h2, volume);
                resources.SetInventoryForChemistry(node, GeodesicOceanResource.H2S, h2s, volume);
                resources.SetInventoryForChemistry(node, GeodesicOceanResource.Fe2, fe2, volume);
                int cell = node / resources.SourceGrid.MaximumLayerCount;
                sediments.DepositSameColumn(cell, result.reactedH2S, result.reactedFe2);
                reactedH2 += result.reactedH2; reactedH2S += result.reactedH2S; reactedFe2 += result.reactedFe2;
                consumedO2 += result.consumedO2; depositedS0 += result.reactedH2S; depositedFe3 += result.reactedFe2;
            }
        }
    }

    public static double ReactionFraction(double simulatedDeltaTime, double halfLifeSeconds)
    {
        if (!(simulatedDeltaTime > 0d) || !(halfLifeSeconds > 0d) || !Finite(simulatedDeltaTime) || !Finite(halfLifeSeconds)) return 0d;
        return 1d - Math.Exp(-Math.Log(2d) * simulatedDeltaTime / halfLifeSeconds);
    }

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
