using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicHydrologySnapshotTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void TinRestoreKeepsSelectedInputIndependentRunoffAndExactNetworkSamples(bool separateInput)
    {
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(1);
        var heights = geometry.UnitVertices.Select(v => 8f + v.y * .25f).ToArray();
        var positions = geometry.UnitVertices.Select((v, i) => v * heights[i]).ToArray();
        var ocean = heights.Select(h => h < 8).ToArray();
        var selected = separateInput ? heights.Select(h => 8f + (h - 8f) * .75f).ToArray() : heights;
        var tin = GeodesicTinHydrology.BuildAdaptive(geometry.UnitVertices, geometry.Triangles, heights, ocean,
            8, 8, .000002, 0, 0, .1, subdivision: 1, completedSurface: positions,
            hydrologyTerrainHeights: selected, captureParcelDestinations: true);
        var write = tin.CaptureSnapshot(Array.Empty<(int, TinPoint[])>(), selected);
        var snapshot = RoundTrip(write, r =>
            GeodesicTinHydrology.RestoreSnapshot(r, geometry, positions, heights, ocean, 8, 8, .000002, 0, separateInput));
        var restored = snapshot.Tin;

        Assert.That(snapshot.SelectedHeights, Is.EqualTo(selected));
        Assert.That(restored.HydrologyHeight, Is.EqualTo(tin.HydrologyHeight));
        Assert.That(restored.TriangleReceiver, Is.EqualTo(tin.TriangleReceiver));
        Assert.That(restored.AccumulatedRunoff, Is.EqualTo(tin.AccumulatedRunoff));
        Assert.That(restored.DrainageArea, Is.EqualTo(tin.DrainageArea));
        Assert.That(GeodesicGenerationFingerprint.Runoff(restored), Is.EqualTo(GeodesicGenerationFingerprint.Runoff(tin)));
        Assert.That(restored.ParcelDestinations, Is.EqualTo(tin.ParcelDestinations));
        Assert.That(restored.Networks, Is.Not.Empty);
        for (int i = 0; i < tin.Networks.Count; i++)
        {
            Assert.That(restored.Networks[i], Is.Not.SameAs(tin.Networks[i]));
            Assert.That(restored.Networks[i].Samples, Is.EqualTo(tin.Networks[i].Samples));
            Assert.That(restored.Networks[i].JunctionNetwork, Is.EqualTo(tin.Networks[i].JunctionNetwork));
        }
        restored.AccumulatedRunoff[0] = -1;
        Assert.That(tin.AccumulatedRunoff[0], Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void PhysicalKeyUsesSeedTopologyAndExclusionIndependentlyOfLakePresentation()
    {
        var root = new GameObject("Physical key test");
        root.SetActive(false);
        try
        {
            var planet = root.AddComponent<PlanetGenerator>();
            planet.ApplyStartupSeed(123456, false);
            planet.excludeSmallDisconnectedSeas = true;
            planet.minimumOceanComponentAreaFraction = .01f;
            byte[] key = planet.PhysicalKey(6, 7);
            planet.minimumLakeBasinAreaFraction = .000225f;
            planet.minimumLakeDepth = .01f;
            Assert.That(planet.PhysicalKey(6, 7), Is.EqualTo(key));
            Assert.That(planet.PhysicalKey(6, 6), Is.Not.EqualTo(key));
            planet.minimumOceanComponentAreaFraction = .02f;
            Assert.That(planet.PhysicalKey(6, 7), Is.Not.EqualTo(key));
            planet.minimumOceanComponentAreaFraction = .01f;
            planet.ApplyStartupSeed(123457, false);
            Assert.That(planet.PhysicalKey(6, 7), Is.Not.EqualTo(key));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void LakeAndDeliveryRestoreBindFreshBasinsAndPreserveParcelContributions()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) =>
            (x <= -.5f ? 1 + 8 * (-.5f - x) : x <= 0 ? 1 + 4 * (x + .5f) : 3 - 4 * x)
            + 8 * (y - 1) * (y - 1), size: 16, ocean: true, boundaryOutflow: false);
        var tin = f.Tin;
        tin.CaptureParcelDestinations = true;
        tin.Accumulate();
        tin.ExtractNetworks(.01);
        var basins = GeodesicTinBasins.Build(tin, 0, 0);
        var geometry = GeodesicTinLakeGeometry.Build(tin, basins);
        var routing = GeodesicTinLakeRouting.Build(tin, basins, geometry);
        var delivery = GeodesicDrainageDelivery.Analyze(tin, basins, routing.Lakes, routing.RiverReaches);

        var restoredBasins = RoundTrip(basins.WriteSnapshot, r => GeodesicTinBasins.RestoreSnapshot(r, tin));
        var restoredGeometry = RoundTrip(geometry.WriteSnapshot,
            r => GeodesicTinLakeGeometry.RestoreSnapshot(r, tin, restoredBasins));
        var restoredRouting = RoundTrip(routing.WriteSnapshot,
            r => GeodesicTinLakeRouting.RestoreSnapshot(r, tin, restoredBasins, restoredGeometry));
        var restoredDelivery = RoundTrip(delivery.WriteSnapshot,
            r => GeodesicDrainageDelivery.RestoreSnapshot(r, tin, restoredBasins, 0, restoredRouting.Lakes.Count));

        var node = routing.Lakes.Single();
        var restoredNode = restoredRouting.Lakes.Single();
        Assert.That(restoredNode.Basin, Is.Not.SameAs(node.Basin));
        Assert.That(restoredNode.Basin, Is.SameAs(restoredBasins.Basins.Single(b => b.Id == node.BasinId)));
        Assert.That(restoredNode.DirectParcels, Is.EqualTo(node.DirectParcels));
        Assert.That(restoredNode.ContributingParcels, Is.EqualTo(node.ContributingParcels));
        Assert.That(restoredNode.OutletPath, Is.EqualTo(node.OutletPath));
        Assert.That(restoredNode.Discharge, Is.EqualTo(node.Discharge));
        Assert.That(restoredGeometry.TerrainPoints, Is.EqualTo(geometry.TerrainPoints));
        Assert.That(restoredDelivery.RiverDestinationSummary(), Is.EqualTo(delivery.RiverDestinationSummary()));
        Assert.That(restoredDelivery.CycleNodes, Is.Zero);
        Assert.That(restoredDelivery.RiverDestinationAccountingPass, Is.True);
    }

    private static T RoundTrip<T>(Action<BinaryWriter> write, Func<BinaryReader, T> read)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        writer.Flush();
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        T value = read(reader);
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
        return value;
    }
}
