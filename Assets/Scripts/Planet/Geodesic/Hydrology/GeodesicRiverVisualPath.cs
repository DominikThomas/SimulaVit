using System;
using UnityEngine;

public sealed class GeodesicRiverVisualDiagnostics
{
    public int EligibleAnchors, UnadjustedAnchors, OneRingAdjustments, TwoRingAdjustments, UnresolvedAdjustments;
    public int HydrologicalDownhillEdges, VisibleDownhillBefore, VisibleFlatBefore, VisibleUphillBefore, RidgeCrossingsBefore;
    public int VisibleDownhillAfter, VisibleFlatAfter, VisibleUphillAfter, RidgeCrossingsAfter, CorrectedEdges, StillProblematicEdges;
    public int SmoothedEdges, SmoothingFallbackEdges;
    public double ValleySnapMilliseconds, SmoothingMilliseconds;

    public string Summary() =>
        $"[GeodesicRiverVisualAudit] eligibleAnchors={EligibleAnchors} unadjusted={UnadjustedAnchors} oneRing={OneRingAdjustments} twoRing={TwoRingAdjustments} unresolved={UnresolvedAdjustments} " +
        $"hydrologicalDownhillEdges={HydrologicalDownhillEdges} visibleDownhillBefore={VisibleDownhillBefore} visibleFlatBefore={VisibleFlatBefore} visibleUphillBefore={VisibleUphillBefore} ridgeCrossingsBefore={RidgeCrossingsBefore} " +
        $"visibleDownhillAfter={VisibleDownhillAfter} visibleFlatAfter={VisibleFlatAfter} visibleUphillAfter={VisibleUphillAfter} ridgeCrossingsAfter={RidgeCrossingsAfter} correctedEdges={CorrectedEdges} stillProblematic={StillProblematicEdges} " +
        $"smoothedEdges={SmoothedEdges} smoothingFallbackEdges={SmoothingFallbackEdges} valleySnapMs={ValleySnapMilliseconds:F2} smoothingMs={SmoothingMilliseconds:F2}";
}

/// <summary>
/// Historical visual correction retained for offline A/B diagnostics only. Runtime routing
/// uses GeodesicRiverPath and the completed-terrain grade gate instead of this unchecked spline.
/// </summary>
public static class GeodesicRiverVisualPath
{
    private const float OneRingFraction = 0.34f;
    private const float TwoRingFraction = 0.22f;
    private const float MinimumValleyDrop = 0.00015f;
    private const float MaterialVisibleRise = 0.0005f;
    private const int SmoothSegments = 4;

