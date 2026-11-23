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
        public long thermalCandidateRejections, foodCandidateRejections, invalidCandidateRejections, placementFailuresWithThermalRejections;
        public int directlySeededSystems, neverSeededSystems, directlySeededOutlets, neverSeededOutlets;
        public long minFoundersPerSystem, maxFoundersPerSystem, minFoundersPerOutlet, maxFoundersPerOutlet;
        public double meanFoundersPerSystem, meanFoundersPerOutlet;
    }

    private struct FounderVentCandidate
    {
        public GeodesicVentSourceOutlet outlet;
        public double weight, initialRetryTime;
        public int initialPlacementFailures;
    }
    private sealed class FounderVentSystemCandidate
    {
        public int systemIndex;
        public double initialWeight;
        public readonly List<int> vents = new List<int>();
    }
    private GeodesicFounderEmergenceSchedule emergenceSchedule;
    private System.Random emergenceSelectionRandom, emergenceTraitsRandom, emergenceVisualRandom;
    private FounderVentCandidate[] emergenceVents = Array.Empty<FounderVentCandidate>();
    private FounderVentSystemCandidate[] emergenceSystems = Array.Empty<FounderVentSystemCandidate>();
    // World identities, independent of the filtered candidate-array positions. Cleared by Initialize/Clear.
    private readonly Dictionary<int, long> founderPlacementsBySystem = new Dictionary<int, long>();
    private readonly Dictionary<int, long> founderPlacementsByOutlet = new Dictionary<int, long>();
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
        if (!availability.AllowsFounders) return;
        emergenceSchedule = new GeodesicFounderEmergenceSchedule();
        emergenceSchedule.Configure(biologySeed, 0d, delay, initialCount, meanInterval, ongoing, ongoingInterval);
        emergenceSelectionRandom = new System.Random(unchecked(biologySeed ^ 0x56454E54));
        emergenceTraitsRandom = new System.Random(unchecked(biologySeed ^ 0x464F554E));
        emergenceVisualRandom = new System.Random(unchecked(biologySeed ^ 0x56495355));
        if (biologyReportsEnabled)
            Debug.Log(FormattableString.Invariant($"[GeodesicFounderEmergenceConfig] mode=Gradual initialEventCap={Math.Max(0,initialCount)} delay={delay:G9} meanInitialInterval={GeodesicFounderEmergenceSchedule.NormalizeInterval(meanInterval):G9} ongoing={ongoing} meanOngoingInterval={GeodesicFounderEmergenceSchedule.NormalizeInterval(ongoingInterval,300f):G9} firstDue={emergenceSchedule.NextEventTime:G9} seed={biologySeed} rejectedEventsConsumeCap=true maxEventsPerStep=64 viabilityCacheSeconds=10"));
    }

    public void AdvanceFounderEmergence(double simulationTime, int maximumPopulation, float co2Need, float h2Need,
        bool oxygenInhibition, float oxygenComfort, float oxygenStress, float oxygenFloor,
        List<Replicator> agents, ReplicatorPopulationState state)
    {
        if (emergenceSchedule == null || grid == null) return;
        founderCO2Need=co2Need; founderH2Need=h2Need;
        // Fixed work bound for pathological intervals/backlogs. Remaining due events stay queued.
        for (int n = 0; n < 64; n++)
        {
            // Capture before consumption so the final initial event still uses initial selection.
            bool initialEvent = emergenceSchedule.InitialEventsRemaining > 0;
            if (!emergenceSchedule.TryConsume(simulationTime, out double due)) break;
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
                if (initialEvent) selected = SelectInitialFounderVent(simulationTime);
                else
                {
                    // Existing ongoing-emergence selection: global production/readiness weights.
                    double total = 0d;
                    for (int i = 0; i < emergenceVents.Length; i++) total += emergenceVents[i].weight;
                    if (!(total > 0d)) break;
                    double pick = emergenceSelectionRandom.NextDouble() * total;
                    for (int i = 0; i < emergenceVents.Length; i++)
                    {
                        pick -= emergenceVents[i].weight;
                        if (pick < 0d) { selected = i; break; }
                    }
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
            if(packed<0)
            {
                emergenceRejectedHabitat++; emergenceVents[selected].weight=0d;
                if (initialEvent)
                {
                    // A food-ready outlet can still have no valid thermal position. Back off repeated
                    // failures for 10, 20, 40, 80, 160, then 300 simulated seconds; never retry this event.
                    int failures = Math.Min(6, emergenceVents[selected].initialPlacementFailures + 1);
                    emergenceVents[selected].initialPlacementFailures = failures;
                    emergenceVents[selected].initialRetryTime = simulationTime + Math.Min(300d, 10d * (1 << (failures - 1)));
                }
                continue;
            }
            if (initialEvent)
            {
                emergenceVents[selected].initialPlacementFailures = 0;
                emergenceVents[selected].initialRetryTime = 0d;
            }
            InitializePassiveSchedulesAtSpawn(state, packed, passiveMovementTime);
        }
    }

    private double InitialFounderOutletWeight(int index, double simulationTime)
    {
        var candidate = emergenceVents[index];
        if (!(candidate.weight > 0d) || simulationTime < candidate.initialRetryTime) return 0d;
        return 1d / (1d + founderPlacementsByOutlet[candidate.outlet.SourceNode]);
    }

    private int SelectInitialFounderVent(double simulationTime)
    {
        double total = 0d;
        for (int i = 0; i < emergenceSystems.Length; i++)
        {
            var system = emergenceSystems[i];
            bool eligible = false;
            for (int k = 0; k < system.vents.Count; k++)
                if (InitialFounderOutletWeight(system.vents[k], simulationTime) > 0d) { eligible = true; break; }
            // Count the system once, regardless of outlet count, production share or food abundance.
            system.initialWeight = eligible ? 1d / (1d + founderPlacementsBySystem[system.systemIndex]) : 0d;
            total += system.initialWeight;
        }
        if (!(total > 0d)) return -1;
        double pick = emergenceSelectionRandom.NextDouble() * total;
        for (int i = 0; i < emergenceSystems.Length; i++)
        {
            var system = emergenceSystems[i];
            pick -= system.initialWeight;
            if (pick >= 0d) continue;
            double outletTotal = 0d;
            for (int k = 0; k < system.vents.Count; k++)
                outletTotal += InitialFounderOutletWeight(system.vents[k], simulationTime);
            double outletPick = emergenceSelectionRandom.NextDouble() * outletTotal;
            for (int k = 0; k < system.vents.Count; k++)
            {
                int index = system.vents[k];
                outletPick -= InitialFounderOutletWeight(index, simulationTime);
                if (outletPick < 0d) return index;
            }
            return -1;
        }
        return -1;
    }

    private double ViableFounderWeight(GeodesicVentSourceOutlet outlet, float co2Need, float h2Need,
        bool oxygenInhibition, float oxygenComfort, float oxygenStress, float oxygenFloor)
    {
        int cell = outlet.SourceNode / grid.MaximumLayerCount, layer = outlet.SourceNode % grid.MaximumLayerCount;
        if (!grid.IsNodeActive(cell,layer)) return 0d;
        // Rank cached resource opportunity. Thermal suitability is resolved at candidate positions,
        // never at the source center (which is expected to be hot).
        double ready=FounderFoodReadiness(cell,layer,co2Need,h2Need);
        for(int k=0;k<grid.SourceTopology.NeighborCounts[cell];k++) {
            int neighbor=grid.SourceTopology.Neighbors6[cell*6+k];
            ready=Math.Max(ready,FounderFoodReadiness(neighbor,grid.GetBottomLayerIndex(neighbor),co2Need,h2Need));
        }
        if(ready<1d) return 0d;
        resources.TryGetConcentration(cell,layer,GeodesicOceanResource.O2,out float o2);
        float oxygen=oxygenInhibition ? CalculateAnaerobeO2Efficiency(o2,oxygenComfort,oxygenStress,oxygenFloor) : 1f;
        if(!float.IsFinite(oxygen) || oxygen<=0f) return 0d;
        double production=(double)outlet.SystemBudgetWeight*outlet.WithinSystemWeight;
        return double.IsFinite(production) && production>0d ? production*(ready/(1d+ready))*oxygen : 0d;
    }

    private void EnsureFounderVentCatalog()
    {
        if (grid == null || resources == null || cachedOutletCount == resources.CompactOutletCount) return;
        cachedOutletCount = resources.CompactOutletCount;
        var candidates = new List<FounderVentCandidate>();
        var systems = new List<FounderVentSystemCandidate>();
        var systemLookup = new Dictionary<int, int>();
        // Include never-eligible systems/outlets (and systems without outlets) in coverage statistics.
        for (int i = 0; i < resources.VentCount; i++)
            if (resources.TryGetVentSystem(i, out var system) && system.Habitat == GeodesicVentHabitat.Submarine
                && !founderPlacementsBySystem.ContainsKey(i)) founderPlacementsBySystem.Add(i, 0L);
        for (int i = 0; i < cachedOutletCount; i++)
        {
            if (!resources.TryGetVentOutlet(i, out var outlet) || outlet.Habitat != GeodesicVentHabitat.Submarine) continue;
            if (!founderPlacementsBySystem.ContainsKey(outlet.SystemIndex)) founderPlacementsBySystem.Add(outlet.SystemIndex, 0L);
            if (!founderPlacementsByOutlet.ContainsKey(outlet.SourceNode)) founderPlacementsByOutlet.Add(outlet.SourceNode, 0L);
            if (!TryResolveFounderVent(i, out outlet, out _, out _)) continue;
            if (!systemLookup.TryGetValue(outlet.SystemIndex, out int systemIndex))
            {
                systemIndex = systems.Count; systemLookup.Add(outlet.SystemIndex, systemIndex);
                systems.Add(new FounderVentSystemCandidate { systemIndex = outlet.SystemIndex });
            }
            systems[systemIndex].vents.Add(candidates.Count);
            candidates.Add(new FounderVentCandidate { outlet=outlet });
        }
        // Preserve resource-catalog order; never use dictionary enumeration for random selection.
        emergenceSystems = systems.ToArray();
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

    private void RegisterFounderPlacement(long id, GeodesicVentSourceOutlet outlet)
    {
        int node = outlet.SourceNode;
        founderPlacementsBySystem.TryGetValue(outlet.SystemIndex, out long systemCount);
        founderPlacementsByOutlet.TryGetValue(node, out long outletCount);
        founderPlacementsBySystem[outlet.SystemIndex] = systemCount + 1;
        founderPlacementsByOutlet[node] = outletCount + 1;
        emergenceSuccesses++; livingDirectFounders.Add(id); directFounderNodes.Add(node);
        if (firstEmergenceNode >= 0) return;
        firstEmergenceNode = node; firstEmergenceTime = emergencePlacementTime; firstEmergenceScheduledTime = emergenceScheduledTime;
    }
    internal void RecordCappedFounderEvents(int count)
    { emergenceAttempts += count; emergenceRejectedCap += count; }
    private void RecordFounderDeath(long id) { if (livingDirectFounders.Remove(id)) emergenceFounderDeaths++; }
    private void RecordFounderParentBirth(long id)
    { if (livingDirectFounders.Contains(id)) emergenceFounderBirths++; else emergenceDescendantBirths++; }

    private static void FounderPlacementStatistics(Dictionary<int, long> counts, out int seeded,
        out long minimum, out double mean, out long maximum)
    {
        seeded = 0; minimum = counts.Count > 0 ? long.MaxValue : 0; maximum = 0;
        long total = 0;
        foreach (long count in counts.Values)
        {
            if (count > 0) seeded++;
            minimum = Math.Min(minimum, count); maximum = Math.Max(maximum, count); total += count;
        }
        mean = counts.Count > 0 ? total / (double)counts.Count : 0d;
    }

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
        FounderPlacementStatistics(founderPlacementsBySystem, out int seededSystems,
            out long systemMin, out double systemMean, out long systemMax);
        FounderPlacementStatistics(founderPlacementsByOutlet, out int seededOutlets,
            out long outletMin, out double outletMean, out long outletMax);
        return new FounderEmergenceSnapshot { attempts=emergenceAttempts, successes=emergenceSuccesses,
            rejectedPopulationCap=emergenceRejectedCap, rejectedHabitat=emergenceRejectedHabitat,
            firstEmergenceTime=firstEmergenceTime, firstScheduledTime=firstEmergenceScheduledTime, firstVentNode=firstEmergenceNode,
            distinctFounderVents=directFounderNodes.Count, inhabitedVents=inhabited, indirectlyColonizedVents=indirect,
            unoccupiedVents=emergenceVents.Length-inhabited, submarineOutlets=emergenceVents.Length,
            livingFounders=livingDirectFounders.Count, founderDeaths=emergenceFounderDeaths,
            founderParentBirths=emergenceFounderBirths, descendantParentBirths=emergenceDescendantBirths,
            initialEventsRemaining=emergenceSchedule?.InitialEventsRemaining??0, candidateRefreshes=emergenceCandidateRefreshes,
            directlySeededSystems=seededSystems, neverSeededSystems=founderPlacementsBySystem.Count-seededSystems,
            directlySeededOutlets=seededOutlets, neverSeededOutlets=founderPlacementsByOutlet.Count-seededOutlets,
            minFoundersPerSystem=systemMin, meanFoundersPerSystem=systemMean, maxFoundersPerSystem=systemMax,
            minFoundersPerOutlet=outletMin, meanFoundersPerOutlet=outletMean, maxFoundersPerOutlet=outletMax,
            thermalCandidateRejections=founderThermalCandidateRejections, foodCandidateRejections=founderFoodCandidateRejections,
            invalidCandidateRejections=founderInvalidCandidateRejections, placementFailuresWithThermalRejections=founderThermalPlacementFailures };
    }

    private void LogFounderEmergence(ReplicatorPopulationState state) =>
        Debug.Log("[GeodesicFounderEmergence] cumulative=true occupancy=outlet-plus-one-ring-bottom-two-layers " + JsonUtility.ToJson(CaptureFounderEmergence(state)));

    private void ClearFounderEmergence()
    {
        emergenceSchedule=null; emergenceSelectionRandom=emergenceTraitsRandom=emergenceVisualRandom=null;
        emergenceVents=Array.Empty<FounderVentCandidate>(); occupiedEmergenceVents=Array.Empty<bool>(); cachedOutletCount=-1;
        emergenceSystems=Array.Empty<FounderVentSystemCandidate>();
        founderPlacementsBySystem.Clear(); founderPlacementsByOutlet.Clear();
        livingDirectFounders.Clear(); directFounderNodes.Clear(); ventNeighborhoods.Clear();
        nextCandidateRefresh=emergencePlacementTime=emergenceScheduledTime=0d;
        emergenceAttempts=emergenceSuccesses=emergenceRejectedCap=emergenceRejectedHabitat=0;
        emergenceFounderDeaths=emergenceFounderBirths=emergenceDescendantBirths=emergenceCandidateRefreshes=0;
        firstEmergenceTime=firstEmergenceScheduledTime=-1d; firstEmergenceNode=-1;
    }
}
