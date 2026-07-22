using UnityEngine;

/// <summary>
/// Authoritative visual-only ocean appearance shared by legacy cube-sphere and geodesic ocean renderers.
/// Geometry, resource sampling, and simulation ownership remain grid-specific.
/// </summary>
[System.Serializable]
public struct OceanAppearanceSettings
{
    public Color baseWaterColor;
    public Color shallowTint;
    public Color deepTint;
    [Range(0f, 1f)] public float opacity;
    [Range(0f, 1f)] public float smoothness;
    [Min(0f)] public float fresnelStrength;
    [Min(0.001f)] public float fresnelPower;
    [Min(0f)] public float ambientResponse;
    [Min(0f)] public float colorIntensity;
    public Color dissolvedFe2Tint;
    public Color suspendedFeOxTint;
    public Color suspendedSulfurTint;
    [Min(0f)] public float dissolvedFe2TintStrength;
    [Min(0f)] public float suspendedFeOxTurbidityStrength;
    [Min(0f)] public float suspendedSulfurTintStrength;
    [Min(0f)] public float organicTurbidityStrength;
    [Min(0f)] public float temperatureTintStrength;
    [Min(0f)] public float iceTintStrength;

    public static OceanAppearanceSettings LegacyDefaults => new OceanAppearanceSettings
    {
        baseWaterColor = new Color(0.15966536f, 0.30129746f, 0.49056602f, 0.58f),
        shallowTint = new Color(0.10f, 0.55f, 0.75f, 0.58f),
        deepTint = new Color(0.02f, 0.12f, 0.28f, 0.58f),
        opacity = 0.58f,
        smoothness = 0.876f,
        fresnelStrength = 0.18f,
        fresnelPower = 3f,
        ambientResponse = 1f,
        colorIntensity = 1f,
        dissolvedFe2Tint = new Color(0.18f, 0.38f, 0.52f, 1f),
        suspendedFeOxTint = new Color(0.72f, 0.36f, 0.12f, 1f),
        suspendedSulfurTint = new Color(0.95f, 0.82f, 0.20f, 1f),
        dissolvedFe2TintStrength = 0f,
        suspendedFeOxTurbidityStrength = 0f,
        suspendedSulfurTintStrength = 0f,
        organicTurbidityStrength = 0f,
        temperatureTintStrength = 0f,
        iceTintStrength = 0f
    };
}

public struct OceanAppearanceSample
{
    public float baseDepthFraction;
    public float dissolvedFe2;
    public float suspendedFeOx;
    public float suspendedSulfur;
    public float organicTurbidity;
    public float temperature;
    public float iceFraction;

    public static OceanAppearanceSample Default => new OceanAppearanceSample { baseDepthFraction = 1f };
}

public struct OceanAppearanceEvaluation
{
    public Color baseColor;
    public Color finalColor;
    public float opacity;
    public float turbidity;
    public bool HasActiveChemistryInputs;
}

public static class OceanAppearanceModel
{
    public static OceanAppearanceEvaluation Evaluate(OceanAppearanceSettings settings, OceanAppearanceSample sample)
    {
        float depth = Mathf.Clamp01(sample.baseDepthFraction);
        Color depthColor = Color.Lerp(settings.shallowTint, settings.deepTint, depth);
        Color baseColor = Color.Lerp(depthColor, settings.baseWaterColor, 0.5f);

        float fe2 = Mathf.Max(0f, sample.dissolvedFe2);
        float feOx = Mathf.Max(0f, sample.suspendedFeOx);
        float sulfur = Mathf.Max(0f, sample.suspendedSulfur);
        float organic = Mathf.Max(0f, sample.organicTurbidity);
        float ice = Mathf.Clamp01(sample.iceFraction);
        bool activeChemistry = fe2 > 0f || feOx > 0f || sulfur > 0f || organic > 0f || ice > 0f || !Mathf.Approximately(sample.temperature, 0f);

        Color finalColor = baseColor;
        finalColor = Color.Lerp(finalColor, settings.dissolvedFe2Tint, SaturatingContribution(fe2, settings.dissolvedFe2TintStrength));
        finalColor = Color.Lerp(finalColor, settings.suspendedFeOxTint, SaturatingContribution(feOx, settings.suspendedFeOxTurbidityStrength));
        finalColor = Color.Lerp(finalColor, settings.suspendedSulfurTint, SaturatingContribution(sulfur, settings.suspendedSulfurTintStrength));
        if (ice > 0f && settings.iceTintStrength > 0f)
        {
            finalColor = Color.Lerp(finalColor, Color.white, Mathf.Clamp01(ice * settings.iceTintStrength));
        }

        float intensity = Mathf.Max(0f, settings.colorIntensity);
        finalColor.r *= intensity;
        finalColor.g *= intensity;
        finalColor.b *= intensity;

        float turbidity = SaturatingContribution(feOx, settings.suspendedFeOxTurbidityStrength)
            + SaturatingContribution(organic, settings.organicTurbidityStrength);
        float opacity = Mathf.Clamp01(settings.opacity + Mathf.Clamp01(turbidity) * 0.15f);
        finalColor.a = opacity;
        baseColor.a = opacity;

        return new OceanAppearanceEvaluation
        {
            baseColor = baseColor,
            finalColor = finalColor,
            opacity = opacity,
            turbidity = Mathf.Clamp01(turbidity),
            HasActiveChemistryInputs = activeChemistry
        };
    }

    private static float SaturatingContribution(float value, float strength)
    {
        return Mathf.Clamp01(Mathf.Max(0f, value) * Mathf.Max(0f, strength));
    }
}
