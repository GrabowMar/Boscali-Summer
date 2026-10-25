using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BoscaliSummer.Core.Fx;

namespace BoscaliSummer.Core.Config
{
    internal readonly struct MaintenanceResult
    {
        public bool Success => Failures == 0;
        public string Message { get; }
        public bool RestartRequired { get; }
        public int ClearedFiles { get; }
        public int ClearedStacks { get; }
        public int ResetSettings { get; }
        public int Failures { get; }

        public MaintenanceResult(string message, int failures = 0, bool restartRequired = false,
            int clearedFiles = 0, int clearedStacks = 0, int resetSettings = 0)
        {
            Message = message;
            Failures = failures;
            RestartRequired = restartRequired;
            ClearedFiles = clearedFiles;
            ClearedStacks = clearedStacks;
            ResetSettings = resetSettings;
        }
    }

    /// <summary>SET maintenance touches only this mod's config and generated cache, never player data folders.</summary>
    internal static class SettingsMaintenance
    {
        private static readonly PropertyInfo OrphanedEntries = typeof(ConfigFile).GetProperty("OrphanedEntries",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // Saved content is stored in the shared settings file too; restoring options must not erase it.
        private static readonly HashSet<ConfigDefinition> SavedContent = new HashSet<ConfigDefinition>
        {
            new ConfigDefinition("Spooktober", "Progress"),
            new ConfigDefinition("Command", "TargetPresets"),
            new ConfigDefinition("Command", "TargetPresetSlots"),
            new ConfigDefinition("Loadout", "SavedTemplates"),
            new ConfigDefinition("Loadout", "Liveries"),
            new ConfigDefinition("Squadron", "Emblem")
        };

        /// <param name="cacheRoot">The BepInEx cache base, or a temporary base for checks. Only its BoscaliSummer child is touched.</param>
        public static MaintenanceResult ClearCache(string cacheRoot = null)
        {
            int files = 0, failures = 0;
            string firstError = null;
            try
            {
                string root = Path.GetFullPath(cacheRoot ?? Paths.CachePath);
                string owned = Path.GetFullPath(Path.Combine(root, "BoscaliSummer"));
                string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!owned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Cache path leaves the mod's cache root.");
                if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Cache root is a link; no files were removed.");
                if (Directory.Exists(owned)) PurgeDirectory(owned, false, ref files, ref failures, ref firstError);
            }
            catch (Exception error) { RecordFailure(error.Message, ref failures, ref firstError); }

            int stacks = RenderCacheMaintenance.ClearUnusedStacks(out string renderError);
            if (renderError != null) RecordFailure(renderError, ref failures, ref firstError);
            string message = "Cache cleared: " + files + " file(s), " + stacks + " unused render stack(s).";
            if (failures > 0) message = "Cache partly cleared: " + files + " file(s), " + stacks + " render stack(s). " + firstError;
            return new MaintenanceResult(message, failures, clearedFiles: files, clearedStacks: stacks);
        }

        /// <summary>The caller must reject a remote session: its host-value ledger owns temporary config overrides.</summary>
        public static MaintenanceResult FactoryReset(ConfigFile config)
        {
            if (config == null) return new MaintenanceResult("Settings are unavailable.", 1);
            var orphans = OrphanedEntries?.GetValue(config) as IDictionary<ConfigDefinition, string>;
            if (orphans == null) return new MaintenanceResult("Factory reset unavailable: config storage could not be read.", 1);
            var entries = new List<KeyValuePair<ConfigEntryBase, object>>();
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in config)
                entries.Add(new KeyValuePair<ConfigEntryBase, object>(pair.Value, pair.Value.BoxedValue));
            var previousOrphans = new Dictionary<ConfigDefinition, string>(orphans);
            string backup = config.ConfigFilePath + ".factory-reset.bak";
            bool saveOnSet = config.SaveOnConfigSet, hadFile = File.Exists(config.ConfigFilePath), attemptedSave = false;
            int reset = 0;
            try
            {
                config.SaveOnConfigSet = false;
                if (hadFile) File.Copy(config.ConfigFilePath, backup, true);
                foreach (KeyValuePair<ConfigEntryBase, object> pair in entries)
                {
                    if (SavedContent.Contains(pair.Key.Definition)) continue;
                    if (!Equals(pair.Key.BoxedValue, pair.Key.DefaultValue)) reset++;
                    pair.Key.BoxedValue = pair.Key.DefaultValue;
                }
                foreach (ConfigDefinition key in new List<ConfigDefinition>(orphans.Keys))
                    if (!SavedContent.Contains(key)) orphans.Remove(key);
                attemptedSave = true;
                config.Save();
                return new MaintenanceResult("Factory settings restored. Restart the game to apply all options. Saves and saved presets kept.",
                    restartRequired: true, resetSettings: reset);
            }
            catch (Exception error)
            {
                // Retained ConfigEntry objects stay bound to their modules; never use ConfigFile.Clear().
                foreach (KeyValuePair<ConfigEntryBase, object> pair in entries) pair.Key.BoxedValue = pair.Value;
                orphans.Clear();
                foreach (KeyValuePair<ConfigDefinition, string> pair in previousOrphans) orphans.Add(pair);
                string restoreError = null;
                if (attemptedSave && hadFile)
                {
                    try { File.Copy(backup, config.ConfigFilePath, true); }
                    catch (Exception failure) { restoreError = " Config backup remains at " + backup + ": " + failure.Message; }
                }
                bool restart = reset > 0;
                return new MaintenanceResult("Factory reset failed; previous setting values restored. " + error.Message + restoreError +
                    (restart ? " Restart the game before continuing; live setting callbacks may have reacted." : ""),
                    1, restartRequired: restart);
            }
            finally { config.SaveOnConfigSet = saveOnSet; }
        }

        private static bool PurgeDirectory(string directory, bool removeDirectory, ref int files, ref int failures, ref string firstError)
        {
            bool complete = true;
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A cache link was skipped.");
                foreach (string path in Directory.GetFileSystemEntries(directory))
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A cache link was skipped.");
                        if ((attributes & FileAttributes.Directory) != 0)
                            complete &= PurgeDirectory(path, true, ref files, ref failures, ref firstError);
                        else { File.Delete(path); files++; }
                    }
                    catch (Exception error) { complete = false; RecordFailure(error.Message, ref failures, ref firstError); }
                }
                if (removeDirectory && complete) Directory.Delete(directory, false);
            }
            catch (Exception error) { complete = false; RecordFailure(error.Message, ref failures, ref firstError); }
            return complete;
        }

        private static void RecordFailure(string error, ref int failures, ref string firstError)
        {
            failures++;
            if (firstError == null) firstError = error;
        }
    }
}
