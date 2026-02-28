using UnityEngine;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;

public class ReplicatorManager : MonoBehaviour
{
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

    [Header("Temperature Preferences")]
    public Vector2 chemoTempRange = new Vector2(0.8f, 1.2f);
    public Vector2 photoTempRange = new Vector2(0.3f, 0.8f);
    public Vector2 saproTempRange = new Vector2(0.2f, 0.7f);
    public float defaultLethalMargin = 0.35f;
    [Range(0f, 1f)] public float tempMutationChance = 0.02f;
    public float tempMutationScale = 0.05f;

    [Header("Steering Habitat Score")]
    [Tooltip("Dominant weight for temperature fitness when computing per-cell habitat score.")]
    public float steerTempWeight = 5f;
    [Tooltip("Secondary weight for food fitness when computing per-cell habitat score.")]
    public float steerFoodWeight = 1f;
    [Tooltip("Food normalization scale for CO2. Values >= this count as fully available.")]
    public float steerGoodCO2 = 0.02f;
    [Tooltip("Food normalization scale for H2S. Values >= this count as fully available.")]
    public float steerGoodH2S = 0.001f;
    [Tooltip("Food normalization scale for O2. Values >= this count as fully available.")]
    public float steerGoodO2 = 0.02f;
    [Tooltip("Food normalization scale for OrganicC. Values >= this count as fully available.")]
    public float steerGoodOrganicC = 0.02f;

    [Header("Spawn Resource Bias")]
    public bool biasSpawnsToChemosynthesisResources = true;
    [Range(1, 256)] public int spawnResourceProbeAttempts = 128;
    [Tooltip("How strongly spontaneous/initial spawn chance scales with local H2S.")]
    public float h2sSpawnBiasWeight = 2.5f;
    [Tooltip("How strongly spontaneous/initial spawn chance scales with local CO2.")]
    public float co2SpawnBiasWeight = 0.5f;
    public float chemoSpawnOptimalTemp = 1.0f;
    public float chemoSpawnTempTolerance = 0.25f;


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
    [Range(0.05f, 4f)] public float energyVisualMultiplier = 1f;
    [Header("HUD")]
    [Tooltip("Draw a small runtime overlay with population and atmosphere stats.")]
    public bool showSimulationHud = true;
    private List<Replicator> agents = new List<Replicator>();

    [SerializeField] private int chemosynthAgentCount;
    [SerializeField] private int photosynthAgentCount;
    [SerializeField] private int saprotrophAgentCount;
    [SerializeField] private float averageOrganicCStore;
    [SerializeField] private int divisionEligibleAgentCount;
    private GUIStyle hudStyle;
    private GUIStyle hudBackgroundStyle;
    private bool isInitialized;
    private float spawnAttemptTimer;
    private bool firstSpontaneousSpawnHappened;
    private float metabolismTickTimer;
    private float metabolismDebugLogTimer;
    private float debugChemoTempSum;
    private float debugPhotoTempSum;
    private float debugSaproTempSum;
    private int debugChemoTempCount;
    private int debugPhotoTempCount;
    private int debugSaproTempCount;
    private int debugChemoStressedCount;
    private int debugPhotoStressedCount;
    private int debugSaproStressedCount;
    private float nextChemoSpawnDebugLogTime;
    private int[] chemoDeathCauseCounts;
    private int[] photoDeathCauseCounts;
    private int[] saproDeathCauseCounts;

    // Arrays for Batching
    private Matrix4x4[] matrixBatch = new Matrix4x4[1023];
    private Vector4[] colorBatch = new Vector4[1023];
    private MaterialPropertyBlock propertyBlock;

    // Reused job buffers to avoid per-frame NativeArray allocations.
    private NativeArray<Vector3> jobPositions;
    private NativeArray<Quaternion> jobRotations;
    private NativeArray<bool> jobMoveOnlyInSea;
    private NativeArray<float> jobSurfaceMoveSpeedMultipliers;
    private NativeArray<float> jobMovementSeeds;
    private NativeArray<float> jobSpeedFactors;
    private int jobCapacity;

    // Cache shader property IDs once.
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int EmissionID = Shader.PropertyToID("_EmissionColor");

    // --- JOB STRUCT DEFINITION ---
    public struct ReplicatorUpdateJob : IJobParallelFor
    {
        public NativeArray<Vector3> Positions;
        public NativeArray<Quaternion> Rotations;
        public NativeArray<bool> MoveOnlyInSea;
        public NativeArray<float> SurfaceMoveSpeedMultipliers;
        public NativeArray<float> MovementSeeds;
        public NativeArray<float> SpeedFactors;

        // Simulation parameters
        public float DeltaTime;
        public float MoveSpeed;
        public float TurnSpeed;
        public float Radius;
        public float TimeVal;

        // --- NEW: Noise Parameters for Surface Snap ---
        public float NoiseMagnitude;
        public float NoiseRoughness;
        public Vector3 NoiseOffset;
        public int NumLayers;
        public float Persistence;
        public float OceanThreshold;
        public float OceanDepth;
        public bool OceanEnabled;

        public void Execute(int index)
        {
            Vector3 pos = Positions[index];
            Quaternion rot = Rotations[index];
            Vector3 surfaceNormal = pos.normalized;

            // 1. Turning (per-agent noise with independent phase offsets)
            float seed = MovementSeeds[index];
            float turnNoiseA = SimpleNoise.Evaluate(surfaceNormal * 3.1f + new Vector3(seed, TimeVal * 0.31f, 0f));
            float turnNoiseB = SimpleNoise.Evaluate(surfaceNormal * 4.7f + new Vector3(TimeVal * 0.19f, seed * 1.37f, 0f));
            float turnNoise = Mathf.Clamp(turnNoiseA + turnNoiseB, -1f, 1f);
            float turnAmount = turnNoise * TurnSpeed * DeltaTime * 35f;
            rot = Quaternion.AngleAxis(turnAmount, surfaceNormal) * rot;

            // 2. Movement (Arc across sphere)
            Vector3 forward = rot * Vector3.forward;
            Vector3 lateralAxis = Vector3.Cross(surfaceNormal, forward);
            float wobble = SimpleNoise.Evaluate(surfaceNormal * 6.2f + new Vector3(MovementSeeds[index] * 0.73f, 0f, TimeVal * 0.43f));
            forward = (forward + lateralAxis * wobble * 0.35f).normalized;

            bool moveOnlyInSea = MoveOnlyInSea[index];
            float currentNoise = CalculateNoise(surfaceNormal);
            bool currentlyInSea = !OceanEnabled || currentNoise < OceanThreshold;
            float speedMultiplier = currentlyInSea ? 1f : SurfaceMoveSpeedMultipliers[index];
            float speedFactor = SpeedFactors[index];

            Quaternion travelRot = Quaternion.AngleAxis((MoveSpeed * speedMultiplier * speedFactor) * DeltaTime / Radius, Vector3.Cross(surfaceNormal, forward));

            // Get the new direction vector (normalized)
            Vector3 newDirection = (travelRot * pos).normalized;

            if (OceanEnabled && moveOnlyInSea)
            {
                float nextNoise = CalculateNoise(newDirection);
                bool nextInSea = nextNoise < OceanThreshold;
                if (!nextInSea)
                {
                    newDirection = surfaceNormal;
                }
            }

            // 3. HEIGHT SNAP
            // We calculate the terrain height right here in the thread
            float terrainNoise = CalculateNoise(newDirection);
            float displacement = GetSurfaceRadiusFromNoise(terrainNoise);

            // Apply height + slight offset so they sit ON the ground, not IN it
            Vector3 newPos = newDirection * (displacement + 0.05f);

            // 4. Rotation Alignment
            Vector3 newNormal = newPos.normalized;
            Quaternion targetRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, newNormal), newNormal);
            rot = Quaternion.Slerp(rot, targetRot, DeltaTime * 5f);

