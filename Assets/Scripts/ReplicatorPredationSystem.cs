using System;
using System.Collections.Generic;
using UnityEngine;

public class ReplicatorPredationSystem
{
    public struct Settings
    {
        public bool EnablePredators;
        public bool UseScentPredation;
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
        Settings settings,
        float deltaTime,
        int resolution,
        Dictionary<int, List<int>> preyAgentsByCell,
        List<int> localPredationCandidates,
        HashSet<int> pendingPredationRemovals,
        List<int> predationRemovalBuffer,
        Func<Replicator, bool> isPredator,
        Action<MetabolismType, DeathCause> registerDeathCause,
        Action<Replicator> depositDeathOrganicC,
        ref int predationKillsWindow)
    {
        if (!settings.EnablePredators || agents.Count <= 1)
        {
            return;
        }

        float biteOrganicC = Mathf.Max(0f, settings.PredatorBiteOrganicC);
        float biteEnergy = Mathf.Max(0f, settings.PredatorBiteEnergy);
        float assimilation = Mathf.Clamp01(settings.PredatorAssimilationFraction);
        float cooldownSeconds = Mathf.Max(0f, settings.PredatorAttackCooldownSeconds);
        float energyPerC = Mathf.Max(0f, settings.PredatorEnergyPerC);
        float maxStore = Mathf.Max(0f, settings.MaxOrganicCStore);
        pendingPredationRemovals.Clear();

        if (!settings.UseScentPredation)
        {
            return;
        }

        for (int i = agents.Count - 1; i >= 0; i--)
        {
            Replicator predator = agents[i];
            if (!isPredator(predator))
            {
                continue;
            }

            predator.attackCooldown = Mathf.Max(0f, predator.attackCooldown - deltaTime);
            if (predator.attackCooldown > 0f)
            {
                continue;
            }

            int predatorCell = PlanetGridIndexing.DirectionToCellIndex(predator.position.normalized, resolution);
            if (!preyAgentsByCell.TryGetValue(predatorCell, out List<int> preyInCell) || preyInCell.Count == 0)
            {
                continue;
            }

            if (!TryBuildPredationCandidates(i, preyInCell, localPredationCandidates, pendingPredationRemovals))
            {
                continue;
            }

            int preyIndex = localPredationCandidates[UnityEngine.Random.Range(0, localPredationCandidates.Count)];
            if (preyIndex < 0 || preyIndex >= agents.Count)
            {
                continue;
            }

            Replicator preyVictim = agents[preyIndex];
            ApplyPredationBite(predator, preyVictim, biteOrganicC, biteEnergy, assimilation, energyPerC, maxStore);
            predator.attackCooldown = cooldownSeconds;

            if (preyVictim.energy <= Mathf.Max(0f, settings.PredatorKillEnergyThreshold))
            {
                registerDeathCause(preyVictim.metabolism, DeathCause.Predation);
                depositDeathOrganicC(preyVictim);
                pendingPredationRemovals.Add(preyIndex);
                predationKillsWindow++;
            }
        }

        RemovePredationVictims(agents, pendingPredationRemovals, predationRemovalBuffer);
    }

    private bool TryBuildPredationCandidates(
        int predatorIndex,
        List<int> preyInCell,
        List<int> localPredationCandidates,
        HashSet<int> pendingPredationRemovals)
    {
        localPredationCandidates.Clear();

        for (int preyListIndex = 0; preyListIndex < preyInCell.Count; preyListIndex++)
        {
            int preyIndexCandidate = preyInCell[preyListIndex];
            if (preyIndexCandidate == predatorIndex || pendingPredationRemovals.Contains(preyIndexCandidate))
            {
                continue;
            }

            localPredationCandidates.Add(preyIndexCandidate);
        }

        return localPredationCandidates.Count > 0;
    }

    private void ApplyPredationBite(
        Replicator predator,
        Replicator preyVictim,
        float biteOrganicC,
        float biteEnergy,
        float assimilation,
        float energyPerC,
        float maxStore)
    {
        float takeC = Mathf.Min(Mathf.Max(0f, preyVictim.organicCStore), biteOrganicC);
        preyVictim.organicCStore = Mathf.Max(0f, preyVictim.organicCStore - takeC);

        float takeE = 0f;
        if (takeC < biteOrganicC && biteEnergy > 0f)
        {
            takeE = Mathf.Min(Mathf.Max(0f, preyVictim.energy), biteEnergy);
            preyVictim.energy = Mathf.Max(0f, preyVictim.energy - takeE);
        }

        float storedGain = takeC * assimilation;
        predator.organicCStore = Mathf.Clamp(predator.organicCStore + storedGain, 0f, maxStore);
        float respiredC = takeC - storedGain;
        predator.energy += (respiredC * energyPerC) + (takeE * 0.5f);
    }

    private void RemovePredationVictims(
        List<Replicator> agents,
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
            if (index >= 0 && index < agents.Count)
            {
                agents.RemoveAt(index);
            }
        }
    }
}
