using UnityEngine;

public class SimulationSpeedController : MonoBehaviour
{
    [Header("GUI")]
    [SerializeField] private float guiWidth = 720f;
    [SerializeField] private float guiHeight = 56f;
    [SerializeField] private float topPadding = 10f;

    [Header("Scaling")]
    [SerializeField] private float referenceHeight = 1080f;
    [SerializeField] private float minGuiScale = 1f;
    [SerializeField] private float maxGuiScale = 1.8f;

    [System.Serializable]
    public struct SpeedOption
    {
        public string label;
        public int simulationStepsPerFrame;
    }

    [SerializeField]
    private SpeedOption[] speedOptions =
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

    private GUIStyle titleStyle;
    private GUIStyle valueStyle;
    private GUIStyle boxStyle;
    private GUIStyle sliderStyle;
    private GUIStyle thumbStyle;

    private ReplicatorManager replicatorManager;
    private float guiScale = 1f;

    private void Awake()
    {
        replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        selectedOptionIndex = Mathf.Clamp(selectedOptionIndex, 0, speedOptions.Length - 1);
        ApplySelectedSpeed();
    }

    private void OnGUI()
    {
        UpdateGuiScale();
        EnsureGuiStyles();

        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1f));

        float scaledScreenWidth = Screen.width / guiScale;

        Rect container = new Rect(
            (scaledScreenWidth - guiWidth) * 0.5f,
            topPadding,
            guiWidth,
            guiHeight);

        GUI.Box(container, GUIContent.none, boxStyle);

        float innerPadding = 12f;
        float titleWidth = 135f;
        float valueWidth = 155f;
        float sliderHeight = sliderStyle.fixedHeight;
        float sliderY = container.y + (container.height - sliderHeight) * 0.5f;

        Rect titleRect = new Rect(
            container.x + innerPadding,
            container.y + 2f,
            titleWidth,
            container.height - 4f);

        GUI.Label(titleRect, "Simulation Speed", titleStyle);

        Rect valueRect = new Rect(
            container.x + container.width - valueWidth - innerPadding,
            container.y + 2f,
            valueWidth,
            container.height - 4f);

        SpeedOption active = speedOptions[selectedOptionIndex];
        GUI.Label(
            valueRect,
            $"{active.label} ({active.simulationStepsPerFrame} steps/frame)",
            valueStyle);

        float sliderX = titleRect.xMax + 12f;
        float sliderWidth = valueRect.x - 12f - sliderX;

        Rect sliderRect = new Rect(
            sliderX,
            sliderY,
            sliderWidth,
            sliderHeight);

        float sliderValue = GUI.HorizontalSlider(
            sliderRect,
            selectedOptionIndex,
            0f,
            speedOptions.Length - 1,
            sliderStyle,
            thumbStyle);

        int snappedIndex = Mathf.Clamp(Mathf.RoundToInt(sliderValue), 0, speedOptions.Length - 1);
        if (snappedIndex != selectedOptionIndex)
        {
            selectedOptionIndex = snappedIndex;
            ApplySelectedSpeed();
        }

        GUI.matrix = oldMatrix;
    }

    private void ApplySelectedSpeed()
    {
        if (speedOptions == null || speedOptions.Length == 0)
        {
            return;
        }

        selectedOptionIndex = Mathf.Clamp(selectedOptionIndex, 0, speedOptions.Length - 1);
        SpeedOption active = speedOptions[selectedOptionIndex];

        Time.timeScale = 1f;
        replicatorManager ??= FindFirstObjectByType<ReplicatorManager>();
        replicatorManager?.SetSimulationTiming(active.simulationStepsPerFrame);
    }

    private void UpdateGuiScale()
    {
        float scaleFromHeight = Screen.height / referenceHeight;
        guiScale = Mathf.Clamp(scaleFromHeight, minGuiScale, maxGuiScale);
    }

    private void EnsureGuiStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip
            };
        }

        if (valueStyle == null)
        {
            valueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Normal,
                clipping = TextClipping.Clip
            };
        }

        if (boxStyle == null)
        {
            Texture2D backgroundTexture = new Texture2D(1, 1);
            backgroundTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.42f));
            backgroundTexture.Apply();

            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = backgroundTexture;
            boxStyle.padding = new RectOffset(8, 8, 8, 8);
        }

        if (sliderStyle == null)
        {
            sliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
        }

        if (thumbStyle == null)
        {
            thumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
        }

        titleStyle.fontSize = Mathf.RoundToInt(10f * guiScale);
        valueStyle.fontSize = Mathf.RoundToInt(10f * guiScale);

        sliderStyle.fixedHeight = Mathf.RoundToInt(12f * guiScale);
        thumbStyle.fixedWidth = Mathf.RoundToInt(14f * guiScale);
        thumbStyle.fixedHeight = Mathf.RoundToInt(20f * guiScale);
    }
}