            Positions[index] = newPos;
            Rotations[index] = rot;
        }

        private float GetSurfaceRadiusFromNoise(float noise)
        {
            float finalNoise = noise;

            if (OceanEnabled && noise < OceanThreshold)
            {
                float t = OceanThreshold > 0f ? Mathf.Clamp01(noise / OceanThreshold) : 0f;
                float minNoise = OceanThreshold * (1f - OceanDepth);
                finalNoise = Mathf.Lerp(minNoise, OceanThreshold, t);
            }

            return Radius * (1f + finalNoise * NoiseMagnitude);
        }

        // Helper function to replicate PlanetGenerator.CalculateNoise inside the Job
        private float CalculateNoise(Vector3 point)
        {
            float noiseValue = 0;
            float frequency = NoiseRoughness;
            float amplitude = 1;
            float maxPossibleHeight = 0;

            for (int i = 0; i < NumLayers; i++)
            {
                // We call SimpleNoise directly. Since it's a static class, this is allowed!
                Vector3 samplePoint = point * frequency + NoiseOffset;
                float singleLayerNoise = SimpleNoise.Evaluate(samplePoint);
                singleLayerNoise = (singleLayerNoise + 1) * 0.5f;

                noiseValue += singleLayerNoise * amplitude;
                maxPossibleHeight += amplitude;

                amplitude *= Persistence;
                frequency *= 2;
            }

            return maxPossibleHeight > 0f ? noiseValue / maxPossibleHeight : 0f;
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

        propertyBlock = new MaterialPropertyBlock();
        isInitialized = true;
        EnsureDeathCauseCounters();

        for (int i = 0; i < initialSpawnCount; i++) SpawnAgentAtRandomLocation();
    }

    void Update()
    {
        if (!isInitialized) return;

        UpdateLifecycle();
        TickMetabolism();
        HandleSpontaneousSpawning();
        RunMovementJob();
        RenderAgents();
        UpdateMetabolismCounts();
        LogMetabolismDebugThrottled();
    }

    void OnGUI()
    {
        if (!showSimulationHud || !isInitialized)
        {
            return;
        }

        EnsureHudStyles();

        int totalAgents = agents.Count;
        int[] totalByLocomotion = new int[4];
        int[] chemosynthByLocomotion = new int[4];
        int[] photosynthByLocomotion = new int[4];
        int[] saprotrophByLocomotion = new int[4];

        for (int i = 0; i < agents.Count; i++)
        {
            int locomotionIndex = Mathf.Clamp((int)agents[i].locomotion, 0, totalByLocomotion.Length - 1);
            totalByLocomotion[locomotionIndex]++;

            if (agents[i].metabolism == MetabolismType.Photosynthesis)
            {
                photosynthByLocomotion[locomotionIndex]++;
            }
            else if (agents[i].metabolism == MetabolismType.Saprotrophy)
            {
                saprotrophByLocomotion[locomotionIndex]++;
            }
            else
            {
                chemosynthByLocomotion[locomotionIndex]++;
            }
        }

        float globalCo2 = planetResourceMap != null ? planetResourceMap.debugGlobalCO2 : 0f;
        float globalO2 = planetResourceMap != null ? planetResourceMap.debugGlobalO2 : 0f;
        float atmosphereTotal = Mathf.Max(0.0001f, globalCo2 + globalO2);
        float co2Pct = (globalCo2 / atmosphereTotal) * 100f;
        float o2Pct = (globalO2 / atmosphereTotal) * 100f;

        string atmosphereText =
            "Atmosphere (global average)\n" +
            $"CO2: {globalCo2:0.000} ({co2Pct:0.0}%)\n" +
            $"O2: {globalO2:0.000} ({o2Pct:0.0}%)";

        string FormatLocomotionCounts(int[] counts)
        {
            return $"{counts[0]}/{counts[1]}/{counts[2]}/{counts[3]}";
        }

        string replicatorsText =
            "Replicators (Passive/Amoeboid/Flagellum/Anchored)\n" +
            $"Total: {FormatLocomotionCounts(totalByLocomotion)}\n" +
            $"<color=#FFD54A>Chemosynthesis:</color> {FormatLocomotionCounts(chemosynthByLocomotion)}";

        if (photosynthAgentCount > 0)
        {
            replicatorsText += $"\n<color=#79E07E>Photosynthesis:</color> {FormatLocomotionCounts(photosynthByLocomotion)}";
        }

        if (saprotrophAgentCount > 0)
        {
            replicatorsText += $"\n<color=#62B0FF>Saprotroph:</color> {FormatLocomotionCounts(saprotrophByLocomotion)}";
        }

        const float panelWidth = 250f;
        const float padding = 8f;
        const float lineHeight = 20f;
        float rightX = Screen.width - panelWidth - padding;

        float atmosphereHeight = (atmosphereText.Split('\n').Length * lineHeight) + (padding * 2f);
        float replicatorHeight = (replicatorsText.Split('\n').Length * lineHeight) + (padding * 2f);

        Rect atmosphereRect = new Rect(rightX, padding, panelWidth, atmosphereHeight);
        GUI.Box(atmosphereRect, GUIContent.none, hudBackgroundStyle);
        GUI.Label(new Rect(atmosphereRect.x + padding, atmosphereRect.y + padding, panelWidth - 2f * padding, atmosphereHeight - 2f * padding), atmosphereText, hudStyle);

        Rect replicatorRect = new Rect(rightX, Screen.height - replicatorHeight - padding, panelWidth, replicatorHeight);
        GUI.Box(replicatorRect, GUIContent.none, hudBackgroundStyle);
        GUI.Label(new Rect(replicatorRect.x + padding, replicatorRect.y + padding, panelWidth - 2f * padding, replicatorHeight - 2f * padding), replicatorsText, hudStyle);
    }

    void EnsureHudStyles()
    {
        if (hudStyle != null && hudBackgroundStyle != null)
        {
            return;
        }

        hudStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            richText = true,
            alignment = TextAnchor.UpperLeft,
            normal =
            {
                textColor = Color.white
            }
        };

        Texture2D backgroundTexture = new Texture2D(1, 1);
        backgroundTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
        backgroundTexture.Apply();

        hudBackgroundStyle = new GUIStyle(GUI.skin.box)
        {
            normal =
            {
                background = backgroundTexture
            }
        };
    }


    void UpdateMetabolismCounts()
    {
        int chemo = 0;
        int photo = 0;
        int sapro = 0;

        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i].metabolism == MetabolismType.Photosynthesis)
            {
                photo++;
            }
            else if (agents[i].metabolism == MetabolismType.SulfurChemosynthesis)
            {
                chemo++;
            }
            else
            {
                sapro++;
            }
        }

        chemosynthAgentCount = chemo;
        photosynthAgentCount = photo;
        saprotrophAgentCount = sapro;
    }



    void LogMetabolismDebugThrottled()
    {
        metabolismDebugLogTimer += Time.deltaTime;
        if (metabolismDebugLogTimer < 3f)
        {
            return;
        }

        metabolismDebugLogTimer = 0f;
        bool unlocked = planetGenerator != null && planetGenerator.PhotosynthesisUnlocked;

        string chemoTempText = FormatTemperatureDebug(debugChemoTempSum, debugChemoTempCount, debugChemoStressedCount);
        string photoTempText = FormatTemperatureDebug(debugPhotoTempSum, debugPhotoTempCount, debugPhotoStressedCount);
        string saproTempText = FormatTemperatureDebug(debugSaproTempSum, debugSaproTempCount, debugSaproStressedCount);


        Debug.Log(
            $"Metabolism: chemo={chemosynthAgentCount} photo={photosynthAgentCount} sapro={saprotrophAgentCount} " +
            $"photoUnlocked={unlocked} saproUnlocked={IsSaprotrophyUnlocked()} " +
            $"temp[chemo:{chemoTempText} photo:{photoTempText} sapro:{saproTempText}] avgOrganicC={averageOrganicCStore:F3} divisionEligible={divisionEligibleAgentCount}");
        Debug.Log($"DeathCauses: chemo[{FormatDeathCauseDistribution(chemoDeathCauseCounts)}] photo[{FormatDeathCauseDistribution(photoDeathCauseCounts)}] sapro[{FormatDeathCauseDistribution(saproDeathCauseCounts)}]");
        ResetDeathCauseCounters();
    }


    void EnsureDeathCauseCounters()
    {
        int len = System.Enum.GetValues(typeof(DeathCause)).Length;
        if (chemoDeathCauseCounts == null || chemoDeathCauseCounts.Length != len) chemoDeathCauseCounts = new int[len];
        if (photoDeathCauseCounts == null || photoDeathCauseCounts.Length != len) photoDeathCauseCounts = new int[len];
        if (saproDeathCauseCounts == null || saproDeathCauseCounts.Length != len) saproDeathCauseCounts = new int[len];
    }

    void ResetDeathCauseCounters()
    {
        if (chemoDeathCauseCounts == null || photoDeathCauseCounts == null || saproDeathCauseCounts == null)
        {
            return;
        }

        System.Array.Clear(chemoDeathCauseCounts, 0, chemoDeathCauseCounts.Length);
        System.Array.Clear(photoDeathCauseCounts, 0, photoDeathCauseCounts.Length);
        System.Array.Clear(saproDeathCauseCounts, 0, saproDeathCauseCounts.Length);
    }

    void RegisterDeathCause(MetabolismType metabolism, DeathCause cause)
    {
        EnsureDeathCauseCounters();
        int causeIndex = Mathf.Clamp((int)cause, 0, chemoDeathCauseCounts.Length - 1);

        int[] counts = metabolism == MetabolismType.Photosynthesis
            ? photoDeathCauseCounts
            : (metabolism == MetabolismType.Saprotrophy ? saproDeathCauseCounts : chemoDeathCauseCounts);

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
            case DeathCause.Lack_Light: return "Light";
            case DeathCause.Lack_OrganicC_Food: return "OrgC";
            case DeathCause.Lack_O2: return "O2";
            case DeathCause.Lack_StoredC: return "StoredC";
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

    string FormatTemperatureDebug(float tempSum, int count, int stressedCount)
    {
        if (count <= 0)
        {
            return "n/a";
        }

        float averageTemp = tempSum / count;
        float stressedFraction = (float)stressedCount / count;
        return $"avg={averageTemp:0.00},stressed={stressedFraction:P0}";
    }
    void HandleSpontaneousSpawning()
    {
        if (!enableSpontaneousSpawning) return;

        if (!firstSpontaneousSpawnHappened && Time.timeSinceLevelLoad >= guaranteedFirstSpawnWithinSeconds)
        {
            if (SpawnAgentAtRandomLocation())
            {
                firstSpontaneousSpawnHappened = true;
            }
        }

        float interval = Mathf.Max(0.05f, spawnAttemptInterval);
        spawnAttemptTimer += Time.deltaTime;

        while (spawnAttemptTimer >= interval)
        {
            spawnAttemptTimer -= interval;

            if (TryRandomSpontaneousSpawn())
            {
                firstSpontaneousSpawnHappened = true;
            }
        }
    }

    bool TryRandomSpontaneousSpawn()
    {
        if (agents.Count >= maxPopulation) return false;

        Vector3 randomDir = GetSpawnDirectionCandidate();
        bool isSeaLocation = IsSeaLocation(randomDir);

        float locationMultiplier = GetLocationSpawnMultiplier(isSeaLocation);
        float spawnChance = Mathf.Clamp01(spontaneousSpawnChance * locationMultiplier);

        if (Random.value >= spawnChance)
        {
            return false;
        }

        return SpawnAgentAtDirection(randomDir, CreateDefaultTraits(), null, MetabolismType.SulfurChemosynthesis, LocomotionType.PassiveDrift, 0f, out _);
    }


    Vector3 GetSpawnDirectionCandidate()
    {
        if (!biasSpawnsToChemosynthesisResources || planetResourceMap == null || planetGenerator == null)
        {
            return Random.onUnitSphere;
        }

        int attempts = Mathf.Max(1, spawnResourceProbeAttempts);
        Vector3 bestDirection = Random.onUnitSphere;
        float bestScore = -1f;

        for (int i = 0; i < attempts; i++)
        {
            Vector3 candidate = Random.onUnitSphere;
            float score = GetChemosynthesisSpawnScore(candidate);
            if (score > bestScore)
            {
                bestScore = score;
                bestDirection = candidate;
            }
        }

        return bestDirection;
    }

    float GetChemosynthesisSpawnScore(Vector3 direction)
    {
        int resolution = Mathf.Max(1, planetGenerator.resolution);
        Vector3 normalizedDir = direction.normalized;
        int cellIndex = PlanetGridIndexing.DirectionToCellIndex(normalizedDir, resolution);

        float co2Need = Mathf.Max(0.0001f, chemosynthesisCo2NeedPerTick);
        float h2sNeed = Mathf.Max(0.0001f, chemosynthesisH2sNeedPerTick);

        float co2Availability = planetResourceMap.Get(ResourceType.CO2, cellIndex) / co2Need;
        float h2sAvailability = planetResourceMap.Get(ResourceType.H2S, cellIndex) / h2sNeed;

        float chemistryScore = (Mathf.Max(0f, co2SpawnBiasWeight) * co2Availability)
                             + (Mathf.Max(0f, h2sSpawnBiasWeight) * h2sAvailability);

        float temp = planetResourceMap.GetTemperature(normalizedDir, cellIndex);
        float tolerance = Mathf.Max(0.0001f, chemoSpawnTempTolerance);
        float d = Mathf.Abs(temp - chemoSpawnOptimalTemp);
        float tempFactor = Mathf.Clamp01(1f - (d / tolerance));
        float temperedFactor = Mathf.Lerp(0.1f, 1f, tempFactor);

        float score = chemistryScore * temperedFactor;

        if (Time.timeSinceLevelLoad >= nextChemoSpawnDebugLogTime)
        {
            nextChemoSpawnDebugLogTime = Time.timeSinceLevelLoad + 8f;
            Debug.Log($"Chemo spawn score: chemistry={chemistryScore:0.00} temp={temp:0.00} tempFactor={tempFactor:0.00} final={score:0.00}");
        }

        return score;
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

    public float ComputeHabitatScore(Replicator agent, Vector3 dir, int cellIndex)
    {
        if (agent == null || planetResourceMap == null)
        {
            return 0f;
        }

        Vector3 normalizedDir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.up;
        float temperature = planetResourceMap.GetTemperature(normalizedDir, cellIndex);
        float tempFitness = ComputeTemperatureFitness(agent, temperature);
        float foodFitness = ComputeFoodFitness(agent, normalizedDir, cellIndex);

        float score = Mathf.Max(0f, steerTempWeight) * tempFitness
                    + Mathf.Max(0f, steerFoodWeight) * foodFitness;

        if (float.IsNaN(score) || float.IsInfinity(score))
        {
            return 0f;
        }

        return Mathf.Clamp(score, 0f, 100f);
    }

    float ComputeTemperatureFitness(Replicator agent, float temperature)
    {
        float optimalTemp = 0.5f * (agent.optimalTempMin + agent.optimalTempMax);
        float tempTolerance = Mathf.Max(0.0001f, 0.5f * (agent.optimalTempMax - agent.optimalTempMin));
        float lethalMargin = Mathf.Max(0.0001f, agent.lethalTempMargin);

        float distFromOptimal = Mathf.Abs(temperature - optimalTemp);
        float safeBand = tempTolerance + lethalMargin;

        if (distFromOptimal <= tempTolerance)
        {
            return 1f;
        }

        float outsideTolerance = distFromOptimal - tempTolerance;
        float fitness = 1f - (outsideTolerance / safeBand);
        return Mathf.Clamp01(fitness);
    }

    float ComputeFoodFitness(Replicator agent, Vector3 normalizedDir, int cellIndex)
    {
        float co2 = NormalizeResource(ResourceType.CO2, cellIndex, steerGoodCO2);

        switch (agent.metabolism)
        {
            case MetabolismType.SulfurChemosynthesis:
            {
                float h2s = NormalizeResource(ResourceType.H2S, cellIndex, steerGoodH2S);
                return Mathf.Min(h2s, co2);
            }
            case MetabolismType.Photosynthesis:
            {
                float light = Mathf.Clamp01(planetResourceMap.GetInsolation(normalizedDir));
                return Mathf.Min(light, co2);
            }
            case MetabolismType.Saprotrophy:
            {
                float organicC = NormalizeResource(ResourceType.OrganicC, cellIndex, steerGoodOrganicC);
                float o2 = NormalizeResource(ResourceType.O2, cellIndex, steerGoodO2);
                return Mathf.Min(organicC, o2);
            }
            default:
                return 0f;
        }
    }

    float NormalizeResource(ResourceType resourceType, int cellIndex, float goodEnoughScale)
    {
        float scale = Mathf.Max(0.0001f, goodEnoughScale);
        float value = planetResourceMap.Get(resourceType, cellIndex);
        float normalized = Mathf.Clamp01(value / scale);
        return float.IsNaN(normalized) || float.IsInfinity(normalized) ? 0f : normalized;
    }

    void UpdateLifecycle()
    {
        float dt = Time.deltaTime;
        float reproductionChance = reproductionRate * dt;
        float organicCSum = 0f;
        int eligibleForDivisionCount = 0;

        for (int i = agents.Count - 1; i >= 0; i--)
        {
            Replicator agent = agents[i];
            agent.age += dt;

            if (agent.age > agent.maxLifespan)
            {
                RegisterDeathCause(agent.metabolism, DeathCause.OldAge);
                DepositDeathOrganicC(agent);
                agents.RemoveAt(i);
                continue;
            }

            float lifeRemaining = agent.maxLifespan - agent.age;
            agent.color = CalculateAgentColor(agent.age, lifeRemaining, agent.energy, agent.metabolism);

            organicCSum += Mathf.Max(0f, agent.organicCStore);

            bool hasEnergyForDivision = enableCarbonLimitedDivision
                ? agent.energy >= Mathf.Max(0f, divisionEnergyCost)
                : agent.energy >= replicationEnergyCost;

            bool hasCarbonForDivision = true;
            if (enableCarbonLimitedDivision)
            {
                float target = Mathf.Max(0.0001f, agent.biomassTarget);
                float divisionThreshold = Mathf.Max(1f, divisionBiomassMultiple) * target;
                hasCarbonForDivision = agent.organicCStore >= divisionThreshold;
                if (hasCarbonForDivision)
                {
                    eligibleForDivisionCount++;
                }
            }

            if (Random.value < reproductionChance && hasEnergyForDivision && hasCarbonForDivision)
            {
                int resolution = Mathf.Max(1, planetGenerator.resolution);
                Vector3 dir = agent.position.normalized;
                int cellIndex = PlanetGridIndexing.DirectionToCellIndex(dir, resolution);
                float temp = planetResourceMap.GetTemperature(dir, cellIndex);

                float min = agent.optimalTempMin;
                float max = agent.optimalTempMax;

                bool insideOptimalBand = (temp >= min && temp <= max);

                if (insideOptimalBand && SpawnAgentFromPopulation(agent, out Replicator childAgent))
                {
                    if (enableCarbonLimitedDivision)
                    {
                        agent.energy = Mathf.Max(0f, agent.energy - Mathf.Max(0f, divisionEnergyCost));

                        float totalC = Mathf.Max(0f, agent.organicCStore);
                        float toChild = totalC * Mathf.Clamp01(divisionCarbonSplitToChild);
                        childAgent.organicCStore = Mathf.Clamp(toChild, 0f, maxOrganicCStore);
                        agent.organicCStore = Mathf.Max(0f, totalC - toChild);
                    }
                    else
                    {
                        agent.energy = Mathf.Max(0f, agent.energy - replicationEnergyCost);
                    }
                }
            }
        }

        if (agents.Count > 0)
        {
            averageOrganicCStore = organicCSum / agents.Count;
        }
        else
        {
            averageOrganicCStore = 0f;
        }

        divisionEligibleAgentCount = eligibleForDivisionCount;
    }


    void TickMetabolism()
    {
        float tick = Mathf.Max(0.01f, metabolismTickSeconds);
        metabolismTickTimer += Time.deltaTime;

        while (metabolismTickTimer >= tick)
        {
            metabolismTickTimer -= tick;
            MetabolismTick(tick);
        }
    }

    private float AerobicRespireFromStore(
        Replicator agent,
        int cellIndex,
        float cMaxThisTick,
        float o2PerC,
        float energyPerC)
    {
        if (agent.organicCStore <= 0f || cMaxThisTick <= 0f || o2PerC <= 0f || energyPerC <= 0f)
            return 0f;

        float cUsed = Mathf.Min(cMaxThisTick, agent.organicCStore);
        if (cUsed <= 0f) return 0f;

        // Scale by available O2
        float o2Needed = cUsed * o2PerC;
        float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
        float ratio = o2Needed <= Mathf.Epsilon ? 1f : Mathf.Clamp01(o2Available / o2Needed);
        cUsed *= ratio;

        if (cUsed <= 0f) return 0f;

        float o2Consumed = cUsed * o2PerC;
        planetResourceMap.Add(ResourceType.O2, cellIndex, -o2Consumed);
        planetResourceMap.Add(ResourceType.CO2, cellIndex, cUsed); // simplified 1:1 C -> CO2

        agent.organicCStore = Mathf.Max(0f, agent.organicCStore - cUsed);
        float gainedEnergy = cUsed * energyPerC;
        agent.energy += gainedEnergy;

        return gainedEnergy;
    }

    void MetabolismTick(float dtTick)
    {
        int resolution = Mathf.Max(1, planetGenerator.resolution);
        float basalCost = Mathf.Max(0f, basalEnergyCostPerSecond) * dtTick;
        float safeEnergyForFullSpeed = Mathf.Max(0.0001f, energyForFullSpeed);

        // Shared chemistry: aerobic respiration (OrganicC + O2 -> CO2 + energy)
        float o2PerC = Mathf.Max(0f, aerobicO2PerC);
        float energyPerC = Mathf.Max(0f, aerobicEnergyPerC);
        float maxStore = Mathf.Max(0f, maxOrganicCStore);

        debugChemoTempSum = 0f;
        debugPhotoTempSum = 0f;
        debugSaproTempSum = 0f;
        debugChemoTempCount = 0;
        debugPhotoTempCount = 0;
        debugSaproTempCount = 0;
        debugChemoStressedCount = 0;
        debugPhotoStressedCount = 0;
        debugSaproStressedCount = 0;

        for (int i = agents.Count - 1; i >= 0; i--)
        {
            Replicator agent = agents[i];
            Vector3 dir = agent.position.normalized;
            int cellIndex = PlanetGridIndexing.DirectionToCellIndex(dir, resolution);

            float temp = planetResourceMap.GetTemperature(dir, cellIndex);

            float min = agent.optimalTempMin;
            float max = agent.optimalTempMax;
            float lethalMargin = Mathf.Max(0.0001f, agent.lethalTempMargin);

            float d = 0f;

            if (temp < min)
                d = min - temp;
            else if (temp > max)
                d = temp - max;

            bool insideOptimalBand = d <= 0f;
            bool lethalTemperature = d > lethalMargin;

            float stress = insideOptimalBand ? 0f : Mathf.Clamp01(d / lethalMargin);
            float performance = insideOptimalBand ? 1f : Mathf.Lerp(0.7f, 0.1f, stress);

            if (agent.metabolism == MetabolismType.Photosynthesis)
            {
                debugPhotoTempSum += temp;
                debugPhotoTempCount++;
                if (!insideOptimalBand) debugPhotoStressedCount++;
            }
            else if (agent.metabolism == MetabolismType.Saprotrophy)
            {
                debugSaproTempSum += temp;
                debugSaproTempCount++;
                if (!insideOptimalBand) debugSaproStressedCount++;
            }
            else
            {
                debugChemoTempSum += temp;
                debugChemoTempCount++;
                if (!insideOptimalBand) debugChemoStressedCount++;
            }

            if (lethalTemperature)
            {
                DeathCause temperatureDeathCause = temp > max
                    ? DeathCause.TemperatureTooHigh
                    : DeathCause.TemperatureTooLow;

                RegisterDeathCause(agent.metabolism, temperatureDeathCause);
                DepositDeathOrganicC(agent);
                agents.RemoveAt(i);
                continue;
            }

            if (agent.metabolism == MetabolismType.Photosynthesis)
            {
                float insolation = Mathf.Clamp01(planetResourceMap.GetInsolation(dir));
                bool lackCo2 = false;
                bool lackLight = false;
                bool lackO2 = false;
                bool lackStoredC = false;

                if (insolation > 0f)
                {
                    float co2Need = Mathf.Max(0f, photosynthesisCo2PerTickAtFullInsolation) * insolation;
                    float co2Available = planetResourceMap.Get(ResourceType.CO2, cellIndex);
                    float co2Consumed = Mathf.Min(co2Need, co2Available);

                    lackCo2 = co2Need > 0f && co2Consumed <= Mathf.Epsilon;

                    if (co2Consumed > 0f)
                    {
                        planetResourceMap.Add(ResourceType.CO2, cellIndex, -co2Consumed);
                        planetResourceMap.Add(ResourceType.O2, cellIndex, co2Consumed);

                        float producedEnergy = co2Consumed * Mathf.Max(0f, photosynthesisEnergyPerCo2) * performance;
                        agent.energy += producedEnergy;

                        float storedOrganicC = Mathf.Max(0f, photosynthStoreFraction) * co2Consumed;
                        if (storedOrganicC > 0f)
                            agent.organicCStore = Mathf.Clamp(agent.organicCStore + storedOrganicC, 0f, maxStore);
                    }
                }
                else
                {
                    float desiredResp = Mathf.Max(0f, nightRespirationCPerTick);
                    float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                    bool hasStore = agent.organicCStore > 0f;
                    lackLight = !hasStore;
                    lackStoredC = !hasStore && desiredResp > 0f;
                    lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;

                    // Night / no light: respire stored organic carbon using the shared aerobic pathway
                    float gained = AerobicRespireFromStore(
                        agent,
                        cellIndex,
                        desiredResp,
                        o2PerC,
                        energyPerC);
                    if (gained > 0f)
                    {
                        agent.energy -= gained * (1f - performance);
                        lackLight = false;
                        lackStoredC = false;
                        lackO2 = false;
                    }
                }

                agent.starveCo2Seconds = UpdateStarveTimer(agent.starveCo2Seconds, lackCo2, dtTick);
                agent.starveLightSeconds = UpdateStarveTimer(agent.starveLightSeconds, lackLight, dtTick);
                agent.starveO2Seconds = UpdateStarveTimer(agent.starveO2Seconds, lackO2, dtTick);
                agent.starveStoredCSeconds = UpdateStarveTimer(agent.starveStoredCSeconds, lackStoredC, dtTick);
                agent.starveH2sSeconds = 0f;
                agent.starveOrganicCFoodSeconds = 0f;
            }
            else if (agent.metabolism == MetabolismType.Saprotrophy)
            {
                // Saprotrophy = aerobic heterotrophy (detritus respiration).
                // IMPORTANT: Do NOT remove full intake if O2 is limiting.
                // Only remove what is actually stored + respired.

                float envC = planetResourceMap.Get(ResourceType.OrganicC, cellIndex);
                float intakeCap = Mathf.Max(0f, saproCPerTick);
                float desiredIntake = Mathf.Min(envC, intakeCap);

                float assimilation = Mathf.Clamp01(saproAssimilationFraction);
                bool lackFood = desiredIntake <= Mathf.Epsilon;
                bool lackO2 = false;
                bool lackStoredC = false;

                if (desiredIntake > 0f)
                {
                    // Split desired intake into "store" and "respire" fractions
                    float desiredStore = desiredIntake * assimilation;
                    float desiredRespire = desiredIntake - desiredStore;

                    // Cap how much can be stored by remaining storage capacity
                    float storeCapacity = Mathf.Max(0f, maxStore - agent.organicCStore);
                    float actualStore = Mathf.Min(desiredStore, storeCapacity);

                    // Respiration is limited by available O2
                    float actualRespire = 0f;
                    if (desiredRespire > 0f && o2PerC > 0f && energyPerC > 0f)
                    {
                        float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                        float maxRespireByO2 = o2Available / o2PerC;          // how much C can be respired with available O2
                        actualRespire = Mathf.Clamp(desiredRespire, 0f, maxRespireByO2);
                        lackO2 = desiredRespire > 0f && actualRespire <= Mathf.Epsilon;
                    }

                    float totalActuallyUsed = actualStore + actualRespire;

                    if (totalActuallyUsed > 0f)
                    {
                        // Now subtract exactly what we used from environment OrganicC
                        planetResourceMap.Add(ResourceType.OrganicC, cellIndex, -totalActuallyUsed);

                        // Apply storage
                        if (actualStore > 0f)
                            agent.organicCStore = Mathf.Clamp(agent.organicCStore + actualStore, 0f, maxStore);

                        // Apply respiration
                        if (actualRespire > 0f)
                        {
                            float o2Consumed = actualRespire * o2PerC;
                            planetResourceMap.Add(ResourceType.O2, cellIndex, -o2Consumed);
                            planetResourceMap.Add(ResourceType.CO2, cellIndex, actualRespire);
                            agent.energy += actualRespire * energyPerC * performance;
                            lackO2 = false;
                        }
                    }
                    // else: O2=0 and store is full -> cannot use any detritus; leave it in the environment.
                }
                else
                {
                    // No food in tile: allow saprotroph to survive briefly by respiring its own store
                    float desiredResp = Mathf.Max(0f, saproRespireStoreCPerTick);
                    float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                    bool hasStore = agent.organicCStore > 0f;
                    lackStoredC = !hasStore && desiredResp > 0f;
                    lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;

                    float gained = AerobicRespireFromStore(
                        agent,
                        cellIndex,
                        desiredResp,
                        o2PerC,
                        energyPerC);
                    if (gained > 0f)
                    {
                        agent.energy -= gained * (1f - performance);
                        lackStoredC = false;
                        lackO2 = false;
                    }
                }

                agent.starveOrganicCFoodSeconds = UpdateStarveTimer(agent.starveOrganicCFoodSeconds, lackFood, dtTick);
                agent.starveO2Seconds = UpdateStarveTimer(agent.starveO2Seconds, lackO2, dtTick);
                agent.starveStoredCSeconds = UpdateStarveTimer(agent.starveStoredCSeconds, lackStoredC, dtTick);
                agent.starveCo2Seconds = 0f;
                agent.starveH2sSeconds = 0f;
                agent.starveLightSeconds = 0f;
            }
            else
            {
                // Sulfur chemosynthesis: CO2 + H2S -> energy + S0, and fix some CO2 into organicCStore (chemoautotrophy)
                float co2Need = Mathf.Max(0f, chemosynthesisCo2NeedPerTick);
                float h2sNeed = Mathf.Max(0f, chemosynthesisH2sNeedPerTick);

                float co2Available = planetResourceMap.Get(ResourceType.CO2, cellIndex);
                float h2sAvailable = planetResourceMap.Get(ResourceType.H2S, cellIndex);
                float co2Ratio = co2Need <= Mathf.Epsilon ? 1f : co2Available / co2Need;
                float h2sRatio = h2sNeed <= Mathf.Epsilon ? 1f : h2sAvailable / h2sNeed;
                float pulledRatio = Mathf.Clamp01(Mathf.Min(co2Ratio, h2sRatio));

                bool lackCo2 = false;
                bool lackH2s = false;
                bool lackO2 = false;
                bool lackStoredC = false;

                if (pulledRatio > 0f)
                {
                    float co2Consumed = co2Need * pulledRatio;
                    float h2sConsumed = h2sNeed * pulledRatio;

                    planetResourceMap.Add(ResourceType.CO2, cellIndex, -co2Consumed);
                    planetResourceMap.Add(ResourceType.H2S, cellIndex, -h2sConsumed);
                    planetResourceMap.Add(ResourceType.S0, cellIndex, h2sConsumed);

                    float producedEnergy = Mathf.Max(0f, chemosynthesisEnergyPerTick) * pulledRatio * performance;
                    agent.energy += producedEnergy;

                    // NEW: chemoautotroph carbon fixation into storage (biomass/reserves)
                    float storeFrac = Mathf.Clamp01(chemosynthStoreFraction);
                    float fixedC = co2Consumed * storeFrac;
                    if (fixedC > 0f)
                        agent.organicCStore = Mathf.Clamp(agent.organicCStore + fixedC, 0f, maxStore);
                }
                else
                {
                    lackCo2 = co2Need > 0f && co2Available <= Mathf.Epsilon;
                    lackH2s = h2sNeed > 0f && h2sAvailable <= Mathf.Epsilon;

                    float desiredResp = Mathf.Max(0f, chemoRespirationCPerTick);
                    bool hasStore = agent.organicCStore > 0f;
                    float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                    lackStoredC = !hasStore && desiredResp > 0f;
                    lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;
                }

                agent.starveCo2Seconds = UpdateStarveTimer(agent.starveCo2Seconds, lackCo2, dtTick);
                agent.starveH2sSeconds = UpdateStarveTimer(agent.starveH2sSeconds, lackH2s, dtTick);
                agent.starveO2Seconds = UpdateStarveTimer(agent.starveO2Seconds, lackO2, dtTick);
                agent.starveStoredCSeconds = UpdateStarveTimer(agent.starveStoredCSeconds, lackStoredC, dtTick);
                agent.starveLightSeconds = 0f;
                agent.starveOrganicCFoodSeconds = 0f;
            }

            float stressedBasal = basalCost * (1f + stress);
            agent.speedFactor = Mathf.Clamp((agent.energy / safeEnergyForFullSpeed) * performance, minSpeedFactor, 1f);
            float movementCost = 0f;
            switch (agent.locomotion)
            {
                case LocomotionType.PassiveDrift:
                case LocomotionType.Anchored:
                    movementCost = 0f;
                    break;
                case LocomotionType.Amoeboid:
                case LocomotionType.Flagellum:
                    // TODO: Re-enable active locomotion energy costs when movement tuning is finalized.
                    movementCost = 0f;
                    break;
                default:
                    movementCost = 0f;
                    break;
            }
            agent.energy -= (stressedBasal + movementCost);

            if (agent.energy <= 0f)
            {
                RegisterDeathCause(agent.metabolism, ResolveEnergyDeathCause(agent));
                DepositDeathOrganicC(agent); // make sure this deposits agent.organicCStore into environment OrganicC
                agents.RemoveAt(i);
            }
        }
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

    void RunMovementJob()
    {
        int count = agents.Count;
        if (count == 0) return;

        EnsureJobBufferCapacity(count);

        for (int i = 0; i < count; i++)
        {
            this.jobPositions[i] = agents[i].position;
            this.jobRotations[i] = agents[i].rotation;
            this.jobMoveOnlyInSea[i] = agents[i].traits.moveOnlyInSea;
            this.jobSurfaceMoveSpeedMultipliers[i] = Mathf.Max(0.01f, agents[i].traits.surfaceMoveSpeedMultiplier);
            this.jobMovementSeeds[i] = agents[i].movementSeed;
            this.jobSpeedFactors[i] = Mathf.Clamp(agents[i].speedFactor, minSpeedFactor, 1f);
        }

        ReplicatorUpdateJob job = new ReplicatorUpdateJob
        {
            Positions = jobPositions,
            Rotations = jobRotations,
            MoveOnlyInSea = jobMoveOnlyInSea,
            SurfaceMoveSpeedMultipliers = jobSurfaceMoveSpeedMultipliers,
            MovementSeeds = jobMovementSeeds,
            SpeedFactors = jobSpeedFactors,
            DeltaTime = Time.deltaTime,
            MoveSpeed = moveSpeed,
            TurnSpeed = turnSpeed,
            Radius = planetGenerator.radius,
            TimeVal = Time.time,

            // Pass Planet Settings to Job
            NoiseMagnitude = planetGenerator.noiseMagnitude,
            NoiseRoughness = planetGenerator.noiseRoughness,
            NoiseOffset = planetGenerator.noiseOffset,
            NumLayers = planetGenerator.numLayers,
            Persistence = planetGenerator.persistence,
            OceanThreshold = planetGenerator.OceanThresholdNoise,
            OceanDepth = planetGenerator.oceanDepth,
            OceanEnabled = planetGenerator.OceanEnabled
        };

        JobHandle handle = job.Schedule(count, 32);
        handle.Complete();

        for (int i = 0; i < count; i++)
        {
            var agent = agents[i];
            agent.position = jobPositions[i];
            agent.rotation = jobRotations[i];
            agent.currentDirection = agent.position.normalized;
        }
    }

    void EnsureJobBufferCapacity(int requiredCount)
    {
        if (jobCapacity >= requiredCount) return;

        if (jobPositions.IsCreated) jobPositions.Dispose();
        if (jobRotations.IsCreated) jobRotations.Dispose();
        if (jobMoveOnlyInSea.IsCreated) jobMoveOnlyInSea.Dispose();
        if (jobSurfaceMoveSpeedMultipliers.IsCreated) jobSurfaceMoveSpeedMultipliers.Dispose();
        if (jobMovementSeeds.IsCreated) jobMovementSeeds.Dispose();
        if (jobSpeedFactors.IsCreated) jobSpeedFactors.Dispose();

        jobCapacity = Mathf.NextPowerOfTwo(requiredCount);
        jobPositions = new NativeArray<Vector3>(jobCapacity, Allocator.Persistent);
        jobRotations = new NativeArray<Quaternion>(jobCapacity, Allocator.Persistent);
        jobMoveOnlyInSea = new NativeArray<bool>(jobCapacity, Allocator.Persistent);
        jobSurfaceMoveSpeedMultipliers = new NativeArray<float>(jobCapacity, Allocator.Persistent);
        jobMovementSeeds = new NativeArray<float>(jobCapacity, Allocator.Persistent);
        jobSpeedFactors = new NativeArray<float>(jobCapacity, Allocator.Persistent);
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
        if (jobPositions.IsCreated) jobPositions.Dispose();
        if (jobRotations.IsCreated) jobRotations.Dispose();
        if (jobMoveOnlyInSea.IsCreated) jobMoveOnlyInSea.Dispose();
        if (jobSurfaceMoveSpeedMultipliers.IsCreated) jobSurfaceMoveSpeedMultipliers.Dispose();
        if (jobMovementSeeds.IsCreated) jobMovementSeeds.Dispose();
        if (jobSpeedFactors.IsCreated) jobSpeedFactors.Dispose();
    }

    bool SpawnAgentFromPopulation(Replicator parent, out Replicator childAgent)
    {
        childAgent = null;

        if (agents.Count >= maxPopulation) return false;

        if (parent.traits.replicateOnlyInSea && !IsSeaLocation(parent.currentDirection))
        {
            return false;
        }

        Vector3 randomDir = parent.currentDirection; // + Random.insideUnitSphere * spawnSpread;
        randomDir = randomDir.normalized;

        MetabolismType childMetabolism = parent.metabolism;
        if (Random.value < Mathf.Clamp01(metabolismMutationChance))
        {
            if (parent.metabolism == MetabolismType.SulfurChemosynthesis)
            {
                if (planetGenerator != null
                    && planetGenerator.PhotosynthesisUnlocked
                    && IsInsolatedLocation(parent.currentDirection))
                {
                    childMetabolism = MetabolismType.Photosynthesis;
                }
            }
            else if (allowReverseMetabolismMutation)
            {
                childMetabolism = MetabolismType.SulfurChemosynthesis;
            }
        }

        if (childMetabolism != MetabolismType.Saprotrophy
            && Random.value < Mathf.Clamp01(saprotrophyMutationChance)
            && CanMutateToSaprotrophy())
        {
            childMetabolism = MetabolismType.Saprotrophy;
        }

        LocomotionType childLocomotion = ResolveInheritedLocomotion(parent);
        float childLocomotionSkill = ResolveInheritedLocomotionSkill(parent);

        return SpawnAgentAtDirection(randomDir, parent.traits, parent, childMetabolism, childLocomotion, childLocomotionSkill, out childAgent);
    }

    bool IsSaprotrophyUnlocked()
    {
        return planetGenerator != null && planetGenerator.SaprotrophyUnlocked;
    }

    LocomotionType ResolveInheritedLocomotion(Replicator parent)
    {
        LocomotionType locomotion = parent != null ? parent.locomotion : LocomotionType.PassiveDrift;

        if (parent == null || Random.value >= Mathf.Clamp01(locomotionMutationChance))
        {
            return locomotion;
        }

        bool mutateToAnchored = Random.value < Mathf.Clamp01(locomotionAnchoredMutationChance);

        if (mutateToAnchored)
        {
            if (locomotion == LocomotionType.PassiveDrift || locomotion == LocomotionType.Amoeboid)
            {
                return LocomotionType.Anchored;
            }

            if (locomotion == LocomotionType.Anchored
                && allowAnchoredToAmoeboidMutation
                && Random.value < Mathf.Clamp01(anchoredToAmoeboidMutationChance))
            {
                return LocomotionType.Amoeboid;
            }
        }

        if (Random.value < Mathf.Clamp01(locomotionUpgradeChance))
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
        return SpawnAgentAtDirection(dir, CreateDefaultTraits(), null, MetabolismType.SulfurChemosynthesis, LocomotionType.PassiveDrift, 0f, out _);
    }

    bool SpawnAgentAtDirection(Vector3 direction, Replicator.Traits traits, Replicator parent, MetabolismType metabolism, LocomotionType locomotion, float locomotionSkill, out Replicator spawnedAgent)
    {
        spawnedAgent = null;

        if (agents.Count >= maxPopulation) return false;

        Vector3 randomDir = direction.normalized;

        if (traits.spawnOnlyInSea)
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
        spawnRotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        float newLifespan = Random.Range(minLifespan, maxLifespan);
        float movementSeed = Random.Range(-1000f, 1000f);
        Replicator newAgent = new Replicator(spawnPosition, spawnRotation, newLifespan, baseAgentColor, traits, movementSeed, metabolism, locomotion, locomotionSkill);
        newAgent.age = parent == null ? Random.Range(0f, newLifespan * 0.5f) : 0f;
        newAgent.energy = parent == null ? Random.Range(0.1f, 0.5f) : Mathf.Max(0.1f, parent.energy * 0.5f);
        newAgent.size = 1f;

        AssignTemperatureTraits(newAgent, parent, metabolism);
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

            if (Random.value < Mathf.Clamp01(biomassMutationChance))
            {
                float mutationScale = Mathf.Max(0f, biomassMutationScale);
                float mutationFactor = 1f + Random.Range(-mutationScale, mutationScale);
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

            agent.lethalTempMargin = Mathf.Max(0.05f, defaultLethalMargin);
            return;
        }

        bool metabolismChanged = parent.metabolism != metabolism;

        if (metabolismChanged)
        {
            // Rebase onto the new metabolism's range so newly-mutated children don't
            // inherit a temperature niche that belongs to another metabolism.
            agent.optimalTempMin = baseMin;
            agent.optimalTempMax = baseMax;
            agent.lethalTempMargin = Mathf.Max(0.05f, defaultLethalMargin);
        }
        else
        {
            // Inherit within the same metabolism.
            agent.optimalTempMin = parent.optimalTempMin;
            agent.optimalTempMax = parent.optimalTempMax;
            agent.lethalTempMargin = Mathf.Max(0.05f, parent.lethalTempMargin);
        }

        // Mutate the band edges slightly
        if (Random.value < mutationChance)
            agent.optimalTempMin += Random.Range(-scale, scale);

        if (Random.value < mutationChance)
            agent.optimalTempMax += Random.Range(-scale, scale);

        // Ensure ordering and minimum band width
        if (agent.optimalTempMin > agent.optimalTempMax)
        {
            float t = agent.optimalTempMin;
            agent.optimalTempMin = agent.optimalTempMax;
            agent.optimalTempMax = t;
        }

        float minWidth = 0.02f; // tunable
        if (agent.optimalTempMax - agent.optimalTempMin < minWidth)
        {
            float center = 0.5f * (agent.optimalTempMin + agent.optimalTempMax);
            agent.optimalTempMin = center - 0.5f * minWidth;
            agent.optimalTempMax = center + 0.5f * minWidth;
        }

        // Optionally clamp to global reasonable range
        agent.optimalTempMin = Mathf.Clamp(agent.optimalTempMin, 0f, 2f);
        agent.optimalTempMax = Mathf.Clamp(agent.optimalTempMax, 0f, 2f);

        if (Random.value < mutationChance)
            agent.lethalTempMargin = Mathf.Max(0.05f, agent.lethalTempMargin + Random.Range(-scale, scale));
    }

    Vector2 GetTempRangeForMetabolism(MetabolismType metabolism)
    {
        if (metabolism == MetabolismType.Photosynthesis)
        {
            return photoTempRange;
        }

        if (metabolism == MetabolismType.Saprotrophy)
        {
            return saproTempRange;
        }

        return chemoTempRange;
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
            candidate = Random.onUnitSphere;
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

        Color metabolismBaseColor = metabolism == MetabolismType.Photosynthesis
            ? Color.green
            : metabolism == MetabolismType.Saprotrophy ? Color.blue : Color.yellow;
        Color finalColor = metabolismBaseColor * intensity;
        finalColor.a = alpha;
        return finalColor;
    }

    void RenderAgents()
    {
        int batchCount = 0;
        int totalAgents = agents.Count;

        for (int i = 0; i < totalAgents; i++)
        {
            Replicator a = agents[i];
            matrixBatch[batchCount] = Matrix4x4.TRS(a.position, a.rotation, Vector3.one * (0.1f * Mathf.Max(0.1f, a.size)));
            colorBatch[batchCount] = a.color;
            batchCount++;

            if (batchCount == 1023)
            {
                FlushBatch(batchCount);
                batchCount = 0;
            }
        }

        if (batchCount > 0)
        {
            FlushBatch(batchCount);
        }
    }

    void FlushBatch(int count)
    {
        Vector4 transparentBlack = Vector4.zero;
        for (int i = count; i < 1023; i++)
        {
            colorBatch[i] = transparentBlack;
        }

        propertyBlock.SetVectorArray(BaseColorID, colorBatch);
        propertyBlock.SetVectorArray(ColorID, colorBatch);
        propertyBlock.SetVectorArray(EmissionID, colorBatch);

        Graphics.DrawMeshInstanced(
            replicatorMesh,
            0,
            replicatorMaterial,
            matrixBatch,
            count,
            propertyBlock,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            true,
            0,
            null,
            UnityEngine.Rendering.LightProbeUsage.Off
        );
    }
}
