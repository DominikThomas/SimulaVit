using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-2000)]
public class SimulationStartupController : MonoBehaviour
{
    private const float AxisTiltMinDegrees = 0f;
    private const float AxisTiltMaxDegrees = 90f;
    private const float DayLengthMinSeconds = 10f;
    private const float DayLengthMaxSeconds = 2000f;
    private const float YearLengthMinDays = 1f;
    private const float YearLengthMaxDays = 500f;
    private const float BaseTempMinKelvin = 200f;
    private const float BaseTempMaxKelvin = 330f;
    private const float InsolationGainMin = 0f;
    private const float InsolationGainMax = 120f;
    private const float InitialAtmosphereMin = 0f;
    private const float InitialCO2Max = 20f;
    private const float InitialO2Max = 10f;
    private const float InitialCH4Max = 10f;
    private const float InitialFe2Min = 0f;
    private const float InitialFe2Max = 20f;
    private const float VentPerTickMin = 0f;
    private const float VentH2MaxPerTick = 0.25f;
    private const float VentH2SMaxPerTick = 0.25f;
    private const float VentCO2MaxPerTick = 1f;
    private const int InitialSpawnMin = 0;
    private const int InitialSpawnMax = 10000;

    [Header("Startup Config")]
    [SerializeField] private SimulationStartupConfig defaults = new SimulationStartupConfig();
    [SerializeField] private SimulationStartupConfig currentConfig = new SimulationStartupConfig();

    [Header("References")]
    [SerializeField] private PlanetGenerator planetGenerator;
    [SerializeField] private PlanetResourceMap planetResourceMap;
    [SerializeField] private ReplicatorManager replicatorManager;
    [SerializeField] private SunSkyRotator sunSkyRotator;
    [SerializeField] private ReplicatorSimulationPipeline simulationPipeline;
    [Tooltip("Overlay used as the persistent setup curtain. If empty, the loading overlay is reused when possible.")]
    [SerializeField] private StartupFadeOverlay setupCurtainOrOverlay;
    [Tooltip("Overlay shown while applying startup config/regenerating and faded out when the world is ready.")]
    [SerializeField] private StartupFadeOverlay loadingOverlay;

    [Header("Screen Roots")]
    [Tooltip("Optional prefab/UI root to show while editing startup settings. If empty, the built-in IMGUI setup panel is used.")]
    [SerializeField] private GameObject startupScreenRoot;
    [Tooltip("Hide assigned runtime HUD roots during setup, then restore their exact previous active states after startup.")]
    [SerializeField] private bool hideRuntimeHudDuringSetup = true;
    [Tooltip("Optional runtime HUD objects that should be hidden during setup and restored after startup. Leave empty to auto-detect common HUD roots and rely on the global setup state for OnGUI HUDs.")]
    [SerializeField] private GameObject[] runtimeHudRoots;
    [Tooltip("Keep a full-screen curtain/overlay visible during setup so the world is covered while controls remain interactive.")]
    [SerializeField] private bool coverWorldDuringSetup = true;
    [Tooltip("Optional world/planet visual roots to disable during setup and restore exactly after startup. Prefer the overlay curtain unless a camera/background still leaks through.")]
    [SerializeField] private GameObject[] worldRootsToHideDuringSetup;
    [Tooltip("Disable assigned worldRootsToHideDuringSetup during setup. Usually unnecessary when coverWorldDuringSetup is enabled.")]
    [SerializeField] private bool hideWorldRootsDuringSetup;

    [Header("Built-in Setup UI")]
    [SerializeField] private bool useBuiltInSetupGui = true;
    [SerializeField] private float setupGuiWidth = 520f;
    [SerializeField] private float setupGuiTopPadding = 70f;

