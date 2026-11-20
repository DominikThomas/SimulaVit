using System;
using System.Collections.Generic;
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
        // Vertical reach is resolved continuously from the actual position, not by a hard bottom-layer switch.
        return habitat == GeodesicVentHabitat.Submarine ? queryIsOcean && queryLayer >= 0 && queryLayer <= bottomLayer : !queryIsOcean && queryLayer == 0;
    }

    public static float BlendKelvin(float baseKelvin, float sourceKelvin, float influence)
    {
        if (!float.IsFinite(baseKelvin) || !float.IsFinite(sourceKelvin)) return float.NaN;
        return Mathf.Lerp(baseKelvin, Mathf.Max(baseKelvin, sourceKelvin), Mathf.Clamp01(influence));
    }
}

/// <summary>Value-type observation; never a second heat/resource inventory.</summary>
[Serializable]
public struct GeodesicVentTemperatureSample
{
    public float CoarseKelvin, TemperatureKelvin, SourceKelvin, Influence;
    public float DistanceUnity, SourceDistanceUnity, SourceDistanceKm, HotCoreRadius;
    public int OutletIndex, SystemIndex, SourceCell;
    public bool InsideHotCore => Influence > 0f && DistanceUnity <= HotCoreRadius;

    public static GeodesicVentTemperatureSample Background(float kelvin) => new GeodesicVentTemperatureSample
    {
        CoarseKelvin=kelvin, TemperatureKelvin=kelvin, SourceKelvin=float.NaN,
        OutletIndex=-1, SystemIndex=-1, SourceCell=-1,
        DistanceUnity=float.PositiveInfinity, SourceDistanceUnity=float.PositiveInfinity, SourceDistanceKm=float.PositiveInfinity
    };
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
public sealed partial class GeodesicExperiencedTemperatureField : MonoBehaviour
{
    [FormerlySerializedAs("ventMicrothermalRadius")]
    [SerializeField, Min(0.001f), Tooltip("Planet-local falloff distance outside each visible hot core; independent of clustering and chemistry.")] private float ventMicrothermalFalloffDistance = 0.12f;
    [SerializeField, Min(0f), Tooltip("Planet-local visible/hot-core radius of the weakest outlet. The visual marker uses this same footprint.")] private float minimumOutletCoreRadius = 0.02f;
    [SerializeField, Min(0f), Tooltip("Planet-local visible/hot-core radius of the strongest outlet. The visual marker uses this same footprint.")] private float maximumOutletCoreRadius = 0.08f;
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
    private float[] outletColumnTopRadius = Array.Empty<float>();
    private int[] outletBySourceNode = Array.Empty<int>();

    public bool IsInitialized => generator != null && resources != null && resources.IsInitialized && cellOffsets.Length > 0;
    public float VentMicrothermalRadius => ventMicrothermalFalloffDistance;
    public int OutletCount => outletCount;
    public int IndexedCellCount => indexedCellCount;
    public long LookupMemoryBytes => lookupMemoryBytes;
    public int MinimumNearbyOutlets => minimumNearbyOutlets;
    public float MeanNearbyOutlets => meanNearbyOutlets;
    public int MaximumNearbyOutlets => maximumNearbyOutlets;
    public float SourceTemperatureC => ocean != null && float.IsFinite(ocean.SubmarineVentSourceTemperatureKelvin)
        ? ocean.SubmarineVentSourceTemperatureKelvin - 273.15f : GeodesicVentThermalModel.SourceTemperatureC;
    public float MaximumSupportRadius => Mathf.Max(minimumOutletCoreRadius,maximumOutletCoreRadius) + ventMicrothermalFalloffDistance;

