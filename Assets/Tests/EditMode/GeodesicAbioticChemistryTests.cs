using System;
using NUnit.Framework;

public class GeodesicAbioticChemistryTests
{
    private const double Tolerance = 1e-10;

    [Test]
    public void IndividualReactionsUseRequiredStoichiometry()
    {
        AssertReaction(10d, 0d, 0d, 1d, 10d, 0d, 0d, 10d, 5d);
        AssertReaction(0d, 8d, 0d, 1d, 0d, 8d, 0d, 8d, 4d);
        AssertReaction(0d, 0d, 12d, 1d, 0d, 0d, 12d, 12d, 3d);
    }

    [Test]
    public void NoLocalOxygenProducesNoReaction()
    {
        double o2 = 0d, h2 = 3d, h2s = 4d, fe2 = 5d;
        GeodesicAbioticReactionResult result = GeodesicAbioticChemistry.ReactNode(ref o2, ref h2, ref h2s, ref fe2, 1d, 1d, 1d);
        Assert.That(result.consumedO2, Is.Zero); Assert.That(h2, Is.EqualTo(3d)); Assert.That(h2s, Is.EqualTo(4d)); Assert.That(fe2, Is.EqualTo(5d));
        AssertFiniteNonnegative(o2, h2, h2s, fe2, result.reactedH2S, result.reactedFe2);
    }

    [Test]
    public void SharedOxygenLimitScalesEveryRequestedExtentEqually()
    {
        double o2 = 1.25d, h2 = 10d, h2s = 20d, fe2 = 40d;
        GeodesicAbioticReactionResult result = GeodesicAbioticChemistry.ReactNode(ref o2, ref h2, ref h2s, ref fe2, 0.5d, 0.5d, 0.5d);
        double scaleH2 = result.reactedH2 / 5d, scaleH2S = result.reactedH2S / 10d, scaleFe2 = result.reactedFe2 / 20d;
        Assert.That(scaleH2, Is.EqualTo(scaleH2S).Within(Tolerance)); Assert.That(scaleH2, Is.EqualTo(scaleFe2).Within(Tolerance));
        Assert.That(result.consumedO2, Is.EqualTo(1.25d).Within(Tolerance)); Assert.That(o2, Is.Zero.Within(Tolerance));
        Assert.That(result.reactedH2S, Is.GreaterThan(0d)); Assert.That(result.reactedFe2, Is.GreaterThan(0d));
        AssertFiniteNonnegative(o2, h2, h2s, fe2, result.reactedH2S, result.reactedFe2);
    }

    [Test]
    public void ChemistryIsLayerLocalUntilOxygenEntersDeepNode()
    {
        double surfaceO2 = 10d, surfaceH2 = 0d, surfaceH2S = 0d, surfaceFe2 = 0d;
        double deepO2 = 0d, deepH2 = 2d, deepH2S = 3d, deepFe2 = 4d;
        GeodesicAbioticChemistry.ReactNode(ref surfaceO2, ref surfaceH2, ref surfaceH2S, ref surfaceFe2, 1d, 1d, 1d);
        GeodesicAbioticReactionResult isolated = GeodesicAbioticChemistry.ReactNode(ref deepO2, ref deepH2, ref deepH2S, ref deepFe2, 1d, 1d, 1d);
        Assert.That(isolated.consumedO2, Is.Zero); Assert.That(deepH2, Is.EqualTo(2d));
        deepO2 = 1d;
        GeodesicAbioticReactionResult afterTransport = GeodesicAbioticChemistry.ReactNode(ref deepO2, ref deepH2, ref deepH2S, ref deepFe2, 1d, 1d, 1d);
        Assert.That(afterTransport.consumedO2, Is.GreaterThan(0d));
    }

    [Test]
    public void EquivalentConcentrationsScaleReactionInventoryWithNodeVolume()
    {
        GeodesicAbioticReactionResult small = ReactConcentrationsAtVolume(2d);
        GeodesicAbioticReactionResult large = ReactConcentrationsAtVolume(10d);
        Assert.That(large.reactedH2, Is.EqualTo(small.reactedH2 * 5d).Within(Tolerance));
        Assert.That(large.consumedO2, Is.EqualTo(small.consumedO2 * 5d).Within(Tolerance));
    }

