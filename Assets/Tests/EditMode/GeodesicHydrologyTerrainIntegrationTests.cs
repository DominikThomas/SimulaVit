using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicHydrologyTerrainIntegrationTests
{
    [Test]
    public void AllModesRoundTripAndSchema13RetainsFailedExperiment()
    {
        Assert.That(new SimulationStartupConfig().hydrologyTerrainMode, Is.EqualTo(HydrologyTerrainMode.Visible));
        var savedType = typeof(SimulationStartupController).GetNestedType("SavedStartupConfig", BindingFlags.NonPublic);
        foreach (HydrologyTerrainMode mode in System.Enum.GetValues(typeof(HydrologyTerrainMode)))
        {
            var config = new SimulationStartupConfig { hydrologyTerrainMode = mode };
            object saved = savedType.GetMethod("FromConfig").Invoke(null, new object[] { config });
            Assert.That(SimulationStartupController.DeserializeSavedConfig(JsonUtility.ToJson(saved), null).hydrologyTerrainMode, Is.EqualTo(mode));
            SimulationStartupController.CopyNormalSettings(new SimulationStartupConfig(), config);
            Assert.That(config.hydrologyTerrainMode, Is.EqualTo(mode));
            SimulationStartupController.CopyAdvancedSettings(new SimulationStartupConfig(), config);
            Assert.That(config.hydrologyTerrainMode, Is.EqualTo(HydrologyTerrainMode.Visible));
        }
        Assert.That(SimulationStartupController.DeserializeSavedConfig("{\"version\":13,\"useLowFrequencyTerrainForHydrology\":true}", null).hydrologyTerrainMode,
            Is.EqualTo(HydrologyTerrainMode.ReducedRidgeAndFineDetail));
        Assert.That(SimulationStartupController.DeserializeSavedConfig("{\"version\":14,\"hydrologyTerrainMode\":99}", null).hydrologyTerrainMode,
            Is.EqualTo(HydrologyTerrainMode.Visible));
    }

    [Test]
    public void DefaultsMigrationSaveAndAdvancedReset()
    {
        var defaults = new SimulationStartupConfig();
        Assert.That(defaults.useLowFrequencyTerrainForHydrology, Is.False);
        var enabled = new SimulationStartupConfig { useLowFrequencyTerrainForHydrology = true };
        Assert.That(SimulationStartupController.DeserializeSavedConfig("{\"version\":12}", enabled).useLowFrequencyTerrainForHydrology, Is.False);
        var type = typeof(SimulationStartupController).GetNestedType("SavedStartupConfig", BindingFlags.NonPublic);
        var saved = type.GetMethod("FromConfig").Invoke(null, new object[] { enabled });
        Assert.That(SimulationStartupController.DeserializeSavedConfig(JsonUtility.ToJson(saved), defaults).useLowFrequencyTerrainForHydrology, Is.True);
        SimulationStartupController.CopyNormalSettings(defaults, enabled);
        Assert.That(enabled.useLowFrequencyTerrainForHydrology, Is.True);
        SimulationStartupController.CopyAdvancedSettings(defaults, enabled);
        Assert.That(enabled.useLowFrequencyTerrainForHydrology, Is.False);
    }

    [TestCase(HydrologyTerrainMode.ReducedRidgeAndFineDetail)]
    [TestCase(HydrologyTerrainMode.FineDetailRemoved)]
    public void RiverOnlyTogglePreservesMeshOceanSeaLevelAndPausedLakesAndRestoresOff(HydrologyTerrainMode experiment)
    {
        var obj = new GameObject("Low frequency invariant fixture"); obj.SetActive(false);
        Mesh surface = null;
        try
        {
            var planet = obj.AddComponent<PlanetGenerator>();
            Assert.That(planet.useLowFrequencyTerrainForHydrology, Is.False);
            planet.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, 10, 2);
            planet.ApplyStartupSeed(123456, false); planet.radius = 8f; planet.geodesicRenderSubdivisionLevel = 3;
            planet.enableOcean = true; planet.enableGeodesicRivers = true; planet.generateHydrologicalLakes = true;
            var topology = GeodesicGridTopology.Build(2);
            typeof(PlanetGenerator).GetProperty("GeodesicTopology").SetValue(planet, topology);
            var mask = topology.CellDirections.Select(d => planet.EvaluateGeodesicTerrainHeight(d) < 0f).ToArray();
            typeof(PlanetGenerator).GetField("geodesicOceanMask", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(planet, mask.ToArray());
            var geometry = IcosphereRenderGeometryCache.GetOrBuild(3);
            var original = geometry.UnitVertices.Select(d => d * (8f + planet.EvaluateGeodesicTerrainHeight(d))).ToArray();
            surface = new Mesh { vertices = original, triangles = geometry.Triangles };
            obj.AddComponent<MeshFilter>().sharedMesh = surface;
            var rivers = obj.AddComponent<GeodesicRiverSystem>(); rivers.riverFlowThreshold = .1f;
            rivers.Initialize(planet, geometry, surface);
            var heights = rivers.TinDrainage.HydrologyHeight.ToArray();
            var receiver = rivers.TinDrainage.TriangleReceiver.ToArray(); var runoff = rivers.TinDrainage.AccumulatedRunoff.ToArray();
            var paths = rivers.RiverPaths.ToDictionary(p => p.Key, p => p.Value.ToArray());
            var ocean = Enumerable.Range(0, rivers.TinDrainage.TriangleCount).Select(t => rivers.TinDrainage.IsOcean(rivers.TinDrainage.Centroid(t))).ToArray();
            float sea = planet.GeodesicSeaLevelRadius;
            foreach (bool enabled in new[] { true, false })
            {
                planet.hydrologyTerrainMode = enabled ? experiment : HydrologyTerrainMode.Visible; rivers.RebuildForCurrentTerrain();
                CollectionAssert.AreEqual(original, surface.vertices);
                CollectionAssert.AreEqual(geometry.Triangles, surface.triangles);
                CollectionAssert.AreEqual(mask, Enumerable.Range(0, topology.CellCount).Select(planet.IsGeodesicCellOcean));
                CollectionAssert.AreEqual(ocean, Enumerable.Range(0, rivers.TinDrainage.TriangleCount).Select(t => rivers.TinDrainage.IsOcean(rivers.TinDrainage.Centroid(t))));
                Assert.That(planet.GeodesicSeaLevelRadius, Is.EqualTo(sea));
                Assert.That(rivers.Lakes, Is.Null); Assert.That(rivers.HydrologicalLakesPausedForBaselineValidation, Is.True);
                Assert.That(planet.generateHydrologicalLakes, Is.True);
                if (enabled) Assert.That(rivers.TinDrainage.HydrologyHeight.SequenceEqual(heights), Is.False);
                else
                {
                    CollectionAssert.AreEqual(heights, rivers.TinDrainage.HydrologyHeight);
                    CollectionAssert.AreEqual(receiver, rivers.TinDrainage.TriangleReceiver); CollectionAssert.AreEqual(runoff, rivers.TinDrainage.AccumulatedRunoff);
                    Assert.That(rivers.RiverPaths.Count, Is.EqualTo(paths.Count));
                    foreach (var pair in paths) CollectionAssert.AreEqual(pair.Value, rivers.RiverPaths[pair.Key]);
                }
            }
        }
        finally { Object.DestroyImmediate(obj); if (surface != null) Object.DestroyImmediate(surface); }
    }
}
