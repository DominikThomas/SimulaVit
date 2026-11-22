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
    private static readonly ProfilerMarker SampleMarker = new ProfilerMarker("SimulationPerformanceAnalyzer.Sample");

    [Header("Sampling (real time)")]
    [SerializeField, Min(1f)] private float summaryIntervalSeconds = 5f;
    [SerializeField, Min(128)] private int maximumFramesPerWindow = 32768;
    [SerializeField] private ReplicatorManager replicatorManager;
    [SerializeField] private GeodesicOceanResourceField resources;
    [SerializeField] private GeodesicAbioticChemistry chemistry;
    [Header("Output")]
    [SerializeField] private bool logSummaryToConsole = true;
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
    private readonly double[] aggregates = new double[10];
    private readonly StringBuilder csvBuffer = new StringBuilder(16384);
    private readonly StringBuilder summary = new StringBuilder(2048);
    private FrameContext pending;
    private bool hasPending, hasClock;
    private int previousSteps, frames, cpuSamples, gcSamples, csvSamplesPending;
    private double previousSimulationTime, previousBoundary, windowSeconds, simulatedSeconds;
    private double nextReferenceRefresh;
    private long previousCandidates, previousProcessed, previousChemistryTicks;
    private double populationSum, speedSum, gcBytes;
    private long steps, candidates, processed, chemistryTicks, activeNodesSum;
    private GeodesicBiologyRuntime.MovementWork previousMovement, movement;
    private int endFrame;
    private bool csvFailed;

    public string CsvOutputPath => csvOutputPath;
    public string LatestSummary => latestSummary;

    private void OnEnable()
    {
#if !ENABLE_PROFILER
        Debug.LogWarning("[SimulationPerformanceAnalyzer] CPU markers require the Unity Editor or a Development Build. Analyzer disabled.", this);
        enabled = false;
#else
        ResolveReferences();
        int capacity = Mathf.Max(128, maximumFramesPerWindow);
        renderedFrames = new double[capacity]; cpuFrames = new double[capacity];
        markers = new MarkerRecorder[MarkerNames.Length];
        markerTotals = new double[markers.Length]; frameMarkers = new double[markers.Length];
        for (int i = 0; i < markers.Length; i++)
        {
            // Register custom markers even before their owning runtime initializes.
            if (MarkerNames[i] != "JobHandle.Complete") _ = new ProfilerMarker(MarkerNames[i]);
            markers[i] = new MarkerRecorder(MarkerNames[i]);
        }
        cpuFrameRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1,
            ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        previousBoundary = Time.realtimeSinceStartupAsDouble;
        hasPending = hasClock = csvFailed = false;
        csvOutputPath = null;
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
                populationSum += pending.Population; speedSum += pending.Speed;
                steps += pending.Steps; simulatedSeconds += pending.SimulatedSeconds;
                activeNodesSum += pending.ActiveNodes;
                candidates += pending.Candidates; processed += pending.Processed; chemistryTicks += pending.ChemistryTicks;
                movement.AddSince(pending.Movement, default);
                if (cpuFrameRecorder.Valid && cpuFrameRecorder.Count > 0)
                    cpuFrames[cpuSamples++] = cpuFrameRecorder.LastValue * 1e-6d;
                if (gcRecorder.Valid && gcRecorder.Count > 0) { gcBytes += gcRecorder.LastValue; gcSamples++; }
                for (int i = 0; i < markers.Length; i++)
                {
                    frameMarkers[i] = markers[i].ReadMilliseconds();
                    markerTotals[i] += frameMarkers[i];
                }
                AccumulateAggregates(frameMarkers, aggregates);
                frames++;
                if (windowSeconds >= Math.Max(1d, summaryIntervalSeconds) || frames == renderedFrames.Length)
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
            var movementDelta = default(GeodesicBiologyRuntime.MovementWork);
            if (hasClock && currentSteps >= previousSteps) movementDelta.AddSince(currentMovement, previousMovement);
            pending = new FrameContext
            {
                Frame = Time.frameCount,
                Population = replicatorManager != null ? replicatorManager.TotalPopulation : 0,
                Speed = replicatorManager != null ? replicatorManager.SimulationSpeedMultiplier : 0f,
                Steps = hasClock ? Math.Max(0, currentSteps - previousSteps) : 0,
                SimulatedSeconds = hasClock ? Math.Max(0d, currentTime - previousSimulationTime) : 0d,
                ActiveNodes = resources != null && resources.IsInitialized ? resources.ActiveNodeCount : 0,
                Candidates = hasClock ? Math.Max(0L, currentCandidates - previousCandidates) : 0L,
                Processed = hasClock ? Math.Max(0L, currentProcessed - previousProcessed) : 0L,
                ChemistryTicks = hasClock ? Math.Max(0L, currentTicks - previousChemistryTicks) : 0L,
                Movement = movementDelta
            };
            previousSteps = currentSteps; previousSimulationTime = currentTime;
            previousCandidates = currentCandidates; previousProcessed = currentProcessed; previousChemistryTicks = currentTicks;
            previousMovement = currentMovement;
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
        nextReferenceRefresh = Time.realtimeSinceStartupAsDouble + Math.Max(1d, summaryIntervalSeconds);
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
        double p95 = Percentile95(renderedFrames, frames);
        double cpuTotal = 0d;
        for (int i = 0; i < cpuSamples; i++) cpuTotal += cpuFrames[i];
        double cpuP95 = Percentile95(cpuFrames, cpuSamples);
        summary.Clear().Append("[SimulationPerformanceAnalyzer] frames=").Append(frames);
        summary.Append(" real_s="); AppendNumber(summary, windowSeconds);
        summary.Append(" FPS="); AppendNumber(summary, frames / windowSeconds);
        summary.Append(" frame_avg/p95_ms="); AppendNumber(summary, averageFrame); summary.Append('/'); AppendNumber(summary, p95);
        summary.Append(" CPU_MainThread_avg/p95_ms="); AppendOptional(summary, cpuTotal, cpuSamples); summary.Append('/'); AppendOptional(summary, cpuP95, cpuSamples > 0 ? 1 : 0);
        summary.Append(" population_avg="); AppendNumber(summary, populationSum / frames);
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
        if (capacityReached) summary.Append(" window_capacity_reached=true");
        latestSummary = summary.ToString();
        if (logSummaryToConsole) Debug.Log(latestSummary, this);
        if (writeCsv && !csvFailed)
        {
            if (string.IsNullOrEmpty(csvOutputPath)) InitializeCsv();
            csvBuffer.Append(endFrame).Append(',').Append(frames).Append(',');
            AppendCsvNumber(windowSeconds); AppendCsvNumber(frames / windowSeconds);
            AppendCsvNumber(averageFrame); AppendCsvNumber(p95);
            AppendCsvOptional(cpuTotal, cpuSamples); AppendCsvOptional(cpuP95, cpuSamples > 0 ? 1 : 0);
            csvBuffer.Append(cpuSamples).Append(','); AppendCsvOptional(gcBytes, gcSamples); csvBuffer.Append(gcSamples).Append(',');
            AppendCsvNumber(populationSum / frames); AppendCsvNumber(speedSum / frames);
            csvBuffer.Append(steps).Append(','); AppendCsvNumber((double)steps / frames);
            AppendCsvNumber(simulatedSeconds); AppendCsvNumber(simulatedSeconds / windowSeconds);
            AppendCsvNumber((double)activeNodesSum / frames);
            csvBuffer.Append(candidates).Append(',').Append(processed).Append(',').Append(chemistryTicks).Append(',').Append(capacityReached ? 1 : 0);
            for (int i = 0; i < aggregates.Length; i++) { csvBuffer.Append(','); AppendNumber(csvBuffer, aggregates[i] / frames); }
            for (int i = 0; i < markers.Length; i++)
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
            csvBuffer.AppendLine();
            if (++csvSamplesPending >= Math.Max(1, flushCsvEveryNSamples)) FlushCsvBuffer();
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
        windowSeconds = simulatedSeconds = populationSum = speedSum = gcBytes = 0d;
        steps = candidates = processed = chemistryTicks = activeNodesSum = 0;
        movement = default;
        Array.Clear(markerTotals, 0, markerTotals.Length); Array.Clear(aggregates, 0, aggregates.Length);
    }

    private void InitializeCsv()
    {
        try
        {
            string prefix = string.IsNullOrWhiteSpace(csvFileNamePrefix) ? "simulation_performance" : Path.GetFileName(csvFileNamePrefix.Trim());
            foreach (char invalid in Path.GetInvalidFileNameChars()) prefix = prefix.Replace(invalid, '_');
            string suffix = appendTimestampToCsvFileName ? "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) : string.Empty;
            csvOutputPath = Path.Combine(Application.persistentDataPath, prefix + suffix + ".csv");
            csvBuffer.Clear(); csvSamplesPending = 0;
            csvBuffer.Append("end_frame,frames,real_seconds,rendered_fps,frame_avg_ms,frame_p95_ms,cpu_main_thread_avg_ms,cpu_main_thread_p95_ms,cpu_samples,gc_bytes_per_frame,gc_samples,population_avg,requested_speed_avg,steps_executed,steps_per_frame,simulated_seconds,simulated_seconds_per_real_second,active_chemistry_nodes_avg,chemistry_candidates_processed,chemistry_nodes_processed,chemistry_ticks,window_capacity_reached");
            foreach (string name in AggregateNames) csvBuffer.Append(',').Append(name).Append("_avg_ms");
            foreach (string name in MarkerNames) csvBuffer.Append(',').Append(name).Append("_avg_ms");
            csvBuffer.Append(",passive_avg,amoeboid_avg,flagellar_avg,anchored_avg,active_us_per_organism_step,active_organism_steps,active_integration_steps,active_senses,active_habitat_observations,active_boundary_searches,active_neighbor_evaluations,active_cell_crossings,active_vertical_trials");
            bool existing = File.Exists(csvOutputPath) && new FileInfo(csvOutputPath).Length > 0;
            if (existing)
            {
                using var reader = new StreamReader(csvOutputPath);
                if (reader.ReadLine() != csvBuffer.ToString()) throw new IOException("Existing CSV has a different schema. Choose a new file prefix or enable timestamps.");
                csvBuffer.Clear();
            }
            else { csvBuffer.AppendLine(); FlushCsvBuffer(); }
            Debug.Log($"[SimulationPerformanceAnalyzer] CSV: {csvOutputPath}", this);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        { FailCsv(ex); }
    }

    private void FlushCsvBuffer()
    {
        if (csvFailed || string.IsNullOrEmpty(csvOutputPath) || csvBuffer.Length == 0) return;
        try { File.AppendAllText(csvOutputPath, csvBuffer.ToString()); csvBuffer.Clear(); csvSamplesPending = 0; }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { FailCsv(ex); }
    }
    private void FailCsv(Exception ex)
    {
        csvFailed = true; csvBuffer.Clear();
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
    private static void AppendNumber(StringBuilder builder, double value) => builder.Append(value.ToString("F4", CultureInfo.InvariantCulture));
    private static void AppendOptional(StringBuilder builder, double total, long count)
    { if (count > 0) AppendNumber(builder, total / count); else builder.Append("n/a"); }
    private void AppendCsvNumber(double value) { AppendNumber(csvBuffer, value); csvBuffer.Append(','); }
    private void AppendCsvOptional(double total, long count) { AppendOptional(csvBuffer, total, count); csvBuffer.Append(','); }

    private struct FrameContext
    {
        public int Frame, Population, Steps, ActiveNodes;
        public float Speed;
        public double SimulatedSeconds;
        public long Candidates, Processed, ChemistryTicks;
        public GeodesicBiologyRuntime.MovementWork Movement;
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
        public void Reset()
        {
            if (!Valid) return;
            recorder.Reset(); // Unity Reset also stops collection.
            recorder.Start();
        }
        public void Dispose() => recorder.Dispose();
    }
}
