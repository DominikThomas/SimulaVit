using System;
using System.Collections.Generic;
using UnityEngine;

public class ReplicatorMetabolismSystem
{
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

        debugSnapshot = default;

        for (int i = agents.Count - 1; i >= 0; i--)
        {
            Replicator agent = agents[i];
            Vector3 dir = agent.position.normalized;
            int cellIndex = PlanetGridIndexing.DirectionToCellIndex(dir, resolution);

            float temp = planetResourceMap.GetTemperature(dir, cellIndex);

            float min = agent.optimalTempMin;
            float max = agent.optimalTempMax;
            float lethalMargin = Mathf.Max(0.0001f, agent.lethalTempMargin);

            float d = 0f;

            if (temp < min)
                d = min - temp;
            else if (temp > max)
                d = temp - max;

            bool insideOptimalBand = d <= 0f;
            bool lethalTemperature = d > lethalMargin;

            float stress = insideOptimalBand ? 0f : Mathf.Clamp01(d / lethalMargin);
            float performance = insideOptimalBand ? 1f : Mathf.Lerp(0.7f, 0.1f, stress);

            if (agent.metabolism == MetabolismType.Photosynthesis)
            {
                debugSnapshot.PhotoTempSum += temp;
                debugSnapshot.PhotoTempCount++;
                if (!insideOptimalBand) debugSnapshot.PhotoStressedCount++;
            }
            else if (agent.metabolism == MetabolismType.Hydrogenotrophy)
            {
                debugSnapshot.HydrogenTempSum += temp;
                debugSnapshot.HydrogenTempCount++;
                if (!insideOptimalBand) debugSnapshot.HydrogenStressedCount++;
            }
            else if (agent.metabolism == MetabolismType.Saprotrophy || agent.metabolism == MetabolismType.Predation)
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

                registerDeathCause(agent.metabolism, temperatureDeathCause);
                depositDeathOrganicC(agent);
                agents.RemoveAt(i);
                continue;
            }

            if (agent.metabolism == MetabolismType.Photosynthesis)
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
                        agent.energy += producedEnergy;