    private Vector2 setupGuiScrollPosition;
    private int resumeStepsPerFrame = 1;
    private bool startupComplete;
    private bool applyingConfig;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle boxStyle;
    private GUIStyle buttonStyle;
    private readonly Dictionary<GameObject, bool> runtimeHudRootStates = new Dictionary<GameObject, bool>();
    private readonly Dictionary<GameObject, bool> worldRootStates = new Dictionary<GameObject, bool>();
    private bool warnedAboutHudRoots;
    private bool warnedAboutMissingOverlay;

    public static bool IsSetupActive { get; private set; }
    public static bool IsStartupBlockingHud => IsSetupActive;

    public SimulationStartupConfig CurrentConfig => currentConfig;
    public bool StartupComplete => startupComplete;

    private void Awake()
    {
        ResolveReferences();
        CaptureSceneDefaults();
        ResetDefaults();
        PrepareDeferredStartup();
    }

    private IEnumerator Start()
    {
        ResolveReferences();
        PrepareDeferredStartup();
        ShowSetupScreen(true);

        if (coverWorldDuringSetup && setupCurtainOrOverlay == null)
        {
            LogMissingSetupOverlayWarning();
        }

        yield return null;
    }

    private void OnDisable()
    {
        RestoreWorldRoots();
        RestoreRuntimeHud();

        if (!startupComplete)
        {
            IsSetupActive = false;
        }
    }

    private void ResolveReferences()
    {
        planetGenerator ??= FindFirstObjectByType<PlanetGenerator>();
        planetResourceMap ??= FindFirstObjectByType<PlanetResourceMap>();
        replicatorManager ??= FindFirstObjectByType<ReplicatorManager>();
        sunSkyRotator ??= FindFirstObjectByType<SunSkyRotator>();
        simulationPipeline ??= FindFirstObjectByType<ReplicatorSimulationPipeline>();
        loadingOverlay ??= FindFirstObjectByType<StartupFadeOverlay>();
        setupCurtainOrOverlay ??= loadingOverlay;
        loadingOverlay ??= setupCurtainOrOverlay;
    }

    private void CaptureSceneDefaults()
    {
        if (planetGenerator != null)
        {
            defaults.planetSeed = planetGenerator.randomSeed;
            defaults.useRandomSeed = planetGenerator.useRandomSeed;
        }

        if (sunSkyRotator != null)
        {
            defaults.axisTiltDegrees = sunSkyRotator.axisTiltDegrees;
            defaults.dayLengthSeconds = sunSkyRotator.orbitDegreesPerSecond > 0f
                ? 360f / sunSkyRotator.orbitDegreesPerSecond
                : defaults.dayLengthSeconds;
            defaults.yearLengthInDays = sunSkyRotator.yearLengthInDays;
        }

        if (planetResourceMap != null)
        {
            defaults.baseTempKelvin = planetResourceMap.baseTempKelvin;
            defaults.insolationTempGain = planetResourceMap.insolationTempGain;
            defaults.initialCO2 = planetResourceMap.baselineCO2;
            defaults.initialO2 = planetResourceMap.baselineO2;
            defaults.initialCH4 = planetResourceMap.baselineCH4;
            defaults.initialDissolvedFe2Plus = planetResourceMap.initialDissolvedFe2PlusPerOceanCell;
            defaults.ventH2PerTick = planetResourceMap.ventH2PerTick;
            defaults.ventH2SPerTick = planetResourceMap.ventH2SPerTick;
            defaults.ventCO2PerTick = planetResourceMap.ventCO2PerTick;
        }

        if (replicatorManager != null)
        {
            defaults.initialSpawnCount = replicatorManager.initialSpawnCount;
            resumeStepsPerFrame = Mathf.Max(1, replicatorManager.ConfiguredSimulationStepsPerFrame);
        }
    }

    private void PrepareDeferredStartup()
    {
        Time.timeScale = 1f;

        if (replicatorManager != null)
        {
            replicatorManager.autoStartOnSceneLoad = false;
            replicatorManager.SetSimulationTiming(0);
        }

        if (simulationPipeline != null)
        {
            simulationPipeline.SetSimulationStepsPerFrame(0);
        }
    }

