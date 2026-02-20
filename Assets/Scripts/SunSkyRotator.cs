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

    void Start()
    {
        initialRotation = transform.rotation;
        SetupSkybox();
        CreateSunVisual();
        UpdateSunVisualPosition();
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
    }

    void OnDestroy()
    {
        if (runtimeSkybox != null && RenderSettings.skybox == runtimeSkybox)
        {
            RenderSettings.skybox = originalSkybox;
        }

        if (generatedSunObject != null)
        {
            if (Application.isPlaying)
            {
                Destroy(generatedSunObject);
            }
            else
            {
                DestroyImmediate(generatedSunObject);
            }
        }

        if (runtimeSkybox != null)
        {
            if (Application.isPlaying)
            {
                Destroy(runtimeSkybox);
            }
            else
            {
                DestroyImmediate(runtimeSkybox);
            }
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

        SphereCollider sphereCollider = generatedSunObject.GetComponent<SphereCollider>();
        if (sphereCollider != null)
        {
            if (Application.isPlaying)
            {
                Destroy(sphereCollider);
            }
            else
            {
                DestroyImmediate(sphereCollider);
            }
        }

        Renderer sunRenderer = generatedSunObject.GetComponent<Renderer>();
        if (sunRenderer != null)
        {
            Material sunMaterial = BuildSunMaterial();
            if (sunMaterial != null)
            {
                sunRenderer.material = sunMaterial;
                sunRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sunRenderer.receiveShadows = false;
            }
        }
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

        if (unlitShader == null)
        {
            return null;
        }

        Material unlitMaterial = new Material(unlitShader);
        Color hdrColor = sunColor * sunEmissionIntensity;
        if (unlitMaterial.HasProperty("_BaseColor"))
        {
            unlitMaterial.SetColor("_BaseColor", hdrColor);
        }
        else if (unlitMaterial.HasProperty("_Color"))
        {
            unlitMaterial.SetColor("_Color", hdrColor);
        }

        return unlitMaterial;
    }

    void UpdateSunVisualPosition()
    {
        if (generatedSunObject == null) return;

        generatedSunObject.transform.position = (-transform.forward * sunDistance);
    }
}
