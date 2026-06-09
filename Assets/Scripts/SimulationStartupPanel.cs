using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SimulationStartupPanel : MonoBehaviour
{
    [SerializeField] private SimulationStartupController controller;

    [Header("Inputs")]
    [SerializeField] private Toggle useRandomSeedToggle;
    [SerializeField] private TMP_InputField seedInput;
    [SerializeField] private Slider axisTiltSlider;
    [SerializeField] private TMP_InputField dayLengthInput;
    [SerializeField] private TMP_InputField yearLengthInput;
    [SerializeField] private TMP_InputField baseTempInput;
    [SerializeField] private TMP_InputField insolationGainInput;
    [SerializeField] private TMP_InputField initialCO2Input;
    [SerializeField] private TMP_InputField initialO2Input;
    [SerializeField] private TMP_InputField initialCH4Input;
    [SerializeField] private TMP_InputField initialFe2Input;
    [SerializeField] private TMP_InputField ventH2Input;
    [SerializeField] private TMP_InputField ventH2SInput;
    [SerializeField] private TMP_InputField ventCO2Input;
    [SerializeField] private TMP_InputField initialSpawnCountInput;

    [Header("Labels")]
    [SerializeField] private TMP_Text axisTiltValueLabel;

    [Header("Buttons")]
    [SerializeField] private Button startSimulationButton;
    [SerializeField] private Button startPausedButton;
    [SerializeField] private Button randomizeSeedButton;
    [SerializeField] private Button resetDefaultsButton;

    private void Awake()
    {
        controller ??= FindFirstObjectByType<SimulationStartupController>();
        WireButtons();
    }

    private void OnEnable()
    {
        RefreshFromConfig();
    }

    private void OnDestroy()
    {
        if (startSimulationButton != null) startSimulationButton.onClick.RemoveListener(StartSimulation);
        if (startPausedButton != null) startPausedButton.onClick.RemoveListener(StartSimulationPaused);
        if (randomizeSeedButton != null) randomizeSeedButton.onClick.RemoveListener(RandomizeSeed);
        if (resetDefaultsButton != null) resetDefaultsButton.onClick.RemoveListener(ResetDefaults);
        if (axisTiltSlider != null) axisTiltSlider.onValueChanged.RemoveListener(OnAxisTiltChanged);
    }

    private void WireButtons()
    {
        if (startSimulationButton != null) startSimulationButton.onClick.AddListener(StartSimulation);
        if (startPausedButton != null) startPausedButton.onClick.AddListener(StartSimulationPaused);
        if (randomizeSeedButton != null) randomizeSeedButton.onClick.AddListener(RandomizeSeed);
        if (resetDefaultsButton != null) resetDefaultsButton.onClick.AddListener(ResetDefaults);
        if (axisTiltSlider != null) axisTiltSlider.onValueChanged.AddListener(OnAxisTiltChanged);
    }

    public void RefreshFromConfig()
    {
        if (controller == null || controller.CurrentConfig == null)
        {
            return;
        }

        SimulationStartupConfig config = controller.CurrentConfig;
        SetToggle(useRandomSeedToggle, config.useRandomSeed);
        SetText(seedInput, config.planetSeed.ToString());
        SetSlider(axisTiltSlider, config.axisTiltDegrees);
        SetText(dayLengthInput, config.dayLengthSeconds.ToString("0.###"));
        SetText(yearLengthInput, config.yearLengthInDays.ToString("0.###"));
        SetText(baseTempInput, config.baseTempKelvin.ToString("0.###"));
        SetText(insolationGainInput, config.insolationTempGain.ToString("0.###"));
        SetText(initialCO2Input, config.initialCO2.ToString("0.###"));
        SetText(initialO2Input, config.initialO2.ToString("0.###"));
        SetText(initialCH4Input, config.initialCH4.ToString("0.###"));
        SetText(initialFe2Input, config.initialDissolvedFe2Plus.ToString("0.###"));
        SetText(ventH2Input, config.ventH2PerTick.ToString("0.####"));
        SetText(ventH2SInput, config.ventH2SPerTick.ToString("0.####"));
        SetText(ventCO2Input, config.ventCO2PerTick.ToString("0.####"));
        SetText(initialSpawnCountInput, config.initialSpawnCount.ToString());
        OnAxisTiltChanged(config.axisTiltDegrees);
    }

    public void PushToConfig()
    {
        if (controller == null || controller.CurrentConfig == null)
        {
            return;
        }

        SimulationStartupConfig config = controller.CurrentConfig;
        config.useRandomSeed = useRandomSeedToggle == null ? config.useRandomSeed : useRandomSeedToggle.isOn;
        config.planetSeed = ReadInt(seedInput, config.planetSeed);
        config.axisTiltDegrees = axisTiltSlider == null ? config.axisTiltDegrees : axisTiltSlider.value;
        config.dayLengthSeconds = Mathf.Max(0.01f, ReadFloat(dayLengthInput, config.dayLengthSeconds));
        config.yearLengthInDays = Mathf.Max(1f, ReadFloat(yearLengthInput, config.yearLengthInDays));
        config.baseTempKelvin = ReadFloat(baseTempInput, config.baseTempKelvin);
        config.insolationTempGain = ReadFloat(insolationGainInput, config.insolationTempGain);
        config.initialCO2 = Mathf.Max(0f, ReadFloat(initialCO2Input, config.initialCO2));
        config.initialO2 = Mathf.Max(0f, ReadFloat(initialO2Input, config.initialO2));
        config.initialCH4 = Mathf.Max(0f, ReadFloat(initialCH4Input, config.initialCH4));
        config.initialDissolvedFe2Plus = Mathf.Max(0f, ReadFloat(initialFe2Input, config.initialDissolvedFe2Plus));
        config.ventH2PerTick = Mathf.Max(0f, ReadFloat(ventH2Input, config.ventH2PerTick));
        config.ventH2SPerTick = Mathf.Max(0f, ReadFloat(ventH2SInput, config.ventH2SPerTick));
        config.ventCO2PerTick = Mathf.Max(0f, ReadFloat(ventCO2Input, config.ventCO2PerTick));
        config.initialSpawnCount = Mathf.Max(0, ReadInt(initialSpawnCountInput, config.initialSpawnCount));
    }

    private void StartSimulation()
    {
        PushToConfig();
        controller?.StartSimulation();
    }

    private void StartSimulationPaused()
    {
        PushToConfig();
        controller?.StartSimulationPaused();
    }

    private void RandomizeSeed()
    {
        controller?.RandomizeSeed();
        RefreshFromConfig();
    }

    private void ResetDefaults()
    {
        controller?.ResetDefaults();
        RefreshFromConfig();
    }

    private void OnAxisTiltChanged(float value)
    {
        if (axisTiltValueLabel != null)
        {
            axisTiltValueLabel.text = $"{value:0.#}°";
        }
    }

    private static void SetText(TMP_InputField input, string value)
    {
        if (input != null) input.SetTextWithoutNotify(value);
    }

    private static void SetToggle(Toggle toggle, bool value)
    {
        if (toggle != null) toggle.SetIsOnWithoutNotify(value);
    }

    private static void SetSlider(Slider slider, float value)
    {
        if (slider != null) slider.SetValueWithoutNotify(value);
    }

    private static float ReadFloat(TMP_InputField input, float fallback)
    {
        return input != null && float.TryParse(input.text, out float parsed) ? parsed : fallback;
    }

    private static int ReadInt(TMP_InputField input, int fallback)
    {
        return input != null && int.TryParse(input.text, out int parsed) ? parsed : fallback;
    }
}
