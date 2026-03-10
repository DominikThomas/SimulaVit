using System;
using System.Reflection;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class ReplicatorSimulationPipeline : MonoBehaviour
{
    [Serializable]
    public struct SpeedProfile
    {
        public float speedMultiplier;
        public int simulationStepsPerFrame;
    }

    [Header("References")]
    [SerializeField] private ReplicatorManager replicatorManager;

    [Header("Stepping")]
    [SerializeField, Min(0)] private int simulationStepsPerFrame = 1;

    [Header("Optional render frame skipping")]
    [SerializeField] private bool allowRenderFrameSkipping = false;
    [SerializeField, Min(1)] private int renderEveryNFramesAtHighSpeed = 2;
    [SerializeField] private float renderSkipMinSpeedMultiplier = 20f;

    private MethodInfo managerStartMethod;
    private MethodInfo updateScentFieldsMethod;
    private MethodInfo resetScentDebugStateMethod;
    private MethodInfo updateLifecycleMethod;
    private MethodInfo tickMetabolismMethod;
    private MethodInfo runPredationPassMethod;
    private MethodInfo handleSpontaneousSpawningMethod;
    private MethodInfo updateRunAndTumbleLocomotionMethod;
    private MethodInfo runMovementJobMethod;
    private MethodInfo validateSessileMovementMethod;
    private MethodInfo updateMetabolismCountsMethod;
    private MethodInfo logMetabolismDebugThrottledMethod;
    private MethodInfo renderAgentsMethod;
    private FieldInfo isInitializedField;

    private bool reflectionReady;
    private float activeSpeedMultiplier = 1f;

    public int SimulationStepsPerFrame => simulationStepsPerFrame;

    private void Awake()
    {
        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }

        CacheReflection();

        if (replicatorManager == null || !reflectionReady)
        {
            enabled = false;
            Debug.LogError("ReplicatorSimulationPipeline could not locate a valid ReplicatorManager.", this);
            return;
        }

        if (!IsManagerInitialized())
        {
            managerStartMethod.Invoke(replicatorManager, null);
        }

        replicatorManager.enableRendering = false;
        replicatorManager.enabled = false;
    }

    public void SetSpeedProfile(SpeedProfile profile)
    {
        activeSpeedMultiplier = Mathf.Max(0f, profile.speedMultiplier);
        simulationStepsPerFrame = Mathf.Max(0, profile.simulationStepsPerFrame);
        if (replicatorManager != null)
        {
            replicatorManager.SetSimulationTiming(activeSpeedMultiplier, simulationStepsPerFrame);
        }
    }

    private void Update()
    {
        if (replicatorManager == null || !IsManagerInitialized())
        {
            return;
        }

        for (int i = 0; i < simulationStepsPerFrame; i++)
        {
            RunSimulationStep();
        }

        UpdateMetabolismCountsAndDebug();

        if (ShouldRenderThisFrame())
        {
            renderAgentsMethod.Invoke(replicatorManager, null);
        }
    }

    private void RunSimulationStep()
    {
        if (replicatorManager.useScentPredation && replicatorManager.planetResourceMap != null && replicatorManager.planetResourceMap.enableScentFields)
        {
            updateScentFieldsMethod.Invoke(replicatorManager, null);
        }
        else
        {
            resetScentDebugStateMethod.Invoke(replicatorManager, null);
        }

        updateLifecycleMethod.Invoke(replicatorManager, null);
        tickMetabolismMethod.Invoke(replicatorManager, null);
        runPredationPassMethod.Invoke(replicatorManager, null);
        handleSpontaneousSpawningMethod.Invoke(replicatorManager, null);
        updateRunAndTumbleLocomotionMethod.Invoke(replicatorManager, null);
        runMovementJobMethod.Invoke(replicatorManager, null);
        validateSessileMovementMethod.Invoke(replicatorManager, null);
    }

    private void UpdateMetabolismCountsAndDebug()
    {
        updateMetabolismCountsMethod.Invoke(replicatorManager, null);
        logMetabolismDebugThrottledMethod.Invoke(replicatorManager, null);
    }

    private bool ShouldRenderThisFrame()
    {
        if (!allowRenderFrameSkipping || activeSpeedMultiplier < renderSkipMinSpeedMultiplier)
        {
            return true;
        }

        int interval = Mathf.Max(1, renderEveryNFramesAtHighSpeed);
        return Time.frameCount % interval == 0;
    }

    private bool IsManagerInitialized()
    {
        return isInitializedField != null && (bool)isInitializedField.GetValue(replicatorManager);
    }

    private void CacheReflection()
    {
        if (replicatorManager == null)
        {
            return;
        }

        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type managerType = typeof(ReplicatorManager);

        managerStartMethod = managerType.GetMethod("Start", flags);
        updateScentFieldsMethod = managerType.GetMethod("UpdateScentFields", flags);
        resetScentDebugStateMethod = managerType.GetMethod("ResetScentDebugState", flags);
        updateLifecycleMethod = managerType.GetMethod("UpdateLifecycle", flags);
        tickMetabolismMethod = managerType.GetMethod("TickMetabolism", flags);
        runPredationPassMethod = managerType.GetMethod("RunPredationPass", flags);
        handleSpontaneousSpawningMethod = managerType.GetMethod("HandleSpontaneousSpawning", flags);
        updateRunAndTumbleLocomotionMethod = managerType.GetMethod("UpdateRunAndTumbleLocomotion", flags);
        runMovementJobMethod = managerType.GetMethod("RunMovementJob", flags);
        validateSessileMovementMethod = managerType.GetMethod("ValidateSessileMovement", flags);
        updateMetabolismCountsMethod = managerType.GetMethod("UpdateMetabolismCounts", flags);
        logMetabolismDebugThrottledMethod = managerType.GetMethod("LogMetabolismDebugThrottled", flags);
        renderAgentsMethod = managerType.GetMethod("RenderAgents", flags);
        isInitializedField = managerType.GetField("isInitialized", flags);

        reflectionReady = managerStartMethod != null
            && updateScentFieldsMethod != null
            && resetScentDebugStateMethod != null
            && updateLifecycleMethod != null
            && tickMetabolismMethod != null
            && runPredationPassMethod != null
            && handleSpontaneousSpawningMethod != null
            && updateRunAndTumbleLocomotionMethod != null
            && runMovementJobMethod != null
            && validateSessileMovementMethod != null
            && updateMetabolismCountsMethod != null
            && logMetabolismDebugThrottledMethod != null
            && renderAgentsMethod != null
            && isInitializedField != null;

        if (!reflectionReady)
        {
            Debug.LogError("ReplicatorSimulationPipeline failed to bind required ReplicatorManager internals.", this);
        }
    }
}
