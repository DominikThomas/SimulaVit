using System;
using UnityEngine;

[Serializable]
public class SimulationStartupConfig
{
    public const float DefaultAirSeaExchangeHalfLifeSeconds = 300f;
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

    [Header("Geodesic Atmosphere")]
    [Tooltip("Normal startup atmosphere pressure. Dense-atmosphere authoring must be enabled explicitly in Advanced settings.")]
    public float initialAtmospherePressureBar = 0.85f;
    [Range(0f, 1f)] public float atmosphericCO2Fraction = 1f / 17f;
    [Range(0f, 1f)] public float atmosphericO2Fraction = 0f;
    [Range(0f, 1f)] public float atmosphericCH4Fraction = 0f;
    [Range(0f, 1f)] public float atmosphericH2Fraction = 0f;
    [Range(0f, 1f)] public float atmosphericH2SFraction = 0f;
    [Tooltip("Explicit opt-in for startup pressures above the normal 5 bar range.")]
    public bool allowDenseAtmosphere;

    // Derived compatibility values. New UI/configuration authority is total pressure plus
    // composition; schema-v6 and older files are explicitly migrated from these partials.
    [HideInInspector] public float atmosphericN2Bar = 0.8f;
    [HideInInspector] public float atmosphericCO2Bar = 0.05f;
    public float atmosphericO2Bar = 0f;
    public float atmosphericCH4Bar = 0f;
    public float atmosphericH2Bar = 0f;
    public float atmosphericH2SBar = 0f;

    [Header("Ocean Chemistry")]
    public float initialDissolvedFe2Plus = 8f;

    [Header("Vents")]
    [Range(0f, 1f)] public float ventClustering = 0.65f;
    // Legacy Cube Sphere rates. Field names and behavior are retained for saved-config compatibility.
    public float ventH2PerTick = 0.006f;
    public float ventH2SPerTick = 0.004f;
    public float ventCO2PerTick = 0.02f;
    [Tooltip("Legacy Cube Sphere Fe2 vent rate. Geodesic uses its separate physical-rate field.")]
    public float ventFe2PerTick = 0.002f;
    [Tooltip("Geodesic physical vent inventory rate in concentration*km3 per simulated second.")]
    public float geodesicVentH2PhysicalPerSecond = 10f;
    [Tooltip("Provisional uncalibrated Geodesic physical rate; zero disables this source.")]
    public float geodesicVentH2SPhysicalPerSecond = 0f;
    [Tooltip("Provisional uncalibrated Geodesic physical rate; zero disables this source.")]
    public float geodesicVentCO2PhysicalPerSecond = 0f;
    [Tooltip("Provisional uncalibrated Geodesic physical rate; zero disables this source.")]
    public float geodesicVentFe2PhysicalPerSecond = 0f;
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
    public float atmosphereInventoryPerBar = 1000f;
    [Tooltip("Common v1 air-sea exchange half-life. Zero disables exchange; per-gas coefficients remain available on GeodesicAirSeaGasExchange.")]
    public float airSeaExchangeHalfLifeSeconds = DefaultAirSeaExchangeHalfLifeSeconds;
    [Tooltip("Simulated-time environmental warmup before normal Geodesic founders appear. Zero preserves immediate spawning.")]
    public float geodesicBiologySpawnDelaySeconds = 0f;

    public SimulationStartupConfig Clone()
    {
        return (SimulationStartupConfig)MemberwiseClone();
    }
}
