using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Ocean-only geodesic biology, lifecycle, and continuous locomotion.</summary>
public sealed partial class GeodesicBiologyRuntime
{
    private static readonly ProfilerMarker Evaluation = new ProfilerMarker("GeodesicBiology.ReactionEvaluation");
    private static readonly ProfilerMarker Telemetry = new ProfilerMarker("GeodesicBiology.Telemetry");
    private static readonly ProfilerMarker HabitatSampling = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.HabitatSampling");
    private static readonly ProfilerMarker RequestBuild = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.RequestBuild");
    private static readonly ProfilerMarker DemandAggregation = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.DemandAggregation");
    private static readonly ProfilerMarker AvailabilityResolution = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.AvailabilityResolution");
    private static readonly ProfilerMarker ReactionCommit = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.ReactionCommit");
    private static readonly ProfilerMarker AgentEnergyAndStores = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.AgentEnergyAndStores");
    private static readonly ProfilerMarker MaintenanceAndLifecycle = new ProfilerMarker("GeodesicBiology.ReactionEvaluation.MaintenanceAndLifecycle");
    private static readonly ProfilerMarker Competition = new ProfilerMarker("GeodesicBiology.CompetitionResolution");
    private static readonly ProfilerMarker Lifecycle = new ProfilerMarker("GeodesicBiology.LifecycleReproduction");
    private static readonly ProfilerMarker Movement = new ProfilerMarker("GeodesicBiology.Movement");
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
    public const float ReactionIntervalSeconds = 0.25f;
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
    private GeodesicOceanSuspendedField suspended;
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
    private int[] temperatureStamps = Array.Empty<int>();
    private int temperatureGeneration = 1;
    private double cachedOceanTemperatureTick = double.NaN;
    private float[] lightByNode = Array.Empty<float>();
    private float[] oxygenByNode = Array.Empty<float>();
    private float[] organicCarbonByNode = Array.Empty<float>();
    private int generation = 1;
    private int touchedCount;
    private System.Random reproductionRandom;
    private System.Random temperatureMutationRandom;
    private GeodesicBiologyTemperatureSettings temperatureSettings = GeodesicBiologyTemperatureSettings.Default;
    private GeodesicBiologyTemperatureTelemetry temperatureTelemetry;
    private HashSet<int> reportOccupiedNodes;
    private GeodesicBiologyDiagnostics accountingDiagnostics;
    private readonly long[] requestedByMetabolism = new long[Enum.GetValues(typeof(MetabolismType)).Length];
    private readonly long[] achievedByMetabolism = new long[Enum.GetValues(typeof(MetabolismType)).Length];
    private readonly double[] extentByMetabolism = new double[Enum.GetValues(typeof(MetabolismType)).Length];
    private readonly double[] energyByMetabolism = new double[Enum.GetValues(typeof(MetabolismType)).Length];
    private long births;
    private long deaths;
    private long starvationDeaths;
    private long lifespanDeaths;
    private readonly long[] recycledDeathsByCause = new long[Enum.GetValues(typeof(DeathCause)).Length];
    private readonly double[] deathCarbonByCause = new double[Enum.GetValues(typeof(DeathCause)).Length];
    private readonly double[] deathDissolvedCarbonByLayer = new double[GeodesicOceanLayerGrid.AbsoluteMaximumLayerCount];
    private double deathCarbonTotal, deathDissolvedCarbon, unplacedDeathCarbon, deathTransferRoundingResidual;
    private long invalidDeathCarbonStates;
    private int lastDeathCell = -1, lastDeathLayer = -1;
    private DeathCause lastRecycledDeathCause;
    private double lastDeathCarbon, lastDeathDissolvedCarbon;
    private long zeroAchieved;
    private long resourceLimited;
    private long hydrogenPrecisionRejected;
    private long invalidHabitats;
    private long invalidBiologicalStates;
    private double maintenancePaid;
    private double diagnosticElapsed;
    private double nextDiagnosticReportRealTime;
    private bool biologyReportsEnabled;
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
    private ReplicatorVisualGrowthSettings visualGrowthSettings = ReplicatorVisualGrowthSettings.Default;
    public long VisualStateCorrections { get; private set; }
    public long ReplacedDivisionTransitions { get; private set; }
    public long ConstrainedDaughterSeparations { get; private set; }
    public long FailedDaughterSeparations { get; private set; }

    public void SetVisualGrowthSettings(ReplicatorVisualGrowthSettings settings) => visualGrowthSettings = settings.Sanitized();
    public void ResetVisualDiagnostics() => VisualStateCorrections = ReplacedDivisionTransitions = ConstrainedDaughterSeparations = FailedDaughterSeparations = 0;

    public long BiologySteps => biologySteps;
    public long AgentEvaluations => agentEvaluations;
    public long HabitatSamples => habitatSamples;
    public long OceanTemperatureReads => oceanTemperatureReads;
    public long PhotosyntheticLightReads => photosyntheticLightReads;
    public long ResourceInventoryReads => resourceInventoryReads;
    public long CompetitionPairs => competitionPairs;
    public float LastBiologyStepSeconds => lastBiologyStepSeconds;
    public float LastMovementKinematicsSeconds { get; private set; }
    public GeodesicOceanLayerGrid MovementGrid => grid;
    public static MetabolismType NormalFounderMetabolism => MetabolismType.Hydrogenotrophy;
    public static LocomotionType NormalFounderLocomotion => LocomotionType.PassiveDrift;

    private GeodesicBiologyAvailability availability = GeodesicBiologyAvailability.Default;

    private struct Request
    {
        public int Node;
        public int Cell;
        public int Layer;
        public GeodesicOceanResource A, B, Product;
        public MetabolismType Metabolism;
        public double NeedA, NeedB, ProductCoefficient, Desired, Actual, CompetedActual;
        public float EnergyPerExtent, StorePerExtent;
        public bool SulfurProduct;
        public bool Supported;
        public float Temperature;
        public float TemperaturePerformance;
        public double AvailabilityFactor;
        // Ephemeral observations from this reaction commit, consumed before lifecycle swap-back.
        public float DiagnosticStoreBefore, DiagnosticStoreAfter;
        public double DiagnosticEnergyDelta;
        public double HydrogenCo2Ratio, HydrogenH2Ratio;
        public float HydrogenEnergyAwarded;
        public double FermentationInput, FermentationCO2, FermentationH2;
        public double PhotosyntheticCO2, PhotosyntheticO2;
        public double AerobicSubstrate, AerobicO2, AerobicCO2;
        public double MethanogenesisCO2, MethanogenesisH2, MethanogenesisCH4;
    }

    public void SetAccountingDiagnostics(GeodesicBiologyDiagnostics diagnostics) => accountingDiagnostics = diagnostics;

    public bool TryGetGrowthSnapshot(int index, List<Replicator> agents, ReplicatorPopulationState state,
        bool carbonDivision, float divisionCost, float replicationCost, float divisionMultiple,
        int maxPopulation, float reproductionRate, float stepSeconds, out GeodesicGrowthSnapshot snapshot)
    {
        snapshot = default;
        if (grid == null || state == null || agents == null || index < 0 || index >= state.Count || index >= agents.Count) return false;
        // External inspection can happen after an environmental tick, before the next biology step.
        RefreshTemperatureCache();
        BeginBiologicalThermalStep();
        float temperature = GetBiologicalTemperature(index,state);
        float performance = CalculateTemperaturePerformance(temperature, state.OptimalTempMin[index],
            state.OptimalTempMax[index], state.LethalTempMargin[index], temperatureSettings.DormancyFloor);
        snapshot = CaptureGrowthSnapshot(index, agents[index].DiagnosticId, state, temperature, performance,
            carbonDivision, divisionCost, replicationCost, divisionMultiple, maxPopulation, reproductionRate, stepSeconds);
        return true;
    }

