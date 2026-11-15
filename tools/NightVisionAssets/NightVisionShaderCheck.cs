#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Production shader GPU checks used by Compile-NightVisionAssets.ps1.
// This fixture does not assert live-game appearance or performance.
public static class NightVisionShaderCheck
{
    private const int ProbeSize = 32;
    private static int checks;

    public static void Run()
    {
        Material material = null;
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            Check(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11, "D3D11 render backend");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string[] paths = { "Assets/NightVision/NvgPhosphor.shader", "Assets/NightVision/NvgHeat.shader" };
            string[] names = { "Boscali/NvgPhosphor", "Boscali/NvgHeat" };
            for (int i = 0; i < paths.Length; i++)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(paths[i]);
                Validate(shader, paths[i]);
                Check(shader.name == names[i], "Stable runtime shader name: " + names[i]);
            }
            material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(paths[0]));
            Check(material.passCount >= 2, "Phosphor and unmodified-source meter passes exist");
            string samples = Response(material, 1f) + Response(material, 8f) + Meter(material) + ThermalMask(material);
            var build = new AssetBundleBuild { assetBundleName = "nightvision.bundle", assetNames = paths };
            Check(BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64) != null, "Bundle compiler succeeded");
            foreach (string path in paths) Validate(AssetDatabase.LoadAssetAtPath<Shader>(path), path);
            Check(new FileInfo("BundleOutput/nightvision.bundle").Length <= 512 * 1024, "Bundle fits 512 KiB loader limit");
            File.WriteAllText("result.txt", "PASS: " + checks + " assertions; original night vision shaders compiled and rendered on D3D11. " +
                "Finite monotonic response, dark black, shadow detail, compressed highlights, phosphor whitening, raw-source meter and thermal silhouette masking. Offline GPU fixture; live appearance not asserted.\n" + samples);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
        finally { if (material != null) Object.DestroyImmediate(material); }
    }

    private static void Validate(Shader shader, string path)
    {
        string errors = "";
        if (shader != null)
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                errors += "\n" + message.severity + " " + message.file + ":" + message.line + " " + message.message;
        Check(shader != null && !ShaderUtil.ShaderHasError(shader) && shader.isSupported, "Shader compiles: " + path + errors);
    }

    private static string Response(Material material, float exposure)
    {
        ClearHeat();
        Shader.SetGlobalVector("_NvgNoise", Vector4.zero);
        Shader.SetGlobalVector("_NvgResponse", new Vector4(exposure, 0f, 0f, 0f));
        Shader.SetGlobalColor("_NvgTint", new Color(.78f, .92f, 1f, 1f));
        // Values describe the exposed signal. Feed the inverse exposure to exercise both gain settings.
        float[] signals = { 0f, .0001f, .001f, .004f, .01f, .03f, .12f, .5f, 1f, 4f, 16f, 64f };
        var samples = new Color[signals.Length];
        float previous = -1f;
        for (int i = 0; i < signals.Length; i++)
        {
            samples[i] = Render(material, new Color(signals[i] / exposure, signals[i] / exposure, signals[i] / exposure, 1f), 0) * exposure;
            Color value = samples[i];
            Check(Finite(value) && value.r >= -.001f && value.g >= -.001f && value.b >= -.001f,
                "Finite nonnegative response at signal " + signals[i] + " gain " + exposure + ": " + value);
            Check(Luma(value) >= previous - .0001f, "Monotonic response at signal " + signals[i]);
            previous = Luma(value);
        }
        Check(Luma(samples[0]) < .002f, "Black stays dark");
        Check(Luma(samples[4]) > Luma(samples[2]) + .001f, "Dim terrain signals stay distinguishable");
        Check(Luma(samples[6]) > Luma(samples[4]) + .01f, "Midtone detail stays distinct");
        Check(Luma(samples[11]) < 3f && Luma(samples[11]) > Luma(samples[8]), "HDR highlights compress without clipping every bright signal");
        float middleTint = Mathf.Abs(samples[6].b - samples[6].r) / Mathf.Max(.001f, Luma(samples[6]));
        float brightTint = Mathf.Abs(samples[11].b - samples[11].r) / Mathf.Max(.001f, Luma(samples[11]));
        Check(samples[6].b > samples[6].r && brightTint < middleTint * .7f, "Bright phosphor tends toward neutral white");
        var report = new StringBuilder("gain=" + exposure);
        for (int i = 0; i < signals.Length; i++) report.Append(" ").Append(signals[i].ToString("G4")).Append("=").Append(Luma(samples[i]).ToString("G6"));
        return report.AppendLine().ToString();
    }

    private static string Meter(Material material)
    {
        ClearHeat();
        var input = new Color(.125f, .25f, .5f, 1f);
        Shader.SetGlobalVector("_NvgNoise", new Vector4(.7f, .01f, 21f, 0f));
        Shader.SetGlobalVector("_NvgResponse", new Vector4(16f, 0f, 0f, 0f));
        Shader.SetGlobalColor("_NvgTint", Color.green);
        Color meter = Render(material, input, 1);
        Check(Mathf.Abs(meter.r - input.r) < .001f && Mathf.Abs(meter.g - input.g) < .001f && Mathf.Abs(meter.b - input.b) < .001f,
            "Meter reads raw RGB independently of gain, tint and grain: " + meter);
        return "raw meter=" + meter + "\n";
    }

    private static string ThermalMask(Material material)
    {
        ClearHeat();
        Shader.SetGlobalVector("_NvgNoise", Vector4.zero);
        Shader.SetGlobalVector("_NvgResponse", new Vector4(1f, 0f, 0f, 0f));
        Shader.SetGlobalColor("_NvgTint", new Color(.78f, .92f, 1f, 1f));
        var scene = new Color(.12f, .12f, .12f, 1f);
        Color phosphor = Render(material, scene, 0);
        var mask = new Texture2D(ProbeSize, ProbeSize, TextureFormat.RGBAFloat, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[ProbeSize * ProbeSize];
        for (int y = 8; y < 24; y++)
            for (int x = 8; x < 24; x++) pixels[y * ProbeSize + x] = Color.red;
        mask.SetPixels(pixels);
        mask.Apply();
        try
        {
            Shader.SetGlobalTexture("_BoscaliNvgHeatMask", mask);
            Shader.SetGlobalColor("_NvgHeatColor", new Color(1f, .36f, .04f, 1f));
            Color[] fused = RenderPixels(material, scene, 0);
            Color edge = fused[16 * ProbeSize + 8] - phosphor;
            Color interior = fused[16 * ProbeSize + 16] - phosphor;
            Color background = fused[3 * ProbeSize + 3];
            Check(edge.r > interior.r * 10f + .05f, "Thermal silhouette is substantially brighter than its interior");
            Check(edge.r > edge.g * 2f && edge.g > edge.b * 3f, "Thermal silhouette retains amber colour");
            Check(interior.r > 0f && interior.r < edge.r * .1f, "Thermal interior remains faint");
            Check(Difference(background, phosphor) < .0001f, "Thermal mask leaves background phosphor unchanged");
            // Keep the populated mask bound: disabling heat must not expose stale contours.
            Shader.SetGlobalColor("_NvgHeatColor", Color.clear);
            Color[] disabled = RenderPixels(material, scene, 0);
            float disabledDifference = 0f;
            foreach (Color value in disabled) disabledDifference = Mathf.Max(disabledDifference, Difference(value, phosphor));
            Check(disabledDifference < .0001f, "Zero heat restores every phosphor pixel despite a stale populated mask");
            Shader.SetGlobalTexture("_BoscaliNvgHeatMask", Texture2D.blackTexture);
            Shader.SetGlobalColor("_NvgHeatColor", new Color(1f, .36f, .04f, 1f));
            float emptyDifference = 0f;
            foreach (Color value in RenderPixels(material, scene, 0)) emptyDifference = Mathf.Max(emptyDifference, Difference(value, phosphor));
            Check(emptyDifference < .0001f, "An empty mask produces no heat tint or contour");
            return "thermal mask: edge=" + edge + "; interior=" + interior + "; disabled difference=" + disabledDifference.ToString("G6") + "\n";
        }
        finally { ClearHeat(); Object.DestroyImmediate(mask); }
    }

    private static void ClearHeat()
    {
        Shader.SetGlobalColor("_NvgHeatColor", Color.clear);
        Shader.SetGlobalTexture("_BoscaliNvgHeatMask", Texture2D.blackTexture);
    }

    private static Color Render(Material material, Color input, int pass) =>
        RenderPixels(material, input, pass)[ProbeSize / 2 * ProbeSize + ProbeSize / 2];

    private static Color[] RenderPixels(Material material, Color input, int pass)
    {
        var source = new Texture2D(ProbeSize, ProbeSize, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
        var pixels = new Color[ProbeSize * ProbeSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = input;
        source.SetPixels(pixels);
        source.Apply();
        var result = RenderTexture.GetTemporary(ProbeSize, ProbeSize, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var read = new Texture2D(ProbeSize, ProbeSize, TextureFormat.RGBAFloat, false, true);
        RenderTexture saved = RenderTexture.active;
        bool savedSrgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = false;
            Graphics.Blit(source, result, material, pass);
            RenderTexture.active = result;
            read.ReadPixels(new Rect(0, 0, ProbeSize, ProbeSize), 0, 0);
            read.Apply();
            Color[] output = read.GetPixels();
            bool finite = true;
            foreach (Color value in output) finite &= Finite(value);
            Check(finite, "Every rendered channel is finite");
            return output;
        }
        finally
        {
            RenderTexture.active = saved;
            GL.sRGBWrite = savedSrgb;
            Object.DestroyImmediate(read);
            Object.DestroyImmediate(source);
            RenderTexture.ReleaseTemporary(result);
        }
    }

    private static float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r),
        Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a))));
    private static float Luma(Color value) => .2126f * value.r + .7152f * value.g + .0722f * value.b;
    private static bool Finite(Color value) => !(float.IsNaN(value.r) || float.IsInfinity(value.r) || float.IsNaN(value.g) ||
        float.IsInfinity(value.g) || float.IsNaN(value.b) || float.IsInfinity(value.b) || float.IsNaN(value.a) || float.IsInfinity(value.a));
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
}
#endif
