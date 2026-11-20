using UnityEngine;

public partial class SimulationStartupController
{
    private bool showVentVisualEffects;

    // Used at startup/load, before regeneration, and by the GUI when a value changes.
    public void ApplyVentVisualSettings()
    {
        if (currentConfig == null) return;
        currentConfig.ventVisuals = currentConfig.ventVisuals.Normalized();
        if (ventVisualizer == null && planetGenerator != null)
        {
            ventVisualizer = planetGenerator.GetComponentInChildren<VentVisualizer>(true);
            if (ventVisualizer == null) ventVisualizer = planetGenerator.gameObject.AddComponent<VentVisualizer>();
        }
        ventVisualizer?.ApplyStartupVisualSettings(currentConfig);
    }

    private void DrawVentVisualEffects()
    {
        GUILayout.Space(10f);
        showVentVisualEffects = GUILayout.Toggle(showVentVisualEffects,
            (showVentVisualEffects ? "▼ " : "▶ ") + "Vent Visual Effects", buttonStyle, GUILayout.Height(32f));
        if (!showVentVisualEffects) return;
        bool previousChanged = GUI.changed;
        GUI.changed = false;
        var settings = currentConfig.ventVisuals;
        SetupBool("Enable vent smoke", ref settings.enableSmoke);
        SetupBool("Enable heat distortion", ref settings.enableHeatDistortion);
        SetupFloat("Vent glow size multiplier", ref settings.glowSizeMultiplier,
            VentVisualSettings.MinGlowSize, VentVisualSettings.MaxGlowSize, true);
        SetupFloat("Smoke base width multiplier", ref settings.smokeBaseWidthMultiplier, 0f, VentVisualSettings.MaxSmokeWidth, true);
        SetupFloat("Smoke height multiplier", ref settings.smokeHeightMultiplier, 0f, VentVisualSettings.MaxSmokeHeight, true);
        SetupFloat("Smoke emission intensity multiplier", ref settings.smokeIntensityMultiplier, 0f, VentVisualSettings.MaxSmokeIntensity, true);
        SetupFloat("Smoke turbulence strength", ref settings.smokeTurbulenceStrength, 0f, 1f, true);
        SetupFloat("Heat distortion strength", ref settings.heatDistortionStrength, 0f, VentVisualSettings.MaxDistortionStrength, true);
        if (!settings.Equals(currentConfig.ventVisuals))
        {
            currentConfig.ventVisuals = settings;
            ApplyVentVisualSettings();
        }
        GUI.changed |= previousChanged;
        SetupHelp("Smoke stays developed while offscreen. Higher emission intensity increases CPU cost for every vent. Width and height remain limited by the available water depth.");
    }

    private partial class SavedStartupConfig
    {
        public VentVisualSettings ventVisuals;

        private void CaptureV25(SimulationStartupConfig config) => ventVisuals = config.ventVisuals.Normalized();

        // FromDefaults seeds this value before JSON overwrite. Missing fields in old files
        // inherit the configured scene defaults; explicit false/zero values remain meaningful.
        private void RestoreV25(SimulationStartupConfig config) => config.ventVisuals = ventVisuals.Normalized();
    }
}
