using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    [Serializable]
    public struct FounderEmergenceSnapshot
    {
        public long attempts, successes, rejectedPopulationCap, rejectedHabitat;
        public double firstScheduledTime, firstEmergenceTime;
        public int firstVentNode, distinctFounderVents, inhabitedVents, indirectlyColonizedVents, unoccupiedVents, submarineOutlets;
        public int livingFounders, initialEventsRemaining;
        public long founderDeaths, founderParentBirths, descendantParentBirths, candidateRefreshes;
    }

    private struct FounderVentCandidate { public GeodesicVentSourceOutlet outlet; public double weight; }
    private GeodesicFounderEmergenceSchedule emergenceSchedule;
    private System.Random emergenceSelectionRandom, emergenceTraitsRandom, emergenceVisualRandom;
    private FounderVentCandidate[] emergenceVents = Array.Empty<FounderVentCandidate>();
    private readonly HashSet<long> livingDirectFounders = new HashSet<long>();
    private readonly HashSet<int> directFounderNodes = new HashSet<int>();
    private readonly Dictionary<int, List<int>> ventNeighborhoods = new Dictionary<int, List<int>>();
    private bool[] occupiedEmergenceVents = Array.Empty<bool>();
    private int cachedOutletCount = -1;
    private double nextCandidateRefresh, emergencePlacementTime, emergenceScheduledTime;
    private long emergenceAttempts, emergenceSuccesses, emergenceRejectedCap, emergenceRejectedHabitat;
    private long emergenceFounderDeaths, emergenceFounderBirths, emergenceDescendantBirths, emergenceCandidateRefreshes;
    private double firstEmergenceTime = -1d, firstEmergenceScheduledTime = -1d;
    private int firstEmergenceNode = -1;

    public void ConfigureGradualEmergence(int initialCount, float delay, float meanInterval, bool ongoing, float ongoingInterval)
    {
        emergenceSchedule = new GeodesicFounderEmergenceSchedule();
        emergenceSchedule.Configure(biologySeed, 0d, delay, initialCount, meanInterval, ongoing, ongoingInterval);
        emergenceSelectionRandom = new System.Random(unchecked(biologySeed ^ 0x56454E54));
        emergenceTraitsRandom = new System.Random(unchecked(biologySeed ^ 0x464F554E));
        emergenceVisualRandom = new System.Random(unchecked(biologySeed ^ 0x56495355));
        Debug.Log(FormattableString.Invariant($"[GeodesicFounderEmergenceConfig] mode=Gradual initialEventCap={Math.Max(0,initialCount)} delay={delay:G9} meanInitialInterval={GeodesicFounderEmergenceSchedule.NormalizeInterval(meanInterval):G9} ongoing={ongoing} meanOngoingInterval={GeodesicFounderEmergenceSchedule.NormalizeInterval(ongoingInterval,300f):G9} firstDue={emergenceSchedule.NextEventTime:G9} seed={biologySeed} rejectedEventsConsumeCap=true maxEventsPerStep=64 viabilityCacheSeconds=10"));
    }

    public void AdvanceFounderEmergence(double simulationTime, int maximumPopulation, float co2Need, float h2Need,
        bool oxygenInhibition, float oxygenComfort, float oxygenStress, float oxygenFloor,
        List<Replicator> agents, ReplicatorPopulationState state)
    {
        if (emergenceSchedule == null || grid == null) return;
        // Fixed work bound for pathological intervals/backlogs. Remaining due events stay queued.
        for (int n = 0; n < 64 && emergenceSchedule.TryConsume(simulationTime, out double due); n++)
        {
            emergenceAttempts++;
            if (state.Count >= Math.Max(0, maximumPopulation)) { emergenceRejectedCap++; continue; }
            EnsureFounderVentCatalog();
            if (simulationTime >= nextCandidateRefresh)
            {
                for (int i = 0; i < emergenceVents.Length; i++)
                    emergenceVents[i].weight = ViableFounderWeight(emergenceVents[i].outlet, co2Need, h2Need,
                        oxygenInhibition, oxygenComfort, oxygenStress, oxygenFloor);
                nextCandidateRefresh = simulationTime + 10d;
                emergenceCandidateRefreshes++;
            }
            int selected = -1;
            for (int sample = 0; sample < 8; sample++)
            {
                double total = 0d;
                for (int i = 0; i < emergenceVents.Length; i++) total += emergenceVents[i].weight;
                if (!(total > 0d)) break;
                double pick = emergenceSelectionRandom.NextDouble() * total;
                for (int i = 0; i < emergenceVents.Length; i++)
                {
                    pick -= emergenceVents[i].weight;
                    if (pick < 0d) { selected = i; break; }
                }
                if (selected < 0) break;
                if (ViableFounderWeight(emergenceVents[selected].outlet, co2Need, h2Need,
                    oxygenInhibition, oxygenComfort, oxygenStress, oxygenFloor) > 0d) break;
                emergenceVents[selected].weight = 0d; selected = -1;
            }
            if (selected < 0) { emergenceRejectedHabitat++; continue; }
            emergencePlacementTime = simulationTime; emergenceScheduledTime = due;
            // An empty runtime has not advanced its movement clock during environmental warmup.
            // Once populated, preserve that clock and every existing organism's scheduled movement.
            if (state.Count == 0) passiveMovementTime = (float)Math.Max(0d, simulationTime);
            int packed = SpawnFounderAtVent(emergenceVents[selected].outlet, emergenceTraitsRandom, emergenceVisualRandom,
                agents, state, founderMinLife, founderMaxLife, founderColor, founderBiomassTarget,
                founderHydrogenTemperatureRange, founderLethalMargin, true);
            InitializePassiveSchedulesAtSpawn(state, packed, passiveMovementTime);
        }
    }

    private double ViableFounderWeight(GeodesicVentSourceOutlet outlet, float co2Need, float h2Need,
        bool oxygenInhibition, float oxygenComfort, float oxygenStress, float oxygenFloor)
    {
        int cell = outlet.SourceNode / grid.MaximumLayerCount, layer = outlet.SourceNode % grid.MaximumLayerCount;
        if (!grid.IsNodeActive(cell,layer)) return 0d;
        resources.TryGetConcentration(cell,layer,GeodesicOceanResource.CO2,out float co2);
        resources.TryGetConcentration(cell,layer,GeodesicOceanResource.H2,out float h2);
        resources.TryGetConcentration(cell,layer,GeodesicOceanResource.O2,out float o2);
        double ready = GeodesicFoodSignal.HydrogenotrophyTicks(co2,h2,grid.PhysicalLayerVolumeKm3[outlet.SourceNode],co2Need,h2Need);
        float thermal = CalculateTemperaturePerformance(GetHabitatTemperature(cell,layer),
            Mathf.Min(founderHydrogenTemperatureRange.x,founderHydrogenTemperatureRange.y),
            Mathf.Max(founderHydrogenTemperatureRange.x,founderHydrogenTemperatureRange.y),
            Mathf.Max(1f,founderLethalMargin),temperatureSettings.DormancyFloor);
        float oxygen = oxygenInhibition ? CalculateAnaerobeO2Efficiency(o2,oxygenComfort,oxygenStress,oxygenFloor) : 1f;
        // Require at least one local reference reaction, then prefer fuller, thermally productive habitats.
        if (!double.IsFinite(ready) || ready < 1d || !float.IsFinite(thermal) || thermal <= 0f || !float.IsFinite(oxygen) || oxygen <= 0f) return 0d;
        double production = (double)outlet.SystemBudgetWeight * outlet.WithinSystemWeight;
        return double.IsFinite(production) && production > 0d ? production * (ready / (1d+ready)) * thermal * oxygen : 0d;
    }

    private void EnsureFounderVentCatalog()
    {
        if (grid == null || resources == null || cachedOutletCount == resources.CompactOutletCount) return;
        cachedOutletCount = resources.CompactOutletCount;
        var candidates = new List<FounderVentCandidate>();
        for (int i = 0; i < cachedOutletCount; i++)
            if (TryResolveFounderVent(i,out var outlet,out _,out _)) candidates.Add(new FounderVentCandidate { outlet=outlet });
        emergenceVents = candidates.ToArray(); occupiedEmergenceVents = new bool[emergenceVents.Length];
        ventNeighborhoods.Clear();
        for (int i = 0; i < emergenceVents.Length; i++)
        {
            int cell = emergenceVents[i].outlet.CellIndex;
            AddVentNeighborhoodCell(cell,i);
            for (int k = 0; k < grid.SourceTopology.NeighborCounts[cell]; k++)
                AddVentNeighborhoodCell(grid.SourceTopology.Neighbors6[cell*6+k],i);
        }
        nextCandidateRefresh = 0d;
    }

    // Source cell + its immediate topology neighbors, each cell's bottom two active layers.
    // Reverse node lookup makes each diagnostic sample O(population + outlets), not population*outlets.
    private void AddVentNeighborhoodCell(int cell, int vent)
    {
        int bottom = grid.GetBottomLayerIndex(cell);
        for (int layer = Math.Max(0,bottom-1); layer <= bottom; layer++)
        {
            int node = grid.GetNodeIndex(cell,layer);
            if (!ventNeighborhoods.TryGetValue(node,out var vents)) ventNeighborhoods.Add(node,vents=new List<int>());
            if (!vents.Contains(vent)) vents.Add(vent);
        }
    }

    private void RegisterFounderPlacement(long id, int node)
    {
        emergenceSuccesses++; livingDirectFounders.Add(id); directFounderNodes.Add(node);
        if (firstEmergenceNode >= 0) return;
        firstEmergenceNode = node; firstEmergenceTime = emergencePlacementTime; firstEmergenceScheduledTime = emergenceScheduledTime;
    }
    internal void RecordCappedFounderEvents(int count)
    { emergenceAttempts += count; emergenceRejectedCap += count; }
    private void RecordFounderDeath(long id) { if (livingDirectFounders.Remove(id)) emergenceFounderDeaths++; }
    private void RecordFounderParentBirth(long id)
    { if (livingDirectFounders.Contains(id)) emergenceFounderBirths++; else emergenceDescendantBirths++; }

    public FounderEmergenceSnapshot CaptureFounderEmergence(ReplicatorPopulationState state)
    {
        EnsureFounderVentCatalog(); Array.Clear(occupiedEmergenceVents,0,occupiedEmergenceVents.Length);
        for (int i = 0; i < state.Count; i++)
        {
            int cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i];
            if (!grid.IsNodeActive(cell,layer)) continue;
            if (ventNeighborhoods.TryGetValue(grid.GetNodeIndex(cell,layer),out var vents))
                for (int k = 0; k < vents.Count; k++) occupiedEmergenceVents[vents[k]] = true;
        }
        int inhabited=0, indirect=0;
        for (int i=0;i<occupiedEmergenceVents.Length;i++) if (occupiedEmergenceVents[i])
        { inhabited++; if (!directFounderNodes.Contains(emergenceVents[i].outlet.SourceNode)) indirect++; }
        return new FounderEmergenceSnapshot { attempts=emergenceAttempts, successes=emergenceSuccesses,
            rejectedPopulationCap=emergenceRejectedCap, rejectedHabitat=emergenceRejectedHabitat,
            firstEmergenceTime=firstEmergenceTime, firstScheduledTime=firstEmergenceScheduledTime, firstVentNode=firstEmergenceNode,
            distinctFounderVents=directFounderNodes.Count, inhabitedVents=inhabited, indirectlyColonizedVents=indirect,
            unoccupiedVents=emergenceVents.Length-inhabited, submarineOutlets=emergenceVents.Length,
            livingFounders=livingDirectFounders.Count, founderDeaths=emergenceFounderDeaths,
            founderParentBirths=emergenceFounderBirths, descendantParentBirths=emergenceDescendantBirths,
            initialEventsRemaining=emergenceSchedule?.InitialEventsRemaining??0, candidateRefreshes=emergenceCandidateRefreshes };
    }

    private void LogFounderEmergence(ReplicatorPopulationState state) =>
        Debug.Log("[GeodesicFounderEmergence] cumulative=true occupancy=outlet-plus-one-ring-bottom-two-layers " + JsonUtility.ToJson(CaptureFounderEmergence(state)));

    private void ClearFounderEmergence()
    {
        emergenceSchedule=null; emergenceSelectionRandom=emergenceTraitsRandom=emergenceVisualRandom=null;
        emergenceVents=Array.Empty<FounderVentCandidate>(); occupiedEmergenceVents=Array.Empty<bool>(); cachedOutletCount=-1;
        livingDirectFounders.Clear(); directFounderNodes.Clear(); ventNeighborhoods.Clear();
        nextCandidateRefresh=emergencePlacementTime=emergenceScheduledTime=0d;
        emergenceAttempts=emergenceSuccesses=emergenceRejectedCap=emergenceRejectedHabitat=0;
        emergenceFounderDeaths=emergenceFounderBirths=emergenceDescendantBirths=emergenceCandidateRefreshes=0;
        firstEmergenceTime=firstEmergenceScheduledTime=-1d; firstEmergenceNode=-1;
    }
}
