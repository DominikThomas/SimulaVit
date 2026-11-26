#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public sealed partial class GeodesicTinLakeRouting
{
    private void LogSpillTrace(TinLakeFlowNode node, TinPoint[] original, bool hasWitness,
        Func<TinPoint, TinPoint, bool> departure, Func<TinBasinDescriptor, string> describeWorld)
    {
        const int stepLimit = 3;
        const int ownerLimit = 8;
        var basin = node.Basin;
        TinPoint spill = basin.Spill.Value;
        int facet = basin.DownstreamTriangle, vertex = basin.DownstreamVertex;
        float rim = (float)basin.SpillElevation;
        int rimBits = BitConverter.SingleToInt32Bits(rim);
        double rimUlp = Math.Abs((double)BitConverter.Int32BitsToSingle(rimBits + 1) - rim);
        double relativeScale = Math.Max(Math.Abs(basin.SpillElevation), double.Epsilon);
        double visibleTolerance = tin.VisibleBumpTolerance + tin.Epsilon;

        var report = new StringBuilder(FormattableString.Invariant(
            $"[GeodesicOutletTrace] basin={basin.Id} hydrologySubdivision={tin.SubdivisionLevel} vertices={tin.BasinVertexCount} facets={tin.TriangleCount}"));
        report.AppendLine().Append(describeWorld?.Invoke(basin) ?? "world=synthetic-or-unspecified");
        report.AppendLine().Append(FormattableString.Invariant(
            $"floor={basin.FloorElevation:R} rim={basin.SpillElevation:R} depth={basin.MaximumDepth:R} rimFloatUlp={rimUlp:R} depthInRimUlps={basin.MaximumDepth / rimUlp:R} area={basin.Area:R} footprintFacetCount={basin.MemberTriangles.Count}"));
        report.AppendLine().Append(FormattableString.Invariant(
            $"logicalBody=true logicalDescriptorQualified={basin.QualifiesForVisibleLake} logicalBelowMinimumDepth={basin.BelowMinimumDepth} logicalBelowMinimumArea={basin.BelowMinimumArea} subseaUnclassified={basin.SubseaUnclassified}"));
        report.AppendLine().Append(FormattableString.Invariant(
            $"spillVertex={SpillVertex(basin)} validWitness={hasWitness} downstreamFacet={facet} downstreamVertex={vertex} parentBasin={basin.ParentBasinId} externalAuthority={basin.ExternalSource} basinTouchesRetainedOcean={basin.TouchesRetainedOcean} seaLevel={tin.BasinSeaLevel:R}"));
        report.AppendLine().Append(FormattableString.Invariant(
            $"hydrologyTolerance={tin.Epsilon:R} visibleBump={tin.VisibleBumpTolerance:R} visibleTolerance={visibleTolerance:R} flatThreshold={tin.Epsilon * .25d:R}"));
        report.AppendLine().Append(FormattableString.Invariant(
            $"relativeToAbsoluteRim(hydrology,visible,flat)=({tin.Epsilon / relativeScale:R},{visibleTolerance / relativeScale:R},{tin.Epsilon * .25d / relativeScale:R})"));
        report.AppendLine().Append("floor ").Append(Point(basin.Floor));
        report.AppendLine().Append("spill ").Append(Point(spill));

        var incident = tin.SpillIncidentFacets(spill);
        foreach (int t in incident.Take(ownerLimit))
            report.AppendLine().Append(tin.DescribeOutletFacet(t)).Append(' ').Append(Owners(tin.TransferLakePoint(spill, t)));
        if (incident.Count > ownerLimit)
            report.AppendLine().Append("incidentFacetCount=").Append(incident.Count);
        if (facet >= 0 && facet < tin.TriangleCount && vertex >= 0 && vertex < tin.BasinVertexCount)
        {
            TinPoint start = tin.TransferLakePoint(spill, facet), end = tin.BasinPoint(facet, vertex);
            // Failed witness validation can leave a carrier with missing weights.
            if (Math.Abs(start.A + start.B + start.C - 1d) <= 1e-9 && end.A + end.B + end.C == 1d)
                report.AppendLine().Append("witness edge ").Append(Candidate(start, end));
            else
                report.AppendLine().Append("witness transfer weights ").Append(
                    FormattableString.Invariant($"start=({start.A:R},{start.B:R},{start.C:R}) end=({end.A:R},{end.B:R},{end.C:R})"));
        }

        const int candidateLimit = 24;
        int candidateCount = 0;
        tin.VisitSpillDescentCandidates(spill, (a, b) =>
        {
            if (candidateCount++ < candidateLimit)
                report.AppendLine().Append("initial descent candidate ").Append(Candidate(a, b));
        });
        report.AppendLine().Append("initialDescentCandidateCount=").Append(candidateCount);

        var failure = default(GeodesicTinHydrology.TraceFailure);
        int selections = 0;
        var replay = tin.TraceLakeOutlet(spill, departure, out var replayTermination,
            failure: value => failure = value, observeSelection: (from, start, end, flat) =>
            {
                if (selections++ >= stepLimit) return;
                report.AppendLine().Append(FormattableString.Invariant(
                    $"selectedTransition={selections} kind={(flat ? "flat" : "descent")} facetHandoff={from.Triangle != start.Triangle} departureAllowed={(selections != 1 || departure(start, end))} coastAtStart={tin.IsOcean(start)} coastAtTarget={tin.IsOcean(end)}"));
                report.AppendLine().Append("previous ").Append(Point(from));
                report.AppendLine().Append(Segment(start, end));
            });

        for (int i = 1; i < Math.Min(replay.Length, 2 * stepLimit + 1); i++)
            report.AppendLine().Append("acceptedSample=").Append(i).Append(' ').Append(Point(replay[i]));

        bool replayMatches = node.OutletTermination == replayTermination && original.SequenceEqual(replay);
        report.AppendLine().Append(FormattableString.Invariant(
            $"termination={node.OutletTermination} replayTermination={replayTermination} replayMatches={replayMatches} selectedTransitions={selections} acceptedSamples={replay.Length} failure={failure.Kind}"));
        report.AppendLine().Append(FormattableString.Invariant(
            $"failsBeforeFirstDownstreamStep={!node.DownstreamSample.HasValue} downstreamSampleInSourceLake={node.DownstreamSample.HasValue && Inside(node.DownstreamSample.Value, basin.Id)}"));
        if (failure.Kind != GeodesicTinHydrology.TraceFailureKind.None)
        {
            report.AppendLine().Append(FormattableString.Invariant(
                $"runningLowHydrology={failure.LowestHydrology:R} runningLowVisible={failure.LowestVisibleHeight:R} runningLowRadius={failure.LowestVisibleRadius:R}"));
            report.AppendLine().Append("failed transition ").Append(Segment(failure.Start, failure.End));
            if (failure.Kind == GeodesicTinHydrology.TraceFailureKind.DepartureRejected)
                report.AppendLine().Append(Departure(failure.Start, failure.End));
            if (failure.Kind == GeodesicTinHydrology.TraceFailureKind.HydrologyGrade ||
                failure.Kind == GeodesicTinHydrology.TraceFailureKind.VisibleGrade)
                report.AppendLine().Append(tin.DescribeOutletGradeChecks(failure));
        }
        else if (replay.Length != 0)
        {
            report.AppendLine().Append("final accepted point ").Append(Point(replay[replay.Length - 1]));
        }
        if (node.DownstreamSample.HasValue)
            report.AppendLine().Append("first downstream sample ").Append(Point(node.DownstreamSample.Value));
        if (FirstEntry(replay, out var entry))
            report.AppendLine().Append("first wet entry basin=").Append(entry.BasinId).Append(' ').Append(Point(entry.Point));
        Debug.Log(report.ToString());

        string Point(TinPoint p) => tin.DescribeOutletPoint(p) + " " + Owners(p);

        string Owners(TinPoint p)
        {
            int[] wet = wetFacets.TryGetValue(p.Triangle, out var owners)
                ? owners.Where(owner => tin.Height(p) < owner.Basin.SpillElevation).Select(owner => owner.BasinId).Take(ownerLimit + 1).ToArray()
                : Array.Empty<int>();
            return $"sourceFootprint={basin.MemberTriangles.Contains(p.Triangle)} sourceWet={Inside(p, basin.Id)} " +
                $"wetFacetOwners=[{string.Join(",", owners?.Take(ownerLimit).Select(owner => owner.BasinId) ?? Enumerable.Empty<int>())}]" +
                $"{(owners?.Count > ownerLimit ? "+" : "")} wetPointOwners=[{string.Join(",", wet.Take(ownerLimit))}]{(wet.Length > ownerLimit ? "+" : "")}";
        }

        string Segment(TinPoint a, TinPoint b) =>
            $"start=[{Point(a)}] end=[{Point(b)}] {tin.DescribeOutletExtrema(a, b)}";

        string Departure(TinPoint a, TinPoint b) => FormattableString.Invariant(
            $"departureAllowed={departure(a, b)} hasWitness={hasWitness} onWitnessFacet={a.Triangle == facet} sourceLakeContainsMidpoint={Inside(Mix(a, b, .5), basin.Id)} {Owners(Mix(a, b, .5))}");

        string Candidate(TinPoint a, TinPoint b) => FormattableString.Invariant(
            $"{Departure(a, b)} scalarSlope={tin.OutletSlope(a, b):R} previewGrade={tin.PreviewSpillGrade(spill, a, b)} {Segment(a, b)}");
    }
}

