using UnityEngine;

[System.Serializable]
public struct PlanetTerrainSettings
{
    [Min(0f)] public float continentAmplitude;
    [Min(0.001f)] public float continentScale;
    [Range(-1f, 1f)] public float continentBias;
    [Min(0f)] public float mountainAmplitude;
    [Min(0.001f)] public float mountainScale;
    [Range(0f, 1f)] public float mountainCoverageThreshold;
    [Min(0.001f)] public float mountainMaskSoftness;
    [Min(0.01f)] public float ridgeSharpness;
    [Min(0.001f)] public float domainWarpScale;
    [Min(0f)] public float domainWarpStrength;
    [Min(0.001f)] public float fineDetailScale;
    [Min(0f)] public float fineDetailAmplitude;
    public float minimumTerrainOffset;
    public float maximumTerrainOffset;
    [Range(1, 8)] public int octaves;
    [Range(0f, 1f)] public float persistence;
    [Min(1f)] public float lacunarity;
    [Range(0.25f, 4f)] public float heightContrast;

    public static PlanetTerrainSettings Earthlike => new PlanetTerrainSettings
    {
        continentAmplitude = 0.09f,
        continentScale = 0.75f,
        continentBias = -0.05f,
        mountainAmplitude = 0.16f,
        mountainScale = 4.75f,
        mountainCoverageThreshold = 0.58f,
        mountainMaskSoftness = 0.18f,
        ridgeSharpness = 1.65f,
        domainWarpScale = 1.35f,
        domainWarpStrength = 0.28f,
        fineDetailScale = 18f,
        fineDetailAmplitude = 0.018f,
        minimumTerrainOffset = -0.09f,
        maximumTerrainOffset = 0.22f,
        octaves = 5,
        persistence = 0.48f,
        lacunarity = 2f,
        heightContrast = 1.15f
    };
}
