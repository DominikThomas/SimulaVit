using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

public readonly struct TinParcelDestination
{
    public readonly TinPoint Point;
    public readonly TinTermination Termination;
    public TinParcelDestination(TinPoint point, TinTermination termination) { Point = point; Termination = termination; }
}

/// <summary>Observation only. Parcels are the original facet inputs, never accumulated reach
/// discharge. Lake membership comes from the router's existing first-entry parcel sets.
/// Graph traversal follows the published lake, river and sediment corridor edges.</summary>
public sealed class GeodesicDrainageDelivery
{
    public enum Destination { DirectOcean, OceanViaLakes, InlandClosed, Unresolved }
    internal enum RiverDestination { Ocean, ClosedBasin, TerminalLake, Unresolved }
    public sealed class Outcome
    {
        public Destination Destination;
        public string TerminalId, Type;
        public TinPoint? Point;
        public TinBasinDescriptor Basin;
        public bool ViaLake, ViaCorridor;
        internal int OceanComponent = -1;
        internal int TerminalLakeId = -1;
        internal RiverDestination RiverColour => Destination == Destination.DirectOcean || Destination == Destination.OceanViaLakes
            ? RiverDestination.Ocean
            : Destination == Destination.InlandClosed
                ? TerminalLakeId >= 0 ? RiverDestination.TerminalLake : RiverDestination.ClosedBasin
                : RiverDestination.Unresolved;
        internal Outcome Through(bool lake, bool corridor, int terminalLakeId = -1) => new Outcome {
            Destination = lake && Destination == Destination.DirectOcean ? Destination.OceanViaLakes : Destination,
            TerminalId = TerminalId, Type = Type, Point = Point, Basin = Basin, ViaLake = ViaLake || lake,
            ViaCorridor = ViaCorridor || corridor, OceanComponent = OceanComponent,
            TerminalLakeId = terminalLakeId >= 0 ? terminalLakeId : TerminalLakeId };
    }
    public sealed class Catchment
    {
        public Outcome Terminal;
        public double Runoff, Area;
        public int Parcels, SourcePaths;
        public bool ViaLake;
    }
    internal sealed class Link { public string Next; public Outcome End; public TinPoint? Contact; public bool Lake, Corridor; }
    private Dictionary<string, Link> links = new Dictionary<string, Link>(StringComparer.Ordinal);
    private Dictionary<string, Outcome> resolved = new Dictionary<string, Outcome>(StringComparer.Ordinal);
    private readonly Dictionary<string, Catchment> catchments = new Dictionary<string, Catchment>(StringComparer.Ordinal);
    private readonly Dictionary<string, double> terminalRunoff = new Dictionary<string, double>(StringComparer.Ordinal);
    private Dictionary<(int, double, double, double, TinTermination), Outcome> terminalCache =
        new Dictionary<(int, double, double, double, TinTermination), Outcome>();
    private readonly GeodesicTinHydrology tin;
    private readonly GeodesicTinBasins basins;
    private Func<TinPoint, int> oceanComponentAt;
    private IReadOnlyDictionary<int, GeodesicTinSedimentCorridors.Connection> corridors;
    public double PrimaryOcean { get; private set; }
    public double SecondaryOcean { get; private set; }
    public double UnattributedOcean { get; private set; }
    public double CorridorOcean { get; private set; }
    private readonly Outcome[] networkOutcomes;
    private readonly double[] runoffByRiverDestination = new double[4];
    private bool lakeTraversalUnavailable;
    internal Outcome RiverOutcome(string route)
    {
        if (lakeTraversalUnavailable) return Unknown(route, "RenderedLakeTraversalUnavailable");
        return resolved.TryGetValue(route, out var end) ? end : Unknown(route, "MissingDirectedConnection");
    }
    internal double RiverRunoff(RiverDestination destination) => runoffByRiverDestination[(int)destination];
    internal bool RiverDestinationAccountingPass =>
        Math.Abs(RiverRunoff(RiverDestination.Ocean) - DirectOcean - ViaLakesOcean) <= Tolerance &&
        Math.Abs(RiverRunoff(RiverDestination.ClosedBasin) + RiverRunoff(RiverDestination.TerminalLake) - InlandClosed) <= Tolerance &&
        Math.Abs(RiverRunoff(RiverDestination.Unresolved) - Unresolved) <= Tolerance && Math.Abs(Residual) <= Tolerance;

