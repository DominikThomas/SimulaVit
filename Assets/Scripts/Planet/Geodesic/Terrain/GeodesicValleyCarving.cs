using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>One immutable-input planning pass. This is a bounded saddle breach, not erosion.
/// All heights and lengths are planet-local units; runoff uses the existing mean-cell-area units.
/// The caller commits the resulting surface once and rebuilds the existing TIN once.</summary>
public sealed class GeodesicValleyCarving
{
    public const float DefaultDepth = .005f, DefaultRunoff = 64f, DefaultWidth = .02f;
    public const string Help = "One generation pass on visible, unfiltered TIN terrain. Uses the matched basin's natural spill and a short downstream breach; unmatched/invalid spills use bounded fallback search. Depth and width are planet-local units; runoff uses mean-cell-area equivalents. Width controls lateral falloff only. Green: accepted; red: rejected; yellow: original termination; cyan: target (Scene gizmos).";
    private const int MaximumLabels = 8192, MaximumTargetProbes = 128, MaximumAffectedVertices = 8192;
    private const int MaximumAffectedFacets = MaximumAffectedVertices * 6;
    private enum Target { River, LakeOutlet, OceanDrainage, Coastline, DownstreamBasin, None }

    public sealed class Candidate
    {
        public Vector3 Origin, TargetPoint;
        public Vector3[] Path = Array.Empty<Vector3>();
        public string Rejection;
        public bool Accepted => Rejection == null;
        public double Length, MaximumCut, MeanCut;
        public int AffectedVertices, AffectedFacets;
        public bool SpillDirected;
        public int BasinId = -1;
        public double OriginalSpillBarrier;
        public string FallbackReason;
        internal int[] Vertices;
        internal Dictionary<int, float> Surface;
        internal TinPoint[] Continuation;
        internal List<TinPoint[]> Incoming;
        internal int TargetKind;
    }

    public sealed class Result
    {
        public float[] Heights;
        public readonly List<Candidate> Candidates = new List<Candidate>();
        public int Eligible, Searched, SkippedLake, SkippedDuplicate, SkippedUnsupported;
        public int MatchedToBasin, SpillDirected, FallbackSearched, InvalidSpills;
        public int AffectedVertices, AffectedFacets;
        public double MaximumCut, MeanCut, PathLength;
        public int Accepted => Candidates.Count(c => c.Accepted);
        public string Summary() => $"[GeodesicValleyCarving] enabled=True eligible={Eligible} matched-to-basin={MatchedToBasin} searched={Searched} " +
            $"spill-directed={SpillDirected} fallback-searched={FallbackSearched} accepted-spill-carves={Candidates.Count(c => c.Accepted && c.SpillDirected)} accepted-fallback-carves={Candidates.Count(c => c.Accepted && !c.SpillDirected)} accepted={Accepted} " +
            $"rejected-invalid-spill={InvalidSpills} invalid-spill-action=fallback fallback-unmatched={FallbackSearched - InvalidSpills} rejected-lake-protected={SkippedLake + Rejected("lake")} " +
            $"rejected-spill({Reasons(true)}) rejected-fallback({Reasons(false)}) " +
            $"skipped(lake/duplicate/unsupported)={SkippedLake}/{SkippedDuplicate}/{SkippedUnsupported} " +
            $"pathLength={PathLength:G6} affectedVertices={AffectedVertices} affectedFacets={AffectedFacets} maximumActualCut={MaximumCut:G6} meanActualCut={MeanCut:G6} " +
            $"connectedTargets(river/lake-outlet/ocean-drainage/coastline/downstream-basin)={Connected(0)}/{Connected(1)}/{Connected(2)}/{Connected(3)}/{Connected(4)} " +
            SpillStatistics() +
            "connections=planned-not-post-rebuild-confirmed passes=1";
        private string Reasons(bool spill) => string.Join(" ", new[] { "too-deep", "below-sea-level", "invalid-spill", "no-downstream-side", "lake", "conflict", "no-target", "search-distance-limit", "footprint-limit", "search-budget" }
            .Select(reason => $"{(reason == "lake" ? "lake-protected" : !spill && reason == "search-budget" ? "fallback-search-budget" : reason)}={Candidates.Count(c => c.SpillDirected == spill && c.Rejection == reason)}"));
        private string SpillStatistics()
        {
            var accepted = Candidates.Where(c => c.Accepted && c.SpillDirected).ToArray();
            return $"spill-original-barrier(mean/max)={accepted.Select(c => c.OriginalSpillBarrier).DefaultIfEmpty(0).Average():G6}/{accepted.Select(c => c.OriginalSpillBarrier).DefaultIfEmpty(0).Max():G6} " +
                $"spill-required-cut(mean/max)={accepted.Sum(c => c.MeanCut * c.AffectedVertices) / Math.Max(1, accepted.Sum(c => c.AffectedVertices)):G6}/{accepted.Select(c => c.MaximumCut).DefaultIfEmpty(0).Max():G6} " +
                $"spill-mean-path-length={accepted.Select(c => c.Length).DefaultIfEmpty(0).Average():G6} ";
        }
        private int Rejected(string reason) => Candidates.Count(c => c.Rejection == reason);
        private int Connected(int kind) => Candidates.Count(c => c.Accepted && c.TargetKind == kind);
    }

