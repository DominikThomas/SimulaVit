using System;
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
    public void RuntimeLakeMeshRiverBridgeAndClearPreserveThePlanet()
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
            var lake = rivers.Lakes.Basins.Single(b => b.Selected);
            Assert.That(rivers.LakeConnectedReaches, Is.GreaterThan(0));
            Assert.That(rivers.LakeOutlets, Is.EqualTo(1));
            Assert.That(rivers.LakeInlets, Is.GreaterThan(0));
            Assert.That(rivers.Mouths.All(m => ocean[m.OceanCell]), Is.True);
            Assert.That(rivers.Mouths.All(m => rivers.Lakes.BasinId[m.OceanCell] != lake.Id), Is.True);
            CollectionAssert.AreEqual(original, surface.vertices);
            CollectionAssert.AreEqual(geometry.Triangles, surface.triangles);
            var water = obj.transform.Find("Geodesic Lakes"); Assert.That(water, Is.Not.Null);
            obj.transform.SetPositionAndRotation(new Vector3(17f, -6f, 11f), Quaternion.Euler(23f, 71f, -12f));
            foreach (Vector3 local in water.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                var restored = obj.transform.InverseTransformPoint(water.TransformPoint(local));
                Assert.That(restored.magnitude, Is.EqualTo(lake.SpillElevation).Within(.00001f));
            }
            var lakeData = rivers.Lakes; var geometryData = rivers.LakeGeometry;
            rivers.UpdateRunoff(new double[topology.CellCount]);
            Assert.That(rivers.Lakes, Is.SameAs(lakeData)); Assert.That(rivers.LakeGeometry, Is.SameAs(geometryData));
            Assert.That(rivers.Lakes.Basins[lake.Id].CurrentSurfaceElevation, Is.EqualTo(lake.SpillElevation));
            planet.generateHydrologicalLakes = false; rivers.Initialize(planet, geometry, surface);
            Assert.That(obj.transform.Find("Geodesic Lakes"), Is.Null);
            Assert.That(rivers.Lakes.LakeMask.Any(wet => wet), Is.False);
            CollectionAssert.AreEqual(original, surface.vertices);
            planet.ApplyStartupGrid(PlanetGridType.LegacyCubeSphere, 10, 3);
            rivers.Initialize(planet, geometry, surface);
            Assert.That(rivers.Drainage, Is.Null); Assert.That(rivers.LakeConnectedReaches, Is.Zero);
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); if (surface != null) UnityEngine.Object.DestroyImmediate(surface); }
    }
}
