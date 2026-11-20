using NUnit.Framework;

public sealed class SimulationPerformanceAnalyzerTests
{
    [Test]
    public void PercentileUsesAllFramesIncludingTailAndIgnoresUnusedCapacity()
    {
        var frames = new double[128];
        for (int i = 0; i < 20; i++) frames[i] = 20 - i;
        Assert.That(SimulationPerformanceAnalyzer.Percentile95(frames, 20), Is.EqualTo(19d));
        Assert.That(SimulationPerformanceAnalyzer.Percentile95(frames, 0), Is.Zero);
    }

    [Test]
    public void AggregateTotalsCountEachRootOnceAndRemoveNestedActiveMovement()
    {
        var markers = new double[48];
        markers[0] = 100; // Biology/legacy simulation root.
        markers[1] = 5; markers[2] = 2; // Visual synchronization, outside telemetry.
        markers[3] = 40; markers[4] = 20; // Resource and thermal Update roots.
        markers[5] = 30; markers[6] = 10; // Reaction parent, nested competition.
        markers[7] = 15; markers[8] = 40; markers[9] = 10; markers[10] = 5;
        markers[11] = 8; markers[12] = 35; // Chemistry is nested inside transport.
        markers[17] = 1; markers[18] = 2; markers[19] = 3;
        markers[20] = 4; markers[21] = 1; // Internal telemetry.
        markers[22] = 3; markers[23] = 1; markers[24] = 0.5;
        markers[25] = 2; markers[26] = 1; markers[27] = 3;
        var totals = new double[10];
        SimulationPerformanceAnalyzer.AccumulateAggregates(markers, totals);
        Assert.That(totals, Is.EqualTo(new double[] { 160, 30, 18, 30, 10, 8, 47, 8, 16.5, 174.5 }));
    }

    [Test]
    public void FrameTotalsAccumulateRatherThanReplaceThePreviousFrame()
    {
        var markers = new double[48];
        var totals = new double[10];
        markers[0] = 12;
        SimulationPerformanceAnalyzer.AccumulateAggregates(markers, totals);
        markers[0] = 3;
        SimulationPerformanceAnalyzer.AccumulateAggregates(markers, totals);
        Assert.That(totals[0], Is.EqualTo(15));
        Assert.That(totals[9], Is.EqualTo(15));
    }
}
