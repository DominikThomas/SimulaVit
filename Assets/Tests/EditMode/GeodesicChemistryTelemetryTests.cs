using NUnit.Framework;

public class GeodesicChemistryTelemetryTests
{
    [Test]
    public void WeightedMeansAndAnoxicFractionUseNodeVolume()
    {
        var stats = new WeightedChemistryStatistics();
        stats.Reset();
        stats.Add(1d, 2f, 0f, 0f, 1f, 0f, 0f, 0f, 0.1f);
        stats.Add(3d, 6f, 2f, 0f, 5f, 0f, 0f, 0f, 0.1f);
        Assert.That(stats.Mean((int)GeodesicOceanResource.CO2), Is.EqualTo(5d).Within(1e-12));
        Assert.That(stats.Mean((int)GeodesicOceanResource.O2), Is.EqualTo(1.5d).Within(1e-12));
        Assert.That(stats.AnoxicFraction, Is.EqualTo(0.25d).Within(1e-12));
        Assert.That(stats.O2Minimum, Is.Zero);
        Assert.That(stats.O2Maximum, Is.EqualTo(2f));
    }

    [Test]
    public void SeparateLayerAccumulatorsPreserveLayerLocality()
    {
        var layer0 = new WeightedChemistryStatistics();
        var layer4 = new WeightedChemistryStatistics();
        layer0.Reset(); layer4.Reset();
        layer0.Add(2d, 0f, 8f, 0f, 0f, 0f, 0f, 0f, 0.01f);
        layer4.Add(5d, 0f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0.01f);
        Assert.That(layer0.Mean((int)GeodesicOceanResource.O2), Is.EqualTo(8d));
        Assert.That(layer4.Mean((int)GeodesicOceanResource.O2), Is.EqualTo(0.5d));
    }

    [Test]
    public void ChemistryCounterDeltaDoesNotResetCumulativeValues()
    {
        var first = new ChemistryCounters(2d, 3d, 4d, 5d, 6d, 7d);
        var second = new ChemistryCounters(3d, 5d, 7d, 9d, 11d, 13d);
        ChemistryCounters delta = second - first;
        Assert.That(delta.H2, Is.EqualTo(1d));
        Assert.That(delta.H2S, Is.EqualTo(2d));
        Assert.That(delta.Fe2, Is.EqualTo(3d));
        Assert.That(delta.O2, Is.EqualTo(4d));
        Assert.That(delta.S0, Is.EqualTo(5d));
        Assert.That(delta.Fe3, Is.EqualTo(6d));
        Assert.That(second.Fe3, Is.EqualTo(13d));
    }


    [Test]
    public void SimulatedTimeSchedulingHandlesPauseAndDisabledInterval()
    {
        Assert.That(GeodesicChemistryTelemetry.IsDue(60d, 59d, 60d), Is.False);
        Assert.That(GeodesicChemistryTelemetry.IsDue(60d, 59d, 60d), Is.False, "stopped authoritative time remains not due");
        Assert.That(GeodesicChemistryTelemetry.IsDue(60d, 60d, 60d), Is.True);
        Assert.That(GeodesicChemistryTelemetry.IsDue(0d, 600d, 60d), Is.False);
        Assert.That(GeodesicChemistryTelemetry.IsDue(-1d, 600d, 60d), Is.False);
    }

    [Test]
    public void DisabledTelemetryNeverTakesAnExpensiveSnapshot()
    {
        var schedule = new ChemistryTelemetrySchedule();
        schedule.Reset(0d, 10d, 60d, 5d);
        Assert.That(schedule.TryTakeSnapshot(false, 60d, 5d, 600d, 100d), Is.False);
        Assert.That(schedule.FullSnapshotCount, Is.Zero);
    }

    [Test]
    public void CrossedSimulationDeadlinesCoalesceUntilRealTimeMinimumElapses()
    {
        var schedule = new ChemistryTelemetrySchedule();
        schedule.Reset(0d, 0d, 60d, 5d);
        Assert.That(schedule.TryTakeSnapshot(true, 60d, 5d, 600d, 1d), Is.False);
        Assert.That(schedule.TryTakeSnapshot(true, 60d, 5d, 600d, 5d), Is.True);
        Assert.That(schedule.FullSnapshotCount, Is.EqualTo(1));
        Assert.That(schedule.NextSimulationTime, Is.EqualTo(660d));
        Assert.That(schedule.TryTakeSnapshot(true, 60d, 5d, 1200d, 9.999d), Is.False);
        Assert.That(schedule.TryTakeSnapshot(true, 60d, 5d, 1200d, 10d), Is.True);
        Assert.That(schedule.FullSnapshotCount, Is.EqualTo(2));
    }

    [Test]
    public void SnapshotRecordsLatestAuthoritativeSimulationTime()
    {
        var schedule = new ChemistryTelemetrySchedule();
        schedule.Reset(12d, 20d, 60d, 5d);
        Assert.That(schedule.TryTakeSnapshot(true, 60d, 5d, 987.25d, 25d), Is.True);
        Assert.That(schedule.LastSnapshotSimulationTime, Is.EqualTo(987.25d));
        Assert.That(schedule.LastSnapshotRealTime, Is.EqualTo(25d));
    }

    [Test]
    public void PausedSimulationDoesNotRepeatedlyTriggerTelemetry()
    {
        var schedule = new ChemistryTelemetrySchedule();
        schedule.Reset(0d, 0d, 60d, 5d);
        Assert.That(schedule.TryTakeSnapshot(true, 60d, 5d, 60d, 5d), Is.True);
        Assert.That(schedule.TryTakeSnapshot(true, 60d, 5d, 60d, 10d), Is.False);
        Assert.That(schedule.TryTakeSnapshot(true, 60d, 5d, 60d, 100d), Is.False);
    }

    [Test]
    public void WorldResetClearsSnapshotHistoryAndTimingState()
    {
        var schedule = new ChemistryTelemetrySchedule();
        schedule.Reset(0d, 0d, 60d, 5d);
        Assert.That(schedule.TryTakeSnapshot(true, 60d, 5d, 60d, 5d), Is.True);
        schedule.Clear();
        Assert.That(schedule.FullSnapshotCount, Is.Zero);
        Assert.That(schedule.LastSnapshotSimulationTime, Is.Zero);
        schedule.Reset(500d, 50d, 60d, 5d);
        Assert.That(schedule.NextSimulationTime, Is.EqualTo(560d));
        Assert.That(schedule.NextEligibleRealTime, Is.EqualTo(55d));
    }

    [Test]
    public void SchedulingDoesNotMutateChemistryOrDiagnosticCounters()
    {
        var counters = new ChemistryCounters(1d, 2d, 3d, 4d, 5d, 6d, 7d);
        var schedule = new ChemistryTelemetrySchedule();
        schedule.Reset(0d, 0d, 60d, 5d);
        Assert.That(schedule.TryTakeSnapshot(false, 60d, 5d, 600d, 50d), Is.False);
        Assert.That(counters.H2, Is.EqualTo(1d));
        Assert.That(counters.FeS, Is.EqualTo(7d));
    }
}
