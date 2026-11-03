using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

public sealed class TinLakeFlowNode
{
    public int BasinId { get; }
    public int DownstreamLakeId = -1, OutletReachId = -1;
    public TinBasinDescriptor Basin { get; internal set; }
    public TinPoint[] OutletPath { get; internal set; } = Array.Empty<TinPoint>();
    public TinTermination OutletTermination { get; internal set; }
    public int JoinNetwork { get; internal set; } = -1;
    // A shared shoreline can transfer discharge without a dry, visible outlet segment.
    public bool Terminal => DownstreamLakeId < 0 && !ExitsAtSpill && OutletPath.Length < 2;
    public TinPoint? DownstreamSample { get; internal set; }
    public bool ExitsAtSpill { get; internal set; }
    public readonly HashSet<int> DirectParcels = new HashSet<int>();
    public readonly HashSet<int> ContributingParcels = new HashSet<int>();
    public readonly List<double> InletDischarges = new List<double>();
    public double Discharge { get; internal set; }
    internal string CycleContext;
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
    private readonly Dictionary<int, TinBasinDescriptor> descriptors;
    public IReadOnlyList<TinLakeFlowNode> Lakes { get; }
    public IReadOnlyList<TinLakeAdjustedReach> RiverReaches { get; private set; }
    public int DuplicateParcelSuppression { get; private set; }
    public int FlowConservationErrors { get; private set; }
    public int SharedSaddleLakes { get; private set; }
    public double ConservationInput { get; private set; }
    public double ConservationOutput { get; private set; }
    public double BuildMilliseconds { get; private set; }
    private readonly Dictionary<int, HashSet<int>> rawContributors = new Dictionary<int, HashSet<int>>();
    private readonly Dictionary<int, List<(int Start, HashSet<int> Parcels)>> addedToNetwork = new Dictionary<int, List<(int, HashSet<int>)>>();

    private GeodesicTinLakeRouting(GeodesicTinHydrology source, GeodesicTinBasins basins, GeodesicTinLakeGeometry geometry)
    {
        tin = source; nodes = new Dictionary<int, TinLakeFlowNode>();
        descriptors = basins.Basins.ToDictionary(b => b.Id);
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
        SharedSaddleLakes = Lakes.Where(n => n.Basin.HasEscape).GroupBy(n => SpillVertex(n.Basin))
            .Where(g => g.Key >= 0 && g.Count() > 1).Sum(g => g.Count());
    }
    private static TinPoint Mix(TinPoint a, TinPoint b, double f) => new TinPoint(a.Triangle, a.A + (b.A - a.A) * f, a.B + (b.B - a.B) * f, a.C + (b.C - a.C) * f);
    private bool Inside(TinPoint p, int id) => wetFacets.TryGetValue(p.Triangle, out var owners) && owners.Any(n => n.BasinId == id && tin.Height(p) < n.Basin.SpillElevation);

    private int SpillVertex(TinBasinDescriptor basin)
    {
        if (!basin.Spill.HasValue) return -1;
        var p = basin.Spill.Value;
        return p.A == 1 ? tin.BasinVertex(p.Triangle, 0) : p.B == 1 ? tin.BasinVertex(p.Triangle, 1) :
            p.C == 1 ? tin.BasinVertex(p.Triangle, 2) : -1;
    }

    private static TinPoint? FirstDownstreamSample(IReadOnlyList<TinPoint> path)
    {
        for (int i = 1; i < path.Count; i++)
            if (path[i - 1].Triangle == path[i].Triangle && !path[i - 1].Equals(path[i]))
                return Mix(path[i - 1], path[i], .5);
        return null;
    }