    public void Initialize(GeodesicOceanResourceField resourceField, PlanetGenerator planet)
    {
        Clear(); resources = resourceField; generator = planet;
        surface = GetComponent<GeodesicSurfaceTemperatureField>(); ocean = GetComponent<GeodesicOceanTemperatureField>();
        if (resources == null || !resources.IsInitialized || generator == null || resources.SourceGrid == null) return;
        BuildOutlets(); BuildLookup();
        Debug.Log($"[GeodesicVentThermalScale] sourceFluidC={SourceTemperatureC:G6} coreLocalU={minimumOutletCoreRadius:G6}-{maximumOutletCoreRadius:G6} coreHorizontalKm={GeodesicPhysicalScale.LengthKilometres(minimumOutletCoreRadius):G6}-{GeodesicPhysicalScale.LengthKilometres(maximumOutletCoreRadius):G6} falloffLocalU={ventMicrothermalFalloffDistance:G6} falloffHorizontalKm={GeodesicPhysicalScale.LengthKilometres(ventMicrothermalFalloffDistance):G6} radialKmPerLocalU={GeodesicPhysicalScale.PhysicalKilometresPerUnityUnit*resources.SourceGrid.PhysicalOceanDepthScale:G6} sourceColumn=visibleSeafloorToSourceBottomLayerCenter readOnly=true",this);
        Debug.Log($"[GeodesicExperiencedTemperature] outlets={outletCount}, authority=GeodesicOceanResourceField compactOutlets, indexedCells={indexedCellCount}, nearbyMinMeanMax={minimumNearbyOutlets}/{meanNearbyOutlets:F2}/{maximumNearbyOutlets}, lookupBytes={lookupMemoryBytes}, coreRadius={minimumOutletCoreRadius:F4}-{maximumOutletCoreRadius:F4}, outsideCoreFalloff={ventMicrothermalFalloffDistance:F4}, falloff=smootherstep*sqrt(localStrength), combination=max, distance=seafloor-to-bottom-center-segment, eligibility=activeOceanLayers-with-3D-attenuation", this);
    }

    private void BuildOutlets()
    {
        int capacity = Mathf.Max(1, resources.CompactOutletCount);
        var built = new GeodesicVentOutlet[capacity]; int count = 0;
        float maximumRaw = 0f;
        for (int i = 0; i < resources.VentCount; i++) if (resources.TryGetVentSystem(i, out GeodesicVentSystem s)) maximumRaw = Mathf.Max(maximumRaw, s.RawStrengthSum);
        for (int outletIndex = 0; outletIndex < resources.CompactOutletCount; outletIndex++)
        {
            if (!resources.TryGetVentOutlet(outletIndex, out GeodesicVentSourceOutlet source) || !resources.TryGetVentSystem(source.SystemIndex, out GeodesicVentSystem system)) continue;
            if (!resources.TryGetVentInjectionHabitat(outletIndex, out source, out int cell, out int injectionLayer)) continue;
            if (source.Habitat == GeodesicVentHabitat.Submarine &&
                (source.CellIndex != cell || injectionLayer != resources.SourceGrid.GetBottomLayerIndex(cell))) continue;
            if (!generator.TryGetVisibleGeodesicSeafloorWorldAnchor(cell, out Vector3 worldPosition, out _)) continue;
            float systemStrength = maximumRaw > 0f ? Mathf.Sqrt(system.RawStrengthSum / maximumRaw) : 0f;
            float memberStrength = Mathf.Sqrt(source.RawStrength / Mathf.Max(system.RawStrengthMax, 1e-6f));
            int bottom = source.Habitat == GeodesicVentHabitat.Submarine ? injectionLayer : -1;
            float strength = Mathf.Clamp01(systemStrength * memberStrength);
            float coreRadius = Mathf.Lerp(Mathf.Max(0f, minimumOutletCoreRadius), Mathf.Max(minimumOutletCoreRadius, maximumOutletCoreRadius), strength);
            Vector3 anchorLocal = transform.InverseTransformPoint(worldPosition);
            // Orange markers use the exact simulation-cell direction while retaining the completed
            // visible terrain's seafloor radius. CellDirections and this stored anchor are planet-local.
            Vector3 sourceDirection = resources.SourceGrid.SourceTopology.CellDirections[cell];
            Vector3 exactSourceAnchor = sourceDirection * anchorLocal.magnitude;
            built[count++] = new GeodesicVentOutlet(source.Habitat, cell, bottom, source.SystemIndex,
                exactSourceAnchor, sourceDirection, strength, coreRadius);
        }
        outlets = new GeodesicVentOutlet[count]; Array.Copy(built, outlets, count); outletCount = count;
    }

