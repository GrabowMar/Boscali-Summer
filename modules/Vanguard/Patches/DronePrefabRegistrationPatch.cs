using System;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using Mirage;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    [HarmonyPatch]
    internal static class DronePrefabRegistrationPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ClientObjectManager), "GetSpawnHandler", new[] { typeof(int) });
        private static bool Prepare() => TargetMethod() != null;

        // Blueprinter can finish its catalog after the game's one-time prefab registration pass.
        private static Exception Finalizer(ClientObjectManager __instance, int prefabHash, ref SpawnHandler __result, Exception __exception)
        {
            if (__exception is not SpawnObjectException || prefabHash == 0) return __exception;
            try
            {
                Encyclopedia encyclopedia = GameAccess.EncyclopediaOrNull;
                if (encyclopedia == null) return __exception;
                foreach (MissileDefinition drone in encyclopedia.missiles)
                {
                    if (drone == null || drone.unitPrefab == null || !VanguardKeys.IsSteerable(VanguardKeys.RoleOf(drone.jsonKey)) ||
                        !drone.unitPrefab.TryGetComponent(out NetworkIdentity identity) || identity.PrefabHash != prefabHash) continue;
                    __instance.RegisterPrefab(identity);
                    __result = new SpawnHandler(identity);
                    return null;
                }
            }
            catch (Exception error) { PatchGuard.Report("Vanguard.DronePrefabRegistration", error); }
            return __exception;
        }
    }
}
