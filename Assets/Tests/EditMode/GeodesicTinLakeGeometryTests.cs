using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicTinLakeGeometryTests
{
    private static GeodesicTinHydrology Facet(float a, float b, float c) => new GeodesicTinHydrology(
        new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
        new[] { 0, 1, 2 }, new[] { a, b, c }, new[] { a, b, c }, onSphere: false);

    [Test] public void OneWetVertexHasExactTriangularShoreline()
    {
        var tin = Facet(0, 2, 2); var p = GeodesicTinLakeGeometry.ClipTriangle(tin, 0, 1);
        Assert.That(p.Length, Is.EqualTo(3));
        Assert.That(p.Count(q => Math.Abs(tin.Height(q) - 1) < 1e-12), Is.EqualTo(2));
        Assert.That(p.Any(q => q.A == .5 && q.B == .5 && q.C == 0));
        Assert.That(p.Any(q => q.A == .5 && q.B == 0 && q.C == .5));
    }
    [Test] public void TwoWetVerticesProduceQuadAndFullAndDryCasesAreExact()
    {
        var tin = Facet(0, 0, 2);
        Assert.That(GeodesicTinLakeGeometry.ClipTriangle(tin, 0, 1).Length, Is.EqualTo(4));
        Assert.That(GeodesicTinLakeGeometry.ClipTriangle(tin, 0, 3).Length, Is.EqualTo(3));
        Assert.That(GeodesicTinLakeGeometry.ClipTriangle(tin, 0, -1), Is.Empty);
        Assert.That(GeodesicTinLakeGeometry.ClipTriangle(Facet(1, 2, 3), 0, 1), Is.Empty);
    }
    [Test] public void SpillVertexIsPreservedWithoutDuplicateVertices()
    {
        var tin = Facet(0, 1, 2); var p = GeodesicTinLakeGeometry.ClipTriangle(tin, 0, 1);
        Assert.That(p.Length, Is.EqualTo(3)); Assert.That(p.Count(q => q.B == 1), Is.EqualTo(1));
        foreach (var q in p) Assert.That(tin.Height(q), Is.LessThanOrEqualTo(1));
    }
    private static GeodesicTinHydrology Bowl() => GeodesicTinHydrologyTests.Grid((x, y) => 2 + x * x + (y - 1) * (y - 1), size: 8).Tin;
    [Test] public void GeometryIsLevelAndDeterministicAndUsesClippedFacets()
    {
        var tin = Bowl(); var topology = GeodesicTinBasins.Build(tin, 0, 0);
        var a = GeodesicTinLakeGeometry.Build(tin, topology); var b = GeodesicTinLakeGeometry.Build(tin, topology);
        Assert.That(a.Bodies.Count, Is.EqualTo(1)); Assert.That(a.Triangles.Length, Is.GreaterThan(0));
        Assert.That(a.Bodies[0].Area, Is.LessThan(topology.Basins.Single().Area));
        foreach (var v in a.Vertices) Assert.That(v.z, Is.EqualTo(3));
        CollectionAssert.AreEqual(a.Vertices, b.Vertices); CollectionAssert.AreEqual(a.Triangles, b.Triangles);
        foreach (var p in a.TerrainPoints) Assert.That(tin.Height(p), Is.LessThanOrEqualTo(3 + 1e-12));
        Assert.That(a.Triangles.All(i => i >= 0 && i < a.Vertices.Length));
    }
    [Test] public void SphericalWaterVerticesHaveConstantRadius()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => 8 + x * x + (y - 1) * (y - 1), size: 8);
        var directions = f.Vertices.Select(v => new Vector3(v.x * .1f, (v.y - 1) * .1f, 1).normalized).ToArray();
        var tin = new GeodesicTinHydrology(directions, f.Triangles, f.Heights, f.Heights, allowBoundaryOutflow: true);
        var geometry = GeodesicTinLakeGeometry.Build(tin, GeodesicTinBasins.Build(tin, 0, 0));
        Assert.That(geometry.Bodies.Count, Is.EqualTo(1));
        foreach (var v in geometry.Vertices) Assert.That(v.magnitude, Is.EqualTo(9).Within(2e-6));
    }
    [Test] public void QualifyingAncestorSuppressesChildWaterButNotRawTopology()
    {
        var tin = GeodesicTinHydrologyTests.Grid((x, y) => 2 + 16 * (x * x - .25f) * (x * x - .25f) + 4 * (y - 1) * (y - 1), size: 16).Tin;
        var topology = GeodesicTinBasins.Build(tin, 0, 0); var geometry = GeodesicTinLakeGeometry.Build(tin, topology);
        Assert.That(topology.Basins.Count, Is.EqualTo(2)); Assert.That(geometry.Bodies.Count, Is.EqualTo(1));
        Assert.That(geometry.NestedSuppressed, Is.EqualTo(1)); Assert.That(geometry.Bodies.Single().WaterLevel, Is.EqualTo(6));
        Assert.That(geometry.TerrainPoints.GroupBy(p => p.Triangle).All(g => g.Count() <= 4));
    }
    [Test] public void TerminalSubseaAndThresholdFailuresDoNotRenderWater()
    {
        var tin = Bowl(); Assert.That(GeodesicTinLakeGeometry.Build(tin, GeodesicTinBasins.Build(tin, 0, 10)).Vertices, Is.Empty);
        var closed = GeodesicTinHydrologyTests.Grid((x, y) => 2 + x * x + (y - 1) * (y - 1), size: 8, boundaryOutflow: false).Tin;
        Assert.That(GeodesicTinLakeGeometry.Build(closed, GeodesicTinBasins.Build(closed, 0, 0)).SkippedTerminal, Is.EqualTo(1));
        Assert.That(GeodesicTinLakeGeometry.Build(closed, GeodesicTinBasins.Build(closed, 0, 0)).Vertices, Is.Empty);
        var subsea = GeodesicTinHydrologyTests.Grid((x, y) => -.1f + x * x + (y - 1) * (y - 1), size: 8).Tin;
        Assert.That(GeodesicTinLakeGeometry.Build(subsea, GeodesicTinBasins.Build(subsea, 0, 0)).Vertices, Is.Empty);
    }
    [Test] public void OceanExclusionAndHydrologyStateRemainUnchanged()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => (x <= -.5f ? 1 + 8 * (-.5f - x) : x <= 0 ? 1 + 4 * (x + .5f) : 3 - 4 * x) + 8 * (y - 1) * (y - 1), size: 16, ocean: true, boundaryOutflow: false);
        var tin = f.Tin; tin.Accumulate(); tin.ExtractNetworks(.01);
        var receiver = tin.TriangleReceiver.ToArray(); var runoff = tin.AccumulatedRunoff.ToArray(); var area = tin.DrainageArea.ToArray();
        var heights = tin.HydrologyHeight.ToArray(); var visible = tin.VisibleHeight.ToArray();
        var networks = tin.Networks.ToArray(); var paths = networks.Select(n => n.Samples.ToArray()).ToArray();
        var centres = networks.Select(n => n.Centreline.ToArray()).ToArray(); var metadata = networks.Select(n => (n.SourceTriangle, n.Termination, n.JunctionNetwork, n.Length)).ToArray();
        string diagnostics = tin.Diagnostics.Summary();
        var ocean = Enumerable.Range(0, tin.TriangleCount).Select(t => tin.IsOcean(tin.Centroid(t))).ToArray();
        var geometry = GeodesicTinLakeGeometry.Build(tin, GeodesicTinBasins.Build(tin, 0, 0));
        Assert.That(geometry.Vertices.Length, Is.GreaterThan(0));
        foreach (var p in geometry.TerrainPoints) Assert.That(tin.IsOcean(p), Is.False);
        CollectionAssert.AreEqual(receiver, tin.TriangleReceiver); CollectionAssert.AreEqual(runoff, tin.AccumulatedRunoff); CollectionAssert.AreEqual(area, tin.DrainageArea);
        CollectionAssert.AreEqual(heights, tin.HydrologyHeight); CollectionAssert.AreEqual(visible, tin.VisibleHeight); CollectionAssert.AreEqual(networks, tin.Networks);
        CollectionAssert.AreEqual(metadata, tin.Networks.Select(n => (n.SourceTriangle, n.Termination, n.JunctionNetwork, n.Length)));
        for (int i = 0; i < networks.Length; i++) { CollectionAssert.AreEqual(paths[i], networks[i].Samples); CollectionAssert.AreEqual(centres[i], networks[i].Centreline); }
        CollectionAssert.AreEqual(ocean, Enumerable.Range(0, tin.TriangleCount).Select(t => tin.IsOcean(tin.Centroid(t))));
        Assert.That(tin.Diagnostics.Summary(), Is.EqualTo(diagnostics));
    }

    [Test] public void SharedCoastVertexCanBeDryInTraceCarrierButOceanInAnotherIncidentFacet()
    {
        var positions = new[] { Vector3.zero, new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(-1, 0, 0), new Vector3(0, -1, 0) };
        var heights = new[] { -1f, 1f, 1f, 1f, 1f };
        var tin = new GeodesicTinHydrology(positions, new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1 }, heights, heights,
            new[] { false, true, false, false, false }, 0, onSphere: false);
        var dryCarrier = new TinPoint(1, 1, 0, 0); var wetCarrier = new TinPoint(0, 1, 0, 0);
        Assert.That(tin.IsOcean(dryCarrier), Is.False); Assert.That(tin.IsOcean(wetCarrier), Is.True);
        tin.Trace(dryCarrier, out var termination); Assert.That(termination, Is.EqualTo(TinTermination.TrueClosedBasin));
        var basins = GeodesicTinBasins.Build(tin, 0, 0);
        Assert.That(basins.Find(dryCarrier), Is.Null); Assert.That(basins.Basins, Is.Empty);
        Assert.That(GeodesicTinLakeGeometry.Build(tin, basins).Vertices, Is.Empty);
    }

    [Test] public void GeneratedQuadUsesTwoTrianglesAndSubmergedFacetUsesOne()
    {
        var tin = Bowl(); var geometry = GeodesicTinLakeGeometry.Build(tin, GeodesicTinBasins.Build(tin, 0, 0));
        bool foundQuad = false, foundFull = false;
        foreach (var group in geometry.TerrainPoints.Select((p, i) => new { Point = p, Index = i }).GroupBy(x => x.Point.Triangle))
        {
            int t = group.Key; var polygon = GeodesicTinLakeGeometry.ClipTriangle(tin, t, 3);
            int count = geometry.Triangles.Count(i => geometry.TerrainPoints[i].Triangle == t) / 3;
            Assert.That(count, Is.EqualTo(polygon.Length - 2));
            if (polygon.Length == 4) foundQuad = true;
            if (polygon.Length == 3 && polygon.All(p => tin.Height(p) < 3)) foundFull = true;
        }
        Assert.That(foundQuad && foundFull, Is.True);
    }
}
