using System;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private struct FounderPosition { public int cell,layer; public Vector3 local; }
    private System.Random founderPositionRandom;
    private float founderCO2Need=.01f,founderH2Need=.02f;
    private long founderThermalCandidateRejections,founderFoodCandidateRejections,founderInvalidCandidateRejections,founderThermalPlacementFailures;

    private bool TryFindFounderPosition(GeodesicVentSourceOutlet source,out FounderPosition position)
    {
        position=default;
        if(experiencedTemperature==null || !experiencedTemperature.TryGetSubmarineOutletForNode(source.SourceNode,out _,out var outlet))
        { founderInvalidCandidateRejections++; return false; }
        founderPositionRandom ??= new System.Random(unchecked(biologySeed ^ 0x5448524D));
        Vector3 normal=outlet.PlanetLocalNormal;
        Vector3 tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)<.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 side=Vector3.Cross(normal,tangent);
        float reach=outlet.HotCoreRadius+experiencedTemperature.VentMicrothermalRadius;
        float referenceRadius=grid.LayerCenterRadius[source.SourceNode];
        bool rejectedThermally=false;
        // Uniform area sampling of the local disc; no preferred radius or target-temperature ring.
        for(int attempt=0;attempt<32;attempt++)
        {
            float distance=reach*Mathf.Sqrt((float)founderPositionRandom.NextDouble());
            float bearing=(float)founderPositionRandom.NextDouble()*Mathf.PI*2f;
            Vector3 direction=normal*Mathf.Cos(distance/referenceRadius)
                +(tangent*Mathf.Cos(bearing)+side*Mathf.Sin(bearing))*Mathf.Sin(distance/referenceRadius);
            direction.Normalize(); int cell=source.CellIndex;
            for(int hop=0;hop<32;hop++) {
                int next=FindCloserRealNeighbor(cell,direction,grid.SourceTopology.CellDirections,grid.SourceTopology.Neighbors6,grid.SourceTopology.NeighborCounts);
                if(next<0) break; cell=next;
            }
            int layer=grid.GetBottomLayerIndex(cell);
            if(!grid.IsNodeActive(cell,layer) || !DirectionInsideCell(cell,direction,grid.SourceTopology))
            { founderInvalidCandidateRejections++; continue; }
            Vector3 local=direction*grid.LayerCenterRadius[grid.GetNodeIndex(cell,layer)];
            float temperature=SampleBiologicalTemperature(cell,layer,planet.transform.TransformPoint(local)).TemperatureKelvin;
            float performance=CalculateTemperaturePerformance(temperature,Mathf.Min(founderHydrogenTemperatureRange.x,founderHydrogenTemperatureRange.y),
                Mathf.Max(founderHydrogenTemperatureRange.x,founderHydrogenTemperatureRange.y),Mathf.Max(1f,founderLethalMargin),temperatureSettings.DormancyFloor);
            if(!GeodesicBiologyTemperaturePolicy.CanReproduce(performance,temperatureSettings))
            { rejectedThermally=true; founderThermalCandidateRejections++; continue; }
            if(FounderFoodReadiness(cell,layer,founderCO2Need,founderH2Need)<1d)
            { founderFoodCandidateRejections++; continue; }
            position=new FounderPosition {cell=cell,layer=layer,local=local}; return true;
        }
        if(rejectedThermally) founderThermalPlacementFailures++;
        return false;
    }

    private double FounderFoodReadiness(int cell,int layer,float co2Need,float h2Need)
    {
        if(!grid.IsNodeActive(cell,layer)) return 0d;
        resources.TryGetConcentration(cell,layer,GeodesicOceanResource.CO2,out float co2);
        resources.TryGetConcentration(cell,layer,GeodesicOceanResource.H2,out float h2);
        double ready=GeodesicFoodSignal.HydrogenotrophyTicks(co2,h2,grid.PhysicalLayerVolumeKm3[grid.GetNodeIndex(cell,layer)],co2Need,h2Need);
        return double.IsFinite(ready) && ready>0d ? ready : 0d;
    }

    private void ClearFounderThermalPlacement()
    {
        founderPositionRandom=null; founderCO2Need=.01f; founderH2Need=.02f;
        founderThermalCandidateRejections=founderFoodCandidateRejections=founderInvalidCandidateRejections=founderThermalPlacementFailures=0;
    }
}
