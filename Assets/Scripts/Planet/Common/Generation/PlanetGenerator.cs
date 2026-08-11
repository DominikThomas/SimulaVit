using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum GeodesicSeaLevelControlMode
{
    ManualOffset,
    TargetAreaCoverage,
    OceanWorld
}

public enum GeodesicCoastType : byte
{
    None,
    ContinentalMargin,
    ContinentalFragmentOrPlateau,
    OceanicIsland,
    MixedMargin
}

public enum GeodesicBathymetryRegion : byte
{
    Land,
    ContinentalShelf,
    OceanicIslandMargin,
    OceanicBankOrPlateau,
    Ridge,
    Seamount,
    Basin
}

public enum GeodesicShelfProfileType : byte
{
    None,
    Continental,
    FragmentOrPlateau,
    Mixed,
    OceanicIsland
}

public class PlanetGenerator : MonoBehaviour, IPlanetSurfaceGeometry, ISerializationCallbackReceiver
{
    /// <summary>
    /// PlanetGenerator is the authoritative source for terrain surface queries.
    /// Do not duplicate terrain noise/surface logic in movement or simulation systems.
    /// </summary>
    public struct SurfaceQueryParameters
    {
        public float Radius;
        public float NoiseMagnitude;
        public float NoiseRoughness;
        public Vector3 NoiseOffset;
        public int NumLayers;
        public float Persistence;
        public float OceanThreshold;
        public float OceanDepth;
        public bool OceanEnabled;
    }

    [SerializeField] private ReplicatorManager replicatorManager;
    public ReplicatorManager ReplicatorManager => replicatorManager;
    [Header("Generation Mode")]
    public PlanetGenerationMode generationMode = PlanetGenerationMode.LegacyCubeSphere;
    [Range(0, GeodesicGridTopology.MaxSupportedSubdivision)]
    public int geodesicSubdivisionLevel = 4;
    [Range(0, GeodesicGridTopology.MaxSupportedSubdivision)]
    public int geodesicSimulationSubdivisionLevel = 4;
    [Range(0, GeodesicGridTopology.MaxSupportedSubdivision)]
    [Tooltip("Visual geodesic terrain subdivision. Each additional level approximately quadruples triangle count; level 8 can be expensive to generate.")]
    public int geodesicRenderSubdivisionLevel = 7;
    [Range(0, GeodesicGridTopology.MaxSupportedSubdivision)]
    [Tooltip("Independent geodesic MeshCollider subdivision used only for interaction raycasts. Cell lookup still resolves from normalized hit direction.")]
    public int geodesicColliderSubdivisionLevel = 6;
    [Tooltip("Warn once per geodesic generation when an estimated render/collider triangle count exceeds this threshold.")]
    public int geodesicDiagnosticTriangleWarningThreshold = 350000;
    private long geodesicSurfaceRadiusQueryCount;
    private long geodesicDirectionToCellQueryCount;
    private long geodesicDirectionCandidateCellsInspected;
    private long geodesicTerrainNoiseEvaluationCount;
    private long geodesicSimulationCellTerrainEvaluationCount;
    private long geodesicRenderVertexTerrainEvaluationCount;
    private long geodesicDiagnosticOnlyTerrainEvaluationCount;
    private long geodesicBathymetryInterpolationCount;
    private long geodesicDirectionMappingCacheHits;
    private long geodesicDirectionMappingCacheMisses;
    private double geodesicSurfaceRadiusQueryMilliseconds;
    private Dictionary<GeodesicDebugDirectionKey, float> geodesicDebugSurfaceRadiusCache;
    private GeodesicRenderTerrainData geodesicCurrentRenderTerrainData;
    private Vector3[] geodesicVisibleSeafloorPositionByCell;
    private Vector3[] geodesicVisibleSeafloorNormalByCell;
    private float maximumGeneratedOpaqueSurfaceRadius;
    private double geodesicLastShorelineDistanceMilliseconds;
    private double geodesicLastCoreGenerationMilliseconds;
    private double geodesicLastFullSynchronousGenerationMilliseconds;
    private double geodesicLastOceanicReliefMilliseconds;
    private double geodesicLastLandComponentMilliseconds;
    private double geodesicLastCoastTypeMilliseconds;
    private double geodesicLastShelfVariationMilliseconds;
    private double geodesicLastFinalBathymetryMilliseconds;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [SerializeField] private bool validateOptimizedGeodesicShoreDistances;
#endif
    private readonly struct GeodesicDebugDirectionKey
    {
        private readonly int x; private readonly int y; private readonly int z;
        private const float Scale = 1000000f;
        private GeodesicDebugDirectionKey(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
        public static GeodesicDebugDirectionKey From(Vector3 direction)
        {
            Vector3 unit = direction.sqrMagnitude > 1e-10f ? direction.normalized : Vector3.up;
            return new GeodesicDebugDirectionKey(Mathf.RoundToInt(unit.x * Scale), Mathf.RoundToInt(unit.y * Scale), Mathf.RoundToInt(unit.z * Scale));
        }
    }

    private struct GeodesicRenderTerrainData
    {
        public float[] RawRadii;
        public float[] SurfaceRadii;
        public float[] Heights;
        public float[] NormalizedHeights;
        public float[] MountainMasks;
    }
    public bool showGeodesicCellOutlines = true;
    public bool highlightGeodesicPentagons = true;
    public bool showGeodesicCellCentres;
    public bool showSelectedGeodesicCell = true;
    public bool highlightGeodesicOceanCells;
    public bool highlightGeodesicCoastlineCells = true;

    [Header("Ocean Appearance")]
    [Tooltip("Authoritative visual-only ocean appearance consumed by both legacy cube-sphere and geodesic ocean renderers.")]
    [SerializeField]
    private OceanAppearanceSettings oceanAppearance = new OceanAppearanceSettings();
    [FormerlySerializedAs("oxygenatedWaterColor")]
    [SerializeField, HideInInspector] private Color deprecatedLegacyOxygenatedWaterColor = default;

    public OceanAppearanceSettings OceanAppearance => oceanAppearance;

    [Header("Geodesic Bathymetry / Continental Shelf")]
    [Tooltip("Enable authoritative geodesic bathymetry after raw-terrain ocean classification. Legacy cube-sphere bathymetry is unaffected.")]
    public bool enableGeodesicBathymetry = true;
    [Tooltip("Mean continental shelf angular width in degrees; local coast ownership and shelf variation modulate this value.")]
    [Min(0.01f)] public float geodesicShelfWidthDegrees = 12f;
    [Tooltip("Mean continental shelf-break depth in planet-radius units; local smooth variation can modulate it.")]
    [Min(0f)] public float geodesicShelfDepth = 0.06f;
    [Tooltip("Maximum generated geodesic bathymetric basin-lowering depth below sea level in planet-radius units. In Ocean World mode this limits optional generated basin lowering where applicable, but does not clamp the total water column or lift/flatten the seafloor; Ocean World Minimum Cover Depth controls the global water shell.")]
    [Min(0f)] public float geodesicMaximumOceanDepth = 0.22f;
    [Tooltip("Mean exponent controlling continental slope descent beyond the shelf edge.")]
    [Min(0.01f)] public float geodesicContinentalSlopeExponent = 1.35f;
    [Header("Geodesic Bathymetry / Oceanic Island Margins")]
    [Tooltip("Enable narrow, steep-sided oceanic island margin profiles. Keep disabled with zero relief/variation for exact legacy geodesic bathymetry comparisons.")] public bool enableGeodesicOceanicIslandMargins = false;
    [Tooltip("Narrow mean shelf width for oceanic volcanic islands, in degrees.")] [Min(0.01f)] public float geodesicOceanicIslandShelfWidthDegrees = 2.5f;
    [Tooltip("Shallow-break depth for narrow oceanic-island margins.")] [Min(0f)] public float geodesicOceanicIslandShelfDepth = 0.025f;
    [Tooltip("Higher values make oceanic island margins descend more steeply immediately offshore.")] [Min(0.01f)] public float geodesicOceanicIslandSlopeExponent = 0.55f;
    [Range(0f,1f)] public float geodesicOceanicIslandShelfVariationStrength = 0.25f;
    [Tooltip("Conservative local blend for MixedMargin coastlines. ContinentalMargin and FragmentOrPlateau remain continental; only OceanicIsland receives the full island profile.")] [Range(0f,1f)] public float geodesicMixedMarginOceanicBlendStrength = 0.15f;
    [Header("Geodesic Bathymetry / Continental Shelf Variation")]
    [Tooltip("Minimum continental shelf width multiplier used at full width-variation strength. Values near zero allow effectively absent continental shelves.")] [Min(0f)] public float geodesicContinentalShelfMinWidthMultiplier = 0.75f;
    [Tooltip("Maximum continental shelf width multiplier used at full width-variation strength.")] [Min(0f)] public float geodesicContinentalShelfMaxWidthMultiplier = 1.25f;
    [Tooltip("Blends shelf width multipliers from exactly 1 at strength 0 to the configured min/max range at strength 1.")] [Range(0f,1f)] public float geodesicContinentalShelfWidthVariationStrength = 0f;
    [Tooltip("Geographic wavelength control for width variation; changes patch size, not amplitude.")] [Min(0.001f)] public float geodesicContinentalShelfWidthVariationScale = 0.55f;
    [Tooltip("Minimum continental shelf-break depth multiplier used at full depth-variation strength.")] [Min(0f)] public float geodesicContinentalShelfMinDepthMultiplier = 0.75f;
    [Tooltip("Maximum continental shelf-break depth multiplier used at full depth-variation strength.")] [Min(0f)] public float geodesicContinentalShelfMaxDepthMultiplier = 1.25f;
    [Tooltip("Blends shelf-break depth multipliers from exactly 1 at strength 0 to the configured min/max range at strength 1.")] [Range(0f,1f)] public float geodesicContinentalShelfDepthVariationStrength = 0f;
    [Tooltip("Geographic wavelength control for depth variation; changes patch size, not amplitude.")] [Min(0.001f)] public float geodesicContinentalShelfDepthVariationScale = 0.55f;
    [Range(0f,1f)] public float geodesicContinentalSlopeVariationStrength = 0f;
    [Tooltip("Legacy/shared variation scale retained for slope variation and older scenes; width/depth now have independent scales.")] [Min(0.001f)] public float geodesicShelfVariationScale = 0.55f;
    [Tooltip("Positive values make broad shelves tend to be deeper; negative values make broad shelves tend to be shallower. Zero keeps width and depth independent.")] [Range(-1f,1f)] public float geodesicShelfWidthDepthCorrelation = 0f;
    [Header("Geodesic Bathymetry / Oceanic Ridges and Plateaus")]
    [Min(0f)] public float geodesicOceanicRidgeStrength = 0f;
    [Min(0.001f)] public float geodesicOceanicRidgeScale = 1.8f;
    [Range(0f,1f)] public float geodesicOceanicRidgeThreshold = 0.72f;
    [Min(0f)] public float geodesicOceanicPlateauStrength = 0f;
    [Min(0.001f)] public float geodesicOceanicPlateauScale = 0.65f;
    [Header("Geodesic Bathymetry / Seamounts and Island Chains")]
    public bool enableGeodesicSeamounts = false;
    [Range(0f,2f)] public float geodesicSeamountDensity = 0.18f;
    [Min(0f)] public float geodesicSeamountAmplitude = 0.12f;
    [Min(0.01f)] public float geodesicSeamountRadiusDegrees = 1.5f;
    [Range(0f,1f)] public float geodesicSeamountChainProbability = 0.35f;
    [Range(1,12)] public int geodesicSeamountChainLength = 4;
    [Min(0.01f)] public float geodesicSeamountChainSpacingDegrees = 3f;
    [Range(-1f,1f)] public float geodesicSeamountEmergenceBias = -0.35f;
    [Header("Geodesic Bathymetry / Basin and Diagnostics")]
    [Tooltip("Low-frequency deterministic basin noise scale sampled by direction.")]
    [Min(0.001f)] public float geodesicBasinNoiseScale = 1.35f;
    [Tooltip("How strongly deterministic basin noise modulates deep geodesic basin depth.")]
    [Range(0f, 1f)] public float geodesicBasinNoiseStrength = 0.25f;
    [Tooltip("Angular shoreline preservation width in degrees where raw coastal terrain is kept dominant.")]
    [Min(0f)] public float geodesicShorelinePreservationDegrees = 2.5f;
    [Range(0, 8)] public int geodesicBathymetrySmoothPasses = 2;
    [Range(0f, 1f)] public float geodesicBathymetrySmoothStrength = 0.35f;
    [Range(0f, 1f)] public float geodesicBathymetryStrength = 1f;

    [Header("Geodesic Ocean Visual")]
    [Range(0, GeodesicGridTopology.MaxSupportedSubdivision)]
    [Tooltip("Smooth visual-only geodesic ocean subdivision. Uses cached render-only unit geometry and has no collider.")] public int geodesicOceanRenderSubdivisionLevel = 5;
    [FormerlySerializedAs("geodesicOceanColour")]
    [SerializeField, HideInInspector] private Color deprecatedGeodesicOceanColour = new Color(0.02f, 0.28f, 0.55f, 0.42f);
    [FormerlySerializedAs("geodesicOceanShallowTint")]
    [SerializeField, HideInInspector] private Color deprecatedGeodesicOceanShallowTint = new Color(0.10f, 0.55f, 0.75f, 0.42f);
    [FormerlySerializedAs("geodesicOceanOpacity")]
    [SerializeField, HideInInspector] private float deprecatedGeodesicOceanOpacity = 0.42f;
    [FormerlySerializedAs("geodesicOceanSmoothness")]
    [SerializeField, HideInInspector] private float deprecatedGeodesicOceanSmoothness = 0.82f;

    [Header("Geodesic Terrain")]
    [Tooltip("Visual nightside floor for the geodesic terrain shader. This is not thermal energy and must not be used as temperature input.")]
    [Range(0f, 1f)] public float geodesicSurfaceAmbientStrength = 0.08f;
    [Tooltip("Multiplier for URP main-light diffuse illumination of geodesic terrain.")]
    [Range(0f, 2f)] public float geodesicSurfaceDiffuseStrength = 1f;
    [Tooltip("Displace only the welded geodesic prototype mesh radially with deterministic direction-sampled terrain. Legacy cube-sphere generation is unaffected.")]
    public bool enableGeodesicTerrainDisplacement = true;
    [Tooltip("When enabled, a deterministic Terrain-domain seed derived from the master planet seed is used.")]
    [FormerlySerializedAs("usePlanetSeedForGeodesicTerrainSeed")]
    public bool usePlanetSeedForTerrain = true;
    [FormerlySerializedAs("geodesicTerrainSeed")]
    public int customTerrainSeed = 67890;
    [FormerlySerializedAs("geodesicBaseRadius")]
    [SerializeField, HideInInspector] private float deprecatedGeodesicBaseRadius = 1f;
    [Min(0f)] public float geodesicContinentAmplitude = 0.09f;
    [Min(0.001f)] public float geodesicContinentNoiseScale = 0.75f;
    [Range(-1f, 1f)] public float geodesicContinentBias = -0.05f;
    [Min(0f)] public float geodesicMountainAmplitude = 0.16f;
    [Min(0.001f)] public float geodesicMountainNoiseScale = 4.75f;
    [Range(0f, 1f)] public float geodesicMountainCoverageThreshold = 0.58f;
    [Min(0.001f)] public float geodesicMountainMaskSoftness = 0.18f;
    [Min(0.01f)] public float geodesicRidgeSharpness = 1.65f;
    [Min(0.001f)] public float geodesicDomainWarpScale = 1.35f;
    [Min(0f)] public float geodesicDomainWarpStrength = 0.28f;
    [Min(0.001f)] public float geodesicFineDetailScale = 18f;
    [Min(0f)] public float geodesicFineDetailAmplitude = 0.018f;
    [Range(1, 8)] public int geodesicTerrainOctaves = 5;
    [Range(0f, 1f)] public float geodesicTerrainPersistence = 0.48f;
    [Min(1f)] public float geodesicTerrainLacunarity = 2f;
    [Range(0.25f, 4f)] public float geodesicTerrainHeightContrast = 1.15f;
    [Range(-1f, 0f)] public float geodesicMinimumTerrainOffset = -0.09f;
    [Range(0f, 1f)] public float geodesicMaximumTerrainOffset = 0.22f;
    [Header("Geodesic Ocean Classification")]
    [Tooltip("Select how geodesic ocean cells resolve their single authoritative sea-level radius. Manual Offset preserves the original default behavior.")]
    public GeodesicSeaLevelControlMode geodesicSeaLevelControlMode = GeodesicSeaLevelControlMode.ManualOffset;
    [Tooltip("Manual geodesic sea-level offset relative to PlanetGenerator.radius. Zero places sea level at BasePlanetRadius; positive values raise sea level and increase ocean coverage; negative values lower sea level and decrease ocean coverage. Large values can intentionally create all-ocean or no-ocean test planets. In Target Area Coverage mode this field is inactive because the offset is calculated automatically.")]
    [FormerlySerializedAs("geodesicSeaLevelPreviewOffset")]
    public float geodesicSeaLevelOffset = 0f;
    [Tooltip("Target approximate physical spherical surface-area coverage for geodesic oceans. Used only when Geodesic Sea Level Control Mode is Target Area Coverage; ignored in Manual Offset mode.")]
    [Range(0f, 100f)] public float geodesicTargetOceanCoveragePercent = 45f;
    [Tooltip("Ocean World minimum water-column depth above the highest solid-surface point in planet-radius units. This is not mean depth or maximum local depth; basins may be much deeper. Raising it raises the global water-surface radius without changing solid terrain. geodesicMaximumOceanDepth only limits generated bathymetric basin lowering where appropriate and does not clamp Ocean World total water-column depth.")]
    [Min(0f)] public float geodesicOceanWorldMinimumDepth = 0.06f;
    [SerializeField, Tooltip("Read-only runtime diagnostic: resolved geodesic sea-level radius from the last generation.")] private float resolvedGeodesicSeaLevelRadius;
    [SerializeField, Tooltip("Read-only runtime diagnostic: resolved geodesic sea-level offset from BasePlanetRadius from the last generation.")] private float resolvedGeodesicSeaLevelOffset;
    [SerializeField, Tooltip("Read-only runtime diagnostic: achieved geodesic ocean coverage by cell count from the last generation.")] private float achievedGeodesicOceanCellCoveragePercent;
    [SerializeField, Tooltip("Read-only runtime diagnostic: achieved geodesic ocean coverage by physical spherical cell area from the last generation.")] private float achievedGeodesicOceanAreaCoveragePercent;
    [SerializeField, Tooltip("Read-only runtime diagnostic: geodesic ocean cell count from the last generation.")] private int geodesicOceanCellCount;
    [SerializeField, Tooltip("Read-only runtime diagnostic: geodesic coastline ocean cell count from the last generation.")] private int geodesicCoastlineOceanCellCount;
    [SerializeField, Tooltip("Read-only runtime diagnostic: minimum local geodesic ocean depth from the last generation.")] private float geodesicMinimumLocalOceanDepth;
    [SerializeField, Tooltip("Read-only runtime diagnostic: area-weighted mean local geodesic ocean depth from the last generation.")] private float geodesicAreaWeightedMeanLocalOceanDepth;
    [SerializeField, Tooltip("Read-only runtime diagnostic: maximum local geodesic ocean depth from the last generation.")] private float geodesicMaximumLocalOceanDepth;
    private const float GeodesicNoShoreDistance = -1f;
    private double geodesicLastOceanWorldSurfaceResolveMilliseconds;
    private float geodesicOceanWorldMaxSimulationSolidRadius;
    private float geodesicOceanWorldMaxRenderSolidRadius;
    private float geodesicOceanWorldMaxColliderSolidRadius;
    private float geodesicOceanWorldMinSimulationCoverDepth;
    private float geodesicOceanWorldMinRenderCoverDepth;
    private float geodesicOceanWorldMinColliderCoverDepth;
    private bool geodesicLastShorelineCalculationSkipped;
    [Tooltip("Blend vertex colours with normalized terrain height while preserving directional visual noise.")]
    public bool geodesicColoursUseTerrainHeight = true;
    [Tooltip("Small radial lift for debug outlines so they track displaced terrain without z-fighting.")]
    [Range(0f, 0.05f)] public float geodesicOutlineRadialOffset = 0.003f;

    [Header("Geodesic Surface Visuals")]
    [Tooltip("Apply deterministic vertex colours to the welded geodesic prototype mesh. Legacy cube-sphere generation is unaffected.")]
    public bool enableGeodesicProceduralSurfaceColours = true;
    [Tooltip("When enabled, a deterministic SurfaceVisuals-domain seed derived from the master planet seed is used.")]
    [FormerlySerializedAs("usePlanetSeedForGeodesicVisualSeed")]
    public bool usePlanetSeedForVisuals = true;
    [FormerlySerializedAs("geodesicVisualSeed")]
    public int customVisualSeed = 12345;
    [Min(0.001f)] public float geodesicVisualNoiseScale = 2.25f;
    [Range(1, 8)] public int geodesicVisualOctaves = 4;
    [Range(0f, 1f)] public float geodesicVisualPersistence = 0.5f;
    [Min(1f)] public float geodesicVisualLacunarity = 2f;
    public Color geodesicLowColour = new Color(0.16f, 0.20f, 0.15f, 1f);
    public Color geodesicMiddleColour = new Color(0.42f, 0.38f, 0.25f, 1f);
    public Color geodesicHighColour = new Color(0.72f, 0.70f, 0.58f, 1f);
    [Range(0.25f, 4f)] public float geodesicVisualContrast = 1.2f;

    [Header("Visual Mesh")]
    [Tooltip("Visual/mesh resolution used for terrain/ocean/atmosphere geometry generation. Simulation/resource resolution is configured on PlanetResourceMap.")]
    [Range(3, 240)]
    public int resolution = 10;
    public float radius = 1f;
    public Material planetMaterial;

    [Header("Surface Rock Shading")]
    public Color darkRockColor = new Color(0.13f, 0.14f, 0.16f, 1f);
    public Color midRockColor = new Color(0.34f, 0.37f, 0.41f, 1f);
    public Color lightRockColor = new Color(0.60f, 0.62f, 0.65f, 1f);
    [Min(0.01f)] public float largeNoiseScale = 1.6f;
    [Min(0.01f)] public float mediumNoiseScale = 4.8f;
    [Min(0.01f)] public float detailNoiseScale = 15f;
    [Range(0.25f, 4f)] public float contrast = 1.35f;
    [Range(0f, 1f)] public float crackDarkening = 0.32f;

    [Header("Terrain Generation")]
    public float noiseMagnitude = 0.1f;
    public float noiseRoughness = 1.0f;
    public int numLayers = 4;
    public float persistence = 0.5f;
    public Vector3 noiseOffset = Vector3.one;

    [Header("Ocean")]
    [Tooltip("Shared ocean enable switch. Affects both legacy cube-sphere oceans and geodesic ocean classification/rendering.")]
    public bool enableOcean = true;
    [Tooltip("Legacy cube-sphere ocean coverage target. Geodesic ocean coverage is controlled by Geodesic Ocean Classification settings.")]
    [Range(0f, 100f)] public float oceanCoveragePercent = 45f;
    [Tooltip("Legacy cube-sphere ocean depth control. Geodesic bathymetry depth is controlled by Geodesic Maximum Ocean Depth.")]
    [Range(0f, 1f)] public float oceanDepth = 0.35f;
    public Material oceanMaterial;

    [Header("Ocean Bathymetry")]
    [Tooltip("Enable shoreline-distance bathymetry shaping for ocean-floor visuals and depth data.")]
    public bool enableBathymetry = true;
    [Tooltip("Approximate continental shelf width in cell-to-cell graph steps.")]
    [Min(1f)] public float shelfDistance = 8f;
    [Tooltip("Target depth at the end of the continental shelf (planet radius units).")]
    [Min(0f)] public float shelfDepth = 0.06f;
    [Tooltip("How aggressively depth ramps toward deep basin after shelf edge.")]
    [Min(0f)] public float slopeStrength = 1.15f;
    [Tooltip("Maximum local ocean depth below sea level (planet radius units).")]
    [Min(0f)] public float maxOceanDepth = 0.22f;
    [Tooltip("Low-frequency basin-shape noise scale sampled on unit sphere.")]
    [Min(0.001f)] public float basinNoiseScale = 1.35f;
    [Tooltip("How strongly basin noise modulates offshore depth.")]
    [Range(0f, 1f)] public float basinNoiseStrength = 0.25f;
    [Tooltip("Optional deterministic offset to decorrelate basin noise from terrain noise.")]
    public Vector3 basinNoiseOffset = new Vector3(23.17f, -11.03f, 7.41f);
    [Tooltip("How many shoreline-distance smoothing passes to apply before depth shaping.")]
    [Range(0, 8)] public int bathymetrySmoothPasses = 2;
    [Tooltip("Per-pass smoothing blend for shoreline-distance field.")]
    [Range(0f, 1f)] public float bathymetrySmoothStrength = 0.45f;
    [Tooltip("Keep coast geometry mostly unchanged within this many cells from shore.")]
    [Min(0f)] public float shorelinePreservationDistance = 3f;
    [Tooltip("Global strength for visible offshore bathymetry deformation.")]
    [Range(0f, 1f)] public float bathymetryVisualStrength = 0.35f;

    [Header("Atmosphere")]
    public bool enableAtmosphere = true;
    [Tooltip("Atmosphere shell radius multiplier relative to planet radius.")]
    [Range(1.001f, 1.2f)] public float atmosphereRadiusMultiplier = 1.04f;
    public Material atmosphereMaterial;

    [Header("Randomization")]
    public bool randomizeOnStart = false;
    public bool useRandomSeed = true;
    public int randomSeed = 12345;
    public Vector2 noiseMagnitudeRange = new Vector2(0.05f, 0.2f);
    public Vector2 noiseRoughnessRange = new Vector2(1.5f, 6f);
    public Vector2 oceanCoverageRange = new Vector2(25f, 60f);

    [Header("Biology Unlocks")]
    [Tooltip("Seconds after simulation start when Photosynthesis mutation becomes possible.")]
    public float photosynthesisUnlockSeconds = 30f;
    [Tooltip("Seconds after simulation start when Saprotrophy mutation becomes possible.")]
    public float saprotrophyUnlockSeconds = 60f;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;
    private Material runtimePlanetMaterial;
    private Material runtimeOceanMaterial;
    private Material runtimeGeodesicSurfaceMaterial;
    private Material runtimeGeodesicOceanMaterial;
    private GameObject geodesicOceanObject;
    private MeshFilter geodesicOceanMeshFilter;
    private MeshRenderer geodesicOceanMeshRenderer;
    private Mesh geodesicOceanMesh;
    private Texture2D runtimeSurfaceTexture;

    private MeshFilter oceanMeshFilter;
    private MeshRenderer oceanMeshRenderer;
    private Mesh oceanMesh;

    private MeshFilter atmosphereMeshFilter;
    private MeshRenderer atmosphereMeshRenderer;
    private Mesh atmosphereMesh;

    private float oceanNoiseThreshold;
    private float[] generatedSurfaceRadiusByCell;
    private float[] localOceanDepthByCell;
    private float[] oceanDistanceToShoreByCell;
    private byte[] oceanMaskByCell;
    private float[] geodesicTerrainHeightByCell;
    private float[] geodesicNormalizedTerrainByCell;
    private float[] geodesicRawTerrainRadius;
    private float[] geodesicSeafloorRadius;
    private float[] geodesicBaseWaterDepth;
    private float[] geodesicWaterDepth;
    private float[] geodesicDistanceToShore;
    private float[] geodesicBasinNoiseContribution;
    private float[] geodesicContinentalInfluenceByCell;
    private float[] geodesicOceanicRidgeReliefByCell;
    private float[] geodesicOceanicPlateauReliefByCell;
    private float[] geodesicSeamountReliefByCell;
    private float[] geodesicTotalOceanicReliefByCell;
    private int[] geodesicLandComponentIdByCell;
    private float[] geodesicContinentalShelfInfluenceByCell;
    private float[] geodesicLocalShelfWidthMultiplierByCell;
    private float[] geodesicLocalShelfDepthByCell;
    private float[] geodesicOceanicIslandShelfInfluenceByCell;
    private float[] geodesicContinentalProfileShelfWidthByCell;
    private float[] geodesicFinalShelfWidthByCell;
    private float[] geodesicApproxCellSpacingDegreesByCell;
    private GeodesicCoastType[] geodesicCoastTypeByCell;
    private GeodesicShelfProfileType[] geodesicShelfProfileTypeByCell;
    private GeodesicBathymetryRegion[] geodesicBathymetryRegion;
    private bool[] geodesicOceanMask;
    private byte[] geodesicOceanNeighborCounts;
    private bool[] geodesicCoastlineMask;
    [System.NonSerialized] private GeodesicTransportGraph geodesicTransportGraph;
    [SerializeField] private bool geodesicTransportGraphInitialized;
    [SerializeField] private int geodesicTransportGraphCellCount;
    [SerializeField] private int geodesicTransportGraphEdgeCount;
    [SerializeField] private long geodesicTransportGraphApproximateMemoryBytes;
    [SerializeField] private double geodesicTransportGraphBuildMilliseconds;

    public MeshRenderer OceanRenderer => oceanMeshRenderer;
    public Material LegacyRuntimeOceanMaterial => runtimeOceanMaterial;
    public IReadOnlyList<float> LocalOceanDepths => localOceanDepthByCell;
    public int VisualResolution => Mathf.Max(1, resolution);
    public bool IsPlanetInitialized { get; private set; }
    public GeodesicGridTopology GeodesicTopology { get; private set; }
    public GeodesicTransportGraph GeodesicTransportGraph => geodesicTransportGraph;
    internal bool[] GeodesicOceanMaskData => geodesicOceanMask;
    internal float[] GeodesicSeafloorRadiusData => geodesicSeafloorRadius;
    public PlanetRuntimeDescriptor RuntimeDescriptor { get; private set; }
    public bool HasRuntimeDescriptor { get; private set; }
    public const int GenerationVersion = 2;
    public float BasePlanetRadius => Mathf.Max(0.001f, radius);
    public float MinimumSurfaceRadius => CurrentGridType == PlanetGridType.GeodesicIcosphere ? BasePlanetRadius + GetGeodesicMinimumTerrainOffset() : GetGeneratedRadiusExtrema().min;
    public float MaximumSurfaceRadius => CurrentGridType == PlanetGridType.GeodesicIcosphere ? BasePlanetRadius + GetGeodesicMaximumTerrainOffset() : GetGeneratedRadiusExtrema().max;
    /// <summary>
    /// Outer opaque/liquid silhouette radius for the currently generated mode, in planet-local units.
    /// Atmosphere is deliberately excluded. When ocean is visible, the greater of terrain and water is used.
    /// </summary>
    public float CurrentVisibleOuterRadius
    {
        get
        {
            float terrainRadius = maximumGeneratedOpaqueSurfaceRadius > 0f ? maximumGeneratedOpaqueSurfaceRadius : BasePlanetRadius;
            if (!enableOcean) return terrainRadius;
            float waterRadius = CurrentGridType == PlanetGridType.GeodesicIcosphere ? GeodesicSeaLevelRadius : GetOceanRadius();
            return Mathf.Max(terrainRadius, waterRadius);
        }
    }
    public float GeodesicSeaLevelRadius => resolvedGeodesicSeaLevelRadius > 0f ? resolvedGeodesicSeaLevelRadius : BasePlanetRadius + geodesicSeaLevelOffset;
    public float ResolvedGeodesicSeaLevelRadius => GeodesicSeaLevelRadius;
    public float ResolvedGeodesicSeaLevelOffset => GeodesicSeaLevelRadius - BasePlanetRadius;
    public bool IsGeodesicOceanWorldActive => CurrentGridType == PlanetGridType.GeodesicIcosphere && geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld && enableOcean;
    public float AchievedGeodesicOceanCellCoveragePercent => achievedGeodesicOceanCellCoveragePercent;
    public float AchievedGeodesicOceanAreaCoveragePercent => achievedGeodesicOceanAreaCoveragePercent;
    public int GeodesicOceanCellCount => geodesicOceanCellCount;
    public int GeodesicCoastlineOceanCellCount => geodesicCoastlineOceanCellCount;
    public int DerivedTerrainSeed => usePlanetSeedForTerrain ? PlanetSeedUtility.DeriveSeed(randomSeed, PlanetSeedDomain.Terrain, GenerationVersion) : customTerrainSeed;
    public int DerivedVisualSeed => usePlanetSeedForVisuals ? PlanetSeedUtility.DeriveSeed(randomSeed, PlanetSeedDomain.SurfaceVisuals, GenerationVersion) : customVisualSeed;
    public int DerivedBathymetrySeed => PlanetSeedUtility.DeriveSeed(randomSeed, PlanetSeedDomain.Bathymetry, GenerationVersion);
    public PlanetGridType CurrentGridType => generationMode == PlanetGenerationMode.GeodesicPrototype ? PlanetGridType.GeodesicIcosphere : PlanetGridType.LegacyCubeSphere;

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        if (!Mathf.Approximately(deprecatedGeodesicBaseRadius, 1f) && Mathf.Approximately(radius, 1f))
        {
            radius = deprecatedGeodesicBaseRadius;
        }

        EnsureOceanAppearanceInitialized();
        MigrateLegacyOxygenatedWaterColorIfNeeded();
    }

