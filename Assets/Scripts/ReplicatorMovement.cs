using UnityEngine;
using System.Collections; // Needed for Coroutines

public class ReplicatorMovement : MonoBehaviour
{
    // Public variables to control movement
    public float planetRadius = 4.0f; // Manually set this to match PlanetGenerator for now
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
        HandleMovement();
        MaintainOnSurface();
    }

    // -----------------------------------------------------------------------

    void HandleMovement()
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

    void MaintainOnSurface()
    {
        // This function is still crucial for fixing the position and orientation every frame.
        Vector3 directionToSurface = transform.position.normalized;
        float surfaceOffset = 0.05f;

        // 1. Fix Position (Ensuring it's always at the right distance)
        transform.position = directionToSurface * (planetRadius + surfaceOffset);

        // 2. Fix Rotation (Ensuring its local UP is always the surface normal)
        // The most stable way to orient an object on a sphere:
        // Force the UP vector (local Y) to be the direction away from the center (directionToSurface).

        // We use Quaternion.Slerp to smoothly blend to the new orientation
        // This is often more stable than direct assignment.
        Quaternion targetRotation = Quaternion.FromToRotation(transform.up, directionToSurface) * transform.rotation;
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 0.5f); // 0.5f is a blend factor
    }
}