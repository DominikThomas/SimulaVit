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
        float reproductionChance = reproductionRate * deltaTime;
        float organicCSum = 0f;
        int eligibleForDivisionCount = 0;

        populationState.EnsureMatchesAgentCount(agents);

        for (int i = agents.Count - 1; i >= 0; i--)
        {
            Replicator agent = agents[i];
            float updatedAge = populationState.Age[i] + deltaTime;
            populationState.Age[i] = updatedAge;

            if (updatedAge > agent.maxLifespan)
            {
                registerDeathCause(populationState.Metabolism[i], DeathCause.OldAge);
                populationState.CopyToDebugState(i, agent);
                depositDeathOrganicC(agent);
                populationState.RemoveAgentAtSwapBack(agents, i);
                continue;
            }

            float lifeRemaining = agent.maxLifespan - updatedAge;
            populationState.Color[i] = calculateAgentColor(updatedAge, lifeRemaining, populationState.Energy[i], populationState.Metabolism[i]);

            organicCSum += Mathf.Max(0f, populationState.OrganicCStore[i]);

            bool canReplicate = populationState.CanReplicate[i];
            bool hasEnergyForDivision = canReplicate && (enableCarbonLimitedDivision
                ? populationState.Energy[i] >= Mathf.Max(0f, divisionEnergyCost)
                : populationState.Energy[i] >= replicationEnergyCost);

            bool hasCarbonForDivision = true;
            if (enableCarbonLimitedDivision)
            {
                float target = Mathf.Max(0.0001f, agent.biomassTarget);
                float divisionThreshold = Mathf.Max(1f, divisionBiomassMultiple) * target;
                hasCarbonForDivision = populationState.OrganicCStore[i] >= divisionThreshold;
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

                if (insideOptimalBand)
                {
                    populationState.CopyToDebugState(i, agent);
                    if (trySpawnChild(agent, out Replicator childAgent))
                    {
                        if (enableCarbonLimitedDivision)
                        {
                            populationState.Energy[i] = Mathf.Max(0f, populationState.Energy[i] - Mathf.Max(0f, divisionEnergyCost));

                            float totalC = Mathf.Max(0f, populationState.OrganicCStore[i]);
                            float toChild = totalC * Mathf.Clamp01(divisionCarbonSplitToChild);
                            childAgent.organicCStore = Mathf.Clamp(toChild, 0f, maxOrganicCStore);
                            populationState.OrganicCStore[i] = Mathf.Max(0f, totalC - toChild);
                            int childIndex = populationState.Count - 1;
                            if (childIndex >= 0)
                            {
                                populationState.OrganicCStore[childIndex] = childAgent.organicCStore;
                            }
                        }
                        else
                        {
                            populationState.Energy[i] = Mathf.Max(0f, populationState.Energy[i] - replicationEnergyCost);
                        }
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
}
