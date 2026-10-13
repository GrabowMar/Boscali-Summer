using System;
using HarmonyLib;

namespace BoscaliSummer.Core.Game
{
    /// <summary>Keep embedded Boscali equipment out of ordinary mission and saved-loadout paths.</summary>
    internal static class MissionContentPatches
    {
        internal static readonly Type[] PatchTypes =
        {
            typeof(UnitAllowedPatch), typeof(MountAllowedPatch),
            typeof(LoadHardpointPatch), typeof(SpawnMountsPatch)
        };

        private static bool Blocked(string jsonKey) => MissionProfile.BlocksContent(jsonKey);

        [HarmonyPatch(typeof(UnitDefinition), nameof(UnitDefinition.IsAllowed))]
        private static class UnitAllowedPatch
        {
            [HarmonyPostfix]
            private static void Postfix(UnitDefinition __instance, ref bool __result)
            {
                if (__result && Blocked(__instance.jsonKey)) __result = false;
            }
        }

        [HarmonyPatch(typeof(WeaponMount), nameof(WeaponMount.IsAllowed))]
        private static class MountAllowedPatch
        {
            [HarmonyPostfix]
            private static void Postfix(WeaponMount __instance, ref bool __result)
            {
                if (__result && Blocked(__instance.jsonKey)) __result = false;
            }
        }

        [HarmonyPatch(typeof(WeaponManager), "LoadHardpointSet")]
        private static class LoadHardpointPatch
        {
            [HarmonyPrefix]
            private static void Prefix(ref WeaponMount weaponMount)
            {
                // Native saved-loadout loading bypasses IsAllowed. A null argument follows its
                // normal RemoveMounts path and preserves native costs, stations and pylons.
                if (weaponMount != null && Blocked(weaponMount.jsonKey)) weaponMount = null;
            }
        }

        [HarmonyPatch(typeof(HardpointSet), nameof(HardpointSet.SpawnMounts))]
        private static class SpawnMountsPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(WeaponMount weaponMount) =>
                weaponMount == null || !Blocked(weaponMount.jsonKey);
        }
    }
}
