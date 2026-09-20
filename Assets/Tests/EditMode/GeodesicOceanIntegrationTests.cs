using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class GeodesicOceanIntegrationTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void StartupDefaultsAndOldSavesDoNotSilentlyEnableFiltering()
    {
        var defaults = new SimulationStartupConfig();
        Assert.That(defaults.excludeSmallDisconnectedSeas, Is.False);
        Assert.That(defaults.minimumOceanComponentAreaFraction, Is.EqualTo(.001f));
        defaults.excludeSmallDisconnectedSeas = true;
        var old = SimulationStartupController.DeserializeSavedConfig("{\"version\":8}", defaults);
        Assert.That(old.excludeSmallDisconnectedSeas, Is.False);
        Assert.That(old.minimumOceanComponentAreaFraction, Is.EqualTo(.001f));
    }

    [Test]
    public void StartupSettingsRoundTripThroughExistingSaveSchema()
    {
        var original = new SimulationStartupConfig { excludeSmallDisconnectedSeas = true, minimumOceanComponentAreaFraction = .0025f };
        Type savedType = typeof(SimulationStartupController).GetNestedType("SavedStartupConfig", BindingFlags.NonPublic);
        object saved = savedType.GetMethod("FromConfig", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { original });
        var loaded = SimulationStartupController.DeserializeSavedConfig(JsonUtility.ToJson(saved), new SimulationStartupConfig());
        Assert.That(loaded.excludeSmallDisconnectedSeas, Is.True);
        Assert.That(loaded.minimumOceanComponentAreaFraction, Is.EqualTo(.0025f));
    }

    [Test]
    public void NormalResetPreservesOceanOptionsAndAdvancedResetRestoresThem()
    {
        var defaults = new SimulationStartupConfig();
        var config = new SimulationStartupConfig { excludeSmallDisconnectedSeas = true, minimumOceanComponentAreaFraction = .004f };
        SimulationStartupController.CopyNormalSettings(defaults, config);
        Assert.That(config.excludeSmallDisconnectedSeas, Is.True);
        Assert.That(config.minimumOceanComponentAreaFraction, Is.EqualTo(.004f));
        SimulationStartupController.CopyAdvancedSettings(defaults, config);
        Assert.That(config.excludeSmallDisconnectedSeas, Is.False);
        Assert.That(config.minimumOceanComponentAreaFraction, Is.EqualTo(.001f));
    }

    [Test]
    public void SameSeedFilterTogglePreservesTerrainAndRemovesMarineHabitat()
    {
        var obj = new GameObject("Ocean connectivity generation test");
        obj.SetActive(false); // Do not start the scene's environment or population.
        try
        {
            var planet = obj.AddComponent<PlanetGenerator>();
            planet.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, 10, 3);
            planet.ApplyStartupSeed(12345, false);
            var topology = GeodesicGridTopology.Build(3);
            typeof(PlanetGenerator).GetProperty("GeodesicTopology").SetValue(planet, topology);
            planet.enableOcean = true;
            planet.enableGeodesicTerrainDisplacement = true;
            // Fine-detail terrain deliberately creates numerous small disconnected basins.
            planet.geodesicContinentAmplitude = 0f;
            planet.geodesicMountainAmplitude = 0f;
            planet.geodesicFineDetailAmplitude = .08f;
            planet.geodesicFineDetailScale = 18f;
            planet.geodesicSeaLevelControlMode = GeodesicSeaLevelControlMode.ManualOffset;
            planet.geodesicSeaLevelOffset = 0f;
            planet.minimumOceanComponentAreaFraction = .01f;
            MethodInfo rebuild = typeof(PlanetGenerator).GetMethod("RebuildGeodesicOceanClassification", PrivateInstance);
            planet.excludeSmallDisconnectedSeas = false;
            rebuild.Invoke(planet, null);
            float[] sortedRaw = Enumerable.Range(0, topology.CellCount).Select(planet.GetGeodesicCellRawTerrainRadius).Distinct().OrderBy(r => r).ToArray();
            Assert.That(sortedRaw.Length, Is.GreaterThan(1));
            // Place sea level between the two lowest distinct heights so this seeded fixture
            // necessarily exercises a small basin, independently of terrain seed derivation.
            planet.geodesicSeaLevelOffset = (sortedRaw[0] + sortedRaw[1]) * .5f - planet.BasePlanetRadius;
            rebuild.Invoke(planet, null);
            float[] beforeRaw = Enumerable.Range(0, topology.CellCount).Select(planet.GetGeodesicCellRawTerrainRadius).ToArray();
            float[] beforeFloor = Enumerable.Range(0, topology.CellCount).Select(planet.GetGeodesicCellSeafloorRadius).ToArray();
            float[] beforeSurface = topology.CellDirections.Select(planet.GetSurfaceRadiusAtDirection).ToArray();
            planet.excludeSmallDisconnectedSeas = true;
            rebuild.Invoke(planet, null);
            Assert.That(planet.OceanConnectivity.ExcludedCells, Is.GreaterThan(0), "Fixture must exercise actual excluded basins.");
            for (int cell = 0; cell < topology.CellCount; cell++)
            {
                Assert.That(planet.GetGeodesicCellRawTerrainRadius(cell), Is.EqualTo(beforeRaw[cell]));
                Assert.That(planet.GetGeodesicCellSeafloorRadius(cell), Is.EqualTo(beforeFloor[cell]));
                Assert.That(planet.GetSurfaceRadiusAtDirection(topology.CellDirections[cell]), Is.EqualTo(beforeSurface[cell]));
                if (!planet.IsGeodesicCellPotentialLakeBasin(cell)) continue;
                Assert.That(planet.IsGeodesicCellOcean(cell), Is.False);
                Assert.That(planet.IsDirectionOcean(topology.CellDirections[cell]), Is.False);
                Assert.That(planet.IsOceanAtDirection(topology.CellDirections[cell]), Is.False);
                Assert.That(planet.GetGeodesicCellWaterDepth(cell), Is.Zero);
                Assert.That(planet.GetGeodesicCellBathymetryRegion(cell), Is.EqualTo(GeodesicBathymetryRegion.Land));
                Assert.That(planet.GetGeodesicCellOceanClassification(cell), Is.EqualTo("InlandBasin"));
            }
            var grid = new GeodesicOceanLayerGrid(topology, new GeodesicTransportGraph(topology), planet.OceanConnectivity.OceanMask,
                beforeFloor, planet.GeodesicSeaLevelRadius, 5, new[] { 0f, .2f, .4f, .6f, .8f, 1f });
            for (int cell = 0; cell < topology.CellCount; cell++)
                if (planet.IsGeodesicCellPotentialLakeBasin(cell)) Assert.That(grid.ActiveLayerCountByCell[cell], Is.Zero);
            // Filter state is cached per generation, and a translated/rotated world point maps back correctly.
            obj.transform.SetPositionAndRotation(new Vector3(17f, -6f, 11f), Quaternion.Euler(23f, 71f, -12f));
            for (int cell = 0; cell < topology.CellCount; cell++)
            {
                Vector3 world = obj.transform.TransformPoint(topology.CellDirections[cell] * beforeFloor[cell]);
                Assert.That(planet.IsGeodesicDirectionInOceanMask(obj.transform.InverseTransformPoint(world).normalized), Is.EqualTo(planet.IsGeodesicCellOcean(cell)));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); }
    }
}
