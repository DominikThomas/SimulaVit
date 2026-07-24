using UnityEditor;

[CustomEditor(typeof(PlanetGenerator))]
public class PlanetGeneratorEditor : Editor
{
    private SerializedProperty oceanAppearanceProperty;
    private static readonly string[] ReadOnlyDiagnostics =
    {
        "resolvedGeodesicSeaLevelRadius",
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

            using (new EditorGUI.DisabledScope(IsReadOnlyDiagnostic(property.propertyPath)))
            {
                EditorGUILayout.PropertyField(property, true);
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
}
