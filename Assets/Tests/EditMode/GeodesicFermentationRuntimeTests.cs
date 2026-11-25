using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicFermentationRuntimeTests
{
    internal const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static object Get(object o,string n) => o.GetType().GetField(n,Private).GetValue(o);
    internal static void Set(object o,string n,object v) => o.GetType().GetField(n,Private).SetValue(o,v);
    internal static object Call(object o,string n,params object[] args) => o.GetType().GetMethod(n,Private).Invoke(o,args);

    [Test]
    public void PublicStepConsumesOrganicCAndCommitsBothProductsBeforeBiologicalCredit()
    {
        using(var f=new Fixture()) { f.Add(); f.Write(GeodesicOceanResource.OrganicC,1d); double carbon=f.Carbon; f.Step();
            Assert.That(f.State.Energy[0],Is.EqualTo(.034f).Within(1e-7)); Assert.That(f.State.OrganicCStore[0],Is.EqualTo(.003f).Within(1e-8));
            Assert.That(f.Inventory(GeodesicOceanResource.OrganicC),Is.EqualTo(.98d).Within(1e-8));
            Assert.That(f.Inventory(GeodesicOceanResource.CO2),Is.EqualTo(1.017d).Within(1e-8)); Assert.That(f.Inventory(GeodesicOceanResource.H2),Is.EqualTo(1.017d).Within(1e-8));
            Assert.That(f.Carbon,Is.EqualTo(carbon).Within(1e-8)); f.AssertAccounting(); }
    }
    [TestCase(0f)][TestCase(-1f)][TestCase(float.NaN)]
    public void DisabledOrganicRequirementCannotCreateEnergyOrProducts(float need)
    { using(var f=new Fixture()) { f.Configure(need:need); f.Add(energy:1f); f.Write(GeodesicOceanResource.OrganicC,1d); double c=f.Carbon; f.Step();
        Assert.That(f.State.Energy[0],Is.EqualTo(1f)); Assert.That(f.Counter("fermentationConsumed"),Is.EqualTo(0d)); Assert.That(f.Carbon,Is.EqualTo(c)); } }
    [Test]
    public void NoExternalFoodDoesNotConsumeStoredCarbonForEnergy()
    { using(var f=new Fixture()) { f.Add(carbon:.15f,energy:1f); f.Step(); Assert.That(f.State.OrganicCStore[0],Is.EqualTo(.15f)); Assert.That(f.State.Energy[0],Is.EqualTo(1f)); Assert.That(f.Counter("fermentationConsumed"),Is.EqualTo(0d)); } }
    [TestCase(0f)][TestCase(.15f)][TestCase(1f)]
    public void ConfiguredAssimilationAndLegacyEnergyDenominatorRemainConservative(float assimilation)
    { using(var f=new Fixture()) { f.Configure(assimilation:assimilation); f.Add(energy:1f); f.Write(GeodesicOceanResource.OrganicC,1d); double c=f.Carbon; f.React();
        Assert.That(f.State.OrganicCStore[0],Is.EqualTo(.02f*assimilation).Within(1e-8)); Assert.That(f.State.Energy[0]-1f,Is.EqualTo(.04f*(1f-assimilation)).Within(2e-7));
        Assert.That(f.Carbon,Is.EqualTo(c).Within(1e-8)); f.AssertAccounting(); } }
    [TestCase(10f)][TestCase(9.999f)]
    public void UnstorableCarbonIsFermentedRatherThanDiscardedOrDuplicated(float store)
    { using(var f=new Fixture()) { f.Add(carbon:store,energy:1f); f.Write(GeodesicOceanResource.OrganicC,1d); double c=f.Carbon; f.React();
        Assert.That(f.State.OrganicCStore[0],Is.EqualTo(10f)); Assert.That(f.Carbon,Is.EqualTo(c).Within(1e-8));
        Assert.That((double)f.Counter("fermentationConsumed"),Is.EqualTo((double)f.Counter("fermentationStored")+(double)f.Counter("fermentationCO2")).Within(1e-8));
        if(store==10f) Assert.That(f.State.Energy[0]-1f,Is.EqualTo(.04f).Within(1e-7)); f.AssertAccounting(); } }
    [Test]
    public void SixteenFermentersShareFiniteFoodWithoutOverspending()
    { using(var f=new Fixture()) { for(int i=0;i<16;i++) f.Add(energy:1f); f.Write(GeodesicOceanResource.OrganicC,.01d); double c=f.Carbon; f.React();
        Assert.That((double)f.Counter("fermentationConsumed"),Is.LessThanOrEqualTo(.01d+1e-12)); Assert.That(f.Carbon,Is.EqualTo(c).Within(1e-8));
        for(int i=1;i<f.State.Count;i++) Assert.That(f.State.OrganicCStore[i],Is.EqualTo(f.State.OrganicCStore[0]).Within(1e-9)); f.AssertAccounting(); } }
    [TestCase(.02f,1f)][TestCase(.07f,.825f)][TestCase(.12f,.65f)]
    public void AuthoritativeLocalOxygenInhibitsThroughputWithoutBeingConsumed(float o2,float efficiency)
    { using(var f=new Fixture()) { f.Configure(oxygen:true); f.Add(energy:1f); f.Write(GeodesicOceanResource.O2,o2); f.Write(GeodesicOceanResource.OrganicC,1d); f.React();
        Assert.That(f.State.Energy[0]-1f,Is.EqualTo(.034f*efficiency).Within(2e-7)); Assert.That(f.State.OrganicCStore[0],Is.EqualTo(.003f*efficiency).Within(1e-8));
        Assert.That(f.Inventory(GeodesicOceanResource.O2),Is.EqualTo(o2)); f.AssertAccounting(); } }
    [Test]
    public void ActualNodeLayerOwnsOrganicWithdrawalAndBothProducts()
    { using(var f=new Fixture(layers:2)) { f.Add(energy:1f); f.Add(energy:1f); f.State.CurrentOceanLayerIndex[1]=1; f.Write(GeodesicOceanResource.OrganicC,1d,layer:1); f.React();
        Assert.That(f.State.OrganicCStore[0],Is.Zero); Assert.That(f.State.OrganicCStore[1],Is.GreaterThan(0f));
        Assert.That(f.Inventory(GeodesicOceanResource.CO2,layer:0),Is.EqualTo(1d)); Assert.That(f.Inventory(GeodesicOceanResource.CO2,layer:1),Is.GreaterThan(0d));
        Assert.That(f.Inventory(GeodesicOceanResource.H2,layer:1),Is.GreaterThan(0d)); f.AssertAccounting(); } }
    [TestCase(false)][TestCase(true)]
    public void LocalOrganicGateCannotUseDistantCarbonWhenInheritedAllocationIsTooSmall(bool local)
    { using(var f=new Fixture()) { f.Configure(chance:1f,gate:.75f); f.Raw.Controls(outer:1f,inner:0f); f.Add(MetabolismType.Hydrogenotrophy,carbon:1f,energy:5f);
        f.Write(GeodesicOceanResource.OrganicC,10d,cell:local?0:1); f.Step(rate:2f); Assert.That(f.State.Count,Is.EqualTo(2));
        Assert.That(f.State.Metabolism[1],Is.EqualTo(local?MetabolismType.Fermentation:MetabolismType.Hydrogenotrophy));
        Assert.That((long)f.Counter("fermentationBlocked"),Is.EqualTo(local?0:1)); } }
    [TestCase(LocomotionType.PassiveDrift)][TestCase(LocomotionType.Amoeboid)][TestCase(LocomotionType.Flagellum)]
    public void InheritedCarbonAllowsARebasedOrangeChildButDoesNotFeedIt(LocomotionType locomotion)
    { using(var f=new Fixture()) { f.Configure(chance:1f); f.Raw.Controls(outer:1f,inner:0f); f.Add(MetabolismType.Hydrogenotrophy,1f,5f,locomotion); f.Step(rate:2f);
        Assert.That(f.State.Metabolism[0],Is.EqualTo(MetabolismType.Hydrogenotrophy)); Assert.That(f.State.Metabolism[1],Is.EqualTo(MetabolismType.Fermentation));
        Assert.That(f.State.Locomotion[1],Is.EqualTo(locomotion)); Assert.That(f.State.OptimalTempMin[1],Is.EqualTo(285.15f)); Assert.That(f.State.OptimalTempMax[1],Is.EqualTo(330.15f));
        Assert.That(f.State.Color[1],Is.EqualTo(Color.orangeRed)); Assert.That(f.State.Age[1],Is.Zero); Assert.That(f.State.GeodesicCellIndex[1],Is.EqualTo(f.State.GeodesicCellIndex[0]));
        Assert.That((long)f.Counter("fermentationInheritedGate"),Is.EqualTo(1)); float carbon=f.State.OrganicCStore[1]; f.Step(); Assert.That(f.State.OrganicCStore[1],Is.EqualTo(carbon)); Assert.That(f.Counter("fermentationConsumed"),Is.EqualTo(0d)); } }
    [TestCase(0f,1f)][TestCase(1f,0f)]
    public void EitherNestedRollCanDisableFermentation(float outer,float inner)
    { using(var f=new Fixture()) { f.Configure(chance:inner); f.Raw.Controls(outer:outer,inner:0f); f.Add(MetabolismType.Hydrogenotrophy,1f,5f); f.Step(rate:2f); Assert.That(f.State.Metabolism[1],Is.EqualTo(MetabolismType.Hydrogenotrophy)); } }
    [Test]
    public void SulfurDrawsSelectionsAndCountersAreIdenticalWithFermentationEnabledOrDisabled()
    { using(var a=new Fixture()) using(var b=new Fixture()) {
        a.Configure(chance:0f); b.Configure(chance:1f); a.Raw.Controls(outer:.2f,inner:.4f); b.Raw.Controls(outer:.2f,inner:.4f);
        for(int i=0;i<128;i++) { a.Add(MetabolismType.Hydrogenotrophy,1f,5f); b.Add(MetabolismType.Hydrogenotrophy,1f,5f); }
        a.Step(rate:2f); b.Step(rate:2f);
        foreach(string n in new[]{"sulfurMutationAttempts","sulfurMutationsAllowed","sulfurMutationsBlocked"}) Assert.That(b.Counter(n),Is.EqualTo(a.Counter(n)),n);
        for(int i=128;i<a.State.Count;i++) { Assert.That(b.State.Metabolism[i]==MetabolismType.SulfurChemosynthesis,Is.EqualTo(a.State.Metabolism[i]==MetabolismType.SulfurChemosynthesis)); Assert.That(b.State.Locomotion[i],Is.EqualTo(a.State.Locomotion[i])); Assert.That(b.State.MovementSeed[i],Is.EqualTo(a.State.MovementSeed[i])); Assert.That(b.State.Energy[i],Is.EqualTo(a.State.Energy[i])); Assert.That(b.State.OrganicCStore[i],Is.EqualTo(a.State.OrganicCStore[i])); }
    } }
    [Test]
    public void ProductiveFermenterCanAccumulateCarbonDivideAndRecycleOnDeath()
    { using(var f=new Fixture()) { f.Configure(energy:.7f,assimilation:.4f); f.Add(carbon:.15f,energy:1f); f.State.BiomassTarget[0]=.15f; f.Write(GeodesicOceanResource.OrganicC,2d); double carbon=f.Carbon;
        for(int i=0;i<30;i++) f.Step(rate:2f); Assert.That(f.State.Count,Is.GreaterThan(1)); Assert.That((long[])f.Counter("fermentationParentBirths"),Has.Some.GreaterThan(0));
        Assert.That(f.Carbon,Is.EqualTo(carbon).Within(1e-7)); for(int i=0;i<f.State.Count;i++) f.State.Age[i]=1000f; f.Step(); Assert.That(f.State.Count,Is.Zero); Assert.That(f.Carbon,Is.EqualTo(carbon).Within(1e-7)); } }
    [Test]
    public void FermenterRoundTripsThroughProductionPopulationSnapshot()
    { using(var f=new Fixture()) { f.Add(carbon:.1f,energy:1f); f.Write(GeodesicOceanResource.OrganicC,1d); f.Step(); var planet=(PlanetGenerator)Get(f.Runtime,"planet"); var manager=planet.gameObject.AddComponent<ReplicatorManager>(); Set(manager,"agents",f.Raw.agents); Set(manager,"populationState",f.State);
        var snapshot=JsonUtility.FromJson<ReplicatorPopulationSnapshot>(JsonUtility.ToJson(manager.CapturePopulationSnapshot())); float c=f.State.OrganicCStore[0]; Assert.That(manager.ApplyPopulationSnapshot(snapshot),Is.True);
        Assert.That(f.State.Metabolism[0],Is.EqualTo(MetabolismType.Fermentation)); Assert.That(f.State.OrganicCStore[0],Is.EqualTo(c)); Assert.That(f.State.Color[0],Is.EqualTo(Color.orangeRed)); Assert.That(f.State.OptimalTempMax[0],Is.EqualTo(330.15f)); } }
    [Test]
    public void UnrepresentableOutputRejectsTheEntireTransactionWithoutDebitingFood()
    { var t=GeodesicFermentationBalance.PlanCommit(1d,1e20d,1e20d,1d,1d,.02d,.04f,.15f,0f,10f);
        Assert.That(t.status,Is.EqualTo(GeodesicHydrogenotrophyBalance.CommitStatus.PrecisionRejected)); Assert.That(t.organicAfter,Is.EqualTo(1d)); Assert.That(t.energy,Is.Zero); Assert.That(t.storeAfter,Is.Zero); }

    [Test]
    public void StartupConfigurationAppliesSelectedFermentationParametersBeforeTheRealManagerStep()
    { using(var f=new Fixture()) {
        var planet=(PlanetGenerator)Get(f.Runtime,"planet"); var manager=planet.gameObject.AddComponent<ReplicatorManager>(); manager.planetGenerator=planet;
        Set(manager,"geodesicBiology",f.Runtime); Set(manager,"agents",f.Raw.agents); Set(manager,"populationState",f.State);
        var b=new SimulationStartupBiologyConfig { fermentationOrganicCPerTick=.031f,fermentationEnergyPerTick=.63f,fermentationAssimilationFraction=.27f,
            hydrogenToFermentationMutationChance=.19f,fermentationTempRange=new Vector2(290f,335f),reproductionRate=0f };
        b.Apply(manager); f.Add(energy:1f); f.Write(GeodesicOceanResource.OrganicC,1d); Assert.That(manager.RunGeodesicBiologyStep(.5f),Is.True);
        Assert.That(f.Counter("fermentationNeed"),Is.EqualTo(.031f)); Assert.That(f.Counter("fermentationEnergy"),Is.EqualTo(.63f)); Assert.That(f.Counter("fermentationAssimilation"),Is.EqualTo(.27f));
        Assert.That(f.Counter("hydrogenFermentationChance"),Is.EqualTo(.19f)); Assert.That(f.Counter("fermentationRange"),Is.EqualTo(new Vector2(290f,335f)));
        Assert.That(f.State.OrganicCStore[0],Is.EqualTo(.031f*.27f).Within(1e-8));
    } }

    [Test]
    public void SelectedButHabitatBlockedSulfurCandidateStillSuppressesFermentation()
    { using(var f=new Fixture()) {
        f.Configure(chance:1f); f.Raw.Controls(outer:1f,inner:1f); f.Add(MetabolismType.Hydrogenotrophy,1f,5f); f.Write(GeodesicOceanResource.H2S,0d); f.Step(rate:2f);
        Assert.That(f.State.Metabolism[1],Is.EqualTo(MetabolismType.Hydrogenotrophy)); Assert.That(f.Counter("sulfurMutationsBlocked"),Is.EqualTo(1L)); Assert.That(f.Counter("fermentationAttempts"),Is.EqualTo(0L));
    } }

    internal sealed class Fixture : IDisposable
    {
        internal readonly GeodesicSulfurRuntimeTests.Fixture Raw;
        internal GeodesicBiologyRuntime Runtime => Raw.runtime;
        internal ReplicatorPopulationState State => Raw.State;
        internal readonly GeodesicOceanResourceField Resources;
        internal Fixture(double volume=1d,int layers=1) {
            Raw=new GeodesicSulfurRuntimeTests.Fixture(1d,1d,volume,layers); Resources=(GeodesicOceanResourceField)Get(Runtime,"resources");
            var temperatures=(float[])Get(Runtime,"temperatureByNode"); for(int i=0;i<temperatures.Length;i++) temperatures[i]=300f;
            Configure();
        }
        internal void Configure(float need=.02f,float energy=.04f,float assimilation=.15f,bool oxygen=false,float chance=0f,float gate=.001f) =>
            Runtime.SetFermentationControls(need,energy,assimilation,oxygen,.65f,chance,gate,new Vector2(285.15f,330.15f),20f);
        internal void Add(MetabolismType type=MetabolismType.Fermentation,float carbon=0f,float energy=0f,LocomotionType locomotion=LocomotionType.Anchored) {
            Raw.AddAgent(carbon,type,locomotion); int i=State.Count-1; State.Energy[i]=energy; State.OptimalTempMin[i]=type==MetabolismType.Fermentation?285.15f:295f; State.OptimalTempMax[i]=type==MetabolismType.Fermentation?330.15f:305f;
            State.Color[i]=Raw.agents[i].color=type==MetabolismType.Fermentation?Color.orangeRed:Color.white;
        }
        internal void Write(GeodesicOceanResource r,double amount,int cell=0,int layer=0) => Raw.Write(r,amount,cell,layer);
        internal double Inventory(GeodesicOceanResource r,int cell=0,int layer=0) => Resources.GetNodeInventory(cell,layer,r);
        internal double Carbon { get { double c=0d; for(int cell=0;cell<Raw.grid.CellCount;cell++) for(int layer=0;layer<Raw.grid.ActiveLayerCountByCell[cell];layer++) c+=Inventory(GeodesicOceanResource.CO2,cell,layer)+Inventory(GeodesicOceanResource.OrganicC,cell,layer); for(int i=0;i<State.Count;i++) c+=State.OrganicCStore[i]; return c; } }
        internal void Step(float dt=.5f,float rate=0f,float maintenance=0f) => Raw.Step(dt,rate,maintenance);
        internal void React() => Raw.React();
        internal object Counter(string name) => Get(Runtime,name);
        internal void AssertAccounting() {
            var d=(GeodesicBiologyDiagnostics)Get(Raw,"diagnostics"); var a=((GeodesicBiologyDiagnostics.ReactionAccounting[])Get(d,"reactions"))[(int)MetabolismType.Fermentation];
            Assert.That(a.modelViolations+a.measuredViolations+a.invalid+a.inputTransferViolations+a.zeroMeasuredInputWithEnergy,Is.Zero); Assert.That(a.capLoss,Is.Zero); Assert.That(Math.Abs(a.measuredCarbonResidual),Is.LessThan(1e-7));
        }
        public void Dispose() => Raw.Dispose();
    }
}
