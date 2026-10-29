using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

public sealed class TinLakeFlowNode
{
    public int BasinId { get; }
    public int DownstreamLakeId = -1, OutletReachId = -1;
    public TinBasinDescriptor Basin { get; internal set; }
    public TinPoint[] OutletPath { get; internal set; } = Array.Empty<TinPoint>();
    public TinTermination OutletTermination { get; internal set; }
    public int JoinNetwork { get; internal set; } = -1;
    public bool Terminal => OutletPath.Length < 2;
    public readonly HashSet<int> DirectParcels = new HashSet<int>();
    public readonly HashSet<int> ContributingParcels = new HashSet<int>();
    public readonly List<double> InletDischarges = new List<double>();
    public double Discharge { get; internal set; }
    public double StrongestInlet => InletDischarges.DefaultIfEmpty(0).Max();
    public TinLakeFlowNode(int basinId) { BasinId = basinId; }
}

public sealed class TinLakeAdjustedReach
{
    public int RawNetworkId, SourceTriangle, ReceivingLakeId = -1, JunctionNetwork = -1;
    public TinPoint[] Samples;
    public bool Changed;
    public double InletDischarge;
    public TinTermination Termination;
}

public readonly struct TinLakeEntry
{
    public readonly int BasinId, SegmentEnd;
    public readonly TinPoint Point;
    public TinLakeEntry(int id, int end, TinPoint point) { BasinId = id; SegmentEnd = end; Point = point; }
}

/// <summary>Lake nodes inserted into the active continuous field. Raw receivers, sources,
/// accumulation and networks stay intact. Discharge uses UNIQUE original centroid-parcel IDs;
/// cumulative inlet values are diagnostic only and are never added together.</summary>
public sealed class GeodesicTinLakeRouting
{
    private readonly GeodesicTinHydrology tin;
    private readonly Dictionary<int, List<TinLakeFlowNode>> wetFacets = new Dictionary<int, List<TinLakeFlowNode>>();
    private readonly Dictionary<int, TinLakeFlowNode> nodes;
    public IReadOnlyList<TinLakeFlowNode> Lakes { get; }
    public IReadOnlyList<TinLakeAdjustedReach> RiverReaches { get; private set; }
    public int DuplicateParcelSuppression { get; private set; }
    public int FlowConservationErrors { get; private set; }
    public double BuildMilliseconds { get; private set; }
    private readonly Dictionary<int, HashSet<int>> rawContributors = new Dictionary<int, HashSet<int>>();
    private readonly Dictionary<int, List<(int Start, HashSet<int> Parcels)>> addedToNetwork = new Dictionary<int, List<(int, HashSet<int>)>>();

    private GeodesicTinLakeRouting(GeodesicTinHydrology source, GeodesicTinBasins basins, GeodesicTinLakeGeometry geometry)
    {
        tin = source; nodes = new Dictionary<int, TinLakeFlowNode>();
        var descriptors = basins.Basins.ToDictionary(b => b.Id);
        foreach (var body in geometry.Bodies.OrderBy(b => b.BasinId))
        {
            var node = new TinLakeFlowNode(body.BasinId) { Basin = descriptors[body.BasinId] }; nodes.Add(node.BasinId, node);
            var seen = new HashSet<int>();
            for (int i = body.FirstVertex; i < body.FirstVertex + body.VertexCount; i++)
            {
                int t = geometry.TerrainPoints[i].Triangle; if (!seen.Add(t)) continue;
                if (!wetFacets.TryGetValue(t, out var list)) wetFacets.Add(t, list = new List<TinLakeFlowNode>());
                list.Add(node);
            }
        }
        Lakes = nodes.Values.OrderBy(n => n.BasinId).ToArray();
    }
    private static TinPoint Mix(TinPoint a, TinPoint b, double f) => new TinPoint(a.Triangle, a.A + (b.A - a.A) * f, a.B + (b.B - a.B) * f, a.C + (b.C - a.C) * f);
    private bool Inside(TinPoint p, int id) => wetFacets.TryGetValue(p.Triangle, out var owners) && owners.Any(n => n.BasinId == id && tin.Height(p) < n.Basin.SpillElevation);

