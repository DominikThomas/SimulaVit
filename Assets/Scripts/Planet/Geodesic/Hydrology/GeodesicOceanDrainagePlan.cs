using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Generation-only selection of witnessed incision proposals, not drainage receivers.
/// Every node owns disjoint original triangle parcels. Only paths rooted at proven ocean
/// evidence may be selected; the completed TIN still decides where water actually goes.</summary>
public static class GeodesicOceanDrainagePlan
{
    public const int Version = 1;
    public const int Ocean = -1, Unresolved = -2;
    public const int MaximumCandidates = 2048;

    public sealed class Catchment
    {
        public int Id;
        public TinBasinDescriptor Basin;
        public TinPoint Floor;
        public double Runoff, TinyNoiseRunoff;
        public readonly List<int> Parcels = new List<int>();
    }

    public sealed class Connection
    {
        public int Id, Downstream;
        public double LocalRunoff, Cost;
    }

    public sealed class Selection
    {
        public readonly List<int> Ordered = new List<int>();
        public readonly Dictionary<int, double> PotentialRunoff = new Dictionary<int, double>();
        public int Unrooted, Conflicts, BelowRunoff;
    }

    public static bool Closed(TinTermination termination) =>
        termination == TinTermination.TrueClosedBasin || termination == TinTermination.TinyNoiseDepression;

    public static bool ConfirmedOcean(GeodesicTinHydrology tin, TinParcelDestination destination) =>
        destination.Termination == TinTermination.OceanCoast && tin.IsRetainedOceanBoundary(destination.Point);

    public static bool TraceToOcean(GeodesicTinHydrology tin, TinPoint source)
    {
        var path = tin.TraceLakeOutlet(source, null, out var termination);
        return path.Length > 0 && ConfirmedOcean(tin, new TinParcelDestination(path[path.Length - 1], termination));
    }

    public static string FinalCatchmentSummary(IReadOnlyList<Catchment> initial, GeodesicTinHydrology final,
        GeodesicDrainageDelivery delivery, double minimumRunoff)
    {
        int high = 0, full = 0, partial = 0, noLongerRunoff = 0; double input = 0, currentInput = 0, ocean = 0;
        foreach (var c in initial)
        {
            if (c.Runoff < minimumRunoff) continue;
            high++; input += c.Runoff; bool all = true, any = false;
            foreach (int id in c.Parcels)
            {
                double amount = final.ParcelRunoff(id); currentInput += amount;
                // A formerly dry parcel can become submerged along a coastal incision.
                // Do not credit disappearance from the land input as delivered runoff.
                if (amount <= 0) { noLongerRunoff++; all = false; continue; }
                bool connected = delivery.IsConfirmedOceanParcel(id); all &= connected; any |= connected;
                if (connected) ocean += amount;
            }
            if (all) full++; else if (any) partial++;
        }
        return FormattableString.Invariant($"[GeodesicOceanDrainagePlan] final=True initialHighRunoffCatchments={high} fullyOceanConnected={full} partlyOceanConnected={partial} initialCatchmentRunoff={input:R} finalComparableRunoff={currentInput:R} noLongerRunoffParcels={noLongerRunoff} confirmedOceanRunoff={ocean:R} minimumIndependentRunoff={minimumRunoff:R} includesLakeOverflow=True identities=initial-parcel-ids-not-final-basin-ids");
    }

