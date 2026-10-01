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
        bool[] mapped = GeodesicHydrologyMapping.MapAuthoritativeOcean(mapping, simulationOcean, surface, 8f);
        for (int vertex = 0; vertex < mapped.Length; vertex++)
            Assert.That(mapped[vertex], Is.EqualTo(mapping.Samples[vertex].NearestCell == 0));
        int retained = Array.FindIndex(mapped, x => x);
        Assert.That(retained, Is.GreaterThanOrEqualTo(0));
        surface[retained] = geometry.UnitVertices[retained] * 8.1f;
        mapped = GeodesicHydrologyMapping.MapAuthoritativeOcean(mapping, simulationOcean, surface, 8f);
        Assert.That(mapped[retained], Is.False);
        simulationOcean[0] = false;
        Assert.That(GeodesicHydrologyMapping.MapAuthoritativeOcean(mapping, simulationOcean, surface, 8f).Any(x => x), Is.False);
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
    public void DirectHydrologyReachUsesTheActualGraphEdgeWithoutCorridorRefinement()
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
            12, 7, .35f, 0f, false, 8f, null, true);
        Assert.That(plan.Failure, Is.EqualTo(GeodesicRiverReachFailure.None));
        Assert.That(plan.Path, Is.EqualTo(new[] { topology.CellDirections[source], topology.CellDirections[receiver] }));
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
            Assert.That(mapped[vertex], Is.EqualTo(connectivity.OceanMask[mapping.Samples[vertex].NearestCell]));
        Assert.That(Enumerable.Range(0, mapped.Length).Any(x => mapping.Samples[x].NearestCell == excluded && !mapped[x]), Is.True);
        Assert.That(mapped.Any(x => x), Is.True);
    }

    [Test]
    public void ExplicitLegacyRoutingSwitchMatchesTheExistingDefaultPath()
    {
        var topology = GeodesicGridTopology.Build(2);
        var geometry = IcosphereRenderMeshBuilder.BuildUnitGeometry(2);
        var heights = topology.CellDirections.Select(x => 8f + x.y * .1f).ToArray();
        var ocean = topology.CellDirections.Select(x => x.y < -.8f).ToArray();
        var graph = GeodesicDrainageGraph.Build(topology, heights, ocean, 8f, 8f);
        var surface = geometry.UnitVertices.Select((x, i) => x * heights[i]).ToArray();
        var terrain = new GeodesicRiverTerrain(geometry, surface, heights);
        int source = Enumerable.Range(0, graph.CellCount).First(x => !ocean[x] && graph.DrainageReceiver[x] >= 0);
        var defaultPlan = GeodesicLakeRiverRouting.Build(source, graph, topology.CellDirections, terrain,
            null, null, 12, 7, .35f, .000002f, true, 8f, null);
        var explicitLegacy = GeodesicLakeRiverRouting.Build(source, graph, topology.CellDirections, terrain,
            null, null, 12, 7, .35f, .000002f, true, 8f, null, false);
        Assert.That(explicitLegacy.Failure, Is.EqualTo(defaultPlan.Failure));
        Assert.That(explicitLegacy.Path, Is.EqualTo(defaultPlan.Path));
    }

    private static bool IsNeighbor(IGeodesicHydrologyTopology topology, int a, int b)
    {
        for (int slot = 0; slot < topology.NeighborCounts[a]; slot++)
            if (topology.Neighbors6[a * 6 + slot] == b) return true;
        return false;
    }
}