    public void RandomizeSeed()
    {
        currentConfig.useRandomSeed = true;
        currentConfig.planetSeed = System.Environment.TickCount ^ System.Guid.NewGuid().GetHashCode();
    }

    public void ResetDefaults()
    {
        currentConfig = defaults.Clone();
    }

    public void StartSimulation()
    {
        currentConfig.startPaused = false;
        StartCoroutine(ApplyAndStartRoutine(false));
    }

    public void StartSimulationPaused()
    {
        currentConfig.startPaused = true;
        StartCoroutine(ApplyAndStartRoutine(true));
    }

    private IEnumerator ApplyAndStartRoutine(bool keepPaused)
    {
        if (applyingConfig)
        {
            yield break;
        }

        applyingConfig = true;
        ShowSetupScreen(false);
        loadingOverlay?.ShowLoading("Generating planet...");
        yield return null;

        ApplyConfig(currentConfig);
        yield return null;

        if (replicatorManager != null)
        {
            if (replicatorManager.InitializeForSimulation(false))
            {
                replicatorManager.SpawnInitialPopulation();
            }
        }

        yield return null;

        int targetSteps = keepPaused ? 0 : Mathf.Max(1, resumeStepsPerFrame);
        replicatorManager?.SetSimulationTiming(targetSteps);
        simulationPipeline?.SetSimulationStepsPerFrame(targetSteps);

        startupComplete = true;
        applyingConfig = false;
        RestoreWorldRoots();
        RestoreRuntimeHud();
        IsSetupActive = false;
        loadingOverlay?.FadeOut(0.5f);
    }

    private void ApplyConfig(SimulationStartupConfig config)
    {
        if (config == null)
        {
            return;
        }

        if (planetGenerator != null)
        {
            int seed = config.useRandomSeed ? (System.Environment.TickCount ^ System.Guid.NewGuid().GetHashCode()) : config.planetSeed;
            config.planetSeed = seed;
            planetGenerator.ApplyStartupSeed(seed, config.useRandomSeed);
            planetGenerator.RegeneratePlanet();
        }

        if (sunSkyRotator != null)
        {
            sunSkyRotator.ApplyStartupTiming(config.axisTiltDegrees, config.dayLengthSeconds, config.yearLengthInDays);
        }

        if (planetResourceMap != null)
        {
            planetResourceMap.baseTempKelvin = config.baseTempKelvin;
            planetResourceMap.insolationTempGain = config.insolationTempGain;
            planetResourceMap.baselineCO2 = Mathf.Max(0f, config.initialCO2);
            planetResourceMap.baselineO2 = Mathf.Max(0f, config.initialO2);
            planetResourceMap.baselineCH4 = Mathf.Max(0f, config.initialCH4);
            planetResourceMap.initialDissolvedFe2PlusPerOceanCell = Mathf.Max(0f, config.initialDissolvedFe2Plus);
            planetResourceMap.ventH2PerTick = Mathf.Max(0f, config.ventH2PerTick);
            planetResourceMap.ventH2SPerTick = Mathf.Max(0f, config.ventH2SPerTick);
            planetResourceMap.ventCO2PerTick = Mathf.Max(0f, config.ventCO2PerTick);
            planetResourceMap.ReinitializeResources();
        }

        if (replicatorManager != null)
        {
            replicatorManager.initialSpawnCount = Mathf.Max(0, config.initialSpawnCount);
            replicatorManager.ClearPopulation();
        }
    }

    private void ShowSetupScreen(bool show)
    {
        IsSetupActive = (show || applyingConfig) && !startupComplete;

        if (startupScreenRoot != null)
        {
            startupScreenRoot.SetActive(show);
        }

        if (show)
        {
            HideRuntimeHudForSetup();
            HideWorldForSetup();

            if (coverWorldDuringSetup)
            {
                if (setupCurtainOrOverlay != null)
                {
                    setupCurtainOrOverlay.ShowSetupCurtain("Planet Simulation Setup");
                }
                else
                {
                    LogMissingSetupOverlayWarning();
                }
            }
        }
        else if (applyingConfig)
        {
            // Keep HUD/world hidden while the loading overlay covers regeneration.
            HideRuntimeHudForSetup();
            HideWorldForSetup();
        }
        else if (startupComplete)
        {
            RestoreWorldRoots();
            RestoreRuntimeHud();
        }
    }

