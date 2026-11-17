using System;
using System.Collections.Generic;

// Production mutation decisions only: no organisms, resources, movement or selection.
public static class LocomotionMutationExperiment
{
    public sealed class Row
    {
        public string configuration, habitat, parent;
        public int seed, attempts;
        public float mutation, upgrade, anchor, release;
        public bool allowRelease, mutationOverride;
        public long[] observed;
        public double[] probability, expected, standardDeviation, zScore;
    }

    public static Row[] Run(int attempts = 1000000)
    {
        var result = new List<Row>();
        for (int configuration = 0; configuration < 3; configuration++)
        {
            var settings = new GeodesicLocomotionMutationPolicy.Settings {
                mutation = configuration == 0 ? .06f : .20f, upgrade = .05f,
                anchor = configuration == 1 ? .001f : .015f, release = .01f, allowRelease = true };
            foreach (bool bottom in new[] { true, false })
            {
                if (configuration == 2 && !bottom) continue;
                double[] matrix = GeodesicLocomotionMutationPolicy.TransitionMatrix(settings, bottom);
                for (int parent = 0; parent < 4; parent++)
                {
                    int seed = 283 + parent + (bottom ? 100 : 0);
                    var random = new Random(seed);
                    var row = new Row { configuration = configuration == 0 ? "previous" : configuration == 1 ? "latest" : "anchor-only-comparison",
                        habitat = bottom ? "active-bottom" : "not-bottom", parent = ((LocomotionType)parent).ToString(), seed = seed, attempts = attempts,
                        mutation = settings.mutation, upgrade = settings.upgrade, anchor = settings.anchor, release = settings.release,
                        allowRelease = settings.allowRelease, mutationOverride = settings.overrideMutation,
                        observed = new long[4], probability = new double[4], expected = new double[4], standardDeviation = new double[4], zScore = new double[4] };
                    for (int i = 0; i < attempts; i++) row.observed[(int)GeodesicLocomotionMutationPolicy.Sample((LocomotionType)parent, bottom, settings, random).target]++;
                    for (int child = 0; child < 4; child++)
                    {
                        double p = matrix[parent * 4 + child]; row.probability[child] = p;
                        row.expected[child] = attempts * p; row.standardDeviation[child] = Math.Sqrt(attempts * p * (1d - p));
                        row.zScore[child] = row.standardDeviation[child] > 0d ? (row.observed[child] - row.expected[child]) / row.standardDeviation[child] : 0d;
                        if (Math.Abs(row.observed[child] - row.expected[child]) > 6d * row.standardDeviation[child] + 2d)
                            throw new InvalidOperationException("Mutation count outside predeclared six-sigma plus two-count bound.");
                    }
                    result.Add(row);
                }
            }
        }
        return result.ToArray();
    }

    public sealed class Geometry
    {
        public int subdivision, cells;
        public double minimumCenterKm, meanCenterKm, maximumCenterKm;
        public double amoeboidKmPerSecond, flagellarKmPerSecond, amoeboidCenterToEdgeSeconds;
    }

    public static Geometry MeasureGeometry()
    {
        var topology = GeodesicGridTopology.Build(6);
        double min = double.PositiveInfinity, max = 0d, sum = 0d; int count = 0;
        for (int i = 0; i < topology.CellCount; i++)
        for (int slot = 0; slot < topology.NeighborCounts[i]; slot++)
        {
            double distance = topology.NeighborAngularDistances6[i * 6 + slot] * 8000d;
            min = Math.Min(min, distance); max = Math.Max(max, distance); sum += distance; count++;
        }
        return new Geometry { subdivision = 6, cells = topology.CellCount, minimumCenterKm = min, meanCenterKm = sum / count,
            maximumCenterKm = max, amoeboidKmPerSecond = .002d * 2d * 1.2d * 8000d,
            flagellarKmPerSecond = .002d * 2d * 2d * 8000d,
            amoeboidCenterToEdgeSeconds = sum / count * .5d / (.002d * 2d * 1.2d * 8000d) };
    }
}
