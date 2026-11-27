using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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

    [TestCase(true)]
    [TestCase(false)]
    public void DownhillSpillEdgeCanFailCompletedFacetGrade(bool oneUlpDrop)
    {
        const float rim = 8.2f;
        float downstream = oneUlpDrop
            ? BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(rim) - 1)
            : 8.198f;
        var directions = new[] { Vector3.right, new Vector3(1f, .1f, 0f).normalized, new Vector3(1f, 0f, .1f).normalized };
        var heights = new[] { rim, downstream, rim + .02f };
        var surface = directions.Select((direction, index) => direction * heights[index]).ToArray();
        var tin = new GeodesicTinHydrology(directions, new[] { 0, 1, 2 }, heights, heights,
            radius: 8d, epsilon: .000002d, visibleBumpTolerance: .0001d, completedSurface: surface);
        var spill = tin.BasinPoint(0, 0);
        var witness = tin.BasinPoint(0, 1);
        Assert.That(tin.OutletSlope(spill, witness), Is.GreaterThan(0d));
        Assert.That(tin.CompletedVisibleRadius(witness), Is.LessThan(tin.CompletedVisibleRadius(spill)));
        Assert.That(tin.CompletedVisibleRadius(witness) - tin.MinimumOutletSegmentRadius(spill, witness),
            Is.GreaterThan(tin.VisibleBumpTolerance + tin.Epsilon));

        var before = tin.Diagnostics.Summary();
        var failure = default(GeodesicTinHydrology.TraceFailure);
        bool Departure(TinPoint a, TinPoint b) => a.Equals(spill) && b.Equals(witness);
        var path = tin.TraceLakeOutlet(spill, Departure, out var termination, maximumSteps: 1);
        CollectionAssert.AreEqual(new[] { spill, witness }, path);
        Assert.That(tin.ValidateLakePath(path), Is.True);
        Assert.That(tin.ValidateCompletedVisibleGrade(path), Is.False);
        Assert.That(tin.VerifyExposedOutlet(path), Is.False);

        var physical = tin.TraceLakeOutlet(spill, Departure, out var physicalTermination, maximumSteps: 1,
            failure: value => failure = value, requireCompletedGrade: true);
        Assert.That(physicalTermination, Is.EqualTo(TinTermination.RoutingFailure));
        Assert.That(failure.Kind, Is.EqualTo(GeodesicTinHydrology.TraceFailureKind.CompletedVisibleGrade));
        CollectionAssert.AreEqual(new[] { spill }, physical);
#if UNITY_EDITOR
        int selections = 0;
        var replay = tin.TraceLakeOutlet(spill, Departure, out var replayTermination, maximumSteps: 1,
            observeSelection: (from, start, end, flat) =>
            {
                selections++;
                Assert.That(from, Is.EqualTo(spill));
                Assert.That(start, Is.EqualTo(spill));
                Assert.That(end, Is.EqualTo(witness));
                Assert.That(flat, Is.False);
            });
        Assert.That(selections, Is.EqualTo(1));
        Assert.That(replayTermination, Is.EqualTo(termination));
        CollectionAssert.AreEqual(path, replay);

        bool foundWitness = false;
        tin.VisitSpillDescentCandidates(spill, (a, b) =>
        {
            if (!a.Equals(spill) || !b.Equals(witness)) return;
            foundWitness = true;
            Assert.That(tin.PreviewSpillGrade(spill, a, b), Is.EqualTo(GeodesicTinHydrology.TraceFailureKind.None));
        });
        Assert.That(foundWitness, Is.True);
