using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Bounded terrain edits and one new outgoing edge per accepted closed basin.
/// The original TIN, lake routes, runoff parcels and ocean mask remain the flow authority.</summary>
public sealed class GeodesicTinSedimentCorridors
{
    // Distances are relative to planet radius. Each centreline uses render edges and
    // blends back into the original surface across the incident facet ring.
    private const float LengthFraction = .22f;
    private const float WidthFraction = .045f;
    private const float CutFraction = .0225f;
    private const float FillFraction = .04f;
    private const float LevelAboveSeaFraction = .004f;
    private const float SurfaceFillFraction = .001f;
    private const float BankFillRatio = .16f;
    private const double VolumeFraction = .00015;
    private const double BatchVolumeFraction = .0007;
    private const int MaximumConnections = 32;

    public sealed class Connection
    {
        public int BasinId { get; }
        public int TargetNetwork { get; }
        public TinPoint? Coast { get; }
        internal int[] Vertices { get; }
        internal int[] InletNetworks { get; }
        internal int[] InletLakes { get; }
        internal double Runoff { get; }
        internal float Length { get; }
        internal double Area { get; }
        internal double Volume { get; }

        internal Connection(int basinId, int targetNetwork, TinPoint? coast, int[] vertices, int[] inletNetworks,
            int[] inletLakes, double runoff, float length, double area, double volume)
        {
            BasinId = basinId; TargetNetwork = targetNetwork; Coast = coast; Vertices = vertices;
            InletNetworks = inletNetworks; InletLakes = inletLakes; Runoff = runoff; Length = length; Area = area; Volume = volume;
        }
    }

    private readonly Dictionary<int, Connection> connections = new Dictionary<int, Connection>();
    private readonly Dictionary<int, float> elevations = new Dictionary<int, float>();
    private readonly List<string> decisions = new List<string>();
    private readonly Dictionary<int, string> rejected = new Dictionary<int, string>();
    private readonly Dictionary<int, float> receiverApproaches = new Dictionary<int, float>();
    private readonly GeodesicDrainageDelivery baseline;
    private int candidates;
    private double area, volume, runoff;
    public IReadOnlyDictionary<int, Connection> Connections => connections;
    internal IReadOnlyDictionary<int, float> Elevations => elevations;
    internal IEnumerable<string> Decisions => decisions;
    internal string Failure { get; private set; }
    internal string DecisionFor(int basin) => Connections.ContainsKey(basin) ? "accepted" :
        Failure ?? (rejected.TryGetValue(basin, out string reason) ? reason : "below runoff threshold or no extracted inlet");

    internal float NearestReceiverDistance(Vector3 terminal, Vector3[] directions, float radius) =>
        receiverApproaches.Count == 0 ? float.NaN : receiverApproaches.Min(r =>
            (directions[r.Key] - terminal).magnitude * radius + r.Value);

    private GeodesicTinSedimentCorridors(GeodesicDrainageDelivery delivery) { baseline = delivery; }

    private sealed class Receiver
    {
        internal int Network = -1;
        internal TinPoint? Coast;
        internal float ApproachLength;
    }

    private sealed class Inlets
    {
        internal readonly List<int> Networks = new List<int>();
        internal readonly List<int> Lakes = new List<int>();
        internal bool DifferentTerminal;
    }

