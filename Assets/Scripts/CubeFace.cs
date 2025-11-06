using UnityEngine;
using static UnityEngine.Mesh;

public class CubeFace
{
    Vector3 localUp;
    Vector3 axisA;
    Vector3 axisB;

    public CubeFace(Vector3 localUp)
    {
        this.localUp = localUp;
        // ... (axisA and axisB assignment remains the same)
        axisA = new Vector3(localUp.y, localUp.z, localUp.x);
        axisB = Vector3.Cross(localUp, axisA);
    }

    // New method that returns both vertices and triangle indices
    public MeshData GenerateMeshData(int resolution)
    {
        // 1. Initialize lists to store data
        Vector3[] vertices = new Vector3[resolution * resolution];
        int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];
        int vertexIndex = 0;
        int triangleIndex = 0;

        // Loop through the grid
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // --- VERTEX GENERATION (Same as before) ---
                Vector2 percent = new Vector2(x, y) / (resolution - 1f);
                Vector3 pointOnCube = localUp +
                                      (percent.x * 2 - 1) * axisA +
                                      (percent.y * 2 - 1) * axisB;

                // Set the vertex and normalize it to the sphere surface
                vertices[vertexIndex] = pointOnCube.normalized;

                // --- TRIANGLE GENERATION ---
                // We only generate triangles for the bottom and left edges of the grid, 
                // up until the last row/column.
                if (x != resolution - 1 && y != resolution - 1)
                {
                    // The 4 indices that form the current square on the grid
                    int i = vertexIndex;
                    int v0 = i;
                    int v1 = i + resolution; // The vertex directly "up"
                    int v2 = i + resolution + 1; // The vertex up and to the "right"
                    int v3 = i + 1; // The vertex directly "right"

                    // First triangle (bottom-left)
                    triangles[triangleIndex++] = v0;
                    triangles[triangleIndex++] = v1;
                    triangles[triangleIndex++] = v3; // Note: Correct winding order is crucial!

                    // Second triangle (top-right)
                    triangles[triangleIndex++] = v3;
                    triangles[triangleIndex++] = v1;
                    triangles[triangleIndex++] = v2;
                }

                vertexIndex++;
            }
        }

        // Return a MeshData object (we'll define this next)
        return new MeshData(vertices, triangles);
    }
}