using System;
using System.Collections.Generic;
using System.Linq;

public sealed partial class GeodesicValleyCarving
{
    private sealed partial class MajorOutletPlanner
    {
        private const int MaximumCorridors = 3, MaximumCoarseExpansions = 512, ReceiverBandEdges = 6;
        private const int ReceiverTraceSteps = 24;
        private const int Primary = GeodesicOceanDrainagePlan.Ocean, Unknown = GeodesicOceanDrainagePlan.Unresolved;

        private readonly struct Portal
        {
            internal readonly int To, Gate, Arrival, ReceivingFacet;
            internal readonly bool SpillWitness;
            internal Portal(int to, int gate, int arrival, int receivingFacet, bool spillWitness)
            { To = to; Gate = gate; Arrival = arrival; ReceivingFacet = receivingFacet; SpillWitness = spillWitness; }
        }

        private sealed class Corridor
        {
            internal int Basin;
            internal Portal Portal;
            internal Corridor Previous;
            internal double Floor, Volume, Barrier, Length, Runoff, Risk;
            internal int Steps;
        }

        private readonly Dictionary<int, List<Portal>> portals = new Dictionary<int, List<Portal>>();
        private readonly Dictionary<int, List<int>> basinFacets = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, int> oceanSteps = new Dictionary<int, int>();
        private readonly Dictionary<int, double> basinRunoff = new Dictionary<int, double>();

        private int[] BuildCoarseGraph(IReadOnlyList<GeodesicOceanDrainagePlan.Catchment> catchments)
        {
            var ids = new HashSet<int>(carving.basins.Basins.Select(b => b.Id));
            var owners = new int[tin.TriangleCount];
            foreach (var c in catchments) basinRunoff.Add(c.Id, c.Runoff);
            for (int t = 0; t < owners.Length; t++)
            {
                var end = tin.ParcelDestinations[t];
                owners[t] = PrimaryOcean(tin, components, primary, end) ? Primary : ClosedFloor(end);
                if (owners[t] < 0) continue;
                if (!basinFacets.TryGetValue(owners[t], out var facets))
                    basinFacets.Add(owners[t], facets = new List<int>());
                facets.Add(t);
            }
            // Both carrier facets have actual centroid traces. Their shared edge is a
            // geographic crossing witness, even when it is not the basin's lowest spill.
            for (int t = 0; t < owners.Length; t++)
            {
                int a = owners[t];
                if (a == Unknown) continue;
                for (int k = 0; k < 3; k++)
                {
                    int other = tin.Neighbors[t * 3 + k];
                    if (other <= t) continue;
                    int b = owners[other];
                    if (b == Unknown || a == b) continue;
                    for (int i = 0; i < 3; i++)
                    {
                        int vertex = tin.BasinVertex(t, i);
                        if (!ContainsVertex(other, vertex)) continue;
                        if (a >= 0) AddPortal(a, new Portal(b, vertex, vertex, other, false));
                        if (b >= 0) AddPortal(b, new Portal(a, vertex, vertex, t, false));
                    }
                }
            }
            // Also retain the exact spill and downstream carrier from the lower-star TIN.
            // This can bridge a saddle where centroid ownership alone misses the outlet.
            foreach (var basin in carving.basins.Basins)
            {
                int side = basin.DownstreamVertex, facet = basin.DownstreamTriangle;
                if (!carving.TrySpillVertex(basin, out int spill) || side < 0 || side == spill ||
                    facet < 0 || !tin.BasinFaceValid(facet) || !ContainsVertex(facet, spill) ||
                    !ContainsVertex(facet, side) || carving.original[side] >= carving.original[spill] ||
                    carving.protectedLake[spill] || carving.protectedLake[side]) continue;
                var trace = tin.TraceLakeOutlet(tin.BasinPoint(facet, side), null, out var termination);
                var end = new TinParcelDestination(trace[trace.Length - 1], termination);
                int to = PrimaryOcean(tin, components, primary, end) ? Primary : ClosedFloor(end);
                if (to != Unknown && to != basin.Id) AddPortal(basin.Id, new Portal(to, spill, side, facet, true));
            }
            var upstream = new Dictionary<int, List<int>>();
            foreach (var entry in portals)
                foreach (int to in entry.Value.Select(p => p.To).Distinct())
                {
                    if (!upstream.TryGetValue(to, out var sources)) upstream.Add(to, sources = new List<int>());
                    sources.Add(entry.Key);
                }
            var queue = new Queue<int>();
            oceanSteps.Add(Primary, 0);
            queue.Enqueue(Primary);
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                if (!upstream.TryGetValue(node, out var sources)) continue;
                foreach (int source in sources)
                    if (oceanSteps.TryAdd(source, oceanSteps[node] + 1)) queue.Enqueue(source);
            }
            return owners;

