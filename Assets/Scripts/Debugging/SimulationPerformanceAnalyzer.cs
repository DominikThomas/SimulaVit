using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

/// <summary>Observes completed rendered frames; never participates in simulation stepping.</summary>
[DefaultExecutionOrder(-32000)]
public sealed class SimulationPerformanceAnalyzer : MonoBehaviour
{
    internal const float MinimumSummaryIntervalSeconds = 1f;
    internal const float DefaultSummaryIntervalSeconds = 5f;

    // Parents and children remain separate detail columns. Aggregate only disjoint roots.
    private static readonly string[] MarkerNames =
    {
        "ReplicatorManager.Simulation", "ReplicatorManager.VisualSync", "ReplicatorManager.Telemetry",
        "GeodesicOceanResource.Update", "GeodesicTemperature.Update",
        "GeodesicBiology.ReactionEvaluation", "GeodesicBiology.CompetitionResolution",
        "GeodesicBiology.LifecycleReproduction", "GeodesicBiology.Movement", "GeodesicBiology.ActiveMovement",
        "GeodesicBiology.ActiveMovement.Sensing", "GeodesicOceanResource.Chemistry.Total",
        "GeodesicOceanResource.Transport", "GeodesicOceanResource.HorizontalMixing", "GeodesicOceanResource.VerticalMixing",
        "GeodesicOceanResource.Chemistry.CandidateRefresh", "GeodesicOceanResource.Chemistry.Scan",
        "GeodesicOceanResource.Chemistry.VisualMemoryDecay", "GeodesicOceanResource.RecurringDiagnostics",
        "GeodesicTemperature.Diagnostics", "GeodesicBiology.Telemetry", "ReplicatorManager.GeodesicDiagnostics",
        "GeodesicChemistryTelemetry.Update", "ReplicatorManager.Hud", "SimulationPerformanceAnalyzer.Sample",
        "GeodesicOceanFe2Visual.Update", "GeodesicOceanSedimentVisual.Update", "GeodesicBiology.FounderLifecycle",
        "GeodesicBiology.ReactionEvaluation.HabitatSampling", "GeodesicBiology.ReactionEvaluation.RequestBuild",
        "GeodesicBiology.ReactionEvaluation.DemandAggregation", "GeodesicBiology.ReactionEvaluation.AvailabilityResolution",
        "GeodesicBiology.ReactionEvaluation.ReactionCommit", "GeodesicBiology.ReactionEvaluation.AgentEnergyAndStores",
        "GeodesicBiology.PassiveMovement.WanderRefresh", "GeodesicBiology.PassiveMovement.KinematicsAndBoundary",
        "GeodesicBiology.PassiveMovement.VerticalEvents", "GeodesicBiology.PassiveMovement.VisualTarget",
        "ReplicatorSteeringSystem.HotLoop", "ReplicatorMetabolismSystem.HotLoop",
        "ReplicatorManager.PopulationStatePrepareForLocomotion", "ReplicatorMovementSystem.SyncFromPopulationState",
        "ReplicatorMovementSystem.CopyToCompanionObjects", "JobHandle.Complete",
        "GeodesicOceanResource.AbioticChemistry", "ReplicatorRenderSystem.RenderAgents",
        "GeodesicOceanTemperature.DiagnosticSnapshot", "VentVisualizer.LateUpdate",
        "ReplicatorManager.GeodesicControlSync",
        "GeodesicOceanResource.StagingClear", "GeodesicOceanResource.TransportCoefficients",
        "GeodesicOceanResource.Suspended.Total", "GeodesicOceanResource.Suspended.Mixing",
        "GeodesicOceanResource.Suspended.Clear", "GeodesicOceanResource.Suspended.CollectOutgoing",
        "GeodesicOceanResource.Suspended.StageIncoming", "GeodesicOceanResource.Suspended.Commit",
        "GeodesicOceanResource.Suspended.Settling", "GeodesicTemperature.TargetAndSurfaceResponse",
        "GeodesicTemperature.HorizontalDiffusion", "GeodesicTemperature.CommittedEvent",
        "GeodesicOceanTemperature.RelaxApproximateProfiles", "GeodesicOceanTemperature.SolveImplicitColumns",
        "GeodesicTemperature.ResponseCoefficients", "GeodesicBiology.ActiveMovement.Vertical"
    };
    private static readonly string[] AggregateNames =
    {
        "simulation_total", "biology_reactions_including_competition", "biology_lifecycle_and_founders",
        "passive_movement_including_visual_targets", "active_movement_including_sensing", "abiotic_chemistry",
        "ocean_transport_and_environment", "rendering_visual_sync", "telemetry_debugging", "measured_total"
    };
    private static readonly string[] MetabolismAverageColumns =
    {
        "hydrogenotrophy_avg", "sulfur_chemosynthesis_avg", "fermentation_avg", "photosynthesis_avg",
        "saprotrophy_avg", "methanogenesis_avg", "methanotrophy_avg", "predation_avg"
    };
    private static readonly ProfilerMarker SampleMarker = new ProfilerMarker("SimulationPerformanceAnalyzer.Sample");

