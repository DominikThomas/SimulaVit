using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public enum GeodesicRiverGrade { ClearlyDownhill, NearFlatDownhill, EffectivelyFlat, NearFlatUphill, ClearlyUphill, NotEvaluated }
public enum GeodesicRiverGradeStage { BeforeGrade, EndpointGate, Refinement, Projection, Complete }

/// <summary>Observation only: never participates in routing, lake selection or rendering decisions.</summary>
public struct GeodesicRiverGradeObservation
{
    public bool Evaluated, VisibleHeight;
    public Vector3 Start, End;
    public float UpstreamHeight, DownstreamHeight;
    public GeodesicRiverGradeStage Stage;
    public double Delta => (double)DownstreamHeight - UpstreamHeight;
    public string HeightSource => !Evaluated ? "none-before-grade" : VisibleHeight ? "interpolated-visible-radius" : "interpolated-large-scale-radius";
}

public sealed class GeodesicRiverGradeRow
{
    public int Cell, Receiver, BasinId;
    public double Delta, HydrologyDelta, VisibleDelta, FilledDelta, Strength, Area;
    public double RequiredCorridorTolerance = double.NaN;
    public bool Suppressed, LakeRelated, FilledDepression, SpillPoint, LakeOutlet, FlatFloodplain;
    public GeodesicRiverGrade Grade;
    public GeodesicRiverReachFailure Failure;
    public GeodesicRiverGradeObservation Observation;
}

public sealed class GeodesicRiverGradeReport
{
    public double EpsilonFlat, EpsilonUphill;
    public int Candidate, Visible, Suppressed, LakeConnected, RunoffViolations, AreaViolations, FilledTopologyUphill;
    public readonly int[] Grades = new int[6], SuppressedGrades = new int[6], Failures = new int[5];
    public readonly int[,] SuppressedHydrologyVisible = new int[5, 5];
    public readonly List<GeodesicRiverGradeRow> Rows = new List<GeodesicRiverGradeRow>();
    public readonly List<(int upstream, GeodesicRiverGradeRow blocked)> ChainBreaks = new List<(int, GeodesicRiverGradeRow)>();

    public string Summary()
    {
        var deltas = Rows.Where(r => r.Suppressed && r.Observation.Evaluated).Select(r => r.Delta).OrderBy(x => x).ToArray();
        var tested = Rows.Where(r => !double.IsNaN(r.RequiredCorridorTolerance)).ToArray();
        string F(double x) => x.ToString("G9", CultureInfo.InvariantCulture);
        return $"[GeodesicRiverGradeDiagnostics] candidate={Candidate} visible={Visible} suppressed={Suppressed} lakeConnected={LakeConnected} " +
            $"flat={Grades[2]} nearFlatUp={Grades[3]} clearUp={Grades[4]} nearFlatDown={Grades[1]} clearDown={Grades[0]} ungraded={Grades[5]} " +
            $"suppressedEffectivelyFlat={SuppressedGrades[2]} suppressedNearFlatUphill={SuppressedGrades[3]} suppressedClearlyUphill={SuppressedGrades[4]} " +
            $"suppressedNearFlatDownhill={SuppressedGrades[1]} suppressedOther={SuppressedGrades[0] + SuppressedGrades[5]} suppressedClearlyDownhill={SuppressedGrades[0]} suppressedUngraded={SuppressedGrades[5]} " +
            $"projectionMismatch={Failures[1]} corridorFailure={Failures[2]} unresolvedDepression={Failures[3]} topologyFailure={Failures[4]} " +
            $"epsilonFlat={F(EpsilonFlat)} epsilonUphill={F(EpsilonUphill)} suppressedDeltaMedian={(deltas.Length > 0 ? F(deltas[(int)Math.Round((deltas.Length-1)*.5)]) : "NA")} " +
            $"largestSuppressedDeltas={string.Join(",", deltas.Reverse().Take(3).Select(F))} chainBreaks={ChainBreaks.Count} " +
            $"runoffViolations={RunoffViolations} areaViolations={AreaViolations} filledTopologyUphill={FilledTopologyUphill} " +
            $"corridorsAudited={tested.Length} corridorsRequiringMaterialRise={tested.Count(r=>r.RequiredCorridorTolerance > EpsilonUphill)}";
    }

