using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Mode boundary for the first ocean-only Geodesic biology foundation.</summary>
public sealed class GeodesicBiologyRuntime
{
    private static readonly ProfilerMarker Evaluation = new ProfilerMarker("GeodesicBiology.ReactionEvaluation");
    private static readonly ProfilerMarker HabitatSampling = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.HabitatSampling");
    private static readonly ProfilerMarker RequestBuild = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.RequestBuild");
    private static readonly ProfilerMarker DemandAggregation = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.DemandAggregation");
    private static readonly ProfilerMarker AvailabilityResolution = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.AvailabilityResolution");
    private static readonly ProfilerMarker ReactionCommit = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.ReactionCommit");
    private static readonly ProfilerMarker AgentEnergyAndStores = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.AgentEnergyAndStores");
    private static readonly ProfilerMarker MaintenanceAndLifecycle = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.MaintenanceAndLifecycle");
    private static readonly ProfilerMarker Competition = new ProfilerMarker("GeodesicBiology.CompetitionResolution");
    private static readonly ProfilerMarker Lifecycle = new ProfilerMarker("GeodesicBiology.LifecycleReproduction");
    private static readonly ProfilerMarker Movement = new ProfilerMarker("GeodesicBiology.PassiveMovement");
    private static readonly ProfilerMarker MovementKinematics = new ProfilerMarker("GeodesicBiology.PassiveMovement.KinematicsAndBoundary");
    private static readonly ProfilerMarker MovementWanderIntegration = new ProfilerMarker("GeodesicBiology.PassiveMovement.KinematicsAndBoundary.WanderIntegration");
    private static readonly ProfilerMarker MovementDirectionUpdate = new ProfilerMarker("GeodesicBiology.PassiveMovement.KinematicsAndBoundary.ContinuousDirectionUpdate");
    private static readonly ProfilerMarker MovementBoundaryCandidates = new ProfilerMarker("GeodesicBiology.PassiveMovement.KinematicsAndBoundary.BoundaryCandidateEvaluation");
    private static readonly ProfilerMarker MovementBoundaryResolution = new ProfilerMarker("GeodesicBiology.PassiveMovement.KinematicsAndBoundary.BoundaryCrossingResolution");
    private static readonly ProfilerMarker MovementLandHandling = new ProfilerMarker("GeodesicBiology.PassiveMovement.KinematicsAndBoundary.LandBoundaryHandling");
    private static readonly ProfilerMarker MovementStateWriteback = new ProfilerMarker("GeodesicBiology.PassiveMovement.KinematicsAndBoundary.StateWriteback");
    private static readonly ProfilerMarker MovementWanderRefresh = new ProfilerMarker("GeodesicBiology.PassiveMovement.WanderRefresh");
    private static readonly ProfilerMarker MovementVertical = new ProfilerMarker("GeodesicBiology.PassiveMovement.VerticalEvents");
    private static readonly ProfilerMarker MovementVisual = new ProfilerMarker("GeodesicBiology.PassiveMovement.VisualTarget");
    private const int ResourceCount = 7;
    public const float ReferenceTickSeconds = 0.5f;
    public const float PassiveKinematicsIntervalSeconds = 0.1f;
    public const float ReactionIntervalSeconds = 0.1f;
    public const float PassiveAngularSpeedRadiansPerSecond = 0.002f;
    public const float PassiveVerticalOpportunitiesPerSecond = 0.0015f;
    public const float PassiveWanderMinimumIntervalSeconds = 1f;
    public const float PassiveWanderMaximumIntervalSeconds = 5f;
    public const float PassiveMaximumWanderRateRadiansPerSecond = 2.0f;
    public const float PassiveWanderResponseSeconds = 2f;
    private const int MaximumBoundaryCrossingsPerStep = 3;
    private const int MaximumKinematicCatchUpSteps = 8;

    private PlanetGenerator planet;
    private GeodesicOceanResourceField resources;
    private GeodesicOceanSedimentField sediment;
    private GeodesicOceanTemperatureField oceanTemperature;
    private GeodesicExperiencedTemperatureField experiencedTemperature;
    private GeodesicSurfaceTemperatureField surfaceTemperature;
    private GeodesicOceanLayerGrid grid;
    private Request[] requests = Array.Empty<Request>();
    private double[] demand = Array.Empty<double>();
    private double[] availabilityFactor = Array.Empty<double>();
    private double[] delta = Array.Empty<double>();
    private int[] touched = Array.Empty<int>();
    private int[] stamps = Array.Empty<int>();
    private int[] lightStamps = Array.Empty<int>();
    private float[] temperatureByNode = Array.Empty<float>();
    private float[] lightByNode = Array.Empty<float>();
    private float[] oxygenByNode = Array.Empty<float>();
    private int generation = 1;
    private int touchedCount;
    private System.Random reproductionRandom;
    private readonly long[] requestedByMetabolism = new long[Enum.GetValues(typeof(MetabolismType)).Length];
    private readonly long[] achievedByMetabolism = new long[Enum.GetValues(typeof(MetabolismType)).Length];
    private readonly double[] extentByMetabolism = new double[Enum.GetValues(typeof(MetabolismType)).Length];
    private readonly double[] energyByMetabolism = new double[Enum.GetValues(typeof(MetabolismType)).Length];
    private long births;
    private long deaths;
    private long starvationDeaths;
    private long lifespanDeaths;
    private long zeroAchieved;
    private long resourceLimited;
    private long invalidHabitats;
    private long invalidBiologicalStates;
    private double maintenancePaid;
    private double diagnosticElapsed;
    private double temperatureSum;
    private float temperatureMin = float.PositiveInfinity;
    private float temperatureMax = float.NegativeInfinity;
    private double performanceSum;
    private float performanceMin = float.PositiveInfinity;
    private float performanceMax = float.NegativeInfinity;
    private long temperatureSamples;
    private long zeroTemperaturePerformance;
    private long biologySteps;
    private long agentEvaluations;
    private long habitatSamples;
    private long oceanTemperatureReads;
    private long photosyntheticLightReads;
    private long resourceInventoryReads;
    private long competitionPairs;
    private float lastBiologyStepSeconds;
    private float passiveMovementTime;
    private float passiveKinematicsAccumulator;
    private float reactionAccumulator;
    private long movementUpdates, continuousKinematicUpdates, horizontalBoundaryCrossings;
    private long verticalTransitions, landBoundaryRejections, invalidLayerCorrections;
    private long wanderStateRefreshes;
    private bool hydrogenotrophyConfigLogged;
    private int biologySeed;
    private float founderMinLife, founderMaxLife, founderBiomassTarget, founderLethalMargin;
    private Color founderColor;
    private Vector2 founderHydrogenTemperatureRange;

    public long BiologySteps => biologySteps;
    public long AgentEvaluations => agentEvaluations;
    public long HabitatSamples => habitatSamples;
    public long OceanTemperatureReads => oceanTemperatureReads;
    public long PhotosyntheticLightReads => photosyntheticLightReads;
    public long ResourceInventoryReads => resourceInventoryReads;
    public long CompetitionPairs => competitionPairs;
    public float LastBiologyStepSeconds => lastBiologyStepSeconds;
    public static MetabolismType NormalFounderMetabolism => MetabolismType.Hydrogenotrophy;
    public static LocomotionType NormalFounderLocomotion => LocomotionType.PassiveDrift;

    private struct Request
    {
        public int Node;
        public int Cell;
        public int Layer;
        public GeodesicOceanResource A, B, Product;
        public MetabolismType Metabolism;
        public double NeedA, NeedB, ProductCoefficient, Desired, Actual;
        public float EnergyPerExtent, StorePerExtent;
        public bool SulfurProduct;
        public bool Supported;
        public float Temperature;
        public float TemperaturePerformance;
        public double AvailabilityFactor;
    }

