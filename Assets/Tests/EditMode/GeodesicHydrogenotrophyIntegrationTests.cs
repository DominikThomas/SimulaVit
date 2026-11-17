using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicHydrogenotrophyIntegrationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestCase(1d, 1d, 6.4f, .008d)]
    [TestCase(1d, .004d, 1.6f, .002d)]
    [TestCase(.003d, 1d, 2.4f, .003d)]
    public void RealRequestCompetitionCommitPipelineBalancesSubstrateLimits(double co2, double h2, float energy, double carbon)
    {
        using (var f = new Fixture(co2, h2))
        {
            double initial = f.TotalCarbon;
            f.AddAgent(); f.React();
            Assert.That(f.State.Energy[0], Is.EqualTo(energy).Within(2e-6));
            Assert.That(f.State.OrganicCStore[0], Is.EqualTo(carbon).Within(1e-8));
            Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-7));
            f.AssertCleanAccounting();
        }
    }

    [Test]
    public void SixteenOrganismsShareBothLimitingInventoriesWithoutDuplicatingCarbon()
    {
        using (var f = new Fixture(.01d, .02d))
        {
            for (int i = 0; i < 16; i++) f.AddAgent();
            double initial = f.TotalCarbon;
            f.React();
            double carbon = 0d, energy = 0d;
            for (int i = 0; i < f.State.Count; i++)
            {
                carbon += f.State.OrganicCStore[i]; energy += f.State.Energy[i];
                Assert.That(f.State.OrganicCStore[i], Is.EqualTo(f.State.OrganicCStore[0]));
            }
            Assert.That(carbon, Is.EqualTo(.01d).Within(1e-8));
            Assert.That(energy, Is.EqualTo(8d).Within(2e-6));
            Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-7));
            Assert.That(f.Inventory(GeodesicOceanResource.H2), Is.GreaterThanOrEqualTo(0d));
            f.AssertCleanAccounting();
        }
    }

    [Test]
    public void FullStoreDoesNotCompeteForResourcesOrDiscardItsExistingCarbon()
    {
        using (var f = new Fixture(.008d, .016d))
        {
            f.AddAgent(10f); f.AddAgent();
            double initial = f.TotalCarbon;
            f.React();
            Assert.That(f.State.OrganicCStore[0], Is.EqualTo(10f));
            Assert.That(f.State.Energy[0], Is.Zero);
            Assert.That(f.State.Energy[1], Is.EqualTo(6.4f).Within(1e-6));
            Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-7));
            f.AssertCleanAccounting();
        }
    }

    [Test]
    public void ZeroWithdrawalNeverAwardsEnergyInTheRealPipeline()
    {
        using (var f = new Fixture(0d, 1d))
        {
            f.AddAgent(); f.React();
            Assert.That(f.State.Energy[0], Is.Zero);
            Assert.That(f.State.OrganicCStore[0], Is.Zero);
            Assert.That(f.Inventory(GeodesicOceanResource.H2), Is.EqualTo(1d));
            f.AssertCleanAccounting();
        }
    }

    [Test]
    public void HighVolumeTransfersAreMeasuredAndDiagnosticsDoNotChangeTheResult()
    {
        using (var observed = new Fixture(1d, 1d, 1e8d))
        using (var unobserved = new Fixture(1d, 1d, 1e8d))
        {
            observed.AddAgent(); unobserved.AddAgent();
            observed.React(true); unobserved.React(false);
            Assert.That(observed.State.Energy[0], Is.GreaterThan(0f));
            Assert.That(observed.Inventory(GeodesicOceanResource.CO2), Is.LessThan(1e8d));
            Assert.That(observed.State.Energy[0], Is.EqualTo(unobserved.State.Energy[0]));
            Assert.That(observed.State.OrganicCStore[0], Is.EqualTo(unobserved.State.OrganicCStore[0]));
            Assert.That(observed.Inventory(GeodesicOceanResource.CO2), Is.EqualTo(unobserved.Inventory(GeodesicOceanResource.CO2)));
            observed.AssertCleanAccounting();
        }
    }

    [TestCase(.3f)]
    [TestCase(.5f)]
    [TestCase(.7f)]
    public void ActualDivisionAndDeathRecyclingPreserveWholeSystemCarbon(float split)
    {
        using (var f = new Fixture(1d, 1d))
        {
            f.AddAgent(); f.React();
            double initial = f.TotalCarbon;
            float parentCarbon = f.State.OrganicCStore[0], parentEnergy = f.State.Energy[0];
            f.Lifecycle(1f, split);
            Assert.That(f.State.Count, Is.EqualTo(2), "Deterministic rate*dt=1 forces the existing eligible division path.");
            Assert.That(f.State.OrganicCStore[1], Is.EqualTo(parentCarbon * split));
            Assert.That((double)f.State.OrganicCStore[0] + f.State.OrganicCStore[1], Is.EqualTo(parentCarbon).Within(1e-9));
            Assert.That((double)f.State.Energy[0] + f.State.Energy[1], Is.EqualTo(parentEnergy - .2f).Within(1e-6));
            Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-7));

            for (int i = 0; i < f.State.Count; i++) f.State.Age[i] = f.State.MaxLifespan[i] + 1f;
            f.Lifecycle(0f, split);
            Assert.That(f.State.Count, Is.Zero);
            Assert.That(f.Inventory(GeodesicOceanResource.OrganicC), Is.EqualTo(parentCarbon).Within(1e-9));
            Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-7));
            double recycled = f.Inventory(GeodesicOceanResource.OrganicC);
            f.Lifecycle(0f, split);
            Assert.That(f.Inventory(GeodesicOceanResource.OrganicC), Is.EqualTo(recycled), "Removal/repeated lifecycle must not recycle twice.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject root;
        private readonly GeodesicOceanResourceField resources;
        private readonly GeodesicBiologyRuntime runtime = new GeodesicBiologyRuntime();
        private readonly GeodesicBiologyDiagnostics diagnostics = new GeodesicBiologyDiagnostics();
        private readonly List<Replicator> agents = new List<Replicator>();
        private readonly GeodesicOceanLayerGrid grid;
        public readonly ReplicatorPopulationState State = new ReplicatorPopulationState();

        public Fixture(double co2, double h2, double volume = 1d)
        {
            root = new GameObject("isolated hydrogenotrophy transaction fixture");
            root.SetActive(false); // No scene startup, generation, vents or environmental updates.
            var planet = root.AddComponent<PlanetGenerator>();
            resources = root.AddComponent<GeodesicOceanResourceField>();
            var topology = GeodesicGridTopology.Build(0);
            var graph = new GeodesicTransportGraph(topology);
            var mask = new bool[topology.CellCount]; var floor = new float[topology.CellCount];
            for (int i = 0; i < mask.Length; i++) { mask[i] = true; floor[i] = 7.5f; }
            grid = new GeodesicOceanLayerGrid(topology, graph, mask, floor, 8f, 1, new[] { 0f, 1f });
            grid.PhysicalLayerVolumeKm3[0] = volume; // Explicit physical-volume unit fixture.
            var concentrations = new double[7 * grid.NodeCapacity];
            concentrations[0] = co2; concentrations[(int)GeodesicOceanResource.H2 * grid.NodeCapacity] = h2;
            Set(resources, "sourceGrid", grid); Set(resources, "nodeCapacity", grid.NodeCapacity);
            Set(resources, "concentrationsByResourceThenNode", concentrations);
            Set(resources, "resourceMayHaveSpatialVariation", new bool[7]); Set(resources, "initialized", true);
            Set(runtime, "planet", planet); Set(runtime, "resources", resources); Set(runtime, "grid", grid);
            foreach (string name in new[] { "demand", "availabilityFactor", "delta" }) Set(runtime, name, new double[7 * grid.NodeCapacity]);
            foreach (string name in new[] { "stamps", "lightStamps", "touched" }) Set(runtime, name, new int[grid.NodeCapacity]);
            var temperatures = new float[grid.NodeCapacity]; var temperatureStamps = new int[grid.NodeCapacity];
            for (int i = 0; i < temperatures.Length; i++) { temperatures[i] = 300f; temperatureStamps[i] = 1; }
            Set(runtime, "temperatureByNode", temperatures); Set(runtime, "temperatureStamps", temperatureStamps);
            Set(runtime, "oxygenByNode", new float[grid.NodeCapacity]); Set(runtime, "lightByNode", new float[grid.NodeCapacity]);
            Set(runtime, "reproductionRandom", new System.Random(123456));
            Set(runtime, "temperatureMutationRandom", new System.Random(123456));
            Set(runtime, "temperatureSettings", new GeodesicBiologyTemperatureSettings { ReplicationMinEfficiency = .15f, StressMaintenanceMultiplier = 1f });
            runtime.SetVisualGrowthSettings(new ReplicatorVisualGrowthSettings { daughterSeparationDistance = 0f });
            runtime.SetAccountingDiagnostics(diagnostics);
        }

        public void AddAgent(float carbon = 0f)
        {
            var agent = new Replicator(grid.SourceTopology.CellDirections[0] * 7.9f, Quaternion.identity, 100f,
                Color.white, default, .5f, MetabolismType.Hydrogenotrophy, LocomotionType.Anchored);
            GeodesicBiologyRuntime.InitializeFounderBiologicalState(agent, 0, 0, new Vector2(290f, 310f), 20f, 0f, 0f, .002f);
            agent.organicCStore = carbon;
            agents.Add(agent); State.AddAgentFromReplicatorData(agent);
        }

        public double Inventory(GeodesicOceanResource resource) => resources.GetNodeInventory(0, 0, resource);
        public double TotalCarbon
        {
            get
            {
                double total = Inventory(GeodesicOceanResource.CO2) + Inventory(GeodesicOceanResource.OrganicC);
                for (int i = 0; i < State.Count; i++) total += State.OrganicCStore[i];
                return total;
            }
        }

        public void React(bool observe = true)
        {
            Invoke(runtime, "EnsureRequestCapacity", State.Count);
            Invoke(runtime, "BeginSparseStep");
            MethodInfo build = typeof(GeodesicBiologyRuntime).GetMethod("BuildRequest", Private);
            for (int i = 0; i < State.Count; i++)
            {
                var args = new object[build.GetParameters().Length];
                for (int k = 0; k < args.Length; k++)
                {
                    string name = build.GetParameters()[k].Name;
                    args[k] = name == "i" ? (object)i : name == "state" ? State : name == "hO2InhibitionEnabled" ? (object)false
                        : name == "dt" ? .5f : name == "hCo2" ? .01f : name == "hH2" ? .02f : name == "hEnergy" ? 8f
                        : name == "hStoreFraction" ? .8f : name == "maximumStore" ? 10f : 0f;
                }
                build.Invoke(runtime, args);
            }
            Invoke(runtime, "AggregateDemands", State.Count); Invoke(runtime, "ResolveAvailabilityFactors");
            Invoke(runtime, "ResolveCompetition", State.Count);
            if (observe) Invoke(runtime, "PrepareAccountingScratch");
            Invoke(runtime, "CommitRequests", State.Count, State, 10f, observe);
            if (observe) for (int i = 0; i < State.Count; i++) Invoke(runtime, "RecordReactionAccounting", i);
        }

        public void Lifecycle(float rate, float split) => Invoke(runtime, "RunLifecycle", 1f, agents, State,
            0f, rate, true, .2f, .5f, 2f, split, 10, null, false, false);

        public void AssertCleanAccounting()
        {
            var reactions = (GeodesicBiologyDiagnostics.ReactionAccounting[])Get(diagnostics, "reactions");
            var r = reactions[(int)MetabolismType.Hydrogenotrophy];
            Assert.That(r.modelViolations, Is.Zero);
            Assert.That(r.measuredViolations, Is.Zero);
            Assert.That(r.inputTransferViolations, Is.Zero);
            Assert.That(r.zeroMeasuredInputWithEnergy, Is.Zero);
            Assert.That(r.capLoss, Is.Zero);
            Assert.That(Math.Abs(r.measuredCarbonResidual), Is.LessThan(1e-7));
            Assert.That((long)Get(diagnostics, "transferViolations"), Is.Zero);
        }

        public void Dispose() => UnityEngine.Object.DestroyImmediate(root);
    }

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    private static void Invoke(object target, string name, params object[] values) => target.GetType().GetMethod(name, Private).Invoke(target, values);
}
