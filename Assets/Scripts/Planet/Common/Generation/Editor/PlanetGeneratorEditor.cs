using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetGenerator))]
public class PlanetGeneratorEditor : Editor
{
    private const double LiveRefreshIntervalSeconds = 0.25d;

    private static readonly HashSet<string> ReadOnlyDiagnostics = new HashSet<string>
    {
        "resolvedGeodesicSeaLevelRadius",
        "resolvedGeodesicSeaLevelOffset",
        "achievedGeodesicOceanCellCoveragePercent",
        "achievedGeodesicOceanAreaCoveragePercent",
        "geodesicOceanCellCount",
        "geodesicCoastlineOceanCellCount",
        "geodesicMinimumLocalOceanDepth",
        "geodesicAreaWeightedMeanLocalOceanDepth",
        "geodesicMaximumLocalOceanDepth"
    };

    private readonly Dictionary<string, string> headerByPropertyName = new Dictionary<string, string>();
    private readonly Dictionary<string, bool> sectionExpanded = new Dictionary<string, bool>();
    private PlanetGenerator generator;
    private GeodesicSurfaceTemperatureField temperatureField;
    private SerializedProperty oceanAppearanceProperty;
    private SerializedProperty seaLevelControlModeProperty;
    private SerializedProperty minimumOceanDepthProperty;
    private SerializedProperty meanOceanDepthProperty;
    private SerializedProperty maximumOceanDepthProperty;
    private bool runtimeDiagnosticsExpanded;
    private bool manualDiagnosticsExpanded;
    private bool liveRuntimeDiagnostics;
    private double nextLiveRefreshTime;
    private RuntimeSnapshot runtimeSnapshot;
    private string rendererInventorySnapshot = "Not captured. Full hierarchy inventory is manual only.";

    private sealed class RuntimeSnapshot
    {
        public string Grid = "Not captured";
        public string SeaLevelRadius = "—";
        public string SeaLevelOffset = "—";
        public string OceanCoverage = "—";
        public string OceanCounts = "—";
        public string OceanDepth = "—";
        public string TemperatureState = "—";
        public string TemperatureRange = "—";
        public string TemperatureTick = "—";
        public string TemperatureProvider = "—";
    }

    private void OnEnable()
    {
        generator = (PlanetGenerator)target;
        temperatureField = generator != null ? generator.GetComponent<GeodesicSurfaceTemperatureField>() : null;
        oceanAppearanceProperty = serializedObject.FindProperty("oceanAppearance");
        seaLevelControlModeProperty = serializedObject.FindProperty("geodesicSeaLevelControlMode");
        minimumOceanDepthProperty = serializedObject.FindProperty("geodesicMinimumLocalOceanDepth");
        meanOceanDepthProperty = serializedObject.FindProperty("geodesicAreaWeightedMeanLocalOceanDepth");
        maximumOceanDepthProperty = serializedObject.FindProperty("geodesicMaximumLocalOceanDepth");
        CacheSectionHeaders();
        runtimeSnapshot = new RuntimeSnapshot();
        RefreshRuntimeSnapshot();
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    // The Inspector never joins Unity's per-frame repaint loop. Optional live diagnostics
    // are explicitly throttled by OnEditorUpdate instead.
    public override bool RequiresConstantRepaint() => false;

    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();

        DrawSettings();
        EditorGUILayout.Space();
        DrawRuntimeDiagnostics();
        DrawManualDiagnostics();
    }

    private void DrawSettings()
    {
        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;
        string currentSection = "References";
        if (!sectionExpanded.ContainsKey(currentSection)) sectionExpanded.Add(currentSection, false);
        bool drawCurrentSection = EditorGUILayout.Foldout(sectionExpanded[currentSection], currentSection, true);
        sectionExpanded[currentSection] = drawCurrentSection;
        bool serializedSettingChanged = false;

        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(property, true);
                continue;
            }

            // Runtime values have their own demand-driven cached section below. Omitting
            // them here prevents changing telemetry from participating in edit handling.
            if (ReadOnlyDiagnostics.Contains(property.propertyPath)) continue;

            if (headerByPropertyName.TryGetValue(property.name, out string header))
            {
                currentSection = header;
                bool expanded = sectionExpanded.TryGetValue(currentSection, out bool value) && value;
                expanded = EditorGUILayout.Foldout(expanded, currentSection, true);
                sectionExpanded[currentSection] = expanded;
                drawCurrentSection = expanded;
            }

            if (!drawCurrentSection) continue;

            if (property.propertyPath == "oceanAppearance")
            {
                DrawSerializedProperty(oceanAppearanceProperty, null, true, ref serializedSettingChanged);
                continue;
            }

            bool inactive = IsInactiveSeaLevelControl(property.propertyPath);
            using (new EditorGUI.DisabledScope(inactive))
            {
                DrawSerializedProperty(property, GetLabel(property), true, ref serializedSettingChanged);
            }

