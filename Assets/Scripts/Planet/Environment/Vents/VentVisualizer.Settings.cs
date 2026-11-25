using UnityEngine;

public partial class VentVisualizer
{
    public VentVisualSettings CaptureVisualSettings() => new VentVisualSettings
    {
        enableSmoke = enableSmoke, enableHeatDistortion = enableHeatDistortion,
        glowSizeMultiplier = ventGlowSizeMultiplier, smokeBaseWidthMultiplier = smokeWidthMultiplier,
        smokeHeightMultiplier = smokeHeightMultiplier, smokeIntensityMultiplier = smokeIntensityMultiplier,
        smokeTurbulenceStrength = smokeTurbulenceStrength, heatDistortionStrength = distortionStrength
    };

    public void ApplyStartupVisualSettings(SimulationStartupConfig config)
    {
        if (config == null) return;
        boundStartupConfig = config;
        var previous = CaptureVisualSettings();
        var settings = config.ventVisuals.Normalized();
        config.ventVisuals = settings;
        AssignVisualSettings(settings);
        if (previous.Equals(settings)) return;
        // A glow/toggle/refraction edit does not rebuild or restart a running smoke plume.
        bool particlesChanged = previous.smokeBaseWidthMultiplier != settings.smokeBaseWidthMultiplier
            || previous.smokeHeightMultiplier != settings.smokeHeightMultiplier
            || previous.smokeIntensityMultiplier != settings.smokeIntensityMultiplier
            || previous.smokeTurbulenceStrength != settings.smokeTurbulenceStrength;
        RefreshVisualConfiguration(particlesChanged);
    }

    private void AssignVisualSettings(VentVisualSettings settings)
    {
        enableSmoke = settings.enableSmoke;
        enableHeatDistortion = settings.enableHeatDistortion;
        ventGlowSizeMultiplier = settings.glowSizeMultiplier;
        smokeWidthMultiplier = settings.smokeBaseWidthMultiplier;
        smokeHeightMultiplier = settings.smokeHeightMultiplier;
        smokeIntensityMultiplier = settings.smokeIntensityMultiplier;
        smokeTurbulenceStrength = settings.smokeTurbulenceStrength;
        distortionStrength = settings.heatDistortionStrength;
    }

    public void RegisterVentGlow(Transform marker, float unscaledDiameter)
    {
        glowVisuals.Add((marker, unscaledDiameter));
        marker.localScale = Vector3.one * (unscaledDiameter * ventGlowSizeMultiplier);
    }

    public void RefreshVisualConfiguration() => RefreshVisualConfiguration(true);

    private void RefreshVisualConfiguration(bool reconfigureParticles)
    {
        if (boundStartupConfig != null)
        {
            boundStartupConfig.ventVisuals = CaptureVisualSettings().Normalized();
            AssignVisualSettings(boundStartupConfig.ventVisuals);
        }
        for (int i = glowVisuals.Count - 1; i >= 0; i--)
        {
            var glow = glowVisuals[i];
            if (glow.marker == null) { glowVisuals.RemoveAt(i); continue; }
            glow.marker.localScale = Vector3.one * (glow.baseSize * ventGlowSizeMultiplier);
        }
        for (int i = 0; i < ventEffects.Count; i++)
        {
            if (reconfigureParticles) ConfigureEffect(ventEffects[i]);
            else ApplySmokeSimulation(ventEffects[i]);
        }
        if (sharedHeatMaterial != null)
        {
            sharedHeatMaterial.SetFloat("_DistortionStrength", distortionStrength);
            sharedHeatMaterial.SetFloat("_DistortionStrengthScaled", Mathf.Clamp(distortionStrength, 0f, 0.2f) * 0.1f);
        }
        appearanceDirty = false;
        nextVisibilityUpdate = 0f;
        renderingDormant = false; // Refresh render state once, even when both effects were just disabled.
    }
}
