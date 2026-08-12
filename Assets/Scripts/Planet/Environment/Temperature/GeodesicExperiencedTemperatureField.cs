using System;
using UnityEngine;
using UnityEngine.Serialization;

public static class GeodesicVentThermalModel
{
    public const float SourceTemperatureC = 350f;

    // Flat visible core followed by compact-support smootherstep falloff. Keeping the core
    // separate prevents a large rendered outlet from looking thermally cold at its edge.
    public static float EvaluateInfluence(float distance, float coreRadius, float falloffDistance, float strength)
    {
        if (!float.IsFinite(distance) || !float.IsFinite(coreRadius) || !float.IsFinite(falloffDistance) || !float.IsFinite(strength) || coreRadius < 0f || falloffDistance <= 0f) return 0f;
        float boundedStrength = Mathf.Sqrt(Mathf.Clamp01(strength));
        if (distance <= coreRadius) return boundedStrength;
        if (distance >= coreRadius + falloffDistance) return 0f;
        float x = Mathf.Clamp01((distance - coreRadius) / falloffDistance);
        float smooth = 1f - x * x * x * (x * (x * 6f - 15f) + 10f);
        return Mathf.Clamp01(smooth * boundedStrength);
    }

    public static bool IsHabitatEligible(GeodesicVentHabitat habitat, bool queryIsOcean, int queryLayer, int bottomLayer)
    {
        return habitat == GeodesicVentHabitat.Submarine ? queryIsOcean && queryLayer == bottomLayer : !queryIsOcean && queryLayer == 0;
    }

    public static float BlendKelvin(float baseKelvin, float sourceKelvin, float influence)
    {
        if (!float.IsFinite(baseKelvin) || !float.IsFinite(sourceKelvin)) return float.NaN;
        return Mathf.Lerp(baseKelvin, Mathf.Max(baseKelvin, sourceKelvin), Mathf.Clamp01(influence));
    }
}

[Serializable]
public readonly struct GeodesicVentOutlet
{
    public readonly GeodesicVentHabitat Habitat;
    public readonly int CellIndex;
    public readonly int BottomLayerIndex;
    public readonly int SystemIndex;
    public readonly Vector3 PlanetLocalPosition;
    public readonly Vector3 PlanetLocalNormal;
    public readonly float Strength01;
    public readonly float HotCoreRadius;

    public GeodesicVentOutlet(GeodesicVentHabitat habitat, int cellIndex, int bottomLayerIndex, int systemIndex, Vector3 localPosition, Vector3 localNormal, float strength01, float hotCoreRadius)
    { Habitat = habitat; CellIndex = cellIndex; BottomLayerIndex = bottomLayerIndex; SystemIndex = systemIndex; PlanetLocalPosition = localPosition; PlanetLocalNormal = localNormal; Strength01 = Mathf.Clamp01(strength01); HotCoreRadius = Mathf.Max(0f, hotCoreRadius); }
}

/// <summary>Read-only environment query combining authoritative coarse temperatures with indexed local vent outlets.</summary>
[DisallowMultipleComponent]
public sealed class GeodesicExperiencedTemperatureField : MonoBehaviour
{
    [FormerlySerializedAs("ventMicrothermalRadius")]
    [SerializeField, Min(0.001f), Tooltip("Planet-local falloff distance outside each visible hot core; independent of clustering and chemistry.")] private float ventMicrothermalFalloffDistance = 0.12f;
    [SerializeField, Min(0f), Tooltip("Planet-local visible/hot-core radius of the weakest outlet. The visual marker uses this same footprint.")] private float minimumOutletCoreRadius = 0.02f;
    [SerializeField, Min(0f), Tooltip("Planet-local visible/hot-core radius of the strongest outlet. The visual marker uses this same footprint.")] private float maximumOutletCoreRadius = 0.08f;
    [SerializeField, Range(1, 8)] private int maximumOutletsPerSystem = 5;
    [SerializeField, Range(0.1f, 20f)] private float outletSelectionRadiusDegrees = 3.5f;
    [SerializeField] private int outletCount;
    [SerializeField] private int indexedCellCount;
    [SerializeField] private int minimumNearbyOutlets;
    [SerializeField] private float meanNearbyOutlets;
    [SerializeField] private int maximumNearbyOutlets;
    [SerializeField] private long lookupMemoryBytes;

