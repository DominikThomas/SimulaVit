using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-2000)]
public class SimulationStartupController : MonoBehaviour
{
    [Header("Startup Config")]
    [SerializeField] private SimulationStartupConfig defaults = new SimulationStartupConfig();
    [SerializeField] private SimulationStartupConfig currentConfig = new SimulationStartupConfig();

    [Header("References")]
    [SerializeField] private PlanetGenerator planetGenerator;
    [SerializeField] private PlanetResourceMap planetResourceMap;
    [SerializeField] private ReplicatorManager replicatorManager;
    [SerializeField] private SunSkyRotator sunSkyRotator;
    [SerializeField] private ReplicatorSimulationPipeline simulationPipeline;
    [SerializeField] private StartupFadeOverlay loadingOverlay;

    [Header("Screen Roots")]
    [Tooltip("Optional prefab/UI root to show while editing startup settings. If empty, the built-in IMGUI setup panel is used.")]
    [SerializeField] private GameObject startupScreenRoot;
    [Tooltip("Optional runtime HUD objects that should be hidden during setup and restored after startup.")]
    [SerializeField] private GameObject[] runtimeHudRoots;

    [Header("Built-in Setup UI")]
    [SerializeField] private bool useBuiltInSetupGui = true;
    [SerializeField] private float setupGuiWidth = 520f;
    [SerializeField] private float setupGuiTopPadding = 70f;

    private int resumeStepsPerFrame = 1;
    private bool startupComplete;
    private bool applyingConfig;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle boxStyle;
    private GUIStyle buttonStyle;

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

        if (loadingOverlay != null)
        {
            loadingOverlay.ShowImmediate("Preparing setup...");
            yield return null;
            loadingOverlay.FadeOut(0.35f);
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
        loadingOverlay?.ShowImmediate("Generating planet...");
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
        ShowRuntimeHud(true);
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
        if (startupScreenRoot != null)
        {
            startupScreenRoot.SetActive(show);
        }

        ShowRuntimeHud(!show && startupComplete);
    }

    private void ShowRuntimeHud(bool show)
    {
        if (runtimeHudRoots == null)
        {
            return;
        }

        foreach (GameObject root in runtimeHudRoots)
        {
            if (root != null)
            {
                root.SetActive(show);
            }
        }
    }

    private void OnGUI()
    {
        if (startupComplete || !useBuiltInSetupGui || startupScreenRoot != null)
        {
            return;
        }

        EnsureStyles();

        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, boxStyle);

        float width = Mathf.Min(setupGuiWidth, Screen.width - 40f);
        float x = (Screen.width - width) * 0.5f;
        float y = setupGuiTopPadding;
        float line = 28f;
        float gap = 8f;

        GUI.Label(new Rect(x, y, width, 34f), "Planet Simulation Setup", titleStyle);
        y += 44f;

        DrawBool(new Rect(x, y, width, line), "Use Random Seed", ref currentConfig.useRandomSeed);
        y += line + gap;
        DrawInt(new Rect(x, y, width, line), "Planet Seed", ref currentConfig.planetSeed, !currentConfig.useRandomSeed);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Axis Tilt", ref currentConfig.axisTiltDegrees, 0f, 90f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Day Length (sec)", ref currentConfig.dayLengthSeconds, 1f, 3600f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Year Length (days)", ref currentConfig.yearLengthInDays, 1f, 1000f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Base Temp (K)", ref currentConfig.baseTempKelvin, 150f, 450f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Insolation Gain", ref currentConfig.insolationTempGain, 0f, 120f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Initial CO2", ref currentConfig.initialCO2, 0f, 5f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Initial O2", ref currentConfig.initialO2, 0f, 1f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Initial CH4", ref currentConfig.initialCH4, 0f, 1f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Initial Fe2+", ref currentConfig.initialDissolvedFe2Plus, 0f, 25f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Vent H2 / Tick", ref currentConfig.ventH2PerTick, 0f, 0.05f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Vent H2S / Tick", ref currentConfig.ventH2SPerTick, 0f, 0.05f);
        y += line + gap;
        DrawFloat(new Rect(x, y, width, line), "Vent CO2 / Tick", ref currentConfig.ventCO2PerTick, 0f, 0.05f);
        y += line + gap;
        DrawInt(new Rect(x, y, width, line), "Initial Spawn Count", ref currentConfig.initialSpawnCount, true, 0, 10000);
        y += line + (gap * 2f);

        float buttonWidth = (width - gap) * 0.5f;
        if (GUI.Button(new Rect(x, y, buttonWidth, 34f), "Start Simulation", buttonStyle))
        {
            StartSimulation();
        }
        if (GUI.Button(new Rect(x + buttonWidth + gap, y, buttonWidth, 34f), "Start Paused", buttonStyle))
        {
            StartSimulationPaused();
        }
        y += 42f;
        if (GUI.Button(new Rect(x, y, buttonWidth, 30f), "Randomize Seed", buttonStyle))
        {
            RandomizeSeed();
        }
        if (GUI.Button(new Rect(x + buttonWidth + gap, y, buttonWidth, 30f), "Reset Defaults", buttonStyle))
        {
            ResetDefaults();
        }
    }

    private void DrawBool(Rect rect, string label, ref bool value)
    {
        GUI.Label(new Rect(rect.x, rect.y, rect.width * 0.45f, rect.height), label, labelStyle);
        value = GUI.Toggle(new Rect(rect.x + rect.width * 0.48f, rect.y, rect.width * 0.52f, rect.height), value, value ? "Yes" : "No");
    }

    private void DrawFloat(Rect rect, string label, ref float value, float min, float max)
    {
        GUI.Label(new Rect(rect.x, rect.y, rect.width * 0.45f, rect.height), $"{label}: {value:0.###}", labelStyle);
        value = GUI.HorizontalSlider(new Rect(rect.x + rect.width * 0.48f, rect.y + 8f, rect.width * 0.52f, rect.height), value, min, max);
    }

    private void DrawInt(Rect rect, string label, ref int value, bool enabled, int min = int.MinValue, int max = int.MaxValue)
    {
        bool oldEnabled = GUI.enabled;
        GUI.enabled = enabled;
        GUI.Label(new Rect(rect.x, rect.y, rect.width * 0.45f, rect.height), $"{label}: {value}", labelStyle);
        if (min != int.MinValue || max != int.MaxValue)
        {
            value = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(rect.x + rect.width * 0.48f, rect.y + 8f, rect.width * 0.52f, rect.height), value, min, max));
        }
        else
        {
            string next = GUI.TextField(new Rect(rect.x + rect.width * 0.48f, rect.y, rect.width * 0.52f, rect.height), value.ToString());
            if (int.TryParse(next, out int parsed))
            {
                value = parsed;
            }
        }
        GUI.enabled = oldEnabled;
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
