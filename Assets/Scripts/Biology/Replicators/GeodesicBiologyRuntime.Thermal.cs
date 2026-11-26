using System;
using UnityEngine;

internal enum LifecycleThermalSamplingMode
{
    Reference,
    Optimized
}

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
    private LifecycleThermalWork lifecycleThermalWork;

    internal LifecycleThermalSamplingMode ThermalSamplingMode { get; private set; }
    internal double LifecycleThermalLookupSetupMilliseconds =>
        ThermalSamplingMode == LifecycleThermalSamplingMode.Optimized && experiencedTemperature != null
            ? experiencedTemperature.LifecycleOceanLookupSetupMilliseconds : 0d;

    internal void SetLifecycleThermalSamplingMode(LifecycleThermalSamplingMode mode)
    {
        if (mode == LifecycleThermalSamplingMode.Optimized && experiencedTemperature != null)
            experiencedTemperature.PrepareLifecycleOceanVentLookup();
        ThermalSamplingMode = mode;
    }

    internal struct LifecycleThermalWork
    {
        internal long CacheHits, CacheMisses, CacheKeyInvalidations, Candidates, InfluenceEvaluations;

        internal void AddSince(in LifecycleThermalWork current, in LifecycleThermalWork previous)
        {
            CacheHits += Math.Max(0L, current.CacheHits - previous.CacheHits);
            CacheMisses += Math.Max(0L, current.CacheMisses - previous.CacheMisses);
            CacheKeyInvalidations += Math.Max(0L, current.CacheKeyInvalidations - previous.CacheKeyInvalidations);
            Candidates += Math.Max(0L, current.Candidates - previous.Candidates);
            InfluenceEvaluations += Math.Max(0L, current.InfluenceEvaluations - previous.InfluenceEvaluations);
        }
    }

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

    private float GetBiologicalTemperature(int index, ReplicatorPopulationState state,
        GeodesicExperiencedTemperatureField.LifecycleOceanVentLookup preparedVents = null, bool countWork = false)
    {
        int cell = state.GeodesicCellIndex[index], layer = state.CurrentOceanLayerIndex[index];
        if (!grid.IsNodeActive(cell, layer)) return float.NaN;
        int node = grid.GetNodeIndex(cell, layer);
        Vector3 position = state.Position[index];
        int start = 0, end = 0;
        bool hasCandidates = preparedVents != null ? preparedVents.TryGetCandidateRange(cell, out start, out end)
            : experiencedTemperature != null && experiencedTemperature.HasLocalInfluenceCandidates(cell, layer);
        if (!hasCandidates)
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
            int candidates = 0, evaluations = 0;
            if (!GeodesicBiologyDiagnostics.FinitePosition(position))
                cached.kelvin = float.NaN;
            else if (preparedVents != null)
            {
                cached.kelvin = preparedVents.SampleTemperature(start, end, position, background, countWork, out evaluations);
                candidates = evaluations;
            }
            else
                cached.kelvin = experiencedTemperature.SampleKnownOceanTemperature(cell, layer, position, background,
                    countWork, out candidates, out evaluations);

            if (countWork)
            {
                lifecycleThermalWork.CacheMisses++;
                if (cached.step == biologicalThermalStep) lifecycleThermalWork.CacheKeyInvalidations++;
                lifecycleThermalWork.Candidates += candidates;
                lifecycleThermalWork.InfluenceEvaluations += evaluations;
            }
            cached.step = biologicalThermalStep;
            cached.coarseEpoch = temperatureGeneration;
            cached.node = node;
            cached.position = position;
        }
        else if (countWork)
            lifecycleThermalWork.CacheHits++;
        return cached.kelvin;
    }

    private void ClearBiologicalThermalCache()
    {
        biologicalThermalCache = Array.Empty<BiologicalThermalCache>();
        biologicalThermalStep = 1;
        ThermalSamplingMode = LifecycleThermalSamplingMode.Reference;
        lifecycleThermalWork = default;
    }
}
