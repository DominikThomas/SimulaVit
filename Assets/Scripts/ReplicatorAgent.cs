using UnityEngine;
using System.Collections; // Needed for Coroutines

public class ReplicatorAgent : MonoBehaviour
{
    public GameObject replicatorPrefab;

    [Header("Life & Death")]
    private float age = 0f;
    public float maxLifespan = 30f; // Base lifespan (can be mutated later)
    public float deathProbabilityPerSecond = 0.005f; // Chance of spontaneous death (0.5% per second)

    [Header("Agent Properties")]
    public float reproductionProbability = 0.2f; // Chance to replicate per second
    public Color baseColor = Color.white;

    [Header("Movement & Surface")]
    // New public variable for clean tuning (default 0.05f is safe)
    public float surfaceHoverOffset = 0.05f;

    // Public variables to control movement
    private float planetRadius;
    public float movementSpeed = 0.05f; // How fast it moves across the surface
    public float turningSpeed = 5.0f; // How quickly it changes direction (degrees per second)

    private float targetYaw = 0f; // The angle (in degrees) the replicator is trying to reach
    private float randomTurnTimer = 0f; // Timer to control when a new random target is set
    public float maxTimeBetweenTurns = 3f; // Max seconds before a new random direction is chosen

    private float currentYaw = 0f;

    // NEW: Quicker Ramp-Up Duration
    public float flareUpDuration = 0.5f; // Time from zero to peak (0.5 seconds is very quick)
    public float dimDownDuration = 2.0f; // Time from peak to normal (Stays the same)

    private Light replicatorLight;
    private float originalIntensity;
    public float flarePeakMultiplier = 2.0f;
    public float flareDuration = 2.0f; // Total time for the effect

    void Awake()
    {
        PlanetGenerator generator = GetComponentInParent<PlanetGenerator>();
        if (generator != null)
        {
            // 2. Read the actual radius from the generator.
            planetRadius = generator.radius;
        }
        else
        {
            // Fallback for safety, though it shouldn't happen
            Debug.LogError("Replicator spawned without a PlanetGenerator parent. Defaulting radius to 1.0f.");
            planetRadius = 1.0f;
        }

        replicatorLight = GetComponent<Light>();
        if (replicatorLight != null)
        {
            originalIntensity = replicatorLight.intensity;

            // CRUCIAL CHANGE 1: Set light intensity to 0 before starting the sequence
            replicatorLight.intensity = 0f;

            StartCoroutine(LightFlareSequence());
        }
    }

    // Coroutine to control the light's intensity over time
    IEnumerator LightFlareSequence()
    {
        float timer = 0f;
        float peakIntensity = originalIntensity * flarePeakMultiplier;

        // 1. PHASE 1: Fade In and Flare Up (0% to 200% intensity)
        // Duration uses the new, shorter flareUpDuration
        while (timer < flareUpDuration)
        {
            timer += Time.deltaTime;
            float t = timer / flareUpDuration; // t goes from 0 to 1

            // The intensity now ramps from 0 up to the Peak Intensity
            replicatorLight.intensity = Mathf.Lerp(0f, peakIntensity, t);

            yield return null;
        }

        // Ensure it hits the peak value exactly
        replicatorLight.intensity = peakIntensity;

        // 2. PHASE 2: Dim Down (200% to 100% intensity)
        // Duration uses the longer dimDownDuration
        timer = 0f;
        while (timer < dimDownDuration)
        {
            timer += Time.deltaTime;
            float t = timer / dimDownDuration; // t goes from 0 to 1

            // The intensity ramps down from Peak Intensity back to Original Intensity
            replicatorLight.intensity = Mathf.Lerp(peakIntensity, originalIntensity, t);

            yield return null;
        }

        // Final sanity check
        replicatorLight.intensity = originalIntensity;
    }

    void Start()
    {
        // Initialize planetRadius
        PlanetGenerator generator = FindObjectOfType<PlanetGenerator>();
        if (generator != null)
        {
            planetRadius = generator.radius;
        }

        // Start the Coroutine to safely handle surface maintenance
        StartCoroutine(SurfaceMaintainer());
    }

