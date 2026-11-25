using System;

/// <summary>Existing local temporal comparison/feedback decision, isolated for deterministic tests.</summary>
public static class GeodesicTaxisPolicy
{
    public enum Trend { Neutral, Improving, Worsening }
    public struct Comparison { public double rawDelta; public float normalizedDelta; public Trend trend; }

    public static Comparison Compare(double current, double remembered, double floor, float improvement, float worsening)
    {
        current = double.IsFinite(current) && current >= 0d ? current : 0d;
        remembered = double.IsFinite(remembered) && remembered >= 0d ? remembered : 0d;
        var result = new Comparison { rawDelta = current - remembered };
        double denominator = Math.Max(floor, Math.Max(current, remembered));
        result.normalizedDelta = (float)Math.Max(-1d, Math.Min(1d, result.rawDelta / denominator));
        if (result.normalizedDelta > 0f && result.normalizedDelta >= improvement) result.trend = Trend.Improving;
        else if (result.normalizedDelta < 0f && result.normalizedDelta <= -worsening) result.trend = Trend.Worsening;
        return result;
    }

    public static float ReferenceTumbleProbability(bool feedback, bool memory, Trend trend,
        float initial, float better, float worse, float neutral)
    {
        float probability = !memory ? initial : trend == Trend.Improving ? better : trend == Trend.Worsening ? worse : neutral;
        if (!feedback && memory) probability = neutral;
        return probability;
    }
}
