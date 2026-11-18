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
public sealed class GeodesicSulfurWorldExperiments
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts/SulfurAudit"));
    [Serializable] private sealed class Report
    {
        public string mode, protocol = "fixed 1/30s: biology; sun; surface-temperature (ocean event); resource transport/chemistry";
        public int seed = 283, subdivision = 6, peakAmoeboid, finalPopulation, peakSulfur;
        public int[] finalMetabolisms = new int[8];
        public int[] sulfurLocomotion = new int[4];
        public long[] sulfurParentBirths;
        public double sulfurSampleSeconds, sulfurViableSampleSeconds, sulfurZeroPerformanceSamples, sulfurH2SConcentrationSum;
        public long sulfurO2Samples, sulfurInhibitedSamples, sulfurPrecisionRejected;
        public double sulfurMeanO2Efficiency;
        public long mutationAttempts, mutationsAllowed, mutationsBlocked, sulfurBirths, sulfurDeaths;
        public double sulfurExtent, sulfurCO2, sulfurH2S, sulfurEnergy, sulfurCarbon, sulfurS0;
        public double h2sInventory, suspendedS0, sedimentS0, abioticS0, abioticFeS;
        public double seconds = 900d, stepSeconds = 1f / 30f, firstHalfAnchored = -1d;
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
        public float temperatureK, performance, sulfurPerformance, h2, h2s, co2, o2, sulfurOxygenEfficiency;
        public double volumeKm3, h2Ticks, co2Ticks, readiness, sulfurH2STicks, sulfurCO2Ticks, sulfurReadiness;
    }

    [UnityTest]
    [Timeout(1500000)]
    public IEnumerator CompareSeed283SulfurRichWithOnlySulfurMutationChanged()
    {
        // Normal planet startup owns deferred Destroy calls. Run this fixture in
        // Play Mode in the disposable project rather than changing generation code
        // or suppressing EditMode destruction errors.
        Assert.That(Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/Temp/FoodTaxisAudit/UnityValidation-"),
            Is.True, "Full-world trials must use the disposable batch project, never the user's open Editor.");
        foreach (var startup in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SimulationStartupController>(true)))
            Assert.That((bool)Get(startup, "saveStartupConfigOnStart"), Is.False, "Use Tools/ValidateSulfur.ps1 to disable the quit-time draft writer in the disposable scene.");
        yield return new EnterPlayMode();
        Directory.CreateDirectory(Output);
        string saved = File.ReadAllText(Path.Combine(Application.persistentDataPath, "startup_config.json"));
        File.WriteAllText(Path.Combine(Output,"pre-trial-saved-startup.json"),saved);
        var selected = SimulationStartupController.DeserializeSavedConfig(saved, new SimulationStartupConfig());
        Assert.That(selected.planetSeed, Is.EqualTo(283)); Assert.That(selected.geodesicSubdivisionLevel, Is.EqualTo(6));
        Scene original = SceneManager.GetActiveScene(); bool originalDirty = original.isDirty;
        UnityEngine.Random.State randomBefore = UnityEngine.Random.state;
        foreach (bool sulfur in new[] { false, true })
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
                config.geodesicVentH2SPhysicalPerSecond = 2f;
                if (!sulfur) config.biology.hydrogenToSulfurMutationChance = 0f; config.biology.enableGeodesicChemotaxisABDiagnostics = false;
                Set(startup, "currentConfig", config);
                var stages = (IEnumerable<PlanetGenerationProgress>)Invoke(startup, "ApplyConfigStages", config);
                foreach (var stage in stages)
                {
                    DisableAutomaticUpdates(preview);
                    File.WriteAllText(Path.Combine(Output, "world-progress.txt"), (sulfur ? "sulfur-on " : "sulfur-off ") + stage.ToString()); yield return null;
                }
                planet.DiscardGeodesicSnapshot(); // Read caches; do not publish a new generation snapshot.
                Set(resources, "simulationClock", manager); Set(surface, "simulationClock", manager); Set(surface, "sunDirectionProvider", sun);
                Invoke(sun, "Update");
                Assert.That(manager.InitializeForSimulation(true), Is.True);
                DisableAutomaticUpdates(preview);
                var runtime = (GeodesicBiologyRuntime)Get(manager, "geodesicBiology");
                var state = (ReplicatorPopulationState)Get(manager, "populationState");
                var report = new Report { mode = sulfur ? "sulfur-on" : "sulfur-off" };
                MethodInfo step = pipeline.GetType().GetMethod("RunSimulationStep", Fields);
                MethodInfo sunUpdate = sun.GetType().GetMethod("Update", Fields), surfaceUpdate = surface.GetType().GetMethod("Update", Fields), resourceUpdate = resources.GetType().GetMethod("Update", Fields);
                object[] dt = { 1f / 30f };
                int steps = 900 * 30;
                for (int tick = 1; tick <= steps; tick++)
                {
                    step.Invoke(pipeline, dt); sunUpdate.Invoke(sun, null); surfaceUpdate.Invoke(surface, null); resourceUpdate.Invoke(resources, null);
                    if (tick % 30 == 0)
                    {
                        var count = new int[4]; int sulfurCount = 0;
                        for (int i = 0; i < state.Count; i++)
                        {
                            int type = (int)state.Locomotion[i]; count[type]++;
                            if (state.Metabolism[i] == MetabolismType.SulfurChemosynthesis) sulfurCount++;
                            report.sampledOrganismSeconds[type]++;
                            int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i];
                            float performance = GeodesicBiologyRuntime.CalculateTemperaturePerformance(ocean.GetLayerTemperatureKelvin(cell, layer),
                                state.OptimalTempMin[i], state.OptimalTempMax[i], state.LethalTempMargin[i], manager.temperatureDormancyFloor);
                            resources.TryGetConcentration(cell, layer, GeodesicOceanResource.H2, out float h2);
                            resources.TryGetConcentration(cell, layer, GeodesicOceanResource.CO2, out float co2);
                            if (performance == 0f) report.unsuitableSeconds[type]++;
                            bool sulfurAgent = state.Metabolism[i] == MetabolismType.SulfurChemosynthesis;
                            resources.TryGetConcentration(cell, layer, GeodesicOceanResource.H2S, out float h2s);
                            if (performance > 0f && (sulfurAgent ? h2s : h2) > 0f && co2 > 0f) report.viableSeconds[type]++;
                            if (sulfurAgent) {
                                report.sulfurSampleSeconds++; report.sulfurH2SConcentrationSum += h2s;
                                if (performance > 0f && h2s > 0f && co2 > 0f) report.sulfurViableSampleSeconds++;
                                if (performance == 0f) report.sulfurZeroPerformanceSamples++;
                            }
                            report.h2ConcentrationSeconds[type] += h2;
                        }
                        report.peakSulfur = Math.Max(report.peakSulfur, sulfurCount);
                        report.peakAmoeboid = Math.Max(report.peakAmoeboid, count[1]);
                        if (state.Count > 0 && 2 * count[3] >= state.Count && report.firstHalfAnchored < 0d) report.firstHalfAnchored = pipeline.SimulationTimeSeconds;
                        if (tick % 3000 == 0) File.WriteAllText(Path.Combine(Output, "world-progress.txt"), report.mode + " simTime=" + pipeline.SimulationTimeSeconds + " population=" + state.Count);
                    }
                    if (tick == 300 * 30 || tick == 900 * 30) CaptureNeighbourhoods(report.mode, pipeline.SimulationTimeSeconds, resources, ocean, config.biology, manager);
                    if (tick % 100 == 0) yield return null;
                }
                var evolution = (GeodesicLocomotionEvolutionDiagnostics)Get(runtime, "evolutionDiagnostics");
                report.parentBirths = (long[])evolution.ParentBirths.Clone(); report.deaths = (long[])evolution.Deaths.Clone();
                report.birthTransitions = (long[])evolution.BirthTransitions.Clone(); report.organismSeconds = (double[])evolution.OrganismSeconds.Clone();
                report.finalPopulation = state.Count;
                report.resourceTransportTicks = resources.CompletedTransportTicks; report.oceanSeconds = ocean.LastCompletedOceanTemperatureTick;
                for (int i = 0; i < state.Count; i++) {
                    report.finalPhenotypes[(int)state.Locomotion[i]]++;
                    report.finalMetabolisms[(int)state.Metabolism[i]]++;
                    if (state.Metabolism[i] == MetabolismType.SulfurChemosynthesis) report.sulfurLocomotion[(int)state.Locomotion[i]]++;
                }
                report.sulfurO2Samples = (long)Get(runtime,"sulfurO2Samples"); report.sulfurInhibitedSamples = (long)Get(runtime,"sulfurInhibitedSamples");
                report.sulfurMeanO2Efficiency = (double)Get(runtime,"sulfurEfficiencySum") / Math.Max(1,report.sulfurO2Samples);
                report.sulfurPrecisionRejected = (long)Get(runtime,"sulfurPrecisionRejected");
                report.sulfurParentBirths = (long[])((long[])Get(runtime,"sulfurParentBirths")).Clone();
                report.mutationAttempts = (long)Get(runtime, "sulfurMutationAttempts");
                report.mutationsAllowed = (long)Get(runtime, "sulfurMutationsAllowed");
                report.mutationsBlocked = (long)Get(runtime, "sulfurMutationsBlocked");
                report.sulfurBirths = (long)Get(runtime, "sulfurBirths"); report.sulfurDeaths = (long)Get(runtime, "sulfurDeaths");
                report.sulfurExtent = ((double[])Get(runtime, "extentByMetabolism"))[(int)MetabolismType.SulfurChemosynthesis];
                report.sulfurEnergy = ((double[])Get(runtime, "energyByMetabolism"))[(int)MetabolismType.SulfurChemosynthesis];
                report.sulfurCO2 = (double)Get(runtime, "sulfurCO2Consumed"); report.sulfurH2S = (double)Get(runtime, "sulfurH2SConsumed");
                report.sulfurCarbon = (double)Get(runtime, "sulfurCarbonFixed"); report.sulfurS0 = (double)Get(runtime, "sulfurProduced");
                var chemistry = planet.GetComponent<GeodesicAbioticChemistry>();
                if (chemistry != null) { report.abioticS0 = (double)Get(chemistry, "precipitatedS0"); report.abioticFeS = (double)Get(chemistry, "precipitatedFeS"); }
                var minerals = resources.SuspendedMinerals;
                var sediment = planet.GetComponent<GeodesicOceanSedimentField>();
                for (int cell = 0; cell < resources.SourceGrid.CellCount; cell++) {
                    report.sedimentS0 += sediment.GetElementalSulfurInventory(cell);
                    for (int layer = 0; layer < resources.SourceGrid.ActiveLayerCountByCell[cell]; layer++) {
                        report.h2sInventory += resources.GetNodeInventory(cell, layer, GeodesicOceanResource.H2S);
                        report.suspendedS0 += minerals.GetInventory(cell, layer, GeodesicSuspendedMineral.S0);
                    }
                }
                Assert.That(report.sulfurS0, Is.EqualTo(report.sulfurH2S));
                Assert.That(Math.Abs(report.sulfurCO2 - report.sulfurCarbon), Is.LessThan(1e-5));
                File.WriteAllText(Path.Combine(Output, "world-" + report.mode + ".json"), JsonUtility.ToJson(report, true));
                File.WriteAllText(Path.Combine(Output, "world-" + report.mode + "-startup.json"), JsonUtility.ToJson(config, true));
                Assert.That(resources.IsInitialized && ocean.IsInitialized, Is.True);
                Assert.That(resources.CompletedTransportTicks, Is.GreaterThan(0));
                Assert.That(ocean.LastCompletedOceanTemperatureTick, Is.GreaterThan(800d));
                Assert.That(pipeline.SimulationTimeSeconds, Is.EqualTo(900d).Within(.001d));
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); UnityEngine.Random.state = randomBefore; }
        }
        Assert.That(original.isDirty, Is.EqualTo(originalDirty), "Preview experiments must not dirty or save the user's scene.");
        yield return new ExitPlayMode();
        Assert.That(File.ReadAllText(Path.Combine(Application.persistentDataPath, "startup_config.json")), Is.EqualTo(saved), "No saved startup settings may change during a world fixture.");
    }

    private static void DisableAutomaticUpdates(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true)) if (component != null) component.enabled = false;
    }

    private static void CaptureNeighbourhoods(string mode, double time, GeodesicOceanResourceField resources,
        GeodesicOceanTemperatureField ocean, SimulationStartupBiologyConfig biology, ReplicatorManager manager)
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
                    resources.TryGetConcentration(cell, selectedLayer, GeodesicOceanResource.H2S, out float h2s);
                    resources.TryGetConcentration(cell, selectedLayer, GeodesicOceanResource.O2, out float o2);
                    float t = ocean.GetLayerTemperatureKelvin(cell, selectedLayer);
                    double volume = grid.PhysicalLayerVolumeKm3[grid.GetNodeIndex(cell, selectedLayer)];
                    float performance = GeodesicBiologyRuntime.CalculateTemperaturePerformance(t, biology.hydrogenTempRange.x,
                        biology.hydrogenTempRange.y, biology.defaultLethalMargin, biology.temperatureDormancyFloor);
                    double ticks = GeodesicFoodSignal.HydrogenotrophyTicks(co2, h2, volume, biology.hydrogenotrophyCO2PerTick, biology.hydrogenotrophyH2PerTick);
                    float sulfurPerformance = GeodesicBiologyRuntime.CalculateTemperaturePerformance(t, biology.sulfurChemoTempRange.x,
                        biology.sulfurChemoTempRange.y, biology.defaultLethalMargin, biology.temperatureDormancyFloor);
                    float sulfurOxygen = manager.anaerobeO2InhibitionEnabled && manager.anaerobeO2InhibitionAffectsSulfurChemosynthesis
                        ? GeodesicBiologyRuntime.CalculateAnaerobeO2Efficiency(o2,manager.anaerobeO2ComfortMax,manager.anaerobeO2StressMax,manager.anaerobeO2MinEfficiencySulfurChemosynthesis) : 1f;
                    double sulfurTicks = biology.chemosynthStoreFraction > 0f ? GeodesicFoodSignal.SulfurChemosynthesisTicks(co2,h2s,volume,biology.chemosynthesisCo2NeedPerTick,biology.chemosynthesisH2sNeedPerTick) : 0d;
                    result.samples.Add(new Sample { outlet = outlet, center = center, cell = cell, layer = selectedLayer, hop = cell == center ? 0 : 1,
                        h2 = h2, h2s = h2s, co2 = co2, o2 = o2, sulfurPerformance = sulfurPerformance, sulfurOxygenEfficiency = sulfurOxygen,
                        sulfurH2STicks = h2s * volume / biology.chemosynthesisH2sNeedPerTick, sulfurCO2Ticks = co2 * volume / biology.chemosynthesisCo2NeedPerTick,
                        sulfurReadiness = GeodesicFoodSignal.Readiness(sulfurTicks,sulfurPerformance,sulfurOxygen), temperatureK = t, performance = performance, volumeKm3 = volume,
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