    void OnValidate()
    {
        EnsureOceanAppearanceInitialized();
        MigrateLegacyOxygenatedWaterColorIfNeeded();
    }

    void EnsureOceanAppearanceInitialized()
    {
        if (oceanAppearance == null || IsUnsetOceanAppearance(oceanAppearance))
        {
            oceanAppearance = OceanAppearanceSettings.LegacyDefaults;
        }
    }

    void MigrateLegacyOxygenatedWaterColorIfNeeded()
    {
        if (oceanAppearance == null || deprecatedLegacyOxygenatedWaterColor == default) return;
        if (oceanAppearance.oxygenatedWaterColor == default || oceanAppearance.oxygenatedWaterColor == OceanAppearanceSettings.LegacyDefaults.oxygenatedWaterColor)
        {
            oceanAppearance.oxygenatedWaterColor = deprecatedLegacyOxygenatedWaterColor;
        }
    }

    static bool IsUnsetOceanAppearance(OceanAppearanceSettings settings)
    {
        return settings == null
            || (settings.baseWaterColor == default
            && settings.shallowWaterColor == default
            && settings.deepWaterColor == default
            && Mathf.Approximately(settings.opacity, 0f)
            && Mathf.Approximately(settings.smoothness, 0f));
    }

    void Awake()
    {
        meshFilter = GetOrAddComponent<MeshFilter>(gameObject);
        meshRenderer = GetOrAddComponent<MeshRenderer>(gameObject);
        MeshCollider meshCollider = GetOrAddComponent<MeshCollider>(gameObject);

        SetupPlanetMaterial();

        mesh = new Mesh { name = "Planet Terrain" };
        meshFilter.sharedMesh = mesh;

        if (meshCollider != null)
        {
            meshCollider.sharedMesh = mesh;
        }

        SetupOceanLayer();
        SetupAtmosphereLayer();
        ClearGeneratedPlanetRuntime();
    }

    public void ApplyStartupSeed(int seed, bool randomSeedEnabled)
    {
        useRandomSeed = randomSeedEnabled;
        randomSeed = seed;

        // The generator's terrain is driven by noiseOffset. Derive that offset from
        // the startup seed so a chosen seed consistently changes the generated planet
        // without changing the existing terrain/noise algorithm.
        System.Random seededRandom = new System.Random(seed);
        noiseOffset = new Vector3(
            (float)(seededRandom.NextDouble() * 2000.0 - 1000.0),
            (float)(seededRandom.NextDouble() * 2000.0 - 1000.0),
            (float)(seededRandom.NextDouble() * 2000.0 - 1000.0));
    }

    public void InitializeAuthoritativePlanet(string reason = null)
    {
        Debug.Log($"[StartupLifecycle] Final planet initialization requested. Reason: {reason ?? "unspecified"}, mode={generationMode}, seed={randomSeed}", this);
        if (randomizeOnStart)
        {
            RandomizeGenerationSettings();
        }

        ClearGeneratedPlanetRuntime();
        if (generationMode == PlanetGenerationMode.GeodesicPrototype)
        {
            GenerateGeodesicPrototype();
        }
        else
        {
            GeneratePlanet();
        }
        IsPlanetInitialized = true;
        Debug.Log($"[StartupLifecycle] Planet generation complete. Mode={generationMode}, seed={randomSeed}, resolution={resolution}, geodesicSimulationSubdivision={geodesicSimulationSubdivisionLevel}, geodesicRenderSubdivision={geodesicRenderSubdivisionLevel}", this);
    }

    public void RegeneratePlanet()
    {
        InitializeAuthoritativePlanet("RegeneratePlanet explicit request");
    }

    private void ClearGeodesicTransportGraph()
    {
        geodesicTransportGraph = null;
        geodesicTransportGraphInitialized = false;
        geodesicTransportGraphCellCount = 0;
        geodesicTransportGraphEdgeCount = 0;
        geodesicTransportGraphApproximateMemoryBytes = 0;
        geodesicTransportGraphBuildMilliseconds = 0d;
    }

    [ContextMenu("Validate Geodesic Transport Graph")]
    private void ValidateGeodesicTransportGraph()
    {
        bool valid = GeodesicTransportGraphValidation.Validate(geodesicTransportGraph, out string report);
        if (valid) Debug.Log($"[GeodesicTransportGraphValidation] {report}", this);
        else Debug.LogError($"[GeodesicTransportGraphValidation] {report}", this);
    }

    private void ClearGeodesicRuntimeVisuals(string reason = null)
    {
        GeodesicGridDebugRenderer debugRenderer = transform.Find("Geodesic Debug Lines")?.GetComponent<GeodesicGridDebugRenderer>();
        if (debugRenderer != null)
        {
            debugRenderer.ClearAndDisable();
        }

        GeodesicCellPicker picker = GetComponent<GeodesicCellPicker>();
        if (picker != null)
        {
            picker.SetTopology(null);
            picker.enabled = false;
        }

        GetComponent<PlanetTemperatureIceVisuals>()?.ClearForGeodesicMode();
        GetComponent<GeodesicVentVisualizer>()?.ClearMarkers();
        GetComponent<GeodesicOceanFe2Visual>()?.ClearVisual();

        GetComponent<GeodesicOceanResourceField>()?.ClearField();
        GetComponent<GeodesicOceanTemperatureField>()?.ClearField();
        GetComponent<GeodesicSurfaceTemperatureField>()?.ClearField();
        GetComponent<GeodesicOceanLayerDomain>()?.ClearGrid();
        GeodesicTopology = null;
        ClearGeodesicTransportGraph();
        geodesicTerrainHeightByCell = null;
        geodesicNormalizedTerrainByCell = null;
        geodesicRawTerrainRadius = null;
        geodesicSeafloorRadius = null;
        geodesicBaseWaterDepth = null;
        geodesicWaterDepth = null;
        geodesicDistanceToShore = null;
        geodesicBasinNoiseContribution = null;
        geodesicBathymetryRegion = null;
        geodesicOceanMask = null;
        geodesicOceanNeighborCounts = null;
        geodesicCoastlineMask = null;
        geodesicDebugSurfaceRadiusCache = null;
        geodesicCurrentRenderTerrainData = default;
        geodesicVisibleSeafloorPositionByCell = null;
        geodesicVisibleSeafloorNormalByCell = null;
        maximumGeneratedOpaqueSurfaceRadius = BasePlanetRadius;

        if (geodesicOceanMeshRenderer != null)
        {
            geodesicOceanMeshRenderer.enabled = false;
            geodesicOceanMeshRenderer.sharedMaterial = null;
        }

        if (geodesicOceanMeshFilter != null)
        {
            geodesicOceanMeshFilter.sharedMesh = null;
        }

        if (geodesicOceanMesh != null)
        {
            geodesicOceanMesh.Clear();
        }

        if (geodesicOceanObject != null)
        {
            geodesicOceanObject.SetActive(false);
            Destroy(geodesicOceanObject);
        }

        ReleaseGeodesicSurfaceMaterial();
        ReleaseGeodesicOceanMaterial();
        geodesicOceanObject = null;
        geodesicOceanMeshFilter = null;
        geodesicOceanMeshRenderer = null;

        if (meshRenderer != null && runtimePlanetMaterial != null && meshRenderer.sharedMaterial == null)
        {
            meshRenderer.sharedMaterial = runtimePlanetMaterial;
        }

        LogModeTransitionRendererInventory($"after geodesic cleanup{(string.IsNullOrEmpty(reason) ? string.Empty : " - " + reason)}", CurrentGridType);
    }

    public void ClearGeneratedPlanetRuntime()
    {
        IsPlanetInitialized = false;
        generatedSurfaceRadiusByCell = null;
        localOceanDepthByCell = null;
        oceanDistanceToShoreByCell = null;
        oceanMaskByCell = null;
        ClearGeodesicRuntimeVisuals("ClearGeneratedPlanetRuntime");

        if (mesh != null) mesh.Clear();
        if (oceanMesh != null) oceanMesh.Clear();
        if (geodesicOceanMesh != null) geodesicOceanMesh.Clear();
        if (atmosphereMesh != null) atmosphereMesh.Clear();

        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider != null) meshCollider.sharedMesh = null;