public sealed partial class GeodesicTinHydrology
{
    internal IReadOnlyList<int> SpillIncidentFacets(TinPoint spill)
    {
        var incident = new List<int>(8);
        ContainingFaces(spill, incident);
        return incident;
    }

    internal void VisitSpillDescentCandidates(TinPoint spill, Action<TinPoint, TinPoint> visit)
    {
        // Examine every feasible gradient or constrained edge before selection or grade.
        DownhillStep(spill, new List<int>(8), out _, out _, (a, b) =>
        {
            visit(a, b);
            return false;
        });
    }

    internal TraceFailureKind PreviewSpillGrade(TinPoint spill, TinPoint start, TinPoint end)
    {
        double lowH = Height(spill), lowV = Height(spill, true), lowR = CompletedVisibleRadius(spill);
        if (IsOcean(start))
            end = start;
        else if (IsOcean(end))
            end = Coast(start, end);
        return AcceptStep(start, end, ref lowH, ref lowV, ref lowR, false, false);
    }

    internal string DescribeOutletFacet(int facet)
    {
        Face face = faces[facet];
        var output = new StringBuilder(FormattableString.Invariant(
            $"incidentFacet={facet} valid={face.Valid} flat={face.Flat} hydrologyRange={face.Min:R}..{face.Max:R} gradientBaryRates=({face.DA:R},{face.DB:R},{face.DC:R}) downhill={Cartesian(face.Downhill)} flatExitEdge={flatNextEdge[facet]} flatExitDistance={flatDistance[facet]} oceanRetained={face.RetainedOcean}"));
        for (int k = 0; k < 3; k++)
        {
            int vertex = BasinVertex(facet, k);
            output.Append(FormattableString.Invariant(
                $" v{vertex}[directionVertex={Cartesian(Vertex(facet, k))} completedVertex={Cartesian(RenderVertex(facet, k))} hydrology={Height(facet, k):R} visible={Height(facet, k, true):R} retained={BasinVertexRetained(vertex)} neighbor={Neighbors[facet * 3 + k]}]"));
        }
        return output.ToString();
    }

