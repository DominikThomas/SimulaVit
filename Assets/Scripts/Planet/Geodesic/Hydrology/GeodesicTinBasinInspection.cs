using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public enum TinBasinDebugSelection { Selected, Qualifying, Largest, Deepest, ClosedTermination }

/// <summary>Read-only selection and reporting shared by editor inspection and validation.</summary>
public static class GeodesicTinBasinInspection
{
    public static string UnmatchedClosedReport(GeodesicTinBasins topology, GeodesicTinHydrology tin)
    {
        var text = new StringBuilder("[GeodesicTinLakes] Unmatched closed termination audit\n");
        for (int i = 0; i < tin.Networks.Count; i++)
        {
            var n = tin.Networks[i];
            if (n.Termination != TinTermination.TrueClosedBasin || n.Samples == null || n.Samples.Length == 0) continue;
            var p = n.Samples[n.Samples.Length - 1]; if (topology.Find(p) != null) continue;
            text.AppendLine($"network={i} source={n.SourceTriangle} triangle={p.Triangle} bary=({p.A:R},{p.B:R},{p.C:R}) height={tin.Height(p):R} ocean={tin.IsOcean(p)} epsilon={tin.Epsilon:R}");
            var nearby = new HashSet<int> { p.Triangle };
            for (int k = 0; k < 3; k++)
            {
                int v = tin.BasinVertex(p.Triangle, k);
                text.AppendLine($" corner={k} vertex={v} height={tin.HydrologyHeight[v]:R} deltaFromEndpoint={tin.HydrologyHeight[v] - tin.Height(p):R}");
                for (int j = tin.BasinIncidentStart(v); j < tin.BasinIncidentEnd(v); j++) nearby.Add(tin.BasinIncidentTriangle(j));
                double weight = k == 0 ? p.A : k == 1 ? p.B : p.C;
                if (weight >= 1 - 1e-10)
                {
                    var oceanFaces = new List<int>(); double minimumNeighbor = double.PositiveInfinity;
                    for (int j = tin.BasinIncidentStart(v); j < tin.BasinIncidentEnd(v); j++)
                    {
                        int t = tin.BasinIncidentTriangle(j);
                        if (tin.IsOcean(tin.BasinPoint(t, v))) oceanFaces.Add(t);
                        for (int corner = 0; corner < 3; corner++)
                        { int u = tin.BasinVertex(t, corner); if (u != v) minimumNeighbor = Math.Min(minimumNeighbor, tin.HydrologyHeight[u]); }
                    }
                    text.AppendLine($" endpointVertex={v} retainedOceanIncidentFacets=[{string.Join(",", oceanFaces)}] minimumNeighborHeight={minimumNeighbor:R} minimumNeighborDelta={minimumNeighbor - tin.HydrologyHeight[v]:R}");
                }
            }
            foreach (var b in topology.Basins.Where(b => b.MemberTriangles.Any(t => nearby.Contains(t))))
                text.AppendLine($" nearbyBasin={b.Id} containsCarrier={b.MemberTriangles.Contains(p.Triangle)} floor={b.FloorElevation:R} spill={b.SpillElevation:R} deltaToSpill={tin.Height(p) - b.SpillElevation:R} terminal={b.Terminal}");
        }
        return text.ToString();
    }

    public static string Exclusions(TinBasinDescriptor b)
    {
        var reasons = new List<string>();
        if (b.Terminal) reasons.Add("terminal/unknown-level");
        if (b.SubseaUnclassified) reasons.Add("subsea-unclassified");
        if (b.BelowMinimumDepth) reasons.Add("depth");
        if (b.BelowMinimumArea) reasons.Add("area");
        return reasons.Count == 0 ? "none" : string.Join(",", reasons);
    }

