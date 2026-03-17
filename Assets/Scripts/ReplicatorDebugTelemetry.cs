using System.Collections.Generic;
using System.Text;
using UnityEngine;

public sealed class ReplicatorTelemetrySnapshot
{
    public string SimulationTimestamp;
    public bool PhotosynthesisUnlocked;
    public bool SaprotrophyUnlocked;

    public int ChemosynthCount;
    public int HydrogenotrophCount;
    public int PhotosynthCount;
    public int SaprotrophCount;
    public int PredatorCount;
    public int FermenterCount;
    public int MethanogenCount;
    public int MethanotrophCount;

    public float SulfurTempSum;
    public int SulfurTempCount;
    public int SulfurTempStressedCount;
    public float HydrogenTempSum;
    public int HydrogenTempCount;
    public int HydrogenTempStressedCount;
    public float PhotoTempSum;
    public int PhotoTempCount;
    public int PhotoTempStressedCount;
    public float SaproTempSum;
    public int SaproTempCount;
    public int SaproTempStressedCount;

    public float AverageOrganicCStore;
    public int DivisionEligibleCount;
    public int PredationKillsWindow;
    public float AverageToxicProteolyticWaste;
    public float AverageDissolvedOrganicLeak;

    public int[] ChemoDeathCauseCounts;
    public int[] HydrogenDeathCauseCounts;
    public int[] PhotoDeathCauseCounts;
    public int[] SaproDeathCauseCounts;
    public int[] FermentDeathCauseCounts;
    public int[] MethanogenDeathCauseCounts;
    public int[] MethanotrophDeathCauseCounts;
    public int[] PredatorDeathCauseCounts;

    public float MeanH2;
    public float MaxH2;
    public float MeanH2S;
    public float MaxH2S;
    public bool IncludeVentPlumeDiagnostics;
    public float AvgVentH2S;
    public float AvgVentH2;
    public float AvgOceanH2;
    public float AvgOceanH2S;

    public float AtmosphereCO2;
    public float AtmosphereO2;
    public float AtmosphereCH4;
    public float DissolvedFe2OceanMean;
    public float DissolvedFe2Total;
    public float DissolvedFe2RemainingFraction;

    public TemperatureDisplayUnit TemperatureDisplayUnit;
}

public class ReplicatorDebugTelemetry
{
    private float metabolismDebugLogTimer;
    private readonly Dictionary<Replicator, Vector3> sessileDebugPositions = new Dictionary<Replicator, Vector3>(512);
    private readonly Dictionary<Replicator, float> sessileDebugTimers = new Dictionary<Replicator, float>(512);
    private readonly HashSet<Replicator> sessileDebugSeen = new HashSet<Replicator>();
    private readonly List<Replicator> staleSessileAgents = new List<Replicator>(128);

