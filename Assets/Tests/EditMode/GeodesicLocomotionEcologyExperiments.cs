using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Development-only selection experiments using the production movement, competition,
/// reaction and lifecycle methods. Synthetic frozen habitat, not a generated seed-283
/// planet replay. Every phenotype gets a separate identical resource field, so unequal
/// competition between phenotype cohorts cannot decide the comparison in advance.
/// </summary>
public sealed class GeodesicLocomotionEcologyExperiments
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static GeodesicGridTopology topology;
    private static GeodesicTransportGraph graph;

    [Serializable]
    public sealed class Report
    {
        public string experiment, phenotype, habitat;
        public int seed = 283, subdivision = 6, founders = 8, survivors, survivingFounders, survivingDescendants;
        public bool steering, immobilized, phenotypeAndThermalMutationDisabled = true;
        public float seconds = 60f, deltaSeconds = .05f;
        public double organismSeconds, ventBottomSeconds, thermallyUnsuitableSeconds;
        public double ventResidenceFraction, unsuitableFraction, returnedFounderFraction;
        public int departedFounders, returnedFounders;
        public double activeAttemptedKm, actualHorizontalKm, movementEnergy, metabolicEnergy, carbonAssimilated;
        public long activeCrossings, activeLayerTransitions, allCellCrossings, allLayerTransitions, births, deaths, improving, worsening, neutral, tumbles;
        public double[] meanChosenTumbleProbabilityByTrend;
        // Saved physical rates 5/2 times the existing global multiplier 10.
        public double h2SourceInventoryPerSecond = 50d, co2SourceInventoryPerSecond = 20d;
        public double carbonBalanceResidual;
    }

    [TestCase("A", false, false, false)]
    [TestCase("B", true, false, false)]
    [TestCase("C", true, true, false)]
    [TestCase("D", true, false, true)]
    public void MatchedCohortsReportProductionMovementAndEcologicalOutcomes(string experiment, bool steering, bool immobilized, bool gradients)
    {
        if (topology == null) { topology = GeodesicGridTopology.Build(6); graph = new GeodesicTransportGraph(topology); }
        var reports = new Report[4];
        for (int phenotype = 0; phenotype < 4; phenotype++)
        using (var f = new Fixture((LocomotionType)phenotype, steering, immobilized, gradients))
        {
            Report r = f.Run(experiment); reports[phenotype] = r;
            Assert.That(double.IsFinite(r.carbonBalanceResidual), Is.True);
            Assert.That(Math.Abs(r.carbonBalanceResidual), Is.LessThan(1e-5), "Ocean+organism carbon, including explicit source additions and death recycling.");
            Assert.That(r.survivors, Is.EqualTo(r.founders + r.births - r.deaths));
            Assert.That(r.ventResidenceFraction, Is.InRange(0d, 1d));
            Assert.That(r.movementEnergy, Is.GreaterThanOrEqualTo(0d));
            if (phenotype == 3 && !gradients) Assert.That(r.ventResidenceFraction, Is.EqualTo(1d).Within(1e-7));
            if (immobilized)
            {
                Assert.That(r.actualHorizontalKm, Is.LessThan(1e-8));
                Assert.That(r.ventResidenceFraction, Is.EqualTo(1d).Within(1e-7));
                if (phenotype == 1 || phenotype == 2) Assert.That(r.movementEnergy, Is.GreaterThan(0d), "Constraint retains paid movement effort.");
            }
        }
        // No assertion forces a desired population ranking. These are measurements.
        string directory = Path.Combine(Application.dataPath, "../Temp/LocomotionAudit");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "ecology-" + experiment + ".json"), JsonUtility.ToJson(new ReportSet { reports = reports }, true));
        foreach (Report r in reports) TestContext.Out.WriteLine(JsonUtility.ToJson(r));
    }

    [Serializable] private sealed class ReportSet { public Report[] reports; }

    [Test]
    public void ResolvedSteeringUsesExplicitFlagAndDiagnosticsNeverSelectMode()
    {
        var runtime = new GeodesicBiologyRuntime();
        runtime.SetChemotaxisABContext(false, true, 0d);
        Assert.That(runtime.DescribeResolvedLocomotionControls(), Does.Contain("steeringFlag=False mode=B ABObserverEnabled=True ABOverridesSteering=false"));
        runtime.SetChemotaxisABContext(true, true, 1d);
        Assert.That(runtime.DescribeResolvedLocomotionControls(), Does.Contain("steeringFlag=True mode=A"));
        runtime.SetChemotaxisABContext(true, false, 2d);
        Assert.That(runtime.DescribeResolvedLocomotionControls(), Does.Contain("steeringFlag=True mode=A ABObserverEnabled=False"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject root;
        private readonly GeodesicBiologyRuntime runtime = new GeodesicBiologyRuntime();
        private readonly ReplicatorPopulationState state = new ReplicatorPopulationState();
        private readonly List<Replicator> agents = new List<Replicator>();
        private readonly GeodesicOceanLayerGrid grid;
        private readonly GeodesicOceanResourceField resources;
        private readonly double[] concentrations;
        private readonly float[] temperatures;
        private readonly bool steering, immobilized, gradients;
        private readonly LocomotionType phenotype;
        private readonly int ventCell, coldNeighbor;
        private const int Bottom = 4;
        private readonly HashSet<long> founders = new HashSet<long>(), departed = new HashSet<long>(), returned = new HashSet<long>();
        private readonly Dictionary<long, Vector3> previousDirection = new Dictionary<long, Vector3>();
        private readonly Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>();
        private readonly GeodesicBiologyTemperatureSettings thermal;
        private double initialCarbon, sourceCarbon;

        public Fixture(LocomotionType phenotype, bool steering, bool immobilized, bool gradients)
        {
            this.phenotype = phenotype; this.steering = steering; this.immobilized = immobilized; this.gradients = gradients;
            root = new GameObject("controlled locomotion ecology " + phenotype); root.SetActive(false);
            var planet = root.AddComponent<PlanetGenerator>(); resources = root.AddComponent<GeodesicOceanResourceField>();
            var mask = new bool[topology.CellCount]; var floor = new float[topology.CellCount];
            for (int i = 0; i < mask.Length; i++) { mask[i] = true; floor[i] = 7.5f; }
            grid = new GeodesicOceanLayerGrid(topology, graph, mask, floor, 8f, 5, new[] { 0f, .1f, .25f, .45f, .7f, 1f });
            ventCell = 283 % grid.CellCount; coldNeighbor = topology.Neighbors6[ventCell * 6];
            concentrations = new double[7 * grid.NodeCapacity]; temperatures = new float[grid.NodeCapacity];
            var temperatureStamps = new int[grid.NodeCapacity];
            int[] rings = Rings();
            for (int cell = 0; cell < grid.CellCount; cell++)
            for (int layer = 0; layer < 5; layer++)
            {
                int node = grid.GetNodeIndex(cell, layer);
                double h2Inventory = rings[cell] == 0 && layer == Bottom ? 50d : gradients && rings[cell] == 1 ? 10d : .001d;
                concentrations[(int)GeodesicOceanResource.H2 * grid.NodeCapacity + node] = h2Inventory / grid.PhysicalLayerVolumeKm3[node];
                concentrations[(int)GeodesicOceanResource.CO2 * grid.NodeCapacity + node] = 100d / grid.PhysicalLayerVolumeKm3[node];
                temperatures[node] = cell == ventCell && layer == Bottom ? 338.15f
                    : gradients && rings[cell] == 1 && cell != coldNeighbor && layer == Bottom ? 313.15f : 293.15f;
                temperatureStamps[node] = 1;
            }
            Set(resources, "sourceGrid", grid); Set(resources, "nodeCapacity", grid.NodeCapacity);
            Set(resources, "concentrationsByResourceThenNode", concentrations); Set(resources, "resourceMayHaveSpatialVariation", new bool[7]); Set(resources, "initialized", true);
            Set(runtime, "planet", planet); Set(runtime, "resources", resources); Set(runtime, "grid", grid);
            foreach (string name in new[] { "demand", "availabilityFactor", "delta" }) Set(runtime, name, new double[7 * grid.NodeCapacity]);
            foreach (string name in new[] { "stamps", "lightStamps", "touched" }) Set(runtime, name, new int[grid.NodeCapacity]);
            Set(runtime, "temperatureByNode", temperatures); Set(runtime, "temperatureStamps", temperatureStamps);
            Set(runtime, "oxygenByNode", new float[grid.NodeCapacity]); Set(runtime, "lightByNode", new float[grid.NodeCapacity]);
            Set(runtime, "reproductionRandom", new System.Random(283)); Set(runtime, "temperatureMutationRandom", new System.Random(283));
            Set(runtime, "locomotionMutationRandom", new System.Random(283));
            thermal = GeodesicBiologyTemperatureSettings.Default; thermal.MutationChance = 0f;
            Set(runtime, "temperatureSettings", thermal);
            runtime.SetChemotaxisABContext(steering, false, 0d);
            var locomotion = GeodesicLocomotionSettings.Default; locomotion.logDiagnostics = false;
            runtime.SetLocomotionSettings(locomotion, Steering(), 4f, 1.2f, 2f, .5f, 1.5f, 0f, .05f, .001f, true, .01f);
            Call("SetHydrogenotrophMovementInputs", .01f, .02f, false, 0f, 1f, 1f);
            runtime.SetVisualGrowthSettings(ReplicatorVisualGrowthSettings.Default);
            var random = new System.Random(283);
            for (int i = 0; i < 8; i++)
            {
                var agent = new Replicator(topology.CellDirections[ventCell] * grid.LayerCenterRadius[grid.GetNodeIndex(ventCell, Bottom)],
                    Quaternion.identity, 10000f, Color.white, default, (float)random.NextDouble(), MetabolismType.Hydrogenotrophy, phenotype);
                GeodesicBiologyRuntime.InitializeFounderBiologicalState(agent, ventCell, Bottom, new Vector2(323.15f, 353.15f), 20f, 100f, 0f, .15f);
                agent.organicCStore = .15f; agents.Add(agent); state.AddAgentFromReplicatorData(agent); founders.Add(agent.DiagnosticId);
            }
            initialCarbon = TotalCarbon();
        }

        private int[] Rings()
        {
            var rings = new int[topology.CellCount]; Array.Fill(rings, int.MaxValue); rings[ventCell] = 0;
            var queue = new Queue<int>(); queue.Enqueue(ventCell);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue(); if (rings[cell] >= 2) continue;
                for (int s = 0; s < topology.NeighborCounts[cell]; s++)
                { int next = topology.Neighbors6[cell * 6 + s]; if (rings[next] <= rings[cell] + 1) continue; rings[next] = rings[cell] + 1; queue.Enqueue(next); }
            }
            return rings;
        }

        public Report Run(string experiment)
        {
            var r = new Report { experiment = experiment, phenotype = phenotype.ToString(), steering = steering, immobilized = immobilized,
                habitat = gradients ? "local H2/thermal shoulders; one adjacent cold zero-performance cell; founder displacement at 10s" : "one viable vent-bottom node; cold ocean" };
            float reactionClock = 0f;
            for (int step = 0; step < 1200; step++)
            {
                float time = (step + 1) * r.deltaSeconds;
                runtime.SetChemotaxisABContext(steering, false, time);
                if (gradients && step == 200) DisplaceFounders();
                for (int i = 0; i < state.Count; i++) previousDirection[agents[i].DiagnosticId] = state.Position[i].normalized;
                Spatial[] snapshot = immobilized ? CapturePositions() : null;
                Call("RunGeodesicMovement", r.deltaSeconds, agents, state);
                if (immobilized) RestorePositions(snapshot);
                for (int i = 0; i < state.Count; i++)
                {
                    double angle = Math.Atan2(Vector3.Cross(previousDirection[agents[i].DiagnosticId], state.Position[i].normalized).magnitude,
                        Mathf.Clamp(Vector3.Dot(previousDirection[agents[i].DiagnosticId], state.Position[i].normalized), -1f, 1f));
                    r.actualHorizontalKm += angle * grid.PhysicalOceanSurfaceRadiusKm;
                    bool core = state.GeodesicCellIndex[i] == ventCell && state.CurrentOceanLayerIndex[i] == Bottom;
                    r.organismSeconds += r.deltaSeconds; if (core) r.ventBottomSeconds += r.deltaSeconds;
                    int node = grid.GetNodeIndex(state.GeodesicCellIndex[i], state.CurrentOceanLayerIndex[i]);
                    if (GeodesicBiologyRuntime.CalculateTemperaturePerformance(temperatures[node], state.OptimalTempMin[i], state.OptimalTempMax[i], state.LethalTempMargin[i]) == 0f)
                        r.thermallyUnsuitableSeconds += r.deltaSeconds;
                    long id = agents[i].DiagnosticId;
                    if (founders.Contains(id)) { if (!core) departed.Add(id); else if (departed.Contains(id)) returned.Add(id); }
                }
                // Explicit physical inventory/s source, identical for all clones. No normal source implementation is changed.
                int ventNode = grid.GetNodeIndex(ventCell, Bottom);
                concentrations[(int)GeodesicOceanResource.H2 * grid.NodeCapacity + ventNode] += r.h2SourceInventoryPerSecond * r.deltaSeconds / grid.PhysicalLayerVolumeKm3[ventNode];
                concentrations[(int)GeodesicOceanResource.CO2 * grid.NodeCapacity + ventNode] += r.co2SourceInventoryPerSecond * r.deltaSeconds / grid.PhysicalLayerVolumeKm3[ventNode];
                sourceCarbon += r.co2SourceInventoryPerSecond * r.deltaSeconds;
                float reactionDt = GeodesicBiologyRuntime.ConsumeFixedInterval(ref reactionClock, r.deltaSeconds, GeodesicBiologyRuntime.ReactionIntervalSeconds);
                if (reactionDt > 0f) React(reactionDt, r);
                Call("RunLifecycle", r.deltaSeconds, agents, state, .01f, .5f, true, .2f, .5f, 2f, .5f, 30000, null, false, false);
            }
            r.survivors = state.Count; r.departedFounders = departed.Count; r.returnedFounders = returned.Count;
            foreach (var agent in agents) if (founders.Contains(agent.DiagnosticId)) r.survivingFounders++;
            r.survivingDescendants = r.survivors - r.survivingFounders;
            r.returnedFounderFraction = departed.Count > 0 ? (double)returned.Count / departed.Count : 0d;
            r.ventResidenceFraction = r.ventBottomSeconds / r.organismSeconds; r.unsuitableFraction = r.thermallyUnsuitableSeconds / r.organismSeconds;
            r.activeAttemptedKm = (double)Get(runtime, "activeDistanceKm"); r.movementEnergy = (double)Get(runtime, "locomotionEnergyPaid");
            r.metabolicEnergy = ((double[])Get(runtime, "energyByMetabolism"))[(int)MetabolismType.Hydrogenotrophy];
            r.births = (long)Get(runtime, "births"); r.deaths = (long)Get(runtime, "deaths");
            r.activeCrossings = (long)Get(runtime, "activeCellCrossings"); r.activeLayerTransitions = (long)Get(runtime, "activeLayerTransitions");
            r.allCellCrossings = (long)Get(runtime, "horizontalBoundaryCrossings"); r.allLayerTransitions = (long)Get(runtime, "verticalTransitions");
            r.improving = (long)Get(runtime, "suitabilityImproving"); r.worsening = (long)Get(runtime, "suitabilityWorsening");
            r.neutral = (long)Get(runtime, "suitabilityNeutral"); r.tumbles = (long)Get(runtime, "activeTumbles");
            r.meanChosenTumbleProbabilityByTrend = new double[3];
            var comparisons = (long[])Get(runtime, "trendSenseCounts"); var probabilities = (double[])Get(runtime, "trendTumbleProbability");
            for (int i = 0; i < 3; i++) r.meanChosenTumbleProbabilityByTrend[i] = comparisons[i] > 0 ? probabilities[i] / comparisons[i] : 0d;
            r.carbonBalanceResidual = TotalCarbon() - initialCarbon - sourceCarbon;
            return r;
        }

        private void React(float dt, Report report)
        {
            Call("EnsureRequestCapacity", state.Count); Call("BeginSparseStep"); Call("SampleOccupiedHabitats", state.Count, state);
            for (int i = 0; i < state.Count; i++) Call("BuildRequest", i, state, dt, .01f, .02f, 8f, .8f, false, 0f, 1f, 1f,
                0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 6f);
            Call("AggregateDemands", state.Count); Call("ResolveAvailabilityFactors"); Call("ResolveCompetition", state.Count);
            double before = 0d; for (int i = 0; i < state.Count; i++) before += state.OrganicCStore[i];
            Call("CommitRequests", state.Count, state, 6f, false);
            double after = 0d; for (int i = 0; i < state.Count; i++) after += state.OrganicCStore[i];
            report.carbonAssimilated += after - before;
        }

        private struct Spatial { public Vector3 position, direction, tangent; public int cell, layer, preferred; public float radius; }
        private Spatial[] CapturePositions()
        {
            var result = new Spatial[state.Count];
            for (int i = 0; i < result.Length; i++) result[i] = new Spatial { position = state.Position[i], direction = state.PassiveDriftDirection[i],
                tangent = state.PassiveDriftTangent[i], cell = state.GeodesicCellIndex[i], layer = state.CurrentOceanLayerIndex[i], preferred = state.PreferredOceanLayerIndex[i], radius = state.PassiveVisualRadius[i] };
            return result;
        }
        private void RestorePositions(Spatial[] before)
        {
            for (int i = 0; i < before.Length; i++)
            {
                state.Position[i] = before[i].position; state.PassiveDriftDirection[i] = before[i].direction; state.PassiveDriftTangent[i] = before[i].tangent;
                state.GeodesicCellIndex[i] = before[i].cell; state.CurrentOceanLayerIndex[i] = before[i].layer;
                state.PreferredOceanLayerIndex[i] = before[i].preferred; state.PassiveVisualRadius[i] = before[i].radius;
            }
        }
        private void DisplaceFounders()
        {
            for (int i = 0; i < state.Count; i++) if (founders.Contains(agents[i].DiagnosticId))
            {
                state.GeodesicCellIndex[i] = coldNeighbor; state.CurrentOceanLayerIndex[i] = state.PreferredOceanLayerIndex[i] = Bottom;
                state.Position[i] = topology.CellDirections[coldNeighbor] * grid.LayerCenterRadius[grid.GetNodeIndex(coldNeighbor, Bottom)];
                state.PassiveDriftDirection[i] = state.PassiveDriftTangent[i] = Vector3.zero; state.PassiveVisualRadius[i] = 0f;
                // Retain temporal memory: this is displacement of an existing swimmer, not a fresh founder.
                departed.Add(agents[i].DiagnosticId);
            }
        }
        private double TotalCarbon()
        {
            double total = 0d;
            for (int node = 0; node < grid.NodeCapacity; node++) total += (concentrations[(int)GeodesicOceanResource.CO2 * grid.NodeCapacity + node]
                + concentrations[(int)GeodesicOceanResource.OrganicC * grid.NodeCapacity + node]) * grid.PhysicalLayerVolumeKm3[node];
            for (int i = 0; i < state.Count; i++) total += state.OrganicCStore[i];
            return total;
        }
        private void Call(string name, params object[] values)
        { if (!methods.TryGetValue(name, out MethodInfo method)) methods.Add(name, method = typeof(GeodesicBiologyRuntime).GetMethod(name, Private)); method.Invoke(runtime, values); }
        public void Dispose() => UnityEngine.Object.DestroyImmediate(root);
    }

    private static ReplicatorSteeringSystem.Settings Steering() => new ReplicatorSteeringSystem.Settings {
        ActiveMovementMetabolismSuitabilityEnabled = true, BaseTumbleProbability = .2f, MinTumbleProbability = .02f, MaxTumbleProbability = .9f,
        MovementTumbleChanceWhenBetter = .05f, MovementTumbleChanceWhenWorse = .65f, MovementTumbleChanceNeutral = .2f,
        MovementSuitabilityMemoryBlend = .35f, MovementSuitabilityImprovementThreshold = .05f, MovementSuitabilityWorseningThreshold = .05f,
        AmoeboidSenseInterval = .5f, FlagellumSenseInterval = .2f, AmoeboidSenseIntervalJitter = .15f, FlagellumSenseIntervalJitter = .05f,
        AmoeboidTurnAngleMax = 60f, FlagellumTurnAngleMax = 180f, AmoeboidRunNoiseStrength = .08f };
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
}
