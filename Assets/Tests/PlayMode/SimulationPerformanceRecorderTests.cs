using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine.TestTools;
using UnityEngine;

public sealed class SimulationPerformanceRecorderTests
{
    [UnityTest]
    public IEnumerator CsvFinalWindowMatchesHeaderAndUsesInvariantNumbers()
    {
        var go = new GameObject("Performance CSV test");
        go.SetActive(false);
        var populationFixture = new GameObject("Performance population test");
        populationFixture.SetActive(false);
        string path = null;
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var analyzer = go.AddComponent<SimulationPerformanceAnalyzer>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var manager = populationFixture.AddComponent<ReplicatorManager>();
            typeof(ReplicatorManager).GetField("simulationTimeSeconds", flags).SetValue(manager, 123.5d);
            var state = (ReplicatorPopulationState)typeof(ReplicatorManager).GetField("populationState", flags).GetValue(manager);
            var agents = (List<Replicator>)typeof(ReplicatorManager).GetField("agents", flags).GetValue(manager);
            var metabolisms = new[]
            {
                (MetabolismType.Hydrogenotrophy, "hydrogenotrophy_avg"),
                (MetabolismType.SulfurChemosynthesis, "sulfur_chemosynthesis_avg"),
                (MetabolismType.Fermentation, "fermentation_avg"),
                (MetabolismType.Photosynthesis, "photosynthesis_avg"),
                (MetabolismType.Saprotrophy, "saprotrophy_avg"),
                (MetabolismType.Methanogenesis, "methanogenesis_avg"),
                (MetabolismType.Methanotrophy, "methanotrophy_avg"),
                (MetabolismType.Predation, "predation_avg")
            };
            for (int i = 0; i < metabolisms.Length; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    var agent = new Replicator(Vector3.up, Quaternion.identity, 100f, Color.white, default, 0f, metabolisms[i].Item1);
                    agents.Add(agent);
                    state.AddAgentFromReplicatorData(agent);
                }
            }
            manager.UpdateMetabolismCounts();
            typeof(SimulationPerformanceAnalyzer).GetField("replicatorManager", flags).SetValue(analyzer, manager);
            typeof(SimulationPerformanceAnalyzer).GetField("writeCsv", flags).SetValue(analyzer, true);
            typeof(SimulationPerformanceAnalyzer).GetField("logSummaryToConsole", flags).SetValue(analyzer, false);
            typeof(SimulationPerformanceAnalyzer).GetField("csvFileNamePrefix", flags).SetValue(analyzer,
                "performance_recorder_test_" + System.Guid.NewGuid().ToString("N"));
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            go.SetActive(true);
            path = analyzer.CsvOutputPath;
            for (int i = 0; i < 4; i++) yield return null;
            analyzer.enabled = false;
            string[] lines = System.IO.File.ReadAllLines(path);
            Assert.That(lines.Length, Is.EqualTo(2), "Header and final partial-window row must both flush on disable.");
            string[] header = lines[0].Split(','), row = lines[1].Split(',');
            Assert.That(row.Length, Is.EqualTo(header.Length));
            Assert.That(double.Parse(row[3], System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThan(0d));
            Assert.That(double.Parse(row[4], System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThan(0d));
            double ReadColumn(string name)
            {
                int index = System.Array.IndexOf(header, name);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), name);
                return double.Parse(row[index], System.Globalization.CultureInfo.InvariantCulture);
            }
            double population = 0d;
            for (int i = 0; i < metabolisms.Length; i++)
            {
                string column = metabolisms[i].Item2;
                double count = ReadColumn(column);
                Assert.That(count, Is.EqualTo(i + 1), column);
                Assert.That(analyzer.LatestSummary, Does.Contain($"{column}={i + 1} "));
                population += count;
            }
            Assert.That(ReadColumn("population_avg"), Is.EqualTo(population).And.EqualTo(manager.TotalPopulation));
            Assert.That(ReadColumn("end_simulated_seconds"), Is.EqualTo(123.5d));
            Assert.That(ReadColumn("simulated_seconds"), Is.Zero);
            Assert.That(analyzer.LatestSummary, Does.Contain("end_simulated_seconds=123.5 "));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(populationFixture);
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    [UnityTest]
    public IEnumerator AnalyzerContinuesCollectingAfterResettingCompletedFrames()
    {
        var go = new GameObject("Performance observer frame test");
        try
        {
            var analyzer = go.AddComponent<SimulationPerformanceAnalyzer>();
            for (int i = 0; i < 4; i++) yield return null;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var totals = (double[])typeof(SimulationPerformanceAnalyzer).GetField("markerTotals", flags).GetValue(analyzer);
            int frames = (int)typeof(SimulationPerformanceAnalyzer).GetField("frames", flags).GetValue(analyzer);
            Assert.That(frames, Is.GreaterThan(0));
            Assert.That(totals[24], Is.GreaterThan(0d), "The analyzer's own marker must continue recording across per-frame resets.");
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void EnablingAndDisablingAnalyzerPreservesRandomStateAndTimeSettings()
    {
        string randomState = JsonUtility.ToJson(Random.state);
        float timeScale = Time.timeScale, fixedDelta = Time.fixedDeltaTime, maximumDelta = Time.maximumDeltaTime;
        var go = new GameObject("Performance observer test");
        try
        {
            var analyzer = go.AddComponent<SimulationPerformanceAnalyzer>();
            analyzer.enabled = false;
            Assert.That(JsonUtility.ToJson(Random.state), Is.EqualTo(randomState));
            Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedDelta));
            Assert.That(Time.maximumDeltaTime, Is.EqualTo(maximumDelta));
        }
        finally { Object.DestroyImmediate(go); }
    }

    [UnityTest]
    public IEnumerator RepeatedInvocationsAreSummedAndIdleFramesDoNotRepeatOldSamples()
    {
        var marker = new ProfilerMarker("SimulaVit.PerformanceRecorder.Test");
        using (var recorder = ProfilerRecorder.StartNew(marker, 1,
            ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
            ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
        {
            Assert.That(recorder.Valid, Is.True);
            yield return null;
            recorder.Reset();
            recorder.Start();
            for (int i = 0; i < 7; i++) { using (marker.Auto()) { } }
            yield return null;
            Assert.That(recorder.Count, Is.EqualTo(1));
            Assert.That(recorder.GetSample(0).Count, Is.EqualTo(7), "All substep invocations must contribute to one frame sample.");
            recorder.Reset();
            recorder.Start();
            yield return null;
            Assert.That(recorder.Count == 0 || recorder.LastValue == 0, Is.True, "Idle frames must not repeat the last nonzero duration.");
        }
    }
}
