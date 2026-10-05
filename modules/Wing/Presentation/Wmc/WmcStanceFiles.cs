using System;
using System.Collections.Generic;
using System.IO;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Configuration;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The player's stances on disk (<c>stances.user.json</c> under the data root, written through a temporary file), read
    /// once. A file that cannot be read keeps the built-ins, is logged, and is kept aside as <c>.bad</c> before a SAVE replaces it
    /// (the same rule as plans.user.json).</summary>
    internal static class WmcStanceFiles
    {
        private static StanceBook book;
        /// <summary>The file exists but could not be read (locked, no access): SAVE must not overwrite it with the built-ins.</summary>
        private static bool unreadable;

        private static string FilePath => Path.Combine(WingConfig.RecordsRoot, "stances.user.json");

        public static StanceBook Book
        {
            get
            {
                if (book != null) return book;
                var errors = new List<string>();
                string json = null;
                try
                {
                    json = File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
                }
                catch (Exception e)
                {
                    errors.Add(e.Message);
                    unreadable = true;
                }
                book = StanceBook.FromJson(json, StanceDoctrine.BuiltIns, errors);
                foreach (string e in errors) WingLog.Logger.LogWarning("[Stances] stances.user.json: " + e);
                if (errors.Count > 0 && !unreadable && File.Exists(FilePath))
                    try
                    {
                        File.Copy(FilePath, FilePath + ".bad", true);
                        WingLog.Logger.LogWarning("[Stances] kept the unreadable stances.user.json as stances.user.json.bad");
                    }
                    catch (Exception e)
                    {
                        WingLog.Logger.LogWarning("[Stances] could not keep the unreadable stances.user.json aside: " + e.Message);
                    }
                return book;
            }
        }

        /// <summary>Writes the book; false (logged) when the write failed.</summary>
        public static bool Save()
        {
            StanceBook b = Book;
            if (unreadable)
            {
                WingLog.Logger.LogWarning("[Stances] not saving: stances.user.json could not be read this session, so it is left as it is");
                return false;
            }
            try
            {
                Directory.CreateDirectory(WingConfig.RecordsRoot);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, b.ToJson());
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                return true;
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning("[Stances] could not save stances.user.json: " + e.Message);
                return false;
            }
        }
    }
}
