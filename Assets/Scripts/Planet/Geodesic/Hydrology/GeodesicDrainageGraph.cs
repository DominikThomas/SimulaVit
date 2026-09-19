using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Terrain-only, ocean-rooted drainage DAG. Arrays are cached; callers must not mutate them.</summary>
public sealed class GeodesicDrainageGraph
{
    public int[] DrainageReceiver { get; }
    public int[] UpstreamToDownstream { get; }
    public int[] OutletCell { get; }
    public float[] HydrologicalElevation { get; }
    public float[] FilledElevation { get; }
    public float[] FillDepth { get; }
    public double[] LocalArea { get; }
    public double[] DrainageArea { get; }
    public double[] AccumulatedRunoff { get; }
    public bool[] Ocean { get; }
    public int UnresolvedSinkCount { get; private set; }
    public int FilledCellCount { get; private set; }
    public int CellCount => DrainageReceiver.Length;

    private GeodesicDrainageGraph(int count)
    {
        DrainageReceiver = new int[count]; Array.Fill(DrainageReceiver, -1);
        UpstreamToDownstream = new int[count]; OutletCell = new int[count];
        HydrologicalElevation = new float[count]; FilledElevation = new float[count];
        FillDepth = new float[count]; LocalArea = new double[count];
        DrainageArea = new double[count]; AccumulatedRunoff = new double[count]; Ocean = new bool[count];
    }

