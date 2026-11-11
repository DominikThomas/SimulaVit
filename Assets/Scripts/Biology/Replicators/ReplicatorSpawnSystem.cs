using System;
using UnityEngine;

public class ReplicatorSpawnSystem
{
    private float simulationElapsedSeconds;
    private float spawnAttemptTimer;
    private bool firstSpontaneousSpawnHappened;
    private bool spontaneousSpawningAllowedByPopulation = true;

    public void HandleSpontaneousSpawning(
        bool enableSpontaneousSpawning,
        float guaranteedFirstSpawnWithinSeconds,
        float spawnAttemptInterval,
        float simulationDeltaSeconds,
        Func<int> getTotalPopulation,
        int disableSpontaneousSpawningAtPopulation,
        int reenableSpontaneousSpawningAtPopulation,
        Func<bool> tryGuaranteedSpawn,
        Func<bool> tryRandomSpontaneousSpawn)
    {
        if (!enableSpontaneousSpawning)
        {
            return;
        }

        if (!UpdatePopulationHysteresis(
                getTotalPopulation(),
                disableSpontaneousSpawningAtPopulation,
                reenableSpontaneousSpawningAtPopulation))
        {
            return;
        }

        float stepSimulationSeconds = Mathf.Max(0f, simulationDeltaSeconds);
        simulationElapsedSeconds += stepSimulationSeconds;

        if (!firstSpontaneousSpawnHappened && simulationElapsedSeconds >= guaranteedFirstSpawnWithinSeconds)
        {
            if (tryGuaranteedSpawn())
            {
                firstSpontaneousSpawnHappened = true;
            }
        }

        float interval = Mathf.Max(0.05f, spawnAttemptInterval);
        spawnAttemptTimer += stepSimulationSeconds;

        while (spawnAttemptTimer >= interval)
        {
            spawnAttemptTimer -= interval;

            if (!UpdatePopulationHysteresis(
                    getTotalPopulation(),
                    disableSpontaneousSpawningAtPopulation,
                    reenableSpontaneousSpawningAtPopulation))
            {
                break;
            }

            if (tryRandomSpontaneousSpawn())
            {
                firstSpontaneousSpawnHappened = true;
            }
        }
    }

    private bool UpdatePopulationHysteresis(
        int totalPopulation,
        int disableSpontaneousSpawningAtPopulation,
        int reenableSpontaneousSpawningAtPopulation)
    {
        int disableThreshold = Mathf.Max(0, disableSpontaneousSpawningAtPopulation);
        int reenableThreshold = Mathf.Clamp(reenableSpontaneousSpawningAtPopulation, 0, disableThreshold);

        if (spontaneousSpawningAllowedByPopulation)
        {
            if (totalPopulation >= disableThreshold)
            {
                spontaneousSpawningAllowedByPopulation = false;
            }
        }
        else if (totalPopulation <= reenableThreshold)
        {
            spontaneousSpawningAllowedByPopulation = true;
        }

        return spontaneousSpawningAllowedByPopulation;
    }

    public bool TryRandomSpontaneousSpawn(
        int agentCount,
        int maxPopulation,
        float spontaneousSpawnChance,
        Func<Vector3> getSpawnDirectionCandidate,
        Func<Vector3, bool> isSeaLocation,
        Func<bool, float> getLocationSpawnMultiplier,
        Func<Vector3, bool> isCandidateViable,
        int candidateRetries,
        Func<Vector3, bool> trySpawnHydrogenotrophAtDirection)
    {
        if (agentCount >= maxPopulation)
        {
            return false;
        }

        int retries = Mathf.Max(1, candidateRetries);

        for (int attempt = 0; attempt < retries; attempt++)
        {
            Vector3 randomDir = getSpawnDirectionCandidate();

            if (!isCandidateViable(randomDir))
            {
                continue;
            }

            bool isSea = isSeaLocation(randomDir);
            float locationMultiplier = getLocationSpawnMultiplier(isSea);
            float spawnChance = Mathf.Clamp01(spontaneousSpawnChance * locationMultiplier);

            if (UnityEngine.Random.value >= spawnChance)
            {
                continue;
            }

            if (trySpawnHydrogenotrophAtDirection(randomDir))
            {
                return true;
            }
        }

        return false;
    }
}