    [Header("Sampling (real time)")]
    [SerializeField, Min(MinimumSummaryIntervalSeconds)] private float summaryIntervalSeconds = DefaultSummaryIntervalSeconds;
    [SerializeField, Min(128)] private int maximumFramesPerWindow = 32768;
    [SerializeField] private ReplicatorManager replicatorManager;
    [SerializeField] private GeodesicOceanResourceField resources;
    [SerializeField] private GeodesicAbioticChemistry chemistry;
    [Header("Output")]
    [SerializeField] private bool logSummaryToConsole;
    [SerializeField] private bool writeCsv;
    [SerializeField, Min(1)] private int flushCsvEveryNSamples = 1;
    [SerializeField] private string csvFileNamePrefix = "simulation_performance";
    [SerializeField] private bool appendTimestampToCsvFileName = true;
    [Header("Latest completed window")]
    [SerializeField, TextArea(3, 12)] private string latestSummary;
    [SerializeField] private string csvOutputPath;

    private MarkerRecorder[] markers;
    private ProfilerRecorder cpuFrameRecorder, gcRecorder;
    private double[] renderedFrames, cpuFrames, markerTotals, frameMarkers;
    private long[] detailedMarkerSamples;
    private readonly double[] aggregates = new double[10];
    private StringBuilder csvBuffer;
    private readonly StringBuilder summary = new StringBuilder(2048);
    private FrameContext pending;
    private bool hasPending, hasClock;
    private int previousSteps, frames, cpuSamples, gcSamples, csvSamplesPending;
    private double previousSimulationTime, previousBoundary, windowSeconds, simulatedSeconds;
    private double nextReferenceRefresh;
    private long previousCandidates, previousProcessed, previousChemistryTicks;
    private ReplicatorManager.MetabolismPopulationCounts populationSum;
    private double speedSum, gcBytes, endSimulatedSeconds;
    private long steps, candidates, processed, chemistryTicks, activeNodesSum;
    private GeodesicBiologyRuntime.MovementWork previousMovement, movement;
    private GeodesicBiologyRuntime.PerformanceWork previousBiologyWork, biologyWork;
    private int endFrame;
    private bool csvFailed;
    private bool useNewCsvFile;
    private bool detailedGeodesicProfiling;

    public string CsvOutputPath => csvOutputPath;
    public string LatestSummary => latestSummary;
    internal string ActiveCsvOutputPath => isActiveAndEnabled && !csvFailed ? csvOutputPath : null;
    internal bool DetailedGeodesicProfilingActive => detailedGeodesicProfiling && isActiveAndEnabled;

    internal static float NormalizeSummaryInterval(float seconds) =>
        float.IsFinite(seconds) ? Mathf.Max(MinimumSummaryIntervalSeconds, seconds) : DefaultSummaryIntervalSeconds;

    internal void BeginCapture(bool writeCsv, bool logSummaryToConsole, float intervalSeconds, bool appendTimestamp,
        bool detailedGeodesicProfiling = false)
    {
        enabled = false;
        this.writeCsv = writeCsv;
        this.logSummaryToConsole = logSummaryToConsole;
        summaryIntervalSeconds = NormalizeSummaryInterval(intervalSeconds);
        appendTimestampToCsvFileName = appendTimestamp;
        this.detailedGeodesicProfiling = detailedGeodesicProfiling;
        useNewCsvFile = true;
        enabled = true;
    }

