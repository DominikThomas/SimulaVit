using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public class ReplicatorMetabolismSystem
{
    private static readonly ProfilerMarker MetabolismHotLoopMarker = new ProfilerMarker("ReplicatorMetabolismSystem.HotLoop");
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
        Func<int, DeathCause> resolveEnergyDeathCauseAtIndex,
        Action<Replicator> depositDeathOrganicC,
        Action<MetabolismType, DeathCause> registerDeathCause,
        out DebugSnapshot debugSnapshot)
    {
        // Metabolism/resource lookups must use simulation/resource grid resolution.
        int resolution = planetResourceMap != null
            ? Mathf.Max(1, planetResourceMap.SimulationResolution)
            : Mathf.Max(1, planetGenerator.resolution);
        float basalCost = Mathf.Max(0f, settings.BasalEnergyCostPerSecond) * dtTick;
        float safeEnergyForFullSpeed = Mathf.Max(0.0001f, settings.EnergyForFullSpeed);

        float o2PerC = Mathf.Max(0f, settings.AerobicO2PerC);
        float energyPerC = Mathf.Max(0f, settings.AerobicEnergyPerC);
        float maxStore = Mathf.Max(0f, settings.MaxOrganicCStore);

        var deadIndices = new List<int>(64);
        var deadCauses = new List<DeathCause>(64);

        debugSnapshot = default;

        populationState.EnsureMatchesAgentCount(agents);

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
                        // Layer-aware OrganicC uptake (saprotrophy): remove consumed detrital carbon from the agent's current layer when valid.
                        AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.OrganicC, cellIndex, -totalActuallyUsed);

                        if (actualStore > 0f)
                            populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + actualStore, 0f, maxStore);

                        if (actualRespire > 0f)
                        {
                            float o2Consumed = actualRespire * o2PerC;
                            AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.O2, cellIndex, -o2Consumed);
                            AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.CO2, cellIndex, actualRespire);
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

                    float gained = AerobicRespireFromStore(populationState, i, ref populationState.OrganicCStore[i], ref populationState.Energy[i], cellIndex, desiredResp, o2PerC, energyPerC, planetResourceMap);
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

                float gained = AerobicRespireFromStore(populationState, i, ref populationState.OrganicCStore[i], ref populationState.Energy[i], cellIndex, desiredResp, o2PerC, energyPerC, planetResourceMap);
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

                    AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.CO2, cellIndex, -co2Consumed);
                    AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.H2, cellIndex, -h2Consumed);

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

                    // Layer-aware OrganicC uptake (fermentation): pull substrate from the agent-local ocean layer when valid.
                    AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.OrganicC, cellIndex, -pulled);

                    if (storedOrganicC > 0f)
                        populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + storedOrganicC, 0f, maxStore);

                    if (fermentedOrganicC > 0f)
                    {
                        AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.H2, cellIndex, fermentedOrganicC);
                        AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.CO2, cellIndex, fermentedOrganicC);
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

                    AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.CO2, cellIndex, -co2Consumed);
                    AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.H2, cellIndex, -h2Consumed);

                    if (storedOrganicC > 0f)
                        populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + storedOrganicC, 0f, maxStore);

                    if (methanizedCarbon > 0f)
                        AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.CH4, cellIndex, methanizedCarbon);

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

                    AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.CH4, cellIndex, -ch4Consumed);
                    AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.O2, cellIndex, -o2Consumed);

                    if (storedOrganicC > 0f)
                        populationState.OrganicCStore[i] = Mathf.Clamp(populationState.OrganicCStore[i] + storedOrganicC, 0f, maxStore);

                    if (oxidizedCH4 > 0f)
                        AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.CO2, cellIndex, oxidizedCH4);

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

                    AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.CO2, cellIndex, -co2Consumed);
                    AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.H2S, cellIndex, -h2sConsumed);
                    AddAgentResourceAtCurrentLayer(populationState, i, planetResourceMap, metabolism, ResourceType.S0, cellIndex, h2sConsumed);

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
                    cause = resolveEnergyDeathCauseAtIndex(index);
                }

                populationState.CopyToDebugState(index, agent);
                registerDeathCause(populationState.Metabolism[index], cause);
                depositDeathOrganicC(agent);
                RemoveAgentAtSwapBack(agents, populationState, index);
            }
        }
    }

    private static void RemoveAgentAtSwapBack(List<Replicator> agents, ReplicatorPopulationState populationState, int index)
    {
        int last = agents.Count - 1;
        if (index < 0 || index > last)
        {
            return;
        }

        if (index != last)
        {
            agents[index] = agents[last];
        }

        agents.RemoveAt(last);
        populationState.RemoveAgentAtSwapBack(index);
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
        float surfaceInsolation = Mathf.Clamp01(planetResourceMap.GetInsolation(dir));
        float photosynthesisLight = ResolvePhotosynthesisLightAvailability(populationState, index, cellIndex, planetResourceMap, surfaceInsolation);
        bool lackCo2 = false;
        bool lackLight = false;
        bool lackO2 = false;
        bool lackStoredC = false;

        if (TryPhotosynthesisEnergyGain(populationState, index, cellIndex, planetResourceMap, settings, photosynthesisLight, performance))
        {
            debugSnapshot.PhotosynthLightModeCount++;
        }
        else
        {
            if (photosynthesisLight > 0f)
            {
                float co2Need = Mathf.Max(0f, settings.PhotosynthesisCo2PerTickAtFullInsolation) * photosynthesisLight;
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

        if (photosynthesisLight <= 0f)
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

        AddAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, MetabolismType.Photosynthesis, ResourceType.CO2, cellIndex, -co2Consumed);
        AddAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, MetabolismType.Photosynthesis, ResourceType.O2, cellIndex, co2Consumed);

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

        float gained = AerobicRespireFromStore(populationState, index, ref populationState.OrganicCStore[index], ref populationState.Energy[index], cellIndex, desiredResp, o2PerC, energyPerC, planetResourceMap);
        if (gained > 0f)
        {
            populationState.Energy[index] -= gained * (1f - performance);
            lackStoredC = false;
            lackO2 = false;
        }

        return gained;
    }

    private static float ResolvePhotosynthesisLightAvailability(
        ReplicatorPopulationState populationState,
        int index,
        int cellIndex,
        PlanetResourceMap planetResourceMap,
        float surfaceInsolation)
    {
        if (TryGetValidAgentOceanLayer(populationState, index, planetResourceMap, cellIndex, out int layer))
        {
            return planetResourceMap.GetLayeredLightForCell(cellIndex, layer, surfaceInsolation);
        }

        return Mathf.Clamp01(surfaceInsolation);
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
            AddAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, MetabolismType.Photosynthesis, ResourceType.CO2, cellIndex, co2Released);
        }

        h2Released = organicCUsed * Mathf.Clamp01(settings.PhotosynthDarkAnoxicH2ReleaseFraction);
        if (h2Released > 0f)
        {
            AddAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, MetabolismType.Photosynthesis, ResourceType.H2, cellIndex, h2Released);
        }

        float dissolvedOrganicLeak = organicCUsed * Mathf.Clamp01(settings.PhotosynthDarkAnoxicOrganicLeakFraction);
        if (dissolvedOrganicLeak > 0f)
        {
            AddAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, MetabolismType.Photosynthesis, ResourceType.DissolvedOrganicLeak, cellIndex, dissolvedOrganicLeak);
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

        int activeLayerCount = planetResourceMap.GetOceanActiveLayerCount(cellIndex);
        if (activeLayerCount <= 0)
        {
            populationState.CurrentOceanLayerIndex[index] = -1;
            populationState.PreferredOceanLayerIndex[index] = -1;
            return -1;
        }

        int deepestLayer = activeLayerCount - 1;
        int current = planetResourceMap.ClampOceanLayerIndex(cellIndex, populationState.CurrentOceanLayerIndex[index]);
        int preferred = planetResourceMap.ClampOceanLayerIndex(cellIndex, populationState.PreferredOceanLayerIndex[index]);
        if (current < 0)
        {
            current = deepestLayer;
        }

        MetabolismType metabolism = populationState.Metabolism[index];
        LocomotionType locomotion = populationState.Locomotion[index];
        bool bottomAssociated = locomotion == LocomotionType.Anchored
            || metabolism == MetabolismType.SulfurChemosynthesis
            || metabolism == MetabolismType.Hydrogenotrophy;

        if (bottomAssociated)
        {
            int next = StepTowardAdjacentLayer(current, deepestLayer);
            populationState.CurrentOceanLayerIndex[index] = next;
            populationState.PreferredOceanLayerIndex[index] = deepestLayer;
            return next;
        }

        float currentScore = EvaluateLayerScore(populationState, index, cellIndex, current, planetResourceMap);
        int bestProbeLayer = current;
        float bestProbeScore = currentScore;

        int upLayer = current - 1;
        int downLayer = current + 1;
        bool canProbeUp = upLayer >= 0;
        bool canProbeDown = downLayer <= deepestLayer;

        if (canProbeUp)
        {
            float upScore = EvaluateLayerScore(populationState, index, cellIndex, upLayer, planetResourceMap);
            if (upScore > bestProbeScore)
            {
                bestProbeScore = upScore;
                bestProbeLayer = upLayer;
            }
        }

        if (canProbeDown)
        {
            float downScore = EvaluateLayerScore(populationState, index, cellIndex, downLayer, planetResourceMap);
            if (downScore > bestProbeScore)
            {
                bestProbeScore = downScore;
                bestProbeLayer = downLayer;
            }
        }

        bool shouldProbe = ShouldPerformVerticalProbe(populationState, index);
        if (!shouldProbe && preferred >= 0 && Mathf.Abs(preferred - current) == 1)
        {
            float preferredScore = EvaluateLayerScore(populationState, index, cellIndex, preferred, planetResourceMap);
            if (preferredScore + 0.03f >= currentScore)
            {
                shouldProbe = true;
                bestProbeLayer = preferred;
                bestProbeScore = preferredScore;
            }
            else
            {
                preferred = -1;
            }
        }

        int nextLayer = current;
        if (shouldProbe && bestProbeLayer != current && bestProbeScore > currentScore + GetVerticalImproveThreshold(locomotion))
        {
            int trendDirection = bestProbeLayer > current ? 1 : -1;
            nextLayer = bestProbeLayer;

            int trendLayer = nextLayer + trendDirection;
            if (trendLayer >= 0 && trendLayer <= deepestLayer)
            {
                float trendScore = EvaluateLayerScore(populationState, index, cellIndex, trendLayer, planetResourceMap);
                preferred = trendScore + 0.02f >= bestProbeScore ? trendLayer : -1;
            }
            else
            {
                preferred = -1;
            }
        }
        else if (shouldProbe && bestProbeLayer != current && bestProbeScore < currentScore - 0.08f)
        {
            preferred = -1;
        }

        populationState.PreferredOceanLayerIndex[index] = preferred;
        populationState.CurrentOceanLayerIndex[index] = nextLayer;
        return nextLayer;
    }

    private static int StepTowardAdjacentLayer(int current, int target)
    {
        if (current < target)
        {
            return current + 1;
        }

        if (current > target)
        {
            return current - 1;
        }

        return current;
    }

    private static bool ShouldPerformVerticalProbe(ReplicatorPopulationState populationState, int index)
    {
        float age = Mathf.Max(0f, populationState.Age[index]);
        float seed = populationState.MovementSeed[index];
        LocomotionType locomotion = populationState.Locomotion[index];

        float probesPerSecond;
        switch (locomotion)
        {
            case LocomotionType.PassiveDrift:
                probesPerSecond = 0.32f;
                break;
            case LocomotionType.Amoeboid:
                probesPerSecond = 0.18f;
                break;
            case LocomotionType.Flagellum:
                probesPerSecond = 0.2f;
                break;
            default:
                probesPerSecond = 0.08f;
                break;
        }

        float phase = Mathf.Abs(Mathf.Sin((age * probesPerSecond * 6.28318f) + (seed * 17.123f)));
        return phase > 0.965f;
    }

    private static float GetVerticalImproveThreshold(LocomotionType locomotion)
    {
        switch (locomotion)
        {
            case LocomotionType.PassiveDrift:
                return 0.02f;
            case LocomotionType.Amoeboid:
                return 0.04f;
            case LocomotionType.Flagellum:
                return 0.035f;
            default:
                return 0.06f;
        }
    }

    private static float EvaluateLayerScore(ReplicatorPopulationState populationState, int index, int cellIndex, int layerIndex, PlanetResourceMap planetResourceMap)
    {
        float co2 = NormalizeLayeredResource(planetResourceMap, ResourceType.CO2, cellIndex, layerIndex, 0.35f);
        float o2 = NormalizeLayeredResource(planetResourceMap, ResourceType.O2, cellIndex, layerIndex, 0.25f);
        float organic = NormalizeLayeredResource(planetResourceMap, ResourceType.OrganicC, cellIndex, layerIndex, 0.25f);
        float h2 = NormalizeLayeredResource(planetResourceMap, ResourceType.H2, cellIndex, layerIndex, 0.22f);
        float h2s = NormalizeLayeredResource(planetResourceMap, ResourceType.H2S, cellIndex, layerIndex, 0.22f);
        float ch4 = NormalizeLayeredResource(planetResourceMap, ResourceType.CH4, cellIndex, layerIndex, 0.18f);
        float light = Mathf.Clamp01(planetResourceMap.GetLayerLightFactor(cellIndex, layerIndex));

        float score;
        switch (populationState.Metabolism[index])
        {
            case MetabolismType.SulfurChemosynthesis:
                score = Mathf.Min(h2s, co2);
                break;
            case MetabolismType.Hydrogenotrophy:
                score = Mathf.Min(h2, co2);
                break;
            case MetabolismType.Photosynthesis:
                score = Mathf.Min(light, co2) + 0.06f;
                break;
            case MetabolismType.Saprotrophy:
                score = Mathf.Min(organic, o2);
                break;
            case MetabolismType.Predation:
                score = o2 * 0.6f + organic * 0.4f;
                break;
            case MetabolismType.Fermentation:
                score = organic;
                break;
            case MetabolismType.Methanogenesis:
                score = Mathf.Min(h2, co2) + (1f - o2) * 0.15f;
                break;
            case MetabolismType.Methanotrophy:
                score = Mathf.Min(ch4, o2);
                break;
            default:
                score = 0f;
                break;
        }

        if (populationState.Locomotion[index] == LocomotionType.PassiveDrift)
        {
            float age = populationState.Age[index];
            float seed = populationState.MovementSeed[index];
            float pseudoRandom = 0.5f + 0.5f * Mathf.Sin((seed * 11.37f) + (age * 0.73f) + (layerIndex * 1.93f));
            score = (score * 0.55f) + (0.45f * pseudoRandom);
        }

        return Mathf.Clamp01(score);
    }

    private static float NormalizeLayeredResource(
        PlanetResourceMap planetResourceMap,
        ResourceType resourceType,
        int cellIndex,
        int layerIndex,
        float goodEnoughScale)
    {
        float raw = planetResourceMap.GetResourceForCellLayer(
            resourceType,
            cellIndex,
            layerIndex,
            PlanetResourceMap.AggregateCompatibilityCallsite.Metabolism);
        return Mathf.Clamp01(raw / Mathf.Max(0.0001f, goodEnoughScale));
    }

    private static bool TryGetValidAgentOceanLayer(ReplicatorPopulationState populationState, int index, PlanetResourceMap planetResourceMap, int cellIndex, out int clampedLayer)
    {
        clampedLayer = -1;
        if (!planetResourceMap.IsOceanCell(cellIndex))
        {
            return false;
        }

        int requestedLayer = populationState.CurrentOceanLayerIndex[index];
        if (requestedLayer < 0)
        {
            return false;
        }

        clampedLayer = planetResourceMap.ClampOceanLayerIndex(cellIndex, requestedLayer);
        return clampedLayer >= 0;
    }

    private static float GetAgentResourceAtCurrentLayer(ReplicatorPopulationState populationState, int index, PlanetResourceMap planetResourceMap, ResourceType resourceType, int cellIndex)
    {
        if (TryGetValidAgentOceanLayer(populationState, index, planetResourceMap, cellIndex, out int layer))
        {
            return planetResourceMap.GetResourceForCellLayer(
                resourceType,
                cellIndex,
                layer,
                PlanetResourceMap.AggregateCompatibilityCallsite.Metabolism);
        }

        return planetResourceMap.Get(resourceType, cellIndex, PlanetResourceMap.AggregateCompatibilityCallsite.Metabolism);
    }

    private static bool TryResolveAgentResourceWriteLayer(
        ReplicatorPopulationState populationState,
        int index,
        PlanetResourceMap planetResourceMap,
        int cellIndex,
        out int clampedLayer)
    {
        return planetResourceMap.TryResolveLayeredOceanWriteLayer(
            cellIndex,
            populationState.CurrentOceanLayerIndex[index],
            populationState.PreferredOceanLayerIndex[index],
            out clampedLayer);
    }

    // Layer-aware metabolism writes migrated in this pass:
    // - aerobic O2 consumption / CO2 release from stored-organic-C respiration
    // - direct O2/CO2/OrganicC handling in photosynthesis, saprotrophy, fermentation, and methanotrophy paths
    // - hydrogenotrophy local CO2/H2 consumption
    // - methanogenesis local CO2/H2 consumption and CH4 production
    // - sulfur chemosynthesis local CO2/H2S/S0 handling
    // - local H2 and dissolved-organic-leak byproducts from photosynth dark-anoxic fallback
    // - fallback-to-preferred-layer writes when current layer is invalid but ocean context is known
    // Intentionally aggregate in this pass: any writes where no valid ocean layer can be resolved
    // (land/non-ocean agents, invalid layer data), which preserves compatibility bridges in PlanetResourceMap.Add(...).
    private static void AddAgentResourceAtCurrentLayer(
        ReplicatorPopulationState populationState,
        int index,
        PlanetResourceMap planetResourceMap,
        MetabolismType metabolismType,
        ResourceType resourceType,
        int cellIndex,
        float delta)
    {
        if (Mathf.Approximately(delta, 0f))
        {
            return;
        }

        bool canUseLayeredResource = planetResourceMap.ShouldUseLayeredOceanForResource(resourceType, cellIndex);
        if (canUseLayeredResource && TryResolveAgentResourceWriteLayer(populationState, index, planetResourceMap, cellIndex, out int layer))
        {
            planetResourceMap.AddResourceForCellLayer(
                resourceType,
                cellIndex,
                layer,
                delta,
                PlanetResourceMap.AggregateCompatibilityCallsite.Metabolism);
            return;
        }

        PlanetResourceMap.LayeredWriteFallbackReason reason = DetermineWriteFallbackReason(populationState, index, planetResourceMap, resourceType, cellIndex, canUseLayeredResource);
        planetResourceMap.RecordMetabolismLayeredWriteFallback(resourceType, (int)metabolismType, reason);
        planetResourceMap.Add(resourceType, cellIndex, delta, PlanetResourceMap.AggregateCompatibilityCallsite.Metabolism);
    }

    private static PlanetResourceMap.LayeredWriteFallbackReason DetermineWriteFallbackReason(
        ReplicatorPopulationState populationState,
        int index,
        PlanetResourceMap planetResourceMap,
        ResourceType resourceType,
        int cellIndex,
        bool canUseLayeredResource)
    {
        if (!planetResourceMap.IsCellValid(cellIndex)) return PlanetResourceMap.LayeredWriteFallbackReason.InvalidCell;
        bool isOceanCell = planetResourceMap.IsOceanCell(cellIndex);
        if (!isOceanCell)
        {
            return canUseLayeredResource
                ? PlanetResourceMap.LayeredWriteFallbackReason.LandOrNonOcean
                : PlanetResourceMap.LayeredWriteFallbackReason.ResourceNotLayeredNonOcean;
        }

        if (!canUseLayeredResource) return PlanetResourceMap.LayeredWriteFallbackReason.ResourceNotLayeredInOcean;
        if (planetResourceMap.GetOceanActiveLayerCount(cellIndex) <= 0) return PlanetResourceMap.LayeredWriteFallbackReason.NoActiveOceanLayers;

        int current = populationState.CurrentOceanLayerIndex[index];
        int preferred = populationState.PreferredOceanLayerIndex[index];
        if (current < 0 && preferred < 0) return PlanetResourceMap.LayeredWriteFallbackReason.MissingPopulationStateSync;
        return PlanetResourceMap.LayeredWriteFallbackReason.InvalidCurrentOrPreferredLayer;
    }

    private static float AerobicRespireFromStore(
        ReplicatorPopulationState populationState,
        int index,
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
        float o2Available = GetAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, ResourceType.O2, cellIndex);
        float ratio = o2Needed <= Mathf.Epsilon ? 1f : Mathf.Clamp01(o2Available / o2Needed);
        cUsed *= ratio;

        if (cUsed <= 0f) return 0f;

        float o2Consumed = cUsed * o2PerC;
        AddAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, MetabolismType.Photosynthesis, ResourceType.O2, cellIndex, -o2Consumed);
        AddAgentResourceAtCurrentLayer(populationState, index, planetResourceMap, MetabolismType.Photosynthesis, ResourceType.CO2, cellIndex, cUsed);

        organicCStore = Mathf.Max(0f, organicCStore - cUsed);
        float gainedEnergy = cUsed * energyPerC;
        energy += gainedEnergy;

        return gainedEnergy;
    }
}
