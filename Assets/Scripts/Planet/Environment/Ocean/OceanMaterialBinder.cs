using UnityEngine;

public static class OceanMaterialBinder
{
    public const string BaseColor = "_BaseColor";
    public const string ShallowColor = "_ShallowColor";
    public const string DeepColor = "_DeepColor";
    public const string Opacity = "_Opacity";
    public const string Smoothness = "_Smoothness";
    public const string FresnelStrength = "_FresnelStrength";
    public const string FresnelPower = "_FresnelPower";
    public const string AmbientResponse = "_AmbientResponse";
    public const string ColorIntensity = "_ColorIntensity";
    public const string Fe2Tint = "_Fe2Tint";
    public const string FeOxTint = "_FeOxTint";
    public const string SulfurTint = "_SulfurTint";
    public const string Turbidity = "_Turbidity";

    public static void Apply(Material material, OceanAppearanceSettings settings, OceanAppearanceEvaluation evaluation)
    {
        if (material == null) return;
        SetColorIfPresent(material, BaseColor, evaluation.finalColor);
        SetColorIfPresent(material, "_Color", evaluation.finalColor);
        SetColorIfPresent(material, ShallowColor, WithAlpha(settings.shallowWaterColor, evaluation.opacity));
        SetColorIfPresent(material, DeepColor, WithAlpha(settings.deepWaterColor, evaluation.opacity));
        SetFloatIfPresent(material, Opacity, evaluation.opacity);
        SetFloatIfPresent(material, Smoothness, Mathf.Clamp01(settings.smoothness));
        SetFloatIfPresent(material, FresnelStrength, Mathf.Max(0f, settings.fresnelStrength));
        SetFloatIfPresent(material, FresnelPower, Mathf.Max(0.001f, settings.fresnelPower));
        SetFloatIfPresent(material, AmbientResponse, Mathf.Max(0f, settings.ambientResponse));
        SetFloatIfPresent(material, ColorIntensity, Mathf.Max(0f, settings.colorIntensity));
        SetColorIfPresent(material, Fe2Tint, settings.dissolvedFe2Tint);
        SetColorIfPresent(material, FeOxTint, settings.suspendedFeOxTint);
        SetColorIfPresent(material, SulfurTint, settings.sulfurTint);
        SetFloatIfPresent(material, Turbidity, evaluation.turbidity);
    }

    private static void SetColorIfPresent(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName)) material.SetColor(propertyName, value);
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName)) material.SetFloat(propertyName, value);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
