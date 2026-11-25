using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

/// <summary>Opt-in actual-production replay. Run with Tools/ValidateEnvironmentalCpu.ps1.</summary>
public sealed class GeodesicEnvironmentalCpuParityTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Results"));
    private static string Phase => Environment.GetEnvironmentVariable("SIMULAVIT_ENVIRONMENTAL_CPU_PHASE");
    private static readonly string[] Timers = {
        "GeodesicOceanResource.Transport", "GeodesicOceanResource.StagingClear",
        "GeodesicOceanResource.Chemistry.CandidateRefresh", "GeodesicOceanResource.Chemistry.Total",
        "GeodesicOceanResource.HorizontalMixing", "GeodesicOceanResource.VerticalMixing",
        "GeodesicOceanResource.Suspended.Total", "GeodesicOceanResource.Suspended.Mixing",
        "GeodesicOceanResource.Suspended.CollectOutgoing", "GeodesicOceanResource.Suspended.StageIncoming",
        "GeodesicOceanResource.Suspended.Clear", "GeodesicOceanResource.Suspended.Commit",
        "GeodesicOceanResource.Suspended.Settling", "GeodesicTemperature.TargetAndSurfaceResponse",
        "GeodesicOceanTemperature.RelaxApproximateProfiles", "GeodesicOceanTemperature.SolveImplicitColumns"
    };

    [TestCase(GeodesicThermalModel.ApproximateEcologicalProfiles)]
    [TestCase(GeodesicThermalModel.ConservativeImplicit)]
    public void CoupledEnvironmentAndBiologyMatchesReferenceBits(GeodesicThermalModel model)
    {
        RequireOptIn();
        UnityEngine.Random.State random = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(283);
            using (var f = new Fixture(2, model, true))
            {
                const float dt = 1f / 30f;
                double thermalCursor = 0d, resourceCursor = 0d;
                float thermalInterval = f.Surface.ActiveUpdateIntervalSeconds;
                double iron = f.IronTotal, sulfur = f.SulfurTotal;
                for (int tick = 1; tick <= 1200; tick++)
                {
                    double time = tick * (double)dt;
                    f.Biology.Step(dt, f.Agents, f.State, .001f,
                        .01f, .02f, 8f, .8f, false, .01f, .1f, .6f,
                        .01f, .02f, 6f, .01f, .02f, 4f, .01f, .02f, 4f, .01f, 4f,
                        10f, .2f, true, .2f, .5f, 2f, .5f, 100, null);
                    // Real input changes exercise per-tick climate/inertia and response invalidation.
                    if (tick == 400) { Set(f.Surface, "landHeatCapacityMultiplier", 1.3f); Set(f.Surface, "heatingTimescaleSeconds", 11f); }
                    if (tick == 600) Call(f.Atmosphere, "SetInventoryForTests", GeodesicAtmosphericGas.N2, f.Atmosphere.AtmosphereInventoryPerBar * 2d);
                    if (tick == 800) { Set(f.Surface, "oceanSurfaceHeatCapacityMultiplier", .8f); Set(f.Surface, "coolingTimescaleSeconds", 41f); }
                    while (thermalCursor + thermalInterval <= time + 1e-9d)
                    {
                        Call(f.Surface, "TickTemperature", thermalInterval, thermalCursor + thermalInterval * .5d);
                        thermalCursor += thermalInterval;
                    }
                    while (resourceCursor + 5d <= time + 1e-9d)
                    {
                        f.ResourceTick(5f); resourceCursor += 5d;
                    }
                    if (tick % 300 == 0) f.CompareSnapshot(model + "_" + tick);
                }
                Assert.That(f.IronTotal, Is.EqualTo(iron).Within(Math.Max(1e-10, Math.Abs(iron) * 1e-11)));
                Assert.That(f.SulfurTotal, Is.EqualTo(sulfur).Within(Math.Max(1e-10, Math.Abs(sulfur) * 1e-11)));
                Assert.That((long)Get(f.Biology, "births"), Is.GreaterThan(0));
                Assert.That((long)Get(f.Biology, "deaths"), Is.GreaterThan(0));
                TestContext.WriteLine($"{model}: nodes={f.Grid.ActiveNodeCount}, population={f.State.Count}, births={Get(f.Biology,"births")}, deaths={Get(f.Biology,"deaths")}, ironResidual={f.IronTotal-iron:E6}, sulfurResidual={f.SulfurTotal-sulfur:E6}");
            }
        }
        finally { UnityEngine.Random.state = random; }
    }

    [Test]
    public void DenseEnvironmentalFixtureRecordsTargetedCostsAndMatchesReferenceBits()
    {
        RequireOptIn();
        var report = new System.Text.StringBuilder("repeat,active_nodes,transport_tick_ms,surface_tick_ms");
        foreach (string timer in Timers) report.Append(',').Append(timer).Append("_ms_per_tick");
        report.AppendLine();
        for (int repeat = 0; repeat < 3; repeat++)
        using (var f = new Fixture(6, GeodesicThermalModel.ApproximateEcologicalProfiles, false))
        {
            // Warm the JIT and create minerals before recording; all candidates remain dense.
            f.ResourceTick(5f); Call(f.Surface, "TickTemperature", 2f, 1d);
            var recorders = new ProfilerRecorder[Timers.Length];
            for (int i = 0; i < Timers.Length; i++) recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, Timers[i], 1,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            try
            {
                const int ticks = 12;
                var watch = new System.Diagnostics.Stopwatch();
                double transportMs = 0d, surfaceMs = 0d;
                for (int tick = 0; tick < ticks; tick++)
                {
                    watch.Restart(); f.ResourceTick(5f); watch.Stop(); transportMs += watch.Elapsed.TotalMilliseconds;
                    watch.Restart(); Call(f.Surface, "TickTemperature", 2f, tick * 2d + 3d); watch.Stop(); surfaceMs += watch.Elapsed.TotalMilliseconds;
                }
                report.Append(repeat).Append(',').Append(f.Grid.ActiveNodeCount).Append(',')
                    .Append((transportMs/ticks).ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                    .Append((surfaceMs/ticks).ToString("F6", CultureInfo.InvariantCulture));
                foreach (var recorder in recorders)
                    report.Append(',').Append(recorder.Valid ? (recorder.CurrentValue * 1e-6d / ticks).ToString("F6", CultureInfo.InvariantCulture) : "n/a");
                report.AppendLine();
                if (repeat == 0) f.CompareSnapshot("dense_environment");
            }
            finally { foreach (var recorder in recorders) recorder.Dispose(); }
        }
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, Phase + "-fixture-timings.csv"), report.ToString());
        TestContext.WriteLine(report.ToString());
    }

    private static void RequireOptIn()
    {
        if (Phase != "reference" && Phase != "optimized") Assert.Ignore("Use Tools/ValidateEnvironmentalCpu.ps1 to capture/compare deterministic reference fixtures.");
        string owner = Path.GetFullPath(Path.Combine(Application.dataPath, "../../OWNER.txt"));
        Assert.That(File.Exists(owner) && File.ReadAllText(owner).StartsWith("Owned by the geodesic environmental CPU optimization chat, 2026-11-22.", StringComparison.Ordinal),
            Is.True, "Environmental replay must run in its owned disposable workspace.");
    }

    [Test]
    public void SuspendedScratchDoesNotReuseOldFluxAndPreservesDonorLimiting()
    {
        RequireOptIn();
        using (var f = new Fixture(1, GeodesicThermalModel.ApproximateEcologicalProfiles, false))
            f.ExerciseSuspendedScratch();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject root;
        public readonly PlanetGenerator Planet;
        public readonly GeodesicOceanLayerGrid Grid;
        public readonly GeodesicOceanResourceField Resources;
        public readonly GeodesicOceanSedimentField Sediment;
        public readonly GeodesicOceanSuspendedField Suspended;
        public readonly GeodesicAbioticChemistry Chemistry;
        public readonly GeodesicSurfaceTemperatureField Surface;
        public readonly GeodesicOceanTemperatureField Ocean;
        public readonly GeodesicAtmosphereField Atmosphere;
        public readonly GeodesicBiologyRuntime Biology = new GeodesicBiologyRuntime();
        public readonly List<Replicator> Agents = new List<Replicator>();
        public readonly ReplicatorPopulationState State = new ReplicatorPopulationState();
        private readonly int[] active;
        private readonly double[] concentrations;

        public Fixture(int subdivision, GeodesicThermalModel model, bool biology)
        {
            root = new GameObject("isolated deterministic environmental CPU fixture"); root.SetActive(false);
            Planet = root.AddComponent<PlanetGenerator>(); Planet.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, 10, subdivision); Planet.ApplyStartupSeed(283, false);
            var topology = GeodesicGridTopology.Build(subdivision); var graph = new GeodesicTransportGraph(topology);
            typeof(PlanetGenerator).GetProperty("GeodesicTopology").SetValue(Planet, topology); Set(Planet, "geodesicTransportGraph", graph);
            var mask = new bool[topology.CellCount]; var floor = new float[topology.CellCount];
            for (int cell = 0; cell < mask.Length; cell++) { mask[cell] = cell % 7 != 0; floor[cell] = 8f - (.08f + .9f * (cell % 11) / 10f); }
            Set(Planet, "geodesicOceanMask", mask);
            Grid = new GeodesicOceanLayerGrid(topology, graph, mask, floor, 8f, 5, new[] { 0f,.12f,.32f,.58f,.82f,1f });
            var domain = root.AddComponent<GeodesicOceanLayerDomain>(); Set(domain,"grid",Grid); Set(domain,"initialized",true);
            Resources = root.AddComponent<GeodesicOceanResourceField>(); Sediment = root.AddComponent<GeodesicOceanSedimentField>(); Sediment.Initialize(Grid.CellCount);
            Suspended = root.AddComponent<GeodesicOceanSuspendedField>(); Call(Suspended,"Initialize",Grid);
            Chemistry = root.AddComponent<GeodesicAbioticChemistry>();
            Atmosphere = root.AddComponent<GeodesicAtmosphereField>(); Atmosphere.Configure(1000d,1d,.1d,.21d,.01d,.02d,.01d); Atmosphere.InitializeForWorld();
            var exchange = root.AddComponent<GeodesicAirSeaGasExchange>(); exchange.SetCommonHalfLife(300d); Call(exchange,"InitializeForWorld",Atmosphere);
            var nodes = new List<int>(); var volumes = new List<double>();
            for (int cell=0;cell<Grid.CellCount;cell++) for(int layer=0;layer<Grid.ActiveLayerCountByCell[cell];layer++) { int node=Grid.GetNodeIndex(cell,layer); nodes.Add(node); volumes.Add(Grid.PhysicalLayerVolumeKm3[node]); }
            active = nodes.ToArray(); concentrations = new double[7 * Grid.NodeCapacity];
            for(int r=0;r<7;r++) foreach(int node in active) concentrations[r*Grid.NodeCapacity+node] = (r==1 ? .8d : r==6 ? .2d : .3d) + (node%13)*.003d;
            Set(Resources,"sourceGrid",Grid); Set(Resources,"nodeCapacity",Grid.NodeCapacity); Set(Resources,"cellCount",Grid.CellCount);
            Set(Resources,"activeNodeCount",active.Length); Set(Resources,"activeNodeIndices",active); Set(Resources,"activeNodeVolumes",volumes.ToArray()); Set(Resources,"activeOceanVolume",volumes.Sum());
            Set(Resources,"concentrationsByResourceThenNode",concentrations); Set(Resources,"stagedInventoryDelta",new double[concentrations.Length]);
            Set(Resources,"horizontalTickCoefficients",new float[7]); Set(Resources,"verticalTickCoefficients",new float[7]); Set(Resources,"resourceMayHaveSpatialVariation",Enumerable.Repeat(true,7).ToArray());
            var horizontal = new float[Grid.HorizontalLinkCount]; var vertical = new float[Grid.VerticalLinkCount];
            var degree = new int[Grid.NodeCapacity];
            for(int link=0;link<horizontal.Length;link++){degree[Grid.HorizontalNodeA[link]]++;degree[Grid.HorizontalNodeB[link]]++;}
            for(int link=0;link<horizontal.Length;link++){int a=Grid.HorizontalNodeA[link],b=Grid.HorizontalNodeB[link];horizontal[link]=(float)Math.Min(Grid.PhysicalLayerVolumeKm3[a]/Math.Max(1,degree[a]),Grid.PhysicalLayerVolumeKm3[b]/Math.Max(1,degree[b]));}
            for(int link=0;link<vertical.Length;link++){int a=Grid.VerticalUpperNode[link],b=Grid.VerticalLowerNode[link];vertical[link]=(float)(.25d*Math.Min(Grid.PhysicalLayerVolumeKm3[a],Grid.PhysicalLayerVolumeKm3[b]));}
            Set(Resources,"horizontalConductanceBase",horizontal); Set(Resources,"verticalConductanceBase",vertical);
            Set(Resources,"horizontalLinkCount",horizontal.Length); Set(Resources,"verticalLinkCount",vertical.Length); Set(Resources,"chemistryCandidateNodes",new int[active.Length]);
            Set(Resources,"ventOutlets",Array.Empty<GeodesicVentSourceOutlet>()); Set(Resources,"sedimentField",Sediment); Set(Resources,"suspendedField",Suspended);
            Set(Resources,"abioticChemistry",Chemistry); Set(Resources,"atmosphereField",Atmosphere); Set(Resources,"airSeaGasExchange",exchange); Set(Resources,"initialized",true);
            var sun=root.AddComponent<SunSkyRotator>();sun.planetCenter=root.transform;sun.orbitDegreesPerSecond=.75f;
            Surface=root.AddComponent<GeodesicSurfaceTemperatureField>(); Set(Surface,"thermalModel",model);Set(Surface,"sunDirectionProvider",sun); Surface.InitializeForCurrentTopology(); Assert.That(Surface.IsInitialized,Is.True);
            Ocean=root.AddComponent<GeodesicOceanTemperatureField>(); Call(Ocean,"Awake"); Ocean.InitializeForCurrentDomain(); Assert.That(Ocean.IsInitialized,Is.True);
            var experienced=root.AddComponent<GeodesicExperiencedTemperatureField>(); experienced.Initialize(Resources,Planet);
            // Both warming and cooling, land and ocean, are exercised from the first tick.
            var surfaceValues=(float[])Get(Surface,"surfaceTemperatureKelvinByCell"); for(int cell=0;cell<surfaceValues.Length;cell++) surfaceValues[cell]=cell%2==0?265f:320f;
            if(!biology) return;
            Assert.That(Biology.Initialize(Planet,Agents,State,0,20f,30f,Color.white,.002f,new Vector2(250f,350f),20f),Is.True);
            Biology.SetSulfurControls(.8f,false,.6f,.1f,.02f,0f,0f,new Vector2(250f,350f),20f);
            Biology.SetFermentationControls(.01f,4f,.5f,false,.65f,.02f,0f,new Vector2(250f,350f),20f);
            for(int i=0;i<50;i++)
            {
                int node=active[(i*17)%active.Length],cell=node/Grid.MaximumLayerCount,layer=node%Grid.MaximumLayerCount;
                var agent=new Replicator(topology.CellDirections[cell]*Grid.LayerCenterRadius[node],Quaternion.identity,20f+i%9,Color.white,default,.1f+i*.01f,(MetabolismType)(i%2==0?MetabolismType.Hydrogenotrophy:MetabolismType.SulfurChemosynthesis),LocomotionType.Anchored);
                GeodesicBiologyRuntime.InitializeFounderBiologicalState(agent,cell,layer,new Vector2(250f,350f),20f,10f,0f,.002f); agent.organicCStore=.01f;
                Agents.Add(agent); State.AddAgentFromReplicatorData(agent);
            }
        }

        public void ResourceTick(float dt)
        {
            Call(Resources,"TickResources",dt);
            Set(Resources,"completedTransportTicks",Resources.CompletedTransportTicks+1);
        }
        public void ExerciseSuspendedScratch()
        {
            double[] inventory = (double[])Get(Suspended,"inventories");
            double[] incoming = (double[])Get(Suspended,"incoming"), outgoing = (double[])Get(Suspended,"outgoing");
            double[] original = (double[])inventory.Clone();
            for(int pass=0;pass<5;pass++)
            {
                Array.Clear(inventory,0,inventory.Length);
                foreach(int node in active) inventory[node] = pass==1 ? 0d : Grid.PhysicalLayerVolumeKm3[node]*(pass==2 ? .5d : node%3==0 ? 1d : .001d);
                Set(Resources,"horizontalMixingRate",pass==3 ? 0f : .02f);
                Set(Resources,"defaultVerticalMixingRate",pass==3 ? 0f : .005f);
                double before=active.Sum(node=>inventory[node]);
                Call(Resources,"MixSuspendedInventory",inventory,0,incoming,outgoing,pass==4 ? 200f : 5f);
                double after=active.Sum(node=>inventory[node]);
                Assert.That(after,Is.EqualTo(before).Within(Math.Max(1e-10,Math.Abs(before)*1e-12)));
                foreach(int node in active)Assert.That(inventory[node],Is.GreaterThanOrEqualTo(0d));
                CompareSnapshot("suspended_flux_"+pass);
            }
            Array.Copy(original,inventory,inventory.Length);
        }
        public double IronTotal => MineralTotal(GeodesicOceanResource.Fe2,GeodesicSuspendedMineral.Fe3,true);
        public double SulfurTotal => MineralTotal(GeodesicOceanResource.H2S,GeodesicSuspendedMineral.S0,false)+Atmosphere.GetInventory(GeodesicAtmosphericGas.H2S);
        private double MineralTotal(GeodesicOceanResource dissolved,GeodesicSuspendedMineral oxidized,bool iron)
        {
            double sum=0d;
            foreach(int node in active){int cell=node/Grid.MaximumLayerCount,layer=node%Grid.MaximumLayerCount;sum+=concentrations[(int)dissolved*Grid.NodeCapacity+node]*Grid.PhysicalLayerVolumeKm3[node]+Suspended.GetInventory(cell,layer,oxidized)+Suspended.GetInventory(cell,layer,GeodesicSuspendedMineral.FeS);}
            for(int cell=0;cell<Grid.CellCount;cell++)sum+=(iron?Sediment.GetOxidizedIronInventory(cell):Sediment.GetElementalSulfurInventory(cell))+Sediment.GetIronSulphideInventory(cell);
            return sum;
        }
        public void CompareSnapshot(string label)
        {
            Directory.CreateDirectory(Output);
            using(var stream=new MemoryStream())
            using(var writer=new BinaryWriter(stream))
            {
                Write(writer,"resources",concentrations); Write(writer,"surface",Get(Surface,"surfaceTemperatureKelvinByCell")); Write(writer,"targets",Get(Surface,"targetTemperatureKelvinByCell"));
                Write(writer,"ocean",Get(Ocean,"subsurfaceTemperatureKelvinByNode")); Write(writer,"atmosphere",Get(Atmosphere,"inventory")); Write(writer,"exchange",Get(Atmosphere,"cumulativeNetTransferToOcean"));
                foreach(string field in new[]{"inventories","produced","deposited","downward"})Write(writer,"particles."+field,Get(Suspended,field));
                foreach(string field in new[]{"elementalSulfurS0ByCell","oxidizedIronFe3ByCell","ironSulphideFeSByCell"})Write(writer,field,Get(Sediment,field));
                foreach(string field in new[]{"reactedH2","reactedH2S","reactedFe2","consumedO2","precipitatedS0","precipitatedFe3","precipitatedFeS","recentOxidizedIronByCell"})Write(writer,"chemistry."+field,Get(Chemistry,field));
                writer.Write(State.Count);
                foreach(var field in typeof(ReplicatorPopulationState).GetFields(Fields).OrderBy(f=>f.Name,StringComparer.Ordinal)) if(field.FieldType.IsArray) Write(writer,"biology."+field.Name,field.GetValue(State));
                if(Agents.Count>0)foreach(string field in new[]{"births","deaths","fermentationBirths","fermentationDeaths","successfulMutations","temperatureByNode"})Write(writer,"biology."+field,Get(Biology,field));
                writer.Flush(); byte[] data=stream.ToArray(); string reference=Path.Combine(Output,label+".reference.bin");
                if(Phase=="reference")File.WriteAllBytes(reference,data);
                else { byte[] expected=File.ReadAllBytes(reference); Assert.That(data.Length,Is.EqualTo(expected.Length),label+" snapshot length"); int mismatch=-1;for(int i=0;i<data.Length;i++)if(data[i]!=expected[i]){mismatch=i;break;} Assert.That(mismatch,Is.EqualTo(-1),label+" first differing byte (zero tolerance)"); }
                File.WriteAllText(Path.Combine(Output,label+"."+Phase+".sha256"),BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(data)).Replace("-",""));
            }
        }
        public void Dispose(){Biology.Clear();UnityEngine.Object.DestroyImmediate(root);}
    }

    private static void Write(BinaryWriter writer,string label,object value){writer.Write(label);WriteValue(writer,value);}
    private static void WriteValue(BinaryWriter writer,object value)
    {
        if(value==null){writer.Write(-1);return;}
        if(value is Array array){writer.Write(array.Length);foreach(object item in array)WriteValue(writer,item);return;}
        Type type=value.GetType();
        if(type.IsEnum){writer.Write(Convert.ToInt32(value));return;}
        if(value is float f){writer.Write(f);return;} if(value is double d){writer.Write(d);return;}
        if(value is int i){writer.Write(i);return;} if(value is long l){writer.Write(l);return;} if(value is uint u){writer.Write(u);return;} if(value is ulong ul){writer.Write(ul);return;} if(value is bool b){writer.Write(b);return;}
        if(value is byte by){writer.Write(by);return;} if(value is short s){writer.Write(s);return;}
        Assert.That(type.IsValueType,Is.True,"Unsupported snapshot type "+type);
        foreach(var field in type.GetFields(Fields).OrderBy(x=>x.Name,StringComparer.Ordinal))WriteValue(writer,field.GetValue(value));
    }
    private static object Get(object target,string name)=>target.GetType().GetField(name,Fields).GetValue(target);
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Fields).SetValue(target,value);
    private static object Call(object target,string name,params object[] values)=>target.GetType().GetMethod(name,Fields).Invoke(target,values);
}
