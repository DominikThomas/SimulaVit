#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed partial class GeodesicTinLakeRouting
{
    private const string SpillTraceMenu = "Tools/Hydrology/Trace Failed TIN Lake Spills";
    private const string SpillTraceSessionKey = "SimulaVit.Hydrology.TraceFailedTinLakeSpills";
    private static bool SpillTraceEnabled => SessionState.GetBool(SpillTraceSessionKey, false);

    [MenuItem(SpillTraceMenu)]
    private static void ToggleSpillTrace() => SessionState.SetBool(SpillTraceSessionKey, !SpillTraceEnabled);

    [MenuItem(SpillTraceMenu, true)]
    private static bool ValidateSpillTraceMenu()
    {
        Menu.SetChecked(SpillTraceMenu, SpillTraceEnabled);
        return true;
    }

    private void LogSpillTrace(TinBasinDescriptor basin, TinPoint spill, bool hasWitness,
        Func<TinPoint, TinPoint, bool> departure)
    {
        var failure = default(GeodesicTinHydrology.TraceFailure);
        var path = tin.TraceLakeOutlet(spill, departure, out var termination, failure: value => failure = value);
        int facet = basin.DownstreamTriangle;
        int vertex = basin.DownstreamVertex;
        float level = (float)basin.SpillElevation;
        int spillBits = BitConverter.SingleToInt32Bits(level);
        double spillUlp = Math.Abs((double)BitConverter.Int32BitsToSingle(spillBits + 1) - level);
        var report = new StringBuilder(FormattableString.Invariant(
            $"[TIN-LAKE-SPILL-TRACE] basin={basin.Id} subdivision={tin.SubdivisionLevel} depth={basin.MaximumDepth:R} spillUlp={spillUlp:R} epsilon={tin.Epsilon:R} bump={tin.VisibleBumpTolerance:R}"));
        report.AppendLine().Append(FormattableString.Invariant(
            $"termination={termination} failure={failure.Kind} acceptedSamples={path.Length} validWitness={hasWitness} witnessFacet={facet} witnessVertex={vertex} externalAuthority={basin.ExternalSource}"));
        report.AppendLine().Append("floor ").Append(Point(basin.Floor));
        report.AppendLine().Append("spill ").Append(Point(spill));
        if (failure.Kind != GeodesicTinHydrology.TraceFailureKind.None)
        {
            report.AppendLine().Append(FormattableString.Invariant(
                $"runningLowHydrology={failure.LowestHydrology:R} runningLowVisibleHeight={failure.LowestVisibleHeight:R} runningLowRadius={failure.LowestVisibleRadius:R}"));
            report.AppendLine().Append("rejected ").Append(Segment(failure.Start, failure.End));
        }
        if (hasWitness)
        {
            var start = tin.TransferLakePoint(spill, facet);
            var witness = tin.BasinPoint(facet, vertex);
            report.AppendLine().Append("witness edge ").Append(Candidate(start, witness));
        }

        tin.VisitSpillDescentCandidates(spill, (a, b) =>
            report.AppendLine().Append("descent candidate ").Append(Candidate(a, b)));
        Debug.Log(report.ToString());

        string Point(TinPoint p) => FormattableString.Invariant(
            $"facet={p.Triangle} bary=({p.A:R},{p.B:R},{p.C:R}) hydrology={tin.Height(p):R} visibleHeight={tin.Height(p, true):R} radius={tin.CompletedVisibleRadius(p):R} retainedFacet={tin.BasinFacetRetained(p.Triangle)} ocean={tin.IsOcean(p)}");

        string Segment(TinPoint a, TinPoint b)
        {
            double minimumRadius = a.Triangle == b.Triangle ? tin.MinimumOutletSegmentRadius(a, b) : double.NaN;
            return FormattableString.Invariant($"start=[{Point(a)}] end=[{Point(b)}] minimumRadius={minimumRadius:R}");
        }

        string Candidate(TinPoint a, TinPoint b) => FormattableString.Invariant(
            $"allowedDeparture={departure(a, b)} sourceLakeContainsMidpoint={Inside(Mix(a, b, .5), basin.Id)} slope={tin.OutletSlope(a, b):R} firstStepGrade={tin.PreviewSpillGrade(spill, a, b)} {Segment(a, b)}");
    }
}

public sealed partial class GeodesicTinHydrology
{
    internal void VisitSpillDescentCandidates(TinPoint spill, Action<TinPoint, TinPoint> visit)
    {
        // Ask the normal candidate generator for the incident fan without selecting a step.
        DownhillStep(spill, new List<int>(8), out _, out _, (a, b) =>
        {
            if (OutletSlope(a, b) > 0d) visit(a, b);
            return false;
        });
    }

    internal TraceFailureKind PreviewSpillGrade(TinPoint spill, TinPoint start, TinPoint end)
    {
        double lowH = Height(spill), lowV = Height(spill, true), lowR = CompletedVisibleRadius(spill);
        if (IsOcean(start))
            end = start;
        else if (TryCoast(start, end, out var coast))
            end = coast;
        return AcceptStep(start, end, ref lowH, ref lowV, ref lowR, false, false);
    }
}
#endif