    internal string RiverDestinationSummary()
    {
        var counts = new int[4];
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < networkOutcomes.Length; i++)
        {
            var end = lakeTraversalUnavailable ? RiverOutcome("network:" + i) : networkOutcomes[i];
            counts[(int)end.RiverColour]++;
            if (end.RiverColour != RiverDestination.Unresolved) continue;
            reasons.TryGetValue(end.Type, out int count);
            reasons[end.Type] = count + 1;
        }
        string unresolvedReasons = string.Join(",", reasons.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{p.Value}"));
        return FormattableString.Invariant($"[GeodesicRiverDestinations] networks={networkOutcomes.Length} blue={counts[0]} red={counts[1]} purple={counts[2]} orange={counts[3]} blueRunoff={runoffByRiverDestination[0]:R} redRunoff={runoffByRiverDestination[1]:R} purpleRunoff={runoffByRiverDestination[2]:R} orangeRunoff={runoffByRiverDestination[3]:R} deliveryOcean={DirectOcean + ViaLakesOcean:R} deliveryClosed={InlandClosed:R} deliveryUnresolved={Unresolved:R} accountingPass={RiverDestinationAccountingPass} unresolvedNetworkReasons=[{unresolvedReasons}]");
    }
    internal Outcome NetworkOutcome(int network) => networkOutcomes[network];
    internal IReadOnlyDictionary<string, Link> OutletLinks => links;
    private readonly byte[] confirmedOceanParcels;
    public bool IsConfirmedOceanParcel(int id) => id >= 0 && id < confirmedOceanParcels.Length && confirmedOceanParcels[id] != 0;
    public bool IsOceanViaLakesParcel(int id) => id >= 0 && id < confirmedOceanParcels.Length && confirmedOceanParcels[id] == 2;
    public double TotalRunoff, DirectOcean, ViaLakesOcean, InlandClosed, Unresolved, TotalArea;
    public int IndependentParcels, DuplicateMemberships, InvalidMemberships, CycleNodes;
    public int LogicalDisconnected, VisualOnlyBreaks, UnknownBreaks, ExtractionAmbiguities;
    public int LakeBoundaryGaps, ShortUnrenderedLinks, ThresholdClipped, CentrelineRejected, ProjectionRejected, MissingTrunkRibbons;
    public int InitialUnexpectedBrokenNetworks;
    public double AnalysisMilliseconds { get; private set; }
    public long ApproximateMemoryBytes { get; private set; }
    public double Residual => TotalRunoff - DirectOcean - ViaLakesOcean - InlandClosed - Unresolved;
    public double Tolerance => Math.Max(1e-9, TotalRunoff * 1e-9);
    internal int ClosedBasinCount => catchments.Values.Where(c => c.Terminal.Destination == Destination.InlandClosed && c.Terminal.Basin != null)
        .Select(c => c.Terminal.Basin.Id).Distinct().Count();
    internal IEnumerable<Catchment> DisconnectedCatchments => catchments.Values;
    public IEnumerable<Catchment> LargestDisconnected => catchments.Values.OrderByDescending(c => c.Runoff).ThenBy(c => c.Terminal.TerminalId, StringComparer.Ordinal).Take(20);
    private GeodesicDrainageDelivery(GeodesicTinHydrology source, GeodesicTinBasins topology)
    { tin = source; basins = topology; networkOutcomes = new Outcome[tin.Networks.Count]; confirmedOceanParcels = new byte[tin.TriangleCount]; }

    private static Outcome Unknown(string id, string reason) => new Outcome { Destination = Destination.Unresolved, TerminalId = id, Type = reason };
    private bool Valid(TinPoint p) => p.Triangle >= 0 && p.Triangle < tin.TriangleCount &&
        !double.IsNaN(p.A + p.B + p.C) && Math.Abs(p.A + p.B + p.C - 1) <= 1e-9 && p.A >= -1e-9 && p.B >= -1e-9 && p.C >= -1e-9;

