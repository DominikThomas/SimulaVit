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
    [Tooltip("Collects locomotion distributions and evolution cohorts for periodic Console reports.")]
    public bool logDiagnostics;
    [Min(0.000000001f), Tooltip("Minimum detectable reaction readiness, in full-reference-tick equivalents. Relative taxis deltas below this floor fade to zero; this is not a raw concentration threshold.")]
    public float taxisReadinessFloor;
    [Tooltip("Tracks recent sensing events for selected active organisms and logs them at the configured real-time interval.")]
    public bool detailedTaxisDiagnostics;
    [Range(1, 4)] public int maximumTrackedTaxisOrganisms;
    [Tooltip("0 automatically follows a few stable IDs. A positive ID follows only that organism, even through packed swap-back.")]
    public long trackedTaxisOrganismId;
    [Min(1f), Tooltip("Real seconds between bounded detailed reports, including at accelerated simulation speeds.")]
    public float detailedTaxisLogIntervalRealSeconds;

    public static GeodesicLocomotionSettings Default => new GeodesicLocomotionSettings
    {
        activeSpeedMultiplier = 2f, amoeboidEnergyPerSecond = 0.02f, flagellumEnergyPerSecond = 0.04f,
        flagellumTumbleMultiplier = 0.3f, tumbleReferenceSeconds = 0.5f,
        verticalSpeedMetersPerSecond = 10f, verticalExplorationIntervalSeconds = 20f,
        verticalContinueWhenBetter = 0.8f, verticalReverseWhenWorse = 0.8f,
        diagnosticMutationChance = 1f,
        taxisReadinessFloor = 0.0001f, maximumTrackedTaxisOrganisms = 2, detailedTaxisLogIntervalRealSeconds = 2f
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
        // New fields in older serialized settings are zero. Give them working defaults without editing scenes.
        s.taxisReadinessFloor = Safe(s.taxisReadinessFloor > 0f ? s.taxisReadinessFloor : 0.0001f, 0.0001f, 1e-9f, 1f);
        s.maximumTrackedTaxisOrganisms = Mathf.Clamp(s.maximumTrackedTaxisOrganisms > 0 ? s.maximumTrackedTaxisOrganisms : 2, 1, 4);
        s.trackedTaxisOrganismId = Math.Max(0L, s.trackedTaxisOrganismId);
        s.detailedTaxisLogIntervalRealSeconds = Safe(s.detailedTaxisLogIntervalRealSeconds > 0f ? s.detailedTaxisLogIntervalRealSeconds : 2f, 2f, 1f, 60f);
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
    // Whole-struct copying already carries these fields through capacity growth, swap-back and snapshots.
    public int taxisModelVersion;
    public bool memoryUsesMetabolism;
    public double reactionReadinessMemory;
    public GeodesicTaxisObservation lastTaxisObservation;
    public int cellCrossingsSinceSense;
    public bool verticalOriginRecorded;
    public int verticalOriginCell, verticalOriginLayer, verticalArrivalCell, verticalArrivalLayer;
    public double verticalReadinessBefore, verticalReadinessAfter;
    public float verticalArrivalSuitability;
}

/// <summary>One actual local experience, independent of the smoothed steering memory.</summary>
[Serializable]
public struct GeodesicTaxisObservation
{
    public bool valid;
    public int cell, layer;
    public float h2, co2, o2, temperature, performance, oxygenEfficiency, suitability;
    public double reactionReadiness;
}

/// <summary>Optional compatible population snapshot extension. Missing data initializes at the saved position.</summary>
[Serializable]
public struct GeodesicMovementSnapshot
{
    public Vector3 directionLocal, tangentLocal;
    public float radius;
    public GeodesicActiveMovementState active;
}
