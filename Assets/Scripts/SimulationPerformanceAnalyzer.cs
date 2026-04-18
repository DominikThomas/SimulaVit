using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

public sealed class SimulationPerformanceAnalyzer : MonoBehaviour
{
    private const double NanosecondsToMilliseconds = 1e-6d;

    private static readonly string[] MarkerNames =
    {
        "ReplicatorSteeringSystem.HotLoop",
        "ReplicatorMetabolismSystem.HotLoop",
        "ReplicatorManager.PopulationStatePrepareForLocomotion",
        "ReplicatorMovementSystem.SyncFromPopulationState",
        "ReplicatorMovementSystem.CopyToCompanionObjects",
        "JobHandle.Complete"
    };

    [Header("Sampling")]
    [SerializeField, Min(1)] private int recordEveryNFrames = 30;
    [SerializeField] private ReplicatorManager replicatorManager;

    [Header("Output")]
    [SerializeField] private bool logSummaryToConsole = true;
    [SerializeField] private bool writeCsv = false;
    [SerializeField, Min(1)] private int flushCsvEveryNSamples = 10;
    [SerializeField] private string csvFileNamePrefix = "simulation_performance";
    [SerializeField] private bool appendTimestampToCsvFileName = true;

    [Header("Debug")]
    [SerializeField] private string csvOutputPath;

    private readonly List<MarkerRecorder> markerRecorders = new List<MarkerRecorder>(MarkerNames.Length);
    private readonly StringBuilder csvBuffer = new StringBuilder(2048);

    private string csvPath;
    private int csvSamplesPending;

    public string CsvOutputPath => csvOutputPath;

    private void OnEnable()
    {
        if (replicatorManager == null)
        {
            replicatorManager = FindFirstObjectByType<ReplicatorManager>();
        }

        InitializeRecorders();

        if (writeCsv)
        {
            InitializeCsv();
        }
    }

    private void OnDisable()
    {
        FlushCsvBuffer();

        for (int i = 0; i < markerRecorders.Count; i++)
        {
            markerRecorders[i].Dispose();
        }

        markerRecorders.Clear();
    }

    private void LateUpdate()
    {
        if (recordEveryNFrames <= 0 || (Time.frameCount % recordEveryNFrames) != 0)
        {
            return;
        }

        if (replicatorManager == null)
        {
            return;
        }

        int population = replicatorManager.TotalPopulation;
        int predators = replicatorManager.PredatorCount;
        int simulationStepsPerFrame = replicatorManager.SimulationStepsPerFrame;
        int frame = Time.frameCount;

        if (logSummaryToConsole)
        {
            Debug.Log(BuildSummary(frame, population, predators, simulationStepsPerFrame), this);
        }

        if (!writeCsv)
        {
            return;
        }

        AppendCsvLine(frame, population, predators, simulationStepsPerFrame);
        csvSamplesPending++;

        if (csvSamplesPending >= flushCsvEveryNSamples)
        {
            FlushCsvBuffer();
        }
    }

    private void InitializeRecorders()
    {
        markerRecorders.Clear();

        for (int i = 0; i < MarkerNames.Length; i++)
        {
            string markerName = MarkerNames[i];
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, markerName, 1);

            if (!recorder.Valid)
            {
                recorder.Dispose();
                markerRecorders.Add(MarkerRecorder.Unavailable(markerName));
                continue;
            }

            markerRecorders.Add(MarkerRecorder.Available(markerName, recorder));
        }
    }

    private string BuildSummary(int frame, int population, int predators, int simulationStepsPerFrame)
    {
        StringBuilder builder = new StringBuilder(256);
        builder.Append("[SimulationPerformanceAnalyzer] ");
        builder.Append("frame=").Append(frame);
        builder.Append(" population=").Append(population);
        builder.Append(" predators=").Append(predators);
        builder.Append(" simulationStepsPerFrame=").Append(simulationStepsPerFrame);

        for (int i = 0; i < markerRecorders.Count; i++)
        {
            MarkerRecorder entry = markerRecorders[i];
            builder.Append(" ").Append(entry.MarkerName).Append("=");
            if (!entry.IsAvailable)
            {
                builder.Append("n/a");
                continue;
            }

            builder.Append(GetMilliseconds(entry.Recorder).ToString("F4", CultureInfo.InvariantCulture)).Append("ms");
        }

        return builder.ToString();
    }

    private void InitializeCsv()
    {
        csvSamplesPending = 0;
        string safePrefix = string.IsNullOrWhiteSpace(csvFileNamePrefix) ? "simulation_performance" : csvFileNamePrefix.Trim();
        string fileName = appendTimestampToCsvFileName
            ? string.Format(CultureInfo.InvariantCulture, "{0}_{1:yyyyMMdd_HHmmss}.csv", safePrefix, DateTime.Now)
            : string.Format(CultureInfo.InvariantCulture, "{0}.csv", safePrefix);

        csvPath = Path.Combine(Application.persistentDataPath, fileName);
        csvOutputPath = csvPath;

        csvBuffer.Clear();
        csvBuffer.Append("frame,totalPopulation,predatorCount,simulationStepsPerFrame");
        for (int i = 0; i < markerRecorders.Count; i++)
        {
            csvBuffer.Append(',').Append(markerRecorders[i].MarkerName).Append("_ms");
        }

        csvBuffer.AppendLine();
        FlushCsvBuffer();

        Debug.Log($"[SimulationPerformanceAnalyzer] CSV logging enabled. Writing to '{csvPath}'.", this);
    }

    private void AppendCsvLine(int frame, int population, int predators, int simulationStepsPerFrame)
    {
        csvBuffer.Append(frame).Append(',');
        csvBuffer.Append(population).Append(',');
        csvBuffer.Append(predators).Append(',');
        csvBuffer.Append(simulationStepsPerFrame);

        for (int i = 0; i < markerRecorders.Count; i++)
        {
            csvBuffer.Append(',');
            MarkerRecorder entry = markerRecorders[i];
            if (!entry.IsAvailable)
            {
                csvBuffer.Append("n/a");
                continue;
            }

            csvBuffer.Append(GetMilliseconds(entry.Recorder).ToString("F4", CultureInfo.InvariantCulture));
        }

        csvBuffer.AppendLine();
    }

    private void FlushCsvBuffer()
    {
        if (!writeCsv || string.IsNullOrEmpty(csvPath) || csvBuffer.Length == 0)
        {
            return;
        }

        File.AppendAllText(csvPath, csvBuffer.ToString());
        csvBuffer.Clear();
        csvSamplesPending = 0;
    }

    private static double GetMilliseconds(ProfilerRecorder recorder)
    {
        return recorder.LastValue * NanosecondsToMilliseconds;
    }

    private readonly struct MarkerRecorder
    {
        public MarkerRecorder(string markerName, ProfilerRecorder recorder, bool isAvailable)
        {
            MarkerName = markerName;
            Recorder = recorder;
            IsAvailable = isAvailable;
        }

        public string MarkerName { get; }
        public ProfilerRecorder Recorder { get; }
        public bool IsAvailable { get; }

        public static MarkerRecorder Available(string markerName, ProfilerRecorder recorder)
        {
            return new MarkerRecorder(markerName, recorder, true);
        }

        public static MarkerRecorder Unavailable(string markerName)
        {
            return new MarkerRecorder(markerName, default, false);
        }

        public void Dispose()
        {
            if (!IsAvailable)
            {
                return;
            }

            Recorder.Dispose();
        }
    }
}