    public static Vector3[] BuildSharedAnchors(IGeodesicHydrologyTopology topology, GeodesicDrainageGraph graph,
        GeodesicRiverTerrain terrain, GeodesicLakeBasins lakes, double threshold, float numericalTolerance,
        out GeodesicRiverVisualDiagnostics diagnostics)
    {
        if (topology == null || graph == null || terrain == null || topology.CellCount != graph.CellCount)
            throw new ArgumentException("Visual river inputs must share one topology.");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var report = new GeodesicRiverVisualDiagnostics();
        var anchors = (Vector3[])topology.CellDirections.Clone();
        float tolerance = Mathf.Max(Mathf.Max(0f, numericalTolerance), MaterialVisibleRise);
        for (int order = graph.CellCount - 1; order >= 0; order--)
        {
            int cell = graph.UpstreamToDownstream[order];
            int receiver = graph.DrainageReceiver[cell];
            if (!Eligible(cell, receiver)) continue;
            report.EligibleAnchors++;
            Vector3 origin = topology.CellDirections[cell];
            Vector3 downstream = anchors[receiver];
            Candidate baseline = Evaluate(origin, cell, downstream, cell);
            Candidate best = baseline;
            int selectedRing = 0;
            for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
            {
                int neighbor = topology.Neighbors6[cell * 6 + slot];
                if (!Compatible(cell, neighbor)) continue;
                Candidate candidate = Evaluate(Vector3.Lerp(origin, topology.CellDirections[neighbor], OneRingFraction).normalized,
                    neighbor, downstream, cell);
                if (Better(candidate, best, baseline)) { best = candidate; selectedRing = 1; }
            }
            if (best.Problem > tolerance || best.Ridge)
            {
                for (int firstSlot = 0; firstSlot < topology.NeighborCounts[cell]; firstSlot++)
                {
                    int first = topology.Neighbors6[cell * 6 + firstSlot];
                    for (int secondSlot = 0; secondSlot < topology.NeighborCounts[first]; secondSlot++)
                    {
                        int second = topology.Neighbors6[first * 6 + secondSlot];
                        if (second == cell || IsNeighbor(cell, second) || !Compatible(cell, second)) continue;
                        Candidate candidate = Evaluate(Vector3.Lerp(origin, topology.CellDirections[second], TwoRingFraction).normalized,
                            second, downstream, cell);
                        if (Better(candidate, best, baseline)) { best = candidate; selectedRing = 2; }
                    }
                }
            }
            anchors[cell] = best.Direction;
            if (selectedRing == 1) report.OneRingAdjustments++;
            else if (selectedRing == 2) report.TwoRingAdjustments++;
            else report.UnadjustedAnchors++;
            if (best.Problem > tolerance || best.Ridge) report.UnresolvedAdjustments++;
        }
        Audit(topology.CellDirections, false);
        Audit(anchors, true);
        watch.Stop(); report.ValleySnapMilliseconds = watch.Elapsed.TotalMilliseconds; diagnostics = report;
        return anchors;

        bool Eligible(int cell, int receiver) => receiver >= 0 && !graph.Ocean[cell] &&
            graph.AccumulatedRunoff[cell] + 1e-12 >= threshold && !SelectedLake(cell);
        bool SelectedLake(int cell)
        {
            if (lakes == null || !lakes.Enabled || cell < 0 || cell >= lakes.BasinId.Length) return false;
            int id = lakes.BasinId[cell];
            return id >= 0 && id < lakes.Basins.Length && lakes.Basins[id].Selected;
        }
        bool Compatible(int cell, int candidate)
        {
            if (candidate < 0 || candidate >= graph.CellCount || graph.Ocean[candidate] || SelectedLake(candidate)) return false;
            int outlet = graph.OutletCell[cell], candidateOutlet = graph.OutletCell[candidate];
            return outlet < 0 || candidateOutlet < 0 || outlet == candidateOutlet;
        }
        bool IsNeighbor(int cell, int candidate)
        {
            for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
                if (topology.Neighbors6[cell * 6 + slot] == candidate) return true;
            return false;
        }
        Candidate Evaluate(Vector3 direction, int associatedCell, Vector3 downstream, int ownerCell)
        {
            float height = terrain.VisibleHeight(direction);
            float downstreamHeight = terrain.VisibleHeight(downstream);
            if (height + tolerance < downstreamHeight)
                return new Candidate(direction, height, float.PositiveInfinity, float.PositiveInfinity);
            float problem = EdgeProblem(direction, downstream, terrain.VisibleHeight, tolerance, out bool ridge);
            float displacement = (direction - topology.CellDirections[ownerCell]).sqrMagnitude;
            Vector3 linear = Vector3.Lerp(topology.CellDirections[ownerCell], topology.CellDirections[graph.DrainageReceiver[ownerCell]], .5f).normalized;
            float corridor = 1f - Mathf.Clamp01(Vector3.Dot(direction, linear));
            float score = problem * 1000f + height + displacement * .04f + corridor * .02f + associatedCell * 1e-12f;
            return new Candidate(direction, height, problem, score, ridge);
        }
        bool Better(Candidate candidate, Candidate current, Candidate baseline)
        {
            if (!float.IsFinite(candidate.Score)) return false;
            // A visual correction must never create a ridge crossing on an edge that did not have one.
            // Prefer a ridge-free candidate when its remaining uphill error is no worse than one tolerance.
            if (candidate.Ridge && !current.Ridge) return false;
            if (!candidate.Ridge && current.Ridge && candidate.Problem <= current.Problem + tolerance) return true;
            if (candidate.Problem + numericalTolerance < current.Problem) return true;
            if (candidate.Problem > current.Problem + numericalTolerance) return false;
            if (baseline.Problem <= tolerance && candidate.Height > baseline.Height - MinimumValleyDrop) return false;
            return candidate.Score + 1e-8f < current.Score;
        }
        void Audit(Vector3[] values, bool after)
        {
            for (int cell = 0; cell < graph.CellCount; cell++)
            {
                int receiver = graph.DrainageReceiver[cell];
                if (!Eligible(cell, receiver)) continue;
                if (!after && graph.HydrologicalElevation[receiver] <= graph.HydrologicalElevation[cell] + numericalTolerance)
                    report.HydrologicalDownhillEdges++;
                float start = terrain.VisibleHeight(values[cell]), end = terrain.VisibleHeight(values[receiver]);
                bool ridge;
                float problem = EdgeProblem(values[cell], values[receiver], terrain.VisibleHeight, tolerance, out ridge);
                if (after)
                {
                    if (end < start - tolerance) report.VisibleDownhillAfter++;
                    else if (end <= start + tolerance) report.VisibleFlatAfter++;
                    else report.VisibleUphillAfter++;
                    if (ridge) report.RidgeCrossingsAfter++;
                    bool beforeRidge;
                    float before = EdgeProblem(topology.CellDirections[cell], topology.CellDirections[receiver], terrain.VisibleHeight, tolerance, out beforeRidge);
                    if ((before > tolerance || beforeRidge) && problem <= tolerance && !ridge) report.CorrectedEdges++;
                    if (problem > tolerance || ridge) report.StillProblematicEdges++;
                }
                else
                {
                    if (end < start - tolerance) report.VisibleDownhillBefore++;
                    else if (end <= start + tolerance) report.VisibleFlatBefore++;
                    else report.VisibleUphillBefore++;
                    if (ridge) report.RidgeCrossingsBefore++;
                }
            }
        }
    }

