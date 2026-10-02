using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

/// <summary>Deterministic, non-scene A/B benchmark for the hydrology migration.</summary>
public static class GeodesicHydrologyBenchmark
{
    public sealed class Result
    {
        public string Mode;
        public int SimulationSubdivision, RenderSubdivision, HydrologySubdivision;
        public int HydrologyNodes, CandidateRiverReaches, VisibleRiverReaches, SuppressedReaches;
        public int LakeConnectedReaches, InlandVisibleTerminations, OceanConnectedChains;
        public int FilledHydrologyCells, CandidateBasins, VisibleLakes, LakeInlets;
        public int ExpectedLakeOutlets, RenderedLakeOutlets, SuppressedLakeOutlets;
        public int LakeOutletBelowThreshold, LakeOutletProjectionFailure, LakeOutletShorelineFailure, LakeOutletTopologyFailure;
        public int ProjectionFailures, CorridorFailures, UnresolvedDepressions, TrueTopologyFailures;
        public int RiverMeshVertexCount, LakeMeshVertexCount, FullTopologyBuilds;
        public int MajorRiverReaches;
        public long RetainedMemoryBytes;
        public double AdjacencyMs, OceanMappingMs, PriorityFloodMs, FlowAccumulationMs;
        public double LakeTopologyMs, LakeGeometryMs, PathExtractionMs, ShorelineCoastMs, RibbonGeometryEstimateMs, TotalMs;
        public double ResolvedThresholdArea;
        public double TotalRenderedRiverLength;
        public GeodesicRiverVisualDiagnostics VisualDiagnostics;

        public override string ToString()
        {
            return $"mode={Mode} simulationSubdivision={SimulationSubdivision} renderSubdivision={RenderSubdivision} " +
                $"hydrologySubdivision={HydrologySubdivision} hydrologyNodes={HydrologyNodes} fullTopologyBuilds={FullTopologyBuilds} " +
                $"candidateRiverReaches={CandidateRiverReaches} visibleRiverReaches={VisibleRiverReaches} suppressedReaches={SuppressedReaches} " +
                $"lakeConnectedReaches={LakeConnectedReaches} inlandVisibleTerminations={InlandVisibleTerminations} oceanConnectedChains={OceanConnectedChains} " +
                $"filledHydrologyCells={FilledHydrologyCells} candidateBasins={CandidateBasins} visibleLakes={VisibleLakes} lakeInlets={LakeInlets} " +
                $"expectedLakeOutlets={ExpectedLakeOutlets} renderedLakeOutlets={RenderedLakeOutlets} suppressedLakeOutlets={SuppressedLakeOutlets} " +
                $"lakeOutletBelowThreshold={LakeOutletBelowThreshold} lakeOutletProjectionFailure={LakeOutletProjectionFailure} " +
                $"lakeOutletShorelineFailure={LakeOutletShorelineFailure} lakeOutletTopologyFailure={LakeOutletTopologyFailure} " +
                $"projectionFailures={ProjectionFailures} corridorFailures={CorridorFailures} unresolvedDepressions={UnresolvedDepressions} " +
                $"trueTopologyFailures={TrueTopologyFailures} riverMeshVertices={RiverMeshVertexCount} lakeMeshVertices={LakeMeshVertexCount} " +
                $"majorRiverReaches={MajorRiverReaches} totalRenderedRiverLength={TotalRenderedRiverLength:G9} " +
                $"thresholdArea={ResolvedThresholdArea:G9} retainedMemoryBytes={RetainedMemoryBytes} " +
                $"adjacencyMs={AdjacencyMs:F1} oceanMappingMs={OceanMappingMs:F1} priorityFloodMs={PriorityFloodMs:F1} " +
                $"flowAccumulationMs={FlowAccumulationMs:F1} lakeTopologyMs={LakeTopologyMs:F1} lakeGeometryMs={LakeGeometryMs:F1} " +
                $"pathExtractionMs={PathExtractionMs:F1} shorelineCoastMs={ShorelineCoastMs:F1} " +
                $"ribbonGeometryEstimateMs={RibbonGeometryEstimateMs:F1} valleySnapMs={(VisualDiagnostics != null ? VisualDiagnostics.ValleySnapMilliseconds : 0d):F1} smoothingMs={(VisualDiagnostics != null ? VisualDiagnostics.SmoothingMilliseconds : 0d):F1} totalMs={TotalMs:F1}" +
                (VisualDiagnostics != null ? $" anchors0={VisualDiagnostics.UnadjustedAnchors} anchors1={VisualDiagnostics.OneRingAdjustments} anchors2={VisualDiagnostics.TwoRingAdjustments} anchorsUnresolved={VisualDiagnostics.UnresolvedAdjustments} visibleUphillBefore={VisualDiagnostics.VisibleUphillBefore} visibleUphillAfter={VisualDiagnostics.VisibleUphillAfter} ridgeBefore={VisualDiagnostics.RidgeCrossingsBefore} ridgeAfter={VisualDiagnostics.RidgeCrossingsAfter}" : string.Empty);
        }
    }

