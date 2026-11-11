using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicRiverVisibleGradeAuditTests
{
    [Test]
    public void CompletedFacetRiseIsAuditedEvenWhenRoutingHeightIsDownhill()
    {
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(2);
        var surface = geometry.UnitVertices.Select(d => d * (8 + .2f * d.z)).ToArray();
        var routing = geometry.UnitVertices.Select(d => 8 - .2f * d.z).ToArray();
        var terrain = new GeodesicRiverTerrain(geometry, surface, routing, true);
        int a = geometry.Triangles[0], b = geometry.Triangles[1];
        if (surface[a].magnitude > surface[b].magnitude) { int t = a; a = b; b = t; }
        var vertices = new List<Vector3> { surface[a], surface[b] };
        var audit = new GeodesicRiverVisibleGradeAudit();
        audit.ObserveEmittedPath(0, vertices, 0, 2, true, 0, terrain, 8); audit.Resolve(8);
        Assert.That(audit.Segments.Count, Is.EqualTo(1));
        Assert.That(audit.Samples.Any(s => s.RoutingDeltaH < -audit.Epsilon && s.VisibleDeltaH > audit.Epsilon), Is.True);
        Assert.That(audit.RoutingValidButVisibleUphill, Is.EqualTo(1));
        Assert.That(audit.UphillLengthPercent, Is.GreaterThan(0));
        Assert.That(audit.Samples.All(s => s.Distance >= 0), Is.True);
        Assert.That(audit.Epsilon, Is.LessThan(.00002), "Numerical error band must not hide relief.");
    }

    [Test]
    public void AuditFindsInteriorRiseWhenBothEndpointsHaveEqualRadius()
    {
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(2);
        var surface = geometry.UnitVertices.Select(d => d * 8).ToArray();
        var terrain = new GeodesicRiverTerrain(geometry, surface);
        var audit = new GeodesicRiverVisibleGradeAudit();
        audit.ObserveEmittedPath(0, new List<Vector3> { surface[geometry.Triangles[0]], surface[geometry.Triangles[1]] },
            0, 2, true, 0, terrain, 8); audit.Resolve(8);
        Assert.That(audit.Samples.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(audit.Samples.Any(s => s.Grade == RiverVisibleGrade.Downhill), Is.True);
        Assert.That(audit.Samples.Any(s => s.Grade >= RiverVisibleGrade.SlightUphill), Is.True);
        Assert.That(audit.UphillLengthPercent, Is.InRange(45, 55));
    }

    [Test]
    public void PerSegmentColoursPreserveCyanNeighboursAndExactPrimitivePositions()
    {
        var vertices = new List<Vector3> { Vector3.right, Vector3.up, Vector3.left, Vector3.down };
        var indices = new List<int> { 0, 1, 1, 2, 2, 3 };
        var colors = Enumerable.Repeat(Color.cyan, 4).ToList();
        var originalPositions = indices.Select(i => vertices[i]).ToArray();
        var audit = new GeodesicRiverVisibleGradeAudit();
        audit.Segments.Add(new RiverRenderedGradeSegment { FirstIndex = 0, IndexCount = 2, Grade = RiverVisibleGrade.Downhill });
        audit.Segments.Add(new RiverRenderedGradeSegment { FirstIndex = 2, IndexCount = 2, Grade = RiverVisibleGrade.StrongUphill, MaximumUphillSlope = .1 });
        audit.Segments.Add(new RiverRenderedGradeSegment { FirstIndex = 4, IndexCount = 2, Grade = RiverVisibleGrade.Downhill });
        audit.ApplyColours(vertices, indices, colors);
        CollectionAssert.AreEqual(originalPositions, indices.Select(i => vertices[i]));
        CollectionAssert.AreEqual(new[] { Color.cyan, Color.cyan, Color.red, Color.red, Color.cyan, Color.cyan }, indices.Select(i => colors[i]));
    }

    [Test]
    public void NumericalEpsilonAndDistanceControlGradeNotRoutingAcceptance()
    {
        Assert.That(GeodesicRiverVisibleGradeAudit.Classify(-.01, 1, .000002), Is.EqualTo(RiverVisibleGrade.Downhill));
        Assert.That(GeodesicRiverVisibleGradeAudit.Classify(.000001, .001, .000002), Is.EqualTo(RiverVisibleGrade.Flat));
        Assert.That(GeodesicRiverVisibleGradeAudit.Classify(.0001, 1, .000002), Is.EqualTo(RiverVisibleGrade.SlightUphill));
        Assert.That(GeodesicRiverVisibleGradeAudit.Classify(.0001, .01, .000002), Is.EqualTo(RiverVisibleGrade.ModerateUphill));
        Assert.That(GeodesicRiverVisibleGradeAudit.Classify(.0001, .001, .000002), Is.EqualTo(RiverVisibleGrade.StrongUphill));
    }
}
