using NUnit.Framework;

public sealed class GeodesicVentThermalModelTests
{
    [Test] public void NoVentAndOutsideRadiusReturnBase()
    {
        Assert.AreEqual(280f, GeodesicVentThermalModel.BlendKelvin(280f, 623.15f, 0f));
        Assert.AreEqual(0f, GeodesicVentThermalModel.EvaluateInfluence(1f, 1f, 1f));
    }

    [Test] public void CentreApproachesSourceAndDistanceIsMonotonic()
    {
        float previous = float.PositiveInfinity;
        for (int i = 0; i <= 10; i++) { float influence = GeodesicVentThermalModel.EvaluateInfluence(i / 10f, 1f, 1f); Assert.LessOrEqual(influence, previous); Assert.IsTrue(float.IsFinite(influence)); previous = influence; }
        Assert.AreEqual(1f, GeodesicVentThermalModel.EvaluateInfluence(0f, 1f, 1f));
        Assert.AreEqual(623.15f, GeodesicVentThermalModel.BlendKelvin(280f, 623.15f, 1f), 0.001f);
    }

    [Test] public void StrengthIsBoundedMonotonicAndMaximumCombinationCannotOvershoot()
    {
        float weak = GeodesicVentThermalModel.EvaluateInfluence(0.25f, 1f, 0.25f);
        float strong = GeodesicVentThermalModel.EvaluateInfluence(0.25f, 1f, 1f);
        Assert.GreaterOrEqual(strong, weak);
        float combined = UnityEngine.Mathf.Max(strong, weak);
        Assert.LessOrEqual(GeodesicVentThermalModel.BlendKelvin(280f, 623.15f, combined), 623.15f);
    }

    [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
    public void InvalidDistanceProducesFiniteZeroInfluence(float distance) => Assert.AreEqual(0f, GeodesicVentThermalModel.EvaluateInfluence(distance, 1f, 1f));
    [Test] public void HabitatEligibilityIsBottomOnlyAndCrossHabitatSafe()
    {
        Assert.IsTrue(GeodesicVentThermalModel.IsHabitatEligible(GeodesicVentHabitat.Submarine, true, 4, 4));
        Assert.IsFalse(GeodesicVentThermalModel.IsHabitatEligible(GeodesicVentHabitat.Submarine, true, 3, 4));
        Assert.IsFalse(GeodesicVentThermalModel.IsHabitatEligible(GeodesicVentHabitat.Submarine, false, 0, -1));
        Assert.IsTrue(GeodesicVentThermalModel.IsHabitatEligible(GeodesicVentHabitat.Terrestrial, false, 0, -1));
        Assert.IsFalse(GeodesicVentThermalModel.IsHabitatEligible(GeodesicVentHabitat.Terrestrial, true, 4, 4));
    }

}
