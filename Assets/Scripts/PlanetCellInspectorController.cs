using UnityEngine;
using UnityEngine.EventSystems;

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

        if (inspectorPanel != null)
        {
            inspectorPanel.ShowSnapshot(snapshot);
        }

        if (selectionMarker != null)
        {
            selectionMarker.ShowSelection(snapshot.CellIndex, directionFromCenter, snapshot.IsOcean);
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
        if (Input.touchCount > 0)
        {
            screenPos = Input.GetTouch(0).position;
            return true;
        }

        screenPos = Input.mousePosition;
        return true;
    }

    private static bool WasPrimaryPointerPressedThisFrame()
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            return touch.phase == TouchPhase.Began;
        }

        return Input.GetMouseButtonDown(0);
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
