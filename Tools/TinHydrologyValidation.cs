using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>Managed validation only. Seed reconstruction is explicitly separate from a
/// licensed Unity capture of the completed scene and its bathymetry/inland-sea edits.</summary>
public static class TinHydrologyValidation
{
    public static int Main(string[] args)
    {
        int passed = 0, failed = 0;
        foreach (Type fixtureType in new[] { typeof(GeodesicTinLakeRoutingTests), typeof(GeodesicTinLakeGeometryTests), typeof(GeodesicTinBasinTests), typeof(GeodesicTinHydrologyTests), typeof(GeodesicHydrologyTerrainTests), typeof(GeodesicRiverUphillDebugTests), typeof(GeodesicRiverVisibleGradeAuditTests), typeof(GeodesicTinSurfaceTests),
            typeof(GeodesicHydrologyTopologyTests), typeof(GeodesicRiverGradeTests), typeof(GeodesicDrainageTests),
            typeof(GeodesicOceanConnectivityTests), typeof(GeodesicLakeTests) })
        foreach (var method in fixtureType.GetMethods())
        {
            if (method.GetCustomAttributes(typeof(TestAttribute), false).Length == 0) continue;
            try { method.Invoke(Activator.CreateInstance(fixtureType), null); passed++; Console.WriteLine("PASS " + method.Name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL " + method.Name + ": " + (e.InnerException ?? e)); }
        }
        Console.WriteLine($"Managed hydrology tests: {passed} passed; {failed} failed. Native Unity integration and visual acceptance NOT RUN.");

        if (args.Length == 2 && args[0] == "--benchmark") Benchmark(int.Parse(args[1]));
        return failed == 0 ? 0 : 1;
    }

    private static void Benchmark(int renderSubdivision)
    {
        const float radius = 8f, sea = 8f;
        const int seed = 123456, simulationSubdivision = 6;
        var total = Stopwatch.StartNew();
        var geometry = IcosphereRenderGeometryCache.GetOrBuild(renderSubdivision);
        // This is the existing deterministic sampler with seed 123456 and Earthlike settings,
        // not a claim to reproduce every serialized setting or completed Unity scene edit.
        var surface = geometry.UnitVertices.Select(d => d * (radius + PlanetTerrainSampler.EvaluateHeight(d, seed, PlanetTerrainSettings.Earthlike))).ToArray();
        var visible = surface.Select(p => p.magnitude).ToArray();
        var oceanTopology = GeodesicHydrologyTopology.Build(geometry);
        var components = GeodesicOceanConnectivity.Build(oceanTopology, visible, sea, true, true, .001f);
        double threshold = 8d * 4d * Math.PI * radius * radius / GeodesicGridTopology.ExpectedCellCount(simulationSubdivision);
        double bump = (visible.Max() - (double)visible.Min()) * .001d;
        int fullBuilds = GeodesicGridTopology.BuildInvocationCount;
        var tin = GeodesicTinHydrology.BuildAdaptive(geometry.UnitVertices, geometry.Triangles, visible,
            components.OceanMask, sea, radius, .000002d, bump, 0d, threshold, subdivision: renderSubdivision, completedSurface: surface);
        var terrain = new GeodesicRiverTerrain(geometry, surface, tin.HydrologyHeight, true);
        int rawValid = 0, projectedValid = 0; double maxH = 0d, maxV = 0d, maxCompleted = 0d;
        foreach (var network in tin.Networks)
        {
            if (network.Centreline.Length < 2) continue;
            maxH = Math.Max(maxH, terrain.MaximumHydrologicalUphillExcursion(network.Centreline));
            maxV = Math.Max(maxV, terrain.MaximumVisibleUphillExcursion(network.Centreline));
            maxCompleted = Math.Max(maxCompleted, terrain.MaximumCompletedVisibleUphillExcursion(network.Centreline));
            if (terrain.IsCentrelineValid(network.Centreline, tin.Epsilon, tin.VisibleBumpTolerance)) rawValid++;
            var projected = network.Centreline.Select(d => (d * (terrain.Radius(d) + .0003f)).normalized).ToArray();
            if (terrain.IsCentrelineValid(projected, tin.Epsilon, tin.VisibleBumpTolerance)) projectedValid++;
        }
        Console.WriteLine($"RECONSTRUCTED managed fixture seed={seed} simulationThresholdSubdivision={simulationSubdivision} " +
            $"renderSubdivision={renderSubdivision} vertices={geometry.VertexCount} triangles={geometry.TriangleCount} " +
            $"configuredThreshold=8 areaThreshold={threshold:G9} rawValidated={rawValid} projectedValidated={projectedValid} " +
            $"maxHydrologyExcursion={maxH:G9} maxVisibleExcursion={maxV:G9} maxCompletedVisibleExcursion={maxCompleted:G9} " +
            $"extraFullTopologyBuilds={GeodesicGridTopology.BuildInvocationCount - fullBuilds} totalMs={total.Elapsed.TotalMilliseconds:F2}");
        Console.WriteLine(tin.Diagnostics.Summary());
        var basins = GeodesicTinBasins.Build(tin);
        Console.WriteLine(basins.Summary());
        foreach (var basin in basins.Basins.OrderByDescending(b => b.Area).Take(5)) Console.WriteLine(basin);
        foreach (string match in basins.TerminationDetails()) Console.WriteLine(match);
        Console.WriteLine("VISUAL ACCEPTANCE PENDING. This managed fixture is not a licensed Unity screenshot or native scene integration test.");
    }
}
