using UnityEngine;

[RequireComponent(typeof(Light))]
public class SunSkyRotator : MonoBehaviour
{
    [Header("Orbit")]
    public float orbitDegreesPerSecond = 0.75f;
    public Vector3 orbitAxis = Vector3.up;

    [Header("Sun Visual")]
    public float sunDistance = 250f;
    public float sunScale = 8f;
    public Color sunColor = new Color(1f, 0.9f, 0.6f, 1f);
    [Range(0f, 1f)] public float sunSurfaceSmoothness = 0.95f;
    [Range(0f, 1f)] public float sunMetallic = 0.1f;
    [Min(0f)] public float sunEmissionIntensity = 12f;

    [Header("Camera-Relative Color Shift")]
    public Transform planetCenter;
    public Transform viewer;
    public Color horizonColor = new Color(1f, 0.45f, 0.2f, 1f);
    public Color dayColor = new Color(1f, 0.95f, 0.75f, 1f);
    public Color nightColor = new Color(0.2f, 0.25f, 0.35f, 1f);
    [Tooltip("How wide the sunrise/sunset band is around the horizon line.")]
    [Range(0.01f, 1f)] public float horizonBand = 0.3f;
    [Range(0f, 1f)] public float colorShiftStrength = 1f;

    [Header("Skybox")]
    public bool rotateSkybox = true;
    [Tooltip("Use -1 to match the light orbit direction for this skybox shader.")]
    public float skyboxRotationMultiplier = -1f;
    public Material skyboxOverride;

    private Quaternion initialRotation;
    private float accumulatedOrbitAngle;

    private Material originalSkybox;
    private Material runtimeSkybox;

    private GameObject generatedSunObject;
    private Material runtimeSunMaterial;

    void Start()
    {
        initialRotation = transform.rotation;
        SetupViewerReference();
        SetupSkybox();
        CreateSunVisual();
        UpdateSunVisualPosition();
        UpdateSunVisualAppearance();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        accumulatedOrbitAngle += orbitDegreesPerSecond * dt;
        Quaternion orbitRotation = Quaternion.AngleAxis(accumulatedOrbitAngle, orbitAxis.normalized);
        transform.rotation = orbitRotation * initialRotation;

        if (rotateSkybox && runtimeSkybox != null && runtimeSkybox.HasFloat("_Rotation"))
        {
            float current = runtimeSkybox.GetFloat("_Rotation");
            runtimeSkybox.SetFloat("_Rotation", current + orbitDegreesPerSecond * skyboxRotationMultiplier * dt);
        }

        UpdateSunVisualPosition();
        UpdateSunVisualAppearance();
    }

    void OnDestroy()
    {
        if (runtimeSkybox != null && RenderSettings.skybox == runtimeSkybox)
        {
            RenderSettings.skybox = originalSkybox;
        }

        DestroyRuntimeObject(generatedSunObject);
        DestroyRuntimeObject(runtimeSunMaterial);
        DestroyRuntimeObject(runtimeSkybox);
    }

    void SetupViewerReference()
    {
        if (viewer == null && Camera.main != null)
        {
            viewer = Camera.main.transform;
        }
    }

    void SetupSkybox()
    {
        originalSkybox = RenderSettings.skybox;
        Material source = skyboxOverride != null ? skyboxOverride : RenderSettings.skybox;
        if (source == null) return;

        runtimeSkybox = new Material(source);
        RenderSettings.skybox = runtimeSkybox;
    }

    void CreateSunVisual()
    {
        generatedSunObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        generatedSunObject.name = "Sun Visual";
        generatedSunObject.transform.localScale = Vector3.one * sunScale;
        RemoveCollider(generatedSunObject);

        Renderer sunRenderer = generatedSunObject.GetComponent<Renderer>();
        if (sunRenderer != null)
        {
            runtimeSunMaterial = BuildSunMaterial();
            if (runtimeSunMaterial != null)
            {
                sunRenderer.sharedMaterial = runtimeSunMaterial;
                sunRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sunRenderer.receiveShadows = false;
            }
        }
    }

    void UpdateSunVisualPosition()
    {
        if (generatedSunObject == null) return;

        generatedSunObject.transform.position = -transform.forward * sunDistance;
        generatedSunObject.transform.localScale = Vector3.one * sunScale;
    }

    void UpdateSunVisualAppearance()
    {
        if (runtimeSunMaterial == null) return;

        Color shiftedColor = EvaluateSunShiftedColor();

        if (runtimeSunMaterial.HasProperty("_BaseColor"))
        {
            runtimeSunMaterial.SetColor("_BaseColor", shiftedColor);
        }
        if (runtimeSunMaterial.HasProperty("_Color"))
        {
            runtimeSunMaterial.SetColor("_Color", shiftedColor);
        }
        if (runtimeSunMaterial.HasProperty("_EmissionColor"))
        {
            runtimeSunMaterial.SetColor("_EmissionColor", shiftedColor * sunEmissionIntensity);
        }
    }

    Color EvaluateSunShiftedColor()
    {
        SetupViewerReference();

        Vector3 center = planetCenter != null ? planetCenter.position : Vector3.zero;
        Vector3 sunDirection = (-transform.forward).normalized;

        // Fallback if no viewer is available.
        if (viewer == null)
        {
            return Color.Lerp(sunColor, dayColor * sunColor, colorShiftStrength);
        }

        // Camera direction from planet center.
        Vector3 viewDirection = (viewer.position - center).normalized;

        // >0 means sun is on visible hemisphere from camera, <0 behind planet.
        float facing = Vector3.Dot(viewDirection, sunDirection);

        // Horizon factor peaks when facing is near 0 (sun near horizon from camera view).
        float horizonFactor = 1f - Mathf.Clamp01(Mathf.Abs(facing) / Mathf.Max(0.0001f, horizonBand));

        // Day amount on visible side, night amount on hidden side.
        float dayAmount = Mathf.Clamp01((facing + 1f) * 0.5f);

        Color baseColor = Color.Lerp(nightColor, dayColor, dayAmount);
        Color finalShifted = Color.Lerp(baseColor, horizonColor, horizonFactor);

        return Color.Lerp(sunColor, finalShifted * sunColor, colorShiftStrength);
    }

    Material BuildSunMaterial()
    {
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader != null)
        {
            Material material = new Material(litShader);
            material.SetFloat("_Metallic", sunMetallic);
            material.SetFloat("_Smoothness", sunSurfaceSmoothness);
            material.EnableKeyword("_EMISSION");
            return material;
        }

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null)
        {
            unlitShader = Shader.Find("Unlit/Color");
        }

        if (unlitShader == null) return null;

        Material fallback = new Material(unlitShader);
        fallback.EnableKeyword("_EMISSION");
        return fallback;
    }

    void RemoveCollider(GameObject target)
    {
        SphereCollider collider = target.GetComponent<SphereCollider>();
        if (collider == null) return;
        DestroyRuntimeObject(collider);
    }

    void DestroyRuntimeObject(Object obj)
    {
        if (obj == null) return;

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }
}
