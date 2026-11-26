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

    [TestCase(false)]
    [TestCase(true)]
    public void PreparedThermalSamplingMatchesReferenceAcrossVentSupportAndLayers(bool transformed)
    {
        using (var f = new Fixture(1d, 1d, withVents: true))
        {
            f.SetThermalMode(optimized: true);
            if (transformed) f.TransformPlanet();
            float center = f.AssertThermalParity(0, 1, f.VentPosition(0f));
            float gradient = f.AssertThermalParity(0, 1, f.VentPosition(.14f));
            Assert.That(center, Is.GreaterThan(gradient));
            Assert.That(gradient, Is.GreaterThan(290f));
            foreach (float offset in new[] { .079f, .08f, .081f, .199f, .2f, .201f })
                f.AssertThermalParity(0, 1, f.VentPosition(offset));
            Assert.That(f.AssertThermalParity(0, 1, f.VentPosition(.24f)), Is.EqualTo(290f));

            Assert.That(f.AssertThermalParity(0, 1, f.VentPosition(0f, 7.75f - .0001f)), Is.GreaterThan(290f));
            Assert.That(f.AssertThermalParity(0, 0, f.VentPosition(0f, 7.75f + .0001f)), Is.GreaterThan(300f));
            f.AssertThermalParity(f.NeighborCell, 1, f.WorldCellPosition(f.NeighborCell, 1));
            Assert.That(f.AssertThermalParity(f.FarCell, 0, f.WorldCellPosition(f.FarCell, 0)), Is.EqualTo(300f));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LifecycleThermalCacheFollowsMovementLayersAndPublishedTemperature(bool optimized)
    {
        using (var f = new Fixture(1d, 1d, withVents: true))
        {
            f.SetThermalMode(optimized);
            f.AddVentAgent(f.VentPosition(0f));
            float center = f.ReadLifecycleTemperature(0);
            Assert.That(f.ReadLifecycleTemperature(0), Is.EqualTo(center));
            f.State.Position[0] = f.VentPosition(.14f);
            float gradient = f.ReadLifecycleTemperature(0);
            Assert.That(gradient, Is.LessThan(center));
            f.State.CurrentOceanLayerIndex[0] = 0;
            float surface = f.ReadLifecycleTemperature(0);
            Assert.That(surface, Is.GreaterThan(gradient));

            f.PublishTemperature(310f, 290f);
            float warmed = f.ReadLifecycleTemperature(0);
            Assert.That(warmed, Is.GreaterThan(surface));
            var work = f.ThermalWork;
            Assert.That(work.CacheHits, Is.EqualTo(1));
            Assert.That(work.CacheMisses, Is.EqualTo(4));
            Assert.That(work.CacheKeyInvalidations, Is.EqualTo(3));
            Assert.That(work.InfluenceEvaluations, Is.GreaterThan(0));
            if (optimized) Assert.That(work.Candidates, Is.EqualTo(work.InfluenceEvaluations));
            else Assert.That(work.Candidates, Is.GreaterThan(work.InfluenceEvaluations));

            f.PublishVentSource(200f);
            Assert.That(f.ReadLifecycleTemperature(0), Is.LessThan(warmed));
            f.PublishTemperature(float.NaN, 290f);
            Assert.That(float.IsNaN(f.ReadLifecycleTemperature(0)), Is.True);
            f.PublishTemperature(310f, 290f);
            f.State.GeodesicCellIndex[0] = f.FarCell;
            f.State.Position[0] = f.WorldCellPosition(f.FarCell, 0);
            Assert.That(f.ReadLifecycleTemperature(0), Is.EqualTo(310f));
            f.State.Position[0] = new Vector3(float.NaN, 0f, 0f);
            Assert.That(float.IsNaN(f.ReadLifecycleTemperature(0)), Is.True);
            f.State.GeodesicCellIndex[0] = 0;
            f.State.Position[0] = f.VentPosition(0f);
            f.ClearThermalVents();
            Assert.That(f.ReadLifecycleTemperature(0), Is.EqualTo(310f));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LifecyclePreservesThresholdBirthAndSwapBackTemperatureReads(bool optimized)
    {
        using (var f = new Fixture(1d, 1d, withVents: true))
        {
            f.SetThermalMode(optimized: true);
            Vector3 position = f.VentPosition(.14f);
            float threshold = f.AssertThermalParity(0, 1, position);
            f.SetThermalMode(optimized);
            f.AddVentAgent(f.VentPosition(0f), .005f);
            long doomed = f.AgentId(0);
            f.State.Age[0] = f.State.MaxLifespan[0];
            float doomedTemperature = f.ReadLifecycleTemperature(0);
            f.AddVentAgent(position, .02f);
            f.State.OptimalTempMin[1] = threshold - 10f;
            f.State.OptimalTempMax[1] = threshold;
            f.AddVentAgent(position);
            long survivor = f.AgentId(2);
            f.State.OptimalTempMin[2] = threshold - 10f;
            f.State.OptimalTempMax[2] = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(threshold) - 1);

            f.Lifecycle(1f, .5f);
            Assert.That(f.State.Count, Is.EqualTo(3));
            Assert.That(f.AgentId(0), Is.Not.EqualTo(doomed));
            Assert.That(f.AgentId(2), Is.EqualTo(survivor));
            Assert.That(f.State.Age[0], Is.Zero);
            Assert.That(f.State.Position[0], Is.Not.EqualTo(position));
            Assert.That(f.State.CanReplicate[1], Is.True);
            Assert.That(f.State.CanReplicate[2], Is.False);
            Assert.That(f.State.OrganicCStore[0], Is.EqualTo(.01f));
            Assert.That(f.State.OrganicCStore[1], Is.EqualTo(.01f));
            Assert.That(f.Inventory(GeodesicOceanResource.OrganicC, 1), Is.EqualTo(.005d).Within(1e-9));
            float newborn = f.ReadLifecycleTemperature(0);
            Assert.That(newborn, Is.Not.EqualTo(doomedTemperature));
            Assert.That(newborn, Is.EqualTo(f.ReferenceTemperature(0, 1, f.State.Position[0], 290f)));
            Assert.That(f.State.CanReplicate[0], Is.EqualTo(newborn >= threshold - 10f && newborn <= threshold));
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
        private GeodesicExperiencedTemperatureField experienced;
        private GeodesicOceanTemperatureField ocean;
        private float[] surfaceKelvin, subsurfaceKelvin;
        private double thermalTick;
        public readonly ReplicatorPopulationState State = new ReplicatorPopulationState();
        public GeodesicBiologyRuntime.LifecycleThermalWork ThermalWork => runtime.PerformanceWorkTotals.LifecycleThermal;
        public int NeighborCell => grid.SourceTopology.Neighbors6[1];
        public int FarCell { get; private set; }

        public Fixture(double co2, double h2, double volume = 1d, bool withVents = false)
        {
            root = new GameObject("isolated hydrogenotrophy transaction fixture");
            root.SetActive(false); // Seed the test authorities directly.
            var planet = root.AddComponent<PlanetGenerator>();
            resources = root.AddComponent<GeodesicOceanResourceField>();
            var topology = GeodesicGridTopology.Build(withVents ? 1 : 0);
            var graph = new GeodesicTransportGraph(topology);
            var mask = new bool[topology.CellCount]; var floor = new float[topology.CellCount];
            for (int i = 0; i < mask.Length; i++) { mask[i] = true; floor[i] = 7.5f; }
            int land = topology.Neighbors6[0];
            if (withVents) { mask[land] = false; floor[land] = 8.1f; }
            grid = new GeodesicOceanLayerGrid(topology, graph, mask, floor, 8f, withVents ? 2 : 1,
                withVents ? new[] { 0f, .5f, 1f } : new[] { 0f, 1f });
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
            if (withVents) InitializeThermalFields(planet, land);
        }

        private void InitializeThermalFields(PlanetGenerator planet, int land)
        {
            var surface = root.AddComponent<GeodesicSurfaceTemperatureField>();
            ocean = root.AddComponent<GeodesicOceanTemperatureField>();
            experienced = root.AddComponent<GeodesicExperiencedTemperatureField>();
            surfaceKelvin = new float[grid.CellCount];
            subsurfaceKelvin = new float[grid.NodeCapacity];
            Set(surface, "initialized", true);
            Set(surface, "runtimeCellCount", grid.CellCount);
            Set(surface, "surfaceTemperatureKelvinByCell", surfaceKelvin);
            Set(ocean, "surfaceField", surface);
            Set(ocean, "sourceGrid", grid);
            Set(ocean, "initialized", true);
            Set(ocean, "subsurfaceTemperatureKelvinByNode", subsurfaceKelvin);
            Set(runtime, "oceanTemperature", ocean);
            Set(runtime, "experiencedTemperature", experienced);
            Set(experienced, "generator", planet);
            Set(experienced, "resources", resources);
            Set(experienced, "ocean", ocean);
            var directions = grid.SourceTopology.CellDirections;
            Set(experienced, "outlets", new[]
            {
                new GeodesicVentOutlet(GeodesicVentHabitat.Submarine, 0, 1, 0, directions[0] * 7.5f, directions[0], .81f, .08f),
                new GeodesicVentOutlet(GeodesicVentHabitat.Terrestrial, land, -1, 1, directions[land] * 8.1f, directions[land], 1f, .08f),
                new GeodesicVentOutlet(GeodesicVentHabitat.Submarine, NeighborCell, 1, 2,
                    directions[NeighborCell] * 7.5f, directions[NeighborCell], .64f, .08f)
            });
            Invoke(experienced, "BuildLookup");
            for (int cell = 0; cell < grid.CellCount; cell++)
                if (grid.IsNodeActive(cell, 0) && Vector3.Dot(directions[0], directions[cell]) < Vector3.Dot(directions[0], directions[FarCell]))
                    FarCell = cell;
            PublishTemperature(300f, 290f);
            Set(runtime, "temperatureSettings", new GeodesicBiologyTemperatureSettings { ReplicationMinEfficiency = 1f });
            runtime.SetVisualGrowthSettings(new ReplicatorVisualGrowthSettings { daughterSeparationDistance = .02f });
        }

        public void SetThermalMode(bool optimized) => runtime.SetLifecycleThermalSamplingMode(
            optimized ? LifecycleThermalSamplingMode.Optimized : LifecycleThermalSamplingMode.Reference);

        public void TransformPlanet()
        {
            root.transform.SetPositionAndRotation(new Vector3(3f, -7f, 11f), Quaternion.Euler(17f, 39f, -13f));
            root.transform.localScale = new Vector3(.75f, 1.5f, 1.2f);
        }

        public Vector3 VentPosition(float offset, float radius = 7.6f)
        {
            Vector3 normal = grid.SourceTopology.CellDirections[0];
            Vector3 tangent = Vector3.Cross(normal, Vector3.forward).normalized;
            return root.transform.TransformPoint(normal * radius + tangent * offset);
        }

        public Vector3 WorldCellPosition(int cell, int layer) => root.transform.TransformPoint(
            grid.SourceTopology.CellDirections[cell] * grid.LayerCenterRadius[grid.GetNodeIndex(cell, layer)]);

        public float ReferenceTemperature(int cell, int layer, Vector3 position, float background) =>
            experienced.HasLocalInfluenceCandidates(cell, layer)
                ? experienced.SampleKnownOceanTemperature(cell, layer, position, background, false, out _, out _) : background;

        public float AssertThermalParity(int cell, int layer, Vector3 position)
        {
            float background = ocean.GetLayerTemperatureKelvin(cell, layer);
            var lookup = experienced.GetLifecycleOceanVentLookup(grid);
            Assert.That(lookup, Is.Not.Null);
            float optimized = lookup.TryGetCandidateRange(cell, out int start, out int end)
                ? lookup.SampleTemperature(start, end, position, background, false, out _) : background;
            float reference = ReferenceTemperature(cell, layer, position, background);
            Assert.That(BitConverter.SingleToInt32Bits(optimized), Is.EqualTo(BitConverter.SingleToInt32Bits(reference)));
            return reference;
        }

        public void PublishTemperature(float surface, float subsurface)
        {
            Array.Fill(surfaceKelvin, surface);
            Array.Fill(subsurfaceKelvin, subsurface);
            Set(ocean, "liveCompletedOceanTemperatureTick", ++thermalTick);
            Invoke(runtime, "RefreshTemperatureCache");
        }

        public void PublishVentSource(float celsius)
        {
            Set(ocean, "submarineVentSourceTemperatureC", celsius);
            Invoke(runtime, "BeginBiologicalThermalStep");
        }

        public void ClearThermalVents() => experienced.Clear();

        public void AddVentAgent(Vector3 position, float carbon = 0f)
        {
            AddAgent(carbon);
            int index = State.Count - 1;
            State.CurrentOceanLayerIndex[index] = State.PreferredOceanLayerIndex[index] = 1;
            State.Position[index] = position;
            State.Energy[index] = 2f;
        }

        public long AgentId(int index) => agents[index].DiagnosticId;

        public float ReadLifecycleTemperature(int index)
        {
            var prepared = runtime.ThermalSamplingMode == LifecycleThermalSamplingMode.Optimized
                ? experienced.GetLifecycleOceanVentLookup(grid) : null;
            return (float)Invoke(runtime, "GetBiologicalTemperature", index, State, prepared, true);
        }

        public void AddAgent(float carbon = 0f)
        {
            var agent = new Replicator(grid.SourceTopology.CellDirections[0] * 7.9f, Quaternion.identity, 100f,
                Color.white, default, .5f, MetabolismType.Hydrogenotrophy, LocomotionType.Anchored);
            GeodesicBiologyRuntime.InitializeFounderBiologicalState(agent, 0, 0, new Vector2(290f, 310f), 20f, 0f, 0f, .002f);
            agent.organicCStore = carbon;
            agents.Add(agent); State.AddAgentFromReplicatorData(agent);
        }

        public double Inventory(GeodesicOceanResource resource, int layer = 0) => resources.GetNodeInventory(0, layer, resource);
        public double TotalCarbon
        {
            get
            {
                double total = Inventory(GeodesicOceanResource.CO2) + Inventory(GeodesicOceanResource.OrganicC);
                for (int i = 0; i < State.Count; i++) total += State.OrganicCStore[i];
                return total;
            }
        }

        public void React(bool observe = true) =>
            GeodesicReactionTestPipeline.React(runtime, State, 10f, observe);

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
    private static object Invoke(object target, string name, params object[] values) => target.GetType().GetMethod(name, Private).Invoke(target, values);
}