            int ClosedFloor(TinParcelDestination end)
            {
                if (!GeodesicOceanDrainagePlan.Closed(end.Termination)) return Unknown;
                var p = end.Point;
                int v = p.A == 1 ? tin.BasinVertex(p.Triangle, 0) : p.B == 1 ? tin.BasinVertex(p.Triangle, 1) :
                    p.C == 1 ? tin.BasinVertex(p.Triangle, 2) : Unknown;
                return ids.Contains(v) ? v : Unknown;
            }
        }

        private bool ContainsVertex(int facet, int vertex) =>
            tin.BasinVertex(facet, 0) == vertex || tin.BasinVertex(facet, 1) == vertex || tin.BasinVertex(facet, 2) == vertex;

        private void AddPortal(int from, Portal portal)
        {
            if (carving.protectedLake[portal.Gate] || carving.protectedLake[portal.Arrival] ||
                (carving.ocean[portal.Gate] && portal.To != Primary)) return;
            if (!portals.TryGetValue(from, out var list)) portals.Add(from, list = new List<Portal>());
            // Keep one exact spill and two distinct boundary alternatives per destination.
            // Estimate ranking is only used to allocate search work.
            int same = list.FindIndex(p => p.To == portal.To && p.SpillWitness == portal.SpillWitness && p.Gate == portal.Gate);
            if (same >= 0) return;
            int count = 0, worst = -1;
            double worstCost = double.NegativeInfinity;
            for (int i = 0; i < list.Count; i++)
            {
                var previous = list[i];
                if (previous.To != portal.To || previous.SpillWitness != portal.SpillWitness) continue;
                count++;
                double cost = PortalCost(from, previous);
                if (cost > worstCost || (cost == worstCost && previous.Gate > list[worst].Gate))
                { worst = i; worstCost = cost; }
            }
            if (count < (portal.SpillWitness ? 1 : 2)) list.Add(portal);
            else
            {
                double cost = PortalCost(from, portal);
                if (cost < worstCost || (cost == worstCost && portal.Gate < list[worst].Gate)) list[worst] = portal;
            }
        }

        private double PortalCost(int from, Portal p) =>
            Math.Max(0, carving.original[p.Gate] - carving.original[from]) / carving.depth +
            carving.Angle(from, p.Gate) * carving.radius / lengthLimit +
            (shore[p.Gate] || carving.protectedSubsea[p.Gate] ? 1 : 0);

