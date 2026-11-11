using System;
using System.Collections.Generic;
using UnityEngine;

public class ReplicatorPredationSystem
{
    private readonly List<int> localPredationCandidates = new List<int>(64);
    private readonly HashSet<int> pendingPredationRemovals = new HashSet<int>();
    private readonly List<int> predationRemovalBuffer = new List<int>(256);

    public struct Settings
    {
        public bool EnablePredators;
        public bool UseScentPredation;
        public int AdjacentLayerSearchDepth;
        public float PredatorBiteOrganicC;
        public float PredatorBiteEnergy;
        public float PredatorAssimilationFraction;
        public float PredatorAttackCooldownSeconds;
        public float PredatorEnergyPerC;
        public float MaxOrganicCStore;
        public float PredatorKillEnergyThreshold;
    }

    public void RunPredationPass(
        List<Replicator> agents,
        ReplicatorPopulationState populationState,
        Settings settings,
        float deltaTime,
        int resolution,
        PlanetResourceMap planetResourceMap,
        Dictionary<long, List<int>> preyAgentsBySpatialBin,
        Action<MetabolismType, DeathCause> registerDeathCause,
        Action<Replicator> depositDeathOrganicC,
        Action<Replicator, float> depositPredationOrganicC,
        ref int predationKillsWindow)
    {
        if (!settings.EnablePredators || agents.Count <= 1 || populationState == null)
        {
            return;
        }

        populationState.EnsureMatchesAgentCount(agents);

        float biteOrganicC = Mathf.Max(0f, settings.PredatorBiteOrganicC);
        float biteEnergy = Mathf.Max(0f, settings.PredatorBiteEnergy);
        float assimilation = Mathf.Clamp01(settings.PredatorAssimilationFraction);
        float cooldownSeconds = Mathf.Max(0f, settings.PredatorAttackCooldownSeconds);
        float maxStore = Mathf.Max(0f, settings.MaxOrganicCStore);
        pendingPredationRemovals.Clear();

        if (!settings.UseScentPredation)
        {
            return;
        }

        for (int i = populationState.Count - 1; i >= 0; i--)
        {
            if (populationState.Metabolism[i] != MetabolismType.Predation)
            {
                continue;
            }

            populationState.AttackCooldown[i] = Mathf.Max(0f, populationState.AttackCooldown[i] - deltaTime);
            if (populationState.AttackCooldown[i] > 0f)
            {
                continue;
            }

            int predatorCell = PlanetGridIndexing.DirectionToCellIndex(populationState.Position[i].normalized, resolution);
            int predatorLayer = ResolvePredationLayerIndex(planetResourceMap, predatorCell, populationState.CurrentOceanLayerIndex[i]);
            if (!TryBuildPredationCandidates(
                    predatorIndex: i,
                    predatorCell: predatorCell,
                    predatorLayer: predatorLayer,
                    settings: settings,
                    preyAgentsBySpatialBin: preyAgentsBySpatialBin,
                    localPredationCandidates: localPredationCandidates,
                    pendingPredationRemovals: pendingPredationRemovals))
            {
                continue;
            }

            int preyIndex = localPredationCandidates[UnityEngine.Random.Range(0, localPredationCandidates.Count)];
            if (preyIndex < 0 || preyIndex >= populationState.Count)
            {
                continue;
            }

            Replicator preyVictim = agents[preyIndex];
            float leakedOrganicC = ApplyPredationBite(populationState, i, preyIndex, biteOrganicC, biteEnergy, assimilation, maxStore);
            if (leakedOrganicC > 0f)
            {
                populationState.CopyToDebugState(preyIndex, preyVictim);
                depositPredationOrganicC?.Invoke(preyVictim, leakedOrganicC);
            }

            populationState.AttackCooldown[i] = cooldownSeconds;
            populationState.CopyPredationEntryToAgent(i, agents[i]);
            populationState.CopyPredationEntryToAgent(preyIndex, preyVictim);

            if (populationState.Energy[preyIndex] <= Mathf.Max(0f, settings.PredatorKillEnergyThreshold))
            {
                registerDeathCause(populationState.Metabolism[preyIndex], DeathCause.Predation);

                populationState.CopyToDebugState(preyIndex, preyVictim);
                depositDeathOrganicC(preyVictim);

                pendingPredationRemovals.Add(preyIndex);
                predationKillsWindow++;
            }
        }

        RemovePredationVictims(agents, populationState, pendingPredationRemovals, predationRemovalBuffer);
    }

