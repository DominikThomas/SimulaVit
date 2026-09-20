using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class GeodesicOceanConnectivityTests
{
    private static float[] Terrain(GeodesicGridTopology topology, out int tiny)
    {
        var radius = new float[topology.CellCount];
        tiny = 0;
        for (int i = 0; i < radius.Length; i++)
        {
            radius[i] = Math.Abs(topology.CellDirections[i].y) > 0.6f ? 9f : 11f;
            if (topology.CellDirections[i].x > topology.CellDirections[tiny].x) tiny = i;
        }
        radius[tiny] = 9f;
        return radius;
    }
    private static GeodesicOceanConnectivity Classify(GeodesicGridTopology topology, float[] radius, bool filter = true, float threshold = 0.005f)
        => GeodesicOceanConnectivity.Build(topology, radius, 10f, true, filter, threshold);

    [Test]
    public void OneLargeOceanRemainsOcean()
    {
        var t = GeodesicGridTopology.Build(2);
        var r = Classify(t, Enumerable.Repeat(9f, t.CellCount).ToArray());
        Assert.That(r.RetainedComponents, Is.EqualTo(1));
        Assert.That(r.RetainedCells, Is.EqualTo(t.CellCount));
        Assert.That(r.LargestAreaFraction, Is.EqualTo(1d).Within(1e-10));
    }

    [Test]
    public void TwoLargeSeasSurviveAndSmallBasinIsExcludedAtTwoResolutions()
    {
        foreach (int level in new[] { 3, 4 })
        {
            var t = GeodesicGridTopology.Build(level);
            var radius = Terrain(t, out int tiny);
            var r = Classify(t, radius);
            Assert.That(r.Components.Length, Is.EqualTo(3));
            Assert.That(r.RetainedComponents, Is.EqualTo(2));
            Assert.That(r.ExcludedComponents, Is.EqualTo(1));
            Assert.That(r.OceanMask[tiny], Is.False);
            Assert.That(r.BelowSeaLevel[tiny] && r.PotentialLakeBasin[tiny], Is.True);
            Assert.That(r.Components[r.ComponentId[tiny]].CellCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void DisabledFilterMatchesEveryLegacyHeightComparison()
    {
        var t = GeodesicGridTopology.Build(3); var radius = Terrain(t, out int tiny);
        var r = Classify(t, radius, false);
        for (int i = 0; i < t.CellCount; i++) Assert.That(r.OceanMask[i], Is.EqualTo(radius[i] < 10f));
        Assert.That(r.OceanMask[tiny], Is.True);
        Assert.That(r.ExcludedCells, Is.Zero);
    }

    [Test]
    public void ThresholdUsesGeometricAreaWithInclusiveBoundary()
    {
        var t = GeodesicGridTopology.Build(3); var radius = Terrain(t, out int tiny);
        var initial = Classify(t, radius, false);
        double fraction = initial.Components[initial.ComponentId[tiny]].PlanetAreaFraction;
        Assert.That(Classify(t, radius, true, (float)(fraction * 0.99d)).OceanMask[tiny], Is.True);
        Assert.That(Classify(t, radius, true, (float)(fraction * 1.01d)).OceanMask[tiny], Is.False);
        // Exact representable boundary: change geometric area, not counts.
        Array.Fill(t.UnitCellAreas, 1f); t.UnitCellAreas[tiny] = 0.5f;
        float exact = 0.00048828125f; // 1/2048
        t.UnitCellAreas[0] += 1024f - t.UnitCellAreas.Sum();
        Assert.That(Classify(t, radius, true, exact).OceanMask[tiny], Is.True);
    }

    [Test]
    public void ClassificationIsDeterministicAndNeverMutatesTerrain()
    {
        var t = GeodesicGridTopology.Build(3); var radius = Terrain(t, out _); var original = (float[])radius.Clone();
        var a = Classify(t, radius); var b = Classify(t, radius);
        CollectionAssert.AreEqual(a.ComponentId, b.ComponentId);
        CollectionAssert.AreEqual(a.OceanMask, b.OceanMask);
        CollectionAssert.AreEqual(a.PotentialLakeBasin, b.PotentialLakeBasin);
        CollectionAssert.AreEqual(original, radius);
        for (int i = 0; i < a.Components.Length; i++) Assert.That(a.Components[i].SurfaceArea, Is.EqualTo(b.Components[i].SurfaceArea));
    }

    [Test]
    public void CoastlineHasNoRingAroundExcludedBasin()
    {
        var t = GeodesicGridTopology.Build(3); var radius = Terrain(t, out int tiny);
        var filtered = Classify(t, radius); var legacy = Classify(t, radius, false);
        for (int n = 0; n < t.NeighborCounts[tiny]; n++)
        {
            int neighbor = t.Neighbors6[tiny * 6 + n];
            Assert.That(legacy.CoastalLand[neighbor], Is.True);
            Assert.That(filtered.CoastalLand[neighbor], Is.False);
        }
        for (int c = 0; c < t.CellCount; c++)
        {
            bool hasWater = false, hasLand = false;
            for (int n = 0; n < t.NeighborCounts[c]; n++)
            { bool ocean = filtered.OceanMask[t.Neighbors6[c * 6 + n]]; hasWater |= ocean; hasLand |= !ocean; }
            Assert.That(filtered.CoastalLand[c], Is.EqualTo(!filtered.OceanMask[c] && hasWater));
            Assert.That(filtered.CoastalOcean[c], Is.EqualTo(filtered.OceanMask[c] && hasLand));
        }
    }

    [Test]
    public void ExcludedBasinHasNoOceanLayersVolumeOrTransportLinks()
    {
        var t = GeodesicGridTopology.Build(3); var radius = Terrain(t, out int tiny);
        var graph = new GeodesicTransportGraph(t);
        var r = Classify(t, radius);
        var grid = new GeodesicOceanLayerGrid(t, graph, r.OceanMask, radius, 10f, 5, new[] { 0f, .2f, .4f, .6f, .8f, 1f });
        Assert.That(grid.ActiveLayerCountByCell[tiny], Is.Zero);
        for (int i = 0; i < 5; i++) Assert.That(grid.LayerVolume[grid.GetNodeIndex(tiny, i)], Is.Zero);
        foreach (int node in grid.HorizontalNodeA.Concat(grid.HorizontalNodeB).Concat(grid.VerticalUpperNode).Concat(grid.VerticalLowerNode))
            Assert.That(node / grid.MaximumLayerCount, Is.Not.EqualTo(tiny));
        Assert.That(grid.ActiveNodeCount, Is.GreaterThan(0));
    }

    [Test]
    public void DisabledOceanAndOceanWorldRetainExistingSemantics()
    {
        var t = GeodesicGridTopology.Build(2); var radius = Terrain(t, out _);
        Assert.That(GeodesicOceanConnectivity.Build(t, radius, 10f, false, true, .05f).RetainedCells, Is.Zero);
        Assert.That(GeodesicOceanConnectivity.Build(t, radius, 10f, true, true, .05f, true).RetainedCells, Is.EqualTo(t.CellCount));
        Assert.That(GeodesicOceanConnectivity.Build(t, radius, 10f, false, true, .05f, true).RetainedCells, Is.Zero);
    }

    [Test]
    public void ThresholdValidationHandlesInvalidValues()
    {
        Assert.That(GeodesicOceanConnectivity.NormalizeThreshold(float.NaN), Is.EqualTo(.001f));
        Assert.That(GeodesicOceanConnectivity.NormalizeThreshold(float.PositiveInfinity), Is.EqualTo(.001f));
        Assert.That(GeodesicOceanConnectivity.NormalizeThreshold(-1f), Is.Zero);
        Assert.That(GeodesicOceanConnectivity.NormalizeThreshold(1f), Is.EqualTo(.05f));
    }

    [Test]
    public void NearestCellWalkMatchesGlobalSearch()
    {
        var t = GeodesicGridTopology.Build(3); var random = new System.Random(723); int last = 0;
        for (int i = 0; i < 500; i++)
        {
            Vector3 direction = new Vector3((float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f).normalized;
            int expected = 0;
            for (int c = 1; c < t.CellCount; c++) if (Vector3.Dot(direction, t.CellDirections[c]) > Vector3.Dot(direction, t.CellDirections[expected])) expected = c;
            last = GeodesicOceanConnectivity.FindNearestCell(t, direction, last);
            Assert.That(last, Is.EqualTo(expected));
        }
    }

    [Test]
    public void OceanTrianglesAndResourceMappingStayInsideRetainedCells()
    {
        var t = GeodesicGridTopology.Build(3); var radius = Terrain(t, out int tiny);
        var filtered = Classify(t, radius);
        foreach (int renderLevel in new[] { 2, 3, 4 })
        {
            var geometry = GeodesicMaskedOceanGeometry.Build(t, filtered.OceanMask, renderLevel, out var mapping);
            Assert.That(mapping.SampleCount, Is.EqualTo(geometry.VertexCount));
            foreach (var sample in mapping.Samples) Assert.That(filtered.OceanMask[sample.NearestCell], Is.True);
            for (int i = 0; i < geometry.Triangles.Length; i += 3)
            {
                Vector3 a = geometry.UnitVertices[geometry.Triangles[i]], b = geometry.UnitVertices[geometry.Triangles[i + 1]], c = geometry.UnitVertices[geometry.Triangles[i + 2]];
                foreach (Vector3 p in new[] { (a + b + c).normalized, (a * .98f + b * .01f + c * .01f).normalized, (a * .01f + b * .98f + c * .01f).normalized, (a * .01f + b * .01f + c * .98f).normalized })
                    Assert.That(filtered.OceanMask[GeodesicOceanConnectivity.FindNearestCell(t, p)], Is.True);
                Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a), a), Is.GreaterThan(0f));
            }
            Assert.That(mapping.Samples.Any(s => s.NearestCell == tiny), Is.False);
        }
        var oldMask = Classify(t, radius, false);
        GeodesicMaskedOceanGeometry.Build(t, oldMask.OceanMask, 3, out var oldMapping);
        Assert.That(oldMapping.Samples.Any(s => s.NearestCell == tiny), Is.True);
    }

    [Test]
    public void EmptyOceanCreatesNoWaterTriangles()
    {
        var t = GeodesicGridTopology.Build(2);
        var g = GeodesicMaskedOceanGeometry.Build(t, new bool[t.CellCount], 3, out var mapping);
        Assert.That(g.TriangleCount, Is.Zero); Assert.That(mapping.SampleCount, Is.Zero);
    }

    [Test]
    public void RiverDoesNotStopAtAnExcludedBelowSeaDepression()
    {
        var path = new[] { new Vector3(-.3f, 0f, 1f).normalized, new Vector3(-.1f, 0f, 1f).normalized, new Vector3(.1f, 0f, 1f).normalized, new Vector3(.3f, 0f, 1f).normalized };
        var legacy = GeodesicRiverPath.ClipAtCoast(path, p => p.x < -.2f ? 11f : 9f, 10f);
        var filtered = GeodesicRiverPath.ClipAtCoast(path, p => p.x < -.2f ? 11f : 9f, 10f, p => p.x > .2f);
        Assert.That(legacy.Last().x, Is.EqualTo(-.2f).Within(.00001f));
        Assert.That(filtered.Last().x, Is.EqualTo(.2f).Within(.00001f));
        Assert.That(filtered.Length, Is.EqualTo(4));
    }
}
