using System;
using System.Collections.Generic;
using System.Linq;

public sealed partial class GeodesicValleyCarving
{
    private const int MaximumOutletRevisions = 4;
    private const int MaximumOutletVerificationBuilds = 7;

    internal sealed class OutletIteration
    {
        internal int Pass, CorrectedJunctions, TerminalVertex, OceanComponent;
        internal string SourceRole, Outcome;
        internal OutletPathComparison Branch;
        internal TinPoint Terminal;
        internal TinTermination Termination;
        internal double Volume, Depth, RevisedVolume = double.NaN, RevisedDepth = double.NaN, RevisedArea;
        internal bool MovedDownstream, RepeatedState;
        internal readonly List<(int Vertex, double Before, double After)> BedChanges =
            new List<(int, double, double)>();

        internal string Summary() => FormattableString.Invariant(
            $"pass={Pass} sourceRole={SourceRole} divergenceVertex={Branch?.DepartureVertex ?? -1} divergenceFacet={Branch?.ActualNext?.Triangle ?? -1} departure={(Branch?.Departure is TinPoint p ? FormatPoint(p) : "none")} intendedVertex={Branch?.PlannedNextVertex ?? -1} competingSlope={Branch?.ActualSlope ?? double.NaN:R} intendedSlope={Branch?.PlannedSlope ?? double.NaN:R} correctedJunctions={CorrectedJunctions} movedDownstream={MovedDownstream} repeatedState={RepeatedState} volume={Volume:R} maxCut={Depth:R} revisedVolume={RevisedVolume:R} revisedMaxCut={RevisedDepth:R} terminal={FormatPoint(Terminal)} terminalVertex={TerminalVertex} termination={Termination} oceanComponent={OceanComponent} outcome={Outcome} bedChanges=[{string.Join(",", BedChanges.Select(b => FormattableString.Invariant($"{b.Vertex}:{b.Before:R}->{b.After:R}")))}]");
    }

    private sealed partial class MajorOutletPlanner
    {
        private float[] OutletBeds(Candidate cut) => cut.Vertices.Select(v =>
            cut.Surface.TryGetValue(v, out float bed) ? bed : carving.original[v]).ToArray();

        private Candidate ReviseOutlet(GeodesicTinHydrology proposed, MajorOutletAttempt attempt,
            OutletIteration iteration, out string rejection)
        {
            var previous = attempt.Cut;
            int[] vertices = previous.Vertices;
            var before = OutletBeds(previous);
            var beds = Array.ConvertAll(before, b => (double)b);
            var indices = vertices.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
            var incident = new List<int>(8);
            Func<TinPoint, double> heightAt = Height;
            var chain = new List<Label> { new Label { Vertex = vertices[0], Floor = beds[0] } };
            rejection = null;

            // Recompute the highest bed allowed at every station, including junctions
            // beyond the first failure. Constraints only lower beds, so a revision
            // cannot undo a previous cut. The rebuilt TIN supplies new constraints.
            for (int i = 1; i < vertices.Length; i++)
            {
                int from = vertices[i - 1], to = vertices[i];
                // The source trace already drained through this prefix. Retain it
                // and let the next TIN reveal any effects of the downstream cuts.
                if (i < iteration.Branch.PlannedIndex)
                {
                    chain.Add(new Label { Vertex = to, Floor = beds[i] });
                    continue;
                }
                int facet = OutletEdgeFacet(proposed, from, to);
                var start = proposed.BasinPoint(facet, from);
                var end = proposed.BasinPoint(facet, to);
                double angle = carving.Angle(from, to);
                double ceiling = Math.Min(beds[i], beds[i - 1] * Math.Cos(angle) - carving.step);
                double gradeCeiling = ceiling;

                // Every incident edge can compete. Also sample the actual steepest
                // facet direction, which can be stronger than every edge in its fan.
                for (int k = 0; k < carving.graph.NeighborCounts[from]; k++)
                {
                    int neighbor = carving.graph.Neighbors6[from * 6 + k];
                    if (neighbor == to) continue;
                    var branch = proposed.BasinPoint(OutletEdgeFacet(proposed, from, neighbor), neighbor);
                    ceiling = Math.Min(ceiling, BranchCeiling(start, branch, end, i));
                }
                if (proposed.TryOutletDownhillStep(start, incident, out var localStart, out var localEnd) &&
                    proposed.OutletDistance(end, localEnd) > 1e-10)
                    ceiling = Math.Min(ceiling, BranchCeiling(localStart, localEnd, end, i, constrainFacet: true));
                if (iteration.Branch.PlannedIndex == i)
                    ceiling = Math.Min(ceiling, BranchCeiling(iteration.Branch.Departure.Value,
                        iteration.Branch.ActualNext.Value, end, i, constrainFacet: true));

                // Leave unchanged beds within the projection tolerance of the
                // minimum grade, but apply a real cut when a competing branch wins.
                if (double.IsNegativeInfinity(ceiling))
                { rejection = "adaptive-branch-cannot-be-steered-by-incision"; return null; }
                if (gradeCeiling - ceiling > carving.step * .05) iteration.CorrectedJunctions++;
                if (before[i] - ceiling > carving.step * .05)
                {
                    beds[i] = (float)ceiling;
                    iteration.BedChanges.Add((to, before[i], beds[i]));
                }
                if (beds[i] < before[i])
                {
                    double cut = carving.original[to] - beds[i];
                    iteration.RevisedDepth = double.IsNaN(iteration.RevisedDepth) ? cut : Math.Max(iteration.RevisedDepth, cut);
                    if (i == vertices.Length - 1) rejection = "adaptive-receiver-entry-would-require-cut";
                    else if (carving.protectedLake[to]) rejection = "adaptive-protected-lake";
                    else if (shore[to] || carving.ocean[to] || carving.protectedSubsea[to]) rejection = "adaptive-protected-coast-or-subsea";
                    else if (beds[i] < carving.seaFloor) rejection = "adaptive-bed-below-dry-channel-floor";
                    else if (cut > carving.depth) rejection = "adaptive-depth-budget";
                    // Limit newly created grades to 1:1. A naturally steeper reach
                    // can retain its original grade.
                    else if (beds[i - 1] - beds[i] > Math.Max(angle * carving.radius,
                        carving.original[from] - (double)carving.original[to])) rejection = "adaptive-excessive-channel-grade";
                }
                if (rejection != null) return null;
                chain.Add(new Label { Vertex = to, Floor = beds[i] });
            }
            if (iteration.BedChanges.Count == 0)
            { rejection = "adaptive-no-material-profile-change"; return null; }

            var revised = new Candidate { Vertices = vertices, Length = previous.Length, TargetKind = previous.TargetKind };
            carving.BuildProfile(revised, chain, receivers[attempt.TargetVertex].Escape, lengthLimit);
            if (revised.Rejection != null && revised.AffectedVertices == 0)
            { rejection = "adaptive-profile-" + revised.Rejection; return null; }
            Metrics(revised.Surface, out double volume, out double area, out double depth);
            iteration.RevisedVolume = volume;
            iteration.RevisedDepth = depth;
            rejection = revised.Rejection != null ? "adaptive-profile-" + revised.Rejection :
                revised.Surface.Keys.Any(v => shore[v]) ? "adaptive-protected-coast" :
                depth > carving.depth ? "adaptive-depth-budget-after-projection" :
                volume > maxVolume ? "adaptive-volume-budget" : null;
            if (rejection != null) return null;
            if (revised.Surface.Count == previous.Surface.Count && revised.Surface.All(edit =>
                previous.Surface.TryGetValue(edit.Key, out float height) && height == edit.Value))
            { rejection = "adaptive-no-material-profile-change"; return null; }
            iteration.RevisedArea = area;
            return revised;

            double BranchCeiling(TinPoint start, TinPoint branch, TinPoint end, int index, bool constrainFacet = false) =>
                beds[index] - Height(end) + OutletBranchBedCeiling(proposed, start, branch, end,
                    vertices[index - 1], vertices[index], heightAt, carving.step, constrainFacet);

            double Height(TinPoint point)
            {
                double height = proposed.Height(point);
                for (int k = 0; k < 3; k++)
                    if (indices.TryGetValue(proposed.BasinVertex(point.Triangle, k), out int index))
                        height += Weight(point, k) * (beds[index] - before[index]);
                return height;
            }
        }
    }

