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

    [Header("Skybox")]
    public bool rotateSkybox = true;
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
            runtimeSkybox.SetFloat("_Rotation", current + orbitDegreesPerSecond * dt);
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
            Shader sunShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (sunShader == null)
            {
                sunShader = Shader.Find("Unlit/Color");
            }

            if (sunShader != null)
            {
                Material sunMaterial = new Material(sunShader);
                sunMaterial.color = sunColor;
                sunRenderer.material = sunMaterial;
            }
        }
    }

    void UpdateSunVisualPosition()
    {
        if (generatedSunObject == null) return;

        generatedSunObject.transform.position = (-transform.forward * sunDistance);
    }
}
