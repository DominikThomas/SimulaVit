using System.Collections.Generic;
using UnityEngine;

public static class IcosphereDirectionMappingBuilder
{
    public static IcosphereDirectionMapping Build(GeodesicGridTopology simulationTopology, IcosphereRenderGeometry targetGeometry)
    {
        if (simulationTopology == null) return new IcosphereDirectionMapping(0, targetGeometry.SubdivisionLevel, new IcosphereDirectionSample[targetGeometry.VertexCount], new int[0], new float[0], false, 0);

        int simulationSubdivision = simulationTopology.SubdivisionLevel;
        int targetSubdivision = targetGeometry.SubdivisionLevel;
        if (targetSubdivision == simulationSubdivision && ValidateIdentity(simulationTopology, targetGeometry))
        {
            return BuildIdentity(simulationTopology, targetGeometry);
        }

        if (targetSubdivision > simulationSubdivision)
        {
            return BuildByCarryingSimulationCandidates(simulationTopology, targetGeometry, targetSubdivision - simulationSubdivision);
        }

        if (ValidatePrefixIdentity(simulationTopology, targetGeometry))
        {
            return BuildPrefixIdentity(simulationTopology, targetGeometry);
        }

        return BuildByBoundedFallback(simulationTopology, targetGeometry);
    }

