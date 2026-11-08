using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicDrainageDeliveryTests
{
    private static GeodesicTinHydrology Prepare(GeodesicTinHydrologyTests.Fixture f)
    { f.Tin.CaptureParcelDestinations = true; f.Tin.Accumulate(); f.Tin.ExtractNetworks(.01); return f.Tin; }
    private static GeodesicTinHydrology Plane() => Prepare(GeodesicTinHydrologyTests.Grid((x, y) => -x, size: 8, ocean: true, boundaryOutflow: false));
    private static void Accounted(GeodesicDrainageDelivery r)
    { Assert.That(Math.Abs(r.Residual), Is.LessThanOrEqualTo(r.Tolerance)); Assert.That(r.DuplicateMemberships, Is.Zero); Assert.That(r.CycleNodes, Is.Zero); }
    private static GeodesicDrainageDelivery Lakes(GeodesicTinHydrology tin, double threshold = 0)
    {
        var basins = GeodesicTinBasins.Build(tin, 0, 0);
        var routing = GeodesicTinLakeRouting.Build(tin, basins, GeodesicTinLakeGeometry.Build(tin, basins), threshold);
        return GeodesicDrainageDelivery.Analyze(tin, basins, routing.Lakes, routing.RiverReaches);
    }
    [Test] public void DirectOceanCountsEachOriginalFacetExactlyOnce()
    {
        var tin = Plane(); var r = GeodesicDrainageDelivery.Analyze(tin);
        Assert.That(r.DirectOcean, Is.EqualTo(r.TotalRunoff).Within(1e-10)); Assert.That(r.TotalRunoff, Is.EqualTo(2).Within(1e-10));
        Assert.That(r.ViaLakesOcean, Is.Zero); Accounted(r);
        Assert.That(r.RiverOutcome("network:0").RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.Ocean));
        Assert.That(r.RiverRunoff(GeodesicDrainageDelivery.RiverDestination.Ocean), Is.EqualTo(r.TotalRunoff).Within(r.Tolerance));
        Assert.That(r.RiverDestinationAccountingPass, Is.True);
    }
    [Test] public void RenderedLakeOutletReachesOceanEvenBelowRibbonThreshold()
    {
        var tin = Prepare(GeodesicTinHydrologyTests.Grid((x, y) =>
            (x <= -.5f ? 1 + 8 * (-.5f - x) : x <= 0 ? 1 + 4 * (x + .5f) : 3 - 4 * x) + 8 * (y - 1) * (y - 1),
            size: 16, ocean: true, boundaryOutflow: false));
        var visible = Lakes(tin); var clipped = Lakes(tin, 1000);
        Assert.That(visible.ViaLakesOcean, Is.GreaterThan(0)); Assert.That(clipped.ViaLakesOcean, Is.EqualTo(visible.ViaLakesOcean));
        Assert.That(clipped.ThresholdClipped, Is.GreaterThan(0)); Accounted(clipped);
    }
    [Test] public void TwoActualLakesDeliverTheirUnionThroughTheFinalOutlet()
    {
        float Profile(float x) => x <= -.625f ? 3 + (-.625f - x) * 8 : x <= -.25f ? 3 + (x + .625f) / .375f :
            x <= .125f ? 4 - (x + .25f) * 8 : x <= .5f ? 1 + (x - .125f) / .375f : 2 - (x - .5f) * 6;
        var tin = Prepare(GeodesicTinHydrologyTests.Grid((x, y) => Profile(x) + 12 * (y - 1) * (y - 1), size: 32, ocean: true, boundaryOutflow: false));
        var basins = GeodesicTinBasins.Build(tin, 0, 0); var routing = GeodesicTinLakeRouting.Build(tin, basins, GeodesicTinLakeGeometry.Build(tin, basins));
        Assert.That(routing.Lakes.Count, Is.EqualTo(2));
        var lower = routing.Lakes.OrderBy(n => n.Basin.SpillElevation).First();
        var r = GeodesicDrainageDelivery.Analyze(tin, basins, routing.Lakes, routing.RiverReaches);
        Assert.That(r.ViaLakesOcean, Is.EqualTo(lower.Discharge).Within(1e-9)); Accounted(r);
        foreach (var lake in routing.Lakes)
            Assert.That(r.RiverOutcome("lake:" + lake.BasinId).RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.Ocean));
        foreach (var reach in routing.RiverReaches.Where(reach => reach.ReceivingLakeId >= 0))
            Assert.That(r.RiverOutcome("network:" + reach.RawNetworkId).RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.Ocean));
    }
    [Test] public void LegitimateClosedBowlRetainsRunoffAndRanksByIndependentInput()
    {
        var tin = Prepare(GeodesicTinHydrologyTests.Grid((x, y) => x * x + (y - 1) * (y - 1), size: 8, boundaryOutflow: false));
        var r = GeodesicDrainageDelivery.Analyze(tin, GeodesicTinBasins.Build(tin, 0, 0));
        Assert.That(r.InlandClosed, Is.EqualTo(r.TotalRunoff).Within(1e-9)); Assert.That(r.LargestDisconnected.First().Area, Is.GreaterThan(0));
        Assert.That(r.LargestDisconnected.First().Terminal.Type, Is.EqualTo("TrueClosedBasin")); Accounted(r);
        Assert.That(r.RiverOutcome("network:0").RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.ClosedBasin));
        Assert.That(r.RiverRunoff(GeodesicDrainageDelivery.RiverDestination.ClosedBasin), Is.EqualTo(r.TotalRunoff).Within(r.Tolerance));
    }
    [Test] public void DescriptorlessTerminalRequiresAVerifiedShorePathAndTerminalLakeRemainsPurple()
    {
        var tin = Plane();
        int parcel = Enumerable.Range(0, tin.TriangleCount).First(t => tin.ParcelRunoff(t) > 0);
        var floor = new TinPoint(parcel, 1, 0, 0);
        int vertex = GeodesicDrainageDelivery.ExactVertex(tin, floor);
        var coast = tin.ParcelDestinations[parcel].Point;
        var reaches = tin.Networks.Select(n => new TinLakeAdjustedReach
        {
            Samples = n.Samples, JunctionNetwork = n.JunctionNetwork, Termination = n.Termination
        }).ToArray();
        reaches[0].Termination = TinTermination.TrueClosedBasin;
        reaches[0].Samples = new[] { tin.Centroid(parcel), floor };
        var lake = new TinLakeFlowNode(vertex)
        {
            Basin = new TinBasinDescriptor(vertex, floor, -1, 0, null, -1, -1, -1,
                new[] { parcel }, 1, false, false, 0, 0)
        };
        reaches[1].ReceivingLakeId = vertex;
        lake.DirectParcels.Add(parcel);
        GeodesicDrainageDelivery Analyze(Vector3[] shorePath)
        {
            var corridor = new GeodesicTinSedimentCorridors.Connection(vertex, -1, coast, new[] { vertex },
                new[] { 0 }, Array.Empty<int>(), 0, 0, 0, 0, shorePath);
            return GeodesicDrainageDelivery.Analyze(tin, lakes: new[] { lake }, reaches: reaches,
                corridors: new Dictionary<int, GeodesicTinSedimentCorridors.Connection> { [vertex] = corridor });
        }
        var before = Analyze(null);
        Assert.That(before.RiverOutcome("network:0").RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.ClosedBasin));
        var final = Analyze(new[] { tin.Direction(floor), tin.Direction(coast) });
        Assert.That(final.RiverOutcome("network:0").RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.Ocean));
        Assert.That(final.RiverOutcome("network:1").RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.TerminalLake));
        Assert.That(final.RiverRunoff(GeodesicDrainageDelivery.RiverDestination.TerminalLake), Is.EqualTo(tin.ParcelRunoff(parcel)));
        Accounted(final);
    }
    [Test] public void ClippedRibbonDoesNotChangeLogicalOceanDelivery()
    {
        var tin = Plane(); var r = GeodesicDrainageDelivery.Analyze(tin); double direct = r.DirectOcean;
        r.ObserveVisualBreak(0, "projection");
        Assert.That(r.VisualOnlyBreaks, Is.EqualTo(1)); Assert.That(r.LogicalDisconnected, Is.Zero);
        Assert.That(r.DirectOcean, Is.EqualTo(direct)); Accounted(r);
    }
    [Test] public void SharedSaddleLogicalTransferNeedsNoRibbonAndNeverAddsReciprocalEdge()
    {
        var tin = Plane(); int parcel = Enumerable.Range(0, tin.TriangleCount).First(t => tin.ParcelRunoff(t) > 0);
        var a = new TinLakeFlowNode(10) { DownstreamLakeId = 20 };
        var b = new TinLakeFlowNode(20) { OutletTermination = TinTermination.OceanCoast, OutletPath = new[] { tin.ParcelDestinations[parcel].Point } };
        var saddle = tin.Centroid(parcel);
        a.Basin = new TinBasinDescriptor(10, saddle, -1, 1, saddle, parcel, 0, 20, new[] { parcel }, 1, false, false, 0, 0);
        b.Basin = new TinBasinDescriptor(20, saddle, -2, 1, saddle, parcel, 0, -1, new[] { parcel }, 1, false, false, 0, 0);
        Assert.That(a.Basin.Spill, Is.EqualTo(b.Basin.Spill));
        a.DirectParcels.Add(parcel);
        GeodesicTinLakeRouting.ResolveDischarge(new[] { a, b }, tin.ParcelRunoff);
        var r = GeodesicDrainageDelivery.Analyze(tin, lakes: new[] { a, b });
        Assert.That(r.ViaLakesOcean, Is.EqualTo(tin.ParcelRunoff(parcel))); Assert.That(b.DownstreamLakeId, Is.EqualTo(-1));
        Assert.That(r.LakeBoundaryGaps, Is.EqualTo(1)); Accounted(r);
    }
    [Test] public void ConfluenceAccountsLocalInputOnceAndFollowsNetworkFromLakeOutlet()
    {
        var tin = Plane(); var parcels = Enumerable.Range(0, tin.TriangleCount).Where(t => tin.ParcelRunoff(t) > 0).Take(2).ToArray();
        Assert.That(tin.Networks.Count, Is.GreaterThanOrEqualTo(3));
        var reaches = tin.Networks.Select((n, i) => new TinLakeAdjustedReach { RawNetworkId = i, Samples = n.Samples,
            Termination = i == 1 || i == 2 ? TinTermination.Confluence : n.Termination,
            JunctionNetwork = i == 1 || i == 2 ? 0 : n.JunctionNetwork }).ToArray();
        var a = new TinLakeFlowNode(10) { JoinNetwork = 1 }; a.DirectParcels.Add(parcels[0]);
        var b = new TinLakeFlowNode(20) { JoinNetwork = 2 }; b.DirectParcels.Add(parcels[1]);
        var r = GeodesicDrainageDelivery.Analyze(tin, lakes: new[] { a, b }, reaches: reaches);
        Assert.That(r.ViaLakesOcean, Is.EqualTo(parcels.Sum(tin.ParcelRunoff)));
        Assert.That(r.TotalRunoff, Is.LessThan(tin.AccumulatedRunoff.Sum())); Accounted(r);
        var trunk = r.RiverOutcome("network:0");
        Assert.That(trunk.RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.Ocean));
        Assert.That(r.RiverOutcome("network:1"), Is.SameAs(trunk));
        Assert.That(r.RiverOutcome("network:2"), Is.SameAs(trunk));
    }
    [Test] public void MissingDirectedLakeEdgeIsExplicitlyUnresolved()
    {
        var tin = Plane(); int parcel = Enumerable.Range(0, tin.TriangleCount).First(t => tin.ParcelRunoff(t) > 0);
        var lake = new TinLakeFlowNode(10) { DownstreamLakeId = 99 }; lake.DirectParcels.Add(parcel);
        var r = GeodesicDrainageDelivery.Analyze(tin, lakes: new[] { lake });
        Assert.That(r.Unresolved, Is.EqualTo(tin.ParcelRunoff(parcel))); Assert.That(r.LargestDisconnected.First().Terminal.Type, Is.EqualTo("MissingDirectedConnection")); Accounted(r);
        Assert.That(r.RiverOutcome("lake:10").RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.Unresolved));
        Assert.That(r.RiverOutcome("lake:10").Type, Is.EqualTo("MissingDirectedConnection"));
    }
    [Test] public void DiagnosticCyclesRemainUnresolvedAndProductionCycleExceptionRemainsActive()
    {
        var tin = Plane(); var a = new TinLakeFlowNode(10) { DownstreamLakeId = 20 }; var b = new TinLakeFlowNode(20) { DownstreamLakeId = 10 };
        int parcel = Enumerable.Range(0, tin.TriangleCount).First(t => tin.ParcelRunoff(t) > 0); a.DirectParcels.Add(parcel);
        var r = GeodesicDrainageDelivery.Analyze(tin, lakes: new[] { a, b });
        Assert.That(r.CycleNodes, Is.EqualTo(2)); Assert.That(r.Unresolved, Is.EqualTo(tin.ParcelRunoff(parcel)));
        Assert.That(r.RiverOutcome("lake:10").RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.Unresolved));
        Assert.That(r.RiverOutcome("lake:20").Type, Is.EqualTo("DirectedCycle"));
        Assert.Throws<InvalidOperationException>(() => GeodesicTinLakeRouting.ResolveDischarge(new[] { a, b }, tin.ParcelRunoff, _ => { }));
    }
    [Test] public void RiverAndTributaryInheritTerminalLakeWithNoOutlet()
    {
        var tin = Plane();
        int parcel = Enumerable.Range(0, tin.TriangleCount).First(t => tin.ParcelRunoff(t) > 0);
        var lake = new TinLakeFlowNode(10)
        {
            Basin = new TinBasinDescriptor(10, tin.Centroid(parcel), -1, 0, null, -1, -1, -1,
                new[] { parcel }, 1, false, false, 0, 0)
        };
        lake.DirectParcels.Add(parcel);
        var reaches = tin.Networks.Select(n => new TinLakeAdjustedReach
        {
            Samples = n.Samples, JunctionNetwork = n.JunctionNetwork, Termination = n.Termination
        }).ToArray();
        reaches[0].ReceivingLakeId = lake.BasinId;
        reaches[1].Termination = TinTermination.Confluence;
        reaches[1].JunctionNetwork = 0;
        var delivery = GeodesicDrainageDelivery.Analyze(tin, lakes: new[] { lake }, reaches: reaches);
        foreach (string route in new[] { "lake:10", "network:0", "network:1" })
        {
            var end = delivery.RiverOutcome(route);
            Assert.That(end.RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.TerminalLake));
            Assert.That(end.TerminalLakeId, Is.EqualTo(lake.BasinId));
        }
        Assert.That(delivery.RiverRunoff(GeodesicDrainageDelivery.RiverDestination.TerminalLake), Is.EqualTo(tin.ParcelRunoff(parcel)));
        Assert.That(delivery.RiverDestinationAccountingPass, Is.True);
    }

    [Test] public void DuplicateLakeOwnershipIsNotDoubleCounted()
    {
        var tin = Plane(); var a = new TinLakeFlowNode(10); var b = new TinLakeFlowNode(20);
        int parcel = Enumerable.Range(0, tin.TriangleCount).First(t => tin.ParcelRunoff(t) > 0); a.DirectParcels.Add(parcel); b.DirectParcels.Add(parcel);
        var r = GeodesicDrainageDelivery.Analyze(tin, lakes: new[] { a, b });
        Assert.That(r.DuplicateMemberships, Is.EqualTo(1)); Assert.That(r.Unresolved, Is.EqualTo(tin.ParcelRunoff(parcel)));
        Assert.That(Math.Abs(r.Residual), Is.LessThanOrEqualTo(r.Tolerance));
    }
    [Test] public void CoastHandoffAtSpillRemainsDeliverableWithoutARibbon()
    {
        var grid = GeodesicTinHydrologyTests.Grid((x, y) => -x, size: 8, boundaryOutflow: false);
        var tin = Prepare(new GeodesicTinHydrologyTests.Fixture
        {
            Tin = new GeodesicTinHydrology(grid.Vertices, grid.Triangles, grid.Heights, grid.Heights,
                grid.Vertices.Select(v => v.x >= .75f).ToArray(), onSphere: false)
        });
        TinPoint spill = grid.At(.5f, .62f);
        var path = tin.TraceLakeOutlet(spill, null, out var end);
        Assert.That(end, Is.EqualTo(TinTermination.OceanCoast));
        Assert.That(tin.IsRetainedOceanBoundary(spill), Is.False);
        Assert.That(tin.IsRetainedOceanBoundary(path.Last()), Is.True);
        Assert.That(tin.Direction(path.Last()), Is.EqualTo(tin.Direction(spill)));
        int parcel = Enumerable.Range(0, tin.TriangleCount).First(t => tin.ParcelRunoff(t) > 0);
        var lake = new TinLakeFlowNode(10) { ExitsAtSpill = true, OutletTermination = end, CoastContactAtSpill = path.Last() };
        lake.DirectParcels.Add(parcel);
        var delivery = GeodesicDrainageDelivery.Analyze(tin, lakes: new[] { lake });
        Assert.That(delivery.ViaLakesOcean, Is.EqualTo(tin.ParcelRunoff(parcel)));
        Accounted(delivery);
    }

    [Test] public void ExternalFixtureBoundaryIsNeverMistakenForRetainedOcean()
    {
        var tin = Prepare(GeodesicTinHydrologyTests.Grid((x, y) => -x, size: 8));
        var r = GeodesicDrainageDelivery.Analyze(tin);
        Assert.That(r.DirectOcean, Is.Zero); Assert.That(r.Unresolved, Is.EqualTo(r.TotalRunoff)); Accounted(r);
    }
    [Test] public void IdenticalFingerprintsAreStableAcrossOrderAndCultureAndDetectEveryBitChange()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            var a = new GeodesicGenerationFingerprint(); a.Add("seed", 123456); a.Add("depth", .005f);
            string expected = a.ParametersHash;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");
            var b = new GeodesicGenerationFingerprint(); b.Add("depth", .005f); b.Add("seed", 123456);
            Assert.That(b.ParametersHash, Is.EqualTo(expected)); b.Add("depth", .005001f); Assert.That(b.ParametersHash, Is.Not.EqualTo(expected));
            Assert.That(GeodesicGenerationFingerprint.Heights(new[] { 8.008490f, 8.552721f }), Is.EqualTo(GeodesicGenerationFingerprint.Heights(new[] { 8.008490f, 8.552721f })));
            Assert.That(GeodesicGenerationFingerprint.Heights(new[] { 8.008490f }), Is.Not.EqualTo(GeodesicGenerationFingerprint.Heights(new[] { 8.012440f })));
            Assert.That(GeodesicGenerationFingerprint.Mask(new[] { true, false }), Is.Not.EqualTo(GeodesicGenerationFingerprint.Mask(new[] { false, true })));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
    [Test] public void DiagnosticsLeaveRoutesHeightsAndRunoffUntouchedAndCanBeDisabled()
    {
        var tin = Plane(); var heights = (float[])tin.HydrologyHeight.Clone(); var receivers = (int[])tin.TriangleReceiver.Clone();
        var runoff = (double[])tin.AccumulatedRunoff.Clone(); var paths = tin.Networks.Select(n => n.Samples.ToArray()).ToArray();
        GeodesicDrainageDelivery.Analyze(tin);
        CollectionAssert.AreEqual(heights, tin.HydrologyHeight); CollectionAssert.AreEqual(receivers, tin.TriangleReceiver); CollectionAssert.AreEqual(runoff, tin.AccumulatedRunoff);
        for (int i = 0; i < paths.Length; i++) CollectionAssert.AreEqual(paths[i], tin.Networks[i].Samples);
        tin.CaptureParcelDestinations = false; tin.Accumulate(); Assert.That(tin.ParcelDestinations, Is.Null); CollectionAssert.AreEqual(runoff, tin.AccumulatedRunoff);
    }
}
