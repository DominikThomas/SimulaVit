using System;

/// <summary>Current inventory-based food signal in full reference-tick units.
/// This is pre-competition opportunity, not an energy award or a reaction.</summary>
public static class GeodesicFoodSignal
{
    public static double HydrogenotrophyTicks(double co2, double h2, double volume, double needCo2, double needH2)
    {
        // Both positive requirements are enforced by the authoritative hydrogen
        // transaction. A disabled/invalid reaction must not advertise usable food.
        if (!(volume > 0d) || !double.IsFinite(volume) || !(needCo2 > 0d) || !(needH2 > 0d)
            || !double.IsFinite(needCo2) || !double.IsFinite(needH2)) return 0d;
        return Math.Min(co2 * volume / needCo2, h2 * volume / needH2);
    }

    public static double Readiness(double ticks, float thermalPerformance, float oxygenEfficiency)
    {
        double value = ticks * thermalPerformance * oxygenEfficiency;
        return double.IsFinite(value) && value >= 0d ? value : 0d;
    }

    public static double SulfurChemosynthesisTicks(double co2, double h2s, double volume, double needCo2, double needH2s)
        => HydrogenotrophyTicks(co2, h2s, volume, needCo2, needH2s);
}
