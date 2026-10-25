#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Production DLL operations against temporary files and synthetic native cameras; never the installed config/cache.
public static class SettingsMaintenanceUnityCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type Maintenance = Type.GetType("BoscaliSummer.Core.Config.SettingsMaintenance, BoscaliSummer", true);
    private static readonly Type Render = Type.GetType("BoscaliSummer.Core.Fx.RenderCacheMaintenance, BoscaliSummer", true);
    private static int assertions;

    public static void Run()
    {
        try
        {
            MethodInfo setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", All);
            ParameterInfo[] parameters = setPaths.GetParameters();
            var pathArgs = new object[parameters.Length];
            pathArgs[0] = Path.GetFullPath("SettingsMaintenanceCheck.exe");
            for (int i = 1; i < pathArgs.Length; i++) pathArgs[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            setPaths.Invoke(null, pathArgs);
            ConfigReset();
            DiskCache();
            NativeCache();
            File.WriteAllText("result.txt", "PASS: " + assertions + " assertions; factory defaults/preserved data/orphans/backup/failed backup, bounded cache deletion and locked files, native unused/duplicate/disposed stacks, active-camera/default-stack protection. Temp files and synthetic cameras only.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error + "\n");
            EditorApplication.Exit(1);
        }
    }

    private static void ConfigReset()
    {
        string path = Path.GetFullPath("fixture.cfg");
        File.WriteAllText(path, "[Retired]\nUnknownKey = old\n");
        var config = new ConfigFile(path, false) { SaveOnConfigSet = false };
        ConfigEntry<bool> toggle = config.Bind("Fixture", "Toggle", true); toggle.Value = false;
        ConfigEntry<int> number = config.Bind("Fixture", "Number", 12); number.Value = 27;
        ConfigEntry<string> mission = config.Bind("Mission", "RememberedMissions", "{}"); mission.Value = "custom";
        string[] sections = { "Spooktober", "Command", "Command", "Loadout", "Loadout", "Squadron" };
        string[] keys = { "Progress", "TargetPresets", "TargetPresetSlots", "SavedTemplates", "Liveries", "Emblem" };
        var saved = new List<ConfigEntry<string>>();
        for (int i = 0; i < keys.Length; i++)
        {
            ConfigEntry<string> entry = config.Bind(sections[i], keys[i], "factory");
            entry.Value = "saved-" + i;
            saved.Add(entry);
        }
        config.Save();
        string original = File.ReadAllText(path);
        config.SaveOnConfigSet = true;
        object result = Maintenance.GetMethod("FactoryReset", All).Invoke(null, new object[] { config });
        Check((bool)Get(result, "Success") && (bool)Get(result, "RestartRequired"), "factory reset succeeds and asks for restart");
        Check(toggle.Value && number.Value == 12 && mission.Value == "{}", "options and remembered-mission selection reset in existing entries");
        Check(config.SaveOnConfigSet && ReferenceEquals(config[new ConfigDefinition("Fixture", "Toggle")], toggle), "save-on-set and bound ConfigEntry references survive");
        for (int i = 0; i < saved.Count; i++) Check(saved[i].Value == "saved-" + i, "saved content survives: " + keys[i]);
        Check(!File.ReadAllText(path).Contains("UnknownKey"), "orphaned settings are removed from disk");
        Check(File.ReadAllText(path + ".factory-reset.bak") == original, "original config is backed up before resetting");

        number.Value = 29;
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = Maintenance.GetMethod("FactoryReset", All).Invoke(null, new object[] { config });
            Check(!(bool)Get(result, "Success") && number.Value == 29 && config.SaveOnConfigSet,
                "failed backup keeps previous values and restores save-on-set");
        }
        number.Value = 31;
        string beforeFailure = File.ReadAllText(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            result = Maintenance.GetMethod("FactoryReset", All).Invoke(null, new object[] { config });
            Check(!(bool)Get(result, "Success") && (bool)Get(result, "RestartRequired") && number.Value == 31 && config.SaveOnConfigSet,
                "failed save restores setting values/save flag and reports live-callback restart requirement");
            Check(((string)Get(result, "Message")).Contains("Restart the game"), "save-failure feedback states the restart requirement");
            Check(File.ReadAllText(path) == beforeFailure && File.ReadAllText(path + ".factory-reset.bak") == beforeFailure,
                "failed save preserves original file and backup");
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    private static void DiskCache()
    {
        string root = Path.GetFullPath("fixture-cache");
        string owned = Path.Combine(root, "BoscaliSummer");
        string nested = Path.Combine(owned, "nested");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(Path.Combine(root, "OtherPlugin"));
        string sibling = Path.Combine(root, "OtherPlugin", "keep.dat");
        File.WriteAllText(sibling, "keep");
        File.WriteAllText(Path.Combine(owned, "one.dat"), "generated");
        File.WriteAllText(Path.Combine(nested, "two.dat"), "generated");
        string locked = Path.Combine(nested, "locked.dat");
        File.WriteAllText(locked, "busy");
        object result;
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = Maintenance.GetMethod("ClearCache", All).Invoke(null, new object[] { root });
            Check(!(bool)Get(result, "Success") && (int)Get(result, "ClearedFiles") == 2, "locked cache reports partial failure while other files clear");
            Check(File.Exists(locked) && File.Exists(sibling), "locked file and sibling plugin cache are preserved");
        }
        result = Maintenance.GetMethod("ClearCache", All).Invoke(null, new object[] { root });
        Check((bool)Get(result, "Success") && (int)Get(result, "ClearedFiles") == 1, "unlocked remainder is cleared");
        Check(Directory.Exists(owned) && Directory.GetFileSystemEntries(owned).Length == 0 && File.Exists(sibling), "clear is confined to this mod's cache contents");
    }

    private static void NativeCache()
    {
        MethodInfo clear = Render.GetMethod("ClearUnusedStacks", All);
        var args = new object[] { null };
        clear.Invoke(null, args);
        var field = typeof(UniversalAdditionalCameraData).GetField("s_CachedVolumeStacks", All);
        var cache = field.GetValue(null) as List<VolumeStack>;
        if (cache == null) field.SetValue(null, cache = new List<VolumeStack>());
        VolumeManager manager = VolumeManager.instance;
        manager.ResetMainStack();
        VolumeStack main = manager.stack;
        Camera camera = new GameObject("Active disabled feed", typeof(Camera)).GetComponent<Camera>();
        camera.enabled = false;
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        VolumeStack active = manager.CreateStack(); data.volumeStack = active;
        VolumeStack unused = manager.CreateStack(), disposed = manager.CreateStack(); disposed.Dispose();
        cache.Add(unused); cache.Add(unused); cache.Add(disposed); cache.Add(active); cache.Add(main);
        manager.stack = unused;
        args[0] = null;
        int count = (int)clear.Invoke(null, args);
        Check(args[0] == null && count == 2 && cache.Count == 0, "native cache removes entries first and disposes unique unused stacks");
        Check(unused.GetComponent<DepthOfField>() == null && ReferenceEquals(manager.stack, main), "cached current stack is released after resetting the global pointer");
        Check(active.GetComponent<DepthOfField>() != null && ReferenceEquals(data.volumeStack, active), "disabled camera's assigned stack stays alive");
        Check(main.GetComponent<DepthOfField>() != null, "main stack stays alive even if incorrectly listed in native cache");
        Object.DestroyImmediate(camera.gameObject);
        args[0] = null;
        Check((int)clear.Invoke(null, args) == 1 && args[0] == null, "released camera stack can be cleared on a later action");
    }

    private static object Get(object value, string property) => value.GetType().GetProperty(property, All).GetValue(value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }
}
#endif