    internal static GeodesicTinSedimentCorridors Build(GeodesicTinHydrology tin, IGeodesicHydrologyTopology graph,
        GeodesicTinBasins basins, GeodesicTinLakeGeometry lakes, GeodesicTinLakeRouting routing,
        GeodesicDrainageDelivery delivery, Func<TinPoint, int> oceanComponentAt, int primary, float radius, double minimumRunoff)
    {
        var plan = new GeodesicTinSedimentCorridors(delivery);
        if (oceanComponentAt == null || primary < 0)
        { plan.Failure = "primary ocean is unclassified"; return plan; }
        if (delivery.CycleNodes != 0 || delivery.DuplicateMemberships != 0 || delivery.InvalidMemberships != 0 ||
            Math.Abs(delivery.Residual) > delivery.Tolerance)
        { plan.Failure = "baseline drainage accounting is invalid"; return plan; }

        var closed = delivery.DisconnectedCatchments.Where(c => c.Terminal.Destination == GeodesicDrainageDelivery.Destination.InlandClosed &&
            c.Terminal.Basin != null).GroupBy(c => c.Terminal.Basin.Id)
            .Select(g => (Basin: g.First().Terminal.Basin, Runoff: g.Sum(c => c.Runoff))).ToArray();
        var inlets = CollectInlets(tin, basins, routing);
        var sources = closed.Where(c => c.Runoff >= minimumRunoff && inlets.ContainsKey(c.Basin.Id)).ToArray();
        plan.candidates = sources.Length;
        if (sources.Length == 0) return plan;

        int count = graph.CellCount;
        float sea = tin.BasinSeaLevel, level = sea + radius * LevelAboveSeaFraction;
        float cutLimit = radius * CutFraction, fillLimit = radius * FillFraction, lengthLimit = radius * LengthFraction;
        double volumeLimit = VolumeFraction * radius * radius * radius;
        double batchVolumeLimit = BatchVolumeFraction * radius * radius * radius;
        var lakeFacets = new HashSet<int>(lakes.TerrainPoints.Select(p => p.Triangle));
        var lakeVertices = new bool[count];
        foreach (int facet in lakeFacets)
            for (int k = 0; k < 3; k++) lakeVertices[tin.BasinVertex(facet, k)] = true;

        var protectedVertices = (bool[])lakeVertices.Clone();
        var receivers = new Receiver[count];
        float Distance(Vector3 a, Vector3 b) => (a - b).magnitude * (tin.BasinOnSphere ? radius : 1f);
        for (int t = 0; t < tin.TriangleCount; t++)
        {
            if (!tin.BasinFacetIntersectsOcean(t)) continue;
            for (int k = 0; k < 3; k++) protectedVertices[tin.BasinVertex(t, k)] = true;
            if (lakeFacets.Contains(t) || oceanComponentAt(tin.Centroid(t)) != primary) continue;
            for (int k = 0; k < 3; k++)
            {
                int v = tin.BasinVertex(t, k);
                if (lakeVertices[v] || tin.VisibleHeight[v] <= sea || tin.VisibleHeight[v] > level) continue;
                var dry = tin.BasinPoint(t, v);
                for (int j = 0; j < 3; j++)
                {
                    if (!tin.TryCoast(dry, tin.BasinPoint(t, tin.BasinVertex(t, j)), out var coast)) continue;
                    float distance = Distance(graph.CellDirections[v], tin.Direction(coast));
                    if (receivers[v] == null || distance < receivers[v].ApproachLength)
                        receivers[v] = new Receiver { Coast = coast, ApproachLength = distance };
                }
            }
        }

        for (int network = 0; network < tin.Networks.Count; network++)
        {
            if (delivery.NetworkOutcome(network).OceanComponent != primary) continue;
            var samples = routing != null ? routing.RiverReaches[network].Samples : tin.Networks[network].Samples;
            if (samples.Length < 2 || tin.Direction(samples[0]).Equals(tin.Direction(samples[samples.Length - 1]))) continue;
            foreach (var point in samples)
            {
                int corner = point.A == 1 ? 0 : point.B == 1 ? 1 : point.C == 1 ? 2 : -1;
                if (corner < 0) continue;
                int v = tin.BasinVertex(point.Triangle, corner);
                if (!protectedVertices[v] && receivers[v] == null && tin.VisibleHeight[v] >= sea && tin.VisibleHeight[v] <= level)
                    receivers[v] = new Receiver { Network = network };
            }
        }

        var penalties = new float[count];
        var widthLimited = new bool[count];
        for (int v = 0; v < count; v++)
        {
            float delta = tin.VisibleHeight[v] - level;
            float fraction = delta > 0 ? delta / cutLimit : -delta / fillLimit;
            if (protectedVertices[v] || fraction > 1) { penalties[v] = float.PositiveInfinity; continue; }
            for (int k = 0; k < graph.NeighborCounts[v]; k++)
                if (2f * Distance(graph.CellDirections[v], graph.CellDirections[graph.Neighbors6[v * 6 + k]]) > radius * WidthFraction)
                    widthLimited[v] = true;
            penalties[v] = widthLimited[v] ? float.PositiveInfinity : 1f + 5f * fraction * fraction;
        }

        // One reverse search from confirmed receivers. Cost penalizes length and the
        // required height edits, with no constraint on the original direction of slope.
        var parent = new int[count]; Array.Fill(parent, -1);
        var cost = new float[count]; Array.Fill(cost, float.PositiveInfinity);
        var length = new float[count];
        var terminalVertices = new HashSet<int>(closed.Select(c => c.Basin.Id));
        var frontier = new GeodesicInlandUplift.DistanceHeap();
        for (int v = 0; v < count; v++)
        {
            if (receivers[v] == null || terminalVertices.Contains(v)) continue;
            plan.receiverApproaches.Add(v, receivers[v].ApproachLength);
            cost[v] = length[v] = receivers[v].ApproachLength;
            frontier.Push(v, cost[v]);
        }
        while (frontier.TryPop(out int v, out float currentCost))
        {
            if (currentCost != cost[v] || terminalVertices.Contains(v)) continue;
            for (int k = 0; k < graph.NeighborCounts[v]; k++)
            {
                int next = graph.Neighbors6[v * 6 + k];
                if (receivers[next] != null || float.IsPositiveInfinity(penalties[next])) continue;
                float distance = Distance(graph.CellDirections[v], graph.CellDirections[next]);
                float nextLength = length[v] + distance, nextCost = currentCost + distance * penalties[next];
                if (nextLength > lengthLimit || nextCost >= cost[next]) continue;
                cost[next] = nextCost; length[next] = nextLength; parent[next] = v;
                frontier.Push(next, nextCost);
            }
        }

        var occupiedFacets = new HashSet<int>();
        var occupiedReceivers = new HashSet<int>();
        foreach (var source in sources.OrderByDescending(c => c.Runoff).ThenBy(c => length[c.Basin.Id]).ThenBy(c => c.Basin.Id))
        {
            int start = source.Basin.Id;
            var inlet = inlets[start];
            if (inlet.DifferentTerminal) { Reject("inlets end at different points in the basin"); continue; }
            if (protectedVertices[start]) { Reject("protected lake or ocean terrain at source"); continue; }
            if (widthLimited[start]) { Reject("terrain mesh exceeds the corridor width limit"); continue; }
            if (float.IsPositiveInfinity(penalties[start])) { Reject("source exceeds the cut or fill limit"); continue; }
            if (parent[start] < 0) { Reject("no receiver within terrain and length limits"); continue; }
            if (plan.connections.Count >= MaximumConnections) { Reject("connection budget reached"); continue; }

            var path = new List<int> { start };
            while (parent[path[path.Count - 1]] >= 0) path.Add(parent[path[path.Count - 1]]);
            int target = path[path.Count - 1];
            var receiver = receivers[target];
            var profile = SurfaceProfile(path, tin.VisibleHeight, graph, sea, level, radius, tin.BasinOnSphere);
            var facets = new HashSet<int>();
            double volume = 0;
            bool excessiveCut = false;
            for (int j = 0; j < path.Count - 1; j++)
            {
                int v = path[j];
                float delta = profile[j] - tin.VisibleHeight[v];
                excessiveCut |= -delta > cutLimit;
                for (int i = tin.BasinIncidentStart(v); i < tin.BasinIncidentEnd(v); i++)
                {
                    int t = tin.BasinIncidentTriangle(i);
                    volume += Math.Abs(delta) * tin.LocalArea[t] / 3d;
                    facets.Add(t);
                }
            }
            if (excessiveCut) { Reject("lowland profile exceeds the cut limit"); continue; }
            if (occupiedReceivers.Contains(target) || facets.Overlaps(occupiedFacets))
            { Reject("overlaps an accepted corridor or receiver"); continue; }
            if (volume > volumeLimit || plan.volume + volume > batchVolumeLimit)
            { Reject("terrain volume budget exceeded"); continue; }

            double area = facets.Sum(t => tin.LocalArea[t]);
            var connection = new Connection(start, receiver.Network, receiver.Coast, path.ToArray(), inlet.Networks.ToArray(),
                inlet.Lakes.ToArray(), source.Runoff, length[start], area, volume);
            for (int j = 0; j < path.Count - 1; j++)
                if (profile[j] != tin.VisibleHeight[path[j]]) plan.elevations.Add(path[j], profile[j]);
            plan.connections.Add(start, connection);
            occupiedFacets.UnionWith(facets);
            occupiedReceivers.Add(target);
            plan.area += area; plan.volume += volume; plan.runoff += source.Runoff;
            plan.decisions.Add(FormattableString.Invariant($"basin={start} accepted receiverNetwork={receiver.Network} coastalFacet={receiver.Coast?.Triangle ?? -1} length={length[start]:R} area={area:R} volume={volume:R} runoff={source.Runoff:R} inletNetworks={inlet.Networks.Count} inletLakes={inlet.Lakes.Count}"));

            void Reject(string reason)
            {
                plan.rejected.Add(start, reason);
                plan.decisions.Add(FormattableString.Invariant($"basin={start} runoff={source.Runoff:R} rejected={reason}"));
            }
        }
        return plan;
    }

