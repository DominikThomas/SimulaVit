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

    [Header("Sunrise / Sunset Color Shift")]
    public Color horizonColor = new Color(1f, 0.45f, 0.2f, 1f);
    public Color zenithColor = new Color(1f, 0.95f, 0.75f, 1f);
    public Color nightColor = new Color(0.2f, 0.25f, 0.35f, 1f);

    [Header("Corona / Halo")]
    [Min(1f)] public float haloScaleMultiplier = 1.9f;
    [Range(0f, 1f)] public float haloAlpha = 0.2f;
    [Min(0f)] public float haloEmissionIntensity = 6f;

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
    private GameObject generatedHaloObject;
    private Material runtimeSunMaterial;
    private Material runtimeHaloMaterial;

    void Start()
    {
        initialRotation = transform.rotation;
        SetupSkybox();
        CreateSunVisuals();
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
        DestroyRuntimeObject(generatedHaloObject);
        DestroyRuntimeObject(runtimeSunMaterial);
        DestroyRuntimeObject(runtimeHaloMaterial);
        DestroyRuntimeObject(runtimeSkybox);
    }

    void SetupSkybox()
    {
        originalSkybox = RenderSettings.skybox;
        Material source = skyboxOverride != null ? skyboxOverride : RenderSettings.skybox;
        if (source == null) return;

        runtimeSkybox = new Material(source);
        RenderSettings.skybox = runtimeSkybox;
    }

    void CreateSunVisuals()
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

        generatedHaloObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        generatedHaloObject.name = "Sun Halo";
        generatedHaloObject.transform.localScale = Vector3.one * sunScale * haloScaleMultiplier;
        RemoveCollider(generatedHaloObject);

        Renderer haloRenderer = generatedHaloObject.GetComponent<Renderer>();
        if (haloRenderer != null)
        {
            runtimeHaloMaterial = BuildHaloMaterial();
            if (runtimeHaloMaterial != null)
            {
                haloRenderer.sharedMaterial = runtimeHaloMaterial;
                haloRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                haloRenderer.receiveShadows = false;
            }
        }
    }

    void UpdateSunVisualPosition()
    {
        Vector3 sunPosition = -transform.forward * sunDistance;

        if (generatedSunObject != null)
        {
            generatedSunObject.transform.position = sunPosition;
            generatedSunObject.transform.localScale = Vector3.one * sunScale;
        }

        if (generatedHaloObject != null)
        {
            generatedHaloObject.transform.position = sunPosition;
            generatedHaloObject.transform.localScale = Vector3.one * sunScale * haloScaleMultiplier;
        }
    }

    void UpdateSunVisualAppearance()
    {
        Color shiftedColor = EvaluateSunShiftedColor();

        if (runtimeSunMaterial != null)
        {
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

        if (runtimeHaloMaterial != null)
        {
            Color haloColor = shiftedColor;
            haloColor.a = haloAlpha;

            if (runtimeHaloMaterial.HasProperty("_BaseColor"))
            {
                runtimeHaloMaterial.SetColor("_BaseColor", haloColor);
            }
            if (runtimeHaloMaterial.HasProperty("_Color"))
            {
                runtimeHaloMaterial.SetColor("_Color", haloColor);
            }
            if (runtimeHaloMaterial.HasProperty("_EmissionColor"))
            {
                runtimeHaloMaterial.SetColor("_EmissionColor", shiftedColor * haloEmissionIntensity);
            }
        }
    }

    Color EvaluateSunShiftedColor()
    {
        // Direction from planet center to sun visual.
        Vector3 sunDirection = -transform.forward;
        float altitude = Vector3.Dot(sunDirection.normalized, Vector3.up);

        float aboveHorizon = Mathf.Clamp01((altitude + 0.1f) / 1.1f);
        float horizonBoost = 1f - Mathf.Abs(aboveHorizon * 2f - 1f);

        Color daylightColor = Color.Lerp(horizonColor, zenithColor, aboveHorizon);
        Color colorWithNight = Color.Lerp(nightColor, daylightColor, aboveHorizon);
        Color shiftedColor = Color.Lerp(colorWithNight, horizonColor, horizonBoost * 0.35f);

        return shiftedColor * sunColor;
    }

    Material BuildSunMaterial()
    {
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader != null)
        {
            Material litMaterial = new Material(litShader);
            litMaterial.SetColor("_BaseColor", sunColor);
            litMaterial.SetFloat("_Metallic", sunMetallic);
            litMaterial.SetFloat("_Smoothness", sunSurfaceSmoothness);
            litMaterial.EnableKeyword("_EMISSION");
            litMaterial.SetColor("_EmissionColor", sunColor * sunEmissionIntensity);
            return litMaterial;
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

    Material BuildHaloMaterial()
    {
        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null)
        {
            unlitShader = Shader.Find("Unlit/Color");
        }
        if (unlitShader == null) return null;

        Material material = new Material(unlitShader);

        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", 1f);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", 1f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.EnableKeyword("_EMISSION");
        return material;
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
