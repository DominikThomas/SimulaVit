using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Actual-scene A/B capture. The lake meshes and raw TIN are never rebuilt.</summary>
[InitializeOnLoad]
public static class TinLakeRoutingCapture
{
    static TinLakeRoutingCapture() => EditorApplication.update += Poll;
    private static void Poll()
    {
        const string request = "Library/TinLakeRoutingCapture.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var p = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None).FirstOrDefault(x => x.IsPlanetInitialized && x.GetComponent<GeodesicRiverSystem>()?.TinDrainage != null);
        if (p == null) return;
        File.Delete(request);
        try { Capture(); }
        catch (Exception e) { Directory.CreateDirectory("Artifacts/TinLakeRouting"); File.WriteAllText("Artifacts/TinLakeRouting/error.txt", e.ToString()); Debug.LogException(e); }
    }
    private static string State(PlanetGenerator planet, GeodesicRiverSystem rivers)
    {
        using (var stream = new MemoryStream())
        using (var w = new BinaryWriter(stream))
        {
            void V(Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
            var tin = rivers.TinDrainage;
            foreach (float h in tin.VisibleHeight) w.Write(h); foreach (float h in tin.HydrologyHeight) w.Write(h);
            foreach (int x in tin.TriangleReceiver) w.Write(x); foreach (int x in tin.Neighbors) w.Write(x);
            foreach (double x in tin.AccumulatedRunoff) w.Write(x); foreach (double x in tin.LocalArea) w.Write(x); foreach (double x in tin.DrainageArea) w.Write(x);
            w.Write(rivers.riverFlowThreshold);
            foreach (var n in tin.Networks)
            {
                w.Write(n.SourceTriangle); w.Write(n.JunctionNetwork); w.Write((int)n.Termination); w.Write(n.Length);
                foreach (var p in n.Samples) { w.Write(p.Triangle); w.Write(p.A); w.Write(p.B); w.Write(p.C); }
                foreach (var v in n.Centreline) V(v);
            }
            for (int t = 0; t < tin.TriangleCount; t++)
            {
                V(tin.DownhillGradient(t)); w.Write(tin.IsOcean(tin.Centroid(t)));
                for (int k = 0; k < 3; k++) w.Write(tin.IsOcean(new TinPoint(t, k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0)));
            }
            // Raw river geometry is intentionally replaced; all terrain, ocean and lake
            // meshes (including their object identities) must remain byte-identical.
            foreach (var f in planet.GetComponentsInChildren<MeshFilter>(true).Where(f =>
                f.gameObject.name != "TIN River Ribbons" && f.gameObject.name != "Raw TIN Gradient Rivers" &&
                !f.gameObject.name.StartsWith("TIN topology DEBUG") && !f.gameObject.name.Contains("Debug") && !f.gameObject.name.Contains("DEBUG")).OrderBy(f => f.GetInstanceID()))
            {
                var m = f.sharedMesh; if (m == null || !m.isReadable) continue;
                w.Write(f.GetInstanceID()); foreach (var v in m.vertices) V(v);
                for (int s = 0; s < m.subMeshCount; s++) { w.Write((int)m.GetTopology(s)); foreach (int i in m.GetIndices(s)) w.Write(i); }
            }
            w.Flush(); using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream.ToArray()));
        }
    }
    [MenuItem("Tools/Hydrology/Capture TIN River Lake Integration (actual planet)")]
    public static void Capture()
    {
        var planet = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsPlanetInitialized && p.CurrentGridType == PlanetGridType.GeodesicIcosphere);
        var rivers = planet != null ? planet.GetComponent<GeodesicRiverSystem>() : null;
        if (rivers?.TinDrainage == null || rivers.TinLakeGeometry == null || Camera.main == null) throw new InvalidOperationException("Start the normal reference planet first.");
        if (planet.randomSeed != 123456 || planet.GeodesicTopology.SubdivisionLevel != 6 || planet.geodesicRenderSubdivisionLevel != 7 ||
            planet.hydrologyTerrainMode != HydrologyTerrainMode.Visible || rivers.riverFlowThreshold != 2 || !planet.generateHydrologicalLakes ||
            Math.Abs(planet.minimumLakeDepth - .045f) > 1e-7 || planet.minimumLakeBasinAreaFraction != 0)
            throw new InvalidOperationException("Reference must be 123456 / 6 / 7 / Visible / river 2 / lakes ON / depth .045 / area 0. No settings substituted.");
        string directory = Path.GetFullPath("Artifacts/TinLakeRouting/" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "planet-settings.json"), EditorJsonUtility.ToJson(planet, true));
        var report = new StringBuilder(); string before = State(planet, rivers); bool enabled = rivers.integrateTinLakeRivers, rawMode = rivers.drawRawTrianglePaths;
        string settings = EditorJsonUtility.ToJson(planet); float timeScale = Time.timeScale;
        var inspector = EditorWindow.GetWindow<TinBasinsLiveWindow>("TIN Basin Debug"); inspector.HideLakeComparison();
        var cameraObject = new GameObject("TIN routing capture camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
        camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
        void Image(string name) => RiverUphillDebugCapture.Image(camera, Path.Combine(directory, name + ".png"));
        void Focus(TinLakeFlowNode n, bool spill = false)
        {
            var b = n.Basin; var point = spill ? b.Spill.Value : b.Floor;
            var direction = rivers.TinDrainage.Direction(point); var normal = planet.transform.TransformDirection(direction).normalized;
            var target = planet.transform.TransformPoint(direction * (float)b.SpillElevation);
            float span = spill ? planet.BasePlanetRadius * .018f : Mathf.Max(planet.BasePlanetRadius * .025f, Mathf.Sqrt((float)rivers.TinLakeGeometry.Bodies.First(x => x.BasinId == b.Id).Area) * 1.35f);
            var up = Vector3.Cross(normal, Math.Abs(normal.y) < .9f ? Vector3.up : Vector3.right).normalized;
            camera.transform.position = target + normal * span * 2.2f; camera.transform.LookAt(target, up); camera.nearClipPlane = .001f;
        }
        try
        {
            Time.timeScale = 0;
            if (rivers.TinLakeRouting == null) { rivers.integrateTinLakeRivers = true; rivers.RefreshTinLakeRiverIntegration(); }
            var routing = rivers.TinLakeRouting;
            var one = routing.Lakes.Where(n => n.InletDischarges.Count == 1 && n.OutletReachId >= 0 && !routing.Lakes.Any(up => up.DownstreamLakeId == n.BasinId)).OrderByDescending(n => n.Discharge).FirstOrDefault();
            var multi = routing.Lakes.Where(n => n.InletDischarges.Count > 1 && n.OutletReachId >= 0).OrderByDescending(n => n.InletDischarges.Count).FirstOrDefault();
            var cascade = routing.Lakes.Where(n => n.DownstreamLakeId >= 0).OrderByDescending(n => n.Discharge).FirstOrDefault();
            var examples = new[] { one, multi, cascade }.Where(n => n != null).GroupBy(n => n.BasinId).Select(g => g.First()).ToArray();
            rivers.integrateTinLakeRivers = false; rivers.RefreshTinLakeRiverIntegration();
            Image("01-before-integration");
            var baselinePaths = rivers.RiverPaths.ToDictionary(p => p.Key, p => p.Value.ToArray());
            foreach (var n in examples) { Focus(n); Image("02-before-lake-" + n.BasinId); Focus(n, true); Image("03-before-spill-" + n.BasinId); }
            rivers.integrateTinLakeRivers = true; rivers.RefreshTinLakeRiverIntegration(); routing = rivers.TinLakeRouting;
            camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation); Image("04-after-integration");
            foreach (var n in examples)
            {
                Focus(n); Image("05-after-lake-" + n.BasinId); Focus(n, true); Image("06-after-spill-" + n.BasinId);
                inspector.ShowLakeComparison(n.BasinId); Image("07-spill-marker-" + n.BasinId); inspector.HideLakeComparison();
            }
            int unchanged = 0, mismatches = 0;
            foreach (var reach in routing.RiverReaches.Where(r => !r.Changed && !routing.HasAddedDischarge(r.RawNetworkId)))
            {
                bool had = baselinePaths.TryGetValue(reach.SourceTriangle, out var a), has = rivers.RiverPaths.TryGetValue(reach.SourceTriangle, out var b);
                if (had == has && (!had || a.SequenceEqual(b))) unchanged++; else mismatches++;
            }
            report.AppendLine(routing.Summary()).AppendLine(rivers.TinLakeGeometry.Summary()).AppendLine(rivers.TinLakeBasins.Summary());
            report.AppendLine($"actualScene={planet.gameObject.scene.path} seed=123456 simulation=6 render=7 Visible threshold=2 minimumDepth=.045 minimumArea=0 rawLineMode={rivers.drawRawTrianglePaths}");
            report.AppendLine($"oneInletExample={one?.BasinId} multiInletExample={multi?.BasinId} cascadeExample={cascade?.BasinId} untouchedRawPaths={unchanged} pathMismatches={mismatches}");
            report.AppendLine($"rawNetworks={rivers.TinDrainage.Networks.Count} visibleReaches={rivers.VisibleRiverReaches} riverVertices={rivers.RiverMeshVertexCount}");
            // Exercise the actual ribbon material/width path as well as the user's raw
            // debug mode. The original mode is restored below.
            rivers.drawRawTrianglePaths = false; rivers.integrateTinLakeRivers = false; rivers.RefreshTinLakeRiverIntegration();
            var ribbonBaseline = rivers.RiverPaths.ToDictionary(p => p.Key, p => p.Value.ToArray());
            foreach (var n in examples) { Focus(n); Image("08-ribbon-before-lake-" + n.BasinId); Focus(n, true); Image("09-ribbon-before-spill-" + n.BasinId); }
            planet.generateHydrologicalLakes = false; rivers.integrateTinLakeRivers = true; rivers.RefreshTinLakeRiverIntegration();
            bool disabledUnchanged = rivers.TinLakeRouting == null && ribbonBaseline.Count == rivers.RiverPaths.Count &&
                ribbonBaseline.All(p => rivers.RiverPaths.TryGetValue(p.Key, out var path) && p.Value.SequenceEqual(path));
            planet.generateHydrologicalLakes = true; rivers.RefreshTinLakeRiverIntegration(); routing = rivers.TinLakeRouting;
            foreach (var n in examples) { Focus(n); Image("10-ribbon-after-lake-" + n.BasinId); Focus(n, true); Image("11-ribbon-after-spill-" + n.BasinId); }
            report.AppendLine($"lakesOffBaselineUnchanged={disabledUnchanged} ribbonVisibleReaches={rivers.VisibleRiverReaches} ribbonVertices={rivers.RiverMeshVertexCount}");
            if (!disabledUnchanged) throw new InvalidOperationException("Lakes OFF changed baseline paths.");
            foreach (string line in routing.Details()) report.AppendLine(line);
            report.AppendLine(GeodesicTinBasinInspection.UnmatchedClosedReport(rivers.TinLakeBasins, rivers.TinDrainage));
            if (routing.FlowConservationErrors != 0 || mismatches != 0) throw new InvalidOperationException("Routing conservation or untouched-path checks failed.");
        }
        finally
        {
            inspector.HideLakeComparison(); planet.generateHydrologicalLakes = true;
            if (rivers.integrateTinLakeRivers != enabled || rivers.drawRawTrianglePaths != rawMode)
            { rivers.integrateTinLakeRivers = enabled; rivers.drawRawTrianglePaths = rawMode; rivers.RefreshTinLakeRiverIntegration(); }
            Time.timeScale = timeScale; Object.DestroyImmediate(cameraObject);
            string after = State(planet, rivers);
            report.AppendLine("before=" + before).AppendLine("after=" + after).AppendLine("RawHydrologyTerrainOceanLakeGeometryUnchanged=" + (before == after)).AppendLine("PlanetSettingsRestored=" + (settings == EditorJsonUtility.ToJson(planet)));
            File.WriteAllText(Path.Combine(directory, "report.txt"), report.ToString()); Debug.Log("[GeodesicTinLakeRouting] Capture: " + directory + "\n" + report);
            if (before != after || settings != EditorJsonUtility.ToJson(planet)) throw new InvalidOperationException("Capture invariant failure; see report.");
        }
    }
}
