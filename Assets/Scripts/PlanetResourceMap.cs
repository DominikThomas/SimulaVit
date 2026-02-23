using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum ResourceType
{
    CO2,
    O2,
    H2S,
    S0,
    P,
    Fe,
    Si,
    Ca
}

[DisallowMultipleComponent]
[RequireComponent(typeof(PlanetGenerator))]
public class PlanetResourceMap : MonoBehaviour
{
    [Header("References")]
    public PlanetGenerator planetGenerator;
    [Tooltip("Optional directional light. If empty, will try SunSkyRotator's Light, then RenderSettings.sun.")]
    public Light sunLight;
    public SunSkyRotator sunSkyRotator;

    [Header("Resource Baselines")]
    public float baselineCO2 = 1.0f;
    public float baselineO2 = 0.01f;
    public float baselineS0 = 0.05f;
    public float baselineSi = 0.35f;
    public float baselineCa = 0.25f;

    [Header("Patchiness")]
    public float phosphorusScale = 0.75f;
    public float ironScale = 0.8f;
    public float siliconPatchScale = 0.35f;
    public float calciumPatchScale = 0.25f;

    [Header("H2S Vent Spots")]
    public float ventFrequency = 12f;
    [Range(0f, 1f)] public float ventThreshold = 0.75f;
    public float ventStrength = 2f;

    [Header("Vents")]
    public bool enableVentReplenishment = true;
    public float ventTickSeconds = 0.5f;
    public float ventH2SPerTick = 0.02f;
    public float ventH2SMax = 1.0f;
    public bool ventsOnlyBelowSeaLevel = false;

    [Header("Debug Preview")]
    public ResourceType debugViewType = ResourceType.CO2;
    public Gradient debugGradient;
    public bool drawDebugPoints = true;
    [Range(8, 8192)] public int debugMaxPoints = 512;
    public float debugPointSize = 0.05f;
    [Tooltip("Show text labels with per-tile values for sampled debug points (Scene view only).")]
    public bool drawDebugLabels = false;
    [Range(1, 256)] public int debugLabelMaxPoints = 48;
    [Tooltip("If enabled, colors are normalized against per-resource expected ranges instead of current frame min/max.")]
    public bool debugUseAbsoluteScale = true;
    [Tooltip("Draws known vent cells in a distinct color.")]
    public bool drawVentDebugPoints = false;
    public Color ventDebugColor = Color.magenta;

    private int resolution;
    private Vector3[] cellDirections;

    private float[] co2;
    private float[] o2;
    private float[] h2s;
    private float[] s0;
    private float[] p;
    private float[] fe;
    private float[] si;
    private float[] ca;

    private bool isInitialized;
    private byte[] ventMask;
    private int[] ventCells;
    private float ventTimer;

    public int VentCount => ventCells != null ? ventCells.Length : 0;

    private void Awake()
    {
        if (planetGenerator == null)
        {
            planetGenerator = GetComponent<PlanetGenerator>();
        }

        if (sunSkyRotator == null)
        {
            sunSkyRotator = FindObjectOfType<SunSkyRotator>();
        }

        if (sunLight == null && sunSkyRotator != null)
        {
            sunLight = sunSkyRotator.GetComponent<Light>();
        }

        if (sunLight == null)
        {
            sunLight = RenderSettings.sun;
        }

        EnsureDebugGradient();
    }

    private void Start()
    {
        InitializeIfNeeded();
    }

    private void Update()
    {
        if (!isInitialized || !enableVentReplenishment)
        {
            return;
        }

        float tickSeconds = Mathf.Max(0.0001f, ventTickSeconds);
        ventTimer += Time.deltaTime;

        while (ventTimer >= tickSeconds)
        {
            ventTimer -= tickSeconds;
            ApplyVentReplenishment();
        }
    }

    public float Get(ResourceType t, int cell)
    {
        if (!isInitialized || !IsCellValid(cell))
        {
            return 0f;
        }

        return GetArray(t)[cell];
    }

