using UnityEngine;
using System.Collections.Generic;
using System;
using UnityEngine.Serialization;
using Unity.Profiling;

public enum TemperatureDisplayUnit
{
    Kelvin,
    Celsius,
    Fahrenheit
}

public class ReplicatorManager : MonoBehaviour
{
    private static readonly ProfilerMarker PredatorScentUpdateMarker = new ProfilerMarker("ReplicatorManager.UpdateScentFields");
    private static readonly ProfilerMarker PredatorScentSkipNoPredatorsMarker = new ProfilerMarker("ReplicatorManager.SkipScentFields.NoPredators");
    private static readonly ProfilerMarker PopulationStateSyncForLocomotionMarker = new ProfilerMarker("ReplicatorManager.PopulationStateSyncForLocomotion");
    private static readonly ProfilerMarker SteeringThrottleSkipMarker = new ProfilerMarker("ReplicatorManager.SteeringThrottleSkip");

    [Header("Settings")]
    public Mesh replicatorMesh;
    public Material replicatorMaterial;
    public PlanetGenerator planetGenerator;
    [Tooltip("Required. If left empty, the manager will try to auto-find a PlanetResourceMap in the scene.")]
    public PlanetResourceMap planetResourceMap;

    [Header("Population")]
    public int initialSpawnCount = 100;
    public int maxPopulation = 50000;

    public Color baseAgentColor = Color.cyan;

    [Header("Simulation")]
    public float moveSpeed = 4.0f;
    public float turnSpeed = 2.0f;
    public float spawnSpread = 0.0f;

    [Range(0f, 1f)]
    public float reproductionRate = 0.1f;

    public float minLifespan = 30f;
    public float maxLifespan = 60f;

    [Header("Metabolism")]
    public float metabolismTickSeconds = 0.5f;
    public float moveEnergyCostPerSecond = 0.05f;
    public float replicationEnergyCost = 0.5f;
    public float basalEnergyCostPerSecond = 0.01f;
    public float starvationAttributionSeconds = 5f;

    [Header("Carbon-limited Division")]
    public bool enableCarbonLimitedDivision = true;
    public float defaultBiomassTarget = 0.2f;
    [Range(1.2f, 3f)] public float divisionBiomassMultiple = 2.0f;
    public float divisionEnergyCost = 0.2f;
    [Range(0.3f, 0.7f)] public float divisionCarbonSplitToChild = 0.5f;
    public float biomassMutationChance = 0.02f;
    public float biomassMutationScale = 0.1f;

    [Header("Energy -> Speed")]
    public float energyForFullSpeed = 0.5f;
    public float minSpeedFactor = 0.15f;

    [Tooltip("CO2 consumed per metabolism tick for default chemosynthesis.")]
    public float chemosynthesisCo2NeedPerTick = 0.02f;
    [Tooltip("H2S consumed per metabolism tick for default chemosynthesis. Kept low because H2S is vent-localized.")]
    public float chemosynthesisH2sNeedPerTick = 0.001f;
    [Tooltip("Energy granted when one full chemosynthesis reaction tick is completed.")]
    public float chemosynthesisEnergyPerTick = 0.3f;
    [Tooltip("Fractional mutation chance on reproduction that flips metabolism type.")]
    [Range(0f, 1f)] public float metabolismMutationChance = 0.01f;
    public float hydrogenotrophyH2PerTick = 0.02f;
    public float hydrogenotrophyCO2PerTick = 0.01f;
    public float hydrogenotrophyEnergyPerTick = 8f;
    [Range(0f, 1f)] public float hydrogenotrophyStoreFraction = 0.8f;

    [Header("Hydrogen Metabolism Mutation")]
    [Range(0f, 1f)] public float hydrogenToPhotosynthesisMutationChance = 0.003f;
    [Range(0f, 1f)] public float hydrogenToSaprotrophyMutationChance = 0.003f;
    [Range(0f, 1f)] public float hydrogenToSulfurMutationChance = 0.003f;

    [Header("Locomotion Mutation")]
    [Range(0f, 1f)] public float locomotionMutationChance = 0.01f;
    [Range(0f, 1f)] public float locomotionUpgradeChance = 0.5f;
    [Range(0f, 1f)] public float locomotionAnchoredMutationChance = 0.05f;
    [Tooltip("If enabled, Anchored can mutate back into Amoeboid.")]
    public bool allowAnchoredToAmoeboidMutation = false;
    [Range(0f, 1f)] public float anchoredToAmoeboidMutationChance = 0.001f;

    [Header("Metabolism Unlock")]
    [Tooltip("If true, Photosynthesis can mutate back to chemosynthesis. Default false.")]
    public bool allowReverseMetabolismMutation = false;
    [Tooltip("Maximum CO2 consumed per metabolism tick at full insolation (1.0).")]
    public float photosynthesisCo2PerTickAtFullInsolation = 0.02f;
    [Tooltip("Energy gained per unit CO2 consumed by photosynthesis.")]
    public float photosynthesisEnergyPerCo2 = 2f;

    [Header("Aerobic Respiration (shared chemistry)")]
    public float aerobicO2PerC = 0.02f;       // O2 consumed per C respired
    public float aerobicEnergyPerC = 0.05f;   // energy gained per C respired

    [Header("Photosynth Storage/Respiration")]
    [Tooltip("Fraction of photosynth production stored as organic carbon.")]
    public float photosynthStoreFraction = 1.0f;
    public float maxOrganicCStore = 10.0f;
    public float nightRespirationCPerTick = 0.01f;
    public float nightRespirationEnergyPerC = 0.05f;
    public float nightRespirationO2PerC = 0.02f;

    [Header("Chemosynth Carbon Storage / Respiration")]
    [Range(0f, 1f)] public float chemosynthStoreFraction = 1.0f; // fraction of chemo CO2 consumed that becomes organicCStore
    public float chemoRespirationCPerTick = 0.01f;               // max C from store per tick when starving

    [Header("Saprotrophy")]
    [Range(0f, 1f)] public float saprotrophyMutationChance = 0.005f;
    public float saproCPerTick = 0.02f;
    public float saproO2PerC = 0.02f;
    public float saproEnergyPerC = 0.06f;
    [Range(0f, 1f)] public float saproAssimilationFraction = 0.2f; // fraction of env OrganicC intake stored as organicCStore (rest respired)
    public float saproRespireStoreCPerTick = 0.01f;                // if no env food, respire own store

    [Header("Predation")]
    public bool enablePredators = true;
    [Range(0f, 1f)] public float predatorMutationChance = 0.002f;
    public bool predatorRequiresMotility = true;
    public float predatorAttackRange = 0.25f;
    public float predatorAttackCooldownSeconds = 1.0f;
    public float predatorBiteOrganicC = 0.05f;
    public float predatorBiteEnergy = 0.05f;
    public float predatorKillEnergyThreshold = 0.01f;
    [Range(0f, 1f)] public float predatorAssimilationFraction = 0.6f;
    public float predatorEnergyPerC = 1.0f;
    public float predatorBasalCostMultiplier = 1.5f;
    public float predatorMoveSpeedMultiplier = 1.1f;
    public float predatorSpawnColorStrength = 1f;

    [Header("Fear / Avoid Predators")]
    public bool enableFear = true;
    public float fearRadius = 0.6f;
    public float fearStrength = 1.0f;
    public float fearMinDot = 0.0f;
    public bool fearRequiresMotility = true;

    [Header("Temperature Preferences")]
    [FormerlySerializedAs("chemoTempRange")]
    public Vector2 sulfurChemoTempRange = new Vector2(333.15f, 393.15f); // 60-120 °C
    public Vector2 hydrogenTempRange = new Vector2(293.15f, 343.15f); // 20-70 °C
    public Vector2 photoTempRange = new Vector2(283.15f, 313.15f); // 10-40 °C
    public Vector2 saproTempRange = new Vector2(278.15f, 308.15f); // 5-35 °C
    public Vector2 predatorTempRange = new Vector2(283.15f, 323.15f); // 10-50 °C
    public float defaultLethalMargin = 20f; // Kelvin (~20 °C beyond optimal range before death)
    [Range(0f, 1f)] public float tempMutationChance = 0.02f;
    public float tempMutationScale = 2f; // Kelvin mutation scale

    [Header("Temperature Display")]
    public TemperatureDisplayUnit temperatureDisplayUnit = TemperatureDisplayUnit.Celsius;

    [Header("Steering Habitat Score")]
    [Tooltip("Dominant weight for temperature fitness when computing per-cell habitat score.")]
    public float steerTempWeight = 5f;
    [Tooltip("Secondary weight for food fitness when computing per-cell habitat score.")]
    public float steerFoodWeight = 1f;
    [Tooltip("Food normalization scale for CO2. Values >= this count as fully available.")]
    public float steerGoodCO2 = 0.02f;
    [Tooltip("Food normalization scale for H2S. Values >= this count as fully available.")]
    public float steerGoodH2S = 0.001f;
    [Tooltip("Food normalization scale for H2. Values >= this count as fully available.")]
    public float steerGoodH2 = 0.4f;
    [Tooltip("Food normalization scale for O2. Values >= this count as fully available.")]
    public float steerGoodO2 = 0.02f;
    [Tooltip("Food normalization scale for OrganicC. Values >= this count as fully available.")]
    public float steerGoodOrganicC = 0.02f;

