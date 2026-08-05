using System.Text;
using UnityEngine;
using Unity.Profiling;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class GeodesicCellPicker : MonoBehaviour
{
    private static readonly ProfilerMarker DynamicRefreshMarker = new ProfilerMarker("GeodesicCellPicker.DynamicRefresh");
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
    [Tooltip("Current runtime popup rectangle in IMGUI screen-local coordinates.")]
    [SerializeField] private Rect popupRect = new Rect(18f, 18f, 380f, 540f);

    [Header("Diagnostic Popup UI")]
    [Tooltip("Default popup width as a fraction of the current Game view width.")]
    [Range(0.2f, 0.6f)]
    [SerializeField] private float popupWidthFraction = 0.32f;
    [Tooltip("Default popup height as a fraction of the current Game view height.")]
    [Range(0.3f, 0.75f)]
    [SerializeField] private float popupHeightFraction = 0.55f;
    [Tooltip("Maximum popup width as a fraction of the current Game view width.")]
    [Range(0.25f, 0.8f)]
    [SerializeField] private float popupMaximumWidthFraction = 0.45f;
    [Tooltip("Maximum popup height as a fraction of the current Game view height.")]
    [Range(0.4f, 0.9f)]
    [SerializeField] private float popupMaximumHeightFraction = 0.8f;
    [Tooltip("Smallest allowed popup width in pixels before screen-margin clamping.")]
    [Min(220f)]
    [SerializeField] private float minimumPopupWidth = 300f;
    [Tooltip("Smallest allowed popup height in pixels before screen-margin clamping.")]
    [Min(160f)]
    [SerializeField] private float minimumPopupHeight = 220f;
    [Tooltip("Minimum distance in pixels between the popup and each Game view edge.")]
    [Range(0f, 64f)]
    [SerializeField] private float popupScreenMargin = 18f;
    [Tooltip("Additional bounded multiplier for picker-specific popup font and spacing.")]
    [Range(0.85f, 1.25f)]
    [SerializeField] private float popupFontScale = 1f;
    [Tooltip("Preserve dragged popup position between selected cells when possible.")]
    [SerializeField] private bool rememberPopupPosition = true;
    [Tooltip("Unscaled-time interval between dynamic simulation-value refreshes while the popup is visible.")]
    [Min(0.05f)]
    [SerializeField] private float popupDynamicRefreshInterval = 0.25f;

    [Header("Selected Cell (Runtime Debug)")]
    public int selectedCellIndex = -1;
    public int selectedNeighborCount;
    public bool selectedIsPentagon;
    public float selectedUnitArea;
    public int[] selectedNeighborIndices = System.Array.Empty<int>();

    private GeodesicGridTopology topology;
    private PlanetGenerator planetGenerator;
    private GeodesicOceanLayerDomain oceanLayerDomain;
    private GeodesicSurfaceTemperatureField temperatureField;
    private GeodesicOceanTemperatureField oceanTemperatureField;
    private GeodesicOceanResourceField oceanResourceField;
    private ReplicatorManager temperatureDisplayAuthority;
    private string selectedLayeredOceanLog = ", layeredOcean=unavailable";
    private string selectedLayeredOceanPopup = "unavailable";
    private string selectedCompactLayeredOceanPopup = "unavailable";
    private string selectedCompactPopup = string.Empty;
    private string selectedDetailedPopup = string.Empty;
    private string selectedCompactStaticHeader = string.Empty;
    private string selectedCompactStaticOcean = string.Empty;
    private string selectedDetailedStaticPopup = string.Empty;
    private float popupDynamicRefreshElapsed;
    private bool temperatureDisplayRefreshRequested;
    private bool showDetailedDebug;
    private bool warnedMissingCamera;
    private bool warnedNoPickSurface;
    private Vector2 popupScrollPosition;
    private int lastScreenWidth;
    private int lastScreenHeight;
    private bool popupPositionInitialized;
    private GUIStyle popupLabelStyle;
    private GUIStyle popupHeaderStyle;
    private GUIStyle popupSectionStyle;
    private GUIStyle popupSummaryStyle;
    private GUIStyle popupWindowStyle;
    private GUIStyle popupButtonStyle;
    private float cachedUiScale = -1f;
    private readonly StringBuilder neighborBuilder = new StringBuilder(96);

    private void Awake()
    {
        ResolvePickingCollider();
        planetGenerator = GetComponent<PlanetGenerator>();
        oceanLayerDomain = GetComponent<GeodesicOceanLayerDomain>();
        temperatureField = GetComponent<GeodesicSurfaceTemperatureField>();
        oceanTemperatureField = GetComponent<GeodesicOceanTemperatureField>();
        oceanResourceField = GetComponent<GeodesicOceanResourceField>();
        SetTemperatureDisplayAuthority(planetGenerator != null ? planetGenerator.ReplicatorManager : null);
    }

    private void OnEnable() => SubscribeToTemperatureDisplayAuthority();
    private void OnDisable() => UnsubscribeFromTemperatureDisplayAuthority();
    private void OnDestroy() => UnsubscribeFromTemperatureDisplayAuthority();

    public void SetTemperatureDisplayAuthority(ReplicatorManager authority)
    {
        if (ReferenceEquals(temperatureDisplayAuthority, authority)) { SubscribeToTemperatureDisplayAuthority(); return; }
        UnsubscribeFromTemperatureDisplayAuthority();
        temperatureDisplayAuthority = authority;
        SubscribeToTemperatureDisplayAuthority();
    }

    private void SubscribeToTemperatureDisplayAuthority()
    {
        if (!isActiveAndEnabled || temperatureDisplayAuthority == null) return;
        temperatureDisplayAuthority.TemperatureDisplayUnitChanged -= OnTemperatureDisplayUnitChanged;
        temperatureDisplayAuthority.TemperatureDisplayUnitChanged += OnTemperatureDisplayUnitChanged;
    }

    private void UnsubscribeFromTemperatureDisplayAuthority()
    {
        if (temperatureDisplayAuthority != null) temperatureDisplayAuthority.TemperatureDisplayUnitChanged -= OnTemperatureDisplayUnitChanged;
    }

    private void OnTemperatureDisplayUnitChanged(TemperatureDisplayUnit unit) => temperatureDisplayRefreshRequested = true;

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
        selectedLayeredOceanLog = ", layeredOcean=unavailable";
        selectedLayeredOceanPopup = "unavailable";
        selectedCompactLayeredOceanPopup = "unavailable";
        selectedCompactPopup = string.Empty;
        selectedDetailedPopup = string.Empty;
        selectedCompactStaticHeader = string.Empty;
        selectedCompactStaticOcean = string.Empty;
        selectedDetailedStaticPopup = string.Empty;
        popupDynamicRefreshElapsed = 0f;
        showDetailedDebug = false;
        popupScrollPosition = Vector2.zero;
    }

    private void Update()
    {
        if (temperatureDisplayRefreshRequested)
        {
            temperatureDisplayRefreshRequested = false;
            RefreshDynamicPopupText(true);
        }
        else
        {
            RefreshDynamicPopupText();
        }

#if ENABLE_INPUT_SYSTEM
        Pointer pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame)
        {
            return;
        }

        Vector2 pointerPosition = pointer.position.ReadValue();
        if (IsScreenPositionInsidePopup(pointerPosition))
        {
            return;
        }

        Pick(pointerPosition);
