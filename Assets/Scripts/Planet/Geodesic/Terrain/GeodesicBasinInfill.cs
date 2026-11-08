using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Reconstructs sedimentary terrain from the completed land surrounding originally
/// excluded ocean components. The geological surface is independent of drainage success.</summary>
internal static class GeodesicBasinInfill
{
    private const double SphereArea = 4d * Math.PI;
    private const int SmoothingPasses = 6;
    private const int SubmergedCollarRings = 2;

    internal sealed class Component
    {
        public int Id;
        public string Status;
        public double OriginalArea, RawFloor = double.NaN, Target = double.NaN, RimRelief = double.NaN;
        public double PrimaryDistance = double.NaN, Area, Volume, MeanLift, MaximumLift, MaximumAddedGrade;
        public double OriginalMinimum = double.NaN, OriginalMean = double.NaN, OriginalMaximum = double.NaN;
        public double FinalMinimum = double.NaN, FinalMean = double.NaN, FinalMaximum = double.NaN;
        public int ProtectedVertices, CollarVertices, GradeLimitedVertices, LiftLimitedVertices;
        public int FinalMinimumVertex = -1;
        public int SubseaProtected, SubseaOutsideRim, SubseaGradeLimited, SubseaLiftLimited, SubseaBlended;
        public bool LargeRelativeToRelief, SteepTransition;

        public string Describe() => FormattableString.Invariant(
            $"[GeodesicBasinInfillComponent] id={Id} status={Status} originalAreaFraction={OriginalArea:R} rawFloor={RawFloor:R} originalMinMeanMax={OriginalMinimum:R}/{OriginalMean:R}/{OriginalMaximum:R} finalMinMeanMax={FinalMinimum:R}/{FinalMean:R}/{FinalMaximum:R} target={Target:R} rimInterquartileRelief={RimRelief:R} primaryDistanceDegrees={PrimaryDistance * Mathf.Rad2Deg:R} modifiedAreaFraction={Area:R} sedimentVolume={Volume:R} meanLift={MeanLift:R} maxLift={MaximumLift:R} maxAddedGrade={MaximumAddedGrade:R} protectedVertices={ProtectedVertices} collarVertices={CollarVertices} gradeLimitedVertices={GradeLimitedVertices} liftLimitedVertices={LiftLimitedVertices} largeRelativeToRelief={LargeRelativeToRelief} steepTransition={SteepTransition} finalMinVertex={FinalMinimumVertex} subseaProtected={SubseaProtected} subseaOutsideRim={SubseaOutsideRim} subseaGradeLimited={SubseaGradeLimited} subseaLiftLimited={SubseaLiftLimited} subseaBlended={SubseaBlended}");
    }

    internal sealed class Result
    {
        public readonly List<Component> Components = new();
        public Vector3[] Surface;
        public bool[] AffectedFacets, ChangedVertices;
        public int[] ComponentByVertex;
        public float[] OriginalHeights;
        public int Infilled;
        public double Area, Volume, MeanLift, MaximumLift;

        public string Describe()
        {
            var distances = Components.Where(c => double.IsFinite(c.PrimaryDistance)).Select(c => c.PrimaryDistance).OrderBy(d => d).ToArray();
            double DistanceAt(double quantile) => distances.Length == 0 ? double.NaN :
                distances[(int)Math.Round((distances.Length - 1) * quantile)] * Mathf.Rad2Deg;
            string largest = string.Join(",", Components.Where(c => c.Status == "infilled")
                .OrderByDescending(c => c.Volume).ThenBy(c => c.Id).Take(5)
                .Select(c => FormattableString.Invariant($"{c.Id}:{c.Volume:R}")));
            string flagged = string.Join(",", Components.Where(c => c.Status == "infilled" &&
                (c.LargeRelativeToRelief || c.SteepTransition)).Select(c => c.Id));
            return FormattableString.Invariant(
                $"[GeodesicBasinInfill] enabled=True excluded={Components.Count} infilled={Infilled} preserved={Components.Count - Infilled} modifiedAreaFraction={Area:R} sedimentVolumeUnityUnitsCubed={Volume:R} meanLift={MeanLift:R} maximumLift={MaximumLift:R} primaryDistanceDegreesMinP10MedianP90Max={DistanceAt(0):R}/{DistanceAt(.1):R}/{DistanceAt(.5):R}/{DistanceAt(.9):R}/{DistanceAt(1):R} largestByVolume=[{largest}] flaggedComponents=[{flagged}]");
        }
    }

