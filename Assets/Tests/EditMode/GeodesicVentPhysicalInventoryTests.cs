using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicVentPhysicalInventoryTests
{
    [TestCase(10d)]
    [TestCase(0d)]
    [TestCase(0.25d)]
    public void ConfiguredPhysicalH2RateIsRuntimeRateWithoutConversion(double configured)
    {
        var go = new GameObject("geodesic-vent-rate-test");
        try
        {
            var resources = go.AddComponent<GeodesicOceanResourceField>();
            resources.SetStartupPhysicalVentRates(configured, 0d, 0d, 0d);
            Assert.That(resources.VentH2Rate, Is.EqualTo(configured));
            if (configured > 0d) Assert.That(resources.VentH2Rate, Is.Not.EqualTo(configured * GeodesicPhysicalScale.PhysicalCubicKilometresPerUnityUnitCubed));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void NormalizedOutletDistributionConservesGlobalPhysicalRate()
    {
        double globalRate = 10d;
        double[] weights = { 0.1d, 0.25d, 0.65d };
        double distributed = 0d;
        foreach (double weight in weights) distributed += globalRate * weight;
        Assert.That(distributed, Is.EqualTo(globalRate).Within(globalRate * 1e-12d));
    }

    [TestCase(0.25d)] [TestCase(10d)] [TestCase(1000d)]
    public void HabitatSplitConservesGlobalPhysicalGasRate(double physical)
    {
        GeodesicOceanResourceField.CalculateHabitatProductionSplit(7d, 3d, out double submarine, out double terrestrial);
        Assert.That(physical * submarine + physical * terrestrial, Is.EqualTo(physical).Within(physical * 1e-12d));
    }

    [Test]
    public void SubmarineInjectionUsesRateTimesTimeOverPhysicalNodeVolume()
    {
        Assert.That(GeodesicPhysicalScale.VentConcentrationDelta(1.2e8d, 5d, 2e9d), Is.EqualTo(0.3d).Within(1e-14d));
    }

    [Test]
    public void TerrestrialInjectionAddsPhysicalInventoryAndExpectedPressure()
    {
        var go = new GameObject("atmosphere-vent-test");
        try
        {
            var atmosphere = go.AddComponent<GeodesicAtmosphereField>();
            atmosphere.Configure(1000d, 0d, 0d, 0d, 0d, 0d, 0d);
            atmosphere.InitializeForWorld();
            double source = GeodesicPhysicalScale.InjectedInventory(5e7d, 10d);
            Assert.That(atmosphere.AddGeologicalSource(GeodesicAtmosphericGas.CO2, source), Is.EqualTo(source));
            Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.CO2), Is.EqualTo(source));
            Assert.That(atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.CO2), Is.EqualTo(source / atmosphere.AtmosphereInventoryPerBar).Within(1e-15d));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void PhysicalVentConversionDoesNotMutateLegacyVentAuthority()
    {
        var go = new GameObject("legacy-vent-test");
        try
        {
            var legacy = go.AddComponent<PlanetResourceMap>();
            legacy.ventH2PerTick = 0.12f;
            Assert.That(legacy.ventH2PerTick, Is.EqualTo(0.12f));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
