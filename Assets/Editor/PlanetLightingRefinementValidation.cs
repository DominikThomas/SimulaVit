using System;
using System.Collections.Generic;
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

/// <summary>Controlled GPU comparisons of visual shaders only; never generates or saves a gameplay world.</summary>
public static class PlanetLightingRefinementValidation
{
    [MenuItem("Tools/Atmosphere/Validate Lighting Refinement")]
    public static void Capture()
    {
        string output = Path.GetFullPath("Artifacts/AtmosphereRefinement");
        Directory.CreateDirectory(output);
        var scene = EditorSceneManager.NewPreviewScene();
        Light originalSun = RenderSettings.sun;
        var originalPipeline = QualitySettings.renderPipeline;
        var owned = new List<Object>();
        var report = new StringBuilder();
        try
        {
            Material MaterialFor(string name)
            {
                var shader = Shader.Find(name);
                if (shader == null || !shader.isSupported) throw new Exception("Missing/unsupported shader: " + name);
                var result = new Material(shader); owned.Add(result); return result;
            }
            var atmosphere = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Atmosphere_Fresnel_Mat.mat"));
            owned.Add(atmosphere);
            var terrain = MaterialFor("SimulaVit/GeodesicVertexColorURP");
            var ocean = MaterialFor("SimulaVit/GeodesicOceanURP");
            var lake = MaterialFor("SimulaVit/GeodesicLakeURP");
            var current = new[] { atmosphere, terrain, ocean, lake };
            var before = new Material[4];
            string[] names = { "Atmosphere", "Terrain", "Ocean", "Lake" };
            bool hasBaseline = Shader.Find("SimulaVit/BeforeRefinementAtmosphere") != null;
            if (hasBaseline)
            {
                for (int i = 0; i < 4; ++i) before[i] = MaterialFor("SimulaVit/BeforeRefinement" + names[i]);
                var priorMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Editor/LightingBaseline/Atmosphere.mat");
                if (priorMaterial != null) before[0].CopyPropertiesFromMaterial(priorMaterial);
            }
            var geometry = IcosphereRenderGeometryCache.GetOrBuild(5);
            Mesh Sphere(float radius, string name)
            {
                var mesh = IcosphereRenderMeshBuilder.BuildSurfaceMesh(geometry, radius, name);
                owned.Add(mesh); return mesh;
            }
            var groundMesh = Sphere(1, "Lighting validation mountains");
            var vertices = groundMesh.vertices;
            var colours = new Color[vertices.Length];
            var radii = new float[vertices.Length];
            var lakeAxis = new Vector3(0.35f, 0.3f, -1).normalized;
            float maximum = 0;
            for (int i = 0; i < vertices.Length; ++i)
            {
                Vector3 n = vertices[i].normalized;
                float land = Mathf.Sin(n.x * 8 + n.y * 3) * Mathf.Cos(n.z * 7 - n.y * 3);
                float peak = Mathf.Exp((Vector3.Dot(n, new Vector3(-1,0.18f,-0.12f).normalized) - 1) * 90);
                float lakePlateau = Mathf.Exp((Vector3.Dot(n, lakeAxis) - 1) * 45);
                radii[i] = 1 + land * 0.016f + peak * 0.075f + lakePlateau * 0.028f;
                maximum = Mathf.Max(maximum, radii[i]);
                vertices[i] = n * radii[i];
                colours[i] = peak > 0.35f ? new Color(0.38f,0.34f,0.29f) : new Color(0.23f,0.3f,0.13f);
            }
            groundMesh.vertices = vertices; groundMesh.colors = colours;
            groundMesh.RecalculateNormals(); groundMesh.RecalculateBounds();
            const float sea = 1.003f;
            float inner = maximum + 0.001f;
            float outer = inner + maximum * 0.01f;
            report.AppendLine($"terrainMax={maximum:F6}; inner={inner:F6}; outer={outer:F6}; clearance={inner-maximum:F6}");
            if (inner <= maximum || outer <= inner) throw new Exception("Atmosphere terrain clearance failed.");
            var oceanMesh = Sphere(sea, "Validation ocean");
            var lakeMesh = Sphere(1.039f, "Validation lake cap");
            var lakeVertices = lakeMesh.vertices;
            var lakeTriangles = new List<int>();
            var allTriangles = lakeMesh.triangles;
            for (int i = 0; i < allTriangles.Length; i += 3)
            {
                Vector3 n = (lakeVertices[allTriangles[i]] + lakeVertices[allTriangles[i+1]] + lakeVertices[allTriangles[i+2]]).normalized;
                if (Vector3.Dot(n, lakeAxis) > Mathf.Cos(9 * Mathf.Deg2Rad))
                    lakeTriangles.AddRange(new[] { allTriangles[i], allTriangles[i+1], allTriangles[i+2] });
            }
            lakeMesh.triangles = lakeTriangles.ToArray();
            var shellMesh = Sphere(outer, "Validation atmosphere");
            MeshRenderer Renderer(string name, Mesh mesh, Material material)
            {
                var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; return renderer;
            }
            var ground = Renderer("Terrain", groundMesh, terrain);
            var water = Renderer("Ocean", oceanMesh, ocean);
            var lakeRenderer = Renderer("Lake", lakeMesh, lake);
            var shell = Renderer("Atmosphere", shellMesh, atmosphere);
            var renderers = new[] { shell, ground, water, lakeRenderer };
            var camera = new GameObject("Lighting validation camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene); camera.scene = scene;
            camera.enabled = false; camera.fieldOfView = 38; camera.nearClipPlane = 0.001f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.001f,0.002f,0.005f);
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false;
            var sun = new GameObject("Validation Sun").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
            sun.type = LightType.Directional; sun.shadows = LightShadows.None; sun.intensity = 1; RenderSettings.sun = sun;
            var orbit = new Vector3(0,0,-3.8f);
            var sunset = new Vector3(-1,0.15f,-0.12f).normalized;
            Texture2D CaptureCase(string name, Vector3 position, Vector3 target, Vector3 sunDirection, bool prior = false)
            {
                camera.transform.position = position; camera.transform.LookAt(target);
                sun.transform.rotation = Quaternion.LookRotation(-sunDirection.normalized);
                for (int i = 0; i < 4; ++i) renderers[i].sharedMaterial = prior ? before[i] : current[i];
                shell.transform.localScale = Vector3.one * (prior ? maximum * 1.01f / outer : 1);
                var properties = new MaterialPropertyBlock();
                properties.SetFloat("_PlanetRadius", prior ? sea / shell.transform.localScale.x : sea);
                properties.SetFloat("_AtmosphereInnerRadius", inner);
                properties.SetFloat("_AtmosphereRadius", outer);
                shell.SetPropertyBlock(properties);
                var image = AtmosphereScatteringValidation.Render(camera);
                File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
                return image;
            }
            void Save(string name, Vector3 position, Vector3 sunDirection, bool prior = false)
            { Object.DestroyImmediate(CaptureCase(name, position, Vector3.zero, sunDirection, prior)); }

            foreach (bool prior in hasBaseline ? new[] {true,false} : new[] {false})
            {
                string prefix = prior ? "before-" : "after-";
                Save(prefix+"day", orbit, Vector3.back, prior);
                Save(prefix+"sunset", orbit, sunset, prior);
                Save(prefix+"night", orbit, Vector3.forward, prior);
                Save(prefix+"mountains", Quaternion.Euler(0,12,0)*orbit, sunset, prior);
            }
            for (int angle = 0; angle <= 360; angle += 30)
                Save($"orbit-{angle:000}", Quaternion.Euler(0,angle,0)*orbit, sunset);
            Object.DestroyImmediate(CaptureCase("after-close", new Vector3(0,0,-1.12f), new Vector3(0,0.25f,-1), sunset));

            // Shader-only atmosphere on black terrain quantifies unwanted face-wide tint.
            water.enabled = lakeRenderer.enabled = false;
            terrain.SetColor("_BaseColor", Color.black);
            if (hasBaseline) before[1].SetColor("_BaseColor", Color.black);
            float CentreSignal(Texture2D image)
            {
                var pixels = image.GetPixels(); float sum = 0; int count = 0;
                for (int y=240; y<480; ++y) for (int x=360; x<600; ++x)
                { sum += pixels[y*image.width+x].maxColorComponent; ++count; }
                return sum/count;
            }
            if (hasBaseline)
            {
                var old = CaptureCase("before-atmosphere-only",orbit,Vector3.zero,sunset,true);
                var revised = CaptureCase("after-atmosphere-only",orbit,Vector3.zero,sunset);
                float oldSignal = CentreSignal(old), newSignal = CentreSignal(revised);
                report.AppendLine($"Atmosphere central-disc signal before={oldSignal:F6}, after={newSignal:F6}");
                Object.DestroyImmediate(old); Object.DestroyImmediate(revised);
                if (newSignal > oldSignal * 0.3f + 0.001f) throw new Exception("Atmosphere still overlays the central terrain.");
            }
            terrain.SetColor("_BaseColor", Color.white);
            ground.enabled = shell.enabled = false; water.enabled = true;
            water.GetComponent<MeshFilter>().sharedMesh = Sphere(1, "Water glint test");
            float Difference(Texture2D a, Texture2D b)
            {
                var p=a.GetPixels(); var q=b.GetPixels(); float maximumDifference=0;
                for (int i=0;i<p.Length;++i) maximumDifference=Mathf.Max(maximumDifference, p[i].maxColorComponent-q[i].maxColorComponent);
                return maximumDifference;
            }
            foreach (var pair in new[] { ("ocean",ocean), ("lake",lake) })
            {
                // Reuse the ocean renderer with either actual water shader on identical geometry.
                current[2] = pair.Item2;
                pair.Item2.SetFloat("_SunGlintStrength",0);
                var control=CaptureCase(pair.Item1+"-glint-control",orbit,Vector3.zero,Vector3.back);
                pair.Item2.SetFloat("_SunGlintStrength",4);
                var lit=CaptureCase(pair.Item1+"-glint",orbit,Vector3.zero,Vector3.back);
                float signal=Difference(lit,control);
                Object.DestroyImmediate(control); Object.DestroyImmediate(lit);
                if(signal<0.04f) throw new Exception(pair.Item1+" missing Sun glint.");
                report.AppendLine($"PASS {pair.Item1}: glint signal={signal:F6}");
                Save(pair.Item1+"-glint-moved",orbit,new Vector3(0.8f,0,-0.6f));
                var night=CaptureCase(pair.Item1+"-night",orbit,Vector3.zero,Vector3.forward);
                pair.Item2.SetFloat("_SunGlintStrength",0);
                var nightControl=CaptureCase(pair.Item1+"-night-control",orbit,Vector3.zero,Vector3.forward);
                if(Difference(night,nightControl)>0.005f) throw new Exception(pair.Item1+" glints on the night side.");
                pair.Item2.SetFloat("_SunGlintStrength",4);
                Object.DestroyImmediate(night); Object.DestroyImmediate(nightControl);
            }
            current[2]=ocean;
            water.GetComponent<MeshFilter>().sharedMesh=oceanMesh;
            ground.enabled=shell.enabled=lakeRenderer.enabled=true;
            QualitySettings.renderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            data.requiresDepthTexture=false;
            Save("after-mobile-sunset",orbit,sunset);
            foreach(var material in current)
                foreach(var message in ShaderUtil.GetShaderMessages(material.shader))
                    if(message.severity.ToString()=="Error") throw new Exception(message.message);
            report.AppendLine("PASS: all four shaders rendered on "+SystemInfo.graphicsDeviceType+"; desktop Forward+ and mobile Forward.");
            report.AppendLine("Controlled synthetic terrain, water and mountain geometry. No gameplay world, hydrology or scene asset was changed.");
            File.WriteAllText(Path.Combine(output,"validation.txt"),report.ToString()); Debug.Log(report);
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(output,"validation.txt"),report+"\nFAIL: "+ex); throw; }
        finally
        {
            RenderSettings.sun=originalSun; QualitySettings.renderPipeline=originalPipeline;
            EditorSceneManager.ClosePreviewScene(scene);
            foreach(var item in owned) if(item!=null) Object.DestroyImmediate(item);
        }
    }
}
