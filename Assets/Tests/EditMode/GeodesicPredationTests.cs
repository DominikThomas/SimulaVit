using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicPredationTests
{
    [Test]
    public void TwoPredatorsConsumeOnePreyOnceWithoutOrdinaryRecycling()
    {
        using var f = new Fixture();
        f.Add(MetabolismType.Predation, 0f);
        long prey = f.Add(MetabolismType.Hydrogenotrophy, 2f);
        long otherPredator = f.Add(MetabolismType.Predation, 0f);
        f.Step();
        Assert.That(f.Deaths, Is.EqualTo(new[] { DeathCause.Predation }));
        Assert.That(f.Agents.Exists(agent => agent.DiagnosticId == prey), Is.False);
        Assert.That(f.Agents[1].DiagnosticId, Is.EqualTo(otherPredator));
        Assert.That(f.State.OrganicCStore[0], Is.EqualTo(1f));
        Assert.That(f.State.OrganicCStore[1], Is.Zero);
        Assert.That(f.State.Energy[0], Is.EqualTo(4f));
        Assert.That(f.State.Energy[1], Is.EqualTo(1f));
        Assert.That(f.Inventory(GeodesicOceanResource.CO2), Is.EqualTo(1d));
        Assert.That(f.Inventory(GeodesicOceanResource.OrganicC), Is.Zero);
        Assert.That(f.Inventory(GeodesicOceanResource.O2), Is.EqualTo(3d));
        f.Step();
        Assert.That(f.Deaths.Count, Is.EqualTo(1));
        Assert.That(f.Runtime.PredationDiagnostics.captures, Is.EqualTo(1L));
        Assert.That(f.Inventory(GeodesicOceanResource.CO2), Is.EqualTo(1d));
    }

    [Test]
    public void FullStoreAndLimitedOxygenSendUnusedCarbonToOrganicPool()
    {
        Assert.That(GeodesicOceanResourceField.TryPlanPredation(2d, .5f, 1f, 3f, 1f, 1f, 1f,
            2d, 4d, .125d, 0d, 0d, out var transfer), Is.True);
        Assert.That(transfer.storeAfter, Is.EqualTo(1f));
        Assert.That(transfer.oxygenConsumed, Is.EqualTo(.25d));
        Assert.That(transfer.co2Produced, Is.EqualTo(.25d));
        Assert.That(transfer.organicReleased, Is.EqualTo(1.75d));
        Assert.That(transfer.organicAfter, Is.EqualTo(1.75d / 4d));
        Assert.That(transfer.energyAfter, Is.EqualTo(1.75f));
        Assert.That(GeodesicOceanResourceField.TryPlanPredation(2d, .5f, 1f, 3f, 1f, 0f, 1f,
            2d, 4d, 0d, 0d, 0d, out _), Is.False);
    }

    [Test]
    public void SearchUsesContinuousDistanceAcrossCellsAndLayersAndFollowsSwapBack()
    {
        using var f = new Fixture();
        f.Add(MetabolismType.Predation, 0f);
        f.Add(MetabolismType.Hydrogenotrophy, 1f);
        long near = f.Add(MetabolismType.Hydrogenotrophy, 1f, f.Grid.SourceTopology.Neighbors6[0], 1);
        f.State.Position[0] = new Vector3(.99f, 0f, 7.8f);
        f.State.Position[1] = new Vector3(1.23f, .24f, 7.8f); // Same resource cell and spatial neighborhood, outside the sphere.
        f.State.Position[2] = new Vector3(1.01f, 0f, 7.78f);
        var index = new GeodesicPreyIndex();
        index.Build(f.Agents, f.State, f.Grid, .25f);
        Assert.That(index.FindPrey(f.State.Position[0], 0, 0, 1, f.Agents, f.State, f.Grid, out int examined), Is.EqualTo(-1));
        Assert.That(examined, Is.EqualTo(1));
        Assert.That(index.FindPrey(f.State.Position[0], 0, 0, 2, f.Agents, f.State, f.Grid, out _), Is.EqualTo(2));

        index.RemoveAtSwapBack(1, 2);
        GeodesicBiologyRuntime.RemoveAgentAtSwapBack(1, f.Agents, f.State);
        Assert.That(index.FindPrey(f.State.Position[0], 0, 1, 2, f.Agents, f.State, f.Grid, out _), Is.EqualTo(1));
        Assert.That(f.Agents[1].DiagnosticId, Is.EqualTo(near));
        index.MarkConsumed(1);
        Assert.That(index.IsConsumed(1, near), Is.True);
        Assert.That(index.IsConsumed(1, f.Agents[0].DiagnosticId), Is.False);
        Assert.That(index.FindPrey(f.State.Position[0], 0, 1, 2, f.Agents, f.State, f.Grid, out _), Is.EqualTo(-1));
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public void IdleCapturePathSkipsIndexAndSearch(bool available, bool hasPredator)
    {
        using var f = new Fixture(available, hasPredator ? 0d : 4d);
        f.Add(MetabolismType.Hydrogenotrophy, 2f);
        if (hasPredator) f.Add(MetabolismType.Predation, 0f);
        if (!available) f.Runtime.ConfigurePredatorFounders(3);
        f.Step();
        Assert.That(f.Runtime.PredationDiagnostics.indexBuilds, Is.Zero);
        Assert.That(f.Runtime.PredationDiagnostics.searches, Is.Zero);
        Assert.That(f.Runtime.PredatorFoundersPlaced, Is.Zero);
    }

    [Test]
    public void DisabledAvailabilityAlsoSkipsAnInjectedPredatorWithOxygen()
    {
        using var f = new Fixture(false);
        f.Add(MetabolismType.Predation, 0f);
        f.Add(MetabolismType.Hydrogenotrophy, 2f);
        f.Runtime.ConfigurePredatorFounders(3);
        f.Step();
        Assert.That(f.State.Count, Is.EqualTo(2));
        Assert.That(f.Runtime.PredatorFoundersPlaced, Is.Zero);
        Assert.That(f.Runtime.PredationDiagnostics.indexBuilds, Is.Zero);
        Assert.That(f.Runtime.PredationDiagnostics.searches, Is.Zero);
        Assert.That(f.Deaths, Is.Empty);
    }

    [Test]
    public void FoundersRemainPendingAtCapacityAndAnExistingPredatorCanDivideAfterFeeding()
    {
        using var f = new Fixture();
        f.Add(MetabolismType.Hydrogenotrophy, 2f);
        f.Runtime.ConfigurePredatorFounders(1);
        f.Step(capacity: 1);
        Assert.That(f.Runtime.PredatorFoundersPlaced, Is.Zero);
        f.Step(capacity: 2);
        Assert.That(f.Runtime.PredatorFoundersPlaced, Is.EqualTo(1));
        f.Step(capacity: 3, reproductionRate: 4f);
        Assert.That(f.Deaths, Is.EqualTo(new[] { DeathCause.Predation }));
        Assert.That(f.Agents.FindAll(agent => agent.metabolism == MetabolismType.Predation).Count, Is.EqualTo(2));
    }

    [TestCase(true, 0f, true)]
    [TestCase(true, 30f, false)]
    [TestCase(false, 0f, false)]
    public void SaprotrophyCanAcquirePredationFromTheLocalParentWithoutBuildingAnIndex(
        bool available, float unlockSeconds, bool changes)
    {
        using var f = new Fixture(available, mutationChance: 1f, unlockSeconds: unlockSeconds);
        f.Add(MetabolismType.Saprotrophy, 2f);
        f.Step(reproductionRate: 4f);
        Assert.That(f.State.Count, Is.EqualTo(2));
        Assert.That(f.State.Metabolism[1], Is.EqualTo(changes ? MetabolismType.Predation : MetabolismType.Saprotrophy));
        Assert.That(f.Runtime.PredationDiagnostics.indexBuilds, Is.Zero);
        Assert.That(f.Runtime.PredationDiagnostics.searches, Is.Zero);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject root = new GameObject("isolated geodesic predation test");
        private readonly GeodesicOceanResourceField resources;
        public readonly GeodesicBiologyRuntime Runtime = new GeodesicBiologyRuntime();
        public readonly List<Replicator> Agents = new List<Replicator>();
        public readonly List<DeathCause> Deaths = new List<DeathCause>();
        public readonly ReplicatorPopulationState State = new ReplicatorPopulationState();
        public readonly GeodesicOceanLayerGrid Grid;

        public Fixture(bool available = true, double oxygen = 4d, float mutationChance = 0f, float unlockSeconds = 0f)
        {
            root.SetActive(false);
            var planet = root.AddComponent<PlanetGenerator>();
            planet.saprotrophyUnlockSeconds = 0f;
            Set(Runtime, "planet", planet);
            resources = root.AddComponent<GeodesicOceanResourceField>();
            var topology = GeodesicGridTopology.Build(0);
            var ocean = new bool[topology.CellCount];
            var floor = new float[topology.CellCount];
            Array.Fill(ocean, true);
            Array.Fill(floor, 7.5f);
            Grid = new GeodesicOceanLayerGrid(topology, new GeodesicTransportGraph(topology), ocean, floor, 8f, 2,
                new[] { 0f, .5f, 1f });
            int nodes = Grid.NodeCapacity;
            Grid.PhysicalLayerVolumeKm3[0] = 1d;
            var concentrations = new double[7 * nodes];
            concentrations[(int)GeodesicOceanResource.O2 * nodes] = oxygen;
            Set(resources, "sourceGrid", Grid);
            Set(resources, "nodeCapacity", nodes);
            Set(resources, "concentrationsByResourceThenNode", concentrations);
            Set(resources, "resourceMayHaveSpatialVariation", new bool[7]);
            Set(resources, "initialized", true);
            Set(Runtime, "resources", resources);
            Set(Runtime, "grid", Grid);
            foreach (string field in new[] { "demand", "availabilityFactor", "delta" }) Set(Runtime, field, new double[7 * nodes]);
            foreach (string field in new[] { "stamps", "lightStamps", "touched" }) Set(Runtime, field, new int[nodes]);
            var temperatures = new float[nodes];
            var stamps = new int[nodes];
            Array.Fill(temperatures, 300f);
            Array.Fill(stamps, 1);
            Set(Runtime, "temperatureByNode", temperatures);
            Set(Runtime, "temperatureStamps", stamps);
            Set(Runtime, "oxygenByNode", new float[nodes]);
            Set(Runtime, "reproductionRandom", new System.Random(27));
            Set(Runtime, "temperatureMutationRandom", new System.Random(28));
            Set(Runtime, "founderMinLife", 100f);
            Set(Runtime, "founderMaxLife", 100f);
            Set(Runtime, "founderBiomassTarget", .2f);
            var availability = GeodesicBiologyAvailability.Default;
            if (available) availability.metabolismMask |= 1 << (int)MetabolismType.Predation;
            Set(Runtime, "availability", availability);
            var settings = GeodesicPredationSettings.Default;
            settings.collectDiagnostics = true;
            settings.energyPerOxidizedCarbon = 3f;
            settings.evolutionRequiresMotility = false;
            settings.evolutionUnlockSeconds = unlockSeconds;
            Runtime.SetPredationControls(settings, true, .5f, 1f, mutationChance, .01f, new Vector2(290f, 310f), 10f);
        }

        public long Add(MetabolismType metabolism, float carbon, int cell = 0, int layer = 0)
        {
            int node = Grid.GetNodeIndex(cell, layer);
            var agent = new Replicator(Grid.SourceTopology.CellDirections[cell] * Grid.LayerCenterRadius[node],
                Quaternion.identity, 100f, Color.white, default, .5f, metabolism, LocomotionType.Anchored);
            GeodesicBiologyRuntime.InitializeFounderBiologicalState(agent, cell, layer, new Vector2(290f, 310f), 10f, 1f, 0f, .2f);
            agent.organicCStore = carbon;
            Agents.Add(agent);
            State.AddAgentFromReplicatorData(agent);
            return agent.DiagnosticId;
        }

        public double Inventory(GeodesicOceanResource resource) => resources.GetNodeInventory(0, 0, resource);

        public void Step(int capacity = 10, float reproductionRate = 0f) =>
            Runtime.Step(.25f, Agents, State, maintenance: 0f,
                hydrogenCo2: 0f, hydrogenH2: 0f, hydrogenEnergy: 0f, hydrogenStoreFraction: 0f,
                hydrogenO2InhibitionEnabled: false, o2ComfortMax: 0f, o2StressMax: 0f, hydrogenMinimumO2Efficiency: 0f,
                sulfurCo2: 0f, sulfurH2s: 0f, sulfurEnergy: 0f, methaneCo2: 0f, methaneH2: 0f, methaneEnergy: 0f,
                methanotrophyCh4: 0f, methanotrophyO2: 0f, methanotrophyEnergy: 0f, photoCo2: 0f, photoEnergy: 0f,
                maxStore: 10f, reproductionRate: reproductionRate, carbonDivision: true, divisionCost: 0f, replicationCost: 0f,
                divisionMultiple: 2f, childSplit: .5f, maxPopulation: capacity, registerDeathCause: (_, cause) => Deaths.Add(cause),
                configuredTemperatureSettings: new GeodesicBiologyTemperatureSettings { ReplicationMinEfficiency = .15f });

        public void Dispose() => UnityEngine.Object.DestroyImmediate(root);

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
