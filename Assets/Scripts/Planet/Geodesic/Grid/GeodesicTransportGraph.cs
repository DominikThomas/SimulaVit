using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Immutable, scalar-independent horizontal transport geometry for a geodesic topology.</summary>
public sealed class GeodesicTransportGraph
{
    private const float ReciprocalTolerance = 1e-5f;

    public GeodesicGridTopology SourceTopology { get; }
    public int CellCount { get; }
    public int EdgeCount { get; }
    public int[] EdgeCellA { get; }
    public int[] EdgeCellB { get; }
    public float[] EdgeAngularDistances { get; }
    public float[] SharedDualEdgeAngularLengths { get; }
    public float[] EdgeConductanceBase { get; }
    public float[] CellConductanceSumBase { get; }
    public long ApproximateMemoryBytes => (long)EdgeCount * 20L + (long)CellCount * 4L;
    public int ReciprocalMetricMismatchCount { get; }
    public float MaximumReciprocalMetricDifference { get; }

    public GeodesicTransportGraph(GeodesicGridTopology topology)
    {
        SourceTopology = topology ?? throw new ArgumentNullException(nameof(topology));
        CellCount = topology.CellCount;
        int capacity = topology.EdgeCount;
        EdgeCellA = new int[capacity];
        EdgeCellB = new int[capacity];
        EdgeAngularDistances = new float[capacity];
        SharedDualEdgeAngularLengths = new float[capacity];
        EdgeConductanceBase = new float[capacity];
        CellConductanceSumBase = new float[CellCount];
        byte[] cellDegrees = new byte[CellCount];

        int edge = 0, mismatchCount = 0;
        float maximumMismatch = 0f;
        for (int a = 0; a < CellCount; a++)
        {
            for (int slot = 0; slot < topology.NeighborCounts[a]; slot++)
            {
                int b = topology.Neighbors6[a * 6 + slot];
                if (b <= a) continue;
                if (edge >= capacity) throw new InvalidOperationException("Topology contains more unique edges than its EdgeCount.");
                int reciprocalSlot = FindNeighborSlot(topology, b, a);
                if (reciprocalSlot < 0) throw new InvalidOperationException($"Non-reciprocal topology edge {a}-{b}.");

                float distance = topology.NeighborAngularDistances6[a * 6 + slot];
                float sharedLength = topology.SharedDualEdgeAngularLengths6[a * 6 + slot];
                RequireFiniteGeometry(a, b, distance, sharedLength);
                float reciprocalDistance = topology.NeighborAngularDistances6[b * 6 + reciprocalSlot];
                float reciprocalLength = topology.SharedDualEdgeAngularLengths6[b * 6 + reciprocalSlot];
                RequireFiniteGeometry(b, a, reciprocalDistance, reciprocalLength);
                float difference = Mathf.Max(Mathf.Abs(distance - reciprocalDistance), Mathf.Abs(sharedLength - reciprocalLength));
                if (difference > ReciprocalTolerance)
                {
                    mismatchCount++;
                    maximumMismatch = Mathf.Max(maximumMismatch, difference);
                }

                float conductance = sharedLength / distance;
                if (!IsFinite(conductance) || conductance < 0f) throw new InvalidOperationException($"Invalid conductance on topology edge {a}-{b}.");
                EdgeCellA[edge] = a;
                EdgeCellB[edge] = b;
                EdgeAngularDistances[edge] = distance;
                SharedDualEdgeAngularLengths[edge] = sharedLength;
                EdgeConductanceBase[edge] = conductance;
                CellConductanceSumBase[a] += conductance;
                CellConductanceSumBase[b] += conductance;
                cellDegrees[a]++;
                cellDegrees[b]++;
                edge++;
            }
        }

        EdgeCount = edge;
        ReciprocalMetricMismatchCount = mismatchCount;
        MaximumReciprocalMetricDifference = maximumMismatch;
        if (edge != capacity) throw new InvalidOperationException($"Transport edge count {edge} does not match topology EdgeCount {capacity}.");
        int degreeSum = 0;
        for (int cell = 0; cell < CellCount; cell++)
        {
            degreeSum += cellDegrees[cell];
            if (cellDegrees[cell] != topology.NeighborCounts[cell]) throw new InvalidOperationException($"Transport degree {cellDegrees[cell]} does not match topology degree {topology.NeighborCounts[cell]} at cell {cell}.");
        }
        if (degreeSum != 2 * EdgeCount) throw new InvalidOperationException($"Transport degree sum {degreeSum} does not equal twice edge count {2 * EdgeCount}.");
        if (mismatchCount > 0) Debug.LogWarning($"[GeodesicTransportGraph] {mismatchCount} reciprocal metric mismatches; maximum absolute difference={maximumMismatch:E3}. Canonical lower-index metrics were retained.");
    }

