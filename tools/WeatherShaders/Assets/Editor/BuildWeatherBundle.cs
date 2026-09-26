using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Batchmode entry point that builds the Weather module's shader bundle.
///
/// Run through tools/build-weather-shaders.ps1. The project is pinned to the game's editor
/// (2022.3.62) and URP (14.0.12); the bundle targets StandaloneWindows64 with D3D11 first
/// (what the game runs on) plus D3D12 and Vulkan for players who force another API. Every
/// shader in Assets/Shaders ships, and every shader is keyword-free, so nothing the game needs
/// can be stripped as an unused variant.
/// </summary>
public static class BuildWeatherBundle
{
    private const string BundleName = "boscali_weather";
    private const string ShaderFolder = "Assets/Shaders";
    private const string SettingsFolder = "Assets/Settings";

    public static void Build()
    {
        string output = GetArgument("-bundleOut") ?? Path.GetFullPath("Build");
        try
        {
            EnsurePipeline();

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[]
            {
                GraphicsDeviceType.Direct3D11,
                GraphicsDeviceType.Direct3D12,
                GraphicsDeviceType.Vulkan,
            });

            string[] shaders = AssetDatabase.FindAssets("t:Shader", new[] { ShaderFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            if (shaders.Length == 0)
                throw new InvalidOperationException("No shaders under " + ShaderFolder);

            foreach (string path in shaders)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
                foreach (ShaderMessage message in messages)
                {
                    Debug.Log($"[WeatherShaders] {path}:{message.line} {message.severity} {message.message}");
                }
                if (messages.Any(m => m.severity == ShaderCompilerMessageSeverity.Error))
                    throw new InvalidOperationException("Shader has errors: " + path);
                Debug.Log($"[WeatherShaders] include {shader.name} ({path})");
            }

            Directory.CreateDirectory(output);
            var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = shaders };
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                output,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
                throw new InvalidOperationException("BuildAssetBundles returned no manifest");

            Debug.Log($"[WeatherShaders] built {Path.Combine(output, BundleName)} with {shaders.Length} shader(s)");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[WeatherShaders] build failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// The URP shader preprocessor strips against the project's pipeline assets, so the build
    /// needs one assigned, as the game has. Created once and reused.
    /// </summary>
    private static void EnsurePipeline()
    {
        string assetPath = SettingsFolder + "/WeatherURP.asset";
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
        if (asset == null)
        {
            Directory.CreateDirectory(SettingsFolder);
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, SettingsFolder + "/WeatherRenderer.asset");
            asset = UniversalRenderPipelineAsset.Create(renderer);
            asset.supportsCameraDepthTexture = true;
            asset.supportsCameraOpaqueTexture = true;
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
        }
        GraphicsSettings.defaultRenderPipeline = asset;
        QualitySettings.renderPipeline = asset;
    }

    private static string GetArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }
}
