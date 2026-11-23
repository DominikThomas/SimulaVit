using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private static readonly ProfilerMarker ActiveMovementMarker = new ProfilerMarker("GeodesicBiology.ActiveMovement");
    private static readonly ProfilerMarker ActiveSensingMarker = new ProfilerMarker("GeodesicBiology.ActiveMovement.Sensing");
    private static readonly ProfilerMarker ActiveVerticalMarker = new ProfilerMarker("GeodesicBiology.ActiveMovement.Vertical");
    private GeodesicLocomotionSettings locomotionSettings = GeodesicLocomotionSettings.Default;
    private ReplicatorSteeringSystem.Settings steeringSettings;
    private float activeMoveSpeed = 4f, amoeboidSpeed = 0.7f, flagellarSpeed = 1.2f;
    private float amoeboidTurn = 0.5f, flagellarTurn = 1.5f;
    private float mutationChance, upgradeChance, anchoredChance, anchoredReleaseChance;
    private bool allowAnchoredRelease;
    private readonly GeodesicLocomotionEvolutionDiagnostics evolutionDiagnostics = new GeodesicLocomotionEvolutionDiagnostics();
    private System.Random locomotionMutationRandom;
    private float movementHydrogenCo2, movementHydrogenH2, movementO2Comfort, movementO2Stress, movementO2Floor;
    private bool movementO2Inhibition;
    private struct MovementHabitatSample { public float h2, h2s, co2, o2, organicC; public double resourceReferenceTicks, sulfurReferenceTicks, fermentationReferenceTicks; }
    private MovementHabitatSample[] movementSamples = Array.Empty<MovementHabitatSample>();
    private int[] movementSampleStamps = Array.Empty<int>();
    private float[] movementSafeAngles = Array.Empty<float>();
    private int movementSampleGeneration = 1;

    // Cumulative counters; population averages are refreshed with the bounded biology report.
    private long mutationAttempts, successfulMutations, blockedMutations, inheritanceCorrections, movementCorrections;
    private readonly long[] locomotionTransitions = new long[16];
    private readonly long[] locomotionBirths = new long[2], locomotionDeaths = new long[2];
    private long suitabilityImproving, suitabilityWorsening, suitabilityNeutral, activeTumbles, worseningTumbles;
    private long completedRuns, verticalTrials, verticalBetter, verticalWorse, verticalNeutral, verticalBlocked;
    private double completedRunSeconds, activeDistanceKm, activeHorizontalKm, activeVerticalKm, locomotionEnergyPaid;
    private long activeCellCrossings, activeLayerTransitions, depthBoundaryRejections, energyLimitedSteps;

#if UNITY_EDITOR
    internal bool CollectActiveMovementDiagnostics { private get; set; }