    public bool FirstEntry(IReadOnlyList<TinPoint> path, out TinLakeEntry entry)
    {
        entry = default; if (path == null || path.Count == 0) return false;
        for (int i = 0; i < path.Count; i++)
        {
            TinPoint a = i == 0 ? path[0] : path[i - 1], b = path[i];
            if (a.Triangle != b.Triangle) continue; // exact zero-length facet handoff; next segment tests the new carrier
            if (!wetFacets.TryGetValue(a.Triangle, out var candidates)) continue;
            double ha = tin.Height(a), hb = tin.Height(b), best = double.PositiveInfinity; TinLakeFlowNode selected = null;
            foreach (var n in candidates)
            {
                double level = n.Basin.SpillElevation;
                if (hb >= level && ha >= level) continue; // touching a rim without entering is not an inlet
                double fraction = ha < level ? 0 : (level - ha) / (hb - ha);
                if (fraction < best || (fraction == best && n.BasinId < selected.BasinId)) { best = fraction; selected = n; }
            }
            if (selected != null) { entry = new TinLakeEntry(selected.BasinId, i, Mix(a, b, best)); return true; }
        }
        return false;
    }
    public static TinPoint[] ClipAtEntry(IReadOnlyList<TinPoint> path, TinLakeEntry entry)
    {
        var result = new List<TinPoint>();
        for (int i = 0; i < entry.SegmentEnd; i++) result.Add(path[i]);
        if (result.Count == 0 || !result[result.Count - 1].Equals(entry.Point)) result.Add(entry.Point);
        return result.ToArray();
    }
    public Vector3[] Directions(TinPoint[] path)
    {
        var output = new List<Vector3>();
        foreach (var p in path) { var d = tin.Direction(p); if (output.Count == 0 || !output[output.Count - 1].Equals(d)) output.Add(d); }
        return output.ToArray();
    }

