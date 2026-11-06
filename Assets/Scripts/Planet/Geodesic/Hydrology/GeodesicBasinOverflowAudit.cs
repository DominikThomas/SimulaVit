using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using static System.FormattableString;

/// <summary>Counterfactual fill-to-spill evidence on the completed TIN. The audit owns
/// only diagnostic state and reads the delivery analyzer's independent terminal totals.</summary>
internal sealed class GeodesicBasinOverflowAudit
{
    private const int MaximumOutletSteps = 4096;
    private const int FreshwaterCandidateLimit = 5;
    private const double FloodAreaFraction = .01;
    // Diagnostic shortlist limits expressed in world units relative to the
    // nominal planet radius. These are scale screens, not storage estimates.
    private const double FloodVerticalFraction = .01;
    private const string FreshwaterReview = "ExcludedInlandWaterNeedsFreshwaterReview";

    internal sealed class BasinEvidence
    {
        public readonly TinBasinDescriptor Basin;
        public BasinEvidence OceanRoot;
        public int SpillSteps, CompoundBasinCount, GeometryBlockBasin = -1;
        public string AncestryFailure, GeometryFailure;
        public bool Duplicate, WetRegionChecked, PossibleRetainedOceanOverlap, PossibleExcludedSeaOverlap, RenderedLakeOverlap;
        public double ControllingSpill => OceanRoot?.Basin.SpillElevation ?? double.NaN;
        internal string PreviousFailure, LocalFailure, FreshwaterLocalFailure, PriorRefinedFailure;
        internal int PreviousBlockBasin = -1;
        internal BasinEvidence Receiver;
        internal WetRegion Region;
        internal bool DirectOcean, RenderedLakeConflict, LargeFlood, SourceSaddleWitnessed, InitialExcludedFloor;
        internal bool? InitialBelowSeaFloor;
        internal bool ExcessiveDepth, ExcessiveHead;
        internal double RouteArea = double.NaN, RouteDepth = double.NaN;
        internal int RouteBasinCount;
        internal byte RouteVisit;
        internal byte Visit;

        public BasinEvidence(TinBasinDescriptor basin) { Basin = basin; }
        public bool StructuralOcean => OceanRoot != null;
        public bool GeometricallySupported => StructuralOcean && GeometryFailure == null && FloodFailure == null;
        internal string FloodFailure => LargeFlood ? "LargeFloodScaleReview" :
            ExcessiveDepth ? "ExcessiveFloodDepth" : ExcessiveHead ? "ExcessiveControllingHead" : null;
    }

    internal enum Outcome
    {
        ConfirmedOcean, ExistingUnresolved, VerifiedBasinContinuation, VerifiedOceanOutlet,
        ExactExcludedSeaIntersection, ProxyOnlyExcludedSeaUncertainty, InvalidHeadOrBackwater,
        MissingDownstreamEvidence, OtherBlocker
    }

    internal enum Overlap { None, BoundaryUncertain, Exact }

    internal enum FreshwaterOutcome
    {
        EligibleOverflow, ExistingRenderedLake, ReceivingBasinBlocked, FootprintEscape,
        ExcessiveArea, ExcessiveDepth, ExcessiveHead, InvalidExposedOutlet, LakeSurfaceConflict, Uncertain
    }

    private enum FreshwaterOccupancy
    {
        Unverified, InvalidOrAmbiguous, FinalDepression, ExceedsFloodScreen, PotentialFreshwater, PotentialSharedWater, ExistingRenderedLake
    }

    internal sealed class WetRegion
    {
        internal readonly HashSet<int> Facets = new HashSet<int>();
        internal double Area, Depth;
        internal Overlap Excluded;
        internal bool Measured, EarlyOcean, BoundaryOcean, EscapesFootprint;
        internal string FreshwaterBlock;
    }

    private readonly Dictionary<int, BasinEvidence> basins = new Dictionary<int, BasinEvidence>();
    private readonly Dictionary<string, int> reasons = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, double> runoffReasons = new Dictionary<string, double>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> excludedCohort = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly double[] runoffByOutcome = new double[Enum.GetValues(typeof(Outcome)).Length];
    private readonly double[] freshwaterRunoff = new double[Enum.GetValues(typeof(FreshwaterOutcome)).Length];
    private readonly List<string> freshwaterCandidates = new List<string>(FreshwaterCandidateLimit);
    private double freshwaterReviewRunoff, freshwaterBoundedSourceRunoff;
    private double largeFloodArea, verticalLimit;
    private int previousClosedOutlets, resolvedClosedOutlets, previousExcluded, resolvedExcluded;
    private double floodingClosed, invalidGeometryClosed, uncertainClosed;
    internal double Runoff(Outcome outcome) => runoffByOutcome[(int)outcome];
    internal double FreshwaterRunoff(FreshwaterOutcome outcome) => freshwaterRunoff[(int)outcome];

    private readonly List<string> opportunities = new List<string>();
    private int descriptorCount, duplicateIds, parentLinks, validLinks, invalidLinks, missingReferences;
    private int cycles, cycleNodes, oceanRoots, boundaryRoots, ambiguousRoots, terminalRoots;
    private int structuralCount, geometryCount;
    private bool excludedMappingMissing, excludedMappingUsed, classifiedGeometryUsed;
    private double total, confirmed, closed, unresolved;
    internal double StructuralClosed, GeometricClosed, MissingAncestryClosed, UnverifiedGeometryClosed;
    private double tinyClosed, tinyStructural, tinyGeometric, milliseconds;
    private long peakRegionBytes, estimatedBytes;

    internal IReadOnlyDictionary<int, BasinEvidence> Basins => basins;
    internal IEnumerable<string> Opportunities => opportunities;
    internal IEnumerable<string> FreshwaterCandidates => freshwaterCandidates;

    internal static GeodesicBasinOverflowAudit Analyze(GeodesicTinHydrology tin,
        IReadOnlyList<TinBasinDescriptor> descriptors, GeodesicDrainageDelivery delivery,
        GeodesicTinLakeGeometry renderedLakes = null, Func<int, bool> excludedAtVertex = null,
        bool excludedMappingMissing = false, ClassifiedCells classifiedCells = null, double planetRadius = double.NaN,
        Func<int, bool> initiallyBelowSeaAtVertex = null)
    {
        var clock = Stopwatch.StartNew();
        var audit = new GeodesicBasinOverflowAudit
        {
            descriptorCount = descriptors.Count,
            excludedMappingMissing = excludedMappingMissing,
            excludedMappingUsed = excludedAtVertex != null || classifiedCells != null,
            classifiedGeometryUsed = classifiedCells != null,
            largeFloodArea = tin.BasinOnSphere ? FloodAreaFraction * tin.LocalArea.Sum() : double.PositiveInfinity,
            verticalLimit = tin.BasinOnSphere && Finite(planetRadius) && planetRadius > 0
                ? FloodVerticalFraction * planetRadius : double.PositiveInfinity
        };
        foreach (var basin in descriptors)
        {
            if (audit.basins.TryGetValue(basin.Id, out var duplicate))
            {
                duplicate.Duplicate = true;
                audit.duplicateIds++;
            }
            else audit.basins.Add(basin.Id, new BasinEvidence(basin)
            {
                InitialExcludedFloor = basin.Id >= 0 && basin.Id < tin.BasinVertexCount && excludedAtVertex?.Invoke(basin.Id) == true,
                InitialBelowSeaFloor = basin.Id >= 0 && basin.Id < tin.BasinVertexCount ? initiallyBelowSeaAtVertex?.Invoke(basin.Id) : null
            });
        }

        var downstreamFirst = audit.ResolveAncestry(tin);
        var renderedLevels = RenderedFacetLevels(renderedLakes);
        var groups = downstreamFirst.Where(b => b.StructuralOcean).GroupBy(b => b.OceanRoot).ToArray();
        var lakeOwners = RenderedFacetOwners(renderedLakes);
        var groupChecks = groups.Select(group =>
            audit.CheckWetRegion(tin, group.Key, group, renderedLevels, lakeOwners, excludedAtVertex, classifiedCells)).ToArray();
        for (int i = 0; i < groups.Length; i++)
            audit.CheckGroupSpills(tin, groups[i], groupChecks[i], renderedLevels, lakeOwners, excludedAtVertex, classifiedCells);

        // Reproduce the previous refined first blocker on the same terminal
        // geometry, then evaluate freshwater semantics on those same legs.
        var structural = downstreamFirst.Where(b => b.StructuralOcean).ToArray();
        foreach (var b in structural) ResolveRoute(b, audit.largeFloodArea);
        foreach (var b in structural)
        {
            b.PriorRefinedFailure = b.GeometryFailure ?? b.FloodFailure;
            b.LocalFailure = b.FreshwaterLocalFailure;
            b.RouteVisit = 0;
            b.RouteArea = b.RouteDepth = double.NaN;
            b.RouteBasinCount = 0;
            b.LargeFlood = false;
        }
        foreach (var b in structural)
        {
            ResolveRoute(b, audit.largeFloodArea);
            b.ExcessiveDepth = b.RouteDepth > audit.verticalLimit;
            b.ExcessiveHead = b.ControllingSpill - tin.BasinSeaLevel > audit.verticalLimit;
        }

        foreach (var b in audit.basins.Values)
        {
            if (b.StructuralOcean) audit.structuralCount++;
            if (b.GeometricallySupported) audit.geometryCount++;
            var reason = b.AncestryFailure ?? b.GeometryFailure ?? b.FloodFailure;
            if (reason != null) Increment(audit.reasons, reason);
            if (b.PreviousFailure == "OutletEndsAtTrueClosedBasin")
            {
                audit.previousClosedOutlets++;
                if (b.GeometricallySupported) audit.resolvedClosedOutlets++;
            }
            if (b.PreviousFailure == "PossibleExcludedSeaOverlap")
            {
                audit.previousExcluded++;
                if (b.GeometricallySupported) audit.resolvedExcluded++;
                Increment(audit.excludedCohort, ExcludedClassification(b.OceanRoot, classifiedCells != null));
            }
        }
        audit.AccountRunoff(tin, delivery, renderedLevels, lakeOwners);
        // Estimates include object/map slack and the largest sequential wet-region workspace.
        audit.estimatedBytes = 448L * audit.basins.Count + 48L * (renderedLevels.Count + lakeOwners.Count) + audit.peakRegionBytes
            + groups.Sum(g => 32L * g.Key.Region.Facets.Count)
            + audit.opportunities.Sum(line => 32L + 2L * line.Length);
        audit.milliseconds = clock.Elapsed.TotalMilliseconds;
        return audit;
    }

