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

/// <summary>Explicitly requested seed-283 batch/gradual comparison in a guarded disposable project.</summary>
public sealed class GeodesicFounderEmergenceWorldExperiments
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static readonly string Output=Path.GetFullPath(Path.Combine(Application.dataPath,"../Artifacts/FounderEmergenceAudit"));
    [Serializable] private sealed class Sample {
        public double time;
        public int population;
        public GeodesicBiologyRuntime.FounderEmergenceSnapshot emergence;
    }
    [Serializable] private sealed class Report {
        public string mode, protocol="fixed 1/30 simulated second, production biology + sun/thermal + resources/chemistry";
        public int seed=283, initialCount;
        public float initialDelay, meanInterval;
        public List<Sample> samples=new List<Sample>();
        public GeodesicBiologyRuntime.FounderEmergenceSnapshot final;
        public int finalPopulation;
        public long transportTicks;
        public double oceanTime;
    }
    [UnityTest] [Timeout(1500000)]
    public IEnumerator CompareSeed283BatchAndGradualWithIdenticalBiology() =>
        RunModes(new[]{GeodesicFounderEmergenceMode.Batch,GeodesicFounderEmergenceMode.Gradual});

    [UnityTest] [Timeout(1500000)]
    public IEnumerator RunSeed283GradualOnly() => RunModes(new[]{GeodesicFounderEmergenceMode.Gradual});

    private IEnumerator RunModes(GeodesicFounderEmergenceMode[] modes)
    {
        Assert.That(Application.isBatchMode && Application.dataPath.Replace('\\','/').Contains("/Temp/FoodTaxisAudit/UnityValidation-"),Is.True);
        foreach(var startup in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SimulationStartupController>(true)))
            Assert.That((bool)Get(startup,"saveStartupConfigOnStart"),Is.False);
        yield return new EnterPlayMode();
        Directory.CreateDirectory(Output);
        string saved=File.ReadAllText(Path.Combine(Application.persistentDataPath,"startup_config.json"));
        var selected=SimulationStartupController.DeserializeSavedConfig(saved,new SimulationStartupConfig());
        Scene original=SceneManager.GetActiveScene(); bool originalDirty=original.isDirty;
        UnityEngine.Random.State randomBefore=UnityEngine.Random.state;
        foreach(var mode in modes)
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
                var config = selected.Clone(); config.useRandomSeed = false; config.planetSeed = 283;
                config.geodesicFounderEmergenceMode = mode;
                config.geodesicFounderMeanIntervalSeconds = 10f;
                config.geodesicFounderOngoingEmergence = false;
                config.biology.enableGeodesicChemotaxisABDiagnostics = false;
                Set(startup, "currentConfig", config);
                var stages = (IEnumerable<PlanetGenerationProgress>)Invoke(startup, "ApplyConfigStages", config);
                foreach (var stage in stages)
                {
                    DisableAutomaticUpdates(preview);
                    File.WriteAllText(Path.Combine(Output, "world-progress.txt"), (mode.ToString()+" ") + stage.ToString()); yield return null;
                }
                planet.DiscardGeodesicSnapshot(); // Read caches; do not publish a new generation snapshot.
                Set(resources, "simulationClock", manager); Set(surface, "simulationClock", manager); Set(surface, "sunDirectionProvider", sun);
                Invoke(sun, "Update");
                Assert.That(manager.InitializeForSimulation(true), Is.True);
                DisableAutomaticUpdates(preview);
                var runtime = (GeodesicBiologyRuntime)Get(manager, "geodesicBiology");
                var state = (ReplicatorPopulationState)Get(manager, "populationState");
                var report=new Report { mode=mode.ToString(),initialCount=config.initialSpawnCount,
                    initialDelay=config.geodesicBiologySpawnDelaySeconds,meanInterval=config.geodesicFounderMeanIntervalSeconds };
                var step=pipeline.GetType().GetMethod("RunSimulationStep",Fields);
                var sunUpdate=sun.GetType().GetMethod("Update",Fields);
                var surfaceUpdate=surface.GetType().GetMethod("Update",Fields);
                var resourceUpdate=resources.GetType().GetMethod("Update",Fields);
                object[] dt={1f/30f};
                for(int tick=1;tick<=1200*30;tick++)
                {
                    step.Invoke(pipeline,dt); sunUpdate.Invoke(sun,null); surfaceUpdate.Invoke(surface,null); resourceUpdate.Invoke(resources,null);
                    if(tick%3000==0) {
                        report.samples.Add(new Sample {time=pipeline.SimulationTimeSeconds,population=state.Count,emergence=runtime.CaptureFounderEmergence(state)});
                        File.WriteAllText(Path.Combine(Output,"progress.txt"),mode+" time="+pipeline.SimulationTimeSeconds+" population="+state.Count);
                    }
                    if(tick%100==0) yield return null;
                }
                report.final=runtime.CaptureFounderEmergence(state); report.finalPopulation=state.Count;
                report.transportTicks=resources.CompletedTransportTicks; report.oceanTime=ocean.LastCompletedOceanTemperatureTick;
                Assert.That(report.final.attempts,Is.EqualTo(config.initialSpawnCount));
                Assert.That(report.final.successes+report.final.rejectedHabitat+report.final.rejectedPopulationCap,Is.EqualTo(report.final.attempts));
                Assert.That(state.Count,Is.LessThanOrEqualTo(config.biology.maxPopulation));
                Assert.That(report.transportTicks,Is.GreaterThan(0)); Assert.That(report.oceanTime,Is.GreaterThan(1100));
                Assert.That(pipeline.SimulationTimeSeconds,Is.EqualTo(1200d).Within(.001));
                File.WriteAllText(Path.Combine(Output,mode+".json"),JsonUtility.ToJson(report,true));
                File.WriteAllText(Path.Combine(Output,mode+"-startup.json"),JsonUtility.ToJson(config,true));
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); UnityEngine.Random.state=randomBefore; }
        }
        Assert.That(original.isDirty,Is.EqualTo(originalDirty));
        yield return new ExitPlayMode();
        Assert.That(File.ReadAllText(Path.Combine(Application.persistentDataPath,"startup_config.json")),Is.EqualTo(saved));
    }
    private static void DisableAutomaticUpdates(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true)) if (component != null) component.enabled = false;
    }

    private static object Get(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] values) => target.GetType().GetMethod(name, Fields).Invoke(target, values);
}
