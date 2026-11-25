using System;
using System.Collections.Generic;
using UnityEngine;
using static GeodesicPerformanceMarkers;

[Serializable]
public struct GeodesicPredationSettings
{
    public const int MaximumCandidateBudget = 256;
    public int founderCount;
    public int candidateBudget;
    public float captureProbability;
    public float captureDistance;
    public float captureCooldownSeconds;
    public float energyPerOxidizedCarbon;
    public float evolutionUnlockSeconds;
    public bool evolutionRequiresMotility;
    public bool collectDiagnostics;

    public static GeodesicPredationSettings Default => new GeodesicPredationSettings
    {
        candidateBudget = 24,
        captureProbability = 1f,
        captureDistance = 0.25f,
        captureCooldownSeconds = 1f,
        energyPerOxidizedCarbon = 0.3f,
        evolutionRequiresMotility = true
    };

    public GeodesicPredationSettings Sanitized()
    {
        var value = this;
        value.founderCount = Math.Max(0, founderCount);
        value.candidateBudget = candidateBudget <= 0 ? Default.candidateBudget : Math.Min(candidateBudget, MaximumCandidateBudget);
        value.captureProbability = float.IsFinite(captureProbability) ? Mathf.Clamp01(captureProbability) : Default.captureProbability;
        value.captureDistance = float.IsFinite(captureDistance) ? Mathf.Max(0f, captureDistance) : Default.captureDistance;
        value.captureCooldownSeconds = float.IsFinite(captureCooldownSeconds) ? Mathf.Max(0f, captureCooldownSeconds) : Default.captureCooldownSeconds;
        value.energyPerOxidizedCarbon = float.IsFinite(energyPerOxidizedCarbon)
            ? Mathf.Max(0f, energyPerOxidizedCarbon) : Default.energyPerOxidizedCarbon;
        value.evolutionUnlockSeconds = float.IsFinite(evolutionUnlockSeconds) ? Mathf.Max(0f, evolutionUnlockSeconds) : 0f;
        return value;
    }
}

public struct GeodesicPredationDiagnostics
{
    public long indexBuilds, searches, candidatesExamined, searchesWithoutPrey, captures, failedAttempts;
    public double preyCarbon, assimilatedCarbon, co2Produced, organicCarbonReleased, oxygenConsumed, energyCredited;
}

public sealed partial class GeodesicBiologyRuntime
{
    private const int PredatorFounderChecksPerTick = 32;
    private GeodesicPredationSettings predationSettings;
    private bool predationEnabled, preyIndexReady;
    private float predationAssimilation, predationO2PerC;
    private float predationMutationChance, predationAcquisitionMinO2, predationLethalMargin;
    private Vector2 predationTemperatureRange;
    private GeodesicPreyIndex preyIndex;
    private List<int> predators;
    private System.Random predationCaptureRandom, predationMutationRandom, predationFounderRandom;
    private GeodesicPredationDiagnostics predationDiagnostics;
    private long performancePreySearches, performancePreyCandidates, performanceCaptureAttempts, performancePreyCaptures;
    private int predatorFoundersRequested, predatorFoundersPlaced, predatorFounderCursor;

    public GeodesicPredationDiagnostics PredationDiagnostics => predationDiagnostics;
    public int PredatorFoundersRequested => predatorFoundersRequested;
    public int PredatorFoundersPlaced => predatorFoundersPlaced;

    public void SetPredationControls(GeodesicPredationSettings settings, bool enabled, float assimilation, float oxygenPerC,
        float mutationChance, float acquisitionMinO2, Vector2 temperatureRange, float lethalMargin)
    {
        predationSettings = settings.Sanitized();
        predationEnabled = enabled && availability.Allows(MetabolismType.Predation)
            && predationSettings.captureDistance > 0f && predationSettings.captureProbability > 0f
            && float.IsFinite(assimilation) && assimilation >= 0f && assimilation < 1f
            && predationSettings.energyPerOxidizedCarbon > 0f && float.IsFinite(oxygenPerC) && oxygenPerC > 0f;
        predationAssimilation = assimilation;
        predationO2PerC = oxygenPerC;
        predationMutationChance = float.IsFinite(mutationChance) ? Mathf.Clamp01(mutationChance) : 0f;
        predationAcquisitionMinO2 = acquisitionMinO2;
        predationTemperatureRange = new Vector2(Mathf.Min(temperatureRange.x, temperatureRange.y),
            Mathf.Max(temperatureRange.x, temperatureRange.y));
        predationLethalMargin = Mathf.Max(1f, lethalMargin);
    }

    public void ConfigurePredatorFounders(int requested)
    {
        predatorFoundersRequested = Math.Max(0, requested);
        predatorFoundersPlaced = predatorFounderCursor = 0;
        predationFounderRandom = null;
    }

    private void BeginPredationStep()
    {
        preyIndexReady = false;
        if (predationEnabled) predators?.Clear();
    }