    private List<BasinEvidence> ResolveAncestry(GeodesicTinHydrology tin)
    {
        var ordered = new List<BasinEvidence>(basins.Count);
        var path = new List<BasinEvidence>();
        foreach (var start in basins.Values.OrderBy(b => b.Basin.Id))
        {
            var current = start;
            while (current != null && current.Visit == 0)
            {
                current.Visit = 1;
                path.Add(current);
                current = ParentOf(current.Basin);
            }
            if (current != null && current.Visit == 1)
            {
                cycles++;
                for (int i = path.IndexOf(current); i < path.Count; i++)
                {
                    path[i].AncestryFailure = "ParentCycle";
                    cycleNodes++;
                }
            }
            for (int i = path.Count - 1; i >= 0; i--)
            {
                var b = path[i];
                var parent = ParentOf(b.Basin);
                var failure = ValidateLink(tin, b, parent);
                b.AncestryFailure = b.AncestryFailure ?? failure ?? parent?.AncestryFailure;
                if (b.AncestryFailure == null)
                {
                    b.OceanRoot = parent == null ? b : parent.OceanRoot;
                    b.SpillSteps = (parent?.SpillSteps ?? 0) + 1;
                }
                b.Visit = 2;
                ordered.Add(b);
            }
            path.Clear();
        }
        return ordered;
    }

    private BasinEvidence ParentOf(TinBasinDescriptor basin) => basin.ParentBasinId >= 0 &&
        basins.TryGetValue(basin.ParentBasinId, out var parent) ? parent : null;

    private string ValidateLink(GeodesicTinHydrology tin, BasinEvidence evidence, BasinEvidence parent)
    {
        var b = evidence.Basin;
        if (b.ParentBasinId >= 0)
        {
            parentLinks++;
            if (parent == null) missingReferences++;
        }
        if (b.ParentBasinId == -1)
        {
            if (b.Terminal) terminalRoots++;
            else if (b.ExternalSource == TinBasinExternalSource.RetainedOcean) oceanRoots++;
            else if (b.ExternalSource == TinBasinExternalSource.OpenBoundary) boundaryRoots++;
            else ambiguousRoots++;
        }

        if (evidence.Duplicate) return "DuplicateBasinId";
        if (CornerVertex(tin, b.Floor) != b.Id || b.Id < 0 ||
            !MatchesHeight(tin.Height(b.Floor), b.FloorElevation, tin.Epsilon)) return "InvalidFloor";
        if (b.ParentBasinId < -1 || (b.Terminal && b.ParentBasinId != -1)) return "InvalidTerminalReference";
        if (b.Terminal) return "TerminalBasin";
        if (!Finite(b.SpillElevation) || b.SpillElevation <= b.FloorElevation) return "InvalidSpillElevation";
        if (b.ParentBasinId == -1)
        {
            if (b.ExternalSource == TinBasinExternalSource.RetainedOcean) return null;
            return b.ExternalSource == TinBasinExternalSource.OpenBoundary ? "OpenBoundaryRoot" : "AmbiguousExternalRoot";
        }
        if (parent == null) return "MissingParent";
        if (parent.Duplicate) return "AmbiguousParent";
        var p = parent.Basin;
        if (b.ExternalSource != TinBasinExternalSource.None || !Finite(p.FloorElevation) ||
            p.FloorElevation > b.FloorElevation || (p.FloorElevation == b.FloorElevation && p.Id >= b.Id))
        {
            invalidLinks++;
            return "InvalidElderRelationship";
        }
        if (p.HasEscape && (!Finite(p.SpillElevation) || p.SpillElevation < b.SpillElevation))
        {
            invalidLinks++;
            return "ParentSpillBelowChild";
        }
        validLinks++;
        return null;
    }

    private static Dictionary<int, double> RenderedFacetLevels(GeodesicTinLakeGeometry geometry)
    {
        var levels = new Dictionary<int, double>();
        if (geometry == null) return levels;
        foreach (var body in geometry.Bodies)
            for (int i = body.FirstVertex; i < body.FirstVertex + body.VertexCount; i++)
            {
                int facet = geometry.TerrainPoints[i].Triangle;
                if (!levels.TryGetValue(facet, out double level) || level < body.WaterLevel)
                    levels[facet] = body.WaterLevel;
            }
        return levels;
    }

    private static Dictionary<int, int> RenderedFacetOwners(GeodesicTinLakeGeometry geometry)
    {
        var owners = new Dictionary<int, int>();
        if (geometry == null) return owners;
        foreach (var body in geometry.Bodies)
            for (int i = body.FirstVertex; i < body.FirstVertex + body.VertexCount; i++)
            {
                int facet = geometry.TerrainPoints[i].Triangle;
                owners[facet] = owners.TryGetValue(facet, out int owner) && owner != body.BasinId ? -1 : body.BasinId;
            }
        return owners;
    }