#else
        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        Vector2 mousePosition = Input.mousePosition;
        if (IsScreenPositionInsidePopup(mousePosition))
        {
            return;
        }

        Pick(mousePosition);
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
                terrainDiagnostics = $", classification={(planetGenerator.IsGeodesicCellOcean(selectedCellIndex) ? "ocean" : "land")}, coastline={planetGenerator.IsGeodesicCellCoastline(selectedCellIndex)}, terrainHeight={terrainSample.HeightOffset:F6}, mountainMask={terrainSample.MountainMask:F4}, ridge={terrainSample.RidgeValue:F4}, continent={terrainSample.ContinentValue:F4}, rawTerrainRadius={planetGenerator.GetGeodesicCellRawTerrainRadius(selectedCellIndex):F6}, finalSeafloorRadius={planetGenerator.GetGeodesicCellSeafloorRadius(selectedCellIndex):F6}, finalSurfaceRadius={planetGenerator.GetSurfaceRadiusAtDirection(centreDirection):F6}, seaLevelRadius={planetGenerator.GeodesicSeaLevelRadius:F6}, baseDepth={planetGenerator.GetGeodesicCellBaseWaterDepth(selectedCellIndex):F6}, finalBathymetryDepth={planetGenerator.GetGeodesicCellWaterDepth(selectedCellIndex):F6}, distanceToShore={(planetGenerator.IsGeodesicOceanWorldActive ? "N/A — OceanWorld" : planetGenerator.GetGeodesicCellDistanceToShore(selectedCellIndex).ToString("F6"))}, depth01={planetGenerator.GetGeodesicCellNormalizedDepth(selectedCellIndex):F3}, bathymetryRegion={planetGenerator.GetGeodesicCellBathymetryRegion(selectedCellIndex)}, basinNoiseContribution={planetGenerator.GetGeodesicCellBasinNoiseContribution(selectedCellIndex):F4}, oceanNeighborCount={planetGenerator.GetGeodesicOceanNeighborCount(selectedCellIndex)}, continentalInfluence01={planetGenerator.GetGeodesicCellContinentalInfluence01(selectedCellIndex):F3}, coastType={planetGenerator.GetGeodesicCellCoastType(selectedCellIndex)}, landComponentId={(planetGenerator.GetGeodesicCellLandComponentId(selectedCellIndex) >= 0 ? planetGenerator.GetGeodesicCellLandComponentId(selectedCellIndex).ToString() : "None")}, shelfProfile={planetGenerator.GetGeodesicCellShelfProfileType(selectedCellIndex)}, continentalShelfInfluence={planetGenerator.GetGeodesicCellContinentalShelfInfluence01(selectedCellIndex):F3}, oceanicIslandInfluence={planetGenerator.GetGeodesicCellOceanicIslandShelfInfluence01(selectedCellIndex):F3}, shelfWidthMul={planetGenerator.GetGeodesicCellLocalShelfWidthMultiplier(selectedCellIndex):F3}, continentalProfileWidthDeg={planetGenerator.GetGeodesicCellContinentalProfileShelfWidthDegrees(selectedCellIndex):F3}, finalShelfWidthDeg={planetGenerator.GetGeodesicCellFinalShelfWidthDegrees(selectedCellIndex):F3}, localShelfDepth={planetGenerator.GetGeodesicCellLocalShelfDepth(selectedCellIndex):F5}, approxCellSpacingDeg={planetGenerator.GetGeodesicCellApproxCellSpacingDegrees(selectedCellIndex):F3}, oceanicRelief(ridge/plateau/seamount/total)={planetGenerator.GetGeodesicCellRidgeContribution(selectedCellIndex):F5}/{planetGenerator.GetGeodesicCellPlateauContribution(selectedCellIndex):F5}/{planetGenerator.GetGeodesicCellSeamountContribution(selectedCellIndex):F5}/{planetGenerator.GetGeodesicCellTotalOceanicReliefContribution(selectedCellIndex):F5}{selectedLayeredOceanLog}";
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

        CacheSelectedLayeredOceanDiagnostics(index);
        showDetailedDebug = false;
        popupDynamicRefreshElapsed = 0f;
        CacheSelectedStaticPopupText(index);
        RefreshDynamicPopupText(true);

        popupScrollPosition = Vector2.zero;
        EnsurePopupRect(true);
    }

    private void CacheSelectedLayeredOceanDiagnostics(int cellIndex)
    {
        GeodesicOceanLayerGrid grid = oceanLayerDomain != null ? oceanLayerDomain.Grid : null;
        if (grid == null || cellIndex < 0 || cellIndex >= grid.CellCount)
        {
            selectedLayeredOceanLog = ", layeredOcean=unavailable";
            selectedLayeredOceanPopup = "unavailable";
            selectedCompactLayeredOceanPopup = "unavailable";
            return;
        }

        int activeCount = grid.ActiveLayerCountByCell[cellIndex];
        int topLayer = grid.GetTopLayerIndex(cellIndex);
        int bottomLayer = grid.GetBottomLayerIndex(cellIndex);
        float localDepth = grid.SourceOceanMask[cellIndex]
            ? Mathf.Max(0f, grid.OceanSurfaceRadius - grid.SourceSeafloorRadius[cellIndex])
            : 0f;
        float normalizedDepth = grid.MaximumOceanDepth > 0f
            ? Mathf.Clamp01(localDepth / grid.MaximumOceanDepth)
            : 0f;

        var log = new StringBuilder(320);
        var popup = new StringBuilder(320);
        var compactPopup = new StringBuilder(192);
        log.Append(", oceanLayerActiveCount=").Append(activeCount)
            .Append(", topLayerIndex=").Append(topLayer)
            .Append(", bottomLayerIndex=").Append(bottomLayer)
            .Append(", oceanLayerLocalDepth=").Append(localDepth.ToString("F6"))
            .Append(", oceanLayerNormalizedDepth=").Append(normalizedDepth.ToString("F3"));
        popup.Append("oceanLayerActiveCount=").Append(activeCount)
            .Append("\ntopLayerIndex=").Append(topLayer)
            .Append("\nbottomLayerIndex=").Append(bottomLayer)
            .Append("\nlocalOceanDepth=").Append(localDepth.ToString("F6"))
            .Append("\nnormalizedDepth=").Append(normalizedDepth.ToString("F3"));
        compactPopup.Append("Active layers: ").Append(activeCount);
        if (activeCount == 0) compactPopup.Append("\nNo active ocean resource layers.");

        for (int layer = 0; layer < activeCount; layer++)
        {
            int node = grid.GetNodeIndex(cellIndex, layer);
            int horizontalDegree = GetHorizontalLayerDegree(grid, node);
            int verticalDegree = GetVerticalLayerDegree(grid, node);
            float centerDepth = grid.OceanSurfaceRadius - grid.LayerCenterRadius[node];
            log.Append(", layer[").Append(layer).Append("]={thickness=").Append(grid.LayerThickness[node].ToString("F6"))
                .Append(", volume=").Append(grid.LayerVolume[node].ToString("G6"))
                .Append(", centerDepth=").Append(centerDepth.ToString("F6"))
                .Append(", horizontalDegree=").Append(horizontalDegree)
                .Append(", verticalDegree=").Append(verticalDegree).Append('}');
            popup.Append("\nlayer ").Append(layer)
                .Append(": thickness=").Append(grid.LayerThickness[node].ToString("F6"))
                .Append(", volume=").Append(grid.LayerVolume[node].ToString("G6"))
                .Append(", centerDepth=").Append(centerDepth.ToString("F6"))
                .Append(", H/V degree=").Append(horizontalDegree).Append('/').Append(verticalDegree)
                .Append(", CO2=").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.CO2))
                .Append(", O2=").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.O2))
                .Append(", CH4=").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.CH4))
                .Append(", H2=").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.H2))
                .Append(", H2S=").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.H2S))
                .Append(", Fe2=").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.Fe2))
                .Append(", OrganicC=").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.OrganicC));
            compactPopup.Append("\nL").Append(layer)
                .Append(": O2 ").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.O2))
                .Append(" | CO2 ").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.CO2))
                .Append(" | H2 ").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.H2))
                .Append(" | H2S ").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.H2S))
                .Append(" | Fe2 ").Append(GetResourceText(cellIndex, layer, GeodesicOceanResource.Fe2));
        }

        selectedLayeredOceanLog = log.ToString();
        selectedLayeredOceanPopup = popup.ToString();
        selectedCompactLayeredOceanPopup = compactPopup.ToString();
    }

    private void CacheSelectedStaticPopupText(int cellIndex)
    {
        if (planetGenerator == null)
        {
            selectedCompactStaticHeader = $"Cell index: {cellIndex}\nType: {(selectedIsPentagon ? "Pentagon" : "Hexagon")}";
            selectedCompactStaticOcean = string.Empty;
            selectedDetailedStaticPopup = selectedCompactStaticHeader;
            selectedCompactPopup = selectedCompactStaticHeader;
            selectedDetailedPopup = selectedDetailedStaticPopup;
            return;
        }

        bool ocean = planetGenerator.IsGeodesicCellOcean(cellIndex);
        float height = planetGenerator.GetCellTerrainHeight(cellIndex);
        float surfaceRadius = planetGenerator.GetCellSurfaceRadius(cellIndex);
        float rawRadius = planetGenerator.GetGeodesicCellRawTerrainRadius(cellIndex);
        float baseDepth = planetGenerator.GetGeodesicCellBaseWaterDepth(cellIndex);
        float finalDepth = planetGenerator.GetGeodesicCellWaterDepth(cellIndex);
        float radialDisplacement = surfaceRadius - rawRadius;
        PlanetTerrainSample sample = planetGenerator.EvaluateGeodesicTerrainSample(topology.CellDirections[cellIndex]);
        var compactHeader = new StringBuilder(128);
        compactHeader.Append("Cell index: ").Append(cellIndex)
            .Append("\nClassification: ").Append(ocean ? "Ocean" : "Land")
            .Append("\nType: ").Append(selectedIsPentagon ? "Pentagon" : "Hexagon");
        selectedCompactStaticHeader = compactHeader.ToString();
        selectedCompactStaticOcean = string.Empty;
        if (ocean)
        {
            var compactOcean = new StringBuilder(256);
            compactOcean.Append("\n\nOcean\nLocal ocean depth: ").Append(finalDepth.ToString("F5"))
                .Append("\nNormalized depth: ").Append(planetGenerator.GetGeodesicCellNormalizedDepth(cellIndex).ToString("F3"))
                .Append("\nBathymetry region: ").Append(planetGenerator.GetGeodesicCellBathymetryRegion(cellIndex))
                .Append("\nDistance to shore: ").Append(planetGenerator.IsGeodesicOceanWorldActive ? "N/A — OceanWorld" : planetGenerator.GetGeodesicCellDistanceToShore(cellIndex).ToString("F5"))
                .Append("\n").Append(selectedCompactLayeredOceanPopup);
            selectedCompactStaticOcean = compactOcean.ToString();
        }

        float addedDepth = Mathf.Max(0f, finalDepth - baseDepth);
        var detailed = new StringBuilder(1800);
        detailed.Append("Cell\nIndex: ").Append(cellIndex).Append("\nType: ").Append(selectedIsPentagon ? "Pentagon" : "Hexagon")
            .Append("\nNeighbors: ").Append(selectedNeighborCount).Append("\nUnit area: ").Append(selectedUnitArea.ToString("F8"))
            .Append("\nPhysical area: ").Append((selectedUnitArea * planetGenerator.BasePlanetRadius * planetGenerator.BasePlanetRadius).ToString("F8"))
            .Append("\nNeighbor IDs: ").Append(BuildNeighborText())
            .Append("\n\nTerrain\nHeight: ").Append(height.ToString("F5")).Append("\nNormalized: ").Append(planetGenerator.GetCellNormalizedTerrainHeight(cellIndex).ToString("F3"))
            .Append("\nRaw radius: ").Append(rawRadius.ToString("F5")).Append("\nFinal radius: ").Append(surfaceRadius.ToString("F5"))
            .Append("\nContinent: ").Append(sample.ContinentValue.ToString("F3")).Append("\nMountain mask: ").Append(sample.MountainMask.ToString("F3")).Append("\nRidge: ").Append(sample.RidgeValue.ToString("F3"))
            .Append("\n\nOcean\nClass: ").Append(ocean ? "Ocean" : "Land").Append("\nCoastline: ").Append(planetGenerator.IsGeodesicCellCoastline(cellIndex))
            .Append("\nSea level: ").Append(planetGenerator.GeodesicSeaLevelRadius.ToString("F5")).Append("\nOcean neighbors: ").Append(planetGenerator.GetGeodesicOceanNeighborCount(cellIndex))
            .Append("\nBase depth: ").Append(baseDepth.ToString("F5")).Append("\nFinal depth: ").Append(finalDepth.ToString("F5")).Append("\nDepth 01: ").Append(planetGenerator.GetGeodesicCellNormalizedDepth(cellIndex).ToString("F3"))
            .Append("\n\nLayered Ocean\nlayeredOcean: ").Append(selectedLayeredOceanPopup)
            .Append("\n\nBathymetry\nEnabled: ").Append(finalDepth > 0f || baseDepth > 0f || radialDisplacement < 0f).Append("\nRegion: ").Append(planetGenerator.GetGeodesicCellBathymetryRegion(cellIndex))
            .Append("\nDistance shore: ").Append(planetGenerator.IsGeodesicOceanWorldActive ? "N/A — OceanWorld" : planetGenerator.GetGeodesicCellDistanceToShore(cellIndex).ToString("F5"))
            .Append("\nAdded depth: ").Append(addedDepth.ToString("F5")).Append("\nRadial disp.: ").Append(radialDisplacement.ToString("F5")).Append("\nBasin noise: ").Append(planetGenerator.GetGeodesicCellBasinNoiseContribution(cellIndex).ToString("F4"))
            .Append("\nCoast type: ").Append(planetGenerator.GetGeodesicCellCoastType(cellIndex)).Append("\nShelf profile: ").Append(planetGenerator.GetGeodesicCellShelfProfileType(cellIndex)).Append("\nLand comp.: ").Append(planetGenerator.GetGeodesicCellLandComponentId(cellIndex))
            .Append("\nContinent 01: ").Append(planetGenerator.GetGeodesicCellContinentalInfluence01(cellIndex).ToString("F3")).Append("\nShelf infl.: ").Append(planetGenerator.GetGeodesicCellContinentalShelfInfluence01(cellIndex).ToString("F3")).Append("\nIsland infl.: ").Append(planetGenerator.GetGeodesicCellOceanicIslandShelfInfluence01(cellIndex).ToString("F3"))
            .Append("\nShelf width x: ").Append(planetGenerator.GetGeodesicCellLocalShelfWidthMultiplier(cellIndex).ToString("F3")).Append("\nCont. width°: ").Append(planetGenerator.GetGeodesicCellContinentalProfileShelfWidthDegrees(cellIndex).ToString("F3")).Append("\nFinal width°: ").Append(planetGenerator.GetGeodesicCellFinalShelfWidthDegrees(cellIndex).ToString("F3"))
            .Append("\nCell spacing°: ").Append(planetGenerator.GetGeodesicCellApproxCellSpacingDegrees(cellIndex).ToString("F3")).Append("\nShelf depth: ").Append(planetGenerator.GetGeodesicCellLocalShelfDepth(cellIndex).ToString("F5"))
            .Append("\nRidge relief: ").Append(planetGenerator.GetGeodesicCellRidgeContribution(cellIndex).ToString("F5")).Append("\nPlateau relief: ").Append(planetGenerator.GetGeodesicCellPlateauContribution(cellIndex).ToString("F5")).Append("\nSeamount relief: ").Append(planetGenerator.GetGeodesicCellSeamountContribution(cellIndex).ToString("F5")).Append("\nOcean relief: ").Append(planetGenerator.GetGeodesicCellTotalOceanicReliefContribution(cellIndex).ToString("F5"))
            .Append("\nModified: ").Append(!Mathf.Approximately(finalDepth, baseDepth) || !Mathf.Approximately(radialDisplacement, 0f));
        selectedDetailedStaticPopup = detailed.ToString();
        selectedCompactPopup = selectedCompactStaticHeader + "\nSurface temperature: unavailable\nIllumination / insolation: unavailable" + selectedCompactStaticOcean;
        selectedDetailedPopup = selectedDetailedStaticPopup + "\n\nSurface Temperature\nunavailable";
    }

    private void RefreshDynamicPopupText(bool force = false)
    {
        using (DynamicRefreshMarker.Auto())
        {
        if (!showSelectionPopup || selectedCellIndex < 0 || temperatureField == null || !temperatureField.IsInitialized)
        {
            return;
        }

        popupDynamicRefreshElapsed += Time.unscaledDeltaTime;
        if (!force && popupDynamicRefreshElapsed < Mathf.Max(0.05f, popupDynamicRefreshInterval))
        {
            return;
        }

        popupDynamicRefreshElapsed = 0f;
        float kelvin = temperatureField.GetCellTemperatureKelvin(selectedCellIndex);
        float insolation = temperatureField.GetCellInsolationCosine(selectedCellIndex);
        temperatureField.GetNeighborTemperatureStats(selectedCellIndex, out float neighborMin, out float neighborMean, out float neighborMax);
        TemperatureDisplayUnit displayUnit = temperatureDisplayAuthority != null ? temperatureDisplayAuthority.CurrentTemperatureDisplayUnit : TemperatureDisplayUnit.Kelvin;
        string temperatureText = ReplicatorManager.FormatTemperature(kelvin, displayUnit);
        string illumination = insolation > 0f ? "Day" : "Night";
        string compactLayers = BuildDynamicLayerTemperatureText(false);
        string detailedLayers = BuildDynamicLayerTemperatureText(true);
        string compactDynamic = $"\nSurface temperature: {temperatureText}\nIllumination / insolation: {illumination} / {insolation:F4}";
        string detailedDynamic = $"\n\nSurface Temperature\nTemperature: {temperatureText}\nInsolation cosine: {insolation:F4}\nIllumination: {illumination}\nTarget equilibrium: {ReplicatorManager.FormatTemperature(temperatureField.GetCellTargetTemperatureKelvin(selectedCellIndex), displayUnit)}\nResponse multiplier: {temperatureField.GetCellEffectiveThermalResponseMultiplier(selectedCellIndex):F3}\nThermal category: {temperatureField.GetCellThermalCategory(selectedCellIndex)}\nNeighbor min/mean/max: {ReplicatorManager.FormatTemperature(neighborMin, displayUnit)} / {ReplicatorManager.FormatTemperature(neighborMean, displayUnit)} / {ReplicatorManager.FormatTemperature(neighborMax, displayUnit)}";
        selectedCompactPopup = selectedCompactStaticHeader + compactDynamic + selectedCompactStaticOcean + compactLayers;
        selectedDetailedPopup = selectedDetailedStaticPopup + detailedDynamic + detailedLayers;
        }
    }

    private string BuildDynamicLayerTemperatureText(bool detailed)
    {
        GeodesicOceanLayerGrid grid = oceanLayerDomain != null ? oceanLayerDomain.Grid : null;
        if (grid == null || oceanTemperatureField == null || !oceanTemperatureField.IsInitialized || selectedCellIndex < 0 || selectedCellIndex >= grid.CellCount || grid.ActiveLayerCountByCell[selectedCellIndex] == 0) return string.Empty;
        var text = new StringBuilder(detailed ? 384 : 256);
        text.Append(detailed ? "\n\nOcean Layer Temperatures" : "\n\nLayer temperatures");
        int count = grid.ActiveLayerCountByCell[selectedCellIndex];
        for (int layer = 0; layer < count; layer++)
        {
            int node = grid.GetNodeIndex(selectedCellIndex, layer);
            float kelvin = oceanTemperatureField.GetLayerTemperatureKelvin(selectedCellIndex, layer);
            float depth = grid.OceanSurfaceRadius - grid.LayerCenterRadius[node];
            TemperatureDisplayUnit displayUnit = temperatureDisplayAuthority != null ? temperatureDisplayAuthority.CurrentTemperatureDisplayUnit : TemperatureDisplayUnit.Kelvin;
            if (detailed) text.Append("\nL").Append(layer).Append(": ").Append(ReplicatorManager.FormatTemperature(kelvin, displayUnit)).Append(" | capacity ").Append(oceanTemperatureField.GetLayerHeatCapacity(selectedCellIndex, layer).ToString("G6")).Append(" | authority ").Append(layer == 0 ? "SurfaceField" : "SubsurfaceField");
            else text.Append("\nL").Append(layer).Append(": ").Append(ReplicatorManager.FormatTemperature(kelvin, displayUnit)).Append(" | depth ").Append(depth.ToString("F3")).Append(" | thickness ").Append(grid.LayerThickness[node].ToString("F3"));
        }
        return text.ToString();
    }

    private string GetResourceText(int cellIndex, int layerIndex, GeodesicOceanResource resource)
    {
        return oceanResourceField != null && oceanResourceField.TryGetConcentration(cellIndex, layerIndex, resource, out float concentration)
            ? concentration.ToString("G4")
            : "--";
    }

    private static int GetHorizontalLayerDegree(GeodesicOceanLayerGrid grid, int nodeIndex)
    {
        int degree = 0;
        for (int link = 0; link < grid.HorizontalLinkCount; link++)
        {
            if (grid.HorizontalNodeA[link] == nodeIndex || grid.HorizontalNodeB[link] == nodeIndex) degree++;
        }
        return degree;
    }

    private static int GetVerticalLayerDegree(GeodesicOceanLayerGrid grid, int nodeIndex)
    {
        int degree = 0;
        for (int link = 0; link < grid.VerticalLinkCount; link++)
        {
            if (grid.VerticalUpperNode[link] == nodeIndex || grid.VerticalLowerNode[link] == nodeIndex) degree++;
        }
        return degree;
    }

    private void OnGUI()
    {
        if (!showSelectionPopup || selectedCellIndex < 0)
        {
            return;
        }

        EnsurePopupRect(false);
        EnsurePopupStyles();

        popupRect = GUI.Window(
            GetInstanceID(),
            popupRect,
            DrawSelectionPopup,
            GUIContent.none,
            popupWindowStyle);
        popupRect = ClampPopupRect(popupRect);
    }

    private void DrawSelectionPopup(int windowId)
    {
        GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
        DrawPopupHeader();

        popupScrollPosition = GUILayout.BeginScrollView(
            popupScrollPosition,
            false,
            true,
            GUIStyle.none,
            GUI.skin.verticalScrollbar,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));
        DrawDiagnostics();
        GUILayout.EndScrollView();

        DrawPopupFooter();
        GUILayout.EndVertical();

        float dragHeight = Mathf.Clamp(36f * GetUiScale(), 30f, 48f);
        GUI.DragWindow(new Rect(0f, 0f, popupRect.width, dragHeight));
    }

    private void DrawPopupHeader()
    {
        string type = selectedIsPentagon ? "Pentagon" : "Hexagon";
        string classification = planetGenerator != null && planetGenerator.IsGeodesicCellOcean(selectedCellIndex) ? "Ocean" : "Land";
        GUILayout.Label("Geodesic Cell Selection", popupHeaderStyle);
        GUILayout.Label($"Cell {selectedCellIndex} — {classification} — {type}", popupSummaryStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(!showDetailedDebug, "Compact", popupButtonStyle) && showDetailedDebug)
        {
            showDetailedDebug = false;
            popupScrollPosition = Vector2.zero;
        }
        if (GUILayout.Toggle(showDetailedDebug, "Detailed Debug", popupButtonStyle) && !showDetailedDebug)
        {
            showDetailedDebug = true;
            popupScrollPosition = Vector2.zero;
        }
        GUILayout.EndHorizontal();
    }

    private void DrawDiagnostics()
    {
        GUILayout.Label(showDetailedDebug ? selectedDetailedPopup : selectedCompactPopup, popupLabelStyle);
    }

    private void DrawPopupFooter()
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", popupButtonStyle, GUILayout.Width(Mathf.Clamp(90f * GetUiScale(), 78f, 118f))))
        {
            ClearSelection();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawSection(string title)
    {
        GUILayout.Space(Mathf.Clamp(6f * GetUiScale(), 4f, 9f));
        GUILayout.Label(title, popupSectionStyle);
    }

    private void DrawLine(string label, string value)
    {
        GUILayout.Label($"{label}: {value}", popupLabelStyle);
    }

    private string BuildNeighborText()
    {
        neighborBuilder.Clear();
        for (int i = 0; i < selectedNeighborIndices.Length; i++)
        {
            if (i > 0)
            {
                neighborBuilder.Append(", ");
            }

            neighborBuilder.Append(selectedNeighborIndices[i]);
        }

        return neighborBuilder.ToString();
    }

    private void EnsurePopupRect(bool selectedNewCell)
    {
        int screenWidth = Mathf.Max(1, Screen.width);
        int screenHeight = Mathf.Max(1, Screen.height);
        bool resolutionChanged = screenWidth != lastScreenWidth || screenHeight != lastScreenHeight;
        lastScreenWidth = screenWidth;
        lastScreenHeight = screenHeight;

        Vector2 size = CalculatePopupSize(screenWidth, screenHeight);
        if (!popupPositionInitialized || !rememberPopupPosition)
        {
            popupRect.x = popupScreenMargin;
            popupRect.y = popupScreenMargin;
            popupPositionInitialized = true;
        }

        if (resolutionChanged || selectedNewCell || !Mathf.Approximately(popupRect.width, size.x) || !Mathf.Approximately(popupRect.height, size.y))
        {
            popupRect.width = size.x;
            popupRect.height = size.y;
        }

        popupRect = ClampPopupRect(popupRect);
    }

    private Vector2 CalculatePopupSize(int screenWidth, int screenHeight)
    {
        float maxWidth = Mathf.Max(1f, screenWidth * Mathf.Clamp01(popupMaximumWidthFraction));
        float maxHeight = Mathf.Max(1f, screenHeight * Mathf.Clamp01(popupMaximumHeightFraction));
        float width = Mathf.Clamp(screenWidth * Mathf.Clamp01(popupWidthFraction), minimumPopupWidth, maxWidth);
        float height = Mathf.Clamp(screenHeight * Mathf.Clamp01(popupHeightFraction), minimumPopupHeight, maxHeight);
        float availableWidth = Mathf.Max(120f, screenWidth - (popupScreenMargin * 2f));
        float availableHeight = Mathf.Max(120f, screenHeight - (popupScreenMargin * 2f));
        return new Vector2(Mathf.Min(width, availableWidth), Mathf.Min(height, availableHeight));
    }

    private Rect ClampPopupRect(Rect rect)
    {
        float margin = Mathf.Max(0f, popupScreenMargin);
        float maxWidth = Mathf.Max(120f, Screen.width - (margin * 2f));
        float maxHeight = Mathf.Max(120f, Screen.height - (margin * 2f));
        rect.width = Mathf.Min(rect.width, maxWidth);
        rect.height = Mathf.Min(rect.height, maxHeight);
        rect.x = Mathf.Clamp(rect.x, margin, Mathf.Max(margin, Screen.width - margin - rect.width));
        rect.y = Mathf.Clamp(rect.y, margin, Mathf.Max(margin, Screen.height - margin - rect.height));
        return rect;
    }

    private bool IsScreenPositionInsidePopup(Vector2 screenPosition)
    {
        if (!showSelectionPopup || selectedCellIndex < 0)
        {
            return false;
        }

        Vector2 imguiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return popupRect.Contains(imguiPosition);
    }

    private void EnsurePopupStyles()
    {
        float uiScale = GetUiScale();
        if (popupLabelStyle != null && Mathf.Approximately(cachedUiScale, uiScale))
        {
            return;
        }

        cachedUiScale = uiScale;
        int labelSize = Mathf.RoundToInt(13f * uiScale);
        int headerSize = Mathf.RoundToInt(16f * uiScale);
        int sectionSize = Mathf.RoundToInt(14f * uiScale);
        popupWindowStyle = new GUIStyle(GUI.skin.window)
        {
            padding = new RectOffset(Mathf.RoundToInt(10f * uiScale), Mathf.RoundToInt(10f * uiScale), Mathf.RoundToInt(8f * uiScale), Mathf.RoundToInt(8f * uiScale))
        };
        popupLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = labelSize, wordWrap = true };
        popupHeaderStyle = new GUIStyle(GUI.skin.label) { fontSize = headerSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        popupSectionStyle = new GUIStyle(GUI.skin.label) { fontSize = sectionSize, fontStyle = FontStyle.Bold };
        popupSummaryStyle = new GUIStyle(GUI.skin.label) { fontSize = labelSize, fontStyle = FontStyle.Italic, wordWrap = true };
        popupButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = labelSize };
    }

    private float GetUiScale()
    {
        float resolutionScale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        return Mathf.Clamp(resolutionScale * popupFontScale, 0.85f, 1.25f);
    }
}