    /// <summary>Use the existing accumulation observations; never sum cumulative river flows
    /// or infer catchment ownership from overlapping lower-star basin footprints.</summary>
    public static IReadOnlyList<Catchment> CaptureCatchments(GeodesicTinHydrology tin, GeodesicTinBasins basins)
    {
        if (tin.ParcelDestinations == null)
            throw new InvalidOperationException("Ocean-directed carving requires original parcel destinations from accumulation.");
        var byFloor = new SortedDictionary<int, Catchment>();
        // Resolve repeated endpoints once. Most parcels share an exact basin-floor vertex.
        var matched = new Dictionary<TinPoint, TinBasinDescriptor>();
        for (int t = 0; t < tin.TriangleCount; t++)
        {
            double amount = tin.ParcelRunoff(t); var destination = tin.ParcelDestinations[t];
            if (amount <= 0 || !Closed(destination.Termination)) continue;
            if (!matched.TryGetValue(destination.Point, out var basin))
            { basin = basins.Find(destination.Point); matched.Add(destination.Point, basin); }
            if (basin == null) continue;
            // Never snap an arbitrary failed/flat interior point to a basin floor.
            var p = destination.Point;
            int vertex = p.A == 1 ? tin.BasinVertex(p.Triangle, 0) : p.B == 1 ? tin.BasinVertex(p.Triangle, 1) :
                p.C == 1 ? tin.BasinVertex(p.Triangle, 2) : -1;
            if (vertex != basin.Id) continue;
            if (!byFloor.TryGetValue(basin.Id, out var catchment))
            { catchment = new Catchment { Id = basin.Id, Basin = basin, Floor = basin.Floor }; byFloor.Add(basin.Id, catchment); }
            catchment.Parcels.Add(t); catchment.Runoff += amount;
            if (destination.Termination == TinTermination.TinyNoiseDepression) catchment.TinyNoiseRunoff += amount;
        }
        return byFloor.Values.ToArray();
    }

    /// <summary>Reverse traversal from ocean roots excludes cycles/unknown targets. Aggregate
    /// disjoint upstream runoff for priority, then admit downstream-first compatible cuts.
    /// Potential runoff is a planning score, never a confirmed delivery measurement.</summary>
    public static Selection Select(IEnumerable<Connection> source, double minimumRunoff,
        Func<int, IReadOnlyList<int>, bool> compatible)
    {
        var nodes = source.OrderBy(n => n.Id).ToDictionary(n => n.Id);
        if (nodes.Count > MaximumCandidates) throw new ArgumentException("Connection planning budget exceeded.");
        foreach (var n in nodes.Values)
            if (n.Id < 0 || n.LocalRunoff < 0 || double.IsNaN(n.LocalRunoff) || double.IsInfinity(n.LocalRunoff) ||
                n.Cost <= 0 || double.IsNaN(n.Cost) || double.IsInfinity(n.Cost)) throw new ArgumentException("Invalid connection cost or runoff.");
        var children = nodes.Keys.ToDictionary(id => id, _ => new List<int>());
        foreach (var n in nodes.Values) if (children.TryGetValue(n.Downstream, out var list)) list.Add(n.Id);
        var roots = nodes.Values.Where(n => n.Downstream == Ocean).Select(n => n.Id).OrderBy(id => id).ToArray();
        var queue = new Queue<int>(roots); var order = new List<int>(); var result = new Selection();
        while (queue.Count > 0)
        {
            int id = queue.Dequeue(); order.Add(id); result.PotentialRunoff.Add(id, nodes[id].LocalRunoff);
            foreach (int child in children[id]) queue.Enqueue(child);
        }
        result.Unrooted = nodes.Count - order.Count;
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var n = nodes[order[i]];
            if (n.Downstream >= 0) result.PotentialRunoff[n.Downstream] += result.PotentialRunoff[n.Id];
        }
        var ready = new SortedSet<int>(Comparer<int>.Create((a, b) =>
        {
            int c = (result.PotentialRunoff[b] / nodes[b].Cost).CompareTo(result.PotentialRunoff[a] / nodes[a].Cost);
            return c != 0 ? c : a.CompareTo(b);
        }));
        foreach (int root in roots) ready.Add(root);
        while (ready.Count > 0)
        {
            int id = ready.Min; ready.Remove(id);
            if (result.PotentialRunoff[id] < minimumRunoff) { result.BelowRunoff++; continue; }
            if (!compatible(id, result.Ordered)) { result.Conflicts++; continue; }
            result.Ordered.Add(id);
            foreach (int child in children[id]) ready.Add(child);
        }
        return result;
    }
}