    public static GeodesicTinLakeRouting Build(GeodesicTinHydrology tin, GeodesicTinBasins topology, GeodesicTinLakeGeometry geometry,
        double minimumVisibleDischarge = 0)
    {
        if (tin == null || topology == null || geometry == null) throw new ArgumentNullException();
        var watch = Stopwatch.StartNew(); var result = new GeodesicTinLakeRouting(tin, topology, geometry);
        var reaches = new List<TinLakeAdjustedReach>();
        for (int id = 0; id < tin.Networks.Count; id++)
        {
            var raw = tin.Networks[id];
            var reach = new TinLakeAdjustedReach { RawNetworkId = id, SourceTriangle = raw.SourceTriangle, Samples = raw.Samples, Termination = raw.Termination, JunctionNetwork = raw.JunctionNetwork };
            if (result.FirstEntry(raw.Samples, out var hit))
            {
                reach.Samples = ClipAtEntry(raw.Samples, hit); reach.Changed = true; reach.ReceivingLakeId = hit.BasinId; reach.JunctionNetwork = -1;
                reach.InletDischarge = tin.AccumulatedRunoff[hit.Point.Triangle];
                if (result.Directions(reach.Samples).Length >= 2) result.nodes[hit.BasinId].InletDischarges.Add(reach.InletDischarge);
            }
            reaches.Add(reach);
        }
        result.RiverReaches = reaches.AsReadOnly();
        if (result.Lakes.Count == 0) { result.BuildMilliseconds = watch.Elapsed.TotalMilliseconds; return result; }

        int RawDestination(int index)
        {
            var seen = new HashSet<int>();
            while (index >= 0 && index < reaches.Count && seen.Add(index))
            {
                var r = reaches[index]; if (r.ReceivingLakeId >= 0) return r.ReceivingLakeId; index = r.JunctionNetwork;
            }
            return -1;
        }
        var destinations = reaches.Select(r => RawDestination(r.RawNetworkId)).ToArray();
        var channelEnds = new Dictionary<int, List<(int Network, int Sample)>>();
        foreach (var r in reaches)
        for (int j = 1; j < r.Samples.Length; j++)
        {
            int t = r.Samples[j].Triangle; if (r.Samples[j - 1].Triangle != t) continue;
            if (!channelEnds.TryGetValue(t, out var list)) channelEnds.Add(t, list = new List<(int, int)>());
            list.Add((r.RawNetworkId, j));
        }
        // Outlet geometry is traced with the SAME continuous field. Only its initial saddle
        // descent is restricted to the side outside its own wet polygon; there is no nudge.
        foreach (var node in result.Lakes)
        {
            var basin = node.Basin;
            if (!basin.HasEscape) continue;
            var spill = basin.Spill.Value;
            bool Departure(TinPoint a, TinPoint b) => !result.Inside(Mix(a, b, .5), basin.Id);
            var path = tin.TraceLakeOutlet(spill, Departure, out var termination);
            if (result.FirstEntry(path, out var hit))
            {
                if (hit.BasinId == node.BasinId) throw new InvalidOperationException("Lake outlet reentered its own water body.");
                path = ClipAtEntry(path, hit); node.DownstreamLakeId = hit.BasinId;
            }
            // Reuse an existing dry channel by an exact same-facet, validated join. Its
            // downstream lake destination is followed through the existing raw junctions.
            bool joined = false;
            for (int i = 1; i < path.Length && !joined; i++)
            {
                if (path[i - 1].Triangle != path[i].Triangle) continue;
                if (!channelEnds.TryGetValue(path[i].Triangle, out var ends)) continue;
                foreach (var end in ends)
                {
                    var r = reaches[end.Network]; int j = end.Sample;
                    if (destinations[r.RawNetworkId] == node.BasinId) continue;
                        var target = r.Samples[j];
                        if (r.Samples[j - 1].Triangle != target.Triangle || target.Triangle != path[i].Triangle || tin.Height(target) > tin.Height(path[i - 1])) continue;
                        var candidate = path.Take(i).Concat(new[] { target }).ToArray();
                        if (!tin.ValidateLakePath(candidate.Concat(r.Samples.Skip(j + 1)).ToArray()) || result.FirstEntry(candidate, out _)) continue;
                        path = candidate; node.JoinNetwork = r.RawNetworkId; termination = TinTermination.Confluence;
                        node.DownstreamLakeId = destinations[r.RawNetworkId]; joined = true;
                        break;
                }
            }
            node.OutletPath = path; node.OutletTermination = termination;
            if (result.Directions(path).Length < 2) node.OutletPath = Array.Empty<TinPoint>();
        }

        // Retain ancestry only for visible raw-channel facets, so downstream width can union
        // an outlet's contribution with the existing trunk without adding cumulative flow twice.
        foreach (var r in reaches)
        foreach (var p in r.Samples) if (!result.rawContributors.ContainsKey(p.Triangle)) result.rawContributors.Add(p.Triangle, new HashSet<int>());
        tin.VisitRunoffParcels((id, amount, path) =>
        {
            foreach (var p in path) if (result.rawContributors.TryGetValue(p.Triangle, out var contributors)) contributors.Add(id);
            if (result.FirstEntry(path, out var hit)) result.nodes[hit.BasinId].DirectParcels.Add(id);
        });
        result.DuplicateParcelSuppression = ResolveDischarge(result.Lakes, id => tin.ParcelRunoff(id));
        int outletId = reaches.Count;
        foreach (var node in result.Lakes)
        {
            if (!node.Terminal && node.Discharge > 0 && node.Discharge >= minimumVisibleDischarge) node.OutletReachId = outletId++;
            if (node.JoinNetwork < 0 || node.Discharge <= 0) continue;
            int network = node.JoinNetwork; TinPoint join = node.OutletPath.Last(); var seen = new HashSet<int>();
            while (network >= 0 && seen.Add(network))
            {
                var r = reaches[network];
                if (!result.addedToNetwork.TryGetValue(network, out var events)) result.addedToNetwork.Add(network, events = new List<(int, HashSet<int>)>());
                int start = Array.FindIndex(r.Samples, p => p.Equals(join)); if (start < 0) start = 0;
                events.Add((start, node.ContributingParcels));
                if (r.JunctionNetwork < 0) break;
                join = r.Samples.Last(); network = r.JunctionNetwork;
            }
        }
        foreach (var n in result.Lakes)
        {
            double authoritative = n.ContributingParcels.OrderBy(x => x).Sum(t => tin.ParcelRunoff(t));
            if (Math.Abs(n.Discharge - authoritative) > Math.Max(1e-10, authoritative * 1e-10)) result.FlowConservationErrors++;
        }
        double direct = result.Lakes.Sum(n => n.DirectParcels.Sum(id => tin.ParcelRunoff(id)));
        double deliveredOrHeld = result.Lakes.Where(n => n.DownstreamLakeId < 0).Sum(n => n.Discharge);
        if (Math.Abs(direct - deliveredOrHeld) > Math.Max(1e-9, direct * 1e-9)) result.FlowConservationErrors++;
        result.BuildMilliseconds = watch.Elapsed.TotalMilliseconds; return result;
    }