    public static TinBasinDescriptor[] Select(GeodesicTinBasins topology, GeodesicTinHydrology tin,
        TinBasinDebugSelection mode, int id, int count, int networkIndex)
    {
        switch (mode)
        {
            case TinBasinDebugSelection.Selected: return topology.Basins.Where(b => b.Id == id).ToArray();
            case TinBasinDebugSelection.Qualifying: return topology.Basins.Where(b => b.QualifiesForVisibleLake).ToArray();
            case TinBasinDebugSelection.Largest: return topology.Basins.OrderByDescending(b => b.Area).ThenBy(b => b.Id).Take(Math.Max(0, count)).ToArray();
            case TinBasinDebugSelection.Deepest: return topology.Basins.Where(b => b.HasEscape).OrderByDescending(b => b.MaximumDepth).ThenBy(b => b.Id).Take(Math.Max(0, count)).ToArray();
            case TinBasinDebugSelection.ClosedTermination:
                if (networkIndex < 0 || networkIndex >= tin.Networks.Count) return Array.Empty<TinBasinDescriptor>();
                var n = tin.Networks[networkIndex];
                if (n.Termination != TinTermination.TrueClosedBasin || n.Samples == null || n.Samples.Length == 0) return Array.Empty<TinBasinDescriptor>();
                var match = topology.Find(n.Samples[n.Samples.Length - 1]);
                return match == null ? Array.Empty<TinBasinDescriptor>() : new[] { match };
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    public static string Describe(GeodesicTinHydrology tin, TinBasinDescriptor b)
    {
        int Vertex(TinPoint p) => tin.BasinVertex(p.Triangle, p.A == 1 ? 0 : p.B == 1 ? 1 : 2);
        return b + $" escape={b.HasEscape} exclusions={Exclusions(b)} floorTriangle={b.Floor.Triangle} floorVertex={b.Id}" +
            (b.Spill.HasValue ? $" spillTriangle={b.Spill.Value.Triangle} spillVertex={Vertex(b.Spill.Value)}" : " spillTriangle=none spillVertex=none") +
            $" downstreamVertex={b.DownstreamVertex} childrenCanOverlapFootprint=true";
    }

    public static string SelectedReport(GeodesicTinBasins topology, GeodesicTinHydrology tin, int id)
    {
        var b = topology.Basins.FirstOrDefault(x => x.Id == id);
        if (b == null) return $"[GeodesicTinBasinsLive] basin={id} not found";
        var text = new StringBuilder(Describe(tin, b));
        for (int i = 0; i < tin.Networks.Count; i++)
        {
            var n = tin.Networks[i];
            if ((n.Termination != TinTermination.TrueClosedBasin && n.Termination != TinTermination.TinyNoiseDepression) || n.Samples == null || n.Samples.Length == 0) continue;
            var p = n.Samples[n.Samples.Length - 1];
            if (topology.Find(p)?.Id == id) text.AppendLine().Append($"network={i} type={n.Termination} triangle={p.Triangle} barycentric=({p.A:R},{p.B:R},{p.C:R}) direction={tin.Direction(p)} height={tin.Height(p):R}");
        }
        return text.ToString();
    }

    public static string Report(GeodesicTinBasins topology, GeodesicTinHydrology tin)
    {
        var b = topology.Basins;
        var eligible = b.Where(x => !x.Terminal && !x.SubseaUnclassified).ToArray();
        var text = new StringBuilder("[GeodesicTinBasinsLive] ").AppendLine(topology.Summary());
        text.AppendLine($"tooSmallArea={b.Count(x => x.BelowMinimumArea)} failsOnlyDepth={eligible.Count(x => x.BelowMinimumDepth && !x.BelowMinimumArea)} failsOnlyArea={eligible.Count(x => !x.BelowMinimumDepth && x.BelowMinimumArea)} failsBoth={eligible.Count(x => x.BelowMinimumDepth && x.BelowMinimumArea)} terminalUnknownLevel={b.Count(x => x.Terminal)} otherExclusion={b.Count(x => !x.Terminal && x.SubseaUnclassified)}");
        foreach (var mode in new[] { TinBasinDebugSelection.Largest, TinBasinDebugSelection.Deepest })
        {
            text.AppendLine("TOP 10 " + mode);
            foreach (var basin in Select(topology, tin, mode, 0, 10, -1)) text.AppendLine(Describe(tin, basin));
        }
        return text.ToString();
    }

    public static string GeometryChecks(GeodesicTinHydrology tin, TinBasinDescriptor b)
    {
        var members = new HashSet<int>(b.MemberTriangles); var visited = new HashSet<int>(); var queue = new Queue<int>();
        if (members.Count > 0) { int first = b.MemberTriangles[0]; visited.Add(first); queue.Enqueue(first); }
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            for (int k = 0; k < 3; k++) { int n = tin.Neighbors[t * 3 + k]; if (members.Contains(n) && visited.Add(n)) queue.Enqueue(n); }
        }
        int fullOcean = 0, partialOcean = 0; double min = double.PositiveInfinity;
        foreach (int t in members)
        {
            int wet = 0;
            for (int k = 0; k < 3; k++)
            {
                var p = new TinPoint(t, k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0);
                if (tin.IsOcean(p)) wet++;
                min = Math.Min(min, tin.Height(p));
            }
            if (wet == 3) fullOcean++; else if (wet > 0) partialOcean++;
        }
        return $"basin={b.Id} edgeConnected={visited.Count == members.Count} fullyOceanFacets={fullOcean} partialOceanFacets={partialOcean} floorAtFootprintMinimum={Math.Abs(min - b.FloorElevation) <= tin.Epsilon} spillAboveFloor={(!b.HasEscape || b.SpillElevation >= b.FloorElevation)}";
    }
}
