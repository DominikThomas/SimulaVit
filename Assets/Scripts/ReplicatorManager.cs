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

    [Header("Simulation")]
    public float moveSpeed = 0.5f;
    public float turnSpeed = 2.0f;

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
        // We must initialize every property we plan to use in FlushBatch.
        Vector4[] maxColors = new Vector4[maxPopulation];

        // Fill with default white so they aren't invisible if something goes wrong
        for (int i = 0; i < maxColors.Length; i++) maxColors[i] = Vector4.one;

        int baseColorID = Shader.PropertyToID("_BaseColor");
        int colorID = Shader.PropertyToID("_Color");

        // 1. Allocate for URP
        propertyBlock.SetVectorArray(baseColorID, maxColors);

        // 2. Allocate for Standard/Built-in (This was missing!)
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

        if (agents.Count > 0)
        {
            // Population exists: Pick a random existing agent to be the 'parent'
            Replicator parent = agents[Random.Range(0, agents.Count)];

            // Spawn near the parent's current position direction (currentDirection)
            // The spawnSpread controls how tightly they cluster. Try 0.5f first.
            const float spawnSpread = 0.5f;
            randomDir = parent.currentDirection + Random.insideUnitSphere * spawnSpread;
            randomDir = randomDir.normalized; // Normalize back to the sphere surface
        }
        else
        {
            // Initial population: Spawn randomly across the sphere
            randomDir = Random.onUnitSphere;
        }

        // 1. Calculate height
        float height = GetSurfaceHeight(randomDir);

        // 2. Determine position and rotation
        Vector3 spawnPosition = randomDir * height;

        // Align the rotation
        Quaternion spawnRotation = Quaternion.FromToRotation(Vector3.up, randomDir);
        spawnRotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        // 3. Create the new agent
        agents.Add(new Replicator(spawnPosition, spawnRotation, 30f, Color.white));
    }

    float GetSurfaceHeight(Vector3 direction)
    {
        // Ask the PlanetGenerator for the noise value at this direction
        float noise = planetGenerator.CalculateNoise(direction);

        // Reconstruct the radius math: Base + (Noise * Magnitude)
        // Note: You might need to make these variables public in PlanetGenerator
        float displacement = planetGenerator.radius * (1f + noise * planetGenerator.noiseMagnitude);

        // Add the hover offset
        return displacement + 0.05f;
    }

    void UpdateAgents()
    {
        float dt = Time.deltaTime;

        // Iterate backwards so we can remove dead agents easily
        for (int i = agents.Count - 1; i >= 0; i--)
        {
            Replicator agent = agents[i];

            // 1. Age & Death
            agent.age += dt;
            if (agent.age > agent.maxLifespan)
            {
                agents.RemoveAt(i);
                continue;
            }

            // 2. Replication (Simple chance)
            if (Random.value < 0.001f) // 0.1% chance per frame
            {
                SpawnAgent(); // Should spawn near parent, but random for now
            }

            // 3. Movement Logic (Rotate around the sphere center)
            // Get current "Up"
            Vector3 surfaceNormal = agent.position.normalized;

            // Move "forward" relative to the sphere surface
            // We rotate the position vector around an axis perpendicular to movement
            Vector3 forward = agent.rotation * Vector3.forward;

            // Rotate the agent's position around the planet center
            // This moves them across the surface without needing physics
            Quaternion travelRot = Quaternion.AngleAxis(moveSpeed * dt * 50f / planetGenerator.radius, Vector3.Cross(surfaceNormal, forward));
            Vector3 newPosDirection = travelRot * agent.position;

            // 4. Snap to Surface Height (The math replacement for Raycasts)
            float height = GetSurfaceHeight(newPosDirection.normalized);
            agent.position = newPosDirection.normalized * height;

            // 5. Align Rotation
            Vector3 newNormal = agent.position.normalized;
            Quaternion targetRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, newNormal), newNormal);
            agent.rotation = Quaternion.Slerp(agent.rotation, targetRot, dt * 5f);
        }
    }

    void RenderAgents()
    {
        // Unity can only draw 1023 instances per call. We must batch them.
        matrices.Clear();
        colors.Clear();

        for (int i = 0; i < agents.Count; i++)
        {
            Replicator a = agents[i];

            // Create the matrix for this agent (Position, Rotation, Scale)
            matrices.Add(Matrix4x4.TRS(a.position, a.rotation, Vector3.one * 0.06f)); // Scale adjusted here
            colors.Add(a.color);

            // Draw when the batch is full
            if (matrices.Count == 1023)
            {
                FlushBatch();
            }
        }

        // Draw remaining agents
        if (matrices.Count > 0)
        {
            FlushBatch();
        }
    }

    void FlushBatch()
    {
        int baseColorID = Shader.PropertyToID("_BaseColor");
        int colorID = Shader.PropertyToID("_Color");

        // FIX 1: Use the standard 2-argument overload for SetVectorArray.
        // We pass the List directly. This fixes the "4 arguments" error.
        propertyBlock.SetVectorArray(baseColorID, colors);
        propertyBlock.SetVectorArray(colorID, colors);

        // FIX 2: Provide ALL arguments explicitly to match the List<Matrix4x4> overload.
        // Previous errors occurred because we skipped the 'bool' or 'layer' arguments.
        Graphics.DrawMeshInstanced(
            replicatorMesh,
            0,
            replicatorMaterial,
            matrices,           // This is your List<Matrix4x4>
            propertyBlock,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            true,               // receiveShadows (Required boolean)
            0,                  // layer (Required int)
            null,               // camera
            UnityEngine.Rendering.LightProbeUsage.Off
        );

        matrices.Clear();
        colors.Clear();
    }
}