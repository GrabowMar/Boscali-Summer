using System.Collections.Generic;
using System.IO;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Configuration;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The saved plans on disk (<c>plans.user.json</c> under the data root, written through a temporary file), read once, and
    /// the theatre the mission is in (its map), so PLANS › lists only the plans made for it.</summary>
    internal static class WmcPlanFiles
    {
        private static PlanStore store;

        private static string FilePath => Path.Combine(WingConfig.RecordsRoot, "plans.user.json");

        /// <summary>The mission's map (MissionManager.CurrentMission.MapKey.Path), or "" outside a mission.</summary>
        public static string Theatre => MissionManager.CurrentMission?.MapKey.Path ?? "";

        public static PlanStore Store
        {
            get
            {
                if (store != null) return store;
                var errors = new List<string>();
                string json = WmcUserFile.Read(FilePath, errors, out _);
                store = PlanStore.FromJson(json, errors);
                // Review minor: a file that could not be read is kept aside before any SAVE writes a fresh one.
                WmcUserFile.Report("[Plans]", FilePath, errors, keepAside: true);
                return store;
            }
        }

        /// <summary>Writes the store; false (logged) when the write failed.</summary>
        public static bool Save() => WmcUserFile.Write("[Plans]", FilePath, Store.ToJson());
    }
}
