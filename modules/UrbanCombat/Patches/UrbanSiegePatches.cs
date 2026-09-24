using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Urban zones defend with their size plus every intact rooftop nest. Runs once per
    /// capture tick (1 Hz per zone), so the whole siege costs two dictionary lookups.
    /// </summary>
    [HarmonyPatch]
    internal static class UrbanDefensePatch
    {
        private static MethodBase TargetMethod()
        {
            MethodBase direct = AccessTools.Method(typeof(Airbase), "ICapturable.get_CaptureDefense");
            if (direct != null) return direct;
            foreach (MethodInfo candidate in typeof(Airbase).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
                if (candidate.Name.EndsWith(".get_CaptureDefense", StringComparison.Ordinal))
                    return candidate;
            return null;
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(Airbase __instance, ref float __result)
        {
            if (__instance == null) return;
            int tier = ZoneGarrisonManager.TierFor(__instance);
            int nests = ZoneGarrisonManager.IntactNestsFor(__instance);
            if (tier < 1 && nests < 1) return;
            __result = SiegeMath.ApplyDefense(__result, tier, nests, ZoneGarrisonManager.SiegeScale);
        }
    }

    /// <summary>
    /// While defender strongpoints stand, control cannot drain past the siege floor: the
    /// zone must be reduced nest by nest before it can go neutral. Only the drain phase
    /// is gated; the fill phase and un-garrisoned zones keep vanilla pacing.
    /// </summary>
    [HarmonyPatch]
    internal static class SiegeFloorPatch
    {
        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(Capture), "target");
        private static readonly Dictionary<int, float> floorLogAt = new Dictionary<int, float>();
        internal static bool Available => TargetField != null;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Capture), "ApplyChange");
        private static bool Prepare() => TargetMethod() != null && Available;

        private static void Prefix(Capture __instance, ref float change)
        {
            if (__instance == null || change >= 0f || !ZoneGarrisonManager.SiegeActive) return;
            if (!(TargetField.GetValue(__instance) is Airbase airbase)) return;
            if (airbase.CurrentHQ == null) return;
            int nests = ZoneGarrisonManager.IntactNestsFor(airbase);
            float floor = SiegeMath.SiegeFloor(ZoneGarrisonManager.TierFor(airbase),
                nests, ZoneGarrisonManager.SiegeScale);
            if (floor <= 0f) return;
            float balance = __instance.controlBalance;
            float original = change;
            if (balance <= floor) change = 0f;
            else if (balance + change < floor) change = floor - balance;
            if (change == original) return;
            int key = airbase.GetInstanceID();
            float now = Time.unscaledTime;
            if (floorLogAt.TryGetValue(key, out float nextAllowed) && now < nextAllowed) return;
            if (floorLogAt.Count >= 64) floorLogAt.Clear();
            floorLogAt[key] = now + 30f;
            string stronghold = !string.IsNullOrEmpty(airbase.NetworknetworkUniqueName)
                ? airbase.NetworknetworkUniqueName
                : airbase.name;
            Plugin.Logger.LogInfo($"[SIEGE] Strongpoints hold {stronghold} at {balance:P0} ({nests} nests stand).");
        }
    }

    /// <summary>
    /// Ground vehicles lead urban assaults: inside towns and larger, their capture
    /// strength rises with the tier, so armor columns crack cities while infantry
    /// alone besieges only slowly.
    /// </summary>
    [HarmonyPatch]
    internal static class UrbanArmorPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Unit), "get_CaptureStrength");
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(Unit __instance, ref float __result)
        {
            if (__instance == null || __result <= 0f || !(__instance is GroundVehicle)) return;
            int tier = ZoneGarrisonManager.TierAt(__instance.transform.position);
            if (tier < 1) return;
            __result *= SiegeMath.ArmorMultiplier(tier, ZoneGarrisonManager.SiegeScale);
        }
    }
}
