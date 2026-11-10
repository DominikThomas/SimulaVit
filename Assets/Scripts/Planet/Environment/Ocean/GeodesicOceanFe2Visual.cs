using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Visual-only dissolved Fe2+ tint for the geodesic ocean surface. Each render vertex samples
/// its authoritative GeodesicOceanResourceField column, with successively less weight on deeper layers.
/// The separate blue vertex channel carries recent local Fe2 oxidation for the shader's rust tint.
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
    [FormerlySerializedAs("layer1VisualWeight")]
    [SerializeField, Range(0f, 1f), Tooltip("Multiplies the visual weight at each successive depth layer. L0 has weight 1.")]
    private float deeperLayerWeightMultiplier = 0.55f;
    [Header("Visual-only rusty oxidation turbidity")]
    [SerializeField, Min(0f)] private float maximumVisualizedRecentOxidation = 2f;
    [SerializeField] private Color rustyOxidationTint = new Color(0.72f, 0.30f, 0.08f, 1f);
    [SerializeField, Range(0f, 1f)] private float rustyOxidationIntensity = 0.65f;

    private static readonly int LowTintId = Shader.PropertyToID("_Fe2VisualLowTint");
    private static readonly int HighTintId = Shader.PropertyToID("_Fe2VisualHighTint");
    private static readonly int IntensityId = Shader.PropertyToID("_Fe2VisualIntensity");
    private static readonly int RustTintId = Shader.PropertyToID("_RustVisualTint");
    private static readonly int RustIntensityId = Shader.PropertyToID("_RustVisualIntensity");
    private PlanetGenerator generator;
    private GeodesicOceanResourceField resourceField;
    private GeodesicAbioticChemistry chemistry;
    private Mesh oceanMesh;
    private MeshRenderer oceanRenderer;
    private IcosphereDirectionMapping directionMapping;
    private Color[] vertexColours;
    private float nextRefreshTime;
    private long lastTransportTick = long.MinValue;

    public bool IsInitialized => ColourUploadCount > 0 && oceanMesh != null && resourceField != null && resourceField.IsInitialized;
    public bool HasColourMaterial
    {
        get
        {
            Material material = oceanRenderer != null ? oceanRenderer.sharedMaterial : null;
            return material != null && material.shader != null && material.shader.isSupported
                && material.HasProperty(IntensityId) && material.HasProperty(RustIntensityId);
        }
    }
    public ulong ColourUploadCount { get; private set; }
    public long LastColourTransportTick => lastTransportTick;
    public float MinimumSampledFe2 { get; private set; }
    public float MaximumSampledFe2 { get; private set; }

    public void Initialize(PlanetGenerator owner, GeodesicOceanResourceField field, Mesh mesh, MeshRenderer renderer, IcosphereDirectionMapping mapping)
    {
        ClearVisual();
        generator = owner; resourceField = field; oceanMesh = mesh; oceanRenderer = renderer; directionMapping = mapping;
        chemistry = owner != null ? owner.GetComponent<GeodesicAbioticChemistry>() : null;
        if (generator == null || generator.CurrentGridType != PlanetGridType.GeodesicIcosphere || resourceField == null ||
            !resourceField.IsInitialized || oceanMesh == null || directionMapping == null) return;
        vertexColours = oceanMesh.colors;
        if (vertexColours == null || vertexColours.Length != oceanMesh.vertexCount) vertexColours = new Color[oceanMesh.vertexCount];
        ApplyMaterialSettings();
        RefreshColours();
        enabled = true;
        Debug.Log($"[GeodesicOceanColour] initialized={IsInitialized}, shaderBound={HasColourMaterial}, vertices={vertexColours.Length}, Fe2=GeodesicOceanResourceField active L0-L4, deeperLayerWeight={deeperLayerWeightMultiplier:G6}, Fe2TintRange={minimumVisualizedFe2:G6}/{maximumVisualizedFe2:G6}, rust=recent GeodesicAbioticChemistry Fe2 oxidation", this);
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
        material.SetColor(RustTintId, rustyOxidationTint);
        material.SetFloat(RustIntensityId, Mathf.Clamp01(rustyOxidationIntensity));
    }

    private void RefreshColours()
    {
        if (vertexColours == null || directionMapping.Samples.Length != vertexColours.Length) return;
        float minimum = Mathf.Max(0f, minimumVisualizedFe2);
        float maximum = Mathf.Max(minimum + 1e-8f, maximumVisualizedFe2);
        float sampledMinimum = float.PositiveInfinity, sampledMaximum = 0f;
        GeodesicOceanLayerGrid grid = resourceField.SourceGrid;
        for (int vertex = 0; vertex < vertexColours.Length; vertex++)
        {
            int cell = directionMapping.Samples[vertex].NearestCell;
            float concentration = GetSurfaceFacingColumnFe2(resourceField, cell, deeperLayerWeightMultiplier);
            if (grid.IsNodeActive(cell, 0))
            {
                sampledMinimum = Mathf.Min(sampledMinimum, concentration);
                sampledMaximum = Mathf.Max(sampledMaximum, concentration);
            }
            Color colour = vertexColours[vertex]; // red remains the existing bathymetric depth channel.
            colour.g = NormalizeVisualizedFe2(concentration, minimum, maximum);
            colour.b = NormalizeRustSignal(chemistry != null ? chemistry.GetRecentOxidizedIronSignal(cell) : 0f, maximumVisualizedRecentOxidation); colour.a = 1f;
            vertexColours[vertex] = colour;
        }
        oceanMesh.colors = vertexColours;
        lastTransportTick = resourceField.CompletedTransportTicks;
        ColourUploadCount++;
        MinimumSampledFe2 = float.IsPositiveInfinity(sampledMinimum) ? 0f : sampledMinimum;
        MaximumSampledFe2 = sampledMaximum;
    }

    public static float GetSurfaceFacingColumnFe2(GeodesicOceanResourceField field, int cell, float deeperWeightMultiplier)
    {
        if (field == null || !field.IsInitialized || field.SourceGrid == null || cell < 0 || cell >= field.CellCount ||
            !field.SourceGrid.SourceOceanMask[cell]) return 0f;
        int layers = field.SourceGrid.ActiveLayerCountByCell[cell];
        double decay = Mathf.Clamp01(deeperWeightMultiplier);
        double weight = 1d, weightedFe2 = 0d, weightSum = 0d;
        for (int layer = 0; layer < layers && weight > 0d; layer++)
        {
            if (field.TryGetConcentration(cell, layer, GeodesicOceanResource.Fe2, out float fe2))
            {
                weightedFe2 += fe2 * weight;
                weightSum += weight;
            }
            weight *= decay;
        }
        return weightSum > 0d ? (float)(weightedFe2 / weightSum) : 0f;
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
    public static float NormalizeRustSignal(float signal, float maximum) => Mathf.Clamp01(signal / Mathf.Max(1e-8f, maximum));

    public void ClearVisual()
    {
        if (oceanRenderer != null && oceanRenderer.sharedMaterial != null) { oceanRenderer.sharedMaterial.SetFloat(IntensityId, 0f); oceanRenderer.sharedMaterial.SetFloat(RustIntensityId, 0f); }
        generator = null; resourceField = null; chemistry = null; oceanMesh = null; oceanRenderer = null; directionMapping = null; vertexColours = null;
        ColourUploadCount = 0UL;
        MinimumSampledFe2 = MaximumSampledFe2 = 0f;
        lastTransportTick = long.MinValue; nextRefreshTime = 0f; enabled = false;
    }

    private void OnDestroy() => ClearVisual();
}
