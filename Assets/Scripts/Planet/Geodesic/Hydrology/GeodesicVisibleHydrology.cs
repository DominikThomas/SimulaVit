using System;
using UnityEngine;

/// <summary>Routing heights and ocean roots on the existing render mesh. The visible mesh is never changed.</summary>
public static class GeodesicVisibleHydrology
{
    public static string FeatureAudit(IGeodesicHydrologyTopology topology, Vector3[] surface, float[] filtered)
    {
        int ridges = 0, retainedRidges = 0, valleys = 0, retainedValleys = 0;
        double ordinarySlopeError = 0d;
        int ordinarySlopes = 0;
        for (int i = 0; i < topology.CellCount; i++)
        {
            double visibleMean = 0d, filteredMean = 0d;
            for (int slot = 0; slot < topology.NeighborCounts[i]; slot++)
            {
                int neighbor = topology.Neighbors6[i * 6 + slot];
                visibleMean += surface[neighbor].magnitude;
                filteredMean += filtered[neighbor];
            }
            visibleMean /= topology.NeighborCounts[i];
            filteredMean /= topology.NeighborCounts[i];
            double visibleProminence = surface[i].magnitude - visibleMean;
            double filteredProminence = filtered[i] - filteredMean;
            if (visibleProminence > .002d)
            { ridges++; if (filteredProminence > visibleProminence * .25d) retainedRidges++; }
            else if (visibleProminence < -.002d)
            { valleys++; if (filteredProminence < visibleProminence * .25d) retainedValleys++; }
            else
            { ordinarySlopes++; ordinarySlopeError += Math.Abs(filteredProminence - visibleProminence); }
        }
        return $"sampledRidges={ridges} retainedRidges={retainedRidges} sampledValleys={valleys} " +
            $"retainedValleys={retainedValleys} ordinarySlopeMeanCurvatureError=" +
            $"{(ordinarySlopes > 0 ? ordinarySlopeError / ordinarySlopes : 0d):G6}";
    }

    public static float[] Filter(IGeodesicHydrologyTopology topology, Vector3[] surface,
        float[] formerHeights, out double meanVisibleDifference, out double meanFormerDifference)
    {
        int count = topology.CellCount;
        if (surface == null || surface.Length != count) throw new ArgumentException("Render surface count mismatch.");
        var visible = new float[count];
        var filtered = new float[count];
        double visibleError = 0d, formerError = 0d;
        for (int i = 0; i < count; i++) visible[i] = surface[i].magnitude;
        // A single half-strength umbrella pass removes vertex-scale roughness while retaining
        // features spanning several triangles. Angular weights account for pentagons and spacing.
        for (int i = 0; i < count; i++)
        {
            double weighted = 0d, weight = 0d;
            for (int slot = 0; slot < topology.NeighborCounts[i]; slot++)
            {
                int neighbor = topology.Neighbors6[i * 6 + slot];
                double w = 1d / Math.Max(1e-7f, topology.NeighborAngularDistance(i, slot));
                weighted += visible[neighbor] * w; weight += w;
            }
            filtered[i] = (float)(0.5d * visible[i] + 0.5d * weighted / weight);
            visibleError += Math.Abs(filtered[i] - visible[i]);
            if (formerHeights != null && formerHeights.Length == count)
                formerError += Math.Abs(filtered[i] - formerHeights[i]);
        }
        meanVisibleDifference = visibleError / count;
        meanFormerDifference = formerHeights != null && formerHeights.Length == count ? formerError / count : double.NaN;
        return filtered;
    }

    public static bool[] MapRetainedOcean(IGeodesicHydrologyTopology topology, Vector3[] surface,
        float seaLevel, Func<Vector3, bool> retainedAtDirection,
        IcosphereDirectionMapping mapping = null, bool[] simulationOcean = null,
        Func<int, bool> excludedSimulationBasin = null)
    {
        int count = topology.CellCount;
        if (surface == null || surface.Length != count) throw new ArgumentException("Render surface count mismatch.");
        var visible = new float[count];
        for (int i = 0; i < count; i++) visible[i] = surface[i].magnitude;
        var ocean = new bool[count];
        var queue = new int[count];
        int head = 0, tail = 0;
        // The L6 retained mask supplies component membership. Every below-sea L7 vertex
        // connected to a retained seed belongs to that ocean, including shoreline vertices
        // whose nearest L6 centre is land. A separate submerged component stays inland.
        bool[] seeds = mapping != null && simulationOcean != null
            ? GeodesicHydrologyMapping.MapAuthoritativeOcean(mapping, simulationOcean, surface, seaLevel)
            : null;
        for (int i = 0; i < count; i++)
            if (visible[i] < seaLevel && (seeds != null ? seeds[i] : retainedAtDirection(topology.CellDirections[i])) &&
                !(mapping != null && excludedSimulationBasin != null &&
                  excludedSimulationBasin(mapping.Samples[i].NearestCell)))
            { ocean[i] = true; queue[tail++] = i; }
        while (head < tail)
        {
            int cell = queue[head++];
            for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
            {
                int neighbor = topology.Neighbors6[cell * 6 + slot];
                if (ocean[neighbor] || visible[neighbor] >= seaLevel ||
                    (mapping != null && excludedSimulationBasin != null &&
                     excludedSimulationBasin(mapping.Samples[neighbor].NearestCell))) continue;
                ocean[neighbor] = true; queue[tail++] = neighbor;
            }
        }
        return ocean;
    }

    public static Vector3[] InteriorPath(IGeodesicHydrologyTopology topology, GeodesicRiverTerrain terrain,
        float[] routing, int cell, int receiver)
    {
        Vector3 a = topology.CellDirections[cell], b = topology.CellDirections[receiver];
        int third = -1;
        float best = float.PositiveInfinity;
        for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
        {
            int candidate = topology.Neighbors6[cell * 6 + slot];
            if (candidate == receiver) continue;
            for (int other = 0; other < topology.NeighborCounts[receiver]; other++)
                if (topology.Neighbors6[receiver * 6 + other] == candidate && routing[candidate] < best)
                { third = candidate; best = routing[candidate]; }
        }
        Vector3 middle = Vector3.Lerp(a, b, 0.5f).normalized;
        if (third >= 0)
        {
            Vector3 interior = (a * 0.425f + b * 0.425f + topology.CellDirections[third] * 0.15f).normalized;
            // Do not bend toward a visible ridge. The centreline may cross the triangle
            // interior, but its endpoint connectivity remains the receiver graph's edge.
            if (terrain.VisibleHeight(interior) <= Mathf.Max(terrain.VisibleHeight(a), terrain.VisibleHeight(b)) + 0.0005f)
                middle = interior;
        }
        return new[] { a, middle, b };
    }
}