    private void OnEnable()
    {
        latestSummary = null;
        csvOutputPath = null;
#if !ENABLE_PROFILER
        Debug.LogWarning("[SimulationPerformanceAnalyzer] CPU markers require the Unity Editor or a Development Build. Analyzer disabled.", this);
        enabled = false;
#else
        ResolveReferences();
        int capacity = Mathf.Max(128, maximumFramesPerWindow);
        renderedFrames = new double[capacity]; cpuFrames = new double[capacity];
        int detailCount = detailedGeodesicProfiling ? GeodesicPerformanceMarkers.RecordedStages.Length : 0;
        markers = new MarkerRecorder[MarkerNames.Length + detailCount];
        markerTotals = new double[markers.Length]; frameMarkers = new double[MarkerNames.Length];
        detailedMarkerSamples = detailedGeodesicProfiling ? new long[detailCount] : null;
        for (int i = 0; i < MarkerNames.Length; i++)
        {
            // Register custom markers even before their owning runtime initializes.
            if (MarkerNames[i] != "JobHandle.Complete") _ = new ProfilerMarker(MarkerNames[i]);
            markers[i] = new MarkerRecorder(MarkerNames[i]);
        }
        for (int i = 0; i < detailCount; i++)
            markers[MarkerNames.Length + i] = new MarkerRecorder(GeodesicPerformanceMarkers.RecordedStages[i].Name);
        cpuFrameRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1,
            ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        previousBoundary = Time.realtimeSinceStartupAsDouble;
        hasPending = hasClock = csvFailed = false;
        ClearWindow();
        if (writeCsv) InitializeCsv();
#endif
    }

    private void Update()
    {
        using (SampleMarker.Auto())
        {
            double now = Time.realtimeSinceStartupAsDouble;
            // Frame-end recorder values belong to the previous LateUpdate's state.
            if (hasPending && pending.Frame == Time.frameCount - 1)
            {
                double elapsed = Math.Max(0d, now - previousBoundary);
                renderedFrames[frames] = elapsed * 1000d;
                windowSeconds += elapsed;
                endFrame = pending.Frame;
                endSimulatedSeconds = pending.AbsoluteSimulatedSeconds;
                populationSum.Add(pending.Population);
                speedSum += pending.Speed;
                steps += pending.Steps; simulatedSeconds += pending.SimulatedSeconds;
                activeNodesSum += pending.ActiveNodes;
                candidates += pending.Candidates; processed += pending.Processed; chemistryTicks += pending.ChemistryTicks;
                movement.AddSince(pending.Movement, default);
                biologyWork.AddSince(pending.BiologyWork, default);
                if (cpuFrameRecorder.Valid && cpuFrameRecorder.Count > 0)
                    cpuFrames[cpuSamples++] = cpuFrameRecorder.LastValue * 1e-6d;
                if (gcRecorder.Valid && gcRecorder.Count > 0) { gcBytes += gcRecorder.LastValue; gcSamples++; }
                for (int i = 0; i < MarkerNames.Length; i++)
                {
                    frameMarkers[i] = markers[i].ReadMilliseconds();
                    markerTotals[i] += frameMarkers[i];
                }
                for (int i = MarkerNames.Length; i < markers.Length; i++)
                {
                    markerTotals[i] += markers[i].ReadMilliseconds(out long samples);
                    detailedMarkerSamples[i - MarkerNames.Length] += samples;
                }
                AccumulateAggregates(frameMarkers, aggregates);
                frames++;
                if (windowSeconds >= NormalizeSummaryInterval(summaryIntervalSeconds) || frames == renderedFrames.Length)
                {
                    PublishWindow(frames == renderedFrames.Length);
                    ClearWindow();
                }
            }
            // Clear completed samples before simulation Update so intermittent markers
            // cannot repeat their last active frame's value on an idle frame.
            for (int i = 0; i < markers.Length; i++) markers[i].Reset();
            previousBoundary = now;
            hasPending = false;
        }
    }

