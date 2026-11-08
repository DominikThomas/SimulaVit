using System;
using UnityEngine;

internal readonly struct GeodesicRiverWidthProfile
{
    private readonly double threshold, logRange;
    private readonly float baseWidth, widthRange;

    internal GeodesicRiverWidthProfile(double threshold, double majorThreshold, float baseWidth, float maximumMultiplier)
    {
        this.threshold = threshold;
        this.baseWidth = baseWidth;
        widthRange = Mathf.Max(1f, maximumMultiplier) - 1f;
        // The major-flow setting shifts the upper end without compressing every
        // trunk into the last size band when the two thresholds are close.
        logRange = Math.Log(32d * Math.Max(2d, majorThreshold / threshold));
    }

    internal float Width(double runoff)
    {
        if (!(runoff > threshold)) return baseWidth;
        float position = (float)(4 * Math.Log(runoff / threshold) / logRange);
        if (position >= 4f) return baseWidth * (1f + widthRange);
        // Five anchors at even intervals in log runoff. These fractions give
        // 1, 1.7, 2.7, 4 and 6 times the base width with the default cap.
        float fraction;
        if (position < 1f) fraction = Mathf.Lerp(0f, .14f, position);
        else if (position < 2f) fraction = Mathf.Lerp(.14f, .34f, position - 1f);
        else if (position < 3f) fraction = Mathf.Lerp(.34f, .60f, position - 2f);
        else fraction = Mathf.Lerp(.60f, 1f, position - 3f);
        return baseWidth * (1f + widthRange * fraction);
    }
}
