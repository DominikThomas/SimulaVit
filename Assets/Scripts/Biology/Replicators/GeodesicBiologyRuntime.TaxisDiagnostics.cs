using System;
using UnityEngine;

public sealed partial class GeodesicBiologyRuntime
{
    private const int CurrentTaxisModelVersion = 2;
    private enum TaxisTrend { Neutral, Improving, Worsening }
    private struct TaxisComparison
    {
        public TaxisTrend trend;
        public double rawReadinessDelta;
        public float rawScoreDelta, normalizedDelta, improvementThreshold, worseningThreshold;
    }

    private GeodesicTaxisObservation ObserveSteeringHabitat(int i, ReplicatorPopulationState state)
    {
        GeodesicTaxisObservation observation = ObserveMovementHabitat(i, state);
        if (!steeringSettings.ActiveMovementMetabolismSuitabilityEnabled)
        {
            observation.reactionReadiness = observation.performance;
            observation.suitability = observation.performance;
        }
        return observation;
    }

    private float SteeringScore(double readiness) => steeringSettings.ActiveMovementMetabolismSuitabilityEnabled
        ? BoundedReactionReadiness(readiness) : (float)Math.Max(0d, Math.Min(1d, readiness));

    private void EnsureTaxisMemoryModel(ref GeodesicActiveMovementState active)
    {
        if (active.taxisModelVersion == CurrentTaxisModelVersion
            && active.memoryUsesMetabolism == steeringSettings.ActiveMovementMetabolismSuitabilityEnabled
            && (!active.hasSuitability || (active.lastTaxisObservation.valid
                && double.IsFinite(active.lastTaxisObservation.reactionReadiness)
                && float.IsFinite(active.lastTaxisObservation.suitability)))) return;
        // An older score or a changed sensing mode must never be compared against the new units.
        // Reset experience only: position, heading, propulsion, RNG sequence and vertical travel survive.
        active.taxisModelVersion = CurrentTaxisModelVersion;
        active.memoryUsesMetabolism = steeringSettings.ActiveMovementMetabolismSuitabilityEnabled;
        active.hasSuitability = false;
        active.reactionReadinessMemory = 0d;
        active.suitabilityMemory = 0f;
        active.lastTaxisObservation = default;
        active.cellCrossingsSinceSense = 0;
        active.verticalOriginRecorded = false;
        taxisMemoryInitializations++;
    }

    private TaxisComparison CompareTaxis(double current, double remembered, float score, float rememberedScore)
    {
        current = double.IsFinite(current) && current >= 0d ? current : 0d;
        remembered = double.IsFinite(remembered) && remembered >= 0d ? remembered : 0d;
        var comparison = new TaxisComparison
        {
            rawReadinessDelta = current - remembered,
            rawScoreDelta = score - rememberedScore,
            improvementThreshold = SafeMovementValue(steeringSettings.MovementSuitabilityImprovementThreshold, 0.05f, 0f, 1f),
            worseningThreshold = SafeMovementValue(steeringSettings.MovementSuitabilityWorseningThreshold, 0.05f, 0f, 1f)
        };
        // Compare fractional changes in the underlying readiness, before display-score compression.
        // The floor prevents arbitrarily small numerical resource traces from becoming strong taxis.
        double denominator = Math.Max(locomotionSettings.taxisReadinessFloor, Math.Max(current, remembered));
        comparison.normalizedDelta = (float)Math.Max(-1d, Math.Min(1d, comparison.rawReadinessDelta / denominator));
        if (comparison.normalizedDelta > 0f && comparison.normalizedDelta >= comparison.improvementThreshold)
            comparison.trend = TaxisTrend.Improving;
        else if (comparison.normalizedDelta < 0f && comparison.normalizedDelta <= -comparison.worseningThreshold)
            comparison.trend = TaxisTrend.Worsening;
        return comparison;
    }

    private sealed class TaxisDeltaStatistics
    {
        public long count;
        private double rawMin, rawMax, rawSum, rawAbsoluteSum, normalizedMin, normalizedMax, normalizedSum, normalizedAbsoluteSum;
        private readonly long[] bins = new long[7];

