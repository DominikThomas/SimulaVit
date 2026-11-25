using System;
using NUnit.Framework;

public sealed class GeodesicFoodSignalTests
{
    private static double Signal(double h2, double co2, float thermal = 1f) =>
        GeodesicFoodSignal.Readiness(GeodesicFoodSignal.HydrogenotrophyTicks(co2, h2, 1d, .01f, .02f), thermal, 1f);
    private static GeodesicTaxisPolicy.Trend Trend(double before, double after) =>
        GeodesicTaxisPolicy.Compare(after, before, .0001d, .05f, .05f).trend;

    [Test] public void IncreasingLimitingHydrogenImprovesSignalAtUniformViableTemperature()
    { Assert.That(Trend(Signal(.001d, 1d), Signal(.002d, 1d)), Is.EqualTo(GeodesicTaxisPolicy.Trend.Improving)); }
    [Test] public void ImprovingThermalPerformanceImprovesSignalAtUniformHydrogen()
    { Assert.That(Trend(Signal(.01d, 1d, .25f), Signal(.01d, 1d, .5f)), Is.EqualTo(GeodesicTaxisPolicy.Trend.Improving)); }
    [Test] public void CompetingFoodAndThermalGradientsFollowTheirProductRatherThanTheLargestHydrogen()
    {
        Assert.That(Signal(.004d, 1d, .25f), Is.EqualTo(Signal(.001d, 1d, 1f)));
        Assert.That(Signal(.004d, 1d, .25f), Is.LessThan(Signal(.002d, 1d, .75f)));
    }
    [Test] public void CarbonLimitedHydrogenotrophRespondsToCarbonNotExtraHydrogen()
    {
        Assert.That(Signal(1d, .001d), Is.EqualTo(Signal(100d, .001d)));
        Assert.That(Trend(Signal(1d, .001d), Signal(1d, .002d)), Is.EqualTo(GeodesicTaxisPolicy.Trend.Improving));
    }
    [Test] public void LethalThermalPerformanceEliminatesEvenAbundantFoodSignal()
    { Assert.That(Signal(1e6d, 1e6d, 0f), Is.Zero); }
    [Test] public void ThermalZeroErasesHydrogenAndThermalRecoveryInformationOutsideViability()
    {
        Assert.That(Trend(Signal(.001d, 1d, 0f), Signal(1d, 1d, 0f)), Is.EqualTo(GeodesicTaxisPolicy.Trend.Neutral));
        Assert.That(Trend(Signal(.001d, 1d, 0f), Signal(.001d, 1d, .5f)), Is.EqualTo(GeodesicTaxisPolicy.Trend.Improving));
    }
    [Test] public void ExhaustingHydrogenWorsensSignalWithoutAnyTemperatureChange()
    { Assert.That(Trend(Signal(.01d, 1d), Signal(.001d, 1d)), Is.EqualTo(GeodesicTaxisPolicy.Trend.Worsening)); }
    [Test] public void DifferentLayerVolumesCanCreateAReadinessGradientAtUniformConcentrations()
    {
        double a = GeodesicFoodSignal.HydrogenotrophyTicks(.001d, .001d, 1d, .01f, .02f);
        double b = GeodesicFoodSignal.HydrogenotrophyTicks(.001d, .001d, 2d, .01f, .02f);
        Assert.That(b, Is.EqualTo(2d * a));
    }
    [Test] public void MoreThanOneTickOfFoodStillChangesReadinessEvenWhenImmediateReactionRateIsUnchanged()
    { Assert.That(Trend(Signal(1d, 1d), Signal(2d, 2d)), Is.EqualTo(GeodesicTaxisPolicy.Trend.Improving)); }
    [TestCase(0d, .02d)] [TestCase(.01d, 0d)]
    public void DisabledRequiredInputDoesNotAdvertiseAFunctioningHydrogenReaction(double co2Need, double h2Need)
    {
        Assert.That(GeodesicHydrogenotrophyBalance.LimitExtent(1d, co2Need, h2Need, 0f, 6f), Is.Zero);
        Assert.That(GeodesicFoodSignal.HydrogenotrophyTicks(1d, 1d, 1d, co2Need, h2Need), Is.Zero);
    }
    [Test] public void PositiveConfiguredInputsMatchTheOriginalFormulaExactly()
    {
        var random = new Random(283);
        for (int i = 0; i < 100000; i++)
        {
            double co2 = random.NextDouble(), h2 = random.NextDouble(), volume = 1d + random.NextDouble() * 1e6d;
            double a = .001d + random.NextDouble(), b = .001d + random.NextDouble();
            Assert.That(GeodesicFoodSignal.HydrogenotrophyTicks(co2, h2, volume, a, b), Is.EqualTo(Math.Min(co2 * volume / a, h2 * volume / b)));
        }
    }
    [Test] public void IndependentThermalChannelRetainsLocalInformationWhenThereIsNoFood()
    {
        double a = GeodesicFoodAlternatives.Signal("B-independent", 0d, 283.15f, 323.15f, 353.15f, 20f, 0d);
        double b = GeodesicFoodAlternatives.Signal("B-independent", 0d, 293.15f, 323.15f, 353.15f, 20f, 0d);
        Assert.That(b, Is.GreaterThan(a));
        Assert.That(GeodesicFoodAlternatives.Signal("C-smooth", 0d, 293.15f, 323.15f, 353.15f, 20f, 0d), Is.Zero);
    }
    [Test] public void SmoothingAloneCanPreferUnusableFoodOverAWarmStarvedHabitat()
    {
        double cold = GeodesicFoodAlternatives.Signal("C-smooth", 100d, 303.15f, 323.15f, 353.15f, 20f, 0d);
        double warm = GeodesicFoodAlternatives.Signal("C-smooth", .001d, 338.15f, 323.15f, 353.15f, 20f, .001d);
        Assert.That(cold, Is.GreaterThan(warm), "Counterexample to treating any smooth food*temperature score as actual metabolic benefit.");
    }
    [Test] public void BoundedIndependentChannelsDoNotIgnoreSevereTemperatureStressForAbundantFood()
    {
        double cold = GeodesicFoodAlternatives.Signal("B-independent", 1e9d, 233.15f, 323.15f, 353.15f, 20f, 0d);
        double warm = GeodesicFoodAlternatives.Signal("B-independent", .001d, 338.15f, 323.15f, 353.15f, 20f, .001d);
        Assert.That(warm, Is.GreaterThan(cold));
    }
}
