using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class UnderwaterVolumeController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlanetGenerator planet;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Volume underwaterVolume;

    [Header("Underwater Detection")]
    [Tooltip("Extra world-space margin above sea level where underwater blend can already begin.")]
    [Min(0f)]
    [SerializeField] private float seaLevelEnterThreshold = 0.05f;

    [Tooltip("How deep below sea level (world units) to reach full underwater weight.")]
    [Min(0.001f)]
    [SerializeField] private float fullEffectDepth = 1.5f;

    [Header("Blend")]
    [Tooltip("How fast the effect blends in when entering water.")]
    [Min(0f)]
    [SerializeField] private float blendInSpeed = 2.5f;

    [Tooltip("How fast the effect blends out when leaving water.")]
    [Min(0f)]
    [SerializeField] private float blendOutSpeed = 3f;

    [Tooltip("Use unscaled delta time so blending is unaffected by timescale changes.")]
    [SerializeField] private bool useUnscaledTime;

    [Header("Underwater Fog")]
    [SerializeField] private bool controlGlobalFog = true;

    [SerializeField] private Color underwaterFogColor = new Color(0.12f, 0.32f, 0.38f, 1f);

    [Tooltip("Fog density when fully underwater, if using exponential fog.")]
    [Min(0f)]
    [SerializeField] private float underwaterFogDensity = 0.08f;

    [Tooltip("Start distance for linear fog when fully underwater.")]
    [Min(0f)]
    [SerializeField] private float underwaterFogStartDistance = 0f;

    [Tooltip("End distance for linear fog when fully underwater.")]
    [Min(0f)]
    [SerializeField] private float underwaterFogEndDistance = 12f;

    [Tooltip("If true, fog gets stronger the deeper you go below sea level.")]
    [SerializeField] private bool scaleFogByDepth = true;

    private bool originalFogEnabled;
    private FogMode originalFogMode;
    private Color originalFogColor;
    private float originalFogDensity;
    private float originalFogStartDistance;
    private float originalFogEndDistance;
    private bool fogStateCaptured;

    private float currentWeight;

    private void Reset()
    {
        cameraTransform = transform;
        planet = FindFirstObjectByType<PlanetGenerator>();
        underwaterVolume = FindFirstObjectByType<Volume>();
    }

    private void Awake()
    {
        ResolveReferences();
        currentWeight = underwaterVolume != null ? Mathf.Clamp01(underwaterVolume.weight) : 0f;
    }

    private void OnEnable()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        seaLevelEnterThreshold = Mathf.Max(0f, seaLevelEnterThreshold);
        fullEffectDepth = Mathf.Max(0.001f, fullEffectDepth);
        blendInSpeed = Mathf.Max(0f, blendInSpeed);
        blendOutSpeed = Mathf.Max(0f, blendOutSpeed);
    }

    private void Update()
    {
        if (!ResolveReferences())
        {
            return;
        }

        float targetWeight = CalculateTargetWeight();
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float speed = targetWeight > currentWeight ? blendInSpeed : blendOutSpeed;

        if (speed <= 0f)
        {
            currentWeight = targetWeight;
        }
        else
        {
            currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, speed * deltaTime);
        }

        underwaterVolume.weight = currentWeight;
        ApplyFog(100f*currentWeight);
    }

    private bool ResolveReferences()
    {
        if (planet == null)
        {
            planet = FindFirstObjectByType<PlanetGenerator>();
        }

        if (cameraTransform == null)
        {
            cameraTransform = transform;
        }

        return planet != null && cameraTransform != null && underwaterVolume != null;
    }

    private float CalculateTargetWeight()
    {
        float oceanRadiusWorld = GetOceanRadiusWorld();
        Vector3 fromCenter = cameraTransform.position - planet.transform.position;
        float distanceFromCenter = fromCenter.magnitude;

        // Positive when camera is below sea level.
        float depthBelowSeaLevel = oceanRadiusWorld - distanceFromCenter;

        if (depthBelowSeaLevel <= -seaLevelEnterThreshold)
        {
            return 0f;
        }

        float depthWithThreshold = depthBelowSeaLevel + seaLevelEnterThreshold;
        return Mathf.Clamp01(depthWithThreshold / fullEffectDepth);
    }

    private float GetOceanRadiusWorld()
    {
        float lossyScaleMax = Mathf.Max(
            Mathf.Abs(planet.transform.lossyScale.x),
            Mathf.Abs(planet.transform.lossyScale.y),
            Mathf.Abs(planet.transform.lossyScale.z));

        return planet.GetOceanRadius() * lossyScaleMax;
    }

    private void CaptureOriginalFogSettings()
    {
        if (fogStateCaptured)
        {
            return;
        }

        originalFogEnabled = RenderSettings.fog;
        originalFogMode = RenderSettings.fogMode;
        originalFogColor = RenderSettings.fogColor;
        originalFogDensity = RenderSettings.fogDensity;
        originalFogStartDistance = RenderSettings.fogStartDistance;
        originalFogEndDistance = RenderSettings.fogEndDistance;
        fogStateCaptured = true;
    }

    private void ApplyFog(float underwaterWeight)
    {
        if (!controlGlobalFog)
        {
            return;
        }

        CaptureOriginalFogSettings();

        if (underwaterWeight <= 0.0001f)
        {
            RenderSettings.fog = originalFogEnabled;
            RenderSettings.fogMode = originalFogMode;
            RenderSettings.fogColor = originalFogColor;
            RenderSettings.fogDensity = originalFogDensity;
            RenderSettings.fogStartDistance = originalFogStartDistance;
            RenderSettings.fogEndDistance = originalFogEndDistance;
            return;
        }

        RenderSettings.fog = true;

        // Keep the current project fog mode if it already existed,
        // otherwise use Linear as a predictable default.
        FogMode targetFogMode = originalFogEnabled ? originalFogMode : FogMode.Linear;
        RenderSettings.fogMode = targetFogMode;

        float fogStrength = underwaterWeight;

        if (scaleFogByDepth)
        {
            fogStrength *= underwaterWeight;
        }

        RenderSettings.fogColor = Color.Lerp(originalFogColor, underwaterFogColor, underwaterWeight);

        if (targetFogMode == FogMode.Linear)
        {
            float start = Mathf.Lerp(originalFogStartDistance, underwaterFogStartDistance, fogStrength);
            float end = Mathf.Lerp(originalFogEndDistance > 0f ? originalFogEndDistance : 200f, underwaterFogEndDistance, fogStrength);

            if (end < start + 0.01f)
            {
                end = start + 0.01f;
            }

            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = end;
        }
        else
        {
            float density = Mathf.Lerp(originalFogDensity, underwaterFogDensity, fogStrength);
            RenderSettings.fogDensity = density;
        }
    }

    private void OnDisable()
    {
        if (!fogStateCaptured)
        {
            return;
        }

        RenderSettings.fog = originalFogEnabled;
        RenderSettings.fogMode = originalFogMode;
        RenderSettings.fogColor = originalFogColor;
        RenderSettings.fogDensity = originalFogDensity;
        RenderSettings.fogStartDistance = originalFogStartDistance;
        RenderSettings.fogEndDistance = originalFogEndDistance;
    }
}
