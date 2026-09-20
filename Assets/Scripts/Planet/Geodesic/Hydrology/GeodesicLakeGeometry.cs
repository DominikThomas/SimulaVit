using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Connected, terrain-clipped lake patches. A local visual flood validates an already detected
/// hydrological basin; it never invents basins, changes elevations, or runs each frame.</summary>
public sealed class GeodesicLakeGeometry
{
    public Vector3[] Vertices { get; private set; }
    public int[] Triangles { get; private set; }
    public int[] VertexBasinIds { get; private set; }
    public int RejectedProjectionBasins { get; private set; }
    public double[] VisibleAreas { get; private set; }
    private int[] triangleBasin, firstTriangle;
    private byte[] triangleCounts;
    private GeodesicRiverTerrain terrain;
    private System.Collections.Generic.List<Vector3>[] shorePoints;

    public bool TryClosestShore(int basin, Vector3 toward, out Vector3 direction)
    {
        direction = Vector3.zero; float best = -2f;
        if (shorePoints == null || basin < 0 || basin >= shorePoints.Length || shorePoints[basin] == null) return false;
        foreach (Vector3 point in shorePoints[basin]) { float dot = Vector3.Dot(point, toward); if (dot > best) { best = dot; direction = point; } }
        return best > -2f;
    }

    public int BasinAtDirection(Vector3 direction)
    {
        if (terrain == null) return -1;
        int face = terrain.FindTriangle(direction), id = triangleBasin[face];
        if (id < 0) return -1;
        Vector3 d = direction.normalized;
        for (int triangle = firstTriangle[face]; triangle < firstTriangle[face] + triangleCounts[face] * 3; triangle += 3)
        {
            Vector3 a = Vertices[Triangles[triangle]].normalized, b = Vertices[Triangles[triangle + 1]].normalized, c = Vertices[Triangles[triangle + 2]].normalized;
            if (InsideEdge(a, b, d) && InsideEdge(b, c, d) && InsideEdge(c, a, d)) return id;
        }
        return -1;
    }

