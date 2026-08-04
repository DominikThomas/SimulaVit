using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-1000)]
public class ReplicatorSimulationPipeline : MonoBehaviour
{
    [Serializable]
    public struct SpeedProfile
    {
        public int simulationStepsPerFrame;
    }

    [Header("References")]
    [SerializeField] private ReplicatorManager replicatorManager;

    [Header("Stepping")]
    [SerializeField, Min(0)] private int simulationStepsPerFrame = 1;
    [SerializeField, Min(0.001f), Tooltip("Maximum authoritative delta accepted by each configured simulation step. Under overload simulation throughput slows coherently instead of creating catch-up work.")] private float maximumSimulationStepDeltaSeconds = 1f / 30f;
    [Header("Diagnostics")]
    [SerializeField] private float simulationSpeedMultiplier = 1f;
    [SerializeField] private float frameDeltaTime;
    [SerializeField] private float simulationDeltaTime;
    [SerializeField] private float frameSimulationDeltaTime;
    [SerializeField] private double simulationTimeSeconds;
    [SerializeField] private bool movementUsesAuthoritativeSimulationDelta = true;
    [SerializeField] private bool shouldAdvanceSimulation = true;
    [SerializeField] private bool pauseDetected;
    [SerializeField] private float rawRenderedFrameDelta;
    [SerializeField] private bool simulationStepDeltaClamped;
    [SerializeField] private int simulationStepsExecutedThisFrame;
    [SerializeField] private float effectiveSimulationTimeAdvanceThisFrame;

    private bool discardNextFrameDelta;
    private int consecutiveClampedFrames;
    private bool warnedSustainedDeltaClamping;

    public int SimulationStepsPerFrame => simulationStepsPerFrame;
    public float SimulationSpeedMultiplier => simulationSpeedMultiplier;
    public float FrameDeltaTime => frameDeltaTime;
    public float SimulationDeltaTime => simulationDeltaTime;
    public float FrameSimulationDeltaTime => frameSimulationDeltaTime;
    public double SimulationTimeSeconds => simulationTimeSeconds;
    public bool MovementUsesAuthoritativeSimulationDelta => movementUsesAuthoritativeSimulationDelta;
    public bool ShouldAdvanceSimulation => shouldAdvanceSimulation;
    public bool PauseDetected => pauseDetected;
    public float RawRenderedFrameDelta => rawRenderedFrameDelta;
    public float MaximumSimulationStepDeltaSeconds => maximumSimulationStepDeltaSeconds;
    public bool SimulationStepDeltaClamped => simulationStepDeltaClamped;
    public int SimulationStepsExecutedThisFrame => simulationStepsExecutedThisFrame;
    public float EffectiveSimulationTimeAdvanceThisFrame => effectiveSimulationTimeAdvanceThisFrame;

    private void Awake()
    {
        replicatorManager = GetComponent<ReplicatorManager>();

        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }

        if (replicatorManager == null)
        {
            enabled = false;
            Debug.LogError("ReplicatorSimulationPipeline could not locate a valid ReplicatorManager.", this);
            return;
        }

        SetSimulationStepsPerFrame(replicatorManager.RuntimeSimulationStepsPerFrame);

#if UNITY_EDITOR
        EditorApplication.pauseStateChanged += OnEditorPauseStateChanged;
#endif
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        EditorApplication.pauseStateChanged -= OnEditorPauseStateChanged;
#endif
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            pauseDetected = true;
            discardNextFrameDelta = true;
            ResetFrameTiming();
            return;
        }

        discardNextFrameDelta = true;
    }

#if UNITY_EDITOR
    private void OnEditorPauseStateChanged(PauseState pauseState)
    {
        pauseDetected = pauseState == PauseState.Paused;
        discardNextFrameDelta = true;
        if (pauseDetected)
        {
            ResetFrameTiming();
        }
    }