    private GeodesicGrowthSnapshot CaptureGrowthSnapshot(int index, long id, ReplicatorPopulationState state,
        float temperature, float performance, bool carbonDivision, float divisionCost, float replicationCost,
        float divisionMultiple, int maxPopulation, float reproductionRate, float stepSeconds)
    {
        float requiredCarbon = Mathf.Max(1f, divisionMultiple) * state.BiomassTarget[index];
        float requiredEnergy = Mathf.Max(0f, carbonDivision ? divisionCost : replicationCost);
        ReplicatorVisualState visual = state.VisualGrowth[index];
        float visualProgress = ReplicatorVisualGrowth.Progress(visual, state.OrganicCStore[index], requiredCarbon);
        bool passiveVisual = state.Locomotion[index] != LocomotionType.Anchored;
        float visualDiameter = passiveVisual ? ReplicatorVisualGrowth.DisplayDiameter(visual, visualProgress, visualGrowthSettings) : 1f;
        var snapshot = new GeodesicGrowthSnapshot
        {
            organismId = id, packedIndex = index, cell = state.GeodesicCellIndex[index], layer = state.CurrentOceanLayerIndex[index],
            thermal = GetAgentTemperatureSample(index,state),
            optimalTempMin=state.OptimalTempMin[index], optimalTempMax=state.OptimalTempMax[index], lethalTempMargin=state.LethalTempMargin[index],
            coldExposureSeconds=state.ColdTemperatureExposureSeconds[index], heatExposureSeconds=state.HeatTemperatureExposureSeconds[index],
            accumulatingColdExposure=temperatureSettings.DirectDamageEnabled && temperatureSettings.ColdExposureDeathSeconds>0f && temperature<state.OptimalTempMin[index]-state.LethalTempMargin[index],
            accumulatingHeatExposure=temperatureSettings.DirectDamageEnabled && temperatureSettings.HeatExposureDeathSeconds>0f && temperature>state.OptimalTempMax[index]+state.LethalTempMargin[index],
            metabolism = state.Metabolism[index], organicC = state.OrganicCStore[index], biomassTarget = state.BiomassTarget[index],
            requiredCarbon = requiredCarbon, carbonProgress = requiredCarbon > 0f ? state.OrganicCStore[index] / requiredCarbon : float.NaN,
            energy = state.Energy[index], requiredEnergy = requiredEnergy, temperatureKelvin = temperature, temperaturePerformance = performance,
            probabilityPerStep = Mathf.Clamp01(reproductionRate * stepSeconds * performance), age = state.Age[index],
            visualSize = state.Size[index] * visualDiameter, matureSize = state.Size[index],
            visualDiameterMultiplier = visualDiameter, growthCycleCarbonBaseline = visual.carbonBaseline,
            cycleStartDiameterMultiplier = ReplicatorVisualGrowth.CycleStartDiameter(visual, visualGrowthSettings),
            visualGrowthProgress = visualProgress, isVisuallyDividing = passiveVisual && visual.IsDividing,
            divisionStartDiameterMultiplier = visual.divisionStartDiameterMultiplier,
            divisionAnimationProgress = ReplicatorVisualGrowth.DivisionBlend(visual),
            worldPosition = state.Position[index], carbonLimited = carbonDivision,
            validHabitat = grid.IsNodeActive(state.GeodesicCellIndex[index], state.CurrentOceanLayerIndex[index]),
            validState = float.IsFinite(state.Energy[index]) && float.IsFinite(state.OrganicCStore[index]) && state.OrganicCStore[index] >= 0f
                && float.IsFinite(requiredCarbon) && requiredCarbon > 0f && float.IsFinite(requiredEnergy)
                && float.IsFinite(temperature) && float.IsFinite(state.Age[index]) && float.IsFinite(state.MaxLifespan[index])
                && float.IsFinite(state.OptimalTempMin[index]) && float.IsFinite(state.OptimalTempMax[index])
                && state.OptimalTempMax[index] >= state.OptimalTempMin[index] && float.IsFinite(state.LethalTempMargin[index])
                && state.LethalTempMargin[index] > 0f && GeodesicBiologyDiagnostics.FinitePosition(state.Position[index]),
            pendingDeath = ClassifyLifecycleDeath(state.Energy[index], state.Age[index], state.MaxLifespan[index]) != DeathCause.Unknown
                || (temperatureSettings.DirectDamageEnabled
                    && ((temperatureSettings.ColdExposureDeathSeconds > 0f && state.ColdTemperatureExposureSeconds[index] >= temperatureSettings.ColdExposureDeathSeconds)
                        || (temperatureSettings.HeatExposureDeathSeconds > 0f && state.HeatTemperatureExposureSeconds[index] >= temperatureSettings.HeatExposureDeathSeconds))),
            carbonReady = !carbonDivision || state.OrganicCStore[index] >= requiredCarbon,
            energyReady = state.Energy[index] > requiredEnergy,
            temperatureReady = GeodesicBiologyTemperaturePolicy.CanReproduce(performance, temperatureSettings),
            belowPopulationCap = state.Count < maxPopulation, packedCanReplicate = state.CanReplicate[index]
        };
        snapshot.Classify();
        return snapshot;
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
        Vector2 hydrogenTemperatureRange, float lethalTemperatureMargin,
        float founderCo2Requirement=.01f,float founderH2Requirement=.02f,float founderReproductionMinEfficiency=.15f,float founderDormancyFloor=0f,
        bool logBiologyReports = false, GeodesicBiologyAvailability? startupAvailability = null)
    {
        Clear();
        SetBiologyReportsEnabled(logBiologyReports);
        planet = generator;
        resources = generator != null ? generator.GetComponent<GeodesicOceanResourceField>() : null;
        suspended = resources != null ? resources.SuspendedMinerals : null;
        oceanTemperature = generator != null ? generator.GetComponent<GeodesicOceanTemperatureField>() : null;
        experiencedTemperature = generator != null ? generator.GetComponent<GeodesicExperiencedTemperatureField>() : null;
        surfaceTemperature = generator != null ? generator.GetComponent<GeodesicSurfaceTemperatureField>() : null;
        grid = resources != null ? resources.SourceGrid : null;
        if (grid == null || !resources.IsInitialized || suspended == null || !suspended.IsInitialized || oceanTemperature == null || !oceanTemperature.IsInitialized
            || experiencedTemperature == null || !experiencedTemperature.IsInitialized)
        {
            Debug.LogError("[GeodesicBiology] Required layered ocean/resource/temperature authority is not initialized.");
            Clear();
            return false;
        }

        availability = (startupAvailability ?? GeodesicBiologyAvailability.Default).Normalized();
        bool allowFounders = availability.AllowsFounders;
        demand = new double[grid.NodeCapacity * ResourceCount];
        availabilityFactor = new double[grid.NodeCapacity * ResourceCount];
        delta = new double[grid.NodeCapacity * ResourceCount];
        touched = new int[grid.NodeCapacity];
        stamps = new int[grid.NodeCapacity];
        lightStamps = new int[grid.NodeCapacity];
        temperatureByNode = new float[grid.NodeCapacity];
        temperatureStamps = new int[grid.NodeCapacity];
        lightByNode = new float[grid.NodeCapacity];
        oxygenByNode = new float[grid.NodeCapacity];
        requests = new Request[allowFounders ? Mathf.Max(1, requestedFounders) : 1];
        biologySeed = PlanetSeedUtility.DeriveSeed(generator.randomSeed, PlanetSeedDomain.Biology, PlanetGenerator.GenerationVersion);
        founderMinLife = minLife; founderMaxLife = maxLife; founderColor = color;
        founderBiomassTarget = biomassTarget; founderHydrogenTemperatureRange = hydrogenTemperatureRange;
        founderLethalMargin = lethalTemperatureMargin;
        founderCO2Need=founderCo2Requirement; founderH2Need=founderH2Requirement;
        temperatureSettings.ReplicationMinEfficiency=founderReproductionMinEfficiency;
        temperatureSettings.DormancyFloor=founderDormancyFloor;
        var random = new System.Random(biologySeed);
        var visualRandom = new System.Random(biologySeed ^ 0x3419A7D);
        reproductionRandom = new System.Random(biologySeed ^ 0x5EED123);
        temperatureMutationRandom = new System.Random(biologySeed ^ 0x54454D50);
        locomotionMutationRandom = new System.Random(biologySeed ^ 0x4C4F434F);
        if (allowFounders)
        {
            EnsureFounderVentCatalog();
            SpawnFounders(requestedFounders, random, visualRandom, agents, state, minLife, maxLife, color, biomassTarget,
                hydrogenTemperatureRange, lethalTemperatureMargin, false);
        }
        return true;
    }

    public int SpawnDeferredFounders(int requestedFounders, List<Replicator> agents,
        ReplicatorPopulationState state, double spawnSimulationTime, int maximumPopulation = int.MaxValue)
    {
        if (grid == null || requestedFounders <= 0 || !availability.AllowsFounders) return 0;
        int allowed = Math.Min(requestedFounders,Math.Max(0,Math.Max(0,maximumPopulation)-state.Count));
        RecordCappedFounderEvents(requestedFounders-allowed);
        requestedFounders = allowed;
        emergencePlacementTime = emergenceScheduledTime = spawnSimulationTime;
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
        emergenceAttempts += Math.Max(0, count);
        int validVents = 0, invalidVents = 0;
        double totalVentWeight = 0d;
        for (int i = 0; i < resources.CompactOutletCount; i++)
        {
            if (!resources.TryGetVentOutlet(i, out GeodesicVentSourceOutlet raw) || raw.Habitat != GeodesicVentHabitat.Submarine) continue;
            if (!TryResolveFounderVent(i, out GeodesicVentSourceOutlet outlet, out _, out _)) { invalidVents++; continue; }
            validVents++;
            totalVentWeight += GetFounderVentWeight(outlet);
        }
        if (count > 0 && validVents == 0) { emergenceRejectedHabitat += count; Debug.LogWarning("[GeodesicBiology] No valid submarine vent habitat; no founders were spawned."); return 0; }
        int firstFounder = state.Count, spawnedCount=0, bottomMatches = 0, exactVentCells = 0, visualCellMismatches = 0, visualLayerMismatches = 0;
        var usedVentCells = biologyReportsEnabled ? new HashSet<int>() : null;
        var layerHistogram = biologyReportsEnabled ? new int[grid.MaximumLayerCount] : null;
        for (int i = 0; i < count; i++)
        {
            double pick = random.NextDouble() * totalVentWeight, cumulative = 0d; GeodesicVentSourceOutlet selected = default;
            for (int v = 0; v < resources.CompactOutletCount; v++)
            {
                if (!TryResolveFounderVent(v, out var candidate, out _, out _)) continue;
                selected = candidate;
                cumulative += GetFounderVentWeight(candidate);
                if (pick < cumulative) break;
            }
            int packed = SpawnFounderAtVent(selected, random, visualRandom, agents, state,
                minLife, maxLife, color, biomassTarget, hydrogenTemperatureRange, lethalTemperatureMargin, forceZeroAge);
            if(packed<0) { emergenceRejectedHabitat++; continue; }
            spawnedCount++;
            if (!biologyReportsEnabled) continue;
            int sourceCell = state.GeodesicCellIndex[packed];
            int bottomLayer = state.CurrentOceanLayerIndex[packed];
            float layerRadius = grid.LayerCenterRadius[grid.GetNodeIndex(sourceCell,bottomLayer)];
            Vector3 visualLocalPosition = planet.transform.InverseTransformPoint(state.Position[packed]);
            usedVentCells.Add(selected.CellIndex);
            if (state.GeodesicCellIndex[packed] == selected.CellIndex) exactVentCells++;
            if (state.CurrentOceanLayerIndex[packed] == grid.GetBottomLayerIndex(state.GeodesicCellIndex[packed])) bottomMatches++;
            layerHistogram[bottomLayer]++;
            if (!DirectionInsideCell(sourceCell,visualLocalPosition.normalized,grid.SourceTopology)) visualCellMismatches++;
            if (Mathf.Abs(visualLocalPosition.magnitude - layerRadius) > 1e-4f) visualLayerMismatches++;

        }
        if (biologyReportsEnabled)
            Debug.Log($"[GeodesicFounderSpawn] seed={biologySeed} requested={count} spawned={spawnedCount} selectedSubmarineOutlets={usedVentCells.Count} actualBottomLayer={bottomMatches}/{spawnedCount} remainedInSourceCell={exactVentCells}/{spawnedCount} invalidVentCandidates={invalidVents} visualCellMismatch={visualCellMismatches} visualLayerMismatch={visualLayerMismatches} placement=bounded-local-thermal-and-food-sampling layers={FormatLayerHistogram(layerHistogram)} packedRange={firstFounder}-{state.Count-1}");
        return spawnedCount;
    }

    private int SpawnFounderAtVent(GeodesicVentSourceOutlet selected, System.Random random, System.Random visualRandom,
        List<Replicator> agents, ReplicatorPopulationState state, float minLife, float maxLife, Color color,
        float biomassTarget, Vector2 hydrogenTemperatureRange, float lethalTemperatureMargin, bool forceZeroAge)
    {
        if(!TryFindFounderPosition(selected,out var placement)) return -1;
        int packed = SpawnPassiveFounder(placement, random, agents, state, minLife, maxLife, color,
            biomassTarget, hydrogenTemperatureRange, lethalTemperatureMargin, forceZeroAge, NormalFounderMetabolism);
        RegisterFounderPlacement(agents[packed].DiagnosticId, selected);
        return packed;
    }

    private int SpawnPassiveFounder(FounderPosition placement, System.Random random,
        List<Replicator> agents, ReplicatorPopulationState state, float minLife, float maxLife,
        Color color, float biomassTarget, Vector2 temperatureRange, float lethalTemperatureMargin,
        bool forceZeroAge, MetabolismType metabolism)
    {
        int sourceCell = placement.cell, layer = placement.layer;
        float layerRadius = grid.LayerCenterRadius[grid.GetNodeIndex(sourceCell, layer)];
        Vector3 visualLocalPosition = placement.local;
        Vector3 position = planet.transform.TransformPoint(visualLocalPosition);
        float life = Mathf.Lerp(minLife, maxLife, (float)random.NextDouble());
        var agent = new Replicator(position, Quaternion.FromToRotation(Vector3.up, visualLocalPosition.normalized), life, color,
            new Replicator.Traits(true, true, true, 0f), (float)random.NextDouble(), metabolism, NormalFounderLocomotion);
        agent.currentDirection=planet.transform.TransformDirection(visualLocalPosition.normalized).normalized;
        agent.moveDirection=agent.currentDirection;
        agent.rotation=Quaternion.FromToRotation(Vector3.up,agent.currentDirection);
        float initialEnergy = Mathf.Lerp(0.1f, 0.5f, (float)random.NextDouble());
        float sampledAge = Mathf.Lerp(0f, life * 0.5f, (float)random.NextDouble());
        InitializeFounderBiologicalState(agent, sourceCell, layer, temperatureRange,
            lethalTemperatureMargin, initialEnergy, forceZeroAge ? 0f : sampledAge, biomassTarget);
        agents.Add(agent);
        state.AddAgentFromReplicatorData(agent);
        int packed = state.Count - 1;
        // Packed state is authoritative. Write both habitat fields explicitly before any movement or sampling.
        state.GeodesicCellIndex[packed] = sourceCell;
        state.CurrentOceanLayerIndex[packed] = layer;
        state.PreferredOceanLayerIndex[packed] = layer;
        state.PassiveVisualRadius[packed] = layerRadius;
        uint seed = MovementSeedBits(state.MovementSeed[packed]);
        state.PassiveDriftDirection[packed] = visualLocalPosition.normalized; // planet-local
        state.PassiveDriftTangent[packed] = CreateInitialPassiveTangent(state.PassiveDriftDirection[packed], seed);
        state.Position[packed] = position; // world-space
        return packed;
    }