    private void TrackPredator(int slot, ReplicatorPopulationState state, float elapsed)
    {
        if (!predationEnabled || ClassifyLifecycleDeath(state.Energy[slot], state.Age[slot],
            state.MaxLifespan[slot]) != DeathCause.Unknown) return;
        float remaining = state.AttackCooldown[slot] - elapsed;
        state.AttackCooldown[slot] = float.IsFinite(remaining) ? Mathf.Max(0f, remaining) : 0f;
        if (state.AttackCooldown[slot] > 0f) return;
        if (predators == null) predators = new List<int>(Mathf.NextPowerOfTwo(state.Count));
        else if (predators.Count == 0 && predators.Capacity < state.Count)
            predators.Capacity = Mathf.NextPowerOfTwo(state.Count);
        predators.Add(slot);
    }

    private int FindPredationPrey(Vector3 position, int searcher, float movementSeed, List<Replicator> agents,
        ReplicatorPopulationState state, bool sample)
    {
        if (!preyIndexReady)
        {
            using (PredationIndexBuild.Auto())
            {
                preyIndex ??= new GeodesicPreyIndex();
                preyIndex.Build(agents, state, grid, predationSettings.captureDistance);
                preyIndexReady = true;
            }
            if (predationSettings.collectDiagnostics) predationDiagnostics.indexBuilds++;
        }

        uint sampleKey = Hash(MovementSeedBits(movementSeed) ^ unchecked((uint)biologySeed), unchecked((uint)biologySteps));
        int prey, examined;
        using (PredationSearch.Measure(sample))
            prey = preyIndex.FindPrey(position, searcher, sampleKey, predationSettings.candidateBudget,
                agents, state, grid, out examined);
        if (DetailedPerformanceEnabled)
        {
            performancePreySearches++;
            performancePreyCandidates += examined;
        }
        if (predationSettings.collectDiagnostics)
        {
            predationDiagnostics.searches++;
            predationDiagnostics.candidatesExamined += examined;
            if (prey < 0) predationDiagnostics.searchesWithoutPrey++;
        }
        return prey;
    }

    private void RunPredation(List<Replicator> agents, ReplicatorPopulationState state, float maximumStore)
    {
        using var scope = PredationPass.Auto();
        bool observe = predationSettings.collectDiagnostics;
        int sampleOffset = PerformanceSampleOffset;
        for (int j = 0; j < predators.Count; j++)
        {
            int predator = predators[j];
            int cell = state.GeodesicCellIndex[predator], layer = state.CurrentOceanLayerIndex[predator];
            int node = grid.GetNodeIndex(cell, layer);
            if (!(GetPredationOxygen(node) > 0d)) continue;
            float performance = CalculateTemperaturePerformance(GetBiologicalTemperature(predator, state),
                state.OptimalTempMin[predator], state.OptimalTempMax[predator], state.LethalTempMargin[predator], 0f);
            if (!(performance > 0f)) continue;
            bool sample = IsPerformanceSample(predator, sampleOffset);
            int prey = FindPredationPrey(state.Position[predator], predator, state.MovementSeed[predator], agents, state, sample);
            if (prey < 0) continue;

            if (DetailedPerformanceEnabled) performanceCaptureAttempts++;
            using var resolutionProfile = PredationResolution.Measure(sample);
            state.AttackCooldown[predator] = predationSettings.captureCooldownSeconds;
            predationCaptureRandom ??= new System.Random(unchecked(biologySeed ^ 0x50524341));
            float preyCarbon = state.OrganicCStore[prey];
            float store = state.OrganicCStore[predator], energy = state.Energy[predator];
            int preyNode = grid.GetNodeIndex(state.GeodesicCellIndex[prey], state.CurrentOceanLayerIndex[prey]);
            if (predationCaptureRandom.NextDouble() >= predationSettings.captureProbability * performance
                || !resources.TryCommitPredation(node, preyNode, preyCarbon, predationAssimilation,
                    predationO2PerC, predationSettings.energyPerOxidizedCarbon, maximumStore, store, energy, out var transfer))
            {
                if (observe) predationDiagnostics.failedAttempts++;
                continue;
            }

            // Slots are stable through this pass. Resource writes succeeded; mark the prey unavailable immediately.
            preyIndex.MarkConsumed(prey);
            state.OrganicCStore[prey] = agents[prey].organicCStore = 0f;
            state.OrganicCStore[predator] = transfer.storeAfter;
            state.Energy[predator] = transfer.energyAfter;
            double gained = (double)transfer.energyAfter - energy;
            if (chemotaxisABDiagnosticsEnabled) RecordABMetabolicEnergy(state.Locomotion[predator], (float)gained);
            if (DetailedPerformanceEnabled) performancePreyCaptures++;
            if (!observe) continue;
            predationDiagnostics.captures++;
            predationDiagnostics.preyCarbon += preyCarbon;
            predationDiagnostics.assimilatedCarbon += (double)transfer.storeAfter - store;
            predationDiagnostics.co2Produced += transfer.co2Produced;
            predationDiagnostics.organicCarbonReleased += transfer.organicReleased;
            predationDiagnostics.oxygenConsumed += transfer.oxygenConsumed;
            predationDiagnostics.energyCredited += gained;
        }
    }