    /// <param name="edgeSpillHeight">Optional symmetric sampled saddle height between adjacent cells.</param>
    public static GeodesicDrainageGraph Build(GeodesicGridTopology topology, float[] elevations,
        bool[] ocean, float seaLevel, float radius, Func<int, int, float> edgeSpillHeight = null)
    {
        if (topology == null || elevations == null || ocean == null ||
            elevations.Length != topology.CellCount || ocean.Length != topology.CellCount)
            throw new ArgumentException("Drainage inputs must match the topology.");
        if (!float.IsFinite(seaLevel) || !float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        int count = topology.CellCount;
        var graph = new GeodesicDrainageGraph(count);
        var settled = new bool[count];
        var rank = new int[count];
        var heap = new CellHeap(graph.FilledElevation);
        Array.Fill(graph.FilledElevation, float.PositiveInfinity);
        for (int i = 0; i < count; i++)
        {
            if (!float.IsFinite(elevations[i])) throw new ArgumentException("Nonfinite terrain height.");
            graph.HydrologicalElevation[i] = elevations[i];
            graph.Ocean[i] = ocean[i];
            // Area is in squared planet-local length units; ocean contributes no terrestrial runoff.
            graph.LocalArea[i] = ocean[i] ? 0d : Math.Max(0d, topology.UnitCellAreas[i]) * radius * radius;
            if (ocean[i]) { graph.FilledElevation[i] = seaLevel; heap.PushOrDecrease(i); }
        }
        int processed = 0;
        while (processed < count)
        {
            if (heap.Count == 0)
            {
                // A dry planet/disconnected land component has no coastal outlet. Keep an explicit sink.
                int root = -1;
                for (int i = 0; i < count; i++)
                    if (!settled[i] && (root < 0 || elevations[i] < elevations[root])) root = i;
                graph.FilledElevation[root] = elevations[root]; heap.PushOrDecrease(root);
                graph.UnresolvedSinkCount++;
            }
            int cell = heap.Pop();
            settled[cell] = true; rank[cell] = processed;
            graph.UpstreamToDownstream[count - 1 - processed++] = cell;
            for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
            {
                int neighbor = topology.Neighbors6[cell * 6 + slot];
                if (settled[neighbor] || ocean[neighbor]) continue;
                float spill = edgeSpillHeight != null ? edgeSpillHeight(cell, neighbor) : float.NegativeInfinity;
                if (float.IsNaN(spill) || float.IsPositiveInfinity(spill)) throw new ArgumentException("Invalid saddle height.");
                float candidate = Mathf.Max(graph.FilledElevation[cell], Mathf.Max(elevations[neighbor], spill));
                if (candidate >= graph.FilledElevation[neighbor]) continue;
                graph.FilledElevation[neighbor] = candidate;
                graph.DrainageReceiver[neighbor] = cell;
                heap.PushOrDecrease(neighbor);
            }
        }
        // Prefer steepest descent where possible. On filled flats keep the flood parent, whose
        // settled rank is strictly smaller. This avoids cycles without epsilon-height inflation.
        for (int cell = 0; cell < count; cell++)
        {
            if (ocean[cell] || graph.DrainageReceiver[cell] < 0) continue;
            float bestSlope = 0f;
            for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
            {
                int neighbor = topology.Neighbors6[cell * 6 + slot];
                if (rank[neighbor] >= rank[cell]) continue;
                float neighborHeight = ocean[neighbor] ? seaLevel : elevations[neighbor];
                float slope = (elevations[cell] - neighborHeight) /
                    Mathf.Max(1e-7f, topology.NeighborAngularDistances6[cell * 6 + slot]);
                if (slope <= bestSlope) continue;
                if (edgeSpillHeight != null && edgeSpillHeight(cell, neighbor) > graph.FilledElevation[cell]) continue;
                bestSlope = slope; graph.DrainageReceiver[cell] = neighbor;
            }
            graph.FillDepth[cell] = Mathf.Max(0f, graph.FilledElevation[cell] - elevations[cell]);
            if (graph.FillDepth[cell] > 1e-6f) graph.FilledCellCount++;
        }
        for (int i = count - 1; i >= 0; i--)
        {
            int cell = graph.UpstreamToDownstream[i], receiver = graph.DrainageReceiver[cell];
            graph.OutletCell[cell] = receiver < 0 ? cell : graph.OutletCell[receiver];
        }
        Array.Copy(graph.LocalArea, graph.DrainageArea, count);
        graph.Accumulate(graph.DrainageArea);
        graph.UpdateRunoff(null);
        return graph;
    }

    /// <summary>Per-cell supply (e.g. precipitation * area * runoff coefficient). Null = unit supply per area.
    /// Reuses terrain topology and catchment area. Ocean supply is ignored. Invalid input leaves state intact.</summary>
    public void UpdateRunoff(double[] localRunoff)
    {
        if (localRunoff != null)
        {
            if (localRunoff.Length != CellCount) throw new ArgumentException("Runoff must match the topology.");
            for (int i = 0; i < CellCount; i++)
                if (!double.IsFinite(localRunoff[i]) || localRunoff[i] < 0d)
                    throw new ArgumentException("Runoff must be finite and nonnegative.");
        }
        for (int i = 0; i < CellCount; i++)
            AccumulatedRunoff[i] = Ocean[i] ? 0d : localRunoff == null ? LocalArea[i] : localRunoff[i];
        Accumulate(AccumulatedRunoff);
    }

    private void Accumulate(double[] values)
    {
        foreach (int cell in UpstreamToDownstream)
        {
            int receiver = DrainageReceiver[cell];
            if (receiver >= 0) values[receiver] += values[cell];
        }
    }

    // Indexed binary heap: bounded O(N) storage, deterministic equal-height ordering by cell index.
    private sealed class CellHeap
    {
        private readonly float[] heights;
        private readonly int[] cells, positions;
        public int Count { get; private set; }
        public CellHeap(float[] heights)
        { this.heights = heights; cells = new int[heights.Length]; positions = new int[heights.Length]; Array.Fill(positions, -1); }
        private bool Before(int a, int b) => heights[a] < heights[b] || heights[a] == heights[b] && a < b;
        private void Swap(int a, int b)
        { int c = cells[a]; cells[a] = cells[b]; cells[b] = c; positions[cells[a]] = a; positions[cells[b]] = b; }
        public void PushOrDecrease(int cell)
        {
            int pos = positions[cell];
            if (pos < 0) { pos = Count++; cells[pos] = cell; positions[cell] = pos; }
            while (pos > 0)
            { int parent = (pos - 1) / 2; if (!Before(cells[pos], cells[parent])) break; Swap(pos, parent); pos = parent; }
        }
        public int Pop()
        {
            int result = cells[0]; positions[result] = -1;
            if (--Count == 0) return result;
            cells[0] = cells[Count]; positions[cells[0]] = 0;
            int pos = 0;
            while (pos * 2 + 1 < Count)
            {
                int child = pos * 2 + 1;
                if (child + 1 < Count && Before(cells[child + 1], cells[child])) child++;
                if (!Before(cells[child], cells[pos])) break;
                Swap(pos, child); pos = child;
            }
            return result;
        }
    }
}
