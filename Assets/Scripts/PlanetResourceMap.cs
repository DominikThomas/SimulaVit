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
    CH4 = 5,
    S0 = 6,
    P = 7,
    Fe = 8,
    Si = 9,
    Ca = 10,
    DissolvedOrganicLeak = 11,
    ToxicProteolyticWaste = 12,
    DissolvedFe2Plus = 13
}

[DisallowMultipleComponent]
public class PlanetResourceMap : MonoBehaviour
{
    // Lightweight callsite tagging used by layered-ocean aggregate compatibility telemetry.
    // Keep this broad (subsystem-level) so counters stay stable and low-overhead.
    public enum AggregateCompatibilityCallsite
    {
        UnknownLegacy = 0,
        Metabolism = 1,
        SpawningLifecycle = 2,
        ResourcePhysics = 3,
        AtmosphereVents = 4,
        DebugTelemetry = 5,
    }

    private const int AggregateCompatibilityCallsiteCount = 6;

    public enum LayeredWriteFallbackReason
    {
        Unknown = 0,
        LandOrNonOcean = 1,
        InvalidCell = 2,
        NoActiveOceanLayers = 3,
        InvalidCurrentOrPreferredLayer = 4,
        MissingPopulationStateSync = 5,
        ResourceNotLayeredInOcean = 6,
        ResourceNotLayeredNonOcean = 7,
    }

    private const int LayeredWriteFallbackReasonCount = 8;
    private const int MetabolismTypeTelemetryCount = 9;

    private const int MaxOceanLayers = 5;

    public struct OceanLayerSnapshot
    {
        public int LayerIndex;
        public float O2;
        public float DissolvedFe2Plus;
        public float CO2;
        public float CH4;
        public float OrganicC;
        public float H2;
        public float H2S;
        public float LightFactor;
        public float TemperatureOffset;
        public float TemperatureKelvinEstimate;
    }

    public struct CellInspectionSnapshot
    {
        public bool IsValid;
        public int CellIndex;
        public bool IsOcean;
        public int ActiveLayerCount;
        public float Insolation;
        public float VentStrength;
        public float EffectiveTemperatureKelvin;
        public LegacyEnvironmentSnapshot EffectiveLegacy;
        public float EffectiveCO2;
        public float EffectiveO2;
        public float EffectiveCH4;
        public float EffectiveOrganicC;
        public float EffectiveH2;
        public float EffectiveH2S;
        public float EffectiveDissolvedFe2Plus;
        public OceanLayerSnapshot[] OceanLayers;
    }

    public struct LegacyEnvironmentSnapshot
    {
        public float O2;
        public float OrganicC;
        public float H2;
        public float H2S;
        public float CH4;
        public float DissolvedFe2Plus;
        public float TemperatureKelvin;
        public float LightFactor;
    }

    [SerializeField] private PlanetGenerator planetGenerator;
    [SerializeField] private ReplicatorManager replicatorManager;
    [Header("Grid Resolution")]
    [Tooltip("Simulation/resource grid resolution. 0 keeps backward-compatible behavior and follows PlanetGenerator visual resolution.")]
    [Min(0)] [SerializeField] private int simulationResolution = 0;
    [Header("References")]
    [Tooltip("Optional directional light. If empty, will try SunSkyRotator's Light, then RenderSettings.sun.")]
    public Light sunLight;
    public SunSkyRotator sunSkyRotator;

    [Header("Resource Baselines")]
    [Tooltip("Reference resolution used to convert legacy per-cell baseline fields into resolution-independent total inventories (cell count = 6 * resolution^2).")]
    [Min(1)] public int inventoryReferenceResolution = 100;
    [Tooltip("Reference planet radius used for baseline inventory calibration. Initial totals scale with surface area (radius^2).")]
    [Min(0.0001f)] public float inventoryReferencePlanetRadius = 1f;
    public float baselineCO2 = 1.0f;
    public float baselineO2 = 0.01f;
    public float baselineCH4 = 0f;
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
    [Tooltip("Reference resolution used for vent-abundance normalization (vents scale with planet surface area, not simulation tile count).")]
    [Min(1)] public int ventReferenceResolution = 100;
    [Tooltip("Reference planet radius used for vent-abundance normalization.")]
    [Min(0.0001f)] public float ventReferencePlanetRadius = 1f;

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
    public float debugGlobalCH4;

    [Header("Ocean Dissolved Chemistry")]
    [Tooltip("Legacy per-cell baseline at reference resolution/radius; converted into a resolution-independent total dissolved Fe2+ ocean inventory.")]
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
    public float debugFeTotal;

    [Header("Ocean Visuals")]
    public bool updateOceanColorFromDissolvedFe2Plus = true;
    public Color ironRichOceanColor = new Color(0.22f, 0.55f, 0.38f, 1f);
    public Color oxygenatedOceanColor = new Color(0.18f, 0.40f, 0.75f, 1f);
    [Range(0.01f, 10f)] public float oceanColorLerpSpeed = 2f;

    private Material oceanMaterialInstance;
    private Color currentOceanColor;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

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

    [Header("Layered Ocean Foundation")]
    [Tooltip("Enables per-cell vertical ocean layers while keeping legacy resource access behavior.")]
    public bool enableLayeredOcean = true;
    [Tooltip("Fraction of local max ocean depth needed to activate a second layer.")]
    [Range(0f, 1f)] public float depth01ForLayer2 = 0.12f;
    [Tooltip("Fraction of local max ocean depth needed to activate a third layer.")]
    [Range(0f, 1f)] public float depth01ForLayer3 = 0.32f;
    [Tooltip("Fraction of local max ocean depth needed to activate a fourth layer.")]
    [Range(0f, 1f)] public float depth01ForLayer4 = 0.58f;
    [Tooltip("Fraction of local max ocean depth needed to activate all five layers.")]
    [Range(0f, 1f)] public float depth01ForLayer5 = 0.82f;
    [Tooltip("Legacy light attenuation knob kept for compatibility. Discrete top/second/deep light factors are now preferred.")]
    [Range(0f, 8f)] public float layeredLightAttenuation = 1.15f;
    [Tooltip("Layered light model: layer 0 (surface) uses strong light = 1.0.")]
    [Range(0f, 1f)] public float layeredTopLightFactor = 1f;
    [Tooltip("Layered light model: layer 1 uses moderate light relative to top (target ~0.40-0.70).")]
    [Range(0.4f, 0.7f)] public float layeredSecondLayerLightFactor = 0.55f;
    [Tooltip("Layered light model: layers 2+ are near-dark by default.")]
    [Range(0f, 0.2f)] public float layeredDeepLightFactor = 0.02f;
    [Tooltip("Simple conservative adjacent-layer diffusion rate applied each atmosphere tick.")]
    [Range(0f, 1f)] public float layeredVerticalMixRate = 0.02f;
    [Tooltip("Slow oxygenation source that pushes atmospheric O2 into ocean surface layers.")]
    [Range(0f, 1f)] public float layeredSurfaceOxygenationRate = 0.02f;
    [Tooltip("Downward sinking flux of organic material (marine snow) per atmosphere tick.")]
    [Range(0f, 1f)] public float layeredMarineSnowRate = 0.015f;
    [Tooltip("Weak in-layer lateral spread for OrganicC (marine snow plume broadening).")]
    [Range(0f, 1f)] public float layeredOrganicCLateralSpreadRate = 0.002f;
    [Tooltip("Optional tiny upward bleed for OrganicC between adjacent layers (kept near zero by default).")]
    [Range(0f, 1f)] public float layeredOrganicCUpwardBleedRate = 0.0002f;
    [Tooltip("Simple temperature decrease per layer away from the surface (Kelvin).")]
    [Min(0f)] public float layeredTempDropPerLayer = 2f;
    [Tooltip("Vent heating contribution per layer toward bottom (Kelvin at full vent strength).")]
    [Min(0f)] public float layeredBottomVentTempGain = 25f;
    [Tooltip("Layered solar heating model: relative heating at layer 0 (surface).")]
    [Range(0f, 1f)] public float layeredTopSolarHeatingFactor = 1f;
    [Tooltip("Layered solar heating model: relative heating at layer 1 (use similar scaling to light).")]
    [Range(0f, 1f)] public float layeredSecondLayerSolarHeatingFactor = 0.55f;
    [Tooltip("Layered solar heating model: direct solar heating for layers 2+ (keep near zero).")]
    [Range(0f, 0.2f)] public float layeredDeepSolarHeatingFactor = 0f;
    [Tooltip("Layered vent heating model: strongest direct vent heating on the bottom-most ocean layer.")]
    [Range(0f, 1f)] public float layeredBottomVentHeatingFactor = 1f;
    [Tooltip("Layered vent heating model: weaker direct vent heating one layer above the bottom.")]
    [Range(0f, 1f)] public float layeredAboveBottomVentHeatingFactor = 0.45f;

    [Header("Layered Ocean Debug")]
    [Tooltip("Inspector sample cell for quick per-cell layered debug values.")]
    public int debugLayeredCellIndex = 0;
    [Range(1, MaxOceanLayers)] public int debugLayeredSampleLayer = 1;
    public int debugLayeredActiveCount;
    public float debugLayeredTopLight;
    public float debugLayeredBottomLight;
    public float debugLayeredEffectiveO2;
    public float debugLayeredEffectiveOrganicC;
    [Tooltip("Ocean-wide mean O2 in top active layer (layer 0) across ocean cells.")]
    public float debugLayeredTopO2Mean;
    [Tooltip("Ocean-wide mean O2 in bottom active layer across ocean cells.")]
    public float debugLayeredBottomO2Mean;
    [Tooltip("Ocean-wide mean OrganicC in top active layer (layer 0) across ocean cells.")]
    public float debugLayeredTopOrganicCMean;
    [Tooltip("Ocean-wide mean OrganicC in bottom active layer across ocean cells.")]
    public float debugLayeredBottomOrganicCMean;
    [Tooltip("Ocean-wide mean H2S in top active layer (layer 0) across ocean cells.")]
    public float debugLayeredTopH2SMean;
    [Tooltip("Ocean-wide mean H2S in bottom active layer across ocean cells.")]
    public float debugLayeredBottomH2SMean;
    [Tooltip("Ocean-wide mean S0 in top active layer (layer 0) across ocean cells.")]
    public float debugLayeredTopS0Mean;
    [Tooltip("Ocean-wide mean S0 in bottom active layer across ocean cells.")]
    public float debugLayeredBottomS0Mean;
    [Tooltip("How often aggregate Add(...) compatibility distribution was applied to layered ocean resources.")]
    public int debugLayeredAggregateAddCompatibilityCount;
    [Tooltip("Total absolute delta routed through aggregate Add(...) compatibility distribution for layered resources.")]
    public float debugLayeredAggregateAddCompatibilityAbsDelta;
    [Tooltip("How often AddResourceForCellLayer(...) had to fall back to aggregate Add(...) due to invalid or unavailable layer context.")]
    public int debugLayeredWriteFallbackToAggregateCount;
    [Tooltip("How often GetResourceForCellLayer(...) had to fall back to effective aggregate Get(...) due to invalid or unavailable layer context.")]
    public int debugLayeredReadFallbackToAggregateCount;
    [Tooltip("Optional: enable per-callsite counters for layered aggregate compatibility usage. Keep disabled in normal gameplay unless auditing migration progress.")]
    public bool enableLayeredCompatibilityCallsiteTelemetry;
    [Tooltip("Compatibility Add(...) calls in layered ocean cells routed through aggregate-to-layer distribution, grouped by callsite.")]
    public int[] debugLayeredAggregateAddCompatibilityCountByCallsite = new int[AggregateCompatibilityCallsiteCount];
    [Tooltip("Compatibility Add(...) absolute delta routed through aggregate-to-layer distribution, grouped by callsite.")]
    public float[] debugLayeredAggregateAddCompatibilityAbsDeltaByCallsite = new float[AggregateCompatibilityCallsiteCount];
    [Tooltip("Aggregate Get(...) calls for layered resources (effective layered aggregate reads), grouped by callsite.")]
    public int[] debugLayeredAggregateGetCompatibilityCountByCallsite = new int[AggregateCompatibilityCallsiteCount];
    [Tooltip("AddResourceForCellLayer(...) fallback-to-aggregate writes, grouped by callsite.")]
    public int[] debugLayeredWriteFallbackToAggregateCountByCallsite = new int[AggregateCompatibilityCallsiteCount];
    [Tooltip("Layered write fallback reasons aggregated across callsites.")]
    public int[] debugLayeredWriteFallbackReasonCount = new int[LayeredWriteFallbackReasonCount];
    [Tooltip("Metabolism-only layered write fallback counts by resource index.")]
    public int[] debugMetabolismWriteFallbackCountByResource = new int[14];
    [Tooltip("Metabolism-only layered write fallback counts by metabolism type enum value.")]
    public int[] debugMetabolismWriteFallbackCountByMetabolismType = new int[MetabolismTypeTelemetryCount];
    [Tooltip("Metabolism-only layered write fallback counts by reason.")]
    public int[] debugMetabolismWriteFallbackCountByReason = new int[LayeredWriteFallbackReasonCount];
    [Tooltip("Metabolism-only counts for ResourceNotLayeredInOcean fallbacks by resource index.")]
    public int[] debugMetabolismResourceNotLayeredInOceanCountByResource = new int[14];
    [Tooltip("Metabolism-only counts for ResourceNotLayeredInOcean fallbacks by metabolism type enum value.")]
    public int[] debugMetabolismResourceNotLayeredInOceanCountByMetabolismType = new int[MetabolismTypeTelemetryCount];
    [Tooltip("GetResourceForCellLayer(...) fallback-to-aggregate reads, grouped by callsite.")]
    public int[] debugLayeredReadFallbackToAggregateCountByCallsite = new int[AggregateCompatibilityCallsiteCount];

    [Header("Scent Fields")]
    [Tooltip("Enable diffuse chemical cue fields used for scent-based predation/fear steering.")]
    public bool enableScentFields = true;
    [Tooltip("Decay rate for prey-emitted dissolved organic leak field.")]
    public float dissolvedOrganicLeakDecayPerSecond = 0.6f;
    [Tooltip("Decay rate for predator-emitted toxic proteolytic waste field.")]
    public float toxicProteolyticWasteDecayPerSecond = 0.8f;
    [Range(0f, 1f)] public float scentDiffuseStrength = 0.25f;
    [Range(0, 4)] public int scentDiffusePasses = 1;
    [Tooltip("Weak vertical coupling between adjacent ocean scent layers (0 = none, 1 = full).")]
    [Range(0f, 1f)] public float scentAdjacentLayerCoupling = 0.15f;
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