    internal static float[] SurfaceProfile(IReadOnlyList<int> path, float[] original, IGeodesicHydrologyTopology graph,
        float sea, float searchLevel, float radius, bool onSphere)
    {
        var heights = path.Select(v => original[v]).ToArray();
        var ordered = (float[])heights.Clone();
        Array.Sort(ordered);
        float valleyLevel = Mathf.Max(sea, Mathf.Min(searchLevel, heights[heights.Length - 1], ordered[ordered.Length / 2]));
        var center = new HashSet<int>(path);
        var banks = new List<float>(6);
        for (int i = 1; i < path.Count - 1; i++)
        {
            int v = path[i];
            float maxFill = radius * SurfaceFillFraction;
            banks.Clear();
            for (int slot = 0; slot < graph.NeighborCounts[v]; slot++)
            {
                int bank = graph.Neighbors6[v * 6 + slot];
                if (center.Contains(bank)) continue;
                float width = (graph.CellDirections[v] - graph.CellDirections[bank]).magnitude * (onSphere ? radius : 1f);
                maxFill = Mathf.Min(maxFill, BankFillRatio * width);
                banks.Add(original[bank]);
            }
            banks.Sort();
            float bankLevel = banks.Count == 0 ? original[v] : banks[(banks.Count - 1) / 2];
            float upper = Mathf.Max(original[v], Mathf.Min(bankLevel, original[v] + maxFill));
            float target = Mathf.Min(valleyLevel, upper);
            // Both junctions keep their original elevations. Over the adjacent edge,
            // blend the edit into the original lowland rather than add a step.
            float blend = Mathf.Min(1f, .5f * Mathf.Min(i, path.Count - 1 - i));
            heights[i] = Mathf.Lerp(original[v], target, blend);
        }
        return heights;
    }

