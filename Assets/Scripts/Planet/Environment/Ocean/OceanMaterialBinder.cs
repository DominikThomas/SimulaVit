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

    public static readonly string[] FinalBaseColorProperties = { BaseColor, "_Color", "_WaterColor", "_BaseWaterColor", "_MainWaterColor", "_Tint" };
    public static readonly string[] ShallowColorProperties = { ShallowColor, "_ShallowWaterColor", "_ShallowTint" };
    public static readonly string[] DeepColorProperties = { DeepColor, "_DeepWaterColor", "_DeepTint" };
    public static readonly string[] OpacityProperties = { Opacity, "_Alpha", "_Transparency" };
    public static readonly string[] SmoothnessProperties = { Smoothness, "_Glossiness" };
    public static readonly string[] FresnelStrengthProperties = { FresnelStrength, "_Fresnel", "_FresnelIntensity" };
    public static readonly string[] FresnelPowerProperties = { FresnelPower };

    public static void Apply(Material material, OceanAppearanceSettings settings, OceanAppearanceEvaluation evaluation)
    {
        if (material == null) return;
        SetColorsIfPresent(material, FinalBaseColorProperties, evaluation.finalColor);
        SetColorsIfPresent(material, ShallowColorProperties, WithAlpha(settings.shallowWaterColor, evaluation.opacity));
        SetColorsIfPresent(material, DeepColorProperties, WithAlpha(settings.deepWaterColor, evaluation.opacity));
        SetFloatsIfPresent(material, OpacityProperties, evaluation.opacity);
        SetFloatsIfPresent(material, SmoothnessProperties, Mathf.Clamp01(settings.smoothness));
        SetFloatsIfPresent(material, FresnelStrengthProperties, Mathf.Max(0f, settings.fresnelStrength));
        SetFloatsIfPresent(material, FresnelPowerProperties, Mathf.Max(0.001f, settings.fresnelPower));
        SetFloatIfPresent(material, AmbientResponse, Mathf.Max(0f, settings.ambientResponse));
        SetFloatIfPresent(material, ColorIntensity, Mathf.Max(0f, settings.colorIntensity));
        SetColorIfPresent(material, Fe2Tint, settings.dissolvedFe2Tint);
        SetColorIfPresent(material, FeOxTint, settings.suspendedFeOxTint);
        SetColorIfPresent(material, SulfurTint, settings.sulfurTint);
        SetFloatIfPresent(material, Turbidity, evaluation.turbidity);
    }

    public static int ApplyFinalBaseColor(Material material, Color color)
    {
        if (material == null) return 0;
        return SetColorsIfPresent(material, FinalBaseColorProperties, color);
    }

    public static string DescribeColorWrites(Material material, Color color)
    {
        if (material == null) return "<no material>";
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        foreach (string propertyName in FinalBaseColorProperties)
        {
            if (material.HasProperty(propertyName))
            {
                if (builder.Length > 0) builder.Append(", ");
                builder.Append(propertyName).Append("=").Append(color);
            }
        }
        return builder.Length > 0 ? builder.ToString() : "<none>";
    }

    public static string DescribeMissingExpectedProperties(Material material)
    {
        if (material == null) return "<no material>";
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        AppendMissing(material, builder, FinalBaseColorProperties);
        AppendMissing(material, builder, ShallowColorProperties);
        AppendMissing(material, builder, DeepColorProperties);
        AppendMissing(material, builder, OpacityProperties);
        AppendMissing(material, builder, SmoothnessProperties);
        AppendMissing(material, builder, FresnelStrengthProperties);
        AppendMissing(material, builder, FresnelPowerProperties);
        return builder.Length > 0 ? builder.ToString() : "<none>";
    }

    private static void AppendMissing(Material material, System.Text.StringBuilder builder, string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (!material.HasProperty(propertyName))
            {
                if (builder.Length > 0) builder.Append(", ");
                builder.Append(propertyName);
            }
        }
    }

    private static int SetColorsIfPresent(Material material, string[] propertyNames, Color value)
    {
        int count = 0;
        foreach (string propertyName in propertyNames)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
                count++;
            }
        }
        return count;
    }

    private static void SetFloatsIfPresent(Material material, string[] propertyNames, float value)
    {
        foreach (string propertyName in propertyNames)
        {
            if (material.HasProperty(propertyName)) material.SetFloat(propertyName, value);
        }
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
