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
            "An initialized biology manager must remain active when its population reaches zero.");
    }

    [Test]
    public void DeferredComponentStart_PreparesWithoutInitializingBiology()
    {
        manager.autoStartOnSceneLoad = false;
        MethodInfo startMethod = typeof(ReplicatorManager).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(startMethod, Is.Not.Null);

        startMethod.Invoke(manager, null);

        Assert.That(manager.IsInitializedForSimulation, Is.False,
            "Waiting at startup selection must not activate biology before a world mode is chosen.");
    }

    [Test]
    public void DeinitializeForStartupMenu_ClearsRuntimeLifecycleAndPopulation()
    {
        initializedField.SetValue(manager, true);

        manager.DeinitializeForStartupMenu();

        Assert.That(manager.TotalPopulation, Is.Zero);
        Assert.That(manager.IsInitializedForSimulation, Is.False);
    }

    [Test]
    public void WorldClock_RemainsRunnableAcrossRepeatedBiologyDeinitialization()
    {
        var planet = root.AddComponent<PlanetGenerator>();
        planet.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, 10, 2);
        manager.planetGenerator = planet;
        var pipeline = root.AddComponent<ReplicatorSimulationPipeline>();
        // EditMode does not call the manager's runtime Awake that creates and binds the pipeline.
        var managerField = typeof(ReplicatorSimulationPipeline).GetField("replicatorManager",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(managerField, Is.Not.Null);
        managerField.SetValue(pipeline, manager);

        for (int generation = 0; generation < 3; generation++)
        {
            initializedField.SetValue(manager, true);
            manager.DeinitializeForStartupMenu();
            pipeline.ResetClockForNewSimulation();
            pipeline.SetSimulationStepsPerFrame(1);

            // ResetClock deliberately discards one rendered delta, just as startup does.
            pipeline.RunFrame();
            Assert.That(pipeline.SimulationTimeSeconds, Is.Zero);
            pipeline.RunFrame();

            Assert.That(manager.IsInitializedForSimulation, Is.False);
            Assert.That(pipeline.ShouldAdvanceSimulation, Is.True,
                $"World timing stopped after biology cleanup on generation {generation}.");
            double expectedAdvance = ReplicatorSimulationPipeline.CalculateAuthoritativeFrameAdvance(
                pipeline.RawRenderedFrameDelta, 1f, pipeline.MaximumSimulationStepDeltaSeconds);
            Assert.That(pipeline.SimulationTimeSeconds, Is.EqualTo(expectedAdvance).Within(1e-8d));
            Assert.That(pipeline.SimulationStepsExecutedThisFrame, Is.Zero);
            Assert.That(manager.SimulationStepCount, Is.Zero,
                "An uninitialized biology runtime must not execute biological steps.");
        }
    }
}