    public void Add(ResourceType t, int cell, float delta)
    {
        if (!isInitialized || !IsCellValid(cell) || Mathf.Approximately(delta, 0f))
        {
            return;
        }

        float[] arr = GetArray(t);
        arr[cell] = Mathf.Max(0f, arr[cell] + delta);
    }

    /// <summary>
    /// Insolation approximation in [0..1]: max(0, dot(surfaceNormal, sunDirection)).
    /// Expects a world position or direction; this method normalizes either into a direction from the planet center.
    /// </summary>
    public float GetInsolation(Vector3 worldPosOrDir)
    {
        Vector3 dir = ResolveSurfaceDirection(worldPosOrDir);
        Vector3 sunDir = GetSunDirection();
        return Mathf.Max(0f, Vector3.Dot(dir, sunDir));
    }

    /// <summary>
    /// Simple temperature model:
    /// temp = base + insolationBoost * insolation - latitudePenalty * |latitude|
    /// where latitude is in [0..1] from equator (0) to poles (1).
    /// This intentionally keeps behavior stable and easy to tune.
    /// </summary>
    public float GetTemperature(Vector3 dir)
    {
        float insolation = GetInsolation(dir);
        Vector3 n = ResolveSurfaceDirection(dir);
        float latitude = Mathf.Abs(n.y);

        const float baseTemperature = 230f;
        const float insolationBoost = 80f;
        const float latitudePenalty = 45f;

        return baseTemperature + (insolationBoost * insolation) - (latitudePenalty * latitude);
    }

    private void InitializeIfNeeded()
    {
        if (planetGenerator == null)
        {
            return;
        }

        int targetResolution = Mathf.Max(1, planetGenerator.resolution);

        if (isInitialized && targetResolution == resolution)
        {
            return;
        }

        resolution = targetResolution;
        int cellCount = PlanetGridIndexing.GetCellCount(resolution);

        co2 = new float[cellCount];
        o2 = new float[cellCount];
        h2s = new float[cellCount];
        s0 = new float[cellCount];
        p = new float[cellCount];
        fe = new float[cellCount];
        si = new float[cellCount];
        ca = new float[cellCount];
        cellDirections = new Vector3[cellCount];
        ventMask = new byte[cellCount];

        int ventCount = 0;
        for (int cell = 0; cell < cellCount; cell++)
        {
            Vector3 dir = CellIndexToDirection(cell, resolution);
            cellDirections[cell] = dir;

            co2[cell] = baselineCO2;
            o2[cell] = baselineO2;
            s0[cell] = baselineS0;

            float phosphorusNoise = SampleResourceNoise(dir, new Vector3(13.7f, -4.2f, 9.9f));
            float ironNoise = SampleResourceNoise(dir, new Vector3(-8.4f, 3.1f, 15.2f));
            float siliconNoise = SampleResourceNoise(dir, new Vector3(2.3f, 11.9f, -6.6f));
            float calciumNoise = SampleResourceNoise(dir, new Vector3(-12.5f, -7.4f, 4.8f));

            p[cell] = Mathf.Max(0f, phosphorusScale * phosphorusNoise);
            fe[cell] = Mathf.Max(0f, ironScale * ironNoise);
            si[cell] = Mathf.Max(0f, baselineSi + siliconPatchScale * (siliconNoise - 0.5f));
            ca[cell] = Mathf.Max(0f, baselineCa + calciumPatchScale * (calciumNoise - 0.5f));

            float ventNoise = HighFrequencyNoise(dir);
            bool isVent = ventNoise > ventThreshold;
            h2s[cell] = isVent ? (ventNoise - ventThreshold) * ventStrength : 0f;

            if (isVent)
            {
                ventMask[cell] = 1;
                ventCount++;
            }
        }

        ventCells = new int[ventCount];
        int ventWrite = 0;
        for (int cell = 0; cell < cellCount; cell++)
        {
            if (ventMask[cell] != 0)
            {
                ventCells[ventWrite++] = cell;
            }
        }

        ventTimer = 0f;
        isInitialized = true;
        Debug.Log($"Initialized {VentCount} vents", this);
    }

