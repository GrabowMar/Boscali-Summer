using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Storage;
using BoscaliSummer.Core.Game;
namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>The saved pilots on disk (R7): <c>v1/pilots.user.json</c> (scenarios use <c>pilots.sim.json</c>), read at every mission
    /// start and written whole through <see cref="AtomicFile"/> by the studio, the interop and the mission-end tally. A write that fails
    /// puts the store back as it was, so nothing shows SAVED that is not on disk.</summary>
    internal static class WingSavedPilots
    {
        public const string UserFile = "pilots.user.json", ExportFile = "exported_pilots.json";

        private static SavedPilotStore store;
        private static bool tallied, locked;

        /// <summary>Why the saved pilots could not be read this mission (the list's empty card says it), or null.</summary>
        public static string Problem { get; private set; }

        public static string FilePath => Path.Combine(WingConfig.RecordsRoot, UserFile);

        /// <summary>Where EXPORT writes and IMPORT reads first (FOLDER opens it).</summary>
        public static string Folder => Path.Combine(WingConfig.RecordsRoot, "Pilots");

        public static SavedPilotStore Store => store ?? Load();

        /// <summary>Reads the file (a missing file is an empty squadron; problems are logged, never thrown).</summary>
        public static SavedPilotStore Load()
        {
            var problems = new List<string>();
            string json = null;
            bool unreadable = false;
            try
            {
                if (File.Exists(FilePath)) json = File.ReadAllText(FilePath);
            }
            catch (Exception e)
            {
                problems.Add(e.Message);
                unreadable = true;
            }
            store = SavedPilotStore.FromJson(json, problems);
            // Review R7: a file that cannot be read, or that loads short of what it holds (too many pilots, a duplicate callsign), is
            // kept aside before anything writes over it; if it cannot be kept, nothing writes.
            locked = false;
            Problem = null;
            if (unreadable || problems.Count > 0)
            {
                bool partial = !unreadable && !SavedPilotStore.Unreadable(json);
                string kept = FilePath + ".bad";
                try
                {
                    File.Copy(FilePath, kept, overwrite: true);
                    Problem = Path.GetFileName(FilePath) + (partial ? " loaded with problems: the original is kept as " + Path.GetFileName(kept)
                        : " could not be read: kept as " + Path.GetFileName(kept) + "; SAVE starts a new file");
                }
                catch (Exception e)
                {
                    locked = true;
                    Problem = Path.GetFileName(FilePath) + (partial ? " loaded with problems and could not be kept aside" : " could not be read or kept aside")
                        + ": saving is off until it is fixed";
                    problems.Add(e.Message);
                }
                WingLog.Logger.LogWarning("[Pilots] " + Problem);
            }
            foreach (string p in problems) WingLog.Logger.LogWarning("[Pilots] " + Path.GetFileName(FilePath) + ": " + p);
            WingPilotRoster.SavedTaken = c => store != null && store.Find(c) != null;
            tallied = false;
            return store;
        }

        /// <summary>A saved callsign (the studio's SAVED / NOT SAVED).</summary>
        public static bool IsSaved(string callsign) => Store.Find(callsign) != null;

        /// <summary>Saves a draft (a rename when <paramref name="original"/> names a saved pilot), carries it onto <paramref name="live"/>
        /// (the pilot this mission it is, if any), then writes the file. False with the reason; the store is unchanged then.</summary>
        public static bool Save(CustomPilotRecord draft, string original, WingPilot live, out string why)
        {
            SavedPilotStore s = Store;
            string backup = s.ToJson();
            string target = draft != null ? PilotText.Callsign(draft.Callsign) : null;
            if (draft != null && !draft.HasCustomPortrait)
            {
                int faction = live != null && live.PortraitFaction >= 0 ? live.PortraitFaction : PortraitFactions.Local;
                draft = draft.Clone();
                draft.ApplySelection(PilotStudio.Frozen(draft.Name, draft.Callsign, null, faction));
            }
            if (!s.Save(draft, original, c => LiveUnsaved(c, live), out why)) return false;
            if (!Write())
            {
                store = SavedPilotStore.FromJson(backup, null);
                why = StudioWords.SaveFailed;
                return false;
            }
            CustomPilotRecord saved = s.Find(target);
            if (live != null && saved != null) WingPilotRoster.UpdateIdentity(live, saved);
            if (original != null && target != null && !string.Equals(original, target, StringComparison.OrdinalIgnoreCase))
            {
                // A rename carries the PNG portrait and the player's pick along with the callsign.
                MovePortrait(original, target);
                if (IsMe(original)) SetMe(target);
            }
            WingPilotRoster.Touch();
            return true;
        }

        public static bool Delete(string callsign, out string why)
        {
            why = null;
            SavedPilotStore s = Store;
            string backup = s.ToJson();
            if (!s.Remove(callsign))
            {
                why = callsign + " is not a saved pilot.";
                return false;
            }
            if (Write())
            {
                WingPilotRoster.Touch();
                return true;
            }
            store = SavedPilotStore.FromJson(backup, null);
            why = StudioWords.SaveFailed;
            return false;
        }

        /// <summary>IMPORT: pilots from <see cref="Folder"/>, only callsigns not saved yet and
        /// not flying this mission unsaved, each with an empty service record. Source files are never touched.</summary>
        public static void Import(out int added, out int skipped, out int files)
        {
            List<CustomPilotRecord> incoming = LoadFolder(Folder, out files);
            SavedPilotStore s = Store;
            string backup = s.ToJson();
            s.Import(incoming, c => LiveUnsaved(c, null), out added, out skipped);
            if (added > 0 && !Write())
            {
                store = SavedPilotStore.FromJson(backup, null);
                skipped += added;
                added = 0;
            }
            if (added > 0) WingPilotRoster.Touch();
        }

        /// <summary>EXPORT: every saved pilot to <c>Pilots/exported_pilots.json</c> (IMPORT reads it).</summary>
        public static bool Export(out int pilots)
        {
            pilots = Store.Records.Count;
            bool ok = AtomicFile.WriteAllText(Path.Combine(Folder, ExportFile), CustomPilotCodec.Encode(Store.Records), out string error);
            if (!ok) WingLog.Logger.LogWarning("[Pilots] could not export: " + error);
            return ok;
        }

        /// <summary>The mission's end (WingService.Deactivate): each saved pilot who sat in an aircraft adds a mission, its sorties and
        /// kills, and keeps its best XP. Once per mission.</summary>
        public static void Tally()
        {
            if (tallied || store == null) return;
            tallied = true;
            var roster = new List<WingPilot>();
            WingPilotRoster.Roster(roster);
            var lines = new List<MissionLine>(roster.Count);
            foreach (WingPilot p in roster) lines.Add(MissionLine.Of(p.Callsign, p.LastAircraft != null, p.Sorties, p.Kills, p.Xp));
            if (store.Tally(lines) && Write()) WingLog.Verbose("[Pilots] service records written");
        }

        /// <summary>The pilots in every readable top-level JSON file of <paramref name="dir"/> (read only); <paramref name="files"/> counts the files.</summary>
        private static List<CustomPilotRecord> LoadFolder(string dir, out int files)
        {
            var pilots = new List<CustomPilotRecord>();
            files = 0;
            try
            {
                if (!Directory.Exists(dir)) return pilots;
                foreach (string file in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        pilots.AddRange(CustomPilotCodec.Decode(File.ReadAllText(file)).Pilots);
                        files++;
                    }
                    catch (Exception e)
                    {
                        WingLog.Logger.LogWarning("[Pilots] could not read " + Path.GetFileName(file) + ": " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning("[Pilots] could not list " + dir + ": " + e.Message);
            }
            return pilots;
        }

        /// <summary>Shows <see cref="Folder"/> in the file browser (created when missing).</summary>
        public static void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                Process.Start(new ProcessStartInfo { FileName = Folder, UseShellExecute = true });
                WingToast.Show("Opened the Pilots folder");
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning("[Pilots] could not open the folder: " + e.Message);
                WingToast.Show("Pilots folder: " + Folder);
            }
        }

        // ---------------------------------------------------------------- the player's pilot (the SQD dossier's profile)

        /// <summary>Progression's [Squadron] PilotProfile: the saved pilot the player flies as (the SQD dossier shows it). Set by
        /// WingModule; null when the config is not there.</summary>
        public static BepInEx.Configuration.ConfigEntry<string> Me;

        public static bool IsMe(string callsign) =>
            Me != null && !string.IsNullOrEmpty(callsign) && string.Equals(Me.Value, callsign, StringComparison.OrdinalIgnoreCase);

        /// <summary>The player flies as <paramref name="callsign"/> (null or empty: the host's squadron record).</summary>
        public static void SetMe(string callsign)
        {
            if (Me != null) Me.Value = callsign ?? "";
        }

        // ---------------------------------------------------------------- PNG portraits (Pilots/Portraits/<CALLSIGN>.png)

        public static string PortraitFolder => Path.Combine(Folder, "Portraits");

        public static string PortraitPath(string callsign) => Path.Combine(PortraitFolder, PilotText.Callsign(callsign) + ".png");

        public static bool HasPortraitFile(string callsign)
        {
            try { return !string.IsNullOrEmpty(callsign) && File.Exists(PortraitPath(callsign)); }
            catch (Exception) { return false; }
        }

        /// <summary>LOAD PNG: an image file becomes the pilot's portrait (cropped to 4:5, 256x320). The source is only read.</summary>
        public static bool LoadPortrait(string callsign, string source, out string why)
        {
            why = null;
            try
            {
                var info = new FileInfo(source);
                if (!info.Exists || info.Length > 32L * 1024 * 1024)
                {
                    why = "Pick a PNG or JPG under 32 MB.";
                    return false;
                }
                byte[] png = PilotPortrait.ToPortraitPng(File.ReadAllBytes(source));
                if (png == null)
                {
                    why = Path.GetFileName(source) + " is not a PNG or JPG image.";
                    return false;
                }
                Directory.CreateDirectory(PortraitFolder);
                File.WriteAllBytes(PortraitPath(callsign), png);
                PilotPortrait.Recheck(callsign);
                return true;
            }
            catch (Exception e)
            {
                why = "Could not read " + Path.GetFileName(source) + ": " + e.Message;
                return false;
            }
        }

        /// <summary>SAVE PNG: the portrait as shown (the PNG on file, else the generated look) written to <paramref name="target"/>.</summary>
        public static bool SavePortrait(string callsign, PortraitSelection look, string target, out string why)
        {
            why = null;
            try
            {
                byte[] png = HasPortraitFile(callsign) ? File.ReadAllBytes(PortraitPath(callsign)) : PilotPortrait.SelectionPng(look);
                if (png == null)
                {
                    why = "The portrait could not be drawn.";
                    return false;
                }
                File.WriteAllBytes(target, png);
                return true;
            }
            catch (Exception e)
            {
                why = "Could not write " + Path.GetFileName(target) + ": " + e.Message;
                return false;
            }
        }

        /// <summary>CLEAR PNG: back to the generated look. The file is kept aside as .png.old (one level of undo).</summary>
        public static bool ClearPortrait(string callsign)
        {
            try
            {
                string path = PortraitPath(callsign);
                if (!File.Exists(path)) return false;
                string old = path + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(path, old);
                PilotPortrait.Recheck(callsign);
                return true;
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning("[Pilots] could not clear the portrait of " + callsign + ": " + e.Message);
                return false;
            }
        }

        private static void MovePortrait(string from, string to)
        {
            try
            {
                string a = PortraitPath(from), b = PortraitPath(to);
                if (!File.Exists(a) || File.Exists(b)) return;
                File.Move(a, b);
                PilotPortrait.Recheck(from);
                PilotPortrait.Recheck(to);
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning("[Pilots] could not carry the portrait of " + from + " to " + to + ": " + e.Message);
            }
        }

        /// <summary>A callsign flying this mission unsaved (other than <paramref name="self"/>): a save or import must not take it.</summary>
        private static bool LiveUnsaved(string callsign, WingPilot self)
        {
            WingPilot p = WingPilotRoster.FindByCallsign(callsign);
            return p != null && !ReferenceEquals(p, self) && (store == null || store.Find(callsign) == null);
        }

        private static bool Write()
        {
            if (locked)
            {
                WingLog.Logger.LogWarning("[Pilots] not writing " + Path.GetFileName(FilePath) + ": " + Problem);
                return false;
            }
            bool ok = AtomicFile.WriteAllText(FilePath, Store.ToJson(), out string error);
            if (!ok) WingLog.Logger.LogWarning("[Pilots] could not write " + Path.GetFileName(FilePath) + ": " + error);
            else Problem = null;
            return ok;
        }
    }
}
