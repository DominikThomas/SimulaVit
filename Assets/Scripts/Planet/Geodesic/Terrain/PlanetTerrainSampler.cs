using UnityEngine;

public struct PlanetTerrainSample
{
    public float HeightOffset;
    public float ContinentValue;
    public float MountainMask;
    public float RidgeValue;
}

public static class PlanetTerrainSampler
{
    private static readonly Vector3 ContinentDomain = new Vector3(17.31f, -41.77f, 83.19f);
    private static readonly Vector3 MountainDomain = new Vector3(91.7f, -37.2f, 18.4f);
    private static readonly Vector3 WarpXDomain = new Vector3(-12.3f, 48.9f, 73.5f);
    private static readonly Vector3 WarpYDomain = new Vector3(55.8f, 12.4f, -88.1f);
    private static readonly Vector3 WarpZDomain = new Vector3(-63.6f, -25.2f, 34.9f);
    private static readonly Vector3 FineDomain = new Vector3(7.4f, 102.8f, -54.6f);

    public static float EvaluateHeight(Vector3 unitDirection, int masterSeed, PlanetTerrainSettings settings)
    {
        return Evaluate(unitDirection, masterSeed, settings).HeightOffset;
    }

    public static PlanetTerrainSample Evaluate(Vector3 unitDirection, int masterSeed, PlanetTerrainSettings settings)
    {
        Vector3 d = unitDirection.sqrMagnitude > 1e-10f ? unitDirection.normalized : Vector3.up;
        Vector3 seedOffset = BuildSeedOffset(masterSeed);
        float continentScale = Mathf.Max(0.001f, settings.continentScale);
        float continentNoise = Fractal01(d, seedOffset + ContinentDomain, continentScale, 3, 0.55f, 2f);
        float continentSigned = (continentNoise * 2f - 1f) + settings.continentBias;
        float continentHeight = continentSigned * Mathf.Max(0f, settings.continentAmplitude);

        float warpScale = Mathf.Max(0.001f, settings.domainWarpScale);
        float warpStrength = Mathf.Max(0f, settings.domainWarpStrength);
        Vector3 warp = new Vector3(
            SimpleNoise.Evaluate(d * warpScale + seedOffset + WarpXDomain),
            SimpleNoise.Evaluate(d * warpScale + seedOffset + WarpYDomain),
            SimpleNoise.Evaluate(d * warpScale + seedOffset + WarpZDomain)) * warpStrength;
        Vector3 mountainDirection = (d + warp).normalized;

        float maskSource = Fractal01(mountainDirection, seedOffset + MountainDomain * 0.37f, Mathf.Max(0.001f, settings.mountainScale * 0.35f), 3, 0.5f, 2f);
        float softness = Mathf.Max(0.001f, settings.mountainMaskSoftness);
        float mask = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((maskSource - Mathf.Clamp01(settings.mountainCoverageThreshold)) / softness));
        mask *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((continentSigned + 0.45f) / 0.65f));

        float ridgeNoise = Fractal01(mountainDirection, seedOffset + MountainDomain, Mathf.Max(0.001f, settings.mountainScale), Mathf.Clamp(settings.octaves, 1, 8), Mathf.Clamp01(settings.persistence), Mathf.Max(1f, settings.lacunarity));
        float ridge = 1f - Mathf.Abs(2f * ridgeNoise - 1f);
        ridge = Mathf.Pow(Mathf.Clamp01(ridge), Mathf.Max(0.01f, settings.ridgeSharpness));
        float mountainHeight = ridge * mask * Mathf.Max(0f, settings.mountainAmplitude);

        float fine = (Fractal01(d, seedOffset + FineDomain, Mathf.Max(0.001f, settings.fineDetailScale), 2, 0.45f, 2.1f) - 0.5f) * 2f;
        float height = continentHeight + mountainHeight + fine * Mathf.Max(0f, settings.fineDetailAmplitude);
        height = Mathf.Sign(height) * Mathf.Pow(Mathf.Abs(height), Mathf.Max(0.25f, settings.heightContrast));
        float min = Mathf.Min(settings.minimumTerrainOffset, settings.maximumTerrainOffset);
        float max = Mathf.Max(settings.minimumTerrainOffset, settings.maximumTerrainOffset);
        return new PlanetTerrainSample { HeightOffset = Mathf.Clamp(height, min, max), ContinentValue = continentNoise, MountainMask = mask, RidgeValue = ridge };
    }

    private static float Fractal01(Vector3 d, Vector3 offset, float scale, int octaves, float persistence, float lacunarity)
    {
        float amp = 1f, freq = scale, total = 0f, ampSum = 0f;
        for (int i = 0; i < octaves; i++)
        {
            total += (SimpleNoise.Evaluate(d * freq + offset) * 0.5f + 0.5f) * amp;
            ampSum += amp; amp *= persistence; freq *= lacunarity;
        }
        return ampSum > 0f ? total / ampSum : 0.5f;
    }

    private static Vector3 BuildSeedOffset(int seed)
    {
        System.Random random = new System.Random(seed);
        return new Vector3((float)(random.NextDouble() * 2000.0 - 1000.0), (float)(random.NextDouble() * 2000.0 - 1000.0), (float)(random.NextDouble() * 2000.0 - 1000.0));
    }
}
