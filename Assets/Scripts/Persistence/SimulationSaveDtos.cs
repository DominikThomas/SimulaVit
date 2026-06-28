using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SimulationSaveFile
{
    public const int CurrentSchemaVersion = 2;

    public int schemaVersion = CurrentSchemaVersion;
    public string applicationVersion;
    public string unityVersion;
    public string savedUtc;
    public SimulationClockSnapshot clock;
    public SunSkySnapshot sun;
    public PlanetGeneratorSnapshot planetGenerator;
    public PlanetResourceMapSnapshot resourceMap;
    public ReplicatorPopulationSnapshot population;
    public SimulationSaveDiagnostics diagnostics;
}

[Serializable]
public class SimulationClockSnapshot
{
    public double simulationTimeSeconds;
    public int simulationStepCount;
    public int simulationStepsPerFrame;
    public float simulationSpeedMultiplier;
    public float frameDeltaTime;
    public float simulationDeltaTime;
    public float frameSimulationDeltaTime;
    public bool shouldAdvanceSimulation;
    public bool pauseDetected;
}

[Serializable]
public class SunSkySnapshot
{
    public bool available;
    public SerializableQuaternion rotation;
    public float orbitDegreesPerSecond;
    public SerializableVector3 orbitAxis;
    public bool keepOrbitOnEquator;
    public bool enableSeasons;
    public float axisTiltDegrees;
    public float yearLengthInDays;
    public float seasonalPhaseOffset;
    public bool northernSummerAtPhaseZero;
    public float accumulatedOrbitAngle;
    public bool skyboxSnapshotAvailable;
    public float skyboxRotation;
    public SerializableColor sunColor;
    public float sunEmissionIntensity;
}

[Serializable]
public class PlanetGeneratorSnapshot
{
    public bool available;
    public int resolution;
    public float radius;
    public float seaLevel;
}

[Serializable]
public class PlanetResourceMapSnapshot
{
    public bool available;
    public bool initialized;
    public string resourceName;
    public int resourceSnapshotVersion;
    public int simulationResolution;
    public int cellCount;
    public bool layeredOceanEnabled;
    public int maxOceanLayers;
    public int oceanCellCount;
    public int[] activeLayerCountHistogram;
    public ResourceArrayLengthSnapshot arrayLengths;
    public ResourceSumsSnapshot resourceSums;
    // Mutable authoritative simulation/resource-grid arrays only. These are lower-resolution
    // PlanetResourceMap arrays (or lower-resolution ocean-layer arrays), not visual mesh/terrain data.
    public float[] co2;
    public float[] o2;
    public float[] organicC;
    public float[] h2s;
    public float[] h2;
    public float[] ch4;
    public float[] s0;
    public float[] dissolvedFe2Plus;
    public float[] surfaceTemperatureKelvin;
    public float[] dissolvedOrganicLeak;
    public float[] toxicProteolyticWaste;
    public ResourceArrayDiagnosticsSnapshot co2Diagnostics;
    public ResourceArrayDiagnosticsSnapshot o2Diagnostics;
    public ResourceArrayDiagnosticsSnapshot organicCDiagnostics;
    public ResourceArrayDiagnosticsSnapshot h2sDiagnostics;
    public ResourceArrayDiagnosticsSnapshot h2Diagnostics;
    public ResourceArrayDiagnosticsSnapshot ch4Diagnostics;
    public ResourceArrayDiagnosticsSnapshot s0Diagnostics;
    public ResourceArrayDiagnosticsSnapshot dissolvedFe2PlusDiagnostics;
    public ResourceArrayDiagnosticsSnapshot surfaceTemperatureDiagnostics;
    public ResourceArrayDiagnosticsSnapshot dissolvedOrganicLeakDiagnostics;
    public ResourceArrayDiagnosticsSnapshot toxicProteolyticWasteDiagnostics;
    public TemperatureSummarySnapshot temperature;
    public ResourceTimerSnapshot timers;
}

[Serializable]
public class ResourceArrayDiagnosticsSnapshot
{
    public bool present;
    public int simulationResolution;
    public int cellCount;
    public int arrayLength;
    public double sum;
    public float min;
    public float max;
}

