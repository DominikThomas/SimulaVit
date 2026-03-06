using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class PerformanceBenchmarks
{
    private const string BenchmarkSceneName = "PerfBenchmarkScene";
    private const int BenchmarkResolution = 32;
    private const int BenchmarkAgentCount = 5000;
    private const int BenchmarkSeed = 424242;
    private const float WarmupSeconds = 5f;
    private const float MeasurementSeconds = 10f;
    private const int MaxMeasurementFrames = 600;

    [Serializable]
    private class PerformanceBaselineCollection
    {
        public List<ScenarioBaseline> scenarios = new List<ScenarioBaseline>();
        public float gcAllocMarginBytes = 64f;
    }

    [Serializable]
    private class ScenarioBaseline
    {
        public string scenario;
        public float avgFrameMs;
        public float p95FrameMs;
        public float gcAllocPerFrame;
        public int expectedAgents;
        public int expectedPredators;
    }

    private struct ScenarioMetrics
    {
        public string Scenario;
        public float AvgFrameMs;
        public float P95FrameMs;
        public float GcAllocPerFrame;
        public int FrameCount;
        public int AgentCount;
        public int PredatorCount;
    }

    [UnityTest]
    public IEnumerator PredatorOff_Benchmark_WithinRegressionGate()
    {
        yield return RunAndValidateScenario("PredatorsOff", enablePredators: false);
    }

    [UnityTest]
    public IEnumerator PredatorOn_Benchmark_WithinRegressionGate()
    {
        yield return RunAndValidateScenario("PredatorsOn", enablePredators: true);
    }

    private IEnumerator RunAndValidateScenario(string scenarioName, bool enablePredators)
    {
        var metrics = new ScenarioMetrics();
        yield return RunScenario(scenarioName, enablePredators, result => metrics = result);

        string baselinePath = GetBaselinePath();
        if (!File.Exists(baselinePath))
        {
            string message =
                $"Performance baseline is missing at '{baselinePath}'.\\n" +
                "Create it intentionally by recording current metrics for PredatorsOff/PredatorsOn scenarios.\\n" +
                "Expected JSON shape is documented in Assets/Tests/Performance/README.md.";

            if (IsCiEnvironment())
            {
                Assert.Fail(message + " CI requires a committed baseline file.");
            }

            Debug.LogWarning(message + " Local run will pass without a baseline.");
            Assert.Pass("Baseline missing locally; regression gate skipped by design.");
        }

        PerformanceBaselineCollection baselines = LoadBaselines(baselinePath);
        ScenarioBaseline baseline = baselines.scenarios.FirstOrDefault(x => x.scenario == scenarioName);
        Assert.That(baseline, Is.Not.Null, $"Missing baseline entry for scenario '{scenarioName}' in {baselinePath}.");

        float avgFrameLimit = baseline.avgFrameMs * 1.15f;
        float gcAllocLimit = baseline.gcAllocPerFrame + baselines.gcAllocMarginBytes;

        Assert.That(metrics.AvgFrameMs, Is.LessThanOrEqualTo(avgFrameLimit),
            $"{scenarioName} avg frame regression: measured={metrics.AvgFrameMs:F3}ms baseline={baseline.avgFrameMs:F3}ms limit={avgFrameLimit:F3}ms");

        Assert.That(metrics.GcAllocPerFrame, Is.LessThanOrEqualTo(gcAllocLimit),
            $"{scenarioName} GC regression: measured={metrics.GcAllocPerFrame:F1}B/frame baseline={baseline.gcAllocPerFrame:F1}B/frame limit={gcAllocLimit:F1}B/frame");

        Debug.Log(
            $"[PERF] {scenarioName} " +
            $"AvgFrameMs={metrics.AvgFrameMs:F3} " +
            $"P95FrameMs={metrics.P95FrameMs:F3} " +
            $"GCAllocPerFrame={metrics.GcAllocPerFrame:F1} " +
            $"Frames={metrics.FrameCount} " +
            $"Agents={metrics.AgentCount} " +
            $"Predators={metrics.PredatorCount}");
    }

    private static IEnumerator RunScenario(string scenarioName, bool enablePredators, Action<ScenarioMetrics> onComplete)
    {
        Time.timeScale = 1f;
        UnityEngine.Random.InitState(BenchmarkSeed);

        Scene testScene = SceneManager.CreateScene(BenchmarkSceneName);
        SceneManager.SetActiveScene(testScene);

        GameObject planetGO = new GameObject("Planet");
        GameObject managerGO = new GameObject("Replicators");

        PlanetGenerator generator = planetGO.AddComponent<PlanetGenerator>();
        PlanetResourceMap resourceMap = planetGO.AddComponent<PlanetResourceMap>();
        ReplicatorManager manager = managerGO.AddComponent<ReplicatorManager>();

        ConfigureScenario(generator, manager, resourceMap, enablePredators);
        EnsureRenderingReferences(manager);

        yield return null;

        float warmupEndTime = Time.realtimeSinceStartup + WarmupSeconds;
        while (Time.realtimeSinceStartup < warmupEndTime)
        {
            yield return null;
        }

        var frameTimesMs = new List<float>(MaxMeasurementFrames);
        long gcAllocTotalBytes = 0;

        using (ProfilerRecorder gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
        {
            float measurementEndTime = Time.realtimeSinceStartup + MeasurementSeconds;
            while (Time.realtimeSinceStartup < measurementEndTime && frameTimesMs.Count < MaxMeasurementFrames)
            {
                yield return null;
                frameTimesMs.Add(Time.unscaledDeltaTime * 1000f);
                gcAllocTotalBytes += gcRecorder.Valid ? gcRecorder.LastValue : 0;
            }
        }

        Assert.That(frameTimesMs.Count, Is.GreaterThan(0), "No measurement frames were collected.");

        float avgFrameMs = frameTimesMs.Average();
        float p95FrameMs = Percentile(frameTimesMs, 0.95f);
        float gcAllocPerFrame = gcAllocTotalBytes / (float)frameTimesMs.Count;

        int predators = GetPredatorCount(manager);
        int totalAgents = GetAgentCount(manager);

        onComplete(new ScenarioMetrics
        {
            Scenario = scenarioName,
            AvgFrameMs = avgFrameMs,
            P95FrameMs = p95FrameMs,
            GcAllocPerFrame = gcAllocPerFrame,
            FrameCount = frameTimesMs.Count,
            AgentCount = totalAgents,
            PredatorCount = predators
        });
    }

    private static void ConfigureScenario(PlanetGenerator generator, ReplicatorManager manager, PlanetResourceMap resourceMap, bool enablePredators)
    {
        generator.randomizeOnStart = false;
        generator.useRandomSeed = false;
        generator.randomSeed = BenchmarkSeed;
        generator.resolution = BenchmarkResolution;
        generator.radius = 1f;
        generator.noiseMagnitude = 0.05f;
        generator.noiseRoughness = 1f;

        manager.planetGenerator = generator;
        manager.planetResourceMap = resourceMap;
        manager.initialSpawnCount = BenchmarkAgentCount;
        manager.maxPopulation = BenchmarkAgentCount;
        manager.enableSpontaneousSpawning = false;
        manager.enableRendering = false;
        manager.showSimulationHud = false;
        manager.enablePredators = enablePredators;
        manager.predatorMutationChance = 0f;
        manager.saprotrophyMutationChance = 0f;
        manager.metabolismMutationChance = 0f;
        manager.locomotionMutationChance = 0f;
    }

    private static int GetAgentCount(ReplicatorManager manager)
    {
        var field = typeof(ReplicatorManager).GetField("agents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
            return 0;
        }

        var agents = field.GetValue(manager) as IList;
        return agents?.Count ?? 0;
    }

    private static int GetPredatorCount(ReplicatorManager manager)
    {
        var field = typeof(ReplicatorManager).GetField("predatorAgentCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
            return 0;
        }

        return (int)field.GetValue(manager);
    }

    private static float Percentile(List<float> values, float percentile)
    {
        if (values == null || values.Count == 0)
        {
            return 0f;
        }

        List<float> sorted = new List<float>(values);
        sorted.Sort();

        float index = Mathf.Clamp01(percentile) * (sorted.Count - 1);
        int lower = Mathf.FloorToInt(index);
        int upper = Mathf.CeilToInt(index);

        if (lower == upper)
        {
            return sorted[lower];
        }

        float t = index - lower;
        return Mathf.Lerp(sorted[lower], sorted[upper], t);
    }

    private static void EnsureRenderingReferences(ReplicatorManager manager)
    {
        if (manager == null)
        {
            return;
        }

        if (manager.replicatorMesh != null && manager.replicatorMaterial != null)
        {
            return;
        }

        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        MeshFilter meshFilter = primitive.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = primitive.GetComponent<MeshRenderer>();

        if (manager.replicatorMesh == null && meshFilter != null)
        {
            manager.replicatorMesh = meshFilter.sharedMesh;
        }

        if (manager.replicatorMaterial == null && meshRenderer != null)
        {
            manager.replicatorMaterial = meshRenderer.sharedMaterial;
        }

        UnityEngine.Object.Destroy(primitive);
    }

    private static string GetBaselinePath()
    {
        return Path.Combine(Application.dataPath, "Tests/Performance/PerformanceBaseline.json");
    }

    private static PerformanceBaselineCollection LoadBaselines(string baselinePath)
    {
        string json = File.ReadAllText(baselinePath);
        PerformanceBaselineCollection baselines = JsonUtility.FromJson<PerformanceBaselineCollection>(json);

        Assert.That(baselines, Is.Not.Null, $"Failed to parse baseline JSON at '{baselinePath}'.");
        Assert.That(baselines.scenarios, Is.Not.Null, $"Baseline JSON has null 'scenarios' at '{baselinePath}'.");

        return baselines;
    }

    private static bool IsCiEnvironment()
    {
        string ci = Environment.GetEnvironmentVariable("CI");
        string githubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");

        return string.Equals(ci, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(githubActions, "true", StringComparison.OrdinalIgnoreCase);
    }
}
