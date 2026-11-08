using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

internal sealed class RiverOutletInspector : EditorWindow
{
    private PlanetGenerator planet;
    private GeodesicRiverSystem rivers;
    private GeodesicRiverOutletAudit audit;
    private Dictionary<string, Vector3[][]> paths;
    private string route = "network:0", maskReport;
    private Vector2 scroll;
    private bool pick, showOriginal = true;

    [MenuItem("Tools/Hydrology/River Outlet Inspector")]
    private static void Open() => GetWindow<RiverOutletInspector>("River Outlets");
    private void OnEnable() => SceneView.duringSceneGui += DrawScene;
    private void OnDisable() => SceneView.duringSceneGui -= DrawScene;
    private bool Loaded => rivers != null && audit != null && audit == rivers.OutletAudit;

    private void OnGUI()
    {
        if (GUILayout.Button("Load generated planet")) Load();
        if (!Loaded)
        {
            EditorGUILayout.HelpBox("Generate the planet with rivers enabled, then load its current geometry.", MessageType.Info);
            return;
        }
        EditorGUILayout.HelpBox(audit.Summary, MessageType.Info);
        pick = EditorGUILayout.ToggleLeft("Shift-click a river or terrain in the Scene View", pick);
        showOriginal = EditorGUILayout.ToggleLeft("Show original TIN flow", showOriginal);
        route = EditorGUILayout.TextField("Route ID", route);
        if (GUILayout.Button("Frame selected route")) Frame(route);
        if (maskReport != null) EditorGUILayout.HelpBox(maskReport, MessageType.None);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (audit.Results.TryGetValue(route, out var selected))
        {
            string report = Describe(selected);
            if (GUILayout.Button("Copy route report")) EditorGUIUtility.systemCopyBuffer = report;
            EditorGUILayout.HelpBox(report, MessageType.None);
        }
        foreach (var gap in Gaps())
            if (GUILayout.Button($"{gap.Route} → {gap.Receiver}   {gap.Distance:G4}   {gap.Reason}"))
            { route = gap.Route; Frame(route); }
        EditorGUILayout.EndScrollView();
        if (GUI.changed) SceneView.RepaintAll();
    }

