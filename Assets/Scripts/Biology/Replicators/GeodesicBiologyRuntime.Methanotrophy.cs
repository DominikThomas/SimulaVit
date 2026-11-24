using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private const double MethanotrophyOxygenPerOxidizedCarbon = 2d;
    // Approximate stored organic carbon as CH2O: CH4 + O2 -> CH2O + H2O.
    private const double MethanotrophyOxygenPerStoredCarbon = 1d;
    private const int MethanotrophFounderNodesPerTransportTick = 1024;

    private float methanotrophyAssimilation = 0.15f;
    private double methanotrophyOxidizedPerTick, methanotrophyAssimilatedPerTick, methanotrophyEnergyPerOxidizedCarbon;
    private float hydrogenMethanotrophyChance, methanotrophyMinCH4, methanotrophyMinO2;
    private Vector2 methanotrophyTemperatureRange;
    private float methanotrophyLethalMargin;
    private System.Random methanotrophyMutationRandom, methanotrophFounderRandom;
    private int requestedMethanotrophFounders, pendingMethanotrophFounders, methanotrophFounderSearchNode;
    private long lastMethanotrophFounderTransportTick = -1;

    public void SetMethanotrophyControls(float assimilation, float hydrogenChance, float minimumCH4, float minimumO2,
        Vector2 temperatureRange, float lethalMargin)
    {
        methanotrophyAssimilation = float.IsFinite(assimilation) ? Mathf.Clamp01(assimilation) : 1f;
        hydrogenMethanotrophyChance = availability.Allows(MetabolismType.Methanotrophy) && float.IsFinite(hydrogenChance)
            ? Mathf.Clamp01(hydrogenChance) : 0f;
        methanotrophyMinCH4 = minimumCH4;
        methanotrophyMinO2 = minimumO2;
        methanotrophyTemperatureRange = float.IsFinite(temperatureRange.x) && float.IsFinite(temperatureRange.y)
            ? new Vector2(Mathf.Min(temperatureRange.x, temperatureRange.y), Mathf.Max(temperatureRange.x, temperatureRange.y))
            : new Vector2(float.NaN, float.NaN);
        methanotrophyLethalMargin = float.IsFinite(lethalMargin) ? Mathf.Max(1f, lethalMargin) : float.NaN;
    }

    private void SetMethanotrophyRecipe(float methane, float oxygen, float energy)
    {
        methanotrophyOxidizedPerTick = methanotrophyAssimilatedPerTick = methanotrophyEnergyPerOxidizedCarbon = 0d;
        if (!availability.Allows(MetabolismType.Methanotrophy) || methanotrophyAssimilation >= 1f
            || !float.IsFinite(methane) || !(methane > 0f) || !float.IsFinite(oxygen) || !(oxygen > 0f)
            || !float.IsFinite(energy) || !(energy > 0f)) return;

        double fraction = methanotrophyAssimilation;
        double oxygenPerMethane = (1d - fraction) * MethanotrophyOxygenPerOxidizedCarbon
            + fraction * MethanotrophyOxygenPerStoredCarbon;
        double uptake = Math.Min(methane, oxygen / oxygenPerMethane);
        methanotrophyOxidizedPerTick = uptake * (1d - fraction);
        methanotrophyAssimilatedPerTick = uptake * fraction;
        methanotrophyEnergyPerOxidizedCarbon = (double)energy / methane;
    }

    private void BuildMethanotrophyDemand(ref Request request, float store, float maximumStore, float energy)
    {
        if (!(methanotrophyOxidizedPerTick > 0d) || !double.IsFinite(request.Desired) || !(request.Desired > 0d)
            || !float.IsFinite(store) || store < 0f || !float.IsFinite(maximumStore) || maximumStore < 0f
            || !float.IsFinite(energy) || energy < 0f)
        {
            request.Desired = 0d;
            return;
        }

        double freeStore = Math.Max(0d, (double)maximumStore - store);
        double assimilated = Math.Min(methanotrophyAssimilatedPerTick, freeStore / request.Desired);
        request.ProductCoefficient = methanotrophyOxidizedPerTick;
        request.NeedA = methanotrophyOxidizedPerTick + assimilated;
        request.NeedB = methanotrophyOxidizedPerTick * MethanotrophyOxygenPerOxidizedCarbon
            + assimilated * MethanotrophyOxygenPerStoredCarbon;
    }

    private void TryMutateMethanotrophicChild(Replicator child, MetabolismType parentMetabolism)
    {
        // Legacy uses a separate Hydrogen-specific roll, followed by the local oxygen gate.
        if (parentMetabolism != MetabolismType.Hydrogenotrophy || !(hydrogenMethanotrophyChance > 0f)
            || !(methanotrophyOxidizedPerTick > 0d)) return;
        methanotrophyMutationRandom ??= new System.Random(unchecked(biologySeed ^ 0x4D544D55));
        if (methanotrophyMutationRandom.NextDouble() >= hydrogenMethanotrophyChance
            || !IsMethanotrophicHabitat(child.geodesicCellIndex, child.currentOceanLayerIndex, child.position)) return;

        child.metabolism = MetabolismType.Methanotrophy;
        child.color = ReplicatorManager.MethanotrophRenderColor;
        child.optimalTempMin = methanotrophyTemperatureRange.x;
        child.optimalTempMax = methanotrophyTemperatureRange.y;
        child.lethalTempMargin = methanotrophyLethalMargin;
    }

    private bool IsMethanotrophicHabitat(int cell, int layer, Vector3 position)
    {
        if (!grid.IsNodeActive(cell, layer) || !GeodesicBiologyDiagnostics.FinitePosition(position)
            || !float.IsFinite(methanotrophyMinCH4) || !float.IsFinite(methanotrophyMinO2)) return false;

        int node = grid.GetNodeIndex(cell, layer);
        double volume = grid.PhysicalLayerVolumeKm3[node];
        double methane = resources.GetRawConcentration(GeodesicOceanResource.CH4, node);
        double oxygen = resources.GetRawConcentration(GeodesicOceanResource.O2, node);
        if (!double.IsFinite(volume) || !(volume > 0d)
            || !double.IsFinite(methane) || !(methane > 0d) || methane < methanotrophyMinCH4
            || !double.IsFinite(oxygen) || !(oxygen > 0d) || oxygen < methanotrophyMinO2) return false;

        float temperature = SampleBiologicalTemperature(cell, layer, position).TemperatureKelvin;
        return CalculateTemperaturePerformance(temperature, methanotrophyTemperatureRange.x,
            methanotrophyTemperatureRange.y, methanotrophyLethalMargin, temperatureSettings.DormancyFloor) > 0f;
    }

    public void ConfigureMethanotrophFounders(int requested)
    {
        requestedMethanotrophFounders = Math.Max(0, requested);
        pendingMethanotrophFounders = availability.AllowsMethanotrophFounders ? requestedMethanotrophFounders : 0;
        methanotrophFounderSearchNode = 0;
        lastMethanotrophFounderTransportTick = -1;
        methanotrophFounderRandom = null;
    }

    private void TrySpawnMethanotrophFounders(List<Replicator> agents, ReplicatorPopulationState state, int maximumPopulation)
    {
        int count = Math.Min(pendingMethanotrophFounders, Math.Max(0, maximumPopulation) - agents.Count);
        long transportTick = resources.CompletedTransportTicks;
        if (count <= 0 || !(methanotrophyOxidizedPerTick > 0d) || transportTick == lastMethanotrophFounderTransportTick)
            return;
        lastMethanotrophFounderTransportTick = transportTick;

        // Spread the search over transport ticks even on low-resolution planets.
        int candidates = Math.Min(MethanotrophFounderNodesPerTransportTick, 1 + (grid.NodeCapacity - 1) / 4);
        for (int i = 0; i < candidates; i++)
        {
            int node = methanotrophFounderSearchNode;
            int cell = node / grid.MaximumLayerCount;
            int layer = node % grid.MaximumLayerCount;
            float radius = grid.LayerCenterRadius[node];
            if (float.IsFinite(radius) && radius > 0f)
            {
                Vector3 local = grid.SourceTopology.CellDirections[cell] * radius;
                if (IsMethanotrophicHabitat(cell, layer, planet.transform.TransformPoint(local)))
                {
                    PlaceMethanotrophFounders(count, new FounderPosition { cell = cell, layer = layer, local = local },
                        agents, state);
                    return;
                }
            }
            methanotrophFounderSearchNode = node + 1 == grid.NodeCapacity ? 0 : node + 1;
        }
    }

    private void PlaceMethanotrophFounders(int count, FounderPosition site, List<Replicator> agents,
        ReplicatorPopulationState state)
    {
        methanotrophFounderRandom ??= new System.Random(unchecked(biologySeed ^ 0x4D544653));
        state.EnsureMatchesAgentCount(agents);
        for (int i = 0; i < count; i++)
        {
            int packed = SpawnPassiveFounder(site, methanotrophFounderRandom, agents, state, founderMinLife,
                founderMaxLife, ReplicatorManager.MethanotrophRenderColor, founderBiomassTarget, methanotrophyTemperatureRange,
                methanotrophyLethalMargin, true, MetabolismType.Methanotrophy);
            InitializePassiveSchedulesAtSpawn(state, packed, passiveMovementTime);
        }
        pendingMethanotrophFounders -= count;
        Debug.Log($"[GeodesicMethanotrophFounders] placed={requestedMethanotrophFounders - pendingMethanotrophFounders}/{requestedMethanotrophFounders} remaining={pendingMethanotrophFounders} cell={site.cell} layer={site.layer}");
    }

    private void ClearMethanotrophy()
    {
        methanotrophyAssimilation = 0.15f;
        methanotrophyOxidizedPerTick = methanotrophyAssimilatedPerTick = methanotrophyEnergyPerOxidizedCarbon = 0d;
        hydrogenMethanotrophyChance = 0f;
        methanotrophyMutationRandom = null;
        ConfigureMethanotrophFounders(0);
    }
}
