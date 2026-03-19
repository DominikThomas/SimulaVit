using System;
using UnityEngine;

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

    public int SimulationStepsPerFrame => simulationStepsPerFrame;
    public float SimulationSpeedMultiplier => simulationSpeedMultiplier;
    public float FrameDeltaTime => frameDeltaTime;
    public float SimulationDeltaTime => simulationDeltaTime;
    public float FrameSimulationDeltaTime => frameSimulationDeltaTime;
    public double SimulationTimeSeconds => simulationTimeSeconds;
    public bool MovementUsesAuthoritativeSimulationDelta => movementUsesAuthoritativeSimulationDelta;

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
    }

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