        public void Observe(in TaxisComparison comparison)
        {
            double raw = comparison.rawScoreDelta, normalized = comparison.normalizedDelta;
            if (count++ == 0) { rawMin = rawMax = raw; normalizedMin = normalizedMax = normalized; }
            rawMin = Math.Min(rawMin, raw); rawMax = Math.Max(rawMax, raw);
            normalizedMin = Math.Min(normalizedMin, normalized); normalizedMax = Math.Max(normalizedMax, normalized);
            rawSum += raw; rawAbsoluteSum += Math.Abs(raw);
            normalizedSum += normalized; normalizedAbsoluteSum += Math.Abs(normalized);
            double absolute = Math.Abs(normalized);
            int bin = absolute == 0d ? 0 : absolute < 1e-4d ? 1 : absolute < 1e-3d ? 2
                : absolute < 0.01d ? 3 : absolute < 0.05d ? 4 : absolute < 0.25d ? 5 : 6;
            bins[bin]++;
        }

        public string Summary()
        {
            double divisor = Math.Max(1L, count);
            return $"count={count} rawScoreDelta(min/mean/max/meanAbs)={rawMin:G6}/{rawSum / divisor:G6}/{rawMax:G6}/{rawAbsoluteSum / divisor:G6} relativeReadinessDelta(min/mean/max/meanAbs)={normalizedMin:G6}/{normalizedSum / divisor:G6}/{normalizedMax:G6}/{normalizedAbsoluteSum / divisor:G6} absRelativeBins(0,<1e-4,<1e-3,<.01,<.05,<.25,>=.25)={bins[0]}/{bins[1]}/{bins[2]}/{bins[3]}/{bins[4]}/{bins[5]}/{bins[6]}";
        }

        public void Reset()
        {
            count = 0;
            rawMin = rawMax = rawSum = rawAbsoluteSum = normalizedMin = normalizedMax = normalizedSum = normalizedAbsoluteSum = 0d;
            Array.Clear(bins, 0, bins.Length);
        }
    }

    private readonly TaxisDeltaStatistics horizontalDeltaStats = new TaxisDeltaStatistics();
    private readonly TaxisDeltaStatistics verticalDeltaStats = new TaxisDeltaStatistics();
    private long taxisMemoryInitializations, verticalUnpaired, unchangedInputSamples, changedInputSamples, sameHabitatSenses, changedHabitatSenses;
    private long neutralExactComparison, neutralDeadbandComparison, changedSamplesInsideDeadband;
    private readonly long[] crossingHistogram = new long[3], trendSenseCounts = new long[3], trendTumbles = new long[3];
    private readonly double[] trendTumbleProbability = new double[3];
    private long crossingsBetweenSenses;
    private int maximumCrossingsBetweenSenses;
    private double senseIntervalSum;

    private static bool SameExperiencedInputs(in GeodesicTaxisObservation a, in GeodesicTaxisObservation b) =>
        a.valid == b.valid && a.cell == b.cell && a.layer == b.layer && a.h2 == b.h2 && a.co2 == b.co2
        && a.o2 == b.o2 && a.temperature == b.temperature && a.performance == b.performance
        && a.oxygenEfficiency == b.oxygenEfficiency && a.reactionReadiness == b.reactionReadiness;

    private void RecordHorizontalTaxis(in GeodesicTaxisObservation current, in GeodesicTaxisObservation previous,
        in TaxisComparison comparison, int crossings, float interval)
    {
        horizontalDeltaStats.Observe(comparison);
        bool unchanged = SameExperiencedInputs(current, previous);
        if (unchanged) unchangedInputSamples++; else changedInputSamples++;
        if (current.cell == previous.cell && current.layer == previous.layer) sameHabitatSenses++; else changedHabitatSenses++;
        if (comparison.trend == TaxisTrend.Neutral)
        {
            if (comparison.rawReadinessDelta == 0d) neutralExactComparison++;
            else neutralDeadbandComparison++;
            if (!unchanged && comparison.rawReadinessDelta != 0d) changedSamplesInsideDeadband++;
        }
        crossingsBetweenSenses += crossings;
        maximumCrossingsBetweenSenses = Math.Max(maximumCrossingsBetweenSenses, crossings);
        crossingHistogram[Math.Min(2, crossings)]++;
        senseIntervalSum += interval;
    }