    private sealed class Patch
    {
        public readonly Component Report;
        public readonly List<int> Core = new();
        public readonly List<int> Editable = new();
        public readonly List<(int Face, double Fraction)> Facets = new();
        public Patch(int id, double originalArea) => Report = new Component { Id = id, OriginalArea = originalArea };
    }

    internal static Result Plan(IGeodesicHydrologyTopology topology, Vector3[] surface,
        GeodesicOceanConnectivity ocean, float[] rawSimulation, int[] simulationCellByVertex,
        bool[] retained, float[] primaryDistanceByCell, float sea, float radius, float trendScale,
        GeodesicBasinInfillLimits settings)
    {
        var result = new Result();
        if (ocean.ExcludedComponents == 0) return result;
        var limits = settings.Normalized();
        var patches = new Patch[ocean.Components.Length];
        var candidates = new List<Patch>(ocean.ExcludedComponents);
        for (int id = 0; id < patches.Length; id++)
        {
            var basin = ocean.Components[id];
            if (basin.Retained) continue;
            var patch = new Patch(id, basin.PlanetAreaFraction);
            patches[id] = patch;
            candidates.Add(patch);
            result.Components.Add(patch.Report);
        }
        for (int cell = 0; cell < rawSimulation.Length; cell++)
        {
            if (!ocean.PotentialLakeBasin[cell]) continue;
            var report = patches[ocean.ComponentId[cell]].Report;
            report.RawFloor = double.IsNaN(report.RawFloor) ? rawSimulation[cell] : Math.Min(report.RawFloor, rawSimulation[cell]);
        }

        int count = surface.Length;
        var heights = new float[count];
        var owner = new int[count];
        var distance = new float[count];
        var anchor = new int[count];
        var proposal = new float[count];
        var smoothed = new float[count];
        Array.Fill(owner, -1);
        var frontier = new List<int>();
        for (int v = 0; v < count; v++)
        {
            heights[v] = surface[v].magnitude;
            int cell = simulationCellByVertex[v];
            if (!ocean.PotentialLakeBasin[cell]) continue;
            var patch = patches[ocean.ComponentId[cell]];
            patch.Core.Add(v);
            if (Protected(v)) { patch.Report.ProtectedVertices++; continue; }
            owner[v] = patch.Report.Id;
            patch.Editable.Add(v);
            frontier.Add(v);
        }

        // The denser render surface can still be submerged just outside a simulation
        // component. Include its immediate underwater collar without touching dry
        // land or crossing another component. The multi-source order is stable.
        for (int ring = 0, head = 0; ring < SubmergedCollarRings && head < frontier.Count; ring++)
        {
            int end = frontier.Count;
            for (; head < end; head++)
            {
                int v = frontier[head], id = owner[v];
                foreach (int n in Neighbors(v))
                {
                    if (owner[n] >= 0 || heights[n] >= sea ||
                        ocean.ComponentId[simulationCellByVertex[n]] >= 0 || Protected(n)) continue;
                    owner[n] = id;
                    patches[id].Editable.Add(n);
                    patches[id].Report.CollarVertices++;
                    frontier.Add(n);
                }
            }
        }

        var planned = new Vector3[count];
        var changed = new bool[count];
        var queue = new GeodesicInlandUplift.DistanceHeap();
        foreach (var patch in candidates)
        {
            MeasureElevations(patch, false);
            var report = patch.Report;
            if (patch.Core.Count == 0) report.Status = "below-render-resolution";
            else if (ocean.PrimaryRetainedComponent < 0) report.Status = "no-retained-primary-ocean";
            else if (patch.Editable.Count == 0) report.Status = "protected-shoreline";
            else Reconstruct(patch);
        }

        // Collect the exact union of incident facets. A facet shared by two
        // neighboring components counts only once against the planet-wide budget.
        var triangles = topology.Triangles;
        for (int f = 0; f < triangles.Length / 3; f++)
        {
            int a = triangles[3 * f], b = triangles[3 * f + 1], c = triangles[3 * f + 2];
            int first = changed[a] ? owner[a] : -1, second = changed[b] ? owner[b] : -1, third = changed[c] ? owner[c] : -1;
            if (first < 0 && second < 0 && third < 0) continue;
            double fraction = GeodesicHydrologyTopology.SphericalTriangleArea(
                topology.CellDirections[a], topology.CellDirections[b], topology.CellDirections[c]) / SphereArea;
            if (first >= 0) patches[first].Facets.Add((f, fraction));
            if (second >= 0 && second != first) patches[second].Facets.Add((f, fraction));
            if (third >= 0 && third != first && third != second) patches[third].Facets.Add((f, fraction));
        }

        var affectedFacets = new bool[triangles.Length / 3];
        double liftedArea = 0d, weightedLift = 0d;
        // A saturated budget favors large landscape features over isolated pinholes.
        foreach (var patch in candidates.OrderByDescending(p => p.Report.OriginalArea).ThenBy(p => p.Report.Id))
        {
            var report = patch.Report;
            if (report.Status != null) continue;
            double additional = 0d;
            foreach (var facet in patch.Facets)
                if (!affectedFacets[facet.Face]) additional += facet.Fraction;
            if (result.Area + additional > limits.totalArea)
            { report.Status = "total-area-budget"; continue; }

            result.Surface ??= (Vector3[])surface.Clone();
            result.ChangedVertices ??= new bool[count];
            foreach (var facet in patch.Facets)
            {
                affectedFacets[facet.Face] = true;
                report.Area += facet.Fraction;
            }
            double patchLiftArea = 0d, patchWeightedLift = 0d;
            foreach (int v in patch.Editable)
            {
                if (!changed[v]) continue;
                Vector3 position = planned[v];
                double initial = heights[v], final = position.magnitude;
                double lift = final - initial, solidAngle = topology.UnitCellAreas[v];
                patchLiftArea += solidAngle;
                patchWeightedLift += solidAngle * lift;
                // Vertex quadrature over spherical wedges, in Unity units cubed.
                report.Volume += solidAngle * lift * (final * final + final * initial + initial * initial) / 3d;
                report.MaximumLift = Math.Max(report.MaximumLift, lift);
                result.Surface[v] = position;
                result.ChangedVertices[v] = true;
            }
            report.MeanLift = patchWeightedLift / patchLiftArea;
            report.LargeRelativeToRelief = report.MaximumLift > Math.Max(3d * report.RimRelief, .01d * radius);
            report.Status = "infilled";
            result.Infilled++;
            result.Area += additional;
            result.Volume += report.Volume;
            result.MaximumLift = Math.Max(result.MaximumLift, report.MaximumLift);
            liftedArea += patchLiftArea;
            weightedLift += patchWeightedLift;
        }

        if (result.Infilled > 0)
        {
            result.AffectedFacets = affectedFacets;
            result.ComponentByVertex = owner;
            result.OriginalHeights = heights;
            result.MeanLift = weightedLift / liftedArea;
        }
        foreach (var patch in candidates)
        {
            MeasureElevations(patch, true);
            if (patch.Report.Status != "infilled") continue;
            foreach (int v in patch.Editable)
            {
                if (!result.ChangedVertices[v]) continue;
                foreach (int n in Neighbors(v))
                {
                    double finalDifference = result.Surface[v].magnitude - (double)result.Surface[n].magnitude;
                    double originalDifference = heights[v] - (double)heights[n];
                    double addedGrade = (finalDifference - Math.Max(0d, originalDifference)) / EdgeLength(v, n);
                    patch.Report.MaximumAddedGrade = Math.Max(patch.Report.MaximumAddedGrade, addedGrade);
                }
            }
            patch.Report.SteepTransition = patch.Report.MaximumAddedGrade > .9d * limits.transitionGrade + .0001d;
        }
        return result;

        void Reconstruct(Patch patch)
        {
            var report = patch.Report;
            int id = report.Id;
            bool Owns(int v) => owner[v] == id;
            bool IsDryRim(int v) => !retained[v] && heights[v] >= sea && ocean.ComponentId[simulationCellByVertex[v]] < 0;
            var rim = new HashSet<int>();
            foreach (int v in patch.Editable)
            {
                distance[v] = float.PositiveInfinity;
                anchor[v] = -1;
                foreach (int n in Neighbors(v))
                {
                    if (!IsDryRim(n)) continue;
                    rim.Add(n);
                    RelaxDistance(v, n, EdgeLength(v, n));
                }
            }
            if (rim.Count == 0) { report.Status = "no-dry-land-rim"; return; }
            float innerRadius = 0f, firstRing = 0f;
            while (queue.TryPop(out int vertex, out float d))
            {
                if (d > distance[vertex]) continue;
                innerRadius = Math.Max(innerRadius, d);
                if (Neighbors(vertex).Any(IsDryRim)) firstRing = Math.Max(firstRing, d);
                foreach (int n in Neighbors(vertex))
                    if (Owns(n)) RelaxDistance(n, anchor[vertex], d + EdgeLength(vertex, n));
            }

            var rimOrder = rim.OrderBy(v => heights[v]).ThenBy(v => v).ToArray();
            float p10 = Quantile(.10f), p25 = Quantile(.25f), p55 = Quantile(.55f), p75 = Quantile(.75f);
            report.RimRelief = p75 - p25;
            float regionalWeight = double.IsFinite(report.PrimaryDistance) && trendScale > 0f
                ? Mathf.SmoothStep(0f, 1f, (float)(report.PrimaryDistance / trendScale)) : 0f;
            float target = Mathf.Lerp(p25, p55, regionalWeight);
            report.Target = target;
            float blendWidth = Mathf.Max(2.5f * firstRing, .60f * innerRadius);
            float rimCeiling = p75 + .25f * (p75 - p25);
            Vector3 center = Vector3.zero;
            foreach (int v in patch.Core) center += topology.CellDirections[v] * topology.UnitCellAreas[v];
            center.Normalize();
            Vector3 outletDirection = Vector3.zero;
            foreach (int v in rimOrder)
            {
                if (heights[v] > p25) break;
                if (!Neighbors(v).Any(n => !Owns(n) && heights[n] < heights[v] &&
                        (retained[n] || ocean.ComponentId[simulationCellByVertex[n]] < 0))) continue;
                outletDirection = Vector3.ProjectOnPlane(topology.CellDirections[v], center).normalized;
                break;
            }
            float pitch = Mathf.Min(.02f, limits.transitionGrade * .15f, .25f * (p75 - p10) / innerRadius);
            foreach (int v in patch.Editable)
            {
                if (anchor[v] < 0) continue;
                float plane = Mathf.Clamp(target - pitch * radius * Vector3.Dot(topology.CellDirections[v], outletDirection), p10, p75);
                proposal[v] = Mathf.Lerp(Mathf.Min(heights[anchor[v]], rimCeiling), plane,
                    Mathf.SmoothStep(0f, 1f, distance[v] / blendWidth));
            }

            // A screened neighbor average smooths seams between the nearest rim samples.
            // Outside vertices are fixed, and the percentile ceiling limits mountain influence.
            for (int pass = 0; pass < SmoothingPasses; pass++)
            {
                foreach (int v in patch.Editable)
                {
                    if (anchor[v] < 0) continue;
                    float sum = 0f, weights = 0f;
                    foreach (int n in Neighbors(v))
                    {
                        float w = 1f / EdgeLength(v, n);
                        sum += w * (Owns(n) && anchor[n] >= 0 ? proposal[n] : Mathf.Min(rimCeiling, heights[n]));
                        weights += w;
                    }
                    smoothed[v] = Mathf.Lerp(proposal[v], sum / weights, .45f);
                }
                foreach (int v in patch.Editable)
                    if (anchor[v] >= 0) proposal[v] = smoothed[v];
            }

            // Cap the potential lift and then propagate a grade ceiling inward from
            // fixed neighbors and low parts of the reconstruction. Original elevations
            // are a hard lower bound, including naturally steep existing faces.
            foreach (int v in patch.Editable)
            {
                if (anchor[v] < 0) continue;
                float cap = heights[v] + limits.liftFraction * radius;
                if (proposal[v] > cap) report.LiftLimitedVertices++;
                float h = Mathf.Clamp(proposal[v], heights[v], cap);
                foreach (int n in Neighbors(v))
                    if (!Owns(n) || anchor[n] < 0) h = Mathf.Min(h, GradeCeiling(v, n, heights[n]));
                smoothed[v] = Mathf.Clamp(proposal[v], heights[v], cap);
                proposal[v] = h;
                queue.Push(v, h);
            }
            while (queue.TryPop(out int vertex, out float h))
            {
                if (h > proposal[vertex]) continue;
                foreach (int n in Neighbors(vertex))
                {
                    if (!Owns(n) || anchor[n] < 0) continue;
                    float cap = GradeCeiling(n, vertex, h);
                    if (cap >= proposal[n]) continue;
                    proposal[n] = cap;
                    queue.Push(n, cap);
                }
            }

            bool any = false;
            foreach (int v in patch.Editable)
            {
                if (anchor[v] < 0) continue;
                if (proposal[v] < smoothed[v]) report.GradeLimitedVertices++;
                if (proposal[v] - heights[v] <= radius * 1e-7f) continue;
                Vector3 position = topology.CellDirections[v] * proposal[v];
                if (position.magnitude <= heights[v]) continue;
                planned[v] = position;
                changed[v] = true;
                any = true;
            }
            if (!any) report.Status = "surface-already-at-target-or-limited";

            float Quantile(float fraction)
            {
                float index = (rimOrder.Length - 1) * fraction;
                int lower = (int)index;
                return Mathf.Lerp(heights[rimOrder[lower]], heights[rimOrder[Math.Min(lower + 1, rimOrder.Length - 1)]], index - lower);
            }

            float GradeCeiling(int v, int neighbor, float height) =>
                Mathf.Max(heights[v], height + limits.transitionGrade * EdgeLength(v, neighbor));
        }

        void RelaxDistance(int vertex, int rim, float d)
        {
            if (d > distance[vertex] || (d == distance[vertex] && rim >= anchor[vertex])) return;
            distance[vertex] = d;
            anchor[vertex] = rim;
            queue.Push(vertex, d);
        }

        void MeasureElevations(Patch patch, bool final)
        {
            if (patch.Core.Count == 0) return;
            double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
            double sum = 0d, area = 0d, distanceSum = 0d;
            foreach (int v in patch.Core)
            {
                double h = final && result.ChangedVertices != null && result.ChangedVertices[v] ? result.Surface[v].magnitude : heights[v];
                double a = topology.UnitCellAreas[v];
                if (h < minimum)
                {
                    minimum = h;
                    if (final) patch.Report.FinalMinimumVertex = v;
                }
                if (final && h < sea && patch.Report.Status == "infilled")
                {
                    if (owner[v] < 0) patch.Report.SubseaProtected++;
                    else if (anchor[v] < 0) patch.Report.SubseaOutsideRim++;
                    else if (proposal[v] < smoothed[v]) patch.Report.SubseaGradeLimited++;
                    else if (smoothed[v] >= heights[v] + limits.liftFraction * radius) patch.Report.SubseaLiftLimited++;
                    else patch.Report.SubseaBlended++;
                }
                maximum = Math.Max(maximum, h);
                sum += a * h;
                area += a;
                if (!final && primaryDistanceByCell != null) distanceSum += a * primaryDistanceByCell[simulationCellByVertex[v]];
            }
            if (final)
            {
                patch.Report.FinalMinimum = minimum;
                patch.Report.FinalMean = sum / area;
                patch.Report.FinalMaximum = maximum;
            }
            else
            {
                patch.Report.OriginalMinimum = minimum;
                patch.Report.OriginalMean = sum / area;
                patch.Report.OriginalMaximum = maximum;
                if (primaryDistanceByCell != null) patch.Report.PrimaryDistance = distanceSum / area;
            }
        }

        bool Protected(int v)
        {
            int cell = simulationCellByVertex[v];
            return retained[v] || ocean.OceanMask[cell] || ocean.CoastalLand[cell] ||
                Neighbors(v).Any(n => retained[n] || ocean.OceanMask[simulationCellByVertex[n]]);
        }

        IEnumerable<int> Neighbors(int v)
        {
            for (int slot = 0; slot < topology.NeighborCounts[v]; slot++)
                yield return topology.Neighbors6[v * 6 + slot];
        }

        float EdgeLength(int a, int b) => Math.Max(radius * 1e-6f,
            radius * (topology.CellDirections[a] - topology.CellDirections[b]).magnitude);
    }
}
