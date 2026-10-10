using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicHydrologyTopologyTests
{
    [Test]
    public void RenderGeometryBuildsDeterministicPackedAdjacencyWithoutFullTopology()
    {
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(4);
        int fullBuilds = GeodesicGridTopology.BuildInvocationCount;
        var first = GeodesicHydrologyTopology.Build(geometry);
        var second = GeodesicHydrologyTopology.Build(geometry);
        Assert.That(GeodesicGridTopology.BuildInvocationCount, Is.EqualTo(fullBuilds));
        Assert.That(first.CellDirections, Is.SameAs(geometry.UnitVertices));
        Assert.That(first.Triangles, Is.SameAs(geometry.Triangles));
        Assert.That(first.PentagonCount, Is.EqualTo(12));
        Assert.That(first.NeighborCounts.Count(x => x == 5), Is.EqualTo(12));
        Assert.That(first.NeighborCounts.Count(x => x == 6), Is.EqualTo(first.CellCount - 12));
        CollectionAssert.AreEqual(first.NeighborCounts, second.NeighborCounts);
        CollectionAssert.AreEqual(first.Neighbors6, second.Neighbors6);
        CollectionAssert.AreEqual(first.UnitCellAreas, second.UnitCellAreas);
    }

    [Test]
    public void HydrologyAreasCoverUnitSphereAndEveryEdgeIsReciprocal()
    {
        var topology = GeodesicHydrologyTopology.Build(IcosphereRenderMeshBuilder.BuildUnitGeometry(4));
        Assert.That(topology.UnitCellAreas.Sum(x => (double)x), Is.EqualTo(4d * Math.PI).Within(2e-5));
        for (int cell = 0; cell < topology.CellCount; cell++)
        for (int slot = 0; slot < topology.NeighborCounts[cell]; slot++)
        {
            int neighbor = topology.Neighbors6[cell * 6 + slot];
            Assert.That(IsNeighbor(topology, neighbor, cell), Is.True);
            Assert.That(topology.NeighborAngularDistance(cell, slot), Is.GreaterThan(0f));
        }
    }

    [Test]
    public void ExistingHydrologicalRadiiAreSharedByTerrainAndDrainage()
    {
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(2);
        var topology = GeodesicHydrologyTopology.Build(geometry);
        var radii = Enumerable.Repeat(8f, geometry.VertexCount).ToArray();
        var surface = geometry.UnitVertices.Select(x => x * 8.01f).ToArray();
        Assert.That(GeodesicHydrologyMapping.RequireHydrologicalRadii(geometry, radii), Is.SameAs(radii));
        var terrain = new GeodesicRiverTerrain(geometry, surface, radii, true);
        var ocean = new bool[radii.Length]; ocean[0] = true;
        var graph = GeodesicDrainageGraph.Build(topology, radii, ocean, 8f, 8f, null, true);
        Assert.That(terrain.UsesSharedHydrologicalRadii, Is.True);
        Assert.That(graph.UsesSharedHydrologicalElevation, Is.True);
        Assert.That(graph.HydrologicalElevation, Is.SameAs(radii));
    }

    [Test]
    public void MappedOceanRequiresBothSimulationAuthorityAndVisibleSubmergence()
    {
        var simulation = GeodesicGridTopology.Build(1);
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(2);
        var mapping = IcosphereDirectionMappingBuilder.Build(simulation, geometry);
        var simulationOcean = new bool[simulation.CellCount]; simulationOcean[0] = true;
        var surface = geometry.UnitVertices.Select(x => x * 7.9f).ToArray();
        bool[] mapped = GeodesicHydrologyMapping.MapAuthoritativeOcean(mapping, simulationOcean, surface, 8f, out var report);
        for (int vertex = 0; vertex < mapped.Length; vertex++)
            Assert.That(mapped[vertex], Is.EqualTo(HasAuthority(vertex)));
        Assert.That(report.MappedOceanVertices, Is.EqualTo(mapped.Count(x => x)));
        Assert.That(report.AuthorityDisagreementVertices, Is.GreaterThan(0));
        int retained = Array.FindIndex(mapped, x => x);
        Assert.That(retained, Is.GreaterThanOrEqualTo(0));
        surface[retained] = geometry.UnitVertices[retained] * 8.1f;
        mapped = GeodesicHydrologyMapping.MapAuthoritativeOcean(mapping, simulationOcean, surface, 8f);
        Assert.That(mapped[retained], Is.False);
        simulationOcean[0] = false;
        Assert.That(GeodesicHydrologyMapping.MapAuthoritativeOcean(mapping, simulationOcean, surface, 8f).Any(x => x), Is.False);

        bool HasAuthority(int vertex)
        {
            var sample = mapping.Samples[vertex];
            if (sample.NearestCell == 0) return true;
            for (int entry = sample.NeighborStart; entry < sample.NeighborStart + sample.NeighborCount; entry++)
                if (mapping.NeighborIndices[entry] == 0) return true;
            return false;
        }
    }

    [Test]
    public void SimulationRunoffMappingConservesEveryCoarseCellIntegral()
    {
        var simulation = GeodesicGridTopology.Build(1);
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(2);
        var mapping = IcosphereDirectionMappingBuilder.Build(simulation, geometry);
        var topology = GeodesicHydrologyTopology.Build(geometry);
        var areas = topology.UnitCellAreas.Select(x => (double)x).ToArray();
        var ocean = new bool[topology.CellCount];
        var supplied = Enumerable.Range(0, simulation.CellCount).Select(i => i * .25d).ToArray();
        double[] mapped = GeodesicHydrologyMapping.DistributeSimulationRunoff(mapping, supplied, areas, ocean);
        var recovered = new double[supplied.Length];
        for (int vertex = 0; vertex < mapped.Length; vertex++) recovered[mapping.Samples[vertex].NearestCell] += mapped[vertex];
        for (int cell = 0; cell < supplied.Length; cell++) Assert.That(recovered[cell], Is.EqualTo(supplied[cell]).Within(1e-10));
    }

    [Test]
    public void PhysicalThresholdUsesSimulationMeanAreaAtEitherHydrologyResolution()
    {
        const double radius = 8d, configured = 8d;
        double simulationMeanArea = 4d * Math.PI * radius * radius / GeodesicGridTopology.ExpectedCellCount(6);
        double resolved = configured * simulationMeanArea;
        double equivalentHighResolutionCells = resolved /
            (4d * Math.PI * radius * radius / GeodesicGridTopology.ExpectedCellCount(7));
        Assert.That(equivalentHighResolutionCells, Is.EqualTo(configured *
            GeodesicGridTopology.ExpectedCellCount(7) / GeodesicGridTopology.ExpectedCellCount(6)).Within(1e-10));
        Assert.That(equivalentHighResolutionCells, Is.EqualTo(32d).Within(.01d));
    }

    [Test]
    public void RenderResolutionReachStillRequiresTerrainRefinement()
    {
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(1);
        var topology = GeodesicHydrologyTopology.Build(geometry);
        var heights = Enumerable.Repeat(8f, topology.CellCount).ToArray();
        var ocean = new bool[topology.CellCount]; ocean[0] = true;
        var graph = GeodesicDrainageGraph.Build(topology, heights, ocean, 8f, 8f);
        var surface = geometry.UnitVertices.Select(x => x * 8f).ToArray();
        var terrain = new GeodesicRiverTerrain(geometry, surface, heights, true);
        int source = Enumerable.Range(1, graph.CellCount - 1).First(x => graph.DrainageReceiver[x] >= 0 && !graph.Ocean[graph.DrainageReceiver[x]]);
        int receiver = graph.DrainageReceiver[source];
        var plan = GeodesicLakeRiverRouting.Build(source, graph, topology.CellDirections, terrain, null, null,
            12, 7, .35f, .000002f, false, 8f, null);
        Assert.That(plan.Failure, Is.EqualTo(GeodesicRiverReachFailure.None));
        Assert.That(plan.Path.Length, Is.GreaterThan(2));
        Assert.That(plan.Path[0], Is.EqualTo(topology.CellDirections[source]));
        Assert.That(plan.Path.Last(), Is.EqualTo(topology.CellDirections[receiver]));
        Assert.That(GeodesicRiverPath.IsPathDownhill(plan.Path, terrain.VisibleHeight, .000002f), Is.True);
        Assert.That(plan.GradeObservation.Stage, Is.EqualTo(GeodesicRiverGradeStage.Complete));
    }

    [Test]
    public void HighResolutionPriorityFloodProducesNeighborRoutesAndMonotonicFlow()
    {
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(3);
        var topology = GeodesicHydrologyTopology.Build(geometry);
        var heights = geometry.UnitVertices.Select(d => 8f + d.y * .1f).ToArray();
        var ocean = geometry.UnitVertices.Select(d => d.y < -.9f).ToArray();
        var graph = GeodesicDrainageGraph.Build(topology, heights, ocean, 8f, 8f, null, true);
        for (int cell = 0; cell < graph.CellCount; cell++)
        {
            int receiver = graph.DrainageReceiver[cell]; if (receiver < 0) continue;
            Assert.That(IsNeighbor(topology, cell, receiver), Is.True);
            Assert.That(graph.DrainageArea[receiver] + 1e-12, Is.GreaterThanOrEqualTo(graph.DrainageArea[cell]));
            Assert.That(graph.AccumulatedRunoff[receiver] + 1e-12, Is.GreaterThanOrEqualTo(graph.AccumulatedRunoff[cell]));
        }
    }

    [Test]
    public void HighResolutionLakeUsesIdentityCellsAndAValidSpillEdge()
    {
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(3);
        var topology = GeodesicHydrologyTopology.Build(geometry);
        float Height(Vector3 direction)
        {
            if (direction.z < -.3f) return 7.9f;
            float angle = Mathf.Acos(Mathf.Clamp(direction.z, -1f, 1f));
            return angle < .4f ? Mathf.Lerp(8.05f, 8.2f, angle * angle / .16f) : 8.2f;
        }
        var heights = geometry.UnitVertices.Select(Height).ToArray();
        var ocean = geometry.UnitVertices.Select(x => x.z < -.3f).ToArray();
        var graph = GeodesicDrainageGraph.Build(topology, heights, ocean, 8f, 8f, null, true);
        var lakes = GeodesicLakeBasins.Build(topology, graph, 8f, true, .0001f, .005f);
        var surface = geometry.UnitVertices.Select(x => x * Height(x)).ToArray();
        var terrain = new GeodesicRiverTerrain(geometry, surface, heights, true);
        var water = GeodesicLakeGeometry.Build(topology, graph, lakes, geometry, surface, terrain, 8f, .0001f);
        GeodesicLakeBasin basin = lakes.Basins.Single(x => x.Selected);
        Assert.That(water.Triangles, Is.Not.Empty);
        Assert.That(IsNeighbor(topology, basin.SpillFromCell, basin.SpillCell), Is.True);
        int[] receivers = lakes.CreateReceivers(topology, graph);
        Assert.That(receivers[basin.SpillFromCell], Is.EqualTo(basin.SpillCell));
        graph.ApplyLakeReceivers(receivers);
        double maximumIncoming = 0d;
        for (int cell = 0; cell < graph.CellCount; cell++)
        {
            int receiver = graph.DrainageReceiver[cell];
            if (receiver >= 0 && lakes.BasinId[cell] != basin.Id && lakes.BasinId[receiver] == basin.Id)
                maximumIncoming = Math.Max(maximumIncoming, graph.AccumulatedRunoff[cell]);
        }
        Assert.That(graph.AccumulatedRunoff[basin.SpillFromCell] + 1e-12,
            Is.GreaterThanOrEqualTo(maximumIncoming));
    }

    [Test]
    public void MappedHighResolutionOceanPreservesRetainedAndExcludedSimulationAuthority()
    {
        var simulation = GeodesicGridTopology.Build(2);
        var raw = Enumerable.Repeat(11f, simulation.CellCount).ToArray();
        for (int cell = 0; cell < raw.Length; cell++)
            if (Math.Abs(simulation.CellDirections[cell].y) > .6f) raw[cell] = 9f;
        int excluded = Enumerable.Range(0, simulation.CellCount)
            .Where(x => Math.Abs(simulation.CellDirections[x].y) <= .6f)
            .OrderByDescending(x => simulation.CellDirections[x].x).First();
        raw[excluded] = 9f;
        var connectivity = GeodesicOceanConnectivity.Build(simulation, raw, 10f, true, true, .02f);
        Assert.That(connectivity.OceanMask[excluded], Is.False);
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(3);
        var mapping = IcosphereDirectionMappingBuilder.Build(simulation, geometry);
        var submerged = geometry.UnitVertices.Select(x => x * 9f).ToArray();
        bool[] mapped = GeodesicHydrologyMapping.MapAuthoritativeOcean(mapping, connectivity.OceanMask, submerged, 10f);
        for (int vertex = 0; vertex < mapped.Length; vertex++)
            Assert.That(mapped[vertex], Is.EqualTo(HasRetainedAuthority(vertex)));
        Assert.That(Enumerable.Range(0, mapped.Length).Any(x => mapping.Samples[x].NearestCell == excluded && !mapped[x]), Is.True);
        Assert.That(mapped.Any(x => x), Is.True);

        bool HasRetainedAuthority(int vertex)
        {
            var sample = mapping.Samples[vertex];
            if (connectivity.OceanMask[sample.NearestCell]) return true;
            for (int entry = sample.NeighborStart; entry < sample.NeighborStart + sample.NeighborCount; entry++)
                if (connectivity.OceanMask[mapping.NeighborIndices[entry]]) return true;
            return false;
        }
    }

    [Test]
    public void SharedValleyAnchorsPreserveConfluencesAndSmoothedEdgeEndpoints()
    {
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(3);
        var topology = GeodesicHydrologyTopology.Build(geometry);
        var hydrology = geometry.UnitVertices.Select(x => 8f + x.y * .1f).ToArray();
        var ocean = geometry.UnitVertices.Select(x => x.y < -.8f).ToArray();
        var graph = GeodesicDrainageGraph.Build(topology, hydrology, ocean, 8f, 8f, null, true);
        int raised = Enumerable.Range(0, graph.CellCount).First(x => !ocean[x] && graph.DrainageReceiver[x] >= 0 &&
            !ocean[graph.DrainageReceiver[x]]);
        var visibleRadii = (float[])hydrology.Clone(); visibleRadii[raised] += .03f;
        var surface = geometry.UnitVertices.Select((x, i) => x * visibleRadii[i]).ToArray();
        var terrain = new GeodesicRiverTerrain(geometry, surface, hydrology, true);
        var anchors = GeodesicRiverVisualPath.BuildSharedAnchors(topology, graph, terrain, null, 0d, .000002f, out var diagnostics);
        Assert.That(diagnostics.EligibleAnchors, Is.GreaterThan(0));
        Assert.That(diagnostics.UnadjustedAnchors + diagnostics.OneRingAdjustments + diagnostics.TwoRingAdjustments,
            Is.EqualTo(diagnostics.EligibleAnchors));
        Assert.That((anchors[raised] - topology.CellDirections[raised]).magnitude, Is.GreaterThan(1e-6f));
        Vector3[] path = GeodesicRiverVisualPath.SmoothEdge(raised, topology, graph, anchors, terrain, .000002f, diagnostics);
        Assert.That(path.First(), Is.EqualTo(anchors[raised]));
        Assert.That(path.Last(), Is.EqualTo(anchors[graph.DrainageReceiver[raised]]));
        Assert.That(path.Length, Is.EqualTo(5));

        int confluence = Enumerable.Range(0, graph.CellCount).FirstOrDefault(cell =>
            Enumerable.Range(0, topology.NeighborCounts[cell]).Count(slot =>
                graph.DrainageReceiver[topology.Neighbors6[cell * 6 + slot]] == cell) >= 2);
        int[] tributaries = Enumerable.Range(0, topology.NeighborCounts[confluence])
            .Select(slot => topology.Neighbors6[confluence * 6 + slot])
            .Where(cell => graph.DrainageReceiver[cell] == confluence).Take(2).ToArray();
        Assert.That(tributaries.Length, Is.EqualTo(2));
        foreach (int tributary in tributaries)
            Assert.That(GeodesicRiverVisualPath.SmoothEdge(tributary, topology, graph, anchors, terrain, .000002f, diagnostics).Last(),
                Is.EqualTo(anchors[confluence]));
    }

    [Test]
    public void RestoredBaselineKeepsLargeScaleRoutingIndependentOfVisibleDetail()
    {
        var topology = GeodesicGridTopology.Build(2);
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(2);
        var heights = topology.CellDirections.Select(x => 8f + x.y * .1f).ToArray();
        var ocean = topology.CellDirections.Select(x => x.y < -.8f).ToArray();
        var graph = GeodesicDrainageGraph.Build(topology, heights, ocean, 8f, 8f);
        var surface = geometry.UnitVertices.Select((x, i) => x * heights[i]).ToArray();
        var terrain = new GeodesicRiverTerrain(geometry, surface, heights);
        int source = Enumerable.Range(0, graph.CellCount).First(x => !ocean[x] && graph.DrainageReceiver[x] >= 0);
        int receiver = graph.DrainageReceiver[source];
        var before = GeodesicLakeRiverRouting.Build(source, graph, topology.CellDirections, terrain,
            null, null, 12, 7, .35f, .000002f, false, 8f, null);
        surface[receiver] = topology.CellDirections[receiver] * (heights[source] + .05f);
        terrain = new GeodesicRiverTerrain(geometry, surface, heights);
        Assert.That(graph.FilledElevation[receiver], Is.LessThanOrEqualTo(graph.FilledElevation[source]));
        var plan = GeodesicLakeRiverRouting.Build(source, graph, topology.CellDirections, terrain,
            null, null, 12, 7, .35f, .000002f, false, 8f, null);
        // 6f48085 deliberately grades ordinary corridors against large-scale terrain.
        // The later visible-height gate was a behavior change, not a baseline invariant.
        Assert.That(before.Path.Length, Is.GreaterThan(1));
        CollectionAssert.AreEqual(before.Path, plan.Path);
        Assert.That(plan.GradeObservation.VisibleHeight, Is.False);
        Assert.That(terrain.Radius(topology.CellDirections[receiver]),
            Is.EqualTo(surface[receiver].magnitude).Within(.00001f), "Projection must still use the final mesh.");
    }

    [Test]
    public void TerrainCorridorsKeepJunctionsAndEveryAcceptedPathDescends()
    {
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(3);
        var topology = GeodesicHydrologyTopology.Build(geometry);
        var heights = topology.CellDirections.Select(d => 8f + d.y * .1f).ToArray();
        var surface = topology.CellDirections.Select((d, i) => d * heights[i]).ToArray();
        var ocean = topology.CellDirections.Select(d => d.y < -.8f).ToArray();
        var graph = GeodesicDrainageGraph.Build(topology, heights, ocean, 7.92f, 8f);
        var terrain = new GeodesicRiverTerrain(geometry, surface);
        var anchors = GeodesicRiverPath.BuildTerrainAnchors(topology, graph, terrain, null, 0d, .000002f);
        var incoming = new int[graph.CellCount];
        foreach (int receiver in graph.DrainageReceiver) if (receiver >= 0) incoming[receiver]++;
        int accepted = 0, moved = 0;
        for (int cell = 0; cell < graph.CellCount; cell++)
        {
            int receiver = graph.DrainageReceiver[cell];
            if (incoming[cell] != 1 || ocean[cell] || receiver < 0 || ocean[receiver])
                Assert.That(anchors[cell], Is.EqualTo(topology.CellDirections[cell]));
            if (anchors[cell] != topology.CellDirections[cell]) moved++;
            if (ocean[cell] || receiver < 0) continue;
            var plan = GeodesicLakeRiverRouting.Build(cell, graph, anchors, terrain, null, null,
                12, 7, .35f, .000002f, true, 7.92f, null);
            if (plan.Path.Length < 2) continue;
            accepted++;
            float minimum = terrain.VisibleHeight(plan.Path[0]);
            for (int p = 1; p < plan.Path.Length; p++)
            for (int sample = 1; sample <= 31; sample++)
            {
                float height = terrain.VisibleHeight(Vector3.Lerp(plan.Path[p - 1], plan.Path[p], sample / 31f).normalized);
                Assert.That(height - minimum, Is.LessThanOrEqualTo(.000002f));
                minimum = Mathf.Min(minimum, height);
            }
        }
        Assert.That(accepted, Is.GreaterThan(100), "Rejecting the entire network is not a routing fix.");
        Assert.That(moved, Is.GreaterThan(0), "Internal controls must be allowed to leave raw graph vertices.");
    }

    private static bool IsNeighbor(IGeodesicHydrologyTopology topology, int a, int b)
    {
        for (int slot = 0; slot < topology.NeighborCounts[a]; slot++)
            if (topology.Neighbors6[a * 6 + slot] == b) return true;
        return false;
    }
}
