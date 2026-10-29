using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>Generation-time land distance, independent of river/lake state and render resolution.
/// The post-filter retained ocean mask is the only coastline authority.</summary>
public sealed class GeodesicInlandUplift
{
    public const float MaximumMagnitude = .05f;
    public const float DefaultScaleDegrees = 30f;
    public const string Help = "Adds a broad continental-scale elevation bias that increases with distance from the retained-ocean coastline. Higher values raise continental interiors relative to coasts. Experimental.";
    public const string CoastlineAuthority = "post-filter retained-ocean mask; coastal land cell centres";
    private readonly GeodesicGridTopology topology;
    private readonly bool[] ocean;
    private readonly float[] distances;
    public float ScaleRadians { get; }
    public bool HasCoast { get; }

    public static float NormalizeMagnitude(float value) => float.IsNaN(value) || float.IsInfinity(value)
        ? 0f : Mathf.Clamp(value, 0f, MaximumMagnitude);

    public GeodesicInlandUplift(GeodesicGridTopology topology, bool[] retainedOcean,
        float scaleDegrees = DefaultScaleDegrees)
    {
        this.topology = topology ?? throw new ArgumentNullException(nameof(topology));
        if (retainedOcean == null || retainedOcean.Length != topology.CellCount)
            throw new ArgumentException("Retained ocean count must match topology.", nameof(retainedOcean));
        if (float.IsNaN(scaleDegrees) || float.IsInfinity(scaleDegrees) || scaleDegrees <= 0f)
            throw new ArgumentOutOfRangeException(nameof(scaleDegrees));
        ScaleRadians = scaleDegrees * Mathf.Deg2Rad;
        ocean = (bool[])retainedOcean.Clone();
        distances = new float[topology.CellCount];
        var queue = new DistanceHeap();
        for (int cell = 0; cell < distances.Length; cell++)
        {
            distances[cell] = ocean[cell] ? 0f : float.PositiveInfinity;
            if (ocean[cell]) continue;
            for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
            {
                if (!ocean[topology.Neighbors6[cell * 6 + slot]]) continue;
                // A conservative zero-width-height coastal band, also covering all mixed triangles.
                distances[cell] = 0f;
                queue.Push(cell, 0f);
                HasCoast = true;
                break;
            }
        }
        while (queue.TryPop(out int cell, out float distance))
        {
            if (distance > distances[cell]) continue;
            for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
            {
                int edge = cell * 6 + slot, neighbor = topology.Neighbors6[edge];
                if (ocean[neighbor]) continue;
                float next = distance + topology.NeighborAngularDistances6[edge];
                if (next >= distances[neighbor]) continue;
                distances[neighbor] = next;
                queue.Push(neighbor, next);
            }
        }
        // No coastline (dry world, all ocean, or all seas excluded): there is no inland reference.
        for (int cell = 0; cell < distances.Length; cell++)
            if (float.IsPositiveInfinity(distances[cell])) distances[cell] = 0f;
    }

    public float CellDistanceRadians(int cell) => distances[cell];
    public bool IsOcean(int cell) => ocean[cell];

    public static float Offset(float distanceRadians, float magnitude, float scaleRadians)
    {
        magnitude = NormalizeMagnitude(magnitude);
        if (magnitude == 0f || distanceRadians <= 0f || scaleRadians <= 0f) return 0f;
        float t = Mathf.Clamp01(distanceRadians / scaleRadians);
        return magnitude * (t * t * t * (t * (t * 6f - 15f) + 10f));
    }

    public float CellOffset(int cell, float magnitude) => Offset(distances[cell], magnitude, ScaleRadians);

    /// <summary>Piecewise linear distance on simulation triangles, then smootherstep.
    /// Unlike nearest-cell or inverse-distance weights this is continuous at cell/triangle edges.
    /// Neighbors are cyclically sorted by GeodesicGridTopology.</summary>
    public float SampleDistanceRadians(Vector3 direction, int nearestCell = -1)
    {
        if (!HasCoast) return 0f;
        Vector3 d = direction.sqrMagnitude > 1e-10f ? direction.normalized : Vector3.up;
        int nearest = GeodesicOceanConnectivity.FindNearestCell(topology, d, nearestCell);
        if (ocean[nearest]) return 0f;
        Vector3 a = topology.CellDirections[nearest];
        if ((d - a).sqrMagnitude < 1e-12f) return distances[nearest];
        int count = topology.NeighborCounts[nearest];
        for (int slot = 0; slot < count; slot++)
        {
            int b = topology.Neighbors6[nearest * 6 + slot];
            int c = topology.Neighbors6[nearest * 6 + (slot + 1) % count];
            Vector3 vb = topology.CellDirections[b], vc = topology.CellDirections[c];
            float determinant = Vector3.Dot(a, Vector3.Cross(vb, vc));
            float wa = Vector3.Dot(d, Vector3.Cross(vb, vc)) / determinant;
            float wb = Vector3.Dot(a, Vector3.Cross(d, vc)) / determinant;
            float wc = Vector3.Dot(a, Vector3.Cross(vb, d)) / determinant;
            if (wa < -1e-5f || wb < -1e-5f || wc < -1e-5f) continue;
            wa = Mathf.Max(0f, wa); wb = Mathf.Max(0f, wb); wc = Mathf.Max(0f, wc);
            return (wa * distances[nearest] + wb * distances[b] + wc * distances[c]) / (wa + wb + wc);
        }
        throw new InvalidOperationException("Inland distance sample was outside the nearest cell triangle fan.");
    }

