using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Every hangar that offers the SFB-81 Darkreach also offers the G-240 (the Skylark's hangar pattern). The carrier never
    /// touches the ground, so the hangar's spawn is replaced by an air start straight above it.</summary>
    internal static class MegaplaneHangars
    {
        internal const float AirStartHeight = 1500f;   // above the hangar or the sea, whichever is higher

        internal static void Include(Hangar hangar)
        {
            AircraftDefinition mega = MegaplaneTemplate.Definition;
            if (hangar == null || mega == null) return;
            var field = Traverse.Create(hangar).Field<AircraftDefinition[]>("availableAircraft");
            AircraftDefinition[] aircraft = field.Value;
            if (aircraft == null || aircraft.Contains(mega) || !aircraft.Any(IsDarkreach)) return;
            var expanded = new AircraftDefinition[aircraft.Length + 1];
            Array.Copy(aircraft, expanded, aircraft.Length);
            expanded[aircraft.Length] = mega;
            field.Value = expanded;   // native busy, damage, airbase and HQ gates still run
        }

        private static bool IsDarkreach(AircraftDefinition d) => d != null && (d.jsonKey == "Darkreach" || d.unitName == "SFB-81 Darkreach");
    }

    [HarmonyPatch]
    internal static class MegaplaneHangarAvailabilityPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Hangar), nameof(Hangar.GetAvailableAircraft));
            yield return AccessTools.Method(typeof(Hangar), nameof(Hangar.CanSpawnAircraft));
        }

        private static void Prefix(Hangar __instance) => MegaplaneHangars.Include(__instance);
    }

    [HarmonyPatch(typeof(Hangar), "SpawnAircraft")]
    internal static class MegaplaneHangarAirStartPatch
    {
        private static bool Prefix(Hangar __instance, NuclearOption.Networking.Player player, AircraftDefinition definition, Loadout loadout, float fuelLevel,
            LiveryKey livery, Transform ___spawnTransform, ref GameObject ___spawnedObject, bool __runOriginal)
        {
            if (!__runOriginal || definition == null || definition.jsonKey != MegaplaneTemplate.Key) return true;
            Transform at = ___spawnTransform != null ? ___spawnTransform : __instance.transform;
            Vector3 forward = Vector3.ProjectOnPlane(at.forward, Vector3.up);
            Quaternion rotation = Quaternion.LookRotation(forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward, Vector3.up);
            // ponytail: straight above the hangar, no terrain probe around it; mountain bases may want a higher start
            Vector3 position = at.position;
            position.y = Mathf.Max(position.y, Datum.LocalSeaY) + MegaplaneHangars.AirStartHeight;
            float speed = (MegaplaneFlight.MinSpeed + MegaplaneFlight.MaxSpeed) * .5f;
            Aircraft aircraft = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(player, definition.unitPrefab, loadout, fuelLevel, livery,
                position.ToGlobalPosition(), rotation, rotation * Vector3.forward * speed, null, __instance.attachedUnit.NetworkHQ, null, 1f, .5f);
            if (aircraft == null) return false;
            if (loadout == null) aircraft.Networkloadout = aircraft.weaponManager.SelectAIAircraftWeapons(__instance.parentAirbase);
            ___spawnedObject = aircraft.gameObject;
            return false;
        }
    }
}
