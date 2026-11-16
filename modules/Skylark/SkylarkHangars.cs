using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    internal static class SkylarkHangars
    {
        private static readonly HashSet<string> CompatibleAircraft = new HashSet<string>(StringComparer.Ordinal)
        {
            "VTOLTrainer1", "AttackHelo1", "UtilityHelo1", "QuadVTOL1"
        };

        internal static void Include(Hangar hangar)
        {
            AircraftDefinition skylark = SkylarkTemplate.Definition;
            if (hangar == null || skylark == null) return;
            var aircraft = SkylarkTemplate.Get<AircraftDefinition[]>(hangar, "availableAircraft");
            if (aircraft == null || aircraft.Contains(skylark) || !aircraft.Any(IsCompatible)) return;
            var expanded = new AircraftDefinition[aircraft.Length + 1];
            Array.Copy(aircraft, expanded, aircraft.Length);
            expanded[aircraft.Length] = skylark;
            // Amend only this hangar's supported types; native busy, damage, airbase and HQ gates still run.
            SkylarkTemplate.Set(hangar, "availableAircraft", expanded);
        }

        private static bool IsCompatible(AircraftDefinition definition) =>
            definition != null && CompatibleAircraft.Contains(definition.jsonKey);

        internal static object Inspect()
        {
            AircraftDefinition skylark = SkylarkTemplate.Definition;
            var rows = UnityEngine.Object.FindObjectsOfType<Hangar>().Where(h => h.GetUnit() != null).Select(h =>
            {
                // Exercise the same patched entry points that the purchase list and spawn path use.
                AircraftDefinition[] offered = h.GetAvailableAircraft();
                var raw = SkylarkTemplate.Get<AircraftDefinition[]>(h, "availableAircraft");
                return new
                {
                    hangar = h.name, unit = h.GetUnit().name,
                    supports = raw.Where(IsCompatible).Select(d => d.jsonKey).ToArray(),
                    disabled = h.Disabled, available = h.Available,
                    listed = skylark != null && offered.Contains(skylark),
                    copies = skylark == null ? 0 : raw.Count(d => d == skylark),
                    canSpawn = skylark != null && h.CanSpawnAircraft(skylark)
                };
            }).ToArray();
            return new { registered = skylark != null, count = rows.Length, compatible = rows.Count(r => r.supports.Length > 0), rows };
        }
    }

    [HarmonyPatch]
    internal static class SkylarkHangarAvailabilityPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Hangar), nameof(Hangar.GetAvailableAircraft));
            yield return AccessTools.Method(typeof(Hangar), nameof(Hangar.CanSpawnAircraft));
        }

        private static void Prefix(Hangar __instance) => SkylarkHangars.Include(__instance);
    }
}