    private PlanetGenerator generator;
    private GeodesicOceanResourceField resources;
    private GeodesicSurfaceTemperatureField surface;
    private GeodesicOceanTemperatureField ocean;
    private GeodesicVentOutlet[] outlets = Array.Empty<GeodesicVentOutlet>();
    private int[] cellOffsets = Array.Empty<int>();
    private int[] outletIndices = Array.Empty<int>();

    public bool IsInitialized => generator != null && resources != null && resources.IsInitialized && cellOffsets.Length > 0;
    public float VentMicrothermalRadius => ventMicrothermalFalloffDistance;
    public int OutletCount => outletCount;
    public int IndexedCellCount => indexedCellCount;
    public long LookupMemoryBytes => lookupMemoryBytes;
    public int MinimumNearbyOutlets => minimumNearbyOutlets;
    public float MeanNearbyOutlets => meanNearbyOutlets;
    public int MaximumNearbyOutlets => maximumNearbyOutlets;
    public float SourceTemperatureC => GeodesicVentThermalModel.SourceTemperatureC;

    public void Initialize(GeodesicOceanResourceField resourceField, PlanetGenerator planet)
    {
        Clear(); resources = resourceField; generator = planet;
        surface = GetComponent<GeodesicSurfaceTemperatureField>(); ocean = GetComponent<GeodesicOceanTemperatureField>();
        if (resources == null || !resources.IsInitialized || generator == null || resources.SourceGrid == null) return;
        BuildOutlets(); BuildLookup();
        Debug.Log($"[GeodesicExperiencedTemperature] outlets={outletCount}, indexedCells={indexedCellCount}, nearbyMinMeanMax={minimumNearbyOutlets}/{meanNearbyOutlets:F2}/{maximumNearbyOutlets}, lookupBytes={lookupMemoryBytes}, coreRadius={minimumOutletCoreRadius:F4}-{maximumOutletCoreRadius:F4}, outsideCoreFalloff={ventMicrothermalFalloffDistance:F4}, falloff=smootherstep*sqrt(localStrength), combination=max", this);
    }

    private void BuildOutlets()
    {
        int capacity = Mathf.Max(1, resources.VentCount * maximumOutletsPerSystem);
        var built = new GeodesicVentOutlet[capacity]; int count = 0;
        int[] selected = new int[Mathf.Max(1, maximumOutletsPerSystem)];
        Vector3[] directions = resources.SourceGrid.SourceTopology.CellDirections;
        float maximumRaw = 0f;
        for (int i = 0; i < resources.VentCount; i++) if (resources.TryGetVentSystem(i, out GeodesicVentSystem s)) maximumRaw = Mathf.Max(maximumRaw, s.RawStrengthSum);
        for (int systemIndex = 0; systemIndex < resources.VentCount; systemIndex++)
        {
            if (!resources.TryGetVentSystem(systemIndex, out GeodesicVentSystem system)) continue;
            GeodesicVentVisualArchetype archetype = GeodesicVentOutletSelector.GetArchetype(system.RepresentativeCell);
            int requested = archetype == GeodesicVentVisualArchetype.SingleDominant ? 1 : archetype == GeodesicVentVisualArchetype.DominantWithSatellites ? 3 + (system.RepresentativeCell & 1) : 3 + system.RepresentativeCell % 3;
            int selectedCount = GeodesicVentOutletSelector.SelectLocalMembers(system, directions, outletSelectionRadiusDegrees, Mathf.Min(requested, maximumOutletsPerSystem), selected);
            for (int i = 0; i < selectedCount; i++)
            {
                GeodesicVentCandidate member = system.Members[selected[i]]; int cell = member.CellIndex;
                if (!generator.TryGetVisibleGeodesicSeafloorWorldAnchor(cell, out Vector3 worldPosition, out Vector3 worldNormal)) continue;
                float systemStrength = maximumRaw > 0f ? Mathf.Sqrt(system.RawStrengthSum / maximumRaw) : 0f;
                float memberStrength = Mathf.Sqrt(member.RawStrength / Mathf.Max(system.RawStrengthMax, 1e-6f));
                int bottom = system.Habitat == GeodesicVentHabitat.Submarine ? resources.SourceGrid.GetBottomLayerIndex(cell) : -1;
                float strength = Mathf.Clamp01(systemStrength * memberStrength);
                float coreRadius = Mathf.Lerp(Mathf.Max(0f, minimumOutletCoreRadius), Mathf.Max(minimumOutletCoreRadius, maximumOutletCoreRadius), strength);
                built[count++] = new GeodesicVentOutlet(system.Habitat, cell, bottom, systemIndex, transform.InverseTransformPoint(worldPosition), transform.InverseTransformDirection(worldNormal).normalized, strength, coreRadius);
            }
        }
        outlets = new GeodesicVentOutlet[count]; Array.Copy(built, outlets, count); outletCount = count;
    }

