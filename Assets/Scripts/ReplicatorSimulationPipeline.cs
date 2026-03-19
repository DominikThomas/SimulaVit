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
    [Header("Diagnostics")]
    [SerializeField] private float simulationSpeedMultiplier = 1f;
    [SerializeField] private float frameDeltaTime;
    [SerializeField] private float simulationDeltaTime;
    [SerializeField] private float frameSimulationDeltaTime;
    [SerializeField] private double simulationTimeSeconds;
    [SerializeField] private bool movementUsesAuthoritativeSimulationDelta = true;
    [SerializeField] private bool shouldAdvanceSimulation = true;
    [SerializeField] private bool pauseDetected;

    private bool discardNextFrameDelta;

    public int SimulationStepsPerFrame => simulationStepsPerFrame;
    public float SimulationSpeedMultiplier => simulationSpeedMultiplier;
    public float FrameDeltaTime => frameDeltaTime;
    public float SimulationDeltaTime => simulationDeltaTime;
    public float FrameSimulationDeltaTime => frameSimulationDeltaTime;
    public double SimulationTimeSeconds => simulationTimeSeconds;
    public bool MovementUsesAuthoritativeSimulationDelta => movementUsesAuthoritativeSimulationDelta;
    public bool ShouldAdvanceSimulation => shouldAdvanceSimulation;
    public bool PauseDetected => pauseDetected;

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

    private void OnApplicationPause(bool isPaused)
    {
        pauseDetected = isPaused || IsEditorPaused();
        discardNextFrameDelta = true;
        if (pauseDetected)
        {
            ResetFrameTiming();
        }
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

        frameDeltaTime = Time.unscaledDeltaTime;
        simulationSpeedMultiplier = simulationStepsPerFrame;
        simulationDeltaTime = simulationStepsPerFrame > 0 ? frameDeltaTime : 0f;
        frameSimulationDeltaTime = simulationDeltaTime * simulationStepsPerFrame;

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
    }

    private bool IsApplicationPauseDetected()
    {
        if (Application.isPaused)
        {
            return true;
        }

        return IsEditorPaused();
    }

    private static bool IsEditorPaused()
    {
#if UNITY_EDITOR
        return EditorApplication.isPaused;
#else
        return false;
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
