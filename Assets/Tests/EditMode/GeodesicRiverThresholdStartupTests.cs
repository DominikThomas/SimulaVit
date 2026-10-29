using System.Linq;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class GeodesicRiverThresholdStartupTests
{
    [UnityTest]
    public IEnumerator StartupThresholdsReachFirstBuildAndOnlyChangeRiverQualification()
    {
        // Planet generation owns runtime materials and uses deferred Destroy during rebuilds.
        yield return new EnterPlayMode();
        var obj = new GameObject("River threshold startup fixture");
        obj.SetActive(false);
        try
        {
            var planet = obj.AddComponent<PlanetGenerator>();
            obj.AddComponent<GeodesicOceanLayerDomain>();
            obj.AddComponent<GeodesicOceanResourceField>();
            obj.AddComponent<GeodesicSurfaceTemperatureField>();
            obj.AddComponent<GeodesicOceanTemperatureField>();
            planet.geodesicRenderSubdivisionLevel = 3;
            planet.geodesicColliderSubdivisionLevel = 2;
            planet.geodesicOceanRenderSubdivisionLevel = 2;
            planet.geodesicAtmosphereRenderSubdivisionLevel = 2;
            planet.enableGeodesicRivers = true;
            planet.randomizeOnStart = false;
            typeof(PlanetGenerator).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(planet, null);
            var controller = obj.AddComponent<SimulationStartupController>();
            typeof(SimulationStartupController).GetField("planetGenerator", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, planet);
            var apply = typeof(SimulationStartupController).GetMethod("ApplyConfig", BindingFlags.Instance | BindingFlags.NonPublic);
            var config = new SimulationStartupConfig
            {
                planetSeed = 123456, useRandomSeed = false,
                gridType = PlanetGridType.GeodesicIcosphere, geodesicSubdivisionLevel = 3,
                majorRiverThreshold = 90.25f
            };
            Vector3[] terrain = null, gradients = null;
            int[] receivers = null;
            double[] runoff = null;
            bool[] ocean = null, previousQualified = null;
            int previousCount = int.MaxValue, firstCount = -1;
            foreach (float threshold in new[] { 2f, 8f, 32f })
            {
                config.riverFlowThreshold = threshold;
                apply.Invoke(controller, new object[] { config });
                var rivers = obj.GetComponent<GeodesicRiverSystem>();
                Assert.That(rivers.riverFlowThreshold, Is.EqualTo(threshold));
                Assert.That(rivers.majorRiverThreshold, Is.EqualTo(90.25f));
                var tin = rivers.TinDrainage;
                Assert.That(tin, Is.Not.Null, "Hydrology must already be built using the startup values.");
                var currentTerrain = obj.GetComponent<MeshFilter>().sharedMesh.vertices;
                var currentGradients = Enumerable.Range(0, tin.TriangleCount).Select(tin.DownhillGradient).ToArray();
                var currentOcean = Enumerable.Range(0, tin.TriangleCount).Select(t => tin.IsOcean(tin.Centroid(t))).ToArray();
                var qualified = Enumerable.Range(0, tin.TriangleCount)
                    .Select(t => !currentOcean[t] && tin.AccumulatedRunoff[t] >= rivers.ResolvedDrainageAreaThreshold).ToArray();
                int count = qualified.Count(q => q);
                Assert.That(tin.Diagnostics.RiverTriangles, Is.EqualTo(count));
                Assert.That(count, Is.LessThanOrEqualTo(previousCount));
                if (terrain != null)
                {
                    CollectionAssert.AreEqual(terrain, currentTerrain);
                    CollectionAssert.AreEqual(receivers, tin.TriangleReceiver);
                    CollectionAssert.AreEqual(gradients, currentGradients);
                    CollectionAssert.AreEqual(runoff, tin.AccumulatedRunoff);
                    CollectionAssert.AreEqual(ocean, currentOcean);
                    for (int t = 0; t < qualified.Length; t++)
                        if (qualified[t]) Assert.That(previousQualified[t], Is.True, "Higher thresholds must select a subset.");
                }
                else
                {
                    firstCount = count;
                    Assert.That(currentOcean.Any(o => o), Is.True, "Exercise ocean classification too.");
                    Assert.That(currentOcean.Any(o => !o), Is.True);
                }
                TestContext.WriteLine($"seed=123456 threshold={threshold} qualifyingTriangles={count} networks={tin.Networks.Count}");
                terrain = currentTerrain; receivers = tin.TriangleReceiver.ToArray(); gradients = currentGradients;
                runoff = tin.AccumulatedRunoff.ToArray(); ocean = currentOcean;
                previousQualified = qualified; previousCount = count;
            }
            Assert.That(previousCount, Is.LessThan(firstCount), "Fixture must exercise a visible threshold difference.");
        }
        finally { Object.DestroyImmediate(obj); }
        yield return new ExitPlayMode();
    }
}
