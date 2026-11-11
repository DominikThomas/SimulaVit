using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

// Observes the existing deterministic lake benchmark. Never publishes a counterfactual path.
public static class GeodesicRiverGradeBenchmark
{
    public static string Run(int seed, int subdivision, bool basinFixture = true)
    {
        bool scalesRecorded = false;
        return $"\nGRADE AUDIT seed={seed} subdivision={subdivision} basinFixture={basinFixture} configuredFineDetailAmplitude={(basinFixture ? .08f : .018f):G9}\n" +
            GeodesicLakeBenchmark.Run(seed, subdivision, .005f, basinFixture, Observe);

        string Observe(GeodesicGridTopology topology, GeodesicDrainageGraph graph, Vector3[] anchors,
            GeodesicRiverTerrain terrain, GeodesicLakeBasins lakes, IReadOnlyDictionary<int, GeodesicRiverReachPlan> plans, double threshold)
        {
            var report = GeodesicRiverGradeAudit.Build(graph, anchors, terrain, lakes, plans, threshold, 8f, .000002f, true);
            var text = new StringBuilder("\n").AppendLine(report.Summary());
            var failed = report.Rows.Where(r => r.Suppressed).ToArray();
            text.AppendLine($"contexts(overlapping): filledDepression={failed.Count(r=>r.FilledDepression)} spillPoint={failed.Count(r=>r.SpillPoint)} lakeOutlet={failed.Count(r=>r.LakeOutlet)} lakeRelated={failed.Count(r=>r.LakeRelated)} flatFloodplain={failed.Count(r=>r.FlatFloodplain)} ordinary={failed.Count(r=>!r.FilledDepression&&!r.SpillPoint&&!r.LakeRelated&&!r.FlatFloodplain)}");
            foreach (var group in new[] {
                ("filledDepression", failed.Where(r=>r.FilledDepression)), ("spillPoint", failed.Where(r=>r.SpillPoint)),
                ("lakeOutlet", failed.Where(r=>r.LakeOutlet)), ("flatFloodplain", failed.Where(r=>r.FlatFloodplain)),
                ("ordinary", failed.Where(r=>!r.FilledDepression&&!r.SpillPoint&&!r.LakeRelated&&!r.FlatFloodplain)) })
            {
                var rows=group.Item2.ToArray();
                text.AppendLine($"gradeContext={group.Item1} suppressed={rows.Length} ungraded={rows.Count(r=>!r.Observation.Evaluated)} corridorMaterialRise={rows.Count(r=>r.RequiredCorridorTolerance>report.EpsilonUphill)} corridorWithinDiagnosticBand={rows.Count(r=>!double.IsNaN(r.RequiredCorridorTolerance)&&r.RequiredCorridorTolerance<=report.EpsilonUphill)}");
            }
            text.AppendLine(Stats("exactDecisionAbsDelta",report.Rows.Where(r=>r.Observation.Evaluated).Select(r=>Math.Abs(r.Delta))));
            text.AppendLine(Stats("suppressedSignedDecisionDelta",failed.Where(r=>r.Observation.Evaluated).Select(r=>r.Delta)));
            text.AppendLine(Stats("minimumRequiredCorridorTolerance",failed.Where(r=>!double.IsNaN(r.RequiredCorridorTolerance)).Select(r=>r.RequiredCorridorTolerance)));
            int recovered = 0;
            foreach (var row in failed.Where(r=>!double.IsNaN(r.RequiredCorridorTolerance) && r.RequiredCorridorTolerance <= report.EpsilonUphill * 1.2))
            {
                var o = row.Observation;
                // Sensitivity experiment only; the original plan remains unchanged.
                if (GeodesicRiverPath.Refine(o.Start,o.End,o.VisibleHeight?terrain.VisibleHeight:terrain.Height,12,7,.35f,(float)report.EpsilonUphill).Length > 1) recovered++;
            }
            text.AppendLine($"readOnlySensitivityReplay recoveredAtDiagnosticBand={recovered} ofSuppressed={failed.Length}; originalPlansUnchanged=true");
            text.AppendLine("hydrologyVsVisibleAnchors columns=ClearlyDownhill,NearFlatDownhill,EffectivelyFlat,NearFlatUphill,ClearlyUphill");
            for (int row=0;row<5;row++) text.AppendLine($"{(GeodesicRiverGrade)row}: {string.Join(",",Enumerable.Range(0,5).Select(c=>report.SuppressedHydrologyVisible[row,c]))}");
            foreach(string example in report.BreakExamples(10)) text.AppendLine(example);
            if (!scalesRecorded)
            {
                scalesRecorded=true;
                var adjacent=new List<double>();var visibleAdjacent=new List<double>();var error=new List<double>();var facet=new List<double>();var detail=new List<double>();
                var visible=anchors.Select(terrain.VisibleHeight).ToArray();
                for(int cell=0;cell<graph.CellCount;cell++)
                {
                    if(graph.Ocean[cell])continue;
                    for(int slot=0;slot<topology.NeighborCounts[cell];slot++)
                    {
                        int next=topology.Neighbors6[cell*6+slot];if(next<=cell||graph.Ocean[next])continue;
                        adjacent.Add(Math.Abs(graph.HydrologicalElevation[next]-(double)graph.HydrologicalElevation[cell]));
                        visibleAdjacent.Add(Math.Abs(visible[next]-(double)visible[cell]));
                    }
                    Vector3 d=Vector3.Lerp(anchors[cell],anchors[topology.Neighbors6[cell*6]],.37f).normalized;
                    error.Add(Math.Abs(terrain.Height(d)-terrain.InterpolationReference(d,false)));
                    error.Add(Math.Abs(terrain.VisibleHeight(d)-terrain.InterpolationReference(d,true)));
                    facet.Add(Math.Abs(terrain.VisibleHeight(d)-(double)terrain.Radius(d)));
                    detail.Add(Math.Abs(terrain.VisibleHeight(d)-(double)terrain.Height(d)));
                }
                text.AppendLine(Stats("adjacentHydroAbs",adjacent)).AppendLine(Stats("adjacentVisibleAbs",visibleAdjacent))
                    .AppendLine(Stats("interpolationRoundoffAbs",error)).AppendLine(Stats("visibleVsFacetAbs",facet)).AppendLine(Stats("visibleVsHydroAbs",detail));
            }
            return text.ToString();
        }
    }

    private static string Stats(string label, IEnumerable<double> values)
    {
        var array=values.OrderBy(v=>v).ToArray();
        double P(double p)=>array.Length==0?double.NaN:array[(int)Math.Round((array.Length-1)*p)];
        return FormattableString.Invariant($"{label}: n={array.Length} min={P(0):G9} median={P(.5):G9} mean={(array.Length==0?double.NaN:array.Average()):G9} p90={P(.9):G9} p95={P(.95):G9} p99={P(.99):G9} max={P(1):G9}");
    }
}