    private bool TryBuildPredationCandidates(
        int predatorIndex,
        int predatorCell,
        int predatorLayer,
        Settings settings,
        Dictionary<long, List<int>> preyAgentsBySpatialBin,
        List<int> localPredationCandidates,
        HashSet<int> pendingPredationRemovals)
    {
        localPredationCandidates.Clear();
        if (preyAgentsBySpatialBin == null || preyAgentsBySpatialBin.Count == 0)
        {
            return false;
        }

        CollectPreyCandidatesForLayer(
            predatorIndex,
            predatorCell,
            predatorLayer,
            preyAgentsBySpatialBin,
            localPredationCandidates,
            pendingPredationRemovals);

        if (localPredationCandidates.Count > 0)
        {
            return true;
        }

        int adjacentDepth = Mathf.Max(0, settings.AdjacentLayerSearchDepth);
        if (adjacentDepth <= 0 || predatorLayer < 0)
        {
            return false;
        }

        for (int offset = 1; offset <= adjacentDepth; offset++)
        {
            CollectPreyCandidatesForLayer(
                predatorIndex,
                predatorCell,
                predatorLayer - offset,
                preyAgentsBySpatialBin,
                localPredationCandidates,
                pendingPredationRemovals);
            CollectPreyCandidatesForLayer(
                predatorIndex,
                predatorCell,
                predatorLayer + offset,
                preyAgentsBySpatialBin,
                localPredationCandidates,
                pendingPredationRemovals);
        }

        return localPredationCandidates.Count > 0;
    }

    private void CollectPreyCandidatesForLayer(
        int predatorIndex,
        int cellIndex,
        int layerIndex,
        Dictionary<long, List<int>> preyAgentsBySpatialBin,
        List<int> localPredationCandidates,
        HashSet<int> pendingPredationRemovals)
    {
        long spatialKey = BuildSpatialBinKey(cellIndex, layerIndex);
        if (!preyAgentsBySpatialBin.TryGetValue(spatialKey, out List<int> preyInBin) || preyInBin.Count == 0)
        {
            return;
        }

        for (int preyListIndex = 0; preyListIndex < preyInBin.Count; preyListIndex++)
        {
            int preyIndexCandidate = preyInBin[preyListIndex];
            if (preyIndexCandidate == predatorIndex || pendingPredationRemovals.Contains(preyIndexCandidate))
            {
                continue;
            }

            localPredationCandidates.Add(preyIndexCandidate);
        }
    }

    public static long BuildSpatialBinKey(int cellIndex, int layerIndex)
    {
        unchecked
        {
            uint layer = (uint)(layerIndex + 1);
            return ((long)cellIndex << 32) | layer;
        }
    }

    public static int ResolvePredationLayerIndex(PlanetResourceMap planetResourceMap, int cellIndex, int layerIndex)
    {
        if (planetResourceMap == null || !planetResourceMap.IsOceanCell(cellIndex))
        {
            return -1;
        }

        return planetResourceMap.ClampOceanLayerIndex(cellIndex, layerIndex);
    }

    private float ApplyPredationBite(
        ReplicatorPopulationState populationState,
        int predatorIndex,
        int preyIndex,
        float biteOrganicC,
        float biteEnergy,
        float assimilation,
        float maxStore)
    {
        float preyOrganicC = Mathf.Max(0f, populationState.OrganicCStore[preyIndex]);
        float takeC = Mathf.Min(preyOrganicC, biteOrganicC);
        populationState.OrganicCStore[preyIndex] = Mathf.Max(0f, preyOrganicC - takeC);

        float takeE = 0f;
        if (takeC < biteOrganicC && biteEnergy > 0f)
        {
            float preyEnergy = Mathf.Max(0f, populationState.Energy[preyIndex]);
            takeE = Mathf.Min(preyEnergy, biteEnergy);
            populationState.Energy[preyIndex] = Mathf.Max(0f, preyEnergy - takeE);
        }

        float storedGain = takeC * assimilation;
        populationState.OrganicCStore[predatorIndex] = Mathf.Clamp(populationState.OrganicCStore[predatorIndex] + storedGain, 0f, maxStore);
        float leakedC = takeC - storedGain;
        populationState.Energy[predatorIndex] += takeE * 0.5f;
        return leakedC;
    }

    private void RemovePredationVictims(
        List<Replicator> agents,
        ReplicatorPopulationState populationState,
        HashSet<int> pendingPredationRemovals,
        List<int> predationRemovalBuffer)
    {
        if (pendingPredationRemovals.Count <= 0)
        {
            return;
        }

        predationRemovalBuffer.Clear();
        foreach (int index in pendingPredationRemovals)
        {
            predationRemovalBuffer.Add(index);
        }

        predationRemovalBuffer.Sort();
        for (int i = predationRemovalBuffer.Count - 1; i >= 0; i--)
        {
            int index = predationRemovalBuffer[i];
            int last = agents.Count - 1;
            if (index < 0 || index > last)
            {
                continue;
            }

            if (index != last)
            {
                agents[index] = agents[last];
            }

            agents.RemoveAt(last);
            populationState.RemoveAgentAtSwapBack(index);
        }
    }
}
