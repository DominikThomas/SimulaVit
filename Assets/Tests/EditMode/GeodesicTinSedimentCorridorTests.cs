using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicTinSedimentCorridorTests
{
    [Test]
    public void BelowSeaBasinUsesRaisedSedimentAndAnExactPrimaryCoast()
    {
        var f = new Fixture(.98f, networkThreshold: .012);
        var plan = f.Plan();
        var connection = plan.Connections.Values.Single();
        Assert.That(plan.Elevations[connection.BasinId], Is.GreaterThan(f.Tin.BasinSeaLevel));
        Assert.That(f.Tin.VisibleHeight[connection.BasinId], Is.LessThan(f.Tin.BasinSeaLevel));
        Assert.That(connection.Coast.HasValue, Is.True);
        Assert.That(f.Tin.IsRetainedOceanBoundary(connection.Coast.Value));
        Assert.That(f.Tin.CompletedVisibleRadius(connection.Coast.Value), Is.EqualTo(f.Tin.BasinSeaLevel).Within(1e-9));
        foreach (int vertex in plan.Elevations.Keys)
            for (int i = f.Tin.BasinIncidentStart(vertex); i < f.Tin.BasinIncidentEnd(vertex); i++)
                Assert.That(f.Tin.BasinFacetIntersectsOcean(f.Tin.BasinIncidentTriangle(i)), Is.False);
        f.AssertDelivery(plan);
    }

    [Test]
    public void CorridorCanJoinAnExistingPrimaryOceanRiver()
    {
        var f = new Fixture(.98f);
        var plan = f.Plan();
        var connection = plan.Connections.Values.Single();
        Assert.That(connection.TargetNetwork, Is.GreaterThanOrEqualTo(0));
        Assert.That(f.Before.NetworkOutcome(connection.TargetNetwork).OceanComponent, Is.Zero);
        Assert.That(f.Before.NetworkOutcome(connection.TargetNetwork).ViaCorridor, Is.False);
        var final = f.AssertDelivery(plan);
        Assert.That(final.NetworkOutcome(connection.TargetNetwork).ViaCorridor, Is.False);
        Assert.That(connection.Coast, Is.Null);
    }

    [Test]
    public void ShortLowlandConnectionLevelsARidgeWithoutReroutingTheTin()
    {
        var f = new Fixture(.998f);
        var heights = f.Tin.VisibleHeight.ToArray();
        var raw = f.Tin.Networks.Select(n => n.Samples).ToArray();
        var plan = f.Plan();
        var connection = plan.Connections.Values.Single();
        Assert.That(connection.Length, Is.LessThan(.22f));
        Assert.That(connection.Volume, Is.GreaterThan(0));
        Assert.That(plan.Elevations.Values.Distinct().Count(), Is.EqualTo(1));
        Assert.That(plan.Elevations.Any(p => p.Value < heights[p.Key]));
        CollectionAssert.AreEqual(heights, f.Tin.VisibleHeight);
        CollectionAssert.AreEqual(raw, f.Tin.Networks.Select(n => n.Samples));
        f.AssertDelivery(plan);
    }

    [Test]
    public void ContinuousHighRidgeLeavesTheBasinClosed()
    {
        var f = new Fixture(.98f, _ => 1.12f);
        var plan = f.Plan();
        Assert.That(f.Before.InlandClosed, Is.GreaterThan(0));
        Assert.That(plan.Connections, Is.Empty);
        Assert.That(plan.Elevations, Is.Empty);
        Assert.That(plan.Decisions.Any(s => s.Contains("no receiver within terrain and length limits")));
        Assert.That(plan.Verify(f.Analyze(plan.Connections)), Is.SameAs(f.Before));
    }

    [Test]
    public void ShortDetourAvoidsTheHighPartOfTheRidge()
    {
        var f = new Fixture(.99f, y => Math.Abs(y - 1) < .025f ? 1.12f : 1.014f);
        var plan = f.Plan();
        var connection = plan.Connections.Values.Single();
        Assert.That(connection.Vertices.Any(v => Math.Abs(f.Graph.CellDirections[v].y - 1f) >= .025f));
        Assert.That(plan.Elevations.Keys.All(v => f.Tin.VisibleHeight[v] < 1.027f));
        f.AssertDelivery(plan);
    }

    [Test]
    public void TributariesShareOneOutletAndTheirRunoffIsCountedOnce()
    {
        var f = new Fixture(.99f);
        var plan = f.Plan();
        var connection = plan.Connections.Values.Single();
        var tributaries = Enumerable.Range(0, f.Tin.Networks.Count)
            .Where(i => f.Before.NetworkOutcome(i).Basin?.Id == connection.BasinId).ToArray();
        Assert.That(tributaries.Length, Is.GreaterThan(1));
        var final = f.AssertDelivery(plan);
        foreach (int i in tributaries) Assert.That(final.NetworkOutcome(i).ViaCorridor, Is.True);
        Assert.That(final.CorridorOcean, Is.EqualTo(connection.Runoff).Within(final.Tolerance));
    }

    [Test]
    public void DeliveryDetectsAnOutletThatLoopsBackIntoItsOwnBasin()
    {
        var f = new Fixture(.99f);
        var plan = f.Plan();
        var connection = plan.Connections.Values.Single();
        int inlet = connection.InletNetworks[0];
        var cyclic = new GeodesicTinSedimentCorridors.Connection(connection.BasinId, inlet, null,
            connection.Vertices, connection.InletNetworks, connection.InletLakes, connection.Runoff,
            connection.Length, connection.Area, connection.Volume);
        var final = f.Analyze(new Dictionary<int, GeodesicTinSedimentCorridors.Connection> { [cyclic.BasinId] = cyclic });
        Assert.That(final.CycleNodes, Is.GreaterThan(0));
        Assert.That(plan.Verify(final), Is.SameAs(f.Before));
        Assert.That(plan.Connections, Is.Empty);
        Assert.That(plan.Elevations, Is.Empty);
    }

    private sealed class Fixture
    {
        internal readonly GeodesicTinHydrology Tin;
        internal readonly IGeodesicHydrologyTopology Graph;
        internal readonly GeodesicTinBasins Basins;
        internal readonly GeodesicTinLakeGeometry Lakes;
        internal readonly GeodesicTinLakeRouting Routing;
        internal readonly GeodesicDrainageDelivery Before;

        internal Fixture(float floor, Func<float, float> ridge = null, double networkThreshold = .0003)
        {
            float Height(float x, float y)
            {
                float saddle = ridge?.Invoke(y) ?? 1.014f;
                float h = x < -.04f ? floor + (-.04f - x) * .25f : x < 0f ? Mathf.Lerp(floor, saddle, (x + .04f) / .04f) :
                    Mathf.LerpUnclamped(saddle, .995f, x / .08f);
                return h + .13f * Math.Abs(y - 1f);
            }
            var grid = GeodesicTinHydrologyTests.Grid(Height, size: 32, extent: .16f, boundaryOutflow: false);
            Graph = new GridTopology(grid.Vertices, grid.Triangles);
            Tin = new GeodesicTinHydrology(grid.Vertices, grid.Triangles, grid.Heights, grid.Heights,
                grid.Vertices.Select(v => v.x >= .07f).ToArray(), 1f, onSphere: false, allowBoundaryOutflow: false);
            Tin.CaptureParcelDestinations = true;
            Tin.Accumulate(); Tin.ExtractNetworks(networkThreshold);
            Basins = GeodesicTinBasins.Build(Tin, 0, 0);
            Lakes = GeodesicTinLakeGeometry.Build(Tin, Basins);
            Routing = GeodesicTinLakeRouting.Build(Tin, Basins, Lakes, networkThreshold);
            Before = Analyze();
        }

        internal GeodesicDrainageDelivery Analyze(IReadOnlyDictionary<int, GeodesicTinSedimentCorridors.Connection> connections = null) =>
            GeodesicDrainageDelivery.Analyze(Tin, Basins, Routing.Lakes, Routing.RiverReaches,
                oceanComponentAt: _ => 0, primaryOceanComponent: 0, corridors: connections);

        internal GeodesicTinSedimentCorridors Plan() => GeodesicTinSedimentCorridors.Build(Tin, Graph, Basins, Lakes, Routing,
            Before, _ => 0, 0, 1f, .001);

        internal GeodesicDrainageDelivery AssertDelivery(GeodesicTinSedimentCorridors plan)
        {
            var final = plan.Verify(Analyze(plan.Connections));
            Assert.That(plan.Failure, Is.Null);
            Assert.That(final.PrimaryOcean, Is.GreaterThan(Before.PrimaryOcean));
            Assert.That(final.ClosedBasinCount, Is.EqualTo(Before.ClosedBasinCount - 1));
            Assert.That(final.TotalRunoff, Is.EqualTo(Before.TotalRunoff));
            Assert.That(final.Residual, Is.EqualTo(0).Within(final.Tolerance));
            Assert.That(final.CycleNodes, Is.Zero);
            return final;
        }
    }

    private sealed class GridTopology : IGeodesicHydrologyTopology
    {
        public GridTopology(Vector3[] vertices, int[] triangles)
        {
            CellDirections = vertices; Triangles = triangles;
            var neighbors = vertices.Select(_ => new SortedSet<int>()).ToArray();
            for (int t = 0; t < triangles.Length; t += 3)
                for (int k = 0; k < 3; k++)
                    for (int j = 1; j < 3; j++) neighbors[triangles[t + k]].Add(triangles[t + (k + j) % 3]);
            NeighborCounts = neighbors.Select(n => (byte)n.Count).ToArray();
            Neighbors6 = neighbors.SelectMany(n => n.Concat(Enumerable.Repeat(-1, 6 - n.Count))).ToArray();
        }

        public int SubdivisionLevel => 0;
        public int CellCount => CellDirections.Length;
        public int TriangleCount => Triangles.Length / 3;
        public Vector3[] CellDirections { get; }
        public int[] Triangles { get; }
        public byte[] NeighborCounts { get; }
        public int[] Neighbors6 { get; }
        public float[] UnitCellAreas => throw new NotSupportedException();
        public long ApproximateHydrologyMemoryBytes => 0;
        public float NeighborAngularDistance(int cell, int slot) => (CellDirections[cell] - CellDirections[Neighbors6[cell * 6 + slot]]).magnitude;
    }
}