#endif

    public void SetSpeedProfile(SpeedProfile profile)
    {
        SetSimulationStepsPerFrame(profile.simulationStepsPerFrame);
        replicatorManager?.SetSimulationTiming(simulationStepsPerFrame);
    }

    public void SetSimulationStepsPerFrame(int stepsPerFrame)
    {
        simulationStepsPerFrame = Mathf.Max(0, stepsPerFrame);
        simulationSpeedMultiplier = simulationStepsPerFrame;
    }


    public void ApplyClockSnapshot(SimulationClockSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        simulationTimeSeconds = System.Math.Max(0d, snapshot.simulationTimeSeconds);
        simulationStepsPerFrame = Mathf.Max(0, snapshot.simulationStepsPerFrame);
        simulationSpeedMultiplier = simulationStepsPerFrame;
        pauseDetected = snapshot.pauseDetected;
        discardNextFrameDelta = true;
        ResetFrameTiming();
        replicatorManager?.ApplyClockSnapshot(snapshot);
    }

    public SimulationClockSnapshot CaptureClockSnapshot()
    {
        return new SimulationClockSnapshot
        {
            simulationTimeSeconds = simulationTimeSeconds,
            simulationStepCount = replicatorManager != null ? replicatorManager.SimulationStepCount : 0,
            simulationStepsPerFrame = simulationStepsPerFrame,
            simulationSpeedMultiplier = simulationSpeedMultiplier,
            frameDeltaTime = frameDeltaTime,
            simulationDeltaTime = simulationDeltaTime,
            frameSimulationDeltaTime = frameSimulationDeltaTime,
            shouldAdvanceSimulation = shouldAdvanceSimulation,
            pauseDetected = pauseDetected
        };
    }

    public void RunFrame()
    {
        if (replicatorManager == null || !replicatorManager.IsInitializedForSimulation)
        {
            shouldAdvanceSimulation = false;
            ResetFrameTiming();
            return;
        }

        pauseDetected = IsApplicationPauseDetected();
        shouldAdvanceSimulation = simulationStepsPerFrame > 0 && !pauseDetected;

        if (!shouldAdvanceSimulation)
        {
            ResetFrameTiming();
            if (!pauseDetected && replicatorManager.enableRendering && replicatorManager.ShouldRenderThisFrame(simulationStepsPerFrame))
            {
                replicatorManager.RenderAgents();
            }

            return;
        }

        if (discardNextFrameDelta)
        {
            discardNextFrameDelta = false;
            ResetFrameTiming();
            return;
        }

        rawRenderedFrameDelta = Time.unscaledDeltaTime;
        frameDeltaTime = rawRenderedFrameDelta;
        simulationSpeedMultiplier = simulationStepsPerFrame;
        simulationDeltaTime = simulationStepsPerFrame > 0 ? Mathf.Min(rawRenderedFrameDelta, Mathf.Max(0.001f, maximumSimulationStepDeltaSeconds)) : 0f;
        simulationStepDeltaClamped = simulationDeltaTime + 1e-7f < rawRenderedFrameDelta;
        frameSimulationDeltaTime = simulationDeltaTime * simulationStepsPerFrame;
        simulationStepsExecutedThisFrame = simulationStepsPerFrame;
        effectiveSimulationTimeAdvanceThisFrame = frameSimulationDeltaTime;
        if (simulationStepDeltaClamped)
        {
            consecutiveClampedFrames++;
            if (consecutiveClampedFrames >= 30 && !warnedSustainedDeltaClamping)
            {
                warnedSustainedDeltaClamping = true;
                Debug.LogWarning($"[SimulationTiming] Rendered-frame delta has remained above the {maximumSimulationStepDeltaSeconds:F4}s simulation-step limit. Requested speed is target throughput; authoritative simulation is slowing coherently under load.", this);
            }
        }
        else consecutiveClampedFrames = 0;

        for (int i = 0; i < simulationStepsPerFrame; i++)
        {
            RunSimulationStep(simulationDeltaTime);
        }

        if (replicatorManager.enableRendering && replicatorManager.ShouldRenderThisFrame(simulationStepsPerFrame))
        {
            replicatorManager.RenderAgents();
        }

        replicatorManager.UpdateMetabolismCounts();
        replicatorManager.LogMetabolismDebugThrottled();
    }

    private void ResetFrameTiming()
    {
        simulationSpeedMultiplier = simulationStepsPerFrame;
        frameDeltaTime = 0f;
        simulationDeltaTime = 0f;
        frameSimulationDeltaTime = 0f;
        rawRenderedFrameDelta = 0f;
        simulationStepDeltaClamped = false;
        simulationStepsExecutedThisFrame = 0;
        effectiveSimulationTimeAdvanceThisFrame = 0f;
    }

    private bool _runtimePaused;

    private void OnApplicationPause(bool paused)
    {
        _runtimePaused = paused;
    }

    private bool IsApplicationPauseDetected()
    {
#if UNITY_EDITOR
        return EditorApplication.isPaused;
#else
    return _runtimePaused;
#endif
    }

    private void RunSimulationStep(float stepDeltaTime)
    {
        simulationTimeSeconds += stepDeltaTime;
        replicatorManager.AdvanceSimulationStep(stepDeltaTime, simulationTimeSeconds);

        if (replicatorManager.ShouldProcessPredatorScent())
        {
            replicatorManager.UpdateScentFields(simulationTimeSeconds);
        }
        else
        {
            replicatorManager.ResetScentDebugState();
        }

        replicatorManager.UpdateLifecycle(stepDeltaTime);
        replicatorManager.TickMetabolism(stepDeltaTime);
        replicatorManager.RunPredationPass(stepDeltaTime);
        replicatorManager.HandleSpontaneousSpawning(stepDeltaTime);
        bool populationStatePrimedForLocomotion = replicatorManager.PreparePopulationStateForLocomotion();
        replicatorManager.UpdateRunAndTumbleLocomotion(populationStatePrimedForLocomotion, stepDeltaTime, simulationTimeSeconds);
        replicatorManager.RunMovementJob(populationStatePrimedForLocomotion, stepDeltaTime, simulationTimeSeconds);
        replicatorManager.ValidateSessileMovement();
    }
}
