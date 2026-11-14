using System;
using UnityEngine;

/// <summary>Presentation parameters only. Size remains the organism's mature mesh scale.</summary>
[Serializable]
public struct ReplicatorVisualGrowthSettings
{
    [Range(0.01f, 1f)] public float minimumDiameterMultiplier;
    [Range(0.01f, 1f)] public float maximumDiameterMultiplier;
    [Range(0f, 1f), Tooltip("Blends linear carbon progress with smoothstep. No age/energy or time-based growth lag.")]
    public float growthCurveSmoothing;
    [Min(0f), Tooltip("Presentation seconds at normal speed. Acceleration shortens transitions; pause freezes them.")]
    public float divisionAnimationDuration;
    [Min(0f), Tooltip("Requested world-space daughter offset. Constrained to a small part of the same valid ocean cell.")]
    public float daughterSeparationDistance;

    public static ReplicatorVisualGrowthSettings Default => new ReplicatorVisualGrowthSettings
    {
        minimumDiameterMultiplier = 0.333f, maximumDiameterMultiplier = 1f,
        growthCurveSmoothing = 1f, divisionAnimationDuration = 0.65f, daughterSeparationDistance = 0.06f
    };

    public ReplicatorVisualGrowthSettings Sanitized()
    {
        var s = this;
        s.minimumDiameterMultiplier = float.IsFinite(s.minimumDiameterMultiplier) ? Mathf.Clamp(s.minimumDiameterMultiplier, 0.01f, 1f) : 0.333f;
        s.maximumDiameterMultiplier = float.IsFinite(s.maximumDiameterMultiplier) ? Mathf.Clamp(s.maximumDiameterMultiplier, s.minimumDiameterMultiplier, 1f) : 1f;
        s.growthCurveSmoothing = float.IsFinite(s.growthCurveSmoothing) ? Mathf.Clamp01(s.growthCurveSmoothing) : 1f;
        s.divisionAnimationDuration = float.IsFinite(s.divisionAnimationDuration) ? Mathf.Clamp(s.divisionAnimationDuration, 0f, 5f) : 0.65f;
        s.daughterSeparationDistance = float.IsFinite(s.daughterSeparationDistance) ? Mathf.Max(0f, s.daughterSeparationDistance) : 0.06f;
        return s;
    }
}

/// <summary>One bounded value per packed organism; copied/cleared with its population entry.
/// Offsets are planet-local and relative to the moving authoritative position, never packed indices or IDs.
/// Relative elapsed time also permits snapshot restoration without retaining a wall-clock timestamp.</summary>
[Serializable]
public struct ReplicatorVisualState
{
    public bool initialized;
    public float carbonBaseline;
    public float growthProgress;
    public float diameterMultiplier;
    public float divisionElapsedSeconds;
    public float divisionDurationSeconds;
    public float divisionStartDiameterMultiplier;
    public Vector3 divisionStartOffsetLocal;
    public Vector3 divisionBirthDirectionLocal;

    public bool IsDividing => divisionDurationSeconds > 0f && divisionElapsedSeconds < divisionDurationSeconds;
}

public static class ReplicatorVisualGrowth
{
    // Carbon-domain deadband suppresses tiny store roundoff without a per-frame growth timer.
    private const float ProgressDeadband = 0.002f;

    public static void InitializeCycle(ref ReplicatorVisualState visual, float carbon, ReplicatorVisualGrowthSettings settings)
    {
        visual = new ReplicatorVisualState
        {
            initialized = true, carbonBaseline = SafeCarbon(carbon),
            diameterMultiplier = settings.minimumDiameterMultiplier
        };
    }

    public static float Progress(in ReplicatorVisualState visual, float carbon, float threshold)
    {
        if (!float.IsFinite(threshold) || threshold <= 0f) return 0f;
        double baseline = visual.initialized ? SafeCarbon(visual.carbonBaseline) : SafeCarbon(carbon);
        double remaining = (double)threshold - baseline;
        // A retained store already at/above a (possibly mutated) threshold has no remaining
        // interval. Start small and use one threshold of additional C as the visual span.
        // Biological eligibility still uses the original absolute threshold, unchanged.
        double span = remaining > Math.Max(1e-7, threshold * 1e-6) ? remaining : threshold;
        float progress = Mathf.Clamp01((float)((SafeCarbon(carbon) - baseline) / span));
        if (progress > 0f && progress < 1f && Mathf.Abs(progress - visual.growthProgress) < ProgressDeadband)
            return visual.growthProgress;
        return progress;
    }

    public static float TargetDiameter(float progress, ReplicatorVisualGrowthSettings settings)
    {
        float curved = Mathf.Lerp(progress, progress * progress * (3f - 2f * progress), settings.growthCurveSmoothing);
        return Mathf.Lerp(settings.minimumDiameterMultiplier, settings.maximumDiameterMultiplier, curved);
    }

    public static float DisplayDiameter(in ReplicatorVisualState visual, float progress, ReplicatorVisualGrowthSettings settings)
    {
        float target = TargetDiameter(progress, settings);
        return visual.IsDividing
            ? Mathf.Lerp(Mathf.Clamp(visual.divisionStartDiameterMultiplier, settings.minimumDiameterMultiplier, settings.maximumDiameterMultiplier),
                target, DivisionBlend(visual))
            : target;
    }