            if (inactive) EditorGUILayout.HelpBox(GetInactiveSeaLevelHelp(property.propertyPath), MessageType.Info);
            if (property.propertyPath == "geodesicSeaLevelControlMode" && CurrentSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld)
            {
                EditorGUILayout.HelpBox("Global ocean mode: coastline and shelf controls are inactive. If Enable Ocean is false, OceanWorld is inactive and no ocean will be generated.", MessageType.Info);
            }
        }

        // Apply only genuine serialized setting edits. Foldouts and telemetry refreshes
        // never dirty the target or invoke OnValidate.
        if (serializedSettingChanged) serializedObject.ApplyModifiedProperties();
    }

    private static void DrawSerializedProperty(SerializedProperty property, GUIContent label, bool includeChildren, ref bool changed)
    {
        EditorGUI.BeginChangeCheck();
        if (label == null) EditorGUILayout.PropertyField(property, includeChildren);
        else EditorGUILayout.PropertyField(property, label, includeChildren);
        if (EditorGUI.EndChangeCheck()) changed = true;
    }

    private void DrawRuntimeDiagnostics()
    {
        runtimeDiagnosticsExpanded = EditorGUILayout.Foldout(runtimeDiagnosticsExpanded, "Runtime Diagnostics (Cached)", true);
        if (!runtimeDiagnosticsExpanded) return;

        bool requestedLive = EditorGUILayout.ToggleLeft("Live Runtime Inspector Diagnostics (4 Hz)", liveRuntimeDiagnostics);
        if (requestedLive != liveRuntimeDiagnostics)
        {
            liveRuntimeDiagnostics = requestedLive;
            nextLiveRefreshTime = 0d;
            if (liveRuntimeDiagnostics) RefreshRuntimeSnapshot();
        }

        if (GUILayout.Button("Refresh Diagnostic Snapshot")) RefreshRuntimeSnapshot();

        EditorGUILayout.LabelField("Grid", runtimeSnapshot.Grid);
        EditorGUILayout.LabelField("Sea level radius", runtimeSnapshot.SeaLevelRadius);
        EditorGUILayout.LabelField("Sea level offset", runtimeSnapshot.SeaLevelOffset);
        EditorGUILayout.LabelField("Ocean coverage", runtimeSnapshot.OceanCoverage);
        EditorGUILayout.LabelField("Ocean cells", runtimeSnapshot.OceanCounts);
        EditorGUILayout.LabelField("Ocean depth min/mean/max", runtimeSnapshot.OceanDepth);
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("Temperature", runtimeSnapshot.TemperatureState);
        EditorGUILayout.LabelField("Temp min/mean/max", runtimeSnapshot.TemperatureRange);
        EditorGUILayout.LabelField("Temp tick", runtimeSnapshot.TemperatureTick);
        EditorGUILayout.LabelField("Sun provider", runtimeSnapshot.TemperatureProvider);
        EditorGUILayout.HelpBox("These are immutable display strings copied from O(1) cached runtime scalars. No cell, edge, temperature-array, or mesh scan occurs here.", MessageType.None);
    }

    private void DrawManualDiagnostics()
    {
        manualDiagnosticsExpanded = EditorGUILayout.Foldout(manualDiagnosticsExpanded, "Manual Expensive Diagnostics", true);
        if (!manualDiagnosticsExpanded) return;

        if (GUILayout.Button("Refresh Renderer Inventory")) RefreshRendererInventory();
        EditorGUILayout.HelpBox(rendererInventorySnapshot, MessageType.None);
        EditorGUILayout.HelpBox("Topology validation and temperature diffusion validation remain explicit context-menu actions on their owning runtime components.", MessageType.Info);
    }

    private void OnEditorUpdate()
    {
        if (!liveRuntimeDiagnostics || !runtimeDiagnosticsExpanded || generator == null) return;
        double now = EditorApplication.timeSinceStartup;
        if (now < nextLiveRefreshTime) return;
        nextLiveRefreshTime = now + LiveRefreshIntervalSeconds;
        RefreshRuntimeSnapshot();
        Repaint();
    }

    private void RefreshRuntimeSnapshot()
    {
        if (generator == null) return;
        if (temperatureField == null) temperatureField = generator.GetComponent<GeodesicSurfaceTemperatureField>();

        // All generator values below are cached scalar fields or O(1) arithmetic getters.
        runtimeSnapshot.Grid = generator.CurrentGridType.ToString();
        runtimeSnapshot.SeaLevelRadius = generator.ResolvedGeodesicSeaLevelRadius.ToString("F6");
        runtimeSnapshot.SeaLevelOffset = generator.ResolvedGeodesicSeaLevelOffset.ToString("F6");
        runtimeSnapshot.OceanCoverage = $"cells {generator.AchievedGeodesicOceanCellCoveragePercent:F3}% / area {generator.AchievedGeodesicOceanAreaCoveragePercent:F3}%";
        runtimeSnapshot.OceanCounts = $"{generator.GeodesicOceanCellCount} ocean / {generator.GeodesicCoastlineOceanCellCount} coastline";
        runtimeSnapshot.OceanDepth = ReadCachedDepthDiagnostics();

        if (temperatureField != null && temperatureField.IsInitialized)
        {
            runtimeSnapshot.TemperatureState = $"Initialized — {temperatureField.CellCount} cells";
            runtimeSnapshot.TemperatureRange = $"{temperatureField.MinimumTemperatureKelvin:F2} / {temperatureField.AreaWeightedMeanTemperatureKelvin:F2} / {temperatureField.MaximumTemperatureKelvin:F2} K";
            runtimeSnapshot.TemperatureTick = $"{temperatureField.LastTickDurationMilliseconds:F3} ms; conservation {temperatureField.LatestDiffusionConservationRelativeError:E3}";
            runtimeSnapshot.TemperatureProvider = temperatureField.CurrentSunDirectionProvider;
        }
        else
        {
            runtimeSnapshot.TemperatureState = "Not initialized";
            runtimeSnapshot.TemperatureRange = runtimeSnapshot.TemperatureTick = runtimeSnapshot.TemperatureProvider = "—";
        }
    }

    private string ReadCachedDepthDiagnostics()
    {
        return minimumOceanDepthProperty == null || meanOceanDepthProperty == null || maximumOceanDepthProperty == null
            ? "—"
            : $"{minimumOceanDepthProperty.floatValue:F6} / {meanOceanDepthProperty.floatValue:F6} / {maximumOceanDepthProperty.floatValue:F6}";
    }

    private void RefreshRendererInventory()
    {
        Renderer[] renderers = generator.GetComponentsInChildren<Renderer>(true);
        var builder = new StringBuilder(256);
        builder.Append("One-time snapshot: ").Append(renderers.Length).Append(" renderer(s)");
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            Material material = renderer.sharedMaterial;
            builder.Append('\n').Append(renderer.name).Append(": ")
                .Append(material != null ? material.name : "<no material>")
                .Append(" / ").Append(material != null && material.shader != null ? material.shader.name : "<no shader>");
        }
        rendererInventorySnapshot = builder.ToString();
    }

    private void CacheSectionHeaders()
    {
        FieldInfo[] fields = typeof(PlanetGenerator).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < fields.Length; i++)
        {
            HeaderAttribute header = fields[i].GetCustomAttribute<HeaderAttribute>();
            if (header == null) continue;
            headerByPropertyName[fields[i].Name] = header.header;
            if (!sectionExpanded.ContainsKey(header.header)) sectionExpanded.Add(header.header, false);
        }
    }

    private GeodesicSeaLevelControlMode CurrentSeaLevelControlMode
    {
        get
        {
            return seaLevelControlModeProperty != null ? (GeodesicSeaLevelControlMode)seaLevelControlModeProperty.enumValueIndex : GeodesicSeaLevelControlMode.ManualOffset;
        }
    }

    private bool IsInactiveSeaLevelControl(string path)
    {
        GeodesicSeaLevelControlMode mode = CurrentSeaLevelControlMode;
        return (mode == GeodesicSeaLevelControlMode.ManualOffset && (path == "geodesicTargetOceanCoveragePercent" || path == "geodesicOceanWorldMinimumDepth"))
            || (mode == GeodesicSeaLevelControlMode.TargetAreaCoverage && (path == "geodesicSeaLevelOffset" || path == "geodesicOceanWorldMinimumDepth"))
            || (mode == GeodesicSeaLevelControlMode.OceanWorld && (path == "geodesicSeaLevelOffset" || path == "geodesicTargetOceanCoveragePercent"));
    }

    private GUIContent GetLabel(SerializedProperty property)
    {
        if (property.propertyPath == "geodesicTargetOceanCoveragePercent" && CurrentSeaLevelControlMode == GeodesicSeaLevelControlMode.ManualOffset)
            return new GUIContent(property.displayName, "Ignored while Manual Offset is authoritative.");
        if (property.propertyPath == "geodesicSeaLevelOffset" && CurrentSeaLevelControlMode != GeodesicSeaLevelControlMode.ManualOffset)
            return new GUIContent(property.displayName, "Active only in Manual Offset mode.");
        if (property.propertyPath == "geodesicOceanWorldMinimumDepth") return new GUIContent("Ocean World Minimum Cover Depth", property.tooltip);
        return new GUIContent(property.displayName, property.tooltip);
    }

    private static string GetInactiveSeaLevelHelp(string path)
    {
        if (path == "geodesicTargetOceanCoveragePercent") return "Target coverage is active only in Target Area Coverage mode.";
        if (path == "geodesicSeaLevelOffset") return "Manual offset is active only in Manual Offset mode.";
        if (path == "geodesicOceanWorldMinimumDepth") return "Minimum cover depth is active only in Ocean World mode.";
        return string.Empty;
    }
}