    [Header("Run and Tumble")]
    public float amoeboidSenseInterval = 0.5f;
    public float flagellumSenseInterval = 0.2f;
    public float amoeboidSenseIntervalJitter = 0.15f;
    public float flagellumSenseIntervalJitter = 0.05f;
    public float baseTumbleProbability = 0.2f;
    public float tumbleIncreaseOnWorsening = 0.25f;
    public float tumbleDecreaseOnImproving = 0.1f;
    public float minTumbleProbability = 0.02f;
    public float maxTumbleProbability = 0.9f;
    public float amoeboidTurnAngleMax = 60f;
    public float flagellumTurnAngleMax = 180f;
    public float amoeboidTurnRate = 0.5f;
    public float amoeboidMoveSpeedMultiplier = 0.7f;
    public float flagellumTurnRate = 1.5f;
    public float flagellumMoveSpeedMultiplier = 1.2f;
    public float flagellumDriftSuppression = 0.5f;
    public float amoeboidRunNoiseStrength = 0.08f;

    [Header("Scent-Based Predation")]
    public bool useScentPredation = true;
    public float dissolvedOrganicLeakEmitPerSecond = 1.0f;
    public float toxicProteolyticWasteEmitPerSecond = 1.5f;
    public float scentEmitInterval = 0.2f;
    public float toxicProteolyticWasteSteerWeight = 2.0f;
    public float dissolvedOrganicLeakSteerWeight = 2.0f;
    public float scentScoreSaturation = 2.0f;

    [Header("Spawn Resource Bias")]
    public bool biasSpawnsToChemosynthesisResources = true;
    [Range(1, 256)] public int spawnResourceProbeAttempts = 128;
    [Tooltip("How strongly spontaneous/initial spawn chance scales with local H2.")]
    public float h2SpawnBiasWeight = 2.5f;
    [Tooltip("How strongly spontaneous/initial spawn chance scales with local CO2.")]
    public float co2SpawnBiasWeight = 0.5f;
    public float chemoSpawnOptimalTemp = 353.15f; // 80 °C
    public float chemoSpawnTempTolerance = 20f; // Kelvin
    [Header("Spawn Viability Gates")]
    [Range(0f, 1f)]
    [Tooltip("Minimum final hydrogenotrophy spawn score required for spontaneous spawning.")]
    public float minHydrogenSpawnScore = 0.12f;
    [Range(0f, 1f)]
    [Tooltip("Minimum normalized local H2 required for spontaneous hydrogenotroph spawning.")]
    public float minHydrogenSpawnH2 = 0.08f;
    [Range(1, 16)]
    [Tooltip("How many times to retry candidate selection before giving up on this spawn tick.")]
    public int spontaneousSpawnCandidateRetries = 10;
    [Range(1, 2048)]
    [Tooltip("How many simulation steps between coarse hydrogenotroph spawn candidate cache rebuilds.")]
    public int spontaneousSpawnCandidateCacheRefreshSteps = 30;
    [Range(1, 64)]
    [Tooltip("How many cached coarse candidates to score in detail per spawn direction pick.")]
    public int spontaneousSpawnDetailedSampleCount = 8;


    [Header("Spontaneous Spawning")]
    [Tooltip("Keeps attempting random world spawns even when all replicators die out.")]
    public bool enableSpontaneousSpawning = true;
    [Range(0.05f, 10f)]
    [Tooltip("Seconds between random spawn attempts.")]
    public float spawnAttemptInterval = 1.0f;
    [Range(0f, 1f)]
    [Tooltip("Base chance for each random spawn attempt.")]
    public float spontaneousSpawnChance = 0.02f;
    [Range(0f, 60f)]
    [Tooltip("Guarantees at least one spontaneous spawn before this many seconds elapse.")]
    public float guaranteedFirstSpawnWithinSeconds = 10f;
    [Range(0f, 1f)]
    [Tooltip("0 = land-only bias, 0.5 = balanced, 1 = sea-only bias (when ocean is enabled).")]
    public float seaSpawnPreference = 1.0f;
    [Min(0)]
    [Tooltip("Disables spontaneous spawning while total population is at or above this value.")]
    public int disableSpontaneousSpawningAtPopulation = 1000;
    [Min(0)]
    [Tooltip("Re-enables spontaneous spawning when total population falls to or below this value.")]
    public int reenableSpontaneousSpawningAtPopulation = 200;

    [Header("Default Traits")]
    [Tooltip("If enabled, newly created replicators can only be spawned in sea locations.")]
    public bool defaultSpawnOnlyInSea = false;
    [Tooltip("If enabled, replicators only reproduce while currently in the sea.")]
    public bool defaultReplicateOnlyInSea = false;
    [Tooltip("If enabled, replicators stay in the sea and do not move onto land.")]
    public bool defaultMoveOnlyInSea = false;
    [Range(0.01f, 1f)]
    [Tooltip("Movement speed multiplier used by replicators while on land/surface.")]
    public float defaultSurfaceMoveSpeedMultiplier = 0.4f;

    [Header("Debug")]
    public bool colorByEnergy = false;
    [Tooltip("If enabled, includes vent plume diagnostics in periodic metabolism logs.")]
    public bool debugVentPlumeDiagnostics = false;
    [Range(0.05f, 4f)] public float energyVisualMultiplier = 1f;
    [Header("HUD")]
    [Tooltip("Draw a small runtime overlay with population and atmosphere stats.")]
    public bool showSimulationHud = true;
    [Header("Runtime Simulation Timing")]
    [SerializeField] private int runtimeSimulationStepsPerFrame = 1;
    [SerializeField] private bool allowRenderFrameSkippingAtHighSpeed = false;
    [SerializeField, Min(1)] private int renderEveryNFramesAtHighSpeed = 2;
    [SerializeField, Min(1)] private int renderSkipMinStepsPerFrame = 20;
    [Header("Locomotion Debug")]
    public bool enableRunAndTumbleDebug = false;
    public float runAndTumbleDebugWindowSeconds = 2f;
    [Tooltip("Logs a warning when anchored replicators drift beyond the configured epsilon within the sample window.")]
    public bool debugSessileMovement = false;
    [Range(0.5f, 30f)] public float debugSessileMovementWindowSeconds = 3f;
    [Range(0.00001f, 0.1f)] public float debugSessileMovementEpsilon = 0.001f;

    public int RuntimeSimulationStepsPerFrame => runtimeSimulationStepsPerFrame;
    public int SimulationStepsPerFrame => runtimeSimulationStepsPerFrame;
    public int TotalPopulation => agents.Count;
    public int PredatorCount => predatorAgentCount;

    public void SetSimulationTiming(int stepsPerFrame)
    {
        runtimeSimulationStepsPerFrame = Mathf.Max(0, stepsPerFrame);
    }

    private List<Replicator> agents = new List<Replicator>();

    [SerializeField] private int chemosynthAgentCount;
    [SerializeField] private int hydrogenotrophAgentCount;
    [SerializeField] private int photosynthAgentCount;
    [SerializeField] private int saprotrophAgentCount;
    [SerializeField] private int predatorAgentCount;
    [SerializeField] private float averageOrganicCStore;
    [SerializeField] private int divisionEligibleAgentCount;
    private readonly ReplicatorHudPresenter hudPresenter = new ReplicatorHudPresenter();
    private readonly ReplicatorDebugTelemetry debugTelemetry = new ReplicatorDebugTelemetry();
    private bool isInitialized;
    private readonly ReplicatorSpawnSystem spawnSystem = new ReplicatorSpawnSystem();
    private readonly ReplicatorLifecycleSystem lifecycleSystem = new ReplicatorLifecycleSystem();
    private readonly ReplicatorMetabolismSystem metabolismSystem = new ReplicatorMetabolismSystem();
    private readonly ReplicatorPopulationState populationState = new ReplicatorPopulationState();
    private readonly ReplicatorPredationSystem predationSystem = new ReplicatorPredationSystem();
    private readonly ReplicatorSteeringSystem steeringSystem = new ReplicatorSteeringSystem();
    private readonly ReplicatorMovementSystem movementSystem = new ReplicatorMovementSystem();
    private readonly ReplicatorRenderSystem renderSystem = new ReplicatorRenderSystem();
    private float metabolismTickTimer;
    private float debugChemoTempSum;
    private float debugHydrogenTempSum;
    private float debugPhotoTempSum;
    private float debugSaproTempSum;
    private int debugChemoTempCount;
    private int debugHydrogenTempCount;
    private int debugPhotoTempCount;
    private int debugSaproTempCount;
    private int debugChemoStressedCount;
    private int debugHydrogenStressedCount;
    private int debugPhotoStressedCount;
    private int debugSaproStressedCount;
    private float nextChemoSpawnDebugLogTime;
    private int[] chemoDeathCauseCounts;
    private int[] hydrogenDeathCauseCounts;
    private int[] photoDeathCauseCounts;
    private int[] saproDeathCauseCounts;
    private int[] predatorDeathCauseCounts;
    private int predationKillsWindow;
    private float avgToxicProteolyticWasteDebug;
    private float avgDissolvedOrganicLeakDebug;
    private float nextScentUpdateTime;
    private float simulationTimeSeconds;
    private float currentStepDeltaTime;
    private ReplicatorSteeringSystem.DebugState steeringDebugState;
    private readonly Dictionary<int, List<int>> preyAgentsByCell = new Dictionary<int, List<int>>(2048);
    private readonly List<int> spontaneousHydrogenSpawnCandidateCells = new List<int>(1024);
    private int spontaneousSpawnCandidateCacheLastRefreshStep = -1;
    private int simulationStepCount;
    private int steeringRecomputeCounter;
    private int predatorPresenceCacheStep = -1;
    private bool predatorPresenceCached;




