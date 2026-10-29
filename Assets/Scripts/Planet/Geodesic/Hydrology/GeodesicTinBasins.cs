using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Immutable lower-star component at its first merge into an older component.
/// Member triangles are a conservative footprint, not shoreline polygons. IDs are floor vertex IDs.
/// ParentBasinId describes the elder-rule merge tree, not a river receiver.</summary>
public sealed class TinBasinDescriptor
{
    public int Id { get; }
    public TinPoint Floor { get; }
    public double FloorElevation { get; }
    public double SpillElevation { get; }
    public double MaximumDepth => HasEscape ? SpillElevation - FloorElevation : double.NaN;
    public TinPoint? Spill { get; }
    public int DownstreamTriangle { get; }
    public int DownstreamVertex { get; }
    public int ParentBasinId { get; }
    public IReadOnlyList<int> MemberTriangles { get; }
    public double Area { get; }
    public bool HasEscape => Spill.HasValue;
    public bool Terminal => !HasEscape;
    public bool TouchesRetainedOcean { get; }
    public bool BelowMinimumDepth { get; }
    public bool BelowMinimumArea { get; }
    public bool SubseaUnclassified { get; }
    public bool QualifiesForVisibleLake => HasEscape && !BelowMinimumDepth && !BelowMinimumArea && !SubseaUnclassified;

    internal TinBasinDescriptor(int id, TinPoint floor, double floorHeight, double spillHeight,
        TinPoint? spill, int downstreamTriangle, int downstreamVertex, int parent, int[] members,
        double area, bool ocean, bool subsea, double minimumDepth, double minimumArea)
    {
        Id = id; Floor = floor; FloorElevation = floorHeight; SpillElevation = spillHeight;
        Spill = spill; DownstreamTriangle = downstreamTriangle; DownstreamVertex = downstreamVertex;
        ParentBasinId = parent; MemberTriangles = Array.AsReadOnly(members); Area = area;
        TouchesRetainedOcean = ocean; SubseaUnclassified = subsea;
        BelowMinimumDepth = HasEscape && MaximumDepth < minimumDepth;
        BelowMinimumArea = area < minimumArea;
    }
    public override string ToString() => $"[GeodesicTinBasins] id={Id} floor={FloorElevation:G9} spill={SpillElevation:G9} depth={MaximumDepth:G9} area={Area:G9} terminal={Terminal} parent={ParentBasinId} downstreamTriangle={DownstreamTriangle} ocean={TouchesRetainedOcean} candidate={QualifiesForVisibleLake}";
}

/// <summary>Optional topology-only sweep of the active piecewise-linear field. No graph-dependent
/// lake code, route changes, terrain edits or automatic execution. Exact height ties use vertex ID;
/// zero-persistence components are suppressed, but no positive-depth depression is filtered.</summary>
public sealed class GeodesicTinBasins
{
    public IReadOnlyList<TinBasinDescriptor> Basins { get; }
    private readonly GeodesicTinHydrology tin;
    private readonly Dictionary<int, List<TinBasinDescriptor>> byTriangle = new Dictionary<int, List<TinBasinDescriptor>>();
    private GeodesicTinBasins(GeodesicTinHydrology source, List<TinBasinDescriptor> basins)
    {
        tin = source; basins.Sort((a, b) => a.Id.CompareTo(b.Id)); Basins = basins.AsReadOnly();
        foreach (var b in basins)
        foreach (int t in b.MemberTriangles)
        {
            if (!byTriangle.TryGetValue(t, out var list)) byTriangle.Add(t, list = new List<TinBasinDescriptor>());
            list.Add(b);
        }
    }