    private sealed class Label
    {
        public int Vertex, Id;
        public double Floor, Length, Cost, MaxCut, Priority;
        public Label Previous;
    }
    private sealed class LabelOrder : IComparer<Label>
    {
        public int Compare(Label a, Label b)
        {
            int c = a.Priority.CompareTo(b.Priority);
            if (c == 0) c = a.Vertex.CompareTo(b.Vertex);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        }
    }
    private sealed class Escape
    {
        public Target Kind;
        public TinPoint[] Path;
    }

    private readonly GeodesicTinHydrology tin;
    private readonly GeodesicHydrologyTopology graph;
    private readonly GeodesicTinLakeRouting lakes;
    private readonly GeodesicTinBasins basins;
    private readonly float[] original;
    private readonly bool[] ocean, protectedLake, protectedSubsea;
    private readonly HashSet<int> riverFacets = new HashSet<int>();
    private readonly Dictionary<int, TinLakeFlowNode> lakeNodes = new Dictionary<int, TinLakeFlowNode>();
    private readonly Dictionary<int, Escape> escapes = new Dictionary<int, Escape>();
    private readonly double radius, depth, width, seaFloor, step, maxLength, maxRadius;

    private GeodesicValleyCarving(GeodesicTinHydrology drainage, GeodesicHydrologyTopology topology,
        bool[] retainedOcean, GeodesicTinBasins basinTopology, GeodesicTinLakeGeometry geometry, GeodesicTinLakeRouting routing,
        double planetRadius, double seaLevel, float maximumDepth, float carveWidth)
    {
        tin = drainage; graph = topology; basins = basinTopology; lakes = routing; ocean = retainedOcean;
        original = tin.VisibleHeight; radius = planetRadius; depth = maximumDepth; width = carveWidth;
        step = Math.Max(tin.Epsilon * 8, radius * .000001);
        // Include the spherical facet sagitta: vertices above sea alone do not protect facet interiors.
        double maxEdge = 0;
        for (int v = 0; v < graph.CellCount; v++)
            for (int k = 0; k < graph.NeighborCounts[v]; k++)
                maxEdge = Math.Max(maxEdge, Angle(v, graph.Neighbors6[v * 6 + k]));
        seaFloor = (seaLevel + step * 2) / Math.Cos(maxEdge);
        maxRadius = radius * 8 * Math.PI / 180; maxLength = radius * 12 * Math.PI / 180;
        protectedLake = new bool[graph.CellCount];
        protectedSubsea = new bool[graph.CellCount];
        for (int v = 0; v < graph.CellCount; v++)
        {
            // Do not lower a facet adjoining an excluded inland sea. Protect its entire
            // one-ring, since keeping only the edited vertex above sea is insufficient.
            if (ocean[v] || original[v] >= seaFloor) continue;
            protectedSubsea[v] = true;
            for (int k = 0; k < graph.NeighborCounts[v]; k++) protectedSubsea[graph.Neighbors6[v * 6 + k]] = true;
        }
        if (geometry != null)
            foreach (var p in geometry.TerrainPoints)
                for (int k = 0; k < 3; k++) protectedLake[tin.BasinVertex(p.Triangle, k)] = true;
        if (lakes != null) foreach (var n in lakes.Lakes) lakeNodes[n.BasinId] = n;
        foreach (var network in tin.Networks)
            foreach (var p in network.Samples) riverFacets.Add(p.Triangle);
    }

