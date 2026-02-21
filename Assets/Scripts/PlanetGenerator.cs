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
            MeshData faceData = face.GenerateMeshData(resolution); // CubeFace now returns a UNIT sphere

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

        // Add this line to ensure proper bounding box calculation
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        // **CRITICAL**: Assign the newly generated mesh to the MeshCollider
        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = mesh;
        }

    }

    public float CalculateNoise(Vector3 pointOnSphere)
    {
        float noiseValue = 0;
        float frequency = noiseRoughness;
        float amplitude = 1;
        float maxPossibleHeight = 0;

        for (int i = 0; i < numLayers; i++)
        {
            // --- FIX: Use 3D Noise instead of 2D ---
            // We pass the full (x, y, z) vector adjusted by frequency and offset
            Vector3 samplePoint = pointOnSphere * frequency + noiseOffset;
            float singleLayerNoise = SimpleNoise.Evaluate(samplePoint);

            // Normalize from (-1 to 1) to (0 to 1) if strictly needed, 
            // though SimpleNoise often returns approx -1 to 1 range.
            singleLayerNoise = (singleLayerNoise + 1) * 0.5f;

            noiseValue += singleLayerNoise * amplitude;
            maxPossibleHeight += amplitude;

            amplitude *= persistence;
            frequency *= 2;
        }

        return maxPossibleHeight > 0f ? noiseValue / maxPossibleHeight : 0f;
    }
}
