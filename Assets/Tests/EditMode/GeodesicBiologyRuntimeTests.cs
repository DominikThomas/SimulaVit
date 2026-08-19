using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public sealed class GeodesicBiologyRuntimeTests
{
    [TestCase(0, 1f)]
    [TestCase(1, 0.55f)]
    [TestCase(2, 0f)]
    [TestCase(4, 0f)]
    public void PhotosyntheticLightUsesFirstPhaseDepthProfile(int layer, float expected)
    {
        Assert.That(GeodesicBiologyRuntime.ResolvePhotosyntheticLight(1f, layer), Is.EqualTo(expected).Within(1e-6f));
    }

    [Test]
    public void PhotosyntheticLightIsZeroInDarkness()
    {
        Assert.That(GeodesicBiologyRuntime.ResolvePhotosyntheticLight(0f, 0), Is.Zero);
    }

    [Test]
    public void FounderInitializationCopiesFiniteLifecycleAndThermalState()
    {
        var agent = new Replicator(Vector3.up, Quaternion.identity, 45f, Color.cyan, default, 1f, MetabolismType.Methanogenesis);
        GeodesicBiologyRuntime.InitializeFounderBiologicalState(agent, 12, 3, new Vector2(305.15f, 355.15f), 20f, 0.3f, 5f, 0.2f);
        Assert.That(agent.energy, Is.GreaterThan(0f));
        Assert.That(agent.maxLifespan, Is.GreaterThan(0f));
        Assert.That(agent.optimalTempMin, Is.LessThan(agent.optimalTempMax));
        Assert.That(agent.lethalTempMargin, Is.GreaterThan(0f));
    }

    [TestCase(333.15f, 333.15f, 393.15f, 20f, 1f)]
    [TestCase(350f, 333.15f, 393.15f, 20f, 1f)]
    [TestCase(323.15f, 333.15f, 393.15f, 20f, 0.5f)]
    public void TemperaturePerformancePreservesLegacyBandSemantics(float temperature, float min, float max, float margin, float expected)
    {
        Assert.That(GeodesicBiologyRuntime.CalculateTemperaturePerformance(temperature, min, max, margin), Is.EqualTo(expected).Within(1e-6f));
    }

    [Test]
    public void DefaultThermalTraitsCannotMasqueradeAsViable()
    {
        Assert.That(GeodesicBiologyRuntime.CalculateTemperaturePerformance(350f, 0f, 0f, 0f), Is.Zero);
    }

    [TestCase(1.0, 1.0, 0.02, 1.0, 0.001, 0.3, TestName="SulfurChemosynthesisAchievesExtentAndEnergy")]
    [TestCase(1.0, 1.0, 0.01, 1.0, 0.02, 0.0255, TestName="MethanogenesisAchievesExtentAndEnergy")]
    public void SupportedVentReactionAchievesPositiveExtent(double requested, double availableA, double needA, double availableB, double needB, double energyPerExtent)
    {
        double achieved = GeodesicBiologyRuntime.CalculateAchievedExtent(requested, availableA, needA, availableB, needB);
        Assert.That(achieved, Is.GreaterThan(0d));
        Assert.That(achieved * energyPerExtent, Is.GreaterThan(0d));
    }

    [Test]
    public void StarvationAndLifespanRemainDistinctAndRemovalStaysSynchronized()
    {
        Assert.That(GeodesicBiologyRuntime.ClassifyLifecycleDeath(0f, 2f, 10f), Is.EqualTo(DeathCause.EnergyDepletion));
        Assert.That(GeodesicBiologyRuntime.ClassifyLifecycleDeath(1f, 11f, 10f), Is.EqualTo(DeathCause.OldAge));
        var agents = new List<Replicator> { new Replicator(Vector3.up, Quaternion.identity, 10f, Color.white, default, 0f, MetabolismType.SulfurChemosynthesis) };
        var state = new ReplicatorPopulationState(); state.AddAgentFromReplicatorData(agents[0]);
        GeodesicBiologyRuntime.RemoveAgentAtSwapBack(0, agents, state);
        Assert.That(agents, Is.Empty); Assert.That(state.Count, Is.Zero);
    }

    [Test]
    public void SharedHabitatTemperatureStillUsesIndividualThermalTraits()
    {
        const float sharedTemperature = 320f;
        float adapted = GeodesicBiologyRuntime.CalculateTemperaturePerformance(sharedTemperature, 300f, 330f, 20f);
        float stressed = GeodesicBiologyRuntime.CalculateTemperaturePerformance(sharedTemperature, 340f, 360f, 40f);
        Assert.That(adapted, Is.EqualTo(1f));
        Assert.That(stressed, Is.EqualTo(0.5f));
    }

    [Test]
    public void SharedCompetitionIsBoundedProportionalAndTraversalIndependent()
    {
        double[] requests = { 2d, 3d, 5d };
        double factorForward = GeodesicBiologyRuntime.CalculateAvailabilityFactor(4d, requests[0] + requests[1] + requests[2]);
        double factorReverse = GeodesicBiologyRuntime.CalculateAvailabilityFactor(4d, requests[2] + requests[1] + requests[0]);
        Assert.That(factorForward, Is.EqualTo(factorReverse).Within(1e-12));
        double withdrawal = 0d;
        for (int i = 0; i < requests.Length; i++) withdrawal += requests[i] * factorForward;
        Assert.That(withdrawal, Is.EqualTo(4d).Within(1e-12));
        Assert.That(withdrawal, Is.LessThanOrEqualTo(4d));
    }

    [Test]
    public void MultipleMetabolismResourcesKeepIndependentCompetitionFactors()
    {
        double co2 = GeodesicBiologyRuntime.CalculateAvailabilityFactor(5d, 10d);
        double h2s = GeodesicBiologyRuntime.CalculateAvailabilityFactor(9d, 9d);
        double h2 = GeodesicBiologyRuntime.CalculateAvailabilityFactor(2d, 8d);
        Assert.That(Mathf.Min((float)co2, (float)h2s), Is.EqualTo(0.5f));
        Assert.That(Mathf.Min((float)co2, (float)h2), Is.EqualTo(0.25f));
    }

    [Test]
    public void SparseHabitatStampCountsUniqueNodesRatherThanAgents()
    {
        int[] stamps = new int[32];
        int[] agentNodes = { 3, 3, 3, 7, 7, 12, 12, 12, 12 };
        int samples = 0;
        for (int i = 0; i < agentNodes.Length; i++)
            if (GeodesicBiologyRuntime.MarkTouchedNode(agentNodes[i], 4, stamps)) samples++;
        Assert.That(samples, Is.EqualTo(3));
    }

    [Test]
    public void ZeroPopulationPerformsNoHabitatOrAgentEvaluationWork()
    {
        var runtime = new GeodesicBiologyRuntime();
        runtime.Step(0.1f, new List<Replicator>(), new ReplicatorPopulationState(), 0.01f,
            0.02f, 0.001f, 0.3f, 0.01f, 0.02f, 0.03f, 0.01f, 0.01f, 0.04f,
            0.02f, 2f, 10f, 0.1f, true, 0.2f, 0.5f, 2f, 0.5f, 50000, null);
        Assert.That(runtime.BiologySteps, Is.EqualTo(1));
        Assert.That(runtime.AgentEvaluations, Is.Zero);
        Assert.That(runtime.HabitatSamples, Is.Zero);
        Assert.That(runtime.ResourceInventoryReads, Is.Zero);
    }
}
