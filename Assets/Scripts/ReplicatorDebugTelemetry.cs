using System;
using System.Collections.Generic;
using UnityEngine;

public class ReplicatorDebugTelemetry
{
    private float metabolismDebugLogTimer;
    private readonly Dictionary<Replicator, Vector3> sessileDebugPositions = new Dictionary<Replicator, Vector3>(512);
    private readonly Dictionary<Replicator, float> sessileDebugTimers = new Dictionary<Replicator, float>(512);
    private readonly HashSet<Replicator> sessileDebugSeen = new HashSet<Replicator>();
    private readonly List<Replicator> staleSessileAgents = new List<Replicator>(128);

    public void LogMetabolismDebugThrottled(
        PlanetGenerator planetGenerator,
        PlanetResourceMap planetResourceMap,
        bool debugVentPlumeDiagnostics,
        int chemosynthAgentCount,
        int hydrogenotrophAgentCount,
        int photosynthAgentCount,
        int saprotrophAgentCount,
        int predatorAgentCount,
        int fermenterAgentCount,
        int methanogenAgentCount,
        int methanotrophAgentCount,
        float debugChemoTempSum,
        int debugChemoTempCount,
        int debugChemoStressedCount,
        float debugHydrogenTempSum,
        int debugHydrogenTempCount,
        int debugHydrogenStressedCount,
        float debugPhotoTempSum,
        int debugPhotoTempCount,
        int debugPhotoStressedCount,
        float debugSaproTempSum,
        int debugSaproTempCount,
        int debugSaproStressedCount,
        float averageOrganicCStore,
        int divisionEligibleAgentCount,
        int predationKillsWindow,
        float avgToxicProteolyticWasteDebug,
        float avgDissolvedOrganicLeakDebug,
        int[] chemoDeathCauseCounts,
        int[] hydrogenDeathCauseCounts,
        int[] photoDeathCauseCounts,
        int[] saproDeathCauseCounts,
        int[] fermentDeathCauseCounts,
        int[] methanogenDeathCauseCounts,
        int[] methanotrophDeathCauseCounts,
        int[] predatorDeathCauseCounts,
        TemperatureDisplayUnit temperatureDisplayUnit,
        Func<bool> isSaprotrophyUnlocked,
        Func<int[], string> formatDeathCauseDistribution,
        Action resetPredationKillsWindow,
        Action resetDeathCauseCounters)
    {
        metabolismDebugLogTimer += Time.deltaTime;
        if (metabolismDebugLogTimer < 3f)
        {
            return;
        }

        metabolismDebugLogTimer = 0f;
        bool unlocked = planetGenerator != null && planetGenerator.PhotosynthesisUnlocked;

        string sulfurTempText = FormatTemperatureDebug(debugChemoTempSum, debugChemoTempCount, debugChemoStressedCount, temperatureDisplayUnit);
        string hydrogenTempText = FormatTemperatureDebug(debugHydrogenTempSum, debugHydrogenTempCount, debugHydrogenStressedCount, temperatureDisplayUnit);
        string photoTempText = FormatTemperatureDebug(debugPhotoTempSum, debugPhotoTempCount, debugPhotoStressedCount, temperatureDisplayUnit);
        string saproTempText = FormatTemperatureDebug(debugSaproTempSum, debugSaproTempCount, debugSaproStressedCount, temperatureDisplayUnit);

        float meanH2 = 0f;
        float maxH2 = 0f;
        float meanH2S = 0f;
        float maxH2S = 0f;
        float avgVentH2S = 0f;
        float avgVentH2 = 0f;
        float avgOceanH2 = 0f;
        float avgOceanH2S = 0f;
        float dissolvedFe2OceanMean = 0f;
        float dissolvedFe2Total = 0f;
        float dissolvedFe2RemainingFraction = 0f;
        if (planetResourceMap != null)
        {
            planetResourceMap.GetVentChemistryStats(out meanH2, out maxH2, out meanH2S, out maxH2S);
            if (debugVentPlumeDiagnostics)
            {
                planetResourceMap.GetVentPlumeDiagnostics(out avgVentH2S, out avgVentH2, out avgOceanH2, out avgOceanH2S);
            }

            dissolvedFe2OceanMean = planetResourceMap.debugDissolvedFe2PlusOceanMean;
            dissolvedFe2Total = planetResourceMap.debugDissolvedFe2PlusTotal;
            dissolvedFe2RemainingFraction = planetResourceMap.debugDissolvedFe2PlusRemainingFraction;
        }

        string plumeDiagnostics = debugVentPlumeDiagnostics
            ? $" plume[ventH2S={avgVentH2S:F3} ventH2={avgVentH2:F3} oceanH2={avgOceanH2:F3} oceanH2S={avgOceanH2S:F3}]"
            : string.Empty;

        Debug.Log(
            $"Metabolism: hydrogen={hydrogenotrophAgentCount} sulfur={chemosynthAgentCount} photo={photosynthAgentCount} sapro={saprotrophAgentCount} predator={predatorAgentCount} ferment={fermenterAgentCount} methanogen={methanogenAgentCount} methanotroph={methanotrophAgentCount} " +
            $"photoUnlocked={unlocked} saproUnlocked={isSaprotrophyUnlocked()} " +
            $"temp[hydrogen:{hydrogenTempText} sulfur:{sulfurTempText} photo:{photoTempText} sapro:{saproTempText}] avgOrganicC={averageOrganicCStore:F3} divisionEligible={divisionEligibleAgentCount} predKillsWindow={predationKillsWindow} avgToxicProteolyticWaste={avgToxicProteolyticWasteDebug:F3} avgDissolvedOrganicLeak={avgDissolvedOrganicLeakDebug:F3} " +
            $"chem[h2Mean={meanH2:F3} h2Max={maxH2:F3} h2sMean={meanH2S:F3} h2sMax={maxH2S:F3} fe2OceanMean={dissolvedFe2OceanMean:F3} fe2Total={dissolvedFe2Total:F1}]" + plumeDiagnostics);
        Debug.Log($"DeathCauses: hydrogen[{formatDeathCauseDistribution(hydrogenDeathCauseCounts)}] sulfur[{formatDeathCauseDistribution(chemoDeathCauseCounts)}] photo[{formatDeathCauseDistribution(photoDeathCauseCounts)}] sapro[{formatDeathCauseDistribution(saproDeathCauseCounts)}] ferment[{formatDeathCauseDistribution(fermentDeathCauseCounts)}] methanogen[{formatDeathCauseDistribution(methanogenDeathCauseCounts)}] methanotroph[{formatDeathCauseDistribution(methanotrophDeathCauseCounts)}] predator[{formatDeathCauseDistribution(predatorDeathCauseCounts)}]");
        Debug.Log($"Atmosphere composition: CO2[{planetResourceMap.debugGlobalCO2}], O2[{planetResourceMap.debugGlobalO2}], CH4[{planetResourceMap.debugGlobalCH4}]");
        Debug.Log($"Ocean chemistry: DissolvedFe2+[{dissolvedFe2OceanMean:F3} avg, {dissolvedFe2Total:F1} total, {(dissolvedFe2RemainingFraction * 100f):F1}% remaining]");

        resetPredationKillsWindow();
        resetDeathCauseCounters();
    }

