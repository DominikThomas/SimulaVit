#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_STANDALONE_WIN)
using System;
#if UNITY_EDITOR
using UnityEditor;
#else
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#endif

internal static class GeodesicGpuBufferLifetime
{
    private const string OptInVariable = "SIMULAVIT_GPU_BUFFER_LIFETIME";
    private static bool IsRequested => Environment.GetEnvironmentVariable(OptInVariable) == "1";

#if UNITY_EDITOR
    private const string MenuPath = "Tools/Diagnostics/Trace GPU Buffer Lifetime in Windows Test Players";

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        // Windows players launched by this Editor inherit the process environment.
        Environment.SetEnvironmentVariable(OptInVariable, IsRequested ? null : "1", EnvironmentVariableTarget.Process);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateMenu()
    {
        Menu.SetChecked(MenuPath, IsRequested);
        return true;
    }
#else
    private const string Prefix = "[GeodesicGpuBufferLifetime]";
    private static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        if (!IsRequested || initialized) return;

        // Keep leak tracking enabled until process exit so Unity can account for late releases.
        NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
        initialized = true;
        RenderPipelineManager.activeRenderPipelineCreated += OnPipelineCreated;
        RenderPipelineManager.activeRenderPipelineDisposed += OnPipelineDisposing;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        Application.quitting += OnApplicationQuitting;
        Debug.Log($"{Prefix} enabled mode={NativeLeakDetection.Mode} graphics={SystemInfo.graphicsDeviceType} " +
            $"asset={GraphicsSettings.currentRenderPipeline?.name ?? "none"} pipeline={PipelineType}");
    }

    private static string PipelineType => RenderPipelineManager.currentPipeline?.GetType().FullName ?? "none";

    private static void OnPipelineCreated() => Debug.Log($"{Prefix} pipeline-created type={PipelineType}");

    private static void OnPipelineDisposing() => Debug.Log($"{Prefix} pipeline-dispose-start type={PipelineType}");

    private static void OnSceneUnloaded(Scene scene) => Debug.Log($"{Prefix} scene-unloaded name={scene.name}");

    private static void OnApplicationQuitting() => Debug.Log($"{Prefix} application-quitting pipeline={PipelineType}");
#endif
}
#endif
