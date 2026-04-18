using System;
using System.Collections.Generic;
using UnityEngine;

public class ReplicatorLifecycleSystem
{
    public delegate bool SpawnAgentFromParentDelegate(Replicator parent, out Replicator childAgent);

    public void UpdateLifecycle(
        List<Replicator> agents,
        ReplicatorPopulationState populationState,
        float deltaTime,
        float reproductionRate,
        bool enableCarbonLimitedDivision,
        float divisionEnergyCost,
        float replicationEnergyCost,
        float divisionBiomassMultiple,
        float divisionCarbonSplitToChild,
        float maxOrganicCStore,
        int resolution,
        Func<Vector3, int, float> getTemperatureAtCell,
        Func<float, float, float, MetabolismType, Color> calculateAgentColor,
        SpawnAgentFromParentDelegate trySpawnChild,
        Action<Replicator> depositDeathOrganicC,
        Action<MetabolismType, DeathCause> registerDeathCause,
        out float averageOrganicCStore,
        out int divisionEligibleAgentCount)
    {
        populationState.EnsureMatchesAgentCount(agents);
        float reproductionChance = reproductionRate * deltaTime;
        float organicCSum = 0f;
        int eligibleForDivisionCount = 0;

        for (int i = populationState.Count - 1; i >= 0; i--)
        {
            Replicator agent = agents[i];
            float updatedAge = populationState.Age[i] + deltaTime;
            populationState.Age[i] = updatedAge;
            agent.age = updatedAge;

            if (updatedAge > agent.maxLifespan)
            {
                populationState.CopyToDebugState(i, agent);
                registerDeathCause(populationState.Metabolism[i], DeathCause.OldAge);
                depositDeathOrganicC(agent);
                RemoveAgentAtSwapBack(agents, populationState, i);
                continue;
            }

            float energy = populationState.Energy[i];
            float organicCStore = Mathf.Max(0f, populationState.OrganicCStore[i]);
            float lifeRemaining = agent.maxLifespan - updatedAge;
            Color color = calculateAgentColor(updatedAge, lifeRemaining, energy, populationState.Metabolism[i]);
            populationState.Color[i] = color;
            agent.color = color;

            organicCSum += organicCStore;

            bool canReplicate = populationState.CanReplicate[i];
            bool hasEnergyForDivision = canReplicate && (enableCarbonLimitedDivision
                ? energy >= Mathf.Max(0f, divisionEnergyCost)
                : energy >= Mathf.Max(0f, replicationEnergyCost));

            bool hasCarbonForDivision = true;
            if (enableCarbonLimitedDivision)
            {
                float target = Mathf.Max(0.0001f, agent.biomassTarget);
                float divisionThreshold = Mathf.Max(1f, divisionBiomassMultiple) * target;
                hasCarbonForDivision = organicCStore >= divisionThreshold;
                if (hasCarbonForDivision)
                {
                    eligibleForDivisionCount++;
                }
            }

            if (UnityEngine.Random.value < reproductionChance && hasEnergyForDivision && hasCarbonForDivision)
            {
                int safeResolution = Mathf.Max(1, resolution);
                Vector3 dir = populationState.Position[i].normalized;
                int cellIndex = PlanetGridIndexing.DirectionToCellIndex(dir, safeResolution);
                float temp = getTemperatureAtCell(dir, cellIndex);

                float min = populationState.OptimalTempMin[i];
                float max = populationState.OptimalTempMax[i];

                bool insideOptimalBand = (temp >= min && temp <= max);

                agent.currentDirection = populationState.CurrentDirection[i];
                agent.currentOceanLayerIndex = populationState.CurrentOceanLayerIndex[i];
                agent.preferredOceanLayerIndex = populationState.PreferredOceanLayerIndex[i];
                agent.metabolism = populationState.Metabolism[i];
                agent.locomotion = populationState.Locomotion[i];

                if (insideOptimalBand && trySpawnChild(agent, out Replicator childAgent))
                {
                    if (enableCarbonLimitedDivision)
                    {
                        energy = Mathf.Max(0f, energy - Mathf.Max(0f, divisionEnergyCost));
                        populationState.Energy[i] = energy;
                        agent.energy = energy;

                        float totalC = organicCStore;
                        float toChild = totalC * Mathf.Clamp01(divisionCarbonSplitToChild);
                        childAgent.organicCStore = Mathf.Clamp(toChild, 0f, maxOrganicCStore);
                        organicCStore = Mathf.Max(0f, totalC - toChild);
                        populationState.OrganicCStore[i] = organicCStore;
                        agent.organicCStore = organicCStore;
                    }
                    else
                    {
                        energy = Mathf.Max(0f, energy - Mathf.Max(0f, replicationEnergyCost));
                        populationState.Energy[i] = energy;
                        agent.energy = energy;
                    }
                }
            }
        }

        if (agents.Count > 0)
        {
            averageOrganicCStore = organicCSum / agents.Count;
        }
        else
        {
            averageOrganicCStore = 0f;
        }

        divisionEligibleAgentCount = eligibleForDivisionCount;
    }

    private static void RemoveAgentAtSwapBack(List<Replicator> agents, ReplicatorPopulationState populationState, int index)
    {
        int last = agents.Count - 1;
        if (index < 0 || index > last)
        {
            return;
        }

        if (index != last)
        {
            agents[index] = agents[last];
        }

        agents.RemoveAt(last);
        populationState.RemoveAgentAtSwapBack(index);
    }
}
