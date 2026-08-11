using UnityEngine;

/// <summary>One static marker per authoritative vent system owned by GeodesicOceanResourceField.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicVentVisualizer : MonoBehaviour
{
    [SerializeField] private bool showVentMarkers = true;
    [SerializeField, Min(0.001f)] private float markerScale = 0.028f;
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

    public void Initialize(GeodesicOceanResourceField resourceField, PlanetGenerator generator)
    {
        ClearMarkers();
        if (resourceField == null || !resourceField.IsInitialized || generator == null) return;

        markerRoot = new GameObject("Geodesic Vent Markers");
        markerRoot.transform.SetParent(transform, false);
        markerRoot.layer = gameObject.layer;
        markerRoot.SetActive(showVentMarkers);
        sharedMarkerMesh = BuildSharedDiscMesh();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader != null)
        {
            sharedMarkerMaterial = new Material(shader) { name = "Geodesic Vent Marker (Runtime)", color = markerColor };
            if (sharedMarkerMaterial.HasProperty("_BaseColor")) sharedMarkerMaterial.SetColor("_BaseColor", markerColor);
            Color emission = markerColor * emissionIntensity;
            sharedMarkerMaterial.EnableKeyword("_EMISSION");
            if (sharedMarkerMaterial.HasProperty("_EmissionColor")) sharedMarkerMaterial.SetColor("_EmissionColor", emission);
        }

        int vents = resourceField.VentCount;
        for (int i = 0; i < vents; i++)
        {
            if (!resourceField.TryGetVentSystem(i, out GeodesicVentSystem system) ||
                !resourceField.TryGetVent(i, out int cell, out _, out _) ||
                !generator.TryGetVisibleGeodesicSeafloorWorldAnchor(cell, out Vector3 seafloorPosition, out Vector3 seafloorNormal)) continue;
            GameObject marker = new GameObject($"{system.Habitat} Vent System {i}");
            marker.layer = gameObject.layer;
            marker.transform.SetParent(markerRoot.transform, true);
            marker.transform.position = seafloorPosition + seafloorNormal * seafloorOffset;
            Vector3 tangent = Vector3.Cross(seafloorNormal, Vector3.up);
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.Cross(seafloorNormal, Vector3.right);
            marker.transform.rotation = Quaternion.LookRotation(seafloorNormal, Vector3.Cross(tangent.normalized, seafloorNormal));
            // Area-like system weight maps to diameter sublinearly; it never affects production.
            float weightScale = 0.65f + 2.25f * Mathf.Sqrt(Mathf.Max(0f, system.NormalizedHabitatWeight));
            marker.transform.localScale = Vector3.one * markerScale * weightScale;
            marker.AddComponent<MeshFilter>().sharedMesh = sharedMarkerMesh;
            marker.AddComponent<MeshRenderer>().sharedMaterial = sharedMarkerMaterial;
            markerCount++;
        }

        Debug.Log($"[GeodesicVentVisualizer] vents={vents}, markers={markerCount}, source=authoritative Geodesic vent records", this);
        if (markerCount != vents) Debug.LogError($"[GeodesicVentVisualizer] Marker invariant failed: vents={vents}, markers={markerCount}.", this);
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