    private static void Increment(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

    private static Overlap Max(Overlap a, Overlap b) => a > b ? a : b;

    private static string ExcludedClassification(BasinEvidence root, bool exactMapping)
    {
        var region = root.Region;
        if (region.EarlyOcean) return "RetainedOcean";
        if (region.Excluded == Overlap.Exact) return "ExactFreshwaterReview";
        if (region.BoundaryOcean || region.Excluded == Overlap.BoundaryUncertain || !exactMapping || !region.Measured)
            return "BoundaryOrMappingUncertain";
        return root.PossibleExcludedSeaOverlap ? "ProxyOnlyNoIntersection" : "NoIntersection";
    }

    private (string Legacy, string Refined) CheckWetRegion(GeodesicTinHydrology tin, BasinEvidence root, IEnumerable<BasinEvidence> group,
        Dictionary<int, double> renderedLevels, Dictionary<int, int> lakeOwners,
        Func<int, bool> excludedAtVertex, ClassifiedCells classifiedCells)
    {
        // Strictly submerged vertices give a conservative connected-water certificate.
        // A point-only, level saddle connection without a submerged path stays unverified.
        double head = root.Basin.SpillElevation;
        root.CompoundBasinCount = group.Count();
        var region = root.Region = new WetRegion();
        var facets = region.Facets;
        var wet = new HashSet<int>();
        bool invalidFacet = false, incompatibleHeight = false, exposedWetCorner = false;
        foreach (int facet in root.Basin.MemberTriangles)
        {
            if (!ValidFacet(tin, facet)) { invalidFacet = true; continue; }
            bool wetFacet = false, excludedCorner = false, differentHeights = false;
            for (int k = 0; k < 3; k++)
            {
                int vertex = tin.BasinVertex(facet, k);
                double radius = tin.CompletedVisibleRadius(tin.BasinPoint(facet, vertex));
                if (tin.HydrologyHeight[vertex] < head)
                {
                    wetFacet = true;
                    wet.Add(vertex);
                    exposedWetCorner |= radius >= head;
                }
                differentHeights |= tin.HydrologyHeight[vertex] != tin.VisibleHeight[vertex] ||
                    !MatchesHeight(radius, tin.VisibleHeight[vertex], tin.Epsilon);
                excludedCorner |= excludedAtVertex?.Invoke(vertex) == true;
            }
            if (!wetFacet) continue;
            facets.Add(facet);
            incompatibleHeight |= differentHeights;
            root.PossibleRetainedOceanOverlap |= tin.BasinFacetIntersectsOcean(facet);
            root.PossibleExcludedSeaOverlap |= excludedCorner;
            root.RenderedLakeOverlap |= renderedLevels.ContainsKey(facet);
        }
        root.WetRegionChecked = !invalidFacet && facets.Count > 0;
        int wetCount = wet.Count;
        string regionFailure = invalidFacet ? "InvalidWetFootprintFacet" : incompatibleHeight ? "VisibleAndHydrologyHeightsDiffer" :
            exposedWetCorner ? "WetCornerAtOrAboveHead" : !Finite(tin.BasinSeaLevel) ? "InvalidSeaLevel" :
            head < tin.BasinSeaLevel ? "ReceivingOceanAboveSpill" : null;
        string legacyFailure = regionFailure ?? (root.PossibleRetainedOceanOverlap ? "PossibleRetainedOceanOverlap" :
            excludedMappingMissing ? "ExcludedSeaMappingUnavailable" : root.PossibleExcludedSeaOverlap ? "PossibleExcludedSeaOverlap" : null);

        if (regionFailure == null)
        {
            var pending = new Queue<int>();
            if (wet.Remove(root.Basin.Id)) pending.Enqueue(root.Basin.Id);
            while (pending.Count > 0)
            {
                int v = pending.Dequeue();
                for (int i = tin.BasinIncidentStart(v); i < tin.BasinIncidentEnd(v); i++)
                {
                    int facet = tin.BasinIncidentTriangle(i);
                    if (!facets.Contains(facet))
                    {
                        region.EscapesFootprint |= ValidFacet(tin, facet);
                        continue;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        int next = tin.BasinVertex(facet, k);
                        if (wet.Remove(next)) pending.Enqueue(next);
                    }
                }
            }
            if (wetCount == 0 || wet.Count != 0) regionFailure = "WetRegionDisconnectedOrLevelOnly";
        }
        legacyFailure = legacyFailure ?? regionFailure;

        if (regionFailure == null)
        {
            MeasureWetRegion(tin, root, lakeOwners, renderedLevels, classifiedCells);
            root.CompoundBasinCount = group.Count(b =>
                ContainsSorted(root.Basin.MemberTriangles, b.Basin.Floor.Triangle) && b.Basin.FloorElevation < head);
            regionFailure = region.EarlyOcean ? "RetainedOceanBeforeSaddle" :
                region.BoundaryOcean ? "AmbiguousRetainedOceanBoundary" :
                region.EscapesFootprint ? "UnboundedFloodFootprint" : null;
        }
        string refinedFailure = regionFailure ??
            (region.Excluded == Overlap.Exact ? FreshwaterReview :
             region.Excluded == Overlap.BoundaryUncertain ||
             (classifiedCells == null && (excludedMappingMissing || root.PossibleExcludedSeaOverlap)) ? "ExcludedSeaBoundaryUncertain" :
             root.RenderedLakeConflict ? "IncompatibleRenderedLakeOwnership" : null);
        peakRegionBytes = Math.Max(peakRegionBytes, 36L * wetCount);
        return (legacyFailure, refinedFailure);
    }

    private void CheckGroupSpills(GeodesicTinHydrology tin, IGrouping<BasinEvidence, BasinEvidence> group,
        (string Legacy, string Refined) regionFailures, Dictionary<int, double> renderedLevels,
        Dictionary<int, int> lakeOwners, Func<int, bool> excludedAtVertex, ClassifiedCells classifiedCells)
    {
        var root = group.Key;
        root.Region.FreshwaterBlock = regionFailures.Refined == FreshwaterReview
            ? (root.RenderedLakeConflict ? "IncompatibleRenderedLakeOwnership" : null)
            : regionFailures.Refined;
        (TinPoint[] Path, TinTermination End) trace = default;
        foreach (var b in group)
        {
            var basin = b.Basin;
            var parent = ParentOf(basin);
            string previous = parent?.PreviousFailure ?? regionFailures.Legacy;
            b.PreviousBlockBasin = parent?.PreviousFailure != null ? parent.PreviousBlockBasin : root.Basin.Id;
            if (previous == null)
            {
                previous = basin.SubseaUnclassified ? "SubseaFloorUnclassified" :
                    CheckSpill(tin, b, parent, renderedLevels, lakeOwners, excludedAtVertex, null, true, ref trace);
                b.PreviousBlockBasin = previous == null ? -1 : basin.Id;
            }
            b.PreviousFailure = previous;
            b.Receiver = parent;
            string spillFailure = root.Region.Measured
                ? CheckSpill(tin, b, parent, renderedLevels, lakeOwners, excludedAtVertex, classifiedCells, false, ref trace)
                : null;
            b.LocalFailure = regionFailures.Refined ?? (basin.SubseaUnclassified ? "SubseaFloorUnclassified" : spillFailure);
            // An initial exclusion has no bearing on water already bounded by
            // this final wet component. Keep the boundary and ownership gates.
            b.FreshwaterLocalFailure = root.Region.FreshwaterBlock ??
                (basin.SubseaUnclassified && !b.SourceSaddleWitnessed ? "SubseaFloorUnclassified" : spillFailure);
        }
        int traceSamples = trace.Path?.Length ?? 0;
        if (traceSamples > 0)
            peakRegionBytes = Math.Max(peakRegionBytes, 128L * traceSamples + 4096L);
    }

    private static void MeasureWetRegion(GeodesicTinHydrology tin, BasinEvidence root,
        Dictionary<int, int> lakeOwners, Dictionary<int, double> lakeLevels, ClassifiedCells classifiedCells)
    {
        var region = root.Region;
        double head = root.ControllingSpill;
        region.Measured = true;
        foreach (int facet in region.Facets)
        {
            var polygon = GeodesicTinLakeGeometry.ClipTriangle(tin, facet, head);
            double minimum = tin.MinimumCompletedRadius(polygon);
            region.Area += AreaFraction(polygon) * tin.LocalArea[facet];
            region.Depth = Math.Max(region.Depth, head - minimum);
            if (tin.BasinFacetRetained(facet))
            {
                region.EarlyOcean |= minimum < tin.BasinSeaLevel - tin.Epsilon;
                // A tangency at the recorded outlet is handled by the spill witness.
                region.BoundaryOcean |= minimum <= tin.BasinSeaLevel + tin.Epsilon &&
                    (head > tin.BasinSeaLevel + tin.Epsilon || !TouchesCorner(tin, facet, root.Basin.Spill.Value));
                if (minimum > tin.BasinSeaLevel + tin.Epsilon && tin.BasinFacetIntersectsOcean(facet))
                    region.BoundaryOcean = true;
            }
            if (classifiedCells != null && region.Excluded != Overlap.Exact)
                region.Excluded = Max(region.Excluded, classifiedCells.InFacet(tin, facet, polygon));
            if (lakeOwners.TryGetValue(facet, out int owner) &&
                (owner != root.Basin.Id || !MatchesHeight(lakeLevels[facet], head, tin.Epsilon)))
                root.RenderedLakeConflict = true;

            for (int k = 0; k < 3; k++)
            {
                int neighbor = tin.Neighbors[facet * 3 + k];
                if (neighbor >= 0 && region.Facets.Contains(neighbor)) continue;
                var a = tin.BasinPoint(facet, tin.BasinVertex(facet, (k + 1) % 3));
                var b = tin.BasinPoint(facet, tin.BasinVertex(facet, (k + 2) % 3));
                if (tin.MinimumOutletSegmentRadius(a, b) < head - tin.Epsilon)
                    region.EscapesFootprint = true;
            }
        }
    }

    private static bool TouchesCorner(GeodesicTinHydrology tin, int facet, TinPoint corner) =>
        CornerVertex(tin, tin.BasinPoint(facet, CornerVertex(tin, corner))) >= 0;

    private static double AreaFraction(IReadOnlyList<TinPoint> polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            sum += a.B * b.C - b.B * a.C;
        }
        return Math.Abs(sum);
    }

