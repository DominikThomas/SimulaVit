using System;
using System.IO;
using UnityEngine;

public partial class SimulationStartupController
{
    private enum ProfileEdit { None, SaveAs, Rename, Delete }

    private SimulationStartupProfiles profileStore;
    private SimulationStartupProfiles ProfileStore => profileStore ??= new SimulationStartupProfiles(Application.persistentDataPath);
    private string[] profileNames = Array.Empty<string>();
    private string selectedProfileName, sourceProfileName, profileBaseline, profileStatus;
    private bool profileModified, includeProfileExperiments, loadProfileExperiments;
    private string profileNameEntry = "";
    private ProfileEdit profileEdit;
    private Vector2 profileListScroll;
    private Action pendingProfileAction;

    private void DrawProfilesTab()
    {
        SetupSection("Configuration Profiles");
        SetupBool("Save last-used configuration automatically", ref saveStartupConfigOnStart);
        SetupHelp("Automatically saves the ordinary setup on Start, Back or quitting from New Game. Named profiles are saved explicitly.");
        SetupHelp(sourceProfileName == null ? "Current setup has no source profile."
            : $"Current setup: {sourceProfileName}" + (profileModified ? " (modified)" : ""));

        profileListScroll = GUILayout.BeginScrollView(profileListScroll, false, true, GUILayout.Height(112f));
        if (profileNames.Length == 0) SetupHelp("No named profiles. Use Save As to keep this setup.");
        foreach (string name in profileNames)
        {
            if (GUILayout.Button(name, name == selectedProfileName ? selectedSetupTabStyle : setupTabStyle, GUILayout.Height(28f)))
            {
                pendingProfileAction = () =>
                {
                    selectedProfileName = name;
                    profileEdit = ProfileEdit.None;
                    loadProfileExperiments = false;
                };
            }
        }
        GUILayout.EndScrollView();
        SetupHelp(selectedProfileName == null ? "No profile selected." : $"Selected profile: {selectedProfileName}. Save overwrites this profile.");

        bool enabled = GUI.enabled;
        GUI.enabled = enabled && selectedProfileName != null;
        SetupBool("Apply saved session-only experiment settings on Load", ref loadProfileExperiments);
        GUI.enabled = enabled;
        SetupBool("Include session-only experiment settings when saving", ref includeProfileExperiments);

        GUILayout.BeginHorizontal();
        GUI.enabled = enabled && selectedProfileName != null;
        if (GUILayout.Button("Load", buttonStyle, GUILayout.Height(30f))) pendingProfileAction = LoadSelectedProfile;
        GUI.enabled = enabled;
        if (GUILayout.Button("Save", buttonStyle, GUILayout.Height(30f)))
        {
            pendingProfileAction = selectedProfileName == null
                ? () => BeginProfileEdit(ProfileEdit.SaveAs) : () => SaveSelectedProfile(selectedProfileName, overwrite: true);
        }
        if (GUILayout.Button("Save As", buttonStyle, GUILayout.Height(30f))) pendingProfileAction = () => BeginProfileEdit(ProfileEdit.SaveAs);
        GUI.enabled = enabled && selectedProfileName != null;
        if (GUILayout.Button("Rename", buttonStyle, GUILayout.Height(30f))) pendingProfileAction = () => BeginProfileEdit(ProfileEdit.Rename);
        if (GUILayout.Button("Delete", buttonStyle, GUILayout.Height(30f))) pendingProfileAction = () => BeginProfileEdit(ProfileEdit.Delete);
        GUI.enabled = enabled;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh", buttonStyle, GUILayout.Height(30f)))
            pendingProfileAction = () => { profileEdit = ProfileEdit.None; RefreshProfileList(); };
        if (GUILayout.Button("Open Profiles Folder", buttonStyle, GUILayout.Height(30f))) pendingProfileAction = OpenProfilesFolder;
        GUILayout.EndHorizontal();

        if (profileEdit != ProfileEdit.None)
        {
            if (profileEdit == ProfileEdit.Delete) SetupHelp($"Delete “{selectedProfileName}”?");
            else
            {
                SetupHelp(profileEdit == ProfileEdit.Rename ? $"New name for “{selectedProfileName}”" : "Profile name");
                profileNameEntry = GUILayout.TextField(profileNameEntry, SimulationStartupProfiles.MaxNameLength, GUILayout.Height(28f));
            }
            GUILayout.BeginHorizontal();
            string confirm = profileEdit == ProfileEdit.Delete ? "Confirm Delete" : profileEdit == ProfileEdit.Rename ? "Confirm Rename" : "Save New Profile";
            if (GUILayout.Button(confirm, buttonStyle, GUILayout.Height(30f))) pendingProfileAction = ConfirmProfileEdit;
            if (GUILayout.Button("Cancel", buttonStyle, GUILayout.Height(30f))) pendingProfileAction = () => BeginProfileEdit(ProfileEdit.None);
            GUILayout.EndHorizontal();
        }
        if (!string.IsNullOrEmpty(profileStatus)) SetupHelp(profileStatus);
    }

