using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Generation-time submerged components. Arrays are immutable by contract.
/// Areas are spherical surface areas, not cell counts; no terrain heights are modified.</summary>
public sealed class GeodesicOceanConnectivity
{
    public const float DefaultMinimumAreaFraction = 0.001f;
    public const float MaximumAreaFraction = 0.05f;

    public readonly struct Basin
    {
        public readonly int CellCount;
        public readonly double SurfaceArea, PlanetAreaFraction;
        public readonly bool Retained;
        public Basin(int cells, double area, double fraction, bool retained)
        { CellCount = cells; SurfaceArea = area; PlanetAreaFraction = fraction; Retained = retained; }
    }

    public bool[] BelowSeaLevel { get; private set; }
    public bool[] OceanMask { get; private set; }
    public bool[] PotentialLakeBasin { get; private set; }
    public bool[] CoastalLand { get; private set; }
    public bool[] CoastalOcean { get; private set; }
    public byte[] OceanNeighborCounts { get; private set; }
    public int[] ComponentId { get; private set; }
    public Basin[] Components { get; private set; }
    public int BelowSeaCells { get; private set; }
    public int RetainedComponents { get; private set; }
    public int ExcludedComponents { get; private set; }
    public int RetainedCells { get; private set; }
    public int ExcludedCells { get; private set; }
    public double LargestAreaFraction { get; private set; }
    public double TotalSurfaceArea { get; private set; }
    public float MinimumAreaFraction { get; private set; }
    public bool FilterEnabled { get; private set; }

    public static float NormalizeThreshold(float fraction) => float.IsNaN(fraction) || float.IsInfinity(fraction)
        ? DefaultMinimumAreaFraction : Mathf.Clamp(fraction, 0f, MaximumAreaFraction);

    /// <summary>Walk the convex spherical neighbor graph to the nearest cell. No allocations or flood fill.
    /// A caller can reuse the previous cell for spatially coherent local-direction queries.</summary>
    public static int FindNearestCell(GeodesicGridTopology topology, Vector3 localDirection, int startCell = 0)
    {
        if (topology == null || topology.CellCount == 0) return -1;
        int current = Math.Max(0, Math.Min(topology.CellCount - 1, startCell));
        while (true)
        {
            int best = current;
            float bestDot = Vector3.Dot(localDirection, topology.CellDirections[current]);
            for (int n = 0; n < topology.NeighborCounts[current]; n++)
            {
                int neighbor = topology.Neighbors6[current * 6 + n];
                float dot = Vector3.Dot(localDirection, topology.CellDirections[neighbor]);
                if (dot > bestDot || (dot == bestDot && neighbor < best)) { best = neighbor; bestDot = dot; }
            }
            if (best == current) return current;
            current = best;
        }
    }

    public static GeodesicOceanConnectivity Build(GeodesicGridTopology topology, float[] terrainRadius,
        float seaRadius, bool oceanEnabled, bool filterEnabled, float minimumAreaFraction, bool oceanWorld = false)
    {
        if (topology == null) throw new ArgumentNullException(nameof(topology));
        int count = topology.CellCount;
        if (terrainRadius == null || terrainRadius.Length != count) throw new ArgumentException("Terrain count must match topology.");
        var result = new GeodesicOceanConnectivity {
            BelowSeaLevel = new bool[count], OceanMask = new bool[count], PotentialLakeBasin = new bool[count],
            CoastalLand = new bool[count], CoastalOcean = new bool[count], OceanNeighborCounts = new byte[count],
            ComponentId = new int[count], FilterEnabled = filterEnabled,
            MinimumAreaFraction = NormalizeThreshold(minimumAreaFraction)
        };
        Array.Fill(result.ComponentId, -1);
        double radiusSquared = (double)seaRadius * seaRadius;
        for (int i = 0; i < count; i++)
        {
            result.TotalSurfaceArea += topology.UnitCellAreas[i] * radiusSquared;
            result.BelowSeaLevel[i] = terrainRadius[i] < seaRadius;
            if (result.BelowSeaLevel[i]) result.BelowSeaCells++;
        }
        var components = new List<Basin>();
        var queue = new int[count];
        for (int seed = 0; seed < count; seed++)
        {
            // OceanWorld intentionally retains the existing all-ocean override.
            if ((!result.BelowSeaLevel[seed] && !oceanWorld) || result.ComponentId[seed] >= 0) continue;
            int id = components.Count, head = 0, tail = 1;
            queue[0] = seed; result.ComponentId[seed] = id;
            double area = 0d;
            while (head < tail)
            {
                int cell = queue[head++];
                area += topology.UnitCellAreas[cell] * radiusSquared;
                for (int n = 0; n < topology.NeighborCounts[cell]; n++)
                {
                    int neighbor = topology.Neighbors6[cell * 6 + n];
                    if ((!result.BelowSeaLevel[neighbor] && !oceanWorld) || result.ComponentId[neighbor] >= 0) continue;
                    result.ComponentId[neighbor] = id; queue[tail++] = neighbor;
                }
            }
            double fraction = result.TotalSurfaceArea > 0d ? area / result.TotalSurfaceArea : 0d;
            bool excluded = oceanEnabled && filterEnabled && !oceanWorld && fraction < result.MinimumAreaFraction;
            bool retained = oceanEnabled && !excluded;
            components.Add(new Basin(tail, area, fraction, retained));
            result.LargestAreaFraction = Math.Max(result.LargestAreaFraction, fraction);
            if (retained) { result.RetainedComponents++; result.RetainedCells += tail; }
            if (excluded) { result.ExcludedComponents++; result.ExcludedCells += tail; }
            for (int i = 0; i < tail; i++)
            { result.OceanMask[queue[i]] = retained; result.PotentialLakeBasin[queue[i]] = excluded; }
        }
        result.Components = components.ToArray();
        for (int cell = 0; cell < count; cell++)
        {
            int neighbors = topology.NeighborCounts[cell];
            for (int n = 0; n < neighbors; n++)
                if (result.OceanMask[topology.Neighbors6[cell * 6 + n]]) result.OceanNeighborCounts[cell]++;
            result.CoastalLand[cell] = !result.OceanMask[cell] && result.OceanNeighborCounts[cell] > 0;
            result.CoastalOcean[cell] = result.OceanMask[cell] && result.OceanNeighborCounts[cell] < neighbors;
        }
        return result;
    }

    public string Describe() => $"[GeodesicOceanConnectivity] belowSeaCells={BelowSeaCells}, components={Components.Length}, retainedOceanComponents={RetainedComponents}, excludedComponents={ExcludedComponents}, retainedOceanCells={RetainedCells}, excludedBelowSeaCells={ExcludedCells}, largestComponentAreaFraction={LargestAreaFraction:F8}, minimumOceanComponentAreaFraction={MinimumAreaFraction:F6}, filterEnabled={FilterEnabled}";
}
