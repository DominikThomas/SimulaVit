using System;
using System.Reflection;
using NUnit.Framework;

public sealed class GeodesicHydrogenotrophyDiagnosticsTests
{
    private static void Record(GeodesicBiologyDiagnostics ledger, double co2, double h2,
        double co2Ratio, double h2Ratio, float stored, float energy)
        => ledger.RecordReaction(MetabolismType.Hydrogenotrophy, GeodesicOceanResource.CO2,
            GeodesicOceanResource.H2, GeodesicOceanResource.CO2,
            co2, h2, co2, h2, 0d, co2Ratio, h2Ratio, 1d, stored, 0f, stored, energy, energy, true);

    private static T Read<T>(GeodesicBiologyDiagnostics ledger, string name) =>
        (T)typeof(GeodesicBiologyDiagnostics).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ledger);

    private static GeodesicBiologyDiagnostics.ReactionAccounting Totals(GeodesicBiologyDiagnostics ledger) =>
        Read<GeodesicBiologyDiagnostics.ReactionAccounting[]>(ledger, "reactions")[(int)MetabolismType.Hydrogenotrophy];

    [Test]
    public void OriginalEightyPercentFormulaIsReportedAsModelLossNotTransferError()
    {
        var ledger = new GeodesicBiologyDiagnostics();
        Record(ledger, .01d, .02d, 1d, 1d, .008f, 8f);
        var r = Totals(ledger);
        Assert.That(r.modelCarbonResidual, Is.EqualTo(.002d).Within(1e-8));
        Assert.That(r.modelViolations, Is.EqualTo(1));
        Assert.That(r.measuredViolations, Is.EqualTo(1));
        Assert.That(r.inputTransferViolations, Is.Zero);
        Assert.That(r.zeroMeasuredInputWithEnergy, Is.Zero);
    }

    [Test]
    public void RoundedZeroInputWithEnergyIsReportedSeparatelyFromTheModelLoss()
    {
        var ledger = new GeodesicBiologyDiagnostics();
        Record(ledger, .01d, .02d, 0d, 1d, .008f, 8f);
        var r = Totals(ledger);
        Assert.That(r.modelCarbonResidual, Is.EqualTo(.002d).Within(1e-8));
        Assert.That(r.measuredCarbonResidual, Is.EqualTo(-.008d).Within(1e-8));
        Assert.That(r.inputTransferViolations, Is.EqualTo(1));
        Assert.That(r.zeroMeasuredInputWithEnergy, Is.EqualTo(1));
    }

    [Test]
    public void MixedNetOnlyAttributionDoesNotInventAZeroGrossWithdrawal()
    {
        var ledger = new GeodesicBiologyDiagnostics();
        Record(ledger, .01d, .02d, double.NaN, 1d, .008f, 8f);
        var r = Totals(ledger);
        Assert.That(r.unattributedCarbonReactions, Is.EqualTo(1));
        Assert.That(r.netOnlyInputs.co2, Is.EqualTo(.01d));
        Assert.That(r.zeroMeasuredInputWithEnergy, Is.Zero);
        Assert.That(r.inputTransferViolations, Is.Zero);
    }

    [Test]
    public void OneBadNodeTransferCanFlagManyRequestsWithoutDuplicatingNodeInventory()
    {
        var ledger = new GeodesicBiologyDiagnostics();
        ledger.RecordNodeExchange(GeodesicOceanResource.CO2, -.01d, 0d, false);
        for (int i = 0; i < 16; i++) Record(ledger, .01d / 16, .02d / 16, 0d, 1d, .0005f, .5f);
        Assert.That(Read<long>(ledger, "transferViolations"), Is.EqualTo(1));
        Assert.That(Totals(ledger).inputTransferViolations, Is.EqualTo(16));
        Assert.That(Totals(ledger).zeroMeasuredInputWithEnergy, Is.EqualTo(16));
        Assert.That(Read<GeodesicBiologyDiagnostics.ResourceAmounts>(ledger, "requestedNet").co2, Is.EqualTo(-.01d));
    }

    [Test]
    public void CorrectedDirectTransactionHasNoMassOrTransferViolationAndIsCountedOnce()
    {
        const double volume = 1e8d;
        var transfer = GeodesicHydrogenotrophyBalance.PlanCommit(1d, 1d, volume, .8d,
            .01f, .02f, 8f, 0f, 10f);
        var ledger = new GeodesicBiologyDiagnostics();
        double plannedCo2 = .01f * transfer.extent, plannedH2 = .02f * transfer.extent;
        ledger.RecordNodeExchange(GeodesicOceanResource.CO2, -plannedCo2, -transfer.co2Withdrawn, false);
        ledger.RecordNodeExchange(GeodesicOceanResource.H2, -plannedH2, -transfer.h2Withdrawn, false);
        Record(ledger, plannedCo2, plannedH2, transfer.co2Withdrawn / plannedCo2,
            transfer.h2Withdrawn / plannedH2, transfer.storeAfter, transfer.energy);
        var r = Totals(ledger);
        Assert.That(r.modelViolations, Is.Zero);
        Assert.That(r.measuredViolations, Is.Zero);
        Assert.That(r.inputTransferViolations, Is.Zero);
        Assert.That(r.zeroMeasuredInputWithEnergy, Is.Zero);
        Assert.That(Read<long>(ledger, "transferViolations"), Is.Zero);
        Assert.That(Read<GeodesicBiologyDiagnostics.ResourceAmounts>(ledger, "measuredNet").co2,
            Is.EqualTo(-transfer.co2Withdrawn), "Reaction attribution must not count the environmental debit twice.");
        Assert.That(Math.Abs(r.measuredCarbonResidual), Is.LessThan(1e-7));
    }
}
