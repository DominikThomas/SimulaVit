using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Reference = HydrologyReference6f48085;

// Compiled only by ValidateGeodesicLakes.ps1 -BaselineReference. The reference classes
// are extracted directly from git, with a namespace wrapper and no algorithm edits.
public sealed class GeodesicRiverBaselineTests
{
    private static float Height(Vector3 d) => 8f + .2f * d.y + .035f * Mathf.Sin(d.x * 23f + 123456f);

    [Test]
    public void RestoredTerrainSamplingMatchesLiteral6f48085AtRenderSeven()
    {
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(7);
        var radii = geometry.UnitVertices.Select(Height).ToArray();
        var surface = geometry.UnitVertices.Select((d, i) => d * (radii[i] + .002f * Mathf.Sin(d.z * 117f))).ToArray();
        var current = new GeodesicRiverTerrain(geometry, surface, radii);
        var reference = new Reference.GeodesicRiverTerrain(geometry, surface, radii);
        for (int i = 0; i < geometry.VertexCount; i += 113)
        {
            Vector3 d = (geometry.UnitVertices[i] + geometry.UnitVertices[(i + 19) % geometry.VertexCount] * .017f).normalized;
            Assert.That(current.Height(d), Is.EqualTo(reference.Height(d)));
            Assert.That(current.VisibleHeight(d), Is.EqualTo(reference.VisibleHeight(d)));
            Assert.That(current.Radius(d), Is.EqualTo(reference.Radius(d)));
        }
    }

    [Test]
    public void RestoredS6DrainageMatchesLiteral6f48085IncludingSaddles()
    {
        var topology = GeodesicGridTopology.Build(6);
        float[] heights = topology.CellDirections.Select(Height).ToArray();
        bool[] ocean = topology.CellDirections.Select(d => d.y < -.45f).ToArray();
        float Saddle(int a, int b)
        {
            float spill = float.NegativeInfinity;
            for (int i = 1; i <= 3; i++)
                spill = Mathf.Max(spill, Height(Vector3.Lerp(topology.CellDirections[a], topology.CellDirections[b], i / 4f).normalized));
            return spill;
        }
        var current = GeodesicDrainageGraph.Build(topology, heights, ocean, 7.91f, 8f, Saddle);
        var reference = Reference.GeodesicDrainageGraph.Build(topology, heights, ocean, 7.91f, 8f, Saddle);
        CollectionAssert.AreEqual(reference.DrainageReceiver, current.DrainageReceiver);
        CollectionAssert.AreEqual(reference.FloodParent, current.FloodParent);
        CollectionAssert.AreEqual(reference.FloodRank, current.FloodRank);
        CollectionAssert.AreEqual(reference.FilledElevation, current.FilledElevation);
        CollectionAssert.AreEqual(reference.DrainageArea, current.DrainageArea);
        CollectionAssert.AreEqual(reference.AccumulatedRunoff, current.AccumulatedRunoff);
    }

    [Test]
    public void RestoredReachPlansMatchLiteral6f48085WithLakesOff()
    {
        var topology = GeodesicGridTopology.Build(3);
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(4);
        var radii = geometry.UnitVertices.Select(Height).ToArray();
        var surface = geometry.UnitVertices.Select((d, i) => d * (radii[i] + .002f * Mathf.Sin(d.z * 117f))).ToArray();
        var currentTerrain = new GeodesicRiverTerrain(geometry, surface, radii);
        var referenceTerrain = new Reference.GeodesicRiverTerrain(geometry, surface, radii);
        float[] heights = topology.CellDirections.Select(currentTerrain.Height).ToArray();
        bool[] ocean = topology.CellDirections.Select(d => d.y < -.45f).ToArray();
        var current = GeodesicDrainageGraph.Build(topology, heights, ocean, 7.91f, 8f);
        var reference = Reference.GeodesicDrainageGraph.Build(topology, heights, ocean, 7.91f, 8f);
        int visible = 0, failed = 0;
        for (int cell = 0; cell < topology.CellCount; cell++)
        {
            if (ocean[cell] || current.DrainageReceiver[cell] < 0) continue;
            var a = Reference.GeodesicLakeRiverRouting.Build(cell, reference, topology.CellDirections,
                referenceTerrain, null, null, 12, 7, .35f, .000002f, true, 7.91f, null);
            var b = GeodesicLakeRiverRouting.Build(cell, current, topology.CellDirections,
                currentTerrain, null, null, 12, 7, .35f, .000002f, true, 7.91f, null);
            CollectionAssert.AreEqual(a.Path, b.Path, "Literal reference path differs at cell " + cell);
            Assert.That((int)b.Failure, Is.EqualTo((int)a.Failure));
            Assert.That(b.GradeObservation.VisibleHeight, Is.EqualTo(a.GradeObservation.VisibleHeight));
            if (b.Path.Length > 1) visible++; else failed++;
        }
        Assert.That(visible, Is.GreaterThan(0));
        Assert.That(failed, Is.GreaterThan(0), "The fixture must exercise historical interruptions too.");
    }
}
