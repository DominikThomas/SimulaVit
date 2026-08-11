using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Visual-only dissolved Fe2+ tint for the geodesic ocean surface. Each render vertex samples
/// its authoritative GeodesicOceanResourceField column. Only the optically relevant L0 and L1
/// layers contribute, so deeper enrichment must propagate upward before it becomes visible.
/// It never reads PlanetResourceMap.
/// </summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanFe2Visual : MonoBehaviour
{
    [Header("Geodesic Dissolved Fe2+ Ocean Tint")]
    [SerializeField, Min(0f), Tooltip("Concentrations at or below this level have no Fe2+ tint.")]
    private float minimumVisualizedFe2 = 0f;
    [SerializeField, Min(0f), Tooltip("Concentrations at or above this level receive the maximum tint.")]
    private float maximumVisualizedFe2 = 8f;
    [SerializeField] private Color lowFe2Tint = new Color(0.20f, 0.36f, 0.44f, 1f);
    [SerializeField] private Color highFe2Tint = new Color(0.12156861f, 0.5803922f, 0.16663016f, 1f);
    [SerializeField, Range(0f, 1f)] private float intensityMultiplier = 0.42f;
    [SerializeField, Min(0.1f), Tooltip("Real-time seconds between visual refresh checks. Mesh colours only upload after an authoritative transport tick changes.")]
    private float refreshIntervalSeconds = 0.5f;
    [FormerlySerializedAs("deeperLayerWeightMultiplier")]
    [SerializeField, Range(0f, 1f), Tooltip("Visual weight of ocean layer L1. Layers L2-L4 do not contribute to dissolved Fe2+ ocean colour.")]
    private float layer1VisualWeight = 0.4f;

    private static readonly int LowTintId = Shader.PropertyToID("_Fe2VisualLowTint");
    private static readonly int HighTintId = Shader.PropertyToID("_Fe2VisualHighTint");
    private static readonly int IntensityId = Shader.PropertyToID("_Fe2VisualIntensity");
    private PlanetGenerator generator;
    private GeodesicOceanResourceField resourceField;
    private Mesh oceanMesh;
    private MeshRenderer oceanRenderer;
    private IcosphereDirectionMapping directionMapping;
    private Color[] vertexColours;
    private float nextRefreshTime;
    private long lastTransportTick = long.MinValue;

    public void Initialize(PlanetGenerator owner, GeodesicOceanResourceField field, Mesh mesh, MeshRenderer renderer, IcosphereDirectionMapping mapping)
    {
        ClearVisual();
        generator = owner; resourceField = field; oceanMesh = mesh; oceanRenderer = renderer; directionMapping = mapping;
        if (generator == null || generator.CurrentGridType != PlanetGridType.GeodesicIcosphere || resourceField == null ||
            !resourceField.IsInitialized || oceanMesh == null || directionMapping == null) return;
        vertexColours = oceanMesh.colors;
        if (vertexColours == null || vertexColours.Length != oceanMesh.vertexCount) vertexColours = new Color[oceanMesh.vertexCount];
        ApplyMaterialSettings();
        RefreshColours();
        enabled = true;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime) return;
        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, refreshIntervalSeconds);
        if (generator == null || generator.CurrentGridType != PlanetGridType.GeodesicIcosphere || resourceField == null || !resourceField.IsInitialized) return;
        ApplyMaterialSettings();
        if (lastTransportTick != resourceField.CompletedTransportTicks) RefreshColours();
    }

    private void ApplyMaterialSettings()
    {
        Material material = oceanRenderer != null ? oceanRenderer.sharedMaterial : null;
        if (material == null) return;
        material.SetColor(LowTintId, lowFe2Tint);
        material.SetColor(HighTintId, highFe2Tint);
        material.SetFloat(IntensityId, Mathf.Clamp01(intensityMultiplier));
    }

    private void RefreshColours()
    {
        if (vertexColours == null || directionMapping.Samples.Length != vertexColours.Length) return;
        float minimum = Mathf.Max(0f, minimumVisualizedFe2);
        float maximum = Mathf.Max(minimum + 1e-8f, maximumVisualizedFe2);
        for (int vertex = 0; vertex < vertexColours.Length; vertex++)
        {
            int cell = directionMapping.Samples[vertex].NearestCell;
            float concentration = GetSurfaceFacingColumnFe2(resourceField, cell, layer1VisualWeight);
            Color colour = vertexColours[vertex]; // red remains the existing bathymetric depth channel.
            colour.g = NormalizeVisualizedFe2(concentration, minimum, maximum);
            colour.b = 0f; colour.a = 1f;
            vertexColours[vertex] = colour;
        }
        oceanMesh.colors = vertexColours;
        lastTransportTick = resourceField.CompletedTransportTicks;
    }

    public static float GetSurfaceFacingColumnFe2(GeodesicOceanResourceField field, int cell, float layer1Weight)
    {
        if (field == null || !field.IsInitialized || field.SourceGrid == null || cell < 0 || cell >= field.CellCount ||
            !field.SourceGrid.SourceOceanMask[cell]) return 0f;
        int layers = field.SourceGrid.ActiveLayerCountByCell[cell];
        if (layers < 1 || !field.TryGetConcentration(cell, 0, GeodesicOceanResource.Fe2, out float layer0Fe2)) return 0f;
        float layer1Fe2 = 0f;
        bool hasLayer1 = layers > 1 && field.TryGetConcentration(cell, 1, GeodesicOceanResource.Fe2, out layer1Fe2);
        return CombineVisibleLayers(layer0Fe2, hasLayer1, layer1Fe2, layer1Weight);
    }

    public static float CombineVisibleLayers(float layer0Fe2, bool hasLayer1, float layer1Fe2, float layer1Weight)
    {
        if (!hasLayer1) return layer0Fe2;
        float weight = Mathf.Clamp01(layer1Weight);
        return (layer0Fe2 + layer1Fe2 * weight) / (1f + weight);
    }

    public static float NormalizeVisualizedFe2(float visibleFe2, float minimumFe2, float maximumFe2)
    {
        float minimum = Mathf.Max(0f, minimumFe2);
        float maximum = Mathf.Max(minimum + 1e-8f, maximumFe2);
        return Mathf.InverseLerp(minimum, maximum, visibleFe2);
    }

    public void ClearVisual()
    {
        if (oceanRenderer != null && oceanRenderer.sharedMaterial != null) oceanRenderer.sharedMaterial.SetFloat(IntensityId, 0f);
        generator = null; resourceField = null; oceanMesh = null; oceanRenderer = null; directionMapping = null; vertexColours = null;
        lastTransportTick = long.MinValue; nextRefreshTime = 0f; enabled = false;
    }

    private void OnDestroy() => ClearVisual();
}