    private void RecordTaxisTumble(TaxisTrend trend, float probability, bool tumbled)
    {
        int group = (int)trend;
        trendSenseCounts[group]++;
        trendTumbleProbability[group] += probability;
        if (tumbled) trendTumbles[group]++;
    }

    private void LogTaxisDistributions()
    {
        double senses = Math.Max(1L, horizontalDeltaStats.count);
        Debug.Log($"[GeodesicTaxisDistribution] cumulative=true model={CurrentTaxisModelVersion} memoryInitializations={taxisMemoryInitializations} {horizontalDeltaStats.Summary()} actualInputs(unchanged/changed)={unchangedInputSamples}/{changedInputSamples} habitat(same/different)={sameHabitatSenses}/{changedHabitatSenses} neutral(exactMemoryMatch/deadband)={neutralExactComparison}/{neutralDeadbandComparison} changedSamplesInsideDeadband={changedSamplesInsideDeadband} sensedCellCrossings(mean/max/0/1/2+)={crossingsBetweenSenses / senses:G6}/{maximumCrossingsBetweenSenses}/{crossingHistogram[0]}/{crossingHistogram[1]}/{crossingHistogram[2]} meanSenseIntervalSeconds={senseIntervalSum / senses:G6} relativeThresholds(better/worse)={steeringSettings.MovementSuitabilityImprovementThreshold:G6}/{steeringSettings.MovementSuitabilityWorseningThreshold:G6} readinessFloor={locomotionSettings.taxisReadinessFloor:G6}");
        Debug.Log($"[GeodesicTaxisResponse] order=neutral/improving/worsening comparisons={trendSenseCounts[0]}/{trendSenseCounts[1]}/{trendSenseCounts[2]} actualTumbles={trendTumbles[0]}/{trendTumbles[1]}/{trendTumbles[2]} meanChosenTumbleProbability={trendTumbleProbability[0] / Math.Max(1L, trendSenseCounts[0]):G6}/{trendTumbleProbability[1] / Math.Max(1L, trendSenseCounts[1]):G6}/{trendTumbleProbability[2] / Math.Max(1L, trendSenseCounts[2]):G6} verticalPaired=({verticalDeltaStats.Summary()}) verticalUnpaired={verticalUnpaired}");
    }

    private struct TaxisSenseDetail
    {
        public long serial;
        public float simulationTime, previousScore, baseTumbleProbability, chosenTumbleProbability, senseSeconds;
        public double previousReadiness;
        public GeodesicTaxisObservation observation;
        public TaxisComparison comparison;
        public bool initializedNow, tumbled;
        public LocomotionType locomotion;
        public int crossings;
    }
    private struct VerticalTaxisDetail
    {
        public bool pending, paired;
        public int originCell, originLayer, arrivalCell, arrivalLayer, trialDirection, nextDirection;
        public float beforeScore, afterScore;
        public double beforeReadiness, afterReadiness;
        public TaxisComparison comparison;
    }
    private struct TrackedTaxisOrganism
    {
        public long id;
        public LocomotionType locomotion;
        public bool seen, hasSense, hasPeak;
        public TaxisSenseDetail latest, peak;
        public VerticalTaxisDetail vertical;
    }
    private readonly TrackedTaxisOrganism[] trackedTaxis = new TrackedTaxisOrganism[4];
    private bool detailedTaxisWasEnabled;
    private long detailedTaxisSelectedId;
    private int detailedTaxisLimit;
    private float nextDetailedTaxisLogTime;