    public IEnumerable<string> BreakExamples(int limit)
    {
        // Round-robin categories gives flat/tiny-up/material-up examples before repeated cases.
        var groups = new[] { 2, 3, 4, 1, 0, 5 }.Select(g => ChainBreaks.Where(b => (int)b.blocked.Grade == g).ToArray()).ToArray();
        int emitted = 0;
        for (int index = 0; emitted < limit && groups.Any(g => index < g.Length); index++)
        foreach (var group in groups)
        {
            if (index >= group.Length || emitted >= limit) continue;
            var item = group[index]; var r = item.blocked; emitted++;
            yield return FormattableString.Invariant($"[GeodesicRiverGradeBreak] upstreamCell={item.upstream} blockedCell={r.Cell} downstreamCell={r.Receiver} deltaH={r.Delta:G9} heightSource={r.Observation.HeightSource} classification={r.Grade} failure={r.Failure} stage={r.Observation.Stage} strength={r.Strength:G9} drainageArea={r.Area:G9} hydrologyDelta={r.HydrologyDelta:G9} visibleAnchorDelta={r.VisibleDelta:G9} filledDelta={r.FilledDelta:G9} lakeBasinId={r.BasinId} lakeRelated={r.LakeRelated} spill={r.SpillPoint} requiredCorridorTolerance={r.RequiredCorridorTolerance:G9}");
        }
    }
}

