using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicSulfurEcologyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object target,string name,object value) => target.GetType().GetField(name,Private).SetValue(target,value);
    private static object Get(object target,string name) => target.GetType().GetField(name,Private).GetValue(target);
    private static void Call(object target,string name,params object[] values) => target.GetType().GetMethod(name,Private).Invoke(target,values);

    [TestCase(LocomotionType.Amoeboid)][TestCase(LocomotionType.Flagellum)]
    public void SulfurTemporalSamplerAndMovementRespondToH2SButIgnoreH2(LocomotionType phenotype)
    {
        using(var a=new GeodesicLocomotionEcologyExperiments.Fixture(phenotype,true,false,false))
        using(var b=new GeodesicLocomotionEcologyExperiments.Fixture(phenotype,true,false,false))
        {
            foreach(var f in new[]{a,b}) {
                Set(f.Runtime,"movementSulfurCO2",.02f); Set(f.Runtime,"movementSulfurH2S",.001f);
                f.Runtime.SetSulfurControls(1f,false,.6f,0f,0f,.0005f,.001f,new Vector2(333.15f,393.15f),20f);
                for(int i=0;i<f.State.Count;i++) f.State.Metabolism[i]=MetabolismType.SulfurChemosynthesis;
                f.ConfigureAlternative("E-speed"); // Existing opt-in fivefold slower transport; energy costs retained.
            }
            for(int cell=0;cell<a.Grid.CellCount;cell++) for(int layer=0;layer<5;layer++) {
                double h2s=cell==a.VentCell?.01d:.00001d;
                a.WriteHabitat(cell,layer,338.15f,.00001d,100d,h2s);
                b.WriteHabitat(cell,layer,338.15f,100d,100d,h2s);
            }
            var low=a.ObserveAt(a.ColdNeighbor,4,MetabolismType.SulfurChemosynthesis);
            var high=a.ObserveAt(a.VentCell,4,MetabolismType.SulfurChemosynthesis);
            Assert.That(high.reactionReadiness, Is.GreaterThan(low.reactionReadiness));
            var otherH2=b.ObserveAt(b.VentCell,4,MetabolismType.SulfurChemosynthesis);
            Assert.That(otherH2.reactionReadiness, Is.EqualTo(high.reactionReadiness));
            for(int tick=0;tick<100;tick++) { a.Move(.1f); b.Move(.1f); }
            Assert.That(a.Counter("suitabilityWorsening"), Is.EqualTo(b.Counter("suitabilityWorsening")));
            for(int i=0;i<a.State.Count;i++) Assert.That(a.State.Position[i], Is.EqualTo(b.State.Position[i]),"Changing only H2 cannot steer sulfur.");
            long worseningBefore=(long)a.Counter("suitabilityWorsening");
            for(int i=0;i<a.State.Count;i++) a.WriteHabitat(a.State.GeodesicCellIndex[i],a.State.CurrentOceanLayerIndex[i],338.15f,100d,100d,0d);
            a.Move(1f);
            Assert.That((long)a.Counter("suitabilityWorsening"), Is.GreaterThan(worseningBefore), "Actual temporal movement decisions must detect H2S loss.");
            a.WriteHabitat(a.VentCell,4,338.15f,100d,100d,0d);
            Assert.That(a.ObserveAt(a.VentCell,4,MetabolismType.SulfurChemosynthesis).reactionReadiness, Is.Zero);
        }
    }

    [Serializable] public sealed class Cohort {
        public string phenotype, habitat="closed, finite CO2/H2S inventories; 350K; public production Step; seed 123456";
        public bool mutations; public int finalHydrogen,finalSulfur, peakSulfur, sulfurParentBirths;
        public double seconds=80d, carbonResidual, sulfurCarbon, sulfurEnergy, sulfurH2S, sulfurS0;
        public long mutationsAllowed, mutationsBlocked, sulfurBirths, sulfurDeaths;
    }

    [TestCase(LocomotionType.Anchored)][TestCase(LocomotionType.Amoeboid)][TestCase(LocomotionType.Flagellum)]
    public void ControlledFiniteSulfurHabitatSupportsDescendantsAndMatchedMutationControl(LocomotionType phenotype)
    {
        foreach(bool mutations in new[]{false,true})
        using(var f=new GeodesicSulfurRuntimeTests.Fixture(100d,100d)) {
            f.Co2Need=.02f; f.H2SNeed=.01f; f.Energy=.1f; // Saved settings, not the code-default stronger yield.
            f.Controls(1f,false,mutations?.2f:0f,.4f); f.Write(GeodesicOceanResource.H2,100d);
            for(int i=0;i<8;i++) { f.AddAgent(.15f,MetabolismType.Hydrogenotrophy,phenotype); f.State.Energy[i]=.5f; f.State.BiomassTarget[i]=.15f; }
            double initial=f.TotalCarbon;
            var r=new Cohort { phenotype=phenotype.ToString(),mutations=mutations };
            for(int tick=0;tick<800;tick++) {
                // Public runtime owns cadence, competition, locomotion, mutation and lifecycle.
                f.Step(.1f,.5f,.01f); int sulfur=0;
                for(int i=0;i<f.State.Count;i++) if(f.State.Metabolism[i]==MetabolismType.SulfurChemosynthesis) sulfur++;
                r.peakSulfur=Math.Max(r.peakSulfur,sulfur);
            }
            for(int i=0;i<f.State.Count;i++) { if(f.State.Metabolism[i]==MetabolismType.SulfurChemosynthesis) r.finalSulfur++; else r.finalHydrogen++; }
            r.carbonResidual=f.TotalCarbon-initial; r.mutationsAllowed=(long)Get(f.runtime,"sulfurMutationsAllowed");
            r.mutationsBlocked=(long)Get(f.runtime,"sulfurMutationsBlocked"); r.sulfurBirths=(long)Get(f.runtime,"sulfurBirths"); r.sulfurDeaths=(long)Get(f.runtime,"sulfurDeaths");
            r.sulfurParentBirths=(int)(r.sulfurBirths-r.mutationsAllowed);
            r.sulfurCarbon=(double)Get(f.runtime,"sulfurCarbonFixed"); r.sulfurH2S=(double)Get(f.runtime,"sulfurH2SConsumed"); r.sulfurS0=(double)Get(f.runtime,"sulfurProduced");
            r.sulfurEnergy=((double[])Get(f.runtime,"energyByMetabolism"))[(int)MetabolismType.SulfurChemosynthesis];
            Assert.That(Math.Abs(r.carbonResidual), Is.LessThan(1e-4)); Assert.That(r.sulfurS0, Is.EqualTo(r.sulfurH2S));
            if(mutations) { Assert.That(r.mutationsAllowed, Is.GreaterThan(0)); Assert.That(r.sulfurParentBirths, Is.GreaterThan(0)); }
            else Assert.That(r.finalSulfur, Is.Zero);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../Artifacts/SulfurAudit")); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"controlled-"+phenotype+"-"+mutations+".json"),JsonUtility.ToJson(r,true));
        }
    }

    [Test]
    public void ExhaustedH2SCausesMaintenanceStarvationWithoutStoredCarbonRespiration()
    {
        using(var f=new GeodesicSulfurRuntimeTests.Fixture(1d,0d)) {
            f.Controls(); f.AddAgent(.15f); f.State.Energy[0]=.05f; double carbon=f.TotalCarbon;
            for(int i=0;i<10;i++) f.Step(.5f,0f,.02f);
            Assert.That(f.State.Count, Is.Zero); Assert.That((long)Get(f.runtime,"sulfurDeaths"), Is.EqualTo(1));
            Assert.That(f.TotalCarbon, Is.EqualTo(carbon).Within(1e-8)); Assert.That(f.Mineral, Is.Zero);
        }
    }
}
