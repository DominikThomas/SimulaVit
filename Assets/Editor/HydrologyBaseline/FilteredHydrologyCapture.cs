using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Captures the actual current Unity view for visual acceptance of filtered L7 rivers.</summary>
public static class FilteredHydrologyCapture
{
    [MenuItem("Tools/Hydrology/Capture Raw TIN Rivers (seed 123456)")]
    public static void Capture()
        => CaptureTin(true);

    [MenuItem("Tools/Hydrology/Capture TIN Ribbons After Raw Acceptance (seed 123456)")]
    public static void CaptureRibbons()
        => CaptureTin(false);

    private static void CaptureTin(bool raw)
    {
        var planet = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None)
            .FirstOrDefault(p => p.IsPlanetInitialized && p.CurrentGridType == PlanetGridType.GeodesicIcosphere);
        if (planet == null || planet.randomSeed != 123456 || planet.GeodesicTopology == null ||
            planet.GeodesicTopology.SubdivisionLevel != 6 || planet.geodesicRenderSubdivisionLevel != 7)
        { Debug.LogError("Generate seed 123456 with simulation subdivision 6 and render subdivision 7 first."); return; }
        var rivers = planet.GetComponent<GeodesicRiverSystem>();
        if (rivers == null || rivers.TinDrainage == null || rivers.LakeMeshVertexCount != 0)
        { Debug.LogError("Continuous TIN rivers with visible lakes OFF are required."); return; }
        if (rivers.drawRawTrianglePaths != raw)
        { Debug.LogError(raw ? "Enable Draw Raw Triangle Paths and refresh river visuals first." :
            "Inspect and accept the raw capture, then disable Draw Raw Triangle Paths and refresh river visuals first."); return; }
        Camera camera = Camera.main;
        if (camera == null) { Debug.LogError("A main camera is required."); return; }
        string directory = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            "Artifacts", "TinHydrology", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "planet-settings.json"), EditorJsonUtility.ToJson(planet, true));
        File.WriteAllText(Path.Combine(directory, "river-settings.json"), EditorJsonUtility.ToJson(rivers, true));
        File.WriteAllText(Path.Combine(directory, "summary.txt"),
            $"seed=123456 simulationSubdivision=6 renderSubdivision=7 hydrologySubdivision={rivers.HydrologySubdivision} lakes=OFF\n" +
            $"configuredThreshold={rivers.riverFlowThreshold} drainageAreaThreshold={rivers.ResolvedDrainageAreaThreshold:G8}\n" +
            $"visibleRiverChains={rivers.VisibleRiverChains} oceanConnectedChains={rivers.OceanConnectedChains} " +
            $"basinTerminatedChains={rivers.BasinTerminatedChains} unexpectedBrokenChains={rivers.UnexpectedBrokenChains} " +
            $"confluences={rivers.Confluences} visibleUphillSegments={rivers.VisibleUphillSegments} " +
            $"ridgeCrossings={rivers.VisibleRidgeCrossings} totalGenerationMs={rivers.GenerationMilliseconds:F2}\n" +
            rivers.TinDiagnostics.Summary() + "\nlicensedUnityCapture=true rawTrianglePaths=" + raw + "\n");
        string basins = string.Join("\n", rivers.TinDrainage.Networks.Where(n => n.Termination == TinTermination.TrueClosedBasin ||
            n.Termination == TinTermination.TinyNoiseDepression || n.Termination == TinTermination.FlatRequiringRouting ||
            n.Termination == TinTermination.RoutingFailure).Select(n =>
                $"sourceTriangle={n.SourceTriangle} termination={n.Termination} lastTriangle={n.Samples.Last().Triangle} " +
                $"lastHeight={rivers.TinDrainage.Height(n.Samples.Last()):G9} length={n.Length:G9}"));
        File.WriteAllText(Path.Combine(directory, "classified-terminations.txt"), basins);
        File.WriteAllText(Path.Combine(directory, "unrouted-active-triangles.txt"),
            string.Join("\n", rivers.TinDrainage.RoutingFailureTriangles.Select(t =>
                $"triangle={t} classification=RoutingFailure reason=active-flow-component-without-valid-visible-source")));
        int width = Math.Max(640, camera.pixelWidth), height = Math.Max(480, camera.pixelHeight);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var previousTarget = camera.targetTexture;
        try
        {
            target.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            else { camera.targetTexture = target; camera.Render(); }
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, raw ? "B-raw-TIN-gradient-lakes-OFF.png" : "C-TIN-ribbons-lakes-OFF.png"), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previous;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
        Debug.Log("[GeodesicTIN] Licensed Unity screenshot and metrics saved to " + directory + "; visual acceptance requires inspection.");
    }
}
