using System;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Light))]
public class SunSkyRotator : MonoBehaviour
{
    private const float DirectionEpsilon = 0.000001f;

    [Header("Orbit")]
    public float orbitDegreesPerSecond = 0.75f;
    public Vector3 orbitAxis = Vector3.up;
    public bool keepOrbitOnEquator = true;

    [Header("Seasons")]
    public bool enableSeasons = false;
    [Range(0f, 90f)] public float axisTiltDegrees = 23.5f;
    [Min(1f)] public float yearLengthInDays = 100f;
    [Tooltip("Offset added to seasonal phase in radians.")]
    public float seasonalPhaseOffset = 0f;
    [Tooltip("When enabled, seasonal phase 0 starts at +axisTilt (northern summer).")]
    public bool northernSummerAtPhaseZero = false;

    [Header("Sun Visual")]
    public float sunDistance = 250f;
    public float sunScale = 8f;
    public Color sunColor = new Color(1f, 0.9f, 0.6f, 1f);
    [Min(0f)] public float sunEmissionIntensity = 4f;

    [Header("Sun Disc Shape")]
    [Range(0.01f, 1f)] public float coreRadius = 0.16f;
    [Range(0.01f, 2f)] public float glowRadius = 0.9f;
    [Range(0.5f, 16f)] public float glowFalloff = 3.5f;
    [Range(64, 512)] public int generatedTextureSize = 256;

    [Header("Material Template")]
    [Tooltip("Assign a Material asset using URP/Unlit, Surface Type Transparent.")]
    public Material sunMaterialTemplate;

    [Header("Camera-Relative Color Shift")]
    public Transform planetCenter;
    public Transform viewer;
    [Min(0.001f)] public float planetRadius = 8f;
    [Tooltip("Sunset colour transition band above the apparent planet limb, in degrees. This is horizon-relative apparent angular geometry.")]
    [Range(0.1f, 25f)] public float horizonTransitionDegrees = 6f;
    public Color horizonColor = new Color(1f, 0.45f, 0.2f, 1f);
    public Color dayColor = new Color(1f, 0.95f, 0.75f, 1f);
    [Range(0f, 1f)] public float colorShiftStrength = 1f;
    [Tooltip("Minimum central-disc brightness at the apparent limb, before the separate geometric occultation factor is applied.")]
    [Range(0f, 2f)] public float minimumSunsetCentreBrightness = 0.8f;
    [Tooltip("How many degrees below the apparent limb the visual red glow may persist after the central disc is occulted.")]
    [Range(0f, 10f)] public float afterHorizonGlowPersistenceDegrees = 1f;
    [Tooltip("Multiplier for the derived apparent sun-disc radius used to soften partial occultation. One spans exactly one core-disc radius either side of the limb.")]
    [Range(0.25f, 3f)] public float sunOcclusionSoftness = 1f;

    [Header("Emission Balancing")]
    [Range(0f, 2f)] public float dayEmissionMultiplier = 1f;
    [Range(0f, 2f)] public float behindPlanetEmissionMultiplier = 0.8f;
    [Range(0f, 2f)] public float horizonEmissionBoost = 0.3f;

    [Header("Skybox")]
    public bool rotateSkybox = true;
    public float skyboxRotationMultiplier = -1f;
    public Material skyboxOverride;

    [Header("Simulation Speed Coupling")]
    public bool scaleRotationWithSimulationSpeed = true;
    public ReplicatorManager replicatorManager;

    [Header("Sun Lighting Diagnostics")]
    [Tooltip("Emit one compact lighting report after initialization and warnings when synchronization is invalid.")]
    public bool logSunLightingDiagnostics;
    [Min(0.01f)] public float angularMismatchWarningDegrees = 1f;
    [SerializeField, Tooltip("Read-only: physical directional light synchronized with the visible sun.")] private Light currentDirectionalLight;
    [SerializeField, Tooltip("Read-only: planet-to-sun direction in world space.")] private Vector3 planetToSunDirectionWorld;
    [SerializeField, Tooltip("Read-only: direction in which sunlight rays travel in world space.")] private Vector3 sunToPlanetDirectionWorld;
    [SerializeField, Tooltip("Read-only: angular difference between expected ray direction and the light transform forward.")] private float lightDirectionAngularError;
    [SerializeField, Tooltip("Read-only: whether the terrain shader consumes the URP main light.")] private bool terrainMainLightSupport;
    [SerializeField, Tooltip("Read-only: whether the ocean shader consumes the URP main light.")] private bool oceanMainLightSupport;
    [SerializeField, Tooltip("Read-only apparent-horizon diagnostic, in world units.")] private float cameraToPlanetDistance;
    [SerializeField, Tooltip("Read-only current opaque/liquid silhouette radius, in world units; atmosphere excluded.")] private float resolvedVisiblePlanetRadius;
    [SerializeField, Tooltip("Read-only apparent planet angular radius, in degrees.")] private float apparentPlanetAngularRadiusDegrees;
    [SerializeField, Tooltip("Read-only apparent bright sun-disc angular radius, in degrees.")] private float apparentSunAngularRadiusDegrees;
    [SerializeField, Tooltip("Read-only signed sun-centre height above the apparent planet limb, in degrees.")] private float sunCentreHeightAboveLimbDegrees;
    [SerializeField, Tooltip("Read-only sunset colour factor.")] private float sunsetColourFactor;
    [SerializeField, Tooltip("Read-only geometric central-disc visibility factor.")] private float visibleDiscFactor;
    [SerializeField, Tooltip("Read-only after-horizon glow factor.")] private float glowFactor;

