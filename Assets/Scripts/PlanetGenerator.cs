using System.Collections.Generic;
using UnityEngine;

public class PlanetGenerator : MonoBehaviour
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

    public MeshRenderer OceanRenderer => oceanMeshRenderer;
    public IReadOnlyList<float> LocalOceanDepths => localOceanDepthByCell;
    public int VisualResolution => Mathf.Max(1, resolution);

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
    }

    void Start()
    {
        if (randomizeOnStart)
        {
            RandomizeGenerationSettings();
        }

        GeneratePlanet();
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
        if (oceanMaterial != null)
        {
            oceanMeshRenderer.sharedMaterial = oceanMaterial;
        }
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
            oceanMeshRenderer.enabled = enableOcean;
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

        UpdateSurfaceMaterialProperties();
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
            return radius;
        }

        return radius * (1f + oceanNoiseThreshold * noiseMagnitude);
    }

    public SurfaceQueryParameters GetSurfaceQueryParameters()
    {
        return new SurfaceQueryParameters
        {
            Radius = radius,
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

        return radius;
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
