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

    internal sealed class BasinEvidence
    {
        public readonly TinBasinDescriptor Basin;
        public BasinEvidence OceanRoot;
        public int SpillSteps, CompoundBasinCount, GeometryBlockBasin = -1;
        public string AncestryFailure, GeometryFailure;
        public bool Duplicate, WetRegionChecked, PossibleRetainedOceanOverlap, PossibleExcludedSeaOverlap, RenderedLakeOverlap;
        public double ControllingSpill => OceanRoot?.Basin.SpillElevation ?? double.NaN;
        internal byte Visit;

        public BasinEvidence(TinBasinDescriptor basin) { Basin = basin; }
        public bool StructuralOcean => OceanRoot != null;
        public bool GeometricallySupported => StructuralOcean && GeometryFailure == null;
    }

    private readonly Dictionary<int, BasinEvidence> basins = new Dictionary<int, BasinEvidence>();
    private readonly Dictionary<string, int> reasons = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly List<string> opportunities = new List<string>();
    private int descriptorCount, duplicateIds, parentLinks, validLinks, invalidLinks, missingReferences;
    private int cycles, cycleNodes, oceanRoots, boundaryRoots, ambiguousRoots, terminalRoots;
    private int structuralCount, geometryCount;
    private bool excludedMappingMissing, excludedMappingUsed;
    private double total, confirmed, closed, unresolved;
    internal double StructuralClosed, GeometricClosed, MissingAncestryClosed, UnverifiedGeometryClosed;
    private double tinyClosed, tinyStructural, tinyGeometric, milliseconds;
    private long peakRegionBytes, estimatedBytes;

    internal IReadOnlyDictionary<int, BasinEvidence> Basins => basins;
    internal IEnumerable<string> Opportunities => opportunities;

    internal static GeodesicBasinOverflowAudit Analyze(GeodesicTinHydrology tin,
        IReadOnlyList<TinBasinDescriptor> descriptors, GeodesicDrainageDelivery delivery,
        GeodesicTinLakeGeometry renderedLakes = null, Func<int, bool> excludedAtVertex = null,
        bool excludedMappingMissing = false)
    {
        var clock = Stopwatch.StartNew();
        var audit = new GeodesicBasinOverflowAudit
        {
            descriptorCount = descriptors.Count,
            excludedMappingMissing = excludedMappingMissing,
            excludedMappingUsed = excludedAtVertex != null
        };
        foreach (var basin in descriptors)
        {
            if (audit.basins.TryGetValue(basin.Id, out var duplicate))
            {
                duplicate.Duplicate = true;
                audit.duplicateIds++;
            }
            else audit.basins.Add(basin.Id, new BasinEvidence(basin));
        }

        var downstreamFirst = audit.ResolveAncestry(tin);
        var renderedLevels = RenderedFacetLevels(renderedLakes);
        foreach (var group in downstreamFirst.Where(b => b.StructuralOcean).GroupBy(b => b.OceanRoot))
            audit.CheckWetRegion(tin, group.Key, group, renderedLevels, excludedAtVertex);

        foreach (var b in audit.basins.Values)
        {
            if (b.StructuralOcean) audit.structuralCount++;
            if (b.GeometricallySupported) audit.geometryCount++;
            var reason = b.AncestryFailure ?? b.GeometryFailure;
            if (reason != null) audit.reasons[reason] = audit.reasons.TryGetValue(reason, out int n) ? n + 1 : 1;
        }
        audit.AccountRunoff(tin, delivery);
        // Estimates include object/map slack and the largest sequential wet-region workspace.
        audit.estimatedBytes = 320L * audit.basins.Count + 48L * renderedLevels.Count + audit.peakRegionBytes
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

    private void CheckWetRegion(GeodesicTinHydrology tin, BasinEvidence root, IEnumerable<BasinEvidence> group,
        Dictionary<int, double> renderedLevels, Func<int, bool> excludedAtVertex)
    {
        // Strictly submerged vertices give a conservative connected-water certificate.
        // A point-only, level saddle connection without a submerged path stays unverified.
        double head = root.Basin.SpillElevation;
        root.CompoundBasinCount = group.Count();
        var facets = new HashSet<int>();
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
        string groupFailure = invalidFacet ? "InvalidWetFootprintFacet" : incompatibleHeight ? "VisibleAndHydrologyHeightsDiffer" :
            exposedWetCorner ? "WetCornerAtOrAboveHead" : !Finite(tin.BasinSeaLevel) ? "InvalidSeaLevel" :
            head < tin.BasinSeaLevel ? "ReceivingOceanAboveSpill" : root.PossibleRetainedOceanOverlap ? "PossibleRetainedOceanOverlap" :
            excludedMappingMissing ? "ExcludedSeaMappingUnavailable" : root.PossibleExcludedSeaOverlap ? "PossibleExcludedSeaOverlap" : null;

        if (groupFailure == null)
        {
            var pending = new Queue<int>();
            if (wet.Remove(root.Basin.Id)) pending.Enqueue(root.Basin.Id);
            while (pending.Count > 0)
            {
                int v = pending.Dequeue();
                for (int i = tin.BasinIncidentStart(v); i < tin.BasinIncidentEnd(v); i++)
                {
                    int facet = tin.BasinIncidentTriangle(i);
                    if (!facets.Contains(facet)) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int next = tin.BasinVertex(facet, k);
                        if (wet.Remove(next)) pending.Enqueue(next);
                    }
                }
            }
            if (wetCount == 0 || wet.Count != 0) groupFailure = "WetRegionDisconnectedOrLevelOnly";
        }

        int traceSamples = 0;
        foreach (var b in group)
        {
            var parent = ParentOf(b.Basin);
            if (parent?.GeometryFailure != null)
            {
                Block(b, parent.GeometryFailure, parent.GeometryBlockBasin);
                continue;
            }
            if (groupFailure != null) { Block(b, groupFailure, root.Basin.Id); continue; }
            var basin = b.Basin;
            if (basin.SubseaUnclassified) { Block(b, "SubseaFloorUnclassified", basin.Id); continue; }
            var reason = CheckSpill(tin, basin, root.Basin, facets, parent, renderedLevels, excludedAtVertex, ref traceSamples);
            if (reason != null) Block(b, reason, basin.Id);
        }
        peakRegionBytes = Math.Max(peakRegionBytes, 32L * facets.Count + 36L * wetCount +
            (traceSamples > 0 ? 96L * traceSamples + 4096L : 0L));
    }

    private static void Block(BasinEvidence evidence, string reason, int basinId)
    {
        evidence.GeometryFailure = reason;
        evidence.GeometryBlockBasin = basinId;
    }

    private static string CheckSpill(GeodesicTinHydrology tin, TinBasinDescriptor basin, TinBasinDescriptor root,
        HashSet<int> facets, BasinEvidence parent, Dictionary<int, double> renderedLevels,
        Func<int, bool> excludedAtVertex, ref int traceSamples)
    {
        var spill = basin.Spill.Value;
        int saddle = CornerVertex(tin, spill);
        int facet = basin.DownstreamTriangle, vertex = basin.DownstreamVertex;
        double head = root.SpillElevation;
        if (saddle < 0 || !MatchesHeight(tin.Height(spill), basin.SpillElevation, tin.Epsilon) ||
            !MatchesHeight(tin.CompletedVisibleRadius(spill), basin.SpillElevation, tin.Epsilon))
            return "SpillDoesNotMatchFinalSurface";
        if (!TouchesWetRegion(tin, basin.Id, facets, head) || !TouchesWetRegion(tin, saddle, facets, head))
            return "FloorOrSpillOutsideCompoundWater";
        // A recorded external merge can be exactly at the retained shoreline.
        if (parent == null && tin.IsRetainedOceanBoundary(spill)) return null;
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
            // Every terminal assigned to this descriptor lies on its below-spill footprint.
            // Checking the whole footprint covers the delivery aggregate, not just a sample.
            if (!basin.MemberTriangles.All(facets.Contains)) return "ChildFootprintOutsideCompoundWater";
            if (!facets.Contains(facet) || !ContainsSorted(parent.Basin.MemberTriangles, facet) || sideHeight >= head || sideRadius >= head)
                return "WitnessOutsideReceivingWater";
            return null;
        }

        // Prove the dry part once per compound root. TraceLakeOutlet uses observe:false.
        bool InWater(TinPoint point) => facets.Contains(point.Triangle) && tin.Height(point) < head;
        var path = tin.TraceLakeOutlet(spill, (a, b) => a.Triangle == facet && !InWater(b),
            out var termination, MaximumOutletSteps);
        traceSamples = Math.Max(traceSamples, path.Length);
        if (termination != TinTermination.OceanCoast)
            return termination == TinTermination.RoutingFailure ? "OutletRoutingFailureOrTraceLimit" : "OutletEndsAt" + termination;
        if (path.Length < 2 || !tin.IsRetainedOceanBoundary(path[path.Length - 1])) return "AmbiguousRetainedOceanEndpoint";
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

    private static bool TouchesWetRegion(GeodesicTinHydrology tin, int vertex, HashSet<int> facets, double level)
    {
        for (int i = tin.BasinIncidentStart(vertex); i < tin.BasinIncidentEnd(vertex); i++)
        {
            int facet = tin.BasinIncidentTriangle(i);
            if (!facets.Contains(facet)) continue;
            for (int k = 0; k < 3; k++)
                if (tin.HydrologyHeight[tin.BasinVertex(facet, k)] < level) return true;
        }
        return false;
    }

    private static int CornerVertex(GeodesicTinHydrology tin, TinPoint point)
    {
        if (!ValidFacet(tin, point.Triangle)) return -1;
        if (point.A == 1 && point.B == 0 && point.C == 0) return tin.BasinVertex(point.Triangle, 0);
        if (point.A == 0 && point.B == 1 && point.C == 0) return tin.BasinVertex(point.Triangle, 1);
        if (point.A == 0 && point.B == 0 && point.C == 1) return tin.BasinVertex(point.Triangle, 2);
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

    private void AccountRunoff(GeodesicTinHydrology tin, GeodesicDrainageDelivery delivery)
    {
        total = delivery.TotalRunoff;
        confirmed = delivery.DirectOcean + delivery.ViaLakesOcean;
        closed = delivery.InlandClosed;
        unresolved = delivery.Unresolved;
        foreach (var catchment in delivery.DisconnectedCatchments)
        {
            if (catchment.Terminal.Destination != GeodesicDrainageDelivery.Destination.InlandClosed) continue;
            var b = EvidenceFor(catchment);
            bool tiny = catchment.Terminal.Type == nameof(TinTermination.TinyNoiseDepression);
            if (tiny) tinyClosed += catchment.Runoff;
            if (b?.StructuralOcean != true) { MissingAncestryClosed += catchment.Runoff; continue; }
            StructuralClosed += catchment.Runoff;
            if (tiny) tinyStructural += catchment.Runoff;
            if (CatchmentGeometryFailure(tin, catchment, b) != null) { UnverifiedGeometryClosed += catchment.Runoff; continue; }
            GeometricClosed += catchment.Runoff;
            if (tiny) tinyGeometric += catchment.Runoff;
        }
        int rank = 0;
        foreach (var catchment in delivery.LargestDisconnected)
        {
            var b = EvidenceFor(catchment);
            var root = b?.OceanRoot;
            var basin = catchment.Terminal.Basin;
            bool isClosed = catchment.Terminal.Destination == GeodesicDrainageDelivery.Destination.InlandClosed;
            string failure = isClosed ? b == null ? "MissingTerminalBasin" : b.AncestryFailure ?? CatchmentGeometryFailure(tin, catchment, b)
                : "ExistingUnresolved:" + catchment.Terminal.Type;
            double head = b?.ControllingSpill ?? double.NaN;
            bool overlapsKnown = root?.WetRegionChecked == true;
            bool structural = isClosed && b?.StructuralOcean == true;
            string steps = structural ? b.SpillSteps.ToString(CultureInfo.InvariantCulture) : "unknown";
            opportunities.Add(
                Invariant($"[GeodesicBasinOverflowOpportunity] rank={++rank} terminalId={catchment.Terminal.TerminalId} terminalType={catchment.Terminal.Type} currentDestination={catchment.Terminal.Destination} ") +
                Invariant($"runoff={catchment.Runoff:R} runoffPercent={Percent(catchment.Runoff):F6} structuralOcean={structural} geometry={(failure == null ? "SupportedCounterfactual" : "Unverified")} ") +
                Invariant($"spillSteps={steps} floor={Number(basin?.FloorElevation ?? double.NaN)} firstSpill={Number(basin?.SpillElevation ?? double.NaN)} controllingSpill={Number(head)} minimumHeadIncludingOcean={Number(Math.Max(head, tin.BasinSeaLevel))} ") +
                Invariant($"oceanRootBasin={root?.Basin.Id ?? -1} compoundBasins={root?.CompoundBasinCount ?? 0} higherSharedLevel={Flag(structural, basin != null && head > basin.SpillElevation)} wetRegionCheckedAtSpill={overlapsKnown} ") +
                Invariant($"possibleRetainedOceanOverlap={Flag(overlapsKnown, root?.PossibleRetainedOceanOverlap == true)} possibleExcludedSeaOverlap={Flag(overlapsKnown && !excludedMappingMissing, root?.PossibleExcludedSeaOverlap == true)} ") +
                Invariant($"renderedLakeOverlap={Flag(overlapsKnown, root?.RenderedLakeOverlap == true)} currentlyPassesRenderedLake={catchment.ViaLake} firstBlock={failure ?? "none"} geometryBlockBasin={b?.GeometryBlockBasin ?? -1}"));
        }
    }

    private BasinEvidence EvidenceFor(GeodesicDrainageDelivery.Catchment catchment) =>
        catchment.Terminal.Basin != null && basins.TryGetValue(catchment.Terminal.Basin.Id, out var b) ? b : null;

    private static string CatchmentGeometryFailure(GeodesicTinHydrology tin, GeodesicDrainageDelivery.Catchment catchment, BasinEvidence b)
    {
        if (b.GeometryFailure != null) return b.GeometryFailure;
        var point = catchment.Terminal.Point;
        if (!point.HasValue || !ValidFacet(tin, point.Value.Triangle) ||
            !ContainsSorted(b.OceanRoot.Basin.MemberTriangles, point.Value.Triangle))
            return "TerminalOutsideCompoundWater";
        double height = tin.Height(point.Value), radius = tin.CompletedVisibleRadius(point.Value);
        return Finite(height) && Finite(radius) && height < b.ControllingSpill && radius <= b.ControllingSpill + tin.Epsilon
            ? null : "TerminalOutsideCompoundWater";
    }

    private static string Flag(bool known, bool value) => known ? value.ToString() : "unknown";
    private double Percent(double amount) => total > 0 ? 100 * amount / total : 0;

    internal string Summary()
    {
        double closedResidual = closed - GeometricClosed - UnverifiedGeometryClosed - MissingAncestryClosed;
        double tolerance = Math.Max(1e-9, total * 1e-9);
        bool accountingPass = Math.Abs(closedResidual) <= tolerance && Math.Abs(total - confirmed - closed - unresolved) <= tolerance;
        string excludedEvidence = excludedMappingMissing ? "unavailable" : excludedMappingUsed ? "conservative_vertex_mapping" : "no_excluded_cells";
        string reasonCounts = string.Join(",", reasons.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value));
        return "[GeodesicBasinOverflowAudit] scope=final counterfactual=filled_lossless_fixed_input aggregation=independent_compound_regions storageVolume=unavailable " +
            Invariant($"descriptors={descriptorCount} duplicateIds={duplicateIds} parentLinks={parentLinks} validParentLinks={validLinks} rejectedParentLinks={parentLinks - validLinks} invalidHierarchyLinks={invalidLinks} missingReferences={missingReferences} ") +
            Invariant($"cycles={cycles} cycleNodes={cycleNodes} oceanRoots={oceanRoots} boundaryRoots={boundaryRoots} ambiguousRoots={ambiguousRoots} terminalRoots={terminalRoots} ") +
            Invariant($"structuralOceanBasins={structuralCount} geometricallySupportedBasins={geometryCount} unverifiedBasins={basins.Count - geometryCount} totalRunoff={total:R} ") +
            Invariant($"confirmedOcean={confirmed:R} confirmedOceanPercent={Percent(confirmed):F6} existingUnresolved={unresolved:R} existingUnresolvedPercent={Percent(unresolved):F6} ") +
            Invariant($"closedRunoff={closed:R} structuralClosed={StructuralClosed:R} structuralClosedPercent={Percent(StructuralClosed):F6} supportedClosed={GeometricClosed:R} supportedClosedPercent={Percent(GeometricClosed):F6} ") +
            Invariant($"closedWithoutValidOceanAncestry={MissingAncestryClosed:R} closedWithoutValidOceanAncestryPercent={Percent(MissingAncestryClosed):F6} ") +
            Invariant($"closedWithUnverifiedGeometry={UnverifiedGeometryClosed:R} closedWithUnverifiedGeometryPercent={Percent(UnverifiedGeometryClosed):F6} ") +
            Invariant($"structuralPotentialOcean={confirmed + StructuralClosed:R} structuralPotentialPercent={Percent(confirmed + StructuralClosed):F6} witnessedPotentialOcean={confirmed + GeometricClosed:R} witnessedPotentialPercent={Percent(confirmed + GeometricClosed):F6} ") +
            Invariant($"tinyClosed={tinyClosed:R} tinyClosedPercent={Percent(tinyClosed):F6} tinyStructural={tinyStructural:R} tinyStructuralPercent={Percent(tinyStructural):F6} tinySupported={tinyGeometric:R} tinySupportedPercent={Percent(tinyGeometric):F6} ") +
            Invariant($"closedResidual={closedResidual:R} accountingPass={accountingPass} analysisMs={milliseconds:F3} approximateScratchBytes={estimatedBytes} excludedSeaEvidence={excludedEvidence} basinBlockReasons=[{reasonCounts}]");
    }
}
