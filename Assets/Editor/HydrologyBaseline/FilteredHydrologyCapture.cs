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
    [MenuItem("Tools/Hydrology/Capture Filtered L7 Rivers (seed 123456)")]
    public static void Capture()
    {
        var planet = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None)
            .FirstOrDefault(p => p.IsPlanetInitialized && p.CurrentGridType == PlanetGridType.GeodesicIcosphere);
        if (planet == null || planet.randomSeed != 123456 || planet.GeodesicTopology == null ||
            planet.GeodesicTopology.SubdivisionLevel != 6 || planet.geodesicRenderSubdivisionLevel != 7)
        { Debug.LogError("Generate seed 123456 with simulation subdivision 6 and render subdivision 7 first."); return; }
        var rivers = planet.GetComponent<GeodesicRiverSystem>();
        if (rivers == null || !rivers.UsesDedicatedRenderHydrology || rivers.LakeMeshVertexCount != 0)
        { Debug.LogError("Filtered L7 rivers with visible lakes OFF are required."); return; }
        Camera camera = Camera.main;
        if (camera == null) { Debug.LogError("A main camera is required."); return; }
        string directory = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            "Artifacts", "FilteredVisibleHydrology", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "planet-settings.json"), EditorJsonUtility.ToJson(planet, true));
        File.WriteAllText(Path.Combine(directory, "river-settings.json"), EditorJsonUtility.ToJson(rivers, true));
        File.WriteAllText(Path.Combine(directory, "summary.txt"),
            $"seed=123456 simulationSubdivision=6 renderSubdivision=7 hydrologySubdivision={rivers.HydrologySubdivision} lakes=OFF\n" +
            $"configuredThreshold={rivers.riverFlowThreshold} drainageAreaThreshold={rivers.ResolvedDrainageAreaThreshold:G8}\n" +
            $"visibleRiverChains={rivers.VisibleRiverChains} oceanConnectedChains={rivers.OceanConnectedChains} " +
            $"basinTerminatedChains={rivers.BasinTerminatedChains} unexpectedBrokenChains={rivers.UnexpectedBrokenChains} " +
            $"confluences={rivers.Confluences} visibleUphillSegments={rivers.VisibleUphillSegments} " +
            $"ridgeCrossings={rivers.VisibleRidgeCrossings} totalGenerationMs={rivers.GenerationMilliseconds:F2}\n");
        if (rivers.Lakes != null)
        {
            string basins = string.Join("\n", rivers.Lakes.Basins.Where(b => b.Terminal).Select(b =>
                $"basinId={b.Id} floor={b.FloorElevation:G9} spill={b.SpillElevation:G9} " +
                $"spillFrom={b.SpillFromCell} spillPoint={b.SpillCell} area={b.Area:G9} " +
                $"depth={b.MaximumDepth:G9} catchmentArea={b.CatchmentArea:G9}"));
            File.WriteAllText(Path.Combine(directory, "true-closed-basins.txt"), basins);
        }
        int width = Math.Max(640, camera.pixelWidth), height = Math.Max(480, camera.pixelHeight);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            target.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            else { camera.targetTexture = target; camera.Render(); }
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, "D-filtered-visible-L7-lakes-OFF.png"), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
        Debug.Log("[FilteredVisibleHydrology] Screenshot and metrics saved to " + directory);
    }
}
