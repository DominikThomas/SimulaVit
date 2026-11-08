using System;
using UnityEngine;

public struct GeodesicHydrologyOceanMappingReport
{
    public int MappedOceanVertices;
    public int HighResBelowSeaVertices;
    public int AuthorityDisagreementVertices;
    public int CoastalHydrologyBarrierCount;
    public int DepressionsCausedByMappedOceanBoundary;
    public string Summary() => $"[GeodesicHydrologyOceanMapping] mappedOceanVertices={MappedOceanVertices} highResBelowSeaVertices={HighResBelowSeaVertices} authorityDisagreementVertices={AuthorityDisagreementVertices} coastalHydrologyBarrierCount={CoastalHydrologyBarrierCount} depressionsCausedByMappedOceanBoundary={DepressionsCausedByMappedOceanBoundary}";
}

public static class GeodesicHydrologyMapping
{
    /// <summary>Validates and returns the existing render-height array without cloning it.</summary>
    public static float[] RequireHydrologicalRadii(IcosphereRenderGeometry geometry, float[] radii)
    {
        if (radii == null || radii.Length != geometry.VertexCount)
            throw new ArgumentException("Hydrological radii must be the existing render-vertex array.");
        return radii;
    }

    /// <summary>Maps authoritative simulation-ocean membership to the final render surface.</summary>
    public static bool[] MapAuthoritativeOcean(IcosphereDirectionMapping mapping, bool[] simulationOcean,
        Vector3[] completedSurface, float seaRadius)
        => MapAuthoritativeOcean(mapping, simulationOcean, completedSurface, seaRadius, out _);

    public static bool[] MapAuthoritativeOcean(IcosphereDirectionMapping mapping, bool[] simulationOcean,
        Vector3[] completedSurface, float seaRadius, out GeodesicHydrologyOceanMappingReport report)
    {
        if (mapping == null || simulationOcean == null || completedSurface == null ||
            mapping.SampleCount != completedSurface.Length)
            throw new ArgumentException("Ocean mapping inputs do not match the render geometry.");
        var result = new bool[completedSurface.Length];
        report = default;
        float seaSquared = seaRadius * seaRadius;
        for (int vertex = 0; vertex < result.Length; vertex++)
        {
            bool belowSea = completedSurface[vertex].sqrMagnitude < seaSquared;
            if (belowSea) report.HighResBelowSeaVertices++;
            IcosphereDirectionSample sample = mapping.Samples[vertex];
            int simulationCell = sample.NearestCell;
            bool nearestAuthority = simulationCell >= 0 && simulationCell < simulationOcean.Length && simulationOcean[simulationCell];
            bool retainedComponentNearby = nearestAuthority;
            int end = sample.NeighborStart + sample.NeighborCount;
            for (int entry = sample.NeighborStart; !retainedComponentNearby && entry < end; entry++)
            {
                int neighbor = mapping.NeighborIndices[entry];
                retainedComponentNearby = neighbor >= 0 && neighbor < simulationOcean.Length && simulationOcean[neighbor];
            }
            result[vertex] = belowSea && retainedComponentNearby;
            if (result[vertex]) report.MappedOceanVertices++;
            if (belowSea && retainedComponentNearby != nearestAuthority) report.AuthorityDisagreementVertices++;
        }
        return result;
    }

    public static void CompleteOceanBoundaryDiagnostics(IGeodesicHydrologyTopology topology,
        GeodesicDrainageGraph graph, Vector3[] completedSurface, float seaRadius,
        ref GeodesicHydrologyOceanMappingReport report)
    {
        if (topology == null || graph == null || completedSurface == null ||
            topology.CellCount != graph.CellCount || completedSurface.Length != graph.CellCount) return;
        float seaSquared = seaRadius * seaRadius;
        for (int cell = 0; cell < graph.CellCount; cell++)
        {
            if (graph.Ocean[cell] || completedSurface[cell].sqrMagnitude >= seaSquared) continue;
            bool adjacentOcean = false;
            for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
                adjacentOcean |= graph.Ocean[topology.Neighbors6[cell * 6 + slot]];
            if (!adjacentOcean) continue;
            report.CoastalHydrologyBarrierCount++;
            if (graph.FillDepth[cell] > GeodesicLakeBasins.ElevationEpsilon)
                report.DepressionsCausedByMappedOceanBoundary++;
        }
    }

    /// <summary>
    /// Distributes each simulation cell's supplied runoff by hydrology-cell area. The integral
    /// associated with each mapped simulation cell is preserved; ocean hydrology nodes receive zero.
    /// </summary>
    public static double[] DistributeSimulationRunoff(IcosphereDirectionMapping mapping,
        double[] simulationRunoff, double[] hydrologyLocalArea, bool[] hydrologyOcean)
    {
        if (mapping == null || simulationRunoff == null || hydrologyLocalArea == null || hydrologyOcean == null ||
            mapping.SampleCount != hydrologyLocalArea.Length || hydrologyOcean.Length != hydrologyLocalArea.Length)
            throw new ArgumentException("Runoff mapping inputs do not match.");
        var mappedArea = new double[simulationRunoff.Length];
        for (int vertex = 0; vertex < hydrologyLocalArea.Length; vertex++)
        {
            int cell = mapping.Samples[vertex].NearestCell;
            if (!hydrologyOcean[vertex] && cell >= 0 && cell < mappedArea.Length) mappedArea[cell] += hydrologyLocalArea[vertex];
        }
        var result = new double[hydrologyLocalArea.Length];
        for (int vertex = 0; vertex < result.Length; vertex++)
        {
            int cell = mapping.Samples[vertex].NearestCell;
            if (hydrologyOcean[vertex] || cell < 0 || cell >= simulationRunoff.Length || mappedArea[cell] <= 0d) continue;
            double value = simulationRunoff[cell];
            if (!double.IsFinite(value) || value < 0d) throw new ArgumentException("Runoff must be finite and nonnegative.");
            result[vertex] = value * hydrologyLocalArea[vertex] / mappedArea[cell];
        }
        return result;
    }
}
