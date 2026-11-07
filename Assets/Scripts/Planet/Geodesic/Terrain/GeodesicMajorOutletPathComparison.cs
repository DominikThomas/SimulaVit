using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public sealed partial class GeodesicValleyCarving
{
    internal sealed class OutletPathComparison
    {
        internal int DepartureIndex = -1, FirstEditedSample = -1, LastChannelSample = -1;
        internal int PlannedNextVertex = -1, PlannedIndex = -1, DepartureVertex = -1;
        internal TinPoint? Departure, ActualNext, PlannedNext;
        internal double ActualSlope = double.NaN, PlannedSlope = double.NaN;
        internal string Summary, Details;
    }

    internal static OutletPathComparison CompareOutletPath(GeodesicTinHydrology original, GeodesicTinHydrology proposed,
        TinPoint[] actual, int[] centreline, IReadOnlyDictionary<int, float> edits, bool detailsRequested = true)
    {
        var report = new OutletPathComparison();
        var indices = new Dictionary<int, int>();
        for (int i = 0; i < centreline.Length; i++) indices.Add(centreline[i], i);
        var details = detailsRequested ? new StringBuilder("Proposed TIN trace in flow order\n") : null;
        double lastProgress = double.NaN;
        for (int i = 0; i < actual.Length; i++)
        {
            var point = actual[i];
            bool onChannel = TryProgress(point, out double progress);
            if (onChannel) report.LastChannelSample = i;
            // A facet excursion can drain back into the channel. Keep the
            // departure that still matters after the last recovered junction.
            if (onChannel && report.PlannedIndex >= 0 && progress >= report.PlannedIndex - 1e-9)
            {
                report.DepartureIndex = report.PlannedNextVertex = report.PlannedIndex = report.DepartureVertex = -1;
                report.Departure = report.ActualNext = report.PlannedNext = null;
                report.ActualSlope = report.PlannedSlope = double.NaN;
            }
            if (detailsRequested && report.FirstEditedSample < 0 && Enumerable.Range(0, 3).Any(k => edits.ContainsKey(proposed.BasinVertex(point.Triangle, k))))
                report.FirstEditedSample = i;
            if (report.DepartureIndex < 0 && !double.IsNaN(lastProgress) && lastProgress < centreline.Length - 1 &&
                (!onChannel || progress < lastProgress - 1e-9 ||
                    Math.Ceiling(progress - 1e-9) > Math.Floor(lastProgress + 1e-9) + 1))
            {
                var departure = actual[i - 1];
                int plannedIndex = (int)Math.Floor(lastProgress + 1e-10) + 1;
                int vertex = centreline[plannedIndex];
                int previous = centreline[plannedIndex - 1];
                int facet = FindEdgeFacet(previous, vertex);
                var expected = proposed.BasinPoint(facet, vertex);
                report.DepartureIndex = i;
                report.Departure = departure; report.ActualNext = point; report.PlannedNext = expected;
                report.PlannedNextVertex = vertex;
                report.PlannedIndex = plannedIndex;
                if (Math.Abs(lastProgress - (plannedIndex - 1)) < 1e-9) report.DepartureVertex = previous;
                report.ActualSlope = proposed.OutletSlope(departure, point);
                report.PlannedSlope = proposed.OutletSlope(departure, expected);
            }
            lastProgress = onChannel ? progress : double.NaN;
            details?.AppendLine(FormattableString.Invariant(
                $"{i} {Describe(point)} centrelineProgress={(onChannel ? progress : double.NaN):R}"));
        }
        if (!detailsRequested) return report;
        var baseline = original.TraceLakeOutlet(actual[0], null, out var baselineEnd);
        details.AppendLine("Candidate centreline in flow order");
        for (int i = 0; i < centreline.Length; i++)
        {
            int v = centreline[i];
            int facet = i + 1 < centreline.Length ? FindEdgeFacet(v, centreline[i + 1]) :
                proposed.BasinIncidentTriangle(proposed.BasinIncidentStart(v));
            details.AppendLine($"{i} vertex={v} {Describe(proposed.BasinPoint(facet, v))}");
        }
        details.AppendLine($"Original TIN trace in flow order ({baselineEnd})");
        for (int i = 0; i < baseline.Length; i++) details.AppendLine($"{i} {Describe(baseline[i])}");
        string branch = report.Departure.HasValue ? FormattableString.Invariant(
            $" from={Describe(report.Departure.Value)} actual={Describe(report.ActualNext.Value)} intendedVertex={report.PlannedNextVertex} intended={Describe(report.PlannedNext.Value)} actualSlope={report.ActualSlope:R} intendedSlope={report.PlannedSlope:R}") : "";
        report.Summary = $"firstOffChannelSample={report.DepartureIndex} firstEditedFacetSample={report.FirstEditedSample} " +
            $"lastOnChannelSample={report.LastChannelSample} originalTerminal={FormatPoint(baseline[baseline.Length - 1])} " +
            $"originalTermination={baselineEnd} proposedTerminal={FormatPoint(actual[actual.Length - 1])}{branch}";
        report.Details = details.ToString();
        return report;

        bool TryProgress(TinPoint p, out double value)
        {
            value = 0;
            int min = int.MaxValue, max = -1, active = 0;
            for (int k = 0; k < 3; k++)
            {
                double weight = k == 0 ? p.A : k == 1 ? p.B : p.C;
                if (weight <= 1e-10) continue;
                if (!indices.TryGetValue(proposed.BasinVertex(p.Triangle, k), out int index)) return false;
                min = Math.Min(min, index); max = Math.Max(max, index);
                value += index * weight; active++;
            }
            return active > 0 && active <= 2 && max - min <= 1;
        }

        int FindEdgeFacet(int a, int b) => OutletEdgeFacet(proposed, a, b);

        string Describe(TinPoint p)
        {
            var corners = Enumerable.Range(0, 3).Select(k =>
            {
                int v = proposed.BasinVertex(p.Triangle, k);
                return FormattableString.Invariant($"{v}:{original.VisibleHeight[v]:R}->{proposed.VisibleHeight[v]:R}");
            });
            return FormattableString.Invariant(
                $"{FormatPoint(p)} height={original.Height(p):R}->{proposed.Height(p):R} radius={original.CompletedVisibleRadius(p):R}->{proposed.CompletedVisibleRadius(p):R} vertices=[{string.Join(",", corners)}]");
        }
    }

    private static int OutletEdgeFacet(GeodesicTinHydrology tin, int a, int b)
    {
        for (int i = tin.BasinIncidentStart(a); i < tin.BasinIncidentEnd(a); i++)
        {
            int facet = tin.BasinIncidentTriangle(i);
            for (int k = 0; k < 3; k++) if (tin.BasinVertex(facet, k) == b) return facet;
        }
        throw new InvalidOperationException("Outlet centreline vertices do not share an edge.");
    }

    internal static string DescribeOutletPonding(GeodesicTinHydrology tin, GeodesicTinBasins basins,
        TinPoint terminal, int[] components, int primary)
    {
        var basin = basins.Find(terminal);
        if (basin == null || !basin.HasEscape) return "ponding=first-spill-unresolved";
        double volume = EstimateSpillStorage(tin, basin, out bool clippedByRetainedOcean);
        var chain = new List<string>();
        var visited = new HashSet<int>();
        bool exposedGradeVerified = true;
        for (var current = basin; current != null && visited.Add(current.Id);)
        {
            chain.Add(FormattableString.Invariant($"{current.Id}@{current.SpillElevation:R}"));
            if (!current.HasEscape || current.DownstreamTriangle < 0 || current.DownstreamVertex < 0)
            { chain.Add("no-downstream-witness"); break; }
            var path = tin.TraceLakeOutlet(tin.BasinPoint(current.DownstreamTriangle, current.DownstreamVertex), null, out var end);
            if (tin.FindExposedOutletRise(path, out int failedStep, out _))
            {
                exposedGradeVerified = false;
                chain.Add($"exposed-grade-unverified({FormatPoint(path[failedStep - 1])} to {FormatPoint(path[failedStep])})");
            }
            var destination = new TinParcelDestination(path[path.Length - 1], end);
            if (PrimaryOcean(tin, components, primary, destination))
            { chain.Add(exposedGradeVerified ? "primary-ocean-witness" : "primary-contact-grade-unverified"); break; }
            if (!GeodesicOceanDrainagePlan.Closed(end))
            { chain.Add(end.ToString()); break; }
            current = basins.Find(destination.Point);
            if (current == null) chain.Add("unmatched-closed-terminal");
            else if (visited.Contains(current.Id)) chain.Add("cycle");
        }
        return FormattableString.Invariant(
            $"ponding=geometric-only basin={basin.Id} floor={basin.FloorElevation:R} firstSpill={basin.SpillElevation:R} spillPoint={FormatPoint(basin.Spill.Value)} headAboveFloor={basin.MaximumDepth:R} estimatedStorageToFirstSpillPlanetUnitsCubed={volume:R} retainedOceanFacetsExcluded={clippedByRetainedOcean} waterBalance=unverified downstream=[{string.Join(" -> ", chain)}]");
    }

    internal static double EstimateSpillStorage(GeodesicTinHydrology tin, TinBasinDescriptor basin, out bool retainedOceanExcluded)
    {
        retainedOceanExcluded = false;
        if (!basin.HasEscape) return double.NaN;
        double volume = 0;
        foreach (int facet in basin.MemberTriangles)
        {
            var polygon = GeodesicTinLakeGeometry.ClipTriangle(tin, facet, basin.SpillElevation);
            if (polygon.Length == 0) continue;
            if (tin.BasinFacetIntersectsOcean(facet)) { retainedOceanExcluded = true; continue; }
            // Approximate shallow storage using the clipped scalar TIN footprint.
            // Linear depth integrates to the mean of the three fan-corner depths.
            var p = Surface(polygon[0]);
            double first = Depth(polygon[0]);
            for (int i = 1; i + 1 < polygon.Length; i++)
            {
                double area = Vector3.Cross(Surface(polygon[i]) - p, Surface(polygon[i + 1]) - p).magnitude * .5;
                volume += area * (first + Depth(polygon[i]) + Depth(polygon[i + 1])) / 3;
            }
        }
        return volume;

        Vector3 Surface(TinPoint p) => tin.BasinOnSphere ? tin.Direction(p) * (float)basin.SpillElevation : tin.Direction(p);
        double Depth(TinPoint p) => Math.Max(0, basin.SpillElevation - tin.Height(p));
    }
}