    void Update()
    {
        age += Time.deltaTime;

        // --- LOGIC THAT MUST RUN EVERY FRAME ---

        // Death Check
        if (age > maxLifespan || Random.value < deathProbabilityPerSecond * Time.deltaTime)
        {
            StartCoroutine(DieAndFade());
        }

        // Replication Check
        if (Random.value < reproductionProbability * Time.deltaTime)
        {
            Replicate();
        }

        // REMOVE HandleMovement() from here! It runs in the coroutine.
    }

    // -----------------------------------------------------------------------

    IEnumerator SurfaceMaintainer()
    {
        // CRITICAL: Wait for 2 frames to ensure MeshCollider is ready.
        yield return null;
        yield return null;

        // Now safely run the positioning and movement logic every frame.
        while (true)
        {
            // 1. Position the agent on the surface and set its rotation (transform.up)
            MaintainOnSurface();

            // 2. Move the agent (which relies on the rotation set in MaintainOnSurface)
            HandleMovement();

            yield return null;
        }
    }

    // Inside ReplicatorAgent.cs

    void HandleMovement()
    {
        // 1. Update the Target Turn Speed
        randomTurnTimer -= Time.deltaTime;

        if (randomTurnTimer <= 0)
        {
            // Choose a new random turn speed (-90 to +90 degrees *per second*)
            targetYaw = Random.Range(-90f, 90f);
            randomTurnTimer = Random.Range(1f, maxTimeBetweenTurns);
        }

        // 2. Smoothly update the *current turn speed* (angular velocity)
        currentYaw = Mathf.Lerp(currentYaw, targetYaw, Time.deltaTime * turningSpeed * 0.1f);
    }

