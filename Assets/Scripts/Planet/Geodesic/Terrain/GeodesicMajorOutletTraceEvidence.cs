using System;
using System.Collections.Generic;

public sealed partial class GeodesicValleyCarving
{
    internal sealed class MajorOutletTraceFailure
    {
        internal string Reason, SourceRole;
        internal TinPoint Source, Start, End, Terminal;
        internal TinPoint[] Trace;
        internal TinTermination Termination;
        internal int OceanComponent = -1, SampleIndex = -1, VerificationPass;
        internal bool StartFacetEdited, EndFacetEdited, HasSegment;
        internal double BeforeStart, BeforeEnd, AfterStart, AfterEnd, LowestRadius, Epsilon, LowestHydrology = double.NaN;
        internal double BeforeSegmentMinimum, AfterSegmentMinimum;
        internal OutletPathComparison PathComparison;
        internal string ProposedPonding;

        internal string Summary() => FormattableString.Invariant(
            $"verificationPass={VerificationPass} sourceRole={SourceRole} failure={Reason} source={FormatPoint(Source)} terminal={FormatPoint(Terminal)} termination={Termination} component={OceanComponent} samples={Trace.Length} hasSegment={HasSegment} sampleIndex={SampleIndex} segmentStart={FormatPoint(Start)} segmentEnd={FormatPoint(End)} editedFacets={StartFacetEdited}/{EndFacetEdited} beforeRadius={BeforeStart:R}/{BeforeEnd:R} afterRadius={AfterStart:R}/{AfterEnd:R} beforeSegmentMinimum={BeforeSegmentMinimum:R} afterSegmentMinimum={AfterSegmentMinimum:R} lowestRadius={LowestRadius:R} lowestHydrology={LowestHydrology:R} riseAboveRunningMinimum={AfterEnd - LowestRadius:R} epsilon={Epsilon:R}");
    }

    internal static string FormatPoint(TinPoint p) => FormattableString.Invariant($"{p.Triangle}({p.A:R},{p.B:R},{p.C:R})");

    internal static MajorOutletTraceFailure InspectMajorOutletTrace(GeodesicTinHydrology original,
        GeodesicTinHydrology proposed, TinPoint source, int[] components, int primary, HashSet<int> editedFacets) =>
        InspectMajorOutletTrace(original, proposed, source, components, primary, editedFacets, out _, out _);

    private static MajorOutletTraceFailure InspectMajorOutletTrace(GeodesicTinHydrology original,
        GeodesicTinHydrology proposed, TinPoint source, int[] components, int primary, HashSet<int> editedFacets,
        out TinPoint[] trace, out TinTermination termination)
    {
        var tracerFailure = default(GeodesicTinHydrology.TraceFailure);
        trace = proposed.TraceLakeOutlet(source, null, out termination, failure: error => tracerFailure = error);
        int index = trace.Length - 1;
        TinPoint terminal = trace[index];
        bool retainedBoundary = GeodesicOceanDrainagePlan.ConfirmedOcean(proposed, new TinParcelDestination(terminal, termination));
        int component = retainedBoundary ? OceanComponentAt(proposed, components, terminal) : -1;
        TinPoint start = trace[Math.Max(0, index - 1)], end = terminal;
        bool hasSegment = false;
        double low = double.NaN;
        string reason;
        if (tracerFailure.Kind != GeodesicTinHydrology.TraceFailureKind.None)
        {
            reason = "tin-trace-" + tracerFailure.Kind;
            start = tracerFailure.Start;
            end = tracerFailure.End;
            low = tracerFailure.LowestVisibleRadius;
            hasSegment = !start.Equals(end);
            index = -1; // Rejected samples are not appended to the trace.
        }
        else if (trace.Length < 2)
            reason = "stationary-" + termination;
        else if (!retainedBoundary)
            reason = termination == TinTermination.OceanCoast ? "unwitnessed-retained-boundary" : "terminal-" + termination;
        else if (component != primary)
            reason = component < 0 ? "unclassified-ocean-boundary" : "wrong-ocean-component";
        else if (proposed.FindExposedOutletRise(trace, out index, out low))
        {
            reason = "strict-visible-grade";
            start = trace[index - 1];
            end = trace[index];
            hasSegment = true;
        }
        else return null;

        return new MajorOutletTraceFailure {
            Reason = reason, Source = source, Start = start, End = end, Terminal = terminal, Termination = termination,
            Trace = trace, OceanComponent = component, SampleIndex = index, HasSegment = hasSegment,
            StartFacetEdited = editedFacets.Contains(start.Triangle), EndFacetEdited = editedFacets.Contains(end.Triangle),
            BeforeStart = original.CompletedVisibleRadius(start), BeforeEnd = original.CompletedVisibleRadius(end),
            AfterStart = proposed.CompletedVisibleRadius(start), AfterEnd = proposed.CompletedVisibleRadius(end),
            BeforeSegmentMinimum = start.Triangle == end.Triangle ? original.MinimumOutletSegmentRadius(start, end) : double.NaN,
            AfterSegmentMinimum = start.Triangle == end.Triangle ? proposed.MinimumOutletSegmentRadius(start, end) : double.NaN,
            LowestRadius = low, LowestHydrology = tracerFailure.Kind == GeodesicTinHydrology.TraceFailureKind.None
                ? double.NaN : tracerFailure.LowestHydrology, Epsilon = proposed.Epsilon
        };
    }
}
