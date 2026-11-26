using System;
using System.Linq;
using System.Reflection;

internal static class GeodesicReactionTestPipeline
{
    private delegate void BuildRequest(int index, ReplicatorPopulationState state, float seconds,
        float hydrogenCo2, float hydrogenH2, float hydrogenEnergy, float hydrogenStoreFraction,
        bool hydrogenOxygenInhibition, float oxygenComfort, float oxygenStress, float hydrogenOxygenFloor,
        float sulfurCo2, float sulfurH2S, float sulfurEnergy, float photoCo2, float photoEnergy, float maximumStore);

    internal static void React(GeodesicBiologyRuntime runtime, ReplicatorPopulationState state,
        float maximumStore, bool observe, float sulfurCo2 = 0f, float sulfurH2S = 0f, float sulfurEnergy = 0f,
        float oxygenComfort = 0f, float oxygenStress = 0f)
    {
        // Isolated reactions have no photosynthetic inputs or detailed slot profiling.
        const float photoCo2 = 0f, photoEnergy = 0f;
        runtime.DetailedPerformanceEnabled = false;
        Bind<Action>(runtime, "BeginBiologicalThermalStep")();
        Bind<Action<int>>(runtime, "EnsureRequestCapacity")(state.Count);
        Bind<Action>(runtime, "BeginSparseStep")();
        Bind<Action<int, ReplicatorPopulationState>>(runtime, "SampleOccupiedHabitats")(state.Count, state);
        var build = Bind<BuildRequest>(runtime, "BuildRequest");
        for (int i = 0; i < state.Count; i++)
            build(i, state, .5f, .01f, .02f, 8f, .8f, false, oxygenComfort, oxygenStress, 0f,
                sulfurCo2, sulfurH2S, sulfurEnergy, photoCo2, photoEnergy, maximumStore);
        Bind<Action<int>>(runtime, "AggregateDemands")(state.Count);
        Bind<Action>(runtime, "ResolveAvailabilityFactors")();
        Bind<Action<int>>(runtime, "ResolveCompetition")(state.Count);
        if (observe) Bind<Action>(runtime, "PrepareAccountingScratch")();
        Bind<Action<int, ReplicatorPopulationState, float, float, bool>>(runtime, "CommitRequests")(
            state.Count, state, maximumStore, photoEnergy, observe);
        if (observe)
        {
            var record = Bind<Action<int>>(runtime, "RecordReactionAccounting");
            for (int i = 0; i < state.Count; i++) record(i);
        }
    }

    private static T Bind<T>(GeodesicBiologyRuntime runtime, string methodName) where T : Delegate
    {
        Type[] arguments = typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray();
        Type target = typeof(GeodesicBiologyRuntime);
        var method = target.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, null, arguments, null);
        if (method == null)
            throw new MissingMethodException($"Expected {target.Name}.{methodName}({string.Join(", ", arguments.Select(t => t.Name))}).");
        return (T)method.CreateDelegate(typeof(T), runtime);
    }
}
