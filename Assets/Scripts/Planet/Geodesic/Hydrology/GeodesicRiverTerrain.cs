using System;
using UnityEngine;

/// <summary>Queries the completed rendered icosphere, without physics or a scan of all its triangles.
/// Height is interpolated large-scale altitude; Radius is exact visible triangle intersection.
/// Optional large-scale radii come from the same terrain sample with only fine-detail noise omitted.</summary>
public sealed class GeodesicRiverTerrain
{
    private readonly IcosphereRenderGeometry[] levels;
    private readonly float[] largeScaleRadii;
    private readonly float[] visibleRadii;
    private readonly Vector3[][] faceNormals;
    private readonly Vector3[] surfacePlanes;

    public GeodesicRiverTerrain(IcosphereRenderGeometry geometry, Vector3[] renderedVertices, float[] hydrologicalRadii = null)
    {
        if (renderedVertices == null || renderedVertices.Length != geometry.VertexCount)
            throw new ArgumentException("Rendered vertices must match the render geometry.");
        Vector3[] vertices = renderedVertices;
        if (hydrologicalRadii != null && hydrologicalRadii.Length != vertices.Length)
            throw new ArgumentException("Hydrological radii must match the render geometry.");
        largeScaleRadii = hydrologicalRadii != null ? (float[])hydrologicalRadii.Clone() : null;
        visibleRadii = new float[vertices.Length];
        for (int i = 0; i < vertices.Length; i++) visibleRadii[i] = vertices[i].magnitude;
        levels = new IcosphereRenderGeometry[geometry.SubdivisionLevel + 1];
        faceNormals = new Vector3[levels.Length][];
        for (int level = 0; level < levels.Length; level++)
        {
            var g = IcosphereRenderGeometryCache.GetOrBuild(level); levels[level] = g;
            var normals = new Vector3[g.Triangles.Length]; faceNormals[level] = normals;
            for (int t = 0; t < g.Triangles.Length; t += 3)
            {
                Vector3 a = g.UnitVertices[g.Triangles[t]], b = g.UnitVertices[g.Triangles[t + 1]], c = g.UnitVertices[g.Triangles[t + 2]];
                normals[t] = InwardNormal(a, b, c); normals[t + 1] = InwardNormal(b, c, a); normals[t + 2] = InwardNormal(c, a, b);
            }
        }
        surfacePlanes = new Vector3[geometry.TriangleCount];
        for (int t = 0; t < geometry.Triangles.Length; t += 3)
        {
            Vector3 a = vertices[geometry.Triangles[t]], b = vertices[geometry.Triangles[t + 1]], c = vertices[geometry.Triangles[t + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            surfacePlanes[t / 3] = normal / Vector3.Dot(normal, a);
        }
    }

    public float Height(Vector3 direction) => Sample(direction, false, true);
    public float VisibleHeight(Vector3 direction) => Sample(direction, false, false);
    public float Radius(Vector3 direction) => Sample(direction, true, false);

    public int FindTriangle(Vector3 direction)
    {
        Vector3 d = direction.normalized;
        int face = 0, first = 0, count = 20;
        for (int level = 0; level < levels.Length; level++)
        {
            Vector3[] normals = faceNormals[level];
            float best = float.NegativeInfinity;
            for (int f = first; f < first + count; f++)
            {
                int offset = f * 3;
                float score = Mathf.Min(Vector3.Dot(normals[offset], d),
                    Mathf.Min(Vector3.Dot(normals[offset + 1], d), Vector3.Dot(normals[offset + 2], d)));
                if (score > best) { best = score; face = f; }
            }
            first = face * 4; count = 4; // Subdivide appends four children for each parent in this order.
        }
        return face;
    }

    private float Sample(Vector3 direction, bool exactRadius, bool largeScale)
    {
        Vector3 d = direction.normalized;
        int face = FindTriangle(d);
        var final = levels[levels.Length - 1];
        int ia = final.Triangles[face * 3], ib = final.Triangles[face * 3 + 1], ic = final.Triangles[face * 3 + 2];
        if (exactRadius) return 1f / Vector3.Dot(surfacePlanes[face], d);
        Vector3 da = final.UnitVertices[ia], db = final.UnitVertices[ib], dc = final.UnitVertices[ic];
        float wa = Vector3.Dot(d, Vector3.Cross(db, dc));
        float wb = Vector3.Dot(d, Vector3.Cross(dc, da));
        float wc = Vector3.Dot(d, Vector3.Cross(da, db));
        return largeScale && largeScaleRadii != null
            ? (wa * largeScaleRadii[ia] + wb * largeScaleRadii[ib] + wc * largeScaleRadii[ic]) / (wa + wb + wc)
            : (wa * visibleRadii[ia] + wb * visibleRadii[ib] + wc * visibleRadii[ic]) / (wa + wb + wc);
    }

    private static Vector3 InwardNormal(Vector3 a, Vector3 b, Vector3 opposite)
    {
        Vector3 normal = Vector3.Cross(a, b).normalized;
        return normal * (Vector3.Dot(normal, opposite) >= 0f ? 1f : -1f);
    }
}
