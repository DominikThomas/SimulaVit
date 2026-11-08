using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal sealed class GeodesicTinLakeCrossings
{
    internal sealed class Crossing
    {
        internal GeodesicTinLakeSediment.Footprint Bed;
        internal Vector2[] Path;
        internal double Discharge, DischargeAtInlet;
    }

    internal readonly List<Crossing> Paths = new List<Crossing>();
    internal readonly List<string> Failures = new List<string>();
    internal readonly HashSet<int> VisibleOutlets = new HashSet<int>();
    internal int Direct, Interior, Junctions, LogicalConnections, Gaps;

    internal static GeodesicTinLakeCrossings Build(GeodesicTinHydrology tin, GeodesicTinLakeRouting routing,
        GeodesicTinLakeSediment sediment, bool[] renderedRivers)
    {
        var result = new GeodesicTinLakeCrossings();
        result.LogicalConnections = routing.RiverReaches.Count(r => r.ReceivingLakeId >= 0 || r.JunctionNetwork >= 0) +
            routing.Lakes.Count(l => !l.Terminal);
        foreach (var failure in sediment.GeometryFailures) result.Fail(failure.Key, failure.Value);
        if (sediment.LakeCount == 0) return result;
        var inlets = routing.Lakes.ToDictionary(l => l.BasinId, _ => new List<(TinPoint Point, double Flow)>());
        var lakes = routing.Lakes.ToDictionary(l => l.BasinId);
        var queue = new Queue<int>();
        void AddInlet(int id, TinPoint point, double flow)
        {
            if (inlets[id].Count == 0) queue.Enqueue(id);
            inlets[id].Add((point, flow));
        }

        foreach (var reach in routing.RiverReaches)
        {
            if (reach.ReceivingLakeId < 0) continue;
            if (reach.Changed && routing.Directions(reach.Samples).Length < 2) continue;
            if (renderedRivers[reach.RawNetworkId])
                AddInlet(reach.ReceivingLakeId, reach.Samples.Last(), routing.RawReachDischarge(reach.RawNetworkId).Last());
            else if (sediment.Lakes.ContainsKey(reach.ReceivingLakeId))
                result.Fail(reach.ReceivingLakeId, $"inlet river {reach.RawNetworkId} did not render");
        }

        // Keep existing visible outlets and continue a visible inlet through a filtered
        // bed even when the lake outlet alone would fall below the ribbon threshold.
        var followed = new HashSet<int>();
        void Follow(TinLakeFlowNode lake)
        {
            if (!followed.Add(lake.BasinId)) return;
            if (lake.OutletPath.Length >= 2) result.VisibleOutlets.Add(lake.BasinId);
            if (lake.DownstreamLakeEntry.HasValue)
                AddInlet(lake.DownstreamLakeId, lake.DownstreamLakeEntry.Value, lake.Discharge);
            if (sediment.Lakes.ContainsKey(lake.BasinId) && lake.JoinNetwork >= 0 && !renderedRivers[lake.JoinNetwork])
                result.Fail(lake.BasinId, $"outlet river junction {lake.JoinNetwork} did not render");
        }
        foreach (var lake in routing.Lakes)
            if (lake.OutletReachId >= 0 || lake.DownstreamLakeEntry.HasValue && lake.Discharge > 0 && lake.OutletPath.Length == 0 &&
                !sediment.Lakes.ContainsKey(lake.BasinId))
                Follow(lake);
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            if (sediment.Lakes.ContainsKey(id)) Follow(lakes[id]);
        }

        foreach (var lake in routing.Lakes)
        {
            if (!sediment.Lakes.TryGetValue(lake.BasinId, out var bed)) continue;
            if (lake.Terminal || !lake.Basin.Spill.HasValue) continue;
            var entries = inlets[lake.BasinId].GroupBy(i => tin.Direction(i.Point))
                .Select(g => (Point: g.Key, Flow: g.Max(i => i.Flow))).OrderByDescending(i => i.Flow);
            var tree = new List<Vector2[]>();
            Vector2 outlet = bed.Project(tin.Direction(lake.Basin.Spill.Value));
            bool merged = false;
            foreach (var entry in entries)
            {
                Vector2 inlet = bed.Project(entry.Point);
                if ((inlet - outlet).sqrMagnitude < 1e-14f) continue; // a shared rim point needs no ribbon
                var path = bed.Route(inlet, outlet, out bool direct);
                if (path == null)
                {
                    result.Fail(lake.BasinId, "no interior route between the original inlet and outlet");
                    continue;
                }
                bool trunk = tree.Count == 0;
                if (!trunk)
                {
                    // Prefer a shared interior join. A straight branch to a trunk
                    // waypoint stays inside the same polygon union as the direct route.
                    foreach (var join in tree[0].Skip(1).Take(tree[0].Length - 2).OrderBy(p => (p - inlet).sqrMagnitude))
                    {
                        if (!bed.Covers(inlet, join)) continue;
                        path = new[] { inlet, (inlet + join) * .5f, join };
                        direct = true;
                        break;
                    }
                    path = TrimAtJoin(path, tree);
                    merged = true;
                }
                if (path.Length < 2) continue;
                tree.Add(path);
                result.Paths.Add(new Crossing
                {
                    Bed = bed, Path = path, DischargeAtInlet = entry.Flow,
                    Discharge = trunk ? lake.Discharge : entry.Flow
                });
                if (direct) result.Direct++; else result.Interior++;
            }
            if (merged) result.Junctions++;
        }
        return result;
    }

    internal void Fail(int basin, string reason)
    {
        Failures.Add($"basin={basin} {reason}"); Gaps++;
    }

    private static Vector2[] TrimAtJoin(Vector2[] path, List<Vector2[]> tree)
    {
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 a = path[i - 1], ab = path[i] - a;
            float best = float.PositiveInfinity;
            foreach (var branch in tree)
            for (int j = 1; j < branch.Length; j++)
            {
                Vector2 c = branch[j - 1], cd = branch[j] - c;
                float cross = GeodesicTinLakeSediment.Cross(ab, cd);
                if (Mathf.Abs(cross) > 1e-14f)
                {
                    float t = GeodesicTinLakeSediment.Cross(c - a, cd) / cross;
                    float u = GeodesicTinLakeSediment.Cross(c - a, ab) / cross;
                    if (t >= 0 && t <= 1 && u >= 0 && u <= 1) best = Mathf.Min(best, t);
                }
                else if (ab.sqrMagnitude > 0 && Mathf.Abs(GeodesicTinLakeSediment.Cross(c - a, ab)) <= 1e-7f * ab.magnitude)
                {
                    float t = Vector2.Dot(c - a, ab) / ab.sqrMagnitude;
                    float u = Vector2.Dot(branch[j] - a, ab) / ab.sqrMagnitude;
                    if (Mathf.Max(t, u) >= 0 && Mathf.Min(t, u) <= 1) best = Mathf.Min(best, Mathf.Max(0, Mathf.Min(t, u)));
                }
            }
            if (float.IsPositiveInfinity(best)) continue;
            var trimmed = path.Take(i).ToList();
            if (best > 0) trimmed.Add(a + ab * best);
            return trimmed.ToArray();
        }
        return path;
    }

    // Clip ribbon banks against the same facets as the sediment surface. The centreline
    // is accepted on topology and polygon containment alone, including zero gradient.
    internal static void AddRibbon(Crossing crossing, float inletWidth, float outletWidth, float offset, Color color, bool lines,
        List<Vector3> vertices, List<int> indices, List<Color> colors)
    {
        var bed = crossing.Bed;
        var path = crossing.Path;
        void Add(Vector2 p) { vertices.Add(bed.Position(p, offset)); colors.Add(color); }
        if (lines)
        {
            Add(path[0]);
            for (int i = 1; i < path.Length; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt((path[i] - path[i - 1]).magnitude / .002f));
                for (int j = 1; j <= steps; j++)
                {
                    indices.Add(vertices.Count - 1); indices.Add(vertices.Count);
                    Add(Vector2.Lerp(path[i - 1], path[i], (float)j / steps));
                }
            }
            return;
        }

        var banks = new Vector2[path.Length];
        float length = 0f;
        for (int i = 1; i < path.Length; i++) length += Vector2.Distance(path[i - 1], path[i]);
        float travelled = 0f;
        for (int i = 0; i < path.Length; i++)
        {
            if (i > 0) travelled += Vector2.Distance(path[i - 1], path[i]);
            Vector2 tangent = (path[Math.Min(i + 1, path.Length - 1)] - path[Math.Max(0, i - 1)]).normalized;
            float width = Mathf.Lerp(inletWidth, outletWidth, length > 0f ? travelled / length : 0f);
            banks[i] = new Vector2(-tangent.y, tangent.x) * (width * .5f);
        }
        var polygon = new List<Vector2>(8); var buffer = new List<Vector2>(8);
        void Strip(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            foreach (var facet in bed.Facets.Values)
            {
                polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c); polygon.Add(d);
                facet.ClipPolygon(polygon, buffer);
                if (polygon.Count < 3) continue;
                int first = vertices.Count;
                foreach (var p in polygon) Add(p);
                for (int j = 1; j + 1 < polygon.Count; j++)
                {
                    float cross = GeodesicTinLakeSediment.Cross(polygon[j] - polygon[0], polygon[j + 1] - polygon[0]);
                    if (cross == 0) continue;
                    indices.Add(first); indices.Add(first + (cross > 0 ? j : j + 1)); indices.Add(first + (cross > 0 ? j + 1 : j));
                }
            }
        }
        void AddSeam(int end, int neighbor)
        {
            // A lakebed strip and the adjacent reach can approach the same
            // shore at different angles. Clip a short overlap to the bed so
            // their differing cross-sections do not leave a wedge uncovered.
            Vector2 point = path[end], bank = banks[end];
            Vector2 past = point + (point - path[neighbor]).normalized * bank.magnitude;
            Strip(point, point - bank, past - bank, past);
            Strip(point, past, past + bank, point + bank);
        }
        AddSeam(0, 1);
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 a = path[i - 1], b = path[i];
            Strip(a, a - banks[i - 1], b - banks[i], b);
            Strip(a, b, b + banks[i], a + banks[i - 1]);
        }
        AddSeam(path.Length - 1, path.Length - 2);
    }
}