    internal string DescribeOutletPoint(TinPoint p)
    {
        D3 position = Position(p);
        D3 chord = RenderVertex(p.Triangle, 0) * p.A + RenderVertex(p.Triangle, 1) * p.B + RenderVertex(p.Triangle, 2) * p.C;
        D3 surface = spherical ? position * (1d / D3.Dot(faces[p.Triangle].VisiblePlane, position)) : chord;
        return FormattableString.Invariant(
            $"facet={p.Triangle} bary=({p.A:R},{p.B:R},{p.C:R}) directionChord={Cartesian(position)} completedChord={Cartesian(chord)} completedChordMagnitude={chord.Length:R} surface={Cartesian(surface)} hydrology={Height(p):R} visibleHeight={Height(p, true):R} surfaceRadius={CompletedVisibleRadius(p):R} retained={BasinFacetRetained(p.Triangle)} ocean={IsOcean(p)}");
    }

    internal string DescribeOutletExtrema(TinPoint a, TinPoint b)
    {
        if (a.Triangle != b.Triangle) return "crossFacet=true";
        double ha = Height(a), hb = Height(b), va = Height(a, true), vb = Height(b, true);
        double ra = CompletedVisibleRadius(a), rb = CompletedVisibleRadius(b), minimum = MinimumVisibleRadius(a, b);
        D3 start = Position(a), end = Position(b);
        double angle = spherical
            ? Math.Atan2(D3.Cross(start, end).Length, D3.Dot(start, end))
            : double.NaN;
        // The projection of the direction segment stays on one straight rendered segment.
        // Its greatest radius is at an endpoint. The least can occur inside.
        return FormattableString.Invariant(
            $"directionDelta={Cartesian(end - start)} angleRadians={angle:R} hydrologyMinMax=({Math.Min(ha, hb):R},{Math.Max(ha, hb):R}) visibleMinMax=({Math.Min(va, vb):R},{Math.Max(va, vb):R})") +
            FormattableString.Invariant($" radialMinMax=({minimum:R},{Math.Max(ra, rb):R}) radialMinInterior={minimum < Math.Min(ra, rb)}");
    }