    private void HideRuntimeHudForSetup()
    {
        if (!hideRuntimeHudDuringSetup)
        {
            return;
        }

        GameObject[] roots = GetRuntimeHudRoots();
        if (roots.Length == 0)
        {
            if (!warnedAboutHudRoots)
            {
                Debug.LogWarning("[SimulationStartupController] No runtimeHudRoots assigned or auto-detected. OnGUI HUDs will still be suppressed by SimulationStartupController.IsSetupActive, but GameObject-based HUDs may remain visible until assigned.");
                warnedAboutHudRoots = true;
            }
            return;
        }

        SetRootsActive(roots, false, runtimeHudRootStates);
    }

    private void RestoreRuntimeHud()
    {
        RestoreRootStates(runtimeHudRootStates);
    }

    private void HideWorldForSetup()
    {
        if (hideWorldRootsDuringSetup)
        {
            SetRootsActive(worldRootsToHideDuringSetup, false, worldRootStates);
        }
    }

    private void RestoreWorldRoots()
    {
        RestoreRootStates(worldRootStates);
    }

    private GameObject[] GetRuntimeHudRoots()
    {
        if (runtimeHudRoots != null && runtimeHudRoots.Length > 0)
        {
            return runtimeHudRoots;
        }

        List<GameObject> detectedRoots = new List<GameObject>();

        foreach (PlanetCellInspectorPanel inspectorPanel in FindObjectsByType<PlanetCellInspectorPanel>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            AddDetectedHudRoot(detectedRoots, inspectorPanel.gameObject);
        }

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (canvas == null || canvas.gameObject == startupScreenRoot || canvas.GetComponentInChildren<StartupFadeOverlay>(true) != null)
            {
                continue;
            }

            string name = canvas.gameObject.name;
            if (name.IndexOf("HUD", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Hud", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Inspector", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddDetectedHudRoot(detectedRoots, canvas.gameObject);
            }
        }

        return detectedRoots.ToArray();
    }

    private void AddDetectedHudRoot(List<GameObject> roots, GameObject candidate)
    {
        if (candidate == null || candidate == gameObject || candidate == startupScreenRoot || roots.Contains(candidate))
        {
            return;
        }

        if (startupScreenRoot != null && candidate.transform.IsChildOf(startupScreenRoot.transform))
        {
            return;
        }

        roots.Add(candidate);
    }

    private void SetRootsActive(GameObject[] roots, bool active, Dictionary<GameObject, bool> previousStates)
    {
        if (roots == null)
        {
            return;
        }

        foreach (GameObject root in roots)
        {
            if (root == null || root == gameObject || root == startupScreenRoot)
            {
                continue;
            }

            if (startupScreenRoot != null && root.transform.IsChildOf(startupScreenRoot.transform))
            {
                continue;
            }

            if (!previousStates.ContainsKey(root))
            {
                previousStates[root] = root.activeSelf;
            }

            root.SetActive(active);
        }
    }

    private void RestoreRootStates(Dictionary<GameObject, bool> previousStates)
    {
        foreach (KeyValuePair<GameObject, bool> state in previousStates)
        {
            if (state.Key != null)
            {
                state.Key.SetActive(state.Value);
            }
        }

        previousStates.Clear();
    }

    private void LogMissingSetupOverlayWarning()
    {
        if (warnedAboutMissingOverlay)
        {
            return;
        }

        Debug.LogWarning("[SimulationStartupController] coverWorldDuringSetup is enabled, but no StartupFadeOverlay is assigned. The built-in setup GUI will still work, but the planet/world may be visible behind setup until a setup curtain overlay is assigned.");
        warnedAboutMissingOverlay = true;
    }

