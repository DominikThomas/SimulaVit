using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicRiverUphillDebugIntegrationTests
{
    [Test]
    public void DebugStartupSettingRoundTripsAndOldSavesDefaultOff()
    {
        var config = new SimulationStartupConfig { showRejectedUphillRiverSegments = true };
        var old = SimulationStartupController.DeserializeSavedConfig("{\"version\":10}", config);
        Assert.That(old.showRejectedUphillRiverSegments, Is.False);
        var type = typeof(SimulationStartupController).GetNestedType("SavedStartupConfig", BindingFlags.NonPublic);
        var saved = type.GetMethod("FromConfig", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { config });
        var loaded = SimulationStartupController.DeserializeSavedConfig(JsonUtility.ToJson(saved), new SimulationStartupConfig());
        Assert.That(loaded.showRejectedUphillRiverSegments, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void OffOnOffPreservesAcceptedMeshColoursRunoffTopologyMouthsLakesAndTerrain(bool raw)
    {
        var obj = new GameObject("Uphill diagnostic invariant fixture"); obj.SetActive(false);
        Mesh surface = null;
        try
        {
            var planet = obj.AddComponent<PlanetGenerator>();
            planet.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, 10, 2);
            planet.radius = 8; planet.enableOcean = false; planet.enableGeodesicRivers = true;
            var topology = GeodesicGridTopology.Build(2);
            typeof(PlanetGenerator).GetProperty("GeodesicTopology").SetValue(planet, topology);
            var geometry = IcosphereRenderGeometryCache.GetOrBuild(3);
            var original = geometry.UnitVertices.Select(d => d * (8 + .2f * d.z)).ToArray();
            surface = new Mesh { vertices = original, triangles = geometry.Triangles };
            var rivers = obj.AddComponent<GeodesicRiverSystem>(); rivers.riverFlowThreshold = .01f; rivers.drawRawTrianglePaths = raw;
            Assert.That(rivers.showRejectedUphillRiverSegments, Is.False);
            rivers.Initialize(planet, geometry, surface);
            var meshField = typeof(GeodesicRiverSystem).GetField("riverMesh", BindingFlags.Instance | BindingFlags.NonPublic);
            var mesh = (Mesh)meshField.GetValue(rivers);
            Assert.That(mesh, Is.Not.Null, "Fixture must include accepted downhill reaches.");
            var vertices = mesh.vertices; var indices = mesh.GetIndices(0); var colors = mesh.colors;
            Assert.That(colors.All(c => c.Equals(rivers.riverColor) || c.Equals(Color.Lerp(rivers.riverColor, Color.cyan, .3f))), Is.True);
            var runoff = rivers.TinDrainage.AccumulatedRunoff.ToArray(); var area = rivers.TinDrainage.DrainageArea.ToArray();
            var receivers = rivers.TinDrainage.TriangleReceiver.ToArray(); var mouths = rivers.Mouths.ToArray();
            int visible = rivers.VisibleRiverReaches;
            foreach (bool enabled in new[] { false, true, false })
            {
                rivers.showRejectedUphillRiverSegments = enabled; rivers.RebuildVisuals();
                mesh = (Mesh)meshField.GetValue(rivers);
                CollectionAssert.AreEqual(vertices, mesh.vertices); CollectionAssert.AreEqual(indices, mesh.GetIndices(0));
                CollectionAssert.AreEqual(colors, mesh.colors); Assert.That(rivers.VisibleRiverReaches, Is.EqualTo(visible));
                CollectionAssert.AreEqual(runoff, rivers.TinDrainage.AccumulatedRunoff);
                CollectionAssert.AreEqual(area, rivers.TinDrainage.DrainageArea);
                CollectionAssert.AreEqual(receivers, rivers.TinDrainage.TriangleReceiver);
                CollectionAssert.AreEqual(mouths, rivers.Mouths); CollectionAssert.AreEqual(original, surface.vertices);
                Assert.That(rivers.Lakes, Is.Null); Assert.That(rivers.LakeMeshVertexCount, Is.Zero);
                if (enabled)
                {
                    Assert.That(rivers.UphillDebugVertexCount, Is.GreaterThan(0));
                    var debugMesh = (Mesh)typeof(GeodesicRiverSystem).GetField("uphillDebugMesh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rivers);
                    CollectionAssert.AreEqual(rivers.RejectedUphillDebug.SelectMany(r => Enumerable.Repeat(
                        GeodesicRiverUphillDebug.SeverityColor(r.MaximumSlope), r.Path.Length * (raw ? 1 : 3))), debugMesh.colors);
                }
                if (!enabled) { Assert.That(rivers.UphillDebugVertexCount, Is.Zero); Assert.That(rivers.RejectedUphillDebug, Is.Null); }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); if (surface != null) UnityEngine.Object.DestroyImmediate(surface); }
    }
}
