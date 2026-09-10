using UnityEngine;

/// <summary>Static outlets selected from real members of authoritative vent systems.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicVentVisualizer : MonoBehaviour
{
    [SerializeField] private bool showVentMarkers = true;
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
        int ventVisualCellMismatch = 0, ventVisualRadiusMismatch = 0; float maxVentVisualAngularError = 0f;
        for (int i = 0; i < temperatureField.OutletCount; i++)
        {
            if (!temperatureField.TryGetOutlet(i, out GeodesicVentOutlet outlet)) continue;
            GameObject marker = new GameObject($"{outlet.Habitat} Vent Outlet {i + 1}"); marker.layer = gameObject.layer; marker.transform.SetParent(markerRoot.transform, false);
            Vector3 normal = outlet.PlanetLocalNormal; marker.transform.localPosition = outlet.PlanetLocalPosition + normal * seafloorOffset;
            int markerCell = FindNearestCell(marker.transform.localPosition.normalized, generator.GeodesicTopology.CellDirections);
            if (markerCell != outlet.CellIndex) ventVisualCellMismatch++;
            maxVentVisualAngularError = Mathf.Max(maxVentVisualAngularError,
                Vector3.Angle(generator.GeodesicTopology.CellDirections[outlet.CellIndex], marker.transform.localPosition));
            if (Mathf.Abs(outlet.PlanetLocalPosition.magnitude - generator.GetGeodesicCellSeafloorRadius(outlet.CellIndex)) > 0.01f)
                ventVisualRadiusMismatch++;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up); if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.Cross(normal, Vector3.right);
            marker.transform.localRotation = Quaternion.LookRotation(normal, Vector3.Cross(tangent.normalized, normal));
            // The disc has unit diameter. Its edge is therefore exactly the thermal hot-core edge.
            marker.transform.localScale = Vector3.one * (outlet.HotCoreRadius * 2f);
            marker.AddComponent<MeshFilter>().sharedMesh = sharedMarkerMesh; marker.AddComponent<MeshRenderer>().sharedMaterial = sharedMarkerMaterial; markerCount++;
        }
        Debug.Log($"[GeodesicVentVisualizer] outlets={markerCount}, authority=GeodesicOceanResourceField.SourceNode, ventVisualCellMismatch={ventVisualCellMismatch}, maxVentVisualAngularError={maxVentVisualAngularError:F6}deg, ventVisualRadiusMismatch={ventVisualRadiusMismatch}, anchors=source-cell direction at completed visible seafloor radius", this);
    }

    private static int FindNearestCell(Vector3 direction, Vector3[] directions)
    {
        int nearest = -1; float best = -2f;
        for (int i = 0; i < directions.Length; i++) { float dot = Vector3.Dot(direction, directions[i]); if (dot > best) { best = dot; nearest = i; } }
        return nearest;
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
