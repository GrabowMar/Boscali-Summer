using System;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
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
    /// Server-side strongpoint rule: an occupied shell under URBAN SIEGE has a structure
    /// pool sized by its roof that only explosions wear, weighted by warhead blast power,
    /// so bombs level it and missiles barely scratch it. Worn hits assert stepped HP and
    /// skip vanilla; the final one levels the shell through vanilla's destroy path.
    /// </summary>
    [HarmonyPatch]
    internal static class StrongpointDamagePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "TakeDamage");
        private static bool Prepare() => TargetMethod() != null && GameAccess.MapBuildingHitPointsAvailable;

        private static bool Prefix(MapBuilding __instance)
        {
            if (ZoneGarrisonManager.SelfDamage || !GameAccess.IsServer()) return true;
            try
            {
                if (__instance == null || !ZoneGarrisonManager.MightBeStrongpointHit(__instance)) return true;
                return ZoneGarrisonManager.ApplyShellHit(__instance);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Garrisons.StrongpointDamage", e);
                return true;
            }
        }
    }

    /// <summary>
    /// Records each server shockwave's blast power for the damage call vanilla makes on the
    /// same target right after it (MapBuilding's own TakeShockwave is empty).
    /// </summary>
    [HarmonyPatch]
    internal static class ShellShockwavePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapBuilding), "TakeShockwave");
        private static bool Prepare() => TargetMethod() != null;

        private static void Prefix(MapBuilding __instance, UnityEngine.Vector3 origin, float blastPower)
        {
            if (GameAccess.IsServer()) ZoneGarrisonManager.NoteShockwave(__instance, origin, blastPower);
        }
    }

    [HarmonyPatch]
    internal static class PartShockwavePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(UnitPart), "TakeShockwave");
        private static bool Prepare() => TargetMethod() != null;

        private static void Prefix(UnitPart __instance, UnityEngine.Vector3 origin, float blastPower)
        {
            if (GameAccess.IsServer()) ZoneGarrisonManager.NoteShockwave(__instance, origin, blastPower);
        }
    }

    /// <summary>
    /// Server-side: a strongpoint nest never takes damage itself; hits on it wear its
    /// shell's structure, so clearing an occupied building means levelling it.
    /// </summary>
    [HarmonyPatch]
    internal static class NestDamageRedirectPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(UnitPart), "TakeDamage");
        private static bool Prepare() => TargetMethod() != null && GameAccess.MapBuildingHitPointsAvailable;

        private static bool Prefix(UnitPart __instance)
        {
            if (ZoneGarrisonManager.SelfDamage || !ZoneGarrisonManager.SiegeActive) return true;
            try
            {
                return ZoneGarrisonManager.ApplyNestHit(__instance);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Garrisons.NestDamageRedirect", e);
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
