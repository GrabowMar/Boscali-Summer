using BoscaliSummer.Features.HighCommand.Runtime;
using BoscaliSummer.Runtime;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Features.HighCommand.Patches
{
    /// <summary>
    /// Remembers the last damager of a watched VIP asset so a kill can be paid to the
    /// hostile faction that dealt it. Only watched assets are recorded.
    /// </summary>
    [HarmonyPatch(typeof(Unit), nameof(Unit.RecordDamage))]
    internal static class HighCommandDamagePatch
    {
        private static void Postfix(Unit __instance, PersistentID lastDamagedBy)
        {
            if (GameAccess.IsServer()) HighCommandManager.Active?.RecordDamage(__instance, lastDamagedBy);
        }
    }
}
