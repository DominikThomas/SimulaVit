using System;

public enum GeodesicFounderEmergenceMode { Batch = 0, Gradual = 1 }

/// <summary>Poisson event times in simulated seconds. Rejected events still consume the initial cap.
/// No frame-time inputs or draws from physiology, movement or reproduction streams.</summary>
public sealed class GeodesicFounderEmergenceSchedule
{
    public const float DefaultMeanInterval = 10f;
    public const float DefaultOngoingMeanInterval = 300f;
    public double NextEventTime { get; private set; } = double.PositiveInfinity;
    public int InitialEventsRemaining { get; private set; }
    private Random random;
    private double initialInterval, ongoingInterval;
    private bool ongoing;

    public static float NormalizeInterval(float value, float fallback = DefaultMeanInterval) =>
        float.IsFinite(value) ? Math.Max(.01f, value) : fallback;

    public void Configure(int biologySeed, double startTime, float delay, int initialCount,
        float meanInterval, bool continueEmergence, float ongoingMeanInterval)
    {
        random = new Random(unchecked(biologySeed ^ 0x454D4552));
        InitialEventsRemaining = Math.Max(0, initialCount);
        initialInterval = NormalizeInterval(meanInterval);
        ongoingInterval = NormalizeInterval(ongoingMeanInterval, DefaultOngoingMeanInterval);
        ongoing = continueEmergence;
        double origin = double.IsFinite(startTime) ? Math.Max(0d, startTime) : 0d;
        origin += float.IsFinite(delay) ? Math.Max(0f, delay) : 0f;
        NextEventTime = InitialEventsRemaining > 0 || ongoing
            ? origin + SampleInterval(InitialEventsRemaining > 0 ? initialInterval : ongoingInterval)
            : double.PositiveInfinity;
    }

    public bool TryConsume(double simulationTime, out double eventTime)
    {
        eventTime = NextEventTime;
        if (!double.IsFinite(simulationTime) || simulationTime < eventTime || !double.IsFinite(eventTime)) return false;
        if (InitialEventsRemaining > 0) InitialEventsRemaining--;
        NextEventTime = InitialEventsRemaining > 0 || ongoing
            ? eventTime + SampleInterval(InitialEventsRemaining > 0 ? initialInterval : ongoingInterval)
            : double.PositiveInfinity;
        return true;
    }

    private double SampleInterval(double mean) => Math.Max(1e-9d, -Math.Log(1d - random.NextDouble()) * mean);
}
