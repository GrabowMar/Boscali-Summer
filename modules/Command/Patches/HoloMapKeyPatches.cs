using System;
using BoscaliSummer.Modules.Command.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Command.Patches
{
    /// <summary>
    /// Brackets the game's own map-key handler. <c>ExtraUiInput.Update</c> reads the Rewired "Map" button and, on a press, calls <c>DynamicMap.Maximize</c> (or
    /// <c>Minimize</c> when the map is already maximised); a Maximize call made while this method runs is therefore the map key's, and nothing else is: the mod's menus, panels and
    /// automation call Maximize from their own code and open the maximised map directly. The finalizer clears the flag even if the game's method throws.
    /// </summary>
    [HarmonyPatch(typeof(ExtraUiInput), "Update")]
    internal static class HoloMapKeyScopePatch
    {
        private static void Prefix() => HoloMap.InKeyUpdate = true;

        private static Exception Finalizer(Exception __exception)
        {
            HoloMap.InKeyUpdate = false;
            return __exception;
        }
    }

    /// <summary>
    /// The map key's Maximize, on key down with the corner map showing: the holo swallows the call and the original is skipped (its postfixes still run, which is why the Maximize postfixes check
    /// that the map is maximised); a tap then calls Maximize itself a few frames later, a hold shows the holo instead. With the holo off or unavailable the press is not taken, so the original
    /// opens the maximised map at once. Minimize and anything that is not the key's call are not touched.
    /// </summary>
    [HarmonyPatch(typeof(DynamicMap), "Maximize")]
    internal static class HoloMapKeyPatch
    {
        private static bool Prefix() => !HoloMap.KeyTakesMaximize();
    }
}