    /// <summary>Deterministic DAG transport of unique original parcels. Public for focused
    /// conservation tests, including future rendered terminal nodes with no downstream edge.</summary>
    public static int ResolveDischarge(IEnumerable<TinLakeFlowNode> source, Func<int, double> runoff)
    {
        var nodes = source.ToDictionary(n => n.BasinId); var indegree = nodes.Keys.ToDictionary(id => id, id => 0);
        foreach (var n in nodes.Values)
        {
            n.ContributingParcels.Clear(); n.ContributingParcels.UnionWith(n.DirectParcels);
            if (n.DownstreamLakeId >= 0) indegree[n.DownstreamLakeId]++;
        }
        var ready = new SortedSet<int>(indegree.Where(p => p.Value == 0).Select(p => p.Key)); int processed = 0, duplicates = 0;
        while (ready.Count > 0)
        {
            int id = ready.Min; ready.Remove(id); processed++; var n = nodes[id];
            n.Discharge = n.ContributingParcels.OrderBy(p => p).Sum(runoff);
            if (n.DownstreamLakeId < 0) continue;
            var downstream = nodes[n.DownstreamLakeId];
            foreach (int parcel in n.ContributingParcels) if (!downstream.ContributingParcels.Add(parcel)) duplicates++;
            if (--indegree[downstream.BasinId] == 0) ready.Add(downstream.BasinId);
        }
        if (processed != nodes.Count) throw new InvalidOperationException("Cycle in rendered lake routing; refusing nonconservative outlets.");
        return duplicates;
    }
    public double[] RawReachDischarge(int network)
    {
        var r = RiverReaches[network]; var output = new double[r.Samples.Length];
        addedToNetwork.TryGetValue(network, out var events);
        for (int i = 0; i < output.Length; i++)
        {
            int t = r.Samples[i].Triangle; output[i] = tin.AccumulatedRunoff[t];
            if (events == null) continue;
            var extra = new HashSet<int>(); foreach (var e in events) if (e.Start <= i) extra.UnionWith(e.Parcels);
            rawContributors.TryGetValue(t, out var raw);
            foreach (int id in extra.OrderBy(x => x)) if (raw == null || !raw.Contains(id)) output[i] += tin.ParcelRunoff(id);
        }
        return output;
    }
    public bool HasAddedDischarge(int network) => addedToNetwork.ContainsKey(network);
    public string Summary() => $"[GeodesicTinLakeRouting] active={Lakes.Count} receivingRivers={Lakes.Count(n => n.InletDischarges.Count > 0)} inlets={Lakes.Sum(n => n.InletDischarges.Count)} multipleInlets={Lakes.Count(n => n.InletDischarges.Count > 1)} outlets={Lakes.Count(n => n.OutletReachId >= 0)} terminalOrBlocked={Lakes.Count(n => n.Terminal)} cascades={Lakes.Count(n => n.DownstreamLakeId >= 0)} clipped={RiverReaches.Count(r => r.Changed)} duplicateParcelSuppression={DuplicateParcelSuppression} conservationErrors={FlowConservationErrors} buildMs={BuildMilliseconds:F2}";
    public IEnumerable<string> Details() => Lakes.Select(n => $"[GeodesicTinLakeRouting] basin={n.BasinId} inlets={n.InletDischarges.Count} inletValues=[{string.Join(",", n.InletDischarges.Select(x => x.ToString("R")))}] strongest={n.StrongestInlet:R} uniqueParcels={n.ContributingParcels.Count} directParcels={n.DirectParcels.Count} discharge={n.Discharge:R} spillTriangle={n.Basin.Spill?.Triangle} outlet={n.OutletReachId} downstreamLake={n.DownstreamLakeId} joinNetwork={n.JoinNetwork} result={n.OutletTermination} terminalOrBlocked={n.Terminal}");
}
