using System;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private float sulfurActivity = 1f, sulfurO2Floor = .6f;
    private bool sulfurO2Inhibition = true;
    private float metabolicMutationChance, hydrogenSulfurChance, sulfurGateH2S, sulfurGateCO2;
    private Vector2 sulfurTemperatureRange = new Vector2(333.15f, 393.15f);
    private float sulfurLethalMargin = 20f;
    private System.Random metabolismMutationRandom;
    private long sulfurMutationAttempts, sulfurMutationsAllowed, sulfurMutationsBlocked;
    private readonly long[] sulfurBlocks = new long[4]; // invalid habitat, missing H2S, missing CO2, invalid traits
    private readonly long[] sulfurParentBirths = new long[4];
    private long sulfurBirths, sulfurDeaths, sulfurPrecisionRejected, sulfurO2Samples, sulfurInhibitedSamples;
    private double sulfurCO2Consumed, sulfurH2SConsumed, sulfurCarbonFixed, sulfurProduced, sulfurO2Sum, sulfurEfficiencySum;
    private float movementSulfurCO2, movementSulfurH2S;
    private Func<float, float, float, MetabolismType, Color> metabolicColor;

    public void SetMetabolicColorResolver(Func<float, float, float, MetabolismType, Color> resolver) => metabolicColor = resolver;

    public void SetSulfurControls(float storeFraction, bool oxygenInhibition, float minimumOxygenEfficiency,
        float mutationChance, float transitionChance, float minimumH2S, float minimumCO2,
        Vector2 temperatureRange, float lethalMargin)
    {
        sulfurActivity = float.IsFinite(storeFraction) ? Mathf.Clamp01(storeFraction) : 0f;
        sulfurO2Inhibition = oxygenInhibition;
        sulfurO2Floor = float.IsFinite(minimumOxygenEfficiency) ? Mathf.Clamp01(minimumOxygenEfficiency) : .6f;
        metabolicMutationChance = float.IsFinite(mutationChance) ? Mathf.Clamp01(mutationChance) : 0f;
        hydrogenSulfurChance = float.IsFinite(transitionChance) ? Mathf.Clamp01(transitionChance) : 0f;
        sulfurGateH2S = Mathf.Max(0f, minimumH2S); sulfurGateCO2 = Mathf.Max(0f, minimumCO2);
        sulfurTemperatureRange = temperatureRange; sulfurLethalMargin = lethalMargin;
    }

    public string DescribeResolvedSulfurControls(float co2, float h2s, float energy, float comfort, float stress) => FormattableString.Invariant(
        $"[GeodesicSulfurConfig] CO2PerTick={co2:G9} H2SPerTick={h2s:G9} energyPerTick={energy:G9} referenceTickSeconds={ReferenceTickSeconds:G9} configuredStoreFraction={sulfurActivity:G9} policy=wholeReactionActivity allWithdrawnCO2Stored=true capacityLimited=true atomicMeasuredInputs=true S0=suspendedMeasuredH2S oxygenInhibition={sulfurO2Inhibition} O2Comfort/Stress/Floor={comfort:G9}/{stress:G9}/{sulfurO2Floor:G9} mutationOuter/Inner={metabolicMutationChance:G9}/{hydrogenSulfurChance:G9} perEligibleBirth={metabolicMutationChance * hydrogenSulfurChance:G9} gateH2S/CO2={sulfurGateH2S:G9}/{sulfurGateCO2:G9} gateUnits=localConcentration temperatureRangeK={sulfurTemperatureRange.x:G9}/{sulfurTemperatureRange.y:G9} lethalMarginK={sulfurLethalMargin:G9} childHabitat=inherited noDirectO2Damage=true");

    private void MutateChildMetabolism(Replicator child, MetabolismType parentMetabolism)
    {
        if (parentMetabolism != MetabolismType.Hydrogenotrophy || metabolicMutationChance <= 0f || hydrogenSulfurChance <= 0f) return;
        // Dedicated stream: enabling this edge does not draw from movement, birth, or thermal RNG.
        if (metabolismMutationRandom == null) metabolismMutationRandom = new System.Random(unchecked(biologySeed ^ 0x53554C46));
        if (metabolismMutationRandom.NextDouble() >= metabolicMutationChance
            || metabolismMutationRandom.NextDouble() >= hydrogenSulfurChance) return;
        sulfurMutationAttempts++;
        int cell = child.geodesicCellIndex, layer = child.currentOceanLayerIndex;
        int block = -1;
        if (!grid.IsNodeActive(cell, layer)) block = 0;
        else if (!resources.TryGetConcentration(cell, layer, GeodesicOceanResource.H2S, out float h2s)
            || !float.IsFinite(h2s) || !(h2s > 0f) || !float.IsFinite(sulfurGateH2S) || h2s < sulfurGateH2S) block = 1;
        else if (!resources.TryGetConcentration(cell, layer, GeodesicOceanResource.CO2, out float co2)
            || !float.IsFinite(co2) || !(co2 > 0f) || !float.IsFinite(sulfurGateCO2) || co2 < sulfurGateCO2) block = 2;
        else if (!float.IsFinite(sulfurTemperatureRange.x) || !float.IsFinite(sulfurTemperatureRange.y)
            || !float.IsFinite(sulfurLethalMargin)) block = 3;
        if (block >= 0) { sulfurMutationsBlocked++; sulfurBlocks[block]++; return; }
        sulfurMutationsAllowed++;
        child.metabolism = MetabolismType.SulfurChemosynthesis;
        child.color = Color.yellow;
        // Rebase before the existing inherited-trait mutations, as in legacy births.
        child.optimalTempMin = Mathf.Min(sulfurTemperatureRange.x, sulfurTemperatureRange.y);
        child.optimalTempMax = Mathf.Max(sulfurTemperatureRange.x, sulfurTemperatureRange.y);
        child.lethalTempMargin = Mathf.Max(1f, sulfurLethalMargin);
    }

    private void CommitSulfur(int index, ReplicatorPopulationState state, float maximumStore, bool observeAccounting)
    {
        ref Request r = ref requests[index];
        // Fail closed before any debit if the existing mineral authority is unavailable.
        if (suspended == null || !suspended.IsInitialized) { r.Actual = 0d; invalidBiologicalStates++; return; }
        float before = state.OrganicCStore[index], energyBefore = state.Energy[index];
        var transfer = resources.CommitCarbonFixation(r.Node, GeodesicOceanResource.H2S, r.Actual,
            r.NeedA, r.NeedB, r.EnergyPerExtent, before, maximumStore);
        r.Actual = transfer.extent;
        if (transfer.status == GeodesicHydrogenotrophyBalance.CommitStatus.PrecisionRejected) sulfurPrecisionRejected++;
        if (transfer.status == GeodesicHydrogenotrophyBalance.CommitStatus.Invalid) invalidBiologicalStates++;
        if (transfer.status != GeodesicHydrogenotrophyBalance.CommitStatus.Committed) return;
        suspended.AddPrecipitate(r.Cell, r.Layer, transfer.h2Withdrawn, 0d);
        state.OrganicCStore[index] = transfer.storeAfter;
        state.Energy[index] += transfer.energy;
        RecordABMetabolicEnergy(state.Locomotion[index], transfer.energy);
        extentByMetabolism[(int)r.Metabolism] += transfer.extent;
        energyByMetabolism[(int)r.Metabolism] += transfer.energy;
        sulfurCO2Consumed += transfer.co2Withdrawn; sulfurH2SConsumed += transfer.h2Withdrawn;
        sulfurCarbonFixed += (double)transfer.storeAfter - before; sulfurProduced += transfer.h2Withdrawn;
        if (!observeAccounting) return;
        r.DiagnosticStoreBefore = before; r.DiagnosticStoreAfter = transfer.storeAfter;
        r.DiagnosticEnergyDelta = (double)state.Energy[index] - energyBefore;
        r.HydrogenEnergyAwarded = transfer.energy;
        r.HydrogenCo2Ratio = transfer.co2Withdrawn / (r.NeedA * r.Actual);
        r.HydrogenH2Ratio = transfer.h2Withdrawn / (r.NeedB * r.Actual);
        accountingDiagnostics.RecordNodeExchange(r.A, -r.NeedA * r.Actual, -transfer.co2Withdrawn, false);
        accountingDiagnostics.RecordNodeExchange(r.B, -r.NeedB * r.Actual, -transfer.h2Withdrawn, false);
    }

    private void LogSulfurDiagnostics(ReplicatorPopulationState state)
    {
        var populations = new int[8];
        for (int i = 0; i < state.Count; i++)
        {
            int row = state.Metabolism[i] == MetabolismType.SulfurChemosynthesis ? 1
                : state.Metabolism[i] == MetabolismType.Hydrogenotrophy ? 0 : -1;
            if (row >= 0 && (uint)state.Locomotion[i] < 4u) populations[row * 4 + (int)state.Locomotion[i]]++;
        }
        int top = 0; for (int i = 1; i < sulfurBlocks.Length; i++) if (sulfurBlocks[i] > sulfurBlocks[top]) top = i;
        string reason = sulfurMutationsBlocked == 0 ? "none" : new[] { "invalidHabitat", "missingLocalH2S", "missingLocalCO2", "invalidThermalTraits" }[top];
        Debug.Log($"[GeodesicSulfurTelemetry] cumulative=true mutationsAttempted/Allowed/Blocked={sulfurMutationAttempts}/{sulfurMutationsAllowed}/{sulfurMutationsBlocked} topBlock={reason} topBlockCount={sulfurBlocks[top]} births={sulfurBirths} deaths={sulfurDeaths} extent={extentByMetabolism[(int)MetabolismType.SulfurChemosynthesis]:G9} CO2Consumed={sulfurCO2Consumed:G9} H2SConsumed={sulfurH2SConsumed:G9} energy={energyByMetabolism[(int)MetabolismType.SulfurChemosynthesis]:G9} carbonFixed={sulfurCarbonFixed:G9} suspendedS0Produced={sulfurProduced:G9} precisionRejected={sulfurPrecisionRejected} localO2Samples/Inhibited={sulfurO2Samples}/{sulfurInhibitedSamples} meanLocalO2={sulfurO2Sum / Math.Max(1, sulfurO2Samples):G9} meanO2Efficiency={sulfurEfficiencySum / Math.Max(1, sulfurO2Samples):G9} locomotionOrder=passive,amoeboid,flagellum,anchored hydrogenPopulation={populations[0]},{populations[1]},{populations[2]},{populations[3]} sulfurPopulation={populations[4]},{populations[5]},{populations[6]},{populations[7]} sulfurParentBirths={sulfurParentBirths[0]},{sulfurParentBirths[1]},{sulfurParentBirths[2]},{sulfurParentBirths[3]}");
    }

    private void ClearSulfur()
    {
        metabolismMutationRandom = null;
        sulfurMutationAttempts = sulfurMutationsAllowed = sulfurMutationsBlocked = sulfurBirths = sulfurDeaths = sulfurPrecisionRejected = sulfurO2Samples = sulfurInhibitedSamples = 0;
        sulfurCO2Consumed = sulfurH2SConsumed = sulfurCarbonFixed = sulfurProduced = sulfurO2Sum = sulfurEfficiencySum = 0d;
        Array.Clear(sulfurBlocks, 0, sulfurBlocks.Length);
        Array.Clear(sulfurParentBirths, 0, sulfurParentBirths.Length);
    }
}