    private static Dictionary<int, Inlets> CollectInlets(GeodesicTinHydrology tin, GeodesicTinBasins basins, GeodesicTinLakeRouting routing)
    {
        var result = new Dictionary<int, Inlets>();
        void Add(int id, TinTermination termination, TinPoint[] samples, bool lake)
        {
            if (samples.Length < 2 || !GeodesicOceanDrainagePlan.Closed(termination)) return;
            var end = samples[samples.Length - 1];
            var basin = basins.Find(end);
            if (basin == null) return;
            if (!result.TryGetValue(basin.Id, out var inlets)) result.Add(basin.Id, inlets = new Inlets());
            inlets.DifferentTerminal |= (tin.Direction(end) - tin.Direction(basin.Floor)).sqrMagnitude > 1e-12f;
            (lake ? inlets.Lakes : inlets.Networks).Add(id);
        }
        for (int i = 0; i < tin.Networks.Count; i++)
        {
            var adjusted = routing?.RiverReaches[i];
            if (adjusted != null && adjusted.ReceivingLakeId >= 0) continue;
            var raw = tin.Networks[i];
            Add(i, adjusted?.Termination ?? raw.Termination, adjusted?.Samples ?? raw.Samples, false);
        }
        if (routing != null)
            foreach (var lake in routing.Lakes)
                if (lake.DownstreamLakeId < 0 && lake.JoinNetwork < 0)
                    Add(lake.BasinId, lake.OutletTermination, lake.OutletPath, true);
        return result;
    }

    internal GeodesicDrainageDelivery Verify(GeodesicDrainageDelivery final)
    {
        if (connections.Count == 0) return baseline;
        double tolerance = baseline.Tolerance;
        if (final.CycleNodes == 0 && final.InvalidMemberships == 0 && final.DuplicateMemberships == 0 &&
            Math.Abs(final.Residual) <= tolerance && Math.Abs(final.TotalRunoff - baseline.TotalRunoff) <= tolerance &&
            Math.Abs(final.CorridorOcean - runoff) <= tolerance && Math.Abs(final.PrimaryOcean - baseline.PrimaryOcean - runoff) <= tolerance &&
            Math.Abs(baseline.InlandClosed - final.InlandClosed - runoff) <= tolerance) return final;

        Failure = "post-connection drainage accounting failed";
        connections.Clear(); elevations.Clear(); area = volume = runoff = 0;
        return baseline;
    }

    internal string Summary(GeodesicDrainageDelivery final, int visualGaps) => FormattableString.Invariant(
        $"[GeodesicTinSedimentCorridors] closedBasinsBefore={baseline.ClosedBasinCount} closedBasinsAfter={final.ClosedBasinCount} candidates={candidates} accepted={connections.Count} rejected={candidates - connections.Count} affectedArea={area:R} modificationVolume={volume:R} newlyOceanConnectedRunoff={runoff:R} primaryOceanBefore={baseline.PrimaryOcean:R} primaryOceanAfter={final.PrimaryOcean:R} residual={final.Residual:R} conservationPass={Math.Abs(final.Residual) <= final.Tolerance} cycleNodes={final.CycleNodes} unsupportedCrossings={visualGaps} failure={Failure ?? "none"}");
}
