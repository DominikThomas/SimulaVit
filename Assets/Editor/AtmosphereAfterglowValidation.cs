using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated GPU checks for a visible night rim without face-wide glow.</summary>
public static class AtmosphereAfterglowValidation
{
    [MenuItem("Tools/Atmosphere/Validate Cool Afterglow")]
    public static void Capture()
    {
        string output = Path.GetFullPath("Artifacts/AtmosphereAfterglow");
        Directory.CreateDirectory(output);
        var scene = EditorSceneManager.NewPreviewScene();
        Light originalSun = RenderSettings.sun;
        var originalPipeline = QualitySettings.renderPipeline;
        var owned = new List<Object>();
        var report = new StringBuilder();
        try
        {
            var material = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Atmosphere_Fresnel_Mat.mat"));
            owned.Add(material);
            Material before = null;
            Shader priorShader = Shader.Find("SimulaVit/BeforeAfterglow");
            if (priorShader != null)
            {
                before = new Material(priorShader);
                var snapshot = AssetDatabase.LoadAssetAtPath<Material>("Assets/Editor/AfterglowBaseline/Atmosphere.mat");
                if (snapshot != null) before.CopyPropertiesFromMaterial(snapshot);
                owned.Add(before);
            }
            var geometry = IcosphereRenderGeometryCache.GetOrBuild(5);
            Mesh Sphere(float radius, string name)
            {
                var mesh = IcosphereRenderMeshBuilder.BuildSurfaceMesh(geometry, radius, name);
                owned.Add(mesh); return mesh;
            }
            var groundMaterial = new Material(Shader.Find("SimulaVit/GeodesicVertexColorURP"));
            owned.Add(groundMaterial);
            var groundMesh = Sphere(1, "Afterglow ground");
            var vertices = groundMesh.vertices;
            var colours = new Color[vertices.Length];
            for (int i=0; i<colours.Length; ++i)
                colours[i] = Color.Lerp(new Color(0.05f,0.12f,0.21f),new Color(0.22f,0.28f,0.13f),
                    Mathf.SmoothStep(0,1,0.5f+0.5f*Mathf.Sin(vertices[i].x*9)*Mathf.Cos(vertices[i].z*7)));
            groundMesh.colors = colours;
            MeshRenderer Renderer(string name, Mesh mesh, Material mat)
            {
                var go=new GameObject(name); SceneManager.MoveGameObjectToScene(go,scene);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var result=go.AddComponent<MeshRenderer>(); result.sharedMaterial=mat;
                result.shadowCastingMode=ShadowCastingMode.Off; return result;
            }
            var ground=Renderer("Afterglow ground",groundMesh,groundMaterial);
            var shell=Renderer("Afterglow shell",Sphere(1.011f,"Afterglow shell"),material);
            var camera=new GameObject("Afterglow camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject,scene); camera.scene=scene;
            camera.enabled=false; camera.fieldOfView=38; camera.nearClipPlane=0.001f; camera.farClipPlane=100;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
            var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=false;
            var sun=new GameObject("Afterglow Sun").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(sun.gameObject,scene);
            sun.type=LightType.Directional; sun.intensity=1; sun.shadows=LightShadows.None;
            RenderSettings.sun=sun;
            Vector3 orbit=new Vector3(0,0,-3.8f);
            Vector3 below=new Vector3(-1,0.15f,0.06f).normalized;
            float inner=1.001f, outer=1.011f;
            Texture2D Render(string name, Material mat, Vector3 position, Vector3 light)
            {
                camera.transform.position=position; camera.transform.LookAt(Vector3.zero);
                sun.transform.rotation=Quaternion.LookRotation(-light.normalized);
                shell.sharedMaterial=mat;
                var block=new MaterialPropertyBlock();
                block.SetFloat("_PlanetRadius",1); block.SetFloat("_AtmosphereInnerRadius",inner);
                block.SetFloat("_AtmosphereRadius",outer); shell.SetPropertyBlock(block);
                var image=AtmosphereScatteringValidation.Render(camera);
                if(name!=null) File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
                return image;
            }
            void Save(string name, Material mat, Vector3 position, Vector3 light)
                => Object.DestroyImmediate(Render(name,mat,position,light));

            foreach (var mat in before!=null ? new[]{before,material} : new[]{material})
            {
                string prefix=mat==before ? "before-" : "after-";
                Save(prefix+"day",mat,orbit,Vector3.back);
                Save(prefix+"just-below-horizon",mat,orbit,below);
                Save(prefix+"far-side",mat,orbit,Vector3.forward);
                Save(prefix+"off-axis-night",mat,orbit,new Vector3(0.3f,0.2f,1));
            }
            for(int angle=-60; angle<=60; angle+=15)
                Save($"night-orbit-{angle+60:000}",material,Quaternion.Euler(0,angle,0)*orbit,Vector3.forward);

            // ReadPixels gives display values. Compare only the residual, using an
            // otherwise identical draw with NightGlow=0 as a control.
            float projectedRadius=360f/Mathf.Tan(19*Mathf.Deg2Rad)/Mathf.Sqrt(orbit.sqrMagnitude-1);
            (float rim,float centre,float peak) ResidualSignal(Material mat, Vector3 light)
            {
                var lit=Render(null,mat,orbit,light);
                float strength=mat.GetFloat("_NightGlow"); mat.SetFloat("_NightGlow",0);
                var control=Render(null,mat,orbit,light); mat.SetFloat("_NightGlow",strength);
                var p=lit.GetPixels(); var q=control.GetPixels();
                float rim=0, centre=0, peak=0; int rimCount=0, centreCount=0;
                for(int y=0;y<lit.height;++y) for(int x=0;x<lit.width;++x)
                {
                    float r=new Vector2(x-480,y-360).magnitude/projectedRadius;
                    float difference=Mathf.Max(0,p[y*lit.width+x].b-q[y*lit.width+x].b);
                    if(r>0.96f && r<1.06f) {rim+=difference; ++rimCount; peak=Mathf.Max(peak,difference);}
                    if(r<0.65f) {centre+=difference; ++centreCount;}
                }
                Object.DestroyImmediate(lit); Object.DestroyImmediate(control);
                return (rim/rimCount,centre/centreCount,peak);
            }
            foreach(var pair in new[]{("below-horizon",below),("far-side",Vector3.forward)})
            {
                var signal=ResidualSignal(material,pair.Item2);
                report.AppendLine($"{pair.Item1}: residual rim mean={signal.rim:F6}, peak={signal.peak:F6}, centre={signal.centre:F6}");
                if(signal.rim<0.002f || signal.peak<0.02f) throw new Exception("Night afterglow not visible: "+pair.Item1);
                if(signal.centre>0.0005f) throw new Exception("Night afterglow spills across terrain.");
                if(before!=null)
                {
                    var previous=ResidualSignal(before,pair.Item2);
                    report.AppendLine($"  Previous rim mean={previous.rim:F6}; improvement={signal.rim/Mathf.Max(previous.rim,1e-6f):F2}x");
                    if(signal.rim<previous.rim*2) throw new Exception("Insufficient afterglow recovery.");
                }
            }
            // All sunlit/warm scattering must be unchanged when residuals are off.
            if(before!=null)
            {
                float strength=material.GetFloat("_NightGlow"); material.SetFloat("_NightGlow",0); before.SetFloat("_NightGlow",0);
                var a=Render(null,material,orbit,below); var b=Render(null,before,orbit,below);
                var p=a.GetPixels();var q=b.GetPixels(); float difference=0;
                for(int i=0;i<p.Length;++i) difference+=Mathf.Abs(p[i].r-q[i].r)+Mathf.Abs(p[i].g-q[i].g)+Mathf.Abs(p[i].b-q[i].b);
                difference/=p.Length*3;
                Object.DestroyImmediate(a);Object.DestroyImmediate(b); material.SetFloat("_NightGlow",strength);
                report.AppendLine($"Residual disabled: old/new mean RGB difference={difference:F9}");
                if(difference>0.00001f) throw new Exception("Warm/sunlit scattering changed.");
            }

            // A tall isolated peak should neither detach the glow nor switch it off.
            for(int i=0;i<vertices.Length;++i)
            {
                Vector3 n=vertices[i].normalized;
                vertices[i]=n*(1+0.07f*Mathf.Exp((Vector3.Dot(n,new Vector3(-1,0.15f,-0.1f).normalized)-1)*100));
            }
            groundMesh.vertices=vertices; groundMesh.RecalculateNormals(); groundMesh.RecalculateBounds();
            inner=1.071f; outer=1.0817f;
            shell.GetComponent<MeshFilter>().sharedMesh=Sphere(outer,"Afterglow mountain shell");
            Save("after-mountain-night",material,orbit,Vector3.forward);
            if(before!=null) {before.SetFloat("_NightGlow",0.012f); Save("before-mountain-night",before,orbit,Vector3.forward);}
            var mountainSignal=ResidualSignal(material,Vector3.forward);
            report.AppendLine($"Mountain-clear shell: rim={mountainSignal.rim:F6}, centre={mountainSignal.centre:F6}");
            if(mountainSignal.rim<0.002f || mountainSignal.centre>0.0005f) throw new Exception("Mountain shell afterglow failed.");
            QualitySettings.renderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            data.requiresDepthTexture=false;
            Save("after-mobile-night",material,orbit,Vector3.forward);
            foreach(var message in ShaderUtil.GetShaderMessages(material.shader))
                if(message.severity.ToString()=="Error") throw new Exception(message.message);
            report.AppendLine("PASS: runtime/editor compile and GPU rendering on "+SystemInfo.graphicsDeviceType+"; desktop Forward+ and mobile Forward.");
            report.AppendLine("Synthetic shader comparison. No gameplay world or simulation was changed.");
            File.WriteAllText(Path.Combine(output,"validation.txt"),report.ToString()); Debug.Log(report);
        }
        catch(Exception ex) {File.WriteAllText(Path.Combine(output,"validation.txt"),report+"\nFAIL: "+ex);throw;}
        finally
        {
            RenderSettings.sun=originalSun; QualitySettings.renderPipeline=originalPipeline;
            EditorSceneManager.ClosePreviewScene(scene);
            foreach(var item in owned) if(item!=null) Object.DestroyImmediate(item);
        }
    }
}
