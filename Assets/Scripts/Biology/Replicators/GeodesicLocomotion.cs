using System;
using UnityEngine;

/// <summary>Geodesic-only controls; legacy steering and mutation controls remain on the manager.</summary>
[Serializable]
public struct GeodesicLocomotionSettings
{
    [Min(0f), Tooltip("Angular swimming speed relative to passive drift, scaled by moveSpeed / 4 and each locomotion speed multiplier.")]
    public float activeSpeedMultiplier;
    [Min(0f)] public float amoeboidEnergyPerSecond;
    [Min(0f)] public float flagellumEnergyPerSecond;
    [Range(0f, 1f), Tooltip("Flagellar tumble hazard relative to amoeboid. Probabilities are converted to hazards, independently of sensing cadence.")]
    public float flagellumTumbleMultiplier;
    [Min(0.1f), Tooltip("Reference seconds for the existing tumble probabilities.")]
    public float tumbleReferenceSeconds;
    [Min(0f), Tooltip("Effective vertical exploration speed in physical metres per simulated second, before the locomotion speed multiplier.")]
    public float verticalSpeedMetersPerSecond;
    [Min(0.1f)] public float verticalExplorationIntervalSeconds;
    [Range(0f, 1f)] public float verticalContinueWhenBetter;
    [Range(0f, 1f)] public float verticalReverseWhenWorse;
    public bool mutationRateOverrideEnabled;
    [Range(0f, 1f), Tooltip("Diagnostic per-birth mutation rate. Normal operation uses locomotionMutationChance.")]
    public float diagnosticMutationChance;
    public bool logDiagnostics;

    public static GeodesicLocomotionSettings Default => new GeodesicLocomotionSettings
    {
        activeSpeedMultiplier = 2f, amoeboidEnergyPerSecond = 0.02f, flagellumEnergyPerSecond = 0.04f,
        flagellumTumbleMultiplier = 0.3f, tumbleReferenceSeconds = 0.5f,
        verticalSpeedMetersPerSecond = 10f, verticalExplorationIntervalSeconds = 20f,
        verticalContinueWhenBetter = 0.8f, verticalReverseWhenWorse = 0.8f,
        diagnosticMutationChance = 1f, logDiagnostics = true
    };

    internal static float Safe(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;

    public GeodesicLocomotionSettings Sanitized()
    {
        var s = this;
        s.activeSpeedMultiplier = Safe(s.activeSpeedMultiplier, 2f, 0f, 16f);
        s.amoeboidEnergyPerSecond = Safe(s.amoeboidEnergyPerSecond, 0.02f, 0f, 1000f);
        s.flagellumEnergyPerSecond = Safe(s.flagellumEnergyPerSecond, 0.04f, 0f, 1000f);
        s.flagellumTumbleMultiplier = Safe(s.flagellumTumbleMultiplier, 0.3f, 0f, 1f);
        s.tumbleReferenceSeconds = Safe(s.tumbleReferenceSeconds, 0.5f, 0.1f, 60f);
        s.verticalSpeedMetersPerSecond = Safe(s.verticalSpeedMetersPerSecond, 10f, 0f, 1000f);
        s.verticalExplorationIntervalSeconds = Safe(s.verticalExplorationIntervalSeconds, 20f, 0.1f, 100000f);
        s.verticalContinueWhenBetter = Safe(s.verticalContinueWhenBetter, 0.8f, 0f, 1f);
        s.verticalReverseWhenWorse = Safe(s.verticalReverseWhenWorse, 0.8f, 0f, 1f);
        s.diagnosticMutationChance = Safe(s.diagnosticMutationChance, 1f, 0f, 1f);
        return s;
    }
}

/// <summary>Short-term experience, not inherited traits. Countdown clocks survive save/load.</summary>
[Serializable]
public struct GeodesicActiveMovementState
{
    public bool initialized, hasSuitability;
    public uint sequence;
    public float pendingSeconds, senseCountdown, lastSenseInterval, suitabilityMemory, runSeconds;
    public Vector3 desiredTangentLocal;
    public int verticalDirection, verticalTargetLayer;
    public bool verticalMoving, verticalObservationPending;
    public float verticalCountdown, verticalBaseline;
}

/// <summary>Optional compatible population snapshot extension. Missing data initializes at the saved position.</summary>
[Serializable]
public struct GeodesicMovementSnapshot
{
    public Vector3 directionLocal, tangentLocal;
    public float radius;
    public GeodesicActiveMovementState active;
}
