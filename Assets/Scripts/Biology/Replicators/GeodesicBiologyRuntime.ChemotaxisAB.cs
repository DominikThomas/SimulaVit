using System;
using System.Text;
using UnityEngine;

/// <summary>Read-only experiment measurements. No gameplay RNG, per-agent authority or runner.</summary>
public sealed partial class GeodesicBiologyRuntime
{
    private bool chemotacticSteeringEnabled = true;
    private bool chemotaxisABDiagnosticsEnabled, abContextSeen, abExternalClock, abResetPending = true;
    private bool abCoverageStartPending = true, abMixedWindow;
    private double abSimulationTime, abWindowStart, abWindowSeconds, abCoverageStart;
    private long abModeSwitches;
    private const int ABLocomotionCount = 4;
    // The first six bands are exactly TemperatureTelemetry's existing map; invalid is separate.
    private const int ABRingCount = 7;
    private static readonly string[] ABLocomotionNames = { "passive", "amoeboid", "flagellum", "anchored" };
    private static readonly string[] ABComparisonNames = { "sameNode", "cellOnly", "layerOnly", "cellAndLayer" };
    private static readonly string[] ABDeathNames = Enum.GetNames(typeof(DeathCause));
    private readonly double[] abOrganismSeconds = new double[ABLocomotionCount];
    private readonly double[] abMetabolicEnergy = new double[ABLocomotionCount];
    private readonly double[] abResidenceSeconds = new double[ABLocomotionCount * ABRingCount];
    private readonly long[] abParentBirths = new long[ABLocomotionCount], abDeaths = new long[ABLocomotionCount];
    private readonly long[] abDeathCauses = new long[ABDeathNames.Length];
    private readonly int[] abSnapshotRings = new int[ABLocomotionCount * ABRingCount];
    private long abInvalidComparisons, abUnsupportedComparisons, abNoReadinessComparisons, abSubFloorComparisons, abInitialSenses;
    private long abInvalidStates;
    private int abInvalidSnapshotLocomotion;

    private struct ABPopulationGroup
    {
        public int count, energyCount, carbonCount, hydrogenHabitatCount;
        public double energy, carbon, h2, performance, suitability;
    }
    private readonly ABPopulationGroup[] abPopulation = new ABPopulationGroup[ABLocomotionCount];

    private struct ABMovementBaseline
    {
        public double distanceKm, movementEnergy;
        public long crossings, layers, tumbles, births, deaths, limited, improving, worsening, neutral, verticalUnpaired;
    }
    private ABMovementBaseline abBaseline;

    private sealed class ABComparisonGroup
    {
        public readonly TaxisDeltaStatistics changes = new TaxisDeltaStatistics();
        public readonly TaxisDeltaStatistics experiencedChanges = new TaxisDeltaStatistics();
        public readonly long[] trends = new long[3], sampleTrends = new long[3], tumbles = new long[3];
        public long unchangedInputs, unchangedWithMemoryLag, crossings, sameEndpointWithCrossings;
        public double probabilitySum;
        public void Reset()
        {
            changes.Reset(); experiencedChanges.Reset();
            Array.Clear(trends, 0, trends.Length); Array.Clear(sampleTrends, 0, sampleTrends.Length); Array.Clear(tumbles, 0, tumbles.Length);
            probabilitySum = 0d; unchangedInputs = unchangedWithMemoryLag = crossings = sameEndpointWithCrossings = 0;
        }
    }
    private readonly ABComparisonGroup[] abComparisons =
    {
        new ABComparisonGroup(), new ABComparisonGroup(), new ABComparisonGroup(), new ABComparisonGroup()
    };

    /// <summary>Called with the authoritative pipeline clock. Mode selection does not reset gameplay state.</summary>
    public void SetChemotaxisABContext(bool steeringEnabled, bool diagnosticsEnabled, double simulationTime)
    {
        if (abContextSeen && chemotacticSteeringEnabled != steeringEnabled)
        {
            abModeSwitches++;
            abMixedWindow = true;
        }
        if (chemotaxisABDiagnosticsEnabled != diagnosticsEnabled)
        {
            abResetPending = true;
            abCoverageStartPending = true;
        }
        abContextSeen = true;
        chemotacticSteeringEnabled = steeringEnabled;
        chemotaxisABDiagnosticsEnabled = diagnosticsEnabled;
        abSimulationTime = simulationTime;
        abExternalClock = true;
    }