        private List<Corridor> FindCorridors(int start, double runoff, MajorOutletSearchEvidence evidence)
        {
            var completed = new List<Corridor>();
            if (!oceanSteps.ContainsKey(start)) return completed;
            var frontier = new SortedSet<(double Score, int Id, Corridor Path)>(
                Comparer<(double Score, int Id, Corridor Path)>.Create((a, b) =>
                {
                    int compare = a.Score.CompareTo(b.Score);
                    return compare != 0 ? compare : a.Id.CompareTo(b.Id);
                }));
            var root = new Corridor { Basin = start, Floor = carving.original[start], Runoff = runoff };
            frontier.Add((0, 0, root));
            int serial = 1;
            var seenRoutes = new HashSet<string>();
            while (frontier.Count > 0 && evidence.CoarseExpansions < MaximumCoarseExpansions)
            {
                var item = frontier.Min;
                frontier.Remove(item);
                var current = item.Path;
                evidence.CoarseExpansions++;
                if (current.Basin == Primary)
                {
                    // Terrain refinement searches the whole chosen basin strip. Different
                    // upstream portal samples in that same strip do not need another pass.
                    string key = string.Join(",", BasinChain(current)) + ":" + current.Portal.Gate;
                    if (seenRoutes.Add(key)) completed.Add(current);
                    if (completed.Count == MaximumCorridors) break;
                    continue;
                }
                if (current.Steps >= GeodesicOceanDrainagePlan.MaximumChainEdges ||
                    !portals.TryGetValue(current.Basin, out var outgoing)) continue;
                int entry = current.Previous == null ? start : current.Portal.Arrival;
                // Every retained gate between two interior basins is included in the
                // same refinement region. Explore that basin transition once.
                var exits = outgoing.Where(p => p.To == Primary).Concat(outgoing.Where(p => p.To >= 0)
                    .GroupBy(p => p.To).Select(g => g.OrderBy(p => PortalCost(current.Basin, p))
                        .ThenByDescending(p => p.SpillWitness).ThenBy(p => p.Gate).First()));
                foreach (var edge in exits)
                {
                    if (!oceanSteps.TryGetValue(edge.To, out int hops) || InChain(current, edge.To)) continue;
                    double length = carving.radius * (carving.Angle(entry, edge.Gate) + carving.Angle(edge.Gate, edge.Arrival));
                    double barrier = Math.Max(0, carving.original[edge.Gate] - current.Floor);
                    double volume = barrier * carving.graph.UnitCellAreas[edge.Gate] * carving.radius * carving.radius;
                    var next = new Corridor {
                        Basin = edge.To, Portal = edge, Previous = current, Steps = current.Steps + 1,
                        Floor = edge.To == Primary ? current.Floor : Math.Min(current.Floor, carving.original[edge.To]),
                        Volume = current.Volume + volume, Barrier = Math.Max(current.Barrier, barrier),
                        Length = current.Length + length, Runoff = current.Runoff + basinRunoff.GetValueOrDefault(edge.To),
                        Risk = current.Risk + (shore[edge.Gate] || carving.protectedSubsea[edge.Gate] ? .5 : 0) +
                            (edge.SpillWitness ? 0 : .05)
                    };
                    double benefit = 1 + .2 * Math.Min(4, Math.Max(0, next.Runoff - runoff) / Math.Max(runoff, 1e-12));
                    frontier.Add(((Score(next.Volume, next.Length, next.Barrier) + next.Risk) / benefit + .04 * hops, serial++, next));
                }
            }
            return completed;

            bool InChain(Corridor path, int basin)
            {
                for (; path != null; path = path.Previous) if (path.Basin == basin) return true;
                return false;
            }
        }

        private static int[] BasinChain(Corridor path)
        {
            var chain = new List<int>();
            for (; path != null; path = path.Previous) chain.Add(path.Basin);
            chain.Reverse();
            return chain.ToArray();
        }

        private static string DescribeCorridor(Corridor path)
        {
            int spills = 0;
            for (var step = path; step.Previous != null; step = step.Previous)
                if (step.Portal.SpillWitness) spills++;
            return FormattableString.Invariant(
                $"chain=[{string.Join(",", BasinChain(path))}] exitVertex={path.Portal.Gate} exactSpillEdges={spills} estimatedVolume={path.Volume:R} barrier={path.Barrier:R} length={path.Length:R} protectedGeometryRisk={path.Risk:R} potentialIndependentRunoff={path.Runoff:R}");
        }

