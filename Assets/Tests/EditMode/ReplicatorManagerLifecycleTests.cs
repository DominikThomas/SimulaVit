using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ReplicatorManagerLifecycleTests
{
    private GameObject root;
    private ReplicatorManager manager;
    private FieldInfo initializedField;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("ReplicatorManagerLifecycleTests");
        manager = root.AddComponent<ReplicatorManager>();
        initializedField = typeof(ReplicatorManager).GetField("isInitialized", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(initializedField, Is.Not.Null);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
    }

    [Test]
    public void ClearPopulation_DoesNotDeinitializeActiveBiology()
    {
        initializedField.SetValue(manager, true);

        manager.ClearPopulation();

        Assert.That(manager.TotalPopulation, Is.Zero);
        Assert.That(manager.IsInitializedForSimulation, Is.True,
            "An initialized Legacy runtime must remain active when its population reaches zero.");
    }

    [Test]
    public void DeinitializeForStartupMenu_ClearsRuntimeLifecycleAndPopulation()
    {
        initializedField.SetValue(manager, true);

        manager.DeinitializeForStartupMenu();

        Assert.That(manager.TotalPopulation, Is.Zero);
        Assert.That(manager.IsInitializedForSimulation, Is.False);
    }
}
