using System.Collections.Generic;
using UnityEngine;

public class ReplicatorSteeringSystem
{
    public struct Settings
    {
        public float SteerTempWeight;
        public float SteerFoodWeight;
        public bool UseScentPredation;
        public float DissolvedOrganicLeakSteerWeight;
        public float ToxicProteolyticWasteSteerWeight;
        public float ScentScoreSaturation;
        public float SteerGoodCO2;
        public float SteerGoodH2S;
        public float SteerGoodH2;
        public float SteerGoodOrganicC;
        public float SteerGoodO2;
        public float BaseTumbleProbability;
        public float MinTumbleProbability;
        public float MaxTumbleProbability;
        public float TumbleDecreaseOnImproving;
        public float TumbleIncreaseOnWorsening;
        public float FlagellumTurnAngleMax;
        public float AmoeboidTurnAngleMax;
        public float AmoeboidRunNoiseStrength;
        public float FlagellumSenseInterval;
        public float AmoeboidSenseInterval;
        public float FlagellumSenseIntervalJitter;
        public float AmoeboidSenseIntervalJitter;
        public bool EnableRunAndTumbleDebug;
        public float RunAndTumbleDebugWindowSeconds;
    }

    public struct DebugState
    {
        public float RunDurationAccumulator;
        public float RunDurationSampleCount;
        public float TumbleProbabilityAccumulator;
        public int TumbleProbabilitySampleCount;
        public int TumblesThisWindow;
        public float RunAndTumbleDebugTimer;
    }

    public float ComputeLocalHabitatValue(Replicator agent, Vector3 dir, int cellIndex, PlanetResourceMap planetResourceMap, in Settings settings)
    {
        if (agent == null || planetResourceMap == null)
        {
            return 0f;
        }

        Vector3 normalizedDir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.up;
        float temperature = planetResourceMap.GetTemperature(normalizedDir, cellIndex);
        float tempFitness = ComputeTemperatureFitness(agent, temperature);
        float foodFitness = ComputeFoodFitness(agent, normalizedDir, cellIndex, planetResourceMap, settings);

        float score = Mathf.Max(0f, settings.SteerTempWeight) * tempFitness
                    + Mathf.Max(0f, settings.SteerFoodWeight) * foodFitness;

        if (settings.UseScentPredation && planetResourceMap.enableScentFields)
        {
            float dissolvedOrganicLeak = NormalizeScent(planetResourceMap.Get(ResourceType.DissolvedOrganicLeak, cellIndex), settings.ScentScoreSaturation);
            float toxicProteolyticWaste = NormalizeScent(planetResourceMap.Get(ResourceType.ToxicProteolyticWaste, cellIndex), settings.ScentScoreSaturation);
            float scentTerm;
            if (agent.metabolism == MetabolismType.Predation)
            {
                scentTerm = Mathf.Max(0f, settings.DissolvedOrganicLeakSteerWeight) * dissolvedOrganicLeak - 0.25f * toxicProteolyticWaste;
            }
            else
            {
                scentTerm = -Mathf.Max(0f, settings.ToxicProteolyticWasteSteerWeight) * toxicProteolyticWaste + 0.1f * dissolvedOrganicLeak;
            }

            score += scentTerm;
        }

        if (float.IsNaN(score) || float.IsInfinity(score))
        {
            return 0f;
        }

        return Mathf.Clamp(score, 0f, 100f);
    }

