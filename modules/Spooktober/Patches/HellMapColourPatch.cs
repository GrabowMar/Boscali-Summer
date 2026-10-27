using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Spooktober.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Patches
{
    /// <summary>Hell's Army draws black on the map (deep red when selected) whatever side the viewer is on. Hot path: every map
    /// icon colour update; one count check while the army is empty.</summary>
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
}
