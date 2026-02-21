using UnityEngine;

[RequireComponent(typeof(Light))]
public class SunSkyRotator : MonoBehaviour
{
    [Header("Orbit")]
    public float orbitDegreesPerSecond = 0.75f;
    public Vector3 orbitAxis = Vector3.up;
    [Tooltip("Keeps the sun path on the great-circle plane perpendicular to the orbit axis (equator-like path).")]
    public bool keepOrbitOnEquator = true;

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
    [Min(0.001f)] public float planetRadius = 8f;
    [Tooltip("Adjust sunrise/sunset trigger around the planet limb. + values trigger earlier, - values later.")]
    public float horizonTriggerOffsetDegrees = 0f;
    [Tooltip("Angular width of the warm sunrise/sunset band around horizon.")]
    [Range(0.1f, 25f)] public float horizonTransitionDegrees = 6f;
    public Color horizonColor = new Color(1f, 0.45f, 0.2f, 1f);
    public Color dayColor = new Color(1f, 0.95f, 0.75f, 1f);
    [Range(0f, 1f)] public float colorShiftStrength = 1f;

    [Header("Emission Balancing")]
    [Range(0f, 2f)] public float dayEmissionMultiplier = 1f;
    [Range(0f, 2f)] public float behindPlanetEmissionMultiplier = 0.8f;
    [Range(0f, 2f)] public float horizonEmissionBoost = 0.3f;

    [Header("Skybox")]
    public bool rotateSkybox = true;
    [Tooltip("Use -1 to match the light orbit direction for this skybox shader.")]
    public float skyboxRotationMultiplier = -1f;
    public Material skyboxOverride;

    private Quaternion initialRotation;
    private float accumulatedOrbitAngle;
    private Vector3 initialOrbitForward;

    private Material originalSkybox;
    private Material runtimeSkybox;

    private GameObject generatedSunObject;
    private Material runtimeSunMaterial;

    void Start()
    {
        initialRotation = transform.rotation;
        CacheInitialOrbitForward();
        SetupViewerReference();
        ResolvePlanetRadius();
        SetupSkybox();
        CreateSunVisual();
        UpdateSunVisualPosition();
        UpdateSunVisualAppearance();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 axis = GetOrbitAxis();

        accumulatedOrbitAngle += orbitDegreesPerSecond * dt;
        Quaternion orbitRotation = Quaternion.AngleAxis(accumulatedOrbitAngle, axis);

        if (keepOrbitOnEquator)
        {
            Vector3 orbitForward = orbitRotation * initialOrbitForward;
            transform.rotation = Quaternion.LookRotation(orbitForward, axis);
        }
        else
        {
            transform.rotation = orbitRotation * initialRotation;
        }

        if (rotateSkybox && runtimeSkybox != null && runtimeSkybox.HasFloat("_Rotation"))
        {
            float current = runtimeSkybox.GetFloat("_Rotation");
            runtimeSkybox.SetFloat("_Rotation", current + orbitDegreesPerSecond * skyboxRotationMultiplier * dt);
        }

        UpdateSunVisualPosition();
        UpdateSunVisualAppearance();
    }

    void CacheInitialOrbitForward()
    {
        Vector3 axis = GetOrbitAxis();
        Vector3 projectedForward = Vector3.ProjectOnPlane(initialRotation * Vector3.forward, axis);

        if (projectedForward.sqrMagnitude < 0.0001f)
        {
            projectedForward = Vector3.Cross(axis, Vector3.right);
            if (projectedForward.sqrMagnitude < 0.0001f)
            {
                projectedForward = Vector3.Cross(axis, Vector3.forward);
            }
        }

        initialOrbitForward = projectedForward.normalized;
    }

    Vector3 GetOrbitAxis()
    {
        if (orbitAxis.sqrMagnitude < 0.0001f)
        {
            return Vector3.up;
        }

        return orbitAxis.normalized;
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

    void ResolvePlanetRadius()
    {
        if (planetCenter == null) return;

        PlanetGenerator generator = planetCenter.GetComponent<PlanetGenerator>();
        if (generator != null)
        {
            planetRadius = Mathf.Max(0.001f, generator.radius);
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

        Vector3 center = planetCenter != null ? planetCenter.position : Vector3.zero;
        generatedSunObject.transform.position = center - transform.forward * sunDistance;
        generatedSunObject.transform.localScale = Vector3.one * sunScale;
    }

    void UpdateSunVisualAppearance()
    {
        if (runtimeSunMaterial == null) return;

        EvaluateSunAppearance(out Color shiftedColor, out float emissionMultiplier);

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
            runtimeSunMaterial.SetColor("_EmissionColor", shiftedColor * sunEmissionIntensity * emissionMultiplier);
        }
    }

    void EvaluateSunAppearance(out Color shiftedColor, out float emissionMultiplier)
    {
        SetupViewerReference();

        Vector3 center = planetCenter != null ? planetCenter.position : Vector3.zero;
        if (viewer == null)
        {
            shiftedColor = Color.Lerp(sunColor, dayColor * sunColor, colorShiftStrength);
            emissionMultiplier = dayEmissionMultiplier;
            return;
        }

        Vector3 cameraPos = viewer.position;
        Vector3 toCenter = center - cameraPos;
        float distanceToCenter = toCenter.magnitude;
        Vector3 centerDir = toCenter.normalized;

        Vector3 sunPosition = center - transform.forward * sunDistance;
        Vector3 sunDir = (sunPosition - cameraPos).normalized;

        float angleToSunFromCenterDir = Vector3.Angle(centerDir, sunDir); // degrees
        float planetAngularRadius = Mathf.Asin(Mathf.Clamp01(planetRadius / Mathf.Max(distanceToCenter, planetRadius + 0.001f))) * Mathf.Rad2Deg;
        float horizonAngle = planetAngularRadius + horizonTriggerOffsetDegrees;
        float deltaFromHorizon = angleToSunFromCenterDir - horizonAngle;

        bool behindPlanet = deltaFromHorizon < 0f;
        float transition = Mathf.Max(0.1f, horizonTransitionDegrees);

        // Warm tint strongest right around horizon crossing.
        float horizonFactor = 1f - Mathf.Clamp01(Mathf.Abs(deltaFromHorizon) / transition);

        // Day factor rises as sun moves above horizon line.
        float dayAmount = Mathf.Clamp01(deltaFromHorizon / transition);

        Color visibleColor = Color.Lerp(dayColor, horizonColor, horizonFactor);
        Color finalColor = behindPlanet ? horizonColor : Color.Lerp(horizonColor, visibleColor, dayAmount);

        shiftedColor = Color.Lerp(sunColor, finalColor * sunColor, colorShiftStrength);

        emissionMultiplier = behindPlanet ? behindPlanetEmissionMultiplier : Mathf.Lerp(behindPlanetEmissionMultiplier, dayEmissionMultiplier, dayAmount);
        emissionMultiplier += horizonFactor * horizonEmissionBoost;
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
