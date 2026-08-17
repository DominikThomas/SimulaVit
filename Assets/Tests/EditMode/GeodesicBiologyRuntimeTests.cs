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
        Assert.Multiple(() => { Assert.That(agent.energy, Is.EqualTo(0.3f)); Assert.That(agent.maxLifespan, Is.EqualTo(45f)); Assert.That(agent.optimalTempMin, Is.EqualTo(305.15f)); Assert.That(agent.optimalTempMax, Is.EqualTo(355.15f)); Assert.That(agent.lethalTempMargin, Is.EqualTo(20f)); Assert.That(agent.biomassTarget, Is.EqualTo(0.2f)); Assert.That(agent.geodesicCellIndex, Is.EqualTo(12)); Assert.That(agent.currentOceanLayerIndex, Is.EqualTo(3)); });
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
}