    private static IcosphereDirectionMapping BuildIdentity(GeodesicGridTopology topology, IcosphereRenderGeometry targetGeometry)
    {
        var samples = new IcosphereDirectionSample[targetGeometry.VertexCount];
        List<int> neighborIndices = new List<int>(targetGeometry.VertexCount * 6);
        List<float> neighborWeights = new List<float>(targetGeometry.VertexCount * 6);
        long inspected = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = AppendNeighbors(topology, targetGeometry.UnitVertices[i], i, neighborIndices, neighborWeights, ref inspected);
        }
        return new IcosphereDirectionMapping(topology.SubdivisionLevel, targetGeometry.SubdivisionLevel, samples, neighborIndices.ToArray(), neighborWeights.ToArray(), true, inspected);
    }

    private static IcosphereDirectionMapping BuildPrefixIdentity(GeodesicGridTopology topology, IcosphereRenderGeometry targetGeometry)
    {
        var samples = new IcosphereDirectionSample[targetGeometry.VertexCount];
        List<int> neighborIndices = new List<int>(targetGeometry.VertexCount * 6);
        List<float> neighborWeights = new List<float>(targetGeometry.VertexCount * 6);
        long inspected = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = AppendNeighbors(topology, targetGeometry.UnitVertices[i], i, neighborIndices, neighborWeights, ref inspected);
        }
        return new IcosphereDirectionMapping(topology.SubdivisionLevel, targetGeometry.SubdivisionLevel, samples, neighborIndices.ToArray(), neighborWeights.ToArray(), true, inspected);
    }

    private static IcosphereDirectionMapping BuildByCarryingSimulationCandidates(GeodesicGridTopology topology, IcosphereRenderGeometry targetGeometry, int extraLevels)
    {
        IcosphereRenderGeometry baseGeometry = IcosphereRenderGeometryCache.GetOrBuild(topology.SubdivisionLevel);
        List<Vector3> vertices = new List<Vector3>(targetGeometry.VertexCount);
        List<int> triangles = new List<int>(baseGeometry.Triangles);
        List<int[]> candidates = new List<int[]>(targetGeometry.VertexCount);
        for (int i = 0; i < baseGeometry.VertexCount; i++)
        {
            vertices.Add(baseGeometry.UnitVertices[i]);
            candidates.Add(new[] { i });
        }

        for (int level = 0; level < extraLevels; level++) Subdivide(vertices, triangles, candidates);

        var samples = new IcosphereDirectionSample[targetGeometry.VertexCount];
        List<int> neighborIndices = new List<int>(targetGeometry.VertexCount * 6);
        List<float> neighborWeights = new List<float>(targetGeometry.VertexCount * 6);
        long inspected = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            int nearest = FindNearestFromCandidates(topology, targetGeometry.UnitVertices[i], candidates[i], ref inspected);
            samples[i] = AppendNeighbors(topology, targetGeometry.UnitVertices[i], nearest, neighborIndices, neighborWeights, ref inspected);
        }
        return new IcosphereDirectionMapping(topology.SubdivisionLevel, targetGeometry.SubdivisionLevel, samples, neighborIndices.ToArray(), neighborWeights.ToArray(), false, inspected);
    }

    private static IcosphereDirectionMapping BuildByBoundedFallback(GeodesicGridTopology topology, IcosphereRenderGeometry targetGeometry)
    {
        var samples = new IcosphereDirectionSample[targetGeometry.VertexCount];
        List<int> neighborIndices = new List<int>(targetGeometry.VertexCount * 6);
        List<float> neighborWeights = new List<float>(targetGeometry.VertexCount * 6);
        long inspected = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            int nearest = FindNearestBrute(topology, targetGeometry.UnitVertices[i], ref inspected);
            samples[i] = AppendNeighbors(topology, targetGeometry.UnitVertices[i], nearest, neighborIndices, neighborWeights, ref inspected);
        }
        return new IcosphereDirectionMapping(topology.SubdivisionLevel, targetGeometry.SubdivisionLevel, samples, neighborIndices.ToArray(), neighborWeights.ToArray(), false, inspected);
    }

    private static bool ValidateIdentity(GeodesicGridTopology topology, IcosphereRenderGeometry targetGeometry)
    {
        if (topology.CellCount != targetGeometry.VertexCount) return false;
        return ValidatePrefixIdentity(topology, targetGeometry);
    }

    private static bool ValidatePrefixIdentity(GeodesicGridTopology topology, IcosphereRenderGeometry targetGeometry)
    {
        if (targetGeometry.VertexCount > topology.CellCount) return false;
        for (int i = 0; i < targetGeometry.VertexCount; i++)
        {
            if (Vector3.Dot(topology.CellDirections[i], targetGeometry.UnitVertices[i]) < 0.999999f) return false;
        }
        return true;
    }

    private static IcosphereDirectionSample AppendNeighbors(GeodesicGridTopology topology, Vector3 direction, int nearest, List<int> neighborIndices, List<float> neighborWeights, ref long inspected)
    {
        int start = neighborIndices.Count;
        byte count = 0;
        if (nearest >= 0)
        {
            int neighborCount = topology.NeighborCounts[nearest];
            for (int n = 0; n < neighborCount; n++)
            {
                int neighbor = topology.Neighbors6[nearest * 6 + n];
                if (neighbor < 0 || neighbor >= topology.CellCount) continue;
                inspected++;
                float dot = Mathf.Clamp(Vector3.Dot(direction, topology.CellDirections[neighbor]), -1f, 1f);
                float weight = 1f / Mathf.Max(0.0001f, Mathf.Acos(dot));
                neighborIndices.Add(neighbor);
                neighborWeights.Add(weight);
                count++;
            }
        }
        return new IcosphereDirectionSample(nearest, start, count);
    }

    private static int FindNearestFromCandidates(GeodesicGridTopology topology, Vector3 direction, int[] candidates, ref long inspected)
    {
        int best = -1;
        float bestDot = -2f;
        for (int i = 0; i < candidates.Length; i++)
        {
            int candidate = candidates[i];
            inspected++;
            float dot = Vector3.Dot(direction, topology.CellDirections[candidate]);
            if (dot > bestDot) { bestDot = dot; best = candidate; }
        }
        return best;
    }

    private static int FindNearestBrute(GeodesicGridTopology topology, Vector3 direction, ref long inspected)
    {
        int best = -1;
        float bestDot = -2f;
        for (int i = 0; i < topology.CellCount; i++)
        {
            inspected++;
            float dot = Vector3.Dot(direction, topology.CellDirections[i]);
            if (dot > bestDot) { bestDot = dot; best = i; }
        }
        return best;
    }

    private static void Subdivide(List<Vector3> vertices, List<int> triangles, List<int[]> candidates)
    {
        Dictionary<ulong, int> midpointCache = new Dictionary<ulong, int>();
        List<int> next = new List<int>(triangles.Count * 4);
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            int ab = Mid(vertices, candidates, midpointCache, a, b), bc = Mid(vertices, candidates, midpointCache, b, c), ca = Mid(vertices, candidates, midpointCache, c, a);
            next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        triangles.Clear();
        triangles.AddRange(next);
    }

    private static int Mid(List<Vector3> vertices, List<int[]> candidates, Dictionary<ulong, int> cache, int a, int b)
    {
        int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
        ulong key = ((ulong)(uint)lo << 32) | (uint)hi;
        if (cache.TryGetValue(key, out int idx)) return idx;
        idx = vertices.Count;
        vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
        candidates.Add(MergeCandidates(candidates[a], candidates[b]));
        cache[key] = idx;
        return idx;
    }

    private static int[] MergeCandidates(int[] a, int[] b)
    {
        List<int> merged = new List<int>(a.Length + b.Length);
        for (int i = 0; i < a.Length; i++) if (!merged.Contains(a[i])) merged.Add(a[i]);
        for (int i = 0; i < b.Length; i++) if (!merged.Contains(b[i])) merged.Add(b[i]);
        merged.Sort();
        return merged.ToArray();
    }
}
