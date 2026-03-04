using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class TemperatureFitnessTests
{
    private GameObject root;
    private ReplicatorManager manager;
    private MethodInfo computeTemperatureFitness;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("TemperatureFitnessTests");
        manager = root.AddComponent<ReplicatorManager>();
        computeTemperatureFitness = typeof(ReplicatorManager)
            .GetMethod("ComputeTemperatureFitness", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(computeTemperatureFitness, Is.Not.Null, "Expected ReplicatorManager.ComputeTemperatureFitness to exist.");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
    }

    [Test]
    public void ComputeTemperatureFitness_IsOneInsideOptimalBand()
    {
        Replicator agent = CreateAgent(0.4f, 0.8f, 0.3f);

        float fitnessAtMin = Compute(agent, 0.4f);
        float fitnessAtCenter = Compute(agent, 0.6f);
        float fitnessAtMax = Compute(agent, 0.8f);

        Assert.That(fitnessAtMin, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(fitnessAtCenter, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(fitnessAtMax, Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void ComputeTemperatureFitness_DecreasesOutsideBand_AndReachesZeroBeyondLethalMargin()
    {
        Replicator agent = CreateAgent(0.4f, 0.8f, 0.3f);

        float justOutside = Compute(agent, 0.9f);
        float farOutside = Compute(agent, 1.3f);

        Assert.That(justOutside, Is.GreaterThan(0f));
        Assert.That(justOutside, Is.LessThan(1f));
        Assert.That(farOutside, Is.EqualTo(0f).Within(1e-5f));
    }

    private float Compute(Replicator agent, float temperature)
    {
        return (float)computeTemperatureFitness.Invoke(manager, new object[] { agent, temperature });
    }

    private static Replicator CreateAgent(float optimalMin, float optimalMax, float lethalMargin)
    {
        var traits = new Replicator.Traits(false, false, false, 1f);
        var agent = new Replicator(Vector3.up, Quaternion.identity, 30f, Color.white, traits, 0f, MetabolismType.SulfurChemosynthesis);
        agent.optimalTempMin = optimalMin;
        agent.optimalTempMax = optimalMax;
        agent.lethalTempMargin = lethalMargin;
        return agent;
    }
}