    private string CheckSpill(GeodesicTinHydrology tin, BasinEvidence evidence, BasinEvidence parent,
        Dictionary<int, double> renderedLevels, Dictionary<int, int> lakeOwners, Func<int, bool> excludedAtVertex,
        ClassifiedCells classifiedCells, bool legacy, ref (TinPoint[] Path, TinTermination End) trace)
    {
        var basin = evidence.Basin;
        var facets = evidence.OceanRoot.Region.Facets;
        var spill = basin.Spill.Value;
        int saddle = CornerVertex(tin, spill);
        int facet = basin.DownstreamTriangle, vertex = basin.DownstreamVertex;
        double head = evidence.ControllingSpill;
        if (saddle < 0 || !MatchesHeight(tin.Height(spill), basin.SpillElevation, tin.Epsilon) ||
            !MatchesHeight(tin.CompletedVisibleRadius(spill), basin.SpillElevation, tin.Epsilon))
            return "SpillDoesNotMatchFinalSurface";
        if (!TouchesWetRegion(tin, basin.Id, facets, head) || !TouchesWetRegion(tin, saddle, facets, head))
            return "FloorOrSpillOutsideCompoundWater";
        if (!legacy && !TouchesWetRegion(tin, saddle, basin.MemberTriangles, basin.SpillElevation))
            return "SpillOutsideSourceWater";
        if (!legacy) evidence.SourceSaddleWitnessed = true;
        // A recorded external merge can be exactly at the retained shoreline.
        if (parent == null && tin.IsRetainedOceanBoundary(spill))
        {
            if (!legacy) evidence.DirectOcean = true;
            return null;
        }
        if (!ValidFacet(tin, facet) || vertex < 0 || vertex >= tin.BasinVertexCount || vertex == saddle)
            return "MissingOrInvalidDownstreamWitness";
        var start = tin.BasinPoint(facet, saddle);
        var side = tin.BasinPoint(facet, vertex);
        if (CornerVertex(tin, start) != saddle || CornerVertex(tin, side) != vertex) return "IncompatibleDownstreamWitness";
        double sideHeight = tin.Height(side), sideRadius = tin.CompletedVisibleRadius(side);
        if (!Finite(sideHeight) || !Finite(sideRadius) || sideHeight > basin.SpillElevation || sideRadius > basin.SpillElevation + tin.Epsilon)
            return "IncompatibleDownstreamWitness";
        if (parent != null)
        {
            if (!basin.MemberTriangles.All(facets.Contains)) return "ChildFootprintOutsideCompoundWater";
            if (!facets.Contains(facet) || !ContainsSorted(parent.Basin.MemberTriangles, facet) || sideHeight >= head || sideRadius >= head)
                return "WitnessOutsideReceivingWater";
            if (!legacy && (sideHeight >= parent.Basin.SpillElevation || sideRadius >= parent.Basin.SpillElevation))
                return "WitnessOutsideReceivingWater";
            return null;
        }

        bool InWater(TinPoint point) => facets.Contains(point.Triangle) && tin.Height(point) < head;
        if (trace.Path == null)
        {
            var samples = tin.TraceLakeOutlet(spill, (a, b) => a.Triangle == facet && !InWater(b),
                out var end, MaximumOutletSteps);
            trace = (samples, end);
        }
        var (path, termination) = trace;
        bool closesInBasin = termination == TinTermination.TrueClosedBasin || termination == TinTermination.TinyNoiseDepression;
        if (termination != TinTermination.OceanCoast && (legacy || !closesInBasin))
            return termination == TinTermination.RoutingFailure ? "OutletRoutingFailureOrTraceLimit" : "OutletEndsAt" + termination;
        var endpoint = path[path.Length - 1];
        if (termination == TinTermination.OceanCoast && (path.Length < 2 || !tin.IsRetainedOceanBoundary(endpoint)))
            return "AmbiguousRetainedOceanEndpoint";
        if (legacy)
        {
            foreach (var point in path)
            {
                if (InWater(point)) return "OutletReentersCompoundWater";
                if (renderedLevels.TryGetValue(point.Triangle, out double waterLevel) && tin.Height(point) < waterLevel)
                    return "OutletEntersExistingRenderedLake";
                if (excludedAtVertex != null)
                    for (int k = 0; k < 3; k++)
                        if (excludedAtVertex(tin.BasinVertex(point.Triangle, k))) return "OutletMayIntersectExcludedSea";
            }
            return null;
        }

        BasinEvidence receiver = null;
        if (closesInBasin)
        {
            int floor = CornerVertex(tin, endpoint, 1e-9);
            if (!basins.TryGetValue(floor, out receiver) || !receiver.StructuralOcean ||
                !ContainsSorted(receiver.Basin.MemberTriangles, endpoint.Triangle) ||
                !MatchesHeight(tin.Height(endpoint), receiver.Basin.FloorElevation, tin.Epsilon) ||
                tin.CompletedVisibleRadius(endpoint) >= receiver.Basin.SpillElevation)
                return "MissingReceivingBasinEvidence";
            evidence.Receiver = receiver;
            if (receiver.OceanRoot == evidence || receiver.ControllingSpill > head)
                return "InvalidReceivingHeadOrBackwater";
        }
        if (receiver != null)
        {
            path = ClipAtReceivingWater(tin, path, receiver.OceanRoot);
            if (path == null) return "MissingReceivingWaterContact";
        }
        if (path.Length < 2 || !tin.VerifyExposedOutlet(path)) return "ExposedOutletRisesOnFinalSurface";
        for (int i = 0; i < path.Length; i++)
        {
            var point = path[i];
            bool sameFacet = i > 0 && path[i - 1].Triangle == point.Triangle;
            if (InWater(point) || (sameFacet && facets.Contains(point.Triangle) &&
                tin.MinimumOutletSegmentRadius(path[i - 1], point) < head - tin.Epsilon))
                return "OutletReentersCompoundWater";
            if (renderedLevels.TryGetValue(point.Triangle, out double waterLevel) && tin.Height(point) < waterLevel &&
                (receiver == null || lakeOwners[point.Triangle] != receiver.OceanRoot.Basin.Id ||
                 !MatchesHeight(waterLevel, receiver.ControllingSpill, tin.Epsilon)))
                return "OutletEntersExistingRenderedLake";
            if (classifiedCells != null && sameFacet)
            {
                var overlap = classifiedCells.OnSegment(tin, path[i - 1], point);
                // Outside a certified pool, a below-sea segment in an excluded
                // cell supplies no evidence about the receiving water level.
                if (overlap == Overlap.Exact) return "ExposedOutletBelowSeaInExcludedCell";
                if (overlap == Overlap.BoundaryUncertain) return "ExcludedSeaBoundaryUncertain";
            }
            else if (classifiedCells == null && excludedAtVertex != null)
                for (int k = 0; k < 3; k++)
                    if (excludedAtVertex(tin.BasinVertex(point.Triangle, k))) return "ExcludedSeaBoundaryUncertain";
        }
        evidence.DirectOcean = receiver == null;
        return null;
    }

    private static TinPoint[] ClipAtReceivingWater(GeodesicTinHydrology tin, TinPoint[] path, BasinEvidence root)
    {
        double level = root.ControllingSpill;
        bool BelowSurface(TinPoint p) => tin.Height(p) < level && tin.CompletedVisibleRadius(p) < level;
        for (int i = 1; i < path.Length; i++)
        {
            var b = path[i];
            if (!root.Region.Facets.Contains(b.Triangle) || !BelowSurface(b)) continue;
            var a = path[i - 1];
            if (a.Triangle == b.Triangle && !BelowSurface(a))
            {
                double low = 0, high = 1;
                for (int step = 0; step < 40; step++)
                {
                    double f = (low + high) / 2;
                    var p = Interpolate(a, b, f);
                    if (BelowSurface(p)) high = f; else low = f;
                }
                b = Interpolate(a, b, high);
            }
            var exposed = new TinPoint[i + 1];
            Array.Copy(path, exposed, i);
            exposed[i] = b;
            return exposed;
        }
        return null;
    }

