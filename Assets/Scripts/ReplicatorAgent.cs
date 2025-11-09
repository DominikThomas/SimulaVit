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

    // Public variables to control movement
    private float planetRadius;
    public float movementSpeed = 0.5f; // How fast it moves across the surface
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
        // Ensure the replicator starts on the surface
        MaintainOnSurface();
    }

    void Update()
    {
        age += Time.deltaTime;

        // Death Check: If lifespan is exceeded OR if random chance triggers death
        if (age > maxLifespan || Random.value < deathProbabilityPerSecond * Time.deltaTime)
        {
            StartCoroutine(DieAndFade());
        }

        // Replication Check (handled in Update for per-second probability)
        if (Random.value < reproductionProbability * Time.deltaTime)
        {
            Replicate();
        }

        HandleMovement();
        MaintainOnSurface();
    }

    // -----------------------------------------------------------------------

    void HandleMovement()
    {
        if (Random.value < 0.05f)
        {
            // 1. Update the Target Yaw (Decision Making remains the same)
            randomTurnTimer -= Time.deltaTime;

            if (randomTurnTimer <= 0)
            {
                // Choose a new random direction target (e.g., +/- 90 degrees from current)
                targetYaw = Random.Range(-90f, 90f);
                // Reset the timer for the next decision
                randomTurnTimer = Random.Range(1f, maxTimeBetweenTurns);
            }

            // --- CRUCIAL CHANGE: Smooth Angular Blending ---

            // 2. Smooth Turning
            // Use Mathf.Lerp to smoothly move the 'currentYaw' toward the 'targetYaw'.
            // Use Time.deltaTime multiplied by a turning factor (e.g., 2) for smoothness.
            // We use the rotationSpeed to control how quickly it blends.
            currentYaw = Mathf.Lerp(currentYaw, targetYaw, Time.deltaTime * turningSpeed * 0.1f);

            // 3. Apply the Rotation
            // Apply only the *change* in yaw this frame, which is the currentYaw itself.
            // This makes the object constantly rotate while moving forward.
            transform.Rotate(transform.up, currentYaw * Time.deltaTime, Space.World);

            // 4. Movement (Remains the same)
            transform.Translate(Vector3.forward * movementSpeed * Time.deltaTime, Space.Self);

            // 5. Check if we have completed the turn goal
            // If the currentYaw is very close to the targetYaw, reset the target to prevent continuous small rotations.
            if (Mathf.Abs(currentYaw - targetYaw) < 0.1f)
            {
                targetYaw = 0f;
            }
        }
    }

    void MaintainOnSurface()
    {
        // The direction pointing from the planet center to the replicator position
        Vector3 surfaceNormal = transform.position.normalized;

        // 1. Fix Position: Ensure it's always at the right distance
        // Using 1.001f factor for a slight hover above the mesh
        transform.position = surfaceNormal * (planetRadius * 1.001f);

        // 2. Fix Rotation (Orientation): This is the critical change.

        // A. Calculate a stable forward direction. 
        // Project the agent's current forward vector onto the plane perpendicular to the surface normal.
        // This forces the forward direction to be perfectly horizontal to the surface.
        Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, surfaceNormal).normalized;

        // B. If projectedForward is zero (e.g., exactly at the pole), use a fallback.
        if (projectedForward == Vector3.zero)
        {
            // Fallback: Use the right vector for stability
            projectedForward = transform.right;
        }

        // C. Use LookRotation to perfectly orient the agent:
        // Its up direction is the surfaceNormal, and its forward is the projectedForward.
        Quaternion targetRotation = Quaternion.LookRotation(projectedForward, surfaceNormal);

        // D. Apply the rotation smoothly.
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
        // Using Time.deltaTime * 10f ensures the correction speed scales with frame rate.
    }

    IEnumerator DieAndFade()
    {
        // Disable movement and replication immediately
        enabled = false;

        MeshRenderer renderer = GetComponent<MeshRenderer>();
        Light agentLight = GetComponent<Light>();
        float fadeDuration = 3.0f;
        float startTime = Time.time;

        // Store original color/intensity
        Color startColor = renderer.material.color;
        float startIntensity = agentLight.intensity;

        while (Time.time < startTime + fadeDuration)
        {
            float t = (Time.time - startTime) / fadeDuration;

            // Slowing Speed (Slowing movement down before disabling it fully)
            // If movement is still enabled at the start, you can modify movementSpeed here.

            // Fading Color and Light to Zero
            renderer.material.color = Color.Lerp(startColor, Color.clear, t);
            agentLight.intensity = Mathf.Lerp(startIntensity, 0f, t);

            yield return null;
        }

        // CRITICAL: Find the PlanetGenerator before destroying the object
        PlanetGenerator generator = GetComponentInParent<PlanetGenerator>();

        if (generator != null)
        {
            // Decrement the global counter
            // NOTE: generator.replicatorCount must be public for this to work.
            if (generator.replicatorCount > 0)
            {
                generator.replicatorCount--;
            } 
            else
            {
                // Log a warning if it tries to decrement when already at zero, 
                // indicating a potential race condition but preventing negative numbers.
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