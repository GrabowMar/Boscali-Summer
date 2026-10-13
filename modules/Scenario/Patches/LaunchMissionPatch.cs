using System;
using BoscaliSummer.Core.Diagnostics;
using HarmonyLib;
using NuclearOption.BuildScripts;
using NuclearOption.SavedMission;

namespace BoscaliSummer.Modules.Scenario.Patches
{
    /// <summary>
    /// The game's <c>-mission</c> launch argument (a dedicated server, automatic hosting) looks only in the game's own mission groups. <c>-mission scenario:&lt;id&gt;</c>
    /// names a scripted mission by its script id; every other name is left to the game, so no existing mission is shadowed.
    /// </summary>
    [HarmonyPatch(typeof(CommandLineArgParser), nameof(CommandLineArgParser.LoadMission))]
    internal static class LaunchMissionPatch
    {
        private const string Scheme = "scenario:";

        /// <summary>Loads the scripted mission of a script id, or null when there is none.</summary>
        internal static Func<string, Mission> Resolve;

        [HarmonyPrefix]
        private static bool Before(string missionName, ref Mission __result)
        {
            if (Resolve == null || missionName == null || !missionName.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                Mission mission = Resolve(missionName.Substring(Scheme.Length));
                if (mission == null) return true;
                __result = mission;
                return false;
            }
            catch (Exception e)
            {
                PatchGuard.Report("ScenarioLaunch", e);
                return true;
            }
        }
    }
}
