using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public enum RiverVisibleGrade { Downhill, Flat, SlightUphill, ModerateUphill, StrongUphill }

public sealed class RiverVisibleGradeSample
{
    public int SourceTriangle, RenderedSegment;
    public Vector3 Start, End;
    public double RoutingDeltaH, VisibleDeltaH, Distance;
    public double RoutingSlope => Distance > 0 ? RoutingDeltaH / Distance : 0;
    public double VisibleSlope => Distance > 0 ? VisibleDeltaH / Distance : 0;
    public RiverVisibleGrade Grade;
}

public sealed class RiverRenderedGradeSegment
{
    public int FirstIndex, IndexCount, FirstSample, SampleCount;
    public RiverVisibleGrade Grade;
    public double MaximumUphillSlope;
}

public readonly struct RiverTerminationDebugPoint
{
    public readonly int SourceTriangle;
    public readonly TinTermination Reason;
    public readonly Vector3 Position;
    public RiverTerminationDebugPoint(int source, TinTermination reason, Vector3 position)
    { SourceTriangle = source; Reason = reason; Position = position; }
}

/// <summary>Read-only audit of emitted centreline vertices. No router acceptance/tolerance input.</summary>
public sealed class GeodesicRiverVisibleGradeAudit
{
    public readonly List<RiverVisibleGradeSample> Samples = new List<RiverVisibleGradeSample>();
    public readonly List<RiverRenderedGradeSegment> Segments = new List<RiverRenderedGradeSegment>();
    private readonly List<double> samplingNoise = new List<double>();
    public double Epsilon { get; private set; }
    public double SamplingNoiseP95 { get; private set; }
    public double TotalLength { get; private set; }
    public double UphillLength { get; private set; }
    public double UphillLengthPercent => TotalLength > 0 ? 100 * UphillLength / TotalLength : 0;
    public int RoutingValidButVisibleUphill { get; private set; }
    public int RoutingDownhillVisibleSlight, RoutingDownhillVisibleStrong, RoutingFlatVisibleUphill;
    public int ClosedBasinTerminations, UnexpectedTerminations, ClosedBasinMarkers, UnexpectedMarkers;

    public static double SurfaceDistance(Vector3 a, Vector3 b, double radius)
    {
        a.Normalize(); b.Normalize();
        return radius * Math.Atan2(Vector3.Cross(a, b).magnitude, Vector3.Dot(a, b));
    }

    public static RiverVisibleGrade Classify(double delta, double distance, double epsilon)
    {
        if (distance <= 0 || Math.Abs(delta) <= epsilon) return RiverVisibleGrade.Flat;
        if (delta < 0) return RiverVisibleGrade.Downhill;
        double slope = delta / distance;
        return slope < .001 ? RiverVisibleGrade.SlightUphill : slope < Math.Sqrt(.01 * .1)
            ? RiverVisibleGrade.ModerateUphill : RiverVisibleGrade.StrongUphill;
    }

    public void ObserveEmittedPath(int source, List<Vector3> vertices, int firstVertex, int pointCount,
        bool rawLines, int firstIndex, GeodesicRiverTerrain terrain, double radius)
    {
        var points = new List<Vector3>(); var heights = new List<double>();
        int stride = rawLines ? 1 : 3, centre = rawLines ? 0 : 1, indexStride = rawLines ? 2 : 12;
        for (int i = 1; i < pointCount; i++)
        {
            // Read positions AFTER multiplication/projection into the actual renderer mesh.
            Vector3 a = vertices[firstVertex + (i - 1) * stride + centre].normalized;
            Vector3 b = vertices[firstVertex + i * stride + centre].normalized;
            points.Clear(); heights.Clear();
            terrain.SampleCompletedVisiblePath(new[] { a, b }, points, heights);
            var segment = new RiverRenderedGradeSegment { FirstIndex = firstIndex + (i - 1) * indexStride,
                IndexCount = indexStride, FirstSample = Samples.Count };
            for (int j = 0; j < points.Count; j++)
            {
                // Empirical float-projection error against the double-precision completed facet.
                samplingNoise.Add(Math.Abs(terrain.Radius(points[j]) - heights[j]));
                if (j == 0) continue;
                Samples.Add(new RiverVisibleGradeSample { SourceTriangle = source, RenderedSegment = Segments.Count,
                    Start = points[j - 1], End = points[j], VisibleDeltaH = heights[j] - heights[j - 1],
                    RoutingDeltaH = terrain.InterpolationReference(points[j], false) - terrain.InterpolationReference(points[j - 1], false),
                    Distance = SurfaceDistance(points[j - 1], points[j], radius) });
            }
            segment.SampleCount = Samples.Count - segment.FirstSample; Segments.Add(segment);
        }
    }

