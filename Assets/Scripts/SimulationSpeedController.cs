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
        public float timeScale;
        public int simulationStepsPerFrame;
    }

    [SerializeField] private SpeedOption[] speedOptions =
    {
        new SpeedOption { label = "0x", timeScale = 0f, simulationStepsPerFrame = 0 },
        new SpeedOption { label = "1x", timeScale = 1f, simulationStepsPerFrame = 1 },
        new SpeedOption { label = "2x", timeScale = 2f, simulationStepsPerFrame = 2 },
        new SpeedOption { label = "5x", timeScale = 5f, simulationStepsPerFrame = 5 },
        new SpeedOption { label = "10x", timeScale = 10f, simulationStepsPerFrame = 10 },
        new SpeedOption { label = "20x", timeScale = 20f, simulationStepsPerFrame = 20 },
        new SpeedOption { label = "50x", timeScale = 50f, simulationStepsPerFrame = 25 },
        new SpeedOption { label = "100x", timeScale = 100f, simulationStepsPerFrame = 50 }
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

        Time.timeScale = active.timeScale;
        replicatorManager ??= FindFirstObjectByType<ReplicatorManager>();
        replicatorManager?.SetSimulationTiming(active.timeScale, active.simulationStepsPerFrame);
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
