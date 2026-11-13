using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Presentation of the original lake footprints and connections on level terrain.</summary>
public sealed class GeodesicTinLakeSediment
{
    internal readonly Dictionary<int, Footprint> Lakes = new Dictionary<int, Footprint>();
    private readonly Dictionary<int, Footprint> byFacet = new Dictionary<int, Footprint>();
    private readonly GeodesicTinHydrology tin;
    public IReadOnlyDictionary<int, string> GeometryFailures { get; }
    public int LakeCount => Lakes.Count;

    public GeodesicTinLakeSediment(GeodesicTinHydrology tin, GeodesicTinLakeGeometry original, GeodesicTinLakeGeometry visible)
    {
        this.tin = tin;
        var failures = new Dictionary<int, string>();
        GeometryFailures = failures;
        if (original == visible) return;
        var retained = new HashSet<int>(visible.Bodies.Select(b => b.BasinId));
        var protectedFacets = new HashSet<int>(visible.TerrainPoints.Select(p => p.Triangle));
        var candidates = original.Bodies.Where(b => !retained.Contains(b.BasinId))
            .Select(b => new Footprint(tin, original, b)).ToArray();
        var shared = new HashSet<int>(candidates.SelectMany(b => b.Facets.Keys).GroupBy(t => t)
            .Where(g => g.Count() > 1).Select(g => g.Key));
        foreach (var lake in candidates)
        {
            if (!lake.HasChart)
                failures.Add(lake.Id, "original footprint spans beyond the local projection");
            else if (lake.Facets.Keys.Any(protectedFacets.Contains))
                failures.Add(lake.Id, "original footprint overlaps retained lake terrain");
            else if (lake.Facets.Keys.Any(shared.Contains))
                failures.Add(lake.Id, "original footprint overlaps another sediment footprint");
            else
            {
                Lakes.Add(lake.Id, lake);
                foreach (int facet in lake.Facets.Keys) byFacet.Add(facet, lake);
            }
        }
    }

    internal float? Level(int lake, TinPoint point) => Lakes.TryGetValue(lake, out var bed) &&
        bed.Contains(tin.Direction(point)) ? (float?)bed.Level : null;

