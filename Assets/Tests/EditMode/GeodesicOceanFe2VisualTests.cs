using NUnit.Framework;

public sealed class GeodesicOceanFe2VisualTests
{
    [Test]
    public void VisibleFe2_OneLayer_UsesLayer0Only()
    {
        Assert.That(GeodesicOceanFe2Visual.CombineVisibleLayers(4f, false, 100f, 0.4f), Is.EqualTo(4f));
    }

    [Test]
    public void VisibleFe2_TwoLayers_UsesNormalizedLayer1Weight()
    {
        float expected = (2f + 6f * 0.4f) / 1.4f;

        Assert.That(GeodesicOceanFe2Visual.CombineVisibleLayers(2f, true, 6f, 0.4f), Is.EqualTo(expected).Within(1e-6f));
    }

    [Test]
    public void VisibleFe2_InactiveLayer1_DoesNotAffectResult()
    {
        Assert.That(GeodesicOceanFe2Visual.CombineVisibleLayers(2f, false, 6f, 0.4f), Is.EqualTo(2f));
    }

    [TestCase(0f, 0f)]
    [TestCase(8f, 1f)]
    [TestCase(12f, 1f)]
    public void DefaultAbsoluteScale_IsClamped(float concentration, float expected)
    {
        Assert.That(GeodesicOceanFe2Visual.NormalizeVisualizedFe2(concentration, 0f, 8f), Is.EqualTo(expected));
    }
}
