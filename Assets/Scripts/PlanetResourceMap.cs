using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum ResourceType
{
    CO2 = 0,
    O2 = 1,
    OrganicC = 2,
    H2S = 3,
    H2 = 4,
    S0 = 5,
    P = 6,
    Fe = 7,
    Si = 8,
    Ca = 9,
    DissolvedOrganicLeak = 10,
    ToxicProteolyticWaste = 11,
    DissolvedFe2Plus = 12,
    OxidizedFeSediment = 13
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

    [Header("Vent Strength")]
    public float ventStrengthMin = 0.25f;
    public float ventStrengthMax = 1.0f;
    public float ventNoiseScale = 3.0f;
    [Range(0f, 1f)] public float ventThreshold = 0.7f;

    [Header("Vents")]
    public bool enableVentReplenishment = true;
    public float ventTickSeconds = 0.5f;
    public bool ventsOnlyBelowSeaLevel = false;

    [Header("Vent Chemistry Balancing")]
    public float ventH2SPerTick = 0.01f;
    public float ventH2PerTick = 0.006f;
    public float ventCO2PerTick = 0f;

    [Header("Vent Resource Caps")]
    public float ventH2SMax = 1.0f;
    public float ventH2Max = 1.5f;

    [Header("Vent Mixing / Diffusion")]
    [Range(0f, 1f)] public float h2sDiffuseStrength = 0.08f;
    [Range(0, 4)] public int h2sDiffusePasses = 1;
    [Range(0f, 1f)] public float h2DiffuseStrength = 0.22f;
    [Range(0, 4)] public int h2DiffusePasses = 1;
    public float ventResourceDecayPerSecond = 0.1f;

    [Header("Natural Oxidation")]
    public bool enableNaturalOxidation = true;
    [Tooltip("Very slow fraction of local environmental OrganicC oxidized per atmosphere tick when O2 is available.")]
    [Range(0f, 1f)] public float naturalOxidationFractionPerTick = 0.0005f;
    [Tooltip("Minimum local O2 required before spontaneous oxidation can occur.")]
    public float naturalOxidationO2HalfSaturation = 0.02f;

    [Header("Atmosphere Mixing")]
    public bool enableAtmosphereMixing = true;
    public float atmosphereTickSeconds = 0.5f;
    public float landExchangeRate = 0.25f;
    public float oceanExchangeRate = 0.05f;
    [Tooltip("Fraction of atmospheric O2 drawdown demand from Fe2+ oxidation that can be transferred into ocean cells each atmosphere tick.")]
    [Range(0f, 1f)] public float atmosphereToOceanO2TransferFractionPerTick = 1f;

    [Header("Atmosphere Debug")]
    public float debugGlobalCO2;
    public float debugGlobalO2;

    [Header("Ocean Dissolved Chemistry")]
    [Tooltip("Initial dissolved Fe2+ loaded into each ocean cell. Represents a large reduced-iron ocean reservoir before oxygenation.")]
    public float initialDissolvedFe2PlusPerOceanCell = 8f;
    [Tooltip("Optional vent source of dissolved Fe2+ per vent tick, scaled by vent strength.")]
    public float ventDissolvedFe2PlusPerTick = 0f;
    [Tooltip("First-order dissolved Fe2+ oxidation rate (s^-1).")]
    public float fe2PlusOxidationRatePerSecond = 0.004f;
    [Tooltip("O2 consumed per 1 Fe2+ oxidized. Simplified stoichiometry for tunable oxygen sink strength.")]
    public float o2ConsumptionPerFe2PlusOxidized = 0.25f;

    [Header("Ocean Chemistry Debug")]
    public float debugDissolvedFe2PlusOceanMean;
    public float debugDissolvedFe2PlusTotal;
    [Range(0f, 1f)] public float debugDissolvedFe2PlusRemainingFraction = 1f;
    public float debugOxidizedFeSedimentTotal;

    [Header("Temperature Model")]
    public float baseTempKelvin = 273.15f; // 0 °C baseline
    public float insolationTempGain = 35f; // +35 K at full sun
    public float ventTempGain = 120f; // +120 K at strong vent core
    [Range(0f, 1f)] public float oceanTempDamping = 0.5f;

    [Header("Temperature - Vent Heat Gradient")]
    public bool enableVentHeatGradient = true;
    [Range(0, 8)] public int ventHeatBlurPasses = 3;
    [Range(0f, 1f)] public float ventHeatSpread = 0.4f;
    [Tooltip("Clamp for ocean temperatures in Kelvin.")]
    public float maxOceanTempKelvin = 500f;
    [Tooltip("Clamp for land temperatures in Kelvin.")]
    public float maxLandTempKelvin = 500f;
    [Tooltip("Global physical minimum temperature clamp in Kelvin.")]
    public float minTempKelvin = 150f;

    [Header("Scent Fields")]
    [Tooltip("Enable diffuse chemical cue fields used for scent-based predation/fear steering.")]
    public bool enableScentFields = true;
    [Tooltip("Decay rate for prey-emitted dissolved organic leak field.")]
    public float dissolvedOrganicLeakDecayPerSecond = 0.6f;
    [Tooltip("Decay rate for predator-emitted toxic proteolytic waste field.")]
    public float toxicProteolyticWasteDecayPerSecond = 0.8f;
    [Range(0f, 1f)] public float scentDiffuseStrength = 0.25f;
    [Range(0, 4)] public int scentDiffusePasses = 1;
    public float scentMaxPerCell = 10f;
    public float scentUpdateInterval = 0.2f;

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
    private float[] organicC;
    private float[] h2s;
    private float[] h2;
    private float[] s0;
    private float[] p;
    private float[] fe;
    private float[] oxidizedFeSediment;
    private float[] si;
    private float[] ca;
    private readonly Dictionary<ResourceType, float[]> oceanDissolvedResources = new Dictionary<ResourceType, float[]>();
    private float initialDissolvedFe2PlusTotal;

    private bool isInitialized;
    public float[] ventStrength;
    public float[] toxicProteolyticWaste;
    public float[] dissolvedOrganicLeak;
    private float[] ventHeat;
    private float[] ventHeatTmp;
    private float[] h2sMixTmp;
    private float[] h2MixTmp;
    private float[] scentWasteTmp;
    private float[] scentLeakTmp;
    private int[] ventHeatNeighbors;
    private byte[] ventMask;
    private int[] ventCells;
    private byte[] oceanMask;
    private float ventTimer;
    private float atmosphereTimer;

    private const int NeighborCount = 6;


    public int VentCount => ventCells != null ? ventCells.Length : 0;
    public int[] VentCells => ventCells;
    public Vector3[] CellDirs => cellDirections;

    private void Awake()
    {
        if (planetGenerator == null)
        {
            planetGenerator = GetComponent<PlanetGenerator>();
        }

        ResolveSunReferences();

        EnsureDebugGradient();
    }

    private void Start()
    {
        InitializeIfNeeded();
    }

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        if (enableVentReplenishment)
        {
            float ventTick = Mathf.Max(0.0001f, ventTickSeconds);
            ventTimer += Time.deltaTime;

            while (ventTimer >= ventTick)
            {
                ventTimer -= ventTick;
                ApplyVentReplenishment();
            }
        }

        if (enableAtmosphereMixing)
        {
            float atmosphereTick = Mathf.Max(0.0001f, atmosphereTickSeconds);
            atmosphereTimer += Time.deltaTime;

            while (atmosphereTimer >= atmosphereTick)
            {
                atmosphereTimer -= atmosphereTick;
                ApplyAtmosphereMixing();
                ApplyNaturalOxidation();
                TransferAtmosphericO2ToOceanFe2Demand(atmosphereTick);
                ApplyDissolvedFe2PlusOxidation(atmosphereTick);
                ApplyLocalResourceMixing(atmosphereTick);
                UpdateAtmosphereDebugMeans();
                UpdateOceanChemistryDebugStats();
            }
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

        if (IsOceanDissolvedResource(t) && !IsOceanCell(cell))
        {
            return;
        }

        float[] arr = GetArray(t);
        arr[cell] = Mathf.Max(0f, arr[cell] + delta);
    }

    public bool IsVolatile(ResourceType t)
    {
        return t == ResourceType.CO2 || t == ResourceType.O2;
    }

    public bool IsOceanDissolvedResource(ResourceType resourceType)
    {
        if (resourceType == ResourceType.DissolvedFe2Plus)
        {
            return true;
        }

        return oceanDissolvedResources.ContainsKey(resourceType);
    }

    public bool IsOceanCell(int cell)
    {
        if (!isInitialized || !IsCellValid(cell) || oceanMask == null)
        {
            return false;
        }

        return oceanMask[cell] != 0;
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
    /// Kelvin-based temperature model:
    /// temp[K] = base[K] + insolationGain[K] * insolation + ventHeating[K].
    /// If a cell is underwater, insolation swings can be damped by oceanTempDamping.
    /// </summary>
    public float GetTemperature(Vector3 dir, int cellIndex = -1)
    {
        Vector3 surfaceDir = ResolveSurfaceDirection(dir);
        float insolation = Mathf.Clamp01(GetInsolation(surfaceDir));

        if (cellIndex < 0 && isInitialized && resolution > 0)
        {
            cellIndex = PlanetGridIndexing.DirectionToCellIndex(surfaceDir, resolution);
        }

        bool underwater = IsOceanCell(cellIndex);
        float insolationDamping = underwater ? Mathf.Clamp01(oceanTempDamping) : 1f;
        float insolationTerm = Mathf.Max(0f, insolationTempGain) * insolation * insolationDamping;

        float ventTerm = 0f;
        if (ventHeat != null && cellIndex >= 0 && cellIndex < ventHeat.Length)
        {
            ventTerm = Mathf.Max(0f, ventHeat[cellIndex]);
        }

        float tempKelvin = baseTempKelvin + insolationTerm + ventTerm;
        float tempMax = underwater ? Mathf.Max(minTempKelvin + 1f, maxOceanTempKelvin) : Mathf.Max(minTempKelvin + 1f, maxLandTempKelvin);
        return Mathf.Clamp(tempKelvin, minTempKelvin, tempMax);
    }

    public void GetTemperatureStats(out float meanKelvin, out float minKelvin, out float maxKelvin)
    {
        meanKelvin = baseTempKelvin;
        minKelvin = baseTempKelvin;
        maxKelvin = baseTempKelvin;

        if (!isInitialized || cellDirections == null || cellDirections.Length == 0)
        {
            return;
        }

        float sum = 0f;
        minKelvin = float.MaxValue;
        maxKelvin = float.MinValue;

        for (int cell = 0; cell < cellDirections.Length; cell++)
        {
            float temp = GetTemperature(cellDirections[cell], cell);
            sum += temp;
            minKelvin = Mathf.Min(minKelvin, temp);
            maxKelvin = Mathf.Max(maxKelvin, temp);
        }

        meanKelvin = sum / cellDirections.Length;
        if (!float.IsFinite(meanKelvin))
        {
            meanKelvin = baseTempKelvin;
        }

        if (!float.IsFinite(minKelvin) || !float.IsFinite(maxKelvin))
        {
            minKelvin = baseTempKelvin;
            maxKelvin = baseTempKelvin;
        }
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
        organicC = new float[cellCount];
        h2s = new float[cellCount];
        h2 = new float[cellCount];
        s0 = new float[cellCount];
        p = new float[cellCount];
        fe = new float[cellCount];
        oxidizedFeSediment = new float[cellCount];
        si = new float[cellCount];
        ca = new float[cellCount];
        cellDirections = new Vector3[cellCount];
        ventMask = new byte[cellCount];
        ventStrength = new float[cellCount];
        ventHeat = new float[cellCount];
        ventHeatTmp = new float[cellCount];
        h2sMixTmp = new float[cellCount];
        h2MixTmp = new float[cellCount];
        ventHeatNeighbors = new int[cellCount * NeighborCount];
        oceanMask = new byte[cellCount];
        ConfigureOceanDissolvedSpecies(cellCount);
        EnsureScentArrays(cellCount);

        int ventCount = 0;
        float oceanRadius = planetGenerator.GetOceanRadius();
        for (int cell = 0; cell < cellCount; cell++)
        {
            Vector3 dir = CellIndexToDirection(cell, resolution);
            cellDirections[cell] = dir;

            float surfaceRadius = planetGenerator.GetSurfaceRadius(dir);
            oceanMask[cell] = surfaceRadius < oceanRadius ? (byte)1 : (byte)0;

            co2[cell] = baselineCO2;
            o2[cell] = baselineO2;
            organicC[cell] = 0f;
            s0[cell] = baselineS0;

            float phosphorusNoise = SampleResourceNoise(dir, new Vector3(13.7f, -4.2f, 9.9f));
            float ironNoise = SampleResourceNoise(dir, new Vector3(-8.4f, 3.1f, 15.2f));
            float siliconNoise = SampleResourceNoise(dir, new Vector3(2.3f, 11.9f, -6.6f));
            float calciumNoise = SampleResourceNoise(dir, new Vector3(-12.5f, -7.4f, 4.8f));

            p[cell] = Mathf.Max(0f, phosphorusScale * phosphorusNoise);
            fe[cell] = Mathf.Max(0f, ironScale * ironNoise);
            si[cell] = Mathf.Max(0f, baselineSi + siliconPatchScale * (siliconNoise - 0.5f));
            ca[cell] = Mathf.Max(0f, baselineCa + calciumPatchScale * (calciumNoise - 0.5f));
            oxidizedFeSediment[cell] = 0f;

            if (oceanMask[cell] != 0)
            {
                SetOceanDissolvedInitial(ResourceType.DissolvedFe2Plus, cell, Mathf.Max(0f, initialDissolvedFe2PlusPerOceanCell));
            }

            float ventNoise = HighFrequencyNoise(dir);
            bool isVent = ventNoise > ventThreshold;
            if (isVent)
            {
                float strengthT = Mathf.InverseLerp(ventThreshold, 1f, ventNoise);
                float strength = Mathf.Lerp(ventStrengthMin, ventStrengthMax, Mathf.Clamp01(strengthT));
                ventStrength[cell] = strength;
                h2s[cell] = strength;
                h2[cell] = strength;
            }
            else
            {
                ventStrength[cell] = 0f;
                h2s[cell] = 0f;
                h2[cell] = 0f;
            }

            if (isVent)
            {
                ventMask[cell] = 1;
                ventCount++;
            }
        }

        BuildVentHeatNeighbors();
        RebuildVentHeatField();

        int total = cellCount;
        Debug.Log($"Vents: {ventCount}/{total} = {(100f * ventCount / total):F1}%");

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
        atmosphereTimer = 0f;
        initialDissolvedFe2PlusTotal = 0f;
        isInitialized = true;
        UpdateAtmosphereDebugMeans();
        UpdateOceanChemistryDebugStats();
        Debug.Log($"Initialized {VentCount} vents", this);
    }

    private void ConfigureOceanDissolvedSpecies(int cellCount)
    {
        oceanDissolvedResources.Clear();
        RegisterOceanDissolvedSpecies(ResourceType.DissolvedFe2Plus, cellCount);
        // Future dissolved ocean species (for example DissolvedCa / DissolvedSi)
        // can be registered here without changing the rest of the resource map flow.
    }

    private void RegisterOceanDissolvedSpecies(ResourceType resourceType, int cellCount)
    {
        if (cellCount <= 0)
        {
            return;
        }

        oceanDissolvedResources[resourceType] = new float[cellCount];
    }

    private void SetOceanDissolvedInitial(ResourceType resourceType, int cell, float value)
    {
        float[] array = GetOceanDissolvedArray(resourceType);
        if (array == null || cell < 0 || cell >= array.Length)
        {
            return;
        }

        array[cell] = Mathf.Max(0f, value);
    }

    private float[] GetOceanDissolvedArray(ResourceType resourceType)
    {
        return oceanDissolvedResources.TryGetValue(resourceType, out float[] array) ? array : null;
    }

    public void EnsureScentArrays()
    {
        int cellCount = co2 != null ? co2.Length : 0;
        EnsureScentArrays(cellCount);
    }

    public void EnsureScentArrays(int cellCount)
    {
        if (cellCount <= 0)
        {
            toxicProteolyticWaste = null;
            dissolvedOrganicLeak = null;
            scentWasteTmp = null;
            scentLeakTmp = null;
            return;
        }

        EnsureArrayCapacity(ref toxicProteolyticWaste, cellCount);
        EnsureArrayCapacity(ref dissolvedOrganicLeak, cellCount);
        EnsureArrayCapacity(ref scentWasteTmp, cellCount);
        EnsureArrayCapacity(ref scentLeakTmp, cellCount);
    }

    public void ClearScents()
    {
        if (toxicProteolyticWaste == null || dissolvedOrganicLeak == null)
        {
            return;
        }

        System.Array.Clear(toxicProteolyticWaste, 0, toxicProteolyticWaste.Length);
        System.Array.Clear(dissolvedOrganicLeak, 0, dissolvedOrganicLeak.Length);
    }

    public void AddScent(ResourceType scentType, int cell, float amount)
    {
        if (!enableScentFields || !isInitialized || !IsCellValid(cell) || amount <= 0f)
        {
            return;
        }

        EnsureScentArrays();
        float cap = Mathf.Max(0f, scentMaxPerCell);
        if (scentType == ResourceType.DissolvedOrganicLeak)
        {
            dissolvedOrganicLeak[cell] = cap > 0f
                ? Mathf.Min(cap, dissolvedOrganicLeak[cell] + amount)
                : dissolvedOrganicLeak[cell] + amount;
        }
        else if (scentType == ResourceType.ToxicProteolyticWaste)
        {
            toxicProteolyticWaste[cell] = cap > 0f
                ? Mathf.Min(cap, toxicProteolyticWaste[cell] + amount)
                : toxicProteolyticWaste[cell] + amount;
        }
    }

    public void ApplyScentDecayAndDiffuse(float dt)
    {
        if (!enableScentFields || toxicProteolyticWaste == null || dissolvedOrganicLeak == null)
        {
            return;
        }

        int cellCount = toxicProteolyticWaste.Length;
        if (cellCount == 0)
        {
            return;
        }

        EnsureScentArrays(cellCount);

        float wasteDecay = Mathf.Exp(-Mathf.Max(0f, toxicProteolyticWasteDecayPerSecond) * Mathf.Max(0f, dt));
        float leakDecay = Mathf.Exp(-Mathf.Max(0f, dissolvedOrganicLeakDecayPerSecond) * Mathf.Max(0f, dt));
        for (int cell = 0; cell < cellCount; cell++)
        {
            toxicProteolyticWaste[cell] = Mathf.Max(0f, toxicProteolyticWaste[cell] * wasteDecay);
            dissolvedOrganicLeak[cell] = Mathf.Max(0f, dissolvedOrganicLeak[cell] * leakDecay);
        }

        if (ventHeatNeighbors == null)
        {
            BuildVentHeatNeighbors();
        }

        if (ventHeatNeighbors == null)
        {
            return;
        }

        int passes = Mathf.Clamp(scentDiffusePasses, 0, 4);
        float strength = Mathf.Clamp01(scentDiffuseStrength);
        float capPerCell = Mathf.Max(0f, scentMaxPerCell);

        for (int pass = 0; pass < passes; pass++)
        {
            for (int cell = 0; cell < cellCount; cell++)
            {
                int baseIndex = cell * NeighborCount;
                float wasteNeighborSum = 0f;
                float leakNeighborSum = 0f;
                int neighborCount = 0;

                for (int n = 0; n < NeighborCount; n++)
                {
                    int neighborCell = ventHeatNeighbors[baseIndex + n];
                    if (neighborCell < 0 || neighborCell >= cellCount)
                    {
                        continue;
                    }

                    wasteNeighborSum += toxicProteolyticWaste[neighborCell];
                    leakNeighborSum += dissolvedOrganicLeak[neighborCell];
                    neighborCount++;
                }

                float wasteNeighborAvg = neighborCount > 0 ? wasteNeighborSum / neighborCount : toxicProteolyticWaste[cell];
                float leakNeighborAvg = neighborCount > 0 ? leakNeighborSum / neighborCount : dissolvedOrganicLeak[cell];
                float blendedWaste = Mathf.Lerp(toxicProteolyticWaste[cell], wasteNeighborAvg, strength);
                float blendedLeak = Mathf.Lerp(dissolvedOrganicLeak[cell], leakNeighborAvg, strength);
                scentWasteTmp[cell] = capPerCell > 0f ? Mathf.Min(capPerCell, Mathf.Max(0f, blendedWaste)) : Mathf.Max(0f, blendedWaste);
                scentLeakTmp[cell] = capPerCell > 0f ? Mathf.Min(capPerCell, Mathf.Max(0f, blendedLeak)) : Mathf.Max(0f, blendedLeak);
            }

            float[] predatorSwap = toxicProteolyticWaste;
            toxicProteolyticWaste = scentWasteTmp;
            scentWasteTmp = predatorSwap;

            float[] preySwap = dissolvedOrganicLeak;
            dissolvedOrganicLeak = scentLeakTmp;
            scentLeakTmp = preySwap;
        }
    }

    private void ApplyAtmosphereMixing()
    {
        if (!enableAtmosphereMixing || !isInitialized || co2 == null || o2 == null || oceanMask == null)
        {
            return;
        }

        int cellCount = co2.Length;
        if (cellCount == 0)
        {
            debugGlobalCO2 = 0f;
            debugGlobalO2 = 0f;
            return;
        }

        float totalCO2 = 0f;
        float totalAtmosphereO2 = 0f;
        int atmosphereCellCount = 0;
        for (int cell = 0; cell < cellCount; cell++)
        {
            totalCO2 += co2[cell];
            if (oceanMask[cell] == 0)
            {
                totalAtmosphereO2 += o2[cell];
                atmosphereCellCount++;
            }
        }

        float invCellCount = 1f / cellCount;
        float globalCO2 = totalCO2 * invCellCount;
        float globalO2 = atmosphereCellCount > 0 ? totalAtmosphereO2 / atmosphereCellCount : 0f;

        float landRate = Mathf.Max(0f, landExchangeRate);
        float oceanRate = Mathf.Max(0f, oceanExchangeRate);

        for (int cell = 0; cell < cellCount; cell++)
        {
            float exchangeRate = oceanMask[cell] != 0 ? oceanRate : landRate;

            float mixedCO2 = co2[cell] + exchangeRate * (globalCO2 - co2[cell]);
            float mixedO2 = o2[cell] + exchangeRate * (globalO2 - o2[cell]);

            co2[cell] = Mathf.Max(0f, mixedCO2);
            o2[cell] = Mathf.Max(0f, mixedO2);
        }

        UpdateAtmosphereDebugMeans();
    }

    private void TransferAtmosphericO2ToOceanFe2Demand(float dt)
    {
        float[] dissolvedFe2Plus = GetOceanDissolvedArray(ResourceType.DissolvedFe2Plus);
        if (!isInitialized || dissolvedFe2Plus == null || o2 == null || oceanMask == null)
        {
            return;
        }

        float transferFraction = Mathf.Clamp01(atmosphereToOceanO2TransferFractionPerTick);
        if (transferFraction <= 0f)
        {
            return;
        }

        float rate = Mathf.Max(0f, fe2PlusOxidationRatePerSecond);
        float o2PerFe2 = Mathf.Max(0f, o2ConsumptionPerFe2PlusOxidized);
        if (rate <= 0f || o2PerFe2 <= 0f)
        {
            return;
        }

        float totalAtmosphericO2 = 0f;
        for (int cell = 0; cell < o2.Length; cell++)
        {
            if (oceanMask[cell] == 0)
            {
                totalAtmosphericO2 += o2[cell];
            }
        }

        if (totalAtmosphericO2 <= 0f)
        {
            return;
        }

        float[] o2DemandByOceanCell = h2MixTmp;
        if (o2DemandByOceanCell == null || o2DemandByOceanCell.Length != o2.Length)
        {
            return;
        }

        float totalDemand = 0f;
        for (int cell = 0; cell < dissolvedFe2Plus.Length; cell++)
        {
            if (oceanMask[cell] == 0)
            {
                o2DemandByOceanCell[cell] = 0f;
                continue;
            }

            float fe2 = dissolvedFe2Plus[cell];
            if (fe2 <= 0f)
            {
                o2DemandByOceanCell[cell] = 0f;
                continue;
            }

            float desiredOxidation = fe2 * rate * Mathf.Max(0f, dt);
            float requiredO2 = desiredOxidation * o2PerFe2;
            float demand = Mathf.Max(0f, requiredO2 - o2[cell]);
            o2DemandByOceanCell[cell] = demand;
            totalDemand += demand;
        }

        if (totalDemand <= 0f)
        {
            return;
        }

        float transferableO2 = Mathf.Min(totalAtmosphericO2, totalDemand * transferFraction);
        if (transferableO2 <= 0f)
        {
            return;
        }

        float toRemove = transferableO2;
        for (int cell = 0; cell < o2.Length; cell++)
        {
            if (oceanMask[cell] != 0)
            {
                continue;
            }

            if (toRemove <= 0f)
            {
                break;
            }

            float share = totalAtmosphericO2 > 0f ? o2[cell] / totalAtmosphericO2 : 0f;
            float remove = Mathf.Min(o2[cell], transferableO2 * share);
            o2[cell] = Mathf.Max(0f, o2[cell] - remove);
            toRemove -= remove;
        }

        if (toRemove > 0f)
        {
            for (int cell = 0; cell < o2.Length && toRemove > 0f; cell++)
            {
                if (oceanMask[cell] != 0 || o2[cell] <= 0f)
                {
                    continue;
                }

                float remove = Mathf.Min(o2[cell], toRemove);
                o2[cell] -= remove;
                toRemove -= remove;
            }
        }

        float transferredO2 = transferableO2 - Mathf.Max(0f, toRemove);
        if (transferredO2 <= 0f)
        {
            return;
        }

        for (int cell = 0; cell < dissolvedFe2Plus.Length; cell++)
        {
            float demand = o2DemandByOceanCell[cell];
            if (demand <= 0f)
            {
                continue;
            }

            float add = transferredO2 * (demand / totalDemand);
            o2[cell] += add;
        }
    }

    private void ApplyLocalResourceMixing(float dt)
    {
        if (!isInitialized || h2s == null || h2 == null || ventHeatNeighbors == null || h2sMixTmp == null || h2MixTmp == null)
        {
            return;
        }

        float h2sRate = Mathf.Clamp01(h2sDiffuseStrength);
        float h2Rate = Mathf.Clamp01(h2DiffuseStrength);
        int h2sPassCount = Mathf.Clamp(h2sDiffusePasses, 0, 4);
        int h2PassCount = Mathf.Clamp(h2DiffusePasses, 0, 4);

        int cellCount = h2s.Length;
        int maxPasses = Mathf.Max(h2sPassCount, h2PassCount);

        for (int pass = 0; pass < maxPasses; pass++)
        {
            bool doH2S = pass < h2sPassCount && h2sRate > 0f;
            bool doH2 = pass < h2PassCount && h2Rate > 0f;
            if (!doH2S && !doH2)
            {
                continue;
            }

            for (int cell = 0; cell < cellCount; cell++)
            {
                float h2sNeighborSum = 0f;
                float h2NeighborSum = 0f;
                int neighborCount = 0;

                int baseIndex = cell * NeighborCount;
                for (int n = 0; n < NeighborCount; n++)
                {
                    int neighbor = ventHeatNeighbors[baseIndex + n];
                    if (neighbor < 0 || neighbor >= cellCount)
                    {
                        continue;
                    }

                    if (doH2S)
                    {
                        h2sNeighborSum += h2s[neighbor];
                    }

                    if (doH2)
                    {
                        h2NeighborSum += h2[neighbor];
                    }

                    neighborCount++;
                }

                if (doH2S)
                {
                    float h2sAverage = neighborCount > 0 ? h2sNeighborSum / neighborCount : h2s[cell];
                    h2sMixTmp[cell] = Mathf.Max(0f, Mathf.Lerp(h2s[cell], h2sAverage, h2sRate));
                }

                if (doH2)
                {
                    float h2Average = neighborCount > 0 ? h2NeighborSum / neighborCount : h2[cell];
                    h2MixTmp[cell] = Mathf.Max(0f, Mathf.Lerp(h2[cell], h2Average, h2Rate));
                }
            }

            if (doH2S)
            {
                float[] h2sSwap = h2s;
                h2s = h2sMixTmp;
                h2sMixTmp = h2sSwap;
            }

            if (doH2)
            {
                float[] h2Swap = h2;
                h2 = h2MixTmp;
                h2MixTmp = h2Swap;
            }
        }

        ApplyVentChemicalDecay(dt);
    }

    private void ApplyVentChemicalDecay(float dt)
    {
        float decayRate = Mathf.Max(0f, ventResourceDecayPerSecond);
        if (decayRate <= 0f || h2s == null || h2 == null)
        {
            return;
        }

        float decayFactor = Mathf.Exp(-decayRate * Mathf.Max(0f, dt));
        for (int cell = 0; cell < h2s.Length; cell++)
        {
            h2s[cell] = Mathf.Max(0f, h2s[cell] * decayFactor);
            h2[cell] = Mathf.Max(0f, h2[cell] * decayFactor);
        }
    }

    public void GetVentChemistryStats(out float meanH2, out float maxH2, out float meanH2S, out float maxH2S)
    {
        meanH2 = 0f;
        maxH2 = 0f;
        meanH2S = 0f;
        maxH2S = 0f;

        if (!isInitialized || h2 == null || h2s == null || h2.Length == 0)
        {
            return;
        }

        float sumH2 = 0f;
        float sumH2S = 0f;
        for (int i = 0; i < h2.Length; i++)
        {
            float h2Value = h2[i];
            float h2sValue = h2s[i];
            sumH2 += h2Value;
            sumH2S += h2sValue;
            maxH2 = Mathf.Max(maxH2, h2Value);
            maxH2S = Mathf.Max(maxH2S, h2sValue);
        }

        float invCount = 1f / h2.Length;
        meanH2 = sumH2 * invCount;
        meanH2S = sumH2S * invCount;
    }

    public void GetVentPlumeDiagnostics(out float avgVentH2S, out float avgVentH2, out float avgOceanH2, out float avgOceanH2S)
    {
        avgVentH2S = 0f;
        avgVentH2 = 0f;
        avgOceanH2 = 0f;
        avgOceanH2S = 0f;

        if (!isInitialized || h2 == null || h2s == null || ventMask == null || oceanMask == null)
        {
            return;
        }

        float ventH2SSum = 0f;
        float ventH2Sum = 0f;
        float oceanH2Sum = 0f;
        float oceanH2SSum = 0f;
        int ventCount = 0;
        int oceanCount = 0;

        for (int cell = 0; cell < h2.Length; cell++)
        {
            if (ventMask[cell] != 0)
            {
                ventH2SSum += h2s[cell];
                ventH2Sum += h2[cell];
                ventCount++;
            }

            if (oceanMask[cell] != 0)
            {
                oceanH2Sum += h2[cell];
                oceanH2SSum += h2s[cell];
                oceanCount++;
            }
        }

        if (ventCount > 0)
        {
            float invVent = 1f / ventCount;
            avgVentH2S = ventH2SSum * invVent;
            avgVentH2 = ventH2Sum * invVent;
        }

        if (oceanCount > 0)
        {
            float invOcean = 1f / oceanCount;
            avgOceanH2 = oceanH2Sum * invOcean;
            avgOceanH2S = oceanH2SSum * invOcean;
        }
    }

    private void UpdateAtmosphereDebugMeans()
    {
        if (co2 == null || o2 == null || co2.Length == 0)
        {
            debugGlobalCO2 = 0f;
            debugGlobalO2 = 0f;
            return;
        }

        float totalCO2 = 0f;
        float totalAtmosphereO2 = 0f;
        int atmosphereCellCount = 0;
        for (int cell = 0; cell < co2.Length; cell++)
        {
            totalCO2 += co2[cell];
            if (oceanMask != null && oceanMask[cell] == 0)
            {
                totalAtmosphereO2 += o2[cell];
                atmosphereCellCount++;
            }
        }

        float invCellCount = 1f / co2.Length;
        debugGlobalCO2 = totalCO2 * invCellCount;
        debugGlobalO2 = atmosphereCellCount > 0 ? totalAtmosphereO2 / atmosphereCellCount : 0f;
    }

    private void ApplyVentReplenishment()
    {
        if (!enableVentReplenishment || !isInitialized || ventCells == null || ventCells.Length == 0)
        {
            return;
        }

        float h2sPerTick = Mathf.Max(0f, ventH2SPerTick);
        float h2PerTick = Mathf.Max(0f, ventH2PerTick);
        float dissolvedFe2PlusPerTick = Mathf.Max(0f, ventDissolvedFe2PlusPerTick);
        if (Mathf.Approximately(h2sPerTick, 0f) && Mathf.Approximately(h2PerTick, 0f) && Mathf.Approximately(dissolvedFe2PlusPerTick, 0f))
        {
            return;
        }

        bool applyH2SCap = ventH2SMax > 0f;
        bool applyH2Cap = ventH2Max > 0f;
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

            float cellVentStrength = ventStrength != null && cell < ventStrength.Length ? ventStrength[cell] : 0f;
            if (cellVentStrength <= 0f)
            {
                continue;
            }

            Add(ResourceType.H2S, cell, h2sPerTick * cellVentStrength);
            Add(ResourceType.H2, cell, h2PerTick * cellVentStrength);
            Add(ResourceType.CO2, cell, Mathf.Max(0f, ventCO2PerTick) * cellVentStrength);
            Add(ResourceType.DissolvedFe2Plus, cell, dissolvedFe2PlusPerTick * cellVentStrength);
            if (applyH2SCap)
            {
                h2s[cell] = Mathf.Min(h2s[cell], ventH2SMax);
            }

            if (applyH2Cap)
            {
                h2[cell] = Mathf.Min(h2[cell], ventH2Max);
            }
        }

        UpdateOceanChemistryDebugStats();
    }

    private void ApplyDissolvedFe2PlusOxidation(float dt)
    {
        float[] dissolvedFe2Plus = GetOceanDissolvedArray(ResourceType.DissolvedFe2Plus);
        if (!isInitialized || dissolvedFe2Plus == null || o2 == null || oxidizedFeSediment == null || oceanMask == null)
        {
            return;
        }

        // Simplified redox sink:
        // Fe2+(dissolved, ocean) + O2 -> oxidized Fe sediment (locked).
        // Uses the existing local O2 field; oxidized sediment is intentionally non-recycled.
        float rate = Mathf.Max(0f, fe2PlusOxidationRatePerSecond);
        float o2PerFe2 = Mathf.Max(0f, o2ConsumptionPerFe2PlusOxidized);
        if (rate <= 0f || o2PerFe2 <= 0f)
        {
            return;
        }

        for (int cell = 0; cell < dissolvedFe2Plus.Length; cell++)
        {
            if (oceanMask[cell] == 0)
            {
                continue;
            }

            float availableFe2 = dissolvedFe2Plus[cell];
            float availableO2 = o2[cell];
            if (availableFe2 <= 0f || availableO2 <= 0f)
            {
                continue;
            }

            float desiredOxidation = availableFe2 * rate * Mathf.Max(0f, dt);
            float maxByO2 = availableO2 / o2PerFe2;
            float oxidizedFe2 = Mathf.Min(availableFe2, desiredOxidation, maxByO2);
            if (oxidizedFe2 <= 0f)
            {
                continue;
            }

            dissolvedFe2Plus[cell] = availableFe2 - oxidizedFe2;
            o2[cell] = Mathf.Max(0f, availableO2 - (oxidizedFe2 * o2PerFe2));
            oxidizedFeSediment[cell] += oxidizedFe2;
        }
    }

    private void UpdateOceanChemistryDebugStats()
    {
        float[] dissolvedFe2Plus = GetOceanDissolvedArray(ResourceType.DissolvedFe2Plus);
        if (!isInitialized || oceanMask == null || dissolvedFe2Plus == null)
        {
            debugDissolvedFe2PlusOceanMean = 0f;
            debugDissolvedFe2PlusTotal = 0f;
            debugDissolvedFe2PlusRemainingFraction = 0f;
            debugOxidizedFeSedimentTotal = 0f;
            return;
        }

        float dissolvedTotal = 0f;
        float oxidizedTotal = 0f;
        int oceanCount = 0;
        for (int cell = 0; cell < oceanMask.Length; cell++)
        {
            if (oceanMask[cell] == 0)
            {
                continue;
            }

            dissolvedTotal += dissolvedFe2Plus[cell];
            oxidizedTotal += oxidizedFeSediment[cell];
            oceanCount++;
        }

        debugDissolvedFe2PlusTotal = dissolvedTotal;
        debugOxidizedFeSedimentTotal = oxidizedTotal;
        debugDissolvedFe2PlusOceanMean = oceanCount > 0 ? dissolvedTotal / oceanCount : 0f;

        if (initialDissolvedFe2PlusTotal <= 0f)
        {
            initialDissolvedFe2PlusTotal = Mathf.Max(0f, dissolvedTotal);
        }

        debugDissolvedFe2PlusRemainingFraction = initialDissolvedFe2PlusTotal > 0f
            ? Mathf.Clamp01(dissolvedTotal / initialDissolvedFe2PlusTotal)
            : 0f;
    }

    private void ApplyNaturalOxidation()
    {
        if (!enableNaturalOxidation || !isInitialized || organicC == null || o2 == null || co2 == null)
            return;

        float k = Mathf.Max(0f, naturalOxidationFractionPerTick); // think of as per-tick rate
        if (k <= 0f) return;

        // Smooth O2 dependence: half-saturation constant (tunable)
        float kHalf = Mathf.Max(1e-6f, naturalOxidationO2HalfSaturation); // e.g. 0.02

        for (int cell = 0; cell < organicC.Length; cell++)
        {
            float c = organicC[cell];
            if (c <= 0f) continue;

            float localO2 = o2[cell];
            if (localO2 <= 0f) continue; // only true zero disables oxidation

            // 0..1 factor, ~linear at low O2, saturates at high O2
            float o2Factor = localO2 / (localO2 + kHalf);

            float desired = c * k * o2Factor;
            float oxidized = Mathf.Min(desired, c, localO2); // 1:1 O2:C in your simplified bookkeeping

            if (oxidized <= 0f) continue;

            organicC[cell] = c - oxidized;
            o2[cell] = localO2 - oxidized;
            co2[cell] += oxidized;
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
        Vector3 samplePoint = dir * (ventFrequency * Mathf.Max(0.0001f, ventNoiseScale)) + new Vector3(17.3f, -9.1f, 5.7f);
        return (SimpleNoise.Evaluate(samplePoint) + 1f) * 0.5f;
    }


    private void BuildVentHeatNeighbors()
    {
        if (cellDirections == null || ventHeatNeighbors == null || resolution <= 0)
        {
            return;
        }

        int cellCount = cellDirections.Length;
        float angularStep = Mathf.PI / Mathf.Max(8f, resolution * 4f);
        float tangentOffset = Mathf.Sin(angularStep);

        for (int cell = 0; cell < cellCount; cell++)
        {
            Vector3 dir = cellDirections[cell];
            Vector3 tangentA = Vector3.Cross(dir, Vector3.up);
            if (tangentA.sqrMagnitude < 1e-6f)
            {
                tangentA = Vector3.Cross(dir, Vector3.right);
            }

            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(dir, tangentA).normalized;

            Vector3[] neighborDirs =
            {
                (dir + tangentA * tangentOffset).normalized,
                (dir - tangentA * tangentOffset).normalized,
                (dir + tangentB * tangentOffset).normalized,
                (dir - tangentB * tangentOffset).normalized,
                (dir + (tangentA + tangentB).normalized * tangentOffset).normalized,
                (dir + (tangentA - tangentB).normalized * tangentOffset).normalized
            };

            for (int n = 0; n < NeighborCount; n++)
            {
                int neighborIndex = PlanetGridIndexing.DirectionToCellIndex(neighborDirs[n], resolution);
                ventHeatNeighbors[(cell * NeighborCount) + n] = neighborIndex;
            }
        }
    }

    private void RebuildVentHeatField()
    {
        if (ventStrength == null || ventHeat == null || ventHeatTmp == null)
        {
            return;
        }

        int cellCount = ventStrength.Length;
        float safeVentTempGain = Mathf.Max(0f, ventTempGain);

        for (int cell = 0; cell < cellCount; cell++)
        {
            float strength = Mathf.Max(0f, ventStrength[cell]);
            ventHeat[cell] = strength * safeVentTempGain;
            ventHeatTmp[cell] = ventHeat[cell];
        }

        if (enableVentHeatGradient && ventHeatNeighbors != null)
        {
            int passes = Mathf.Max(0, ventHeatBlurPasses);
            float spread = Mathf.Clamp01(ventHeatSpread);

            for (int pass = 0; pass < passes; pass++)
            {
                for (int cell = 0; cell < cellCount; cell++)
                {
                    if (!IsOceanCell(cell))
                    {
                        ventHeatTmp[cell] = ventHeat[cell];
                        continue;
                    }

                    float neighborSum = 0f;
                    int neighborCount = 0;
                    int baseIndex = cell * NeighborCount;
                    for (int n = 0; n < NeighborCount; n++)
                    {
                        int neighborCell = ventHeatNeighbors[baseIndex + n];
                        if (neighborCell < 0 || neighborCell >= cellCount || !IsOceanCell(neighborCell))
                        {
                            continue;
                        }

                        neighborSum += ventHeat[neighborCell];
                        neighborCount++;
                    }

                    float neighborAverage = neighborCount > 0 ? neighborSum / neighborCount : ventHeat[cell];
                    ventHeatTmp[cell] = Mathf.Lerp(ventHeat[cell], neighborAverage, spread);
                }

                float[] swap = ventHeat;
                ventHeat = ventHeatTmp;
                ventHeatTmp = swap;
            }
        }

        float oceanTempSum = 0f;
        float landTempSum = 0f;
        float oceanTempMax = 0f;
        float landTempMax = 0f;
        int oceanCount = 0;
        int landCount = 0;

        for (int cell = 0; cell < cellCount; cell++)
        {
            float baseValue = Mathf.Max(0f, baseTempKelvin);
            float temp = baseValue + ventHeat[cell];
            if (IsOceanCell(cell))
            {
                oceanTempSum += temp;
                oceanTempMax = Mathf.Max(oceanTempMax, temp);
                oceanCount++;
            }
            else
            {
                landTempSum += temp;
                landTempMax = Mathf.Max(landTempMax, temp);
                landCount++;
            }
        }

        float oceanAvg = oceanCount > 0 ? oceanTempSum / oceanCount : 0f;
        float landAvg = landCount > 0 ? landTempSum / landCount : 0f;
        float oceanAvgC = oceanAvg - 273.15f;
        float oceanMaxC = oceanTempMax - 273.15f;
        float landAvgC = landAvg - 273.15f;
        float landMaxC = landTempMax - 273.15f;
        Debug.Log($"Vent heat field: oceanAvg={oceanAvgC:0.0} °C ({oceanAvg:0.0} K) oceanMax={oceanMaxC:0.0} °C ({oceanTempMax:0.0} K) landAvg={landAvgC:0.0} °C ({landAvg:0.0} K) landMax={landMaxC:0.0} °C ({landTempMax:0.0} K)", this);
    }

    private void ResolveSunReferences()
    {
        if (sunSkyRotator == null)
        {
            sunSkyRotator = FindFirstObjectByType<SunSkyRotator>();
        }

        if (sunLight == null && sunSkyRotator != null)
        {
            sunLight = sunSkyRotator.GetComponent<Light>();
        }

        if (sunLight == null)
        {
            sunLight = RenderSettings.sun;
        }
    }

    private static void EnsureArrayCapacity(ref float[] array, int cellCount)
    {
        if (array == null || array.Length != cellCount)
        {
            array = new float[cellCount];
        }
    }

    private Vector3 GetSunDirection()
    {
        ResolveSunReferences();

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
        float[] dissolvedArray = GetOceanDissolvedArray(type);
        if (dissolvedArray != null)
        {
            return dissolvedArray;
        }

        switch (type)
        {
            case ResourceType.CO2: return co2;
            case ResourceType.O2: return o2;
            case ResourceType.OrganicC: return organicC;
            case ResourceType.H2S: return h2s;
            case ResourceType.H2: return h2;
            case ResourceType.S0: return s0;
            case ResourceType.P: return p;
            case ResourceType.Fe: return fe;
            case ResourceType.OxidizedFeSediment: return oxidizedFeSediment;
            case ResourceType.Si: return si;
            case ResourceType.Ca: return ca;
            case ResourceType.DissolvedOrganicLeak: return dissolvedOrganicLeak;
            case ResourceType.ToxicProteolyticWaste: return toxicProteolyticWaste;
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
        ventCO2PerTick = Mathf.Max(0f, ventCO2PerTick);
        ventH2PerTick = Mathf.Max(0f, ventH2PerTick);
        ventH2Max = Mathf.Max(0f, ventH2Max);
        h2sDiffuseStrength = Mathf.Clamp01(h2sDiffuseStrength);
        h2sDiffusePasses = Mathf.Clamp(h2sDiffusePasses, 0, 4);
        h2DiffuseStrength = Mathf.Clamp01(h2DiffuseStrength);
        h2DiffusePasses = Mathf.Clamp(h2DiffusePasses, 0, 4);
        ventResourceDecayPerSecond = Mathf.Max(0f, ventResourceDecayPerSecond);
        atmosphereTickSeconds = Mathf.Max(0.0001f, atmosphereTickSeconds);
        landExchangeRate = Mathf.Max(0f, landExchangeRate);
        oceanExchangeRate = Mathf.Max(0f, oceanExchangeRate);
        atmosphereToOceanO2TransferFractionPerTick = Mathf.Clamp01(atmosphereToOceanO2TransferFractionPerTick);
        naturalOxidationFractionPerTick = Mathf.Clamp01(naturalOxidationFractionPerTick);
        initialDissolvedFe2PlusPerOceanCell = Mathf.Max(0f, initialDissolvedFe2PlusPerOceanCell);
        ventDissolvedFe2PlusPerTick = Mathf.Max(0f, ventDissolvedFe2PlusPerTick);
        fe2PlusOxidationRatePerSecond = Mathf.Max(0f, fe2PlusOxidationRatePerSecond);
        o2ConsumptionPerFe2PlusOxidized = Mathf.Max(0f, o2ConsumptionPerFe2PlusOxidized);
        dissolvedOrganicLeakDecayPerSecond = Mathf.Max(0f, dissolvedOrganicLeakDecayPerSecond);
        toxicProteolyticWasteDecayPerSecond = Mathf.Max(0f, toxicProteolyticWasteDecayPerSecond);
        scentDiffusePasses = Mathf.Clamp(scentDiffusePasses, 0, 4);
        scentDiffuseStrength = Mathf.Clamp01(scentDiffuseStrength);
        scentMaxPerCell = Mathf.Max(0f, scentMaxPerCell);
        scentUpdateInterval = Mathf.Max(0.01f, scentUpdateInterval);
    }


    private string BuildDebugLabelText(int cellIndex)
    {
        return $"{debugViewType}: {Get(debugViewType, cellIndex):0.###}\nCO2: {Get(ResourceType.CO2, cellIndex):0.###}\nH2: {Get(ResourceType.H2, cellIndex):0.###}\nH2S: {Get(ResourceType.H2S, cellIndex):0.###}\nS0: {Get(ResourceType.S0, cellIndex):0.###}";
    }


    private float GetAbsoluteDebugMax(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.CO2: return Mathf.Max(0.0001f, baselineCO2);
            case ResourceType.O2: return Mathf.Max(0.0001f, baselineO2);
            case ResourceType.OrganicC: return 1f;
            case ResourceType.H2S: return Mathf.Max(0.0001f, ventH2SMax > 0f ? ventH2SMax : ventStrengthMax);
            case ResourceType.H2: return Mathf.Max(0.0001f, ventH2Max > 0f ? ventH2Max : ventStrengthMax);
            case ResourceType.S0: return Mathf.Max(0.0001f, baselineS0);
            case ResourceType.P: return Mathf.Max(0.0001f, phosphorusScale);
            case ResourceType.Fe: return Mathf.Max(0.0001f, ironScale);
            case ResourceType.DissolvedFe2Plus: return Mathf.Max(0.0001f, initialDissolvedFe2PlusPerOceanCell);
            case ResourceType.OxidizedFeSediment: return Mathf.Max(0.0001f, initialDissolvedFe2PlusPerOceanCell);
            case ResourceType.Si: return Mathf.Max(0.0001f, baselineSi + siliconPatchScale);
            case ResourceType.Ca: return Mathf.Max(0.0001f, baselineCa + calciumPatchScale);
            case ResourceType.DissolvedOrganicLeak: return Mathf.Max(0.0001f, scentMaxPerCell);
            case ResourceType.ToxicProteolyticWaste: return Mathf.Max(0.0001f, scentMaxPerCell);
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
