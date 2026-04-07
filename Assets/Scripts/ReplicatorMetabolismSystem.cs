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
        public bool PhotosynthDarkAnoxicEnabled;
        public float PhotosynthDarkAnoxicOrganicCUseRate;
        public float PhotosynthDarkAnoxicEnergyYieldMultiplier;
        public float PhotosynthDarkAnoxicMaxFractionOfBaseMaintenanceCovered;
        public float PhotosynthDarkAnoxicStressMultiplier;
        public bool PhotosynthDarkAnoxicCanReplicate;
        public float PhotosynthDarkAnoxicCO2ReleaseFraction;
        public float PhotosynthDarkAnoxicH2ReleaseFraction;
        public float PhotosynthDarkAnoxicOrganicLeakFraction;
        public float SaproCPerTick;
        public float SaproAssimilationFraction;
        public float SaproRespireStoreCPerTick;
        public float HydrogenotrophyCO2PerTick;
        public float HydrogenotrophyH2PerTick;
        public float HydrogenotrophyEnergyPerTick;
        public float HydrogenotrophyStoreFraction;
        public float FermentationOrganicCPerTick;
        public float FermentationEnergyPerTick;
        public float FermentationAssimilationFraction;
        public float MethanogenesisCO2PerTick;
        public float MethanogenesisH2PerTick;
        public float MethanogenesisEnergyPerTick;
        public float MethanogenesisAssimilationFraction;
        public float MethanotrophyCH4PerTick;
        public float MethanotrophyO2PerTick;
        public float MethanotrophyEnergyPerTick;
        public float MethanotrophyAssimilationFraction;
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
        public int PhotosynthLightModeCount;
        public int PhotosynthDarkAerobicModeCount;
        public int PhotosynthDarkAnoxicFallbackModeCount;
        public float PhotosynthDarkAnoxicOrganicCConsumed;
        public float PhotosynthDarkAnoxicEnergyGenerated;
        public float PhotosynthDarkAnoxicCO2Released;
        public float PhotosynthDarkAnoxicH2Released;
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
            ResolveAndUpdateOceanLayer(populationState, i, cellIndex, planetResourceMap);

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

            populationState.CanReplicate[i] = true;
            float metabolismStressMultiplier = 1f;
            float speedCapMultiplier = 1f;

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
            else if (metabolism == MetabolismType.Saprotrophy || metabolism == MetabolismType.Predation || metabolism == MetabolismType.Fermentation || metabolism == MetabolismType.Methanotrophy)
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
                ProcessPhotosynthesisMetabolism(
                    populationState,
                    i,
                    dir,
                    cellIndex,
                    planetResourceMap,
                    settings,
                    performance,
                    basalCost,
                    o2PerC,
                    energyPerC,
                    maxStore,
                    dtTick,
                    ref metabolismStressMultiplier,
                    ref speedCapMultiplier,
                    ref debugSnapshot);
            }
            else if (metabolism == MetabolismType.Saprotrophy)
            {
                float envC = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.OrganicC, cellIndex);
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
                        float o2Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.O2, cellIndex);
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
                    float o2Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.O2, cellIndex);
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
                populationState.StarveCh4Seconds[i] = 0f;
                populationState.O2ToxicSeconds[i] = 0f;
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
                float o2Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.O2, cellIndex);
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
                populationState.StarveCh4Seconds[i] = 0f;
                populationState.O2ToxicSeconds[i] = 0f;
                populationState.StarveCo2Seconds[i] = 0f;
                populationState.StarveH2sSeconds[i] = 0f;
                populationState.StarveLightSeconds[i] = 0f;
                populationState.StarveH2Seconds[i] = 0f;
            }
            else if (metabolism == MetabolismType.Hydrogenotrophy)
            {
                float co2Need = Mathf.Max(0f, settings.HydrogenotrophyCO2PerTick);
                float h2Need = Mathf.Max(0f, settings.HydrogenotrophyH2PerTick);

                float co2Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.CO2, cellIndex);
                float h2Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.H2, cellIndex);
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
                populationState.StarveCh4Seconds[i] = 0f;
                populationState.O2ToxicSeconds[i] = 0f;
                populationState.StarveStoredCSeconds[i] = 0f;
            }
            else if (metabolism == MetabolismType.Fermentation)
            {
                float cNeed = Mathf.Max(0f, settings.FermentationOrganicCPerTick);
                float organicCAvailable = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.OrganicC, cellIndex);
                float pulled = Mathf.Min(cNeed, organicCAvailable);
                bool lackOrganicC = cNeed > 0f && pulled <= Mathf.Epsilon;
                bool lackStoredC = false;

                if (pulled > 0f)
                {
                    // Fermenters can now retain part of consumed OrganicC as biomass, similar to other carbon-fixing metabolisms.
                    float assimilation = Mathf.Clamp01(settings.FermentationAssimilationFraction);
                    float desiredStore = pulled * assimilation;
                    float storeCapacity = Mathf.Max(0f, maxStore - populationState.OrganicCStore[i]);
                    float storedOrganicC = Mathf.Min(desiredStore, storeCapacity);
                    float fermentedOrganicC = Mathf.Max(0f, pulled - storedOrganicC);

                    planetResourceMap.Add(ResourceType.OrganicC, cellIndex, -pulled);

                    if (storedOrganicC > 0f)
                        populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + storedOrganicC, 0f, maxStore);

                    if (fermentedOrganicC > 0f)
                    {
                        planetResourceMap.Add(ResourceType.H2, cellIndex, fermentedOrganicC);
                        planetResourceMap.Add(ResourceType.CO2, cellIndex, fermentedOrganicC);
                        // Keep energy proportional only to the actually fermented fraction.
                        populationState.Energy[i] += Mathf.Max(0f, settings.FermentationEnergyPerTick) * (fermentedOrganicC / Mathf.Max(0.0001f, cNeed)) * performance;
                    }

                    lackStoredC = storeCapacity <= Mathf.Epsilon && desiredStore > 0f;
                }

                populationState.StarveOrganicCFoodSeconds[i] = UpdateStarveTimer(populationState.StarveOrganicCFoodSeconds[i], lackOrganicC, dtTick);
                populationState.StarveStoredCSeconds[i] = UpdateStarveTimer(populationState.StarveStoredCSeconds[i], lackStoredC, dtTick);
                populationState.StarveCo2Seconds[i] = 0f;
                populationState.StarveH2sSeconds[i] = 0f;
                populationState.StarveH2Seconds[i] = 0f;
                populationState.StarveLightSeconds[i] = 0f;
                populationState.StarveO2Seconds[i] = 0f;
                populationState.StarveCh4Seconds[i] = 0f;
                populationState.O2ToxicSeconds[i] = 0f;
            }
            else if (metabolism == MetabolismType.Methanogenesis)
            {
                float co2Need = Mathf.Max(0f, settings.MethanogenesisCO2PerTick);
                float h2Need = Mathf.Max(0f, settings.MethanogenesisH2PerTick);
                float co2Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.CO2, cellIndex);
                float h2Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.H2, cellIndex);
                float pulledRatio = Mathf.Clamp01(Mathf.Min(co2Need <= Mathf.Epsilon ? 1f : co2Available / co2Need, h2Need <= Mathf.Epsilon ? 1f : h2Available / h2Need));
                bool lackCo2 = false;
                bool lackH2 = false;
                bool lackStoredC = false;

                if (pulledRatio > 0f)
                {
                    float co2Consumed = co2Need * pulledRatio;
                    float h2Consumed = h2Need * pulledRatio;
                    float assimilation = Mathf.Clamp01(settings.MethanogenesisAssimilationFraction);
                    float desiredStore = co2Consumed * assimilation;
                    float storeCapacity = Mathf.Max(0f, maxStore - populationState.OrganicCStore[i]);
                    float storedOrganicC = Mathf.Min(desiredStore, storeCapacity);
                    float methanizedCarbon = Mathf.Max(0f, co2Consumed - storedOrganicC);

                    planetResourceMap.Add(ResourceType.CO2, cellIndex, -co2Consumed);
                    planetResourceMap.Add(ResourceType.H2, cellIndex, -h2Consumed);

                    if (storedOrganicC > 0f)
                        populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + storedOrganicC, 0f, maxStore);

                    if (methanizedCarbon > 0f)
                        planetResourceMap.Add(ResourceType.CH4, cellIndex, methanizedCarbon);

                    // Keep methanogenesis energy tied only to the carbon that is actually converted to CH4.
                    populationState.Energy[i] += Mathf.Max(0f, settings.MethanogenesisEnergyPerTick) * (methanizedCarbon / Mathf.Max(0.0001f, co2Need)) * performance;
                    lackStoredC = storeCapacity <= Mathf.Epsilon && desiredStore > 0f;
                }
                else
                {
                    lackCo2 = co2Need > 0f && co2Available <= Mathf.Epsilon;
                    lackH2 = h2Need > 0f && h2Available <= Mathf.Epsilon;
                }

                populationState.StarveCo2Seconds[i] = UpdateStarveTimer(populationState.StarveCo2Seconds[i], lackCo2, dtTick);
                populationState.StarveH2Seconds[i] = UpdateStarveTimer(populationState.StarveH2Seconds[i], lackH2, dtTick);
                populationState.StarveStoredCSeconds[i] = UpdateStarveTimer(populationState.StarveStoredCSeconds[i], lackStoredC, dtTick);
                populationState.StarveH2sSeconds[i] = 0f;
                populationState.StarveLightSeconds[i] = 0f;
                populationState.StarveOrganicCFoodSeconds[i] = 0f;
                populationState.StarveO2Seconds[i] = 0f;
                populationState.StarveCh4Seconds[i] = 0f;
                populationState.O2ToxicSeconds[i] = 0f;
            }
            else if (metabolism == MetabolismType.Methanotrophy)
            {
                float ch4Need = Mathf.Max(0f, settings.MethanotrophyCH4PerTick);
                float o2Need = Mathf.Max(0f, settings.MethanotrophyO2PerTick);
                float ch4Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.CH4, cellIndex);
                float o2Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.O2, cellIndex);
                float pulledRatio = Mathf.Clamp01(Mathf.Min(ch4Need <= Mathf.Epsilon ? 1f : ch4Available / ch4Need, o2Need <= Mathf.Epsilon ? 1f : o2Available / o2Need));
                bool lackCh4 = false;
                bool lackO2 = false;
                bool lackStoredC = false;
                bool o2Toxic = false;
                float comfortMax = Mathf.Max(0f, populationState.O2ComfortMax[i]);
                float stressMax = Mathf.Max(comfortMax, populationState.O2StressMax[i]);

                if (pulledRatio > 0f)
                {
                    float ch4Consumed = ch4Need * pulledRatio;
                    float o2Consumed = o2Need * pulledRatio;
                    float assimilation = Mathf.Clamp01(settings.MethanotrophyAssimilationFraction);
                    float desiredStore = ch4Consumed * assimilation;
                    float storeCapacity = Mathf.Max(0f, maxStore - populationState.OrganicCStore[i]);
                    float storedOrganicC = Mathf.Min(desiredStore, storeCapacity);
                    float oxidizedCH4 = Mathf.Max(0f, ch4Consumed - storedOrganicC);

                    planetResourceMap.Add(ResourceType.CH4, cellIndex, -ch4Consumed);
                    planetResourceMap.Add(ResourceType.O2, cellIndex, -o2Consumed);

                    if (storedOrganicC > 0f)
                        populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + storedOrganicC, 0f, maxStore);

                    if (oxidizedCH4 > 0f)
                        planetResourceMap.Add(ResourceType.CO2, cellIndex, oxidizedCH4);

                    // Keep methanotrophy energy tied to the oxidized (not stored) CH4 fraction.
                    populationState.Energy[i] += Mathf.Max(0f, settings.MethanotrophyEnergyPerTick) * (oxidizedCH4 / Mathf.Max(0.0001f, ch4Need)) * performance;
                    lackStoredC = storeCapacity <= Mathf.Epsilon && desiredStore > 0f;
                }
                else
                {
                    lackCh4 = ch4Need > 0f && ch4Available <= Mathf.Epsilon;
                    lackO2 = o2Need > 0f && o2Available <= Mathf.Epsilon;
                }

                if (stressMax > 0f && o2Available > comfortMax)
                {
                    o2Toxic = o2Available > stressMax;
                }

                populationState.StarveCh4Seconds[i] = UpdateStarveTimer(populationState.StarveCh4Seconds[i], lackCh4, dtTick);
                populationState.StarveO2Seconds[i] = UpdateStarveTimer(populationState.StarveO2Seconds[i], lackO2, dtTick);
                populationState.StarveStoredCSeconds[i] = UpdateStarveTimer(populationState.StarveStoredCSeconds[i], lackStoredC, dtTick);
                populationState.O2ToxicSeconds[i] = UpdateStarveTimer(populationState.O2ToxicSeconds[i], o2Toxic, dtTick);
                populationState.StarveCo2Seconds[i] = 0f;
                populationState.StarveH2sSeconds[i] = 0f;
                populationState.StarveH2Seconds[i] = 0f;
                populationState.StarveLightSeconds[i] = 0f;
                populationState.StarveOrganicCFoodSeconds[i] = 0f;
            }
            else
            {
                float co2Need = Mathf.Max(0f, settings.ChemosynthesisCo2NeedPerTick);
                float h2sNeed = Mathf.Max(0f, settings.ChemosynthesisH2sNeedPerTick);

                float co2Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.CO2, cellIndex);
                float h2sAvailable = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.H2S, cellIndex);
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
                    float o2Available = GetAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, ResourceType.O2, cellIndex);
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

            if (metabolism != MetabolismType.Methanotrophy)
            {
                populationState.StarveCh4Seconds[i] = 0f;
                populationState.O2ToxicSeconds[i] = 0f;
            }

            float metabolismBasalCostMultiplier = metabolism == MetabolismType.Predation ? Mathf.Max(0f, settings.PredatorBasalCostMultiplier) : 1f;
            float stressedBasal = basalCost * metabolismBasalCostMultiplier * (1f + stress * metabolismStressMultiplier);
            float speedMultiplier = metabolism == MetabolismType.Predation ? Mathf.Max(0f, settings.PredatorMoveSpeedMultiplier) : 1f;
            populationState.SpeedFactor[i] = Mathf.Clamp((populationState.Energy[i] / safeEnergyForFullSpeed) * performance * speedMultiplier, settings.MinSpeedFactor, speedCapMultiplier);
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

    private static void ProcessPhotosynthesisMetabolism(
        ReplicatorPopulationState populationState,
        int index,
        Vector3 dir,
        int cellIndex,
        PlanetResourceMap planetResourceMap,
        Settings settings,
        float performance,
        float basalCost,
        float o2PerC,
        float energyPerC,
        float maxStore,
        float dtTick,
        ref float metabolismStressMultiplier,
        ref float speedCapMultiplier,
        ref DebugSnapshot debugSnapshot)
    {
        float insolation = Mathf.Clamp01(planetResourceMap.GetInsolation(dir));
        bool lackCo2 = false;
        bool lackLight = false;
        bool lackO2 = false;
        bool lackStoredC = false;

        if (TryPhotosynthesisEnergyGain(populationState, index, cellIndex, planetResourceMap, settings, insolation, performance))
        {
            debugSnapshot.PhotosynthLightModeCount++;
        }
        else
        {
            if (insolation > 0f)
            {
                float co2Need = Mathf.Max(0f, settings.PhotosynthesisCo2PerTickAtFullInsolation) * insolation;
                float co2Available = GetAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, ResourceType.CO2, cellIndex);
                lackCo2 = co2Need > 0f && co2Available <= Mathf.Epsilon;
            }

            float gainedAerobic = TryPhotosynthAerobicDarkRespiration(populationState, index, cellIndex, planetResourceMap, settings, o2PerC, energyPerC, performance, out lackStoredC, out lackO2);
            if (gainedAerobic > 0f)
            {
                debugSnapshot.PhotosynthDarkAerobicModeCount++;
            }
            else
            {
                bool usedFallback = TryPhotosynthAnoxicDarkFallback(
                    populationState,
                    index,
                    cellIndex,
                    planetResourceMap,
                    settings,
                    basalCost,
                    performance,
                    out float fallbackUsed,
                    out float fallbackEnergy,
                    out float fallbackCO2,
                    out float fallbackH2);

                if (usedFallback)
                {
                    // Simplified biology abstraction: this is weak fermentation-like dark maintenance from stored photosynthate,
                    // not full anaerobic respiration. It keeps pre-oxygenation photosynths alive overnight but much weaker than aerobic nights.
                    debugSnapshot.PhotosynthDarkAnoxicFallbackModeCount++;
                    debugSnapshot.PhotosynthDarkAnoxicOrganicCConsumed += fallbackUsed;
                    debugSnapshot.PhotosynthDarkAnoxicEnergyGenerated += fallbackEnergy;
                    debugSnapshot.PhotosynthDarkAnoxicCO2Released += fallbackCO2;
                    debugSnapshot.PhotosynthDarkAnoxicH2Released += fallbackH2;
                    populationState.CanReplicate[index] = settings.PhotosynthDarkAnoxicCanReplicate;
                    speedCapMultiplier = Mathf.Min(speedCapMultiplier, 0.5f);
                    metabolismStressMultiplier = Mathf.Max(1f, settings.PhotosynthDarkAnoxicStressMultiplier);
                    lackLight = false;
                    lackStoredC = false;
                    lackO2 = false;
                }
                else
                {
                    lackLight = true;
                }
            }
        }

        if (insolation <= 0f)
        {
            if (populationState.OrganicCStore[index] <= Mathf.Epsilon)
            {
                lackStoredC = true;
            }

            if (GetAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, ResourceType.O2, cellIndex) <= Mathf.Epsilon)
            {
                bool fallbackPossible = settings.PhotosynthDarkAnoxicEnabled && populationState.OrganicCStore[index] > Mathf.Epsilon;
                lackO2 = !fallbackPossible;
            }
        }

        populationState.StarveCo2Seconds[index] = UpdateStarveTimer(populationState.StarveCo2Seconds[index], lackCo2, dtTick);
        populationState.StarveLightSeconds[index] = UpdateStarveTimer(populationState.StarveLightSeconds[index], lackLight, dtTick);
        populationState.StarveO2Seconds[index] = UpdateStarveTimer(populationState.StarveO2Seconds[index], lackO2, dtTick);
        populationState.StarveStoredCSeconds[index] = UpdateStarveTimer(populationState.StarveStoredCSeconds[index], lackStoredC, dtTick);
        populationState.StarveH2sSeconds[index] = 0f;
        populationState.StarveH2Seconds[index] = 0f;
        populationState.StarveOrganicCFoodSeconds[index] = 0f;
    }

    private static bool TryPhotosynthesisEnergyGain(
        ReplicatorPopulationState populationState,
        int index,
        int cellIndex,
        PlanetResourceMap planetResourceMap,
        Settings settings,
        float insolation,
        float performance)
    {
        if (insolation <= 0f)
        {
            return false;
        }

        float co2Need = Mathf.Max(0f, settings.PhotosynthesisCo2PerTickAtFullInsolation) * insolation;
        float co2Available = GetAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, ResourceType.CO2, cellIndex);
        float co2Consumed = Mathf.Min(co2Need, co2Available);
        if (co2Consumed <= 0f)
        {
            return false;
        }

        planetResourceMap.Add(ResourceType.CO2, cellIndex, -co2Consumed);
        planetResourceMap.Add(ResourceType.O2, cellIndex, co2Consumed);

        float producedEnergy = co2Consumed * Mathf.Max(0f, settings.PhotosynthesisEnergyPerCo2) * performance;
        populationState.Energy[index] += producedEnergy;

        float storedOrganicC = Mathf.Max(0f, settings.PhotosynthStoreFraction) * co2Consumed;
        if (storedOrganicC > 0f)
        {
            populationState.OrganicCStore[index] = Mathf.Clamp(populationState.OrganicCStore[index] + storedOrganicC, 0f, Mathf.Max(0f, settings.MaxOrganicCStore));
        }

        return true;
    }

    private static float TryPhotosynthAerobicDarkRespiration(
        ReplicatorPopulationState populationState,
        int index,
        int cellIndex,
        PlanetResourceMap planetResourceMap,
        Settings settings,
        float o2PerC,
        float energyPerC,
        float performance,
        out bool lackStoredC,
        out bool lackO2)
    {
        float desiredResp = Mathf.Max(0f, settings.NightRespirationCPerTick);
        float o2Available = GetAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, ResourceType.O2, cellIndex);
        bool hasStore = populationState.OrganicCStore[index] > 0f;
        lackStoredC = !hasStore && desiredResp > 0f;
        lackO2 = hasStore && desiredResp > 0f && o2Available <= Mathf.Epsilon;

        float gained = AerobicRespireFromStore(ref populationState.OrganicCStore[index], ref populationState.Energy[index], cellIndex, desiredResp, o2PerC, energyPerC, planetResourceMap);
        if (gained > 0f)
        {
            populationState.Energy[index] -= gained * (1f - performance);
            lackStoredC = false;
            lackO2 = false;
        }

        return gained;
    }

    private static bool TryPhotosynthAnoxicDarkFallback(
        ReplicatorPopulationState populationState,
        int index,
        int cellIndex,
        PlanetResourceMap planetResourceMap,
        Settings settings,
        float basalCost,
        float performance,
        out float organicCUsed,
        out float energyGenerated,
        out float co2Released,
        out float h2Released)
    {
        organicCUsed = 0f;
        energyGenerated = 0f;
        co2Released = 0f;
        h2Released = 0f;

        if (!settings.PhotosynthDarkAnoxicEnabled)
        {
            return false;
        }

        if (GetAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, ResourceType.O2, cellIndex) > Mathf.Epsilon)
        {
            return false;
        }

        float stored = populationState.OrganicCStore[index];
        if (stored <= Mathf.Epsilon)
        {
            return false;
        }

        float cUseRate = Mathf.Max(0f, settings.PhotosynthDarkAnoxicOrganicCUseRate);
        float cToUse = Mathf.Min(stored, cUseRate);
        if (cToUse <= 0f)
        {
            return false;
        }

        float aerobicEquivalentEnergy = cToUse * Mathf.Max(0f, settings.AerobicEnergyPerC);
        float weakEnergy = aerobicEquivalentEnergy * Mathf.Clamp01(settings.PhotosynthDarkAnoxicEnergyYieldMultiplier) * performance;

        float maxMaintenance = Mathf.Max(0f, basalCost) * Mathf.Clamp01(settings.PhotosynthDarkAnoxicMaxFractionOfBaseMaintenanceCovered);
        energyGenerated = Mathf.Min(weakEnergy, maxMaintenance);
        if (energyGenerated <= 0f)
        {
            return false;
        }

        organicCUsed = cToUse;
        populationState.OrganicCStore[index] = Mathf.Max(0f, stored - organicCUsed);
        populationState.Energy[index] += energyGenerated;

        co2Released = organicCUsed * Mathf.Clamp01(settings.PhotosynthDarkAnoxicCO2ReleaseFraction);
        if (co2Released > 0f)
        {
            planetResourceMap.Add(ResourceType.CO2, cellIndex, co2Released);
        }

        h2Released = organicCUsed * Mathf.Clamp01(settings.PhotosynthDarkAnoxicH2ReleaseFraction);
        if (h2Released > 0f)
        {
            planetResourceMap.Add(ResourceType.H2, cellIndex, h2Released);
        }

        float dissolvedOrganicLeak = organicCUsed * Mathf.Clamp01(settings.PhotosynthDarkAnoxicOrganicLeakFraction);
        if (dissolvedOrganicLeak > 0f)
        {
            planetResourceMap.Add(ResourceType.DissolvedOrganicLeak, cellIndex, dissolvedOrganicLeak);
        }

        return true;
    }

    private static float UpdateStarveTimer(float current, bool deprived, float dt)
    {
        return deprived ? (current + dt) : 0f;
    }

    private static int ResolveAndUpdateOceanLayer(ReplicatorPopulationState populationState, int index, int cellIndex, PlanetResourceMap planetResourceMap)
    {
        if (!planetResourceMap.IsOceanCell(cellIndex))
        {
            populationState.CurrentOceanLayerIndex[index] = -1;
            populationState.PreferredOceanLayerIndex[index] = -1;
            return -1;
        }

        int preferred = populationState.PreferredOceanLayerIndex[index];
        int current = populationState.CurrentOceanLayerIndex[index];
        int clampedPreferred = planetResourceMap.ClampOceanLayerIndex(cellIndex, preferred >= 0 ? preferred : current);
        int clampedCurrent = planetResourceMap.ClampOceanLayerIndex(cellIndex, current);

        if (clampedCurrent < 0)
        {
            clampedCurrent = clampedPreferred;
        }

        if (clampedCurrent < clampedPreferred)
        {
            clampedCurrent++;
        }
        else if (clampedCurrent > clampedPreferred)
        {
            clampedCurrent--;
        }

        populationState.PreferredOceanLayerIndex[index] = clampedPreferred;
        populationState.CurrentOceanLayerIndex[index] = clampedCurrent;
        return clampedCurrent;
    }

    private static float GetAgentResourceAtCurrentLayer(ReplicatorPopulationState populationState, int index, PlanetResourceMap planetResourceMap, ResourceType resourceType, int cellIndex)
    {
        int layer = populationState.CurrentOceanLayerIndex[index];
        if (layer >= 0 && planetResourceMap.IsOceanCell(cellIndex))
        {
            return planetResourceMap.GetResourceForCellLayer(resourceType, cellIndex, layer);
        }

        return planetResourceMap.GetCompatibilityResourceValue(resourceType, cellIndex);
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
