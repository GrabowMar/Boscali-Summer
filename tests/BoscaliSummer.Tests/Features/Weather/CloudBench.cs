#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Offline cloud renderer bench: renders fixed weather scenes with the shipped shaders through
// the same map builder and uniforms as the game (built-in pipeline, so no Nuclear Option
// camera stack), times each mode on the GPU and saves a PNG per scene and mode.
// Modes: "old" (the previous full-resolution shader, if present), "full" (current shader,
// full resolution) and "half" (reduced-resolution march + bilateral composite).
public sealed class CloudBench : MonoBehaviour
{
    private const int Width = 1920, Height = 1080, Frames = 12;
    private const float HalfExtent = 60000f;

    private sealed class Scene
    {
        public string Name;
        public WeatherRegimeType State;
        public byte Sets;
        public bool Anchor;
        public Vector3 Camera;
        public float Yaw, Pitch;
        public int LookAtHero = -1;
        public float InCloud;
    }

#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            foreach (string name in new[] { "FlightCloud", "FlightCloudComposite", "FlightCloudOld" })
            {
                Shader shader = Resources.Load<Shader>(name);
                if (shader == null) { if (name == "FlightCloudOld") continue; throw new Exception("Missing shader " + name); }
                if (ShaderUtil.ShaderHasError(shader))
                {
                    var messages = new StringBuilder();
                    foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(shader))
                        messages.AppendLine(m.severity + " " + m.line + ": " + m.message);
                    throw new Exception("Shader compile failed: " + name + "\n" + messages);
                }
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Bench").AddComponent<CloudBench>();
            EditorSceneManager.SaveScene(scene, "Assets/bench.unity");
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/bench.unity" }, "Player/CloudBench.exe",
                BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Player build failed: " + report.summary.result);
            File.WriteAllText("build-result.txt", "PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("build-result.txt", e.ToString()); EditorApplication.Exit(1); }
    }
#endif

    private IEnumerator Start()
    {
        yield return null;
        var log = new StringBuilder();
        try { Run(log); }
        catch (Exception e) { log.AppendLine("FAIL " + e); }
        File.WriteAllText("result.txt", log.ToString());
        Application.Quit(0);
    }

    private static Texture2D Map(byte[] data, int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        t.LoadRawTextureData(data);
        t.Apply(false, false);
        return t;
    }