    private void BeginDetailedTaxisBatch()
    {
        bool enabled = locomotionSettings.detailedTaxisDiagnostics;
        int limit = locomotionSettings.trackedTaxisOrganismId > 0 ? 1 : locomotionSettings.maximumTrackedTaxisOrganisms;
        if (enabled != detailedTaxisWasEnabled || detailedTaxisSelectedId != locomotionSettings.trackedTaxisOrganismId || detailedTaxisLimit != limit)
        {
            Array.Clear(trackedTaxis, 0, trackedTaxis.Length);
            nextDetailedTaxisLogTime = 0f;
        }
        detailedTaxisWasEnabled = enabled;
        detailedTaxisSelectedId = locomotionSettings.trackedTaxisOrganismId;
        detailedTaxisLimit = limit;
        if (!enabled) return;
        for (int slot = 0; slot < limit; slot++) trackedTaxis[slot].seen = false;
    }

    private int TrackTaxisOrganism(long id, LocomotionType locomotion)
    {
        if (!locomotionSettings.detailedTaxisDiagnostics || (detailedTaxisSelectedId > 0 && id != detailedTaxisSelectedId)) return -1;
        int empty = -1;
        for (int slot = 0; slot < detailedTaxisLimit; slot++)
        {
            if (trackedTaxis[slot].id == id)
            {
                trackedTaxis[slot].seen = true;
                trackedTaxis[slot].locomotion = locomotion;
                return slot;
            }
            if (empty < 0 && trackedTaxis[slot].id == 0) empty = slot;
        }
        if (empty >= 0) trackedTaxis[empty] = new TrackedTaxisOrganism { id = id, locomotion = locomotion, seen = true };
        return empty;
    }

    private void RecordDetailedTaxisSense(int slot, in GeodesicTaxisObservation observation, float previousScore,
        double previousReadiness, in TaxisComparison comparison, float baseProbability, float chosenProbability,
        bool tumbled, bool initializing, int crossings, float senseSeconds)
    {
        if (slot < 0) return;
        ref TrackedTaxisOrganism tracked = ref trackedTaxis[slot];
        var detail = new TaxisSenseDetail
        {
            serial = tracked.latest.serial + 1, simulationTime = passiveMovementTime, observation = observation,
            locomotion = tracked.locomotion,
            previousScore = previousScore, previousReadiness = previousReadiness, comparison = comparison,
            baseTumbleProbability = baseProbability, chosenTumbleProbability = chosenProbability,
            tumbled = tumbled, initializedNow = initializing, crossings = crossings, senseSeconds = senseSeconds
        };
        tracked.latest = detail;
        if (!tracked.hasPeak || Math.Abs(detail.comparison.normalizedDelta) >= Math.Abs(tracked.peak.comparison.normalizedDelta)) tracked.peak = detail;
        tracked.hasSense = true;
        tracked.hasPeak = true;
    }

    private void RecordDetailedVerticalTaxis(int slot, in GeodesicActiveMovementState active,
        in TaxisComparison comparison, int trialDirection, bool paired)
    {
        if (slot < 0) return;
        trackedTaxis[slot].vertical = new VerticalTaxisDetail
        {
            pending = true, paired = paired, originCell = active.verticalOriginCell, originLayer = active.verticalOriginLayer,
            arrivalCell = active.verticalArrivalCell, arrivalLayer = active.verticalArrivalLayer,
            trialDirection = trialDirection, nextDirection = active.verticalDirection,
            beforeScore = active.verticalBaseline, afterScore = active.verticalArrivalSuitability,
            beforeReadiness = active.verticalReadinessBefore, afterReadiness = active.verticalReadinessAfter, comparison = comparison
        };
    }

