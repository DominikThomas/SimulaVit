using UnityEngine;

/// <summary>Static debug markers for the authoritative vents owned by GeodesicOceanResourceField.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicVentVisualizer : MonoBehaviour
{
    [SerializeField] private bool showVentMarkers = true;
    [SerializeField, Min(0.001f)] private float markerScale = 0.018f;
    [SerializeField] private Color markerColor = new Color(1f, 0.24f, 0.04f, 1f);
    [SerializeField] private int markerCount;

    private GameObject markerRoot;
    private Material sharedMarkerMaterial;

    public bool ShowVentMarkers
    {
        get => showVentMarkers;
        set { showVentMarkers = value; if (markerRoot != null) markerRoot.SetActive(value); }
    }

    public int MarkerCount => markerCount;

    public void Initialize(GeodesicGridTopology topology, GeodesicOceanResourceField resourceField)
    {
        ClearMarkers();
        if (topology == null || resourceField == null || !resourceField.IsInitialized) return;

        markerRoot = new GameObject("Geodesic Vent Markers");
        markerRoot.transform.SetParent(transform, false);
        markerRoot.layer = gameObject.layer;
        markerRoot.SetActive(showVentMarkers);
        Mesh sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader != null)
        {
            sharedMarkerMaterial = new Material(shader) { name = "Geodesic Vent Marker (Runtime)", color = markerColor };
            if (sharedMarkerMaterial.HasProperty("_BaseColor")) sharedMarkerMaterial.SetColor("_BaseColor", markerColor);
        }

        GeodesicOceanLayerGrid grid = resourceField.SourceGrid;
        int vents = resourceField.VentCount;
        for (int i = 0; i < vents; i++)
        {
            if (!resourceField.TryGetVent(i, out int cell, out int bottomLayer, out _)) continue;
            int node = grid.GetNodeIndex(cell, bottomLayer);
            GameObject marker = new GameObject($"Vent {i}");
            marker.layer = gameObject.layer;
            marker.transform.SetParent(markerRoot.transform, false);
            marker.transform.localPosition = topology.CellDirections[cell] * grid.LayerCenterRadius[node];
            marker.transform.localScale = Vector3.one * markerScale;
            marker.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
            marker.AddComponent<MeshRenderer>().sharedMaterial = sharedMarkerMaterial;
            markerCount++;
        }

        Debug.Log($"[GeodesicVentVisualizer] vents={vents}, markers={markerCount}, source=authoritative Geodesic vent records", this);
        if (markerCount != vents) Debug.LogError($"[GeodesicVentVisualizer] Marker invariant failed: vents={vents}, markers={markerCount}.", this);
    }

    [ContextMenu("Toggle Geodesic Vent Markers")]
    private void ToggleMarkers() => ShowVentMarkers = !ShowVentMarkers;

    public void ClearMarkers()
    {
        markerCount = 0;
        if (markerRoot != null) Destroy(markerRoot);
        markerRoot = null;
        if (sharedMarkerMaterial != null) Destroy(sharedMarkerMaterial);
        sharedMarkerMaterial = null;
    }

    private void OnDestroy() => ClearMarkers();
}