    public readonly struct HydrogenotrophyTickResult
    {
        public readonly double AchievedExtent;
        public readonly double Co2Withdrawal;
        public readonly double H2Withdrawal;
        public readonly float EnergyGain;
        public readonly float OrganicCIncrease;

        public HydrogenotrophyTickResult(double extent, double co2, double h2, float energy, float organicC)
        {
            AchievedExtent = extent;
            Co2Withdrawal = co2;
            H2Withdrawal = h2;
            EnergyGain = energy;
            OrganicCIncrease = organicC;
        }
    }

    public bool Initialize(PlanetGenerator generator, List<Replicator> agents, ReplicatorPopulationState state,
        int requestedFounders, float minLife, float maxLife, Color color, float biomassTarget,
        Vector2 hydrogenTemperatureRange, float lethalTemperatureMargin)
    {
        Clear();
        planet = generator;
        resources = generator != null ? generator.GetComponent<GeodesicOceanResourceField>() : null;
        sediment = generator != null ? generator.GetComponent<GeodesicOceanSedimentField>() : null;
        oceanTemperature = generator != null ? generator.GetComponent<GeodesicOceanTemperatureField>() : null;
        experiencedTemperature = generator != null ? generator.GetComponent<GeodesicExperiencedTemperatureField>() : null;
        surfaceTemperature = generator != null ? generator.GetComponent<GeodesicSurfaceTemperatureField>() : null;
        grid = resources != null ? resources.SourceGrid : null;
        if (grid == null || !resources.IsInitialized || oceanTemperature == null || !oceanTemperature.IsInitialized
            || experiencedTemperature == null || !experiencedTemperature.IsInitialized)
        {
            Debug.LogError("[GeodesicBiology] Required layered ocean/resource/temperature authority is not initialized.");
            Clear();
            return false;
        }

        demand = new double[grid.NodeCapacity * ResourceCount];
        availabilityFactor = new double[grid.NodeCapacity * ResourceCount];
        delta = new double[grid.NodeCapacity * ResourceCount];
        touched = new int[grid.NodeCapacity];
        stamps = new int[grid.NodeCapacity];
        lightStamps = new int[grid.NodeCapacity];
        temperatureByNode = new float[grid.NodeCapacity];
        lightByNode = new float[grid.NodeCapacity];
        oxygenByNode = new float[grid.NodeCapacity];
        requests = new Request[Mathf.Max(1, requestedFounders)];
        biologySeed = PlanetSeedUtility.DeriveSeed(generator.randomSeed, PlanetSeedDomain.Biology, PlanetGenerator.GenerationVersion);
        founderMinLife = minLife; founderMaxLife = maxLife; founderColor = color;
        founderBiomassTarget = biomassTarget; founderHydrogenTemperatureRange = hydrogenTemperatureRange;
        founderLethalMargin = lethalTemperatureMargin;
        var random = new System.Random(biologySeed);
        var visualRandom = new System.Random(biologySeed ^ 0x3419A7D);
        reproductionRandom = new System.Random(biologySeed ^ 0x5EED123);
        int spawned = SpawnFounders(requestedFounders, random, visualRandom, agents, state, minLife, maxLife, color, biomassTarget,
            hydrogenTemperatureRange, lethalTemperatureMargin, false);
        int occupiedCells = 0, occupiedLayers = 0;
        var cellSeen = new bool[grid.CellCount]; var layerSeen = new bool[grid.MaximumLayerCount];
        for (int i = 0; i < spawned; i++) { if (!cellSeen[agents[i].geodesicCellIndex]) { cellSeen[agents[i].geodesicCellIndex] = true; occupiedCells++; } int layer=agents[i].currentOceanLayerIndex; if(!layerSeen[layer]){layerSeen[layer]=true;occupiedLayers++;} }
        Debug.Log($"[GeodesicBiology] mode=Geodesic biologySeed={biologySeed} requested={requestedFounders} spawned={spawned} hydrogenotrophy={spawned} passiveDrift={spawned} occupiedCells={occupiedCells} occupiedLayers={occupiedLayers} ventFounders={spawned} founders=hydrogenotrophy-passive-drift-compact-submarine-vent-bottom");
        return true;
    }

    public int SpawnDeferredFounders(int requestedFounders, List<Replicator> agents,
        ReplicatorPopulationState state, double spawnSimulationTime)
    {
        if (grid == null || requestedFounders <= 0) return 0;
        passiveMovementTime = (float)Math.Max(0d, spawnSimulationTime);
        var random = new System.Random(biologySeed);
        var visualRandom = new System.Random(biologySeed ^ 0x3419A7D);
        int firstFounder = state.Count;
        int spawned = SpawnFounders(requestedFounders, random, visualRandom, agents, state,
            founderMinLife, founderMaxLife, founderColor, founderBiomassTarget,
            founderHydrogenTemperatureRange, founderLethalMargin, true);
        for (int i = firstFounder; i < firstFounder + spawned; i++)
            InitializePassiveSchedulesAtSpawn(state, i, passiveMovementTime);
        return spawned;
    }

    public static void InitializePassiveSchedulesAtSpawn(ReplicatorPopulationState state, int index, float spawnTime)
    {
        if (state == null || index < 0 || index >= state.Count) return;
        uint seed = MovementSeedBits(state.MovementSeed[index]);
        uint wanderSequence = state.PassiveWanderSequence[index];
        RefreshPassiveWanderTarget(ref state.PassiveTargetWanderRate[index],
            ref state.NextPassiveWanderUpdateTime[index], seed, ref wanderSequence, Mathf.Max(0f, spawnTime));
        state.PassiveWanderSequence[index] = wanderSequence;
        uint verticalSequence = state.PassiveMovementSequence[index];
        state.NextPassiveVerticalDriftTime[index] = Mathf.Max(0f, spawnTime)
            + SampleVerticalInterval(seed, verticalSequence++);
        state.PassiveMovementSequence[index] = verticalSequence;
    }

    private int SpawnFounders(int count, System.Random random, System.Random visualRandom,
        List<Replicator> agents, ReplicatorPopulationState state,
        float minLife, float maxLife, Color color, float biomassTarget, Vector2 hydrogenTemperatureRange,
        float lethalTemperatureMargin, bool forceZeroAge)
    {
        int validVents = 0;
        for (int i = 0; i < resources.CompactOutletCount; i++)
            if (resources.TryGetVentOutlet(i, out GeodesicVentSourceOutlet outlet) && outlet.Habitat == GeodesicVentHabitat.Submarine && grid.IsNodeActive(outlet.CellIndex, outlet.SourceNode % grid.MaximumLayerCount)) validVents++;
        if (count > 0 && validVents == 0) { Debug.LogWarning("[GeodesicBiology] No valid submarine vent habitat; no founders were spawned."); return 0; }
        for (int i = 0; i < count; i++)
        {
            int pick = random.Next(validVents), seen = 0; GeodesicVentSourceOutlet selected = default;
            for (int v = 0; v < resources.CompactOutletCount; v++) if (resources.TryGetVentOutlet(v, out var candidate) && candidate.Habitat == GeodesicVentHabitat.Submarine && grid.IsNodeActive(candidate.CellIndex, candidate.SourceNode % grid.MaximumLayerCount) && seen++ == pick) { selected = candidate; break; }
            MetabolismType metabolism = NormalFounderMetabolism;
            Vector3 direction = planet.GeodesicTopology.CellDirections[selected.CellIndex];
            int bottomLayer = selected.SourceNode % grid.MaximumLayerCount;
            float layerRadius = grid.LayerCenterRadius[grid.GetNodeIndex(selected.CellIndex, bottomLayer)];
            float meanSpacingRadians = GetMeanNeighborSpacingRadians(selected.CellIndex);
            Vector3 visualLocalPosition = CalculateVisualFounderPosition(direction, layerRadius, meanSpacingRadians,
                (float)visualRandom.NextDouble(), (float)visualRandom.NextDouble());
            Vector3 position = planet.transform.TransformPoint(visualLocalPosition);
            float life = Mathf.Lerp(minLife, maxLife, (float)random.NextDouble());
            var agent = new Replicator(position, Quaternion.FromToRotation(Vector3.up, visualLocalPosition.normalized), life, color,
                new Replicator.Traits(true, true, true, 0f), (float)random.NextDouble(), metabolism, NormalFounderLocomotion);
            float initialEnergy = Mathf.Lerp(0.1f, 0.5f, (float)random.NextDouble());
            float sampledAge = Mathf.Lerp(0f, life * 0.5f, (float)random.NextDouble());
            InitializeFounderBiologicalState(agent, selected.CellIndex, bottomLayer, hydrogenTemperatureRange,
                lethalTemperatureMargin, initialEnergy, forceZeroAge ? 0f : sampledAge, biomassTarget);
            agents.Add(agent); state.AddAgentFromReplicatorData(agent);
        }
        return count;
    }

