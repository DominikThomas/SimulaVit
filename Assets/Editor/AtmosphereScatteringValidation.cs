using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>GPU smoke test and repeatable isolated comparison; never runs the simulation or saves scenes.</summary>
public static class AtmosphereScatteringValidation
{
    [MenuItem("Tools/Atmosphere/Validate and Capture")]
    public static void Capture()
    {
        string output = Path.GetFullPath("Artifacts/AtmosphereScattering");
        Directory.CreateDirectory(output);
        var scene = EditorSceneManager.NewPreviewScene();
        Light originalSun = RenderSettings.sun;
        float originalAmbientIntensity = RenderSettings.ambientIntensity;
        RenderPipelineAsset originalPipeline = QualitySettings.renderPipeline;
        var report = new StringBuilder();
        var owned = new System.Collections.Generic.List<Object>();
        try
        {
            var material = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Atmosphere_Fresnel_Mat.mat"));
            owned.Add(material);
            if (material == null || material.shader == null || !material.shader.isSupported)
                throw new InvalidOperationException("Atmosphere shader missing or unsupported.");
            material.SetFloat("_PlanetRadius", 1f);
            material.SetFloat("_AtmosphereRadius", 1.04f);
            var geometry = IcosphereRenderGeometryCache.GetOrBuild(5);
            var groundMesh = IcosphereRenderMeshBuilder.BuildSurfaceMesh(geometry, 1f, "Validation ground");
            var colours = new Color[groundMesh.vertexCount];
            Vector3[] vertices = groundMesh.vertices;
            for (int i = 0; i < colours.Length; ++i)
            {
                Vector3 n = vertices[i].normalized;
                float land = Mathf.Sin(n.x * 10f + n.y * 3f) * Mathf.Cos(n.z * 9f - n.y * 2f);
                colours[i] = land > 0.25f ? new Color(0.20f, 0.30f, 0.12f) : new Color(0.025f, 0.10f, 0.19f);
            }
            groundMesh.colors = colours;
            owned.Add(groundMesh);
            var shellMesh = IcosphereRenderMeshBuilder.BuildSurfaceMesh(geometry, 1.04f, "Validation atmosphere");
            owned.Add(shellMesh);
            var groundMaterial = new Material(Shader.Find("SimulaVit/GeodesicVertexColorURP"));
            owned.Add(groundMaterial);
            var ground = new GameObject("Validation planet");
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            var shell = new GameObject("Validation atmosphere");
            SceneManager.MoveGameObjectToScene(shell, scene);
            shell.AddComponent<MeshFilter>().sharedMesh = shellMesh;
            var shellRenderer = shell.AddComponent<MeshRenderer>();
            shellRenderer.sharedMaterial = material;
            shellRenderer.shadowCastingMode = ShadowCastingMode.Off;
            var shellProperties = new MaterialPropertyBlock();
            shellProperties.SetFloat("_PlanetRadius", 1f);
            shellProperties.SetFloat("_AtmosphereRadius", 1.04f);
            shellRenderer.SetPropertyBlock(shellProperties);
            var camera = new GameObject("Validation camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.001f, 0.002f, 0.005f);
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.001f;
            camera.farClipPlane = 100f;
            camera.enabled = false;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.requiresColorTexture = false;
            cameraData.requiresDepthTexture = true;
            var sun = new GameObject("Validation Main Light").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
            sun.type = LightType.Directional;
            sun.intensity = 1;
            sun.color = Color.white;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;
            RenderSettings.ambientIntensity = 0;
            var legacy = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Materials/Atmosphere_Fresnel.shadergraph"));
            legacy.SetFloat("_Intensity", 0.4f);
            legacy.SetFloat("_FresnelPower", 4f);
            legacy.SetFloat("_AlphaStrength", 0);
            legacy.SetColor("_AtmosphereColor", new Color(0.05513528f, 0.1981132f, 0.18917705f, 0.6666667f));
            owned.Add(legacy);

            float CaptureCase(string name, Vector3 position, Vector3 target, Vector3 sunDirection, bool baseline = false)
            {
                camera.transform.position = position;
                camera.transform.LookAt(target);
                sun.transform.rotation = Quaternion.LookRotation(-sunDirection.normalized);
                shellRenderer.sharedMaterial = baseline ? legacy : material;
                var image = Render(camera);
                float brightness = image.GetPixels().Average(c => c.maxColorComponent);
                File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
                report.AppendLine($"{name}: mean display peak={brightness:F6}");
                return brightness;
            }

            Vector3 orbit = new Vector3(0, 0, -3.6f);
            Vector3 horizonSun = new Vector3(-1, 0.15f, -0.12f).normalized;

            void CheckAtmosphereSignal(string rendererName)
            {
                groundMaterial.SetColor("_BaseColor", Color.black);
                shellRenderer.enabled = false;
                float control = CaptureCase(rendererName + "-signal-control", orbit, Vector3.zero, horizonSun);
                shellRenderer.enabled = true;
                float scattering = CaptureCase(rendererName + "-signal", orbit, Vector3.zero, horizonSun);
                groundMaterial.SetColor("_BaseColor", Color.white);
                if (scattering - control < 0.0005f)
                    throw new InvalidOperationException(rendererName + " has no measurable atmospheric illumination; check the Main Light shader variants.");
                report.AppendLine($"PASS: {rendererName} atmospheric signal above control = {scattering - control:F6}");
            }

            foreach (bool baseline in new[] { true, false })
            {
                string prefix = baseline ? "before-" : "after-";
                CaptureCase(prefix + "day", orbit, Vector3.zero, Vector3.back, baseline);
                CaptureCase(prefix + "sunset", orbit, Vector3.zero, horizonSun, baseline);
                CaptureCase(prefix + "night", orbit, Vector3.zero, Vector3.forward, baseline);
            }
            CaptureCase("after-far", new Vector3(0, 0, -9), Vector3.zero, horizonSun);
            CaptureCase("after-close", new Vector3(0, 0, -1.08f), new Vector3(0, 0.30f, -1), horizonSun);
            CaptureCase("after-inside", new Vector3(0, 0, -1.02f), new Vector3(0, 0.30f, -1), horizonSun);
            CaptureCase("after-below-surface", new Vector3(0, 0, -0.99f), new Vector3(0, 0.30f, -1), horizonSun);
            camera.orthographic = true;
            camera.orthographicSize = 1.3f;
            CaptureCase("after-orthographic", orbit, Vector3.zero, horizonSun);
            camera.orthographic = false;
            CheckAtmosphereSignal("desktop-forward-plus");
            cameraData.requiresDepthTexture = false;
            QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            CaptureCase("after-mobile-no-depth", orbit, Vector3.zero, horizonSun);
            CheckAtmosphereSignal("mobile-forward");
            report.AppendLine("Mobile forward renderer captured with camera depth disabled (desktop Direct3D11, not a device build).");
            QualitySettings.renderPipeline = originalPipeline;
            cameraData.requiresDepthTexture = true;
            ground.transform.localScale = Vector3.one * 1.02f;
            CaptureCase("after-raised-terrain", orbit, Vector3.zero, horizonSun);
            ground.transform.localScale = Vector3.one;
            // Exercise both the scene's narrow multiplier and a shell spanning tall mountains.
            foreach (float outerRadius in new[] { 1.01f, 1.104f })
            {
                float scale = outerRadius / 1.04f;
                shell.transform.localScale = Vector3.one * scale;
                material.SetFloat("_PlanetRadius", 1f / scale);
                shellProperties.SetFloat("_PlanetRadius", 1f / scale);
                shellRenderer.SetPropertyBlock(shellProperties);
                CaptureCase($"after-shell-{outerRadius:F3}", orbit, Vector3.zero, horizonSun);
            }
            shell.transform.localScale = Vector3.one;
            material.SetFloat("_PlanetRadius", 1f);
            shellProperties.SetFloat("_PlanetRadius", 1f);
            shellRenderer.SetPropertyBlock(shellProperties);
            // Full camera orbit samples and adjacent images make discontinuities inspectable.
            for (int angle = 0; angle <= 360; angle += 30)
            {
                Vector3 position = Quaternion.Euler(0, angle, 0) * orbit;
                CaptureCase($"orbit-{angle:000}", position, Vector3.zero, horizonSun);
            }
            foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
            {
                report.AppendLine($"Shader {message.severity}: {message.message} ({message.file}:{message.line})");
                if (message.severity.ToString() == "Error") throw new InvalidOperationException(message.message);
            }
            report.AppendLine("PASS: runtime scripts compiled; shader rendered on " + SystemInfo.graphicsDeviceType);
            report.AppendLine("Synthetic spherical terrain comparison; not a screenshot of the running simulation.");
            File.WriteAllText(Path.Combine(output, "validation.txt"), report.ToString());
            Debug.Log("[AtmosphereValidation] " + report);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "validation.txt"), report + "\nFAIL: " + ex);
            throw;
        }
        finally
        {
            RenderSettings.sun = originalSun;
            RenderSettings.ambientIntensity = originalAmbientIntensity;
            QualitySettings.renderPipeline = originalPipeline;
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
        }
    }

    private static Texture2D Render(Camera camera)
    {
        var target = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
        try
        {
            target.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            else { camera.targetTexture = target; camera.Render(); }
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
            image.Apply();
            return image;
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(target);
        }
    }
}
