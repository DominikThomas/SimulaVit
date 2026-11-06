using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

/// <summary>Bounded, fill-only reconstruction on the completed render TIN. Original simulation
/// component IDs select the cores. Unchanged, continuously draining terrain supplies the outlets.</summary>
internal static class GeodesicDepressionRepair
{
    private const double SphereArea = 4d * Math.PI;
    private const int MaximumTraceSteps = 4096;

    internal sealed class Component
    {
        public int Id;
        public double OriginalArea, RawFloor = double.NaN, Floor = double.NaN, Spill = double.NaN;
        public double Depth => Spill - Floor;
        public double AffectedArea, MaximumLift;
        public string Status;
        public string Describe() => FormattableString.Invariant(
            $"[GeodesicDepressionRepairComponent] id={Id} status={Status} originalAreaFraction={OriginalArea:R} rawFloor={RawFloor:R} completedFloor={Floor:R} lowestWitnessedSpill={Spill:R} fillDepth={Depth:R} proposedAreaFraction={AffectedArea:R} proposedMaximumLift={MaximumLift:R}");
    }

    internal sealed class Result
    {
        public readonly List<Component> Components = new List<Component>();
        public Vector3[] Surface;
        public bool[] AffectedFacets;
        public int Repaired;
        public double Area, MaximumLift, Milliseconds;
    }

    private sealed class Proposal
    {
        public Component Report;
        public List<int> Core;
        public SortedDictionary<int, Vector3> Changes;
        public SortedSet<int> Facets;
    }

    internal static Result Plan(IGeodesicHydrologyTopology topology, Vector3[] surface,
        GeodesicOceanConnectivity ocean, float[] rawSimulation, int[] simulationCellByVertex, bool[] retained,
        float sea, float radius, GeodesicDepressionRepairLimits settings,
        Func<Vector3[], GeodesicTinHydrology> buildTin)
    {
        var watch = Stopwatch.StartNew();
        var result = new Result();
        var limits = settings.Normalized();
        if (ocean.ExcludedComponents == 0) return result;
        int primary = ocean.PrimaryRetainedComponent;
        var cores = new Dictionary<int, List<int>>();
        var reports = new Dictionary<int, Component>();
        for (int id = 0; id < ocean.Components.Length; id++)
        {
            var basin = ocean.Components[id];
            if (basin.Retained) continue;
            var report = new Component { Id = id, OriginalArea = basin.PlanetAreaFraction };
            result.Components.Add(report);
            if (primary < 0 || basin.PlanetAreaFraction >= ocean.Components[primary].PlanetAreaFraction)
                report.Status = "primary-scale-or-no-retained-ocean";
            else if (basin.PlanetAreaFraction > limits.componentArea) report.Status = "component-area";
            else { cores.Add(id, new List<int>()); reports.Add(id, report); }
        }

        for (int cell = 0; cell < rawSimulation.Length; cell++)
            if (ocean.PotentialLakeBasin[cell] && reports.TryGetValue(ocean.ComponentId[cell], out var report))
                report.RawFloor = double.IsNaN(report.RawFloor) ? rawSimulation[cell] : Math.Min(report.RawFloor, rawSimulation[cell]);

        if (cores.Count == 0) { result.Milliseconds = watch.Elapsed.TotalMilliseconds; return result; }
        var heights = new float[surface.Length];
        var coast = new bool[surface.Length];
        for (int v = 0; v < surface.Length; v++)
        {
            heights[v] = surface[v].magnitude;
            int cell = simulationCellByVertex[v];
            if (cores.TryGetValue(ocean.ComponentId[cell], out var core)) core.Add(v);
            coast[v] = ocean.OceanMask[cell] || ocean.CoastalLand[cell] || retained[v];
            for (int slot = 0; !coast[v] && slot < topology.NeighborCounts[v]; slot++)
                coast[v] = retained[topology.Neighbors6[v * 6 + slot]];
        }

        var proposals = Propose();
        if (proposals.Count > 0)
        {
            var completed = (Vector3[])surface.Clone();
            foreach (var proposal in proposals)
                foreach (var edit in proposal.Changes) completed[edit.Key] = edit.Value;

            // A downstream route can cross another proposal. After rolling back failures,
            // certify the survivors together again. A second failure rejects the residual batch.
            if (RejectFailed(completed, proposals) && proposals.Count > 0 && RejectFailed(completed, proposals))
            {
                foreach (var proposal in proposals) proposal.Report.Status = "dependent-batch-rollback";
                proposals.Clear();
            }

            if (proposals.Count > 0)
            {
                result.Surface = completed;
                result.AffectedFacets = new bool[topology.Triangles.Length / 3];
                foreach (var proposal in proposals)
                {
                    proposal.Report.Status = "repaired";
                    result.Repaired++;
                    result.Area += proposal.Report.AffectedArea;
                    result.MaximumLift = Math.Max(result.MaximumLift, proposal.Report.MaximumLift);
                    foreach (int facet in proposal.Facets) result.AffectedFacets[facet] = true;
                }
            }
        }

        result.Milliseconds = watch.Elapsed.TotalMilliseconds;
        return result;

        List<Proposal> Propose()
        {
            var proposals = new List<Proposal>();
            var reserved = new HashSet<int>();
            var drains = new Dictionary<int, bool>();
            GeodesicTinHydrology baseline = null;
            double total = 0d;
            foreach (var report in result.Components)
            {
                if (report.Status != null) continue;
                var core = cores[report.Id];
                if (core.Count == 0) { report.Status = "unresolved-at-render-resolution"; continue; }
                report.Floor = core.Min(v => heights[v]);
                if (sea - report.RawFloor > limits.fillDepth || sea - report.Floor > limits.fillDepth)
                { report.Status = "original-depth"; continue; }
                if (core.Any(v => coast[v] || reserved.Contains(v)))
                { report.Status = "protected-coast-or-overlap"; continue; }

                baseline ??= buildTin(surface);
                if (core.All(Drains)) { report.Status = "already-drains"; continue; }
                var proposal = Fill(topology, baseline, core, report, heights, sea, radius, limits,
                    v => !coast[v] && !reserved.Contains(v) &&
                        (ocean.ComponentId[simulationCellByVertex[v]] < 0 || ocean.ComponentId[simulationCellByVertex[v]] == report.Id),
                    v => !ocean.PotentialLakeBasin[simulationCellByVertex[v]] && Drains(v));
                if (proposal == null) continue;
                if (total + report.AffectedArea > limits.totalArea) { report.Status = "total-area"; continue; }
                foreach (int facet in proposal.Facets)
                    for (int corner = 0; corner < 3; corner++) reserved.Add(topology.Triangles[facet * 3 + corner]);
                total += report.AffectedArea;
                proposals.Add(proposal);
            }
            return proposals;

            bool Drains(int vertex)
            {
                if (!drains.TryGetValue(vertex, out bool valid))
                    drains.Add(vertex, valid = VerifiedRoute(baseline, VertexPoint(baseline, vertex)));
                return valid;
            }
        }

        bool RejectFailed(Vector3[] completed, List<Proposal> candidates)
        {
            var tin = buildTin(completed);
            var failures = new List<Proposal>();
            foreach (var proposal in candidates)
            {
                bool verified = proposal.Core.All(v => VerifiedRoute(tin, VertexPoint(tin, v), true)) &&
                    proposal.Facets.All(f => VerifiedRoute(tin, tin.Centroid(f), true));
                if (!verified) failures.Add(proposal);
            }
            foreach (var failure in failures)
            {
                failure.Report.Status = "final-tin-no-descending-outlet";
                foreach (int vertex in failure.Changes.Keys) completed[vertex] = surface[vertex];
                candidates.Remove(failure);
            }
            return failures.Count > 0;
        }
    }

