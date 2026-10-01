using System;
using UnityEngine;

/// <summary>
/// Lightweight drainage topology derived from, and sharing, an existing render icosphere.
/// It deliberately omits dual corners and transport metrics used by the simulation grid.
/// </summary>
public sealed class GeodesicHydrologyTopology : IGeodesicHydrologyTopology
{
    public int SubdivisionLevel { get; }
    public Vector3[] CellDirections { get; }
    public int[] Triangles { get; }
    public byte[] NeighborCounts { get; }
    public int[] Neighbors6 { get; }
    public float[] UnitCellAreas { get; }
    public int CellCount => CellDirections.Length;
    public int TriangleCount => Triangles.Length / 3;
    public int PentagonCount { get; }
    public long ApproximateHydrologyMemoryBytes => NeighborCounts.LongLength +
        Neighbors6.LongLength * sizeof(int) + UnitCellAreas.LongLength * sizeof(float);

    private GeodesicHydrologyTopology(IcosphereRenderGeometry geometry, byte[] counts,
        int[] neighbors, float[] areas, int pentagons)
    {
        SubdivisionLevel = geometry.SubdivisionLevel;
        CellDirections = geometry.UnitVertices;
        Triangles = geometry.Triangles;
        NeighborCounts = counts;
        Neighbors6 = neighbors;
        UnitCellAreas = areas;
        PentagonCount = pentagons;
    }

    public static GeodesicHydrologyTopology Build(IcosphereRenderGeometry geometry)
    {
        if (geometry.UnitVertices == null || geometry.Triangles == null || geometry.VertexCount == 0)
            throw new ArgumentException("Hydrology requires an initialized render geometry.");
        int count = geometry.VertexCount;
        var degrees = new byte[count];
        var neighbors = new int[count * 6];
        var areas = new float[count];
        Array.Fill(neighbors, -1);
        for (int t = 0; t < geometry.Triangles.Length; t += 3)
        {
            int a = geometry.Triangles[t], b = geometry.Triangles[t + 1], c = geometry.Triangles[t + 2];
            ValidateVertex(a); ValidateVertex(b); ValidateVertex(c);
            AddEdge(a, b); AddEdge(b, a); AddEdge(b, c);
            AddEdge(c, b); AddEdge(c, a); AddEdge(a, c);
            float thirdArea = SphericalTriangleArea(geometry.UnitVertices[a], geometry.UnitVertices[b], geometry.UnitVertices[c]) / 3f;
            areas[a] += thirdArea; areas[b] += thirdArea; areas[c] += thirdArea;
        }
        int pentagons = 0;
        for (int cell = 0; cell < count; cell++)
        {
            if (degrees[cell] == 5) pentagons++;
            else if (degrees[cell] != 6) throw new InvalidOperationException($"Hydrology vertex {cell} has degree {degrees[cell]}, expected five or six.");
        }
        if (pentagons != 12) throw new InvalidOperationException($"Hydrology topology has {pentagons} pentagons, expected twelve.");
        return new GeodesicHydrologyTopology(geometry, degrees, neighbors, areas, pentagons);

        void ValidateVertex(int vertex)
        {
            if (vertex < 0 || vertex >= count) throw new ArgumentException("Render geometry contains an invalid triangle index.");
        }
        void AddEdge(int from, int to)
        {
            int start = from * 6;
            for (int slot = 0; slot < degrees[from]; slot++) if (neighbors[start + slot] == to) return;
            if (degrees[from] >= 6) throw new InvalidOperationException($"Hydrology vertex {from} exceeds degree six.");
            neighbors[start + degrees[from]++] = to;
        }
    }

    public float NeighborAngularDistance(int cell, int slot)
    {
        int neighbor = Neighbors6[cell * 6 + slot];
        return Mathf.Acos(Mathf.Clamp(Vector3.Dot(CellDirections[cell], CellDirections[neighbor]), -1f, 1f));
    }

    private static float SphericalTriangleArea(Vector3 a, Vector3 b, Vector3 c)
    {
        double determinant = Math.Abs(Vector3.Dot(a, Vector3.Cross(b, c)));
        double denominator = 1d + Vector3.Dot(a, b) + Vector3.Dot(b, c) + Vector3.Dot(c, a);
        return (float)(2d * Math.Atan2(determinant, denominator));
    }
}
