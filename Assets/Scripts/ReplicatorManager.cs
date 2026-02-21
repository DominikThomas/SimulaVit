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

    [Header("Spontaneous Spawning")]
    [Tooltip("Keeps attempting random world spawns even when all replicators die out.")]
    public bool enableSpontaneousSpawning = true;
    [Tooltip("Seconds between random spawn attempts.")]
    public float spawnAttemptInterval = 1.0f;
    [Range(0f, 1f)]
    [Tooltip("Base chance for each random spawn attempt.")]
    public float spontaneousSpawnChance = 0.02f;
    [Tooltip("Guarantees at least one spontaneous spawn before this many seconds elapse.")]
    public float guaranteedFirstSpawnWithinSeconds = 10f;
    [Range(0f, 1f)]
    [Tooltip("0 = land-only bias, 0.5 = balanced, 1 = sea-only bias (when ocean is enabled).")]
    public float seaSpawnPreference = 0.5f;

    [Header("Debug")]
    private List<Replicator> agents = new List<Replicator>();

    [SerializeField] private int activeAgentCount;
    private bool isInitialized;
    private float spawnAttemptTimer;
    private bool firstSpontaneousSpawnHappened;

    // Arrays for Batching
    private Matrix4x4[] matrixBatch = new Matrix4x4[1023];
    private Vector4[] colorBatch = new Vector4[1023];
    private MaterialPropertyBlock propertyBlock;

    // Reused job buffers to avoid per-frame NativeArray allocations.
    private NativeArray<Vector3> jobPositions;
    private NativeArray<Quaternion> jobRotations;
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

            // 1. Turning (Pseudo-random noise)
            float noiseVal = Mathf.Sin(pos.x * 0.5f + TimeVal) * Mathf.Cos(pos.z * 0.5f + TimeVal);
            float turnAmount = noiseVal * TurnSpeed * DeltaTime * 20f;
            rot = rot * Quaternion.AngleAxis(turnAmount, surfaceNormal);

            // 2. Movement (Arc across sphere)
            Vector3 forward = rot * Vector3.forward;
            Quaternion travelRot = Quaternion.AngleAxis(MoveSpeed * DeltaTime / Radius, Vector3.Cross(surfaceNormal, forward));

            // Get the new direction vector (normalized)
            Vector3 newDirection = (travelRot * pos).normalized;

            // 3. HEIGHT SNAP (The Missing Fix!)
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

    void Start()
    {
        if (replicatorMesh == null || replicatorMaterial == null || planetGenerator == null)
        {
            Debug.LogError("ReplicatorManager is missing required references (mesh/material/planetGenerator).", this);
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

        return SpawnAgentAtDirection(randomDir);
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
            agent.color = CalculateAgentColor(agent.age, lifeRemaining);

            if (Random.value < reproductionChance)
            {
                SpawnAgentFromPopulation();
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
        }

        ReplicatorUpdateJob job = new ReplicatorUpdateJob
        {
            Positions = jobPositions,
            Rotations = jobRotations,
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

        jobCapacity = Mathf.NextPowerOfTwo(requiredCount);
        jobPositions = new NativeArray<Vector3>(jobCapacity, Allocator.Persistent);
        jobRotations = new NativeArray<Quaternion>(jobCapacity, Allocator.Persistent);
    }

    void OnDestroy()
    {
        if (jobPositions.IsCreated) jobPositions.Dispose();
        if (jobRotations.IsCreated) jobRotations.Dispose();
    }

    bool SpawnAgentFromPopulation()
    {
        if (agents.Count >= maxPopulation) return false;

        Vector3 randomDir;

        if (agents.Count > 0)
        {
            Replicator parent = agents[Random.Range(0, agents.Count)];
            randomDir = parent.currentDirection + Random.insideUnitSphere * spawnSpread;
            randomDir = randomDir.normalized;
        }
        else
        {
            randomDir = Random.onUnitSphere;
        }

        return SpawnAgentAtDirection(randomDir);
    }

    bool SpawnAgentAtRandomLocation()
    {
        if (agents.Count >= maxPopulation) return false;
        return SpawnAgentAtDirection(Random.onUnitSphere);
    }

    bool SpawnAgentAtDirection(Vector3 direction)
    {
        if (agents.Count >= maxPopulation) return false;

        Vector3 randomDir = direction.normalized;
        Vector3 spawnPosition;
        Quaternion spawnRotation;

        float height = GetSurfaceHeight(randomDir);
        spawnPosition = randomDir * height;
        spawnRotation = Quaternion.FromToRotation(Vector3.up, randomDir);
        spawnRotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        float newLifespan = Random.Range(minLifespan, maxLifespan);
        Replicator newAgent = new Replicator(spawnPosition, spawnRotation, newLifespan, baseAgentColor);
        newAgent.age = Random.Range(0f, newLifespan * 0.5f);

        agents.Add(newAgent);
        return true;
    }

    float GetSurfaceHeight(Vector3 direction)
    {
        float displacement = planetGenerator.GetSurfaceRadius(direction);
        return displacement + 0.05f;
    }

    Color CalculateAgentColor(float age, float lifeRemaining)
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
            matrixBatch[batchCount] = Matrix4x4.TRS(a.position, a.rotation, Vector3.one * 0.1f);
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