    private void EnterProfilesTab()
    {
        profileEdit = ProfileEdit.None;
        profileStatus = null;
        RefreshProfileList();
        profileModified = HasProfileChanges(currentConfig);
    }

    private void RefreshProfileList()
    {
        try
        {
            profileNames = ProfileStore.List();
            if (Array.IndexOf(profileNames, selectedProfileName) < 0)
            {
                selectedProfileName = null;
                loadProfileExperiments = false;
            }
        }
        catch (Exception exception) when (IsProfileFileError(exception))
        {
            profileStatus = $"Could not list profiles. {exception.Message}";
        }
    }

    private void RunPendingProfileAction()
    {
        if (pendingProfileAction == null) return;
        Action action = pendingProfileAction;
        pendingProfileAction = null;
        profileStatus = null;
        try { action(); }
        catch (Exception exception) when (IsProfileFileError(exception)) { profileStatus = exception.Message; }
        GUI.FocusControl(null);
        GUIUtility.ExitGUI();
    }

    private static bool IsProfileFileError(Exception exception) =>
        exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is ArgumentException ||
        exception is NotSupportedException || exception is System.ComponentModel.Win32Exception;

    private void BeginProfileEdit(ProfileEdit edit)
    {
        profileEdit = edit;
        profileNameEntry = edit == ProfileEdit.Rename ? selectedProfileName : "";
    }

    private void ConfirmProfileEdit()
    {
        switch (profileEdit)
        {
            case ProfileEdit.SaveAs:
                SaveSelectedProfile(profileNameEntry, overwrite: false);
                return;
            case ProfileEdit.Rename:
                ProfileStore.Rename(selectedProfileName, profileNameEntry);
                if (sourceProfileName == selectedProfileName) sourceProfileName = profileNameEntry;
                selectedProfileName = profileNameEntry;
                profileStatus = $"Renamed to “{selectedProfileName}”.";
                break;
            case ProfileEdit.Delete:
                ProfileStore.Delete(selectedProfileName);
                profileStatus = $"Deleted “{selectedProfileName}”.";
                selectedProfileName = null;
                break;
        }
        profileEdit = ProfileEdit.None;
        RefreshProfileList();
    }

    private void SaveSelectedProfile(string name, bool overwrite)
    {
        var config = currentConfig.Clone();
        ClampLoadedConfig(config);
        SimulationStartupProfiles.ExperimentSettings? experiments = includeProfileExperiments
            ? new SimulationStartupProfiles.ExperimentSettings(detailedGeodesicProfiling, lifecycleThermalSampling) : null;
        if (overwrite) ProfileStore.Overwrite(name, config, experiments);
        else ProfileStore.Create(name, config, experiments);
        selectedProfileName = name;
        SetProfileBaseline(name, config);
        loadProfileExperiments = false;
        profileEdit = ProfileEdit.None;
        profileStatus = $"Saved “{name}”.";
        RefreshProfileList();
    }

    private void LoadSelectedProfile()
    {
        var config = ProfileStore.Load(selectedProfileName, defaults, out var experiments);
        ApplyLoadedStartupConfig(config);
        ResolveProfileExperiments(loadProfileExperiments, experiments, ref detailedGeodesicProfiling, ref lifecycleThermalSampling);
        RefreshStartupPanels();
        SetProfileBaseline(selectedProfileName, config);
        profileStatus = $"Loaded “{selectedProfileName}”." + (loadProfileExperiments && !experiments.HasValue
            ? " This profile has no experiment settings." : "");
        loadProfileExperiments = false;
        profileEdit = ProfileEdit.None;
    }

