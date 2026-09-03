using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    [HarmonyPatch(typeof(MapBuilding), nameof(MapBuilding.TakeDamage))]
    internal static class StrongholdMapBuildingDamagePatch
    {
        private static bool Prefix(
            MapBuilding __instance,
            float pierceDamage,
            float blastDamage,
            float amountAffected,
            float fireDamage,
            float impactDamage,
            PersistentID dealerID)
        {
            if (__instance == null) return true;

            StrongholdBuilding stronghold = __instance.GetComponent<StrongholdBuilding>();
            if (stronghold != null && !stronghold.IsDestroyed)
            {
                // Stronghold absorbs and mitigates damage with its heavy armor and large HP pool
                bool destroyed = stronghold.TakeDamage(
                    pierceDamage, blastDamage, amountAffected, fireDamage, impactDamage, dealerID);

                // If stronghold survives, skip vanilla TakeDamage so the 100 HP civilian pool doesn't collapse
                if (!destroyed)
                {
                    return false;
                }
                // If destroyed, proceed with original method so buildingSet ruins and wreckage spawn
                return true;
            }

            // General building damage shader integration: apply charring and heat glow if enabled
            if (Plugin.Settings.UrbanCombat.DamageShaderEnabled.Value)
            {
                BuildingDamageVisual visual = BuildingDamageVisual.GetOrAdd(__instance.gameObject);
                if (visual != null)
                {
                    float approxHp = BoscaliSummer.Runtime.GameAccess.GetMapBuildingHitPoints(__instance);
                    visual.ApplyDamage(
                        Mathf.Max(0f, approxHp - (pierceDamage + blastDamage + fireDamage + impactDamage)),
                        100f,
                        __instance.transform.position + Vector3.up * 2f,
                        blastDamage * 0.1f,
                        pierceDamage + blastDamage + fireDamage + impactDamage);
                }
            }

            return true;
        }
    }
}
