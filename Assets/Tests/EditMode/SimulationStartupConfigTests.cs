using NUnit.Framework;

public class SimulationStartupConfigTests
{
    [TestCase(float.NaN, 2f)]
    [TestCase(float.PositiveInfinity, 2f)]
    [TestCase(0f, 2f)]
    [TestCase(-1f, 2f)]
    [TestCase(1.2f, 1f)]
    [TestCase(4.8f, 5f)]
    public void ApproximateThermalCadence_NormalizesSafely(float input, float expected)
    {
        Assert.That(SimulationStartupController.NormalizeToPreset(
            input,
            SimulationStartupController.ApproximateThermalIntervalPresets,
            SimulationStartupController.DefaultApproximateThermalIntervalSeconds), Is.EqualTo(expected));
    }

    [TestCase(float.NaN, 5f)]
    [TestCase(float.NegativeInfinity, 5f)]
    [TestCase(0f, 5f)]
    [TestCase(1.6f, 2f)]
    [TestCase(9f, 10f)]
    public void ResourceTransportCadence_NormalizesSafely(float input, float expected)
    {
        Assert.That(SimulationStartupController.NormalizeToPreset(
            input,
            SimulationStartupController.ResourceTransportIntervalPresets,
            SimulationStartupController.DefaultResourceTransportIntervalSeconds), Is.EqualTo(expected));
    }

    [Test]
    public void NewConfig_UsesValidatedProductionCadences()
    {
        var config = new SimulationStartupConfig();
        Assert.That(config.approximateThermalIntervalSeconds, Is.EqualTo(2f));
        Assert.That(config.geodesicResourceTransportIntervalSeconds, Is.EqualTo(5f));
        Assert.That(config.atmosphereInventoryPerBar, Is.EqualTo(1000f));
        Assert.That(config.airSeaExchangeHalfLifeSeconds, Is.EqualTo(300f));
        Assert.That(config.geodesicBiologySpawnDelaySeconds, Is.Zero);
        SimulationStartupController.NormalizeAtmosphereComposition(config);
        Assert.That(config.initialAtmospherePressureBar, Is.EqualTo(0.85f));
        Assert.That(config.atmosphericN2Bar, Is.EqualTo(0.8f).Within(1e-5f));
        Assert.That(config.atmosphericCO2Bar, Is.EqualTo(0.05f).Within(1e-5f));
        Assert.That(config.ventCO2PerTick, Is.EqualTo(0.02f));
        Assert.That(config.ventH2PerTick, Is.EqualTo(0.006f));
        Assert.That(config.ventH2SPerTick, Is.EqualTo(0.004f));
        Assert.That(config.ventFe2PerTick, Is.EqualTo(0.002f));
    }

    [Test]
    public void NormalResetPreservesEveryAdvancedSetting()
    {
        var defaults = new SimulationStartupConfig();
        var current = CreateCustomizedConfig();
        SimulationStartupConfig advancedBefore = current.Clone();

        SimulationStartupController.CopyNormalSettings(defaults, current);

        AssertNormalSettingsEqual(defaults, current);
        AssertAdvancedSettingsEqual(advancedBefore, current);
    }

    [Test]
    public void AdvancedResetPreservesEveryNormalSettingAndRestoresAirSeaDefault()
    {
        var defaults = new SimulationStartupConfig();
        var current = CreateCustomizedConfig();
        SimulationStartupConfig normalBefore = current.Clone();

        SimulationStartupController.CopyAdvancedSettings(defaults, current);

        AssertAdvancedSettingsEqual(defaults, current);
        AssertNormalSettingsEqual(normalBefore, current);
        Assert.That(current.airSeaExchangeHalfLifeSeconds, Is.EqualTo(300f));
    }

    [Test]
    public void NormalAndAdvancedResetsRemainSequentiallyIndependent()
    {
        var defaults = new SimulationStartupConfig();
        var current = CreateCustomizedConfig();
        SimulationStartupController.CopyNormalSettings(defaults, current);
        current.initialSpawnCount = 777;
        SimulationStartupController.CopyAdvancedSettings(defaults, current);
        Assert.That(current.initialSpawnCount, Is.EqualTo(777));

        current.airSeaExchangeHalfLifeSeconds = 17f;
        SimulationStartupController.CopyNormalSettings(defaults, current);
        Assert.That(current.airSeaExchangeHalfLifeSeconds, Is.EqualTo(17f));
    }

