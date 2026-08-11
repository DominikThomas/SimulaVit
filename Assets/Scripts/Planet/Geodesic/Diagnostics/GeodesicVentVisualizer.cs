using UnityEngine;

/// <summary>Static outlets selected from real members of authoritative vent systems.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicVentVisualizer : MonoBehaviour
{
    [SerializeField] private bool showVentMarkers = true;
    [SerializeField, Min(0.001f), Tooltip("Base diameter scale shared by every visible outlet.")] private float markerScale = 0.05f;
    [SerializeField, Min(0.1f), Tooltip("Multiplier used by the weakest authoritative system.")] private float minimumMarkerScaleMultiplier = 0.8f;
    [SerializeField, Min(0.1f), Tooltip("Multiplier used by the strongest authoritative system.")] private float maximumMarkerScaleMultiplier = 3.2f;
    [SerializeField, Range(0.1f, 2f), Tooltip("Exponent controlling visual response to relative raw system strength. Lower values emphasize differences among weaker systems.")] private float markerStrengthResponse = 0.65f;
    [SerializeField, Range(0.1f, 20f), Tooltip("Maximum angular distance from the representative cell for real member outlets. This is visual-only and independent of authoritative clustering.")] private float visualOutletRadiusDegrees = 3.5f;
    [SerializeField, Range(1, 8), Tooltip("Maximum visual-only outlets rendered for one authoritative system.")] private int maxVisibleOutletsPerSystem = 5;
    [SerializeField, Min(0.00001f)] private float seafloorOffset = 0.0015f;
    [SerializeField] private Color markerColor = new Color(1f, 0.12f, 0.015f, 1f);
    [SerializeField, Min(1f)] private float emissionIntensity = 4f;
    [SerializeField] private int markerCount;

    private GameObject markerRoot;
    private Material sharedMarkerMaterial;
    private Mesh sharedMarkerMesh;

    public bool ShowVentMarkers
    {
        get => showVentMarkers;
        set { showVentMarkers = value; if (markerRoot != null) markerRoot.SetActive(value); }
    }

    public int MarkerCount => markerCount;

    public void Initialize(GeodesicExperiencedTemperatureField temperatureField, PlanetGenerator generator)
    {
        ClearMarkers();
        if (temperatureField == null || !temperatureField.IsInitialized || generator == null) return;
        markerRoot = new GameObject("Geodesic Vent Markers"); markerRoot.transform.SetParent(transform, false); markerRoot.layer = gameObject.layer; markerRoot.SetActive(showVentMarkers);
        sharedMarkerMesh = BuildSharedDiscMesh(); Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader != null) { sharedMarkerMaterial = new Material(shader) { name = "Geodesic Vent Marker (Runtime)", color = markerColor }; if (sharedMarkerMaterial.HasProperty("_BaseColor")) sharedMarkerMaterial.SetColor("_BaseColor", markerColor); sharedMarkerMaterial.EnableKeyword("_EMISSION"); if (sharedMarkerMaterial.HasProperty("_EmissionColor")) sharedMarkerMaterial.SetColor("_EmissionColor", markerColor * emissionIntensity); }
        for (int i = 0; i < temperatureField.OutletCount; i++)
        {
            if (!temperatureField.TryGetOutlet(i, out GeodesicVentOutlet outlet)) continue;
            GameObject marker = new GameObject($"{outlet.Habitat} Vent Outlet {i + 1}"); marker.layer = gameObject.layer; marker.transform.SetParent(markerRoot.transform, false);
            Vector3 normal = outlet.PlanetLocalNormal; marker.transform.localPosition = outlet.PlanetLocalPosition + normal * seafloorOffset;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up); if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.Cross(normal, Vector3.right);
            marker.transform.localRotation = Quaternion.LookRotation(normal, Vector3.Cross(tangent.normalized, normal));
            marker.transform.localScale = Vector3.one * markerScale * Mathf.Lerp(minimumMarkerScaleMultiplier, maximumMarkerScaleMultiplier, Mathf.Pow(outlet.Strength01, markerStrengthResponse));
            marker.AddComponent<MeshFilter>().sharedMesh = sharedMarkerMesh; marker.AddComponent<MeshRenderer>().sharedMaterial = sharedMarkerMaterial; markerCount++;
        }
        Debug.Log($"[GeodesicVentVisualizer] outlets={markerCount}, authority=GeodesicExperiencedTemperatureField immutable outlet records, anchors=completed visible terrain", this);
    }

    [ContextMenu("Toggle Geodesic Vent Markers")]
    private void ToggleMarkers() => ShowVentMarkers = !ShowVentMarkers;

    private static Mesh BuildSharedDiscMesh()
    {
        const int segments = 12;
        var vertices = new Vector3[segments + 1];
        var triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, Mathf.Sin(angle) * 0.5f, 0f);
            int triangle = i * 3;
            triangles[triangle] = 0;
            triangles[triangle + 1] = i + 1;
            triangles[triangle + 2] = (i + 1) % segments + 1;
        }
        var mesh = new Mesh { name = "Geodesic Vent Disc" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public void ClearMarkers()
    {
        markerCount = 0;
        if (markerRoot != null) Destroy(markerRoot);
        markerRoot = null;
        if (sharedMarkerMaterial != null) Destroy(sharedMarkerMaterial);
        sharedMarkerMaterial = null;
        if (sharedMarkerMesh != null) Destroy(sharedMarkerMesh);
        sharedMarkerMesh = null;
    }

    private void OnDestroy() => ClearMarkers();
}

