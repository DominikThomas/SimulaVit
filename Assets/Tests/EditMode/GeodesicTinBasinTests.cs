using System;
using System.Linq;
using NUnit.Framework;

public sealed class GeodesicTinBasinTests
{
    private static GeodesicTinHydrology Bowl(bool open = true, float scale = 1f) =>
        GeodesicTinHydrologyTests.Grid((x, y) => scale * (x * x + (y - 1f) * (y - 1f)), size: 8, boundaryOutflow: open).Tin;

    [Test] public void SimpleBowlHasExactGeometricSpillAndConservativeMembership()
    {
        var tin = Bowl(); var b = GeodesicTinBasins.Build(tin).Basins.Single();
        Assert.That(b.FloorElevation, Is.EqualTo(0)); Assert.That(b.SpillElevation, Is.EqualTo(1));
        Assert.That(tin.Height(b.Spill.Value), Is.EqualTo(1)); Assert.That(b.MaximumDepth, Is.EqualTo(1));
        Assert.That(b.MemberTriangles.Contains(b.Floor.Triangle), Is.True);
        for (int t = 0; t < tin.TriangleCount; t++)
            Assert.That(b.MemberTriangles.Contains(t), Is.EqualTo(Enumerable.Range(0, 3).Any(k => tin.Height(new TinPoint(t, k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0)) < 1)));
        Assert.That(b.Terminal, Is.False);
    }
    [Test] public void TerminalBowlHasNoInventedSpill()
    {
        var b = GeodesicTinBasins.Build(Bowl(false)).Basins.Single();
        Assert.That(b.Terminal, Is.True); Assert.That(b.Spill, Is.Null);
        Assert.That(double.IsNaN(b.SpillElevation), Is.True); Assert.That(b.DownstreamTriangle, Is.EqualTo(-1));
    }
    [Test] public void TwoMinimaMergeAtInnerSaddleBeforeOuterBoundary()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => 16f * (x * x - .25f) * (x * x - .25f) + 4f * (y - 1f) * (y - 1f), size: 16);
        var basins = GeodesicTinBasins.Build(f.Tin).Basins;
        Assert.That(basins.Count, Is.EqualTo(2));
        var child = basins.Single(b => b.ParentBasinId >= 0); var parent = basins.Single(b => b.Id == child.ParentBasinId);
        Assert.That(child.SpillElevation, Is.EqualTo(1)); Assert.That(parent.SpillElevation, Is.EqualTo(4));
        Assert.That(child.MemberTriangles.All(t => parent.MemberTriangles.Contains(t)), Is.True);
        Assert.That(f.Tin.Height(child.Spill.Value), Is.EqualTo(1));
        Assert.That(GeodesicTinBasins.Build(f.Tin).Find(child.Floor).Id, Is.EqualTo(child.Id));
    }
    [Test] public void FlatSaddleAndRepeatedBuildHaveIdenticalTopology()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => Math.Min(1f, 16f * (x * x - .25f) * (x * x - .25f)) + 4f * (y - 1f) * (y - 1f), size: 16);
        var a = GeodesicTinBasins.Build(f.Tin); var b = GeodesicTinBasins.Build(f.Tin);
        Assert.That(a.Basins.Count, Is.EqualTo(2));
        CollectionAssert.AreEqual(a.Basins.Select(x => x.ToString()), b.Basins.Select(x => x.ToString()));
        for (int i = 0; i < a.Basins.Count; i++) CollectionAssert.AreEqual(a.Basins[i].MemberTriangles, b.Basins[i].MemberTriangles);
    }
    [Test] public void TinyDepressionRemainsRawTopology()
    {
        var b = GeodesicTinBasins.Build(Bowl(scale: .0001f), minimumDepth: .005).Basins.Single();
        Assert.That(b.HasEscape, Is.True); Assert.That(b.BelowMinimumDepth, Is.True); Assert.That(b.QualifiesForVisibleLake, Is.False);
    }
    [Test] public void OceanIsExternalAndExcludedInlandWaterIsNotAutomaticallyQualified()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => x * x + (y - 1f) * (y - 1f) - .1f, size: 8, ocean: true);
        Assert.That(GeodesicTinBasins.Build(f.Tin).Basins.Count, Is.Zero);
        var inland = GeodesicTinHydrologyTests.Grid((x, y) => x * x + (y - 1f) * (y - 1f) - .1f, size: 8);
        var b = GeodesicTinBasins.Build(inland.Tin).Basins.Single();
        Assert.That(b.SubseaUnclassified, Is.True); Assert.That(b.QualifiesForVisibleLake, Is.False);
    }
    [Test] public void BuildDoesNotMutateHydrologyOrNetworksAndMatchesTerminations()
    {
        var tin = Bowl(); tin.Accumulate(); tin.ExtractNetworks(.01);
        var receivers = (int[])tin.TriangleReceiver.Clone(); var runoff = (double[])tin.AccumulatedRunoff.Clone();
        var area = (double[])tin.DrainageArea.Clone(); var h = (float[])tin.HydrologyHeight.Clone();
        var v = (float[])tin.VisibleHeight.Clone(); var networks = tin.Networks.ToArray();
        var paths = networks.Select(n => n.Samples.ToArray()).ToArray();
        var centrelines = networks.Select(n => n.Centreline.ToArray()).ToArray();
        var metadata = networks.Select(n => (n.SourceTriangle, n.JunctionNetwork, n.Termination, n.Length)).ToArray();
        var ocean = Enumerable.Range(0, tin.TriangleCount).Select(t => tin.IsOcean(tin.Centroid(t))).ToArray();
        string diagnostics = tin.Diagnostics.Summary(); var basins = GeodesicTinBasins.Build(tin);
        CollectionAssert.AreEqual(receivers, tin.TriangleReceiver); CollectionAssert.AreEqual(runoff, tin.AccumulatedRunoff);
        CollectionAssert.AreEqual(area, tin.DrainageArea); CollectionAssert.AreEqual(h, tin.HydrologyHeight); CollectionAssert.AreEqual(v, tin.VisibleHeight);
        CollectionAssert.AreEqual(networks, tin.Networks); Assert.That(tin.Diagnostics.Summary(), Is.EqualTo(diagnostics));
        CollectionAssert.AreEqual(metadata, tin.Networks.Select(n => (n.SourceTriangle, n.JunctionNetwork, n.Termination, n.Length)));
        CollectionAssert.AreEqual(ocean, Enumerable.Range(0, tin.TriangleCount).Select(t => tin.IsOcean(tin.Centroid(t))));
        for (int i = 0; i < paths.Length; i++)
        {
            CollectionAssert.AreEqual(paths[i], tin.Networks[i].Samples);
            CollectionAssert.AreEqual(centrelines[i], tin.Networks[i].Centreline);
        }
        Assert.That(networks.Count(n => n.Termination == TinTermination.TrueClosedBasin), Is.GreaterThan(0));
        foreach (var n in networks.Where(n => n.Termination == TinTermination.TrueClosedBasin)) Assert.That(basins.Find(n.Samples.Last()), Is.Not.Null);
    }

    [Test] public void OceanEscapeKeepsFreshwaterBowlSeparateAndProvidesDownstreamGeometry()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) =>
            (x <= -.5f ? 1f + 8f * (-.5f - x) : x <= 0f ? 1f + 4f * (x + .5f) : 3f - 4f * x)
            + 8f * (y - 1f) * (y - 1f), size: 16, ocean: true, boundaryOutflow: false);
        var topology = GeodesicTinBasins.Build(f.Tin); var b = topology.Basins.Single();
        Assert.That(b.FloorElevation, Is.EqualTo(1)); Assert.That(b.SpillElevation, Is.EqualTo(3));
        Assert.That(b.ParentBasinId, Is.EqualTo(-1)); Assert.That(b.DownstreamTriangle, Is.GreaterThanOrEqualTo(0));
        Assert.That(f.Heights[b.DownstreamVertex], Is.LessThan(b.SpillElevation));
        Assert.That(b.QualifiesForVisibleLake, Is.True);
        for (int t = 0; t < f.Tin.TriangleCount; t++)
            if (Enumerable.Range(0, 3).All(k => f.Tin.IsOcean(new TinPoint(t, k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0))))
                Assert.That(b.MemberTriangles.Contains(t), Is.False);
        Assert.That(topology.Find(f.At(1f, 1f)), Is.Null);
    }

    [Test] public void NestedDepressionMergesIntoTerminalParentWithoutLosingEitherBasin()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => 16f * (x * x - .25f) * (x * x - .25f) + 4f * (y - 1f) * (y - 1f), size: 16, boundaryOutflow: false);
        var basins = GeodesicTinBasins.Build(f.Tin).Basins;
        Assert.That(basins.Count, Is.EqualTo(2)); var terminal = basins.Single(b => b.Terminal);
        var child = basins.Single(b => b.HasEscape);
        Assert.That(child.ParentBasinId, Is.EqualTo(terminal.Id)); Assert.That(child.SpillElevation, Is.EqualTo(1));
        Assert.That(child.MemberTriangles.All(t => terminal.MemberTriangles.Contains(t)), Is.True);
    }

    [Test] public void BasinAuthorityIsActiveHydrologyHeightRatherThanAnIndependentVisibleModel()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => x * x + (y - 1f) * (y - 1f), size: 8);
        var slope = f.Vertices.Select(v => v.x + 2f).ToArray();
        var tin = new GeodesicTinHydrology(f.Vertices, f.Triangles, f.Heights, slope, onSphere: false, allowBoundaryOutflow: true);
        Assert.That(GeodesicTinBasins.Build(tin).Basins.Count, Is.Zero);
    }

    [Test] public void InspectionSeparatesThresholdReasonsAndUnknownTerminalLevel()
    {
        var tin = GeodesicTinHydrologyTests.Grid((x, y) => 2f + x * x + (y - 1f) * (y - 1f), size: 8).Tin;
        Assert.That(GeodesicTinBasinInspection.Exclusions(GeodesicTinBasins.Build(tin, 0, 0).Basins.Single()), Is.EqualTo("none"));
        Assert.That(GeodesicTinBasinInspection.Exclusions(GeodesicTinBasins.Build(tin, 0, 2).Basins.Single()), Is.EqualTo("depth"));
        Assert.That(GeodesicTinBasinInspection.Exclusions(GeodesicTinBasins.Build(tin, 2, 0).Basins.Single()), Is.EqualTo("area"));
        Assert.That(GeodesicTinBasinInspection.Exclusions(GeodesicTinBasins.Build(tin, 2, 2).Basins.Single()), Is.EqualTo("depth,area"));
        var terminal = GeodesicTinBasins.Build(Bowl(false));
        StringAssert.Contains("terminal/unknown-level", GeodesicTinBasinInspection.Exclusions(terminal.Basins.Single()));
        Assert.That(GeodesicTinBasinInspection.Select(terminal, Bowl(false), TinBasinDebugSelection.Deepest, 0, 10, -1), Is.Empty);
    }

    [Test] public void InspectionSelectionReportsMissingIdsAndChecksConnectedFootprint()
    {
        var tin = Bowl(); var basins = GeodesicTinBasins.Build(tin); var b = basins.Basins.Single();
        Assert.That(GeodesicTinBasinInspection.Select(basins, tin, TinBasinDebugSelection.Selected, b.Id, 1, -1).Single(), Is.SameAs(b));
        Assert.That(GeodesicTinBasinInspection.Select(basins, tin, TinBasinDebugSelection.ClosedTermination, 0, 1, -1), Is.Empty);
        StringAssert.Contains("not found", GeodesicTinBasinInspection.SelectedReport(basins, tin, -1));
        StringAssert.Contains("edgeConnected=True", GeodesicTinBasinInspection.GeometryChecks(tin, b));
        StringAssert.Contains("fullyOceanFacets=0", GeodesicTinBasinInspection.GeometryChecks(tin, b));
        StringAssert.Contains("spillVertex=", GeodesicTinBasinInspection.Describe(tin, b));
    }
}

