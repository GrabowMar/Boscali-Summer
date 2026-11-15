using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Production ocean pass, procedural stand-ins for native inputs; explicit draw preserves its URP-only pass tags.
// This checks shader output only: no game camera stack, cloud renderer, visual acceptance or FPS claim.
public static class OceanReflectionCheck
{
    private const int Width = 640, Height = 360;

    public static string Run(Shader shader)
    {
        if (!shader.isSupported || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
            throw new Exception("Ocean fixture requires its shader and an ARGBFloat render target.");
        var baseline = Baseline();
        var eyeObject = new GameObject("OceanCheck.Camera");
        var mesh = new Mesh { name = "OceanCheck.FlatPlane" };
        var active = new Material(shader);
        var before = baseline == null ? null : new Material(baseline);
        var cube = new Cubemap(128, TextureFormat.RGBAHalf, true) { filterMode = FilterMode.Trilinear };
        var black = new Cubemap(16, TextureFormat.RGBAHalf, true);
        var ripple = Ripple();
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var read = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
        RenderTexture savedTarget = RenderTexture.active;
        Material savedSkybox = RenderSettings.skybox;
        var skybox = new Material(Shader.Find("Skybox/Cubemap"));
        string output = "OceanReflectionCheck";
        Directory.CreateDirectory(output);
        var metrics = new StringBuilder("scenario,variant,mean_water_luma,stddev_water_luma,max_linear_rgb,mean_rgb_change\n");
        try
        {
            mesh.vertices = new[] { new Vector3(-200000, 0, -200000), new Vector3(-200000, 0, 200000),
                new Vector3(200000, 0, 200000), new Vector3(200000, 0, -200000) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            var eye = eyeObject.AddComponent<Camera>();
            eye.enabled = false;
            eye.clearFlags = CameraClearFlags.Skybox;
            eye.cullingMask = 0;
            eye.nearClipPlane = 0.5f;
            eye.farClipPlane = 250000;
            eye.fieldOfView = 60;
            eye.allowHDR = true;
            eye.allowMSAA = false;
            eye.targetTexture = target;
            Shader.SetGlobalVector("_MainLightPosition", new Vector4(-0.25f, 0.13f, 0.96f, 0));
            Shader.SetGlobalVector("_MainLightColor", new Vector4(0.7f, 0.27f, 0.08f, 1));
            Shader.SetGlobalVector("_MainLightShadowParams", Vector4.zero);
            for (int face = 0; face < 6; face++) black.SetPixels(new Color[16 * 16], (CubemapFace)face);
            black.Apply(true);
            skybox.SetTexture("_Tex", cube);
            skybox.SetColor("_Tint", Color.gray);
            skybox.SetFloat("_Exposure", 1);
            RenderSettings.skybox = skybox;
            for (int scenario = 0; scenario < 4; scenario++)
            {
                bool aerial = scenario == 3;
                float wind = scenario == 0 ? 3.5f : scenario == 2 ? 22 : 10;
                string name = aerial ? "aerial-fresh-sunset" : "low-" + (scenario == 0 ? "calm" : scenario == 1 ? "fresh" : "storm") + "-sunset";
                CloudSky(cube, scenario == 2);
                eye.transform.position = new Vector3(0, aerial ? 2600 : 14, 0);
                eye.transform.rotation = Quaternion.Euler(aerial ? 31 : 8, 23, 0);
                Configure(active, cube, ripple, wind);
                Color[] image = Render(eye, target, read, mesh, active);
                Stats(image, name, "active", 0, metrics);
                Save(Path.Combine(output, name + "-active.png"), image);
                if (before != null)
                {
                    Configure(before, cube, ripple, wind);
                    Color[] original = Render(eye, target, read, mesh, before);
                    double difference = Difference(image, original);
                    Stats(original, name, "baseline", difference, metrics);
                    Save(Path.Combine(output, name + "-baseline.png"), original);
                }
                if (scenario != 1) continue;
                active.SetTexture("_OceanSky", black);
                Color[] noProbe = Render(eye, target, read, mesh, active);
                Stats(noProbe, "black-probe", "active", 0, metrics);
                if (Difference(image, noProbe) < 0.0001) throw new Exception("Ocean fixture did not respond to cloud probe radiance.");
                Save(Path.Combine(output, "black-probe.png"), noProbe);
                active.SetTexture("_OceanSky", cube);
                Color fogColour = new Color(0.12f, 0.16f, 0.2f, 1);
                active.SetColor("_OceanFogColor", fogColour);
                active.SetVector("_OceanFog", new Vector4(10, 0, 1, 0));
                Color[] fog = Render(eye, target, read, mesh, active);
                for (int p = 0; p < Width * Height / 3; p++)
                    if (Math.Abs(fog[p].r - fogColour.r) > 0.0003 || Math.Abs(fog[p].g - fogColour.g) > 0.0003 || Math.Abs(fog[p].b - fogColour.b) > 0.0003)
                        throw new Exception("Ocean dense fog did not converge to the fog colour at water pixel " + p);
                Save(Path.Combine(output, "dense-fog.png"), fog);
            }
            File.WriteAllText(Path.Combine(output, "metrics.csv"), metrics.ToString());
            return "Ocean fixture PASS: four sunset sea views, finite nonempty water, cloud probe contributes, black probe finite and dense fog converges. " +
                (before == null ? "No comparison shader supplied. " : "Four baseline/active pairs saved for visual review. ") +
                "Procedural cubemap/ripple/flat plane; explicit production pass draw, no game performance or native cloud acceptance claim.";
        }
        finally
        {
            RenderTexture.active = savedTarget;
            RenderSettings.skybox = savedSkybox;
            Object.DestroyImmediate(eyeObject); Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(active); if (before != null) Object.DestroyImmediate(before);
            Object.DestroyImmediate(cube); Object.DestroyImmediate(black); Object.DestroyImmediate(ripple);
            Object.DestroyImmediate(skybox); Object.DestroyImmediate(read);
            target.Release(); Object.DestroyImmediate(target);
        }
    }

    private static Shader Baseline()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-oceanBaselineShader");
        if (index < 0) return null;
        if (index + 1 >= args.Length) throw new Exception("Missing -oceanBaselineShader asset path.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(args[index + 1]);
        if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new Exception("Ocean comparison shader missing or unsupported.");
        return shader;
    }

    private static void Configure(Material material, Cubemap cube, Texture2D ripple, float wind)
    {
        var waves = new Vector4[16]; var phase = new Vector4[16];
        float storm = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(9, 22, wind));
        for (int i = 0; i < 16; i++)
        {
            float length = 320 * Mathf.Pow(0.76f, i), angle = (i * 0.618034f - Mathf.Floor(i * 0.618034f) - 0.5f) * 1.6f;
            float amplitude = i < 4 ? 0.18f * Mathf.Exp(-0.3f * i) : Mathf.Lerp(0.008f, 0.055f, storm) * Mathf.Sqrt(length);
            waves[i] = new Vector4(Mathf.Sin(angle), Mathf.Cos(angle), 2 * Mathf.PI / length, amplitude);
            phase[i] = new Vector4(i * 2.39f, Mathf.Lerp(0.35f, 0.6f, storm), length, 0);
        }
        material.SetVectorArray("_Waves", waves); material.SetVectorArray("_WavePhase", phase);
        material.SetVectorArray("_OceanCalm", new Vector4[8]); material.SetVectorArray("_OceanWash", new Vector4[4]);
        material.SetVectorArray("_OceanWashDir", new Vector4[4]);
        material.SetTexture("_OceanSky", cube); material.SetTexture("_RippleNormal", ripple);
        material.SetTexture("_macro_basecolor", Texture2D.blackTexture); material.SetTexture("_macro_depth", Texture2D.blackTexture);
        material.SetTexture("_FoamTex", Texture2D.grayTexture); material.SetTexture("_StreakTex", Texture2D.blackTexture);
        material.SetTexture("_GustTex", Texture2D.grayTexture);
        material.SetVector("_OceanAnchor", Vector4.zero); material.SetVector("_OriginOffset", Vector4.zero);
        material.SetVector("_size", new Vector4(81920, 81920, 0, 0));
        material.SetVector("_OceanDisp", new Vector4(900, 2200, 37, 0));
        material.SetVector("_OceanWind", new Vector4(0, 1, Mathf.Lerp(0.3f, 1, Mathf.InverseLerp(3.5f, 12, wind)), 0.45f));
        material.SetVector("_OceanSea", new Vector4(Mathf.Lerp(0.03f, 0.1f, Mathf.InverseLerp(2, 20, wind)), 3, 2.6f, 0));
        material.SetVector("_OceanSea2", new Vector4(0, 0, 7, 1));
        material.SetVector("_RippleA", new Vector4(0.17f, 0.31f, 1f / 11, 1));
        material.SetVector("_RippleB", new Vector4(0.41f, 0.07f, 1f / 37, 0.6f));
        material.SetVector("_RippleC", new Vector4(0.13f, 0.71f, 1f / 170, 0.8f));
        material.SetVector("_FoamUV", new Vector4(0, 0, 1f / 46, 1));
        material.SetVector("_StreakUV", new Vector4(0, 0, 1f / 140, 1));
        material.SetVector("_GustUV", new Vector4(0, 0, 1f / 2400, 1));
        material.SetVector("_OceanShore", new Vector4(0.55f, 0.92f, 0.6f, 0.35f));
        material.SetColor("_OceanDeep", new Color(0.006f, 0.075f, 0.17f, 1));
        material.SetColor("_OceanScatter", Color.clear); material.SetColor("_OceanFoamTint", Color.white);
        material.SetColor("_OceanAmbient", new Color(0.12f, 0.15f, 0.19f, 1));
        material.SetVector("_OceanFog", Vector4.zero);
    }

    private static Texture2D Ripple()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float u = x * 2 * Mathf.PI / size, v = y * 2 * Mathf.PI / size;
            pixels[y * size + x] = new Color(0.5f + 0.115f * Mathf.Sin(u * 8 + 0.9f * Mathf.Sin(v * 4)),
                0.5f + 0.09f * Mathf.Cos(v * 10 + 0.7f * Mathf.Sin(u * 5)), 1, 1);
        }
        texture.SetPixels(pixels); texture.Apply(true); return texture;
    }

    private static void CloudSky(Cubemap cube, bool storm)
    {
        int size = cube.width;
        for (int face = 0; face < 6; face++)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                Vector3 d = Direction(face, (x + 0.5f) * 2 / size - 1, (y + 0.5f) * 2 / size - 1);
                // Both hemispheres carry the same artificial sky: the fixture has no ground in the probe.
                // This also makes cloud contrast visible in the built-in skybox's face convention.
                float up = Mathf.Abs(d.y);
                Color sky = Color.Lerp(new Color(0.82f, 0.36f, 0.18f), new Color(0.05f, 0.12f, 0.26f), Mathf.Sqrt(up));
                float noise = 0.7f * Mathf.PerlinNoise(d.x * 4.3f + 6.7f, d.z * 4.3f + up * 1.7f + 11.2f) +
                    0.3f * Mathf.PerlinNoise(d.x * 11 + 1, d.z * 11 + up * 3 + 9);
                float cloud = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(storm ? 0.35f : 0.46f, 0.64f, noise));
                float top = 0.12f + 0.30f * noise;
                cloud *= Mathf.SmoothStep(0, 1, (up - 0.035f) / 0.04f) * (1 - Mathf.SmoothStep(0, 1, (up - top) / 0.055f));
                Color litCloud = Color.Lerp(new Color(0.1f, 0.13f, 0.19f), new Color(1.1f, 0.6f, 0.3f), Mathf.SmoothStep(0, 1, (up - 0.10f) / 0.23f));
                pixels[y * size + x] = Color.Lerp(sky, litCloud * (storm ? 0.48f : 1), cloud);
            }
            cube.SetPixels(pixels, (CubemapFace)face);
        }
        cube.Apply(true);
    }

    private static Vector3 Direction(int face, float u, float v)
    {
        // SetPixels rows grow upward; the cubemap face equations use downward texture v.
        v = -v;
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

    private static Color[] Render(Camera eye, RenderTexture target, Texture2D read, Mesh mesh, Material material)
    {
        // Camera.Render supplies the procedural skybox/depth. DrawMesh explicitly selects the unchanged
        // SRPDefaultUnlit ocean pass, which the fixture's built-in renderer intentionally does not schedule.
        eye.Render();
        var commands = new CommandBuffer { name = "OceanCheck.ProductionPass" };
        try
        {
            commands.SetRenderTarget(target);
            commands.SetViewport(new Rect(0, 0, Width, Height));
            commands.SetViewProjectionMatrices(eye.worldToCameraMatrix, eye.projectionMatrix);
            commands.SetGlobalVector("_WorldSpaceCameraPos", eye.transform.position);
            commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            Graphics.ExecuteCommandBuffer(commands);
        }
        finally { commands.Release(); }
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); read.Apply(false, false);
        Color[] pixels = read.GetPixels();
        foreach (Color pixel in pixels)
            if (!Finite(pixel.r) || !Finite(pixel.g) || !Finite(pixel.b) || !Finite(pixel.a)) throw new Exception("Non-finite ocean fixture pixel.");
        return pixels;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void Stats(Color[] pixels, string name, string variant, double change, StringBuilder metrics)
    {
        double sum = 0, square = 0; float maximum = 0; int count = Width * Height / 3;
        for (int i = 0; i < count; i++)
        {
            Color p = pixels[i]; double luma = p.r * 0.2126 + p.g * 0.7152 + p.b * 0.0722;
            sum += luma; square += luma * luma; maximum = Mathf.Max(maximum, Mathf.Max(p.r, Mathf.Max(p.g, p.b)));
            if (Mathf.Min(p.r, Mathf.Min(p.g, p.b)) < -0.0001f || maximum > 1000) throw new Exception("Ocean fixture radiance out of range: " + name);
        }
        double mean = sum / count, deviation = Math.Sqrt(Math.Max(0, square / count - mean * mean));
        if (mean < 0.00001 || deviation < 0.0000001) throw new Exception("Ocean fixture is empty or constant: " + name);
        metrics.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2:G8},{3:G8},{4:G8},{5:G8}\n", name, variant, mean, deviation, maximum, change);
    }

    private static double Difference(Color[] a, Color[] b)
    {
        double difference = 0; int count = Width * Height / 3;
        for (int i = 0; i < count; i++) difference += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
        return difference / (count * 3);
    }

    private static void Save(string path, Color[] pixels)
    {
        var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        try
        {
            var display = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) display[i] = pixels[i].gamma;
            image.SetPixels(display); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(image); }
    }
}
