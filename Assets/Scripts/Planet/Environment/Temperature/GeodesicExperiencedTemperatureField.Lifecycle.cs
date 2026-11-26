using System.Diagnostics;
using UnityEngine;

public sealed partial class GeodesicExperiencedTemperatureField
{
    private LifecycleOceanVentLookup lifecycleOceanVents;
    internal double LifecycleOceanLookupSetupMilliseconds { get; private set; }

    internal void PrepareLifecycleOceanVentLookup()
    {
        if (lifecycleOceanVents != null || !IsInitialized) return;
        long started = Stopwatch.GetTimestamp();
        lifecycleOceanVents = new LifecycleOceanVentLookup(this);
        LifecycleOceanLookupSetupMilliseconds = (Stopwatch.GetTimestamp() - started) * (1000d / Stopwatch.Frequency);
    }

    internal LifecycleOceanVentLookup GetLifecycleOceanVentLookup(GeodesicOceanLayerGrid grid) =>
        lifecycleOceanVents != null && ReferenceEquals(lifecycleOceanVents.Grid, grid)
        && IsInitialized && ReferenceEquals(resources.SourceGrid, grid) ? lifecycleOceanVents : null;

    // A world-scoped view of the existing broad phase and fixed submarine column inputs.
    // All active layers in an ocean cell are eligible for the same candidate range.
    internal sealed class LifecycleOceanVentLookup
    {
        private readonly GeodesicExperiencedTemperatureField field;
        private readonly Transform planetTransform;
        private readonly int[] offsets, indices;
        private readonly Column[] columns;
        internal readonly GeodesicOceanLayerGrid Grid;

        internal LifecycleOceanVentLookup(GeodesicExperiencedTemperatureField field)
        {
            this.field = field;
            planetTransform = field.transform;
            Grid = field.resources.SourceGrid;
            var sourceOutlets = field.outlets;
            var sourceOffsets = field.cellOffsets;
            var sourceIndices = field.outletIndices;
            columns = new Column[sourceOutlets.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                ref readonly var outlet = ref sourceOutlets[i];
                if (outlet.Habitat == GeodesicVentHabitat.Submarine)
                    columns[i] = new Column(outlet, field.outletColumnTopRadius[i]);
            }

            offsets = new int[sourceOffsets.Length];
            int total = 0;
            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (Grid.ActiveLayerCountByCell[cell] > 0)
                    for (int cursor = sourceOffsets[cell]; cursor < sourceOffsets[cell + 1]; cursor++)
                        if (sourceOutlets[sourceIndices[cursor]].Habitat == GeodesicVentHabitat.Submarine) total++;
                offsets[cell + 1] = total;
            }

            indices = new int[total];
            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                int write = offsets[cell];
                if (write == offsets[cell + 1]) continue;
                for (int cursor = sourceOffsets[cell]; cursor < sourceOffsets[cell + 1]; cursor++)
                {
                    int outlet = sourceIndices[cursor];
                    if (sourceOutlets[outlet].Habitat == GeodesicVentHabitat.Submarine) indices[write++] = outlet;
                }
            }
        }

        internal bool TryGetCandidateRange(int cell, out int start, out int end)
        {
            start = offsets[cell];
            end = offsets[cell + 1];
            return start < end;
        }

        internal float SampleTemperature(int start, int end, Vector3 worldPosition, float coarseKelvin,
            bool countWork, out int evaluations)
        {
            evaluations = 0;
            if (!float.IsFinite(coarseKelvin)) return coarseKelvin;
            Vector3 local = planetTransform.InverseTransformPoint(worldPosition);
            if (!float.IsFinite(local.x) || !float.IsFinite(local.y) || !float.IsFinite(local.z)) return coarseKelvin;
            if (countWork) evaluations = end - start;

            float falloff = field.ventMicrothermalFalloffDistance;
            bool validFalloff = float.IsFinite(falloff) && falloff > 0f;
            float strongest = 0f;
            bool hasFiniteDistance = false;
            for (int cursor = start; cursor < end; cursor++)
            {
                ref readonly var column = ref columns[indices[cursor]];
                float axial = Vector3.Dot(local, column.Normal);
                float reference = Mathf.Clamp(axial, column.AnchorRadius, column.TopRadius);
                float distance = (local - column.Normal * reference).magnitude;
                if (!float.IsFinite(distance)) continue;
                hasFiniteDistance = true;
                if (!validFalloff || !float.IsFinite(column.BoundedStrength)) continue;
                float influence = GeodesicVentThermalModel.EvaluateBoundedInfluence(
                    distance, column.CoreRadius, falloff, column.BoundedStrength);
                if (influence > strongest) strongest = influence;
            }

            // A finite nearest candidate still goes through the reference zero-influence blend.
            return hasFiniteDistance ? field.BlendOceanTemperature(coarseKelvin, strongest) : coarseKelvin;
        }

        private readonly struct Column
        {
            internal readonly Vector3 Normal;
            internal readonly float AnchorRadius, TopRadius, CoreRadius, BoundedStrength;

            internal Column(in GeodesicVentOutlet outlet, float topRadius)
            {
                Normal = outlet.PlanetLocalNormal;
                AnchorRadius = outlet.PlanetLocalPosition.magnitude;
                TopRadius = topRadius;
                CoreRadius = outlet.HotCoreRadius;
                BoundedStrength = float.IsFinite(CoreRadius) && CoreRadius >= 0f && float.IsFinite(outlet.Strength01)
                    ? Mathf.Sqrt(Mathf.Clamp01(outlet.Strength01)) : float.NaN;
            }
        }
    }
}
