#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;
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
        public WeatherScenario? Preset;
        public byte Turn, Salt;
        public Vector3 Camera;
        public float Yaw, Pitch;
        public int LookAtHero = -1;
        /// <summary>0 fixed camera, 1 in first cell, 2 beside its anvil, 3 cumulus group, 4 set-piece portrait, 5 outside first cell.</summary>
        public int Place;
        public Vector3 Travel;
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
        int exitCode = 0;
        try { Run(log); }
        catch (Exception e) { log.AppendLine("FAIL " + e); exitCode = 1; }
        File.WriteAllText("result.txt", log.ToString());
        Application.Quit(exitCode);
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
        string[] filters = string.IsNullOrEmpty(only) ? Array.Empty<string>() : only.Split(',');
        bool staticView = Environment.GetEnvironmentVariable("CLOUD_BENCH_STATIC") == "1";
        string poseFile = Environment.GetEnvironmentVariable("CLOUD_BENCH_POSES");
        var fixedPoses = new Dictionary<string, float[]>();
        if (!string.IsNullOrEmpty(poseFile))
            foreach (string line in File.ReadAllLines(poseFile))
            {
                string[] columns = line.Split(',');
                if (columns.Length != 9 || columns[0] == "scene") continue;
                var values = new float[8];
                for (int i = 0; i < values.Length; i++) values[i] = float.Parse(columns[i + 1], CultureInfo.InvariantCulture);
                fixedPoses.Add(columns[0], values);
            }
        File.WriteAllText("scene-poses.csv", "scene,x,y,z,yaw,pitch,travelX,travelY,travelZ\n");
        int settleFrames = int.TryParse(Environment.GetEnvironmentVariable("CLOUD_BENCH_SETTLE"), out int settle) ? Math.Max(4, settle) : 48;
        int measuredFrames = int.TryParse(Environment.GetEnvironmentVariable("CLOUD_BENCH_FRAMES"), out int measured) ? Math.Max(12, measured) : 48;
        byte[] noiseBytes = CloudNoise3D.Generate(WeatherVolumeDressingNoise.Size, 47);
        int n3 = WeatherVolumeDressingNoise.Size;
        var pixels = new Color32[n3 * n3 * n3];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(noiseBytes[i * 4], noiseBytes[i * 4 + 1], noiseBytes[i * 4 + 2], 255);
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
        var lowRes = new CloudLowRes();
        lowRes.Ensure(Width, Height);
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
            new Scene { Name = "fair-humilis-low", State = WeatherRegimeType.Fair, Camera = new Vector3(0, 1400, 0), Yaw = 40, Pitch = 8, Place = 3 },
            new Scene { Name = "fair-humilis-high", State = WeatherRegimeType.Fair, Camera = new Vector3(0, 4000, 0), Yaw = 40, Pitch = -16, Place = 3 },
            new Scene { Name = "scattered-cu", State = WeatherRegimeType.Scattered, Camera = new Vector3(0, 2400, 0), Yaw = 70, Pitch = 2, Place = 3 },
            new Scene { Name = "broken-sc", State = WeatherRegimeType.Broken, Camera = new Vector3(0, 1500, 0), Yaw = 20, Pitch = 4 },
            new Scene { Name = "overcast-st", State = WeatherRegimeType.Overcast, Camera = new Vector3(0, 400, 0), Yaw = 15, Pitch = 10 },
            new Scene { Name = "ns-squall", State = WeatherRegimeType.RainSquall, Camera = new Vector3(0, 400, 0), Yaw = 160, Pitch = 8 },
            new Scene { Name = "night-squall", State = WeatherRegimeType.RainSquall, Camera = new Vector3(0, 900, 0), Yaw = 160, Pitch = 5 },
            new Scene { Name = "storm-anvil", State = WeatherRegimeType.Storm, Camera = new Vector3(0, 8000, 0), Pitch = 2, Place = 2 },
            new Scene { Name = "in-cloud", State = WeatherRegimeType.Storm, Camera = new Vector3(0, 2500, 0), Place = 1 },
            new Scene { Name = "supercell-profile", State = WeatherRegimeType.Clear, Sets = Superstructures.SupercellSet, LookAtHero = 0, Place = 4 },
            new Scene { Name = "shelf-profile", State = WeatherRegimeType.Clear, Sets = Superstructures.SquallLineSet, LookAtHero = 0, Place = 4 },
            new Scene { Name = "lens-profile", State = WeatherRegimeType.Clear, Sets = Superstructures.LenticularSet, LookAtHero = 0, Place = 4 },
            new Scene { Name = "tower-profile", State = WeatherRegimeType.Storm, Place = 5 },
            new Scene { Name = "storm-crown-exterior", State = WeatherRegimeType.Storm, Place = 8 },
            new Scene { Name = "frontal-edge-exterior", State = WeatherRegimeType.Storm, Place = 9 },
            new Scene { Name = "flythrough-cumulus", State = WeatherRegimeType.Scattered, Place = 6 },
            new Scene { Name = "flythrough-storm", State = WeatherRegimeType.Storm, Place = 7 },
        };

        // Exercise the console's actual scenario definitions, plus all state and set buttons.
        foreach (WeatherRegimeType state in (WeatherRegimeType[])Enum.GetValues(typeof(WeatherRegimeType)))
        {
            scenes.Add(new Scene { Name = "console-state-" + state + "-above", State = state,
                Camera = new Vector3(0, 9000, 0), Yaw = 30, Pitch = -22 });
            scenes.Add(new Scene { Name = "console-state-" + state + "-below", State = state,
                Camera = new Vector3(0, 400, 0), Yaw = 30, Pitch = 10 });
        }
        foreach (WeatherScenario preset in (WeatherScenario[])Enum.GetValues(typeof(WeatherScenario)))
            scenes.Add(new Scene { Name = "console-preset-" + preset, Preset = preset, State = WeatherRegimeType.Storm,
                Camera = new Vector3(0, preset == WeatherScenario.SeaFog ? 600 : 4500, 0),
                Pitch = preset == WeatherScenario.HurricaneEye ? 45 : preset == WeatherScenario.SeaFog ? -12 : 5 });
        for (int bit = 0; bit < 6; bit++)
            scenes.Add(new Scene { Name = "console-set-" + (1 << bit), State = WeatherRegimeType.Clear,
                Sets = (byte)(1 << bit), Anchor = true, LookAtHero = bit < 5 ? 0 : -1, Place = bit < 5 ? 4 : 0,
                Camera = new Vector3(0, 180, 0), Pitch = 1 });
        foreach (byte turn in new byte[] { 0, 1, 4 })
            scenes.Add(new Scene { Name = "console-front-turn-" + turn, State = WeatherRegimeType.Overcast,
                Turn = turn, Camera = new Vector3(0, 18000, 0), Pitch = -65 });
        scenes.Add(new Scene { Name = "console-reroll", State = WeatherRegimeType.Scattered, Salt = 1,
            Camera = new Vector3(0, 9000, 0), Yaw = 30, Pitch = -22 });
        scenes.Add(new Scene { Name = "console-eye-above", Preset = WeatherScenario.HurricaneEye,
            Camera = new Vector3(0, 22000, -20000), Pitch = -48 });
        scenes.Add(new Scene { Name = "console-eye-wall", Preset = WeatherScenario.HurricaneEye,
            Camera = new Vector3(0, 4500, 0), Pitch = 0 });

        var uniforms = new CloudVolumeUniforms();
        foreach (Scene s in scenes)
        {
            if (filters.Length > 0 && !Array.Exists(filters, filter => s.Name.Contains(filter))) continue;
            var key = new WeatherKey(90210u, 0f, false, (byte)s.State, 5f, 60f, s.Sets, s.Salt, s.Anchor, 0f, 25000f, s.Turn);
            if (s.Preset.HasValue) key = WeatherScenarios.Apply(s.Preset.Value, key, true, 0f, 0f, 0f, 1f);
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

            if (s.Place == 1 && field.CellCount > 0)
            {
                StormCell cell = field.Cell(0);
                s.Camera = new Vector3(cell.X, (cell.Base + cell.Top) * 0.45f, cell.Z);
            }
            else if (s.Place == 2 && field.CellCount > 0)
            {
                StormCell cell = field.Cell(0);
                s.Camera = new Vector3(cell.X - cell.Radius * 2.2f, cell.Base + (cell.Top - cell.Base) * 0.82f, cell.Z);
                s.Yaw = Mathf.Atan2(cell.X - s.Camera.x, cell.Z - s.Camera.z) * Mathf.Rad2Deg;
            }
            else if (s.Place == 3 && field.CloudClusterCount > 0)
            {
                DryCloudCluster cloud = field.CloudCluster(0);
                float y = s.Name.Contains("high") ? cloud.Top + 1800f : Math.Max(400f, cloud.Base - 500f);
                s.Camera = new Vector3(cloud.X - cloud.Radius * 0.9f, y, cloud.Z - cloud.Radius * 0.4f);
                s.Yaw = Mathf.Atan2(cloud.X - s.Camera.x, cloud.Z - s.Camera.z) * Mathf.Rad2Deg;
                s.Pitch = s.Name.Contains("high") ? -12f : 6f;
            }
            else if (s.Place == 4 && s.LookAtHero >= 0 && s.LookAtHero < field.SuperstructureCount)
            {
                Superstructure h = field.SuperstructureAt(s.LookAtHero);
                Vector3 side = new Vector3(Mathf.Sin(h.Heading), 0f, -Mathf.Cos(h.Heading));
                float distance = h.Kind == SuperstructureKind.ShelfLine ? 24000f : h.Kind == SuperstructureKind.Lenticulars ? 16000f : 35000f;
                s.Camera = new Vector3(h.X, h.Kind == SuperstructureKind.Lenticulars ? h.Top - 800f : 3500f, h.Z) + side * distance;
                s.Pitch = h.Kind == SuperstructureKind.Lenticulars ? 5f : 8f;
            }
            else if (s.Place == 5 && field.CellCount > 0)
            {
                StormCell cell = field.Cell(0);
                s.Camera = new Vector3(cell.X - cell.Radius * 5f, 4500f, cell.Z);
                s.Yaw = 90f;
                s.Pitch = 7f;
            }
            else if (s.Place == 6 && field.CloudClusterCount > 0)
            {
                DryCloudCluster cloud = field.CloudCluster(0);
                s.Camera = new Vector3(cloud.X - 10000f, cloud.Base + 650f, cloud.Z);
                s.Travel = new Vector3(20000f, 0f, 0f);
                s.Yaw = 90f;
            }
            else if (s.Place == 7 && field.CellCount > 0)
            {
                StormCell cell = field.Cell(0);
                s.Camera = new Vector3(cell.X, cell.Base + 650f, cell.Z);
                s.Travel = new Vector3(5000f, cell.Top + 2000f - s.Camera.y, 0f);
                s.Yaw = 90f;
                s.Pitch = 12f;
            }
            else if (s.Place == 8 && field.CellCount > 0)
            {
                StormCell cell = field.Cell(0);
                FrontState front = field.Front(0);
                s.Camera = new Vector3(cell.X + front.NormalX * 25000f, maps.Top + 1200f,
                    cell.Z + front.NormalZ * 25000f);
                s.Yaw = Mathf.Atan2(cell.X - s.Camera.x, cell.Z - s.Camera.z) * Mathf.Rad2Deg;
                s.Pitch = -Mathf.Atan2(s.Camera.y - (cell.Top - 500f), 25000f) * Mathf.Rad2Deg;
            }
            else if (s.Place == 9 && field.CellCount > 0)
            {
                StormCell cell = field.Cell(0);
                FrontState front = field.Front(0);
                s.Camera = new Vector3(cell.X + front.NormalX * 25000f, 900f,
                    cell.Z + front.NormalZ * 25000f);
                s.Yaw = Mathf.Atan2(cell.X - s.Camera.x, cell.Z - s.Camera.z) * Mathf.Rad2Deg;
                s.Pitch = 8f;
            }
            float yaw = s.Yaw;
            if (s.LookAtHero >= 0 && s.LookAtHero < field.SuperstructureCount)
            {
                Superstructure h = field.SuperstructureAt(s.LookAtHero);
                yaw = Mathf.Atan2(h.X - s.Camera.x, h.Z - s.Camera.z) * Mathf.Rad2Deg;
            }
            if (fixedPoses.TryGetValue(s.Name, out float[] pose))
            {
                s.Camera = new Vector3(pose[0], pose[1], pose[2]); yaw = pose[3]; s.Pitch = pose[4];
                s.Travel = new Vector3(pose[5], pose[6], pose[7]);
            }
            File.AppendAllText("scene-poses.csv", FormattableString.Invariant($"{s.Name},{s.Camera.x:R},{s.Camera.y:R},{s.Camera.z:R},{yaw:R},{s.Pitch:R},{s.Travel.x:R},{s.Travel.y:R},{s.Travel.z:R}\n"));
            bool moving = s.Travel.sqrMagnitude > 0f;
            int frames = moving ? Mathf.Clamp(Mathf.CeilToInt(s.Travel.magnitude / 250f * 30f), 60, 2400) : measuredFrames;
            WeatherPoint SampleDisplayed(float x, float z)
            {
                CloudBodies.MapWeights(x, z, 105000f, 315000f, out float nearWeight, out float farFade);
                if (farFade <= 0f) return default;
                WeatherPoint nearPoint = CloudBodies.SampleMap(maps.Near, maps.NearProfiles, CloudMaps.NearSize,
                    x / 210000f + .5f, z / 210000f + .5f);
                if (nearWeight >= 1f) return nearPoint;
                WeatherPoint outer = CloudBodies.SampleMap(maps.Far, maps.FarProfiles, CloudMaps.FarSize,
                    x / 630000f + .5f, z / 630000f + .5f);
                outer.BackgroundCover *= farFade; outer.FrontCover *= farFade; outer.CellShape *= farFade;
                return nearWeight <= 0f ? outer : CloudBodies.BlendMaps(outer, nearPoint, nearWeight);
            }
            var bodies = new CloudBodies(noiseBytes, n3, field.Params, field.PrevailingHeading, field.Split, 0f,
                field, null, true, SampleDisplayed);
            camera.transform.position = s.Camera;
            camera.transform.rotation = Quaternion.Euler(-s.Pitch, yaw, 0f);
            Vector3 sun = new Vector3(0.35f, 0.55f, -0.65f).normalized;

            uniforms.Settle(field);
            foreach (string mode in new[] { "old", "full", "half", "temporal" })
            {
                if (mode == "old" && old == null) continue;
                if (moving && mode != "temporal") continue;
                camera.transform.position = s.Camera;
                Material m = mode == "old" ? old : march;
                var frame = new CloudFrame
                {
                    WorldOffset = Vector3.zero,
                    CameraPosition = camera.transform.position,
                    CameraForward = camera.transform.forward,
                    FieldOfView = camera.fieldOfView,
                    PixelHeight = mode == "half" || mode == "temporal" ? Height / 2 : Height,
                    Bottom = maps.Bottom, Top = maps.Top,
                    HorizonCover = maps.HorizonCover,
                    SunDirection = sun,
                    SunColor = s.Name.Contains("night") ? new Color(.13f, .16f, .24f) : new Color(1.9f, 1.8f, 1.6f),
                    Ambient = s.Name.Contains("night") ? new Color(.022f, .029f, .045f) :
                        new Color(0.55f, 0.65f, 0.85f) * 0.45f + new Color(0.72f, 0.8f, 0.9f) * 0.3f + new Color(1.9f, 1.8f, 1.6f) * 0.05f,
                    Ground = new Color(0.25f, 0.27f, 0.22f) * 0.25f + new Color(0.72f, 0.8f, 0.9f) * 0.1f,
                    Fog = new Color(0.72f, 0.8f, 0.9f),
                    Extinction = 0.00004f,
                    DeltaTime = 0f,
                    RainVisualsEnabled = maps.RainMaximum > .02f &&
                        Environment.GetEnvironmentVariable("CLOUD_BENCH_NORAIN") != "1",
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
                Quaternion start = Quaternion.Euler(-s.Pitch, yaw, 0f);
                float inCloud = 0f;
                var routeLog = new StringBuilder("frame,x,y,z,density,inCloud\n");
                // One frame: the command buffer is re-recorded each time (the temporal targets
                // ping-pong), and the camera turns half a degree a frame, as in a gentle turn,
                // so the temporal mode shows its reprojection rather than a still image.
                void RenderFrame(int index)
                {
                    camera.transform.position = s.Camera + s.Travel * Mathf.Clamp01(index / (float)(frames - 1));
                    camera.transform.rotation = moving || staticView ? start : Quaternion.AngleAxis(index * 0.5f, Vector3.up) * start;
                    Vector3 position = camera.transform.position;
                    float density = bodies.Density(SampleDisplayed(position.x, position.z), position.x, position.y, position.z, 0f);
                    inCloud = Mathf.MoveTowards(inCloud, Mathf.Clamp01((density - 0.02f) * 5f), 1.5f / 30f);
                    uniforms.ApplyView(camera, camera.transform.position);
                    Matrix4x4 box = Matrix4x4.TRS(camera.transform.position, Quaternion.identity, Vector3.one * 1000f);
                    cmd.Clear();
                    if (mode == "half" || mode == "temporal")
                    {
                        lowRes.Record(cmd, m, composite, mode == "temporal");
                        cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                        cmd.DrawMesh(cube, box, composite, 0, 0);
                    }
                    else cmd.DrawMesh(cube, box, m, 0, 0);
                    camera.Render();
                    if (moving && index >= 0 && (index % 30 == 0 || index == frames - 1))
                    {
                        RenderTexture.active = target;
                        readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                        readback.Apply();
                        RenderTexture.active = null;
                        Directory.CreateDirectory(s.Name);
                        File.WriteAllBytes(s.Name + "/" + index.ToString("D4") + ".png", readback.EncodeToPNG());
                        routeLog.AppendLine(FormattableString.Invariant($"{index},{position.x:F1},{position.y:F1},{position.z:F1},{density:F4},{inCloud:F4}"));
                    }
                }
                lowRes.InvalidateHistory();
                for (int i = 0; i < settleFrames; i++) RenderFrame(i - settleFrames);
                Sync(target, readback);
                watch.Restart();
                for (int i = 0; i < frames; i++) RenderFrame(i);
                Sync(target, readback);
                double ms = watch.Elapsed.TotalMilliseconds / frames;
                if (moving) File.WriteAllText(s.Name + "/route.csv", routeLog.ToString());
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                readback.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(s.Name + "-" + mode + ".png", readback.EncodeToPNG());
                log.AppendLine(s.Name.PadRight(16) + mode.PadRight(5) + ms.ToString("0.00").PadLeft(8) + " ms/frame" +
                    "  (maps " + mapMs + " ms, heroes " + field.SuperstructureCount + (moving ? "; route includes PNG capture overhead" : "") + ")");
            }
            cmd.Clear();
            camera.transform.rotation = Quaternion.Euler(-s.Pitch, yaw, 0f);
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
