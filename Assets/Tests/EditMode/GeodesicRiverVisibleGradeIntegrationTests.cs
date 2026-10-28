using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicRiverVisibleGradeIntegrationTests
{
    [Test]
    public void NewDebugOptionsDefaultOffAndPersistIndependently()
    {
        var defaults = new SimulationStartupConfig();
        Assert.That(defaults.colourRenderedRiversByVisibleTerrainGrade, Is.False);
        Assert.That(defaults.showRiverTerminationReasons, Is.False);
        var config = new SimulationStartupConfig { colourRenderedRiversByVisibleTerrainGrade = true, showRiverTerminationReasons = true };
        var old = SimulationStartupController.DeserializeSavedConfig("{\"version\":11}", config);
        Assert.That(old.colourRenderedRiversByVisibleTerrainGrade, Is.False);
        Assert.That(old.showRiverTerminationReasons, Is.False);
        var type = typeof(SimulationStartupController).GetNestedType("SavedStartupConfig", BindingFlags.NonPublic);
        var saved = type.GetMethod("FromConfig", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { config });
        var loaded = SimulationStartupController.DeserializeSavedConfig(JsonUtility.ToJson(saved), defaults);
        Assert.That(loaded.colourRenderedRiversByVisibleTerrainGrade, Is.True);
        Assert.That(loaded.showRiverTerminationReasons, Is.True);
        Assert.That(loaded.showRejectedUphillRiverSegments, Is.False);
        SimulationStartupController.CopyAdvancedSettings(defaults, loaded);
        Assert.That(loaded.colourRenderedRiversByVisibleTerrainGrade && loaded.showRiverTerminationReasons, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AllToggleCombinationsPreserveGeometryAndHydrologyAndOffRestoresExactMesh(bool raw)
    {
        var obj = new GameObject("Visible grade audit invariant fixture"); obj.SetActive(false);
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
            rivers.Initialize(planet, geometry, surface);
            var field = typeof(GeodesicRiverSystem).GetField("riverMesh", BindingFlags.Instance | BindingFlags.NonPublic);
            var mesh = (Mesh)field.GetValue(rivers);
            Assert.That(mesh, Is.Not.Null);
            var vertices = mesh.vertices; var indices = mesh.GetIndices(0); var colors = mesh.colors;
            var primitivePositions = indices.Select(i => vertices[i]).ToArray();
            var receivers = rivers.TinDrainage.TriangleReceiver.ToArray(); var runoff = rivers.TinDrainage.AccumulatedRunoff.ToArray();
            var areas = rivers.TinDrainage.DrainageArea.ToArray(); var mouths = rivers.Mouths.ToArray();
            var paths = rivers.RiverPaths.ToDictionary(p => p.Key, p => p.Value.ToArray());
            string previousAudit = null;
            foreach (var toggles in new[] { (false,false), (true,false), (true,true), (false,true), (true,false), (false,false) })
            {
                rivers.colourRenderedRiversByVisibleTerrainGrade = toggles.Item1;
                rivers.showRiverTerminationReasons = toggles.Item2; rivers.RebuildVisuals();
                mesh = (Mesh)field.GetValue(rivers); var currentVertices = mesh.vertices;
                CollectionAssert.AreEqual(primitivePositions, mesh.GetIndices(0).Select(i => currentVertices[i]));
                CollectionAssert.AreEqual(receivers, rivers.TinDrainage.TriangleReceiver);
                CollectionAssert.AreEqual(runoff, rivers.TinDrainage.AccumulatedRunoff);
                CollectionAssert.AreEqual(areas, rivers.TinDrainage.DrainageArea); CollectionAssert.AreEqual(mouths, rivers.Mouths);
                foreach (var pair in paths) CollectionAssert.AreEqual(pair.Value, rivers.RiverPaths[pair.Key]);
                CollectionAssert.AreEqual(original, surface.vertices); Assert.That(rivers.Lakes, Is.Null);
                if (!toggles.Item1)
                {
                    CollectionAssert.AreEqual(vertices, mesh.vertices); CollectionAssert.AreEqual(indices, mesh.GetIndices(0));
                    CollectionAssert.AreEqual(colors, mesh.colors);
                }
                if (toggles.Item1 || toggles.Item2)
                {
                    Assert.That(rivers.VisibleGradeAudit.Segments.Count, Is.EqualTo(paths.Sum(p => p.Value.Length - 1)));
                    string summary = rivers.VisibleGradeAudit.Summary();
                    if (previousAudit != null) Assert.That(summary, Is.EqualTo(previousAudit));
                    previousAudit = summary;
                }
                else Assert.That(rivers.VisibleGradeAudit, Is.Null);
                if (!toggles.Item2) Assert.That(rivers.TerminationDebugVertexCount, Is.Zero);
                else
                {
                    int markerCount = rivers.RiverTerminationPoints.Count(p => p.Reason == TinTermination.TrueClosedBasin || p.Reason == TinTermination.RoutingFailure);
                    Assert.That(rivers.TerminationDebugVertexCount, Is.EqualTo(markerCount * 6));
                }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); if (surface != null) UnityEngine.Object.DestroyImmediate(surface); }
    }
}