    public static GeodesicTinBasins Build(GeodesicTinHydrology tin, double minimumAreaFraction = .0001d, double minimumDepth = .005d)
    {
        if (tin == null) throw new ArgumentNullException(nameof(tin));
        if (double.IsNaN(minimumAreaFraction) || double.IsInfinity(minimumAreaFraction) || minimumAreaFraction < 0 ||
            double.IsNaN(minimumDepth) || double.IsInfinity(minimumDepth) || minimumDepth < 0)
            throw new ArgumentOutOfRangeException("Basin thresholds must be finite and nonnegative.");
        int n = tin.BasinVertexCount;
        var parent = new int[n]; var head = new int[n]; var tail = new int[n]; var next = new int[n];
        var floor = new int[n]; var external = new bool[n]; var ocean = new bool[n];
        var point = new TinPoint[n]; var used = new bool[n];
        Array.Fill(parent, -1); Array.Fill(next, -1);
        double totalArea = 0;
        for (int t = 0; t < tin.TriangleCount; t++)
        {
            if (!tin.BasinFaceValid(t)) continue;
            totalArea += tin.LocalArea[t];
            for (int k = 0; k < 3; k++)
            {
                int v = tin.BasinVertex(t, k); var p = tin.BasinPoint(t, v);
                if (!used[v]) { point[v] = p; used[v] = true; }
                ocean[v] |= tin.IsOcean(p);
                if (tin.BasinBoundaryOutflow && (tin.Neighbors[t * 3 + (k + 1) % 3] < 0 || tin.Neighbors[t * 3 + (k + 2) % 3] < 0)) external[v] = true;
            }
        }
        for (int v = 0; v < n; v++) external[v] |= ocean[v];
        var order = Enumerable.Range(0, n).Where(v => used[v]).ToArray();
        Array.Sort(order, (a, b) => { int c = tin.HydrologyHeight[a].CompareTo(tin.HydrologyHeight[b]); return c != 0 ? c : a.CompareTo(b); });
        var result = new List<TinBasinDescriptor>();
        int Root(int v) { int r = v; while (parent[r] != r) r = parent[r]; while (v != r) { int p = parent[v]; parent[v] = r; v = p; } return r; }
        bool Older(int a, int b) => external[a] != external[b] ? external[a] :
            tin.HydrologyHeight[floor[a]] != tin.HydrologyHeight[floor[b]] ? tin.HydrologyHeight[floor[a]] < tin.HydrologyHeight[floor[b]] : floor[a] < floor[b];
        void Record(int r, int saddle, int target, int sideTriangle, int sideVertex)
        {
            double level = saddle < 0 ? double.NaN : tin.HydrologyHeight[saddle];
            if (external[r] || (saddle >= 0 && level <= tin.HydrologyHeight[floor[r]])) return;
            var members = new HashSet<int>(); bool touches = saddle >= 0 && tin.IsOcean(tin.BasinPoint(sideTriangle, saddle));
            for (int v = head[r]; v >= 0; v = next[v])
            {
                if (saddle >= 0 && tin.HydrologyHeight[v] >= level) continue;
                for (int i = tin.BasinIncidentStart(v); i < tin.BasinIncidentEnd(v); i++)
                {
                    int t = tin.BasinIncidentTriangle(i);
                    if (!tin.BasinFaceValid(t)) continue;
                    bool allOcean = true;
                    for (int k = 0; k < 3; k++) { bool wet = tin.IsOcean(tin.BasinPoint(t, tin.BasinVertex(t, k))); touches |= wet; allOcean &= wet; }
                    if (!allOcean) members.Add(t);
                }
            }
            int[] footprint = members.OrderBy(t => t).ToArray(); double area = 0;
            foreach (int t in footprint) area += tin.LocalArea[t];
            int f = floor[r];
            result.Add(new TinBasinDescriptor(f, point[f], tin.HydrologyHeight[f], level,
                saddle < 0 ? (TinPoint?)null : tin.BasinPoint(sideTriangle, saddle), sideVertex == saddle ? -1 : sideTriangle, sideVertex,
                target >= 0 && !external[target] ? floor[target] : -1, footprint, area, touches,
                tin.VisibleHeight[f] <= tin.BasinSeaLevel && !ocean[f], minimumDepth, totalArea * minimumAreaFraction));
        }
        foreach (int v in order)
        {
            parent[v] = v; head[v] = tail[v] = floor[v] = v;
            // Process incident triangles in their existing stable mesh order.
            for (int i = tin.BasinIncidentStart(v); i < tin.BasinIncidentEnd(v); i++)
            {
                int t = tin.BasinIncidentTriangle(i); if (!tin.BasinFaceValid(t)) continue;
                for (int k = 0; k < 3; k++)
                {
                    int w = tin.BasinVertex(t, k); if (parent[w] < 0) continue;
                    int a = Root(v), b = Root(w); if (a == b) continue;
                    int winner = Older(a, b) ? a : b, loser = winner == a ? b : a;
                    // Find an actual incident edge into the winning component BEFORE union.
                    // The current triangle can lie on the losing side when v already belongs
                    // to the elder component, so it is not necessarily a downstream witness.
                    int sideTriangle = t, sideVertex = v;
                    for (int j = tin.BasinIncidentStart(v); j < tin.BasinIncidentEnd(v); j++)
                    {
                        int candidate = tin.BasinIncidentTriangle(j);
                        if (!tin.BasinFaceValid(candidate)) continue;
                        for (int c = 0; c < 3; c++)
                        {
                            int u = tin.BasinVertex(candidate, c);
                            if (u != v && parent[u] >= 0 && Root(u) == winner &&
                                (sideVertex == v || tin.HydrologyHeight[u] < tin.HydrologyHeight[sideVertex] ||
                                (tin.HydrologyHeight[u] == tin.HydrologyHeight[sideVertex] && u < sideVertex)))
                            { sideTriangle = candidate; sideVertex = u; }
                        }
                    }
                    Record(loser, v, winner, sideTriangle, sideVertex);
                    parent[loser] = winner; next[tail[winner]] = head[loser]; tail[winner] = tail[loser];
                }
            }
        }
        foreach (int v in order) if (parent[v] == v) Record(v, -1, -1, -1, -1);
        return new GeodesicTinBasins(tin, result);
    }

