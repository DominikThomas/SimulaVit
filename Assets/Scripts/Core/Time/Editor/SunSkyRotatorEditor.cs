using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SunSkyRotator))]
public class SunSkyRotatorEditor : Editor
{
    private static readonly string[] ReadOnlyFields =
    {
        "currentDirectionalLight",
        "planetToSunDirectionWorld",
        "sunToPlanetDirectionWorld",
        "lightDirectionAngularError",
        "terrainMainLightSupport",
        "oceanMainLightSupport",
        "cameraToPlanetDistance",
        "resolvedVisiblePlanetRadius",
        "apparentPlanetAngularRadiusDegrees",
        "apparentSunAngularRadiusDegrees",
        "sunCentreHeightAboveLimbDegrees",
        "sunsetColourFactor",
        "visibleDiscFactor",
        "glowFactor"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script" || IsReadOnly(property.propertyPath)))
            {
                EditorGUILayout.PropertyField(property, true);
            }
        }
        serializedObject.ApplyModifiedProperties();

        SunSkyRotator controller = (SunSkyRotator)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Authoritative Sun Direction", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Sun Direction Provider", controller, typeof(SunSkyRotator), true);
            EditorGUILayout.ObjectField("Physical Directional Light", controller.CurrentDirectionalLight, typeof(Light), true);
            EditorGUILayout.ObjectField("RenderSettings Sun", RenderSettings.sun, typeof(Light), true);
        }

        if (controller.CurrentDirectionalLight == null)
        {
            EditorGUILayout.HelpBox("The visible sun has no synchronized physical Directional Light.", MessageType.Warning);
        }
        else if (controller.LightDirectionAngularError > controller.angularMismatchWarningDegrees)
        {
            EditorGUILayout.HelpBox($"Directional light mismatch: {controller.LightDirectionAngularError:F3} degrees.", MessageType.Warning);
        }
        if (Application.isPlaying && !controller.TerrainMainLightSupport)
        {
            EditorGUILayout.HelpBox("Geodesic terrain main-light shader support was not found.", MessageType.Warning);
        }
        if (Application.isPlaying && !controller.OceanMainLightSupport)
        {
            EditorGUILayout.HelpBox("Geodesic ocean main-light shader support was not found.", MessageType.Warning);
        }
    }

    private static bool IsReadOnly(string path)
    {
        for (int i = 0; i < ReadOnlyFields.Length; i++)
        {
            if (ReadOnlyFields[i] == path) return true;
        }
        return false;
    }
}