    private TinPoint[] TraceSpillOutlet(TinLakeFlowNode node, out TinTermination termination)
    {
        var basin = node.Basin; var spill = basin.Spill.Value;
        int facet = basin.DownstreamTriangle, vertex = basin.DownstreamVertex;
        bool hasWitness = facet >= 0 && facet < tin.TriangleCount && vertex >= 0 && vertex < tin.BasinVertexCount;
        if (hasWitness)
        {
            var start = tin.TransferLakePoint(spill, facet); var witness = tin.BasinPoint(facet, vertex);
            hasWitness = tin.BasinFaceValid(facet) && Math.Abs(start.A + start.B + start.C - 1) <= 1e-9 &&
                witness.A + witness.B + witness.C == 1 && vertex != SpillVertex(basin) &&
                Math.Abs(tin.Height(start) - basin.SpillElevation) <= tin.Epsilon && tin.Height(witness) <= basin.SpillElevation;
        }
        // The lower-star sweep recorded this facet on the winning side BEFORE merging
        // the source basin. Other incident facets may descend into upstream sibling lakes
        // sharing the saddle. Being outside this lake alone does not make them downstream.
        // Keep the existing continuous tracer; restrict only its first descent cone.
        bool Departure(TinPoint a, TinPoint b) => hasWitness && a.Triangle == facet &&
            !Inside(Mix(a, b, .5), basin.Id);
        var path = tin.TraceLakeOutlet(spill, Departure, out termination);
        node.OutletTermination = termination;
        node.DownstreamSample = FirstDownstreamSample(path);
        node.ExitsAtSpill = !node.DownstreamSample.HasValue && termination == TinTermination.OceanCoast;
        if (node.ExitsAtSpill) return path; // An exact ocean/boundary spill needs no invented dry segment.
        if (!hasWitness || !node.DownstreamSample.HasValue || Inside(node.DownstreamSample.Value, basin.Id))
        {
            string report = "[GeodesicTinLakeRouting] unresolved overflowing outlet; refusing invented terminal storage. " +
                $"validSpillWitness={hasWitness} traceTermination={termination} " + DescribeOutlet(node);
            UnityEngine.Debug.LogError(report);
            throw new InvalidOperationException(report);
        }
        return path;
    }

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
        // The first departure uses the recorded downstream spill facet, not whichever
        // adjacent depression has the steepest descent. There is no shoreline/height nudge.
        foreach (var node in result.Lakes)
        {
            var basin = node.Basin;
            if (!basin.HasEscape) continue;
            var path = result.TraceSpillOutlet(node, out var termination);
            if (result.FirstEntry(path, out var hit))
            {
                path = ClipAtEntry(path, hit); node.DownstreamLakeId = hit.BasinId;
            }
            // Reuse an existing dry channel by an exact same-facet, validated join. Its
            // downstream lake destination is followed through the existing raw junctions.
            bool joined = false;
            for (int i = 1; i < path.Length && !joined && node.DownstreamLakeId != node.BasinId; i++)
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
                        // A dry-channel join must retain an actual departure from the
                        // saddle; a coincident rim point cannot override its witnessed side.
                        if (result.Directions(candidate).Length < 2) continue;
                        if (!tin.ValidateLakePath(candidate.Concat(r.Samples.Skip(j + 1)).ToArray()) || result.FirstEntry(candidate, out _)) continue;
                        path = candidate; node.JoinNetwork = r.RawNetworkId; termination = TinTermination.Confluence;
                        node.DownstreamSample = FirstDownstreamSample(path);
                        node.DownstreamLakeId = destinations[r.RawNetworkId]; joined = true;
                        break;
                }
            }
            node.OutletPath = path; node.OutletTermination = termination;
            // Retain the proven downstream sample and logical lake edge even when exact
            // shoreline clipping leaves no dry ribbon between equal-level touching lakes.
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
        foreach (var node in result.Lakes) node.CycleContext = result.DescribeOutlet(node);
        result.DuplicateParcelSuppression = ResolveDischarge(result.Lakes, id => tin.ParcelRunoff(id));
        int outletId = reaches.Count;
        foreach (var node in result.Lakes)
        {
            if (node.OutletPath.Length >= 2 && node.Discharge > 0 && node.Discharge >= minimumVisibleDischarge) node.OutletReachId = outletId++;
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
        result.ConservationInput = direct; result.ConservationOutput = deliveredOrHeld;
        if (result.FlowConservationErrors > 0 || result.DuplicateParcelSuppression > 0)
            throw new InvalidOperationException("Rendered lake routing did not conserve unique runoff parcels. " + result.Summary());
        result.BuildMilliseconds = watch.Elapsed.TotalMilliseconds; return result;
    }

    /// <summary>Deterministic DAG transport of unique original parcels. Public for focused
    /// conservation tests, including future rendered terminal nodes with no downstream edge.</summary>
    public static int ResolveDischarge(IEnumerable<TinLakeFlowNode> source, Func<int, double> runoff, Action<string> cycleLog = null)
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
        if (processed != nodes.Count)
        {
            string report = DescribeCycles(nodes);
            if (cycleLog != null) cycleLog(report); else UnityEngine.Debug.LogError(report);
            throw new InvalidOperationException("Cycle in rendered lake routing; refusing nonconservative outlets.\n" + report);
        }
        return duplicates;
    }

    private bool IsAncestor(int ancestor, int child)
    {
        var seen = new HashSet<int>();
        while (descriptors.TryGetValue(child, out var basin) && seen.Add(child))
        { child = basin.ParentBasinId; if (child == ancestor) return true; }
        return false;
    }

    private string DescribeOutlet(TinLakeFlowNode node)
    {
        var b = node.Basin;
        string Point(TinPoint p) => FormattableString.Invariant($"facet={p.Triangle} bary=({p.A:R},{p.B:R},{p.C:R}) height={tin.Height(p):R}");
        int spillVertex = SpillVertex(b);
        TinPoint? downstream = node.DownstreamSample;
        var owners = downstream.HasValue ? Lakes.Where(n => Inside(downstream.Value, n.BasinId)).Select(n => n.BasinId).ToArray() : Array.Empty<int>();
        string Classify(int id) => id == b.Id ? "same" : IsAncestor(id, b.Id) ? "ancestor" : IsAncestor(b.Id, id) ? "descendant" : "unrelated";
        var suppressed = descriptors.Values.Where(d => d.QualifiesForVisibleLake && !nodes.ContainsKey(d.Id) &&
            IsAncestor(b.Id, d.Id) && b.SpillElevation >= d.SpillElevation).Select(d => d.Id).OrderBy(id => id);
        string target = node.DownstreamLakeId >= 0 ? "rendered-lake" : node.JoinNetwork >= 0 ? "ordinary-TIN-channel" : node.OutletTermination.ToString();
        return FormattableString.Invariant($"lake={node.BasinId} basin={b.Id} parent={b.ParentBasinId} floor={b.FloorElevation:R} spill={b.SpillElevation:R} ") +
            $"spillVertex={spillVertex} spillPoint=[{(b.Spill.HasValue ? Point(b.Spill.Value) : "none")}] " +
            $"outletTargetType={target} targetLake={node.DownstreamLakeId} targetBasin={node.DownstreamLakeId} joinNetwork={node.JoinNetwork} " +
            $"topologyDownstreamFacet={b.DownstreamTriangle} topologyDownstreamVertex={b.DownstreamVertex} downstreamSample=[{(downstream.HasValue ? Point(downstream.Value) : "none")}] " +
            $"insideSame={owners.Contains(b.Id)} insideAncestor={owners.Any(id => IsAncestor(id, b.Id))} insideDescendant={owners.Any(id => IsAncestor(b.Id, id))} " +
            $"insideUnrelated={owners.Any(id => Classify(id) == "unrelated")} owners=[{string.Join(",", owners.Select(id => id + ":" + Classify(id)))}] " +
            $"survivedHierarchySuppression=True suppressedSourceBasins=[{string.Join(",", suppressed)}] " +
            $"topologyTerminal={b.Terminal} terminalOrBlocked={node.Terminal} exitsAtSpill={node.ExitsAtSpill} visibleOutletSamples={node.OutletPath.Length}";
    }

    private static string DescribeCycles(Dictionary<int, TinLakeFlowNode> graph)
    {
        var report = new StringBuilder("[GeodesicTinLakeRouting] cycle failure (rendered lake IDs are basin IDs)");
        var visited = new HashSet<int>();
        foreach (int start in graph.Keys.OrderBy(id => id))
        {
            var positions = new Dictionary<int, int>(); var path = new List<int>(); int current = start;
            while (graph.ContainsKey(current) && !visited.Contains(current) && !positions.ContainsKey(current))
            { positions.Add(current, path.Count); path.Add(current); current = graph[current].DownstreamLakeId; }
            if (positions.TryGetValue(current, out int first))
            {
                var cycle = path.Skip(first).ToArray();
                string Label(int id) => $"Lake {id} / Basin {graph[id].Basin?.Id ?? id}";
                report.AppendLine().Append("type=").Append(cycle.Length == 1 ? "self-cycle" : cycle.Length == 2 ? "2-node-cycle" : "longer-cycle")
                    .Append(" length=").Append(cycle.Length).Append(" ").Append(string.Join(" -> ", cycle.Concat(new[] { current }).Select(Label)));
                foreach (int id in cycle) report.AppendLine().Append(graph[id].CycleContext ?? $"lake={id} basin={id} targetLake={graph[id].DownstreamLakeId} topology=unavailable (synthetic node)");
            }
            visited.UnionWith(path);
        }
        return report.ToString();
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
    public string Summary() => $"[GeodesicTinLakeRouting] active={Lakes.Count} receivingRivers={Lakes.Count(n => n.InletDischarges.Count > 0)} inlets={Lakes.Sum(n => n.InletDischarges.Count)} multipleInlets={Lakes.Count(n => n.InletDischarges.Count > 1)} outlets={Lakes.Count(n => n.OutletReachId >= 0)} terminalOrBlocked={Lakes.Count(n => n.Terminal)} cascades={Lakes.Count(n => n.DownstreamLakeId >= 0)} clipped={RiverReaches.Count(r => r.Changed)} duplicateParcelSuppression={DuplicateParcelSuppression} conservationErrors={FlowConservationErrors} " +
        $"sharedSaddleLakes={SharedSaddleLakes} witnessedOutlets={Lakes.Count(n => n.DownstreamSample.HasValue)} edges={Lakes.Count(n => n.DownstreamLakeId >= 0)} selfLoops=0 cycles=0 conservationInput={ConservationInput:R} conservationOutput={ConservationOutput:R} buildMs={BuildMilliseconds:F2}";
    public IEnumerable<string> Details() => Lakes.Select(n => $"[GeodesicTinLakeRouting] basin={n.BasinId} inlets={n.InletDischarges.Count} inletValues=[{string.Join(",", n.InletDischarges.Select(x => x.ToString("R")))}] strongest={n.StrongestInlet:R} uniqueParcels={n.ContributingParcels.Count} directParcels={n.DirectParcels.Count} discharge={n.Discharge:R} spillTriangle={n.Basin.Spill?.Triangle} outlet={n.OutletReachId} downstreamLake={n.DownstreamLakeId} joinNetwork={n.JoinNetwork} result={n.OutletTermination} terminalOrBlocked={n.Terminal}");
}