    private void ApplyVentReplenishment()
    {
        if (!enableVentReplenishment || !isInitialized || ventCells == null || ventCells.Length == 0)
        {
            return;
        }

        float perTick = Mathf.Max(0f, ventH2SPerTick);
        if (Mathf.Approximately(perTick, 0f))
        {
            return;
        }

        bool applyCap = ventH2SMax > 0f;
        float oceanRadius = 0f;
        if (ventsOnlyBelowSeaLevel && planetGenerator != null)
        {
            oceanRadius = planetGenerator.GetOceanRadius();
        }

        for (int i = 0; i < ventCells.Length; i++)
        {
            int cell = ventCells[i];

            if (ventsOnlyBelowSeaLevel)
            {
                if (planetGenerator == null)
                {
                    continue;
                }

                Vector3 dir = cellDirections[cell];
                float surfaceRadius = planetGenerator.GetSurfaceRadius(dir);
                if (surfaceRadius >= oceanRadius)
                {
                    continue;
                }
            }

            Add(ResourceType.H2S, cell, perTick);
            if (applyCap)
            {
                h2s[cell] = Mathf.Min(h2s[cell], ventH2SMax);
            }
        }
    }

    private float SampleResourceNoise(Vector3 dir, Vector3 offset)
    {
        if (planetGenerator != null)
        {
            Vector3 shiftedDir = (dir + offset * 0.05f).normalized;
            return planetGenerator.CalculateNoise(shiftedDir);
        }

        return (SimpleNoise.Evaluate(dir * 3f + offset) + 1f) * 0.5f;
    }

    private float HighFrequencyNoise(Vector3 dir)
    {
        Vector3 samplePoint = dir * ventFrequency + new Vector3(17.3f, -9.1f, 5.7f);
        return (SimpleNoise.Evaluate(samplePoint) + 1f) * 0.5f;
    }

    private Vector3 GetSunDirection()
    {
        if (sunLight == null && sunSkyRotator != null)
        {
            sunLight = sunSkyRotator.GetComponent<Light>();
        }

        if (sunLight == null)
        {
            sunLight = RenderSettings.sun;
        }

        if (sunLight != null && sunLight.type == LightType.Directional)
        {
            return (-sunLight.transform.forward).normalized;
        }

        return Vector3.up;
    }

    private Vector3 ResolveSurfaceDirection(Vector3 worldPosOrDir)
    {
        Vector3 center = transform.position;
        Vector3 relative = worldPosOrDir - center;

        if (relative.sqrMagnitude > 0.25f)
        {
            return relative.normalized;
        }

        if (worldPosOrDir.sqrMagnitude > Mathf.Epsilon)
        {
            return worldPosOrDir.normalized;
        }

        return Vector3.up;
    }