    void MaintainOnSurface()
    {
        // 1. Skip if position is invalid (safety check for coroutine timing)
        if (transform.position.sqrMagnitude < 0.1f)
        {
            return;
        }

        // 2. Reverse Raycast (stable physics check)
        Vector3 directionFromCenter = transform.position.normalized;
        float maxSafeDistance = planetRadius * 3f;
        Vector3 rayStartPoint = directionFromCenter * maxSafeDistance;
        Ray ray = new Ray(rayStartPoint, -directionFromCenter);
        RaycastHit hit;
        float raycastDistance = maxSafeDistance * 2f;

        if (Physics.Raycast(ray, out hit, raycastDistance))
        {
            Vector3 surfaceNormal = hit.normal;

            // 3. --- CALCULATE ROTATION ---

            // Project current 'forward' onto the ground plane (hit.normal)
            Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, surfaceNormal);

            // Apply the 'yaw' rotation (from HandleMovement) around the surface normal
            Quaternion yawRotation = Quaternion.AngleAxis(currentYaw * Time.deltaTime, surfaceNormal);
            Vector3 finalForward = yawRotation * projectedForward;

            // Create the target rotation (facing 'finalForward', with 'surfaceNormal' as UP)
            Quaternion targetRotation = Quaternion.LookRotation(finalForward, surfaceNormal);

            // 4. --- CALCULATE MOVEMENT & INTENDED POSITION ---

            // Calculate movement vector
            Vector3 moveVector = finalForward.normalized * movementSpeed * Time.deltaTime;

            // Calculate the *intended* next position (ground + move + hover)
            Vector3 intendedPosition = hit.point + moveVector + (surfaceNormal * surfaceHoverOffset);

            // 5. --- APPLY TRANSFORM WITH CRITICAL SMOOTHING ---

            // CRITICAL FIX 1: Smooth the position (Linear Interpolation)
            // Lerp from current position to the intended position. Factor of 20f provides fast, stable snapping.
            transform.position = Vector3.Lerp(transform.position, intendedPosition, Time.deltaTime * 20f);

            // CRITICAL FIX 2: Smooth the rotation (Spherical Linear Interpolation)
            // This dampens rotation jitter and stops the light flashing. Factor of 10f is stable.
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
        }
        else
        {
            // This should not be hit now, but it's a safe fallback.
            Debug.LogError("CRITICAL FAILURE: Reverse Raycast failed.", gameObject);
        }
    }

    void SetMaterialRenderingModeToFade(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    IEnumerator DieAndFade()
    {
        // Disable movement and replication immediately
        enabled = false;

        MeshRenderer renderer = GetComponent<MeshRenderer>();
        Light agentLight = GetComponent<Light>();
        float fadeDuration = 3.0f;
        float startTime = Time.time;

        // CRITICAL FIX: Prepare the material for transparency
        SetMaterialRenderingModeToFade(renderer.material);

        // Store original color/intensity
        Color startColor = renderer.material.color;
        float startIntensity = agentLight.intensity;

        // Define the target color: The original RGB but with Alpha set to 0 (fully transparent)
        Color targetColor = startColor;
        targetColor.a = 0f;

        while (Time.time < startTime + fadeDuration)
        {
            float t = (Time.time - startTime) / fadeDuration;

            // Fading to transparency (interpolating the alpha channel from 1 to 0)
            renderer.material.color = Color.Lerp(startColor, targetColor, t);
            agentLight.intensity = Mathf.Lerp(startIntensity, 0f, t);

            yield return null;
        }

        // Final application of zero alpha/intensity and cleanup
        renderer.material.color = targetColor;
        agentLight.intensity = 0f;

        // CRITICAL: Find the PlanetGenerator before destroying the object
        PlanetGenerator generator = GetComponentInParent<PlanetGenerator>();

        if (generator != null)
        {
            // Decrement the global counter
            if (generator.replicatorCount > 0)
            {
                generator.replicatorCount--;
            }
            else
            {
                Debug.LogWarning("Replicator count attempted to decrement when already at zero.");
            }
        }

        // Final cleanup
        Destroy(gameObject);
    }

    void Replicate()
    {
        PlanetGenerator generator = GetComponentInParent<PlanetGenerator>();

        // 1. CAPACITY CHECK (Should be the first check)
        if (generator != null)
        {
            // Use the soft cap logic (e.g., 10% overshoot) to prevent population lock
            float hardCap = generator.maxReplicatorCount * 1.10f;
            if (generator.replicatorCount >= hardCap)
            {
                return; // Abort replication if hard limit is reached
            }
        }

        // 2. PREFAB FALLBACK CHECK (Only assigns the reference if it's missing)
        if (replicatorPrefab == null)
        {
            if (generator != null && generator.replicatorPrefab != null)
            {
                replicatorPrefab = generator.replicatorPrefab;
            }
            else
            {
                Debug.LogError("FATAL: Cannot replicate because replicatorPrefab is unassigned.");
                return; // Abort if we can't get the reference
            }
        }

        // --- CORE REPLICATION LOGIC STARTS HERE (ALWAYS RUNS IF CHECKS PASS) ---

        // 3. Increment Counter
        if (generator != null)
        {
            generator.replicatorCount++;
        }

        // 4. Calculate Spawn Point
        Vector3 offset = Random.onUnitSphere * 0.01f;
        Vector3 spawnPoint = transform.position + offset;

        // 5. Instantiate and Mutate
        GameObject baby = Instantiate(replicatorPrefab, spawnPoint, Quaternion.identity, transform.parent);

        ReplicatorAgent babyAgent = baby.GetComponent<ReplicatorAgent>();
        Mutate(babyAgent);
    }

    void Mutate(ReplicatorAgent baby)
    {
        // 1. Inherit properties from the parent
        baby.maxLifespan = maxLifespan;
        baby.movementSpeed = movementSpeed;
        baby.reproductionProbability = reproductionProbability;

        // 2. Introduce a random mutation factor (e.g., up to 10% deviation)
        float mutationFactor = 0.1f;

        // Mutate Lifespan: e.g., slightly shorter or longer
        baby.maxLifespan *= 1f + Random.Range(-mutationFactor, mutationFactor);

        // Mutate Speed: e.g., slightly faster or slower
        baby.movementSpeed *= 1f + Random.Range(-mutationFactor, mutationFactor);

        // Mutate Color: Tie properties to visual feedback (H S V shift)
        Color.RGBToHSV(baseColor, out float h, out float s, out float v);

        // Shift the hue (color) slightly based on a property, e.g., Lifespan
        h += (baby.maxLifespan - maxLifespan) * 5f;

        if (h > 1f) h -= 1f;
        if (h < 0f) h += 1f;

        //baby.baseColor = Color.HSVToRGB(h, s, v); //use random colour instead
        baby.baseColor = Random.ColorHSV(
        0f, 1f,   // Hue min/max (Full spectrum)
        0.5f, 1f, // Saturation min/max (Avoid pale/grey colors)
        0.7f, 1f  // Value min/max (Avoid very dark colors)
        );

        // Apply the new color immediately
        baby.GetComponent<MeshRenderer>().material.color = baby.baseColor;
    }
}