using UnityEngine;
using System.Collections.Generic;

public class ReplicatorManager : MonoBehaviour
{
    [Header("Settings")]
    public Mesh replicatorMesh;
    public Material replicatorMaterial;
    public PlanetGenerator planetGenerator;

    [Header("Population")]
    public int initialSpawnCount = 10;
    public int maxPopulation = 50000;

    // NEW: Base color for agents to allow for color manipulation
    public Color baseAgentColor = Color.yellow;

    [Header("Simulation")]
    public float moveSpeed = 0.3f;
    public float turnSpeed = 100.0f;
    public float spawnSpread = 0.5f; // Controls how far new agents can spawn from parents

    // The master list of all agents
    private List<Replicator> agents = new List<Replicator>();

    // Data structures for GPU Instancing
    private List<Matrix4x4> matrices = new List<Matrix4x4>();
    private List<Vector4> colors = new List<Vector4>(); // Shader uses Vector4 for Color
    private MaterialPropertyBlock propertyBlock;

    void Start()
    {
        // Initialize Instancing data
        propertyBlock = new MaterialPropertyBlock();

        // --- FIX: Pre-allocate BOTH color properties to max size ---
        // This prevents the persistent "exceeds array size" warning.
        Vector4[] maxColors = new Vector4[maxPopulation];
        for (int i = 0; i < maxColors.Length; i++) maxColors[i] = Vector4.one;

        int baseColorID = Shader.PropertyToID("_BaseColor");
        int colorID = Shader.PropertyToID("_Color");

        propertyBlock.SetVectorArray(baseColorID, maxColors);
        propertyBlock.SetVectorArray(colorID, maxColors);
        // -----------------------------------------------------------

        // Spawn initial population
        for (int i = 0; i < initialSpawnCount; i++)
        {
            SpawnAgent();
        }
    }

    void Update()
    {
        UpdateAgents();
        RenderAgents();
    }

    void SpawnAgent()
    {
        if (agents.Count >= maxPopulation) return;

        Vector3 randomDir;
        Vector3 spawnPosition;
        Quaternion spawnRotation;

        // --- FIX: CLUSTERED SPAWNING LOGIC ---
        if (agents.Count > 0)
        {
            // Pick a random parent agent
            Replicator parent = agents[Random.Range(0, agents.Count)];

            // Spawn near the parent's current direction
            randomDir = parent.currentDirection + Random.insideUnitSphere * spawnSpread;
            randomDir = randomDir.normalized;
        }
        else
        {
            // Initial population: Spawn randomly across the sphere
            randomDir = Random.onUnitSphere;
        }
        // -------------------------------------

        // 1. Calculate height
        float height = GetSurfaceHeight(randomDir);

        // 2. Determine position and rotation
        spawnPosition = randomDir * height;

        // Align the rotation
        spawnRotation = Quaternion.FromToRotation(Vector3.up, randomDir);
        spawnRotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        // 3. Create the new agent with the base color (for manipulation)
        // Note: Using 'baseAgentColor' from the Inspector now, instead of Random.ColorHSV()
        agents.Add(new Replicator(spawnPosition, spawnRotation, Random.Range(20f, 40f), baseAgentColor));
    }

    float GetSurfaceHeight(Vector3 direction)
    {
        float noise = planetGenerator.CalculateNoise(direction);
        float displacement = planetGenerator.radius * (1f + noise * planetGenerator.noiseMagnitude);
        return displacement + 0.05f;
    }

    void UpdateAgents()
    {
        float dt = Time.deltaTime;
        float radius = planetGenerator.radius;

        for (int i = agents.Count - 1; i >= 0; i--)
        {
            Replicator agent = agents[i];

            // 1. Age & Death
            agent.age += dt;
            float lifeRemaining = agent.maxLifespan - agent.age;

            // Fading Logic: Fades out in the last 5 seconds of life
            const float fadeTime = 5f;
            if (lifeRemaining < fadeTime)
            {
                // Calculate fade factor (1.0 to 0.0)
                float fadeFactor = Mathf.Clamp01(lifeRemaining / fadeTime);

                // Diminish the color's brightness and alpha
                agent.color = baseAgentColor * fadeFactor;
                agent.color.a = fadeFactor;
            }

            // Check for death (after fading)
            if (agent.age > agent.maxLifespan)
            {
                agents.RemoveAt(i);
                continue;
            }

            // 2. Replication (Clustered spawn via call in SpawnAgent)
            if (Random.value < 0.001f)
            {
                SpawnAgent();
            }

            // 3. Movement Logic (Rotation remains the same)
            Vector3 surfaceNormal = agent.position.normalized;
            Vector3 forward = agent.rotation * Vector3.forward;
            Quaternion travelRot = Quaternion.AngleAxis(moveSpeed * dt * 50f / radius, Vector3.Cross(surfaceNormal, forward));
            Vector3 newPosDirection = travelRot * agent.position;

            // 4. Snap to Surface Height
            float height = GetSurfaceHeight(newPosDirection.normalized);
            agent.position = newPosDirection.normalized * height;

            // 5. Align Rotation
            Vector3 newNormal = agent.position.normalized;
            Quaternion targetRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, newNormal), newNormal);
            agent.rotation = Quaternion.Slerp(agent.rotation, targetRot, dt * 5f);

            // Update the current direction for use in SpawnAgent
            agent.currentDirection = agent.position.normalized;
        }
    }

    void RenderAgents()
    {
        matrices.Clear();
        colors.Clear();

        for (int i = 0; i < agents.Count; i++)
        {
            Replicator a = agents[i];

            matrices.Add(Matrix4x4.TRS(a.position, a.rotation, Vector3.one * 0.1f));
            colors.Add(a.color);

            if (matrices.Count == 1023)
            {
                FlushBatch();
            }
        }

        if (matrices.Count > 0)
        {
            FlushBatch();
        }
    }

    void FlushBatch()
    {
        int baseColorID = Shader.PropertyToID("_BaseColor");
        int colorID = Shader.PropertyToID("_Color");

        // Use the safe 2-argument overload which takes the List.
        propertyBlock.SetVectorArray(baseColorID, colors);
        propertyBlock.SetVectorArray(colorID, colors);

        // Correct DrawMeshInstanced call
        Graphics.DrawMeshInstanced(
            replicatorMesh,
            0,
            replicatorMaterial,
            matrices,
            propertyBlock,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            true,               // receiveShadows
            0,                  // layer
            null,               // camera
            UnityEngine.Rendering.LightProbeUsage.Off
        );

        matrices.Clear();
        colors.Clear();
    }
}