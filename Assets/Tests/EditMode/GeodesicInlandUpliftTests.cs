using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicInlandUpliftTests
{
    private const float Magnitude = .02f;
    private const float Scale = GeodesicInlandUplift.DefaultScaleDegrees * Mathf.Deg2Rad;
    private static bool[] Hemisphere(GeodesicGridTopology t) => t.CellDirections.Select(d => d.y < 0f).ToArray();

    [Test]
    public void ZeroPreservesEveryBaselineBit()
    {
        var topology = GeodesicGridTopology.Build(3);
        var field = new GeodesicInlandUplift(topology, Hemisphere(topology));
        foreach (Vector3 d in topology.CellDirections)
        {
            float baseline = 8f + PlanetTerrainSampler.EvaluateHeight(d, 123456, PlanetTerrainSettings.Earthlike);
            Assert.That(BitConverter.GetBytes(GeodesicInlandUplift.Add(baseline, field.SampleOffset(d, 0f))),
                Is.EqualTo(BitConverter.GetBytes(baseline)));
        }
    }

    [Test]
    public void CoastAndOceanAreZeroIncludingMixedTriangleInteriors()
    {
        var t = GeodesicGridTopology.Build(3); var ocean = Hemisphere(t);
        var field = new GeodesicInlandUplift(t, ocean);
        int coastalLand = 0, mixed = 0;
        for (int c = 0; c < t.CellCount; c++)
        {
            bool coastal = !ocean[c] && Enumerable.Range(0, t.NeighborCounts[c]).Any(n => ocean[t.Neighbors6[c * 6 + n]]);
            if (coastal) coastalLand++;
            if (coastal || ocean[c]) Assert.That(field.SampleOffset(t.CellDirections[c], Magnitude, c), Is.Zero);
        }
        for (int i = 0; i < t.Triangles.Length; i += 3)
        {
            int a = t.Triangles[i], b = t.Triangles[i + 1], c = t.Triangles[i + 2];
            if (!(ocean[a] || ocean[b] || ocean[c]) || (ocean[a] && ocean[b] && ocean[c])) continue;
            mixed++;
            Vector3 d = (t.CellDirections[a] + t.CellDirections[b] + t.CellDirections[c]).normalized;
            Assert.That(field.SampleOffset(d, Magnitude), Is.Zero);
        }
        Assert.That(coastalLand, Is.GreaterThan(0)); Assert.That(mixed, Is.GreaterThan(0));
        Assert.That(GeodesicInlandUplift.Offset(Scale * .001f, Magnitude, Scale), Is.LessThan(1e-8f));
    }

    [Test]
    public void OffsetIsMonotonicAndScalesLinearlyWithMagnitude()
    {
        float previous = 0;
        for (int i = 0; i <= 200; i++)
        {
            float d = Scale * i / 100f;
            float value = GeodesicInlandUplift.Offset(d, Magnitude, Scale);
            Assert.That(value, Is.GreaterThanOrEqualTo(previous));
            Assert.That(GeodesicInlandUplift.Offset(d, 2f * Magnitude, Scale), Is.EqualTo(2f * value));
            previous = value;
        }
        Assert.That(previous, Is.EqualTo(Magnitude));
    }

    [Test]
    public void SameSeedIsDeterministicAndInputsRemainUntouched()
    {
        var t = GeodesicGridTopology.Build(3);
        var heights = t.CellDirections.Select(d => 8f + PlanetTerrainSampler.EvaluateHeight(d, 123456, PlanetTerrainSettings.Earthlike)).ToArray();
        var mask = GeodesicOceanConnectivity.Build(t, heights, 8f, true, true, .001f).OceanMask;
        var beforeMask = mask.ToArray(); var beforeHeights = heights.ToArray(); var beforeTriangles = t.Triangles.ToArray();
        var a = new GeodesicInlandUplift(t, mask); var b = new GeodesicInlandUplift(t, mask);
        for (int i = 0; i < t.CellCount; i++)
        {
            Assert.That(a.CellDistanceRadians(i), Is.EqualTo(b.CellDistanceRadians(i)));
            Assert.That(a.SampleOffset(t.CellDirections[i], Magnitude), Is.EqualTo(b.SampleOffset(t.CellDirections[i], Magnitude)));
        }
        CollectionAssert.AreEqual(beforeHeights, heights); CollectionAssert.AreEqual(beforeMask, mask);
        CollectionAssert.AreEqual(beforeTriangles, t.Triangles);
    }

    [Test]
    public void ExcludedInlandSeaDoesNotResetDistance()
    {
        var t = GeodesicGridTopology.Build(4);
        int north = Array.IndexOf(t.CellDirections, t.CellDirections.OrderByDescending(d => d.y).First());
        var h = t.CellDirections.Select(d => d.y < 0 ? 7f : 9f).ToArray(); h[north] = 7f;
        var connectivity = GeodesicOceanConnectivity.Build(t, h, 8f, true, true, .001f);
        Assert.That(connectivity.PotentialLakeBasin[north], Is.True);
        var retained = new GeodesicInlandUplift(t, connectivity.OceanMask);
        var hemisphere = new GeodesicInlandUplift(t, Hemisphere(t));
        for (int c = 0; c < t.CellCount; c++)
            Assert.That(retained.CellDistanceRadians(c), Is.EqualTo(hemisphere.CellDistanceRadians(c)));
        Assert.That(retained.CellOffset(north, Magnitude), Is.EqualTo(Magnitude));
    }

    [Test]
    public void NoCoastProducesNoUpliftAndInvalidMagnitudeIsSafe()
    {
        var t = GeodesicGridTopology.Build(1);
        foreach (bool allOcean in new[] { false, true })
        {
            var field = new GeodesicInlandUplift(t, Enumerable.Repeat(allOcean, t.CellCount).ToArray());
            Assert.That(field.HasCoast, Is.False);
            Assert.That(field.SampleOffset(Vector3.up, Magnitude), Is.Zero);
        }
        Assert.That(GeodesicInlandUplift.NormalizeMagnitude(float.NaN), Is.Zero);
        Assert.That(GeodesicInlandUplift.NormalizeMagnitude(float.PositiveInfinity), Is.Zero);
        Assert.That(GeodesicInlandUplift.NormalizeMagnitude(-1f), Is.Zero);
        Assert.That(GeodesicInlandUplift.NormalizeMagnitude(1f), Is.EqualTo(GeodesicInlandUplift.MaximumMagnitude));
    }

    [Test]
    public void RenderSubdivisionKeepsSharedLocationsIdenticalAndTriangleInterpolationContinuous()
    {
        var t = GeodesicGridTopology.Build(3); var field = new GeodesicInlandUplift(t, Hemisphere(t));
        var coarse = IcosphereRenderGeometryCache.GetOrBuild(4); var fine = IcosphereRenderGeometryCache.GetOrBuild(5);
        var cm = IcosphereDirectionMappingBuilder.Build(t, coarse); var fm = IcosphereDirectionMappingBuilder.Build(t, fine);
        for (int i = 0; i < coarse.VertexCount; i++)
            Assert.That(field.SampleOffset(coarse.UnitVertices[i], Magnitude, cm.Samples[i].NearestCell),
                Is.EqualTo(field.SampleOffset(fine.UnitVertices[i], Magnitude, fm.Samples[i].NearestCell)));
        // Sample every finer vertex as well: no triangle-fan gaps at edges or pentagons.
        foreach (var d in fine.UnitVertices) Assert.That(field.SampleOffset(d, Magnitude), Is.InRange(0f, Magnitude));
        for (int i = 0; i < t.Triangles.Length; i += 3)
        {
            var a = t.CellDirections[t.Triangles[i]]; var b = t.CellDirections[t.Triangles[i + 1]];
            var edge = (a + b).normalized; var normal = Vector3.Cross(a, b).normalized;
            Assert.That(field.SampleOffset((edge + normal * 1e-6f).normalized, Magnitude),
                Is.EqualTo(field.SampleOffset((edge - normal * 1e-6f).normalized, Magnitude)).Within(1e-6f));
        }
    }

    [Test]
    public void AngularScaleIsBroadlyConsistentAcrossSimulationResolutions()
    {
        var a = GeodesicGridTopology.Build(4); var b = GeodesicGridTopology.Build(5);
        var fa = new GeodesicInlandUplift(a, Hemisphere(a)); var fb = new GeodesicInlandUplift(b, Hemisphere(b));
        foreach (float latitude in new[] { 10f, 20f, 40f, 70f })
        {
            var d = new Vector3(Mathf.Cos(latitude * Mathf.Deg2Rad), Mathf.Sin(latitude * Mathf.Deg2Rad), 0f);
            Assert.That(fa.SampleOffset(d, Magnitude) / Magnitude,
                Is.EqualTo(fb.SampleOffset(d, Magnitude) / Magnitude).Within(.15f));
        }
    }

    [Test]
    public void DiagnosticsReportActualFinalDifferenceWithoutAnotherGeneration()
    {
        var d = new GeodesicInlandUplift.Diagnostics();
        d.Add(false, 0f, 8f, 8f); d.Add(false, Scale, 8f, 8.02f); d.Add(true, 0f, 7f, 7f);
        string report = d.Describe(Magnitude, Scale, true);
        StringAssert.Contains("[GeodesicInlandUplift]", report);
        StringAssert.Contains("landVertices=2, affected=1", report);
        StringAssert.Contains("nearCoastMeanUplift=0", report);
        StringAssert.Contains("maximumFinalTerrainDifference=0.02", report);
        StringAssert.Contains("interiorQuartileMeanUplift=0.02", report);
    }
}
