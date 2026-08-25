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
    public void HydrogenotrophyUsesLegacyReactionBalance()
    {
        const double co2Need = 0.01d;
        const double h2Need = 0.02d;
        const double energyPerExtent = 8d;
        const double storePerExtent = co2Need * 0.8d;
        double achieved = GeodesicBiologyRuntime.CalculateAchievedExtent(1d, co2Need, co2Need, h2Need, h2Need);
        Assert.That(achieved, Is.EqualTo(1d));
        Assert.That(achieved * energyPerExtent, Is.EqualTo(8d));
        Assert.That(achieved * storePerExtent, Is.EqualTo(0.008d).Within(1e-12));
    }

    [Test]
    public void HydrogenotrophyCompetitionIsBoundedAndOrderIndependent()
    {
        const int organismCount = 10;
        const double needPerOrganism = 0.02d;
        const double available = 0.05d;
        double factorForward = GeodesicBiologyRuntime.CalculateAvailabilityFactor(available, organismCount * needPerOrganism);
        double factorReverse = GeodesicBiologyRuntime.CalculateAvailabilityFactor(available, needPerOrganism * organismCount);
        Assert.That(factorForward, Is.EqualTo(factorReverse).Within(1e-12));
        Assert.That(organismCount * needPerOrganism * factorForward, Is.EqualTo(available).Within(1e-12));
    }

    [Test]
    public void HydrogenotrophyTemperatureAndOxygenModifierMatchLegacySemantics()
    {
        Assert.That(GeodesicBiologyRuntime.CalculateTemperaturePerformance(320f, 293.15f, 343.15f, 20f), Is.EqualTo(1f));
        Assert.That(GeodesicBiologyRuntime.CalculateAnaerobeO2Efficiency(0.02f, 0.02f, 0.12f, 0.25f), Is.EqualTo(1f));
        Assert.That(GeodesicBiologyRuntime.CalculateAnaerobeO2Efficiency(0.12f, 0.02f, 0.12f, 0.25f), Is.EqualTo(0.25f));
    }

    [Test]
    public void NormalFounderMetabolismIsOnlyHydrogenotrophy()
    {
        Assert.That(GeodesicBiologyRuntime.NormalFounderMetabolism, Is.EqualTo(MetabolismType.Hydrogenotrophy));
        Assert.That(GeodesicBiologyRuntime.NormalFounderMetabolism, Is.Not.EqualTo(MetabolismType.SulfurChemosynthesis));
        Assert.That(GeodesicBiologyRuntime.NormalFounderMetabolism, Is.Not.EqualTo(MetabolismType.Methanogenesis));
    }

    [Test]
    public void NormalFounderLocomotionIsPassiveDrift()
    {
        Assert.That(GeodesicBiologyRuntime.NormalFounderLocomotion, Is.EqualTo(LocomotionType.PassiveDrift));
        Assert.That(GeodesicBiologyRuntime.NormalFounderLocomotion, Is.Not.EqualTo(LocomotionType.Anchored));
    }

    [TestCase(4, 5, 4)]
    [TestCase(4, 3, 2)]
    [TestCase(1, 5, 1)]
    [TestCase(2, 0, -1)]
    public void HorizontalDriftMapsDepthToAnActiveTargetLayer(int sourceLayer, int activeLayers, int expected)
    {
        Assert.That(GeodesicBiologyRuntime.ResolveHorizontalTargetLayer(sourceLayer, activeLayers), Is.EqualTo(expected));
    }

    [TestCase(2, -1, 5, 1)]
    [TestCase(2, 1, 5, 3)]
    [TestCase(0, -1, 5, 0)]
    [TestCase(4, 1, 5, 4)]
    public void VerticalDriftIsAdjacentAndCannotCrossColumnBounds(int layer, int direction, int activeLayers, int expected)
    {
        int target = GeodesicBiologyRuntime.ResolveAdjacentVerticalLayer(layer, direction, activeLayers);
        Assert.That(target, Is.EqualTo(expected));
        Assert.That(Mathf.Abs(target - layer), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void PassiveKinematicsMovesContinuouslyWithinCurrentCell()
    {
        Vector3 direction = Vector3.up;
        Vector3 tangent = Vector3.right;
        Vector3 before = direction;
        GeodesicBiologyRuntime.AdvancePassiveKinematics(ref direction, ref tangent, 0.05f, 0.5f);
        Assert.That(Vector3.Angle(before, direction), Is.GreaterThan(0f));
        Assert.That(Vector3.Angle(before, direction), Is.LessThan(1f));
        Assert.That(direction.magnitude, Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void PassiveKinematicsIsDeterministicAndHasNoHabitatInputs()
    {
        Vector3 directionA = Vector3.up, directionB = Vector3.up;
        Vector3 tangentA = Vector3.forward, tangentB = Vector3.forward;
        GeodesicBiologyRuntime.AdvancePassiveKinematics(ref directionA, ref tangentA, 0.05f, 2f);
        GeodesicBiologyRuntime.AdvancePassiveKinematics(ref directionB, ref tangentB, 0.05f, 2f);
        Assert.That(directionA, Is.EqualTo(directionB));
        Assert.That(tangentA, Is.EqualTo(tangentB));
    }

    [Test]
    public void InitialPassiveTangentsAreIndependentAndIsotropicWithinOneHabitat()
    {
        Vector3 direction = new Vector3(0.31f, 0.87f, -0.38f).normalized;
        Vector3 sum = Vector3.zero;
        Vector3 first = GeodesicBiologyRuntime.CreateInitialPassiveTangent(direction, 1u);
        int different = 0;
        for (uint seed = 1; seed <= 256; seed++)
        {
            Vector3 tangent = GeodesicBiologyRuntime.CreateInitialPassiveTangent(direction, seed);
            sum += tangent;
            if (Vector3.Dot(first, tangent) < 0.95f) different++;
            Assert.That(Mathf.Abs(Vector3.Dot(direction, tangent)), Is.LessThan(1e-5f));
        }
        Assert.That(different, Is.GreaterThan(200));
        Assert.That((sum / 256f).magnitude, Is.LessThan(0.15f));
    }

    [Test]
    public void WanderIsShortTermCorrelatedSmoothAndLongTermDecorrelating()
    {
        Vector3 direction = Vector3.up;
        Vector3 tangent = GeodesicBiologyRuntime.CreateInitialPassiveTangent(direction, 741u);
        Vector3 initialTangent = tangent;
        float rate = 0f, target = 0f, next = 0f, time = 0f;
        uint sequence = 0;
        float minimumConsecutiveDot = 1f;
        for (int step = 0; step < 1200; step++)
        {
            time += 0.1f;
            if (!(next > 0f) || time >= next)
                GeodesicBiologyRuntime.RefreshPassiveWanderTarget(ref target, ref next, 741u, ref sequence, time);
            rate = GeodesicBiologyRuntime.EvolvePassiveWanderRate(rate, target, 0.1f);
            Vector3 previousTangent = tangent;
            GeodesicBiologyRuntime.AdvancePassiveKinematics(ref direction, ref tangent, rate, 0.1f);
            minimumConsecutiveDot = Mathf.Min(minimumConsecutiveDot, Vector3.Dot(previousTangent, tangent));
        }
        Assert.That(minimumConsecutiveDot, Is.GreaterThan(0.995f));
        Assert.That(Vector3.Dot(initialTangent, tangent), Is.LessThan(0.85f));
        Assert.That(direction.magnitude, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(tangent.magnitude, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(Mathf.Abs(Vector3.Dot(direction, tangent)), Is.LessThan(1e-4f));
    }

    [Test]
    public void BoundaryAuthorityChangesOnlyToARealCloserNeighbor()
    {
        Vector3[] centres = { Vector3.up, new Vector3(0.2f, 0.98f, 0f).normalized, Vector3.right };
        int[] neighbors = { 1, -1, -1, -1, -1, -1, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        byte[] counts = { 1, 1, 0 };
        Assert.That(GeodesicBiologyRuntime.FindCloserRealNeighbor(0, centres[0], centres, neighbors, counts), Is.EqualTo(-1));
        Assert.That(GeodesicBiologyRuntime.FindCloserRealNeighbor(0, centres[1], centres, neighbors, counts), Is.EqualTo(1));
        Assert.That(GeodesicBiologyRuntime.FindCloserRealNeighbor(0, centres[2], centres, neighbors, counts), Is.EqualTo(1));
    }

    [Test]
    public void ContinuousVisualDirectionDoesNotSnapToDestinationCellCentre()
    {
        Vector3[] centres = { Vector3.up, new Vector3(0.2f, 0.98f, 0f).normalized };
        int[] neighbors = { 1, -1, -1, -1, -1, -1, 0, -1, -1, -1, -1, -1 };
        byte[] counts = { 1, 1 };
        Vector3 boundaryDirection = (centres[0] + centres[1] * 1.01f).normalized;
        Assert.That(GeodesicBiologyRuntime.FindCloserRealNeighbor(0, boundaryDirection, centres, neighbors, counts), Is.EqualTo(1));
        Assert.That(boundaryDirection, Is.Not.EqualTo(centres[1]));
    }

    [Test]
    public void LandBoundaryIsLocallyRejected()
    {
        Vector3 ocean = Vector3.up;
        Vector3 land = new Vector3(0.2f, 0.98f, 0f).normalized;
        Vector3 direction = land;
        Vector3 tangent = Vector3.right;
        GeodesicBiologyRuntime.ReflectFromLandBoundary(ref direction, ref tangent, ocean, land);
        Assert.That(Vector3.Dot(direction, ocean), Is.GreaterThanOrEqualTo(Vector3.Dot(direction, land)));
        Assert.That(Mathf.Abs(Vector3.Dot(direction, tangent)), Is.LessThan(1e-5f));
    }

    [Test]
    public void VerticalScheduleIsDeterministicAndFinite()
    {
        float first = GeodesicBiologyRuntime.SampleVerticalInterval(42u, 7u);
        Assert.That(first, Is.EqualTo(GeodesicBiologyRuntime.SampleVerticalInterval(42u, 7u)));
        Assert.That(first, Is.GreaterThan(0f));
        Assert.That(float.IsFinite(first), Is.True);
        Assert.That(GeodesicBiologyRuntime.PassiveVerticalOpportunitiesPerSecond, Is.EqualTo(0.015f));
        double sum = 0d;
        for (uint sequence = 0; sequence < 4096; sequence++)
            sum += GeodesicBiologyRuntime.SampleVerticalInterval(42u, sequence);
        Assert.That(sum / 4096d, Is.InRange(55d, 80d));
    }

    [Test]
    public void SwapBackPreservesPassiveMovementStreamState()
    {
        var agents = new List<Replicator>
        {
            new Replicator(Vector3.up, Quaternion.identity, 10f, Color.white, default, 0.1f, MetabolismType.Hydrogenotrophy),
            new Replicator(Vector3.down, Quaternion.identity, 10f, Color.white, default, 0.2f, MetabolismType.Hydrogenotrophy)
        };
        var state = new ReplicatorPopulationState();
        state.AddAgentFromReplicatorData(agents[0]); state.AddAgentFromReplicatorData(agents[1]);
        state.PassiveMovementSequence[1] = 91u;
        state.PassiveDriftDirection[1] = Vector3.forward;
        state.PassiveDriftTangent[1] = Vector3.right;
        state.PassiveWanderRate[1] = 0.04f;
        state.PassiveTargetWanderRate[1] = -0.08f;
        state.NextPassiveWanderUpdateTime[1] = 4.5f;
        state.PassiveWanderSequence[1] = 17u;
        state.NextPassiveVerticalDriftTime[1] = 12.5f;
        state.PassiveVisualRadius[1] = 8f;
        GeodesicBiologyRuntime.RemoveAgentAtSwapBack(0, agents, state);
        Assert.That(state.Count, Is.EqualTo(1));
        Assert.That(state.PassiveMovementSequence[0], Is.EqualTo(91u));
        Assert.That(state.PassiveDriftDirection[0], Is.EqualTo(Vector3.forward));
        Assert.That(state.PassiveDriftTangent[0], Is.EqualTo(Vector3.right));
        Assert.That(state.PassiveWanderRate[0], Is.EqualTo(0.04f));
        Assert.That(state.PassiveTargetWanderRate[0], Is.EqualTo(-0.08f));
        Assert.That(state.NextPassiveWanderUpdateTime[0], Is.EqualTo(4.5f));
        Assert.That(state.PassiveWanderSequence[0], Is.EqualTo(17u));
        Assert.That(state.NextPassiveVerticalDriftTime[0], Is.EqualTo(12.5f));
        Assert.That(state.PassiveVisualRadius[0], Is.EqualTo(8f));
        Assert.That(state.Locomotion[0], Is.EqualTo(LocomotionType.PassiveDrift));
    }

    [Test]
    public void NewChildPopulationEntryInitializesIndependentWanderState()
    {
        var state = new ReplicatorPopulationState();
        var parent = new Replicator(Vector3.up, Quaternion.identity, 10f, Color.white, default, 0.1f,
            MetabolismType.Hydrogenotrophy, LocomotionType.PassiveDrift);
        var child = new Replicator(Vector3.up, Quaternion.identity, 10f, Color.white, default, 0.9f,
            MetabolismType.Hydrogenotrophy, LocomotionType.PassiveDrift);
        state.AddAgentFromReplicatorData(parent);
        state.PassiveWanderRate[0] = 0.1f;
        state.PassiveWanderSequence[0] = 12u;
        state.AddAgentFromReplicatorData(child);
        Assert.That(state.MovementSeed[1], Is.Not.EqualTo(state.MovementSeed[0]));
        Assert.That(state.PassiveWanderRate[1], Is.Zero);
        Assert.That(state.PassiveWanderSequence[1], Is.Zero);
        Assert.That(state.NextPassiveWanderUpdateTime[1], Is.Zero);
    }

    [Test]
    public void VisualFounderScatterIsDeterministicAndPreservesRadius()
    {
        Vector3 first = GeodesicBiologyRuntime.CalculateVisualFounderPosition(Vector3.up, 9f, 0.1f, 0.5f, 0.25f);
        Vector3 second = GeodesicBiologyRuntime.CalculateVisualFounderPosition(Vector3.up, 9f, 0.1f, 0.5f, 0.25f);
        Vector3 different = GeodesicBiologyRuntime.CalculateVisualFounderPosition(Vector3.up, 9f, 0.1f, 0.8f, 0.75f);
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Is.Not.EqualTo(different));
        Assert.That(first.magnitude, Is.EqualTo(9f).Within(1e-5f));
        Assert.That(Vector3.Distance(first, Vector3.up * 9f), Is.GreaterThan(0f));
        var agent = new Replicator(first, Quaternion.identity, 10f, Color.white, default, 0f, MetabolismType.Hydrogenotrophy);
        GeodesicBiologyRuntime.InitializeFounderBiologicalState(agent, 6, 2, new Vector2(293.15f, 343.15f), 20f, 0.3f, 0f, 0.2f);
        agent.position = different;
        Assert.That(agent.geodesicCellIndex, Is.EqualTo(6));
        Assert.That(agent.currentOceanLayerIndex, Is.EqualTo(2));
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
            0.01f, 0.02f, 8f, 0.8f, true, 0.02f, 0.12f, 0.25f,
            0.02f, 0.001f, 0.3f, 0.01f, 0.02f, 0.03f, 0.01f, 0.01f, 0.04f,
            0.02f, 2f, 10f, 0.1f, true, 0.2f, 0.5f, 2f, 0.5f, 50000, null);
        Assert.That(runtime.BiologySteps, Is.EqualTo(1));
        Assert.That(runtime.AgentEvaluations, Is.Zero);
        Assert.That(runtime.HabitatSamples, Is.Zero);
        Assert.That(runtime.ResourceInventoryReads, Is.Zero);
    }
}
