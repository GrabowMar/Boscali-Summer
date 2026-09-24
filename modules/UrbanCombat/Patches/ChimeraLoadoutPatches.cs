using System;
using System.Reflection;
using HarmonyLib;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Registers the Chimera/Tarantula paradrop mount right after vanilla indexes the
    /// Encyclopedia, which every peer does once at boot before any loadout is sent.
    /// </summary>
    [HarmonyPatch]
    internal static class ChimeraMountRegistrationPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
        private static bool Prepare() => TargetMethod() != null;

        // Encyclopedia.i is still null here: the loader assigns it after AfterLoad returns.
        private static void Postfix(Encyclopedia __instance)
        {
            try
            {
                ChimeraInfantryLoadoutAdapter.Register(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[Chimera Loadout] Error registering the paratrooper mount: " + ex);
            }
        }
    }

    internal static class ChimeraLoadoutSetRules
    {
        internal static bool IsChimeraCargoSet(string hardpointName)
        {
            if (string.IsNullOrWhiteSpace(hardpointName)) return false;
            return hardpointName.IndexOf("Cargo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   hardpointName.IndexOf("Mission Bay", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
