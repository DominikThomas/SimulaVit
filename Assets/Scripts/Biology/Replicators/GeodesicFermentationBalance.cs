using System;

/// <summary>Legacy OrganicC -> stored C + CO2 + H2 abstraction, in physical inventory units.
/// Plans all ocean writes before credit; H2 carries no carbon. Unstorable C is fermented.</summary>
public static class GeodesicFermentationBalance
{
    public struct Transfer
    {
        public GeodesicHydrogenotrophyBalance.CommitStatus status;
        public double extent, organicAfter, co2After, h2After, consumed, co2Produced, h2Produced;
        public float storeAfter, energy;
    }

    public static double LimitExtent(double desired, double need) => double.IsFinite(desired)
        && double.IsFinite(need) && desired > 0d && need > 0d ? desired : 0d;

    public static Transfer PlanCommit(double organic, double co2, double h2, double volume,
        double extent, double need, float energyParameter, float assimilation, float store, float maximumStore)
    {
        var t = new Transfer { organicAfter = organic, co2After = co2, h2After = h2, storeAfter = store };
        if (!double.IsFinite(organic) || !double.IsFinite(co2) || !double.IsFinite(h2) || !double.IsFinite(volume)
            || organic < 0d || co2 < 0d || h2 < 0d || volume <= 0d || !float.IsFinite(energyParameter)
            || energyParameter < 0f || !float.IsFinite(assimilation) || !float.IsFinite(store)
            || !float.IsFinite(maximumStore) || store < 0f || maximumStore < 0f)
        { t.status = GeodesicHydrogenotrophyBalance.CommitStatus.Invalid; return t; }
        extent = Math.Min(LimitExtent(extent, need), organic * volume / need);
        if (!(extent > 0d) || !double.IsFinite(extent)) return t;
        double planned = need * extent;
        double afterOrganic = Math.Max(0d, organic - planned / volume);
        double consumed = (organic - afterOrganic) * volume;
        if (!(consumed > 0d) || !Matches(planned, consumed))
        { t.status = GeodesicHydrogenotrophyBalance.CommitStatus.PrecisionRejected; return t; }
        double budget = Math.Min(consumed * Math.Max(0d, Math.Min(1d, assimilation)), Math.Max(0d, (double)maximumStore - store));
        float afterStore = (float)(store + budget);
        if ((double)afterStore - store > budget)
            afterStore = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(afterStore) - 1);
        double stored = (double)afterStore - store;
        double fermented = consumed - stored;
        double afterCo2 = co2 + fermented / volume, afterH2 = h2 + fermented / volume;
        double actualCo2 = (afterCo2 - co2) * volume, actualH2 = (afterH2 - h2) * volume;
        if (stored < 0d || fermented < 0d || !Matches(fermented, actualCo2) || !Matches(fermented, actualH2)
            || (fermented > 0d && (!(actualCo2 > 0d) || !(actualH2 > 0d))))
        { t.status = GeodesicHydrogenotrophyBalance.CommitStatus.PrecisionRejected; return t; }
        float energy = (float)(energyParameter * (fermented / Math.Max(.0001f, need)));
        if (!float.IsFinite(energy)) { t.status = GeodesicHydrogenotrophyBalance.CommitStatus.Invalid; return t; }
        t.status = GeodesicHydrogenotrophyBalance.CommitStatus.Committed;
        t.extent = consumed / need; t.organicAfter = afterOrganic; t.co2After = afterCo2; t.h2After = afterH2;
        t.consumed = consumed; t.co2Produced = actualCo2; t.h2Produced = actualH2; t.storeAfter = afterStore; t.energy = energy;
        return t;
    }

    private static bool Matches(double planned, double actual) => double.IsFinite(actual)
        && Math.Abs(planned - actual) <= GeodesicHydrogenotrophyBalance.RelativeTolerance * Math.Abs(planned);
}
