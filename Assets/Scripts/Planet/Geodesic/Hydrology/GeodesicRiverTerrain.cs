using System;
using System.Collections.Generic;
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
    public bool UsesSharedHydrologicalRadii { get; }

    public GeodesicRiverTerrain(IcosphereRenderGeometry geometry, Vector3[] renderedVertices,
        float[] hydrologicalRadii = null, bool shareHydrologicalRadii = false)
    {
        if (renderedVertices == null || renderedVertices.Length != geometry.VertexCount)
            throw new ArgumentException("Rendered vertices must match the render geometry.");
        Vector3[] vertices = renderedVertices;
        if (hydrologicalRadii != null && hydrologicalRadii.Length != vertices.Length)
            throw new ArgumentException("Hydrological radii must match the render geometry.");
        largeScaleRadii = hydrologicalRadii == null ? null : shareHydrologicalRadii ? hydrologicalRadii : (float[])hydrologicalRadii.Clone();
        UsesSharedHydrologicalRadii = hydrologicalRadii != null && shareHydrologicalRadii;
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

    /// <summary>Check every crossed terrain triangle, including narrow ridges between
    /// uniform samples. Barycentric altitude is monotone along a straight spherical arc
    /// inside one triangle, so its extrema are at triangle entry/exit points.</summary>
    public float MaximumVisibleUphillExcursion(Vector3[] path)
    {
        if (path == null || path.Length < 2) return float.PositiveInfinity;
        float lowest = VisibleHeight(path[0]), rise = 0f;
        var crossings = new List<double>(16);
        for (int segment = 1; segment < path.Length; segment++)
        {
            Vector3 start = path[segment - 1], end = path[segment];
            crossings.Clear(); crossings.Add(0d); crossings.Add(1d);
            if (FindTriangle(start) != FindTriangle(end))
                for (int face = 0; face < 20; face++) CrossedFaces(0, face, 0d, 1d);
            crossings.Sort();
            foreach (double t in crossings)
            {
                float height = VisibleHeight(Vector3.Lerp(start, end, (float)t).normalized);
                if (!float.IsFinite(height)) return float.PositiveInfinity;
                rise = Mathf.Max(rise, height - lowest); lowest = Mathf.Min(lowest, height);
            }

            void CrossedFaces(int level, int face, double enter, double exit)
            {
                var g = levels[level];
                Vector3 a = g.UnitVertices[g.Triangles[face * 3]], b = g.UnitVertices[g.Triangles[face * 3 + 1]], c = g.UnitVertices[g.Triangles[face * 3 + 2]];
                double sign = Triple(c, a, b) >= 0d ? 1d : -1d;
                if (!Clip(a, b) || !Clip(b, c) || !Clip(c, a)) return;
                if (level == levels.Length - 1) { crossings.Add(enter); crossings.Add(exit); return; }
                for (int child = 0; child < 4; child++) CrossedFaces(level + 1, face * 4 + child, enter, exit);

                bool Clip(Vector3 v1, Vector3 v2)
                {
                    double first = sign * Triple(start, v1, v2), last = sign * Triple(end, v1, v2);
                    if (first < 0d && last < 0d) return false;
                    if (first >= 0d && last >= 0d) return true;
                    double intersection = first / (first - last);
                    if (first < 0d) enter = Math.Max(enter, intersection);
                    else exit = Math.Min(exit, intersection);
                    return exit >= enter;
                }
            }
        }
        return rise;
    }

    private static double Triple(Vector3 d, Vector3 a, Vector3 b) =>
        d.x * ((double)a.y * b.z - (double)a.z * b.y) +
        d.y * ((double)a.z * b.x - (double)a.x * b.z) +
        d.z * ((double)a.x * b.y - (double)a.y * b.x);

    /// <summary>Diagnostic double-precision reference. Production sampling retains the
    /// original 6f48085 float arithmetic so this restoration does not alter grade decisions.</summary>
    public double InterpolationReference(Vector3 direction, bool visible)
    {
        Vector3 d = direction.normalized; int face = FindTriangle(d);
        var geometry = levels[levels.Length - 1];
        int a = geometry.Triangles[face * 3], b = geometry.Triangles[face * 3 + 1], c = geometry.Triangles[face * 3 + 2];
        double Triple(Vector3 x, Vector3 y) => d.x * ((double)x.y*y.z - (double)x.z*y.y) +
            d.y * ((double)x.z*y.x - (double)x.x*y.z) + d.z * ((double)x.x*y.y - (double)x.y*y.x);
        double wa = Triple(geometry.UnitVertices[b], geometry.UnitVertices[c]);
        double wb = Triple(geometry.UnitVertices[c], geometry.UnitVertices[a]);
        double wc = Triple(geometry.UnitVertices[a], geometry.UnitVertices[b]);
        float[] radii = visible || largeScaleRadii == null ? visibleRadii : largeScaleRadii;
        return radii[a] + (wb * (radii[b] - (double)radii[a]) + wc * (radii[c] - (double)radii[a])) / (wa + wb + wc);
    }
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
