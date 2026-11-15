using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Spooktober.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Patches
{
    /// <summary>Hell's units and captured structures use their own violet ink for every viewer.</summary>
    [HarmonyPatch(typeof(UnitMapIcon), "GetColor")]
    internal static class HellMapColourPatch
    {
        private static void Postfix(UnitMapIcon __instance, ref Color __result, bool ___isSelected)
        {
            try
            {
                if (HellArmy.IsHell(__instance.unit))
                    __result = ___isSelected ? HellArmy.MapInkSelected : HellArmy.MapInk;
            }
            catch (Exception e) { PatchGuard.Report("Spooktober.HellMapColour", e); }
        }
    }

    // AirbaseMapIcon has its own GetColor, which otherwise draws a hostile captured airfield as friendly.
    [HarmonyPatch(typeof(AirbaseMapIcon), "GetColor")]
    internal static class HellAirbaseMapColourPatch
    {
        private static void Postfix(AirbaseMapIcon __instance, ref Color __result, bool ___isSelected)
        {
            try
            {
                if (__instance.airbase != null && HellSide.Side.Owns(__instance.airbase.CurrentHQ))
                    __result = ___isSelected ? HellArmy.MapInkSelected : HellArmy.MapInk;
            }
            catch (Exception e) { PatchGuard.Report("Spooktober.HellAirbaseMapColour", e); }
        }
    }
}
