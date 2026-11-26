using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class SimulationStartupProfilesTests
{
    private string dataPath;
    private SimulationStartupProfiles profiles;

    [SetUp]
    public void SetUp()
    {
        dataPath = Path.Combine(Path.GetTempPath(), "SimulaVitProfilesTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataPath);
        profiles = new SimulationStartupProfiles(dataPath);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(dataPath, recursive: true);

    [Test]
    public void NamedProfileRoundTripsAuthoritativeConfigAndEmptyAvailability()
    {
        var config = new SimulationStartupConfig
        {
            gridType = PlanetGridType.GeodesicIcosphere,
            planetSeed = 283,
            useRandomSeed = false,
            geodesicSubdivisionLevel = 5,
            geodesicTargetOceanCoveragePercent = 62.5f,
            geodesicFounderEmergenceMode = GeodesicFounderEmergenceMode.Gradual,
            geodesicFounderOngoingEmergence = true,
            initialSpawnCount = 337,
            enablePerformanceRecording = true,
            performanceSummaryIntervalSeconds = 7f,
            performanceAppendTimestampToCsvFileName = false
        };
        config.biology.geodesicAvailability = default;
        config.biology.hydrogenToFermentationMutationChance = 0.02f;
        config.biology.hydrogenToSulfurMutationChance = 0f;
        config.biology.fermentationEnergyPerTick = 0.091f;
        config.biology.maxPopulation = 10000;
        config.ventVisuals.enableSmoke = false;
        SimulationStartupController.NormalizeAtmosphereComposition(config);
        var fallback = new SimulationStartupConfig();
        fallback.biology.hydrogenToFermentationMutationChance = 0.73f;

        const string name = "Écosystème — 10 000";
        profiles.Create(name, config, null);
        var loaded = new SimulationStartupProfiles(dataPath).Load(name, fallback, out var experiments);

        Assert.That(profiles.List(), Is.EqualTo(new[] { name }));
        Assert.That(loaded.biology.geodesicAvailability.metabolismMask, Is.Zero);
        Assert.That(loaded.biology.geodesicAvailability.locomotionMask, Is.Zero);
        Assert.That(loaded.biology.hydrogenToFermentationMutationChance, Is.EqualTo(0.02f));
        Assert.That(loaded.biology.fermentationEnergyPerTick, Is.EqualTo(0.091f));
        Assert.That(experiments.HasValue, Is.False);
        SimulationStartupController.NormalizeAtmosphereComposition(loaded);
        Assert.That(SimulationStartupController.SerializeSavedConfig(loaded),
            Is.EqualTo(SimulationStartupController.SerializeSavedConfig(config)));
    }

    [Test]
    public void SaveAsRejectsConflictsAndSaveOverwritesOnlyItsTarget()
    {
        profiles.Create("Café", new SimulationStartupConfig { planetSeed = 11 }, null);
        profiles.Create("Another", new SimulationStartupConfig { planetSeed = 22 }, null);
        var replacement = new SimulationStartupConfig { planetSeed = 33 };

        Assert.Throws<IOException>(() => profiles.Create("Café", replacement, null));
        Assert.Throws<IOException>(() => profiles.Create("CAFÉ", replacement, null));
        Assert.Throws<IOException>(() => profiles.Create("Cafe\u0301", replacement, null));
        Assert.That(profiles.Load("Café", null, out _).planetSeed, Is.EqualTo(11));

        profiles.Overwrite("Café", replacement, null);
        Assert.That(profiles.Load("Café", null, out _).planetSeed, Is.EqualTo(33));
        Assert.That(profiles.Load("Another", null, out _).planetSeed, Is.EqualTo(22));
    }

    [Test]
    public void RenamePreservesContentsAndDeleteLeavesOrdinaryConfigAlone()
    {
        string ordinaryPath = Path.Combine(dataPath, "startup_config.json");
        File.WriteAllText(ordinaryPath, "ordinary");
        profiles.Create("Original", new SimulationStartupConfig(), null);
        profiles.Create("Occupied", new SimulationStartupConfig(), null);
        byte[] before = File.ReadAllBytes(ProfilePath("Original"));

        Assert.Throws<IOException>(() => profiles.Rename("Original", "occupied"));
        profiles.Rename("Original", "Trial 2");

        Assert.That(File.Exists(ProfilePath("Original")), Is.False);
        Assert.That(File.ReadAllBytes(ProfilePath("Trial 2")), Is.EqualTo(before));
        profiles.Delete("Trial 2");
        Assert.That(profiles.List(), Is.EqualTo(new[] { "Occupied" }));
        Assert.That(File.ReadAllText(ordinaryPath), Is.EqualTo("ordinary"));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase(" leading")]
    [TestCase("trailing.")]
    [TestCase("../startup_config")]
    [TestCase("..\\startup_config")]
    [TestCase("C:\\external")]
    [TestCase("a:b")]
    [TestCase("NUL.json")]
    [TestCase("COM¹")]
    [TestCase("startup_config")]
    public void InvalidNamesCannotCreateRenameOrDeleteFiles(string name)
    {
        profiles.Create("Original", new SimulationStartupConfig(), null);
        Assert.Throws<ArgumentException>(() => profiles.Create(name, new SimulationStartupConfig(), null));
        Assert.Throws<ArgumentException>(() => profiles.Load(name, null, out _));
        Assert.Throws<ArgumentException>(() => profiles.Rename("Original", name));
        Assert.Throws<ArgumentException>(() => profiles.Delete(name));
        Assert.That(profiles.List(), Is.EqualTo(new[] { "Original" }));
    }

    [TestCase("not json")]
    [TestCase("{\"profileVersion\":1,\"configurationJson\":\"bad json\"}")]
    [TestCase("{\"profileVersion\":1,\"configurationJson\":\"{}\"}")]
    [TestCase("{\"profileVersion\":1,\"configurationJson\":\"{\\\"version\\\":999}\"}")]
    [TestCase("{\"profileVersion\":99,\"configurationJson\":\"{\\\"version\\\":25}\"}")]
    public void InvalidProfilesCannotLoadOrOverwriteAndOtherProfilesStillWork(string json)
    {
        profiles.Create("Valid", new SimulationStartupConfig { planetSeed = 44 }, null);
        File.WriteAllText(ProfilePath("Invalid"), json);

        Assert.Throws<InvalidDataException>(() => profiles.Load("Invalid", null, out _));
        Assert.Throws<InvalidDataException>(() => profiles.Overwrite("Invalid", new SimulationStartupConfig(), null));
        Assert.That(File.ReadAllText(ProfilePath("Invalid")), Is.EqualTo(json));
        Assert.That(profiles.Load("Valid", null, out _).planetSeed, Is.EqualTo(44));
    }

    [Test]
    public void EmbeddedHistoricalConfigUsesExistingMigrationAndSceneFallback()
    {
        const string oldConfig = "{\"version\":6,\"atmosphericN2Bar\":2,\"atmosphericCO2Bar\":0.5}";
        File.WriteAllText(ProfilePath("Historical"), JsonUtility.ToJson(new HistoricalProfile { configurationJson = oldConfig }));
        var fallback = new SimulationStartupConfig();
        fallback.biology.hydrogenToFermentationMutationChance = 0.027f;

        var loaded = profiles.Load("Historical", fallback, out _);

        Assert.That(loaded.initialAtmospherePressureBar, Is.EqualTo(2.5f));
        Assert.That(loaded.atmosphericCO2Fraction, Is.EqualTo(0.2f));
        Assert.That(loaded.biology.geodesicAvailability.metabolismMask, Is.EqualTo(GeodesicBiologyAvailability.CoreMetabolisms));
        Assert.That(loaded.biology.geodesicAvailability.locomotionMask, Is.EqualTo(GeodesicBiologyAvailability.AllLocomotions));
        Assert.That(loaded.biology.hydrogenToFermentationMutationChance, Is.EqualTo(0.027f));
    }

    [Test]
    public void ExperimentSettingsRequireOptInToCaptureAndApply()
    {
        var config = new SimulationStartupConfig { enablePerformanceRecording = true };
        profiles.Create("Basic", config, null);
        profiles.Create("Detailed", config,
            new SimulationStartupProfiles.ExperimentSettings(true, LifecycleThermalSamplingMode.Optimized));

        profiles.Load("Detailed", null, out var detailedSettings);
        bool detailed = false;
        var mode = LifecycleThermalSamplingMode.Reference;
        SimulationStartupController.ResolveProfileExperiments(false, detailedSettings, ref detailed, ref mode);
        Assert.That(detailed, Is.False);
        Assert.That(mode, Is.EqualTo(LifecycleThermalSamplingMode.Reference));

        SimulationStartupController.ResolveProfileExperiments(true, detailedSettings, ref detailed, ref mode);
        Assert.That(detailed, Is.True);
        Assert.That(mode, Is.EqualTo(LifecycleThermalSamplingMode.Optimized));

        var loaded = profiles.Load("Basic", null, out var missingSettings);
        SimulationStartupController.ResolveProfileExperiments(true, missingSettings, ref detailed, ref mode);
        Assert.That(detailed, Is.True);
        Assert.That(mode, Is.EqualTo(LifecycleThermalSamplingMode.Optimized));
        StringAssert.DoesNotContain("lifecycleThermalSamplingMode", SimulationStartupController.SerializeSavedConfig(loaded));
        StringAssert.DoesNotContain("detailedGeodesicProfiling", SimulationStartupController.SerializeSavedConfig(loaded));
    }

    [Test]
    public void DisabledAutoSaveLeavesOrdinaryFileUntouchedAndExplicitProfilesStillSave()
    {
        string ordinaryPath = Path.Combine(dataPath, "startup_config.json");
        File.WriteAllText(ordinaryPath, "existing setup");
        var owner = new GameObject("Uninitialized startup controller");
        owner.SetActive(false);
        try
        {
            var controller = owner.AddComponent<SimulationStartupController>();
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(SimulationStartupController);
            type.GetField("savedConfigFileName", instance).SetValue(controller, ordinaryPath);
            type.GetField("saveStartupConfigOnStart", instance).SetValue(controller, false);
            type.GetField("logAppliedStartupConfig", instance).SetValue(controller, false);
            var uiState = type.GetField("startupUiState", instance);
            uiState.SetValue(controller, Enum.Parse(uiState.FieldType, "ConfigScreen"));
            type.GetField("profileStore", instance).SetValue(controller, profiles);

            // Generated startup, Back and application quit funnel through these methods.
            type.GetMethod("SaveLastUsedStartupConfig", instance).Invoke(controller, null);
            type.GetMethod("SaveSetupDraft", instance).Invoke(controller, null);
            type.GetMethod("OnApplicationQuit", instance).Invoke(controller, null);
            type.GetMethod("SaveSelectedProfile", instance).Invoke(controller, new object[] { "Explicit", false });

            Assert.That(File.ReadAllText(ordinaryPath), Is.EqualTo("existing setup"));
            Assert.That(profiles.List(), Is.EqualTo(new[] { "Explicit" }));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    [Test]
    public void CaptureSnapshotMatchesCsvStemAndSeparatesRequestedAndResolvedSeed()
    {
        string csv = Path.Combine(dataPath, "simulation_performance_20261126_005950_509_2.csv");
        File.WriteAllText(csv, "header\n");
        var effective = new SimulationStartupConfig { planetSeed = 847, useRandomSeed = false, enablePerformanceRecording = true };
        var experiments = new SimulationStartupProfiles.ExperimentSettings(true, LifecycleThermalSamplingMode.Optimized);

        SimulationStartupController.WriteStartupCaptureSnapshot(csv, effective, 283, true, "Trial", true, experiments);

        string path = Path.Combine(dataPath, "simulation_performance_20261126_005950_509_2.config.json");
        var snapshot = JsonUtility.FromJson<SnapshotProbe>(File.ReadAllText(path));
        Assert.That(snapshot.sourceProfileName, Is.EqualTo("Trial"));
        Assert.That(snapshot.profileModified, Is.True);
        Assert.That(snapshot.configuredSeed, Is.EqualTo(283));
        Assert.That(snapshot.configuredUseRandomSeed, Is.True);
        Assert.That(snapshot.resolvedWorldSeed, Is.EqualTo(847));
        Assert.That(snapshot.configuration.planetSeed, Is.EqualTo(847));
        Assert.That(snapshot.configuration.useRandomSeed, Is.False);
        Assert.That(snapshot.configuration.enablePerformanceRecording, Is.True);
        Assert.That(snapshot.experimentSettings.detailedGeodesicProfiling, Is.True);
        Assert.That(snapshot.experimentSettings.ThermalMode, Is.EqualTo(LifecycleThermalSamplingMode.Optimized));
        Assert.Throws<IOException>(() =>
            SimulationStartupController.WriteStartupCaptureSnapshot(csv, effective, 0, false, null, false, experiments));
        Assert.That(File.ReadAllText(csv), Is.EqualTo("header\n"));
    }

    private string ProfilePath(string name) => Path.Combine(profiles.OpenDirectory(), name + ".profile.json");

    [Serializable]
    private sealed class HistoricalProfile
    {
        public int profileVersion = 1;
        public string configurationJson;
    }

    [Serializable]
    private sealed class SnapshotProbe
    {
        [Serializable]
        public sealed class Configuration
        {
            public int planetSeed;
            public bool useRandomSeed, enablePerformanceRecording;
        }

        public string sourceProfileName;
        public bool profileModified, configuredUseRandomSeed;
        public int configuredSeed, resolvedWorldSeed;
        public Configuration configuration;
        public SimulationStartupProfiles.ExperimentSettings experimentSettings;
    }
}
