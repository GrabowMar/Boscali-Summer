using BoscaliSummer.Modules.Squad.Runtime;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Squad.Patches
{
    [HarmonyPatch(typeof(Unit), nameof(Unit.RecordDamage))]
    internal static class SquadDamagePatch
    {
        private static void Postfix(Unit __instance, PersistentID lastDamagedBy, float damageAmount)
        {
            if (GameAccess.IsServer()) SquadRuntime.Active?.RecordDamage(__instance, lastDamagedBy, damageAmount);
        }
    }

    // The shared native patch calls this observer when the Squad module is active.
    internal static class SquadKillPatch
    {
        internal static void RecordKill(Unit unit)
        {
            if (GameAccess.IsServer()) SquadRuntime.Active?.RecordKill(unit);
        }
    }

    [HarmonyPatch(typeof(Pilot), nameof(Pilot.ApplyDamage))]
    internal static class SquadPilotDeathPatch
    {
        private static void Prefix(Pilot __instance, out Player __state) =>
            __state = __instance == null || __instance.dead ? null : __instance.aircraft?.Player;
        private static void Postfix(Pilot __instance, Player __state)
        {
            if (GameAccess.IsServer() && __state != null && __instance.dead)
                SquadRuntime.Active?.RecordPilotDeath(__state, __instance);
        }
    }
}
