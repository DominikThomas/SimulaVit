using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class CameraRotation : MonoBehaviour
{
    private const float MinOrbitPitch = 10f;
    private const float MaxOrbitPitch = 85f;
    private const float DefaultFarOrbitPitch = 70f;
    private const string RigRootName = "CameraRigRoot";
    private const string PitchPivotName = "CameraPitchPivot";

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
    private Transform rigRoot;
    private Transform pitchPivot;

    private float targetYaw;
    private float targetOrbitPitch;
    private float targetDistance;
    private float smoothedYaw;
    private float smoothedPitch;
    private float smoothedDistance;
    private float yawVelocity;
    private float pitchVelocity;
    private float distanceVelocity;
    private float planetRadius;
    private Vector2 lookInput;
    private bool isOrbiting;

    private Vector3 TargetPosition => targetTransform != null ? targetTransform.position : Vector3.zero;

    private void Awake()
    {
        InitializeTargetAndDistance();
        SetupRigHierarchy();
        SyncStateFromCurrentTransform();
        InitializeInput();
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

    private void LateUpdate()
    {
        if (isOrbiting)
        {
            targetYaw += lookInput.x * rotationSpeed;
            targetOrbitPitch = Mathf.Clamp(targetOrbitPitch - lookInput.y * rotationSpeed, MinOrbitPitch, MaxOrbitPitch);
        }

        float tiltBlend = GetTiltBlend(targetDistance);
        float nearSurfacePitch = Mathf.Clamp(90f - maxTiltAngle, MinOrbitPitch, MaxOrbitPitch);
        float effectivePitchTarget = Mathf.Lerp(targetOrbitPitch, nearSurfacePitch, tiltBlend);

        Vector3 radialDirection = GetOrbitDirection(targetYaw, effectivePitchTarget);
        float surfaceRadius = planetGenerator != null ? planetGenerator.GetSurfaceRadius(radialDirection) : planetRadius;
        float seaRadius = planetGenerator != null && planetGenerator.OceanEnabled ? planetGenerator.GetOceanRadius() : surfaceRadius;
        bool overOcean = planetGenerator != null && planetGenerator.OceanEnabled && surfaceRadius < seaRadius - 0.001f;

        float terrainClearance = GetTerrainClearance();
        float minAllowedDistance = overOcean
            ? Mathf.Max(surfaceRadius + terrainClearance, seaRadius - underwaterAllowance)
            : surfaceRadius + terrainClearance;
        float effectiveDistanceTarget = Mathf.Max(targetDistance, minAllowedDistance);

        float smoothTime = GetSmoothTime();
        smoothedYaw = Mathf.SmoothDampAngle(smoothedYaw, targetYaw, ref yawVelocity, smoothTime);
        smoothedPitch = Mathf.SmoothDampAngle(smoothedPitch, effectivePitchTarget, ref pitchVelocity, smoothTime);
        smoothedPitch = Mathf.Clamp(smoothedPitch, MinOrbitPitch, MaxOrbitPitch);
        smoothedDistance = Mathf.SmoothDamp(smoothedDistance, effectiveDistanceTarget, ref distanceVelocity, smoothTime);

        ApplyRigPose(smoothedYaw, smoothedPitch, smoothedDistance);
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

    private void SetupRigHierarchy()
    {
        if (transform.parent != null && transform.parent.name == PitchPivotName && transform.parent.parent != null && transform.parent.parent.name == RigRootName)
        {
            pitchPivot = transform.parent;
            rigRoot = pitchPivot.parent;
            return;
        }

        Transform originalParent = transform.parent;

        GameObject rigRootObject = new GameObject(RigRootName);
        rigRoot = rigRootObject.transform;
        rigRoot.SetParent(originalParent, false);
        rigRoot.position = TargetPosition;
        rigRoot.rotation = Quaternion.identity;

        GameObject pitchPivotObject = new GameObject(PitchPivotName);
        pitchPivot = pitchPivotObject.transform;
        pitchPivot.SetParent(rigRoot, false);
        pitchPivot.localPosition = Vector3.zero;
        pitchPivot.localRotation = Quaternion.identity;

        transform.SetParent(pitchPivot, true);
    }

    private void SyncStateFromCurrentTransform()
    {
        Vector3 offset = transform.position - TargetPosition;
        if (offset.sqrMagnitude < 0.0001f)
        {
            offset = new Vector3(0f, 0f, -(planetRadius + distanceBuffer));
        }

        float initialDistance = Mathf.Clamp(offset.magnitude, minZoomDistance, maxZoomDistance);
        float horizontalDistance = new Vector2(offset.x, offset.z).magnitude;

        targetYaw = Mathf.Atan2(offset.x, -offset.z) * Mathf.Rad2Deg;
        targetOrbitPitch = Mathf.Clamp(Mathf.Atan2(offset.y, Mathf.Max(0.0001f, horizontalDistance)) * Mathf.Rad2Deg, MinOrbitPitch, MaxOrbitPitch);
        targetOrbitPitch = Mathf.Max(targetOrbitPitch, DefaultFarOrbitPitch);
        targetDistance = initialDistance;

        smoothedYaw = targetYaw;
        smoothedPitch = targetOrbitPitch;
        smoothedDistance = targetDistance;

        ApplyRigPose(smoothedYaw, smoothedPitch, smoothedDistance);
    }

    private void ApplyRigPose(float yaw, float pitch, float distance)
    {
        if (rigRoot == null || pitchPivot == null)
        {
            return;
        }

        rigRoot.position = TargetPosition;
        rigRoot.rotation = Quaternion.AngleAxis(yaw, Vector3.up);
        pitchPivot.localPosition = Vector3.zero;
        pitchPivot.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
        transform.localPosition = new Vector3(0f, 0f, -distance);
        transform.localRotation = Quaternion.identity;
    }

    private Vector3 GetOrbitDirection(float yaw, float pitch)
    {
        Quaternion orbitRotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
        return (orbitRotation * Vector3.back).normalized;
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

    private float GetSmoothTime()
    {
        return 1f / Mathf.Max(0.01f, smoothingSpeed);
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
            targetDistance = Mathf.Clamp(targetDistance + zoomDelta, minZoomDistance, maxZoomDistance);
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
            targetDistance = 10.0f;
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
            targetDistance = 10.0f;
            Debug.LogError("PlanetGenerator script not found on GameObject tagged 'Planet'. Defaulting to 10.0f.");
            return;
        }

        planetRadius = planetGenerator.radius;
        maxZoomDistance = Mathf.Max(minZoomDistance, maxZoomDistance);
        targetDistance = Mathf.Clamp(planetGenerator.radius + distanceBuffer, minZoomDistance, maxZoomDistance);
        tiltTransitionStartDistance = Mathf.Clamp(tiltTransitionStartDistance, minZoomDistance, maxZoomDistance);
        maxTiltAngle = Mathf.Clamp(maxTiltAngle, 0f, 85f);
        underwaterAllowance = Mathf.Max(0f, underwaterAllowance);
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
