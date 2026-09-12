using BoscaliSummer.Features.Squad.Runtime;
using BoscaliSummer.Runtime;
using HarmonyLib;
using NuclearOption.Networking;

namespace BoscaliSummer.Features.Squad.Patches
{
    [HarmonyPatch(typeof(Unit), nameof(Unit.RecordDamage))]
    internal static class SquadDamagePatch
    {
        private static void Postfix(Unit __instance, PersistentID lastDamagedBy, float damageAmount)
        {
            if (GameAccess.IsServer()) SquadRuntime.Active?.RecordDamage(__instance, lastDamagedBy, damageAmount);
        }
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.ReportKilled))]
    internal static class SquadKillPatch
    {
        private static void Postfix(Unit __instance)
        {
            if (GameAccess.IsServer()) SquadRuntime.Active?.RecordKill(__instance);
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