    public static float Normalize(float value, float fallback, float maximum) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, 0, maximum);

    public static Result Plan(GeodesicTinHydrology tin, GeodesicHydrologyTopology graph, bool[] retainedOcean,
        GeodesicTinLakeGeometry geometry, GeodesicTinLakeRouting routing, double radius, double seaLevel,
        float maximumDepth, double minimumRunoff, float width, GeodesicTinBasins basinTopology = null)
    {
        // Reuse rendered-lake topology, or observe the same active TIN when lakes are disabled.
        // This does not create lake geometry or replace any drainage receivers.
        var planner = new GeodesicValleyCarving(tin, graph, retainedOcean, basinTopology ?? GeodesicTinBasins.Build(tin), geometry, routing, radius, seaLevel, maximumDepth, width);
        return planner.Build(minimumRunoff);
    }

    private Result Build(double minimumRunoff)
    {
        var result = new Result { Heights = (float[])original.Clone() };
        var starts = new SortedDictionary<int, TinPoint>();
        var incoming = new Dictionary<int, List<TinPoint[]>>();
        var matched = new Dictionary<int, TinBasinDescriptor>();
        foreach (var n in tin.Networks)
        {
            if (n.Termination != TinTermination.TrueClosedBasin || n.Samples == null || n.Samples.Length < 2) continue;
            // Never sum accumulated values (that would double-count tributary parcels).
            double runoff = n.Samples.Max(p => tin.AccumulatedRunoff[p.Triangle]);
            if (runoff < minimumRunoff) continue;
            result.Eligible++;
            var end = n.Samples[n.Samples.Length - 1];
            var basin = basins.Find(end);
            if (basin != null) result.MatchedToBasin++;
            if (lakes != null && lakes.FirstEntry(n.Samples, out _)) { result.SkippedLake++; continue; }
            int vertex = -1;
            for (int k = 0; k < 3; k++)
                if ((k == 0 ? end.A : k == 1 ? end.B : end.C) >= 1 - 1e-8) vertex = tin.BasinVertex(end.Triangle, k);
            // Never snap an arbitrary failed/flat facet path to a different depression.
            if (vertex < 0 || ocean[vertex]) { result.SkippedUnsupported++; continue; }
            if (protectedLake[vertex]) { result.SkippedLake++; continue; }
            if (starts.ContainsKey(vertex)) { incoming[vertex].Add(n.Samples); result.SkippedDuplicate++; continue; }
            starts.Add(vertex, end);
            matched[vertex] = basin;
            incoming[vertex] = new List<TinPoint[]> { n.Samples };
        }
        foreach (var pair in starts)
        {
            result.Searched++;
            var basin = matched[pair.Key];
            Candidate c;
            if (TrySpillVertex(basin, out int spill))
            {
                result.SpillDirected++;
                c = PlanSpill(pair.Key, basin, spill);
            }
            else
            {
                result.FallbackSearched++;
                if (basin != null) result.InvalidSpills++;
                c = Search(pair.Key);
                c.BasinId = basin?.Id ?? -1;
                c.FallbackReason = basin == null ? "unmatched-basin" : "invalid-spill";
            }
            c.Incoming = incoming[pair.Key];
            if (c.Accepted && !ProfileValid(c, null)) c.Rejection = "conflict";
            c.Origin = tin.Direction(pair.Value) * (float)tin.CompletedVisibleRadius(pair.Value);
            result.Candidates.Add(c);
        }

        // Check every overlapping pair against immutable proposals. Symmetric rejection avoids
        // processing-order winners. A min of individually descending pair profiles stays descending.
        var proposed = result.Candidates.Where(c => c.Accepted).ToArray();
        var conflicts = new HashSet<Candidate>();
        for (int i = 0; i < proposed.Length; i++)
            for (int j = i + 1; j < proposed.Length; j++)
                if (!ProfileValid(proposed[i], proposed[j].Surface) || !ProfileValid(proposed[j], proposed[i].Surface))
                { conflicts.Add(proposed[i]); conflicts.Add(proposed[j]); }
        foreach (var c in conflicts) c.Rejection = "conflict";
        var edges = new HashSet<ulong>();
        foreach (var c in result.Candidates.Where(c => c.Accepted))
        {
            foreach (var cut in c.Surface) result.Heights[cut.Key] = Mathf.Min(result.Heights[cut.Key], cut.Value);
            for (int i = 1; i < c.Vertices.Length; i++)
            {
                int a = c.Vertices[i - 1], b = c.Vertices[i];
                ulong key = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                if (edges.Add(key)) result.PathLength += Angle(a, b) * radius;
            }
        }
        var facets = new HashSet<int>(); double total = 0;
        for (int v = 0; v < original.Length; v++)
        {
            double cut = original[v] - result.Heights[v]; if (cut <= 0) continue;
            result.AffectedVertices++; total += cut; result.MaximumCut = Math.Max(result.MaximumCut, cut);
            AddFacets(v, facets);
        }
        result.AffectedFacets = facets.Count; result.MeanCut = total / Math.Max(1, result.AffectedVertices);
        return result;
    }

    private bool TrySpillVertex(TinBasinDescriptor basin, out int vertex)
    {
        vertex = -1;
        if (basin == null || !basin.Spill.HasValue) return false;
        var p = basin.Spill.Value;
        if (p.Triangle < 0 || p.Triangle >= tin.TriangleCount || !tin.BasinFaceValid(p.Triangle)) return false;
        // The existing lower-star topology records exact mesh vertices, not snapped centroids.
        // If a future descriptor uses another geometry, keep that case in the explicit fallback.
        if (p.A == 1 && p.B == 0 && p.C == 0) vertex = tin.BasinVertex(p.Triangle, 0);
        else if (p.A == 0 && p.B == 1 && p.C == 0) vertex = tin.BasinVertex(p.Triangle, 1);
        else if (p.A == 0 && p.B == 0 && p.C == 1) vertex = tin.BasinVertex(p.Triangle, 2);
        return vertex >= 0 && !double.IsNaN(basin.SpillElevation) && !double.IsInfinity(basin.SpillElevation)
            && Math.Abs(tin.Height(p) - basin.SpillElevation) <= tin.Epsilon;
    }

    private Candidate PlanSpill(int start, TinBasinDescriptor basin, int spill)
    {
        var c = new Candidate {
            SpillDirected = true, BasinId = basin.Id, OriginalSpillBarrier = Math.Max(0, basin.SpillElevation - original[start]),
            Origin = graph.CellDirections[start] * original[start], TargetPoint = tin.Direction(basin.Spill.Value) * original[spill],
            TargetKind = (int)Target.DownstreamBasin
        };
        if (original[start] <= seaFloor + step || basin.SpillElevation < seaFloor)
        { c.Rejection = "below-sea-level"; return c; }
        if (c.OriginalSpillBarrier > depth) { c.Rejection = "too-deep"; return c; }
        if (protectedLake[start] || protectedLake[spill]) { c.Rejection = "lake"; return c; }

        int side = basin.DownstreamVertex, triangle = basin.DownstreamTriangle;
        if (triangle < 0 || triangle >= tin.TriangleCount || !tin.BasinFaceValid(triangle) ||
            side < 0 || side >= original.Length || side == spill ||
            !Enumerable.Range(0, 3).Any(k => tin.BasinVertex(triangle, k) == spill) ||
            !Enumerable.Range(0, 3).Any(k => tin.BasinVertex(triangle, k) == side) ||
            original[side] >= basin.SpillElevation)
        { c.Rejection = "no-downstream-side"; return c; }

        // Search for geometry to ONE known saddle within the matched sublevel component.
        // No target probes, Pareto-label explosion, ancestor traversal or ocean search.
        var interior = new HashSet<int>();
        foreach (int t in basin.MemberTriangles)
            for (int k = 0; k < 3; k++)
            {
                int v = tin.BasinVertex(t, k);
                if (original[v] < basin.SpillElevation) interior.Add(v);
            }
        interior.Remove(side); interior.Add(start); interior.Add(spill);
        var first = new Label { Vertex = start, Floor = original[start] };
        var saddle = RouteGeometry(first, spill, interior, start, maxLength, false, out string reason);
        if (saddle == null) { c.Rejection = reason; return c; }
        SetPath(c, saddle);

        var witness = Extend(saddle, side, start, maxLength, out reason);
        if (witness == null) { c.Rejection = reason; return c; }
        SetPath(c, witness);

        // Observe only a short existing TIN descent from the exact downstream witness.
        // The corridor stops after eight local edge lengths; the tracer is also step-bounded.
        // A lower closed parent/downstream basin is a valid endpoint; no ocean proof is needed.
        double downstreamLimit = Angle(spill, side) * radius * 8;
        var trace = tin.TraceLakeOutlet(tin.BasinPoint(triangle, side), null, out _, maximumSteps: 64);
        var corridor = new HashSet<int> { side };
        double tracedLength = Angle(spill, side) * radius;
        for (int i = 0; i < trace.Length; i++)
        {
            if (i > 0) tracedLength += Vector3.Distance(tin.Direction(trace[i - 1]), tin.Direction(trace[i])) * radius;
            if (tracedLength > downstreamLimit) break;
            for (int k = 0; k < 3; k++)
            {
                int v = tin.BasinVertex(trace[i].Triangle, k);
                if (original[v] < basin.SpillElevation) corridor.Add(v);
            }
        }
        foreach (int v in c.Vertices) if (v != side) corridor.Remove(v);
        var end = RouteGeometry(witness, -1, corridor, start, Math.Min(maxLength, saddle.Length + downstreamLimit), true, out reason);
        if (end == null) { c.Rejection = reason; return c; }
        var chain = SetPath(c, end);
        // Preserve the local existing continuation; reaching another basin is intentionally OK.
        int carrier = tin.BasinIncidentTriangle(tin.BasinIncidentStart(end.Vertex));
        var continuation = tin.TraceLakeOutlet(tin.BasinPoint(carrier, end.Vertex), null, out _, maximumSteps: 64);
        BuildProfile(c, chain, new Escape { Kind = Target.DownstreamBasin, Path = continuation });

        return c;
    }

    private List<Label> SetPath(Candidate c, Label end)
    {
        var chain = new List<Label>();
        for (var l = end; l != null; l = l.Previous) chain.Add(l);
        chain.Reverse();
        c.Vertices = chain.Select(l => l.Vertex).ToArray(); c.Length = end.Length;
        c.Path = chain.Select(l => graph.CellDirections[l.Vertex] * original[l.Vertex]).ToArray();
        c.TargetPoint = c.Path[c.Path.Length - 1];
        return chain;
    }

    // Geometric A* to the descriptor's spill, or a short descent inside its downstream
    // TIN trace corridor. These temporary labels never become drainage receivers.
    private Label RouteGeometry(Label first, int goal, HashSet<int> allowed, int origin,
        double lengthLimit, bool descendingSide, out string rejection)
    {
        var frontier = new SortedSet<Label>(new LabelOrder());
        var best = new Dictionary<int, Label> { [first.Vertex] = first };
        first.Priority = first.Cost + (goal >= 0 ? Angle(first.Vertex, goal) * radius : 0);
        frontier.Add(first); int serial = 1;
        bool tooDeep = false, subsea = false, lake = false, limited = false;
        while (frontier.Count > 0)
        {
            var current = frontier.Min; frontier.Remove(current);
            if (best[current.Vertex] != current) continue;
            if (current.Vertex == goal || (descendingSide && original[current.Vertex] <= current.Floor))
            { rejection = null; return current; }
            for (int k = 0; k < graph.NeighborCounts[current.Vertex]; k++)
            {
                int v = graph.Neighbors6[current.Vertex * 6 + k];
                if (!allowed.Contains(v) || (descendingSide && original[v] >= original[current.Vertex])) continue;
                var next = Extend(current, v, origin, lengthLimit, out string reason);
                if (next == null)
                {
                    tooDeep |= reason == "too-deep"; subsea |= reason == "below-sea-level";
                    lake |= reason == "lake"; limited |= reason == "search-distance-limit";
                    continue;
                }
                if (best.TryGetValue(v, out var previous) && previous.Cost <= next.Cost) continue;
                next.Id = serial++; next.Priority = next.Cost + (goal >= 0 ? Angle(v, goal) * radius : 0);
                best[v] = next; frontier.Add(next);
            }
        }
        rejection = tooDeep ? "too-deep" : subsea ? "below-sea-level" : lake ? "lake" : limited ? "search-distance-limit" : "no-downstream-side";
        return null;
    }

    private Label Extend(Label previous, int vertex, int origin, double lengthLimit, out string rejection)
    {
        rejection = null;
        double angle = Angle(previous.Vertex, vertex), length = previous.Length + angle * radius;
        if (length > lengthLimit || Angle(origin, vertex) * radius > maxRadius)
        { rejection = "search-distance-limit"; return null; }
        double floor = Math.Min(original[vertex], previous.Floor * Math.Cos(angle) - step);
        double cut = original[vertex] - floor;
        if (ocean[vertex] || floor < seaFloor || (protectedSubsea[vertex] && cut > 0))
        { rejection = "below-sea-level"; return null; }
        if (cut > depth) { rejection = "too-deep"; return null; }
        if (protectedLake[vertex] && cut > 0) { rejection = "lake"; return null; }
        double cost = previous.Cost + angle * radius * (1 + 4 * cut / Math.Max(depth, step));
        return new Label { Vertex = vertex, Previous = previous, Floor = floor, Length = length,
            MaxCut = Math.Max(previous.MaxCut, cut), Cost = cost, Priority = cost };
    }

    private Candidate Search(int start)
    {
        var c = new Candidate { Origin = graph.CellDirections[start] * original[start], TargetKind = (int)Target.None };
        if (original[start] <= seaFloor + step) { c.Rejection = "below-sea-level"; return c; }
        var frontier = new SortedSet<Label>(new LabelOrder());
        var labels = new Dictionary<int, List<Label>>();
        var first = new Label { Vertex = start, Floor = original[start] };
        frontier.Add(first); labels[start] = new List<Label> { first };
        int serial = 1, probes = 0; bool tooDeep = false, belowSea = false, distanceLimit = false, budget = false;
        Label best = null; Escape target = null;
        while (frontier.Count > 0)
        {
            var current = frontier.Min; frontier.Remove(current);
            if (!labels[current.Vertex].Contains(current)) continue;
            int v = current.Vertex;
            // A target is untouched and lower, so its existing downhill continuation remains usable.
            if (v != start && original[v] <= current.Floor && original[v] < original[start] - step)
            {
                if (!escapes.TryGetValue(v, out var escape))
                {
                    if (++probes > MaximumTargetProbes) { budget = true; break; }
                    escape = FindEscape(v); escapes[v] = escape;
                }
                if (escape.Kind != Target.None)
                {
                    if (target == null || escape.Kind < target.Kind || (escape.Kind == target.Kind && current.Cost < best.Cost))
                    { best = current; target = escape; }
                    // Never continue a proposed incision through its already accepted outlet.
                    continue;
                }
            }
            for (int k = 0; k < graph.NeighborCounts[v]; k++)
            {
                int next = graph.Neighbors6[v * 6 + k]; if (ocean[next]) continue;
                double angle = Angle(v, next), length = current.Length + angle * radius;
                if (length > maxLength || Angle(start, next) * radius > maxRadius) { distanceLimit = true; continue; }
                double floor = Math.Min(original[next], current.Floor * Math.Cos(angle) - step);
                if (floor < seaFloor) { belowSea = true; continue; }
                double cut = original[next] - floor, maxCut = Math.Max(current.MaxCut, cut);
                if (maxCut > depth) { tooDeep = true; continue; }
                if (protectedSubsea[next] && cut > 0) { belowSea = true; continue; }
                if (protectedLake[next] && cut > 0) continue;
                // Positive additive costs favor short shallow routes; Pareto labels retain a
                // higher floor even if another route reached this vertex more cheaply.
                double cost = current.Cost + angle * radius * (1 + 4 * cut / Math.Max(depth, step)) + Math.Max(0, maxCut - current.MaxCut) * 4;
                var nextLabel = new Label { Vertex = next, Id = serial++, Floor = floor, Length = length, Cost = cost, Priority = cost, MaxCut = maxCut, Previous = current };
                if (!labels.TryGetValue(next, out var list)) labels[next] = list = new List<Label>();
                if (list.Any(l => l.Cost <= cost && l.Length <= length && l.Floor >= floor && l.MaxCut <= maxCut)) continue;
                list.RemoveAll(l => cost <= l.Cost && length <= l.Length && floor >= l.Floor && maxCut <= l.MaxCut);
                list.Add(nextLabel); frontier.Add(nextLabel);
                if (serial >= MaximumLabels) { budget = true; break; }
            }
            if (budget) break;
        }
        if (best == null)
        {
            c.Rejection = budget ? "search-budget" : tooDeep ? "too-deep" : belowSea ? "below-sea-level" : distanceLimit ? "search-distance-limit" : "no-target";
            return c;
        }
        var chain = new List<Label>(); for (var l = best; l != null; l = l.Previous) chain.Add(l); chain.Reverse();
        c.Vertices = chain.Select(l => l.Vertex).ToArray(); c.Length = best.Length; c.TargetKind = (int)target.Kind;
        c.Path = chain.Select(l => graph.CellDirections[l.Vertex] * original[l.Vertex]).ToArray();
        c.TargetPoint = c.Path[c.Path.Length - 1];
        BuildProfile(c, chain, target);
        return c;
    }

    private Escape FindEscape(int vertex)
    {
        int triangle = tin.BasinIncidentTriangle(tin.BasinIncidentStart(vertex));
        // The existing observational outlet tracer does not alter receivers, runoff or diagnostics.
        var path = tin.TraceLakeOutlet(tin.BasinPoint(triangle, vertex), null, out var termination);
        if (path.Length < 2) return new Escape { Kind = Target.None };
        bool lakeEscape = lakes != null && lakes.FirstEntry(path, out var entry) && LakeEscapes(entry.BasinId, new HashSet<int>());
        if (!lakeEscape && termination != TinTermination.OceanCoast) return new Escape { Kind = Target.None };
        // Only a target already lying on a river-supporting facet gets river priority.
        bool river = false;
        for (int i = tin.BasinIncidentStart(vertex); i < tin.BasinIncidentEnd(vertex); i++)
            river |= riverFacets.Contains(tin.BasinIncidentTriangle(i));
        double distance = 0;
        for (int i = 1; i < path.Length; i++) distance += Vector3.Distance(tin.Direction(path[i - 1]), tin.Direction(path[i])) * radius;
        double localEdge = 0;
        for (int k = 0; k < graph.NeighborCounts[vertex]; k++) localEdge = Math.Max(localEdge, Angle(vertex, graph.Neighbors6[vertex * 6 + k]) * radius);
        return new Escape { Kind = river ? Target.River : lakeEscape ? Target.LakeOutlet : distance <= localEdge * 2 ? Target.Coastline : Target.OceanDrainage, Path = path };
    }

    private bool LakeEscapes(int id, HashSet<int> seen)
    {
        if (!seen.Add(id) || !lakeNodes.TryGetValue(id, out var node) || node.Terminal) return false;
        if (node.DownstreamLakeId >= 0) return LakeEscapes(node.DownstreamLakeId, seen);
        if (node.OutletTermination == TinTermination.OceanCoast) return true;
        int network = node.JoinNetwork; var visited = new HashSet<int>();
        while (network >= 0 && network < lakes.RiverReaches.Count && visited.Add(network))
        {
            var reach = lakes.RiverReaches[network];
            if (reach.ReceivingLakeId >= 0) return LakeEscapes(reach.ReceivingLakeId, seen);
            if (reach.Termination == TinTermination.OceanCoast) return true;
            network = reach.JunctionNetwork;
        }
        return false;
    }

    private void BuildProfile(Candidate c, List<Label> chain, Escape escape)
    {
        c.Surface = new Dictionary<int, float>();
        var centre = new HashSet<int>(c.Vertices);
        for (int i = 1; i < chain.Count; i++)
        {
            var a = chain[i - 1]; var b = chain[i];
            if (width <= 0 || (original[a.Vertex] <= a.Floor && original[b.Vertex] <= b.Floor)) continue;
            double edge = Angle(a.Vertex, b.Vertex) * radius;
            var visited = new HashSet<int>(); var queue = new Queue<int>();
            queue.Enqueue(a.Vertex); queue.Enqueue(b.Vertex);
            Vector3 start = graph.CellDirections[a.Vertex], end = graph.CellDirections[b.Vertex];
            Vector3 delta = end - start;
            while (queue.Count > 0)
            {
                int v = queue.Dequeue(); if (!visited.Add(v)) continue;
                Vector3 point = graph.CellDirections[v];
                float t = Mathf.Clamp01(Vector3.Dot(point - start, delta) / delta.sqrMagnitude);
                double distance = Vector3.Distance(point, (start + delta * t).normalized) * radius;
                if (distance > width + edge) continue;
                if (distance < width && !centre.Contains(v))
                {
                    double x = distance / width, falloff = 1 - x * x * (3 - 2 * x);
                    double cut = ((original[a.Vertex] - a.Floor) * (1 - t) + (original[b.Vertex] - b.Floor) * t) * falloff;
                    if (cut > step * .01)
                    {
                        if (protectedLake[v]) { c.Rejection = "lake"; return; }
                        // Retained water and already-subsea land are never lowered, even at valley margins.
                        if (!ocean[v])
                        {
                            if (protectedSubsea[v]) { c.Rejection = "below-sea-level"; return; }
                            float h = (float)(original[v] - cut);
                            if (h < seaFloor) { c.Rejection = "below-sea-level"; return; }
                            if (!c.Surface.TryGetValue(v, out float old) || h < old) c.Surface[v] = h;
                            if (c.Surface.Count > MaximumAffectedVertices) { c.Rejection = "footprint-limit"; return; }
                        }
                    }
                }
                for (int k = 0; k < graph.NeighborCounts[v]; k++) queue.Enqueue(graph.Neighbors6[v * 6 + k]);
            }
        }
        foreach (var l in chain)
        {
            float h = (float)l.Floor;
            if (h < original[l.Vertex]) c.Surface[l.Vertex] = h;
        }
        var facets = new HashSet<int>(); double sum = 0;
        foreach (var pair in c.Surface)
        {
            double cut = original[pair.Key] - pair.Value;
            c.MaximumCut = Math.Max(c.MaximumCut, cut); sum += cut; AddFacets(pair.Key, facets);
        }
        c.AffectedVertices = c.Surface.Count; c.AffectedFacets = facets.Count; c.MeanCut = sum / Math.Max(1, c.AffectedVertices);
        if (c.MaximumCut > depth || c.MeanCut > depth) { c.Rejection = "too-deep"; return; }
        if (c.Length > maxLength) { c.Rejection = "search-distance-limit"; return; }
        if (c.AffectedVertices > MaximumAffectedVertices || c.AffectedFacets > MaximumAffectedFacets)
        { c.Rejection = "footprint-limit"; return; }
        if (c.AffectedVertices == 0) { c.Rejection = "no-target"; return; }
        // Preserve a descending connection along the existing target continuation as well.
        c.Continuation = escape.Path;
        if (!ProfileValid(c, null)) c.Rejection = "conflict";
    }

    private bool ProfileValid(Candidate c, Dictionary<int, float> other)
    {
        double Height(int v)
        {
            double h = c.Surface.TryGetValue(v, out float own) ? own : original[v];
            return other != null && other.TryGetValue(v, out float additional) ? Math.Min(h, additional) : h;
        }
        for (int i = 1; i < c.Vertices.Length; i++)
        {
            int a = c.Vertices[i - 1], b = c.Vertices[i];
            if (Height(b) > Height(a) * Math.Cos(Angle(a, b)) - step * .5) return false;
        }
        bool Descent(TinPoint[] path)
        {
            double lowest = double.PositiveInfinity;
            foreach (var p in path)
            {
                double h = p.A * Height(tin.BasinVertex(p.Triangle, 0)) + p.B * Height(tin.BasinVertex(p.Triangle, 1)) + p.C * Height(tin.BasinVertex(p.Triangle, 2));
                if (h > lowest + tin.Epsilon) return false;
                lowest = Math.Min(lowest, h);
            }
            return true;
        }
        if (c.Continuation != null && !Descent(c.Continuation)) return false;
        if (c.Incoming != null && c.Incoming.Any(path => !Descent(path))) return false;
        return true;
    }

    private double Angle(int a, int b)
    {
        // Chord/asin is stable for adjacent vertices at high subdivisions.
        return 2 * Math.Asin(Math.Min(1, Vector3.Distance(graph.CellDirections[a], graph.CellDirections[b]) * .5));
    }
    private void AddFacets(int vertex, HashSet<int> facets)
    {
        for (int i = tin.BasinIncidentStart(vertex); i < tin.BasinIncidentEnd(vertex); i++) facets.Add(tin.BasinIncidentTriangle(i));
    }
}
