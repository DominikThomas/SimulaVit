using System;
using System.Linq;
using NUnit.Framework;

public sealed class GeodesicTinLakeRoutingTests
{
    private static GeodesicTinHydrologyTests.Fixture Overflow()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) =>
            (x <= -.5f ? 1 + 8 * (-.5f - x) : x <= 0 ? 1 + 4 * (x + .5f) : 3 - 4 * x)
            + 8 * (y - 1) * (y - 1), size: 16, ocean: true, boundaryOutflow: false);
        f.Tin.Accumulate(); f.Tin.ExtractNetworks(.01); return f;
    }
    private static GeodesicTinLakeRouting Route(GeodesicTinHydrology tin, double depth = 0)
    {
        var basins = GeodesicTinBasins.Build(tin, 0, depth);
        return GeodesicTinLakeRouting.Build(tin, basins, GeodesicTinLakeGeometry.Build(tin, basins));
    }
    [Test] public void OverflowClipsExactlyAtShorelineAndStartsAtExactSpill()
    {
        var f = Overflow(); var routing = Route(f.Tin); var lake = routing.Lakes.Single();
        var path = f.Tin.Trace(f.At(-.9f, 1.1f), out _);
        Assert.That(routing.FirstEntry(path, out var entry));
        var clipped = GeodesicTinLakeRouting.ClipAtEntry(path, entry);
        Assert.That(f.Tin.Height(clipped.Last()), Is.EqualTo(lake.Basin.SpillElevation).Within(1e-12));
        Assert.That(clipped.Last().Equals(f.Tin.Centroid(clipped.Last().Triangle)), Is.False);
        Assert.That(lake.OutletReachId, Is.GreaterThanOrEqualTo(f.Tin.Networks.Count));
        Assert.That(lake.OutletPath[0].Equals(lake.Basin.Spill.Value));
        Assert.That(f.Tin.ValidateLakePath(lake.OutletPath));
        Assert.That(routing.FirstEntry(lake.OutletPath, out _), Is.False);
        Assert.That(routing.FlowConservationErrors, Is.Zero);
        Assert.That(lake.InletDischarges.Count, Is.GreaterThan(1));
        Assert.That(routing.Lakes.Count(n => n.OutletReachId >= 0), Is.EqualTo(1));
    }
    [Test] public void IndependentTributariesCombineOnceAndTerminalHoldsTheirDischarge()
    {
        var a = new TinLakeFlowNode(1) { DownstreamLakeId = 3 };
        var b = new TinLakeFlowNode(2) { DownstreamLakeId = 3 };
        var terminal = new TinLakeFlowNode(3);
        a.DirectParcels.UnionWith(new[] { 0, 1 }); b.DirectParcels.UnionWith(new[] { 2, 3 }); terminal.DirectParcels.Add(4);
        Assert.That(GeodesicTinLakeRouting.ResolveDischarge(new[] { terminal, b, a }, id => id + 1), Is.Zero);
        Assert.That(a.Discharge, Is.EqualTo(3)); Assert.That(b.Discharge, Is.EqualTo(7));
        Assert.That(terminal.Discharge, Is.EqualTo(15)); Assert.That(terminal.Terminal);
        Assert.That(terminal.OutletReachId, Is.EqualTo(-1));
    }
    [Test] public void SharedUpstreamParcelsAreNotCountedTwice()
    {
        var a = new TinLakeFlowNode(1) { DownstreamLakeId = 3 }; var b = new TinLakeFlowNode(2) { DownstreamLakeId = 3 }; var c = new TinLakeFlowNode(3);
        a.DirectParcels.UnionWith(new[] { 0, 1 }); b.DirectParcels.UnionWith(new[] { 1, 2 }); c.DirectParcels.Add(2);
        Assert.That(GeodesicTinLakeRouting.ResolveDischarge(new[] { a, b, c }, id => id + 1), Is.EqualTo(2));
        Assert.That(c.Discharge, Is.EqualTo(6)); Assert.That(c.ContributingParcels.Count, Is.EqualTo(3));
    }
    [Test] public void SubthresholdOutletStillTransfersFlowWithoutCreatingANewVisibleRiverSource()
    {
        var f = Overflow(); var basins = GeodesicTinBasins.Build(f.Tin, 0, 0); var geometry = GeodesicTinLakeGeometry.Build(f.Tin, basins);
        var visible = GeodesicTinLakeRouting.Build(f.Tin, basins, geometry);
        var hidden = GeodesicTinLakeRouting.Build(f.Tin, basins, geometry, 1000);
        Assert.That(hidden.Lakes.Single().OutletReachId, Is.EqualTo(-1));
        Assert.That(hidden.Lakes.Single().Discharge, Is.EqualTo(visible.Lakes.Single().Discharge));
        CollectionAssert.AreEqual(hidden.Lakes.Single().OutletPath, visible.Lakes.Single().OutletPath);
        Assert.That(hidden.FlowConservationErrors, Is.Zero);
    }
    [Test] public void CycleIsRejectedBeforeAnyNonconservativeOutletIsPublished()
    {
        var a = new TinLakeFlowNode(1) { DownstreamLakeId = 2 }; var b = new TinLakeFlowNode(2) { DownstreamLakeId = 1 };
        Assert.Throws<InvalidOperationException>(() => GeodesicTinLakeRouting.ResolveDischarge(new[] { a, b }, _ => 1));
    }
    [Test] public void CascadedGeometryTransfersIntoLowerLakeAndConservesLocalAndUpstreamFlow()
    {
        float Profile(float x) => x <= -.625f ? 3 + (-.625f - x) * 8 : x <= -.25f ? 3 + (x + .625f) / .375f :
            x <= .125f ? 4 - (x + .25f) * 8 : x <= .5f ? 1 + (x - .125f) / .375f : 2 - (x - .5f) * 6;
        var f = GeodesicTinHydrologyTests.Grid((x, y) => Profile(x) + 12 * (y - 1) * (y - 1), size: 32, ocean: true, boundaryOutflow: false);
        f.Tin.Accumulate(); f.Tin.ExtractNetworks(.01); var r = Route(f.Tin);
        Assert.That(r.Lakes.Count, Is.EqualTo(2));
        var high = r.Lakes.OrderByDescending(n => n.Basin.SpillElevation).First(); var low = r.Lakes.OrderBy(n => n.Basin.SpillElevation).First();
        Assert.That(high.DownstreamLakeId, Is.EqualTo(low.BasinId));
        Assert.That(low.ContributingParcels.IsSupersetOf(high.ContributingParcels));
        Assert.That(low.Discharge, Is.EqualTo(low.DirectParcels.Sum(f.Tin.ParcelRunoff) + high.Discharge).Within(1e-10));
        Assert.That(r.FlowConservationErrors, Is.Zero);
    }
    [Test] public void MissAndNonqualifyingBasinsLeaveRawPathsUntouched()
    {
        var f = Overflow(); var routing = Route(f.Tin);
        var miss = f.Tin.Trace(f.At(.4f, 1.1f), out _);
        Assert.That(routing.FirstEntry(miss, out _), Is.False);
        Assert.That(routing.RiverReaches.Any(r => !r.Changed));
        foreach (var reach in routing.RiverReaches.Where(r => !r.Changed)) Assert.That(reach.Samples, Is.SameAs(f.Tin.Networks[reach.RawNetworkId].Samples));
        var disabled = Route(f.Tin, 100);
        Assert.That(disabled.Lakes, Is.Empty);
        foreach (var r in disabled.RiverReaches) { Assert.That(r.Changed, Is.False); Assert.That(r.Samples, Is.SameAs(f.Tin.Networks[r.RawNetworkId].Samples)); }
    }
    [Test] public void ReplayDoesNotChangeRawHydrologyAndAdjustedTopologyIsDeterministic()
    {
        var f = Overflow(); var tin = f.Tin;
        var raw = tin.Networks.Select(n => n.Samples.ToArray()).ToArray();
        var metadata = tin.Networks.Select(n => (n.SourceTriangle, n.Termination, n.JunctionNetwork, n.Length)).ToArray();
        var heights = tin.HydrologyHeight.ToArray(); var visible = tin.VisibleHeight.ToArray(); var receiver = tin.TriangleReceiver.ToArray();
        var runoff = tin.AccumulatedRunoff.ToArray(); var area = tin.DrainageArea.ToArray(); var diagnostics = tin.Diagnostics.Summary();
        var ocean = Enumerable.Range(0, tin.TriangleCount).Select(t => tin.IsOcean(tin.Centroid(t))).ToArray();
        var gradients = Enumerable.Range(0, tin.TriangleCount).Select(tin.DownhillGradient).ToArray();
        var a = Route(tin); var b = Route(tin);
        CollectionAssert.AreEqual(a.Details(), b.Details());
        for (int i = 0; i < a.RiverReaches.Count; i++) CollectionAssert.AreEqual(a.RiverReaches[i].Samples, b.RiverReaches[i].Samples);
        for (int i = 0; i < a.Lakes.Count; i++) CollectionAssert.AreEqual(a.Lakes[i].OutletPath, b.Lakes[i].OutletPath);
        CollectionAssert.AreEqual(metadata, tin.Networks.Select(n => (n.SourceTriangle, n.Termination, n.JunctionNetwork, n.Length)));
        for (int i = 0; i < raw.Length; i++) CollectionAssert.AreEqual(raw[i], tin.Networks[i].Samples);
        CollectionAssert.AreEqual(heights, tin.HydrologyHeight); CollectionAssert.AreEqual(visible, tin.VisibleHeight); CollectionAssert.AreEqual(receiver, tin.TriangleReceiver);
        CollectionAssert.AreEqual(runoff, tin.AccumulatedRunoff); CollectionAssert.AreEqual(area, tin.DrainageArea);
        CollectionAssert.AreEqual(ocean, Enumerable.Range(0, tin.TriangleCount).Select(t => tin.IsOcean(tin.Centroid(t))));
        CollectionAssert.AreEqual(gradients, Enumerable.Range(0, tin.TriangleCount).Select(tin.DownhillGradient));
        Assert.That(tin.Diagnostics.Summary(), Is.EqualTo(diagnostics));
    }
}
