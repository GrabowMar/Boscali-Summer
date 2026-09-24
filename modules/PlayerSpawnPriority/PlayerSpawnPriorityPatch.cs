using System;
using BoscaliSummer.Infrastructure.Diagnostics;
using BoscaliSummer.Runtime;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Features.PlayerSpawnPriority
{
    [HarmonyPatch(typeof(Hangar), nameof(Hangar.TrySpawnAircraft))]
    internal static class PlayerSpawnPriorityPatch
    {
        private static void Prefix(Hangar __instance, Player player, AircraftDefinition definition,
            GameObject ___spawnedObject, float ___clearDistance)
        {
            try
            {
                if (player == null || definition == null || !__instance.IsServer ||
                    __instance.Disabled || __instance.Available || ___spawnedObject == null)
                    return;

                Airbase airbase = __instance.parentAirbase;
                if (airbase == null || airbase.disabled) return;
                foreach (Hangar other in airbase.hangars)
                    if (other != null && other != __instance && other.CanSpawnAircraft(definition))
                        return;

                if (Array.IndexOf(__instance.GetAvailableAircraft(), definition) < 0 ||
                    !___spawnedObject.TryGetComponent(out Aircraft occupant))
                    return;

                Transform spawn = __instance.GetSpawnTransform();
                float radius = Mathf.Max(___clearDistance, definition.length, definition.width,
                    definition.height);
                if (spawn == null || !SpawnPriorityPolicy.CanEvict(player != null,
                    occupant.Player == null,
                    WingLink.IsWingMember(occupant.NetworkpersistentID.GetHashCode()),
                    (occupant.transform.position - spawn.position).sqrMagnitude <= radius * radius))
                    return;

                // The request is already past Spawner.AllowedToSpawn and loadout vetting.
                __instance.ServerObjectManager.Destroy(occupant.gameObject);
                __instance.Networkavailable = true;
            }
            catch (Exception error)
            {
                PatchGuard.Report("PlayerSpawnPriority.TrySpawnAircraft", error);
            }
        }
    }
}
