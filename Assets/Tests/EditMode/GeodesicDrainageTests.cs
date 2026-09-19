using System;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicDrainageTests
{
    [Test]
    public void LargeScaleHeightSharesTerrainFormulaButIgnoresFineDetail()
    {
        var settings = PlanetTerrainSettings.Earthlike;
        var withoutDetail = settings; withoutDetail.fineDetailAmplitude = 0f;
        var strongerDetail = settings; strongerDetail.fineDetailAmplitude *= 1.5f;
        var topology = GeodesicGridTopology.Build(1);
        bool visibleDetailDiffers = false;
        foreach (Vector3 direction in topology.CellDirections)
        {
            var sample = PlanetTerrainSampler.Evaluate(direction, 42, settings);
            Assert.That(sample.LargeScaleHeightOffset,
                Is.EqualTo(PlanetTerrainSampler.EvaluateHeight(direction, 42, withoutDetail)));
            Assert.That(sample.LargeScaleHeightOffset,
                Is.EqualTo(PlanetTerrainSampler.Evaluate(direction, 42, strongerDetail).LargeScaleHeightOffset));
            visibleDetailDiffers |= sample.HeightOffset != sample.LargeScaleHeightOffset;
        }
        Assert.That(visibleDetailDiffers, Is.True);
    }

    [Test]
    public void BroadOrHighAmplitudeDetailRemainsInDrainageTerrain()
    {
        Vector3 direction = new Vector3(1f, 2f, 3f).normalized;
        var broad = PlanetTerrainSettings.Earthlike; broad.fineDetailScale = 1f;
        var sample = PlanetTerrainSampler.Evaluate(direction, 42, broad);
        Assert.That(sample.LargeScaleHeightOffset, Is.EqualTo(sample.HeightOffset));
        var high = PlanetTerrainSettings.Earthlike; high.fineDetailAmplitude = 0.2f;
        sample = PlanetTerrainSampler.Evaluate(direction, 42, high);
        Assert.That(sample.LargeScaleHeightOffset, Is.EqualTo(sample.HeightOffset));
    }

    [Test]
    public void HydrologicalAltitudeAndVisibleSurfaceStaySeparate()
    {
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(1);
        var vertices = new Vector3[geometry.VertexCount]; var largeScale = new float[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        { vertices[i] = geometry.UnitVertices[i] * 8.02f; largeScale[i] = 8f; }
        var terrain = new GeodesicRiverTerrain(geometry, vertices, largeScale);
        Assert.That(terrain.Height(geometry.UnitVertices[0]), Is.EqualTo(8f).Within(1e-5f));
        Assert.That(terrain.VisibleHeight(geometry.UnitVertices[0]), Is.EqualTo(8.02f).Within(1e-5f));
        Assert.That(terrain.Radius(geometry.UnitVertices[0]), Is.EqualTo(8.02f).Within(1e-5f));
    }

    [Test]
    public void FilledBowlAndFlatsDrainToOceanWithoutCyclesOrTerrainEdits()
    {
        var topology = GeodesicGridTopology.Build(2);
        var heights = new float[topology.CellCount]; Array.Fill(heights, 4f);
        var ocean = new bool[topology.CellCount]; ocean[0] = true; heights[0] = 0f;
        int pit = topology.Neighbors6[6]; heights[pit] = 1f;
        float[] original = (float[])heights.Clone();
        var graph = GeodesicDrainageGraph.Build(topology, heights, ocean, 0f, 8f);
        CollectionAssert.AreEqual(original, heights);
        Assert.That(graph.UnresolvedSinkCount, Is.Zero);
        Assert.That(graph.FillDepth[pit], Is.GreaterThan(0f));
        double area = 0d;
        for (int cell = 0; cell < topology.CellCount; cell++)
        {
            area += graph.LocalArea[cell];
            int cursor = cell, steps = 0;
            while (graph.DrainageReceiver[cursor] >= 0 && steps++ < topology.CellCount)
            {
                int next = graph.DrainageReceiver[cursor];
                Assert.That(IsNeighbor(topology, cursor, next), Is.True);
                cursor = next;
            }
            Assert.That(steps, Is.LessThan(topology.CellCount));
            Assert.That(cursor, Is.Zero);
        }
        Assert.That(graph.DrainageArea[0], Is.EqualTo(area).Within(1e-8));
        CollectionAssert.AreEqual(graph.DrainageArea, graph.AccumulatedRunoff);
    }

    [Test]
    public void WetDryRunoffChangesDischargeWithoutChangingReceiversOrArea()
    {
        var topology = GeodesicGridTopology.Build(1);
        var heights = new float[topology.CellCount]; Array.Fill(heights, 1f);
        var ocean = new bool[topology.CellCount]; ocean[0] = true;
        var graph = GeodesicDrainageGraph.Build(topology, heights, ocean, 0f, 1f);
        int[] receivers = (int[])graph.DrainageReceiver.Clone();
        double[] area = (double[])graph.DrainageArea.Clone();
        var runoff = new double[topology.CellCount]; runoff[7] = 3d; runoff[19] = 5d;
        graph.UpdateRunoff(runoff);
        Assert.That(graph.AccumulatedRunoff[0], Is.EqualTo(8d));
        Array.Clear(runoff, 0, runoff.Length); graph.UpdateRunoff(runoff);
        Assert.That(graph.AccumulatedRunoff[0], Is.Zero);
        CollectionAssert.AreEqual(receivers, graph.DrainageReceiver);
        CollectionAssert.AreEqual(area, graph.DrainageArea);
        runoff[1] = double.NaN;
        Assert.Throws<ArgumentException>(() => graph.UpdateRunoff(runoff));
        Assert.That(graph.AccumulatedRunoff[0], Is.Zero);
    }

    [Test]
    public void DryPlanetKeepsExplicitBasinAndOceanWorldHasNoLandRunoff()
    {
        var topology = GeodesicGridTopology.Build(1);
        var heights = new float[topology.CellCount];
        var ocean = new bool[topology.CellCount];
        var dry = GeodesicDrainageGraph.Build(topology, heights, ocean, 0f, 1f);
        Assert.That(dry.UnresolvedSinkCount, Is.EqualTo(1));
        Assert.That(dry.DrainageReceiver[0], Is.EqualTo(-1));
        Assert.That(dry.DrainageArea[0], Is.GreaterThan(12d));
        Array.Fill(ocean, true);
        var wet = GeodesicDrainageGraph.Build(topology, heights, ocean, 0f, 1f);
        Assert.That(wet.UnresolvedSinkCount, Is.Zero);
        for (int i = 0; i < wet.CellCount; i++)
        { Assert.That(wet.DrainageReceiver[i], Is.EqualTo(-1)); Assert.That(wet.AccumulatedRunoff[i], Is.Zero); }
    }

    [Test]
    public void PriorityFloodHonorsSampledSaddlesAndIsDeterministic()
    {
        var topology = GeodesicGridTopology.Build(1);
        var heights = new float[topology.CellCount]; Array.Fill(heights, 2f);
        var ocean = new bool[topology.CellCount]; ocean[0] = true;
        float Spill(int a, int b) => a == 0 || b == 0 ? 7f : 2f;
        var first = GeodesicDrainageGraph.Build(topology, heights, ocean, 0f, 1f, Spill);
        var second = GeodesicDrainageGraph.Build(topology, heights, ocean, 0f, 1f, Spill);
        Assert.That(first.FilledElevation[1], Is.EqualTo(7f));
        CollectionAssert.AreEqual(first.DrainageReceiver, second.DrainageReceiver);
        CollectionAssert.AreEqual(first.UpstreamToDownstream, second.UpstreamToDownstream);
    }

    [Test]
    public void RiverTerrainMatchesVisibleVerticesAndTriangleInterior()
    {
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(3);
        var vertices = new Vector3[geometry.VertexCount];
        for (int i = 0; i < vertices.Length; i++) vertices[i] = geometry.UnitVertices[i] * (8f + i % 13 * 0.001f);
        var terrain = new GeodesicRiverTerrain(geometry, vertices);
        for (int i = 0; i < vertices.Length; i += 3)
            Assert.That(terrain.Radius(geometry.UnitVertices[i]), Is.EqualTo(vertices[i].magnitude).Within(1e-4f));
        for (int t = 0; t < geometry.Triangles.Length; t += 21)
        {
            Vector3 a = vertices[geometry.Triangles[t]], b = vertices[geometry.Triangles[t + 1]], c = vertices[geometry.Triangles[t + 2]];
            Vector3 point = a * 0.2f + b * 0.3f + c * 0.5f;
            Assert.That(terrain.Radius(point.normalized), Is.EqualTo(point.magnitude).Within(1e-4f));
        }
    }

    [Test]
    public void LocalRefinementAvoidsRidgeAndKeepsTributaryEndpoints()
    {
        Vector3 a = new Vector3(-0.1f, 1f, 0f).normalized, b = new Vector3(0.1f, 1f, 0f).normalized;
        float Height(Vector3 d) => 8f - d.x + 0.2f * Mathf.Exp(-Mathf.Pow(d.x / 0.025f, 2f) - Mathf.Pow(d.z / 0.015f, 2f));
        var path = GeodesicRiverPath.Refine(a, b, Height, 24, 11, 0.4f, 1e-6f);
        Assert.That(path.Length, Is.GreaterThan(2));
        Assert.That(path[0], Is.EqualTo(a)); Assert.That(path[path.Length - 1], Is.EqualTo(b));
        float maxOffset = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            maxOffset = Mathf.Max(maxOffset, Mathf.Abs(path[i].z));
            Assert.That(GeodesicRiverPath.IsDownhill(path[i - 1], path[i], Height, 1e-6f), Is.True);
        }
        Assert.That(maxOffset, Is.GreaterThan(0.015f));
    }

    [Test]
    public void UphillBasinIsNotDrawnAndMouthTerminatesAtSeaLevel()
    {
        Vector3 a = new Vector3(-0.1f, 1f, 0f).normalized, b = new Vector3(0.1f, 1f, 0f).normalized;
        Assert.That(GeodesicRiverPath.Refine(a, b, d => 8f + d.x, 12, 7, 0.3f, 1e-6f), Is.Empty);
        float Height(Vector3 d) => 8f - d.x;
        var path = GeodesicRiverPath.Refine(a, b, Height, 12, 7, 0.3f, 1e-6f);
        var clipped = GeodesicRiverPath.ClipAtCoast(path, Height, 8f);
        Assert.That(Height(clipped[clipped.Length - 1]), Is.EqualTo(8f).Within(1e-5f));
    }

    private static bool IsNeighbor(GeodesicGridTopology topology, int a, int b)
    {
        for (int i = 0; i < topology.NeighborCounts[a]; i++) if (topology.Neighbors6[a * 6 + i] == b) return true;
        return false;
    }
}
