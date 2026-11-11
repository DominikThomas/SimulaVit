using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicRiverGradeTests
{
    private static readonly Vector3 Start = Vector3.right;
    private static readonly Vector3 End = new Vector3(1f,.2f,0f).normalized;
    private static float T(Vector3 d) => Mathf.Clamp01(d.y / End.y);

    [Test]
    public void ExactlyFlatSamplesPassEvenWithZeroTolerance()
    {
        Assert.That(GeodesicRiverPath.IsDownhill(Start,End,d=>8f,0f),Is.True);
        Assert.That(GeodesicRiverPath.Refine(Start,End,d=>8f,12,7,.35f,0f).Length,Is.EqualTo(13));
        Assert.That(GeodesicRiverGradeAudit.MinimumCorridorTolerance(Start,End,d=>8f,12,7,.35f),Is.Zero);
    }

    [Test]
    public void DownhillTerracesAndTwoMicroUnitRisesAreAlreadyAccepted()
    {
        float Height(Vector3 d) => T(d)<.25f?1.2f:T(d)<.5f?1.15f:T(d)<.75f?1.150002f:1.1f;
        Assert.That(GeodesicRiverPath.Refine(Start,End,Height,12,7,0f,.000002f).Length,Is.EqualTo(13));
        Assert.That(GeodesicRiverGradeAudit.MinimumCorridorTolerance(Start,End,Height,12,7,0f),Is.EqualTo(.000002f).Within(.00000012f));
    }

    [Test]
    public void EqualEndpointsDoNotMakeAnInteriorRidgeFlat()
    {
        float Height(Vector3 d) => 8f + .01f * Mathf.Sin(T(d)*Mathf.PI);
        Assert.That(Height(Start),Is.EqualTo(Height(End)).Within(.000002f));
        Assert.That(GeodesicRiverPath.Refine(Start,End,Height,12,7,0f,.000002f),Is.Empty);
        Assert.That(GeodesicRiverGradeAudit.MinimumCorridorTolerance(Start,End,Height,12,7,0f),Is.GreaterThan(.009f));
    }

    [Test]
    public void DiagnosticCategoriesHaveExplicitInclusiveFlatAndNearBoundaries()
    {
        double near=GeodesicRiverGradeAudit.NearFlatBand(8f,.000002f),flat=.000002;
        Assert.That(near,Is.EqualTo(.00000762939453125));
        Assert.That(GeodesicRiverGradeAudit.Classify(-near-1e-9,flat,near),Is.EqualTo(GeodesicRiverGrade.ClearlyDownhill));
        Assert.That(GeodesicRiverGradeAudit.Classify(-near,flat,near),Is.EqualTo(GeodesicRiverGrade.NearFlatDownhill));
        Assert.That(GeodesicRiverGradeAudit.Classify(-flat,flat,near),Is.EqualTo(GeodesicRiverGrade.EffectivelyFlat));
        Assert.That(GeodesicRiverGradeAudit.Classify(flat,flat,near),Is.EqualTo(GeodesicRiverGrade.EffectivelyFlat));
        Assert.That(GeodesicRiverGradeAudit.Classify(near,flat,near),Is.EqualTo(GeodesicRiverGrade.NearFlatUphill));
        Assert.That(GeodesicRiverGradeAudit.Classify(near+1e-9,flat,near),Is.EqualTo(GeodesicRiverGrade.ClearlyUphill));
        Assert.That(GeodesicRiverGradeAudit.Classify(double.NaN,flat,near),Is.EqualTo(GeodesicRiverGrade.NotEvaluated));
    }

    [Test]
    public void MinimaxConsidersAlternativeLanesRatherThanOnlyTheCenterline()
    {
        float Height(Vector3 d) => 8f + (T(d)>.3f && T(d)<.7f && Math.Abs(d.z)<.006f ? .01f : 0f);
        Assert.That(GeodesicRiverGradeAudit.MinimumCorridorTolerance(Start,End,Height,12,7,0f),Is.GreaterThan(.009f));
        Assert.That(GeodesicRiverGradeAudit.MinimumCorridorTolerance(Start,End,Height,12,7,.4f),Is.Zero);
        Assert.That(GeodesicRiverPath.Refine(Start,End,Height,12,7,.4f,.000002f).Length,Is.GreaterThan(1));
    }

    [Test]
    public void AuditIsReadOnlyAndChecksAreaAndRunoffIndependently()
    {
        var t=GeodesicGridTopology.Build(1);var geometry=IcosphereRenderGeometryCache.GetOrBuild(2);
        var ocean=new bool[t.CellCount];ocean[0]=true;
        var graph=GeodesicDrainageGraph.Build(t,Enumerable.Repeat(8f,t.CellCount).ToArray(),ocean,8f,8f);
        var terrain=new GeodesicRiverTerrain(geometry,geometry.UnitVertices.Select(d=>d*8f).ToArray());
        var plans=new Dictionary<int,GeodesicRiverReachPlan>();
        for(int c=1;c<t.CellCount;c++) plans[c]=GeodesicLakeRiverRouting.Build(c,graph,t.CellDirections,terrain,null,null,12,7,.35f,.000002f,false,8f,null);
        int[] receivers=(int[])graph.DrainageReceiver.Clone();double[] runoff=(double[])graph.AccumulatedRunoff.Clone();
        var paths=plans.ToDictionary(p=>p.Key,p=>p.Value.Path);
        var report=GeodesicRiverGradeAudit.Build(graph,t.CellDirections,terrain,null,plans,.001,8f,.000002f,true);
        Assert.That(report.RunoffViolations,Is.Zero);Assert.That(report.AreaViolations,Is.Zero);
        CollectionAssert.AreEqual(receivers,graph.DrainageReceiver);CollectionAssert.AreEqual(runoff,graph.AccumulatedRunoff);
        foreach(var pair in plans) Assert.That(pair.Value.Path,Is.SameAs(paths[pair.Key]));
        int source=1,next=receivers[source];graph.AccumulatedRunoff[source]=graph.AccumulatedRunoff[next]+1d;
        report=GeodesicRiverGradeAudit.Build(graph,t.CellDirections,terrain,null,plans,.001,8f,.000002f);
        Assert.That(report.RunoffViolations,Is.GreaterThan(0));Assert.That(report.AreaViolations,Is.Zero);
        graph.DrainageArea[source]=graph.DrainageArea[next]+1d;
        Assert.That(GeodesicRiverGradeAudit.Build(graph,t.CellDirections,terrain,null,plans,.001,8f,.000002f).AreaViolations,Is.GreaterThan(0));
    }
}