    public void UpdateRunAndTumbleLocomotion(
        List<Replicator> agents,
        PlanetGenerator planetGenerator,
        PlanetResourceMap planetResourceMap,
        in Settings settings,
        float deltaTime,
        float now,
        ref DebugState debugState)
    {
        if (agents.Count == 0 || planetGenerator == null || planetResourceMap == null)
        {
            return;
        }

        int resolution = Mathf.Max(1, planetGenerator.resolution);

        if (settings.EnableRunAndTumbleDebug)
        {
            debugState.RunAndTumbleDebugTimer += deltaTime;
        }

        for (int i = 0; i < agents.Count; i++)
        {
            Replicator agent = agents[i];
            bool isAmoeboid = agent.locomotion == LocomotionType.Amoeboid;
            bool isFlagellum = agent.locomotion == LocomotionType.Flagellum;
            if (!isAmoeboid && !isFlagellum)
            {
                continue;
            }

            Vector3 currentDir = agent.currentDirection.sqrMagnitude > 0f ? agent.currentDirection.normalized : agent.position.normalized;
            if (agent.moveDirection.sqrMagnitude <= 0.0001f)
            {
                agent.moveDirection = currentDir;
                agent.desiredMoveDir = currentDir;
            }

            if (now < agent.nextSenseTime)
            {
                if (isAmoeboid && settings.AmoeboidRunNoiseStrength > 0f)
                {
                    ApplyAmoeboidRunNoise(agent, now, settings);
                }
                continue;
            }

            int cellIndex = PlanetGridIndexing.DirectionToCellIndex(currentDir, resolution);
            float habitatValue = ComputeLocalHabitatValue(agent, currentDir, cellIndex, planetResourceMap, settings);
            bool initialized = agent.nextSenseTime > 0f;

            if (!initialized)
            {
                agent.tumbleProbability = Mathf.Clamp(settings.BaseTumbleProbability, settings.MinTumbleProbability, settings.MaxTumbleProbability);
            }
            else if (habitatValue > agent.lastHabitatValue)
            {
                agent.tumbleProbability = Mathf.Clamp(agent.tumbleProbability - Mathf.Max(0f, settings.TumbleDecreaseOnImproving), settings.MinTumbleProbability, settings.MaxTumbleProbability);
            }
            else if (habitatValue < agent.lastHabitatValue)
            {
                agent.tumbleProbability = Mathf.Clamp(agent.tumbleProbability + Mathf.Max(0f, settings.TumbleIncreaseOnWorsening), settings.MinTumbleProbability, settings.MaxTumbleProbability);
            }

            if (UnityEngine.Random.value < agent.tumbleProbability)
            {
                float maxTurnAngle = isFlagellum ? Mathf.Max(0f, settings.FlagellumTurnAngleMax) : Mathf.Max(0f, settings.AmoeboidTurnAngleMax);
                agent.moveDirection = GenerateTumbledDirection(agent.moveDirection, currentDir, maxTurnAngle);
                debugState.TumblesThisWindow++;
            }
            else if (isAmoeboid && settings.AmoeboidRunNoiseStrength > 0f)
            {
                ApplyAmoeboidRunNoise(agent, now, settings);
            }

            agent.desiredMoveDir = agent.moveDirection;
            agent.lastHabitatValue = habitatValue;

            float baseInterval = isFlagellum ? Mathf.Max(0.01f, settings.FlagellumSenseInterval) : Mathf.Max(0.01f, settings.AmoeboidSenseInterval);
            float jitter = isFlagellum ? Mathf.Max(0f, settings.FlagellumSenseIntervalJitter) : Mathf.Max(0f, settings.AmoeboidSenseIntervalJitter);
            float runDuration = Mathf.Max(0.01f, baseInterval + UnityEngine.Random.Range(-jitter, jitter));
            agent.nextSenseTime = now + runDuration;

            debugState.RunDurationAccumulator += runDuration;
            debugState.RunDurationSampleCount += 1f;
            debugState.TumbleProbabilityAccumulator += agent.tumbleProbability;
            debugState.TumbleProbabilitySampleCount++;
        }

        if (settings.EnableRunAndTumbleDebug && debugState.RunAndTumbleDebugTimer >= Mathf.Max(0.2f, settings.RunAndTumbleDebugWindowSeconds))
        {
            float avgTumbleProbability = debugState.TumbleProbabilitySampleCount > 0 ? debugState.TumbleProbabilityAccumulator / debugState.TumbleProbabilitySampleCount : 0f;
            float avgRunDuration = debugState.RunDurationSampleCount > 0f ? debugState.RunDurationAccumulator / debugState.RunDurationSampleCount : 0f;
            Debug.Log($"Run&Tumble debug: avgTumbleProbability={avgTumbleProbability:0.000}, avgRunDuration={avgRunDuration:0.000}s, tumblesInWindow={debugState.TumblesThisWindow}");

            debugState.RunAndTumbleDebugTimer = 0f;
            debugState.RunDurationAccumulator = 0f;
            debugState.RunDurationSampleCount = 0f;
            debugState.TumbleProbabilityAccumulator = 0f;
            debugState.TumbleProbabilitySampleCount = 0;
            debugState.TumblesThisWindow = 0;
        }
    }

    float ComputeTemperatureFitness(Replicator agent, float temperature)
    {
        float optimalTemp = 0.5f * (agent.optimalTempMin + agent.optimalTempMax);
        float tempTolerance = Mathf.Max(0.0001f, 0.5f * (agent.optimalTempMax - agent.optimalTempMin));
        float lethalMargin = Mathf.Max(0.0001f, agent.lethalTempMargin);

        float distFromOptimal = Mathf.Abs(temperature - optimalTemp);
        float safeBand = tempTolerance + lethalMargin;

        if (distFromOptimal <= tempTolerance)
        {
            return 1f;
        }

        float outsideTolerance = distFromOptimal - tempTolerance;
        float fitness = 1f - (outsideTolerance / safeBand);
        return Mathf.Clamp01(fitness);
    }

