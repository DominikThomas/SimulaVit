using System;
using UnityEngine;

[Serializable]
public class SimulationStartupConfig
{
    [Header("Planet")]
    public int planetSeed = 12345;
    public bool useRandomSeed = true;
    public PlanetGridType gridType = PlanetGridType.LegacyCubeSphere;
    [Range(3, 240)] public int cubeSphereResolution = 10;
    [Range(0, GeodesicGridTopology.MaxSupportedSubdivision)] public int geodesicSubdivisionLevel = 3;

    [Header("Sun / Seasons")]
    [Range(0f, 90f)] public float axisTiltDegrees = 23.5f;
    [Min(0.01f)] public float dayLengthSeconds = 480f;
    [Min(1f)] public float yearLengthInDays = 100f;

    [Header("Climate")]
    public float baseTempKelvin = 273.15f;
    public float insolationTempGain = 45f;

    [Header("Dissolved Ocean Gases")]
    public float initialCO2 = 1.0f;
    public float initialO2 = 0.01f;
    public float initialCH4 = 0f;

    [Header("Geodesic Atmosphere (partial pressure bar)")]
    public float atmosphericN2Bar = 0f;
    public float atmosphericCO2Bar = 0f;
    public float atmosphericO2Bar = 0f;
    public float atmosphericCH4Bar = 0f;
    public float atmosphericH2Bar = 0f;
    public float atmosphericH2SBar = 0f;

    [Header("Ocean Chemistry")]
    public float initialDissolvedFe2Plus = 8f;

    [Header("Vents")]
    [Range(0f, 1f)] public float ventClustering = 0.65f;
    public float ventH2PerTick = 0.006f;
    public float ventH2SPerTick = 0.01f;
    public float ventCO2PerTick = 0f;
    [Tooltip("Fe2 inventory injected by each Geodesic logical vent per fixed resource tick. Legacy resource behavior is unchanged.")]
    public float ventFe2PerTick = 0.002f;
    [Range(0f, 1f)] public float terrestrialVentFraction = 0.25f;

    [Header("Population")]
    [Min(0)] public int initialSpawnCount = 100;
    public bool startPaused;

    [Header("Advanced Environment Timing")]
    [Tooltip("Fixed simulated-time interval used only by ApproximateEcologicalProfiles temperature updates.")]
    public float approximateThermalIntervalSeconds = 2f;
    [Tooltip("Fixed simulated-time interval used by Geodesic dissolved-ocean resource transport.")]
    public float geodesicResourceTransportIntervalSeconds = 5f;
    [Tooltip("Authoritative simulated seconds between Geodesic chemistry diagnostics. Zero or less disables them.")]
    public float chemistryTelemetryIntervalSimSeconds = 60f;
    [Tooltip("Simulation inventory units represented by one atmospheric bar; not a physical Earth constant.")]
    public float atmosphereInventoryPerBar = 100f;
    [Tooltip("Common v1 air-sea exchange half-life. Zero disables exchange; per-gas coefficients remain available on GeodesicAirSeaGasExchange.")]
    public float airSeaExchangeHalfLifeSeconds = 0f;

    public SimulationStartupConfig Clone()
    {
        return (SimulationStartupConfig)MemberwiseClone();
    }
}
