using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Editor-only, opt-in observation of the existing generated planet. Never generates terrain.</summary>
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
    private (int Network, string Terminal, double Runoff)[] rankedNetworks =
        Array.Empty<(int, string, double)>();
    private GeodesicRiverSystem rivers;
    private PlanetGenerator planet;
    private string status = "Overlay OFF. Build reads the existing generated planet.";
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
        EditorGUILayout.Space();
        if (GUILayout.Button("Load ranked inland river networks")) Try(Load);
        if (rankedNetworks.Length > 0)
        {
            var ranks = rankedNetworks.Take(topN).ToArray();
            int current = Array.FindIndex(ranks, r => r.Network == networkIndex);
            var labels = new[] { "Select by independent catchment runoff" }.Concat(ranks.Select((r, i) =>
                $"{i + 1}. Network {r.Network}, {r.Terminal}, runoff {r.Runoff:G6}")).ToArray();
            int selected = EditorGUILayout.Popup("Ranked inland rivers", current + 1, labels);
            if (selected > 0 && selected != current + 1) networkIndex = ranks[selected - 1].Network;
        }
        if (GUILayout.Button("Inspect network and show raw downstream arrows")) Try(ShowNetwork);
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
        rankedNetworks = RankClosedNetworks(rivers.DrainageDelivery);
        status = topology.Summary();
    }
    private (int Network, string Terminal, double Runoff)[] RankClosedNetworks(GeodesicDrainageDelivery delivery)
    {
        if (delivery == null) return Array.Empty<(int, string, double)>();
        var representative = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int id = 0; id < rivers.TinDrainage.Networks.Count; id++)
        {
            var outcome = delivery.RiverOutcome("network:" + id);
            if (outcome.Destination == GeodesicDrainageDelivery.Destination.InlandClosed)
                representative.TryAdd(outcome.TerminalId, id);
        }
        return delivery.DisconnectedCatchments.Where(c => representative.ContainsKey(c.Terminal.TerminalId))
            .OrderByDescending(c => c.Runoff).ThenBy(c => c.Terminal.TerminalId, StringComparer.Ordinal)
            .Select(c => (representative[c.Terminal.TerminalId], c.Terminal.TerminalId, c.Runoff)).ToArray();
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
        var tin = rivers.TinDrainage;
        var network = tin.Networks[networkIndex];
        var path = tin.TraceLakeOutlet(tin.Centroid(network.SourceTriangle), null, out var rawEnd);
        var outcome = rivers.DrainageDelivery?.RiverOutcome("network:" + networkIndex);
        var report = new StringBuilder($"Network {networkIndex}, source facet {network.SourceTriangle}\n" +
            $"Final destination {outcome?.RiverColour.ToString() ?? "unavailable"}, terminal {outcome?.TerminalId}\n" +
            $"Raw TIN termination {rawEnd} before lake and corridor routing\n" +
            "Raw downstream samples (facet, barycentrics, visible radius)\n");
        foreach (var p in path)
            report.AppendLine(FormattableString.Invariant($"{p.Triangle}\t{p.A:R}\t{p.B:R}\t{p.C:R}\t{tin.CompletedVisibleRadius(p):R}"));
        networkReport = report.ToString();
        Show(Array.Empty<TinBasinDescriptor>(), path[path.Length - 1], path);
        status = $"Network {networkIndex}. Cyan arrows show the raw TIN trace before lake and corridor routing.";
        Focus(path[0], Vector3.Distance(Surface(path[0]), Surface(path[path.Length - 1])));
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

    private void Show(IEnumerable<TinBasinDescriptor> selection, TinPoint? termination = null, TinPoint[] path = null)
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
}
