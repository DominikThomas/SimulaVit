using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicSulfurRuntimeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void PublicStepFeedsSulfurAndProducesMeasuredSuspendedS0() {
        using (var f = new Fixture()) {
            f.Controls(); f.AddAgent(); double initial = f.TotalCarbon;
            f.Step();
            Assert.That(f.State.Energy[0], Is.EqualTo(.3f).Within(1e-6));
            Assert.That(f.State.OrganicCStore[0], Is.EqualTo(.02f).Within(1e-8));
            Assert.That(f.Mineral, Is.EqualTo(1d-f.Inventory(GeodesicOceanResource.H2S)).Within(1e-12));
            Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-8));
        }
    }

    [TestCase(0d,1d)][TestCase(1d,0d)]
    public void MissingEitherLocalInputStopsProduction(double co2, double h2s) {
        using (var f = new Fixture(co2,h2s)) { f.Controls(); f.AddAgent(); f.React();
            Assert.That(f.State.Energy[0], Is.Zero); Assert.That(f.State.OrganicCStore[0], Is.Zero); Assert.That(f.Mineral, Is.Zero); f.AssertCleanAccounting(); }
    }

    [TestCase(0f,.001f)][TestCase(.02f,0f)][TestCase(0f,0f)][TestCase(float.NaN,.001f)]
    public void DisabledOrInvalidRequirementsCannotGenerateFreeEnergy(float co2, float h2s) {
        using (var f = new Fixture()) { f.Controls(); f.Co2Need=co2; f.H2SNeed=h2s; f.AddAgent(); f.Step();
            Assert.That(f.State.Energy[0], Is.Zero); Assert.That(f.Mineral, Is.Zero); Assert.That(f.TotalCarbon, Is.EqualTo(1d)); }
    }

    [TestCase(16,false)][TestCase(16,true)]
    public void BatchCompetitionConservesSharedCarbonAndSulfur(int count, bool mixed) {
        using (var f = new Fixture(.01d,.0003d)) { f.Controls();
            for(int i=0;i<count;i++) f.AddAgent(0f,mixed && i%2==0 ? MetabolismType.Hydrogenotrophy : MetabolismType.SulfurChemosynthesis);
            double initial=f.TotalCarbon; f.React();
            Assert.That(f.Inventory(GeodesicOceanResource.CO2), Is.GreaterThanOrEqualTo(0d));
            Assert.That(f.Inventory(GeodesicOceanResource.H2S), Is.GreaterThanOrEqualTo(0d));
            Assert.That(f.Mineral, Is.EqualTo(.0003d-f.Inventory(GeodesicOceanResource.H2S)).Within(1e-12));
            Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-8)); f.AssertCleanAccounting();
        }
    }

    [TestCase(0f)][TestCase(.4f)][TestCase(1f)]
    public void StoreFractionScalesWholeReactionWithoutDiscardedCarbon(float activity) {
        using(var f=new Fixture()) { f.Controls(activity); f.AddAgent(); double initial=f.TotalCarbon; f.React();
            Assert.That(f.State.Energy[0], Is.EqualTo(.3f*activity).Within(1e-6));
            Assert.That(f.State.OrganicCStore[0], Is.EqualTo(.02f*activity).Within(1e-8));
            Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-8)); f.AssertCleanAccounting(); }
    }

    [TestCase(10f)][TestCase(9.999f)]
    public void StoreCapacityLimitsDemandAndMeasuredWithdrawal(float store) {
        using(var f=new Fixture()) { f.Controls(); f.AddAgent(store); double initial=f.TotalCarbon; f.React();
            Assert.That(f.State.OrganicCStore[0], Is.EqualTo(10f));
            Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-8));
            if(store==10f) { Assert.That(f.State.Energy[0], Is.Zero); Assert.That(f.Mineral, Is.Zero); }
            f.AssertCleanAccounting(); }
    }

    [TestCase(.3f)][TestCase(.5f)][TestCase(.7f)]
    public void FeedingDivisionAndDeathRecycleExactlyOnce(float split) {
        using(var f=new Fixture()) { f.Controls(); f.AddAgent(); double initial=f.TotalCarbon; f.React();
            f.Lifecycle(1f,split); Assert.That(f.State.Count, Is.EqualTo(2));
            Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-8));
            for(int i=0;i<f.State.Count;i++) f.State.Age[i]=1000f;
            f.Lifecycle(0f,split); f.Lifecycle(0f,split);
            Assert.That(f.State.Count, Is.Zero); Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-8)); }
    }

    [TestCase(.02f,1f)][TestCase(.07f,.8f)][TestCase(.12f,.6f)]
    public void AuthoritativeLocalOxygenReducesAllThroughput(float o2,float efficiency) {
        using(var f=new Fixture()) { f.Controls(1f,true); f.Write(GeodesicOceanResource.O2,o2); f.AddAgent(); f.React();
            Assert.That(f.State.Energy[0], Is.EqualTo(.3f*efficiency).Within(1e-6));
            Assert.That(f.State.OrganicCStore[0], Is.EqualTo(.02f*efficiency).Within(1e-8));
            Assert.That(f.Mineral, Is.EqualTo(.001f*efficiency).Within(1e-9)); f.AssertCleanAccounting(); }
    }

    [TestCase(LocomotionType.PassiveDrift)][TestCase(LocomotionType.Amoeboid)][TestCase(LocomotionType.Flagellum)]
    public void PublicDivisionMutatesOnlyChildAndPacksRebasedTraitsAndYellowColor(LocomotionType locomotion) {
        using(var f=new Fixture()) { f.Controls(1f,false,1f,1f); f.AddAgent(1f,MetabolismType.Hydrogenotrophy,locomotion); f.State.Energy[0]=5f;
            f.Step(.5f,2f); Assert.That(f.State.Count, Is.EqualTo(2));
            Assert.That(f.State.Metabolism[0], Is.EqualTo(MetabolismType.Hydrogenotrophy));
            Assert.That(f.State.Metabolism[1], Is.EqualTo(MetabolismType.SulfurChemosynthesis));
            Assert.That(f.State.Locomotion[1], Is.EqualTo(locomotion));
            Assert.That(f.State.OptimalTempMin[1], Is.EqualTo(333.15f)); Assert.That(f.State.OptimalTempMax[1], Is.EqualTo(393.15f));
            Assert.That(f.State.Color[1], Is.EqualTo(Color.yellow)); Assert.That(f.agents[1].metabolism, Is.EqualTo(f.State.Metabolism[1]));
            Assert.That(f.State.GeodesicCellIndex[1], Is.EqualTo(f.State.GeodesicCellIndex[0])); Assert.That(f.State.CurrentOceanLayerIndex[1], Is.EqualTo(0));
            f.State.CopyHotStateToAgent(1,f.agents[1]);
            Assert.That(f.agents[1].optimalTempMin, Is.EqualTo(333.15f));
            Assert.That((long)Get(f.runtime,"sulfurMutationsAllowed"), Is.EqualTo(1)); }
    }

    [TestCase(false)][TestCase(true)]
    public void MutationGateReadsChildHabitatDespiteH2SElsewhere(bool missingCO2) {
        using(var f=new Fixture()) { f.Controls(1f,false,1f,1f); f.AddAgent(1f,MetabolismType.Hydrogenotrophy); f.State.Energy[0]=5f;
            f.Write(missingCO2?GeodesicOceanResource.CO2:GeodesicOceanResource.H2S,0d);
            f.Write(GeodesicOceanResource.H2S,10d,1); f.Step(.5f,2f);
            Assert.That(f.State.Count, Is.EqualTo(2)); Assert.That(f.State.Metabolism[1], Is.EqualTo(MetabolismType.Hydrogenotrophy));
            Assert.That((long)Get(f.runtime,"sulfurMutationsBlocked"), Is.EqualTo(1)); }
    }

    [TestCase(0f,1f)][TestCase(1f,0f)]
    public void EitherNestedMutationRollCanDisableTheEdge(float outer,float inner) {
        using(var f=new Fixture()) { f.Controls(1f,false,outer,inner); f.AddAgent(1f,MetabolismType.Hydrogenotrophy); f.State.Energy[0]=5f; f.Step(.5f,2f);
            Assert.That(f.State.Metabolism[1], Is.EqualTo(MetabolismType.Hydrogenotrophy)); }
    }

    [Test]
    public void ZeroH2SControlIsBitExactWithSulfurMutationsDisabled() {
        using(var a=new Fixture(100d,0d)) using(var b=new Fixture(100d,0d)) {
            a.Controls(1f,false,1f,1f); b.Controls();
            a.AddAgent(1f,MetabolismType.Hydrogenotrophy,LocomotionType.Amoeboid); b.AddAgent(1f,MetabolismType.Hydrogenotrophy,LocomotionType.Amoeboid);
            a.State.Energy[0]=b.State.Energy[0]=5f;
            for(int tick=0;tick<20;tick++) { a.Step(.1f,10f); b.Step(.1f,10f); }
            Assert.That(a.State.Count, Is.EqualTo(b.State.Count));
            for(int i=0;i<a.State.Count;i++) { Assert.That(a.State.Energy[i], Is.EqualTo(b.State.Energy[i])); Assert.That(a.State.OrganicCStore[i], Is.EqualTo(b.State.OrganicCStore[i]));
                Assert.That(a.State.Position[i], Is.EqualTo(b.State.Position[i])); Assert.That(a.State.OptimalTempMin[i], Is.EqualTo(b.State.OptimalTempMin[i])); }
        }
    }

    [Test]
    public void SulfurMutationDrawsAreRepeatableAndRetainTheNestedRolls() {
        using(var a=new Fixture(100d,100d)) using(var b=new Fixture(100d,100d)) {
            a.Controls(1f,false,.2f,.4f); b.Controls(1f,false,.2f,.4f);
            for(int i=0;i<64;i++) { a.AddAgent(1f,MetabolismType.Hydrogenotrophy); b.AddAgent(1f,MetabolismType.Hydrogenotrophy); a.State.Energy[i]=b.State.Energy[i]=5f; }
            a.Step(.5f,2f); b.Step(.5f,2f);
            var expected=new System.Random(0x53554C46); int allowed=0;
            for(int i=0;i<64;i++) {
                bool mutated=expected.NextDouble()<.2f && expected.NextDouble()<.4f;
                if(mutated) allowed++;
                Assert.That(a.State.Metabolism[64+i], Is.EqualTo(mutated?MetabolismType.SulfurChemosynthesis:MetabolismType.Hydrogenotrophy));
                Assert.That(a.State.Metabolism[64+i], Is.EqualTo(b.State.Metabolism[64+i]));
                Assert.That(a.State.OrganicCStore[64+i], Is.EqualTo(b.State.OrganicCStore[64+i]));
            }
            Assert.That((long)Get(a.runtime,"sulfurMutationsAllowed"), Is.EqualTo(allowed));
        }
    }

    [Test]
    public void MutatedSulfurRoundTripsThroughProductionPopulationSnapshots() {
        using(var f=new Fixture()) {
            f.Controls(1f,false,1f,1f); f.AddAgent(1f,MetabolismType.Hydrogenotrophy,LocomotionType.Flagellum); f.State.Energy[0]=5f; f.Step(.5f,2f);
            var root=new GameObject("isolated sulfur snapshot manager"); root.SetActive(false);
            try {
                var manager=root.AddComponent<ReplicatorManager>();
                Set(manager,"agents",f.agents); Set(manager,"populationState",f.State);
                var snapshot=JsonUtility.FromJson<ReplicatorPopulationSnapshot>(JsonUtility.ToJson(manager.CapturePopulationSnapshot()));
                float carbon=f.State.OrganicCStore[1],energy=f.State.Energy[1]; Vector3 position=f.State.Position[1];
                Assert.That(manager.ApplyPopulationSnapshot(snapshot), Is.True);
                Assert.That(f.State.Metabolism[1], Is.EqualTo(MetabolismType.SulfurChemosynthesis));
                Assert.That(f.State.Locomotion[1], Is.EqualTo(LocomotionType.Flagellum));
                Assert.That(f.State.OrganicCStore[1], Is.EqualTo(carbon)); Assert.That(f.State.Energy[1], Is.EqualTo(energy));
                Assert.That(f.State.Position[1], Is.EqualTo(position)); Assert.That(f.State.Color[1], Is.EqualTo(Color.yellow));
                Assert.That(f.State.OptimalTempMin[1], Is.EqualTo(333.15f)); Assert.That(f.State.CurrentOceanLayerIndex[1], Is.EqualTo(0));
                f.Step(); Assert.That(f.State.Metabolism[1], Is.EqualTo(MetabolismType.SulfurChemosynthesis));
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    [Test]
    public void SulfurReactionReadsAndWritesTheOccupiedLayerOnly() {
        using(var f=new Fixture(1d,0d,1d,2)) {
            f.Controls(); f.AddAgent(); f.AddAgent();
            f.State.CurrentOceanLayerIndex[1]=1; f.State.PreferredOceanLayerIndex[1]=1;
            f.Write(GeodesicOceanResource.CO2,1d,0,1); f.Write(GeodesicOceanResource.H2S,1d,0,1); f.React();
            Assert.That(f.State.Energy[0], Is.Zero); Assert.That(f.State.Energy[1], Is.GreaterThan(0f));
            Assert.That(f.Mineral, Is.Zero);
            Assert.That(((GeodesicOceanSuspendedField)Get(f.runtime,"suspended")).GetInventory(0,1,GeodesicSuspendedMineral.S0), Is.GreaterThan(0d));
            f.AssertCleanAccounting();
        }
    }

    [Test]
    public void TurningOffConfiguredSulfurInhibitionRetainsFullThroughputAtHighOxygen() {
        using(var f=new Fixture()) { f.Controls(1f,false); f.Write(GeodesicOceanResource.O2,1d); f.AddAgent(); f.React();
            Assert.That(f.State.Energy[0], Is.EqualTo(.3f).Within(1e-6)); f.AssertCleanAccounting(); }
    }

    [Test]
    public void PhysicalHighVolumeSulfurCommitsMeasuredInputsBeforeCredit() {
        using(var f=new Fixture(1d,1d,1e8d)) { f.Controls(); f.AddAgent(); double initial=f.TotalCarbon; f.React();
            Assert.That(f.State.Energy[0], Is.GreaterThan(0f)); Assert.That(f.TotalCarbon, Is.EqualTo(initial).Within(1e-7));
            Assert.That(f.Mineral, Is.GreaterThan(0d)); f.AssertCleanAccounting(); }
    }

    internal sealed class Fixture : IDisposable
    {
        private readonly GameObject root;
        private readonly GeodesicOceanResourceField resources;
        internal readonly GeodesicBiologyRuntime runtime = new GeodesicBiologyRuntime();
        private readonly GeodesicBiologyDiagnostics diagnostics = new GeodesicBiologyDiagnostics();
        internal readonly List<Replicator> agents = new List<Replicator>();
        internal readonly GeodesicOceanLayerGrid grid;
        public readonly ReplicatorPopulationState State = new ReplicatorPopulationState();

        public Fixture(double co2 = 1d, double h2s = 1d, double volume = 1d, int layerCount = 1)
        {
            root = new GameObject("isolated sulfur production runtime fixture");
            root.SetActive(false); // No scene startup, generation, vents or environmental updates.
            var planet = root.AddComponent<PlanetGenerator>();
            resources = root.AddComponent<GeodesicOceanResourceField>();
            var minerals = root.AddComponent<GeodesicOceanSuspendedField>();
            var topology = GeodesicGridTopology.Build(0);
            var graph = new GeodesicTransportGraph(topology);
            var mask = new bool[topology.CellCount]; var floor = new float[topology.CellCount];
            for (int i = 0; i < mask.Length; i++) { mask[i] = true; floor[i] = 7.5f; }
            grid = new GeodesicOceanLayerGrid(topology, graph, mask, floor, 8f, layerCount, layerCount == 1 ? new[] { 0f, 1f } : new[] {0f,.5f,1f});
            minerals.Initialize(grid); Set(runtime, "suspended", minerals);
            grid.PhysicalLayerVolumeKm3[0] = volume; // Explicit physical-volume unit fixture.
            var concentrations = new double[7 * grid.NodeCapacity];
            concentrations[0] = co2; concentrations[(int)GeodesicOceanResource.H2 * grid.NodeCapacity] = 1d;
            concentrations[(int)GeodesicOceanResource.H2S * grid.NodeCapacity] = h2s;
            Set(resources, "sourceGrid", grid); Set(resources, "nodeCapacity", grid.NodeCapacity);
            Set(resources, "concentrationsByResourceThenNode", concentrations);
            Set(resources, "resourceMayHaveSpatialVariation", new bool[7]); Set(resources, "initialized", true);
            Set(runtime, "planet", planet); Set(runtime, "resources", resources); Set(runtime, "grid", grid);
            foreach (string name in new[] { "demand", "availabilityFactor", "delta" }) Set(runtime, name, new double[7 * grid.NodeCapacity]);
            foreach (string name in new[] { "stamps", "lightStamps", "touched" }) Set(runtime, name, new int[grid.NodeCapacity]);
            var temperatures = new float[grid.NodeCapacity]; var temperatureStamps = new int[grid.NodeCapacity];
            for (int i = 0; i < temperatures.Length; i++) { temperatures[i] = 350f; temperatureStamps[i] = 1; }
            Set(runtime, "temperatureByNode", temperatures); Set(runtime, "temperatureStamps", temperatureStamps);
            Set(runtime, "oxygenByNode", new float[grid.NodeCapacity]); Set(runtime, "lightByNode", new float[grid.NodeCapacity]);
            Set(runtime, "reproductionRandom", new System.Random(123456));
            Set(runtime, "temperatureMutationRandom", new System.Random(123456));
            Set(runtime, "temperatureSettings", new GeodesicBiologyTemperatureSettings { ReplicationMinEfficiency = .15f, StressMaintenanceMultiplier = 1f });
            var locomotion = GeodesicLocomotionSettings.Default; locomotion.activeSpeedMultiplier = .4f; locomotion.logDiagnostics = false;
            var steering = (ReplicatorSteeringSystem.Settings)typeof(GeodesicLocomotionEcologyExperiments).GetMethod("Steering", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,null);
            runtime.SetLocomotionSettings(locomotion, steering, 4f,1.2f,2f,2f,1.5f,0f,.05f,.001f,true,.01f);
            runtime.SetVisualGrowthSettings(new ReplicatorVisualGrowthSettings { daughterSeparationDistance = 0f });
            runtime.SetAccountingDiagnostics(diagnostics);
        }

        public void AddAgent(float carbon = 0f, MetabolismType metabolism = MetabolismType.SulfurChemosynthesis, LocomotionType locomotion = LocomotionType.Anchored)
        {
            var agent = new Replicator(grid.SourceTopology.CellDirections[0] * 7.9f, Quaternion.identity, 100f,
                Color.white, default, .5f, metabolism, locomotion);
            GeodesicBiologyRuntime.InitializeFounderBiologicalState(agent, 0, 0, new Vector2(330f, 390f), 20f, 0f, 0f, .002f);
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

        public float Co2Need = .02f, H2SNeed = .001f, Energy = .3f, MaxStore = 10f;
        public double Mineral => ((GeodesicOceanSuspendedField)Get(runtime, "suspended")).GetInventory(0, 0, GeodesicSuspendedMineral.S0);
        public void Write(GeodesicOceanResource resource, double inventory, int cell = 0, int layer = 0) {
            int node = grid.GetNodeIndex(cell, layer);
            ((double[])Get(resources, "concentrationsByResourceThenNode"))[(int)resource * grid.NodeCapacity + node] = inventory / grid.PhysicalLayerVolumeKm3[node];
        }
        public void Controls(float activity = 1f, bool oxygen = false, float outer = 0f, float inner = 0f) =>
            runtime.SetSulfurControls(activity, oxygen, .6f, outer, inner, .0005f, .001f, new Vector2(333.15f,393.15f), 20f);
        public void Step(float dt = .5f, float rate = 0f, float maintenance = 0f) => runtime.Step(dt, agents, State, maintenance,
            .01f, .02f, 8f, .8f, false, .02f, .12f, .25f, Co2Need, H2SNeed, Energy,
            0f,0f,0f,0f,0f,0f,0f,0f,MaxStore,rate,true,.2f,.5f,2f,.5f,30000,null,
            new GeodesicBiologyTemperatureSettings { ReplicationMinEfficiency = .15f, StressMaintenanceMultiplier = 1f });
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
                        : name == "hStoreFraction" ? .8f : name == "maximumStore" ? MaxStore : name == "sCo2" ? Co2Need : name == "sH2s" ? H2SNeed : name == "sEnergy" ? Energy : name == "o2ComfortMax" ? .02f : name == "o2StressMax" ? .12f : 0f;
                }
                build.Invoke(runtime, args);
            }
            Invoke(runtime, "AggregateDemands", State.Count); Invoke(runtime, "ResolveAvailabilityFactors");
            Invoke(runtime, "ResolveCompetition", State.Count);
            if (observe) Invoke(runtime, "PrepareAccountingScratch");
            Invoke(runtime, "CommitRequests", State.Count, State, MaxStore, observe);
            if (observe) for (int i = 0; i < State.Count; i++) Invoke(runtime, "RecordReactionAccounting", i);
        }

        public void Lifecycle(float rate, float split) => Invoke(runtime, "RunLifecycle", 1f, agents, State,
            0f, rate, true, .2f, .5f, 2f, split, 10, null, false, false);

        public void AssertCleanAccounting()
        {
            var reactions = (GeodesicBiologyDiagnostics.ReactionAccounting[])Get(diagnostics, "reactions");
            var r = reactions[(int)MetabolismType.SulfurChemosynthesis];
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
