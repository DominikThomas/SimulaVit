using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class LowFrequencyHydrologyCapture
{
    // Batch validation uses a disposable project copy; never saves scene or preferences.
    public static void CaptureBatch() => CaptureBatch(HydrologyTerrainMode.ReducedRidgeAndFineDetail);
    public static void CaptureFineDetailBatch() => CaptureBatch(HydrologyTerrainMode.FineDetailRemoved);

    private static void CaptureBatch(HydrologyTerrainMode experiment)
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/PlanetScene.unity");
            var planet = Object.FindFirstObjectByType<PlanetGenerator>();
            planet.ApplyStartupSeed(123456, false); planet.randomizeOnStart = false;
            planet.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, planet.resolution, 6);
            planet.geodesicRenderSubdivisionLevel = 7; planet.enableGeodesicRivers = true;
            planet.useLowFrequencyTerrainForHydrology = false;
            typeof(PlanetGenerator).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(planet, null);
            planet.InitializeAuthoritativePlanet("Low-frequency hydrology A/B");
            Camera.main.transform.position = planet.transform.position + new Vector3(0, .4f, -1).normalized * planet.BasePlanetRadius * 3.2f;
            Camera.main.transform.LookAt(planet.transform.position);
            CaptureComparison(experiment); EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/Hydrology/Capture Low-Frequency Terrain A-B (seed 123456)")]
    public static void Capture() => CaptureComparison(HydrologyTerrainMode.ReducedRidgeAndFineDetail);

    [MenuItem("Tools/Hydrology/Capture Fine Detail Only A-B (seed 123456)")]
    public static void CaptureFineDetail() => CaptureComparison(HydrologyTerrainMode.FineDetailRemoved);

    private static void CaptureComparison(HydrologyTerrainMode experiment)
    {
        var planet = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None)
            .FirstOrDefault(p => p.IsPlanetInitialized && p.CurrentGridType == PlanetGridType.GeodesicIcosphere);
        if (planet == null || planet.randomSeed != 123456 || planet.GeodesicTopology.SubdivisionLevel != 6
            || planet.geodesicRenderSubdivisionLevel != 7 || Camera.main == null)
            throw new InvalidOperationException("Generate seed 123456, simulation subdivision 6, render subdivision 7, and position the main camera first.");
        var rivers = planet.GetComponent<GeodesicRiverSystem>();
        if (rivers == null || rivers.TinDrainage == null || !rivers.showRivers || !planet.enableGeodesicRivers)
            throw new InvalidOperationException("Enable TIN rivers before capture.");
        string directory = Path.GetFullPath("Artifacts/" + (experiment == HydrologyTerrainMode.FineDetailRemoved ? "FineDetailHydrology/" : "LowFrequencyHydrology/") + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        var previous = planet.hydrologyTerrainMode;
        bool grade = rivers.colourRenderedRiversByVisibleTerrainGrade,
            markers = rivers.showRiverTerminationReasons, rejected = rivers.showRejectedUphillRiverSegments;
        var camera = Camera.main; Vector3 cameraPosition = camera.transform.position; Quaternion cameraRotation = camera.transform.rotation;
        float timeScale = Time.timeScale, sea = planet.GeodesicSeaLevelRadius;
        var mesh = planet.GetComponent<MeshFilter>().sharedMesh; var vertices = mesh.vertices; var indices = mesh.triangles;
        var mask = Enumerable.Range(0, planet.GeodesicTopology.CellCount).Select(planet.IsGeodesicCellOcean).ToArray();
        var report = new StringBuilder("# Seed 123456: hydrology input A/B\n\nSimulation 6, render 7. Identical visible mesh, retained ocean authority, sea level, camera and river settings. Lakes remain paused.\n\n");
        File.WriteAllText(Path.Combine(directory, "planet-settings.json"), EditorJsonUtility.ToJson(planet, true));
        File.WriteAllText(Path.Combine(directory, "river-settings.json"), EditorJsonUtility.ToJson(rivers, true));
        try
        {
            Time.timeScale = 0f; rivers.showRejectedUphillRiverSegments = false;
            var results = new List<Dictionary<string, double>>();
            int[] baselineReceivers = null; double[] baselineRunoff = null; bool[] baselineOcean = null;
            Vector3 closeupDirection = Vector3.up;
            foreach (bool enabled in new[] { false, true })
            {
                planet.hydrologyTerrainMode = enabled ? experiment : HydrologyTerrainMode.Visible;
                rivers.colourRenderedRiversByVisibleTerrainGrade = rivers.showRiverTerminationReasons = false;
                rivers.RebuildForCurrentTerrain(); Verify();
                string name = enabled ? "B-" + experiment : "A-Visible";
                if (!enabled)
                {
                    baselineReceivers = rivers.TinDrainage.TriangleReceiver.ToArray(); baselineRunoff = rivers.TinDrainage.AccumulatedRunoff.ToArray();
                    var longest = rivers.TinDrainage.Networks.OrderByDescending(n => n.Length).FirstOrDefault();
                    if (longest != null) closeupDirection = longest.Centreline[longest.Centreline.Length / 2].normalized;
                    report.AppendLine("Matching close-up centered on longest baseline candidate, direction=" + closeupDirection.ToString("R") + ".\n");
                    baselineOcean = Enumerable.Range(0, rivers.TinDrainage.TriangleCount).Select(t => rivers.TinDrainage.IsOcean(rivers.TinDrainage.Centroid(t))).ToArray();
                }
                else if (!baselineOcean.SequenceEqual(Enumerable.Range(0, rivers.TinDrainage.TriangleCount).Select(t => rivers.TinDrainage.IsOcean(rivers.TinDrainage.Centroid(t)))))
                    throw new InvalidOperationException("TIN ocean membership changed.");
                for (int view = 0; view < 4; view++)
                {
                    camera.transform.position = planet.transform.position + Quaternion.AngleAxis(view * 90f, Vector3.up) * (cameraPosition - planet.transform.position);
                    camera.transform.LookAt(planet.transform.position);
                    RiverUphillDebugCapture.Image(camera, Path.Combine(directory, name + "-view" + view + ".png"));
                }
                camera.transform.position = planet.transform.TransformPoint(closeupDirection * (planet.MaximumSurfaceRadius + planet.BasePlanetRadius * .15f));
                camera.transform.LookAt(planet.transform.TransformPoint(closeupDirection * planet.BasePlanetRadius));
                RiverUphillDebugCapture.Image(camera, Path.Combine(directory, name + "-closeup.png"));
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                rivers.colourRenderedRiversByVisibleTerrainGrade = rivers.showRiverTerminationReasons = true;
                rivers.RebuildVisuals(); Verify();
                RiverUphillDebugCapture.Image(camera, Path.Combine(directory, name + "-grade.png"));
                var d = rivers.TinDiagnostics;
                var visibleInput = rivers.TinDrainage.VisibleHeight;
                var hydrologyInput = rivers.HydrologyTerrainInputHeights;
                double absolute = 0, squared = 0;
                for (int i = 0; i < visibleInput.Length; i++)
                { double delta = hydrologyInput[i] - (double)visibleInput[i]; absolute += Math.Abs(delta); squared += delta * delta; }
                var geometry = IcosphereRenderGeometryCache.GetOrBuild(planet.geodesicRenderSubdivisionLevel);
                double visibleRoughness = GeodesicHydrologyTerrain.Roughness(visibleInput, geometry.Triangles);
                double hydrologyRoughness = GeodesicHydrologyTerrain.Roughness(hydrologyInput, geometry.Triangles);
                results.Add(new Dictionary<string, double> {
                    { "Mean absolute input radius difference", absolute / visibleInput.Length },
                    { "RMS input radius difference", Math.Sqrt(squared / visibleInput.Length) },
                    { "Input neighbour roughness", hydrologyRoughness },
                    { "Neighbour roughness reduction percent", visibleRoughness > 0 ? 100 * (1 - hydrologyRoughness / visibleRoughness) : 0 },
                    { "Depression/fill count (no filling in TIN)", 0 }, { "Tiny/noise depression terminations", d.NoiseDepressionTerminations },
                    { "Routing failure triangles", rivers.TinDrainage.RoutingFailureTriangles.Count },
                    { "Candidate networks before ribbon validation", rivers.TinDrainage.Networks.Count },
                    { "Visible river reaches", rivers.VisibleRiverReaches }, { "Visible river chains", rivers.RiverPaths.Count },
                    { "Ocean-connected rendered mouths", rivers.Mouths.Count }, { "Ocean terminations (TIN)", d.OceanTerminations }, { "Broken chains", d.UnexpectedBrokenNetworks },
                    { "TIN visible material uphill segments", d.VisibleMaterialUphillSegments }, { "TIN ridge crossings", d.VisibleRidgeCrossings },
                    { "Audited visible uphill segments", rivers.VisibleGradeAudit.Segments.Count(s => s.Grade >= RiverVisibleGrade.SlightUphill) },
                    { "Audited uphill length percent", rivers.VisibleGradeAudit.UphillLengthPercent },
                    { "Closed-basin terminations", d.ClosedBasinTerminations }, { "Flat terminations", d.FlatTerminations },
                    { "Rejected visible steps", d.RejectedVisibleSteps }, { "Confluences", d.Confluences },
                    { "Mean network length", d.MeanNetworkLength }, { "Maximum network length", d.MaxNetworkLength }
                });
                report.AppendLine("## " + name + "\n\n```\n" + rivers.HydrologyTerrainSummary + "\n" + d.Summary() + "\n" + rivers.VisibleGradeAudit.Summary() + "\n```\n");
                File.WriteAllText(Path.Combine(directory, name + "-terminations.txt"), string.Join("\n", rivers.RiverTerminationPoints.Select(t => t.SourceTriangle + " " + t.Reason + " " + t.Position)));
            }
            report.AppendLine("| Metric | Visible | Experimental | Change |\n| --- | ---: | ---: | ---: |");
            foreach (var pair in results[0])
            {
                double b = results[1][pair.Key];
                string change = pair.Value == 0 ? (b == 0 ? "0%" : "n/a (baseline zero)") : ((b / pair.Value - 1) * 100).ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";
                report.AppendLine(FormattableString.Invariant($"| {pair.Key} | {pair.Value:G9} | {b:G9} | {change} |"));
            }
            planet.useLowFrequencyTerrainForHydrology = false; rivers.RebuildForCurrentTerrain(); Verify();
            if (!baselineReceivers.SequenceEqual(rivers.TinDrainage.TriangleReceiver) || !baselineRunoff.SequenceEqual(rivers.TinDrainage.AccumulatedRunoff))
                throw new InvalidOperationException("OFF did not restore baseline routing.");
            report.AppendLine("\nPASS: visible vertices/triangles, simulation ocean mask, TIN ocean membership and sea level unchanged; OFF restores exact receivers/runoff. Human visual acceptance still required.");
            File.WriteAllText(Path.Combine(directory, "comparison.md"), report.ToString());
            Debug.Log("[GeodesicHydrologyTerrain] A/B comparison: " + directory);
        }
        finally
        {
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            planet.hydrologyTerrainMode = previous; rivers.colourRenderedRiversByVisibleTerrainGrade = grade;
            rivers.showRiverTerminationReasons = markers; rivers.showRejectedUphillRiverSegments = rejected;
            try { rivers.RebuildForCurrentTerrain(); } finally { Time.timeScale = timeScale; }
        }

        void Verify()
        {
            if (!vertices.SequenceEqual(mesh.vertices) || !indices.SequenceEqual(mesh.triangles) || sea != planet.GeodesicSeaLevelRadius
                || !mask.SequenceEqual(Enumerable.Range(0, planet.GeodesicTopology.CellCount).Select(planet.IsGeodesicCellOcean))
                || rivers.Lakes != null || !rivers.HydrologicalLakesPausedForBaselineValidation)
                throw new InvalidOperationException("Visible terrain, ocean, sea level or paused lakes changed.");
        }
    }
}
