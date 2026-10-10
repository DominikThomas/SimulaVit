using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Capture literal historical routing and restored routing on one existing planet.
/// Does not regenerate terrain, alter the camera, save scenes, or accept the visual result.</summary>
[InitializeOnLoad]
public static class HydrologyBaselineCapture
{
    private const string ReferenceType = "HydrologyReference6f48085.GeodesicRiverSystem";
    private static readonly string Project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static readonly string Request = Path.Combine(Project, "Library/HydrologyBaselineCapture.request");
    private static readonly string Output = Path.Combine(Project, "Artifacts/HydrologyBaseline");
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool captureInProgress;

    static HydrologyBaselineCapture() => EditorApplication.delayCall += ProcessRequest;

    // One-shot request used by local validation. It never retries or starts a simulation.
    private static void ProcessRequest()
    {
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        Capture();
    }

    [MenuItem("Tools/Hydrology/Capture 6f48085 A-B (current planet, lakes OFF)")]
    public static async void Capture()
    {
        if (captureInProgress)
        {
            Debug.LogWarning("[GeodesicRiverBaselineCapture] A capture is already in progress.");
            return;
        }
        captureInProgress = true;
        Directory.CreateDirectory(Output);
        try
        {
            Type historicalType = typeof(HydrologyReference6f48085.GeodesicRiverSystem);
            if (historicalType == null)
                throw new InvalidOperationException("Run Tools/PrepareRiverBaselineReference.ps1, then let Unity finish importing the literal reference sources.");
            var planet = Object.FindObjectsByType<PlanetGenerator>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsPlanetInitialized && p.CurrentGridType == PlanetGridType.GeodesicIcosphere);
            if (planet == null || planet.GeodesicTopology == null)
                throw new InvalidOperationException("Start a Geodesic planet with seed 123456, simulation subdivision 6, render subdivision 7, and the recent sea/inland-sea settings. Position the camera, then run the capture menu.");
            if (planet.randomSeed != 123456 || planet.GeodesicTopology.SubdivisionLevel != 6 || planet.geodesicRenderSubdivisionLevel != 7)
                throw new InvalidOperationException($"Capture requires seed 123456 / simulation 6 / render 7; current values are {planet.randomSeed} / {planet.GeodesicTopology.SubdivisionLevel} / {planet.geodesicRenderSubdivisionLevel}.");
            Camera sourceCamera = Camera.main;
            if (sourceCamera == null) throw new InvalidOperationException("A current main camera is required for the same-view comparison.");
            var current = planet.GetComponent<GeodesicRiverSystem>();
            Mesh surface = planet.GetComponent<MeshFilter>()?.sharedMesh;
            if (current == null || surface == null || !current.isActiveAndEnabled || !current.showRivers || !planet.enableGeodesicRivers)
                throw new InvalidOperationException("Enable rivers on the generated planet before capturing.");
            var geometry = IcosphereRenderGeometryCache.GetOrBuild(7);
            string directory = Path.Combine(Output, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "planet-settings.json"), EditorJsonUtility.ToJson(planet, true));
            File.WriteAllText(Path.Combine(directory, "river-settings.json"), EditorJsonUtility.ToJson(current, true));

