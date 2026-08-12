using NUnit.Framework;

public class GeodesicChemistryTelemetryTests
{
    [Test]
    public void WeightedMeansAndAnoxicFractionUseNodeVolume()
    {
        var stats = new WeightedChemistryStatistics();
        stats.Reset();
        stats.Add(1d, 2f, 0f, 0f, 1f, 0f, 0f, 0f, 0.1f);
        stats.Add(3d, 6f, 2f, 0f, 5f, 0f, 0f, 0f, 0.1f);
        Assert.That(stats.Mean((int)GeodesicOceanResource.CO2), Is.EqualTo(5d).Within(1e-12));
        Assert.That(stats.Mean((int)GeodesicOceanResource.O2), Is.EqualTo(1.5d).Within(1e-12));
        Assert.That(stats.AnoxicFraction, Is.EqualTo(0.25d).Within(1e-12));
        Assert.That(stats.O2Minimum, Is.Zero);
        Assert.That(stats.O2Maximum, Is.EqualTo(2f));
    }

    [Test]
    public void SeparateLayerAccumulatorsPreserveLayerLocality()
    {
        var layer0 = new WeightedChemistryStatistics();
        var layer4 = new WeightedChemistryStatistics();
        layer0.Reset(); layer4.Reset();
        layer0.Add(2d, 0f, 8f, 0f, 0f, 0f, 0f, 0f, 0.01f);
        layer4.Add(5d, 0f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0.01f);
        Assert.That(layer0.Mean((int)GeodesicOceanResource.O2), Is.EqualTo(8d));
        Assert.That(layer4.Mean((int)GeodesicOceanResource.O2), Is.EqualTo(0.5d));
    }

    [Test]
    public void ChemistryCounterDeltaDoesNotResetCumulativeValues()
    {
        var first = new ChemistryCounters(2d, 3d, 4d, 5d, 6d, 7d);
        var second = new ChemistryCounters(3d, 5d, 7d, 9d, 11d, 13d);
        ChemistryCounters delta = second - first;
        Assert.That(delta.H2, Is.EqualTo(1d));
        Assert.That(delta.H2S, Is.EqualTo(2d));
        Assert.That(delta.Fe2, Is.EqualTo(3d));
        Assert.That(delta.O2, Is.EqualTo(4d));
        Assert.That(delta.S0, Is.EqualTo(5d));
        Assert.That(delta.Fe3, Is.EqualTo(6d));
        Assert.That(second.Fe3, Is.EqualTo(13d));
    }


    [Test]
    public void SimulatedTimeSchedulingHandlesPauseAndDisabledInterval()
    {
        Assert.That(GeodesicChemistryTelemetry.IsDue(60d, 59d, 60d), Is.False);
        Assert.That(GeodesicChemistryTelemetry.IsDue(60d, 59d, 60d), Is.False, "stopped authoritative time remains not due");
        Assert.That(GeodesicChemistryTelemetry.IsDue(60d, 60d, 60d), Is.True);
        Assert.That(GeodesicChemistryTelemetry.IsDue(0d, 600d, 60d), Is.False);
        Assert.That(GeodesicChemistryTelemetry.IsDue(-1d, 600d, 60d), Is.False);
    }
}