    private double GetPredationOxygen(int node)
    {
        double oxygen = resources.GetRawConcentration(GeodesicOceanResource.O2, node);
        return double.IsFinite(oxygen) ? oxygen : 0d;
    }

    private bool IsPredatoryHabitat(int cell, int layer, Vector3 position)
    {
        if (!grid.IsNodeActive(cell, layer) || !GeodesicBiologyDiagnostics.FinitePosition(position)
            || !float.IsFinite(predationAcquisitionMinO2)) return false;
        double oxygen = GetPredationOxygen(grid.GetNodeIndex(cell, layer));
        if (!(oxygen > 0d) || oxygen < predationAcquisitionMinO2) return false;
        float temperature = SampleBiologicalTemperature(cell, layer, position).TemperatureKelvin;
        float performance = CalculateTemperaturePerformance(temperature, predationTemperatureRange.x,
            predationTemperatureRange.y, predationLethalMargin, 0f);
        return GeodesicBiologyTemperaturePolicy.CanReproduce(performance, temperatureSettings);
    }

    private void TryMutatePredatoryChild(Replicator child, int parent, ReplicatorPopulationState state, bool splitCarbon, float split)
    {
        if (!predationEnabled || !(predationMutationChance > 0f)
            || state.Metabolism[parent] != MetabolismType.Saprotrophy || child.metabolism != MetabolismType.Saprotrophy
            || !availability.Allows(child.locomotion) || (predationSettings.evolutionRequiresMotility && !IsActive(state.Locomotion[parent]))
            || !planet.SaprotrophyUnlocked || abSimulationTime < predationSettings.evolutionUnlockSeconds) return;

        predationMutationRandom ??= new System.Random(unchecked(biologySeed ^ 0x50524D55));
        if (predationMutationRandom.NextDouble() >= predationMutationChance
            || !GeodesicPreyIndex.IsLivingPrey(parent, state, grid)) return;

        // The known live parent can witness local prey availability without a population query.
        // Require it to retain carbon after division and still be within reach of the placed daughter.
        float parentCarbon = state.OrganicCStore[parent];
        if (splitCarbon && !(parentCarbon - parentCarbon * Mathf.Clamp01(split) > 0f)) return;
        double range = predationSettings.captureDistance;
        if (!GeodesicPreyIndex.WithinRange(child.position, state.Position[parent], range * range)
            || !IsPredatoryHabitat(child.geodesicCellIndex, child.currentOceanLayerIndex, child.position)) return;

        child.metabolism = MetabolismType.Predation;
        child.color = Color.red;
        child.optimalTempMin = predationTemperatureRange.x;
        child.optimalTempMax = predationTemperatureRange.y;
        child.lethalTempMargin = predationLethalMargin;
    }

    private void TrySpawnPredatorFounders(List<Replicator> agents, ReplicatorPopulationState state, int maximumPopulation)
    {
        if (!predationEnabled || predatorFoundersPlaced >= predatorFoundersRequested
            || !availability.AllowsPredatorFounders || state.Count >= maximumPopulation || state.Count == 0) return;

        int population = state.Count;
        predatorFounderCursor %= population;
        int checks = Math.Min(population, PredatorFounderChecksPerTick);
        for (int i = 0; i < checks && state.Count < maximumPopulation && predatorFoundersPlaced < predatorFoundersRequested; i++)
        {
            int prey = predatorFounderCursor;
            predatorFounderCursor = (predatorFounderCursor + 1) % population;
            if (!GeodesicPreyIndex.IsLivingPrey(prey, state, grid)) continue;
            int cell = state.GeodesicCellIndex[prey], layer = state.CurrentOceanLayerIndex[prey];
            Vector3 position = state.Position[prey];
            if (!IsPredatoryHabitat(cell, layer, position)) continue;

            Vector3 local = planet.transform.InverseTransformPoint(position);
            if (!GeodesicBiologyDiagnostics.FinitePosition(local) || !(local.sqrMagnitude > 0f)) continue;
            var site = new FounderPosition { cell = cell, layer = layer, local = local };
            predationFounderRandom ??= new System.Random(unchecked(biologySeed ^ 0x50524644));
            int packed = SpawnPassiveFounder(site, predationFounderRandom, agents, state, founderMinLife, founderMaxLife,
                Color.red, founderBiomassTarget, predationTemperatureRange, predationLethalMargin, true, MetabolismType.Predation);
            state.PassiveVisualRadius[packed] = local.magnitude;
            InitializePassiveSchedulesAtSpawn(state, packed, passiveMovementTime);
            predatorFoundersPlaced++;
            if (predatorFoundersPlaced == predatorFoundersRequested)
                Debug.Log($"[GeodesicPredatorFounders] placed={predatorFoundersPlaced}/{predatorFoundersRequested}");
        }
    }

    private void ClearPredation()
    {
        predationEnabled = preyIndexReady = false;
        preyIndex = null;
        predators = null;
        predationCaptureRandom = predationMutationRandom = null;
        predationDiagnostics = default;
        performancePreySearches = performancePreyCandidates = performanceCaptureAttempts = performancePreyCaptures = 0;
        ConfigurePredatorFounders(0);
    }
}
