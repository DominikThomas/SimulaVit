using UnityEngine;

public class PlanetGenerator : MonoBehaviour
{
    // Public variables allow you to adjust these values in the Unity Inspector
    [Range(3, 240)] // A slider for easy adjustment in the Inspector
    public int resolution = 10;
    public float radius = 1f;

    public Material planetMaterial;

    private MeshFilter meshFilter;
    private Mesh mesh;

    public GameObject replicatorPrefab;

    public int replicatorCount = 0;

    private Vector3[] allPlanetVertices;

    [Header("Scheduled Spawning")]
    public float spawnCheckInterval = 1.0f; // Check for new initial spawns every X seconds
    private float spawnTimer = 0f;

    // Base probability for initial *external* spawning (per vertex, per second)
    // Use a very small number for scarce spawning.
    public float baseVertexSpawnProbabilityPerSecond = 0.0000001f;

    [Header("Replicator Density Control")]
    public float replicatorDensityMultiplier = 0.5f; // How many replicators per vertex (e.g., 0.5 means 1 rep for every 2 vertices)
    public int maxReplicatorCount = 0; // The calculated population limit

    [Header("Terrain Generation")]
    public float noiseMagnitude = 0.1f; // How tall the mountains are (e.g., 10% of radius)
    public float noiseRoughness = 1.0f; // Controls the frequency/scale of the features
    public int numLayers = 4; // How many layers of noise to combine (for complex terrain)
    public float persistence = 0.5f; // How much each subsequent layer contributes
    public Vector3 noiseOffset = Vector3.one; // Used to change the terrain pattern

    void Awake()
    {
        // 1. Get or Add the MeshFilter component (remains the same)
        meshFilter = gameObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        // 2. Get or Add the MeshRenderer component
        MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        // **NEW**: 3. Get or Add the MeshCollider component
        MeshCollider meshCollider = gameObject.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }

        // **CRUCIAL CHANGE:** Assign the saved asset material here
        if (planetMaterial != null)
        {
            meshRenderer.sharedMaterial = planetMaterial;
        }
        // NOTE: You can remove the old line that created a temporary material
        // if you want to rely entirely on this reference.

        // 4. Create and assign a new Mesh object (remains the same)
        mesh = new Mesh();
        meshFilter.sharedMesh = mesh;

