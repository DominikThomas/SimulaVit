using System;
using UnityEngine;

public readonly struct IcosphereDirectionSample
{
    public readonly int NearestCell;
    public readonly int NeighborStart;
    public readonly byte NeighborCount;

    public IcosphereDirectionSample(int nearestCell, int neighborStart, byte neighborCount)
    {
        NearestCell = nearestCell;
        NeighborStart = neighborStart;
        NeighborCount = neighborCount;
    }
}

public sealed class IcosphereDirectionMapping
{
    public const int Version = 1;
    public readonly int SimulationSubdivision;
    public readonly int TargetSubdivision;
    public readonly IcosphereDirectionSample[] Samples;
    public readonly int[] NeighborIndices;
    public readonly float[] NeighborWeights;
    public readonly bool UsedIdentityMapping;
    public readonly long CandidateCellsInspected;

    public IcosphereDirectionMapping(int simulationSubdivision, int targetSubdivision, IcosphereDirectionSample[] samples, int[] neighborIndices, float[] neighborWeights, bool usedIdentityMapping, long candidateCellsInspected)
    {
        SimulationSubdivision = simulationSubdivision;
        TargetSubdivision = targetSubdivision;
        Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        NeighborIndices = neighborIndices ?? throw new ArgumentNullException(nameof(neighborIndices));
        NeighborWeights = neighborWeights ?? throw new ArgumentNullException(nameof(neighborWeights));
        UsedIdentityMapping = usedIdentityMapping;
        CandidateCellsInspected = candidateCellsInspected;
    }

    public int SampleCount => Samples.Length;
    public long ApproximateManagedBytes => (long)Samples.Length * 12L + (long)NeighborIndices.Length * 4L + (long)NeighborWeights.Length * 4L;

    public float SampleSeafloorRadius(int sampleIndex, Vector3 direction, float rawRadius, float seaLevelRadius, bool[] oceanMask, float[] seafloorRadius)
    {
        if (rawRadius >= seaLevelRadius || oceanMask == null || seafloorRadius == null || sampleIndex < 0 || sampleIndex >= Samples.Length)
        {
            return rawRadius;
        }

        IcosphereDirectionSample sample = Samples[sampleIndex];
        int nearest = sample.NearestCell;
        if (nearest < 0 || nearest >= oceanMask.Length || !oceanMask[nearest])
        {
            return rawRadius;
        }

        float weighted = seafloorRadius[nearest];
        float weightSum = 1f;
        int end = sample.NeighborStart + sample.NeighborCount;
        for (int i = sample.NeighborStart; i < end; i++)
        {
            int neighbor = NeighborIndices[i];
            if (neighbor < 0 || neighbor >= seafloorRadius.Length || !oceanMask[neighbor]) continue;
            float weight = NeighborWeights[i];
            weighted += seafloorRadius[neighbor] * weight;
            weightSum += weight;
        }

        return Mathf.Min(rawRadius, weighted / Mathf.Max(0.0001f, weightSum));
    }
}