    private static void Run(StringBuilder log)
    {
        string only = Environment.GetEnvironmentVariable("CLOUD_BENCH_ONLY");
        byte[] noiseBytes = CloudNoise3D.Generate(WeatherVolumeDressingNoise.Size, 47);
        int n3 = WeatherVolumeDressingNoise.Size;
        var pixels = new Color32[n3 * n3 * n3];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(noiseBytes[i * 4], noiseBytes[i * 4 + 1], 0, 255);
        var noise = new Texture3D(n3, n3, n3, TextureFormat.RGBA32, true)
        { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat };
        noise.SetPixels32(pixels);
        noise.Apply(true, true);

        Shader marchShader = Resources.Load<Shader>("FlightCloud");
        Shader compositeShader = Resources.Load<Shader>("FlightCloudComposite");
        Shader oldShader = Resources.Load<Shader>("FlightCloudOld");
        log.AppendLine("GPU " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType +
            " | march supported " + marchShader.isSupported + " composite " + compositeShader.isSupported +
            " old " + (oldShader != null && oldShader.isSupported));
        var march = new Material(marchShader);
        var composite = new Material(compositeShader);
        Material old = oldShader != null ? new Material(oldShader) : null;

        Mesh cube = Cube();
        var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        ground.transform.localScale = new Vector3(700000f, 700000f, 1f);
        ground.GetComponent<Renderer>().sharedMaterial = new Material(Resources.Load<Shader>("BenchGround"));

        var cameraObject = new GameObject("Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.42f, 0.6f, 0.85f);
        camera.nearClipPlane = 1f;
        camera.farClipPlane = 400000f;
        camera.fieldOfView = 60f;
        camera.depthTextureMode = DepthTextureMode.Depth;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Bench" };
        camera.targetTexture = target;
        var lowColour = new RenderTexture(Width / 2, Height / 2, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var lowDepth = new RenderTexture(Width / 2, Height / 2, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        lowColour.Create();
        lowDepth.Create();
        composite.SetTexture("_CloudLowResColour", lowColour);
        composite.SetTexture("_CloudLowResDepth", lowDepth);
        var lowSize = new Vector4(lowColour.width, lowColour.height, 1f / lowColour.width, 1f / lowColour.height);
        composite.SetVector("_CloudLowResSize", lowSize);
        march.SetVector("_CloudLowResSize", lowSize);
        var cmd = new CommandBuffer { name = "Clouds" };
        camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, cmd);
        var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);

        var scenes = new List<Scene>
        {
            new Scene { Name = "broken-high", State = WeatherRegimeType.Broken, Camera = new Vector3(0, 7000, 0), Yaw = 30, Pitch = -6 },
            new Scene { Name = "broken-low", State = WeatherRegimeType.Broken, Camera = new Vector3(0, 900, 0), Yaw = 30, Pitch = 4 },
            new Scene { Name = "scattered-down", State = WeatherRegimeType.Scattered, Camera = new Vector3(0, 9000, 0), Yaw = 120, Pitch = -35 },
            new Scene { Name = "overcast-level", State = WeatherRegimeType.Overcast, Camera = new Vector3(0, 2500, 0), Yaw = 200, Pitch = 1 },
            new Scene { Name = "storm-supercell", State = WeatherRegimeType.Storm, Sets = Superstructures.SupercellSet | Superstructures.SquallLineSet,
                Camera = new Vector3(0, 6000, 0), Pitch = 2, LookAtHero = 1 },
            new Scene { Name = "storm-squall", State = WeatherRegimeType.Storm, Sets = Superstructures.SupercellSet | Superstructures.SquallLineSet,
                Camera = new Vector3(0, 3000, 0), Pitch = 1, LookAtHero = 0 },
            new Scene { Name = "eye", State = WeatherRegimeType.Storm, Sets = Superstructures.StormEyeSet, Anchor = true,
                Camera = new Vector3(0, 5000, 25000), Yaw = 10, Pitch = 3 },
            new Scene { Name = "lenticulars", State = WeatherRegimeType.Fair, Sets = Superstructures.LenticularSet, Anchor = true,
                Camera = new Vector3(0, 3500, -20000), Pitch = 3, LookAtHero = 0 },
            new Scene { Name = "seafog", State = WeatherRegimeType.Clear, Sets = Superstructures.FogBankSet, Camera = new Vector3(0, 1500, 0), Yaw = 60, Pitch = -12 },
        };

        var uniforms = new CloudVolumeUniforms();
        foreach (Scene s in scenes)
        {
            if (!string.IsNullOrEmpty(only) && !s.Name.Contains(only)) continue;
            var key = new WeatherKey(90210u, 0f, false, (byte)s.State, 5f, 60f, s.Sets, 0, s.Anchor, 0f, 25000f, 0);
            const float time = 900f;
            var field = new WeatherField();
            field.Build(key, time, HalfExtent, HalfExtent, 13f);
            var watch = Stopwatch.StartNew();
            CloudMaps maps = CloudMaps.Build(key, time, HalfExtent, HalfExtent, 13f, 105000f, 315000f);
            long mapMs = watch.ElapsedMilliseconds;
            Texture2D near = Map(maps.Near, CloudMaps.NearSize), nearProfiles = Map(maps.NearProfiles, CloudMaps.NearSize);
            Texture2D far = Map(maps.Far, CloudMaps.FarSize), farProfiles = Map(maps.FarProfiles, CloudMaps.FarSize);
            Texture2D envelope = Map(maps.Envelope, CloudMaps.EnvelopeSize);
            envelope.filterMode = FilterMode.Point;

            float yaw = s.Yaw;
            if (s.LookAtHero >= 0 && s.LookAtHero < field.SuperstructureCount)
            {
                Superstructure h = field.SuperstructureAt(s.LookAtHero);
                yaw = Mathf.Atan2(h.X - s.Camera.x, h.Z - s.Camera.z) * Mathf.Rad2Deg;
            }
            camera.transform.position = s.Camera;
            camera.transform.rotation = Quaternion.Euler(-s.Pitch, yaw, 0f);
            Vector3 sun = new Vector3(0.35f, 0.55f, -0.65f).normalized;

            uniforms.Settle(field);
            foreach (string mode in new[] { "old", "full", "half" })
            {
                if (mode == "old" && old == null) continue;
                Material m = mode == "old" ? old : march;
                var frame = new CloudFrame
                {
                    WorldOffset = Vector3.zero,
                    CameraPosition = camera.transform.position,
                    CameraForward = camera.transform.forward,
                    FieldOfView = camera.fieldOfView,
                    PixelHeight = mode == "half" ? Height / 2 : Height,
                    Bottom = maps.Bottom, Top = maps.Top,
                    HorizonCover = maps.HorizonCover,
                    SunDirection = sun,
                    SunColor = new Color(1.9f, 1.8f, 1.6f),
                    Ambient = new Color(0.55f, 0.65f, 0.85f) * 0.45f + new Color(0.72f, 0.8f, 0.9f) * 0.3f + new Color(1.9f, 1.8f, 1.6f) * 0.05f,
                    Ground = new Color(0.25f, 0.27f, 0.22f) * 0.25f + new Color(0.72f, 0.8f, 0.9f) * 0.1f,
                    Fog = new Color(0.72f, 0.8f, 0.9f),
                    Extinction = 0.00004f,
                    CameraInCloud = s.InCloud,
                    DeltaTime = 0f,
                };
                uniforms.Apply(m, field, frame, noise);
                CloudVolumeUniforms.ApplySpans(m, 105000f, 315000f);
                if (mode == "old" && field.SuperstructureCount > 0 && Environment.GetEnvironmentVariable("CLOUD_BENCH_VARIANT") != "1")
                {
                    float top = 0f;
                    for (int i = 0; i < field.SuperstructureCount; i++) top = Mathf.Max(top, field.SuperstructureAt(i).Top + 1500f);
                    m.SetVector("_CloudAltitudeBounds", new Vector2(Mathf.Min(maps.Bottom, 350f), Mathf.Max(maps.Top, top)));
                }
                m.SetTexture("_WeatherMapTex", near);
                m.SetTexture("_WeatherProfileTex", nearProfiles);
                m.SetTexture("_WeatherFarMapTex", far);
                m.SetTexture("_WeatherFarProfileTex", farProfiles);
                m.SetTexture("_WeatherEnvelopeTex", envelope);
                m.SetFloat("_WeatherEnvelopeOn", Environment.GetEnvironmentVariable("CLOUD_BENCH_NOENVELOPE") == "1" ? 0f : 1f);
                uniforms.ApplyFrustum(m, camera);

                Matrix4x4 box = Matrix4x4.TRS(camera.transform.position, Quaternion.identity, Vector3.one * 1000f);
                cmd.Clear();
                if (mode == "half")
                {
                    cmd.SetRenderTarget(new RenderTargetIdentifier[] { lowColour, lowDepth }, lowColour.depthBuffer);
                    cmd.ClearRenderTarget(false, true, Color.clear);
                    cmd.DrawProcedural(Matrix4x4.identity, m, 2, MeshTopology.Triangles, 3);
                    cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                    cmd.DrawMesh(cube, box, composite, 0, 0);
                }
                else if (mode != "none") cmd.DrawMesh(cube, box, m, 0, 0);

                for (int i = 0; i < 3; i++) camera.Render();
                Sync(target, readback);
                watch.Restart();
                for (int i = 0; i < Frames; i++) camera.Render();
                Sync(target, readback);
                double ms = watch.Elapsed.TotalMilliseconds / Frames;
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                readback.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(s.Name + "-" + mode + ".png", readback.EncodeToPNG());
                log.AppendLine(s.Name.PadRight(16) + mode.PadRight(5) + ms.ToString("0.00").PadLeft(8) + " ms/frame" +
                    "  (maps " + mapMs + " ms, heroes " + field.SuperstructureCount + ")");
            }
            cmd.Clear();
            camera.Render();
            Sync(target, readback);
            watch.Restart();
            for (int i = 0; i < Frames; i++) camera.Render();
            Sync(target, readback);
            log.AppendLine(s.Name.PadRight(16) + "none " + (watch.Elapsed.TotalMilliseconds / Frames).ToString("0.00").PadLeft(8) + " ms/frame");
            UnityEngine.Object.Destroy(near); UnityEngine.Object.Destroy(nearProfiles);
            UnityEngine.Object.Destroy(far); UnityEngine.Object.Destroy(farProfiles); UnityEngine.Object.Destroy(envelope);
        }
    }

    private static void Sync(RenderTexture target, Texture2D readback)
    {
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        readback.Apply();
        RenderTexture.active = null;
    }

    private static Mesh Cube()
    {
        var m = new Mesh();
        m.vertices = new[] {
            new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
            new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
            new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f),
            new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
        m.triangles = new[] { 0,2,1, 0,3,2, 1,2,6, 1,6,5, 5,6,7, 5,7,4, 4,7,3, 4,3,0, 3,7,6, 3,6,2, 4,0,1, 4,1,5 };
        m.RecalculateBounds();
        return m;
    }
}

internal static class WeatherVolumeDressingNoise
{
    internal const int Size = 64;
}
#endif
