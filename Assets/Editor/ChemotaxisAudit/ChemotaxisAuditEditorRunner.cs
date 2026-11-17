using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>Opt-in local development runner for an already open Editor. Never
/// stops Play Mode, changes scenes, saves assets, or launches another Editor.</summary>
[InitializeOnLoad]
public static class ChemotaxisAuditEditorRunner
{
    private static readonly string DirectoryPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/FoodTaxisAudit"));
    private static double nextPoll;
    private static TestRunnerApi api;
    private static bool running;
    private static string label;

    static ChemotaxisAuditEditorRunner() => EditorApplication.update += Poll;

    [Serializable] private sealed class Status
    {
        public string utc, state, command;
        public string runnerVersion = "food-audit-v2";
        public bool playing, paused, compiling, sceneDirty;
        public int passed, failed, skipped;
    }

    private static void WriteStatus(string state, int passed = 0, int failed = 0, int skipped = 0)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, "editor-status.json"), JsonUtility.ToJson(new Status {
            utc = DateTime.UtcNow.ToString("O"), state = state, command = label,
            playing = EditorApplication.isPlaying, paused = EditorApplication.isPaused,
            compiling = EditorApplication.isCompiling, sceneDirty = UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty,
            passed = passed, failed = failed, skipped = skipped }, true));
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1d;
        if (!File.Exists(Path.Combine(DirectoryPath, "editor-enabled"))) return;
        if (running) return;
        string commandPath = Path.Combine(DirectoryPath, "editor-command.txt");
        string planPath = Path.Combine(DirectoryPath, "editor-plan.txt");
        if (!File.Exists(commandPath) && File.Exists(planPath))
        {
            string[] plan = File.ReadAllLines(planPath);
            if (plan.Length > 0) File.WriteAllText(commandPath, plan[0]);
        }
        if (!File.Exists(commandPath)) { WriteStatus("ready"); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        { WriteStatus("waiting-for-edit-mode"); return; }
        string refreshPath = Path.Combine(DirectoryPath, "editor-refresh.txt");
        if (File.Exists(refreshPath))
        {
            File.Delete(refreshPath); WriteStatus("refreshing-assets");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); nextPoll = EditorApplication.timeSinceStartup + 3d; return;
        }
        label = File.ReadAllText(commandPath).Trim();
        string groups = label == "baseline" ? "^(GeodesicLocomotionEcologyExperiments|GeodesicHydrogenotrophyIntegrationTests|GeodesicTaxisPolicyTests)\\."
            : label == "food" ? "^GeodesicFoodGradientTests\\."
            : label == "alternatives" ? "^(GeodesicFoodGradientTests|GeodesicFoodEcologyExperiments)\\." : null;
        if (groups == null) { WriteStatus("invalid-command"); return; }
        File.Delete(commandPath); // Consume only this opt-in command file, never user data.
        running = true; WriteStatus("running");
        try
        {
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Results());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode,
                assemblyNames = new[] { "SimulaVit.EditModeTests" }, groupNames = new[] { groups } }));
        }
        catch (Exception error) { running = false; WriteStatus("runner-error"); Debug.LogException(error); }
    }

    private sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor test) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result)
        {
            if (!result.HasChildren) File.AppendAllText(Path.Combine(DirectoryPath, "native-progress.txt"),
                label + " " + result.ResultState + " " + result.FullName + "\n");
        }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (api != null) api.UnregisterCallbacks(this);
            TestRunnerApi.SaveResultToFile(result, Path.Combine(DirectoryPath, label + "-results.xml"));
            running = false;
            WriteStatus(result.PassCount + result.FailCount + result.SkipCount > 0 ? "completed" : "no-tests-matched",
                result.PassCount, result.FailCount, result.SkipCount);
            File.Copy(Path.Combine(DirectoryPath, "editor-status.json"), Path.Combine(DirectoryPath, label + "-summary.json"), true);
            string planPath = Path.Combine(DirectoryPath, "editor-plan.txt");
            if (File.Exists(planPath))
            {
                string[] plan = File.ReadAllLines(planPath);
                if (result.PassCount > 0 && result.FailCount == 0 && plan.Length > 0 && plan[0].Trim() == label)
                {
                    if (plan.Length == 1) File.Delete(planPath);
                    else File.WriteAllLines(planPath, new ArraySegment<string>(plan, 1, plan.Length - 1));
                }
                else File.Move(planPath, Path.Combine(DirectoryPath, "editor-plan-stopped-" + DateTime.UtcNow.Ticks + ".txt"));
            }
            if (api != null) UnityEngine.Object.DestroyImmediate(api);
        }
    }
}
