using System;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private struct BiologicalThermalCache
    {
        public int step, coarseEpoch, node;
        public Vector3 position;
        public float kelvin;
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

    // Inspection and periodic reports request the full outlet and distance readout.
    private GeodesicVentTemperatureSample GetAgentTemperatureSample(int index, ReplicatorPopulationState state)
    {
        return SampleBiologicalTemperature(state.GeodesicCellIndex[index], state.CurrentOceanLayerIndex[index],
            state.Position[index]);
    }

    private float GetBiologicalTemperature(int index, ReplicatorPopulationState state)
    {
        int cell = state.GeodesicCellIndex[index], layer = state.CurrentOceanLayerIndex[index];
        if (!grid.IsNodeActive(cell, layer)) return float.NaN;
        int node = grid.GetNodeIndex(cell, layer);
        Vector3 position = state.Position[index];
        if (experiencedTemperature == null || !experiencedTemperature.HasLocalInfluenceCandidates(cell, layer))
        {
            float background = GetHabitatTemperatureAtNode(node, cell, layer);
            return GeodesicBiologyDiagnostics.FinitePosition(position) ? background : float.NaN;
        }

        if (biologicalThermalCache.Length <= index)
            Array.Resize(ref biologicalThermalCache, Mathf.NextPowerOfTwo(Mathf.Max(1, state.Count)));
        ref var cached = ref biologicalThermalCache[index];
        if (cached.step != biologicalThermalStep || cached.coarseEpoch != temperatureGeneration
            || cached.node != node || !cached.position.Equals(position))
        {
            float background = GetHabitatTemperatureAtNode(node, cell, layer);
            cached.kelvin = !GeodesicBiologyDiagnostics.FinitePosition(position) ? float.NaN
                : experiencedTemperature.SampleKnownOceanTemperature(cell, layer, position, background);
            cached.step = biologicalThermalStep;
            cached.coarseEpoch = temperatureGeneration;
            cached.node = node;
            cached.position = position;
        }
        return cached.kelvin;
    }

    private void ClearBiologicalThermalCache()
    { biologicalThermalCache=Array.Empty<BiologicalThermalCache>(); biologicalThermalStep=1; }
}
