using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Core.Scenario;
using BoscaliSummer.Core.Storage;
using UnityEngine;

namespace BoscaliSummer.Modules.Scenario.Runtime
{
    /// <summary>
    /// Every mission script the game can run and what the pilot's campaigns remember. The built-in scripts are embedded; a pilot's own are text files in
    /// <c>BepInEx/config/BoscaliSummer/scenarios/*.scenario</c>, re-read when one changes, and a file with the id of a built-in script replaces it.
    /// </summary>
    internal sealed class ScenarioLibrary
    {
        private const string ResourcePrefix = "BoscaliSummer.Scenario.", Extension = ".scenario", ProgressFile = "progress.txt";
        private const long MaxScriptBytes = 512 * 1024;
        private const int MaxUserScripts = 64;

        private readonly ManualLogSource log;
        private readonly List<ScenarioDef> scripts = new List<ScenarioDef>();
        private string stamp;
        private float nextScan;

        public string Folder { get; }
        public IReadOnlyList<ScenarioDef> Scripts => scripts;
        public ScenarioProgress Progress { get; }
        /// <summary>Moves whenever the scripts were read again: whatever was built from them is stale.</summary>
        public int Revision { get; private set; }

        public ScenarioLibrary(string folder, ManualLogSource logger)
        {
            Folder = folder;
            log = logger;
            string saved = null;
            try
            {
                string path = Path.Combine(Folder, ProgressFile);
                if (File.Exists(path)) saved = File.ReadAllText(path);
            }
            catch (IOException error) { log?.LogWarning("[Scenario] campaign progress could not be read: " + error.Message); }
            catch (UnauthorizedAccessException error) { log?.LogWarning("[Scenario] campaign progress could not be read: " + error.Message); }
            Progress = ScenarioProgress.Parse(saved);
            stamp = Stamp();
            Load();
        }

        public ScenarioDef Find(string id)
        {
            if (id == null) return null;
            foreach (ScenarioDef script in scripts)
                if (script.Id == id) return script;
            return null;
        }

        /// <summary>Cheap enough for a menu to call every frame: looks at the folder every two seconds and re-reads only after a change.</summary>
        public void Refresh()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 2f;
            string now = Stamp();
            if (now == stamp) return;
            stamp = now;
            Load();
        }

        public void SaveProgress()
        {
            try { Directory.CreateDirectory(Folder); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (!AtomicFile.WriteAllText(Path.Combine(Folder, ProgressFile), Progress.Format(), out string error))
                log?.LogWarning("[Scenario] campaign progress could not be saved: " + error);
        }

        private void Load()
        {
            scripts.Clear();
            Assembly assembly = typeof(ScenarioLibrary).Assembly;
            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(Extension, StringComparison.Ordinal)) continue;
                using (Stream stream = assembly.GetManifestResourceStream(resource))
                using (var reader = new StreamReader(stream))
                    Add(resource.Substring(ResourcePrefix.Length), reader.ReadToEnd(), false);
            }
            foreach (string file in UserFiles())
            {
                try
                {
                    if (new FileInfo(file).Length > MaxScriptBytes) { log?.LogWarning("[Scenario] " + Path.GetFileName(file) + " is larger than 512 KB; skipped."); continue; }
                    Add(Path.GetFileName(file), File.ReadAllText(file), true);
                }
                catch (IOException error) { log?.LogWarning("[Scenario] " + Path.GetFileName(file) + " could not be read: " + error.Message); }
                catch (UnauthorizedAccessException error) { log?.LogWarning("[Scenario] " + Path.GetFileName(file) + " could not be read: " + error.Message); }
            }
            Revision++;
            log?.LogInfo("[Scenario] " + scripts.Count + " mission script(s) loaded; your own go in " + Folder);
        }

        private void Add(string source, string text, bool fromUser)
        {
            ScenarioDef script = ScenarioParser.Parse(source, text);
            foreach (string error in script.Errors) log?.LogWarning("[Scenario] " + error);
            if (script.Id == null || string.IsNullOrEmpty(script.Title) || string.IsNullOrEmpty(script.Base)) return;
            int same = scripts.FindIndex(other => other.Id == script.Id);
            if (same >= 0)
            {
                if (!fromUser) { log?.LogWarning("[Scenario] " + source + " repeats the id '" + script.Id + "'; skipped."); return; }
                log?.LogInfo("[Scenario] " + source + " replaces the built-in script '" + script.Id + "'.");
                scripts.RemoveAt(same);
            }
            scripts.Add(script);
        }

        private string[] UserFiles()
        {
            try
            {
                if (!Directory.Exists(Folder)) return Array.Empty<string>();
                string[] files = Directory.GetFiles(Folder, "*" + Extension);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                if (files.Length > MaxUserScripts) Array.Resize(ref files, MaxUserScripts);
                return files;
            }
            catch (IOException) { return Array.Empty<string>(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        /// <summary>Names, sizes and write times of the pilot's script files: changes when any of them does.</summary>
        private string Stamp()
        {
            var text = new System.Text.StringBuilder();
            foreach (string file in UserFiles())
            {
                try
                {
                    var info = new FileInfo(file);
                    text.Append(info.Name).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append(';');
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return text.ToString();
        }
    }
}