        if (meshRenderer != null) meshRenderer.enabled = false;
        if (oceanMeshRenderer != null) oceanMeshRenderer.enabled = false;
        if (geodesicOceanMeshRenderer != null) geodesicOceanMeshRenderer.enabled = false;
        if (atmosphereMeshRenderer != null) atmosphereMeshRenderer.enabled = false;
        HasRuntimeDescriptor = false;
        RuntimeDescriptor = default;
        ReleaseRuntimeOceanMaterial();
        if (meshRenderer != null && runtimePlanetMaterial != null)
        {
            meshRenderer.sharedMaterial = runtimePlanetMaterial;
        }
    }

    public void ApplyStartupGrid(PlanetGridType gridType, int cubeSphereResolution, int geodesicSubdivision)
    {
        generationMode = gridType == PlanetGridType.GeodesicIcosphere ? PlanetGenerationMode.GeodesicPrototype : PlanetGenerationMode.LegacyCubeSphere;
        // Prevent the apparent-horizon API from exposing the previous mode's generated maximum
        // during the short interval before the newly selected mode finishes generation.
        maximumGeneratedOpaqueSurfaceRadius = BasePlanetRadius;
        resolution = Mathf.Clamp(cubeSphereResolution, 3, 240);
        geodesicSubdivisionLevel = Mathf.Clamp(geodesicSubdivision, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        geodesicSimulationSubdivisionLevel = geodesicSubdivisionLevel;
        geodesicRenderSubdivisionLevel = Mathf.Clamp(Mathf.Max(geodesicSimulationSubdivisionLevel, geodesicRenderSubdivisionLevel), 0, GeodesicGridTopology.MaxSupportedSubdivision);
        geodesicColliderSubdivisionLevel = Mathf.Clamp(geodesicColliderSubdivisionLevel, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        int expected = gridType == PlanetGridType.GeodesicIcosphere
            ? GeodesicGridTopology.ExpectedCellCount(geodesicSubdivisionLevel)
            : PlanetGridIndexing.GetCellCount(resolution);
        Debug.Log($"[PlanetGenerator] Selected grid type={gridType}, cubeSphereResolution={resolution}, geodesicSimulationSubdivision={geodesicSimulationSubdivisionLevel}, geodesicRenderSubdivision={geodesicRenderSubdivisionLevel}, expectedCellCount={expected}", this);
    }

    private void GenerateGeodesicPrototype()
    {
        var total = System.Diagnostics.Stopwatch.StartNew();
        ResetGeodesicQueryDiagnostics();
        void LogStage(string stage, System.Diagnostics.Stopwatch stageWatch)
        {
            stageWatch.Stop();
            Debug.Log($"[GeodesicGenerationProfile] stage={stage}, durationMs={stageWatch.Elapsed.TotalMilliseconds:F2}", this);
        }

        int simulationSubdivision = Mathf.Clamp(geodesicSimulationSubdivisionLevel, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        int renderSubdivision = Mathf.Clamp(geodesicRenderSubdivisionLevel, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        int colliderSubdivision = Mathf.Clamp(geodesicColliderSubdivisionLevel, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        int estimatedRenderTriangles = GeodesicGridTopology.ExpectedTriangleCount(renderSubdivision);
        int estimatedColliderTriangles = GeodesicGridTopology.ExpectedTriangleCount(colliderSubdivision);
        geodesicSubdivisionLevel = simulationSubdivision;
        geodesicColliderSubdivisionLevel = colliderSubdivision;
        WarnForGeodesicSubdivisionCost(renderSubdivision, colliderSubdivision, estimatedRenderTriangles, estimatedColliderTriangles);

        var stage = System.Diagnostics.Stopwatch.StartNew();
        GeodesicTopology = GeodesicTopologyCache.GetOrBuild(simulationSubdivision, out bool topologyCacheHit);
        LogStage(topologyCacheHit ? "simulation topology cache retrieval" : "simulation topology generation", stage);
        Debug.Log($"[GeodesicTopologyCache] subdivision={simulationSubdivision}, cacheHit={topologyCacheHit}, cachedSubdivisions={GeodesicTopologyCache.CachedSubdivisionCount}, approxTopologyMemory={GeodesicTopology.ApproximateMemoryBytes} bytes", this);
        if (!GeodesicGridValidation.Validate(GeodesicTopology, out string validation))
        {
            Debug.LogError($"[GeodesicPrototype] Validation failed: {validation}", this);
            ClearGeneratedPlanetRuntime();
            return;
        }

        stage = System.Diagnostics.Stopwatch.StartNew();
        geodesicTransportGraph = new GeodesicTransportGraph(GeodesicTopology);
        stage.Stop();
        geodesicTransportGraphBuildMilliseconds = stage.Elapsed.TotalMilliseconds;
        geodesicTransportGraphInitialized = true;
        geodesicTransportGraphCellCount = geodesicTransportGraph.CellCount;
        geodesicTransportGraphEdgeCount = geodesicTransportGraph.EdgeCount;
        geodesicTransportGraphApproximateMemoryBytes = geodesicTransportGraph.ApproximateMemoryBytes;
        Debug.Log($"[GeodesicTransportGraph] subdivision={simulationSubdivision}, cells={geodesicTransportGraphCellCount}, uniqueEdges={geodesicTransportGraphEdgeCount}, buildMs={geodesicTransportGraphBuildMilliseconds:F2}, approximateMemory={geodesicTransportGraphApproximateMemoryBytes} bytes", this);

        stage = System.Diagnostics.Stopwatch.StartNew();
        RebuildGeodesicCellTerrainCache();
        LogStage("simulation-cell terrain cache", stage);
        stage = System.Diagnostics.Stopwatch.StartNew();
        RebuildGeodesicOceanClassification();
        LogStage("bathymetry sampling/interpolation", stage);

        stage = System.Diagnostics.Stopwatch.StartNew();
        var oceanLayerDomain = GetComponent<GeodesicOceanLayerDomain>();
        if (oceanLayerDomain == null)
        {
            Debug.LogError("[GeodesicOceanLayers] Planet Generator is missing its required scene-owned GeodesicOceanLayerDomain component; layered-ocean initialization was skipped.", this);
        }
        else
        {
            oceanLayerDomain.enabled = true;
            oceanLayerDomain.Initialize(this, GeodesicTopology, geodesicTransportGraph, geodesicOceanMask, geodesicSeafloorRadius, GeodesicSeaLevelRadius);
        }
        LogStage("layered-ocean domain initialization", stage);

        stage = System.Diagnostics.Stopwatch.StartNew();
        var temperatureField = GetComponent<GeodesicSurfaceTemperatureField>();
        if (temperatureField == null)
        {
            Debug.LogError("[GeodesicTemperature] Planet Generator is missing its required GeodesicSurfaceTemperatureField component; temperature initialization was skipped.", this);
        }
        else
        {
            temperatureField.enabled = true;
            temperatureField.InitializeForCurrentTopology();
        }
        LogStage("surface-temperature initialization", stage);

        stage = System.Diagnostics.Stopwatch.StartNew();
        var oceanTemperatureField = GetComponent<GeodesicOceanTemperatureField>();
        if (oceanTemperatureField == null)
        {
            Debug.LogError("[GeodesicOceanTemperature] Planet Generator is missing its required scene-owned GeodesicOceanTemperatureField component; subsurface temperature initialization was skipped.", this);
        }
        else
        {
            oceanTemperatureField.enabled = true;
            oceanTemperatureField.InitializeForCurrentDomain();
        }
        LogStage("ocean-temperature initialization", stage);

        stage = System.Diagnostics.Stopwatch.StartNew();
        var oceanResourceField = GetComponent<GeodesicOceanResourceField>();
        if (oceanResourceField == null)
        {
            Debug.LogError("[GeodesicOceanResource] Planet Generator is missing its required scene-owned GeodesicOceanResourceField component; dissolved resource initialization was skipped.", this);
        }
        else
        {
            oceanResourceField.enabled = true;
            bool resourcesInitialized = oceanResourceField.InitializeForCurrentDomain();
            if (!resourcesInitialized || !oceanResourceField.IsInitialized)
            {
                Debug.LogError($"[GeodesicOceanResource] PlanetGenerator observed resource initialization failure: {oceanResourceField.LastInitializationFailure}; {oceanResourceField.LastInitializationFailureMessage}", this);
            }
        }
        LogStage("ocean-resource initialization", stage);

        stage = System.Diagnostics.Stopwatch.StartNew();
        IcosphereRenderGeometry renderGeometry = IcosphereRenderGeometryCache.GetOrBuild(renderSubdivision);
        IcosphereDirectionMapping renderMapping = GetOrBuildDirectionMapping(renderGeometry);
        mesh = IcosphereRenderMeshBuilder.BuildSurfaceMesh(renderGeometry, BasePlanetRadius, $"Geodesic Terrain Render L{renderSubdivision}");
        LogStage("render icosphere generation", stage);

        stage = System.Diagnostics.Stopwatch.StartNew();
        geodesicCurrentRenderTerrainData = ApplyGeodesicTerrainDisplacement(mesh, renderGeometry, renderMapping, false, false, true);
        maximumGeneratedOpaqueSurfaceRadius = Mathf.Max(BasePlanetRadius, MaxRadiusFromTerrainData(geodesicCurrentRenderTerrainData));
        LogStage("terrain displacement", stage);
        stage = System.Diagnostics.Stopwatch.StartNew();
        ApplyGeodesicSurfaceColours(mesh, geodesicCurrentRenderTerrainData);
        LogStage("vertex-colour generation", stage);
        stage = System.Diagnostics.Stopwatch.StartNew();
        mesh.RecalculateNormals();
        CacheVisibleGeodesicSeafloorAnchors(mesh, renderGeometry, renderMapping);
        LogStage("normal recalculation", stage);
        stage = System.Diagnostics.Stopwatch.StartNew();
        mesh.RecalculateBounds();
        LogStage("bounds recalculation", stage);

        stage = System.Diagnostics.Stopwatch.StartNew();
        meshFilter.sharedMesh = mesh;
        if (meshRenderer != null) meshRenderer.enabled = true;
        LogStage("terrain mesh assignment/upload", stage);

        var ventVisualizer = GetOrAddComponent<GeodesicVentVisualizer>(gameObject);
        ventVisualizer.Initialize(oceanResourceField, this);

        stage = System.Diagnostics.Stopwatch.StartNew();
        IcosphereRenderGeometry colliderGeometry = IcosphereRenderGeometryCache.GetOrBuild(colliderSubdivision);
        IcosphereDirectionMapping colliderMapping = GetOrBuildDirectionMapping(colliderGeometry);
        Mesh colliderMesh = IcosphereRenderMeshBuilder.BuildSurfaceMesh(colliderGeometry, BasePlanetRadius, $"Geodesic Terrain Collider L{colliderSubdivision}");
        GeodesicRenderTerrainData colliderTerrainData = ApplyGeodesicTerrainDisplacement(colliderMesh, colliderGeometry, colliderMapping, true, true, geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld);
        if (geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld) ValidateOceanWorldGeometryCoverage(geodesicCurrentRenderTerrainData, colliderTerrainData);
        MeshCollider meshCollider = GetOrAddComponent<MeshCollider>(gameObject);
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = colliderMesh;
        LogStage("MeshCollider assignment/cooking", stage);

        if (oceanMesh != null) oceanMesh.Clear();
        if (geodesicOceanMesh != null) geodesicOceanMesh.Clear();
        if (atmosphereMesh != null) atmosphereMesh.Clear();
        if (oceanMeshRenderer != null) oceanMeshRenderer.enabled = false;
        if (geodesicOceanMeshRenderer != null) geodesicOceanMeshRenderer.enabled = false;
        if (atmosphereMeshRenderer != null) atmosphereMeshRenderer.enabled = false;

        stage = System.Diagnostics.Stopwatch.StartNew();
        BuildGeodesicOceanVisual();
        GetOrAddComponent<GeodesicOceanFe2Visual>(gameObject).Initialize(
            this, oceanResourceField, geodesicOceanMesh, geodesicOceanMeshRenderer, oceanMapping);
        LogStage("ocean mesh generation", stage);
        var picker = GetOrAddComponent<GeodesicCellPicker>(gameObject);
        picker.SetTemperatureDisplayAuthority(replicatorManager);
        picker.enabled = true;
        picker.SetTopology(GeodesicTopology);
        Transform debugTransform = transform.Find("Geodesic Debug Lines");
        GameObject debugObject = debugTransform != null ? debugTransform.gameObject : new GameObject("Geodesic Debug Lines");
        debugObject.transform.SetParent(transform, false);
        debugObject.SetActive(true);
        debugObject.layer = gameObject.layer;
        var debug = GetOrAddComponent<GeodesicGridDebugRenderer>(debugObject);
        debug.showCellOutlines = showGeodesicCellOutlines;
        debug.highlightPentagons = highlightGeodesicPentagons;
        debug.showCellCentres = showGeodesicCellCentres;
        debug.showSelectedCell = showSelectedGeodesicCell;
        debug.highlightOceanCells = highlightGeodesicOceanCells;
        debug.highlightCoastlineCells = highlightGeodesicCoastlineCells;
        debug.oceanMask = geodesicOceanMask;
        debug.coastlineMask = geodesicCoastlineMask;
        debug.surfaceRadiusSampler = GetSurfaceRadiusAtDirection;
        debug.surfacePositionSampler = SampleGeodesicDebugSurfacePosition;
        debug.radialOffset = geodesicOutlineRadialOffset;
        stage = System.Diagnostics.Stopwatch.StartNew();
        debug.Render(GeodesicTopology, BasePlanetRadius);
        LogStage("debug renderer generation", stage);
        PopulateRuntimeDescriptor(GeodesicTopology.CellCount);
        LogPlanetGenerationValidation(meshCollider);
        geodesicLastCoreGenerationMilliseconds = total.Elapsed.TotalMilliseconds;
        stage = System.Diagnostics.Stopwatch.StartNew();
        LogGeodesicTerrainDiagnostics(mesh, simulationSubdivision, renderSubdivision, geodesicCurrentRenderTerrainData);
        LogStage("terrain diagnostics", stage);
        LogGeodesicQueryDiagnostics();
        LogModeTransitionRendererInventory("after generation", PlanetGridType.GeodesicIcosphere);
        total.Stop();
        geodesicLastFullSynchronousGenerationMilliseconds = total.Elapsed.TotalMilliseconds;
        Debug.Log($"[GeodesicPrototype] coreGenerationDurationMs={geodesicLastCoreGenerationMilliseconds:F2}, fullSynchronousGenerationDurationMs={geodesicLastFullSynchronousGenerationMilliseconds:F2}, profilingBoundaryDifferenceMs={(geodesicLastFullSynchronousGenerationMilliseconds - geodesicLastCoreGenerationMilliseconds):F2}, profilingBoundaryDifference=post-core synchronous diagnostics/query logging/inventory reporting,  simulationSubdivision={simulationSubdivision}, renderSubdivision={renderSubdivision}, colliderSubdivision={colliderSubdivision}, oceanSubdivision={geodesicOceanRenderSubdivisionLevel}, cells={GeodesicTopology.CellCount}, renderVertices={mesh.vertexCount}, renderTriangles={mesh.triangles.Length / 3}, colliderVertices={colliderMesh.vertexCount}, colliderTriangles={colliderMesh.triangles.Length / 3}, simulationTriangles={GeodesicTopology.TriangleCount}, edges={GeodesicTopology.EdgeCount}, durationMs={geodesicLastFullSynchronousGenerationMilliseconds:F2}, approxSimulationTopologyMemory={GeodesicTopology.ApproximateMemoryBytes} bytes, approxRenderGeometryMemory={renderGeometry.ApproximateManagedBytes} bytes, approxColliderGeometryMemory={colliderGeometry.ApproximateManagedBytes} bytes, approxRenderMappingMemory={renderMapping.ApproximateManagedBytes} bytes, approxColliderMappingMemory={colliderMapping.ApproximateManagedBytes} bytes, mappingCacheEntries={IcosphereDirectionMappingCache.CachedMappingCount}, renderGeometryCacheEntries={IcosphereRenderGeometryCache.CachedSubdivisionCount}. Validation: {validation}", this);
    }



    [ContextMenu("Clear Geodesic Render Geometry Cache")]
    public void ClearGeodesicRenderGeometryCache()
    {
        IcosphereRenderGeometryCache.Clear();
        IcosphereDirectionMappingCache.Clear();
        Debug.Log("[GeodesicGenerationProfile] Cleared immutable geodesic render unit-geometry and direction-mapping caches.", this);
    }

    [ContextMenu("Clear Geodesic Topology Cache")]
    public void ClearGeodesicTopologyCache()
    {
        GeodesicTopologyCache.Clear();
        Debug.Log("[GeodesicGenerationProfile] Cleared immutable geodesic simulation-topology cache.", this);
    }


    IcosphereDirectionMapping GetOrBuildDirectionMapping(IcosphereRenderGeometry geometry)
    {
        IcosphereDirectionMapping mapping = IcosphereDirectionMappingCache.GetOrBuild(GeodesicTopology, geometry, out bool cacheHit);
        if (cacheHit) geodesicDirectionMappingCacheHits++; else geodesicDirectionMappingCacheMisses++;
        long currentCandidateCellsInspected = cacheHit ? 0L : mapping.CandidateCellsInspected;
        geodesicDirectionCandidateCellsInspected += currentCandidateCellsInspected;
        Debug.Log($"[GeodesicDirectionMapping] simulationSubdivision={mapping.SimulationSubdivision}, targetSubdivision={mapping.TargetSubdivision}, samples={mapping.SampleCount}, identity={mapping.UsedIdentityMapping}, originalCandidateCellsInspectedDuringBuild={mapping.CandidateCellsInspected}, currentRequestCandidateCellsInspected={currentCandidateCellsInspected}, approxMappingMemory={mapping.ApproximateManagedBytes} bytes, cacheHit={cacheHit}", this);
        return mapping;
    }

    Vector3 SampleGeodesicDebugSurfacePosition(Vector3 direction)
    {
        Vector3 unit = direction.sqrMagnitude > 1e-10f ? direction.normalized : Vector3.up;
        if (geodesicDebugSurfaceRadiusCache == null) BuildGeodesicDebugSurfaceRadiusCache();
        if (geodesicDebugSurfaceRadiusCache != null && geodesicDebugSurfaceRadiusCache.TryGetValue(GeodesicDebugDirectionKey.From(unit), out float cachedRadius))
        {
            geodesicSurfaceRadiusQueryCount++;
            return unit * (cachedRadius + geodesicOutlineRadialOffset);
        }

        float radius = SampleGeodesicSurfaceRadiusMapped(unit, -1, null);
        return unit * (radius + geodesicOutlineRadialOffset);
    }

    void BuildGeodesicDebugSurfaceRadiusCache()
    {
        if (GeodesicTopology == null || GeodesicTopology.Triangles == null)
        {
            geodesicDebugSurfaceRadiusCache = new Dictionary<GeodesicDebugDirectionKey, float>();
            return;
        }

        geodesicDebugSurfaceRadiusCache = new Dictionary<GeodesicDebugDirectionKey, float>(GeodesicTopology.TriangleCount);
        for (int triangleIndex = 0; triangleIndex < GeodesicTopology.TriangleCount; triangleIndex++)
        {
            int a = GeodesicTopology.Triangles[triangleIndex * 3];
            int b = GeodesicTopology.Triangles[triangleIndex * 3 + 1];
            int c = GeodesicTopology.Triangles[triangleIndex * 3 + 2];
            Vector3 direction = (GeodesicTopology.CellDirections[a] + GeodesicTopology.CellDirections[b] + GeodesicTopology.CellDirections[c]).normalized;
            int nearest = FindNearestDebugCandidate(direction, a, b, c);
            geodesicDebugSurfaceRadiusCache[GeodesicDebugDirectionKey.From(direction)] = SampleGeodesicSurfaceRadiusFromNearestCell(direction, nearest);
        }
    }

    int FindNearestDebugCandidate(Vector3 direction, int a, int b, int c)
    {
        int best = a;
        float bestDot = Vector3.Dot(direction, GeodesicTopology.CellDirections[a]);
        float dotB = Vector3.Dot(direction, GeodesicTopology.CellDirections[b]);
        if (dotB > bestDot) { bestDot = dotB; best = b; }
        float dotC = Vector3.Dot(direction, GeodesicTopology.CellDirections[c]);
        if (dotC > bestDot) best = c;
        geodesicDirectionCandidateCellsInspected += 3;
        return best;
    }

    float SampleGeodesicSurfaceRadiusFromNearestCell(Vector3 direction, int nearest)
    {
        geodesicSurfaceRadiusQueryCount++;
        if (enableGeodesicTerrainDisplacement) geodesicTerrainNoiseEvaluationCount++;
        float raw = EvaluateRawGeodesicTerrainRadiusUncounted(direction);
        if (GeodesicTopology == null || geodesicSeafloorRadius == null || geodesicOceanMask == null || raw >= GeodesicSeaLevelRadius || nearest < 0 || nearest >= geodesicOceanMask.Length || !geodesicOceanMask[nearest]) return raw;
        geodesicBathymetryInterpolationCount++;
        float weighted = geodesicSeafloorRadius[nearest];
        float weightSum = 1f;
        int baseIndex = nearest * 6;
        for (int n = 0; n < GeodesicTopology.NeighborCounts[nearest]; n++)
        {
            int nb = GeodesicTopology.Neighbors6[baseIndex + n];
            if (nb < 0 || nb >= geodesicSeafloorRadius.Length || !geodesicOceanMask[nb]) continue;
            geodesicDirectionCandidateCellsInspected++;
            float dot = Mathf.Clamp(Vector3.Dot(direction, GeodesicTopology.CellDirections[nb]), -1f, 1f);
            float w = 1f / Mathf.Max(0.0001f, Mathf.Acos(dot));
            weighted += geodesicSeafloorRadius[nb] * w;
            weightSum += w;
        }
        return Mathf.Min(raw, weighted / Mathf.Max(0.0001f, weightSum));
    }

    void ResetGeodesicQueryDiagnostics()
    {
        geodesicSurfaceRadiusQueryCount = 0;
        geodesicDirectionToCellQueryCount = 0;
        geodesicDirectionCandidateCellsInspected = 0;
        geodesicTerrainNoiseEvaluationCount = 0;
        geodesicSimulationCellTerrainEvaluationCount = 0;
        geodesicRenderVertexTerrainEvaluationCount = 0;
        geodesicDiagnosticOnlyTerrainEvaluationCount = 0;
        geodesicBathymetryInterpolationCount = 0;
        geodesicDirectionMappingCacheHits = 0;
        geodesicDirectionMappingCacheMisses = 0;
        geodesicSurfaceRadiusQueryMilliseconds = 0d;
        geodesicLastShorelineDistanceMilliseconds = 0d;
        geodesicDebugSurfaceRadiusCache = null;
    }

    void LogGeodesicQueryDiagnostics()
    {
        Debug.Log($"[GeodesicSurfaceQueryDiagnostics] surfaceRadiusQueries={geodesicSurfaceRadiusQueryCount}, directionToCellQueries={geodesicDirectionToCellQueryCount}, candidateCellsInspected={geodesicDirectionCandidateCellsInspected}, terrainNoiseEvaluations={geodesicTerrainNoiseEvaluationCount}, simulationCellTerrainEvaluations={geodesicSimulationCellTerrainEvaluationCount}, renderVertexTerrainEvaluations={geodesicRenderVertexTerrainEvaluationCount}, diagnosticOnlyTerrainEvaluations={geodesicDiagnosticOnlyTerrainEvaluationCount}, shorelineDistanceMs={geodesicLastShorelineDistanceMilliseconds:F2}, bathymetryInterpolations={geodesicBathymetryInterpolationCount}, directionMappingCacheHits={geodesicDirectionMappingCacheHits}, directionMappingCacheMisses={geodesicDirectionMappingCacheMisses}, surfaceRadiusQueryMs={geodesicSurfaceRadiusQueryMilliseconds:F2}, oceanicReliefMs={geodesicLastOceanicReliefMilliseconds:F2}, landComponentMs={geodesicLastLandComponentMilliseconds:F2}, coastTypeMs={geodesicLastCoastTypeMilliseconds:F2}, shelfVariationMs={geodesicLastShelfVariationMilliseconds:F2}, finalBathymetryMs={geodesicLastFinalBathymetryMilliseconds:F2}", this);
    }

    void WarnForGeodesicSubdivisionCost(int renderSubdivision, int colliderSubdivision, int estimatedRenderTriangles, int estimatedColliderTriangles)
    {
        if (renderSubdivision >= 8)
        {
            Debug.LogWarning("[GeodesicGenerationProfile] Render subdivision 8 is available but can generate multi-million-triangle meshes and several-second main-thread stalls. Each additional icosphere subdivision approximately quadruples triangle count.", this);
        }

        if (colliderSubdivision >= 8 || (renderSubdivision >= 8 && colliderSubdivision >= renderSubdivision))
        {
            Debug.LogWarning($"[GeodesicGenerationProfile] Collider subdivision {colliderSubdivision} is at an extreme visual resolution. Prefer an interaction-only collider around subdivision 6 because picking resolves the authoritative cell from normalized hit direction.", this);
        }

        int threshold = Mathf.Max(1, geodesicDiagnosticTriangleWarningThreshold);
        if (estimatedRenderTriangles > threshold)
        {
            Debug.LogWarning($"[GeodesicGenerationProfile] Estimated render triangle count {estimatedRenderTriangles:N0} exceeds diagnostic threshold {threshold:N0}. Every additional geodesic subdivision approximately quadruples triangle count.", this);
        }
        if (estimatedColliderTriangles > threshold)
        {
            Debug.LogWarning($"[GeodesicGenerationProfile] Estimated collider triangle count {estimatedColliderTriangles:N0} exceeds diagnostic threshold {threshold:N0}; lower geodesicColliderSubdivisionLevel unless high-resolution collision is intentionally required.", this);
        }
    }

    void ApplyGeodesicSurfaceColours(Mesh targetMesh, GeodesicRenderTerrainData terrainData)
    {
        if (targetMesh == null || !enableGeodesicProceduralSurfaceColours)
        {
            if (targetMesh != null) targetMesh.colors = null;
            return;
        }

        Vector3[] vertices = targetMesh.vertices;
        Color[] colours = new Color[vertices.Length];
        int seed = DerivedVisualSeed;
        Vector3 seedOffset = BuildGeodesicVisualSeedOffset(seed);
        float scale = Mathf.Max(0.001f, geodesicVisualNoiseScale);
        int octaves = Mathf.Clamp(geodesicVisualOctaves, 1, 8);
        float persistenceSafe = Mathf.Clamp01(geodesicVisualPersistence);
        float lacunaritySafe = Mathf.Max(1f, geodesicVisualLacunarity);
        float contrastSafe = Mathf.Max(0.25f, geodesicVisualContrast);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 direction = vertices[i].normalized;
            float value = SampleGeodesicVisualNoise(direction, seedOffset, scale, octaves, persistenceSafe, lacunaritySafe);
            if (geodesicColoursUseTerrainHeight)
            {
                float terrainValue = terrainData.NormalizedHeights != null && i < terrainData.NormalizedHeights.Length ? terrainData.NormalizedHeights[i] : GetNormalizedTerrainHeightAtDirection(direction);
                value = Mathf.Lerp(value, terrainValue, 0.55f);
            }
            value = Mathf.Clamp01(Mathf.Pow(value, contrastSafe));
            colours[i] = BlendGeodesicPalette(value);
        }

        targetMesh.colors = colours;
        EnsureGeodesicSurfaceMaterial();
    }

    float SampleGeodesicVisualNoise(Vector3 direction, Vector3 seedOffset, float scale, int octaves, float persistenceSafe, float lacunaritySafe)
    {
        float amplitude = 1f;
        float frequency = scale;
        float total = 0f;
        float amplitudeSum = 0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            float sample = 0.5f * (SimpleNoise.Evaluate(direction * frequency + seedOffset) + 1f);
            total += sample * amplitude;
            amplitudeSum += amplitude;
            amplitude *= persistenceSafe;
            frequency *= lacunaritySafe;
        }

        return amplitudeSum > 0f ? total / amplitudeSum : 0f;
    }

    Vector3 BuildGeodesicVisualSeedOffset(int seed)
    {
        System.Random random = new System.Random(seed);
        return new Vector3(
            (float)(random.NextDouble() * 2000.0 - 1000.0),
            (float)(random.NextDouble() * 2000.0 - 1000.0),
            (float)(random.NextDouble() * 2000.0 - 1000.0));
    }

    Color BlendGeodesicPalette(float value)
    {
        if (value < 0.5f)
        {
            return Color.Lerp(geodesicLowColour, geodesicMiddleColour, value * 2f);
        }

        return Color.Lerp(geodesicMiddleColour, geodesicHighColour, (value - 0.5f) * 2f);
    }

    public float GetTerrainHeightAtDirection(Vector3 direction)
    {
        if (CurrentGridType == PlanetGridType.GeodesicIcosphere)
        {
            return EvaluateGeodesicTerrainHeight(direction);
        }

        return GetSurfaceRadius(direction) - BasePlanetRadius;
    }

    public float GetSurfaceRadiusAtDirection(Vector3 direction)
    {
        var queryWatch = System.Diagnostics.Stopwatch.StartNew();
        geodesicSurfaceRadiusQueryCount++;
        float result;
        if (CurrentGridType == PlanetGridType.GeodesicIcosphere)
        {
            result = GetGeodesicSeafloorRadiusAtDirection(direction);
        }
        else
        {
            result = GetSurfaceRadius(direction);
        }
        queryWatch.Stop();
        geodesicSurfaceRadiusQueryMilliseconds += queryWatch.Elapsed.TotalMilliseconds;
        return result;
    }

    public float GetCellTerrainHeight(int geodesicCellIndex)
    {
        if (geodesicTerrainHeightByCell != null && geodesicCellIndex >= 0 && geodesicCellIndex < geodesicTerrainHeightByCell.Length)
        {
            return geodesicTerrainHeightByCell[geodesicCellIndex];
        }

        return GeodesicTopology != null && geodesicCellIndex >= 0 && geodesicCellIndex < GeodesicTopology.CellCount
            ? GetTerrainHeightAtDirection(GeodesicTopology.CellDirections[geodesicCellIndex])
            : 0f;
    }

    public float GetCellSurfaceRadius(int geodesicCellIndex)
    {
        return GetGeodesicCellSeafloorRadius(geodesicCellIndex);
    }

    public float GetNormalizedTerrainHeightAtDirection(Vector3 direction)
    {
        float min = GetGeodesicMinimumTerrainOffset();
        float max = GetGeodesicMaximumTerrainOffset();
        if (max <= min) return 0.5f;
        return Mathf.InverseLerp(min, max, EvaluateGeodesicTerrainHeight(direction));
    }

    public float GetCellNormalizedTerrainHeight(int geodesicCellIndex)
    {
        if (geodesicNormalizedTerrainByCell != null && geodesicCellIndex >= 0 && geodesicCellIndex < geodesicNormalizedTerrainByCell.Length)
        {
            return geodesicNormalizedTerrainByCell[geodesicCellIndex];
        }

        return GeodesicTopology != null && geodesicCellIndex >= 0 && geodesicCellIndex < GeodesicTopology.CellCount
            ? GetNormalizedTerrainHeightAtDirection(GeodesicTopology.CellDirections[geodesicCellIndex])
            : 0.5f;
    }

    public float GetWaterDepthAtDirection(Vector3 direction)
    {
        if (CurrentGridType == PlanetGridType.GeodesicIcosphere) return Mathf.Max(0f, GeodesicSeaLevelRadius - GetGeodesicSeafloorRadiusAtDirection(direction));
        return Mathf.Max(0f, GeodesicSeaLevelRadius - GetSurfaceRadiusAtDirection(direction));
    }

    public bool IsDirectionOcean(Vector3 direction)
    {
        return GetWaterDepthAtDirection(direction) > 0f;
    }

    public bool IsGeodesicCellOcean(int geodesicCellIndex)
    {
        if (geodesicOceanMask != null && geodesicCellIndex >= 0 && geodesicCellIndex < geodesicOceanMask.Length) return geodesicOceanMask[geodesicCellIndex];
        return GeodesicTopology != null && geodesicCellIndex >= 0 && geodesicCellIndex < GeodesicTopology.CellCount && IsDirectionOcean(GeodesicTopology.CellDirections[geodesicCellIndex]);
    }

    public float GetGeodesicCellWaterDepth(int geodesicCellIndex)
    {
        if (geodesicWaterDepth != null && geodesicCellIndex >= 0 && geodesicCellIndex < geodesicWaterDepth.Length) return geodesicWaterDepth[geodesicCellIndex];
        return GeodesicTopology != null && geodesicCellIndex >= 0 && geodesicCellIndex < GeodesicTopology.CellCount ? GetWaterDepthAtDirection(GeodesicTopology.CellDirections[geodesicCellIndex]) : 0f;
    }

    public byte GetGeodesicOceanNeighborCount(int geodesicCellIndex)
    {
        return geodesicOceanNeighborCounts != null && geodesicCellIndex >= 0 && geodesicCellIndex < geodesicOceanNeighborCounts.Length ? geodesicOceanNeighborCounts[geodesicCellIndex] : (byte)0;
    }

    public bool IsGeodesicCellCoastline(int geodesicCellIndex)
    {
        return geodesicCoastlineMask != null && geodesicCellIndex >= 0 && geodesicCellIndex < geodesicCoastlineMask.Length && geodesicCoastlineMask[geodesicCellIndex];
    }

    public float EvaluateGeodesicTerrainHeight(Vector3 direction)
    {
        if (!enableGeodesicTerrainDisplacement) return 0f;
        geodesicTerrainNoiseEvaluationCount++;
        return PlanetTerrainSampler.EvaluateHeight(direction, DerivedTerrainSeed, GetGeodesicTerrainSettings());
    }

    public PlanetTerrainSample EvaluateGeodesicTerrainSample(Vector3 direction)
    {
        if (!enableGeodesicTerrainDisplacement) return default;
        return PlanetTerrainSampler.Evaluate(direction, DerivedTerrainSeed, GetGeodesicTerrainSettings());
    }

    PlanetTerrainSettings GetGeodesicTerrainSettings()
    {
        return new PlanetTerrainSettings
        {
            continentAmplitude = geodesicContinentAmplitude,
            continentScale = geodesicContinentNoiseScale,
            continentBias = geodesicContinentBias,
            mountainAmplitude = geodesicMountainAmplitude,
            mountainScale = geodesicMountainNoiseScale,
            mountainCoverageThreshold = geodesicMountainCoverageThreshold,
            mountainMaskSoftness = geodesicMountainMaskSoftness,
            ridgeSharpness = geodesicRidgeSharpness,
            domainWarpScale = geodesicDomainWarpScale,
            domainWarpStrength = geodesicDomainWarpStrength,
            fineDetailScale = geodesicFineDetailScale,
            fineDetailAmplitude = geodesicFineDetailAmplitude,
            minimumTerrainOffset = geodesicMinimumTerrainOffset,
            maximumTerrainOffset = geodesicMaximumTerrainOffset,
            octaves = geodesicTerrainOctaves,
            persistence = geodesicTerrainPersistence,
            lacunarity = geodesicTerrainLacunarity,
            heightContrast = geodesicTerrainHeightContrast
        };
    }

    [ContextMenu("Apply Geodesic Smooth Terrain Preset")]
    public void ApplyGeodesicSmoothTerrainPreset()
    {
        geodesicContinentAmplitude = 0.045f; geodesicContinentNoiseScale = 0.65f; geodesicContinentBias = -0.02f;
        geodesicMountainAmplitude = 0.055f; geodesicMountainNoiseScale = 3.8f; geodesicMountainCoverageThreshold = 0.68f; geodesicMountainMaskSoftness = 0.24f;
        geodesicRidgeSharpness = 2.2f; geodesicDomainWarpScale = 1.1f; geodesicDomainWarpStrength = 0.12f; geodesicFineDetailScale = 12f; geodesicFineDetailAmplitude = 0.006f;
        geodesicMinimumTerrainOffset = -0.04f; geodesicMaximumTerrainOffset = 0.08f;
    }

    [ContextMenu("Apply Geodesic Earthlike Terrain Preset")]
    public void ApplyGeodesicEarthlikeTerrainPreset()
    {
        PlanetTerrainSettings s = PlanetTerrainSettings.Earthlike;
        geodesicContinentAmplitude = s.continentAmplitude; geodesicContinentNoiseScale = s.continentScale; geodesicContinentBias = s.continentBias;
        geodesicMountainAmplitude = s.mountainAmplitude; geodesicMountainNoiseScale = s.mountainScale; geodesicMountainCoverageThreshold = s.mountainCoverageThreshold; geodesicMountainMaskSoftness = s.mountainMaskSoftness;
        geodesicRidgeSharpness = s.ridgeSharpness; geodesicDomainWarpScale = s.domainWarpScale; geodesicDomainWarpStrength = s.domainWarpStrength; geodesicFineDetailScale = s.fineDetailScale; geodesicFineDetailAmplitude = s.fineDetailAmplitude;
        geodesicMinimumTerrainOffset = s.minimumTerrainOffset; geodesicMaximumTerrainOffset = s.maximumTerrainOffset; geodesicTerrainOctaves = s.octaves; geodesicTerrainPersistence = s.persistence; geodesicTerrainLacunarity = s.lacunarity; geodesicTerrainHeightContrast = s.heightContrast;
    }

    [ContextMenu("Apply Geodesic Rugged Terrain Preset")]
    public void ApplyGeodesicRuggedTerrainPreset()
    {
        geodesicContinentAmplitude = 0.11f; geodesicContinentNoiseScale = 0.72f; geodesicContinentBias = -0.04f;
        geodesicMountainAmplitude = 0.24f; geodesicMountainNoiseScale = 5.4f; geodesicMountainCoverageThreshold = 0.5f; geodesicMountainMaskSoftness = 0.16f;
        geodesicRidgeSharpness = 1.35f; geodesicDomainWarpScale = 1.45f; geodesicDomainWarpStrength = 0.38f; geodesicFineDetailScale = 22f; geodesicFineDetailAmplitude = 0.025f;
        geodesicMinimumTerrainOffset = -0.11f; geodesicMaximumTerrainOffset = 0.31f; geodesicRenderSubdivisionLevel = Mathf.Max(5, geodesicRenderSubdivisionLevel);
    }

    [ContextMenu("Apply Geodesic Extreme Terrain Preset")]
    public void ApplyGeodesicExtremeTerrainPreset()
    {
        geodesicContinentAmplitude = 0.14f; geodesicContinentNoiseScale = 0.68f; geodesicContinentBias = -0.02f;
        geodesicMountainAmplitude = 0.36f; geodesicMountainNoiseScale = 6.8f; geodesicMountainCoverageThreshold = 0.44f; geodesicMountainMaskSoftness = 0.12f;
        geodesicRidgeSharpness = 1.1f; geodesicDomainWarpScale = 1.65f; geodesicDomainWarpStrength = 0.52f; geodesicFineDetailScale = 28f; geodesicFineDetailAmplitude = 0.035f;
        geodesicMinimumTerrainOffset = -0.16f; geodesicMaximumTerrainOffset = 0.48f; geodesicRenderSubdivisionLevel = Mathf.Max(5, geodesicRenderSubdivisionLevel);
    }

    void ApplyGeodesicTerrainDisplacement(Mesh targetMesh) => ApplyGeodesicTerrainDisplacement(targetMesh, default(IcosphereRenderGeometry), null, true, true, false);

    GeodesicRenderTerrainData ApplyGeodesicTerrainDisplacement(Mesh targetMesh, IcosphereRenderGeometry geometry, IcosphereDirectionMapping mapping, bool recalculateNormals, bool recalculateBounds, bool captureTerrainData)
    {
        GeodesicRenderTerrainData data = default;
        if (targetMesh == null) return data;
        Vector3[] vertices = targetMesh.vertices;
        if (captureTerrainData)
        {
            data.RawRadii = new float[vertices.Length]; data.SurfaceRadii = new float[vertices.Length]; data.Heights = new float[vertices.Length]; data.NormalizedHeights = new float[vertices.Length]; data.MountainMasks = new float[vertices.Length];
        }
        bool hasGeometryDirections = geometry.UnitVertices != null;
        PlanetTerrainSettings settings = GetGeodesicTerrainSettings();
        float min = GetGeodesicMinimumTerrainOffset(); float max = GetGeodesicMaximumTerrainOffset();
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 direction = hasGeometryDirections && i < geometry.VertexCount ? geometry.UnitVertices[i] : (vertices[i].sqrMagnitude > 1e-10f ? vertices[i].normalized : Vector3.up);
            PlanetTerrainSample sample = enableGeodesicTerrainDisplacement ? PlanetTerrainSampler.Evaluate(direction, DerivedTerrainSeed, settings) : default;
            if (enableGeodesicTerrainDisplacement) geodesicTerrainNoiseEvaluationCount++;
            geodesicRenderVertexTerrainEvaluationCount++;
            float height = enableGeodesicTerrainDisplacement ? sample.HeightOffset : 0f;
            float raw = BasePlanetRadius + height;
            float surface = SampleGeodesicSurfaceRadiusMappedWithRaw(direction, i, mapping, raw);
            vertices[i] = direction * surface;
            if (captureTerrainData)
            {
                data.RawRadii[i] = raw; data.SurfaceRadii[i] = surface; data.Heights[i] = height; data.NormalizedHeights[i] = max > min ? Mathf.InverseLerp(min, max, height) : 0.5f; data.MountainMasks[i] = sample.MountainMask;
            }
        }
        targetMesh.vertices = vertices;
        if (recalculateNormals) targetMesh.RecalculateNormals();
        if (recalculateBounds) targetMesh.RecalculateBounds();
        return data;
    }

    private void CacheVisibleGeodesicSeafloorAnchors(Mesh renderMesh, IcosphereRenderGeometry geometry, IcosphereDirectionMapping mapping)
    {
        if (renderMesh == null || GeodesicTopology == null || mapping == null || geometry.UnitVertices == null) return;
        Vector3[] vertices = renderMesh.vertices;
        Vector3[] normals = renderMesh.normals;
        int cellCount = GeodesicTopology.CellCount;
        geodesicVisibleSeafloorPositionByCell = new Vector3[cellCount];
        geodesicVisibleSeafloorNormalByCell = new Vector3[cellCount];
        float[] bestDot = new float[cellCount];
        for (int cell = 0; cell < cellCount; cell++) bestDot[cell] = -2f;

        int sampleCount = Mathf.Min(mapping.SampleCount, Mathf.Min(vertices.Length, geometry.VertexCount));
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            int cell = mapping.Samples[sampleIndex].NearestCell;
            if (cell < 0 || cell >= cellCount) continue;
            float dot = Vector3.Dot(geometry.UnitVertices[sampleIndex], GeodesicTopology.CellDirections[cell]);
            if (dot <= bestDot[cell]) continue;
            bestDot[cell] = dot;
            geodesicVisibleSeafloorPositionByCell[cell] = vertices[sampleIndex];
            geodesicVisibleSeafloorNormalByCell[cell] = sampleIndex < normals.Length ? normals[sampleIndex].normalized : geometry.UnitVertices[sampleIndex];
        }
    }

    public bool TryGetVisibleGeodesicSeafloorWorldAnchor(int cellIndex, out Vector3 worldPosition, out Vector3 worldNormal)
    {
        worldPosition = default;
        worldNormal = Vector3.up;
        if (CurrentGridType != PlanetGridType.GeodesicIcosphere || geodesicVisibleSeafloorPositionByCell == null ||
            geodesicVisibleSeafloorNormalByCell == null || cellIndex < 0 || cellIndex >= geodesicVisibleSeafloorPositionByCell.Length) return false;
        Vector3 localPosition = geodesicVisibleSeafloorPositionByCell[cellIndex];
        Vector3 localNormal = geodesicVisibleSeafloorNormalByCell[cellIndex];
        if (localPosition.sqrMagnitude <= 1e-10f || localNormal.sqrMagnitude <= 1e-10f) return false;
        worldPosition = transform.TransformPoint(localPosition);
        worldNormal = transform.localToWorldMatrix.inverse.transpose.MultiplyVector(localNormal).normalized;
        return true;
    }

    float SampleGeodesicSurfaceRadiusMapped(Vector3 direction, int sampleIndex, IcosphereDirectionMapping mapping)
    {
        if (enableGeodesicTerrainDisplacement) geodesicTerrainNoiseEvaluationCount++;
        Vector3 d = direction.sqrMagnitude > 1e-10f ? direction.normalized : Vector3.up;
        float raw = EvaluateRawGeodesicTerrainRadiusUncounted(d);
        return SampleGeodesicSurfaceRadiusMappedWithRaw(d, sampleIndex, mapping, raw);
    }

    float SampleGeodesicSurfaceRadiusMappedWithRaw(Vector3 d, int sampleIndex, IcosphereDirectionMapping mapping, float raw)
    {
        geodesicSurfaceRadiusQueryCount++;
        if (CurrentGridType != PlanetGridType.GeodesicIcosphere || mapping == null)
        {
            return GetGeodesicSeafloorRadiusAtDirectionUnprofiled(d, raw);
        }

        geodesicBathymetryInterpolationCount++;
        return mapping.SampleSeafloorRadius(sampleIndex, d, raw, resolvedGeodesicSeaLevelRadius, geodesicOceanMask, geodesicSeafloorRadius);
    }


    void LogGeodesicTerrainDiagnostics(Mesh renderMesh, int simulationSubdivision, int renderSubdivision, GeodesicRenderTerrainData terrainData)
    {
        if (renderMesh == null) return;
        Vector3[] vertices = renderMesh.vertices;
        if (vertices == null || vertices.Length == 0) return;
        float min = float.PositiveInfinity, max = float.NegativeInfinity, sum = 0f, sumSq = 0f, maskAbove = 0f;
        float minRadius = float.PositiveInfinity, maxRadius = float.NegativeInfinity;
        bool hasGeneratedData = terrainData.Heights != null && terrainData.Heights.Length == vertices.Length && terrainData.RawRadii != null && terrainData.RawRadii.Length == vertices.Length;
        PlanetTerrainSettings settings = hasGeneratedData ? default : GetGeodesicTerrainSettings();
        for (int i = 0; i < vertices.Length; i++)
        {
            float h; float r; float mountainMask;
            if (hasGeneratedData)
            {
                h = terrainData.Heights[i]; r = terrainData.RawRadii[i]; mountainMask = terrainData.MountainMasks != null && i < terrainData.MountainMasks.Length ? terrainData.MountainMasks[i] : 0f;
            }
            else
            {
                Vector3 direction = vertices[i].sqrMagnitude > 1e-10f ? vertices[i].normalized : Vector3.up;
                PlanetTerrainSample sample = PlanetTerrainSampler.Evaluate(direction, DerivedTerrainSeed, settings);
                geodesicTerrainNoiseEvaluationCount++; geodesicDiagnosticOnlyTerrainEvaluationCount++;
                h = enableGeodesicTerrainDisplacement ? sample.HeightOffset : 0f; r = BasePlanetRadius + h; mountainMask = sample.MountainMask;
            }
            min = Mathf.Min(min, h); max = Mathf.Max(max, h); sum += h; sumSq += h * h;
            if (mountainMask > 0f) maskAbove += 1f;
            minRadius = Mathf.Min(minRadius, r); maxRadius = Mathf.Max(maxRadius, r);
        }
        float count = vertices.Length;
        float mean = sum / count;
        float variance = Mathf.Max(0f, (sumSq / count) - mean * mean);
        float stdDev = Mathf.Sqrt(variance);
        MeshCollider collider = GetComponent<MeshCollider>();
        Debug.Log($"[GeodesicTerrainDiagnostics] simulationSubdivision={simulationSubdivision}, simulationCells={GeodesicTopology?.CellCount ?? 0}, renderSubdivision={renderSubdivision}, renderVertices={renderMesh.vertexCount}, renderTriangles={renderMesh.triangles.Length / 3}, heightMinMaxMeanStd={min:F6}/{max:F6}/{mean:F6}/{stdDev:F6}, terrainSeed={DerivedTerrainSeed}, mountainMaskActivePercent={(maskAbove / count) * 100f:F2}, surfaceRadiusMinMax={minRadius:F6}/{maxRadius:F6}, meshBounds={renderMesh.bounds}, colliderBounds={(collider != null ? collider.bounds.ToString() : "<none>")}", this);
    }

    void RebuildGeodesicCellTerrainCache()
    {
        if (GeodesicTopology == null) { geodesicTerrainHeightByCell = null; geodesicNormalizedTerrainByCell = null; return; }
        geodesicTerrainHeightByCell = new float[GeodesicTopology.CellCount];
        geodesicNormalizedTerrainByCell = new float[GeodesicTopology.CellCount];
        geodesicRawTerrainRadius = new float[GeodesicTopology.CellCount];
        float min = GetGeodesicMinimumTerrainOffset(); float max = GetGeodesicMaximumTerrainOffset();
        for (int i = 0; i < GeodesicTopology.CellCount; i++)
        {
            Vector3 direction = GeodesicTopology.CellDirections[i];
            float height = EvaluateGeodesicTerrainHeight(direction);
            geodesicSimulationCellTerrainEvaluationCount++;
            geodesicTerrainHeightByCell[i] = height;
            geodesicNormalizedTerrainByCell[i] = max > min ? Mathf.InverseLerp(min, max, height) : 0.5f;
            geodesicRawTerrainRadius[i] = BasePlanetRadius + height;
        }
    }

    public float EvaluateRawGeodesicTerrainRadius(Vector3 direction) => BasePlanetRadius + EvaluateGeodesicTerrainHeight(direction);
    float EvaluateRawGeodesicTerrainRadiusUncounted(Vector3 direction) => BasePlanetRadius + (enableGeodesicTerrainDisplacement ? PlanetTerrainSampler.EvaluateHeight(direction, DerivedTerrainSeed, GetGeodesicTerrainSettings()) : 0f);

    public float GetGeodesicCellRawTerrainRadius(int cellIndex)
    {
        if (geodesicRawTerrainRadius != null && cellIndex >= 0 && cellIndex < geodesicRawTerrainRadius.Length) return geodesicRawTerrainRadius[cellIndex];
        return GeodesicTopology != null && cellIndex >= 0 && cellIndex < GeodesicTopology.CellCount ? EvaluateRawGeodesicTerrainRadius(GeodesicTopology.CellDirections[cellIndex]) : BasePlanetRadius;
    }

    public float GetGeodesicCellSeafloorRadius(int cellIndex)
    {
        if (geodesicSeafloorRadius != null && cellIndex >= 0 && cellIndex < geodesicSeafloorRadius.Length) return geodesicSeafloorRadius[cellIndex];
        return GetGeodesicCellRawTerrainRadius(cellIndex);
    }

    public float GetGeodesicCellBaseWaterDepth(int cellIndex)
    {
        if (geodesicBaseWaterDepth != null && cellIndex >= 0 && cellIndex < geodesicBaseWaterDepth.Length) return geodesicBaseWaterDepth[cellIndex];
        return Mathf.Max(0f, GeodesicSeaLevelRadius - GetGeodesicCellRawTerrainRadius(cellIndex));
    }

    public float GetGeodesicCellDistanceToShore(int cellIndex)
    {
        return geodesicDistanceToShore != null && cellIndex >= 0 && cellIndex < geodesicDistanceToShore.Length ? geodesicDistanceToShore[cellIndex] : -1f;
    }

    public float GetGeodesicCellNormalizedDepth(int cellIndex) => geodesicMaximumOceanDepth > 0f ? Mathf.Clamp01(GetGeodesicCellWaterDepth(cellIndex) / geodesicMaximumOceanDepth) : 0f;
    public float GetGeodesicCellBasinNoiseContribution(int cellIndex) => geodesicBasinNoiseContribution != null && cellIndex >= 0 && cellIndex < geodesicBasinNoiseContribution.Length ? geodesicBasinNoiseContribution[cellIndex] : 0f;
    public string GetGeodesicCellBathymetryRegion(int cellIndex)
    {
        if (geodesicBathymetryRegion == null || cellIndex < 0 || cellIndex >= geodesicBathymetryRegion.Length) return "unknown";
        return geodesicBathymetryRegion[cellIndex].ToString();
    }
    public float GetGeodesicCellContinentalInfluence01(int cellIndex) => geodesicContinentalInfluenceByCell != null && cellIndex >= 0 && cellIndex < geodesicContinentalInfluenceByCell.Length ? geodesicContinentalInfluenceByCell[cellIndex] : 0f;
    public string GetGeodesicCellCoastType(int cellIndex) => geodesicCoastTypeByCell != null && cellIndex >= 0 && cellIndex < geodesicCoastTypeByCell.Length ? geodesicCoastTypeByCell[cellIndex].ToString() : "None";
    public float GetGeodesicCellContinentalShelfInfluence01(int cellIndex) => geodesicContinentalShelfInfluenceByCell != null && cellIndex >= 0 && cellIndex < geodesicContinentalShelfInfluenceByCell.Length ? geodesicContinentalShelfInfluenceByCell[cellIndex] : 1f;
    public int GetGeodesicCellLandComponentId(int cellIndex) => geodesicLandComponentIdByCell != null && cellIndex >= 0 && cellIndex < geodesicLandComponentIdByCell.Length ? geodesicLandComponentIdByCell[cellIndex] : -1;
    public float GetGeodesicCellLocalShelfWidthMultiplier(int cellIndex) => geodesicLocalShelfWidthMultiplierByCell != null && cellIndex >= 0 && cellIndex < geodesicLocalShelfWidthMultiplierByCell.Length ? geodesicLocalShelfWidthMultiplierByCell[cellIndex] : 1f;
    public float GetGeodesicCellLocalShelfDepth(int cellIndex) => geodesicLocalShelfDepthByCell != null && cellIndex >= 0 && cellIndex < geodesicLocalShelfDepthByCell.Length ? geodesicLocalShelfDepthByCell[cellIndex] : geodesicShelfDepth;
    public string GetGeodesicCellShelfProfileType(int cellIndex) => geodesicShelfProfileTypeByCell != null && cellIndex >= 0 && cellIndex < geodesicShelfProfileTypeByCell.Length ? geodesicShelfProfileTypeByCell[cellIndex].ToString() : "None";
    public float GetGeodesicCellOceanicIslandShelfInfluence01(int cellIndex) => geodesicOceanicIslandShelfInfluenceByCell != null && cellIndex >= 0 && cellIndex < geodesicOceanicIslandShelfInfluenceByCell.Length ? geodesicOceanicIslandShelfInfluenceByCell[cellIndex] : 0f;
    public float GetGeodesicCellContinentalProfileShelfWidthDegrees(int cellIndex) => geodesicContinentalProfileShelfWidthByCell != null && cellIndex >= 0 && cellIndex < geodesicContinentalProfileShelfWidthByCell.Length ? geodesicContinentalProfileShelfWidthByCell[cellIndex] / Mathf.Max(0.0001f, BasePlanetRadius) * Mathf.Rad2Deg : geodesicShelfWidthDegrees;
    public float GetGeodesicCellFinalShelfWidthDegrees(int cellIndex) => geodesicFinalShelfWidthByCell != null && cellIndex >= 0 && cellIndex < geodesicFinalShelfWidthByCell.Length ? geodesicFinalShelfWidthByCell[cellIndex] / Mathf.Max(0.0001f, BasePlanetRadius) * Mathf.Rad2Deg : geodesicShelfWidthDegrees;
    public float GetGeodesicCellApproxCellSpacingDegrees(int cellIndex) => geodesicApproxCellSpacingDegreesByCell != null && cellIndex >= 0 && cellIndex < geodesicApproxCellSpacingDegreesByCell.Length ? geodesicApproxCellSpacingDegreesByCell[cellIndex] : EstimateMeanGeodesicCellSpacingDegrees();
    public float GetGeodesicCellRidgeContribution(int cellIndex) => geodesicOceanicRidgeReliefByCell != null && cellIndex >= 0 && cellIndex < geodesicOceanicRidgeReliefByCell.Length ? geodesicOceanicRidgeReliefByCell[cellIndex] : 0f;
    public float GetGeodesicCellPlateauContribution(int cellIndex) => geodesicOceanicPlateauReliefByCell != null && cellIndex >= 0 && cellIndex < geodesicOceanicPlateauReliefByCell.Length ? geodesicOceanicPlateauReliefByCell[cellIndex] : 0f;
    public float GetGeodesicCellSeamountContribution(int cellIndex) => geodesicSeamountReliefByCell != null && cellIndex >= 0 && cellIndex < geodesicSeamountReliefByCell.Length ? geodesicSeamountReliefByCell[cellIndex] : 0f;
    public float GetGeodesicCellTotalOceanicReliefContribution(int cellIndex) => geodesicTotalOceanicReliefByCell != null && cellIndex >= 0 && cellIndex < geodesicTotalOceanicReliefByCell.Length ? geodesicTotalOceanicReliefByCell[cellIndex] : 0f;

    public float GetGeodesicSeafloorRadiusAtDirection(Vector3 direction)
    {
        if (enableGeodesicTerrainDisplacement) geodesicTerrainNoiseEvaluationCount++;
        Vector3 d = direction.sqrMagnitude > 1e-10f ? direction.normalized : Vector3.up;
        float raw = EvaluateRawGeodesicTerrainRadiusUncounted(d);
        return GetGeodesicSeafloorRadiusAtDirectionUnprofiled(d, raw);
    }

    float GetGeodesicSeafloorRadiusAtDirectionUnprofiled(Vector3 d, float raw)
    {
        if (GeodesicTopology == null || geodesicSeafloorRadius == null || geodesicOceanMask == null || raw >= GeodesicSeaLevelRadius) return raw;
        int nearest = DirectionToGeodesicCell(d);
        if (nearest < 0 || !geodesicOceanMask[nearest]) return raw;
        geodesicBathymetryInterpolationCount++;
        float weighted = geodesicSeafloorRadius[nearest];
        float weightSum = 1f;
        int baseIndex = nearest * 6;
        for (int n = 0; n < GeodesicTopology.NeighborCounts[nearest]; n++)
        {
            int nb = GeodesicTopology.Neighbors6[baseIndex + n];
            if (nb < 0 || nb >= geodesicSeafloorRadius.Length || !geodesicOceanMask[nb]) continue;
            geodesicDirectionCandidateCellsInspected++;
            float dot = Mathf.Clamp(Vector3.Dot(d, GeodesicTopology.CellDirections[nb]), -1f, 1f);
            float w = 1f / Mathf.Max(0.0001f, Mathf.Acos(dot));
            weighted += geodesicSeafloorRadius[nb] * w; weightSum += w;
        }
        return Mathf.Min(raw, weighted / Mathf.Max(0.0001f, weightSum));
    }

    int DirectionToGeodesicCell(Vector3 direction)
    {
        if (GeodesicTopology == null) return -1;
        geodesicDirectionToCellQueryCount++;
        int best = -1; float bestDot = -2f;
        for (int i = 0; i < GeodesicTopology.CellCount; i++) { geodesicDirectionCandidateCellsInspected++; float dot = Vector3.Dot(direction, GeodesicTopology.CellDirections[i]); if (dot > bestDot) { bestDot = dot; best = i; } }
        return best;
    }

    private readonly struct GeodesicSeaLevelCandidate
    {
        public readonly float Radius;
        public readonly float Area;
        public GeodesicSeaLevelCandidate(float radius, float area) { Radius = radius; Area = area; }
    }

    void ResolveGeodesicSeaLevelRadius()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        resolvedGeodesicSeaLevelRadius = BasePlanetRadius + geodesicSeaLevelOffset;
        resolvedGeodesicSeaLevelOffset = resolvedGeodesicSeaLevelRadius - BasePlanetRadius;

        if (GeodesicTopology == null || geodesicRawTerrainRadius == null || geodesicRawTerrainRadius.Length == 0 || geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.ManualOffset)
        {
            watch.Stop();
            geodesicLastOceanWorldSurfaceResolveMilliseconds = 0d;
            Debug.Log($"[GeodesicSeaLevelDiagnostics] mode={geodesicSeaLevelControlMode}, manualOffset={geodesicSeaLevelOffset:F6}, requestedTargetPercent={geodesicTargetOceanCoveragePercent:F3}, oceanWorldMinimumRequestedDepth={geodesicOceanWorldMinimumDepth:F6}, resolvedSeaLevelRadius={resolvedGeodesicSeaLevelRadius:F6}, resolvedSeaLevelOffset={resolvedGeodesicSeaLevelOffset:F6}, targetResolveMs={watch.Elapsed.TotalMilliseconds:F3}", this);
            return;
        }

        int count = geodesicRawTerrainRadius.Length;
        if (geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld)
        {
            resolvedGeodesicSeaLevelRadius = ResolveOceanWorldSurfaceRadiusFromSimulationCells();
            resolvedGeodesicSeaLevelOffset = resolvedGeodesicSeaLevelRadius - BasePlanetRadius;
            watch.Stop();
            geodesicLastOceanWorldSurfaceResolveMilliseconds = watch.Elapsed.TotalMilliseconds;
            Debug.Log($"[GeodesicSeaLevelDiagnostics] mode={geodesicSeaLevelControlMode}, oceanWorldMinimumRequestedDepth={Mathf.Max(0f, geodesicOceanWorldMinimumDepth):F6}, maximumSimulationSolidRadius={geodesicOceanWorldMaxSimulationSolidRadius:F6}, conservativeFineDetailSafetyMargin={GetOceanWorldFineDetailSafetyMargin():F6}, resolvedSeaLevelRadius={resolvedGeodesicSeaLevelRadius:F6}, resolvedSeaLevelOffset={resolvedGeodesicSeaLevelOffset:F6}, oceanWorldSurfaceResolveMs={watch.Elapsed.TotalMilliseconds:F3}", this);
            return;
        }

        float targetPercent = Mathf.Clamp(geodesicTargetOceanCoveragePercent, 0f, 100f);
        if (targetPercent <= 0f)
        {
            resolvedGeodesicSeaLevelRadius = MinGeodesicRawTerrainRadius() - 0.000001f;
            resolvedGeodesicSeaLevelOffset = resolvedGeodesicSeaLevelRadius - BasePlanetRadius;
            watch.Stop();
            Debug.Log($"[GeodesicSeaLevelDiagnostics] mode={geodesicSeaLevelControlMode}, manualOffset={geodesicSeaLevelOffset:F6}, requestedTargetPercent={targetPercent:F3}, resolvedSeaLevelRadius={resolvedGeodesicSeaLevelRadius:F6}, resolvedSeaLevelOffset={resolvedGeodesicSeaLevelOffset:F6}, endpoint=all-land, targetResolveMs={watch.Elapsed.TotalMilliseconds:F3}", this);
            return;
        }
        if (targetPercent >= 100f)
        {
            resolvedGeodesicSeaLevelRadius = MaxGeodesicRawTerrainRadius() + 0.000001f;
            resolvedGeodesicSeaLevelOffset = resolvedGeodesicSeaLevelRadius - BasePlanetRadius;
            watch.Stop();
            Debug.Log($"[GeodesicSeaLevelDiagnostics] mode={geodesicSeaLevelControlMode}, manualOffset={geodesicSeaLevelOffset:F6}, requestedTargetPercent={targetPercent:F3}, resolvedSeaLevelRadius={resolvedGeodesicSeaLevelRadius:F6}, resolvedSeaLevelOffset={resolvedGeodesicSeaLevelOffset:F6}, endpoint=all-ocean, targetResolveMs={watch.Elapsed.TotalMilliseconds:F3}", this);
            return;
        }

        GeodesicSeaLevelCandidate[] candidates = new GeodesicSeaLevelCandidate[count];
        float totalArea = 0f;
        for (int i = 0; i < count; i++)
        {
            float area = GeodesicTopology.UnitCellAreas[i] * BasePlanetRadius * BasePlanetRadius;
            candidates[i] = new GeodesicSeaLevelCandidate(geodesicRawTerrainRadius[i], area);
            totalArea += area;
        }

        System.Array.Sort(candidates, (a, b) => a.Radius.CompareTo(b.Radius));
        float targetArea = totalArea * (targetPercent / 100f);
        float cumulativeArea = 0f;
        float bestDelta = float.PositiveInfinity;
        int bestSubmergedCount = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            cumulativeArea += candidates[i].Area;
            float delta = Mathf.Abs(cumulativeArea - targetArea);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestSubmergedCount = i + 1;
            }
        }

        if (bestSubmergedCount <= 0) resolvedGeodesicSeaLevelRadius = candidates[0].Radius - 0.000001f;
        else if (bestSubmergedCount >= count) resolvedGeodesicSeaLevelRadius = candidates[count - 1].Radius + 0.000001f;
        else resolvedGeodesicSeaLevelRadius = (candidates[bestSubmergedCount - 1].Radius + candidates[bestSubmergedCount].Radius) * 0.5f;
        resolvedGeodesicSeaLevelOffset = resolvedGeodesicSeaLevelRadius - BasePlanetRadius;

        watch.Stop();
        Debug.Log($"[GeodesicSeaLevelDiagnostics] mode={geodesicSeaLevelControlMode}, manualOffset={geodesicSeaLevelOffset:F6}, requestedTargetPercent={targetPercent:F3}, resolvedSeaLevelRadius={resolvedGeodesicSeaLevelRadius:F6}, resolvedSeaLevelOffset={resolvedGeodesicSeaLevelOffset:F6}, selectedCells={bestSubmergedCount}/{count}, targetResolveMs={watch.Elapsed.TotalMilliseconds:F3}", this);
    }

    float MinGeodesicRawTerrainRadius()
    {
        float min = float.PositiveInfinity;
        for (int i = 0; i < geodesicRawTerrainRadius.Length; i++) min = Mathf.Min(min, geodesicRawTerrainRadius[i]);
        return float.IsInfinity(min) ? BasePlanetRadius : min;
    }

    float MaxGeodesicRawTerrainRadius()
    {
        float max = float.NegativeInfinity;
        for (int i = 0; i < geodesicRawTerrainRadius.Length; i++) max = Mathf.Max(max, geodesicRawTerrainRadius[i]);
        return float.IsInfinity(max) ? BasePlanetRadius : max;
    }


    float ResolveOceanWorldSurfaceRadiusFromSimulationCells()
    {
        geodesicOceanWorldMaxSimulationSolidRadius = MaxGeodesicRawTerrainRadius();
        geodesicOceanWorldMaxRenderSolidRadius = 0f;
        geodesicOceanWorldMaxColliderSolidRadius = 0f;
        geodesicOceanWorldMinSimulationCoverDepth = 0f;
        geodesicOceanWorldMinRenderCoverDepth = 0f;
        geodesicOceanWorldMinColliderCoverDepth = 0f;
        return geodesicOceanWorldMaxSimulationSolidRadius + Mathf.Max(0f, geodesicOceanWorldMinimumDepth) + GetOceanWorldFineDetailSafetyMargin();
    }

    float GetOceanWorldFineDetailSafetyMargin()
    {
        // The water shell is resolved before render/collider meshes are displaced. Add a conservative margin for
        // terrain extrema that can occur between simulation-cell centres; oceanic relief is already sampled on
        // simulation cells and included in geodesicRawTerrainRadius, so it is not added here a second time.
        return enableGeodesicTerrainDisplacement ? Mathf.Max(0f, geodesicFineDetailAmplitude) : 0f;
    }

    static float MaxRadiusFromTerrainData(GeodesicRenderTerrainData data)
    {
        if (data.SurfaceRadii == null || data.SurfaceRadii.Length == 0) return 0f;
        float max = float.NegativeInfinity;
        for (int i = 0; i < data.SurfaceRadii.Length; i++) max = Mathf.Max(max, data.SurfaceRadii[i]);
        return max;
    }

    static float MinCoverDepthFromTerrainData(GeodesicRenderTerrainData data, float waterSurfaceRadius)
    {
        if (data.SurfaceRadii == null || data.SurfaceRadii.Length == 0) return 0f;
        float min = float.PositiveInfinity;
        for (int i = 0; i < data.SurfaceRadii.Length; i++) min = Mathf.Min(min, waterSurfaceRadius - data.SurfaceRadii[i]);
        return float.IsPositiveInfinity(min) ? 0f : min;
    }

    void ValidateOceanWorldGeometryCoverage(GeodesicRenderTerrainData renderData, GeodesicRenderTerrainData colliderData)
    {
        if (geodesicSeaLevelControlMode != GeodesicSeaLevelControlMode.OceanWorld) return;
        geodesicOceanWorldMaxRenderSolidRadius = MaxRadiusFromTerrainData(renderData);
        geodesicOceanWorldMaxColliderSolidRadius = MaxRadiusFromTerrainData(colliderData);
        geodesicOceanWorldMinSimulationCoverDepth = geodesicRawTerrainRadius != null && geodesicRawTerrainRadius.Length > 0 ? resolvedGeodesicSeaLevelRadius - MaxGeodesicRawTerrainRadius() : 0f;
        geodesicOceanWorldMinRenderCoverDepth = MinCoverDepthFromTerrainData(renderData, resolvedGeodesicSeaLevelRadius);
        geodesicOceanWorldMinColliderCoverDepth = MinCoverDepthFromTerrainData(colliderData, resolvedGeodesicSeaLevelRadius);
        float requested = Mathf.Max(0f, geodesicOceanWorldMinimumDepth);
        Debug.Log($"[GeodesicOceanWorldCoverageDiagnostics] requestedMinimumDepth={requested:F6}, maximumSimulationSolidRadius={geodesicOceanWorldMaxSimulationSolidRadius:F6}, maximumRenderSolidRadius={geodesicOceanWorldMaxRenderSolidRadius:F6}, maximumColliderSolidRadius={geodesicOceanWorldMaxColliderSolidRadius:F6}, resolvedWaterSurfaceRadius={resolvedGeodesicSeaLevelRadius:F6}, minCoverDepths(simulation/render/collider)={geodesicOceanWorldMinSimulationCoverDepth:F6}/{geodesicOceanWorldMinRenderCoverDepth:F6}/{geodesicOceanWorldMinColliderCoverDepth:F6}", this);
        const float tolerance = 0.0001f;
        if (geodesicOceanWorldMinSimulationCoverDepth + tolerance < requested || geodesicOceanWorldMinRenderCoverDepth + tolerance < requested || geodesicOceanWorldMinColliderCoverDepth + tolerance < requested)
        {
            Debug.LogWarning($"[GeodesicOceanWorldCoverageDiagnostics] Minimum cover depth fell below requested value. Increase conservative safety margin or resolve the water surface after render/collider terrain sampling. requested={requested:F6}", this);
        }
    }

    void RebuildGeodesicOceanClassification()
    {
        if (GeodesicTopology == null)
        {
            geodesicRawTerrainRadius = null; geodesicSeafloorRadius = null; geodesicBaseWaterDepth = null; geodesicWaterDepth = null; geodesicDistanceToShore = null; geodesicBasinNoiseContribution = null; geodesicBathymetryRegion = null; geodesicOceanMask = null; geodesicOceanNeighborCounts = null; geodesicCoastlineMask = null;
            return;
        }

        int count = GeodesicTopology.CellCount;
        geodesicSeafloorRadius = new float[count]; geodesicBaseWaterDepth = new float[count]; geodesicWaterDepth = new float[count]; geodesicDistanceToShore = new float[count]; geodesicBasinNoiseContribution = new float[count]; geodesicBathymetryRegion = new GeodesicBathymetryRegion[count]; geodesicOceanMask = new bool[count]; geodesicOceanNeighborCounts = new byte[count]; geodesicCoastlineMask = new bool[count];
        geodesicContinentalInfluenceByCell = new float[count]; geodesicLandComponentIdByCell = new int[count]; geodesicContinentalShelfInfluenceByCell = new float[count]; geodesicLocalShelfWidthMultiplierByCell = new float[count]; geodesicLocalShelfDepthByCell = new float[count]; geodesicOceanicIslandShelfInfluenceByCell = new float[count]; geodesicContinentalProfileShelfWidthByCell = new float[count]; geodesicFinalShelfWidthByCell = new float[count]; geodesicApproxCellSpacingDegreesByCell = new float[count]; geodesicCoastTypeByCell = new GeodesicCoastType[count]; geodesicShelfProfileTypeByCell = new GeodesicShelfProfileType[count];
        for (int i = 0; i < count; i++) { geodesicLandComponentIdByCell[i] = -1; geodesicDistanceToShore[i] = GeodesicNoShoreDistance; }

        var reliefWatch = System.Diagnostics.Stopwatch.StartNew();
        GenerateGeodesicOceanicRelief();
        reliefWatch.Stop(); geodesicLastOceanicReliefMilliseconds = reliefWatch.Elapsed.TotalMilliseconds;

        if (geodesicRawTerrainRadius == null || geodesicRawTerrainRadius.Length != count) geodesicRawTerrainRadius = new float[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = GeodesicTopology.CellDirections[i];
            PlanetTerrainSample sample = EvaluateGeodesicTerrainSample(dir);
            geodesicContinentalInfluenceByCell[i] = Mathf.Clamp01(sample.ContinentValue);
            float baseRadius = BasePlanetRadius + (enableGeodesicTerrainDisplacement ? sample.HeightOffset : 0f);
            geodesicRawTerrainRadius[i] = baseRadius + (geodesicTotalOceanicReliefByCell != null ? geodesicTotalOceanicReliefByCell[i] : 0f);
            geodesicSeafloorRadius[i] = geodesicRawTerrainRadius[i];
        }

        ResolveGeodesicSeaLevelRadius();

        for (int i = 0; i < count; i++)
        {
            float raw = geodesicRawTerrainRadius[i];
            bool ocean = enableOcean && (geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld || raw < resolvedGeodesicSeaLevelRadius);
            geodesicOceanMask[i] = ocean; geodesicBaseWaterDepth[i] = ocean ? Mathf.Max(0f, resolvedGeodesicSeaLevelRadius - raw) : 0f; geodesicWaterDepth[i] = geodesicBaseWaterDepth[i];
        }

        int oceanCountBefore = 0;
        for (int i = 0; i < count; i++)
        {
            if (geodesicOceanMask[i]) oceanCountBefore++;
            byte oceanNeighbors = 0; bool coastline = false;
            for (int n = 0; n < GeodesicTopology.NeighborCounts[i]; n++)
            {
                int nb = GeodesicTopology.Neighbors6[i * 6 + n]; if (nb < 0 || nb >= count) continue;
                if (geodesicOceanMask[nb]) oceanNeighbors++;
                if (geodesicOceanMask[nb] != geodesicOceanMask[i]) coastline = true;
            }
            geodesicOceanNeighborCounts[i] = oceanNeighbors;
            geodesicCoastlineMask[i] = geodesicSeaLevelControlMode != GeodesicSeaLevelControlMode.OceanWorld && geodesicOceanMask[i] && coastline;
            if (geodesicCoastlineMask[i]) geodesicDistanceToShore[i] = 0f;
        }

        if (geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld)
        {
            geodesicLastLandComponentMilliseconds = 0d;
            geodesicLastCoastTypeMilliseconds = 0d;
            geodesicLastShorelineDistanceMilliseconds = 0d;
            geodesicLastShelfVariationMilliseconds = 0d;
            geodesicLastShorelineCalculationSkipped = true;
            if (!enableOcean) Debug.LogWarning("[GeodesicOceanWorld] OceanWorld mode is selected but Enable Ocean is false; global ocean generation is inactive and no ocean cells or ocean mesh will be generated.", this);
            var oceanWorldBathWatch = System.Diagnostics.Stopwatch.StartNew();
            ApplyGeodesicOceanWorldBathymetry();
            oceanWorldBathWatch.Stop(); geodesicLastFinalBathymetryMilliseconds = oceanWorldBathWatch.Elapsed.TotalMilliseconds;
        }
        else
        {
            geodesicLastShorelineCalculationSkipped = false;
            var componentWatch = System.Diagnostics.Stopwatch.StartNew();
            AnalyzeGeodesicLandComponents();
            componentWatch.Stop(); geodesicLastLandComponentMilliseconds = componentWatch.Elapsed.TotalMilliseconds;
            var coastWatch = System.Diagnostics.Stopwatch.StartNew();
            ClassifyGeodesicCoasts();
            coastWatch.Stop(); geodesicLastCoastTypeMilliseconds = coastWatch.Elapsed.TotalMilliseconds;
            ComputeGeodesicShoreDistances();
            SmoothGeodesicDistanceField();
            var bathWatch = System.Diagnostics.Stopwatch.StartNew();
            ApplyGeodesicBathymetryProfile();
            bathWatch.Stop(); geodesicLastFinalBathymetryMilliseconds = bathWatch.Elapsed.TotalMilliseconds;
        }
        LogGeodesicBathymetryDiagnostics(oceanCountBefore);
    }


    void ApplyGeodesicOceanWorldBathymetry()
    {
        float maxDepthSafe = Mathf.Max(0f, geodesicMaximumOceanDepth);
        float strength = enableGeodesicBathymetry ? Mathf.Clamp01(geodesicBathymetryStrength) : 0f;
        Vector3 basinOffset = BuildGeodesicVisualSeedOffset(DerivedBathymetrySeed);
        for (int i = 0; i < geodesicOceanMask.Length; i++)
        {
            geodesicDistanceToShore[i] = GeodesicNoShoreDistance;
            geodesicCoastlineMask[i] = false;
            geodesicCoastTypeByCell[i] = GeodesicCoastType.None;
            geodesicLandComponentIdByCell[i] = -1;
            geodesicShelfProfileTypeByCell[i] = GeodesicShelfProfileType.None;
            geodesicContinentalShelfInfluenceByCell[i] = 0f;
            geodesicOceanicIslandShelfInfluenceByCell[i] = 0f;
            geodesicLocalShelfWidthMultiplierByCell[i] = 0f;
            geodesicLocalShelfDepthByCell[i] = 0f;
            geodesicContinentalProfileShelfWidthByCell[i] = 0f;
            geodesicFinalShelfWidthByCell[i] = 0f;
            geodesicApproxCellSpacingDegreesByCell[i] = EstimateMeanGeodesicCellSpacingDegrees();
            if (!geodesicOceanMask[i]) { geodesicWaterDepth[i] = 0f; geodesicSeafloorRadius[i] = geodesicRawTerrainRadius[i]; geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.Land; continue; }
            float baseDepth = Mathf.Max(0f, resolvedGeodesicSeaLevelRadius - geodesicRawTerrainRadius[i]);
            float noise01 = 0.5f + 0.5f * SimpleNoise.Evaluate(GeodesicTopology.CellDirections[i] * Mathf.Max(0.001f, geodesicBasinNoiseScale) + basinOffset);
            float basinLowering = maxDepthSafe * Mathf.Clamp01(geodesicBasinNoiseStrength) * noise01 * strength;
            geodesicBasinNoiseContribution[i] = basinLowering;
            float finalDepth = baseDepth + basinLowering;
            geodesicWaterDepth[i] = finalDepth;
            geodesicSeafloorRadius[i] = Mathf.Max(0.01f, resolvedGeodesicSeaLevelRadius - finalDepth);
            float ridge = geodesicOceanicRidgeReliefByCell != null ? geodesicOceanicRidgeReliefByCell[i] : 0f, plateau = geodesicOceanicPlateauReliefByCell != null ? geodesicOceanicPlateauReliefByCell[i] : 0f, sm = geodesicSeamountReliefByCell != null ? geodesicSeamountReliefByCell[i] : 0f;
            if (sm > .001f) geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.Seamount;
            else if (plateau > .001f) geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.OceanicBankOrPlateau;
            else if (ridge > .001f) geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.Ridge;
            else geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.Basin;
        }
    }

    void GenerateGeodesicOceanicRelief()
    {
        int count = GeodesicTopology.CellCount;
        geodesicOceanicRidgeReliefByCell = new float[count]; geodesicOceanicPlateauReliefByCell = new float[count]; geodesicSeamountReliefByCell = new float[count]; geodesicTotalOceanicReliefByCell = new float[count];
        Vector3 off = BuildGeodesicVisualSeedOffset(DerivedBathymetrySeed + 101);
        for (int i = 0; i < count; i++)
        {
            Vector3 d = GeodesicTopology.CellDirections[i];
            float ridgeNoise = 1f - Mathf.Abs(SimpleNoise.Evaluate(d * Mathf.Max(.001f, geodesicOceanicRidgeScale) + off));
            float ridge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(geodesicOceanicRidgeThreshold, 1f, ridgeNoise)) * Mathf.Max(0f, geodesicOceanicRidgeStrength);
            float plateauNoise = .5f + .5f * SimpleNoise.Evaluate(d * Mathf.Max(.001f, geodesicOceanicPlateauScale) + off * .37f + Vector3.one * 19.7f);
            float plateau = Mathf.SmoothStep(.58f, .86f, plateauNoise) * Mathf.Max(0f, geodesicOceanicPlateauStrength);
            geodesicOceanicRidgeReliefByCell[i] = ridge; geodesicOceanicPlateauReliefByCell[i] = plateau;
        }
        if (enableGeodesicSeamounts && geodesicSeamountAmplitude > 0f && geodesicSeamountDensity > 0f)
        {
            int featureCount = Mathf.Clamp(Mathf.RoundToInt(count * geodesicSeamountDensity * 0.004f), 1, Mathf.Max(1, count / 8));
            var rnd = new System.Random(DerivedBathymetrySeed ^ 0x51EA);
            float radiusRad = Mathf.Max(.0001f, geodesicSeamountRadiusDegrees * Mathf.Deg2Rad);
            for (int f = 0; f < featureCount; f++)
            {
                Vector3 seed = RandomUnitVector(rnd); bool chain = rnd.NextDouble() < geodesicSeamountChainProbability; int members = chain ? Mathf.Max(1, geodesicSeamountChainLength) : 1; Vector3 axis = Vector3.Cross(seed, RandomUnitVector(rnd)).normalized; if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(seed, Vector3.up).normalized;
                for (int m = 0; m < members; m++)
                {
                    Vector3 centre = chain ? Quaternion.AngleAxis((m - (members - 1) * .5f) * geodesicSeamountChainSpacingDegrees, axis) * seed : seed;
                    float amp = geodesicSeamountAmplitude * Mathf.Lerp(1f, .45f, members <= 1 ? 0f : m / (float)(members - 1)) * Mathf.Lerp(.75f, 1.25f, (float)rnd.NextDouble()) + geodesicSeamountEmergenceBias * 0.02f;
                    for (int i = 0; i < count; i++)
                    {
                        float a = Mathf.Acos(Mathf.Clamp(Vector3.Dot(centre, GeodesicTopology.CellDirections[i]), -1f, 1f));
                        if (a > radiusRad * 3f) continue;
                        float t = a / radiusRad; geodesicSeamountReliefByCell[i] = Mathf.Max(geodesicSeamountReliefByCell[i], amp * Mathf.Exp(-t * t));
                    }
                }
            }
        }
        for (int i = 0; i < count; i++) geodesicTotalOceanicReliefByCell[i] = geodesicOceanicRidgeReliefByCell[i] + geodesicOceanicPlateauReliefByCell[i] + geodesicSeamountReliefByCell[i];
    }

    static Vector3 RandomUnitVector(System.Random rnd)
    {
        double z = rnd.NextDouble() * 2.0 - 1.0, a = rnd.NextDouble() * System.Math.PI * 2.0, r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - z * z));
        return new Vector3((float)(r * System.Math.Cos(a)), (float)z, (float)(r * System.Math.Sin(a)));
    }

    void AnalyzeGeodesicLandComponents()
    {
        int count = GeodesicTopology.CellCount, id = 0; int[] q = new int[count];
        for (int i = 0; i < count; i++) if (!geodesicOceanMask[i] && geodesicLandComponentIdByCell[i] < 0)
        {
            int head = 0, tail = 0; q[tail++] = i; geodesicLandComponentIdByCell[i] = id;
            while (head < tail)
            {
                int c = q[head++];
                for (int n = 0; n < GeodesicTopology.NeighborCounts[c]; n++) { int nb = GeodesicTopology.Neighbors6[c * 6 + n]; if (nb >= 0 && nb < count && !geodesicOceanMask[nb] && geodesicLandComponentIdByCell[nb] < 0) { geodesicLandComponentIdByCell[nb] = id; q[tail++] = nb; } }
            }
            id++;
        }
    }

    void ClassifyGeodesicCoasts()
    {
        int count = GeodesicTopology.CellCount;
        for (int i = 0; i < count; i++)
        {
            geodesicCoastTypeByCell[i] = GeodesicCoastType.None;
            geodesicContinentalShelfInfluenceByCell[i] = 1f;
            if (geodesicOceanMask[i] && !geodesicCoastlineMask[i]) continue;

            float sumContinent = 0f, maxContinent = 0f, sumPlateau = 0f, sumOceanic = 0f;
            int adjacentLand = 0;
            if (!geodesicOceanMask[i])
            {
                sumContinent += geodesicContinentalInfluenceByCell[i];
                maxContinent = geodesicContinentalInfluenceByCell[i];
                sumPlateau += geodesicOceanicPlateauReliefByCell != null ? geodesicOceanicPlateauReliefByCell[i] : 0f;
                sumOceanic += geodesicTotalOceanicReliefByCell != null ? geodesicTotalOceanicReliefByCell[i] : 0f;
                adjacentLand = 1;
            }
            for (int n = 0; n < GeodesicTopology.NeighborCounts[i]; n++)
            {
                int nb = GeodesicTopology.Neighbors6[i * 6 + n]; if (nb < 0 || nb >= count || geodesicOceanMask[nb]) continue;
                float ci = geodesicContinentalInfluenceByCell[nb];
                sumContinent += ci; maxContinent = Mathf.Max(maxContinent, ci);
                sumPlateau += geodesicOceanicPlateauReliefByCell != null ? geodesicOceanicPlateauReliefByCell[nb] : 0f;
                sumOceanic += geodesicTotalOceanicReliefByCell != null ? geodesicTotalOceanicReliefByCell[nb] : 0f;
                adjacentLand++;
            }
            if (adjacentLand <= 0) continue;
            float meanContinent = sumContinent / adjacentLand;
            float meanPlateau = sumPlateau / adjacentLand;
            float meanOceanic = sumOceanic / adjacentLand;
            bool strongContinental = meanContinent >= .52f || maxContinent >= .68f;
            bool broadPlateau = meanPlateau >= .01f;
            bool trueOceanicFeature = meanOceanic >= .015f && meanContinent < .38f && !broadPlateau;
            GeodesicCoastType type;
            float continentalInfluence;
            if (strongContinental) { type = GeodesicCoastType.ContinentalMargin; continentalInfluence = 1f; }
            else if (broadPlateau) { type = GeodesicCoastType.ContinentalFragmentOrPlateau; continentalInfluence = .9f; }
            else if (trueOceanicFeature) { type = GeodesicCoastType.OceanicIsland; continentalInfluence = 0f; }
            else { type = GeodesicCoastType.MixedMargin; continentalInfluence = .75f; }
            geodesicCoastTypeByCell[i] = type;
            geodesicContinentalShelfInfluenceByCell[i] = continentalInfluence;
        }
    }

    void ComputeGeodesicShoreDistances()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        float[] legacyDistances = null;
        double legacyMs = 0d;
        if (validateOptimizedGeodesicShoreDistances)
        {
            legacyDistances = (float[])geodesicDistanceToShore.Clone();
            var legacyWatch = System.Diagnostics.Stopwatch.StartNew();
            ComputeGeodesicShoreDistancesLegacy(legacyDistances);
            legacyWatch.Stop();
            legacyMs = legacyWatch.Elapsed.TotalMilliseconds;
        }