    private void BuildLookup()
    {
        var grid=resources.SourceGrid; var topology=grid.SourceTopology; int cells=grid.CellCount;
        var counts=new int[cells]; var pairs=new List<Vector2Int>();
        var visited=new int[cells]; var queue=new int[cells];
        outletColumnTopRadius=new float[outlets.Length]; outletBySourceNode=new int[grid.NodeCapacity];
        Array.Fill(outletBySourceNode,-1);
        // A conservative cell-footprint pad covers points near Voronoi edges, not just cell centers.
        float maximumSpacing=0f;
        for(int c=0;c<cells;c++) for(int k=0;k<topology.NeighborCounts[c];k++)
            maximumSpacing=Mathf.Max(maximumSpacing,topology.NeighborAngularDistances6[c*6+k]);
        for(int i=0;i<outlets.Length;i++)
        {
            var outlet=outlets[i]; float sourceRadius=outlet.PlanetLocalPosition.magnitude;
            float columnTop=sourceRadius;
            if(outlet.Habitat==GeodesicVentHabitat.Submarine)
            {
                int node=grid.GetNodeIndex(outlet.CellIndex,outlet.BottomLayerIndex);
                columnTop=Mathf.Max(sourceRadius,grid.LayerCenterRadius[node]); outletBySourceNode[node]=i;
            }
            outletColumnTopRadius[i]=columnTop;
            float support=outlet.HotCoreRadius+Mathf.Max(.001f,ventMicrothermalFalloffDistance);
            // Any point within support of the radial segment lies in this angular cap.
            float cap=sourceRadius>support ? Mathf.Asin(Mathf.Clamp01(support/sourceRadius)) : Mathf.PI;
            float cutoff=Mathf.Cos(Mathf.Min(Mathf.PI,cap+2f*maximumSpacing));
            int read=0,write=0,stamp=i+1; queue[write++]=outlet.CellIndex; visited[outlet.CellIndex]=stamp;
            while(read<write)
            {
                int cell=queue[read++]; counts[cell]++; pairs.Add(new Vector2Int(cell,i));
                for(int k=0;k<topology.NeighborCounts[cell];k++)
                {
                    int neighbor=topology.Neighbors6[cell*6+k]; if(visited[neighbor]==stamp) continue;
                    visited[neighbor]=stamp;
                    if(Vector3.Dot(topology.CellDirections[neighbor],outlet.PlanetLocalNormal)>=cutoff) queue[write++]=neighbor;
                }
            }
        }
        cellOffsets=new int[cells+1]; for(int c=0;c<cells;c++) cellOffsets[c+1]=cellOffsets[c]+counts[c];
        outletIndices=new int[pairs.Count]; var cursors=new int[cells]; Array.Copy(cellOffsets,cursors,cells);
        for(int i=0;i<pairs.Count;i++) outletIndices[cursors[pairs[i].x]++]=pairs[i].y;
        indexedCellCount=0; minimumNearbyOutlets=int.MaxValue; maximumNearbyOutlets=0; long total=0;
        for(int c=0;c<cells;c++) if(counts[c]>0) {
            indexedCellCount++; total+=counts[c]; minimumNearbyOutlets=Mathf.Min(minimumNearbyOutlets,counts[c]); maximumNearbyOutlets=Mathf.Max(maximumNearbyOutlets,counts[c]);
        }
        if(indexedCellCount==0) minimumNearbyOutlets=0;
        meanNearbyOutlets=indexedCellCount>0 ? total/(float)indexedCellCount : 0f;
        lookupMemoryBytes=(long)(cellOffsets.Length+outletIndices.Length+outletBySourceNode.Length+outletColumnTopRadius.Length)*4L+(long)outlets.Length*52L;
    }

    public bool HasLocalInfluenceCandidates(int cell,int layer) => IsInitialized && cell>=0
        && cell+1<cellOffsets.Length && cellOffsets[cell]<cellOffsets[cell+1]
        && (resources.SourceGrid.SourceOceanMask[cell] ? resources.SourceGrid.IsNodeActive(cell,layer) : layer==0);

    public bool TryGetSubmarineOutletForNode(int node,out int index,out GeodesicVentOutlet outlet)
    {
        if(!IsInitialized) { index=-1; outlet=default; return false; }
        index=node>=0 && node<outletBySourceNode.Length ? outletBySourceNode[node] : -1;
        outlet=index>=0 ? outlets[index] : default; return index>=0;
    }

    public bool TryGetLocalTemperatureKelvin(int cellIndex,int layerIndex,Vector3 worldPosition,out float temperatureKelvin)
    {
        temperatureKelvin=float.NaN;
        if(!TryGetBackground(cellIndex,layerIndex,out float background)) return false;
        bool ok=TrySampleTemperature(cellIndex,layerIndex,worldPosition,background,out var sample);
        temperatureKelvin=sample.TemperatureKelvin; return ok;
    }

    private bool TryGetBackground(int cell,int layer,out float kelvin)
    {
        kelvin=float.NaN; if(!IsInitialized || cell<0 || cell>=resources.SourceGrid.CellCount) return false;
        if(resources.SourceGrid.SourceOceanMask[cell])
            return ocean!=null && ocean.TryGetLayerTemperatureKelvin(cell,layer,out kelvin);
        if(layer!=0 || surface==null) return false;
        kelvin=surface.GetCellTemperatureKelvin(cell); return float.IsFinite(kelvin);
    }