#endif
        Assert.That(tin.Diagnostics.Summary(), Is.EqualTo(before));
        Assert.That(tin.HasVisibleDisagreement, Is.False);
    }

    [TestCase(3, .14)]
    [TestCase(4, .07)]
    [TestCase(6, .018)]
    [TestCase(7, .009)]
    public void EqualRadiiHaveAnAnalyticMeshMinimumButNoLogicalRise(int subdivision, double angle)
    {
        const float radius = 8f;
        var directions = new[] { Vector3.right, new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f),
            new Vector3(1f, 0f, .1f).normalized };
        var heights = new[] { radius, radius, radius };
        var tin = new GeodesicTinHydrology(directions, new[] { 0, 1, 2 }, heights, heights, subdivisionLevel: subdivision);
        var a = tin.BasinPoint(0, 0); var b = tin.BasinPoint(0, 1);
        Assert.That(tin.MinimumOutletSegmentRadius(a, b), Is.EqualTo(radius * Math.Cos(angle * .5)).Within(1e-6d));
        Assert.That(tin.Validate(new[] { a, b }), Is.True);
        Assert.That(tin.ValidateCompletedVisibleGrade(new[] { a, b }), Is.False);
    }

    [Test]
    public void VisibleElevationObstacleStillStopsSphericalFilteredDescent()
    {
        var directions = new[] { Vector3.right, new Vector3(1f, .1f, 0f).normalized, new Vector3(1f, 0f, .1f).normalized };
        var hydro = new[] { 8f, 7.998f, 8.02f };
        var visible = new[] { 8f, 8.02f, 8.02f };
        var tin = new GeodesicTinHydrology(directions, new[] { 0, 1, 2 }, visible, hydro, visibleBumpTolerance: .0001d);
        var spill = tin.BasinPoint(0, 0); var side = tin.BasinPoint(0, 1);
        var failure = default(GeodesicTinHydrology.TraceFailure);
        var path = tin.TraceLakeOutlet(spill, (_, b) => b.Equals(side), out var termination, failure: value => failure = value);
        Assert.That(tin.OutletSlope(spill, side), Is.Positive);
        Assert.That(failure.Kind, Is.EqualTo(GeodesicTinHydrology.TraceFailureKind.VisibleGrade));
        Assert.That(termination, Is.EqualTo(TinTermination.RoutingFailure));
        CollectionAssert.AreEqual(new[] { spill }, path);
#if UNITY_EDITOR
        Assert.That(tin.DescribeOutletGradeChecks(failure), Does.Contain("hydrologyEndPass=True").And.Contain("visibleLinearPass=False"));
#endif
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FlatDepartureUsesTheWitnessOrRejectsTheTransition(bool allowWitness)
    {
        var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1f, -1f, 0f), new Vector3(2f, -1f, 0f) };
        var heights = new[] { 2f, 2f, 2f, 2f, 1f };
        var tin = new GeodesicTinHydrology(vertices, new[] { 0, 1, 2, 0, 3, 1, 1, 3, 4 }, heights, heights,
            new[] { false, false, false, false, true }, 1.5f, onSphere: false);
        var spill = tin.BasinPoint(0, 0);
        var unconstrained = tin.Trace(spill, out var unconstrainedEnd);
        Assert.That(unconstrained[1].Triangle, Is.EqualTo(1), "The shortest plateau exit starts in the other facet.");
        Assert.That(unconstrainedEnd, Is.EqualTo(TinTermination.OceanCoast));

        var failure = default(GeodesicTinHydrology.TraceFailure);
        var path = tin.TraceLakeOutlet(spill, (a, _) => allowWitness && a.Triangle == 0, out var end,
            failure: value => failure = value);
        if (allowWitness)
        {
            Assert.That(path[1], Is.EqualTo(new TinPoint(0, .5d, .5d, 0d)));
            Assert.That(end, Is.EqualTo(TinTermination.OceanCoast));
            Assert.That(tin.IsRetainedOceanBoundary(path.Last()), Is.True);
            Assert.That(tin.ValidateLakePath(path), Is.True);
        }
        else
        {
            Assert.That(end, Is.EqualTo(TinTermination.RoutingFailure));
            Assert.That(failure.Kind, Is.EqualTo(GeodesicTinHydrology.TraceFailureKind.DepartureRejected));
            CollectionAssert.AreEqual(new[] { spill }, path);
        }
    }

    [Test]
    public void OverflowWithoutAScalarDepartureStillThrows()
    {
        var f = Overflow();
        var visible = f.Heights.Select((h, i) => f.Vertices[i].x > 0f && h > 0f ? h + 20f : h).ToArray();
        var tin = new GeodesicTinHydrology(f.Vertices, f.Triangles, visible, f.Heights,
            f.Heights.Select(h => h <= 0f).ToArray(), onSphere: false);
        var basins = GeodesicTinBasins.Build(tin, 0, 0);
        var geometry = GeodesicTinLakeGeometry.Build(tin, basins);
        Assert.That(geometry.Bodies.Count, Is.EqualTo(1));
        LogAssert.Expect(LogType.Error, new Regex("unresolved overflowing outlet"));
        var error = Assert.Throws<InvalidOperationException>(() => GeodesicTinLakeRouting.Build(tin, basins, geometry));
        Assert.That(error.Message, Does.Contain("validSpillWitness=True").And.Contain("downstreamSample=[none]"));
    }

    [Test]
    public void MeshGradeRejectionKeepsProvenLakeDeliveryAndIndependentRunoff()
    {
        var f = Overflow();
        var directions = f.Vertices.Select(v => new Vector3(1f, v.x, v.y - 1f).normalized).ToArray();
        var heights = f.Heights.Select(h => 8f + .002f * h).ToArray();
        var tin = new GeodesicTinHydrology(directions, f.Triangles, heights, heights,
            f.Heights.Select(h => h <= 0f).ToArray(), 8f, radius: 8d, visibleBumpTolerance: .0001d);
        tin.CaptureParcelDestinations = true;
        tin.Accumulate();
        var basins = GeodesicTinBasins.Build(tin, 0, 0);
        var routing = GeodesicTinLakeRouting.Build(tin, basins, GeodesicTinLakeGeometry.Build(tin, basins));
        var lake = routing.Lakes.Single();
        Assert.That(lake.OutletTermination, Is.EqualTo(TinTermination.OceanCoast));
        Assert.That(tin.IsRetainedOceanBoundary(lake.OutletPath.Last()), Is.True);
        Assert.That(tin.ValidateCompletedVisibleGrade(lake.OutletPath), Is.False);
        var delivery = GeodesicDrainageDelivery.Analyze(tin, basins, routing.Lakes, routing.RiverReaches);
        Assert.That(lake.Discharge, Is.Positive);
        Assert.That(delivery.ViaLakesOcean, Is.EqualTo(lake.Discharge).Within(delivery.Tolerance));
        Assert.That(Math.Abs(delivery.Residual), Is.LessThanOrEqualTo(delivery.Tolerance));
        Assert.That(delivery.DuplicateMemberships + routing.FlowConservationErrors, Is.Zero);
    }

    [Test]
    public void RoundedSphericalShelfKeepsIndependentRunoffWithoutCreatingLakeOwners()
    {
        var tin = SphericalShelf(out _);
        tin.CaptureParcelDestinations = true;
        tin.Accumulate();
        var originalDestinations = tin.ParcelDestinations.ToArray();
        var basins = GeodesicTinBasins.Build(tin, 0, 0);
        var rounded = basins.Basins.Where(b => b.NumericalShelf).Select(b => b.Id).ToArray();
        Assert.That(rounded.Length, Is.GreaterThan(1));
        CollectionAssert.AreEqual(rounded, GeodesicTinBasins.Build(tin, 0, 0).Basins
            .Where(b => b.NumericalShelf).Select(b => b.Id));
        var originalDelivery = GeodesicDrainageDelivery.Analyze(tin, basins);
        var geometry = GeodesicTinLakeGeometry.Build(tin, basins);
        Assert.That(geometry.Bodies, Is.Empty);
        var routing = GeodesicTinLakeRouting.Build(tin, basins, geometry);
        var delivery = GeodesicDrainageDelivery.Analyze(tin, basins, routing.Lakes, routing.RiverReaches);
        CollectionAssert.AreEqual(originalDestinations, tin.ParcelDestinations);
        Assert.That(delivery.IndependentParcels, Is.EqualTo(originalDelivery.IndependentParcels));
        Assert.That(delivery.TotalRunoff, Is.EqualTo(originalDelivery.TotalRunoff));
        Assert.That(delivery.DirectOcean, Is.EqualTo(originalDelivery.DirectOcean));
        Assert.That(delivery.ViaLakesOcean, Is.Zero);
        Assert.That(delivery.InlandClosed, Is.EqualTo(originalDelivery.InlandClosed));
        Assert.That(delivery.Unresolved, Is.EqualTo(originalDelivery.Unresolved));
        Assert.That(Math.Abs(delivery.Residual), Is.LessThanOrEqualTo(delivery.Tolerance));
    }

    [Test]
    public void DistinctShallowSphericalDepressionStillQualifiesOnTheSameShelf()
    {
        var tin = SphericalShelf(out int depression, withDepression: true);
        var basins = GeodesicTinBasins.Build(tin, 0, 0);
        var basin = basins.Basins.Single(b => b.Id == depression);
        Assert.That(basin.HasEscape, Is.True);
        Assert.That(basin.MaximumDepth, Is.InRange(.00001d, .00003d));
        Assert.That(basin.NumericalShelf, Is.False);
        Assert.That(GeodesicTinLakeGeometry.Build(tin, basins).Bodies.Any(b => b.BasinId == depression), Is.True);
    }

    [Test]
    public void AdjacentAuthoredFloatRadiiAreNotASharedShelf()
    {
        const float floor = 8f;
        float rim = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(floor) + 1);
        var directions = new[] { Vector3.right, Vector3.up, Vector3.forward };
        var surface = new[] { directions[0] * floor, directions[1] * rim, directions[2] * rim };
        var heights = surface.Select(p => p.magnitude).ToArray();
        var tin = new GeodesicTinHydrology(directions, new[] { 0, 1, 2 }, heights, heights, completedSurface: surface);
        Assert.That(heights[1] - heights[0], Is.InRange(float.Epsilon, (float)tin.Epsilon));
        Assert.That(tin.CommonRoundedShelfRadius(0, 1), Is.Zero);
    }

    private static GeodesicTinHydrology SphericalShelf(out int depression, bool withDepression = false)
    {
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(4);
        int north = Array.FindIndex(geometry.UnitVertices, d => d.z > .9999f);
        Assert.That(north, Is.GreaterThanOrEqualTo(0));
        depression = north;
        bool[] retained = geometry.UnitVertices.Select(d => d.z < -.3f).ToArray();
        var surface = geometry.UnitVertices.Select((d, i) =>
            d * (retained[i] ? 7.9f : withDepression && i == north ? 8.19998f : 8.2f)).ToArray();
        var heights = surface.Select(p => p.magnitude).ToArray();
        return new GeodesicTinHydrology(geometry.UnitVertices, geometry.Triangles, heights, heights,
            retained, 8f, radius: 8d, visibleBumpTolerance: .0003d, completedSurface: surface);
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
        Assert.Throws<InvalidOperationException>(() => GeodesicTinLakeRouting.ResolveDischarge(new[] { a, b }, _ => 1, _ => { }));
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
