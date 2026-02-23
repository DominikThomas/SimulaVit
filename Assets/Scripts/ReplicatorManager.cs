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
    public float spawnSpread = 0.5f;

    [Range(0f, 1f)]
    public float reproductionRate = 0.1f;

    public float minLifespan = 30f;
    public float maxLifespan = 60f;

    [Header("Metabolism")]
    public float metabolismTickSeconds = 0.5f;
    public float moveEnergyCostPerSecond = 0.05f;
    public float replicationEnergyCost = 0.5f;
    public float basalEnergyCostPerSecond = 0.01f;
    [Tooltip("CO2 consumed per metabolism tick for default chemosynthesis.")]
    public float chemosynthesisCo2NeedPerTick = 0.02f;
    [Tooltip("H2S consumed per metabolism tick for default chemosynthesis. Kept low because H2S is vent-localized.")]
    public float chemosynthesisH2sNeedPerTick = 0.001f;
    [Tooltip("Energy granted when one full chemosynthesis reaction tick is completed.")]
    public float chemosynthesisEnergyPerTick = 0.3f;

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
    public float seaSpawnPreference = 0.5f;

    [Header("Default Traits")]
    [Tooltip("If enabled, newly created replicators can only be spawned in sea locations.")]
    public bool defaultSpawnOnlyInSea = true;
    [Tooltip("If enabled, replicators only reproduce while currently in the sea.")]
    public bool defaultReplicateOnlyInSea = true;
    [Tooltip("If enabled, replicators stay in the sea and do not move onto land.")]
    public bool defaultMoveOnlyInSea = false;
    [Range(0.01f, 1f)]
    [Tooltip("Movement speed multiplier used by replicators while on land/surface.")]
    public float defaultSurfaceMoveSpeedMultiplier = 0.4f;

    [Header("Debug")]
    public bool colorByEnergy = false;
    [Range(0.05f, 4f)] public float energyVisualMultiplier = 1f;
    private List<Replicator> agents = new List<Replicator>();

    [SerializeField] private int activeAgentCount;
    private bool isInitialized;
    private float spawnAttemptTimer;
    private bool firstSpontaneousSpawnHappened;
    private float metabolismTickTimer;

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

            Quaternion travelRot = Quaternion.AngleAxis((MoveSpeed * speedMultiplier) * DeltaTime / Radius, Vector3.Cross(surfaceNormal, forward));

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
            planetGenerator = GetComponent<PlanetGenerator>();
        }

        if (planetGenerator == null)
        {
            planetGenerator = FindObjectOfType<PlanetGenerator>();
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
            planetResourceMap = FindObjectOfType<PlanetResourceMap>();
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
        activeAgentCount = agents.Count;
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

        Vector3 randomDir = Random.onUnitSphere;
        bool isSeaLocation = IsSeaLocation(randomDir);

        float locationMultiplier = GetLocationSpawnMultiplier(isSeaLocation);
        float spawnChance = Mathf.Clamp01(spontaneousSpawnChance * locationMultiplier);

        if (Random.value >= spawnChance)
        {
            return false;
        }

        return SpawnAgentAtDirection(randomDir, CreateDefaultTraits(), null);
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

    void UpdateLifecycle()
    {
        float dt = Time.deltaTime;
        float reproductionChance = reproductionRate * dt;

        for (int i = agents.Count - 1; i >= 0; i--)
        {
            Replicator agent = agents[i];
            agent.age += dt;

            if (agent.age > agent.maxLifespan)
            {
                agents.RemoveAt(i);
                continue;
            }

            float lifeRemaining = agent.maxLifespan - agent.age;
            agent.color = CalculateAgentColor(agent.age, lifeRemaining, agent.energy);

            if (Random.value < reproductionChance && agent.energy >= replicationEnergyCost)
            {
                if (SpawnAgentFromPopulation(agent))
                {
                    agent.energy = Mathf.Max(0f, agent.energy - replicationEnergyCost);
                }
            }
        }
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

    void MetabolismTick(float dtTick)
    {
        int resolution = Mathf.Max(1, planetGenerator.resolution);
        float totalCost = (Mathf.Max(0f, basalEnergyCostPerSecond) + Mathf.Max(0f, moveEnergyCostPerSecond)) * dtTick;

        for (int i = agents.Count - 1; i >= 0; i--)
        {
            Replicator agent = agents[i];
            int cellIndex = PlanetGridIndexing.DirectionToCellIndex(agent.position.normalized, resolution);

            float co2Need = Mathf.Max(0f, chemosynthesisCo2NeedPerTick);
            float h2sNeed = Mathf.Max(0f, chemosynthesisH2sNeedPerTick);

            float co2Available = planetResourceMap.Get(ResourceType.CO2, cellIndex);
            float h2sAvailable = planetResourceMap.Get(ResourceType.H2S, cellIndex);
            float co2Ratio = co2Need <= Mathf.Epsilon ? 1f : co2Available / co2Need;
            float h2sRatio = h2sNeed <= Mathf.Epsilon ? 1f : h2sAvailable / h2sNeed;
            float pulledRatio = Mathf.Clamp01(Mathf.Min(co2Ratio, h2sRatio));

            if (pulledRatio > 0f)
            {
                float co2Consumed = co2Need * pulledRatio;
                float h2sConsumed = h2sNeed * pulledRatio;

                planetResourceMap.Add(ResourceType.CO2, cellIndex, -co2Consumed);
                planetResourceMap.Add(ResourceType.H2S, cellIndex, -h2sConsumed);
                planetResourceMap.Add(ResourceType.S0, cellIndex, h2sConsumed);

                float producedEnergy = Mathf.Max(0f, chemosynthesisEnergyPerTick) * pulledRatio;
                agent.energy += producedEnergy;
            }

            agent.energy -= totalCost;

            if (agent.energy <= 0f)
            {
                agents.RemoveAt(i);
            }
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
        }

        ReplicatorUpdateJob job = new ReplicatorUpdateJob
        {
            Positions = jobPositions,
            Rotations = jobRotations,
            MoveOnlyInSea = jobMoveOnlyInSea,
            SurfaceMoveSpeedMultipliers = jobSurfaceMoveSpeedMultipliers,
            MovementSeeds = jobMovementSeeds,
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

        jobCapacity = Mathf.NextPowerOfTwo(requiredCount);
        jobPositions = new NativeArray<Vector3>(jobCapacity, Allocator.Persistent);
        jobRotations = new NativeArray<Quaternion>(jobCapacity, Allocator.Persistent);
        jobMoveOnlyInSea = new NativeArray<bool>(jobCapacity, Allocator.Persistent);
        jobSurfaceMoveSpeedMultipliers = new NativeArray<float>(jobCapacity, Allocator.Persistent);
        jobMovementSeeds = new NativeArray<float>(jobCapacity, Allocator.Persistent);
    }

    void Reset()
    {
        if (planetGenerator == null)
        {
            planetGenerator = FindObjectOfType<PlanetGenerator>();
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
    }

    bool SpawnAgentFromPopulation(Replicator parent)
    {
        if (agents.Count >= maxPopulation) return false;

        if (parent.traits.replicateOnlyInSea && !IsSeaLocation(parent.currentDirection))
        {
            return false;
        }

        Vector3 randomDir = parent.currentDirection + Random.insideUnitSphere * spawnSpread;
        randomDir = randomDir.normalized;

        return SpawnAgentAtDirection(randomDir, parent.traits, parent);
    }

    bool SpawnAgentAtRandomLocation()
    {
        if (agents.Count >= maxPopulation) return false;
        return SpawnAgentAtDirection(Random.onUnitSphere, CreateDefaultTraits(), null);
    }

    bool SpawnAgentAtDirection(Vector3 direction, Replicator.Traits traits, Replicator parent)
    {
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
        Replicator newAgent = new Replicator(spawnPosition, spawnRotation, newLifespan, baseAgentColor, traits, movementSeed);
        newAgent.age = parent == null ? Random.Range(0f, newLifespan * 0.5f) : 0f;
        newAgent.energy = parent == null ? Random.Range(0.1f, 0.5f) : Mathf.Max(0.1f, parent.energy * 0.5f);
        newAgent.size = 1f;

        agents.Add(newAgent);
        return true;
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

    Color CalculateAgentColor(float age, float lifeRemaining, float energy)
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

        Color finalColor = baseAgentColor * intensity;
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