public static class GeodesicRiverGradeAudit
{
    // Radius ~8 has a float ULP of 9.54e-7. Measured interpolation p99 at subdivisions 6/7 is ~1.0-1.8e-6.
    // Eight ULPs brackets typical paired numerical errors (not every outlier), far below terrain
    // relief. This is a diagnostic band ONLY; it never changes uphillTolerance.
    public static double NearFlatBand(float radius, float productionTolerance)
    {
        float r = Mathf.Max(.001f, Mathf.Abs(radius));
        double ulp = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(r) + 1) - (double)r;
        return Math.Max(Math.Max(0f, productionTolerance), 8d * ulp);
    }

    public static GeodesicRiverGrade Classify(double delta, double flat, double near)
    {
        if (double.IsNaN(delta)) return GeodesicRiverGrade.NotEvaluated;
        if (delta < -near) return GeodesicRiverGrade.ClearlyDownhill;
        if (delta < -flat) return GeodesicRiverGrade.NearFlatDownhill;
        if (delta <= flat) return GeodesicRiverGrade.EffectivelyFlat;
        return delta <= near ? GeodesicRiverGrade.NearFlatUphill : GeodesicRiverGrade.ClearlyUphill;
    }

    public static GeodesicRiverGradeReport Build(GeodesicDrainageGraph graph, Vector3[] anchors,
        GeodesicRiverTerrain terrain, GeodesicLakeBasins lakes, IReadOnlyDictionary<int, GeodesicRiverReachPlan> plans,
        double flowThreshold, float radius, float productionTolerance, bool auditCorridors = false,
        int steps = 12, int lanes = 7, float corridor = .35f)
    {
        var report = new GeodesicRiverGradeReport { EpsilonFlat = Math.Max(0f, productionTolerance), EpsilonUphill = NearFlatBand(radius, productionTolerance) };
        var spill = new HashSet<int>();
        if (lakes != null) foreach (var b in lakes.Basins) { spill.Add(b.SpillFromCell); spill.Add(b.SpillCell); }
        var rowsByCell = new Dictionary<int, GeodesicRiverGradeRow>();
        for (int cell = 0; cell < graph.CellCount; cell++)
        {
            int next = graph.DrainageReceiver[cell]; if (next < 0) continue;
            // Double accumulation comparison tolerance is independent of height tolerance.
            if (graph.AccumulatedRunoff[next] + 1e-12 * Math.Max(1d, graph.AccumulatedRunoff[cell]) < graph.AccumulatedRunoff[cell]) report.RunoffViolations++;
            if (graph.DrainageArea[next] + 1e-12 * Math.Max(1d, graph.DrainageArea[cell]) < graph.DrainageArea[cell]) report.AreaViolations++;
            if (graph.FilledElevation[next] > graph.FilledElevation[cell] + GeodesicLakeBasins.ElevationEpsilon) report.FilledTopologyUphill++;
            if (graph.Ocean[cell] || graph.AccumulatedRunoff[cell] < flowThreshold || !plans.TryGetValue(cell, out var plan)) continue;
            var o = plan.GradeObservation;
            int basin = lakes != null ? lakes.BasinId[cell] : -1;
            int targetBasin = lakes != null ? lakes.BasinId[next] : -1;
            var row = new GeodesicRiverGradeRow {
                Cell = cell, Receiver = next, BasinId = basin >= 0 ? basin : targetBasin,
                Observation = o, Delta = o.Evaluated ? o.Delta : double.NaN,
                HydrologyDelta = graph.HydrologicalElevation[next] - (double)graph.HydrologicalElevation[cell],
                VisibleDelta = terrain.VisibleHeight(anchors[next]) - (double)terrain.VisibleHeight(anchors[cell]),
                FilledDelta = graph.FilledElevation[next] - (double)graph.FilledElevation[cell],
                Strength = graph.AccumulatedRunoff[cell] / flowThreshold, Area = graph.DrainageArea[cell],
                Suppressed = plan.Failure != GeodesicRiverReachFailure.None, Failure = plan.Failure,
                LakeRelated = plan.InletBasin >= 0 || plan.OutletBasin >= 0 || plan.LakeConnected ||
                    (basin >= 0 && lakes.Basins[basin].Selected) || (targetBasin >= 0 && lakes.Basins[targetBasin].Selected),
                FilledDepression = graph.FillDepth[cell] > GeodesicLakeBasins.ElevationEpsilon || graph.FillDepth[next] > GeodesicLakeBasins.ElevationEpsilon,
                SpillPoint = spill.Contains(cell) || spill.Contains(next), LakeOutlet = plan.OutletBasin >= 0
            };
            row.FlatFloodplain = !row.FilledDepression && Math.Abs(row.FilledDelta) <= report.EpsilonFlat;
            row.Grade = Classify(row.Delta, report.EpsilonFlat, report.EpsilonUphill);
            report.Rows.Add(row); rowsByCell.Add(cell, row); report.Candidate++; report.Grades[(int)row.Grade]++;
            if (plan.Path.Length > 1) report.Visible++;
            if (plan.LakeConnected) report.LakeConnected++;
            if (!row.Suppressed) continue;
            report.Suppressed++; report.SuppressedGrades[(int)row.Grade]++; report.Failures[(int)row.Failure]++;
            report.SuppressedHydrologyVisible[(int)Classify(row.HydrologyDelta, report.EpsilonFlat, report.EpsilonUphill), (int)Classify(row.VisibleDelta, report.EpsilonFlat, report.EpsilonUphill)]++;
            if (auditCorridors && o.Evaluated && o.Stage == GeodesicRiverGradeStage.Refinement)
                row.RequiredCorridorTolerance = MinimumCorridorTolerance(o.Start, o.End, o.VisibleHeight ? terrain.VisibleHeight : terrain.Height, steps, lanes, corridor);
        }
        foreach (var row in report.Rows)
        {
            var p = plans[row.Cell];
            if (p.Path.Length > 1 && p.InletBasin < 0 && rowsByCell.TryGetValue(row.Receiver, out var blocked) && blocked.Suppressed)
                report.ChainBreaks.Add((row.Cell, blocked));
        }
        return report;
    }

    /// <summary>Read-only minimax audit of the SAME lane graph and quarter-segment samples as Refine.
    /// Finds the smallest rise allowance along any sampled corridor, including the source-height
    /// ceiling. No path is published, no thresholds are altered. Float addition rounds thresholds
    /// in Refine; the inferred allowance has approximately one ULP of boundary uncertainty.</summary>
    public static double MinimumCorridorTolerance(Vector3 source, Vector3 receiver, Func<Vector3, float> height,
        int steps, int lanes, float corridorFraction)
    {
        steps = Mathf.Clamp(steps, 4, 64); lanes = Mathf.Clamp(lanes | 1, 3, 15);
        int middle = lanes / 2;
        float width = (source - receiver).magnitude * Mathf.Clamp(corridorFraction, 0f, .45f);
        Vector3 side = Vector3.Cross(source, receiver).normalized;
        var points = new Vector3[(steps + 1) * lanes]; var heights = new float[points.Length];
        var cost = new double[points.Length]; Array.Fill(cost, double.PositiveInfinity);
        for (int step = 0; step <= steps; step++)
        for (int lane = 0; lane < lanes; lane++)
        {
            float t = step / (float)steps;
            Vector3 center = Vector3.Lerp(source, receiver, t).normalized;
            int i = step * lanes + lane;
            points[i] = step == 0 ? source : step == steps ? receiver :
                (center + side * (width * Mathf.Sin(t * Mathf.PI) * (lane - middle) / middle)).normalized;
            heights[i] = height(points[i]);
        }
        cost[middle] = 0d;
        for (int step = 1; step <= steps; step++)
        for (int lane = 0; lane < lanes; lane++)
        {
            if (step == steps && lane != middle) continue;
            int index = step * lanes + lane;
            for (int priorLane = 0; priorLane < lanes; priorLane++)
            {
                if (step > 1 && step < steps && Math.Abs(priorLane - lane) > 2) continue;
                int prior = (step - 1) * lanes + priorLane;
                if (double.IsPositiveInfinity(cost[prior])) continue;
                double required = Math.Max(cost[prior], Math.Max(heights[index] - (double)heights[prior], heights[index] - (double)heights[middle]));
                if (required >= cost[index]) continue;
                float last = heights[prior];
                for (int sample = 1; sample <= 4; sample++)
                {
                    float next = sample == 4 ? heights[index] : height(Vector3.Lerp(points[prior], points[index], sample / 4f).normalized);
                    required = Math.Max(required, Math.Max(next - (double)last, next - (double)heights[prior])); last = next;
                }
                if (required < cost[index]) cost[index] = required;
            }
        }
        return cost[steps * lanes + middle];
    }
}