            var referenceObject = new GameObject("6f48085 comparison (temporary)");
            var cameraObject = new GameObject("Hydrology comparison camera (temporary)");
            bool requestedLakes = planet.generateHydrologicalLakes;
            var terrainBefore = surface.vertices;
            var oceanBefore = (bool[])planet.GeodesicOceanMaskData.Clone();
            try
            {
                referenceObject.layer = planet.gameObject.layer;
                referenceObject.transform.SetParent(planet.transform, false);
                var reference = (MonoBehaviour)referenceObject.AddComponent(historicalType);
                if (reference == null)
                    throw new InvalidOperationException("Unity could not attach the historical runtime component. Check that SimulaVit.HydrologyBaselineReference compiled without errors.");
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(current), reference);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(sourceCamera);
                camera.enabled = false;
                camera.transform.SetPositionAndRotation(sourceCamera.transform.position, sourceCamera.transform.rotation);
                if (sourceCamera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData))
                    EditorUtility.CopySerialized(cameraData, cameraObject.AddComponent<UniversalAdditionalCameraData>());
                // Render only this camera; an inherited camera stack must not add another view.
                if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var copiedData))
                { copiedData.renderType = CameraRenderType.Base; copiedData.cameraStack.Clear(); }
                File.WriteAllText(Path.Combine(directory, "camera-settings.json"), EditorJsonUtility.ToJson(sourceCamera, true));
                File.WriteAllText(Path.Combine(directory, "camera-transform.json"), EditorJsonUtility.ToJson(sourceCamera.transform, true));

                planet.generateHydrologicalLakes = false;
                current.enabled = false;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                historicalType.GetMethod("Initialize").Invoke(reference, new object[] { planet, geometry, surface });
                double referenceMs = watch.Elapsed.TotalMilliseconds;
                var a = Snapshot.Read(reference);
                CaptureImage(camera, Path.Combine(directory, "A-6f48085-lakes-OFF.png"), sourceCamera.pixelWidth, sourceCamera.pixelHeight);
                reference.enabled = false;
                historicalType.GetMethod("Clear").Invoke(reference, null);
                await NextEditorUpdate();

                current.enabled = true;
                watch.Restart();
                current.Initialize(planet, geometry, surface);
                double restoredMs = watch.Elapsed.TotalMilliseconds;
                var b = Snapshot.Read(current);
                if (a.Visible == 0 || b.Visible == 0 || a.Vertices.Length == 0 || b.Vertices.Length == 0)
                    throw new InvalidOperationException("An empty river render cannot validate the baseline. Inspect the generated planet and river threshold.");
                await NextEditorUpdate();
                CaptureImage(camera, Path.Combine(directory, "B-restored-lakes-OFF.png"), sourceCamera.pixelWidth, sourceCamera.pixelHeight);
                bool preservedTerrain = terrainBefore.SequenceEqual(surface.vertices);
                bool preservedOcean = oceanBefore.SequenceEqual(planet.GeodesicOceanMaskData);
                var report = new Report {
                    reference = "6f48085dea26a6f46cf215f7f026e66999208717", seed = 123456,
                    seaLevel = planet.GeodesicSeaLevelRadius, simulationSubdivision = 6, renderSubdivision = 7,
                    referenceGenerationMilliseconds = referenceMs, restoredGenerationMilliseconds = restoredMs,
                    referenceVisibleReaches = a.Visible, restoredVisibleReaches = b.Visible,
                    referenceFilledCells = a.Filled, restoredFilledCells = b.Filled,
                    pathsExactlyEqual = EqualPaths(a.Paths, b.Paths), receiversExactlyEqual = a.Receivers.SequenceEqual(b.Receivers),
                    anchorsExactlyEqual = a.Anchors.SequenceEqual(b.Anchors), meshVerticesExactlyEqual = a.Vertices.SequenceEqual(b.Vertices),
                    meshIndicesExactlyEqual = a.Indices.SequenceEqual(b.Indices), meshColorsExactlyEqual = a.Colors.SequenceEqual(b.Colors),
                    terrainUnchanged = preservedTerrain, oceanMaskUnchanged = preservedOcean,
                    suppressedDepressionReaches = current.UnrenderedSmallDepression,
                    suppressedCorridorReaches = current.SuppressedCorridorFailure,
                    projectionFailures = current.SuppressedProjectionMismatch,
                    topologyFailures = current.TrueTopologyFailure,
                    inlandVisibleTerminations = current.InlandVisibleTerminations,
                    oceanConnectedChains = current.OceanConnectedChains,
                    gradeDiagnostics = current.GradeDiagnostics?.Summary(),
                    visibleLakes = 0, largestVisibleLakeArea = 0,
                    trueLakeBasinCells = "Not validated: lake correction is gated on visual acceptance of A/B.",
                    visualAcceptance = "PENDING: inspect both Unity screenshots. Numerical equality is not visual acceptance.",
                    resultC = "Not produced: do not reintroduce lakes before accepting the river baseline."
                };
                File.WriteAllText(Path.Combine(directory, "comparison.json"), JsonUtility.ToJson(report, true));
                if (!report.pathsExactlyEqual || !report.receiversExactlyEqual || !report.anchorsExactlyEqual ||
                    !report.meshVerticesExactlyEqual || !report.meshIndicesExactlyEqual || !report.meshColorsExactlyEqual ||
                    !preservedTerrain || !preservedOcean)
                    throw new InvalidOperationException("A/B geometry or preserved terrain differs. Inspect " + directory);
                File.WriteAllText(Path.Combine(Output, "last-capture-status.txt"), "CAPTURED; visual acceptance pending: " + directory);
                Debug.Log("[GeodesicRiverBaselineCapture] Exact A/B geometry match; inspect Unity screenshots before accepting: " + directory);
            }
            finally
            {
                planet.generateHydrologicalLakes = requestedLakes;
                current.enabled = true;
                Object.DestroyImmediate(referenceObject);
                Object.DestroyImmediate(cameraObject);
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(Output, "last-capture-status.txt"), "NOT VALIDATED: " + error);
            Debug.LogException(error);
        }
        finally { captureInProgress = false; }
    }

    private static Task NextEditorUpdate()
    {
        var completion = new TaskCompletionSource<bool>();
        EditorApplication.delayCall += () => completion.TrySetResult(true);
        EditorApplication.QueuePlayerLoopUpdate();
        return completion.Task;
    }

    private static void CaptureImage(Camera camera, string path, int width, int height)
    {
        width = Math.Max(640, width); height = Math.Max(480, height);
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
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }

    private static bool EqualPaths(Dictionary<int, Vector3[]> a, Dictionary<int, Vector3[]> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var path) && pair.Value.SequenceEqual(path));

    private sealed class Snapshot
    {
        public int Visible, Filled;
        public int[] Receivers, Indices;
        public Vector3[] Anchors, Vertices;
        public Color[] Colors;
        public Dictionary<int, Vector3[]> Paths;
        public static Snapshot Read(MonoBehaviour system)
        {
            Type type = system.GetType();
            object graph = type.GetProperty("Drainage").GetValue(system);
            var mesh = (Mesh)type.GetField("riverMesh", Fields).GetValue(system);
            var paths = (IReadOnlyDictionary<int, Vector3[]>)type.GetProperty("RiverPaths").GetValue(system);
            return new Snapshot {
                Visible = (int)type.GetField("visibleReaches", Fields).GetValue(system),
                Filled = (int)graph.GetType().GetProperty("FilledCellCount").GetValue(graph),
                Receivers = (int[])((int[])graph.GetType().GetProperty("DrainageReceiver").GetValue(graph)).Clone(),
                Anchors = (Vector3[])((Vector3[])type.GetProperty("ChannelAnchors").GetValue(system)).Clone(),
                Paths = paths.ToDictionary(p => p.Key, p => (Vector3[])p.Value.Clone()),
                Vertices = mesh != null ? mesh.vertices : Array.Empty<Vector3>(),
                Indices = mesh != null ? mesh.triangles : Array.Empty<int>(),
                Colors = mesh != null ? mesh.colors : Array.Empty<Color>()
            };
        }
    }

    [Serializable]
    private sealed class Report
    {
        public string reference, visualAcceptance, resultC, trueLakeBasinCells, gradeDiagnostics;
        public int seed, simulationSubdivision, renderSubdivision, referenceVisibleReaches, restoredVisibleReaches;
        public int referenceFilledCells, restoredFilledCells, visibleLakes, suppressedDepressionReaches, suppressedCorridorReaches;
        public int projectionFailures, topologyFailures, inlandVisibleTerminations, oceanConnectedChains;
        public float seaLevel, largestVisibleLakeArea;
        public double referenceGenerationMilliseconds, restoredGenerationMilliseconds;
        public bool pathsExactlyEqual, receiversExactlyEqual, anchorsExactlyEqual, meshVerticesExactlyEqual;
        public bool meshIndicesExactlyEqual, meshColorsExactlyEqual, terrainUnchanged, oceanMaskUnchanged;
    }
}
