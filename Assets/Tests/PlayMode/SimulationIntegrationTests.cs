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
    private readonly List<string> capturedExceptions = new List<string>();

    [UnityTest]
    public IEnumerator MainScene_RunForSimulatedTime_RemainsStable()
    {
        Application.logMessageReceived += HandleLog;

        Random.InitState(12345);
        yield return LoadMainScene();
        yield return null;

        ReplicatorManager manager = UnityEngine.Object.FindFirstObjectByType<ReplicatorManager>();
        Assert.That(manager, Is.Not.Null, "Expected a ReplicatorManager in main scene.");

        PlanetGenerator generator = manager.planetGenerator ?? UnityEngine.Object.FindFirstObjectByType<PlanetGenerator>();
        Assert.That(generator, Is.Not.Null, "Expected a PlanetGenerator in main scene.");

        PlanetResourceMap resourceMap = manager.planetResourceMap ?? UnityEngine.Object.FindFirstObjectByType<PlanetResourceMap>();
        Assert.That(resourceMap, Is.Not.Null, "Expected a PlanetResourceMap in main scene.");

        manager.planetGenerator = generator;
        manager.planetResourceMap = resourceMap;
        manager.enableSpontaneousSpawning = false;
        manager.maxPopulation = 250;

        EnsureRenderingReferences(manager);
        EnsureInitialized(manager);

        SetPrivateField(manager, "agents", new List<Replicator>());

        Random.InitState(12345);
        MethodInfo spawnMethod = typeof(ReplicatorManager).GetMethod("SpawnAgentAtRandomLocation", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(spawnMethod, Is.Not.Null, "Expected SpawnAgentAtRandomLocation private method to exist.");

        int spawned = 0;
        for (int i = 0; i < 12; i++)
        {
            bool result = (bool)spawnMethod.Invoke(manager, null);
            if (result)
            {
                spawned++;
            }
        }

        Assert.That(spawned, Is.GreaterThan(0), "Expected at least one deterministic spawn.");

        float simulationSeconds = 10f;
        yield return new WaitForSeconds(simulationSeconds);

        List<Replicator> agents = GetAgents(manager);
        Assert.That(agents.Count, Is.GreaterThan(0), "All agents died unexpectedly during integration test runtime.");
        Assert.That(agents.Count, Is.LessThan(manager.maxPopulation), "Population runaway exceeded cap.");

        AssertAllFinite(resourceMap, "co2", "o2", "organicC", "h2s", "s0", "p", "fe", "si", "ca", "ventStrength", "predatorCue", "preyCue");
        Assert.That(capturedExceptions, Is.Empty, "Exceptions were logged during simulation:\n" + string.Join("\n", capturedExceptions));

        Application.logMessageReceived -= HandleLog;
    }

    private static IEnumerator LoadMainScene()
    {
        AsyncOperation op = SceneManager.LoadSceneAsync("PlanetScene", LoadSceneMode.Single);
        if (op == null)
        {
            op = SceneManager.LoadSceneAsync("Assets/PlanetScene.unity", LoadSceneMode.Single);
        }

        if (op == null)
        {
            op = SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        }

        Assert.That(op, Is.Not.Null, "Could not load PlanetScene/SampleScene for integration testing.");

        while (!op.isDone)
        {
            yield return null;
        }
    }

    private static void EnsureInitialized(ReplicatorManager manager)
    {
        manager.enabled = true;
        MethodInfo startMethod = typeof(ReplicatorManager).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(startMethod, Is.Not.Null);
        startMethod.Invoke(manager, null);
    }

    private static void EnsureRenderingReferences(ReplicatorManager manager)
    {
        if (manager.replicatorMesh != null && manager.replicatorMaterial != null)
        {
            return;
        }

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        MeshFilter meshFilter = temp.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = temp.GetComponent<MeshRenderer>();

        if (manager.replicatorMesh == null && meshFilter != null)
        {
            manager.replicatorMesh = meshFilter.sharedMesh;
        }

        if (manager.replicatorMaterial == null && meshRenderer != null)
        {
            manager.replicatorMaterial = meshRenderer.sharedMaterial;
        }

        UnityEngine.Object.Destroy(temp);
    }

    private static List<Replicator> GetAgents(ReplicatorManager manager)
    {
        FieldInfo field = typeof(ReplicatorManager).GetField("agents", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Expected private agents field on ReplicatorManager.");
        return (List<Replicator>)field.GetValue(manager);
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}'.");
        field.SetValue(instance, value);
    }

    private static void AssertAllFinite(PlanetResourceMap map, params string[] fieldNames)
    {
        Type type = typeof(PlanetResourceMap);
        foreach (string fieldName in fieldNames)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' on PlanetResourceMap.");

            float[] values = field.GetValue(map) as float[];
            Assert.That(values, Is.Not.Null, $"Field '{fieldName}' was null.");

            for (int i = 0; i < values.Length; i++)
            {
                float value = values[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    Assert.Fail($"Field '{fieldName}' has non-finite value at index {i}: {value}");
                }
            }
        }
    }

    private void HandleLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Exception)
        {
            capturedExceptions.Add(condition + "\n" + stackTrace);
        }
    }
}
