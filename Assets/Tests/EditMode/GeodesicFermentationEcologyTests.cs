using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicFermentationEcologyTests
{
    [Serializable] private sealed class Report {
        public string scenario, phenotype; public double seconds=80d;
        public int founders, survivors, hydrogen, sulfur, fermenters;
        public long births, deaths; public long[] parentBirths;
        public double consumed, assimilated, co2, h2, energy, deathDissolvedCarbon, remainingOrganic, carbonResidual;
        public double meanTemperaturePerformance, meanLocalOrganicC;
    }
    private static void Save(string name,object report) { string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../Artifacts/FermentationAudit")); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir,name+".json"),JsonUtility.ToJson(report,true)); }

    [TestCase(LocomotionType.Anchored)][TestCase(LocomotionType.Amoeboid)][TestCase(LocomotionType.Flagellum)]
    public void FiniteOrganicRichAndOrganicPoorHabitatsUseTheSavedYieldAndActualMovementCosts(LocomotionType phenotype)
    {
        foreach(bool rich in new[]{false,true}) using(var f=new GeodesicFermentationRuntimeTests.Fixture()) {
            f.Configure(energy:.7f,assimilation:.4f);
            for(int i=0;i<8;i++) { f.Add(carbon:.15f,energy:1f,locomotion:phenotype); f.State.BiomassTarget[i]=.15f; }
            if(rich) f.Write(GeodesicOceanResource.OrganicC,3.2d); // Explicit finite test initial condition, never a production source.
            double initial=f.Carbon;
            for(int tick=0;tick<800;tick++) f.Step(.1f,.5f,.01f);
            var r=Capture(f,initial,rich?"finite-organic-rich":"organic-poor",phenotype.ToString());
            Assert.That(Math.Abs(r.carbonResidual),Is.LessThan(1e-7));
            Assert.That(Math.Abs(r.consumed-r.assimilated-r.co2),Is.LessThan(1e-7));
            if(rich) Assert.That(r.births,Is.GreaterThan(0),"A viable finite-food habitat should demonstrate productive reproduction at the saved yield.");
            else Assert.That(r.consumed,Is.LessThanOrEqualTo(r.deathDissolvedCarbon+1e-7),"Without initial food, only real deaths can feed this cohort.");
            Save("controlled-"+phenotype+"-"+rich,r);
        }
    }

    [Test]
    public void MixedProducersAndFermentersCrossFeedOnlyThroughRealDeathRecycling()
    {
        using(var f=new GeodesicFermentationRuntimeTests.Fixture()) {
            f.Configure(energy:.7f,assimilation:.4f); f.Raw.H2SNeed=.01f; f.Raw.Energy=.1f;
            f.Write(GeodesicOceanResource.CO2,100d); f.Write(GeodesicOceanResource.H2,100d); f.Write(GeodesicOceanResource.H2S,100d);
            for(int i=0;i<24;i++) {
                var type=i<8?MetabolismType.Hydrogenotrophy:i<16?MetabolismType.SulfurChemosynthesis:MetabolismType.Fermentation;
                f.Add(type,.15f,1f); f.State.BiomassTarget[i]=.15f;
                // Equal 100s lifespans. Older producer founders create a controlled natural death pulse.
                if(i<16) f.State.Age[i]=80f;
            }
            Assert.That(f.Inventory(GeodesicOceanResource.OrganicC),Is.Zero);
            double initial=f.Carbon; double firstConsumption=-1d;
            for(int tick=0;tick<1600;tick++) { f.Step(.1f,.5f,.01f); if(firstConsumption<0d && (double)f.Counter("fermentationConsumed")>0d) firstConsumption=(tick+1)*.1d; }
            var r=Capture(f,initial,"mixed-death-recycling-only","anchored"); r.seconds=160d; r.founders=24;
            Assert.That(r.deathDissolvedCarbon,Is.GreaterThan(0d)); Assert.That(r.consumed,Is.GreaterThan(0d));
            Assert.That(firstConsumption,Is.GreaterThan(0d)); Assert.That(r.consumed,Is.LessThanOrEqualTo(r.deathDissolvedCarbon+1e-7));
            Assert.That(r.remainingOrganic+r.consumed,Is.EqualTo(r.deathDissolvedCarbon).Within(1e-7));
            Assert.That(Math.Abs(r.carbonResidual),Is.LessThan(1e-6)); Assert.That(r.births,Is.GreaterThan(0));
            Save("controlled-mixed-death-only",r);
        }
    }

    private static Report Capture(GeodesicFermentationRuntimeTests.Fixture f,double initial,string scenario,string phenotype) {
        var r=new Report {scenario=scenario,phenotype=phenotype,founders=8,survivors=f.State.Count,
            births=(long)f.Counter("fermentationBirths"),deaths=(long)f.Counter("fermentationDeaths"),parentBirths=(long[])((long[])f.Counter("fermentationParentBirths")).Clone(),
            consumed=(double)f.Counter("fermentationConsumed"),assimilated=(double)f.Counter("fermentationStored"),co2=(double)f.Counter("fermentationCO2"),h2=(double)f.Counter("fermentationH2"),
            energy=((double[])f.Counter("energyByMetabolism"))[(int)MetabolismType.Fermentation], deathDissolvedCarbon=(double)f.Counter("deathDissolvedCarbon"),remainingOrganic=f.Inventory(GeodesicOceanResource.OrganicC),carbonResidual=f.Carbon-initial};
        double n=Math.Max(1,(long)f.Counter("fermentationSamples")); r.meanTemperaturePerformance=(double)f.Counter("fermentationPerformanceSum")/n; r.meanLocalOrganicC=(double)f.Counter("fermentationLocalCSum")/n;
        for(int i=0;i<f.State.Count;i++) { if(f.State.Metabolism[i]==MetabolismType.Fermentation) r.fermenters++; else if(f.State.Metabolism[i]==MetabolismType.Hydrogenotrophy) r.hydrogen++; else if(f.State.Metabolism[i]==MetabolismType.SulfurChemosynthesis) r.sulfur++; }
        return r;
    }

    [TestCase(LocomotionType.Amoeboid)][TestCase(LocomotionType.Flagellum)]
    public void ActualTemporalMovementRespondsToOrganicCButIgnoresHydrogenAndSulfurGradients(LocomotionType phenotype)
    {
        using(var a=new GeodesicLocomotionEcologyExperiments.Fixture(phenotype,true,false,false))
        using(var b=new GeodesicLocomotionEcologyExperiments.Fixture(phenotype,true,false,false)) {
            foreach(var f in new[]{a,b}) {
                f.Runtime.SetFermentationControls(.02f,.7f,.4f,false,.65f,0f,.001f,new Vector2(285.15f,330.15f),20f); f.ConfigureAlternative("E-speed");
                var resources=(GeodesicOceanResourceField)GeodesicFermentationRuntimeTests.Get(f.Runtime,"resources"); var concentrations=(double[])GeodesicFermentationRuntimeTests.Get(resources,"concentrationsByResourceThenNode");
                for(int i=0;i<f.State.Count;i++) f.State.Metabolism[i]=MetabolismType.Fermentation;
                for(int cell=0;cell<f.Grid.CellCount;cell++) for(int layer=0;layer<5;layer++) {
                    f.WriteHabitat(cell,layer,338.15f,f==a?.00001d:100d,100d,f==a?100d:.00001d);
                    int node=f.Grid.GetNodeIndex(cell,layer); concentrations[(int)GeodesicOceanResource.OrganicC*f.Grid.NodeCapacity+node]=(cell==f.VentCell?.04d:.00001d)/f.Grid.PhysicalLayerVolumeKm3[node];
                }
            }
            var high=a.ObserveAt(a.VentCell,4,MetabolismType.Fermentation); var low=a.ObserveAt(a.ColdNeighbor,4,MetabolismType.Fermentation);
            Assert.That(high.reactionReadiness,Is.GreaterThan(low.reactionReadiness)); Assert.That(high.reactionReadiness,Is.EqualTo(b.ObserveAt(b.VentCell,4,MetabolismType.Fermentation).reactionReadiness));
            for(int tick=0;tick<100;tick++) { a.Move(.1f); b.Move(.1f); }
            for(int i=0;i<a.State.Count;i++) Assert.That(a.State.Position[i],Is.EqualTo(b.State.Position[i]));
            long worse=(long)a.Counter("suitabilityWorsening"); var field=(GeodesicOceanResourceField)GeodesicFermentationRuntimeTests.Get(a.Runtime,"resources"); var c=(double[])GeodesicFermentationRuntimeTests.Get(field,"concentrationsByResourceThenNode");
            for(int i=0;i<a.State.Count;i++) c[(int)GeodesicOceanResource.OrganicC*a.Grid.NodeCapacity+a.Grid.GetNodeIndex(a.State.GeodesicCellIndex[i],a.State.CurrentOceanLayerIndex[i])]=0d;
            a.Move(1f); Assert.That((long)a.Counter("suitabilityWorsening"),Is.GreaterThan(worse));
            a.Runtime.SetFermentationControls(0f,.7f,.4f,false,.65f,0f,.001f,new Vector2(285.15f,330.15f),20f);
            Assert.That(a.ObserveAt(a.VentCell,4,MetabolismType.Fermentation).reactionReadiness,Is.Zero);
        }
    }

    [Serializable] private sealed class Batch { public int organisms=10000; public double milliseconds; public long allocatedBytes; }
    [Test]
    public void WarmProductionBatchSupportsTenThousandFermentersWithoutPerAgentAllocations()
    {
        using(var f=new GeodesicFermentationRuntimeTests.Fixture()) {
            f.Write(GeodesicOceanResource.OrganicC,1000d); for(int i=0;i<10000;i++) f.Add(energy:1f); f.Step();
            long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp(); f.Step();
            long elapsed=Stopwatch.GetTimestamp()-start,allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Assert.That(f.State.Count,Is.EqualTo(10000)); Assert.That(allocated,Is.LessThan(4096),"Warm public Step should not allocate per organism.");
            Save("performance-10000",new Batch { milliseconds=elapsed*1000d/Stopwatch.Frequency,allocatedBytes=allocated });
        }
    }
}
