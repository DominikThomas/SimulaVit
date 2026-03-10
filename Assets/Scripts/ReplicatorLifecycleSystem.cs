using System;
using System.Collections.Generic;
using UnityEngine;

public class ReplicatorLifecycleSystem
{
    public delegate bool SpawnAgentFromParentDelegate(Replicator parent, out Replicator childAgent);

    public void UpdateLifecycle(
        List<Replicator> agents,
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

        for (int i = agents.Count - 1; i >= 0; i--)
        {
            Replicator agent = agents[i];
            agent.age += deltaTime;

            if (agent.age > agent.maxLifespan)
            {
                registerDeathCause(agent.metabolism, DeathCause.OldAge);
                depositDeathOrganicC(agent);
                agents.RemoveAt(i);
                continue;
            }

            float lifeRemaining = agent.maxLifespan - agent.age;
            agent.color = calculateAgentColor(agent.age, lifeRemaining, agent.energy, agent.metabolism);

            organicCSum += Mathf.Max(0f, agent.organicCStore);

            bool hasEnergyForDivision = enableCarbonLimitedDivision
                ? agent.energy >= Mathf.Max(0f, divisionEnergyCost)
                : agent.energy >= replicationEnergyCost;

            bool hasCarbonForDivision = true;
            if (enableCarbonLimitedDivision)
            {
                float target = Mathf.Max(0.0001f, agent.biomassTarget);
                float divisionThreshold = Mathf.Max(1f, divisionBiomassMultiple) * target;
                hasCarbonForDivision = agent.organicCStore >= divisionThreshold;
                if (hasCarbonForDivision)
                {
                    eligibleForDivisionCount++;
                }
            }

            if (UnityEngine.Random.value < reproductionChance && hasEnergyForDivision && hasCarbonForDivision)
            {
                int safeResolution = Mathf.Max(1, resolution);
                Vector3 dir = agent.position.normalized;
                int cellIndex = PlanetGridIndexing.DirectionToCellIndex(dir, safeResolution);
                float temp = getTemperatureAtCell(dir, cellIndex);

                float min = agent.optimalTempMin;
                float max = agent.optimalTempMax;

                bool insideOptimalBand = (temp >= min && temp <= max);

                if (insideOptimalBand && trySpawnChild(agent, out Replicator childAgent))
                {
                    if (enableCarbonLimitedDivision)
                    {
                        agent.energy = Mathf.Max(0f, agent.energy - Mathf.Max(0f, divisionEnergyCost));

                        float totalC = Mathf.Max(0f, agent.organicCStore);
                        float toChild = totalC * Mathf.Clamp01(divisionCarbonSplitToChild);
                        childAgent.organicCStore = Mathf.Clamp(toChild, 0f, maxOrganicCStore);
                        agent.organicCStore = Mathf.Max(0f, totalC - toChild);
                    }
                    else
                    {
                        agent.energy = Mathf.Max(0f, agent.energy - replicationEnergyCost);
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