    internal string DescribeOutletGradeChecks(TraceFailure failure)
    {
        // A retained-carrier handoff grades the transferred point against the running
        // minima. The failure records both sides so the carrier change is also visible.
        TinPoint b = failure.End, a = failure.Start.Triangle == b.Triangle ? failure.Start : b;
        double ha = Height(a), hb = Height(b), va = Height(a, true), vb = Height(b, true);
        double ra = CompletedVisibleRadius(a), rb = CompletedVisibleRadius(b);
        double lowH = failure.LowestHydrology, lowV = failure.LowestVisibleHeight, lowR = failure.LowestVisibleRadius;
        double radiusLow = Math.Min(lowR, MinimumVisibleRadius(a, b));
        double tolerance = VisibleBumpTolerance + Epsilon;
        return FormattableString.Invariant(
            $"gradeChecks hydrologyFinite={Finite(ha) && Finite(hb)} hydrologyStartPass={ha <= lowH + Epsilon} hydrologyEndPass={hb <= Math.Min(lowH, ha) + Epsilon}") +
            FormattableString.Invariant(
            $" visibleFinite={Finite(va) && Finite(vb) && Finite(ra) && Finite(rb) && Finite(radiusLow)} visibleLinearPass={Math.Max(va, vb) <= lowV + tolerance}") +
            FormattableString.Invariant(
            $" radialStartPass={ra <= lowR + tolerance} radialEndPass={rb <= radiusLow + tolerance} radialRunningLowWithSegment={radiusLow:R} radialEndRise={rb - radiusLow:R}");
    }

    private static string Cartesian(D3 v) => FormattableString.Invariant($"({v.X:R},{v.Y:R},{v.Z:R})");
}
#endif
