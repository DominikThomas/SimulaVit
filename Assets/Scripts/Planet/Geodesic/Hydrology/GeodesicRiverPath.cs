using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded corridor search, shared endpoints at tributaries, no unconstrained smoothing.
/// Refuses an uphill route instead of hiding a filled basin with floating water or terrain carving.</summary>
public static class GeodesicRiverPath
{
    /// <summary>Archived L7 experiment; the restored baseline never calls this method.
    /// Move only non-junction controls off graph vertices. Every move must descend
    /// across the actual terrain on both sides; sources, confluences, coasts and lakes stay fixed.</summary>
    public static Vector3[] BuildTerrainAnchors(IGeodesicHydrologyTopology topology, GeodesicDrainageGraph graph,
        GeodesicRiverTerrain terrain, GeodesicLakeBasins lakes, double threshold, float tolerance)
    {
        var anchors = (Vector3[])topology.CellDirections.Clone();
        var incoming = new int[graph.CellCount]; var upstream = new int[graph.CellCount];
        for (int cell = 0; cell < graph.CellCount; cell++)
        {
            int next = graph.DrainageReceiver[cell];
            if (next < 0 || graph.Ocean[cell] || graph.AccumulatedRunoff[cell] < threshold) continue;
            incoming[next]++; upstream[next] = cell;
        }
        for (int pass = 0; pass < 2; pass++)
        {
            var updated = (Vector3[])anchors.Clone();
            for (int cell = 0; cell < graph.CellCount; cell++)
            {
                int next = graph.DrainageReceiver[cell], prior = upstream[cell];
                if (incoming[cell] != 1 || next < 0 || graph.Ocean[cell] || graph.Ocean[next] ||
                    Lake(cell) || Lake(prior) || Lake(next) || graph.AccumulatedRunoff[cell] < threshold) continue;
                Vector3 candidate = (anchors[cell] * .5f + (anchors[prior] + anchors[next]) * .25f).normalized;
                var localPath = new[] { anchors[prior], candidate, anchors[next] };
                if (terrain.MaximumVisibleUphillExcursion(localPath) <= tolerance) updated[cell] = candidate;
            }
            anchors = updated;
        }
        return anchors;

        bool Lake(int cell) => lakes != null && lakes.Enabled && lakes.BasinId[cell] >= 0 &&
            lakes.Basins[lakes.BasinId[cell]].Selected;
    }

    public static Vector3[] Refine(Vector3 source, Vector3 receiver, Func<Vector3, float> height,
        int steps, int lanes, float corridorFraction, float uphillTolerance)
    {
        steps = Mathf.Clamp(steps, 4, 64); lanes = Mathf.Clamp(lanes | 1, 3, 15);
        float width = (source - receiver).magnitude * Mathf.Clamp(corridorFraction, 0f, 0.45f);
        Vector3 side = Vector3.Cross(source, receiver).normalized;
        var points = new Vector3[(steps + 1) * lanes];
        var heights = new float[points.Length];
        var cost = new double[points.Length]; Array.Fill(cost, double.PositiveInfinity);
        var previous = new int[points.Length]; Array.Fill(previous, -1);
        int middle = lanes / 2;
        float floor = Mathf.Min(height(source), height(receiver));
        for (int step = 0; step <= steps; step++)
        {
            float t = step / (float)steps;
            Vector3 center = Vector3.Lerp(source, receiver, t).normalized;
            float taper = Mathf.Sin(t * Mathf.PI);
            for (int lane = 0; lane < lanes; lane++)
            {
                int index = step * lanes + lane;
                points[index] = step == 0 ? source : step == steps ? receiver :
                    (center + side * (width * taper * (lane - middle) / middle)).normalized;
                heights[index] = height(points[index]);
            }
        }
        cost[middle] = 0d;
        for (int step = 1; step <= steps; step++)
        for (int lane = 0; lane < lanes; lane++)
        {
            if (step == steps && lane != middle) continue;
            int index = step * lanes + lane;
            for (int priorLane = 0; priorLane < lanes; priorLane++)
            {
                if (step > 1 && step < steps && Math.Abs(priorLane - lane) > 2) continue;
                int prior = (step - 1) * lanes + priorLane;
                if (double.IsPositiveInfinity(cost[prior]) || heights[index] > heights[prior] + uphillTolerance ||
                    heights[index] > heights[middle] + uphillTolerance) continue;
                double candidate = cost[prior] + Math.Max(0f, heights[index] - floor) +
                    (points[index] - points[prior]).sqrMagnitude * 0.01d;
                if (candidate >= cost[index]) continue;
                // End heights are already cached. Test interior samples only when this transition
                // can improve the solution; a thin ridge must not slip between grid endpoints.
                if (!IsDownhill(points[prior], points[index], height, uphillTolerance, heights[prior], heights[index])) continue;
                cost[index] = candidate; previous[index] = prior;
            }
        }
        int end = steps * lanes + middle;
        if (previous[end] < 0) return Array.Empty<Vector3>();
        var result = new Vector3[steps + 1];
        for (int step = steps; step >= 0; step--) { result[step] = points[end]; end = previous[end]; }
        return result;
    }

    public static bool IsDownhill(Vector3 a, Vector3 b, Func<Vector3, float> height, float tolerance)
        => IsDownhill(a, b, height, tolerance, height(a), height(b));

    /// <summary>Validate the final centreline at a finer spacing than ribbon construction.
    /// The tolerance is numerical only and applies to total rise from any earlier low point.</summary>
    public static bool IsPathDownhill(Vector3[] path, Func<Vector3, float> height, float tolerance)
        => MaximumUphillExcursion(path, height) <= Mathf.Max(0f, tolerance);

    public static float MaximumUphillExcursion(Vector3[] path, Func<Vector3, float> height)
    {
        if (path == null || path.Length < 2) return float.PositiveInfinity;
        float lowest = height(path[0]), rise = 0f;
        for (int segment = 1; segment < path.Length; segment++)
        for (int sample = 1; sample <= 8; sample++)
        {
            float value = height(Vector3.Lerp(path[segment - 1], path[segment], sample / 8f).normalized);
            if (!float.IsFinite(value)) return float.PositiveInfinity;
            rise = Mathf.Max(rise, value - lowest);
            lowest = Mathf.Min(lowest, value);
        }
        return rise;
    }

    private static bool IsDownhill(Vector3 a, Vector3 b, Func<Vector3, float> height, float tolerance, float start, float end)
    {
        float last = start;
        for (int i = 1; i <= 4; i++)
        {
            float next = i == 4 ? end : height(Vector3.Lerp(a, b, i / 4f).normalized);
            if (next > last + tolerance || next > start + tolerance) return false;
            last = next;
        }
        return true;
    }

    /// <summary>Stop at the first sea-level crossing, rather than extending to the ocean cell centre.</summary>
    public static Vector3[] ClipAtCoast(Vector3[] path, Func<Vector3, float> height, float seaLevel, Func<Vector3, bool> retainedOcean = null)
    {
        var result = new List<Vector3>();
        for (int i = 0; i < path.Length; i++)
        {
            if (height(path[i]) <= seaLevel && (retainedOcean == null || retainedOcean(path[i])) && result.Count > 0)
            {
                Vector3 land = result[result.Count - 1], water = path[i];
                for (int j = 0; j < 16; j++)
                { Vector3 middle = (land + water).normalized; if (height(middle) > seaLevel || (retainedOcean != null && !retainedOcean(middle))) land = middle; else water = middle; }
                result.Add((land + water).normalized); break;
            }
            result.Add(path[i]);
        }
        return result.ToArray();
    }
}