#endif
        ComputeGeodesicShoreDistancesDijkstra(geodesicDistanceToShore);
        watch.Stop();
        geodesicLastShorelineDistanceMilliseconds = watch.Elapsed.TotalMilliseconds;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (validateOptimizedGeodesicShoreDistances && legacyDistances != null)
        {
            const float tolerance = 0.0001f;
            double sumDeviation = 0d;
            float maxDeviation = 0f;
            int compared = 0;
            int exceeding = 0;
            for (int i = 0; i < geodesicDistanceToShore.Length; i++)
            {
                if (!geodesicOceanMask[i]) continue;
                float a = legacyDistances[i];
                float b = geodesicDistanceToShore[i];
                float deviation = (a < 0f && b < 0f) ? 0f : Mathf.Abs(a - b);
                sumDeviation += deviation;
                if (deviation > maxDeviation) maxDeviation = deviation;
                if (deviation > tolerance) exceeding++;
                compared++;
            }
            double meanDeviation = compared > 0 ? sumDeviation / compared : 0d;
            Debug.Log($"[GeodesicShoreDistanceValidation] oldMs={legacyMs:F2}, newMs={geodesicLastShorelineDistanceMilliseconds:F2}, maxAbsDeviation={maxDeviation:G9}, meanDeviation={meanDeviation:G9}, cellsExceedingTolerance={exceeding}, tolerance={tolerance:G9}", this);
        }
