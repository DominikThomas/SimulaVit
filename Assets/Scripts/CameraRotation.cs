using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class CameraRotation : MonoBehaviour
{
    [Header("Orbit")]
    [SerializeField] private float rotationSpeed = 1.0f;
    [SerializeField] private float distanceBuffer = 6.0f;

    [Header("Zoom")]
    [SerializeField] private Transform targetTransform;
    [SerializeField] private float minZoomDistance = 8.0f;
    [SerializeField] private float maxZoomDistance = 40.0f;
    [SerializeField] private float zoomSpeed = 8.0f;
    [SerializeField] private float pinchZoomSpeed = 0.02f;

    [Header("Input")]
    [SerializeField] private InputActionAsset controls;

    private InputAction lookDeltaAction;
    private InputAction orbitActivateAction;
    private InputActionMap cameraActionMap;

    private float orbitDistance;
    private float planetRadius;
    private float currentX;
    private float currentY;
    private Vector2 lookInput;
    private bool isOrbiting;

    private Vector3 TargetPosition => targetTransform != null ? targetTransform.position : Vector3.zero;

    private void Awake()
    {
        InitializeTargetAndDistance();
        InitializeInput();
        orbitDistance = Mathf.Clamp(orbitDistance, minZoomDistance, maxZoomDistance);
    }

    private void OnEnable()
    {
        if (cameraActionMap != null)
        {
            cameraActionMap.Enable();
        }
    }

    private void OnDisable()
    {
        if (cameraActionMap != null)
        {
            cameraActionMap.Disable();
        }
    }

    private void Update()
    {
        lookInput = lookDeltaAction != null ? lookDeltaAction.ReadValue<Vector2>() : Vector2.zero;
        HandleZoomInput();
    }

    private void OnDestroy()
    {
        if (orbitActivateAction == null)
        {
            return;
        }

        orbitActivateAction.performed -= OnOrbitActivatePerformed;
        orbitActivateAction.canceled -= OnOrbitActivateCanceled;
    }

    private void LateUpdate()
    {
        if (isOrbiting)
        {
            currentY += lookInput.x * rotationSpeed;
            currentX -= lookInput.y * rotationSpeed;
            currentX = Mathf.Clamp(currentX, -80f, 80f);
        }

        Quaternion orbitRotation = Quaternion.Euler(currentX, currentY, 0f);
        Vector3 position = TargetPosition + orbitRotation * new Vector3(0f, 0f, -orbitDistance);

        transform.position = position;
        transform.LookAt(TargetPosition, Vector3.up);
    }

    private void HandleZoomInput()
    {
        float zoomDelta = 0f;

        if (Mouse.current != null)
        {
            zoomDelta -= Mouse.current.scroll.ReadValue().y * zoomSpeed * 0.01f;
        }

        if (Touchscreen.current != null)
        {
            TouchControl primaryTouch = Touchscreen.current.primaryTouch;
            TouchControl secondaryTouch = GetSecondaryTouch(primaryTouch.touchId.ReadValue());
            if (primaryTouch.press.isPressed && secondaryTouch != null)
            {
                Vector2 primaryPosition = primaryTouch.position.ReadValue();
                Vector2 secondaryPosition = secondaryTouch.position.ReadValue();
                Vector2 previousPrimaryPosition = primaryPosition - primaryTouch.delta.ReadValue();
                Vector2 previousSecondaryPosition = secondaryPosition - secondaryTouch.delta.ReadValue();

                float previousDistance = Vector2.Distance(previousPrimaryPosition, previousSecondaryPosition);
                float currentDistance = Vector2.Distance(primaryPosition, secondaryPosition);
                zoomDelta -= (currentDistance - previousDistance) * pinchZoomSpeed;
            }
        }

        if (Mathf.Abs(zoomDelta) > Mathf.Epsilon)
        {
            orbitDistance = Mathf.Clamp(orbitDistance + zoomDelta, minZoomDistance, maxZoomDistance);
        }
    }

    private TouchControl GetSecondaryTouch(int primaryTouchId)
    {
        foreach (TouchControl touch in Touchscreen.current.touches)
        {
            if (!touch.press.isPressed || touch.touchId.ReadValue() == primaryTouchId)
            {
                continue;
            }

            return touch;
        }

        return null;
    }

    private void InitializeTargetAndDistance()
    {
        GameObject planetObject = GameObject.FindWithTag("Planet");
        if (planetObject == null)
        {
            orbitDistance = 10.0f;
            Debug.LogError("GameObject with tag 'Planet' not found. Defaulting camera orbit distance to 10.0f.");
            return;
        }

        if (targetTransform == null)
        {
            targetTransform = planetObject.transform;
        }

        PlanetGenerator generator = planetObject.GetComponent<PlanetGenerator>();
        if (generator == null)
        {
            orbitDistance = 10.0f;
            Debug.LogError("PlanetGenerator script not found on GameObject tagged 'Planet'. Defaulting to 10.0f.");
            return;
        }

        planetRadius = generator.radius;
        orbitDistance = generator.radius + distanceBuffer;
        minZoomDistance = Mathf.Max(planetRadius + 0.5f, minZoomDistance);
        maxZoomDistance = Mathf.Max(minZoomDistance, maxZoomDistance);
    }

    private void InitializeInput()
    {
        if (controls == null)
        {
            Debug.LogError("CameraRotation is missing InputActionAsset reference.");
            enabled = false;
            return;
        }

        cameraActionMap = controls.FindActionMap("Camera", true);
        if (cameraActionMap == null)
        {
            Debug.LogError("Camera action map not found in InputActionAsset.");
            enabled = false;
            return;
        }

        lookDeltaAction = cameraActionMap.FindAction("LookDelta", true);
        orbitActivateAction = cameraActionMap.FindAction("OrbitActivate", true);

        if (lookDeltaAction == null || orbitActivateAction == null)
        {
            Debug.LogError("CameraRotation is missing LookDelta or OrbitActivate actions.");
            enabled = false;
            return;
        }

        orbitActivateAction.performed += OnOrbitActivatePerformed;
        orbitActivateAction.canceled += OnOrbitActivateCanceled;
    }

    private void OnOrbitActivatePerformed(InputAction.CallbackContext context)
    {
        isOrbiting = true;
    }

    private void OnOrbitActivateCanceled(InputAction.CallbackContext context)
    {
        isOrbiting = false;
    }
}
