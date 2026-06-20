using System;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SimulationSaveLoadService : MonoBehaviour
{
    [Header("Capture References")]
    [SerializeField] private ReplicatorManager replicatorManager;
    [SerializeField] private ReplicatorSimulationPipeline simulationPipeline;
    [SerializeField] private SunSkyRotator sunSkyRotator;
    [SerializeField] private PlanetResourceMap planetResourceMap;
    [SerializeField] private PlanetGenerator planetGenerator;

    [Header("Debug Save")]
    [SerializeField] private bool enableKeyboardQuickSave = true;
    [SerializeField] private bool useTimestampedFileNames = true;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        // Temporary debug hotkey for snapshot validation; a real UI Save button will be added later.
        if (enableKeyboardQuickSave && Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
        {
            SaveSnapshot();
        }
#endif
    }

    [ContextMenu("Debug Save Simulation Snapshot")]
    public void SaveSnapshotFromContextMenu()
    {
        SaveSnapshot();
    }

    public string SaveSnapshot()
    {
        ResolveReferences();

        SimulationSaveFile saveFile = BuildSaveFile();
        string saveDirectory = Path.Combine(Application.persistentDataPath, "Saves");
        Directory.CreateDirectory(saveDirectory);

        string fileName = useTimestampedFileNames
            ? $"simv-{DateTime.UtcNow:yyyyMMdd-HHmmss}.simv.json"
            : "quicksave.simv.json";
        string fullPath = Path.Combine(saveDirectory, fileName);

        string json = JsonUtility.ToJson(saveFile, true);
        File.WriteAllText(fullPath, json);

        Debug.Log(BuildDiagnosticLog(fullPath, saveFile), this);
        return fullPath;
    }

    private SimulationSaveFile BuildSaveFile()
    {
        PlanetResourceMapSnapshot resourceSnapshot = planetResourceMap != null
            ? planetResourceMap.CaptureSnapshotSummary()
            : new PlanetResourceMapSnapshot { available = false };

        ReplicatorPopulationSnapshot populationSnapshot = replicatorManager != null
            ? replicatorManager.CapturePopulationSnapshot()
            : new ReplicatorPopulationSnapshot();

        SimulationClockSnapshot clockSnapshot = simulationPipeline != null
            ? simulationPipeline.CaptureClockSnapshot()
            : replicatorManager != null ? replicatorManager.CaptureClockSnapshot() : new SimulationClockSnapshot();

        SimulationSaveDiagnostics diagnostics = new SimulationSaveDiagnostics
        {
            replicatorCount = populationSnapshot != null ? populationSnapshot.count : 0,
            resourceCellCount = resourceSnapshot != null ? resourceSnapshot.cellCount : 0,
            layeredOceanEnabled = resourceSnapshot != null && resourceSnapshot.layeredOceanEnabled,
            o2Sum = resourceSnapshot != null && resourceSnapshot.resourceSums != null ? resourceSnapshot.resourceSums.o2 : 0d,
            co2Sum = resourceSnapshot != null && resourceSnapshot.resourceSums != null ? resourceSnapshot.resourceSums.co2 : 0d,
            organicCSum = resourceSnapshot != null && resourceSnapshot.resourceSums != null ? resourceSnapshot.resourceSums.organicC : 0d,
            temperatureMinKelvin = resourceSnapshot != null && resourceSnapshot.temperature != null ? resourceSnapshot.temperature.minKelvin : 0f,
            temperatureMaxKelvin = resourceSnapshot != null && resourceSnapshot.temperature != null ? resourceSnapshot.temperature.maxKelvin : 0f,
            temperatureMeanKelvin = resourceSnapshot != null && resourceSnapshot.temperature != null ? resourceSnapshot.temperature.meanKelvin : 0f
        };

        return new SimulationSaveFile
        {
            schemaVersion = SimulationSaveFile.CurrentSchemaVersion,
            applicationVersion = Application.version,
            unityVersion = Application.unityVersion,
            savedUtc = DateTime.UtcNow.ToString("o"),
            clock = clockSnapshot,
            sun = sunSkyRotator != null ? sunSkyRotator.CaptureSnapshot() : new SunSkySnapshot { available = false },
            planetGenerator = CapturePlanetGeneratorSnapshot(),
            resourceMap = resourceSnapshot,
            population = populationSnapshot,
            diagnostics = diagnostics
        };
    }

    private PlanetGeneratorSnapshot CapturePlanetGeneratorSnapshot()
    {
        if (planetGenerator == null)
        {
            return new PlanetGeneratorSnapshot { available = false };
        }

        return new PlanetGeneratorSnapshot
        {
            available = true,
            resolution = planetGenerator.resolution,
            radius = planetGenerator.radius,
            seaLevel = 0f // TODO: capture PlanetGenerator sea-level once the generator exposes an authoritative field.
        };
    }

    private string BuildDiagnosticLog(string path, SimulationSaveFile saveFile)
    {
        TemperatureSummarySnapshot temp = saveFile.resourceMap != null ? saveFile.resourceMap.temperature : null;
        ResourceSumsSnapshot sums = saveFile.resourceMap != null ? saveFile.resourceMap.resourceSums : null;
        return "Saved simulation snapshot:\n" +
            $"Path: {path}\n" +
            $"Schema: {saveFile.schemaVersion}\n" +
            $"Simulation time: {(saveFile.clock != null ? saveFile.clock.simulationTimeSeconds : 0d):F3}\n" +
            $"Step count: {(saveFile.clock != null ? saveFile.clock.simulationStepCount : 0)}\n" +
            $"Replicators: {(saveFile.population != null ? saveFile.population.count : 0)}\n" +
            $"Resource cells: {(saveFile.resourceMap != null ? saveFile.resourceMap.cellCount : 0)}\n" +
            $"Layered ocean: {saveFile.resourceMap != null && saveFile.resourceMap.layeredOceanEnabled}\n" +
            $"O2 sum: {(sums != null ? sums.o2 : 0d):F6}\n" +
            $"CO2 sum: {(sums != null ? sums.co2 : 0d):F6}\n" +
            $"OrganicC sum: {(sums != null ? sums.organicC : 0d):F6}\n" +
            $"Temperature min/max/mean: {(temp != null ? temp.minKelvin : 0f):F2}/{(temp != null ? temp.maxKelvin : 0f):F2}/{(temp != null ? temp.meanKelvin : 0f):F2}";
    }

    private void ResolveReferences()
    {
        if (replicatorManager == null) replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        if (simulationPipeline == null) simulationPipeline = FindFirstObjectByType<ReplicatorSimulationPipeline>();
        if (sunSkyRotator == null) sunSkyRotator = FindFirstObjectByType<SunSkyRotator>();
        if (planetResourceMap == null) planetResourceMap = FindFirstObjectByType<PlanetResourceMap>();
        if (planetGenerator == null) planetGenerator = FindFirstObjectByType<PlanetGenerator>();
    }
}
