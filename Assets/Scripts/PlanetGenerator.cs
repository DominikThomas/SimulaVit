using System.Collections.Generic;
using UnityEngine;

public class PlanetGenerator : MonoBehaviour
{
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

    private float oceanNoiseThreshold;

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

        Vector3[] terrainVertices = new Vector3[unitVertices.Count];
        Vector3[] oceanVertices = new Vector3[unitVertices.Count];

        for (int i = 0; i < unitVertices.Count; i++)
        {
            Vector3 dir = unitVertices[i];
            float terrainRadius = GetSurfaceRadiusFromNoise(noiseSamples[i]);
            float seaRadius = GetOceanRadius();

            terrainVertices[i] = dir * terrainRadius;
            oceanVertices[i] = dir * seaRadius;
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
            if (oceanMaterial != null)
            {
                oceanMeshRenderer.sharedMaterial = oceanMaterial;
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
    public bool PhotosynthesisUnlocked => Time.timeSinceLevelLoad >= photosynthesisUnlockSeconds;
    public bool SaprotrophyUnlocked => Time.timeSinceLevelLoad >= saprotrophyUnlockSeconds;

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