    private static int FindNeighborSlot(GeodesicGridTopology topology, int cell, int neighbor)
    {
        for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++) if (topology.Neighbors6[cell * 6 + slot] == neighbor) return slot;
        return -1;
    }

    private static void RequireFiniteGeometry(int a, int b, float distance, float sharedLength)
    {
        if (!IsFinite(distance) || distance <= 0f || !IsFinite(sharedLength) || sharedLength < 0f)
            throw new InvalidOperationException($"Invalid geometry on topology edge {a}-{b}: distance={distance}, sharedLength={sharedLength}.");
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

public static class GeodesicTransportGraphValidation
{
    public static bool Validate(GeodesicTransportGraph graph, out string report)
    {
        if (graph == null || graph.SourceTopology == null) { report = "graph/source topology is null"; return false; }
        GeodesicGridTopology topology = graph.SourceTopology;
        int[] degree = new int[graph.CellCount];
        float[] sums = new float[graph.CellCount];
        var keys = new HashSet<ulong>();
        int pentagons = 0;
        for (int i = 0; i < topology.CellCount; i++) if (topology.NeighborCounts[i] == 5) pentagons++;
        if (graph.CellCount != topology.CellCount || graph.EdgeCount != topology.EdgeCount || graph.EdgeCount != GeodesicGridTopology.ExpectedEdgeCount(topology.SubdivisionLevel)) { report = "cell/edge count mismatch"; return false; }
        for (int edge = 0; edge < graph.EdgeCount; edge++)
        {
            int a = graph.EdgeCellA[edge], b = graph.EdgeCellB[edge];
            if (a < 0 || b >= graph.CellCount || a >= b) { report = $"non-canonical/self edge at {edge}"; return false; }
            ulong key = ((ulong)(uint)a << 32) | (uint)b;
            if (!keys.Add(key)) { report = $"duplicate edge {a}-{b}"; return false; }
            if (!HasNeighbor(topology, a, b) || !HasNeighbor(topology, b, a)) { report = $"non-reciprocal edge {a}-{b}"; return false; }
            float distance = graph.EdgeAngularDistances[edge], length = graph.SharedDualEdgeAngularLengths[edge], conductance = graph.EdgeConductanceBase[edge];
            if (!Finite(distance) || distance <= 0f || !Finite(length) || length < 0f || !Finite(conductance) || conductance < 0f) { report = $"invalid edge geometry at {edge}"; return false; }
            degree[a]++; degree[b]++; sums[a] += conductance; sums[b] += conductance;
        }
        int degreeSum = 0;
        for (int i = 0; i < graph.CellCount; i++)
        {
            degreeSum += degree[i];
            if (degree[i] != topology.NeighborCounts[i]) { report = $"degree mismatch at cell {i}"; return false; }
            if (Mathf.Abs(sums[i] - graph.CellConductanceSumBase[i]) > 1e-5f * Mathf.Max(1f, sums[i])) { report = $"conductance sum mismatch at cell {i}"; return false; }
        }
        if (degreeSum != 2 * graph.EdgeCount || pentagons != 12) { report = $"degree sum/pentagon mismatch: degreeSum={degreeSum}, pentagons={pentagons}"; return false; }
        report = $"valid; cells={graph.CellCount}, edges={graph.EdgeCount}, pentagons={pentagons}, reciprocalMetricMismatches={graph.ReciprocalMetricMismatchCount}";
        return true;
    }

    private static bool HasNeighbor(GeodesicGridTopology topology, int cell, int neighbor)
    {
        for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++) if (topology.Neighbors6[cell * 6 + slot] == neighbor) return true;
        return false;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
