using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlanetCellInspectorController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private PlanetGenerator planetGenerator;
    [SerializeField] private PlanetResourceMap planetResourceMap;
    [SerializeField] private PlanetCellInspectorPanel inspectorPanel;
    [SerializeField] private PlanetCellSelectionMarker selectionMarker;

    [Header("Picking")]
    [SerializeField] private LayerMask raycastMask = ~0;
    [SerializeField] private bool ignoreClicksOverUi = true;
    [SerializeField, Min(0.01f)] private float refreshIntervalSeconds = 0.2f;

    private bool hasSelection;
    private int selectedCellIndex = -1;
    private Vector3 selectedDirection;
    private float refreshTimer;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (planetGenerator == null)
        {
            planetGenerator = FindFirstObjectByType<PlanetGenerator>();
        }

        if (planetResourceMap == null)
        {
            planetResourceMap = planetGenerator != null
                ? planetGenerator.GetComponent<PlanetResourceMap>()
                : FindFirstObjectByType<PlanetResourceMap>();
        }
    }

    private void Update()
    {
        if (targetCamera == null || planetGenerator == null || planetResourceMap == null)
        {
            return;
        }

        RefreshSelectedSnapshot();

        if (!WasPrimaryPointerPressedThisFrame())
        {
            return;
        }

        if (ignoreClicksOverUi && IsPointerOverUi())
        {
            return;
        }

        if (!TryGetPointerScreenPosition(out Vector2 screenPos))
        {
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hitInfo, Mathf.Infinity, raycastMask, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        if (!IsPlanetHit(hitInfo.transform))
        {
            return;
        }

        Vector3 directionFromCenter = (hitInfo.point - planetGenerator.transform.position).normalized;
        int cellIndex = PlanetGridIndexing.DirectionToCellIndex(directionFromCenter, Mathf.Max(1, planetGenerator.resolution));

        if (!planetResourceMap.TryGetCellInspectionSnapshot(cellIndex, directionFromCenter, out PlanetResourceMap.CellInspectionSnapshot snapshot))
        {
            return;
        }

        hasSelection = true;
        selectedCellIndex = cellIndex;
        selectedDirection = directionFromCenter;
        refreshTimer = 0f;

        PresentSnapshot(snapshot, directionFromCenter);
    }

    private void RefreshSelectedSnapshot()
    {
        if (!hasSelection || inspectorPanel == null || !inspectorPanel.IsVisible())
        {
            return;
        }

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f)
        {
            return;
        }

        refreshTimer = refreshIntervalSeconds;
        if (!planetResourceMap.TryGetCellInspectionSnapshot(selectedCellIndex, selectedDirection, out PlanetResourceMap.CellInspectionSnapshot snapshot))
        {
            return;
        }

        PresentSnapshot(snapshot, selectedDirection);
    }

    private void PresentSnapshot(PlanetResourceMap.CellInspectionSnapshot snapshot, Vector3 direction)
    {
        if (inspectorPanel != null)
        {
            inspectorPanel.ShowSnapshot(snapshot);
        }

        if (selectionMarker != null)
        {
            selectionMarker.ShowSelection(snapshot.CellIndex, direction, snapshot.IsOcean);
        }
    }

    private bool IsPlanetHit(Transform hitTransform)
    {
        if (hitTransform == null)
        {
            return false;
        }

        return hitTransform == planetGenerator.transform || hitTransform.IsChildOf(planetGenerator.transform);
    }

    private static bool TryGetPointerScreenPosition(out Vector2 screenPos)
    {
        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                if (touch.press.isPressed || touch.press.wasPressedThisFrame)
                {
                    screenPos = touch.position.ReadValue();
                    return true;
                }
            }
        }

        if (Mouse.current != null)
        {
            screenPos = Mouse.current.position.ReadValue();
            return true;
        }

        screenPos = default;
        return false;
    }

    private static bool WasPrimaryPointerPressedThisFrame()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            return true;
        }

        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                if (touch.press.wasPressedThisFrame)
                {
                    return true;
                }
            }
        }

        return false;
    }
    private static bool IsPointerOverUi()
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        if (Input.touchCount > 0)
        {
            return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
        }

        return EventSystem.current.IsPointerOverGameObject();
    }
}
