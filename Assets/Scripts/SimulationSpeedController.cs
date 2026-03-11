using UnityEngine;

public class SimulationSpeedController : MonoBehaviour
{
    [Header("GUI")]
    [SerializeField] private float guiWidth = 420f;
    [SerializeField] private float guiHeight = 52f;
    [SerializeField] private float topPadding = 10f;

    [System.Serializable]
    public struct SpeedOption
    {
        public string label;
        public int simulationStepsPerFrame;
    }

    [SerializeField] private SpeedOption[] speedOptions =
    {
        new SpeedOption { label = "0x", simulationStepsPerFrame = 0 },
        new SpeedOption { label = "1x", simulationStepsPerFrame = 1 },
        new SpeedOption { label = "2x", simulationStepsPerFrame = 2 },
        new SpeedOption { label = "5x", simulationStepsPerFrame = 5 },
        new SpeedOption { label = "10x", simulationStepsPerFrame = 10 },
        new SpeedOption { label = "20x", simulationStepsPerFrame = 20 },
        new SpeedOption { label = "50x", simulationStepsPerFrame = 25 },
        new SpeedOption { label = "100x", simulationStepsPerFrame = 50 }
    };

    [SerializeField] private int selectedOptionIndex = 1;

    private GUIStyle labelStyle;
    private ReplicatorManager replicatorManager;

    private void Awake()
    {
        replicatorManager = FindFirstObjectByType<ReplicatorManager>();

        selectedOptionIndex = Mathf.Clamp(selectedOptionIndex, 0, speedOptions.Length - 1);
        ApplySelectedSpeed();
    }

    private void OnGUI()
    {
        EnsureGuiStyles();

        Rect container = new Rect(
            (Screen.width - guiWidth) * 0.5f,
            topPadding,
            guiWidth,
            guiHeight);

        GUILayout.BeginArea(container, GUI.skin.box);
        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Simulation Speed", GUILayout.Width(140));

        float sliderValue = GUILayout.HorizontalSlider(selectedOptionIndex, 0f, speedOptions.Length - 1, GUILayout.Width(200));
        int snappedIndex = Mathf.Clamp(Mathf.RoundToInt(sliderValue), 0, speedOptions.Length - 1);

        if (snappedIndex != selectedOptionIndex)
        {
            selectedOptionIndex = snappedIndex;
            ApplySelectedSpeed();
        }

        SpeedOption active = speedOptions[selectedOptionIndex];
        GUILayout.Label($"{active.label} ({active.simulationStepsPerFrame} steps/frame)", labelStyle, GUILayout.Width(180));
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private void ApplySelectedSpeed()
    {
        if (speedOptions == null || speedOptions.Length == 0)
        {
            return;
        }

        SpeedOption active = speedOptions[selectedOptionIndex];

        Time.timeScale = 1f;
        replicatorManager ??= FindFirstObjectByType<ReplicatorManager>();
        replicatorManager?.SetSimulationTiming(active.simulationStepsPerFrame);
    }

    private void EnsureGuiStyles()
    {
        if (labelStyle != null)
        {
            return;
        }

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold
        };
    }
}
