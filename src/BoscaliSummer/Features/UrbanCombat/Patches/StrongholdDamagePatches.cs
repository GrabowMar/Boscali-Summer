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

            // For regular civilian buildings: when struck by heavy ordnance, place a localized
            // breach crater and ground rubble at the hit site without modifying building materials.
            if (blastDamage > 12f)
            {
                BuildingDamageVisual visual = BuildingDamageVisual.GetOrAdd(__instance.gameObject);
                if (visual != null)
                {
                    Vector3 hitPoint = __instance.transform.position + Vector3.up * 3f;
                    Vector3 normal = Vector3.forward;

                    if (dealerID.IsValid && dealerID.TryGetUnit(out Unit attacker) && attacker != null)
                    {
                        Vector3 attackerPos = attacker.transform.position;
                        Vector3 toCenter = (__instance.transform.position + Vector3.up * 3f) - attackerPos;
                        if (Physics.Raycast(attackerPos, toCenter.normalized, out RaycastHit hit, toCenter.magnitude + 20f, PhysicsLayers.StaticsMask))
                        {
                            hitPoint = hit.point;
                            normal = hit.normal;
                        }
                        else
                        {
                            normal = (-toCenter).normalized;
                        }
                    }

                    visual.ApplyLocalImpact(hitPoint, normal, blastDamage * 0.1f, blastDamage);
                }
            }

            return true;
        }
    }
}