    private ABMovementBaseline CaptureABBaseline() => new ABMovementBaseline
    {
        distanceKm = activeDistanceKm, movementEnergy = locomotionEnergyPaid,
        crossings = activeCellCrossings, layers = activeLayerTransitions, tumbles = activeTumbles,
        births = this.births, deaths = this.deaths, limited = resourceLimited,
        improving = suitabilityImproving, worsening = suitabilityWorsening, neutral = suitabilityNeutral,
        verticalUnpaired = this.verticalUnpaired
    };

    private void AdvanceChemotaxisABStep(float dt)
    {
        if (!abExternalClock) abSimulationTime += dt;
        if (!chemotaxisABDiagnosticsEnabled) return;
        if (abResetPending)
        {
            ResetABWindow(abSimulationTime - dt);
            if (abCoverageStartPending) abCoverageStart = abWindowStart;
            abCoverageStartPending = abResetPending = false;
        }
        abWindowSeconds += dt;
    }

    private static int ABLocomotionIndex(LocomotionType type) => (uint)type < ABLocomotionCount ? (int)type : -1;

    private int ABRingBand(int cell, int layer)
    {
        if (grid == null || !grid.IsNodeActive(cell, layer)) return ABRingCount - 1;
        int band = temperatureTelemetry.GetVentRingBand(cell);
        return band >= 0 ? band : ABRingCount - 1;
    }

    private void ObserveABOrganismTime(int i, ReplicatorPopulationState state, float dt)
    {
        if (!chemotaxisABDiagnosticsEnabled) return;
        int group = ABLocomotionIndex(state.Locomotion[i]);
        if (group < 0) { abInvalidStates++; return; }
        // Folded into the existing lifecycle pass, before death/removal or birth. Each organism
        // alive during this step contributes once; newborns receive time from the next step.
        abOrganismSeconds[group] += dt;
        int ring = ABRingBand(state.GeodesicCellIndex[i], state.CurrentOceanLayerIndex[i]);
        abResidenceSeconds[group * ABRingCount + ring] += dt;
    }

    private void RecordABMetabolicEnergy(LocomotionType type, float awardedEnergy)
    {
        if (!chemotaxisABDiagnosticsEnabled) return;
        int group = ABLocomotionIndex(type);
        if (group >= 0 && float.IsFinite(awardedEnergy) && awardedEnergy >= 0f) abMetabolicEnergy[group] += awardedEnergy;
        else abInvalidStates++;
    }
    private void RecordABBirth(LocomotionType parent)
    {
        if (!chemotaxisABDiagnosticsEnabled) return;
        int group = ABLocomotionIndex(parent);
        if (group >= 0) abParentBirths[group]++; else abInvalidStates++;
    }
    private void RecordABDeath(LocomotionType type, DeathCause cause)
    {
        if (!chemotaxisABDiagnosticsEnabled) return;
        int group = ABLocomotionIndex(type);
        if (group >= 0) abDeaths[group]++; else abInvalidStates++;
        int causeIndex = (int)cause;
        abDeathCauses[(uint)causeIndex < abDeathCauses.Length ? causeIndex : 0]++;
    }

    private static bool ValidABSignal(in GeodesicTaxisObservation observation) => observation.valid
        && float.IsFinite(observation.h2) && float.IsFinite(observation.co2) && float.IsFinite(observation.o2)
        && float.IsFinite(observation.temperature) && float.IsFinite(observation.performance)
        && float.IsFinite(observation.oxygenEfficiency) && float.IsFinite(observation.suitability)
        && double.IsFinite(observation.reactionReadiness) && observation.reactionReadiness >= 0d;

