using UnityEngine;

public struct MeshData
{
    public Vector3[] vertices;
    public int[] triangles;

    public MeshData(Vector3[] vertices, int[] triangles)
    {
        this.vertices = vertices;
        this.triangles = triangles;
    }
}