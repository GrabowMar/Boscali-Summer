#if UNITY_EDITOR
using System;
using System.IO;
using BoscaliSummer.Modules.Effects.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class CinematicAssetsUnityCheck
{
    private static int checks;

    public static void Run()
    {
        var assets = new CinematicAssets();
        RenderPipelineAsset graphics = GraphicsSettings.renderPipelineAsset, quality = QualitySettings.renderPipeline;
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        Tonemapping tone = profile.Add<Tonemapping>();
        bool passed = false;
        string result;
        try
        {
            pipeline.colorGradingMode = ColorGradingMode.LowDynamicRange;
            GraphicsSettings.renderPipelineAsset = QualitySettings.renderPipeline = pipeline;
            tone.mode.Override(TonemappingMode.ACES);
            int baseline = LoadedBundles();
            Shader first = assets.Streak(null);
            Check(first != null && first.name == "Hidden/Boscali/CinematicStreak" && first.isSupported && first.passCount == 4,
                "Embedded shipped shader loads with four supported passes");
            Check(LoadedBundles() == baseline + 1, "Exactly one bundle is owned");
            Check(ReferenceEquals(first, assets.Streak(null)) && LoadedBundles() == baseline + 1, "Repeated access uses cached shader and bundle");
            NativeUntouched(pipeline, tone);
            assets.Dispose();
            Check(LoadedBundles() == baseline && first == null, "Dispose unloads bundle and its shader");
            NativeUntouched(pipeline, tone);
            assets.Dispose();
            Check(LoadedBundles() == baseline, "Repeated disposal is safe");
            Shader second = assets.Streak(null);
            Check(second != null && second.isSupported && second.passCount == 4 && !ReferenceEquals(first, second),
                "Disposed loader reloads a fresh supported shader");
            Check(LoadedBundles() == baseline + 1, "Reload owns exactly one bundle");
            NativeUntouched(pipeline, tone);
            assets.Dispose();
            Check(LoadedBundles() == baseline && second == null, "Reloaded bundle and shader are released");
            NativeUntouched(pipeline, tone);
            result = "PASS: " + checks + " cinematic loader checks; shipped shader load/cache/dispose/reload; native tonemapping unchanged.";
            passed = true;
        }
        catch (Exception error) { result = "FAIL: " + error; Debug.LogException(error); }
        finally
        {
            assets.Dispose();
            GraphicsSettings.renderPipelineAsset = graphics;
            QualitySettings.renderPipeline = quality;
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(renderer);
        }
        File.WriteAllText("result.txt", result);
        EditorApplication.Exit(passed ? 0 : 1);
    }

    private static int LoadedBundles()
    {
        int count = 0;
        foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles()) count++;
        return count;
    }

    private static void NativeUntouched(UniversalRenderPipelineAsset pipeline, Tonemapping tone) =>
        Check(pipeline.colorGradingMode == ColorGradingMode.LowDynamicRange && tone.mode.value == TonemappingMode.ACES &&
            tone.mode.overrideState, "Native grading mode and tonemapping remain unchanged");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}
#endif
