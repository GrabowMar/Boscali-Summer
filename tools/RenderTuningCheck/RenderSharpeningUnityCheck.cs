#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Exercises the compiled production DisplayGrade/RenderTuning path, not a copied resolver.
// No camera or mission is rendered: this establishes configuration, ownership and restoration.
public static class RenderSharpeningUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Type GradeType = Type.GetType("BoscaliSummer.Modules.Effects.Runtime.DisplayGrade, BoscaliSummer", true);
    private static readonly Type TuningType = GradeType.Assembly.GetType("BoscaliSummer.Modules.Performance.Runtime.RenderTuning", true);
    private static int assertions;

    public static void Run()
    {
        bool passed = false;
        string result;
        try
        {
            SetExecutablePath();
            Check(!Application.isBatchMode, "client policy is exercised outside batch mode");
            Cases();
            passed = true;
            result = "PASS: " + assertions + " production sharpening assertions; unchanged defaults, independent grade-off RCAS, strength precedence, legacy grade controls, immediate night-vision bypass, toggle/module disable, scene reset, pipeline replacement/loss, and repeated teardown. Offline Unity settings/lifecycle check; no rendered quality or FPS claim.\n";
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            result = "FAIL: " + error + "\n";
        }
        File.WriteAllText("result.txt", result);
        Debug.Log(result);
        EditorApplication.Exit(passed ? 0 : 1);
    }

    private static void Cases()
    {
        RenderPipelineAsset graphics = GraphicsSettings.renderPipelineAsset, quality = QualitySettings.renderPipeline;
        var first = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
        var replacement = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
        var root = new GameObject("Independent sharpening lifecycle check");
        Type registryType = GradeType.Assembly.GetType("BoscaliSummer.Core.Services.ServiceRegistry", true);
        PropertyInfo activeRegistry = GradeType.Assembly.GetType("BoscaliSummer.Core.Services.ModuleServices", true).GetProperty("Active", All);
        object previousRegistry = activeRegistry.GetValue(null);
        MonoBehaviour grade = null;
        try
        {
            object registry = Activator.CreateInstance(registryType, true);
            activeRegistry.SetValue(null, registry);
            SetBaseline(first, UpscalingFilterSelection.Point, false, 0.73f);
            SetBaseline(replacement, UpscalingFilterSelection.Linear, true, 0.19f);
            var firstBaseline = new Snapshot(first);
            var replacementBaseline = new Snapshot(replacement);
            GraphicsSettings.renderPipelineAsset = QualitySettings.renderPipeline = first;

            var config = new ConfigFile(Path.GetFullPath("render-sharpening.cfg"), false) { SaveOnConfigSet = false };
            object effects = Settings("Effects", config);
            object performance = Settings("Performance", config);
            Check(!Entry<bool>(effects, "Enabled").Value && Entry<bool>(effects, "SharpenEnabled").Value,
                "legacy grade/sharpen defaults remain unchanged");
            Check(!Entry<bool>(performance, "IndependentSharpening").Value &&
                Entry<float>(performance, "IndependentSharpenStrength").Value == 0.4f,
                "independent sharpening defaults off at documented strength");

            grade = (MonoBehaviour)root.AddComponent(GradeType);
            var tuning = (MonoBehaviour)root.AddComponent(TuningType);
            Call(grade, "Configure", effects, null);
            Call(tuning, "Configure", performance);
            Tick(grade);
            firstBaseline.Check("default-off leaves all three pipeline values unchanged");

            Entry<bool>(performance, "IndependentSharpening").Value = true;
            Entry<float>(performance, "IndependentSharpenStrength").Value = 0.25f;
            Tick(grade);
            firstBaseline.Check("an absent render-tuning service leaves independent sharpening off");
            Type renderContract = GradeType.Assembly.GetType("BoscaliSummer.Core.Contracts.IRenderTuning", true);
            registryType.GetMethod("Add", All).MakeGenericMethod(renderContract).Invoke(registry, new object[] { tuning });
            Tick(grade);
            CheckSharpen(first, 0.25f, "independent sharpening applies with colour grade off");
            Check(GradeType.GetField("holder", All).GetValue(grade) == null,
                "independent sharpening does not construct a colour-grade volume");
            Tick(grade);
            Entry<float>(performance, "IndependentSharpenStrength").Value = 0.65f;
            Tick(grade);
            CheckSharpen(first, 0.65f, "strength changes apply without recapturing the baseline");
            CheckNightVision(grade, first, firstBaseline);

            Entry<bool>(effects, "Enabled").Value = true;
            Entry<float>(effects, "SharpenStrength").Value = 0.85f;
            Tick(grade);
            CheckSharpen(first, 0.65f, "independent strength takes precedence while both controls are on");
            Entry<bool>(performance, "IndependentSharpening").Value = false;
            Tick(grade);
            CheckSharpen(first, 0.85f, "legacy grade sharpening resumes when independent control is off");
            Entry<bool>(effects, "SharpenEnabled").Value = false;
            Tick(grade);
            firstBaseline.Check("legacy sharpen off restores exact originals");
            Entry<bool>(effects, "Enabled").Value = false;
            Entry<bool>(performance, "IndependentSharpening").Value = true;
            Tick(grade);
            CheckSharpen(first, 0.65f, "independent control also works with legacy sharpen disabled");

            tuning.enabled = false;
            Tick(grade);
            firstBaseline.Check("disabling Performance removes its sharpening request on the next settings tick");
            Call(tuning, "ResetForScene");
            Tick(grade);
            firstBaseline.Check("disabled service cannot reapply during rollback reset");
            tuning.enabled = true;
            Call(tuning, "Configure", performance);
            Tick(grade);
            CheckSharpen(first, 0.65f, "reinstalling Performance reacquires the original pipeline");

            Call(grade, "ResetForScene");
            firstBaseline.Check("scene reset immediately restores sharpening");
            Tick(grade);
            CheckSharpen(first, 0.65f, "next scene tick can reapply the active request");

            GraphicsSettings.renderPipelineAsset = QualitySettings.renderPipeline = replacement;
            Tick(grade);
            firstBaseline.Check("pipeline replacement restores the previous asset");
            CheckSharpen(replacement, 0.65f, "pipeline replacement captures and sharpens the new asset");
            Entry<bool>(performance, "IndependentSharpening").Value = false;
            Tick(grade);
            replacementBaseline.Check("replacement asset restores its own original values");

            Entry<bool>(performance, "IndependentSharpening").Value = true;
            Tick(grade);
            GraphicsSettings.renderPipelineAsset = QualitySettings.renderPipeline = null;
            Tick(grade);
            replacementBaseline.Check("losing the pipeline restores the owned asset");
            GraphicsSettings.renderPipelineAsset = QualitySettings.renderPipeline = first;
            Tick(grade);
            grade.enabled = false;
            Call(grade, "OnDisable"); // Edit-mode fixtures drive lifecycle callbacks explicitly.
            firstBaseline.Check("disabling the Effects owner immediately restores sharpening");
            Call(grade, "ResetForScene");
            Call(grade, "ReleaseFx");
            Call(grade, "ReleaseFx");
            firstBaseline.Check("rollback reset and repeated release leave the restored values intact");
        }
        finally
        {
            if (grade != null) Call(grade, "ReleaseFx");
            Object.DestroyImmediate(root);
            activeRegistry.SetValue(null, previousRegistry);
            GraphicsSettings.renderPipelineAsset = graphics;
            QualitySettings.renderPipeline = quality;
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(replacement);
        }
    }

    private static void CheckNightVision(object grade, UniversalRenderPipelineAsset asset, Snapshot baseline)
    {
        Type access = GradeType.Assembly.GetType("BoscaliSummer.Core.Game.GameAccess", true);
        FieldInfo selected = access.GetField("nvSelectedRef", All), active = access.GetField("nvActiveRef", All);
        object originalSelected = selected.GetValue(null), originalActive = active.GetValue(null);
        NightVision original = NightVision.i;
        var root = new GameObject("Native night-vision state fixture");
        root.SetActive(false); // No native Start or Update: only its real state fields are exercised.
        try
        {
            NightVision nv = root.AddComponent<RenderSharpeningNightVision>();
            NightVision.i = nv;
            selected.SetValue(null, AccessTools.FieldRefAccess<NightVision, bool>("nightVisSelected"));
            active.SetValue(null, AccessTools.FieldRefAccess<NightVision, bool>("nightVisActive"));
            typeof(NightVision).GetField("nightVisActive", All).SetValue(nv, true);
            GradeType.GetField("nextApply", All).SetValue(grade, float.PositiveInfinity);
            Call(grade, "Update");
            baseline.Check("night vision restores sharpening immediately before the next settings tick");
            Tick(grade);
            baseline.Check("active night vision cannot reapply sharpening on settings ticks");
            typeof(NightVision).GetField("nightVisActive", All).SetValue(nv, false);
            Tick(grade);
            CheckSharpen(asset, 0.65f, "goggles-off resumes independent sharpening");
        }
        finally
        {
            NightVision.i = original;
            selected.SetValue(null, originalSelected);
            active.SetValue(null, originalActive);
            Object.DestroyImmediate(root);
        }
    }

    private static object Settings(string module, ConfigFile config) => Activator.CreateInstance(
        GradeType.Assembly.GetType("BoscaliSummer.Modules." + module + ".Configuration." + module + "Settings", true),
        All, null, new object[] { config }, null);

    private static void SetBaseline(UniversalRenderPipelineAsset asset, UpscalingFilterSelection filter, bool sharpnessOverride, float strength)
    {
        asset.upscalingFilter = filter;
        asset.fsrOverrideSharpness = sharpnessOverride;
        asset.fsrSharpness = strength;
    }

    private sealed class Snapshot
    {
        private readonly UniversalRenderPipelineAsset asset;
        private readonly UpscalingFilterSelection filter;
        private readonly bool sharpnessOverride;
        private readonly float strength;

        public Snapshot(UniversalRenderPipelineAsset pipeline)
        {
            asset = pipeline;
            filter = pipeline.upscalingFilter;
            sharpnessOverride = pipeline.fsrOverrideSharpness;
            strength = pipeline.fsrSharpness;
        }

        public void Check(string message) => RenderSharpeningUnityCheck.Check(asset.upscalingFilter == filter &&
            asset.fsrOverrideSharpness == sharpnessOverride && asset.fsrSharpness == strength, message);
    }

    private static void CheckSharpen(UniversalRenderPipelineAsset asset, float strength, string message) =>
        Check(asset.upscalingFilter == UpscalingFilterSelection.FSR && asset.fsrOverrideSharpness &&
            Mathf.Approximately(asset.fsrSharpness, strength), message);
    private static void Tick(object grade)
    {
        GradeType.GetField("nextApply", All).SetValue(grade, -1f);
        Call(grade, "Update");
    }
    private static ConfigEntry<T> Entry<T>(object settings, string name) => (ConfigEntry<T>)settings.GetType().GetProperty(name, All).GetValue(settings);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, All).Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
    private static void SetExecutablePath()
    {
        MethodInfo method = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", All);
        ParameterInfo[] parameters = method.GetParameters();
        var args = new object[parameters.Length];
        args[0] = Path.GetFullPath("RenderSharpeningCheck.exe");
        for (int i = 1; i < args.Length; i++) args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
        method.Invoke(null, args);
    }
}

// Unity cannot attach scripts directly from a copied Assembly-CSharp DLL. A fixture
// subclass gives it an editor script identity while preserving the native state fields.
public sealed class RenderSharpeningNightVision : NightVision { }
#endif
