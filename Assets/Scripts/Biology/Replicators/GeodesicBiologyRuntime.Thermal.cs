using System;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private struct BiologicalThermalCache
    {
        public int step, coarseEpoch, node;
        public Vector3 position;
        public GeodesicVentTemperatureSample sample;
    }
    private BiologicalThermalCache[] biologicalThermalCache=Array.Empty<BiologicalThermalCache>();
    private int biologicalThermalStep=1;

    private void BeginBiologicalThermalStep()
    {
        if(++biologicalThermalStep!=int.MaxValue) return;
        Array.Clear(biologicalThermalCache,0,biologicalThermalCache.Length); biologicalThermalStep=1;
    }

    private GeodesicVentTemperatureSample SampleBiologicalTemperature(int cell,int layer,Vector3 position)
    {
        float coarse=GetHabitatTemperature(cell,layer); // This cache is deliberately coarse-only.
        if(!GeodesicBiologyDiagnostics.FinitePosition(position)) return GeodesicVentTemperatureSample.Background(float.NaN);
        if(experiencedTemperature!=null && experiencedTemperature.IsInitialized
            && experiencedTemperature.TrySampleTemperature(cell,layer,position,coarse,out var sample)) return sample;
        return GeodesicVentTemperatureSample.Background(coarse);
    }

    private GeodesicVentTemperatureSample GetAgentTemperatureSample(int index,ReplicatorPopulationState state)
    {
        int cell=state.GeodesicCellIndex[index],layer=state.CurrentOceanLayerIndex[index];
        if(!grid.IsNodeActive(cell,layer)) return GeodesicVentTemperatureSample.Background(float.NaN);
        if(experiencedTemperature==null || !experiencedTemperature.HasLocalInfluenceCandidates(cell,layer))
            return SampleBiologicalTemperature(cell,layer,state.Position[index]);
        if(biologicalThermalCache.Length<=index)
            Array.Resize(ref biologicalThermalCache,Mathf.NextPowerOfTwo(Mathf.Max(1,state.Count)));
        int node=grid.GetNodeIndex(cell,layer);
        ref var cached=ref biologicalThermalCache[index];
        if(cached.step!=biologicalThermalStep || cached.coarseEpoch!=temperatureGeneration
            || cached.node!=node || !cached.position.Equals(state.Position[index]))
        {
            cached.sample=SampleBiologicalTemperature(cell,layer,state.Position[index]);
            cached.step=biologicalThermalStep; cached.coarseEpoch=temperatureGeneration;
            cached.node=node; cached.position=state.Position[index];
        }
        return cached.sample;
    }

    private float GetBiologicalTemperature(int index,ReplicatorPopulationState state) =>
        GetAgentTemperatureSample(index,state).TemperatureKelvin;

    private void ClearBiologicalThermalCache()
    { biologicalThermalCache=Array.Empty<BiologicalThermalCache>(); biologicalThermalStep=1; }
}
