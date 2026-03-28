using System.Collections.Generic;
using UnityEngine;

public class PlanetGenerator : MonoBehaviour
{
    [SerializeField] private ReplicatorManager replicatorManager;
    [Range(3, 240)]
    public int resolution = 10;
    public float radius = 1f;
    public Material planetMaterial;

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
    private Mesh mesh;

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

    void Awake()
    {
        meshFilter = GetOrAddComponent<MeshFilter>(gameObject);
        MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(gameObject);
        MeshCollider meshCollider = GetOrAddComponent<MeshCollider>(gameObject);

        if (planetMaterial != null)
        {
            meshRenderer.sharedMaterial = planetMaterial;
        }

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
        int cellCount = unitVertices.Count;

        Vector3[] terrainVertices = new Vector3[cellCount];
        Vector3[] oceanVertices = new Vector3[cellCount];
        Vector3[] atmosphereVertices = new Vector3[cellCount];
        float[] finalTerrainRadii = new float[cellCount];

        int[] neighbors = BuildCellNeighborLookup(cellCount, allTriangles);
        BuildOceanBathymetry(unitVertices, noiseSamples, finalTerrainRadii, seaRadius, neighbors);

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
        mesh.triangles = allTriangles.ToArray();
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

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
        oceanMesh.triangles = allTriangles.ToArray();
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
        atmosphereMesh.triangles = allTriangles.ToArray();
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

    public float GetSurfaceRadiusFromNoise(float noise)
    {
        float seaNoise = oceanNoiseThreshold;
        float finalNoise = noise;

        if (enableOcean && noise < seaNoise)
        {
            float t = seaNoise > 0f ? Mathf.Clamp01(noise / seaNoise) : 0f;
            float minNoise = seaNoise * (1f - oceanDepth);
            finalNoise = Mathf.Lerp(minNoise, seaNoise, t);
        }

        return radius * (1f + finalNoise * noiseMagnitude);
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

        float shelfDistanceSafe = Mathf.Max(1f, shelfDistance);
        float shelfDepthSafe = Mathf.Clamp(shelfDepth, 0f, Mathf.Max(0f, maxOceanDepth));
        float maxDepthSafe = Mathf.Max(shelfDepthSafe, maxOceanDepth);
        float slopeStrengthSafe = Mathf.Max(0f, slopeStrength);
        float basinScale = Mathf.Max(0.001f, basinNoiseScale);
        float basinStrength = Mathf.Clamp01(basinNoiseStrength);
        float falloffRange = Mathf.Max(1f, maxDistance - shelfDistanceSafe);

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

            localOceanDepthByCell[cell] = depthTarget;
            float oceanFloorRadius = Mathf.Max(0.01f, seaRadius - depthTarget);
            finalTerrainRadii[cell] = Mathf.Min(seaRadius, oceanFloorRadius);
            generatedSurfaceRadiusByCell[cell] = finalTerrainRadii[cell];
        }
    }

    static int[] BuildCellNeighborLookup(int cellCount, List<int> triangles)
    {
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
        float noiseValue = 0;
        float frequency = noiseRoughness;
        float amplitude = 1;
        float maxPossibleHeight = 0;

        for (int i = 0; i < numLayers; i++)
        {
            Vector3 samplePoint = pointOnSphere * frequency + noiseOffset;
            float singleLayerNoise = SimpleNoise.Evaluate(samplePoint);
            singleLayerNoise = (singleLayerNoise + 1) * 0.5f;

            noiseValue += singleLayerNoise * amplitude;
            maxPossibleHeight += amplitude;

            amplitude *= persistence;
            frequency *= 2;
        }

        return maxPossibleHeight > 0f ? noiseValue / maxPossibleHeight : 0f;
    }
}
