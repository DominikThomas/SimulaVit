using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class VentVisualizer : MonoBehaviour
{
    [Header("References")]
    public PlanetResourceMap resourceMap;
    public PlanetGenerator planetGenerator;
    public Transform parent;
    public Material ventGlowMaterial;
    public ParticleSystem sootPrefab;

    [Header("Glow Settings")]
    public float glowRadiusMin = 0.05f;
    public float glowRadiusMax = 0.15f;
    public float glowEmissionMin = 0.5f;
    public float glowEmissionMax = 3.0f;
    public float surfaceOffset = 0.01f;
    public bool showLandVents = true;
    public bool showUnderwaterVents = true;

    [Header("Vent Clustering")]
    [Tooltip("Higher merges vents more aggressively into patches. 1–3 is typical.")]
    public float clusterAngleMultiplier = 2f;

    [Tooltip("Skip tiny clusters (helps remove speckle). 0 disables.")]
    public float minClusterStrengthToRender = 0f;

    private static Mesh markerMesh;
    private static Mesh quadMesh;

    private int spawnedVentIndex = 0;

    private void Awake()
    {
        if (resourceMap == null)
        {
            resourceMap = GetComponent<PlanetResourceMap>();
        }

        if (planetGenerator == null)
        {
            planetGenerator = GetComponent<PlanetGenerator>();
        }

        if (parent == null)
        {
            parent = transform;
        }
    }

    private IEnumerator Start()
    {
        // wait 1 frame
        yield return null;

        // wait for PlanetResourceMap init
        float timeout = 5f; // avoid infinite loop
        while (timeout > 0f &&
              (resourceMap == null ||
               resourceMap.ventStrength == null ||
               resourceMap.CellDirs == null ||
               resourceMap.CellDirs.Length == 0 ||
               resourceMap.VentCells == null))
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (timeout <= 0f)
        {
            Debug.LogWarning("[VentVisualizer] Timed out waiting for PlanetResourceMap initialization.");
            yield break;
        }

        Debug.Log($"[VentVisualizer] Ready. Vent count: {resourceMap.VentCells.Length}");
        BuildVentVisuals();
    }

    public void BuildVentVisuals()
    {
        if (resourceMap == null || planetGenerator == null || resourceMap.ventStrength == null)
        {
            Debug.Log($"[VentVisualizer] resourceMap == null || planetGenerator == null || resourceMap.ventStrength == null.");
            return;
        }

        int[] vents = resourceMap.VentCells;
        Vector3[] cellDirs = resourceMap.CellDirs;
        if (cellDirs == null || cellDirs.Length == 0)
        {
            Debug.Log($"[VentVisualizer] cellDirs == null || cellDirs.Length == 0.");
            return;
        }

        float oceanRadius = planetGenerator.GetOceanRadius();
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        if (vents != null && vents.Length > 0)
        {
            int resolution = Mathf.Max(2, planetGenerator.resolution);

            // Build clusters
            List<VentCluster> clusters = BuildClusters(vents, cellDirs, resourceMap.ventStrength, resolution);

            int spawned = 0;
            for (int k = 0; k < clusters.Count; k++)
            {
                var cl = clusters[k];

                if (minClusterStrengthToRender > 0f && cl.strengthMax < minClusterStrengthToRender)
                    continue;

                SpawnClusterVisual(cl, oceanRadius, propertyBlock);
                spawned++;
            }

            Debug.Log($"[VentVisualizer] Spawned {spawned} vent clusters (from {vents.Length} vent cells).");
            return;
        }
        else
        {
            Debug.Log($"[VentVisualizer] No vents exist yet. Count is: {(vents == null ? 0 : vents.Length)}.");
        }

        for (int cell = 0; cell < resourceMap.ventStrength.Length; cell++)
        {
            if (resourceMap.ventStrength[cell] > 0f)
            {
                BuildVentVisual(cell, oceanRadius, cellDirs, propertyBlock);
            }
        }
    }

    private void BuildVentVisual(int cell, float oceanRadius, Vector3[] cellDirs, MaterialPropertyBlock propertyBlock)
    {
        if (cell < 0 || cell >= cellDirs.Length || cell >= resourceMap.ventStrength.Length)
        {
            return;
        }

        float strength = resourceMap.ventStrength[cell];
        if (strength <= 0f)
        {
            return;
        }

        Vector3 dir = cellDirs[cell].normalized;
        float surfaceRadius = planetGenerator.GetSurfaceRadius(dir);
        bool underwater = surfaceRadius < oceanRadius;

        if ((!underwater && !showLandVents) || (underwater && !showUnderwaterVents))
        {
            return;
        }

        float normalizedStrength = Mathf.InverseLerp(resourceMap.ventStrengthMin, resourceMap.ventStrengthMax, strength);
        float scale = Mathf.Lerp(glowRadiusMin, glowRadiusMax, normalizedStrength);
        float emission = Mathf.Lerp(glowEmissionMin, glowEmissionMax, normalizedStrength);

        Vector3 worldPos = planetGenerator.transform.position + dir * (surfaceRadius + surfaceOffset);

        GameObject marker = new GameObject($"Vent_{cell}");
        marker.transform.SetParent(parent, true);
        marker.transform.position = worldPos;
        marker.transform.rotation = Quaternion.LookRotation(-dir);
        marker.transform.localScale = Vector3.one * scale;

        MeshFilter filter = marker.AddComponent<MeshFilter>();
        filter.sharedMesh = GetQuadMesh();

        MeshRenderer renderer = marker.AddComponent<MeshRenderer>();
        if (ventGlowMaterial != null)
        {
            renderer.sharedMaterial = ventGlowMaterial;
        }

        propertyBlock.Clear();
        propertyBlock.SetColor("_EmissionColor", Color.red * emission);
        renderer.SetPropertyBlock(propertyBlock);

        if (underwater)
        {
            AttachSoot(marker.transform, normalizedStrength);
        }
    }

    private void AttachSoot(Transform marker, float normalizedStrength)
    {
        ParticleSystem soot = sootPrefab != null
            ? Instantiate(sootPrefab, marker)
            : CreateRuntimeSoot(marker);

        soot.transform.localPosition = Vector3.zero;
        soot.transform.localRotation = Quaternion.identity;

        ParticleSystem.EmissionModule emission = soot.emission;
        emission.rateOverTime = Mathf.Lerp(3f, 18f, normalizedStrength);

        ParticleSystem.MainModule main = soot.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0.05f, 0.2f, normalizedStrength));

        if (!soot.isPlaying)
        {
            soot.Play(true);
        }
    }

    private ParticleSystem CreateRuntimeSoot(Transform marker)
    {
        GameObject sootObj = new GameObject("Soot");
        sootObj.transform.SetParent(marker, false);

        ParticleSystem ps = sootObj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer psRenderer = sootObj.GetComponent<ParticleSystemRenderer>();
        psRenderer.renderMode = ParticleSystemRenderMode.Billboard;

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.05f, 0.05f, 0.05f, 0.55f), new Color(0.15f, 0.15f, 0.15f, 0.35f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 8f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = 0.03f;
        shape.angle = 20f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fadeGradient = new Gradient();
        fadeGradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.08f, 0.08f, 0.08f), 0f),
                new GradientColorKey(new Color(0.18f, 0.18f, 0.18f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.55f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = fadeGradient;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.8f),
            new Keyframe(1f, 1.35f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.08f;
        noise.frequency = 0.35f;

        return ps;
    }

    private static Mesh GetMarkerMesh()
    {
        if (markerMesh != null)
        {
            return markerMesh;
        }

        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        MeshFilter source = primitive.GetComponent<MeshFilter>();
        markerMesh = source != null ? source.sharedMesh : null;
        Object.DestroyImmediate(primitive);
        return markerMesh;
    }

    private Mesh GetQuadMesh()
    {
        if (quadMesh != null)
            return quadMesh;

        quadMesh = new Mesh();
        quadMesh.name = "VentQuad";

        quadMesh.vertices = new Vector3[]
        {
        new Vector3(-0.5f, -0.5f, 0f),
        new Vector3( 0.5f, -0.5f, 0f),
        new Vector3(-0.5f,  0.5f, 0f),
        new Vector3( 0.5f,  0.5f, 0f)
        };

        quadMesh.uv = new Vector2[]
        {
        new Vector2(0f, 0f),
        new Vector2(1f, 0f),
        new Vector2(0f, 1f),
        new Vector2(1f, 1f)
        };

        quadMesh.triangles = new int[]
        {
        0, 2, 1,
        2, 3, 1
        };

        quadMesh.RecalculateNormals();
        return quadMesh;
    }

    private class VentCluster
    {
        public Vector3 weightedDirSum;
        public float strengthSum;
        public float strengthMax;
        public int count;

        public Vector3 CenterDir
        {
            get
            {
                if (weightedDirSum.sqrMagnitude <= 1e-12f) return Vector3.up;
                return weightedDirSum.normalized;
            }
        }

        public float NormalizedStrength(float minS, float maxS)
        {
            // Use max strength (or sum) to drive emission; max is usually nicer/stable.
            float s = strengthMax;
            return Mathf.InverseLerp(minS, maxS, s);
        }
    }

    private List<VentCluster> BuildClusters(int[] vents, Vector3[] cellDirs, float[] ventStrength, int resolution)
    {
        var clusters = new List<VentCluster>(Mathf.Max(4, vents.Length / 8));

        // Approx tile angular size on unit sphere (very rough but good enough):
        // For resolution R, a face spans ~90 degrees. Tile ~ (90deg / (R-1)).
        float safeR = Mathf.Max(2, resolution);
        float tileAngleRad = (Mathf.PI * 0.5f) / (safeR - 1f);
        float maxAngle = tileAngleRad * Mathf.Max(0.1f, clusterAngleMultiplier);
        float cosThreshold = Mathf.Cos(maxAngle);

        for (int i = 0; i < vents.Length; i++)
        {
            int cell = vents[i];
            if (cell < 0 || cell >= cellDirs.Length || cell >= ventStrength.Length) continue;

            float s = ventStrength[cell];
            if (s <= 0f) continue;

            Vector3 dir = cellDirs[cell].normalized;

            // Assign to an existing cluster if close enough to its center direction.
            int bestIndex = -1;
            float bestDot = -1f;

            for (int c = 0; c < clusters.Count; c++)
            {
                float d = Vector3.Dot(dir, clusters[c].CenterDir);
                if (d > cosThreshold && d > bestDot)
                {
                    bestDot = d;
                    bestIndex = c;
                }
            }

            if (bestIndex < 0)
            {
                var nc = new VentCluster
                {
                    weightedDirSum = dir * s,
                    strengthSum = s,
                    strengthMax = s,
                    count = 1
                };
                clusters.Add(nc);
            }
            else
            {
                var cl = clusters[bestIndex];
                cl.weightedDirSum += dir * s;
                cl.strengthSum += s;
                cl.strengthMax = Mathf.Max(cl.strengthMax, s);
                cl.count++;
            }
        }

        return clusters;
    }

    private void SpawnClusterVisual(VentCluster cl, float oceanRadius, MaterialPropertyBlock propertyBlock)
    {
        Vector3 dir = cl.CenterDir;

        float surfaceRadius = planetGenerator.GetSurfaceRadius(dir);
        bool underwater = surfaceRadius < oceanRadius;

        if ((!underwater && !showLandVents) || (underwater && !showUnderwaterVents))
            return;

        float normalizedStrength = cl.NormalizedStrength(resourceMap.ventStrengthMin, resourceMap.ventStrengthMax);

        // Scale can depend on strength AND cluster size (count). sqrt gives nice growth.
        float sizeBoost = Mathf.Sqrt(Mathf.Max(1, cl.count));
        float scale = Mathf.Lerp(glowRadiusMin, glowRadiusMax, normalizedStrength) * Mathf.Lerp(1f, 1.8f, Mathf.Clamp01((sizeBoost - 1f) / 4f));
        float emission = Mathf.Lerp(glowEmissionMin, glowEmissionMax, normalizedStrength) * Mathf.Lerp(1f, 1.5f, Mathf.Clamp01((sizeBoost - 1f) / 4f));

        Vector3 worldPos = planetGenerator.transform.position + dir * (surfaceRadius + surfaceOffset);

        GameObject marker = new GameObject($"VentCluster_{spawnedVentIndex++}_n{cl.count}");
        marker.transform.SetParent(parent, true);
        marker.transform.position = worldPos;
        marker.transform.rotation = Quaternion.LookRotation(-dir);
        marker.transform.localScale = Vector3.one * scale;

        MeshFilter filter = marker.AddComponent<MeshFilter>();
        filter.sharedMesh = GetQuadMesh();

        MeshRenderer renderer = marker.AddComponent<MeshRenderer>();
        if (ventGlowMaterial != null)
            renderer.sharedMaterial = ventGlowMaterial;

        propertyBlock.Clear();
        propertyBlock.SetColor("_EmissionColor", Color.red * emission);
        renderer.SetPropertyBlock(propertyBlock);

        if (underwater)
            AttachSoot(marker.transform, normalizedStrength);
    }

}
