using System;
using UnityEngine;

public enum GeodesicRiverReachFailure { None, ProjectionMismatch, CorridorFailure, UnresolvedDepression, TopologyFailure }
public sealed class GeodesicRiverReachPlan
{
    public Vector3[] Path = Array.Empty<Vector3>();
    public bool LakeConnected;
    public int InletBasin = -1, OutletBasin = -1;
    public GeodesicRiverReachFailure Failure;
}

/// <summary>Visual river planning consumes existing lakes; rendering failure can never create one.</summary>
public static class GeodesicLakeRiverRouting
{
    public static GeodesicRiverReachPlan Build(int cell, GeodesicDrainageGraph graph, Vector3[] anchors,
        GeodesicRiverTerrain terrain, GeodesicLakeBasins lakes, GeodesicLakeGeometry water,
        int steps, int lanes, float corridor, float tolerance, bool oceanEnabled, float seaLevel,
        Func<Vector3, bool> oceanMask)
    {
        var plan = new GeodesicRiverReachPlan();
        int receiver = graph.DrainageReceiver[cell];
        if (receiver < 0 || graph.FilledElevation[receiver] > graph.FilledElevation[cell] + GeodesicLakeBasins.ElevationEpsilon)
        { plan.Failure = GeodesicRiverReachFailure.TopologyFailure; return plan; }
        int Basin(int c) => lakes != null && lakes.Enabled && lakes.BasinId[c] >= 0 && lakes.Basins[lakes.BasinId[c]].Selected ? lakes.BasinId[c] : -1;
        int sourceBasin = Basin(cell), targetBasin = Basin(receiver);
        Vector3 start = anchors[cell], end = anchors[receiver];
        int sourceWater = water != null ? water.BasinAtDirection(start) : -1;
        int targetWater = water != null ? water.BasinAtDirection(end) : -1;
        if (sourceBasin >= 0 && sourceBasin == targetBasin)
        {
            // The connected visible pool bridges its submerged routing edges. Dry projection
            // islands are diagnosed separately, never counted as successful water connections.
            if (sourceWater == sourceBasin && targetWater == sourceBasin) plan.LakeConnected = true;
            else plan.Failure = GeodesicRiverReachFailure.ProjectionMismatch;
            return plan;
        }
        if (sourceWater >= 0 && sourceWater == targetWater)
        { plan.LakeConnected = true; return plan; }
        bool outletContinuation = sourceBasin < 0 && sourceWater >= 0 && IsOutletContinuation(cell, sourceWater);
        if (outletContinuation) sourceBasin = sourceWater;
        if (sourceBasin >= 0)
        {
            var basin = lakes.Basins[sourceBasin];
            if (!outletContinuation && (cell != basin.SpillFromCell || receiver != basin.SpillCell))
            { plan.Failure = GeodesicRiverReachFailure.TopologyFailure; return plan; }
            if (!water.TryClosestShore(sourceBasin, end, out start))
            { plan.Failure = GeodesicRiverReachFailure.ProjectionMismatch; return plan; }
            plan.OutletBasin = sourceBasin;
        }
        else if (sourceWater >= 0)
        {
            // A coarse land anchor projected into a lake does not authorize a second outlet.
            plan.Failure = GeodesicRiverReachFailure.ProjectionMismatch; return plan;
        }
        if (targetBasin >= 0 || targetWater >= 0)
        {
            int id = targetBasin >= 0 ? targetBasin : targetWater;
            if (!water.TryClosestShore(id, start, out end))
            { plan.Failure = GeodesicRiverReachFailure.ProjectionMismatch; return plan; }
            plan.InletBasin = id;
        }
        bool lakeEdge = plan.InletBasin >= 0 || plan.OutletBasin >= 0;
        // Bound the visual correction to the local edge corridor; a far side of a lake is
        // not an acceptable substitute for the authoritative inlet or spill edge.
        float span = (anchors[cell] - anchors[receiver]).magnitude;
        if (lakeEdge && ((start - anchors[cell]).magnitude > span * 2f || (end - anchors[receiver]).magnitude > span * 2f))
        { plan.Failure = GeodesicRiverReachFailure.ProjectionMismatch; return plan; }
        // Grade lake approaches against visible vertex elevations. Exact ray/triangle
        // Radius includes spherical facet sag even on a level spillway; it remains the
        // authority for ribbon placement, not the downhill test.
        Func<Vector3, float> height = lakeEdge ? terrain.VisibleHeight : terrain.Height;
        if (lakeEdge && height(end) > height(start) + tolerance)
        { plan.Failure = GeodesicRiverReachFailure.ProjectionMismatch; return plan; }
        plan.Path = GeodesicRiverPath.Refine(start, end, height, steps, lanes, corridor, tolerance);
        if (plan.Path.Length < 2)
        {
            plan.Failure = lakeEdge ? GeodesicRiverReachFailure.CorridorFailure :
                graph.FillDepth[cell] > GeodesicLakeBasins.ElevationEpsilon ? GeodesicRiverReachFailure.UnresolvedDepression : GeodesicRiverReachFailure.CorridorFailure;
            return plan;
        }
        if (oceanEnabled) plan.Path = GeodesicRiverPath.ClipAtCoast(plan.Path, terrain.VisibleHeight, seaLevel, oceanMask);
        if (water != null)
        {
            // A clipped pool can extend into the spill's neighbouring routing cell. Follow
            // that same single receiver chain and start its ribbon at the actual water exit.
            if (plan.OutletBasin >= 0 && water.BasinAtDirection(plan.Path[0]) == plan.OutletBasin)
            {
                int firstLand = 1;
                while (firstLand < plan.Path.Length && water.BasinAtDirection(plan.Path[firstLand]) == plan.OutletBasin) firstLand++;
                if (firstLand == plan.Path.Length) { plan.Path = Array.Empty<Vector3>(); plan.LakeConnected = true; return plan; }
                Vector3 wet = plan.Path[firstLand - 1], dry = plan.Path[firstLand];
                for (int iteration = 0; iteration < 24; iteration++)
                { Vector3 middle = (dry + wet).normalized; if (water.BasinAtDirection(middle) == plan.OutletBasin) wet = middle; else dry = middle; }
                var clipped = new Vector3[plan.Path.Length - firstLand + 1]; clipped[0] = (dry + wet).normalized;
                Array.Copy(plan.Path, firstLand, clipped, 1, plan.Path.Length - firstLand); plan.Path = clipped;
            }
            // Catch a lake encountered between coarse endpoints. The first shoreline is the
            // visual inlet; no ribbon is drawn over the water body.
            for (int i = 1; i < plan.Path.Length; i++)
            {
                int id = water.BasinAtDirection(plan.Path[i]);
                if (id < 0) continue;
                if (id != targetBasin && id != targetWater)
                {
                    // A visual intersection cannot redirect the authoritative receiver graph.
                    plan.Path = Array.Empty<Vector3>(); plan.Failure = GeodesicRiverReachFailure.ProjectionMismatch; return plan;
                }
                Vector3 dry = plan.Path[i - 1], wet = plan.Path[i];
                if (water.BasinAtDirection(dry) >= 0) continue;
                for (int iteration = 0; iteration < 24; iteration++)
                { Vector3 middle = (dry + wet).normalized; if (water.BasinAtDirection(middle) >= 0) wet = middle; else dry = middle; }
                var clipped = new Vector3[i + 1]; Array.Copy(plan.Path, clipped, i); clipped[i] = (dry + wet).normalized;
                plan.Path = clipped; plan.InletBasin = id; break;
            }
        }
        plan.LakeConnected = plan.Path.Length > 1 && (plan.InletBasin >= 0 || plan.OutletBasin >= 0);
        return plan;

        bool IsOutletContinuation(int source, int id)
        {
            // Only the authoritative spill chain may leave a pool. Other land anchors
            // projected into it do not authorize duplicate outlet ribbons.
            int cursor = lakes.Basins[id].SpillCell;
            for (int visited = 0; cursor >= 0 && visited < graph.CellCount; visited++)
            {
                if (water.BasinAtDirection(anchors[cursor]) != id) return false;
                if (cursor == source) return true;
                cursor = graph.DrainageReceiver[cursor];
            }
            return false;
        }
    }
}
