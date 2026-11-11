using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class RiverVisibleGradeCapture
{
    public static void CaptureBatch() => RiverUphillDebugCapture.CaptureBatchWith(Capture);

    [MenuItem("Tools/Hydrology/Capture Visible Grade A-B-C (seed 123456)")]
    public static void Capture()
    {
        var planet = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None)
            .FirstOrDefault(p => p.IsPlanetInitialized && p.CurrentGridType == PlanetGridType.GeodesicIcosphere);
        if (planet == null || planet.randomSeed != 123456 || Camera.main == null)
            throw new InvalidOperationException("Generate seed 123456 and position the main camera first.");
        var rivers = planet.GetComponent<GeodesicRiverSystem>();
        if (rivers == null || rivers.TinDrainage == null || !rivers.showRivers || !planet.enableGeodesicRivers)
            throw new InvalidOperationException("Enable the existing TIN rivers first.");
        string directory = Path.GetFullPath("Artifacts/RiverVisibleGrade/" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        bool rejected = rivers.showRejectedUphillRiverSegments, grade = rivers.colourRenderedRiversByVisibleTerrainGrade,
            markers = rivers.showRiverTerminationReasons;
        float timeScale = Time.timeScale;
        Camera captureCamera = Camera.main;
        Vector3 cameraPosition = captureCamera.transform.position;
        Quaternion cameraRotation = captureCamera.transform.rotation;
        var meshField = typeof(GeodesicRiverSystem).GetField("riverMesh", BindingFlags.Instance | BindingFlags.NonPublic);
        Mesh Mesh() => (Mesh)meshField.GetValue(rivers);
        Vector3[] Positions()
        {
            var m = Mesh(); if (m == null) return Array.Empty<Vector3>();
            var v = m.vertices; return m.GetIndices(0).Select(i => v[i]).ToArray();
        }
        try
        {
            Time.timeScale = 0;
            rivers.showRejectedUphillRiverSegments = rivers.colourRenderedRiversByVisibleTerrainGrade = rivers.showRiverTerminationReasons = false;
            rivers.RebuildVisuals();
            var positions = Positions(); var vertices = Mesh()?.vertices ?? Array.Empty<Vector3>();
            var indices = Mesh()?.GetIndices(0) ?? Array.Empty<int>(); var colors = Mesh()?.colors ?? Array.Empty<Color>();
            var receivers = rivers.TinDrainage.TriangleReceiver.ToArray(); var runoff = rivers.TinDrainage.AccumulatedRunoff.ToArray();
            var areas = rivers.TinDrainage.DrainageArea.ToArray(); var mouths = rivers.Mouths.ToArray(); var lakes = rivers.Lakes;
            var terrain = planet.GetComponent<MeshFilter>().sharedMesh.vertices;
            var paths = rivers.RiverPaths.ToDictionary(p => p.Key, p => p.Value.ToArray());
            RiverUphillDebugCapture.Image(Camera.main, Path.Combine(directory, "A-normal.png"));
            rivers.colourRenderedRiversByVisibleTerrainGrade = true; rivers.RebuildVisuals(); Verify();
            RiverUphillDebugCapture.Image(Camera.main, Path.Combine(directory, "B-visible-grade.png"));
            string summary = rivers.VisibleGradeAudit.Summary();
            rivers.showRiverTerminationReasons = true; rivers.RebuildVisuals(); Verify();
            if (summary != rivers.VisibleGradeAudit.Summary()) throw new InvalidOperationException("Marker toggle changed grade audit.");
            RiverUphillDebugCapture.Image(Camera.main, Path.Combine(directory, "C-visible-grade-terminations.png"));
            var csv = new StringBuilder("sourceTriangle,renderedSegment,startX,startY,startZ,endX,endY,endZ,routingDeltaH,visibleDeltaH,routingSlope,visibleSlope,distance,grade\n");
            foreach (var sample in rivers.VisibleGradeAudit.Samples)
                csv.AppendLine(FormattableString.Invariant($"{sample.SourceTriangle},{sample.RenderedSegment},{sample.Start.x:R},{sample.Start.y:R},{sample.Start.z:R},{sample.End.x:R},{sample.End.y:R},{sample.End.z:R},{sample.RoutingDeltaH:R},{sample.VisibleDeltaH:R},{sample.RoutingSlope:R},{sample.VisibleSlope:R},{sample.Distance:R},{sample.Grade}"));
            File.WriteAllText(Path.Combine(directory, "grade-samples.csv"), csv.ToString());
            csv = new StringBuilder("sourceTriangle,termination,x,y,z\n");
            foreach (var end in rivers.RiverTerminationPoints)
                csv.AppendLine(FormattableString.Invariant($"{end.SourceTriangle},{end.Reason},{end.Position.x:R},{end.Position.y:R},{end.Position.z:R}"));
            File.WriteAllText(Path.Combine(directory, "termination-points.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(directory, "planet-settings.json"), EditorJsonUtility.ToJson(planet, true));
            File.WriteAllText(Path.Combine(directory, "report.txt"), "Seed=123456; same camera/settings as previous batch screenshot. No routing or terrain changes.\n"
                + summary + "\n" + rivers.TinDiagnostics.Summary()
                + "\nAll A/B/C primitive positions, terrain, paths, receivers, areas, runoff, mouths and lakes unchanged.\n"
                + "Purple=closed basin; white=unexpected routing failure. Only rendered endpoints are marked.\n");
            // A/B/C retain the requested original camera. A clearly separate close-up pair
            // exposes the worst audited interval even when it is on the far hemisphere.
            var worst = rivers.VisibleGradeAudit.Samples.Where(s => s.Grade >= RiverVisibleGrade.SlightUphill)
                .OrderByDescending(s => s.VisibleSlope).FirstOrDefault();
            if (worst != null)
            {
                Vector3 direction = (worst.Start + worst.End).normalized;
                captureCamera.transform.position = planet.transform.TransformPoint(direction * (planet.MaximumSurfaceRadius + planet.BasePlanetRadius * .15f));
                captureCamera.transform.LookAt(planet.transform.TransformPoint(direction * planet.BasePlanetRadius));
                rivers.colourRenderedRiversByVisibleTerrainGrade = rivers.showRiverTerminationReasons = false;
                rivers.RebuildVisuals(); Verify();
                RiverUphillDebugCapture.Image(captureCamera, Path.Combine(directory, "D-supplemental-closeup-normal.png"));
                rivers.colourRenderedRiversByVisibleTerrainGrade = rivers.showRiverTerminationReasons = true;
                rivers.RebuildVisuals(); Verify();
                RiverUphillDebugCapture.Image(captureCamera, Path.Combine(directory, "E-supplemental-closeup-audit.png"));
                File.AppendAllText(Path.Combine(directory, "report.txt"), FormattableString.Invariant(
                    $"Supplemental D/E use a separate identical close-up camera for source={worst.SourceTriangle}, renderedSegment={worst.RenderedSegment}, visibleDelta={worst.VisibleDeltaH:R}, visibleSlope={worst.VisibleSlope:R}; A/B/C camera unchanged.\n"));
                captureCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            }
            rivers.colourRenderedRiversByVisibleTerrainGrade = rivers.showRiverTerminationReasons = false;
            rivers.RebuildVisuals(); Verify();
            if (!vertices.SequenceEqual(Mesh()?.vertices ?? Array.Empty<Vector3>()) || !indices.SequenceEqual(Mesh()?.GetIndices(0) ?? Array.Empty<int>())
                || !colors.SequenceEqual(Mesh()?.colors ?? Array.Empty<Color>())) throw new InvalidOperationException("OFF did not restore exact mesh.");
            Debug.Log("[GeodesicRiverVisibleGradeAudit] Captures and CSV audit: " + directory);

            void Verify()
            {
                if (!positions.SequenceEqual(Positions()) || !receivers.SequenceEqual(rivers.TinDrainage.TriangleReceiver)
                    || !runoff.SequenceEqual(rivers.TinDrainage.AccumulatedRunoff) || !areas.SequenceEqual(rivers.TinDrainage.DrainageArea)
                    || !mouths.SequenceEqual(rivers.Mouths) || !ReferenceEquals(lakes, rivers.Lakes)
                    || !terrain.SequenceEqual(planet.GetComponent<MeshFilter>().sharedMesh.vertices)
                    || paths.Count != rivers.RiverPaths.Count || !paths.All(p => rivers.RiverPaths.TryGetValue(p.Key, out var current) && p.Value.SequenceEqual(current)))
                    throw new InvalidOperationException("Visible grade audit modified geometry or hydrology state.");
            }
        }
        finally
        {
            captureCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            rivers.showRejectedUphillRiverSegments = rejected; rivers.colourRenderedRiversByVisibleTerrainGrade = grade;
            rivers.showRiverTerminationReasons = markers; rivers.RebuildVisuals(); Time.timeScale = timeScale;
        }
    }
}
