using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicOceanSedimentVisualTests
{
    [Test]
    public void FeSOverridesRustWithDistinctDarkDeposit()
    {
        Color result = GeodesicOceanSedimentVisual.BlendSediments(Color.blue, 0d, 5d, 5d, 5f, Color.yellow, Color.red, Color.black);
        Assert.That(result.r, Is.Zero.Within(1e-6f)); Assert.That(result.g, Is.Zero.Within(1e-6f)); Assert.That(result.b, Is.Zero.Within(1e-6f));
    }

    [Test]
    public void EmptyInventoryPreservesTerrainColour()
    {
        Color original = new Color(0.2f, 0.3f, 0.4f, 1f);
        Assert.That(GeodesicOceanSedimentVisual.BlendSediments(original, 0d, 0d, 0d, 5f, Color.yellow, Color.red, Color.black), Is.EqualTo(original));
    }
}
