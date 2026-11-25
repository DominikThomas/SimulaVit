using NUnit.Framework;

public sealed class GeodesicTaxisPolicyTests
{
    [Test]
    public void ModeBUsesNeutralProbabilityEvenWhenReadinessChangesStrongly()
    {
        var improved = GeodesicTaxisPolicy.Compare(10d, 1d, .0001d, .05f, .05f);
        Assert.That(improved.trend, Is.EqualTo(GeodesicTaxisPolicy.Trend.Improving));
        Assert.That(GeodesicTaxisPolicy.ReferenceTumbleProbability(false, true, improved.trend, .2f, .05f, .65f, .2f), Is.EqualTo(.2f));
        Assert.That(GeodesicTaxisPolicy.ReferenceTumbleProbability(true, true, improved.trend, .2f, .05f, .65f, .2f), Is.EqualTo(.05f));
    }

    [Test]
    public void ModeARespondsToBothDirectionsOfAResolvableLocalTemporalGradient()
    {
        Assert.That(GeodesicTaxisPolicy.Compare(2d, 1d, .0001d, .05f, .05f).trend, Is.EqualTo(GeodesicTaxisPolicy.Trend.Improving));
        var worse = GeodesicTaxisPolicy.Compare(0d, 1d, .0001d, .05f, .05f);
        Assert.That(worse.trend, Is.EqualTo(GeodesicTaxisPolicy.Trend.Worsening));
        Assert.That(GeodesicTaxisPolicy.ReferenceTumbleProbability(true, true, worse.trend, .2f, .05f, .65f, .2f), Is.EqualTo(.65f));
    }

    [Test]
    public void ZeroTemperaturePerformanceFlattensBothHydrogenAndTemperatureGradients()
    {
        double coldA = 10d * 0d, coldB = 1000d * 0d;
        Assert.That(GeodesicTaxisPolicy.Compare(coldB, coldA, .0001d, .05f, .05f).trend, Is.EqualTo(GeodesicTaxisPolicy.Trend.Neutral));
        Assert.That(GeodesicTaxisPolicy.Compare(0d, 0d, .0001d, .05f, .05f).normalizedDelta, Is.Zero);
    }

    [Test]
    public void DisplayScoreSaturationDoesNotEliminateUnderlyingReadinessComparison()
    {
        var result = GeodesicTaxisPolicy.Compare(2e12d, 1e12d, .0001d, .05f, .05f);
        Assert.That(result.normalizedDelta, Is.EqualTo(.5f));
        Assert.That(result.trend, Is.EqualTo(GeodesicTaxisPolicy.Trend.Improving));
    }

    [Test]
    public void NoiseFloorAndDeadbandDoNotInventAUsefulGradient()
    {
        Assert.That(GeodesicTaxisPolicy.Compare(1e-10d, 0d, .0001d, .05f, .05f).trend, Is.EqualTo(GeodesicTaxisPolicy.Trend.Neutral));
        Assert.That(GeodesicTaxisPolicy.Compare(1.01d, 1d, .0001d, .05f, .05f).trend, Is.EqualTo(GeodesicTaxisPolicy.Trend.Neutral));
    }
}