    internal static void ResolveProfileExperiments(bool apply, SimulationStartupProfiles.ExperimentSettings? experiments,
        ref bool detailed, ref LifecycleThermalSamplingMode thermalMode)
    {
        if (!apply || !experiments.HasValue) return;
        detailed = experiments.Value.detailedGeodesicProfiling;
        thermalMode = experiments.Value.ThermalMode;
    }

    private void OpenProfilesFolder()
    {
        string path = ProfileStore.OpenDirectory();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })) { }
#else
        Application.OpenURL(new Uri(path + Path.DirectorySeparatorChar).AbsoluteUri);
#endif
    }

    private void SetProfileBaseline(string name, SimulationStartupConfig config)
    {
        sourceProfileName = name;
        profileBaseline = ProfileComparisonJson(config);
        profileModified = false;
    }

    private bool HasProfileChanges(SimulationStartupConfig config) =>
        profileBaseline != null && profileBaseline != ProfileComparisonJson(config);

    private string ProfileComparisonJson(SimulationStartupConfig config)
    {
        var copy = config.Clone();
        ClampLoadedConfig(copy);
        // The start command chooses the pause state and random seeds are resolved at each generation.
        copy.startPaused = false;
        if (copy.useRandomSeed) copy.planetSeed = 0;
        return SerializeSavedConfig(copy);
    }

    [Serializable]
    private sealed class StartupCaptureSnapshot
    {
        public int snapshotVersion = 1;
        public string sourceProfileName;
        public bool profileModified;
        public int configuredSeed;
        public bool configuredUseRandomSeed;
        public int resolvedWorldSeed;
        public SavedStartupConfig configuration;
        public SimulationStartupProfiles.ExperimentSettings experimentSettings;
        public CaptureGeodesicGeometry geodesicGeometry;
    }

    [Serializable]
    private sealed class CaptureGeodesicGeometry
    {
        public int renderSubdivision, colliderSubdivision, oceanRenderSubdivision, atmosphereRenderSubdivision;
        public float resolvedSeaLevelRadius;
        public bool terrainDisplacement;
        public PlanetTerrainSettings terrain;
    }

    internal static string StartupCaptureSnapshotPath(string csvPath) => Path.ChangeExtension(csvPath, ".config.json");

    internal static void WriteStartupCaptureSnapshot(string csvPath, SimulationStartupConfig config, int configuredSeed, bool configuredUseRandomSeed,
        string sourceProfileName, bool profileModified, SimulationStartupProfiles.ExperimentSettings experiments,
        PlanetGenerator generator = null)
    {
        var capture = new StartupCaptureSnapshot
        {
            sourceProfileName = sourceProfileName,
            profileModified = profileModified,
            configuredSeed = configuredSeed,
            configuredUseRandomSeed = configuredUseRandomSeed,
            resolvedWorldSeed = config.planetSeed,
            configuration = SavedStartupConfig.FromConfig(config),
            experimentSettings = experiments
        };
        if (generator != null && config.gridType == PlanetGridType.GeodesicIcosphere)
        {
            capture.geodesicGeometry = new CaptureGeodesicGeometry
            {
                renderSubdivision = generator.geodesicRenderSubdivisionLevel,
                colliderSubdivision = generator.geodesicColliderSubdivisionLevel,
                oceanRenderSubdivision = generator.geodesicOceanRenderSubdivisionLevel,
                atmosphereRenderSubdivision = generator.geodesicAtmosphereRenderSubdivisionLevel,
                resolvedSeaLevelRadius = generator.ResolvedGeodesicSeaLevelRadius,
                terrainDisplacement = generator.enableGeodesicTerrainDisplacement,
                terrain = generator.GetGeodesicTerrainSettings()
            };
        }
        SimulationStartupProfiles.WriteNewJson(StartupCaptureSnapshotPath(csvPath), JsonUtility.ToJson(capture, true));
    }
}
