using System;
using NUnit.Framework;

public sealed class GeodesicLocomotionMutationTests
{
    public static GeodesicLocomotionMutationPolicy.Settings Settings(bool latest) => new GeodesicLocomotionMutationPolicy.Settings
    { mutation = latest ? .20f : .06f, anchor = latest ? .001f : .015f, upgrade = .05f, allowRelease = true, release = .01f, diagnosticMutation = 1f };

    [TestCase(false)]
    [TestCase(true)]
    public void AnalyticRowsSumToOneAndOnlyExistingPathsHavePositiveProbability(bool latest)
    {
        var s = Settings(latest);
        foreach (bool bottom in new[] { false, true })
        {
            var matrix = GeodesicLocomotionMutationPolicy.TransitionMatrix(s, bottom);
            for (int parent = 0; parent < 4; parent++)
            {
                double total = 0d;
                for (int child = 0; child < 4; child++) { Assert.That(matrix[parent * 4 + child], Is.GreaterThanOrEqualTo(0d)); total += matrix[parent * 4 + child]; }
                Assert.That(total, Is.EqualTo(1d).Within(1e-14));
            }
            foreach (int impossible in new[] { 2, 4, 8, 9, 12, 14 }) Assert.That(matrix[impossible], Is.Zero);
            if (!bottom) foreach (int anchor in new[] { 3, 7, 11 }) Assert.That(matrix[anchor], Is.Zero);
            Assert.That(matrix[13], Is.EqualTo((double)s.mutation * s.anchor * s.release).Within(1e-14));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SampledTransitionsAgreeWithBinomialUncertaintyWithoutEnvironmentalSelection(bool latest)
    {
        const int n = 200000;
        var settings = Settings(latest);
        foreach (bool bottom in new[] { false, true })
        {
            var expected = GeodesicLocomotionMutationPolicy.TransitionMatrix(settings, bottom);
            for (int parent = 0; parent < 4; parent++)
            {
                var random = new Random(283 + parent + (bottom ? 100 : 0)); var counts = new int[4];
                for (int i = 0; i < n; i++) counts[(int)GeodesicLocomotionMutationPolicy.Sample((LocomotionType)parent, bottom, settings, random).target]++;
                for (int child = 0; child < 4; child++)
                {
                    double p = expected[parent * 4 + child], mean = n * p;
                    double bound = 6d * Math.Sqrt(n * p * (1d - p)) + 2d;
                    if (p == 0d) Assert.That(counts[child], Is.Zero);
                    else Assert.That(Math.Abs(counts[child] - mean), Is.LessThanOrEqualTo(bound));
                }
            }
        }
    }

    [Test]
    public void ExtractionPreservesOriginalDecisionsDrawOrderAndFollowingRandomValue()
    {
        foreach (bool latest in new[] { false, true })
        foreach (bool bottom in new[] { false, true })
        for (int parent = -1; parent < 5; parent++)
        {
            var settings = Settings(latest);
            var original = new Random(283 + parent);
            var extracted = new Random(283 + parent);
            for (int i = 0; i < 20000; i++)
            {
                var expected = OriginalDecision((LocomotionType)parent, bottom, settings, original);
                var actual = GeodesicLocomotionMutationPolicy.Sample((LocomotionType)parent, bottom, settings, extracted);
                Assert.That(actual.target, Is.EqualTo(expected));
            }
            Assert.That(extracted.NextDouble(), Is.EqualTo(original.NextDouble()), "Instrumentation must not consume additional gameplay RNG draws.");
        }
    }

    // Literal pre-audit branch structure, retained as a regression oracle rather than
    // calling the new policy to compute its own expected answer.
    private static LocomotionType OriginalDecision(LocomotionType source, bool bottom,
        GeodesicLocomotionMutationPolicy.Settings s, Random random)
    {
        if ((uint)source > (uint)LocomotionType.Anchored) source = LocomotionType.PassiveDrift;
        float chance = s.overrideMutation ? s.diagnosticMutation : s.mutation;
        if (chance <= 0f || random.NextDouble() >= chance) return source;
        LocomotionType target = source;
        if (random.NextDouble() < s.anchor)
        {
            if (source != LocomotionType.Anchored && bottom) target = LocomotionType.Anchored;
            else if (source == LocomotionType.Anchored && s.allowRelease && random.NextDouble() < s.release) target = LocomotionType.Amoeboid;
        }
        if (target == source && random.NextDouble() < s.upgrade)
        {
            if (source == LocomotionType.PassiveDrift) target = LocomotionType.Amoeboid;
            else if (source == LocomotionType.Amoeboid) target = LocomotionType.Flagellum;
        }
        return target;
    }

    private sealed class SequenceRandom : Random
    {
        private readonly double[] values;
        public int Draws { get; private set; }
        public SequenceRandom(params double[] values) { this.values = values; }
        public override double NextDouble() => values[Draws++];
    }

    [Test]
    public void AnchoringHasPriorityAndARejectedBottomGateFallsThroughToUpgrade()
    {
        var s = Settings(true); s.mutation = s.anchor = s.upgrade = 1f;
        var first = new SequenceRandom(0d, 0d, 0d);
        var anchored = GeodesicLocomotionMutationPolicy.Sample(LocomotionType.PassiveDrift, true, s, first);
        Assert.That(anchored.target, Is.EqualTo(LocomotionType.Anchored)); Assert.That(first.Draws, Is.EqualTo(2));
        var fallback = new SequenceRandom(0d, 0d, 0d);
        var amoeboid = GeodesicLocomotionMutationPolicy.Sample(LocomotionType.PassiveDrift, false, s, fallback);
        Assert.That(amoeboid.target, Is.EqualTo(LocomotionType.Amoeboid)); Assert.That(fallback.Draws, Is.EqualTo(3));
    }

    [Test]
    public void AnchoredReleaseIsNestedAndCannotSkipDirectlyToFlagellum()
    {
        var s = Settings(true); s.mutation = s.anchor = s.upgrade = s.release = 1f;
        var random = new SequenceRandom(0d, 0d, 0d);
        var result = GeodesicLocomotionMutationPolicy.Sample(LocomotionType.Anchored, false, s, random);
        Assert.That(result.target, Is.EqualTo(LocomotionType.Amoeboid)); Assert.That(random.Draws, Is.EqualTo(3));
        s.anchor = 0f;
        Assert.That(GeodesicLocomotionMutationPolicy.Sample(LocomotionType.Anchored, true, s, new SequenceRandom(0d, .5d, 0d)).target, Is.EqualTo(LocomotionType.Anchored));
        s.anchor = 1f; s.allowRelease = false;
        Assert.That(GeodesicLocomotionMutationPolicy.Sample(LocomotionType.Anchored, true, s, new SequenceRandom(0d, 0d, 0d)).target, Is.EqualTo(LocomotionType.Anchored));
    }

    [Test]
    public void ZeroAttemptRateInheritsWithoutAnyRandomDrawAndOverrideIsExplicit()
    {
        var s = Settings(true); s.mutation = 0f;
        var random = new SequenceRandom();
        Assert.That(GeodesicLocomotionMutationPolicy.Sample(LocomotionType.Amoeboid, true, s, random).target, Is.EqualTo(LocomotionType.Amoeboid));
        Assert.That(random.Draws, Is.Zero);
        s.overrideMutation = true; s.diagnosticMutation = 1f; s.anchor = 0f; s.upgrade = 1f;
        Assert.That(GeodesicLocomotionMutationPolicy.Sample(LocomotionType.Amoeboid, true, s, new SequenceRandom(0d, .5d, 0d)).target, Is.EqualTo(LocomotionType.Flagellum));
    }
}