    [Test]
    public void ProductsAndOxygenAreConservedAcrossMixedNodes()
    {
        double totalH2S = 0d, totalFe2 = 0d, totalO2 = 0d, expectedO2 = 0d;
        for (int i = 1; i <= 4; i++)
        {
            double o2 = i, h2 = i * 2d, h2s = i * 3d, fe2 = i * 4d;
            GeodesicAbioticReactionResult result = GeodesicAbioticChemistry.ReactNode(ref o2, ref h2, ref h2s, ref fe2, 0.2d, 0.3d, 0.4d);
            totalH2S += result.reactedH2S; totalFe2 += result.reactedFe2; totalO2 += result.consumedO2;
            expectedO2 += 0.5d * result.reactedH2 + 0.5d * result.reactedH2S + 0.25d * result.reactedFe2;
        }
        Assert.That(totalO2, Is.EqualTo(expectedO2).Within(Tolerance));
        Assert.That(totalH2S, Is.GreaterThan(0d), "H2S consumed equals the S0 deposit increment.");
        Assert.That(totalFe2, Is.GreaterThan(0d), "Fe2 consumed equals the oxidized-iron deposit increment; H2 water is intentionally untracked.");
    }

    [TestCase(1d)] [TestCase(2d)] [TestCase(5d)] [TestCase(10d)]
    public void ExponentialHalfLifeIsCadenceStable(double interval)
    {
        double o2 = 1000d, h2 = 10d, h2s = 0d, fe2 = 0d;
        for (double elapsed = 0d; elapsed < 120d; elapsed += interval)
            GeodesicAbioticChemistry.ReactNode(ref o2, ref h2, ref h2s, ref fe2, GeodesicAbioticChemistry.ReactionFraction(interval, 60d), 0d, 0d);
        Assert.That(h2, Is.EqualTo(2.5d).Within(1e-9));
    }

    [Test]
    public void ZeroAuthoritativeDeltaRepresentsPause()
    {
        Assert.That(GeodesicAbioticChemistry.ReactionFraction(0d, 60d), Is.Zero);
        Assert.That(GeodesicAbioticChemistry.ReactionFraction(5d, 0d), Is.Zero);
    }

    private static void AssertReaction(double startH2, double startH2S, double startFe2, double fraction, double expectedH2, double expectedH2S, double expectedFe2, double expectedProduct, double expectedO2)
    {
        double o2 = 100d, h2 = startH2, h2s = startH2S, fe2 = startFe2;
        GeodesicAbioticReactionResult result = GeodesicAbioticChemistry.ReactNode(ref o2, ref h2, ref h2s, ref fe2, fraction, fraction, fraction);
        Assert.That(result.reactedH2, Is.EqualTo(expectedH2).Within(Tolerance)); Assert.That(result.reactedH2S, Is.EqualTo(expectedH2S).Within(Tolerance)); Assert.That(result.reactedFe2, Is.EqualTo(expectedFe2).Within(Tolerance)); Assert.That(result.consumedO2, Is.EqualTo(expectedO2).Within(Tolerance));
        Assert.That(expectedProduct, Is.EqualTo(expectedH2 + expectedH2S + expectedFe2).Within(Tolerance)); AssertFiniteNonnegative(o2, h2, h2s, fe2);
    }

    private static GeodesicAbioticReactionResult ReactConcentrationsAtVolume(double volume)
    { double o2 = 10d * volume, h2 = 2d * volume, h2s = volume, fe2 = 3d * volume; return GeodesicAbioticChemistry.ReactNode(ref o2, ref h2, ref h2s, ref fe2, 0.25d, 0.25d, 0.25d); }
    private static void AssertFiniteNonnegative(params double[] values)
    { foreach (double value in values) Assert.That(!double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d, Is.True); }
}