    [Test]
    public void SavedConfigFallbackUsesDefaultButExplicitZeroRemainsDisabled()
    {
        var defaults = new SimulationStartupConfig();
        SimulationStartupConfig missingField = SimulationStartupController.DeserializeSavedConfig(
            "{\"version\":7,\"initialSpawnCount\":42}", defaults);
        Assert.That(missingField.airSeaExchangeHalfLifeSeconds, Is.EqualTo(300f));

        SimulationStartupConfig explicitZero = SimulationStartupController.DeserializeSavedConfig(
            "{\"version\":7,\"airSeaExchangeHalfLifeSeconds\":0}", defaults);
        Assert.That(explicitZero.airSeaExchangeHalfLifeSeconds, Is.Zero);

        SimulationStartupConfig delayedBiology = SimulationStartupController.DeserializeSavedConfig(
            "{\"version\":7,\"geodesicBiologySpawnDelaySeconds\":300}", defaults);
        Assert.That(delayedBiology.geodesicBiologySpawnDelaySeconds, Is.EqualTo(300f));
    }

    [Test]
    public void AtmosphereComposition_NormalizesTraceGasesAndUsesN2Remainder()
    {
        var config = new SimulationStartupConfig
        {
            initialAtmospherePressureBar = 2f,
            atmosphericCO2Fraction = 0.8f,
            atmosphericO2Fraction = 0.8f
        };
        SimulationStartupController.NormalizeAtmosphereComposition(config);
        float sum = config.atmosphericN2Bar + config.atmosphericCO2Bar + config.atmosphericO2Bar + config.atmosphericCH4Bar + config.atmosphericH2Bar + config.atmosphericH2SBar;
        Assert.That(sum, Is.EqualTo(2f).Within(1e-6f));
        Assert.That(config.atmosphericN2Bar, Is.Zero.Within(1e-6f));
        Assert.That(config.atmosphericCO2Bar, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(config.atmosphericO2Bar, Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void DenseAtmosphere_RequiresExplicitAdvancedOptIn()
    {
        var config = new SimulationStartupConfig { initialAtmospherePressureBar = 20f, allowDenseAtmosphere = false };
        SimulationStartupController.NormalizeAtmosphereComposition(config);
        Assert.That(config.initialAtmospherePressureBar, Is.EqualTo(5f));
        config.allowDenseAtmosphere = true;
        config.initialAtmospherePressureBar = 20f;
        SimulationStartupController.NormalizeAtmosphereComposition(config);
        Assert.That(config.initialAtmospherePressureBar, Is.EqualTo(20f));
    }

    [Test]
    public void LegacyPartialPressures_AreExplicitlyMigratedWithoutReinterpretation()
    {
        var config = new SimulationStartupConfig();
        SimulationStartupController.MigrateLegacyAtmospherePartials(config, 20f, 1f, 0.5f, 0f, 0f, 0f);
        Assert.That(config.initialAtmospherePressureBar, Is.EqualTo(21.5f));
        Assert.That(config.allowDenseAtmosphere, Is.True);
        Assert.That(config.atmosphericN2Bar, Is.EqualTo(20f).Within(1e-5f));
        Assert.That(config.atmosphericCO2Bar, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(config.atmosphericO2Bar, Is.EqualTo(0.5f).Within(1e-5f));
    }

    private static SimulationStartupConfig CreateCustomizedConfig()
    {
        return new SimulationStartupConfig
        {
            planetSeed = 9876, useRandomSeed = false, gridType = PlanetGridType.GeodesicIcosphere,
            cubeSphereResolution = 99, axisTiltDegrees = 7f, dayLengthSeconds = 17f, yearLengthInDays = 23f,
            insolationTempGain = 12f, initialCO2 = 3f, initialO2 = 4f, initialCH4 = 5f,
            initialAtmospherePressureBar = 2f, atmosphericCO2Fraction = 0.1f, atmosphericO2Fraction = 0.2f,
            atmosphericCH4Fraction = 0.1f, atmosphericH2Fraction = 0.05f, atmosphericH2SFraction = 0.03f,
            initialDissolvedFe2Plus = 6f, ventClustering = 0.2f, ventH2PerTick = 0.1f,
            ventH2SPerTick = 0.2f, ventCO2PerTick = 0.3f, ventFe2PerTick = 0.4f, initialSpawnCount = 321,
            geodesicSubdivisionLevel = 5, baseTempKelvin = 310f, terrestrialVentFraction = 0.7f,
            allowDenseAtmosphere = true, atmosphereInventoryPerBar = 4321f,
            airSeaExchangeHalfLifeSeconds = 17f, approximateThermalIntervalSeconds = 5f,
            geodesicResourceTransportIntervalSeconds = 10f, chemistryTelemetryIntervalSimSeconds = 19f,
            geodesicBiologySpawnDelaySeconds = 300f
        };
    }

    private static void AssertNormalSettingsEqual(SimulationStartupConfig expected, SimulationStartupConfig actual)
    {
            Assert.That(actual.planetSeed, Is.EqualTo(expected.planetSeed)); Assert.That(actual.useRandomSeed, Is.EqualTo(expected.useRandomSeed));
            Assert.That(actual.gridType, Is.EqualTo(expected.gridType)); Assert.That(actual.cubeSphereResolution, Is.EqualTo(expected.cubeSphereResolution));
            Assert.That(actual.axisTiltDegrees, Is.EqualTo(expected.axisTiltDegrees)); Assert.That(actual.dayLengthSeconds, Is.EqualTo(expected.dayLengthSeconds));
            Assert.That(actual.yearLengthInDays, Is.EqualTo(expected.yearLengthInDays)); Assert.That(actual.insolationTempGain, Is.EqualTo(expected.insolationTempGain));
            Assert.That(actual.initialCO2, Is.EqualTo(expected.initialCO2)); Assert.That(actual.initialO2, Is.EqualTo(expected.initialO2)); Assert.That(actual.initialCH4, Is.EqualTo(expected.initialCH4));
            Assert.That(actual.initialAtmospherePressureBar, Is.EqualTo(expected.initialAtmospherePressureBar));
            Assert.That(actual.atmosphericCO2Fraction, Is.EqualTo(expected.atmosphericCO2Fraction)); Assert.That(actual.atmosphericO2Fraction, Is.EqualTo(expected.atmosphericO2Fraction));
            Assert.That(actual.atmosphericCH4Fraction, Is.EqualTo(expected.atmosphericCH4Fraction)); Assert.That(actual.atmosphericH2Fraction, Is.EqualTo(expected.atmosphericH2Fraction)); Assert.That(actual.atmosphericH2SFraction, Is.EqualTo(expected.atmosphericH2SFraction));
            Assert.That(actual.atmosphericN2Bar, Is.EqualTo(expected.atmosphericN2Bar)); Assert.That(actual.atmosphericCO2Bar, Is.EqualTo(expected.atmosphericCO2Bar));
            Assert.That(actual.atmosphericO2Bar, Is.EqualTo(expected.atmosphericO2Bar)); Assert.That(actual.atmosphericCH4Bar, Is.EqualTo(expected.atmosphericCH4Bar));
            Assert.That(actual.atmosphericH2Bar, Is.EqualTo(expected.atmosphericH2Bar)); Assert.That(actual.atmosphericH2SBar, Is.EqualTo(expected.atmosphericH2SBar));
            Assert.That(actual.initialDissolvedFe2Plus, Is.EqualTo(expected.initialDissolvedFe2Plus)); Assert.That(actual.ventClustering, Is.EqualTo(expected.ventClustering));
            Assert.That(actual.ventH2PerTick, Is.EqualTo(expected.ventH2PerTick)); Assert.That(actual.ventH2SPerTick, Is.EqualTo(expected.ventH2SPerTick));
            Assert.That(actual.ventCO2PerTick, Is.EqualTo(expected.ventCO2PerTick)); Assert.That(actual.ventFe2PerTick, Is.EqualTo(expected.ventFe2PerTick)); Assert.That(actual.initialSpawnCount, Is.EqualTo(expected.initialSpawnCount));
    }

    private static void AssertAdvancedSettingsEqual(SimulationStartupConfig expected, SimulationStartupConfig actual)
    {
            Assert.That(actual.geodesicSubdivisionLevel, Is.EqualTo(expected.geodesicSubdivisionLevel)); Assert.That(actual.baseTempKelvin, Is.EqualTo(expected.baseTempKelvin));
            Assert.That(actual.terrestrialVentFraction, Is.EqualTo(expected.terrestrialVentFraction)); Assert.That(actual.allowDenseAtmosphere, Is.EqualTo(expected.allowDenseAtmosphere));
            Assert.That(actual.atmosphereInventoryPerBar, Is.EqualTo(expected.atmosphereInventoryPerBar)); Assert.That(actual.airSeaExchangeHalfLifeSeconds, Is.EqualTo(expected.airSeaExchangeHalfLifeSeconds));
            Assert.That(actual.approximateThermalIntervalSeconds, Is.EqualTo(expected.approximateThermalIntervalSeconds)); Assert.That(actual.geodesicResourceTransportIntervalSeconds, Is.EqualTo(expected.geodesicResourceTransportIntervalSeconds));
            Assert.That(actual.chemistryTelemetryIntervalSimSeconds, Is.EqualTo(expected.chemistryTelemetryIntervalSimSeconds));
            Assert.That(actual.geodesicBiologySpawnDelaySeconds, Is.EqualTo(expected.geodesicBiologySpawnDelaySeconds));
    }
}
