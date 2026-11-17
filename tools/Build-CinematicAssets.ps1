param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$ProjectDirectory
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UnityCheck.Common.ps1"
if (-not $ProjectDirectory) { $ProjectDirectory = Join-Path $repo '.nomodkit/cinematic-assets' }
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
New-UnityCheckProject $ProjectDirectory '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}' -Folders 'Assets/Cinematic', 'Assets/Editor', 'BundleOutput'
Copy-Item -Path "$repo/modules/Effects/Assets/Source/CinematicStreak.shader*" -Destination "$ProjectDirectory/Assets/Cinematic/" -Force
Copy-Item -Path "$repo/tools/CinematicAssets/StreakParityCheck.cs", "$repo/tools/CinematicAssets/StreakParity.shader" -Destination "$ProjectDirectory/Assets/Editor/" -Force
Set-Content -LiteralPath "$ProjectDirectory/Assets/Editor/BuildCinematicBundle.cs" -Value @'
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildCinematicBundle
{
    public static void Run()
    {
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            const string shaderPath = "Assets/Cinematic/CinematicStreak.shader";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new Exception("Cinematic streak shader failed to import or compile");
            string samples;
            var material = new Material(shader);
            try { samples = CheckStreak(material) + "\n"; }
            finally { UnityEngine.Object.DestroyImmediate(material); }
            var build = new AssetBundleBuild { assetBundleName = "cinematic.bundle", assetNames = new[] { shaderPath } };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64) == null) throw new Exception("Cinematic bundle build failed");
            if (ShaderUtil.ShaderHasError(shader)) throw new Exception("Cinematic streak shader compilation failed");
            // Check the bundled player shader after stripping, not only the Editor asset.
            var bundle = AssetBundle.LoadFromFile(Path.GetFullPath("BundleOutput/cinematic.bundle"));
            if (bundle == null) throw new Exception("Built cinematic bundle could not be loaded");
            try
            {
                if (bundle.GetAllAssetNames().Length != 1 || bundle.LoadAllAssets<Texture>().Length != 0)
                    throw new Exception("Cinematic bundle must contain only its original streak shader");
                var builtShader = bundle.LoadAsset<Shader>(shaderPath);
                if (builtShader == null) throw new Exception("Built streak shader missing");
                var builtMaterial = new Material(builtShader);
                try { samples += StreakParityCheck.Run(builtMaterial); }
                finally { UnityEngine.Object.DestroyImmediate(builtMaterial); }
            }
            finally { bundle.Unload(true); }
            File.WriteAllText("result.txt", "PASS: cinematic bundle contains one original streak shader and no textures.\n" + samples);
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

}
'@
$process = Invoke-UnityCheck $Unity $ProjectDirectory 'BuildCinematicBundle.Run' -TimeoutSeconds 240
Show-UnityCheckResult $ProjectDirectory
Assert-UnityResult $ProjectDirectory $process -ExitMessage "Cinematic build failed: $($process.ExitCode)"
Copy-Item -LiteralPath "$ProjectDirectory/BundleOutput/cinematic.bundle" -Destination "$repo/modules/Effects/Assets/cinematic.bundle" -Force
Write-Output "Cinematic bundle: $repo/modules/Effects/Assets/cinematic.bundle"