    public Vector3 PlanetToSunDirectionWorld => planetToSunDirectionWorld;
    public Vector3 SunToPlanetDirectionWorld => sunToPlanetDirectionWorld;
    public Light CurrentDirectionalLight => currentDirectionalLight;
    public bool IsSunDirectionValid { get; private set; }
    public float LightDirectionAngularError => lightDirectionAngularError;
    public bool TerrainMainLightSupport => terrainMainLightSupport;
    public bool OceanMainLightSupport => oceanMainLightSupport;
    public Transform VisibleSunTransform => generatedSunObject != null ? generatedSunObject.transform : null;

    private Quaternion initialRotation;
    private float accumulatedOrbitAngle;
    private double orbitAngleOffsetDegrees;
    private Vector3 initialOrbitForward;

    private Material originalSkybox;
    private Material runtimeSkybox;
    private float initialSkyboxRotation;
    private float currentSkyboxRotation;

    private GameObject generatedSunObject;
    private Material runtimeSunMaterial;
    private Texture2D runtimeSunTexture;
    private MeshFilter sunMeshFilter;
    private MeshRenderer sunMeshRenderer;
    private bool angularMismatchWarningActive;
    private PlanetGenerator planetGenerator;
    private PlanetGridType lastAppearanceGridMode;
    private bool hasAppearanceGridMode;
    private bool appearanceGeometryWarningActive;

    void OnValidate()
    {
        Light attachedLight = GetComponent<Light>();
        currentDirectionalLight = attachedLight != null && attachedLight.type == LightType.Directional ? attachedLight : null;
        angularMismatchWarningDegrees = Mathf.Max(0.01f, angularMismatchWarningDegrees);
        RefreshShaderDiagnostics();
    }

    void Start()
    {
        ResolvePhysicalLight();
        initialRotation = transform.rotation;
        CacheInitialOrbitForward();
        SetupViewerReference();
        ResolveReplicatorManagerReference();
        ResolvePlanetRadius();
        SetupSkybox();
        CreateSunVisual();
        UpdateSunVisualPosition();
        UpdateSunVisualAppearance();
        SynchronizePhysicalLight(true);
    }


    public void ApplyStartupTiming(float axisTilt, float dayLengthSeconds, float yearLengthDays)
    {
        axisTiltDegrees = Mathf.Clamp(axisTilt, 0f, 90f);
        yearLengthInDays = Mathf.Max(1f, yearLengthDays);

        if (dayLengthSeconds > 0f)
        {
            orbitDegreesPerSecond = 360f / dayLengthSeconds;
        }

        ResetOrbitPhase();
    }

    public void ResetOrbitPhase()
    {
        accumulatedOrbitAngle = 0f;
        double simulationTime = replicatorManager != null ? replicatorManager.SimulationTimeSeconds : 0d;
        orbitAngleOffsetDegrees = -simulationTime * orbitDegreesPerSecond;
        transform.rotation = initialRotation;
        currentSkyboxRotation = initialSkyboxRotation;
        ApplySkyboxRotation(currentSkyboxRotation);
        CacheInitialOrbitForward();
        UpdateSunVisualPosition();
        UpdateSunVisualAppearance();
    }