    private bool TryResolveFounderVent(int outletIndex, out GeodesicVentSourceOutlet outlet, out int sourceCell, out int sourceLayer)
    {
        if (!resources.TryGetVentInjectionHabitat(outletIndex, out outlet, out sourceCell, out sourceLayer)
            || outlet.Habitat != GeodesicVentHabitat.Submarine) return false;
        int bottom = GetValidVentBottomLayer(sourceCell, grid.ActiveLayerCountByCell);
        bool consistent = outlet.CellIndex == sourceCell && sourceLayer == bottom;
        Debug.Assert(consistent, $"Vent outlet {outletIndex} disagrees with injection node: CellIndex={outlet.CellIndex}, sourceCell={sourceCell}, sourceLayer={sourceLayer}, bottom={bottom}.");
        return consistent;
    }

    private float GetMinimumNeighborSpacingRadians(int cellIndex)
    {
        GeodesicGridTopology topology = grid.SourceTopology;
        int count = topology.NeighborCounts[cellIndex];
        if (count <= 0) return 0f;
        float minimum = float.PositiveInfinity;
        int offset = cellIndex * 6;
        for (int i = 0; i < count; i++) minimum = Mathf.Min(minimum, topology.NeighborAngularDistances6[offset + i]);
        return float.IsFinite(minimum) ? minimum : 0f;
    }

    public static int GetValidVentBottomLayer(int cellIndex, byte[] activeLayerCounts)
    => activeLayerCounts != null
       && cellIndex >= 0
       && cellIndex < activeLayerCounts.Length
       && activeLayerCounts[cellIndex] > 0
        ? activeLayerCounts[cellIndex] - 1
        : -1;

    public static int GetValidVentBottomLayer(int cellIndex, int[] activeLayerCounts)
        => activeLayerCounts != null
           && cellIndex >= 0
           && cellIndex < activeLayerCounts.Length
           && activeLayerCounts[cellIndex] > 0
            ? activeLayerCounts[cellIndex] - 1
            : -1;

    public static double GetFounderVentWeight(GeodesicVentSourceOutlet outlet)
    {
        double weight = (double)outlet.SystemBudgetWeight * outlet.WithinSystemWeight;
        return double.IsFinite(weight) && weight > 0d ? weight : double.Epsilon;
    }