    private static TinPoint Interpolate(TinPoint a, TinPoint b, double fraction) => new TinPoint(a.Triangle,
        a.A + fraction * (b.A - a.A), a.B + fraction * (b.B - a.B), a.C + fraction * (b.C - a.C));

    private static void ResolveRoute(BasinEvidence start, double largeFloodArea)
    {
        if (start.RouteVisit == 2) return;
        var path = new List<BasinEvidence>();
        var b = start;
        while (b != null && b.RouteVisit == 0)
        {
            b.RouteVisit = 1;
            path.Add(b);
            b = b.LocalFailure == null ? b.Receiver : null;
        }
        bool cycle = b?.RouteVisit == 1;
        for (int i = path.Count - 1; i >= 0; i--)
        {
            b = path[i];
            var next = b.Receiver;
            string failure = b.LocalFailure ?? (cycle ? "OutletCycle" : next?.GeometryFailure);
            b.GeometryBlockBasin = failure == null ? -1 :
                b.LocalFailure != null || cycle ? b.Basin.Id : next.GeometryBlockBasin;
            if (failure == null && next == null && !b.DirectOcean)
            {
                failure = "MissingDownstreamRoutingEvidence";
                b.GeometryBlockBasin = b.Basin.Id;
            }
            if (failure == null)
            {
                var region = b.OceanRoot.Region;
                bool joinsRegion = next != null && next.OceanRoot != b.OceanRoot;
                // Distinct footprints can have coincident boundary facets. Their
                // sublevel polygons would overlap there, so separate heads cannot
                // certify them as separate reservoirs.
                if (joinsRegion && HasDownstreamOverlap(b.OceanRoot, next))
                {
                    failure = "InvalidReceivingHeadOrBackwater";
                    b.GeometryBlockBasin = b.Basin.Id;
                }
                else
                {
                    b.RouteArea = joinsRegion ? region.Area + next.RouteArea : next?.RouteArea ?? region.Area;
                    b.RouteDepth = joinsRegion ? Math.Max(region.Depth, next.RouteDepth) : next?.RouteDepth ?? region.Depth;
                    int count = b.OceanRoot.CompoundBasinCount;
                    b.RouteBasinCount = joinsRegion ? count + next.RouteBasinCount : next?.RouteBasinCount ?? count;
                    b.LargeFlood = b.RouteArea > largeFloodArea;
                }
            }
            b.GeometryFailure = failure;
            b.RouteVisit = 2;
        }
    }

    private static bool HasDownstreamOverlap(BasinEvidence sourceRoot, BasinEvidence next)
    {
        for (; next != null; next = next.Receiver)
            if (sourceRoot.Region.Facets.Overlaps(next.OceanRoot.Region.Facets)) return true;
        return false;
    }

    private static bool TouchesWetRegion(GeodesicTinHydrology tin, int vertex, IReadOnlyList<int> facets, double level) =>
        TouchesWetRegion(tin, vertex, facet => ContainsSorted(facets, facet), level);

    private static bool TouchesWetRegion(GeodesicTinHydrology tin, int vertex, HashSet<int> facets, double level) =>
        TouchesWetRegion(tin, vertex, facets.Contains, level);

    private static bool TouchesWetRegion(GeodesicTinHydrology tin, int vertex, Func<int, bool> includes, double level)
    {
        for (int i = tin.BasinIncidentStart(vertex); i < tin.BasinIncidentEnd(vertex); i++)
        {
            int facet = tin.BasinIncidentTriangle(i);
            if (!includes(facet)) continue;
            for (int k = 0; k < 3; k++)
                if (tin.HydrologyHeight[tin.BasinVertex(facet, k)] < level) return true;
        }
        return false;
    }

    private static int CornerVertex(GeodesicTinHydrology tin, TinPoint point, double tolerance = 0)
    {
        if (!ValidFacet(tin, point.Triangle)) return -1;
        if (Math.Abs(1 - point.A) <= tolerance && Math.Abs(point.B) <= tolerance && Math.Abs(point.C) <= tolerance) return tin.BasinVertex(point.Triangle, 0);
        if (Math.Abs(point.A) <= tolerance && Math.Abs(1 - point.B) <= tolerance && Math.Abs(point.C) <= tolerance) return tin.BasinVertex(point.Triangle, 1);
        if (Math.Abs(point.A) <= tolerance && Math.Abs(point.B) <= tolerance && Math.Abs(1 - point.C) <= tolerance) return tin.BasinVertex(point.Triangle, 2);
        return -1;
    }