    public bool ApplySnapshot(SunSkySnapshot snapshot, SimulationClockSnapshot clockSnapshot = null)
    {
        if (snapshot != null && snapshot.available)
        {
            transform.rotation = snapshot.rotation.ToQuaternion();
            orbitDegreesPerSecond = snapshot.orbitDegreesPerSecond;
            orbitAxis = snapshot.orbitAxis.ToVector3();
            keepOrbitOnEquator = snapshot.keepOrbitOnEquator;
            enableSeasons = snapshot.enableSeasons;
            axisTiltDegrees = snapshot.axisTiltDegrees;
            yearLengthInDays = Mathf.Max(1f, snapshot.yearLengthInDays);
            seasonalPhaseOffset = snapshot.seasonalPhaseOffset;
            northernSummerAtPhaseZero = snapshot.northernSummerAtPhaseZero;
            accumulatedOrbitAngle = snapshot.accumulatedOrbitAngle;
            orbitAngleOffsetDegrees = clockSnapshot != null
                ? accumulatedOrbitAngle - clockSnapshot.simulationTimeSeconds * orbitDegreesPerSecond
                : accumulatedOrbitAngle;
            sunColor = snapshot.sunColor.ToColor();
            sunEmissionIntensity = snapshot.sunEmissionIntensity;
            currentSkyboxRotation = snapshot.skyboxSnapshotAvailable
                ? snapshot.skyboxRotation
                : CalculateSkyboxRotationForOrbitAngle(accumulatedOrbitAngle);
            ApplySkyboxRotation(currentSkyboxRotation);
            UpdateSunVisualPosition();
            UpdateSunVisualAppearance();
            DynamicGI.UpdateEnvironment();
            return true;
        }

        if (clockSnapshot != null && orbitDegreesPerSecond > 0f)
        {
            accumulatedOrbitAngle = (float)(clockSnapshot.simulationTimeSeconds * orbitDegreesPerSecond);
            orbitAngleOffsetDegrees = 0d;
            transform.rotation = Quaternion.AngleAxis(accumulatedOrbitAngle, GetOrbitAxis()) * initialRotation;
            currentSkyboxRotation = CalculateSkyboxRotationForOrbitAngle(accumulatedOrbitAngle);
            ApplySkyboxRotation(currentSkyboxRotation);
            UpdateSunVisualPosition();
            UpdateSunVisualAppearance();
            DynamicGI.UpdateEnvironment();
            Debug.LogWarning("Sun/sky save snapshot unavailable; reconstructed orbit phase from simulation time.", this);
            return false;
        }

        return false;
    }

    public SunSkySnapshot CaptureSnapshot()
    {
        return new SunSkySnapshot
        {
            available = true,
            rotation = new SerializableQuaternion(transform.rotation),
            orbitDegreesPerSecond = orbitDegreesPerSecond,
            orbitAxis = new SerializableVector3(orbitAxis),
            keepOrbitOnEquator = keepOrbitOnEquator,
            enableSeasons = enableSeasons,
            axisTiltDegrees = axisTiltDegrees,
            yearLengthInDays = yearLengthInDays,
            seasonalPhaseOffset = seasonalPhaseOffset,
            northernSummerAtPhaseZero = northernSummerAtPhaseZero,
            accumulatedOrbitAngle = accumulatedOrbitAngle,
            skyboxSnapshotAvailable = runtimeSkybox != null && runtimeSkybox.HasFloat("_Rotation"),
            skyboxRotation = GetCurrentSkyboxRotation(),
            sunColor = new SerializableColor(sunColor),
            sunEmissionIntensity = sunEmissionIntensity
        };
    }

    void Update()
    {
        float dt = GetSimulationDeltaTime();
        Vector3 axis = GetOrbitAxis();

        // Daily angle drives day/night progression and remains tied to simulation delta time.
        if (scaleRotationWithSimulationSpeed && replicatorManager != null)
        {
            accumulatedOrbitAngle = (float)GetOrbitAngleDegreesAtSimulationTime(replicatorManager.SimulationTimeSeconds);
        }
        else
        {
            accumulatedOrbitAngle += orbitDegreesPerSecond * dt;
        }
        Quaternion orbitRotation = Quaternion.AngleAxis(accumulatedOrbitAngle, axis);

        if (keepOrbitOnEquator)
        {
            Vector3 orbitForward = orbitRotation * initialOrbitForward;

            // Seasonal phase advances over a configurable simulation year (dayLength * yearLengthInDays).
            float declinationDegrees = GetSeasonalDeclinationDegrees();

            // Declination is the apparent north/south latitude of the sun path (axis tilt model).
            Vector3 east = Vector3.Cross(axis, orbitForward);
            if (east.sqrMagnitude < 0.0001f)
            {
                east = Vector3.Cross(axis, Vector3.right);
                if (east.sqrMagnitude < 0.0001f)
                {
                    east = Vector3.Cross(axis, Vector3.forward);
                }
            }

            Quaternion declinationRotation = Quaternion.AngleAxis(-declinationDegrees, east.normalized);
            Vector3 tiltedForward = declinationRotation * orbitForward;
            transform.rotation = Quaternion.LookRotation(tiltedForward, axis);
        }
        else
        {
            transform.rotation = orbitRotation * initialRotation;
        }

        if (rotateSkybox && runtimeSkybox != null && runtimeSkybox.HasFloat("_Rotation"))
        {
            currentSkyboxRotation += orbitDegreesPerSecond * skyboxRotationMultiplier * dt;
            ApplySkyboxRotation(currentSkyboxRotation);
        }

        UpdateSunVisualPosition();
        UpdateSunVisualAppearance();
    }