    [Header("Pipeline")]
    [SerializeField] private ReplicatorSimulationPipeline simulationPipeline;
    [Header("Debug/Testing")]
    public bool enableRendering = true;


    void Awake()
    {
        simulationPipeline = GetComponent<ReplicatorSimulationPipeline>();
        if (simulationPipeline == null)
        {
            simulationPipeline = gameObject.AddComponent<ReplicatorSimulationPipeline>();
        }

        if (simulationPipeline != null)
        {
            simulationPipeline.enabled = false;
        }
    }

    void ResolvePlanetResourceMapReference()
    {
        if (planetGenerator == null)
        {
            planetGenerator = GetComponentInParent<PlanetGenerator>();
        }

        if (planetGenerator == null)
        {
            planetGenerator = FindFirstObjectByType<PlanetGenerator>();
        }

        if (planetResourceMap != null)
        {
            return;
        }

        planetResourceMap = GetComponent<PlanetResourceMap>();

        if (planetResourceMap == null && planetGenerator != null)
        {
            planetResourceMap = planetGenerator.GetComponent<PlanetResourceMap>();

            if (planetResourceMap == null)
            {
                planetResourceMap = planetGenerator.gameObject.AddComponent<PlanetResourceMap>();
                Debug.Log("ReplicatorManager auto-added PlanetResourceMap to the PlanetGenerator object.", planetGenerator);
            }
        }

        if (planetResourceMap == null)
        {
            planetResourceMap = FindFirstObjectByType<PlanetResourceMap>();
        }
    }

    void Start()
    {
        ResolvePlanetResourceMapReference();

        if (replicatorMesh == null || replicatorMaterial == null || planetGenerator == null || planetResourceMap == null)
        {
            Debug.LogError("ReplicatorManager is missing required references (mesh/material/planetGenerator/planetResourceMap). Assign PlanetResourceMap in Inspector. It can also be auto-added to the PlanetGenerator object if one exists in scene.", this);
            enabled = false;
            return;
        }

        isInitialized = true;
        EnsureDeathCauseCounters();

        for (int i = 0; i < initialSpawnCount; i++) SpawnAgentAtRandomLocation();
    }

    void Update()
    {
        if (!isInitialized) return;

        int stepsPerFrame = Mathf.Max(0, runtimeSimulationStepsPerFrame);
        for (int i = 0; i < stepsPerFrame; i++)
        {
            currentStepDeltaTime = Time.deltaTime;
            simulationTimeSeconds += currentStepDeltaTime;
            simulationStepCount++;

            if (ShouldProcessPredatorScent())
            {
                UpdateScentFields();
            }
            else
            {
                ResetScentDebugState();
            }

            UpdateLifecycle();
            TickMetabolism();
            RunPredationPass();
            HandleSpontaneousSpawning();
            bool populationStatePrimedForLocomotion = PreparePopulationStateForLocomotion();
            UpdateRunAndTumbleLocomotion(populationStatePrimedForLocomotion);
            RunMovementJob(populationStatePrimedForLocomotion);
            ValidateSessileMovement();
        }

        if (enableRendering && ShouldRenderThisFrame(stepsPerFrame))
        {
            RenderAgents();
        }

        UpdateMetabolismCounts();
        LogMetabolismDebugThrottled();
    }

    public bool ShouldProcessPredatorScent()
    {
        return useScentPredation
            && planetResourceMap != null
            && planetResourceMap.enableScentFields
            && HasPredatorsThisStep();
    }


    bool ShouldRenderThisFrame(int stepsPerFrame)
    {
        if (!allowRenderFrameSkippingAtHighSpeed || stepsPerFrame < renderSkipMinStepsPerFrame)
        {
            return true;
        }

        int interval = Mathf.Max(1, renderEveryNFramesAtHighSpeed);
        return Time.frameCount % interval == 0;
    }

    void ResetScentDebugState()
    {
        avgToxicProteolyticWasteDebug = 0f;
        avgDissolvedOrganicLeakDebug = 0f;
    }

    void OnGUI()
    {
        if (!showSimulationHud || !isInitialized)
        {
            return;
        }

        hudPresenter.Draw(
            agents,
            planetResourceMap,
            chemosynthAgentCount,
            hydrogenotrophAgentCount,
            photosynthAgentCount,
            saprotrophAgentCount,
            predatorAgentCount,
            ref temperatureDisplayUnit);
    }