    private void RecordABChemotaxisComparison(in GeodesicTaxisObservation current, in GeodesicTaxisObservation previous,
        in TaxisComparison comparison, MetabolismType metabolism, bool hadMemory, float chosenProbability, bool tumbled, int crossings)
    {
        if (!chemotaxisABDiagnosticsEnabled) return;
        if (!hadMemory) { abInitialSenses++; return; }
        if (!ValidABSignal(current) || !ValidABSignal(previous)) { abInvalidComparisons++; return; }
        if (steeringSettings.ActiveMovementMetabolismSuitabilityEnabled && metabolism != MetabolismType.Hydrogenotrophy)
        { abUnsupportedComparisons++; return; }
        bool changedCell = current.cell != previous.cell, changedLayer = current.layer != previous.layer;
        int category = (changedCell ? 1 : 0) + (changedLayer ? 2 : 0);
        ABComparisonGroup group = abComparisons[category];
        group.crossings += crossings;
        if (category == 0 && crossings > 0) group.sameEndpointWithCrossings++;
        group.changes.Observe(comparison);
        TaxisComparison experienced = CompareTaxis(current.reactionReadiness, previous.reactionReadiness,
            current.suitability, previous.suitability);
        group.experiencedChanges.Observe(experienced);
        group.sampleTrends[(int)experienced.trend]++;
        if (SameExperiencedInputs(current, previous))
        {
            group.unchangedInputs++;
            if (comparison.rawReadinessDelta != 0d) group.unchangedWithMemoryLag++;
        }
        int trend = (int)comparison.trend; // neutral / improving / worsening
        group.trends[trend]++;
        if (tumbled) group.tumbles[trend]++;
        group.probabilitySum += chosenProbability;
        if (current.reactionReadiness == 0d && previous.reactionReadiness == 0d) abNoReadinessComparisons++;
        if (Math.Max(current.reactionReadiness, previous.reactionReadiness) < locomotionSettings.taxisReadinessFloor)
            abSubFloorComparisons++;
    }

    private bool BeginABPopulationReport()
    {
        if (!chemotaxisABDiagnosticsEnabled) return false;
        Array.Clear(abPopulation, 0, abPopulation.Length);
        Array.Clear(abSnapshotRings, 0, abSnapshotRings.Length);
        abInvalidSnapshotLocomotion = 0;
        BeginMovementSamples();
        return true;
    }

    private void ObserveABPopulation(int i, ReplicatorPopulationState state)
    {
        int group = ABLocomotionIndex(state.Locomotion[i]);
        if (group < 0) { abInvalidSnapshotLocomotion++; return; }
        ref ABPopulationGroup snapshot = ref abPopulation[group];
        snapshot.count++;
        if (float.IsFinite(state.Energy[i])) { snapshot.energy += state.Energy[i]; snapshot.energyCount++; }
        if (float.IsFinite(state.OrganicCStore[i])) { snapshot.carbon += state.OrganicCStore[i]; snapshot.carbonCount++; }
        int ring = ABRingBand(state.GeodesicCellIndex[i], state.CurrentOceanLayerIndex[i]);
        abSnapshotRings[group * ABRingCount + ring]++;
        if (state.Metabolism[i] != MetabolismType.Hydrogenotrophy) return;
        // Habitat means always use metabolism-aware physiology, even in B or temperature-only sensing mode.
        GeodesicTaxisObservation habitat = ObserveMovementHabitat(i, state);
        if (!ValidABSignal(habitat)) return;
        snapshot.hydrogenHabitatCount++;
        snapshot.h2 += habitat.h2; snapshot.performance += habitat.performance; snapshot.suitability += habitat.suitability;
    }

    private static double ABRate(double numerator, double denominator) => denominator > 0d ? numerator / denominator : double.NaN;

    private void AppendABRings(StringBuilder text, int group, double denominator, bool residence, bool percent)
    {
        for (int ring = 0; ring < ABRingCount; ring++)
        {
            if (ring > 0) text.Append('/');
            double value = 0d;
            int first = group < 0 ? 0 : group, end = group < 0 ? ABLocomotionCount : group + 1;
            for (int type = first; type < end; type++) value += residence
                ? abResidenceSeconds[type * ABRingCount + ring] : abSnapshotRings[type * ABRingCount + ring];
            text.Append((percent ? 100d * ABRate(value, denominator) : value).ToString("G6"));
        }
    }

