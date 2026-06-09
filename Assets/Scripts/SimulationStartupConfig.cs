using System;
using UnityEngine;

[Serializable]
public class SimulationStartupConfig
{
    [Header("Planet")]
    public int planetSeed = 12345;
    public bool useRandomSeed = true;

    [Header("Sun / Seasons")]
    [Range(0f, 90f)] public float axisTiltDegrees = 23.5f;
    [Min(0.01f)] public float dayLengthSeconds = 480f;
    [Min(1f)] public float yearLengthInDays = 100f;

    [Header("Climate")]
    public float baseTempKelvin = 273.15f;
    public float insolationTempGain = 45f;

    [Header("Atmosphere")]
    public float initialCO2 = 1.0f;
    public float initialO2 = 0.01f;
    public float initialCH4 = 0f;

    [Header("Ocean Chemistry")]
    public float initialDissolvedFe2Plus = 8f;

    [Header("Vents")]
    public float ventH2PerTick = 0.006f;
    public float ventH2SPerTick = 0.01f;
    public float ventCO2PerTick = 0f;

    [Header("Population")]
    [Min(0)] public int initialSpawnCount = 100;
    public bool startPaused;

    public SimulationStartupConfig Clone()
    {
        return (SimulationStartupConfig)MemberwiseClone();
    }
}
