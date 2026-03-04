using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class SimulationIntegrationTests
{
    private readonly List<string> capturedErrors = new List<string>();

    [UnityTest]
    public IEnumerator SceneLess_RunForSimulatedTime_RemainsStable()
    {
        // CI safety: ensure game isn't paused by any accidental timescale changes.
        Time.timeScale = 1f;

        Application.logMessageReceived += HandleLog;

        // Deterministic seed for repeatability
        UnityEngine.Random.InitState(12345);

        // Create a clean test scene (no Build Profiles dependency)
        Scene testScene = SceneManager.CreateScene("CI_TestScene_SimulaVit");
        SceneManager.SetActiveScene(testScene);

        // Root objects
        GameObject planetGO = new GameObject("Planet");
        GameObject managerGO = new GameObject("Replicators");

        // Add core components
        PlanetGenerator generator = planetGO.AddComponent<PlanetGenerator>();
        PlanetResourceMap resourceMap = planetGO.AddComponent<PlanetResourceMap>();
        ReplicatorManager manager = managerGO.AddComponent<ReplicatorManager>();

        Assert.That(generator, Is.Not.Null);
        Assert.That(resourceMap, Is.Not.Null);
        Assert.That(manager, Is.Not.Null);

        // Wire references
        manager.planetGenerator = generator;
        manager.planetResourceMap = resourceMap;

        // Keep the test stable and bounded
        manager.enableSpontaneousSpawning = false;
        manager.maxPopulation = 250;
        manager.enableRendering = false;

        // Provide minimal rendering assets so manager doesn't error if it needs them
        EnsureRenderingReferences(manager);

        // Let Awake run
        yield return null;

        // Try to initialize generator/resourceMap/manager robustly, regardless of how your project sets them up.
        EnsureInitializedComponent(generator);
        EnsureInitializedComponent(resourceMap);
        EnsureInitializedComponent(manager);

        // Some projects keep 'agents' private; reset it to avoid scene state dependence
        SetPrivateFieldIfExists(manager, "agents", new List<Replicator>());

        // Deterministic spawn using your existing private method if present
        UnityEngine.Random.InitState(12345);
        MethodInfo spawnMethod = typeof(ReplicatorManager).GetMethod(
            "SpawnAgentAtRandomLocation",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(spawnMethod, Is.Not.Null,
            "Expected ReplicatorManager private method SpawnAgentAtRandomLocation() to exist.");

        int spawned = 0;
        for (int i = 0; i < 12; i++)
        {
            bool ok = (bool)spawnMethod.Invoke(manager, null);
            if (ok) spawned++;
        }
        Assert.That(spawned, Is.GreaterThan(0), "Expected at least one deterministic spawn.");

        // Run simulation for realtime seconds (independent of Time.timeScale)
        float simulationSeconds = 10f;
        float end = Time.realtimeSinceStartup + simulationSeconds;
        while (Time.realtimeSinceStartup < end)
            yield return null;

        // Validate population is alive and bounded
        List<Replicator> agents = GetAgents(manager);
        Assert.That(agents.Count, Is.GreaterThan(0), "All agents died unexpectedly during integration test runtime.");
        Assert.That(agents.Count, Is.LessThan(manager.maxPopulation), "Population runaway exceeded cap.");

        // Validate key arrays finite (only checks fields that exist)
        AssertAllFiniteIfPresent(resourceMap,
            "co2", "o2", "organicC", "h2s", "s0", "p", "fe", "si", "ca",
            "ventStrength", "predatorCue", "preyCue");

        // Fail on logged errors/exceptions
        Assert.That(capturedErrors, Is.Empty,
            "Errors/exceptions were logged during simulation:\n" + string.Join("\n\n", capturedErrors));

        Application.logMessageReceived -= HandleLog;
    }

    // ---------- Helpers ----------

    private static void EnsureInitializedComponent(Component component)
    {
        if (component == null)
            return;

        // Only Behaviour has 'enabled'
        if (component is Behaviour behaviour)
        {
            behaviour.enabled = true;
        }

        // Prefer explicit Initialize() if present
        MethodInfo init = component.GetType().GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (init != null && init.GetParameters().Length == 0)
        {
            init.Invoke(component, null);
            return;
        }

        // Otherwise call Start() if present
        MethodInfo start = component.GetType().GetMethod(
            "Start",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (start != null && start.GetParameters().Length == 0)
        {
            start.Invoke(component, null);
        }
    }

    private static void EnsureRenderingReferences(ReplicatorManager manager)
    {
        if (manager == null) return;
        if (manager.replicatorMesh != null && manager.replicatorMaterial != null) return;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        MeshFilter mf = temp.GetComponent<MeshFilter>();
        MeshRenderer mr = temp.GetComponent<MeshRenderer>();

        if (manager.replicatorMesh == null && mf != null)
            manager.replicatorMesh = mf.sharedMesh;

        if (manager.replicatorMaterial == null && mr != null)
            manager.replicatorMaterial = mr.sharedMaterial;

        UnityEngine.Object.Destroy(temp);
    }

    private static List<Replicator> GetAgents(ReplicatorManager manager)
    {
        FieldInfo field = typeof(ReplicatorManager).GetField(
            "agents",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(field, Is.Not.Null, "Expected private 'agents' field on ReplicatorManager.");
        return (List<Replicator>)field.GetValue(manager);
    }

    private static void SetPrivateFieldIfExists(object instance, string fieldName, object value)
    {
        if (instance == null) return;

        FieldInfo field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (field != null)
            field.SetValue(instance, value);
    }

    private static void AssertAllFiniteIfPresent(PlanetResourceMap map, params string[] fieldNames)
    {
        Type type = typeof(PlanetResourceMap);

        foreach (string fieldName in fieldNames)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                // Field not present in this build/version -> ignore (CI-friendly across branches)
                continue;
            }

            float[] values = field.GetValue(map) as float[];
            Assert.That(values, Is.Not.Null, $"Field '{fieldName}' was present but null.");

            for (int i = 0; i < values.Length; i++)
            {
                float v = values[i];
                if (float.IsNaN(v) || float.IsInfinity(v))
                    Assert.Fail($"Field '{fieldName}' has non-finite value at index {i}: {v}");
            }
        }
    }

    private void HandleLog(string condition, string stackTrace, LogType type)
    {
        // CI-friendly: treat Errors and Exceptions as failures
        if (type == LogType.Exception || type == LogType.Error)
        {
            capturedErrors.Add($"[{type}] {condition}\n{stackTrace}");
        }
    }
}