    private void LogChemotaxisABReport(int population)
    {
        // Formatting and bounded arrays are touched only at the existing reporting boundary.
        string prefix = $"[GeodesicChemotaxisAB] simTime={abSimulationTime:G9} mode={(chemotacticSteeringEnabled ? "A" : "B")} mixedWindow={abMixedWindow} runModeSwitches={abModeSwitches} ";
        double organismSeconds = 0d, energy = 0d, carbon = 0d;
        int energyN = 0, carbonN = 0;
        for (int group = 0; group < ABLocomotionCount; group++)
        {
            organismSeconds += abOrganismSeconds[group];
            energy += abPopulation[group].energy; carbon += abPopulation[group].carbon;
            energyN += abPopulation[group].energyCount; carbonN += abPopulation[group].carbonCount;
        }
        double activeSeconds = abOrganismSeconds[1] + abOrganismSeconds[2];
        double distance = activeDistanceKm - abBaseline.distanceKm, movementEnergy = locomotionEnergyPaid - abBaseline.movementEnergy;
        long tumbles = activeTumbles - abBaseline.tumbles;
        var text = new StringBuilder(prefix);
        text.Append($"kind=population seed={planet.randomSeed} biologySeed={biologySeed} windowStart={abWindowStart:G9} windowSeconds={abWindowSeconds:G6} coverageStart={abCoverageStart:G9} population={population} locomotion(P/A/F/X)={abPopulation[0].count}/{abPopulation[1].count}/{abPopulation[2].count}/{abPopulation[3].count} births/deaths={births - abBaseline.births}/{deaths - abBaseline.deaths} starvation={abDeathCauses[(int)DeathCause.EnergyDepletion]} deathCauses=");
        bool hasCause = false;
        for (int cause = 0; cause < abDeathCauses.Length; cause++)
        {
            if (abDeathCauses[cause] == 0) continue;
            if (hasCause) text.Append(',');
            text.Append(ABDeathNames[cause]).Append(':').Append(abDeathCauses[cause]); hasCause = true;
        }
        if (!hasCause) text.Append("none");
        text.Append($" meanEnergy/C={ABRate(energy, energyN):G6}/{ABRate(carbon, carbonN):G6} organismSeconds={organismSeconds:G9} activeOrganismSeconds={activeSeconds:G9} resourceLimited(interval/cumulative/perOrgSecond)={resourceLimited - abBaseline.limited}/{resourceLimited}/{ABRate(resourceLimited - abBaseline.limited, organismSeconds):G6} activeKm(interval/cumulative/perActiveOrgSecond)={distance:G6}/{activeDistanceKm:G6}/{ABRate(distance, activeSeconds):G6} activeCrossings(interval/cumulative/perActiveOrgSecond)={activeCellCrossings - abBaseline.crossings}/{activeCellCrossings}/{ABRate(activeCellCrossings - abBaseline.crossings, activeSeconds):G6} activeLayers(interval/cumulative/perActiveOrgSecond)={activeLayerTransitions - abBaseline.layers}/{activeLayerTransitions}/{ABRate(activeLayerTransitions - abBaseline.layers, activeSeconds):G6} movementEnergy(interval/cumulative/perActiveOrgSecond)={movementEnergy:G6}/{locomotionEnergyPaid:G6}/{ABRate(movementEnergy, activeSeconds):G6} tumbles(interval/cumulative/perActiveOrgSecond)={tumbles}/{activeTumbles}/{ABRate(tumbles, activeSeconds):G6} trend(N/I/W)={suitabilityNeutral - abBaseline.neutral}/{suitabilityImproving - abBaseline.improving}/{suitabilityWorsening - abBaseline.worsening} invalidComparisons={abInvalidComparisons} unsupportedComparisons={abUnsupportedComparisons} zeroReadinessExperiencedPairs={abNoReadinessComparisons} subFloorExperiencedPairs={abSubFloorComparisons} initialSenses={abInitialSenses} unpairedVertical={verticalUnpaired - abBaseline.verticalUnpaired} invalidStates={abInvalidStates} invalidLocomotionSnapshot={abInvalidSnapshotLocomotion} neutralReferenceTumble={steeringSettings.MovementTumbleChanceNeutral:G6} metabolismAwareSensing={steeringSettings.ActiveMovementMetabolismSuitabilityEnabled} ringOrder=0/1/2/3-4/5+/unreachable/invalid ringN=");
        AppendABRings(text, -1, population, false, false);
        text.Append(" ringPct="); AppendABRings(text, -1, population, false, true);
        text.Append(" residencePct="); AppendABRings(text, -1, organismSeconds, true, true);
        Debug.Log(text.ToString());

        for (int group = 0; group < ABLocomotionCount; group++)
        {
            ABPopulationGroup p = abPopulation[group]; double time = abOrganismSeconds[group];
            text.Clear(); text.Append(prefix).Append($"kind=locomotion type={ABLocomotionNames[group]} snapshotN={p.count} validHydrogenHabitatN={p.hydrogenHabitatCount} meanH2/performance/suitability={ABRate(p.h2, p.hydrogenHabitatCount):G6}/{ABRate(p.performance, p.hydrogenHabitatCount):G6}/{ABRate(p.suitability, p.hydrogenHabitatCount):G6} meanEnergy/C={ABRate(p.energy, p.energyCount):G6}/{ABRate(p.carbon, p.carbonCount):G6} organismSeconds={time:G9} achievedMetabolicEnergy={abMetabolicEnergy[group]:G6} metabolicEnergyPerOrgSecond={ABRate(abMetabolicEnergy[group], time):G6} parentBirths/deaths={abParentBirths[group]}/{abDeaths[group]} birth/deathRatePerOrgSecond={ABRate(abParentBirths[group], time):G6}/{ABRate(abDeaths[group], time):G6} ringN=");
            AppendABRings(text, group, p.count, false, false);
            text.Append(" ringPct="); AppendABRings(text, group, p.count, false, true);
            text.Append(" residencePct="); AppendABRings(text, group, time, true, true);
            Debug.Log(text.ToString());
        }

        text.Clear(); text.Append(prefix).Append("kind=comparisons order=N/I/W categoriesAreEndpointChanges=true");
        for (int category = 0; category < abComparisons.Length; category++)
        {
            ABComparisonGroup c = abComparisons[category];
            text.Append($" {ABComparisonNames[category]}{{memoryTrend={c.trends[0]}/{c.trends[1]}/{c.trends[2]},sampleTrend={c.sampleTrends[0]}/{c.sampleTrends[1]}/{c.sampleTrends[2]},tumblesByMemoryTrend={c.tumbles[0]}/{c.tumbles[1]}/{c.tumbles[2]},unchangedInputs/withMemoryLag={c.unchangedInputs}/{c.unchangedWithMemoryLag},cellCrossings/sameEndpointWithCrossings={c.crossings}/{c.sameEndpointWithCrossings},meanP={ABRate(c.probabilitySum, c.changes.count):G6},memoryDelta[{c.changes.CompactSummary()}],sampleDelta[{c.experiencedChanges.CompactSummary()}]}}");
        }
        Debug.Log(text.ToString());
        ResetABWindow(abSimulationTime);
    }