    private void LateUpdate()
    {
        using (SampleMarker.Auto())
        {
            if ((replicatorManager == null || resources == null || chemistry == null)
                && Time.realtimeSinceStartupAsDouble >= nextReferenceRefresh)
                ResolveReferences();
            int currentSteps = replicatorManager != null ? replicatorManager.SimulationStepCount : 0;
            double currentTime = replicatorManager != null ? replicatorManager.SimulationTimeSeconds : 0d;
            long currentCandidates = chemistry != null ? chemistry.TotalPerformanceCandidates : 0;
            long currentProcessed = chemistry != null ? chemistry.TotalPerformanceNodesProcessed : 0;
            long currentTicks = chemistry != null ? chemistry.ChemistryTicks : 0;
            var currentMovement = replicatorManager != null ? replicatorManager.GeodesicMovementPerformance : default;
            var currentBiologyWork = replicatorManager != null ? replicatorManager.GeodesicPerformanceWork : default;
            var movementDelta = default(GeodesicBiologyRuntime.MovementWork);
            var biologyDelta = default(GeodesicBiologyRuntime.PerformanceWork);
            if (hasClock && currentSteps >= previousSteps)
            {
                movementDelta.AddSince(currentMovement, previousMovement);
                biologyDelta.AddSince(currentBiologyWork, previousBiologyWork);
            }
            pending = new FrameContext
            {
                Frame = Time.frameCount,
                Population = replicatorManager != null ? replicatorManager.MetabolismCounts : default,
                Speed = replicatorManager != null ? replicatorManager.SimulationSpeedMultiplier : 0f,
                Steps = hasClock ? Math.Max(0, currentSteps - previousSteps) : 0,
                SimulatedSeconds = hasClock ? Math.Max(0d, currentTime - previousSimulationTime) : 0d,
                AbsoluteSimulatedSeconds = currentTime,
                ActiveNodes = resources != null && resources.IsInitialized ? resources.ActiveNodeCount : 0,
                Candidates = hasClock ? Math.Max(0L, currentCandidates - previousCandidates) : 0L,
                Processed = hasClock ? Math.Max(0L, currentProcessed - previousProcessed) : 0L,
                ChemistryTicks = hasClock ? Math.Max(0L, currentTicks - previousChemistryTicks) : 0L,
                Movement = movementDelta,
                BiologyWork = biologyDelta
            };
            previousSteps = currentSteps; previousSimulationTime = currentTime;
            previousCandidates = currentCandidates; previousProcessed = currentProcessed; previousChemistryTicks = currentTicks;
            previousMovement = currentMovement;
            previousBiologyWork = currentBiologyWork;
            hasPending = hasClock; // Discard the first partial frame after enabling.
            hasClock = true;
        }
    }

    private void ResolveReferences()
    {
        ReplicatorManager oldManager = replicatorManager;
        GeodesicAbioticChemistry oldChemistry = chemistry;
        if (replicatorManager == null) replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        if (resources == null) resources = FindFirstObjectByType<GeodesicOceanResourceField>();
        if (chemistry == null) chemistry = FindFirstObjectByType<GeodesicAbioticChemistry>();
        if (!ReferenceEquals(oldManager, replicatorManager) || !ReferenceEquals(oldChemistry, chemistry)) hasClock = false;
        nextReferenceRefresh = Time.realtimeSinceStartupAsDouble + NormalizeSummaryInterval(summaryIntervalSeconds);
    }

    /// <summary>Input order is MarkerNames. Inclusive details never get blindly summed.</summary>
    public static void AccumulateAggregates(double[] m, double[] totals)
    {
        double internalTelemetry = m[18] + m[19] + m[20] + m[21] + m[46];
        double outsideTelemetry = m[2] + m[22] + m[23] + m[24];
        double simulation = m[0] + m[3] + m[4];
        double rendering = m[1] + m[25] + m[26] + m[47];
        totals[0] += simulation;
        totals[1] += m[5]; // Includes competition and commits.
        totals[2] += m[7] + m[27];
        totals[3] += Math.Max(0d, m[8] - m[9]);
        totals[4] += m[9]; // Includes sensing.
        totals[5] += m[11];
        totals[6] += Math.Max(0d, m[3] + m[4] - m[11] - m[18] - m[19] - m[46]);
        totals[7] += rendering;
        totals[8] += internalTelemetry + outsideTelemetry;
        totals[9] += simulation + rendering + outsideTelemetry;
    }

