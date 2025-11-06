using UnityEngine;

public class PlanetGenerator : MonoBehaviour
{
    // Public variables allow you to adjust these values in the Unity Inspector
    [Range(1, 240)] // A slider for easy adjustment in the Inspector
    public int resolution = 10;
    public float radius = 1f;

    public Material planetMaterial;

    private MeshFilter meshFilter;
    private Mesh mesh;

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
            MeshData faceData = face.GenerateMeshData(resolution);

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
        mesh.RecalculateNormals();
    }
}