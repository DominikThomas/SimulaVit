using UnityEngine;
using UnityEngine.InputSystem; // NEW: Crucial namespace for the Input System

public class CameraRotation : MonoBehaviour
{
    // Public variables to control rotation speed and distance
    public float rotationSpeed = 1.0f; // Adjusted for New Input System delta
    public float distance = 10.0f;

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

        // 2. Calculate and apply new position and rotation (Remains similar to before)
        Quaternion rotation = Quaternion.Euler(currentX, currentY, 0);
        Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance);

        transform.rotation = rotation;
        transform.position = target + position;
    }
}