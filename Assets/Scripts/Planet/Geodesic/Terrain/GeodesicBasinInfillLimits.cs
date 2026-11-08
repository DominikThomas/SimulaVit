using System;
using UnityEngine;

[Serializable]
public struct GeodesicBasinInfillLimits
{
    public const float MaximumTotalArea = .30f;
    public const float MaximumLiftFraction = .75f;
    public const float MaximumGrade = 1f;

    [Range(0f, MaximumTotalArea)] public float totalArea;
    [Range(0f, MaximumLiftFraction)] public float liftFraction;
    [Range(0f, MaximumGrade)] public float transitionGrade;

    public static GeodesicBasinInfillLimits Defaults => new GeodesicBasinInfillLimits
    {
        totalArea = .08f, liftFraction = .20f, transitionGrade = .35f
    };

    public GeodesicBasinInfillLimits Normalized()
    {
        var defaults = Defaults;
        return new GeodesicBasinInfillLimits
        {
            totalArea = Bound(totalArea, defaults.totalArea, MaximumTotalArea),
            liftFraction = Bound(liftFraction, defaults.liftFraction, MaximumLiftFraction),
            transitionGrade = Bound(transitionGrade, defaults.transitionGrade, MaximumGrade)
        };
    }

    private static float Bound(float value, float fallback, float maximum) =>
        float.IsFinite(value) ? Mathf.Clamp(value, 0f, maximum) : fallback;
}