public enum GeodesicVentVisualArchetype { SingleDominant, DominantWithSatellites, SimilarOutlets }

/// <summary>Pure deterministic visual selection; it never mutates vent systems or simulation weights.</summary>
public static class GeodesicVentOutletSelector
{
    public static GeodesicVentVisualArchetype GetArchetype(int representativeCell)
    {
        uint value = unchecked((uint)representativeCell) ^ 0xB5297A4Du;
        value ^= value >> 16; value *= 0x68E31DA4u; value ^= value >> 15;
        return (GeodesicVentVisualArchetype)(value % 3u);
    }

    public static int SelectLocalMembers(GeodesicVentSystem system, Vector3[] cellDirections, float radiusDegrees, int maximumOutlets, int[] destination)
    {
        if (system == null || system.Members == null || cellDirections == null || destination == null || maximumOutlets <= 0) return 0;
        int capacity = Mathf.Min(maximumOutlets, destination.Length);
        float minimumDot = Mathf.Cos(Mathf.Clamp(radiusDegrees, 0.1f, 180f) * Mathf.Deg2Rad);
        Vector3 representativeDirection = cellDirections[system.RepresentativeCell];
        int count = 0;
        while (count < capacity)
        {
            int best = -1; float bestDot = -2f; float bestStrength = -1f; int bestCell = int.MaxValue;
            for (int member = 0; member < system.Members.Length; member++)
            {
                int cell = system.Members[member].CellIndex;
                float dot = Vector3.Dot(representativeDirection, cellDirections[cell]);
                if (dot < minimumDot || AlreadySelected(destination, count, member)) continue;
                float strength = system.Members[member].RawStrength;
                if (dot > bestDot + 1e-7f || (Mathf.Abs(dot - bestDot) <= 1e-7f && (strength > bestStrength || (Mathf.Approximately(strength, bestStrength) && cell < bestCell))))
                { best = member; bestDot = dot; bestStrength = strength; bestCell = cell; }
            }
            if (best < 0) break;
            destination[count++] = best;
        }
        return count;
    }

    public static float GetOutletScale(GeodesicVentVisualArchetype archetype, int outletIndex, float relativeMemberStrength)
    {
        if (outletIndex == 0) return archetype == GeodesicVentVisualArchetype.SimilarOutlets ? 1f : 1.2f;
        float memberScale = Mathf.Lerp(0.72f, 1f, Mathf.Sqrt(Mathf.Clamp01(relativeMemberStrength)));
        return archetype == GeodesicVentVisualArchetype.DominantWithSatellites ? memberScale * 0.72f : memberScale;
    }

    private static bool AlreadySelected(int[] selected, int count, int candidate)
    { for (int i = 0; i < count; i++) if (selected[i] == candidate) return true; return false; }
}
