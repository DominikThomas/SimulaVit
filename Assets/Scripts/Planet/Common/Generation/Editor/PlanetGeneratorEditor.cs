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
        "geodesicCoastlineOceanCellCount"
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
        return (mode == GeodesicSeaLevelControlMode.ManualOffset && propertyPath == "geodesicTargetOceanCoveragePercent")
            || (mode == GeodesicSeaLevelControlMode.TargetAreaCoverage && propertyPath == "geodesicSeaLevelOffset");
    }

    private GUIContent GetLabel(SerializedProperty property)
    {
        if (property.propertyPath == "geodesicTargetOceanCoveragePercent" && CurrentSeaLevelControlMode == GeodesicSeaLevelControlMode.ManualOffset)
        {
            return new GUIContent(property.displayName, "Ignored while Geodesic Sea Level Control Mode is Manual Offset; manual sea-level offset is authoritative.");
        }

        if (property.propertyPath == "geodesicSeaLevelOffset" && CurrentSeaLevelControlMode == GeodesicSeaLevelControlMode.TargetAreaCoverage)
        {
            return new GUIContent(property.displayName, "Inactive while Geodesic Sea Level Control Mode is Target Area Coverage; the resolved offset is calculated automatically.");
        }

        return new GUIContent(property.displayName, property.tooltip);
    }

    private static string GetInactiveSeaLevelHelp(string propertyPath)
    {
        if (propertyPath == "geodesicTargetOceanCoveragePercent")
        {
            return "Target ocean coverage is ignored in Manual Offset mode. Change Geodesic Sea Level Control Mode to Target Area Coverage to use this slider.";
        }

        if (propertyPath == "geodesicSeaLevelOffset")
        {
            return "Manual sea-level offset is inactive in Target Area Coverage mode. The resolved offset is calculated automatically from the requested area coverage.";
        }

        return string.Empty;
    }
}
