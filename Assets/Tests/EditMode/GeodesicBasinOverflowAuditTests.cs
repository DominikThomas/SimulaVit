using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicBasinOverflowAuditTests
{
    private static GeodesicTinHydrology Prepare(Func<float, float, float> heights, bool ocean = true, bool boundary = false)
    {
        var tin = GeodesicTinHydrologyTests.Grid(heights, size: 32, ocean: ocean, boundaryOutflow: boundary).Tin;
        tin.CaptureParcelDestinations = true;
        tin.Accumulate();
        tin.ExtractNetworks(.01);
        return tin;
    }

    private static GeodesicTinHydrology OceanBowl() => Prepare((x, y) =>
        (x <= -.5f ? 1 + 8 * (-.5f - x) : x <= 0 ? 1 + 4 * (x + .5f) : 3 - 4 * x) + 8 * (y - 1) * (y - 1));

    private static GeodesicTinHydrology Compound(bool third = false, bool nested = false)
    {
        float Profile(float x)
        {
            if (x <= -.75f) return (nested ? 2.5f : 2) + 12 * (-.75f - x);
            if (third)
            {
                if (nested)
                {
                    if (x <= -.625f) return 2.5f + 4 * (x + .75f);
                    if (x <= -.5f) return 3 - 8 * (x + .625f);
                    if (x <= -.375f) return 2 + 10 * (x + .5f);
                    if (x <= -.25f) return 3.25f - 18 * (x + .375f);
                }
                if (x <= -.625f) return 2 + 10 * (x + .75f);
                if (x <= -.5f) return 3.25f - 6 * (x + .625f);
                if (x <= -.375f) return 2.5f + 4 * (x + .5f);
                if (x <= -.25f) return 3 - 16 * (x + .375f);
            }
            else
            {
                if (x <= -.5f) return 2 + 4 * (x + .75f);
                if (x <= -.25f) return 3 - 8 * (x + .5f);
            }
            return x <= 0 ? 1 + 12 * (x + .25f) : 4 - 6 * x;
        }
        return Prepare((x, y) => Profile(x) + 8 * (y - 1) * (y - 1));
    }

    private static TinBasinDescriptor Copy(TinBasinDescriptor b, int? parent = null,
        int? witnessFacet = null, TinBasinExternalSource? external = null) =>
        new TinBasinDescriptor(b.Id, b.Floor, b.FloorElevation, b.SpillElevation, b.Spill,
            witnessFacet ?? b.DownstreamTriangle, b.DownstreamVertex, parent ?? b.ParentBasinId,
            b.MemberTriangles.ToArray(), b.Area, b.TouchesRetainedOcean, b.SubseaUnclassified,
            0, 0, external ?? b.ExternalSource);

    private static GeodesicTinHydrology Cascade()
    {
        // Three separate spill pools. The lower two have already joined the
        // ocean component when the sweep records the higher outlet saddles.
        var profile = new[] { 9f, 1f, 7f, 2f, 5f, 3f, 4f, -1f, -2f };
        return Prepare((x, y) =>
        {
            float i = (x + 1) * 4;
            int left = Math.Min((int)i, profile.Length - 2);
            return Mathf.Lerp(profile[left], profile[left + 1], i - left) + 16 * (y - 1) * (y - 1);
        });
    }

    [Test] public void SingleBasinHasWitnessedOceanPotentialAndPreservesActualDelivery()
    {
        var tin = OceanBowl();
        var topology = GeodesicTinBasins.Build(tin, 0, 4);
        var basin = topology.Basins.Single();
        var delivery = GeodesicDrainageDelivery.Analyze(tin, topology);
        var actual = delivery.Summary();
        var paths = tin.Networks.Select(n => n.Samples).ToArray();
        var receivers = (int[])tin.TriangleReceiver.Clone();
        var parcels = (TinParcelDestination[])tin.ParcelDestinations.Clone();
        var runoff = (double[])tin.AccumulatedRunoff.Clone();
        var diagnostics = tin.Diagnostics.Summary();
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, topology.Basins, delivery);
        Assert.That(basin.QualifiesForVisibleLake, Is.False);
        Assert.That(basin.ExternalSource, Is.EqualTo(TinBasinExternalSource.RetainedOcean));
        Assert.That(audit.Basins[basin.Id].GeometricallySupported, Is.True, audit.Summary());
        Assert.That(delivery.InlandClosed, Is.GreaterThan(0));
        Assert.That(audit.GeometricClosed, Is.EqualTo(delivery.InlandClosed).Within(1e-9));
        StringAssert.Contains("accountingPass=True", audit.Summary());
        Assert.That(delivery.Summary(), Is.EqualTo(actual));
        Assert.That(tin.Diagnostics.Summary(), Is.EqualTo(diagnostics));
        CollectionAssert.AreEqual(paths, tin.Networks.Select(n => n.Samples));
        CollectionAssert.AreEqual(receivers, tin.TriangleReceiver);
        CollectionAssert.AreEqual(parcels, tin.ParcelDestinations);
        CollectionAssert.AreEqual(runoff, tin.AccumulatedRunoff);
    }

    [Test] public void NestedBasinUsesTheEnclosingSpillAsItsCommonHead()
    {
        var tin = Compound();
        var topology = GeodesicTinBasins.Build(tin);
        var child = topology.Basins.Single(b => b.ParentBasinId >= 0);
        var delivery = GeodesicDrainageDelivery.Analyze(tin, topology);
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, topology.Basins, delivery);
        var evidence = audit.Basins[child.Id];
        Assert.That(child.SpillElevation, Is.EqualTo(3));
        Assert.That(evidence.ControllingSpill, Is.EqualTo(4));
        Assert.That(evidence.SpillSteps, Is.EqualTo(2));
        Assert.That(evidence.GeometricallySupported, Is.True, audit.Summary());
        Assert.That(audit.Opportunities.Any(line => line.Contains("higherSharedLevel=True")), Is.True);
    }

    [Test] public void SiblingBasinsShareOneDownstreamWetRegionAndCountRunoffOnce()
    {
        var tin = Compound(third: true);
        var topology = GeodesicTinBasins.Build(tin);
        var root = topology.Basins.Single(b => b.ParentBasinId < 0);
        Assert.That(topology.Basins.Count(b => b.ParentBasinId == root.Id), Is.EqualTo(2));
        var delivery = GeodesicDrainageDelivery.Analyze(tin, topology);
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, topology.Basins, delivery);
        Assert.That(audit.Basins[root.Id].CompoundBasinCount, Is.EqualTo(3));
        Assert.That(audit.Basins.Values.All(b => b.GeometricallySupported), Is.True, audit.Summary());
        Assert.That(audit.StructuralClosed, Is.EqualTo(delivery.InlandClosed).Within(1e-9));
        Assert.That(audit.GeometricClosed, Is.EqualTo(delivery.InlandClosed).Within(1e-9));
        Assert.That(audit.GeometricClosed + delivery.DirectOcean + delivery.Unresolved, Is.EqualTo(delivery.TotalRunoff).Within(1e-9));
    }

    [Test] public void ThreeSuccessiveDryDepressionsHaveSeparateDescendingSpills()
    {
        var tin = Cascade();
        var topology = GeodesicTinBasins.Build(tin);
        var delivery = GeodesicDrainageDelivery.Analyze(tin, topology);
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, topology.Basins, delivery);
        var route = audit.Basins.Values.OrderByDescending(b => b.Basin.SpillElevation).ToArray();
        Assert.That(route.Length, Is.EqualTo(3));
        Assert.That(route[0].Receiver, Is.SameAs(route[1]), audit.Summary());
        Assert.That(route[1].Receiver, Is.SameAs(route[2]), audit.Summary());
        Assert.That(route[2].DirectOcean, Is.True, audit.Summary());
        Assert.That(route[0].GeometricallySupported, Is.True, audit.Summary());
        Assert.That(route[0].RouteBasinCount, Is.EqualTo(3));
        Assert.That(route[0].RouteArea, Is.GreaterThan(route[1].RouteArea));
        Assert.That(route[0].RouteDepth, Is.EqualTo(6).Within(1e-9));
        StringAssert.Contains("previousClosedOutletDescriptors=2 resolvedClosedOutletDescriptors=2", audit.Summary());
        Assert.That(audit.Runoff(GeodesicBasinOverflowAudit.Outcome.VerifiedBasinContinuation), Is.GreaterThan(0));
        Assert.That(audit.Runoff(GeodesicBasinOverflowAudit.Outcome.VerifiedOceanOutlet), Is.GreaterThan(0));
        Assert.That(Enum.GetValues(typeof(GeodesicBasinOverflowAudit.Outcome)).Cast<GeodesicBasinOverflowAudit.Outcome>()
            .Sum(audit.Runoff), Is.EqualTo(delivery.TotalRunoff).Within(1e-9));
        StringAssert.Contains("accountingPass=True", audit.Summary());
    }

    [Test] public void ThreeNestedBasinsUseOneMeasuredWaterSurface()
    {
        var tin = Compound(third: true, nested: true);
        var topology = GeodesicTinBasins.Build(tin);
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, topology.Basins, GeodesicDrainageDelivery.Analyze(tin, topology));
        var child = audit.Basins.Values.Single(b => b.SpillSteps == 3);
        Assert.That(child.Receiver.Receiver, Is.SameAs(child.OceanRoot));
        Assert.That(child.GeometricallySupported, Is.True, audit.Summary());
        Assert.That(child.ControllingSpill, Is.EqualTo(4));
        Assert.That(child.RouteBasinCount, Is.EqualTo(3));
        Assert.That(child.RouteArea, Is.EqualTo(child.OceanRoot.RouteArea));
    }

    [Test] public void FilledPoolsCanHandOffWithoutDuplicatingRenderedLakeOwnership()
    {
        var tin = Cascade();
        var topology = GeodesicTinBasins.Build(tin, 0, 0);
        var geometry = GeodesicTinLakeGeometry.Build(tin, topology);
        var routing = GeodesicTinLakeRouting.Build(tin, topology, geometry);
        var delivery = GeodesicDrainageDelivery.Analyze(tin, topology, routing.Lakes, routing.RiverReaches);
        var lakeOwners = routing.Lakes.SelectMany(l => l.DirectParcels).OrderBy(id => id).ToArray();
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, topology.Basins, delivery, geometry);
        var source = audit.Basins.Values.OrderByDescending(b => b.Basin.SpillElevation).First();
        Assert.That(geometry.Bodies.Count, Is.EqualTo(3));
        Assert.That(source.Receiver.RenderedLakeOverlap, Is.True);
        Assert.That(source.GeometricallySupported, Is.True, audit.Summary());
        Assert.That(lakeOwners.Distinct().Count(), Is.EqualTo(lakeOwners.Length));
        CollectionAssert.AreEqual(lakeOwners, routing.Lakes.SelectMany(l => l.DirectParcels).OrderBy(id => id));
        Assert.That(audit.Runoff(GeodesicBasinOverflowAudit.Outcome.ConfirmedOcean),
            Is.EqualTo(delivery.DirectOcean + delivery.ViaLakesOcean));
    }

    [Test] public void ReceivingHeadAboveIndependentSourceSpillCannotCertifyBackwater()
    {
        var tin = Cascade();
        var topology = GeodesicTinBasins.Build(tin);
        var order = topology.Basins.OrderByDescending(b => b.SpillElevation).ToArray();
        var receiver = order[1];
        // Inject an incompatible claimed level while leaving the source saddle,
        // endpoint and receiving floor physically intact.
        var higherReceiver = new TinBasinDescriptor(receiver.Id, receiver.Floor, receiver.FloorElevation,
            8, receiver.Spill, receiver.DownstreamTriangle, receiver.DownstreamVertex, -1, receiver.MemberTriangles.ToArray(),
            receiver.Area, false, false, 0, 0, TinBasinExternalSource.RetainedOcean);
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, new[] { order[0], higherReceiver, order[2] },
            GeodesicDrainageDelivery.Analyze(tin, topology));
        Assert.That(audit.Basins[order[0].Id].GeometryFailure, Is.EqualTo("InvalidReceivingHeadOrBackwater"), audit.Summary());
        Assert.That(audit.Runoff(GeodesicBasinOverflowAudit.Outcome.InvalidHeadOrBackwater), Is.GreaterThan(0));
    }

    [Test] public void MissingReceivingOutletBlocksTheEntireUpstreamCascade()
    {
        var tin = Cascade();
        var topology = GeodesicTinBasins.Build(tin);
        var order = topology.Basins.OrderByDescending(b => b.SpillElevation).ToArray();
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, new[] { order[0], order[1], Copy(order[2], witnessFacet: -1) },
            GeodesicDrainageDelivery.Analyze(tin, topology));
        Assert.That(audit.Basins[order[0].Id].GeometryBlockBasin, Is.EqualTo(order[2].Id));
        Assert.That(audit.Runoff(GeodesicBasinOverflowAudit.Outcome.VerifiedBasinContinuation), Is.Zero);
        Assert.That(audit.Runoff(GeodesicBasinOverflowAudit.Outcome.MissingDownstreamEvidence), Is.GreaterThan(0));
    }

    [Test] public void FacetClippingDistinguishesExcludedWaterFromAnExcludedDryCorner()
    {
        var grid = GeodesicGridTopology.Build(1);
        const int cell = 0;
        int neighbor = grid.Neighbors6[cell * 6];
        var a = grid.CellDirections[cell];
        var b = grid.CellDirections[neighbor];
        var c = (b + Vector3.Cross(a, b) * .1f).normalized;
        var classified = new GeodesicBasinOverflowAudit.ClassifiedCells(grid, id => id == cell, _ => cell);
        GeodesicBasinOverflowAudit.Overlap Intersection(float corner, float other)
        {
            var heights = new[] { corner, other, other };
            var tin = new GeodesicTinHydrology(new[] { a, b, c }, new[] { 0, 1, 2 }, heights, heights, seaLevelRadius: 1);
            var wet = GeodesicTinLakeGeometry.ClipTriangle(tin, 0, 3);
            return classified.InFacet(tin, 0, wet);
        }
        Assert.That(Intersection(.8f, 1.2f), Is.EqualTo(GeodesicBasinOverflowAudit.Overlap.Exact));
        Assert.That(Intersection(2f, .8f), Is.EqualTo(GeodesicBasinOverflowAudit.Overlap.None));
    }

    [Test] public void ExcludedWaterAlongAnOutletDistinguishesSubmersionFromSeaLevelTangency()
    {
        var grid = GeodesicGridTopology.Build(1);
        const int cell = 0;
        var directions = new[] { grid.CellDirections[cell], grid.CellDirections[grid.Neighbors6[0]], grid.CellDirections[grid.Neighbors6[1]] };
        var heights = new[] { 1f, .8f, 2f };
        var tin = new GeodesicTinHydrology(directions, new[] { 0, 1, 2 }, heights, heights, seaLevelRadius: 1);
        var classified = new GeodesicBasinOverflowAudit.ClassifiedCells(grid, id => id == cell, _ => cell);
        var start = new TinPoint(0, 1, 0, 0);
        Assert.That(classified.OnSegment(tin, start, start), Is.EqualTo(GeodesicBasinOverflowAudit.Overlap.BoundaryUncertain));
        Assert.That(classified.OnSegment(tin, start, new TinPoint(0, .75, .25, 0)),
            Is.EqualTo(GeodesicBasinOverflowAudit.Overlap.Exact));
    }

    [Test] public void RetainedOceanInsideTheSourceFootprintPrecedesItsRecordedSaddle()
    {
        var fixture = GeodesicTinHydrologyTests.Grid((x, y) =>
            (x <= -.5f ? 1 + 8 * (-.5f - x) : x <= 0 ? 1 + 4 * (x + .5f) : 3 - 4 * x) + 8 * (y - 1) * (y - 1),
            size: 32);
        var ocean = fixture.Vertices.Select(p => p.x >= .75f || (p.x == -.5f && p.y == 1f)).ToArray();
        var tin = new GeodesicTinHydrology(fixture.Vertices, fixture.Triangles, fixture.Heights, fixture.Heights,
            ocean, 1.125f, onSphere: false) { CaptureParcelDestinations = true };
        tin.Accumulate();
        // Use the original saddle record, as if the final terrain exposed an
        // earlier ocean contact that the record did not describe.
        var topology = GeodesicTinBasins.Build(OceanBowl());
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, topology.Basins, GeodesicDrainageDelivery.Analyze(tin, topology));
        Assert.That(audit.Basins.Values.Single().GeometryFailure, Is.EqualTo("RetainedOceanBeforeSaddle"), audit.Summary());
    }

    [Test] public void MissingParentRetainsClosedInputInTheUnverifiedBudget()
    {
        var tin = OceanBowl(); var topology = GeodesicTinBasins.Build(tin);
        var basin = topology.Basins.Single();
        var delivery = GeodesicDrainageDelivery.Analyze(tin, topology);
        var audit = GeodesicBasinOverflowAudit.Analyze(tin,
            new[] { Copy(basin, parent: int.MaxValue, external: TinBasinExternalSource.None) }, delivery);
        Assert.That(audit.Basins[basin.Id].AncestryFailure, Is.EqualTo("MissingParent"));
        Assert.That(audit.MissingAncestryClosed, Is.EqualTo(delivery.InlandClosed).Within(1e-9));
        Assert.That(audit.StructuralClosed, Is.Zero);
        StringAssert.Contains("missingReferences=1", audit.Summary());
    }

    [Test] public void ParentCycleIsReportedEvenWhenItAlsoViolatesElderOrder()
    {
        var tin = Compound(); var topology = GeodesicTinBasins.Build(tin);
        var child = topology.Basins.Single(b => b.ParentBasinId >= 0);
        var root = topology.Basins.Single(b => b.ParentBasinId < 0);
        var audit = GeodesicBasinOverflowAudit.Analyze(tin,
            new[] { Copy(root, parent: child.Id, external: TinBasinExternalSource.None), child },
            GeodesicDrainageDelivery.Analyze(tin, topology));
        Assert.That(audit.Basins.Values.All(b => b.AncestryFailure == "ParentCycle"));
        StringAssert.Contains("cycles=1 cycleNodes=2", audit.Summary());
    }

    [Test] public void OpenBoundaryRootAndTrueTerminalRemainDistinctFromRetainedOcean()
    {
        foreach (bool open in new[] { true, false })
        {
            var tin = Prepare((x, y) => x * x + (y - 1) * (y - 1), ocean: false, boundary: open);
            var topology = GeodesicTinBasins.Build(tin); var basin = topology.Basins.Single();
            var audit = GeodesicBasinOverflowAudit.Analyze(tin, topology.Basins, GeodesicDrainageDelivery.Analyze(tin, topology));
            Assert.That(audit.Basins[basin.Id].AncestryFailure, Is.EqualTo(open ? "OpenBoundaryRoot" : "TerminalBasin"));
            Assert.That(audit.StructuralClosed, Is.Zero);
        }
    }

    [Test] public void MissingSaddleWitnessPreservesAncestryButBlocksGeometricCredit()
    {
        var tin = OceanBowl(); var topology = GeodesicTinBasins.Build(tin);
        var basin = topology.Basins.Single(); var delivery = GeodesicDrainageDelivery.Analyze(tin, topology);
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, new[] { Copy(basin, witnessFacet: -1) }, delivery);
        Assert.That(audit.Basins[basin.Id].StructuralOcean, Is.True);
        Assert.That(audit.Basins[basin.Id].GeometryFailure, Is.EqualTo("MissingOrInvalidDownstreamWitness"));
        Assert.That(audit.UnverifiedGeometryClosed, Is.EqualTo(delivery.InlandClosed).Within(1e-9));
        Assert.That(audit.GeometricClosed, Is.Zero);
    }

    [Test] public void ExistingRenderedLakeDeliveryIsNeverCreditedAgainAsPotential()
    {
        var tin = OceanBowl(); var topology = GeodesicTinBasins.Build(tin, 0, 0);
        var geometry = GeodesicTinLakeGeometry.Build(tin, topology);
        var routing = GeodesicTinLakeRouting.Build(tin, topology, geometry);
        var delivery = GeodesicDrainageDelivery.Analyze(tin, topology, routing.Lakes, routing.RiverReaches);
        var actual = delivery.Summary();
        var parcelOwners = routing.Lakes.Single().DirectParcels.OrderBy(p => p).ToArray();
        Assert.That(delivery.ViaLakesOcean, Is.GreaterThan(0));
        var audit = GeodesicBasinOverflowAudit.Analyze(tin, topology.Basins, delivery, geometry);
        Assert.That(audit.Basins.Values.Single().RenderedLakeOverlap, Is.True);
        Assert.That(audit.StructuralClosed, Is.LessThanOrEqualTo(delivery.InlandClosed + 1e-9));
        Assert.That(audit.GeometricClosed + delivery.DirectOcean + delivery.ViaLakesOcean + delivery.Unresolved,
            Is.LessThanOrEqualTo(delivery.TotalRunoff + 1e-9));
        Assert.That(delivery.Summary(), Is.EqualTo(actual));
        CollectionAssert.AreEqual(parcelOwners, routing.Lakes.Single().DirectParcels.OrderBy(p => p));
        StringAssert.Contains("accountingPass=True", audit.Summary());
    }
}