    public float SampleOffset(Vector3 direction, float magnitude, int nearestCell = -1) =>
        NormalizeMagnitude(magnitude) == 0f ? 0f : Offset(SampleDistanceRadians(direction, nearestCell), magnitude, ScaleRadians);

    // Explicit no-op avoids even a floating-point addition on the disabled terrain path.
    public static float Add(float baseline, float offset) => offset == 0f ? baseline : baseline + offset;

    private sealed class DistanceHeap
    {
        private readonly List<(int cell, float distance)> entries = new();
        private static bool Less((int cell, float distance) a, (int cell, float distance) b) =>
            a.distance < b.distance || (a.distance == b.distance && a.cell < b.cell);
        public void Push(int cell, float distance)
        {
            entries.Add((cell, distance));
            int i = entries.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Less(entries[i], entries[parent])) break;
                (entries[i], entries[parent]) = (entries[parent], entries[i]); i = parent;
            }
        }
        public bool TryPop(out int cell, out float distance)
        {
            if (entries.Count == 0) { cell = -1; distance = 0f; return false; }
            (cell, distance) = entries[0];
            int last = entries.Count - 1;
            entries[0] = entries[last]; entries.RemoveAt(last);
            int i = 0;
            while (i * 2 + 1 < entries.Count)
            {
                int child = i * 2 + 1;
                if (child + 1 < entries.Count && Less(entries[child + 1], entries[child])) child++;
                if (!Less(entries[child], entries[i])) break;
                (entries[i], entries[child]) = (entries[child], entries[i]); i = child;
            }
            return true;
        }
    }

    /// <summary>One aggregate report for the rendered land vertices; no second terrain generation.</summary>
    public sealed class Diagnostics
    {
        private readonly List<(float distance, float uplift)> land = new();
        private float maximumDifference;
        public void Add(bool retainedOcean, float distance, float baselineRadius, float finalRadius)
        {
            float difference = Mathf.Abs(finalRadius - baselineRadius);
            maximumDifference = Mathf.Max(maximumDifference, difference);
            if (!retainedOcean) land.Add((distance, finalRadius - baselineRadius));
        }
        public string Describe(float magnitude, float scaleRadians, bool hasCoast)
        {
            land.Sort((a, b) => a.distance.CompareTo(b.distance));
            double sumDistance = 0, sumUplift = 0, nearSum = 0, interiorSum = 0;
            float minUplift = float.PositiveInfinity, maxUplift = 0;
            int affected = 0, nearCount = 0, interiorStart = land.Count * 3 / 4;
            for (int i = 0; i < land.Count; i++)
            {
                var sample = land[i];
                sumDistance += sample.distance; sumUplift += sample.uplift;
                minUplift = Mathf.Min(minUplift, sample.uplift); maxUplift = Mathf.Max(maxUplift, sample.uplift);
                if (sample.uplift > 0f) affected++;
                if (sample.distance <= scaleRadians * .1f) { nearSum += sample.uplift; nearCount++; }
                if (i >= interiorStart) interiorSum += sample.uplift;
            }
            int n = land.Count;
            return string.Format(CultureInfo.InvariantCulture,
                "[GeodesicInlandUplift] magnitude={0:G9}, coastlineAuthority={1}, scaleDegrees={2:F3}, hasCoast={3}, landVertices={4}, affected={5}, inlandDistanceDegreesMinMeanMax={6:F6}/{7:F6}/{8:F6}, appliedUpliftMinMeanMax={9:G9}/{10:G9}/{11:G9}, nearCoastMeanUplift={12:G9}, nearCoastLimitDegrees={13:F3}, interiorQuartileMeanUplift={14:G9}, maximumFinalTerrainDifference={15:G9}",
                magnitude, CoastlineAuthority, scaleRadians * Mathf.Rad2Deg, hasCoast, n, affected,
                n > 0 ? land[0].distance * Mathf.Rad2Deg : 0f, sumDistance / Math.Max(1, n) * Mathf.Rad2Deg,
                n > 0 ? land[n - 1].distance * Mathf.Rad2Deg : 0f, n > 0 ? minUplift : 0f,
                sumUplift / Math.Max(1, n), maxUplift, nearSum / Math.Max(1, nearCount),
                scaleRadians * Mathf.Rad2Deg * .1f, interiorSum / Math.Max(1, n - interiorStart), maximumDifference);
        }
    }
}