    private void PublishWindow(bool capacityReached)
    {
        double averageFrame = windowSeconds * 1000d / frames;
        double averagePopulation = (double)populationSum.Total / frames;
        double p95 = Percentile95(renderedFrames, frames);
        double cpuTotal = 0d;
        for (int i = 0; i < cpuSamples; i++) cpuTotal += cpuFrames[i];
        double cpuP95 = Percentile95(cpuFrames, cpuSamples);
        var thermalMode = replicatorManager != null ? replicatorManager.GeodesicLifecycleThermalMode : null;
        string thermalModeName = thermalMode?.ToString() ?? "n/a";
        double thermalSetupMilliseconds = thermalMode.HasValue
            ? replicatorManager.GeodesicLifecycleThermalLookupSetupMilliseconds : 0d;
        var thermalWork = biologyWork.LifecycleThermal;
        summary.Clear().Append("[SimulationPerformanceAnalyzer] frames=").Append(frames);
        summary.Append(" real_s="); AppendNumber(summary, windowSeconds);
        summary.Append(" FPS="); AppendNumber(summary, frames / windowSeconds);
        summary.Append(" frame_avg/p95_ms="); AppendNumber(summary, averageFrame); summary.Append('/'); AppendNumber(summary, p95);
        summary.Append(" CPU_MainThread_avg/p95_ms="); AppendOptional(summary, cpuTotal, cpuSamples); summary.Append('/'); AppendOptional(summary, cpuP95, cpuSamples > 0 ? 1 : 0);
        summary.Append(" end_simulated_seconds="); AppendNumber(summary, endSimulatedSeconds, "R");
        summary.Append(" population_avg="); AppendNumber(summary, averagePopulation, "R");
        AppendMetabolismAverages(summary, includeNames: true);
        summary.Append(" requested_speed_avg="); AppendNumber(summary, speedSum / frames);
        summary.Append(" steps=").Append(steps).Append(" simulated_s_per_real_s="); AppendNumber(summary, simulatedSeconds / windowSeconds);
        summary.Append(" GC_B_per_frame="); AppendOptional(summary, gcBytes, gcSamples);
        summary.Append(" active_nodes_avg="); AppendNumber(summary, (double)activeNodesSum / frames);
        summary.Append(" chemistry_candidates/processed/ticks=").Append(candidates).Append('/').Append(processed).Append('/').Append(chemistryTicks);
        summary.Append(" locomotion_P/A/F/X_avg=");
        AppendOptional(summary, movement.PassiveSteps, steps); summary.Append('/');
        AppendOptional(summary, movement.AmoeboidSteps, steps); summary.Append('/');
        AppendOptional(summary, movement.FlagellarSteps, steps); summary.Append('/');
        AppendOptional(summary, movement.AnchoredSteps, steps);
        summary.Append(" active_us_per_organism_step="); AppendOptional(summary, aggregates[4] * 1000d, movement.ActiveSteps);
        summary.Append(" active_integrations/senses/crossings=").Append(movement.IntegrationSteps)
            .Append('/').Append(movement.Senses).Append('/').Append(movement.CellCrossings);
        for (int i = 0; i < aggregates.Length; i++) { summary.Append(' ').Append(AggregateNames[i]).Append("_ms="); AppendNumber(summary, aggregates[i] / frames); }
        summary.Append(" simulation_cpu_ms_per_step="); AppendOptional(summary, aggregates[0], steps);
        summary.Append(" lifecycle_births/deaths=").Append(biologyWork.Births).Append('/').Append(biologyWork.Deaths);
        summary.Append(" lifecycle_thermal_sampling_mode=").Append(thermalModeName);
        if (thermalMode.HasValue)
        {
            summary.Append(" lifecycle_thermal_lookup_setup_ms=");
            AppendNumber(summary, thermalSetupMilliseconds);
        }
        if (detailedGeodesicProfiling)
        {
            summary.Append(" detailed_slot_stride=").Append(GeodesicPerformanceMarkers.SlotSampleStride);
            summary.Append(" prey_searches/candidates/attempts/captures=").Append(biologyWork.PreySearches)
                .Append('/').Append(biologyWork.PreyCandidates).Append('/').Append(biologyWork.CaptureAttempts)
                .Append('/').Append(biologyWork.PreyCaptures);
            summary.Append(" thermal_sampled_hits/misses/key_invalidations/candidates/evaluations=")
                .Append(thermalWork.CacheHits).Append('/').Append(thermalWork.CacheMisses).Append('/')
                .Append(thermalWork.CacheKeyInvalidations).Append('/').Append(thermalWork.Candidates)
                .Append('/').Append(thermalWork.InfluenceEvaluations);
            AppendDetailedTimings(summary, csv: false);
        }
        if (capacityReached) summary.Append(" window_capacity_reached=true");
        latestSummary = summary.ToString();
        if (logSummaryToConsole) Debug.Log(latestSummary, this);
        if (writeCsv && !csvFailed)
        {
            if (string.IsNullOrEmpty(csvOutputPath)) InitializeCsv();
            if (csvFailed) return;
            csvBuffer.Append(endFrame).Append(',').Append(frames).Append(',');
            AppendCsvNumber(windowSeconds); AppendCsvNumber(frames / windowSeconds);
            AppendCsvNumber(averageFrame); AppendCsvNumber(p95);
            AppendCsvOptional(cpuTotal, cpuSamples); AppendCsvOptional(cpuP95, cpuSamples > 0 ? 1 : 0);
            csvBuffer.Append(cpuSamples).Append(','); AppendCsvOptional(gcBytes, gcSamples); csvBuffer.Append(gcSamples).Append(',');
            AppendCsvNumber(averagePopulation, "R");
            AppendCsvNumber(speedSum / frames);
            csvBuffer.Append(steps).Append(','); AppendCsvNumber((double)steps / frames);
            AppendCsvNumber(simulatedSeconds); AppendCsvNumber(simulatedSeconds / windowSeconds);
            AppendCsvNumber((double)activeNodesSum / frames);
            csvBuffer.Append(candidates).Append(',').Append(processed).Append(',').Append(chemistryTicks).Append(',').Append(capacityReached ? 1 : 0);
            for (int i = 0; i < aggregates.Length; i++) { csvBuffer.Append(','); AppendNumber(csvBuffer, aggregates[i] / frames); }
            for (int i = 0; i < MarkerNames.Length; i++)
            {
                csvBuffer.Append(',');
                if (markers[i].Valid) AppendNumber(csvBuffer, markerTotals[i] / frames); else csvBuffer.Append("n/a");
            }
            csvBuffer.Append(',');
            AppendCsvOptional(movement.PassiveSteps, steps);
            AppendCsvOptional(movement.AmoeboidSteps, steps);
            AppendCsvOptional(movement.FlagellarSteps, steps);
            AppendCsvOptional(movement.AnchoredSteps, steps);
            AppendCsvOptional(aggregates[4] * 1000d, movement.ActiveSteps);
            csvBuffer.Append(movement.ActiveSteps).Append(',').Append(movement.IntegrationSteps).Append(',')
                .Append(movement.Senses).Append(',').Append(movement.HabitatObservations).Append(',')
                .Append(movement.BoundarySearches).Append(',').Append(movement.NeighborEvaluations).Append(',')
                .Append(movement.CellCrossings).Append(',').Append(movement.VerticalTrials);
            csvBuffer.Append(',');
            AppendNumber(csvBuffer, endSimulatedSeconds, "R");
            AppendMetabolismAverages(csvBuffer, includeNames: false);
            csvBuffer.Append(','); AppendCsvOptional(aggregates[0], steps);
            csvBuffer.Append(biologyWork.Births).Append(',').Append(biologyWork.Deaths).Append(',');
            csvBuffer.Append(detailedGeodesicProfiling ? GeodesicPerformanceMarkers.SlotSampleStride : 0);
            if (detailedGeodesicProfiling)
                csvBuffer.Append(',').Append(biologyWork.PreySearches).Append(',').Append(biologyWork.PreyCandidates)
                    .Append(',').Append(biologyWork.CaptureAttempts).Append(',').Append(biologyWork.PreyCaptures);
            else
                csvBuffer.Append(",n/a,n/a,n/a,n/a");
            AppendDetailedTimings(csvBuffer, csv: true);
            csvBuffer.Append(',').Append(thermalModeName).Append(',');
            if (thermalMode.HasValue) AppendNumber(csvBuffer, thermalSetupMilliseconds); else csvBuffer.Append("n/a");
            if (detailedGeodesicProfiling)
                csvBuffer.Append(',').Append(thermalWork.CacheHits).Append(',').Append(thermalWork.CacheMisses)
                    .Append(',').Append(thermalWork.CacheKeyInvalidations).Append(',').Append(thermalWork.Candidates)
                    .Append(',').Append(thermalWork.InfluenceEvaluations);
            else
                csvBuffer.Append(",n/a,n/a,n/a,n/a,n/a");
            csvBuffer.AppendLine();
            if (++csvSamplesPending >= Math.Max(1, flushCsvEveryNSamples)) FlushCsvBuffer();
        }
    }