    private static Proposal Fill(IGeodesicHydrologyTopology topology, GeodesicTinHydrology tin, List<int> core,
        Component report, float[] heights, float sea, float radius, GeodesicDepressionRepairLimits limits,
        Func<int, bool> canRaise, Func<int, bool> hasOutlet)
    {
        var region = new HashSet<int>(core);
        var inner = new List<int>(core);
        // At most two render-vertex rings of shoulder. Any raised shoulder participates in
        // the same constrained spill solution, with fixed heights at the outer boundary.
        for (int ring = 0, head = 0; ring < 2; ring++)
        {
            int end = inner.Count;
            for (; head < end; head++)
                foreach (int n in Neighbors(topology, inner[head]))
                    if (canRaise(n) && region.Add(n)) inner.Add(n);
        }

        var flood = new SortedSet<(double Height, int Vertex)>();
        var labels = new Dictionary<int, double>();
        double ceiling = report.Floor + limits.fillDepth;
        foreach (int vertex in inner)
            foreach (int n in Neighbors(topology, vertex))
                if (!region.Contains(n) && !labels.ContainsKey(n) && heights[n] <= ceiling && hasOutlet(n))
                {
                    labels.Add(n, heights[n]);
                    flood.Add((heights[n], n));
                }

        // First find the lowest witnessed ungraded saddle. The second flood finds the
        // minimum feasible fill with a small pitch, independent of outlet enumeration.
        var ungraded = new Dictionary<int, double>(labels);
        var ungradedQueue = new SortedSet<(double Height, int Vertex)>(flood);
        Flood(ungraded, ungradedQueue, false);
        if (core.Any(v => !ungraded.ContainsKey(v))) { report.Status = "no-bounded-spill"; return null; }
        report.Spill = core.Max(v => ungraded[v]);
        Flood(labels, flood, true);
        if (core.Any(v => !labels.ContainsKey(v))) { report.Status = "graded-fill-depth"; return null; }

        // Only the filled patch touching the original component is in scope. Unrelated
        // pockets in the search shoulder stay untouched.
        var changes = new SortedDictionary<int, Vector3>();
        var patch = new Queue<int>(core);
        var visited = new HashSet<int>(core);
        while (patch.Count > 0)
        {
            int vertex = patch.Dequeue();
            if (!labels.TryGetValue(vertex, out double filled) || filled <= heights[vertex]) continue;
            Vector3 position = topology.CellDirections[vertex] * (float)filled;
            double completedHeight = position.magnitude;
            if (completedHeight > ceiling) { report.Status = "graded-fill-depth"; return null; }
            double lift = completedHeight - heights[vertex];
            if (lift <= 0d) continue;
            changes.Add(vertex, position);
            report.MaximumLift = Math.Max(report.MaximumLift, lift);
            foreach (int n in Neighbors(topology, vertex))
                if (region.Contains(n) && visited.Add(n)) patch.Enqueue(n);
        }
        if (changes.Count == 0) { report.Status = "no-fill-required"; return null; }
        if (report.MaximumLift > limits.localAdjustment)
        { report.Status = "local-adjustment"; return null; }

        var facets = new SortedSet<int>();
        foreach (var edit in changes)
        {
            int v = edit.Key;
            double raised = edit.Value.magnitude;
            foreach (int n in Neighbors(topology, v))
            {
                double adjacent = changes.TryGetValue(n, out Vector3 changed) ? changed.magnitude : heights[n];
                double length = (topology.CellDirections[v] - topology.CellDirections[n]).magnitude * radius;
                // Limit newly introduced outward edge grade. Existing steeper landforms
                // can be made gentler by the fill without forcing a wider edit.
                if (raised - adjacent > Math.Max(0d, heights[v] - (double)heights[n]) + .05d * length + tin.Epsilon)
                { report.Status = "shoulder-grade"; return null; }
            }
            for (int i = tin.BasinIncidentStart(v); i < tin.BasinIncidentEnd(v); i++)
                facets.Add(tin.BasinIncidentTriangle(i));
        }
        foreach (int f in facets)
            report.AffectedArea += GeodesicHydrologyTopology.SphericalTriangleArea(
                topology.CellDirections[tin.BasinVertex(f, 0)], topology.CellDirections[tin.BasinVertex(f, 1)],
                topology.CellDirections[tin.BasinVertex(f, 2)]) / SphereArea;
        if (report.AffectedArea > limits.affectedArea) { report.Status = "affected-area"; return null; }
        return new Proposal { Report = report, Core = core, Changes = changes, Facets = facets };

        void Flood(Dictionary<int, double> levels, SortedSet<(double Height, int Vertex)> queue, bool graded)
        {
            while (queue.Count > 0)
            {
                var current = queue.Min;
                queue.Remove(current);
                foreach (int n in Neighbors(topology, current.Vertex))
                {
                    if (!region.Contains(n)) continue;
                    double height = Math.Max(heights[n], current.Height);
                    if (graded)
                    {
                        double chord = (topology.CellDirections[current.Vertex] - topology.CellDirections[n]).magnitude;
                        // The radial pitch also clears the chord's inward sag.
                        double pitch = Math.Max(4d * tin.Epsilon, chord * radius * (.001d + .55d * chord));
                        height = Math.Max(heights[n], current.Height + pitch);
                    }
                    if (height > ceiling || (levels.TryGetValue(n, out double old) && height >= old)) continue;
                    if (levels.ContainsKey(n)) queue.Remove((old, n));
                    levels[n] = height;
                    queue.Add((height, n));
                }
            }
        }
    }

