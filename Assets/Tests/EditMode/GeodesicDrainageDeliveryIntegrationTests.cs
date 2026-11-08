using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// Native Unity fixture: verifies the owner lifecycle and mesh invariance in addition to
// the managed topology tests. High carving threshold exercises two finalization passes
// without requiring a full generated PlanetGenerator terrain-data cache in this fixture.
public sealed class GeodesicDrainageDeliveryIntegrationTests
{
    [Test] public void DiagnosticsRunOnceAfterFinalPassAndDoNotChangeHydrologyOrMeshes()
    {
        var obj = new GameObject("Drainage delivery integration"); obj.SetActive(false);
        Mesh surface = null; var logs = new List<string>();
        void Capture(string message, string stack, LogType type) => logs.Add(message);
        Application.logMessageReceived += Capture;
        try
        {
            var planet = obj.AddComponent<PlanetGenerator>();
            planet.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, 10, 2);
            planet.ApplyStartupSeed(123456, false); planet.radius = 8;
            planet.geodesicRenderSubdivisionLevel = 3; planet.enableOcean = true;
            planet.enableGeodesicRivers = true; planet.generateHydrologicalLakes = true;
            planet.enableTerminationValleyCarving = true; planet.valleyCarvingPasses = 2;
            planet.minimumValleyCarveRunoff = 1000000;
            var topology = GeodesicGridTopology.Build(2);
            typeof(PlanetGenerator).GetProperty("GeodesicTopology").SetValue(planet, topology);
            var mask = topology.CellDirections.Select(d => planet.EvaluateGeodesicTerrainHeight(d) < 0).ToArray();
            typeof(PlanetGenerator).GetField("geodesicOceanMask", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(planet, mask);
            var geometry = IcosphereRenderGeometryCache.GetOrBuild(3);
            var original = geometry.UnitVertices.Select(d => d * (8 + planet.EvaluateGeodesicTerrainHeight(d))).ToArray();
            surface = new Mesh { vertices = original, triangles = geometry.Triangles };
            obj.AddComponent<MeshFilter>().sharedMesh = surface;
            var rivers = obj.AddComponent<GeodesicRiverSystem>(); rivers.riverFlowThreshold = .1f;
            rivers.logDrainageDelivery = false; rivers.Initialize(planet, geometry, surface);
            Assert.That(rivers.DrainageDelivery, Is.Null); Assert.That(rivers.TinDrainage.ParcelDestinations, Is.Null);
            var heights = rivers.TinDrainage.HydrologyHeight.ToArray(); var receivers = rivers.TinDrainage.TriangleReceiver.ToArray();
            var runoff = rivers.TinDrainage.AccumulatedRunoff.ToArray();
            var paths = rivers.TinDrainage.Networks.Select(n => n.Samples.ToArray()).ToArray();
            var metadata = rivers.TinDrainage.Networks.Select(n => (n.Termination, n.JunctionNetwork)).ToArray();
            var lakeVertices = rivers.TinLakeGeometry.Vertices.ToArray(); var lakeTriangles = rivers.TinLakeGeometry.Triangles.ToArray();
            var riverMeshField = typeof(GeodesicRiverSystem).GetField("riverMesh", BindingFlags.NonPublic | BindingFlags.Instance);
            var mesh = (Mesh)riverMeshField.GetValue(rivers);
            var riverVertices = mesh.vertices; var riverIndices = mesh.GetIndices(0);
            planet.GeodesicValleyCarvingAttempted = false; rivers.logDrainageDelivery = true; logs.Clear();
            rivers.Initialize(planet, geometry, surface);
            Assert.That(rivers.DrainageDelivery, Is.Not.Null);
            Assert.That(logs.Count(l => l.StartsWith("[GeodesicDrainageDelivery]")), Is.EqualTo(1));
            Assert.That(logs.Count(l => l.StartsWith("[GeodesicDrainageClosedCatchment]")), Is.LessThanOrEqualTo(20));
            Assert.That(logs.FindIndex(l => l.StartsWith("[GeodesicDrainageDelivery]")),
                Is.GreaterThan(logs.FindLastIndex(l => l.Contains("coastalVerification=authoritative-rebuild"))));
            Assert.That(rivers.ValleyCarvingResults.Count, Is.EqualTo(2));
            CollectionAssert.AreEqual(original, surface.vertices); CollectionAssert.AreEqual(heights, rivers.TinDrainage.HydrologyHeight);
            CollectionAssert.AreEqual(receivers, rivers.TinDrainage.TriangleReceiver); CollectionAssert.AreEqual(runoff, rivers.TinDrainage.AccumulatedRunoff);
            CollectionAssert.AreEqual(metadata, rivers.TinDrainage.Networks.Select(n => (n.Termination, n.JunctionNetwork)));
            for (int i = 0; i < paths.Length; i++) CollectionAssert.AreEqual(paths[i], rivers.TinDrainage.Networks[i].Samples);
            CollectionAssert.AreEqual(lakeVertices, rivers.TinLakeGeometry.Vertices); CollectionAssert.AreEqual(lakeTriangles, rivers.TinLakeGeometry.Triangles);
            mesh = (Mesh)riverMeshField.GetValue(rivers);
            CollectionAssert.AreEqual(riverVertices, mesh.vertices); CollectionAssert.AreEqual(riverIndices, mesh.GetIndices(0));
            CollectionAssert.AreEqual(mask, Enumerable.Range(0, topology.CellCount).Select(planet.IsGeodesicCellOcean));
            Assert.That(rivers.TinDrainage.ParcelDestinations, Is.Null, "Generation-only scratch must be released.");
            double result = rivers.DrainageDelivery.TotalRunoff;
            logs.Clear(); rivers.RebuildVisuals();
            Assert.That(logs.Any(l => l.StartsWith("[GeodesicDrainageDelivery]")), Is.False);
            Assert.That(rivers.DrainageDelivery.TotalRunoff, Is.EqualTo(result));
        }
        finally
        {
            Application.logMessageReceived -= Capture;
            Object.DestroyImmediate(obj); if (surface != null) Object.DestroyImmediate(surface);
        }
    }
}
