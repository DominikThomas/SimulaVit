using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicFounderEmergenceTests
{
    [Test]
    public void EventTimesRespectDelayAndInitialAttemptCap()
    {
        var s=new GeodesicFounderEmergenceSchedule(); s.Configure(283,20d,300f,20,10f,false,300f);
        Assert.That(s.NextEventTime,Is.GreaterThan(320d));
        Assert.That(s.TryConsume(320d,out _),Is.False);
        double previous=320d;
        for(int i=0;i<20;i++) { Assert.That(s.TryConsume(s.NextEventTime,out double time),Is.True); Assert.That(time,Is.GreaterThan(previous)); previous=time; }
        Assert.That(s.InitialEventsRemaining,Is.Zero); Assert.That(s.TryConsume(double.MaxValue,out _),Is.False);
    }

    [Test]
    public void EventSequenceIsIndependentOfPollingAndSpeedGrouping()
    {
        List<double> Collect(int stepsPerFrame,double step) {
            var s=new GeodesicFounderEmergenceSchedule(); s.Configure(283,0,3,40,2,false,300);
            var times=new List<double>(); double now=0;
            for(int frame=0;now<200;frame++) for(int k=0;k<stepsPerFrame && now<200;k++) {
                now+=step; while(s.TryConsume(now,out double due)) times.Add(due);
            }
            return times;
        }
        CollectionAssert.AreEqual(Collect(1,1d/60d),Collect(100,1d/60d));
        CollectionAssert.AreEqual(Collect(1,1d/60d),Collect(1,1d/17d));
    }

    [Test]
    public void OngoingEmergenceIsOptInAndStartsAfterInitialPhase()
    {
        var off=new GeodesicFounderEmergenceSchedule(); off.Configure(1,0,0,0,10,false,300);
        Assert.That(off.TryConsume(1e6,out _),Is.False);
        var on=new GeodesicFounderEmergenceSchedule(); on.Configure(1,0,0,2,10,true,300);
        Assert.That(on.TryConsume(on.NextEventTime,out _),Is.True); Assert.That(on.InitialEventsRemaining,Is.EqualTo(1));
        Assert.That(on.TryConsume(on.NextEventTime,out double lastInitial),Is.True); Assert.That(on.InitialEventsRemaining,Is.Zero);
        Assert.That(on.NextEventTime,Is.GreaterThan(lastInitial)); Assert.That(on.TryConsume(on.NextEventTime,out _),Is.True);
    }

    [TestCase(float.NaN)][TestCase(0f)][TestCase(-1f)]
    public void InvalidIntervalsCannotStallOrCreateSimultaneousInfiniteEvents(float interval)
    {
        var s=new GeodesicFounderEmergenceSchedule(); s.Configure(1,0,0,2,interval,true,interval);
        double first=s.NextEventTime; Assert.That(double.IsFinite(first)&&first>0,Is.True);
        Assert.That(s.TryConsume(first,out _),Is.True); Assert.That(s.NextEventTime,Is.GreaterThan(first));
    }

    [Test]
    public void NativePlacementInitializesNormalPackedFounderAndPassiveSchedules()
    {
        using(var f=new Fixture()) {
            f.Advance(1000,1); Assert.That(f.State.Count,Is.EqualTo(1));
            Assert.That(f.State.Metabolism[0],Is.EqualTo(MetabolismType.Hydrogenotrophy));
            Assert.That(f.State.Locomotion[0],Is.EqualTo(LocomotionType.PassiveDrift));
            Assert.That(f.State.GeodesicCellIndex[0],Is.Zero); Assert.That(f.State.CurrentOceanLayerIndex[0],Is.Zero);
            Assert.That(f.State.PreferredOceanLayerIndex[0],Is.Zero); Assert.That(f.State.Age[0],Is.Zero);
            Assert.That(f.State.Energy[0],Is.InRange(.1f,.5f)); Assert.That(f.State.OrganicCStore[0],Is.Zero);
            Assert.That(f.State.MaxLifespan[0],Is.InRange(360f,600f)); Assert.That(f.State.BiomassTarget[0],Is.EqualTo(.15f));
            Assert.That(f.State.OptimalTempMin[0],Is.EqualTo(330f)); Assert.That(f.State.OptimalTempMax[0],Is.EqualTo(390f));
            Assert.That(f.State.Position[0],Is.EqualTo(f.Raw.agents[0].position)); Assert.That(f.State.Position[0].magnitude,Is.EqualTo(f.Raw.grid.LayerCenterRadius[0]).Within(1e-5f));
            Assert.That(f.State.NextPassiveWanderUpdateTime[0],Is.GreaterThan(1000f));
            Assert.That(f.State.NextPassiveVerticalDriftTime[0],Is.GreaterThan(1000f));
            Assert.That(f.Snapshot.successes,Is.EqualTo(1)); Assert.That(f.Snapshot.livingFounders,Is.EqualTo(1));
        }
    }

    [Test]
    public void SelectionUsesOnlyAuthenticViableInjectionNodesAndAllowsReplacement()
    {
        using(var f=new Fixture(count:12)) {
            f.Vents((0,1f),(6,100f)); // The heavily weighted outlet is initially foodless.
            f.Advance(10000,100);
            Assert.That(f.State.Count,Is.EqualTo(12));
            for(int i=0;i<f.State.Count;i++) Assert.That(f.State.GeodesicCellIndex[i],Is.Zero);
            Assert.That(f.Snapshot.distinctFounderVents,Is.EqualTo(1)); Assert.That(f.Snapshot.attempts,Is.EqualTo(12));
        }
    }

    [TestCase(GeodesicOceanResource.H2)][TestCase(GeodesicOceanResource.CO2)]
    public void EmptyLocalInventoryRejectsWithoutInventingFoodAndCacheCanRecover(GeodesicOceanResource missing)
    {
        using(var f=new Fixture(count:1)) {
            f.Raw.Write(missing,0); f.Advance(1000,100);
            Assert.That(f.State.Count,Is.Zero); Assert.That(f.Snapshot.rejectedHabitat,Is.EqualTo(1));
            f.Raw.Write(missing,10); f.Raw.runtime.ConfigureGradualEmergence(1,0,.01f,false,300);
            f.Advance(1001,100); Assert.That(f.State.Count,Is.Zero,"Rejected habitat remains excluded during the short cache interval.");
            f.Raw.runtime.ConfigureGradualEmergence(1,0,.01f,false,300);
            f.Advance(1011,100); Assert.That(f.State.Count,Is.EqualTo(1));
        }
    }

    [Test]
    public void ThermalUnsuitabilityAndEmptyVentCatalogRejectWithBoundedWork()
    {
        using(var f=new Fixture(count:1000)) {
            var temperatures=(float[])Get(f.Raw.runtime,"temperatureByNode"); temperatures[0]=100f;
            f.Advance(10000,10000); Assert.That(f.Snapshot.attempts,Is.EqualTo(64)); Assert.That(f.State.Count,Is.Zero);
            Assert.That(f.Snapshot.thermalCandidateRejections, Is.GreaterThan(0));
            Assert.That(f.Snapshot.invalidCandidateRejections, Is.Zero);
        }
        using(var f=new Fixture()) { f.Vents(); f.Advance(1000,100); Assert.That(f.Snapshot.rejectedHabitat,Is.EqualTo(1)); }
    }

    [Test]
    public void PopulationCapRejectsEventsRatherThanQueueingGuaranteedFounders()
    {
        using(var f=new Fixture(count:8)) { f.Advance(1000,3); Assert.That(f.State.Count,Is.EqualTo(3));
            Assert.That(f.Snapshot.successes,Is.EqualTo(3)); Assert.That(f.Snapshot.rejectedPopulationCap,Is.EqualTo(5));
            GeodesicBiologyRuntime.RemoveAgentAtSwapBack(0,f.Raw.agents,f.State); f.Advance(2000,3); Assert.That(f.State.Count,Is.EqualTo(2)); }
    }

    [Test]
    public void SameSeedReproducesHabitatsTraitsAndPositionsWithoutDrawingOtherStreams()
    {
        using(var a=new Fixture(count:20)) using(var b=new Fixture(count:20)) {
            a.Vents((0,1f),(6,3f)); b.Vents((0,1f),(6,3f));
            foreach(var f in new[]{a,b}) { f.Raw.Write(GeodesicOceanResource.H2,100,6); f.Raw.Write(GeodesicOceanResource.CO2,100,6); }
            string[] streamNames={"reproductionRandom","temperatureMutationRandom","locomotionMutationRandom","metabolismMutationRandom","fermentationMutationRandom"};
            var streams=new System.Random[streamNames.Length];
            for(int k=0;k<streams.Length;k++) { streams[k]=new System.Random(777); Set(a.Raw.runtime,streamNames[k],streams[k]); }
            a.Advance(10000,100); b.Advance(10000,100);
            Assert.That(a.State.Count, Is.EqualTo(20));
            Assert.That(b.State.Count, Is.EqualTo(a.State.Count));
            for(int i=0;i<a.State.Count;i++) { Assert.That(a.State.GeodesicCellIndex[i],Is.EqualTo(b.State.GeodesicCellIndex[i]));
                Assert.That(a.State.Energy[i],Is.EqualTo(b.State.Energy[i])); Assert.That(a.State.MaxLifespan[i],Is.EqualTo(b.State.MaxLifespan[i]));
                Assert.That(a.State.MovementSeed[i],Is.EqualTo(b.State.MovementSeed[i])); Assert.That(a.State.Position[i],Is.EqualTo(b.State.Position[i])); }
            foreach(var stream in streams) Assert.That(stream.NextDouble(),Is.EqualTo(new System.Random(777).NextDouble()));
            Assert.That(a.Snapshot.distinctFounderVents,Is.EqualTo(2));
        }
    }

    [Test]
    public void NativeEmergenceIsIndependentOfOneOrHundredStepsPerRenderedFrame()
    {
        using(var a=new Fixture(count:20)) using(var b=new Fixture(count:20)) {
            void AdvanceFrames(Fixture f,int stepsPerFrame) {
                int tick=0;
                while(tick<600) for(int k=0;k<stepsPerFrame && tick<600;k++) { tick++; f.Advance(tick/60d,100); }
            }
            AdvanceFrames(a,1); AdvanceFrames(b,100);
            Assert.That(a.Snapshot.successes,Is.EqualTo(20)); Assert.That(b.Snapshot.successes,Is.EqualTo(20));
            Assert.That(a.Snapshot.firstScheduledTime,Is.EqualTo(b.Snapshot.firstScheduledTime));
            for(int i=0;i<20;i++) Assert.That(a.State.Position[i],Is.EqualTo(b.State.Position[i]));
        }
    }

    [Test]
    public void DifferentBiologySeedsProduceDifferentEventTimes()
    {
        var a=new GeodesicFounderEmergenceSchedule(); var b=new GeodesicFounderEmergenceSchedule();
        a.Configure(283,0,0,20,10,false,300); b.Configure(284,0,0,20,10,false,300);
        Assert.That(a.NextEventTime,Is.Not.EqualTo(b.NextEventTime));
    }

    [Test]
    public void FounderUsesInjectionBottomLayerAndOccupancyIncludesLayerAbove()
    {
        using(var f=new Fixture(layerCount:2)) {
            f.Advance(1000,10); Assert.That(f.State.Count,Is.EqualTo(1));
            Assert.That(f.State.CurrentOceanLayerIndex[0],Is.EqualTo(1)); Assert.That(f.State.PreferredOceanLayerIndex[0],Is.EqualTo(1));
            Assert.That(f.State.Position[0].magnitude,Is.EqualTo(f.Raw.grid.LayerCenterRadius[1]).Within(1e-5f));
            f.State.CurrentOceanLayerIndex[0]=0;
            Assert.That(f.Snapshot.inhabitedVents,Is.EqualTo(1));
        }
    }

    [Test]
    public void ViabilityUsesTheSameOrderedTemperatureTemplateAsFounderInitialization()
    {
        using(var f=new Fixture()) {
            Set(f.Raw.runtime,"founderHydrogenTemperatureRange",new Vector2(390,330));
            f.Advance(1000,10); Assert.That(f.State.Count,Is.EqualTo(1));
            Assert.That(f.State.OptimalTempMin[0],Is.EqualTo(330f)); Assert.That(f.State.OptimalTempMax[0],Is.EqualTo(390f));
        }
    }

    [Test]
    public void LaterEmergenceDoesNotResetTheMovementClockOrExistingSchedules()
    {
        using(var f=new Fixture()) {
            f.Raw.AddAgent(); Set(f.Raw.runtime,"passiveMovementTime",42f);
            f.State.NextPassiveWanderUpdateTime[0]=45f; f.State.NextPassiveVerticalDriftTime[0]=48f;
            f.Advance(1000,10);
            Assert.That(f.State.Count,Is.EqualTo(2)); Assert.That((float)Get(f.Raw.runtime,"passiveMovementTime"),Is.EqualTo(42f));
            Assert.That(f.State.NextPassiveWanderUpdateTime[0],Is.EqualTo(45f)); Assert.That(f.State.NextPassiveVerticalDriftTime[0],Is.EqualTo(48f));
            Assert.That(f.State.NextPassiveWanderUpdateTime[1],Is.GreaterThan(42f));
        }
    }

    [Test]
    public void OccupancyCountsNeighborsAndBottomTwoLayersWithoutDirectPlacement()
    {
        using(var f=new Fixture(count:0)) {
            f.Vents((0,1f)); int neighbor=f.Raw.grid.SourceTopology.Neighbors6[0];
            f.Raw.AddAgent(); f.State.GeodesicCellIndex[0]=neighbor;
            Assert.That(f.Snapshot.inhabitedVents,Is.EqualTo(1)); Assert.That(f.Snapshot.indirectlyColonizedVents,Is.EqualTo(1));
            Assert.That(f.Snapshot.distinctFounderVents,Is.Zero);
        }
    }

    [Test]
    public void OriginBirthAndDeathAccountingSurvivesSwapBack()
    {
        using(var f=new Fixture(count:2)) {
            f.Advance(1000,10);
            Assert.That(f.State.Count, Is.EqualTo(2));
            long id=f.Raw.agents[0].DiagnosticId;
            Call(f.Raw.runtime,"RecordFounderParentBirth",id); Call(f.Raw.runtime,"RecordFounderParentBirth",long.MaxValue);
            Call(f.Raw.runtime,"RecordFounderDeath",id); GeodesicBiologyRuntime.RemoveAgentAtSwapBack(0,f.Raw.agents,f.State);
            Assert.That(f.Snapshot.livingFounders,Is.EqualTo(1)); Assert.That(f.Snapshot.founderDeaths,Is.EqualTo(1));
            Assert.That(f.Snapshot.founderParentBirths,Is.EqualTo(1)); Assert.That(f.Snapshot.descendantParentBirths,Is.EqualTo(1));
        }
    }

    [Test]
    public void CurrentStartupFieldsRoundTripAndOldProfilesStayBatch()
    {
        var config=new SimulationStartupConfig { geodesicFounderEmergenceMode=GeodesicFounderEmergenceMode.Gradual,
            geodesicFounderMeanIntervalSeconds=12.5f,geodesicFounderOngoingEmergence=true,geodesicFounderOngoingMeanIntervalSeconds=456f };
        config.biology.hydrogenToFermentationMutationChance=.02f;
        var savedType=typeof(SimulationStartupController).GetNestedType("SavedStartupConfig",BindingFlags.NonPublic);
        var saved=savedType.GetMethod("FromConfig").Invoke(null,new object[]{config});
        var restored=SimulationStartupController.DeserializeSavedConfig(JsonUtility.ToJson(saved),new SimulationStartupConfig());
        Assert.That(restored.geodesicFounderEmergenceMode,Is.EqualTo(GeodesicFounderEmergenceMode.Gradual));
        Assert.That(restored.geodesicFounderMeanIntervalSeconds,Is.EqualTo(12.5f)); Assert.That(restored.geodesicFounderOngoingEmergence,Is.True);
        Assert.That(restored.geodesicFounderOngoingMeanIntervalSeconds,Is.EqualTo(456f)); Assert.That(restored.biology.hydrogenToFermentationMutationChance,Is.EqualTo(.02f));
        foreach(string json in new[]{"{}","{\"version\":23,\"geodesicBiologySpawnDelaySeconds\":300}"}) {
            var old=SimulationStartupController.DeserializeSavedConfig(json,config);
            Assert.That(old.geodesicFounderEmergenceMode,Is.EqualTo(GeodesicFounderEmergenceMode.Batch)); Assert.That(old.geodesicFounderOngoingEmergence,Is.False);
        }
    }

    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static object Get(object target,string name)=>target.GetType().GetField(name,Private).GetValue(target);
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    private static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
    private sealed class Fixture:IDisposable
    {
        public readonly GeodesicSulfurRuntimeTests.Fixture Raw;
        private readonly GeodesicOceanResourceField resources;
        private readonly GeodesicExperiencedTemperatureField experienced;
        public ReplicatorPopulationState State=>Raw.State;
        public GeodesicBiologyRuntime.FounderEmergenceSnapshot Snapshot=>Raw.runtime.CaptureFounderEmergence(State);
        public Fixture(int count=1,int layerCount=1) {
            Raw=new GeodesicSulfurRuntimeTests.Fixture(layerCount:layerCount);
            resources = Raw.resources;
            experienced = resources.gameObject.AddComponent<GeodesicExperiencedTemperatureField>();
            Set(experienced, "resources", resources);
            Set(experienced, "generator", resources.GetComponent<PlanetGenerator>());
            Set(experienced, "ventMicrothermalFalloffDistance", .12f);
            Set(Raw.runtime, "experiencedTemperature", experienced);
            Set(Raw.runtime,"biologySeed",283); Set(Raw.runtime,"founderMinLife",360f); Set(Raw.runtime,"founderMaxLife",600f);
            Set(Raw.runtime,"founderColor",Color.cyan); Set(Raw.runtime,"founderBiomassTarget",.15f);
            Set(Raw.runtime,"founderHydrogenTemperatureRange",new Vector2(330,390)); Set(Raw.runtime,"founderLethalMargin",20f);
            int bottom=Raw.grid.GetBottomLayerIndex(0);
            Raw.Write(GeodesicOceanResource.CO2,100,layer:bottom); Raw.Write(GeodesicOceanResource.H2,100,layer:bottom); Vents((0,1f));
            Raw.runtime.ConfigureGradualEmergence(count,0,.01f,false,300);
        }
        public void Vents(params (int cell,float weight)[] cells) {
            var outlets = new GeodesicVentSourceOutlet[cells.Length];
            var thermalOutlets = new GeodesicVentOutlet[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                int cell = cells[i].cell;
                int layer = Raw.grid.GetBottomLayerIndex(cell);
                int node = Raw.grid.GetNodeIndex(cell, layer);
                var direction = Raw.grid.SourceTopology.CellDirections[cell];
                outlets[i] = new GeodesicVentSourceOutlet(GeodesicVentHabitat.Submarine, cell, node, i, 1f, cells[i].weight, 1f);
                // The local disc fits inside this coarse cell. A weak thermal anomaly keeps the
                // 350 K background viable and leaves the deliberately cold habitat unsuitable.
                thermalOutlets[i] = new GeodesicVentOutlet(GeodesicVentHabitat.Submarine, cell, layer, i,
                    direction * 7.5f, direction, .01f, .02f);
            }
            Set(resources, "ventOutlets", outlets);
            Set(experienced, "outlets", thermalOutlets);
            Call(experienced, "BuildLookup");
            foreach (var outlet in outlets)
                Assert.That(experienced.TryGetSubmarineOutletForNode(outlet.SourceNode, out _, out _), Is.True,
                    $"Missing thermal outlet for injection node {outlet.SourceNode}.");
        }
        public void Advance(double time,int cap)=>Raw.runtime.AdvanceFounderEmergence(time,cap,.01f,.02f,false,.02f,.12f,.25f,Raw.agents,State);
        public void Dispose()=>Raw.Dispose();
    }
}
