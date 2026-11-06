using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Editor-only, opt-in observation of the existing generated planet. Never generates terrain.</summary>
[InitializeOnLoad]
public sealed class TinBasinsLiveWindow : EditorWindow
{
    private TinBasinDebugSelection mode;
    private int basinId, topN = 10, networkIndex;
    private string networkReport;
    private Vector2 networkScroll;
    private GameObject overlay;
    private Mesh mesh;
    private Material material;
    private GeodesicTinBasins topology;
    private GeodesicRiverSystem rivers;
    private PlanetGenerator planet;
    private string status = "Overlay OFF. Build reads the existing generated planet.";
    private static readonly string Request = "Library/TinBasinsLive.request";
    static TinBasinsLiveWindow() { EditorApplication.update += PollRequest; }
    private static void PollRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request);
        try { GetWindow<TinBasinsLiveWindow>("TIN Basin Debug").Capture(); }
        catch (Exception e)
        {
            Directory.CreateDirectory("Artifacts/TinBasinsLive");
            File.WriteAllText("Artifacts/TinBasinsLive/request-error.txt", e.ToString()); Debug.LogException(e);
        }
    }
    [MenuItem("Tools/Hydrology/TIN Basin Live Inspector")]
    public static void Open() => GetWindow<TinBasinsLiveWindow>("TIN Basin Debug");
    public void ShowLakeComparison(int id) { Load(); Show(topology.Basins.Where(b => b.Id == id)); }
    public void HideLakeComparison() => Clear();
    private void OnDisable() => Clear();
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Topology wireframe only, NOT water. Green=candidate, orange=depth, yellow=area, red=both, magenta=terminal, gray=subsea, cyan=raw. White=floor, pink=spill, blue=downstream. Parent links are purple.", MessageType.Info);
        mode = (TinBasinDebugSelection)EditorGUILayout.EnumPopup("Selection", mode);
        basinId = EditorGUILayout.IntField("Floor vertex / basin ID", basinId);
        networkIndex = EditorGUILayout.IntField("Network index", networkIndex);
        topN = EditorGUILayout.IntSlider("Top N", topN, 1, 50);
        if (GUILayout.Button("Build / Show selected overlay")) Try(() => { Load(); Show(GeodesicTinBasinInspection.Select(topology, rivers.TinDrainage, mode, basinId, topN, networkIndex)); });
        if (GUILayout.Button("Overlay OFF")) Clear();
        if (GUILayout.Button("Log selected basin and matching terminations")) Try(() => { Load(); Debug.Log(GeodesicTinBasinInspection.SelectedReport(topology, rivers.TinDrainage, basinId)); });
        if (GUILayout.Button("Capture live acceptance set A–F")) Try(Capture);
        EditorGUILayout.Space();
        if (GUILayout.Button("Load ranked inland river networks")) Try(Load);
        if (rivers != null && rivers.RankedInlandNetworks.Count > 0)
        {
            var ranks = rivers.RankedInlandNetworks.Take(topN).ToArray();
            int current = Array.FindIndex(ranks, r => r.Network == networkIndex);
            var labels = new[] { "Select by independent catchment runoff" }.Concat(ranks.Select((r, i) =>
                $"{i + 1}. Network {r.Network}, basin {r.Basin}, runoff {r.Runoff:G6}")).ToArray();
            int selected = EditorGUILayout.Popup("Ranked inland rivers", current + 1, labels);
            if (selected > 0 && selected != current + 1) networkIndex = ranks[selected - 1].Network;
        }
        if (GUILayout.Button("Inspect network and show downstream arrows")) Try(ShowNetwork);
        if (GUILayout.Button("Inspect outlet attempt for basin ID")) Try(ShowOutletAttempt);
        if (!string.IsNullOrEmpty(networkReport))
        {
            if (GUILayout.Button("Copy exact TIN path and report")) EditorGUIUtility.systemCopyBuffer = networkReport;
            networkScroll = EditorGUILayout.BeginScrollView(networkScroll, GUILayout.MinHeight(100));
            EditorGUILayout.TextArea(networkReport);
            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.HelpBox(status, MessageType.None);
    }
    private void Try(Action action) { try { action(); } catch (Exception e) { status = e.Message; Debug.LogException(e); } }
    private void Load()
    {
        planet = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsPlanetInitialized && p.CurrentGridType == PlanetGridType.GeodesicIcosphere);
        if (planet == null) throw new InvalidOperationException("No live generated Geodesic planet. Start the normal scene with seed 123456 / simulation 6 / render 7 / Visible / river threshold 2, then capture. No defaults were substituted.");
        rivers = planet.GetComponent<GeodesicRiverSystem>();
        if (rivers == null || rivers.TinDrainage == null) throw new InvalidOperationException("The current planet has no active TIN hydrology.");
        topology = GeodesicTinBasins.Build(rivers.TinDrainage, planet.minimumLakeBasinAreaFraction, planet.minimumLakeDepth);
        status = topology.Summary();
    }
    private void Clear()
    {
        if (overlay != null) DestroyImmediate(overlay);
        if (mesh != null) DestroyImmediate(mesh);
        if (material != null) DestroyImmediate(material);
        overlay = null; mesh = null; material = null;
    }
    private Vector3 Surface(TinPoint p)
    {
        var tin = rivers.TinDrainage;
        return tin.Direction(p) * ((float)tin.CompletedVisibleRadius(p) + planet.BasePlanetRadius * .00008f);
    }
    private static Color StatusColor(TinBasinDescriptor b) => b.Terminal ? Color.magenta : b.SubseaUnclassified ? Color.gray :
        b.QualifiesForVisibleLake ? Color.green : b.BelowMinimumArea && b.BelowMinimumDepth ? Color.red :
        b.BelowMinimumDepth ? new Color(1f, .45f, 0f) : b.BelowMinimumArea ? Color.yellow : Color.cyan;
    private void ShowNetwork()
    {
        Load();
        if (networkIndex < 0 || networkIndex >= rivers.TinDrainage.Networks.Count)
            throw new ArgumentOutOfRangeException(nameof(networkIndex), "Select an existing network index.");
        networkReport = rivers.InspectTinNetwork(networkIndex, out var path);
        Show(Array.Empty<TinBasinDescriptor>(), path[path.Length - 1], path);
        status = $"Network {networkIndex}. White cross at source, cyan arrows downstream and cyan cross at terminal.";
        Focus(path[0], Vector3.Distance(Surface(path[0]), Surface(path[path.Length - 1])));
    }

    private void ShowOutletAttempt()
    {
        Load();
        var selected = topology.Basins.FirstOrDefault(b => b.Id == basinId);
        networkReport = rivers.InspectTinOutletAttempt(basinId, out var path, out var attempt);
        Show(selected == null ? Array.Empty<TinBasinDescriptor>() : new[] { selected },
            path.Length == 0 ? (TinPoint?)null : path[path.Length - 1], path, attempt);
        status = "Cyan is live flow. Yellow ticks mark the remaining frontier, yellow lines mark the closest unverified receiver hint, orange is the candidate, green is its existing receiver, and magenta marks the verification failure.";
        if (selected != null)
        {
            var search = attempt?.Search;
            float extent = search == null ? 0f : search.ExpandedVertices.Concat(search.CandidateVertices)
                .Select(v => Vector3.Distance(Surface(selected.Floor), Surface(VertexPoint(v)))).DefaultIfEmpty(0f).Max();
            Focus(selected.Floor, extent);
        }
    }

    private TinPoint VertexPoint(int vertex)
    {
        var tin = rivers.TinDrainage;
        return tin.BasinPoint(tin.BasinIncidentTriangle(tin.BasinIncidentStart(vertex)), vertex);
    }

    private void Focus(TinPoint point, float extent)
    {
        var view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            Vector3 start = Surface(point);
            Vector3 focus = planet.transform.TransformPoint(start);
            Vector3 outward = planet.transform.TransformDirection(start.normalized);
            Vector3 up = Mathf.Abs(Vector3.Dot(outward, Vector3.up)) > .95f ? Vector3.forward : Vector3.up;
            view.LookAt(focus, Quaternion.LookRotation(-outward, up),
                Mathf.Max(planet.BasePlanetRadius * .025f, extent * 1.2f));
        }
    }

    private void Show(IEnumerable<TinBasinDescriptor> selection, TinPoint? termination = null, TinPoint[] path = null,
        GeodesicValleyCarving.MajorOutletAttempt attempt = null)
    {
        Clear(); var vertices = new List<Vector3>(); var colors = new List<Color>();
        void Line(Vector3 a, Vector3 b, Color c) { vertices.Add(a); vertices.Add(b); colors.Add(c); colors.Add(c); }
        void Mark(Vector3 p, Color color)
        {
            float size = planet.BasePlanetRadius * .002f;
            Vector3 up = p.normalized, right = Vector3.Cross(up, Math.Abs(up.y) < .9f ? Vector3.up : Vector3.right).normalized * size;
            Vector3 forward = Vector3.Cross(up, right);
            Line(p - right, p + right, color); Line(p - forward, p + forward, color); Line(p, p + up * size * 2, color);
        }
        foreach (var b in selection)
        {
            Color color = StatusColor(b);
            foreach (int t in b.MemberTriangles)
            {
                var a = Surface(new TinPoint(t, 1, 0, 0)); var c = Surface(new TinPoint(t, 0, 1, 0)); var d = Surface(new TinPoint(t, 0, 0, 1));
                Line(a, c, color); Line(c, d, color); Line(d, a, color);
            }
            Mark(Surface(b.Floor), Color.white);
            if (b.Spill.HasValue)
            {
                Vector3 spill = Surface(b.Spill.Value); Mark(spill, new Color(1f, .3f, .8f));
                if (b.DownstreamTriangle >= 0) { Vector3 side = Surface(rivers.TinDrainage.Centroid(b.DownstreamTriangle)); Line(spill, side, Color.blue); Mark(side, Color.blue); }
            }
            var parent = topology.Basins.FirstOrDefault(p => p.Id == b.ParentBasinId);
            if (parent != null && b.Spill.HasValue) Line(Surface(b.Spill.Value), Surface(parent.Floor), new Color(.6f, .2f, 1f));
        }
        if (path != null && path.Length > 0)
        {
            Mark(Surface(path[0]), Color.white);
            float spacing = planet.BasePlanetRadius * .008f, sinceArrow = spacing;
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 a = Surface(path[i - 1]), b = Surface(path[i]);
                float distance = Vector3.Distance(a, b);
                if (distance <= 1e-8f) continue;
                Line(a, b, Color.cyan);
                sinceArrow += distance;
                if (sinceArrow < spacing) continue;
                Vector3 middle = (a + b) * .5f;
                Vector3 forward = (b - a).normalized * (spacing * .28f);
                Vector3 side = Vector3.Cross(middle.normalized, forward) * .6f;
                Line(middle + forward, middle - forward + side, Color.cyan);
                Line(middle + forward, middle - forward - side, Color.cyan);
                sinceArrow = 0f;
            }
        }
        if (termination.HasValue) Mark(Surface(termination.Value), Color.cyan);
        if (attempt != null)
        {
            var search = attempt.Search;
            foreach (int vertex in search.FrontierVertices)
            {
                Vector3 point = Surface(VertexPoint(vertex));
                Line(point, point + point.normalized * (planet.BasePlanetRadius * .001f), Color.yellow);
            }
            void DrawPath(TinPoint[] points, Color color)
            {
                for (int i = 1; i < points.Length; i++) Line(Surface(points[i - 1]), Surface(points[i]), color);
            }
            DrawPath(search.HintPath.Select(VertexPoint).ToArray(), Color.yellow);
            DrawPath(search.CandidateVertices.Select(VertexPoint).ToArray(), new Color(1f, .45f, 0f));
            DrawPath(search.ReceiverPath, Color.green);
            if (attempt.TraceFailure != null)
            {
                var failure = attempt.TraceFailure;
                DrawPath(failure.Trace, new Color(.75f, .45f, .9f));
                if (failure.HasSegment) Line(Surface(failure.Start), Surface(failure.End), Color.magenta);
                Mark(Surface(failure.HasSegment ? failure.End : failure.Terminal), Color.magenta);
            }
        }
        overlay = new GameObject("TIN topology DEBUG (not water)") { hideFlags = HideFlags.HideAndDontSave };
        overlay.transform.SetParent(planet.transform, false);
        mesh = new Mesh { name = "TIN basin debug lines", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetIndices(Enumerable.Range(0, vertices.Count).ToArray(), MeshTopology.Lines, 0); mesh.RecalculateBounds();
        var shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null) throw new InvalidOperationException("Debug vertex-color shader unavailable.");
        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.SetInt("_ZWrite", 0); material.SetInt("_ZTest", (int)CompareFunction.LessEqual); material.SetInt("_Cull", (int)CullMode.Off);
        overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = overlay.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        SceneView.RepaintAll();
    }

    private string Fingerprint()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            var tin = rivers.TinDrainage;
            writer.Write(EditorJsonUtility.ToJson(planet)); writer.Write(EditorJsonUtility.ToJson(rivers));
            foreach (float x in tin.VisibleHeight) writer.Write(x); foreach (float x in tin.HydrologyHeight) writer.Write(x);
            foreach (int x in tin.TriangleReceiver) writer.Write(x); foreach (double x in tin.AccumulatedRunoff) writer.Write(x); foreach (double x in tin.DrainageArea) writer.Write(x);
            void V(Vector3 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
            for (int t = 0; t < tin.TriangleCount; t++)
            { writer.Write(tin.IsOcean(tin.Centroid(t))); for (int k = 0; k < 3; k++) writer.Write(tin.IsOcean(new TinPoint(t, k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0))); }
            writer.Write(tin.Diagnostics.Sources); writer.Write(tin.Networks.Count);
            foreach (var n in tin.Networks)
            {
                writer.Write(n.SourceTriangle); writer.Write(n.JunctionNetwork); writer.Write((int)n.Termination); writer.Write(n.Length);
                foreach (var p in n.Samples) { writer.Write(p.Triangle); writer.Write(p.A); writer.Write(p.B); writer.Write(p.C); }
                foreach (var p in n.Centreline) V(p);
            }
            foreach (var filter in planet.GetComponentsInChildren<MeshFilter>(true).Where(f => overlay == null || f.gameObject != overlay).OrderBy(f => f.GetInstanceID()))
            {
                var m = filter.sharedMesh; if (m == null || !m.isReadable) continue;
                writer.Write(filter.GetInstanceID()); foreach (var v in m.vertices) V(v);
                foreach (var color in m.colors) { writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a); }
                for (int sub = 0; sub < m.subMeshCount; sub++)
                { writer.Write((int)m.GetTopology(sub)); foreach (int i in m.GetIndices(sub)) writer.Write(i); }
            }
            writer.Flush(); using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream.ToArray()));
        }
    }

    public void Capture()
    {
        Clear(); Load();
        if (planet.randomSeed != 123456 || planet.GeodesicTopology.SubdivisionLevel != 6 || planet.geodesicRenderSubdivisionLevel != 7 ||
            planet.hydrologyTerrainMode != HydrologyTerrainMode.Visible || rivers.riverFlowThreshold != 2 || rivers.LakeMeshVertexCount != 0)
            throw new InvalidOperationException($"Live settings mismatch: seed={planet.randomSeed} sim={planet.GeodesicTopology.SubdivisionLevel} render={planet.geodesicRenderSubdivisionLevel} input={planet.hydrologyTerrainMode} threshold={rivers.riverFlowThreshold} lakeSetting={planet.generateHydrologicalLakes} lakeMeshVertices={rivers.LakeMeshVertexCount}. Capture refused; no settings changed.");
        if (Camera.main == null) throw new InvalidOperationException("Live main camera required.");
        string directory = Path.GetFullPath("Artifacts/TinBasinsLive/" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "planet-settings.json"), EditorJsonUtility.ToJson(planet, true));
        File.WriteAllText(Path.Combine(directory, "river-settings.json"), EditorJsonUtility.ToJson(rivers, true));
        string before = Fingerprint(); var report = new StringBuilder(GeodesicTinBasinInspection.Report(topology, rivers.TinDrainage));
        report.AppendLine($"seed={planet.randomSeed} sim={planet.GeodesicTopology.SubdivisionLevel} render={planet.geodesicRenderSubdivisionLevel} input={planet.hydrologyTerrainMode} threshold={rivers.riverFlowThreshold} lakeSetting={planet.generateHydrologicalLakes} lakeMeshVertices={rivers.LakeMeshVertexCount} minAreaFraction={planet.minimumLakeBasinAreaFraction:R} minDepth={planet.minimumLakeDepth:R}");
        var cameraObject = new GameObject("TIN debug capture camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
        camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
        void Image(string name) => RiverUphillDebugCapture.Image(camera, Path.Combine(directory, name + ".png"));
        void Focus(TinBasinDescriptor b)
        {
            Vector3 floor = planet.transform.TransformPoint(Surface(b.Floor));
            Vector3 normal = (floor - planet.transform.position).normalized;
            float span = Mathf.Max(planet.BasePlanetRadius * .02f, Mathf.Sqrt((float)b.Area) * 1.4f);
            camera.transform.position = floor + normal * span * 2f; camera.transform.LookAt(floor, Vector3.Cross(normal, Vector3.right).normalized);
            camera.nearClipPlane = .001f;
        }
        try
        {
            Image("A-normal");
            foreach (var item in new[] { (TinBasinDebugSelection.Qualifying, "B-qualifying"), (TinBasinDebugSelection.Largest, "C-largest"), (TinBasinDebugSelection.Deepest, "D-deepest") })
            {
                var selected = GeodesicTinBasinInspection.Select(topology, rivers.TinDrainage, item.Item1, basinId, 10, networkIndex);
                Show(selected); Image(item.Item2);
                foreach (var b in selected.Take(10)) { Focus(b); Image(item.Item2 + "-basin-" + b.Id); report.AppendLine(GeodesicTinBasinInspection.GeometryChecks(rivers.TinDrainage, b)); }
                camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
            }
            var chosen = topology.Basins.FirstOrDefault(b => b.Id == basinId) ?? topology.Basins.OrderByDescending(b => b.Area).FirstOrDefault();
            if (chosen != null) { Show(new[] { chosen }); Focus(chosen); Image("E-selected-" + chosen.Id); report.AppendLine(GeodesicTinBasinInspection.SelectedReport(topology, rivers.TinDrainage, chosen.Id)); }
            for (int i = 0; i < rivers.TinDrainage.Networks.Count; i++)
            {
                var selected = GeodesicTinBasinInspection.Select(topology, rivers.TinDrainage, TinBasinDebugSelection.ClosedTermination, 0, 1, i);
                if (selected.Length == 0) continue;
                Show(selected, rivers.TinDrainage.Networks[i].Samples.Last()); Focus(selected[0]); Image("F-closed-termination-network-" + i); report.AppendLine(GeodesicTinBasinInspection.SelectedReport(topology, rivers.TinDrainage, selected[0].Id)); break;
            }
            Clear(); string after = Fingerprint(); report.AppendLine("stateBefore=" + before).AppendLine("stateAfter=" + after).AppendLine("ExactObservedStateUnchanged=" + (before == after));
            report.AppendLine("Screenshots require visual inspection; topology debug is not water. No settings were changed.");
            File.WriteAllText(Path.Combine(directory, "report.txt"), report.ToString());
            if (before != after) throw new InvalidOperationException("Observed terrain/hydrology/river/settings state changed during capture.");
            Debug.Log(report.ToString()); status = "Captured: " + directory;
        }
        finally { Clear(); DestroyImmediate(cameraObject); }
    }
}
