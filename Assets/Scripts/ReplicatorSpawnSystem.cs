using System;
using UnityEngine;

public class ReplicatorSpawnSystem
{
    private float spawnAttemptTimer;
    private bool firstSpontaneousSpawnHappened;

    public void HandleSpontaneousSpawning(
        bool enableSpontaneousSpawning,
        float guaranteedFirstSpawnWithinSeconds,
        float spawnAttemptInterval,
        Func<bool> tryGuaranteedSpawn,
        Func<bool> tryRandomSpontaneousSpawn)
    {
        if (!enableSpontaneousSpawning)
        {
            return;
        }

        if (!firstSpontaneousSpawnHappened && Time.timeSinceLevelLoad >= guaranteedFirstSpawnWithinSeconds)
        {
            if (tryGuaranteedSpawn())
            {
                firstSpontaneousSpawnHappened = true;
            }
        }

        float interval = Mathf.Max(0.05f, spawnAttemptInterval);
        spawnAttemptTimer += Time.deltaTime;

        while (spawnAttemptTimer >= interval)
        {
            spawnAttemptTimer -= interval;

            if (tryRandomSpontaneousSpawn())
            {
                firstSpontaneousSpawnHappened = true;
            }
        }
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
