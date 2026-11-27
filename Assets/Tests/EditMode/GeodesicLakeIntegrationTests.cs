using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicLakeIntegrationTests
{
    [Test]
    public void OldSavesStayDisabledAndNewSettingsRoundTrip()
    {
        var defaults = new SimulationStartupConfig();
        Assert.That(defaults.generateHydrologicalLakes, Is.False);
        Assert.That(defaults.minimumLakeBasinAreaFraction, Is.EqualTo(.0001f));
        Assert.That(defaults.minimumLakeDepth, Is.EqualTo(.005f));
        var custom = new SimulationStartupConfig { generateHydrologicalLakes = true, minimumLakeBasinAreaFraction = .002f, minimumLakeDepth = .03f };
        var old = SimulationStartupController.DeserializeSavedConfig("{\"version\":9}", custom);
        Assert.That(old.generateHydrologicalLakes, Is.False);
        Assert.That(old.minimumLakeDepth, Is.EqualTo(.005f));
        Type savedType = typeof(SimulationStartupController).GetNestedType("SavedStartupConfig", BindingFlags.NonPublic);
        object saved = savedType.GetMethod("FromConfig", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { custom });
        var loaded = SimulationStartupController.DeserializeSavedConfig(JsonUtility.ToJson(saved), defaults);
        Assert.That(loaded.generateHydrologicalLakes, Is.True);
        Assert.That(loaded.minimumLakeBasinAreaFraction, Is.EqualTo(.002f));
        Assert.That(loaded.minimumLakeDepth, Is.EqualTo(.03f));
    }

    [Test]
    public void NormalResetPreservesLakesAndAdvancedResetRestoresDefaults()
    {
        var config = new SimulationStartupConfig { generateHydrologicalLakes = true, minimumLakeBasinAreaFraction = .002f, minimumLakeDepth = .03f };
        SimulationStartupController.CopyNormalSettings(new SimulationStartupConfig(), config);
        Assert.That(config.generateHydrologicalLakes, Is.True);
        Assert.That(config.minimumLakeDepth, Is.EqualTo(.03f));
        SimulationStartupController.CopyAdvancedSettings(new SimulationStartupConfig(), config);
        Assert.That(config.generateHydrologicalLakes, Is.False);
        Assert.That(config.minimumLakeBasinAreaFraction, Is.EqualTo(.0001f));
        Assert.That(config.minimumLakeDepth, Is.EqualTo(.005f));
    }

    [Test]
    public void TinRenderDrainageUsesActiveTopologyWithoutChangingTerrain()
    {
        var obj = new GameObject("Lake integration fixture"); obj.SetActive(false);
        Mesh surface = null;
        try
        {
            var planet = obj.AddComponent<PlanetGenerator>();
            planet.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, 10, 3);
            planet.radius = 8f; planet.geodesicSeaLevelOffset = 0f;
            planet.enableOcean = true; planet.generateHydrologicalLakes = true;
            var topology = GeodesicGridTopology.Build(3);
            typeof(PlanetGenerator).GetProperty("GeodesicTopology").SetValue(planet, topology);
            bool[] ocean = topology.CellDirections.Select(d => d.z < -.3f).ToArray();
            typeof(PlanetGenerator).GetField("geodesicOceanMask", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(planet, ocean);
            var geometry = IcosphereRenderGeometryCache.GetOrBuild(4);
            float Bowl(Vector3 d) { if (d.z < -.3f) return 7.9f; float a = Mathf.Acos(Mathf.Clamp(d.z, -1f, 1f)); return a < .4f ? Mathf.Lerp(8.05f, 8.2f, a*a/.16f) : 8.2f; }
            Vector3[] original = geometry.UnitVertices.Select(d => d * Bowl(d)).ToArray();
            surface = new Mesh { vertices = original, triangles = geometry.Triangles };
            var rivers = obj.AddComponent<GeodesicRiverSystem>(); rivers.riverFlowThreshold = .01f;
            rivers.Initialize(planet, geometry, surface);
            Assert.That(rivers.UsesDedicatedRenderHydrology, Is.True);
            Assert.That(rivers.HydrologySubdivision, Is.EqualTo(4));
            Assert.That(rivers.Drainage, Is.Null);
            Assert.That(rivers.TinDrainage.TriangleCount, Is.EqualTo(geometry.TriangleCount));
            Assert.That(rivers.drawRawTrianglePaths, Is.False);
            Assert.That(planet.generateHydrologicalLakes, Is.True, "Preserve the saved lake generation preference.");
            Assert.That(rivers.Lakes, Is.Null);
            Assert.That(rivers.LakeGeometry, Is.Null);
            Assert.That(rivers.TinLakeBasins, Is.Not.Null);
            Assert.That(obj.transform.Find("Geodesic Lakes"), Is.Null);
            Assert.That(rivers.Mouths.All(m => m.OceanCell >= 0 && planet.IsGeodesicCellOcean(m.OceanCell)), Is.True);
            CollectionAssert.AreEqual(original, surface.vertices);
            CollectionAssert.AreEqual(geometry.Triangles, surface.triangles);
            var tin = rivers.TinDrainage;
            var receivers = tin.TriangleReceiver.ToArray();
            var runoff = tin.AccumulatedRunoff.ToArray();
            var networks = tin.Networks.ToArray();
            var basinIds = rivers.TinLakeBasins.Basins.Select(b => b.Id).ToArray();
            var withLakePaths = rivers.RiverPaths.ToDictionary(p => p.Key, p => p.Value.ToArray());
            var routing = rivers.TinLakeRouting;
            Assert.That(routing, Is.Not.Null);
            Assert.That(routing.Lakes.Count, Is.EqualTo(1));
            var lake = routing.Lakes.Single();
            Assert.That(lake.Basin.NumericalShelf, Is.False);
            CollectionAssert.AreEqual(new[] { lake.BasinId }, rivers.TinLakeGeometry.Bodies.Select(b => b.BasinId));
            Assert.That(routing.FlowConservationErrors, Is.Zero);
            Assert.That(routing.DuplicateParcelSuppression, Is.Zero);

            // Raw networks survive the lake toggle. Presentation can shorten an inlet
            // to a single point without removing the network that supplied it.
            var rawSources = networks.Where(n => n.Centreline.Length >= 2).Select(n => n.SourceTriangle).ToArray();
            var adjustedPaths = routing.RiverReaches.Where(r => r.Changed || routing.HasAddedDischarge(r.RawNetworkId))
                .ToDictionary(r => r.SourceTriangle, r => routing.Directions(r.Samples));
            var shortInlets = routing.RiverReaches.Where(r => r.Changed && adjustedPaths[r.SourceTriangle].Length < 2)
                .Select(r => r.SourceTriangle).ToHashSet();
            Assert.That(shortInlets, Is.Not.Empty, "The bowl must exercise inlets too short to display.");
            foreach (var reach in routing.RiverReaches.Where(r => r.Changed))
            {
                var raw = networks[reach.RawNetworkId];
                Assert.That(routing.FirstEntry(raw.Samples, out var entry), Is.True, $"Source facet {raw.SourceTriangle}");
                Assert.That(reach.ReceivingLakeId, Is.EqualTo(lake.BasinId));
                Assert.That(entry.BasinId, Is.EqualTo(lake.BasinId));
                CollectionAssert.AreEqual(GeodesicTinLakeRouting.ClipAtEntry(raw.Samples, entry), reach.Samples,
                    $"Source facet {raw.SourceTriangle} must end at its first lake entry.");
            }
            AssertEquivalentSources(rawSources.Where(id => !shortInlets.Contains(id)), withLakePaths.Keys, "Lakes on");
            foreach (var path in adjustedPaths.Where(p => p.Value.Length >= 2))
                CollectionAssert.AreEqual(path.Value, withLakePaths[path.Key], $"Lake-adjusted source facet {path.Key}");

            var withLakeDelivery = rivers.DrainageDelivery;
            Assert.That(withLakeDelivery, Is.Not.Null);
            var lakeOutcome = withLakeDelivery.RiverOutcome("lake:" + lake.BasinId);
            Assert.That(lakeOutcome.Destination, Is.EqualTo(GeodesicDrainageDelivery.Destination.InlandClosed));
            Assert.That(lakeOutcome.Type, Is.EqualTo(nameof(TinTermination.TinyNoiseDepression)));
            double lakeRunoff = lake.Discharge;
            var lakeData = rivers.Lakes;
            rivers.UpdateSimulationRunoff(new double[topology.CellCount]);
            Assert.That(rivers.Lakes, Is.SameAs(lakeData));
            Assert.That(rivers.TinDrainage.Networks.Count, Is.Zero);
            planet.generateHydrologicalLakes = false;
            obj.transform.SetPositionAndRotation(new Vector3(17f, -6f, 11f), Quaternion.Euler(23f, 71f, -12f));
            rivers.Initialize(planet, geometry, surface);
            CollectionAssert.AreEqual(receivers, rivers.TinDrainage.TriangleReceiver);
            CollectionAssert.AreEqual(runoff, rivers.TinDrainage.AccumulatedRunoff);
            CollectionAssert.AreEqual(networks.Select(n => (n.SourceTriangle, n.Termination, n.JunctionNetwork)),
                rivers.TinDrainage.Networks.Select(n => (n.SourceTriangle, n.Termination, n.JunctionNetwork)));
            for (int i = 0; i < networks.Length; i++)
                CollectionAssert.AreEqual(networks[i].Samples, rivers.TinDrainage.Networks[i].Samples,
                    $"Raw samples for source facet {networks[i].SourceTriangle}");
            CollectionAssert.AreEqual(basinIds, rivers.TinLakeBasins.Basins.Select(b => b.Id));
            var rawBasin = rivers.TinLakeBasins.Basins.Single(b => b.Id == lake.BasinId);
            Assert.That((rawBasin.Floor, rawBasin.Spill, rawBasin.DownstreamTriangle, rawBasin.DownstreamVertex),
                Is.EqualTo((lake.Basin.Floor, lake.Basin.Spill, lake.Basin.DownstreamTriangle, lake.Basin.DownstreamVertex)));

            AssertEquivalentSources(rawSources, rivers.RiverPaths.Keys, "Lakes off");
            foreach (var path in withLakePaths.Where(p => !adjustedPaths.ContainsKey(p.Key)))
                CollectionAssert.AreEqual(path.Value, rivers.RiverPaths[path.Key], $"Unaffected source facet {path.Key}");
            foreach (var raw in networks.Where(n => adjustedPaths.ContainsKey(n.SourceTriangle) && n.Centreline.Length >= 2))
                Assert.That(rivers.RiverPaths[raw.SourceTriangle].Last(), Is.EqualTo(raw.Centreline.Last()),
                    $"Source facet {raw.SourceTriangle} must reach its unclipped endpoint with lakes off.");
            AssertDrainageDelivery(withLakeDelivery, rivers.DrainageDelivery, lakeRunoff);

            Assert.That(rivers.TinLakeRouting, Is.Null);
            Assert.That(rivers.TinLakeGeometry, Is.Null);
            Assert.That(obj.transform.Find("Geodesic Lakes"), Is.Null);
            CollectionAssert.AreEqual(original, surface.vertices);
            CollectionAssert.AreEqual(geometry.Triangles, surface.triangles);
            planet.ApplyStartupGrid(PlanetGridType.LegacyCubeSphere, 10, 3);
            rivers.Initialize(planet, geometry, surface);
            Assert.That(rivers.Drainage, Is.Null); Assert.That(rivers.TinDrainage, Is.Null); Assert.That(rivers.LakeConnectedReaches, Is.Zero);
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); if (surface != null) UnityEngine.Object.DestroyImmediate(surface); }
    }

    private static void AssertEquivalentSources(IEnumerable<int> expected, IEnumerable<int> actual, string mode)
    {
        var expectedIds = expected.ToArray();
        var actualIds = actual.ToArray();
        var expectedCounts = expectedIds.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        var actualCounts = actualIds.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        var missing = expectedCounts.Keys.Except(actualCounts.Keys).OrderBy(id => id);
        var extra = actualCounts.Keys.Except(expectedCounts.Keys).OrderBy(id => id);
        var multiplicity = expectedCounts.Keys.Intersect(actualCounts.Keys).OrderBy(id => id)
            .Where(id => expectedCounts[id] != actualCounts[id])
            .Select(id => $"{id} ({expectedCounts[id]} expected, {actualCounts[id]} actual)");
        CollectionAssert.AreEquivalent(expectedIds, actualIds,
            $"{mode} expected={expectedIds.Length} actual={actualIds.Length} missing=[{string.Join(", ", missing)}] " +
            $"extra=[{string.Join(", ", extra)}] multiplicity=[{string.Join(", ", multiplicity)}]");
    }

    private static void AssertDrainageDelivery(GeodesicDrainageDelivery withLake, GeodesicDrainageDelivery withoutLake, double lakeRunoff)
    {
        Assert.That(withoutLake, Is.Not.Null);
        double tolerance = withoutLake.Tolerance;
        Assert.That(lakeRunoff, Is.Positive);
        Assert.That(withLake.CycleNodes + withoutLake.CycleNodes, Is.Zero);
        Assert.That(withLake.DuplicateMemberships + withoutLake.DuplicateMemberships, Is.Zero);
        Assert.That(Math.Abs(withLake.Residual), Is.LessThanOrEqualTo(tolerance));
        Assert.That(Math.Abs(withoutLake.Residual), Is.LessThanOrEqualTo(tolerance));
        Assert.That(withLake.IndependentParcels, Is.EqualTo(withoutLake.IndependentParcels));
        Assert.That(withLake.TotalRunoff, Is.EqualTo(withoutLake.TotalRunoff).Within(tolerance));
        Assert.That(withLake.DirectOcean + withLake.ViaLakesOcean,
            Is.EqualTo(withoutLake.DirectOcean + withoutLake.ViaLakesOcean).Within(tolerance));
        Assert.That(withLake.InlandClosed, Is.EqualTo(withoutLake.InlandClosed).Within(tolerance));
        Assert.That(withLake.Unresolved, Is.EqualTo(withoutLake.Unresolved).Within(tolerance));
        Assert.That(ClosedRunoff(withoutLake, nameof(TinTermination.TrueClosedBasin)) -
            ClosedRunoff(withLake, nameof(TinTermination.TrueClosedBasin)), Is.EqualTo(lakeRunoff).Within(tolerance));
        Assert.That(ClosedRunoff(withLake, nameof(TinTermination.TinyNoiseDepression)) -
            ClosedRunoff(withoutLake, nameof(TinTermination.TinyNoiseDepression)), Is.EqualTo(lakeRunoff).Within(tolerance));

        static double ClosedRunoff(GeodesicDrainageDelivery delivery, string type) => delivery.DisconnectedCatchments
            .Where(c => c.Terminal.Destination == GeodesicDrainageDelivery.Destination.InlandClosed && c.Terminal.Type == type)
            .Sum(c => c.Runoff);
    }
}