        // **NEW**: 5. Assign the mesh to the collider (done automatically when sharedMesh is set)
        // You'll explicitly assign the collider mesh in GeneratePlanet() for safety.
    }

    void Start()
    {
        GeneratePlanet();
    }

    void Update()
    {
        // The Update loop now only handles the timer for the spawn cycle.
        spawnTimer -= Time.deltaTime;

        if (spawnTimer <= 0f)
        {
            RunScheduledSpawnCycle();
            spawnTimer = spawnCheckInterval; // Reset timer
        }
    }
    void RunScheduledSpawnCycle()
    {
        // Check 1: Ensure we have vertices and are below max capacity
        if (allPlanetVertices == null || replicatorCount >= maxReplicatorCount)
        {
            return;
        }

        // 1. Calculate the total chance (expected number of spawns) across the planet
        // This value can now be greater than 1.0
        float totalChance = allPlanetVertices.Length * baseVertexSpawnProbabilityPerSecond * spawnCheckInterval;

        // 2. Determine the number of spawns to attempt this cycle:

        // A. Guaranteed Spawns (the integer part of the chance)
        int guaranteedSpawns = Mathf.FloorToInt(totalChance);

        // B. Fractional Chance (the remainder, e.g., if totalChance is 3.7, this is 0.7)
        float fractionalChance = totalChance - guaranteedSpawns;

        // Start with the guaranteed number
        int spawnsToAttempt = guaranteedSpawns;

        // C. Probabilistic Spawn: Use the fractional chance for one additional spawn
        if (Random.value < fractionalChance)
        {
            spawnsToAttempt++;
        }

        // 3. Clamp to ensure we don't exceed the capacity
        int maxAllowedSpawns = maxReplicatorCount - replicatorCount;
        int finalSpawnsToAttempt = Mathf.Min(spawnsToAttempt, maxAllowedSpawns);

        // 4. Spawn at the targeted spots
        if (finalSpawnsToAttempt > 0)
        {
            SpawnAtBestWeightedSpots(finalSpawnsToAttempt);
        }
    }

    void SpawnAtBestWeightedSpots(int spawnsToAttempt)
    {
        for (int i = 0; i < spawnsToAttempt; i++)
        {
            // Get the actual DEFORMED vertex position.
            int randomIndex = Random.Range(0, allPlanetVertices.Length);

            // CRITICAL CHANGE: Use the full position vector, NOT the normalized direction vector.
            Vector3 actualSpawnPosition = allPlanetVertices[randomIndex];

            SpawnReplicatorAtSpot(actualSpawnPosition);
        }
    }

    void SpawnReplicatorAtSpot(Vector3 spawnPosition) // Changed parameter name for clarity
    {
        if (replicatorPrefab == null) { return; }

        Vector3 finalPosition;
        Vector3 spawnDirection = spawnPosition.normalized; // The vector for rotation and offset

        // --- Special case: FIRST replicator spawn must be camera-biased ---
        if (replicatorCount == 0)
        {
            // Keep the old logic for the first spawn based on camera position
            spawnDirection = GetBiasedRandomDirection(Vector3.back, 0.7f);
            float surfaceOffset = 0.05f;
            finalPosition = spawnDirection * (radius + surfaceOffset);
        }
        else
        {
            // General Case: Use the actual deformed position.
            // Reset buffer to a stable, small value.
            float initialOffset = 0.01f;
            finalPosition = spawnPosition + spawnDirection * initialOffset;
        }

        // 4. Instantiate and Parent
        GameObject newReplicatorObject = Instantiate(replicatorPrefab, finalPosition, Quaternion.identity, this.transform);

        ReplicatorAgent newAgent = newReplicatorObject.GetComponent<ReplicatorAgent>();
        if (newAgent != null)
        {
            newAgent.replicatorPrefab = replicatorPrefab;
        }
        replicatorCount++;
    }

    void GeneratePlanet()
    {
        Vector3[] faceDirections = new Vector3[] {
        Vector3.up,
        Vector3.down,
        Vector3.left,
        Vector3.right,
        Vector3.forward,
        Vector3.back
    };

        // Use Lists as they are easier to resize when combining data
        System.Collections.Generic.List<Vector3> allVertices = new System.Collections.Generic.List<Vector3>();
        System.Collections.Generic.List<int> allTriangles = new System.Collections.Generic.List<int>();

        // This tracks the base index for the vertices of the NEXT face.
        int currentVertexOffset = 0;

        // --- Generate and Assemble ---
        foreach (Vector3 dir in faceDirections)
        {
            CubeFace face = new CubeFace(dir);
            MeshData faceData = face.GenerateMeshData(resolution, radius); // CubeFace now returns a UNIT sphere

            // CRITICAL CHANGE: DEFORM THE VERTICES HERE
            for (int i = 0; i < faceData.vertices.Length; i++)
            {
                Vector3 pointOnUnitSphere = faceData.vertices[i];

                // 1. Calculate the final noise value for this point
                float height = CalculateNoise(pointOnUnitSphere);

                // 2. Determine the final distance from center
                // Base Radius + (Noise value (0 to 1) * Magnitude)
                float displacement = radius * (1f + height * noiseMagnitude);

                // 3. Deform the vertex
                faceData.vertices[i] = pointOnUnitSphere * displacement;

                // 4. Add the now-deformed vertex to the final list
                allVertices.Add(faceData.vertices[i]);
            }

            // 2. Add Triangles: Crucially, we must offset the indices!
            // ... (rest of the triangle appending logic remains the same) ...
            for (int i = 0; i < faceData.triangles.Length; i++)
            {
                allTriangles.Add(faceData.triangles[i] + currentVertexOffset);
            }

            // 3. Update Offset: Move the offset marker to the end of the combined list
            currentVertexOffset += faceData.vertices.Length;
        }

        // 4. Apply the data to the mesh
        mesh.Clear();
        mesh.vertices = allVertices.ToArray();
        mesh.triangles = allTriangles.ToArray();

        // Cache the vertices for the spawning system
        allPlanetVertices = allVertices.ToArray();

        // Add this line to ensure proper bounding box calculation
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        // **CRITICAL**: Assign the newly generated mesh to the MeshCollider
        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = mesh;
        }

        maxReplicatorCount = Mathf.FloorToInt(Mathf.Pow(radius, 3) * replicatorDensityMultiplier);
        Debug.Log($"Planet capacity calculated: Max Replicators = {maxReplicatorCount}");

    }

    Vector3 GetBiasedRandomDirection(Vector3 biasDirection, float blendFactor)
    {
        // Generate a completely random vector on the sphere
        Vector3 randomDirection = Random.onUnitSphere;

        // Blend the random direction with the strong bias direction (Vector3.back).
        // The blendFactor (e.g., 0.8) ensures 80% of the vector points 'back'.
        Vector3 biasedDirection = Vector3.Lerp(randomDirection, biasDirection, blendFactor).normalized;

        return biasedDirection;
    }

    public float CalculateNoise(Vector3 pointOnSphere)
    {
        float noiseValue = 0;
        float frequency = noiseRoughness;
        float amplitude = 1;
        float maxPossibleHeight = 0;

        for (int i = 0; i < numLayers; i++)
        {
            // Replace 'Noise.Evaluate' with your actual 3D noise function (e.g., Simplex or Perlin3D)
            // float singleLayerNoise = Noise.Evaluate(pointOnSphere * frequency + noiseOffset); 

            // --- TEMPORARY Placeholder for Noise.Evaluate ---
            // For testing, if you don't have a 3D noise class, you can temporarily use this:
            float singleLayerNoise = Mathf.PerlinNoise(pointOnSphere.x * frequency + noiseOffset.x, pointOnSphere.y * frequency + noiseOffset.y);
            // This 2D version will cause seams, but confirms the logic works.
            // ---------------------------------------------------

            // Normalize the noise from (-1 to 1) to (0 to 1)
            singleLayerNoise = (singleLayerNoise + 1) * 0.5f;

            noiseValue += singleLayerNoise * amplitude;
            maxPossibleHeight += amplitude;

            amplitude *= persistence;
            frequency *= 2; // Increase frequency (making features smaller)
        }

        // Normalize the final noise value
        return noiseValue / maxPossibleHeight;
    }
}