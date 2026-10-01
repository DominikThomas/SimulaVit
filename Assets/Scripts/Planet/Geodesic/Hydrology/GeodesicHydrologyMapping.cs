using System;
using UnityEngine;

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
    {
        if (mapping == null || simulationOcean == null || completedSurface == null ||
            mapping.SampleCount != completedSurface.Length)
            throw new ArgumentException("Ocean mapping inputs do not match the render geometry.");
        var result = new bool[completedSurface.Length];
        float seaSquared = seaRadius * seaRadius;
        for (int vertex = 0; vertex < result.Length; vertex++)
        {
            int simulationCell = mapping.Samples[vertex].NearestCell;
            result[vertex] = simulationCell >= 0 && simulationCell < simulationOcean.Length &&
                simulationOcean[simulationCell] && completedSurface[vertex].sqrMagnitude < seaSquared;
        }
        return result;
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