    public static Vector3[] SmoothEdge(int cell, IGeodesicHydrologyTopology topology, GeodesicDrainageGraph graph,
        Vector3[] anchors, GeodesicRiverTerrain terrain, float numericalTolerance,
        GeodesicRiverVisualDiagnostics diagnostics)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        int receiver = graph.DrainageReceiver[cell];
        if (receiver < 0) return Array.Empty<Vector3>();
        Vector3 p1 = anchors[cell], p2 = anchors[receiver];
        int upstream = DominantUpstream(cell);
        int downstream = graph.DrainageReceiver[receiver];
        Vector3 p0 = upstream >= 0 ? anchors[upstream] : (p1 * 2f - p2).normalized;
        Vector3 p3 = downstream >= 0 ? anchors[downstream] : (p2 * 2f - p1).normalized;
        var smooth = new Vector3[SmoothSegments + 1];
        smooth[0] = p1; smooth[SmoothSegments] = p2;
        float maxDeviation = (p1 - p2).magnitude * .45f;
        bool valid = true;
        for (int sample = 1; sample < SmoothSegments; sample++)
        {
            float t = sample / (float)SmoothSegments;
            float t2 = t * t, t3 = t2 * t;
            Vector3 point = .5f * ((2f * p1) + (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
            point.Normalize(); smooth[sample] = point;
            Vector3 linear = Vector3.Lerp(p1, p2, t).normalized;
            if ((point - linear).magnitude > maxDeviation) valid = false;
        }
        float tolerance = Mathf.Max(Mathf.Max(0f, numericalTolerance), MaterialVisibleRise);
        float smoothProblem = PathProblem(smooth, terrain.VisibleHeight, tolerance);
        Vector3[] linearPath = Subdivide(p1, p2);
        float linearProblem = PathProblem(linearPath, terrain.VisibleHeight, tolerance);
        if (!valid || smoothProblem > linearProblem + tolerance)
        {
            diagnostics.SmoothingFallbackEdges++;
            smooth = linearPath;
        }
        else diagnostics.SmoothedEdges++;
        diagnostics.SmoothingMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - started) *
            1000d / System.Diagnostics.Stopwatch.Frequency;
        return smooth;

        int DominantUpstream(int target)
        {
            int best = -1; double flow = double.NegativeInfinity;
            for (int slot = 0; slot < topology.NeighborCounts[target]; slot++)
            {
                int neighbor = topology.Neighbors6[target * 6 + slot];
                if (graph.DrainageReceiver[neighbor] != target || graph.AccumulatedRunoff[neighbor] <= flow) continue;
                best = neighbor; flow = graph.AccumulatedRunoff[neighbor];
            }
            return best;
        }
    }

    private static Vector3[] Subdivide(Vector3 start, Vector3 end)
    {
        var path = new Vector3[SmoothSegments + 1];
        for (int i = 0; i <= SmoothSegments; i++) path[i] = Vector3.Lerp(start, end, i / (float)SmoothSegments).normalized;
        return path;
    }

    private static float PathProblem(Vector3[] path, Func<Vector3, float> height, float tolerance)
    {
        float problem = 0f, first = height(path[0]), prior = first;
        for (int i = 1; i < path.Length; i++)
        {
            float current = height(path[i]);
            problem = Mathf.Max(problem, current - prior - tolerance, current - first - tolerance);
            prior = current;
        }
        return Mathf.Max(0f, problem);
    }

    private static float EdgeProblem(Vector3 start, Vector3 end, Func<Vector3, float> height, float tolerance, out bool ridge)
    {
        float first = height(start), last = first, endHeight = height(end), problem = Mathf.Max(0f, endHeight - first - tolerance);
        float endpointMaximum = Mathf.Max(first, endHeight); ridge = false;
        for (int sample = 1; sample <= 4; sample++)
        {
            float current = sample == 4 ? endHeight : height(Vector3.Lerp(start, end, sample / 4f).normalized);
            problem = Mathf.Max(problem, current - last - tolerance, current - first - tolerance);
            if (sample < 4 && current > endpointMaximum + tolerance) ridge = true;
            last = current;
        }
        return Mathf.Max(0f, problem);
    }

    private readonly struct Candidate
    {
        public readonly Vector3 Direction;
        public readonly float Height, Problem, Score;
        public readonly bool Ridge;
        public Candidate(Vector3 direction, float height, float problem, float score, bool ridge = false)
        { Direction = direction; Height = height; Problem = problem; Score = score; Ridge = ridge; }
    }
}
