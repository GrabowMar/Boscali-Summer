using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Gamemodes.Domain;
using BoscaliSummer.Modules.Gamemodes.Runtime;
using HarmonyLib;
using NuclearOption.BuildScripts;
using NuclearOption.Networking;
using NuclearOption.SavedMission;

namespace BoscaliSummer.Modules.Gamemodes.Patches
{
    internal static class GamemodePatches
    {
        internal static readonly Type[] Types = [typeof(GamemodeLaunchPatch), typeof(GamemodeKillPatch),
            typeof(GamemodeCargoPatch), typeof(GamemodeSpawnPhasePatch), typeof(GamemodeScorePatch),
            typeof(HeavyweightInfrastructurePatch), typeof(HeavyweightQueuedSpawnPatch)];
    }

    [HarmonyPatch(typeof(CommandLineArgParser), nameof(CommandLineArgParser.LoadMission))]
    internal static class GamemodeLaunchPatch
    {
        internal static Func<string, Mission> Resolve;

        // After Scenario's LaunchMissionPatch (Maps' MapLaunchPatch runs after this): every prefix runs and the last __result writer wins.
        [HarmonyAfter("com.marci.boscalisummer.module.scenario")]
        private static bool Prefix(string missionName, ref Mission __result)
        {
            const string scheme = "gamemode:";
            if (Resolve == null || missionName == null) return true;
            string id = missionName.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) ? missionName.Substring(scheme.Length) : missionName;
            if (GamemodeMissionBuilder.FromId(id) == GameModeKind.None) return true;
            try
            {
                Mission mission = Resolve(id);
                if (mission == null) return true;
                __result = mission;
                return false;
            }
            catch (Exception e) { PatchGuard.Report("GamemodeLaunch", e); return true; }
        }
    }

    // The outgoing host RPC has the native final credited killer, unlike per-assist damage callbacks.
    [HarmonyPatch(typeof(MessageManager), nameof(MessageManager.RpcKillMessage))]
    internal static class GamemodeKillPatch
    {
        private static void Prefix(MessageManager __instance, PersistentID killerID, PersistentID killedID)
        {
            if (__instance.IsServer) GamemodeRuntime.Active?.Kill(killerID, killedID);
        }
    }

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnUnit), new[] { typeof(UnitDefinition), typeof(UnityEngine.Vector3),
        typeof(UnityEngine.Quaternion), typeof(UnityEngine.Vector3), typeof(Unit), typeof(Player) })]
    internal static class GamemodeCargoPatch
    {
        private static void Postfix(Spawner __instance, Unit __result, Unit owner, Player player)
        {
            if (__instance.IsServer && __result != null) GamemodeRuntime.Active?.CargoReleased(__result, owner, player);
        }
    }

    [HarmonyPatch(typeof(Airbase), nameof(Airbase.TrySpawnAircraft))]
    internal static class GamemodeSpawnPhasePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Airbase __instance, Player player, ref Airbase.TrySpawnResult __result)
        {
            if (!GamemodeRuntime.Competitive || !GameAccess.IsServer()) return true;
            if (GamemodeRuntime.CanFly && (GamemodeRuntime.Active?.AllowsHeavyweightSpawn(__instance, player) ?? false)) return true;
            __result = new Airbase.TrySpawnResult(false, null, false);
            return false;
        }
    }

    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.AddScore))]
    internal static class GamemodeScorePatch
    {
        private static bool Prefix() => !GamemodeRuntime.Competitive;
    }

    [HarmonyPatch(typeof(Hangar), "SpawnAircraft")]
    internal static class HeavyweightQueuedSpawnPatch
    {
        // Native hangars may open asynchronously after the request; revalidate roles at actual deployment.
        private static bool Prefix(Hangar __instance, Player player)
        {
            if (GamemodeRuntime.Kind != GameModeKind.Heavyweight) return true;
            if (GamemodeRuntime.CanFly && player != null && player.HQ == __instance.attachedUnit?.NetworkHQ &&
                GamemodeRuntime.Active?.AllowsHeavyweightSpawn(__instance.parentAirbase, player) == true) return true;
            if (player != null && __instance.IsServer) player.RpcClearSpawnPending();
            return false;
        }
    }

    [HarmonyPatch(typeof(UnitPart), nameof(UnitPart.TakeDamage))]
    internal static class HeavyweightInfrastructurePatch
    {
        // Fixed facilities are the course, not scoring targets; protect both legs' spawn/service access equally.
        private static bool Prefix(UnitPart __instance) => GamemodeRuntime.Kind != GameModeKind.Heavyweight ||
            !(__instance.parentUnit is Building);
    }
}