    private void EndDetailedTaxisBatch()
    {
        if (!locomotionSettings.detailedTaxisDiagnostics) return;
        for (int slot = 0; slot < detailedTaxisLimit; slot++)
            if (!trackedTaxis[slot].seen) trackedTaxis[slot] = default;
        if (Time.realtimeSinceStartup < nextDetailedTaxisLogTime) return;
        nextDetailedTaxisLogTime = Time.realtimeSinceStartup + locomotionSettings.detailedTaxisLogIntervalRealSeconds;
        bool found = false;
        for (int slot = 0; slot < detailedTaxisLimit; slot++)
        {
            ref TrackedTaxisOrganism tracked = ref trackedTaxis[slot];
            if (!tracked.seen) continue;
            found = true;
            if (tracked.hasSense)
            {
                LogDetailedTaxisSense(tracked, tracked.latest, "latest");
                if (tracked.hasPeak && tracked.peak.serial != tracked.latest.serial) LogDetailedTaxisSense(tracked, tracked.peak, "largestDeltaSinceReport");
                tracked.hasPeak = false;
            }
            if (tracked.vertical.pending)
            {
                VerticalTaxisDetail v = tracked.vertical;
                Debug.Log($"[GeodesicVerticalTaxisTrace] id={tracked.id} locomotion={tracked.locomotion} paired={v.paired} origin={v.originCell}/{v.originLayer} arrival={v.arrivalCell}/{v.arrivalLayer} direction(trial/next)={v.trialDirection}/{v.nextDirection} suitability(before/after)={v.beforeScore:G9}/{v.afterScore:G9} readiness(before/after)={v.beforeReadiness:G9}/{v.afterReadiness:G9} rawScoreDelta={v.comparison.rawScoreDelta:G9} normalizedDelta={v.comparison.normalizedDelta:G9} thresholds(better/worse)={v.comparison.improvementThreshold:G6}/{v.comparison.worseningThreshold:G6} classification={v.comparison.trend}");
                tracked.vertical.pending = false;
            }
        }
        if (!found && detailedTaxisSelectedId > 0) Debug.Log($"[GeodesicTaxisTrace] id={detailedTaxisSelectedId} noActiveOrganismFound=true");
    }

    private static void LogDetailedTaxisSense(in TrackedTaxisOrganism tracked, in TaxisSenseDetail detail, string sample)
    {
        GeodesicTaxisObservation o = detail.observation;
        string decision = detail.initializedNow ? "initialize" : detail.tumbled ? "tumble" : "continue";
        Debug.Log($"[GeodesicTaxisTrace] id={tracked.id} locomotion={detail.locomotion} sample={sample} simTime={detail.simulationTime:G9} cell/layer={o.cell}/{o.layer} H2={o.h2:G9} CO2={o.co2:G9} O2={o.o2:G9} temperatureK={o.temperature:G9} performance={o.performance:G9} oxygenEfficiency={o.oxygenEfficiency:G9} readiness={o.reactionReadiness:G9} previousReadiness={detail.previousReadiness:G9} suitability={o.suitability:G9} previousSuitability={detail.previousScore:G9} rawScoreDelta={detail.comparison.rawScoreDelta:G9} rawReadinessDelta={detail.comparison.rawReadinessDelta:G9} normalizedDelta={detail.comparison.normalizedDelta:G9} thresholds(better/worse)={detail.comparison.improvementThreshold:G6}/{detail.comparison.worseningThreshold:G6} tumbleProbability(reference/actual)={detail.baseTumbleProbability:G6}/{detail.chosenTumbleProbability:G6} classification={detail.comparison.trend} outcome={decision} cellsCrossedSinceSense={detail.crossings} senseSeconds={detail.senseSeconds:G6}");
    }

    private void ClearTaxisDiagnostics()
    {
        horizontalDeltaStats.Reset(); verticalDeltaStats.Reset();
        taxisMemoryInitializations = verticalUnpaired = unchangedInputSamples = changedInputSamples = sameHabitatSenses = changedHabitatSenses = 0;
        neutralExactComparison = neutralDeadbandComparison = changedSamplesInsideDeadband = crossingsBetweenSenses = 0;
        maximumCrossingsBetweenSenses = 0; senseIntervalSum = 0d;
        Array.Clear(crossingHistogram, 0, crossingHistogram.Length); Array.Clear(trendSenseCounts, 0, trendSenseCounts.Length);
        Array.Clear(trendTumbles, 0, trendTumbles.Length); Array.Clear(trendTumbleProbability, 0, trendTumbleProbability.Length);
        Array.Clear(trackedTaxis, 0, trackedTaxis.Length);
        detailedTaxisWasEnabled = false; detailedTaxisSelectedId = 0; detailedTaxisLimit = 0; nextDetailedTaxisLogTime = 0f;
    }
}
