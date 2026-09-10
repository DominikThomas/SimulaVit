using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class IcosphereRenderMeshBuilder
{
    public static IcosphereRenderGeometry BuildUnitGeometry(int subdivision)
    {
        subdivision = Mathf.Clamp(subdivision, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        List<Vector3> vertices = new List<Vector3>(GeodesicGridTopology.ExpectedCellCount(subdivision));
        List<int> triangles = new List<int>(GeodesicGridTopology.ExpectedTriangleCount(subdivision) * 3);
        BuildIcosahedron(vertices, triangles);
        for (int level = 0; level < subdivision; level++) Subdivide(vertices, triangles);
        return new IcosphereRenderGeometry(subdivision, vertices.ToArray(), triangles.ToArray());
    }

    public static Mesh BuildSurfaceMesh(IcosphereRenderGeometry geometry, float radius, string name = "Geodesic Icosphere")
    {
        Mesh mesh = new Mesh { name = name };
        if (geometry.VertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
        Vector3[] vertices = new Vector3[geometry.VertexCount];
        Vector3[] normals = new Vector3[geometry.VertexCount];
        Vector2[] uvs = new Vector2[geometry.VertexCount];
        for (int i = 0; i < geometry.VertexCount; i++)
        {
            Vector3 d = geometry.UnitVertices[i];
            vertices[i] = d * radius;
            normals[i] = d;
            uvs[i] = new Vector2(Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI) + 0.5f, Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI + 0.5f);
        }
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = (int[])geometry.Triangles.Clone();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void BuildIcosahedron(List<Vector3> v, List<int> tri)
    {
        float phi = (1f + Mathf.Sqrt(5f)) * 0.5f;
        Vector3[] p = { new(-1, phi, 0), new(1, phi, 0), new(-1, -phi, 0), new(1, -phi, 0), new(0, -1, phi), new(0, 1, phi), new(0, -1, -phi), new(0, 1, -phi), new(phi, 0, -1), new(phi, 0, 1), new(-phi, 0, -1), new(-phi, 0, 1) };
        foreach (var x in p) v.Add(x.normalized);
        int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        tri.AddRange(f);
    }

    private static void Subdivide(List<Vector3> v, List<int> tri)
    {
        Dictionary<ulong, int> midpointCache = new Dictionary<ulong, int>();
        List<int> next = new List<int>(tri.Count * 4);
        for (int i = 0; i < tri.Count; i += 3)
        {
            int a = tri[i], b = tri[i + 1], c = tri[i + 2];
            int ab = Mid(v, midpointCache, a, b), bc = Mid(v, midpointCache, b, c), ca = Mid(v, midpointCache, c, a);
            next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        tri.Clear();
        tri.AddRange(next);
    }

    private static int Mid(List<Vector3> v, Dictionary<ulong, int> c, int a, int b)
    {
        int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
        ulong key = ((ulong)(uint)lo << 32) | (uint)hi;
        if (c.TryGetValue(key, out int idx)) return idx;
        idx = v.Count;
        v.Add(((v[a] + v[b]) * 0.5f).normalized);
        c[key] = idx;
        return idx;
    }
}