    private float GetMeanNeighborSpacingRadians(int cellIndex)
    {
        GeodesicGridTopology topology = grid.SourceTopology;
        int count = topology.NeighborCounts[cellIndex];
        if (count <= 0) return 0f;
        float sum = 0f;
        int offset = cellIndex * 6;
        for (int i = 0; i < count; i++) sum += topology.NeighborAngularDistances6[offset + i];
        return sum / count;
    }

    public void Step(float dt, List<Replicator> agents, ReplicatorPopulationState state, float maintenance,
        float hydrogenCo2, float hydrogenH2, float hydrogenEnergy, float hydrogenStoreFraction,
        bool hydrogenO2InhibitionEnabled, float o2ComfortMax, float o2StressMax, float hydrogenMinimumO2Efficiency,
        float sulfurCo2, float sulfurH2s, float sulfurEnergy, float methaneCo2, float methaneH2, float methaneEnergy,
        float methanotrophyCh4, float methanotrophyO2, float methanotrophyEnergy, float photoCo2, float photoEnergy,
        float maxStore, float reproductionRate, bool carbonDivision, float divisionCost, float replicationCost,
        float divisionMultiple, float childSplit, int maxPopulation, Action<MetabolismType, DeathCause> registerDeathCause)
    {
        if (!hydrogenotrophyConfigLogged)
        {
            Debug.Log($"[GeodesicHydrogenotrophyConfig] H2PerTick={hydrogenH2:G6} CO2PerTick={hydrogenCo2:G6} EnergyPerTick={hydrogenEnergy:G6} StoreFraction={hydrogenStoreFraction:G6} BasalEnergyCostPerSecond={maintenance:G6} ReferenceTickSeconds={ReferenceTickSeconds:G6}");
            hydrogenotrophyConfigLogged = true;
        }
        biologySteps++;
        lastBiologyStepSeconds = dt;
        if (agents.Count == 0)
        {
            diagnosticElapsed += dt;
            if (diagnosticElapsed >= 10d) { LogDiagnostics(state, agents, carbonDivision, divisionCost, replicationCost, divisionMultiple); diagnosticElapsed = 0d; }
            return;
        }
        state.EnsureMatchesAgentCount(agents);
        using (Movement.Auto()) RunPassiveMovement(dt, agents, state);
        float reactionDt = ConsumeFixedInterval(ref reactionAccumulator, dt, ReactionIntervalSeconds);
        if (reactionDt > 0f)
        {
            EnsureRequestCapacity(agents.Count); BeginSparseStep();
            using (Evaluation.Auto())
            {
                using (HabitatSampling.Auto()) SampleOccupiedHabitats(state.Count, agents, state);
                using (RequestBuild.Auto())
                    for (int i = 0; i < state.Count; i++) BuildRequest(i, agents[i], state, reactionDt,
                        hydrogenCo2, hydrogenH2, hydrogenEnergy, hydrogenStoreFraction, hydrogenO2InhibitionEnabled,
                        o2ComfortMax, o2StressMax, hydrogenMinimumO2Efficiency, sulfurCo2, sulfurH2s, sulfurEnergy,
                        methaneCo2, methaneH2, methaneEnergy, methanotrophyCh4, methanotrophyO2, methanotrophyEnergy, photoCo2, photoEnergy);
                using (DemandAggregation.Auto()) AggregateDemands(state.Count);
                using (AvailabilityResolution.Auto()) ResolveAvailabilityFactors();
                using (Competition.Auto()) ResolveCompetition(state.Count);
                CommitRequests(state.Count, state, maxStore);
            }
        }
        using (Lifecycle.Auto())
        using (MaintenanceAndLifecycle.Auto()) RunLifecycle(dt, agents, state, maintenance, reproductionRate, carbonDivision, divisionCost, replicationCost, divisionMultiple, childSplit, maxPopulation, registerDeathCause);
        diagnosticElapsed += dt;
        if (diagnosticElapsed >= 10d)
        {
            LogDiagnostics(state, agents, carbonDivision, divisionCost, replicationCost, divisionMultiple);
            diagnosticElapsed = 0d;
        }
    }

    private void BuildRequest(int i, Replicator agent, ReplicatorPopulationState state, float dt,
        float hCo2, float hH2, float hEnergy, float hStoreFraction, bool hO2InhibitionEnabled,
        float o2ComfortMax, float o2StressMax, float hMinimumO2Efficiency,
        float sCo2, float sH2s, float sEnergy, float mCo2, float mH2, float mEnergy,
        float mtCh4, float mtO2, float mtEnergy, float pCo2, float pEnergy)
    {
        agentEvaluations++;
        int cell = agent.geodesicCellIndex, layer = state.CurrentOceanLayerIndex[i]; Request r = default; r.Cell = cell; r.Layer = layer; r.Metabolism = state.Metabolism[i];
        if (!grid.IsNodeActive(cell, layer)) { invalidHabitats++; requests[i] = r; return; }
        r.Node = grid.GetNodeIndex(cell, layer); float scale = dt / ReferenceTickSeconds;
        switch (state.Metabolism[i])
        {
            case MetabolismType.Hydrogenotrophy: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2; r.NeedA=hCo2; r.NeedB=hH2; r.EnergyPerExtent=hEnergy; r.StorePerExtent=hCo2*Mathf.Clamp01(hStoreFraction); break;
            case MetabolismType.SulfurChemosynthesis: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2S; r.NeedA=sCo2; r.NeedB=sH2s; r.EnergyPerExtent=sEnergy; r.StorePerExtent=sCo2; r.SulfurProduct=true; break;
            case MetabolismType.Methanogenesis: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2; r.Product=GeodesicOceanResource.CH4; r.NeedA=mCo2; r.NeedB=mH2; r.ProductCoefficient=mCo2*0.85; r.EnergyPerExtent=mEnergy*0.85f; r.StorePerExtent=(float)(mCo2*0.15); break;
            case MetabolismType.Methanotrophy: r.A=GeodesicOceanResource.CH4; r.B=GeodesicOceanResource.O2; r.Product=GeodesicOceanResource.CO2; r.NeedA=mtCh4; r.NeedB=mtO2; r.ProductCoefficient=mtCh4; r.EnergyPerExtent=mtEnergy; r.StorePerExtent=(float)(mtCh4*0.15); break;
            case MetabolismType.Photosynthesis:
                float light = GetCachedPhotosyntheticLight(r.Node, cell, layer);
                r.A = GeodesicOceanResource.CO2;
                r.B = GeodesicOceanResource.CO2;
                r.Product = GeodesicOceanResource.O2;
                r.NeedA = pCo2 * light;
                r.NeedB = 0;
                r.ProductCoefficient = r.NeedA;
                r.EnergyPerExtent = (float)(pEnergy * r.NeedA);
                r.StorePerExtent = (float)r.NeedA;
                break;
            default: requests[i]=r; return;
        }
        r.Supported = true;
        EnsureHabitatSample(r.Node, cell, layer);
        // Replicator.position is visual-only in Geodesic mode. Until biology owns an
        // authoritative sub-cell coordinate, feeding that position into the vent-core
        // query would make rendering placement control biology and can expose founders
        // to source-fluid temperature. Cell/layer temperature is the authoritative
        // organism temperature for this foundation.
        float temperature = temperatureByNode[r.Node];
        float optimumMin = state.OptimalTempMin[i], optimumMax = state.OptimalTempMax[i];
        float temperatureScale = CalculateTemperaturePerformance(temperature, optimumMin, optimumMax, state.LethalTempMargin[i]);
        float oxygenEfficiency = state.Metabolism[i] == MetabolismType.Hydrogenotrophy && hO2InhibitionEnabled
            ? CalculateAnaerobeO2Efficiency(oxygenByNode[r.Node], o2ComfortMax, o2StressMax, hMinimumO2Efficiency)
            : 1f;
        r.Temperature = temperature;
        r.TemperaturePerformance = temperatureScale;
        AccumulateTemperature(temperature, temperatureScale);
        if (!float.IsFinite(state.Energy[i]) || !float.IsFinite(state.OrganicCStore[i]) || !float.IsFinite(temperature)) invalidBiologicalStates++;
        r.Desired=Math.Max(0,scale*temperatureScale*oxygenEfficiency); requests[i]=r;
    }

