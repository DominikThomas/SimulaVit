using UnityEngine;
using UnityEngine.Serialization;

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
        [FormerlySerializedAs("simulationStepsPerFrame")]
        public int requestedMultiplier;
    }

    [SerializeField]
    private SpeedOption[] speedOptions =
    {
        new SpeedOption { label = "0x", requestedMultiplier = 0 },
        new SpeedOption { label = "1x", requestedMultiplier = 1 },
        new SpeedOption { label = "2x", requestedMultiplier = 2 },
        new SpeedOption { label = "5x", requestedMultiplier = 5 },
        new SpeedOption { label = "10x", requestedMultiplier = 10 },
        new SpeedOption { label = "20x", requestedMultiplier = 20 },
        new SpeedOption { label = "50x", requestedMultiplier = 50 },
        new SpeedOption { label = "100x", requestedMultiplier = 100 }
    };

    [SerializeField] private int selectedOptionIndex = 1;

    private GUIStyle titleStyle;
    private GUIStyle valueStyle;
    private GUIStyle boxStyle;
    private GUIStyle sliderStyle;
    private GUIStyle thumbStyle;

    private ReplicatorManager replicatorManager;
    private float guiScale = 1f;
    [Header("Throughput Diagnostics")]
    [SerializeField, Min(0.5f)] private float throughputMeasurementWindowSeconds = 0.75f;
    [SerializeField] private float achievedSimulationSpeed;
    [SerializeField] private string displayedSpeedText = "1x";
    private double measurementStartSimulationTime;
    private float measurementStartRealTime;
    private bool hasThroughputMeasurement;

    private void Awake()
    {
        replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        selectedOptionIndex = Mathf.Clamp(selectedOptionIndex, 0, speedOptions.Length - 1);
        ApplySelectedSpeed();
        ResetThroughputMeasurement();
    }

    private void Update()
    {
        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
            ResetThroughputMeasurement();
            return;
        }

        float now = Time.unscaledTime;
        float elapsed = now - measurementStartRealTime;
        if (elapsed < Mathf.Max(0.5f, throughputMeasurementWindowSeconds))
        {
            return;
        }

        achievedSimulationSpeed = (float)((replicatorManager.SimulationTimeSeconds - measurementStartSimulationTime) / elapsed);
        hasThroughputMeasurement = true;
        measurementStartRealTime = now;
        measurementStartSimulationTime = replicatorManager.SimulationTimeSeconds;
        RefreshDisplayedSpeedText();
    }

    private void OnGUI()
    {
        if (SimulationStartupController.IsStartupBlockingHud)
        {
            return;
        }

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

        GUI.Label(valueRect, displayedSpeedText, valueStyle);

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

    public void RefreshFromSimulationTiming()
    {
        replicatorManager ??= FindFirstObjectByType<ReplicatorManager>();
        int authoritativeStepsPerFrame = replicatorManager != null ? replicatorManager.SimulationStepsPerFrame : 1;
        selectedOptionIndex = FindClosestSpeedOptionIndex(authoritativeStepsPerFrame);
        ResetThroughputMeasurement();
        RefreshDisplayedSpeedText();
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
        replicatorManager?.SetSimulationTiming(active.requestedMultiplier);
        ResetThroughputMeasurement();
        RefreshDisplayedSpeedText();
    }

    private int FindClosestSpeedOptionIndex(int simulationStepsPerFrame)
    {
        if (speedOptions == null || speedOptions.Length == 0)
        {
            return 0;
        }

        int closestIndex = 0;
        int smallestDelta = Mathf.Abs(speedOptions[0].requestedMultiplier - simulationStepsPerFrame);
        for (int i = 1; i < speedOptions.Length; i++)
        {
            int delta = Mathf.Abs(speedOptions[i].requestedMultiplier - simulationStepsPerFrame);
            if (delta < smallestDelta)
            {
                closestIndex = i;
                smallestDelta = delta;
            }
        }

        return closestIndex;
    }

    private void ResetThroughputMeasurement()
    {
        measurementStartRealTime = Time.unscaledTime;
        measurementStartSimulationTime = replicatorManager != null ? replicatorManager.SimulationTimeSeconds : 0d;
        achievedSimulationSpeed = 0f;
        hasThroughputMeasurement = false;
    }

    private void RefreshDisplayedSpeedText()
    {
        if (speedOptions == null || speedOptions.Length == 0)
        {
            displayedSpeedText = "--";
            return;
        }

        SpeedOption active = speedOptions[Mathf.Clamp(selectedOptionIndex, 0, speedOptions.Length - 1)];
        displayedSpeedText = hasThroughputMeasurement
            ? $"{active.label} (actual ~{achievedSimulationSpeed:F1}x)"
            : active.label;
    }

#if UNITY_EDITOR
    [ContextMenu("Validate Simulation Speed Semantics")]
    private void ValidateSimulationSpeedSemantics()
    {
        const double tolerance = 1e-5;
        float[] frames = { 0.02f, 0.02f, 0.02f, 0.02f, 0.02f, 0.02f, 0.02f, 0.02f, 0.02f, 0.02f };
        bool valid = true;
        foreach (SpeedOption option in speedOptions)
        {
            double actual = Integrate(frames, option.requestedMultiplier);
            double expected = 0.2d * option.requestedMultiplier;
            bool optionValid = System.Math.Abs(actual - expected) <= tolerance;
            valid &= optionValid;
            Debug.Log($"[SimulationSpeedValidation] {option.label}: expected={expected:F6}s actual={actual:F6}s {(optionValid ? "PASS" : "FAIL")}", this);
        }

        valid &= System.Math.Abs(Integrate(frames, 0)) <= tolerance;
        valid &= System.Math.Abs(Integrate(new[] { 0.02f, 0.02f }, 10) - Integrate(new[] { 0.01f, 0.01f, 0.01f, 0.01f }, 10)) <= tolerance;
        double transitions = Integrate(new[] { 0.02f }, 10) + Integrate(new[] { 0.02f }, 100)
            + Integrate(new[] { 0.02f }, 100) + Integrate(new[] { 0.02f }, 1);
        valid &= System.Math.Abs(transitions - 4.22d) <= tolerance;
        Debug.Log($"[SimulationSpeedValidation] pause, frame partition, 10x->100x, and 100x->1x: {(valid ? "PASS" : "FAIL")}", this);
    }

    private static double Integrate(float[] frameDeltas, int requestedMultiplier)
    {
        double total = 0d;
        foreach (float frameDelta in frameDeltas)
        {
            total += ReplicatorSimulationPipeline.CalculateAuthoritativeFrameAdvance(frameDelta, requestedMultiplier, 1f / 30f);
        }
        return total;
    }
#endif

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
