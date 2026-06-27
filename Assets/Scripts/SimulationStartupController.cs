using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
    private const int SavedStartupConfigVersion = 1;

    [Header("Startup Config")]
    [SerializeField] private SimulationStartupConfig defaults = new SimulationStartupConfig();
    [SerializeField] private SimulationStartupConfig currentConfig = new SimulationStartupConfig();

    [Header("Startup Config Persistence")]
    [SerializeField] private bool loadSavedStartupConfig = true;
    [SerializeField] private bool saveStartupConfigOnStart = true;
    [SerializeField] private bool logAppliedStartupConfig = true;
    [SerializeField] private string savedConfigFileName = "startup_config.json";

    [Header("References")]
    [SerializeField] private PlanetGenerator planetGenerator;
    [SerializeField] private PlanetResourceMap planetResourceMap;
    [SerializeField] private ReplicatorManager replicatorManager;
    [SerializeField] private SunSkyRotator sunSkyRotator;
    [SerializeField] private ReplicatorSimulationPipeline simulationPipeline;
    [SerializeField] private SimulationSaveLoadService saveLoadService;
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

    [Header("Built-in Start Screen UI")]
    [SerializeField] private bool useBuiltInSetupGui = true;
    [SerializeField] private float setupGuiWidth = 520f;
    [SerializeField] private float setupGuiTopPadding = 70f;

    private enum BuiltInScreenMode { MainMenu, SavePicker }

    private Vector2 setupGuiScrollPosition;
    private Vector2 savePickerScrollPosition;
    private BuiltInScreenMode builtInScreenMode;
    private IReadOnlyList<SimulationSaveLoadService.SaveFileInfo> cachedSaveFiles = Array.Empty<SimulationSaveLoadService.SaveFileInfo>();
    private string startScreenStatusMessage;
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
        LoadSavedStartupConfigIfEnabled();
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
        saveLoadService ??= FindFirstObjectByType<SimulationSaveLoadService>();
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

    private void LoadSavedStartupConfigIfEnabled()
    {
        if (!loadSavedStartupConfig)
        {
            return;
        }

        LoadSavedStartupConfig();
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


    public void QuickLoadFromStartScreen()
    {
        ResolveReferences();
        if (saveLoadService != null && saveLoadService.LoadLatestSave())
        {
            CompleteStartupAfterLoadedGame();
        }
        else
        {
            startScreenStatusMessage = "No save files found or latest save failed to load.";
        }
    }

    public void ShowSavePicker()
    {
        builtInScreenMode = BuiltInScreenMode.SavePicker;
        RefreshSavePicker();
    }

    public void RefreshSavePicker()
    {
        ResolveReferences();
        cachedSaveFiles = saveLoadService != null
            ? saveLoadService.ListSaveFiles()
            : Array.Empty<SimulationSaveLoadService.SaveFileInfo>();
        startScreenStatusMessage = cachedSaveFiles.Count == 0 ? "No save files found." : null;
    }

    public void BackToStartMenu()
    {
        builtInScreenMode = BuiltInScreenMode.MainMenu;
        startScreenStatusMessage = null;
    }

    public void LoadSaveFromStartScreen(string path)
    {
        ResolveReferences();
        if (saveLoadService != null && saveLoadService.LoadSnapshotFromPath(path))
        {
            CompleteStartupAfterLoadedGame();
        }
        else
        {
            startScreenStatusMessage = $"Failed to load save: {System.IO.Path.GetFileName(path)}";
            Debug.LogError($"[SimulationStartupController] Failed to load selected save: {path}", this);
        }
    }

    public void ExitApplication()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void CompleteStartupAfterLoadedGame()
    {
        builtInScreenMode = BuiltInScreenMode.MainMenu;
        startupComplete = true;
        applyingConfig = false;
        RestoreWorldRoots();
        RestoreRuntimeHud();
        IsSetupActive = false;
        loadingOverlay?.FadeOut(0.25f);
        replicatorManager?.SetSimulationTiming(1);
        simulationPipeline?.SetSimulationStepsPerFrame(1);
        FindFirstObjectByType<SimulationSpeedController>()?.RefreshFromSimulationTiming();
        FindFirstObjectByType<PlanetCellInspectorController>()?.ClearSelection();
    }

    public void RandomizeSeed()
    {
        currentConfig.useRandomSeed = true;
        currentConfig.planetSeed = GenerateConcreteSeed();
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

        if (saveStartupConfigOnStart)
        {
            SaveStartupConfig(currentConfig);
        }

        if (logAppliedStartupConfig)
        {
            LogStartupConfigApplied(currentConfig, keepPaused);
        }

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
        FindFirstObjectByType<SimulationSpeedController>()?.RefreshFromSimulationTiming();

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

        bool requestedRandomSeed = config.useRandomSeed;
        int seed = requestedRandomSeed ? GenerateConcreteSeed() : config.planetSeed;
        config.planetSeed = seed;

        if (planetGenerator != null)
        {
            planetGenerator.ApplyStartupSeed(seed, requestedRandomSeed);
            planetGenerator.RegeneratePlanet();
        }

        if (requestedRandomSeed)
        {
            config.useRandomSeed = false;
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


    private int GenerateConcreteSeed()
    {
        return Environment.TickCount ^ Guid.NewGuid().GetHashCode();
    }

    private string SavedStartupConfigPath => Path.Combine(Application.persistentDataPath, string.IsNullOrWhiteSpace(savedConfigFileName) ? "startup_config.json" : savedConfigFileName);

    [ContextMenu("Clear Saved Startup Config")]
    public void ClearSavedStartupConfig()
    {
        string path = SavedStartupConfigPath;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"[SimulationStartupController] Cleared saved startup config: {path}");
            }
            else
            {
                Debug.Log($"[SimulationStartupController] No saved startup config to clear: {path}");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[SimulationStartupController] Failed to clear saved startup config at {path}: {exception.Message}");
        }
    }

    [ContextMenu("Save Current Startup Config")]
    public void SaveCurrentStartupConfig()
    {
        SaveStartupConfig(currentConfig);
    }

    [ContextMenu("Load Saved Startup Config")]
    public void LoadSavedStartupConfigFromContextMenu()
    {
        if (LoadSavedStartupConfig())
        {
            RefreshStartupPanels();
        }
    }

    private bool LoadSavedStartupConfig()
    {
        string path = SavedStartupConfigPath;
        if (!File.Exists(path))
        {
            if (logAppliedStartupConfig)
            {
                Debug.Log($"[SimulationStartupController] No saved startup config found at: {path}");
            }
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning($"[SimulationStartupController] Saved startup config is empty at {path}. Falling back to scene defaults.");
                return false;
            }

            SavedStartupConfig savedConfig = SavedStartupConfig.FromDefaults(defaults);
            JsonUtility.FromJsonOverwrite(json, savedConfig);
            currentConfig = savedConfig.ToConfig(defaults);
            ClampLoadedConfig(currentConfig);

            if (logAppliedStartupConfig)
            {
                Debug.Log($"[SimulationStartupController] Loaded saved startup config from: {path}");
            }

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[SimulationStartupController] Failed to load saved startup config at {path}. Falling back to scene defaults. {exception.Message}");
            return false;
        }
    }

    private void SaveStartupConfig(SimulationStartupConfig config)
    {
        if (config == null)
        {
            return;
        }

        string path = SavedStartupConfigPath;
        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            SavedStartupConfig savedConfig = SavedStartupConfig.FromConfig(config);
            string json = JsonUtility.ToJson(savedConfig, true);
            File.WriteAllText(path, json);

            if (logAppliedStartupConfig)
            {
                Debug.Log($"[SimulationStartupController] Saved startup config to: {path}");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[SimulationStartupController] Failed to save startup config at {path}: {exception.Message}");
        }
    }

    private void ClampLoadedConfig(SimulationStartupConfig config)
    {
        if (config == null)
        {
            return;
        }

        config.axisTiltDegrees = Mathf.Clamp(config.axisTiltDegrees, AxisTiltMinDegrees, AxisTiltMaxDegrees);
        config.dayLengthSeconds = Mathf.Clamp(config.dayLengthSeconds, DayLengthMinSeconds, DayLengthMaxSeconds);
        config.yearLengthInDays = Mathf.Clamp(config.yearLengthInDays, YearLengthMinDays, YearLengthMaxDays);
        config.baseTempKelvin = Mathf.Clamp(config.baseTempKelvin, BaseTempMinKelvin, BaseTempMaxKelvin);
        config.insolationTempGain = Mathf.Clamp(config.insolationTempGain, InsolationGainMin, InsolationGainMax);
        config.initialCO2 = Mathf.Clamp(config.initialCO2, InitialAtmosphereMin, InitialCO2Max);
        config.initialO2 = Mathf.Clamp(config.initialO2, InitialAtmosphereMin, InitialO2Max);
        config.initialCH4 = Mathf.Clamp(config.initialCH4, InitialAtmosphereMin, InitialCH4Max);
        config.initialDissolvedFe2Plus = Mathf.Clamp(config.initialDissolvedFe2Plus, InitialFe2Min, InitialFe2Max);
        config.ventH2PerTick = Mathf.Clamp(config.ventH2PerTick, VentPerTickMin, VentH2MaxPerTick);
        config.ventH2SPerTick = Mathf.Clamp(config.ventH2SPerTick, VentPerTickMin, VentH2SMaxPerTick);
        config.ventCO2PerTick = Mathf.Clamp(config.ventCO2PerTick, VentPerTickMin, VentCO2MaxPerTick);
        config.initialSpawnCount = Mathf.Clamp(config.initialSpawnCount, InitialSpawnMin, InitialSpawnMax);
    }

    private void RefreshStartupPanels()
    {
        foreach (SimulationStartupPanel panel in FindObjectsByType<SimulationStartupPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            panel.RefreshFromConfig();
        }
    }

    private void LogStartupConfigApplied(SimulationStartupConfig config, bool startPaused)
    {
        if (config == null)
        {
            return;
        }

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("[Startup Config Applied]");
        builder.AppendLine($"Planet Seed: {config.planetSeed}");
        builder.AppendLine($"Use Random Seed: {config.useRandomSeed}");
        builder.AppendLine($"Axis Tilt Degrees: {config.axisTiltDegrees:0.###}");
        builder.AppendLine($"Day Length Seconds: {config.dayLengthSeconds:0.###}");
        builder.AppendLine($"Year Length In Days: {config.yearLengthInDays:0.###}");
        builder.AppendLine($"Base Temp Kelvin: {config.baseTempKelvin:0.###}");
        builder.AppendLine($"Insolation Temp Gain: {config.insolationTempGain:0.###}");
        builder.AppendLine($"Initial CO2: {config.initialCO2:0.###}");
        builder.AppendLine($"Initial O2: {config.initialO2:0.###}");
        builder.AppendLine($"Initial CH4: {config.initialCH4:0.###}");
        builder.AppendLine($"Initial Dissolved Fe2+: {config.initialDissolvedFe2Plus:0.###}");
        builder.AppendLine($"Vent H2 Per Tick: {config.ventH2PerTick:0.####}");
        builder.AppendLine($"Vent H2S Per Tick: {config.ventH2SPerTick:0.####}");
        builder.AppendLine($"Vent CO2 Per Tick: {config.ventCO2PerTick:0.####}");
        builder.AppendLine($"Initial Spawn Count: {config.initialSpawnCount}");
        builder.AppendLine($"Start Paused: {startPaused}");
        builder.AppendLine($"Saved Config Path: {SavedStartupConfigPath}");
        Debug.Log(builder.ToString());
    }

    [Serializable]
    private class SavedStartupConfig
    {
        public int version = SavedStartupConfigVersion;
        public int planetSeed;
        public bool useRandomSeed;
        public float axisTiltDegrees;
        public float dayLengthSeconds;
        public float yearLengthInDays;
        public float baseTempKelvin;
        public float insolationTempGain;
        public float initialCO2;
        public float initialO2;
        public float initialCH4;
        public float initialDissolvedFe2Plus;
        public float ventH2PerTick;
        public float ventH2SPerTick;
        public float ventCO2PerTick;
        public int initialSpawnCount;
        public bool startPaused;

        public static SavedStartupConfig FromDefaults(SimulationStartupConfig defaults)
        {
            return FromConfig(defaults ?? new SimulationStartupConfig());
        }

        public static SavedStartupConfig FromConfig(SimulationStartupConfig config)
        {
            config ??= new SimulationStartupConfig();
            return new SavedStartupConfig
            {
                version = SavedStartupConfigVersion,
                planetSeed = config.planetSeed,
                useRandomSeed = config.useRandomSeed,
                axisTiltDegrees = config.axisTiltDegrees,
                dayLengthSeconds = config.dayLengthSeconds,
                yearLengthInDays = config.yearLengthInDays,
                baseTempKelvin = config.baseTempKelvin,
                insolationTempGain = config.insolationTempGain,
                initialCO2 = config.initialCO2,
                initialO2 = config.initialO2,
                initialCH4 = config.initialCH4,
                initialDissolvedFe2Plus = config.initialDissolvedFe2Plus,
                ventH2PerTick = config.ventH2PerTick,
                ventH2SPerTick = config.ventH2SPerTick,
                ventCO2PerTick = config.ventCO2PerTick,
                initialSpawnCount = config.initialSpawnCount,
                startPaused = config.startPaused
            };
        }

        public SimulationStartupConfig ToConfig(SimulationStartupConfig fallback)
        {
            SimulationStartupConfig config = (fallback ?? new SimulationStartupConfig()).Clone();
            config.planetSeed = planetSeed;
            config.useRandomSeed = useRandomSeed;
            config.axisTiltDegrees = axisTiltDegrees;
            config.dayLengthSeconds = dayLengthSeconds;
            config.yearLengthInDays = yearLengthInDays;
            config.baseTempKelvin = baseTempKelvin;
            config.insolationTempGain = insolationTempGain;
            config.initialCO2 = initialCO2;
            config.initialO2 = initialO2;
            config.initialCH4 = initialCH4;
            config.initialDissolvedFe2Plus = initialDissolvedFe2Plus;
            config.ventH2PerTick = ventH2PerTick;
            config.ventH2SPerTick = ventH2SPerTick;
            config.ventCO2PerTick = ventCO2PerTick;
            config.initialSpawnCount = initialSpawnCount;
            config.startPaused = startPaused;
            return config;
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

        if (builtInScreenMode == BuiltInScreenMode.SavePicker)
        {
            DrawBuiltInSavePicker();
        }
        else
        {
            DrawBuiltInStartMenu();
        }
    }

    private void DrawBuiltInStartMenu()
    {
        float width = Mathf.Max(1f, Mathf.Min(420f, Screen.width - 40f));
        float height = 330f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        GUILayout.BeginArea(rect);
        GUILayout.Label("SimulaVit", titleStyle, GUILayout.Height(52f));
        GUILayout.Space(16f);
        if (GUILayout.Button("New Game", buttonStyle, GUILayout.Height(42f))) StartSimulation();
        if (GUILayout.Button("Quick Load", buttonStyle, GUILayout.Height(42f))) QuickLoadFromStartScreen();
        if (GUILayout.Button("Load Game", buttonStyle, GUILayout.Height(42f))) ShowSavePicker();
        if (GUILayout.Button("Exit", buttonStyle, GUILayout.Height(42f))) ExitApplication();
        if (!string.IsNullOrWhiteSpace(startScreenStatusMessage))
        {
            GUILayout.Space(12f);
            GUILayout.Label(startScreenStatusMessage, labelStyle);
        }
        GUILayout.EndArea();
    }

    private void DrawBuiltInSavePicker()
    {
        float width = Mathf.Max(1f, Mathf.Min(720f, Screen.width - 40f));
        float height = Mathf.Max(260f, Mathf.Min(560f, Screen.height - 80f));
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        GUILayout.BeginArea(rect);
        GUILayout.Label("Load Game", titleStyle, GUILayout.Height(42f));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Back", buttonStyle, GUILayout.Height(34f))) BackToStartMenu();
        if (GUILayout.Button("Refresh", buttonStyle, GUILayout.Height(34f))) RefreshSavePicker();
        GUILayout.EndHorizontal();
        GUILayout.Space(8f);

        if (!string.IsNullOrWhiteSpace(startScreenStatusMessage)) GUILayout.Label(startScreenStatusMessage, labelStyle);

        savePickerScrollPosition = GUILayout.BeginScrollView(savePickerScrollPosition, GUILayout.Height(height - 120f));
        foreach (SimulationSaveLoadService.SaveFileInfo save in cachedSaveFiles)
        {
            string row = $"{save.FileName}  •  {FormatBytes(save.SizeBytes)}  •  {save.LastWriteTimeLocal:yyyy-MM-dd HH:mm:ss}";
            if (GUILayout.Button(row, buttonStyle, GUILayout.Height(34f))) LoadSaveFromStartScreen(save.Path);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024d * 1024d):0.##} MB";
        if (bytes >= 1024L) return $"{bytes / 1024d:0.##} KB";
        return $"{bytes} B";
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
