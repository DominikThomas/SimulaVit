using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetGenerator))]
public class PlanetGeneratorEditor : Editor
{
    private SerializedProperty oceanAppearanceProperty;
    private static readonly string[] ReadOnlyDiagnostics =
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

    private void OnEnable()
    {
        oceanAppearanceProperty = serializedObject.FindProperty("oceanAppearance");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(property, true);
                }
                continue;
            }

            if (property.propertyPath == "oceanAppearance")
            {
                EditorGUILayout.PropertyField(oceanAppearanceProperty, true);
                continue;
            }

            bool isInactiveModeControl = IsInactiveSeaLevelControl(property.propertyPath);
            using (new EditorGUI.DisabledScope(IsReadOnlyDiagnostic(property.propertyPath) || isInactiveModeControl))
            {
                EditorGUILayout.PropertyField(property, GetLabel(property), true);
            }

            if (isInactiveModeControl)
            {
                EditorGUILayout.HelpBox(GetInactiveSeaLevelHelp(property.propertyPath), MessageType.Info);
            }
            if (property.propertyPath == "geodesicSeaLevelControlMode" && CurrentSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld)
            {
                EditorGUILayout.HelpBox("Global ocean mode: coastline and shelf controls are inactive. If Enable Ocean is false, OceanWorld is inactive and no ocean will be generated.", MessageType.Info);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static bool IsReadOnlyDiagnostic(string propertyPath)
    {
        for (int i = 0; i < ReadOnlyDiagnostics.Length; i++)
        {
            if (propertyPath == ReadOnlyDiagnostics[i]) return true;
        }

        return false;
    }

    private GeodesicSeaLevelControlMode CurrentSeaLevelControlMode
    {
        get
        {
            SerializedProperty mode = serializedObject.FindProperty("geodesicSeaLevelControlMode");
            return mode != null ? (GeodesicSeaLevelControlMode)mode.enumValueIndex : GeodesicSeaLevelControlMode.ManualOffset;
        }
    }

    private bool IsInactiveSeaLevelControl(string propertyPath)
    {
        GeodesicSeaLevelControlMode mode = CurrentSeaLevelControlMode;
        return (mode == GeodesicSeaLevelControlMode.ManualOffset && (propertyPath == "geodesicTargetOceanCoveragePercent" || propertyPath == "geodesicOceanWorldMinimumDepth"))
            || (mode == GeodesicSeaLevelControlMode.TargetAreaCoverage && (propertyPath == "geodesicSeaLevelOffset" || propertyPath == "geodesicOceanWorldMinimumDepth"))
            || (mode == GeodesicSeaLevelControlMode.OceanWorld && (propertyPath == "geodesicSeaLevelOffset" || propertyPath == "geodesicTargetOceanCoveragePercent"));
    }

    private GUIContent GetLabel(SerializedProperty property)
    {
        if (property.propertyPath == "geodesicTargetOceanCoveragePercent" && CurrentSeaLevelControlMode == GeodesicSeaLevelControlMode.ManualOffset)
        {
            return new GUIContent(property.displayName, "Ignored while Geodesic Sea Level Control Mode is Manual Offset; manual sea-level offset is authoritative.");
        }

        if (property.propertyPath == "geodesicSeaLevelOffset" && CurrentSeaLevelControlMode != GeodesicSeaLevelControlMode.ManualOffset)
        {
            return new GUIContent(property.displayName, "Inactive unless Geodesic Sea Level Control Mode is Manual Offset; the resolved offset is calculated automatically.");
        }

        if (property.propertyPath == "geodesicOceanWorldMinimumDepth")
        {
            return new GUIContent("Ocean World Minimum Cover Depth", property.tooltip);
        }

        return new GUIContent(property.displayName, property.tooltip);
    }

    private static string GetInactiveSeaLevelHelp(string propertyPath)
    {
        if (propertyPath == "geodesicTargetOceanCoveragePercent")
        {
            return "Target ocean coverage is active only in Target Area Coverage mode. Target Area Coverage 100% remains distinct from Ocean World.";
        }

        if (propertyPath == "geodesicSeaLevelOffset")
        {
            return "Manual sea-level offset is active only in Manual Offset mode. The resolved offset is calculated automatically in the other modes.";
        }

        if (propertyPath == "geodesicOceanWorldMinimumDepth")
        {
            return "Ocean World minimum cover depth is active only in Ocean World mode. Global ocean mode: coastline and shelf controls are inactive.";
        }

        return string.Empty;
    }
}
