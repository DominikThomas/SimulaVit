using UnityEngine;

/// <summary>Minimal immutable geometry required by terrain drainage and static lakes.</summary>
public interface IGeodesicHydrologyTopology
{
    int SubdivisionLevel { get; }
    int CellCount { get; }
    int TriangleCount { get; }
    Vector3[] CellDirections { get; }
    int[] Triangles { get; }
    byte[] NeighborCounts { get; }
    int[] Neighbors6 { get; }
    float[] UnitCellAreas { get; }
    float NeighborAngularDistance(int cell, int slot);
    long ApproximateHydrologyMemoryBytes { get; }
}