#endif

    private bool ObserveActiveMovementDiagnostics()
    {
#if UNITY_EDITOR
        if (CollectActiveMovementDiagnostics) return true;
#endif
        return locomotionSettings.logDiagnostics || chemotaxisABDiagnosticsEnabled;
    }

    internal struct MovementWork
    {
        public long PassiveSteps, AmoeboidSteps, FlagellarSteps, AnchoredSteps;
        public long IntegrationSteps, Senses, BoundarySearches, NeighborEvaluations, HabitatObservations;
        public long CellCrossings, VerticalTrials;
        public long ActiveSteps => AmoeboidSteps + FlagellarSteps;

        public void AddSince(in MovementWork current, in MovementWork previous)
        {
            PassiveSteps += Delta(current.PassiveSteps, previous.PassiveSteps);
            AmoeboidSteps += Delta(current.AmoeboidSteps, previous.AmoeboidSteps);
            FlagellarSteps += Delta(current.FlagellarSteps, previous.FlagellarSteps);
            AnchoredSteps += Delta(current.AnchoredSteps, previous.AnchoredSteps);
            IntegrationSteps += Delta(current.IntegrationSteps, previous.IntegrationSteps);
            Senses += Delta(current.Senses, previous.Senses);
            BoundarySearches += Delta(current.BoundarySearches, previous.BoundarySearches);
            NeighborEvaluations += Delta(current.NeighborEvaluations, previous.NeighborEvaluations);
            HabitatObservations += Delta(current.HabitatObservations, previous.HabitatObservations);
            CellCrossings += Delta(current.CellCrossings, previous.CellCrossings);
            VerticalTrials += Delta(current.VerticalTrials, previous.VerticalTrials);
        }

        private static long Delta(long current, long previous) => Math.Max(0, current - previous);
    }

    private MovementWork movementPerformance;

    internal MovementWork MovementPerformance
    {
        get
        {
            MovementWork snapshot = movementPerformance;
            snapshot.CellCrossings = activeCellCrossings;
            snapshot.VerticalTrials = verticalTrials;
            return snapshot;
        }
    }

    public void SetLocomotionSettings(GeodesicLocomotionSettings settings, ReplicatorSteeringSystem.Settings steering,
        float moveSpeed, float amoebaSpeed, float flagellumSpeed, float amoebaTurn, float flagellumTurn,
        float mutation, float upgrade, float anchor, bool releaseAnchor, float releaseChance)
    {
        locomotionSettings = settings.Sanitized();
        steeringSettings = steering;
        activeMoveSpeed = SafeMovementValue(moveSpeed, 4f, 0f, 40f);
        amoeboidSpeed = SafeMovementValue(amoebaSpeed, 0.7f, 0f, 10f);
        flagellarSpeed = SafeMovementValue(flagellumSpeed, 1.2f, 0f, 10f);
        amoeboidTurn = SafeMovementValue(amoebaTurn, 0.5f, 0f, 100f);
        flagellarTurn = SafeMovementValue(flagellumTurn, 1.5f, 0f, 100f);
        mutationChance = SafeMovementValue(mutation, 0.01f, 0f, 1f);
        upgradeChance = SafeMovementValue(upgrade, 0.5f, 0f, 1f);
        anchoredChance = SafeMovementValue(anchor, 0.05f, 0f, 1f);
        allowAnchoredRelease = releaseAnchor;
        anchoredReleaseChance = SafeMovementValue(releaseChance, 0.001f, 0f, 1f);
        evolutionDiagnostics.SetEnabled(locomotionSettings.logDiagnostics || chemotaxisABDiagnosticsEnabled, abSimulationTime);
    }

    public string DescribeResolvedLocomotionControls() => FormattableString.Invariant(
        $"[GeodesicResolvedLocomotion] simTime={abSimulationTime:G9} authority=ReplicatorManager_after_startup_or_load_overrides steeringFlag={chemotacticSteeringEnabled} mode={(chemotacticSteeringEnabled ? "A" : "B")} ABObserverEnabled={chemotaxisABDiagnosticsEnabled} ABOverridesSteering=false foodSignalScope=Hydrogenotrophy_SulfurChemosynthesis_Fermentation metabolismAwareSensing={steeringSettings.ActiveMovementMetabolismSuitabilityEnabled} feedback={(chemotacticSteeringEnabled ? steeringSettings.ActiveMovementMetabolismSuitabilityEnabled ? "localTemporalMetabolicReadiness" : "localTemporalTemperature" : "neutralTumble_randomVerticalPersistence")} mutationOverride={locomotionSettings.mutationRateOverrideEnabled} effectiveMutation={(locomotionSettings.mutationRateOverrideEnabled ? locomotionSettings.diagnosticMutationChance : mutationChance):G9} upgrade={upgradeChance:G9} anchor={anchoredChance:G9} allowAnchoredRelease={allowAnchoredRelease} nestedRelease={anchoredReleaseChance:G9} angularSpeed(A/F)={ActiveAngularSpeed(LocomotionType.Amoeboid):G9}/{ActiveAngularSpeed(LocomotionType.Flagellum):G9} movementEnergyPerSecond(A/F)={locomotionSettings.amoeboidEnergyPerSecond:G9}/{locomotionSettings.flagellumEnergyPerSecond:G9} sensingSeconds(A/F)={steeringSettings.AmoeboidSenseInterval:G9}/{steeringSettings.FlagellumSenseInterval:G9}");

    private static float SafeMovementValue(float value, float fallback, float min, float max) =>
        GeodesicLocomotionSettings.Safe(value, fallback, min, max);
    private static bool IsActive(LocomotionType type) => type == LocomotionType.Amoeboid || type == LocomotionType.Flagellum;

    public void SetDiagnosticLocomotion(int index, LocomotionType type, ReplicatorPopulationState state)
    {
        if (grid == null || index < 0 || index >= state.Count || (uint)type > (uint)LocomotionType.Anchored) return;
        state.Locomotion[index] = type;
        state.ActiveMovement[index] = default;
        // Position and growth state are retained; the next active tick learns from this habitat.
        EnsurePassiveKinematicState(index, state, planet.transform.worldToLocalMatrix);
        if (type == LocomotionType.PassiveDrift) InitializePassiveSchedulesAtSpawn(state, index, passiveMovementTime);
    }

    private void SetHydrogenotrophMovementInputs(float co2, float h2, bool inhibition, float comfort, float stress, float floor)
    {
        movementHydrogenCo2 = Mathf.Max(0f, co2);
        movementHydrogenH2 = Mathf.Max(0f, h2);
        movementO2Inhibition = inhibition;
        movementO2Comfort = comfort; movementO2Stress = stress; movementO2Floor = floor;
    }

    private void BeginMovementSamples()
    {
        if (movementSamples.Length != grid.NodeCapacity)
        {
            movementSamples = new MovementHabitatSample[grid.NodeCapacity];
            movementSampleStamps = new int[grid.NodeCapacity];
            movementSafeAngles = new float[grid.CellCount];
        }
        if (++movementSampleGeneration == int.MaxValue)
        {
            Array.Clear(movementSampleStamps, 0, movementSampleStamps.Length);
            movementSampleGeneration = 1;
        }
    }

    private MovementHabitatSample SampleMovementHabitat(int cell, int layer)
    {
        int node = grid.GetNodeIndex(cell, layer);
        if (movementSampleStamps[node] != movementSampleGeneration)
        {
            resources.ReadTaxisConcentrations(cell, layer, out float h2, out float co2,
                out float o2, out float h2s, out float organicC);
            movementSamples[node] = new MovementHabitatSample
            {
                h2 = SafeMovementValue(h2, 0f, 0f, float.MaxValue),
                h2s = SafeMovementValue(h2s, 0f, 0f, float.MaxValue),
                organicC = SafeMovementValue(organicC,0f,0f,float.MaxValue),
                co2 = SafeMovementValue(co2, 0f, 0f, float.MaxValue),
                o2 = SafeMovementValue(o2, 0f, 0f, float.MaxValue)
            };
            // Biology consumes inventories, not concentrations. The local concentration knee
            // is the configured per-reference-tick need divided by this actual physical volume.
            double volume = grid.PhysicalLayerVolumeKm3[node];
            movementSamples[node].resourceReferenceTicks = GeodesicFoodSignal.HydrogenotrophyTicks(
                movementSamples[node].co2, movementSamples[node].h2, volume, movementHydrogenCo2, movementHydrogenH2);
            movementSamples[node].sulfurReferenceTicks = sulfurActivity > 0f
                ? GeodesicFoodSignal.SulfurChemosynthesisTicks(movementSamples[node].co2,
                    movementSamples[node].h2s, volume, movementSulfurCO2, movementSulfurH2S) : 0d;
            movementSamples[node].fermentationReferenceTicks = GeodesicFoodSignal.FermentationTicks(movementSamples[node].organicC,volume,fermentationNeed);
            movementSampleStamps[node] = movementSampleGeneration;
        }
        return movementSamples[node];
    }

    private float CurrentMovementSuitability(int i, ReplicatorPopulationState state, out float h2, out float performance)
    {
        GeodesicTaxisObservation observation = ObserveMovementHabitat(i, state);
        h2 = observation.h2;
        performance = observation.performance;
        return observation.suitability;
    }

    private GeodesicTaxisObservation ObserveMovementHabitat(int i, ReplicatorPopulationState state)
    {
        int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i];
        var observation = new GeodesicTaxisObservation { cell = cell, layer = layer };
        if (!grid.IsNodeActive(cell, layer)) return observation;
        observation.valid = true;
        observation.temperature = GetBiologicalTemperature(i,state);
        observation.performance = CalculateTemperaturePerformance(observation.temperature, state.OptimalTempMin[i],
            state.OptimalTempMax[i], state.LethalTempMargin[i], temperatureSettings.DormancyFloor);
        MovementHabitatSample sample = SampleMovementHabitat(cell, layer);
        observation.h2 = sample.h2; observation.co2 = sample.co2; observation.o2 = sample.o2;
        // Each future pathway must supply substrates matching its actual geodesic reaction.
        // Do not invent suitability or activate mutations for nonfunctional metabolisms here.
        switch (state.Metabolism[i])
        {
            case MetabolismType.Hydrogenotrophy:
                observation.oxygenEfficiency = movementO2Inhibition
                    ? CalculateAnaerobeO2Efficiency(sample.o2, movementO2Comfort, movementO2Stress, movementO2Floor) : 1f;
                observation.reactionReadiness = GeodesicFoodSignal.Readiness(sample.resourceReferenceTicks,
                    observation.performance, observation.oxygenEfficiency);
                observation.suitability = BoundedReactionReadiness(observation.reactionReadiness);
                break;
            case MetabolismType.SulfurChemosynthesis:
                observation.oxygenEfficiency = sulfurO2Inhibition
                    ? CalculateAnaerobeO2Efficiency(sample.o2, movementO2Comfort, movementO2Stress, sulfurO2Floor) : 1f;
                observation.reactionReadiness = GeodesicFoodSignal.Readiness(sample.sulfurReferenceTicks,
                    observation.performance, observation.oxygenEfficiency);
                observation.suitability = BoundedReactionReadiness(observation.reactionReadiness);
                break;
            case MetabolismType.Fermentation:
                observation.oxygenEfficiency = fermentationO2Inhibition
                    ? CalculateAnaerobeO2Efficiency(sample.o2,movementO2Comfort,movementO2Stress,fermentationO2Floor) : 1f;
                observation.reactionReadiness = GeodesicFoodSignal.Readiness(sample.fermentationReferenceTicks,
                    observation.performance,observation.oxygenEfficiency);
                observation.suitability = BoundedReactionReadiness(observation.reactionReadiness);
                break;
        }
        return observation;
    }

    private static float BoundedReactionReadiness(double readiness)
    {
        if (!(readiness > 0d) || !double.IsFinite(readiness)) return 0f;
        // log1p is approximated near zero to retain tiny but finite observations. No hard
        // substrate saturation; 0 / 0.1 / 1 / 10 ticks map to 0 / .121 / .5 / .776.
        double log = readiness < 1e-4d ? readiness - readiness * readiness * 0.5d : Math.Log(1d + readiness);
        return (float)(log / (0.6931471805599453d + log)); // ln(2)
    }

    private void AccumulateActiveMovementTime(float dt, ReplicatorPopulationState state)
    {
        int passive = 0, amoeboid = 0, flagellar = 0, anchored = 0;
        for (int i = 0; i < state.Count; i++)
        {
            if ((uint)state.Locomotion[i] > (uint)LocomotionType.Anchored)
            {
                state.Locomotion[i] = LocomotionType.PassiveDrift;
                state.ActiveMovement[i] = default;
                inheritanceCorrections++;
            }
            switch (state.Locomotion[i])
            {
                case LocomotionType.PassiveDrift: passive++; continue;
                case LocomotionType.Anchored: anchored++; continue;
                case LocomotionType.Amoeboid: amoeboid++; break;
                case LocomotionType.Flagellum: flagellar++; break;
            }
            state.ActiveMovement[i].pendingSeconds = SafeMovementValue(state.ActiveMovement[i].pendingSeconds, 0f, 0f, 60f) + dt;
        }
        movementPerformance.PassiveSteps += passive;
        movementPerformance.AmoeboidSteps += amoeboid;
        movementPerformance.FlagellarSteps += flagellar;
        movementPerformance.AnchoredSteps += anchored;
    }

    private float ActiveAngularSpeed(LocomotionType type) => PassiveAngularSpeedRadiansPerSecond
        * locomotionSettings.activeSpeedMultiplier * (activeMoveSpeed / 4f)
        * (type == LocomotionType.Flagellum ? flagellarSpeed : amoeboidSpeed);

    private void RunActiveMovement(float elapsed, List<Replicator> agents, ReplicatorPopulationState state, Matrix4x4 worldToLocal, Matrix4x4 localToWorld)
    {
        bool samplesReady = false;
        bool observeMovement = ObserveActiveMovementDiagnostics();
        BeginDetailedTaxisBatch();
        for (int i = 0; i < state.Count; i++)
        {
            LocomotionType type = state.Locomotion[i];
            if (!IsActive(type)) continue;
            int trackedSlot = locomotionSettings.detailedTaxisDiagnostics ? TrackTaxisOrganism(agents[i].DiagnosticId, type) : -1;
            if (!samplesReady) { BeginMovementSamples(); samplesReady = true; }
            if (!EnsureActiveHabitat(i, state)) continue;
            int cell = state.GeodesicCellIndex[i];
            EnsurePassiveKinematicState(i, state, worldToLocal);
            ref GeodesicActiveMovementState active = ref state.ActiveMovement[i];
            if (!ValidActiveMemory(active))
            {
                float pending = SafeMovementValue(active.pendingSeconds, 0f, 0f, 60f);
                active = new GeodesicActiveMovementState { pendingSeconds = pending };
                movementCorrections++;
            }
            float remaining = Mathf.Min(elapsed, active.pendingSeconds);
            if (!active.initialized)
            {
                // Never reconstruct from the cell centre or a parent's transport state.
                active.initialized = true;
                active.desiredTangentLocal = state.PassiveDriftTangent[i];
                active.verticalCountdown = locomotionSettings.verticalExplorationIntervalSeconds * (0.5f + NextActiveRandom(i, state));
                InitializeActiveDepth(i, state);
            }
            EnsureTaxisMemoryModel(ref active);
            float speed = ActiveAngularSpeed(type);
            // Bounded catch-up; never enlarge a geometric step to absorb excess elapsed time.
            // Normal pipeline ticks are <= 1/30s and movement batches are 0.1s.
            int step = 0;
            for (; remaining > 1e-6f && step < MaximumKinematicCatchUpSteps; step++)
            {
                cell = state.GeodesicCellIndex[i];
                if (!(movementSafeAngles[cell] > 0f))
                    movementSafeAngles[cell] = Mathf.Max(1e-7f, GetMinimumNeighborSpacingRadians(cell) * 0.2f);
                float stepDt = Mathf.Min(remaining, PassiveKinematicsIntervalSeconds);
                if (speed > 0f) stepDt = Mathf.Min(stepDt, movementSafeAngles[cell] / speed);
                bool canReuseObservation;
                using (ActiveSensingMarker.Auto()) canReuseObservation = SenseActiveHabitat(i, state, stepDt, trackedSlot, observeMovement);
                using (ActiveVerticalMarker.Auto()) PrepareVerticalExploration(i, state, stepDt, canReuseObservation);
                float rate = type == LocomotionType.Flagellum ? locomotionSettings.flagellumEnergyPerSecond : locomotionSettings.amoeboidEnergyPerSecond;
                bool hasPropulsion = speed > 0f || (active.verticalMoving && locomotionSettings.verticalSpeedMetersPerSecond > 0f);
                float energy = state.Energy[i];
                if (!float.IsFinite(energy)) { movementCorrections++; energy = state.Energy[i] = 0f; }
                float paid = hasPropulsion ? Mathf.Min(Mathf.Max(0f, energy), rate * stepDt) : 0f;
                float effortDt = !hasPropulsion || energy <= 0f ? 0f : rate > 0f ? paid / rate : stepDt;
                if (hasPropulsion && effortDt + 1e-6f < stepDt) energyLimitedSteps++;
                state.Energy[i] -= paid;
                if (observeMovement) locomotionEnergyPaid += paid;
                active.runSeconds += effortDt;
                float effortShare = active.verticalMoving ? 0.70710678f : 1f;
                Vector3 direction = state.PassiveDriftDirection[i], tangent = state.PassiveDriftTangent[i];
                Vector3 desired = Vector3.ProjectOnPlane(active.desiredTangentLocal, direction);
                if (desired.sqrMagnitude < 1e-10f || !GeodesicBiologyDiagnostics.FinitePosition(desired))
                {
                    desired = tangent;
                    active.desiredTangentLocal = tangent;
                    movementCorrections++;
                }
                float turnRate = type == LocomotionType.Flagellum ? flagellarTurn : amoeboidTurn;
                Vector3 unitDesired = desired.normalized;
                float desiredAngle = Mathf.Atan2(Vector3.Dot(direction, Vector3.Cross(tangent, unitDesired)),
                    Mathf.Clamp(Vector3.Dot(tangent, unitDesired), -1f, 1f));
                float turn = Mathf.Clamp(desiredAngle, -turnRate * effortDt, turnRate * effortDt);
                tangent = (tangent * Mathf.Cos(turn) + Vector3.Cross(direction, tangent) * Mathf.Sin(turn)).normalized;
                if (type == LocomotionType.Amoeboid)
                {
                    float noise = Mathf.Sin((state.Age[i] + state.MovementSeed[i] * 100f + elapsed - remaining) * 2.7f);
                    float angle = noise * SafeMovementValue(steeringSettings.AmoeboidRunNoiseStrength, 0.08f, 0f, 1f)
                        * SafeMovementValue(steeringSettings.AmoeboidTurnAngleMax, 60f, 0f, 180f) * effortDt;
                    tangent = Quaternion.AngleAxis(angle, direction) * tangent;
                }
                Vector3 before = direction;
                float travel = speed * effortDt * effortShare;
                direction = (before * Mathf.Cos(travel) + tangent * Mathf.Sin(travel)).normalized;
                tangent = Vector3.ProjectOnPlane(tangent, direction).normalized;
                TraverseMovementBoundaries(i, state, ref direction, ref tangent, true);
                double distance = 0d;
                if (observeMovement)
                {
                    // Cross-product atan2 resolves very small angles accurately.
                    float actualAngle = Mathf.Atan2(Vector3.Cross(before, direction).magnitude,
                        Mathf.Clamp(Vector3.Dot(before, direction), -1f, 1f));
                    double physicalRadiusKm = grid.PhysicalOceanSurfaceRadiusKm
                        - grid.ToPhysicalDepthKm((double)grid.OceanSurfaceRadius - state.PassiveVisualRadius[i]);
                    distance = actualAngle * physicalRadiusKm;
                    activeHorizontalKm += distance;
                }
                state.PassiveDriftDirection[i] = direction;
                state.PassiveDriftTangent[i] = tangent;
                // Parallel-transport the desired run; reflected coast headings become the new run.
                active.desiredTangentLocal = Vector3.ProjectOnPlane(active.desiredTangentLocal, direction).normalized;
                double verticalDistance;
                using (ActiveVerticalMarker.Auto()) verticalDistance = AdvanceActiveVertical(i, state, effortDt * effortShare, observeMovement);
                if (observeMovement) activeDistanceKm += Math.Sqrt(distance * distance + verticalDistance * verticalDistance);
                active.pendingSeconds = Mathf.Max(0f, active.pendingSeconds - stepDt);
                remaining -= stepDt;
                movementUpdates++;
                continuousKinematicUpdates++;
            }
            movementPerformance.IntegrationSteps += step;
            Vector3 normal = state.PassiveDriftDirection[i];
            Vector3 worldNormal = localToWorld.MultiplyVector(normal).normalized;
            Vector3 worldTangent = localToWorld.MultiplyVector(state.PassiveDriftTangent[i]).normalized;
            state.Position[i] = localToWorld.MultiplyPoint3x4(normal * state.PassiveVisualRadius[i]);
            state.CurrentDirection[i] = worldNormal;
            state.MoveDirection[i] = state.DesiredMoveDirection[i] = worldTangent;
            state.Rotation[i] = Quaternion.LookRotation(worldTangent, worldNormal);
            state.PreferredOceanLayerIndex[i] = state.CurrentOceanLayerIndex[i];
        }
        EndDetailedTaxisBatch();
    }

    private static bool ValidActiveMemory(in GeodesicActiveMovementState active) =>
        float.IsFinite(active.pendingSeconds) && active.pendingSeconds >= 0f
        && float.IsFinite(active.senseCountdown) && float.IsFinite(active.lastSenseInterval) && active.lastSenseInterval >= 0f
        && float.IsFinite(active.suitabilityMemory) && active.suitabilityMemory >= 0f && active.suitabilityMemory <= 1f
        && float.IsFinite(active.runSeconds) && active.runSeconds >= 0f && float.IsFinite(active.verticalCountdown)
        && double.IsFinite(active.reactionReadinessMemory) && active.reactionReadinessMemory >= 0d
        && active.cellCrossingsSinceSense >= 0
        && float.IsFinite(active.verticalBaseline) && active.verticalBaseline >= 0f && active.verticalBaseline <= 1f
        && GeodesicBiologyDiagnostics.FinitePosition(active.desiredTangentLocal)
        && active.verticalDirection >= -1 && active.verticalDirection <= 1;

    private void InitializeActiveDepth(int i, ReplicatorPopulationState state)
    {
        int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i];
        float radius = state.PassiveVisualRadius[i];
        int node = grid.GetNodeIndex(cell, layer);
        if (float.IsFinite(radius) && radius > 0f)
        {
            if (radius >= grid.LayerInnerRadius[node] && radius <= grid.LayerOuterRadius[node]) return;
            // A passive parent can be partway through its visual depth interpolation at birth.
            // Start the active child in the layer containing that actual radius; never snap it
            // to the inherited logical layer or grant free travel to its centre.
            for (int candidate = 0; candidate < grid.ActiveLayerCountByCell[cell]; candidate++)
            {
                int candidateNode = grid.GetNodeIndex(cell, candidate);
                if (radius < grid.LayerInnerRadius[candidateNode] || radius > grid.LayerOuterRadius[candidateNode]) continue;
                state.CurrentOceanLayerIndex[i] = candidate;
                movementCorrections++;
                return;
            }
        }
        // Only a genuinely invalid/out-of-ocean position needs a bounded radius repair.
        state.PassiveVisualRadius[i] = float.IsFinite(radius) && radius > 0f
            ? Mathf.Clamp(radius, grid.LayerInnerRadius[node], grid.LayerOuterRadius[node]) : grid.LayerCenterRadius[node];
        movementCorrections++;
    }

    private bool EnsureActiveHabitat(int i, ReplicatorPopulationState state)
    {
        int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i];
        if (grid.IsNodeActive(cell, layer)) return true;
        if (cell < 0 || cell >= grid.CellCount || grid.ActiveLayerCountByCell[cell] == 0)
        {
            // No distant relocation to repair invalid geography. Leave it to the existing invalid-habitat lifecycle.
            invalidHabitats++;
            return false;
        }
        // Repair only an invalid layer in the same ocean column, choosing the layer containing its actual depth.
        float radius = planet.transform.InverseTransformPoint(state.Position[i]).magnitude;
        int resolved = grid.ActiveLayerCountByCell[cell] - 1;
        for (int candidate = 0; candidate < grid.ActiveLayerCountByCell[cell]; candidate++)
        {
            int node = grid.GetNodeIndex(cell, candidate);
            if (radius >= grid.LayerInnerRadius[node] && radius <= grid.LayerOuterRadius[node]) { resolved = candidate; break; }
        }
        state.CurrentOceanLayerIndex[i] = resolved;
        state.ActiveMovement[i] = default;
        movementCorrections++;
        invalidLayerCorrections++;
        return true;
    }

    private float NextActiveRandom(int i, ReplicatorPopulationState state) =>
        ToUnitFloat(Hash(MovementSeedBits(state.MovementSeed[i]) ^ 0xAC714E5u, state.ActiveMovement[i].sequence++));

    private bool SenseActiveHabitat(int i, ReplicatorPopulationState state, float dt, int trackedSlot, bool observeMovement)
    {
        ref GeodesicActiveMovementState active = ref state.ActiveMovement[i];
        active.senseCountdown -= dt;
        active.lastSenseInterval += dt;
        if (active.senseCountdown > 0f) return false;
        movementPerformance.Senses++;
        bool canReuseObservation = true;
#if UNITY_EDITOR
        canReuseObservation = DiagnosticSteeringObservationOverride == null;
#endif
        bool flagellar = state.Locomotion[i] == LocomotionType.Flagellum;
        GeodesicTaxisObservation observation = ObserveSteeringHabitat(i, state);
        bool hadMemory = active.hasSuitability;
        float previousSuitability = active.suitabilityMemory;
        double previousReadiness = active.reactionReadinessMemory;
        TaxisComparison comparison = hadMemory
            ? CompareTaxis(observation.reactionReadiness, previousReadiness, observation.suitability, previousSuitability)
            : CompareTaxis(observation.reactionReadiness, observation.reactionReadiness, observation.suitability, observation.suitability);
        bool worse = hadMemory && comparison.trend == TaxisTrend.Worsening;
        float probability = GeodesicTaxisPolicy.ReferenceTumbleProbability(chemotacticSteeringEnabled,
            hadMemory, (GeodesicTaxisPolicy.Trend)comparison.trend, steeringSettings.BaseTumbleProbability,
            steeringSettings.MovementTumbleChanceWhenBetter, steeringSettings.MovementTumbleChanceWhenWorse,
            steeringSettings.MovementTumbleChanceNeutral);
        bool reportTaxis = locomotionSettings.logDiagnostics;
        bool unchangedInputs = (reportTaxis || chemotaxisABDiagnosticsEnabled) && hadMemory
            && SameExperiencedInputs(observation, active.lastTaxisObservation);
        if (observeMovement && hadMemory)
        {
            if (comparison.trend == TaxisTrend.Improving) suitabilityImproving++;
            else if (worse) suitabilityWorsening++;
            else suitabilityNeutral++;
            if (reportTaxis)
                RecordHorizontalTaxis(observation, active.lastTaxisObservation, comparison,
                    active.cellCrossingsSinceSense, active.lastSenseInterval, unchangedInputs);
        }
        float min = SafeMovementValue(steeringSettings.MinTumbleProbability, 0.02f, 0f, 1f);
        float max = SafeMovementValue(steeringSettings.MaxTumbleProbability, 0.9f, min, 1f);
        probability = SafeMovementValue(probability, 0.2f, min, max);
        // The same per-reference-time hazard at different sensing cadences. Flagella have longer runs.
        float hazardTime = active.lastSenseInterval / locomotionSettings.tumbleReferenceSeconds
            * (flagellar ? locomotionSettings.flagellumTumbleMultiplier : 1f);
        float tumble = 1f - Mathf.Pow(1f - probability, hazardTime);
        state.TumbleProbability[i] = tumble;
        bool tumbled = hadMemory && NextActiveRandom(i, state) < tumble;
        if (tumbled)
        {
            float maxAngle = flagellar ? steeringSettings.FlagellumTurnAngleMax : steeringSettings.AmoeboidTurnAngleMax;
            float angle = (NextActiveRandom(i, state) * 2f - 1f) * SafeMovementValue(maxAngle, flagellar ? 180f : 60f, 0f, 180f);
            active.desiredTangentLocal = Quaternion.AngleAxis(angle, state.PassiveDriftDirection[i]) * state.PassiveDriftTangent[i];
            activeTumbles++;
            if (worse) worseningTumbles++;
            completedRunSeconds += active.runSeconds;
            completedRuns++;
            active.runSeconds = 0f;
        }
        if (reportTaxis && hadMemory) RecordTaxisTumble(comparison.trend, tumble, tumbled);
        if (chemotaxisABDiagnosticsEnabled)
            RecordABChemotaxisComparison(observation, active.lastTaxisObservation, comparison,
                state.Metabolism[i], hadMemory, tumble, tumbled, active.cellCrossingsSinceSense, unchangedInputs);
        if (trackedSlot >= 0)
            RecordDetailedTaxisSense(trackedSlot, observation, previousSuitability, previousReadiness,
                comparison, probability, tumble, tumbled, !hadMemory, active.cellCrossingsSinceSense, active.lastSenseInterval);
        double blend = SafeMovementValue(steeringSettings.MovementSuitabilityMemoryBlend, 0.35f, 0f, 1f);
        active.reactionReadinessMemory = hadMemory
            ? previousReadiness + (observation.reactionReadiness - previousReadiness) * blend : observation.reactionReadiness;
        active.suitabilityMemory = SteeringScore(active.reactionReadinessMemory);
        active.hasSuitability = true;
        active.lastTaxisObservation = observation;
        active.cellCrossingsSinceSense = 0;
        state.LastHabitatValue[i] = active.suitabilityMemory;
        if (active.verticalObservationPending && !active.verticalMoving)
        {
            bool paired = active.verticalOriginRecorded
                && grid.IsNodeActive(active.verticalOriginCell, active.verticalOriginLayer)
                && grid.IsNodeActive(active.verticalArrivalCell, active.verticalArrivalLayer)
                && Math.Abs(active.verticalArrivalLayer - active.verticalOriginLayer) == 1
                && active.verticalArrivalLayer == active.verticalTargetLayer
                && double.IsFinite(active.verticalReadinessBefore) && active.verticalReadinessBefore >= 0d
                && double.IsFinite(active.verticalReadinessAfter) && active.verticalReadinessAfter >= 0d
                && float.IsFinite(active.verticalArrivalSuitability);
            TaxisComparison vertical = CompareTaxis(active.verticalReadinessAfter, active.verticalReadinessBefore,
                active.verticalArrivalSuitability, active.verticalBaseline);
            int trialDirection = active.verticalDirection;
            if (!paired) verticalUnpaired++;
            else if (vertical.trend == TaxisTrend.Improving)
            {
                verticalBetter++;
                if (chemotacticSteeringEnabled)
                {
                    if (NextActiveRandom(i, state) >= locomotionSettings.verticalContinueWhenBetter) active.verticalDirection *= -1;
                }
                else if (NextActiveRandom(i, state) < 0.5f) active.verticalDirection *= -1;
            }
            else if (vertical.trend == TaxisTrend.Worsening)
            {
                verticalWorse++;
                if (NextActiveRandom(i, state) < (chemotacticSteeringEnabled
                    ? locomotionSettings.verticalReverseWhenWorse : 0.5f)) active.verticalDirection *= -1;
            }
            else if (paired)
            {
                verticalNeutral++;
                if (NextActiveRandom(i, state) < 0.5f) active.verticalDirection *= -1;
            }
            if (reportTaxis && paired) verticalDeltaStats.Observe(vertical);
            if (trackedSlot >= 0) RecordDetailedVerticalTaxis(trackedSlot, active, vertical, trialDirection, paired);
            active.verticalObservationPending = false;
            active.verticalOriginRecorded = false;
        }
        float interval = flagellar ? steeringSettings.FlagellumSenseInterval : steeringSettings.AmoeboidSenseInterval;
        float jitter = flagellar ? steeringSettings.FlagellumSenseIntervalJitter : steeringSettings.AmoeboidSenseIntervalJitter;
        interval = SafeMovementValue(interval, flagellar ? 0.2f : 0.5f, 0.1f, 60f);
        jitter = SafeMovementValue(jitter, 0f, 0f, interval * 0.9f);
        active.senseCountdown = Mathf.Max(0.1f, interval + (NextActiveRandom(i, state) * 2f - 1f) * jitter);
        active.lastSenseInterval = 0f;
        return canReuseObservation;
    }

    private void PrepareVerticalExploration(int i, ReplicatorPopulationState state, float dt, bool canReuseObservation)
    {
        ref GeodesicActiveMovementState active = ref state.ActiveMovement[i];
        active.verticalCountdown -= dt;
        if (active.verticalMoving || active.verticalObservationPending || active.verticalCountdown > 0f
            || locomotionSettings.verticalSpeedMetersPerSecond <= 0f) return;
        int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i];
        if (active.verticalDirection != -1 && active.verticalDirection != 1)
            active.verticalDirection = NextActiveRandom(i, state) < 0.5f ? -1 : 1;
        int target = ResolveAdjacentVerticalLayer(layer, active.verticalDirection, grid.ActiveLayerCountByCell[cell]);
        if (target == layer)
        {
            active.verticalDirection *= -1;
            target = ResolveAdjacentVerticalLayer(layer, active.verticalDirection, grid.ActiveLayerCountByCell[cell]);
        }
        active.verticalCountdown = locomotionSettings.verticalExplorationIntervalSeconds * (0.5f + NextActiveRandom(i, state));
        if (target == layer || !grid.IsNodeActive(cell, target)) { verticalBlocked++; return; }
        // Sensing and trial preparation see the same habitat before any movement.
        // Arrival always samples at its original point in the integration.
        GeodesicTaxisObservation origin = canReuseObservation ? active.lastTaxisObservation : ObserveSteeringHabitat(i, state);
        active.verticalBaseline = origin.suitability;
        active.verticalReadinessBefore = origin.reactionReadiness;
        active.verticalOriginCell = cell;
        active.verticalOriginLayer = layer;
        active.verticalOriginRecorded = origin.valid;
        active.verticalTargetLayer = target;
        active.verticalMoving = true;
        verticalTrials++;
    }

    private double AdvanceActiveVertical(int i, ReplicatorPopulationState state, float effortDt, bool observeMovement)
    {
        ref GeodesicActiveMovementState active = ref state.ActiveMovement[i];
        if (!active.verticalMoving || effortDt <= 0f) return 0d;
        int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i], target = active.verticalTargetLayer;
        if (!grid.IsNodeActive(cell, target) || Mathf.Abs(target - layer) > 1)
        {
            active.verticalMoving = false;
            verticalBlocked++;
            movementCorrections++;
            return 0d;
        }
        float speed = locomotionSettings.verticalSpeedMetersPerSecond
            * (state.Locomotion[i] == LocomotionType.Flagellum ? flagellarSpeed : amoeboidSpeed);
        float radius = state.PassiveVisualRadius[i];
        float targetRadius = grid.LayerCenterRadius[grid.GetNodeIndex(cell, target)];
        double physicalMetresPerUnit = grid.ToPhysicalDepthKm(1d) * 1000d;
        float nextRadius = Mathf.MoveTowards(radius, targetRadius, (float)(speed * effortDt / physicalMetresPerUnit));
        double distanceKm = 0d;
        if (observeMovement)
        {
            distanceKm = grid.ToPhysicalDepthKm(Math.Abs((double)nextRadius - radius));
            activeVerticalKm += distanceKm;
        }
        state.PassiveVisualRadius[i] = nextRadius;
        int node = grid.GetNodeIndex(cell, layer);
        if (target != layer && ((target > layer && nextRadius <= grid.LayerInnerRadius[node])
            || (target < layer && nextRadius >= grid.LayerOuterRadius[node])))
        {
            state.CurrentOceanLayerIndex[i] = target;
            verticalTransitions++;
            activeLayerTransitions++;
        }
        if (Mathf.Abs(nextRadius - targetRadius) < 1e-7f)
        {
            active.verticalMoving = false;
            active.verticalObservationPending = true;
            // Freeze the actual arrival experience now, before any later horizontal motion.
            GeodesicTaxisObservation arrival = ObserveSteeringHabitat(i, state);
            active.verticalArrivalSuitability = arrival.suitability;
            active.verticalReadinessAfter = arrival.reactionReadiness;
            active.verticalArrivalCell = arrival.cell;
            active.verticalArrivalLayer = arrival.layer;
            active.verticalOriginRecorded &= arrival.valid;
            active.verticalCountdown = locomotionSettings.verticalExplorationIntervalSeconds * (0.5f + NextActiveRandom(i, state));
            // The captured origin/arrival pair is classified at the next normal sensing event.
        }
        return distanceKm;
    }

    /// <summary>Shared neighbour/Voronoi/coast handling. Passive mapping is unchanged; swimmers retain physical depth.</summary>
    private void TraverseMovementBoundaries(int i, ReplicatorPopulationState state, ref Vector3 direction, ref Vector3 tangent, bool active)
    {
        GeodesicGridTopology topology = grid.SourceTopology;
        Vector3[] centers = topology.CellDirections;
        int cell = state.GeodesicCellIndex[i];
        for (int crossing = 0; crossing < MaximumBoundaryCrossingsPerStep; crossing++)
        {
            if (active)
            {
                movementPerformance.BoundarySearches++;
                movementPerformance.NeighborEvaluations += topology.NeighborCounts[cell];
            }
            int candidate;
            using (MovementBoundaryCandidates.Auto()) candidate = FindCloserRealNeighbor(cell, direction, centers, topology.Neighbors6, topology.NeighborCounts);
            if (candidate < 0) break;
            int layers = grid.ActiveLayerCountByCell[candidate];
            bool blocked = layers <= 0;
            if (active && !blocked)
            {
                int layer = state.CurrentOceanLayerIndex[i];
                float radius = state.PassiveVisualRadius[i];
                int node = grid.GetNodeIndex(candidate, layer);
                blocked = !grid.IsNodeActive(candidate, layer)
                    || radius < grid.LayerInnerRadius[node] || radius > grid.LayerOuterRadius[node]
                    || (state.ActiveMovement[i].verticalMoving && !grid.IsNodeActive(candidate, state.ActiveMovement[i].verticalTargetLayer));
                if (blocked) depthBoundaryRejections++;
            }
            if (blocked)
            {
                if (layers <= 0) landBoundaryRejections++;
                using (MovementLandHandling.Auto()) ReflectFromLandBoundary(ref direction, ref tangent, centers[cell], centers[candidate]);
                if (active) state.ActiveMovement[i].desiredTangentLocal = tangent;
                break;
            }
            using (MovementBoundaryResolution.Auto())
            {
                if (!active)
                {
                    int mappedLayer = ResolveHorizontalTargetLayer(state.CurrentOceanLayerIndex[i], layers);
                    if (mappedLayer != state.CurrentOceanLayerIndex[i]) invalidLayerCorrections++;
                    state.CurrentOceanLayerIndex[i] = mappedLayer;
                }
                cell = candidate;
                state.GeodesicCellIndex[i] = cell;
                horizontalBoundaryCrossings++;
                if (active)
                {
                    activeCellCrossings++;
                    if (state.ActiveMovement[i].cellCrossingsSinceSense < int.MaxValue)
                        state.ActiveMovement[i].cellCrossingsSinceSense++;
                }
            }
        }
    }

    private LocomotionType ResolveGeodesicInheritedLocomotion(LocomotionType source, int cell, int layer,
        out GeodesicLocomotionMutationPolicy.Result outcome)
    {
        var settings = new GeodesicLocomotionMutationPolicy.Settings
        {
            mutation = mutationChance, upgrade = upgradeChance, anchor = anchoredChance,
            release = anchoredReleaseChance, allowRelease = allowAnchoredRelease,
            overrideMutation = locomotionSettings.mutationRateOverrideEnabled,
            diagnosticMutation = locomotionSettings.diagnosticMutationChance
        };
        bool bottom = grid != null && grid.IsNodeActive(cell, layer)
            && layer == grid.ActiveLayerCountByCell[cell] - 1;
        outcome = GeodesicLocomotionMutationPolicy.Sample(source, bottom, settings, locomotionMutationRandom);
        if (outcome.correctedInheritance) inheritanceCorrections++;
        if (outcome.attempted)
        {
            mutationAttempts++;
            if (!outcome.Changed) blockedMutations++;
            else { successfulMutations++; locomotionTransitions[(int)outcome.source * 4 + (int)outcome.target]++; }
        }
        return outcome.target;
    }

    private void RecordLocomotionBirth(MetabolismType metabolism, LocomotionType parent)
    {
        if (metabolism == MetabolismType.Hydrogenotrophy && parent != LocomotionType.Anchored)
            locomotionBirths[IsActive(parent) ? 1 : 0]++;
    }
    private void RecordLocomotionDeath(MetabolismType metabolism, LocomotionType type)
    {
        if (metabolism == MetabolismType.Hydrogenotrophy && type != LocomotionType.Anchored)
            locomotionDeaths[IsActive(type) ? 1 : 0]++;
    }

    private void LogLocomotionDiagnostics(ReplicatorPopulationState state)
    {
        if (!locomotionSettings.logDiagnostics) return;
        BeginMovementSamples();
        int passive = 0, amoeba = 0, flagellar = 0, anchored = 0, passiveH = 0, activeH = 0;
        double passiveH2 = 0d, activeH2 = 0d, passivePerformance = 0d, activePerformance = 0d;
        double passiveSuitability = 0d, activeSuitability = 0d, passiveEnergy = 0d, activeEnergy = 0d;
        for (int i = 0; i < state.Count; i++)
        {
            LocomotionType type = state.Locomotion[i];
            switch (type)
            {
                case LocomotionType.PassiveDrift: passive++; break;
                case LocomotionType.Amoeboid: amoeba++; break;
                case LocomotionType.Flagellum: flagellar++; break;
                case LocomotionType.Anchored: anchored++; break;
            }
            if (state.Metabolism[i] != MetabolismType.Hydrogenotrophy || type == LocomotionType.Anchored) continue;
            float score = CurrentMovementSuitability(i, state, out float h2, out float performance);
            if (IsActive(type))
            {
                activeH++; activeH2 += h2; activePerformance += performance;
                activeSuitability += score; activeEnergy += state.Energy[i];
            }
            else
            {
                passiveH++; passiveH2 += h2; passivePerformance += performance;
                passiveSuitability += score; passiveEnergy += state.Energy[i];
            }
        }
        double p = Math.Max(1, passiveH), a = Math.Max(1, activeH);
        Debug.Log($"[GeodesicLocomotion] cumulativeCounters=true population(passive/amoeboid/flagellum/anchored)={passive}/{amoeba}/{flagellar}/{anchored} mutationAttempts={mutationAttempts} successful={successfulMutations} blockedOrNoChange={blockedMutations} inheritanceCorrections={inheritanceCorrections} movementCorrections={movementCorrections} activeDistanceKm={activeDistanceKm:G6} horizontalKm={activeHorizontalKm:G6} verticalKm={activeVerticalKm:G6} activeCellCrossings={activeCellCrossings} activeLayerTransitions={activeLayerTransitions} depthBoundaryRejections={depthBoundaryRejections} energySpent={locomotionEnergyPaid:G6} energyLimitedSteps={energyLimitedSteps} improving/worsening/neutral={suitabilityImproving}/{suitabilityWorsening}/{suitabilityNeutral} tumbles={activeTumbles} worseningTumbles={worseningTumbles} completedMeanRunSeconds={completedRunSeconds / Math.Max(1, completedRuns):G6} verticalTrials/better/worse/neutral/blocked={verticalTrials}/{verticalBetter}/{verticalWorse}/{verticalNeutral}/{verticalBlocked} mutationRate={(locomotionSettings.mutationRateOverrideEnabled ? locomotionSettings.diagnosticMutationChance : mutationChance):G6}");
        Debug.Log($"[GeodesicLocomotionEcology] hydrogenotrophSamples(passive/active)={passiveH}/{activeH} meanH2={passiveH2 / p:G6}/{activeH2 / a:G6} meanTemperaturePerformance={passivePerformance / p:G6}/{activePerformance / a:G6} meanSuitability={passiveSuitability / p:G6}/{activeSuitability / a:G6} meanEnergy={passiveEnergy / p:G6}/{activeEnergy / a:G6} cumulativeParentBirths={locomotionBirths[0]}/{locomotionBirths[1]} cumulativeDeaths={locomotionDeaths[0]}/{locomotionDeaths[1]}");
        Debug.Log($"[GeodesicLocomotionMutations] rows/columns=passive,amoeboid,flagellum,anchored transitions={locomotionTransitions[0]},{locomotionTransitions[1]},{locomotionTransitions[2]},{locomotionTransitions[3]};{locomotionTransitions[4]},{locomotionTransitions[5]},{locomotionTransitions[6]},{locomotionTransitions[7]};{locomotionTransitions[8]},{locomotionTransitions[9]},{locomotionTransitions[10]},{locomotionTransitions[11]};{locomotionTransitions[12]},{locomotionTransitions[13]},{locomotionTransitions[14]},{locomotionTransitions[15]}");
        LogTaxisDistributions();
    }

    private void ClearLocomotion()
    {
#if UNITY_EDITOR
        CollectActiveMovementDiagnostics = false;
#endif
        ClearTaxisDiagnostics();
        movementPerformance = default;
        locomotionMutationRandom = null;
        movementSamples = Array.Empty<MovementHabitatSample>(); movementSampleStamps = Array.Empty<int>(); movementSafeAngles = Array.Empty<float>();
        movementSampleGeneration = 1;
        mutationAttempts = successfulMutations = blockedMutations = inheritanceCorrections = movementCorrections = 0;
        suitabilityImproving = suitabilityWorsening = suitabilityNeutral = activeTumbles = worseningTumbles = completedRuns = 0;
        verticalTrials = verticalBetter = verticalWorse = verticalNeutral = verticalBlocked = 0;
        activeCellCrossings = activeLayerTransitions = depthBoundaryRejections = energyLimitedSteps = 0;
        completedRunSeconds = activeDistanceKm = activeHorizontalKm = activeVerticalKm = locomotionEnergyPaid = 0d;
        Array.Clear(locomotionTransitions, 0, locomotionTransitions.Length);
        Array.Clear(locomotionBirths, 0, locomotionBirths.Length); Array.Clear(locomotionDeaths, 0, locomotionDeaths.Length);
    }
}