    public static string FormatLayerHistogram(int[] histogram)
    {
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < histogram.Length; i++) { if (i > 0) text.Append('/'); text.Append("layer").Append(i).Append('=').Append(histogram[i]); }
        return text.ToString();
    }

    public void SetBiologyReportsEnabled(bool enabled) => biologyReportsEnabled = enabled;

    public void Step(float dt, List<Replicator> agents, ReplicatorPopulationState state, float maintenance,
        float hydrogenCo2, float hydrogenH2, float hydrogenEnergy, float hydrogenStoreFraction,
        bool hydrogenO2InhibitionEnabled, float o2ComfortMax, float o2StressMax, float hydrogenMinimumO2Efficiency,
        float sulfurCo2, float sulfurH2s, float sulfurEnergy, float methaneCo2, float methaneH2, float methaneEnergy,
        float methanotrophyCh4, float methanotrophyO2, float methanotrophyEnergy, float photoCo2, float photoEnergy,
        float maxStore, float reproductionRate, bool carbonDivision, float divisionCost, float replicationCost,
        float divisionMultiple, float childSplit, int maxPopulation, Action<MetabolismType, DeathCause> registerDeathCause,
        GeodesicBiologyTemperatureSettings? configuredTemperatureSettings = null)
    {
        temperatureSettings = configuredTemperatureSettings ?? GeodesicBiologyTemperatureSettings.Default;
        SetMethanogenesisRecipe(methaneCo2, methaneH2, methaneEnergy, o2ComfortMax, o2StressMax);
        SetMethanotrophyRecipe(methanotrophyCh4, methanotrophyO2, methanotrophyEnergy);
        if ((biologyReportsEnabled || chemotaxisABDiagnosticsEnabled) && temperatureTelemetry == null)
        {
            temperatureTelemetry = new GeodesicBiologyTemperatureTelemetry();
            temperatureTelemetry.Initialize(grid, resources, biologySeed);
        }
        AdvanceChemotaxisABStep(dt);
        bool observeAccounting = accountingDiagnostics != null && accountingDiagnostics.Enabled;
        if (observeAccounting) accountingDiagnostics.AdvanceObservedTime(dt);
        RefreshTemperatureCache();
        BeginBiologicalThermalStep();
        if (biologyReportsEnabled && !hydrogenotrophyConfigLogged)
        {
            Debug.Log(DescribeResolvedFermentationControls(o2ComfortMax,o2StressMax,maxStore));
            Debug.Log(DescribeResolvedSulfurControls(sulfurCo2, sulfurH2s, sulfurEnergy, o2ComfortMax, o2StressMax));
            Debug.Log($"[GeodesicHydrogenotrophyConfig] H2PerTick={hydrogenH2:G6} CO2PerTick={hydrogenCo2:G6} EnergyPerCompletedTick={hydrogenEnergy:G6} WholeReactionActivityFraction={hydrogenStoreFraction:G6} CarbonAssimilationOfWithdrawnCO2=1 capacityLimited=true atomicMeasuredInputs=true BasalEnergyCostPerSecond={maintenance:G6} ReferenceTickSeconds={ReferenceTickSeconds:G6}");
            Debug.Log($"[GeodesicTemperatureConfig] authority=positionDependentExperiencedTemperature coarseCache=cellLayer biologicalCache=organismPosition dormancyFloor={temperatureSettings.DormancyFloor:G6} reproductionMinEfficiency={temperatureSettings.ReplicationMinEfficiency:G6} reproductionChance=rate*dt*performance directDamage={temperatureSettings.DirectDamageEnabled} coldExposureSeconds={temperatureSettings.ColdExposureDeathSeconds:G6} heatExposureSeconds={temperatureSettings.HeatExposureDeathSeconds:G6} recoveryPerSecond={temperatureSettings.ExposureRecoveryPerSecond:G6} extraStressMaintenanceMultiplier={temperatureSettings.StressMaintenanceMultiplier:G6} mutationChance={temperatureSettings.MutationChance:G6} mutationScaleK={temperatureSettings.MutationScale:G6} divisionEnergy=conservative-half-split");
            hydrogenotrophyConfigLogged = true;
        }
        biologySteps++;
        lastBiologyStepSeconds = dt;
        LastMovementKinematicsSeconds = 0f;
        if (pendingMethanogenFounders > 0) TrySpawnMethanogenFounders(agents, state, maxPopulation);
        if (pendingMethanotrophFounders > 0) TrySpawnMethanotrophFounders(agents, state, maxPopulation);
        if (agents.Count == 0)
        {
            MaybeLogDiagnostics(dt, state, agents, carbonDivision, divisionCost, replicationCost,
                divisionMultiple, maxPopulation, reproductionRate, observeAccounting);
            return;
        }
        state.EnsureMatchesAgentCount(agents);
        SetHydrogenotrophMovementInputs(hydrogenCo2, hydrogenH2, hydrogenO2InhibitionEnabled,
            o2ComfortMax, o2StressMax, hydrogenMinimumO2Efficiency);
        movementSulfurCO2 = sulfurCo2; movementSulfurH2S = sulfurH2s;
        using (Movement.Auto()) RunGeodesicMovement(dt, agents, state);
        float reactionDt = ConsumeFixedInterval(ref reactionAccumulator, dt, ReactionIntervalSeconds);
        if (reactionDt > 0f)
        {
            EnsureRequestCapacity(agents.Count); BeginSparseStep();
            using (Evaluation.Auto())
            {
                using (HabitatSampling.Auto()) SampleOccupiedHabitats(state.Count, state);
                using (RequestBuild.Auto())
                    for (int i = 0; i < state.Count; i++) BuildRequest(i, state, reactionDt,
                        hydrogenCo2, hydrogenH2, hydrogenEnergy, hydrogenStoreFraction, hydrogenO2InhibitionEnabled,
                        o2ComfortMax, o2StressMax, hydrogenMinimumO2Efficiency, sulfurCo2, sulfurH2s, sulfurEnergy,
                        photoCo2, photoEnergy, maxStore);
                using (DemandAggregation.Auto()) AggregateDemands(state.Count);
                using (AvailabilityResolution.Auto()) ResolveAvailabilityFactors();
                using (Competition.Auto()) ResolveCompetition(state.Count);
                if (observeAccounting) PrepareAccountingScratch();
                CommitRequests(state.Count, state, maxStore, photoEnergy, observeAccounting);
            }
        }
        using (Lifecycle.Auto())
        using (MaintenanceAndLifecycle.Auto()) RunLifecycle(dt, agents, state, maintenance, reproductionRate, carbonDivision, divisionCost, replicationCost, divisionMultiple, childSplit, maxPopulation, registerDeathCause, observeAccounting && reactionDt > 0f, observeAccounting);
        if (pendingSaprotrophFounders > 0) SpawnSaprotrophFounders(agents, state, maxPopulation);
        MaybeLogDiagnostics(dt, state, agents, carbonDivision, divisionCost, replicationCost,
            divisionMultiple, maxPopulation, reproductionRate, observeAccounting);
    }

    private void BuildRequest(int i, ReplicatorPopulationState state, float dt,
        float hCo2, float hH2, float hEnergy, float hStoreFraction, bool hO2InhibitionEnabled,
        float o2ComfortMax, float o2StressMax, float hMinimumO2Efficiency,
        float sCo2, float sH2s, float sEnergy,
        float pCo2, float pEnergy, float maximumStore)
    {
        agentEvaluations++;
        int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i]; Request r = default; r.Cell = cell; r.Layer = layer; r.Metabolism = state.Metabolism[i];
        if (!grid.IsNodeActive(cell, layer)) { invalidHabitats++; requests[i] = r; return; }
        r.Node = grid.GetNodeIndex(cell, layer); float scale = dt / ReferenceTickSeconds;
        switch (state.Metabolism[i])
        {
            case MetabolismType.Hydrogenotrophy: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2; r.NeedA=hCo2; r.NeedB=hH2; r.EnergyPerExtent=hEnergy; r.StorePerExtent=hCo2; break;
            case MetabolismType.SulfurChemosynthesis: r.A=GeodesicOceanResource.CO2; r.B=GeodesicOceanResource.H2S; r.NeedA=sCo2; r.NeedB=sH2s; r.EnergyPerExtent=sEnergy; r.StorePerExtent=sCo2; r.SulfurProduct=true; break;
            case MetabolismType.Fermentation: r.A=GeodesicOceanResource.OrganicC; r.B=r.A; r.NeedA=fermentationNeed; r.Product=GeodesicOceanResource.CO2; r.StorePerExtent=fermentationNeed*fermentationAssimilation; break;
            case MetabolismType.Saprotrophy:
                r.A = GeodesicOceanResource.OrganicC;
                r.B = GeodesicOceanResource.O2;
                r.Product = GeodesicOceanResource.CO2;
                break;
            case MetabolismType.Methanogenesis:
                r.A = GeodesicOceanResource.CO2;
                r.B = GeodesicOceanResource.H2;
                r.Product = GeodesicOceanResource.CH4;
                break;
            case MetabolismType.Methanotrophy:
                if (!availability.Allows(MetabolismType.Methanotrophy)) { requests[i] = r; return; }
                r.A = GeodesicOceanResource.CH4;
                r.B = GeodesicOceanResource.O2;
                r.Product = GeodesicOceanResource.CO2;
                break;
            case MetabolismType.Photosynthesis:
                float light = GetCachedPhotosyntheticLight(r.Node, cell, layer);
                r.A = GeodesicOceanResource.CO2;
                r.B = GeodesicOceanResource.CO2;
                r.Product = GeodesicOceanResource.O2;
                if (float.IsFinite(pCo2) && pCo2 > 0f && float.IsFinite(pEnergy) && pEnergy >= 0f)
                    r.NeedA = (double)pCo2 * light;
                break;
            default: requests[i]=r; return;
        }
        r.Supported = true;
        EnsureHabitatSample(r.Node, cell, layer);
        float temperature = GetBiologicalTemperature(i,state);
        float optimumMin = state.OptimalTempMin[i], optimumMax = state.OptimalTempMax[i];
        float temperatureScale = CalculateTemperaturePerformance(temperature, optimumMin, optimumMax,
            state.LethalTempMargin[i], temperatureSettings.DormancyFloor);
        float oxygenEfficiency = state.Metabolism[i] == MetabolismType.Hydrogenotrophy && hO2InhibitionEnabled
            ? CalculateAnaerobeO2Efficiency(oxygenByNode[r.Node], o2ComfortMax, o2StressMax, hMinimumO2Efficiency)
            : state.Metabolism[i] == MetabolismType.SulfurChemosynthesis && sulfurO2Inhibition
                ? CalculateAnaerobeO2Efficiency(oxygenByNode[r.Node], o2ComfortMax, o2StressMax, sulfurO2Floor)
                : r.Metabolism == MetabolismType.Fermentation && fermentationO2Inhibition
                    ? CalculateAnaerobeO2Efficiency(oxygenByNode[r.Node],o2ComfortMax,o2StressMax,fermentationO2Floor) : 1f;
        if (r.Metabolism == MetabolismType.SulfurChemosynthesis)
        {
            sulfurO2Samples++; if (oxygenEfficiency < 1f) sulfurInhibitedSamples++;
            sulfurO2Sum += oxygenByNode[r.Node]; sulfurEfficiencySum += oxygenEfficiency;
        }
        r.Temperature = temperature;
        r.TemperaturePerformance = temperatureScale;
        if (biologyReportsEnabled) AccumulateTemperature(temperature, temperatureScale);
        if (!float.IsFinite(state.Energy[i]) || !float.IsFinite(state.OrganicCStore[i]) || !float.IsFinite(temperature)) invalidBiologicalStates++;
        r.Desired=Math.Max(0,scale*temperatureScale*oxygenEfficiency);
        if(r.Metabolism == MetabolismType.Fermentation) { r.Desired=GeodesicFermentationBalance.LimitExtent(r.Desired,r.NeedA); ObserveFermentationRequest(r,oxygenEfficiency); }
        if (r.Metabolism == MetabolismType.Hydrogenotrophy)
            r.Desired = GeodesicHydrogenotrophyBalance.LimitExtent(r.Desired * Mathf.Clamp01(hStoreFraction),
                r.NeedA, r.NeedB, state.OrganicCStore[i], maximumStore);
        else if (r.Metabolism == MetabolismType.SulfurChemosynthesis)
            r.Desired = GeodesicHydrogenotrophyBalance.LimitExtent(r.Desired * sulfurActivity,
                r.NeedA, r.NeedB, state.OrganicCStore[i], maximumStore);
        else if (r.Metabolism == MetabolismType.Photosynthesis)
            r.Desired = LimitPhotosyntheticExtent(r.Desired, r.NeedA, state.OrganicCStore[i], maximumStore, state.Energy[i]);
        else if (r.Metabolism == MetabolismType.Saprotrophy)
            BuildSaprotrophyDemand(ref r, state.OrganicCStore[i], maximumStore, state.Energy[i]);
        else if (r.Metabolism == MetabolismType.Methanogenesis)
            BuildMethanogenesisDemand(ref r, state.OrganicCStore[i], maximumStore, state.Energy[i]);
        else if (r.Metabolism == MetabolismType.Methanotrophy)
            BuildMethanotrophyDemand(ref r, state.OrganicCStore[i], maximumStore, state.Energy[i]);
        requests[i]=r;
    }

    private void SampleOccupiedHabitats(int count, ReplicatorPopulationState state)
    {
        for (int i = 0; i < count; i++)
        {
            int cell = state.GeodesicCellIndex[i];
            int layer = state.CurrentOceanLayerIndex[i];
            if (grid.IsNodeActive(cell, layer)) EnsureHabitatSample(grid.GetNodeIndex(cell, layer), cell, layer);
        }
    }

    private void AggregateDemands(int count)
    {
        for (int i = 0; i < count; i++)
        {
            ref Request r = ref requests[i];
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
            ref Request r = ref requests[i];
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
            r.CompetedActual = r.Actual;
            if (factor < 1d && r.Desired > 0) resourceLimited++;
            if (r.Actual > 0) achievedByMetabolism[metabolism]++; else zeroAchieved++;
        }
    }
    private void PrepareAccountingScratch()
    {
        // Competition has finished reading these buffers. Reuse only touched scratch pairs,
        // not another ocean-sized allocation: gross input/product now, measured ratio after commit.
        for (int t = 0; t < touchedCount; t++)
        for (int k = 0; k < ResourceCount; k++)
        {
            int scratch = k * grid.NodeCapacity + touched[t];
            demand[scratch] = availabilityFactor[scratch] = 0d;
        }
    }

    private void CommitRequests(int count, ReplicatorPopulationState state, float maxStore, float photoEnergy, bool observeAccounting)
    {
        using (AgentEnergyAndStores.Auto())
        for (int i = 0; i < count; i++)
        {
            ref Request r = ref requests[i]; if (r.Actual <= 0) continue;
            if (r.Metabolism == MetabolismType.Hydrogenotrophy)
            {
                CommitHydrogenotrophy(i, state, maxStore, observeAccounting);
                continue;
            }
            if (r.Metabolism == MetabolismType.SulfurChemosynthesis)
            {
                CommitSulfur(i, state, maxStore, observeAccounting);
                continue;
            }
            if (r.Metabolism == MetabolismType.Fermentation) { CommitFermentation(i,state,maxStore,observeAccounting); continue; }
            if (r.Metabolism == MetabolismType.Saprotrophy)
            {
                CommitAerobicCarbon(i, state, maxStore, saprotrophyOxygenYield, 0d, saprotrophyEnergyYield, observeAccounting);
                continue;
            }
            if (r.Metabolism == MetabolismType.Methanotrophy)
            {
                CommitAerobicCarbon(i, state, maxStore, MethanotrophyOxygenPerOxidizedCarbon,
                    MethanotrophyOxygenPerStoredCarbon, methanotrophyEnergyPerOxidizedCarbon, observeAccounting);
                continue;
            }
            if (r.Metabolism == MetabolismType.Methanogenesis)
            {
                CommitMethanogenesis(i, state, maxStore, observeAccounting);
                continue;
            }
            if (r.Metabolism == MetabolismType.Photosynthesis)
            {
                CommitPhotosynthesis(i, state, maxStore, photoEnergy, observeAccounting);
                continue;
            }
            float energyBefore = state.Energy[i];
            if (observeAccounting)
            {
                r.DiagnosticStoreBefore = state.OrganicCStore[i];
                demand[(int)r.A * grid.NodeCapacity + r.Node] += r.NeedA * r.Actual;
                if (r.NeedB > 0) demand[(int)r.B * grid.NodeCapacity + r.Node] += r.NeedB * r.Actual;
                if (r.ProductCoefficient > 0) availabilityFactor[(int)r.Product * grid.NodeCapacity + r.Node] += r.ProductCoefficient * r.Actual;
            }
            delta[(int)r.A*grid.NodeCapacity+r.Node] -= r.NeedA*r.Actual;
            if (r.NeedB > 0) delta[(int)r.B*grid.NodeCapacity+r.Node] -= r.NeedB*r.Actual;
            if (r.ProductCoefficient > 0) delta[(int)r.Product*grid.NodeCapacity+r.Node] += r.ProductCoefficient*r.Actual;
            if (r.SulfurProduct) suspended.AddPrecipitate(r.Cell,r.Layer,r.NeedB*r.Actual,0);
            float gained = r.EnergyPerExtent*(float)r.Actual;
            if (chemotaxisABDiagnosticsEnabled) RecordABMetabolicEnergy(state.Locomotion[i], gained);
            state.Energy[i] += gained;
            state.OrganicCStore[i] = Mathf.Min(maxStore,state.OrganicCStore[i]+r.StorePerExtent*(float)r.Actual);
            if (observeAccounting)
            {
                r.DiagnosticStoreAfter = state.OrganicCStore[i];
                r.DiagnosticEnergyDelta = (double)state.Energy[i] - energyBefore;
            }
            int metabolism=(int)state.Metabolism[i]; extentByMetabolism[metabolism]+=r.Actual; energyByMetabolism[metabolism]+=gained;
        }
        using (ReactionCommit.Auto())
        for (int t=0;t<touchedCount;t++)
        {
            int node=touched[t];
            for(int k=0;k<ResourceCount;k++)
            {
                double d=delta[k*grid.NodeCapacity+node];
                double actual = d != 0 ? resources.ApplyDirectExchangeInventory((GeodesicOceanResource)k,node,d) : 0d;
                if (observeAccounting)
                {
                    int scratch = k * grid.NodeCapacity + node;
                    bool mixed = demand[scratch] > 0d && availabilityFactor[scratch] > 0d;
                    if (d != 0d || mixed) accountingDiagnostics.RecordNodeExchange((GeodesicOceanResource)k, d, actual, mixed);
                    // A mixed input/product pair exposes only a net exchange, even when it is zero.
                    // Do not invent a measured gross consumption for its individual requests.
                    availabilityFactor[scratch] = mixed ? double.NaN : d != 0d ? actual / d : 1d;
                }
            }
        }
    }

    private void CommitHydrogenotrophy(int index, ReplicatorPopulationState state, float maximumStore, bool observeAccounting)
    {
        ref Request r = ref requests[index];
        float storeBefore = state.OrganicCStore[index], energyBefore = state.Energy[index];
        var transfer = resources.CommitHydrogenotrophy(r.Node, r.Actual, r.NeedA, r.NeedB,
            r.EnergyPerExtent, storeBefore, maximumStore);
        r.Actual = transfer.extent;
        if (transfer.status == GeodesicHydrogenotrophyBalance.CommitStatus.PrecisionRejected) hydrogenPrecisionRejected++;
        if (transfer.status == GeodesicHydrogenotrophyBalance.CommitStatus.Invalid) invalidBiologicalStates++;
        if (transfer.status != GeodesicHydrogenotrophyBalance.CommitStatus.Committed) return;

        // Both authoritative substrate writes succeeded before any biological credit.
        state.OrganicCStore[index] = transfer.storeAfter;
        state.Energy[index] += transfer.energy;
        if (chemotaxisABDiagnosticsEnabled) RecordABMetabolicEnergy(state.Locomotion[index], transfer.energy);
        extentByMetabolism[(int)r.Metabolism] += transfer.extent;
        energyByMetabolism[(int)r.Metabolism] += transfer.energy;
        if (!observeAccounting) return;
        r.DiagnosticStoreBefore = storeBefore; r.DiagnosticStoreAfter = transfer.storeAfter;
        r.DiagnosticEnergyDelta = (double)state.Energy[index] - energyBefore;
        r.HydrogenEnergyAwarded = transfer.energy;
        r.HydrogenCo2Ratio = transfer.co2Withdrawn / (r.NeedA * r.Actual);
        r.HydrogenH2Ratio = transfer.h2Withdrawn / (r.NeedB * r.Actual);
        accountingDiagnostics.RecordNodeExchange(r.A, -r.NeedA * r.Actual, -transfer.co2Withdrawn, false);
        accountingDiagnostics.RecordNodeExchange(r.B, -r.NeedB * r.Actual, -transfer.h2Withdrawn, false);
    }
    private void RunLifecycle(float dt, List<Replicator> agents, ReplicatorPopulationState state, float maintenance,
        float rate, bool carbon, float divisionCost, float replicationCost, float multiple, float split,
        int maxPopulation, Action<MetabolismType, DeathCause> registerDeathCause, bool recordReactions, bool observeAccounting)
    {
        bool observeEvolution = evolutionDiagnostics.Enabled;
        for (int i = state.Count - 1; i >= 0; i--)
        {
            if (observeEvolution)
                evolutionDiagnostics.Observe(i, agents[i].DiagnosticId, state.Locomotion[i], dt, abSimulationTime);
            if (chemotaxisABDiagnosticsEnabled) ObserveABOrganismTime(i, state, dt);
            // Original request indices still match here, before any reverse-loop swap-back or birth.
            // Fold accounting into this existing pass rather than scanning the population again.
            if (recordReactions) RecordReactionAccounting(i);
            ReplicatorVisualGrowth.AdvanceDivision(ref state.VisualGrowth[i], dt);
            state.Age[i] += dt;
            float temperature = GetBiologicalTemperature(i,state);
            float performance = CalculateTemperaturePerformance(temperature, state.OptimalTempMin[i],
                state.OptimalTempMax[i], state.LethalTempMargin[i], temperatureSettings.DormancyFloor);
            state.CanReplicate[i] = GeodesicBiologyTemperaturePolicy.CanReproduce(performance, temperatureSettings);
            DeathCause exposureCause = GeodesicBiologyTemperaturePolicy.AdvanceExposure(temperature,
                state.OptimalTempMin[i], state.OptimalTempMax[i], state.LethalTempMargin[i], dt, temperatureSettings,
                ref state.ColdTemperatureExposureSeconds[i], ref state.HeatTemperatureExposureSeconds[i]);
            float paid = Mathf.Max(0f, maintenance * dt) * GeodesicBiologyTemperaturePolicy.MaintenanceMultiplier(performance, temperatureSettings);
            state.Energy[i] -= paid;
            maintenancePaid += paid;
            if (metabolicColor != null)
                state.Color[i] = agents[i].color = metabolicColor(state.Age[i], state.MaxLifespan[i] - state.Age[i], state.Energy[i], state.Metabolism[i]);
            DeathCause cause = ClassifyLifecycleDeath(state.Energy[i], state.Age[i], state.MaxLifespan[i]);
            if (cause == DeathCause.Unknown) cause = exposureCause;
            if (cause != DeathCause.Unknown)
            {
                RecycleDeathInventory(i, agents[i], state, cause);
                RecordLocomotionDeath(state.Metabolism[i], state.Locomotion[i]);
                if (chemotaxisABDiagnosticsEnabled) RecordABDeath(state.Locomotion[i], cause);
                if (observeEvolution)
                    evolutionDiagnostics.Death(i, agents[i].DiagnosticId, state.Locomotion[i], abSimulationTime);
                RecordFounderDeath(agents[i].DiagnosticId);
                deaths++;
                if (state.Metabolism[i] == MetabolismType.SulfurChemosynthesis) sulfurDeaths++;
                if (state.Metabolism[i] == MetabolismType.Fermentation) fermentationDeaths++;
                if (cause == DeathCause.EnergyDepletion) starvationDeaths++;
                else if (cause == DeathCause.OldAge) lifespanDeaths++;
                if (biologyReportsEnabled) temperatureTelemetry.RecordDeath(cause);
                registerDeathCause?.Invoke(state.Metabolism[i], cause);
                if (observeEvolution) evolutionDiagnostics.SwapBackRemoval(i, state.Count - 1);
                RemoveAgentAtSwapBack(i, agents, state);
                continue;
            }

            float energyCost = Mathf.Max(0f, carbon ? divisionCost : replicationCost);
            bool eligible = state.Energy[i] > energyCost
                && (!carbon || state.OrganicCStore[i] >= Mathf.Max(1, multiple) * state.BiomassTarget[i]);
            if (!eligible || !state.CanReplicate[i] || agents.Count >= maxPopulation
                || reproductionRandom.NextDouble() >= Mathf.Clamp01(rate * dt * performance)) continue;
            Replicator parent = agents[i];
            // Capture presentation before the instantaneous biological transfer. No gate or RNG depends on this state.
            bool animatedDivision = state.Locomotion[i] != LocomotionType.Anchored;
            float divisionStartDiameter = 1f;
            Vector3 parentLocal = Vector3.zero, commonVisualLocal = Vector3.zero;
            if (animatedDivision)
            {
                ref ReplicatorVisualState parentVisual = ref state.VisualGrowth[i];
                if (ReplicatorVisualGrowth.Update(ref parentVisual, state.OrganicCStore[i],
                    Mathf.Max(1f, multiple) * state.BiomassTarget[i], visualGrowthSettings)) VisualStateCorrections++;
                divisionStartDiameter = parentVisual.diameterMultiplier;
                parentLocal = planet.transform.InverseTransformPoint(state.Position[i]);
                commonVisualLocal = ReplicatorVisualGrowth.ResolveLocalPosition(parentVisual, parentLocal);
                if (parentVisual.IsDividing) ReplacedDivisionTransitions++;
            }
            GeodesicGrowthSnapshot divisionBefore = observeAccounting
                ? CaptureGrowthSnapshot(i, parent.DiagnosticId, state, temperature, performance,
                    carbon, divisionCost, replicationCost, multiple, maxPopulation, rate, dt)
                : default;
            float childMovementSeed = (float)reproductionRandom.NextDouble();
            var child = new Replicator(state.Position[i], state.Rotation[i], state.MaxLifespan[i], parent.color, parent.traits,
                childMovementSeed, state.Metabolism[i], state.Locomotion[i], parent.locomotionSkill);
            child.locomotion = ResolveGeodesicInheritedLocomotion(state.Locomotion[i], state.GeodesicCellIndex[i],
                state.CurrentOceanLayerIndex[i], out var locomotionMutation);
            float childInitialEnergy = observeAccounting ? child.energy : 0f;
            float childInitialCarbon = observeAccounting ? child.organicCStore : 0f;
            child.geodesicCellIndex = state.GeodesicCellIndex[i];
            child.currentOceanLayerIndex = state.CurrentOceanLayerIndex[i];
            child.preferredOceanLayerIndex = state.CurrentOceanLayerIndex[i];
            child.biomassTarget = state.BiomassTarget[i];
            if (animatedDivision) child.size = state.Size[i];
            child.optimalTempMin = state.OptimalTempMin[i];
            child.optimalTempMax = state.OptimalTempMax[i];
            child.lethalTempMargin = state.LethalTempMargin[i];
            PlaceDaughter(i, child, state);
            MutateChildMetabolism(child, state.Metabolism[i], carbon ? state.OrganicCStore[i]*Mathf.Clamp01(split) : child.organicCStore);
            TryMutateMethanogenicChild(child, state.Metabolism[i]);
            TryMutateMethanotrophicChild(child, state.Metabolism[i]);
            // Legacy saprotrophy rolls independently and can supersede an earlier child metabolism.
            TryMutateSaprotrophicChild(child, state.Metabolism[i]);
            GeodesicBiologyTemperaturePolicy.MutateInheritedTraits(child, temperatureMutationRandom, temperatureSettings);
            SplitDivisionEnergy(state.Energy[i], energyCost, out float parentEnergy, out float childEnergy);
            state.Energy[i] = parentEnergy;
            child.energy = childEnergy;
            if (carbon) { child.organicCStore = state.OrganicCStore[i] * Mathf.Clamp01(split); state.OrganicCStore[i] -= child.organicCStore; }
            float childTemperature=SampleBiologicalTemperature(child.geodesicCellIndex,child.currentOceanLayerIndex,child.position).TemperatureKelvin;
            child.canReplicate=GeodesicBiologyTemperaturePolicy.CanReproduce(CalculateTemperaturePerformance(childTemperature,
                child.optimalTempMin,child.optimalTempMax,child.lethalTempMargin,temperatureSettings.DormancyFloor),temperatureSettings);
            if (child.locomotion != LocomotionType.PassiveDrift)
            {
                Vector3 birthNormal = planet.transform.InverseTransformPoint(child.position).normalized;
                Vector3 birthTangent = CreateInitialPassiveTangent(birthNormal, MovementSeedBits(child.movementSeed));
                child.currentDirection = planet.transform.TransformDirection(birthNormal).normalized;
                child.moveDirection = child.desiredMoveDir = planet.transform.TransformDirection(birthTangent).normalized;
                child.rotation = Quaternion.LookRotation(child.moveDirection, child.currentDirection);
            }
            agents.Add(child);
            state.AddAgentFromReplicatorData(child);
            int childIndex = state.Count - 1;
            if (observeEvolution)
                evolutionDiagnostics.Birth(i, parent.DiagnosticId, state.Locomotion[i], childIndex,
                    child.DiagnosticId, child.locomotion, abSimulationTime, locomotionMutation);
            if (observeEvolution && grid.IsNodeActive(child.geodesicCellIndex, child.currentOceanLayerIndex))
            {
                int birthNode = grid.GetNodeIndex(child.geodesicCellIndex, child.currentOceanLayerIndex);
                float radius = planet.transform.InverseTransformPoint(child.position).magnitude;
                // Observe the existing passive visual-layer interpolation asymmetry without
                // relocating a child or changing its inherited logical habitat.
                double outside = Math.Max(0d, Math.Max(grid.LayerInnerRadius[birthNode] - radius,
                    radius - grid.LayerOuterRadius[birthNode]));
                evolutionDiagnostics.ObserveBirthDepth(child.locomotion, grid.ToPhysicalDepthKm(outside));
            }
            // Initialize all newborn transport from the separated world position. Steering memory is not inherited.
            EnsurePassiveKinematicState(childIndex, state, planet.transform.worldToLocalMatrix);
            InitializePassiveSchedulesAtSpawn(state, childIndex, passiveMovementTime);
            if (animatedDivision)
            {
                Vector3 childLocal = planet.transform.InverseTransformPoint(state.Position[childIndex]);
                // Presentation observes the configured carbon allocation; no inventory or split is changed.
                // Energy-only reproduction has no carbon split to mirror, so retain equal visual volumes.
                float childVisualFraction = carbon && float.IsFinite(split) ? Mathf.Clamp01(split) : 0.5f;
                ReplicatorVisualGrowth.BeginDivision(ref state.VisualGrowth[i], state.OrganicCStore[i], divisionStartDiameter,
                    commonVisualLocal - parentLocal, parentLocal.normalized, visualGrowthSettings, 1f - childVisualFraction);
                ReplicatorVisualGrowth.BeginDivision(ref state.VisualGrowth[childIndex], state.OrganicCStore[childIndex], divisionStartDiameter,
                    commonVisualLocal - childLocal, childLocal.normalized, visualGrowthSettings, childVisualFraction);
            }
            if (observeAccounting) accountingDiagnostics.RecordDivision(divisionBefore, i, state.Count - 1,
                child.DiagnosticId, state, grid, split, childInitialEnergy, childInitialCarbon,
                planet.transform, visualGrowthSettings.daughterSeparationDistance);
            RecordFounderParentBirth(parent.DiagnosticId);
            births++;
            if (child.metabolism == MetabolismType.SulfurChemosynthesis) sulfurBirths++;
            if (child.metabolism == MetabolismType.Fermentation) fermentationBirths++;
            if (state.Metabolism[i] == MetabolismType.Fermentation && (uint)state.Locomotion[i] < 4u) fermentationParentBirths[(int)state.Locomotion[i]]++;
            if (state.Metabolism[i] == MetabolismType.SulfurChemosynthesis && (uint)state.Locomotion[i] < 4u)
                sulfurParentBirths[(int)state.Locomotion[i]]++;
            if (chemotaxisABDiagnosticsEnabled) RecordABBirth(state.Locomotion[i]);
            RecordLocomotionBirth(state.Metabolism[i], state.Locomotion[i]);
            if (biologyReportsEnabled) temperatureTelemetry.RecordBirth(performance, temperatureSettings);
        }
    }

    private void PlaceDaughter(int parentIndex, Replicator child, ReplicatorPopulationState state)
    {
        if (visualGrowthSettings.daughterSeparationDistance <= 0f) return;
        int cell = state.GeodesicCellIndex[parentIndex], layer = state.CurrentOceanLayerIndex[parentIndex];
        Vector3 local = planet.transform.InverseTransformPoint(state.Position[parentIndex]);
        float radius = local.magnitude;
        if (!grid.IsNodeActive(cell, layer) || !GeodesicBiologyDiagnostics.FinitePosition(local) || !float.IsFinite(radius) || !(radius > 0f))
        {
            FailedDaughterSeparations++;
            return;
        }
        Vector3 direction = local / radius;
        // Independent hash of the existing child seed: consumes no reproduction/movement random stream.
        Vector3 axis = CreateInitialPassiveTangent(direction, MovementSeedBits(child.movementSeed) ^ 0xD171510Fu);
        Vector3 side = Vector3.Cross(direction, axis);
        float safeAngle = Mathf.Min(0.1f, GetMinimumNeighborSpacingRadians(cell) * 0.1f);
        float requested = visualGrowthSettings.daughterSeparationDistance;
        Vector3 bestWorld = child.position;
        float bestDistance = 0f;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float turn = attempt * Mathf.PI * 0.25f;
            Vector3 tangent = axis * Mathf.Cos(turn) + side * Mathf.Sin(turn);
            float worldPerRadian = planet.transform.TransformVector(tangent * radius).magnitude;
            if (!(worldPerRadian > 0f)) continue;
            float angle = Mathf.Min(safeAngle, requested / worldPerRadian);
            for (int shrink = 0; shrink < 12 && angle > 1e-8f; shrink++, angle *= 0.5f)
            {
                Vector3 candidate = (direction * Mathf.Cos(angle) + tangent * Mathf.Sin(angle)).normalized;
                if (!DirectionInsideCell(cell, candidate, grid.SourceTopology)) continue;
                Vector3 world = planet.transform.TransformPoint(candidate * radius);
                float distance = Vector3.Distance(world, state.Position[parentIndex]);
                if (distance > requested + 1e-6f || distance < 1e-7f) continue;
                if (distance > bestDistance) { bestWorld = world; bestDistance = distance; }
                break;
            }
        }
        if (bestDistance > 0f)
        {
            child.position = bestWorld;
            child.currentDirection = bestWorld.normalized;
            if (bestDistance < requested * 0.95f) ConstrainedDaughterSeparations++;
            return;
        }
        // Never repair a bad pre-existing position by relocating a birth across the planet.
        // A very narrow/invalid cell can forgo displacement; normal passive motion still initializes.
        FailedDaughterSeparations++;
    }

    internal static bool DirectionInsideCell(int cell, Vector3 direction, GeodesicGridTopology topology)
    {
        int offset = cell * 6;
        for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
        {
            int neighbor = topology.Neighbors6[offset + slot];
            if (neighbor >= 0 && Vector3.Dot(direction, topology.CellDirections[cell] - topology.CellDirections[neighbor]) < -1e-8f)
                return false;
        }
        return true;
    }

    private void RecordReactionAccounting(int index)
    {
        ref Request r = ref requests[index];
        if (!r.Supported) return;
        if (r.Metabolism == MetabolismType.Fermentation) { RecordFermentationAccounting(r); return; }
        if (r.Metabolism == MetabolismType.Photosynthesis) { RecordPhotosynthesisAccounting(r); return; }
        if (r.Metabolism == MetabolismType.Saprotrophy || r.Metabolism == MetabolismType.Methanotrophy)
        {
            RecordAerobicCarbonAccounting(r);
            return;
        }
        if (r.Metabolism == MetabolismType.Methanogenesis) { RecordMethanogenesisAccounting(r); return; }
        double product = r.ProductCoefficient > 0d ? r.ProductCoefficient * r.Actual : 0d;
        bool hydrogen = r.Metabolism == MetabolismType.Hydrogenotrophy || r.Metabolism == MetabolismType.SulfurChemosynthesis;
        accountingDiagnostics.RecordReaction(r.Metabolism, r.A, r.B, r.Product,
            r.NeedA * r.Desired, r.NeedB * r.Desired, r.NeedA * r.Actual, r.NeedB * r.Actual, product,
            hydrogen ? r.HydrogenCo2Ratio : availabilityFactor[(int)r.A * grid.NodeCapacity + r.Node],
            hydrogen ? r.HydrogenH2Ratio : availabilityFactor[(int)r.B * grid.NodeCapacity + r.Node],
            availabilityFactor[(int)r.Product * grid.NodeCapacity + r.Node],
            r.StorePerExtent * (float)r.Actual, r.DiagnosticStoreBefore, r.DiagnosticStoreAfter,
            hydrogen ? r.HydrogenEnergyAwarded : r.EnergyPerExtent * (float)r.Actual, r.DiagnosticEnergyDelta, r.Actual > 0d,
            r.NeedA * r.CompetedActual, r.NeedB * r.CompetedActual);
    }

    private void RecycleDeathInventory(int index, Replicator companion, ReplicatorPopulationState state, DeathCause cause)
    {
        float stored = state.OrganicCStore[index];
        if (!float.IsFinite(stored) || stored < 0f) invalidDeathCarbonStates++;
        double carbon = float.IsFinite(stored) ? Math.Max(0d, stored) : 0d;
        int cell = state.GeodesicCellIndex[index], layer = state.CurrentOceanLayerIndex[index];
        lastDeathCell = cell; lastDeathLayer = layer; lastRecycledDeathCause = cause;
        lastDeathCarbon = carbon; lastDeathDissolvedCarbon = 0d;
        recycledDeathsByCause[(int)cause]++;
        deathCarbonByCause[(int)cause] += carbon;
        deathCarbonTotal += carbon;

        // organicCStore is the only tracked carbon inventory. biomassTarget is a
        // division threshold, and size/energy do not represent additional carbon.
        // Clear both representations before swap-back removal; cleanup itself never releases carbon.
        state.OrganicCStore[index] = 0f;
        companion.organicCStore = 0f;
        if (!grid.IsNodeActive(cell, layer))
        {
            // No terrestrial reservoir exists. Do not redirect a land/invalid death into ocean water.
            unplacedDeathCarbon += carbon;
            return;
        }
        if (carbon <= 0d) return;
        int node = grid.GetNodeIndex(cell, layer);
        // Metabolism withdraws concentration*km3 inventories and stores carbon in
        // those same units. Add inventory (not concentration) to the exact death node.
        double actual = resources.ApplyDirectExchangeInventory(GeodesicOceanResource.OrganicC, node, carbon);
        lastDeathDissolvedCarbon = actual;
        deathDissolvedCarbon += actual;
        deathDissolvedCarbonByLayer[layer] += actual;
        // Report measured dissolved-storage rounding separately from the transfer.
        deathTransferRoundingResidual += carbon - actual;
        if (actual > 0d && pendingSaprotrophFounders > 0) ConsiderSaprotrophFounderSite(cell, layer, node);
    }

    private void LogDeathRecycling()
    {
        var text = new System.Text.StringBuilder(700);
        text.Append("[GeodesicDeathRecycling] cumulative=true unit=concentration*km3 reasons={");
        bool first = true;
        for (int cause = 0; cause < recycledDeathsByCause.Length; cause++)
        {
            if (recycledDeathsByCause[cause] == 0) continue;
            if (!first) text.Append(',');
            first = false;
            text.Append((DeathCause)cause).Append("={count=").Append(recycledDeathsByCause[cause])
                .Append(",carbon=").Append(deathCarbonByCause[cause].ToString("G9")).Append('}');
        }
        text.Append("} dissolvedByLayer=[");
        for (int layer = 0; layer < deathDissolvedCarbonByLayer.Length; layer++)
        {
            if (layer > 0) text.Append(',');
            text.Append(deathDissolvedCarbonByLayer[layer].ToString("G9"));
        }
        text.Append("] remainingCarbonTotal=").Append(deathCarbonTotal.ToString("G9"))
            .Append(" dissolvedTransferredTotal=").Append(deathDissolvedCarbon.ToString("G9"))
            .Append(" particulate=unsupported organicSediment=unsupported unplacedCarbon=").Append(unplacedDeathCarbon.ToString("G9"))
            .Append(" transferRoundingResidual=").Append(deathTransferRoundingResidual.ToString("G9"))
            .Append(" accountingResidual=").Append((deathCarbonTotal - deathDissolvedCarbon - unplacedDeathCarbon - deathTransferRoundingResidual).ToString("G9"))
            .Append(" invalidCarbonStates=").Append(invalidDeathCarbonStates)
            .Append(" lastDeath={reason=").Append(lastRecycledDeathCause).Append(",cell=").Append(lastDeathCell)
            .Append(",layer=").Append(lastDeathLayer).Append(",remainingCarbon=").Append(lastDeathCarbon.ToString("G9"))
            .Append(",dissolvedTransferred=").Append(lastDeathDissolvedCarbon.ToString("G9")).Append('}');
        Debug.Log(text.ToString());
    }

    public static void RemoveAgentAtSwapBack(int i,List<Replicator>a,ReplicatorPopulationState s){int last=a.Count-1;if(i<0||i>last)return;if(i!=last)a[i]=a[last];a.RemoveAt(last);s.RemoveAgentAtSwapBack(i);}
    private void RunGeodesicMovement(float dt, List<Replicator> agents, ReplicatorPopulationState state)
    {
        if (!(dt > 0f) || state.Count == 0) return;
        passiveMovementTime += dt;
        float kinematicsDt = ConsumeFixedInterval(ref passiveKinematicsAccumulator, dt, PassiveKinematicsIntervalSeconds);
        LastMovementKinematicsSeconds = kinematicsDt;
        Matrix4x4 worldToLocal = planet.transform.worldToLocalMatrix;
        Matrix4x4 localToWorld = planet.transform.localToWorldMatrix;
        AccumulateActiveMovementTime(dt, state);

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

                TraverseMovementBoundaries(i, state, ref proposedDirection, ref tangent, false);
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

        if (kinematicsDt > 0f)
            using (ActiveMovementMarker.Auto()) RunActiveMovement(kinematicsDt, agents, state, worldToLocal, localToWorld);

        using (MovementVertical.Auto())
        {
            for (int i = 0; i < state.Count; i++)
            {
                if (state.Locomotion[i] != LocomotionType.PassiveDrift) continue;
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
                    int cell = state.GeodesicCellIndex[i];
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
                // Newborns can reach this visual update before the next kinematic tick.
                // Recover their local direction/radius while the inherited world position is valid.
                EnsurePassiveKinematicState(i, state, worldToLocal);
                int cell = state.GeodesicCellIndex[i];
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
        // Spawn may already have supplied direction/radius without a travel tangent.
        // Validate each part independently so partial state cannot disable horizontal drift.
        Vector3 direction = state.PassiveDriftDirection[index];
        if (!GeodesicBiologyDiagnostics.FinitePosition(direction) || !(direction.sqrMagnitude > 0.9f))
        {
            direction = worldToLocal.MultiplyPoint3x4(state.Position[index]).normalized;
            if (!GeodesicBiologyDiagnostics.FinitePosition(direction) || direction.sqrMagnitude < 0.9f)
            {
                direction = grid.SourceTopology.CellDirections[state.GeodesicCellIndex[index]];
                movementCorrections++;
            }
            state.PassiveDriftDirection[index] = direction;
        }
        if (Mathf.Abs(direction.sqrMagnitude - 1f) > 1e-4f)
        {
            direction.Normalize();
            state.PassiveDriftDirection[index] = direction;
            movementCorrections++;
        }
        Vector3 tangent = state.PassiveDriftTangent[index];
        if (!GeodesicBiologyDiagnostics.FinitePosition(tangent) || !(tangent.sqrMagnitude > 0.9f) || Mathf.Abs(Vector3.Dot(direction, tangent)) > 1e-4f)
        {
            tangent = Vector3.ProjectOnPlane(tangent, direction);
            state.PassiveDriftTangent[index] = tangent.sqrMagnitude > 1e-10f
                ? tangent.normalized
                : CreateInitialPassiveTangent(direction, MovementSeedBits(state.MovementSeed[index]));
        }
        if (!float.IsFinite(state.PassiveVisualRadius[index]) || !(state.PassiveVisualRadius[index] > 0f))
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
        GetHabitatTemperature(cell, layer);
        resources.TryGetConcentration(cell, layer, GeodesicOceanResource.O2, out oxygenByNode[node]);
        if (organicCarbonByNode.Length != grid.NodeCapacity) organicCarbonByNode = new float[grid.NodeCapacity];
        resources.TryGetConcentration(cell, layer, GeodesicOceanResource.OrganicC, out organicCarbonByNode[node]);
        habitatSamples++;
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
        lightByNode[node] = GetPhotosyntheticLight(cell, layer);
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
    private void RefreshTemperatureCache()
    {
        if (oceanTemperature == null || cachedOceanTemperatureTick == oceanTemperature.LastCompletedOceanTemperatureTick) return;
        cachedOceanTemperatureTick = oceanTemperature.LastCompletedOceanTemperatureTick;
        if (++temperatureGeneration == int.MaxValue)
        {
            Array.Clear(temperatureStamps, 0, temperatureStamps.Length);
            temperatureGeneration = 1;
        }
    }

    // Coarse background only. Organism consumers must use GetBiologicalTemperature / GetAgentTemperatureSample.
    private float GetHabitatTemperature(int cell, int layer)
    {
        if (grid == null || !grid.IsNodeActive(cell, layer)) return float.NaN;
        return GetHabitatTemperatureAtNode(grid.GetNodeIndex(cell, layer), cell, layer);
    }

    private float GetHabitatTemperatureAtNode(int node, int cell, int layer)
    {
        if (temperatureStamps[node] != temperatureGeneration)
        {
            temperatureByNode[node] = oceanTemperature.GetLayerTemperatureKelvin(cell, layer);
            temperatureStamps[node] = temperatureGeneration;
            oceanTemperatureReads++;
        }
        return temperatureByNode[node];
    }

    public static float CalculateTemperaturePerformance(float temperature, float optimumMin, float optimumMax, float lethalMargin,
        float dormancyFloor = 0f)
    {
        if (!float.IsFinite(temperature) || !float.IsFinite(optimumMin) || !float.IsFinite(optimumMax)
            || !float.IsFinite(lethalMargin) || optimumMax < optimumMin || lethalMargin <= 0f) return 0f;
        if (temperature >= optimumMin && temperature <= optimumMax) return 1f;
        float distance = temperature < optimumMin ? optimumMin - temperature : temperature - optimumMax;
        float performance = Mathf.Clamp01(1f - distance / lethalMargin);
        // As in legacy biology, the floor supports residual activity only within the
        // tolerance shoulders; it never restores metabolism beyond zero performance.
        return performance > 0f ? Mathf.Max(performance, Mathf.Clamp01(dormancyFloor)) : 0f;
    }

    public static void SplitDivisionEnergy(float energy, float divisionCost, out float parentEnergy, out float childEnergy)
    {
        float remaining = Mathf.Max(0f, energy - Mathf.Max(0f, divisionCost));
        childEnergy = remaining * 0.5f;
        parentEnergy = remaining - childEnergy;
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
            * Mathf.Clamp01(substrateAvailabilityFactor)
            * Mathf.Clamp01(storeFraction);
        if (!(co2PerTick > 0f) || !(h2PerTick > 0f)) extent = 0d;
        return new HydrogenotrophyTickResult(
            extent,
            Math.Max(0f, co2PerTick) * extent,
            Math.Max(0f, h2PerTick) * extent,
            Math.Max(0f, energyPerTick) * (float)extent,
            Math.Max(0f, co2PerTick) * (float)extent);
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
    private void MaybeLogDiagnostics(float dt, ReplicatorPopulationState state, List<Replicator> agents,
        bool carbonDivision, float divisionCost, float replicationCost, float divisionMultiple,
        int maxPopulation, float reproductionRate, bool observeAccounting)
    {
        diagnosticElapsed += dt;
        if (diagnosticElapsed < 10d) return;
        diagnosticElapsed = 0d;
        if (biologyReportsEnabled || locomotionSettings.logDiagnostics || chemotaxisABDiagnosticsEnabled || observeAccounting)
            MaybeEmitDiagnosticReports(state, agents, carbonDivision, divisionCost, replicationCost,
                divisionMultiple, maxPopulation, reproductionRate, observeAccounting);

        // These readouts retain their ten-simulated-second window even when Console reports are off.
        biologySteps = agentEvaluations = habitatSamples = oceanTemperatureReads = photosyntheticLightReads
            = resourceInventoryReads = competitionPairs = movementUpdates = wanderStateRefreshes = continuousKinematicUpdates
            = horizontalBoundaryCrossings = verticalTransitions = landBoundaryRejections = invalidLayerCorrections = 0;
    }

    private void MaybeEmitDiagnosticReports(ReplicatorPopulationState state, List<Replicator> agents,
        bool carbonDivision, float divisionCost, float replicationCost, float divisionMultiple,
        int maxPopulation, float reproductionRate, bool observeAccounting)
    {
        double now = Time.unscaledTimeAsDouble;
        if (!chemotaxisABDiagnosticsEnabled && now < nextDiagnosticReportRealTime) return;
        nextDiagnosticReportRealTime = now + 5d;

        using var performanceScope = Telemetry.Auto();
        if (biologyReportsEnabled)
            LogBiologyReports(state, carbonDivision, divisionCost, replicationCost, divisionMultiple, maxPopulation);
        if (!chemotaxisABDiagnosticsEnabled && locomotionSettings.logDiagnostics) LogLocomotionDiagnostics(state);
        if (evolutionDiagnostics.Enabled)
        {
            Debug.Log(evolutionDiagnostics.Summary(abSimulationTime));
            Debug.Log(evolutionDiagnostics.CohortSummary());
        }
        if (BeginABPopulationReport())
        {
            for (int i = 0; i < state.Count; i++) ObserveABPopulation(i, state);
            LogChemotaxisABReport(state.Count);
        }
        if (observeAccounting)
        {
            accountingDiagnostics.BeginPopulationReport();
            for (int i = 0; i < state.Count; i++)
                if (TryGetGrowthSnapshot(i, agents, state, carbonDivision, divisionCost, replicationCost,
                    divisionMultiple, maxPopulation, reproductionRate, lastBiologyStepSeconds, out GeodesicGrowthSnapshot growth))
                    accountingDiagnostics.ObserveGrowth(growth);
            accountingDiagnostics.LogAndResetInterval();
        }
    }

    private void LogBiologyReports(ReplicatorPopulationState state, bool carbonDivision,
        float divisionCost, float replicationCost, float divisionMultiple, int maxPopulation)
    {
        LogDeathRecycling();
        LogSulfurDiagnostics(state);
        LogFermentationDiagnostics(state);
        LogFounderEmergence(state);
        temperatureTelemetry.Log(state, oceanTemperature, temperatureSettings, carbonDivision,
            divisionCost, replicationCost, divisionMultiple, maxPopulation, GetAgentTemperatureSample);
        int hydrogen=0,sulfur=0,methanogenesis=0,photosynthesis=0,methanotrophy=0,passive=0,amoeboid=0,flagellum=0,anchored=0;
        int saprotrophy = 0;
        reportOccupiedNodes ??= new HashSet<int>();
        reportOccupiedNodes.Clear();
        float organicMin=float.PositiveInfinity,organicMax=float.NegativeInfinity,thresholdMin=float.PositiveInfinity,thresholdMax=float.NegativeInfinity,maxDivisionCarbonFraction=0f;double organicSum=0d,thresholdSum=0d;int divisionEligible=0;
        for (int i = 0; i < state.Count; i++)
        {
            switch (state.Metabolism[i])
            {
                case MetabolismType.Hydrogenotrophy: hydrogen++; break;
                case MetabolismType.SulfurChemosynthesis: sulfur++; break;
                case MetabolismType.Methanogenesis: methanogenesis++; break;
                case MetabolismType.Photosynthesis: photosynthesis++; break;
                case MetabolismType.Methanotrophy: methanotrophy++; break;
                case MetabolismType.Saprotrophy: saprotrophy++; break;
            }
            switch (state.Locomotion[i])
            {
                case LocomotionType.PassiveDrift: passive++; break;
                case LocomotionType.Amoeboid: amoeboid++; break;
                case LocomotionType.Flagellum: flagellum++; break;
                case LocomotionType.Anchored: anchored++; break;
            }
            float store = Mathf.Max(0f, state.OrganicCStore[i]);
            float threshold = Mathf.Max(1f, divisionMultiple) * Mathf.Max(0.0001f, state.BiomassTarget[i]);
            organicMin = Mathf.Min(organicMin, store); organicMax = Mathf.Max(organicMax, store); organicSum += store;
            thresholdMin = Mathf.Min(thresholdMin, threshold); thresholdMax = Mathf.Max(thresholdMax, threshold); thresholdSum += threshold;
            maxDivisionCarbonFraction = Mathf.Max(maxDivisionCarbonFraction, store / threshold);
            bool energyReady = state.Energy[i] > Mathf.Max(0f, carbonDivision ? divisionCost : replicationCost);
            if (energyReady && state.CanReplicate[i] && (!carbonDivision || store >= threshold)) divisionEligible++;
            int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i];
            if (grid.IsNodeActive(cell, layer)) reportOccupiedNodes.Add(grid.GetNodeIndex(cell, layer));
        }
        float occupiedH2Min=float.PositiveInfinity,occupiedH2Max=float.NegativeInfinity,occupiedCo2Min=float.PositiveInfinity,occupiedCo2Max=float.NegativeInfinity;double occupiedH2Sum=0d,occupiedCo2Sum=0d;int occupiedSamples=0;
        foreach (int node in reportOccupiedNodes)
        {
            int cell = node / grid.MaximumLayerCount;
            int layer = node % grid.MaximumLayerCount;
            if (!resources.TryGetConcentration(cell, layer, GeodesicOceanResource.H2, out float h2Value)
                || !resources.TryGetConcentration(cell, layer, GeodesicOceanResource.CO2, out float co2Value)) continue;
            occupiedH2Min = Mathf.Min(occupiedH2Min, h2Value);
            occupiedH2Max = Mathf.Max(occupiedH2Max, h2Value);
            occupiedH2Sum += h2Value;
            occupiedCo2Min = Mathf.Min(occupiedCo2Min, co2Value);
            occupiedCo2Max = Mathf.Max(occupiedCo2Max, co2Value);
            occupiedCo2Sum += co2Value;
            occupiedSamples++;
        }
        if(state.Count==0){organicMin=organicMax=thresholdMin=thresholdMax=float.NaN;}if(occupiedSamples==0){occupiedH2Min=occupiedH2Max=occupiedCo2Min=occupiedCo2Max=float.NaN;}
        double samples=Math.Max(1,temperatureSamples);
        int h=(int)MetabolismType.Hydrogenotrophy,s=(int)MetabolismType.SulfurChemosynthesis,m=(int)MetabolismType.Methanogenesis,p=(int)MetabolismType.Photosynthesis,mt=(int)MetabolismType.Methanotrophy;
        double evaluationsPerHabitat=agentEvaluations/(double)Math.Max(1L,habitatSamples);
        double evaluationsPerTemperatureRead=agentEvaluations/(double)Math.Max(1L,oceanTemperatureReads);
        Debug.Log($"[GeodesicBiologyTelemetry] population={state.Count} biologySteps={biologySteps} lastStepSeconds={lastBiologyStepSeconds:G6} agentEvaluations={agentEvaluations} habitatSamples={habitatSamples} occupiedHabitatH2(min/avg/max)={occupiedH2Min:G6}/{occupiedH2Sum/Math.Max(1,occupiedSamples):G6}/{occupiedH2Max:G6} occupiedHabitatCO2(min/avg/max)={occupiedCo2Min:G6}/{occupiedCo2Sum/Math.Max(1,occupiedSamples):G6}/{occupiedCo2Max:G6} organicCStore(min/avg/max)={organicMin:G6}/{organicSum/Math.Max(1,state.Count):G6}/{organicMax:G6} divisionThreshold(min/avg/max)={thresholdMin:G6}/{thresholdSum/Math.Max(1,state.Count):G6}/{thresholdMax:G6} maxDivisionCarbonFraction={maxDivisionCarbonFraction:G6} divisionEligible={divisionEligible} oceanTemperatureReads={oceanTemperatureReads} photosyntheticLightReads={photosyntheticLightReads} resourceInventoryReads={resourceInventoryReads} competitionPairs={competitionPairs} evaluationsPerHabitat={evaluationsPerHabitat:G6} evaluationsPerTemperatureRead={evaluationsPerTemperatureRead:G6} metabolismOrder=h/s/m/p/mt populationByMetabolism={hydrogen}/{sulfur}/{methanogenesis}/{photosynthesis}/{methanotrophy} saprotrophyPopulation={saprotrophy} locomotionOrder=passive/amoeboid/flagellum/anchored populationByLocomotion={passive}/{amoeboid}/{flagellum}/{anchored} movementUpdates={movementUpdates} wanderStateRefreshes={wanderStateRefreshes} continuousKinematicUpdates={continuousKinematicUpdates} horizontalBoundaryCrossings={horizontalBoundaryCrossings} verticalTransitions={verticalTransitions} landBoundaryRejections={landBoundaryRejections} invalidLayerCorrections={invalidLayerCorrections} births={births} deaths={deaths} starvation={starvationDeaths} lifespan={lifespanDeaths} requests={requestedByMetabolism[h]}/{requestedByMetabolism[s]}/{requestedByMetabolism[m]}/{requestedByMetabolism[p]}/{requestedByMetabolism[mt]} achieved={achievedByMetabolism[h]}/{achievedByMetabolism[s]}/{achievedByMetabolism[m]}/{achievedByMetabolism[p]}/{achievedByMetabolism[mt]} zeroAchieved={zeroAchieved} hydrogenPrecisionRejected={hydrogenPrecisionRejected} resourceLimited={resourceLimited} extent={extentByMetabolism[h]:G6}/{extentByMetabolism[s]:G6}/{extentByMetabolism[m]:G6}/{extentByMetabolism[p]:G6}/{extentByMetabolism[mt]:G6} energy={energyByMetabolism[h]:G6}/{energyByMetabolism[s]:G6}/{energyByMetabolism[m]:G6}/{energyByMetabolism[p]:G6}/{energyByMetabolism[mt]:G6} maintenance={maintenancePaid:G6} biologyTemperatureAuthority=coarseOceanLayer temperatureK(min/avg/max)={temperatureMin:G6}/{temperatureSum/samples:G6}/{temperatureMax:G6} performance(min/avg/max)={performanceMin:G6}/{performanceSum/samples:G6}/{performanceMax:G6} zeroPerformance={zeroTemperaturePerformance} invalidHabitat={invalidHabitats} invalidState={invalidBiologicalStates}");
    }
    public static float ResolvePhotosyntheticLight(float daylight, int layer) => float.IsFinite(daylight)
        ? Mathf.Clamp01(daylight) * (layer == 0 ? 1f : layer == 1 ? 0.55f : 0f) : 0f;
    public void Clear()
    {
#if UNITY_EDITOR
        DiagnosticSteeringObservationOverride = null;
#endif
        availability = GeodesicBiologyAvailability.Default;
        ClearChemotaxisAB();
        ClearLocomotion();
        ClearSulfur();
        ClearFermentation();
        hydrogenPhotosynthesisChance = 0f;
        photosynthesisMutationRandom = null;
        ClearSaprotrophy();
        ClearMethanogenesis();
        ClearMethanotrophy();
        ClearFounderEmergence();
        ClearBiologicalThermalCache();
        ClearFounderThermalPlacement();
        ResetVisualDiagnostics();
        planet = null; resources = null; suspended = null; oceanTemperature = null;
        experiencedTemperature = null; surfaceTemperature = null; grid = null;
        requests = Array.Empty<Request>(); demand = availabilityFactor = delta = Array.Empty<double>();
        touched = stamps = lightStamps = Array.Empty<int>();
        temperatureByNode = lightByNode = oxygenByNode = Array.Empty<float>(); touchedCount = 0;
        organicCarbonByNode = Array.Empty<float>();
        temperatureStamps = Array.Empty<int>(); temperatureGeneration = 1; cachedOceanTemperatureTick = double.NaN;
        temperatureSettings = GeodesicBiologyTemperatureSettings.Default;
        temperatureMutationRandom = null;
        temperatureTelemetry = null;
        reportOccupiedNodes = null;
        biologyReportsEnabled = false;
        nextDiagnosticReportRealTime = 0d;
        accountingDiagnostics?.Reset();
        evolutionDiagnostics.Clear();
        Array.Clear(requestedByMetabolism, 0, requestedByMetabolism.Length);
        Array.Clear(achievedByMetabolism, 0, achievedByMetabolism.Length);
        Array.Clear(extentByMetabolism, 0, extentByMetabolism.Length);
        Array.Clear(energyByMetabolism, 0, energyByMetabolism.Length);
        Array.Clear(recycledDeathsByCause, 0, recycledDeathsByCause.Length);
        Array.Clear(deathCarbonByCause, 0, deathCarbonByCause.Length);
        Array.Clear(deathDissolvedCarbonByLayer, 0, deathDissolvedCarbonByLayer.Length);
        deathCarbonTotal = deathDissolvedCarbon = unplacedDeathCarbon = deathTransferRoundingResidual = 0d;
        lastDeathCarbon = lastDeathDissolvedCarbon = 0d; invalidDeathCarbonStates = 0;
        lastDeathCell = lastDeathLayer = -1; lastRecycledDeathCause = DeathCause.Unknown;
        births=deaths=starvationDeaths=lifespanDeaths=zeroAchieved=resourceLimited=invalidHabitats=invalidBiologicalStates=zeroTemperaturePerformance=temperatureSamples=biologySteps=agentEvaluations=habitatSamples=oceanTemperatureReads=photosyntheticLightReads=resourceInventoryReads=competitionPairs=movementUpdates=wanderStateRefreshes=continuousKinematicUpdates=horizontalBoundaryCrossings=verticalTransitions=landBoundaryRejections=invalidLayerCorrections=0;
        maintenancePaid = diagnosticElapsed = temperatureSum = performanceSum = 0d;
        LastMovementKinematicsSeconds = lastBiologyStepSeconds = passiveMovementTime = passiveKinematicsAccumulator = reactionAccumulator = 0f;
        hydrogenotrophyConfigLogged = false; hydrogenPrecisionRejected = 0; biologySeed = 0;
        founderMinLife = founderMaxLife = founderBiomassTarget = founderLethalMargin = 0f;
        founderColor = default; founderHydrogenTemperatureRange = default;
        temperatureMin = performanceMin = float.PositiveInfinity;
        temperatureMax = performanceMax = float.NegativeInfinity;
    }
}