    private static bool ContainsSorted(IReadOnlyList<int> facets, int facet)
    {
        int low = 0, high = facets.Count - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (facets[mid] == facet) return true;
            if (facets[mid] < facet) low = mid + 1;
            else high = mid - 1;
        }
        return false;
    }

    private static bool ValidFacet(GeodesicTinHydrology tin, int facet) => facet >= 0 && facet < tin.TriangleCount && tin.BasinFaceValid(facet);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool MatchesHeight(double a, double b, double epsilon) => Finite(a) && Finite(b) && Math.Abs(a - b) <= epsilon;
    private static string Number(double value) => Finite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "unknown";

    private void AccountRunoff(GeodesicTinHydrology tin, GeodesicDrainageDelivery delivery,
        Dictionary<int, double> renderedLevels, Dictionary<int, int> lakeOwners)
    {
        total = delivery.TotalRunoff;
        confirmed = delivery.DirectOcean + delivery.ViaLakesOcean;
        closed = delivery.InlandClosed;
        unresolved = delivery.Unresolved;
        runoffByOutcome[(int)Outcome.ConfirmedOcean] = confirmed;
        runoffByOutcome[(int)Outcome.ExistingUnresolved] = unresolved;
        var candidateRunoff = new Dictionary<BasinEvidence, double>();
        foreach (var catchment in delivery.DisconnectedCatchments)
        {
            if (catchment.Terminal.Destination != GeodesicDrainageDelivery.Destination.InlandClosed) continue;
            var b = EvidenceFor(catchment);
            double runoff = catchment.Runoff;
            bool tiny = catchment.Terminal.Type == nameof(TinTermination.TinyNoiseDepression);
            if (tiny) tinyClosed += runoff;
            string failure = FailureFor(tin, catchment, b);
            var outcome = Classify(failure, b?.Receiver != null);
            runoffByOutcome[(int)outcome] += runoff;
            if (b?.StructuralOcean == true)
            {
                bool existing = HasMatchingRenderedLake(tin, catchment, b, renderedLevels, lakeOwners);
                if (b.PriorRefinedFailure == FreshwaterReview)
                {
                    freshwaterReviewRunoff += runoff;
                    freshwaterRunoff[(int)ClassifyFreshwater(b, failure, existing)] += runoff;
                    var sourceOccupancy = Occupancy(tin, b, existing);
                    if (TerminalInSourceWater(tin, catchment, b) &&
                        (sourceOccupancy == FreshwaterOccupancy.PotentialFreshwater ||
                         sourceOccupancy == FreshwaterOccupancy.PotentialSharedWater ||
                         sourceOccupancy == FreshwaterOccupancy.ExistingRenderedLake))
                        freshwaterBoundedSourceRunoff += runoff;
                }
                if (failure == null && !existing)
                    candidateRunoff[b] = candidateRunoff.TryGetValue(b, out double input) ? input + runoff : runoff;
            }
            if (failure != null)
                runoffReasons[failure] = runoffReasons.TryGetValue(failure, out double amount) ? amount + runoff : runoff;
            if (b?.StructuralOcean != true) { MissingAncestryClosed += runoff; continue; }
            StructuralClosed += runoff;
            if (tiny) tinyStructural += runoff;
            if (failure != null)
            {
                UnverifiedGeometryClosed += runoff;
                if (failure == "UnboundedFloodFootprint" || failure == b.FloodFailure) floodingClosed += runoff;
                else if (InvalidGeometry(failure)) invalidGeometryClosed += runoff;
                else uncertainClosed += runoff;
                continue;
            }
            GeometricClosed += runoff;
            if (tiny) tinyGeometric += runoff;
        }
        RankFreshwaterCandidates(tin, candidateRunoff);
        int rank = 0;
        foreach (var catchment in delivery.LargestDisconnected)
        {
            var b = EvidenceFor(catchment);
            var root = b?.OceanRoot;
            var basin = catchment.Terminal.Basin;
            bool isClosed = catchment.Terminal.Destination == GeodesicDrainageDelivery.Destination.InlandClosed;
            string failure = isClosed ? FailureFor(tin, catchment, b)
                : "ExistingUnresolved:" + catchment.Terminal.Type;
            double head = b?.ControllingSpill ?? double.NaN;
            bool existing = b?.StructuralOcean == true && HasMatchingRenderedLake(tin, catchment, b, renderedLevels, lakeOwners);
            var occupancy = Occupancy(tin, b, existing);
            double finalFloor = b != null && ValidFacet(tin, b.Basin.Floor.Triangle)
                ? tin.CompletedVisibleRadius(b.Basin.Floor) : double.NaN;
            var route = ReceivingRoute(b, out bool cycle);
            double routeHead = route.Count > 0 ? route.Max(leg => leg.ControllingSpill) : double.NaN;
            bool earlyOcean = route.Any(leg => leg.OceanRoot?.Region.EarlyOcean == true);
            bool floodEscape = route.Any(leg => leg.OceanRoot?.Region.EscapesFootprint == true);
            bool overlapsKnown = root?.WetRegionChecked == true;
            bool structural = isClosed && b?.StructuralOcean == true;
            string steps = structural ? b.SpillSteps.ToString(CultureInfo.InvariantCulture) : "unknown";
            opportunities.Add(
                Invariant($"[GeodesicBasinOverflowOpportunity] rank={++rank} terminalId={catchment.Terminal.TerminalId} terminalType={catchment.Terminal.Type} currentDestination={catchment.Terminal.Destination} ") +
                Invariant($"runoff={catchment.Runoff:R} runoffPercent={Percent(catchment.Runoff):F6} structuralOcean={structural} geometry={(failure == null ? "SupportedCounterfactual" : "Unverified")} ") +
                Invariant($"spillSteps={steps} floor={Number(basin?.FloorElevation ?? double.NaN)} firstSpill={Number(basin?.SpillElevation ?? double.NaN)} controllingSpill={Number(head)} minimumHeadIncludingOcean={Number(Math.Max(head, tin.BasinSeaLevel))} ") +
                Invariant($"oceanRootBasin={root?.Basin.Id ?? -1} compoundBasins={root?.CompoundBasinCount ?? 0} higherSharedLevel={Flag(structural, basin != null && head > basin.SpillElevation)} wetRegionCheckedAtSpill={overlapsKnown} ") +
                Invariant($"possibleRetainedOceanOverlap={Flag(overlapsKnown, root?.PossibleRetainedOceanOverlap == true)} possibleExcludedSeaOverlap={Flag(overlapsKnown && !excludedMappingMissing, root?.PossibleExcludedSeaOverlap == true)} ") +
                Invariant($"renderedLakeOverlap={Flag(overlapsKnown, root?.RenderedLakeOverlap == true)} currentlyPassesRenderedLake={catchment.ViaLake} firstBlock={b?.PreviousFailure ?? b?.AncestryFailure ?? "none"} firstBlockBasin={b?.PreviousBlockBasin ?? -1} ") +
                Invariant($"refinedBlock={failure ?? "none"} geometryBlockBasin={b?.GeometryBlockBasin ?? -1} route={RouteLabel(route, cycle)} routeRequiredHead={Number(routeHead)} ") +
                Invariant($"excludedSea={(root == null ? "unknown" : ExcludedClassification(root, classifiedGeometryUsed || !excludedMappingUsed && !excludedMappingMissing))} ") +
                Invariant($"floodedLandAreaEstimate={Number(earlyOcean ? double.NaN : RouteMeasure(b, true))} maximumDepthEstimate={Number(RouteMeasure(b, false))} routeCompoundBasins={b?.RouteBasinCount ?? 0} floodEstimateScope={(b?.RouteBasinCount > 0 ? "route" : "source_region")} ") +
                Invariant($"retainedOceanBeforeSaddle={Flag(earlyOcean || root?.Region.Measured == true, earlyOcean)} floodEscapesFootprint={Flag(floodEscape || root?.Region.Measured == true, floodEscape)} renderedLakeCompatibility={LakeCompatibility(route)} ") +
                Invariant($"initialBelowSeaFloor={Flag(b?.InitialBelowSeaFloor != null, b?.InitialBelowSeaFloor == true)} initialExcludedFloor={Flag(excludedMappingUsed && !excludedMappingMissing && b != null, b?.InitialExcludedFloor == true)} finalFloorRadius={Number(finalFloor)} finalFloorRelativeToSea={RelativeToSea(tin, finalFloor)} freshwaterOccupancy={occupancy} priorRefinedBlock={b?.PriorRefinedFailure ?? "none"} ") +
                Invariant($"areaScreenExceeded={Flag(Finite(RouteMeasure(b, true)), RouteMeasure(b, true) > largeFloodArea)} depthScreenExceeded={Flag(Finite(RouteMeasure(b, false)), RouteMeasure(b, false) > verticalLimit)} headScreenExceeded={Flag(Finite(routeHead), routeHead - tin.BasinSeaLevel > verticalLimit)} ") +
                Invariant($"canPlausiblyReachOcean={(failure == null ? "geometry_supported_storage_unassessed" : "unverified")}"));
        }
    }

    private static bool HasMatchingRenderedLake(GeodesicTinHydrology tin, GeodesicDrainageDelivery.Catchment catchment,
        BasinEvidence b, Dictionary<int, double> renderedLevels, Dictionary<int, int> lakeOwners)
    {
        var point = catchment.Terminal.Point;
        return point.HasValue && lakeOwners.TryGetValue(point.Value.Triangle, out int owner) &&
            owner == b.OceanRoot.Basin.Id && !b.OceanRoot.RenderedLakeConflict &&
            renderedLevels.TryGetValue(point.Value.Triangle, out double level) &&
            MatchesHeight(level, b.ControllingSpill, tin.Epsilon) &&
            tin.Height(point.Value) < level && tin.CompletedVisibleRadius(point.Value) < level;
    }

    private FreshwaterOccupancy Occupancy(GeodesicTinHydrology tin, BasinEvidence b, bool existing)
    {
        if (b == null) return FreshwaterOccupancy.Unverified;
        if (existing) return FreshwaterOccupancy.ExistingRenderedLake;
        var region = b.OceanRoot?.Region;
        if (region?.FreshwaterBlock != null) return FreshwaterOccupancy.InvalidOrAmbiguous;
        if (region?.Measured != true || !b.SourceSaddleWitnessed) return FreshwaterOccupancy.FinalDepression;
        if (region.Area > largeFloodArea || region.Depth > verticalLimit || b.ControllingSpill - tin.BasinSeaLevel > verticalLimit)
            return FreshwaterOccupancy.ExceedsFloodScreen;
        return b.OceanRoot.CompoundBasinCount > 1 ? FreshwaterOccupancy.PotentialSharedWater : FreshwaterOccupancy.PotentialFreshwater;
    }

    private static string RelativeToSea(GeodesicTinHydrology tin, double radius)
    {
        double offset = radius - tin.BasinSeaLevel;
        if (!Finite(offset)) return "unknown";
        return Math.Abs(offset) <= tin.Epsilon ? "at_sea_tolerance" : offset < 0 ? "below_sea" : "above_sea";
    }

    private static FreshwaterOutcome ClassifyFreshwater(BasinEvidence b, string failure, bool existing)
    {
        if (existing) return FreshwaterOutcome.ExistingRenderedLake;
        switch (failure)
        {
            case null: return FreshwaterOutcome.EligibleOverflow;
            case "UnboundedFloodFootprint": return FreshwaterOutcome.FootprintEscape;
            case "LargeFloodScaleReview": return FreshwaterOutcome.ExcessiveArea;
            case "ExcessiveFloodDepth": return FreshwaterOutcome.ExcessiveDepth;
            case "ExcessiveControllingHead": return FreshwaterOutcome.ExcessiveHead;
            case "ExposedOutletRisesOnFinalSurface":
            case "OutletReentersCompoundWater": return FreshwaterOutcome.InvalidExposedOutlet;
            case "IncompatibleRenderedLakeOwnership":
            case "OutletEntersExistingRenderedLake": return FreshwaterOutcome.LakeSurfaceConflict;
            case "InvalidReceivingHeadOrBackwater":
            case "MissingReceivingBasinEvidence":
            case "MissingReceivingWaterContact": return FreshwaterOutcome.ReceivingBasinBlocked;
            default: return b.GeometryBlockBasin >= 0 && b.GeometryBlockBasin != b.Basin.Id
                ? FreshwaterOutcome.ReceivingBasinBlocked : FreshwaterOutcome.Uncertain;
        }
    }

    private void RankFreshwaterCandidates(GeodesicTinHydrology tin, Dictionary<BasinEvidence, double> candidateRunoff)
    {
        int rank = 0;
        foreach (var pair in candidateRunoff.OrderByDescending(p => p.Value).ThenBy(p => p.Key.RouteArea)
            .ThenBy(p => p.Key.RouteDepth).ThenBy(p => p.Key.Basin.Id).Take(FreshwaterCandidateLimit))
        {
            var b = pair.Key;
            var route = ReceivingRoute(b, out bool cycle);
            freshwaterCandidates.Add(
                Invariant($"[GeodesicFreshwaterOverflowCandidate] rank={++rank} basin={b.Basin.Id} independentRunoff={pair.Value:R} runoffPercent={Percent(pair.Value):F6} ") +
                Invariant($"route={RouteLabel(route, cycle)} head={b.ControllingSpill:R} headAboveSea={Math.Max(0, b.ControllingSpill - tin.BasinSeaLevel):R} ") +
                Invariant($"areaEstimate={b.RouteArea:R} maxDepthEstimate={b.RouteDepth:R} occupancy={Occupancy(tin, b, false)} renderedLakeCompatibility={LakeCompatibility(route)} status=future_overflow_candidate"));
        }
    }

    private static List<BasinEvidence> ReceivingRoute(BasinEvidence basin, out bool cycle)
    {
        var legs = new List<BasinEvidence>();
        var seen = new HashSet<int>();
        for (; basin != null && seen.Add(basin.Basin.Id); basin = basin.Receiver)
            legs.Add(basin);
        cycle = basin != null;
        return legs;
    }

    private static string RouteLabel(List<BasinEvidence> route, bool cycle)
    {
        if (route.Count == 0) return "unknown";
        string legs = string.Join(">", route.Select(leg => leg.Basin.Id.ToString(CultureInfo.InvariantCulture)));
        return legs + (cycle ? ">cycle" : route[route.Count - 1].DirectOcean ? ">ocean" : "");
    }

    private static double RouteMeasure(BasinEvidence b, bool area)
    {
        if (b == null) return double.NaN;
        if (b.RouteBasinCount > 0) return area ? b.RouteArea : b.RouteDepth;
        var region = b.OceanRoot?.Region;
        return region?.Measured == true ? area ? region.Area : region.Depth : double.NaN;
    }

    private static string LakeCompatibility(List<BasinEvidence> route)
    {
        bool overlap = false;
        foreach (var b in route)
        {
            if (b.OceanRoot?.RenderedLakeConflict == true || b.LocalFailure == "OutletEntersExistingRenderedLake")
                return "conflict";
            overlap |= b.OceanRoot?.RenderedLakeOverlap == true;
        }
        return overlap ? "compatible" : "no_observed_overlap";
    }

    private static string FailureFor(GeodesicTinHydrology tin, GeodesicDrainageDelivery.Catchment catchment, BasinEvidence b) =>
        b == null ? "MissingTerminalBasin" : b.AncestryFailure ?? CatchmentGeometryFailure(tin, catchment, b) ?? b.FloodFailure;

    private static Outcome Classify(string failure, bool continuation)
    {
        switch (failure)
        {
            case null: return continuation ? Outcome.VerifiedBasinContinuation : Outcome.VerifiedOceanOutlet;
            case FreshwaterReview:
            case "ExposedOutletBelowSeaInExcludedCell": return Outcome.ExactExcludedSeaIntersection;
            case "ExcludedSeaBoundaryUncertain": return Outcome.ProxyOnlyExcludedSeaUncertainty;
            case "ReceivingOceanAboveSpill":
            case "ParentSpillBelowChild":
            case "InvalidReceivingHeadOrBackwater": return Outcome.InvalidHeadOrBackwater;
            case "MissingTerminalBasin":
            case "MissingParent":
            case "MissingReceivingBasinEvidence":
            case "MissingReceivingWaterContact":
            case "MissingDownstreamRoutingEvidence":
            case "MissingOrInvalidDownstreamWitness":
            case "OutletRoutingFailureOrTraceLimit":
            case "OutletEndsAtFlatRequiringRouting": return Outcome.MissingDownstreamEvidence;
            default: return Outcome.OtherBlocker;
        }
    }

    private static bool InvalidGeometry(string failure)
    {
        switch (failure)
        {
            case "RetainedOceanBeforeSaddle":
            case "InvalidReceivingHeadOrBackwater":
            case "ReceivingOceanAboveSpill":
            case "SpillDoesNotMatchFinalSurface":
            case "SpillOutsideSourceWater":
            case "IncompatibleDownstreamWitness":
            case "WitnessOutsideReceivingWater":
            case "OutletReentersCompoundWater":
            case "OutletCycle":
            case "ExposedOutletRisesOnFinalSurface": return true;
            default: return false;
        }
    }

    private BasinEvidence EvidenceFor(GeodesicDrainageDelivery.Catchment catchment) =>
        catchment.Terminal.Basin != null && basins.TryGetValue(catchment.Terminal.Basin.Id, out var b) ? b : null;

    private static string CatchmentGeometryFailure(GeodesicTinHydrology tin, GeodesicDrainageDelivery.Catchment catchment, BasinEvidence b) =>
        b.GeometryFailure ?? (TerminalInSourceWater(tin, catchment, b) ? null : "TerminalOutsideCompoundWater");

    private static bool TerminalInSourceWater(GeodesicTinHydrology tin, GeodesicDrainageDelivery.Catchment catchment, BasinEvidence b)
    {
        var point = catchment.Terminal.Point;
        if (!point.HasValue || !ValidFacet(tin, point.Value.Triangle) ||
            !b.OceanRoot.Region.Facets.Contains(point.Value.Triangle) ||
            !ContainsSorted(b.Basin.MemberTriangles, point.Value.Triangle))
            return false;
        double height = tin.Height(point.Value), radius = tin.CompletedVisibleRadius(point.Value);
        return Finite(height) && Finite(radius) && height < b.Basin.SpillElevation && radius <= b.ControllingSpill + tin.Epsilon;
    }

    private static string Flag(bool known, bool value) => known ? value.ToString() : "unknown";
    private double Percent(double amount) => total > 0 ? 100 * amount / total : 0;

    internal string Summary()
    {
        double closedResidual = closed - GeometricClosed - UnverifiedGeometryClosed - MissingAncestryClosed;
        double tolerance = Math.Max(1e-9, total * 1e-9);
        double outcomeResidual = total - runoffByOutcome.Sum();
        double potentialResidual = StructuralClosed - GeometricClosed - floodingClosed - invalidGeometryClosed - uncertainClosed;
        double freshwaterResidual = freshwaterReviewRunoff - freshwaterRunoff.Sum();
        bool accountingPass = Math.Abs(closedResidual) <= tolerance && Math.Abs(total - confirmed - closed - unresolved) <= tolerance &&
            Math.Abs(outcomeResidual) <= tolerance && Math.Abs(potentialResidual) <= tolerance && Math.Abs(freshwaterResidual) <= tolerance;
        string excludedEvidence = excludedMappingMissing ? "unavailable" : classifiedGeometryUsed ? "final_facet_and_simulation_voronoi" : excludedMappingUsed ? "conservative_vertex_mapping" : "no_excluded_cells";
        string reasonCounts = string.Join(",", reasons.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value));
        string outcomeTotals = string.Join(",", Enum.GetValues(typeof(Outcome)).Cast<Outcome>().Select(o => Invariant($"{o}:{Runoff(o):R}")));
        string blockerRunoff = string.Join(",", runoffReasons.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Invariant($"{p.Key}:{p.Value:R}")));
        string excludedBreakdown = string.Join(",", excludedCohort.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Invariant($"{p.Key}:{p.Value}")));
        string freshwaterTotals = string.Join(",", Enum.GetValues(typeof(FreshwaterOutcome)).Cast<FreshwaterOutcome>()
            .Select(o => Invariant($"{o}:{FreshwaterRunoff(o):R}")));
        return "[GeodesicBasinOverflowAudit] scope=final counterfactual=filled_lossless_fixed_input aggregation=independent_terminal_parcels storageVolume=unavailable floodArea=linear_TIN_clip_approximation largeFloodScreen=1_percent_of_spherical_TIN_area " +
            Invariant($"freshwaterVerticalScreen=1_percent_of_nominal_radius areaLimit={Number(largeFloodArea)} depthLimit={Number(verticalLimit)} headAboveSeaLimit={Number(verticalLimit)} ") +
            Invariant($"descriptors={descriptorCount} duplicateIds={duplicateIds} parentLinks={parentLinks} validParentLinks={validLinks} rejectedParentLinks={parentLinks - validLinks} invalidHierarchyLinks={invalidLinks} missingReferences={missingReferences} ") +
            Invariant($"cycles={cycles} cycleNodes={cycleNodes} oceanRoots={oceanRoots} boundaryRoots={boundaryRoots} ambiguousRoots={ambiguousRoots} terminalRoots={terminalRoots} ") +
            Invariant($"structuralOceanBasins={structuralCount} geometricallySupportedBasins={geometryCount} unverifiedBasins={basins.Count - geometryCount} totalRunoff={total:R} ") +
            Invariant($"confirmedOcean={confirmed:R} confirmedOceanPercent={Percent(confirmed):F6} existingUnresolved={unresolved:R} existingUnresolvedPercent={Percent(unresolved):F6} ") +
            Invariant($"closedRunoff={closed:R} structuralClosed={StructuralClosed:R} structuralClosedPercent={Percent(StructuralClosed):F6} supportedClosed={GeometricClosed:R} supportedClosedPercent={Percent(GeometricClosed):F6} ") +
            Invariant($"closedWithoutValidOceanAncestry={MissingAncestryClosed:R} closedWithoutValidOceanAncestryPercent={Percent(MissingAncestryClosed):F6} ") +
            Invariant($"closedWithUnverifiedGeometry={UnverifiedGeometryClosed:R} closedWithUnverifiedGeometryPercent={Percent(UnverifiedGeometryClosed):F6} ") +
            Invariant($"structuralPotentialOcean={confirmed + StructuralClosed:R} structuralPotentialPercent={Percent(confirmed + StructuralClosed):F6} witnessedPotentialOcean={confirmed + GeometricClosed:R} witnessedPotentialPercent={Percent(confirmed + GeometricClosed):F6} ") +
            Invariant($"floodingPotential={floodingClosed:R} floodingPotentialPercent={Percent(floodingClosed):F6} invalidGeometryPotential={invalidGeometryClosed:R} invalidGeometryPotentialPercent={Percent(invalidGeometryClosed):F6} uncertainPotential={uncertainClosed:R} uncertainPotentialPercent={Percent(uncertainClosed):F6} ") +
            Invariant($"previousClosedOutletDescriptors={previousClosedOutlets} resolvedClosedOutletDescriptors={resolvedClosedOutlets} previousExcludedProxyDescriptors={previousExcluded} resolvedExcludedProxyDescriptors={resolvedExcluded} excludedProxyBreakdown=[{excludedBreakdown}] ") +
            Invariant($"runoffByOutcome=[{outcomeTotals}] blockerRunoff=[{blockerRunoff}] outcomeResidual={outcomeResidual:R} structuralPotentialResidual={potentialResidual:R} ") +
            Invariant($"freshwaterPriorReviewRunoff={freshwaterReviewRunoff:R} freshwaterBoundedSourceSubset={freshwaterBoundedSourceRunoff:R} freshwaterCohortRunoffByOutcome=[{freshwaterTotals}] freshwaterCohortResidual={freshwaterResidual:R} freshwaterCandidates={freshwaterCandidates.Count} ") +
            Invariant($"tinyClosed={tinyClosed:R} tinyClosedPercent={Percent(tinyClosed):F6} tinyStructural={tinyStructural:R} tinyStructuralPercent={Percent(tinyStructural):F6} tinySupported={tinyGeometric:R} tinySupportedPercent={Percent(tinyGeometric):F6} ") +
            Invariant($"closedResidual={closedResidual:R} accountingPass={accountingPass} analysisMs={milliseconds:F3} approximateScratchBytes={estimatedBytes} excludedSeaEvidence={excludedEvidence} basinBlockReasons=[{reasonCounts}]");
    }

    /// <summary>Intersect completed terrain with the frozen nearest-cell classification.
    /// Cell boundaries are great-circle bisectors and therefore linear on the direction facet.</summary>
    internal sealed class ClassifiedCells
    {
        private const double CellBoundaryTolerance = 2e-7;
        private readonly IGeodesicHydrologyTopology topology;
        private readonly Func<int, bool> isExcluded;
        private readonly Func<int, int> seedCell;
        private readonly HashSet<int> visited = new HashSet<int>();
        private readonly Queue<int> pending = new Queue<int>();
        private readonly List<TinPoint> first = new List<TinPoint>(12);
        private readonly List<TinPoint> second = new List<TinPoint>(12);

        internal ClassifiedCells(IGeodesicHydrologyTopology topology, Func<int, bool> isExcluded, Func<int, int> seedCell)
        {
            this.topology = topology;
            this.isExcluded = isExcluded;
            this.seedCell = seedCell;
        }

        internal Overlap InFacet(GeodesicTinHydrology tin, int facet, TinPoint[] wet)
        {
            var full = new[] { new TinPoint(facet, 1, 0, 0), new TinPoint(facet, 0, 1, 0), new TinPoint(facet, 0, 0, 1) };
            return Intersect(tin, full, wet);
        }

        internal Overlap OnSegment(GeodesicTinHydrology tin, TinPoint a, TinPoint b)
        {
            var segment = new[] { a, b };
            return Intersect(tin, segment, segment);
        }

        private Overlap Intersect(GeodesicTinHydrology tin, TinPoint[] domain, TinPoint[] wet)
        {
            double sea = tin.BasinSeaLevel;
            if (tin.MinimumCompletedRadius(domain) > sea + tin.Epsilon) return Overlap.None;
            int vertex = tin.BasinVertex(domain[0].Triangle, 0);
            int start = GeodesicOceanConnectivity.FindNearestCell(topology, tin.Direction(domain[0]), seedCell(vertex));
            if (start < 0) return Overlap.BoundaryUncertain;
            visited.Clear();
            pending.Clear();
            pending.Enqueue(start);
            visited.Add(start);
            var result = Overlap.None;
            while (pending.Count > 0)
            {
                int cell = pending.Dequeue();
                var outer = Clip(tin, domain, cell, -CellBoundaryTolerance);
                if (outer.Count == 0)
                {
                    if (cell == start) return Overlap.BoundaryUncertain;
                    continue;
                }
                if (isExcluded(cell) && tin.MinimumCompletedRadius(outer) <= sea + tin.Epsilon)
                {
                    var inner = Clip(tin, wet, cell, CellBoundaryTolerance);
                    if (tin.MinimumCompletedRadius(inner) < sea - tin.Epsilon) return Overlap.Exact;
                    result = Overlap.BoundaryUncertain;
                }
                // A convex patch can only enter another cell across a shared boundary.
                for (int i = 0; i < topology.NeighborCounts[cell]; i++)
                {
                    int neighbor = topology.Neighbors6[cell * 6 + i];
                    if (visited.Add(neighbor)) pending.Enqueue(neighbor);
                }
            }
            return result;
        }

        private List<TinPoint> Clip(GeodesicTinHydrology tin, TinPoint[] domain, int cell, double margin)
        {
            var input = first;
            var output = second;
            input.Clear();
            input.AddRange(domain);
            for (int i = 0; i < topology.NeighborCounts[cell] && input.Count > 0; i++)
            {
                output.Clear();
                int neighbor = topology.Neighbors6[cell * 6 + i];
                var center = topology.CellDirections[cell];
                var other = topology.CellDirections[neighbor];
                var a = input[input.Count - 1];
                double ha = tin.DirectionSide(a, center, other) - margin;
                foreach (var b in input)
                {
                    double hb = tin.DirectionSide(b, center, other) - margin;
                    if ((ha < 0) != (hb < 0))
                    {
                        double fraction = ha / (ha - hb);
                        output.Add(Interpolate(a, b, fraction));
                    }
                    if (hb >= 0) output.Add(b);
                    a = b;
                    ha = hb;
                }
                var swap = input;
                input = output;
                output = swap;
            }
            return input;
        }
    }

}
