using UnityEngine;

public class PlanetGenerator : MonoBehaviour
{
    // Public variables allow you to adjust these values in the Unity Inspector
    [Range(1, 240)] // A slider for easy adjustment in the Inspector
    public int resolution = 10;
    public float radius = 1f;

    private MeshFilter meshFilter;
    private Mesh mesh;

    void Awake()
    {
        // 1. Get or Add the MeshFilter component
        // This component holds the mesh data
        meshFilter = gameObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        // 2. Get or Add the MeshRenderer component
        // This component makes the mesh visible
        if (gameObject.GetComponent<MeshRenderer>() == null)
        {
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard"));
        }

        // 3. Create and assign a new Mesh object
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