    public void Resolve(double radius)
    {
        samplingNoise.Sort();
        SamplingNoiseP95 = samplingNoise.Count == 0 ? 0 : samplingNoise[(int)Math.Floor((samplingNoise.Count - 1) * .95)];
        float r = (float)radius;
        int bits = BitConverter.ToInt32(BitConverter.GetBytes(r), 0);
        double ulp = BitConverter.ToSingle(BitConverter.GetBytes(bits + 1), 0) - (double)r;
        // Two measured projection-error units, floored at two float radius ULPs.
        // No visibleMicroBumpFraction or hydrological tolerance participates in this test.
        Epsilon = Math.Max(2 * ulp, 2 * SamplingNoiseP95);
        TotalLength = UphillLength = 0; RoutingValidButVisibleUphill = 0;
        RoutingDownhillVisibleSlight = RoutingDownhillVisibleStrong = RoutingFlatVisibleUphill = 0;
        foreach (var sample in Samples)
        {
            sample.Grade = Classify(sample.VisibleDeltaH, sample.Distance, Epsilon);
            TotalLength += sample.Distance;
            if (sample.Grade >= RiverVisibleGrade.SlightUphill) UphillLength += sample.Distance;
        }
        foreach (var segment in Segments)
        {
            segment.Grade = RiverVisibleGrade.Flat; segment.MaximumUphillSlope = 0;
            bool mismatch = false, slight = false, strong = false, flat = false, downhill = false;
            for (int i = segment.FirstSample; i < segment.FirstSample + segment.SampleCount; i++)
            {
                var sample = Samples[i]; downhill |= sample.Grade == RiverVisibleGrade.Downhill;
                if (sample.Grade < RiverVisibleGrade.SlightUphill) continue;
                if (sample.Grade > segment.Grade) segment.Grade = sample.Grade;
                segment.MaximumUphillSlope = Math.Max(segment.MaximumUphillSlope, sample.VisibleSlope);
                mismatch |= sample.RoutingDeltaH <= Epsilon;
                flat |= Math.Abs(sample.RoutingDeltaH) <= Epsilon;
                slight |= sample.RoutingDeltaH < -Epsilon && sample.Grade == RiverVisibleGrade.SlightUphill;
                strong |= sample.RoutingDeltaH < -Epsilon && sample.Grade == RiverVisibleGrade.StrongUphill;
            }
            if (segment.Grade == RiverVisibleGrade.Flat && downhill) segment.Grade = RiverVisibleGrade.Downhill;
            if (mismatch) RoutingValidButVisibleUphill++;
            if (slight) RoutingDownhillVisibleSlight++;
            if (strong) RoutingDownhillVisibleStrong++;
            if (flat) RoutingFlatVisibleUphill++;
        }
    }

    public void ApplyColours(List<Vector3> vertices, List<int> indices, List<Color> colors)
    {
        foreach (var segment in Segments)
        {
            if (segment.Grade < RiverVisibleGrade.SlightUphill) continue;
            Color color = GeodesicRiverUphillDebug.SeverityColor(segment.MaximumUphillSlope);
            var copied = new Dictionary<int, int>();
            for (int i = segment.FirstIndex; i < segment.FirstIndex + segment.IndexCount; i++)
            {
                int original = indices[i];
                if (!copied.TryGetValue(original, out int duplicate))
                {
                    duplicate = vertices.Count; copied.Add(original, duplicate);
                    vertices.Add(vertices[original]); colors.Add(color);
                }
                indices[i] = duplicate;
            }
        }
    }

    public string Summary() => string.Format(CultureInfo.InvariantCulture,
        "[GeodesicRiverVisibleGradeAudit] renderedSegments={0} sampledIntervals={1} visibleDownhill={2} visibleFlat={3} visibleSlightUphill={4} visibleModerateUphill={5} visibleStrongUphill={6} maxVisibleUphillSlope={7:G9} maxVisibleUphillDelta={8:G9} routingValidButVisibleUphill={9} uphillRenderedLengthPercent={10:F6} epsilon={11:G9} samplingNoiseP95={12:G9} closedBasinTerminations={13} unexpectedTerminations={14} closedBasinMarkers={15} unexpectedMarkers={16} routingDownhillVisibleSlight={17} routingDownhillVisibleStrong={18} routingFlatVisibleUphill={19}",
        Segments.Count, Samples.Count, Segments.Count(s => s.Grade == RiverVisibleGrade.Downhill), Segments.Count(s => s.Grade == RiverVisibleGrade.Flat),
        Segments.Count(s => s.Grade == RiverVisibleGrade.SlightUphill), Segments.Count(s => s.Grade == RiverVisibleGrade.ModerateUphill),
        Segments.Count(s => s.Grade == RiverVisibleGrade.StrongUphill), Segments.Count == 0 ? 0 : Segments.Max(s => s.MaximumUphillSlope),
        Samples.Count == 0 ? 0 : Samples.Max(s => Math.Max(0, s.VisibleDeltaH)), RoutingValidButVisibleUphill, UphillLengthPercent, Epsilon,
        SamplingNoiseP95, ClosedBasinTerminations, UnexpectedTerminations, ClosedBasinMarkers, UnexpectedMarkers,
        RoutingDownhillVisibleSlight, RoutingDownhillVisibleStrong, RoutingFlatVisibleUphill);
}
