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
        // 1. Array to store all the vertices (points in 3D space)
        Vector3[] vertices = new Vector3[resolution * resolution * 6];

        // 2. Array to store the triangles (which vertices form a face)
        int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6 * 2 * 3]; // Calculate the required size

        // --- Core Mesh Generation Logic (This is the complex part) ---

        // This is where you would iterate through the 6 faces of the cube,
        // create a grid of (resolution x resolution) vertices on each face,
        // and then generate the triangles for that face.

        // After generating the flat cube vertices, you must "normalize" them:
        // for (int i = 0; i < vertices.Length; i++) {
        //     vertices[i] = vertices[i].normalized * radius;
        // }

        // --- End of Core Logic Placeholder ---

        // 3. Apply the data to the mesh
        mesh.Clear();
        // mesh.vertices = vertices;
        // mesh.triangles = triangles;
        mesh.RecalculateNormals(); // Important for lighting!
    }
}