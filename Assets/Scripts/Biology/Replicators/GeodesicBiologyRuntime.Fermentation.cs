using System;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private float fermentationNeed, fermentationEnergy, fermentationAssimilation, hydrogenFermentationChance, fermentationGate;
    private bool fermentationO2Inhibition = true;
    private float fermentationO2Floor = .65f, fermentationMargin = 20f;
    private Vector2 fermentationRange = new Vector2(285.15f,330.15f);
    private System.Random fermentationMutationRandom;
    private float[] fermentationOrganicByNode = Array.Empty<float>();
    private long fermentationAttempts, fermentationAllowed, fermentationBlocked, fermentationBirths, fermentationDeaths;
    private long fermentationLocalGate, fermentationInheritedGate, fermentationPrecisionRejected, fermentationInvalid;
    private readonly long[] fermentationBlocks = new long[3], fermentationParentBirths = new long[4];
    private long fermentationSamples, fermentationInhibited;
    private double fermentationConsumed, fermentationStored, fermentationCO2, fermentationH2;
    private double fermentationPerformanceSum, fermentationO2Sum, fermentationEfficiencySum, fermentationLocalCSum, fermentationLocalInventorySum;
    private double firstFermentationMutationTime = -1d;

    public void SetFermentationControls(float organicPerTick, float energyPerTick, float assimilation,
        bool oxygenInhibition, float oxygenFloor, float transitionChance, float minimumOrganicC,
        Vector2 temperatureRange, float lethalMargin)
    {
        fermentationNeed = organicPerTick; fermentationEnergy = energyPerTick;
        fermentationAssimilation = float.IsFinite(assimilation) ? Mathf.Clamp01(assimilation) : float.NaN;
        fermentationO2Inhibition = oxygenInhibition;
        fermentationO2Floor = float.IsFinite(oxygenFloor) ? Mathf.Clamp01(oxygenFloor) : .65f;
        hydrogenFermentationChance = float.IsFinite(transitionChance) ? Mathf.Clamp01(transitionChance) : 0f;
        fermentationGate = float.IsFinite(minimumOrganicC) ? Mathf.Max(0f,minimumOrganicC) : float.NaN;
        fermentationRange = temperatureRange; fermentationMargin = lethalMargin;
    }

    public string DescribeResolvedFermentationControls(float comfort, float stress, float maximumStore) => FormattableString.Invariant(
        $"[GeodesicFermentationConfig] OrganicCPerTick={fermentationNeed:G9} energyParameter={fermentationEnergy:G9} assimilation={fermentationAssimilation:G9} referenceTickSeconds={ReferenceTickSeconds:G9} energy=parameter*fermentedC/max(.0001,need) CO2/H2=fermentedC fullStore=fermentUnstoredCarbon atomicMeasuredInputsAndProducts=true O2Inhibition={fermentationO2Inhibition} O2Comfort/Stress/Floor={comfort:G9}/{stress:G9}/{fermentationO2Floor:G9} maxStore={maximumStore:G9} mutationOuter/Inner={metabolicMutationChance:G9}/{hydrogenFermentationChance:G9} priority=sulfurSelectionThenFermentation localOrganicCGate={fermentationGate:G9} gateUnits=concentration inheritedActualChildStoreFallback=true fallbackDoesNotFeed=true temperatureRangeK={fermentationRange.x:G9}/{fermentationRange.y:G9} lethalMarginK={fermentationMargin:G9}");

    private System.Random FermentationRandom => fermentationMutationRandom ??=
        new System.Random(unchecked(biologySeed ^ 0x4645524D));

    private void TryMutateFermentationChild(Replicator child, float inheritedCarbon)
    {
        if (hydrogenFermentationChance <= 0f || FermentationRandom.NextDouble() >= hydrogenFermentationChance) return;
        fermentationAttempts++;
        int cell = child.geodesicCellIndex, layer = child.currentOceanLayerIndex;
        int block = -1; bool local = false, inherited = false;
        if (!grid.IsNodeActive(cell,layer)) block = 0;
        else
        {
            local = resources.TryGetConcentration(cell,layer,GeodesicOceanResource.OrganicC,out float c)
                && float.IsFinite(c) && float.IsFinite(fermentationGate) && c >= fermentationGate;
            inherited = float.IsFinite(inheritedCarbon) && float.IsFinite(fermentationGate) && inheritedCarbon >= fermentationGate;
            if (!local && !inherited) block = 1;
            else if (!float.IsFinite(fermentationRange.x) || !float.IsFinite(fermentationRange.y) || !float.IsFinite(fermentationMargin)) block = 2;
        }
        if (block >= 0) { fermentationBlocked++; fermentationBlocks[block]++; return; }
        fermentationAllowed++; if(local) fermentationLocalGate++; else fermentationInheritedGate++;
        if(firstFermentationMutationTime < 0d) firstFermentationMutationTime = abSimulationTime;
        child.metabolism = MetabolismType.Fermentation; child.color = Color.orangeRed;
        child.optimalTempMin = Mathf.Min(fermentationRange.x,fermentationRange.y);
        child.optimalTempMax = Mathf.Max(fermentationRange.x,fermentationRange.y);
        child.lethalTempMargin = Mathf.Max(1f,fermentationMargin);
    }

    private void ObserveFermentationRequest(in Request r, float efficiency)
    {
        fermentationSamples++; if(efficiency < 1f) fermentationInhibited++;
        fermentationPerformanceSum += r.TemperaturePerformance; fermentationO2Sum += oxygenByNode[r.Node];
        fermentationEfficiencySum += efficiency; fermentationLocalCSum += fermentationOrganicByNode[r.Node];
        fermentationLocalInventorySum += fermentationOrganicByNode[r.Node] * grid.PhysicalLayerVolumeKm3[r.Node];
    }

    private void CommitFermentation(int index, ReplicatorPopulationState state, float maximumStore, bool observe)
    {
        ref Request r = ref requests[index];
        float before = state.OrganicCStore[index], energyBefore = state.Energy[index];
        var t = resources.CommitFermentation(r.Node,r.Actual,r.NeedA,fermentationEnergy,fermentationAssimilation,before,maximumStore);
        r.Actual = t.extent;
        if(t.status == GeodesicHydrogenotrophyBalance.CommitStatus.PrecisionRejected) fermentationPrecisionRejected++;
        if(t.status == GeodesicHydrogenotrophyBalance.CommitStatus.Invalid) fermentationInvalid++;
        if(t.status != GeodesicHydrogenotrophyBalance.CommitStatus.Committed) return;
        state.OrganicCStore[index] = t.storeAfter; state.Energy[index] += t.energy;
        if (chemotaxisABDiagnosticsEnabled) RecordABMetabolicEnergy(state.Locomotion[index],t.energy);
        extentByMetabolism[(int)MetabolismType.Fermentation] += t.extent;
        energyByMetabolism[(int)MetabolismType.Fermentation] += t.energy;
        fermentationConsumed += t.consumed; fermentationStored += (double)t.storeAfter-before;
        fermentationCO2 += t.co2Produced; fermentationH2 += t.h2Produced;
        if(!observe) return;
        r.FermentationInput = t.consumed; r.FermentationCO2 = t.co2Produced; r.FermentationH2 = t.h2Produced;
        r.DiagnosticStoreBefore = before; r.DiagnosticStoreAfter = t.storeAfter;
        r.DiagnosticEnergyDelta = (double)state.Energy[index]-energyBefore; r.HydrogenEnergyAwarded = t.energy;
        double fermented = t.consumed - ((double)t.storeAfter-before);
        accountingDiagnostics.RecordNodeExchange(r.A,-r.NeedA*r.Actual,-t.consumed,false);
        accountingDiagnostics.RecordNodeExchange(GeodesicOceanResource.CO2,fermented,t.co2Produced,false);
        accountingDiagnostics.RecordNodeExchange(GeodesicOceanResource.H2,fermented,t.h2Produced,false);
    }

    private void RecordFermentationAccounting(in Request r)
    {
        accountingDiagnostics.RecordReaction(r.Metabolism,r.A,r.B,GeodesicOceanResource.CO2,
            r.NeedA*r.Desired,0d,r.FermentationInput,0d,r.FermentationCO2,1d,1d,1d,
            r.DiagnosticStoreAfter-r.DiagnosticStoreBefore,r.DiagnosticStoreBefore,r.DiagnosticStoreAfter,
            r.HydrogenEnergyAwarded,r.DiagnosticEnergyDelta,r.Actual>0d,r.NeedA*r.CompetedActual,0d,
            GeodesicOceanResource.H2,r.FermentationH2,1d);
    }

    private void LogFermentationDiagnostics(ReplicatorPopulationState state)
    {
        int p=0,a=0,f=0,x=0;
        for(int i=0;i<state.Count;i++) if(state.Metabolism[i]==MetabolismType.Fermentation)
            switch(state.Locomotion[i]) { case LocomotionType.PassiveDrift:p++;break; case LocomotionType.Amoeboid:a++;break; case LocomotionType.Flagellum:f++;break; case LocomotionType.Anchored:x++;break; }
        double n=Math.Max(1,fermentationSamples);
        Debug.Log($"[GeodesicFermentationTelemetry] cumulative=true living={p+a+f+x} populationP/A/F/X={p}/{a}/{f}/{x} mutationAttempts/Allowed/Blocked={fermentationAttempts}/{fermentationAllowed}/{fermentationBlocked} gateBlocks(invalidHabitat/noLocalOrInheritedC/invalidTemplate)={fermentationBlocks[0]}/{fermentationBlocks[1]}/{fermentationBlocks[2]} allowedViaLocal/Inherited={fermentationLocalGate}/{fermentationInheritedGate} firstMutationTime={firstFermentationMutationTime:G9} births/deaths={fermentationBirths}/{fermentationDeaths} parentBirthsP/A/F/X={fermentationParentBirths[0]}/{fermentationParentBirths[1]}/{fermentationParentBirths[2]}/{fermentationParentBirths[3]} OrganicCConsumed={fermentationConsumed:G9} carbonAssimilated={fermentationStored:G9} CO2Produced={fermentationCO2:G9} H2Produced={fermentationH2:G9} energy={energyByMetabolism[(int)MetabolismType.Fermentation]:G9} requestSamples/O2Inhibited={fermentationSamples}/{fermentationInhibited} meanTemperaturePerformance={fermentationPerformanceSum/n:G9} meanLocalO2/efficiency={fermentationO2Sum/n:G9}/{fermentationEfficiencySum/n:G9} meanLocalOrganicC(concentration/inventory)={fermentationLocalCSum/n:G9}/{fermentationLocalInventorySum/n:G9} precisionRejected/invalid={fermentationPrecisionRejected}/{fermentationInvalid}");
    }

    private void ClearFermentation()
    {
        fermentationMutationRandom=null; fermentationOrganicByNode=Array.Empty<float>();
        fermentationAttempts=fermentationAllowed=fermentationBlocked=fermentationBirths=fermentationDeaths=0;
        fermentationLocalGate=fermentationInheritedGate=fermentationPrecisionRejected=fermentationInvalid=fermentationSamples=fermentationInhibited=0;
        fermentationConsumed=fermentationStored=fermentationCO2=fermentationH2=0d;
        fermentationPerformanceSum=fermentationO2Sum=fermentationEfficiencySum=fermentationLocalCSum=fermentationLocalInventorySum=0d;
        firstFermentationMutationTime=-1d; Array.Clear(fermentationBlocks,0,fermentationBlocks.Length); Array.Clear(fermentationParentBirths,0,fermentationParentBirths.Length);
    }
}