    private void BuildLookup()
    {
        int cells = resources.SourceGrid.CellCount; int[] counts = new int[cells]; GeodesicGridTopology topology = resources.SourceGrid.SourceTopology;
        for (int i = 0; i < outlets.Length; i++) { int cell = outlets[i].CellIndex; counts[cell]++; for (int n = 0; n < topology.NeighborCounts[cell]; n++) counts[topology.Neighbors6[cell * 6 + n]]++; }
        cellOffsets = new int[cells + 1]; for (int c = 0; c < cells; c++) cellOffsets[c + 1] = cellOffsets[c] + counts[c];
        outletIndices = new int[cellOffsets[cells]]; int[] cursors = new int[cells]; Array.Copy(cellOffsets, cursors, cells);
        for (int i = 0; i < outlets.Length; i++) { int cell = outlets[i].CellIndex; outletIndices[cursors[cell]++] = i; for (int n = 0; n < topology.NeighborCounts[cell]; n++) { int neighbor = topology.Neighbors6[cell * 6 + n]; outletIndices[cursors[neighbor]++] = i; } }
        indexedCellCount = 0; minimumNearbyOutlets = int.MaxValue; maximumNearbyOutlets = 0; long total = 0;
        for (int c = 0; c < cells; c++) { int count = counts[c]; if (count == 0) continue; indexedCellCount++; total += count; minimumNearbyOutlets = Mathf.Min(minimumNearbyOutlets, count); maximumNearbyOutlets = Mathf.Max(maximumNearbyOutlets, count); }
        if (indexedCellCount == 0) minimumNearbyOutlets = 0; meanNearbyOutlets = indexedCellCount > 0 ? total / (float)indexedCellCount : 0f;
        lookupMemoryBytes = (long)(cellOffsets.Length + outletIndices.Length) * sizeof(int) + (long)outlets.Length * 52L;
    }

    public bool TryGetLocalTemperatureKelvin(int cellIndex, int layerIndex, Vector3 worldPosition, out float temperatureKelvin)
    {
        temperatureKelvin = float.NaN; if (!IsInitialized || cellIndex < 0 || cellIndex >= resources.SourceGrid.CellCount) return false;
        bool isOcean = resources.SourceGrid.SourceOceanMask[cellIndex];
        if (isOcean) { if (ocean == null || !ocean.TryGetLayerTemperatureKelvin(cellIndex, layerIndex, out temperatureKelvin)) return false; if (layerIndex != resources.SourceGrid.GetBottomLayerIndex(cellIndex)) return true; }
        else { if (layerIndex != 0 || surface == null) return false; temperatureKelvin = surface.GetCellTemperatureKelvin(cellIndex); }
        Vector3 local = transform.InverseTransformPoint(worldPosition); float strongest = 0f;
        for (int cursor = cellOffsets[cellIndex]; cursor < cellOffsets[cellIndex + 1]; cursor++)
        {
            GeodesicVentOutlet outlet = outlets[outletIndices[cursor]]; if (!GeodesicVentThermalModel.IsHabitatEligible(outlet.Habitat, isOcean, layerIndex, resources.SourceGrid.GetBottomLayerIndex(cellIndex))) continue;
            strongest = Mathf.Max(strongest, GeodesicVentThermalModel.EvaluateInfluence(Vector3.Distance(local, outlet.PlanetLocalPosition), outlet.HotCoreRadius, ventMicrothermalFalloffDistance, outlet.Strength01));
        }
        temperatureKelvin = GeodesicVentThermalModel.BlendKelvin(temperatureKelvin, GeodesicVentThermalModel.SourceTemperatureC + 273.15f, strongest); return float.IsFinite(temperatureKelvin);
    }

    public bool TryGetOutlet(int index, out GeodesicVentOutlet outlet) { if (index < 0 || index >= outlets.Length) { outlet = default; return false; } outlet = outlets[index]; return true; }
    public void Clear() { generator = null; resources = null; surface = null; ocean = null; outlets = Array.Empty<GeodesicVentOutlet>(); cellOffsets = outletIndices = Array.Empty<int>(); outletCount = indexedCellCount = minimumNearbyOutlets = maximumNearbyOutlets = 0; meanNearbyOutlets = 0f; lookupMemoryBytes = 0; }
    private void OnDestroy() => Clear();
}
