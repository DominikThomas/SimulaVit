using System;
using UnityEngine;

public enum HydrologyTerrainMode
{
    Visible = 0,
    FineDetailRemoved = 1,
    ReducedRidgeAndFineDetail = 2
}

/// <summary>Unconditioned drainage input on the render vertices. Never edits visible
/// geometry, retained-ocean membership, sea level, or bathymetry.</summary>
public static class GeodesicHydrologyTerrain
{
    public static float[] Build(Vector3[] directions, float[] visibleTerrainHeight, bool[] retainedOcean,
        int terrainSeed, PlanetTerrainSettings settings, float radius, bool lowFrequency, bool displacement = true)
        => Build(directions, visibleTerrainHeight, retainedOcean, terrainSeed, settings, radius,
            lowFrequency ? HydrologyTerrainMode.ReducedRidgeAndFineDetail : HydrologyTerrainMode.Visible, displacement);

    public static float[] Build(Vector3[] directions, float[] visibleTerrainHeight, bool[] retainedOcean,
        int terrainSeed, PlanetTerrainSettings settings, float radius, HydrologyTerrainMode mode, bool displacement = true,
        float[] largeScaleRadii = null, float[] rawRadii = null)
    {
        if (mode == HydrologyTerrainMode.Visible) return visibleTerrainHeight;
        if (mode != HydrologyTerrainMode.FineDetailRemoved && mode != HydrologyTerrainMode.ReducedRidgeAndFineDetail)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (directions.Length != visibleTerrainHeight.Length || retainedOcean.Length != directions.Length)
            throw new ArgumentException("Hydrology terrain must match render vertices and retained ocean mask.");
        bool cached = largeScaleRadii != null && rawRadii != null && largeScaleRadii.Length == directions.Length && rawRadii.Length == directions.Length;
        var hydrologyTerrainHeight = new float[directions.Length];
        for (int i = 0; i < directions.Length; i++)
        {
            hydrologyTerrainHeight[i] = visibleTerrainHeight[i];
            if (retainedOcean[i] || !displacement) continue;
            if (mode == HydrologyTerrainMode.ReducedRidgeAndFineDetail)
                hydrologyTerrainHeight[i] = radius + PlanetTerrainSampler.EvaluateHydrologyLowFrequency(directions[i], terrainSeed, settings);
            else
            {
                // Reuse the paired fields from the SAME visible generation. Apply only
                // their difference, preserving completed-surface adjustments/rounding.
                // Fixture meshes without a generation cache use the same existing sampler.
                float raw, largeScale;
                if (cached) { raw = rawRadii[i]; largeScale = largeScaleRadii[i]; }
                else
                {
                    var sample = PlanetTerrainSampler.Evaluate(directions[i], terrainSeed, settings);
                    raw = radius + sample.HeightOffset; largeScale = radius + sample.LargeScaleHeightOffset;
                }
                hydrologyTerrainHeight[i] = visibleTerrainHeight[i] + (largeScale - raw);
            }
        }
        return hydrologyTerrainHeight;
    }

    // Mean absolute edge height difference. Each edge occurs twice on the closed
    // render sphere, so triangle-edge traversal gives the same mean as unique edges.
    public static double Roughness(float[] heights, int[] triangles)
    {
        double sum = 0d;
        for (int t = 0; t < triangles.Length; t += 3)
            for (int k = 0; k < 3; k++)
                sum += Math.Abs(heights[triangles[t + k]] - (double)heights[triangles[t + (k + 1) % 3]]);
        return triangles.Length == 0 ? 0d : sum / triangles.Length;
    }

    public static string Summary(float[] visible, float[] hydrology, int[] triangles,
        bool lowFrequency, PlanetTerrainSettings settings, bool displacement = true)
        => Summary(visible, hydrology, triangles,
            lowFrequency ? HydrologyTerrainMode.ReducedRidgeAndFineDetail : HydrologyTerrainMode.Visible, settings, displacement);

    public static string Summary(float[] visible, float[] hydrology, int[] triangles,
        HydrologyTerrainMode mode, PlanetTerrainSettings settings, bool displacement = true, bool cachedFineDetail = false)
    {
        double absolute = 0d, squared = 0d;
        for (int i = 0; i < visible.Length; i++)
        { double delta = hydrology[i] - (double)visible[i]; absolute += Math.Abs(delta); squared += delta * delta; }
        int count = Math.Max(1, visible.Length);
        var used = mode == HydrologyTerrainMode.ReducedRidgeAndFineDetail ? PlanetTerrainSampler.HydrologyLowFrequencySettings(settings) : settings;
        bool qualifying = PlanetTerrainSampler.IsQualifyingFineDetail(settings);
        if (mode == HydrologyTerrainMode.FineDetailRemoved && qualifying) used.fineDetailAmplitude = 0f;
        return $"[GeodesicHydrologyTerrain] mode={mode} samples={visible.Length} " +
            $"visible({Stats(visible)}) hydrology({Stats(hydrology)}) meanAbsoluteDifference={absolute / count:G9} rmsDifference={Math.Sqrt(squared / count):G9} " +
            $"visibleRoughness={Roughness(visible, triangles):G9} hydrologyRoughness={Roughness(hydrology, triangles):G9} " +
            $"continentOctaves=3 maskOctaves=3 ridgeOctaves={Mathf.Clamp(used.octaves, 1, 8)} fineOctaves=2 fineAmplitude={used.fineDetailAmplitude:G9} " +
            $"qualifyingFineDetail={qualifying} cachedFineDetail={cachedFineDetail} displacement={displacement} units=planet-radius input=before-adaptive-filter retainedOcean=unchanged";
    }

    private static string Stats(float[] heights)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity, mean = 0d, m2 = 0d;
        for (int i = 0; i < heights.Length; i++)
        {
            double h = heights[i], delta = h - mean; mean += delta / (i + 1); m2 += delta * (h - mean);
            min = Math.Min(min, h); max = Math.Max(max, h);
        }
        return FormattableString.Invariant($"min={min:G9} max={max:G9} mean={mean:G9} std={Math.Sqrt(m2 / Math.Max(1, heights.Length)):G9}");
    }
}