    public void ValidateSessileMovement(
        bool debugSessileMovement,
        float debugSessileMovementWindowSeconds,
        float debugSessileMovementEpsilon,
        List<Replicator> agents,
        UnityEngine.Object logContext)
    {
        if (!debugSessileMovement)
        {
            return;
        }

        float window = Mathf.Max(0.5f, debugSessileMovementWindowSeconds);
        float epsilon = Mathf.Max(0.00001f, debugSessileMovementEpsilon);
        float epsilonSqr = epsilon * epsilon;
        sessileDebugSeen.Clear();

        for (int i = 0; i < agents.Count; i++)
        {
            Replicator agent = agents[i];
            if (agent.locomotion != LocomotionType.Anchored)
            {
                sessileDebugPositions.Remove(agent);
                sessileDebugTimers.Remove(agent);
                continue;
            }

            sessileDebugSeen.Add(agent);

            if (!sessileDebugPositions.TryGetValue(agent, out Vector3 baseline))
            {
                sessileDebugPositions[agent] = agent.position;
                sessileDebugTimers[agent] = 0f;
                continue;
            }

            float timer = sessileDebugTimers.TryGetValue(agent, out float existingTimer) ? existingTimer : 0f;
            timer += Time.deltaTime;

            if (timer < window)
            {
                sessileDebugTimers[agent] = timer;
                continue;
            }

            float distanceSqr = (agent.position - baseline).sqrMagnitude;
            if (distanceSqr > epsilonSqr)
            {
                Debug.LogWarning($"Anchored replicator drift detected. moved={Mathf.Sqrt(distanceSqr):F6} (> {epsilon:F6}) over {timer:F2}s", logContext);
            }

            sessileDebugPositions[agent] = agent.position;
            sessileDebugTimers[agent] = 0f;
        }

        if (sessileDebugPositions.Count > sessileDebugSeen.Count)
        {
            staleSessileAgents.Clear();
            foreach (Replicator tracked in sessileDebugPositions.Keys)
            {
                if (sessileDebugSeen.Contains(tracked))
                {
                    continue;
                }

                staleSessileAgents.Add(tracked);
            }

            for (int i = 0; i < staleSessileAgents.Count; i++)
            {
                Replicator tracked = staleSessileAgents[i];
                sessileDebugPositions.Remove(tracked);
                sessileDebugTimers.Remove(tracked);
            }
        }
    }

    private static string FormatTemperatureDebug(float tempSum, int count, int stressedCount, TemperatureDisplayUnit temperatureDisplayUnit)
    {
        if (count <= 0)
        {
            return "n/a";
        }

        float averageTemp = tempSum / count;
        float stressedFraction = (float)stressedCount / count;
        return $"avg={ReplicatorManager.FormatTemperature(averageTemp, temperatureDisplayUnit)},stressed={stressedFraction:P0}";
    }
}
