using System.Reflection;
using HarmonyLib;
using UnityEngine;
using BoscaliSummer.Framework.Visuals;

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

            // For regular civilian buildings: when struck by weapons, place a localized
            // breach crater and ground rubble at the exterior hit site without modifying materials.
            float totalDamage = pierceDamage + blastDamage + fireDamage + impactDamage;
            if (totalDamage > 2f)
            {
                BuildingDamageVisual visual = BuildingDamageVisual.GetOrAdd(__instance.gameObject);
                if (visual != null)
                {
                    Vector3 buildingCenter = __instance.transform.position + Vector3.up * 4f;
                    Collider col = __instance.GetComponentInChildren<Collider>();
                    if (col != null) buildingCenter = col.bounds.center;

                    Vector3 hitPoint = buildingCenter;
                    Vector3 normal = Vector3.up;

                    Vector3 fromDir = Vector3.forward;
                    if (dealerID.IsValid && dealerID.TryGetUnit(out Unit attacker) && attacker != null)
                    {
                        fromDir = (buildingCenter - attacker.transform.position).normalized;
                    }
                    else if (Camera.main != null)
                    {
                        fromDir = (buildingCenter - Camera.main.transform.position).normalized;
                    }

                    Vector3 rayStart = buildingCenter - fromDir * 35f;
                    if (Physics.Raycast(rayStart, fromDir, out RaycastHit hit, 45f, PhysicsLayers.StaticsMask))
                    {
                        hitPoint = hit.point;
                        normal = hit.normal;
                    }
                    else if (col != null)
                    {
                        hitPoint = col.ClosestPoint(rayStart);
                        normal = (hitPoint - buildingCenter).normalized;
                    }

                    visual.ApplyLocalImpact(hitPoint, normal, blastDamage * 0.1f, totalDamage);
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(MapBuilding), nameof(MapBuilding.TakeShockwave))]
    internal static class StrongholdMapBuildingShockwavePatch
    {
        private static void Postfix(
            MapBuilding __instance,
            Vector3 origin,
            float blastEffectScale,
            float blastPower)
        {
            if (__instance == null || blastPower < 0.1f) return;

            BuildingDamageVisual visual = BuildingDamageVisual.GetOrAdd(__instance.gameObject);
            if (visual != null)
            {
                Vector3 buildingCenter = __instance.transform.position + Vector3.up * 6f;
                Collider col = __instance.GetComponentInChildren<Collider>();
                if (col != null) buildingCenter = col.bounds.center;

                Vector3 fromBlast = (buildingCenter - origin).normalized;
                if (fromBlast.sqrMagnitude < 0.01f) fromBlast = Vector3.forward;

                Vector3 hitPoint = buildingCenter;
                Vector3 normal = -fromBlast;

                // Cast ray from blast origin directly into the building facade
                if (Physics.Raycast(origin - fromBlast * 1.5f, fromBlast, out RaycastHit hit, 90f, PhysicsLayers.StaticsMask))
                {
                    hitPoint = hit.point;
                    normal = hit.normal;
                }
                else
                {
                    // Cast from building perimeter back towards the blast
                    Vector3 toBlast = (origin - buildingCenter).normalized;
                    if (Physics.Raycast(buildingCenter + toBlast * 40f, -toBlast, out RaycastHit hitBack, 55f, PhysicsLayers.StaticsMask))
                    {
                        hitPoint = hitBack.point;
                        normal = hitBack.normal;
                    }
                }

                visual.ApplyLocalImpact(hitPoint, normal, blastPower, blastPower * 12f);
            }
        }
    }
}
