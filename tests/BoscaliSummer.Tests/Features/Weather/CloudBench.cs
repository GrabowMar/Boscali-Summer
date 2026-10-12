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
        public uint Seed = 90210u;
        public WeatherRegimeType State;
        /// <summary>Library sky to pin (0-46), or -1 for the class state.</summary>
        public int Sky = -1;
        /// <summary>A grazing view (pitch 1 degree) of a sky: rendered and saved, never measured.</summary>
        public bool Grazing;
        public byte Sets;
        public bool Anchor;
        public WeatherScenario? Preset;
        public byte Turn, Salt;
        public Vector3 Camera;
        public float Yaw, Pitch;
        public int LookAtHero = -1;
        /// <summary>0 fixed camera, 1 in the nearest storm cell, 2 beside its anvil, 3 at the nearest cumulus group, 4 set-piece portrait,
        /// 5 outside the nearest cell, 6 flythrough at a group, 7 flythrough of a cell, 8 and 9 the front beside a cell, 10 the sky-sheet
        /// framing, 11 looking along the tape axis (across it when the name says so).</summary>
        public int Place;
        /// <summary>Above 0: a one-keyframe tape of the scene's sky with this BaseVariation (the Humilis ledge check).</summary>
        public float BaseVariation;
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
        // CLOUD_BENCH_TAPE: "" the field's own one-keyframe tape, "texture" the same sky as a 4096-texel tape (the texture path), "ladder" every rung half-way to the next, "ramp" a ramped tape, "mix" the slab worst case.
        string tapeMode = Environment.GetEnvironmentVariable("CLOUD_BENCH_TAPE") ?? "";
        // CLOUD_BENCH_SKIES: the sky contact-sheet mode (see CloudBenchSkies).
        CloudBenchSkies skies = CloudBenchSkies.FromEnvironment();
        // CLOUD_BENCH_SKYCLASS=1 renders the same poses under the class state instead of the pin: the perf baseline of the sky run.
        bool skyClass = Environment.GetEnvironmentVariable("CLOUD_BENCH_SKYCLASS") == "1";
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

        // The deck seam metric (spec 7.3, CloudBenchSeam): its own render loop, run alone with -Only seam (skies 8, 15 and 29, or CLOUD_BENCH_SKIES).
        if (Array.Exists(filters, filter => filter == "seam"))
        {
            ground.SetActive(false);
            CloudBenchSeam.Run(log, skies != null ? skies.Skies : new[] { 7, 14, 28 }, camera, target, cmd, cube, march, noise, HalfExtent);
            return;
        }

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
            // Slice 3: a flat-base Humilis sky (sky 8) at the smallest and the largest base variation: the underside must show no ledges.
            new Scene { Name = "humilis-ledge-150", State = WeatherRegimeType.Fair, Sky = 7, BaseVariation = 150f, Place = 3 },
            new Scene { Name = "humilis-ledge-400", State = WeatherRegimeType.Fair, Sky = 7, BaseVariation = 400f, Place = 3 },
            // Cloud streets (sky 10) seen along the rows and across them.
            new Scene { Name = "streets-along", State = WeatherRegimeType.Fair, Sky = 9, Camera = new Vector3(0, 500, 0), Pitch = 8, Place = 11 },
            new Scene { Name = "streets-across", State = WeatherRegimeType.Fair, Sky = 9, Camera = new Vector3(0, 500, 0), Pitch = 8, Place = 11 },
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

        // Explicit opt-in: the all-map matrix must not lengthen the existing focused bench.
        // Nine sites cover center, edges and corners; high views reveal layout/composition
        // problems hidden by cloud-out and horizon portraits. The outward storm views also
        // exercise map/horizon continuation. Height is a render-fixture pose, not a claim
        // that the native aircraft can sustain flight at this altitude.
        if (Array.Exists(filters, filter => filter == "map" || filter.Contains("map-")))
            AddMapViews(scenes);
        if (skies != null)
        {
            // The sky sheets run alone: one pose under each selected sky, from 300 m, the horizon at 65 % of the frame height; a sky whose
            // cloud is a cumulus group or a storm cell (CloudBenchSkies.IsFeature) is framed from below at that feature instead (place 10).
            scenes.Clear();
            foreach (int sky in skies.Skies)
                scenes.Add(new Scene { Name = CloudBenchSkies.SceneName(sky), Sky = sky, State = SkyLibrary.ClassOf(sky),
                    Camera = new Vector3(0, 300, 0), Yaw = 30, Pitch = CloudBenchSkies.Pitch, Place = CloudBenchSkies.IsFeature(sky) ? 10 : 0 });
            // Grazing views (spec 10.3 extra scenes): the ripple rows of sky 18 (AcUndulatus) at the horizon, where the ripple LOD of 4.3 is new in
            // this slice, and the cirrus streaks of sky 2 edge-on (the high slab's longest chord). The same pose at a 1 degree pitch; saved, not measured.
            foreach (int sky in new[] { 17, 1 })
                if (skies.Includes(sky))
                    scenes.Add(new Scene { Name = CloudBenchSkies.SceneName(sky) + "-grazing", Sky = sky, State = SkyLibrary.ClassOf(sky),
                        Camera = new Vector3(0, 300, 0), Yaw = 30, Pitch = 1f, Grazing = true });
        }

        var uniforms = new CloudVolumeUniforms();
        foreach (Scene s in scenes)
        {
            if (filters.Length > 0 && !Array.Exists(filters, filter => s.Name.Contains(filter))) continue;
            // The key has no front turn: the three front-turn scenes render the unturned sky (slice 4b retires them).
            var key = new WeatherKey(s.Seed, 0f, false, (byte)s.State, s.Sets, s.Salt, s.Anchor, 0f, 25000f,
                skyPin: s.Sky >= 0 && !skyClass ? (byte)s.Sky : WeatherKey.None);
            if (s.Preset.HasValue) key = WeatherScenarios.Apply(s.Preset.Value, key, true, 0f, 0f, 0f, 1f);
            const float time = 900f;
            var field = new WeatherField();
            field.Build(key, time, HalfExtent, HalfExtent, 13f);
            TapeFixture fixture = TapeVariant(tapeMode, field, s.BaseVariation);
            if (fixture != null) field.UseFixtureTape(fixture.Sampler, fixture.Frame);
            var watch = Stopwatch.StartNew();
            CloudMaps maps = CloudMaps.Build(key, time, HalfExtent, HalfExtent, 13f, 105000f, 315000f, fixture);
            long mapMs = watch.ElapsedMilliseconds;
            long cookieMs = CookieMs(noiseBytes, n3, field);
            Texture2D near = Map(maps.Near, CloudMaps.NearSize), nearProfiles = Map(maps.NearProfiles, CloudMaps.NearSize);
            Texture2D far = Map(maps.Far, CloudMaps.FarSize), farProfiles = Map(maps.FarProfiles, CloudMaps.FarSize);
            Texture2D envelope = Map(maps.Envelope, CloudMaps.EnvelopeSize);
            envelope.filterMode = FilterMode.Point;

            // The lattice has no fixed first cell or group: the features near the origin are found, nearest first.
            FieldFeature cell = default, cloud = default;
            bool hasCell = s.Place != 0 && field.FindNearestCell(0f, 0f, out cell);
            bool hasCloud = s.Place != 0 && field.FindNearestCloud(0f, 0f, out cloud);
            if (s.Place == 1 && hasCell)
            {
                s.Camera = new Vector3(cell.X, (cell.Base + cell.Top) * 0.45f, cell.Z);
            }
            else if (s.Place == 2 && hasCell)
            {
                s.Camera = new Vector3(cell.X - cell.Radius * 2.2f, cell.Base + (cell.Top - cell.Base) * 0.82f, cell.Z);
                s.Yaw = Mathf.Atan2(cell.X - s.Camera.x, cell.Z - s.Camera.z) * Mathf.Rad2Deg;
            }
            else if (s.Place == 3 && hasCloud)
            {
                float y = s.Name.Contains("high") ? cloud.Top + 1800f : Math.Max(400f, cloud.Base - 500f);
                s.Camera = new Vector3(cloud.X - cloud.Radius * 0.9f, y, cloud.Z - cloud.Radius * 0.4f);
                s.Yaw = Mathf.Atan2(cloud.X - s.Camera.x, cloud.Z - s.Camera.z) * Mathf.Rad2Deg;
                s.Pitch = s.Name.Contains("high") ? -12f : 6f;
            }
            else if (s.Place == 10 && (hasCloud || hasCell))
            {
                // The sky sheets: a cumulus group seen from below at its edge, or - for a storm sky, or when there is no group - the
                // nearest storm cell.
                if (hasCell && (!hasCloud || s.State >= WeatherRegimeType.RainSquall))
                {
                    s.Camera = new Vector3(cell.X - cell.Radius * 5f, 600f, cell.Z);
                    s.Yaw = 90f;
                    s.Pitch = 8f;
                }
                else
                {
                    s.Camera = new Vector3(cloud.X - cloud.Radius * 0.9f, Math.Max(400f, cloud.Base - 500f), cloud.Z - cloud.Radius * 0.4f);
                    s.Yaw = Mathf.Atan2(cloud.X - s.Camera.x, cloud.Z - s.Camera.z) * Mathf.Rad2Deg;
                    s.Pitch = 6f;
                }
            }
            else if (s.Place == 11)
            {
                // Along the tape axis (up the wind, the direction cloud streets run), or across it when the name says so.
                s.Yaw = Mathf.Atan2(field.Frame.UX, field.Frame.UZ) * Mathf.Rad2Deg + (s.Name.Contains("across") ? 90f : 0f);
            }
            else if (s.Place == 4 && s.LookAtHero >= 0 && s.LookAtHero < field.SuperstructureCount)
            {
                Superstructure h = field.SuperstructureAt(s.LookAtHero);
                Vector3 side = new Vector3(Mathf.Sin(h.Heading), 0f, -Mathf.Cos(h.Heading));
                float distance = h.Kind == SuperstructureKind.ShelfLine ? 24000f : h.Kind == SuperstructureKind.Lenticulars ? 16000f : 35000f;
                s.Camera = new Vector3(h.X, h.Kind == SuperstructureKind.Lenticulars ? h.Top - 800f : 3500f, h.Z) + side * distance;
                s.Pitch = h.Kind == SuperstructureKind.Lenticulars ? 5f : 8f;
            }
            else if (s.Place == 5 && hasCell)
            {
                s.Camera = new Vector3(cell.X - cell.Radius * 5f, 4500f, cell.Z);
                s.Yaw = 90f;
                s.Pitch = 7f;
            }
            else if (s.Place == 6 && hasCloud)
            {
                s.Camera = new Vector3(cloud.X - 10000f, cloud.Base + 650f, cloud.Z);
                s.Travel = new Vector3(20000f, 0f, 0f);
                s.Yaw = 90f;
            }
            else if (s.Place == 7 && hasCell)
            {
                s.Camera = new Vector3(cell.X, cell.Base + 650f, cell.Z);
                s.Travel = new Vector3(5000f, cell.Top + 2000f - s.Camera.y, 0f);
                s.Yaw = 90f;
                s.Pitch = 12f;
            }
            else if (s.Place == 8 && hasCell)
            {
                CloudVolumeUniforms.FarBounds(field, new Vector3(cell.X + 25000f, 0f, cell.Z), out _, out float boundsTop);
                s.Camera = new Vector3(cell.X + 25000f, boundsTop + 1200f, cell.Z);
                s.Yaw = Mathf.Atan2(cell.X - s.Camera.x, cell.Z - s.Camera.z) * Mathf.Rad2Deg;
                s.Pitch = -Mathf.Atan2(s.Camera.y - (cell.Top - 500f), 25000f) * Mathf.Rad2Deg;
            }
            else if (s.Place == 9 && hasCell)
            {
                s.Camera = new Vector3(cell.X + 25000f, 900f, cell.Z);
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
                outer.BackgroundCover *= farFade; outer.CellShape *= farFade;
                return nearWeight <= 0f ? outer : CloudBodies.BlendMaps(outer, nearPoint, nearWeight);
            }
            var bodies = new CloudBodies(noiseBytes, n3, field.Tape, field.Frame, field.PrevailingHeading, 0f,
                field, null, true, SampleDisplayed);
            camera.transform.position = s.Camera;
            camera.transform.rotation = Quaternion.Euler(-s.Pitch, yaw, 0f);
            Vector3 sun = new Vector3(0.35f, 0.55f, -0.65f).normalized;

            uniforms.Settle(field);
            foreach (string mode in skies != null ? new[] { "temporal" } : new[] { "old", "full", "half", "temporal" })
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
                    CloudVolumeUniforms.FarBounds(field, s.Camera, out float boundsBottom, out float boundsTop);
                    m.SetVector("_CloudAltitudeBounds", new Vector2(Mathf.Min(boundsBottom, 350f), Mathf.Max(boundsTop, top)));
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
                    float density = bodies.Density(SampleDisplayed(position.x, position.z), position.x, position.y, position.z);
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
                if (skies != null && !s.Grazing) skies.Capture(s.Sky, readback);
                log.AppendLine(s.Name.PadRight(16) + mode.PadRight(5) + ms.ToString("0.00").PadLeft(8) + " ms/frame" +
                    "  (maps " + mapMs + " ms, cookie " + cookieMs + " ms, heroes " + field.SuperstructureCount + (moving ? "; route includes PNG capture overhead" : "") + ")");
            }
            cmd.Clear();
            camera.transform.rotation = Quaternion.Euler(-s.Pitch, yaw, 0f);
            camera.Render();
            Sync(target, readback);
            if (skies != null && !s.Grazing)
            {
                // The same pose without clouds: the metrics compare the two.
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                readback.Apply();
                RenderTexture.active = null;
                skies.Finish(s.Sky, readback, camera.fieldOfView / Height, log);
            }
            watch.Restart();
            for (int i = 0; i < Frames; i++) camera.Render();
            Sync(target, readback);
            log.AppendLine(s.Name.PadRight(16) + "none " + (watch.Elapsed.TotalMilliseconds / Frames).ToString("0.00").PadLeft(8) + " ms/frame");
            UnityEngine.Object.Destroy(near); UnityEngine.Object.Destroy(nearProfiles);
            UnityEngine.Object.Destroy(far); UnityEngine.Object.Destroy(farProfiles); UnityEngine.Object.Destroy(envelope);
        }
    }

    /// <summary>The shadow cookie build's CPU cost (WeatherCloudShadows.Queue; spec 10.3 gates "cookie worker time"): the same loop on every
    /// fourth pixel of both axes, 14 density taps each, a fixed 45-degree sun, scaled by 16. A mirror of that loop; keep it in step.</summary>
    private static long CookieMs(byte[] noise, int size, WeatherField field)
    {
        const int cookie = 256, stride = 4, samples = 14;
        const float span = 48000f;
        var body = new CloudBodies(noise, size, field.Tape, field.Frame, field.PrevailingHeading, 0f, field);
        Vector3 direction = new Vector3(0.35f, -0.72f, 0.6f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized, up = Vector3.Cross(direction, right);
        float baseY = field.RowAt(0f, 0f).CloudBase;
        float lower = Mathf.Max(0f, baseY - 800f), upper = 12000f, dy = (upper - lower) / samples;
        float rayStep = dy / Mathf.Max(0.06f, -direction.y), sink = 0f;
        var timer = Stopwatch.StartNew();
        for (int y = stride / 2; y < cookie; y += stride)
            for (int x = stride / 2; x < cookie; x += stride)
            {
                float u = ((x + 0.5f) / cookie - 0.5f) * span, v = ((y + 0.5f) / cookie - 0.5f) * span;
                float px = right.x * u + up.x * v, py = right.y * u + up.y * v, pz = right.z * u + up.z * v;
                float deckT = (baseY + 700f - py) / direction.y;
                WeatherPoint p = field.Sample(px + direction.x * deckT, pz + direction.z * deckT);
                float depth = 0f;
                for (int s = 0; s < samples && depth < 4f; s++)
                {
                    float altitude = lower + (s + 0.5f) * dy;
                    float t = (altitude - py) / direction.y;
                    depth += body.Density(p, px + direction.x * t, altitude, pz + direction.z * t) * rayStep * 0.0021f;
                }
                sink += depth;
            }
        if (sink < 0f) UnityEngine.Debug.Log("cookie sink " + sink); // keeps the loop live
        return (long)(timer.Elapsed.TotalMilliseconds * ((cookie / stride) * (cookie / stride) / 4096.0) * 16.0);
    }

    /// <summary>The tape the bench renders with. "" is the field's own one-keyframe tape (null here: its low genus rides in uniforms).
    /// "texture" is the same sky as a 4096-texel tape with a 10 km meander, so the texture path runs. "ladder" is the same sky with
    /// every rung half-way to the next, so the shader blends two rungs at every sample (the worst case of the feature ladder).
    /// "ramp" is the scene's own sky joined to the same sky a rung or more away by a 12 km ramp through the origin, bent by a 10 km meander.
    /// "mix" is the worst-case morphology mix: both thin layers carry all four morphologies at weight 0.25.</summary>
    private static TapeFixture TapeVariant(string mode, WeatherField field, float baseVariation = 0f)
    {
        // The lattice reads its genus from the tape, so every fixture keeps the field's own.
        LowGenus genus = field.Tape.GenusAt(0f);
        if (baseVariation > 0f && mode == "")
        {
            SkyParams sky = field.RowAt(0f, 0f);
            sky.BaseVariation = baseVariation;
            var tape = new BakedTape(1);
            tape.SetConstant(sky, genus);
            return new TapeFixture(tape, field.Frame);
        }
        if (mode == "texture")
        {
            // B': the sky B renders, as a 4096-texel tape of that one sky. IsConstant is false, so BindTape sends no uniforms and the shader
            // fetches and lerps three low-genus rows per sample and evaluates TapeS with its sines. The origin sits inside the tape (s about
            // 1500 km of 3000), so both texels of a lerp are fetched, as on a real tape.
            SkyParams sky = field.RowAt(0f, 0f);
            return new TapeFixture(BakedTape.TwoKeyframes(sky, sky, 1500000f, 12000f, genus, genus), new TapeFrame(90f, 1500000f, 10000f, 80000f, 0.7f));
        }
        if (mode == "ladder")
        {
            SkyParams sky = field.RowAt(0f, 0f);
            sky.LobeRung += 0.5f;
            sky.Mid.PeriodRung += 0.5f;
            sky.High.PeriodRung += 0.5f;
            var tape = new BakedTape(1);
            tape.SetConstant(sky, genus);
            return new TapeFixture(tape, field.Frame);
        }
        if (mode == "ramp")
        {
            // The scene's own sky on one side and the same sky a rung or more away on the other (two lobe rungs, three period rungs,
            // a 15 % higher base), joined by a 12 km ramp through the origin and bent by a 10 km meander.
            SkyParams left = field.RowAt(0f, 0f), right = left;
            right.LobeRung += 2f;
            right.Mid.PeriodRung += 3f;
            right.High.PeriodRung += 3f;
            right.CloudBase *= 1.15f;
            // The thin layers' aspect and ripple wavelength change across the ramp too (the bake holds them at the nearer side, spec 6).
            right.Mid.Aspect = left.Mid.Aspect * 0.6f; right.High.Aspect = left.High.Aspect * 0.6f;
            right.Mid.RippleWavelength = left.Mid.RippleWavelength * 0.7f; right.High.RippleWavelength = left.High.RippleWavelength * 0.7f;
            return new TapeFixture(BakedTape.TwoKeyframes(left, right, 1500000f, 12000f, genus, genus), new TapeFrame(90f, 1500000f, 10000f, 80000f, 0.7f));
        }
        if (mode == "mix")
        {
            // The worst-case morphology mix: both thin layers carry all four morphologies at weight 0.25 and a curl.
            SkyParams sky = field.RowAt(0f, 0f);
            sky.Mid = SkyArchetypes.Layer(LayerKind.AcFloccus).ToLayer(0.6f);
            sky.High = SkyArchetypes.Layer(LayerKind.CirrusSpissatus).ToLayer(0.5f);
            for (int i = 0; i < 2; i++)
            {
                SkyLayer layer = sky.Layer(i);
                layer.Cells = layer.Ripples = layer.Streaks = layer.Veil = 0.25f;
                layer.Curl = 0.8f; layer.Aspect = 3f; layer.RippleWavelength = 900f;
                if (i == 0) sky.Mid = layer; else sky.High = layer;
            }
            SkyDerived.Apply(ref sky);
            var tape = new BakedTape(1);
            tape.SetConstant(sky, genus);
            return new TapeFixture(tape, field.Frame);
        }
        return null;
    }

    private static void AddMapViews(List<Scene> scenes)
    {
        string[] sites = { "southwest", "south", "southeast", "west", "center", "east", "northwest", "north", "northeast" };
        foreach (WeatherRegimeType state in new[] { WeatherRegimeType.Scattered, WeatherRegimeType.RainSquall, WeatherRegimeType.Storm })
        for (int z = -1; z <= 1; z++)
        for (int x = -1; x <= 1; x++)
        {
            string site = sites[(z + 1) * 3 + x + 1];
            string prefix = "map-" + state.ToString().ToLowerInvariant() + "-" + site;
            float yaw = x == 0 && z == 0 ? 30f : Mathf.Atan2(-x, -z) * Mathf.Rad2Deg;
            float cameraX = x * HalfExtent, cameraZ = z * HalfExtent;
            scenes.Add(new Scene { Name = prefix + "-ground", State = state,
                Camera = new Vector3(cameraX, 600f, cameraZ), Yaw = yaw, Pitch = 5f });
            scenes.Add(new Scene { Name = prefix + "-ceiling", State = state,
                Camera = new Vector3(cameraX, 17000f, cameraZ), Yaw = yaw, Pitch = x == 0 && z == 0 ? -65f : -35f });
            if (state == WeatherRegimeType.Storm && (x != 0 || z != 0))
                scenes.Add(new Scene { Name = prefix + "-outward-ceiling", State = state,
                    Camera = new Vector3(cameraX, 17000f, cameraZ), Yaw = yaw + 180f, Pitch = -5f });
        }
        // An independently seeded opposite-corner hero view exercises the fixed far cutoff.
        scenes.Add(new Scene { Name = "map-hero-range-seed-6", Seed = 6u, State = WeatherRegimeType.Storm,
            Camera = new Vector3(-HalfExtent, 17000f, -HalfExtent), Pitch = -3f, LookAtHero = 2 });
        scenes.Add(new Scene { Name = "map-storm-center-mid-layer", State = WeatherRegimeType.Storm,
            Camera = new Vector3(0f, 6000f, 0f), Yaw = 30f, Pitch = -20f });
        scenes.Add(new Scene { Name = "map-storm-center-high35km", State = WeatherRegimeType.Storm,
            Camera = new Vector3(0f, 35000f, 0f), Yaw = 30f, Pitch = -65f });
        scenes.Add(new Scene { Name = "map-storm-center-high45km", State = WeatherRegimeType.Storm,
            Camera = new Vector3(0f, 45000f, 0f), Yaw = 30f, Pitch = -65f });
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
