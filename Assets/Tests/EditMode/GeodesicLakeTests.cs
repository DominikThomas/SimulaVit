using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicLakeTests
{
    private sealed class Fixture
    {
        public GeodesicGridTopology Topology;
        public GeodesicDrainageGraph Graph;
        public GeodesicLakeBasins Lakes;
        public GeodesicLakeGeometry Water;
        public IcosphereRenderGeometry Geometry;
        public Vector3[] Surface;
        public GeodesicRiverTerrain Terrain;
        public float[] Heights;
    }
    private static float Bowl(Vector3 d, bool belowSea = false)
    {
        if (d.z < -.3f) return 7.9f;
        float angle = Mathf.Acos(Mathf.Clamp(d.z, -1f, 1f));
        float floor = belowSea ? 7.95f : 8.05f;
        return angle < .4f ? Mathf.Lerp(floor, 8.2f, angle * angle / .16f) : 8.2f;
    }
    private static Fixture Create(int level = 3, bool belowSea = false, bool enabled = true, float area = 0.0001f, float depth = .005f, Func<Vector3, float> visual = null)
    {
        var f = new Fixture { Topology = GeodesicGridTopology.Build(level), Geometry = IcosphereRenderGeometryCache.GetOrBuild(level + 1) };
        f.Heights = f.Topology.CellDirections.Select(d => Bowl(d, belowSea)).ToArray();
        bool[] ocean = f.Topology.CellDirections.Select(d => d.z < -.3f).ToArray();
        f.Graph = GeodesicDrainageGraph.Build(f.Topology, f.Heights, ocean, 8f, 8f);
        f.Lakes = GeodesicLakeBasins.Build(f.Topology, f.Graph, 8f, enabled, area, depth);
        f.Surface = f.Geometry.UnitVertices.Select(d => d * (visual != null ? visual(d) : Bowl(d, belowSea))).ToArray();
        f.Terrain = new GeodesicRiverTerrain(f.Geometry, f.Surface);
        f.Water = GeodesicLakeGeometry.Build(f.Topology, f.Graph, f.Lakes, f.Geometry, f.Surface, f.Terrain, 8f, area);
        return f;
    }
    private static GeodesicLakeBasin Visible(Fixture f) => f.Lakes.Basins.Single(b => b.Selected);

    [Test]
    public void TinyShallowDepressionStaysVirtual()
    {
        var t = GeodesicGridTopology.Build(3);
        var h = Enumerable.Repeat(8.2f, t.CellCount).ToArray(); var ocean = new bool[h.Length]; ocean[0] = true; h[10] = 8.1999f;
        var g = GeodesicDrainageGraph.Build(t, h, ocean, 8f, 8f);
        var lakes = GeodesicLakeBasins.Build(t, g, 8f, true, .0001f, .005f);
        Assert.That(lakes.Basins.Length, Is.GreaterThan(0));
        Assert.That(lakes.Basins.Any(b => b.Selected), Is.False);
        Assert.That(g.FillDepth[10], Is.GreaterThan(0f));
    }

    [Test]
    public void AboveSeaBasinBecomesOneLevelLakeAtTwoSubdivisions()
    {
        foreach (int level in new[] { 2, 3 })
        {
            var f = Create(level); var lake = Visible(f);
            Assert.That(lake.FloorElevation, Is.GreaterThan(8f));
            Assert.That(lake.CurrentSurfaceElevation, Is.EqualTo(lake.SpillElevation));
            Assert.That(f.Water.Triangles.Length, Is.GreaterThan(0));
            foreach (Vector3 p in f.Water.Vertices) Assert.That(p.magnitude, Is.EqualTo(lake.SpillElevation).Within(.000003f));
        }
    }

    [Test]
    public void ExcludedBelowSeaBasinUsesTheSameLakeDetection()
    {
        var f = Create(3, true);
        var ocean = GeodesicOceanConnectivity.Build(f.Topology, f.Heights, 8f, true, true, .03f);
        int floor = Visible(f).FloorCell;
        Assert.That(ocean.BelowSeaLevel[floor], Is.True);
        Assert.That(ocean.PotentialLakeBasin[floor], Is.True);
        var graph = GeodesicDrainageGraph.Build(f.Topology, f.Heights, ocean.OceanMask, 8f, 8f);
        var lakes = GeodesicLakeBasins.Build(f.Topology, graph, 8f, true, .0001f, .005f);
        Assert.That(lakes.LakeMask[floor], Is.True);
        Assert.That(graph.Ocean[floor], Is.False);
    }

    [Test]
    public void RetainedOceanNeverBecomesLakeOrMarineLayersInLake()
    {
        var f = Create();
        var grid = new GeodesicOceanLayerGrid(f.Topology, new GeodesicTransportGraph(f.Topology), f.Graph.Ocean, f.Heights, 8f, 5, new[] { 0f, .2f, .4f, .6f, .8f, 1f });
        for (int cell = 0; cell < f.Topology.CellCount; cell++)
        {
            if (f.Graph.Ocean[cell]) Assert.That(f.Lakes.LakeMask[cell], Is.False);
            if (f.Lakes.LakeMask[cell]) Assert.That(grid.ActiveLayerCountByCell[cell], Is.Zero);
        }
    }

    [Test]
    public void LakeGenerationDoesNotModifyTerrainVerticesOrHeights()
    {
        var off = Create(enabled: false); var on = Create();
        CollectionAssert.AreEqual(off.Heights, on.Heights);
        CollectionAssert.AreEqual(off.Surface, on.Surface);
        CollectionAssert.AreEqual(on.Heights, on.Graph.HydrologicalElevation);
        Assert.That(off.Water.Vertices, Is.Empty);
    }

    [Test]
    public void RewiredLakeHasExactlyOneExitAndNoCycles()
    {
        var f = Create(); var lake = Visible(f);
        int[] receivers = f.Lakes.CreateReceivers(f.Topology, f.Graph);
        Assert.That(lake.Cells.Count(c => f.Lakes.BasinId[receivers[c]] != lake.Id), Is.EqualTo(1));
        Assert.That(receivers[lake.SpillFromCell], Is.EqualTo(lake.SpillCell));
        f.Graph.ApplyLakeReceivers(receivers);
        foreach (int source in lake.Cells)
        {
            int cell = source, steps = 0;
            while (f.Graph.DrainageReceiver[cell] >= 0 && steps++ < f.Graph.CellCount) cell = f.Graph.DrainageReceiver[cell];
            Assert.That(steps, Is.LessThan(f.Graph.CellCount));
            Assert.That(f.Graph.Ocean[cell], Is.True);
        }
        Assert.That(f.Graph.DrainageArea[lake.SpillFromCell], Is.GreaterThanOrEqualTo(lake.Area - 1e-7));
    }

    [Test]
    public void BasinExistenceAndWaterLevelDoNotDependOnRunoff()
    {
        var f = Create(); var original = Visible(f);
        f.Graph.UpdateRunoff(new double[f.Graph.CellCount]);
        var dry = GeodesicLakeBasins.Build(f.Topology, f.Graph, 8f, true, .0001f, .005f);
        var basin = dry.Basins.Single(b => b.Selected);
        CollectionAssert.AreEqual(original.Cells, basin.Cells);
        Assert.That(basin.SpillElevation, Is.EqualTo(original.SpillElevation));
    }

    [Test]
    public void ThresholdsAndDisableArePredictable()
    {
        Assert.That(Create(area: .05f).Lakes.Basins.Any(b => b.Selected), Is.False);
        Assert.That(Create(depth: .5f).Lakes.Basins.Any(b => b.Selected), Is.False);
        var off = Create(enabled: false);
        Assert.That(off.Lakes.LakeMask.Any(x => x), Is.False);
        Assert.That(off.Water.Triangles, Is.Empty);
        CollectionAssert.AreEqual(off.Graph.DrainageReceiver, off.Lakes.CreateReceivers(off.Topology, off.Graph));
        Assert.That(GeodesicLakeBasins.NormalizeArea(float.NaN), Is.EqualTo(.0001f));
        Assert.That(GeodesicLakeBasins.NormalizeDepth(float.PositiveInfinity), Is.EqualTo(.005f));
    }

    [Test]
    public void LakeGenerationIsDeterministic()
    {
        var a = Create(); var b = Create();
        CollectionAssert.AreEqual(a.Lakes.BasinId, b.Lakes.BasinId);
        CollectionAssert.AreEqual(a.Water.Vertices, b.Water.Vertices);
        CollectionAssert.AreEqual(a.Water.Triangles, b.Water.Triangles);
        CollectionAssert.AreEqual(a.Lakes.CreateReceivers(a.Topology, a.Graph), b.Lakes.CreateReceivers(b.Topology, b.Graph));
    }

    [Test]
    public void CompletedTerrainShorelineClipsWaterBeforeRidges()
    {
        var f = Create(3, visual: d => Bowl(d) + (d.z > .93f && Math.Abs(d.x) < .03f ? .3f : 0f));
        Assert.That(f.Water.Triangles.Length, Is.GreaterThan(0));
        for (int i = 0; i < f.Water.Triangles.Length; i += 3)
        {
            Vector3 a = f.Water.Vertices[f.Water.Triangles[i]], b = f.Water.Vertices[f.Water.Triangles[i + 1]], c = f.Water.Vertices[f.Water.Triangles[i + 2]];
            Vector3 midpoint = (a + b + c) / 3f;
            Assert.That(midpoint.magnitude + .000004f, Is.GreaterThanOrEqualTo(f.Terrain.Radius(midpoint.normalized)));
        }
    }

    [Test]
    public void VisualProjectionMismatchDoesNotInventOrForceALake()
    {
        var hiddenBowl = Create(visual: d => 8.3f);
        Assert.That(hiddenBowl.Lakes.Basins.Length, Is.GreaterThan(0));
        Assert.That(hiddenBowl.Lakes.Basins.Any(b => b.Selected), Is.False);
        Assert.That(hiddenBowl.Water.RejectedProjectionBasins, Is.GreaterThan(0));
        var t = GeodesicGridTopology.Build(2); var ocean = Enumerable.Repeat(true, t.CellCount).ToArray();
        var graph = GeodesicDrainageGraph.Build(t, Enumerable.Repeat(8f, t.CellCount).ToArray(), ocean, 8f, 8f);
        Assert.That(GeodesicLakeBasins.Build(t, graph, 8f, true, 0f, 0f).Basins, Is.Empty);
    }

    [Test]
    public void NoOceanDoesNotInventAnOverflowOutlet()
    {
        var f = Create();
        var g = GeodesicDrainageGraph.Build(f.Topology, f.Heights, new bool[f.Heights.Length], 8f, 8f);
        var lakes = GeodesicLakeBasins.Build(f.Topology, g, 8f, true, 0f, 0f);
        Assert.That(lakes.Basins.Any(b => b.Selected), Is.False);
        Assert.That(lakes.Basins.All(b => b.Terminal), Is.True);
    }

    [Test]
    public void AdjacentDifferentSpillLevelsRemainDifferentBasins()
    {
        var t = GeodesicGridTopology.Build(2); var h = Enumerable.Repeat(8.4f, t.CellCount).ToArray();
        var ocean = new bool[h.Length]; ocean[0] = true; h[0] = 7.9f;
        int a = t.Neighbors6[0], b = t.Neighbors6[a * 6 + 2];
        if (b == 0) b = t.Neighbors6[a * 6 + 3];
        h[a] = 8.05f; h[b] = 8.01f;
        float Spill(int x, int y) => x == b || y == b ? 8.3f : 8.2f;
        var graph = GeodesicDrainageGraph.Build(t, h, ocean, 8f, 8f, Spill);
        var lakes = GeodesicLakeBasins.Build(t, graph, 8f, true, 0f, 0f, Spill);
        Assert.That(lakes.BasinId[a], Is.Not.EqualTo(lakes.BasinId[b]));
        Assert.That(lakes.Basins[lakes.BasinId[a]].SpillElevation, Is.EqualTo(8.2f));
        Assert.That(lakes.Basins[lakes.BasinId[b]].SpillElevation, Is.EqualTo(8.3f));
    }

    [Test]
    public void MultipleInletsTerminateAtWaterAndAreNotOceanMouths()
    {
        var f = Create(); var basin = Visible(f);
        int tested = 0;
        foreach (int target in basin.Cells)
        {
            for (int n = 0; n < f.Topology.NeighborCounts[target]; n++)
            {
                int source = f.Topology.Neighbors6[target * 6 + n];
                if (f.Lakes.BasinId[source] == basin.Id || f.Graph.Ocean[source] || source == basin.SpillCell) continue;
                int[] saved = (int[])f.Graph.DrainageReceiver.Clone(); f.Graph.DrainageReceiver[source] = target;
                var plan = GeodesicLakeRiverRouting.Build(source, f.Graph, f.Topology.CellDirections, f.Terrain, f.Lakes, f.Water, 12, 7, .35f, .00001f, true, 8f, d => d.z < -.3f);
                Array.Copy(saved, f.Graph.DrainageReceiver, saved.Length);
                if (plan.Failure != GeodesicRiverReachFailure.None) continue;
                Assert.That(plan.InletBasin, Is.EqualTo(basin.Id));
                Assert.That(f.Graph.Ocean[target], Is.False);
                Assert.That(plan.Path.Length, Is.GreaterThan(1));
                Assert.That(f.Terrain.Radius(plan.Path.Last()), Is.EqualTo(basin.SpillElevation).Within(.00001f));
                if (++tested == 2) break;
            }
            if (tested == 2) break;
        }
        Assert.That(tested, Is.EqualTo(2), "Two tributaries must actually obtain shoreline paths.");
    }

    [Test]
    public void OutletStartsAtSpillSideAndInternalReachesAreLakeBridges()
    {
        var f = Create(); var basin = Visible(f);
        f.Graph.ApplyLakeReceivers(f.Lakes.CreateReceivers(f.Topology, f.Graph));
        int connected = 0;
        foreach (int cell in basin.Cells)
        {
            var plan = GeodesicLakeRiverRouting.Build(cell, f.Graph, f.Topology.CellDirections, f.Terrain, f.Lakes, f.Water, 12, 7, .35f, .00001f, true, 8f, d => d.z < -.3f);
            if (cell == basin.SpillFromCell)
            {
                Assert.That(plan.Failure, Is.EqualTo(GeodesicRiverReachFailure.None));
                Assert.That(plan.OutletBasin, Is.EqualTo(basin.Id));
                Assert.That(plan.Path.Length, Is.GreaterThan(1));
            }
            else if (plan.LakeConnected) { connected++; Assert.That(plan.Path, Is.Empty); }
        }
        Assert.That(connected, Is.GreaterThan(0));
    }
    [Test]
    public void EqualLevelPoolsSeparatedAtTheirSpillAreNotMerged()
    {
        var t = GeodesicGridTopology.Build(2);
        var h = Enumerable.Repeat(8.4f, t.CellCount).ToArray(); var ocean = new bool[h.Length];
        ocean[0] = true; h[0] = 7.9f;
        int a = t.Neighbors6[0];
        int b = Enumerable.Range(0, t.NeighborCounts[a]).Select(n => t.Neighbors6[a * 6 + n]).First(c => c != 0);
        h[a] = h[b] = 8.05f;
        float Spill(int x, int y) => 8.2f;
        var graph = GeodesicDrainageGraph.Build(t, h, ocean, 8f, 8f, Spill);
        var lakes = GeodesicLakeBasins.Build(t, graph, 8f, true, 0f, 0f, Spill);
        Assert.That(lakes.BasinId[a], Is.GreaterThanOrEqualTo(0));
        Assert.That(lakes.BasinId[b], Is.GreaterThanOrEqualTo(0));
        Assert.That(lakes.BasinId[a], Is.Not.EqualTo(lakes.BasinId[b]));
    }

    [Test]
    public void VisiblePoolEscapingCoarseSpillIsDiagnosedInsteadOfTruncated()
    {
        var f = Create(visual: d => d.z < -.3f ? 7.9f : 8.05f);
        Assert.That(f.Lakes.Basins.Any(b => b.RejectionReason == "projection-spill-or-footprint-mismatch"), Is.True);
        Assert.That(f.Water.Triangles, Is.Empty);
        Assert.That(f.Lakes.LakeMask.Any(x => x), Is.False);
    }

    [Test]
    public void DisabledLakePlannerPreservesExistingRiverPathsExactly()
    {
        var f = Create(enabled: false);
        for (int cell = 0; cell < f.Graph.CellCount; cell++)
        {
            int next = f.Graph.DrainageReceiver[cell];
            if (f.Graph.Ocean[cell] || next < 0) continue;
            var original = GeodesicRiverPath.Refine(f.Topology.CellDirections[cell], f.Topology.CellDirections[next], f.Terrain.Height, 12, 7, .35f, .000002f);
            if (original.Length > 1) original = GeodesicRiverPath.ClipAtCoast(original, f.Terrain.VisibleHeight, 8f);
            var actual = GeodesicLakeRiverRouting.Build(cell, f.Graph, f.Topology.CellDirections, f.Terrain, f.Lakes, null, 12, 7, .35f, .000002f, true, 8f, null);
            CollectionAssert.AreEqual(original, actual.Path);
        }
    }
    [Test]
    public void SharedRenderEdgesDoNotCreateFalseDryHolesAtHighSubdivision()
    {
        var f = Create(5); var basin = Visible(f); int checkedAnchors = 0;
        foreach (int cell in basin.Cells)
        for (int n = 0; n < f.Topology.NeighborCounts[cell]; n++)
        {
            Vector3 direction = Vector3.Lerp(f.Topology.CellDirections[cell], f.Topology.CellDirections[f.Topology.Neighbors6[cell * 6 + n]], .2f).normalized;
            if (f.Terrain.VisibleHeight(direction) >= basin.SpillElevation - .01f) continue;
            Assert.That(f.Water.BasinAtDirection(direction), Is.EqualTo(basin.Id));
            checkedAnchors++;
        }
        Assert.That(checkedAnchors, Is.GreaterThan(100));
    }
}
