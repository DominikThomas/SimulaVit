using System;
using UnityEngine;

[Serializable]
public struct GeodesicDepressionRepairLimits
{
    public const float MaximumComponentArea = .00025f;
    public const float MaximumAffectedArea = .001f;
    public const float MaximumTotalArea = .002f;
    public const float MaximumHeight = .08f;

    [Range(0f, MaximumComponentArea)] public float componentArea;
    [Range(0f, MaximumHeight)] public float fillDepth;
    [Range(0f, MaximumAffectedArea)] public float affectedArea;
    [Range(0f, MaximumHeight)] public float localAdjustment;
    [Range(0f, MaximumTotalArea)] public float totalArea;

    public static GeodesicDepressionRepairLimits Defaults => new GeodesicDepressionRepairLimits
    {
        componentArea = .00005f, fillDepth = .02f, affectedArea = .0002f,
        localAdjustment = .02f, totalArea = .0005f
    };

    public GeodesicDepressionRepairLimits Normalized()
    {
        var defaults = Defaults;
        return new GeodesicDepressionRepairLimits
        {
            componentArea = Bound(componentArea, defaults.componentArea, MaximumComponentArea),
            fillDepth = Bound(fillDepth, defaults.fillDepth, MaximumHeight),
            affectedArea = Bound(affectedArea, defaults.affectedArea, MaximumAffectedArea),
            localAdjustment = Bound(localAdjustment, defaults.localAdjustment, MaximumHeight),
            totalArea = Bound(totalArea, defaults.totalArea, MaximumTotalArea)
        };
    }

    private static float Bound(float value, float fallback, float maximum) =>
        float.IsFinite(value) ? Mathf.Clamp(value, 0f, maximum) : fallback;
}
