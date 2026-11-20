using System.Collections.Generic;
using BoscaliSummer.Core.Diagnostics;
using HarmonyLib;
using NuclearOption.SavedMission;

namespace BoscaliSummer.Modules.Scenario.Patches
{
    /// <summary>
    /// <c>MissionRunner.StopObjective</c> and <c>CompleteObjective</c> end with <c>activeByFaction[obj.FactionHQ].Remove(obj)</c>, which throws KeyNotFoundException
    /// when the objective's faction has never had a running objective: a script's <c>then stop X</c> (or the <c>cancel</c> beat action) for an objective that has not
    /// started, as the prologue's cargo_escaped / force_down pair can. The objective is still marked complete by the game before the throw; this only makes the removal safe.
    /// </summary>
    [HarmonyPatch(typeof(MissionRunner), "RemoveActiveObjective")]
    internal static class StopObjectivePatch
    {
        private static readonly AccessTools.FieldRef<MissionRunner, Dictionary<FactionHQ, List<Objective>>> ByFaction =
            AccessTools.FieldRefAccess<MissionRunner, Dictionary<FactionHQ, List<Objective>>>("activeByFaction");

        [HarmonyPrefix]
        private static bool Before(MissionRunner __instance, Objective obj)
        {
            try
            {
                if (obj == null || obj.FactionHQ == null || ByFaction(__instance).ContainsKey(obj.FactionHQ)) return true;
                __instance.ActiveObjectives.Remove(obj);
                return false;
            }
            catch (System.Exception e)
            {
                PatchGuard.Report("StopObjective", e);
                return true;
            }
        }
    }
}