    private void SampleOccupiedHabitats(int count, List<Replicator> agents, ReplicatorPopulationState state)
    {
        for (int i = 0; i < count; i++)
        {
            int cell = agents[i].geodesicCellIndex;
            int layer = state.CurrentOceanLayerIndex[i];
            if (grid.IsNodeActive(cell, layer)) EnsureHabitatSample(grid.GetNodeIndex(cell, layer), cell, layer);
        }
    }

    private void AggregateDemands(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Request r = requests[i];
            if (!r.Supported || r.Desired <= 0d || r.NeedA <= 0d) continue;
            demand[(int)r.A * grid.NodeCapacity + r.Node] += r.NeedA * r.Desired;
            if (r.NeedB > 0d) demand[(int)r.B * grid.NodeCapacity + r.Node] += r.NeedB * r.Desired;
        }
    }

    private void ResolveAvailabilityFactors()
    {
        for (int touchedIndex = 0; touchedIndex < touchedCount; touchedIndex++)
        {
            int node = touched[touchedIndex];
            int cell = node / grid.MaximumLayerCount;
            int layer = node % grid.MaximumLayerCount;
            for (int resourceIndex = 0; resourceIndex < ResourceCount; resourceIndex++)
            {
                int scratch = resourceIndex * grid.NodeCapacity + node;
                double totalDemand = demand[scratch];
                if (totalDemand <= 0d) continue;
                double available = resources.GetNodeInventory(cell, layer, (GeodesicOceanResource)resourceIndex);
                availabilityFactor[scratch] = CalculateAvailabilityFactor(available, totalDemand);
                resourceInventoryReads++;
                competitionPairs++;
            }
        }
    }

    private void ResolveCompetition(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Request r = requests[i];
            if (!r.Supported) continue;
            int metabolism = (int)r.Metabolism;
            requestedByMetabolism[metabolism]++;
            double factor = r.Desired > 0 ? 1d : 0d;
            if (r.Desired > 0)
            {
                factor = Math.Min(factor, availabilityFactor[(int)r.A * grid.NodeCapacity + r.Node]);
                if (r.NeedB > 0) factor = Math.Min(factor, availabilityFactor[(int)r.B * grid.NodeCapacity + r.Node]);
            }
            r.AvailabilityFactor = factor;
            r.Actual = r.Desired * factor;
            if (factor < 1d && r.Desired > 0) resourceLimited++;
            if (r.Actual > 0) achievedByMetabolism[metabolism]++; else zeroAchieved++;
            requests[i] = r;
        }
    }
    private void CommitRequests(int count, ReplicatorPopulationState state, float maxStore)
    {
        using (AgentEnergyAndStores.Auto())
        for (int i = 0; i < count; i++)
        {
            Request r = requests[i]; if (r.Actual <= 0) continue;
            delta[(int)r.A*grid.NodeCapacity+r.Node] -= r.NeedA*r.Actual;
            if (r.NeedB > 0) delta[(int)r.B*grid.NodeCapacity+r.Node] -= r.NeedB*r.Actual;
            if (r.ProductCoefficient > 0) delta[(int)r.Product*grid.NodeCapacity+r.Node] += r.ProductCoefficient*r.Actual;
            if (r.SulfurProduct) sediment.DepositSameColumn(r.Cell,r.NeedB*r.Actual,0);
            float gained = r.EnergyPerExtent*(float)r.Actual;
            state.Energy[i] += gained;
            state.OrganicCStore[i] = Mathf.Min(maxStore,state.OrganicCStore[i]+r.StorePerExtent*(float)r.Actual);
            int metabolism=(int)state.Metabolism[i]; extentByMetabolism[metabolism]+=r.Actual; energyByMetabolism[metabolism]+=gained;
        }
        using (ReactionCommit.Auto())
        for (int t=0;t<touchedCount;t++)
        {
            int node=touched[t];
            for(int k=0;k<ResourceCount;k++)
            {
                double d=delta[k*grid.NodeCapacity+node];
                if(d!=0) resources.ApplyDirectExchangeInventory((GeodesicOceanResource)k,node,d);
            }
        }
    }
    private void RunLifecycle(float dt, List<Replicator> agents, ReplicatorPopulationState state, float maintenance,
        float rate, bool carbon, float divisionCost, float replicationCost, float multiple, float split,
        int maxPopulation, Action<MetabolismType, DeathCause> registerDeathCause)
    {
        for (int i = state.Count - 1; i >= 0; i--)
        {
            state.Age[i] += dt;
            float paid = Mathf.Max(0f, maintenance * dt);
            state.Energy[i] -= paid;
            maintenancePaid += paid;
            DeathCause cause = ClassifyLifecycleDeath(state.Energy[i], state.Age[i], agents[i].maxLifespan);
            if (cause != DeathCause.Unknown)
            {
                deaths++;
                if (cause == DeathCause.EnergyDepletion) starvationDeaths++; else lifespanDeaths++;
                registerDeathCause?.Invoke(state.Metabolism[i], cause);
                RemoveAgentAtSwapBack(i, agents, state);
                continue;
            }

            bool eligible = state.Energy[i] >= (carbon ? divisionCost : replicationCost)
                && (!carbon || state.OrganicCStore[i] >= Mathf.Max(1, multiple) * agents[i].biomassTarget);
            if (!eligible || agents.Count >= maxPopulation || reproductionRandom.NextDouble() >= rate * dt) continue;
            Replicator parent = agents[i];
            float childMovementSeed = (float)reproductionRandom.NextDouble();
            var child = new Replicator(state.Position[i], state.Rotation[i], parent.maxLifespan, parent.color, parent.traits,
                childMovementSeed, state.Metabolism[i], state.Locomotion[i], parent.locomotionSkill);
            child.geodesicCellIndex = parent.geodesicCellIndex;
            child.currentOceanLayerIndex = state.CurrentOceanLayerIndex[i];
            child.preferredOceanLayerIndex = state.CurrentOceanLayerIndex[i];
            child.biomassTarget = parent.biomassTarget;
            child.optimalTempMin = state.OptimalTempMin[i];
            child.optimalTempMax = state.OptimalTempMax[i];
            child.lethalTempMargin = state.LethalTempMargin[i];
            state.Energy[i] -= carbon ? divisionCost : replicationCost;
            child.energy = Mathf.Max(0.1f, state.Energy[i] * 0.5f);
            if (carbon) { child.organicCStore = state.OrganicCStore[i] * Mathf.Clamp01(split); state.OrganicCStore[i] -= child.organicCStore; }
            agents.Add(child);
            state.AddAgentFromReplicatorData(child);
            births++;
        }
    }
    public static void RemoveAgentAtSwapBack(int i,List<Replicator>a,ReplicatorPopulationState s){int last=a.Count-1;if(i<0||i>last)return;if(i!=last)a[i]=a[last];a.RemoveAt(last);s.RemoveAgentAtSwapBack(i);}
    private void RunPassiveMovement(float dt, List<Replicator> agents, ReplicatorPopulationState state)
    {
        if (!(dt > 0f) || state.Count == 0) return;
        passiveMovementTime += dt;
        float kinematicsDt = ConsumeFixedInterval(ref passiveKinematicsAccumulator, dt, PassiveKinematicsIntervalSeconds);
        GeodesicGridTopology topology = grid.SourceTopology;
        Vector3[] cellDirections = topology.CellDirections;
        Matrix4x4 worldToLocal = planet.transform.worldToLocalMatrix;
        Matrix4x4 localToWorld = planet.transform.localToWorldMatrix;

        using (MovementWanderRefresh.Auto())
        {
            for (int i = 0; i < state.Count; i++)
            {
                if (state.Locomotion[i] != LocomotionType.PassiveDrift) continue;
                bool uninitialized = !(state.NextPassiveWanderUpdateTime[i] > 0f);
                if (!uninitialized && passiveMovementTime < state.NextPassiveWanderUpdateTime[i]) continue;
                uint sequence = state.PassiveWanderSequence[i];
                uint seed = MovementSeedBits(state.MovementSeed[i]);
                int safety = 0;
                if (uninitialized)
                {
                    RefreshPassiveWanderTarget(ref state.PassiveTargetWanderRate[i],
                        ref state.NextPassiveWanderUpdateTime[i], seed, ref sequence, passiveMovementTime);
                    wanderStateRefreshes++;
                }
                while (passiveMovementTime >= state.NextPassiveWanderUpdateTime[i] && safety++ < 4)
                {
                    RefreshPassiveWanderTarget(ref state.PassiveTargetWanderRate[i],
                        ref state.NextPassiveWanderUpdateTime[i], seed, ref sequence,
                        state.NextPassiveWanderUpdateTime[i]);
                    wanderStateRefreshes++;
                }
                state.PassiveWanderSequence[i] = sequence;
            }
        }

        using (MovementKinematics.Auto())
        {
            if (kinematicsDt > 0f)
            for (int i = 0; i < state.Count; i++)
            {
                if (state.Locomotion[i] != LocomotionType.PassiveDrift) continue;
                movementUpdates++;
                EnsurePassiveKinematicState(i, state, worldToLocal);
                Vector3 direction = state.PassiveDriftDirection[i];
                Vector3 tangent = state.PassiveDriftTangent[i];

                // Great-circle advection plus a smoothly evolving scheduled curvature. This is
                // continuous sub-cell transport and has no access to environmental suitability.
                float wanderRate;
                using (MovementWanderIntegration.Auto()) wanderRate = EvolvePassiveWanderRate(state.PassiveWanderRate[i],
                    state.PassiveTargetWanderRate[i], kinematicsDt);
                state.PassiveWanderRate[i] = wanderRate;
                int integrationSteps = Mathf.Clamp(Mathf.CeilToInt(kinematicsDt / PassiveKinematicsIntervalSeconds), 1,
                    MaximumKinematicCatchUpSteps);
                float integrationDt = kinematicsDt / integrationSteps;
                Vector3 proposedDirection = direction;
                for (int integrationStep = 0; integrationStep < integrationSteps; integrationStep++)
                {
                using (MovementDirectionUpdate.Auto()) AdvancePassiveKinematics(ref direction, ref tangent, wanderRate, integrationDt);
                proposedDirection = direction;

                int cell = agents[i].geodesicCellIndex;
                for (int crossing = 0; crossing < MaximumBoundaryCrossingsPerStep; crossing++)
                {
                    int candidate;
                    using (MovementBoundaryCandidates.Auto()) candidate = FindCloserRealNeighbor(cell, proposedDirection, cellDirections,
                        topology.Neighbors6, topology.NeighborCounts);
                    if (candidate < 0) break;
                    int activeLayers = grid.ActiveLayerCountByCell[candidate];
                    if (activeLayers <= 0)
                    {
                        landBoundaryRejections++;
                        using (MovementLandHandling.Auto()) ReflectFromLandBoundary(ref proposedDirection, ref tangent, cellDirections[cell], cellDirections[candidate]);
                        break;
                    }
                    using (MovementBoundaryResolution.Auto())
                    {
                    int mappedLayer = ResolveHorizontalTargetLayer(state.CurrentOceanLayerIndex[i], activeLayers);
                    if (mappedLayer != state.CurrentOceanLayerIndex[i]) invalidLayerCorrections++;
                    state.CurrentOceanLayerIndex[i] = mappedLayer;
                    cell = candidate;
                    agents[i].geodesicCellIndex = cell;
                    horizontalBoundaryCrossings++;
                    }
                }
                direction = proposedDirection;
                }

                using (MovementStateWriteback.Auto())
                {
                state.PassiveDriftDirection[i] = proposedDirection;
                state.PassiveDriftTangent[i] = tangent;
                }
                continuousKinematicUpdates++;
            }
        }

        using (MovementVertical.Auto())
        {
            for (int i = 0; i < state.Count; i++)
            {
                if (state.Locomotion[i] != LocomotionType.PassiveDrift) continue;
                agents[i].currentOceanLayerIndex = state.CurrentOceanLayerIndex[i];
                agents[i].preferredOceanLayerIndex = state.CurrentOceanLayerIndex[i];
                state.PreferredOceanLayerIndex[i] = state.CurrentOceanLayerIndex[i];
                bool uninitialized = !(state.NextPassiveVerticalDriftTime[i] > 0f);
                if (!uninitialized && passiveMovementTime < state.NextPassiveVerticalDriftTime[i]) continue;
                uint seed = MovementSeedBits(state.MovementSeed[i]);
                uint sequence = state.PassiveMovementSequence[i];
                if (uninitialized)
                    state.NextPassiveVerticalDriftTime[i] = passiveMovementTime + SampleVerticalInterval(seed, sequence++);
                int eventSafety = 0;
                while (passiveMovementTime >= state.NextPassiveVerticalDriftTime[i] && eventSafety++ < 4)
                {
                    int cell = agents[i].geodesicCellIndex;
                    int layer = state.CurrentOceanLayerIndex[i];
                    int verticalDirection = (Hash(seed, sequence++) & 1u) == 0u ? -1 : 1;
                    int targetLayer = ResolveAdjacentVerticalLayer(layer, verticalDirection, grid.ActiveLayerCountByCell[cell]);
                    if (targetLayer != layer && grid.IsNodeActive(cell, targetLayer))
                    {
                        state.CurrentOceanLayerIndex[i] = targetLayer;
                        verticalTransitions++;
                    }
                    state.NextPassiveVerticalDriftTime[i] += SampleVerticalInterval(seed, sequence++);
                }
                state.PassiveMovementSequence[i] = sequence;
            }
        }

        using (MovementVisual.Auto())
        {
            for (int i = 0; i < state.Count; i++)
            {
                if (state.Locomotion[i] != LocomotionType.PassiveDrift) continue;
                int cell = agents[i].geodesicCellIndex;
                int layer = state.CurrentOceanLayerIndex[i];
                float targetRadius = grid.LayerCenterRadius[grid.GetNodeIndex(cell, layer)];
                float visualRadius = state.PassiveVisualRadius[i];
                if (!(visualRadius > 0f)) visualRadius = targetRadius;
                visualRadius = Mathf.MoveTowards(visualRadius, targetRadius, Mathf.Max(0.01f, grid.MaximumOceanDepth) * dt * 2f);
                state.PassiveVisualRadius[i] = visualRadius;
                Vector3 localDirection = state.PassiveDriftDirection[i];
                state.Position[i] = localToWorld.MultiplyPoint3x4(localDirection * visualRadius);
            }
        }
    }

    public static float ConsumeFixedInterval(ref float accumulator, float dt, float interval)
    {
        if (!(dt > 0f) || !(interval > 0f)) return 0f;
        accumulator += dt;
        int ticks = Mathf.FloorToInt((accumulator + interval * 1e-5f) / interval);
        if (ticks <= 0) return 0f;
        float elapsed = ticks * interval;
        accumulator = Mathf.Max(0f, accumulator - elapsed);
        return elapsed;
    }

    private void EnsurePassiveKinematicState(int index, ReplicatorPopulationState state, Matrix4x4 worldToLocal)
    {
        if (state.PassiveDriftDirection[index].sqrMagnitude > 0.9f) return;
        Vector3 direction = worldToLocal.MultiplyPoint3x4(state.Position[index]).normalized;
        if (direction.sqrMagnitude < 0.9f) direction = planet.GeodesicTopology.CellDirections[0];
        uint seed = MovementSeedBits(state.MovementSeed[index]);
        state.PassiveDriftDirection[index] = direction;
        state.PassiveDriftTangent[index] = CreateInitialPassiveTangent(direction, seed);
        state.PassiveVisualRadius[index] = worldToLocal.MultiplyPoint3x4(state.Position[index]).magnitude;
    }

    public static int FindCloserRealNeighbor(int currentCell, Vector3 continuousDirection, Vector3[] cellDirections,
        int[] neighbors6, byte[] neighborCounts)
    {
        if (cellDirections == null || neighbors6 == null || neighborCounts == null || currentCell < 0
            || currentCell >= cellDirections.Length || currentCell >= neighborCounts.Length) return -1;
        float currentAlignment = Vector3.Dot(continuousDirection, cellDirections[currentCell]);
        int result = -1;
        float bestAlignment = currentAlignment;
        int count = neighborCounts[currentCell];
        int offset = currentCell * 6;
        for (int slot = 0; slot < count; slot++)
        {
            int neighbor = neighbors6[offset + slot];
            if (neighbor < 0 || neighbor >= cellDirections.Length) continue;
            float alignment = Vector3.Dot(continuousDirection, cellDirections[neighbor]);
            if (alignment > bestAlignment + 1e-7f) { bestAlignment = alignment; result = neighbor; }
        }
        return result;
    }

    public static Vector3 CreateInitialPassiveTangent(Vector3 direction, uint seed)
    {
        direction = direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector3.up;
        Vector3 reference = Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 tangentA = Vector3.Cross(reference, direction).normalized;
        Vector3 tangentB = Vector3.Cross(direction, tangentA);
        float angle = ToUnitFloat(Hash(seed, 0x94D049BBu)) * Mathf.PI * 2f;
        return (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)).normalized;
    }

    public static void RefreshPassiveWanderTarget(ref float targetRate, ref float nextUpdateTime,
        uint seed, ref uint sequence, float scheduleFromTime)
    {
        targetRate = (ToUnitFloat(Hash(seed, sequence++)) * 2f - 1f)
            * PassiveMaximumWanderRateRadiansPerSecond;
        float interval01 = ToUnitFloat(Hash(seed, sequence++));
        nextUpdateTime = scheduleFromTime + Mathf.Lerp(PassiveWanderMinimumIntervalSeconds,
            PassiveWanderMaximumIntervalSeconds, interval01);
    }

    public static void AdvancePassiveKinematics(ref Vector3 direction, ref Vector3 tangent, float wanderRate, float dt)
    {
        if (!(dt > 0f)) return;
        float turnAngle = Mathf.Clamp(wanderRate, -PassiveMaximumWanderRateRadiansPerSecond,
            PassiveMaximumWanderRateRadiansPerSecond) * dt;
        Vector3 side = Vector3.Cross(direction, tangent);
        tangent = (tangent + side * turnAngle).normalized;
        float travelAngle = PassiveAngularSpeedRadiansPerSecond * dt;
        Vector3 oldDirection = direction;
        direction = (oldDirection + tangent * travelAngle).normalized;
        tangent = Vector3.ProjectOnPlane(tangent, direction).normalized;
    }

    public static float EvolvePassiveWanderRate(float currentRate, float targetRate, float dt)
    {
        float response = Mathf.Clamp01(Mathf.Max(0f, dt) / PassiveWanderResponseSeconds);
        return Mathf.Lerp(currentRate, Mathf.Clamp(targetRate, -PassiveMaximumWanderRateRadiansPerSecond,
            PassiveMaximumWanderRateRadiansPerSecond), response);
    }

    public static void ReflectFromLandBoundary(ref Vector3 direction, ref Vector3 tangent,
        Vector3 oceanCellDirection, Vector3 landCellDirection)
    {
        Vector3 bisectorNormal = (landCellDirection - oceanCellDirection).normalized;
        float penetration = Vector3.Dot(direction, bisectorNormal);
        if (penetration > 0f) direction = (direction - 2f * penetration * bisectorNormal).normalized;
        Vector3 boundaryNormal = Vector3.ProjectOnPlane(bisectorNormal, direction);
        if (boundaryNormal.sqrMagnitude > 1e-10f)
        {
            boundaryNormal.Normalize();
            tangent = (tangent - 2f * Vector3.Dot(tangent, boundaryNormal) * boundaryNormal).normalized;
        }
        direction = (direction + oceanCellDirection * 1e-7f).normalized;
        tangent = Vector3.ProjectOnPlane(tangent, direction).normalized;
    }

    public static int ResolveHorizontalTargetLayer(int sourceLayer, int targetActiveLayerCount)
        => targetActiveLayerCount <= 0 ? -1 : Mathf.Clamp(sourceLayer, 0, targetActiveLayerCount - 1);

    public static int ResolveAdjacentVerticalLayer(int sourceLayer, int direction, int activeLayerCount)
    {
        if (activeLayerCount <= 0) return -1;
        int current = Mathf.Clamp(sourceLayer, 0, activeLayerCount - 1);
        return Mathf.Clamp(current + (direction < 0 ? -1 : 1), 0, activeLayerCount - 1);
    }

    public static float SampleVerticalInterval(uint seed, uint sequence)
    {
        float unit = Mathf.Clamp(ToUnitFloat(Hash(seed, sequence)), 1e-7f, 1f - 1e-7f);
        return -Mathf.Log(1f - unit) / PassiveVerticalOpportunitiesPerSecond;
    }

    private static uint MovementSeedBits(float seed) => unchecked((uint)Mathf.RoundToInt(seed * 16777213f)) ^ 0x9E3779B9u;
    private static float ToUnitFloat(uint value) => (value >> 8) * (1f / 16777216f);
    private static uint Hash(uint seed, uint sequence)
    {
        uint x = seed ^ (sequence * 0x9E3779B9u);
        x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
        return x;
    }
    private void EnsureHabitatSample(int node, int cell, int layer)
    {
        if (!MarkTouchedNode(node, generation, stamps)) return;
        touched[touchedCount++] = node;
        temperatureByNode[node] = oceanTemperature.GetLayerTemperatureKelvin(cell, layer);
        resources.TryGetConcentration(cell, layer, GeodesicOceanResource.O2, out oxygenByNode[node]);
        habitatSamples++;
        oceanTemperatureReads++;
        for (int resource = 0; resource < ResourceCount; resource++)
        {
            int scratch = resource * grid.NodeCapacity + node;
            demand[scratch] = 0d;
            availabilityFactor[scratch] = 0d;
            delta[scratch] = 0d;
        }
    }
    private float GetCachedPhotosyntheticLight(int node, int cell, int layer)
    {
        EnsureHabitatSample(node, cell, layer);
        if (lightStamps[node] == generation) return lightByNode[node];
        lightStamps[node] = generation;
        lightByNode[node] = ResolvePhotosyntheticLight(surfaceTemperature.GetCellInsolationCosine(cell), layer);
        photosyntheticLightReads++;
        return lightByNode[node];
    }
    public static bool MarkTouchedNode(int node, int generationValue, int[] generationStamps)
    {
        if (generationStamps == null || node < 0 || node >= generationStamps.Length) return false;
        if (generationStamps[node] == generationValue) return false;
        generationStamps[node] = generationValue;
        return true;
    }
    private void BeginSparseStep(){touchedCount=0;if(++generation==int.MaxValue){Array.Clear(stamps,0,stamps.Length);Array.Clear(lightStamps,0,lightStamps.Length);generation=1;}}
    private void EnsureRequestCapacity(int count){if(requests.Length<count)Array.Resize(ref requests,Mathf.NextPowerOfTwo(count));}
    public static float CalculateTemperaturePerformance(float temperature, float optimumMin, float optimumMax, float lethalMargin)
    {
        if (!float.IsFinite(temperature) || !float.IsFinite(optimumMin) || !float.IsFinite(optimumMax)
            || !float.IsFinite(lethalMargin) || optimumMax < optimumMin || lethalMargin <= 0f) return 0f;
        if (temperature >= optimumMin && temperature <= optimumMax) return 1f;
        float distance = temperature < optimumMin ? optimumMin - temperature : temperature - optimumMax;
        return Mathf.Clamp01(1f - distance / lethalMargin);
    }
    public static void InitializeFounderBiologicalState(Replicator agent, int cell, int layer, Vector2 thermalRange,
        float lethalMargin, float energy, float age, float biomassTarget)
    {
        if (agent == null) return;
        agent.geodesicCellIndex = cell; agent.currentOceanLayerIndex = layer; agent.preferredOceanLayerIndex = layer;
        agent.energy = Mathf.Max(0f, energy); agent.age = Mathf.Max(0f, age); agent.organicCStore = 0f;
        agent.optimalTempMin = Mathf.Min(thermalRange.x, thermalRange.y);
        agent.optimalTempMax = Mathf.Max(thermalRange.x, thermalRange.y);
        agent.lethalTempMargin = Mathf.Max(1f, lethalMargin);
        agent.biomassTarget = Mathf.Max(0.0001f, biomassTarget);
    }
    public static double CalculateAchievedExtent(double requestedExtent, double availableA, double needA, double availableB, double needB)
    {
        if (!(requestedExtent > 0d) || !(needA > 0d)) return 0d;
        double factor = Math.Min(1d, Math.Max(0d, availableA) / (needA * requestedExtent));
        if (needB > 0d) factor = Math.Min(factor, Math.Max(0d, availableB) / (needB * requestedExtent));
        return requestedExtent * factor;
    }
    public static HydrogenotrophyTickResult CalculateHydrogenotrophyTick(float dt, float co2PerTick,
        float h2PerTick, float energyPerTick, float storeFraction, float temperaturePerformance,
        float oxygenEfficiency, float substrateAvailabilityFactor)
    {
        double extent = Math.Max(0d, dt / ReferenceTickSeconds)
            * Mathf.Clamp01(temperaturePerformance)
            * Mathf.Clamp01(oxygenEfficiency)
            * Mathf.Clamp01(substrateAvailabilityFactor);
        return new HydrogenotrophyTickResult(
            extent,
            Math.Max(0f, co2PerTick) * extent,
            Math.Max(0f, h2PerTick) * extent,
            Math.Max(0f, energyPerTick) * (float)extent,
            Math.Max(0f, co2PerTick) * Mathf.Clamp01(storeFraction) * (float)extent);
    }
    public static double CalculateAvailabilityFactor(double availableInventory, double totalDemand)
    {
        if (!(totalDemand > 0d)) return 0d;
        return Math.Min(1d, Math.Max(0d, availableInventory) / totalDemand);
    }
    public static float CalculateAnaerobeO2Efficiency(float localO2, float comfortMax, float stressMax, float minimumEfficiency)
    {
        comfortMax = Mathf.Max(0f, comfortMax);
        stressMax = Mathf.Max(comfortMax + 0.0001f, stressMax);
        if (localO2 <= comfortMax) return 1f;
        float t = Mathf.Clamp01((localO2 - comfortMax) / (stressMax - comfortMax));
        float inhibition = t * t * (3f - 2f * t);
        return Mathf.Lerp(1f, Mathf.Clamp01(minimumEfficiency), inhibition);
    }
    public static Vector3 CalculateVisualFounderPosition(Vector3 cellDirection, float layerRadius,
        float meanNeighborSpacingRadians, float radialRandom01, float angleRandom01)
    {
        Vector3 normal = cellDirection.sqrMagnitude > 1e-12f ? cellDirection.normalized : Vector3.up;
        Vector3 reference = Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 tangentA = Vector3.Cross(reference, normal).normalized;
        Vector3 tangentB = Vector3.Cross(normal, tangentA);
        float angularRadius = Mathf.Max(0f, meanNeighborSpacingRadians) * 0.18f * Mathf.Sqrt(Mathf.Clamp01(radialRandom01));
        float angle = Mathf.Clamp01(angleRandom01) * Mathf.PI * 2f;
        Vector3 tangent = tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle);
        Vector3 scatteredDirection = (normal + tangent * Mathf.Tan(angularRadius)).normalized;
        return scatteredDirection * Mathf.Max(0f, layerRadius);
    }
    public static DeathCause ClassifyLifecycleDeath(float energy, float age, float lifespan)
    {
        if (float.IsFinite(age) && float.IsFinite(lifespan) && age > lifespan) return DeathCause.OldAge;
        if (!float.IsFinite(energy) || energy <= 0f) return DeathCause.EnergyDepletion;
        return DeathCause.Unknown;
    }
    private void AccumulateTemperature(float temperature, float performance)
    {
        if (!float.IsFinite(temperature) || !float.IsFinite(performance)) return;
        temperatureSum += temperature; temperatureMin = Mathf.Min(temperatureMin, temperature); temperatureMax = Mathf.Max(temperatureMax, temperature);
        performanceSum += performance; performanceMin = Mathf.Min(performanceMin, performance); performanceMax = Mathf.Max(performanceMax, performance);
        temperatureSamples++; if (performance <= 0f) zeroTemperaturePerformance++;
    }
    private void LogDiagnostics(ReplicatorPopulationState state, List<Replicator> agents, bool carbonDivision,
        float divisionCost, float replicationCost, float divisionMultiple)
    {
        int hydrogen=0,sulfur=0,methanogenesis=0,photosynthesis=0,methanotrophy=0,passive=0,amoeboid=0,flagellum=0,anchored=0;
        float organicMin=float.PositiveInfinity,organicMax=float.NegativeInfinity,thresholdMin=float.PositiveInfinity,thresholdMax=float.NegativeInfinity,maxDivisionCarbonFraction=0f;double organicSum=0d,thresholdSum=0d;int divisionEligible=0,occupiedNodeCount=0;
        for(int i=0;i<state.Count;i++){switch(state.Metabolism[i]){case MetabolismType.Hydrogenotrophy:hydrogen++;break;case MetabolismType.SulfurChemosynthesis:sulfur++;break;case MetabolismType.Methanogenesis:methanogenesis++;break;case MetabolismType.Photosynthesis:photosynthesis++;break;case MetabolismType.Methanotrophy:methanotrophy++;break;}switch(state.Locomotion[i]){case LocomotionType.PassiveDrift:passive++;break;case LocomotionType.Amoeboid:amoeboid++;break;case LocomotionType.Flagellum:flagellum++;break;case LocomotionType.Anchored:anchored++;break;}float store=Mathf.Max(0f,state.OrganicCStore[i]);float threshold=Mathf.Max(1f,divisionMultiple)*(i<agents.Count?Mathf.Max(0.0001f,agents[i].biomassTarget):0.0001f);organicMin=Mathf.Min(organicMin,store);organicMax=Mathf.Max(organicMax,store);organicSum+=store;thresholdMin=Mathf.Min(thresholdMin,threshold);thresholdMax=Mathf.Max(thresholdMax,threshold);thresholdSum+=threshold;maxDivisionCarbonFraction=Mathf.Max(maxDivisionCarbonFraction,store/threshold);bool energyReady=state.Energy[i]>=(carbonDivision?divisionCost:replicationCost);if(energyReady&&(!carbonDivision||store>=threshold))divisionEligible++;int cell=agents[i].geodesicCellIndex;int layer=state.CurrentOceanLayerIndex[i];if(grid.IsNodeActive(cell,layer)){int node=grid.GetNodeIndex(cell,layer);if(MarkTouchedNode(node,-generation,lightStamps))touched[occupiedNodeCount++]=node;}}
        float occupiedH2Min=float.PositiveInfinity,occupiedH2Max=float.NegativeInfinity,occupiedCo2Min=float.PositiveInfinity,occupiedCo2Max=float.NegativeInfinity;double occupiedH2Sum=0d,occupiedCo2Sum=0d;int occupiedSamples=0;
        for(int t=0;t<occupiedNodeCount;t++){int node=touched[t];int cell=node/grid.MaximumLayerCount;int layer=node%grid.MaximumLayerCount;if(resources.TryGetConcentration(cell,layer,GeodesicOceanResource.H2,out float h2Value)&&resources.TryGetConcentration(cell,layer,GeodesicOceanResource.CO2,out float co2Value)){occupiedH2Min=Mathf.Min(occupiedH2Min,h2Value);occupiedH2Max=Mathf.Max(occupiedH2Max,h2Value);occupiedH2Sum+=h2Value;occupiedCo2Min=Mathf.Min(occupiedCo2Min,co2Value);occupiedCo2Max=Mathf.Max(occupiedCo2Max,co2Value);occupiedCo2Sum+=co2Value;occupiedSamples++;}}
        if(state.Count==0){organicMin=organicMax=thresholdMin=thresholdMax=float.NaN;}if(occupiedSamples==0){occupiedH2Min=occupiedH2Max=occupiedCo2Min=occupiedCo2Max=float.NaN;}
        double samples=Math.Max(1,temperatureSamples);
        int h=(int)MetabolismType.Hydrogenotrophy,s=(int)MetabolismType.SulfurChemosynthesis,m=(int)MetabolismType.Methanogenesis,p=(int)MetabolismType.Photosynthesis,mt=(int)MetabolismType.Methanotrophy;
        double evaluationsPerHabitat=agentEvaluations/(double)Math.Max(1L,habitatSamples);
        double evaluationsPerTemperatureRead=agentEvaluations/(double)Math.Max(1L,oceanTemperatureReads);
        Debug.Log($"[GeodesicBiologyTelemetry] population={state.Count} biologySteps={biologySteps} lastStepSeconds={lastBiologyStepSeconds:G6} agentEvaluations={agentEvaluations} habitatSamples={habitatSamples} occupiedHabitatH2(min/avg/max)={occupiedH2Min:G6}/{occupiedH2Sum/Math.Max(1,occupiedSamples):G6}/{occupiedH2Max:G6} occupiedHabitatCO2(min/avg/max)={occupiedCo2Min:G6}/{occupiedCo2Sum/Math.Max(1,occupiedSamples):G6}/{occupiedCo2Max:G6} organicCStore(min/avg/max)={organicMin:G6}/{organicSum/Math.Max(1,state.Count):G6}/{organicMax:G6} divisionThreshold(min/avg/max)={thresholdMin:G6}/{thresholdSum/Math.Max(1,state.Count):G6}/{thresholdMax:G6} maxDivisionCarbonFraction={maxDivisionCarbonFraction:G6} divisionEligible={divisionEligible} oceanTemperatureReads={oceanTemperatureReads} photosyntheticLightReads={photosyntheticLightReads} resourceInventoryReads={resourceInventoryReads} competitionPairs={competitionPairs} evaluationsPerHabitat={evaluationsPerHabitat:G6} evaluationsPerTemperatureRead={evaluationsPerTemperatureRead:G6} metabolismOrder=h/s/m/p/mt populationByMetabolism={hydrogen}/{sulfur}/{methanogenesis}/{photosynthesis}/{methanotrophy} locomotionOrder=passive/amoeboid/flagellum/anchored populationByLocomotion={passive}/{amoeboid}/{flagellum}/{anchored} movementUpdates={movementUpdates} wanderStateRefreshes={wanderStateRefreshes} continuousKinematicUpdates={continuousKinematicUpdates} horizontalBoundaryCrossings={horizontalBoundaryCrossings} verticalTransitions={verticalTransitions} landBoundaryRejections={landBoundaryRejections} invalidLayerCorrections={invalidLayerCorrections} births={births} deaths={deaths} starvation={starvationDeaths} lifespan={lifespanDeaths} requests={requestedByMetabolism[h]}/{requestedByMetabolism[s]}/{requestedByMetabolism[m]}/{requestedByMetabolism[p]}/{requestedByMetabolism[mt]} achieved={achievedByMetabolism[h]}/{achievedByMetabolism[s]}/{achievedByMetabolism[m]}/{achievedByMetabolism[p]}/{achievedByMetabolism[mt]} zeroAchieved={zeroAchieved} resourceLimited={resourceLimited} extent={extentByMetabolism[h]:G6}/{extentByMetabolism[s]:G6}/{extentByMetabolism[m]:G6}/{extentByMetabolism[p]:G6}/{extentByMetabolism[mt]:G6} energy={energyByMetabolism[h]:G6}/{energyByMetabolism[s]:G6}/{energyByMetabolism[m]:G6}/{energyByMetabolism[p]:G6}/{energyByMetabolism[mt]:G6} maintenance={maintenancePaid:G6} biologyTemperatureAuthority=coarseOceanLayer temperatureK(min/avg/max)={temperatureMin:G6}/{temperatureSum/samples:G6}/{temperatureMax:G6} performance(min/avg/max)={performanceMin:G6}/{performanceSum/samples:G6}/{performanceMax:G6} zeroPerformance={zeroTemperaturePerformance} invalidHabitat={invalidHabitats} invalidState={invalidBiologicalStates}");
        biologySteps=agentEvaluations=habitatSamples=oceanTemperatureReads=photosyntheticLightReads=resourceInventoryReads=competitionPairs=movementUpdates=wanderStateRefreshes=continuousKinematicUpdates=horizontalBoundaryCrossings=verticalTransitions=landBoundaryRejections=invalidLayerCorrections=0;
    }
    public static float ResolvePhotosyntheticLight(float daylight, int layer) => Mathf.Clamp01(daylight) * (layer == 0 ? 1f : layer == 1 ? 0.55f : 0f);
    public void Clear(){planet=null;resources=null;sediment=null;oceanTemperature=null;experiencedTemperature=null;surfaceTemperature=null;grid=null;requests=Array.Empty<Request>();demand=availabilityFactor=delta=Array.Empty<double>();touched=stamps=lightStamps=Array.Empty<int>();temperatureByNode=lightByNode=oxygenByNode=Array.Empty<float>();touchedCount=0;Array.Clear(requestedByMetabolism,0,requestedByMetabolism.Length);Array.Clear(achievedByMetabolism,0,achievedByMetabolism.Length);Array.Clear(extentByMetabolism,0,extentByMetabolism.Length);Array.Clear(energyByMetabolism,0,energyByMetabolism.Length);births=deaths=starvationDeaths=lifespanDeaths=zeroAchieved=resourceLimited=invalidHabitats=invalidBiologicalStates=zeroTemperaturePerformance=temperatureSamples=biologySteps=agentEvaluations=habitatSamples=oceanTemperatureReads=photosyntheticLightReads=resourceInventoryReads=competitionPairs=movementUpdates=wanderStateRefreshes=continuousKinematicUpdates=horizontalBoundaryCrossings=verticalTransitions=landBoundaryRejections=invalidLayerCorrections=0;maintenancePaid=diagnosticElapsed=temperatureSum=performanceSum=0d;lastBiologyStepSeconds=passiveMovementTime=passiveKinematicsAccumulator=reactionAccumulator=0f;hydrogenotrophyConfigLogged=false;biologySeed=0;founderMinLife=founderMaxLife=founderBiomassTarget=founderLethalMargin=0f;founderColor=default;founderHydrogenTemperatureRange=default;temperatureMin=performanceMin=float.PositiveInfinity;temperatureMax=performanceMax=float.NegativeInfinity;}
}
