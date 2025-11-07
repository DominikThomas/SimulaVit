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

    private int replicatorCount = 0;

    private Vector3[] allPlanetVertices;

    public float baseSpawnProbabilityPerVertex = 0.000005f; // Must be VERY small now! (e.g., 0.000005f)
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

        // **CRUCIAL CHANGE:** Assign the saved asset material here
        if (planetMaterial != null)
        {
            meshRenderer.sharedMaterial = planetMaterial;
        }
        // NOTE: You can remove the old line that created a temporary material
        // if you want to rely entirely on this reference.

        // 3. Create and assign a new Mesh object (remains the same)
        mesh = new Mesh();
        meshFilter.sharedMesh = mesh;
    }

    void Start()
    {
        GeneratePlanet();
    }

    void Update()
    {
        // Check only if the mesh has been generated and vertices are available
        if (allPlanetVertices == null || allPlanetVertices.Length == 0)
        {
            return;
        }

        // Iterate through every vertex on the planet's surface
        for (int i = 0; i < allPlanetVertices.Length; i++)
        {
            Vector3 vertexPosition = allPlanetVertices[i];

            // Use the vertex position's direction (normalized) for the spawn spot
            Vector3 spotDirection = vertexPosition.normalized;

            // FUTURE: float localProbability = CalculateLocalProbability(spotDirection);
            float localProbability = baseSpawnProbabilityPerVertex;

            // Check if a spawn should occur at THIS vertex this frame.
            if (Random.value < localProbability)
            {
                // If the check passes, spawn a replicator near this specific spot.
                SpawnReplicatorAtSpot(spotDirection);
                // NOTE: The loop continues, allowing simultaneous spawning at other vertices.

                // OPTIONAL: Break out of the loop after the first spawn to limit 
                // the max number of replicators spawned per frame (for performance).
                // break; 
            }
        }
    }

    void SpawnReplicatorAtSpot(Vector3 spotDirection)
    {
        if (replicatorPrefab == null) { return; }

        Vector3 spawnDirection;

        // --- Special case: FIRST replicator spawn must be camera-biased ---
        if (replicatorCount == 0)
        {
            spawnDirection = GetBiasedRandomDirection(Vector3.back, 0.7f);
        }
        else
        {
            // General Case: Spawn precisely at the given vertex location (no scatter needed)
            // If you want a small jitter around the vertex, you can re-introduce the scatter.
            spawnDirection = spotDirection;
        }

        // --- (Rest of the instantiation and parenting logic remains the same) ---
        float surfaceOffset = 0.05f;
        Vector3 spawnPoint = spawnDirection * (radius + surfaceOffset);

        GameObject newReplicator = Instantiate(replicatorPrefab, spawnPoint, Quaternion.identity, this.transform);
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
            MeshData faceData = face.GenerateMeshData(resolution, radius);

            // 1. Add Vertices: Simply append all vertices from the face
            allVertices.AddRange(faceData.vertices);

            // 2. Add Triangles: Crucially, we must offset the indices!
            // The face's indices (v0, v1, v2, v3) are relative to its own small array.
            // We must add the total number of vertices already in the full mesh.
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
        mesh.triangles = allTriangles.ToArray(); // Now we assign the combined triangles!
     
        // Cache the vertices for the spawning system
        allPlanetVertices = allVertices.ToArray();
        mesh.RecalculateNormals();
    }

    void SpawnReplicator()
    {
        if (replicatorPrefab == null) { return; }

        // --- 1. Determine the Spawn Direction ---
        Vector3 spawnDirection;

        if (replicatorCount == 0)
        {
            // We use the empirically confirmed visible direction (Vector3.back) as the bias.
            // blendFactor of 0.7 means 70% chance of facing 'back', 30% random spread.
            spawnDirection = GetBiasedRandomDirection(Vector3.back, 0.7f);
            // 2. FUTURE STEP: GET TERRAIN PROPERTIES AT THIS SPOT
            // float elevation = GetElevationAtPoint(spawnDirection); 
            // float localProbability = baseProbability * (1.0f - elevation); // Example: Less likely on high mountains
        }
        else
        {
            // This is a subsequent replicator—spawn completely randomly
            spawnDirection = Random.onUnitSphere;
            // 2. FUTURE STEP: GET TERRAIN PROPERTIES AT THIS SPOT
            // float elevation = GetElevationAtPoint(spawnDirection); 
            // float localProbability = baseProbability * (1.0f - elevation); // Example: Less likely on high mountains
        }

        // --- 2. Calculate Position and Instantiate ---
        float surfaceOffset = 0.05f;
        Vector3 spawnPoint = spawnDirection * (radius + surfaceOffset);

        // --- CRUCIAL CHANGE: Use the Instantiate overload that includes the parent transform ---
        // The arguments are: (Prefab, Position, Rotation, Parent Transform)
        GameObject newReplicator = Instantiate(replicatorPrefab, spawnPoint, Quaternion.identity, this.transform);

        // The old SetParent call is now redundant and should be REMOVED or commented out.
        // REMOVED: newReplicator.transform.SetParent(this.transform); 

        // --- 3. Increment the Counter ---
        replicatorCount++;
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
}