using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private float methaneAssimilation = 0.15f;
    private bool methaneO2Inhibition = true;
    private float methaneO2Floor, methaneO2Comfort, methaneO2Stress;
    private double methanePerTick, methaneAssimilatedPerTick, methaneEnergyPerCH4;
    private float fermentationMethanogenesisChance, methaneMinH2, methaneMinCO2, methaneMaxO2;
    private Vector2 methaneTemperatureRange;
    private float methaneLethalMargin;
    private System.Random methanogenesisMutationRandom;
    private int pendingMethanogenFounders;
    private long lastMethanogenFounderTransportTick = -1;

    public void SetMethanogenesisControls(float assimilation, bool oxygenInhibition, float minimumOxygenEfficiency,
        float transitionChance, float minimumH2, float minimumCO2, float maximumO2,
        Vector2 temperatureRange, float lethalMargin)
    {
        methaneAssimilation = float.IsFinite(assimilation) ? Mathf.Clamp01(assimilation) : 1f;
        methaneO2Inhibition = oxygenInhibition;
        methaneO2Floor = float.IsFinite(minimumOxygenEfficiency) ? Mathf.Clamp01(minimumOxygenEfficiency) : 0f;
        fermentationMethanogenesisChance = availability.Allows(MetabolismType.Methanogenesis) && float.IsFinite(transitionChance)
            ? Mathf.Clamp01(transitionChance) : 0f;
        methaneMinH2 = minimumH2;
        methaneMinCO2 = minimumCO2;
        methaneMaxO2 = maximumO2;
        methaneTemperatureRange = float.IsFinite(temperatureRange.x) && float.IsFinite(temperatureRange.y)
            ? new Vector2(Mathf.Min(temperatureRange.x, temperatureRange.y), Mathf.Max(temperatureRange.x, temperatureRange.y))
            : new Vector2(float.NaN, float.NaN);
        methaneLethalMargin = float.IsFinite(lethalMargin) ? Mathf.Max(1f, lethalMargin) : float.NaN;
    }

    private void SetMethanogenesisRecipe(float carbon, float hydrogenBudget, float energy, float oxygenComfort, float oxygenStress)
    {
        methaneO2Comfort = oxygenComfort;
        methaneO2Stress = oxygenStress;
        methanePerTick = methaneAssimilatedPerTick = methaneEnergyPerCH4 = 0d;
        if (!float.IsFinite(carbon) || !(carbon > 0f) || !float.IsFinite(hydrogenBudget) || !(hydrogenBudget > 0f)
            || !float.IsFinite(energy) || !(energy > 0f) || methaneAssimilation >= 1f) return;

        double catabolicCarbon = carbon * (1d - methaneAssimilation);
        double hydrogenLimitedCarbon = hydrogenBudget / GeodesicOceanResourceField.MethanogenesisHydrogenPerMethane;
        methanePerTick = Math.Min(catabolicCarbon, hydrogenLimitedCarbon);
        methaneAssimilatedPerTick = (double)carbon * methaneAssimilation * (methanePerTick / catabolicCarbon);
        methaneEnergyPerCH4 = (double)energy / carbon;
    }

    private float MethanogenesisOxygenEfficiency(double oxygen)
    {
        if (!double.IsFinite(oxygen) || oxygen < 0d) return 0f;
        if (!methaneO2Inhibition) return 1f;
        if (!float.IsFinite(methaneO2Comfort) || !float.IsFinite(methaneO2Stress)) return 0f;
        return CalculateAnaerobeO2Efficiency((float)oxygen, methaneO2Comfort, methaneO2Stress, methaneO2Floor);
    }

    private void BuildMethanogenesisDemand(ref Request request, float store, float maximumStore, float energy)
    {
        request.Desired *= MethanogenesisOxygenEfficiency(oxygenByNode[request.Node]);
        if (!(methanePerTick > 0d) || !double.IsFinite(request.Desired) || !(request.Desired > 0d)
            || !float.IsFinite(store) || store < 0f || !float.IsFinite(maximumStore) || maximumStore < 0f
            || !float.IsFinite(energy) || energy < 0f)
        {
            request.Desired = 0d;
            return;
        }

        double freeStore = Math.Max(0d, (double)maximumStore - store);
        request.ProductCoefficient = methanePerTick;
        request.NeedA = methanePerTick + Math.Min(methaneAssimilatedPerTick, freeStore / request.Desired);
        request.NeedB = methanePerTick * GeodesicOceanResourceField.MethanogenesisHydrogenPerMethane;
    }

    private void CommitMethanogenesis(int index, ReplicatorPopulationState state, float maximumStore, bool observeAccounting)
    {
        ref Request request = ref requests[index];
        double competedExtent = request.Actual;
        request.Actual = 0d;
        float storeBefore = state.OrganicCStore[index], energyBefore = state.Energy[index];
        if (!resources.TryCommitMethanogenesis(request.Node, competedExtent, request.NeedA, request.ProductCoefficient,
            methaneEnergyPerCH4, maximumStore, storeBefore, energyBefore, out var transfer)) return;

        request.Actual = transfer.methaneProduced / request.ProductCoefficient;
        state.OrganicCStore[index] = transfer.storeAfter;
        state.Energy[index] = transfer.energyAfter;
        double gained = (double)transfer.energyAfter - energyBefore;
        if (chemotaxisABDiagnosticsEnabled) RecordABMetabolicEnergy(state.Locomotion[index], (float)gained);
        extentByMetabolism[(int)request.Metabolism] += request.Actual;
        energyByMetabolism[(int)request.Metabolism] += gained;
        if (!observeAccounting) return;

        request.MethanogenesisCO2 = transfer.co2Consumed;
        request.MethanogenesisH2 = transfer.hydrogenConsumed;
        request.MethanogenesisCH4 = transfer.methaneProduced;
        request.DiagnosticStoreBefore = storeBefore;
        request.DiagnosticStoreAfter = transfer.storeAfter;
        request.DiagnosticEnergyDelta = gained;
        accountingDiagnostics.RecordNodeExchange(request.A, -transfer.co2Consumed, -transfer.co2Consumed, false);
        accountingDiagnostics.RecordNodeExchange(request.B, -transfer.hydrogenConsumed, -transfer.hydrogenConsumed, false);
        accountingDiagnostics.RecordNodeExchange(request.Product, transfer.methaneProduced, transfer.methaneProduced, false);
    }

    private void RecordMethanogenesisAccounting(in Request request)
    {
        accountingDiagnostics.RecordReaction(request.Metabolism, request.A, request.B, request.Product,
            request.NeedA * request.Desired, request.NeedB * request.Desired, request.MethanogenesisCO2,
            request.MethanogenesisH2, request.MethanogenesisCH4, 1d, 1d, 1d,
            request.DiagnosticStoreAfter - request.DiagnosticStoreBefore, request.DiagnosticStoreBefore,
            request.DiagnosticStoreAfter, (float)request.DiagnosticEnergyDelta, request.DiagnosticEnergyDelta,
            request.Actual > 0d, request.NeedA * request.CompetedActual, request.NeedB * request.CompetedActual);
    }

    private void TryMutateMethanogenicChild(Replicator child, MetabolismType parentMetabolism)
    {
        // Legacy uses the general roll followed by the fermentation-specific roll, with no timed unlock.
        if (parentMetabolism != MetabolismType.Fermentation || !(fermentationMethanogenesisChance > 0f)
            || !(metabolicMutationChance > 0f) || !(methanePerTick > 0d)) return;
        methanogenesisMutationRandom ??= new System.Random(unchecked(biologySeed ^ 0x4D455448));
        if (methanogenesisMutationRandom.NextDouble() >= metabolicMutationChance
            || methanogenesisMutationRandom.NextDouble() >= fermentationMethanogenesisChance
            || !IsMethanogenicHabitat(child.geodesicCellIndex, child.currentOceanLayerIndex, child.position)) return;

        child.metabolism = MetabolismType.Methanogenesis;
        child.color = ReplicatorManager.MethanogenRenderColor;
        child.optimalTempMin = methaneTemperatureRange.x;
        child.optimalTempMax = methaneTemperatureRange.y;
        child.lethalTempMargin = methaneLethalMargin;
    }

    private bool IsMethanogenicHabitat(int cell, int layer, Vector3 position, bool requireAnoxia = false)
    {
        if (!grid.IsNodeActive(cell, layer) || !GeodesicBiologyDiagnostics.FinitePosition(position)
            || !float.IsFinite(methaneMinH2) || !float.IsFinite(methaneMinCO2) || !float.IsFinite(methaneMaxO2))
            return false;

        int node = grid.GetNodeIndex(cell, layer);
        double hydrogen = resources.GetRawConcentration(GeodesicOceanResource.H2, node);
        double carbon = resources.GetRawConcentration(GeodesicOceanResource.CO2, node);
        double oxygen = resources.GetRawConcentration(GeodesicOceanResource.O2, node);
        double volume = grid.PhysicalLayerVolumeKm3[node];
        if (!double.IsFinite(hydrogen) || !(hydrogen > 0d) || hydrogen < methaneMinH2
            || !double.IsFinite(carbon) || !(carbon > 0d) || carbon < methaneMinCO2
            || !double.IsFinite(volume) || !(volume > 0d)
            || oxygen > Math.Max(0f, methaneMaxO2) || (requireAnoxia && oxygen != 0d)
            || !(MethanogenesisOxygenEfficiency(oxygen) > 0f)) return false;

        float temperature = SampleBiologicalTemperature(cell, layer, position).TemperatureKelvin;
        return CalculateTemperaturePerformance(temperature, methaneTemperatureRange.x,
            methaneTemperatureRange.y, methaneLethalMargin, temperatureSettings.DormancyFloor) > 0f;
    }

    public void ConfigureMethanogenFounders(int requested) =>
        pendingMethanogenFounders = availability.AllowsMethanogenFounders ? Math.Max(0, requested) : 0;

    private void TrySpawnMethanogenFounders(List<Replicator> agents, ReplicatorPopulationState state, int maximumPopulation)
    {
        int count = Math.Min(pendingMethanogenFounders, Math.Max(0, maximumPopulation) - agents.Count);
        long transportTick = resources.CompletedTransportTicks;
        if (count <= 0 || !(methanePerTick > 0d) || transportTick == lastMethanogenFounderTransportTick) return;
        lastMethanogenFounderTransportTick = transportTick;

        for (int i = 0; i < resources.CompactOutletCount; i++)
        {
            if (!resources.TryGetVentInjectionHabitat(i, out var outlet, out int cell, out int layer)
                || outlet.Habitat != GeodesicVentHabitat.Submarine) continue;
            float radius = grid.LayerCenterRadius[outlet.SourceNode];
            if (!float.IsFinite(radius) || !(radius > 0f)) continue;
            Vector3 local = grid.SourceTopology.CellDirections[cell] * radius;
            if (!IsMethanogenicHabitat(cell, layer, planet.transform.TransformPoint(local), requireAnoxia: true)) continue;

            var site = new FounderPosition { cell = cell, layer = layer, local = local };
            var random = new System.Random(unchecked(biologySeed ^ 0x4D465344));
            state.EnsureMatchesAgentCount(agents);
            for (int founder = 0; founder < count; founder++)
            {
                int packed = SpawnPassiveFounder(site, random, agents, state, founderMinLife, founderMaxLife,
                    ReplicatorManager.MethanogenRenderColor, founderBiomassTarget, methaneTemperatureRange, methaneLethalMargin,
                    true, MetabolismType.Methanogenesis);
                InitializePassiveSchedulesAtSpawn(state, packed, passiveMovementTime);
            }
            Debug.Log($"[GeodesicMethanogenFounders] placed={count}/{pendingMethanogenFounders} cell={cell} layer={layer}");
            pendingMethanogenFounders = 0;
            return;
        }
    }

    private void ClearMethanogenesis()
    {
        methaneAssimilation = 0.15f;
        methaneO2Inhibition = true;
        methaneO2Floor = fermentationMethanogenesisChance = 0f;
        methanogenesisMutationRandom = null;
        pendingMethanogenFounders = 0;
        lastMethanogenFounderTransportTick = -1;
    }
}
