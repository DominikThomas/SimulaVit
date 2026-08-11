using UnityEngine;

/// <summary>
/// Visual-only dissolved Fe2+ tint for the geodesic ocean surface. Each render vertex samples
/// its authoritative GeodesicOceanResourceField column. The column is averaged with exponentially
/// decreasing weights from the surface downward (surface weight 1, then 0.55 per layer). This keeps
/// the surface readable and faithful to near-surface water while retaining a small signal from deep
/// vent enrichment before vertical mixing carries it upward. It never reads PlanetResourceMap.
/// </summary>
[DisallowMultipleComponent]
public sealed class GeodesicOceanFe2Visual : MonoBehaviour
{
    [Header("Geodesic Dissolved Fe2+ Ocean Tint")]
    [SerializeField, Min(0f), Tooltip("Concentrations at or below this level have no Fe2+ tint.")]
    private float minimumVisualizedFe2 = 0.00001f;
    [SerializeField, Min(0f), Tooltip("Concentrations at or above this level receive the maximum tint.")]
    private float maximumVisualizedFe2 = 0.01f;
    [SerializeField] private Color lowFe2Tint = new Color(0.20f, 0.36f, 0.44f, 1f);
    [SerializeField] private Color highFe2Tint = new Color(0.58f, 0.30f, 0.12f, 1f);
    [SerializeField, Range(0f, 1f)] private float intensityMultiplier = 0.42f;
    [SerializeField, Min(0.1f), Tooltip("Real-time seconds between visual refresh checks. Mesh colours only upload after an authoritative transport tick changes.")]
    private float refreshIntervalSeconds = 0.5f;
    [SerializeField, Range(0.05f, 1f), Tooltip("Multiplier applied successively to deeper layers in the shallow-column surface-facing average.")]
    private float deeperLayerWeightMultiplier = 0.55f;

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
            float concentration = GetSurfaceFacingColumnFe2(resourceField, cell, deeperLayerWeightMultiplier);
            Color colour = vertexColours[vertex]; // red remains the existing bathymetric depth channel.
            colour.g = Mathf.InverseLerp(minimum, maximum, concentration);
            colour.b = 0f; colour.a = 1f;
            vertexColours[vertex] = colour;
        }
        oceanMesh.colors = vertexColours;
        lastTransportTick = resourceField.CompletedTransportTicks;
    }

    public static float GetSurfaceFacingColumnFe2(GeodesicOceanResourceField field, int cell, float deeperWeightMultiplier)
    {
        if (field == null || !field.IsInitialized || field.SourceGrid == null || cell < 0 || cell >= field.CellCount ||
            !field.SourceGrid.SourceOceanMask[cell]) return 0f;
        int layers = field.SourceGrid.ActiveLayerCountByCell[cell];
        float weight = 1f, weighted = 0f, weightSum = 0f;
        float decay = Mathf.Clamp(deeperWeightMultiplier, 0.05f, 1f);
        for (int layer = 0; layer < layers; layer++)
        {
            if (field.TryGetConcentration(cell, layer, GeodesicOceanResource.Fe2, out float value))
            { weighted += value * weight; weightSum += weight; }
            weight *= decay;
        }
        return weightSum > 0f ? weighted / weightSum : 0f;
    }

    public void ClearVisual()
    {
        if (oceanRenderer != null && oceanRenderer.sharedMaterial != null) oceanRenderer.sharedMaterial.SetFloat(IntensityId, 0f);
        generator = null; resourceField = null; oceanMesh = null; oceanRenderer = null; directionMapping = null; vertexColours = null;
        lastTransportTick = long.MinValue; nextRefreshTime = 0f; enabled = false;
    }

    private void OnDestroy() => ClearVisual();
}
