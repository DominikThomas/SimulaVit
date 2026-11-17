using System;

/// <summary>
/// Carbon and energy in simulation inventory units (concentration * km3), not moles.
/// Geodesic activity scales the whole CO2 + H2 -> stored-carbon reaction. Every
/// consumed carbon unit is stored; there is no implicit carbon product or biomass pool.
/// </summary>
public static class GeodesicHydrogenotrophyBalance
{
    // Shared with diagnostics; these are the existing bounds, not relaxed tolerances.
    public const double AbsoluteTolerance = 1e-7;
    public const double RelativeTolerance = 2e-6;

    public enum CommitStatus { NoExtent, Committed, Invalid, PrecisionRejected }

    public struct Transfer
    {
        public CommitStatus status;
        public double extent, co2After, h2After, co2Withdrawn, h2Withdrawn;
        public float storeAfter, energy;
    }

    public static double LimitExtent(double desired, double co2PerTick, double h2PerTick,
        float store, float maximumStore)
    {
        if (!Finite(desired) || !Finite(co2PerTick) || !Finite(h2PerTick)
            || !Finite(store) || !Finite(maximumStore) || desired <= 0d
            || co2PerTick <= 0d || h2PerTick <= 0d || store < 0f) return 0d;
        return Math.Min(desired, Math.Max(0d, (double)maximumStore - store) / co2PerTick);
    }

    /// <summary>
    /// Plans both writes atomically. No caller state is changed on rejection. Charge
    /// the actual representable organism store increment, so float store rounding
    /// cannot create or discard carbon. Sub-float ocean transfers remain double.
    /// </summary>
    public static Transfer PlanCommit(double co2Concentration, double h2Concentration,
        double volumeKm3, double competedExtent, double co2PerTick, double h2PerTick,
        float energyPerTick, float storeBefore, float maximumStore)
    {
        var result = new Transfer { co2After = co2Concentration, h2After = h2Concentration, storeAfter = storeBefore };
        if (!Finite(co2Concentration) || !Finite(h2Concentration) || !Finite(volumeKm3)
            || !Finite(energyPerTick) || energyPerTick < 0f || volumeKm3 <= 0d
            || co2Concentration < 0d || h2Concentration < 0d)
        { result.status = CommitStatus.Invalid; return result; }

        double extent = LimitExtent(competedExtent, co2PerTick, h2PerTick, storeBefore, maximumStore);
        if (!(extent > 0d)) return result;
        extent = Math.Min(extent, co2Concentration * volumeKm3 / co2PerTick);
        extent = Math.Min(extent, h2Concentration * volumeKm3 / h2PerTick);
        if (!(extent > 0d)) return result;
        double carbonBudget = co2PerTick * extent;
        float afterStore = (float)Math.Min(maximumStore, storeBefore + carbonBudget);
        // Round downward when nearest-float rounding would credit more than the budget.
        if ((double)afterStore - storeBefore > carbonBudget)
            afterStore = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(afterStore) - 1);
        double carbon = (double)afterStore - storeBefore;
        if (!(carbon > 0d)) { result.status = CommitStatus.PrecisionRejected; return result; }
        extent = carbon / co2PerTick;
        double hydrogen = h2PerTick * extent;
        double afterCo2 = Math.Max(0d, co2Concentration - carbon / volumeKm3);
        double afterH2 = Math.Max(0d, h2Concentration - hydrogen / volumeKm3);
        double actualCo2 = (co2Concentration - afterCo2) * volumeKm3;
        double actualH2 = (h2Concentration - afterH2) * volumeKm3;
        if (!(actualCo2 > 0d) || !(actualH2 > 0d)
            || !TransferMatches(carbon, actualCo2) || !TransferMatches(hydrogen, actualH2))
        { result.status = CommitStatus.PrecisionRejected; return result; }
        float energy = (float)(energyPerTick * extent);
        if (!Finite(energy)) { result.status = CommitStatus.Invalid; return result; }

        result.status = CommitStatus.Committed;
        result.extent = extent;
        result.co2After = afterCo2; result.h2After = afterH2;
        result.co2Withdrawn = actualCo2; result.h2Withdrawn = actualH2;
        result.storeAfter = afterStore;
        result.energy = energy;
        return result;
    }

    private static bool TransferMatches(double planned, double actual) =>
        // The transaction guard is stricter than reporting's absolute floor: tiny
        // reactions must not use that floor as permission for a large relative debit error.
        Finite(actual) && Math.Abs(actual - planned) <= RelativeTolerance * Math.Abs(planned);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
