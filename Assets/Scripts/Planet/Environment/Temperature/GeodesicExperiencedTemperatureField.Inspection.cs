using System.Text;
using UnityEngine;

public sealed partial class GeodesicExperiencedTemperatureField
{
    public bool TryGetLayerProbe(int cell,int layer,Vector3 selectedWorldPosition,out Vector3 probe,out GeodesicVentTemperatureSample sample)
    {
        probe=default; sample=GeodesicVentTemperatureSample.Background(float.NaN);
        if(!IsInitialized || !resources.SourceGrid.IsNodeActive(cell,layer)) return false;
        Vector3 direction=transform.InverseTransformPoint(selectedWorldPosition).normalized;
        if(!GeodesicBiologyDiagnostics.FinitePosition(direction) || direction.sqrMagnitude<.9f
            || !GeodesicBiologyRuntime.DirectionInsideCell(cell,direction,resources.SourceGrid.SourceTopology)) return false;
        probe=transform.TransformPoint(direction*resources.SourceGrid.LayerCenterRadius[resources.SourceGrid.GetNodeIndex(cell,layer)]);
        return TryGetBackground(cell,layer,out float coarse) && TrySampleTemperature(cell,layer,probe,coarse,out sample);
    }

    /// <summary>Selected-cell view only. worldPosition is an actual clicked world point, not a unit direction.</summary>
    public bool TryGetCellInspectionSnapshot(int cell,Vector3 worldPosition,out PlanetResourceMap.CellInspectionSnapshot snapshot)
    {
        snapshot=default;
        if(!IsInitialized || cell<0 || cell>=resources.SourceGrid.CellCount || surface==null || !surface.IsInitialized) return false;
        var grid=resources.SourceGrid; bool isOcean=grid.SourceOceanMask[cell];
        snapshot.IsValid=true; snapshot.IsGeodesic=true; snapshot.CellIndex=cell; snapshot.IsOcean=isOcean;
        snapshot.ActiveLayerCount=grid.ActiveLayerCountByCell[cell];
        snapshot.SurfaceTemperatureKelvin=surface.GetCellTemperatureKelvin(cell);
        snapshot.EffectiveTemperatureKelvin=snapshot.SurfaceTemperatureKelvin;
        snapshot.Insolation=surface.GetCellInsolationCosine(cell);
        snapshot.VentStrength=resources.GetSubmarineThermalInfluence(cell);
        snapshot.ExperiencedProbeLayer=-1;
        if(!isOcean) return true;
        var layers=new PlanetResourceMap.OceanLayerSnapshot[snapshot.ActiveLayerCount];
        double totalVolume=0d,co2=0d,o2=0d,ch4=0d,h2=0d,h2s=0d,organic=0d,fe2=0d;
        for(int layer=0;layer<layers.Length;layer++)
        {
            ref var item=ref layers[layer]; item.LayerIndex=layer;
            resources.TryGetConcentration(cell,layer,GeodesicOceanResource.CO2,out item.CO2);
            resources.TryGetConcentration(cell,layer,GeodesicOceanResource.O2,out item.O2);
            resources.TryGetConcentration(cell,layer,GeodesicOceanResource.CH4,out item.CH4);
            resources.TryGetConcentration(cell,layer,GeodesicOceanResource.H2,out item.H2);
            resources.TryGetConcentration(cell,layer,GeodesicOceanResource.H2S,out item.H2S);
            resources.TryGetConcentration(cell,layer,GeodesicOceanResource.OrganicC,out item.OrganicC);
            resources.TryGetConcentration(cell,layer,GeodesicOceanResource.Fe2,out item.DissolvedFe2Plus);
            item.CoarseTemperatureKelvin=ocean!=null ? ocean.GetLayerTemperatureKelvin(cell,layer) : float.NaN;
            item.TemperatureKelvinEstimate=item.CoarseTemperatureKelvin; // Compatibility field, actual authority in geodesic mode.
            item.LightFactor=GeodesicBiologyRuntime.ResolvePhotosyntheticLight(snapshot.Insolation,layer);
            double volume=grid.PhysicalLayerVolumeKm3[grid.GetNodeIndex(cell,layer)]; totalVolume+=volume;
            co2+=item.CO2*volume; o2+=item.O2*volume; ch4+=item.CH4*volume; h2+=item.H2*volume;
            h2s+=item.H2S*volume; organic+=item.OrganicC*volume; fe2+=item.DissolvedFe2Plus*volume;
        }
        snapshot.OceanLayers=layers;
        if(totalVolume>0d) {
            snapshot.EffectiveCO2=(float)(co2/totalVolume); snapshot.EffectiveO2=(float)(o2/totalVolume);
            snapshot.EffectiveCH4=(float)(ch4/totalVolume); snapshot.EffectiveH2=(float)(h2/totalVolume);
            snapshot.EffectiveH2S=(float)(h2s/totalVolume); snapshot.EffectiveOrganicC=(float)(organic/totalVolume);
            snapshot.EffectiveDissolvedFe2Plus=(float)(fe2/totalVolume);
        }
        int bottom=grid.GetBottomLayerIndex(cell);
        snapshot.HasExperiencedProbe=TryGetLayerProbe(cell,bottom,worldPosition,out snapshot.ExperiencedProbeWorldPosition,out snapshot.ThermalProbe);
        snapshot.ExperiencedProbeLayer=bottom;
        return true;
    }

    public static string DescribeTemperatureSample(in GeodesicVentTemperatureSample sample,TemperatureDisplayUnit unit,string location="probe")
    {
        var text=new StringBuilder(320);
        text.Append("Coarse background: ").Append(ReplicatorManager.FormatTemperature(sample.CoarseKelvin,unit))
            .Append("\nExperienced at ").Append(location).Append(": ").Append(ReplicatorManager.FormatTemperature(sample.TemperatureKelvin,unit));
        if(sample.OutletIndex<0) return text.Append("\nLocal vent influence: none").ToString();
        text.Append("\nRelevant outlet/system: ").Append(sample.OutletIndex).Append('/').Append(sample.SystemIndex)
            .Append(" (source cell ").Append(sample.SourceCell).Append(')')
            .Append("\nSource-fluid reference: ").Append(ReplicatorManager.FormatTemperature(sample.SourceKelvin,unit))
            .Append("\nInfluence / hot core: ").Append(sample.Influence.ToString("G4")).Append('/').Append(sample.InsideHotCore)
            .Append("\nModel distance to thermal column: ").Append(sample.DistanceUnity.ToString("G5")).Append(" local U")
            .Append("\nDistance to seabed source: ").Append(sample.SourceDistanceUnity.ToString("G5")).Append(" local U / ")
            .Append(sample.SourceDistanceKm.ToString("G5")).Append(" km (depth-scaled)");
        return text.ToString();
    }
}
