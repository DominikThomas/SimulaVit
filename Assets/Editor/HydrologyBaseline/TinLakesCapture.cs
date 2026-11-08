using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Captures the actual initialized scene; never reconstructs or regenerates a planet.</summary>
[InitializeOnLoad]
public static class TinLakesCapture
{
    static TinLakesCapture() => EditorApplication.update += Poll;
    private static double captureAfter;
    private static void Poll()
    {
        const string request = "Library/TinLakesCapture.request";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (SessionState.GetBool("TinLakes.StartReference", false) && EditorApplication.isPlaying)
        {
            var startup = Object.FindFirstObjectByType<SimulationStartupController>();
            var owner = Object.FindFirstObjectByType<PlanetGenerator>();
            if (startup == null || owner == null) return;
            SessionState.SetBool("TinLakes.StartReference", false);
            var c = startup.CurrentConfig;
            if (c.planetSeed != 123456 || c.useRandomSeed || c.gridType != PlanetGridType.GeodesicIcosphere || c.geodesicSubdivisionLevel != 6 ||
                owner.geodesicRenderSubdivisionLevel != 7 || c.riverFlowThreshold != 2 || c.hydrologyTerrainMode != HydrologyTerrainMode.Visible ||
                !c.generateHydrologicalLakes || Math.Abs(c.minimumLakeDepth - .045f) > 1e-7 || c.minimumLakeBasinAreaFraction != 0)
            {
                Directory.CreateDirectory("Artifacts/TinLakes");
                File.WriteAllText("Artifacts/TinLakes/startup-mismatch.json", JsonUtility.ToJson(c, true));
                Debug.LogError("[GeodesicTinLakes] Saved startup configuration does not match the reference. No settings were substituted; start the correct planet manually.");
                return;
            }
            var save = typeof(SimulationStartupController).GetField("saveStartupConfigOnStart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            SessionState.SetBool("TinLakes.RestoreSave", (bool)save.GetValue(startup)); save.SetValue(startup, false);
            SessionState.SetBool("TinLakes.WaitForStart", true);
            startup.StartSimulationPaused(); captureAfter = EditorApplication.timeSinceStartup + 3;
        }
        if (SessionState.GetBool("TinLakes.WaitForStart", false))
        {
            var startup = Object.FindFirstObjectByType<SimulationStartupController>();
            if (startup == null || !startup.StartupComplete || EditorApplication.timeSinceStartup < captureAfter) return;
            typeof(SimulationStartupController).GetField("saveStartupConfigOnStart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(startup, SessionState.GetBool("TinLakes.RestoreSave", true));
            SessionState.SetBool("TinLakes.WaitForStart", false);
        }
        if (!File.Exists(request)) return;
        if (File.ReadAllText(request).Trim() == "start-reference" && !EditorApplication.isPlaying)
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/PlanetScene.unity") return;
            File.WriteAllText(request, "capture"); SessionState.SetBool("TinLakes.StartReference", true);
            EditorApplication.isPlaying = true; return;
        }
        var p = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None).FirstOrDefault(x => x.IsPlanetInitialized && x.GetComponent<GeodesicRiverSystem>()?.TinDrainage != null);
        if (p == null) return; // Wait for the user's normal startup, never substitute terrain settings.
        File.Delete(request);
        try { Capture(); } catch (Exception e) { Directory.CreateDirectory("Artifacts/TinLakes"); File.WriteAllText("Artifacts/TinLakes/error.txt", e.ToString()); Debug.LogException(e); }
    }
    private static string State(PlanetGenerator planet, GeodesicRiverSystem rivers)
    {
        using (var stream = new MemoryStream())
        using (var w = new BinaryWriter(stream))
        {
            var tin = rivers.TinDrainage;
            foreach (float h in tin.VisibleHeight) w.Write(h); foreach (float h in tin.HydrologyHeight) w.Write(h);
            foreach (int x in tin.TriangleReceiver) w.Write(x); foreach (int x in tin.Neighbors) w.Write(x);
            foreach (double x in tin.AccumulatedRunoff) w.Write(x); foreach (double x in tin.LocalArea) w.Write(x); foreach (double x in tin.DrainageArea) w.Write(x);
            w.Write(tin.Diagnostics.Summary()); w.Write(rivers.riverFlowThreshold);
            void V(Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
            foreach (var n in tin.Networks)
            {
                w.Write(n.SourceTriangle); w.Write(n.JunctionNetwork); w.Write((int)n.Termination); w.Write(n.Length);
                foreach (var p in n.Samples) { w.Write(p.Triangle); w.Write(p.A); w.Write(p.B); w.Write(p.C); }
                foreach (var v in n.Centreline) V(v);
            }
            for (int t = 0; t < tin.TriangleCount; t++)
            { w.Write(tin.IsOcean(tin.Centroid(t))); for (int k = 0; k < 3; k++) w.Write(tin.IsOcean(new TinPoint(t, k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0))); }
            foreach (var f in planet.GetComponentsInChildren<MeshFilter>(true).Where(f => f.gameObject.name != "Geodesic TIN Lakes" && !f.gameObject.name.StartsWith("TIN topology DEBUG")).OrderBy(f => f.GetInstanceID()))
            {
                var m = f.sharedMesh; if (m == null || !m.isReadable) continue;
                w.Write(f.GetInstanceID()); foreach (var v in m.vertices) V(v);
                foreach (var c in m.colors) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
                for (int s = 0; s < m.subMeshCount; s++) { w.Write((int)m.GetTopology(s)); foreach (int i in m.GetIndices(s)) w.Write(i); }
            }
            w.Flush(); using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream.ToArray()));
        }
    }
    [MenuItem("Tools/Hydrology/Capture Static TIN Lakes (actual planet)")]
    public static void Capture()
    {
        var planet = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsPlanetInitialized && p.CurrentGridType == PlanetGridType.GeodesicIcosphere);
        var rivers = planet != null ? planet.GetComponent<GeodesicRiverSystem>() : null;
        if (rivers?.TinDrainage == null || Camera.main == null) throw new InvalidOperationException("Generate the normal live planet first.");
        if (planet.randomSeed != 123456 || planet.GeodesicTopology.SubdivisionLevel != 6 || planet.geodesicRenderSubdivisionLevel != 7 ||
            planet.hydrologyTerrainMode != HydrologyTerrainMode.Visible || rivers.riverFlowThreshold != 2 || Math.Abs(planet.minimumLakeDepth - .045f) > 1e-7 || planet.minimumLakeBasinAreaFraction != 0)
            throw new InvalidOperationException("Live reference settings must be seed 123456 / 6 / 7 / Visible / river 2 / depth .045 / area 0. No settings were substituted.");
        string directory = Path.GetFullPath("Artifacts/TinLakes/" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "planet-settings.json"), EditorJsonUtility.ToJson(planet, true));
        bool enabled = planet.generateHydrologicalLakes; string settings = EditorJsonUtility.ToJson(planet);
        var inspector = EditorWindow.GetWindow<TinBasinsLiveWindow>("TIN Basin Debug"); inspector.HideLakeComparison();
        var cameraObject = new GameObject("TIN lake capture camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
        camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
        var report = new StringBuilder(); string before = State(planet, rivers);
        void Image(string name) => RiverUphillDebugCapture.Image(camera, Path.Combine(directory, name + ".png"));
        void Focus(TinBasinDescriptor b, bool spill = false)
        {
            var point = spill && b.Spill.HasValue ? b.Spill.Value : b.Floor;
            Vector3 direction = rivers.TinDrainage.Direction(point);
            Vector3 target = planet.transform.TransformPoint(direction * (float)b.SpillElevation);
            Vector3 normal = planet.transform.TransformDirection(direction).normalized;
            float span = spill ? planet.BasePlanetRadius * .025f : Mathf.Max(planet.BasePlanetRadius * .025f, Mathf.Sqrt((float)rivers.TinLakeGeometry.Bodies.First(x => x.BasinId == b.Id).Area) * 1.2f);
            Vector3 up = Vector3.Cross(normal, Math.Abs(normal.y) < .9f ? Vector3.up : Vector3.right).normalized;
            camera.transform.position = target + normal * span * 2.2f; camera.transform.LookAt(target, up); camera.nearClipPlane = .001f;
        }
        try
        {
            planet.generateHydrologicalLakes = false; rivers.RefreshTinLakeVisuals(); Image("01-lakes-OFF");
            if (before != State(planet, rivers)) throw new InvalidOperationException("Lake OFF changed hydrology/river/terrain state.");
            planet.generateHydrologicalLakes = true; rivers.RefreshTinLakeVisuals(); Image("02-lakes-ON");
            if (before != State(planet, rivers)) throw new InvalidOperationException("Lake ON changed hydrology/river/terrain state.");
            var geometry = rivers.TinLakeGeometry; var topology = rivers.TinLakeBasins;
            report.AppendLine(geometry.Summary()).AppendLine(topology.Summary()).AppendLine(GeodesicTinBasinInspection.UnmatchedClosedReport(topology, rivers.TinDrainage));
            report.AppendLine($"actualScene={planet.gameObject.scene.path} seed={planet.randomSeed} simulation=6 render=7 Visible threshold=2 minimumDepth={planet.minimumLakeDepth:R} minimumArea={planet.minimumLakeBasinAreaFraction:R}");
            foreach (var b in geometry.Bodies.OrderByDescending(b => b.Area)) report.AppendLine($"body={b.BasinId} level={b.WaterLevel:R} area={b.Area:R} vertices={b.VertexCount} triangles={b.IndexCount / 3}");
            camera.transform.position = planet.transform.position + (Camera.main.transform.position - planet.transform.position).normalized * planet.BasePlanetRadius * 3.2f;
            camera.transform.LookAt(planet.transform.position); Image("03-whole-planet");
            var chosen = geometry.Bodies.OrderByDescending(b => b.Area).Take(3).ToArray();
            foreach (var body in chosen)
            {
                var basin = topology.Basins.First(b => b.Id == body.BasinId); Focus(basin);
                Image("04-large-lake-" + basin.Id);
                // Exact same camera OFF/ON detail comparison without re-extracting a river.
                var root = rivers.TinLakeVisualRoot; root.SetActive(false); Image("05-shoreline-OFF-" + basin.Id); root.SetActive(true); Image("05-shoreline-ON-" + basin.Id);
                inspector.ShowLakeComparison(basin.Id); Image("06-overlay-water-" + basin.Id); inspector.HideLakeComparison();
                Focus(basin, true); inspector.ShowLakeComparison(basin.Id); Image("07-spill-" + basin.Id); inspector.HideLakeComparison();
                report.AppendLine(GeodesicTinBasinInspection.GeometryChecks(rivers.TinDrainage, basin));
            }
        }
        finally
        {
            inspector.HideLakeComparison(); planet.generateHydrologicalLakes = enabled; rivers.RefreshTinLakeVisuals();
            Object.DestroyImmediate(cameraObject);
            string after = State(planet, rivers); report.AppendLine("before=" + before).AppendLine("after=" + after).AppendLine("RiverHydrologyTerrainUnchanged=" + (before == after)).AppendLine("PlanetSettingsRestored=" + (settings == EditorJsonUtility.ToJson(planet)));
            File.WriteAllText(Path.Combine(directory, "report.txt"), report.ToString()); Debug.Log(report.ToString());
            if (before != after || settings != EditorJsonUtility.ToJson(planet)) throw new InvalidOperationException("Capture state invariants failed; see report.");
        }
    }
}