    private void OnGUI()
    {
        if (startupComplete || applyingConfig || !useBuiltInSetupGui || startupScreenRoot != null)
        {
            return;
        }

        EnsureStyles();

        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, boxStyle);

        float width = Mathf.Max(1f, Mathf.Min(setupGuiWidth, Screen.width - 40f));
        float x = (Screen.width - width) * 0.5f;
        float line = 28f;
        float gap = 8f;
        float visibleTopPadding = Mathf.Clamp(setupGuiTopPadding, 0f, Mathf.Max(0f, Screen.height - 100f));
        float scrollHeight = Mathf.Max(100f, Screen.height - visibleTopPadding - 20f);
        Rect setupRect = new Rect(x, visibleTopPadding, width, scrollHeight);

        GUILayout.BeginArea(setupRect);
        setupGuiScrollPosition = GUILayout.BeginScrollView(setupGuiScrollPosition, GUILayout.Width(width), GUILayout.Height(scrollHeight));

        float contentWidth = Mathf.Max(1f, width - 20f);
        float contentHeight = 44f + ((line + gap) * 15f) + (gap * 2f) + 42f + 30f + 6f;
        Rect contentRect = GUILayoutUtility.GetRect(contentWidth, contentHeight, GUILayout.Width(contentWidth), GUILayout.Height(contentHeight));
        float controlX = contentRect.x;
        float y = contentRect.y;

        GUI.Label(new Rect(controlX, y, contentWidth, 34f), "Planet Simulation Setup", titleStyle);
        y += 44f;

