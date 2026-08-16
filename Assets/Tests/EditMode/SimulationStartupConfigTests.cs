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
        SimulationStartupController.NormalizeAtmosphereComposition(config);
        Assert.That(config.initialAtmospherePressureBar, Is.EqualTo(0.85f));
        Assert.That(config.atmosphericN2Bar, Is.EqualTo(0.8f).Within(1e-5f));
        Assert.That(config.atmosphericCO2Bar, Is.EqualTo(0.05f).Within(1e-5f));
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
}