    public bool LogMetabolismDebugThrottled(ReplicatorTelemetrySnapshot snapshot)
    {
        metabolismDebugLogTimer += Time.deltaTime;
        if (metabolismDebugLogTimer < 3f)
        {
            return false;
        }

        metabolismDebugLogTimer = 0f;
        string prefix = $"[SIM {snapshot.SimulationTimestamp}]";

        Debug.Log($"{prefix} Population: {FormatPopulation(snapshot)}");
        Debug.Log($"{prefix} Temperature: {FormatTemperatureSummary(snapshot)}");
        Debug.Log($"{prefix} DeathCauses: {FormatDeathCauses(snapshot)}");
        Debug.Log($"{prefix} Atmosphere: CO2={snapshot.AtmosphereCO2:F3} O2={snapshot.AtmosphereO2:F3} CH4={snapshot.AtmosphereCH4:F3}");
        Debug.Log($"{prefix} Ocean: Fe2+=avg {snapshot.DissolvedFe2OceanMean:F3} total {snapshot.DissolvedFe2Total:F1} remaining {(snapshot.DissolvedFe2RemainingFraction * 100f):F1}%");
        Debug.Log($"{prefix} Resources: {FormatChemistrySummary(snapshot)}");
        return true;
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

    private static string FormatPopulation(ReplicatorTelemetrySnapshot snapshot)
    {
        string unlocked = FormatUnlocked(snapshot.PhotosynthesisUnlocked, snapshot.SaprotrophyUnlocked);
        return $"Hydrogen={snapshot.HydrogenotrophCount} Sulfur={snapshot.ChemosynthCount} Photo={snapshot.PhotosynthCount} Sapro={snapshot.SaprotrophCount} Predator={snapshot.PredatorCount} Ferment={snapshot.FermenterCount} Methanogen={snapshot.MethanogenCount} Methanotroph={snapshot.MethanotrophCount} | unlocked: {unlocked} | divEligible={snapshot.DivisionEligibleCount} | predKills={snapshot.PredationKillsWindow}";
    }

    private static string FormatTemperatureSummary(ReplicatorTelemetrySnapshot snapshot)
    {
        return string.Format(
            "Hydrogen {0} | Sulfur {1} | Photo {2} | Sapro {3}",
            FormatTemperatureDebug(snapshot.HydrogenTempSum, snapshot.HydrogenTempCount, snapshot.HydrogenTempStressedCount, snapshot.TemperatureDisplayUnit),
            FormatTemperatureDebug(snapshot.SulfurTempSum, snapshot.SulfurTempCount, snapshot.SulfurTempStressedCount, snapshot.TemperatureDisplayUnit),
            FormatTemperatureDebug(snapshot.PhotoTempSum, snapshot.PhotoTempCount, snapshot.PhotoTempStressedCount, snapshot.TemperatureDisplayUnit),
            FormatTemperatureDebug(snapshot.SaproTempSum, snapshot.SaproTempCount, snapshot.SaproTempStressedCount, snapshot.TemperatureDisplayUnit));
    }

    private static string FormatDeathCauses(ReplicatorTelemetrySnapshot snapshot)
    {
        return $"Hydrogen[{FormatDeathCauseSummary(snapshot.HydrogenDeathCauseCounts)}] Sulfur[{FormatDeathCauseSummary(snapshot.ChemoDeathCauseCounts)}] Photo[{FormatDeathCauseSummary(snapshot.PhotoDeathCauseCounts)}] Sapro[{FormatDeathCauseSummary(snapshot.SaproDeathCauseCounts)}] Ferment[{FormatDeathCauseSummary(snapshot.FermentDeathCauseCounts)}] Methanogen[{FormatDeathCauseSummary(snapshot.MethanogenDeathCauseCounts)}] Methanotroph[{FormatDeathCauseSummary(snapshot.MethanotrophDeathCauseCounts)}] Predator[{FormatDeathCauseSummary(snapshot.PredatorDeathCauseCounts)}]";
    }

    private static string FormatChemistrySummary(ReplicatorTelemetrySnapshot snapshot)
    {
        string plume = snapshot.IncludeVentPlumeDiagnostics
            ? $" | plume: ventH2S={snapshot.AvgVentH2S:F3} ventH2={snapshot.AvgVentH2:F3} oceanH2={snapshot.AvgOceanH2:F3} oceanH2S={snapshot.AvgOceanH2S:F3}"
            : string.Empty;

        return $"OrgC={snapshot.AverageOrganicCStore:F3} toxWaste={snapshot.AverageToxicProteolyticWaste:F3} dissolvedLeak={snapshot.AverageDissolvedOrganicLeak:F3} | chem: H2 mean={snapshot.MeanH2:F3} max={snapshot.MaxH2:F3}, H2S mean={snapshot.MeanH2S:F3} max={snapshot.MaxH2S:F3}{plume}";
    }

    private static string FormatUnlocked(bool photosynthesisUnlocked, bool saprotrophyUnlocked)
    {
        if (photosynthesisUnlocked && saprotrophyUnlocked)
        {
            return "photo,sapro";
        }

        if (photosynthesisUnlocked)
        {
            return "photo";
        }

        if (saprotrophyUnlocked)
        {
            return "sapro";
        }

        return "none";
    }

    private static string FormatDeathCauseSummary(int[] counts)
    {
        if (counts == null)
        {
            return "n/a";
        }

        int total = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            total += counts[i];
        }

        if (total <= 0)
        {
            return "n/a";
        }

        StringBuilder sb = new StringBuilder(48);
        for (int i = 0; i < counts.Length; i++)
        {
            int count = counts[i];
            if (count <= 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            DeathCause cause = (DeathCause)i;
            float pct = (100f * count) / total;
            sb.Append(DeathCauseShortLabel(cause));
            sb.Append('=');
            sb.Append(pct.ToString("0"));
            sb.Append('%');
        }

        return sb.Length > 0 ? sb.ToString() : "n/a";
    }

    private static string DeathCauseShortLabel(DeathCause cause)
    {
        switch (cause)
        {
            case DeathCause.OldAge: return "Age";
            case DeathCause.EnergyDepletion: return "Energy";
            case DeathCause.TemperatureTooHigh: return "TempHi";
            case DeathCause.TemperatureTooLow: return "TempLo";
            case DeathCause.Lack_CO2: return "CO2";
            case DeathCause.Lack_H2S: return "H2S";
            case DeathCause.Lack_H2: return "H2";
            case DeathCause.Lack_Light: return "Light";
            case DeathCause.Lack_OrganicC_Food: return "OrgC";
            case DeathCause.Lack_O2: return "O2";
            case DeathCause.Lack_CH4: return "CH4";
            case DeathCause.Lack_StoredC: return "StoredC";
            case DeathCause.O2_Toxicity: return "O2Tox";
            case DeathCause.Predation: return "Predation";
            default: return "?";
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
        return $"{ReplicatorManager.FormatTemperature(averageTemp, temperatureDisplayUnit)} ({stressedFraction:P0} stress)";
    }
}
