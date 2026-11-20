using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class VentStartupSettingsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject root = new GameObject("isolated vent settings test");
        public readonly SimulationStartupController Controller;
        public readonly VentVisualizer Visualizer;
        public readonly string FilePath;

        public Fixture()
        {
            root.SetActive(false); // Never load shared user settings or generate a planet.
            Visualizer = root.AddComponent<VentVisualizer>();
            Controller = root.AddComponent<SimulationStartupController>();
            FilePath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/VentStartupSettingsTests", Guid.NewGuid() + ".json"));
            Set(Controller, "savedConfigFileName", FilePath);
            Set(Controller, "logAppliedStartupConfig", false);
            Set(Controller, "ventVisualizer", Visualizer);
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
    }

    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);

    [Test]
    public void RequestedDefaultsMatchInspectorAndStartupConfig()
    {
        using (var fixture = new Fixture())
        {
            var settings = fixture.Controller.CurrentConfig.ventVisuals;
            Assert.That(settings.enableSmoke && settings.enableHeatDistortion, Is.True);
            Assert.That(settings.glowSizeMultiplier, Is.EqualTo(0.85f));
            Assert.That(settings.smokeBaseWidthMultiplier, Is.EqualTo(1.75f));
            Assert.That(settings.smokeHeightMultiplier, Is.EqualTo(1f));
            Assert.That(settings.smokeIntensityMultiplier, Is.EqualTo(1f));
            Assert.That(fixture.Visualizer.CaptureVisualSettings(), Is.EqualTo(settings));
        }
    }

    [Test]
    public void OldAndPartialProfilesInheritSceneVisualDefaultsWithoutChangingSavedBiology()
    {
        var fallback = new SimulationStartupConfig();
        fallback.ventVisuals.smokeBaseWidthMultiplier = 2.125f;
        var old = SimulationStartupController.DeserializeSavedConfig(
            "{\"version\":24,\"hydrogenToFermentationMutationChance\":0.02}", fallback);
        Assert.That(old.ventVisuals, Is.EqualTo(fallback.ventVisuals));
        Assert.That(old.biology.hydrogenToFermentationMutationChance, Is.EqualTo(0.02f));
        var partial = SimulationStartupController.DeserializeSavedConfig(
            "{\"version\":25,\"ventVisuals\":{\"enableSmoke\":false}}", fallback);
        Assert.That(partial.ventVisuals.enableSmoke, Is.False);
        Assert.That(partial.ventVisuals.enableHeatDistortion, Is.True);
        Assert.That(partial.ventVisuals.smokeBaseWidthMultiplier, Is.EqualTo(2.125f));
    }

    [Test]
    public void ActualPersistenceAndInspectorEditsUseTheSameStartupValues()
    {
        using (var fixture = new Fixture())
        {
            fixture.Controller.CurrentConfig.ventVisuals = new VentVisualSettings
            {
                enableSmoke = false, enableHeatDistortion = true, glowSizeMultiplier = 0.72f,
                smokeBaseWidthMultiplier = 2.1f, smokeHeightMultiplier = 1.4f,
                smokeIntensityMultiplier = 0f, smokeTurbulenceStrength = 0.13f, heatDistortionStrength = 0.017f
            };
            fixture.Controller.CurrentConfig.biology.hydrogenToFermentationMutationChance = 0.02f;
            fixture.Controller.ApplyVentVisualSettings();
            Assert.That(fixture.Visualizer.CaptureVisualSettings(), Is.EqualTo(fixture.Controller.CurrentConfig.ventVisuals));
            fixture.Visualizer.smokeWidthMultiplier = 2.25f;
            typeof(VentVisualizer).GetMethod("OnValidate", Private).Invoke(fixture.Visualizer, null);
            Assert.That(fixture.Controller.CurrentConfig.ventVisuals.smokeBaseWidthMultiplier, Is.EqualTo(2.25f));
            fixture.Controller.SaveCurrentStartupConfig();
            string json = File.ReadAllText(fixture.FilePath);
            Assert.That(json, Does.Contain("\"version\": 25"));
            using (var restarted = new Fixture())
            {
                Set(restarted.Controller, "savedConfigFileName", fixture.FilePath);
                Assert.That((bool)typeof(SimulationStartupController).GetMethod("LoadSavedStartupConfig", Private).Invoke(restarted.Controller, null), Is.True);
                Assert.That(restarted.Controller.CurrentConfig.ventVisuals, Is.EqualTo(fixture.Controller.CurrentConfig.ventVisuals));
                Assert.That(restarted.Visualizer.CaptureVisualSettings(), Is.EqualTo(fixture.Controller.CurrentConfig.ventVisuals));
                Assert.That(restarted.Controller.CurrentConfig.biology.hydrogenToFermentationMutationChance, Is.EqualTo(0.02f));
            }
        }
    }

    [Test]
    public void AdvancedResetIncludesVisualsWhileNormalResetAndClonePreserveThem()
    {
        var defaults = new SimulationStartupConfig();
        var current = defaults.Clone();
        current.ventVisuals.glowSizeMultiplier = 0.6f;
        current.ventVisuals.smokeBaseWidthMultiplier = 2.5f;
        var customized = current.ventVisuals;
        SimulationStartupController.CopyNormalSettings(defaults, current);
        Assert.That(current.ventVisuals, Is.EqualTo(customized));
        var clone = current.Clone();
        clone.ventVisuals.enableSmoke = false;
        Assert.That(current.ventVisuals.enableSmoke, Is.True);
        SimulationStartupController.CopyAdvancedSettings(defaults, current);
        Assert.That(current.ventVisuals, Is.EqualTo(defaults.ventVisuals));
    }

    [Test]
    public void InvalidNumbersNormalizeWithoutTurningExplicitDisableValuesIntoDefaults()
    {
        var settings = VentVisualSettings.Defaults;
        settings.enableSmoke = false;
        settings.enableHeatDistortion = false;
        settings.glowSizeMultiplier = float.NaN;
        settings.smokeBaseWidthMultiplier = 100f;
        settings.smokeHeightMultiplier = -1f;
        settings.smokeIntensityMultiplier = 0f;
        settings.smokeTurbulenceStrength = float.PositiveInfinity;
        settings.heatDistortionStrength = 0f;
        settings = settings.Normalized();
        Assert.That(settings.enableSmoke || settings.enableHeatDistortion, Is.False);
        Assert.That(settings.glowSizeMultiplier, Is.EqualTo(0.85f));
        Assert.That(settings.smokeBaseWidthMultiplier, Is.EqualTo(VentVisualSettings.MaxSmokeWidth));
        Assert.That(settings.smokeHeightMultiplier, Is.Zero);
        Assert.That(settings.smokeIntensityMultiplier, Is.Zero);
        Assert.That(settings.smokeTurbulenceStrength, Is.EqualTo(0.25f));
        Assert.That(settings.heatDistortionStrength, Is.Zero);
    }

    [Test]
    public void InspectorNormalizationKeepsEffectiveValuesAndSavedValuesIdentical()
    {
        using (var fixture = new Fixture())
        {
            fixture.Controller.ApplyVentVisualSettings();
            fixture.Visualizer.smokeHeightMultiplier = 10f;
            fixture.Visualizer.distortionStrength = float.NaN;
            typeof(VentVisualizer).GetMethod("OnValidate", Private).Invoke(fixture.Visualizer, null);
            Assert.That(fixture.Visualizer.smokeHeightMultiplier, Is.EqualTo(VentVisualSettings.MaxSmokeHeight));
            Assert.That(fixture.Visualizer.CaptureVisualSettings(), Is.EqualTo(fixture.Controller.CurrentConfig.ventVisuals));
        }
    }
}