    /// <summary>Choose the smallest containing sublevel component. A facet may occur in several
    /// nested footprints; elevation excludes portions above its spill. Ocean points never match.</summary>
    public TinBasinDescriptor Find(TinPoint p)
    {
        if (tin.IsOcean(p) || !byTriangle.TryGetValue(p.Triangle, out var list)) return null;
        double height = tin.Height(p);
        return list.Where(b => height >= b.FloorElevation - tin.Epsilon && (!b.HasEscape || height < b.SpillElevation))
            .OrderBy(b => b.HasEscape ? b.SpillElevation : double.PositiveInfinity).ThenBy(b => b.Id).FirstOrDefault();
    }

    public IEnumerable<string> TerminationDetails()
    {
        foreach (var network in tin.Networks)
        {
            if (network.Termination != TinTermination.TrueClosedBasin && network.Termination != TinTermination.TinyNoiseDepression) continue;
            var b = network.Samples != null && network.Samples.Length > 0 ? Find(network.Samples[network.Samples.Length - 1]) : null;
            yield return $"[GeodesicTinBasins] source={network.SourceTriangle} termination={network.Termination} " + (b == null ? "unmatched" : b.ToString());
        }
    }
    public string Summary()
    {
        int closed = 0, matched = 0, noise = 0, noiseMatched = 0;
        foreach (var network in tin.Networks)
        {
            bool found = network.Samples != null && network.Samples.Length > 0 && Find(network.Samples[network.Samples.Length - 1]) != null;
            if (network.Termination == TinTermination.TrueClosedBasin) { closed++; if (found) matched++; }
            if (network.Termination == TinTermination.TinyNoiseDepression) { noise++; if (found) noiseMatched++; }
        }
        var depths = Basins.Where(b => b.HasEscape).Select(b => b.MaximumDepth).ToArray();
        double totalArea = byTriangle.Keys.Sum(t => tin.LocalArea[t]);
        return $"[GeodesicTinBasins] raw={Basins.Count} overflowing={depths.Length} terminal={Basins.Count - depths.Length} oceanAdjacent={Basins.Count(b => b.TouchesRetainedOcean)} candidates={Basins.Count(b => b.QualifiesForVisibleLake)} shallow={Basins.Count(b => b.BelowMinimumDepth)} nonQualifying={Basins.Count(b => !b.QualifiesForVisibleLake)} totalBasinArea={totalArea:G9} summedFootprintArea={Basins.Sum(b => b.Area):G9} largestArea={Basins.Select(b => b.Area).DefaultIfEmpty(0).Max():G9} maxDepth={depths.DefaultIfEmpty(0).Max():G9} meanDepth={depths.DefaultIfEmpty(0).Average():G9} closed={closed} matched={matched} unmatched={closed - matched} tinyNoise={noise} tinyNoiseMatched={noiseMatched}";
    }
}
