using System;
using NUnit.Framework;

public sealed class GeodesicHydrogenotrophyBalanceTests
{
    private static GeodesicHydrogenotrophyBalance.Transfer Commit(double co2 = 1d, double h2 = 1d,
        double volume = 1d, double extent = .8d, float store = 0f, float cap = 10f) =>
        GeodesicHydrogenotrophyBalance.PlanCommit(co2, h2, volume, extent, .01f, .02f, 8f, store, cap);

    private static void AssertBalance(GeodesicHydrogenotrophyBalance.Transfer transfer, float before = 0f)
    {
        Assert.That(transfer.status, Is.EqualTo(GeodesicHydrogenotrophyBalance.CommitStatus.Committed));
        double stored = (double)transfer.storeAfter - before;
        Assert.That(transfer.co2Withdrawn - stored, Is.EqualTo(0d).Within(1e-7));
        Assert.That(transfer.h2Withdrawn, Is.EqualTo(2d * transfer.co2Withdrawn).Within(1e-7));
        Assert.That(transfer.energy, Is.EqualTo(800d * stored).Within(2e-5));
    }

    [Test]
    public void IsolatedReactionConservesEveryCarbonUnitWithApprovedActivityScaling()
    {
        var transfer = Commit();
        AssertBalance(transfer);
        Assert.That(transfer.co2Withdrawn, Is.EqualTo(.008d).Within(1e-8));
        Assert.That(transfer.h2Withdrawn, Is.EqualTo(.016d).Within(1e-8));
        Assert.That(transfer.energy, Is.EqualTo(6.4f).Within(1e-6));
    }

    [Test]
    public void HydrogenLimitedReactionCannotSpendMoreThanAvailable()
    {
        var transfer = Commit(h2: .004d);
        AssertBalance(transfer);
        Assert.That(transfer.h2Withdrawn, Is.LessThanOrEqualTo(.004d));
        Assert.That(transfer.storeAfter, Is.EqualTo(.002f).Within(1e-8));
        Assert.That(transfer.energy, Is.EqualTo(1.6f).Within(1e-6));
    }

    [Test]
    public void CarbonDioxideLimitedReactionCannotSpendMoreThanAvailable()
    {
        var transfer = Commit(co2: .003d);
        AssertBalance(transfer);
        Assert.That(transfer.co2Withdrawn, Is.LessThanOrEqualTo(.003d));
        Assert.That(transfer.energy, Is.EqualTo(2.4f).Within(1e-6));
    }

    [Test]
    public void NearlyFullStoreChargesOnlyCarbonThatActuallyFits()
    {
        const float before = 9.999f;
        var transfer = Commit(store: before);
        AssertBalance(transfer, before);
        Assert.That(transfer.storeAfter, Is.EqualTo(10f));
        Assert.That(transfer.co2Withdrawn, Is.EqualTo((double)10f - before).Within(1e-12));
    }

    [Test]
    public void FullOrLoweredCapDoesNotDiscardExistingCarbonOrAwardEnergy()
    {
        foreach (float cap in new[] { 10f, 9f })
        {
            var transfer = Commit(store: 10f, cap: cap);
            Assert.That(transfer.status, Is.EqualTo(GeodesicHydrogenotrophyBalance.CommitStatus.NoExtent));
            Assert.That(transfer.storeAfter, Is.EqualTo(10f));
            Assert.That(transfer.co2After, Is.EqualTo(1d));
            Assert.That(transfer.h2After, Is.EqualTo(1d));
            Assert.That(transfer.energy, Is.Zero);
        }
    }

    [Test]
    public void ZeroActualRequiredSubstrateCannotProduceEnergyOrCarbon()
    {
        foreach (var transfer in new[] { Commit(co2: 0d), Commit(h2: 0d), Commit(extent: 0d) })
        {
            Assert.That(transfer.extent, Is.Zero);
            Assert.That(transfer.co2Withdrawn, Is.Zero);
            Assert.That(transfer.h2Withdrawn, Is.Zero);
            Assert.That(transfer.energy, Is.Zero);
            Assert.That(transfer.storeAfter, Is.Zero);
        }
    }

    [Test]
    public void InvalidInputsCannotAwardEnergy()
    {
        foreach (var transfer in new[] { Commit(co2: double.NaN), Commit(h2: -1d), Commit(volume: 0d) })
        {
            Assert.That(transfer.status, Is.EqualTo(GeodesicHydrogenotrophyBalance.CommitStatus.Invalid));
            Assert.That(transfer.energy, Is.Zero);
        }
    }