    private static IEnumerable<int> Neighbors(IGeodesicHydrologyTopology topology, int vertex)
    {
        for (int slot = 0; slot < topology.NeighborCounts[vertex]; slot++)
            yield return topology.Neighbors6[vertex * 6 + slot];
    }

    private static TinPoint VertexPoint(GeodesicTinHydrology tin, int vertex) =>
        tin.BasinPoint(tin.BasinIncidentTriangle(tin.BasinIncidentStart(vertex)), vertex);

    internal static bool VerifiedRoute(GeodesicTinHydrology tin, TinPoint source, bool requireDepartureGrade = false)
    {
        var path = tin.TraceLakeOutlet(source, null, out var termination, MaximumTraceSteps);
        if (path.Length < 2 || !GeodesicOceanDrainagePlan.ConfirmedOcean(tin,
                new TinParcelDestination(path[path.Length - 1], termination)) ||
            !tin.VerifyExposedOutlet(path)) return false;
        if (tin.CompletedVisibleRadius(source) - tin.CompletedVisibleRadius(path[path.Length - 1]) <= tin.Epsilon) return false;
        if (!requireDepartureGrade) return true;
        for (int i = 1; i < path.Length; i++)
            if (path[i - 1].Triangle == path[i].Triangle)
                return tin.Height(path[i - 1]) - tin.Height(path[i]) > tin.Epsilon * .25d &&
                       tin.CompletedVisibleRadius(path[i - 1]) - tin.CompletedVisibleRadius(path[i]) > tin.Epsilon * .25d;
        return false;
    }
}
