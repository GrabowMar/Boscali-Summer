using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class BuildSceneryInstancing
{
    private const string ShaderPath = "Assets/Map/SceneryInstancing/MapSceneryInstanced.shader";
    private const string BundleName = "scenery-instancing.bundle";
    private const string Output = "BundleOutput";

    public static void Run()
    {
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            ConfigurePipeline();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("The scenery shader did not import cleanly.");
            Directory.CreateDirectory(Output);
            var assets = new List<string> { ShaderPath };
            foreach (string foliage in new[] { "PalmFoliage", "ScrubFoliage" })
            {
                string path = $"Assets/Map/SceneryInstancing/{foliage}.png";
                ImportFoliage(path);
                assets.Add(path);
            }
            var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = assets.ToArray() };
            const BuildAssetBundleOptions options = BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ChunkBasedCompression;
            if (BuildPipeline.BuildAssetBundles(Output, new[] { build }, options, BuildTarget.StandaloneWindows64) == null || ShaderUtil.ShaderHasError(shader))
                throw new Exception("The scenery shader bundle did not build cleanly.");
            File.WriteAllText("result.txt", $"PASS: {BundleName} ({new FileInfo(Path.Combine(Output, BundleName)).Length} bytes)");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }

    private static void ImportFoliage(string path)
    {
        var texture = (TextureImporter)AssetImporter.GetAtPath(path);
        if (texture == null) throw new FileNotFoundException("Foliage atlas was not imported.", path);
        texture.sRGBTexture = true;
        texture.alphaSource = TextureImporterAlphaSource.FromInput;
        texture.alphaIsTransparency = true;
        texture.mipmapEnabled = true;
        texture.mipMapsPreserveCoverage = true;
        texture.alphaTestReferenceValue = .42f;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Trilinear;
        texture.maxTextureSize = 512;
        texture.SaveAndReimport();
    }

    private static void ConfigurePipeline()
    {
        const string rendererPath = "Assets/BuildRenderer.asset", pipelinePath = "Assets/BuildPipeline.asset";
        UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, rendererPath);
        }
        UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, pipelinePath);
        }
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;

        // The live game controls fog and shadow variants, so keep them in the scratch bake.
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        graphics.FindProperty("m_FogStripping").intValue = 1;
        foreach (string mode in new[] { "Linear", "Exp", "Exp2" }) graphics.FindProperty("m_FogKeep" + mode).boolValue = true;
        graphics.ApplyModifiedPropertiesWithoutUndo();
        const string globalPath = "Assets/BuildGlobalSettings.asset";
        RenderPipelineGlobalSettings global = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(globalPath);
        if (global == null)
        {
            Type type = typeof(UniversalRenderPipelineAsset).Assembly.GetType(
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings", true);
            global = (RenderPipelineGlobalSettings)ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(global, globalPath);
        }
        var settings = new SerializedObject(global);
        settings.FindProperty("m_StripUnusedVariants").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.RegisterRenderPipelineSettings<UniversalRenderPipeline>(global);
        AssetDatabase.SaveAssets();
    }
}