    private float[] GetArray(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.CO2: return co2;
            case ResourceType.O2: return o2;
            case ResourceType.H2S: return h2s;
            case ResourceType.S0: return s0;
            case ResourceType.P: return p;
            case ResourceType.Fe: return fe;
            case ResourceType.Si: return si;
            case ResourceType.Ca: return ca;
            default: return co2;
        }
    }

    private bool IsCellValid(int cell)
    {
        return co2 != null && cell >= 0 && cell < co2.Length;
    }

    private static Vector3 CellIndexToDirection(int cell, int resolution)
    {
        int cellsPerFace = resolution * resolution;
        int face = Mathf.Clamp(cell / cellsPerFace, 0, 5);
        int local = Mathf.Clamp(cell % cellsPerFace, 0, cellsPerFace - 1);

        int x = local % resolution;
        int y = local / resolution;

        float u = ((x + 0.5f) / resolution) * 2f - 1f;
        float v = ((y + 0.5f) / resolution) * 2f - 1f;

        Vector3 pointOnCube;
        switch (face)
        {
            case 0: pointOnCube = new Vector3(u, 1f, -v); break;      // +Y
            case 1: pointOnCube = new Vector3(-u, -1f, -v); break;    // -Y
            case 2: pointOnCube = new Vector3(-1f, -v, -u); break;    // -X
            case 3: pointOnCube = new Vector3(1f, -v, u); break;      // +X
            case 4: pointOnCube = new Vector3(-v, u, 1f); break;      // +Z
            default: pointOnCube = new Vector3(-v, -u, -1f); break;   // -Z
        }

        return pointOnCube.normalized;
    }

    private void EnsureDebugGradient()
    {
        if (debugGradient != null)
        {
            return;
        }

        debugGradient = new Gradient();
        debugGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.blue, 0f),
                new GradientColorKey(Color.green, 0.5f),
                new GradientColorKey(Color.red, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });
    }

    private void OnValidate()
    {
        if (planetGenerator == null)
        {
            planetGenerator = GetComponent<PlanetGenerator>();
        }

        EnsureDebugGradient();

        ventTickSeconds = Mathf.Max(0.0001f, ventTickSeconds);
        ventH2SPerTick = Mathf.Max(0f, ventH2SPerTick);
    }


    private string BuildDebugLabelText(int cellIndex)
    {
        return $"{debugViewType}: {Get(debugViewType, cellIndex):0.###}\nCO2: {Get(ResourceType.CO2, cellIndex):0.###}\nH2S: {Get(ResourceType.H2S, cellIndex):0.###}\nS0: {Get(ResourceType.S0, cellIndex):0.###}";
    }


    private float GetAbsoluteDebugMax(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.CO2: return Mathf.Max(0.0001f, baselineCO2);
            case ResourceType.O2: return Mathf.Max(0.0001f, baselineO2);
            case ResourceType.H2S: return Mathf.Max(0.0001f, ventStrength);
            case ResourceType.S0: return Mathf.Max(0.0001f, baselineS0);
            case ResourceType.P: return Mathf.Max(0.0001f, phosphorusScale);
            case ResourceType.Fe: return Mathf.Max(0.0001f, ironScale);
            case ResourceType.Si: return Mathf.Max(0.0001f, baselineSi + siliconPatchScale);
            case ResourceType.Ca: return Mathf.Max(0.0001f, baselineCa + calciumPatchScale);
            default: return 1f;
        }
    }

    private float GetDebugColorT(float value, float minValue, float range)
    {
        if (!debugUseAbsoluteScale)
        {
            return (value - minValue) / range;
        }

        float absoluteMax = GetAbsoluteDebugMax(debugViewType);
        return Mathf.Clamp01(value / absoluteMax);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugPoints)
        {
            return;
        }

        InitializeIfNeeded();

        if (!isInitialized || co2 == null || co2.Length == 0 || cellDirections == null)
        {
            return;
        }

        float[] array = GetArray(debugViewType);
        if (array == null || array.Length == 0)
        {
            return;
        }

        float minValue = float.MaxValue;
        float maxValue = float.MinValue;
        for (int i = 0; i < array.Length; i++)
        {
            minValue = Mathf.Min(minValue, array[i]);
            maxValue = Mathf.Max(maxValue, array[i]);
        }

        float range = Mathf.Max(0.0001f, maxValue - minValue);
        int stride = Mathf.Max(1, Mathf.CeilToInt((float)array.Length / Mathf.Max(1, debugMaxPoints)));

        int labelStride = Mathf.Max(1, Mathf.CeilToInt((float)array.Length / Mathf.Max(1, debugLabelMaxPoints)));

        for (int i = 0; i < array.Length; i += stride)
        {
            float t = GetDebugColorT(array[i], minValue, range);
            Gizmos.color = debugGradient.Evaluate(t);

            Vector3 dir = cellDirections[i];
            float surfaceRadius = planetGenerator != null ? planetGenerator.GetSurfaceRadius(dir) : 1f;
            Vector3 world = transform.position + dir * surfaceRadius;
            Gizmos.DrawSphere(world, debugPointSize);

#if UNITY_EDITOR
            if (drawDebugLabels && i % labelStride == 0)
            {
                Handles.color = Gizmos.color;
                Handles.Label(world + dir * (debugPointSize * 1.6f), BuildDebugLabelText(i));
            }
#endif

            if (drawVentDebugPoints && ventMask != null && ventMask[i] != 0)
            {
                Gizmos.color = ventDebugColor;
                Gizmos.DrawWireSphere(world, debugPointSize * 1.25f);
            }
        }
    }
}
