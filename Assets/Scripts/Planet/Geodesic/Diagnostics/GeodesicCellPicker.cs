using System.Text;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class GeodesicCellPicker : MonoBehaviour
{
    [Header("Picking")]
    public Camera pickingCamera;
    [Tooltip("Optional collider for the generated geodesic sphere. If omitted, the picker searches this object and its children.")]
    [SerializeField] private Collider pickingCollider;
    [Tooltip("Used only when no suitable collider is available.")]
    [SerializeField] private bool useAnalyticSphereFallback = true;
    [Tooltip("Fallback world-space radius used only when no collider or renderer bounds can provide a radius.")]
    [SerializeField] private float fallbackSphereRadius = 10f;

    [Header("Selection Display")]
    [SerializeField] private bool showSelectionPopup = true;
    [SerializeField] private bool logSuccessfulSelection = true;
    [SerializeField] private Rect popupRect = new Rect(18f, 18f, 380f, 2100f);

    [Header("Selected Cell (Runtime Debug)")]
    public int selectedCellIndex = -1;
    public int selectedNeighborCount;
    public bool selectedIsPentagon;
    public float selectedUnitArea;
    public int[] selectedNeighborIndices = System.Array.Empty<int>();

    private GeodesicGridTopology topology;
    private PlanetGenerator planetGenerator;
    private bool warnedMissingCamera;
    private bool warnedNoPickSurface;

    private void Awake()
    {
        ResolvePickingCollider();
        planetGenerator = GetComponent<PlanetGenerator>();
    }

    public void SetTopology(GeodesicGridTopology t)
    {
        topology = t;
        ResolvePickingCollider();
        ClearSelection();
    }

    public void ClearSelection()
    {
        selectedCellIndex = -1;
        selectedNeighborCount = 0;
        selectedIsPentagon = false;
        selectedUnitArea = 0f;
        selectedNeighborIndices = System.Array.Empty<int>();
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Pointer pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame)
        {
            return;
        }

        Pick(pointer.position.ReadValue());
#else
        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        Pick(Input.mousePosition);
#endif
    }

    public bool Pick(Vector2 screenPosition)
    {
        if (topology == null || topology.CellCount == 0)
        {
            return false;
        }

        Camera cam = pickingCamera != null ? pickingCamera : Camera.main;
        if (cam == null)
        {
            if (!warnedMissingCamera)
            {
                Debug.LogWarning(
                    "[GeodesicCellPicker] No pickingCamera is assigned and Camera.main could not be found.",
                    this);
                warnedMissingCamera = true;
            }

            return false;
        }

        Ray ray = cam.ScreenPointToRay(screenPosition);

        if (!TryGetPlanetHit(ray, out Vector3 worldHitPoint))
        {
            return false;
        }

        Vector3 localDirection = transform.InverseTransformPoint(worldHitPoint).normalized;
        int selected = SelectNearest(localDirection);
        return selected >= 0;
    }

    private bool TryGetPlanetHit(Ray ray, out Vector3 worldHitPoint)
    {
        ResolvePickingCollider();

        if (pickingCollider != null &&
            pickingCollider.Raycast(ray, out RaycastHit colliderHit, Mathf.Infinity))
        {
            worldHitPoint = colliderHit.point;
            return true;
        }

        // A general raycast is useful when the generated collider is on another child
        // and has not yet been found or assigned.
        if (Physics.Raycast(ray, out RaycastHit physicsHit, Mathf.Infinity))
        {
            Transform hitTransform = physicsHit.collider != null
                ? physicsHit.collider.transform
                : null;

            if (hitTransform == transform || (hitTransform != null && hitTransform.IsChildOf(transform)))
            {
                pickingCollider = physicsHit.collider;
                worldHitPoint = physicsHit.point;
                return true;
            }
        }

        if (useAnalyticSphereFallback &&
            TryIntersectFallbackSphere(ray, out worldHitPoint))
        {
            return true;
        }

        worldHitPoint = default;

        if (!warnedNoPickSurface)
        {
            Debug.LogWarning(
                "[GeodesicCellPicker] Click did not hit the geodesic planet. " +
                "Assign the generated MeshCollider/SphereCollider to pickingCollider, " +
                "or ensure the generated sphere has a Renderer so the analytic fallback can estimate its radius. " +
                "The analytic sphere fallback is approximate for displaced geodesic terrain; prefer the refreshed MeshCollider.",
                this);
            warnedNoPickSurface = true;
        }

        return false;
    }

    private bool TryIntersectFallbackSphere(Ray ray, out Vector3 worldHitPoint)
    {
        Vector3 center = transform.position;
        float radius = ResolveWorldSphereRadius();

        if (radius <= 0f)
        {
            worldHitPoint = default;
            return false;
        }

        Vector3 toOrigin = ray.origin - center;
        float b = Vector3.Dot(toOrigin, ray.direction);
        float c = Vector3.Dot(toOrigin, toOrigin) - radius * radius;
        float discriminant = b * b - c;

        if (discriminant < 0f)
        {
            worldHitPoint = default;
            return false;
        }

        float root = Mathf.Sqrt(discriminant);
        float t = -b - root;

        if (t < 0f)
        {
            t = -b + root;
        }

        if (t < 0f)
        {
            worldHitPoint = default;
            return false;
        }

        worldHitPoint = ray.GetPoint(t);
        return true;
    }

    private float ResolveWorldSphereRadius()
    {
        if (pickingCollider is SphereCollider sphereCollider)
        {
            float scale = Mathf.Max(
                Mathf.Abs(sphereCollider.transform.lossyScale.x),
                Mathf.Abs(sphereCollider.transform.lossyScale.y),
                Mathf.Abs(sphereCollider.transform.lossyScale.z));

            return Mathf.Max(0f, sphereCollider.radius * scale);
        }

        if (pickingCollider != null)
        {
            return Mathf.Max(
                pickingCollider.bounds.extents.x,
                pickingCollider.bounds.extents.y,
                pickingCollider.bounds.extents.z);
        }

        if (planetGenerator != null)
        {
            float transformScale = Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.y),
                Mathf.Abs(transform.lossyScale.z));
            return planetGenerator.MaximumSurfaceRadius * transformScale;
        }

        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            return Mathf.Max(
                renderer.bounds.extents.x,
                renderer.bounds.extents.y,
                renderer.bounds.extents.z);
        }

        float fallbackTransformScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z));

        return Mathf.Max(0f, fallbackSphereRadius * fallbackTransformScale);
    }

    private void ResolvePickingCollider()
    {
        if (pickingCollider != null)
        {
            return;
        }

        pickingCollider = GetComponent<Collider>();
        if (pickingCollider == null)
        {
            pickingCollider = GetComponentInChildren<Collider>();
        }
    }

    public int SelectNearest(Vector3 direction)
    {
        if (topology == null || topology.CellCount == 0)
        {
            ClearSelection();
            return -1;
        }

        Vector3 normalizedDirection = direction.normalized;
        float bestDot = -2f;
        int bestIndex = -1;

        for (int index = 0; index < topology.CellCount; index++)
        {
            float dot = Vector3.Dot(normalizedDirection, topology.CellDirections[index]);

            if (dot > bestDot + 1e-7f ||
                (Mathf.Abs(dot - bestDot) <= 1e-7f &&
                 (bestIndex < 0 || index < bestIndex)))
            {
                bestDot = dot;
                bestIndex = index;
            }
        }

        Apply(bestIndex);

        if (logSuccessfulSelection && bestIndex >= 0)
        {
            string terrainDiagnostics = string.Empty;
            if (planetGenerator != null)
            {
                Vector3 centreDirection = topology.CellDirections[selectedCellIndex];
                PlanetTerrainSample terrainSample = planetGenerator.EvaluateGeodesicTerrainSample(centreDirection);
                terrainDiagnostics = $", classification={(planetGenerator.IsGeodesicCellOcean(selectedCellIndex) ? "ocean" : "land")}, coastline={planetGenerator.IsGeodesicCellCoastline(selectedCellIndex)}, terrainHeight={terrainSample.HeightOffset:F6}, mountainMask={terrainSample.MountainMask:F4}, ridge={terrainSample.RidgeValue:F4}, continent={terrainSample.ContinentValue:F4}, rawTerrainRadius={planetGenerator.GetGeodesicCellRawTerrainRadius(selectedCellIndex):F6}, finalSeafloorRadius={planetGenerator.GetGeodesicCellSeafloorRadius(selectedCellIndex):F6}, finalSurfaceRadius={planetGenerator.GetSurfaceRadiusAtDirection(centreDirection):F6}, seaLevelRadius={planetGenerator.GeodesicSeaLevelRadius:F6}, baseDepth={planetGenerator.GetGeodesicCellBaseWaterDepth(selectedCellIndex):F6}, finalBathymetryDepth={planetGenerator.GetGeodesicCellWaterDepth(selectedCellIndex):F6}, distanceToShore={planetGenerator.GetGeodesicCellDistanceToShore(selectedCellIndex):F6}, depth01={planetGenerator.GetGeodesicCellNormalizedDepth(selectedCellIndex):F3}, bathymetryRegion={planetGenerator.GetGeodesicCellBathymetryRegion(selectedCellIndex)}, basinNoiseContribution={planetGenerator.GetGeodesicCellBasinNoiseContribution(selectedCellIndex):F4}, oceanNeighborCount={planetGenerator.GetGeodesicOceanNeighborCount(selectedCellIndex)}";
            }

            Debug.Log(
                $"[GeodesicCellPicker] Selected cell={selectedCellIndex}, " +
                $"neighbors={selectedNeighborCount}, pentagon={selectedIsPentagon}, " +
                $"unitArea={selectedUnitArea:F8}{terrainDiagnostics}.",
                this);
        }

        return bestIndex;
    }

    private void Apply(int index)
    {
        selectedCellIndex = index;

        if (index < 0)
        {
            ClearSelection();
            return;
        }

        selectedNeighborCount = topology.NeighborCounts[index];
        selectedIsPentagon = topology.IsPentagon[index];
        selectedUnitArea = topology.UnitCellAreas[index];

        selectedNeighborIndices = new int[selectedNeighborCount];
        for (int slot = 0; slot < selectedNeighborCount; slot++)
        {
            selectedNeighborIndices[slot] = topology.Neighbors6[index * 6 + slot];
        }
    }

    private void OnGUI()
    {
        if (!showSelectionPopup || selectedCellIndex < 0)
        {
            return;
        }

        popupRect = GUI.Window(
            GetInstanceID(),
            popupRect,
            DrawSelectionPopup,
            "Geodesic Cell Selection");
    }

    private void DrawSelectionPopup(int windowId)
    {
        GUILayout.Label($"Cell index: {selectedCellIndex}");
        GUILayout.Label($"Cell type: {(selectedIsPentagon ? "Pentagon" : "Hexagon")}");
        GUILayout.Label($"Neighbor count: {selectedNeighborCount}");
        GUILayout.Label($"Unit-sphere area: {selectedUnitArea:F8}");
        if (planetGenerator != null)
        {
            float height = planetGenerator.GetCellTerrainHeight(selectedCellIndex);
            float surfaceRadius = planetGenerator.GetCellSurfaceRadius(selectedCellIndex);
            float rawRadius = planetGenerator.GetGeodesicCellRawTerrainRadius(selectedCellIndex);
            float normalizedHeight = planetGenerator.GetCellNormalizedTerrainHeight(selectedCellIndex);
            bool ocean = planetGenerator.IsGeodesicCellOcean(selectedCellIndex);
            bool coastline = planetGenerator.IsGeodesicCellCoastline(selectedCellIndex);
            PlanetTerrainSample sample = planetGenerator.EvaluateGeodesicTerrainSample(topology.CellDirections[selectedCellIndex]);
            GUILayout.Label($"Terrain height: {height:F5}");
            GUILayout.Label($"Land/ocean: {(ocean ? "Ocean" : "Land")}");
            GUILayout.Label($"Coastline: {coastline}");
            GUILayout.Label($"Raw terrain radius: {rawRadius:F5}");
            GUILayout.Label($"Final seafloor radius: {surfaceRadius:F5}");
            GUILayout.Label($"Sea-level radius: {planetGenerator.GeodesicSeaLevelRadius:F5}");
            GUILayout.Label($"Base depth: {planetGenerator.GetGeodesicCellBaseWaterDepth(selectedCellIndex):F5}");
            GUILayout.Label($"Final bathymetry depth: {planetGenerator.GetGeodesicCellWaterDepth(selectedCellIndex):F5}");
            GUILayout.Label($"Distance to shore: {planetGenerator.GetGeodesicCellDistanceToShore(selectedCellIndex):F5}");
            GUILayout.Label($"Depth 01: {planetGenerator.GetGeodesicCellNormalizedDepth(selectedCellIndex):F3}");
            GUILayout.Label($"Bathymetry region: {planetGenerator.GetGeodesicCellBathymetryRegion(selectedCellIndex)}");
            GUILayout.Label($"Basin noise contribution: {planetGenerator.GetGeodesicCellBasinNoiseContribution(selectedCellIndex):F4}");
            GUILayout.Label($"Ocean-neighbor count: {planetGenerator.GetGeodesicOceanNeighborCount(selectedCellIndex)}");
            GUILayout.Label($"Geodesic cell area: {selectedUnitArea * planetGenerator.BasePlanetRadius * planetGenerator.BasePlanetRadius:F8}");
            GUILayout.Label($"Normalized terrain: {normalizedHeight:F3}");
            GUILayout.Label($"Mountain mask: {sample.MountainMask:F3}");
            GUILayout.Label($"Ridge value: {sample.RidgeValue:F3}");
            GUILayout.Label($"Continent value: {sample.ContinentValue:F3}");
        }

        StringBuilder neighbors = new StringBuilder();
        for (int i = 0; i < selectedNeighborIndices.Length; i++)
        {
            if (i > 0)
            {
                neighbors.Append(", ");
            }

            neighbors.Append(selectedNeighborIndices[i]);
        }

        GUILayout.Label($"Neighbors: {neighbors}");

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Clear selection"))
        {
            ClearSelection();
        }

        GUI.DragWindow(new Rect(0f, 0f, popupRect.width, 24f));
    }
}