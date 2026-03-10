using UnityEngine;

public static class SimulationSpeedBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureSimulationSpeedController()
    {
        SimulationSpeedController existing = Object.FindFirstObjectByType<SimulationSpeedController>();
        if (existing != null)
        {
            return;
        }

        GameObject bootstrap = new GameObject("SimulationSpeedController");
        Object.DontDestroyOnLoad(bootstrap);
        bootstrap.AddComponent<SimulationSpeedController>();
    }
}