        DrawBool(new Rect(controlX, y, contentWidth, line), "Use Random Seed", ref currentConfig.useRandomSeed);
        y += line + gap;
        DrawInt(new Rect(controlX, y, contentWidth, line), "Planet Seed", ref currentConfig.planetSeed, !currentConfig.useRandomSeed);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Axis Tilt (deg)", ref currentConfig.axisTiltDegrees, AxisTiltMinDegrees, AxisTiltMaxDegrees);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Day Length (sec)", ref currentConfig.dayLengthSeconds, DayLengthMinSeconds, DayLengthMaxSeconds);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Year Length (days)", ref currentConfig.yearLengthInDays, YearLengthMinDays, YearLengthMaxDays);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Base Temp (K)", ref currentConfig.baseTempKelvin, BaseTempMinKelvin, BaseTempMaxKelvin);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Insolation Gain", ref currentConfig.insolationTempGain, InsolationGainMin, InsolationGainMax);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Initial CO2", ref currentConfig.initialCO2, InitialAtmosphereMin, InitialCO2Max);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Initial O2", ref currentConfig.initialO2, InitialAtmosphereMin, InitialO2Max);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Initial CH4", ref currentConfig.initialCH4, InitialAtmosphereMin, InitialCH4Max);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Initial Fe2+", ref currentConfig.initialDissolvedFe2Plus, InitialFe2Min, InitialFe2Max);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Vent H2 / Tick", ref currentConfig.ventH2PerTick, VentPerTickMin, VentH2MaxPerTick);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Vent H2S / Tick", ref currentConfig.ventH2SPerTick, VentPerTickMin, VentH2SMaxPerTick);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Vent CO2 / Tick", ref currentConfig.ventCO2PerTick, VentPerTickMin, VentCO2MaxPerTick);
        y += line + gap;
        DrawInt(new Rect(controlX, y, contentWidth, line), "Initial Spawn Count", ref currentConfig.initialSpawnCount, true, InitialSpawnMin, InitialSpawnMax);
        y += line + (gap * 2f);

        float buttonWidth = (contentWidth - gap) * 0.5f;
        if (GUI.Button(new Rect(controlX, y, buttonWidth, 34f), "Start Simulation", buttonStyle))
        {
            StartSimulation();
        }
        if (GUI.Button(new Rect(controlX + buttonWidth + gap, y, buttonWidth, 34f), "Start Paused", buttonStyle))
        {
            StartSimulationPaused();
        }
        y += 42f;
        if (GUI.Button(new Rect(controlX, y, buttonWidth, 30f), "Randomize Seed", buttonStyle))
        {
            RandomizeSeed();
        }
        if (GUI.Button(new Rect(controlX + buttonWidth + gap, y, buttonWidth, 30f), "Reset Defaults", buttonStyle))
        {
            ResetDefaults();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawBool(Rect rect, string label, ref bool value)
    {
        GUI.Label(new Rect(rect.x, rect.y, rect.width * 0.45f, rect.height), label, labelStyle);
        value = GUI.Toggle(new Rect(rect.x + rect.width * 0.48f, rect.y, rect.width * 0.52f, rect.height), value, value ? "Yes" : "No");
    }

    private void DrawFloat(Rect rect, string label, ref float value, float min, float max)
    {
        float clampedValue = Mathf.Clamp(value, min, max);
        if (!Mathf.Approximately(value, clampedValue))
        {
            value = clampedValue;
        }

        GUI.Label(new Rect(rect.x, rect.y, rect.width * 0.42f, rect.height), $"{label}: {value:0.###} [{min:0.###}-{max:0.###}]", labelStyle);

        Rect sliderRect = new Rect(rect.x + rect.width * 0.44f, rect.y + 8f, rect.width * 0.34f, rect.height);
        float sliderValue = GUI.HorizontalSlider(sliderRect, value, min, max);
        if (!Mathf.Approximately(value, sliderValue))
        {
            value = sliderValue;
        }

        Rect fieldRect = new Rect(rect.x + rect.width * 0.80f, rect.y, rect.width * 0.20f, rect.height);
        string next = GUI.TextField(fieldRect, value.ToString("0.####"));
        if (float.TryParse(next, out float parsed))
        {
            value = Mathf.Clamp(parsed, min, max);
        }
    }

    private void DrawInt(Rect rect, string label, ref int value, bool enabled, int min = int.MinValue, int max = int.MaxValue)
    {
        bool oldEnabled = GUI.enabled;
        GUI.enabled = enabled;
        GUI.Label(new Rect(rect.x, rect.y, rect.width * 0.42f, rect.height), FormatIntLabel(label, value, min, max), labelStyle);

        Rect controlRect = new Rect(rect.x + rect.width * 0.44f, rect.y, rect.width * 0.56f, rect.height);
        if (min != int.MinValue || max != int.MaxValue)
        {
            int clampedValue = Mathf.Clamp(value, min, max);
            if (value != clampedValue)
            {
                value = clampedValue;
            }

            Rect sliderRect = new Rect(controlRect.x, rect.y + 8f, rect.width * 0.34f, rect.height);
            value = Mathf.RoundToInt(GUI.HorizontalSlider(sliderRect, value, min, max));

            Rect fieldRect = new Rect(rect.x + rect.width * 0.80f, rect.y, rect.width * 0.20f, rect.height);
            string next = GUI.TextField(fieldRect, value.ToString());
            if (int.TryParse(next, out int parsed))
            {
                value = Mathf.Clamp(parsed, min, max);
            }
        }
        else
        {
            string next = GUI.TextField(controlRect, value.ToString());
            if (int.TryParse(next, out int parsed))
            {
                value = parsed;
            }
        }
        GUI.enabled = oldEnabled;
    }

    private static string FormatIntLabel(string label, int value, int min, int max)
    {
        if (min != int.MinValue || max != int.MaxValue)
        {
            return $"{label}: {value} [{min}-{max}]";
        }

        return $"{label}: {value}";
    }

    private void EnsureStyles()
    {
        titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        labelStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = Color.white }
        };

        buttonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };

        if (boxStyle == null)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.82f));
            texture.Apply();
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = texture;
        }
    }
}
