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
        popupScrollPosition = Vector2.zero;
    }

    private void Update()
    {
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
                terrainDiagnostics = $", classification={(planetGenerator.IsGeodesicCellOcean(selectedCellIndex) ? "ocean" : "land")}, coastline={planetGenerator.IsGeodesicCellCoastline(selectedCellIndex)}, terrainHeight={terrainSample.HeightOffset:F6}, mountainMask={terrainSample.MountainMask:F4}, ridge={terrainSample.RidgeValue:F4}, continent={terrainSample.ContinentValue:F4}, rawTerrainRadius={planetGenerator.GetGeodesicCellRawTerrainRadius(selectedCellIndex):F6}, finalSeafloorRadius={planetGenerator.GetGeodesicCellSeafloorRadius(selectedCellIndex):F6}, finalSurfaceRadius={planetGenerator.GetSurfaceRadiusAtDirection(centreDirection):F6}, seaLevelRadius={planetGenerator.GeodesicSeaLevelRadius:F6}, baseDepth={planetGenerator.GetGeodesicCellBaseWaterDepth(selectedCellIndex):F6}, finalBathymetryDepth={planetGenerator.GetGeodesicCellWaterDepth(selectedCellIndex):F6}, distanceToShore={(planetGenerator.IsGeodesicOceanWorldActive ? "N/A — OceanWorld" : planetGenerator.GetGeodesicCellDistanceToShore(selectedCellIndex).ToString("F6"))}, depth01={planetGenerator.GetGeodesicCellNormalizedDepth(selectedCellIndex):F3}, bathymetryRegion={planetGenerator.GetGeodesicCellBathymetryRegion(selectedCellIndex)}, basinNoiseContribution={planetGenerator.GetGeodesicCellBasinNoiseContribution(selectedCellIndex):F4}, oceanNeighborCount={planetGenerator.GetGeodesicOceanNeighborCount(selectedCellIndex)}, continentalInfluence01={planetGenerator.GetGeodesicCellContinentalInfluence01(selectedCellIndex):F3}, coastType={planetGenerator.GetGeodesicCellCoastType(selectedCellIndex)}, landComponentId={(planetGenerator.GetGeodesicCellLandComponentId(selectedCellIndex) >= 0 ? planetGenerator.GetGeodesicCellLandComponentId(selectedCellIndex).ToString() : "None")}, shelfProfile={planetGenerator.GetGeodesicCellShelfProfileType(selectedCellIndex)}, continentalShelfInfluence={planetGenerator.GetGeodesicCellContinentalShelfInfluence01(selectedCellIndex):F3}, oceanicIslandInfluence={planetGenerator.GetGeodesicCellOceanicIslandShelfInfluence01(selectedCellIndex):F3}, shelfWidthMul={planetGenerator.GetGeodesicCellLocalShelfWidthMultiplier(selectedCellIndex):F3}, continentalProfileWidthDeg={planetGenerator.GetGeodesicCellContinentalProfileShelfWidthDegrees(selectedCellIndex):F3}, finalShelfWidthDeg={planetGenerator.GetGeodesicCellFinalShelfWidthDegrees(selectedCellIndex):F3}, localShelfDepth={planetGenerator.GetGeodesicCellLocalShelfDepth(selectedCellIndex):F5}, approxCellSpacingDeg={planetGenerator.GetGeodesicCellApproxCellSpacingDegrees(selectedCellIndex):F3}, oceanicRelief(ridge/plateau/seamount/total)={planetGenerator.GetGeodesicCellRidgeContribution(selectedCellIndex):F5}/{planetGenerator.GetGeodesicCellPlateauContribution(selectedCellIndex):F5}/{planetGenerator.GetGeodesicCellSeamountContribution(selectedCellIndex):F5}/{planetGenerator.GetGeodesicCellTotalOceanicReliefContribution(selectedCellIndex):F5}";
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

        popupScrollPosition = Vector2.zero;
        EnsurePopupRect(true);
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
    }

    private void DrawDiagnostics()
    {
        DrawSection("Cell");
        DrawLine("Index", selectedCellIndex.ToString());
        DrawLine("Type", selectedIsPentagon ? "Pentagon" : "Hexagon");
        DrawLine("Neighbors", selectedNeighborCount.ToString());
        DrawLine("Unit area", selectedUnitArea.ToString("F8"));
        if (planetGenerator != null)
        {
            DrawLine("Physical area", (selectedUnitArea * planetGenerator.BasePlanetRadius * planetGenerator.BasePlanetRadius).ToString("F8"));
        }
        DrawLine("Neighbor IDs", BuildNeighborText());

        if (planetGenerator == null)
        {
            return;
        }

        float height = planetGenerator.GetCellTerrainHeight(selectedCellIndex);
        float surfaceRadius = planetGenerator.GetCellSurfaceRadius(selectedCellIndex);
        float rawRadius = planetGenerator.GetGeodesicCellRawTerrainRadius(selectedCellIndex);
        float normalizedHeight = planetGenerator.GetCellNormalizedTerrainHeight(selectedCellIndex);
        bool ocean = planetGenerator.IsGeodesicCellOcean(selectedCellIndex);
        bool coastline = planetGenerator.IsGeodesicCellCoastline(selectedCellIndex);
        PlanetTerrainSample sample = planetGenerator.EvaluateGeodesicTerrainSample(topology.CellDirections[selectedCellIndex]);
        float baseDepth = planetGenerator.GetGeodesicCellBaseWaterDepth(selectedCellIndex);
        float finalDepth = planetGenerator.GetGeodesicCellWaterDepth(selectedCellIndex);
        float bathymetryAddedDepth = Mathf.Max(0f, finalDepth - baseDepth);
        float radialDisplacement = surfaceRadius - rawRadius;
        bool bathymetryModified = !Mathf.Approximately(finalDepth, baseDepth) || !Mathf.Approximately(radialDisplacement, 0f);

        DrawSection("Terrain");
        DrawLine("Height", height.ToString("F5"));
        DrawLine("Normalized", normalizedHeight.ToString("F3"));
        DrawLine("Raw radius", rawRadius.ToString("F5"));
        DrawLine("Final radius", surfaceRadius.ToString("F5"));
        DrawLine("Continent", sample.ContinentValue.ToString("F3"));
        DrawLine("Mountain mask", sample.MountainMask.ToString("F3"));
        DrawLine("Ridge", sample.RidgeValue.ToString("F3"));

        DrawSection("Ocean");
        DrawLine("Class", ocean ? "Ocean" : "Land");
        DrawLine("Coastline", coastline.ToString());
        DrawLine("Sea level", planetGenerator.GeodesicSeaLevelRadius.ToString("F5"));
        DrawLine("Ocean neighbors", planetGenerator.GetGeodesicOceanNeighborCount(selectedCellIndex).ToString());
        DrawLine("Base depth", baseDepth.ToString("F5"));
        DrawLine("Final depth", finalDepth.ToString("F5"));
        DrawLine("Depth 01", planetGenerator.GetGeodesicCellNormalizedDepth(selectedCellIndex).ToString("F3"));

        DrawSection("Bathymetry");
        DrawLine("Enabled", (finalDepth > 0f || baseDepth > 0f || radialDisplacement < 0f).ToString());
        DrawLine("Region", planetGenerator.GetGeodesicCellBathymetryRegion(selectedCellIndex).ToString());
        DrawLine("Distance shore", planetGenerator.IsGeodesicOceanWorldActive ? "N/A — OceanWorld" : planetGenerator.GetGeodesicCellDistanceToShore(selectedCellIndex).ToString("F5"));
        DrawLine("Added depth", bathymetryAddedDepth.ToString("F5"));
        DrawLine("Radial disp.", radialDisplacement.ToString("F5"));
        DrawLine("Basin noise", planetGenerator.GetGeodesicCellBasinNoiseContribution(selectedCellIndex).ToString("F4"));
        DrawLine("Coast type", planetGenerator.GetGeodesicCellCoastType(selectedCellIndex));
        DrawLine("Shelf profile", planetGenerator.GetGeodesicCellShelfProfileType(selectedCellIndex));
        DrawLine("Land comp.", planetGenerator.GetGeodesicCellLandComponentId(selectedCellIndex).ToString());
        DrawLine("Continent 01", planetGenerator.GetGeodesicCellContinentalInfluence01(selectedCellIndex).ToString("F3"));
        DrawLine("Shelf infl.", planetGenerator.GetGeodesicCellContinentalShelfInfluence01(selectedCellIndex).ToString("F3"));
        DrawLine("Island infl.", planetGenerator.GetGeodesicCellOceanicIslandShelfInfluence01(selectedCellIndex).ToString("F3"));
        DrawLine("Shelf width x", planetGenerator.GetGeodesicCellLocalShelfWidthMultiplier(selectedCellIndex).ToString("F3"));
        DrawLine("Cont. width°", planetGenerator.GetGeodesicCellContinentalProfileShelfWidthDegrees(selectedCellIndex).ToString("F3"));
        DrawLine("Final width°", planetGenerator.GetGeodesicCellFinalShelfWidthDegrees(selectedCellIndex).ToString("F3"));
        DrawLine("Cell spacing°", planetGenerator.GetGeodesicCellApproxCellSpacingDegrees(selectedCellIndex).ToString("F3"));
        DrawLine("Shelf depth", planetGenerator.GetGeodesicCellLocalShelfDepth(selectedCellIndex).ToString("F5"));
        DrawLine("Ridge relief", planetGenerator.GetGeodesicCellRidgeContribution(selectedCellIndex).ToString("F5"));
        DrawLine("Plateau relief", planetGenerator.GetGeodesicCellPlateauContribution(selectedCellIndex).ToString("F5"));
        DrawLine("Seamount relief", planetGenerator.GetGeodesicCellSeamountContribution(selectedCellIndex).ToString("F5"));
        DrawLine("Ocean relief", planetGenerator.GetGeodesicCellTotalOceanicReliefContribution(selectedCellIndex).ToString("F5"));
        DrawLine("Modified", bathymetryModified.ToString());

        GeodesicSurfaceTemperatureField temperature = planetGenerator.GetComponent<GeodesicSurfaceTemperatureField>();
        if (temperature != null && temperature.IsInitialized)
        {
            float kelvin = temperature.GetCellTemperatureKelvin(selectedCellIndex);
            float insolation = temperature.GetCellInsolationCosine(selectedCellIndex);
            temperature.GetNeighborTemperatureStats(selectedCellIndex, out float neighborMin, out float neighborMean, out float neighborMax);
            DrawSection("Surface Temperature");
            DrawLine("Temperature", $"{kelvin:F2} K / {kelvin - 273.15f:F2} °C");
            DrawLine("Insolation cosine", insolation.ToString("F4"));
            DrawLine("Illumination", insolation > 0f ? "Day" : "Night");
            DrawLine("Target equilibrium", $"{temperature.GetCellTargetTemperatureKelvin(selectedCellIndex):F2} K");
            DrawLine("Response multiplier", temperature.GetCellEffectiveThermalResponseMultiplier(selectedCellIndex).ToString("F3"));
            DrawLine("Thermal category", temperature.GetCellThermalCategory(selectedCellIndex));
            DrawLine("Neighbor min/mean/max", $"{neighborMin:F2} / {neighborMean:F2} / {neighborMax:F2} K");
        }
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
