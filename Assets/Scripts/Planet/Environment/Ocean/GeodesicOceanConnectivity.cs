using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using S = GeodesicSnapshotIO;

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
    public int PrimaryRetainedComponent { get; private set; } = -1;
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
    public static int FindNearestCell(IGeodesicHydrologyTopology topology, Vector3 localDirection, int startCell = 0)
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

    public static GeodesicOceanConnectivity Build(IGeodesicHydrologyTopology topology, float[] terrainRadius,
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
            if (retained)
            {
                result.RetainedComponents++;
                result.RetainedCells += tail;
                int primary = result.PrimaryRetainedComponent;
                if (primary < 0 || fraction > components[primary].PlanetAreaFraction) result.PrimaryRetainedComponent = id;
            }
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

    internal void WriteSnapshot(BinaryWriter w)
    {
        S.Flags(w, BelowSeaLevel);
        S.Flags(w, OceanMask);
        S.Flags(w, PotentialLakeBasin);
        S.Flags(w, CoastalLand);
        S.Flags(w, CoastalOcean);
        S.Write(w, OceanNeighborCounts, (b, value) => b.Write(value));
        S.Ints(w, ComponentId);
        S.Write(w, Components, (b, basin) =>
        {
            b.Write(basin.CellCount); b.Write(basin.SurfaceArea); b.Write(basin.PlanetAreaFraction); b.Write(basin.Retained);
        });
        w.Write(BelowSeaCells);
        w.Write(PrimaryRetainedComponent);
        w.Write(RetainedComponents);
        w.Write(ExcludedComponents);
        w.Write(RetainedCells);
        w.Write(ExcludedCells);
        w.Write(LargestAreaFraction);
        w.Write(TotalSurfaceArea);
        w.Write(MinimumAreaFraction);
        w.Write(FilterEnabled);
    }

    internal static GeodesicOceanConnectivity ReadSnapshot(BinaryReader r, int cells)
    {
        var result = new GeodesicOceanConnectivity
        {
            BelowSeaLevel = S.Flags(r, cells),
            OceanMask = S.Flags(r, cells),
            PotentialLakeBasin = S.Flags(r, cells),
            CoastalLand = S.Flags(r, cells),
            CoastalOcean = S.Flags(r, cells),
            OceanNeighborCounts = S.Read(r, cells, 1, b => b.ReadByte(), true),
            ComponentId = S.Read(r, cells, 4, b => S.Id(b, cells, -1), true),
            Components = S.Read(r, cells, 21, b => new Basin(S.Id(b, cells + 1, 1), S.Double(b), S.Double(b), S.Flag(b)))
        };
        result.BelowSeaCells = S.Id(r, cells + 1);
        result.PrimaryRetainedComponent = S.Id(r, result.Components.Length, -1);
        result.RetainedComponents = S.Id(r, result.Components.Length + 1);
        result.ExcludedComponents = S.Id(r, result.Components.Length + 1);
        result.RetainedCells = S.Id(r, cells + 1);
        result.ExcludedCells = S.Id(r, cells + 1);
        result.LargestAreaFraction = S.Double(r);
        result.TotalSurfaceArea = S.Double(r);
        result.MinimumAreaFraction = S.Float(r);
        result.FilterEnabled = S.Flag(r);
        for (int i = 0; i < cells; i++)
        {
            int id = result.ComponentId[i];
            S.Require(id < result.Components.Length && result.OceanNeighborCounts[i] <= 6 &&
                result.OceanMask[i] == (id >= 0 && result.Components[id].Retained) &&
                (!result.PotentialLakeBasin[i] || (id >= 0 && !result.OceanMask[i])),
                "Ocean membership is inconsistent with its component.");
        }
        return result;
    }

    public string Describe() => $"[GeodesicOceanConnectivity] belowSeaCells={BelowSeaCells}, components={Components.Length}, retainedOceanComponents={RetainedComponents}, excludedComponents={ExcludedComponents}, retainedOceanCells={RetainedCells}, excludedBelowSeaCells={ExcludedCells}, largestComponentAreaFraction={LargestAreaFraction:F8}, minimumOceanComponentAreaFraction={MinimumAreaFraction:F6}, filterEnabled={FilterEnabled}";
}
