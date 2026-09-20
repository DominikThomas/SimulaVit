using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Depression geometry from the existing priority flood, independent of runoff and water state.
/// Elevations are absolute planet-local radii, as in GeodesicDrainageGraph.</summary>
public sealed class GeodesicLakeBasin
{
    public int Id, FloorCell, SpillFromCell, SpillCell, DownstreamReceiver;
    public int[] Cells;
    public double Area, AreaFraction, CatchmentArea;
    public float FloorElevation, SpillElevation;
    public float MaximumDepth => SpillElevation - FloorElevation;
    public bool Terminal, Selected;
    public string RejectionReason;
    // Separate state: future water budgets may initialize/update this below the geometric spill.
    public float CurrentSurfaceElevation;
    public int IncomingRiverCount;
}

public sealed class GeodesicLakeBasins
{
    public const float DefaultMinimumAreaFraction = 0.0001f; // 0.01% of planet
    public const float DefaultMinimumDepth = 0.005f;
    public const float ElevationEpsilon = 0.000001f;
    public readonly GeodesicLakeBasin[] Basins;
    public readonly int[] BasinId;
    public readonly bool[] LakeMask;
    public readonly float[] LakeSurfaceElevation;
    public readonly bool Enabled;

    private GeodesicLakeBasins(GeodesicLakeBasin[] basins, int[] ids, bool enabled)
    {
        Basins = basins; BasinId = ids; Enabled = enabled;
        LakeMask = new bool[ids.Length]; LakeSurfaceElevation = new float[ids.Length];
        RefreshMask();
    }

    public static float NormalizeArea(float value) => float.IsFinite(value) ? Mathf.Clamp(value, 0f, .05f) : DefaultMinimumAreaFraction;
    public static float NormalizeDepth(float value) => float.IsFinite(value) ? Mathf.Clamp(value, 0f, 1f) : DefaultMinimumDepth;

    public static GeodesicLakeBasins Build(GeodesicGridTopology topology, GeodesicDrainageGraph graph,
        float radius, bool enabled, float minimumAreaFraction, float minimumDepth, Func<int, int, float> edgeSpill = null)
    {
        if (topology == null || graph == null || topology.CellCount != graph.CellCount) throw new ArgumentException("Lake topology must match drainage.");
        int count = graph.CellCount;
        int[] ids = new int[count]; Array.Fill(ids, -1);
        var queue = new int[count]; var basins = new List<GeodesicLakeBasin>();
        double planetArea = 0d;
        for (int i = 0; i < count; i++) planetArea += topology.UnitCellAreas[i] * (double)radius * radius;
        minimumAreaFraction = NormalizeArea(minimumAreaFraction); minimumDepth = NormalizeDepth(minimumDepth);
        bool Filled(int cell) => !graph.Ocean[cell] && graph.FillDepth[cell] > ElevationEpsilon;
        for (int seed = 0; seed < count; seed++)
        {
            if (ids[seed] >= 0 || !Filled(seed)) continue;
            float level = graph.FilledElevation[seed];
            int id = basins.Count, head = 0, tail = 1;
            ids[seed] = id; queue[0] = seed;
            var basin = new GeodesicLakeBasin { Id = id, FloorCell = seed, FloorElevation = graph.HydrologicalElevation[seed],
                SpillElevation = level, SpillFromCell = -1, SpillCell = -1, DownstreamReceiver = -1 };
            while (head < tail)
            {
                int cell = queue[head++];
                basin.Area += graph.LocalArea[cell];
                if (graph.HydrologicalElevation[cell] < basin.FloorElevation)
                { basin.FloorElevation = graph.HydrologicalElevation[cell]; basin.FloorCell = cell; }
                for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
                {
                    int neighbor = topology.Neighbors6[cell * 6 + slot];
                    if (ids[neighbor] >= 0 || !Filled(neighbor) || graph.FilledElevation[neighbor] != level) continue;
                    // Equal-level pools separated by a saddle exactly at the waterline are distinct basins.
                    if (edgeSpill != null && edgeSpill(cell, neighbor) >= level - ElevationEpsilon) continue;
                    ids[neighbor] = id; queue[tail++] = neighbor;
                }
            }
            basin.Cells = new int[tail]; Array.Copy(queue, basin.Cells, tail);
            basin.AreaFraction = planetArea > 0d ? basin.Area / planetArea : 0d;
            // Earliest settled member has an original flood parent outside this basin. Its parent
            // precedes every member, so selecting it cannot introduce a downstream loop.
            foreach (int cell in basin.Cells)
            {
                int parent = graph.FloodParent[cell];
                if (parent < 0 || ids[parent] == id) continue;
                if (basin.SpillFromCell < 0 || graph.FloodRank[cell] < graph.FloodRank[basin.SpillFromCell])
                { basin.SpillFromCell = cell; basin.SpillCell = parent; }
            }
            basin.Terminal = basin.SpillCell < 0 || !graph.Ocean[graph.OutletCell[seed]];
            if (basin.SpillCell >= 0) basin.DownstreamReceiver = graph.DrainageReceiver[basin.SpillCell];
            basin.Selected = enabled && !basin.Terminal && basin.AreaFraction >= minimumAreaFraction && basin.MaximumDepth >= minimumDepth;
            basin.RejectionReason = !enabled ? "disabled" : basin.Terminal ? "terminal-unresolved" : !basin.Selected ? "below-threshold" : null;
            basin.CurrentSurfaceElevation = basin.SpillElevation;
            basins.Add(basin);
        }
        return new GeodesicLakeBasins(basins.ToArray(), ids, enabled);
    }

    public void RefreshMask()
    {
        for (int cell = 0; cell < BasinId.Length; cell++)
        {
            int id = BasinId[cell]; bool lake = Enabled && id >= 0 && Basins[id].Selected;
            LakeMask[cell] = lake; LakeSurfaceElevation[cell] = lake ? Basins[id].CurrentSurfaceElevation : 0f;
        }
    }

    /// <summary>Collapse each selected pool to its one authoritative spill edge without changing terrain.
    /// The receiver DAG, catchment areas and runoff are rebuilt once by the caller.</summary>
    public int[] CreateReceivers(GeodesicGridTopology topology, GeodesicDrainageGraph graph, Func<int, int, float> edgeSpill = null)
    {
        int[] receivers = (int[])graph.DrainageReceiver.Clone();
        var visited = new bool[graph.CellCount]; var queue = new int[graph.CellCount];
        foreach (var basin in Basins)
        {
            if (!basin.Selected) continue;
            int head = 0, tail = 1; queue[0] = basin.SpillFromCell; visited[queue[0]] = true;
            receivers[queue[0]] = basin.SpillCell;
            while (head < tail)
            {
                int cell = queue[head++];
                for (int n = 0; n < topology.NeighborCounts[cell]; n++)
                {
                    int neighbor = topology.Neighbors6[cell * 6 + n];
                    if (visited[neighbor] || BasinId[neighbor] != basin.Id) continue;
                    if (edgeSpill != null && edgeSpill(cell, neighbor) >= basin.SpillElevation - ElevationEpsilon) continue;
                    visited[neighbor] = true; receivers[neighbor] = cell; queue[tail++] = neighbor;
                }
            }
            if (tail != basin.Cells.Length) throw new InvalidOperationException("Lake basin is disconnected from its spill.");
        }
        return receivers;
    }
}
