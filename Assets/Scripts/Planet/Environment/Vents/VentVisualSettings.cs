using System;
using UnityEngine;

/// <summary>Saved, visual-only startup settings. No vent chemistry or thermal authority.</summary>
[Serializable]
public struct VentVisualSettings : IEquatable<VentVisualSettings>
{
    public const float MinGlowSize = 0.25f, MaxGlowSize = 2f;
    public const float MaxSmokeWidth = 4f, MaxSmokeHeight = 3f, MaxSmokeIntensity = 3f;
    public const float MaxDistortionStrength = 0.2f;

    public bool enableSmoke, enableHeatDistortion;
    [Range(MinGlowSize, MaxGlowSize)] public float glowSizeMultiplier;
    [Range(0f, MaxSmokeWidth)] public float smokeBaseWidthMultiplier;
    [Range(0f, MaxSmokeHeight)] public float smokeHeightMultiplier;
    [Range(0f, MaxSmokeIntensity)] public float smokeIntensityMultiplier;
    [Range(0f, 1f)] public float smokeTurbulenceStrength;
    [Range(0f, MaxDistortionStrength)] public float heatDistortionStrength;

    public static VentVisualSettings Defaults => new VentVisualSettings
    {
        enableSmoke = true, enableHeatDistortion = true, glowSizeMultiplier = 0.85f,
        smokeBaseWidthMultiplier = 1.75f, smokeHeightMultiplier = 1f,
        smokeIntensityMultiplier = 1f, smokeTurbulenceStrength = 0.25f, heatDistortionStrength = 0.03f
    };

    public VentVisualSettings Normalized()
    {
        var value = this;
        value.glowSizeMultiplier = Clamp(glowSizeMultiplier, MinGlowSize, MaxGlowSize, Defaults.glowSizeMultiplier);
        value.smokeBaseWidthMultiplier = Clamp(smokeBaseWidthMultiplier, 0f, MaxSmokeWidth, Defaults.smokeBaseWidthMultiplier);
        value.smokeHeightMultiplier = Clamp(smokeHeightMultiplier, 0f, MaxSmokeHeight, Defaults.smokeHeightMultiplier);
        value.smokeIntensityMultiplier = Clamp(smokeIntensityMultiplier, 0f, MaxSmokeIntensity, Defaults.smokeIntensityMultiplier);
        value.smokeTurbulenceStrength = Clamp(smokeTurbulenceStrength, 0f, 1f, Defaults.smokeTurbulenceStrength);
        value.heatDistortionStrength = Clamp(heatDistortionStrength, 0f, MaxDistortionStrength, Defaults.heatDistortionStrength);
        return value;
    }

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;

    public bool Equals(VentVisualSettings other) => enableSmoke == other.enableSmoke
        && enableHeatDistortion == other.enableHeatDistortion && glowSizeMultiplier == other.glowSizeMultiplier
        && smokeBaseWidthMultiplier == other.smokeBaseWidthMultiplier && smokeHeightMultiplier == other.smokeHeightMultiplier
        && smokeIntensityMultiplier == other.smokeIntensityMultiplier && smokeTurbulenceStrength == other.smokeTurbulenceStrength
        && heatDistortionStrength == other.heatDistortionStrength;
    public override bool Equals(object obj) => obj is VentVisualSettings other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(enableSmoke, enableHeatDistortion, glowSizeMultiplier,
        smokeBaseWidthMultiplier, smokeHeightMultiplier, smokeIntensityMultiplier, smokeTurbulenceStrength, heatDistortionStrength);
}
