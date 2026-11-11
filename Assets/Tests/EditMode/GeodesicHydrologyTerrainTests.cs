using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicHydrologyTerrainTests
{
    [Test]
    public void FineDetailRemovedKeepsEveryConfiguredRidgeOctaveAndAllBroadComponents()
    {
        var g = IcosphereRenderGeometryCache.GetOrBuild(3);
        var settings = PlanetTerrainSettings.Earthlike;
        settings.mountainAmplitude = .8f; settings.mountainScale = 4.25f;
        settings.continentScale = .85f; settings.ridgeSharpness = 1.25f;
        bool fineMadeDifference = false, higherRidgesMadeDifference = false;
        for (int octaves = 1; octaves <= 8; octaves++)
        {
            settings.octaves = octaves;
            var noFine = settings; noFine.fineDetailAmplitude = 0f;
            var samples = g.UnitVertices.Select(d => PlanetTerrainSampler.Evaluate(d, 123456, settings)).ToArray();
            var visible = samples.Select(s => 8f + s.HeightOffset).ToArray();
            // Match the stored float radii used by the render cache. Keeping the entire
            // subtraction expression in registers can retain excess precision in Mono.
            var expectedRadii = g.UnitVertices.Select(d => 8f + PlanetTerrainSampler.EvaluateHeight(d, 123456, noFine)).ToArray();
            var field = GeodesicHydrologyTerrain.Build(g.UnitVertices, visible, new bool[visible.Length], 123456,
                settings, 8f, HydrologyTerrainMode.FineDetailRemoved);
            CollectionAssert.AreEqual(field, GeodesicHydrologyTerrain.Build(g.UnitVertices, visible, new bool[visible.Length],
                123456, settings, 8f, HydrologyTerrainMode.FineDetailRemoved));
            for (int i = 0; i < samples.Length; i++)
            {
                var full = samples[i]; var expected = PlanetTerrainSampler.Evaluate(g.UnitVertices[i], 123456, noFine);
                Assert.That(expected.ContinentValue, Is.EqualTo(full.ContinentValue));
                Assert.That(expected.MountainMask, Is.EqualTo(full.MountainMask));
                Assert.That(expected.RidgeValue, Is.EqualTo(full.RidgeValue));
                Assert.That(expected.RidgeValue * expected.MountainMask * settings.mountainAmplitude,
                    Is.EqualTo(full.RidgeValue * full.MountainMask * settings.mountainAmplitude));
                Assert.That(full.LargeScaleHeightOffset, Is.EqualTo(expected.HeightOffset));
                Assert.That(field[i], Is.EqualTo(expectedRadii[i]));
                Assert.That(float.IsNaN(field[i]) || float.IsInfinity(field[i]), Is.False);
                fineMadeDifference |= full.HeightOffset != full.LargeScaleHeightOffset;
                if (octaves > 2)
                    higherRidgesMadeDifference |= full.LargeScaleHeightOffset != PlanetTerrainSampler.EvaluateHydrologyLowFrequency(g.UnitVertices[i], 123456, settings);
            }
            Assert.That(settings.octaves, Is.EqualTo(octaves));
        }
        Assert.That(fineMadeDifference, Is.True); Assert.That(higherRidgesMadeDifference, Is.True);
    }

    [Test]
    public void FineDetailRemovedReusesCachedPairAndPreservesCompletedAdjustmentsAndOcean()
    {
        var g = IcosphereRenderGeometryCache.GetOrBuild(4); var settings = PlanetTerrainSettings.Earthlike;
        var samples = g.UnitVertices.Select(d => PlanetTerrainSampler.Evaluate(d, 123456, settings)).ToArray();
        var raw = samples.Select(s => 8f + s.HeightOffset).ToArray();
        var large = samples.Select(s => 8f + s.LargeScaleHeightOffset).ToArray();
        var visible = raw.Select((r, i) => r + (i % 7 == 0 ? .015f : 0f)).ToArray();
        var ocean = raw.Select(r => r < 8f).ToArray();
        var rawCopy = raw.ToArray(); var largeCopy = large.ToArray(); var visibleCopy = visible.ToArray();
        var cached = GeodesicHydrologyTerrain.Build(g.UnitVertices, visible, ocean, 123456, settings, 8f,
            HydrologyTerrainMode.FineDetailRemoved, largeScaleRadii: large, rawRadii: raw);
        var fallback = GeodesicHydrologyTerrain.Build(g.UnitVertices, visible, ocean, 123456, settings, 8f, HydrologyTerrainMode.FineDetailRemoved);
        CollectionAssert.AreEqual(cached, fallback);
        CollectionAssert.AreEqual(rawCopy, raw); CollectionAssert.AreEqual(largeCopy, large); CollectionAssert.AreEqual(visibleCopy, visible);
        for (int i = 0; i < cached.Length; i++)
            Assert.That(cached[i], Is.EqualTo(ocean[i] ? visible[i] : visible[i] + (large[i] - raw[i])));
        CollectionAssert.AreEqual(visible, GeodesicHydrologyTerrain.Build(g.UnitVertices, visible, ocean, 123456, settings, 8f,
            HydrologyTerrainMode.FineDetailRemoved, displacement: false, largeScaleRadii: large, rawRadii: raw));
    }

    [Test]
    public void FineDetailRemovedIsExactNoOpForUnqualifiedOrZeroFineDetail()
    {
        var g = IcosphereRenderGeometryCache.GetOrBuild(3);
        foreach (int scenario in new[] { 0, 1, 2 })
        {
            var settings = PlanetTerrainSettings.Earthlike;
            if (scenario == 0) settings.fineDetailScale = settings.mountainScale;
            else if (scenario == 1) settings.fineDetailAmplitude = settings.mountainAmplitude;
            else settings.fineDetailAmplitude = 0f;
            var visible = g.UnitVertices.Select(d => 8f + PlanetTerrainSampler.EvaluateHeight(d, 123456, settings) + .012f).ToArray();
            CollectionAssert.AreEqual(visible, GeodesicHydrologyTerrain.Build(g.UnitVertices, visible, new bool[visible.Length],
                123456, settings, 8f, HydrologyTerrainMode.FineDetailRemoved));
        }
    }

    [Test]
    public void OffReturnsOriginalFieldAndExplicitNullPreservesAdaptiveTin()
    {
        var g = IcosphereRenderGeometryCache.GetOrBuild(2);
        var visible = g.UnitVertices.Select(d => 8f + .1f * d.z).ToArray();
        var ocean = visible.Select(h => h < 8f).ToArray();
        Assert.That(GeodesicHydrologyTerrain.Build(g.UnitVertices, visible, ocean, 123456,
            PlanetTerrainSettings.Earthlike, 8f, false), Is.SameAs(visible));
        var a = GeodesicTinHydrology.BuildAdaptive(g.UnitVertices, g.Triangles, visible, ocean, 8f, 8d, .000002d, .0001d, .05d, .01d);
        var b = GeodesicTinHydrology.BuildAdaptive(g.UnitVertices, g.Triangles, visible, ocean, 8f, 8d, .000002d, .0001d, .05d, .01d,
            hydrologyTerrainHeights: null);
        CollectionAssert.AreEqual(a.HydrologyHeight, b.HydrologyHeight);
        CollectionAssert.AreEqual(a.TriangleReceiver, b.TriangleReceiver);
        CollectionAssert.AreEqual(a.AccumulatedRunoff, b.AccumulatedRunoff);
        CollectionAssert.AreEqual(a.Networks.Select(n => n.Termination), b.Networks.Select(n => n.Termination));
    }

    [Test]
    public void LowFrequencyIsDeterministicFiniteAndPreservesBroadComponents()
    {
        var settings = PlanetTerrainSettings.Earthlike;
        var original = settings;
        foreach (Vector3 d in IcosphereRenderGeometryCache.GetOrBuild(3).UnitVertices)
        {
            float a = PlanetTerrainSampler.EvaluateHydrologyLowFrequency(d, 123456, settings);
            float b = PlanetTerrainSampler.EvaluateHydrologyLowFrequency(d, 123456, settings);
            Assert.That(a, Is.EqualTo(b)); Assert.That(float.IsNaN(a) || float.IsInfinity(a), Is.False);
            var full = PlanetTerrainSampler.Evaluate(d, 123456, settings);
            var low = PlanetTerrainSampler.Evaluate(d, 123456, PlanetTerrainSampler.HydrologyLowFrequencySettings(settings));
            Assert.That(low.ContinentValue, Is.EqualTo(full.ContinentValue));
            Assert.That(low.MountainMask, Is.EqualTo(full.MountainMask));
        }
        Assert.That(settings, Is.EqualTo(original));
        var derived = PlanetTerrainSampler.HydrologyLowFrequencySettings(settings);
        Assert.That(derived.fineDetailAmplitude, Is.Zero); Assert.That(derived.octaves, Is.EqualTo(2));
        settings.octaves = 1;
        Assert.That(PlanetTerrainSampler.HydrologyLowFrequencySettings(settings).octaves, Is.EqualTo(1));
    }

    [Test]
    public void LowFrequencyReducesRoughnessWithoutEditingInputsOrOceanBathymetry()
    {
        var g = IcosphereRenderGeometryCache.GetOrBuild(5);
        var settings = PlanetTerrainSettings.Earthlike;
        var visible = g.UnitVertices.Select(d => 8f + PlanetTerrainSampler.EvaluateHeight(d, 123456, settings)).ToArray();
        var ocean = visible.Select(h => h < 8f).ToArray();
        var before = (float[])visible.Clone(); var mask = (bool[])ocean.Clone(); var directions = g.UnitVertices.ToArray();
        var low = GeodesicHydrologyTerrain.Build(g.UnitVertices, visible, ocean, 123456, settings, 8f, true);
        CollectionAssert.AreEqual(before, visible); CollectionAssert.AreEqual(mask, ocean);
        CollectionAssert.AreEqual(directions, g.UnitVertices);
        Assert.That(GeodesicHydrologyTerrain.Roughness(low, g.Triangles),
            Is.LessThan(GeodesicHydrologyTerrain.Roughness(visible, g.Triangles)));
        for (int i = 0; i < low.Length; i++) if (ocean[i]) Assert.That(low[i], Is.EqualTo(visible[i]));
        CollectionAssert.AreEqual(visible, GeodesicHydrologyTerrain.Build(g.UnitVertices, visible, ocean, 123456, settings, 8f, true, false));
    }

    [Test]
    public void SeparateFieldControlsGradientWithoutRedefiningOceanOrFallingBackToVisible()
    {
        var g = IcosphereRenderGeometryCache.GetOrBuild(3);
        var settings = PlanetTerrainSettings.Earthlike;
        var surface = g.UnitVertices.Select(d => d * (8f + PlanetTerrainSampler.EvaluateHeight(d, 123456, settings))).ToArray();
        var visible = surface.Select(p => p.magnitude).ToArray(); var ocean = visible.Select(h => h < 8f).ToArray();
        var low = GeodesicHydrologyTerrain.Build(g.UnitVertices, visible, ocean, 123456, settings, 8f, true);
        var a = GeodesicTinHydrology.BuildAdaptive(g.UnitVertices, g.Triangles, visible, ocean, 8f, 8d, .000002d, .0001d, 0d, .01d, completedSurface: surface);
        var b = GeodesicTinHydrology.BuildAdaptive(g.UnitVertices, g.Triangles, visible, ocean, 8f, 8d, .000002d, .0001d, 0d, .01d,
            completedSurface: surface, hydrologyTerrainHeights: low);
        CollectionAssert.AreEqual(low, b.HydrologyHeight); CollectionAssert.AreEqual(visible, b.VisibleHeight);
        bool changed = false;
        for (int t = 0; t < b.TriangleCount; t++)
        {
            Assert.That(b.IsOcean(b.Centroid(t)), Is.EqualTo(a.IsOcean(a.Centroid(t))));
            Assert.That(b.CompletedVisibleRadius(b.Centroid(t)), Is.EqualTo(a.CompletedVisibleRadius(a.Centroid(t))));
            Assert.That(b.TriangleReceiver[t], Is.InRange(-1, b.TriangleCount - 1));
            Assert.That(double.IsNaN(b.AccumulatedRunoff[t]) || double.IsInfinity(b.AccumulatedRunoff[t]), Is.False);
            Assert.That(b.LocalArea[t], Is.GreaterThan(0d));
            var gradient = b.DownhillGradient(t);
            Assert.That(float.IsNaN(gradient.sqrMagnitude) || float.IsInfinity(gradient.sqrMagnitude), Is.False);
            changed |= !gradient.Equals(a.DownhillGradient(t));
        }
        Assert.That(changed, Is.True);
    }
}