    // Simulation/resource resolution used for resource arrays, indexing, chemistry, and lookup APIs.
    // Visual/mesh resolution remains owned by PlanetGenerator.resolution.
    private int resolution;
    private Vector3[] cellDirections;

    private float[] co2;
    private float[] o2;
    private float[] organicC;
    private float[] h2s;
    private float[] h2;
    private float[] ch4;
    private float[] s0;
    private float[] p;
    private float[] fe;
    private float[] si;
    private float[] ca;
    private readonly Dictionary<ResourceType, float[]> oceanDissolvedResources = new Dictionary<ResourceType, float[]>();
    private readonly Dictionary<ResourceType, float[]> layeredOceanResources = new Dictionary<ResourceType, float[]>();
    private float initialDissolvedFe2PlusTotal;

    private bool isInitialized;
    public float[] ventStrength;
    public float[] toxicProteolyticWaste;
    public float[] dissolvedOrganicLeak;
    private float[] ventHeat;
    private float[] ventHeatTmp;
    private float[] h2sMixTmp;
    private float[] h2MixTmp;
    private float[] surfaceO2DemandTmp;
    private float[] scentWasteTmp;
    private float[] scentLeakTmp;
    private float[] layeredScentWasteTmp;
    private float[] layeredScentLeakTmp;
    private float[] layeredOrganicCTmp;
    private int[] ventHeatNeighbors;
    private byte[] ventMask;
    private int[] ventCells;
    private byte[] oceanMask;
    private byte[] oceanActiveLayerCounts;
    private float[] oceanLayerLightFactors;
    private float[] oceanLayerTemperatureOffsets;
    private float ventTimer;
    private float atmosphereTimer;
    private float ventSourceStrengthNormalization = 1f;

    private const int NeighborCount = 6;


    public int VentCount => ventCells != null ? ventCells.Length : 0;
    public int[] VentCells => ventCells;
    public Vector3[] CellDirs => cellDirections;
    public int SimulationResolution => Mathf.Max(1, simulationResolution > 0
        ? simulationResolution
        : (planetGenerator != null ? planetGenerator.resolution : resolution));
    public int SimulationCellCount => PlanetGridIndexing.GetCellCount(SimulationResolution);

    private void Awake()
    {
        if (planetGenerator == null)
        {
            planetGenerator = GetComponent<PlanetGenerator>();
        }

        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }

        ResolveSunReferences();

