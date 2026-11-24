using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private double saprotrophyOxidizedPerTick, saprotrophyAssimilatedPerTick;
    private float saprotrophyOxygenYield, saprotrophyEnergyYield;
    private float hydrogenSaprotrophyChance, otherSaprotrophyChance, saprotrophyMinO2, saprotrophyMinOrganicC;
    private Vector2 saprotrophyTemperatureRange;
    private float saprotrophyLethalMargin;
    private System.Random saprotrophyMutationRandom;
    private int pendingSaprotrophFounders;
    private FounderPosition? saprotrophFounderSite;

    public void SetSaprotrophyControls(float organicCPerTick, float assimilationFraction, float oxygenPerC,
        float energyPerC, float hydrogenChance, float otherChance, float minimumO2, float minimumOrganicC,
        Vector2 temperatureRange, float lethalMargin)
    {
        double uptake = float.IsFinite(organicCPerTick) && organicCPerTick > 0f ? organicCPerTick : 0d;
        double assimilation = float.IsFinite(assimilationFraction) ? Mathf.Clamp01(assimilationFraction) : 1d;
        saprotrophyOxidizedPerTick = uptake * (1d - assimilation);
        saprotrophyAssimilatedPerTick = uptake * assimilation;
        saprotrophyOxygenYield = oxygenPerC;
        saprotrophyEnergyYield = energyPerC;
        bool allowed = availability.Allows(MetabolismType.Saprotrophy);
        hydrogenSaprotrophyChance = allowed && float.IsFinite(hydrogenChance) ? Mathf.Clamp01(hydrogenChance) : 0f;
        otherSaprotrophyChance = allowed && float.IsFinite(otherChance) ? Mathf.Clamp01(otherChance) : 0f;
        saprotrophyMinO2 = minimumO2;
        saprotrophyMinOrganicC = minimumOrganicC;
        saprotrophyTemperatureRange = float.IsFinite(temperatureRange.x) && float.IsFinite(temperatureRange.y)
            ? new Vector2(Mathf.Min(temperatureRange.x, temperatureRange.y), Mathf.Max(temperatureRange.x, temperatureRange.y))
            : new Vector2(float.NaN, float.NaN);
        saprotrophyLethalMargin = float.IsFinite(lethalMargin) ? Mathf.Max(1f, lethalMargin) : float.NaN;
    }

    private bool HasSaprotrophyRecipe => saprotrophyOxidizedPerTick > 0d
        && float.IsFinite(saprotrophyOxygenYield) && saprotrophyOxygenYield > 0f
        && float.IsFinite(saprotrophyEnergyYield) && saprotrophyEnergyYield > 0f;

    private void TryMutateSaprotrophicChild(Replicator child, MetabolismType parentMetabolism)
    {
        float chance;
        switch (parentMetabolism)
        {
            case MetabolismType.Hydrogenotrophy: chance = hydrogenSaprotrophyChance; break;
            case MetabolismType.SulfurChemosynthesis:
            case MetabolismType.Fermentation:
            case MetabolismType.Photosynthesis: chance = otherSaprotrophyChance; break;
            default: return;
        }
        if (!(chance > 0f) || !planet.SaprotrophyUnlocked || !HasSaprotrophyRecipe) return;

        saprotrophyMutationRandom ??= new System.Random(unchecked(biologySeed ^ 0x53415052));
        if (saprotrophyMutationRandom.NextDouble() >= chance
            || !IsSaprotrophicHabitat(child.geodesicCellIndex, child.currentOceanLayerIndex, child.position)) return;

        child.metabolism = MetabolismType.Saprotrophy;
        child.color = Color.blue;
        child.optimalTempMin = saprotrophyTemperatureRange.x;
        child.optimalTempMax = saprotrophyTemperatureRange.y;
        child.lethalTempMargin = saprotrophyLethalMargin;
    }

    private bool IsSaprotrophicHabitat(int cell, int layer, Vector3 position)
    {
        if (!grid.IsNodeActive(cell, layer) || !GeodesicBiologyDiagnostics.FinitePosition(position)
            || !float.IsFinite(saprotrophyMinO2) || !float.IsFinite(saprotrophyMinOrganicC)) return false;

        int node = grid.GetNodeIndex(cell, layer);
        double oxygen = resources.GetRawConcentration(GeodesicOceanResource.O2, node);
        double organicC = resources.GetRawConcentration(GeodesicOceanResource.OrganicC, node);
        double volume = grid.PhysicalLayerVolumeKm3[node];
        if (!double.IsFinite(oxygen) || !(oxygen > 0d) || oxygen < saprotrophyMinO2
            || !double.IsFinite(organicC) || !(organicC > 0d) || organicC < saprotrophyMinOrganicC
            || !double.IsFinite(volume) || !(volume > 0d)) return false;

        float temperature = SampleBiologicalTemperature(cell, layer, position).TemperatureKelvin;
        return CalculateTemperaturePerformance(temperature, saprotrophyTemperatureRange.x,
            saprotrophyTemperatureRange.y, saprotrophyLethalMargin, temperatureSettings.DormancyFloor) > 0f;
    }

    public void ConfigureSaprotrophFounders(int requested) =>
        pendingSaprotrophFounders = availability.AllowsSaprotrophFounders ? Math.Max(0, requested) : 0;

    private void ConsiderSaprotrophFounderSite(int cell, int layer, int node)
    {
        if (pendingSaprotrophFounders <= 0 || saprotrophFounderSite.HasValue || !HasSaprotrophyRecipe) return;
        float radius = grid.LayerCenterRadius[node];
        if (!float.IsFinite(radius) || !(radius > 0f)) return;
        Vector3 local = grid.SourceTopology.CellDirections[cell] * radius;
        if (IsSaprotrophicHabitat(cell, layer, planet.transform.TransformPoint(local)))
            saprotrophFounderSite = new FounderPosition { cell = cell, layer = layer, local = local };
    }

    private void SpawnSaprotrophFounders(List<Replicator> agents, ReplicatorPopulationState state, int maximumPopulation)
    {
        if (!saprotrophFounderSite.HasValue) return;
        FounderPosition site = saprotrophFounderSite.Value;
        saprotrophFounderSite = null;
        int count = Math.Min(pendingSaprotrophFounders, Math.Max(0, maximumPopulation) - state.Count);
        if (count <= 0) return;

        var random = new System.Random(unchecked(biologySeed ^ 0x53465344));
        for (int i = 0; i < count; i++)
        {
            int packed = SpawnPassiveFounder(site, random, agents, state, founderMinLife, founderMaxLife,
                Color.blue, founderBiomassTarget, saprotrophyTemperatureRange, saprotrophyLethalMargin,
                true, MetabolismType.Saprotrophy);
            InitializePassiveSchedulesAtSpawn(state, packed, passiveMovementTime);
        }
        Debug.Log($"[GeodesicSaprotrophFounders] placed={count}/{pendingSaprotrophFounders} cell={site.cell} layer={site.layer}");
        pendingSaprotrophFounders = 0;
    }

    private void BuildSaprotrophyDemand(ref Request request, float store, float maximumStore, float energy)
    {
        float oxygen = oxygenByNode[request.Node], organicC = organicCarbonByNode[request.Node];
        if (!HasSaprotrophyRecipe || !double.IsFinite(request.Desired) || !(request.Desired > 0d)
            || !float.IsFinite(store) || store < 0f || !float.IsFinite(maximumStore) || maximumStore < 0f
            || !float.IsFinite(energy) || energy < 0f || !float.IsFinite(oxygen) || !(oxygen > 0f)
            || !float.IsFinite(organicC) || !(organicC > 0f))
        {
            request.Desired = 0d;
            return;
        }

        double freeStore = Math.Max(0d, (double)maximumStore - store);
        request.ProductCoefficient = saprotrophyOxidizedPerTick;
        request.NeedA = saprotrophyOxidizedPerTick
            + Math.Min(saprotrophyAssimilatedPerTick, freeStore / request.Desired);
        request.NeedB = saprotrophyOxidizedPerTick * saprotrophyOxygenYield;
    }

    private void CommitSaprotrophy(int index, ReplicatorPopulationState state, float maximumStore, bool observeAccounting)
    {
        ref Request request = ref requests[index];
        double competedExtent = request.Actual;
        request.Actual = 0d;
        float storeBefore = state.OrganicCStore[index], energyBefore = state.Energy[index];
        if (!resources.TryCommitSaprotrophy(request.Node, competedExtent, request.NeedA, request.ProductCoefficient,
            saprotrophyOxygenYield, saprotrophyEnergyYield, maximumStore, storeBefore, energyBefore,
            out var transfer)) return;

        request.Actual = transfer.co2Produced / request.ProductCoefficient;
        state.OrganicCStore[index] = transfer.storeAfter;
        state.Energy[index] = transfer.energyAfter;
        double gained = (double)transfer.energyAfter - energyBefore;
        if (chemotaxisABDiagnosticsEnabled) RecordABMetabolicEnergy(state.Locomotion[index], (float)gained);
        extentByMetabolism[(int)MetabolismType.Saprotrophy] += request.Actual;
        energyByMetabolism[(int)MetabolismType.Saprotrophy] += gained;
        if (!observeAccounting) return;

        request.SaprotrophyOrganicC = transfer.organicCConsumed;
        request.SaprotrophyO2 = transfer.oxygenConsumed;
        request.SaprotrophyCO2 = transfer.co2Produced;
        request.DiagnosticStoreBefore = storeBefore;
        request.DiagnosticStoreAfter = transfer.storeAfter;
        request.DiagnosticEnergyDelta = gained;
        accountingDiagnostics.RecordNodeExchange(request.A, -transfer.organicCConsumed, -transfer.organicCConsumed, false);
        accountingDiagnostics.RecordNodeExchange(request.B, -transfer.oxygenConsumed, -transfer.oxygenConsumed, false);
        accountingDiagnostics.RecordNodeExchange(request.Product, transfer.co2Produced, transfer.co2Produced, false);
    }

    private void RecordSaprotrophyAccounting(in Request request)
    {
        accountingDiagnostics.RecordReaction(request.Metabolism, request.A, request.B, request.Product,
            request.NeedA * request.Desired, request.NeedB * request.Desired, request.SaprotrophyOrganicC,
            request.SaprotrophyO2, request.SaprotrophyCO2, 1d, 1d, 1d,
            request.DiagnosticStoreAfter - request.DiagnosticStoreBefore, request.DiagnosticStoreBefore,
            request.DiagnosticStoreAfter, (float)request.DiagnosticEnergyDelta, request.DiagnosticEnergyDelta,
            request.Actual > 0d, request.NeedA * request.CompetedActual, request.NeedB * request.CompetedActual);
    }

    private void ClearSaprotrophy()
    {
        saprotrophyOxidizedPerTick = saprotrophyAssimilatedPerTick = 0d;
        hydrogenSaprotrophyChance = otherSaprotrophyChance = 0f;
        saprotrophyMutationRandom = null;
        pendingSaprotrophFounders = 0;
        saprotrophFounderSite = null;
    }
}
