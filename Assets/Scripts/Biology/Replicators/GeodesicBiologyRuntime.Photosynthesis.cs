using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private float hydrogenPhotosynthesisChance, photosynthesisMinLight, photosynthesisMinCO2, photosynthesisLethalMargin;
    private Vector2 photosynthesisTemperatureRange;
    private System.Random photosynthesisMutationRandom;

    private System.Random PhotosynthesisRandom => photosynthesisMutationRandom ??=
        new System.Random(unchecked(biologySeed ^ 0x50484F54));

    public void SetPhotosynthesisControls(float transitionChance, float minimumLight, float minimumCO2,
        Vector2 temperatureRange, float lethalMargin)
    {
        hydrogenPhotosynthesisChance = availability.Allows(MetabolismType.Photosynthesis) && float.IsFinite(transitionChance)
            ? Mathf.Clamp01(transitionChance) : 0f;
        photosynthesisMinLight = minimumLight;
        photosynthesisMinCO2 = minimumCO2;
        photosynthesisTemperatureRange = temperatureRange;
        photosynthesisLethalMargin = lethalMargin;
    }

    private float GetPhotosyntheticLight(int cell, int layer) => surfaceTemperature != null
        ? ResolvePhotosyntheticLight(surfaceTemperature.GetCellInsolationCosine(cell), layer) : 0f;

    private bool TryMutatePhotosyntheticChild(Replicator child)
    {
        if (hydrogenPhotosynthesisChance <= 0f || !planet.PhotosynthesisUnlocked
            || PhotosynthesisRandom.NextDouble() >= hydrogenPhotosynthesisChance
            || !IsPhotosyntheticHabitat(child.geodesicCellIndex, child.currentOceanLayerIndex, child.position)) return false;

        child.metabolism = MetabolismType.Photosynthesis;
        child.color = Color.green;
        child.optimalTempMin = Mathf.Min(photosynthesisTemperatureRange.x, photosynthesisTemperatureRange.y);
        child.optimalTempMax = Mathf.Max(photosynthesisTemperatureRange.x, photosynthesisTemperatureRange.y);
        child.lethalTempMargin = Mathf.Max(1f, photosynthesisLethalMargin);
        return true;
    }

    private bool IsPhotosyntheticHabitat(int cell, int layer, Vector3 position)
    {
        if (!grid.IsNodeActive(cell, layer)) return false;
        float light = GetPhotosyntheticLight(cell, layer);
        if (!(light > 0f) || !float.IsFinite(photosynthesisMinLight) || light < photosynthesisMinLight
            || !float.IsFinite(photosynthesisMinCO2) || !resources.TryGetConcentration(cell, layer, GeodesicOceanResource.CO2, out float co2)
            || !float.IsFinite(co2) || !(co2 > 0f) || co2 < photosynthesisMinCO2) return false;

        double volume = grid.PhysicalLayerVolumeKm3[grid.GetNodeIndex(cell, layer)];
        if (!(volume > 0d) || !double.IsFinite(volume) || !GeodesicBiologyDiagnostics.FinitePosition(position)
            || !float.IsFinite(photosynthesisTemperatureRange.x) || !float.IsFinite(photosynthesisTemperatureRange.y)
            || !float.IsFinite(photosynthesisLethalMargin)) return false;

        float temperature = SampleBiologicalTemperature(cell, layer, position).TemperatureKelvin;
        float performance = CalculateTemperaturePerformance(temperature,
            Mathf.Min(photosynthesisTemperatureRange.x, photosynthesisTemperatureRange.y),
            Mathf.Max(photosynthesisTemperatureRange.x, photosynthesisTemperatureRange.y),
            Mathf.Max(1f, photosynthesisLethalMargin), temperatureSettings.DormancyFloor);
        return performance > 0f;
    }

    public int SpawnPhotosyntheticFounders(int requested, int maximumPopulation, float co2PerTick,
        float energyPerCO2, float maximumStore, List<Replicator> agents, ReplicatorPopulationState state)
    {
        int count = Math.Min(requested, Math.Max(0, maximumPopulation) - state.Count);
        if (count <= 0 || grid == null || surfaceTemperature == null || !availability.AllowsPhotosyntheticFounders
            || !float.IsFinite(co2PerTick) || !(co2PerTick > 0f) || !float.IsFinite(energyPerCO2) || !(energyPerCO2 > 0f)
            || !float.IsFinite(maximumStore) || !(maximumStore > 0f)) return 0;

        // Startup resets the visible sun while its normal LateUpdate is suspended.
        surfaceTemperature.SynchronizeSunDirection();
        var candidates = new List<FounderPosition>();
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            // Use the shallowest eligible node in each ocean cell.
            for (int layer = 0; layer < 2 && grid.IsNodeActive(cell, layer); layer++)
            {
                int node = grid.GetNodeIndex(cell, layer);
                float radius = grid.LayerCenterRadius[node];
                if (!float.IsFinite(radius) || !(radius > 0f)) continue;
                Vector3 local = grid.SourceTopology.CellDirections[cell] * radius;
                if (!IsPhotosyntheticHabitat(cell, layer, planet.transform.TransformPoint(local))) continue;
                candidates.Add(new FounderPosition { cell = cell, layer = layer, local = local });
                break;
            }
        }
        if (candidates.Count == 0) return 0;

        var random = new System.Random(unchecked(biologySeed ^ 0x50465344));
        int remaining = candidates.Count;
        for (int i = 0; i < count; i++)
        {
            // Visit each site before using it again. The vent founder streams are independent.
            if (remaining == 0) remaining = candidates.Count;
            int selected = random.Next(remaining);
            FounderPosition placement = candidates[selected];
            candidates[selected] = candidates[--remaining];
            candidates[remaining] = placement;
            int packed = SpawnPassiveFounder(placement, random, agents, state, founderMinLife, founderMaxLife,
                Color.green, founderBiomassTarget, photosynthesisTemperatureRange, photosynthesisLethalMargin,
                true, MetabolismType.Photosynthesis);
            InitializePassiveSchedulesAtSpawn(state, packed, passiveMovementTime);
        }
        return count;
    }

    private static double LimitPhotosyntheticExtent(double desired, double need, float store, float maximumStore, float energy)
    {
        if (!double.IsFinite(desired) || !(desired > 0d) || !(need > 0d) || !float.IsFinite(store)
            || store < 0f || !float.IsFinite(maximumStore) || store >= maximumStore
            || !float.IsFinite(energy) || energy < 0f) return 0d;

        return Math.Min(desired, ((double)maximumStore - store) / need);
    }

    private void CommitPhotosynthesis(int index, ReplicatorPopulationState state, float maximumStore,
        float energyPerCO2, bool observeAccounting)
    {
        ref Request r = ref requests[index];
        double carbonBudget = r.NeedA * r.Actual;
        r.Actual = 0d;
        float storeBefore = state.OrganicCStore[index], energyBefore = state.Energy[index];
        if (!resources.TryCommitPhotosynthesis(r.Node, carbonBudget, maximumStore, energyPerCO2,
            storeBefore, energyBefore, out float storeAfter, out float energyAfter,
            out double co2Withdrawn, out double oxygenProduced)) return;

        r.Actual = co2Withdrawn / r.NeedA;
        state.OrganicCStore[index] = storeAfter;
        state.Energy[index] = energyAfter;
        float gained = energyAfter - energyBefore;
        if (chemotaxisABDiagnosticsEnabled) RecordABMetabolicEnergy(state.Locomotion[index], gained);
        extentByMetabolism[(int)r.Metabolism] += r.Actual;
        energyByMetabolism[(int)r.Metabolism] += gained;
        if (!observeAccounting) return;

        r.PhotosyntheticCO2 = co2Withdrawn;
        r.PhotosyntheticO2 = oxygenProduced;
        r.DiagnosticStoreBefore = storeBefore;
        r.DiagnosticStoreAfter = storeAfter;
        r.DiagnosticEnergyDelta = (double)energyAfter - energyBefore;
        double stored = (double)storeAfter - storeBefore;
        accountingDiagnostics.RecordNodeExchange(r.A, -stored, -co2Withdrawn, false);
        accountingDiagnostics.RecordNodeExchange(r.Product, stored, oxygenProduced, false);
    }

    private void RecordPhotosynthesisAccounting(in Request r)
    {
        accountingDiagnostics.RecordReaction(r.Metabolism, r.A, r.B, r.Product,
            r.NeedA * r.Desired, 0d, r.PhotosyntheticCO2, 0d, r.PhotosyntheticO2, 1d, 1d, 1d,
            r.DiagnosticStoreAfter - r.DiagnosticStoreBefore, r.DiagnosticStoreBefore, r.DiagnosticStoreAfter,
            (float)r.DiagnosticEnergyDelta, r.DiagnosticEnergyDelta, r.Actual > 0d, r.NeedA * r.CompetedActual, 0d);
    }
}
