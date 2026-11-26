using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Opt-in deterministic full-domain production-code replay. Opens a preview
/// copy of the scene, never saves a scene/config or changes the user's live world.
/// An explicit update order and fixed step remove rendered-FPS differences. This is
/// not claimed to reproduce either historical variable-frame trajectory bit for bit.</summary>
public sealed class GeodesicFoodWorldExperiments
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts/FoodTaxisAudit"));
    [Serializable] private sealed class Report
    {
        public string mode, protocol = "fixed 1/30s: biology; sun; surface-temperature (ocean event); resource transport/chemistry";
        public int seed = 283, subdivision = 6, peakAmoeboid, finalPopulation;
        public double seconds = 1800d, stepSeconds = 1f / 30f, firstHalfAnchored = -1d;
        public long[] parentBirths, deaths, birthTransitions;
        public int[] finalPhenotypes = new int[4];
        public double[] organismSeconds, viableSeconds = new double[4], unsuitableSeconds = new double[4], h2ConcentrationSeconds = new double[4];
        public double[] sampledOrganismSeconds = new double[4];
        public long resourceTransportTicks;
        public double oceanSeconds;
    }
    [Serializable] private sealed class Neighbourhood
    { public string mode; public double simulationTime, oceanTime; public int seed = 283; public List<Sample> samples = new List<Sample>(); }
    [Serializable] private sealed class Sample
    {
        public int outlet, center, cell, layer, hop;
        public float temperatureK, performance, h2, co2;
        public double volumeKm3, h2Ticks, co2Ticks, readiness;
    }

    [UnityTest]
    [Explicit("Run in a disposable batch project.")]
    [Timeout(1200000)]
    public IEnumerator CompareSeed283WithIdenticalFixedStepsAndOnlySteeringChanged()
    {
        // Normal planet startup owns deferred Destroy calls. Run this fixture in
        // Play Mode in the disposable project rather than changing generation code
        // or suppressing EditMode destruction errors.
        Assert.That(Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/Temp/FoodTaxisAudit/UnityValidation-"),
            Is.True, "Full-world trials must use the disposable batch project, never the user's open Editor.");
        yield return new EnterPlayMode();
        Directory.CreateDirectory(Output);
        string saved = File.ReadAllText(Path.Combine(Application.persistentDataPath, "startup_config.json"));
        var selected = SimulationStartupController.DeserializeSavedConfig(saved, new SimulationStartupConfig());
        Assert.That(selected.planetSeed, Is.EqualTo(283)); Assert.That(selected.geodesicSubdivisionLevel, Is.EqualTo(6));
        Scene original = SceneManager.GetActiveScene(); bool originalDirty = original.isDirty;
        UnityEngine.Random.State randomBefore = UnityEngine.Random.state;
        foreach (bool steering in new[] { false, true })
        {
            Scene preview = EditorSceneManager.OpenPreviewScene("Assets/PlanetScene.unity");
            try
            {
                var components = preview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
                DisableAutomaticUpdates(preview);
                T Find<T>() where T : MonoBehaviour => components.OfType<T>().Single();
                var startup = Find<SimulationStartupController>(); var manager = Find<ReplicatorManager>();
                var pipeline = Find<ReplicatorSimulationPipeline>(); var planet = Find<PlanetGenerator>(); var sun = Find<SunSkyRotator>();
                var surface = Find<GeodesicSurfaceTemperatureField>(); var ocean = Find<GeodesicOceanTemperatureField>(); var resources = Find<GeodesicOceanResourceField>();
                Invoke(planet, "Awake"); Invoke(manager, "Awake"); Invoke(pipeline, "Awake");
                Invoke(resources, "Awake"); Invoke(surface, "Awake"); Invoke(ocean, "Awake");
                // These nonserialized clock/provider references ordinarily resolve by scene
                // search. Bind to the preview's components to prevent cross-world access.
                Set(resources, "simulationClock", manager); Set(surface, "simulationClock", manager); Set(surface, "sunDirectionProvider", sun);
                sun.replicatorManager = manager; sun.rotateSkybox = false;
                Set(sun, "initialRotation", sun.transform.rotation); Invoke(sun, "CacheInitialOrbitForward"); Invoke(sun, "ResolvePhysicalLight");
                var config = selected.Clone(); config.useRandomSeed = false;
                config.biology.enableGeodesicChemotacticSteering = steering; config.biology.enableGeodesicChemotaxisABDiagnostics = false;
                Set(startup, "currentConfig", config);
                var stages = (IEnumerable<PlanetGenerationProgress>)Invoke(startup, "ApplyConfigStages", config);
                foreach (var stage in stages)
                {
                    DisableAutomaticUpdates(preview);
                    File.WriteAllText(Path.Combine(Output, "world-progress.txt"), (steering ? "A " : "B ") + stage.ToString()); yield return null;
                }
                planet.DiscardGeodesicSnapshot(); // Read caches; do not publish a new generation snapshot.
                Set(resources, "simulationClock", manager); Set(surface, "simulationClock", manager); Set(surface, "sunDirectionProvider", sun);
                Invoke(sun, "Update");
                Assert.That(manager.InitializeForSimulation(true), Is.True);
                DisableAutomaticUpdates(preview);
                var runtime = (GeodesicBiologyRuntime)Get(manager, "geodesicBiology");
                var state = (ReplicatorPopulationState)Get(manager, "populationState");
                var report = new Report { mode = steering ? "A" : "B" };
                MethodInfo step = pipeline.GetType().GetMethod("RunSimulationStep", Fields);
                MethodInfo sunUpdate = sun.GetType().GetMethod("Update", Fields), surfaceUpdate = surface.GetType().GetMethod("Update", Fields), resourceUpdate = resources.GetType().GetMethod("Update", Fields);
                object[] dt = { 1f / 30f };
                int steps = 1800 * 30;
                for (int tick = 1; tick <= steps; tick++)
                {
                    step.Invoke(pipeline, dt); sunUpdate.Invoke(sun, null); surfaceUpdate.Invoke(surface, null); resourceUpdate.Invoke(resources, null);
                    if (tick % 30 == 0)
                    {
                        var count = new int[4];
                        for (int i = 0; i < state.Count; i++)
                        {
                            int type = (int)state.Locomotion[i]; count[type]++;
                            report.sampledOrganismSeconds[type]++;
                            int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i];
                            float performance = GeodesicBiologyRuntime.CalculateTemperaturePerformance(ocean.GetLayerTemperatureKelvin(cell, layer),
                                state.OptimalTempMin[i], state.OptimalTempMax[i], state.LethalTempMargin[i], manager.temperatureDormancyFloor);
                            resources.TryGetConcentration(cell, layer, GeodesicOceanResource.H2, out float h2);
                            resources.TryGetConcentration(cell, layer, GeodesicOceanResource.CO2, out float co2);
                            if (performance == 0f) report.unsuitableSeconds[type]++;
                            if (performance > 0f && h2 > 0f && co2 > 0f) report.viableSeconds[type]++;
                            report.h2ConcentrationSeconds[type] += h2;
                        }
                        report.peakAmoeboid = Math.Max(report.peakAmoeboid, count[1]);
                        if (state.Count > 0 && 2 * count[3] >= state.Count && report.firstHalfAnchored < 0d) report.firstHalfAnchored = pipeline.SimulationTimeSeconds;
                        if (tick % 3000 == 0) File.WriteAllText(Path.Combine(Output, "world-progress.txt"), report.mode + " simTime=" + pipeline.SimulationTimeSeconds + " population=" + state.Count);
                    }
                    if (tick == 300 * 30 || tick == 900 * 30) CaptureNeighbourhoods(report.mode, pipeline.SimulationTimeSeconds, resources, ocean, config.biology);
                    if (tick % 100 == 0) yield return null;
                }
                var evolution = (GeodesicLocomotionEvolutionDiagnostics)Get(runtime, "evolutionDiagnostics");
                report.parentBirths = (long[])evolution.ParentBirths.Clone(); report.deaths = (long[])evolution.Deaths.Clone();
                report.birthTransitions = (long[])evolution.BirthTransitions.Clone(); report.organismSeconds = (double[])evolution.OrganismSeconds.Clone();
                report.finalPopulation = state.Count;
                report.resourceTransportTicks = resources.CompletedTransportTicks; report.oceanSeconds = ocean.LastCompletedOceanTemperatureTick;
                for (int i = 0; i < state.Count; i++) report.finalPhenotypes[(int)state.Locomotion[i]]++;
                File.WriteAllText(Path.Combine(Output, "world-" + report.mode + ".json"), JsonUtility.ToJson(report, true));
                File.WriteAllText(Path.Combine(Output, "world-" + report.mode + "-startup.json"), JsonUtility.ToJson(config, true));
                Assert.That(resources.IsInitialized && ocean.IsInitialized, Is.True);
                Assert.That(resources.CompletedTransportTicks, Is.GreaterThan(0));
                Assert.That(ocean.LastCompletedOceanTemperatureTick, Is.GreaterThan(1700d));
                Assert.That(pipeline.SimulationTimeSeconds, Is.EqualTo(1800d).Within(.001d));
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); UnityEngine.Random.state = randomBefore; }
        }
        Assert.That(original.isDirty, Is.EqualTo(originalDirty), "Preview experiments must not dirty or save the user's scene.");
        yield return new ExitPlayMode();
    }

    private static void DisableAutomaticUpdates(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true)) if (component != null) component.enabled = false;
    }

    private static void CaptureNeighbourhoods(string mode, double time, GeodesicOceanResourceField resources,
        GeodesicOceanTemperatureField ocean, SimulationStartupBiologyConfig biology)
    {
        var result = new Neighbourhood { mode = mode, simulationTime = time, oceanTime = ocean.LastCompletedOceanTemperatureTick };
        var grid = resources.SourceGrid; int captured = 0;
        for (int outlet = 0; outlet < resources.CompactOutletCount && captured < 8; outlet++)
        {
            if (!resources.TryGetVentInjectionHabitat(outlet, out var source, out int center, out int layer) || source.Habitat != GeodesicVentHabitat.Submarine) continue;
            captured++;
            var cells = new List<int> { center };
            for (int s = 0; s < grid.SourceTopology.NeighborCounts[center]; s++) cells.Add(grid.SourceTopology.Neighbors6[center * 6 + s]);
            foreach (int cell in cells)
            {
                int bottom = grid.GetBottomLayerIndex(cell);
                for (int selectedLayer = Math.Max(0, bottom - 1); selectedLayer <= bottom; selectedLayer++)
                {
                    if (!grid.IsNodeActive(cell, selectedLayer)) continue;
                    resources.TryGetConcentration(cell, selectedLayer, GeodesicOceanResource.H2, out float h2);
                    resources.TryGetConcentration(cell, selectedLayer, GeodesicOceanResource.CO2, out float co2);
                    float t = ocean.GetLayerTemperatureKelvin(cell, selectedLayer);
                    double volume = grid.PhysicalLayerVolumeKm3[grid.GetNodeIndex(cell, selectedLayer)];
                    float performance = GeodesicBiologyRuntime.CalculateTemperaturePerformance(t, biology.hydrogenTempRange.x,
                        biology.hydrogenTempRange.y, biology.defaultLethalMargin, biology.temperatureDormancyFloor);
                    double ticks = GeodesicFoodSignal.HydrogenotrophyTicks(co2, h2, volume, biology.hydrogenotrophyCO2PerTick, biology.hydrogenotrophyH2PerTick);
                    result.samples.Add(new Sample { outlet = outlet, center = center, cell = cell, layer = selectedLayer, hop = cell == center ? 0 : 1,
                        h2 = h2, co2 = co2, temperatureK = t, performance = performance, volumeKm3 = volume,
                        h2Ticks = h2 * volume / biology.hydrogenotrophyH2PerTick, co2Ticks = co2 * volume / biology.hydrogenotrophyCO2PerTick,
                        readiness = GeodesicFoodSignal.Readiness(ticks, performance, 1f) });
                }
            }
        }
        File.WriteAllText(Path.Combine(Output, "neighbourhood-" + mode + "-" + (int)time + ".json"), JsonUtility.ToJson(result, true));
    }

    private static object Get(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] values) => target.GetType().GetMethod(name, Fields).Invoke(target, values);
}
