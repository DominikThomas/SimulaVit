using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicRiverUphillDebugTests
{
    [Test]
    public void FixedSlopeAnchorsAreYellowOrangeAndRed()
    {
        Assert.That(GeodesicRiverUphillDebug.SeverityColor(.0001), Is.EqualTo(Color.yellow));
        Assert.That(GeodesicRiverUphillDebug.SeverityColor(.01), Is.EqualTo(new Color(1, .5f, 0, 1)));
        Assert.That(GeodesicRiverUphillDebug.SeverityColor(.1), Is.EqualTo(Color.red));
        Assert.That(GeodesicRiverUphillDebug.SeverityColor(100), Is.EqualTo(Color.red));
    }

    [Test]
    public void SeverityUsesSurfaceDistanceAndIgnoresNumericalNoise()
    {
        var path = new[] { Vector3.right, new Vector3((float)Math.Cos(.01), (float)Math.Sin(.01), 0) };
        var shortReach = new RiverUphillDebugReach(); var longReach = new RiverUphillDebugReach();
        GeodesicRiverUphillDebug.Measure(shortReach, path, new[] { 8d, 8.001 }, 8, .000002);
        GeodesicRiverUphillDebug.Measure(longReach, path, new[] { 8d, 8.001 }, 80, .000002);
        Assert.That(shortReach.MaximumSlope, Is.EqualTo(longReach.MaximumSlope * 10).Within(1e-10));
        Assert.That(shortReach.MaximumStep, Is.EqualTo(.001).Within(1e-12));
        Assert.That(shortReach.TotalRise, Is.EqualTo(.001).Within(1e-12));
        var noise = new RiverUphillDebugReach();
        GeodesicRiverUphillDebug.Measure(noise, path, new[] { 8d, 8.000001 }, 8, .000002);
        Assert.That(noise.MaximumSlope, Is.Zero);
    }

    [Test]
    public void MeasuredSlightMediumAndStrongRisesUseTheFixedColourScale()
    {
        var points = new[] { Vector3.right, new Vector3((float)Math.Cos(.01), (float)Math.Sin(.01), 0) };
        var expected = new[] { Color.yellow, new Color(1, .5f, 0, 1), Color.red };
        var slopes = new[] { .0001, .01, .1 };
        for (int i = 0; i < slopes.Length; i++)
        {
            var reach = new RiverUphillDebugReach();
            GeodesicRiverUphillDebug.Measure(reach, points, new[] { 8d, 8 + slopes[i] * .08 }, 8, .000002);
            Color actual = GeodesicRiverUphillDebug.SeverityColor(reach.MaximumSlope);
            Assert.That(actual.r, Is.EqualTo(expected[i].r).Within(1e-5));
            Assert.That(actual.g, Is.EqualTo(expected[i].g).Within(1e-5));
            Assert.That(actual.b, Is.EqualTo(expected[i].b).Within(1e-5));
        }
    }

    [Test]
    public void MultipleSamplesRetainTotalRiseAndDoNotHideCumulativeUphillSteps()
    {
        var points = Enumerable.Range(0, 5).Select(i => new Vector3((float)Math.Cos(i*.01), (float)Math.Sin(i*.01), 0)).ToArray();
        var reach = new RiverUphillDebugReach();
        GeodesicRiverUphillDebug.Measure(reach, points, new[] { 8d, 8.000001, 8.000002, 8.000003, 8.000004 }, 8, .000002);
        Assert.That(reach.MaximumStep, Is.EqualTo(.000001).Within(1e-10));
        Assert.That(reach.TotalRise, Is.EqualTo(.000004).Within(1e-10));
        Assert.That(reach.MaximumSlope, Is.GreaterThan(0));
    }

    [Test]
    public void DebugObservationPreservesTopologyRunoffAreasNetworksAndOceanTerminations()
    {
        Vector3[] v = { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) };
        var visible = new[] { 0f, 1f, 0f, 1f }; var hydro = new[] { 2f, 1f, 2f, 1f };
        var tin = new GeodesicTinHydrology(v, new[] { 0, 1, 2, 1, 3, 2 }, visible, hydro,
            visibleBumpTolerance: .001, onSphere: false, allowBoundaryOutflow: true);
        tin.Accumulate(); tin.ExtractNetworks(.25);
        var receivers = (int[])tin.TriangleReceiver.Clone(); var areas = (double[])tin.DrainageArea.Clone();
        var runoff = (double[])tin.AccumulatedRunoff.Clone();
        var paths = tin.Networks.Select(n => n.Centreline.ToArray()).ToArray();
        var ends = tin.Networks.Select(n => n.Termination).ToArray();
        var rejected = new List<TinPoint[]>();
        tin.ExtractNetworks(.25, rejectedUphill: (source, path) => rejected.Add(path));
        Assert.That(rejected.Count, Is.GreaterThan(0));
        Assert.That(rejected.All(p => tin.Height(p.Last(), true) > tin.Height(p[p.Length - 2], true)), Is.True);
        CollectionAssert.AreEqual(receivers, tin.TriangleReceiver);
        CollectionAssert.AreEqual(areas, tin.DrainageArea);
        CollectionAssert.AreEqual(runoff, tin.AccumulatedRunoff);
        CollectionAssert.AreEqual(visible, tin.VisibleHeight);
        CollectionAssert.AreEqual(ends, tin.Networks.Select(n => n.Termination));
        for (int i = 0; i < paths.Length; i++) CollectionAssert.AreEqual(paths[i], tin.Networks[i].Centreline);
        var repeated = new List<TinPoint[]>();
        tin.ExtractNetworks(.25, rejectedUphill: (source, path) => repeated.Add(path));
        Assert.That(repeated.Count, Is.EqualTo(rejected.Count));
        for (int i = 0; i < rejected.Count; i++) CollectionAssert.AreEqual(rejected[i], repeated[i]);
    }

    [Test]
    public void AcceptedDownhillAndNonGradeBasinFailuresDoNotProduceDebugReaches()
    {
        foreach (var f in new[] { GeodesicTinHydrologyTests.Grid((x, y) => 10 - x - y, size: 8),
            GeodesicTinHydrologyTests.Grid((x, y) => x*x + (y-1)*(y-1), size: 8, boundaryOutflow: false) })
        {
            f.Tin.Accumulate();
            int rejected = 0;
            f.Tin.ExtractNetworks(.01, rejectedUphill: (source, path) => rejected++);
            Assert.That(rejected, Is.Zero);
        }
    }
}
