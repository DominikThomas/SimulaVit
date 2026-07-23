using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
    [Header("Generation Mode")]
    public PlanetGenerationMode generationMode = PlanetGenerationMode.LegacyCubeSphere;
    [Range(0, GeodesicGridTopology.MaxSupportedSubdivision)]
    public int geodesicSubdivisionLevel = 4;
    [Range(0, GeodesicGridTopology.MaxSupportedSubdivision)]
    public int geodesicSimulationSubdivisionLevel = 4;
    [Range(0, GeodesicGridTopology.MaxSupportedSubdivision)]
    public int geodesicRenderSubdivisionLevel = 5;
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

    [Header("Geodesic Ocean Visual")]
    [Range(0, GeodesicGridTopology.MaxSupportedSubdivision)] public int geodesicOceanRenderSubdivisionLevel = 5;
    [FormerlySerializedAs("geodesicOceanColour")]
    [SerializeField, HideInInspector] private Color deprecatedGeodesicOceanColour = new Color(0.02f, 0.28f, 0.55f, 0.42f);
    [FormerlySerializedAs("geodesicOceanShallowTint")]
    [SerializeField, HideInInspector] private Color deprecatedGeodesicOceanShallowTint = new Color(0.10f, 0.55f, 0.75f, 0.42f);
    [FormerlySerializedAs("geodesicOceanOpacity")]
    [SerializeField, HideInInspector] private float deprecatedGeodesicOceanOpacity = 0.42f;
    [FormerlySerializedAs("geodesicOceanSmoothness")]
    [SerializeField, HideInInspector] private float deprecatedGeodesicOceanSmoothness = 0.82f;

    [Header("Geodesic Terrain")]
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
    [Tooltip("Authoritative sea-level offset relative to PlanetGenerator.radius for geodesic land/ocean classification and ocean rendering.")]
    [FormerlySerializedAs("geodesicSeaLevelPreviewOffset")]
    [Range(-1f, 1f)] public float geodesicSeaLevelOffset = 0f;
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
    public bool enableOcean = true;
    [Range(20f, 70f)] public float oceanCoveragePercent = 45f;
    [Tooltip("How much of the mountain height range can sink below sea level.")]
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
    private bool[] geodesicOceanMask;
    private float[] geodesicWaterDepth;
    private byte[] geodesicOceanNeighborCounts;
    private bool[] geodesicCoastlineMask;

    public MeshRenderer OceanRenderer => oceanMeshRenderer;
    public IReadOnlyList<float> LocalOceanDepths => localOceanDepthByCell;
    public int VisualResolution => Mathf.Max(1, resolution);
    public bool IsPlanetInitialized { get; private set; }
    public GeodesicGridTopology GeodesicTopology { get; private set; }
    public PlanetRuntimeDescriptor RuntimeDescriptor { get; private set; }
    public bool HasRuntimeDescriptor { get; private set; }
    public const int GenerationVersion = 1;
    public float BasePlanetRadius => Mathf.Max(0.001f, radius);
    public float MinimumSurfaceRadius => CurrentGridType == PlanetGridType.GeodesicIcosphere ? BasePlanetRadius + GetGeodesicMinimumTerrainOffset() : GetGeneratedRadiusExtrema().min;
    public float MaximumSurfaceRadius => CurrentGridType == PlanetGridType.GeodesicIcosphere ? BasePlanetRadius + GetGeodesicMaximumTerrainOffset() : GetGeneratedRadiusExtrema().max;
    public float GeodesicSeaLevelRadius => BasePlanetRadius + geodesicSeaLevelOffset;
    public int DerivedTerrainSeed => usePlanetSeedForTerrain ? PlanetSeedUtility.DeriveSeed(randomSeed, PlanetSeedDomain.Terrain, GenerationVersion) : customTerrainSeed;
    public int DerivedVisualSeed => usePlanetSeedForVisuals ? PlanetSeedUtility.DeriveSeed(randomSeed, PlanetSeedDomain.SurfaceVisuals, GenerationVersion) : customVisualSeed;
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

    public void ClearGeneratedPlanetRuntime()
    {
        IsPlanetInitialized = false;
        generatedSurfaceRadiusByCell = null;
        localOceanDepthByCell = null;
        oceanDistanceToShoreByCell = null;
        oceanMaskByCell = null;
        geodesicTerrainHeightByCell = null;
        geodesicNormalizedTerrainByCell = null;
        geodesicOceanMask = null;
        geodesicWaterDepth = null;
        geodesicOceanNeighborCounts = null;
        geodesicCoastlineMask = null;

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
        GeodesicTopology = null;
        GetComponent<GeodesicCellPicker>()?.SetTopology(null);
        HasRuntimeDescriptor = false;
        RuntimeDescriptor = default;
        transform.Find("Geodesic Debug Lines")?.GetComponent<GeodesicGridDebugRenderer>()?.Render(null, BasePlanetRadius);
        ReleaseGeodesicSurfaceMaterial();
        ReleaseRuntimeOceanMaterial();
        ReleaseGeodesicOceanMaterial();
        if (geodesicOceanObject != null) Destroy(geodesicOceanObject);
        geodesicOceanObject = null;
        geodesicOceanMeshFilter = null;
        geodesicOceanMeshRenderer = null;
        if (meshRenderer != null && runtimePlanetMaterial != null)
        {
            meshRenderer.sharedMaterial = runtimePlanetMaterial;
        }
    }

    public void ApplyStartupGrid(PlanetGridType gridType, int cubeSphereResolution, int geodesicSubdivision)
    {
        generationMode = gridType == PlanetGridType.GeodesicIcosphere ? PlanetGenerationMode.GeodesicPrototype : PlanetGenerationMode.LegacyCubeSphere;
        resolution = Mathf.Clamp(cubeSphereResolution, 3, 240);
        geodesicSubdivisionLevel = Mathf.Clamp(geodesicSubdivision, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        geodesicSimulationSubdivisionLevel = geodesicSubdivisionLevel;
        geodesicRenderSubdivisionLevel = Mathf.Clamp(Mathf.Max(geodesicSimulationSubdivisionLevel, geodesicRenderSubdivisionLevel), 0, GeodesicGridTopology.MaxSupportedSubdivision);
        int expected = gridType == PlanetGridType.GeodesicIcosphere
            ? GeodesicGridTopology.ExpectedCellCount(geodesicSubdivisionLevel)
            : PlanetGridIndexing.GetCellCount(resolution);
        Debug.Log($"[PlanetGenerator] Selected grid type={gridType}, cubeSphereResolution={resolution}, geodesicSimulationSubdivision={geodesicSimulationSubdivisionLevel}, geodesicRenderSubdivision={geodesicRenderSubdivisionLevel}, expectedCellCount={expected}", this);
    }

    private void GenerateGeodesicPrototype()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int simulationSubdivision = Mathf.Clamp(geodesicSimulationSubdivisionLevel, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        int renderSubdivision = Mathf.Clamp(geodesicRenderSubdivisionLevel, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        geodesicSubdivisionLevel = simulationSubdivision;
        GeodesicTopology = GeodesicGridTopology.Build(simulationSubdivision);
        if (!GeodesicGridValidation.Validate(GeodesicTopology, out string validation))
        {
            Debug.LogError($"[GeodesicPrototype] Validation failed: {validation}", this);
            ClearGeneratedPlanetRuntime();
            return;
        }

        GeodesicGridTopology renderTopology = GeodesicGridTopology.Build(renderSubdivision);
        mesh = GeodesicSphereMeshBuilder.BuildSurfaceMesh(renderTopology, BasePlanetRadius, $"Geodesic Terrain Render L{renderSubdivision}");
        ApplyGeodesicTerrainDisplacement(mesh);
        RebuildGeodesicCellTerrainCache();
        RebuildGeodesicOceanClassification();
        ApplyGeodesicSurfaceColours(mesh);
        meshFilter.sharedMesh = mesh;
        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;
        }
        MeshCollider meshCollider = GetOrAddComponent<MeshCollider>(gameObject);
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
        if (oceanMesh != null) oceanMesh.Clear();
        if (geodesicOceanMesh != null) geodesicOceanMesh.Clear();
        if (atmosphereMesh != null) atmosphereMesh.Clear();
        if (oceanMeshRenderer != null) oceanMeshRenderer.enabled = false;
        if (geodesicOceanMeshRenderer != null) geodesicOceanMeshRenderer.enabled = false;
        if (atmosphereMeshRenderer != null) atmosphereMeshRenderer.enabled = false;
        BuildGeodesicOceanVisual();
        var picker = GetOrAddComponent<GeodesicCellPicker>(gameObject);
        picker.SetTopology(GeodesicTopology);
        Transform debugTransform = transform.Find("Geodesic Debug Lines");
        GameObject debugObject = debugTransform != null ? debugTransform.gameObject : new GameObject("Geodesic Debug Lines");
        debugObject.transform.SetParent(transform, false);
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
        debug.radialOffset = geodesicOutlineRadialOffset;
        debug.Render(GeodesicTopology, BasePlanetRadius);
        PopulateRuntimeDescriptor(GeodesicTopology.CellCount);
        LogPlanetGenerationValidation(meshCollider);
        sw.Stop();
        LogGeodesicTerrainDiagnostics(mesh, simulationSubdivision, renderSubdivision);
        Debug.Log($"[GeodesicPrototype] simulationSubdivision={simulationSubdivision}, renderSubdivision={renderSubdivision}, cells={GeodesicTopology.CellCount}, renderVertices={mesh.vertexCount}, renderTriangles={mesh.triangles.Length / 3}, simulationTriangles={GeodesicTopology.TriangleCount}, edges={GeodesicTopology.EdgeCount}, durationMs={sw.Elapsed.TotalMilliseconds:F2}, approxTopologyMemory={GeodesicTopology.ApproximateMemoryBytes} bytes. Validation: {validation}", this);
    }

    void ApplyGeodesicSurfaceColours(Mesh targetMesh)
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
                float terrainValue = GetNormalizedTerrainHeightAtDirection(direction);
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
        if (CurrentGridType == PlanetGridType.GeodesicIcosphere)
        {
            return BasePlanetRadius + EvaluateGeodesicTerrainHeight(direction);
        }

        return GetSurfaceRadius(direction);
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
        return BasePlanetRadius + GetCellTerrainHeight(geodesicCellIndex);
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

    void ApplyGeodesicTerrainDisplacement(Mesh targetMesh)
    {
        if (targetMesh == null) return;
        Vector3[] vertices = targetMesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 direction = vertices[i].sqrMagnitude > 1e-10f ? vertices[i].normalized : Vector3.up;
            vertices[i] = direction * GetSurfaceRadiusAtDirection(direction);
        }
        targetMesh.vertices = vertices;
        targetMesh.RecalculateNormals();
        targetMesh.RecalculateBounds();
    }


    void LogGeodesicTerrainDiagnostics(Mesh renderMesh, int simulationSubdivision, int renderSubdivision)
    {
        if (renderMesh == null) return;
        Vector3[] vertices = renderMesh.vertices;
        if (vertices == null || vertices.Length == 0) return;
        float min = float.PositiveInfinity, max = float.NegativeInfinity, sum = 0f, sumSq = 0f, maskAbove = 0f;
        float minRadius = float.PositiveInfinity, maxRadius = float.NegativeInfinity;
        PlanetTerrainSettings settings = GetGeodesicTerrainSettings();
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 direction = vertices[i].sqrMagnitude > 1e-10f ? vertices[i].normalized : Vector3.up;
            PlanetTerrainSample sample = PlanetTerrainSampler.Evaluate(direction, DerivedTerrainSeed, settings);
            float h = sample.HeightOffset;
            min = Mathf.Min(min, h); max = Mathf.Max(max, h); sum += h; sumSq += h * h;
            if (sample.MountainMask > 0f) maskAbove += 1f;
            float r = BasePlanetRadius + h; minRadius = Mathf.Min(minRadius, r); maxRadius = Mathf.Max(maxRadius, r);
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
        for (int i = 0; i < GeodesicTopology.CellCount; i++)
        {
            Vector3 direction = GeodesicTopology.CellDirections[i];
            geodesicTerrainHeightByCell[i] = GetTerrainHeightAtDirection(direction);
            geodesicNormalizedTerrainByCell[i] = GetNormalizedTerrainHeightAtDirection(direction);
        }
    }

    void RebuildGeodesicOceanClassification()
    {
        if (GeodesicTopology == null)
        {
            geodesicOceanMask = null; geodesicWaterDepth = null; geodesicOceanNeighborCounts = null; geodesicCoastlineMask = null;
            return;
        }

        int count = GeodesicTopology.CellCount;
        geodesicOceanMask = new bool[count];
        geodesicWaterDepth = new float[count];
        geodesicOceanNeighborCounts = new byte[count];
        geodesicCoastlineMask = new bool[count];

        int landCount = 0, oceanCount = 0, coastlineCount = 0;
        float areaSum = 0f, landArea = 0f, oceanArea = 0f, depthAreaSum = 0f;
        float minDepth = float.PositiveInfinity, maxDepth = 0f;
        float minTerrainRadius = float.PositiveInfinity, maxTerrainRadius = float.NegativeInfinity;

        for (int i = 0; i < count; i++)
        {
            float terrainRadius = GetSurfaceRadiusAtDirection(GeodesicTopology.CellDirections[i]);
            float depth = Mathf.Max(0f, GeodesicSeaLevelRadius - terrainRadius);
            bool ocean = depth > 0f;
            float area = GeodesicTopology.UnitCellAreas[i] * BasePlanetRadius * BasePlanetRadius;
            geodesicOceanMask[i] = ocean; geodesicWaterDepth[i] = depth;
            areaSum += area; minTerrainRadius = Mathf.Min(minTerrainRadius, terrainRadius); maxTerrainRadius = Mathf.Max(maxTerrainRadius, terrainRadius);
            if (ocean) { oceanCount++; oceanArea += area; depthAreaSum += depth * area; minDepth = Mathf.Min(minDepth, depth); maxDepth = Mathf.Max(maxDepth, depth); }
            else { landCount++; landArea += area; }
        }

        for (int i = 0; i < count; i++)
        {
            byte oceanNeighbors = 0; bool coastline = false; bool ocean = geodesicOceanMask[i];
            for (int n = 0; n < GeodesicTopology.NeighborCounts[i]; n++)
            {
                int neighbor = GeodesicTopology.Neighbors6[i * 6 + n];
                if (neighbor < 0 || neighbor >= count) continue;
                if (geodesicOceanMask[neighbor]) oceanNeighbors++;
                if (geodesicOceanMask[neighbor] != ocean) coastline = true;
            }
            geodesicOceanNeighborCounts[i] = oceanNeighbors;
            geodesicCoastlineMask[i] = coastline;
            if (coastline) coastlineCount++;
        }

        if (oceanCount == 0) minDepth = 0f;
        float landFraction = areaSum > 0f ? landArea / areaSum : 0f;
        float oceanFraction = areaSum > 0f ? oceanArea / areaSum : 0f;
        float meanDepth = oceanArea > 0f ? depthAreaSum / oceanArea : 0f;
        Debug.Log($"[GeodesicOceanDiagnostics] cellsLandOceanCoast={landCount}/{oceanCount}/{coastlineCount}, totalGeodesicArea={areaSum:F8}, areaWeightedLandOcean={landFraction:F6}/{oceanFraction:F6}, areaFractionSum={(landFraction + oceanFraction):F6}, oceanDepthMinMaxMean={minDepth:F6}/{maxDepth:F6}/{meanDepth:F6}, seaLevelRadius={GeodesicSeaLevelRadius:F6}, terrainSurfaceRadiusMinMax={minTerrainRadius:F6}/{maxTerrainRadius:F6}", this);
    }

    void BuildGeodesicOceanVisual()
    {
        int oceanSubdivision = Mathf.Clamp(geodesicOceanRenderSubdivisionLevel, 0, GeodesicGridTopology.MaxSupportedSubdivision);
        GeodesicGridTopology oceanTopology = GeodesicGridTopology.Build(oceanSubdivision);
        geodesicOceanMesh = GeodesicSphereMeshBuilder.BuildSurfaceMesh(oceanTopology, GeodesicSeaLevelRadius, $"Geodesic Ocean Render L{oceanSubdivision}");
        Transform existing = transform.Find("Geodesic Ocean");
        geodesicOceanObject = existing != null ? existing.gameObject : new GameObject("Geodesic Ocean");
        geodesicOceanObject.transform.SetParent(transform, false);
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
        Debug.Log($"[OceanVisualDiagnostics] renderer={rendererType}, settingsSource={settingsSource}, baseColorBound={evaluation.finalColor}, opacityBound={evaluation.opacity:F3}, shader={shaderName}, oxygenation01={sample.oxygenation01:F3}, deprecatedFallbackFieldsUsed={deprecatedFallbackFieldsUsed}", this);
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
    }

    void ReleaseGeodesicSurfaceMaterial()
    {
        if (runtimeGeodesicSurfaceMaterial != null)
        {
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
            Mathf.Clamp(Mathf.Min(oceanCoverageRange.x, oceanCoverageRange.y), 20f, 70f),
            Mathf.Clamp(Mathf.Max(oceanCoverageRange.x, oceanCoverageRange.y), 20f, 70f)
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
            PopulateRuntimeDescriptor(cellCount);
            LogPlanetGenerationValidation(GetComponent<MeshCollider>());
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
        Debug.Log($"[PlanetGenerationCache] Regenerated planet and saved cache ({cachePath}).");
    }

    void ApplyGeneratedPlanetGeometry(Vector3[] unitVertices, int[] triangles, float[] finalTerrainRadii)
    {
        if (unitVertices == null || triangles == null || finalTerrainRadii == null)
        {
            return;
        }

        int cellCount = unitVertices.Length;
        float seaRadius = GetOceanRadius();
        Vector3[] terrainVertices = new Vector3[cellCount];
        Vector3[] oceanVertices = new Vector3[cellCount];
        Vector3[] atmosphereVertices = new Vector3[cellCount];

        for (int i = 0; i < cellCount; i++)
        {
            Vector3 dir = unitVertices[i];
            float shellBaseRadius = enableOcean ? seaRadius : radius;
            float atmosphereRadius = shellBaseRadius * atmosphereRadiusMultiplier;
            terrainVertices[i] = dir * finalTerrainRadii[i];
            oceanVertices[i] = dir * seaRadius;
            atmosphereVertices[i] = dir * atmosphereRadius;
        }

        mesh.Clear();
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

        float clampedCoverage = Mathf.Clamp(coveragePercent, 20f, 70f) / 100f;
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