[Serializable]
public class ResourceArrayLengthSnapshot
{
    public int co2;
    public int o2;
    public int organicC;
    public int h2s;
    public int h2;
    public int ch4;
    public int s0;
    public int dissolvedFe2Plus;
    public int surfaceTemperatureKelvin;
}

[Serializable]
public class ResourceSumsSnapshot
{
    public double co2;
    public double o2;
    public double organicC;
    public double h2s;
    public double h2;
    public double ch4;
    public double s0;
    public double dissolvedFe2Plus;
}

[Serializable]
public class TemperatureSummarySnapshot
{
    public bool available;
    public float minKelvin;
    public float maxKelvin;
    public float meanKelvin;
}

[Serializable]
public class ResourceTimerSnapshot
{
    public float ventTimer;
    public float atmosphereTimer;
    public float thermalTimer;
    public double lastThermalSimulationTime;
    public float debugSimulationDeltaTimeUsedByPlanetResourceMap;
    public float debugLastVentDeltaTime;
    public float debugVentTimer;
}

[Serializable]
public class ReplicatorPopulationSnapshot
{
    public int count;
    public List<ReplicatorSnapshot> replicators = new List<ReplicatorSnapshot>();
}

[Serializable]
public class ReplicatorSnapshot
{
    public SerializableVector3 position;
    public SerializableQuaternion rotation;
    public SerializableVector3 currentDirection;
    public SerializableVector3 moveDirection;
    public SerializableVector3 desiredMoveDirection;
    public SerializableVector3 velocity;
    public float energy;
    public float age;
    public float organicCStore;
    public float speedFactor;
    public float attackCooldown;
    public float fearCooldown;
    public string metabolism;
    public string locomotion;
    public float optimalTempMin;
    public float optimalTempMax;
    public float lethalTempMargin;
    public float starveCo2Seconds;
    public float starveH2sSeconds;
    public float starveH2Seconds;
    public float starveLightSeconds;
    public float starveOrganicCFoodSeconds;
    public float starveO2Seconds;
    public float starveCh4Seconds;
    public float starveStoredCSeconds;
    public float o2ToxicSeconds;
    public float o2ComfortMax;
    public float o2StressMax;
    public bool canReplicate;
    public float lastHabitatValue;
    public float tumbleProbability;
    public float nextSenseTime;
    public float movementSeed;
    public float size;
    public SerializableColor color;
    public int currentOceanLayerIndex;
    public int preferredOceanLayerIndex;
    public float maxLifespan;
    public ReplicatorTraitsSnapshot traits;
    public float biomassTarget;
    public float locomotionSkill;
}

[Serializable]
public class ReplicatorTraitsSnapshot
{
    public bool spawnOnlyInSea;
    public bool replicateOnlyInSea;
    public bool moveOnlyInSea;
    public float surfaceMoveSpeedMultiplier;
}

[Serializable]
public class SimulationSaveDiagnostics
{
    public int replicatorCount;
    public int resourceCellCount;
    public bool layeredOceanEnabled;
    public double o2Sum;
    public double co2Sum;
    public double organicCSum;
    public float temperatureMinKelvin;
    public float temperatureMaxKelvin;
    public float temperatureMeanKelvin;
}

[Serializable]
public struct SerializableVector3
{
    public float x;
    public float y;
    public float z;

    public SerializableVector3(Vector3 value)
    {
        x = value.x;
        y = value.y;
        z = value.z;
    }

    public Vector3 ToVector3()
    {
        return new Vector3(x, y, z);
    }
}

[Serializable]
public struct SerializableQuaternion
{
    public float x;
    public float y;
    public float z;
    public float w;

    public SerializableQuaternion(Quaternion value)
    {
        x = value.x;
        y = value.y;
        z = value.z;
        w = value.w;
    }

    public Quaternion ToQuaternion()
    {
        return new Quaternion(x, y, z, w);
    }
}

[Serializable]
public struct SerializableColor
{
    public float r;
    public float g;
    public float b;
    public float a;

    public SerializableColor(Color value)
    {
        r = value.r;
        g = value.g;
        b = value.b;
        a = value.a;
    }

    public Color ToColor()
    {
        return new Color(r, g, b, a);
    }
}
