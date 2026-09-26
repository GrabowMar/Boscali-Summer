#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BoscaliSummer.Features.Weather.Visuals;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Standalone native-mesh and shader fixture; not a Nuclear Option camera/multiplayer test.
public sealed class WeatherUnityCheck : MonoBehaviour
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++;
    }

#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            Shader shader = Resources.Load<Shader>("CanopyRain");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Shader compile failed");
            Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("TerrainRain")), "Terrain shader compile failed");
            AssetDatabase.CreateAsset(new Material(Shader.Find("Particles/Standard Unlit")), "Assets/Resources/RainFallback.mat");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("WeatherCheck").AddComponent<WeatherUnityCheck>();
            EditorSceneManager.SaveScene(scene, "Assets/check.unity");
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/check.unity" }, "Player/WeatherCheck.exe",
                BuildTarget.StandaloneWindows64, BuildOptions.Development);
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
        try
        {
            Run();
            File.WriteAllText("result.txt", "PASS: " + assertions + " standalone weather assertions");
            Application.Quit(0);
        }
        catch (Exception e) { File.WriteAllText("result.txt", e.ToString()); Debug.LogException(e); Application.Quit(1); }
    }

    private static void Run()
    {
        var clip = (AudioClip)typeof(BoscaliSummer.Features.Weather.Audio.ProceduralRainAudio)
            .GetMethod("SynthesizeCanopyPatterClip", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { "RainPreview", 24f, 44100 });
        var samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        float peak = 0f;
        foreach (float sample in samples)
        {
            if (float.IsNaN(sample) || float.IsInfinity(sample)) throw new Exception("Invalid rain audio sample");
            peak = Mathf.Max(peak, Mathf.Abs(sample));
        }
        Check(peak > 0.01f && peak < 0.8f, "Rain taps must have headroom without clipping");
        double energy = 0, sharpness = 0, stereo = 0;
        for (int i = 2; i < samples.Length; i += 2)
        {
            energy += samples[i] * samples[i];
            sharpness += Math.Pow(samples[i] - samples[i - 2], 2);
            stereo += Math.Pow(samples[i] - samples[i + 1], 2);
        }
        Check(energy > 0.001 && sharpness / energy < 0.15, "Patter must remain softly filtered, not sharp clicks");
        Check(stereo / energy > 0.02 && stereo / energy < 1, "Patter needs gentle stereo separation");
        Check(Math.Abs(samples[0] - samples[samples.Length - 2]) < 0.01f &&
            Math.Abs(samples[1] - samples[samples.Length - 1]) < 0.01f, "Stereo loop seam must not pop");
        using (var wav = new BinaryWriter(File.Create("rain-patter-preview.wav")))
        {
            wav.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); wav.Write(36 + samples.Length * 2);
            wav.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); wav.Write(16);
            wav.Write((short)1); wav.Write((short)2); wav.Write(44100); wav.Write(44100*4);
            wav.Write((short)4); wav.Write((short)16);
            wav.Write(System.Text.Encoding.ASCII.GetBytes("data")); wav.Write(samples.Length * 2);
            foreach (float sample in samples) wav.Write((short)(sample * 32767));
        }
        UnityEngine.Object.Destroy(clip);
        Shader shader = Resources.Load<Shader>("CanopyRain");
        Check(shader != null && shader.isSupported, "Rain shader unsupported in player");
        var opaque = new Material(Resources.Load<Shader>("Fixture")) { name = "Paint", renderQueue = 2000 };
        var glass = new Material(opaque) { name = "WindowGlass", renderQueue = 3000 };
        var hud = new Material(glass) { name = "HUD_Glass" };
        var root = new GameObject("Aircraft");
        var node = new GameObject("Canopy");
        node.transform.SetParent(root.transform, false);
        Mesh mesh = Quad();
        mesh.subMeshCount = 3;
        for (int i = 0; i < 3; i++) mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, i);
        mesh.UploadMeshData(true);
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = node.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { opaque, hud, glass };
        Check(!mesh.isReadable, "Fixture must exercise non-readable native mesh behavior");
        CanopyGlassResolver.ResetForScene();
        var surfaces = CanopyGlassResolver.Resolve(root.transform, new Vector3(0,0,-1));
        Check(surfaces.Count == 1 && surfaces[0].Submesh == 2, "Select glass slot beyond first two; reject frame/HUD");
        Check(surfaces[0].Mesh == mesh, "Reuse native mesh without cloning");

        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0,0,-3);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.3f,0.3f,0.3f);
        camera.orthographic = true;
        camera.orthographicSize = 0.65f;
        var dressing = new CanopyShaderDressing();
        typeof(CanopyShaderDressing).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(dressing, new Material(shader));
        Check(dressing.Draw(surfaces, camera, 1f, Vector3.down, 0f, 1f), "Draw non-readable glass");
        Check(renderer.sharedMaterials[0] == opaque && renderer.sharedMaterials[2] == glass,
            "Overlay must never replace original materials");
        renderer.enabled = false;
        Check(!dressing.Draw(surfaces, camera, 1f, Vector3.down, 0f, 1f), "Hidden canopy must not draw");
        renderer.enabled = true;
        dressing.Detach(); dressing.Detach();
        Check(renderer.sharedMaterials[2] == glass, "Idempotent teardown preserves glass");

        var dropsObject = new GameObject("Fallback");
        var drops = dropsObject.AddComponent<CanopyDropletEmitter>();
        drops.Initialize();
        drops.UpdateDrops(surfaces[0], 1f, 0f);
        Check(!drops.IsEmitting, "Unreadable mesh fallback must fail closed");

        for (int i = 0; i < 16; i++)
        {
            var pane = new GameObject("Windshield" + i);
            pane.transform.SetParent(root.transform, false);
            pane.AddComponent<MeshFilter>().sharedMesh = mesh;
            pane.AddComponent<MeshRenderer>().sharedMaterial = glass;
        }
        CanopyGlassResolver.ResetForScene();
        Check(CanopyGlassResolver.Resolve(root.transform, Vector3.zero).Count == CanopyGlassResolver.MaxSurfaces,
            "Multi-pane discovery must obey hard cap");
        CanopyGlassResolver.ResetForScene();
        Check(CanopyGlassResolver.Resolve(root.transform, Vector3.one * 100f).Count == 0,
            "Remote geometry must not count as cockpit glass");

        // Native cockpit parts are detached roots, and their interior glass is layer 3.
        var body = new GameObject("DetachedAircraftBody");
        var cockpit = new GameObject("DetachedCockpit");
        var interior = new GameObject("canopy_int");
        interior.transform.SetParent(cockpit.transform, false);
        interior.layer = 3;
        interior.AddComponent<MeshFilter>().sharedMesh = mesh;
        var interiorRenderer = interior.AddComponent<MeshRenderer>();
        interiorRenderer.sharedMaterials = new[] { opaque, hud, glass };
        Check(CanopyGlassResolver.Resolve(body.transform, Vector3.zero, 1 << 3).Count == 0,
            "Detached cockpit cannot be discovered from aircraft body");
        var interiorSurfaces = CanopyGlassResolver.Resolve(cockpit.transform, Vector3.zero, 1 << 3);
        Check(interiorSurfaces.Count == 1, "Discover detached interior glass through cockpit camera mask");
        typeof(CanopyShaderDressing).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(dressing, new Material(shader));
        camera.cullingMask = 1;
        Check(!dressing.Draw(interiorSurfaces, camera, 1f, Vector3.down, 0f, 1f),
            "World camera cannot draw interior glass");
        camera.cullingMask = 1 << 3;
        Check(dressing.Draw(interiorSurfaces, camera, 1f, Vector3.down, 0f, 1f),
            "Cockpit camera draws GPU-only interior glass");
        Check(CanopyGlassResolver.Resolve(cockpit.transform, Vector3.zero, 1).Count == 0,
            "Camera mask changes invalidate cached discovery");
        dressing.Detach(); cockpit.SetActive(false); camera.cullingMask = -1;

        // Render the actual shader, checking sparse coverage and a completely clear dry state.
        root.SetActive(false);
        camera.enabled = false;
        camera = UnityEngine.Object.Instantiate(camera);
        camera.enabled = true;
        var sheet = new GameObject("RainShaderFixture");
        sheet.AddComponent<MeshFilter>().sharedMesh = Quad();
        Material rain = new Material(shader);
        var dMask = Resources.Load<Texture2D>("droplets_mask");
        if (dMask != null) rain.SetTexture("_DropletMask", dMask);
        var rMask = Resources.Load<Texture2D>("rivulets_mask");
        if (rMask != null) rain.SetTexture("_RivuletMask", rMask);
        sheet.AddComponent<MeshRenderer>().sharedMaterial = rain;
        rain.SetFloat("_LightLevel", 1f);
        var texture = new RenderTexture(256,256,24);
        camera.targetTexture = texture;
        rain.SetFloat("_Intensity", 0f);
        Color32[] dry = Capture(camera, texture, "dry.png");
        rain.SetFloat("_Intensity", 1f);
        Color32[] wet = Capture(camera, texture, "wet.png");
        int changed = 0;
        for (int i = 0; i < dry.Length; i++)
            if (Math.Abs(dry[i].r-wet[i].r) + Math.Abs(dry[i].g-wet[i].g) + Math.Abs(dry[i].b-wet[i].b) > 6) changed++;
        // At 256px over 1.3 m, physical 3-6 mm drops are subpixel: this render guards
        // against a full-pane veil only. Drop visibility is asserted at close-up scale
        // below, where drops span multiple pixels; rewarding subpixel pixels here would
        // reward the aliasing glitter the bead fade exists to remove.
        Check(changed < dry.Length / 2, "Wet shader must not paint a full-pane veil: " + changed);
        // Close glass at a useful inspection resolution; check motion, fast airflow and drying.
        texture.Release();
        texture = new RenderTexture(1024,1024,24);
        camera.targetTexture = texture;
        camera.orthographicSize = 0.16f;
        Color32[] close = Capture(camera, texture, "canopy-close.png");
        rain.SetFloat("_FlowPhase", 4.7f);
        Color32[] moved = Capture(camera, texture, "canopy-later.png");
        int motion = 0, opaquePixels = 0;
        for (int i = 0; i < close.Length; i++)
        {
            if (Math.Abs(close[i].r - moved[i].r) > 2) motion++;
            if (Math.Abs(close[i].r - dry[0].r) > 40) opaquePixels++;
        }
        Check(motion > 100, "Canopy water must evolve over time");
        Check(opaquePixels == 0, "Fallback beads must not become opaque grey blobs");
        rain.SetFloat("_Speed", 1f);
        Capture(camera, texture, "canopy-fast.png");
        rain.SetFloat("_Intensity", 0f);
        Color32[] dried = Capture(camera, texture, "canopy-dried.png");
        Check(Array.TrueForAll(dried, pixel => pixel.Equals(dried[0])), "Dry glass must leave no residual filter");
        camera.orthographicSize = 0.65f;
        texture.Release();
        texture = new RenderTexture(256,256,24);
        camera.targetTexture = texture;

        // --- 3-pane windscreen rig: authored UVs vs atlas slivers vs planar projection.
        // Native windscreens ship split into angled panes with authored UVs of unknown
        // span; the single unmapped quad above cannot show pane seams or per-pane mismatch.
        sheet.SetActive(false);
        var paneMetrics = new System.Text.StringBuilder();
        paneMetrics.AppendLine("wet256 changed=" + changed + " (no-veil guard only)");
        int closeVisible = 0;
        for (int i = 0; i < close.Length; i++)
            if (Math.Abs(close[i].r - dried[i].r) + Math.Abs(close[i].g - dried[i].g) + Math.Abs(close[i].b - dried[i].b) > 6) closeVisible++;
        paneMetrics.AppendLine("close1024 visible=" + closeVisible + " (drop visibility at scale)");
        Check(closeVisible > 20000, "Drops must stay visible at close-up scale: " + closeVisible);
        RenderPaneRig(camera, texture, rain, PaneUvMode.Authored01, "wet-3pane-uv01.png", paneMetrics);
        RenderPaneRig(camera, texture, rain, PaneUvMode.AtlasSliver, "wet-3pane-atlas.png", paneMetrics);
        RenderPaneRig(camera, texture, rain, PaneUvMode.Planar, "wet-3pane-planar.png", paneMetrics);
        rain.SetVector("_Tiling", new Vector2(2f, 2f));
        RenderPaneRig(camera, texture, rain, PaneUvMode.Authored01, "wet-3pane-tile2.png", paneMetrics);
        rain.SetVector("_Tiling", new Vector2(3f, 3f));
        RenderCloseChurn(camera, rain, paneMetrics);
        RenderRefractionSweep(camera, rain, paneMetrics);
        File.WriteAllText("shimmer.txt", paneMetrics.ToString());
        var terrainMap = new GameObject("Map");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.SetParent(terrainMap.transform, false);
        var terrainMaterial = new Material(Resources.Load<Shader>("TerrainFixture"));
        ground.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
        ground.GetComponent<MeshFilter>().sharedMesh.UploadMeshData(true);
        camera.transform.position = new Vector3(0,3,0);
        camera.transform.rotation = Quaternion.Euler(90,0,0);
        var terrain = new TerrainRainDressing();
        terrain.Update(terrainMap.transform,camera,0f,0f);
        Check(terrain.SurfaceCount == 1, "Native terrain shader discovery");
        var terrainOverlay = new Material(Resources.Load<Shader>("TerrainRain"));
        typeof(TerrainRainDressing).GetField("material",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(terrain,terrainOverlay);
        Color32[] dryGround = Capture(camera,texture,"terrain-dry.png");
        terrain.Update(terrainMap.transform,camera,1f,35f);
        Check(terrain.Wetness == 1f, "Ground becomes wet gradually");
        Color32[] wetGround = Capture(camera,texture,"terrain-wet.png");
        int center = 128 * 256 + 128;
        Check(wetGround[center].r < dryGround[center].r - 3 && wetGround[center].r > dryGround[center].r * 0.75f,
            "Terrain shader adds restrained darkening: " + dryGround[center].r + " -> " + wetGround[center].r);
        terrain.Update(terrainMap.transform,camera,0f,90f);
        Check(Math.Abs(terrain.Wetness - 0.5f) < 0.001f, "Ground retains water after rain stops");
        var terrainProperties = (MaterialPropertyBlock)typeof(TerrainRainDressing)
            .GetField("properties", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(terrain);
        Check(terrainProperties.GetFloat("_Rain") == 0f && terrainProperties.GetFloat("_Wetness") > 0f,
            "Impact rings stop when rain stops, while ground remains damp");
        terrain.Reset(); terrain.Reset();
        Check(terrain.SurfaceCount == 0 && terrain.Wetness == 0f && ground.GetComponent<MeshRenderer>().sharedMaterial == terrainMaterial,
            "Terrain reset preserves original material and releases cached surfaces");
        terrainMap.SetActive(false);

        var streaks = new GameObject("Streaks").AddComponent<ProceduralRainEmitter>();
        streaks.Initialize(camera);
        streaks.UpdateRain(Vector3.forward * 250f, Vector3.zero, 1f, camera);
        var ps = streaks.GetComponent<ParticleSystem>();
        Check(ps.main.simulationSpace == ParticleSystemSimulationSpace.Custom && ps.main.maxParticles == 1000,
            "Relative rain needs translating simulation frame and fixed budget");
    }

    private enum PaneUvMode { Authored01, AtlasSliver, Planar }

    // Builds a center pane plus two side panes angled 35 degrees (a wrapped
    // windscreen seen from the cockpit), renders it wet at two adjacent flow phases,
    // and records frame-to-frame churn plus per-pane brightness spread. Dry glass must
    // leave no residue on any pane.
    private static void RenderPaneRig(Camera camera, RenderTexture target, Material rain,
        PaneUvMode mode, string path, System.Text.StringBuilder metrics)
    {
        float savedSize = camera.orthographicSize;
        camera.orthographicSize = 1.0f;
        var rig = new GameObject("PaneRig-" + mode);
        try
        {
            AddPane(rig.transform, "Center", new Vector3(0f, 0f, 0f), 0f, 1.0f, mode, rain);
            AddPane(rig.transform, "Left", new Vector3(-0.60f, 0f, -0.12f), 35f, 0.7f, mode, rain);
            AddPane(rig.transform, "Right", new Vector3(0.60f, 0f, -0.12f), -35f, 0.7f, mode, rain);
            rain.SetFloat("_Speed", 0f);
            rain.SetFloat("_Intensity", 1f);
            rain.SetFloat("_FlowPhase", 4.7f);
            Color32[] wet = Capture(camera, target, path);
            rain.SetFloat("_FlowPhase", 4.72f);
            Color32[] next = Capture(camera, target, path.Replace("wet-3pane", "churn-3pane"));
            int churn = 0;
            double abs = 0;
            double[] third = new double[3];
            int[] count = new int[3];
            for (int i = 0; i < wet.Length; i++)
            {
                int dr = Math.Abs(wet[i].r - next[i].r);
                int dg = Math.Abs(wet[i].g - next[i].g);
                int db = Math.Abs(wet[i].b - next[i].b);
                if (dr + dg + db > 24) churn++;
                abs += dr + dg + db;
                int slot = (i % target.width) * 3 / target.width;
                third[slot] += wet[i].r + wet[i].g + wet[i].b;
                count[slot]++;
            }
            double spread = Math.Max(Math.Abs(third[0] / count[0] - third[1] / count[1]),
                Math.Abs(third[2] / count[2] - third[1] / count[1]));
            metrics.AppendLine(string.Format("{0}: churn={1} meanAbs={2:F3} paneSpread={3:F2}",
                path, churn, abs / wet.Length, spread));
            rain.SetFloat("_Intensity", 0f);
            Color32[] dried = Capture(camera, target, "dry-" + path);
            Check(Array.TrueForAll(dried, pixel => pixel.Equals(dried[0])),
                "Dry 3-pane glass (" + mode + ") must leave no residual filter");
            rain.SetFloat("_Intensity", 1f);
        }
        finally
        {
            camera.orthographicSize = savedSize;
            UnityEngine.Object.Destroy(rig);
        }
    }

    // Close-up frame-to-frame churn at the left pane seam, with the imported mask
    // sampling (mip bias / aniso from the .meta files) versus neutral sampling.
    // A high churn count under imported settings that collapses under neutral
    // settings identifies texture aliasing as the shimmer driver.
    private static void RenderCloseChurn(Camera camera, Material rain, System.Text.StringBuilder metrics)
    {
        var savedPos = camera.transform.position;
        float savedSize = camera.orthographicSize;
        RenderTexture savedTarget = camera.targetTexture;
        var big = new RenderTexture(1024, 1024, 24);
        var rig = new GameObject("ChurnRig");
        try
        {
            AddPane(rig.transform, "Center", new Vector3(0f, 0f, 0f), 0f, 1.0f, PaneUvMode.Authored01, rain);
            AddPane(rig.transform, "Left", new Vector3(-0.60f, 0f, -0.12f), 35f, 0.7f, PaneUvMode.Authored01, rain);
            AddPane(rig.transform, "Right", new Vector3(0.60f, 0f, -0.12f), -35f, 0.7f, PaneUvMode.Authored01, rain);
            camera.transform.position = new Vector3(-0.45f, 0f, -3f);
            camera.orthographicSize = 0.16f;
            camera.targetTexture = big;
            rain.SetFloat("_Speed", 0f);
            rain.SetFloat("_Intensity", 1f);
            var dMask = Resources.Load<Texture2D>("droplets_mask");
            var rMask = Resources.Load<Texture2D>("rivulets_mask");
            float savedBiasD = dMask != null ? dMask.mipMapBias : 0f;
            float savedBiasR = rMask != null ? rMask.mipMapBias : 0f;
            int savedAnisoD = dMask != null ? dMask.anisoLevel : 1;
            int savedAnisoR = rMask != null ? rMask.anisoLevel : 1;
            metrics.AppendLine(string.Format("imported sampling: dropletBias={0} rivuletBias={1} dropletAniso={2} rivuletAniso={3}",
                savedBiasD, savedBiasR, savedAnisoD, savedAnisoR));
            rain.SetFloat("_FlowPhase", 4.70f);
            Color32[] sharpA = Capture(camera, big, "close-sharp-a.png");
            rain.SetFloat("_FlowPhase", 4.72f);
            Color32[] sharpB = Capture(camera, big, "close-sharp-b.png");
            Check(savedAnisoD >= 4 && savedAnisoR >= 4, "Mask aniso must tame grazing side panes");
            int sharpChurn = Churn(sharpA, sharpB);
            metrics.AppendLine(string.Format("imported churn={0} meanAbs={1:F3}",
                sharpChurn, MeanAbs(sharpA, sharpB)));
            Check(sharpChurn < 400, "Soft strike cycle must not pop frame to frame: " + sharpChurn);
            rain.SetVector("_Flow", new Vector4(1f, -1f, 0f, 0f));
            rain.SetFloat("_FlowPhase", 4.70f);
            Capture(camera, big, "close-flowside.png");
            rain.SetVector("_Flow", new Vector4(0f, -1f, 0f, 0f));
        }
        finally
        {
            camera.transform.position = savedPos;
            camera.orthographicSize = savedSize;
            camera.targetTexture = savedTarget;
            big.Release();
            UnityEngine.Object.Destroy(big);
            UnityEngine.Object.Destroy(rig);
        }
    }

    // Refraction shimmer sweep: the fixture has no URP opaque texture, so a
    // high-contrast checker stands in for the moving cockpit/terrain scene the
    // drops refract in-game. Measures frame-to-frame churn per _Distortion value;
    // this is a lower bound (the real scene also moves under the drops).
    private static void RenderRefractionSweep(Camera camera, Material rain, System.Text.StringBuilder metrics)
    {
        var savedPos = camera.transform.position;
        float savedSize = camera.orthographicSize;
        RenderTexture savedTarget = camera.targetTexture;
        float savedDistortion = rain.GetFloat("_Distortion");
        var big = new RenderTexture(1024, 1024, 24);
        var rig = new GameObject("RefractRig");
        var checker = new Texture2D(256, 256, TextureFormat.RGB24, false);
        BuildChecker(checker, 0);
        try
        {
            AddPane(rig.transform, "Center", new Vector3(0f, 0f, 0f), 0f, 1.0f, PaneUvMode.Authored01, rain);
            AddPane(rig.transform, "Left", new Vector3(-0.60f, 0f, -0.12f), 35f, 0.7f, PaneUvMode.Authored01, rain);
            AddPane(rig.transform, "Right", new Vector3(0.60f, 0f, -0.12f), -35f, 0.7f, PaneUvMode.Authored01, rain);
            camera.transform.position = new Vector3(-0.45f, 0f, -3f);
            camera.orthographicSize = 0.16f;
            camera.targetTexture = big;
            rain.SetFloat("_Speed", 0f);
            rain.SetFloat("_Intensity", 1f);
            rain.SetFloat("_Refract", 1f);
            Shader.SetGlobalTexture("_CameraOpaqueTexture", checker);
            float[] distortions = { 0.035f, 0.010f, 0.006f };
            string[] tags = { "d035", "d010", "d006" };
            for (int d = 0; d < distortions.Length; d++)
            {
                rain.SetFloat("_Distortion", distortions[d]);
                rain.SetFloat("_FlowPhase", 4.70f);
                Color32[] a = Capture(camera, big, "refract-" + tags[d] + "-a.png");
                rain.SetFloat("_FlowPhase", 4.72f);
                Color32[] b = Capture(camera, big, "refract-" + tags[d] + "-b.png");
                int refractChurn = Churn(a, b);
                metrics.AppendLine(string.Format("refract {0} churn={1} meanAbs={2:F3}",
                    tags[d], refractChurn, MeanAbs(a, b)));
                if (tags[d] == "d010")
                    Check(refractChurn < 10000, "Shipped distortion must not churn refraction: " + refractChurn);
            }
            // Scene-motion variant: identical phase, checker scrolled 8px (terrain
            // rushing under static drops). Isolates the distortion lever properly.
            float[] motDist = { 0.035f, 0.006f };
            string[] motTags = { "m035", "m006" };
            for (int d = 0; d < motDist.Length; d++)
            {
                rain.SetFloat("_Distortion", motDist[d]);
                rain.SetFloat("_FlowPhase", 4.70f);
                BuildChecker(checker, 0);
                Color32[] still = Capture(camera, big, "refract-" + motTags[d] + "-still.png");
                BuildChecker(checker, 8);
                Color32[] moved = Capture(camera, big, "refract-" + motTags[d] + "-moved.png");
                metrics.AppendLine(string.Format("motion {0} churn={1} meanAbs={2:F3}",
                    motTags[d], Churn(still, moved), MeanAbs(still, moved)));
            }
            BuildChecker(checker, 0);
        }
        finally
        {
            rain.SetFloat("_Distortion", savedDistortion);
            rain.SetFloat("_Refract", 0f);
            Shader.SetGlobalTexture("_CameraOpaqueTexture", Texture2D.blackTexture);
            camera.transform.position = savedPos;
            camera.orthographicSize = savedSize;
            camera.targetTexture = savedTarget;
            big.Release();
            UnityEngine.Object.Destroy(big);
            UnityEngine.Object.Destroy(rig);
            UnityEngine.Object.Destroy(checker);
        }
    }

    private static void BuildChecker(Texture2D checker, int xOffset)
    {
        for (int y = 0; y < checker.height; y++)
            for (int x = 0; x < checker.width; x++)
                checker.SetPixel(x, y, (((x + xOffset) / 16) + (y / 16)) % 2 == 0 ? Color.white : Color.black);
        checker.Apply();
    }

    private static int Churn(Color32[] a, Color32[] b)
    {
        int churn = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 24) churn++;
        return churn;
    }

    private static double MeanAbs(Color32[] a, Color32[] b)
    {
        double abs = 0;
        for (int i = 0; i < a.Length; i++)
            abs += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
        return abs / a.Length;
    }

    private static void AddPane(Transform rig, string name, Vector3 center, float yaw,
        float width, PaneUvMode mode, Material rain)
    {
        var pane = new GameObject(name);
        pane.transform.SetParent(rig, false);
        pane.transform.position = center;
        pane.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        var mesh = new Mesh();
        float x = width / 2f;
        mesh.vertices = new[] { new Vector3(-x, -0.5f, 0f), new Vector3(x, -0.5f, 0f),
            new Vector3(x, 0.5f, 0f), new Vector3(-x, 0.5f, 0f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        if (mode == PaneUvMode.Authored01)
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f) };
        else if (mode == PaneUvMode.AtlasSliver)
            mesh.uv = new[] { new Vector2(0.42f, 0.61f), new Vector2(0.47f, 0.61f),
                new Vector2(0.47f, 0.66f), new Vector2(0.42f, 0.66f) };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        pane.AddComponent<MeshFilter>().sharedMesh = mesh;
        pane.AddComponent<MeshRenderer>().sharedMaterial = rain;
    }

    private static Mesh Quad()
    {
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-0.5f,-0.5f,0), new Vector3(0.5f,-0.5f,0),
            new Vector3(0.5f,0.5f,0), new Vector3(-0.5f,0.5f,0) };
        mesh.triangles = new[] { 0,1,2,0,2,3 };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static Color32[] Capture(Camera camera, RenderTexture target, string path)
    {
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());
        Color32[] pixels = image.GetPixels32();
        UnityEngine.Object.Destroy(image); RenderTexture.active = previous;
        return pixels;
    }
}
#endif