    void UpdateMetabolismCounts()
    {
        int chemo = 0;
        int hydrogen = 0;
        int photo = 0;
        int sapro = 0;
        int predator = 0;

        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i].metabolism == MetabolismType.Hydrogenotrophy)
            {
                hydrogen++;
            }
            else if (agents[i].metabolism == MetabolismType.Photosynthesis)
            {
                photo++;
            }
            else if (agents[i].metabolism == MetabolismType.SulfurChemosynthesis)
            {
                chemo++;
            }
            else if (agents[i].metabolism == MetabolismType.Predation)
            {
                predator++;
            }
            else
            {
                sapro++;
            }
        }

        chemosynthAgentCount = chemo;
        hydrogenotrophAgentCount = hydrogen;
        photosynthAgentCount = photo;
        saprotrophAgentCount = sapro;
        predatorAgentCount = predator;
    }



    void LogMetabolismDebugThrottled()
    {
        debugTelemetry.LogMetabolismDebugThrottled(
            planetGenerator,
            planetResourceMap,
            debugVentPlumeDiagnostics,
            chemosynthAgentCount,
            hydrogenotrophAgentCount,
            photosynthAgentCount,
            saprotrophAgentCount,
            predatorAgentCount,
            debugChemoTempSum,
            debugChemoTempCount,
            debugChemoStressedCount,
            debugHydrogenTempSum,
            debugHydrogenTempCount,
            debugHydrogenStressedCount,
            debugPhotoTempSum,
            debugPhotoTempCount,
            debugPhotoStressedCount,
            debugSaproTempSum,
            debugSaproTempCount,
            debugSaproStressedCount,
            averageOrganicCStore,
            divisionEligibleAgentCount,
            predationKillsWindow,
            avgToxicProteolyticWasteDebug,
            avgDissolvedOrganicLeakDebug,
            chemoDeathCauseCounts,
            hydrogenDeathCauseCounts,
            photoDeathCauseCounts,
            saproDeathCauseCounts,
            predatorDeathCauseCounts,
            temperatureDisplayUnit,
            IsSaprotrophyUnlocked,
            FormatDeathCauseDistribution,
            () => predationKillsWindow = 0,
            ResetDeathCauseCounters);
    }


    void EnsureDeathCauseCounters()
    {
        int len = System.Enum.GetValues(typeof(DeathCause)).Length;
        if (chemoDeathCauseCounts == null || chemoDeathCauseCounts.Length != len) chemoDeathCauseCounts = new int[len];
        if (hydrogenDeathCauseCounts == null || hydrogenDeathCauseCounts.Length != len) hydrogenDeathCauseCounts = new int[len];
        if (photoDeathCauseCounts == null || photoDeathCauseCounts.Length != len) photoDeathCauseCounts = new int[len];
        if (saproDeathCauseCounts == null || saproDeathCauseCounts.Length != len) saproDeathCauseCounts = new int[len];
        if (predatorDeathCauseCounts == null || predatorDeathCauseCounts.Length != len) predatorDeathCauseCounts = new int[len];
    }

    void ResetDeathCauseCounters()
    {
        if (chemoDeathCauseCounts == null || hydrogenDeathCauseCounts == null || photoDeathCauseCounts == null || saproDeathCauseCounts == null || predatorDeathCauseCounts == null)
        {
            return;
        }

        System.Array.Clear(chemoDeathCauseCounts, 0, chemoDeathCauseCounts.Length);
        System.Array.Clear(hydrogenDeathCauseCounts, 0, hydrogenDeathCauseCounts.Length);
        System.Array.Clear(photoDeathCauseCounts, 0, photoDeathCauseCounts.Length);
        System.Array.Clear(saproDeathCauseCounts, 0, saproDeathCauseCounts.Length);
        System.Array.Clear(predatorDeathCauseCounts, 0, predatorDeathCauseCounts.Length);
    }

    void RegisterDeathCause(MetabolismType metabolism, DeathCause cause)
    {
        EnsureDeathCauseCounters();
        int causeIndex = Mathf.Clamp((int)cause, 0, chemoDeathCauseCounts.Length - 1);

        int[] counts = metabolism == MetabolismType.Hydrogenotrophy
            ? hydrogenDeathCauseCounts
            : (metabolism == MetabolismType.Photosynthesis
                ? photoDeathCauseCounts
                : (metabolism == MetabolismType.Saprotrophy ? saproDeathCauseCounts : (metabolism == MetabolismType.Predation ? predatorDeathCauseCounts : chemoDeathCauseCounts)));

        counts[causeIndex]++;
    }

    string FormatDeathCauseDistribution(int[] counts)
    {
        if (counts == null)
        {
            return "n/a";
        }

        int total = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            total += counts[i];
        }

        if (total <= 0)
        {
            return "n/a";
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < counts.Length; i++)
        {
            int count = counts[i];
            if (count <= 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            DeathCause cause = (DeathCause)i;
            float pct = (100f * count) / total;
            sb.Append(DeathCauseShortLabel(cause));
            sb.Append('=');
            sb.Append(pct.ToString("0"));
            sb.Append('%');
        }

        return sb.ToString();
    }

    string DeathCauseShortLabel(DeathCause cause)
    {
        switch (cause)
        {
            case DeathCause.OldAge: return "OldAge";
            case DeathCause.EnergyDepletion: return "Energy";
            case DeathCause.TemperatureTooHigh: return "TempHigh";
            case DeathCause.TemperatureTooLow: return "TempLow";
            case DeathCause.Lack_CO2: return "CO2";
            case DeathCause.Lack_H2S: return "H2S";
            case DeathCause.Lack_H2: return "H2";
            case DeathCause.Lack_Light: return "Light";
            case DeathCause.Lack_OrganicC_Food: return "OrgC";
            case DeathCause.Lack_O2: return "O2";
            case DeathCause.Lack_StoredC: return "StoredC";
            case DeathCause.Predation: return "Predation";
            default: return "Unknown";
        }
    }

    DeathCause ResolveEnergyDeathCause(Replicator agent)
    {
        float threshold = Mathf.Max(0f, starvationAttributionSeconds);
        float best = -1f;
        DeathCause cause = DeathCause.EnergyDepletion;

        if (agent.starveH2sSeconds >= threshold && agent.starveH2sSeconds > best)
        {
            best = agent.starveH2sSeconds;
            cause = DeathCause.Lack_H2S;
        }

        if (agent.starveH2Seconds >= threshold && agent.starveH2Seconds > best)
        {
            best = agent.starveH2Seconds;
            cause = DeathCause.Lack_H2;
        }

        if (agent.starveCo2Seconds >= threshold && agent.starveCo2Seconds > best)
        {
            best = agent.starveCo2Seconds;
            cause = DeathCause.Lack_CO2;
        }

        if (agent.starveLightSeconds >= threshold && agent.starveLightSeconds > best)
        {
            best = agent.starveLightSeconds;
            cause = DeathCause.Lack_Light;
        }

        if (agent.starveOrganicCFoodSeconds >= threshold && agent.starveOrganicCFoodSeconds > best)
        {
            best = agent.starveOrganicCFoodSeconds;
            cause = DeathCause.Lack_OrganicC_Food;
        }

        if (agent.starveO2Seconds >= threshold && agent.starveO2Seconds > best)
        {
            best = agent.starveO2Seconds;
            cause = DeathCause.Lack_O2;
        }

        if (agent.starveStoredCSeconds >= threshold && agent.starveStoredCSeconds > best)
        {
            cause = DeathCause.Lack_StoredC;
        }

        return cause;
    }

    float UpdateStarveTimer(float current, bool deprived, float dt)
    {
        return deprived ? (current + dt) : 0f;
    }

    public static float KelvinToCelsius(float k) => k - 273.15f;

    public static float KelvinToFahrenheit(float k) => (k - 273.15f) * 9f / 5f + 32f;

    public static string FormatTemperature(float kelvin, TemperatureDisplayUnit unit)
    {
        switch (unit)
        {
            case TemperatureDisplayUnit.Kelvin:
                return $"{kelvin:0.0} K";
            case TemperatureDisplayUnit.Fahrenheit:
                return $"{KelvinToFahrenheit(kelvin):0.0} °F";
            default:
                return $"{KelvinToCelsius(kelvin):0.0} °C";
        }
    }

    void HandleSpontaneousSpawning()
    {
        spawnSystem.HandleSpontaneousSpawning(
            enableSpontaneousSpawning,
            guaranteedFirstSpawnWithinSeconds,
            spawnAttemptInterval,
            currentStepDeltaTime,
            () => agents.Count,
            disableSpontaneousSpawningAtPopulation,
            reenableSpontaneousSpawningAtPopulation,
            SpawnAgentAtRandomLocation,
            TryRandomSpontaneousSpawn);
    }

    bool TryRandomSpontaneousSpawn()
    {
        RefreshSpontaneousHydrogenSpawnCandidateCacheIfNeeded();

        return spawnSystem.TryRandomSpontaneousSpawn(
            agents.Count,
            maxPopulation,
            spontaneousSpawnChance,
            GetSpawnDirectionCandidate,
            IsSeaLocation,
            GetLocationSpawnMultiplier,
            IsHydrogenSpawnCandidateViable,
            spontaneousSpawnCandidateRetries,
            direction => SpawnAgentAtDirection(
                direction,
                CreateDefaultTraits(),
                null,
                MetabolismType.Hydrogenotrophy,
                LocomotionType.PassiveDrift,
                0f,
                out _));
    }

    Vector3 GetSpawnDirectionCandidate()
    {
        if (!biasSpawnsToChemosynthesisResources || planetResourceMap == null || planetGenerator == null)
        {
            return UnityEngine.Random.onUnitSphere;
        }

        int cachedCount = spontaneousHydrogenSpawnCandidateCells.Count;
        if (cachedCount > 0)
        {
            int samples = Mathf.Clamp(spontaneousSpawnDetailedSampleCount, 1, cachedCount);
            Vector3 cachedBestDirection = GetDirectionForCellIndex(spontaneousHydrogenSpawnCandidateCells[UnityEngine.Random.Range(0, cachedCount)]);
            float cachedBestScore = -1f;

            for (int i = 0; i < samples; i++)
            {
                int candidateCell = spontaneousHydrogenSpawnCandidateCells[UnityEngine.Random.Range(0, cachedCount)];
                Vector3 candidate = GetDirectionForCellIndex(candidateCell);
                float score = GetHydrogenotrophySpawnScore(candidate);
                if (score > cachedBestScore)
                {
                    cachedBestScore = score;
                    cachedBestDirection = candidate;
                }
            }

            return cachedBestDirection;
        }

        int attempts = Mathf.Max(1, spawnResourceProbeAttempts);
        Vector3 bestDirection = UnityEngine.Random.onUnitSphere;
        float bestScore = -1f;

        for (int i = 0; i < attempts; i++)
        {
            Vector3 candidate = UnityEngine.Random.onUnitSphere;
            float score = GetHydrogenotrophySpawnScore(candidate);
            if (score > bestScore)
            {
                bestScore = score;
                bestDirection = candidate;
            }
        }

        return bestDirection;
    }

    void RefreshSpontaneousHydrogenSpawnCandidateCacheIfNeeded()
    {
        if (!biasSpawnsToChemosynthesisResources || planetResourceMap == null || planetGenerator == null)
        {
            spontaneousHydrogenSpawnCandidateCells.Clear();
            spontaneousSpawnCandidateCacheLastRefreshStep = simulationStepCount;
            return;
        }

        int refreshSteps = Mathf.Max(1, spontaneousSpawnCandidateCacheRefreshSteps);
        if (spontaneousSpawnCandidateCacheLastRefreshStep >= 0
            && simulationStepCount - spontaneousSpawnCandidateCacheLastRefreshStep < refreshSteps)
        {
            return;
        }

        spontaneousSpawnCandidateCacheLastRefreshStep = simulationStepCount;
        spontaneousHydrogenSpawnCandidateCells.Clear();

        int resolution = Mathf.Max(1, planetGenerator.resolution);
        int cellCount = PlanetGridIndexing.GetCellCount(resolution);
        for (int cell = 0; cell < cellCount; cell++)
        {
            float h2Availability = NormalizeResource(ResourceType.H2, cell, steerGoodH2);
            if (h2Availability >= minHydrogenSpawnH2)
            {
                spontaneousHydrogenSpawnCandidateCells.Add(cell);
            }
        }
    }

    Vector3 GetDirectionForCellIndex(int cell)
    {
        int resolution = Mathf.Max(1, planetGenerator.resolution);
        int cellsPerFace = resolution * resolution;
        int face = Mathf.Clamp(cell / cellsPerFace, 0, 5);
        int local = Mathf.Clamp(cell % cellsPerFace, 0, cellsPerFace - 1);

        int x = local % resolution;
        int y = local / resolution;

        float u = ((x + 0.5f) / resolution) * 2f - 1f;
        float v = ((y + 0.5f) / resolution) * 2f - 1f;

        Vector3 pointOnCube;
        switch (face)
        {
            case 0: pointOnCube = new Vector3(u, 1f, -v); break;
            case 1: pointOnCube = new Vector3(-u, -1f, -v); break;
            case 2: pointOnCube = new Vector3(-1f, -v, -u); break;
            case 3: pointOnCube = new Vector3(1f, -v, u); break;
            case 4: pointOnCube = new Vector3(-v, u, 1f); break;
            default: pointOnCube = new Vector3(-v, -u, -1f); break;
        }

        return pointOnCube.normalized;
    }

    float GetHydrogenotrophySpawnScore(Vector3 direction)
    {
        int resolution = Mathf.Max(1, planetGenerator.resolution);
        Vector3 normalizedDir = direction.normalized;
        int cellIndex = PlanetGridIndexing.DirectionToCellIndex(normalizedDir, resolution);

        float co2Availability = NormalizeResource(ResourceType.CO2, cellIndex, steerGoodCO2);
        float h2Availability = NormalizeResource(ResourceType.H2, cellIndex, steerGoodH2);
        float chemistryScore = Mathf.Min(h2Availability, co2Availability);

        float temp = planetResourceMap.GetTemperature(normalizedDir, cellIndex);

        float tempFitness = ComputeTemperatureFitnessForRange(temp, hydrogenTempRange);
        float score = chemistryScore * tempFitness;

        if (Time.timeSinceLevelLoad >= nextChemoSpawnDebugLogTime)
        {
            nextChemoSpawnDebugLogTime = Time.timeSinceLevelLoad + 8f;
            Debug.Log($"Hydrogen spawn score: chemistry={chemistryScore:0.00} temp={FormatTemperature(temp, temperatureDisplayUnit)} tempFitness={tempFitness:0.00} final={score:0.00}");
        }
        return score;
    }

    bool IsHydrogenSpawnCandidateViable(Vector3 direction)
    {
        if (planetResourceMap == null || planetGenerator == null)
        {
            return true;
        }

        int resolution = Mathf.Max(1, planetGenerator.resolution);
        Vector3 normalizedDir = direction.normalized;
        int cellIndex = PlanetGridIndexing.DirectionToCellIndex(normalizedDir, resolution);

        float h2Availability = NormalizeResource(ResourceType.H2, cellIndex, steerGoodH2);
        if (h2Availability < minHydrogenSpawnH2)
        {
            return false;
        }

        float score = GetHydrogenotrophySpawnScore(normalizedDir);
        return score >= minHydrogenSpawnScore;
    }

    float GetLocationSpawnMultiplier(bool isSeaLocation)
    {
        if (!planetGenerator.OceanEnabled)
        {
            return 1f;
        }

        float seaWeight = Mathf.Clamp01(seaSpawnPreference);
        float landWeight = 1f - seaWeight;
        float selectedWeight = isSeaLocation ? seaWeight : landWeight;

        // 0.5 means unbiased (multiplier = 1). 1.0 means sea-only, 0.0 means land-only.
        return selectedWeight / 0.5f;
    }

    bool IsSeaLocation(Vector3 direction)
    {
        if (!planetGenerator.OceanEnabled)
        {
            return false;
        }

        float noise = planetGenerator.CalculateNoise(direction.normalized);
        return noise < planetGenerator.OceanThresholdNoise;
    }

    float NormalizeResource(ResourceType resourceType, int cellIndex, float goodEnoughScale)
    {
        float scale = Mathf.Max(0.0001f, goodEnoughScale);
        float value = planetResourceMap.Get(resourceType, cellIndex);
        float normalized = Mathf.Clamp01(value / scale);
        return float.IsNaN(normalized) || float.IsInfinity(normalized) ? 0f : normalized;
    }

    public float ComputeLocalHabitatValue(Replicator agent, Vector3 dir, int cellIndex)
    {
        return steeringSystem.ComputeLocalHabitatValue(
            agent,
            dir,
            cellIndex,
            planetResourceMap,
            CreateSteeringSettings());
    }

    float ComputeTemperatureFitnessForRange(float temperature, Vector2 range)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        if (temperature >= min && temperature <= max)
        {
            return 1f;
        }

        float tolerance = Mathf.Max(0.0001f, 0.5f * (max - min));
        float d = temperature < min ? (min - temperature) : (temperature - max);
        return Mathf.Clamp01(1f - (d / tolerance));
    }

    void UpdateScentFields()
    {
        using (PredatorScentUpdateMarker.Auto())
        {
            float interval = Mathf.Max(0.01f, scentEmitInterval);
            if (simulationTimeSeconds < nextScentUpdateTime)
            {
                return;
            }

            nextScentUpdateTime = simulationTimeSeconds + interval;

            if (planetResourceMap == null)
            {
                return;
            }

            int resolution = Mathf.Max(1, planetGenerator.resolution);
            int cellCount = PlanetGridIndexing.GetCellCount(resolution);
            planetResourceMap.EnsureScentArrays(cellCount);
            populationState.SyncPredationFieldsFromAgents(agents);
            RebuildPreyCellBins(resolution, populationState);

            float leakEmit = Mathf.Max(0f, dissolvedOrganicLeakEmitPerSecond) * interval;
            float wasteEmit = Mathf.Max(0f, toxicProteolyticWasteEmitPerSecond) * interval;
            bool hasPredator = false;

            for (int i = 0; i < agents.Count; i++)
            {
                Replicator agent = agents[i];
                int cellIndex = PlanetGridIndexing.DirectionToCellIndex(agent.position.normalized, resolution);
                if (IsPredator(agent))
                {
                    hasPredator = true;
                    planetResourceMap.AddScent(ResourceType.ToxicProteolyticWaste, cellIndex, wasteEmit);
                }
                else
                {
                    planetResourceMap.AddScent(ResourceType.DissolvedOrganicLeak, cellIndex, leakEmit);
                }
            }

            if (!hasPredator)
            {
                using (PredatorScentSkipNoPredatorsMarker.Auto())
                {
                    ResetScentDebugState();
                }

                return;
            }

            planetResourceMap.ApplyScentDecayAndDiffuse(interval);
            SampleScentDiagnostics();
        }
    }

    bool HasAnyPredators()
    {
        for (int i = 0; i < agents.Count; i++)
        {
            if (IsPredator(agents[i]))
            {
                return true;
            }
        }

        return false;
    }

    bool HasPredatorsThisStep()
    {
        if (predatorPresenceCacheStep == simulationStepCount)
        {
            return predatorPresenceCached;
        }

        predatorPresenceCacheStep = simulationStepCount;
        predatorPresenceCached = HasAnyPredators();
        return predatorPresenceCached;
    }

    void RebuildPreyCellBins(int resolution, ReplicatorPopulationState state)
    {
        preyAgentsByCell.Clear();
        int count = Mathf.Min(agents.Count, state != null ? state.Count : 0);
        for (int i = 0; i < count; i++)
        {
            if (state.Metabolism[i] == MetabolismType.Predation)
            {
                continue;
            }

            int cellIndex = PlanetGridIndexing.DirectionToCellIndex(state.Position[i].normalized, resolution);
            if (!preyAgentsByCell.TryGetValue(cellIndex, out List<int> preyIndices))
            {
                preyIndices = new List<int>(4);
                preyAgentsByCell[cellIndex] = preyIndices;
            }

            preyIndices.Add(i);
        }
    }

    void SampleScentDiagnostics()
    {
        if (planetResourceMap == null || planetResourceMap.dissolvedOrganicLeak == null || planetResourceMap.toxicProteolyticWaste == null)
        {
            avgToxicProteolyticWasteDebug = 0f;
            avgDissolvedOrganicLeakDebug = 0f;
            return;
        }

        float[] dissolvedOrganicLeak = planetResourceMap.dissolvedOrganicLeak;
        float[] toxicProteolyticWaste = planetResourceMap.toxicProteolyticWaste;
        int cellCount = dissolvedOrganicLeak.Length;
        if (cellCount == 0)
        {
            avgToxicProteolyticWasteDebug = 0f;
            avgDissolvedOrganicLeakDebug = 0f;
            return;
        }

        int samples = Mathf.Min(32, cellCount);
        int stride = Mathf.Max(1, cellCount / samples);
        float leakSum = 0f;
        float wasteSum = 0f;
        int sampled = 0;
        for (int cell = 0; cell < cellCount && sampled < samples; cell += stride)
        {
            leakSum += dissolvedOrganicLeak[cell];
            wasteSum += toxicProteolyticWaste[cell];
            sampled++;
        }

        float invSampleCount = sampled > 0 ? 1f / sampled : 0f;
        avgDissolvedOrganicLeakDebug = leakSum * invSampleCount;
        avgToxicProteolyticWasteDebug = wasteSum * invSampleCount;
    }

    bool IsPredator(Replicator agent)
    {
        return agent != null && agent.metabolism == MetabolismType.Predation;
    }

    bool IsMotile(Replicator agent)
    {
        if (agent == null)
        {
            return false;
        }

        return agent.locomotion == LocomotionType.Amoeboid || agent.locomotion == LocomotionType.Flagellum;
    }

    void RunPredationPass()
    {
        if (planetGenerator == null)
        {
            return;
        }

        if (!HasPredatorsThisStep())
        {
            return;
        }

        ReplicatorPredationSystem.Settings settings = new ReplicatorPredationSystem.Settings
        {
            EnablePredators = enablePredators,
            UseScentPredation = useScentPredation,
            PredatorBiteOrganicC = predatorBiteOrganicC,
            PredatorBiteEnergy = predatorBiteEnergy,
            PredatorAssimilationFraction = predatorAssimilationFraction,
            PredatorAttackCooldownSeconds = predatorAttackCooldownSeconds,
            PredatorEnergyPerC = predatorEnergyPerC,
            MaxOrganicCStore = maxOrganicCStore,
            PredatorKillEnergyThreshold = predatorKillEnergyThreshold,
        };

        predationSystem.RunPredationPass(
            agents,
            populationState,
            settings,
            currentStepDeltaTime,
            Mathf.Max(1, planetGenerator.resolution),
            preyAgentsByCell,
            RegisterDeathCause,
            DepositDeathOrganicC,
            DepositPredationOrganicCAtLocation,
            ref predationKillsWindow);
    }

    void UpdateRunAndTumbleLocomotion(bool populationStatePrimed)
    {
        if (!ShouldRecomputeSteeringThisStep())
        {
            using (SteeringThrottleSkipMarker.Auto())
            {
                return;
            }
        }

        steeringSystem.UpdateRunAndTumbleLocomotion(
            agents,
            populationState,
            planetGenerator,
            planetResourceMap,
            CreateSteeringSettings(),
            currentStepDeltaTime,
            simulationTimeSeconds,
            populationStatePrimed,
            ref steeringDebugState);
    }

    bool ShouldRecomputeSteeringThisStep()
    {
        int interval = GetSteeringRecomputeInterval(Mathf.Max(1, runtimeSimulationStepsPerFrame));
        int phase = steeringRecomputeCounter++;
        return interval <= 1 || (phase % interval) == 0;
    }

    static int GetSteeringRecomputeInterval(int stepsPerFrame)
    {
        if (stepsPerFrame >= 50)
        {
            return 8;
        }

        if (stepsPerFrame >= 20)
        {
            return 4;
        }

        if (stepsPerFrame >= 5)
        {
            return 2;
        }

        return 1;
    }

    bool PreparePopulationStateForLocomotion()
    {
        if (agents.Count == 0)
        {
            return false;
        }

        using (PopulationStateSyncForLocomotionMarker.Auto())
        {
            populationState.SyncFromAgents(agents);
        }

        return true;
    }

    void ValidateSessileMovement()
    {
        debugTelemetry.ValidateSessileMovement(
            debugSessileMovement,
            debugSessileMovementWindowSeconds,
            debugSessileMovementEpsilon,
            agents,
            this);
    }


    ReplicatorSteeringSystem.Settings CreateSteeringSettings()
    {
        return new ReplicatorSteeringSystem.Settings
        {
            SteerTempWeight = steerTempWeight,
            SteerFoodWeight = steerFoodWeight,
            UseScentPredation = ShouldProcessPredatorScent(),
            DissolvedOrganicLeakSteerWeight = dissolvedOrganicLeakSteerWeight,
            ToxicProteolyticWasteSteerWeight = toxicProteolyticWasteSteerWeight,
            ScentScoreSaturation = scentScoreSaturation,
            SteerGoodCO2 = steerGoodCO2,
            SteerGoodH2S = steerGoodH2S,
            SteerGoodH2 = steerGoodH2,
            SteerGoodOrganicC = steerGoodOrganicC,
            SteerGoodO2 = steerGoodO2,
            BaseTumbleProbability = baseTumbleProbability,
            MinTumbleProbability = minTumbleProbability,
            MaxTumbleProbability = maxTumbleProbability,
            TumbleDecreaseOnImproving = tumbleDecreaseOnImproving,
            TumbleIncreaseOnWorsening = tumbleIncreaseOnWorsening,
            FlagellumTurnAngleMax = flagellumTurnAngleMax,
            AmoeboidTurnAngleMax = amoeboidTurnAngleMax,
            AmoeboidRunNoiseStrength = amoeboidRunNoiseStrength,
            FlagellumSenseInterval = flagellumSenseInterval,
            AmoeboidSenseInterval = amoeboidSenseInterval,
            FlagellumSenseIntervalJitter = flagellumSenseIntervalJitter,
            AmoeboidSenseIntervalJitter = amoeboidSenseIntervalJitter,
            EnableRunAndTumbleDebug = enableRunAndTumbleDebug,
            RunAndTumbleDebugWindowSeconds = runAndTumbleDebugWindowSeconds
        };
    }

    void UpdateLifecycle()
    {
        int resolution = planetGenerator != null ? planetGenerator.resolution : 1;
        lifecycleSystem.UpdateLifecycle(
            agents,
            populationState,
            currentStepDeltaTime,
            reproductionRate,
            enableCarbonLimitedDivision,
            divisionEnergyCost,
            replicationEnergyCost,
            divisionBiomassMultiple,
            divisionCarbonSplitToChild,
            maxOrganicCStore,
            resolution,
            (dir, cellIndex) => planetResourceMap.GetTemperature(dir, cellIndex),
            CalculateAgentColor,
            SpawnAgentFromPopulation,
            DepositDeathOrganicC,
            RegisterDeathCause,
            out averageOrganicCStore,
            out divisionEligibleAgentCount);
    }


    void TickMetabolism()
    {
        float tick = Mathf.Max(0.01f, metabolismTickSeconds);
        metabolismTickTimer += currentStepDeltaTime;

        while (metabolismTickTimer >= tick)
        {
            metabolismTickTimer -= tick;
            MetabolismTick(tick);
        }
    }

    void MetabolismTick(float dtTick)
    {
        var settings = new ReplicatorMetabolismSystem.Settings
        {
            BasalEnergyCostPerSecond = basalEnergyCostPerSecond,
            EnergyForFullSpeed = energyForFullSpeed,
            AerobicO2PerC = aerobicO2PerC,
            AerobicEnergyPerC = aerobicEnergyPerC,
            MaxOrganicCStore = maxOrganicCStore,
            PhotosynthesisCo2PerTickAtFullInsolation = photosynthesisCo2PerTickAtFullInsolation,
            PhotosynthesisEnergyPerCo2 = photosynthesisEnergyPerCo2,
            PhotosynthStoreFraction = photosynthStoreFraction,
            NightRespirationCPerTick = nightRespirationCPerTick,
            SaproCPerTick = saproCPerTick,
            SaproAssimilationFraction = saproAssimilationFraction,
            SaproRespireStoreCPerTick = saproRespireStoreCPerTick,
            HydrogenotrophyCO2PerTick = hydrogenotrophyCO2PerTick,
            HydrogenotrophyH2PerTick = hydrogenotrophyH2PerTick,
            HydrogenotrophyEnergyPerTick = hydrogenotrophyEnergyPerTick,
            HydrogenotrophyStoreFraction = hydrogenotrophyStoreFraction,
            ChemosynthesisCo2NeedPerTick = chemosynthesisCo2NeedPerTick,
            ChemosynthesisH2sNeedPerTick = chemosynthesisH2sNeedPerTick,
            ChemosynthesisEnergyPerTick = chemosynthesisEnergyPerTick,
            ChemosynthStoreFraction = chemosynthStoreFraction,
            ChemoRespirationCPerTick = chemoRespirationCPerTick,
            PredatorBasalCostMultiplier = predatorBasalCostMultiplier,
            PredatorMoveSpeedMultiplier = predatorMoveSpeedMultiplier,
            MinSpeedFactor = minSpeedFactor
        };

        metabolismSystem.MetabolismTick(
            agents,
            populationState,
            planetGenerator,
            planetResourceMap,
            settings,
            dtTick,
            ResolveEnergyDeathCause,
            DepositDeathOrganicC,
            RegisterDeathCause,
            out ReplicatorMetabolismSystem.DebugSnapshot debugSnapshot);

        debugChemoTempSum = debugSnapshot.ChemoTempSum;
        debugHydrogenTempSum = debugSnapshot.HydrogenTempSum;
        debugPhotoTempSum = debugSnapshot.PhotoTempSum;
        debugSaproTempSum = debugSnapshot.SaproTempSum;
        debugChemoTempCount = debugSnapshot.ChemoTempCount;
        debugHydrogenTempCount = debugSnapshot.HydrogenTempCount;
        debugPhotoTempCount = debugSnapshot.PhotoTempCount;
        debugSaproTempCount = debugSnapshot.SaproTempCount;
        debugChemoStressedCount = debugSnapshot.ChemoStressedCount;
        debugHydrogenStressedCount = debugSnapshot.HydrogenStressedCount;
        debugPhotoStressedCount = debugSnapshot.PhotoStressedCount;
        debugSaproStressedCount = debugSnapshot.SaproStressedCount;
    }




    void DepositPredationOrganicCAtLocation(Replicator agent, float amount)
    {
        if (planetResourceMap == null || planetGenerator == null)
        {
            return;
        }

        float depositAmount = Mathf.Max(0f, amount);
        if (depositAmount <= 0f)
        {
            return;
        }

        int resolution = Mathf.Max(1, planetGenerator.resolution);
        int cellIndex = PlanetGridIndexing.DirectionToCellIndex(agent.position.normalized, resolution);
        planetResourceMap.Add(ResourceType.OrganicC, cellIndex, depositAmount);
    }

    void DepositDeathOrganicC(Replicator agent)
    {
        if (planetResourceMap == null || planetGenerator == null)
        {
            return;
        }

        float stored = Mathf.Max(0f, agent.organicCStore);
        if (stored <= 0f)
        {
            return;
        }

        int resolution = Mathf.Max(1, planetGenerator.resolution);
        int cellIndex = PlanetGridIndexing.DirectionToCellIndex(agent.position.normalized, resolution);
        float depositAmount = stored;

        if (depositAmount > 0f)
        {
            planetResourceMap.Add(ResourceType.OrganicC, cellIndex, depositAmount);
        }
    }

    void RunMovementJob(bool populationStatePrimed)
    {
        ReplicatorMovementSystem.Settings settings = new ReplicatorMovementSystem.Settings
        {
            MoveSpeed = moveSpeed,
            TurnSpeed = turnSpeed,
            AmoeboidTurnRate = amoeboidTurnRate,
            AmoeboidMoveSpeedMultiplier = amoeboidMoveSpeedMultiplier,
            FlagellumTurnRate = flagellumTurnRate,
            FlagellumMoveSpeedMultiplier = flagellumMoveSpeedMultiplier,
            FlagellumDriftSuppression = flagellumDriftSuppression,
            AnchoredDriftMultiplier = 0.1f,
            MinSpeedFactor = minSpeedFactor
        };

        movementSystem.RunMovementJob(
            agents,
            populationState,
            settings,
            planetGenerator,
            currentStepDeltaTime,
            simulationTimeSeconds,
            populationStatePrimed);
    }

    void Reset()
    {
        if (planetGenerator == null)
        {
            planetGenerator = FindFirstObjectByType<PlanetGenerator>();
        }

        ResolvePlanetResourceMapReference();
    }

    void OnDestroy()
    {
        movementSystem.Dispose();
    }

    bool SpawnAgentFromPopulation(Replicator parent, out Replicator childAgent)
    {
        childAgent = null;

        if (agents.Count >= maxPopulation) return false;

        if (parent.traits.replicateOnlyInSea && !IsSeaLocation(parent.currentDirection))
        {
            return false;
        }

        Vector3 randomDir = parent.currentDirection + UnityEngine.Random.insideUnitSphere * spawnSpread;
        randomDir = randomDir.normalized;

        MetabolismType childMetabolism = parent.metabolism;
        if (UnityEngine.Random.value < Mathf.Clamp01(metabolismMutationChance))
        {
            if (parent.metabolism == MetabolismType.Hydrogenotrophy)
            {
                if (planetGenerator != null
                    && planetGenerator.PhotosynthesisUnlocked
                    && IsInsolatedLocation(parent.currentDirection)
                    && UnityEngine.Random.value < Mathf.Clamp01(hydrogenToPhotosynthesisMutationChance))
                {
                    childMetabolism = MetabolismType.Photosynthesis;
                }
                else if (UnityEngine.Random.value < Mathf.Clamp01(hydrogenToSulfurMutationChance))
                {
                    childMetabolism = MetabolismType.SulfurChemosynthesis;
                }
            }
            else if (allowReverseMetabolismMutation)
            {
                childMetabolism = MetabolismType.Hydrogenotrophy;
            }
        }

        if (childMetabolism != MetabolismType.Saprotrophy
            && childMetabolism != MetabolismType.Predation
            && UnityEngine.Random.value < Mathf.Clamp01(parent.metabolism == MetabolismType.Hydrogenotrophy ? hydrogenToSaprotrophyMutationChance : saprotrophyMutationChance)
            && CanMutateToSaprotrophy())
        {
            childMetabolism = MetabolismType.Saprotrophy;
        }

        if (enablePredators
            && childMetabolism == MetabolismType.Saprotrophy
            && parent.metabolism == MetabolismType.Saprotrophy
            && UnityEngine.Random.value < Mathf.Clamp01(predatorMutationChance)
            && CanMutateToPredation(parent))
        {
            childMetabolism = MetabolismType.Predation;
        }

        LocomotionType childLocomotion = ResolveInheritedLocomotion(parent);
        float childLocomotionSkill = ResolveInheritedLocomotionSkill(parent);

        // Reproduction should happen at/near the parent's current habitat.
        // `spawnOnlyInSea` is intended for initial/spontaneous seeding, while
        // `replicateOnlyInSea` controls whether a parent is allowed to divide on land.
        return SpawnAgentAtDirection(randomDir, parent.traits, parent, childMetabolism, childLocomotion, childLocomotionSkill, out childAgent, enforceSpawnOnlyInSeaTrait: false);
    }

    bool IsSaprotrophyUnlocked()
    {
        return planetGenerator != null && planetGenerator.SaprotrophyUnlocked;
    }

    bool CanMutateToPredation(Replicator parent)
    {
        if (!enablePredators || !IsSaprotrophyUnlocked() || parent == null)
        {
            return false;
        }

        if (!predatorRequiresMotility)
        {
            return true;
        }

        return IsMotile(parent);
    }

    LocomotionType ResolveInheritedLocomotion(Replicator parent)
    {
        LocomotionType locomotion = parent != null ? parent.locomotion : LocomotionType.PassiveDrift;

        if (parent == null || UnityEngine.Random.value >= Mathf.Clamp01(locomotionMutationChance))
        {
            return locomotion;
        }

        bool mutateToAnchored = UnityEngine.Random.value < Mathf.Clamp01(locomotionAnchoredMutationChance);

        if (mutateToAnchored)
        {
            if (locomotion == LocomotionType.PassiveDrift || locomotion == LocomotionType.Amoeboid)
            {
                return LocomotionType.Anchored;
            }

            if (locomotion == LocomotionType.Anchored
                && allowAnchoredToAmoeboidMutation
                && UnityEngine.Random.value < Mathf.Clamp01(anchoredToAmoeboidMutationChance))
            {
                return LocomotionType.Amoeboid;
            }
        }

        if (UnityEngine.Random.value < Mathf.Clamp01(locomotionUpgradeChance))
        {
            if (locomotion == LocomotionType.PassiveDrift)
            {
                return LocomotionType.Amoeboid;
            }

            if (locomotion == LocomotionType.Amoeboid)
            {
                return LocomotionType.Flagellum;
            }
        }

        return locomotion;
    }

    float ResolveInheritedLocomotionSkill(Replicator parent)
    {
        if (parent == null)
        {
            return 0f;
        }

        return Mathf.Clamp01(parent.locomotionSkill);
    }

    bool IsInsolatedLocation(Vector3 direction)
    {
        if (planetResourceMap == null)
        {
            return false;
        }

        return planetResourceMap.GetInsolation(direction.normalized) > 0f;
    }

    bool CanMutateToSaprotrophy()
    {
        if (!IsSaprotrophyUnlocked() || planetResourceMap == null)
        {
            return false;
        }

        const float minGlobalO2 = 0.01f;
        const float minGlobalOrganicC = 0.001f;

        float globalO2 = planetResourceMap.debugGlobalO2;
        float globalOrganicC = EstimateGlobalOrganicC();

        return globalO2 > minGlobalO2 && globalOrganicC > minGlobalOrganicC;
    }

    float EstimateGlobalOrganicC()
    {
        if (planetResourceMap == null || planetGenerator == null)
        {
            return 0f;
        }

        int resolution = Mathf.Max(1, planetGenerator.resolution);
        int cellCount = PlanetGridIndexing.GetCellCount(resolution);
        if (cellCount <= 0)
        {
            return 0f;
        }

        float totalOrganicC = 0f;
        for (int cell = 0; cell < cellCount; cell++)
        {
            totalOrganicC += planetResourceMap.Get(ResourceType.OrganicC, cell);
        }

        return totalOrganicC / cellCount;
    }

    bool SpawnAgentAtRandomLocation()
    {
        if (agents.Count >= maxPopulation) return false;
        Vector3 dir = GetSpawnDirectionCandidate();
        return SpawnAgentAtDirection(dir, CreateDefaultTraits(), null, MetabolismType.Hydrogenotrophy, LocomotionType.PassiveDrift, 0f, out _, enforceSpawnOnlyInSeaTrait: true);
    }

    bool SpawnAgentAtDirection(Vector3 direction, Replicator.Traits traits, Replicator parent, MetabolismType metabolism, LocomotionType locomotion, float locomotionSkill, out Replicator spawnedAgent, bool enforceSpawnOnlyInSeaTrait = true)
    {
        spawnedAgent = null;

        if (agents.Count >= maxPopulation) return false;

        Vector3 randomDir = direction.normalized;

        if (enforceSpawnOnlyInSeaTrait && traits.spawnOnlyInSea)
        {
            if (!TryFindSeaDirection(randomDir, out randomDir))
            {
                return false;
            }
        }

        Vector3 spawnPosition;
        Quaternion spawnRotation;

        float height = GetSurfaceHeight(randomDir);
        spawnPosition = randomDir * height;
        spawnRotation = Quaternion.FromToRotation(Vector3.up, randomDir);
        spawnRotation *= Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);

        float newLifespan = UnityEngine.Random.Range(minLifespan, maxLifespan);
        float movementSeed = UnityEngine.Random.Range(-1000f, 1000f);
        Replicator newAgent = new Replicator(spawnPosition, spawnRotation, newLifespan, baseAgentColor, traits, movementSeed, metabolism, locomotion, locomotionSkill);
        newAgent.age = parent == null ? UnityEngine.Random.Range(0f, newLifespan * 0.5f) : 0f;
        newAgent.energy = parent == null ? UnityEngine.Random.Range(0.1f, 0.5f) : Mathf.Max(0.1f, parent.energy * 0.5f);
        newAgent.size = 1f;

        AssignTemperatureTraits(newAgent, parent, metabolism);
        newAgent.moveDirection = randomDir;
        newAgent.desiredMoveDir = randomDir;
        newAgent.tumbleProbability = Mathf.Clamp(baseTumbleProbability, minTumbleProbability, maxTumbleProbability);
        newAgent.lastHabitatValue = 0f;
        newAgent.nextSenseTime = 0f;

        float baselineTarget = Mathf.Max(0.0001f, defaultBiomassTarget);
        if (parent == null)
        {
            newAgent.biomassTarget = baselineTarget;
        }
        else
        {
            float inheritedTarget = Mathf.Max(0.0001f, parent.biomassTarget);
            if (inheritedTarget <= 0f)
            {
                inheritedTarget = baselineTarget;
            }

            if (UnityEngine.Random.value < Mathf.Clamp01(biomassMutationChance))
            {
                float mutationScale = Mathf.Max(0f, biomassMutationScale);
                float mutationFactor = 1f + UnityEngine.Random.Range(-mutationScale, mutationScale);
                inheritedTarget *= Mathf.Max(0.1f, mutationFactor);
            }

            newAgent.biomassTarget = Mathf.Max(0.0001f, inheritedTarget);
        }

        agents.Add(newAgent);
        spawnedAgent = newAgent;
        return true;
    }

    void AssignTemperatureTraits(Replicator agent, Replicator parent, MetabolismType metabolism)
    {
        Vector2 baseRange = GetTempRangeForMetabolism(metabolism);
        float baseMin = Mathf.Min(baseRange.x, baseRange.y);
        float baseMax = Mathf.Max(baseRange.x, baseRange.y);

        float mutationChance = Mathf.Clamp01(tempMutationChance);
        float scale = Mathf.Abs(tempMutationScale);

        if (parent == null)
        {
            // Entire metabolism range is the optimal band
            agent.optimalTempMin = baseMin;
            agent.optimalTempMax = baseMax;

            agent.lethalTempMargin = Mathf.Max(1f, defaultLethalMargin);
            return;
        }

        bool metabolismChanged = parent.metabolism != metabolism;

        if (metabolismChanged)
        {
            // Rebase onto the new metabolism's range so newly-mutated children don't
            // inherit a temperature niche that belongs to another metabolism.
            agent.optimalTempMin = baseMin;
            agent.optimalTempMax = baseMax;
            agent.lethalTempMargin = Mathf.Max(1f, defaultLethalMargin);
        }
        else
        {
            // Inherit within the same metabolism.
            agent.optimalTempMin = parent.optimalTempMin;
            agent.optimalTempMax = parent.optimalTempMax;
            agent.lethalTempMargin = Mathf.Max(1f, parent.lethalTempMargin);
        }

        // Mutate the band edges slightly
        if (UnityEngine.Random.value < mutationChance)
            agent.optimalTempMin += UnityEngine.Random.Range(-scale, scale);

        if (UnityEngine.Random.value < mutationChance)
            agent.optimalTempMax += UnityEngine.Random.Range(-scale, scale);

        // Ensure ordering and minimum band width
        if (agent.optimalTempMin > agent.optimalTempMax)
        {
            float t = agent.optimalTempMin;
            agent.optimalTempMin = agent.optimalTempMax;
            agent.optimalTempMax = t;
        }

        float minWidth = 2f; // Kelvin
        if (agent.optimalTempMax - agent.optimalTempMin < minWidth)
        {
            float center = 0.5f * (agent.optimalTempMin + agent.optimalTempMax);
            agent.optimalTempMin = center - 0.5f * minWidth;
            agent.optimalTempMax = center + 0.5f * minWidth;
        }

        // Keep inherited/mutated temperatures in a physically sensible Kelvin range.
        agent.optimalTempMin = Mathf.Clamp(agent.optimalTempMin, 150f, 500f);
        agent.optimalTempMax = Mathf.Clamp(agent.optimalTempMax, 150f, 500f);

        if (UnityEngine.Random.value < mutationChance)
            agent.lethalTempMargin = Mathf.Max(1f, agent.lethalTempMargin + UnityEngine.Random.Range(-scale, scale));
    }

    Vector2 GetTempRangeForMetabolism(MetabolismType metabolism)
    {
        if (metabolism == MetabolismType.Hydrogenotrophy)
        {
            return hydrogenTempRange;
        }

        if (metabolism == MetabolismType.Photosynthesis)
        {
            return photoTempRange;
        }

        if (metabolism == MetabolismType.Saprotrophy)
        {
            return saproTempRange;
        }

        if (metabolism == MetabolismType.Predation)
        {
            return predatorTempRange;
        }

        return sulfurChemoTempRange;
    }

    Replicator.Traits CreateDefaultTraits()
    {
        return new Replicator.Traits(
            defaultSpawnOnlyInSea,
            defaultReplicateOnlyInSea,
            defaultMoveOnlyInSea,
            Mathf.Max(0.01f, defaultSurfaceMoveSpeedMultiplier)
        );
    }

    bool TryFindSeaDirection(Vector3 preferredDirection, out Vector3 seaDirection)
    {
        const int maxAttempts = 12;

        Vector3 candidate = preferredDirection.normalized;
        if (IsSeaLocation(candidate))
        {
            seaDirection = candidate;
            return true;
        }

        for (int i = 0; i < maxAttempts; i++)
        {
            candidate = UnityEngine.Random.onUnitSphere;
            if (IsSeaLocation(candidate))
            {
                seaDirection = candidate;
                return true;
            }
        }

        seaDirection = preferredDirection.normalized;
        return !planetGenerator.OceanEnabled;
    }

    float GetSurfaceHeight(Vector3 direction)
    {
        float displacement = planetGenerator.GetSurfaceRadius(direction);
        return displacement + 0.05f;
    }

    Color CalculateAgentColor(float age, float lifeRemaining, float energy, MetabolismType metabolism)
    {
        float intensity = 1.0f;
        float alpha = 1.0f;

        if (lifeRemaining < 3.0f)
        {
            float t = Mathf.Clamp01(lifeRemaining / 3.0f);
            intensity = Mathf.Lerp(0.01f, 1.0f, t);
            alpha = Mathf.Lerp(0f, 1.0f, t);
        }
        else if (age < 1.5f)
        {
            float t = age / 1.5f;
            intensity = Mathf.Lerp(8.0f, 1.0f, t);
        }

        if (colorByEnergy)
        {
            float energyScale = Mathf.Clamp01(energy * energyVisualMultiplier);
            intensity *= Mathf.Lerp(0.2f, 1.5f, energyScale);
        }

        Color predatorBaseColor = Color.Lerp(Color.red, Color.white, 1f - Mathf.Clamp01(predatorSpawnColorStrength));
        Color metabolismBaseColor = metabolism == MetabolismType.Photosynthesis
            ? Color.green
            : metabolism == MetabolismType.Saprotrophy
                ? Color.blue
                : metabolism == MetabolismType.Predation
                    ? predatorBaseColor
                    : metabolism == MetabolismType.Hydrogenotrophy ? new Color(0.86f, 1f, 0.98f) : Color.yellow;
        Color finalColor = metabolismBaseColor * intensity;
        finalColor.a = alpha;
        return finalColor;
    }

    void RenderAgents()
    {
        renderSystem.RenderAgents(
            agents,
            populationState,
            replicatorMesh,
            replicatorMaterial);
    }
}
