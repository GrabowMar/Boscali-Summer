using BoscaliSummer.Modules.Gamemodes.Runtime;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using System.Collections.Generic;
using System.Reflection;

namespace BoscaliSummer.Modules.Gamemodes.Patches
{
    [HarmonyPatch(typeof(Airbase), nameof(Airbase.GetAvailableAircraft))]
    internal static class FuzzballAircraftListPatch
    {
        private static bool Prefix(Airbase __instance, ref List<AircraftDefinition> __result)
        {
            if (!FuzzballFlight.Active) return true;
            __result = FuzzballFlight.AircraftFor(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Airbase), nameof(Airbase.CanSpawnAircraft))]
    internal static class FuzzballCanSpawnPatch
    {
        private static bool Prefix(Airbase __instance, AircraftDefinition definition, ref bool __result)
        {
            if (!FuzzballFlight.Active) return true;
            __result = !__instance.disabled && FuzzballFlight.Allowed(__instance.CurrentHQ, definition);
            return false;
        }
    }

    [HarmonyPatch(typeof(Airbase), nameof(Airbase.AnyHangarsAvailable))]
    internal static class FuzzballHangarsPatch
    {
        private static bool Prefix(Airbase __instance, ref bool __result)
        {
            if (!FuzzballFlight.Active) return true;
            __result = FuzzballFlight.AircraftFor(__instance).Count > 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(Airbase), nameof(Airbase.TrySpawnAircraft))]
    internal static class FuzzballSpawnPatch
    {
        private static bool Prefix(Airbase __instance, Player player, AircraftDefinition definition,
            LiveryKey livery, Loadout loadout, float fuelLevel, ref Airbase.TrySpawnResult __result)
        {
            if (!FuzzballFlight.Active) return true;
            __result = FuzzballFlight.Spawn(__instance, player, definition, livery, loadout, fuelLevel);
            return false;
        }
    }

    [HarmonyPatch(typeof(UnitDefinition), nameof(UnitDefinition.IsAllowed))]
    internal static class FuzzballDefinitionPatch
    {
        private static void Postfix(UnitDefinition __instance, ref bool __result)
        {
            if (FuzzballFlight.Active && __result && FuzzballFlight.Surface(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnSavedUnit))]
    internal static class FuzzballSavedSpawnPatch
    {
        private static bool Prefix(SavedUnit savedUnit) => !FuzzballFlight.Active ||
            savedUnit is SavedAircraft || savedUnit is SavedMissile || savedUnit is SavedScenery;
    }

    [HarmonyPatch(typeof(MountedCargo), nameof(MountedCargo.Fire))]
    internal static class FuzzballCargoPatch
    {
        private static bool Prefix() => !FuzzballFlight.Active;
    }

    [HarmonyPatch(typeof(MountedTroops), nameof(MountedTroops.Fire))]
    internal static class FuzzballTroopsPatch
    {
        private static bool Prefix() => !FuzzballFlight.Active;
    }

    [HarmonyPatch(typeof(VehicleDepot), nameof(VehicleDepot.TrySpawnVehicle))]
    internal static class FuzzballDepotPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!FuzzballFlight.Active) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.SpawnEjectingPilot))]
    internal static class FuzzballPilotPatch
    {
        private static bool Prefix(Aircraft __instance, int pilotNumber)
        {
            if (!FuzzballFlight.Active) return true;
            if (__instance.IsServer && __instance.pilots != null && pilotNumber >= 0 && pilotNumber < __instance.pilots.Length)
                __instance.pilots[pilotNumber].ejected = true;
            return false;
        }
    }

    // OnSceneLoaded has linked all native airbases; StartMission has not started objectives yet.
    [HarmonyPatch(typeof(Mission), nameof(Mission.OnSceneLoaded))]
    internal static class FuzzballSceneReadyPatch
    {
        private static void Postfix() => FuzzballFlight.SceneReady();
    }

    [HarmonyPatch]
    internal static class CompetitiveFactoryPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Factory), "ProduceUnit");
            yield return AccessTools.Method(typeof(Factory), "ProduceWarhead");
        }

        private static bool Prefix() => !GamemodeRuntime.Competitive;
    }
}
