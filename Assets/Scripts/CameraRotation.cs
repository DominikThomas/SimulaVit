using UnityEngine;
using UnityEngine.InputSystem; // NEW: Crucial namespace for the Input System

public class CameraRotation : MonoBehaviour
{
    // Public variables to control rotation speed and distance
    public float rotationSpeed = 1.0f; // Adjusted for New Input System delta
    public float distanceBuffer = 6.0f;
    private float orbitDistance; // The calculated final distance

    // NEW: Reference to the Input Actions Asset
    public InputActionAsset controls;

    // Private variables to hold the specific actions
    private InputAction lookDeltaAction;
    private InputAction orbitActivateAction;

    private float currentX = 0.0f;
    private float currentY = 0.0f;
    private Vector2 lookInput;
    private bool isOrbiting = false;

    // The point we are orbiting around (Planet Center)
    private readonly Vector3 target = Vector3.zero;

    void Awake()
    {
        // 1. Find the PlanetGenerator script (assuming it's on the parent 'Planet System')
        GameObject planetObject = GameObject.FindWithTag("Planet");

        if (planetObject != null)
        {
            PlanetGenerator generator = planetObject.GetComponent<PlanetGenerator>();

            if (generator != null)
            {
                // 2. Calculate the orbit distance: Planet Radius + Buffer
                orbitDistance = generator.radius + distanceBuffer;
            }
            else
            {
                // Fallback for safety if tag exists but script does not
                orbitDistance = 10.0f;
                Debug.LogError("PlanetGenerator script not found on GameObject tagged 'Planet'. Defaulting to 10.0f.");
            }
        }
        else
        {
            // Fallback if no object is found with the correct tag
            orbitDistance = 10.0f;
            Debug.LogError("GameObject with tag 'Planet' not found. Defaulting camera orbit distance to 10.0f.");
        }
        // Find the actions defined in the asset
        lookDeltaAction = controls.FindActionMap("Camera").FindAction("LookDelta");
        orbitActivateAction = controls.FindActionMap("Camera").FindAction("OrbitActivate");

        // Subscribe to the OrbitActivate action press and release events
        orbitActivateAction.performed += ctx => isOrbiting = true;
        orbitActivateAction.canceled += ctx => isOrbiting = false;
    }

    void OnEnable()
    {
        // Enable all actions when the script becomes active
        controls.FindActionMap("Camera").Enable();
    }

    void OnDisable()
    {
        // Disable all actions when the script is deactivated
        controls.FindActionMap("Camera").Disable();
    }

    void Update()
    {
        // Read the mouse delta value every frame
        lookInput = lookDeltaAction.ReadValue<Vector2>();
    }

    void LateUpdate()
    {
        // 1. Check if Orbit is active (Left Mouse Button held down)
        if (isOrbiting)
        {
            // Horizontal movement (Mouse X) changes the Y rotation (around the planet)
            currentY += lookInput.x * rotationSpeed;

            // Vertical movement (Mouse Y) changes the X rotation (up and down)
            currentX -= lookInput.y * rotationSpeed;

            // Limit vertical rotation to prevent the camera from going upside down
            currentX = Mathf.Clamp(currentX, -80f, 80f);
        }

        // 2. Calculate and apply new position and rotation
        Quaternion rotation = Quaternion.Euler(currentX, currentY, 0);

        // CRUCIAL CHANGE: Use orbitDistance instead of the hardcoded 'distance'
        Vector3 position = rotation * new Vector3(0.0f, 0.0f, -orbitDistance);

        transform.rotation = rotation;
        transform.position = target + position;
    }
}