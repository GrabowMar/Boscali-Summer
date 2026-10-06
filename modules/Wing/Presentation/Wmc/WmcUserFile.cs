using System;
using System.Collections.Generic;
using System.IO;

using BoscaliSummer.Core.Storage;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The load, keep-<c>.bad</c> and save steps the player's JSON files under the data root share (plans, routes, stances);
    /// each store keeps its own parsing and its own rule for when a damaged file is kept aside. Writes go through <see cref="AtomicFile"/>.</summary>
    internal static class WmcUserFile
    {
        /// <summary>The file's text, or null when it does not exist; a read failure goes into <paramref name="errors"/> and
        /// <paramref name="unreadable"/> (locked, no access).</summary>
        public static string Read(string path, List<string> errors, out bool unreadable)
        {
            unreadable = false;
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception e)
            {
                errors.Add(e.Message);
                unreadable = true;
                return null;
            }
        }

        /// <summary>Logs every error under <paramref name="tag"/>; with <paramref name="keepAside"/> and any error, copies the file to
        /// <c>.bad</c> so the next SAVE cannot overwrite what could not be read.</summary>
        public static void Report(string tag, string path, List<string> errors, bool keepAside)
        {
            string name = Path.GetFileName(path);
            foreach (string e in errors) WingLog.Logger.LogWarning(tag + " " + name + ": " + e);
            if (!keepAside || errors.Count == 0 || !File.Exists(path)) return;
            try
            {
                File.Copy(path, path + ".bad", true);
                WingLog.Logger.LogWarning(tag + " kept the unreadable " + name + " as " + name + ".bad");
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning(tag + " could not keep the unreadable " + name + " aside: " + e.Message);
            }
        }

        /// <summary>Writes <paramref name="json"/> whole; false (logged) when the write failed.</summary>
        public static bool Write(string tag, string path, string json)
        {
            if (AtomicFile.WriteAllText(path, json, out string error)) return true;
            WingLog.Logger.LogWarning(tag + " could not save " + Path.GetFileName(path) + ": " + error);
            return false;
        }
    }
}