                        float storedOrganicC = Mathf.Max(0f, settings.PhotosynthStoreFraction) * co2Consumed;
                        if (storedOrganicC > 0f)
                            agent.organicCStore = Mathf.Clamp(agent.organicCStore + storedOrganicC, 0f, maxStore);
                    }
                }
                else
                {
                    float desiredResp = Mathf.Max(0f, settings.NightRespirationCPerTick);
                    float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                    bool hasStore = agent.organicCStore > 0f;
                    lackLight = !hasStore;
                    lackStoredC = !hasStore && desiredResp > 0f;
                    lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;

                    float gained = AerobicRespireFromStore(agent, cellIndex, desiredResp, o2PerC, energyPerC, planetResourceMap);
                    if (gained > 0f)
                    {
                        agent.energy -= gained * (1f - performance);
                        lackLight = false;
                        lackStoredC = false;
                        lackO2 = false;
                    }
                }

                agent.starveCo2Seconds = UpdateStarveTimer(agent.starveCo2Seconds, lackCo2, dtTick);
                agent.starveLightSeconds = UpdateStarveTimer(agent.starveLightSeconds, lackLight, dtTick);
                agent.starveO2Seconds = UpdateStarveTimer(agent.starveO2Seconds, lackO2, dtTick);
                agent.starveStoredCSeconds = UpdateStarveTimer(agent.starveStoredCSeconds, lackStoredC, dtTick);
                agent.starveH2sSeconds = 0f;
                agent.starveH2Seconds = 0f;
                agent.starveOrganicCFoodSeconds = 0f;
            }
            else if (agent.metabolism == MetabolismType.Saprotrophy)
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

                    float storeCapacity = Mathf.Max(0f, maxStore - agent.organicCStore);
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
                            agent.organicCStore = Mathf.Clamp(agent.organicCStore + actualStore, 0f, maxStore);

                        if (actualRespire > 0f)
                        {
                            float o2Consumed = actualRespire * o2PerC;
                            planetResourceMap.Add(ResourceType.O2, cellIndex, -o2Consumed);
                            planetResourceMap.Add(ResourceType.CO2, cellIndex, actualRespire);
                            agent.energy += actualRespire * energyPerC * performance;
                            lackO2 = false;
                        }
                    }
                }
                else
                {
                    float desiredResp = Mathf.Max(0f, settings.SaproRespireStoreCPerTick);
                    float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                    bool hasStore = agent.organicCStore > 0f;
                    lackStoredC = !hasStore && desiredResp > 0f;
                    lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;

                    float gained = AerobicRespireFromStore(agent, cellIndex, desiredResp, o2PerC, energyPerC, planetResourceMap);
                    if (gained > 0f)
                    {
                        agent.energy -= gained * (1f - performance);
                        lackStoredC = false;
                        lackO2 = false;
                    }
                }

                agent.starveOrganicCFoodSeconds = UpdateStarveTimer(agent.starveOrganicCFoodSeconds, lackFood, dtTick);
                agent.starveO2Seconds = UpdateStarveTimer(agent.starveO2Seconds, lackO2, dtTick);
                agent.starveStoredCSeconds = UpdateStarveTimer(agent.starveStoredCSeconds, lackStoredC, dtTick);
                agent.starveCo2Seconds = 0f;
                agent.starveH2sSeconds = 0f;
                agent.starveH2Seconds = 0f;
                agent.starveLightSeconds = 0f;
            }
            else if (agent.metabolism == MetabolismType.Predation)
            {
                bool lackFood = true;
                float desiredResp = Mathf.Max(0f, settings.SaproRespireStoreCPerTick);
                bool hasStore = agent.organicCStore > 0f;
                float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                bool lackStoredC = !hasStore && desiredResp > 0f;
                bool lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;

                float gained = AerobicRespireFromStore(agent, cellIndex, desiredResp, o2PerC, energyPerC, planetResourceMap);
                if (gained > 0f)
                {
                    agent.energy -= gained * (1f - performance);
                    lackStoredC = false;
                    lackO2 = false;
                    lackFood = false;
                }

                agent.starveOrganicCFoodSeconds = UpdateStarveTimer(agent.starveOrganicCFoodSeconds, lackFood, dtTick);
                agent.starveO2Seconds = UpdateStarveTimer(agent.starveO2Seconds, lackO2, dtTick);
                agent.starveStoredCSeconds = UpdateStarveTimer(agent.starveStoredCSeconds, lackStoredC, dtTick);
                agent.starveCo2Seconds = 0f;
                agent.starveH2sSeconds = 0f;
                agent.starveLightSeconds = 0f;
                agent.starveH2Seconds = 0f;
            }
            else if (agent.metabolism == MetabolismType.Hydrogenotrophy)
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
                    agent.energy += producedEnergy;

                    float storeFrac = Mathf.Clamp01(settings.HydrogenotrophyStoreFraction);
                    float fixedC = co2Consumed * storeFrac;
                    if (fixedC > 0f)
                    {
                        agent.organicCStore = Mathf.Clamp(agent.organicCStore + fixedC, 0f, maxStore);
                    }
                }
                else
                {
                    lackCo2 = co2Need > 0f && co2Available <= Mathf.Epsilon;
                    lackH2 = h2Need > 0f && h2Available <= Mathf.Epsilon;
                }

                agent.starveCo2Seconds = UpdateStarveTimer(agent.starveCo2Seconds, lackCo2, dtTick);
                agent.starveH2Seconds = UpdateStarveTimer(agent.starveH2Seconds, lackH2, dtTick);
                agent.starveH2sSeconds = 0f;
                agent.starveLightSeconds = 0f;
                agent.starveOrganicCFoodSeconds = 0f;
                agent.starveO2Seconds = 0f;
                agent.starveStoredCSeconds = 0f;
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
                    agent.energy += producedEnergy;

                    float storeFrac = Mathf.Clamp01(settings.ChemosynthStoreFraction);
                    float fixedC = co2Consumed * storeFrac;
                    if (fixedC > 0f)
                        agent.organicCStore = Mathf.Clamp(agent.organicCStore + fixedC, 0f, maxStore);
                }
                else
                {
                    lackCo2 = co2Need > 0f && co2Available <= Mathf.Epsilon;
                    lackH2s = h2sNeed > 0f && h2sAvailable <= Mathf.Epsilon;

                    float desiredResp = Mathf.Max(0f, settings.ChemoRespirationCPerTick);
                    bool hasStore = agent.organicCStore > 0f;
                    float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
                    lackStoredC = !hasStore && desiredResp > 0f;
                    lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;
                }

                agent.starveCo2Seconds = UpdateStarveTimer(agent.starveCo2Seconds, lackCo2, dtTick);
                agent.starveH2sSeconds = UpdateStarveTimer(agent.starveH2sSeconds, lackH2s, dtTick);
                agent.starveH2Seconds = 0f;
                agent.starveO2Seconds = UpdateStarveTimer(agent.starveO2Seconds, lackO2, dtTick);
                agent.starveStoredCSeconds = UpdateStarveTimer(agent.starveStoredCSeconds, lackStoredC, dtTick);
                agent.starveLightSeconds = 0f;
                agent.starveOrganicCFoodSeconds = 0f;
            }

            float metabolismBasalCostMultiplier = agent.metabolism == MetabolismType.Predation ? Mathf.Max(0f, settings.PredatorBasalCostMultiplier) : 1f;
            float stressedBasal = basalCost * metabolismBasalCostMultiplier * (1f + stress);
            float speedMultiplier = agent.metabolism == MetabolismType.Predation ? Mathf.Max(0f, settings.PredatorMoveSpeedMultiplier) : 1f;
            agent.speedFactor = Mathf.Clamp((agent.energy / safeEnergyForFullSpeed) * performance * speedMultiplier, settings.MinSpeedFactor, 1f);
            float movementCost = 0f;
            switch (agent.locomotion)
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
            agent.energy -= (stressedBasal + movementCost);

            if (agent.energy <= 0f)
            {
                registerDeathCause(agent.metabolism, resolveEnergyDeathCause(agent));
                depositDeathOrganicC(agent);
                agents.RemoveAt(i);
            }
        }
    }

    private static float UpdateStarveTimer(float current, bool deprived, float dt)
    {
        return deprived ? (current + dt) : 0f;
    }

    private static float AerobicRespireFromStore(
        Replicator agent,
        int cellIndex,
        float cMaxThisTick,
        float o2PerC,
        float energyPerC,
        PlanetResourceMap planetResourceMap)
    {
        if (agent.organicCStore <= 0f || cMaxThisTick <= 0f || o2PerC <= 0f || energyPerC <= 0f)
            return 0f;

        float cUsed = Mathf.Min(cMaxThisTick, agent.organicCStore);
        if (cUsed <= 0f) return 0f;

        float o2Needed = cUsed * o2PerC;
        float o2Available = planetResourceMap.Get(ResourceType.O2, cellIndex);
        float ratio = o2Needed <= Mathf.Epsilon ? 1f : Mathf.Clamp01(o2Available / o2Needed);
        cUsed *= ratio;

        if (cUsed <= 0f) return 0f;

        float o2Consumed = cUsed * o2PerC;
        planetResourceMap.Add(ResourceType.O2, cellIndex, -o2Consumed);
        planetResourceMap.Add(ResourceType.CO2, cellIndex, cUsed);

        agent.organicCStore = Mathf.Max(0f, agent.organicCStore - cUsed);
        float gainedEnergy = cUsed * energyPerC;
        agent.energy += gainedEnergy;

        return gainedEnergy;
    }
}