    private void ResetABWindow(double start)
    {
        abWindowStart = start; abWindowSeconds = 0d; abMixedWindow = false;
        abBaseline = CaptureABBaseline();
        Array.Clear(abOrganismSeconds, 0, abOrganismSeconds.Length); Array.Clear(abMetabolicEnergy, 0, abMetabolicEnergy.Length);
        Array.Clear(abResidenceSeconds, 0, abResidenceSeconds.Length); Array.Clear(abParentBirths, 0, abParentBirths.Length);
        Array.Clear(abDeaths, 0, abDeaths.Length); Array.Clear(abDeathCauses, 0, abDeathCauses.Length);
        foreach (ABComparisonGroup comparison in abComparisons) comparison.Reset();
        abInvalidComparisons = abUnsupportedComparisons = abNoReadinessComparisons = abSubFloorComparisons = abInitialSenses = abInvalidStates = 0;
    }

    private void ClearChemotaxisAB()
    {
        chemotacticSteeringEnabled = true; chemotaxisABDiagnosticsEnabled = abContextSeen = abExternalClock = false;
        abResetPending = abCoverageStartPending = true; abMixedWindow = false;
        abSimulationTime = abWindowStart = abWindowSeconds = abCoverageStart = 0d; abModeSwitches = 0;
        ResetABWindow(0d);
    }
}
