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

    // Base color is standard, script handles the brightness
    public Color baseAgentColor = Color.cyan;

    [Header("Simulation")]
    public float moveSpeed = 0.5f;
    public float turnSpeed = 2.0f;
    public float spawnSpread = 0.5f;

    private List<Replicator> agents = new List<Replicator>();
    private List<Matrix4x4> matrices = new List<Matrix4x4>();
    private List<Vector4> colors = new List<Vector4>();
    private MaterialPropertyBlock propertyBlock;

    void Start()
    {
        propertyBlock = new MaterialPropertyBlock();

        // Initialize buffer to max size to prevent warnings
        Vector4[] maxColors = new Vector4[maxPopulation];
        for (int i = 0; i < maxColors.Length; i++) maxColors[i] = Vector4.one;

        // We need to pre-allocate for Emission as well!
        int baseColorID = Shader.PropertyToID("_BaseColor");
        int colorID = Shader.PropertyToID("_Color");
        int emissionID = Shader.PropertyToID("_EmissionColor");

        propertyBlock.SetVectorArray(baseColorID, maxColors);
        propertyBlock.SetVectorArray(colorID, maxColors);
        propertyBlock.SetVectorArray(emissionID, maxColors);

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

        // Spawn with the base color
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

            // 1. AGE LOGIC
            agent.age += dt;
            float lifeRemaining = agent.maxLifespan - agent.age;

            // --- VISUALS: CALCULATE FLARE & FADE ---
            agent.color = CalculateAgentColor(agent.age, lifeRemaining);

            // Death Check
            if (agent.age > agent.maxLifespan)
            {
                agents.RemoveAt(i);
                continue;
            }

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

    // --- NEW HELPER FOR FLARE & FLICKER ---
    Color CalculateAgentColor(float age, float lifeRemaining)
    {
        float intensity = 1.0f; // Default brightness

        // 1. FLARE UP (Birth)
        // For the first 1.5 seconds, intensity bursts from 10 down to 1
        if (age < 1.5f)
        {
            float t = age / 1.5f;
            intensity = Mathf.Lerp(8.0f, 1.0f, t); // Start super bright (8x)
        }
        // 2. FLICKER & DIM (Death)
        // In the last 3 seconds, fade out and flicker
        else if (lifeRemaining < 3.0f)
        {
            float t = lifeRemaining / 3.0f; // 0 is dead, 1 is start of death

            // Dim down linearly
            intensity = Mathf.Lerp(0f, 1.0f, t);
        }

        // Create the final color.
        // We multiply the base color by the intensity. 
        // Values > 1.0 create HDR colors which trigger the Glow/Bloom.
        Color finalColor = baseAgentColor * intensity;

        // Keep alpha for transparency (optional, depending on shader)
        finalColor.a = (lifeRemaining < 3.0f) ? (lifeRemaining / 3.0f) : 1.0f;

        return finalColor;
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
        // NEW: We must override the Emission Color too!
        int emissionID = Shader.PropertyToID("_EmissionColor");

        Vector4[] colorArray = colors.ToArray();

        // Set Base Color (Albedo)
        propertyBlock.SetVectorArray(baseColorID, colorArray);
        propertyBlock.SetVectorArray(colorID, colorArray);

        // Set Emission Color (The Glow)
        // Since our 'colorArray' contains HDR values (brightness > 1), 
        // passing it to Emission will create the exact Flare/Dim effect we want.
        propertyBlock.SetVectorArray(emissionID, colorArray);

        Graphics.DrawMeshInstanced(
            replicatorMesh,
            0,
            replicatorMaterial,
            matrices,
            propertyBlock,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            true,
            0,
            null,
            UnityEngine.Rendering.LightProbeUsage.Off
        );

        matrices.Clear();
        colors.Clear();
    }
}