    private static string DetailTimingSuffix(GeodesicPerformanceMarkers.Stage stage) =>
        stage.Sampled ? "_estimated_avg_ms" : "_avg_ms";

    private void AppendDetailedTimings(StringBuilder output, bool csv)
    {
        var stages = GeodesicPerformanceMarkers.RecordedStages;
        for (int i = 0; i < stages.Length; i++)
        {
            var stage = stages[i];
            int recorderIndex = MarkerNames.Length + i;
            bool available = detailedGeodesicProfiling && markers[recorderIndex].Valid;
            long samples = available ? detailedMarkerSamples[i] : 0;
            if (csv)
                output.Append(',');
            else
                output.Append(' ').Append(stage.Name).Append(DetailTimingSuffix(stage)).Append('=');

            if (available && (!stage.Sampled || samples > 0))
            {
                int scale = stage.Sampled ? GeodesicPerformanceMarkers.SlotSampleStride : 1;
                AppendNumber(output, markerTotals[recorderIndex] * scale / frames);
            }
            else output.Append("n/a");
            output.Append(csv ? ',' : '/');
            if (available) output.Append(samples); else output.Append("n/a");
        }
    }

    private void AppendMetabolismAverages(StringBuilder builder, bool includeNames)
    {
        ReadOnlySpan<long> counts = stackalloc long[]
        {
            populationSum.Hydrogenotrophy, populationSum.SulfurChemosynthesis, populationSum.Fermentation,
            populationSum.Photosynthesis, populationSum.Saprotrophy, populationSum.Methanogenesis,
            populationSum.Methanotrophy, populationSum.Predation
        };
        for (int i = 0; i < counts.Length; i++)
        {
            if (includeNames)
                builder.Append(' ').Append(MetabolismAverageColumns[i]).Append('=');
            else
                builder.Append(',');
            AppendNumber(builder, (double)counts[i] / frames, "R");
        }
    }

