using System;
using System.Reflection;
using NUnit.Framework;

public sealed class GeodesicHorizontalMixingActivityTests
{
    private static readonly float[] Enabled = { 1f, 1f, 1f, 1f, 1f, 1f, 1f };

    [Test]
    public void UniformWorld_SkipsEveryChannel()
    { Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(new bool[7], Enabled), Is.Zero); }

    [Test]
    public void LocalizedWrite_ActivatesOnlyWrittenChannel()
    {
        bool[] varying = new bool[7]; varying[(int)GeodesicOceanResource.OrganicC] = true;
        Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(varying, Enabled), Is.EqualTo(1 << (int)GeodesicOceanResource.OrganicC));
    }

    [Test]
    public void VentWriteBeforeMixing_ActivatesVentResourceInSameTick()
    {
        bool[] varying = new bool[7]; varying[(int)GeodesicOceanResource.H2] = true;
        Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(varying, Enabled) & (1 << (int)GeodesicOceanResource.H2), Is.Not.Zero);
    }

    [Test]
    public void VerticalDifference_RemainsActiveForFollowingHorizontalPass()
    {
        bool[] varying = new bool[7]; varying[(int)GeodesicOceanResource.O2] = true;
        Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(varying, Enabled), Is.EqualTo(1 << (int)GeodesicOceanResource.O2));
    }

    [Test]
    public void MultipleActiveChannels_AreCombinedWithoutActivatingOthers()
    {
        bool[] varying = new bool[7]; varying[0] = varying[3] = varying[5] = true;
        Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(varying, Enabled), Is.EqualTo((1 << 0) | (1 << 3) | (1 << 5)));
    }

    [Test]
    public void DenseWorld_ProcessesAllSevenChannels()
    { Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(new[] { true, true, true, true, true, true, true }, Enabled), Is.EqualTo(0x7f)); }

    [Test]
    public void PairTransfer_IsEqualAndOppositeAndZeroTransferDoesNotWrite()
    {
        MethodInfo accumulate = typeof(GeodesicOceanResourceField).GetMethod("AccumulatePair", BindingFlags.NonPublic | BindingFlags.Static);
        float[] state = { 3f, 1f, 2f, 2f }; double[] delta = new double[4];
        accumulate.Invoke(null, new object[] { state, delta, 0, 1, 0.25f });
        Assert.That(delta[0] + delta[1], Is.EqualTo(0d));
        accumulate.Invoke(null, new object[] { state, delta, 2, 3, 0.25f });
        Assert.That(delta[2], Is.EqualTo(0d)); Assert.That(delta[3], Is.EqualTo(0d));
    }

    [Test]
    public void WorldReset_ClearsAllVariationState()
    {
        bool[] varying = { true, true, true, true, true, true, true };
        Array.Clear(varying, 0, varying.Length);
        Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(varying, Enabled), Is.Zero);
    }

    private static readonly int[] SyntheticA = { 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 2, 5 };
    private static readonly int[] SyntheticB = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 1, 8, 11 };
    private static readonly float[] SyntheticConductance = { .08f, .07f, .06f, .05f, .04f, .09f, .03f, .08f, .07f, .06f, .05f, .04f, .025f, .035f };
    private static readonly double[] UnequalVolumes = { .4, .7, 1.1, .55, 1.4, .9, .65, 1.25, .8, 1.6, .5, 1.05 };

    [TestCase("zero")]
    [TestCase("uniform")]
    [TestCase("isolated")]
    [TestCase("plume")]
    [TestCase("gradient")]
    [TestCase("checkerboard")]
    [TestCase("almost-global")]
    [TestCase("global-varying")]
    public void OptimizedDenseKernel_MatchesReferenceAcrossSpatialSupports(string scenario)
    {
        float[] state = BuildScenario(scenario);
        double[] reference = new double[state.Length], optimized = new double[state.Length];
        GeodesicOceanResourceField.AccumulateHorizontalDenseReference(state, reference, SyntheticA, SyntheticB, SyntheticConductance, .1f);
        GeodesicOceanResourceField.AccumulateHorizontalDenseOptimized(state, optimized, SyntheticA, SyntheticB, SyntheticConductance, .1f);
        Assert.That(optimized, Is.EqualTo(reference).AsCollection, scenario);
    }

    [Test]
    public void UnequalVolumes_ConserveInventoryAndProduceDifferentConcentrationChanges()
    {
        float[] state = { 10f, 0f }; double[] delta = new double[2]; double[] volume = { 2.75, .625 };
        int[] a = { 0 }, b = { 1 }; float[] conductance = { .25f };
        double before = Inventory(state, volume);
        GeodesicOceanResourceField.AccumulateHorizontalDenseOptimized(state, delta, a, b, conductance, .1f);
        GeodesicOceanResourceField.ApplyInventoryDeltas(state, delta, volume);
        Assert.That(Inventory(state, volume), Is.EqualTo(before).Within(2e-6));
        Assert.That(10f - state[0], Is.Not.EqualTo(state[1]).Within(1e-6));
    }

    [Test]
    public void IsolatedSupport_ExpandsAcrossFrontierAndMatchesDenseReference()
    {
        float[] reference = new float[12], optimized = new float[12]; reference[0] = optimized[0] = 4f;
        for (int tick = 0; tick < 8; tick++)
        {
            Step(reference, false); Step(optimized, true);
            for (int i = 0; i < optimized.Length; i++)
                Assert.That(optimized[i], Is.EqualTo(reference[i]).Within(1e-6), $"tick {tick + 1}, node {i}");
            Assert.That(optimized[tick == 0 ? 1 : Math.Min(8, tick + 1)], Is.GreaterThanOrEqualTo(0f));
        }
        Assert.That(optimized[1], Is.GreaterThan(0f), "tick one must enter an empty neighbor");
        Assert.That(optimized[8], Is.GreaterThan(0f), "support must continue through later frontier links");
    }

    [Test]
    public void RepeatedHorizontalTicks_ConserveInventoryAndRemainFiniteNonnegative()
    {
        float[] state = BuildScenario("checkerboard"); double before = Inventory(state, UnequalVolumes);
        for (int tick = 0; tick < 1000; tick++) Step(state, true);
        Assert.That(Inventory(state, UnequalVolumes), Is.EqualTo(before).Within(2e-4));
        for (int i = 0; i < state.Length; i++)
            Assert.That(float.IsFinite(state[i]) && state[i] >= 0f, Is.True, $"node {i}");
    }

    [Test]
    public void TinyPositiveValue_IsNotTreatedAsZero()
    {
        float[] state = { float.Epsilon, 0f }; double[] delta = new double[2];
        GeodesicOceanResourceField.AccumulateHorizontalDenseOptimized(state, delta, new[] { 0 }, new[] { 1 }, new[] { 1f }, 1f);
        Assert.That(delta[0], Is.LessThan(0d)); Assert.That(delta[1], Is.GreaterThan(0d));
    }

    [Test]
    public void CatchUpGuard_RetainsBacklogUntilEveryCompleteIntervalIsConsumed()
    {
        const double target = 1000d, interval = 5d; const int guard = 64;
        double cursor = 0d; int renderedFrames = 0, ticks = 0;
        while (GeodesicOceanResourceField.IsTransportTickDue(cursor, target, interval))
        {
            int thisFrame = 0;
            while (GeodesicOceanResourceField.IsTransportTickDue(cursor, target, interval) && thisFrame < guard)
            { cursor += interval; thisFrame++; ticks++; }
            renderedFrames++;
        }
        Assert.That(ticks, Is.EqualTo(200));
        Assert.That(cursor, Is.EqualTo(target));
        Assert.That(renderedFrames, Is.EqualTo(4));
        Assert.That(GeodesicOceanResourceField.IsTransportTickDue(cursor, target + 4.999d, interval), Is.False, "fractional remainder is retained");
    }

    private static void Step(float[] state, bool optimized)
    {
        double[] delta = new double[state.Length];
        if (optimized) GeodesicOceanResourceField.AccumulateHorizontalDenseOptimized(state, delta, SyntheticA, SyntheticB, SyntheticConductance, .1f);
        else GeodesicOceanResourceField.AccumulateHorizontalDenseReference(state, delta, SyntheticA, SyntheticB, SyntheticConductance, .1f);
        GeodesicOceanResourceField.ApplyInventoryDeltas(state, delta, UnequalVolumes);
    }

    private static double Inventory(float[] state, double[] volumes)
    { double sum = 0d; for (int i = 0; i < state.Length; i++) sum += state[i] * volumes[i]; return sum; }

    private static float[] BuildScenario(string scenario)
    {
        var state = new float[12];
        for (int i = 0; i < state.Length; i++) state[i] = scenario switch
        {
            "zero" => 0f,
            "uniform" => 2f,
            "isolated" => i == 0 ? 3f : 0f,
            "plume" => i < 4 ? 4f - i : 0f,
            "gradient" => i * .2f,
            "checkerboard" => (i & 1) == 0 ? 8f : 0f,
            "almost-global" => i == 11 ? 0f : 1f + i * .01f,
            "global-varying" => .1f + (i * 17 % 11) * .3f,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        return state;
    }
}