        private Dictionary<int, int> RefineRegion(Corridor corridor, int start, MajorOutletSearchEvidence evidence,
            out Dictionary<int, double> minimumReceiverBed)
        {
            var allowed = new HashSet<int>();
            for (var path = corridor; path != null; path = path.Previous)
            {
                if (path.Previous != null)
                {
                    foreach (var edge in portals[path.Previous.Basin])
                    {
                        if (edge.To != path.Basin || (path.Basin == Primary && edge.Gate != path.Portal.Gate)) continue;
                        allowed.Add(edge.Gate);
                        allowed.Add(edge.Arrival);
                    }
                }
                if (path.Basin < 0) continue;
                allowed.Add(path.Basin);
                if (!basinFacets.TryGetValue(path.Basin, out var facets)) continue;
                foreach (int facet in facets)
                    for (int k = 0; k < 3; k++) allowed.Add(tin.BasinVertex(facet, k));
            }
            // Extend the selected exit into a local band of existing drainage. Interior
            // basin strips already come from the witnessed facet ownership above.
            var band = new Dictionary<int, int> { [corridor.Portal.Arrival] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(corridor.Portal.Arrival);
            var outlet = corridor.Portal;
            var witness = outlet.SpillWitness ? tin.BasinPoint(outlet.ReceivingFacet, outlet.Arrival) :
                tin.Centroid(outlet.ReceivingFacet);
            foreach (var point in tin.TraceLakeOutlet(witness, null, out _, ReceiverTraceSteps))
                for (int k = 0; k < 3; k++)
                {
                    int vertex = tin.BasinVertex(point.Triangle, k);
                    if (!carving.protectedLake[vertex] && band.TryAdd(vertex, 0)) queue.Enqueue(vertex);
                }
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                allowed.Add(v);
                if (band[v] == ReceiverBandEdges) continue;
                for (int k = 0; k < carving.graph.NeighborCounts[v]; k++)
                {
                    int next = carving.graph.Neighbors6[v * 6 + k];
                    if (!carving.protectedLake[next] && band.TryAdd(next, band[v] + 1)) queue.Enqueue(next);
                }
            }
            var distance = new Dictionary<int, int>();
            foreach (int v in band.Keys.OrderBy(v => v))
            {
                if (carving.protectedLake[v] || (receiverHops[v] != 0 &&
                    (v != corridor.Portal.Arrival || !corridor.Portal.SpillWitness))) continue;
                if (carving.original[v] >= carving.original[start])
                { evidence.ReceiverAboveSource++; continue; }
                distance.Add(v, 0);
                queue.Enqueue(v);
            }
            minimumReceiverBed = ReceiverBedBounds(allowed, queue);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                for (int k = 0; k < carving.graph.NeighborCounts[v]; k++)
                {
                    int next = carving.graph.Neighbors6[v * 6 + k];
                    if (allowed.Contains(next) && !carving.protectedLake[next] && distance.TryAdd(next, distance[v] + 1))
                        queue.Enqueue(next);
                }
            }
            return distance;
        }

        private Dictionary<int, double> ReceiverBedBounds(HashSet<int> allowed, IEnumerable<int> seeds)
        {
            var minimum = new Dictionary<int, double>();
            var frontier = new SortedSet<(double Bed, int Vertex)>();
            double slack = Math.Max(tin.Epsilon, carving.radius * 1e-12);
            foreach (int vertex in seeds)
            {
                minimum.Add(vertex, carving.original[vertex]);
                frontier.Add((minimum[vertex], vertex));
            }
            // Invert the exact descending step. The minimum over all suffixes is an
            // optimistic bed requirement since we ignore volume, length and bank edits.
            while (frontier.Count > 0)
            {
                var current = frontier.Min;
                frontier.Remove(current);
                var (bed, vertex) = current;
                for (int k = 0; k < carving.graph.NeighborCounts[vertex]; k++)
                {
                    int previous = carving.graph.Neighbors6[vertex * 6 + k];
                    if (!allowed.Contains(previous) || carving.protectedLake[previous]) continue;
                    double cosine = Math.Cos(carving.Angle(previous, vertex));
                    if (cosine <= 0) continue;
                    double original = carving.original[previous];
                    double localMinimum = shore[previous] || carving.protectedSubsea[previous] ? original :
                        Math.Min(original, Math.Max(original - carving.depth, carving.seaFloor));
                    double required = Math.Max(localMinimum, (bed + carving.step) / cosine);
                    if (required > original + slack) continue;
                    if (minimum.TryGetValue(previous, out double existing))
                    {
                        if (existing <= required) continue;
                        frontier.Remove((existing, previous));
                    }
                    minimum[previous] = required;
                    frontier.Add((required, previous));
                }
            }
            return minimum;
        }
    }
}
