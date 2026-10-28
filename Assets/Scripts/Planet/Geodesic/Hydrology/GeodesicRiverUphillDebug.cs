using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public enum RiverDebugRejection { HydrologyGrade, VisibleGrade, CentrelineGrade, ProjectionGrade }

/// <summary>Observation only. Never inserted into networks, mouths or accumulation.</summary>
public sealed class RiverUphillDebugReach
{
    public int Source, Receiver;
    public RiverDebugRejection Reason;
    public Vector3[] Path;
    public double MaximumStep, TotalRise, MaximumSlope;
}

public static class GeodesicRiverUphillDebug
{
    // Dimensionless local rise / surface distance; fixed across seeds and planet sizes.
    public const double YellowSlope = .0001, OrangeSlope = .01, RedSlope = .1;
    public static Color SeverityColor(double slope)
    {
        var orange = new Color(1f, .5f, 0f, 1f);
        if (slope <= YellowSlope) return Color.yellow;
        if (slope >= RedSlope) return Color.red;
        return slope <= OrangeSlope
            ? Color.Lerp(Color.yellow, orange, (float)(Math.Log(slope / YellowSlope) / Math.Log(OrangeSlope / YellowSlope)))
            : Color.Lerp(orange, Color.red, (float)(Math.Log(slope / OrangeSlope) / Math.Log(RedSlope / OrangeSlope)));
    }

    public static void Measure(RiverUphillDebugReach reach, IReadOnlyList<Vector3> points,
        IReadOnlyList<double> heights, double radius, double epsilon)
    {
        double step = 0, total = 0, slope = 0, risingDistance = 0, risingHeight = 0;
        for (int i = 1; i < points.Count; i++)
        {
            double rise = Math.Max(0, heights[i] - heights[i - 1]);
            if (double.IsNaN(rise) || double.IsInfinity(rise)) continue;
            step = Math.Max(step, rise); total += rise;
            // atan2 is stable for very short arcs, unlike float acos(dot).
            Vector3 a = points[i - 1].normalized, b = points[i].normalized;
            double distance = radius * Math.Atan2(Vector3.Cross(a, b).magnitude, Vector3.Dot(a, b));
            if (rise > epsilon && distance > radius * 1e-9) slope = Math.Max(slope, rise / distance);
            if (heights[i] < heights[i - 1]) { risingDistance = risingHeight = 0; }
            else
            {
                risingDistance += distance; risingHeight += rise;
                if (risingHeight > epsilon && risingDistance > radius * 1e-9)
                    slope = Math.Max(slope, risingHeight / risingDistance);
            }
        }
        reach.MaximumStep = Math.Max(reach.MaximumStep, step);
        reach.TotalRise = Math.Max(reach.TotalRise, total);
        reach.MaximumSlope = Math.Max(reach.MaximumSlope, slope);
    }

    public static string Summary(IReadOnlyList<RiverUphillDebugReach> reaches, int vertices, double milliseconds)
    {
        double[] deltas = reaches.Select(r => r.MaximumStep).OrderBy(v => v).ToArray();
        double[] slopes = reaches.Select(r => r.MaximumSlope).OrderBy(v => v).ToArray();
        double Median(double[] v) => v.Length == 0 ? 0 : (v[(v.Length - 1) / 2] + v[v.Length / 2]) * .5;
        return string.Format(CultureInfo.InvariantCulture,
            "[GeodesicRiverUphillDebug] enabled=True rejectedUphillReaches={0} yellow={1} orange={2} red={3} maxUphillDelta={4:G9} medianUphillDelta={5:G9} maxUphillSlope={6:G9} medianUphillSlope={7:G9} debugVertices={8} overlayMs={9:F2} maxAccumulatedUphillRise={10:G9} retainedPathPoints={11} slopeAnchors=0.0001/0.01/0.1 bins=<0.001/<0.0316228/remaining",
            reaches.Count, slopes.Count(s => s < .001), slopes.Count(s => s >= .001 && s < Math.Sqrt(OrangeSlope * RedSlope)),
            slopes.Count(s => s >= Math.Sqrt(OrangeSlope * RedSlope)), deltas.LastOrDefault(), Median(deltas), slopes.LastOrDefault(), Median(slopes), vertices, milliseconds, reaches.Count == 0 ? 0 : reaches.Max(r => r.TotalRise), reaches.Sum(r => r.Path.Length));
    }
}
