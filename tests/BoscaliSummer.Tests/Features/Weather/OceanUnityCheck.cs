#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Actual production shader and SeaState sources, with procedural stand-ins for native texture/probe/mesh inputs.
// The fixture does not run OceanDressing, the game's camera stack, buoyancy, or multiplayer, and is not a performance gate.
public sealed class OceanUnityCheck : MonoBehaviour
{
    private readonly StringBuilder log = new StringBuilder();
    private readonly StringBuilder metrics = new StringBuilder("scenario,mean_linear_rgb,stddev_linear_luma,max_linear_rgb\n");
    private readonly RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest();
    private readonly float[] amplitudes = new float[16];
    private readonly Vector4[] waves = new Vector4[16], phases = new Vector4[16];
    private Camera eye;
    private Light sun;
    private RenderTexture target;
    private MeshRenderer water;
    private Material baseline, improved;
    private Cubemap sky;
    private int assertions;

#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            foreach (string name in new[] { "OceanRenderer.asset", "OceanPipeline.asset" }) AssetDatabase.DeleteAsset("Assets/Resources/" + name);
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, "Assets/Resources/OceanRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.supportsHDR = true; pipeline.msaaSampleCount = 1;
            AssetDatabase.CreateAsset(pipeline, "Assets/Resources/OceanPipeline.asset");
            GraphicsSettings.renderPipelineAsset = pipeline; QualitySettings.renderPipeline = pipeline;
            PlayerSettings.colorSpace = ColorSpace.Linear; PlayerSettings.graphicsJobs = false;
            foreach (string name in new[] { "Ocean", "OceanBaseline" })
            {
                Shader shader = Resources.Load<Shader>(name);
                if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Production shader missing or invalid: " + name);
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Offline ocean fixture").AddComponent<OceanUnityCheck>();
            EditorSceneManager.SaveScene(scene, "Assets/check.unity"); AssetDatabase.SaveAssets();
            if (File.Exists("editor-render.txt"))
            {
                EditorApplication.EnterPlaymode();
                return;
            }
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/check.unity" }, "Player/OceanCheck.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Player build failed: " + report.summary.result);
            File.WriteAllText("build-result.txt", "PASS: production and baseline Ocean shaders + SeaState built in isolated URP 14.0.12 player.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { File.WriteAllText("build-result.txt", "FAIL: " + error); Debug.LogException(error); EditorApplication.Exit(1); }
    }
#endif

    private IEnumerator Start()
    {
        bool failed = false;
        IEnumerator run = Run();
        while (true)
        {
            bool next;
            try { next = run.MoveNext(); }
            catch (Exception error) { log.AppendLine("FAIL: " + error); Debug.LogException(error); failed = true; break; }
            if (!next) break;
            yield return run.Current;
        }
        File.WriteAllText("metrics.csv", metrics.ToString());
        File.WriteAllText("result.txt", (failed ? "FAIL: " : "PASS: " + assertions + " assertions. ") +
            "Production Ocean shaders and pure sea spectra rendered through real offline URP. " +
            "PROCEDURAL FIXTURE textures, sky, nonuniform grid, and manual material inputs substitute native game data. " +
            "No game boot, deployment, live acceptance, or performance claim.\n" + log);
#if UNITY_EDITOR
        EditorApplication.Exit(failed ? 1 : 0);
#else
        Application.Quit(failed ? 1 : 0);
#endif
    }

    private IEnumerator Run()
    {
        Application.runInBackground = true; QualitySettings.vSyncCount = 0;
        log.AppendLine("GPU " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType);
        Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "Real URP active");
        baseline = new Material(Resources.Load<Shader>("OceanBaseline"));
        improved = new Material(Resources.Load<Shader>("Ocean"));
        Check(baseline.shader.isSupported && improved.shader.isSupported, "Both ocean shaders supported by active graphics device");
        target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf) { name = "OceanFixtureFrame", antiAliasing = 1 };
        target.Create(); request.destination = target;
        eye = new GameObject("Fixture flight camera").AddComponent<Camera>(); eye.enabled = false;
        eye.clearFlags = CameraClearFlags.SolidColor; eye.allowHDR = true; eye.targetTexture = target;
        eye.nearClipPlane = .5f; eye.farClipPlane = 100000; eye.fieldOfView = 60;
        sun = new GameObject("Fixture sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.shadows = LightShadows.None;
        var plane = new GameObject("PROCEDURAL FIXTURE nonuniform ocean grid");
        plane.AddComponent<MeshFilter>().sharedMesh = Grid(); water = plane.AddComponent<MeshRenderer>();
        sky = new Cubemap(64, TextureFormat.RGBAHalf, true);
        BindTextures(baseline); BindTextures(improved);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.35f, .4f, .45f);
        foreach (string view in new[] { "low", "aerial" })
        foreach (string sea in new[] { "calm", "fresh", "storm" })
        foreach (string light in new[] { "noon", "sunset" })
        {
            float wind = sea == "calm" ? 3.5f : sea == "fresh" ? 10f : 22f;
            SetLight(light == "sunset", sea == "storm");
            eye.transform.position = new Vector3(0, view == "low" ? 14 : 2600, 0);
            eye.transform.rotation = Quaternion.Euler(view == "low" ? 8 : 31, 23, 0);
            // The runtime disc follows the camera; the grid is centred beneath it too, with anchor-relative shader coordinates.
            plane.transform.position = new Vector3(eye.transform.position.x, 0, eye.transform.position.z);
            foreach (bool before in new[] { true, false })
            {
                Material material = before ? baseline : improved;
                SetSea(material, wind, before);
                water.sharedMaterial = material;
                // StandardRequest renders the actual URP camera; rendering disabled on the camera avoids duplicate automatic frames.
                // Let URP and each material finish their first-use setup before reading a comparison frame.
                for (int frame = 0; frame < 4; frame++)
                {
                    yield return null;
                    RenderPipeline.SubmitRenderRequest(eye, request);
                }
                // ReadPixels synchronizes GPU completion; a frame yield also works in batch Editor PlayMode.
                yield return null;
                Save(view + "-" + sea + "-" + light + (before ? "-baseline" : "-improved"));
            }
        }
        Check(true, "24 low/aerial, calm/fresh/storm, noon/sunset comparison frames rendered");
    }

    private void SetSea(Material material, float wind, bool before)
    {
        if (before) SeaStateBaseline.Amplitudes(wind, amplitudes); else SeaState.Amplitudes(wind, amplitudes);
        float steep = before ? SeaStateBaseline.Steepness(wind) : SeaState.Steepness(wind);
        float leanSum = 0;
        for (int i = 0; i < 16; i++)
            leanSum += steep * (float)(2 * Math.PI / (before ? SeaStateBaseline.Wavelength(i) : SeaState.Wavelength(i))) * amplitudes[i];
        float q = leanSum > .95f ? steep * .95f / leanSum : steep;
        // Old snapshots used two swell components and inline seeded spread; newer snapshots own their direction helper.
        MethodInfo baselineDirection = typeof(SeaStateBaseline).GetMethod("DirectionOffset", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo baselineCount = typeof(SeaStateBaseline).GetField("SwellCount", BindingFlags.Static | BindingFlags.NonPublic);
        int swellCount = before ? baselineCount == null ? 2 : (int)baselineCount.GetRawConstantValue() : SeaState.SwellCount;
        for (int i = 0; i < 16; i++)
        {
            float wavelength = (float)(before ? SeaStateBaseline.Wavelength(i) : SeaState.Wavelength(i));
            float width = i < 2 ? 12 : 15 + 55 * i / 15f;
            float spread = before ? baselineDirection == null
                ? WeatherMath.HashRange(0x5EA5u, i, 0, 0, -width, width)
                : (float)baselineDirection.Invoke(null, new object[] { i }) : SeaState.DirectionOffset(i);
            float deg = (i < swellCount ? 32 : 0) + spread;
            float phase = WeatherMath.HashRange(0x5EA5u, i, 1, 0, 0, 2 * Mathf.PI) - Mathf.Sqrt(9.81f * 2 * Mathf.PI / wavelength) * 37;
            waves[i] = new Vector4(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad), 2 * Mathf.PI / wavelength, amplitudes[i]);
            phases[i] = new Vector4(phase, q, wavelength, 0);
        }
        float storm = before ? SeaStateBaseline.Storminess(wind) : SeaState.Storminess(wind);
        float coverage = before ? SeaStateBaseline.Whitecaps(wind) : SeaState.Whitecaps(wind);
        material.SetVectorArray("_Waves", waves); material.SetVectorArray("_WavePhase", phases);
        material.SetVectorArray("_OceanCalm", new Vector4[8]); material.SetVectorArray("_OceanWash", new Vector4[4]);
        material.SetVectorArray("_OceanWashDir", new Vector4[4]);
        material.SetVector("_OceanAnchor", Vector4.zero); material.SetVector("_OriginOffset", Vector4.zero);
        material.SetVector("_size", new Vector4(81920, 81920, 0, 0));
        material.SetVector("_OceanDisp", new Vector4(900, 2200, 37, 0));
        material.SetVector("_OceanWind", new Vector4(0, 1, before ? SeaStateBaseline.Ripples(wind) : SeaState.Ripples(wind), .45f));
        // Baseline snapshots read y/z as a lean-share threshold; production reads breaking cores and lace start in sigmas.
        material.SetVector("_OceanSea", before
            ? new Vector4(SeaStateBaseline.Roughness(wind), Mathf.Min(leanSum, .95f) * Mathf.Lerp(.7f, .3f, Mathf.Sqrt(coverage / .6f)), .12f, coverage)
            : new Vector4(SeaState.Roughness(wind), SeaState.WhitecapSigma(coverage * .2f), SeaState.WhitecapSigma(coverage), coverage));
        material.SetVector("_OceanSea2", new Vector4(before ? SeaStateBaseline.Streaks(wind) : SeaState.Streaks(wind), Mathf.Lerp(.25f, 1, storm), 6, 1));
        material.SetVector("_OceanShore", new Vector4(.55f, .92f, .6f, .35f));
        material.SetVector("_RippleA", new Vector4(.13f, .29f, 1 / 11f, 1));
        material.SetVector("_RippleB", new Vector4(.37f, .61f, 1 / 37f, .6f));
        material.SetVector("_RippleC", new Vector4(.47f, .19f, 1 / 170f, .8f));
        material.SetVector("_FoamUV", new Vector4(.17f, .43f, 1 / 46f, 1));
        material.SetVector("_StreakUV", new Vector4(.31f, .73f, 1 / 140f, 1));
        material.SetVector("_GustUV", new Vector4(.23f, .53f, 1 / 2400f, 1));
        material.SetColor("_OceanDeep", before ? Color.Lerp(new Color(.01f, .16f, .27f), new Color(.09f, .16f, .17f), storm)
            : Color.Lerp(new Color(.006f, .075f, .17f), new Color(.03f, .065f, .08f), storm));
        material.SetColor("_OceanScatter", new Color(.1f, .55f, .48f, .6f * Mathf.Lerp(.5f, 1, storm)));
        material.SetColor("_OceanFoamTint", new Color(.93f, .96f, .98f));
        material.SetVector("_OceanAmbient", new Vector4(.24f, .29f, .34f, 1));
        material.SetColor("_OceanFogColor", before ? eye.backgroundColor : eye.backgroundColor.linear);
        material.SetVector("_OceanFog", new Vector4(.000035f, 0, 1, 0));
    }

    private void SetLight(bool sunset, bool storm)
    {
        float elevation = sunset ? 8 : 52;
        Vector3 toward = Quaternion.Euler(-elevation, 23, 0) * Vector3.forward;
        sun.transform.rotation = Quaternion.LookRotation(-toward); sun.intensity = storm ? .28f : sunset ? 1 : 1.35f;
        sun.color = sunset ? new Color(1, .53f, .26f) : new Color(1, .96f, .9f);
        Color horizon = storm ? new Color(.22f, .26f, .29f) : sunset ? new Color(.48f, .29f, .21f) : new Color(.44f, .61f, .74f);
        Color zenith = storm ? new Color(.15f, .18f, .21f) : sunset ? new Color(.12f, .18f, .3f) : new Color(.1f, .3f, .57f);
        eye.backgroundColor = horizon;
        for (int face = 0; face < 6; face++)
        {
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                Vector3 d = CubeDirection(face, (x + .5f) / 32 - 1, (y + .5f) / 32 - 1);
                float up = Mathf.Clamp01(d.y);
                float wisps = Mathf.PerlinNoise(d.x * 2.3f + 7, d.z * 2.3f + 13) * Mathf.Pow(1 - up, 2);
                pixels[y * 64 + x] = Color.Lerp(horizon, zenith, Mathf.Sqrt(up)) + Color.white * wisps * (storm ? .035f : .08f);
            }
            sky.SetPixels(pixels, (CubemapFace)face);
        }
        sky.Apply(true);
    }

    private void BindTextures(Material material)
    {
        material.SetTexture("_OceanSky", sky);
        material.SetTexture("_macro_basecolor", Texture2D.blackTexture); material.SetTexture("_macro_depth", Texture2D.blackTexture);
        foreach (string name in new[] { "_RippleNormal", "_FoamTex", "_StreakTex", "_GustTex" })
        {
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, true, true) { name = "PROCEDURAL FIXTURE " + name, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[256 * 256];
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                float u = x / 256f, v = y / 256f;
                // Periodic lattice noise avoids a hard seam; these are stand-ins, not recovered native textures.
                float n = TileNoise(u, v, name == "_StreakTex" ? 3 : 9, 14);
                if (name == "_RippleNormal")
                {
                    float nx = .23f * Mathf.Sin(u * 16 * Mathf.PI + .9f * Mathf.Sin(v * 8 * Mathf.PI));
                    float ny = .18f * Mathf.Cos(v * 20 * Mathf.PI + .7f * Mathf.Sin(u * 10 * Mathf.PI));
                    // Unity's RG-or-AG unpack multiplies R by A: use RG encoding with A=1, never duplicate encoded X in alpha.
                    pixels[y * 256 + x] = new Color(nx * .5f + .5f, ny * .5f + .5f, 1, 1);
                }
                else pixels[y * 256 + x] = new Color(n, n, n, 1);
            }
            texture.SetPixels(pixels); texture.Apply(true); material.SetTexture(name, texture);
        }
    }

    private static float TileNoise(float u, float v, int nx, int ny)
    {
        float x = u * nx, y = v * ny;
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float tx = Mathf.SmoothStep(0, 1, x - ix), ty = Mathf.SmoothStep(0, 1, y - iy);
        float a = WeatherMath.Hash01(318u, ix % nx, iy % ny), b = WeatherMath.Hash01(318u, (ix + 1) % nx, iy % ny);
        float c = WeatherMath.Hash01(318u, ix % nx, (iy + 1) % ny), d = WeatherMath.Hash01(318u, (ix + 1) % nx, (iy + 1) % ny);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty) * .75f + .125f;
    }

    private static Vector3 CubeDirection(int face, float u, float v)
    {
        switch (face)
        {
            case 0: return new Vector3(1, -v, -u).normalized;
            case 1: return new Vector3(-1, -v, u).normalized;
            case 2: return new Vector3(u, 1, v).normalized;
            case 3: return new Vector3(u, -1, -v).normalized;
            case 4: return new Vector3(u, -v, 1).normalized;
            default: return new Vector3(-u, -v, -1).normalized;
        }
    }

    private static Mesh Grid()
    {
        const int half = 304, count = half * 2 + 1;
        var vertices = new Vector3[count * count]; var triangles = new int[(count - 1) * (count - 1) * 6];
        for (int z = 0; z < count; z++) for (int x = 0; x < count; x++) vertices[z * count + x] = new Vector3(GridAxis(x - half), 0, GridAxis(z - half));
        int t = 0;
        for (int z = 0; z < count - 1; z++) for (int x = 0; x < count - 1; x++)
        {
            int p = z * count + x;
            triangles[t++] = p; triangles[t++] = p + count; triangles[t++] = p + 1;
            triangles[t++] = p + 1; triangles[t++] = p + count; triangles[t++] = p + count + 1;
        }
        var mesh = new Mesh { name = "Fixture 4m centre spacing, 2% growth, ~82km half-extent", indexFormat = IndexFormat.UInt32 };
        mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateBounds();
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(180000, 50, 180000));
        return mesh;
    }
    private static float GridAxis(int i) => Mathf.Sign(i) * 200 * (Mathf.Pow(1.02f, Mathf.Abs(i)) - 1);

    private void Save(string name)
    {
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active; RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); RenderTexture.active = previous;
        Color[] pixels = image.GetPixels(); double sum = 0, sumLuma = 0, sumSquares = 0, foregroundLuma = 0; float maximum = 0;
        int visibleWater = 0;
        Color skyPixel = pixels[pixels.Length - target.width / 2];
        for (int index = 0; index < pixels.Length; index++)
        {
            Color pixel = pixels[index];
            if (!Finite(pixel.r) || !Finite(pixel.g) || !Finite(pixel.b) || !Finite(pixel.a)) throw new Exception("Nonfinite pixel: " + name);
            float luma = pixel.r * .2126f + pixel.g * .7152f + pixel.b * .0722f;
            sum += (pixel.r + pixel.g + pixel.b) / 3; sumLuma += luma; sumSquares += luma * luma;
            if (index < pixels.Length / 4) foregroundLuma += luma;
            maximum = Mathf.Max(maximum, Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)));
            if (index < pixels.Length / 2 && Mathf.Abs(pixel.r - skyPixel.r) + Mathf.Abs(pixel.g - skyPixel.g) + Mathf.Abs(pixel.b - skyPixel.b) > .005f) visibleWater++;
        }
        double mean = sum / pixels.Length, variance = sumSquares / pixels.Length - Math.Pow(sumLuma / pixels.Length, 2);
        Check(mean > .00001 && variance > .0000001, name + ": finite, nonempty frame with visible variation");
        Check(visibleWater > pixels.Length / 20, name + ": foreground water differs from clear background");
        Check(foregroundLuma / (pixels.Length / 4) > .001, name + ": foreground water has nonzero radiance after shader warmup");
        metrics.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F7},{2:F7},{3:F7}\n", name, mean, Math.Sqrt(Math.Max(0, variance)), maximum);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
        var output = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        output.SetPixels(pixels); output.Apply(); File.WriteAllBytes(name + ".png", output.EncodeToPNG());
        Destroy(output); Destroy(image);
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        assertions++; log.AppendLine("PASS " + description);
    }
}
#endif