    float ComputeFoodFitness(Replicator agent, Vector3 normalizedDir, int cellIndex, PlanetResourceMap planetResourceMap, in Settings settings)
    {
        float co2 = NormalizeResource(planetResourceMap, ResourceType.CO2, cellIndex, settings.SteerGoodCO2);

        switch (agent.metabolism)
        {
            case MetabolismType.SulfurChemosynthesis:
            {
                float h2s = NormalizeResource(planetResourceMap, ResourceType.H2S, cellIndex, settings.SteerGoodH2S);
                return Mathf.Min(h2s, co2);
            }
            case MetabolismType.Hydrogenotrophy:
            {
                float h2 = NormalizeResource(planetResourceMap, ResourceType.H2, cellIndex, settings.SteerGoodH2);
                return Mathf.Min(h2, co2);
            }
            case MetabolismType.Photosynthesis:
            {
                float light = Mathf.Clamp01(planetResourceMap.GetInsolation(normalizedDir));
                return Mathf.Min(light, co2);
            }
            case MetabolismType.Saprotrophy:
            {
                float organicC = NormalizeResource(planetResourceMap, ResourceType.OrganicC, cellIndex, settings.SteerGoodOrganicC);
                float o2 = NormalizeResource(planetResourceMap, ResourceType.O2, cellIndex, settings.SteerGoodO2);
                return Mathf.Min(organicC, o2);
            }
            case MetabolismType.Predation:
            {
                float o2 = NormalizeResource(planetResourceMap, ResourceType.O2, cellIndex, settings.SteerGoodO2);
                float dissolvedOrganicLeak = NormalizeScent(planetResourceMap.Get(ResourceType.DissolvedOrganicLeak, cellIndex), settings.ScentScoreSaturation);
                return Mathf.Min(o2, dissolvedOrganicLeak);
            }
            default:
                return 0f;
        }
    }

    float NormalizeResource(PlanetResourceMap planetResourceMap, ResourceType resourceType, int cellIndex, float goodEnoughScale)
    {
        float scale = Mathf.Max(0.0001f, goodEnoughScale);
        float value = planetResourceMap.Get(resourceType, cellIndex);
        float normalized = Mathf.Clamp01(value / scale);
        return float.IsNaN(normalized) || float.IsInfinity(normalized) ? 0f : normalized;
    }

    float NormalizeScent(float scentValue, float scentScoreSaturation)
    {
        float half = Mathf.Max(0.0001f, scentScoreSaturation);
        float normalized = scentValue / (scentValue + half);
        return float.IsNaN(normalized) || float.IsInfinity(normalized) ? 0f : Mathf.Clamp01(normalized);
    }

    void ApplyAmoeboidRunNoise(Replicator agent, float now, in Settings settings)
    {
        float noise = Mathf.Sin((now + agent.movementSeed) * 2.7f) * Mathf.Max(0f, settings.AmoeboidRunNoiseStrength) * Mathf.Max(0f, settings.AmoeboidTurnAngleMax);
        agent.moveDirection = RotateDirectionAroundSurfaceNormal(agent.moveDirection, agent.currentDirection, noise);
        agent.desiredMoveDir = agent.moveDirection;
    }

    Vector3 GenerateTumbledDirection(Vector3 currentMoveDirection, Vector3 surfaceNormal, float maxTurnAngle)
    {
        Vector3 baseDirection = currentMoveDirection.sqrMagnitude > 0.0001f ? currentMoveDirection.normalized : surfaceNormal;
        Vector3 tangent = Vector3.ProjectOnPlane(baseDirection, surfaceNormal);
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            tangent = Vector3.Cross(surfaceNormal, Vector3.up);
            if (tangent.sqrMagnitude <= 0.0001f)
            {
                tangent = Vector3.Cross(surfaceNormal, Vector3.right);
            }
        }

        float turnAngle = UnityEngine.Random.Range(-Mathf.Abs(maxTurnAngle), Mathf.Abs(maxTurnAngle));
        return RotateDirectionAroundSurfaceNormal(tangent.normalized, surfaceNormal, turnAngle);
    }

    Vector3 RotateDirectionAroundSurfaceNormal(Vector3 direction, Vector3 surfaceNormal, float angleDegrees)
    {
        Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;
        Vector3 tangent = Vector3.ProjectOnPlane(direction, normal);
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude <= 0.0001f)
            {
                tangent = Vector3.Cross(normal, Vector3.right);
            }
        }

        Vector3 rotated = Quaternion.AngleAxis(angleDegrees, normal) * tangent.normalized;
        return rotated.sqrMagnitude > 0.0001f ? rotated.normalized : tangent.normalized;
    }
}
