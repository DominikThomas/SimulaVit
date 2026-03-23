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

    [Header("Close-up Tilt")]
    [SerializeField] private float tiltTransitionStartDistance = 18.0f;
    [SerializeField] private float maxTiltAngle = 65.0f;
    [SerializeField] private float underwaterAllowance = 1.5f;
    [SerializeField] private float smoothingSpeed = 8.0f;

    [Header("Input")]
    [SerializeField] private InputActionAsset controls;

    private InputAction lookDeltaAction;
    private InputAction orbitActivateAction;
    private InputActionMap cameraActionMap;

    private PlanetGenerator planetGenerator;
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
        Vector3 radialDirection = (orbitRotation * Vector3.back).normalized;

        float surfaceRadius = planetGenerator != null ? planetGenerator.GetSurfaceRadius(radialDirection) : planetRadius;
        float seaRadius = planetGenerator != null && planetGenerator.OceanEnabled ? planetGenerator.GetOceanRadius() : surfaceRadius;

        bool overOcean = planetGenerator != null && planetGenerator.OceanEnabled && surfaceRadius < seaRadius - 0.001f;
        float terrainClearance = GetTerrainClearance();
        float minAllowedDistance = overOcean
            ? Mathf.Max(surfaceRadius + terrainClearance, seaRadius - underwaterAllowance)
            : surfaceRadius + terrainClearance;
        float desiredDistance = Mathf.Max(orbitDistance, minAllowedDistance);

        Vector3 desiredPosition = TargetPosition + radialDirection * desiredDistance;
        Vector3 lookTarget = GetBlendedLookTarget(orbitRotation, radialDirection, surfaceRadius, seaRadius, overOcean, desiredDistance);
        Quaternion desiredRotation = Quaternion.LookRotation((lookTarget - desiredPosition).normalized, radialDirection);

        float smoothing = 1f - Mathf.Exp(-Mathf.Max(0.01f, smoothingSpeed) * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothing);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, smoothing);
    }

    private Vector3 GetBlendedLookTarget(
        Quaternion orbitRotation,
        Vector3 radialDirection,
        float surfaceRadius,
        float seaRadius,
        bool overOcean,
        float desiredDistance)
    {
        float tiltBlend = GetTiltBlend(orbitDistance);
        float tiltAngle = maxTiltAngle * tiltBlend;
        Vector3 surfacePoint = TargetPosition + radialDirection * surfaceRadius;

        Vector3 tangentForward = Vector3.ProjectOnPlane(orbitRotation * Vector3.up, radialDirection).normalized;
        if (tangentForward.sqrMagnitude < 0.0001f)
        {
            tangentForward = Vector3.ProjectOnPlane(orbitRotation * Vector3.right, radialDirection).normalized;
        }

        float closeRange = Mathf.Max(0.01f, tiltTransitionStartDistance - minZoomDistance);
        float lookAheadDistance = Mathf.Lerp(0f, closeRange * 1.5f, tiltBlend);
        float verticalBias = overOcean
            ? Mathf.Lerp(0f, -underwaterAllowance * 0.5f, tiltBlend)
            : Mathf.Lerp(0f, surfaceRadius * 0.05f, tiltBlend);

        Vector3 nearLookTarget = surfacePoint + tangentForward * lookAheadDistance + radialDirection * verticalBias;
        Vector3 farLookTarget = TargetPosition;

        Vector3 blendedTarget = Vector3.Lerp(farLookTarget, nearLookTarget, tiltBlend);
        if (tiltAngle > 0f)
        {
            Vector3 desiredForward = (blendedTarget - (TargetPosition + radialDirection * desiredDistance)).normalized;
            Vector3 rightAxis = Vector3.Cross(radialDirection, desiredForward).normalized;
            if (rightAxis.sqrMagnitude > 0.0001f)
            {
                Quaternion tiltRotation = Quaternion.AngleAxis(tiltAngle * 0.15f, rightAxis);
                desiredForward = tiltRotation * desiredForward;
                blendedTarget = TargetPosition + radialDirection * desiredDistance + desiredForward * Mathf.Max(1f, desiredDistance);
            }
        }

        if (overOcean && desiredDistance < seaRadius)
        {
            float belowSurfaceDepth = seaRadius - desiredDistance;
            blendedTarget -= radialDirection * Mathf.Min(belowSurfaceDepth, underwaterAllowance);
        }

        return blendedTarget;
    }

    private float GetTiltBlend(float distance)
    {
        if (tiltTransitionStartDistance <= minZoomDistance)
        {
            return distance <= minZoomDistance ? 1f : 0f;
        }

        float normalized = Mathf.InverseLerp(tiltTransitionStartDistance, minZoomDistance, distance);
        return normalized * normalized * (3f - 2f * normalized);
    }

    private float GetTerrainClearance()
    {
        Camera attachedCamera = GetComponent<Camera>();
        float nearClip = attachedCamera != null ? attachedCamera.nearClipPlane : 0.3f;
        return Mathf.Max(nearClip * 1.5f, planetRadius * 0.01f);
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

        planetGenerator = planetObject.GetComponent<PlanetGenerator>();
        if (planetGenerator == null)
        {
            orbitDistance = 10.0f;
            Debug.LogError("PlanetGenerator script not found on GameObject tagged 'Planet'. Defaulting to 10.0f.");
            return;
        }

        planetRadius = planetGenerator.radius;
        orbitDistance = planetGenerator.radius + distanceBuffer;
        maxZoomDistance = Mathf.Max(minZoomDistance, maxZoomDistance);
        tiltTransitionStartDistance = Mathf.Clamp(tiltTransitionStartDistance, minZoomDistance, maxZoomDistance);
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
