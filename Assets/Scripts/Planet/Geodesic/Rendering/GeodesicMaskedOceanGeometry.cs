using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Water patches bounded by the nearest-cell spherical Voronoi regions.
/// No triangle crosses into an excluded cell. Generated once, with a matching resource mapping.</summary>
public static class GeodesicMaskedOceanGeometry
{
    public static IcosphereRenderGeometry Build(GeodesicGridTopology topology, bool[] oceanMask,
        int renderSubdivision, out IcosphereDirectionMapping mapping)
    {
        if (topology == null || oceanMask == null || oceanMask.Length != topology.CellCount)
            throw new ArgumentException("Ocean mask must match topology.");
        var vertices = new List<Vector3>();
        var owners = new List<int>();
        var triangles = new List<int>();
        var corners = new Dictionary<ulong, int>();
        int AddVertex(Vector3 direction, int owner) { int index = vertices.Count; vertices.Add(direction); owners.Add(owner); return index; }
        int Corner(int a, int b, int c, int owner)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            ulong key = ((ulong)a << 40) | ((ulong)b << 20) | (uint)c;
            if (corners.TryGetValue(key, out int index)) return index;
            Vector3 center = topology.CellDirections[a];
            Vector3 direction = Vector3.Cross(topology.CellDirections[b] - center, topology.CellDirections[c] - center).normalized;
            if (Vector3.Dot(direction, center) < 0f) direction = -direction;
            index = AddVertex(direction, owner); corners.Add(key, index); return index;
        }
        for (int cell = 0; cell < topology.CellCount; cell++)
        {
            if (!oceanMask[cell]) continue;
            int center = AddVertex(topology.CellDirections[cell], cell);
            int count = topology.NeighborCounts[cell];
            var ring = new int[count];
            for (int n = 0; n < count; n++)
                ring[n] = Corner(cell, topology.Neighbors6[cell * 6 + n], topology.Neighbors6[cell * 6 + (n + 1) % count], cell);
            for (int n = 0; n < count; n++)
            {
                int a = ring[n], b = ring[(n + 1) % count];
                if (Vector3.Dot(Vector3.Cross(vertices[a] - vertices[center], vertices[b] - vertices[center]), vertices[center]) < 0f) (a, b) = (b, a);
                triangles.Add(center); triangles.Add(a); triangles.Add(b);
            }
        }
        int level = Math.Max(topology.SubdivisionLevel, Math.Min(GeodesicGridTopology.MaxSupportedSubdivision, renderSubdivision));
        for (int subdivision = topology.SubdivisionLevel; subdivision < level; subdivision++)
        {
            var midpoints = new Dictionary<ulong, int>();
            var refined = new List<int>(triangles.Count * 4);
            int Midpoint(int a, int b)
            {
                ulong key = ((ulong)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                if (midpoints.TryGetValue(key, out int index)) return index;
                Vector3 direction = (vertices[a] + vertices[b]).normalized;
                int owner = Vector3.Dot(direction, topology.CellDirections[owners[a]]) >= Vector3.Dot(direction, topology.CellDirections[owners[b]]) ? owners[a] : owners[b];
                index = AddVertex(direction, owner); midpoints.Add(key, index); return index;
            }
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);
                refined.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            triangles = refined;
        }
        var samples = new IcosphereDirectionSample[vertices.Count];
        var neighborIndices = new List<int>(vertices.Count * 6);
        var neighborWeights = new List<float>(vertices.Count * 6);
        for (int i = 0; i < vertices.Count; i++)
        {
            int nearest = GeodesicOceanConnectivity.FindNearestCell(topology, vertices[i], owners[i]);
            // Boundary vertices may tie with a dry cell; retain a wet owner for resource tinting.
            int cell = oceanMask[nearest] ? nearest : owners[i];
            samples[i] = new IcosphereDirectionSample(cell, neighborIndices.Count, topology.NeighborCounts[cell]);
            for (int n = 0; n < topology.NeighborCounts[cell]; n++)
            {
                int neighbor = topology.Neighbors6[cell * 6 + n];
                neighborIndices.Add(neighbor);
                float dot = Vector3.Dot(vertices[i], topology.CellDirections[neighbor]);
                neighborWeights.Add(1f / Mathf.Max(0.0001f, Mathf.Acos(Mathf.Clamp(dot, -1f, 1f))));
            }
        }
        mapping = new IcosphereDirectionMapping(topology.SubdivisionLevel, level, samples,
            neighborIndices.ToArray(), neighborWeights.ToArray(), false, neighborIndices.Count);
        return new IcosphereRenderGeometry(level, vertices.ToArray(), triangles.ToArray());
    }
}
