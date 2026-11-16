#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Runs the production decision callback with real Unity camera identities and the installed game's SSAO type.
public static class RenderTuningUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Type Service = Type.GetType("BoscaliSummer.Modules.Performance.Runtime.RenderTuning, BoscaliSummer", true);
    private static readonly Type Patch = Service.Assembly.GetType("BoscaliSummer.Modules.Performance.Runtime.ReliefAmbientOcclusionPatch", true);
    private static int assertions;

    public static void Run()
    {
        try
        {
            SetExecutablePath();
            Cases();
            File.WriteAllText("result.txt", "PASS: " + assertions + " production render-tuning assertions. Native SSAO target/signature; exact camera identity; default-off, AfterOpaque/missing-settings fallback, replacement/unregistration/destruction, disable/reset/re-enable/teardown, and shared policy lifetime. Offline editor check only; no game, rendered image or performance acceptance.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error + "\n");
            EditorApplication.Exit(1);
        }
    }

    private static void Cases()
    {
        Check(!Application.isBatchMode && !GameManager.IsHeadless, "non-headless editor required to exercise opt-in client paths");
        var owner = new GameObject("Render tuning check");
        Camera relief = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        Camera main = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        Camera sensor = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        Camera replacement = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        main.tag = "MainCamera";
        relief.orthographic = sensor.orthographic = replacement.orthographic = true;
        var target = (MethodInfo)Patch.GetMethod("TargetMethod", All).Invoke(null, null);
        Check(target != null && target.DeclaringType.Assembly == typeof(UniversalRenderer).Assembly &&
            target.DeclaringType.FullName == "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion" &&
            target.GetParameters()[0].ParameterType == typeof(ScriptableRenderer) &&
            target.GetParameters()[1].ParameterType == typeof(RenderingData).MakeByRefType(), "exact installed native SSAO callback");
        Check((bool)Patch.GetMethod("Prepare", All).Invoke(null, null), "native settings contract accepted");
        var feature = ScriptableObject.CreateInstance(target.DeclaringType);
        FieldInfo settingsField = target.DeclaringType.GetField("m_Settings", All);
        object aoSettings = settingsField.GetValue(feature);
        FieldInfo afterOpaque = aoSettings.GetType().GetField("AfterOpaque", All);
        afterOpaque.SetValue(aoSettings, false);
        bool activeBefore = ((ScriptableRendererFeature)feature).isActive;
        MonoBehaviour service = null;
        try
        {
            service = (MonoBehaviour)owner.AddComponent(Service);
            var config = new ConfigFile(Path.GetFullPath("render-tuning.cfg"), false) { SaveOnConfigSet = false };
            Type settingsType = Service.Assembly.GetType("BoscaliSummer.Modules.Performance.Configuration.PerformanceSettings", true);
            object settings = Activator.CreateInstance(settingsType, All, null, new object[] { config }, null);
            Call(service, "Configure", settings);
            Call(null, "RegisterReliefCamera", relief);
            Check(Get<bool>("ReliefCameraRegistered"), "owner explicitly registered");
            Check(!Entry<bool>(settings, "SkipReliefAmbientOcclusion").Value &&
                !Get<bool>("IndependentSharpening") && !Get<bool>("OptimizeCinematicStreaks"), "render tunings default off");
            Check(NativeAllowed(feature, relief), "default-off keeps native callback");
            Entry<bool>(settings, "SkipReliefAmbientOcclusion").Value = true;
            Check(!NativeAllowed(feature, relief), "registered unlit relief skips lighting AO");
            int seen = Get<int>("ReliefAoCalls"), skipped = Get<int>("ReliefAoSkipped");
            Check(NativeAllowed(feature, main), "main camera preserved");
            Check(NativeAllowed(feature, sensor), "same-name orthographic sensor preserved");
            Check(NativeAllowed(feature, null), "null camera preserved");
            Check(Get<int>("ReliefAoCalls") == seen && Get<int>("ReliefAoSkipped") == skipped, "other cameras do not affect relief counters");

            afterOpaque.SetValue(aoSettings, true);
            Check(NativeAllowed(feature, relief), "after-opaque image multiplication preserved");
            Check((bool)afterOpaque.GetValue(aoSettings), "native setting untouched");
            afterOpaque.SetValue(aoSettings, false);
            settingsField.SetValue(feature, null);
            Check(NativeAllowed(feature, relief), "missing settings keeps native callback");
            settingsField.SetValue(feature, aoSettings);
            Check(NativeAllowed(null, relief), "missing feature keeps native callback");
            Entry<bool>(settings, "SkipReliefAmbientOcclusion").Value = false;
            Check(NativeAllowed(feature, relief), "switch-off takes effect immediately");
            Entry<bool>(settings, "SkipReliefAmbientOcclusion").Value = true;

            Call(null, "RegisterReliefCamera", replacement);
            Call(null, "UnregisterReliefCamera", relief);
            Check(!NativeAllowed(feature, replacement), "late old-camera release cannot unregister its replacement");
            Check(NativeAllowed(feature, relief), "old camera no longer classified as relief");
            Object.DestroyImmediate(replacement.gameObject);
            Check(!Get<bool>("ReliefCameraRegistered") && NativeAllowed(feature, replacement), "destroyed Unity camera cannot qualify");
            Call(null, "UnregisterReliefCamera", replacement);
            Call(null, "RegisterReliefCamera", relief);

            Entry<bool>(settings, "IndependentSharpening").Value = true;
            Entry<bool>(settings, "OptimizeCinematicStreaks").Value = true;
            Entry<float>(settings, "IndependentSharpenStrength").Value = .37f;
            Check(Get<bool>("IndependentSharpening") && Get<bool>("OptimizeCinematicStreaks") &&
                Mathf.Abs(Get<float>("SharpenStrength") - .37f) < .00001f, "shared opt-ins and strength read active settings");
            service.enabled = false;
            Call(service, "OnDisable");
            Check(NativeAllowed(feature, relief) && !Get<bool>("IndependentSharpening") &&
                !Get<bool>("OptimizeCinematicStreaks") && Get<float>("SharpenStrength") == 0f, "disabled service preserves native calls and stops all overrides");
            Call(service, "ResetForScene");
            Check(NativeAllowed(feature, relief) && Get<int>("ReliefAoCalls") == 0 && Get<int>("ReliefAoSkipped") == 0,
                "rollback scene reset cannot reactivate a disabled service");
            service.enabled = true;
            Call(service, "OnEnable");
            Check(!NativeAllowed(feature, relief), "re-enable retains the current relief registration");
            Call(null, "UnregisterReliefCamera", relief);
            Check(NativeAllowed(feature, relief), "release unregisters immediately before deferred destruction");
            Call(null, "RegisterReliefCamera", relief);
            Call(service, "OnDestroy");
            Check(NativeAllowed(feature, relief) && !Get<bool>("IndependentSharpening") && !Get<bool>("OptimizeCinematicStreaks"), "teardown stops every override");
            Check(((ScriptableRendererFeature)feature).isActive == activeBefore && ReferenceEquals(settingsField.GetValue(feature), aoSettings) &&
                !(bool)afterOpaque.GetValue(aoSettings), "shared feature activation and settings never mutated");
        }
        finally
        {
            Call(null, "UnregisterReliefCamera", relief);
            if (service != null) Call(service, "OnDestroy");
            Object.DestroyImmediate(feature);
            Object.DestroyImmediate(owner);
            if (relief != null) Object.DestroyImmediate(relief.gameObject);
            if (main != null) Object.DestroyImmediate(main.gameObject);
            if (sensor != null) Object.DestroyImmediate(sensor.gameObject);
            if (replacement != null) Object.DestroyImmediate(replacement.gameObject);
        }
    }

    private static bool NativeAllowed(object feature, Camera camera)
    {
        var data = new RenderingData { cameraData = new CameraData { camera = camera } };
        return (bool)Patch.GetMethod("Prefix", All).Invoke(null, new object[] { feature, data });
    }

    private static ConfigEntry<T> Entry<T>(object settings, string name) => (ConfigEntry<T>)settings.GetType().GetProperty(name, All).GetValue(settings);
    private static T Get<T>(string property) => (T)Service.GetProperty(property, All).GetValue(null);
    private static object Call(object instance, string method, params object[] args) => Service.GetMethod(method, All).Invoke(instance, args);
    private static void Check(bool condition, string description)
    {
        assertions++;
        if (!condition) throw new Exception(description);
    }

    private static void SetExecutablePath()
    {
        MethodInfo method = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", All);
        ParameterInfo[] parameters = method.GetParameters();
        var args = new object[parameters.Length];
        args[0] = Path.GetFullPath("RenderTuningCheck.exe");
        for (int i = 1; i < args.Length; i++) args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
        method.Invoke(null, args);
    }
}
#endif