    /// <summary>Caller may supply the cached authoritative coarse value. No heat budget is modified.</summary>
    public bool TrySampleTemperature(int cell,int layer,Vector3 worldPosition,float coarseKelvin,out GeodesicVentTemperatureSample sample)
    {
        sample=GeodesicVentTemperatureSample.Background(coarseKelvin);
        if(!IsInitialized || !float.IsFinite(coarseKelvin) || cell<0 || cell>=resources.SourceGrid.CellCount) return false;
        var grid=resources.SourceGrid; bool isOcean=grid.SourceOceanMask[cell];
        int bottom=grid.GetBottomLayerIndex(cell);
        if(isOcean ? !grid.IsNodeActive(cell,layer) : layer!=0) return false;
        if(!float.IsFinite(worldPosition.x)||!float.IsFinite(worldPosition.y)||!float.IsFinite(worldPosition.z)) return false;
        if(!HasLocalInfluenceCandidates(cell,layer)) return true;
        Vector3 local=transform.InverseTransformPoint(worldPosition);
        if(!float.IsFinite(local.x)||!float.IsFinite(local.y)||!float.IsFinite(local.z)) return false;
        int selected=-1,nearest=-1; float strongest=0f,nearestDistance=float.PositiveInfinity;
        for(int cursor=cellOffsets[cell];cursor<cellOffsets[cell+1];cursor++)
        {
            int index=outletIndices[cursor]; var outlet=outlets[index];
            if(!GeodesicVentThermalModel.IsHabitatEligible(outlet.Habitat,isOcean,layer,bottom)) continue;
            // Submarine source extends through its unresolved seafloor-to-bottom-center segment.
            // Above/below that segment the actual radial offset attenuates normally.
            float axial=Vector3.Dot(local,outlet.PlanetLocalNormal);
            float anchorRadius=outlet.PlanetLocalPosition.magnitude;
            float reference=isOcean ? Mathf.Clamp(axial,anchorRadius,outletColumnTopRadius[index]) : anchorRadius;
            float distance=(local-outlet.PlanetLocalNormal*reference).magnitude;
            if(distance<nearestDistance) { nearestDistance=distance; nearest=index; }
            float influence=GeodesicVentThermalModel.EvaluateInfluence(distance,outlet.HotCoreRadius,ventMicrothermalFalloffDistance,outlet.Strength01);
            if(influence>strongest) { strongest=influence; selected=index; }
        }
        if(selected<0) selected=nearest;
        if(selected>=0)
        {
            var outlet=outlets[selected]; float axial=Vector3.Dot(local,outlet.PlanetLocalNormal);
            float radius=outlet.PlanetLocalPosition.magnitude;
            float reference=isOcean ? Mathf.Clamp(axial,radius,outletColumnTopRadius[selected]) : radius;
            Vector3 horizontal=local-outlet.PlanetLocalNormal*axial;
            double horizontalKm=GeodesicPhysicalScale.LengthKilometres(horizontal.magnitude);
            double verticalKm=GeodesicPhysicalScale.LengthKilometres(axial-radius)*(isOcean ? grid.PhysicalOceanDepthScale : 1d);
            sample.OutletIndex=selected; sample.SystemIndex=outlet.SystemIndex; sample.SourceCell=outlet.CellIndex;
            sample.Influence=strongest; sample.HotCoreRadius=outlet.HotCoreRadius;
            sample.DistanceUnity=(local-outlet.PlanetLocalNormal*reference).magnitude;
            sample.SourceDistanceUnity=(local-outlet.PlanetLocalPosition).magnitude;
            sample.SourceDistanceKm=(float)Math.Sqrt(horizontalKm*horizontalKm+verticalKm*verticalKm);
            sample.SourceKelvin=(isOcean ? SourceTemperatureC : GeodesicVentThermalModel.SourceTemperatureC)+273.15f;
            sample.TemperatureKelvin=GeodesicVentThermalModel.BlendKelvin(coarseKelvin,sample.SourceKelvin,strongest);
        }
        return float.IsFinite(sample.TemperatureKelvin);
    }

    public bool TryGetOutlet(int index, out GeodesicVentOutlet outlet) { if (index < 0 || index >= outlets.Length) { outlet = default; return false; } outlet = outlets[index]; return true; }
    public void Clear() { generator = null; resources = null; surface = null; ocean = null; outlets = Array.Empty<GeodesicVentOutlet>(); cellOffsets = outletIndices = outletBySourceNode = Array.Empty<int>(); outletColumnTopRadius = Array.Empty<float>(); outletCount = indexedCellCount = minimumNearbyOutlets = maximumNearbyOutlets = 0; meanNearbyOutlets = 0f; lookupMemoryBytes = 0; }
    private void OnDestroy() => Clear();
}
