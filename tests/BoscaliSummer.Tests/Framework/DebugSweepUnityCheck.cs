#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Runs the actual production lifecycle methods in an isolated editor, without a mission.
public static class DebugSweepUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Assembly Mod = Assembly.Load("BoscaliSummer");
    private static int checks;
    private static Type TypeOf(string name) => Mod.GetType("BoscaliSummer." + name, true);
    private static object Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, All).Invoke(instance, args);
    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        try
        {
            UnityCheckHarness.SetExecutablePath("DebugSweepCheck.exe");
            CheckPerformance();
            CheckWingTeardown();
            CheckSession();
            CheckRadioFade();
            CheckStorage();
            File.WriteAllText("result.txt", "PASS: " + checks + " production lifecycle/storage assertions. Isolated Unity editor; no mission or multiplayer acceptance.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckPerformance()
    {
        float lod = QualitySettings.lodBias, shadow = QualitySettings.shadowDistance;
        int frame = Application.targetFrameRate;
        var root = new GameObject("PerformanceLifecycleCheck");
        try
        {
            var service = root.AddComponent(TypeOf("Modules.Performance.Runtime.BaseGameTuning"));
            var config = new ConfigFile(Path.GetFullPath("lifecycle.cfg"), false) { SaveOnConfigSet = false };
            var settings = Activator.CreateInstance(TypeOf("Modules.Performance.Configuration.PerformanceSettings"), All, null, new object[] { config }, null);
            Call(service, "Configure", settings);
            for (int scene = 0; scene < 3; scene++)
            {
                QualitySettings.lodBias = 1f;
                QualitySettings.shadowDistance = 4000f;
                Application.targetFrameRate = -1;
                Call(service, "SyncLod", true); Call(service, "SyncShadow", true); Call(service, "SyncFrame", true);
                Check(QualitySettings.lodBias == 1.5f && QualitySettings.shadowDistance == 2000f && Application.targetFrameRate == 60,
                    "production knobs apply their planned values");
                // Batch mode does not reapply; the reset must still recover what it changed.
                Call(service, "ResetForScene");
                Check(QualitySettings.lodBias == 1f && QualitySettings.shadowDistance == 4000f && Application.targetFrameRate == -1,
                    "scene reset preserves the original graphics baseline");
            }
            Application.targetFrameRate = 30;
            Call(service, "SyncFrame", true);
            Check(Application.targetFrameRate == 30, "production cap preserves a lower frame limit");
            QualitySettings.lodBias = 1f; QualitySettings.shadowDistance = 4000f;
            Call(service, "SyncLod", true); Call(service, "SyncShadow", true);
            QualitySettings.lodBias = 2f; QualitySettings.shadowDistance = 1500f; Application.targetFrameRate = 45;
            Call(service, "OnDisable");
            Check(QualitySettings.lodBias == 2f && QualitySettings.shadowDistance == 1500f && Application.targetFrameRate == 45,
                "disable preserves settings changed by the game");
        }
        finally
        {
            Object.DestroyImmediate(root);
            QualitySettings.lodBias = lod; QualitySettings.shadowDistance = shadow; Application.targetFrameRate = frame;
        }
    }

    private static void CheckWingTeardown()
    {
        TypeOf("Modules.Wing.Runtime.WingLog").GetMethod("Init", All).Invoke(null, new object[] { new ManualLogSource("WingLifecycleCheck") });
        var root = new GameObject("WingLifecycleCheck");
        try
        {
            var manager = root.AddComponent(TypeOf("Modules.Wing.Runtime.WingManager"));
            // Ordinary MonoBehaviours do not receive play-mode callbacks in this editor fixture.
            Call(manager, "Awake");
            var plans = Activator.CreateInstance(TypeOf("Modules.Wing.Runtime.WingPlans"), true);
            var plan = plans.GetType().GetProperty("Plan", All).GetValue(plans);
            var runner = Activator.CreateInstance(TypeOf("Modules.Wing.Domain.PlanRunner"), All, null, new[] { plan }, null);
            plans.GetType().GetProperty("Runner", All).SetValue(plans, runner);
            Call(manager, "Register", plans);
            Call(runner, "Execute", 0f);
            var faulted = manager.GetType().GetField("faulted", All).GetValue(manager);
            Call(faulted, "Add", plans);
            manager.GetType().GetField("active", All).SetValue(manager, true);
            Call(manager, "EndMission");
            Check(!(bool)runner.GetType().GetProperty("Running", All).GetValue(runner), "faulted Wing service still deactivates");
            Call(runner, "Execute", 0f);
            manager.GetType().GetField("active", All).SetValue(manager, true);
            Type net = TypeOf("Modules.Wing.Networking.WingNet");
            net.GetField("hooked", All).SetValue(null, true);
            net.GetField("<HostGreeted>k__BackingField", All).SetValue(null, true);
            Call(manager, "OnDestroy");
            Object.DestroyImmediate(root);
            Check(!(bool)runner.GetType().GetProperty("Running", All).GetValue(runner), "destroying Wing manager ends its active mission");
            Check(!(bool)net.GetField("hooked", All).GetValue(null) && !(bool)net.GetProperty("HostGreeted", All).GetValue(null),
                "destroying Wing manager releases its transport and handshake state");
        }
        finally { if (root != null) Object.DestroyImmediate(root); }
    }

    private static void CheckStorage()
    {
        string path = Path.GetFullPath("atomic-lifecycle.json");
        MethodInfo write = TypeOf("Core.Storage.AtomicFile").GetMethod("WriteAllText", All);
        foreach (string value in new[] { "original", "replacement" })
        {
            object[] args = { path, value, null };
            Check((bool)write.Invoke(null, args) && File.ReadAllText(path) == value,
                "production Mono storage writes a complete save");
        }
    }

    private static void CheckSession()
    {
        var root = new GameObject("SessionLifecycleCheck");
        try
        {
            var session = root.AddComponent(TypeOf("Modules.Session.Networking.SessionNet"));
            var config = new ConfigFile(Path.GetFullPath("session-lifecycle.cfg"), false) { SaveOnConfigSet = true };
            var entry = config.Bind("Support", "CostMultiplier", 1f);
            Call(session, "Configure", new ConfigEntryBase[] { entry }, new ConfigEntryBase[0]);
            // A first snapshot chunk may contain only module switches or no known keys.
            Call(session, "BeginSession");
            Call(session, "BeginSession");
            Check(!config.SaveOnConfigSet, "session values do not save to the client config");
            Call(session, "EndSession", "empty snapshot check");
            Check(config.SaveOnConfigSet, "empty host snapshot still restores config saving on disconnect");
            Call(session, "BeginSession");
            Call(session, "BeginSession");
            object ledger = session.GetType().GetField("ledger", All).GetValue(session);
            object read = session.GetType().GetField("readLocal", All).GetValue(session);
            object write = session.GetType().GetField("writeLocal", All).GetValue(session);
            string key = "Support/CostMultiplier";
            // Use the exact key from the production allow-list, independent of separators.
            var allowed = (System.Collections.IDictionary)session.GetType().GetField("allowed", All).GetValue(session);
            foreach (object candidate in allowed.Keys) { key = (string)candidate; break; }
            Call(ledger, "Apply", key, "2", read, write);
            Check(entry.Value == 2f, "session ledger applies a host gameplay setting");
            Call(session, "EndSession", "chunked snapshot check");
            Check(entry.Value == 1f && config.SaveOnConfigSet,
                "a chunked host snapshot restores both the client value and its original saving preference");
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CheckRadioFade()
    {
        var root = new GameObject("RadioFadeCheck");
        var clip = AudioClip.Create("SyntheticSilentClip", 4800, 1, 48000, false);
        object program = null;
        try
        {
            var runner = (MonoBehaviour)root.AddComponent(TypeOf("Modules.Wing.Runtime.WingManager"));
            var current = root.AddComponent<AudioSource>();
            var incoming = root.AddComponent<AudioSource>();
            program = Activator.CreateInstance(TypeOf("Modules.Radio.Runtime.RadioProgram"), All, null,
                new object[] { runner, root, "SilentFadeCheck", new ManualLogSource("SilentFadeCheck") }, null);
            program.GetType().GetField("currentSource", All).SetValue(program, current);
            program.GetType().GetField("incomingSource", All).SetValue(program, incoming);
            program.GetType().GetField("incomingClip", All).SetValue(program, clip);
            Call(program, "SetVolume", 1f);
            var fade = (System.Collections.IEnumerator)Call(program, "CrossFadeToIncoming", true);
            Check(fade.MoveNext(), "production fade starts over two sources");
            Call(program, "SetVolume", 0f);
            Check(fade.MoveNext() && current.volume == 0f && incoming.volume == 0f,
                "mute during a fade silences both sources on its next step");
            // Advance the coroutine's clock deterministically without playing any audio.
            foreach (FieldInfo field in fade.GetType().GetFields(All))
                if (field.FieldType == typeof(float) && field.Name.Contains("elapsed")) field.SetValue(fade, 10f);
            Check(!fade.MoveNext() && incoming.volume == 0f,
                "finishing a fade preserves the latest volume instead of restoring old gain");
        }
        finally
        {
            if (program != null) Call(program, "Dispose");
            Object.DestroyImmediate(root); Object.DestroyImmediate(clip);
        }
    }
}
#endif
