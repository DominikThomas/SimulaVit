using System;
using NUnit.Framework;
using UnityEngine;

public class GeodesicAtmosphereTests
{
    private GameObject owner;
    private GeodesicAtmosphereField atmosphere;

    [SetUp] public void SetUp() { owner = new GameObject("atmosphere-test"); atmosphere = owner.AddComponent<GeodesicAtmosphereField>(); }
    [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(owner); }

    [Test]
    public void AuthoringInventoryPerBarConvertsToPhysicalInventoryUnits()
    {
        Assert.That(GeodesicAtmosphereField.ToPhysicalInventoryPerBar(1000d), Is.EqualTo(1e12d));
        atmosphere.Configure(1000d, 0d, 0.05d, 0d, 0d, 0d, 0d); atmosphere.InitializeForWorld();
        Assert.That(atmosphere.AtmosphereInventoryPerBarAuthoring, Is.EqualTo(1000d));
        Assert.That(atmosphere.AtmosphereInventoryPerBar, Is.EqualTo(1e12d));
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.CO2), Is.EqualTo(5e10d));
        Assert.That(atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.CO2), Is.EqualTo(0.05d).Within(1e-14d));
    }

    [Test]
    public void PhysicalExchangeInventoryProducesMillibarNotMillionBarChange()
    {
        double pressureDelta = 1.46869486e9d / GeodesicAtmosphereField.ToPhysicalInventoryPerBar(1000d);
        Assert.That(pressureDelta, Is.EqualTo(0.00146869486d).Within(1e-15d));
        Assert.That(pressureDelta, Is.LessThan(1d));
    }

    [Test]
    public void CoupledReservoirTransferConservesAndClampsEitherDonor()
    {
        double ocean = 100d, air = 40d, total = ocean + air;
        Assert.That(GeodesicAirSeaGasExchange.ApplyReservoirTransfer(ref ocean, ref air, 60d), Is.EqualTo(40d));
        Assert.That(air, Is.Zero); Assert.That(ocean + air, Is.EqualTo(total));
        Assert.That(GeodesicAirSeaGasExchange.ApplyReservoirTransfer(ref ocean, ref air, -200d), Is.EqualTo(-140d));
        Assert.That(ocean, Is.Zero); Assert.That(air, Is.EqualTo(total));
    }

    [Test]
    public void RepeatedPhysicalExchangeApproachesEquilibriumWithoutOvershoot()
    {
        const double volumeKm3 = 1e9d, inventoryPerBar = 1e12d, halfLife = 300d, dt = 5d;
        double ocean = volumeKm3, air = 0.05d * inventoryPerBar, total = ocean + air;
        double previousDifference = double.PositiveInfinity, maximumPressure = 0d;
        for (int tick = 0; tick < 5000; tick++)
        {
            double pressure = air / inventoryPerBar, concentration = ocean / volumeKm3;
            double difference = Math.Abs(concentration - pressure);
            double requested = (pressure - concentration) * GeodesicAirSeaGasExchange.ExchangeFraction(dt, halfLife) * volumeKm3;
            GeodesicAirSeaGasExchange.ApplyReservoirTransfer(ref ocean, ref air, requested);
            maximumPressure = Math.Max(maximumPressure, air / inventoryPerBar);
            Assert.That(double.IsFinite(ocean) && double.IsFinite(air), Is.True);
            Assert.That(ocean, Is.GreaterThanOrEqualTo(0d)); Assert.That(air, Is.GreaterThanOrEqualTo(0d));
            Assert.That(ocean + air, Is.EqualTo(total).Within(total * 1e-12d));
            Assert.That(difference, Is.LessThanOrEqualTo(previousDifference + 1e-12d));
            previousDifference = difference;
        }
        Assert.That(maximumPressure, Is.LessThan(1d));
        Assert.That(previousDifference, Is.LessThan(1e-10d));
    }

    [Test]
    public void IndependentInitializationAndPressureSum()
    {
        atmosphere.Configure(10d, 0.7d, 0.1d, 0.2d, 0.01d, 0.02d, 0.03d);
        atmosphere.InitializeForWorld();
        double sum = 0d;
        foreach (GeodesicAtmosphericGas gas in System.Enum.GetValues(typeof(GeodesicAtmosphericGas))) sum += atmosphere.GetPartialPressureBar(gas);
        Assert.That(atmosphere.TotalPressureBar, Is.EqualTo(sum).Within(1e-12));
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.N2), Is.EqualTo(7d * GeodesicPhysicalScale.PhysicalCubicKilometresPerUnityUnitCubed).Within(1e-3));
    }

    [Test]
    public void N2ContributesToPressureAndCannotBeSelectedForExchange()
    {
        atmosphere.Configure(2d, 1d, 0d, 0d, 0d, 0d, 0d); atmosphere.InitializeForWorld();
        owner.AddComponent<GeodesicAirSeaGasExchange>().SetParameters(GeodesicAtmosphericGas.N2, 99d, 1d);
        Assert.That(atmosphere.TotalPressureBar, Is.EqualTo(1d));
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.N2), Is.EqualTo(2d * GeodesicPhysicalScale.PhysicalCubicKilometresPerUnityUnitCubed));
    }

    [Test]
    public void CommonHalfLifeReportsConfiguredRelaxationTimescaleAndZeroDisabled()
    {
        var exchange = owner.AddComponent<GeodesicAirSeaGasExchange>();
        Assert.That(exchange.EffectiveCommonHalfLifeSeconds, Is.Zero);
        exchange.SetCommonHalfLife(300d);
        Assert.That(exchange.EffectiveCommonHalfLifeSeconds, Is.EqualTo(300d));
        exchange.SetCommonHalfLife(0d);
        Assert.That(exchange.EffectiveCommonHalfLifeSeconds, Is.Zero);
    }

    [Test]
    public void AtmosphericLimitAndConservationLedgerAreExact()
    {
        atmosphere.Configure(1d, 0d, 0d, 0.25d, 0d, 0d, 0d); atmosphere.InitializeForWorld();
        double before = atmosphere.GetInventory(GeodesicAtmosphericGas.O2);
        double oceanTransfer = atmosphere.CommitExchange(GeodesicAtmosphericGas.O2, 1e12d);
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.O2), Is.Zero);
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.O2) + oceanTransfer, Is.EqualTo(before));
    }

    [TestCase(0d, 10d, 0d)]
    [TestCase(10d, 0d, 0d)]
    [TestCase(10d, 10d, 0.5d)]
    public void ExchangeFractionHandlesPauseDisableAndFiniteIntegration(double dt, double halfLife, double expected)
        => Assert.That(GeodesicAirSeaGasExchange.ExchangeFraction(dt, halfLife), Is.EqualTo(expected).Within(1e-12));

    [Test]
    public void ExponentialExchangeIsPartitionStable()
    {
        double one = 1d; for (int i = 0; i < 10; i++) one *= 1d - GeodesicAirSeaGasExchange.ExchangeFraction(1d, 20d);
        double two = 1d; for (int i = 0; i < 5; i++) two *= 1d - GeodesicAirSeaGasExchange.ExchangeFraction(2d, 20d);
        double five = 1d; for (int i = 0; i < 2; i++) five *= 1d - GeodesicAirSeaGasExchange.ExchangeFraction(5d, 20d);
        double ten = 1d - GeodesicAirSeaGasExchange.ExchangeFraction(10d, 20d);
        Assert.That(one, Is.EqualTo(two).Within(1e-12)); Assert.That(one, Is.EqualTo(five).Within(1e-12)); Assert.That(one, Is.EqualTo(ten).Within(1e-12));
    }

    [Test]
    public void CleanupAndRegenerationResetAllRuntimeState()
    {
        atmosphere.Configure(1d, 0d, 0d, 1d, 0d, 0d, 0d); atmosphere.InitializeForWorld(); atmosphere.CommitExchange(GeodesicAtmosphericGas.O2, 0.5d * GeodesicPhysicalScale.PhysicalCubicKilometresPerUnityUnitCubed); atmosphere.CompleteExchangeTick();
        atmosphere.ClearField(); Assert.That(atmosphere.IsInitialized, Is.False); Assert.That(atmosphere.TotalPressureBar, Is.Zero); Assert.That(atmosphere.CompletedExchangeTicks, Is.Zero);
        atmosphere.InitializeForWorld(); Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.O2), Is.EqualTo(GeodesicPhysicalScale.PhysicalCubicKilometresPerUnityUnitCubed)); Assert.That(atmosphere.GetCumulativeNetTransferToOcean(GeodesicAtmosphericGas.O2), Is.Zero);
    }

    [Test]
    public void TerrestrialGeologicalSourceAcceptsOnlyVentGasesAndResets()
    {
        atmosphere.Configure(10d, 0d, 0d, 0d, 0d, 0d, 0d); atmosphere.InitializeForWorld();
        Assert.That(atmosphere.AddGeologicalSource(GeodesicAtmosphericGas.CO2, 2d), Is.EqualTo(2d));
        Assert.That(atmosphere.AddGeologicalSource(GeodesicAtmosphericGas.H2, 3d), Is.EqualTo(3d));
        Assert.That(atmosphere.AddGeologicalSource(GeodesicAtmosphericGas.H2S, 4d), Is.EqualTo(4d));
        Assert.That(atmosphere.AddGeologicalSource(GeodesicAtmosphericGas.O2, 5d), Is.Zero);
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.CO2), Is.EqualTo(2d));
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.H2), Is.EqualTo(3d));
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.H2S), Is.EqualTo(4d));
        atmosphere.ClearField(); atmosphere.InitializeForWorld();
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.CO2), Is.Zero);
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.H2), Is.Zero);
        Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.H2S), Is.Zero);
    }

    [TestCase(5d, 0d, 1d, 0d)]
    [TestCase(0d, 7d, 0d, 1d)]
    [TestCase(3d, 1d, 0.75d, 0.25d)]
    public void VentGasBudgetSplitUsesCrossHabitatRawStrength(double submarineWeight, double terrestrialWeight, double expectedSubmarine, double expectedTerrestrial)
    {
        GeodesicOceanResourceField.CalculateHabitatProductionSplit(submarineWeight, terrestrialWeight, out double submarine, out double terrestrial);
        Assert.That(submarine, Is.EqualTo(expectedSubmarine).Within(1e-12));
        Assert.That(terrestrial, Is.EqualTo(expectedTerrestrial).Within(1e-12));
        Assert.That(submarine + terrestrial, Is.EqualTo(1d).Within(1e-12));
    }

    [Test]
    public void ExtremeValidConfigurationRemainsFiniteAndNonNegative()
    {
        atmosphere.Configure(double.MaxValue / 1e100, 1e50, 0d, 0d, 0d, 0d, 0d); atmosphere.InitializeForWorld();
        Assert.That(double.IsFinite(atmosphere.TotalPressureBar), Is.True); Assert.That(atmosphere.GetInventory(GeodesicAtmosphericGas.N2), Is.GreaterThanOrEqualTo(0d));
    }

    [Test]
    public void LargerInventoryPerBarSlowsPressureChangeWithoutChangingGeologicalInventory()
    {
        atmosphere.Configure(100d, 0d, 0d, 0d, 0d, 0d, 0d); atmosphere.InitializeForWorld();
        double oldAdded = atmosphere.AddGeologicalSource(GeodesicAtmosphericGas.CO2, 10d);
        double oldPressure = atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.CO2);
        atmosphere.ClearField();
        atmosphere.Configure(1000d, 0d, 0d, 0d, 0d, 0d, 0d); atmosphere.InitializeForWorld();
        double newAdded = atmosphere.AddGeologicalSource(GeodesicAtmosphericGas.CO2, 10d);
        double newPressure = atmosphere.GetPartialPressureBar(GeodesicAtmosphericGas.CO2);
        Assert.That(newAdded, Is.EqualTo(oldAdded));
        Assert.That(newPressure, Is.EqualTo(oldPressure / 10d).Within(1e-12));
    }

    [Test]
    public void InventoryScaleDoesNotChangeExactExchangeInventoryConservation()
    {
        atmosphere.Configure(100d, 0d, 1d, 0d, 0d, 0d, 0d); atmosphere.InitializeForWorld();
        double oldBefore = atmosphere.GetInventory(GeodesicAtmosphericGas.CO2);
        double oldTransfer = atmosphere.CommitExchange(GeodesicAtmosphericGas.CO2, 25d);
        double oldAfter = atmosphere.GetInventory(GeodesicAtmosphericGas.CO2);
        atmosphere.ClearField();
        atmosphere.Configure(1000d, 0d, 0.1d, 0d, 0d, 0d, 0d); atmosphere.InitializeForWorld();
        double newBefore = atmosphere.GetInventory(GeodesicAtmosphericGas.CO2);
        double newTransfer = atmosphere.CommitExchange(GeodesicAtmosphericGas.CO2, 25d);
        double newAfter = atmosphere.GetInventory(GeodesicAtmosphericGas.CO2);
        Assert.That(oldBefore, Is.EqualTo(newBefore));
        Assert.That(oldTransfer, Is.EqualTo(newTransfer));
        Assert.That(oldAfter + oldTransfer, Is.EqualTo(oldBefore));
        Assert.That(newAfter + newTransfer, Is.EqualTo(newBefore));
    }
}
