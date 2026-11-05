using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicOceanDrainagePlanTests
{
    private static GeodesicOceanDrainagePlan.Connection Edge(int id, int downstream, double runoff, double cost = 1) =>
        new GeodesicOceanDrainagePlan.Connection { Id = id, Downstream = downstream, LocalRunoff = runoff, Cost = cost };

    [Test] public void OnlyWitnessedOceanRootedComponentsAreSelectedDownstreamFirst()
    {
        var r = GeodesicOceanDrainagePlan.Select(new[] { Edge(3, 2, 5), Edge(2, 1, 2), Edge(1, -1, 1),
            Edge(4, 99, 100), Edge(5, 6, 100), Edge(6, 5, 100), Edge(7, 7, 100), Edge(8, -2, 100) }, 0, (_, __) => true);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, r.Ordered);
        Assert.That(r.Unrooted, Is.EqualTo(5));
        Assert.That(r.PotentialRunoff[1], Is.EqualTo(8));
    }

    [Test] public void HighRunoffShallowConnectionsWinConflictsWithoutRejectingBoth()
    {
        var r = GeodesicOceanDrainagePlan.Select(new[] { Edge(10, -1, 4, 1), Edge(20, -1, 12, 2), Edge(30, 10, 1) },
            0, (_, selected) => selected.Count == 0);
        CollectionAssert.AreEqual(new[] { 20 }, r.Ordered);
        Assert.That(r.Conflicts, Is.EqualTo(1));
        Assert.That(r.Ordered.Contains(30), Is.False, "A rejected downstream cut cannot support an upstream promise.");
    }

    [Test] public void SmallDownstreamConnectorCanUnlockLargeIndependentUpstreamCatchment()
    {
        var r = GeodesicOceanDrainagePlan.Select(new[] { Edge(1, -1, .1), Edge(2, 1, 6), Edge(3, 1, 4) }, 5, (_, __) => true);
        CollectionAssert.AreEqual(new[] { 1, 2 }, r.Ordered);
        Assert.That(r.PotentialRunoff[1], Is.EqualTo(10.1).Within(1e-12));
        Assert.That(r.PotentialRunoff[2], Is.EqualTo(6));
        Assert.That(r.BelowRunoff, Is.EqualTo(1));
    }

    [Test] public void SelectionUsesStableIdsForTiesAndDoesNotDependOnInputOrder()
    {
        var edges = new[] { Edge(9, -1, 4), Edge(2, -1, 4), Edge(7, 2, 0) };
        var a = GeodesicOceanDrainagePlan.Select(edges, 0, (_, __) => true);
        var b = GeodesicOceanDrainagePlan.Select(edges.Reverse(), 0, (_, __) => true);
        CollectionAssert.AreEqual(a.Ordered, b.Ordered); Assert.That(a.Ordered[0], Is.EqualTo(2));
    }

    [Test] public void PlannerRejectsInvalidCostsAndHasAnExplicitCandidateBudget()
    {
        Assert.Throws<ArgumentException>(() => GeodesicOceanDrainagePlan.Select(new[] { Edge(1, -1, 1, double.NaN) }, 0, (_, __) => true));
        Assert.Throws<ArgumentException>(() => GeodesicOceanDrainagePlan.Select(
            Enumerable.Range(0, GeodesicOceanDrainagePlan.MaximumCandidates + 1).Select(i => Edge(i, -1, 1)), 0, (_, __) => true));
    }

    [Test] public void TinyNoiseCatchmentsUseUniqueOriginalParcelsWithoutTerrainOrReceiverChanges()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => .00005f * (x * x + (y - 1) * (y - 1)), size: 8, boundaryOutflow: false);
        f.Tin.CaptureParcelDestinations = true; f.Tin.Accumulate(); f.Tin.ExtractNetworks(.01);
        var heights = f.Heights.ToArray(); var receivers = f.Tin.TriangleReceiver.ToArray(); var flow = f.Tin.AccumulatedRunoff.ToArray();
        var catchments = GeodesicOceanDrainagePlan.CaptureCatchments(f.Tin, GeodesicTinBasins.Build(f.Tin, 0, 0));
        Assert.That(catchments.Sum(c => c.TinyNoiseRunoff), Is.GreaterThan(0));
        var ids = catchments.SelectMany(c => c.Parcels).ToArray();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length));
        Assert.That(catchments.Sum(c => c.Runoff), Is.EqualTo(ids.Sum(f.Tin.ParcelRunoff)).Within(1e-12));
        Assert.That(catchments.Sum(c => c.Runoff), Is.LessThan(f.Tin.AccumulatedRunoff.Sum()));
        CollectionAssert.AreEqual(heights, f.Heights); CollectionAssert.AreEqual(receivers, f.Tin.TriangleReceiver);
        CollectionAssert.AreEqual(flow, f.Tin.AccumulatedRunoff);
    }

    [Test] public void OceanEvidenceRejectsOpenBoundaryAndIsolatedSubseaEndpoints()
    {
        var boundary = GeodesicTinHydrologyTests.Grid((x, y) => 3 - x, size: 4);
        Assert.That(GeodesicOceanDrainagePlan.TraceToOcean(boundary.Tin, boundary.At(0, 1)), Is.False);
        var isolated = GeodesicTinHydrologyTests.Grid((x, y) => -2 + x * x + (y - 1) * (y - 1), size: 4, boundaryOutflow: false);
        Assert.That(GeodesicOceanDrainagePlan.TraceToOcean(isolated.Tin, isolated.At(0, 1)), Is.False);
        var ocean = GeodesicTinHydrologyTests.Grid((x, y) => -x, size: 8, ocean: true, boundaryOutflow: false);
        Assert.That(GeodesicOceanDrainagePlan.TraceToOcean(ocean.Tin, ocean.At(-.5f, 1)), Is.True);
    }

    [Test] public void SphericalIncisionIsBoundedDeterministicAndVerifiedByTheActualTin()
    {
        // Small analytic geometry fixture, not planet generation or a seed benchmark.
        var g = IcosphereRenderGeometryCache.GetOrBuild(4); const float sea = 1.02f;
        var surface = g.UnitVertices.Select(d => d * (1.1f - .3f * d.x -
            (d.z > 0 ? .025f * (float)Math.Exp(-(d.x * d.x + d.y * d.y) / .003f) : 0f))).ToArray();
        var h = surface.Select(p => p.magnitude).ToArray();
        // Explicit component authority covers the shoreline facets in this synthetic TIN.
        // IsOcean still requires the actual sea-sphere intersection, not this mask alone.
        var mask = g.UnitVertices.Select(d => d.x > .2f).ToArray();
        var graph = GeodesicHydrologyTopology.Build(g);
        var tin = GeodesicTinHydrology.BuildAdaptive(g.UnitVertices, g.Triangles, h, mask, sea, 1, .00000025,
            (h.Max() - (double)h.Min()) * .001, 0, .0001, subdivision: 4, completedSurface: surface, captureParcelDestinations: true);
        var basins = GeodesicTinBasins.Build(tin, 0, double.MaxValue);
        var baseline = GeodesicDrainageDelivery.Analyze(tin, basins);
        var before = GeodesicGenerationFingerprint.Positions(surface);
        var originalMask = mask.ToArray();
        var a = GeodesicValleyCarving.Plan(tin, graph, mask, null, null, 1, sea, .04f, 0, .01f, basins, surface);
        var b = GeodesicValleyCarving.Plan(tin, graph, mask, null, null, 1, sea, .04f, 0, .01f, basins, surface);
        CollectionAssert.AreEqual(a.Heights, b.Heights);
        Assert.That(GeodesicGenerationFingerprint.Positions(surface), Is.EqualTo(before));
        Assert.That(a.Accepted, Is.GreaterThan(0), "The fixture must exercise an actual accepted connection. " + a.Summary() +
            " candidates=" + string.Join(";", a.Candidates.Select(c => c.BasinId + ":" + c.Rejection + ":target=" + c.DownstreamBasinId + ":profile=" + c.ProfileFailure + ":verification=" + c.VerificationFailure)));
        Assert.That(a.Candidates.Any(c => c.Accepted && c.RequiresRetrace), "Exercise a legitimate reroute rejected by the former old-polyline guard.");
        CollectionAssert.AreEqual(originalMask, mask);
        var after = surface.ToArray();
        for (int v = 0; v < h.Length; v++)
        {
            Assert.That(h[v] - a.Heights[v], Is.InRange(0f, .04f));
            if (mask[v]) Assert.That(a.Heights[v], Is.EqualTo(h[v]));
            if (a.Heights[v] < h[v]) after[v] = after[v].normalized * a.Heights[v];
        }
        var finalHeights = after.Select(p => p.magnitude).ToArray();
        var rebuilt = GeodesicTinHydrology.BuildAdaptive(g.UnitVertices, g.Triangles, finalHeights, mask, sea, 1,
            tin.Epsilon, (finalHeights.Max() - (double)finalHeights.Min()) * .001, 0, .0001, subdivision: 4,
            completedSurface: after, captureParcelDestinations: true);
        foreach (var c in a.Candidates.Where(c => c.Accepted)) Assert.That(GeodesicOceanDrainagePlan.TraceToOcean(rebuilt, c.SourceFloor));
        Assert.That(rebuilt.Diagnostics.VisibleMaterialUphillSegments, Is.Zero);
        var delivery = GeodesicDrainageDelivery.Analyze(rebuilt, GeodesicTinBasins.Build(rebuilt, 0, double.MaxValue));
        Assert.That(Math.Abs(delivery.Residual), Is.LessThanOrEqualTo(delivery.Tolerance));
        Assert.That(delivery.CycleNodes, Is.Zero); Assert.That(delivery.DuplicateMemberships, Is.Zero);
        Assert.That(delivery.TotalRunoff, Is.EqualTo(baseline.TotalRunoff).Within(1e-12));
        Assert.That(delivery.DirectOcean, Is.GreaterThan(baseline.DirectOcean));
        Console.WriteLine(FormattableString.Invariant($"ANALYTIC FIXTURE ONLY: runoff={delivery.TotalRunoff:R} directBefore={baseline.DirectOcean:R} directAfter={delivery.DirectOcean:R} inlandBefore={baseline.InlandClosed:R} inlandAfter={delivery.InlandClosed:R} unresolvedBefore={baseline.Unresolved:R} unresolvedAfter={delivery.Unresolved:R} residual={delivery.Residual:R} accepted={a.Accepted} verificationBuilds={a.VerificationBuilds}"));
    }

    [Test] public void ExistingLakeCascadeAndZeroLengthOutletConservationRemainIntact()
    {
        var delivery = new GeodesicDrainageDeliveryTests();
        delivery.TwoActualLakesDeliverTheirUnionThroughTheFinalOutlet();
        delivery.SharedSaddleLogicalTransferNeedsNoRibbonAndNeverAddsReciprocalEdge();
        delivery.RenderedLakeOutletReachesOceanEvenBelowRibbonThreshold();
        var a = new TinLakeFlowNode(1) { DownstreamLakeId = 2 }; var b = new TinLakeFlowNode(2) { DownstreamLakeId = 1 };
        string cycle = null;
        Assert.Throws<InvalidOperationException>(() => GeodesicTinLakeRouting.ResolveDischarge(new[] { a, b }, _ => 1, log => cycle = log));
        StringAssert.Contains("2-node-cycle", cycle);
    }

    [Test] public void UnconfirmedShorelineCannotAuthorizeAnOtherwiseValidCut()
    {
        // Same analytic incision, but exact wet-vertex authority leaves a sagged coastal
        // facet without retained evidence. OceanCoast alone must not authorize a cut.
        var g = IcosphereRenderGeometryCache.GetOrBuild(4); const float sea = 1.02f;
        var surface = g.UnitVertices.Select(d => d * (1.1f - .3f * d.x -
            (d.z > 0 ? .025f * (float)Math.Exp(-(d.x * d.x + d.y * d.y) / .003f) : 0f))).ToArray();
        var h = surface.Select(p => p.magnitude).ToArray(); var ocean = h.Select(x => x < sea).ToArray();
        var tin = GeodesicTinHydrology.BuildAdaptive(g.UnitVertices, g.Triangles, h, ocean, sea, 1, .00000025,
            (h.Max() - (double)h.Min()) * .001, 0, .0001, subdivision: 4, completedSurface: surface, captureParcelDestinations: true);
        var result = GeodesicValleyCarving.Plan(tin, GeodesicHydrologyTopology.Build(g), ocean, null, null, 1, sea,
            .04f, 0, .01f, GeodesicTinBasins.Build(tin, 0, double.MaxValue), surface);
        Assert.That(result.VerificationBuilds, Is.GreaterThan(0));
        Assert.That(result.Accepted, Is.Zero); CollectionAssert.AreEqual(h, result.Heights);
        Assert.That(result.Candidates.Any(c => c.Rejection == "proposed-no-ocean-connection" || c.Rejection == "existing-ocean-route-changed"));
    }

    [Test] public void FinalParcelEvidenceMatchesDeliveryAndDoesNotInventOceanGain()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => x * x + (y - 1) * (y - 1), size: 8, boundaryOutflow: false);
        f.Tin.CaptureParcelDestinations = true; f.Tin.Accumulate(); f.Tin.ExtractNetworks(.01);
        var basins = GeodesicTinBasins.Build(f.Tin, 0, 0);
        var initial = GeodesicOceanDrainagePlan.CaptureCatchments(f.Tin, basins);
        f.Tin.Accumulate(2); // Reporting must preserve the original catchment input.
        var delivery = GeodesicDrainageDelivery.Analyze(f.Tin, basins);
        Assert.That(Enumerable.Range(0, f.Tin.TriangleCount).Any(delivery.IsConfirmedOceanParcel), Is.False);
        StringAssert.Contains("fullyOceanConnected=0", GeodesicOceanDrainagePlan.FinalCatchmentSummary(initial, f.Tin, delivery, 0));
        StringAssert.Contains("initialCatchmentRunoff=4 finalComparableRunoff=8", GeodesicOceanDrainagePlan.FinalCatchmentSummary(initial, f.Tin, delivery, 0));
    }
}
