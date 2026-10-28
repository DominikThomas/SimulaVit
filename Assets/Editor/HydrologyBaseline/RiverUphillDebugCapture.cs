using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Same terrain and camera, toggling only the observation overlay.</summary>
public static class RiverUphillDebugCapture
{
    // Run only in a disposable project copy: no scenes or startup preferences are saved.
    public static void CaptureBatch() => CaptureBatchWith(Capture);

    public static void CaptureBatchWith(Action capture)
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/PlanetScene.unity");
            var planet = Object.FindFirstObjectByType<PlanetGenerator>();
            planet.ApplyStartupSeed(123456, false); planet.randomizeOnStart = false;
            planet.ApplyStartupGrid(PlanetGridType.GeodesicIcosphere, planet.resolution, planet.geodesicSimulationSubdivisionLevel);
            // Edit-mode scene loading does not invoke MonoBehaviour.Awake. Run the same
            // component setup as normal play before requesting generation.
            typeof(PlanetGenerator).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(planet, null);
            planet.InitializeAuthoritativePlanet("Uphill debug same-camera capture");
            var camera = Camera.main;
            camera.transform.position = planet.transform.position + new Vector3(0, .4f, -1).normalized * planet.BasePlanetRadius * 3.2f;
            camera.transform.LookAt(planet.transform.position);
            capture();
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/Hydrology/Capture Uphill Debug OFF-ON (seed 123456)")]
    public static void Capture()
    {
        var planet = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None)
            .FirstOrDefault(p => p.IsPlanetInitialized && p.CurrentGridType == PlanetGridType.GeodesicIcosphere);
        if (planet == null || planet.randomSeed != 123456 || Camera.main == null)
            throw new InvalidOperationException("Generate a Geodesic planet with seed 123456 and position the main camera first.");
        var rivers = planet.GetComponent<GeodesicRiverSystem>();
        if (rivers == null || rivers.TinDrainage == null || !rivers.showRivers || !planet.enableGeodesicRivers)
            throw new InvalidOperationException("Enable rivers before capturing.");
        string directory = Path.GetFullPath("Artifacts/RiverUphillDebug/" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        bool previous = rivers.showRejectedUphillRiverSegments;
        float timeScale = Time.timeScale;
        try
        {
            Time.timeScale = 0;
            var receiver = (int[])rivers.TinDrainage.TriangleReceiver.Clone();
            var runoff = (double[])rivers.TinDrainage.AccumulatedRunoff.Clone();
            var area = (double[])rivers.TinDrainage.DrainageArea.Clone();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            rivers.showRejectedUphillRiverSegments = false; rivers.RebuildVisuals();
            double offMs = watch.Elapsed.TotalMilliseconds;
            var meshField = typeof(GeodesicRiverSystem).GetField("riverMesh", BindingFlags.Instance | BindingFlags.NonPublic);
            var mesh = (Mesh)meshField.GetValue(rivers);
            var vertices = mesh != null ? mesh.vertices : Array.Empty<Vector3>();
            var colors = mesh != null ? mesh.colors : Array.Empty<Color>();
            var indices = mesh != null ? mesh.GetIndices(0) : Array.Empty<int>();
            var mouths = rivers.Mouths.ToArray(); var lakes = rivers.Lakes;
            Image(Camera.main, Path.Combine(directory, "A-debug-OFF.png"));
            long offMemory = GC.GetTotalMemory(true); watch.Restart();
            rivers.showRejectedUphillRiverSegments = true; rivers.RebuildVisuals();
            double onMs = watch.Elapsed.TotalMilliseconds;
            long retainedManagedDelta = GC.GetTotalMemory(true) - offMemory;
            mesh = (Mesh)meshField.GetValue(rivers);
            bool unchanged = receiver.SequenceEqual(rivers.TinDrainage.TriangleReceiver) && runoff.SequenceEqual(rivers.TinDrainage.AccumulatedRunoff)
                && area.SequenceEqual(rivers.TinDrainage.DrainageArea) && mouths.SequenceEqual(rivers.Mouths) && ReferenceEquals(lakes, rivers.Lakes)
                && vertices.SequenceEqual(mesh != null ? mesh.vertices : Array.Empty<Vector3>())
                && colors.SequenceEqual(mesh != null ? mesh.colors : Array.Empty<Color>())
                && indices.SequenceEqual(mesh != null ? mesh.GetIndices(0) : Array.Empty<int>());
            if (!unchanged) throw new InvalidOperationException("OFF/ON invariant comparison failed.");
            Image(Camera.main, Path.Combine(directory, "B-debug-ON.png"));
            File.WriteAllText(Path.Combine(directory, "report.txt"), "Seed=123456; same camera and terrain; accepted mesh, receivers, runoff, areas, mouths and lakes unchanged.\n"
                + GeodesicRiverUphillDebug.Summary(rivers.RejectedUphillDebug, rivers.UphillDebugVertexCount, 0)
                + $"\nVisual rebuild OFF={offMs:F2}ms ON={onMs:F2}ms; approximate retained managed delta={retainedManagedDelta} bytes (excludes native/GPU mesh storage).\n"
                + "\nScreenshots captured; human visual acceptance still required.\n");
            Debug.Log("[GeodesicRiverUphillDebug] Captures: " + directory);
        }
        finally { rivers.showRejectedUphillRiverSegments = previous; rivers.RebuildVisuals(); Time.timeScale = timeScale; }
    }

    public static void Image(Camera camera, string path)
    {
        var target = new RenderTexture(Math.Max(640, camera.pixelWidth), Math.Max(480, camera.pixelHeight), 24);
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        var active = RenderTexture.active; var previousTarget = camera.targetTexture;
        try
        {
            target.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            else { camera.targetTexture = target; camera.Render(); }
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = active;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }
}
