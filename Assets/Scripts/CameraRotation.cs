using UnityEngine;
using UnityEngine.EventSystems;
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
    [SerializeField] private bool useDynamicMinZoom = true;
    [SerializeField] private float terrainClearance = 0.5f;
    [SerializeField] private bool allowUnderwaterZoom = false;
    [SerializeField] private float maxUnderwaterDepth = 0.0f;
    [SerializeField] private float oceanSurfaceClearance = 0.0f;

    [Header("Close-Range Tilt")]
    [SerializeField] private float tiltStartDistance = 14.0f;
    [SerializeField] private float maxTiltAngle = 25.0f;
    [SerializeField] private float tiltSmoothing = 8.0f;

    [Header("Input")]
    [SerializeField] private InputActionAsset controls;
    [SerializeField] private Vector2 touchLookScale = new Vector2(0.05f, 0.01f);

    private InputAction lookDeltaAction;
    private InputAction orbitActivateAction;
    private InputActionMap cameraActionMap;

    private float orbitDistance;
    private float planetRadius;
    private PlanetGenerator planetGenerator;
    private float currentX;
    private float currentY;
    private Vector2 lookInput;
    private bool isOrbiting;
    private bool isTouchOrbiting;
    private float currentTiltAngle;
    private float tiltVelocity;
    private int activeOrbitTouchId = -1;
    private int blockedOrbitTouchId = -1;
    private bool orbitActivateRequested;

    private Vector3 TargetPosition => targetTransform != null ? targetTransform.position : Vector3.zero;

    private void Awake()
    {
        InitializeTargetAndDistance();
        ValidateTiltSettings();
        InitializeInput();
        orbitDistance = Mathf.Clamp(orbitDistance, minZoomDistance, maxZoomDistance);
    }

    private void OnValidate()
    {
        maxTiltAngle = Mathf.Clamp(maxTiltAngle, 0f, 89f);
        tiltSmoothing = Mathf.Max(0f, tiltSmoothing);
        terrainClearance = Mathf.Max(0f, terrainClearance);
        maxUnderwaterDepth = Mathf.Max(0f, maxUnderwaterDepth);
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
        if (orbitActivateRequested)
        {
            orbitActivateRequested = false;

            if (IsPointerOverUi())
            {
                isOrbiting = false;
            }
            else
            {
                isOrbiting = true;
            }
        }
        lookInput = lookDeltaAction != null ? lookDeltaAction.ReadValue<Vector2>() : Vector2.zero;

        isTouchOrbiting = HandleTouchOrbitInput(out Vector2 touchLookInput);
        if (isTouchOrbiting)
        {
            lookInput += touchLookInput;
        }

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
        if (isOrbiting || isTouchOrbiting)
        {
            currentY += lookInput.x * rotationSpeed;
            currentX -= lookInput.y * rotationSpeed;
            currentX = Mathf.Clamp(currentX, -80f, 80f);
        }

        Quaternion orbitRotation = Quaternion.Euler(currentX, currentY, 0f);
        Vector3 orbitDirection = orbitRotation * Vector3.back;
        float localMinZoomDistance = GetMinZoomDistanceForDirection(orbitDirection);
        orbitDistance = Mathf.Clamp(orbitDistance, localMinZoomDistance, maxZoomDistance);
        Vector3 position = TargetPosition + orbitRotation * new Vector3(0f, 0f, -orbitDistance);
        Quaternion lookRotation = Quaternion.LookRotation(TargetPosition - position, Vector3.up);

        float targetTiltAngle = GetTargetTiltAngle();
        if (tiltSmoothing <= 0f)
        {
            currentTiltAngle = targetTiltAngle;
        }
        else
        {
            float smoothTime = 1f / tiltSmoothing;
            currentTiltAngle = Mathf.SmoothDampAngle(currentTiltAngle, targetTiltAngle, ref tiltVelocity, smoothTime);
        }

        transform.position = position;
        transform.rotation = lookRotation * Quaternion.Euler(-currentTiltAngle, 0f, 0f);
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
            Vector3 zoomDirection = GetCurrentOrbitDirection();
            float localMinZoomDistance = GetMinZoomDistanceForDirection(zoomDirection);
            orbitDistance = Mathf.Clamp(orbitDistance + zoomDelta, localMinZoomDistance, maxZoomDistance);
        }
    }

    private bool HandleTouchOrbitInput(out Vector2 touchDelta)
    {
        touchDelta = Vector2.zero;
        if (Touchscreen.current == null)
        {
            activeOrbitTouchId = -1;
            blockedOrbitTouchId = -1;
            return false;
        }

        int pressedTouchCount = 0;
        TouchControl firstPressedTouch = null;
        bool activeTouchStillPressed = false;
        bool blockedTouchStillPressed = false;

        foreach (TouchControl touch in Touchscreen.current.touches)
        {
            if (!touch.press.isPressed)
            {
                continue;
            }

            pressedTouchCount++;
            if (firstPressedTouch == null)
            {
                firstPressedTouch = touch;
            }

            int touchId = touch.touchId.ReadValue();
            if (touchId == activeOrbitTouchId)
            {
                activeTouchStillPressed = true;
            }

            if (touchId == blockedOrbitTouchId)
            {
                blockedTouchStillPressed = true;
            }
        }

        if (!activeTouchStillPressed)
        {
            activeOrbitTouchId = -1;
        }

        if (!blockedTouchStillPressed)
        {
            blockedOrbitTouchId = -1;
        }

        if (pressedTouchCount != 1 || firstPressedTouch == null)
        {
            activeOrbitTouchId = -1;
            return false;
        }

        int firstTouchId = firstPressedTouch.touchId.ReadValue();
        if (blockedOrbitTouchId == firstTouchId)
        {
            return false;
        }

        UnityEngine.InputSystem.TouchPhase touchPhase = firstPressedTouch.phase.ReadValue();
        if (activeOrbitTouchId < 0 && touchPhase == UnityEngine.InputSystem.TouchPhase.Began)
        {
            if (IsPointerOverUi(firstTouchId))
            {
                blockedOrbitTouchId = firstTouchId;
                return false;
            }

            activeOrbitTouchId = firstTouchId;
        }

        if (activeOrbitTouchId != firstTouchId)
        {
            return false;
        }

        Vector2 rawTouchDelta = firstPressedTouch.delta.ReadValue();
        touchDelta = new Vector2(rawTouchDelta.x * touchLookScale.x, rawTouchDelta.y * touchLookScale.y);
        return touchPhase == UnityEngine.InputSystem.TouchPhase.Moved;
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

        planetGenerator = generator;
        planetRadius = generator.radius;
        orbitDistance = generator.radius + distanceBuffer;
        minZoomDistance = Mathf.Max(planetRadius + 0.5f, minZoomDistance);
        maxZoomDistance = Mathf.Max(minZoomDistance, maxZoomDistance);
    }

    private void ValidateTiltSettings()
    {
        maxTiltAngle = Mathf.Clamp(maxTiltAngle, 0f, 89f);
        tiltSmoothing = Mathf.Max(0f, tiltSmoothing);
        tiltStartDistance = Mathf.Clamp(tiltStartDistance, minZoomDistance, maxZoomDistance);
    }

    private float GetTargetTiltAngle()
    {
        float closeRangeProgress = Mathf.InverseLerp(tiltStartDistance, minZoomDistance, orbitDistance);
        return closeRangeProgress * maxTiltAngle;
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
        orbitActivateRequested = true;
    }

    private void OnOrbitActivateCanceled(InputAction.CallbackContext context)
    {
        isOrbiting = false;
    }

    private static bool IsPointerOverUi(int pointerId = -1)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        return pointerId >= 0
            ? EventSystem.current.IsPointerOverGameObject(pointerId)
            : EventSystem.current.IsPointerOverGameObject();
    }

    private Vector3 GetCurrentOrbitDirection()
    {
        Vector3 fromTarget = transform.position - TargetPosition;
        if (fromTarget.sqrMagnitude > Mathf.Epsilon)
        {
            return fromTarget.normalized;
        }

        return Quaternion.Euler(currentX, currentY, 0f) * Vector3.back;
    }

    private float GetMinZoomDistanceForDirection(Vector3 directionFromCenter)
    {
        float fixedMinZoomDistance = minZoomDistance;
        if (!useDynamicMinZoom || planetGenerator == null || directionFromCenter.sqrMagnitude <= Mathf.Epsilon)
        {
            return fixedMinZoomDistance;
        }

        Vector3 normalizedDirection = directionFromCenter.normalized;
        float terrainSurfaceRadius = planetGenerator.GetSurfaceRadius(normalizedDirection);
        float landMinDistance = terrainSurfaceRadius + terrainClearance;
        float localMinDistance = landMinDistance;

        int cellIndex = PlanetGridIndexing.DirectionToCellIndex(normalizedDirection, planetGenerator.resolution);
        bool isOceanCell = planetGenerator.OceanEnabled && planetGenerator.IsOceanCell(cellIndex);
        if (isOceanCell)
        {
            float oceanSurfaceRadius = planetGenerator.GetOceanRadius();
            float oceanMinDistance = oceanSurfaceRadius + oceanSurfaceClearance;

            if (allowUnderwaterZoom)
            {
                oceanMinDistance = oceanSurfaceRadius + oceanSurfaceClearance - Mathf.Max(0f, maxUnderwaterDepth);
            }

            localMinDistance = Mathf.Max(landMinDistance, oceanMinDistance);
        }

        return Mathf.Clamp(localMinDistance, 0f, maxZoomDistance);
    }
}
