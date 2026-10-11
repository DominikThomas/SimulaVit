using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicTinHydrologyTests
{
    public sealed class Fixture
    {
        public Vector3[] Vertices;
        public int[] Triangles;
        public float[] Heights;
        public GeodesicTinHydrology Tin;
        public TinPoint At(float x, float y)
        {
            for (int t = 0; t < Triangles.Length / 3; t++)
            {
                Vector3 a = Vertices[Triangles[t * 3]], b = Vertices[Triangles[t * 3 + 1]], c = Vertices[Triangles[t * 3 + 2]];
                double dx = b.x - (double)a.x, dy = b.y - (double)a.y, ex = c.x - (double)a.x, ey = c.y - (double)a.y;
                double determinant = dx * ey - dy * ex;
                double u = ((x - (double)a.x) * ey - (y - (double)a.y) * ex) / determinant;
                double v = (dx * (y - (double)a.y) - dy * (x - (double)a.x)) / determinant;
                if (u >= -1e-8 && v >= -1e-8 && u + v <= 1d + 1e-8)
                    return new TinPoint(t, Math.Max(0d, 1d - u - v), Math.Max(0d, u), Math.Max(0d, v));
            }
            throw new InvalidOperationException("Fixture point is outside its TIN.");
        }
    }

    public static Fixture Grid(Func<float, float, float> height, int size = 24, bool flip = false,
        float rotation = 0f, bool ocean = false, bool boundaryOutflow = true, float extent = 1f)
    {
        var vertices = new List<Vector3>(); var heights = new List<float>(); var indices = new List<int>();
        double cosine = Math.Cos(rotation), sine = Math.Sin(rotation);
        for (int y = 0; y <= size; y++)
        for (int x = 0; x <= size; x++)
        {
            double px = extent * (2d * x / size - 1d), py = extent * (2d * y / size - 1d);
            float wx = (float)(px * cosine - py * sine), wy = 1f + (float)(px * sine + py * cosine);
            vertices.Add(new Vector3(wx, wy, 0f)); heights.Add(height(wx, wy));
        }
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int a = y * (size + 1) + x, b = a + 1, c = a + size + 1, d = c + 1;
            if (flip) indices.AddRange(new[] { a, b, c, b, d, c });
            else indices.AddRange(new[] { a, b, d, a, d, c });
        }
        var f = new Fixture { Vertices = vertices.ToArray(), Triangles = indices.ToArray(), Heights = heights.ToArray() };
        f.Tin = new GeodesicTinHydrology(f.Vertices, f.Triangles, f.Heights, f.Heights,
            ocean ? f.Heights.Select(h => h <= 0f).ToArray() : null, 0f, epsilon: .000002d,
            visibleBumpTolerance: .00001d, onSphere: false, allowBoundaryOutflow: boundaryOutflow);
        return f;
    }

    private static TinPoint[] Trace(Fixture f, float x, float y, TinTermination expected)
    {
        TinPoint[] path = f.Tin.Trace(f.At(x, y), out var termination);
        Assert.That(termination, Is.EqualTo(expected));
        Assert.That(f.Tin.Validate(path), Is.True);
        for (int i = 1; i < path.Length; i++)
            Assert.That(f.Tin.Height(path[i]), Is.LessThanOrEqualTo(f.Tin.Height(path[i - 1]) + f.Tin.Epsilon));
        return path;
    }

    [Test]
    public void AnalyticTriangleGradientsAreFlatDownhillRidgeAndValleyFacing()
    {
        Vector3[] vertices = { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) };
        foreach (var sample in new[] { new[] { 2f, 2f, 2f }, new[] { 2f, 1f, 2f }, new[] { 1f, 2f, 1f }, new[] { 2f, 0f, 1f } })
        {
            var tin = new GeodesicTinHydrology(vertices, new[] { 0, 1, 2 }, sample, sample, onSphere: false);
            var downhill = tin.DownhillGradient(0);
            Assert.That(downhill.x, Is.EqualTo(sample[0] - sample[1]).Within(1e-7));
            Assert.That(downhill.y, Is.EqualTo(sample[0] - sample[2]).Within(1e-7));
        }
    }

    [Test]
    public void PlaneIsStraightDownhillThroughTriangleInteriors()
    {
        var f = Grid((x, y) => 10f - 2f * x - y);
        var path = Trace(f, -.7f, .23f, TinTermination.OceanCoast);
        Vector3 start = f.Tin.Direction(path[0]);
        foreach (var p in path)
        {
            Vector3 delta = f.Tin.Direction(p) - start;
            Assert.That(Math.Abs(delta.x - 2f * delta.y), Is.LessThan(2e-5));
        }
        Assert.That(path.Zip(path.Skip(1), (a, b) => a.Triangle == b.Triangle &&
            (a.A + b.A) > .02d && (a.B + b.B) > .02d && (a.C + b.C) > .02d).Any(x => x), Is.True);
    }

    [Test]
    public void VValleyConvergesAndContinuesDownItsBottom()
    {
        var f = Grid((x, y) => 3f * Math.Abs(x) + 2f - y);
        foreach (float x in new[] { -.6f, .6f })
        {
            var path = Trace(f, x, .2f, TinTermination.OceanCoast);
            Vector3 end = f.Tin.Direction(path.Last());
            Assert.That(Math.Abs(end.x), Is.LessThan(.00001f));
            Assert.That(end.y, Is.EqualTo(2f).Within(.00001f));
        }
    }

    [Test]
    public void RidgeDividesFlowOntoOppositeSides()
    {
        var f = Grid((x, y) => -2f * Math.Abs(x) + .01f * y);
        foreach (float x in new[] { -.2f, .2f })
        {
            var path = Trace(f, x, .7f, TinTermination.OceanCoast);
            Assert.That(f.Tin.Direction(path.Last()).x, Is.EqualTo(Math.Sign(x)).Within(.00001f));
        }
    }

    [Test]
    public void SaddleSplitsDrainageToOppositeDownstreamArms()
    {
        var f = Grid((x, y) => x * x - (y - 1f) * (y - 1f));
        var lower = Trace(f, .21f, .5f, TinTermination.OceanCoast);
        var upper = Trace(f, -.21f, 1.5f, TinTermination.OceanCoast);
        Assert.That(f.Tin.Direction(lower.Last()).y, Is.LessThan(.001f));
        Assert.That(f.Tin.Direction(upper.Last()).y, Is.GreaterThan(1.999f));
    }

    [Test]
    public void ClosedBowlStopsAtItsMinimumWithoutInventingAnEscape()
    {
        var f = Grid((x, y) => x * x + (y - 1f) * (y - 1f), boundaryOutflow: false);
        var path = Trace(f, .61f, .27f, TinTermination.TrueClosedBasin);
        Assert.That(f.Tin.Direction(path.Last()).magnitude, Is.EqualTo(1f).Within(.00001f));
        Assert.That(f.Tin.Height(path.Last()), Is.LessThan(.00001d));
    }

    [Test]
    public void TinyNumericalFlatRoutesDeterministicallyWithoutEpsilonStaircase()
    {
        var f = Grid((x, y) => 1f + (float)Math.Sin(x * 100f + y * 100f) * 1e-7f);
        var first = Trace(f, .17f, 1.13f, TinTermination.OceanCoast);
        var second = Trace(f, .17f, 1.13f, TinTermination.OceanCoast);
        CollectionAssert.AreEqual(first.Select(f.Tin.Direction), second.Select(f.Tin.Direction));
        double lowest = f.Tin.Height(first[0]);
        foreach (var p in first) { Assert.That(f.Tin.Height(p), Is.LessThanOrEqualTo(lowest + f.Tin.Epsilon)); lowest = Math.Min(lowest, f.Tin.Height(p)); }
    }

    [Test]
    public void FlatWithoutAnExitIsClassifiedInsteadOfConnectedToAFakeReceiver()
    {
        var f = Grid((x, y) => 1f, boundaryOutflow: false);
        var path = f.Tin.Trace(f.At(.17f, 1.13f), out var end);
        Assert.That(end, Is.EqualTo(TinTermination.FlatRequiringRouting));
        Assert.That(path.Length, Is.EqualTo(1));
    }

    [Test]
    public void FlatPlateauConnectsContinuouslyToItsRealDownhillExit()
    {
        var f = Grid((x, y) => y < 1f ? 1f : 2f - y, ocean: true, boundaryOutflow: false);
        var path = Trace(f, .17f, .23f, TinTermination.OceanCoast);
        Assert.That(f.Tin.Direction(path.Last()).y, Is.EqualTo(2f).Within(.00001f));
    }

    [Test]
    public void LevelValleyGutterIsClassifiedAsAFlatInsteadOfAClosedBowl()
    {
        var f = Grid((x, y) => 3f * Math.Abs(x) + (y < 1f ? 1f : 2f - y), boundaryOutflow: false);
        var path = Trace(f, .61f, .23f, TinTermination.FlatRequiringRouting);
        Assert.That(Math.Abs(f.Tin.Direction(path.Last()).x), Is.LessThan(.00001f));
    }

    [Test]
    public void WindingValleyIsApproximatelyInvariantToDiagonalFlipsAndGridRotation()
    {
        float Height(float x, float y) { float distance = x - .3f * (float)Math.Sin(Math.PI * y); return 12f * distance * distance + 2f - y; }
        var fixtures = new[] { Grid(Height, 96, ocean: true, boundaryOutflow: false, extent: 2f),
            Grid(Height, 96, flip: true, ocean: true, boundaryOutflow: false, extent: 2f),
            Grid(Height, 96, rotation: .47f, ocean: true, boundaryOutflow: false, extent: 2f) };
        var paths = fixtures.Select(f => Trace(f, .61f, .15f, TinTermination.OceanCoast).Select(f.Tin.Direction).ToArray()).ToArray();
        foreach (var path in paths)
        {
            Assert.That(path.Last().y, Is.GreaterThan(1.98f));
            foreach (Vector3 p in path.Where(p => p.y > .7f && p.y < 1.9f))
                Assert.That(Math.Abs(p.x - .3f * Math.Sin(Math.PI * p.y)), Is.LessThan(.11d));
        }
        for (int i = 1; i < paths.Length; i++)
        {
            double distance = Math.Max(Hausdorff(paths[0], paths[i]), Hausdorff(paths[i], paths[0]));
            Console.WriteLine($"TIN triangulation invariance fixture={i} maxWorldDeviation={distance:G6}");
            Assert.That(distance, Is.LessThan(.14d), "World-space streamline should not rotate with triangle edges.");
        }
    }

    private static double Hausdorff(Vector3[] a, Vector3[] b)
    {
        double maximum = 0d;
        foreach (Vector3 p in a)
        {
            double minimum = double.PositiveInfinity;
            for (int i = 1; i < b.Length; i++)
            {
                Vector3 d = b[i] - b[i - 1]; float t = Mathf.Clamp01(Vector3.Dot(p - b[i - 1], d) / Math.Max(1e-20f, d.sqrMagnitude));
                minimum = Math.Min(minimum, (p - (b[i - 1] + d * t)).magnitude);
            }
            maximum = Math.Max(maximum, minimum);
        }
        return maximum;
    }

    [Test]
    public void HeavilyFilteredDownhillFieldCannotAuthorizeAMaterialVisibleClimb()
    {
        Vector3[] v = { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) };
        var visible = new[] { 0f, 1f, 0f }; var hydro = new[] { 2f, 1f, 2f };
        var tin = new GeodesicTinHydrology(v, new[] { 0, 1, 2 }, visible, hydro,
            visibleBumpTolerance: .001d, onSphere: false, allowBoundaryOutflow: true);
        var path = tin.Trace(tin.Centroid(0), out var end);
        Assert.That(end, Is.EqualTo(TinTermination.RoutingFailure));
        Assert.That(path.Length, Is.EqualTo(1));
        Assert.That(tin.HasVisibleDisagreement, Is.True);
        Assert.That(tin.Diagnostics.RejectedVisibleSteps, Is.GreaterThan(0));
    }

    [Test]
    public void SmoothingThatRisesHydrologicallyRetainsTheOriginalDownhillPath()
    {
        Vector3[] v = { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) };
        var h = new[] { 1f, 0f, 1f };
        var tin = new GeodesicTinHydrology(v, new[] { 0, 1, 2 }, h, h, onSphere: false);
        var raw = new[] { new TinPoint(0, .7d, .2d, .1d), new TinPoint(0, .1d, .8d, .1d) };
        var badSpline = new[] { raw[0], new TinPoint(0, .8d, .1d, .1d), raw[1] };
        Assert.That(tin.AcceptProcessedPath(raw, badSpline), Is.SameAs(raw));
        Assert.That(tin.Validate(raw), Is.True);
        Assert.That(tin.Diagnostics.RejectedPostProcessing, Is.EqualTo(1));
    }

    [Test]
    public void RejectedVisibleContinuationDoesNotPropagateRunoffOrInventAConnection()
    {
        Vector3[] v = { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f) };
        var visible = new[] { 0f, 1f, 0f, 1f }; var hydro = new[] { 2f, 1f, 2f, 1f };
        var tin = new GeodesicTinHydrology(v, new[] { 0, 1, 2, 1, 3, 2 }, visible, hydro,
            visibleBumpTolerance: .001d, onSphere: false, allowBoundaryOutflow: true);
        tin.Accumulate();
        Assert.That(tin.DrainageArea[0], Is.EqualTo(tin.LocalArea[0]));
        Assert.That(tin.DrainageArea[1], Is.EqualTo(tin.LocalArea[1]));
        Assert.That(tin.TriangleReceiver.All(t => t < 0), Is.True);
        tin.ExtractNetworks(.25d);
        Assert.That(tin.Networks.All(n => n.Termination == TinTermination.RoutingFailure && n.Samples.Length == 1), Is.True);
    }

    [Test]
    public void AreaParcelsAndWholeNetworksStopAtValidTerminationsAndShareExactJunctions()
    {
        var f = Grid((x, y) => 3f * Math.Abs(x) + 2f - y, size: 16);
        f.Tin.Accumulate();
        Assert.That(f.Tin.LocalArea.Sum(), Is.EqualTo(4d).Within(1e-6));
        CollectionAssert.AreEqual(f.Tin.DrainageArea, f.Tin.AccumulatedRunoff);
        f.Tin.ExtractNetworks(.03d);
        Assert.That(f.Tin.Networks.Count, Is.GreaterThan(0));
        Assert.That(f.Tin.Diagnostics.HydrologyUphillSegments, Is.Zero);
        Assert.That(f.Tin.Diagnostics.VisibleMaterialUphillSegments, Is.Zero);
        foreach (var network in f.Tin.Networks)
        {
            Assert.That(f.Tin.Validate(network.Samples), Is.True);
            if (network.Termination == TinTermination.Confluence)
                Assert.That(f.Tin.Networks[network.JunctionNetwork].Centreline.Contains(network.Centreline.Last()), Is.True);
        }
        f.Tin.Accumulate(0d);
        Assert.That(f.Tin.AccumulatedRunoff.Sum(), Is.Zero);
        f.Tin.ExtractNetworks(.03d);
        Assert.That(f.Tin.Networks.Count, Is.Zero);
    }

    [Test]
    public void FilteringIsWeakMeasuredAndRawTerrainRemainsAvailable()
    {
        var f = Grid((x, y) => 3f * Math.Abs(x) + 2f - y, size: 16);
        var raw = GeodesicTinHydrology.FilterVisible(f.Vertices, f.Triangles, f.Heights, 0d, out var meanRaw, out var maxRaw);
        CollectionAssert.AreEqual(f.Heights, raw); Assert.That(meanRaw, Is.Zero); Assert.That(maxRaw, Is.Zero);
        var filtered = GeodesicTinHydrology.FilterVisible(f.Vertices, f.Triangles, f.Heights, .05d, out var mean, out var max);
        Assert.That(mean, Is.GreaterThan(0d)); Assert.That(max, Is.LessThan(.02d));
        Assert.That(filtered.Max() - filtered.Min(), Is.GreaterThan(.97f * (f.Heights.Max() - f.Heights.Min())));
    }
}
