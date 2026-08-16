using NUnit.Framework;

public sealed class GeodesicVentThermalModelTests
{
    [Test] public void AbyssalTargetDefaultsToColdLiquidWaterAndRetainsBoundedClimateVariation()
    {
        Assert.AreEqual(274.65f, GeodesicOceanThermalModel.AbyssalTargetKelvin(273.15f, 273.15f, 0f, 1.5f, 0.25f), 0.001f);
        Assert.AreEqual(277.15f, GeodesicOceanThermalModel.AbyssalTargetKelvin(283.15f, 273.15f, 0f, 1.5f, 0.25f), 0.001f);
        float twoKilometre = GeodesicOceanThermalModel.ProfileTargetKelvin(288.15f, 274.65f, 0.5f, 1.4f, 0f, 623.15f, 0f);
        float abyss = GeodesicOceanThermalModel.ProfileTargetKelvin(288.15f, 274.65f, 1f, 1.4f, 0f, 623.15f, 0f);
        Assert.Greater(twoKilometre, abyss);
        Assert.AreEqual(274.65f, abyss, 0.001f);
    }

    [Test] public void CoarseVentHeatingIsLocalBoundedAndBottomFocused()
    {
        const float surface = 288.15f, abyss = 274.65f, source = 623.15f;
        float ordinary = GeodesicOceanThermalModel.ProfileTargetKelvin(surface, abyss, 1f, 1.4f, 0f, source, 0.08f);
        float neighboringBottom = GeodesicOceanThermalModel.ProfileTargetKelvin(surface, abyss, 1f, 1.4f, 0.3f, source, 0.08f);
        float sourceAboveBottom = GeodesicOceanThermalModel.ProfileTargetKelvin(surface, abyss, 0.75f, 1.4f, 1f, source, 0.08f * 0.35f);
        float sourceBottom = GeodesicOceanThermalModel.ProfileTargetKelvin(surface, abyss, 1f, 1.4f, 1f, source, 0.08f);
        Assert.Greater(sourceBottom, neighboringBottom);
        Assert.Greater(neighboringBottom, ordinary);
        Assert.Greater(sourceBottom - ordinary, sourceAboveBottom - GeodesicOceanThermalModel.ProfileTargetKelvin(surface, abyss, 0.75f, 1.4f, 0f, source, 0f));
        Assert.Less(sourceBottom, source);
    }

    [Test] public void NoVentAndOutsideRadiusReturnBase()
    {
        Assert.AreEqual(280f, GeodesicVentThermalModel.BlendKelvin(280f, 623.15f, 0f));
        Assert.AreEqual(0f, GeodesicVentThermalModel.EvaluateInfluence(1.25f, 0.25f, 1f, 1f));
    }

    [Test] public void CentreApproachesSourceAndDistanceIsMonotonic()
    {
        float previous = float.PositiveInfinity;
        for (int i = 0; i <= 10; i++) { float influence = GeodesicVentThermalModel.EvaluateInfluence(i / 10f, 0.2f, 0.8f, 1f); Assert.LessOrEqual(influence, previous); Assert.IsTrue(float.IsFinite(influence)); previous = influence; }
        Assert.AreEqual(1f, GeodesicVentThermalModel.EvaluateInfluence(0f, 0.2f, 0.8f, 1f));
        Assert.AreEqual(623.15f, GeodesicVentThermalModel.BlendKelvin(280f, 623.15f, 1f), 0.001f);
    }

    [Test] public void StrengthIsBoundedMonotonicAndMaximumCombinationCannotOvershoot()
    {
        float weak = GeodesicVentThermalModel.EvaluateInfluence(0.25f, 0.1f, 0.9f, 0.25f);
        float strong = GeodesicVentThermalModel.EvaluateInfluence(0.25f, 0.1f, 0.9f, 1f);
        Assert.GreaterOrEqual(strong, weak);
        float combined = UnityEngine.Mathf.Max(strong, weak);
        Assert.LessOrEqual(GeodesicVentThermalModel.BlendKelvin(280f, 623.15f, combined), 623.15f);
    }

    [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
    public void InvalidDistanceProducesFiniteZeroInfluence(float distance) => Assert.AreEqual(0f, GeodesicVentThermalModel.EvaluateInfluence(distance, 0.1f, 1f, 1f));
    [Test] public void VisibleCoreIsFlatAndFalloffBeginsOutsideItsEdge()
    {
        Assert.AreEqual(1f, GeodesicVentThermalModel.EvaluateInfluence(0.08f, 0.08f, 0.12f, 1f));
        Assert.AreEqual(1f, GeodesicVentThermalModel.EvaluateInfluence(0.04f, 0.04f, 0.12f, 1f));
        Assert.Less(GeodesicVentThermalModel.EvaluateInfluence(0.14f, 0.08f, 0.12f, 1f), 1f);
        Assert.AreEqual(0f, GeodesicVentThermalModel.EvaluateInfluence(0.20f, 0.08f, 0.12f, 1f));
    }
    [Test] public void HabitatEligibilityIsBottomOnlyAndCrossHabitatSafe()
    {
        Assert.IsTrue(GeodesicVentThermalModel.IsHabitatEligible(GeodesicVentHabitat.Submarine, true, 4, 4));
        Assert.IsFalse(GeodesicVentThermalModel.IsHabitatEligible(GeodesicVentHabitat.Submarine, true, 3, 4));
        Assert.IsFalse(GeodesicVentThermalModel.IsHabitatEligible(GeodesicVentHabitat.Submarine, false, 0, -1));
        Assert.IsTrue(GeodesicVentThermalModel.IsHabitatEligible(GeodesicVentHabitat.Terrestrial, false, 0, -1));
        Assert.IsFalse(GeodesicVentThermalModel.IsHabitatEligible(GeodesicVentHabitat.Terrestrial, true, 4, 4));
    }

}
