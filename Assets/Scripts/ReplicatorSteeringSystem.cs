using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public class ReplicatorSteeringSystem
{
    private static readonly ProfilerMarker SteeringHotLoopMarker = new ProfilerMarker("ReplicatorSteeringSystem.HotLoop");
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

    public float ComputeLocalHabitatValue(ReplicatorPopulationState populationState, int index, Vector3 dir, int cellIndex, PlanetResourceMap planetResourceMap, in Settings settings)
    {
        if (populationState == null || planetResourceMap == null)
        {
            return 0f;
        }

        Vector3 normalizedDir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.up;
        int layerIndex = ResolveCurrentLayer(populationState, index, cellIndex, planetResourceMap);
        float temperature = planetResourceMap.GetTemperature(normalizedDir, cellIndex);
        float tempFitness = ComputeTemperatureFitness(populationState, index, temperature);
        float foodFitness = ComputeFoodFitness(populationState, index, normalizedDir, cellIndex, layerIndex, planetResourceMap, settings);

        float score = Mathf.Max(0f, settings.SteerTempWeight) * tempFitness
                    + Mathf.Max(0f, settings.SteerFoodWeight) * foodFitness;

        if (settings.UseScentPredation && planetResourceMap.enableScentFields)
        {
            float dissolvedOrganicLeak = NormalizeScent(planetResourceMap.Get(ResourceType.DissolvedOrganicLeak, cellIndex), settings.ScentScoreSaturation);
            float toxicProteolyticWaste = NormalizeScent(planetResourceMap.Get(ResourceType.ToxicProteolyticWaste, cellIndex), settings.ScentScoreSaturation);
            float scentTerm;
            if (populationState.Metabolism[index] == MetabolismType.Predation)
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

    public float ComputeLocalHabitatValue(Replicator agent, Vector3 dir, int cellIndex, PlanetResourceMap planetResourceMap, in Settings settings)
    {
        if (agent == null || planetResourceMap == null)
        {
            return 0f;
        }

        Vector3 normalizedDir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.up;
        float temperature = planetResourceMap.GetTemperature(normalizedDir, cellIndex);

        float optimalTemp = 0.5f * (agent.optimalTempMin + agent.optimalTempMax);
        float tempTolerance = Mathf.Max(0.0001f, 0.5f * (agent.optimalTempMax - agent.optimalTempMin));
        float lethalMargin = Mathf.Max(0.0001f, agent.lethalTempMargin);
        float distFromOptimal = Mathf.Abs(temperature - optimalTemp);
        float safeBand = tempTolerance + lethalMargin;
        float tempFitness = distFromOptimal <= tempTolerance ? 1f : Mathf.Clamp01(1f - ((distFromOptimal - tempTolerance) / safeBand));

        float co2 = NormalizeResource(planetResourceMap, ResourceType.CO2, cellIndex, -1, settings.SteerGoodCO2);
        float foodFitness;
        switch (agent.metabolism)
        {
            case MetabolismType.SulfurChemosynthesis:
                foodFitness = Mathf.Min(NormalizeResource(planetResourceMap, ResourceType.H2S, cellIndex, agent.currentOceanLayerIndex, settings.SteerGoodH2S), co2);
                break;
            case MetabolismType.Hydrogenotrophy:
                foodFitness = Mathf.Min(NormalizeResource(planetResourceMap, ResourceType.H2, cellIndex, agent.currentOceanLayerIndex, settings.SteerGoodH2), co2);
                break;
            case MetabolismType.Photosynthesis:
                foodFitness = Mathf.Min(Mathf.Clamp01(planetResourceMap.GetInsolation(normalizedDir)), co2);
                break;
            case MetabolismType.Saprotrophy:
                foodFitness = Mathf.Min(
                    NormalizeResource(planetResourceMap, ResourceType.OrganicC, cellIndex, agent.currentOceanLayerIndex, settings.SteerGoodOrganicC),
                    NormalizeResource(planetResourceMap, ResourceType.O2, cellIndex, agent.currentOceanLayerIndex, settings.SteerGoodO2));
                break;
            case MetabolismType.Predation:
                foodFitness = Mathf.Min(
                    NormalizeResource(planetResourceMap, ResourceType.O2, cellIndex, agent.currentOceanLayerIndex, settings.SteerGoodO2),
                    NormalizeScent(planetResourceMap.Get(ResourceType.DissolvedOrganicLeak, cellIndex), settings.ScentScoreSaturation));
                break;
            case MetabolismType.Fermentation:
                foodFitness = NormalizeResource(planetResourceMap, ResourceType.OrganicC, cellIndex, agent.currentOceanLayerIndex, settings.SteerGoodOrganicC);
                break;
            case MetabolismType.Methanogenesis:
                foodFitness = Mathf.Min(NormalizeResource(planetResourceMap, ResourceType.H2, cellIndex, agent.currentOceanLayerIndex, settings.SteerGoodH2), co2);
                break;
            case MetabolismType.Methanotrophy:
                foodFitness = Mathf.Min(NormalizeResource(planetResourceMap, ResourceType.CH4, cellIndex, agent.currentOceanLayerIndex, settings.SteerGoodH2), NormalizeResource(planetResourceMap, ResourceType.O2, cellIndex, agent.currentOceanLayerIndex, settings.SteerGoodO2));
                break;
            default:
                foodFitness = 0f;
                break;
        }

        float score = Mathf.Max(0f, settings.SteerTempWeight) * tempFitness
                    + Mathf.Max(0f, settings.SteerFoodWeight) * foodFitness;

        if (settings.UseScentPredation && planetResourceMap.enableScentFields)
        {
            float dissolvedOrganicLeak = NormalizeScent(planetResourceMap.Get(ResourceType.DissolvedOrganicLeak, cellIndex), settings.ScentScoreSaturation);
            float toxicProteolyticWaste = NormalizeScent(planetResourceMap.Get(ResourceType.ToxicProteolyticWaste, cellIndex), settings.ScentScoreSaturation);
            score += agent.metabolism == MetabolismType.Predation
                ? Mathf.Max(0f, settings.DissolvedOrganicLeakSteerWeight) * dissolvedOrganicLeak - 0.25f * toxicProteolyticWaste
                : -Mathf.Max(0f, settings.ToxicProteolyticWasteSteerWeight) * toxicProteolyticWaste + 0.1f * dissolvedOrganicLeak;
        }

        if (float.IsNaN(score) || float.IsInfinity(score))
        {
            return 0f;
        }

        return Mathf.Clamp(score, 0f, 100f);
    }

    public void UpdateRunAndTumbleLocomotion(
        List<Replicator> agents,
        ReplicatorPopulationState populationState,
        PlanetGenerator planetGenerator,
        PlanetResourceMap planetResourceMap,
        in Settings settings,
        float deltaTime,
        float now,
        bool populationStatePrimed,
        ref DebugState debugState)
    {
        if (agents.Count == 0 || populationState == null || planetGenerator == null || planetResourceMap == null)
        {
            return;
        }

        int resolution = Mathf.Max(1, planetGenerator.resolution);

        if (settings.EnableRunAndTumbleDebug)
        {
            debugState.RunAndTumbleDebugTimer += deltaTime;
        }

        populationState.EnsureMatchesAgentCount(agents);

        using (SteeringHotLoopMarker.Auto())
        {
            int count = populationState.Count;
            for (int i = 0; i < count; i++)
            {
                LocomotionType locomotion = populationState.Locomotion[i];
                bool isAmoeboid = locomotion == LocomotionType.Amoeboid;
                bool isFlagellum = locomotion == LocomotionType.Flagellum;
                if (!isAmoeboid && !isFlagellum)
                {
                    continue;
                }

                Vector3 currentDir = populationState.CurrentDirection[i].sqrMagnitude > 0f ? populationState.CurrentDirection[i].normalized : populationState.Position[i].normalized;
                if (populationState.MoveDirection[i].sqrMagnitude <= 0.0001f)
                {
                    populationState.MoveDirection[i] = currentDir;
                    populationState.DesiredMoveDirection[i] = currentDir;
                }

                if (now < populationState.NextSenseTime[i])
                {
                    if (isAmoeboid && settings.AmoeboidRunNoiseStrength > 0f)
                    {
                        ApplyAmoeboidRunNoise(populationState, i, now, settings);
                    }
                    continue;
                }

                int cellIndex = PlanetGridIndexing.DirectionToCellIndex(currentDir, resolution);
                float habitatValue = ComputeLocalHabitatValue(populationState, i, currentDir, cellIndex, planetResourceMap, settings);
                bool initialized = populationState.NextSenseTime[i] > 0f;

                if (!initialized)
                {
                    populationState.TumbleProbability[i] = Mathf.Clamp(settings.BaseTumbleProbability, settings.MinTumbleProbability, settings.MaxTumbleProbability);
                }
                else if (habitatValue > populationState.LastHabitatValue[i])
                {
                    populationState.TumbleProbability[i] = Mathf.Clamp(populationState.TumbleProbability[i] - Mathf.Max(0f, settings.TumbleDecreaseOnImproving), settings.MinTumbleProbability, settings.MaxTumbleProbability);
                }
                else if (habitatValue < populationState.LastHabitatValue[i])
                {
                    populationState.TumbleProbability[i] = Mathf.Clamp(populationState.TumbleProbability[i] + Mathf.Max(0f, settings.TumbleIncreaseOnWorsening), settings.MinTumbleProbability, settings.MaxTumbleProbability);
                }

                if (UnityEngine.Random.value < populationState.TumbleProbability[i])
                {
                    float maxTurnAngle = isFlagellum ? Mathf.Max(0f, settings.FlagellumTurnAngleMax) : Mathf.Max(0f, settings.AmoeboidTurnAngleMax);
                    populationState.MoveDirection[i] = GenerateTumbledDirection(populationState.MoveDirection[i], currentDir, maxTurnAngle);
                    debugState.TumblesThisWindow++;
                }
                else if (isAmoeboid && settings.AmoeboidRunNoiseStrength > 0f)
                {
                    ApplyAmoeboidRunNoise(populationState, i, now, settings);
                }

                populationState.DesiredMoveDirection[i] = populationState.MoveDirection[i];
                populationState.LastHabitatValue[i] = habitatValue;

                float baseInterval = isFlagellum ? Mathf.Max(0.01f, settings.FlagellumSenseInterval) : Mathf.Max(0.01f, settings.AmoeboidSenseInterval);
                float jitter = isFlagellum ? Mathf.Max(0f, settings.FlagellumSenseIntervalJitter) : Mathf.Max(0f, settings.AmoeboidSenseIntervalJitter);
                float runDuration = Mathf.Max(0.01f, baseInterval + UnityEngine.Random.Range(-jitter, jitter));
                populationState.NextSenseTime[i] = now + runDuration;

                debugState.RunDurationAccumulator += runDuration;
                debugState.RunDurationSampleCount += 1f;
                debugState.TumbleProbabilityAccumulator += populationState.TumbleProbability[i];
                debugState.TumbleProbabilitySampleCount++;
            }
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

    float ComputeTemperatureFitness(ReplicatorPopulationState populationState, int index, float temperature)
    {
        float optimalTemp = 0.5f * (populationState.OptimalTempMin[index] + populationState.OptimalTempMax[index]);
        float tempTolerance = Mathf.Max(0.0001f, 0.5f * (populationState.OptimalTempMax[index] - populationState.OptimalTempMin[index]));
        float lethalMargin = Mathf.Max(0.0001f, populationState.LethalTempMargin[index]);

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

    float ComputeFoodFitness(ReplicatorPopulationState populationState, int index, Vector3 normalizedDir, int cellIndex, int layerIndex, PlanetResourceMap planetResourceMap, in Settings settings)
    {
        float co2 = NormalizeResource(planetResourceMap, ResourceType.CO2, cellIndex, -1, settings.SteerGoodCO2);

        switch (populationState.Metabolism[index])
        {
            case MetabolismType.SulfurChemosynthesis:
            {
                float h2s = NormalizeResource(planetResourceMap, ResourceType.H2S, cellIndex, layerIndex, settings.SteerGoodH2S);
                return Mathf.Min(h2s, co2);
            }
            case MetabolismType.Hydrogenotrophy:
            {
                float h2 = NormalizeResource(planetResourceMap, ResourceType.H2, cellIndex, layerIndex, settings.SteerGoodH2);
                return Mathf.Min(h2, co2);
            }
            case MetabolismType.Photosynthesis:
            {
                float light = Mathf.Clamp01(planetResourceMap.GetInsolation(normalizedDir));
                return Mathf.Min(light, co2);
            }
            case MetabolismType.Saprotrophy:
            {
                float organicC = NormalizeResource(planetResourceMap, ResourceType.OrganicC, cellIndex, layerIndex, settings.SteerGoodOrganicC);
                float o2 = NormalizeResource(planetResourceMap, ResourceType.O2, cellIndex, layerIndex, settings.SteerGoodO2);
                return Mathf.Min(organicC, o2);
            }
            case MetabolismType.Predation:
            {
                float o2 = NormalizeResource(planetResourceMap, ResourceType.O2, cellIndex, layerIndex, settings.SteerGoodO2);
                float dissolvedOrganicLeak = NormalizeScent(planetResourceMap.Get(ResourceType.DissolvedOrganicLeak, cellIndex), settings.ScentScoreSaturation);
                return Mathf.Min(o2, dissolvedOrganicLeak);
            }
            case MetabolismType.Fermentation:
            {
                return NormalizeResource(planetResourceMap, ResourceType.OrganicC, cellIndex, layerIndex, settings.SteerGoodOrganicC);
            }
            case MetabolismType.Methanogenesis:
            {
                float h2 = NormalizeResource(planetResourceMap, ResourceType.H2, cellIndex, layerIndex, settings.SteerGoodH2);
                return Mathf.Min(h2, co2);
            }
            case MetabolismType.Methanotrophy:
            {
                float ch4 = NormalizeResource(planetResourceMap, ResourceType.CH4, cellIndex, layerIndex, settings.SteerGoodH2);
                float o2 = NormalizeResource(planetResourceMap, ResourceType.O2, cellIndex, layerIndex, settings.SteerGoodO2);
                return Mathf.Min(ch4, o2);
            }
            default:
                return 0f;
        }
    }

    float NormalizeResource(PlanetResourceMap planetResourceMap, ResourceType resourceType, int cellIndex, int layerIndex, float goodEnoughScale)
    {
        float scale = Mathf.Max(0.0001f, goodEnoughScale);
        float value = layerIndex >= 0
            ? planetResourceMap.GetResourceForCellLayer(resourceType, cellIndex, layerIndex)
            : planetResourceMap.GetCompatibilityResourceValue(resourceType, cellIndex);
        float normalized = Mathf.Clamp01(value / scale);
        return float.IsNaN(normalized) || float.IsInfinity(normalized) ? 0f : normalized;
    }

    int ResolveCurrentLayer(ReplicatorPopulationState populationState, int index, int cellIndex, PlanetResourceMap planetResourceMap)
    {
        if (!planetResourceMap.IsOceanCell(cellIndex))
        {
            populationState.CurrentOceanLayerIndex[index] = -1;
            populationState.PreferredOceanLayerIndex[index] = -1;
            return -1;
        }

        int preferred = planetResourceMap.ClampOceanLayerIndex(cellIndex, populationState.PreferredOceanLayerIndex[index]);
        int current = planetResourceMap.ClampOceanLayerIndex(cellIndex, populationState.CurrentOceanLayerIndex[index]);
        if (current < 0)
        {
            current = preferred;
        }

        if (current < preferred) current++;
        else if (current > preferred) current--;

        populationState.PreferredOceanLayerIndex[index] = preferred;
        populationState.CurrentOceanLayerIndex[index] = current;
        return current;
    }

    float NormalizeScent(float scentValue, float scentScoreSaturation)
    {
        float half = Mathf.Max(0.0001f, scentScoreSaturation);
        float normalized = scentValue / (scentValue + half);
        return float.IsNaN(normalized) || float.IsInfinity(normalized) ? 0f : Mathf.Clamp01(normalized);
    }

    void ApplyAmoeboidRunNoise(ReplicatorPopulationState populationState, int index, float now, in Settings settings)
    {
        float noise = Mathf.Sin((now + populationState.MovementSeed[index]) * 2.7f) * Mathf.Max(0f, settings.AmoeboidRunNoiseStrength) * Mathf.Max(0f, settings.AmoeboidTurnAngleMax);
        populationState.MoveDirection[index] = RotateDirectionAroundSurfaceNormal(populationState.MoveDirection[index], populationState.CurrentDirection[index], noise);
        populationState.DesiredMoveDirection[index] = populationState.MoveDirection[index];
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