    public static double Percentile95(double[] values, int count)
    {
        if (count == 0) return 0d;
        Array.Sort(values, 0, count);
        return values[Math.Max(0, (int)Math.Ceiling(count * 0.95d) - 1)];
    }

    private void ClearWindow()
    {
        frames = cpuSamples = gcSamples = 0;
        windowSeconds = simulatedSeconds = speedSum = gcBytes = 0d;
        populationSum = default;
        steps = candidates = processed = chemistryTicks = activeNodesSum = 0;
        movement = default;
        biologyWork = default;
        Array.Clear(markerTotals, 0, markerTotals.Length); Array.Clear(aggregates, 0, aggregates.Length);
        if (detailedMarkerSamples != null) Array.Clear(detailedMarkerSamples, 0, detailedMarkerSamples.Length);
    }

    private void InitializeCsv()
    {
        try
        {
            string prefix = string.IsNullOrWhiteSpace(csvFileNamePrefix) ? "simulation_performance" : Path.GetFileName(csvFileNamePrefix.Trim());
            foreach (char invalid in Path.GetInvalidFileNameChars()) prefix = prefix.Replace(invalid, '_');
            string suffix = appendTimestampToCsvFileName ? "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) : string.Empty;
            string fileStem = prefix + suffix;
            csvBuffer ??= new StringBuilder(4096);
            csvBuffer.Clear(); csvSamplesPending = 0;
            csvBuffer.Append("end_frame,frames,real_seconds,rendered_fps,frame_avg_ms,frame_p95_ms,cpu_main_thread_avg_ms,cpu_main_thread_p95_ms,cpu_samples,gc_bytes_per_frame,gc_samples,population_avg,requested_speed_avg,steps_executed,steps_per_frame,simulated_seconds,simulated_seconds_per_real_second,active_chemistry_nodes_avg,chemistry_candidates_processed,chemistry_nodes_processed,chemistry_ticks,window_capacity_reached");
            foreach (string name in AggregateNames) csvBuffer.Append(',').Append(name).Append("_avg_ms");
            foreach (string name in MarkerNames) csvBuffer.Append(',').Append(name).Append("_avg_ms");
            csvBuffer.Append(",passive_avg,amoeboid_avg,flagellar_avg,anchored_avg,active_us_per_organism_step,active_organism_steps,active_integration_steps,active_senses,active_habitat_observations,active_boundary_searches,active_neighbor_evaluations,active_cell_crossings,active_vertical_trials");
            csvBuffer.Append(",end_simulated_seconds");
            foreach (string name in MetabolismAverageColumns) csvBuffer.Append(',').Append(name);
            csvBuffer.Append(",simulation_cpu_ms_per_step,lifecycle_births,lifecycle_deaths,detailed_slot_stride,predation_searches,predation_candidates_examined,predation_capture_attempts,predation_captures");
            foreach (var stage in GeodesicPerformanceMarkers.RecordedStages)
                csvBuffer.Append(',').Append(stage.Name).Append(DetailTimingSuffix(stage)).Append(',').Append(stage.Name).Append("_samples");
            csvBuffer.Append(",lifecycle_thermal_sampling_mode,lifecycle_thermal_lookup_setup_ms")
                .Append(",lifecycle_thermal_cache_hits_sampled,lifecycle_thermal_cache_misses_sampled,lifecycle_thermal_cache_key_invalidations_sampled")
                .Append(",lifecycle_thermal_candidates_sampled,lifecycle_thermal_influence_evaluations_sampled");
            if (useNewCsvFile)
            {
                csvBuffer.AppendLine();
                csvOutputPath = WriteNewCsvFile(Application.persistentDataPath, fileStem, csvBuffer.ToString());
                csvBuffer.Clear();
            }
            else
            {
                csvOutputPath = Path.Combine(Application.persistentDataPath, fileStem + ".csv");
                bool existing = File.Exists(csvOutputPath) && new FileInfo(csvOutputPath).Length > 0;
                if (existing)
                {
                    bool matchingHeader;
                    using (var reader = new StreamReader(csvOutputPath)) matchingHeader = reader.ReadLine() == csvBuffer.ToString();
                    if (!matchingHeader)
                        csvOutputPath = WriteNewCsvFile(Application.persistentDataPath, fileStem, csvBuffer.AppendLine().ToString());
                    csvBuffer.Clear();
                }
                else { csvBuffer.AppendLine(); FlushCsvBuffer(); }
            }
            if (!csvFailed) Debug.Log($"[SimulationPerformanceAnalyzer] CSV: {csvOutputPath}", this);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        { FailCsv(ex); }
    }

    private static string WriteNewCsvFile(string directory, string fileStem, string header)
    {
        for (int captureNumber = 1; ; captureNumber++)
        {
            string stem = captureNumber == 1 ? fileStem : fileStem + "_" + captureNumber.ToString(CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, stem + ".csv");
            FileStream file;
            try { file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); }
            catch (IOException) when (File.Exists(path)) { continue; }
            using (file)
            using (var writer = new StreamWriter(file))
                writer.Write(header);
            return path;
        }
    }

