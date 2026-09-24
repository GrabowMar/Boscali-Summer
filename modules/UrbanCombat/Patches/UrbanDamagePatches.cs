using System;
using System.Reflection;
using BoscaliSummer.Infrastructure.Diagnostics;
using BoscaliSummer.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Stages occupied shells as they take damage: scarred nests look burnt, ravaged ones
    /// stop counting as siege strongpoints. Unoccupied shells miss one dictionary lookup.
    /// </summary>
    [HarmonyPatch]
    internal static class ShellDamagePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "TakeDamage");
        private static bool Prepare() => TargetMethod() != null && GameAccess.MapBuildingHitPointsAvailable;

        private static void Postfix(MapBuilding __instance)
        {
            if (__instance == null) return;
            ZoneGarrisonManager.NoteShellDamage(__instance);
        }
    }

    /// <summary>
    /// Server-side strongpoint rule: an occupied shell under URBAN SIEGE takes four
    /// separate explosive hits; overwhelming blasts still kill outright. Non-final
    /// hits assert stepped HP and skip vanilla so the existing staging sees progress.
    /// </summary>
    [HarmonyPatch]
    internal static class StrongpointDamagePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "TakeDamage");
        private static bool Prepare() => TargetMethod() != null && GameAccess.MapBuildingHitPointsAvailable;

        private static bool Prefix(
            MapBuilding __instance,
            float pierceDamage, float blastDamage, float amountAffected,
            float fireDamage, float impactDamage)
        {
            if (!GameAccess.IsServer()) return true;
            try
            {
                if (__instance == null) return true;
                ArmorProperties armor = __instance.GetArmorProperties();
                if (armor == null) return true;
                float blastTerm = StrongpointHitPolicy.BlastTerm(
                    blastDamage, armor.blastArmor, amountAffected, armor.blastTolerance);
                float total = StrongpointHitPolicy.TotalEstimate(
                    pierceDamage, blastDamage, amountAffected, fireDamage, impactDamage,
                    armor.pierceArmor, armor.pierceTolerance,
                    armor.blastArmor, armor.blastTolerance,
                    armor.fireArmor, armor.fireTolerance);
                return ZoneGarrisonManager.ApplyStrongpointHit(__instance, blastTerm, total);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Garrisons.StrongpointDamage", e);
                return true;
            }
        }
    }

    /// <summary>
    /// Client-side guard: local damage is skipped on known strongpoint shells so client
    /// guns never kill through the local copy and send the destroy command. Applies to
    /// every occupied shell whatever the URBAN SIEGE toggle; the server decides.
    /// </summary>
    [HarmonyPatch]
    internal static class StrongpointClientGuardPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "TakeDamage");
        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix(MapBuilding __instance)
        {
            if (GameAccess.IsServer()) return true;
            try
            {
                return !(__instance != null && NestRegistry.IsStrongpointShell(__instance));
            }
            catch (Exception e)
            {
                PatchGuard.Report("Garrisons.StrongpointClientGuard", e);
                return true;
            }
        }
    }

    /// <summary>
    /// Server-side guard on the client destroy command: an occupied shell is rejected,
    /// closing the no-authority client-kill hole. Applies whatever the URBAN SIEGE
    /// toggle; ordinary buildings keep the vanilla path untouched.
    /// </summary>
    [HarmonyPatch]
    internal static class DestroyCommandGuardPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(MapBuildingSet), "UserCode_CmdDestroyBuilding_1002795805");
        private static bool Prepare() =>
            TargetMethod() != null && GameAccess.MapBuildingSetBuildingsAvailable;

        private static bool Prefix(MapBuildingSet __instance, int index)
        {
            try
            {
                if (__instance != null &&
                    GameAccess.TryGetMapBuilding(__instance, index, out MapBuilding building) &&
                    GarrisonOccupancy.IsOccupied(building.gameObject))
                    return false;
                return true;
            }
            catch (Exception e)
            {
                PatchGuard.Report("Garrisons.DestroyCommandGuard", e);
                return true;
            }
        }
    }

    /// <summary>
    /// Kills the rooftop nest the instant its shell is destroyed instead of waiting for
    /// the 2 s lifecycle tick. Server-only; clients follow through vanilla despawn.
    /// </summary>
    [HarmonyPatch]
    internal static class ShellDestructPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "Destruct");
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(MapBuilding __instance)
        {
            if (__instance == null) return;
            ZoneGarrisonManager.NoteShellDestroyed(__instance);
        }
    }
}
