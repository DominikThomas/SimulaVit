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

    public int SimulationStepsPerFrame => simulationStepsPerFrame;

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

    }

    public void SetSpeedProfile(SpeedProfile profile)
    {
        simulationStepsPerFrame = Mathf.Max(0, profile.simulationStepsPerFrame);
        if (replicatorManager != null)
        {
            replicatorManager.SetSimulationTiming(simulationStepsPerFrame);
        }
    }

    public void RunFrame()
    {
        if (replicatorManager == null || !replicatorManager.IsInitializedForSimulation)
        {
            return;
        }

        for (int i = 0; i < simulationStepsPerFrame; i++)
        {
            RunSimulationStep();
        }

        if (replicatorManager.enableRendering && replicatorManager.ShouldRenderThisFrame(simulationStepsPerFrame))
        {
            replicatorManager.RenderAgents();
        }

        replicatorManager.UpdateMetabolismCounts();
        replicatorManager.LogMetabolismDebugThrottled();
    }

    private void RunSimulationStep()
    {
        replicatorManager.AdvanceSimulationStep();

        if (replicatorManager.ShouldProcessPredatorScent())
        {
            replicatorManager.UpdateScentFields();
        }
        else
        {
            replicatorManager.ResetScentDebugState();
        }

        replicatorManager.UpdateLifecycle();
        replicatorManager.TickMetabolism();
        replicatorManager.RunPredationPass();
        replicatorManager.HandleSpontaneousSpawning();
        bool populationStatePrimedForLocomotion = replicatorManager.PreparePopulationStateForLocomotion();
        replicatorManager.UpdateRunAndTumbleLocomotion(populationStatePrimedForLocomotion);
        replicatorManager.RunMovementJob(populationStatePrimedForLocomotion);
        replicatorManager.ValidateSessileMovement();
    }
}