    public static string Run(int seed = 123456, int simulationSubdivision = 6, int renderSubdivision = 7)
    {
        const float radius = 8f, sea = 8f, minimumLakeArea = .0001f, minimumLakeDepth = .005f;
        const double configuredThreshold = 8d;
        int buildStart = GeodesicGridTopology.BuildInvocationCount;
        var simulation = GeodesicGridTopology.Build(simulationSubdivision);
        int simulationBuilds = GeodesicGridTopology.BuildInvocationCount - buildStart;
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(renderSubdivision);
        var surface = new Vector3[geometry.VertexCount];
        var hydrologicalRadii = new float[geometry.VertexCount];
        PlanetTerrainSettings settings = PlanetTerrainSettings.Earthlike;
        for (int i = 0; i < geometry.VertexCount; i++)
        {
            PlanetTerrainSample sample = PlanetTerrainSampler.Evaluate(geometry.UnitVertices[i], seed, settings);
            surface[i] = geometry.UnitVertices[i] * (radius + sample.HeightOffset);
            hydrologicalRadii[i] = radius + sample.LargeScaleHeightOffset;
        }
        var terrain = new GeodesicRiverTerrain(geometry, surface, hydrologicalRadii, true);
        var simulationTerrain = new float[simulation.CellCount];
        for (int i = 0; i < simulationTerrain.Length; i++) simulationTerrain[i] = surface[i].magnitude;
        GeodesicOceanConnectivity connectivity = GeodesicOceanConnectivity.Build(simulation, simulationTerrain,
            sea, true, true, .001f);
        IcosphereDirectionMapping mapping = IcosphereDirectionMappingBuilder.Build(simulation, geometry);
        double threshold = configuredThreshold * (4d * Math.PI * radius * radius / simulation.CellCount);

        bool SimulationOcean(Vector3 direction)
        {
            int cell = GeodesicOceanConnectivity.FindNearestCell(simulation, direction);
            return cell >= 0 && connectivity.OceanMask[cell];
        }

        Result legacy = Build(false, false);
        Result dedicated = Build(true, false);
        Result corrected = Build(true, true);
        return $"HYDROLOGY A/B seed={seed} sharedSimulationCells={simulation.CellCount} sharedRenderVertices={geometry.VertexCount} " +
            $"simulationTopologyBuilds={simulationBuilds} configuredThresholdSimulationCells={configuredThreshold:G6}\n" +
            legacy + "\n" + dedicated + "\n" + corrected;

        Result Build(bool dedicated, bool corrected)
        {
            var total = Stopwatch.StartNew();
            int fullBefore = GeodesicGridTopology.BuildInvocationCount;
            IGeodesicHydrologyTopology topology;
            var stage = Stopwatch.StartNew();
            if (dedicated) topology = GeodesicHydrologyTopology.Build(geometry);
            else topology = simulation;
            stage.Stop();
            double adjacencyMs = dedicated ? stage.Elapsed.TotalMilliseconds : 0d;
            Vector3[] anchors;
            float[] elevations;
            bool[] ocean;
            Func<int, int, float> edgeSpill = null;
            stage.Restart();
            if (dedicated)
            {
                anchors = topology.CellDirections;
                elevations = hydrologicalRadii;
                ocean = corrected ? GeodesicHydrologyMapping.MapAuthoritativeOcean(mapping, connectivity.OceanMask, surface, sea) :
                    Enumerable.Range(0, geometry.VertexCount).Select(vertex =>
                        connectivity.OceanMask[mapping.Samples[vertex].NearestCell] && surface[vertex].sqrMagnitude < sea * sea).ToArray();
            }
            else
            {
                int count = topology.CellCount;
                anchors = new Vector3[count]; elevations = new float[count]; ocean = (bool[])connectivity.OceanMask.Clone();
                for (int cell = 0; cell < count; cell++)
                {
                    Vector3 anchor = topology.CellDirections[cell];
                    float lowest = terrain.Height(anchor);
                    if (!ocean[cell])
                    {
                        for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
                        {
                            int neighbor = topology.Neighbors6[cell * 6 + slot];
                            Vector3 candidate = Vector3.Lerp(topology.CellDirections[cell], topology.CellDirections[neighbor], .2f).normalized;
                            float height = terrain.Height(candidate);
                            if (height < lowest && (!SimulationOcean(candidate) || terrain.VisibleHeight(candidate) > sea))
                            { lowest = height; anchor = candidate; }
                        }
                    }
                    anchors[cell] = anchor; elevations[cell] = lowest;
                }
                var spills = new Dictionary<ulong, float>();
                edgeSpill = (a, b) =>
                {
                    ulong key = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                    if (spills.TryGetValue(key, out float cached)) return cached;
                    float spill = float.NegativeInfinity;
                    for (int sample = 1; sample <= 3; sample++)
                        spill = Mathf.Max(spill, terrain.Height(Vector3.Lerp(anchors[a], anchors[b], sample / 4f).normalized));
                    spills.Add(key, spill); return spill;
                };
            }
            stage.Stop();
            double oceanMs = dedicated ? stage.Elapsed.TotalMilliseconds : 0d;
            GeodesicDrainageGraph graph = GeodesicDrainageGraph.Build(topology, elevations, ocean, sea, radius,
                edgeSpill, dedicated);
            stage.Restart();
            GeodesicLakeBasins lakes = GeodesicLakeBasins.Build(topology, graph, radius, true,
                minimumLakeArea, minimumLakeDepth, edgeSpill);
            stage.Stop(); double lakeTopologyMs = stage.Elapsed.TotalMilliseconds;
            stage.Restart();
            GeodesicLakeGeometry water = GeodesicLakeGeometry.Build(topology, graph, lakes, geometry, surface,
                terrain, radius, minimumLakeArea);
            stage.Stop(); double lakeGeometryMs = stage.Elapsed.TotalMilliseconds;
            graph.ApplyLakeReceivers(lakes.CreateReceivers(topology, graph, edgeSpill));
            GeodesicRiverVisualDiagnostics visualDiagnostics = null;
            if (corrected)
                anchors = GeodesicRiverVisualPath.BuildSharedAnchors(topology, graph, terrain, lakes, threshold,
                    .000002f, out visualDiagnostics);

            stage.Restart();
            var plans = new GeodesicRiverReachPlan[topology.CellCount];
            var incoming = new bool[topology.CellCount];
            var maximumLakeInflow = new double[lakes.Basins.Length];
            int candidates = 0, visible = 0, major = 0, lakeConnected = 0, lakeInlets = 0;
            int projection = 0, corridor = 0, unresolved = 0, topologyFailures = 0, riverVertices = 0;
            double shorelineCoastMs = 0d;
            var renderedOutletBasins = new HashSet<int>();
            var visiblePaths = new List<Vector3[]>();
            double renderedLength = 0d;
            for (int cell = 0; cell < topology.CellCount; cell++)
            {
                int receiver = graph.DrainageReceiver[cell];
                if (ocean[cell] || receiver < 0 || graph.AccumulatedRunoff[cell] < threshold) continue;
                candidates++; incoming[receiver] = true;
                GeodesicRiverReachPlan plan = plans[cell] = GeodesicLakeRiverRouting.Build(cell, graph, anchors,
                    terrain, lakes, water, 12, 7, .35f, .000002f, true, sea, SimulationOcean, dedicated,
                    corrected ? topology : null, visualDiagnostics);
                shorelineCoastMs += plan.ShorelineCoastMilliseconds;
                if (plan.LakeConnected) lakeConnected++;
                if (plan.Failure == GeodesicRiverReachFailure.ProjectionMismatch) projection++;
                else if (plan.Failure == GeodesicRiverReachFailure.CorridorFailure) corridor++;
                else if (plan.Failure == GeodesicRiverReachFailure.UnresolvedDepression) unresolved++;
                else if (plan.Failure == GeodesicRiverReachFailure.TopologyFailure) topologyFailures++;
                if (plan.Path.Length < 2) continue;
                visible++;
                if (graph.AccumulatedRunoff[cell] >= threshold * 8d) major++;
                visiblePaths.Add(plan.Path);
                for (int point = 1; point < plan.Path.Length; point++)
                    renderedLength += radius * Mathf.Acos(Mathf.Clamp(Vector3.Dot(plan.Path[point - 1], plan.Path[point]), -1f, 1f));
                riverVertices += 2 * (3 * (plan.Path.Length - 1) + 1);
                if (plan.InletBasin >= 0)
                {
                    lakeInlets++; lakes.Basins[plan.InletBasin].IncomingRiverCount++;
                    maximumLakeInflow[plan.InletBasin] = Math.Max(maximumLakeInflow[plan.InletBasin], graph.AccumulatedRunoff[cell]);
                }
                if (plan.OutletBasin >= 0) renderedOutletBasins.Add(plan.OutletBasin);
            }
            stage.Stop(); double pathMs = stage.Elapsed.TotalMilliseconds;
            stage.Restart();
            // Exercise the same per-segment final-detail radius projection used by AddRibbon.
            foreach (Vector3[] path in visiblePaths)
            {
                for (int i = 1; i < path.Length; i++)
                    for (int sample = 0; sample < 3; sample++)
                        terrain.Radius(Vector3.Lerp(path[i - 1], path[i], sample / 3f).normalized);
                terrain.Radius(path[path.Length - 1]);
            }
            stage.Stop(); double ribbonMs = stage.Elapsed.TotalMilliseconds;

            int expectedOutlets = 0, suppressedOutlets = 0, belowThreshold = 0, outletProjection = 0;
            int outletShore = 0, outletTopology = 0;
            foreach (GeodesicLakeBasin basin in lakes.Basins)
            {
                if (!basin.Selected || basin.IncomingRiverCount <= 0) continue;
                expectedOutlets++;
                int outlet = basin.SpillFromCell;
                if (outlet < 0 || outlet >= graph.CellCount)
                { suppressedOutlets++; outletTopology++; continue; }
                if (graph.AccumulatedRunoff[outlet] < threshold)
                { suppressedOutlets++; belowThreshold++; continue; }
                GeodesicRiverReachPlan plan = plans[outlet];
                if (plan == null || plan.OutletBasin != basin.Id || plan.Path.Length < 2)
                {
                    suppressedOutlets++;
                    if (plan != null && plan.ShorelineFailure) outletShore++;
                    else if (plan != null && plan.Failure == GeodesicRiverReachFailure.TopologyFailure) outletTopology++;
                    else outletProjection++;
                }
            }

            var reachesOcean = new bool[topology.CellCount];
            int chains = 0, terminations = 0;
            for (int order = topology.CellCount - 1; order >= 0; order--)
            {
                int cell = graph.UpstreamToDownstream[order], receiver = graph.DrainageReceiver[cell];
                if (ocean[cell]) { reachesOcean[cell] = true; continue; }
                GeodesicRiverReachPlan plan = plans[cell];
                if (plan == null || receiver < 0) continue;
                bool shown = plan.Path.Length > 1;
                reachesOcean[cell] = (shown || plan.LakeConnected) && reachesOcean[receiver];
                if (reachesOcean[cell] && !incoming[cell]) chains++;
                if (shown && plan.InletBasin < 0 && !ocean[receiver] &&
                    (plans[receiver] == null || (plans[receiver].Path.Length < 2 && !plans[receiver].LakeConnected))) terminations++;
            }
            total.Stop();
            long pathBytes = plans.Where(x => x != null).Sum(x => (long)x.Path.Length * 12L);
            long retained = (dedicated ? topology.ApproximateHydrologyMemoryBytes : 0L) + graph.ApproximateMemoryBytes +
                (long)topology.CellCount * sizeof(float) + (long)lakes.BasinId.Length * (sizeof(int) + sizeof(float) + sizeof(byte)) +
                pathBytes + (long)water.Vertices.Length * 12L + (long)water.Triangles.Length * sizeof(int);
            return new Result
            {
                Mode = !dedicated ? "GOOD_VISUAL_OLD-legacy-simulation-6" : corrected ? "corrected-render-7" : "CURRENT_HIGHRES-raw-render-7",
                SimulationSubdivision = simulationSubdivision, RenderSubdivision = renderSubdivision,
                HydrologySubdivision = topology.SubdivisionLevel, HydrologyNodes = topology.CellCount,
                FullTopologyBuilds = GeodesicGridTopology.BuildInvocationCount - fullBefore,
                CandidateRiverReaches = candidates, VisibleRiverReaches = visible,
                SuppressedReaches = projection + corridor + unresolved + topologyFailures,
                LakeConnectedReaches = lakeConnected, InlandVisibleTerminations = terminations, OceanConnectedChains = chains,
                FilledHydrologyCells = graph.FilledCellCount, CandidateBasins = lakes.Basins.Length,
                VisibleLakes = lakes.Basins.Count(x => x.Selected), LakeInlets = lakeInlets,
                ExpectedLakeOutlets = expectedOutlets, RenderedLakeOutlets = renderedOutletBasins.Count,
                SuppressedLakeOutlets = suppressedOutlets, LakeOutletBelowThreshold = belowThreshold,
                LakeOutletProjectionFailure = outletProjection, LakeOutletShorelineFailure = outletShore,
                LakeOutletTopologyFailure = outletTopology, ProjectionFailures = projection, CorridorFailures = corridor,
                UnresolvedDepressions = unresolved, TrueTopologyFailures = topologyFailures,
                RiverMeshVertexCount = riverVertices, LakeMeshVertexCount = water.Vertices.Length,
                MajorRiverReaches = major, TotalRenderedRiverLength = renderedLength, VisualDiagnostics = visualDiagnostics,
                RetainedMemoryBytes = retained, ResolvedThresholdArea = threshold,
                AdjacencyMs = adjacencyMs, OceanMappingMs = oceanMs,
                PriorityFloodMs = graph.PriorityFloodMilliseconds, FlowAccumulationMs = graph.FlowAccumulationMilliseconds,
                LakeTopologyMs = lakeTopologyMs, LakeGeometryMs = lakeGeometryMs,
                PathExtractionMs = Math.Max(0d, pathMs - shorelineCoastMs),
                ShorelineCoastMs = shorelineCoastMs, RibbonGeometryEstimateMs = ribbonMs,
                TotalMs = total.Elapsed.TotalMilliseconds
            };
        }
    }
}