#endif
    }

    void ComputeGeodesicShoreDistancesDijkstra(float[] distances)
    {
        int count = GeodesicTopology.CellCount;
        var heap = new GeodesicDistanceMinHeap(Mathf.Max(16, count / 8));
        for (int i = 0; i < count; i++)
        {
            if (geodesicOceanMask[i] && distances[i] == 0f) heap.Push(i, 0f);
        }
        while (heap.TryPop(out int current, out float best))
        {
            if (current < 0 || current >= count || !geodesicOceanMask[current]) continue;
            if (distances[current] < 0f || best > distances[current] + 0.000001f) continue;
            int baseIndex = current * 6;
            for (int n = 0; n < GeodesicTopology.NeighborCounts[current]; n++)
            {
                int nb = GeodesicTopology.Neighbors6[baseIndex + n];
                if (nb < 0 || nb >= count || !geodesicOceanMask[nb]) continue;
                float edge = GeodesicTopology.NeighborAngularDistances6[baseIndex + n] * BasePlanetRadius;
                float next = best + Mathf.Max(0.000001f, edge);
                if (distances[nb] < 0f || next < distances[nb])
                {
                    distances[nb] = next;
                    heap.Push(nb, next);
                }
            }
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void ComputeGeodesicShoreDistancesLegacy(float[] distances)
    {
        int count = GeodesicTopology.CellCount;
        bool[] visited = new bool[count];
        for (int iter = 0; iter < count; iter++)
        {
            int current = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++) if (geodesicOceanMask[i] && !visited[i] && distances[i] >= 0f && distances[i] < best) { best = distances[i]; current = i; }
            if (current < 0) break;
            visited[current] = true;
            for (int n = 0; n < GeodesicTopology.NeighborCounts[current]; n++)
            {
                int nb = GeodesicTopology.Neighbors6[current * 6 + n]; if (nb < 0 || nb >= count || !geodesicOceanMask[nb]) continue;
                float edge = GeodesicTopology.NeighborAngularDistances6[current * 6 + n] * BasePlanetRadius;
                float next = best + Mathf.Max(0.000001f, edge);
                if (distances[nb] < 0f || next < distances[nb]) distances[nb] = next;
            }
        }
    }
#endif

    private sealed class GeodesicDistanceMinHeap
    {
        private struct Entry { public int Cell; public float Distance; public long Order; }
        private Entry[] entries;
        private int count;
        private long nextOrder;
        public GeodesicDistanceMinHeap(int capacity) { entries = new Entry[Mathf.Max(4, capacity)]; }
        public void Push(int cell, float distance)
        {
            if (count == entries.Length) System.Array.Resize(ref entries, entries.Length * 2);
            entries[count] = new Entry { Cell = cell, Distance = distance, Order = nextOrder++ };
            SiftUp(count++);
        }
        public bool TryPop(out int cell, out float distance)
        {
            if (count == 0) { cell = -1; distance = 0f; return false; }
            Entry root = entries[0]; count--; entries[0] = entries[count]; SiftDown(0);
            cell = root.Cell; distance = root.Distance; return true;
        }
        private static bool Less(Entry a, Entry b) => a.Distance < b.Distance || (Mathf.Approximately(a.Distance, b.Distance) && a.Order < b.Order);
        private void Swap(int a, int b) { Entry temp = entries[a]; entries[a] = entries[b]; entries[b] = temp; }
        private void SiftUp(int i) { while (i > 0) { int p = (i - 1) >> 1; if (!Less(entries[i], entries[p])) break; Swap(i, p); i = p; } }
        private void SiftDown(int i) { while (true) { int l = i * 2 + 1, r = l + 1, s = i; if (l < count && Less(entries[l], entries[s])) s = l; if (r < count && Less(entries[r], entries[s])) s = r; if (s == i) break; Swap(i, s); i = s; } }
    }

    void SmoothGeodesicDistanceField()
    {
        int passes = Mathf.Clamp(geodesicBathymetrySmoothPasses, 0, 8); float strength = Mathf.Clamp01(geodesicBathymetrySmoothStrength);
        if (passes <= 0 || strength <= 0f) return;
        int count = GeodesicTopology.CellCount; float[] temp = new float[count];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                if (!geodesicOceanMask[i] || geodesicDistanceToShore[i] <= 0f) { temp[i] = geodesicDistanceToShore[i]; continue; }
                float sum = geodesicDistanceToShore[i]; int c = 1;
                for (int n = 0; n < GeodesicTopology.NeighborCounts[i]; n++) { int nb = GeodesicTopology.Neighbors6[i * 6 + n]; if (nb >= 0 && nb < count && geodesicOceanMask[nb] && geodesicDistanceToShore[nb] >= 0f) { sum += geodesicDistanceToShore[nb]; c++; } }
                temp[i] = Mathf.Lerp(geodesicDistanceToShore[i], sum / c, strength);
            }
            System.Array.Copy(temp, geodesicDistanceToShore, count);
        }
    }

    void ApplyGeodesicBathymetryProfile()
    {
        float continentalWidthMean = Mathf.Max(0.0001f, geodesicShelfWidthDegrees * Mathf.Deg2Rad * BasePlanetRadius);
        float islandWidthMean = Mathf.Max(0.0001f, geodesicOceanicIslandShelfWidthDegrees * Mathf.Deg2Rad * BasePlanetRadius);
        float maxDepthSafe = Mathf.Max(0f, geodesicMaximumOceanDepth);
        float preserve = Mathf.Max(0f, geodesicShorelinePreservationDegrees * Mathf.Deg2Rad * BasePlanetRadius);
        float strength = enableGeodesicBathymetry ? Mathf.Clamp01(geodesicBathymetryStrength) : 0f;
        Vector3 basinOffset = BuildGeodesicVisualSeedOffset(DerivedBathymetrySeed);
        Vector3 widthOffset = BuildGeodesicVisualSeedOffset(DerivedBathymetrySeed + 313);
        Vector3 depthOffset = BuildGeodesicVisualSeedOffset(DerivedBathymetrySeed + 719);
        float meanSpacingDegrees = EstimateMeanGeodesicCellSpacingDegrees();
        var variationWatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < geodesicOceanMask.Length; i++)
        {
            Vector3 dir = GeodesicTopology.CellDirections[i];
            float widthNoise01 = 0.5f + 0.5f * SimpleNoise.Evaluate(dir * Mathf.Max(.001f, geodesicContinentalShelfWidthVariationScale) + widthOffset);
            float independentDepthNoise01 = 0.5f + 0.5f * SimpleNoise.Evaluate(dir * Mathf.Max(.001f, geodesicContinentalShelfDepthVariationScale) + depthOffset);
            float corr = Mathf.Clamp(geodesicShelfWidthDepthCorrelation, -1f, 1f);
            float depthNoise01 = corr >= 0f ? Mathf.Lerp(independentDepthNoise01, widthNoise01, corr) : Mathf.Lerp(independentDepthNoise01, 1f - widthNoise01, -corr);
            float minWidthMul = Mathf.Min(geodesicContinentalShelfMinWidthMultiplier, geodesicContinentalShelfMaxWidthMultiplier);
            float maxWidthMul = Mathf.Max(geodesicContinentalShelfMinWidthMultiplier, geodesicContinentalShelfMaxWidthMultiplier);
            float minDepthMul = Mathf.Min(geodesicContinentalShelfMinDepthMultiplier, geodesicContinentalShelfMaxDepthMultiplier);
            float maxDepthMul = Mathf.Max(geodesicContinentalShelfMinDepthMultiplier, geodesicContinentalShelfMaxDepthMultiplier);
            float variedWidthMul = Mathf.Lerp(minWidthMul, maxWidthMul, widthNoise01);
            float variedDepthMul = Mathf.Lerp(minDepthMul, maxDepthMul, depthNoise01);
            float widthMul = Mathf.Lerp(1f, variedWidthMul, Mathf.Clamp01(geodesicContinentalShelfWidthVariationStrength));
            float depthMul = Mathf.Lerp(1f, variedDepthMul, Mathf.Clamp01(geodesicContinentalShelfDepthVariationStrength));
            float continentalWidth = continentalWidthMean * Mathf.Max(0f, widthMul);
            float continentalDepth = geodesicShelfDepth * Mathf.Max(0f, depthMul);
            float oceanicBlend = ResolveOceanicIslandProfileInfluence(i);
            float islandVarNoise = 0.5f + 0.5f * SimpleNoise.Evaluate(dir * Mathf.Max(.001f, geodesicContinentalShelfWidthVariationScale) + widthOffset * .41f);
            float islandWidthMul = Mathf.Max(0f, 1f + (islandVarNoise - 0.5f) * 2f * geodesicOceanicIslandShelfVariationStrength);
            float finalWidth = Mathf.Lerp(continentalWidth, islandWidthMean * islandWidthMul, oceanicBlend);
            float finalDepth = Mathf.Lerp(continentalDepth, geodesicOceanicIslandShelfDepth, oceanicBlend);
            geodesicOceanicIslandShelfInfluenceByCell[i] = oceanicBlend;
            geodesicContinentalProfileShelfWidthByCell[i] = continentalWidth;
            geodesicFinalShelfWidthByCell[i] = finalWidth;
            geodesicApproxCellSpacingDegreesByCell[i] = EstimateLocalGeodesicCellSpacingDegrees(i, meanSpacingDegrees);
            geodesicLocalShelfWidthMultiplierByCell[i] = finalWidth / continentalWidthMean;
            geodesicLocalShelfDepthByCell[i] = finalDepth;
        }
        variationWatch.Stop(); geodesicLastShelfVariationMilliseconds = variationWatch.Elapsed.TotalMilliseconds;
        float maxShore = 0f; for (int i = 0; i < geodesicDistanceToShore.Length; i++) if (geodesicOceanMask[i]) maxShore = Mathf.Max(maxShore, geodesicDistanceToShore[i]);
        for (int i = 0; i < geodesicOceanMask.Length; i++)
        {
            if (!geodesicOceanMask[i]) { geodesicSeafloorRadius[i] = geodesicRawTerrainRadius[i]; geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.Land; continue; }
            float d = Mathf.Max(0f, geodesicDistanceToShore[i]);
            float shelfWidth = Mathf.Max(.000001f, geodesicFinalShelfWidthByCell[i]);
            float shelfDepthSafe = Mathf.Clamp(geodesicLocalShelfDepthByCell[i], 0f, maxDepthSafe);
            float oceanicBlend = geodesicOceanicIslandShelfInfluenceByCell != null ? geodesicOceanicIslandShelfInfluenceByCell[i] : 0f;
            float exponentNoise = SimpleNoise.Evaluate(GeodesicTopology.CellDirections[i] * Mathf.Max(.001f, geodesicShelfVariationScale) + widthOffset * .53f);
            float continentalExponent = Mathf.Max(.01f, geodesicContinentalSlopeExponent * (1f + exponentNoise * geodesicContinentalSlopeVariationStrength));
            float exponent = Mathf.Lerp(continentalExponent, geodesicOceanicIslandSlopeExponent, oceanicBlend);
            float shelfT = Mathf.Clamp01(d / shelfWidth);
            float shelfTarget = shelfDepthSafe * Mathf.SmoothStep(0f, 1f, shelfT);
            float deepRange = Mathf.Max(shelfWidth, maxShore - shelfWidth);
            float slopeT = Mathf.Pow(Mathf.Clamp01((d - shelfWidth) / deepRange), exponent);
            float target = Mathf.Lerp(shelfTarget, maxDepthSafe, slopeT);
            float noise01 = 0.5f + 0.5f * SimpleNoise.Evaluate(GeodesicTopology.CellDirections[i] * Mathf.Max(0.001f, geodesicBasinNoiseScale) + basinOffset);
            float modulation = 1f + (noise01 - 0.5f) * 2f * Mathf.Clamp01(geodesicBasinNoiseStrength) * Mathf.Clamp01(slopeT + 0.25f * shelfT);
            geodesicBasinNoiseContribution[i] = modulation - 1f;
            target = Mathf.Clamp(target * modulation, 0f, maxDepthSafe);
            float preserveBlend = preserve <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / preserve));
            float finalDepth = Mathf.Clamp(Mathf.Max(geodesicBaseWaterDepth[i], Mathf.Lerp(geodesicBaseWaterDepth[i], target, preserveBlend * strength)), 0f, maxDepthSafe);
            geodesicWaterDepth[i] = finalDepth; geodesicSeafloorRadius[i] = Mathf.Max(0.01f, resolvedGeodesicSeaLevelRadius - finalDepth);
            float ridge = geodesicOceanicRidgeReliefByCell != null ? geodesicOceanicRidgeReliefByCell[i] : 0f, plateau = geodesicOceanicPlateauReliefByCell != null ? geodesicOceanicPlateauReliefByCell[i] : 0f, sm = geodesicSeamountReliefByCell != null ? geodesicSeamountReliefByCell[i] : 0f;
            if (sm > .001f) geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.Seamount;
            else if (plateau > .001f) geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.OceanicBankOrPlateau;
            else if (ridge > .001f) geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.Ridge;
            else if (d <= shelfWidth && oceanicBlend < .5f) geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.ContinentalShelf;
            else if (d <= shelfWidth) geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.OceanicIslandMargin;
            else geodesicBathymetryRegion[i] = GeodesicBathymetryRegion.Basin;
        }
    }

    float ResolveOceanicIslandProfileInfluence(int cellIndex)
    {
        GeodesicCoastType coastType = geodesicCoastTypeByCell != null && cellIndex >= 0 && cellIndex < geodesicCoastTypeByCell.Length ? geodesicCoastTypeByCell[cellIndex] : GeodesicCoastType.None;
        if (!enableGeodesicOceanicIslandMargins || coastType == GeodesicCoastType.None) { SetShelfProfile(cellIndex, GeodesicShelfProfileType.Continental); return 0f; }
        if (coastType == GeodesicCoastType.OceanicIsland) { SetShelfProfile(cellIndex, GeodesicShelfProfileType.OceanicIsland); return 1f; }
        if (coastType == GeodesicCoastType.MixedMargin)
        {
            float relief = geodesicTotalOceanicReliefByCell != null ? Mathf.Clamp01(geodesicTotalOceanicReliefByCell[cellIndex] / Mathf.Max(0.0001f, geodesicSeamountAmplitude + geodesicOceanicRidgeStrength + geodesicOceanicPlateauStrength)) : 0f;
            float blend = Mathf.Clamp01(geodesicMixedMarginOceanicBlendStrength) * relief;
            SetShelfProfile(cellIndex, blend > 0.001f ? GeodesicShelfProfileType.Mixed : GeodesicShelfProfileType.Continental);
            return blend;
        }
        SetShelfProfile(cellIndex, coastType == GeodesicCoastType.ContinentalFragmentOrPlateau ? GeodesicShelfProfileType.FragmentOrPlateau : GeodesicShelfProfileType.Continental);
        return 0f;
    }

    void SetShelfProfile(int cellIndex, GeodesicShelfProfileType profile)
    {
        if (geodesicShelfProfileTypeByCell != null && cellIndex >= 0 && cellIndex < geodesicShelfProfileTypeByCell.Length) geodesicShelfProfileTypeByCell[cellIndex] = profile;
    }

    float EstimateMeanGeodesicCellSpacingDegrees()
    {
        if (GeodesicTopology == null || GeodesicTopology.CellCount <= 0) return 0f;
        float meanArea = (4f * Mathf.PI) / GeodesicTopology.CellCount;
        return Mathf.Sqrt(meanArea) * Mathf.Rad2Deg;
    }

    float EstimateLocalGeodesicCellSpacingDegrees(int cellIndex, float fallbackDegrees)
    {
        if (GeodesicTopology == null || cellIndex < 0 || cellIndex >= GeodesicTopology.CellCount) return fallbackDegrees;
        float sum = 0f; int c = 0;
        for (int n = 0; n < GeodesicTopology.NeighborCounts[cellIndex]; n++)
        {
            int nb = GeodesicTopology.Neighbors6[cellIndex * 6 + n];
            if (nb < 0 || nb >= GeodesicTopology.CellCount) continue;
            sum += GeodesicTopology.NeighborAngularDistances6[cellIndex * 6 + n] * Mathf.Rad2Deg; c++;
        }
        return c > 0 ? sum / c : fallbackDegrees;
    }

    void LogGeodesicBathymetryDiagnostics(int oceanCountBefore)
    {
        int count = GeodesicTopology.CellCount, land = 0, ocean = 0, coast = 0, shallow = 0, shelf = 0, slope = 0, deep = 0; float areaSum = 0f, oceanArea = 0f, depthArea = 0f, minD = float.PositiveInfinity, maxD = 0f, minFloor = float.PositiveInfinity, maxFloor = float.NegativeInfinity, maxShore = 0f; List<float> depths = new List<float>();
        for (int i = 0; i < count; i++)
        {
            float area = GeodesicTopology.UnitCellAreas[i] * BasePlanetRadius * BasePlanetRadius; areaSum += area;
            if (!geodesicOceanMask[i]) { land++; continue; }
            ocean++; oceanArea += area; coast += geodesicCoastlineMask[i] ? 1 : 0; float d = geodesicWaterDepth[i]; depths.Add(d); depthArea += d * area; minD = Mathf.Min(minD, d); maxD = Mathf.Max(maxD, d); minFloor = Mathf.Min(minFloor, geodesicSeafloorRadius[i]); maxFloor = Mathf.Max(maxFloor, geodesicSeafloorRadius[i]); maxShore = Mathf.Max(maxShore, geodesicDistanceToShore[i]); if (geodesicBathymetryRegion[i] == GeodesicBathymetryRegion.ContinentalShelf) shelf++; else if (geodesicBathymetryRegion[i] == GeodesicBathymetryRegion.OceanicIslandMargin) shallow++; else if (geodesicBathymetryRegion[i] == GeodesicBathymetryRegion.Basin) deep++; else slope++;
        }
        depths.Sort(); float P(float q) => depths.Count == 0 ? 0f : depths[Mathf.Clamp(Mathf.RoundToInt((depths.Count - 1) * q), 0, depths.Count - 1)]; if (ocean == 0) minD = minFloor = maxFloor = 0f;
        int components = 0, continentalCoasts = 0, fragmentCoasts = 0, mixedCoasts = 0, islandCoasts = 0, ridgeCells = 0, plateauCells = 0, seamountCells = 0, openOceanShallows = 0; float minWidth = float.PositiveInfinity, maxWidth = 0f, sumWidth = 0f, minDepthApplied = float.PositiveInfinity, maxDepthApplied = 0f, sumDepthApplied = 0f;
        int belowOneCell = 0, belowQuarterCell = 0; int[] profileCounts = new int[5]; float[] profileMin = new float[5], profileMax = new float[5], profileSum = new float[5]; for (int p = 0; p < profileMin.Length; p++) profileMin[p] = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (geodesicLandComponentIdByCell != null && geodesicLandComponentIdByCell[i] >= components) components = geodesicLandComponentIdByCell[i] + 1;
            if (geodesicCoastlineMask[i])
            {
                if (geodesicCoastTypeByCell[i] == GeodesicCoastType.ContinentalMargin) continentalCoasts++; else if (geodesicCoastTypeByCell[i] == GeodesicCoastType.ContinentalFragmentOrPlateau) fragmentCoasts++; else if (geodesicCoastTypeByCell[i] == GeodesicCoastType.MixedMargin) mixedCoasts++; else if (geodesicCoastTypeByCell[i] == GeodesicCoastType.OceanicIsland) islandCoasts++;
                float widthDeg = geodesicFinalShelfWidthByCell != null ? geodesicFinalShelfWidthByCell[i] / Mathf.Max(.0001f, BasePlanetRadius) * Mathf.Rad2Deg : geodesicShelfWidthDegrees;
                float spacing = geodesicApproxCellSpacingDegreesByCell != null ? geodesicApproxCellSpacingDegreesByCell[i] : EstimateMeanGeodesicCellSpacingDegrees();
                if (widthDeg < spacing) belowOneCell++;
                if (widthDeg < spacing * .25f) belowQuarterCell++;
                int profile = geodesicShelfProfileTypeByCell != null ? Mathf.Clamp((int)geodesicShelfProfileTypeByCell[i], 0, profileCounts.Length - 1) : 0; profileCounts[profile]++; profileMin[profile] = Mathf.Min(profileMin[profile], widthDeg); profileMax[profile] = Mathf.Max(profileMax[profile], widthDeg); profileSum[profile] += widthDeg;
            }
            if (!geodesicOceanMask[i]) continue;
            float width = geodesicLocalShelfWidthMultiplierByCell != null ? geodesicLocalShelfWidthMultiplierByCell[i] : 1f; minWidth = Mathf.Min(minWidth, width); maxWidth = Mathf.Max(maxWidth, width); sumWidth += width;
            float depthApplied = geodesicLocalShelfDepthByCell != null ? geodesicLocalShelfDepthByCell[i] : geodesicShelfDepth; minDepthApplied = Mathf.Min(minDepthApplied, depthApplied); maxDepthApplied = Mathf.Max(maxDepthApplied, depthApplied); sumDepthApplied += depthApplied;
            if (geodesicOceanicRidgeReliefByCell != null && geodesicOceanicRidgeReliefByCell[i] > .001f) ridgeCells++;
            if (geodesicOceanicPlateauReliefByCell != null && geodesicOceanicPlateauReliefByCell[i] > .001f) plateauCells++;
            if (geodesicSeamountReliefByCell != null && geodesicSeamountReliefByCell[i] > .001f) seamountCells++;
            if (!geodesicCoastlineMask[i] && geodesicWaterDepth[i] < geodesicMaximumOceanDepth * .5f && geodesicDistanceToShore[i] > geodesicShelfWidthDegrees * Mathf.Deg2Rad * BasePlanetRadius) openOceanShallows++;
        }
        if (ocean == 0) { minWidth = 0f; minDepthApplied = 0f; }
        string ProfileStats(GeodesicShelfProfileType profile) { int idx = (int)profile; return profileCounts[idx] > 0 ? $"{profileCounts[idx]}:{profileMin[idx]:F3}/{profileSum[idx] / profileCounts[idx]:F3}/{profileMax[idx]:F3}" : "0:0/0/0"; }
        geodesicOceanCellCount = ocean;
        geodesicCoastlineOceanCellCount = coast;
        geodesicMinimumLocalOceanDepth = minD;
        geodesicAreaWeightedMeanLocalOceanDepth = oceanArea > 0f ? depthArea / oceanArea : 0f;
        geodesicMaximumLocalOceanDepth = maxD;
        achievedGeodesicOceanCellCoveragePercent = count > 0 ? ocean * 100f / count : 0f;
        achievedGeodesicOceanAreaCoveragePercent = areaSum > 0f ? oceanArea * 100f / areaSum : 0f;
        Debug.Log($"[GeodesicBathymetryDiagnostics] mode={geodesicSeaLevelControlMode}, manualOffset={geodesicSeaLevelOffset:F6}, requestedTargetPercent={geodesicTargetOceanCoveragePercent:F3}, resolvedSeaLevelRadius={resolvedGeodesicSeaLevelRadius:F6}, resolvedSeaLevelOffset={resolvedGeodesicSeaLevelOffset:F6}, oceanCells={ocean}, coastlineOceanCells={coast}, cellCountOceanPercent={achievedGeodesicOceanCellCoveragePercent:F3}, areaWeightedOceanPercent={achievedGeodesicOceanAreaCoveragePercent:F3}, areaWeightedOceanFraction={(areaSum > 0f ? oceanArea / areaSum : 0f):F6}, finalDepthMinMaxMean={minD:F6}/{maxD:F6}/{(oceanArea > 0f ? depthArea / oceanArea : 0f):F6}, percentilesP25P50P75P90={P(.25f):F6}/{P(.5f):F6}/{P(.75f):F6}/{P(.9f):F6}, categoryCounts(islandMargin/continentalShelf/oceanicRelief/basin)={shallow}/{shelf}/{slope}/{deep}, categoryAreaFractionApprox={shallow / (float)Mathf.Max(1, ocean):F3}/{shelf / (float)Mathf.Max(1, ocean):F3}/{slope / (float)Mathf.Max(1, ocean):F3}/{deep / (float)Mathf.Max(1, ocean):F3}, seafloorRadiusMinMax={minFloor:F6}/{maxFloor:F6}, maxShoreDistance={maxShore:F6}, bathymetrySeed={DerivedBathymetrySeed}, simulationSubdivision={geodesicSimulationSubdivisionLevel}, renderSubdivision={geodesicRenderSubdivisionLevel}, landComponents={components}, coastlineByType(continental/fragment/mixed/oceanicIsland)={continentalCoasts}/{fragmentCoasts}/{mixedCoasts}/{islandCoasts}, shelfProfileCountsWidthDegMinMeanMax(none/continental/fragment/mixed/island)={ProfileStats(GeodesicShelfProfileType.None)}|{ProfileStats(GeodesicShelfProfileType.Continental)}|{ProfileStats(GeodesicShelfProfileType.FragmentOrPlateau)}|{ProfileStats(GeodesicShelfProfileType.Mixed)}|{ProfileStats(GeodesicShelfProfileType.OceanicIsland)}, shelfWidthMultiplierMinMeanMax={minWidth:F3}/{(ocean > 0 ? sumWidth / ocean : 0f):F3}/{maxWidth:F3}, shelfBreakDepthMinMeanMax={minDepthApplied:F4}/{(ocean > 0 ? sumDepthApplied / ocean : 0f):F4}/{maxDepthApplied:F4}, configuredWidthMultiplierRange={Mathf.Min(geodesicContinentalShelfMinWidthMultiplier, geodesicContinentalShelfMaxWidthMultiplier):F3}-{Mathf.Max(geodesicContinentalShelfMinWidthMultiplier, geodesicContinentalShelfMaxWidthMultiplier):F3}, configuredDepthMultiplierRange={Mathf.Min(geodesicContinentalShelfMinDepthMultiplier, geodesicContinentalShelfMaxDepthMultiplier):F3}-{Mathf.Max(geodesicContinentalShelfMinDepthMultiplier, geodesicContinentalShelfMaxDepthMultiplier):F3}, widthDepthVariationScales={geodesicContinentalShelfWidthVariationScale:F3}/{geodesicContinentalShelfDepthVariationScale:F3}, coastCellsBelowOneCellShelfWidth={belowOneCell}, coastCellsBelowQuarterCellShelfWidth={belowQuarterCell}, ridgePlateauSeamountCells={ridgeCells}/{plateauCells}/{seamountCells}, openOceanShallowCells={openOceanShallows}, oceanWorldRequestedMinimumDepth={Mathf.Max(0f, geodesicOceanWorldMinimumDepth):F6}, oceanWorldMaxSolidRadii(sim/render/collider)={geodesicOceanWorldMaxSimulationSolidRadius:F6}/{geodesicOceanWorldMaxRenderSolidRadius:F6}/{geodesicOceanWorldMaxColliderSolidRadius:F6}, oceanWorldMinimumCoverDepths(sim/render/collider)={geodesicOceanWorldMinSimulationCoverDepth:F6}/{geodesicOceanWorldMinRenderCoverDepth:F6}/{geodesicOceanWorldMinColliderCoverDepth:F6}, shorelineDistanceCalculationSkipped={geodesicLastShorelineCalculationSkipped}, timingsMs(oceanWorldSurfaceResolve/oceanicRelief/landComponents/coastTypes/shelfVariation/finalBathymetry/shoreline)={geodesicLastOceanWorldSurfaceResolveMilliseconds:F2}/{geodesicLastOceanicReliefMilliseconds:F2}/{geodesicLastLandComponentMilliseconds:F2}/{geodesicLastCoastTypeMilliseconds:F2}/{geodesicLastShelfVariationMilliseconds:F2}/{geodesicLastFinalBathymetryMilliseconds:F2}/{geodesicLastShorelineDistanceMilliseconds:F2}", this);
        if (geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.ManualOffset && Mathf.Abs(Mathf.Clamp(geodesicTargetOceanCoveragePercent, 0f, 100f) - achievedGeodesicOceanAreaCoveragePercent) > 0.05f)
        {
            Debug.LogWarning($"[GeodesicSeaLevelDiagnostics] Geodesic Target Ocean Coverage is inactive because mode=ManualOffset; classification is controlled by manualOffset={geodesicSeaLevelOffset:F6}, resolvedSeaLevelRadius={resolvedGeodesicSeaLevelRadius:F6}, achievedAreaWeightedOceanPercent={achievedGeodesicOceanAreaCoveragePercent:F3}.", this);
        }
        else if (geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.TargetAreaCoverage)
        {
            Debug.LogWarning($"[GeodesicSeaLevelDiagnostics] Geodesic Sea Level Offset is inactive because mode=TargetAreaCoverage; the resolved offset is calculated automatically as {resolvedGeodesicSeaLevelOffset:F6}.", this);
        }
        else if (geodesicSeaLevelControlMode == GeodesicSeaLevelControlMode.OceanWorld)
        {
            Debug.Log("[GeodesicOceanWorld] Global ocean mode: coastline and shelf controls are inactive.", this);
            if (!enableOcean) Debug.LogWarning("[GeodesicOceanWorld] OceanWorld mode is inactive because Enable Ocean is false; no ocean is rendered or simulated.", this);
        }
        if (belowOneCell > 0) Debug.LogWarning($"[GeodesicBathymetryDiagnostics] {belowOneCell} coastline cells have shelf widths below approximate local simulation-cell spacing; meanSpacingDegrees={EstimateMeanGeodesicCellSpacingDegrees():F3}. Narrow values such as 0.3 degrees may intentionally render as effectively shelf-free at subdivision {geodesicSimulationSubdivisionLevel}.", this);
        if (ocean != oceanCountBefore) Debug.LogWarning("[GeodesicBathymetryDiagnostics] Bathymetry changed ocean classification count; this should not happen.", this);
        if (ocean > 0 && shallow == ocean) Debug.LogWarning("[GeodesicBathymetryDiagnostics] All ocean cells are shallow.", this);
        if (ocean > 0 && slope + deep == 0) Debug.LogWarning("[GeodesicBathymetryDiagnostics] No ocean cells reached slope/deep-basin categories.", this);
        if (ocean > 0 && geodesicMaximumOceanDepth > 0f && maxD < geodesicMaximumOceanDepth * 0.85f) Debug.LogWarning("[GeodesicBathymetryDiagnostics] Configured maximum geodesic depth is not approached.", this);
        if (land > 0) for (int i = 0; i < count; i++) if (!geodesicOceanMask[i] && !Mathf.Approximately(geodesicRawTerrainRadius[i], geodesicSeafloorRadius[i])) { Debug.LogWarning("[GeodesicBathymetryDiagnostics] Land cell was displaced by bathymetry pass.", this); break; }
    }

    void BuildGeodesicOceanVisual()
    {
        int oceanSubdivision = Mathf.Clamp(geodesicOceanRenderSubdivisionLevel, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        IcosphereRenderGeometry oceanGeometry = IcosphereRenderGeometryCache.GetOrBuild(oceanSubdivision);
        IcosphereDirectionMapping oceanMapping = GetOrBuildDirectionMapping(oceanGeometry);
        geodesicOceanMesh = IcosphereRenderMeshBuilder.BuildSurfaceMesh(oceanGeometry, resolvedGeodesicSeaLevelRadius, $"Geodesic Ocean Render L{oceanSubdivision}");
        ApplyGeodesicOceanDepthColours(geodesicOceanMesh, oceanGeometry, oceanMapping);
        Transform existing = transform.Find("Geodesic Ocean");
        geodesicOceanObject = existing != null ? existing.gameObject : new GameObject("Geodesic Ocean");
        geodesicOceanObject.transform.SetParent(transform, false);
        geodesicOceanObject.SetActive(true);
        geodesicOceanObject.layer = gameObject.layer; // no collider is added; terrain MeshCollider remains the explicit picking target.
        geodesicOceanMeshFilter = GetOrAddComponent<MeshFilter>(geodesicOceanObject);
        geodesicOceanMeshRenderer = GetOrAddComponent<MeshRenderer>(geodesicOceanObject);
        geodesicOceanMeshFilter.sharedMesh = geodesicOceanMesh;
        EnsureGeodesicOceanMaterial();
        geodesicOceanMeshRenderer.enabled = enableOcean;
        geodesicOceanMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        geodesicOceanMeshRenderer.receiveShadows = false;
        LogOceanRendererDiagnostics("GeodesicIcosphere", geodesicOceanMeshRenderer.sharedMaterial, GetGeodesicOceanAppearanceSample(), false);
    }

    void ApplyGeodesicOceanDepthColours(Mesh oceanSurfaceMesh, IcosphereRenderGeometry geometry, IcosphereDirectionMapping mapping)
    {
        if (oceanSurfaceMesh == null) return;
        Vector3[] vertices = oceanSurfaceMesh.vertices;
        Color[] colors = new Color[vertices.Length];
        float maxDepth = Mathf.Max(0.0001f, geodesicMaximumOceanDepth);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 direction = geometry.UnitVertices != null && i < geometry.VertexCount ? geometry.UnitVertices[i] : (vertices[i].sqrMagnitude > 1e-10f ? vertices[i].normalized : Vector3.up);
            float surfaceRadius = SampleGeodesicSurfaceRadiusMapped(direction, i, mapping);
            float depth01 = Mathf.Clamp01(Mathf.Max(0f, resolvedGeodesicSeaLevelRadius - surfaceRadius) / maxDepth);
            colors[i] = new Color(depth01, depth01, depth01, 1f);
        }
        oceanSurfaceMesh.colors = colors;
    }

    public void ApplySharedOceanAppearance()
    {
        EnsureLegacyOceanMaterial();
    }

    public void ApplyLegacyOceanResourceTint(Color resourceTint, float blend01)
    {
        EnsureOceanAppearanceInitialized();
        if (runtimeOceanMaterial == null) EnsureLegacyOceanMaterial();
        if (runtimeOceanMaterial == null) return;

        OceanAppearanceEvaluation evaluation = OceanAppearanceModel.Evaluate(oceanAppearance, GetLegacyOceanAppearanceSample());
        Color finalColor = Color.Lerp(evaluation.finalColor, resourceTint, Mathf.Clamp01(blend01));
        finalColor.a = evaluation.opacity;
        OceanMaterialBinder.ApplyFinalBaseColor(runtimeOceanMaterial, finalColor);
        if (oceanMeshRenderer != null && oceanMeshRenderer.sharedMaterial != runtimeOceanMaterial)
        {
            Debug.LogWarning($"[OceanVisualDiagnostics] Legacy ocean renderer material was replaced by {GetMaterialName(oceanMeshRenderer.sharedMaterial)}; restoring {runtimeOceanMaterial.name} before applying resource tint.", this);
            oceanMeshRenderer.sharedMaterial = runtimeOceanMaterial;
        }
    }

    void EnsureLegacyOceanMaterial()
    {
        EnsureOceanAppearanceInitialized();
        if (oceanMeshRenderer == null) return;
        if (runtimeOceanMaterial == null)
        {
            runtimeOceanMaterial = oceanMaterial != null
                ? new Material(oceanMaterial) { name = $"{oceanMaterial.name} (Runtime Ocean)" }
                : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "Legacy Ocean (Runtime)" };
        }

        OceanAppearanceSample sample = GetLegacyOceanAppearanceSample();
        OceanAppearanceEvaluation evaluation = OceanAppearanceModel.Evaluate(oceanAppearance, sample);
        OceanMaterialBinder.Apply(runtimeOceanMaterial, oceanAppearance, evaluation);
        oceanMeshRenderer.sharedMaterial = runtimeOceanMaterial;
    }

    static string GetMaterialName(Material material) => material != null ? material.name : "<none>";

    void ReleaseRuntimeOceanMaterial()
    {
        if (runtimeOceanMaterial != null)
        {
            Destroy(runtimeOceanMaterial);
            runtimeOceanMaterial = null;
        }
    }

    void EnsureGeodesicOceanMaterial()
    {
        bool hadValidSharedSettings = oceanAppearance != null && !IsUnsetOceanAppearance(oceanAppearance);
        EnsureOceanAppearanceInitialized();
        if (!hadValidSharedSettings)
        {
            Debug.LogWarning("[OceanVisualDiagnostics] Geodesic ocean requested without valid shared PlanetGenerator.oceanAppearance; restored legacy shared defaults before binding.", this);
        }
        if (geodesicOceanMeshRenderer == null) return;
        if (runtimeGeodesicOceanMaterial == null)
        {
            Shader shader = Shader.Find("SimulaVit/GeodesicOceanURP");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            runtimeGeodesicOceanMaterial = new Material(shader) { name = "Geodesic Ocean (Runtime)" };
        }
        OceanAppearanceSample sample = GetGeodesicOceanAppearanceSample();
        OceanAppearanceEvaluation evaluation = OceanAppearanceModel.Evaluate(oceanAppearance, sample);
        OceanMaterialBinder.Apply(runtimeGeodesicOceanMaterial, oceanAppearance, evaluation);
        geodesicOceanMeshRenderer.sharedMaterial = runtimeGeodesicOceanMaterial;
    }

    void ReleaseGeodesicOceanMaterial()
    {
        if (runtimeGeodesicOceanMaterial != null)
        {
            Destroy(runtimeGeodesicOceanMaterial);
            runtimeGeodesicOceanMaterial = null;
        }
    }

    OceanAppearanceSample GetLegacyOceanAppearanceSample()
    {
        // Temporary compatibility path: legacy chemistry/resource rendering has not yet been
        // migrated into the shared appearance model, so base ocean material binding uses the
        // default zero-oxygenation sample while resource-driven overlays remain legacy-owned.
        return OceanAppearanceSample.Default;
    }

    OceanAppearanceSample GetGeodesicOceanAppearanceSample()
    {
        // Geodesic mode has no migrated O2 state yet. Keep chemistry appearance inputs defaulted.
        return OceanAppearanceSample.Default;
    }

    void LogOceanRendererDiagnostics(string rendererType, Material material, OceanAppearanceSample sample, bool deprecatedFallbackFieldsUsed)
    {
        string settingsSource = $"PlanetGenerator.oceanAppearance ({nameof(OceanAppearanceSettings)})";
        string shaderName = material != null && material.shader != null ? material.shader.name : "<none>";
        OceanAppearanceEvaluation evaluation = OceanAppearanceModel.Evaluate(oceanAppearance, sample);
        string materialName = material != null ? material.name : "<none>";
        string sharedMaterialName = rendererType == "LegacyCubeSphere" && oceanMeshRenderer != null ? GetMaterialName(oceanMeshRenderer.sharedMaterial) : materialName;
        string writtenColors = OceanMaterialBinder.DescribeColorWrites(material, evaluation.finalColor);
        string missingProperties = OceanMaterialBinder.DescribeMissingExpectedProperties(material);
        bool legacyMaterialIntact = rendererType != "LegacyCubeSphere" || oceanMeshRenderer == null || oceanMeshRenderer.sharedMaterial == runtimeOceanMaterial;
        Debug.Log($"[OceanVisualDiagnostics] renderer={rendererType}, material={materialName}, shader={shaderName}, evaluatedFinalColor={evaluation.finalColor}, opacityBound={evaluation.opacity:F3}, shaderColorPropertiesWritten={writtenColors}, expectedPropertiesMissing={missingProperties}, rendererSharedMaterial={sharedMaterialName}, legacyRuntimeMaterialIntact={legacyMaterialIntact}, oxygenation01={sample.oxygenation01:F3}, deprecatedFallbackFieldsUsed={deprecatedFallbackFieldsUsed}", this);
        if (rendererType == "LegacyCubeSphere") StartCoroutine(CheckLegacyOceanMaterialNextFrame(evaluation.finalColor));
    }

    System.Collections.IEnumerator CheckLegacyOceanMaterialNextFrame(Color expectedColor)
    {
        yield return null;
        if (oceanMeshRenderer == null || runtimeOceanMaterial == null) yield break;
        bool materialStillBound = oceanMeshRenderer.sharedMaterial == runtimeOceanMaterial;
        string currentWrites = OceanMaterialBinder.DescribeColorWrites(runtimeOceanMaterial, expectedColor);
        Debug.Log($"[OceanVisualDiagnostics] oneFrameLater materialStillBound={materialStillBound}, runtimeMaterial={runtimeOceanMaterial.name}, rendererSharedMaterial={GetMaterialName(oceanMeshRenderer.sharedMaterial)}, finalColorProperties={currentWrites}", this);
    }

    float GetGeodesicMinimumTerrainOffset() => Mathf.Min(geodesicMinimumTerrainOffset, geodesicMaximumTerrainOffset);
    float GetGeodesicMaximumTerrainOffset() => Mathf.Max(geodesicMinimumTerrainOffset, geodesicMaximumTerrainOffset);

    void EnsureGeodesicSurfaceMaterial()
    {
        if (meshRenderer == null)
        {
            return;
        }

        if (runtimeGeodesicSurfaceMaterial == null)
        {
            Shader shader = Shader.Find("SimulaVit/GeodesicVertexColorURP");
            if (shader == null)
            {
                Debug.LogWarning("[GeodesicPrototype] Vertex-colour shader SimulaVit/GeodesicVertexColorURP was not found; falling back to the existing planet material, which may not display geodesic vertex colours.", this);
                return;
            }

            runtimeGeodesicSurfaceMaterial = new Material(shader) { name = "Geodesic Vertex Colour Surface (Runtime)" };
        }

        meshRenderer.sharedMaterial = runtimeGeodesicSurfaceMaterial;
        runtimeGeodesicSurfaceMaterial.SetFloat("_AmbientStrength", geodesicSurfaceAmbientStrength);
        runtimeGeodesicSurfaceMaterial.SetFloat("_DiffuseStrength", geodesicSurfaceDiffuseStrength);
    }

    void ReleaseGeodesicSurfaceMaterial()
    {
        if (runtimeGeodesicSurfaceMaterial != null)
        {
            if (meshRenderer != null && meshRenderer.sharedMaterial == runtimeGeodesicSurfaceMaterial)
            {
                meshRenderer.sharedMaterial = runtimePlanetMaterial;
            }

            Destroy(runtimeGeodesicSurfaceMaterial);
            runtimeGeodesicSurfaceMaterial = null;
        }
    }

    [ContextMenu("Geodesic Prototype Scaling Test 0-5")]
    public void RunGeodesicPrototypeScalingTest()
    {
        for (int level = 0; level <= GeodesicGridTopology.MaxSupportedSubdivision; level++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            GeodesicGridTopology t = GeodesicGridTopology.Build(level);
            sw.Stop();
            GeodesicGridValidation.Validate(t, out string validation);
            int pentagons = 0; float minArea = float.MaxValue, maxArea = 0f, sumArea = 0f, minDist = float.MaxValue, maxDist = 0f;
            for (int i = 0; i < t.CellCount; i++)
            {
                if (t.IsPentagon[i]) pentagons++;
                minArea = Mathf.Min(minArea, t.UnitCellAreas[i]); maxArea = Mathf.Max(maxArea, t.UnitCellAreas[i]); sumArea += t.UnitCellAreas[i];
                for (int k = 0; k < t.NeighborCounts[i]; k++) { float d = t.NeighborAngularDistances6[i * 6 + k]; minDist = Mathf.Min(minDist, d); maxDist = Mathf.Max(maxDist, d); }
            }
            float hX = GetTerrainHeightAtDirection(Vector3.right);
            float hY = GetTerrainHeightAtDirection(Vector3.up);
            float hDiag = GetTerrainHeightAtDirection(new Vector3(1f, 1f, 1f));
            Debug.Log($"[GeodesicScalingTest] level={level}, expectedCells={GeodesicGridTopology.ExpectedCellCount(level)}, actualCells={t.CellCount}, expectedTriangles={GeodesicGridTopology.ExpectedTriangleCount(level)}, actualTriangles={t.TriangleCount}, pentagons={pentagons}, areaMinMaxMean={minArea:F8}/{maxArea:F8}/{sumArea / t.CellCount:F8}, neighborDistanceMinMax={minDist:F8}/{maxDist:F8}, deterministicTerrainHeights(+X/+Y/diag)={hX:F8}/{hY:F8}/{hDiag:F8}, durationMs={sw.Elapsed.TotalMilliseconds:F2}, validation={validation}", this);
        }
    }

    void RandomizeGenerationSettings()
    {
        if (!useRandomSeed)
        {
            Random.InitState(randomSeed);
        }

        noiseMagnitude = Random.Range(Mathf.Min(noiseMagnitudeRange.x, noiseMagnitudeRange.y), Mathf.Max(noiseMagnitudeRange.x, noiseMagnitudeRange.y));
        noiseRoughness = Random.Range(Mathf.Min(noiseRoughnessRange.x, noiseRoughnessRange.y), Mathf.Max(noiseRoughnessRange.x, noiseRoughnessRange.y));
        oceanCoveragePercent = Random.Range(
            Mathf.Clamp(Mathf.Min(oceanCoverageRange.x, oceanCoverageRange.y), 0f, 100f),
            Mathf.Clamp(Mathf.Max(oceanCoverageRange.x, oceanCoverageRange.y), 0f, 100f)
        );
    }

    void SetupOceanLayer()
    {
        Transform existing = transform.Find("Ocean Layer");
        GameObject oceanObj = existing != null ? existing.gameObject : new GameObject("Ocean Layer");
        oceanObj.transform.SetParent(transform, false);
        oceanObj.layer = gameObject.layer;

        oceanMeshFilter = GetOrAddComponent<MeshFilter>(oceanObj);
        oceanMeshRenderer = GetOrAddComponent<MeshRenderer>(oceanObj);

        if (oceanMesh == null)
        {
            oceanMesh = new Mesh { name = "Planet Ocean" };
        }

        oceanMeshFilter.sharedMesh = oceanMesh;
        EnsureLegacyOceanMaterial();
    }

    void SetupAtmosphereLayer()
    {
        Transform existing = transform.Find("Atmosphere Layer");
        GameObject atmosphereObj = existing != null ? existing.gameObject : new GameObject("Atmosphere Layer");
        atmosphereObj.transform.SetParent(transform, false);
        atmosphereObj.layer = gameObject.layer;

        atmosphereMeshFilter = GetOrAddComponent<MeshFilter>(atmosphereObj);
        atmosphereMeshRenderer = GetOrAddComponent<MeshRenderer>(atmosphereObj);

        if (atmosphereMesh == null)
        {
            atmosphereMesh = new Mesh { name = "Planet Atmosphere" };
        }

        atmosphereMeshFilter.sharedMesh = atmosphereMesh;

        if (atmosphereMaterial != null)
        {
            atmosphereMeshRenderer.sharedMaterial = atmosphereMaterial;
        }

        // Draw atmosphere after planet/ocean if transparency sorting gets awkward.
        atmosphereMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        atmosphereMeshRenderer.receiveShadows = false;
    }

    void GeneratePlanet()
    {
        GetComponent<GeodesicSurfaceTemperatureField>()?.ClearField();
        ClearGeodesicRuntimeVisuals("before legacy generation");
        RestoreLegacyTerrainMaterial();
        AssertLegacyModeClean("legacy generation start");
        int cellCount = PlanetGridIndexing.GetCellCount(resolution);
        string cacheKey = PlanetGenerationCache.BuildPlanetCacheKeyString(this);
        string cachePath = PlanetGenerationCache.BuildPlanetCachePath(cacheKey);
        if (PlanetGenerationCache.TryLoadPlanet(cachePath, cellCount, out PlanetGenerationCache.PlanetData cachedData))
        {
            oceanNoiseThreshold = cachedData.OceanNoiseThreshold;
            generatedSurfaceRadiusByCell = cachedData.FinalTerrainRadii;
            localOceanDepthByCell = cachedData.LocalOceanDepthByCell;
            oceanDistanceToShoreByCell = cachedData.OceanDistanceToShoreByCell;
            oceanMaskByCell = cachedData.OceanMaskByCell;
            ApplyGeneratedPlanetGeometry(cachedData.UnitVertices, cachedData.Triangles, cachedData.FinalTerrainRadii);
            RefreshLegacyIceVisuals("legacy generation complete (cache)");
            PopulateRuntimeDescriptor(cellCount);
            LogPlanetGenerationValidation(GetComponent<MeshCollider>());
            AssertLegacyModeClean("legacy generation complete (cache)");
            LogModeTransitionRendererInventory("after generation", PlanetGridType.LegacyCubeSphere);
            Debug.Log($"[PlanetGenerationCache] Loaded planet generation cache ({cachePath}).");
            return;
        }

        Vector3[] faceDirections =
        {
            Vector3.up,
            Vector3.down,
            Vector3.left,
            Vector3.right,
            Vector3.forward,
            Vector3.back
        };

        List<Vector3> unitVertices = new List<Vector3>();
        List<float> noiseSamples = new List<float>();
        List<int> allTriangles = new List<int>();

        int currentVertexOffset = 0;

        foreach (Vector3 dir in faceDirections)
        {
            CubeFace face = new CubeFace(dir);
            MeshData faceData = face.GenerateMeshData(resolution);

            for (int i = 0; i < faceData.vertices.Length; i++)
            {
                Vector3 pointOnUnitSphere = faceData.vertices[i];
                unitVertices.Add(pointOnUnitSphere);
                noiseSamples.Add(CalculateNoise(pointOnUnitSphere));
            }

            for (int i = 0; i < faceData.triangles.Length; i++)
            {
                allTriangles.Add(faceData.triangles[i] + currentVertexOffset);
            }

            currentVertexOffset += faceData.vertices.Length;
        }

        oceanNoiseThreshold = CalculateNoiseThreshold(noiseSamples, oceanCoveragePercent);
        float seaRadius = GetOceanRadius();
        cellCount = unitVertices.Count;

        float[] finalTerrainRadii = new float[cellCount];

        int[] neighbors = BuildCellNeighborLookup(unitVertices, allTriangles);
        BuildOceanBathymetry(unitVertices, noiseSamples, finalTerrainRadii, seaRadius, neighbors);

        int[] triangles = allTriangles.ToArray();
        ApplyGeneratedPlanetGeometry(unitVertices.ToArray(), triangles, finalTerrainRadii);
        RefreshLegacyIceVisuals("legacy generation complete");
        PlanetGenerationCache.SavePlanet(
            cachePath,
            new PlanetGenerationCache.PlanetData
            {
                UnitVertices = unitVertices.ToArray(),
                Triangles = triangles,
                FinalTerrainRadii = (float[])generatedSurfaceRadiusByCell.Clone(),
                OceanMaskByCell = (byte[])oceanMaskByCell.Clone(),
                LocalOceanDepthByCell = (float[])localOceanDepthByCell.Clone(),
                OceanDistanceToShoreByCell = (float[])oceanDistanceToShoreByCell.Clone(),
                OceanNoiseThreshold = oceanNoiseThreshold
            });
        PopulateRuntimeDescriptor(cellCount);
        LogPlanetGenerationValidation(GetComponent<MeshCollider>());
        AssertLegacyModeClean("legacy generation complete");
        LogModeTransitionRendererInventory("after generation", PlanetGridType.LegacyCubeSphere);
        Debug.Log($"[PlanetGenerationCache] Regenerated planet and saved cache ({cachePath}).");
    }

    public void RefreshLegacyIceVisuals(string reason)
    {
        if (CurrentGridType != PlanetGridType.LegacyCubeSphere)
        {
            GetComponent<PlanetTemperatureIceVisuals>()?.ClearForGeodesicMode();
            return;
        }

        PlanetTemperatureIceVisuals iceVisuals = GetComponent<PlanetTemperatureIceVisuals>();
        if (iceVisuals != null)
        {
            iceVisuals.RebindAndRefreshLegacySurface(reason);
        }

        LogLegacySurfaceIntegrity(reason);
        StartCoroutine(CheckLegacySurfaceIntegrityNextFrame(reason));
    }

    private void LogLegacySurfaceIntegrity(string reason)
    {
        Mesh terrainMesh = meshFilter != null ? meshFilter.sharedMesh : null;
        Material rendererMaterial = meshRenderer != null ? meshRenderer.sharedMaterial : null;
        Texture baseMap = runtimePlanetMaterial != null && runtimePlanetMaterial.HasProperty("_BaseMap") ? runtimePlanetMaterial.GetTexture("_BaseMap") : null;
        Texture mainTex = runtimePlanetMaterial != null && runtimePlanetMaterial.HasProperty("_MainTex") ? runtimePlanetMaterial.GetTexture("_MainTex") : null;
        PlanetTemperatureIceVisuals iceVisuals = GetComponent<PlanetTemperatureIceVisuals>();
        string shaderName = rendererMaterial != null && rendererMaterial.shader != null ? rendererMaterial.shader.name : "<none>";
        string baseMapInfo = DescribeTexture(baseMap);
        string mainTexInfo = DescribeTexture(mainTex);
        string runtimeTextureInfo = DescribeTexture(runtimeSurfaceTexture);
        ColorStats colorStats = CalculateColorStats(terrainMesh);
        int vertexCount = terrainMesh != null ? terrainMesh.vertexCount : 0;
        int uvCount = terrainMesh != null && terrainMesh.uv != null ? terrainMesh.uv.Length : 0;
        Debug.Log($"[LegacySurfaceIntegrity] reason={reason}, meshInstanceId={(terrainMesh != null ? terrainMesh.GetInstanceID() : 0)}, vertexCount={vertexCount}, uvCount={uvCount}, colorCount={colorStats.count}, colorRMinMaxMean={colorStats.minR:F4}/{colorStats.maxR:F4}/{colorStats.meanR:F4}, colorAMinMaxMean={colorStats.minA:F4}/{colorStats.maxA:F4}/{colorStats.meanA:F4}, rendererMaterialInstanceId={(rendererMaterial != null ? rendererMaterial.GetInstanceID() : 0)}, runtimePlanetMaterialInstanceId={(runtimePlanetMaterial != null ? runtimePlanetMaterial.GetInstanceID() : 0)}, shader={shaderName}, baseMap={baseMapInfo}, mainTex={mainTexInfo}, runtimeSurfaceTexture={runtimeTextureInfo}, baseMapMatchesRuntime={baseMap == runtimeSurfaceTexture}, iceVisualsEnabled={(iceVisuals != null && iceVisuals.enabled)}, iceBoundMeshInstanceId={(iceVisuals != null ? iceVisuals.BoundMeshInstanceId : 0)}, iceBoundVertexCount={(iceVisuals != null ? iceVisuals.BoundVertexCount : 0)}, iceAppliedAfterCurrentMeshBinding={(iceVisuals != null && iceVisuals.HasAppliedAfterCurrentMeshBinding)}", this);
    }

    private System.Collections.IEnumerator CheckLegacySurfaceIntegrityNextFrame(string reason)
    {
        yield return null;
        if (CurrentGridType != PlanetGridType.LegacyCubeSphere)
        {
            yield break;
        }

        Mesh terrainMesh = meshFilter != null ? meshFilter.sharedMesh : null;
        Texture baseMap = runtimePlanetMaterial != null && runtimePlanetMaterial.HasProperty("_BaseMap") ? runtimePlanetMaterial.GetTexture("_BaseMap") : null;
        PlanetTemperatureIceVisuals iceVisuals = GetComponent<PlanetTemperatureIceVisuals>();
        int vertexCount = terrainMesh != null ? terrainMesh.vertexCount : 0;
        int uvCount = terrainMesh != null && terrainMesh.uv != null ? terrainMesh.uv.Length : 0;
        ColorStats colorStats = CalculateColorStats(terrainMesh);
        bool fullIceUniform = colorStats.count > 0 && colorStats.minA >= 0.999f && colorStats.maxA >= 0.999f;

        if (meshRenderer != null && runtimePlanetMaterial != null && meshRenderer.sharedMaterial != runtimePlanetMaterial)
        {
            Debug.LogWarning($"[LegacySurfaceIntegrity] oneFrameLater reason={reason}, terrain renderer material drifted to {GetMaterialName(meshRenderer.sharedMaterial)}; expected {runtimePlanetMaterial.name}.", this);
        }

        if (runtimePlanetMaterial != null && baseMap != runtimeSurfaceTexture)
        {
            Debug.LogWarning($"[LegacySurfaceIntegrity] oneFrameLater reason={reason}, _BaseMap is not runtimeSurfaceTexture. baseMap={DescribeTexture(baseMap)}, runtimeSurfaceTexture={DescribeTexture(runtimeSurfaceTexture)}.", this);
        }

        if (vertexCount > 0 && uvCount != vertexCount)
        {
            Debug.LogWarning($"[LegacySurfaceIntegrity] oneFrameLater reason={reason}, UV count {uvCount} does not match vertex count {vertexCount}.", this);
        }

        if (vertexCount > 0 && colorStats.count != vertexCount)
        {
            Debug.LogWarning($"[LegacySurfaceIntegrity] oneFrameLater reason={reason}, color count {colorStats.count} does not match vertex count {vertexCount}; legacy ice shader needs alpha=0 for no ice and alpha=1 for full ice.", this);
        }

        if (fullIceUniform)
        {
            Debug.LogWarning($"[LegacySurfaceIntegrity] oneFrameLater reason={reason}, all legacy terrain vertices have full-ice alpha. Verify temperatures justify global ice before accepting this state.", this);
        }

        if (iceVisuals != null && terrainMesh != null && iceVisuals.BoundMeshInstanceId != terrainMesh.GetInstanceID())
        {
            Debug.LogWarning($"[LegacySurfaceIntegrity] oneFrameLater reason={reason}, ice visual binding references mesh {iceVisuals.BoundMeshInstanceId} while terrain mesh is {terrainMesh.GetInstanceID()}.", this);
        }

        if (runtimeGeodesicSurfaceMaterial != null && meshRenderer != null && meshRenderer.sharedMaterial == runtimeGeodesicSurfaceMaterial)
        {
            Debug.LogWarning($"[LegacySurfaceIntegrity] oneFrameLater reason={reason}, geodesic surface material remains assigned to legacy terrain.", this);
        }
    }

    private struct ColorStats
    {
        public int count;
        public float minR;
        public float maxR;
        public float meanR;
        public float minA;
        public float maxA;
        public float meanA;
    }

    private static ColorStats CalculateColorStats(Mesh targetMesh)
    {
        Color[] colors = targetMesh != null ? targetMesh.colors : null;
        ColorStats stats = new ColorStats { count = colors != null ? colors.Length : 0 };
        if (stats.count == 0)
        {
            return stats;
        }

        stats.minR = stats.minA = float.PositiveInfinity;
        stats.maxR = stats.maxA = float.NegativeInfinity;
        for (int i = 0; i < colors.Length; i++)
        {
            Color color = colors[i];
            stats.minR = Mathf.Min(stats.minR, color.r);
            stats.maxR = Mathf.Max(stats.maxR, color.r);
            stats.meanR += color.r;
            stats.minA = Mathf.Min(stats.minA, color.a);
            stats.maxA = Mathf.Max(stats.maxA, color.a);
            stats.meanA += color.a;
        }

        stats.meanR /= stats.count;
        stats.meanA /= stats.count;
        return stats;
    }

    private static string DescribeTexture(Texture texture)
    {
        if (texture == null)
        {
            return "<none>";
        }

        return $"id={texture.GetInstanceID()}, name={texture.name}, size={texture.width}x{texture.height}";
    }

    private void RestoreLegacyTerrainMaterial()
    {
        if (meshRenderer == null || runtimePlanetMaterial == null)
        {
            return;
        }

        meshRenderer.sharedMaterial = runtimePlanetMaterial;
        runtimePlanetMaterial.SetColor("_BaseColor", Color.white);
    }

    private void AssertLegacyModeClean(string stage)
    {
        bool stale = false;
        GeodesicGridDebugRenderer debugRenderer = transform.Find("Geodesic Debug Lines")?.GetComponent<GeodesicGridDebugRenderer>();
        MeshRenderer debugMeshRenderer = debugRenderer != null ? debugRenderer.GetComponent<MeshRenderer>() : null;
        if (debugMeshRenderer != null && debugMeshRenderer.enabled)
        {
            stale = true;
            debugRenderer.ClearAndDisable();
        }

        if (geodesicOceanMeshRenderer != null && geodesicOceanMeshRenderer.enabled)
        {
            stale = true;
            geodesicOceanMeshRenderer.enabled = false;
        }

        GeodesicCellPicker picker = GetComponent<GeodesicCellPicker>();
        if (picker != null && picker.enabled)
        {
            picker.SetTopology(null);
            picker.enabled = false;
        }

        if (GeodesicTopology != null)
        {
            stale = true;
            GeodesicTopology = null;
        }
        if (geodesicTransportGraph != null)
        {
            stale = true;
            ClearGeodesicTransportGraph();
        }

        if (meshRenderer != null && runtimePlanetMaterial != null && meshRenderer.sharedMaterial != runtimePlanetMaterial)
        {
            stale = true;
            meshRenderer.sharedMaterial = runtimePlanetMaterial;
        }

        bool geodesicRendererStillActive = false;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (renderer.GetComponent<GeodesicGridDebugRenderer>() != null || renderer.name.Contains("Geodesic"))
            {
                geodesicRendererStillActive = true;
                break;
            }
        }

        if (stale)
        {
            Debug.LogWarning($"[PlanetModeTransitionDiagnostics] Corrected stale geodesic runtime state during {stage}.", this);
        }

        if (geodesicRendererStillActive)
        {
            Debug.LogWarning($"[PlanetModeTransitionDiagnostics] Legacy generation completed with an active geodesic-only renderer during {stage}.", this);
        }
    }

    private void LogModeTransitionRendererInventory(string stage, PlanetGridType targetGridMode)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        System.Text.StringBuilder sb = new System.Text.StringBuilder(1024);
        sb.Append($"[PlanetModeTransitionDiagnostics] stage={stage}, targetGridMode={targetGridMode}, childRenderers=");
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            Mesh meshForRenderer = null;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null) meshForRenderer = filter.sharedMesh;
            string owner = renderer.GetComponent<GeodesicGridDebugRenderer>() != null || renderer.name.Contains("Geodesic") ? "geodesic" : (renderer.transform == transform || renderer.name.Contains("Ocean") || renderer.name.Contains("Atmosphere") ? "shared" : "legacy");
            Material material = renderer.sharedMaterial;
            string materialName = material != null ? material.name : "<none>";
            string shaderName = material != null && material.shader != null ? material.shader.name : "<none>";
            sb.Append($" [{i}] name={renderer.name}, owner={owner}, active={renderer.gameObject.activeInHierarchy}, enabled={renderer.enabled}, vertices={(meshForRenderer != null ? meshForRenderer.vertexCount : 0)}, material={materialName}, shader={shaderName};");
        }
        Debug.Log(sb.ToString(), this);
    }

    void ApplyGeneratedPlanetGeometry(Vector3[] unitVertices, int[] triangles, float[] finalTerrainRadii)
    {
        if (unitVertices == null || triangles == null || finalTerrainRadii == null)
        {
            return;
        }

        int cellCount = unitVertices.Length;
        float seaRadius = GetOceanRadius();
        maximumGeneratedOpaqueSurfaceRadius = BasePlanetRadius;
        Vector3[] terrainVertices = new Vector3[cellCount];
        Vector3[] oceanVertices = new Vector3[cellCount];
        Vector3[] atmosphereVertices = new Vector3[cellCount];

        for (int i = 0; i < cellCount; i++)
        {
            Vector3 dir = unitVertices[i];
            float shellBaseRadius = enableOcean ? seaRadius : radius;
            float atmosphereRadius = shellBaseRadius * atmosphereRadiusMultiplier;
            terrainVertices[i] = dir * finalTerrainRadii[i];
            maximumGeneratedOpaqueSurfaceRadius = Mathf.Max(maximumGeneratedOpaqueSurfaceRadius, finalTerrainRadii[i]);
            oceanVertices[i] = dir * seaRadius;
            atmosphereVertices[i] = dir * atmosphereRadius;
        }

        mesh.Clear();
        RestoreLegacyTerrainMaterial();
        mesh.vertices = terrainVertices;
        mesh.uv = BuildSphereUvs(new List<Vector3>(unitVertices));
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        UpdateSurfaceMaterialProperties();

        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;
        }

        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = mesh;
        }

        if (oceanMesh == null)
        {
            oceanMesh = new Mesh { name = "Planet Ocean" };
        }

        oceanMesh.Clear();
        oceanMesh.vertices = oceanVertices;
        oceanMesh.triangles = triangles;
        oceanMesh.RecalculateBounds();
        oceanMesh.RecalculateNormals();

        if (oceanMeshFilter != null)
        {
            oceanMeshFilter.sharedMesh = oceanMesh;
        }

        if (oceanMeshRenderer != null)
        {
            EnsureLegacyOceanMaterial();
            oceanMeshRenderer.enabled = enableOcean;
            LogOceanRendererDiagnostics("LegacyCubeSphere", oceanMeshRenderer.sharedMaterial, GetLegacyOceanAppearanceSample(), false);
        }

        if (atmosphereMesh == null)
        {
            atmosphereMesh = new Mesh { name = "Planet Atmosphere" };
        }

        atmosphereMesh.Clear();
        atmosphereMesh.vertices = atmosphereVertices;
        atmosphereMesh.triangles = triangles;
        atmosphereMesh.RecalculateBounds();
        atmosphereMesh.RecalculateNormals();

        if (atmosphereMeshFilter != null)
        {
            atmosphereMeshFilter.sharedMesh = atmosphereMesh;
        }

        if (atmosphereMeshRenderer != null)
        {
            atmosphereMeshRenderer.enabled = enableAtmosphere;
            if (atmosphereMaterial != null)
            {
                atmosphereMeshRenderer.sharedMaterial = atmosphereMaterial;
            }
        }
    }


    private (float min, float max) GetGeneratedRadiusExtrema()
    {
        if (generatedSurfaceRadiusByCell == null || generatedSurfaceRadiusByCell.Length == 0)
        {
            return (BasePlanetRadius, BasePlanetRadius);
        }

        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int i = 0; i < generatedSurfaceRadiusByCell.Length; i++)
        {
            float value = generatedSurfaceRadiusByCell[i];
            if (value < min) min = value;
            if (value > max) max = value;
        }

        bool minValid = !float.IsNaN(min) && !float.IsInfinity(min);
        bool maxValid = !float.IsNaN(max) && !float.IsInfinity(max);
        return (minValid ? min : BasePlanetRadius, maxValid ? max : BasePlanetRadius);
    }

    private void PopulateRuntimeDescriptor(int cellCount)
    {
        RuntimeDescriptor = new PlanetRuntimeDescriptor
        {
            GridType = CurrentGridType,
            MasterSeed = randomSeed,
            GenerationVersion = GenerationVersion,
            BaseRadius = BasePlanetRadius,
            MinimumGeneratedRadius = MinimumSurfaceRadius,
            MaximumGeneratedRadius = MaximumSurfaceRadius,
            CellCount = Mathf.Max(0, cellCount),
            CubeSphereResolution = CurrentGridType == PlanetGridType.LegacyCubeSphere ? resolution : 0,
            GeodesicSubdivision = CurrentGridType == PlanetGridType.GeodesicIcosphere ? geodesicSimulationSubdivisionLevel : 0
        };
        HasRuntimeDescriptor = true;
    }

    private void LogPlanetGenerationValidation(MeshCollider meshCollider)
    {
        Bounds meshBounds = mesh != null ? mesh.bounds : default;
        Bounds colliderBounds = meshCollider != null ? meshCollider.bounds : default;
        float cameraMinDistance = MaximumSurfaceRadius + 0.5f;
        Debug.Log($"[PlanetGenerationValidation] grid={CurrentGridType}, masterSeed={randomSeed}, derivedTerrainSeed={DerivedTerrainSeed}, derivedVisualSeed={DerivedVisualSeed}, baseRadius={BasePlanetRadius:F5}, minSurfaceRadius={MinimumSurfaceRadius:F5}, maxSurfaceRadius={MaximumSurfaceRadius:F5}, meshBounds={meshBounds}, colliderBounds={colliderBounds}, cameraMinimumDistance={cameraMinDistance:F5}", this);
    }

    void SetupPlanetMaterial()
    {
        if (meshRenderer == null)
        {
            return;
        }

        if (planetMaterial != null)
        {
            runtimePlanetMaterial = new Material(planetMaterial);
            runtimePlanetMaterial.name = $"{planetMaterial.name} (Runtime Planet Surface)";
            meshRenderer.sharedMaterial = runtimePlanetMaterial;
        }
        else
        {
            runtimePlanetMaterial = meshRenderer.sharedMaterial;
        }

    }

    void UpdateSurfaceMaterialProperties()
    {
        if (runtimePlanetMaterial == null)
        {
            return;
        }

        Texture2D texture = BuildSurfaceColorTexture();
        if (texture == null)
        {
            return;
        }

        runtimePlanetMaterial.SetTexture("_BaseMap", texture);
        runtimePlanetMaterial.SetColor("_BaseColor", Color.white);

        if (runtimePlanetMaterial.HasProperty("_Metallic"))
        {
            runtimePlanetMaterial.SetFloat("_Metallic", 0f);
        }

        if (runtimePlanetMaterial.HasProperty("_Smoothness"))
        {
            runtimePlanetMaterial.SetFloat("_Smoothness", 0.12f);
        }
    }

    Texture2D BuildSurfaceColorTexture()
    {
        const int textureWidth = 4096;
        const int textureHeight = 2048;
        const TextureFormat textureFormat = TextureFormat.RGBA32;
        const bool linearColorSpace = true;
        int pixelCount = textureWidth * textureHeight;

        if (runtimeSurfaceTexture == null || runtimeSurfaceTexture.width != textureWidth || runtimeSurfaceTexture.height != textureHeight)
        {
            runtimeSurfaceTexture = new Texture2D(textureWidth, textureHeight, textureFormat, false, linearColorSpace)
            {
                name = "Planet Surface Rock Colors",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
        }

        string textureCacheKey = PlanetGenerationCache.BuildSurfaceTextureCacheKeyString(this, textureWidth, textureHeight, textureFormat, linearColorSpace);
        string textureCachePath = PlanetGenerationCache.BuildSurfaceTextureCachePath(textureCacheKey);
        if (PlanetGenerationCache.TryLoadSurfaceTexture(textureCachePath, textureWidth, textureHeight, textureFormat, linearColorSpace, out PlanetGenerationCache.SurfaceTextureData cachedTextureData))
        {
            runtimeSurfaceTexture.LoadRawTextureData(cachedTextureData.RawTextureData);
            runtimeSurfaceTexture.Apply(false, false);
            Debug.Log($"[PlanetGenerationCache] Loaded surface texture cache ({textureCachePath}).");
            return runtimeSurfaceTexture;
        }

        Color[] pixels = new Color[pixelCount];

        float largeScale = Mathf.Max(0.01f, largeNoiseScale);
        float mediumScale = Mathf.Max(0.01f, mediumNoiseScale);
        float detailScale = Mathf.Max(0.01f, detailNoiseScale);
        float contrastSafe = Mathf.Max(0.01f, contrast);
        float crackDarkeningSafe = Mathf.Clamp01(crackDarkening);

        for (int y = 0; y < textureHeight; y++)
        {
            float v = y / (textureHeight - 1f);
            float phi = v * Mathf.PI;
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);

            for (int x = 0; x < textureWidth; x++)
            {
                float u = x / (textureWidth - 1f);
                float theta = u * Mathf.PI * 2f;
                Vector3 sampleDir = new Vector3(
                    sinPhi * Mathf.Cos(theta),
                    cosPhi,
                    sinPhi * Mathf.Sin(theta));

                float large = 0.5f * (SimpleNoise.Evaluate(sampleDir * largeScale + noiseOffset * 0.15f) + 1f);
                float medium = 0.5f * (SimpleNoise.Evaluate(sampleDir * mediumScale + noiseOffset * 0.45f) + 1f);
                float detail = 0.5f * (SimpleNoise.Evaluate(sampleDir * detailScale + noiseOffset) + 1f);

                float rockyBlend = large * 0.6f + medium * 0.3f + detail * 0.1f;
                rockyBlend = Mathf.Clamp01(Mathf.Pow(rockyBlend, contrastSafe));

                float crackMask = Mathf.Clamp01(1f - medium * 0.75f - detail * 0.25f);
                Color rockColor = BlendRockPalette(rockyBlend);
                rockColor *= 1f - crackMask * crackDarkeningSafe;
                rockColor.a = 1f;
                pixels[(y * textureWidth) + x] = rockColor;
            }
        }

        runtimeSurfaceTexture.SetPixels(pixels);
        runtimeSurfaceTexture.Apply(false, false);
        var rawTextureData = runtimeSurfaceTexture.GetRawTextureData<byte>();
        byte[] rawTextureBytes = new byte[rawTextureData.Length];
        rawTextureData.CopyTo(rawTextureBytes);
        PlanetGenerationCache.SaveSurfaceTexture(
            textureCachePath,
            new PlanetGenerationCache.SurfaceTextureData
            {
                Width = textureWidth,
                Height = textureHeight,
                Format = textureFormat,
                LinearColorSpace = linearColorSpace,
                RawTextureData = rawTextureBytes
            });
        Debug.Log($"[PlanetGenerationCache] Regenerated surface texture and saved cache ({textureCachePath}).");
        return runtimeSurfaceTexture;
    }

    Color BlendRockPalette(float blend)
    {
        if (blend < 0.5f)
        {
            return Color.Lerp(darkRockColor, midRockColor, blend * 2f);
        }

        return Color.Lerp(midRockColor, lightRockColor, (blend - 0.5f) * 2f);
    }

    Vector2[] BuildSphereUvs(List<Vector3> unitVertices)
    {
        if (unitVertices == null || unitVertices.Count == 0)
        {
            return System.Array.Empty<Vector2>();
        }

        Vector2[] uvs = new Vector2[unitVertices.Count];
        for (int i = 0; i < unitVertices.Count; i++)
        {
            Vector3 dir = unitVertices[i].normalized;
            float u = Mathf.Atan2(dir.z, dir.x) / (2f * Mathf.PI) + 0.5f;
            float v = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) / Mathf.PI + 0.5f;
            uvs[i] = new Vector2(u, v);
        }

        return uvs;
    }

    void OnDestroy()
    {
        ClearGeodesicRuntimeVisuals("OnDestroy");
        if (runtimePlanetMaterial != null)
        {
            Destroy(runtimePlanetMaterial);
            runtimePlanetMaterial = null;
        }

        if (runtimeSurfaceTexture != null)
        {
            Destroy(runtimeSurfaceTexture);
            runtimeSurfaceTexture = null;
        }

        ReleaseGeodesicSurfaceMaterial();
        ReleaseRuntimeOceanMaterial();
        ReleaseGeodesicOceanMaterial();
        if (geodesicOceanMesh != null) Destroy(geodesicOceanMesh);
        geodesicOceanMesh = null;
    }

    float CalculateNoiseThreshold(List<float> samples, float coveragePercent)
    {
        if (samples == null || samples.Count == 0)
        {
            return 0f;
        }

        float[] sorted = samples.ToArray();
        System.Array.Sort(sorted);

        float clampedCoverage = Mathf.Clamp(coveragePercent, 0f, 100f) / 100f;
        if (clampedCoverage <= 0f) return sorted[0] - 0.000001f;
        if (clampedCoverage >= 1f) return sorted[sorted.Length - 1] + 0.000001f;
        int index = Mathf.Clamp(Mathf.RoundToInt((sorted.Length - 1) * clampedCoverage), 0, sorted.Length - 1);
        return sorted[index];
    }

    public bool OceanEnabled => enableOcean;
    public float OceanThresholdNoise => oceanNoiseThreshold;
    public bool PhotosynthesisUnlocked => GetSimulationTimeSeconds() >= photosynthesisUnlockSeconds;
    public bool SaprotrophyUnlocked => GetSimulationTimeSeconds() >= saprotrophyUnlockSeconds;

    double GetSimulationTimeSeconds()
    {
        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }

        return replicatorManager != null ? replicatorManager.SimulationTimeSeconds : Time.timeSinceLevelLoad;
    }

    public float GetOceanRadius()
    {
        if (!enableOcean)
        {
            return BasePlanetRadius;
        }

        return BasePlanetRadius * (1f + oceanNoiseThreshold * noiseMagnitude);
    }

    public SurfaceQueryParameters GetSurfaceQueryParameters()
    {
        return new SurfaceQueryParameters
        {
            Radius = BasePlanetRadius,
            NoiseMagnitude = noiseMagnitude,
            NoiseRoughness = noiseRoughness,
            NoiseOffset = noiseOffset,
            NumLayers = numLayers,
            Persistence = persistence,
            OceanThreshold = oceanNoiseThreshold,
            OceanDepth = oceanDepth,
            OceanEnabled = enableOcean
        };
    }

    public float GetSurfaceRadius(Vector3 pointOnSphere)
    {
        if (generatedSurfaceRadiusByCell != null && generatedSurfaceRadiusByCell.Length > 0)
        {
            int cellIndex = PlanetGridIndexing.DirectionToCellIndex(pointOnSphere.normalized, resolution);
            if (cellIndex >= 0 && cellIndex < generatedSurfaceRadiusByCell.Length)
            {
                return generatedSurfaceRadiusByCell[cellIndex];
            }
        }

        float noise = CalculateNoise(pointOnSphere.normalized);
        return GetSurfaceRadiusFromNoise(noise);
    }

    public float GetSurfaceRadius(int cellIndex)
    {
        if (generatedSurfaceRadiusByCell != null && cellIndex >= 0 && cellIndex < generatedSurfaceRadiusByCell.Length)
        {
            return generatedSurfaceRadiusByCell[cellIndex];
        }

        return BasePlanetRadius;
    }

    public float GetOceanFloorRadius(int cellIndex)
    {
        // For ocean cells this is the local seafloor shell.
        // For land cells this remains the terrain surface radius (unchanged behavior).
        return GetSurfaceRadius(cellIndex);
    }

    public float GetOceanFloorRadius(Vector3 pointOnSphere)
    {
        // Direction-based query used by lower-resolution simulation grids.
        return GetSurfaceRadius(pointOnSphere);
    }

    public float GetOceanTopRadius(int cellIndex)
    {
        if (!IsOceanCell(cellIndex))
        {
            return GetSurfaceRadius(cellIndex);
        }

        // Sea level shell used by the ocean surface mesh.
        return GetOceanRadius();
    }

    public float GetOceanTopRadius(Vector3 pointOnSphere)
    {
        if (!IsOceanAtDirection(pointOnSphere))
        {
            return GetSurfaceRadius(pointOnSphere);
        }

        return GetOceanRadius();
    }

    public float GetSurfaceRadiusFromNoise(float noise)
    {
        return GetSurfaceRadiusFromNoise(noise, GetSurfaceQueryParameters());
    }

    public static float GetSurfaceRadiusFromNoise(float noise, in SurfaceQueryParameters query)
    {
        float seaNoise = query.OceanThreshold;
        float finalNoise = noise;

        if (query.OceanEnabled && noise < seaNoise)
        {
            float t = seaNoise > 0f ? Mathf.Clamp01(noise / seaNoise) : 0f;
            float minNoise = seaNoise * (1f - query.OceanDepth);
            finalNoise = Mathf.Lerp(minNoise, seaNoise, t);
        }

        return query.Radius * (1f + finalNoise * query.NoiseMagnitude);
    }

    public bool IsOceanAtDirection(Vector3 pointOnSphere)
    {
        int cellIndex = PlanetGridIndexing.DirectionToCellIndex(pointOnSphere.normalized, resolution);
        if (oceanMaskByCell != null && cellIndex >= 0 && cellIndex < oceanMaskByCell.Length)
        {
            return oceanMaskByCell[cellIndex] != 0;
        }

        float noise = CalculateNoise(pointOnSphere.normalized);
        return IsOceanNoise(noise, GetSurfaceQueryParameters());
    }

    public static bool IsOceanNoise(float noise, in SurfaceQueryParameters query)
    {
        return query.OceanEnabled && noise < query.OceanThreshold;
    }

    public float GetLocalOceanDepth(Vector3 pointOnSphere)
    {
        int cellIndex = PlanetGridIndexing.DirectionToCellIndex(pointOnSphere.normalized, resolution);
        return GetLocalOceanDepth(cellIndex);
    }

    public float GetLocalOceanDepth(int cellIndex)
    {
        if (localOceanDepthByCell == null || cellIndex < 0 || cellIndex >= localOceanDepthByCell.Length)
        {
            return 0f;
        }

        return localOceanDepthByCell[cellIndex];
    }

    public bool IsOceanCell(int cellIndex)
    {
        if (oceanMaskByCell == null || cellIndex < 0 || cellIndex >= oceanMaskByCell.Length)
        {
            return false;
        }

        return oceanMaskByCell[cellIndex] != 0;
    }

    void BuildOceanBathymetry(
        List<Vector3> unitVertices,
        List<float> noiseSamples,
        float[] finalTerrainRadii,
        float seaRadius,
        int[] neighbors)
    {
        int cellCount = unitVertices.Count;
        if (finalTerrainRadii == null || finalTerrainRadii.Length != cellCount)
        {
            return;
        }

        generatedSurfaceRadiusByCell = new float[cellCount];
        localOceanDepthByCell = new float[cellCount];
        oceanDistanceToShoreByCell = new float[cellCount];
        oceanMaskByCell = new byte[cellCount];

        for (int cell = 0; cell < cellCount; cell++)
        {
            float baseRadius = GetSurfaceRadiusFromNoise(noiseSamples[cell]);
            finalTerrainRadii[cell] = baseRadius;
            oceanDistanceToShoreByCell[cell] = -1f;

            bool isOcean = enableOcean && baseRadius < seaRadius;
            oceanMaskByCell[cell] = isOcean ? (byte)1 : (byte)0;
            localOceanDepthByCell[cell] = isOcean ? Mathf.Max(0f, seaRadius - baseRadius) : 0f;
        }

        if (!enableOcean || !enableBathymetry || neighbors == null || neighbors.Length != cellCount * 6)
        {
            System.Array.Copy(finalTerrainRadii, generatedSurfaceRadiusByCell, cellCount);
            return;
        }

        Queue<int> bfsQueue = new Queue<int>(cellCount);
        float maxDistance = 0f;

        for (int cell = 0; cell < cellCount; cell++)
        {
            if (oceanMaskByCell[cell] == 0)
            {
                continue;
            }

            bool isShore = false;
            int baseIndex = cell * 6;
            for (int n = 0; n < 6; n++)
            {
                int neighbor = neighbors[baseIndex + n];
                if (neighbor < 0 || neighbor >= cellCount || oceanMaskByCell[neighbor] == 0)
                {
                    isShore = true;
                    break;
                }
            }

            if (isShore)
            {
                oceanDistanceToShoreByCell[cell] = 0f;
                bfsQueue.Enqueue(cell);
            }
        }

        while (bfsQueue.Count > 0)
        {
            int current = bfsQueue.Dequeue();
            float currentDistance = oceanDistanceToShoreByCell[current];
            int baseIndex = current * 6;

            for (int n = 0; n < 6; n++)
            {
                int neighbor = neighbors[baseIndex + n];
                if (neighbor < 0 || neighbor >= cellCount || oceanMaskByCell[neighbor] == 0 || oceanDistanceToShoreByCell[neighbor] >= 0f)
                {
                    continue;
                }

                float nextDistance = currentDistance + 1f;
                oceanDistanceToShoreByCell[neighbor] = nextDistance;
                maxDistance = Mathf.Max(maxDistance, nextDistance);
                bfsQueue.Enqueue(neighbor);
            }
        }

        SmoothOceanDistanceField(neighbors, oceanDistanceToShoreByCell, oceanMaskByCell, Mathf.Clamp(bathymetrySmoothPasses, 0, 8), Mathf.Clamp01(bathymetrySmoothStrength));
        maxDistance = 0f;
        for (int cell = 0; cell < cellCount; cell++)
        {
            if (oceanMaskByCell[cell] == 0 || oceanDistanceToShoreByCell[cell] < 0f)
            {
                continue;
            }

            maxDistance = Mathf.Max(maxDistance, oceanDistanceToShoreByCell[cell]);
        }

        float shelfDistanceSafe = Mathf.Max(1f, shelfDistance);
        float shelfDepthSafe = Mathf.Clamp(shelfDepth, 0f, Mathf.Max(0f, maxOceanDepth));
        float maxDepthSafe = Mathf.Max(shelfDepthSafe, maxOceanDepth);
        float slopeStrengthSafe = Mathf.Max(0f, slopeStrength);
        float basinScale = Mathf.Max(0.001f, basinNoiseScale);
        float basinStrength = Mathf.Clamp01(basinNoiseStrength);
        float falloffRange = Mathf.Max(1f, maxDistance - shelfDistanceSafe);
        float shorelinePreserve = Mathf.Max(0f, shorelinePreservationDistance);
        float visualStrength = Mathf.Clamp01(bathymetryVisualStrength);

        for (int cell = 0; cell < cellCount; cell++)
        {
            if (oceanMaskByCell[cell] == 0)
            {
                generatedSurfaceRadiusByCell[cell] = finalTerrainRadii[cell];
                localOceanDepthByCell[cell] = 0f;
                continue;
            }

            float shoreDistance = oceanDistanceToShoreByCell[cell];
            if (shoreDistance < 0f)
            {
                shoreDistance = shelfDistanceSafe + falloffRange;
            }

            float shelfT = Mathf.Clamp01(shoreDistance / shelfDistanceSafe);
            float shelfDepthTarget = shelfDepthSafe * Mathf.SmoothStep(0f, 1f, shelfT);

            float offshoreDistance = Mathf.Max(0f, shoreDistance - shelfDistanceSafe);
            float offshoreT = Mathf.Clamp01(offshoreDistance / falloffRange);
            float slopeT = Mathf.Clamp01(Mathf.Pow(offshoreT, 0.75f) * slopeStrengthSafe);
            float basinDepthTarget = Mathf.Lerp(shelfDepthSafe, maxDepthSafe, slopeT);

            float basinNoise = SimpleNoise.Evaluate(unitVertices[cell] * basinScale + basinNoiseOffset);
            float basinNoise01 = (basinNoise + 1f) * 0.5f;
            float basinModulation = 1f + (basinNoise01 - 0.5f) * 2f * basinStrength;

            float depthTarget = Mathf.Lerp(shelfDepthTarget, basinDepthTarget, shelfT) * basinModulation;
            depthTarget = Mathf.Clamp(depthTarget, 0f, maxDepthSafe);

            float baseDepth = Mathf.Max(0f, seaRadius - finalTerrainRadii[cell]);
            float additionalDepth = Mathf.Max(0f, depthTarget - baseDepth);
            float offshoreBlend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01((shoreDistance - shorelinePreserve) / Mathf.Max(1f, shelfDistanceSafe)));
            float appliedAdditionalDepth = additionalDepth * offshoreBlend * visualStrength;
            float finalDepth = Mathf.Clamp(baseDepth + appliedAdditionalDepth, 0f, maxDepthSafe);

            localOceanDepthByCell[cell] = finalDepth;
            float oceanFloorRadius = Mathf.Max(0.01f, seaRadius - finalDepth);
            finalTerrainRadii[cell] = Mathf.Min(seaRadius, oceanFloorRadius);
            generatedSurfaceRadiusByCell[cell] = finalTerrainRadii[cell];
        }
    }

    static void SmoothOceanDistanceField(int[] neighbors, float[] distances, byte[] oceanMask, int passes, float strength)
    {
        if (neighbors == null || distances == null || oceanMask == null || passes <= 0 || strength <= 0f)
        {
            return;
        }

        int cellCount = distances.Length;
        float[] temp = new float[cellCount];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int cell = 0; cell < cellCount; cell++)
            {
                if (oceanMask[cell] == 0 || distances[cell] < 0f)
                {
                    temp[cell] = distances[cell];
                    continue;
                }

                int baseIndex = cell * 6;
                float sum = distances[cell];
                int count = 1;
                for (int n = 0; n < 6; n++)
                {
                    int neighbor = neighbors[baseIndex + n];
                    if (neighbor < 0 || neighbor >= cellCount || oceanMask[neighbor] == 0 || distances[neighbor] < 0f)
                    {
                        continue;
                    }

                    sum += distances[neighbor];
                    count++;
                }

                float average = count > 0 ? sum / count : distances[cell];
                temp[cell] = Mathf.Lerp(distances[cell], average, strength);
            }

            for (int i = 0; i < cellCount; i++)
            {
                distances[i] = temp[i];
            }
        }
    }

    static int[] BuildCellNeighborLookup(List<Vector3> unitVertices, List<int> triangles)
    {
        int cellCount = unitVertices != null ? unitVertices.Count : 0;
        if (cellCount <= 0)
        {
            return System.Array.Empty<int>();
        }

        const int maxNeighbors = 6;
        int[] neighbors = new int[cellCount * maxNeighbors];
        for (int i = 0; i < neighbors.Length; i++)
        {
            neighbors[i] = -1;
        }

        if (triangles == null)
        {
            return neighbors;
        }

        for (int tri = 0; tri + 2 < triangles.Count; tri += 3)
        {
            int a = triangles[tri];
            int b = triangles[tri + 1];
            int c = triangles[tri + 2];
            AddNeighborPair(neighbors, a, b, maxNeighbors);
            AddNeighborPair(neighbors, b, c, maxNeighbors);
            AddNeighborPair(neighbors, c, a, maxNeighbors);
        }

        Dictionary<Vector3Int, List<int>> seamBuckets = new Dictionary<Vector3Int, List<int>>(cellCount);
        const float quantizeScale = 100000f;
        for (int i = 0; i < cellCount; i++)
        {
            Vector3 dir = unitVertices[i];
            Vector3Int key = new Vector3Int(
                Mathf.RoundToInt(dir.x * quantizeScale),
                Mathf.RoundToInt(dir.y * quantizeScale),
                Mathf.RoundToInt(dir.z * quantizeScale));

            if (!seamBuckets.TryGetValue(key, out List<int> bucket))
            {
                bucket = new List<int>(3);
                seamBuckets[key] = bucket;
            }

            bucket.Add(i);
        }

        foreach (List<int> bucket in seamBuckets.Values)
        {
            if (bucket.Count < 2)
            {
                continue;
            }

            for (int a = 0; a < bucket.Count; a++)
            {
                for (int b = a + 1; b < bucket.Count; b++)
                {
                    AddNeighborPair(neighbors, bucket[a], bucket[b], maxNeighbors);
                }
            }
        }

        return neighbors;
    }

    static void AddNeighborPair(int[] neighbors, int a, int b, int maxNeighbors)
    {
        AddNeighbor(neighbors, a, b, maxNeighbors);
        AddNeighbor(neighbors, b, a, maxNeighbors);
    }

    static void AddNeighbor(int[] neighbors, int source, int neighbor, int maxNeighbors)
    {
        if (source < 0 || neighbor < 0)
        {
            return;
        }

        int start = source * maxNeighbors;
        for (int i = 0; i < maxNeighbors; i++)
        {
            int idx = start + i;
            int current = neighbors[idx];
            if (current == neighbor)
            {
                return;
            }

            if (current == -1)
            {
                neighbors[idx] = neighbor;
                return;
            }
        }
    }


    static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null)
        {
            component = target.AddComponent<T>();
        }

        return component;
    }

    public float CalculateNoise(Vector3 pointOnSphere)
    {
        return CalculateNoise(pointOnSphere, GetSurfaceQueryParameters());
    }

    public static float CalculateNoise(Vector3 pointOnSphere, in SurfaceQueryParameters query)
    {
        float noiseValue = 0;
        float frequency = query.NoiseRoughness;
        float amplitude = 1;

        for (int i = 0; i < query.NumLayers; i++)
        {
            // Sample 3D noise using the current frequency and offset
            Vector3 samplePoint = pointOnSphere * frequency + query.NoiseOffset;
            float v = SimpleNoise.Evaluate(samplePoint);

            // Convert noise from range (-1, 1) to (0, 1)
            //v = (v + 1f) * 0.5f;
            v = 1.0f - Mathf.Abs(v); // Creates sharp ridges
            v *= v; // Further accentuates valleys and peaks

            // Add to the total value using the current amplitude
            noiseValue += v * amplitude;

            // Update parameters for the next layer (octave)
            amplitude *= query.Persistence; // Each next layer has less influence (e.g. 0.5)
            frequency *= 2.0f;        // Each next layer has more detail (e.g. 2.0)
        }

        // Return the accumulated value
        return noiseValue;
    }
}
