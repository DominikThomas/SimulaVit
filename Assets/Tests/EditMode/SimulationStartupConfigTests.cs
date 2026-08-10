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
    }
}
