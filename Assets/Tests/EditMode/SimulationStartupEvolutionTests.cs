using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class SimulationStartupEvolutionTests
{
    internal const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static object Get(object owner, string name) => owner.GetType().GetField(name, Private).GetValue(owner);
    internal static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    internal static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private).Invoke(owner, args);
    internal const string General = "General metabolism mutation chance";
    internal const string Sulfur = "Hydrogen → sulfur chemosynthesis probability";
    private static readonly (string label, string field, bool geodesicEditable)[] OtherControls = {
        ("Hydrogen → photosynthesis probability", "hydrogenToPhotosynthesisMutationChance", true),
        ("Hydrogen → saprotrophy probability", "hydrogenToSaprotrophyMutationChance", true),
        ("Fermentation → methanogenesis probability", "fermentationToMethanogenesisMutationChance", true),
        ("Saprotrophy mutation chance", "saprotrophyMutationChance", true),
        ("Hydrogen → methanotrophy probability", "hydrogenToMethanotrophyMutationChance", false),
        ("Predator mutation chance", "predatorMutationChance", false),
        ("Biomass mutation chance", "biomassMutationChance", false),
        ("Biomass mutation scale", "biomassMutationScale", false) };

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject root = new GameObject("isolated startup evolution tests");
        public readonly SimulationStartupController Controller;
        public readonly string Path;
        public Fixture(PlanetGridType grid = PlanetGridType.GeodesicIcosphere)
        {
            root.SetActive(false); // No normal startup, world generation, live scene search or shared-config load.
            Controller = root.AddComponent<SimulationStartupController>();
            Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../Temp/StartupEvolutionAudit", Guid.NewGuid()+".json"));
            Set(Controller,"savedConfigFileName",Path); Set(Controller,"saveStartupConfigOnStart",false);
            Set(Controller,"logAppliedStartupConfig",false);
            Set(Controller,"currentConfig",new SimulationStartupConfig { gridType = grid });
            Set(Controller,"defaults",new SimulationStartupConfig { gridType = grid });
        }
        public void Dispose() { UnityEngine.Object.DestroyImmediate(root); if(File.Exists(Path)) File.Delete(Path); }
    }

    [UnityTest]
    [Timeout(15000)]
    public IEnumerator GeodesicFieldsAcceptInputAndActualSaveLoadRestoresThem()
    {
        RequireGraphics();
        using(var f = new Fixture())
        {
            StartupEvolutionTestWindow window = ScriptableObject.CreateInstance<StartupEvolutionTestWindow>();
            try {
                window.Controller=f.Controller; window.position=new Rect(0,0,900,900); window.ShowUtility();
                yield return Ready(window);
                Assert.That(f.Controller.CurrentConfig.biology.metabolismMutationChance,Is.EqualTo(.01f));
                Assert.That(f.Controller.CurrentConfig.biology.hydrogenToSulfurMutationChance,Is.EqualTo(.003f));
                float photosynthesisBefore=f.Controller.CurrentConfig.biology.hydrogenToPhotosynthesisMutationChance;
                yield return Edit(window,General,"0.2"); yield return Edit(window,Sulfur,"0.4");
                yield return Edit(window,"Hydrogen → fermentation probability","0.27");
                yield return Edit(window,"Fermentation → methanogenesis probability","0.31");
                Assert.That(f.Controller.CurrentConfig.biology.hydrogenToFermentationMutationChance,Is.EqualTo(.27f));
                Assert.That(f.Controller.CurrentConfig.biology.metabolismMutationChance,Is.EqualTo(.2f));
                Assert.That(f.Controller.CurrentConfig.biology.hydrogenToSulfurMutationChance,Is.EqualTo(.4f));
                Assert.That(f.Controller.CurrentConfig.biology.hydrogenToPhotosynthesisMutationChance,Is.EqualTo(photosynthesisBefore));
                Assert.That(f.Controller.CurrentConfig.biology.fermentationToMethanogenesisMutationChance,Is.EqualTo(.31f));
                f.Controller.CurrentConfig.biology.geodesicMethanogenFounderCount = 3;
                f.Controller.SaveCurrentStartupConfig();
                Assert.That(File.Exists(f.Path),Is.True,"The real startup writer must write only the isolated test path.");
                using(var restarted = new Fixture()) {
                    Set(restarted.Controller,"savedConfigFileName",f.Path);
                    Assert.That((bool)Call(restarted.Controller,"LoadSavedStartupConfig"),Is.True);
                    Assert.That(restarted.Controller.CurrentConfig.biology.metabolismMutationChance,Is.EqualTo(.2f));
                    Assert.That(restarted.Controller.CurrentConfig.biology.hydrogenToSulfurMutationChance,Is.EqualTo(.4f));
                    Assert.That(restarted.Controller.CurrentConfig.biology.hydrogenToFermentationMutationChance,Is.EqualTo(.27f));
                    Assert.That(restarted.Controller.CurrentConfig.biology.fermentationToMethanogenesisMutationChance,Is.EqualTo(.31f));
                    Assert.That(restarted.Controller.CurrentConfig.biology.geodesicMethanogenFounderCount,Is.EqualTo(3));
                }
                Assert.That(File.ReadAllText(f.Path),Does.Contain("\"version\": 25"));
            } finally { window.Close(); }
        }
    }

    [UnityTest]
    [Timeout(15000)]
    public IEnumerator UnsupportedGeodesicControlsIgnoreInputAndPreserveExistingValues()
    {
        RequireGraphics();
        using(var f=new Fixture()) {
            var window=ScriptableObject.CreateInstance<StartupEvolutionTestWindow>();
            try {
                window.Controller=f.Controller; window.position=new Rect(0,0,900,900); window.ShowUtility(); yield return Ready(window);
                foreach (var control in OtherControls) {
                    if (control.geodesicEditable) continue;
                    var field=typeof(SimulationStartupBiologyConfig).GetField(control.field); object before=field.GetValue(f.Controller.CurrentConfig.biology);
                    yield return Edit(window,control.label,"0.333"); Assert.That(field.GetValue(f.Controller.CurrentConfig.biology),Is.EqualTo(before),control.field);
                }
                Assert.That(GUI.enabled,Is.True);
            } finally { window.Close(); }
        }
    }

    [UnityTest]
    [Timeout(15000)]
    public IEnumerator LegacyEvolutionKeepsEveryExistingTransitionAndBiomassControlEditable()
    {
        RequireGraphics();
        using(var f=new Fixture(PlanetGridType.LegacyCubeSphere)) {
            var window=ScriptableObject.CreateInstance<StartupEvolutionTestWindow>();
            try {
                window.Controller=f.Controller; window.position=new Rect(0,0,900,900); window.ShowUtility(); yield return Ready(window);
                foreach (var control in OtherControls) {
                    yield return Edit(window,control.label,"0.333");
                    Assert.That((float)typeof(SimulationStartupBiologyConfig).GetField(control.field).GetValue(f.Controller.CurrentConfig.biology),Is.EqualTo(.333f),control.field);
                }
            } finally { window.Close(); }
        }
    }

    [UnityTest]
    [Timeout(15000)]
    public IEnumerator GeodesicFermentationParametersAreEditableAndPersistThroughTheExistingWriter()
    {
        RequireGraphics();
        using(var f=new Fixture()) {
            var window=ScriptableObject.CreateInstance<StartupEvolutionTestWindow>();
            try {
                window.Controller=f.Controller; window.DrawMethod="DrawMetabolismParameters"; Set(f.Controller,"selectedMetabolism",2);
                window.position=new Rect(0,0,900,900); window.ShowUtility(); yield return Ready(window,false);
                yield return Edit(window,"Organic carbon consumed / tick","0.031"); yield return Edit(window,"Energy parameter / reaction tick","0.63"); yield return Edit(window,"Carbon assimilation fraction","0.27");
                var b=f.Controller.CurrentConfig.biology;
                Assert.That(b.fermentationOrganicCPerTick,Is.EqualTo(.031f)); Assert.That(b.fermentationEnergyPerTick,Is.EqualTo(.63f)); Assert.That(b.fermentationAssimilationFraction,Is.EqualTo(.27f));
                f.Controller.SaveCurrentStartupConfig();
                using(var loaded=new Fixture()) { Set(loaded.Controller,"savedConfigFileName",f.Path); Assert.That((bool)Call(loaded.Controller,"LoadSavedStartupConfig"),Is.True);
                    Assert.That(loaded.Controller.CurrentConfig.biology.fermentationOrganicCPerTick,Is.EqualTo(.031f)); Assert.That(loaded.Controller.CurrentConfig.biology.fermentationEnergyPerTick,Is.EqualTo(.63f)); Assert.That(loaded.Controller.CurrentConfig.biology.fermentationAssimilationFraction,Is.EqualTo(.27f)); }
            } finally { window.Close(); }
        }
    }

    private static void RequireGraphics()
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)
            Assert.Ignore("Native IMGUI input requires a graphics device; run ValidateSulfur.ps1 with -Graphics -AllowStartupTestFiles.");
    }
    private static IEnumerator Ready(StartupEvolutionTestWindow window,bool checkMutationLabels=true)
    {
        double deadline=EditorApplication.timeSinceStartup+4d;
        while(window.Draws<2 && EditorApplication.timeSinceStartup<deadline) { window.Repaint(); yield return null; }
        Assert.That(window.Error,Is.Null); Assert.That(window.Draws,Is.GreaterThanOrEqualTo(2),"Native IMGUI must actually execute, not merely compile.");
        var labels=(Dictionary<string,GUIContent>)Get(window.Controller,"setupLabelContents");
        if(checkMutationLabels) { Assert.That(labels.ContainsKey(General),Is.True); Assert.That(labels.ContainsKey(Sulfur),Is.True); }
    }
    private static IEnumerator Edit(StartupEvolutionTestWindow window,string label,string text)
    {
        window.FocusLabel=label; window.Focus(); window.Repaint(); yield return null; yield return null;
        window.SendEvent(Event.KeyboardEvent("^a"));
        foreach(char c in text) window.SendEvent(new Event { type=EventType.KeyDown, character=c });
        window.Repaint(); yield return null; Assert.That(window.Error,Is.Null);
    }

    [TestCase(.2f,.4f)][TestCase(0f,.4f)][TestCase(.2f,0f)]
    public void EditedProbabilitiesSurviveActualFileSaveAndFreshControllerLoad(float general,float sulfur)
    {
        using(var f=new Fixture()) {
            f.Controller.CurrentConfig.biology.metabolismMutationChance=general; f.Controller.CurrentConfig.biology.hydrogenToSulfurMutationChance=sulfur;
            f.Controller.SaveCurrentStartupConfig(); Assert.That(File.Exists(f.Path),Is.True);
            using(var restarted=new Fixture()) { Set(restarted.Controller,"savedConfigFileName",f.Path); Assert.That((bool)Call(restarted.Controller,"LoadSavedStartupConfig"),Is.True);
                Assert.That(restarted.Controller.CurrentConfig.biology.metabolismMutationChance,Is.EqualTo(general));
                Assert.That(restarted.Controller.CurrentConfig.biology.hydrogenToSulfurMutationChance,Is.EqualTo(sulfur)); }
        }
    }

    [Test]
    public void ResetUsesConfiguredDefaultsAndExistingNormalizationHandlesInvalidInputs()
    {
        using(var f=new Fixture()) {
            var defaults=new SimulationStartupConfig(); defaults.biology.metabolismMutationChance=.17f; defaults.biology.hydrogenToSulfurMutationChance=.29f;
            Set(f.Controller,"defaults",defaults); f.Controller.CurrentConfig.biology.metabolismMutationChance=.9f; f.Controller.CurrentConfig.biology.hydrogenToSulfurMutationChance=.8f;
            f.Controller.ResetDefaults(); Assert.That(f.Controller.CurrentConfig.biology.metabolismMutationChance,Is.EqualTo(.17f)); Assert.That(f.Controller.CurrentConfig.biology.hydrogenToSulfurMutationChance,Is.EqualTo(.29f));
            var b=f.Controller.CurrentConfig.biology; b.metabolismMutationChance=float.NaN; b.hydrogenToSulfurMutationChance=2f; b.Normalize(defaults.biology);
            Assert.That(b.metabolismMutationChance,Is.EqualTo(.17f)); Assert.That(b.hydrogenToSulfurMutationChance,Is.EqualTo(1f));
            b.metabolismMutationChance=-1f; b.hydrogenToSulfurMutationChance=float.PositiveInfinity; b.Normalize(defaults.biology);
            Assert.That(b.metabolismMutationChance,Is.Zero); Assert.That(b.hydrogenToSulfurMutationChance,Is.EqualTo(.29f));
        }
    }

    [TestCase(.2f,.4f,true)][TestCase(0f,1f,false)][TestCase(1f,0f,false)]
    public void StartupSelectionReachesEffectiveManagerControlsBeforeActualGeodesicBirths(float general,float sulfur,bool expectSulfur)
    {
        using(var f=new GeodesicSulfurRuntimeTests.Fixture(100d,100d)) {
            var planet=(PlanetGenerator)Get(f.runtime,"planet"); var manager=planet.gameObject.AddComponent<ReplicatorManager>();
            manager.planetGenerator=planet; Set(manager,"geodesicBiology",f.runtime); Set(manager,"agents",f.agents); Set(manager,"populationState",f.State);
            var b=new SimulationStartupBiologyConfig { metabolismMutationChance=general,hydrogenToSulfurMutationChance=sulfur,
                hydrogenToPhotosynthesisMutationChance=1f,hydrogenToSaprotrophyMutationChance=1f,hydrogenToFermentationMutationChance=0f,
                hydrogenToMethanotrophyMutationChance=1f,saprotrophyMutationChance=1f,predatorMutationChance=1f,fermentationToMethanogenesisMutationChance=1f,
                locomotionMutationChance=0f,tempMutationChance=0f,reproductionRate=2f,basalEnergyCostPerSecond=0f };
            // Inspector-like starting values must lose to the selected startup configuration.
            manager.metabolismMutationChance=.91f; manager.hydrogenToSulfurMutationChance=.92f; b.Apply(manager);
            for(int i=0;i<64;i++) { f.AddAgent(1f,MetabolismType.Hydrogenotrophy); f.State.Energy[i]=5f; }
            Assert.That(manager.RunGeodesicBiologyStep(.5f),Is.True); Assert.That(f.State.Count,Is.EqualTo(128));
            Assert.That((float)Get(f.runtime,"metabolicMutationChance"),Is.EqualTo(general)); Assert.That((float)Get(f.runtime,"hydrogenSulfurChance"),Is.EqualTo(sulfur));
            int mutants=0; for(int i=64;i<f.State.Count;i++) { if(f.State.Metabolism[i]==MetabolismType.SulfurChemosynthesis) mutants++;
                else Assert.That(f.State.Metabolism[i],Is.EqualTo(MetabolismType.Hydrogenotrophy),"Other transitions must respect their source metabolism and availability."); }
            Assert.That(mutants>0,Is.EqualTo(expectSulfur));
            Assert.That(manager.metabolismMutationChance,Is.EqualTo(general)); Assert.That(manager.hydrogenToSulfurMutationChance,Is.EqualTo(sulfur));
        }
    }
}

public sealed class StartupEvolutionTestWindow : EditorWindow
{
    public SimulationStartupController Controller;
    public string DrawMethod="DrawMetabolicEvolution";
    public string FocusLabel;
    public int Draws;
    public Exception Error;
    private void OnGUI()
    {
        if(Controller==null) return;
        try {
            GUI.enabled=true;
            SimulationStartupEvolutionTests.Call(Controller,"EnsureStyles"); SimulationStartupEvolutionTests.Call(Controller,"EnsureSetupStyles");
            GUILayout.BeginArea(new Rect(0,0,900,2000)); SimulationStartupEvolutionTests.Call(Controller,DrawMethod,Controller.CurrentConfig.biology); GUILayout.EndArea();
            if(FocusLabel!=null) GUI.FocusControl(FocusLabel);
            Draws++;
        } catch(Exception e) { Error=e.InnerException??e; }
    }
}