    // Replace only the wet portion of a facet. Keep the dry polygon on the original
    // physical plane, even when the hydrology input height differs from visible terrain.
    // A wall closes the rim wherever the original physical and sediment planes differ.
    internal Mesh BuildSurface(Mesh original)
    {
        if (byFacet.Count == 0) return null;
        var source = original.vertices;
        var uv = original.uv;
        var sourceColors = original.colors;
        var vertices = new List<Vector3>(source);
        var normals = new List<Vector3>(original.normals);
        var uvs = new List<Vector2>(uv);
        var colors = new List<Color>(sourceColors);
        var indices = new List<int>(tin.TriangleCount * 3);

        int Add(TinPoint point, float? level, Vector3? normal = null)
        {
            int a = tin.BasinVertex(point.Triangle, 0), b = tin.BasinVertex(point.Triangle, 1), c = tin.BasinVertex(point.Triangle, 2);
            // The scalar TIN uses unit directions. Inverse-radius weights locate the
            // same ray on the original physical facet and interpolate its attributes.
            double wa = point.A / source[a].magnitude, wb = point.B / source[b].magnitude, wc = point.C / source[c].magnitude;
            double sum = wa + wb + wc;
            float x = (float)(wa / sum), y = (float)(wb / sum), z = (float)(wc / sum);
            Vector3 direction = tin.Direction(point);
            int index = vertices.Count;
            vertices.Add(level.HasValue ? direction * level.Value : source[a] * x + source[b] * y + source[c] * z);
            normals.Add(normal ?? (level.HasValue ? direction : (normals[a] * x + normals[b] * y + normals[c] * z).normalized));
            if (uv.Length > 0) uvs.Add(uv[a] * x + uv[b] * y + uv[c] * z);
            if (sourceColors.Length > 0) colors.Add(sourceColors[a] * x + sourceColors[b] * y + sourceColors[c] * z);
            return index;
        }
        void Fan(TinPoint[] points, float? level)
        {
            if (points.Length < 3) return;
            int first = vertices.Count;
            foreach (var p in points) Add(p, level);
            for (int i = 1; i + 1 < points.Length; i++)
            { indices.Add(first); indices.Add(first + i); indices.Add(first + i + 1); }
        }

        for (int t = 0; t < tin.TriangleCount; t++)
        {
            if (!byFacet.TryGetValue(t, out var lake))
            {
                for (int k = 0; k < 3; k++) indices.Add(tin.BasinVertex(t, k));
                continue;
            }
            var wet = lake.Facets[t].Points;
            Fan(wet, lake.Level);
            Fan(GeodesicTinLakeGeometry.ClipTriangle(tin, t, lake.Level, wet: false), null);
            for (int i = 0; i < wet.Length; i++)
            {
                var a = wet[i]; var b = wet[(i + 1) % wet.Length];
                int edge = SharedEdge(a, b);
                if (edge >= 0 && lake.Facets.ContainsKey(tin.Neighbors[t * 3 + edge])) continue;
                Vector3 side = Vector3.Cross(tin.Direction(b), tin.Direction(a)).normalized;
                int first = Add(a, lake.Level, side);
                Add(b, lake.Level, side); Add(a, null, side); Add(b, null, side);
                if ((vertices[first] - vertices[first + 2]).sqrMagnitude > 1e-14f)
                { indices.Add(first); indices.Add(first + 2); indices.Add(first + 1); }
                if ((vertices[first + 1] - vertices[first + 3]).sqrMagnitude > 1e-14f)
                { indices.Add(first + 1); indices.Add(first + 2); indices.Add(first + 3); }
            }
        }
        var mesh = new Mesh { name = "Geodesic Terrain with Lake Sediment", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(indices, 0);
        if (uv.Length > 0) mesh.SetUVs(0, uvs);
        if (sourceColors.Length > 0) mesh.SetColors(colors);
        return mesh;
    }

    private static int SharedEdge(TinPoint a, TinPoint b) => a.A == 0 && b.A == 0 ? 0 :
        a.B == 0 && b.B == 0 ? 1 : a.C == 0 && b.C == 0 ? 2 : -1;

    internal sealed class Footprint
    {
        internal readonly int Id;
        internal readonly float Level;
        internal readonly Dictionary<int, Facet> Facets;
        internal readonly bool HasChart;
        private readonly GeodesicTinHydrology tin;
        private readonly Vector3 origin, east, north;
        private readonly Vector2 minimum, maximum;
        private const float Tolerance = 0.0000002f;
        internal long QueryTicks { get; private set; }

        internal Footprint(GeodesicTinHydrology tin, GeodesicTinLakeGeometry geometry, TinLakeBody body)
        {
            this.tin = tin; Id = body.BasinId; Level = (float)body.WaterLevel;
            origin = tin.Direction(geometry.TerrainPoints[body.FirstVertex]);
            if (tin.BasinOnSphere)
            {
                east = Vector3.Cross(Mathf.Abs(origin.y) < .9f ? Vector3.up : Vector3.right, origin).normalized;
                north = Vector3.Cross(origin, east);
            }
            var points = geometry.TerrainPoints.Skip(body.FirstVertex).Take(body.VertexCount);
            HasChart = !tin.BasinOnSphere || points.All(p => Vector3.Dot(tin.Direction(p), origin) > .01f);
            Facets = points.GroupBy(p => p.Triangle)
                .ToDictionary(g => g.Key, g => new Facet(g.ToArray(), g.Select(p => Project(tin.Direction(p))).ToArray()));
            minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var facet in Facets.Values)
            foreach (var point in facet.Polygon)
            { minimum = Vector2.Min(minimum, point); maximum = Vector2.Max(maximum, point); }
        }

        // A gnomonic chart maps great-circle segments onto straight lines. The footprint
        // is a union of the existing convex clipped TIN polygons, including island holes.
        internal Vector2 Project(Vector3 direction)
        {
            if (!tin.BasinOnSphere) return new Vector2(direction.x - origin.x, direction.y - origin.y);
            float scale = Vector3.Dot(direction, origin);
            return new Vector2(Vector3.Dot(direction, east) / scale, Vector3.Dot(direction, north) / scale);
        }
        internal Vector3 Direction(Vector2 point) => tin.BasinOnSphere ? (origin + east * point.x + north * point.y).normalized :
            origin + new Vector3(point.x, point.y, 0);
        internal Vector3 Position(Vector2 point, float offset) => tin.BasinOnSphere ? Direction(point) * (Level + offset) :
            Direction(point) + Vector3.forward * (Level + offset);
        internal bool Contains(Vector3 direction) => (!tin.BasinOnSphere || Vector3.Dot(direction, origin) > 0) &&
            FindFacet(Project(direction)) >= 0;

        private int FindFacet(Vector2 point)
        {
            foreach (var pair in Facets)
                if (pair.Value.Clip(point, point, Tolerance, out _, out _)) return pair.Key;
            return -1;
        }

        internal bool Covers(Vector2 a, Vector2 b)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return CoversCore(a, b); }
            finally { QueryTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started; }
        }
        private bool CoversCore(Vector2 a, Vector2 b)
        {
            var intervals = new List<(float Start, float End)>();
            foreach (var facet in Facets.Values)
                if (facet.Clip(a, b, Tolerance, out float start, out float end)) intervals.Add((start, end));
            intervals.Sort((x, y) => x.Start.CompareTo(y.Start));
            float covered = 0;
            foreach (var interval in intervals)
            {
                if (interval.Start > covered) return false;
                covered = Mathf.Max(covered, interval.End);
                if (covered >= 1) return true;
            }
            return false;
        }

        // Coverage of the complete polygon union, not a per-facet corridor.
        // A ribbon may retain existing shoreline clipping, but not lose more of
        // its actual tapered width than the original course at the same station.
        internal float Coverage(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return CoverageCore(a, b, c, d); }
            finally { QueryTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started; }
        }
        private float CoverageCore(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            double expected = Mathf.Abs(Cross(b - a, c - a)) + Mathf.Abs(Cross(c - a, d - a));
            if (!(expected > 0d)) return 0f;
            double covered = 0d;
            var polygon = new List<Vector2>(8); var buffer = new List<Vector2>(8);
            foreach (var facet in Facets.Values)
            {
                polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c); polygon.Add(d);
                facet.ClipPolygon(polygon, buffer);
                for (int i = 1; i + 1 < polygon.Count; i++)
                    covered += Mathf.Abs(Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]));
            }
            return (float)(covered / expected);
        }

        internal void LateralRoom(Vector2 point, Vector2 direction, out float negative, out float positive)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { LateralRoomCore(point, direction, out negative, out positive); }
            finally { QueryTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started; }
        }
        private void LateralRoomCore(Vector2 point, Vector2 direction, out float negative, out float positive)
        {
            negative = positive = 0f;
            float reach = (maximum - minimum).magnitude;
            if (!(reach > 0f) || direction.sqrMagnitude == 0f) return;
            var intervals = new List<(float Start, float End)>();
            Vector2 a = point - direction * reach, b = point + direction * reach;
            foreach (var facet in Facets.Values)
                if (facet.Clip(a, b, 0f, out float start, out float end)) intervals.Add((start, end));
            intervals.Sort((x, y) => x.Start.CompareTo(y.Start));
            float low = -1f, high = -1f, tolerance = Tolerance / (reach * 2f);
            foreach (var interval in intervals)
            {
                if (low < 0f) { low = interval.Start; high = interval.End; }
                else if (interval.Start <= high + tolerance) high = Mathf.Max(high, interval.End);
                else
                {
                    if (low <= .5f + tolerance && high >= .5f - tolerance) break;
                    low = interval.Start; high = interval.End;
                }
            }
            if (low > .5f + tolerance || high < .5f - tolerance) return;
            negative = Mathf.Max(0f, (.5f - low) * reach * 2f);
            positive = Mathf.Max(0f, (high - .5f) * reach * 2f);
        }

        internal void LateralExtent(Vector2 direction, out float low, out float high)
        {
            low = float.PositiveInfinity; high = float.NegativeInfinity;
            foreach (var facet in Facets.Values)
            foreach (var point in facet.Polygon)
            { float value = Vector2.Dot(point, direction); low = Mathf.Min(low, value); high = Mathf.Max(high, value); }
        }

        internal Facet[] SurfaceTriangles(Facet facet)
        {
            if (facet.SurfaceTriangles == null)
            {
                facet.SurfaceTriangles = new Facet[facet.Points.Length - 2];
                for (int i = 1; i + 1 < facet.Points.Length; i++)
                    facet.SurfaceTriangles[i - 1] = new Facet(new[] { facet.Points[0], facet.Points[i], facet.Points[i + 1] },
                        new[] { facet.Polygon[0], facet.Polygon[i], facet.Polygon[i + 1] });
            }
            return facet.SurfaceTriangles;
        }

        // Match the exact triangle fans emitted by BuildSurface. Constant radial
        // placement over a coarse fan would otherwise float above its plane.
        internal Vector3 SurfacePosition(Vector2 point, Facet triangle, float offset)
        {
            if (!tin.BasinOnSphere) return Position(point, offset);
            if (!triangle.SurfacePlane.HasValue)
            {
                Vector3 a = tin.Direction(triangle.Points[0]);
                Vector3 normal = Vector3.Cross(tin.Direction(triangle.Points[1]) - a, tin.Direction(triangle.Points[2]) - a);
                float denominator = Vector3.Dot(normal, a) * Level;
                triangle.SurfacePlane = denominator != 0f ? normal / denominator : Vector3.zero;
            }
            Vector3 direction = Direction(point);
            float scale = Vector3.Dot(triangle.SurfacePlane.Value, direction);
            return scale > 0f && float.IsFinite(scale) ? direction * (1f / scale + offset) : Position(point, offset);
        }

        internal Vector3 SurfacePosition(Vector2 point, float offset)
        {
            foreach (var facet in Facets.Values)
                if (facet.Clip(point, point, Tolerance, out _, out _))
                    foreach (var triangle in SurfaceTriangles(facet))
                        if (triangle.Clip(point, point, Tolerance, out _, out _)) return SurfacePosition(point, triangle, offset);
            return Position(point, offset);
        }
        internal Vector2[] Route(Vector2 inlet, Vector2 outlet, out bool direct)
        {
            direct = Covers(inlet, outlet);
            if (direct) return new[] { inlet, (inlet + outlet) * .5f, outlet };
            int start = FindFacet(inlet), end = FindFacet(outlet);
            if (start < 0 || end < 0) return null;
            var visited = new Dictionary<int, (int Next, Vector2 Portal)> { [end] = (-1, outlet) };
            var queue = new Queue<int>(); queue.Enqueue(end);
            while (queue.Count > 0 && !visited.ContainsKey(start))
            {
                int t = queue.Dequeue(); var facet = Facets[t];
                for (int i = 0; i < facet.Points.Length; i++)
                {
                    int j = (i + 1) % facet.Points.Length;
                    int edge = SharedEdge(facet.Points[i], facet.Points[j]);
                    if (edge < 0) continue;
                    int neighbor = tin.Neighbors[t * 3 + edge];
                    if (!Facets.ContainsKey(neighbor) || visited.ContainsKey(neighbor)) continue;
                    visited.Add(neighbor, (t, (facet.Polygon[i] + facet.Polygon[j]) * .5f));
                    queue.Enqueue(neighbor);
                }
            }
            if (!visited.ContainsKey(start)) return null;
            var route = new List<Vector2> { inlet };
            for (int t = start; t != end; t = visited[t].Next) route.Add(visited[t].Portal);
            route.Add(outlet);
            // Skip redundant turns if the shortcut remains in the wet polygons.
            for (int i = 1; i < route.Count - 1;)
            {
                if (Covers(route[i - 1], route[i + 1])) route.RemoveAt(i);
                else i++;
            }
            for (int i = 1; i < route.Count; i++) if (!Covers(route[i - 1], route[i])) return null;
            return route.ToArray();
        }
    }

    internal sealed class Facet
    {
        internal readonly TinPoint[] Points;
        internal readonly Vector2[] Polygon;
        internal Facet[] SurfaceTriangles;
        internal Vector3? SurfacePlane;
        private readonly float winding;
        internal Facet(TinPoint[] points, Vector2[] polygon)
        {
            Points = points; Polygon = polygon;
            winding = Cross(polygon[1] - polygon[0], polygon[2] - polygon[0]) < 0 ? -1 : 1;
        }
        private float Distance(int i, Vector2 p)
        {
            Vector2 edge = Polygon[(i + 1) % Polygon.Length] - Polygon[i];
            return winding * Cross(edge, p - Polygon[i]) / Mathf.Max(1e-12f, edge.magnitude);
        }
        internal bool Clip(Vector2 a, Vector2 b, float tolerance, out float start, out float end)
        {
            start = 0; end = 1;
            for (int i = 0; i < Polygon.Length; i++)
            {
                float x = Distance(i, a) + tolerance, y = Distance(i, b) + tolerance;
                if (x < 0 && y < 0) return false;
                if (x < 0) start = Mathf.Max(start, x / (x - y));
                if (y < 0) end = Mathf.Min(end, x / (x - y));
                if (start > end) return false;
            }
            return true;
        }
        internal void ClipPolygon(List<Vector2> polygon, List<Vector2> buffer)
        {
            for (int i = 0; i < Polygon.Length && polygon.Count > 0; i++)
            {
                buffer.Clear();
                Vector2 a = polygon[polygon.Count - 1]; float x = Distance(i, a);
                foreach (var b in polygon)
                {
                    float y = Distance(i, b);
                    if ((x < 0) != (y < 0)) buffer.Add(Vector2.LerpUnclamped(a, b, x / (x - y)));
                    if (y >= 0) buffer.Add(b);
                    a = b; x = y;
                }
                polygon.Clear(); polygon.AddRange(buffer);
            }
        }
    }

    internal static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
