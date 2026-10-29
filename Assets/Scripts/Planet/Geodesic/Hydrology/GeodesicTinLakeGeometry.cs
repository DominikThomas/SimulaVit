using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

public sealed class TinLakeBody
{
    public int BasinId { get; internal set; }
    public double WaterLevel { get; internal set; }
    public double Area { get; internal set; }
    public int FirstVertex { get; internal set; }
    public int VertexCount { get; internal set; }
    public int FirstIndex { get; internal set; }
    public int IndexCount { get; internal set; }
}

/// <summary>Static water only. Scalar clipping is exact on the active linear TIN. Spherical
/// vertices share the spill radius, with zero radial offset (shader polygon bias only).
/// Qualifying ancestors own submerged children; no runoff or routing state is used.</summary>
public sealed class GeodesicTinLakeGeometry
{
    public Vector3[] Vertices { get; private set; }
    public int[] Triangles { get; private set; }
    public TinPoint[] TerrainPoints { get; private set; }
    public IReadOnlyList<TinLakeBody> Bodies { get; private set; }
    public int RawBasins, QualifyingBasins, SkippedTerminal, SkippedSubsea, NestedSuppressed;
    public int TrianglesConsidered, ClippedPolygons, OceanFacetsSkipped;
    public double BuildMilliseconds;
    public string Summary() => $"[GeodesicTinLakes] raw={RawBasins} qualifying={QualifyingBasins} rendered={Bodies.Count} skippedTerminal={SkippedTerminal} skippedSubsea={SkippedSubsea} nestedSuppressed={NestedSuppressed} trianglesConsidered={TrianglesConsidered} polygons={ClippedPolygons} vertices={Vertices.Length} triangles={Triangles.Length / 3} totalArea={Bodies.Sum(b => b.Area):G9} largestArea={Bodies.Select(b => b.Area).DefaultIfEmpty(0).Max():G9} oceanFacetsSkipped={OceanFacetsSkipped} geometryMs={BuildMilliseconds:F2} radialOffset=0";

    /// <summary>Ordered convex wet polygon (0, 3 or 4 vertices, except zero-area ties).
    /// Shared edges always interpolate from the same indexed endpoint, preventing cracks.</summary>
    public static TinPoint[] ClipTriangle(GeodesicTinHydrology tin, int triangle, double level)
    {
        if (tin == null) throw new ArgumentNullException(nameof(tin));
        if (triangle < 0 || triangle >= tin.TriangleCount || double.IsNaN(level) || double.IsInfinity(level)) throw new ArgumentOutOfRangeException();
        var result = new List<TinPoint>(4);
        TinPoint Corner(int k) => new TinPoint(triangle, k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0);
        void Add(TinPoint p)
        {
            if (result.Count == 0 || !Same(result[result.Count - 1], p)) result.Add(p);
        }
        for (int k = 0; k < 3; k++)
        {
            int j = (k + 1) % 3; TinPoint a = Corner(k), b = Corner(j);
            double ha = tin.Height(a), hb = tin.Height(b);
            if (ha <= level) Add(a);
            if ((ha < level && hb > level) || (ha > level && hb < level))
            {
                if (tin.BasinVertex(triangle, k) > tin.BasinVertex(triangle, j)) { var p = a; a = b; b = p; double h = ha; ha = hb; hb = h; }
                double t = (level - ha) / (hb - ha);
                Add(new TinPoint(triangle, a.A + t * (b.A - a.A), a.B + t * (b.B - a.B), a.C + t * (b.C - a.C)));
            }
        }
        if (result.Count > 1 && Same(result[0], result[result.Count - 1])) result.RemoveAt(result.Count - 1);
        return result.Count < 3 ? Array.Empty<TinPoint>() : result.ToArray();
    }
    private static bool Same(TinPoint a, TinPoint b) => a.A == b.A && a.B == b.B && a.C == b.C;

    public static GeodesicTinLakeGeometry Build(GeodesicTinHydrology tin, GeodesicTinBasins topology)
    {
        if (tin == null || topology == null) throw new ArgumentNullException();
        var watch = Stopwatch.StartNew(); var output = new GeodesicTinLakeGeometry();
        var basins = topology.Basins.ToDictionary(b => b.Id); var bodies = new List<TinLakeBody>();
        var vertices = new List<Vector3>(); var indices = new List<int>(); var points = new List<TinPoint>();
        output.RawBasins = basins.Count; output.QualifyingBasins = basins.Values.Count(b => b.QualifiesForVisibleLake);
        output.SkippedTerminal = basins.Values.Count(b => b.Terminal); output.SkippedSubsea = basins.Values.Count(b => b.SubseaUnclassified);
        foreach (var basin in topology.Basins.Where(b => b.QualifiesForVisibleLake).OrderBy(b => b.Id))
        {
            bool covered = false; int ancestorId = basin.ParentBasinId; var seen = new HashSet<int>();
            while (ancestorId >= 0 && basins.TryGetValue(ancestorId, out var ancestor))
            {
                if (!seen.Add(ancestorId)) throw new InvalidOperationException("Cycle in TIN basin hierarchy.");
                if (ancestor.QualifiesForVisibleLake && ancestor.SpillElevation >= basin.SpillElevation) { covered = true; break; }
                ancestorId = ancestor.ParentBasinId;
            }
            if (covered) { output.NestedSuppressed++; continue; }
            var body = new TinLakeBody { BasinId = basin.Id, WaterLevel = basin.SpillElevation, FirstVertex = vertices.Count, FirstIndex = indices.Count };
            foreach (int triangle in basin.MemberTriangles)
            {
                output.TrianglesConsidered++;
                if (tin.BasinFacetIntersectsOcean(triangle)) { output.OceanFacetsSkipped++; continue; }
                var polygon = ClipTriangle(tin, triangle, basin.SpillElevation);
                if (polygon.Length < 3) continue;
                // Flat facets exactly at the rim are dry (zero depth), not extensions of water.
                if (polygon.All(p => tin.Height(p) >= basin.SpillElevation)) continue;
                var positions = polygon.Select(p => tin.BasinOnSphere ? tin.Direction(p) * (float)basin.SpillElevation :
                    tin.Direction(p) + Vector3.forward * (float)basin.SpillElevation).ToArray();
                int first = vertices.Count; double area = 0;
                for (int j = 1; j + 1 < positions.Length; j++)
                {
                    Vector3 cross = Vector3.Cross(positions[j] - positions[0], positions[j + 1] - positions[0]);
                    if (cross.sqrMagnitude == 0) continue;
                    bool reverse = Vector3.Dot(cross, tin.BasinOnSphere ? positions[0] : Vector3.forward) < 0;
                    indices.Add(first); indices.Add(first + (reverse ? j + 1 : j)); indices.Add(first + (reverse ? j : j + 1));
                    area += cross.magnitude * .5d;
                }
                if (area == 0) continue;
                vertices.AddRange(positions); points.AddRange(polygon); body.Area += area; output.ClippedPolygons++;
            }
            body.VertexCount = vertices.Count - body.FirstVertex; body.IndexCount = indices.Count - body.FirstIndex;
            if (body.IndexCount > 0) bodies.Add(body);
        }
        output.Vertices = vertices.ToArray(); output.Triangles = indices.ToArray(); output.TerrainPoints = points.ToArray();
        output.Bodies = bodies.AsReadOnly(); output.BuildMilliseconds = watch.Elapsed.TotalMilliseconds; return output;
    }
}
