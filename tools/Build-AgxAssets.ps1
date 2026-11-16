param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$ProjectDirectory,
    [switch]$PublicOnly,
    [switch]$SkipRenderCheck
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UnityCheck.Common.ps1"
if (-not $ProjectDirectory) { $ProjectDirectory = Join-Path $repo '.nomodkit/agx-assets' }
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
New-UnityCheckProject $ProjectDirectory '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}' -Folders 'Assets/Agx', 'Assets/Editor', 'BundleOutput'
if ($PublicOnly) {
    Copy-Item -Path "$repo/modules/Effects/Assets/Source/CinematicStreak.shader*" -Destination "$ProjectDirectory/Assets/Agx/" -Force
} else {
    Copy-Item -Path "$repo/modules/Effects/Assets/Source/*" -Destination "$ProjectDirectory/Assets/Agx/" -Recurse -Force
}
Copy-Item -Path "$repo/tools/AgxAssets/StreakParityCheck.cs", "$repo/tools/AgxAssets/StreakParity.shader" -Destination "$ProjectDirectory/Assets/Editor/" -Force
Set-Content -LiteralPath "$ProjectDirectory/Assets/Editor/BuildAgxBundle.cs" -Value @'
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildAgxBundle
{
    public static void Run()
    {
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            bool publicOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-publicEffectsOnly") >= 0;
            var names = publicOnly ? new string[0] : new[] { "agx-base", "agx-punchy", "agx-hue" };
            var paths = new string[names.Length + 2];
            paths[0] = "Assets/Agx/AgxLutComposite.shader";
            paths[paths.Length - 1] = "Assets/Agx/CinematicStreak.shader";
            if (publicOnly) paths = new[] { "Assets/Agx/CinematicStreak.shader" };
            Shader shader = publicOnly ? null : AssetDatabase.LoadAssetAtPath<Shader>(paths[0]);
            if (!publicOnly && shader == null) throw new Exception("AgX shader was not imported");
            if (shader != null && ShaderUtil.ShaderHasError(shader))
            {
                foreach (var message in ShaderUtil.GetShaderMessages(shader)) Debug.LogError(message.message);
                throw new Exception("AgX shader compilation failed");
            }
            var material = shader != null ? new Material(shader) : null;
            string samples = "";
            for (int i = 0; i < names.Length; i++)
            {
                paths[i + 1] = "Assets/Agx/" + names[i] + ".exr";
                // Keep the original 3D slice layout and sRGB import setting. Disable
                // platform compression so each look retains its half-float precision.
                var importer = (TextureImporter)AssetImporter.GetAtPath(paths[i + 1]);
                importer.textureShape = TextureImporterShape.Texture3D;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var platform = importer.GetPlatformTextureSettings("Standalone");
                platform.overridden = true;
                platform.format = TextureImporterFormat.RGBAHalf;
                platform.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
                var lut = AssetDatabase.LoadAssetAtPath<Texture3D>(paths[i + 1]);
                if (lut == null || lut.width != 32 || lut.height != 32 || lut.depth != 32)
                    throw new Exception("Invalid 32-cubed LUT: " + paths[i + 1]);
                material.SetTexture("_AgxLut", lut);
                samples += names[i] + " " + lut.graphicsFormat + " dataSRGB=" + lut.isDataSRGB + " " + Check(material) + "\n";
                if (i == 0)
                {
                    // A non-sRGB import is a control. These HDR EXRs produce the
                    // same linear samples with either setting; do not decode twice.
                    importer.sRGBTexture = false;
                    importer.SaveAndReimport();
                    material.SetTexture("_AgxLut", AssetDatabase.LoadAssetAtPath<Texture3D>(paths[i + 1]));
                    samples += "agx-base non-sRGB control " + Check(material) + "\n";
                    importer.sRGBTexture = true;
                    importer.SaveAndReimport();
                }
            }
            UnityEngine.Object.DestroyImmediate(material);
            var streakShader = AssetDatabase.LoadAssetAtPath<Shader>(paths[paths.Length - 1]);
            if (streakShader == null || ShaderUtil.ShaderHasError(streakShader))
            {
                if (streakShader != null)
                    foreach (var message in ShaderUtil.GetShaderMessages(streakShader)) Debug.LogError(message.message);
                throw new Exception("Cinematic streak shader compilation failed");
            }
            var streakMaterial = new Material(streakShader);
            samples += CheckStreak(streakMaterial) + "\n";
            UnityEngine.Object.DestroyImmediate(streakMaterial);
            string bundleName = publicOnly ? "cinematic.bundle" : "agx.bundle";
            var build = new AssetBundleBuild { assetBundleName = bundleName, assetNames = paths };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64) == null) throw new Exception("AgX bundle build failed");
            if ((shader != null && ShaderUtil.ShaderHasError(shader)) || ShaderUtil.ShaderHasError(streakShader))
                throw new Exception("Effects shader compilation failed");
            // Exercise the player bundle: successful editor compilation alone does not
            // prove that the shipped shader retained executable passes after stripping.
            var bundle = AssetBundle.LoadFromFile(Path.GetFullPath("BundleOutput/" + bundleName));
            if (bundle == null) throw new Exception("Built AgX bundle could not be loaded");
            try
            {
                if (publicOnly && (bundle.GetAllAssetNames().Length != 1 || bundle.LoadAllAssets<Texture>().Length != 0))
                    throw new Exception("Public effects bundle contains assets other than the original streak shader");
                var builtShader = bundle.LoadAsset<Shader>("Assets/Agx/CinematicStreak.shader");
                if (builtShader == null) throw new Exception("Built streak shader missing");
                var builtMaterial = new Material(builtShader);
                try { samples += StreakParityCheck.Run(builtMaterial); }
                finally { UnityEngine.Object.DestroyImmediate(builtMaterial); }
            }
            finally { bundle.Unload(true); }
            File.WriteAllText("result.txt", (publicOnly
                ? "PASS: public cinematic bundle contains only the original streak shader; no third-party LUTs; streak shader checks below.\n"
                : "PASS: shaders compiled; three 32-cubed LUTs bundled; finite, monotonic HDR grey ramp; streak shader checks below.\n") + samples);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }

    private static string CheckStreak(Material material)
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-skipAgxRenderCheck") >= 0)
            return "streak render check skipped";
        const int width = 256, height = 64;
        var source = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        var pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.5f, 0.5f, 0.5f, 1f);
        // The central source is warm; preserve the source's hue in the flare.
        for (int y = height / 2 - 2; y < height / 2 + 2; y++)
            for (int x = width / 2 - 2; x < width / 2 + 2; x++)
                pixels[y * width + x] = new Color(32f, 16f, 8f, 1f);
        source.SetPixels(pixels);
        source.Apply();
        var extract = RenderTexture.GetTemporary(width / 4, height / 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var blur = RenderTexture.GetTemporary(width / 4, height / 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var result = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        extract.filterMode = blur.filterMode = FilterMode.Bilinear;
        bool savedSrgb = GL.sRGBWrite;
        GL.sRGBWrite = false;
        material.SetVector("_StreakParams", new Vector4(4f, 32f, 0.5f, 1.5f));
        Graphics.Blit(source, extract, material, 0);
        Graphics.Blit(extract, blur, material, 1);
        Shader.SetGlobalTexture("_BoscaliStreakTex", blur);
        Graphics.Blit(source, result, material, 2);
        var read = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        RenderTexture saved = RenderTexture.active;
        RenderTexture.active = result;
        read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        read.Apply();
        Color horizontal = read.GetPixel(width / 2 + 18, height / 2);
        Color vertical = read.GetPixel(width / 2, height / 2 + 18);
        Color corner = read.GetPixel(0, 0);
        Color center = read.GetPixel(width / 2, height / 2);
        if (!(horizontal.r > 0.51f && horizontal.r > horizontal.g && horizontal.g > horizontal.b &&
            Mathf.Abs(vertical.r - 0.5f) < 0.001f && Mathf.Abs(corner.r - 0.5f) < 0.001f && center.r >= 32f))
            throw new Exception("Invalid horizontal streak: horizontal=" + horizontal + "; vertical=" + vertical + "; corner=" + corner);
        foreach (Color value in read.GetPixels())
            if (float.IsNaN(value.r) || float.IsInfinity(value.r) || value.r < 0.499f)
                throw new Exception("Streak changed scene brightness or produced non-finite output");
        material.SetVector("_StreakParams", new Vector4(4f, 32f, 0f, 1.5f));
        Graphics.Blit(source, result, material, 2);
        read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        read.Apply();
        if (Mathf.Abs(read.GetPixel(width / 2 + 18, height / 2).r - 0.5f) > 0.001f)
            throw new Exception("Zero streak strength did not restore the original image");
        RenderTexture.active = saved;
        GL.sRGBWrite = savedSrgb;
        UnityEngine.Object.DestroyImmediate(source);
        UnityEngine.Object.DestroyImmediate(read);
        RenderTexture.ReleaseTemporary(result);
        RenderTexture.ReleaseTemporary(blur);
        RenderTexture.ReleaseTemporary(extract);
        return "streak PASS: horizontal flare retains warm source hue; no vertical/corner haze; finite output; scene brightness retained; zero strength restores source";
    }

    private static string Check(Material material)
    {
        if (Environment.GetCommandLineArgs().Length > 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-skipAgxRenderCheck") >= 0)
            return "render check skipped";
        const int count = 258;
        var source = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true);
        for (int i = 0; i < 256; i++)
        {
            float value = i == 0 ? 0f : Mathf.Pow(2f, -12f + (i - 1) * 18f / 254f);
            source.SetPixel(i, 0, new Color(value, value, value, 1f));
        }
        source.SetPixel(256, 0, new Color(0.18f, 0.18f, 0.18f, 1f));
        source.SetPixel(257, 0, Color.white);
        source.Apply();
        var target = RenderTexture.GetTemporary(count, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        bool savedSrgb = GL.sRGBWrite;
        GL.sRGBWrite = false;
        Graphics.Blit(source, target, material);
        GL.sRGBWrite = savedSrgb;
        RenderTexture saved = RenderTexture.active;
        RenderTexture.active = target;
        var read = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, count, 1), 0, 0);
        read.Apply();
        RenderTexture.active = saved;
        Color[] values = read.GetPixels();
        float last = -1f;
        for (int i = 0; i < 256; i++)
        {
            Color value = values[i];
            if (float.IsNaN(value.r) || float.IsInfinity(value.r) || value.r < -0.001f || value.r > 1.001f || value.r < last - 0.002f)
                throw new Exception("Invalid AgX ramp at " + i + ": " + value);
            last = value.r;
        }
        if (values[0].r > 0.01f || values[255].r < 0.8f || values[128].r <= values[0].r)
            throw new Exception("AgX ramp endpoints or dynamic range invalid");
        string report = "black=" + values[0].r.ToString("G6") + ", grey18=" + values[256].r.ToString("G6")
            + ", one=" + values[257].r.ToString("G6") + ", white=" + values[255].r.ToString("G6");
        UnityEngine.Object.DestroyImmediate(source);
        UnityEngine.Object.DestroyImmediate(read);
        RenderTexture.ReleaseTemporary(target);
        return report;
    }
}
'@
$arguments = @()
if ($PublicOnly) { $arguments += '-publicEffectsOnly' }
if ($SkipRenderCheck) { $arguments += '-skipAgxRenderCheck' }
$process = Invoke-UnityCheck $Unity $ProjectDirectory 'BuildAgxBundle.Run' -ExtraArguments $arguments -TimeoutSeconds 240
Show-UnityCheckResult $ProjectDirectory
Assert-UnityResult $ProjectDirectory $process -ExitMessage "AgX build failed: $($process.ExitCode)"
$bundleName = if ($PublicOnly) { 'cinematic.bundle' } else { 'agx.bundle' }
Copy-Item -LiteralPath "$ProjectDirectory/BundleOutput/$bundleName" -Destination "$repo/modules/Effects/Assets/$bundleName" -Force
Write-Output "Effects bundle: $repo/modules/Effects/Assets/$bundleName"
