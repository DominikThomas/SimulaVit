using System;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SimulationSaveLoadService : MonoBehaviour
{
    public const string CompressedSaveSearchPattern = "*.simv.json.gz";

    public readonly struct SaveFileInfo
    {
        public SaveFileInfo(string path, string fileName, DateTime lastWriteTimeLocal, DateTime lastWriteTimeUtc, long sizeBytes)
        {
            Path = path;
            FileName = fileName;
            LastWriteTimeLocal = lastWriteTimeLocal;
            LastWriteTimeUtc = lastWriteTimeUtc;
            SizeBytes = sizeBytes;
        }

        public string Path { get; }
        public string FileName { get; }
        public DateTime LastWriteTimeLocal { get; }
        public DateTime LastWriteTimeUtc { get; }
        public long SizeBytes { get; }
    }

    [Header("Capture References")]
    [SerializeField] private ReplicatorManager replicatorManager;
    [SerializeField] private ReplicatorSimulationPipeline simulationPipeline;
    [SerializeField] private SunSkyRotator sunSkyRotator;
    [SerializeField] private PlanetResourceMap planetResourceMap;
    [SerializeField] private PlanetGenerator planetGenerator;
    [SerializeField] private SimulationSpeedController simulationSpeedController;
    [SerializeField] private PlanetCellInspectorController planetCellInspectorController;

    [Header("Debug Save")]
    [SerializeField] private bool enableKeyboardQuickSave = true;
    [SerializeField] private bool enableKeyboardQuickLoad = true;
    [SerializeField] private bool useTimestampedFileNames = true;
    [Tooltip("Optional compatibility/debug export for old milestone-1 uncompressed .simv.json saves. The default save format is compressed JSON (.simv.json.gz).")]
    [SerializeField] private bool alsoWriteUncompressedDebugJson = false;
    [SerializeField] private bool prettyPrintUncompressedDebugJson = true;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        // Temporary debug hotkey for snapshot validation; a real UI Save button will be added later.
        if (Keyboard.current != null)
        {
            if (enableKeyboardQuickSave && Keyboard.current.f5Key.wasPressedThisFrame)
            {
                SaveSnapshot();
            }

            if (enableKeyboardQuickLoad && Keyboard.current.f9Key.wasPressedThisFrame)
            {
                LoadLatestDebugSnapshot();
            }
        }
#endif
    }

    [ContextMenu("Debug Save Simulation Snapshot")]
    public void SaveSnapshotFromContextMenu()
    {
        SaveSnapshot();
    }

    public string SaveLatestManualSnapshot()
    {
        return SaveSnapshot();
    }

    public void LoadLatestManualSnapshot()
    {
        LoadLatestDebugSnapshot();
    }

    public string SavesDirectory => Path.Combine(Application.persistentDataPath, "Saves");

    [ContextMenu("Debug Load Latest Simulation Snapshot")]
    public void LoadLatestDebugSnapshot()
    {
        LoadLatestSave();
    }

    public bool LoadLatestSave()
    {
        ResolveReferences();
        string path = FindLatestCompressedSavePath();
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning($"No compressed simulation save files found in {SavesDirectory}. Expected {CompressedSaveSearchPattern}.", this);
            return false;
        }

        return LoadSnapshotFromPath(path);
    }

    public IReadOnlyList<SaveFileInfo> ListSaveFiles()
    {
        string saveDirectory = SavesDirectory;
        if (!Directory.Exists(saveDirectory))
        {
            return Array.Empty<SaveFileInfo>();
        }

        return Directory.GetFiles(saveDirectory, CompressedSaveSearchPattern)
            .Where(file => !file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Select(file => new FileInfo(file))
            .Where(info => info.Exists)
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => new SaveFileInfo(info.FullName, info.Name, info.LastWriteTime, info.LastWriteTimeUtc, info.Length))
            .ToArray();
    }

    public bool LoadSnapshotFromPath(string path)
    {
        return LoadSnapshot(path);
    }

    public bool LoadSnapshot(string path)
    {
        ResolveReferences();
        SimulationSaveFile saveFile;
        try
        {
            saveFile = ReadCompressedJson(path);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to read simulation snapshot '{path}': {ex.Message}", this);
            return false;
        }

        if (!ValidateSaveFile(saveFile, out string validationError))
        {
            Debug.LogError($"Simulation snapshot load aborted: {validationError}", this);
            return false;
        }

        if (replicatorManager == null)
        {
            Debug.LogError("Simulation snapshot load aborted: no ReplicatorManager is available to receive the population snapshot.", this);
            return false;
        }

        bool pipelineWasEnabled = simulationPipeline != null && simulationPipeline.enabled;
        if (simulationPipeline != null)
        {
            simulationPipeline.enabled = false;
        }

        bool sunRestored = false;
        bool dissolvedFe2Restored = false;
        try
        {
            if (simulationPipeline != null)
            {
                simulationPipeline.ApplyClockSnapshot(saveFile.clock);
            }
            else if (replicatorManager != null)
            {
                replicatorManager.ApplyClockSnapshot(saveFile.clock);
            }

            sunRestored = sunSkyRotator != null && sunSkyRotator.ApplySnapshot(saveFile.sun, saveFile.clock);

            if (planetResourceMap != null)
            {
                dissolvedFe2Restored = planetResourceMap.ApplyDissolvedFe2Snapshot(saveFile.resourceMap);
            }
            else if (saveFile.resourceMap != null && saveFile.resourceMap.dissolvedFe2Plus != null && saveFile.resourceMap.dissolvedFe2Plus.Length > 0)
            {
                Debug.LogWarning("Dissolved Fe2+ restore skipped: no PlanetResourceMap is available to receive the snapshot.", this);
            }
            else
            {
                Debug.LogWarning("Dissolved Fe2+ not present in save; keeping generated/default values.", this);
            }

            if (!replicatorManager.ApplyPopulationSnapshot(saveFile.population))
            {
                return false;
            }

            ResetSimulationSpeedAfterLoad();
        }
        finally
        {
            if (simulationPipeline != null)
            {
                simulationPipeline.enabled = pipelineWasEnabled;
            }
        }

        RefreshUiAfterLoad();
        Debug.Log(BuildLoadDiagnosticLog(path, saveFile, sunRestored, dissolvedFe2Restored), this);
        return true;
    }

    private void ResetSimulationSpeedAfterLoad()
    {
        const int defaultStepsPerFrame = 1;

        if (replicatorManager != null)
        {
            replicatorManager.SetSimulationTiming(defaultStepsPerFrame);
            return;
        }

        if (simulationPipeline != null)
        {
            simulationPipeline.SetSimulationStepsPerFrame(defaultStepsPerFrame);
        }
    }

    private void RefreshUiAfterLoad()
    {
        simulationSpeedController ??= FindFirstObjectByType<SimulationSpeedController>();
        simulationSpeedController?.RefreshFromSimulationTiming();

        planetCellInspectorController ??= FindFirstObjectByType<PlanetCellInspectorController>();
        planetCellInspectorController?.ClearSelection();
    }

    public string SaveSnapshot()
    {
        ResolveReferences();

        SimulationSaveFile saveFile = BuildSaveFile();
        string saveDirectory = SavesDirectory;
        Directory.CreateDirectory(saveDirectory);

        string fileName = useTimestampedFileNames
            ? $"simv-{DateTime.Now:yyyyMMdd-HHmmss}.simv.json.gz"
            : "quicksave.simv.json.gz";
        string fullPath = Path.Combine(saveDirectory, fileName);

        // Compatibility note: old .simv.json debug saves were uncompressed milestone-1 saves.
        // The default save format is now compressed JSON (.simv.json.gz) with the same DTO structure.
        string json = JsonUtility.ToJson(saveFile, false);
        long uncompressedJsonBytes = Encoding.UTF8.GetByteCount(json);
        WriteCompressedJsonAtomic(fullPath, json);

        if (alsoWriteUncompressedDebugJson)
        {
            string debugJsonPath = Path.Combine(saveDirectory, Path.GetFileNameWithoutExtension(fullPath));
            string debugJson = prettyPrintUncompressedDebugJson ? JsonUtility.ToJson(saveFile, true) : json;
            WriteTextAtomic(debugJsonPath, debugJson);
        }

        FileInfo compressedFile = new FileInfo(fullPath);
        Debug.Log(BuildDiagnosticLog(fullPath, compressedFile.Length, uncompressedJsonBytes, saveFile), this);
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

    private static SimulationSaveFile ReadCompressedJson(string path)
    {
        using (FileStream fileStream = File.OpenRead(path))
        using (GZipStream gzipStream = new GZipStream(fileStream, System.IO.Compression.CompressionMode.Decompress))
        using (StreamReader reader = new StreamReader(gzipStream, Encoding.UTF8))
        {
            string json = reader.ReadToEnd();
            return JsonUtility.FromJson<SimulationSaveFile>(json);
        }
    }

    private string FindLatestCompressedSavePath()
    {
        IReadOnlyList<SaveFileInfo> saves = ListSaveFiles();
        return saves.Count > 0 ? saves[0].Path : null;
    }

    private static bool ValidateSaveFile(SimulationSaveFile saveFile, out string error)
    {
        if (saveFile == null)
        {
            error = "save file JSON could not be deserialized.";
            return false;
        }

        if (saveFile.schemaVersion < 1 || saveFile.schemaVersion > SimulationSaveFile.CurrentSchemaVersion)
        {
            error = $"unsupported schema version {saveFile.schemaVersion}.";
            return false;
        }

        if (saveFile.population == null || saveFile.population.replicators == null)
        {
            error = "population snapshot is missing.";
            return false;
        }

        if (saveFile.population.count != saveFile.population.replicators.Count)
        {
            error = $"population count mismatch: count={saveFile.population.count}, records={saveFile.population.replicators.Count}.";
            return false;
        }

        if (saveFile.clock == null)
        {
            saveFile.clock = new SimulationClockSnapshot();
            Debug.LogWarning("Simulation snapshot has no clock; using zero-time fallback.");
        }

        error = null;
        return true;
    }

    private string BuildLoadDiagnosticLog(string path, SimulationSaveFile saveFile, bool sunRestored, bool dissolvedFe2Restored)
    {
        ResourceArrayDiagnosticsSnapshot fe2 = planetResourceMap != null
            ? planetResourceMap.CaptureDissolvedFe2PlusDiagnostics()
            : saveFile.resourceMap != null ? saveFile.resourceMap.dissolvedFe2PlusDiagnostics : null;

        return "Loaded simulation snapshot:\n" +
            $"Path: {path}\n" +
            $"Schema: {saveFile.schemaVersion}\n" +
            $"Saved UTC: {saveFile.savedUtc}\n" +
            $"Simulation time: {(saveFile.clock != null ? saveFile.clock.simulationTimeSeconds : 0d):F3}\n" +
            $"Step count: {(saveFile.clock != null ? saveFile.clock.simulationStepCount : 0)}\n" +
            $"Replicators loaded: {(saveFile.population != null ? saveFile.population.count : 0)}\n" +
            $"Sun phase restored: {sunRestored}\n" +
            $"Resource map restored: {dissolvedFe2Restored} (Dissolved Fe2+ only)\n" +
            $"Dissolved Fe2+ restored: {dissolvedFe2Restored}\n" +
            $"Dissolved Fe2+ length: {(fe2 != null ? fe2.arrayLength : 0)}\n" +
            $"Dissolved Fe2+ sum/min/max after apply: {(fe2 != null ? fe2.sum : 0d):F6}/{(fe2 != null ? fe2.min : 0f):F6}/{(fe2 != null ? fe2.max : 0f):F6}";
    }

    private static void WriteCompressedJsonAtomic(string path, string json)
    {
        string tempPath = path + ".tmp";

        try
        {
            using (FileStream fileStream = File.Create(tempPath))
            using (GZipStream gzipStream = new GZipStream(fileStream, System.IO.Compression.CompressionLevel.Optimal))
            using (StreamWriter writer = new StreamWriter(gzipStream, Encoding.UTF8))
            {
                writer.Write(json);
            }

            ReplaceFile(tempPath, path);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static void WriteTextAtomic(string path, string text)
    {
        string tempPath = path + ".tmp";

        try
        {
            File.WriteAllText(tempPath, text, Encoding.UTF8);
            ReplaceFile(tempPath, path);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static void ReplaceFile(string tempPath, string finalPath)
    {
        if (File.Exists(finalPath))
        {
            File.Replace(tempPath, finalPath, null);
        }
        else
        {
            File.Move(tempPath, finalPath);
        }
    }

    private string BuildDiagnosticLog(string path, long compressedBytes, long uncompressedJsonBytes, SimulationSaveFile saveFile)
    {
        TemperatureSummarySnapshot temp = saveFile.resourceMap != null ? saveFile.resourceMap.temperature : null;
        ResourceSumsSnapshot sums = saveFile.resourceMap != null ? saveFile.resourceMap.resourceSums : null;
        ResourceArrayDiagnosticsSnapshot fe2 = saveFile.resourceMap != null ? saveFile.resourceMap.dissolvedFe2PlusDiagnostics : null;
        return "Saved simulation snapshot:\n" +
            $"Path: {path}\n" +
            $"Compressed size: {BytesToMegabytes(compressedBytes):F3} MB\n" +
            $"Uncompressed JSON size estimate: {BytesToMegabytes(uncompressedJsonBytes):F3} MB\n" +
            $"Replicators: {(saveFile.population != null ? saveFile.population.count : 0)}\n" +
            $"Schema: {saveFile.schemaVersion}\n" +
            $"Simulation time: {(saveFile.clock != null ? saveFile.clock.simulationTimeSeconds : 0d):F3}\n" +
            $"Step count: {(saveFile.clock != null ? saveFile.clock.simulationStepCount : 0)}\n" +
            $"Resource cells: {(saveFile.resourceMap != null ? saveFile.resourceMap.cellCount : 0)}\n" +
            $"Layered ocean: {saveFile.resourceMap != null && saveFile.resourceMap.layeredOceanEnabled}\n" +
            $"O2 sum: {(sums != null ? sums.o2 : 0d):F6}\n" +
            $"CO2 sum: {(sums != null ? sums.co2 : 0d):F6}\n" +
            $"OrganicC sum: {(sums != null ? sums.organicC : 0d):F6}\n" +
            $"Dissolved Fe2+ saved: {fe2 != null && fe2.present}\n" +
            $"Dissolved Fe2+ length: {(fe2 != null ? fe2.arrayLength : 0)}\n" +
            $"Dissolved Fe2+ sum/min/max: {(fe2 != null ? fe2.sum : 0d):F6}/{(fe2 != null ? fe2.min : 0f):F6}/{(fe2 != null ? fe2.max : 0f):F6}\n" +
            $"Temperature min/max/mean: {(temp != null ? temp.minKelvin : 0f):F2}/{(temp != null ? temp.maxKelvin : 0f):F2}/{(temp != null ? temp.meanKelvin : 0f):F2}";
    }

    private static double BytesToMegabytes(long bytes)
    {
        return bytes / (1024d * 1024d);
    }

    private void ResolveReferences()
    {
        if (replicatorManager == null) replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        if (simulationPipeline == null) simulationPipeline = FindFirstObjectByType<ReplicatorSimulationPipeline>();
        if (sunSkyRotator == null) sunSkyRotator = FindFirstObjectByType<SunSkyRotator>();
        if (planetResourceMap == null) planetResourceMap = FindFirstObjectByType<PlanetResourceMap>();
        if (planetGenerator == null) planetGenerator = FindFirstObjectByType<PlanetGenerator>();
        if (simulationSpeedController == null) simulationSpeedController = FindFirstObjectByType<SimulationSpeedController>();
        if (planetCellInspectorController == null) planetCellInspectorController = FindFirstObjectByType<PlanetCellInspectorController>();
    }
}
