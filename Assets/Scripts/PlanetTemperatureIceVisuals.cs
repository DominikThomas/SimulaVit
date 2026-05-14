using UnityEngine;

[DisallowMultipleComponent]
public class PlanetTemperatureIceVisuals : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlanetGenerator planetGenerator;
    [SerializeField] private PlanetResourceMap planetResourceMap;
    [SerializeField] private ReplicatorManager replicatorManager;

    [Header("Land Ice Visuals")]
    public bool enableTemperatureLandIce = true;
    [Min(50f)] public float landIceThresholdKelvin = 273.15f;
    [Min(0.01f)] public float landIceFadeKelvin = 3f;
    [Range(32, 2048)] public int landIceMaskResolutionX = 512;
    [Range(16, 1024)] public int landIceMaskResolutionY = 256;
    [Range(0, 4)] public int landIceMaskBlurPasses = 2;
    [Range(0f, 1f)] public float landIceMaskBlurStrength = 0.5f;
    [Min(0.05f)] public float iceMaskUpdateIntervalSeconds = 1.5f;
    public Color landIceColor = new Color(0.88f, 0.93f, 0.98f, 1f);
    [Range(0f, 2f)] public float landIceStrength = 1f;
    public bool forceIceMaskPreview = false;

    [Header("Sea Ice Scaffold (Visual Only)")]
    public bool enableTemperatureSeaIce = false;
    [Min(50f)] public float seaIceThresholdKelvin = 269.15f;
    [Min(0.01f)] public float seaIceFadeKelvin = 3f;
    [Range(1.0001f, 1.05f)] public float seaIceRadiusMultiplier = 1.002f;

    private Texture2D landIceMaskTexture;
    private Color32[] landIcePixels;
    private float[] landIceMaskValues;
    private float[] landIceMaskBlurBuffer;
    private Material planetMaterial;

    private double lastSimulationTime = double.NegativeInfinity;
    private double nextUpdateSimulationTime;
    private int lastMaskWidth;
    private int lastMaskHeight;

    private static readonly int IceMaskId = Shader.PropertyToID("_IceMask");
    private static readonly int IceColorId = Shader.PropertyToID("_IceColor");
    private static readonly int IceStrengthId = Shader.PropertyToID("_IceStrength");
    private static readonly int ForceIceMaskPreviewId = Shader.PropertyToID("_ForceIceMaskPreview");

    private void Awake()
    {
        ResolveReferences();
        TryBindPlanetMaterial();
        EnsureLandIceTexture();
        PushStaticMaterialParams();
        RefreshNow();
    }

    private void OnEnable()
    {
        ResolveReferences();
        TryBindPlanetMaterial();
        EnsureLandIceTexture();
        PushStaticMaterialParams();
        RefreshNow();
    }

    private void Update()
    {
        if (!enableTemperatureLandIce || planetResourceMap == null || planetGenerator == null)
        {
            return;
        }

        double simTime = GetSimulationTimeSeconds();
        if (!double.IsFinite(simTime))
        {
            return;
        }

        if (simTime <= lastSimulationTime)
        {
            return;
        }

        if (simTime < nextUpdateSimulationTime)
        {
            return;
        }

        RefreshNow();
    }

    private void OnValidate()
    {
        landIceMaskResolutionX = Mathf.Clamp(landIceMaskResolutionX, 32, 2048);
        landIceMaskResolutionY = Mathf.Clamp(landIceMaskResolutionY, 16, 1024);
        landIceFadeKelvin = Mathf.Max(0.01f, landIceFadeKelvin);
        seaIceFadeKelvin = Mathf.Max(0.01f, seaIceFadeKelvin);
        landIceMaskBlurPasses = Mathf.Clamp(landIceMaskBlurPasses, 0, 4);
        landIceMaskBlurStrength = Mathf.Clamp01(landIceMaskBlurStrength);
        iceMaskUpdateIntervalSeconds = Mathf.Max(0.05f, iceMaskUpdateIntervalSeconds);

        if (!Application.isPlaying)
        {
            return;
        }

        EnsureLandIceTexture();
        PushStaticMaterialParams();
    }

    [ContextMenu("Refresh Ice Mask Now")]
    public void RefreshNow()
    {
        ResolveReferences();
        if (planetResourceMap == null || planetGenerator == null)
        {
            return;
        }

        EnsureLandIceTexture();
        PushStaticMaterialParams();
        UpdateLandIceMask();

        lastSimulationTime = GetSimulationTimeSeconds();
        nextUpdateSimulationTime = lastSimulationTime + iceMaskUpdateIntervalSeconds;
    }

    private void ResolveReferences()
    {
        if (planetGenerator == null)
        {
            planetGenerator = GetComponent<PlanetGenerator>();
        }

        if (planetResourceMap == null)
        {
            planetResourceMap = GetComponent<PlanetResourceMap>();
            if (planetResourceMap == null)
            {
                planetResourceMap = FindFirstObjectByType<PlanetResourceMap>();
            }
        }

        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }
    }

    private void TryBindPlanetMaterial()
    {
        if (planetGenerator == null)
        {
            return;
        }

        MeshRenderer renderer = planetGenerator.GetComponent<MeshRenderer>();
        planetMaterial = renderer != null ? renderer.sharedMaterial : null;
    }

    private void EnsureLandIceTexture()
    {
        if (landIceMaskTexture != null && lastMaskWidth == landIceMaskResolutionX && lastMaskHeight == landIceMaskResolutionY)
        {
            return;
        }

        if (landIceMaskTexture != null)
        {
            Destroy(landIceMaskTexture);
        }

        landIceMaskTexture = new Texture2D(landIceMaskResolutionX, landIceMaskResolutionY, TextureFormat.R8, false, true)
        {
            name = "Planet Land Ice Mask Runtime",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        lastMaskWidth = landIceMaskResolutionX;
        lastMaskHeight = landIceMaskResolutionY;
        landIcePixels = new Color32[lastMaskWidth * lastMaskHeight];
        landIceMaskValues = new float[lastMaskWidth * lastMaskHeight];
        landIceMaskBlurBuffer = new float[lastMaskWidth * lastMaskHeight];

        if (planetMaterial != null)
        {
            planetMaterial.SetTexture(IceMaskId, landIceMaskTexture);
        }
    }

    private void PushStaticMaterialParams()
    {
        if (planetMaterial == null)
        {
            TryBindPlanetMaterial();
            if (planetMaterial == null)
            {
                return;
            }
        }

        planetMaterial.SetColor(IceColorId, landIceColor);
        planetMaterial.SetFloat(IceStrengthId, landIceStrength);
        planetMaterial.SetFloat(ForceIceMaskPreviewId, forceIceMaskPreview ? 1f : 0f);
        if (landIceMaskTexture != null)
        {
            planetMaterial.SetTexture(IceMaskId, landIceMaskTexture);
        }
    }

    private void UpdateLandIceMask()
    {
        if (landIceMaskTexture == null || landIcePixels == null)
        {
            return;
        }

        float threshold = landIceThresholdKelvin;
        float fade = Mathf.Max(0.01f, landIceFadeKelvin);

        for (int y = 0; y < lastMaskHeight; y++)
        {
            float v = (y + 0.5f) / lastMaskHeight;

            for (int x = 0; x < lastMaskWidth; x++)
            {
                float u = (x + 0.5f) / lastMaskWidth;
                Vector3 dir = SphericalUvToDirection(u, v);

                float iceValue = 0f;
                if (!planetGenerator.IsOceanAtDirection(dir))
                {
                    int cell = planetResourceMap.GetCellIndexFromDirection(dir);
                    float tempKelvin = planetResourceMap.GetTemperature(dir, cell);
                    float t = Mathf.InverseLerp(threshold + fade, threshold - fade, tempKelvin);
                    iceValue = Mathf.Clamp01(t);
                }

                int index = (y * lastMaskWidth) + x;
                landIceMaskValues[index] = iceValue;
            }
        }

        ApplyMaskBlur();

        for (int i = 0; i < landIceMaskValues.Length; i++)
        {
            byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(landIceMaskValues[i]) * 255f);
            landIcePixels[i] = new Color32(b, b, b, 255);
        }

        landIceMaskTexture.SetPixelData(landIcePixels, 0);
        landIceMaskTexture.Apply(false, false);
    }

    private void ApplyMaskBlur()
    {
        if (landIceMaskBlurPasses <= 0 || landIceMaskBlurStrength <= 0f || landIceMaskValues == null || landIceMaskBlurBuffer == null)
        {
            return;
        }

        float strength = Mathf.Clamp01(landIceMaskBlurStrength);
        float neighborWeight = strength / 4f;
        float centerWeight = 1f - strength;

        for (int pass = 0; pass < landIceMaskBlurPasses; pass++)
        {
            for (int y = 0; y < lastMaskHeight; y++)
            {
                int yUp = Mathf.Min(y + 1, lastMaskHeight - 1);
                int yDown = Mathf.Max(y - 1, 0);

                for (int x = 0; x < lastMaskWidth; x++)
                {
                    int xLeft = (x - 1 + lastMaskWidth) % lastMaskWidth;
                    int xRight = (x + 1) % lastMaskWidth;

                    int idx = y * lastMaskWidth + x;
                    float center = landIceMaskValues[idx];
                    float up = landIceMaskValues[yUp * lastMaskWidth + x];
                    float down = landIceMaskValues[yDown * lastMaskWidth + x];
                    float left = landIceMaskValues[y * lastMaskWidth + xLeft];
                    float right = landIceMaskValues[y * lastMaskWidth + xRight];

                    float blurred = (center * centerWeight) + ((up + down + left + right) * neighborWeight);
                    landIceMaskBlurBuffer[idx] = Mathf.Clamp01(blurred);
                }
            }

            float[] swap = landIceMaskValues;
            landIceMaskValues = landIceMaskBlurBuffer;
            landIceMaskBlurBuffer = swap;
        }
    }

    private static Vector3 SphericalUvToDirection(float u, float v)
    {
        float phi = (1f - v) * Mathf.PI;
        float theta = u * Mathf.PI * 2f;
        float sinPhi = Mathf.Sin(phi);
        float cosPhi = Mathf.Cos(phi);
        return new Vector3(
            sinPhi * Mathf.Cos(theta),
            cosPhi,
            sinPhi * Mathf.Sin(theta)).normalized;
    }

    private double GetSimulationTimeSeconds()
    {
        if (replicatorManager == null)
        {
            return Time.timeSinceLevelLoad;
        }

        return replicatorManager.SimulationTimeSeconds;
    }

    private void OnDestroy()
    {
        if (landIceMaskTexture != null)
        {
            Destroy(landIceMaskTexture);
            landIceMaskTexture = null;
        }
    }
}
