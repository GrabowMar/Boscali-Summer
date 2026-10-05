#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using BoscaliSummer.Modules.Weather.Visuals;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Fixed-seed moving-particle captures of the production emitter. Simplified background and
// built-in particle fallback; this is not proof of the game's URP/cockpit integration or FPS.
public sealed class RainBench : MonoBehaviour
{
#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            AssetDatabase.CreateAsset(RainStreakMaterial.CreateMaterial(null), "Assets/Resources/RainFallback.mat");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("RainBench").AddComponent<RainBench>();
            EditorSceneManager.SaveScene(scene, "Assets/rain.unity");
            var result = BuildPipeline.BuildPlayer(new[] { "Assets/rain.unity" }, "Player/WeatherCheck.exe",
                BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Rain player build failed");
            File.WriteAllText("build-result.txt", "PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("build-result.txt", e.ToString()); EditorApplication.Exit(1); }
    }
#endif

    private IEnumerator Start()
    {
        yield return null;
        try { Run(); Application.Quit(0); }
        catch (Exception e) { File.WriteAllText("result.txt", "FAIL " + e); Application.Quit(1); }
    }

    private static void Run()
    {
        const int width = 1280, height = 720;
        var log = new StringBuilder("Rain bench: fixed seed, 60 Hz simulation, built-in particle fallback.\n");
        log.AppendLine(SystemInfo.graphicsDeviceName);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.fieldOfView = 70f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 1000f;
        camera.depthTextureMode = DepthTextureMode.Depth;
        var target = new RenderTexture(width, height, 24);
        camera.targetTexture = target;
        var readback = new Texture2D(width, height, TextureFormat.RGB24, false);
        var backdrop = new GameObject("Contrast reference");
        backdrop.transform.SetParent(camera.transform, false);
        for (int x = 0; x < 12; x++)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.transform.SetParent(backdrop.transform, false);
            block.transform.localPosition = new Vector3((x - 5.5f) * 12f, -18f + (x % 3) * 2f, 80f);
            block.transform.localScale = new Vector3(11f, 30f + (x % 3) * 6f, 8f);
            var material = new Material(Resources.Load<Shader>("Fixture"));
            material.SetColor("_Color", new Color(0.08f + x % 3 * 0.035f, 0.11f + x % 3 * 0.035f, 0.13f + x % 3 * 0.035f));
            block.GetComponent<Renderer>().sharedMaterial = material;
        }
        string[] names = { "light-hover", "heavy-hover", "heavy-crosswind", "heavy-flight-forward", "heavy-flight-side", "heavy-night" };
        for (int scene = 0; scene < names.Length; scene++)
        {
            string name = names[scene];
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.Euler(0f, scene == 4 ? 70f : 0f, 0f);
            float light = scene == 5 ? 0.08f : 0.7f;
            camera.backgroundColor = scene == 5 ? new Color(0.012f, 0.018f, 0.028f) : new Color(0.34f, 0.38f, 0.43f);
            var emitter = new GameObject("Rain").AddComponent<ProceduralRainEmitter>();
            emitter.Initialize(camera);
            var ps = emitter.GetComponent<ParticleSystem>();
            if (ps == null) throw new Exception("Rain emitter failed to initialize");
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 47291;
            Vector3 velocity = scene == 3 || scene == 4 ? Vector3.forward * 220f : Vector3.zero;
            Vector3 wind = scene == 2 ? new Vector3(14f, 0f, 0f) : Vector3.zero;
            // Configure apparent velocity once, then move its custom frame with the camera.
            // Production obtains travel/Time.deltaTime; this synchronous fixture supplies exact 60 Hz motion.
            emitter.UpdateRain(velocity, wind, scene == 0 ? 0.4f : 1f, camera, lightLevel: light);
            Transform frame = ps.main.customSimulationSpace;
            Vector3 upstream = emitter.transform.position;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.enabled = false;
            Capture(camera, target, readback, name + "-dry.png");
            renderer.enabled = true;
            Directory.CreateDirectory(name);
            for (int f = 0; f < 240; f++)
            {
                camera.transform.position += velocity / 60f;
                frame.position = camera.transform.position;
                emitter.transform.position = camera.transform.position + upstream;
                ps.Simulate(1f / 60f, true, false, true);
                if (f >= 180 && f % 3 == 0)
                    Capture(camera, target, readback, name + "/" + f.ToString("D4") + ".png");
            }
            Capture(camera, target, readback, name + ".png");
            var watch = Stopwatch.StartNew();
            for (int f = 0; f < 60; f++) camera.Render();
            Sync(target, readback);
            double wetMs = watch.Elapsed.TotalMilliseconds / 60;
            renderer.enabled = false;
            watch.Restart();
            for (int f = 0; f < 60; f++) camera.Render();
            Sync(target, readback);
            log.AppendLine(FormattableString.Invariant($"{name}: alive={ps.particleCount}, limit={ps.main.maxParticles}, material={renderer.sharedMaterial.shader.name}, wet={wetMs:F3} ms, dry={watch.Elapsed.TotalMilliseconds / 60:F3} ms"));
            if (ps.main.maxParticles > 2500 || ps.particleCount > ps.main.maxParticles)
                throw new Exception("Rain exceeded the production particle ceiling");
            UnityEngine.Object.DestroyImmediate(emitter.gameObject);
        }
        File.WriteAllText("result.txt", "PASS\n" + log);
    }

    private static void Capture(Camera camera, RenderTexture target, Texture2D image, string path)
    {
        camera.Render();
        RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, image.EncodeToPNG());
    }

    private static void Sync(RenderTexture target, Texture2D image)
    {
        RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        image.Apply();
        RenderTexture.active = null;
    }
}
#endif
