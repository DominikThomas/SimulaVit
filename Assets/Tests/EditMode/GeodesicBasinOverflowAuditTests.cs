using System;
using System.Linq;
using NUnit.Framework;

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

    private static GeodesicTinHydrology Compound(bool third = false)
    {
        float Profile(float x)
        {
            if (x <= -.75f) return 2 + 12 * (-.75f - x);
            if (third)
            {
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
