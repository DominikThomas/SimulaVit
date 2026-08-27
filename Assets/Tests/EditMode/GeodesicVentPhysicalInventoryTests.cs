using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicVentPhysicalInventoryTests
{
    private const double OceanVolumeKm3 = 1.22321e11d;

    [TestCase(0.12d, 1.2e8d)]
    [TestCase(0.004d, 4e6d)]
    [TestCase(0.05d, 5e7d)]
    [TestCase(0.002d, 2e6d)]
    public void AuthoringRateConvertsOnceToPhysicalInventoryRate(double authoring, double expectedPhysical)
    {
        double physical = GeodesicPhysicalScale.PhysicalInventoryRate(authoring);
        Assert.That(physical, Is.EqualTo(expectedPhysical).Within(expectedPhysical * 1e-12d));
        Assert.That(physical, Is.LessThan(1e18d), "A runtime physical rate must not receive a second 1e9 conversion.");
    }

    [Test]
    public void SixHundredSecondH2InventoryAndWholeOceanMeanMatchPhysicalScaleSanityCheck()
    {
        double rate = GeodesicPhysicalScale.PhysicalInventoryRate(0.12d);
        double inventory = GeodesicPhysicalScale.InjectedInventory(rate, 600d);
        Assert.That(inventory, Is.EqualTo(7.2e10d));
        Assert.That(inventory / OceanVolumeKm3, Is.EqualTo(0.5886d).Within(5e-5d));
    }

    [Test]
    public void NormalizedOutletDistributionConservesGlobalPhysicalRate()
    {
        double globalRate = GeodesicPhysicalScale.PhysicalInventoryRate(0.12d);
        double[] weights = { 0.1d, 0.25d, 0.65d };
        double distributed = 0d;
        foreach (double weight in weights) distributed += globalRate * weight;
        Assert.That(distributed, Is.EqualTo(globalRate).Within(globalRate * 1e-12d));
    }

    [TestCase(0.05d)] [TestCase(0.12d)] [TestCase(0.004d)]
    public void HabitatSplitConservesGlobalPhysicalGasRate(double authoringRate)
    {
        GeodesicOceanResourceField.CalculateHabitatProductionSplit(7d, 3d, out double submarine, out double terrestrial);
        double physical = GeodesicPhysicalScale.PhysicalInventoryRate(authoringRate);
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
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void PhysicalVentConversionDoesNotMutateLegacyVentAuthority()
    {
        var go = new GameObject("legacy-vent-test");
        try
        {
            var legacy = go.AddComponent<PlanetResourceMap>();
            legacy.ventH2PerTick = 0.12f;
            _ = GeodesicPhysicalScale.PhysicalInventoryRate(legacy.ventH2PerTick);
            Assert.That(legacy.ventH2PerTick, Is.EqualTo(0.12f));
        }
        finally { Object.DestroyImmediate(go); }
    }
}
