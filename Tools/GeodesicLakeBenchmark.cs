using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
// Standalone comparison of the actual pure hydrology classes; not a Unity scene benchmark.
public static class GeodesicLakeBenchmark
{
    public static string Run(int seed, int subdivision, float minDepth = .005f, bool broadDepressions = false)
    {
        var clock = Stopwatch.StartNew();
        var topology = GeodesicGridTopology.Build(subdivision);
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(subdivision);
        var vertices = new Vector3[geometry.VertexCount]; var hydro = new float[geometry.VertexCount];
        var settings = PlanetTerrainSettings.Earthlike; if (broadDepressions) { settings.continentAmplitude = settings.mountainAmplitude = 0f; settings.fineDetailAmplitude = .08f; }
        for (int i = 0; i < vertices.Length; i++)
        { var sample = PlanetTerrainSampler.Evaluate(geometry.UnitVertices[i], seed, settings); vertices[i] = geometry.UnitVertices[i] * (8f + sample.HeightOffset); hydro[i] = 8f + sample.LargeScaleHeightOffset; }
        var terrain = new GeodesicRiverTerrain(geometry, vertices, hydro);
        int n = topology.CellCount;
        var rawHeights = topology.CellDirections.Select(d => 8f + PlanetTerrainSampler.EvaluateHeight(d, seed, settings)).ToArray();
        var connectivity = GeodesicOceanConnectivity.Build(topology, rawHeights, 8f, true, true, .001f);
        var ocean = connectivity.OceanMask; var heights = new float[n]; var anchors = new Vector3[n];
        bool Ocean(Vector3 d) { int cell = 0; float best = Vector3.Dot(d, topology.CellDirections[0]); bool moved;
            do { moved = false; int prior = cell; for (int k = 0; k < topology.NeighborCounts[prior]; k++) { int next = topology.Neighbors6[prior * 6 + k]; float dot = Vector3.Dot(d, topology.CellDirections[next]); if (dot > best) { best = dot; cell = next; moved = true; } } } while (moved); return ocean[cell]; }
        for (int i = 0; i < n; i++)
        {
            anchors[i] = topology.CellDirections[i]; heights[i] = terrain.Height(anchors[i]);
            if (ocean[i]) continue;
            for (int slot = 0; slot < topology.NeighborCounts[i]; slot++)
            {
                Vector3 a = Vector3.Lerp(topology.CellDirections[i], topology.CellDirections[topology.Neighbors6[i * 6 + slot]], .2f).normalized;
                float h = terrain.Height(a);
                if (h < heights[i] && (!Ocean(a) || terrain.VisibleHeight(a) > 8f)) { heights[i] = h; anchors[i] = a; }
            }
        }
        var spills = new Dictionary<ulong,float>();
        float Spill(int a, int b)
        {
            ulong key = ((ulong)(uint)Math.Min(a,b) << 32) | (uint)Math.Max(a,b);
            if (spills.TryGetValue(key, out float h)) return h;
            h = float.NegativeInfinity;
            for (int i = 1; i < 4; i++) h = Mathf.Max(h, terrain.Height(Vector3.Lerp(anchors[a],anchors[b],i/4f).normalized));
            spills[key] = h; return h;
        }
        var graph = GeodesicDrainageGraph.Build(topology, heights, ocean, 8f, 8f, Spill);
        double setupMs = clock.Elapsed.TotalMilliseconds;
        string off = Evaluate(null, null);
        clock.Restart();
        var lakes = GeodesicLakeBasins.Build(topology, graph, 8f, true, .0001f, minDepth, Spill);
        int eligible = lakes.Basins.Count(b => b.Selected);
        var water = GeodesicLakeGeometry.Build(topology, graph, lakes, geometry, vertices, terrain, 8f, .0001f);
        graph.ApplyLakeReceivers(lakes.CreateReceivers(topology, graph, Spill));
        double lakeMs = clock.Elapsed.TotalMilliseconds;
        string on = Evaluate(lakes, water);
        return $"seed={seed} subdivision={subdivision} renderSubdivision={subdivision} cells={n} filled={graph.FilledCellCount} ocean={connectivity.Describe()}\nOFF {off}\nON {on}\ndepthThreshold={minDepth:G5} maxCandidateDepth={lakes.Basins.Select(b=>b.MaximumDepth).DefaultIfEmpty().Max():G5} maxCandidateArea={lakes.Basins.Select(b=>b.AreaFraction).DefaultIfEmpty().Max():G5} deepest={string.Join(";",lakes.Basins.OrderByDescending(b=>b.MaximumDepth).Take(8).Select(b=>$"{b.AreaFraction:G3}/{b.MaximumDepth:G3}"))} basins={lakes.Basins.Length} eligible={eligible} visibleLakes={lakes.Basins.Count(b=>b.Selected)} reasons={string.Join(",",lakes.Basins.Where(b=>!b.Selected).GroupBy(b=>b.RejectionReason).Select(g=>$"{g.Key}:{g.Count()}"))} lakeTriangles={water.Triangles.Length/3} setupMs={setupMs:F0} lakeMs={lakeMs:F0}";
        string Evaluate(GeodesicLakeBasins basins, GeodesicLakeGeometry waterGeometry)
        {
            var sw = Stopwatch.StartNew(); int candidates=0, visible=0, connected=0, inlets=0, outlets=0, mouths=0, terminations=0, chains=0;
            int[] failures = new int[5]; var plans = new GeodesicRiverReachPlan[n]; var incoming = new bool[n]; var reachesOcean = new bool[n];
            double threshold = 4d*Math.PI*64/n*8;
            for(int i=0;i<n;i++)
            {
                int next=graph.DrainageReceiver[i]; if(ocean[i]||next<0||graph.AccumulatedRunoff[i]<threshold) continue;
                candidates++; incoming[next]=true;
                var p=plans[i]=GeodesicLakeRiverRouting.Build(i,graph,anchors,terrain,basins,waterGeometry,12,7,.35f,.000002f,true,8f,Ocean);
                if(p.Failure!=GeodesicRiverReachFailure.None) failures[(int)p.Failure]++;
                if(p.LakeConnected) connected++;
                if(p.Path.Length>1) {visible++;if(p.InletBasin>=0)inlets++;if(p.OutletBasin>=0)outlets++;}
                if(ocean[next]&&p.InletBasin<0)mouths++;
            }
            for(int order=n-1;order>=0;order--)
            {
                int i=graph.UpstreamToDownstream[order], next=graph.DrainageReceiver[i];
                if(ocean[i]) {reachesOcean[i]=true;continue;}
                var p=plans[i]; if(p==null||next<0)continue;
                reachesOcean[i]=(p.Path.Length>1||p.LakeConnected)&&reachesOcean[next];
                if(reachesOcean[i]&&!incoming[i])chains++;
                if(p.Path.Length>1&&p.InletBasin<0&&!ocean[next]&&(plans[next]==null||(plans[next].Path.Length<2&&!plans[next].LakeConnected)))terminations++;
            }
            return $"candidates={candidates} visible={visible} suppressed={failures.Sum()} lakeConnected={connected} inlets={inlets} outlets={outlets} oceanMouths={mouths} inlandTerminations={terminations} oceanChains={chains} projection={failures[1]} corridor={failures[2]} unrenderedDepression={failures[3]} topologyFailure={failures[4]} refinementMs={sw.Elapsed.TotalMilliseconds:F0}";
        }
    }
}
