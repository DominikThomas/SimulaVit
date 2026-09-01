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
    public void FastRejectionSkipsOnlyNodesWithoutPositiveReducedReactants()
    {
        Assert.That(GeodesicAbioticChemistry.HasReducedReactants(0f, 0f, 0f), Is.False);
        Assert.That(GeodesicAbioticChemistry.HasReducedReactants(-1f, float.NaN, 0f), Is.False);
        Assert.That(GeodesicAbioticChemistry.HasReducedReactants(float.Epsilon, 0f, 0f), Is.True, "Tiny positive H2 must retain the existing chemistry path.");
        Assert.That(GeodesicAbioticChemistry.HasReducedReactants(0f, float.Epsilon, 0f), Is.True);
        Assert.That(GeodesicAbioticChemistry.HasReducedReactants(0f, 0f, float.Epsilon), Is.True);
    }

    [Test]
    public void CandidatePredicateRequiresACompleteReactionPair()
    {
        Assert.That(GeodesicAbioticChemistry.CanReact(0f, 1f, 0f, 0f), Is.False, "H2 alone cannot oxidize in an anoxic node.");
        Assert.That(GeodesicAbioticChemistry.CanReact(0f, 0f, 1f, 0f), Is.False, "H2S alone cannot oxidize or precipitate.");
        Assert.That(GeodesicAbioticChemistry.CanReact(0f, 0f, 0f, 1f), Is.False, "Fe2 alone cannot oxidize or precipitate.");
        Assert.That(GeodesicAbioticChemistry.CanReact(1f, float.Epsilon, 0f, 0f), Is.True);
        Assert.That(GeodesicAbioticChemistry.CanReact(0f, 0f, float.Epsilon, float.Epsilon), Is.True);
        Assert.That(GeodesicAbioticChemistry.CanReact(float.NaN, 1f, 0f, 0f), Is.False);
    }

    [Test]
    public void SparseCandidateWorkTracksCompleteReactionPairs()
    {
        const int activeNodes = 112679;
        const int reactiveNodes = 100;
        int[] candidates = new int[activeNodes];
        int count = 0;
        for (int node = 0; node < activeNodes; node++)
        {
            bool reactive = node < reactiveNodes;
            count = GeodesicOceanResourceField.AppendChemistryCandidate(
                node, reactive ? 1f : 0f, 1f, 0f, 0f, candidates, count);
        }

        Assert.That(count, Is.EqualTo(reactiveNodes));
        Assert.That(count, Is.LessThan(activeNodes / 1000 * 2), "Chemistry scan work, unlike the fused post-transport refresh, is proportional to candidates.");
    }

    [Test]
    public void PostTransportCandidateCollectionTracksOnlyCurrentAuthoritativeReactants()
    {
        int[] candidates = new int[4];
        int count = GeodesicOceanResourceField.AppendChemistryCandidate(10, 0f, 0f, 0f, 0f, candidates, 0);
        Assert.That(count, Is.Zero, "A tick with no reduced reactants must not schedule chemistry.");

        count = GeodesicOceanResourceField.AppendChemistryCandidate(10, 0f, 1f, 2f, 3f, candidates, count);
        count = GeodesicOceanResourceField.AppendChemistryCandidate(11, 1f, 0.25f, 0f, 0f, candidates, count);
        count = GeodesicOceanResourceField.AppendChemistryCandidate(12, 0f, 0f, 0.5f, 1f, candidates, count);
        Assert.That(count, Is.EqualTo(3), "Vent, horizontal, and vertical arrivals visible after staged application must be candidates in this tick.");
        CollectionAssert.AreEqual(new[] { 10, 11, 12 }, new ArraySegment<int>(candidates, 0, count));

        count = 0; // Start of the following resource tick.
        count = GeodesicOceanResourceField.AppendChemistryCandidate(10, 0f, 0f, 0f, 0f, candidates, count);
        Assert.That(count, Is.Zero, "A node whose reactants disappeared must naturally leave the rebuilt list.");
    }

    [Test]
    public void DensePostTransportStateCanScheduleEveryActiveNodeWithoutChangingOrder()
    {
        int[] activeNodes = { 2, 5, 9, 14 };
        int[] candidates = new int[activeNodes.Length];
        int count = 0;
        for (int i = 0; i < activeNodes.Length; i++)
            count = GeodesicOceanResourceField.AppendChemistryCandidate(activeNodes[i], 1f, 0f, 0f, 1f, candidates, count);

        Assert.That(count, Is.EqualTo(activeNodes.Length));
        CollectionAssert.AreEqual(activeNodes, candidates, "Dense chemistry must degrade to the original authoritative active-node order.");
    }

    [Test]
    public void ZeroOxygenStillAllowsFeSAfterOxidationNoOp()
    {
        double o2 = 0d, h2 = 0d, h2s = 3d, fe2 = 5d;
        GeodesicAbioticReactionResult oxidation = GeodesicAbioticChemistry.ReactNode(ref o2, ref h2, ref h2s, ref fe2, 1d, 1d, 1d);
        double feS = GeodesicAbioticChemistry.PrecipitateFeS(ref fe2, ref h2s, 1d);
        Assert.That(oxidation.consumedO2, Is.Zero);
        Assert.That(feS, Is.EqualTo(3d));
        Assert.That(fe2, Is.EqualTo(2d));
        Assert.That(h2s, Is.Zero);
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

    [Test]
    public void FeSUsesOneToOneStoichiometryAndRequiresBothReactants()
    {
        double fe2 = 8d, h2s = 3d;
        Assert.That(GeodesicAbioticChemistry.PrecipitateFeS(ref fe2, ref h2s, 1d), Is.EqualTo(3d));
        Assert.That(fe2, Is.EqualTo(5d)); Assert.That(h2s, Is.Zero);
        Assert.That(GeodesicAbioticChemistry.PrecipitateFeS(ref fe2, ref h2s, 1d), Is.Zero, "No H2S means no FeS.");
        fe2 = 0d; h2s = 5d;
        Assert.That(GeodesicAbioticChemistry.PrecipitateFeS(ref fe2, ref h2s, 1d), Is.Zero, "No Fe2 means no FeS.");
    }

    [Test]
    public void NonPositiveHalfLifeDisablesFeSAndPartitioningIsStable()
    {
        double disabledFe = 4d, disabledS = 4d;
        Assert.That(GeodesicAbioticChemistry.PrecipitateFeS(ref disabledFe, ref disabledS, GeodesicAbioticChemistry.ReactionFraction(5d, 0d)), Is.Zero);
        double fe = 10d, sulphide = 10d;
        for (int i = 0; i < 12; i++) GeodesicAbioticChemistry.PrecipitateFeS(ref fe, ref sulphide, GeodesicAbioticChemistry.ReactionFraction(10d, 60d));
        Assert.That(fe, Is.EqualTo(2.5d).Within(1e-9)); Assert.That(sulphide, Is.EqualTo(2.5d).Within(1e-9));
    }

    [Test]
    public void OxidationFirstLeavesOnlyRemainderForFeS()
    {
        double o2 = 0.25d, h2 = 0d, h2s = 10d, fe2 = 2d;
        GeodesicAbioticReactionResult oxidation = GeodesicAbioticChemistry.ReactNode(ref o2, ref h2, ref h2s, ref fe2, 0d, 0d, 1d);
        double feS = GeodesicAbioticChemistry.PrecipitateFeS(ref fe2, ref h2s, 1d);
        Assert.That(oxidation.reactedFe2, Is.EqualTo(1d).Within(Tolerance));
        Assert.That(feS, Is.EqualTo(1d).Within(Tolerance));
        AssertFiniteNonnegative(o2, h2s, fe2, feS);
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
