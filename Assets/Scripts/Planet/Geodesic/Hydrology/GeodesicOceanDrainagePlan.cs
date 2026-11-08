using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Generation-only selection of witnessed incision proposals, not drainage receivers.
/// Every node owns disjoint original triangle parcels. Only paths rooted at proven ocean
/// evidence may be selected; the completed TIN still decides where water actually goes.</summary>
public static class GeodesicOceanDrainagePlan
{
    public const int Version = 3;
    public const int Ocean = -1, Unresolved = -2;
    public const int MaximumCandidates = 2048;
    public const int MaximumChainEdges = 32;

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
        public int MissingDownstream, Cyclic, UnknownDestination, OverBudget;
        public readonly List<Chain> Chains = new List<Chain>();
        public readonly Dictionary<int, string> Rejections = new Dictionary<int, string>();
    }

    public sealed class Chain
    {
        public int SourceId;
        // Complete downstream-first path, including any already selected shared trunk.
        public int[] Edges;
        public double AddedCost, AddedRunoff;
    }

    /// <summary>Reserve the unchanged candidate budget for each seed's actual downstream
    /// dependencies before moving to the next seed. Missing/invalid witnesses stay missing.</summary>
    public static int[] ExpandDependencies(IEnumerable<int> seeds, Func<int, int> build)
    {
        var built = new HashSet<int>(); var order = new List<int>();
        foreach (int seed in seeds)
        {
            int id = seed; var seen = new HashSet<int>();
            for (int depth = 0; id >= 0 && depth < MaximumChainEdges && seen.Add(id); depth++)
            {
                if (!built.Contains(id))
                {
                    if (built.Count == MaximumCandidates) break;
                    built.Add(id); order.Add(id);
                }
                id = build(id); // Caller memoizes the physical proposal.
            }
        }
        return order.ToArray();
    }

    public static HashSet<int> CompleteChainEdges(IEnumerable<Chain> chains, Func<int, bool> verified) =>
        new HashSet<int>(chains.Where(c => c.Edges.All(verified)).SelectMany(c => c.Edges));

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

    public static string FinalChainSummary(IEnumerable<int> originallyClosedParcels, GeodesicTinHydrology final,
        GeodesicDrainageDelivery delivery)
    {
        double input = 0, direct = 0, viaLakes = 0, rawClosed = 0; int count = 0, submerged = 0;
        foreach (int id in originallyClosedParcels.Distinct().OrderBy(id => id))
        {
            count++; double amount = final.ParcelRunoff(id); input += amount;
            if (amount <= 0) { submerged++; continue; }
            if (delivery.IsOceanViaLakesParcel(id)) viaLakes += amount;
            else if (delivery.IsConfirmedOceanParcel(id)) direct += amount;
            else if (final.ParcelDestinations != null && Closed(final.ParcelDestinations[id].Termination)) rawClosed += amount;
        }
        return FormattableString.Invariant($"[GeodesicOceanDrainagePlan] finalChains=True uniqueOriginallyClosedParcels={count} finalComparableRunoff={input:R} newlyDeliveredThroughChains={direct + viaLakes:R} direct={direct:R} viaLakes={viaLakes:R} rawStillClosed={rawClosed:R} noLongerRunoffParcels={submerged} scope=unique-parcel-union-across-passes authority=final-drainage-delivery");
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

    // Compatibility wrapper for callers inspecting one edge at a time. Production admits
    // the whole package through SelectChains, so failed packages cannot leak partial cuts.
    public static Selection Select(IEnumerable<Connection> source, double minimumRunoff,
        Func<int, IReadOnlyList<int>, bool> compatible) => SelectChains(source, minimumRunoff, (package, selected) =>
        {
            var provisional = selected.ToList();
            foreach (int id in package) { if (!compatible(id, provisional)) return false; provisional.Add(id); }
            return true;
        });

    /// <summary>Rank complete packages by marginal independent runoff / combined new cost.
    /// Shared trunks are charged once. Compatibility and admission are atomic per package.</summary>
    public static Selection SelectChains(IEnumerable<Connection> source, double minimumRunoff,
        Func<IReadOnlyList<int>, IReadOnlyList<int>, bool> compatible)
    {
        var nodes = source.OrderBy(n => n.Id).ToDictionary(n => n.Id);
        if (nodes.Count > MaximumCandidates) throw new ArgumentException("Connection planning budget exceeded.");
        foreach (var n in nodes.Values)
            if (n.Id < 0 || n.LocalRunoff < 0 || double.IsNaN(n.LocalRunoff) || double.IsInfinity(n.LocalRunoff) ||
                n.Cost <= 0 || double.IsNaN(n.Cost) || double.IsInfinity(n.Cost)) throw new ArgumentException("Invalid connection cost or runoff.");
        var result = new Selection(); var plans = new List<Chain>();
        foreach (var n in nodes.Values.OrderBy(n => n.Id))
        {
            var path = new List<int>(); var seen = new HashSet<int>(); int id = n.Id; string failure = null;
            while (id != Ocean)
            {
                if (id < 0) { failure = "unknown-destination"; result.UnknownDestination++; break; }
                if (!seen.Add(id)) { failure = "cycle"; result.Cyclic++; break; }
                if (!nodes.TryGetValue(id, out var edge)) { failure = "missing-downstream"; result.MissingDownstream++; break; }
                if (path.Count == MaximumChainEdges) { failure = "chain-budget"; result.OverBudget++; break; }
                path.Add(id); id = edge.Downstream;
            }
            if (failure != null) { result.Rejections[n.Id] = failure; result.Unrooted++; continue; }
            path.Reverse(); plans.Add(new Chain { SourceId = n.Id, Edges = path.ToArray() });
            result.PotentialRunoff[n.Id] = n.LocalRunoff;
        }
        // Diagnostic upstream potential, separate from the marginal benefit used to admit
        // actual packages. Each source owns a disjoint original set of runoff parcels.
        foreach (var plan in plans.OrderBy(p => p.SourceId))
            foreach (int id in plan.Edges)
                if (id != plan.SourceId) result.PotentialRunoff[id] += nodes[plan.SourceId].LocalRunoff;
        var selected = new HashSet<int>();
        while (plans.Count > 0)
        {
            foreach (var p in plans)
            {
                p.AddedCost = p.Edges.Where(id => !selected.Contains(id)).Sum(id => nodes[id].Cost);
                p.AddedRunoff = p.Edges.Where(id => !selected.Contains(id)).Sum(id => nodes[id].LocalRunoff);
            }
            var best = plans.OrderByDescending(p => p.AddedCost > 0 ? p.AddedRunoff / p.AddedCost : -1)
                .ThenBy(p => p.SourceId).First(); plans.Remove(best);
            // A set of small tributaries can justify their common connector even when
            // every single linear path is subthreshold. Admit their union atomically;
            // never count a hoped-for, unselected tributary as delivered benefit.
            var package = best.Edges.ToList(); var members = new HashSet<int>(package);
            while (best.AddedRunoff < minimumRunoff && best.AddedCost > 0)
            {
                var partner = plans.Where(p => p.Edges[0] == best.Edges[0])
                    .Select(p => new { Plan = p, Extra = p.Edges.Where(id => !members.Contains(id)).ToArray() })
                    .Where(p => p.Extra.Length > 0 && package.Count + p.Extra.Length <= MaximumChainEdges)
                    .Select(p => new { p.Plan, p.Extra,
                        Gain = p.Extra.Where(id => !selected.Contains(id)).Sum(id => nodes[id].LocalRunoff),
                        Cost = p.Extra.Where(id => !selected.Contains(id)).Sum(id => nodes[id].Cost) })
                    .Where(p => p.Gain > 0 && p.Cost > 0)
                    .OrderByDescending(p => p.Gain / p.Cost).ThenBy(p => p.Plan.SourceId).FirstOrDefault();
                if (partner == null) break;
                foreach (int id in partner.Extra) { members.Add(id); package.Add(id); }
                best.AddedRunoff += partner.Gain; best.AddedCost += partner.Cost;
            }
            best.Edges = package.ToArray();
            var added = best.Edges.Where(id => !selected.Contains(id)).ToArray();
            if (added.Length == 0) continue;
            if (best.AddedRunoff < minimumRunoff) { result.BelowRunoff++; result.Rejections[best.SourceId] = "runoff"; continue; }
            if (!compatible(added, result.Ordered)) { result.Conflicts++; result.Rejections[best.SourceId] = "conflict"; continue; }
            result.Chains.Add(best);
            foreach (int id in added) { selected.Add(id); result.Ordered.Add(id); }
        }
        return result;
    }
}
