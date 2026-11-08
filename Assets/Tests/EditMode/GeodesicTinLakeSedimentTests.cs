using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicTinLakeSedimentTests
{
    private static GeodesicTinLakeSediment Sediment(GeodesicTinHydrology tin, GeodesicTinBasins basins, double depth = 100) =>
        new GeodesicTinLakeSediment(tin, GeodesicTinLakeGeometry.Build(tin, basins),
            GeodesicTinLakeGeometry.Build(tin, basins.WithVisibility(0, depth)));

    private static GeodesicTinLakeSediment.Footprint Footprint(Func<float, float, float> height)
    {
        var tin = GeodesicTinHydrologyTests.Grid(height, size: 24).Tin;
        return Sediment(tin, GeodesicTinBasins.Build(tin, 0, 0)).Lakes.Values.Single();
    }

    [Test] public void StraightHorizontalCrossingRetainsItsEndpointsAndLevel()
    {
        var lake = Footprint((x, y) => 2 + x * x + (y - 1) * (y - 1));
        var start = new Vector3(-1, 1, 0); var end = new Vector3(1, 1, 0);
        var path = lake.Route(lake.Project(start), lake.Project(end), out bool direct);
        Assert.That(direct, Is.True);
        Assert.That(lake.Direction(path.First()), Is.EqualTo(start));
        Assert.That(lake.Direction(path.Last()), Is.EqualTo(end));
        var vertices = new List<Vector3>(); var indices = new List<int>();
        GeodesicTinLakeCrossings.AddRibbon(new GeodesicTinLakeCrossings.Crossing { Bed = lake, Path = path },
            .05f, .0003f, Color.blue, false, vertices, indices, new List<Color>());
        Assert.That(indices.Count, Is.GreaterThan(0));
        Assert.That(vertices.Max(v => Math.Abs(v.z - (lake.Level + .0003f))), Is.LessThan(1e-6f));
        Assert.That(vertices.Min(v => (v - (start + Vector3.forward * (lake.Level + .0003f))).magnitude), Is.LessThan(1e-6f), "inlet cap");
        Assert.That(vertices.Min(v => (v - (end + Vector3.forward * (lake.Level + .0003f))).magnitude), Is.LessThan(1e-6f), "outlet cap");
    }

    [Test] public void CrescentUsesAnInteriorRoute()
    {
        float Radius(float x, float y) => Mathf.Sqrt(x * x + y * y);
        var lake = Footprint((x, y) => 2 + Mathf.Max(Radius(x, y - 1) - .7f, .7f - Radius(x - .75f, y - 1)));
        AssertInterior(lake, new Vector3(.65f, .3f), new Vector3(.65f, 1.7f));
    }

    [Test] public void IslandRemainsDryAndCrossingGoesAroundIt()
    {
        var lake = Footprint((x, y) =>
        {
            float r = Mathf.Sqrt(x * x + (y - 1) * (y - 1));
            return 2 + Mathf.Max(r - .65f, .6f - r);
        });
        Assert.That(lake.Contains(new Vector3(0, 1)), Is.False);
        AssertInterior(lake, new Vector3(-.6f, 1), new Vector3(.6f, 1));
    }

    private static void AssertInterior(GeodesicTinLakeSediment.Footprint lake, Vector3 start, Vector3 end)
    {
        var a = lake.Project(start); var b = lake.Project(end);
        Assert.That(lake.Covers(a, b), Is.False);
        var path = lake.Route(a, b, out bool direct);
        Assert.That(direct, Is.False);
        Assert.That(path, Is.Not.Null);
        Assert.That(path.Length, Is.GreaterThan(2));
        Assert.That(path.First(), Is.EqualTo(a)); Assert.That(path.Last(), Is.EqualTo(b));
        for (int i = 1; i < path.Length; i++) Assert.That(lake.Covers(path[i - 1], path[i]), Is.True);
    }

    private static (GeodesicTinHydrology Tin, GeodesicTinBasins Basins, GeodesicTinLakeRouting Routing) Cascade()
    {
        var heights = new[] { 9f, 6f, 7f, 4f, 4.25f, 2f, 3f, 0f, -1f };
        float Height(float x, float y)
        {
            float sample = (x + 1) * 4;
            int i = Math.Min((int)sample, heights.Length - 2);
            return Mathf.Lerp(heights[i], heights[i + 1], sample - i) + 12 * (y - 1) * (y - 1);
        }
        var tin = GeodesicTinHydrologyTests.Grid(Height, size: 32, ocean: true, boundaryOutflow: false).Tin;
        tin.CaptureParcelDestinations = true; tin.Accumulate(); tin.ExtractNetworks(.01);
        var basins = GeodesicTinBasins.Build(tin, 0, 0);
        return (tin, basins, GeodesicTinLakeRouting.Build(tin, basins, GeodesicTinLakeGeometry.Build(tin, basins), .01));
    }

    private static GeodesicTinLakeCrossings Crossings(GeodesicTinHydrology tin, GeodesicTinLakeRouting routing,
        GeodesicTinLakeSediment sediment) => GeodesicTinLakeCrossings.Build(tin, routing, sediment,
            routing.RiverReaches.Select(r => routing.Directions(r.Samples).Length >= 2).ToArray());

    [Test] public void MultipleInletsShareAConnectedTrunk()
    {
        var (tin, basins, routing) = Cascade();
        var crossings = Crossings(tin, routing, Sediment(tin, basins));
        Assert.That(crossings.Junctions, Is.GreaterThan(0));
        var paths = crossings.Paths.GroupBy(p => p.Bed.Id).First(g => g.Count() > 1).ToArray();
        var trunk = paths[0];
        var lake = routing.Lakes.Single(n => n.BasinId == trunk.Bed.Id);
        Assert.That(trunk.Discharge, Is.EqualTo(lake.Discharge));
        foreach (var path in paths.Skip(1))
        {
            Vector2 join = path.Path.Last();
            Assert.That(paths.TakeWhile(p => p != path).Any(p => OnPath(p.Path, join)), Is.True);
        }
    }

    private static bool OnPath(Vector2[] path, Vector2 point)
    {
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 a = path[i - 1], ab = path[i] - a;
            float t = Vector2.Dot(point - a, ab) / ab.sqrMagnitude;
            if ((a + ab * Mathf.Clamp01(t) - point).sqrMagnitude < 1e-12f) return true;
        }
        return false;
    }

    [Test] public void ConsecutiveFilteredLakesKeepTheirOriginalInletsAndOutlets()
    {
        var (tin, basins, routing) = Cascade();
        var lakes = routing.Lakes.OrderByDescending(n => n.Basin.SpillElevation).ToArray();
        Assert.That(lakes.Length, Is.EqualTo(3));
        Assert.That(lakes[0].DownstreamLakeId, Is.EqualTo(lakes[1].BasinId));
        Assert.That(lakes[1].DownstreamLakeId, Is.EqualTo(lakes[2].BasinId));
        var crossings = Crossings(tin, routing, Sediment(tin, basins));
        Assert.That(crossings.Failures, Is.Empty);
        Assert.That(crossings.Paths.Select(p => p.Bed.Id).Distinct(), Is.EquivalentTo(lakes.Select(n => n.BasinId)));
        foreach (var upstream in lakes.Take(2))
        {
            TinPoint? inlet = upstream.DownstreamLakeEntry;
            if (!inlet.HasValue)
            {
                int network = upstream.JoinNetwork;
                while (network >= 0 && routing.RiverReaches[network].ReceivingLakeId < 0)
                    network = routing.RiverReaches[network].JunctionNetwork;
                Assert.That(network, Is.GreaterThanOrEqualTo(0));
                inlet = routing.RiverReaches[network].Samples.Last();
            }
            var target = crossings.Paths.Where(p => p.Bed.Id == upstream.DownstreamLakeId).ToArray();
            Assert.That(target.Any(p => OnPath(p.Path, p.Bed.Project(tin.Direction(inlet.Value)))));
        }
    }

    [Test] public void FilteredLakeConnectsTwoRetainedLakes()
    {
        var (tin, basins, routing) = Cascade();
        var sediment = Sediment(tin, basins, .5);
        Assert.That(sediment.LakeCount, Is.EqualTo(1));
        int middle = sediment.Lakes.Keys.Single();
        Assert.That(routing.Lakes.Any(n => n.BasinId != middle && n.DownstreamLakeId == middle));
        var crossings = Crossings(tin, routing, sediment);
        Assert.That(crossings.Failures, Is.Empty);
        Assert.That(crossings.Paths, Is.Not.Empty);
        Assert.That(crossings.Paths.All(p => p.Bed.Id == middle));
        Assert.That(crossings.VisibleOutlets, Does.Contain(middle));
    }

    [Test] public void ClosedBasinKeepsItsInlandDestination()
    {
        var tin = GeodesicTinHydrologyTests.Grid((x, y) => 2 + x * x + (y - 1) * (y - 1),
            size: 8, boundaryOutflow: false).Tin;
        tin.CaptureParcelDestinations = true; tin.Accumulate(); tin.ExtractNetworks(.01);
        var basins = GeodesicTinBasins.Build(tin, 0, 0);
        var sediment = Sediment(tin, basins);
        Assert.That(basins.Basins.Single().Terminal);
        Assert.That(sediment.LakeCount, Is.Zero);
        var delivery = GeodesicDrainageDelivery.Analyze(tin, basins);
        Assert.That(delivery.InlandClosed, Is.EqualTo(delivery.TotalRunoff).Within(1e-10));
        Assert.That(delivery.DirectOcean + delivery.ViaLakesOcean, Is.Zero);
    }

    [Test] public void NestedLakeUsesOneOriginalFootprint()
    {
        var tin = GeodesicTinHydrologyTests.Grid((x, y) => 2 + 16 * (x * x - .25f) * (x * x - .25f) + 4 * (y - 1) * (y - 1), size: 16).Tin;
        var basins = GeodesicTinBasins.Build(tin, 0, 0);
        var sediment = Sediment(tin, basins);
        Assert.That(basins.Basins.Count, Is.EqualTo(2));
        Assert.That(sediment.LakeCount, Is.EqualTo(1));
        Assert.That(sediment.GeometryFailures, Is.Empty);
        Assert.That(sediment.Lakes.Values.Single().Level, Is.EqualTo(6));
    }

    [Test] public void SedimentMeshLevelsTheWetPolygonsAndKeepsUntouchedFacets()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => 8 + x * x + (y - 1) * (y - 1), size: 8);
        var directions = f.Vertices.Select(v => new Vector3(v.x * .1f, (v.y - 1) * .1f, 1).normalized).ToArray();
        var original = new Mesh { vertices = directions.Select((d, i) => d * f.Heights[i]).ToArray(), triangles = f.Triangles };
        Mesh surface = null;
        try
        {
            original.RecalculateNormals();
            var tin = new GeodesicTinHydrology(directions, f.Triangles, f.Heights, f.Heights, allowBoundaryOutflow: true);
            var sediment = Sediment(tin, GeodesicTinBasins.Build(tin, 0, 0));
            var lake = sediment.Lakes.Values.Single();
            surface = sediment.BuildSurface(original);
            var positions = surface.vertices;
            var triangles = surface.triangles;
            var faces = new HashSet<(Vector3, Vector3, Vector3)>();
            for (int i = 0; i < triangles.Length; i += 3)
                faces.Add((positions[triangles[i]], positions[triangles[i + 1]], positions[triangles[i + 2]]));
            var originalPositions = original.vertices;
            for (int t = 0; t < tin.TriangleCount; t++)
            {
                if (!lake.Facets.TryGetValue(t, out var facet))
                {
                    Assert.That(faces.Contains((originalPositions[f.Triangles[t * 3]], originalPositions[f.Triangles[t * 3 + 1]],
                        originalPositions[f.Triangles[t * 3 + 2]])), Is.True);
                    continue;
                }
                var level = facet.Points.Select(p => tin.Direction(p) * lake.Level).ToArray();
                for (int i = 1; i + 1 < facet.Points.Length; i++)
                    Assert.That(faces.Contains((level[0], level[i], level[i + 1])), Is.True);
            }
            CollectionAssert.AreEqual(f.Triangles, original.triangles);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(surface);
            UnityEngine.Object.DestroyImmediate(original);
        }
    }

    [Test] public void FilteringPreservesPrimaryAndTotalOceanRunoffAndParcelDestinations()
    {
        var (tin, basins, routing) = Cascade();
        var before = GeodesicDrainageDelivery.Analyze(tin, basins, routing.Lakes, routing.RiverReaches,
            oceanComponentAt: _ => 0, primaryOceanComponent: 0);
        var runoff = routing.Lakes.Select(l => (l.BasinId, l.Discharge, l.DownstreamLakeId)).ToArray();
        Crossings(tin, routing, Sediment(tin, basins));
        var after = GeodesicDrainageDelivery.Analyze(tin, basins.WithVisibility(0, 100), routing.Lakes, routing.RiverReaches,
            oceanComponentAt: _ => 0, primaryOceanComponent: 0);
        Assert.That(before.PrimaryOcean, Is.GreaterThan(0));
        Assert.That(after.PrimaryOcean, Is.EqualTo(before.PrimaryOcean));
        foreach (var lake in routing.Lakes)
        {
            string route = "lake:" + lake.BasinId;
            Assert.That(before.RiverOutcome(route).RiverColour, Is.EqualTo(GeodesicDrainageDelivery.RiverDestination.Ocean));
            Assert.That(after.RiverOutcome(route).RiverColour, Is.EqualTo(before.RiverOutcome(route).RiverColour));
        }
        Assert.That(after.DirectOcean + after.ViaLakesOcean, Is.EqualTo(before.DirectOcean + before.ViaLakesOcean));
        CollectionAssert.AreEqual(runoff, routing.Lakes.Select(l => (l.BasinId, l.Discharge, l.DownstreamLakeId)));
        for (int i = 0; i < tin.TriangleCount; i++)
            Assert.That(after.IsConfirmedOceanParcel(i), Is.EqualTo(before.IsConfirmedOceanParcel(i)));
    }
}