    internal static double OutletBranchBedCeiling(GeodesicTinHydrology tin, TinPoint start, TinPoint branch, TinPoint end,
        int from, int target, Func<TinPoint, double> height, double margin, bool constrainFacet)
    {
        double intendedDistance = tin.OutletDistance(start, end);
        double competingDistance = tin.OutletDistance(start, branch);
        if (intendedDistance <= 1e-10 || competingDistance <= 1e-10) return double.PositiveInfinity;
        double startHeight = height(start), endHeight = height(end);
        double actualSlope = (startHeight - height(branch)) / competingDistance;
        if (actualSlope <= 0) return double.PositiveInfinity;
        if (constrainFacet)
        {
            int third = -1, shared = 0;
            for (int k = 0; k < 3; k++)
            {
                int vertex = tin.BasinVertex(branch.Triangle, k);
                if (vertex == from || vertex == target) shared++;
                else third = vertex;
            }
            if (shared == 2)
            {
                var edgeStart = tin.BasinPoint(branch.Triangle, from);
                var bank = tin.BasinPoint(branch.Triangle, third);
                double projection = tin.OutletEdgeProjection(edgeStart, end, bank);
                if (projection > 1e-10)
                {
                    // Keep the fitted gradient from pointing into this facet.
                    // Comparing one ray alone can expose a steeper rotated ray.
                    double head = height(edgeStart);
                    return head + (height(bank) - head) / projection - margin;
                }
            }
        }
        double startWeight = TargetWeight(start);
        double gain = (1 - startWeight) / intendedDistance - (TargetWeight(branch) - startWeight) / competingDistance;
        double deficit = actualSlope - (startHeight - endHeight) / intendedDistance;
        if (gain <= 1e-10) return deficit > margin / intendedDistance ? double.NegativeInfinity : double.PositiveInfinity;
        // Express the numerical margin in bed units. A fixed slope margin
        // would require an unbounded cut for nearly parallel directions.
        return endHeight - deficit / gain - margin;

        double TargetWeight(TinPoint point)
        {
            for (int k = 0; k < 3; k++)
                if (tin.BasinVertex(point.Triangle, k) == target) return Weight(point, k);
            return 0;
        }
    }

    private static double Weight(TinPoint point, int corner) => corner == 0 ? point.A : corner == 1 ? point.B : point.C;
}