    public static void BeginDivision(ref ReplicatorVisualState visual, float retainedCarbon, float startDiameter,
        Vector3 startOffsetLocal, Vector3 birthDirectionLocal, ReplicatorVisualGrowthSettings settings)
    {
        InitializeCycle(ref visual, retainedCarbon, settings);
        visual.divisionStartDiameterMultiplier = Mathf.Clamp(startDiameter, settings.minimumDiameterMultiplier, settings.maximumDiameterMultiplier);
        visual.diameterMultiplier = visual.divisionStartDiameterMultiplier;
        visual.divisionDurationSeconds = settings.divisionAnimationDuration;
        visual.divisionStartOffsetLocal = startOffsetLocal;
        visual.divisionBirthDirectionLocal = birthDirectionLocal;
        if (!visual.IsDividing)
        {
            visual.diameterMultiplier = settings.minimumDiameterMultiplier;
            visual.divisionStartOffsetLocal = Vector3.zero;
        }
    }

    public static float DivisionBlend(in ReplicatorVisualState visual)
    {
        float t = visual.IsDividing ? Mathf.Clamp01(visual.divisionElapsedSeconds / visual.divisionDurationSeconds) : 1f;
        return t * t * (3f - 2f * t);
    }

    public static Vector3 ResolveLocalPosition(in ReplicatorVisualState visual, Vector3 authoritativeLocal)
    {
        if (!visual.IsDividing || authoritativeLocal.sqrMagnitude < 1e-12f) return authoritativeLocal;
        Vector3 direction = authoritativeLocal.normalized;
        Quaternion transport = Quaternion.FromToRotation(visual.divisionBirthDirectionLocal, direction);
        Vector3 offset = transport * visual.divisionStartOffsetLocal * (1f - DivisionBlend(visual));
        Vector3 lobed = authoritativeLocal + offset;
        // Follow the same spherical depth interpolation as the packed passive position.
        return lobed.sqrMagnitude > 1e-12f ? lobed.normalized * authoritativeLocal.magnitude : authoritativeLocal;
    }

    public static bool Update(ref ReplicatorVisualState visual, float carbon, float threshold,
        ReplicatorVisualGrowthSettings settings)
    {
        bool corrected = false;
        if (!visual.initialized || !float.IsFinite(visual.carbonBaseline) || visual.carbonBaseline < 0f)
        {
            InitializeCycle(ref visual, carbon, settings);
            corrected = true;
        }
        if (!float.IsFinite(visual.divisionDurationSeconds) || visual.divisionDurationSeconds < 0f || visual.divisionDurationSeconds > 5f
            || !float.IsFinite(visual.divisionElapsedSeconds) || visual.divisionElapsedSeconds < 0f
            || !float.IsFinite(visual.divisionStartDiameterMultiplier)
            || visual.divisionStartDiameterMultiplier < 0f || visual.divisionStartDiameterMultiplier > 1f
            || !Finite(visual.divisionStartOffsetLocal) || !Finite(visual.divisionBirthDirectionLocal)
            || !float.IsFinite(visual.divisionStartOffsetLocal.sqrMagnitude)
            || (visual.IsDividing && (!float.IsFinite(visual.divisionBirthDirectionLocal.sqrMagnitude)
                || visual.divisionBirthDirectionLocal.sqrMagnitude < 0.9f || visual.divisionBirthDirectionLocal.sqrMagnitude > 1.1f)))
        {
            visual.divisionDurationSeconds = visual.divisionElapsedSeconds = 0f;
            visual.divisionStartOffsetLocal = Vector3.zero;
            visual.divisionBirthDirectionLocal = Vector3.zero;
            visual.divisionStartDiameterMultiplier = settings.minimumDiameterMultiplier;
            corrected = true;
        }
        if (!float.IsFinite(visual.growthProgress) || visual.growthProgress < 0f || visual.growthProgress > 1f)
        {
            visual.growthProgress = 0f;
            corrected = true;
        }
        if (!float.IsFinite(visual.diameterMultiplier) || visual.diameterMultiplier < settings.minimumDiameterMultiplier
            || visual.diameterMultiplier > settings.maximumDiameterMultiplier) corrected = true;
        visual.growthProgress = Progress(visual, carbon, threshold);
        visual.diameterMultiplier = DisplayDiameter(visual, visual.growthProgress, settings);
        if (!visual.IsDividing) visual.divisionStartOffsetLocal = Vector3.zero;
        return corrected;
    }

    public static void AdvanceDivision(ref ReplicatorVisualState visual, float simulationDelta)
    {
        // Invoked in the existing lifecycle pass, even with rendering disabled. Pausing stops
        // the clock; acceleration can finish/replace a transition before another frame is drawn.
        if (visual.IsDividing && simulationDelta > 0f)
            visual.divisionElapsedSeconds = Mathf.Min(visual.divisionDurationSeconds,
                visual.divisionElapsedSeconds + simulationDelta);
    }

    private static float SafeCarbon(float carbon) => float.IsFinite(carbon) ? Mathf.Max(0f, carbon) : 0f;
    private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}
