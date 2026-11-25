using System;

/// <summary>Explicit research hypotheses, not production switches or defaults.
/// All signals use only the currently sampled habitat and inherited thermal traits.</summary>
internal static class GeodesicFoodAlternatives
{
    public static double Signal(string alternative, double resourceTicks, float temperature,
        float minimum, float maximum, float margin, double currentReadiness)
    {
        if (alternative != "B-independent" && alternative != "C-smooth") return currentReadiness;
        double distance = Math.Max(0d, Math.Max(minimum - temperature, temperature - maximum));
        double thermal = Math.Exp(-distance / Math.Max(1d, margin));
        double food = resourceTicks > 0d ? resourceTicks / (1d + resourceTicks) : 0d;
        // B retains thermal information even without food, with bounded food weight.
        // C smooths the thermal gate but can still lose all signal when food is zero.
        return alternative == "B-independent" ? thermal * (.5d + .5d * food) : thermal * food;
    }
}