    private Outcome Terminal(TinTermination type, TinPoint? point, string fallback)
    {
        if (!point.HasValue || !Valid(point.Value)) return Unknown(fallback, "MissingOrInvalidTerminalPoint");
        var p = point.Value;
        var cacheKey = (p.Triangle, p.A, p.B, p.C, type);
        if (terminalCache.TryGetValue(cacheKey, out var cached)) return cached;
        bool ocean = type == TinTermination.OceanCoast && tin.IsRetainedOceanBoundary(p);
        bool closed = type == TinTermination.TrueClosedBasin || type == TinTermination.TinyNoiseDepression;
        var basin = closed ? basins?.Find(p) : null;
        // Floor vertex IDs identify basin catchments. Without a basin, use an exact canonical
        // mesh point (shared vertex/edge terminals do not depend on their carrier triangle).
        string key = basin != null ? "basin:" + basin.Id : PointId(p);
        var result = new Outcome { Destination = ocean ? Destination.DirectOcean : closed ? Destination.InlandClosed : Destination.Unresolved,
            TerminalId = type + ":" + key, Type = type == TinTermination.OceanCoast && !ocean ? "OceanCoastWithoutRetainedBoundary" : type.ToString(),
            Point = p, Basin = basin, OceanComponent = ocean ? oceanComponentAt?.Invoke(p) ?? -1 : -1 };
        terminalCache.Add(cacheKey, result); return result;
    }
    private string PointId(TinPoint p)
    {
        if (p.A == 1) return "vertex:" + tin.BasinVertex(p.Triangle, 0);
        if (p.B == 1) return "vertex:" + tin.BasinVertex(p.Triangle, 1);
        if (p.C == 1) return "vertex:" + tin.BasinVertex(p.Triangle, 2);
        if (p.A == 0 || p.B == 0 || p.C == 0)
        {
            int a = p.A == 0 ? 1 : 0, b = p.C == 0 ? 1 : 2;
            int va = tin.BasinVertex(p.Triangle, a), vb = tin.BasinVertex(p.Triangle, b);
            double weight = va < vb ? (a == 0 ? p.A : p.B) : (b == 1 ? p.B : p.C);
            return FormattableString.Invariant($"edge:{Math.Min(va, vb)}:{Math.Max(va, vb)}:{weight:R}");
        }
        return FormattableString.Invariant($"facet:{p.Triangle}:{p.A:R}:{p.B:R}:{p.C:R}");
    }
    private Outcome Resolve(string start)
    {
        if (resolved.TryGetValue(start, out var result)) return result;
        var path = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal); string id = start;
        while (!resolved.TryGetValue(id, out result))
        {
            if (!links.TryGetValue(id, out var link)) { result = Unknown(id, "MissingDirectedConnection"); break; }
            if (!seen.Add(id))
            {
                int first = path.IndexOf(id); CycleNodes += path.Count - first;
                result = Unknown("cycle:" + path.Skip(first).OrderBy(x => x, StringComparer.Ordinal).First(), "DirectedCycle"); break;
            }
            path.Add(id);
            string next = link.Next ?? CorridorLink(link.End);
            if (next == null) { result = link.End; break; }
            id = next;
        }
        for (int i = path.Count - 1; i >= 0; i--)
        {
            var link = links[path[i]];
            if (link.Lake || link.Corridor) result = result.Through(link.Lake, link.Corridor);
            resolved[path[i]] = result;
        }
        return result;
    }

    private string CorridorLink(Outcome end) => corridors != null && end?.Destination == Destination.InlandClosed &&
        end.Basin != null && corridors.ContainsKey(end.Basin.Id) ? "corridor:" + end.Basin.Id : null;

    private static TinPoint? End(IReadOnlyList<TinPoint> path) => path != null && path.Count > 0 ? path[path.Count - 1] : (TinPoint?)null;

    public static GeodesicDrainageDelivery Analyze(GeodesicTinHydrology tin, GeodesicTinBasins basins = null,
        IReadOnlyList<TinLakeFlowNode> lakes = null, IReadOnlyList<TinLakeAdjustedReach> reaches = null, bool renderedLakesWithoutRouting = false,
        Func<TinPoint, int> oceanComponentAt = null, int primaryOceanComponent = -1,
        IReadOnlyDictionary<int, GeodesicTinSedimentCorridors.Connection> corridors = null)
    {
        var watch = Stopwatch.StartNew(); var r = new GeodesicDrainageDelivery(tin, basins);
        r.oceanComponentAt = oceanComponentAt;
        r.lakeTraversalUnavailable = renderedLakesWithoutRouting;
        r.corridors = corridors;
        if (corridors != null)
            foreach (var pair in corridors)
            {
                var connection = pair.Value;
                string id = "corridor:" + pair.Key;
                r.links.Add(id, new Link { Corridor = true,
                    Next = connection.TargetNetwork >= 0 ? "network:" + connection.TargetNetwork : null,
                    Contact = connection.Coast, End = r.Terminal(TinTermination.OceanCoast, connection.Coast, id) });
            }
        lakes = lakes ?? Array.Empty<TinLakeFlowNode>();
        for (int i = 0; i < tin.Networks.Count; i++)
        {
            var n = tin.Networks[i]; var reach = reaches != null && i < reaches.Count ? reaches[i] : null;
            int lake = reach?.ReceivingLakeId ?? -1, join = reach?.JunctionNetwork ?? n.JunctionNetwork;
            var type = reach?.Termination ?? n.Termination; string id = "network:" + i;
            var contact = End(reach?.Samples ?? n.Samples);
            r.links.Add(id, new Link { Next = lake >= 0 ? "lake:" + lake : type == TinTermination.Confluence && join >= 0 ? "network:" + join : null,
                Contact = contact, End = r.Terminal(type, contact, id) });
        }
        var owners = new int[tin.TriangleCount]; Array.Fill(owners, -1);
        foreach (var lake in lakes)
        {
            string id = "lake:" + lake.BasinId;
            Outcome end = lake.Basin != null && !lake.Basin.HasEscape
                ? r.Terminal(TinTermination.TrueClosedBasin, lake.Basin.Floor, id)
                : r.Terminal(lake.OutletTermination, End(lake.OutletPath) ?? (lake.ExitsAtSpill ? lake.CoastContactAtSpill ?? lake.Basin?.Spill : null), id);
            if (lake.Terminal && lake.JoinNetwork < 0 && lake.Basin != null && !lake.Basin.HasEscape &&
                end.Destination == Destination.InlandClosed)
                end = end.Through(true, false, lake.BasinId);
            r.links.Add(id, new Link { Lake = true, End = end,
                Contact = lake.DownstreamLakeEntry ?? End(lake.OutletPath) ?? lake.CoastContactAtSpill ?? lake.Basin?.Floor,
                Next = lake.DownstreamLakeId >= 0 ? "lake:" + lake.DownstreamLakeId : lake.JoinNetwork >= 0 ? "network:" + lake.JoinNetwork : null });
            foreach (int parcel in lake.DirectParcels)
            {
                if (parcel < 0 || parcel >= owners.Length) { r.InvalidMemberships++; continue; }
                if (owners[parcel] != -1) { r.DuplicateMemberships++; owners[parcel] = -2; }
                else owners[parcel] = lake.BasinId;
            }
            if (lake.DownstreamLakeId >= 0 && lake.OutletPath.Length < 2) r.LakeBoundaryGaps++;
            else if (lake.OutletPath.Length >= 2 && lake.Discharge > 0 && lake.OutletReachId < 0) r.ThresholdClipped++;
        }
        // Audit even zero-discharge lake edges. Existing router exceptions remain untouched.
        foreach (var id in r.links.Keys) r.Resolve(id);
        for (int t = 0; t < tin.TriangleCount; t++)
        {
            double amount = tin.ParcelRunoff(t); if (amount <= 0) continue;
            r.TotalRunoff += amount; r.TotalArea += tin.LocalArea[t]; r.IndependentParcels++;
            Outcome end;
            if (renderedLakesWithoutRouting) end = Unknown("unintegrated-rendered-lakes", "RenderedLakeTraversalUnavailable");
            else if (owners[t] == -2) end = Unknown("parcel:" + t, "DuplicateFirstLakeOwnership");
            else if (owners[t] >= 0) end = r.Resolve("lake:" + owners[t]);
            else if (tin.ParcelDestinations == null) end = Unknown("parcel:" + t, "ParcelEndpointNotCaptured");
            else
            {
                var p = tin.ParcelDestinations[t]; end = r.Terminal(p.Termination, p.Point, "parcel:" + t);
                string next = r.CorridorLink(end);
                if (next != null) end = r.Resolve(next);
            }
            r.runoffByRiverDestination[(int)end.RiverColour] += amount;
            switch (end.Destination)
            {
                case Destination.DirectOcean: r.DirectOcean += amount; break;
                case Destination.OceanViaLakes: r.ViaLakesOcean += amount; break;
                case Destination.InlandClosed: r.InlandClosed += amount; break;
                default: r.Unresolved += amount; break;
            }
            r.confirmedOceanParcels[t] = end.Destination == Destination.DirectOcean ? (byte)1 : end.Destination == Destination.OceanViaLakes ? (byte)2 : (byte)0;
            r.terminalRunoff.TryGetValue(end.Type, out double previous); r.terminalRunoff[end.Type] = previous + amount;
            if (end.Destination == Destination.DirectOcean || end.Destination == Destination.OceanViaLakes)
            {
                if (end.ViaCorridor) r.CorridorOcean += amount;
                if (end.OceanComponent < 0) r.UnattributedOcean += amount;
                else if (end.OceanComponent == primaryOceanComponent) r.PrimaryOcean += amount;
                else r.SecondaryOcean += amount;
                continue;
            }
            if (!r.catchments.TryGetValue(end.TerminalId, out var catchment))
                r.catchments.Add(end.TerminalId, catchment = new Catchment { Terminal = end });
            catchment.Runoff += amount; catchment.Area += tin.LocalArea[t]; catchment.Parcels++; catchment.ViaLake |= end.ViaLake;
        }
        int rawFailures = 0;
        for (int i = 0; i < tin.Networks.Count; i++)
        {
            var end = r.networkOutcomes[i] = r.Resolve("network:" + i);
            if (r.catchments.TryGetValue(end.TerminalId, out var c)) c.SourcePaths++;
            if (tin.Networks[i].Termination != TinTermination.RoutingFailure) continue;
            rawFailures++; r.CountBreak(end);
        }
        r.InitialUnexpectedBrokenNetworks = tin.Diagnostics.UnexpectedBrokenNetworks;
        r.ExtractionAmbiguities = Math.Max(0, r.InitialUnexpectedBrokenNetworks - rawFailures);
        r.UnknownBreaks += r.ExtractionAmbiguities;
        // Estimate managed storage, not process RSS: endpoints (40 B/triangle), owners,
        // graph entries, dictionaries/strings and catchments. Runtime object headers vary.
        r.ApproximateMemoryBytes = (tin.ParcelDestinations == null ? 0L : 40L * tin.TriangleCount) +
            5L * tin.TriangleCount + 400L * r.links.Count + r.terminalCache.Values.Sum(o => 240L + 2L * o.TerminalId.Length) +
            r.catchments.Values.Sum(c => 320L + 2L * c.Terminal.TerminalId.Length);
        foreach (var link in r.links.Values) link.Next ??= r.CorridorLink(link.End);
        r.oceanComponentAt = null; r.corridors = null; r.terminalCache = null;
        r.AnalysisMilliseconds = watch.Elapsed.TotalMilliseconds; return r;
    }
    private void CountBreak(Outcome end)
    {
        if (end.Destination != Destination.Unresolved) VisualOnlyBreaks++;
        else if (end.Type == "RoutingFailure" || end.Type == "MissingDirectedConnection" || end.Type == "DirectedCycle") LogicalDisconnected++;
        else UnknownBreaks++;
    }
    // Called exactly where the old visual code increments unexpectedBrokenNetworks.
    public void ObserveVisualBreak(int network, string cause)
    {
        CountBreak(networkOutcomes[network]);
        if (cause == "centreline") CentrelineRejected++;
        else if (cause == "projection") ProjectionRejected++;
        else if (cause == "missing-trunk") MissingTrunkRibbons++;
    }
    public void ObserveShortLink(bool lakeBoundary)
    { if (lakeBoundary) LakeBoundaryGaps++; else ShortUnrenderedLinks++; }
    private double Percent(double value) => TotalRunoff > 0 ? 100 * value / TotalRunoff : double.NaN;
    public string Summary() => FormattableString.Invariant($"[GeodesicDrainageDelivery] totalRunoff={TotalRunoff:R} directOcean={DirectOcean:R} viaLakesOcean={ViaLakesOcean:R} inlandClosed={InlandClosed:R} unresolved={Unresolved:R} oceanPercent={Percent(DirectOcean + ViaLakesOcean):F6} inlandPercent={Percent(InlandClosed):F6} unresolvedPercent={Percent(Unresolved):F6} independentParcels={IndependentParcels} contributingArea={TotalArea:R} accountingResidual={Residual:R} tolerance={Tolerance:R} accountingPass={Math.Abs(Residual) <= Tolerance} duplicateFirstLakeMemberships={DuplicateMemberships} invalidMemberships={InvalidMemberships} directedCycleNodes={CycleNodes} analysisMs={AnalysisMilliseconds:F3} approximatePeakBytes={ApproximateMemoryBytes} units=existing-TIN-local-runoff terminalRunoff=[{string.Join(",", terminalRunoff.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value.ToString("R", CultureInfo.InvariantCulture)))}]");
    public string Connectivity(int finalUnexpected) => $"[GeodesicDrainageConnectivity] unexpectedBrokenNetworks={finalUnexpected} logicalDisconnected={LogicalDisconnected} visualOnlyBreaks={VisualOnlyBreaks} unknown={UnknownBreaks} classifiedBreakEvents={LogicalDisconnected + VisualOnlyBreaks + UnknownBreaks} extractionAmbiguities={ExtractionAmbiguities} centrelineRejected={CentrelineRejected} projectionRejected={ProjectionRejected} missingTrunkRibbons={MissingTrunkRibbons} lakeBoundaryGaps={LakeBoundaryGaps} shortUnrenderedLinks={ShortUnrenderedLinks} thresholdClippedLakeOutlets={ThresholdClipped} gapCountsAreSeparate=True meshCracks=not-observed representativeReceiverIsNotParcelRoute=True";
    public IEnumerable<string> CatchmentLogs(Func<TinBasinDescriptor, string> carvingEvidence = null)
    {
        int rank = 0;
        foreach (var c in LargestDisconnected)
        {
            var end = c.Terminal; var b = end.Basin;
            string position = end.Point.HasValue ? PointPosition(end.Point.Value) : "unknown";
            yield return FormattableString.Invariant($"[GeodesicDrainageClosedCatchment] rank={++rank} terminalId={end.TerminalId} terminalType={end.Type} destination={end.Destination} position={position} runoff={c.Runoff:R} area={c.Area:R} independentParcels={c.Parcels} contributingSourcePaths={c.SourcePaths} passesRenderedLakes={c.ViaLake} spillWitness={(b == null ? "unknown" : (b.HasEscape && b.DownstreamTriangle >= 0 && b.DownstreamVertex >= 0).ToString())} spillElevation={(b != null && b.HasEscape ? b.SpillElevation.ToString("R", CultureInfo.InvariantCulture) : "unknown")} floorToSpillBarrier={(b != null && b.HasEscape ? b.MaximumDepth.ToString("R", CultureInfo.InvariantCulture) : "unknown")} requiredIncision=unknown carvingEvidence={carvingEvidence?.Invoke(b) ?? "unavailable"} closedDoesNotImplyMustDrain=True");
        }
    }
    private string PointPosition(TinPoint p)
    {
        var d = tin.Direction(p); double height = tin.Height(p, true);
        return FormattableString.Invariant($"(facet:{p.Triangle},bary:{p.A:R}/{p.B:R}/{p.C:R},direction:{d.x:R}/{d.y:R}/{d.z:R},height:{height:R})");
    }
}
