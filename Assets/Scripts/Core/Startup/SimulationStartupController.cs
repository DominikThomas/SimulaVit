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
    private const int SavedStartupConfigVersion = 7;
    public const float NormalAtmospherePressureMaxBar = 5f;
    public const float DenseAtmospherePressureMaxBar = 600f;
    public const float DefaultApproximateThermalIntervalSeconds = 2f;
    public const float DefaultResourceTransportIntervalSeconds = 5f;
    public const float DefaultChemistryTelemetryIntervalSimSeconds = 60f;
    public static readonly float[] ApproximateThermalIntervalPresets = { 0.5f, 1f, 2f, 5f };
    public static readonly float[] ResourceTransportIntervalPresets = { 1f, 2f, 5f, 10f };

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
    [SerializeField] private VentVisualizer ventVisualizer;
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

    private enum StartupUiState { MainMenu, ConfigScreen, SavePicker }

    private Vector2 setupGuiScrollPosition;
    private Vector2 savePickerScrollPosition;
    private StartupUiState startupUiState;
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
    private bool returningToMainMenu;
    private bool deferredStartupPrepared;
    private bool advancedSettingsExpanded;

    public static bool IsSetupActive { get; private set; }
    public static bool IsStartupBlockingHud => IsSetupActive;

    public SimulationStartupConfig CurrentConfig => currentConfig;
    public bool StartupComplete => startupComplete;

    private void Awake()
    {
        ResolveReferences();
        CaptureSceneDefaults();
        ResetAllToDefaults();
        LoadSavedStartupConfigIfEnabled();
        PrepareDeferredStartup();
    }

    private IEnumerator Start()
    {
        ResolveReferences();
        PrepareDeferredStartup();
        ShowMainMenu();

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
        ventVisualizer ??= FindFirstObjectByType<VentVisualizer>();
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
            defaults.gridType = planetGenerator.CurrentGridType;
            defaults.cubeSphereResolution = planetGenerator.resolution;
            defaults.geodesicSubdivisionLevel = planetGenerator.geodesicSubdivisionLevel;
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
        if (deferredStartupPrepared)
        {
            return;
        }

        deferredStartupPrepared = true;
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

        ClearRuntimePlanetState("startup menu entered");
        Debug.Log("[StartupLifecycle] Startup menu entered; planet not initialized.", this);
    }

    private void ClearRuntimePlanetState(string reason)
    {
        replicatorManager?.DeinitializeForStartupMenu();
        ventVisualizer?.ClearRuntimeVisuals(reason);
        planetResourceMap?.DeinitializeForStartupMenu(reason);
        planetGenerator?.ClearGeneratedPlanetRuntime();
    }



    public void ShowConfigScreenFromMainMenu()
    {
        advancedSettingsExpanded = false;
        startupUiState = StartupUiState.ConfigScreen;
        startScreenStatusMessage = null;
        ShowSetupScreen(true);
        RefreshStartupPanels();
    }

    private void ShowMainMenu()
    {
        startupUiState = StartupUiState.MainMenu;
        startScreenStatusMessage = null;
        IsSetupActive = !startupComplete || returningToMainMenu;

        if (startupScreenRoot != null)
        {
            startupScreenRoot.SetActive(false);
        }

        if (IsSetupActive)
        {
            HideRuntimeHudForSetup();
            HideWorldForSetup();

            if (coverWorldDuringSetup)
            {
                if (setupCurtainOrOverlay != null)
                {
                    setupCurtainOrOverlay.ShowSetupCurtain("SimulaVit");
                }
                else
                {
                    LogMissingSetupOverlayWarning();
                }
            }
        }
    }

    public void ExitToMainMenu()
    {
        ResolveReferences();
        returningToMainMenu = true;
        startupComplete = false;
        applyingConfig = false;
        startScreenStatusMessage = null;

        Time.timeScale = 1f;
        replicatorManager?.SetSimulationTiming(0);
        simulationPipeline?.SetSimulationStepsPerFrame(0);
        FindFirstObjectByType<SimulationSpeedController>()?.RefreshFromSimulationTiming();
        FindFirstObjectByType<PlanetCellInspectorController>()?.ClearSelection();
        ClearRuntimePlanetState("returning to main menu");

        ShowMainMenu();
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
        startupUiState = StartupUiState.SavePicker;
        IsSetupActive = !startupComplete;
        if (startupScreenRoot != null)
        {
            startupScreenRoot.SetActive(false);
        }
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
        ShowMainMenu();
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
        startupUiState = StartupUiState.MainMenu;
        startupComplete = true;
        returningToMainMenu = false;
        applyingConfig = false;
        RestoreWorldRoots();
        RestoreRuntimeHud();
        IsSetupActive = false;
        loadingOverlay?.FadeOut(0.25f);
        Debug.Log("[StartupLifecycle] Simulation started.", this);
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
        if (currentConfig == null) currentConfig = new SimulationStartupConfig();
        CopyNormalSettings(defaults ?? new SimulationStartupConfig(), currentConfig);
        NormalizeAtmosphereComposition(currentConfig);
        RefreshStartupPanels();
    }

    public void ResetAdvancedToDefaults()
    {
        if (currentConfig == null) currentConfig = new SimulationStartupConfig();
        CopyAdvancedSettings(defaults ?? new SimulationStartupConfig(), currentConfig);
        RefreshStartupPanels();
    }

    private void ResetAllToDefaults()
    {
        currentConfig = (defaults ?? new SimulationStartupConfig()).Clone();
        NormalizeAtmosphereComposition(currentConfig);
    }

    public static void CopyNormalSettings(SimulationStartupConfig source, SimulationStartupConfig destination)
    {
        if (source == null || destination == null) return;
        destination.planetSeed = source.planetSeed;
        destination.useRandomSeed = source.useRandomSeed;
        destination.gridType = source.gridType;
        destination.cubeSphereResolution = source.cubeSphereResolution;
        destination.axisTiltDegrees = source.axisTiltDegrees;
        destination.dayLengthSeconds = source.dayLengthSeconds;
        destination.yearLengthInDays = source.yearLengthInDays;
        destination.insolationTempGain = source.insolationTempGain;
        destination.initialCO2 = source.initialCO2;
        destination.initialO2 = source.initialO2;
        destination.initialCH4 = source.initialCH4;
        destination.initialAtmospherePressureBar = source.initialAtmospherePressureBar;
        destination.atmosphericCO2Fraction = source.atmosphericCO2Fraction;
        destination.atmosphericO2Fraction = source.atmosphericO2Fraction;
        destination.atmosphericCH4Fraction = source.atmosphericCH4Fraction;
        destination.atmosphericH2Fraction = source.atmosphericH2Fraction;
        destination.atmosphericH2SFraction = source.atmosphericH2SFraction;
        destination.atmosphericN2Bar = source.atmosphericN2Bar;
        destination.atmosphericCO2Bar = source.atmosphericCO2Bar;
        destination.atmosphericO2Bar = source.atmosphericO2Bar;
        destination.atmosphericCH4Bar = source.atmosphericCH4Bar;
        destination.atmosphericH2Bar = source.atmosphericH2Bar;
        destination.atmosphericH2SBar = source.atmosphericH2SBar;
        destination.initialDissolvedFe2Plus = source.initialDissolvedFe2Plus;
        destination.ventClustering = source.ventClustering;
        destination.ventH2PerTick = source.ventH2PerTick;
        destination.ventH2SPerTick = source.ventH2SPerTick;
        destination.ventCO2PerTick = source.ventCO2PerTick;
        destination.ventFe2PerTick = source.ventFe2PerTick;
        destination.initialSpawnCount = source.initialSpawnCount;
    }

    public static void CopyAdvancedSettings(SimulationStartupConfig source, SimulationStartupConfig destination)
    {
        if (source == null || destination == null) return;
        destination.geodesicSubdivisionLevel = source.geodesicSubdivisionLevel;
        destination.baseTempKelvin = source.baseTempKelvin;
        destination.terrestrialVentFraction = source.terrestrialVentFraction;
        destination.allowDenseAtmosphere = source.allowDenseAtmosphere;
        destination.atmosphereInventoryPerBar = source.atmosphereInventoryPerBar;
        destination.airSeaExchangeHalfLifeSeconds = source.airSeaExchangeHalfLifeSeconds;
        destination.geodesicBiologySpawnDelaySeconds = source.geodesicBiologySpawnDelaySeconds;
        destination.approximateThermalIntervalSeconds = source.approximateThermalIntervalSeconds;
        destination.geodesicResourceTransportIntervalSeconds = source.geodesicResourceTransportIntervalSeconds;
        destination.chemistryTelemetryIntervalSimSeconds = source.chemistryTelemetryIntervalSimSeconds;
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

        ClampLoadedConfig(currentConfig);
        ApplyConfig(currentConfig);

        if (saveStartupConfigOnStart)
        {
            SaveStartupConfig(currentConfig);
        }

        if (logAppliedStartupConfig)
        {
            LogStartupConfigApplied(currentConfig, keepPaused);
        }

        if (currentConfig.gridType == PlanetGridType.LegacyCubeSphere && replicatorManager != null)
        {
            if (replicatorManager.InitializeForSimulation(false))
            {
                replicatorManager.SpawnInitialPopulation();
            }
        }
        else if (currentConfig.gridType == PlanetGridType.GeodesicIcosphere)
        {
            if (replicatorManager != null && !replicatorManager.InitializeForSimulation(true))
            {
                Debug.LogError("[StartupLifecycle] Geodesic biology initialization failed.", this);
            }
        }

        // Simulation timing remains the shared authoritative clock for either biology mode.
        int targetSteps = keepPaused ? 0 : Mathf.Max(1, resumeStepsPerFrame);
        replicatorManager?.SetSimulationTiming(targetSteps);
        simulationPipeline?.SetSimulationStepsPerFrame(targetSteps);
        FindFirstObjectByType<SimulationSpeedController>()?.RefreshFromSimulationTiming();

        startupComplete = true;
        returningToMainMenu = false;
        applyingConfig = false;
        RestoreWorldRoots();
        RestoreRuntimeHud();
        IsSetupActive = false;
        loadingOverlay?.FadeOut(0.5f);
        Debug.Log("[StartupLifecycle] Simulation started.", this);
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

        simulationPipeline?.ResetClockForNewSimulation();
        replicatorManager?.ClearPopulation();

        if (sunSkyRotator != null)
        {
            sunSkyRotator.ApplyStartupTiming(config.axisTiltDegrees, config.dayLengthSeconds, config.yearLengthInDays);
        }

        if (planetGenerator != null)
        {
            planetGenerator.ApplyStartupSeed(seed, requestedRandomSeed);
            planetGenerator.ApplyStartupGrid(config.gridType, config.cubeSphereResolution, config.geodesicSubdivisionLevel);
            if (config.gridType == PlanetGridType.GeodesicIcosphere)
            {
                planetGenerator.GetComponent<GeodesicSurfaceTemperatureField>()?.SetStartupTemperatureParameters(config.baseTempKelvin, config.insolationTempGain);
                planetGenerator.GetComponent<GeodesicSurfaceTemperatureField>()?.SetStartupApproximateUpdateInterval(config.approximateThermalIntervalSeconds);
                planetGenerator.GetComponent<GeodesicOceanResourceField>()?.SetStartupConcentrations(config.initialCO2, config.initialO2, config.initialCH4, config.initialDissolvedFe2Plus);
                planetGenerator.GetComponent<GeodesicOceanResourceField>()?.SetStartupVentRates(config.ventH2PerTick, config.ventH2SPerTick, config.ventCO2PerTick, config.ventFe2PerTick);
                planetGenerator.GetComponent<GeodesicOceanResourceField>()?.SetStartupVentGeography(config.ventClustering, config.terrestrialVentFraction);
                planetGenerator.GetComponent<GeodesicOceanResourceField>()?.SetStartupTransportInterval(config.geodesicResourceTransportIntervalSeconds);
                planetGenerator.GetComponent<GeodesicOceanResourceField>()?.SetStartupChemistryTelemetryInterval(config.chemistryTelemetryIntervalSimSeconds);
                GeodesicAtmosphereField atmosphere = planetGenerator.GetComponent<GeodesicAtmosphereField>();
                atmosphere?.Configure(config.atmosphereInventoryPerBar, config.atmosphericN2Bar, config.atmosphericCO2Bar, config.atmosphericO2Bar, config.atmosphericCH4Bar, config.atmosphericH2Bar, config.atmosphericH2SBar);
                planetGenerator.GetComponent<GeodesicAirSeaGasExchange>()?.SetCommonHalfLife(config.airSeaExchangeHalfLifeSeconds);
            }
            planetGenerator.InitializeAuthoritativePlanet("New Game startup selection");
        }

        if (requestedRandomSeed)
        {
            config.useRandomSeed = false;
        }

        if (config.gridType == PlanetGridType.LegacyCubeSphere && planetResourceMap != null)
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
            planetGenerator?.RefreshLegacyIceVisuals("resources initialized after legacy startup config");
        }
        else if (config.gridType == PlanetGridType.GeodesicIcosphere)
        {
            planetResourceMap?.DeinitializeForStartupMenu("geodesic prototype mode selected");
            Debug.Log("[StartupLifecycle] Geodesic mode: skipping legacy PlanetResourceMap resource initialization and vent/resource overlays; GeodesicOceanResourceField owns dissolved-ocean concentrations.", this);
            ventVisualizer?.ClearRuntimeVisuals("geodesic prototype mode selected");
            planetGenerator?.GetComponent<PlanetTemperatureIceVisuals>()?.ClearForGeodesicMode();
            if (planetResourceMap != null && planetResourceMap.IsInitialized)
            {
                Debug.LogError("[StartupLifecycle] Geodesic Legacy-isolation invariant failed: PlanetResourceMap remained initialized.", this);
            }
        }

        if (replicatorManager != null)
        {
            replicatorManager.initialSpawnCount = Mathf.Max(0, config.initialSpawnCount);
            replicatorManager.geodesicBiologySpawnDelaySeconds = Mathf.Max(0f, config.geodesicBiologySpawnDelaySeconds);
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

            currentConfig = DeserializeSavedConfig(json, defaults);
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
        NormalizeAtmosphereComposition(config);
        config.atmosphereInventoryPerBar = Mathf.Max(1e-6f, config.atmosphereInventoryPerBar);
        config.airSeaExchangeHalfLifeSeconds = Mathf.Max(0f, config.airSeaExchangeHalfLifeSeconds);
        config.geodesicBiologySpawnDelaySeconds = Mathf.Max(0f, config.geodesicBiologySpawnDelaySeconds);
        config.initialDissolvedFe2Plus = Mathf.Clamp(config.initialDissolvedFe2Plus, InitialFe2Min, InitialFe2Max);
        config.ventH2PerTick = Mathf.Clamp(config.ventH2PerTick, VentPerTickMin, VentH2MaxPerTick);
        config.ventH2SPerTick = Mathf.Clamp(config.ventH2SPerTick, VentPerTickMin, VentH2SMaxPerTick);
        config.ventCO2PerTick = Mathf.Clamp(config.ventCO2PerTick, VentPerTickMin, VentCO2MaxPerTick);
        config.ventFe2PerTick = Mathf.Clamp(config.ventFe2PerTick, VentPerTickMin, VentCO2MaxPerTick);
        config.ventClustering = Mathf.Clamp01(config.ventClustering);
        config.terrestrialVentFraction = Mathf.Clamp01(config.terrestrialVentFraction);
        config.initialSpawnCount = Mathf.Clamp(config.initialSpawnCount, InitialSpawnMin, InitialSpawnMax);
        config.cubeSphereResolution = Mathf.Clamp(config.cubeSphereResolution, 3, 240);
        config.geodesicSubdivisionLevel = Mathf.Clamp(config.geodesicSubdivisionLevel, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        config.approximateThermalIntervalSeconds = NormalizeToPreset(config.approximateThermalIntervalSeconds, ApproximateThermalIntervalPresets, DefaultApproximateThermalIntervalSeconds);
        config.geodesicResourceTransportIntervalSeconds = NormalizeToPreset(config.geodesicResourceTransportIntervalSeconds, ResourceTransportIntervalPresets, DefaultResourceTransportIntervalSeconds);
        if (float.IsNaN(config.chemistryTelemetryIntervalSimSeconds) || float.IsInfinity(config.chemistryTelemetryIntervalSimSeconds)) config.chemistryTelemetryIntervalSimSeconds = DefaultChemistryTelemetryIntervalSimSeconds;
    }

    public static float NormalizeToPreset(float value, float[] presets, float fallback)
    {
        if (presets == null || presets.Length == 0 || float.IsNaN(value) || float.IsInfinity(value) || value <= 0f) return fallback;
        float nearest = presets[0];
        float nearestDistance = Mathf.Abs(value - nearest);
        for (int i = 1; i < presets.Length; i++)
        {
            float distance = Mathf.Abs(value - presets[i]);
            if (distance < nearestDistance) { nearest = presets[i]; nearestDistance = distance; }
        }
        return nearest;
    }

    /// <summary>Normalizes trace-gas fractions, assigns N2 the remainder, and derives partial pressures.</summary>
    public static void NormalizeAtmosphereComposition(SimulationStartupConfig config)
    {
        if (config == null) return;
        float pressureMax = config.allowDenseAtmosphere ? DenseAtmospherePressureMaxBar : NormalAtmospherePressureMaxBar;
        config.initialAtmospherePressureBar = Mathf.Clamp(FiniteOrZero(config.initialAtmospherePressureBar), 0f, pressureMax);
        config.atmosphericCO2Fraction = Mathf.Clamp01(FiniteOrZero(config.atmosphericCO2Fraction));
        config.atmosphericO2Fraction = Mathf.Clamp01(FiniteOrZero(config.atmosphericO2Fraction));
        config.atmosphericCH4Fraction = Mathf.Clamp01(FiniteOrZero(config.atmosphericCH4Fraction));
        config.atmosphericH2Fraction = Mathf.Clamp01(FiniteOrZero(config.atmosphericH2Fraction));
        config.atmosphericH2SFraction = Mathf.Clamp01(FiniteOrZero(config.atmosphericH2SFraction));
        float traceSum = config.atmosphericCO2Fraction + config.atmosphericO2Fraction + config.atmosphericCH4Fraction + config.atmosphericH2Fraction + config.atmosphericH2SFraction;
        if (traceSum > 1f)
        {
            float scale = 1f / traceSum;
            config.atmosphericCO2Fraction *= scale; config.atmosphericO2Fraction *= scale; config.atmosphericCH4Fraction *= scale;
            config.atmosphericH2Fraction *= scale; config.atmosphericH2SFraction *= scale;
            traceSum = 1f;
        }
        float pressure = config.initialAtmospherePressureBar;
        config.atmosphericCO2Bar = pressure * config.atmosphericCO2Fraction;
        config.atmosphericO2Bar = pressure * config.atmosphericO2Fraction;
        config.atmosphericCH4Bar = pressure * config.atmosphericCH4Fraction;
        config.atmosphericH2Bar = pressure * config.atmosphericH2Fraction;
        config.atmosphericH2SBar = pressure * config.atmosphericH2SFraction;
        config.atmosphericN2Bar = pressure * Mathf.Max(0f, 1f - traceSum);
    }

    private static float FiniteOrZero(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

    public static void MigrateLegacyAtmospherePartials(SimulationStartupConfig config, float n2Bar, float co2Bar, float o2Bar, float ch4Bar, float h2Bar, float h2sBar)
    {
        if (config == null) return;
        n2Bar = Mathf.Max(0f, FiniteOrZero(n2Bar)); co2Bar = Mathf.Max(0f, FiniteOrZero(co2Bar));
        o2Bar = Mathf.Max(0f, FiniteOrZero(o2Bar)); ch4Bar = Mathf.Max(0f, FiniteOrZero(ch4Bar));
        h2Bar = Mathf.Max(0f, FiniteOrZero(h2Bar)); h2sBar = Mathf.Max(0f, FiniteOrZero(h2sBar));
        float total = n2Bar + co2Bar + o2Bar + ch4Bar + h2Bar + h2sBar;
        config.initialAtmospherePressureBar = total;
        config.atmosphericCO2Fraction = total > 0f ? co2Bar / total : 0f;
        config.atmosphericO2Fraction = total > 0f ? o2Bar / total : 0f;
        config.atmosphericCH4Fraction = total > 0f ? ch4Bar / total : 0f;
        config.atmosphericH2Fraction = total > 0f ? h2Bar / total : 0f;
        config.atmosphericH2SFraction = total > 0f ? h2sBar / total : 0f;
        config.allowDenseAtmosphere = total > NormalAtmospherePressureMaxBar;
        NormalizeAtmosphereComposition(config);
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
        builder.AppendLine($"Planet Grid: {config.gridType}");
        builder.AppendLine($"Cube Sphere Resolution: {config.cubeSphereResolution}");
        builder.AppendLine($"Geodesic Subdivision Level: {config.geodesicSubdivisionLevel}");
        builder.AppendLine($"Approx Thermal Interval: {config.approximateThermalIntervalSeconds:0.###} s simulated time");
        builder.AppendLine($"Resource Transport Interval: {config.geodesicResourceTransportIntervalSeconds:0.###} s simulated time");
        builder.AppendLine($"Chemistry Telemetry Interval: {config.chemistryTelemetryIntervalSimSeconds:0.###} s simulated time (<=0 disabled)");
        builder.AppendLine($"Axis Tilt Degrees: {config.axisTiltDegrees:0.###}");
        builder.AppendLine($"Day Length Seconds: {config.dayLengthSeconds:0.###}");
        builder.AppendLine($"Year Length In Days: {config.yearLengthInDays:0.###}");
        builder.AppendLine($"Base Temp Kelvin: {config.baseTempKelvin:0.###}");
        builder.AppendLine($"Insolation Temp Gain: {config.insolationTempGain:0.###}");
        builder.AppendLine($"Initial CO2: {config.initialCO2:0.###}");
        builder.AppendLine($"Initial O2: {config.initialO2:0.###}");
        builder.AppendLine($"Initial CH4: {config.initialCH4:0.###}");
        float n2Fraction = config.initialAtmospherePressureBar > 0f ? config.atmosphericN2Bar / config.initialAtmospherePressureBar : 1f;
        string atmosphereAuthoringMode = config.allowDenseAtmosphere ? "Advanced dense (0-600 bar)" : "Normal (0-5 bar)";
        builder.AppendLine($"Atmosphere Authoring Mode: {atmosphereAuthoringMode}");
        builder.AppendLine($"Initial Atmosphere: {config.initialAtmospherePressureBar:0.###} bar total; composition N2/CO2/O2/CH4/H2/H2S={n2Fraction:P2}/{config.atmosphericCO2Fraction:P2}/{config.atmosphericO2Fraction:P2}/{config.atmosphericCH4Fraction:P2}/{config.atmosphericH2Fraction:P2}/{config.atmosphericH2SFraction:P2}");
        builder.AppendLine($"Atmosphere Partials (bar) N2/CO2/O2/CH4/H2/H2S={config.atmosphericN2Bar:0.######}/{config.atmosphericCO2Bar:0.######}/{config.atmosphericO2Bar:0.######}/{config.atmosphericCH4Bar:0.######}/{config.atmosphericH2Bar:0.######}/{config.atmosphericH2SBar:0.######}");
        builder.AppendLine($"Atmosphere Inventory / bar: {config.atmosphereInventoryPerBar:0.###} inventory units/bar");
        builder.AppendLine($"Air-Sea Exchange Half-Life: {config.airSeaExchangeHalfLifeSeconds:0.###} simulated s (0 disabled)");
        builder.AppendLine($"Geodesic Prebiotic Biology Delay: {config.geodesicBiologySpawnDelaySeconds:0.###} simulated s");
        builder.AppendLine($"Initial Dissolved Fe2+: {config.initialDissolvedFe2Plus:0.###}");
        builder.AppendLine($"Vent Clustering: {config.ventClustering:0.###}");
        builder.AppendLine($"Global Vent H2 / sim s: {config.ventH2PerTick:0.####}");
        builder.AppendLine($"Global Vent H2S / sim s: {config.ventH2SPerTick:0.####}");
        builder.AppendLine($"Global Vent CO2 / sim s: {config.ventCO2PerTick:0.####}");
        builder.AppendLine($"Global Vent Fe2 / sim s: {config.ventFe2PerTick:0.####}");
        builder.AppendLine($"Terrestrial Vent Fraction: {config.terrestrialVentFraction:0.###}");
        builder.AppendLine($"Initial Spawn Count: {config.initialSpawnCount}");
        builder.AppendLine($"Start Paused: {startPaused}");
        builder.AppendLine($"Saved Config Path: {SavedStartupConfigPath}");
        Debug.Log(builder.ToString());
    }

    public static SimulationStartupConfig DeserializeSavedConfig(string json, SimulationStartupConfig fallback)
    {
        SimulationStartupConfig authoritativeFallback = fallback ?? new SimulationStartupConfig();
        SavedStartupConfig saved = SavedStartupConfig.FromDefaults(authoritativeFallback);
        if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, saved);
        return saved.ToConfig(authoritativeFallback);
    }

    [Serializable]
    private class SavedStartupConfig
    {
        public int version = SavedStartupConfigVersion;
        public int planetSeed;
        public bool useRandomSeed;
        public PlanetGridType gridType;
        public int cubeSphereResolution;
        public int geodesicSubdivisionLevel;
        public float axisTiltDegrees;
        public float dayLengthSeconds;
        public float yearLengthInDays;
        public float baseTempKelvin;
        public float insolationTempGain;
        public float initialCO2;
        public float initialO2;
        public float initialCH4;
        public float atmosphericN2Bar, atmosphericCO2Bar, atmosphericO2Bar, atmosphericCH4Bar, atmosphericH2Bar, atmosphericH2SBar;
        public float initialAtmospherePressureBar, atmosphericCO2Fraction, atmosphericO2Fraction, atmosphericCH4Fraction, atmosphericH2Fraction, atmosphericH2SFraction;
        public bool allowDenseAtmosphere;
        public float atmosphereInventoryPerBar, airSeaExchangeHalfLifeSeconds, geodesicBiologySpawnDelaySeconds;
        public float initialDissolvedFe2Plus;
        public float ventH2PerTick;
        public float ventH2SPerTick;
        public float ventCO2PerTick;
        public float ventFe2PerTick;
        public float ventClustering;
        public float terrestrialVentFraction;
        public int initialSpawnCount;
        public bool startPaused;
        public float approximateThermalIntervalSeconds;
        public float geodesicResourceTransportIntervalSeconds;
        public float chemistryTelemetryIntervalSimSeconds;

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
                gridType = config.gridType,
                cubeSphereResolution = config.cubeSphereResolution,
                geodesicSubdivisionLevel = config.geodesicSubdivisionLevel,
                axisTiltDegrees = config.axisTiltDegrees,
                dayLengthSeconds = config.dayLengthSeconds,
                yearLengthInDays = config.yearLengthInDays,
                baseTempKelvin = config.baseTempKelvin,
                insolationTempGain = config.insolationTempGain,
                initialCO2 = config.initialCO2,
                initialO2 = config.initialO2,
                initialCH4 = config.initialCH4,
                atmosphericN2Bar = config.atmosphericN2Bar, atmosphericCO2Bar = config.atmosphericCO2Bar, atmosphericO2Bar = config.atmosphericO2Bar,
                atmosphericCH4Bar = config.atmosphericCH4Bar, atmosphericH2Bar = config.atmosphericH2Bar, atmosphericH2SBar = config.atmosphericH2SBar,
                initialAtmospherePressureBar = config.initialAtmospherePressureBar, atmosphericCO2Fraction = config.atmosphericCO2Fraction,
                atmosphericO2Fraction = config.atmosphericO2Fraction, atmosphericCH4Fraction = config.atmosphericCH4Fraction,
                atmosphericH2Fraction = config.atmosphericH2Fraction, atmosphericH2SFraction = config.atmosphericH2SFraction,
                allowDenseAtmosphere = config.allowDenseAtmosphere,
                atmosphereInventoryPerBar = config.atmosphereInventoryPerBar, airSeaExchangeHalfLifeSeconds = config.airSeaExchangeHalfLifeSeconds,
                geodesicBiologySpawnDelaySeconds = config.geodesicBiologySpawnDelaySeconds,
                initialDissolvedFe2Plus = config.initialDissolvedFe2Plus,
                ventH2PerTick = config.ventH2PerTick,
                ventH2SPerTick = config.ventH2SPerTick,
                ventCO2PerTick = config.ventCO2PerTick,
                ventFe2PerTick = config.ventFe2PerTick,
                ventClustering = config.ventClustering,
                terrestrialVentFraction = config.terrestrialVentFraction,
                initialSpawnCount = config.initialSpawnCount,
                startPaused = config.startPaused,
                approximateThermalIntervalSeconds = config.approximateThermalIntervalSeconds,
                geodesicResourceTransportIntervalSeconds = config.geodesicResourceTransportIntervalSeconds,
                chemistryTelemetryIntervalSimSeconds = config.chemistryTelemetryIntervalSimSeconds
            };
        }

        public SimulationStartupConfig ToConfig(SimulationStartupConfig fallback)
        {
            SimulationStartupConfig config = (fallback ?? new SimulationStartupConfig()).Clone();
            config.planetSeed = planetSeed;
            config.useRandomSeed = useRandomSeed;
            config.gridType = gridType;
            config.cubeSphereResolution = cubeSphereResolution > 0 ? cubeSphereResolution : config.cubeSphereResolution;
            config.geodesicSubdivisionLevel = geodesicSubdivisionLevel;
            config.axisTiltDegrees = axisTiltDegrees;
            config.dayLengthSeconds = dayLengthSeconds;
            config.yearLengthInDays = yearLengthInDays;
            config.baseTempKelvin = baseTempKelvin;
            config.insolationTempGain = insolationTempGain;
            config.initialCO2 = initialCO2;
            config.initialO2 = initialO2;
            config.initialCH4 = initialCH4;
            if (version >= 7)
            {
                config.initialAtmospherePressureBar = initialAtmospherePressureBar;
                config.atmosphericCO2Fraction = atmosphericCO2Fraction; config.atmosphericO2Fraction = atmosphericO2Fraction;
                config.atmosphericCH4Fraction = atmosphericCH4Fraction; config.atmosphericH2Fraction = atmosphericH2Fraction;
                config.atmosphericH2SFraction = atmosphericH2SFraction; config.allowDenseAtmosphere = allowDenseAtmosphere;
                config.atmosphereInventoryPerBar = atmosphereInventoryPerBar; config.airSeaExchangeHalfLifeSeconds = airSeaExchangeHalfLifeSeconds;
                config.geodesicBiologySpawnDelaySeconds = geodesicBiologySpawnDelaySeconds;
            }
            else if (version >= 6)
            {
                // Explicit schema migration: legacy values remain partial pressures and are converted,
                // never reinterpreted as composition fractions.
                MigrateLegacyAtmospherePartials(config, atmosphericN2Bar, atmosphericCO2Bar, atmosphericO2Bar, atmosphericCH4Bar, atmosphericH2Bar, atmosphericH2SBar);
                config.atmosphereInventoryPerBar = atmosphereInventoryPerBar; config.airSeaExchangeHalfLifeSeconds = airSeaExchangeHalfLifeSeconds;
                config.geodesicBiologySpawnDelaySeconds = geodesicBiologySpawnDelaySeconds;
            }
            config.initialDissolvedFe2Plus = initialDissolvedFe2Plus;
            config.ventH2PerTick = ventH2PerTick;
            config.ventH2SPerTick = ventH2SPerTick;
            config.ventCO2PerTick = ventCO2PerTick;
            config.ventFe2PerTick = ventFe2PerTick > 0f ? ventFe2PerTick : config.ventFe2PerTick;
            if (version >= 4) { config.ventClustering = ventClustering; config.terrestrialVentFraction = terrestrialVentFraction; }
            config.initialSpawnCount = initialSpawnCount;
            config.startPaused = startPaused;
            config.approximateThermalIntervalSeconds = approximateThermalIntervalSeconds;
            config.geodesicResourceTransportIntervalSeconds = geodesicResourceTransportIntervalSeconds;
            config.chemistryTelemetryIntervalSimSeconds = version >= 5 ? chemistryTelemetryIntervalSimSeconds : config.chemistryTelemetryIntervalSimSeconds;
            return config;
        }
    }

    private void ShowSetupScreen(bool show)
    {
        IsSetupActive = ((show || applyingConfig) && !startupComplete) || returningToMainMenu;

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
        if ((startupComplete && !returningToMainMenu) || applyingConfig || !useBuiltInSetupGui)
        {
            return;
        }

        EnsureStyles();
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, boxStyle);

        if (startupUiState == StartupUiState.SavePicker)
        {
            DrawBuiltInSavePicker();
        }
        else if (startupUiState == StartupUiState.ConfigScreen)
        {
            if (startupScreenRoot == null)
            {
                DrawBuiltInConfigScreen();
            }
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
        if (GUILayout.Button("New Game", buttonStyle, GUILayout.Height(42f))) ShowConfigScreenFromMainMenu();
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

    private void DrawBuiltInConfigScreen()
    {
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
        float advancedHeight = advancedSettingsExpanded ? 480f : 0f;
        float contentHeight = 44f + ((line + gap) * 25f) + advancedHeight + (gap * 2f) + 42f + 30f + 82f;
        Rect contentRect = GUILayoutUtility.GetRect(contentWidth, contentHeight, GUILayout.Width(contentWidth), GUILayout.Height(contentHeight));
        float controlX = contentRect.x;
        float y = contentRect.y;

        GUI.Label(new Rect(controlX, y, contentWidth, 34f), "Planet Simulation Setup", titleStyle);
        y += 44f;

        DrawBool(new Rect(controlX, y, contentWidth, line), "Use Random Seed", ref currentConfig.useRandomSeed);
        y += line + gap;
        DrawInt(new Rect(controlX, y, contentWidth, line), "Planet Seed", ref currentConfig.planetSeed, !currentConfig.useRandomSeed);
        y += line + gap;
        DrawPlanetGridPopup(new Rect(controlX, y, contentWidth, line));
        y += line + gap;
        if (currentConfig.gridType == PlanetGridType.LegacyCubeSphere)
        {
            DrawInt(new Rect(controlX, y, contentWidth, line), "Cube Sphere Resolution", ref currentConfig.cubeSphereResolution, true, 3, 240);
        }
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Axis Tilt (deg)", ref currentConfig.axisTiltDegrees, AxisTiltMinDegrees, AxisTiltMaxDegrees);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Day Length (sec)", ref currentConfig.dayLengthSeconds, DayLengthMinSeconds, DayLengthMaxSeconds);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Year Length (days)", ref currentConfig.yearLengthInDays, YearLengthMinDays, YearLengthMaxDays);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Insolation Gain", ref currentConfig.insolationTempGain, InsolationGainMin, InsolationGainMax);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Initial Ocean CO2", ref currentConfig.initialCO2, InitialAtmosphereMin, InitialCO2Max);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Initial Ocean O2", ref currentConfig.initialO2, InitialAtmosphereMin, InitialO2Max);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Initial Ocean CH4", ref currentConfig.initialCH4, InitialAtmosphereMin, InitialCH4Max);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Initial Ocean Fe2+", ref currentConfig.initialDissolvedFe2Plus, InitialFe2Min, InitialFe2Max);
        y += line + gap;
        float atmospherePressureMax = currentConfig.allowDenseAtmosphere ? DenseAtmospherePressureMaxBar : NormalAtmospherePressureMaxBar;
        string atmospherePressureLabel = currentConfig.allowDenseAtmosphere
            ? "Initial Atmosphere Pressure (bar; Advanced Dense 0-600)"
            : "Initial Atmosphere Pressure (bar; Normal 0-5)";
        DrawFloat(new Rect(controlX, y, contentWidth, line), atmospherePressureLabel, ref currentConfig.initialAtmospherePressureBar, 0f, atmospherePressureMax); y += line + gap;
        NormalizeAtmosphereComposition(currentConfig);
        DrawFloat(new Rect(controlX, y, contentWidth, line), $"CO2 fraction ({currentConfig.atmosphericCO2Bar:0.###} bar)", ref currentConfig.atmosphericCO2Fraction, 0f, 1f); y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), $"O2 fraction ({currentConfig.atmosphericO2Bar:0.###} bar)", ref currentConfig.atmosphericO2Fraction, 0f, 1f); y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), $"CH4 fraction ({currentConfig.atmosphericCH4Bar:0.###} bar)", ref currentConfig.atmosphericCH4Fraction, 0f, 1f); y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), $"H2 fraction ({currentConfig.atmosphericH2Bar:0.###} bar)", ref currentConfig.atmosphericH2Fraction, 0f, 1f); y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), $"H2S fraction ({currentConfig.atmosphericH2SBar:0.###} bar)", ref currentConfig.atmosphericH2SFraction, 0f, 1f); y += line + gap;
        NormalizeAtmosphereComposition(currentConfig);
        GUI.Label(new Rect(controlX, y, contentWidth, line), $"N2 remainder: {(currentConfig.initialAtmospherePressureBar > 0f ? currentConfig.atmosphericN2Bar / currentConfig.initialAtmospherePressureBar : 1f):P2} ({currentConfig.atmosphericN2Bar:0.###} bar)", labelStyle); y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Vent Clustering", ref currentConfig.ventClustering, 0f, 1f);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Global Vent H2 / sim s", ref currentConfig.ventH2PerTick, VentPerTickMin, VentH2MaxPerTick);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Global Vent H2S / sim s", ref currentConfig.ventH2SPerTick, VentPerTickMin, VentH2SMaxPerTick);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Global Vent CO2 / sim s", ref currentConfig.ventCO2PerTick, VentPerTickMin, VentCO2MaxPerTick);
        y += line + gap;
        DrawFloat(new Rect(controlX, y, contentWidth, line), "Global Vent Fe2 / sim s", ref currentConfig.ventFe2PerTick, VentPerTickMin, VentCO2MaxPerTick);
        y += line + gap;
        DrawInt(new Rect(controlX, y, contentWidth, line), "Initial Spawn Count", ref currentConfig.initialSpawnCount, true, InitialSpawnMin, InitialSpawnMax);
        y += line + (gap * 2f);

        if (GUI.Button(new Rect(controlX, y, contentWidth, 32f), advancedSettingsExpanded ? "Advanced ▲" : "Advanced ▼", buttonStyle))
        {
            advancedSettingsExpanded = !advancedSettingsExpanded;
        }
        y += 40f;
        if (advancedSettingsExpanded)
        {
            if (currentConfig.gridType == PlanetGridType.GeodesicIcosphere)
            {
                DrawInt(new Rect(controlX, y, contentWidth, line), "Geodesic Subdivision Level", ref currentConfig.geodesicSubdivisionLevel, true, 0, GeodesicGridTopology.MaxSupportedSubdivision);
                y += line + gap;
            }
            DrawFloat(new Rect(controlX, y, contentWidth, line), "Base Temperature (K)", ref currentConfig.baseTempKelvin, BaseTempMinKelvin, BaseTempMaxKelvin);
            y += line + gap;
            DrawFloat(new Rect(controlX, y, contentWidth, line), "Terrestrial Vent Fraction", ref currentConfig.terrestrialVentFraction, 0f, 1f);
            y += line + gap;
            DrawBool(new Rect(controlX, y, contentWidth, line), "Allow Dense Atmosphere (up to 600 bar)", ref currentConfig.allowDenseAtmosphere);
            y += line + gap;
            DrawFloat(new Rect(controlX, y, contentWidth, line), "Atmosphere Inventory / bar", ref currentConfig.atmosphereInventoryPerBar, 0.000001f, 1000000f); y += line + gap;
            DrawFloat(new Rect(controlX, y, contentWidth, line), "Air-Sea L0 Relaxation Half-Life (sim s; 0 off)", ref currentConfig.airSeaExchangeHalfLifeSeconds, 0f, 1000000f); y += line + gap;
            DrawFloat(new Rect(controlX, y, contentWidth, line), "Prebiotic Biology Delay (sim s)", ref currentConfig.geodesicBiologySpawnDelaySeconds, 0f, 5000f); y += line + gap;
            GUI.Label(new Rect(controlX, y, contentWidth, 38f), "Relaxation of surface-ocean L0 concentration toward atmosphere-controlled equilibrium; not finite-atmosphere depletion half-life.", labelStyle);
            y += 42f;
            GUI.Label(new Rect(controlX, y, contentWidth, 24f), "Environment Timing", labelStyle);
            y += 26f;
            DrawPreset(new Rect(controlX, y, contentWidth, line), "Temperature update interval", ref currentConfig.approximateThermalIntervalSeconds, ApproximateThermalIntervalPresets);
            y += line;
            GUI.Label(new Rect(controlX, y, contentWidth, 22f), "Simulated time. Lower updates more often but costs more CPU. Recommended: 2 s.", labelStyle);
            y += 28f;
            DrawPreset(new Rect(controlX, y, contentWidth, line), "Resource transport interval", ref currentConfig.geodesicResourceTransportIntervalSeconds, ResourceTransportIntervalPresets);
            y += line;
            GUI.Label(new Rect(controlX, y, contentWidth, 22f), "Simulated time. Lower gives finer transport but costs more CPU. Recommended: 5 s.", labelStyle);
            y += 30f;
            DrawFloat(new Rect(controlX, y, contentWidth, line), "Chemistry telemetry interval", ref currentConfig.chemistryTelemetryIntervalSimSeconds, -1f, 3600f);
            y += line;
            GUI.Label(new Rect(controlX, y, contentWidth, 22f), "Authoritative simulated time. Recommended: 60 s; <= 0 disables.", labelStyle);
            y += 30f;
            if (GUI.Button(new Rect(controlX, y, contentWidth, 30f), "Reset Advanced to Defaults", buttonStyle)) ResetAdvancedToDefaults();
            y += 38f;
        }

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
        y += 38f;
        if (GUI.Button(new Rect(controlX, y, contentWidth, 30f), "Back", buttonStyle))
        {
            BackToStartMenu();
        }

        GUILayout.EndScrollView();
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
            string row = $"{save.FileName}  •  {save.GridDescription}  •  {FormatBytes(save.SizeBytes)}  •  {save.LastWriteTimeLocal:yyyy-MM-dd HH:mm:ss}";
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

    private void DrawPlanetGridPopup(Rect rect)
    {
        GUI.Label(new Rect(rect.x, rect.y, rect.width * 0.42f, rect.height), "Planet Grid", labelStyle);
        string[] labels = { "Cube Sphere (Legacy)", "Geodesic Icosphere" };
        int selected = currentConfig.gridType == PlanetGridType.GeodesicIcosphere ? 1 : 0;
        selected = GUI.SelectionGrid(new Rect(rect.x + rect.width * 0.44f, rect.y, rect.width * 0.56f, rect.height), selected, labels, 2);
        currentConfig.gridType = selected == 1 ? PlanetGridType.GeodesicIcosphere : PlanetGridType.LegacyCubeSphere;
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

    private void DrawPreset(Rect rect, string label, ref float value, float[] presets)
    {
        value = NormalizeToPreset(value, presets, presets[0]);
        GUI.Label(new Rect(rect.x, rect.y, rect.width * 0.42f, rect.height), $"{label}: {value:0.#} s simulated", labelStyle);
        string[] choices = new string[presets.Length];
        int selected = 0;
        for (int i = 0; i < presets.Length; i++)
        {
            choices[i] = $"{presets[i]:0.#} s";
            if (Mathf.Approximately(value, presets[i])) selected = i;
        }
        selected = GUI.SelectionGrid(new Rect(rect.x + rect.width * 0.44f, rect.y, rect.width * 0.56f, rect.height), selected, choices, presets.Length);
        value = presets[selected];
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