        EnsureDebugGradient();
    }

    private void Start()
    {
        InitializeIfNeeded();
        InitializeOceanVisuals();
    }

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }

        if (replicatorManager != null && !replicatorManager.ShouldAdvanceSimulation)
        {
            return;
        }

        float simulationDeltaTime = Time.deltaTime;
        if (replicatorManager != null)
        {
            simulationDeltaTime *= Mathf.Max(0f, replicatorManager.SimulationSpeedMultiplier);
        }

        if (enableVentReplenishment)
        {
            float ventTick = Mathf.Max(0.0001f, ventTickSeconds);
            ventTimer += simulationDeltaTime;

            while (ventTimer >= ventTick)
            {
                ventTimer -= ventTick;
                ApplyVentReplenishment();
            }
        }

        if (enableAtmosphereMixing)
        {
            float atmosphereTick = Mathf.Max(0.0001f, atmosphereTickSeconds);
            atmosphereTimer += simulationDeltaTime;

            while (atmosphereTimer >= atmosphereTick)
            {
                atmosphereTimer -= atmosphereTick;
                ApplyAtmosphereMixing();
                ApplyNaturalOxidation();
                // Surface Fe2+ can still draw atmospheric O2, but only from local top-layer demand.
                TransferAtmosphericO2ToSurfaceFe2Demand(atmosphereTick);
                ApplyDissolvedFe2PlusOxidation(atmosphereTick);
                ApplyLocalResourceMixing(atmosphereTick);
                ApplyVerticalLayeredOceanProcesses(atmosphereTick);
                UpdateAtmosphereDebugMeans();
                UpdateOceanChemistryDebugStats();
            }
        }

        UpdateOceanVisuals();
    }

    public float Get(ResourceType t, int cell, AggregateCompatibilityCallsite callsite = AggregateCompatibilityCallsite.UnknownLegacy)
    {
        if (!isInitialized || !IsCellValid(cell))
        {
            return 0f;
        }

        if (ShouldUseLayeredOceanForResource(t, cell))
        {
            RecordAggregateGetCompatibilityCallsite(callsite);
            return GetEffectiveLayeredResource(t, cell);
        }

        return GetArray(t)[cell];
    }

    public void Add(ResourceType t, int cell, float delta, AggregateCompatibilityCallsite callsite = AggregateCompatibilityCallsite.UnknownLegacy)
    {
        if (!isInitialized || !IsCellValid(cell) || Mathf.Approximately(delta, 0f))
        {
            return;
        }

        if (ShouldUseLayeredOceanForResource(t, cell))
        {
            debugLayeredAggregateAddCompatibilityCount++;
            debugLayeredAggregateAddCompatibilityAbsDelta += Mathf.Abs(delta);
            RecordAggregateAddCompatibilityCallsite(callsite, delta);
            AddLayeredResourceDelta(t, cell, delta, callsite);
            SyncLegacyOceanResourceFromLayers(t, cell);
            return;
        }

        if (IsOceanDissolvedResource(t) && !IsOceanCell(cell))
        {
            return;
        }

        float[] arr = GetArray(t);
        arr[cell] = Mathf.Max(0f, arr[cell] + delta);
    }

    public void AddResourceForCellLayer(ResourceType t, int cell, int requestedLayerIndex, float delta, AggregateCompatibilityCallsite callsite = AggregateCompatibilityCallsite.UnknownLegacy)
    {
        if (!isInitialized || !IsCellValid(cell) || Mathf.Approximately(delta, 0f))
        {
            return;
        }

        if (!ShouldUseLayeredOceanForResource(t, cell))
        {
            debugLayeredWriteFallbackToAggregateCount++;
            RecordWriteFallbackToAggregateCallsite(callsite);
            Add(t, cell, delta, callsite);
            return;
        }

        int clampedLayer = ClampOceanLayerIndex(cell, requestedLayerIndex);
        if (clampedLayer < 0)
        {
            debugLayeredWriteFallbackToAggregateCount++;
            RecordWriteFallbackToAggregateCallsite(callsite);
            Add(t, cell, delta, callsite);
            return;
        }

        float[] layered = GetLayeredOceanArray(t);
        if (layered == null)
        {
            debugLayeredWriteFallbackToAggregateCount++;
            RecordWriteFallbackToAggregateCallsite(callsite);
            Add(t, cell, delta, callsite);
            return;
        }

        int idx = GetLayeredArrayIndex(cell, clampedLayer);
        layered[idx] = Mathf.Max(0f, layered[idx] + delta);
        SyncLegacyOceanResourceFromLayers(t, cell);
    }

    public bool IsVolatile(ResourceType t)
    {
        return t == ResourceType.CO2 || t == ResourceType.O2 || t == ResourceType.CH4;
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

    public int GetOceanActiveLayerCount(int cell)
    {
        if (!isInitialized || !IsOceanCell(cell))
        {
            return 0;
        }

        if (!enableLayeredOcean || oceanActiveLayerCounts == null || cell >= oceanActiveLayerCounts.Length)
        {
            return 1;
        }

        int count = oceanActiveLayerCounts[cell];
        if (count <= 0)
        {
            return 0;
        }

        return Mathf.Clamp(count, 1, MaxOceanLayers);
    }

    public int GetValidActiveOceanLayerCount(int cell)
    {
        return GetOceanActiveLayerCount(cell);
    }

    public int ClampOceanLayerIndex(int cell, int requestedLayerIndex)
    {
        int activeCount = GetOceanActiveLayerCount(cell);
        if (activeCount <= 0)
        {
            return -1;
        }

        return Mathf.Clamp(requestedLayerIndex, 0, activeCount - 1);
    }


    public bool TryResolveLayeredOceanWriteLayer(int cell, int currentLayerIndex, int preferredLayerIndex, out int resolvedLayerIndex)
    {
        resolvedLayerIndex = -1;
        if (!isInitialized || !IsCellValid(cell) || !ShouldUseLayeredOceanForResource(ResourceType.OrganicC, cell))
        {
            return false;
        }

        if (currentLayerIndex >= 0)
        {
            int currentClamped = ClampOceanLayerIndex(cell, currentLayerIndex);
            if (currentClamped >= 0)
            {
                resolvedLayerIndex = currentClamped;
                return true;
            }
        }

        if (preferredLayerIndex >= 0)
        {
            int preferredClamped = ClampOceanLayerIndex(cell, preferredLayerIndex);
            if (preferredClamped >= 0)
            {
                resolvedLayerIndex = preferredClamped;
                return true;
            }
        }

        int deterministicFallback = GetOceanBottomLayerIndex(cell);
        if (deterministicFallback >= 0)
        {
            resolvedLayerIndex = deterministicFallback;
            return true;
        }

        return false;
    }

    public int GetOceanTopLayerIndex(int cell)
    {
        int count = GetOceanActiveLayerCount(cell);
        return count >= 1 ? 0 : -1;
    }

    public int GetOceanBottomLayerIndex(int cell)
    {
        int count = GetOceanActiveLayerCount(cell);
        return count > 0 ? count - 1 : -1;
    }

    public float GetOceanTopRadius(int cell)
    {
        if (!isInitialized || !IsCellValid(cell))
        {
            return planetGenerator != null ? planetGenerator.radius : 0f;
        }

        if (planetGenerator == null)
        {
            return 0f;
        }

        return planetGenerator.GetOceanTopRadius(GetDirectionForCell(cell));
    }

    public float GetOceanFloorRadius(int cell)
    {
        if (!isInitialized || !IsCellValid(cell))
        {
            return planetGenerator != null ? planetGenerator.radius : 0f;
        }

        if (planetGenerator == null)
        {
            return 0f;
        }

        return planetGenerator.GetOceanFloorRadius(GetDirectionForCell(cell));
    }

    public float GetOceanLayerShellRadius(int cell, int requestedLayerIndex)
    {
        if (!isInitialized || !IsCellValid(cell))
        {
            return 0f;
        }

        // Layer index semantics:
        // - 0 = top ocean layer (sea surface)
        // - (activeCount - 1) = deepest active ocean layer (seafloor-adjacent)
        // Intermediate layers are monotonically interpolated between top and bottom.
        int activeCount = GetOceanActiveLayerCount(cell);
        if (activeCount <= 0)
        {
            return planetGenerator != null ? planetGenerator.GetSurfaceRadius(GetDirectionForCell(cell)) : 0f;
        }

        int clampedLayerIndex = Mathf.Clamp(requestedLayerIndex, 0, activeCount - 1);
        float topRadius = GetOceanTopRadius(cell);
        float floorRadius = GetOceanFloorRadius(cell);
        float upper = Mathf.Max(topRadius, floorRadius);
        float lower = Mathf.Min(topRadius, floorRadius);

        if (activeCount == 1)
        {
            return lower;
        }

        float depthT = clampedLayerIndex / (float)(activeCount - 1);
        return Mathf.Lerp(upper, lower, depthT);
    }

    public float GetOceanLayerShellRadius(Vector3 worldPosOrDir, int requestedLayerIndex)
    {
        if (!isInitialized || resolution <= 0)
        {
            return 0f;
        }

        Vector3 dir = ResolveSurfaceDirection(worldPosOrDir);
        int cell = GetCellIndexFromDirection(dir);
        if (!IsCellValid(cell))
        {
            return 0f;
        }

        return GetOceanLayerShellRadius(cell, requestedLayerIndex);
    }

    public float GetLayerLightFactor(int cell, int layerIndex)
    {
        if (!IsLayerAccessValid(cell, layerIndex))
        {
            return 0f;
        }

        return oceanLayerLightFactors[GetLayeredArrayIndex(cell, layerIndex)];
    }

    public float GetLayerTemperatureOffset(int cell, int layerIndex)
    {
        if (!IsLayerAccessValid(cell, layerIndex))
        {
            return 0f;
        }

        return oceanLayerTemperatureOffsets[GetLayeredArrayIndex(cell, layerIndex)];
    }

    public float GetLayeredLightForCell(int cell, int requestedLayerIndex, float surfaceInsolation)
    {
        float clampedInsolation = Mathf.Clamp01(surfaceInsolation);
        if (!isInitialized || !IsCellValid(cell))
        {
            return clampedInsolation;
        }

        if (!enableLayeredOcean || !IsOceanCell(cell))
        {
            return clampedInsolation;
        }

        int clampedLayer = ClampOceanLayerIndex(cell, requestedLayerIndex);
        if (clampedLayer < 0)
        {
            return clampedInsolation;
        }

        return clampedInsolation * Mathf.Clamp01(GetLayerLightFactor(cell, clampedLayer));
    }

    public float GetLayerResource(ResourceType resourceType, int cell, int layerIndex)
    {
        if (!IsLayerAccessValid(cell, layerIndex))
        {
            return 0f;
        }

        float[] layeredArray = GetLayeredOceanArray(resourceType);
        if (layeredArray == null)
        {
            return Get(resourceType, cell);
        }

        return layeredArray[GetLayeredArrayIndex(cell, layerIndex)];
    }

    public float GetResourceForCellLayer(ResourceType resourceType, int cell, int requestedLayerIndex, AggregateCompatibilityCallsite callsite = AggregateCompatibilityCallsite.UnknownLegacy)
    {
        if (!isInitialized || !IsCellValid(cell))
        {
            return 0f;
        }

        if (!ShouldUseLayeredOceanForResource(resourceType, cell))
        {
            debugLayeredReadFallbackToAggregateCount++;
            RecordReadFallbackToAggregateCallsite(callsite);
            return Get(resourceType, cell, callsite);
        }

        int clampedLayer = ClampOceanLayerIndex(cell, requestedLayerIndex);
        if (clampedLayer < 0)
        {
            debugLayeredReadFallbackToAggregateCount++;
            RecordReadFallbackToAggregateCallsite(callsite);
            return Get(resourceType, cell, callsite);
        }

        return GetLayerResource(resourceType, cell, clampedLayer);
    }

    public LegacyEnvironmentSnapshot GetEffectiveLegacyEnvironment(int cell, Vector3 worldPosOrDir)
    {
        LegacyEnvironmentSnapshot snapshot = default;
        snapshot.O2 = Get(ResourceType.O2, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.OrganicC = Get(ResourceType.OrganicC, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.H2 = Get(ResourceType.H2, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.H2S = Get(ResourceType.H2S, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.CH4 = Get(ResourceType.CH4, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.DissolvedFe2Plus = Get(ResourceType.DissolvedFe2Plus, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.TemperatureKelvin = GetTemperature(worldPosOrDir, cell);
        snapshot.LightFactor = GetEffectiveLayeredLight(cell);
        return snapshot;
    }

    /// <summary>
    /// Read-only inspection snapshot for debug UI.
    /// Notes:
    /// - In layered ocean cells, CO2 now reports true per-layer values.
    /// - Legacy aggregate CO2 views are still bridged for compatibility using top-layer coupling.
    /// - Layer temperature is estimated from the current cell effective temperature plus that layer's offset.
    /// </summary>
    public bool TryGetCellInspectionSnapshot(int cell, Vector3 worldPosOrDir, out CellInspectionSnapshot snapshot)
    {
        snapshot = default;
        if (!isInitialized || !IsCellValid(cell))
        {
            return false;
        }

        Vector3 dir = ResolveSurfaceDirection(worldPosOrDir);
        bool isOcean = IsOceanCell(cell);
        int activeLayerCount = isOcean ? GetOceanActiveLayerCount(cell) : 0;

        snapshot.IsValid = true;
        snapshot.CellIndex = cell;
        snapshot.IsOcean = isOcean;
        snapshot.ActiveLayerCount = activeLayerCount;
        snapshot.Insolation = GetInsolation(dir);
        snapshot.VentStrength = ventStrength != null && cell < ventStrength.Length ? Mathf.Max(0f, ventStrength[cell]) : 0f;
        snapshot.EffectiveLegacy = GetEffectiveLegacyEnvironment(cell, dir);
        snapshot.EffectiveTemperatureKelvin = snapshot.EffectiveLegacy.TemperatureKelvin;
        snapshot.EffectiveCO2 = Get(ResourceType.CO2, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.EffectiveO2 = Get(ResourceType.O2, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.EffectiveCH4 = Get(ResourceType.CH4, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.EffectiveOrganicC = Get(ResourceType.OrganicC, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.EffectiveH2 = Get(ResourceType.H2, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.EffectiveH2S = Get(ResourceType.H2S, cell, AggregateCompatibilityCallsite.DebugTelemetry);
        snapshot.EffectiveDissolvedFe2Plus = Get(ResourceType.DissolvedFe2Plus, cell, AggregateCompatibilityCallsite.DebugTelemetry);

        if (!isOcean || activeLayerCount <= 0)
        {
            snapshot.OceanLayers = null;
            return true;
        }

        OceanLayerSnapshot[] layers = new OceanLayerSnapshot[activeLayerCount];
        for (int layer = 0; layer < activeLayerCount; layer++)
        {
            OceanLayerSnapshot layerSnapshot = default;
            layerSnapshot.LayerIndex = layer;
            layerSnapshot.O2 = GetLayerResource(ResourceType.O2, cell, layer);
            layerSnapshot.DissolvedFe2Plus = GetLayerResource(ResourceType.DissolvedFe2Plus, cell, layer);
            layerSnapshot.CO2 = GetLayerResource(ResourceType.CO2, cell, layer);
            layerSnapshot.CH4 = GetLayerResource(ResourceType.CH4, cell, layer);
            layerSnapshot.OrganicC = GetLayerResource(ResourceType.OrganicC, cell, layer);
            layerSnapshot.H2 = GetLayerResource(ResourceType.H2, cell, layer);
            layerSnapshot.H2S = GetLayerResource(ResourceType.H2S, cell, layer);
            layerSnapshot.LightFactor = GetLayerLightFactor(cell, layer);
            layerSnapshot.TemperatureOffset = GetLayerTemperatureOffset(cell, layer);
            layerSnapshot.TemperatureKelvinEstimate = Mathf.Max(minTempKelvin, snapshot.EffectiveTemperatureKelvin + layerSnapshot.TemperatureOffset);
            layers[layer] = layerSnapshot;
        }

        snapshot.OceanLayers = layers;
        return true;
    }

    public bool TryGetCellInspectionSnapshotByDirection(Vector3 worldPosOrDir, out CellInspectionSnapshot snapshot)
    {
        snapshot = default;
        if (!isInitialized || resolution <= 0)
        {
            return false;
        }

        Vector3 dir = ResolveSurfaceDirection(worldPosOrDir);
        int cell = GetCellIndexFromDirection(dir);
        return TryGetCellInspectionSnapshot(cell, dir, out snapshot);
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
            cellIndex = GetCellIndexFromDirection(surfaceDir);
        }

        bool underwater = IsOceanCell(cellIndex);
        float insolationDamping = underwater ? Mathf.Clamp01(oceanTempDamping) : 1f;
        float insolationTerm = Mathf.Max(0f, insolationTempGain) * insolation * insolationDamping;

        float ventTerm = 0f;
        if (ventHeat != null && cellIndex >= 0 && cellIndex < ventHeat.Length)
        {
            ventTerm = Mathf.Max(0f, ventHeat[cellIndex]);
        }

        if (underwater && enableLayeredOcean)
        {
            insolationTerm *= GetLayeredSolarHeatingAggregateFactor(cellIndex);
            ventTerm *= GetLayeredVentHeatingAggregateFactor(cellIndex);
        }

        float tempKelvin = baseTempKelvin + insolationTerm + ventTerm;
        if (underwater && enableLayeredOcean)
        {
            int active = GetOceanActiveLayerCount(cellIndex);
            if (active > 0)
            {
                float layerOffset = 0f;
                for (int layer = 0; layer < active; layer++)
                {
                    layerOffset += GetLayerTemperatureOffset(cellIndex, layer);
                }

                tempKelvin += layerOffset / active;
            }
        }
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

        // Resource/simulation arrays are sized from simulationResolution (or visual resolution when in compatibility mode).
        int targetResolution = SimulationResolution;

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
        ch4 = new float[cellCount];
        s0 = new float[cellCount];
        p = new float[cellCount];
        fe = new float[cellCount];
        si = new float[cellCount];
        ca = new float[cellCount];
        cellDirections = new Vector3[cellCount];
        ventMask = new byte[cellCount];
        ventStrength = new float[cellCount];
        ventHeat = new float[cellCount];
        ventHeatTmp = new float[cellCount];
        h2sMixTmp = new float[cellCount];
        h2MixTmp = new float[cellCount];
        surfaceO2DemandTmp = new float[cellCount];
        ventHeatNeighbors = new int[cellCount * NeighborCount];
        oceanMask = new byte[cellCount];
        oceanActiveLayerCounts = new byte[cellCount];
        oceanLayerLightFactors = new float[cellCount * MaxOceanLayers];
        oceanLayerTemperatureOffsets = new float[cellCount * MaxOceanLayers];
        layeredOrganicCTmp = new float[cellCount * MaxOceanLayers];
        ConfigureOceanDissolvedSpecies(cellCount);
        ConfigureLayeredOceanResources(cellCount);
        EnsureScentArrays(cellCount);

        string resourceCacheKey = PlanetGenerationCache.BuildResourceCacheKeyString(planetGenerator, this, resolution);
        string resourceCachePath = PlanetGenerationCache.BuildResourceCachePath(resourceCacheKey);
        bool loadedResourceCache = PlanetGenerationCache.TryLoadResource(resourceCachePath, cellCount, out PlanetGenerationCache.ResourceData cachedResourceData);

        int ventCount = 0;
        int oceanCellCount = 0;
        if (loadedResourceCache)
        {
            cellDirections = cachedResourceData.CellDirections;
            oceanMask = cachedResourceData.OceanMask;
            p = cachedResourceData.Phosphorus;
            fe = cachedResourceData.Iron;
            si = cachedResourceData.Silicon;
            ca = cachedResourceData.Calcium;
            ventMask = cachedResourceData.VentMask;
            ventStrength = cachedResourceData.VentStrength;
            ventCells = cachedResourceData.VentCells;
            ventCount = ventCells.Length;
            oceanCellCount = CountDomainCells(oceanMask, true);

            float allCellInventoryScale = ComputeDomainPerCellInventoryScale(cellCount, cellCount);
            float oceanCellInventoryScale = ComputeDomainPerCellInventoryScale(cellCount, oceanCellCount);

            for (int cell = 0; cell < cellCount; cell++)
            {
                bool isOcean = oceanMask[cell] != 0;

                co2[cell] = Mathf.Max(0f, baselineCO2 * allCellInventoryScale);
                o2[cell] = Mathf.Max(0f, baselineO2 * allCellInventoryScale);
                organicC[cell] = 0f;
                ch4[cell] = Mathf.Max(0f, baselineCH4 * allCellInventoryScale);
                s0[cell] = Mathf.Max(0f, baselineS0 * allCellInventoryScale);

                bool hasVent = ventMask[cell] != 0;
                float strength = hasVent ? ventStrength[cell] : 0f;
                h2s[cell] = strength;
                h2[cell] = strength;

                if (isOcean)
                {
                    SetOceanDissolvedInitial(ResourceType.DissolvedFe2Plus, cell, Mathf.Max(0f, initialDissolvedFe2PlusPerOceanCell * oceanCellInventoryScale));
                }
                else
                {
                    SetOceanDissolvedInitial(ResourceType.DissolvedFe2Plus, cell, 0f);
                }
            }

            Debug.Log($"[PlanetGenerationCache] Loaded resource-generation cache ({resourceCachePath}).");
        }
        else
        {
            for (int cell = 0; cell < cellCount; cell++)
            {
                Vector3 dir = CellIndexToDirection(cell, resolution);
                cellDirections[cell] = dir;
                bool isOcean = planetGenerator.IsOceanAtDirection(dir);
                oceanMask[cell] = isOcean ? (byte)1 : (byte)0;
                if (isOcean)
                {
                    oceanCellCount++;
                }
            }

            float allCellInventoryScale = ComputeDomainPerCellInventoryScale(cellCount, cellCount);
            float oceanCellInventoryScale = ComputeDomainPerCellInventoryScale(cellCount, oceanCellCount);
            float landCellInventoryScale = ComputeDomainPerCellInventoryScale(cellCount, cellCount - oceanCellCount);

            for (int cell = 0; cell < cellCount; cell++)
            {
                Vector3 dir = cellDirections[cell];
                bool isOcean = oceanMask[cell] != 0;

                co2[cell] = Mathf.Max(0f, baselineCO2 * allCellInventoryScale);
                o2[cell] = Mathf.Max(0f, baselineO2 * allCellInventoryScale);
                organicC[cell] = 0f;
                ch4[cell] = Mathf.Max(0f, baselineCH4 * allCellInventoryScale);
                s0[cell] = Mathf.Max(0f, baselineS0 * allCellInventoryScale);

                float phosphorusNoise = SampleResourceNoise(dir, new Vector3(13.7f, -4.2f, 9.9f));
                float ironNoise = SampleResourceNoise(dir, new Vector3(-8.4f, 3.1f, 15.2f));
                float siliconNoise = SampleResourceNoise(dir, new Vector3(2.3f, 11.9f, -6.6f));
                float calciumNoise = SampleResourceNoise(dir, new Vector3(-12.5f, -7.4f, 4.8f));

                float domainInventoryScale = isOcean ? oceanCellInventoryScale : landCellInventoryScale;
                p[cell] = Mathf.Max(0f, phosphorusScale * phosphorusNoise * domainInventoryScale);
                fe[cell] = Mathf.Max(0f, ironScale * ironNoise * domainInventoryScale);
                si[cell] = Mathf.Max(0f, (baselineSi + siliconPatchScale * (siliconNoise - 0.5f)) * domainInventoryScale);
                ca[cell] = Mathf.Max(0f, (baselineCa + calciumPatchScale * (calciumNoise - 0.5f)) * domainInventoryScale);

                if (isOcean)
                {
                    SetOceanDissolvedInitial(ResourceType.DissolvedFe2Plus, cell, Mathf.Max(0f, initialDissolvedFe2PlusPerOceanCell * oceanCellInventoryScale));
                }
                else
                {
                    SetOceanDissolvedInitial(ResourceType.DissolvedFe2Plus, cell, 0f);
                }

                ventMask[cell] = 0;
                ventStrength[cell] = 0f;
                h2s[cell] = 0f;
                h2[cell] = 0f;
            }

            // Resolution-independent vent abundance:
            // - each cell still gets deterministic procedural randomness from vent noise
            // - but the number of selected vents is normalized to a reference-resolution planet area
            //   so reducing simulation tile count does not collapse total vent abundance.
            float[] ventNoiseSamples = new float[cellCount];
            float[] ventStrengthCandidates = new float[cellCount];
            int thresholdVentCount = 0;
            for (int cell = 0; cell < cellCount; cell++)
            {
                float ventNoise = HighFrequencyNoise(cellDirections[cell]);
                ventNoiseSamples[cell] = ventNoise;
                if (ventNoise > ventThreshold)
                {
                    float strengthT = Mathf.InverseLerp(ventThreshold, 1f, ventNoise);
                    ventStrengthCandidates[cell] = Mathf.Lerp(ventStrengthMin, ventStrengthMax, Mathf.Clamp01(strengthT));
                    thresholdVentCount++;
                }
            }

            int targetVentCount = ResolveResolutionIndependentVentTarget(cellCount, thresholdVentCount, out float desiredVentCount);
            if (targetVentCount > 0)
            {
                int[] sortedCells = new int[cellCount];
                for (int i = 0; i < cellCount; i++)
                {
                    sortedCells[i] = i;
                }

                System.Array.Sort(sortedCells, (a, b) =>
                {
                    int compare = ventNoiseSamples[b].CompareTo(ventNoiseSamples[a]);
                    return compare != 0 ? compare : a.CompareTo(b);
                });

                for (int i = 0; i < targetVentCount; i++)
                {
                    int cell = sortedCells[i];
                    float strength = ventStrengthCandidates[cell];
                    if (strength <= 0f)
                    {
                        float strengthT = Mathf.InverseLerp(ventThreshold, 1f, ventNoiseSamples[cell]);
                        strength = Mathf.Lerp(ventStrengthMin, ventStrengthMax, Mathf.Clamp01(strengthT));
                    }

                    ventMask[cell] = 1;
                    ventStrength[cell] = strength;
                    h2s[cell] = strength;
                    h2[cell] = strength;
                }
            }

            ventCount = targetVentCount;
            ventSourceStrengthNormalization = ComputeVentSourceStrengthNormalization(desiredVentCount, ventCount);
        }

        if (loadedResourceCache)
        {
            int thresholdVentCount = 0;
            for (int cell = 0; cell < cellCount; cell++)
            {
                if (HighFrequencyNoise(cellDirections[cell]) > ventThreshold)
                {
                    thresholdVentCount++;
                }
            }

            ResolveResolutionIndependentVentTarget(cellCount, thresholdVentCount, out float desiredVentCount);
            ventSourceStrengthNormalization = ComputeVentSourceStrengthNormalization(desiredVentCount, ventCount);
        }

        if (ventSourceStrengthNormalization <= 0f || !float.IsFinite(ventSourceStrengthNormalization))
        {
            ventSourceStrengthNormalization = 1f;
        }
        isInitialized = true;
        BuildVentHeatNeighbors();
        RebuildVentHeatField();
        InitializeLayeredOceanState();

        int total = cellCount;
        Debug.Log($"Vents: {ventCount}/{total} = {(100f * ventCount / total):F1}% (sourceNorm={ventSourceStrengthNormalization:0.###})");

        if (!loadedResourceCache)
        {
            ventCells = new int[ventCount];
            int ventWrite = 0;
            for (int cell = 0; cell < cellCount; cell++)
            {
                if (ventMask[cell] != 0)
                {
                    ventCells[ventWrite++] = cell;
                }
            }

            PlanetGenerationCache.SaveResource(
                resourceCachePath,
                new PlanetGenerationCache.ResourceData
                {
                    CellDirections = (Vector3[])cellDirections.Clone(),
                    OceanMask = (byte[])oceanMask.Clone(),
                    Phosphorus = (float[])p.Clone(),
                    Iron = (float[])fe.Clone(),
                    Silicon = (float[])si.Clone(),
                    Calcium = (float[])ca.Clone(),
                    VentMask = (byte[])ventMask.Clone(),
                    VentStrength = (float[])ventStrength.Clone(),
                    VentCells = (int[])ventCells.Clone()
                });
            Debug.Log($"[PlanetGenerationCache] Regenerated resources and saved cache ({resourceCachePath}).");
        }

        ventTimer = 0f;
        atmosphereTimer = 0f;
        initialDissolvedFe2PlusTotal = 0f;
        UpdateAtmosphereDebugMeans();
        UpdateOceanChemistryDebugStats();
        LogInitializationInventoryDebug(cellCount, CountDomainCells(oceanMask, true));
        Debug.Log($"Initialized {VentCount} vents", this);
    }

    private void ConfigureOceanDissolvedSpecies(int cellCount)
    {
        oceanDissolvedResources.Clear();
        RegisterOceanDissolvedSpecies(ResourceType.DissolvedFe2Plus, cellCount);
        // Future dissolved ocean species (for example DissolvedCa / DissolvedSi)
        // can be registered here without changing the rest of the resource map flow.
    }

    private void ConfigureLayeredOceanResources(int cellCount)
    {
        layeredOceanResources.Clear();
        if (cellCount <= 0)
        {
            return;
        }

        RegisterLayeredOceanResource(ResourceType.O2, cellCount);
        RegisterLayeredOceanResource(ResourceType.CO2, cellCount);
        RegisterLayeredOceanResource(ResourceType.OrganicC, cellCount);
        RegisterLayeredOceanResource(ResourceType.H2, cellCount);
        RegisterLayeredOceanResource(ResourceType.H2S, cellCount);
        RegisterLayeredOceanResource(ResourceType.S0, cellCount);
        RegisterLayeredOceanResource(ResourceType.CH4, cellCount);
        RegisterLayeredOceanResource(ResourceType.DissolvedFe2Plus, cellCount);
        RegisterLayeredOceanResource(ResourceType.DissolvedOrganicLeak, cellCount);
        RegisterLayeredOceanResource(ResourceType.ToxicProteolyticWaste, cellCount);
        // CO2 now participates in layered ocean chemistry.
        // Atmosphere coupling is still explicit/top-layer only in ApplyAtmosphereMixing(),
        // while legacy aggregate Get()/debug views are bridged via SyncLegacyOceanFromLayeredArrays().
    }

    private void RegisterLayeredOceanResource(ResourceType resourceType, int cellCount)
    {
        layeredOceanResources[resourceType] = new float[cellCount * MaxOceanLayers];
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
            layeredScentWasteTmp = null;
            layeredScentLeakTmp = null;
            layeredOrganicCTmp = null;
            return;
        }

        EnsureArrayCapacity(ref toxicProteolyticWaste, cellCount);
        EnsureArrayCapacity(ref dissolvedOrganicLeak, cellCount);
        EnsureArrayCapacity(ref scentWasteTmp, cellCount);
        EnsureArrayCapacity(ref scentLeakTmp, cellCount);
        EnsureArrayCapacity(ref layeredScentWasteTmp, cellCount * MaxOceanLayers);
        EnsureArrayCapacity(ref layeredScentLeakTmp, cellCount * MaxOceanLayers);
        EnsureArrayCapacity(ref layeredOrganicCTmp, cellCount * MaxOceanLayers);
    }

    public void ClearScents()
    {
        if (toxicProteolyticWaste == null || dissolvedOrganicLeak == null)
        {
            return;
        }

        System.Array.Clear(toxicProteolyticWaste, 0, toxicProteolyticWaste.Length);
        System.Array.Clear(dissolvedOrganicLeak, 0, dissolvedOrganicLeak.Length);

        float[] layeredLeak = GetLayeredOceanArray(ResourceType.DissolvedOrganicLeak);
        float[] layeredWaste = GetLayeredOceanArray(ResourceType.ToxicProteolyticWaste);
        if (layeredLeak != null)
        {
            System.Array.Clear(layeredLeak, 0, layeredLeak.Length);
        }

        if (layeredWaste != null)
        {
            System.Array.Clear(layeredWaste, 0, layeredWaste.Length);
        }
    }

    public void AddScent(ResourceType scentType, int cell, float amount)
    {
        AddScent(scentType, cell, -1, amount);
    }

    public void AddScent(ResourceType scentType, int cell, int requestedLayerIndex, float amount)
    {
        if (!enableScentFields || !isInitialized || !IsCellValid(cell) || amount <= 0f)
        {
            return;
        }

        EnsureScentArrays();
        float cap = Mathf.Max(0f, scentMaxPerCell);
        if (enableLayeredOcean && IsOceanCell(cell))
        {
            float[] layered = GetLayeredOceanArray(scentType);
            int clampedLayer = ClampOceanLayerIndex(cell, requestedLayerIndex);
            if (layered != null && clampedLayer >= 0)
            {
                int layerIdx = GetLayeredArrayIndex(cell, clampedLayer);
                float updated = layered[layerIdx] + amount;
                layered[layerIdx] = cap > 0f ? Mathf.Min(cap, updated) : updated;
                SyncLegacyOceanResourceFromLayers(scentType, cell);
                return;
            }
        }

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

    public float GetScentValue(ResourceType scentType, int cell, int requestedLayerIndex, bool includeAdjacentLayers = true)
    {
        if (!enableScentFields || !isInitialized || !IsCellValid(cell))
        {
            return 0f;
        }

        if (!enableLayeredOcean || !IsOceanCell(cell))
        {
            return Get(scentType, cell, AggregateCompatibilityCallsite.ResourcePhysics);
        }

        int layerIndex = ClampOceanLayerIndex(cell, requestedLayerIndex);
        if (layerIndex < 0)
        {
            return Get(scentType, cell, AggregateCompatibilityCallsite.ResourcePhysics);
        }

        float[] layered = GetLayeredOceanArray(scentType);
        if (layered == null)
        {
            return Get(scentType, cell, AggregateCompatibilityCallsite.ResourcePhysics);
        }

        float center = GetLayerValue(layered, cell, layerIndex);
        if (!includeAdjacentLayers)
        {
            return center;
        }

        float coupling = Mathf.Clamp01(scentAdjacentLayerCoupling);
        if (coupling <= 0f)
        {
            return center;
        }

        int active = GetOceanActiveLayerCount(cell);
        if (active <= 1)
        {
            return center;
        }

        float adjacentSum = 0f;
        int adjacentCount = 0;
        int above = layerIndex - 1;
        int below = layerIndex + 1;
        if (above >= 0)
        {
            adjacentSum += GetLayerValue(layered, cell, above);
            adjacentCount++;
        }

        if (below < active)
        {
            adjacentSum += GetLayerValue(layered, cell, below);
            adjacentCount++;
        }

        if (adjacentCount <= 0)
        {
            return center;
        }

        float adjacentAvg = adjacentSum / adjacentCount;
        return Mathf.Lerp(center, adjacentAvg, coupling);
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
        int passes = Mathf.Clamp(scentDiffusePasses, 0, 4);
        float strength = Mathf.Clamp01(scentDiffuseStrength);
        float capPerCell = Mathf.Max(0f, scentMaxPerCell);
        bool useLayeredScents = enableLayeredOcean
                                && GetLayeredOceanArray(ResourceType.DissolvedOrganicLeak) != null
                                && GetLayeredOceanArray(ResourceType.ToxicProteolyticWaste) != null;

        if (useLayeredScents)
        {
            ApplyLayeredScentDecayAndDiffuse(cellCount, wasteDecay, leakDecay, passes, strength, capPerCell);
            SyncLegacyOceanResourceFromLayers(ResourceType.DissolvedOrganicLeak);
            SyncLegacyOceanResourceFromLayers(ResourceType.ToxicProteolyticWaste);
            return;
        }

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

    private void ApplyLayeredScentDecayAndDiffuse(int cellCount, float wasteDecay, float leakDecay, int passes, float horizontalStrength, float capPerCell)
    {
        float[] layeredWaste = GetLayeredOceanArray(ResourceType.ToxicProteolyticWaste);
        float[] layeredLeak = GetLayeredOceanArray(ResourceType.DissolvedOrganicLeak);
        if (layeredWaste == null || layeredLeak == null)
        {
            return;
        }

        for (int cell = 0; cell < cellCount; cell++)
        {
            int active = GetOceanActiveLayerCount(cell);
            if (active <= 0)
            {
                continue;
            }

            for (int layer = 0; layer < active; layer++)
            {
                int idx = GetLayeredArrayIndex(cell, layer);
                layeredWaste[idx] = Mathf.Max(0f, layeredWaste[idx] * wasteDecay);
                layeredLeak[idx] = Mathf.Max(0f, layeredLeak[idx] * leakDecay);
            }
        }

        if (passes <= 0)
        {
            return;
        }

        if (ventHeatNeighbors == null)
        {
            BuildVentHeatNeighbors();
        }

        if (ventHeatNeighbors == null)
        {
            return;
        }

        float verticalCoupling = Mathf.Clamp01(scentAdjacentLayerCoupling);
        for (int pass = 0; pass < passes; pass++)
        {
            for (int cell = 0; cell < cellCount; cell++)
            {
                int active = GetOceanActiveLayerCount(cell);
                if (active <= 0)
                {
                    continue;
                }

                for (int layer = 0; layer < active; layer++)
                {
                    int idx = GetLayeredArrayIndex(cell, layer);
                    int baseIndex = cell * NeighborCount;
                    float wasteNeighborSum = 0f;
                    float leakNeighborSum = 0f;
                    int neighborCount = 0;

                    for (int n = 0; n < NeighborCount; n++)
                    {
                        int neighborCell = ventHeatNeighbors[baseIndex + n];
                        if (neighborCell < 0 || neighborCell >= cellCount || !IsOceanCell(neighborCell))
                        {
                            continue;
                        }

                        int neighborLayer = ClampOceanLayerIndex(neighborCell, layer);
                        if (neighborLayer < 0)
                        {
                            continue;
                        }

                        int neighborIdx = GetLayeredArrayIndex(neighborCell, neighborLayer);
                        wasteNeighborSum += layeredWaste[neighborIdx];
                        leakNeighborSum += layeredLeak[neighborIdx];
                        neighborCount++;
                    }

                    float centerWaste = layeredWaste[idx];
                    float centerLeak = layeredLeak[idx];
                    float wasteNeighborAvg = neighborCount > 0 ? wasteNeighborSum / neighborCount : centerWaste;
                    float leakNeighborAvg = neighborCount > 0 ? leakNeighborSum / neighborCount : centerLeak;
                    float mixedWaste = Mathf.Lerp(centerWaste, wasteNeighborAvg, horizontalStrength);
                    float mixedLeak = Mathf.Lerp(centerLeak, leakNeighborAvg, horizontalStrength);

                    if (verticalCoupling > 0f && active > 1)
                    {
                        float wasteVerticalSum = 0f;
                        float leakVerticalSum = 0f;
                        int verticalCount = 0;
                        int above = layer - 1;
                        int below = layer + 1;
                        if (above >= 0)
                        {
                            int aboveIdx = GetLayeredArrayIndex(cell, above);
                            wasteVerticalSum += layeredWaste[aboveIdx];
                            leakVerticalSum += layeredLeak[aboveIdx];
                            verticalCount++;
                        }

                        if (below < active)
                        {
                            int belowIdx = GetLayeredArrayIndex(cell, below);
                            wasteVerticalSum += layeredWaste[belowIdx];
                            leakVerticalSum += layeredLeak[belowIdx];
                            verticalCount++;
                        }

                        if (verticalCount > 0)
                        {
                            float wasteVerticalAvg = wasteVerticalSum / verticalCount;
                            float leakVerticalAvg = leakVerticalSum / verticalCount;
                            mixedWaste = Mathf.Lerp(mixedWaste, wasteVerticalAvg, verticalCoupling);
                            mixedLeak = Mathf.Lerp(mixedLeak, leakVerticalAvg, verticalCoupling);
                        }
                    }

                    layeredScentWasteTmp[idx] = capPerCell > 0f ? Mathf.Min(capPerCell, Mathf.Max(0f, mixedWaste)) : Mathf.Max(0f, mixedWaste);
                    layeredScentLeakTmp[idx] = capPerCell > 0f ? Mathf.Min(capPerCell, Mathf.Max(0f, mixedLeak)) : Mathf.Max(0f, mixedLeak);
                }
            }

            float[] wasteSwap = layeredWaste;
            layeredWaste = layeredScentWasteTmp;
            layeredScentWasteTmp = wasteSwap;

            float[] leakSwap = layeredLeak;
            layeredLeak = layeredScentLeakTmp;
            layeredScentLeakTmp = leakSwap;

            layeredOceanResources[ResourceType.ToxicProteolyticWaste] = layeredWaste;
            layeredOceanResources[ResourceType.DissolvedOrganicLeak] = layeredLeak;
        }
    }

    // Atmosphere coupling note:
    // - Land cells continue to exchange directly using legacy per-cell volatile arrays.
    // - Ocean CO2 now exchanges with atmosphere through ocean layer 0 (surface) only.
    // - Deeper ocean layers affect atmosphere indirectly through layered vertical diffusion/mixing.
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
            debugGlobalCH4 = 0f;
            return;
        }

        float[] layeredCo2 = GetLayeredOceanArray(ResourceType.CO2);
        bool useLayeredCo2 = enableLayeredOcean && layeredCo2 != null;

        float totalCO2 = 0f;
        float totalAtmosphereO2 = 0f;
        float totalAtmosphereCH4 = 0f;
        int atmosphereCellCount = 0;
        for (int cell = 0; cell < cellCount; cell++)
        {
            if (useLayeredCo2 && oceanMask[cell] != 0)
            {
                int top = GetOceanTopLayerIndex(cell);
                totalCO2 += top >= 0 ? layeredCo2[GetLayeredArrayIndex(cell, top)] : co2[cell];
            }
            else
            {
                totalCO2 += co2[cell];
            }
            if (oceanMask[cell] == 0)
            {
                totalAtmosphereO2 += o2[cell];
                totalAtmosphereCH4 += ch4[cell];
                atmosphereCellCount++;
            }
        }

        float invCellCount = 1f / cellCount;
        float globalCO2 = totalCO2 * invCellCount;
        float globalO2 = atmosphereCellCount > 0 ? totalAtmosphereO2 / atmosphereCellCount : 0f;
        float globalCH4 = atmosphereCellCount > 0 ? totalAtmosphereCH4 / atmosphereCellCount : 0f;

        float landRate = Mathf.Max(0f, landExchangeRate);
        float oceanRate = Mathf.Max(0f, oceanExchangeRate);

        for (int cell = 0; cell < cellCount; cell++)
        {
            float exchangeRate = oceanMask[cell] != 0 ? oceanRate : landRate;

            float currentCO2 = co2[cell];
            int topLayer = -1;
            if (useLayeredCo2 && oceanMask[cell] != 0)
            {
                topLayer = GetOceanTopLayerIndex(cell);
                if (topLayer >= 0)
                {
                    currentCO2 = layeredCo2[GetLayeredArrayIndex(cell, topLayer)];
                }
            }

            float mixedCO2 = currentCO2 + exchangeRate * (globalCO2 - currentCO2);
            float mixedO2 = o2[cell] + exchangeRate * (globalO2 - o2[cell]);
            float mixedCH4 = ch4[cell] + exchangeRate * (globalCH4 - ch4[cell]);

            if (topLayer >= 0)
            {
                layeredCo2[GetLayeredArrayIndex(cell, topLayer)] = Mathf.Max(0f, mixedCO2);
            }
            else
            {
                co2[cell] = Mathf.Max(0f, mixedCO2);
            }
            o2[cell] = Mathf.Max(0f, mixedO2);
            ch4[cell] = Mathf.Max(0f, mixedCH4);
        }

        if (useLayeredCo2)
        {
            SyncLegacyOceanResourceFromLayers(ResourceType.CO2);
        }

        UpdateAtmosphereDebugMeans();
    }

    private void TransferAtmosphericO2ToSurfaceFe2Demand(float dt)
    {
        if (!isInitialized || o2 == null || oceanMask == null)
        {
            return;
        }

        float transferFraction = Mathf.Clamp01(atmosphereToOceanO2TransferFractionPerTick);
        float rate = Mathf.Max(0f, fe2PlusOxidationRatePerSecond);
        float o2PerFe2 = Mathf.Max(0f, o2ConsumptionPerFe2PlusOxidized);
        if (transferFraction <= 0f || rate <= 0f || o2PerFe2 <= 0f)
        {
            return;
        }

        float[] demandWeights = surfaceO2DemandTmp;
        if (demandWeights == null || demandWeights.Length != o2.Length)
        {
            return;
        }

        float totalDemand = 0f;

        if (enableLayeredOcean)
        {
            float[] dissolvedFe2PlusLayers = GetLayeredOceanArray(ResourceType.DissolvedFe2Plus);
            float[] o2Layers = GetLayeredOceanArray(ResourceType.O2);
            if (dissolvedFe2PlusLayers == null || o2Layers == null)
            {
                return;
            }

            for (int cell = 0; cell < oceanMask.Length; cell++)
            {
                demandWeights[cell] = 0f;
                if (oceanMask[cell] == 0)
                {
                    continue;
                }

                int topLayer = GetOceanTopLayerIndex(cell);
                if (topLayer < 0)
                {
                    continue;
                }

                int idx = GetLayeredArrayIndex(cell, topLayer);
                if (idx < 0 || idx >= dissolvedFe2PlusLayers.Length || idx >= o2Layers.Length)
                {
                    continue;
                }

                float availableFe2 = Mathf.Max(0f, dissolvedFe2PlusLayers[idx]);
                if (availableFe2 <= 0f)
                {
                    continue;
                }

                float desiredOxidation = availableFe2 * rate * Mathf.Max(0f, dt);
                float o2Demand = desiredOxidation * o2PerFe2;
                float localDeficit = Mathf.Max(0f, o2Demand - Mathf.Max(0f, o2Layers[idx]));
                if (localDeficit <= 0f)
                {
                    continue;
                }

                demandWeights[cell] = localDeficit;
                totalDemand += localDeficit;
            }
        }
        else
        {
            float[] dissolvedFe2Plus = GetOceanDissolvedArray(ResourceType.DissolvedFe2Plus);
            if (dissolvedFe2Plus == null)
            {
                return;
            }

            for (int cell = 0; cell < oceanMask.Length; cell++)
            {
                demandWeights[cell] = 0f;
                if (oceanMask[cell] == 0)
                {
                    continue;
                }

                float availableFe2 = Mathf.Max(0f, dissolvedFe2Plus[cell]);
                if (availableFe2 <= 0f)
                {
                    continue;
                }

                float desiredOxidation = availableFe2 * rate * Mathf.Max(0f, dt);
                float o2Demand = desiredOxidation * o2PerFe2;
                float localDeficit = Mathf.Max(0f, o2Demand - Mathf.Max(0f, o2[cell]));
                if (localDeficit <= 0f)
                {
                    continue;
                }

                demandWeights[cell] = localDeficit;
                totalDemand += localDeficit;
            }
        }

        if (totalDemand <= 1e-6f)
        {
            return;
        }

        float totalAtmosphericO2 = 0f;
        for (int cell = 0; cell < o2.Length; cell++)
        {
            if (oceanMask[cell] == 0)
            {
                totalAtmosphericO2 += Mathf.Max(0f, o2[cell]);
            }
        }

        if (totalAtmosphericO2 <= 0f)
        {
            return;
        }

        float transferredO2 = Mathf.Min(totalDemand, totalAtmosphericO2 * transferFraction);
        if (transferredO2 <= 0f)
        {
            return;
        }

        float toRemove = transferredO2;
        for (int cell = 0; cell < o2.Length; cell++)
        {
            if (oceanMask[cell] != 0 || toRemove <= 0f)
            {
                continue;
            }

            float share = totalAtmosphericO2 > 0f ? Mathf.Max(0f, o2[cell]) / totalAtmosphericO2 : 0f;
            float remove = Mathf.Min(o2[cell], transferredO2 * share);
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
                o2[cell] = Mathf.Max(0f, o2[cell] - remove);
                toRemove -= remove;
            }
        }

        float actualTransferred = transferredO2 - Mathf.Max(0f, toRemove);
        if (actualTransferred <= 0f)
        {
            return;
        }

        if (enableLayeredOcean)
        {
            float[] o2Layers = GetLayeredOceanArray(ResourceType.O2);
            if (o2Layers == null)
            {
                return;
            }

            for (int cell = 0; cell < oceanMask.Length; cell++)
            {
                float demand = demandWeights[cell];
                if (demand <= 0f)
                {
                    continue;
                }

                int topLayer = GetOceanTopLayerIndex(cell);
                if (topLayer < 0)
                {
                    continue;
                }

                int idx = GetLayeredArrayIndex(cell, topLayer);
                if (idx < 0 || idx >= o2Layers.Length)
                {
                    continue;
                }

                o2Layers[idx] += actualTransferred * (demand / totalDemand);
            }
        }
        else
        {
            for (int cell = 0; cell < oceanMask.Length; cell++)
            {
                float demand = demandWeights[cell];
                if (demand <= 0f)
                {
                    continue;
                }

                o2[cell] += actualTransferred * (demand / totalDemand);
            }
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

    private void InitializeLayeredOceanState()
    {
        if (!enableLayeredOcean || oceanActiveLayerCounts == null || oceanMask == null)
        {
            return;
        }

        for (int cell = 0; cell < oceanActiveLayerCounts.Length; cell++)
        {
            if (oceanMask[cell] == 0)
            {
                oceanActiveLayerCounts[cell] = 0;
                continue;
            }

            Vector3 dir = GetDirectionForCell(cell);
            float localDepth = planetGenerator != null ? planetGenerator.GetLocalOceanDepth(dir) : 0f;
            oceanActiveLayerCounts[cell] = (byte)Mathf.Clamp(DetermineActiveLayerCount(localDepth), 1, MaxOceanLayers);
        }


        SyncLayeredOceanFromLegacyArrays();
        UpdateLayerLightAndTemperatureProfiles();
        SyncLegacyOceanFromLayeredArrays();
        UpdateLayeredDebugSample();
    }

    private void ApplyVerticalLayeredOceanProcesses(float dt)
    {
        if (!enableLayeredOcean || !isInitialized)
        {
            return;
        }

        UpdateLayerLightAndTemperatureProfiles();
        ApplySurfaceOxygenationToLayers(dt);
        ApplyVerticalDiffusion(dt);
        ApplyLayeredOrganicCTransport(dt);
        SyncLegacyOceanFromLayeredArrays();
        UpdateLayeredDebugSample();
    }

    private void ApplySurfaceOxygenationToLayers(float dt)
    {
        float oxygenationRate = Mathf.Clamp01(layeredSurfaceOxygenationRate) * Mathf.Max(0f, dt);
        if (oxygenationRate <= 0f)
        {
            return;
        }

        float targetSurfaceO2 = Mathf.Max(0f, debugGlobalO2);
        float[] o2Layers = GetLayeredOceanArray(ResourceType.O2);
        if (o2Layers == null || oceanMask == null)
        {
            return;
        }

        for (int cell = 0; cell < oceanMask.Length; cell++)
        {
            if (!IsOceanCell(cell))
            {
                continue;
            }

            int top = GetOceanTopLayerIndex(cell);
            if (top < 0)
            {
                continue;
            }

            int idx = GetLayeredArrayIndex(cell, top);
            if (idx < 0 || idx >= o2Layers.Length)
            {
                continue;
            }

            o2Layers[idx] = Mathf.Max(0f, Mathf.Lerp(o2Layers[idx], targetSurfaceO2, oxygenationRate));
        }
    }

    private void ApplyVerticalDiffusion(float dt)
    {
        float transferRate = Mathf.Clamp01(layeredVerticalMixRate) * Mathf.Max(0f, dt);
        if (transferRate <= 0f)
        {
            return;
        }

        ApplyVerticalDiffusionForResource(ResourceType.O2, transferRate);
        ApplyVerticalDiffusionForResource(ResourceType.CO2, transferRate);
        ApplyVerticalDiffusionForResource(ResourceType.H2, transferRate);
        ApplyVerticalDiffusionForResource(ResourceType.H2S, transferRate);
        ApplyVerticalDiffusionForResource(ResourceType.CH4, transferRate);
        ApplyVerticalDiffusionForResource(ResourceType.DissolvedFe2Plus, transferRate);
        // S0 intentionally excluded from strong symmetric vertical diffusion.
        // In this pass we treat elemental sulfur as primarily local particulate storage,
        // preserving layer-local persistence while compatibility bridges remain in place.
    }

    private void ApplyVerticalDiffusionForResource(ResourceType resourceType, float transferRate)
    {
        float[] layeredArray = GetLayeredOceanArray(resourceType);
        if (layeredArray == null)
        {
            return;
        }

        for (int cell = 0; cell < oceanMask.Length; cell++)
        {
            int activeCount = GetOceanActiveLayerCount(cell);
            if (activeCount <= 1)
            {
                continue;
            }

            for (int layer = 0; layer < activeCount - 1; layer++)
            {
                int a = GetLayeredArrayIndex(cell, layer);
                int b = GetLayeredArrayIndex(cell, layer + 1);
                float delta = (layeredArray[a] - layeredArray[b]) * transferRate;
                layeredArray[a] = Mathf.Max(0f, layeredArray[a] - delta);
                layeredArray[b] = Mathf.Max(0f, layeredArray[b] + delta);
            }
        }
    }

    private void ApplyLayeredOrganicCTransport(float dt)
    {
        float sinkRate = Mathf.Clamp01(layeredMarineSnowRate) * Mathf.Max(0f, dt);
        float lateralRate = Mathf.Clamp01(layeredOrganicCLateralSpreadRate) * Mathf.Max(0f, dt);
        float upwardBleedRate = Mathf.Clamp01(layeredOrganicCUpwardBleedRate) * Mathf.Max(0f, dt);
        if (sinkRate <= 0f && lateralRate <= 0f && upwardBleedRate <= 0f)
        {
            return;
        }

        float[] organicLayers = GetLayeredOceanArray(ResourceType.OrganicC);
        if (organicLayers == null || layeredOrganicCTmp == null || ventHeatNeighbors == null)
        {
            return;
        }

        ApplyOrganicCDownwardSettling(organicLayers, sinkRate);
        ApplyOrganicCLateralSpread(organicLayers, lateralRate);

        // Lateral pass uses a swap-buffer; reacquire the authoritative array before upward bleed.
        organicLayers = GetLayeredOceanArray(ResourceType.OrganicC);
        if (organicLayers == null)
        {
            return;
        }

        ApplyOrganicCUpwardBleed(organicLayers, upwardBleedRate);
    }

    private void ApplyOrganicCDownwardSettling(float[] organicLayers, float sinkRate)
    {
        if (sinkRate <= 0f)
        {
            return;
        }

        for (int cell = 0; cell < oceanMask.Length; cell++)
        {
            int activeCount = GetOceanActiveLayerCount(cell);
            if (activeCount <= 1)
            {
                continue;
            }

            // Marine-snow model:
            // OrganicC is mostly particulate detritus, so it preferentially sinks down one layer at a time.
            for (int layer = 0; layer < activeCount - 1; layer++)
            {
                int upper = GetLayeredArrayIndex(cell, layer);
                int lower = GetLayeredArrayIndex(cell, layer + 1);
                float flux = organicLayers[upper] * sinkRate;
                if (flux <= 0f)
                {
                    continue;
                }

                organicLayers[upper] = Mathf.Max(0f, organicLayers[upper] - flux);
                organicLayers[lower] = Mathf.Max(0f, organicLayers[lower] + flux);
            }
        }
    }

    private void ApplyOrganicCLateralSpread(float[] organicLayers, float lateralRate)
    {
        if (lateralRate <= 0f)
        {
            return;
        }

        int cellCount = oceanMask != null ? oceanMask.Length : 0;
        if (cellCount <= 0)
        {
            return;
        }

        System.Array.Copy(organicLayers, layeredOrganicCTmp, organicLayers.Length);

        for (int cell = 0; cell < cellCount; cell++)
        {
            int activeCount = GetOceanActiveLayerCount(cell);
            if (activeCount <= 0)
            {
                continue;
            }

            int baseNeighbor = cell * NeighborCount;
            for (int layer = 0; layer < activeCount; layer++)
            {
                float neighborSum = 0f;
                int neighborCount = 0;

                for (int n = 0; n < NeighborCount; n++)
                {
                    int neighbor = ventHeatNeighbors[baseNeighbor + n];
                    if (neighbor < 0 || neighbor >= cellCount || !IsOceanCell(neighbor))
                    {
                        continue;
                    }

                    int neighborLayer = ClampOceanLayerIndex(neighbor, layer);
                    if (neighborLayer < 0)
                    {
                        continue;
                    }

                    neighborSum += organicLayers[GetLayeredArrayIndex(neighbor, neighborLayer)];
                    neighborCount++;
                }

                if (neighborCount <= 0)
                {
                    continue;
                }

                int idx = GetLayeredArrayIndex(cell, layer);
                float neighborAvg = neighborSum / neighborCount;
                // Keep horizontal spread weak so top-layer production forms a narrow sinking plume.
                layeredOrganicCTmp[idx] = Mathf.Max(0f, Mathf.Lerp(organicLayers[idx], neighborAvg, lateralRate));
            }
        }

        float[] swap = layeredOrganicCTmp;
        layeredOrganicCTmp = organicLayers;
        layeredOceanResources[ResourceType.OrganicC] = swap;
    }

    private void ApplyOrganicCUpwardBleed(float[] organicLayers, float upwardBleedRate)
    {
        if (upwardBleedRate <= 0f)
        {
            return;
        }

        for (int cell = 0; cell < oceanMask.Length; cell++)
        {
            int activeCount = GetOceanActiveLayerCount(cell);
            if (activeCount <= 1)
            {
                continue;
            }

            // Optional tiny local redistribution to avoid hard discontinuities.
            // Intentionally one-way and very small: no symmetric vertical diffusion for OrganicC.
            for (int layer = activeCount - 1; layer > 0; layer--)
            {
                int lower = GetLayeredArrayIndex(cell, layer);
                int upper = GetLayeredArrayIndex(cell, layer - 1);
                float bleed = organicLayers[lower] * upwardBleedRate;
                if (bleed <= 0f)
                {
                    continue;
                }

                organicLayers[lower] = Mathf.Max(0f, organicLayers[lower] - bleed);
                organicLayers[upper] = Mathf.Max(0f, organicLayers[upper] + bleed);
            }
        }
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
        if (co2 == null || o2 == null || ch4 == null || co2.Length == 0)
        {
            debugGlobalCO2 = 0f;
            debugGlobalO2 = 0f;
            debugGlobalCH4 = 0f;
            return;
        }

        float totalCO2 = 0f;
        float totalAtmosphereO2 = 0f;
        float totalAtmosphereCH4 = 0f;
        int atmosphereCellCount = 0;
        for (int cell = 0; cell < co2.Length; cell++)
        {
            totalCO2 += co2[cell];
            if (oceanMask != null && oceanMask[cell] == 0)
            {
                totalAtmosphereO2 += o2[cell];
                totalAtmosphereCH4 += ch4[cell];
                atmosphereCellCount++;
            }
        }

        float invCellCount = 1f / co2.Length;
        debugGlobalCO2 = totalCO2 * invCellCount;
        debugGlobalO2 = atmosphereCellCount > 0 ? totalAtmosphereO2 / atmosphereCellCount : 0f;
        debugGlobalCH4 = atmosphereCellCount > 0 ? totalAtmosphereCH4 / atmosphereCellCount : 0f;
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

        // The vent list is now normalized to planet area (reference-resolution equivalent count).
        // We keep a secondary source scale so total injected chemistry stays approximately
        // resolution-independent if low resolutions cannot represent the full target vent count.
        float sourceScale = Mathf.Max(0f, ventSourceStrengthNormalization);

        bool applyH2SCap = ventH2SMax > 0f;
        bool applyH2Cap = ventH2Max > 0f;
        for (int i = 0; i < ventCells.Length; i++)
        {
            int cell = ventCells[i];

            if (ventsOnlyBelowSeaLevel)
            {
                if (planetGenerator == null)
                {
                    continue;
                }

                if (!planetGenerator.IsOceanAtDirection(GetDirectionForCell(cell)))
                {
                    continue;
                }
            }

            float cellVentStrength = ventStrength != null && cell < ventStrength.Length ? ventStrength[cell] : 0f;
            if (cellVentStrength <= 0f)
            {
                continue;
            }

            if (enableLayeredOcean && IsOceanCell(cell))
            {
                InjectVentProductToBottomLayer(ResourceType.H2S, cell, h2sPerTick * cellVentStrength * sourceScale);
                InjectVentProductToBottomLayer(ResourceType.H2, cell, h2PerTick * cellVentStrength * sourceScale);
                InjectVentProductToBottomLayer(ResourceType.CO2, cell, Mathf.Max(0f, ventCO2PerTick) * cellVentStrength * sourceScale);
                InjectVentProductToBottomLayer(ResourceType.DissolvedFe2Plus, cell, dissolvedFe2PlusPerTick * cellVentStrength * sourceScale);
            }
            else
            {
                Add(ResourceType.H2S, cell, h2sPerTick * cellVentStrength * sourceScale, AggregateCompatibilityCallsite.AtmosphereVents);
                Add(ResourceType.H2, cell, h2PerTick * cellVentStrength * sourceScale, AggregateCompatibilityCallsite.AtmosphereVents);
                Add(ResourceType.CO2, cell, Mathf.Max(0f, ventCO2PerTick) * cellVentStrength * sourceScale, AggregateCompatibilityCallsite.AtmosphereVents);
                Add(ResourceType.DissolvedFe2Plus, cell, dissolvedFe2PlusPerTick * cellVentStrength * sourceScale, AggregateCompatibilityCallsite.AtmosphereVents);
            }
            if (applyH2SCap)
            {
                h2s[cell] = Mathf.Min(h2s[cell], ventH2SMax);
            }

            if (applyH2Cap)
            {
                h2[cell] = Mathf.Min(h2[cell], ventH2Max);
            }
        }

        SyncLegacyOceanFromLayeredArrays();
        UpdateOceanChemistryDebugStats();
    }

    private void ApplyDissolvedFe2PlusOxidation(float dt)
    {
        if (!isInitialized || oceanMask == null || fe == null)
        {
            return;
        }

        float rate = Mathf.Max(0f, fe2PlusOxidationRatePerSecond);
        float o2PerFe2 = Mathf.Max(0f, o2ConsumptionPerFe2PlusOxidized);
        if (rate <= 0f || o2PerFe2 <= 0f)
        {
            return;
        }

        // Legacy single-layer path
        if (!enableLayeredOcean)
        {
            float[] dissolvedFe2PlusLegacy = GetOceanDissolvedArray(ResourceType.DissolvedFe2Plus);
            if (dissolvedFe2PlusLegacy == null || o2 == null)
            {
                return;
            }

            for (int cell = 0; cell < dissolvedFe2PlusLegacy.Length; cell++)
            {
                if (oceanMask[cell] == 0)
                {
                    continue;
                }

                float availableFe2 = dissolvedFe2PlusLegacy[cell];
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

                dissolvedFe2PlusLegacy[cell] = availableFe2 - oxidizedFe2;
                o2[cell] = Mathf.Max(0f, availableO2 - (oxidizedFe2 * o2PerFe2));
                fe[cell] += oxidizedFe2;
            }

            return;
        }

        // Layered-ocean path
        float[] dissolvedFe2PlusLayers = GetLayeredOceanArray(ResourceType.DissolvedFe2Plus);
        float[] o2Layers = GetLayeredOceanArray(ResourceType.O2);
        float[] dissolvedFe2PlusLegacyForSync = GetOceanDissolvedArray(ResourceType.DissolvedFe2Plus);

        if (dissolvedFe2PlusLayers == null || o2Layers == null)
        {
            return;
        }

        for (int cell = 0; cell < oceanMask.Length; cell++)
        {
            if (oceanMask[cell] == 0)
            {
                continue;
            }

            int activeCount = GetOceanActiveLayerCount(cell);
            if (activeCount <= 0)
            {
                continue;
            }

            // Simple first-pass rule:
            // oxidize Fe2+ in each active layer using O2 available in that same layer.
            // This keeps chemistry consistent with the layered state and avoids legacy overwrite issues.
            float oxidizedTotalThisCell = 0f;

            for (int layer = 0; layer < activeCount; layer++)
            {
                int idx = GetLayeredArrayIndex(cell, layer);
                if (idx < 0 || idx >= dissolvedFe2PlusLayers.Length || idx >= o2Layers.Length)
                {
                    continue;
                }

                float availableFe2 = dissolvedFe2PlusLayers[idx];
                float availableO2 = o2Layers[idx];
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

                dissolvedFe2PlusLayers[idx] = availableFe2 - oxidizedFe2;
                o2Layers[idx] = Mathf.Max(0f, availableO2 - (oxidizedFe2 * o2PerFe2));
                oxidizedTotalThisCell += oxidizedFe2;
            }

            if (oxidizedTotalThisCell > 0f)
            {
                fe[cell] += oxidizedTotalThisCell;
            }

            // Keep legacy compatibility arrays in sync for systems that still read them.
            if (dissolvedFe2PlusLegacyForSync != null && cell < dissolvedFe2PlusLegacyForSync.Length)
            {
                dissolvedFe2PlusLegacyForSync[cell] = GetEffectiveLayeredResource(ResourceType.DissolvedFe2Plus, cell);
            }

            if (o2 != null && cell < o2.Length)
            {
                o2[cell] = GetEffectiveLayeredResource(ResourceType.O2, cell);
            }
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
            debugFeTotal = 0f;
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
            oxidizedTotal += fe[cell];
            oceanCount++;
        }

        debugDissolvedFe2PlusTotal = dissolvedTotal;
        debugFeTotal = oxidizedTotal;
        debugDissolvedFe2PlusOceanMean = oceanCount > 0 ? dissolvedTotal / oceanCount : 0f;

        if (initialDissolvedFe2PlusTotal <= 0f)
        {
            initialDissolvedFe2PlusTotal = Mathf.Max(0f, dissolvedTotal);
        }

        debugDissolvedFe2PlusRemainingFraction = initialDissolvedFe2PlusTotal > 0f
            ? Mathf.Clamp01(dissolvedTotal / initialDissolvedFe2PlusTotal)
            : 0f;
    }

    private void InitializeOceanVisuals()
    {
        if (planetGenerator == null || planetGenerator.OceanRenderer == null)
            return;

        oceanMaterialInstance = planetGenerator.OceanRenderer.material;
        currentOceanColor = GetTargetOceanColor();
        ApplyOceanColor(currentOceanColor);
    }

    private Color GetTargetOceanColor()
    {
        float fe2Remaining = Mathf.Clamp01(debugDissolvedFe2PlusRemainingFraction);
        return Color.Lerp(oxygenatedOceanColor, ironRichOceanColor, fe2Remaining);
    }

    private void UpdateOceanVisuals()
    {
        if (!updateOceanColorFromDissolvedFe2Plus || oceanMaterialInstance == null)
            return;

        Color targetColor = GetTargetOceanColor();
        float t = 1f - Mathf.Exp(-oceanColorLerpSpeed * Time.deltaTime);
        currentOceanColor = Color.Lerp(currentOceanColor, targetColor, t);

        ApplyOceanColor(currentOceanColor);
    }

    private void ApplyOceanColor(Color color)
    {
        if (oceanMaterialInstance == null)
            return;

        if (oceanMaterialInstance.HasProperty(BaseColorId))
            oceanMaterialInstance.SetColor(BaseColorId, color);

        if (oceanMaterialInstance.HasProperty(ColorId))
            oceanMaterialInstance.SetColor(ColorId, color);
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

    private int GetReferenceCellCountForInventory()
    {
        int referenceResolution = Mathf.Max(1, inventoryReferenceResolution);
        return Mathf.Max(1, PlanetGridIndexing.GetCellCount(referenceResolution));
    }

    private int GetReferenceCellCountForVents()
    {
        int referenceResolution = Mathf.Max(1, ventReferenceResolution);
        return Mathf.Max(1, PlanetGridIndexing.GetCellCount(referenceResolution));
    }

    private int ResolveResolutionIndependentVentTarget(int totalCellCount, int thresholdVentCountAtCurrentResolution, out float desiredVentCount)
    {
        desiredVentCount = 0f;
        if (totalCellCount <= 0 || thresholdVentCountAtCurrentResolution <= 0)
        {
            return 0;
        }

        float currentFraction = Mathf.Clamp01(thresholdVentCountAtCurrentResolution / (float)totalCellCount);
        float referenceRadius = Mathf.Max(0.0001f, ventReferencePlanetRadius);
        float currentRadius = planetGenerator != null ? Mathf.Max(0.0001f, planetGenerator.radius) : referenceRadius;
        float areaScale = (currentRadius * currentRadius) / (referenceRadius * referenceRadius);
        desiredVentCount = Mathf.Max(0f, currentFraction * GetReferenceCellCountForVents() * areaScale);
        return Mathf.Clamp(Mathf.RoundToInt(desiredVentCount), 0, totalCellCount);
    }

    private static float ComputeVentSourceStrengthNormalization(float desiredVentCount, int selectedVentCount)
    {
        if (desiredVentCount <= 0f || selectedVentCount <= 0)
        {
            return 1f;
        }

        return Mathf.Max(1f, desiredVentCount / selectedVentCount);
    }

    // Inventory normalization bridge:
    // - inspector baseline fields now represent legacy per-cell values at a reference resolution/radius
    // - initialization converts those into resolution-independent total inventories
    // - resulting per-cell starting values scale with tile area (fewer tiles => more per tile)
    private float ComputeDomainPerCellInventoryScale(int totalCellCount, int domainCellCount)
    {
        if (totalCellCount <= 0 || domainCellCount <= 0)
        {
            return 0f;
        }

        float referenceRadius = Mathf.Max(0.0001f, inventoryReferencePlanetRadius);
        float currentRadius = planetGenerator != null ? Mathf.Max(0.0001f, planetGenerator.radius) : referenceRadius;
        float areaScale = (currentRadius * currentRadius) / (referenceRadius * referenceRadius);
        float referenceDomainCellCount = GetReferenceCellCountForInventory() * (domainCellCount / (float)totalCellCount);
        float totalInventory = referenceDomainCellCount * areaScale;
        return totalInventory / domainCellCount;
    }

    private static int CountDomainCells(byte[] mask, bool nonZero)
    {
        if (mask == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            bool include = nonZero ? mask[i] != 0 : mask[i] == 0;
            if (include)
            {
                count++;
            }
        }

        return count;
    }

    private void LogInitializationInventoryDebug(int cellCount, int oceanCellCount)
    {
        float co2Total = SumArray(co2);
        float o2Total = SumArray(o2);
        float ch4Total = SumArray(ch4);
        float s0Total = SumArray(s0);
        float pTotal = SumArray(p);
        float feTotal = SumArray(fe);
        float siTotal = SumArray(si);
        float caTotal = SumArray(ca);
        float dissolvedFe2Total = SumArray(GetOceanDissolvedArray(ResourceType.DissolvedFe2Plus));

        Debug.Log(
            $"[PlanetResourceMap] Init inventory totals (resolution-independent targets): " +
            $"cells={cellCount}, oceanCells={oceanCellCount}, refCells={GetReferenceCellCountForInventory()}, " +
            $"CO2={co2Total:F3}, O2={o2Total:F3}, CH4={ch4Total:F3}, S0={s0Total:F3}, " +
            $"P={pTotal:F3}, Fe={feTotal:F3}, Si={siTotal:F3}, Ca={caTotal:F3}, DissolvedFe2+={dissolvedFe2Total:F3}",
            this);
    }

    private static float SumArray(float[] values)
    {
        if (values == null)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < values.Length; i++)
        {
            sum += Mathf.Max(0f, values[i]);
        }

        return sum;
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
            case ResourceType.CH4: return ch4;
            case ResourceType.S0: return s0;
            case ResourceType.P: return p;
            case ResourceType.Fe: return fe;
            case ResourceType.Si: return si;
            case ResourceType.Ca: return ca;
            case ResourceType.DissolvedOrganicLeak: return dissolvedOrganicLeak;
            case ResourceType.ToxicProteolyticWaste: return toxicProteolyticWaste;
            default: return co2;
        }
    }

    public bool ShouldUseLayeredOceanForResource(ResourceType resourceType, int cell)
    {
        return enableLayeredOcean && IsOceanCell(cell) && GetLayeredOceanArray(resourceType) != null;
    }

    private float[] GetLayeredOceanArray(ResourceType resourceType)
    {
        return layeredOceanResources.TryGetValue(resourceType, out float[] array) ? array : null;
    }

    private int GetLayeredArrayIndex(int cell, int layerIndex)
    {
        return (cell * MaxOceanLayers) + layerIndex;
    }

    private bool IsLayerAccessValid(int cell, int layerIndex)
    {
        int active = GetOceanActiveLayerCount(cell);
        return active > 0 && layerIndex >= 0 && layerIndex < active && oceanLayerLightFactors != null;
    }

    private int DetermineActiveLayerCount(float localDepth)
    {
        float maxDepth = planetGenerator != null ? Mathf.Max(0.0001f, planetGenerator.maxOceanDepth) : 0.0001f;
        float depth01 = Mathf.Clamp01(localDepth / maxDepth);
        int count = 1;
        if (depth01 >= depth01ForLayer2) count = 2;
        if (depth01 >= depth01ForLayer3) count = 3;
        if (depth01 >= depth01ForLayer4) count = 4;
        if (depth01 >= depth01ForLayer5) count = 5;
        return Mathf.Clamp(count, 1, MaxOceanLayers);
    }

    private void UpdateLayerLightAndTemperatureProfiles()
    {
        if (oceanLayerLightFactors == null || oceanLayerTemperatureOffsets == null)
        {
            return;
        }

        float tempDrop = Mathf.Max(0f, layeredTempDropPerLayer);
        float ventTempGain = Mathf.Max(0f, layeredBottomVentTempGain);
        for (int cell = 0; cell < oceanMask.Length; cell++)
        {
            int active = GetOceanActiveLayerCount(cell);
            if (active <= 0)
            {
                continue;
            }

            float ventStrengthFactor = ventStrength != null && cell < ventStrength.Length ? Mathf.Max(0f, ventStrength[cell]) : 0f;
            for (int layer = 0; layer < MaxOceanLayers; layer++)
            {
                int idx = GetLayeredArrayIndex(cell, layer);
                if (layer >= active)
                {
                    oceanLayerLightFactors[idx] = 0f;
                    oceanLayerTemperatureOffsets[idx] = 0f;
                    continue;
                }

                // Layered light model (intended simplified profile):
                // - layer 0 (surface): strong light
                // - layer 1: moderate light (~40%-70% of top)
                // - layers 2+: near-dark
                oceanLayerLightFactors[idx] = GetDiscreteLayerLightFactor(layer);

                // Layered temperature offsets are an incremental visualization/fitness bridge:
                // - small depth cooling per layer
                // - vent-biased warming concentrated at bottom, with weaker warming one layer above bottom
                float ventLayerFactor = GetVentHeatingFactorForLayer(layer, active);
                oceanLayerTemperatureOffsets[idx] = (-tempDrop * layer) + (ventStrengthFactor * ventTempGain * ventLayerFactor);
            }
        }
    }

    private float GetDiscreteLayerLightFactor(int layerIndex)
    {
        if (layerIndex <= 0)
        {
            return Mathf.Clamp01(layeredTopLightFactor);
        }

        if (layerIndex == 1)
        {
            return Mathf.Clamp01(layeredSecondLayerLightFactor);
        }

        return Mathf.Clamp01(layeredDeepLightFactor);
    }

    private float GetSolarHeatingFactorForLayer(int layerIndex)
    {
        if (layerIndex <= 0)
        {
            return Mathf.Clamp01(layeredTopSolarHeatingFactor);
        }

        if (layerIndex == 1)
        {
            return Mathf.Clamp01(layeredSecondLayerSolarHeatingFactor);
        }

        return Mathf.Clamp01(layeredDeepSolarHeatingFactor);
    }

    private float GetVentHeatingFactorForLayer(int layerIndex, int activeLayerCount)
    {
        if (activeLayerCount <= 0)
        {
            return 0f;
        }

        int bottom = activeLayerCount - 1;
        if (layerIndex == bottom)
        {
            return Mathf.Clamp01(layeredBottomVentHeatingFactor);
        }

        if (layerIndex == bottom - 1)
        {
            return Mathf.Clamp01(layeredAboveBottomVentHeatingFactor);
        }

        return 0f;
    }

    private float GetLayeredSolarHeatingAggregateFactor(int cell)
    {
        int active = GetOceanActiveLayerCount(cell);
        if (active <= 0)
        {
            return 1f;
        }

        float sum = 0f;
        for (int layer = 0; layer < active; layer++)
        {
            sum += GetSolarHeatingFactorForLayer(layer);
        }

        return Mathf.Clamp01(sum / active);
    }

    private float GetLayeredVentHeatingAggregateFactor(int cell)
    {
        int active = GetOceanActiveLayerCount(cell);
        if (active <= 0)
        {
            return 1f;
        }

        float sum = 0f;
        for (int layer = 0; layer < active; layer++)
        {
            sum += GetVentHeatingFactorForLayer(layer, active);
        }

        return Mathf.Clamp01(sum / active);
    }

    private void SyncLayeredOceanFromLegacyArrays()
    {
        SyncResourceLegacyToLayered(ResourceType.O2);
        SyncResourceLegacyToLayered(ResourceType.CO2);
        SyncResourceLegacyToLayered(ResourceType.OrganicC);
        SyncResourceLegacyToLayered(ResourceType.H2);
        SyncResourceLegacyToLayered(ResourceType.H2S);
        SyncResourceLegacyToLayered(ResourceType.S0);
        SyncResourceLegacyToLayered(ResourceType.CH4);
        SyncResourceLegacyToLayered(ResourceType.DissolvedFe2Plus);
        SyncResourceLegacyToLayered(ResourceType.DissolvedOrganicLeak);
        SyncResourceLegacyToLayered(ResourceType.ToxicProteolyticWaste);
    }

    private void SyncResourceLegacyToLayered(ResourceType resourceType)
    {
        float[] legacy = GetArray(resourceType);
        float[] layered = GetLayeredOceanArray(resourceType);
        if (legacy == null || layered == null)
        {
            return;
        }

        for (int cell = 0; cell < legacy.Length; cell++)
        {
            int active = GetOceanActiveLayerCount(cell);
            if (active <= 0)
            {
                continue;
            }

            float value = Mathf.Max(0f, legacy[cell]);
            for (int layer = 0; layer < active; layer++)
            {
                layered[GetLayeredArrayIndex(cell, layer)] = value;
            }
        }
    }

    private void SyncLegacyOceanFromLayeredArrays()
    {
        // Compatibility sync is intentionally minimal while layered arrays remain authoritative.
        // Most gameplay reads should use Get()/GetResourceForCellLayer() which already resolve layered values.
        // Transitional compatibility bridge:
        // - CO2 legacy per-cell values mirror the ocean top layer so atmosphere exchange remains
        //   coupled to the surface interface only.
        // - DissolvedFe2+ legacy values mirror layered state for existing debug/legacy readers.
        SyncLegacyOceanResourceFromLayers(ResourceType.CO2);
        SyncLegacyOceanResourceFromLayers(ResourceType.DissolvedFe2Plus);
    }

    private void SyncLegacyOceanResourceFromLayers(ResourceType resourceType, int specificCell = -1)
    {
        float[] legacy = GetArray(resourceType);
        float[] layered = GetLayeredOceanArray(resourceType);
        if (legacy == null || layered == null)
        {
            return;
        }

        int start = specificCell >= 0 ? specificCell : 0;
        int end = specificCell >= 0 ? specificCell + 1 : legacy.Length;
        for (int cell = start; cell < end; cell++)
        {
            int active = GetOceanActiveLayerCount(cell);
            if (active <= 0)
            {
                continue;
            }

            legacy[cell] = GetEffectiveLayeredResource(resourceType, cell);
        }
    }

    private float GetEffectiveLayeredResource(ResourceType t, int cell)
    {
        int layerCount = GetOceanActiveLayerCount(cell);
        if (layerCount <= 0)
            return 0f;

        float[] arr = GetLayeredOceanArray(t);
        if (arr == null)
            return 0f;

        switch (t)
        {
            // Surface-driven
            case ResourceType.O2:
            case ResourceType.CO2:
                return GetLayerValue(arr, cell, 0);

            // Deep / vent-driven
            case ResourceType.H2:
            case ResourceType.H2S:
            case ResourceType.DissolvedFe2Plus:
                return GetLayerValue(arr, cell, layerCount - 1);

            // Mixed → average
            default:
                {
                    float sum = 0f;
                    for (int l = 0; l < layerCount; l++)
                        sum += GetLayerValue(arr, cell, l);

                    return sum / layerCount;
                }
        }
    }

    private float GetLayerValue(float[] arr, int cell, int layer)
    {
        if (arr == null)
            return 0f;

        int idx = GetLayeredArrayIndex(cell, layer);

        if (idx < 0 || idx >= arr.Length)
            return 0f;

        return arr[idx];
    }

    private float GetEffectiveLayeredLight(int cell)
    {
        int active = GetOceanActiveLayerCount(cell);
        if (active <= 0)
        {
            return 0f;
        }

        float sum = 0f;
        for (int layer = 0; layer < active; layer++)
        {
            sum += GetLayerLightFactor(cell, layer);
        }

        return Mathf.Clamp01(sum / active);
    }

    private void AddLayeredResourceDelta(ResourceType resourceType, int cell, float delta, AggregateCompatibilityCallsite callsite)
    {
        _ = callsite;
        float[] layered = GetLayeredOceanArray(resourceType);
        if (layered == null)
        {
            return;
        }

        int active = GetOceanActiveLayerCount(cell);
        if (active <= 0)
        {
            return;
        }

        // Compatibility bridge intentionally kept during incremental migration.
        // Most resources still spread aggregate Add(...) uniformly across active layers.
        // CO2 is an exception: aggregate CO2 writes in ocean cells are applied to layer 0
        // so direct atmosphere coupling remains surface-only; deeper influence comes from
        // explicit vertical diffusion/mixing.
        int startLayer = 0;
        int endLayerExclusive = active;
        float perLayerDelta = delta / active;
        if (resourceType == ResourceType.CO2)
        {
            endLayerExclusive = 1;
            perLayerDelta = delta;
        }

        for (int layer = startLayer; layer < endLayerExclusive; layer++)
        {
            int idx = GetLayeredArrayIndex(cell, layer);
            layered[idx] = Mathf.Max(0f, layered[idx] + perLayerDelta);
        }
    }

    private void InjectVentProductToBottomLayer(ResourceType resourceType, int cell, float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        float[] layered = GetLayeredOceanArray(resourceType);
        if (layered == null)
        {
            Add(resourceType, cell, amount, AggregateCompatibilityCallsite.AtmosphereVents);
            return;
        }

        int bottom = GetOceanBottomLayerIndex(cell);
        if (bottom < 0)
        {
            return;
        }

        int idx = GetLayeredArrayIndex(cell, bottom);
        layered[idx] = Mathf.Max(0f, layered[idx] + amount);
    }

    private void UpdateLayeredDebugSample()
    {
        if (!isInitialized || oceanMask == null || oceanMask.Length == 0)
        {
            debugLayeredActiveCount = 0;
            debugLayeredTopLight = 0f;
            debugLayeredBottomLight = 0f;
            debugLayeredEffectiveO2 = 0f;
            debugLayeredEffectiveOrganicC = 0f;
            debugLayeredTopO2Mean = 0f;
            debugLayeredBottomO2Mean = 0f;
            debugLayeredTopOrganicCMean = 0f;
            debugLayeredBottomOrganicCMean = 0f;
            debugLayeredTopH2SMean = 0f;
            debugLayeredBottomH2SMean = 0f;
            debugLayeredTopS0Mean = 0f;
            debugLayeredBottomS0Mean = 0f;
            return;
        }

        int cell = Mathf.Clamp(debugLayeredCellIndex, 0, oceanMask.Length - 1);
        debugLayeredActiveCount = GetOceanActiveLayerCount(cell);
        if (debugLayeredActiveCount > 0)
        {
            int top = GetOceanTopLayerIndex(cell);
            int bottom = GetOceanBottomLayerIndex(cell);
            debugLayeredTopLight = GetLayerLightFactor(cell, top);
            debugLayeredBottomLight = GetLayerLightFactor(cell, bottom);
            debugLayeredEffectiveO2 = GetEffectiveLayeredResource(ResourceType.O2, cell);
            debugLayeredEffectiveOrganicC = GetEffectiveLayeredResource(ResourceType.OrganicC, cell);
        }
        else
        {
            debugLayeredTopLight = 0f;
            debugLayeredBottomLight = 0f;
            debugLayeredEffectiveO2 = 0f;
            debugLayeredEffectiveOrganicC = 0f;
        }

        UpdateLayeredOceanTrendDebugStats();
    }

    // Lightweight observability for layered-ocean drift checks.
    // These means help detect whether chemistry is collapsing toward uniform values
    // (over-diffusive behavior / excessive compatibility smearing) versus maintaining
    // plausible top-vs-bottom structure.
    private void UpdateLayeredOceanTrendDebugStats()
    {
        float topO2Sum = 0f;
        float bottomO2Sum = 0f;
        float topOrganicCSum = 0f;
        float bottomOrganicCSum = 0f;
        float topH2SSum = 0f;
        float bottomH2SSum = 0f;
        float topS0Sum = 0f;
        float bottomS0Sum = 0f;
        int oceanCells = 0;

        for (int cell = 0; cell < oceanMask.Length; cell++)
        {
            if (!IsOceanCell(cell))
            {
                continue;
            }

            int top = GetOceanTopLayerIndex(cell);
            int bottom = GetOceanBottomLayerIndex(cell);
            if (top < 0 || bottom < 0)
            {
                continue;
            }

            topO2Sum += GetLayerResource(ResourceType.O2, cell, top);
            bottomO2Sum += GetLayerResource(ResourceType.O2, cell, bottom);
            topOrganicCSum += GetLayerResource(ResourceType.OrganicC, cell, top);
            bottomOrganicCSum += GetLayerResource(ResourceType.OrganicC, cell, bottom);
            topH2SSum += GetLayerResource(ResourceType.H2S, cell, top);
            bottomH2SSum += GetLayerResource(ResourceType.H2S, cell, bottom);
            topS0Sum += GetLayerResource(ResourceType.S0, cell, top);
            bottomS0Sum += GetLayerResource(ResourceType.S0, cell, bottom);
            oceanCells++;
        }

        if (oceanCells <= 0)
        {
            debugLayeredTopO2Mean = 0f;
            debugLayeredBottomO2Mean = 0f;
            debugLayeredTopOrganicCMean = 0f;
            debugLayeredBottomOrganicCMean = 0f;
            debugLayeredTopH2SMean = 0f;
            debugLayeredBottomH2SMean = 0f;
            debugLayeredTopS0Mean = 0f;
            debugLayeredBottomS0Mean = 0f;
            return;
        }

        float invOcean = 1f / oceanCells;
        debugLayeredTopO2Mean = topO2Sum * invOcean;
        debugLayeredBottomO2Mean = bottomO2Sum * invOcean;
        debugLayeredTopOrganicCMean = topOrganicCSum * invOcean;
        debugLayeredBottomOrganicCMean = bottomOrganicCSum * invOcean;
        debugLayeredTopH2SMean = topH2SSum * invOcean;
        debugLayeredBottomH2SMean = bottomH2SSum * invOcean;
        debugLayeredTopS0Mean = topS0Sum * invOcean;
        debugLayeredBottomS0Mean = bottomS0Sum * invOcean;
    }

    public bool IsCellValid(int cell)
    {
        return co2 != null && cell >= 0 && cell < co2.Length;
    }

    /// <summary>
    /// Simulation/resource grid index lookup. Uses PlanetResourceMap simulation resolution, not mesh resolution.
    /// </summary>
    public int GetCellIndexFromDirection(Vector3 worldPosOrDir)
    {
        if (!isInitialized || resolution <= 0)
        {
            return -1;
        }

        Vector3 dir = ResolveSurfaceDirection(worldPosOrDir);
        return PlanetGridIndexing.DirectionToCellIndex(dir, resolution);
    }

    public bool TryGetCellDirection(int cell, out Vector3 direction)
    {
        direction = Vector3.up;
        if (!isInitialized || cellDirections == null || cell < 0 || cell >= cellDirections.Length)
        {
            return false;
        }

        direction = cellDirections[cell];
        return true;
    }

    private Vector3 GetDirectionForCell(int cell)
    {
        if (TryGetCellDirection(cell, out Vector3 direction))
        {
            return direction;
        }

        return Vector3.up;
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

        simulationResolution = Mathf.Max(0, simulationResolution);
        inventoryReferenceResolution = Mathf.Max(1, inventoryReferenceResolution);
        inventoryReferencePlanetRadius = Mathf.Max(0.0001f, inventoryReferencePlanetRadius);
        ventReferenceResolution = Mathf.Max(1, ventReferenceResolution);
        ventReferencePlanetRadius = Mathf.Max(0.0001f, ventReferencePlanetRadius);
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
        scentAdjacentLayerCoupling = Mathf.Clamp01(scentAdjacentLayerCoupling);
        scentMaxPerCell = Mathf.Max(0f, scentMaxPerCell);
        scentUpdateInterval = Mathf.Max(0.01f, scentUpdateInterval);
        depth01ForLayer2 = Mathf.Clamp01(depth01ForLayer2);
        depth01ForLayer3 = Mathf.Clamp01(Mathf.Max(depth01ForLayer2, depth01ForLayer3));
        depth01ForLayer4 = Mathf.Clamp01(Mathf.Max(depth01ForLayer3, depth01ForLayer4));
        depth01ForLayer5 = Mathf.Clamp01(Mathf.Max(depth01ForLayer4, depth01ForLayer5));
        layeredLightAttenuation = Mathf.Max(0f, layeredLightAttenuation);
        layeredTopLightFactor = Mathf.Clamp01(layeredTopLightFactor);
        layeredSecondLayerLightFactor = Mathf.Clamp(layeredSecondLayerLightFactor, 0.4f, 0.7f);
        layeredDeepLightFactor = Mathf.Clamp(layeredDeepLightFactor, 0f, 0.2f);
        layeredVerticalMixRate = Mathf.Clamp01(layeredVerticalMixRate);
        layeredSurfaceOxygenationRate = Mathf.Clamp01(layeredSurfaceOxygenationRate);
        layeredMarineSnowRate = Mathf.Clamp01(layeredMarineSnowRate);
        layeredOrganicCLateralSpreadRate = Mathf.Clamp01(layeredOrganicCLateralSpreadRate);
        layeredOrganicCUpwardBleedRate = Mathf.Clamp01(layeredOrganicCUpwardBleedRate);
        layeredTempDropPerLayer = Mathf.Max(0f, layeredTempDropPerLayer);
        layeredBottomVentTempGain = Mathf.Max(0f, layeredBottomVentTempGain);
        layeredTopSolarHeatingFactor = Mathf.Clamp01(layeredTopSolarHeatingFactor);
        layeredSecondLayerSolarHeatingFactor = Mathf.Clamp01(layeredSecondLayerSolarHeatingFactor);
        layeredDeepSolarHeatingFactor = Mathf.Clamp(layeredDeepSolarHeatingFactor, 0f, 0.2f);
        layeredBottomVentHeatingFactor = Mathf.Clamp01(layeredBottomVentHeatingFactor);
        layeredAboveBottomVentHeatingFactor = Mathf.Clamp01(layeredAboveBottomVentHeatingFactor);
    }


    private string BuildDebugLabelText(int cellIndex)
    {
        return $"{debugViewType}: {Get(debugViewType, cellIndex, AggregateCompatibilityCallsite.DebugTelemetry):0.###}\nCO2: {Get(ResourceType.CO2, cellIndex, AggregateCompatibilityCallsite.DebugTelemetry):0.###}\nH2: {Get(ResourceType.H2, cellIndex, AggregateCompatibilityCallsite.DebugTelemetry):0.###}\nCH4: {Get(ResourceType.CH4, cellIndex, AggregateCompatibilityCallsite.DebugTelemetry):0.###}\nH2S: {Get(ResourceType.H2S, cellIndex, AggregateCompatibilityCallsite.DebugTelemetry):0.###}\nS0: {Get(ResourceType.S0, cellIndex, AggregateCompatibilityCallsite.DebugTelemetry):0.###}";
    }

    // Interpretation note:
    // - "AggregateAddCompatibility" = layered resource write that entered Add(...) and was distributed
    //   through the compatibility bridge rather than an explicit layer target.
    // - "Write/ReadFallbackToAggregate" = layer-aware API called without valid layer context, then fell back.
    // Use *_ByCallsite arrays to identify which subsystem should be migrated next.
    private void RecordAggregateAddCompatibilityCallsite(AggregateCompatibilityCallsite callsite, float deltaAbs)
    {
        if (!enableLayeredCompatibilityCallsiteTelemetry)
        {
            return;
        }

        int index = (int)callsite;
        if (index < 0 || index >= AggregateCompatibilityCallsiteCount)
        {
            index = (int)AggregateCompatibilityCallsite.UnknownLegacy;
        }

        debugLayeredAggregateAddCompatibilityCountByCallsite[index]++;
        debugLayeredAggregateAddCompatibilityAbsDeltaByCallsite[index] += Mathf.Abs(deltaAbs);
    }

    private void RecordAggregateGetCompatibilityCallsite(AggregateCompatibilityCallsite callsite)
    {
        if (!enableLayeredCompatibilityCallsiteTelemetry)
        {
            return;
        }

        int index = (int)callsite;
        if (index < 0 || index >= AggregateCompatibilityCallsiteCount)
        {
            index = (int)AggregateCompatibilityCallsite.UnknownLegacy;
        }

        debugLayeredAggregateGetCompatibilityCountByCallsite[index]++;
    }

    private void RecordWriteFallbackToAggregateCallsite(AggregateCompatibilityCallsite callsite)
    {
        if (!enableLayeredCompatibilityCallsiteTelemetry)
        {
            return;
        }

        int index = (int)callsite;
        if (index < 0 || index >= AggregateCompatibilityCallsiteCount)
        {
            index = (int)AggregateCompatibilityCallsite.UnknownLegacy;
        }

        debugLayeredWriteFallbackToAggregateCountByCallsite[index]++;
        debugLayeredWriteFallbackReasonCount[(int)LayeredWriteFallbackReason.Unknown]++;
    }


    public void RecordMetabolismLayeredWriteFallback(ResourceType resourceType, int metabolismType, LayeredWriteFallbackReason reason)
    {
        if (!enableLayeredCompatibilityCallsiteTelemetry)
        {
            return;
        }

        int resourceIndex = Mathf.Clamp((int)resourceType, 0, debugMetabolismWriteFallbackCountByResource.Length - 1);
        int metabolismIndex = Mathf.Clamp(metabolismType, 0, debugMetabolismWriteFallbackCountByMetabolismType.Length - 1);
        int reasonIndex = Mathf.Clamp((int)reason, 0, debugMetabolismWriteFallbackCountByReason.Length - 1);
        debugMetabolismWriteFallbackCountByResource[resourceIndex]++;
        debugMetabolismWriteFallbackCountByMetabolismType[metabolismIndex]++;
        debugMetabolismWriteFallbackCountByReason[reasonIndex]++;

        if (reason == LayeredWriteFallbackReason.ResourceNotLayeredInOcean)
        {
            debugMetabolismResourceNotLayeredInOceanCountByResource[resourceIndex]++;
            debugMetabolismResourceNotLayeredInOceanCountByMetabolismType[metabolismIndex]++;
        }
    }

    private void RecordReadFallbackToAggregateCallsite(AggregateCompatibilityCallsite callsite)
    {
        if (!enableLayeredCompatibilityCallsiteTelemetry)
        {
            return;
        }

        int index = (int)callsite;
        if (index < 0 || index >= AggregateCompatibilityCallsiteCount)
        {
            index = (int)AggregateCompatibilityCallsite.UnknownLegacy;
        }

        debugLayeredReadFallbackToAggregateCountByCallsite[index]++;
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
            case ResourceType.CH4: return Mathf.Max(0.0001f, baselineCH4 <= 0f ? 1f : baselineCH4);
            case ResourceType.S0: return Mathf.Max(0.0001f, baselineS0);
            case ResourceType.P: return Mathf.Max(0.0001f, phosphorusScale);
            case ResourceType.Fe: return Mathf.Max(0.0001f, initialDissolvedFe2PlusPerOceanCell);
            case ResourceType.DissolvedFe2Plus: return Mathf.Max(0.0001f, initialDissolvedFe2PlusPerOceanCell);
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