    // Anchor directions often lie exactly on shared render edges. Double precision avoids
    // cancellation in tiny spherical triangles; tolerance is angular, independent of subdivision.
    private static bool InsideEdge(Vector3 a, Vector3 b, Vector3 direction)
    {
        double x = (double)a.y * b.z - (double)a.z * b.y;
        double y = (double)a.z * b.x - (double)a.x * b.z;
        double z = (double)a.x * b.y - (double)a.y * b.x;
        return x * direction.x + y * direction.y + z * direction.z >= -0.0000001d * Math.Sqrt(x*x + y*y + z*z);
    }
    public static GeodesicLakeGeometry Build(GeodesicGridTopology topology, GeodesicDrainageGraph drainage,
        GeodesicLakeBasins lakes, IcosphereRenderGeometry geometry, Vector3[] surface, GeodesicRiverTerrain terrain,
        float planetRadius, float minimumAreaFraction)
    {
        if (surface == null || surface.Length != geometry.VertexCount) throw new ArgumentException("Visible terrain does not match geometry.");
        var result = new GeodesicLakeGeometry { terrain = terrain,
            shorePoints = new List<Vector3>[lakes.Basins.Length], triangleBasin = new int[geometry.TriangleCount], firstTriangle = new int[geometry.TriangleCount],
            triangleCounts = new byte[geometry.TriangleCount], VisibleAreas = new double[lakes.Basins.Length] };
        Array.Fill(result.triangleBasin, -1);
        var vertices = new List<Vector3>(); var triangles = new List<int>(); var vertexBasins = new List<int>();
        bool anySelected = false; foreach (var basin in lakes.Basins) anySelected |= basin.Selected;
        if (anySelected)
        {
            var mapping = IcosphereDirectionMappingBuilder.Build(topology, geometry);
            int[] counts = new int[geometry.VertexCount];
            foreach (int vertex in geometry.Triangles) counts[vertex]++;
            int[] starts = new int[counts.Length + 1];
            for (int i = 0; i < counts.Length; i++) starts[i + 1] = starts[i] + counts[i];
            int[] incident = new int[geometry.Triangles.Length], cursor = (int[])starts.Clone();
            for (int f = 0; f < geometry.TriangleCount; f++)
                for (int n = 0; n < 3; n++) incident[cursor[geometry.Triangles[f * 3 + n]]++] = f;
            var seeds = new int[lakes.Basins.Length]; Array.Fill(seeds, -1);
            for (int v = 0; v < surface.Length; v++)
            {
                int id = lakes.BasinId[mapping.Samples[v].NearestCell];
                if (id >= 0 && lakes.Basins[id].Selected && (seeds[id] < 0 || surface[v].sqrMagnitude < surface[seeds[id]].sqrMagnitude)) seeds[id] = v;
            }
            int[] allowed = new int[topology.CellCount], visited = new int[surface.Length], touched = new int[geometry.TriangleCount];
            var queue = new int[surface.Length]; var faces = new List<int>();
            foreach (var basin in lakes.Basins)
            {
                if (!basin.Selected) continue;
                float level = basin.CurrentSurfaceElevation;
                int stamp = basin.Id + 1, seed = seeds[basin.Id];
                if (seed < 0 || surface[seed].magnitude >= level - GeodesicLakeBasins.ElevationEpsilon)
                { Reject(basin, "projection-no-visible-depression"); continue; }
                foreach (int cell in basin.Cells)
                {
                    allowed[cell] = stamp;
                    for (int n = 0; n < topology.NeighborCounts[cell]; n++) allowed[topology.Neighbors6[cell * 6 + n]] = stamp;
                }
                int head = 0, tail = 1; queue[0] = seed; visited[seed] = stamp; faces.Clear();
                bool leaks = false;
                while (head < tail)
                {
                    int v = queue[head++];
                    for (int entry = starts[v]; entry < starts[v + 1]; entry++)
                    {
                        int face = incident[entry];
                        if (touched[face] != stamp) { touched[face] = stamp; faces.Add(face); }
                        for (int n = 0; n < 3; n++)
                        {
                            int neighbor = geometry.Triangles[face * 3 + n];
                            if (visited[neighbor] == stamp || surface[neighbor].magnitude >= level - GeodesicLakeBasins.ElevationEpsilon) continue;
                            int cell = mapping.Samples[neighbor].NearestCell;
                            if (allowed[cell] != stamp || drainage.Ocean[cell] || (lakes.BasinId[cell] >= 0 && lakes.BasinId[cell] != basin.Id))
                            { leaks = true; continue; }
                            visited[neighbor] = stamp; queue[tail++] = neighbor;
                        }
                    }
                }
                // An escaping visible pool means the coarse spill/projection is not safe at this level.
                // Report it; never chop a full-cell water patch across that valley/ridge to hide the error.
                if (leaks) { Reject(basin, "projection-spill-or-footprint-mismatch"); continue; }
                result.shorePoints[basin.Id] = new List<Vector3>();
                int firstVertex = vertices.Count, firstIndex = triangles.Count;
                double area = 0d;
                foreach (int face in faces)
                {
                    Vector3[] input = { surface[geometry.Triangles[face * 3]], surface[geometry.Triangles[face * 3 + 1]], surface[geometry.Triangles[face * 3 + 2]] };
                    List<Vector3> polygon = ClipTriangle(input, level);
                    if (polygon.Count < 3) continue;
                    result.firstTriangle[face] = triangles.Count;
                    result.triangleBasin[face] = basin.Id;
                    int first = vertices.Count;
                    foreach (var p in polygon) { vertices.Add(p.normalized * level); vertexBasins.Add(basin.Id); if (Mathf.Abs(p.magnitude - level) < 0.000002f) result.shorePoints[basin.Id].Add(p.normalized); }
                    for (int i = 1; i < polygon.Count - 1; i++)
                    {
                        int b = first + i, c = first + i + 1;
                        if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[first], vertices[c] - vertices[first]), vertices[first]) < 0f) (b, c) = (c, b);
                        triangles.Add(first); triangles.Add(b); triangles.Add(c); result.triangleCounts[face]++;
                        area += Vector3.Cross(vertices[b] - vertices[first], vertices[c] - vertices[first]).magnitude * .5d;
                    }
                }
                double planetArea = 4d * Math.PI * planetRadius * planetRadius;
                if (triangles.Count == firstIndex || area / planetArea < GeodesicLakeBasins.NormalizeArea(minimumAreaFraction))
                {
                    foreach (int face in faces) { result.triangleBasin[face] = -1; result.triangleCounts[face] = 0; }
                    triangles.RemoveRange(firstIndex, triangles.Count - firstIndex);
                    vertices.RemoveRange(firstVertex, vertices.Count - firstVertex); vertexBasins.RemoveRange(firstVertex, vertexBasins.Count - firstVertex);
                    basin.Selected = false; basin.RejectionReason = "visible-area-below-threshold";
                    continue;
                }
                result.VisibleAreas[basin.Id] = area;
            }
            void Reject(GeodesicLakeBasin basin, string reason)
            { basin.Selected = false; basin.RejectionReason = reason; result.RejectedProjectionBasins++; }
        }
        result.Vertices = vertices.ToArray(); result.Triangles = triangles.ToArray(); result.VertexBasinIds = vertexBasins.ToArray();
        lakes.RefreshMask();
        // Cell-centre convention for partial shore cells. BasinId still preserves the whole
        // hydrological footprint; direction queries resolve the actual clipped water polygon.
        for (int cell = 0; cell < topology.CellCount; cell++)
        {
            int id = anySelected ? result.BasinAtDirection(topology.CellDirections[cell]) : -1;
            lakes.LakeMask[cell] = !drainage.Ocean[cell] && id >= 0;
            lakes.LakeSurfaceElevation[cell] = lakes.LakeMask[cell] ? lakes.Basins[id].CurrentSurfaceElevation : 0f;
        }
        return result;
    }

    /// <summary>Clip against the sphere at the lake radius using actual completed triangle edges.
    /// Terrain is never changed. A small depth-buffer offset in the lake shader handles shore contact.</summary>
    public static List<Vector3> ClipTriangle(Vector3[] triangle, float radius)
    {
        var output = new List<Vector3>(4);
        Vector3 previous = triangle[triangle.Length - 1]; bool priorWet = previous.sqrMagnitude < radius * radius;
        foreach (Vector3 current in triangle)
        {
            bool wet = current.sqrMagnitude < radius * radius;
            if (wet != priorWet) output.Add(IntersectSphere(previous, current, radius));
            if (wet) output.Add(current);
            previous = current; priorWet = wet;
        }
        return output;
    }
    private static Vector3 IntersectSphere(Vector3 a, Vector3 b, float radius)
    {
        // Bisection on the actual edge is stable even for very shallow intersections.
        bool aWet = a.sqrMagnitude < radius * radius;
        for (int i = 0; i < 24; i++)
        {
            Vector3 middle = (a + b) * .5f;
            if ((middle.sqrMagnitude < radius * radius) == aWet) a = middle; else b = middle;
        }
        return (a + b) * .5f;
    }
}
