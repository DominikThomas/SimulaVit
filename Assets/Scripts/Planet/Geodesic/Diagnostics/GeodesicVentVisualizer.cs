using UnityEngine;

/// <summary>Static outlets selected from real members of authoritative vent systems.</summary>
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
        float maximumRawStrength = 0f;
        for (int i = 0; i < vents; i++) if (resourceField.TryGetVentSystem(i, out GeodesicVentSystem measured)) maximumRawStrength = Mathf.Max(maximumRawStrength, measured.RawStrengthSum);
        int minimumOutlets = int.MaxValue, maximumOutlets = 0;
        float minimumScale = float.PositiveInfinity, maximumScale = 0f, scaleSum = 0f;
        for (int i = 0; i < vents; i++)
        {
            if (!resourceField.TryGetVentSystem(i, out GeodesicVentSystem system)) continue;
            int outletTarget = Mathf.Clamp(1 + Mathf.FloorToInt(Mathf.Sqrt(Mathf.Max(0, system.MemberCount - 1))), 1, 5);
            int outlets = 0;
            for (int member = 0; member < system.MemberCount && outlets < outletTarget; member++)
            {
                int cell = system.Members[member].CellIndex;
                if (!generator.TryGetVisibleGeodesicSeafloorWorldAnchor(cell, out Vector3 seafloorPosition, out Vector3 seafloorNormal)) continue;
                GameObject marker = new GameObject($"{system.Habitat} Vent System {i} Outlet {outlets + 1}");
                marker.layer = gameObject.layer;
                marker.transform.SetParent(markerRoot.transform, true);
                marker.transform.position = seafloorPosition + seafloorNormal * seafloorOffset;
                Vector3 tangent = Vector3.Cross(seafloorNormal, Vector3.up);
                if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.Cross(seafloorNormal, Vector3.right);
                marker.transform.rotation = Quaternion.LookRotation(seafloorNormal, Vector3.Cross(tangent.normalized, seafloorNormal));
                float relativeSystemStrength = maximumRawStrength > 0f ? system.RawStrengthSum / maximumRawStrength : 0f;
                float relativeOutletStrength = system.RawStrengthMax > 0f ? system.Members[member].RawStrength / system.RawStrengthMax : 0f;
                float weightScale = Mathf.Lerp(0.55f, 1.9f, Mathf.Log10(1f + 9f * relativeSystemStrength)) * Mathf.Lerp(0.65f, 1f, Mathf.Sqrt(relativeOutletStrength));
                marker.transform.localScale = Vector3.one * markerScale * weightScale;
                marker.AddComponent<MeshFilter>().sharedMesh = sharedMarkerMesh;
                marker.AddComponent<MeshRenderer>().sharedMaterial = sharedMarkerMaterial;
                float absoluteScale = markerScale * weightScale;
                minimumScale = Mathf.Min(minimumScale, absoluteScale); maximumScale = Mathf.Max(maximumScale, absoluteScale); scaleSum += absoluteScale;
                markerCount++; outlets++;
            }
            minimumOutlets = Mathf.Min(minimumOutlets, outlets); maximumOutlets = Mathf.Max(maximumOutlets, outlets);
        }

        Debug.Log($"[GeodesicVentVisualizer] systems={vents}, visibleOutlets={markerCount}, outletsMinMeanMax={(vents > 0 ? minimumOutlets : 0)}/{(vents > 0 ? markerCount / (float)vents : 0f):F2}/{maximumOutlets}, markerScaleMinMeanMax={(markerCount > 0 ? minimumScale : 0f):F4}/{(markerCount > 0 ? scaleSum / markerCount : 0f):F4}/{maximumScale:F4}, sizeSource=relative raw cluster/member strength, anchors=completed visible terrain", this);
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