    [Test]
    public void BulkOceanWithdrawalThatRoundedToFloatZeroIsNowBackedByBothInputs()
    {
        Assert.That((float)(1d - .008d / 1e8d), Is.EqualTo(1f), "Pre-fix float concentration cannot represent this withdrawal.");
        var transfer = Commit(volume: 1e8d);
        AssertBalance(transfer);
        Assert.That(transfer.co2After, Is.LessThan(1d));
        Assert.That(transfer.h2After, Is.LessThan(1d));
        Assert.That(transfer.co2Withdrawn, Is.GreaterThan(0d));
        Assert.That(transfer.h2Withdrawn, Is.GreaterThan(0d));
    }

    [Test]
    public void UnrepresentableDoubleWithdrawalRejectsBothWritesAndAllCredits()
    {
        var transfer = Commit(co2: 1e30d, volume: 1e8d);
        Assert.That(transfer.status, Is.EqualTo(GeodesicHydrogenotrophyBalance.CommitStatus.PrecisionRejected));
        Assert.That(transfer.co2After, Is.EqualTo(1e30d));
        Assert.That(transfer.h2After, Is.EqualTo(1d));
        Assert.That(transfer.storeAfter, Is.Zero);
        Assert.That(transfer.energy, Is.Zero);
    }

    [Test]
    public void RepeatedSmallTransfersConserveAcrossCommittedAndRejectedTransactions()
    {
        const double volume = 10000d;
        double co2 = 1d, h2 = 1d, withdrawn = 0d;
        float store = 0f;
        int committed = 0, rejected = 0;
        for (int i = 0; i < 10000; i++)
        {
            var transfer = Commit(co2, h2, volume, .00001d * (1d + .1d * (i % 7)), store);
            if (transfer.status == GeodesicHydrogenotrophyBalance.CommitStatus.PrecisionRejected)
            {
                rejected++;
                Assert.That(transfer.co2After, Is.EqualTo(co2));
                Assert.That(transfer.h2After, Is.EqualTo(h2));
                Assert.That(transfer.storeAfter, Is.EqualTo(store));
                Assert.That(transfer.energy, Is.Zero);
                continue;
            }
            committed++;
            AssertBalance(transfer, store);
            co2 = transfer.co2After; h2 = transfer.h2After;
            store = transfer.storeAfter; withdrawn += transfer.co2Withdrawn;
        }
        double residual = (1d - co2) * volume - store;
        Assert.That(Math.Abs(residual), Is.LessThan(1e-7 + 2e-6 * withdrawn));
        Assert.That(withdrawn, Is.EqualTo((1d - co2) * volume).Within(1e-10));
        Assert.That(committed, Is.GreaterThan(1000));
        Assert.That(rejected, Is.GreaterThan(0), "Sub-precision attempted transfers are observable rejections, not free credits.");
        Assert.That(store, Is.GreaterThan(.0001f));
    }

    [Test]
    public void StoreRoundingCannotCreditUnwithdrawnCarbon()
    {
        const float before = .2f;
        var transfer = Commit(extent: .0012345d, store: before);
        AssertBalance(transfer, before);
        Assert.That((double)transfer.storeAfter - before, Is.LessThanOrEqualTo(.01f * .0012345d));
    }

    [Test]
    public void PositiveButPoorlyResolvedDebitCannotUseTheAbsoluteDiagnosticFloor()
    {
        var transfer = Commit(volume: 1e8d, extent: .000001d);
        Assert.That(transfer.status, Is.EqualTo(GeodesicHydrogenotrophyBalance.CommitStatus.PrecisionRejected));
        Assert.That(transfer.co2After, Is.EqualTo(1d));
        Assert.That(transfer.h2After, Is.EqualTo(1d));
        Assert.That(transfer.energy, Is.Zero);
    }

    [Test]
    public void OverflowingEnergyRejectsTheTransactionBeforeAnyWrite()
    {
        var transfer = GeodesicHydrogenotrophyBalance.PlanCommit(100d, 100d, 1d, 100d,
            .01f, .02f, float.MaxValue, 0f, 10f);
        Assert.That(transfer.status, Is.EqualTo(GeodesicHydrogenotrophyBalance.CommitStatus.Invalid));
        Assert.That(transfer.co2After, Is.EqualTo(100d));
        Assert.That(transfer.h2After, Is.EqualTo(100d));
        Assert.That(transfer.energy, Is.Zero);
    }

    [Test]
    public void ZeroActivityAndCapacityHaveNoDemandBeforeCompetition()
    {
        Assert.That(GeodesicHydrogenotrophyBalance.LimitExtent(0d, .01, .02, 0f, 10f), Is.Zero);
        Assert.That(GeodesicHydrogenotrophyBalance.LimitExtent(.8d, .01, .02, 10f, 10f), Is.Zero);
        Assert.That(GeodesicHydrogenotrophyBalance.LimitExtent(.8d, .01, 0d, 0f, 10f), Is.Zero);
        Assert.That(GeodesicHydrogenotrophyBalance.LimitExtent(.8d, 0d, .02, 0f, 10f), Is.Zero);
    }
}
