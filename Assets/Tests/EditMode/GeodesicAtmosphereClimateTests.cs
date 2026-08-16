using NUnit.Framework;
using UnityEngine;

public class GeodesicAtmosphereClimateTests
{
    [Test]
    public void ZeroAtmosphereHasNoGreenhouseWarming()
    {
        Assert.That(Gas(0d, 0.01f, 5f, 50f), Is.Zero);
        Assert.That(Gas(0d, 0.001f, 2f, 15f), Is.Zero);
    }

    [Test]
    public void N2ChangesInertiaWithoutChangingEquilibriumTarget()
    {
        float low = Inertia(0.01d), reference = Inertia(1d), high = Inertia(4d);
        Assert.That(low, Is.LessThan(reference));
        Assert.That(reference, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(high, Is.GreaterThan(reference));
        Assert.That(Gas(0d, 0.01f, 5f, 50f), Is.Zero);
    }

    [Test]
    public void CarbonDioxideAndPressureEffectsRemainDistinctAndMonotonic()
    {
        Assert.That(Gas(0.1d, 0.01f, 5f, 50f), Is.GreaterThan(Gas(0.01d, 0.01f, 5f, 50f)));
        Assert.That(Inertia(0.1d), Is.LessThan(Inertia(1d)));
    }

    [Test]
    public void MethaneWarmingIsMonotonic()
        => Assert.That(Gas(0.01d, 0.001f, 2f, 15f), Is.GreaterThan(Gas(0.001d, 0.001f, 2f, 15f)));

    [Test]
    public void IndividualAndCombinedGreenhouseClampsAreBounded()
    {
        float co2 = Gas(double.MaxValue, 0.01f, 5f, 50f);
        float ch4 = Gas(double.MaxValue, 0.001f, 2f, 15f);
        Assert.That(co2, Is.EqualTo(50f));
        Assert.That(ch4, Is.EqualTo(15f));
        Assert.That(GeodesicAtmosphereClimateModel.CombinedGreenhouseDeltaKelvin(co2, ch4, 60f), Is.EqualTo(60f));
        Assert.That(Inertia(double.MaxValue), Is.EqualTo(4f));
    }

    [Test]
    public void LiveAtmosphereInventoryChangesForcingWithoutReinitialization()
    {
        var owner = new GameObject("dynamic-climate-test");
        try
        {
            var atmosphere = owner.AddComponent<GeodesicAtmosphereField>();
            atmosphere.Configure(100d, 0d, 0d, 0d, 0d, 0d, 0d);
            atmosphere.InitializeForWorld();
            float before = Gas(atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.CO2), 0.01f, 5f, 50f);
            atmosphere.AddGeologicalSource(GeodesicAtmosphericGas.CO2, 10d);
            float after = Gas(atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.CO2), 0.01f, 5f, 50f);
            Assert.That(after, Is.GreaterThan(before));
            Assert.That(atmosphere.IsInitialized, Is.True);
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void AbyssalClimateUsesExplicitKelvinReferenceAndCoupledGreenhouse()
    {
        float baseline = GeodesicOceanThermalModel.AbyssalTargetKelvin(273.15f, 273.15f, 0f, 1.5f, 0.25f);
        float greenhouse = GeodesicOceanThermalModel.AbyssalTargetKelvin(273.15f, 273.15f, 20f, 1.5f, 0.25f);
        float colderBase = GeodesicOceanThermalModel.AbyssalTargetKelvin(263.15f, 273.15f, 0f, 1.5f, 0.25f);
        Assert.That(baseline, Is.EqualTo(274.65f).Within(1e-4f));
        Assert.That(greenhouse - baseline, Is.EqualTo(5f).Within(1e-4f));
        Assert.That(colderBase, Is.EqualTo(272.15f).Within(1e-4f));
    }

    private static float Gas(double pressure, float reference, float sensitivity, float maximum)
        => GeodesicAtmosphereClimateModel.GasGreenhouseDeltaKelvin(pressure, reference, sensitivity, maximum);
    private static float Inertia(double pressure)
        => GeodesicAtmosphereClimateModel.PressureInertiaMultiplier(pressure, 1f, 0.25f, 4f);
}
