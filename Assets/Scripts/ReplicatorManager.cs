using UnityEngine;
using System.Collections.Generic;

public class ReplicatorManager : MonoBehaviour
{
    [Header("Settings")]
    public Mesh replicatorMesh;
    public Material replicatorMaterial;
    public PlanetGenerator planetGenerator;

    [Header("Population")]
    public int initialSpawnCount = 100;
    public int maxPopulation = 5000;

    public Color baseAgentColor = Color.cyan;

    [Header("Simulation")]
    public float moveSpeed = 0.5f;
    public float turnSpeed = 2.0f;
    public float spawnSpread = 0.5f;

    private List<Replicator> agents = new List<Replicator>();

    // Arrays for Batching (reused to avoid GC)
    private Matrix4x4[] matrixBatch = new Matrix4x4[1023];
    private Vector4[] colorBatch = new Vector4[1023];
    private MaterialPropertyBlock propertyBlock;

    void Start()
    {
        propertyBlock = new MaterialPropertyBlock();

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

        float height = GetSurfaceHeight(randomDir);
        spawnPosition = randomDir * height;
        spawnRotation = Quaternion.FromToRotation(Vector3.up, randomDir);
        spawnRotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        float newLifespan = Random.Range(30f, 60f);

        Replicator newAgent = new Replicator(spawnPosition, spawnRotation, newLifespan, baseAgentColor);

        // FIX #1: Give a large random starting age to significantly desynchronize death times
        newAgent.age = Random.Range(0f, newLifespan * 0.5f);

        agents.Add(newAgent);
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

            // 1. AGE LOGIC
            agent.age += dt;
            float lifeRemaining = agent.maxLifespan - agent.age;

            // Check for death
            if (agent.age > agent.maxLifespan)
            {
                agents.RemoveAt(i);
                continue;
            }

            // --- VISUALS: COLOR CALCULATION ---
            agent.color = CalculateAgentColor(agent.age, lifeRemaining);

            // 2. REPLICATION
            if (Random.value < 0.001f)
            {
                SpawnAgent();
            }

            // 3. MOVEMENT
            Vector3 surfaceNormal = agent.position.normalized;
            Vector3 forward = agent.rotation * Vector3.forward;
            Quaternion travelRot = Quaternion.AngleAxis(moveSpeed * dt * 50f / radius, Vector3.Cross(surfaceNormal, forward));
            Vector3 newPosDirection = travelRot * agent.position;

            // 4. POSITION SNAP
            float height = GetSurfaceHeight(newPosDirection.normalized);
            agent.position = newPosDirection.normalized * height;

            // 5. ROTATION ALIGN
            Vector3 newNormal = agent.position.normalized;
            Quaternion targetRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, newNormal), newNormal);
            agent.rotation = Quaternion.Slerp(agent.rotation, targetRot, dt * 5f);

            agent.currentDirection = agent.position.normalized;
        }
    }

    Color CalculateAgentColor(float age, float lifeRemaining)
    {
        float intensity = 1.0f;
        float alpha = 1.0f;

        // PRIORITIZE DEATH: If dying, force dimming and transparency fade.
        if (lifeRemaining < 3.0f)
        {
            // Smooth dimming from 1.0 to 0.0 over the last 3 seconds
            float t = Mathf.Clamp01(lifeRemaining / 3.0f);

            // Intensity fades the glow/color.
            intensity = Mathf.Lerp(0.01f, 1.0f, t);

            // Alpha fades the visibility of the mesh itself.
            alpha = Mathf.Lerp(0f, 1.0f, t);
        }
        else if (age < 1.5f)
        {
            // Flare up on birth
            float t = age / 1.5f;
            intensity = Mathf.Lerp(8.0f, 1.0f, t);
        }

        // Apply color and intensity
        Color finalColor = baseAgentColor * intensity;

        // Set the transparency of the color
        finalColor.a = alpha;

        return finalColor;
    }

    void RenderAgents()
    {
        int batchCount = 0;

        for (int i = 0; i < agents.Count; i++)
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
        int baseColorID = Shader.PropertyToID("_BaseColor");
        int colorID = Shader.PropertyToID("_Color");
        int emissionID = Shader.PropertyToID("_EmissionColor");

        // CRITICAL FIX: Clear the unused portion of the color array to transparent black.
        // This overwrites any old, bright color data that might be sitting 
        // in the buffer when the list shrinks or shifts.
        Vector4 transparentBlack = Vector4.zero;
        for (int i = count; i < 1023; i++)
        {
            // Set the color to fully transparent and black in the unused slots
            colorBatch[i] = transparentBlack;
        }

        // Now, call the two-parameter function you have access to. 
        // The array is now "safe" because we cleared the garbage data it contains.
        propertyBlock.SetVectorArray(baseColorID, colorBatch);
        propertyBlock.SetVectorArray(colorID, colorBatch);
        propertyBlock.SetVectorArray(emissionID, colorBatch);

        Graphics.DrawMeshInstanced(
            replicatorMesh,
            0,
            replicatorMaterial,
            matrixBatch,
            count, // Tell Unity exactly how many matrices to draw
            propertyBlock,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            true,
            0,
            null,
            UnityEngine.Rendering.LightProbeUsage.Off
        );
    }
}