    private void Load()
    {
        planet = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None)
            .FirstOrDefault(p => p.GetComponent<GeodesicRiverSystem>()?.OutletAudit != null);
        rivers = planet == null ? null : planet.GetComponent<GeodesicRiverSystem>();
        audit = rivers == null ? null : rivers.OutletAudit;
        paths = audit?.EmittedRoutes.ToDictionary(r => r, r => audit.Centerlines(r).ToArray());
        SceneView.RepaintAll();
    }

    private string Describe(GeodesicRiverOutletAudit.Result result)
    {
        var tin = rivers.TinDrainage;
        var chain = Downstream(result.Route).ToArray();
        var end = audit.Links[chain[chain.Length - 1]].End;
        int corridor = chain.Where(r => r.StartsWith("corridor:", StringComparison.Ordinal)).Select(Id).DefaultIfEmpty(-1).First();
        int lake = chain.Where(r => r.StartsWith("lake:", StringComparison.Ordinal)).Select(Id).DefaultIfEmpty(-1).First();
        int basin = corridor >= 0 ? corridor : end.Basin?.Id ?? (end.TerminalVertex >= 0 ? end.TerminalVertex : lake);
        string destination = end.Destination == GeodesicDrainageDelivery.Destination.InlandClosed
            ? (lake >= 0 && !rivers.OutletIsSedimentLake(lake) ? "retained lake in a closed inland basin" : "closed inland basin")
            : end.Destination == GeodesicDrainageDelivery.Destination.Unresolved ? end.Type
            : end.OceanComponent < 0 ? "retained ocean (component unattributed)"
            : end.OceanComponent == planet.OceanConnectivity?.PrimaryRetainedComponent ? "primary ocean" : "secondary sea";
        string kind = corridor >= 0 ? "accepted sediment corridor" :
            lake >= 0 && rivers.OutletIsSedimentLake(lake) ? "converted lake crossing" : "ordinary drainage";
        bool closed = end.Destination == GeodesicDrainageDelivery.Destination.InlandClosed;
        string details = closed ? $"Terminal {end.TerminalId}   closed runoff {audit.ClosedRunoff(end):G8}\n" : "";
        if (rivers.SedimentCorridors != null && (corridor >= 0 || closed))
        {
            string decision = closed && end.Basin == null && end.TerminalVertex < 0
                ? "closed terminal has no basin descriptor or exact shore vertex" : rivers.SedimentCorridors.DecisionFor(basin);
            details += $"Corridor decision {decision}\n";
            if (closed && end.Point.HasValue)
            {
                float distance = rivers.SedimentCorridors.NearestReceiverDistance(tin.Direction(end.Point.Value),
                    IcosphereRenderGeometryCache.GetOrBuild(tin.SubdivisionLevel).UnitVertices, planet.BasePlanetRadius);
                details += $"Nearest eligible receiver, straight-line lower bound {distance:G6}\n";
            }
        }
        string surfaces = result.End.HasValue ? Surface(result.End.Value.normalized) : "No emitted endpoint";
        return $"{result.Route}   basin {basin}   {kind}\nDestination {destination}   component {end.OceanComponent}\n" +
            $"{result.State}   {result.Reason}\nReceiver {result.Receiver}\n" +
            $"Last emitted {Position(result.End)}   expected {Position(result.Expected)}   gap {result.Distance:G6}\n" +
            $"Logical connection {Position(result.Logical)}\n" +
            $"{surfaces}\n{details}Downstream {string.Join(" → ", chain)}";
    }

    private string Surface(Vector3 direction)
    {
        int cell = GeodesicOceanConnectivity.FindNearestCell(planet.GeodesicTopology, direction);
        var collider = planet.GetComponent<MeshCollider>();
        var mesh = planet.GetComponent<MeshFilter>();
        string contact = "unavailable";
        if (collider != null)
        {
            Vector3 outside = World(direction * (planet.BasePlanetRadius * 3f));
            Vector3 center = planet.transform.position;
            if (collider.Raycast(new Ray(outside, center - outside), out var hit, Vector3.Distance(outside, center)))
                contact = planet.transform.InverseTransformPoint(hit.point).magnitude.ToString("G8");
        }
        string oceanFacet = planet.RiverShoreline != null && planet.RiverShoreline.TryWaterRadius(direction, -1, out float water)
            ? water.ToString("G8") : "no ocean facet";
        return $"Original terrain {rivers.OutletOriginalRadius(direction):G8}   corridor terrain {rivers.OutletRenderRadius(direction):G8}   collider {contact}\n" +
            $"Render and collider share mesh {collider != null && mesh != null && mesh.sharedMesh == collider.sharedMesh}\n" +
            $"Sea level {planet.GeodesicSeaLevelRadius:G8}   rendered ocean {oceanFacet}   L{planet.GeodesicTopology.SubdivisionLevel} cell {cell} " +
            $"{planet.GetGeodesicCellOceanClassification(cell)}   component {planet.OceanConnectivity?.ComponentId[cell]}";
    }

    private IEnumerable<GeodesicRiverOutletAudit.Result> Gaps() => audit.Results.Values
        .Where(r => r.State == GeodesicRiverOutletAudit.Status.Gap).OrderByDescending(r => r.Distance).Take(15);

    private IEnumerable<string> Downstream(string start)
    {
        var seen = new HashSet<string>();
        while (start != null && audit.Links.TryGetValue(start, out var link) && seen.Add(start))
        { yield return start; start = link.Next; }
    }

    private IEnumerable<string> Upstream(string start)
    {
        var incoming = audit.Links.Where(p => p.Value.Next != null).ToLookup(p => p.Value.Next, p => p.Key);
        var visited = new HashSet<string> { start };
        var queue = new Queue<string>();
        queue.Enqueue(start);
        while (queue.Count > 0)
            foreach (string parent in incoming[queue.Dequeue()])
                if (visited.Add(parent)) { queue.Enqueue(parent); yield return parent; }
    }

    private void DrawScene(SceneView view)
    {
        if (!Loaded) return;
        var e = Event.current;
        if (pick && e.shift && e.type == EventType.MouseDown && e.button == 0)
        {
            Pick(e.mousePosition);
            e.Use();
            Repaint();
        }
        if (e.type != EventType.Repaint) return;
        var previousDepth = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
        foreach (var gap in Gaps())
            if (gap.End.HasValue) Marker(gap.End.Value, Color.red);
        foreach (string id in Upstream(route)) Draw(id, Color.yellow);
        foreach (string id in Downstream(route)) Draw(id, id == route ? Color.cyan : Color.green);
        if (audit.Results.TryGetValue(route, out var result))
        {
            if (result.End.HasValue) Marker(result.End.Value, Color.cyan);
            if (result.Expected.HasValue) Marker(result.Expected.Value, Color.magenta);
            if (showOriginal && route.StartsWith("network:", StringComparison.Ordinal))
            {
                var raw = rivers.TinDrainage.Networks[Id(route)].Centreline;
                Handles.color = new Color(1f, .55f, .15f);
                if (raw.Length >= 2)
                    Handles.DrawAAPolyLine(2f, raw.Select(d => World(d * (rivers.OutletOriginalRadius(d) + .001f))).ToArray());
            }
        }
        Handles.zTest = previousDepth;
    }

    private void Pick(Vector2 position)
    {
        var collider = planet.GetComponent<MeshCollider>();
        if (collider == null || !collider.Raycast(HandleUtility.GUIPointToWorldRay(position), out var hit, float.MaxValue)) return;
        maskReport = Surface(planet.transform.InverseTransformPoint(hit.point).normalized);
        float best = 9f;
        float range = HandleUtility.GetHandleSize(hit.point) * .5f;
        foreach (var entry in paths)
            foreach (var path in entry.Value)
                for (int i = 1; i < path.Length; i++)
                {
                    Vector3 a = World(path[i - 1]), b = World(path[i]);
                    if ((GeodesicRiverOutletAudit.ClosestOnSegment(hit.point, a, b) - hit.point).sqrMagnitude > range * range) continue;
                    float distance = HandleUtility.DistanceToLine(a, b);
                    if (distance >= best) continue;
                    best = distance;
                    route = entry.Key.StartsWith("bed:", StringComparison.Ordinal) ? "lake:" + Id(entry.Key) : entry.Key;
                }
    }

    private void Draw(string id, Color color)
    {
        Handles.color = color;
        if (paths.TryGetValue(id, out var routes))
            foreach (var path in routes) Handles.DrawAAPolyLine(3f, path.Select(World).ToArray());
        if (id.StartsWith("lake:", StringComparison.Ordinal) && paths.TryGetValue("bed:" + Id(id), out routes))
            foreach (var path in routes) Handles.DrawAAPolyLine(3f, path.Select(World).ToArray());
    }

    private void Frame(string id)
    {
        if (!audit.Results.TryGetValue(id, out var item)) return;
        Vector3? focus = item.End ?? item.Expected;
        if (focus.HasValue) SceneView.lastActiveSceneView?.Frame(new Bounds(World(focus.Value),
            Vector3.one * planet.BasePlanetRadius * .08f), false);
    }

    private Vector3 World(Vector3 local) => planet.transform.TransformPoint(local);
    private void Marker(Vector3 local, Color color)
    {
        Vector3 point = World(local + local.normalized * .001f);
        Handles.color = color;
        Handles.SphereHandleCap(0, point, Quaternion.identity, HandleUtility.GetHandleSize(point) * .055f, EventType.Repaint);
    }
    private static int Id(string value) => int.Parse(value.Substring(value.IndexOf(':') + 1));
    private static string Position(Vector3? point) => point?.ToString("F6") ?? "unavailable";
}
