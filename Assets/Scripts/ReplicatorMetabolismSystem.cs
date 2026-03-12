using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public class ReplicatorMetabolismSystem
{
    private static readonly ProfilerMarker SyncFromAgentsMarker = new ProfilerMarker("ReplicatorPopulationState.SyncFromAgents");
    private static readonly ProfilerMarker MetabolismHotLoopMarker = new ProfilerMarker("ReplicatorMetabolismSystem.HotLoop");
    private static readonly ProfilerMarker SyncToAgentsMarker = new ProfilerMarker("ReplicatorPopulationState.SyncToAgents");
    private static readonly ProfilerMarker RemoveDeadAgentsMarker = new ProfilerMarker("ReplicatorMetabolismSystem.RemoveDeadAgents");

    public struct Settings
    {
        public float BasalEnergyCostPerSecond;
        public float EnergyForFullSpeed;
        public float AerobicO2PerC;
        public float AerobicEnergyPerC;
        public float MaxOrganicCStore;
        public float PhotosynthesisCo2PerTickAtFullInsolation;
        public float PhotosynthesisEnergyPerCo2;
        public float PhotosynthStoreFraction;
        public float NightRespirationCPerTick;
        public float SaproCPerTick;
        public float SaproAssimilationFraction;
        public float SaproRespireStoreCPerTick;
        public float HydrogenotrophyCO2PerTick;
        public float HydrogenotrophyH2PerTick;
        public float HydrogenotrophyEnergyPerTick;
        public float HydrogenotrophyStoreFraction;
        public float ChemosynthesisCo2NeedPerTick;
        public float ChemosynthesisH2sNeedPerTick;
        public float ChemosynthesisEnergyPerTick;
        public float ChemosynthStoreFraction;
        public float ChemoRespirationCPerTick;
        public float PredatorBasalCostMultiplier;
        public float PredatorMoveSpeedMultiplier;
        public float MinSpeedFactor;
    }

    public struct DebugSnapshot
    {
        public float ChemoTempSum;
        public float HydrogenTempSum;
        public float PhotoTempSum;
        public float SaproTempSum;
        public int ChemoTempCount;
        public int HydrogenTempCount;
        public int PhotoTempCount;
        public int SaproTempCount;
        public int ChemoStressedCount;
        public int HydrogenStressedCount;
        public int PhotoStressedCount;
        public int SaproStressedCount;
    }

    public void MetabolismTick(
        List<Replicator> agents,
        ReplicatorPopulationState populationState,
        PlanetGenerator planetGenerator,
        PlanetResourceMap planetResourceMap,
        Settings settings,
        float dtTick,
        Func<Replicator, DeathCause> resolveEnergyDeathCause,
        Action<Replicator> depositDeathOrganicC,
        Action<MetabolismType, DeathCause> registerDeathCause,
        out DebugSnapshot debugSnapshot)
    {
        int resolution = Mathf.Max(1, planetGenerator.resolution);
        float basalCost = Mathf.Max(0f, settings.BasalEnergyCostPerSecond) * dtTick;
        float safeEnergyForFullSpeed = Mathf.Max(0.0001f, settings.EnergyForFullSpeed);

        float o2PerC = Mathf.Max(0f, settings.AerobicO2PerC);
        float energyPerC = Mathf.Max(0f, settings.AerobicEnergyPerC);
        float maxStore = Mathf.Max(0f, settings.MaxOrganicCStore);

        var deadIndices = new List<int>(64);
        var deadCauses = new List<DeathCause>(64);

        debugSnapshot = default;

        using (SyncFromAgentsMarker.Auto())
        {
            populationState.SyncFromAgents(agents);
        }

        using (MetabolismHotLoopMarker.Auto())
        for (int i = populationState.Count - 1; i >= 0; i--)
        {
            MetabolismType metabolism = populationState.Metabolism[i];
            LocomotionType locomotion = populationState.Locomotion[i];
            Vector3 dir = populationState.Position[i].normalized;
            int cellIndex = PlanetGridIndexing.DirectionToCellIndex(dir, resolution);

            float temp = planetResourceMap.GetTemperature(dir, cellIndex);

            float min = populationState.OptimalTempMin[i];
            float max = populationState.OptimalTempMax[i];
            float lethalMargin = Mathf.Max(0.0001f, populationState.LethalTempMargin[i]);

            float d = 0f;

            if (temp < min)
                d = min - temp;
            else if (temp > max)
                d = temp - max;

            bool insideOptimalBand = d <= 0f;
            bool lethalTemperature = d > lethalMargin;

            float stress = insideOptimalBand ? 0f : Mathf.Clamp01(d / lethalMargin);
            float performance = insideOptimalBand ? 1f : Mathf.Lerp(0.7f, 0.1f, stress);

            if (metabolism == MetabolismType.Photosynthesis)
            {
                debugSnapshot.PhotoTempSum += temp;
                debugSnapshot.PhotoTempCount++;
                if (!insideOptimalBand) debugSnapshot.PhotoStressedCount++;
            }
            else if (metabolism == MetabolismType.Hydrogenotrophy)
            {
                debugSnapshot.HydrogenTempSum += temp;
                debugSnapshot.HydrogenTempCount++;
                if (!insideOptimalBand) debugSnapshot.HydrogenStressedCount++;
            }
            else if (metabolism == MetabolismType.Saprotrophy || metabolism == MetabolismType.Predation)
            {
                debugSnapshot.SaproTempSum += temp;
                debugSnapshot.SaproTempCount++;
                if (!insideOptimalBand) debugSnapshot.SaproStressedCount++;
            }
            else
            {
                debugSnapshot.ChemoTempSum += temp;
                debugSnapshot.ChemoTempCount++;
                if (!insideOptimalBand) debugSnapshot.ChemoStressedCount++;
            }

            if (lethalTemperature)
            {
                DeathCause temperatureDeathCause = temp > max
                    ? DeathCause.TemperatureTooHigh
                    : DeathCause.TemperatureTooLow;

                deadIndices.Add(i);
                deadCauses.Add(temperatureDeathCause);
                continue;
            }

            if (metabolism == MetabolismType.Photosynthesis)
            {
                float insolation = Mathf.Clamp01(planetResourceMap.GetInsolation(dir));
                bool lackCo2 = false;
                bool lackLight = false;
                bool lackO2 = false;
                bool lackStoredC = false;

                if (insolation > 0f)
                {
                    float co2Need = Mathf.Max(0f, settings.PhotosynthesisCo2PerTickAtFullInsolation) * insolation;
                    float co2Available = planetResourceMap.Get(ResourceType.CO2, cellIndex);
                    float co2Consumed = Mathf.Min(co2Need, co2Available);

                    lackCo2 = co2Need > 0f && co2Consumed <= Mathf.Epsilon;

                    if (co2Consumed > 0f)
                    {
                        planetResourceMap.Add(ResourceType.CO2, cellIndex, -co2Consumed);
                        planetResourceMap.Add(ResourceType.O2, cellIndex, co2Consumed);

                        float producedEnergy = co2Consumed * Mathf.Max(0f, settings.PhotosynthesisEnergyPerCo2) * performance;
                        populationState.Energy[i] += producedEnergy;

                        float storedOrganicC = Mathf.Max(0f, settings.PhotosynthStoreFraction) * co2Consumed;
                        if (storedOrganicC > 0f)
                            populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + storedOrganicC, 0f, maxStore);
                    }
                }
                else
                {
                    float desiredResp = Mathf.Max(0f, settings.NightRespirationCPerTick);
                    float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                    bool hasStore = populationState.OrganicCStore[i] > 0f;
                    lackLight = !hasStore;
                    lackStoredC = !hasStore && desiredResp > 0f;
                    lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;

                    float gained = AerobicRespireFromStore(ref populationState.OrganicCStore[i], ref populationState.Energy[i], cellIndex, desiredResp, o2PerC, energyPerC, planetResourceMap);
                    if (gained > 0f)
                    {
                        populationState.Energy[i] -= gained * (1f - performance);
                        lackLight = false;
                        lackStoredC = false;
                        lackO2 = false;
                    }
                }

                populationState.StarveCo2Seconds[i] = UpdateStarveTimer(populationState.StarveCo2Seconds[i], lackCo2, dtTick);
                populationState.StarveLightSeconds[i] = UpdateStarveTimer(populationState.StarveLightSeconds[i], lackLight, dtTick);
                populationState.StarveO2Seconds[i] = UpdateStarveTimer(populationState.StarveO2Seconds[i], lackO2, dtTick);
                populationState.StarveStoredCSeconds[i] = UpdateStarveTimer(populationState.StarveStoredCSeconds[i], lackStoredC, dtTick);
                populationState.StarveH2sSeconds[i] = 0f;
                populationState.StarveH2Seconds[i] = 0f;
                populationState.StarveOrganicCFoodSeconds[i] = 0f;
            }
            else if (metabolism == MetabolismType.Saprotrophy)
            {
                float envC = planetResourceMap.Get(ResourceType.OrganicC, cellIndex);
                float intakeCap = Mathf.Max(0f, settings.SaproCPerTick);
                float desiredIntake = Mathf.Min(envC, intakeCap);

                float assimilation = Mathf.Clamp01(settings.SaproAssimilationFraction);
                bool lackFood = desiredIntake <= Mathf.Epsilon;
                bool lackO2 = false;
                bool lackStoredC = false;

                if (desiredIntake > 0f)
                {
                    float desiredStore = desiredIntake * assimilation;
                    float desiredRespire = desiredIntake - desiredStore;

                    float storeCapacity = Mathf.Max(0f, maxStore - populationState.OrganicCStore[i]);
                    float actualStore = Mathf.Min(desiredStore, storeCapacity);

                    float actualRespire = 0f;
                    if (desiredRespire > 0f && o2PerC > 0f && energyPerC > 0f)
                    {
                        float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                        float maxRespireByO2 = o2Available / o2PerC;
                        actualRespire = Mathf.Clamp(desiredRespire, 0f, maxRespireByO2);
                        lackO2 = desiredRespire > 0f && actualRespire <= Mathf.Epsilon;
                    }

                    float totalActuallyUsed = actualStore + actualRespire;

                    if (totalActuallyUsed > 0f)
                    {
                        planetResourceMap.Add(ResourceType.OrganicC, cellIndex, -totalActuallyUsed);

                        if (actualStore > 0f)
                            populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + actualStore, 0f, maxStore);

                        if (actualRespire > 0f)
                        {
                            float o2Consumed = actualRespire * o2PerC;
                            planetResourceMap.Add(ResourceType.O2, cellIndex, -o2Consumed);
                            planetResourceMap.Add(ResourceType.CO2, cellIndex, actualRespire);
                            populationState.Energy[i] += actualRespire * energyPerC * performance;
                            lackO2 = false;
                        }
                    }
                }
                else
                {
                    float desiredResp = Mathf.Max(0f, settings.SaproRespireStoreCPerTick);
                    float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                    bool hasStore = populationState.OrganicCStore[i] > 0f;
                    lackStoredC = !hasStore && desiredResp > 0f;
                    lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;

                    float gained = AerobicRespireFromStore(ref populationState.OrganicCStore[i], ref populationState.Energy[i], cellIndex, desiredResp, o2PerC, energyPerC, planetResourceMap);
                    if (gained > 0f)
                    {
                        populationState.Energy[i] -= gained * (1f - performance);
                        lackStoredC = false;
                        lackO2 = false;
                    }
                }

                populationState.StarveOrganicCFoodSeconds[i] = UpdateStarveTimer(populationState.StarveOrganicCFoodSeconds[i], lackFood, dtTick);
                populationState.StarveO2Seconds[i] = UpdateStarveTimer(populationState.StarveO2Seconds[i], lackO2, dtTick);
                populationState.StarveStoredCSeconds[i] = UpdateStarveTimer(populationState.StarveStoredCSeconds[i], lackStoredC, dtTick);
                populationState.StarveCo2Seconds[i] = 0f;
                populationState.StarveH2sSeconds[i] = 0f;
                populationState.StarveH2Seconds[i] = 0f;
                populationState.StarveLightSeconds[i] = 0f;
            }
            else if (metabolism == MetabolismType.Predation)
            {
                bool lackFood = true;
                float desiredResp = Mathf.Max(0f, settings.SaproRespireStoreCPerTick);
                bool hasStore = populationState.OrganicCStore[i] > 0f;
                float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                bool lackStoredC = !hasStore && desiredResp > 0f;
                bool lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;

                float gained = AerobicRespireFromStore(ref populationState.OrganicCStore[i], ref populationState.Energy[i], cellIndex, desiredResp, o2PerC, energyPerC, planetResourceMap);
                if (gained > 0f)
                {
                    populationState.Energy[i] -= gained * (1f - performance);
                    lackStoredC = false;
                    lackO2 = false;
                    lackFood = false;
                }

                populationState.StarveOrganicCFoodSeconds[i] = UpdateStarveTimer(populationState.StarveOrganicCFoodSeconds[i], lackFood, dtTick);
                populationState.StarveO2Seconds[i] = UpdateStarveTimer(populationState.StarveO2Seconds[i], lackO2, dtTick);
                populationState.StarveStoredCSeconds[i] = UpdateStarveTimer(populationState.StarveStoredCSeconds[i], lackStoredC, dtTick);
                populationState.StarveCo2Seconds[i] = 0f;
                populationState.StarveH2sSeconds[i] = 0f;
                populationState.StarveLightSeconds[i] = 0f;
                populationState.StarveH2Seconds[i] = 0f;
            }
            else if (metabolism == MetabolismType.Hydrogenotrophy)
            {
                float co2Need = Mathf.Max(0f, settings.HydrogenotrophyCO2PerTick);
                float h2Need = Mathf.Max(0f, settings.HydrogenotrophyH2PerTick);

                float co2Available = planetResourceMap.Get(ResourceType.CO2, cellIndex);
                float h2Available = planetResourceMap.Get(ResourceType.H2, cellIndex);
                float co2Ratio = co2Need <= Mathf.Epsilon ? 1f : co2Available / co2Need;
                float h2Ratio = h2Need <= Mathf.Epsilon ? 1f : h2Available / h2Need;
                float pulledRatio = Mathf.Clamp01(Mathf.Min(co2Ratio, h2Ratio));

                bool lackCo2 = false;
                bool lackH2 = false;

                if (pulledRatio > 0f)
                {
                    float co2Consumed = co2Need * pulledRatio;
                    float h2Consumed = h2Need * pulledRatio;

                    planetResourceMap.Add(ResourceType.CO2, cellIndex, -co2Consumed);
                    planetResourceMap.Add(ResourceType.H2, cellIndex, -h2Consumed);

                    float producedEnergy = Mathf.Max(0f, settings.HydrogenotrophyEnergyPerTick) * pulledRatio * performance;
                    populationState.Energy[i] += producedEnergy;

                    float storeFrac = Mathf.Clamp01(settings.HydrogenotrophyStoreFraction);
                    float fixedC = co2Consumed * storeFrac;
                    if (fixedC > 0f)
                    {
                        populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + fixedC, 0f, maxStore);
                    }
                }
                else
                {
                    lackCo2 = co2Need > 0f && co2Available <= Mathf.Epsilon;
                    lackH2 = h2Need > 0f && h2Available <= Mathf.Epsilon;
                }

                populationState.StarveCo2Seconds[i] = UpdateStarveTimer(populationState.StarveCo2Seconds[i], lackCo2, dtTick);
                populationState.StarveH2Seconds[i] = UpdateStarveTimer(populationState.StarveH2Seconds[i], lackH2, dtTick);
                populationState.StarveH2sSeconds[i] = 0f;
                populationState.StarveLightSeconds[i] = 0f;
                populationState.StarveOrganicCFoodSeconds[i] = 0f;
                populationState.StarveO2Seconds[i] = 0f;
                populationState.StarveStoredCSeconds[i] = 0f;
            }
            else
            {
                float co2Need = Mathf.Max(0f, settings.ChemosynthesisCo2NeedPerTick);
                float h2sNeed = Mathf.Max(0f, settings.ChemosynthesisH2sNeedPerTick);

                float co2Available = planetResourceMap.Get(ResourceType.CO2, cellIndex);
                float h2sAvailable = planetResourceMap.Get(ResourceType.H2S, cellIndex);
                float co2Ratio = co2Need <= Mathf.Epsilon ? 1f : co2Available / co2Need;
                float h2sRatio = h2sNeed <= Mathf.Epsilon ? 1f : h2sAvailable / h2sNeed;
                float pulledRatio = Mathf.Clamp01(Mathf.Min(co2Ratio, h2sRatio));

                bool lackCo2 = false;
                bool lackH2s = false;
                bool lackO2 = false;
                bool lackStoredC = false;

                if (pulledRatio > 0f)
                {
                    float co2Consumed = co2Need * pulledRatio;
                    float h2sConsumed = h2sNeed * pulledRatio;

                    planetResourceMap.Add(ResourceType.CO2, cellIndex, -co2Consumed);
                    planetResourceMap.Add(ResourceType.H2S, cellIndex, -h2sConsumed);
                    planetResourceMap.Add(ResourceType.S0, cellIndex, h2sConsumed);

                    float producedEnergy = Mathf.Max(0f, settings.ChemosynthesisEnergyPerTick) * pulledRatio * performance;
                    populationState.Energy[i] += producedEnergy;

                    float storeFrac = Mathf.Clamp01(settings.ChemosynthStoreFraction);
                    float fixedC = co2Consumed * storeFrac;
                    if (fixedC > 0f)
                        populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + fixedC, 0f, maxStore);
                }
                else
                {
                    lackCo2 = co2Need > 0f && co2Available <= Mathf.Epsilon;
                    lackH2s = h2sNeed > 0f && h2sAvailable <= Mathf.Epsilon;

                    float desiredResp = Mathf.Max(0f, settings.ChemoRespirationCPerTick);
                    bool hasStore = populationState.OrganicCStore[i] > 0f;
                    float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                    lackStoredC = !hasStore && desiredResp > 0f;
                    lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;
                }

                populationState.StarveCo2Seconds[i] = UpdateStarveTimer(populationState.StarveCo2Seconds[i], lackCo2, dtTick);
                populationState.StarveH2sSeconds[i] = UpdateStarveTimer(populationState.StarveH2sSeconds[i], lackH2s, dtTick);
                populationState.StarveH2Seconds[i] = 0f;
                populationState.StarveO2Seconds[i] = UpdateStarveTimer(populationState.StarveO2Seconds[i], lackO2, dtTick);
                populationState.StarveStoredCSeconds[i] = UpdateStarveTimer(populationState.StarveStoredCSeconds[i], lackStoredC, dtTick);
                populationState.StarveLightSeconds[i] = 0f;
                populationState.StarveOrganicCFoodSeconds[i] = 0f;
            }

            float metabolismBasalCostMultiplier = metabolism == MetabolismType.Predation ? Mathf.Max(0f, settings.PredatorBasalCostMultiplier) : 1f;
            float stressedBasal = basalCost * metabolismBasalCostMultiplier * (1f + stress);
            float speedMultiplier = metabolism == MetabolismType.Predation ? Mathf.Max(0f, settings.PredatorMoveSpeedMultiplier) : 1f;
            populationState.SpeedFactor[i] = Mathf.Clamp((populationState.Energy[i] / safeEnergyForFullSpeed) * performance * speedMultiplier, settings.MinSpeedFactor, 1f);
            float movementCost = 0f;
            switch (locomotion)
            {
                case LocomotionType.PassiveDrift:
                case LocomotionType.Anchored:
                    movementCost = 0f;
                    break;
                case LocomotionType.Amoeboid:
                case LocomotionType.Flagellum:
                    movementCost = 0f;
                    break;
                default:
                    movementCost = 0f;
                    break;
            }
            populationState.Energy[i] -= (stressedBasal + movementCost);

            if (populationState.Energy[i] <= 0f)
            {
                deadIndices.Add(i);
                deadCauses.Add(DeathCause.EnergyDepletion);
            }
        }

        using (SyncToAgentsMarker.Auto())
        {
            populationState.SyncToAgents(agents);
        }

        using (RemoveDeadAgentsMarker.Auto())
        {
            for (int dead = 0; dead < deadIndices.Count; dead++)
            {
                int index = deadIndices[dead];
                if (index < 0 || index >= agents.Count)
                {
                    continue;
                }

                Replicator agent = agents[index];
                DeathCause cause = deadCauses[dead];
                if (cause == DeathCause.EnergyDepletion)
                {
                    cause = resolveEnergyDeathCause(agent);
                }

                registerDeathCause(agent.metabolism, cause);
                depositDeathOrganicC(agent);
                agents.RemoveAt(index);
            }
        }
    }

    private static float UpdateStarveTimer(float current, bool deprived, float dt)
    {
        return deprived ? (current + dt) : 0f;
    }

    private static float AerobicRespireFromStore(
        ref float organicCStore,
        ref float energy,
        int cellIndex,
        float cMaxThisTick,
        float o2PerC,
        float energyPerC,
        PlanetResourceMap planetResourceMap)
    {
        if (organicCStore <= 0f || cMaxThisTick <= 0f || o2PerC <= 0f || energyPerC <= 0f)
            return 0f;

        float cUsed = Mathf.Min(cMaxThisTick, organicCStore);
        if (cUsed <= 0f) return 0f;

        float o2Needed = cUsed * o2PerC;
        float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
        float ratio = o2Needed <= Mathf.Epsilon ? 1f : Mathf.Clamp01(o2Available / o2Needed);
        cUsed *= ratio;

        if (cUsed <= 0f) return 0f;

        float o2Consumed = cUsed * o2PerC;
        planetResourceMap.Add(ResourceType.O2, cellIndex, -o2Consumed);
        planetResourceMap.Add(ResourceType.CO2, cellIndex, cUsed);

        organicCStore = Mathf.Max(0f, organicCStore - cUsed);
        float gainedEnergy = cUsed * energyPerC;
        energy += gainedEnergy;

        return gainedEnergy;
    }
}
