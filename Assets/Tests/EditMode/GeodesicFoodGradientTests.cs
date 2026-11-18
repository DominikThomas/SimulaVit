using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicFoodGradientTests
{
    [Serializable] private sealed class Pair
    {
        public string scenario, detail;
        public GeodesicTaxisObservation before, after;
        public double relativeDelta;
        public string classification;
    }

    [TestCase("A")][TestCase("B")][TestCase("C")][TestCase("D")]
    [TestCase("E")][TestCase("F")][TestCase("G")][TestCase("H")]
    public void ProductionSamplerUsesActualLocalInputsAndExposesClippedSignals(string scenario)
    {
        using (var f = new GeodesicLocomotionEcologyExperiments.Fixture(LocomotionType.Amoeboid, true, false, false))
        {
            int cell = f.VentCell, next = f.ColdNeighbor, layer = 4, nextLayer = 4;
            float t0 = 338.15f, t1 = 338.15f; double h0 = .001d, h1 = .002d, c0 = 100d, c1 = 100d;
            switch (scenario)
            {
                case "B": h1 = h0; t0 = 308.15f; t1 = 313.15f; break;
                case "C": h1 = .004d; t1 = 308.15f; break;
                case "D": h0 = 10d; h1 = 100d; c0 = .0001d; c1 = .0002d; break;
                case "E": h0 = 100d; h1 = 1000d; t0 = 233.15f; t1 = 243.15f; break;
                case "F": h0 = .001d; h1 = 50d; t0 = 293.15f; t1 = 338.15f; break;
                case "G": h0 = .004d; h1 = .001d; break;
                case "H": next = cell; layer = 3; nextLayer = 4; h1 = .008d; t0 = 293.15f; break;
            }
            if (scenario != "H")
            {
                // Unit-volume channel-isolation fixture: concentration and inventory
                // gradients coincide. Walks and world trials retain physical volumes.
                f.Grid.PhysicalLayerVolumeKm3[f.Grid.GetNodeIndex(cell, layer)] = 1d;
                f.Grid.PhysicalLayerVolumeKm3[f.Grid.GetNodeIndex(next, nextLayer)] = 1d;
            }
            f.WriteHabitat(cell, layer, t0, h0, c0); f.WriteHabitat(next, nextLayer, t1, h1, c1);
            var a = f.ObserveAt(cell, layer); var b = f.ObserveAt(next, nextLayer);
            Assert.That(a.cell, Is.EqualTo(cell)); Assert.That(b.layer, Is.EqualTo(nextLayer));
            Assert.That(a.temperature, Is.EqualTo(t0)); Assert.That(b.temperature, Is.EqualTo(t1));
            double expectedA = GeodesicFoodSignal.Readiness(GeodesicFoodSignal.HydrogenotrophyTicks(a.co2, a.h2,
                f.Grid.PhysicalLayerVolumeKm3[f.Grid.GetNodeIndex(cell, layer)], .01f, .02f), a.performance, a.oxygenEfficiency);
            Assert.That(a.reactionReadiness, Is.EqualTo(expectedA));
            var comparison = GeodesicTaxisPolicy.Compare(b.reactionReadiness, a.reactionReadiness, .0001d, .05f, .05f);
            if (scenario == "A" || scenario == "B" || scenario == "D" || scenario == "F" || scenario == "H") Assert.That(comparison.trend, Is.EqualTo(GeodesicTaxisPolicy.Trend.Improving));
            if (scenario == "C") Assert.That(comparison.trend, Is.EqualTo(GeodesicTaxisPolicy.Trend.Neutral));
            if (scenario == "E") { Assert.That(a.reactionReadiness, Is.Zero); Assert.That(b.reactionReadiness, Is.Zero); }
            if (scenario == "G") Assert.That(comparison.trend, Is.EqualTo(GeodesicTaxisPolicy.Trend.Worsening));
            Save("pair-" + scenario, new Pair { scenario = scenario, detail = "Current-node temporal pair; not anticipatory neighbour sensing.", before = a, after = b,
                relativeDelta = comparison.normalizedDelta, classification = comparison.trend.ToString() });
        }
    }

    [Serializable] private sealed class Support { public string metabolism; public bool reactionDefined; public double withoutRelevantFood, withRelevantFood; }
    [TestCase(MetabolismType.Methanogenesis, true)]
    [TestCase(MetabolismType.Methanotrophy, true)]
    [TestCase(MetabolismType.Photosynthesis, true)]
    [TestCase(MetabolismType.Saprotrophy, false)]
    [TestCase(MetabolismType.Predation, false)]
    public void AuditOtherMetabolismsWithoutPretendingHydrogenSensingSupportsThem(MetabolismType metabolism, bool reactionDefined)
    {
        using (var f = new GeodesicLocomotionEcologyExperiments.Fixture(LocomotionType.Amoeboid, true, false, false))
        {
            f.WriteHabitat(f.VentCell, 4, 338.15f,
                metabolism == MetabolismType.Methanogenesis ? 0d : 100d,
                metabolism == MetabolismType.Photosynthesis ? 0d : 100d, 0d, 0d, 0d);
            var empty = f.ObserveAt(f.VentCell, 4, metabolism);
            f.WriteHabitat(f.VentCell, 4, 338.15f, 100d, 100d, 100d, 100d, 100d);
            var sample = f.ObserveAt(f.VentCell, 4, metabolism);
            bool defined = f.HasDefinedReaction(metabolism);
            Assert.That(defined, Is.EqualTo(reactionDefined));
            Assert.That(sample.reactionReadiness, Is.Zero, "Known baseline sensing gap: this characterizes the current implementation, not correct food-seeking support.");
            Assert.That(empty.reactionReadiness, Is.Zero);
            Save("support-" + metabolism, new Support { metabolism = metabolism.ToString(), reactionDefined = defined,
                withoutRelevantFood = empty.reactionReadiness, withRelevantFood = sample.reactionReadiness });
        }
    }

    [Test]
    public void SamplerDoesNotAdvertiseFoodForAZeroRequiredHydrogenInput()
    {
        using (var f = new GeodesicLocomotionEcologyExperiments.Fixture(LocomotionType.Amoeboid, true, false, false))
        {
            f.WriteHabitat(f.VentCell, 4, 338.15f, 100d, 100d);
            f.SetHydrogenInputs(.01f, 0f);
            Assert.That(f.ObserveAt(f.VentCell, 4).reactionReadiness, Is.Zero);
        }
    }

    [Test]
    public void ActualAmoeboidDivisionCanProduceFlagellaWhenTheExistingUpgradeGateSucceeds()
    {
        using (var f = new GeodesicLocomotionEcologyExperiments.Fixture(LocomotionType.Amoeboid, true, false, false))
        {
            f.ForceEligibleAmoeboidDivision(); int amoebae = 0, flagella = 0;
            for (int i = 0; i < f.State.Count; i++) { if (f.State.Locomotion[i] == LocomotionType.Amoeboid) amoebae++; if (f.State.Locomotion[i] == LocomotionType.Flagellum) flagella++; }
            Assert.That(amoebae, Is.EqualTo(8)); Assert.That(flagella, Is.EqualTo(8));
        }
    }

    [Serializable] private sealed class Walk
    {
        public string scenario, phenotype; public bool steering;
        public string fieldSemantics = "concentration fields on actual physical node volumes";
        public int organisms = 32; public float seconds = 60f;
        public double meanSignedFoodDirection, meanSignedThermalDirection, meanThermalPerformance, meanReadiness, meanPhysicalKm;
        public int invalidPhysicalLayerSamples;
        public int returnedToCore;
        public double coreResidenceFraction;
        public long improving, worsening, neutral, tumbles, verticalBetter, verticalWorse, layerTransitions;
    }
    [TestCase("A")][TestCase("B")][TestCase("C")][TestCase("D")]
    [TestCase("E")][TestCase("F")][TestCase("G")][TestCase("H")]
    public void MeasureActualRunAndTumbleOnKnownFieldsWithoutAssertingAnEcologicalVictory(string scenario)
    {
        for (int phenotype = 1; phenotype <= 2; phenotype++)
        foreach (bool steering in new[] { false, true })
        using (var f = new GeodesicLocomotionEcologyExperiments.Fixture((LocomotionType)phenotype, steering, false, false, 32, scenario == "H" ? 7.99f : 7.5f))
        {
            Vector3 origin = f.Topology.CellDirections[f.VentCell];
            Vector3 axis = Vector3.Cross(origin, Vector3.up).normalized, thermalAxis = Vector3.Cross(origin, axis).normalized;
            double referenceVolume = f.Grid.PhysicalLayerVolumeKm3[f.Grid.GetNodeIndex(f.VentCell, 4)];
            for (int cell = 0; cell < f.Grid.CellCount; cell++)
            for (int layer = 0; layer < 5; layer++)
            {
                double x = Vector3.Dot(f.Topology.CellDirections[cell], axis), y = Vector3.Dot(f.Topology.CellDirections[cell], thermalAxis);
                double h2 = .001d * Math.Exp(Math.Max(-6d, Math.Min(6d, x * 30d))), co2 = 100d;
                float t = 338.15f;
                if (scenario == "B" || scenario == "C") { t = 313.15f + (float)(40d * Math.Tanh(20d * (scenario == "C" ? y : x))); if (scenario == "B") h2 = .001d; }
                if (scenario == "D") { co2 = .0001d * Math.Exp(Math.Max(-6d, Math.Min(6d, x * 30d))); h2 = 100d; }
                if (scenario == "E") t = 233.15f;
                if (scenario == "F") { t = cell == f.VentCell ? 338.15f : 293.15f; h2 = cell == f.VentCell ? 50d : .001d; }
                if (scenario == "H") { t = layer == 4 ? 338.15f : 293.15f; h2 = layer == 4 ? .01d : .001d; }
                // Parameters specify concentrations relative to one reference-node
                // volume, so 'uniform H2' is chemically uniform, not equal inventory
                // in differently sized cells/layers.
                double volumeRatio = f.Grid.PhysicalLayerVolumeKm3[f.Grid.GetNodeIndex(cell, layer)] / referenceVolume;
                f.WriteHabitat(cell, layer, t, h2 * volumeRatio, co2 * volumeRatio);
            }
            if (scenario == "G") for (int i = 0; i < f.State.Count; i++) f.State.PassiveDriftTangent[i] = -axis;
            if (scenario == "F")
            {
                f.Move(.1f); // Establish a real warm experience before displacement.
                for (int i = 0; i < f.State.Count; i++)
                {
                    f.State.GeodesicCellIndex[i] = f.ColdNeighbor;
                    f.State.Position[i] = f.Topology.CellDirections[f.ColdNeighbor] * f.Grid.LayerCenterRadius[f.Grid.GetNodeIndex(f.ColdNeighbor, 4)];
                    f.State.PassiveDriftDirection[i] = f.State.PassiveDriftTangent[i] = Vector3.zero; f.State.PassiveVisualRadius[i] = 0f;
                }
            }
            var report = new Walk { scenario = scenario, phenotype = ((LocomotionType)phenotype).ToString(), steering = steering };
            var returned = new bool[f.State.Count];
            for (int step = 0; step < 600; step++)
            {
                f.Move(.1f);
                for (int i = 0; i < f.State.Count; i++)
                {
                    f.State.Age[i] += .1f;
                    int cell = f.State.GeodesicCellIndex[i], layer = f.State.CurrentOceanLayerIndex[i];
                    if (cell == f.VentCell && layer == 4)
                    { report.coreResidenceFraction += 1d; if (scenario == "F") returned[i] = true; }
                    if (scenario == "H")
                    {
                        int node = f.Grid.GetNodeIndex(cell, layer); float radius = f.State.PassiveVisualRadius[i];
                        if (!f.Grid.IsNodeActive(cell, layer) || radius < f.Grid.LayerInnerRadius[node] - 1e-5f || radius > f.Grid.LayerOuterRadius[node] + 1e-5f)
                            report.invalidPhysicalLayerSamples++;
                    }
                    var observation = f.ObserveAt(cell, layer);
                    report.meanThermalPerformance += observation.performance; report.meanReadiness += observation.reactionReadiness;
                }
            }
            for (int i = 0; i < f.State.Count; i++)
            {
                report.meanSignedFoodDirection += Vector3.Dot(f.State.Position[i].normalized - origin, axis) / f.State.Count;
                report.meanSignedThermalDirection += Vector3.Dot(f.State.Position[i].normalized - origin, scenario == "C" ? thermalAxis : axis) / f.State.Count;
            }
            report.meanThermalPerformance /= 600d * f.State.Count; report.meanReadiness /= 600d * f.State.Count;
            report.coreResidenceFraction /= 600d * f.State.Count;
            for (int i = 0; i < returned.Length; i++) if (returned[i]) report.returnedToCore++;
            report.meanPhysicalKm = (double)f.Counter("activeDistanceKm") / f.State.Count;
            report.improving = (long)f.Counter("suitabilityImproving"); report.worsening = (long)f.Counter("suitabilityWorsening"); report.neutral = (long)f.Counter("suitabilityNeutral");
            report.tumbles = (long)f.Counter("activeTumbles"); report.verticalBetter = (long)f.Counter("verticalBetter"); report.verticalWorse = (long)f.Counter("verticalWorse");
            report.layerTransitions = (long)f.Counter("activeLayerTransitions");
            Assert.That(double.IsFinite(report.meanReadiness), Is.True);
            Assert.That(report.invalidPhysicalLayerSamples, Is.Zero);
            if (scenario == "E") Assert.That(report.meanReadiness, Is.Zero);
            Save("walk-" + scenario + "-" + phenotype + "-" + steering, report);
        }
    }

    private static void Save(string name, object value)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/FoodTaxisAudit")); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, name + ".json"), JsonUtility.ToJson(value, true));
    }
}
