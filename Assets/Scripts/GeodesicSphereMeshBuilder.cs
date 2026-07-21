using UnityEngine;
using UnityEngine.Rendering;

public static class GeodesicSphereMeshBuilder
{
    public static Mesh BuildSurfaceMesh(GeodesicGridTopology topology, float radius, string name = "Geodesic Icosphere")
    {
        Mesh mesh = new Mesh { name = name };
        if (topology.CellCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
        Vector3[] vertices = new Vector3[topology.CellCount];
        Vector3[] normals = new Vector3[topology.CellCount];
        Vector2[] uvs = new Vector2[topology.CellCount];
        for (int i = 0; i < topology.CellCount; i++)
        {
            Vector3 d = topology.CellDirections[i]; vertices[i] = d * radius; normals[i] = d;
            uvs[i] = new Vector2(Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI) + 0.5f, Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI + 0.5f);
        }
        mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uvs; mesh.triangles = topology.Triangles; mesh.RecalculateBounds();
        return mesh;
    }
}
