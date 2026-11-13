using UnityEngine;

public struct GeodesicBiologyTemperatureSettings
{
    public float DormancyFloor, ReplicationMinEfficiency;
    public bool DirectDamageEnabled;
    public float ExtremeColdKelvin, ExtremeHeatKelvin;
    public float StressMaintenanceMultiplier;
    public float ColdExposureDeathSeconds, HeatExposureDeathSeconds, ExposureRecoveryPerSecond;
    public float MutationChance, MutationScale;

    public static GeodesicBiologyTemperatureSettings Default => new GeodesicBiologyTemperatureSettings
    {
        DormancyFloor = 0.02f, ReplicationMinEfficiency = 0.15f, DirectDamageEnabled = true,
        ExtremeColdKelvin = 233.15f, ExtremeHeatKelvin = 423.15f,
        StressMaintenanceMultiplier = 1f, ColdExposureDeathSeconds = 120f,
        HeatExposureDeathSeconds = 60f, ExposureRecoveryPerSecond = 1f,
        MutationChance = 0.02f, MutationScale = 2f
    };
}

/// <summary>Individual thermal traits govern biology; vent distance is never an input.</summary>
public static class GeodesicBiologyTemperaturePolicy
{
    public enum SuitabilityBand { Suitable, Marginal, Unsuitable }

    public static bool CanReproduce(float performance, in GeodesicBiologyTemperatureSettings settings)
        => float.IsFinite(performance) && performance > 0f && performance >= Mathf.Clamp01(settings.ReplicationMinEfficiency);

    public static SuitabilityBand GetBand(float performance, in GeodesicBiologyTemperatureSettings settings)
    {
        if (!CanReproduce(performance, settings)) return SuitabilityBand.Unsuitable;
        return performance >= Mathf.Max(0.5f, Mathf.Clamp01(settings.ReplicationMinEfficiency))
            ? SuitabilityBand.Suitable : SuitabilityBand.Marginal;
    }

    public static float MaintenanceMultiplier(float performance, in GeodesicBiologyTemperatureSettings settings)
        => 1f + (1f - Mathf.Clamp01(performance)) * Mathf.Max(0f, settings.StressMaintenanceMultiplier);

    public static bool IsDormant(float performance, in GeodesicBiologyTemperatureSettings settings)
        => performance <= Mathf.Max(0.05f, Mathf.Clamp01(settings.DormancyFloor) + 0.01f);

    public static DeathCause AdvanceExposure(float temperature, float optimumMin, float optimumMax, float margin,
        float dt, in GeodesicBiologyTemperatureSettings settings, ref float coldExposure, ref float heatExposure)
    {
        if (!settings.DirectDamageEnabled)
        {
            coldExposure = heatExposure = 0f;
            return DeathCause.Unknown;
        }
        if (!(dt > 0f) || !float.IsFinite(temperature) || !float.IsFinite(optimumMin)
            || !float.IsFinite(optimumMax) || !float.IsFinite(margin) || margin <= 0f || optimumMax < optimumMin)
            return DeathCause.Unknown;

        // Stress begins outside this individual's zero-performance edges. The legacy
        // broad extreme settings anchor full severity, rather than causing instant death.
        float coldEdge = optimumMin - margin;
        float heatEdge = optimumMax + margin;
        float coldExtreme = settings.ExtremeColdKelvin > 0f ? Mathf.Min(coldEdge, settings.ExtremeColdKelvin) : optimumMin - 2f * margin;
        float heatExtreme = settings.ExtremeHeatKelvin > 0f ? Mathf.Max(heatEdge, settings.ExtremeHeatKelvin) : optimumMax + 2f * margin;
        float coldSeverity = Mathf.Clamp01((coldEdge - temperature) / Mathf.Max(0.0001f, coldEdge - coldExtreme));
        float heatSeverity = Mathf.Clamp01((temperature - heatEdge) / Mathf.Max(0.0001f, heatExtreme - heatEdge));
        float recovery = Mathf.Max(0f, settings.ExposureRecoveryPerSecond) * dt;
        coldExposure = settings.ColdExposureDeathSeconds > 0f
            ? (coldSeverity > 0f ? Mathf.Max(0f, coldExposure) + coldSeverity * dt : Mathf.Max(0f, coldExposure - recovery)) : 0f;
        heatExposure = settings.HeatExposureDeathSeconds > 0f
            ? (heatSeverity > 0f ? Mathf.Max(0f, heatExposure) + heatSeverity * dt : Mathf.Max(0f, heatExposure - recovery)) : 0f;
        if (settings.ColdExposureDeathSeconds > 0f && coldExposure >= settings.ColdExposureDeathSeconds) return DeathCause.TemperatureTooLow;
        if (settings.HeatExposureDeathSeconds > 0f && heatExposure >= settings.HeatExposureDeathSeconds) return DeathCause.TemperatureTooHigh;
        return DeathCause.Unknown;
    }

    public static void MutateInheritedTraits(Replicator child, System.Random random, in GeodesicBiologyTemperatureSettings settings)
    {
        // A separate deterministic stream keeps trait mutation out of movement and
        // reproduction's random decisions. Damage/exposure is not inherited.
        float chance = Mathf.Clamp01(settings.MutationChance), scale = Mathf.Abs(settings.MutationScale);
        bool bandMutated = false;
        if (random.NextDouble() < chance) { child.optimalTempMin += SignedMutation(random, scale); bandMutated = true; }
        if (random.NextDouble() < chance) { child.optimalTempMax += SignedMutation(random, scale); bandMutated = true; }
        if (bandMutated)
        {
            float minimum = Mathf.Min(child.optimalTempMin, child.optimalTempMax);
            float maximum = Mathf.Max(child.optimalTempMin, child.optimalTempMax);
            float center = Mathf.Clamp((minimum + maximum) * 0.5f, 151f, 499f);
            float halfWidth = Mathf.Max(1f, (maximum - minimum) * 0.5f);
            child.optimalTempMin = Mathf.Clamp(center - halfWidth, 150f, 498f);
            child.optimalTempMax = Mathf.Clamp(center + halfWidth, child.optimalTempMin + 2f, 500f);
        }
        if (random.NextDouble() < chance) child.lethalTempMargin += SignedMutation(random, scale);
        child.lethalTempMargin = Mathf.Max(1f, child.lethalTempMargin);
    }

    private static float SignedMutation(System.Random random, float scale) => ((float)random.NextDouble() * 2f - 1f) * scale;
}
