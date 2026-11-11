using System;
using NUnit.Framework;

public sealed class GeodesicRiverWidthProfileTests
{
    [Test]
    public void FiveBandsSpanTheConfiguredStreamAndMajorTrunkWidths()
    {
        var profile = new GeodesicRiverWidthProfile(2, 4, .0048f, 6f);
        double step = Math.Pow(64, .25);
        var multipliers = new[] { 1f, 1.7f, 2.7f, 4f, 6f };
        for (int i = 0; i < multipliers.Length; i++)
            Assert.That(profile.Width(2 * Math.Pow(step, i)) / .0048f, Is.EqualTo(multipliers[i]).Within(1e-5f));
        Assert.That(profile.Width(0), Is.EqualTo(.0048f));
        Assert.That(profile.Width(500), Is.EqualTo(.0048f * 6f));
    }

    [Test]
    public void WidthGrowsContinuouslyAndRespectsSavedControls()
    {
        var profile = new GeodesicRiverWidthProfile(8, 64, .002f, 5f);
        var higherMajor = new GeodesicRiverWidthProfile(8, 128, .002f, 5f);
        Assert.That(higherMajor.Width(128), Is.LessThan(profile.Width(128)));
        Assert.That(profile.Width(8), Is.EqualTo(.002f));
        Assert.That(profile.Width(10000), Is.EqualTo(.002f * 5f));
        float previous = profile.Width(8);
        for (double runoff = 8.08; runoff <= 2048; runoff *= 1.01)
        {
            float width = profile.Width(runoff);
            Assert.That(width, Is.GreaterThanOrEqualTo(previous));
            Assert.That(width - previous, Is.LessThan(.00003f));
            previous = width;
        }
    }
}