    void LateUpdate()
    {
        BillboardSunVisual();
        SynchronizePhysicalLight(false);
    }

    void ResolvePhysicalLight()
    {
        currentDirectionalLight = GetComponent<Light>();
        if (currentDirectionalLight != null && currentDirectionalLight.type != LightType.Directional)
        {
            Debug.LogWarning("[SunLighting] SunSkyRotator requires its Light to be Directional; the visible sun will not be synchronized to a point or spot light.", this);
            currentDirectionalLight = null;
        }

        if (currentDirectionalLight != null && (RenderSettings.sun == null || RenderSettings.sun == currentDirectionalLight))
        {
            RenderSettings.sun = currentDirectionalLight;
        }

        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        int enabledDirectionalCount = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].isActiveAndEnabled && lights[i].type == LightType.Directional) enabledDirectionalCount++;
        }
        if (enabledDirectionalCount > 1)
        {
            Debug.LogWarning($"[SunLighting] Found {enabledDirectionalCount} enabled Directional Lights. Disable competing sun lights or explicitly retain only the SunSkyRotator light.", this);
        }
        if (currentDirectionalLight == null)
        {
            Debug.LogWarning("[SunLighting] A visible sun exists but no physical Directional Light is synchronized.", this);
        }
    }

    void SynchronizePhysicalLight(bool initializationReport)
    {
        Vector3 center = planetCenter != null ? planetCenter.position : Vector3.zero;
        if (generatedSunObject == null)
        {
            IsSunDirectionValid = false;
            return;
        }

        Vector3 planetToSun = generatedSunObject.transform.position - center;
        IsSunDirectionValid = planetToSun.sqrMagnitude > DirectionEpsilon;
        if (!IsSunDirectionValid) return;

        planetToSunDirectionWorld = planetToSun.normalized;
        sunToPlanetDirectionWorld = -planetToSunDirectionWorld;
        if (currentDirectionalLight != null)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(sunToPlanetDirectionWorld, Vector3.up)) < 0.98f ? Vector3.up : Vector3.forward;
            currentDirectionalLight.transform.rotation = Quaternion.LookRotation(sunToPlanetDirectionWorld, up);
            lightDirectionAngularError = Vector3.Angle(sunToPlanetDirectionWorld, currentDirectionalLight.transform.forward);
        }
        else
        {
            lightDirectionAngularError = 180f;
        }

        if (initializationReport)
        {
            RefreshShaderDiagnostics();
            if (logSunLightingDiagnostics) LogLightingDiagnostics();
        }
        bool mismatchNow = currentDirectionalLight != null && lightDirectionAngularError > angularMismatchWarningDegrees;
        if (mismatchNow && !angularMismatchWarningActive)
        {
            Debug.LogWarning($"[SunLighting] Directional Light mismatch is {lightDirectionAngularError:F3} degrees (threshold {angularMismatchWarningDegrees:F3}).", this);
        }
        angularMismatchWarningActive = mismatchNow;
    }

    void RefreshShaderDiagnostics()
    {
        terrainMainLightSupport = Shader.Find("SimulaVit/GeodesicVertexColorURP") != null;
        oceanMainLightSupport = Shader.Find("SimulaVit/GeodesicOceanURP") != null;
    }

    [ContextMenu("Log Sun Lighting Diagnostics")]
    public void LogLightingDiagnostics()
    {
        RefreshShaderDiagnostics();
        PlanetGenerator generator = planetCenter != null ? planetCenter.GetComponent<PlanetGenerator>() : null;
        Renderer terrainRenderer = generator != null ? generator.GetComponent<Renderer>() : null;
        Transform ocean = generator != null ? generator.transform.Find("Geodesic Ocean") : null;
        Renderer oceanRenderer = ocean != null ? ocean.GetComponent<Renderer>() : null;
        string terrainMaterial = terrainRenderer != null && terrainRenderer.sharedMaterial != null ? $"{terrainRenderer.sharedMaterial.name}/{terrainRenderer.sharedMaterial.shader.name}" : "<none>";
        string oceanMaterial = oceanRenderer != null && oceanRenderer.sharedMaterial != null ? $"{oceanRenderer.sharedMaterial.name}/{oceanRenderer.sharedMaterial.shader.name}" : "<none>";
        Debug.Log($"[SunLighting] planetToSunWS={planetToSunDirectionWorld}, sunRayWS={sunToPlanetDirectionWorld}, lightForward={(currentDirectionalLight != null ? currentDirectionalLight.transform.forward.ToString() : "<none>")}, angularError={lightDirectionAngularError:F3}, directionalLight={(currentDirectionalLight != null ? currentDirectionalLight.name : "<none>")}, renderSettingsSun={(RenderSettings.sun != null ? RenderSettings.sun.name : "<none>")}, terrain={terrainMaterial}, terrainMainLight={terrainMainLightSupport}, ocean={oceanMaterial}, oceanMainLight={oceanMainLightSupport}", this);
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
        DestroyRuntimeObject(runtimeSunTexture);
        DestroyRuntimeObject(runtimeSkybox);
    }

    void SetupViewerReference()
    {
        if (viewer == null && Camera.main != null)
        {
            viewer = Camera.main.transform;
        }
    }

    void ResolveReplicatorManagerReference()
    {
        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }
    }

    float GetSimulationDeltaTime()
    {
        if (!scaleRotationWithSimulationSpeed)
        {
            return Time.unscaledDeltaTime;
        }

        ResolveReplicatorManagerReference();
        if (replicatorManager == null)
        {
            return Time.unscaledDeltaTime;
        }

        return Mathf.Max(0f, replicatorManager.FrameSimulationDeltaTime);
    }

    public float GetDayLengthSeconds()
    {
        return orbitDegreesPerSecond > 0.0001f ? 360f / orbitDegreesPerSecond : float.PositiveInfinity;
    }

    public Vector3 GetSunDirectionForDayPhase01(float dayPhase01)
    {
        if (initialRotation == default)
        {
            initialRotation = transform.rotation;
        }

        if (initialOrbitForward.sqrMagnitude < 0.0001f)
        {
            CacheInitialOrbitForward();
        }

        Vector3 axis = GetOrbitAxis();
        float normalizedPhase = Mathf.Repeat(dayPhase01, 1f);
        float orbitAngle = normalizedPhase * 360f;
        Quaternion orbitRotation = Quaternion.AngleAxis(orbitAngle, axis);

        if (keepOrbitOnEquator)
        {
            Vector3 orbitForward = orbitRotation * initialOrbitForward;
            float declinationDegrees = GetSeasonalDeclinationDegrees();
            Vector3 east = Vector3.Cross(axis, orbitForward);
            if (east.sqrMagnitude < 0.0001f)
            {
                east = Vector3.Cross(axis, Vector3.right);
                if (east.sqrMagnitude < 0.0001f)
                {
                    east = Vector3.Cross(axis, Vector3.forward);
                }
            }

            Quaternion declinationRotation = Quaternion.AngleAxis(-declinationDegrees, east.normalized);
            Vector3 tiltedForward = declinationRotation * orbitForward;
            // The public API is planet -> sun. The controller/light forward is the
            // opposite direction: the direction in which directional-light rays travel.
            return -tiltedForward.normalized;
        }

        Quaternion rotation = orbitRotation * initialRotation;
        return -(rotation * Vector3.forward).normalized;
    }

    /// <summary>Pure ephemeris query; does not mutate the visible sun transform or orbit state.</summary>
    public Vector3 GetPlanetToSunDirectionWorldAtSimulationTime(double simulationTimeSeconds)
    {
        EnsureEphemerisBasis();
        Vector3 axis = GetOrbitAxis();
        float orbitAngle = (float)GetOrbitAngleDegreesAtSimulationTime(simulationTimeSeconds);
        Quaternion orbitRotation = Quaternion.AngleAxis(orbitAngle, axis);
        if (!keepOrbitOnEquator) return -(orbitRotation * initialRotation * Vector3.forward).normalized;
        Vector3 orbitForward = orbitRotation * initialOrbitForward;
        Vector3 east = Vector3.Cross(axis, orbitForward);
        if (east.sqrMagnitude < 0.0001f) { east = Vector3.Cross(axis, Vector3.right); if (east.sqrMagnitude < 0.0001f) east = Vector3.Cross(axis, Vector3.forward); }
        Quaternion declinationRotation = Quaternion.AngleAxis(-GetSeasonalDeclinationDegrees(orbitAngle), east.normalized);
        return -(declinationRotation * orbitForward).normalized;
    }

    public double GetOrbitAngleDegreesAtSimulationTime(double simulationTimeSeconds) => orbitAngleOffsetDegrees + Math.Max(0d, simulationTimeSeconds) * orbitDegreesPerSecond;
    public float GetDayPhase01AtSimulationTime(double simulationTimeSeconds) => Mathf.Repeat((float)(GetOrbitAngleDegreesAtSimulationTime(simulationTimeSeconds) / 360d), 1f);
    public double CurrentOrbitTimeSeconds => replicatorManager != null ? replicatorManager.SimulationTimeSeconds : (orbitDegreesPerSecond > 0f ? accumulatedOrbitAngle / orbitDegreesPerSecond : 0d);

    private void EnsureEphemerisBasis()
    {
        if (initialRotation == default) initialRotation = transform.rotation;
        if (initialOrbitForward.sqrMagnitude < 0.0001f) CacheInitialOrbitForward();
    }

    float GetYearLengthSeconds()
    {
        // Year duration is explicitly derived from the current day length.
        float dayLengthSeconds = GetDayLengthSeconds();
        return dayLengthSeconds * Mathf.Max(1f, yearLengthInDays);
    }

    float GetSeasonalDeclinationDegrees()
    {
        return GetSeasonalDeclinationDegrees(accumulatedOrbitAngle);
    }

    float GetSeasonalDeclinationDegrees(float orbitAngleDegrees)
    {
        if (!enableSeasons)
        {
            return 0f;
        }

        float yearLengthSeconds = GetYearLengthSeconds();
        if (!float.IsFinite(yearLengthSeconds) || yearLengthSeconds <= 0f)
        {
            return 0f;
        }

        // Seasonal phase is based on simulation time only (deterministic, pause-aware).
        float yearPhase01 = Mathf.Repeat(orbitAngleDegrees / 360f, yearLengthInDays) / Mathf.Max(1f, yearLengthInDays);
        float phaseRadians = (yearPhase01 * Mathf.PI * 2f) + seasonalPhaseOffset;
        float sine = Mathf.Sin(phaseRadians);

        if (northernSummerAtPhaseZero)
        {
            // Shift sin to cos so phase 0 starts at +tilt for northern summer.
            sine = Mathf.Cos(phaseRadians);
        }

        // Declination oscillates smoothly between +axisTilt and -axisTilt.
        return Mathf.Clamp(axisTiltDegrees, 0f, 90f) * sine;
    }

    void ResolvePlanetRadius()
    {
        if (planetCenter == null) return;

        planetGenerator = planetCenter.GetComponent<PlanetGenerator>();
        if (planetGenerator != null)
        {
            planetRadius = Mathf.Max(0.001f, planetGenerator.radius);
        }
    }

    void SetupSkybox()
    {
        originalSkybox = RenderSettings.skybox;
        Material source = skyboxOverride != null ? skyboxOverride : RenderSettings.skybox;
        if (source == null) return;

        runtimeSkybox = new Material(source);
        if (runtimeSkybox.HasFloat("_Rotation"))
        {
            initialSkyboxRotation = runtimeSkybox.GetFloat("_Rotation");
            currentSkyboxRotation = initialSkyboxRotation;
        }
        RenderSettings.skybox = runtimeSkybox;
    }

    float GetCurrentSkyboxRotation()
    {
        if (runtimeSkybox != null && runtimeSkybox.HasFloat("_Rotation"))
        {
            currentSkyboxRotation = runtimeSkybox.GetFloat("_Rotation");
        }

        return currentSkyboxRotation;
    }

    float CalculateSkyboxRotationForOrbitAngle(float orbitAngle)
    {
        return initialSkyboxRotation + orbitAngle * skyboxRotationMultiplier;
    }

    void ApplySkyboxRotation(float rotation)
    {
        currentSkyboxRotation = rotation;

        if (runtimeSkybox != null && runtimeSkybox.HasFloat("_Rotation"))
        {
            runtimeSkybox.SetFloat("_Rotation", currentSkyboxRotation);
        }
    }

    void CreateSunVisual()
    {
        generatedSunObject = new GameObject("Sun Visual");
        sunMeshFilter = generatedSunObject.AddComponent<MeshFilter>();
        sunMeshRenderer = generatedSunObject.AddComponent<MeshRenderer>();

        sunMeshFilter.sharedMesh = BuildQuadMesh();
        runtimeSunTexture = BuildSunTexture();
        runtimeSunMaterial = BuildSunMaterial();

        if (runtimeSunMaterial != null)
        {
            sunMeshRenderer.sharedMaterial = runtimeSunMaterial;
        }

        sunMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        sunMeshRenderer.receiveShadows = false;
        sunMeshRenderer.lightProbeUsage = LightProbeUsage.Off;
        sunMeshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        sunMeshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        generatedSunObject.transform.localScale = Vector3.one * sunScale;
        BillboardSunVisual();
    }

    Mesh BuildQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "SunQuad";

        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f)
        };

        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };

        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    Texture2D BuildSunTexture()
    {
        int size = Mathf.Clamp(generatedTextureSize, 64, 512);
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        texture.name = "RuntimeSunDisc";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        float inner = Mathf.Clamp01(coreRadius);
        float outer = Mathf.Max(inner + 0.001f, glowRadius);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);

                float alpha;
                if (r <= inner)
                {
                    alpha = 1f;
                }
                else if (r >= outer)
                {
                    alpha = 0f;
                }
                else
                {
                    float t = 1f - Mathf.InverseLerp(inner, outer, r);
                    alpha = Mathf.Pow(t, glowFalloff);
                }

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply(false, false);
        return texture;
    }

    Material BuildSunMaterial()
    {
        if (sunMaterialTemplate == null)
        {
            Debug.LogError("SunSkyRotator: sunMaterialTemplate is not assigned.");
            return null;
        }

        Material material = new Material(sunMaterialTemplate);
        material.name = "Runtime Sun Material";

        if (material.HasProperty("_BaseMap") && runtimeSunTexture != null)
        {
            material.SetTexture("_BaseMap", runtimeSunTexture);
        }

        if (material.HasProperty("_MainTex") && runtimeSunTexture != null)
        {
            material.SetTexture("_MainTex", runtimeSunTexture);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }

    void UpdateSunVisualPosition()
    {
        if (generatedSunObject == null) return;

        Vector3 center = planetCenter != null ? planetCenter.position : Vector3.zero;
        generatedSunObject.transform.position = center - transform.forward * sunDistance;
        generatedSunObject.transform.localScale = Vector3.one * sunScale;
        BillboardSunVisual();
    }

    void BillboardSunVisual()
    {
        if (generatedSunObject == null) return;

        SetupViewerReference();
        if (viewer == null) return;

        Vector3 toCamera = viewer.position - generatedSunObject.transform.position;
        if (toCamera.sqrMagnitude < 0.000001f) return;

        generatedSunObject.transform.rotation = Quaternion.LookRotation(toCamera.normalized, viewer.up);
    }

    void UpdateSunVisualAppearance()
    {
        if (runtimeSunMaterial == null) return;

        EvaluateSunAppearance(out Color shiftedColor, out float emissionMultiplier, out float visualAlpha);

        Color finalColor = shiftedColor * Mathf.Max(0f, sunEmissionIntensity * emissionMultiplier);
        finalColor.a = visualAlpha;

        if (runtimeSunMaterial.HasProperty("_BaseColor"))
        {
            runtimeSunMaterial.SetColor("_BaseColor", finalColor);
        }

        if (runtimeSunMaterial.HasProperty("_Color"))
        {
            runtimeSunMaterial.SetColor("_Color", finalColor);
        }
    }

    void EvaluateSunAppearance(out Color shiftedColor, out float emissionMultiplier, out float visualAlpha)
    {
        SetupViewerReference();

        Vector3 center = planetCenter != null ? planetCenter.position : Vector3.zero;
        if (viewer == null)
        {
            shiftedColor = Color.Lerp(sunColor, dayColor * sunColor, colorShiftStrength);
            emissionMultiplier = dayEmissionMultiplier;
            visualAlpha = 1f;
            return;
        }

        Vector3 cameraPos = viewer.position;
        Vector3 toCenter = center - cameraPos;
        float distanceToCenter = toCenter.magnitude;
        Vector3 centerDir = toCenter.normalized;

        Vector3 sunPosition = center - transform.forward * sunDistance;
        Vector3 sunDir = (sunPosition - cameraPos).normalized;

        cameraToPlanetDistance = distanceToCenter;
        resolvedVisiblePlanetRadius = ResolveVisiblePlanetRadiusWorld();
        bool radiusValid = float.IsFinite(resolvedVisiblePlanetRadius) && resolvedVisiblePlanetRadius > 0f;
        bool cameraOutside = radiusValid && distanceToCenter > resolvedVisiblePlanetRadius;
        float sunCameraDistance = (sunPosition - cameraPos).magnitude;
        float sunCoreWorldRadius = Mathf.Max(0f, sunScale * 0.5f * Mathf.Clamp01(coreRadius));
        bool sunRadiusValid = sunCameraDistance > 0.0001f && sunCoreWorldRadius > 0f;

        apparentPlanetAngularRadiusDegrees = radiusValid
            ? Mathf.Asin(Mathf.Clamp01(resolvedVisiblePlanetRadius / Mathf.Max(distanceToCenter, 0.0001f))) * Mathf.Rad2Deg
            : 0f;
        apparentSunAngularRadiusDegrees = sunRadiusValid
            ? Mathf.Asin(Mathf.Clamp01(sunCoreWorldRadius / sunCameraDistance)) * Mathf.Rad2Deg
            : 0f;
        float separationDegrees = Vector3.Angle(centerDir, sunDir);
        sunCentreHeightAboveLimbDegrees = separationDegrees - apparentPlanetAngularRadiusDegrees;

        float transition = Mathf.Max(0.1f, horizonTransitionDegrees);
        sunsetColourFactor = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sunCentreHeightAboveLimbDegrees / transition));
        float occultationHalfBand = Mathf.Max(0.0001f, apparentSunAngularRadiusDegrees * sunOcclusionSoftness);
        visibleDiscFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-occultationHalfBand, occultationHalfBand, sunCentreHeightAboveLimbDegrees));
        float glowStart = -apparentSunAngularRadiusDegrees - Mathf.Max(0f, afterHorizonGlowPersistenceDegrees);
        glowFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(glowStart, apparentSunAngularRadiusDegrees, sunCentreHeightAboveLimbDegrees));

        Color horizonShiftedColor = horizonColor * sunColor;
        Color daylightShiftedColor = dayColor * sunColor;
        shiftedColor = Color.Lerp(sunColor, Color.Lerp(daylightShiftedColor, horizonShiftedColor, sunsetColourFactor), colorShiftStrength);

        float centreBrightness = Mathf.Lerp(Mathf.Max(0f, minimumSunsetCentreBrightness), Mathf.Max(0f, dayEmissionMultiplier), 1f - sunsetColourFactor);
        float styledVisibility = Mathf.Max(visibleDiscFactor, glowFactor * Mathf.Clamp01(behindPlanetEmissionMultiplier));
        emissionMultiplier = centreBrightness * styledVisibility + sunsetColourFactor * glowFactor * Mathf.Max(0f, horizonEmissionBoost);
        visualAlpha = glowFactor;

        bool invalidGeometry = !radiusValid || !cameraOutside || !sunRadiusValid;
        if (invalidGeometry && !appearanceGeometryWarningActive)
        {
            if (!radiusValid) Debug.LogWarning("[SunAppearance] Current visible planet radius is invalid.", this);
            else if (!cameraOutside) Debug.LogWarning("[SunAppearance] Camera is inside the resolved visible planet radius; apparent horizon geometry is undefined.", this);
            if (!sunRadiusValid) Debug.LogWarning("[SunAppearance] Apparent sun radius could not be resolved from sun scale, core radius, and camera distance.", this);
        }
        appearanceGeometryWarningActive = invalidGeometry;
        LogAppearanceDiagnosticsIfModeChanged();
    }

    float ResolveVisiblePlanetRadiusWorld()
    {
        float localRadius = planetGenerator != null ? planetGenerator.CurrentVisibleOuterRadius : planetRadius;
        if (planetCenter == null) return localRadius;
        Vector3 scale = planetCenter.lossyScale;
        float maximumScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        return localRadius * maximumScale;
    }

    void LogAppearanceDiagnosticsIfModeChanged()
    {
        PlanetGridType mode = planetGenerator != null ? planetGenerator.CurrentGridType : PlanetGridType.LegacyCubeSphere;
        if (hasAppearanceGridMode && mode == lastAppearanceGridMode) return;
        hasAppearanceGridMode = true;
        lastAppearanceGridMode = mode;
        if (!logSunLightingDiagnostics) return;
        Debug.Log($"[SunAppearance] gridMode={mode}, cameraPlanetDistance={cameraToPlanetDistance:F4}, visiblePlanetRadius={resolvedVisiblePlanetRadius:F4}, planetAngularRadiusDeg={apparentPlanetAngularRadiusDegrees:F4}, sunAngularRadiusDeg={apparentSunAngularRadiusDegrees:F4}, sunCentreHeightAboveLimbDeg={sunCentreHeightAboveLimbDegrees:F4}, sunsetColourFactor={sunsetColourFactor:F3}, visibleDiscFactor={visibleDiscFactor:F3}, glowFactor={glowFactor:F3}", this);
    }

    void DestroyRuntimeObject(UnityEngine.Object obj)
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
