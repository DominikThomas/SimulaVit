using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicTinSurfaceTests
{
    [Test]
    public void SphereSharesRenderGeometryAndAreasWithoutBuildingAnotherSimulationGrid()
    {
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(3);
        int builds = GeodesicGridTopology.BuildInvocationCount;
        var h = geometry.UnitVertices.Select(d => 8f + .1f * d.z).ToArray();
        var tin = new GeodesicTinHydrology(geometry.UnitVertices, geometry.Triangles, h, h, radius: 8d);
        Assert.That(tin.TriangleCount, Is.EqualTo(geometry.TriangleCount));
        Assert.That(tin.LocalArea.Sum(), Is.EqualTo(4d * Math.PI * 64d).Within(.0001d));
        Assert.That(GeodesicGridTopology.BuildInvocationCount, Is.EqualTo(builds));
        for (int t = 0; t < tin.TriangleCount; t++)
            for (int edge = 0; edge < 3; edge++)
                Assert.That(tin.Neighbors.Skip(tin.Neighbors[t * 3 + edge] * 3).Take(3).Contains(t), Is.True);
    }

    [Test]
    public void SphereCoastUsesExactCompletedFacetAndProjectedPathRemainsDownhill()
    {
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(5);
        var surface = geometry.UnitVertices.Select(d => d * (8f + .1f * d.z)).ToArray();
        var h = surface.Select(p => p.magnitude).ToArray(); const float sea = 7.96f;
        var ocean = h.Select(v => v < sea).ToArray();
        var tin = new GeodesicTinHydrology(geometry.UnitVertices, geometry.Triangles, h, h, ocean,
            sea, 8d, .000002d, .002d, completedSurface: surface);
        var terrain = new GeodesicRiverTerrain(geometry, surface, h, true);
        for (int t = 0; t < geometry.TriangleCount; t += 997)
        {
            var source = tin.Centroid(t); if (tin.IsOcean(source)) continue;
            var path = tin.Trace(source, out var termination);
            Assert.That(termination, Is.EqualTo(TinTermination.OceanCoast));
            Assert.That(tin.Validate(path), Is.True);
            Assert.That(tin.CompletedVisibleRadius(path.Last()), Is.EqualTo(sea).Within(.0000001d));
            Vector3[] directions = path.Select(tin.Direction).ToArray();
            Assert.That(terrain.IsCentrelineValid(directions, tin.Epsilon, tin.VisibleBumpTolerance), Is.True);
            Vector3[] projected = directions.Select(d => (d * (terrain.Radius(d) + .0003f)).normalized).ToArray();
            Assert.That(terrain.IsCentrelineValid(projected, tin.Epsilon, tin.VisibleBumpTolerance), Is.True);
        }
    }

    [Test]
    public void AdaptiveFilterReducesFilteringInsteadOfIncreasingUphillTolerance()
    {
        var f = GeodesicTinHydrologyTests.Grid((x, y) => 2f - y + .3f * (float)Math.Cos(x * 20f + y * 13f), size: 16);
        const double epsilon = .000002d;
        var tin = GeodesicTinHydrology.BuildAdaptive(f.Vertices, f.Triangles, f.Heights, null, 0f, 1d,
            epsilon, .000001d, .1d, .02d, onSphere: false, boundaryOutflow: true);
        Assert.That(tin.Diagnostics.FilterRetries, Is.GreaterThan(0));
        Assert.That(tin.Diagnostics.FilterStrength, Is.LessThan(.1d));
        Assert.That(tin.Epsilon, Is.EqualTo(epsilon));
        Assert.That(tin.Networks.All(n => n.Samples.Length < 2 || tin.Validate(n.Samples)), Is.True);
        Assert.That(tin.Diagnostics.HydrologyUphillSegments, Is.Zero);
        Assert.That(tin.Diagnostics.VisibleMaterialUphillSegments, Is.Zero);
    }
}
