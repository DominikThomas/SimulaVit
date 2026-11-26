using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

internal sealed class SimulationStartupProfiles
{
    internal const string FolderName = "StartupProfiles";
    private const string FileSuffix = ".profile.json";
    internal const int MaxNameLength = 100;
    private const int ProfileVersion = 1;
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private readonly string directory;

    [Serializable]
    private sealed class ProfileFile
    {
        public int profileVersion;
        public string configurationJson;
        public bool includesExperimentSettings;
        public ExperimentSettings experimentSettings;
    }

    [Serializable]
    internal struct ExperimentSettings
    {
        public bool detailedGeodesicProfiling;
        public string lifecycleThermalSamplingMode;

        public ExperimentSettings(bool detailed, LifecycleThermalSamplingMode thermalMode)
        {
            detailedGeodesicProfiling = detailed;
            lifecycleThermalSamplingMode = thermalMode.ToString();
        }

        public LifecycleThermalSamplingMode ThermalMode =>
            lifecycleThermalSamplingMode == nameof(LifecycleThermalSamplingMode.Optimized)
                ? LifecycleThermalSamplingMode.Optimized : LifecycleThermalSamplingMode.Reference;
    }

    public SimulationStartupProfiles(string persistentDataPath)
    {
        directory = Path.GetFullPath(Path.Combine(persistentDataPath, FolderName));
    }

    public string OpenDirectory()
    {
        Directory.CreateDirectory(directory);
        RequireRegularPath(directory);
        return directory;
    }

    public bool OwnsPath(string path) =>
        Path.GetFullPath(path).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public string[] List()
    {
        var names = new List<string>();
        foreach (string path in Directory.EnumerateFiles(OpenDirectory()))
        {
            string file = Path.GetFileName(path);
            if (file.EndsWith(FileSuffix, StringComparison.Ordinal) &&
                IsValidName(file.Substring(0, file.Length - FileSuffix.Length)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                names.Add(file.Substring(0, file.Length - FileSuffix.Length));
        }
        names.Sort(NameComparer);
        return names.ToArray();
    }

    public void Create(string name, SimulationStartupConfig config, ExperimentSettings? experimentSettings)
    {
        string path = GetAvailablePath(name);
        WriteNewJson(path, Serialize(config, experimentSettings));
    }

    public void Overwrite(string name, SimulationStartupConfig config, ExperimentSettings? experimentSettings)
    {
        string path = GetExistingPath(name);
        Read(path, null, out _);
        WriteJson(path, Serialize(config, experimentSettings), replace: true);
    }

    public SimulationStartupConfig Load(string name, SimulationStartupConfig fallback, out ExperimentSettings? experimentSettings) =>
        Read(GetExistingPath(name), fallback, out experimentSettings);

    public void Rename(string name, string newName)
    {
        string source = GetExistingPath(name);
        string destination = GetAvailablePath(newName);
        File.Move(source, destination);
    }

    public void Delete(string name) => File.Delete(GetExistingPath(name));

    private string GetExistingPath(string name)
    {
        string path = GetPath(name);
        if (!File.Exists(path)) throw new FileNotFoundException("Profile is missing or inaccessible. Refresh the list.");
        RequireRegularPath(path);
        return path;
    }

    private string GetAvailablePath(string name)
    {
        string path = GetPath(name);
        string fileName = Path.GetFileName(path).Normalize();
        foreach (string existing in Directory.EnumerateFileSystemEntries(directory))
        {
            if (NameComparer.Equals(Path.GetFileName(existing).Normalize(), fileName))
                throw new IOException("A profile with that name already exists.");
        }
        return path;
    }

    private string GetPath(string name)
    {
        if (!IsValidName(name))
            throw new ArgumentException("Enter a valid profile name without reserved names, slashes or trailing dots.");
        return Path.Combine(OpenDirectory(), name + FileSuffix);
    }

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength || name != name.Trim() ||
            name[0] == '.' || name.EndsWith(".", StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(name) > 200 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(InvalidNameCharacters) >= 0)
            return false;
        foreach (char character in name)
            if (char.IsControl(character)) return false;
        try { name.Normalize(); }
        catch (ArgumentException) { return false; }

        string baseName = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        return baseName != "STARTUP_CONFIG" && baseName != "CON" && baseName != "PRN" && baseName != "AUX" &&
            baseName != "NUL" && baseName != "CONIN$" && baseName != "CONOUT$" &&
            !(baseName.Length == 4 && (baseName.StartsWith("COM", StringComparison.Ordinal) ||
                baseName.StartsWith("LPT", StringComparison.Ordinal)) && "0123456789¹²³".IndexOf(baseName[3]) >= 0);
    }

    private static readonly char[] InvalidNameCharacters = { '/', '\\', '<', '>', ':', '"', '|', '?', '*' };

    private static void RequireRegularPath(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked profile files and folders are unsupported.");
    }

    private static string Serialize(SimulationStartupConfig config, ExperimentSettings? experimentSettings) =>
        JsonUtility.ToJson(new ProfileFile
        {
            profileVersion = ProfileVersion,
            configurationJson = SimulationStartupController.SerializeSavedConfig(config),
            includesExperimentSettings = experimentSettings.HasValue,
            experimentSettings = experimentSettings.GetValueOrDefault()
        }, true);

    private static SimulationStartupConfig Read(string path, SimulationStartupConfig fallback, out ExperimentSettings? experiments)
    {
        try
        {
            var file = JsonUtility.FromJson<ProfileFile>(File.ReadAllText(path));
            if (file == null || file.profileVersion == 0 || string.IsNullOrWhiteSpace(file.configurationJson))
                throw new InvalidDataException("This file is missing its startup profile data.");
            if (file.profileVersion != ProfileVersion)
                throw new InvalidDataException("This profile format version is unsupported.");
            experiments = file.includesExperimentSettings ? file.experimentSettings : (ExperimentSettings?)null;
            if (experiments.HasValue &&
                experiments.Value.lifecycleThermalSamplingMode != nameof(LifecycleThermalSamplingMode.Reference) &&
                experiments.Value.lifecycleThermalSamplingMode != nameof(LifecycleThermalSamplingMode.Optimized))
                throw new InvalidDataException("This profile has an unsupported lifecycle thermal sampling mode.");
            return SimulationStartupController.DeserializeProfileConfiguration(file.configurationJson, fallback);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("This profile contains malformed JSON.", exception);
        }
    }

    internal static void WriteNewJson(string path, string json) => WriteJson(path, json, replace: false);

    private static void WriteJson(string path, string json, bool replace)
    {
        string temporaryPath = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".tmp");
        var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write);
        try
        {
            using (stream)
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
                    writer.Write(json);
                stream.Flush(flushToDisk: true);
            }
            if (replace) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