    private void FlushCsvBuffer()
    {
        if (csvFailed || string.IsNullOrEmpty(csvOutputPath) || csvBuffer == null || csvBuffer.Length == 0) return;
        try { File.AppendAllText(csvOutputPath, csvBuffer.ToString()); csvBuffer.Clear(); csvSamplesPending = 0; }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { FailCsv(ex); }
    }
    private void FailCsv(Exception ex)
    {
        csvFailed = true; csvBuffer?.Clear();
        Debug.LogWarning($"[SimulationPerformanceAnalyzer] CSV export stopped: {ex.Message}", this);
    }
    private void OnDisable()
    {
        // The current frame is incomplete. Finalize only frames already collected.
        if (frames > 0) PublishWindow(false);
        FlushCsvBuffer();
        if (markers != null) foreach (MarkerRecorder marker in markers) marker.Dispose();
        cpuFrameRecorder.Dispose(); gcRecorder.Dispose();
        hasPending = false;
    }
    private static void AppendNumber(StringBuilder builder, double value, string format = "F4")
    {
        Span<char> characters = stackalloc char[64];
        if (value.TryFormat(characters, out int written, format, CultureInfo.InvariantCulture))
            builder.Append(characters.Slice(0, written));
        else
            builder.Append(value.ToString(format, CultureInfo.InvariantCulture));
    }
    private static void AppendOptional(StringBuilder builder, double total, long count)
    { if (count > 0) AppendNumber(builder, total / count); else builder.Append("n/a"); }
    private void AppendCsvNumber(double value, string format = "F4") { AppendNumber(csvBuffer, value, format); csvBuffer.Append(','); }
    private void AppendCsvOptional(double total, long count) { AppendOptional(csvBuffer, total, count); csvBuffer.Append(','); }

    private struct FrameContext
    {
        public int Frame, Steps, ActiveNodes;
        public ReplicatorManager.MetabolismPopulationCounts Population;
        public float Speed;
        public double SimulatedSeconds, AbsoluteSimulatedSeconds;
        public long Candidates, Processed, ChemistryTicks;
        public GeodesicBiologyRuntime.MovementWork Movement;
        public GeodesicBiologyRuntime.PerformanceWork BiologyWork;
    }
    private readonly struct MarkerRecorder : IDisposable
    {
        private readonly ProfilerRecorder recorder;
        public bool Valid => recorder.Valid;
        public MarkerRecorder(string name)
        {
            recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, name, 1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        }
        public double ReadMilliseconds() => Valid && recorder.Count > 0 ? recorder.LastValue * 1e-6d : 0d;
        public double ReadMilliseconds(out long count)
        {
            count = 0;
            if (!Valid || recorder.Count == 0) return 0d;
            ProfilerRecorderSample sample = recorder.GetSample(0);
            count = sample.Count;
            return sample.Value * 1e-6d;
        }
        public void Reset()
        {
            if (!Valid) return;
            recorder.Reset(); // Unity Reset also stops collection.
            recorder.Start();
        }
        public void Dispose() => recorder.Dispose();
    }
}
