using System;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Patches
{
    [HarmonyPatch(typeof(DynamicMap), "Maximize")]
    internal static class DynamicMapMaximizePatch
    {
        public static event Action OnMaximized;

        private static void Postfix()
        {
            OnMaximized?.Invoke();
        }
    }

    [HarmonyPatch(typeof(DynamicMap), "Minimize")]
    internal static class DynamicMapMinimizePatch
    {
        public static event Action OnMinimized;

        private static void Postfix()
        {
            OnMinimized?.Invoke();
        }
    }

    /// <summary>
    /// Mouse input over the instrument column or the control rail must not reach the map.
    ///
    /// <para><c>MapControls</c> reads the mouse through Rewired, not through uGUI, so the
    /// panels cannot consume it: scrolling a list or dragging its scrollbar also zoomed or
    /// panned the map underneath. While the pointer is over the mod's controls, wheel and
    /// mouse-held frames are skipped; camera follow resumes the moment the pointer leaves.
    /// Keyboard, HOTAS and controller map actions are untouched.</para>
    /// </summary>
    [HarmonyPatch(typeof(DynamicMap), "MapControls")]
    internal static class MapControlsPanelGuardPatch
    {
        private static bool Prefix()
        {
            if (!MapUiPointer.OverControls()) return true;
            if (Input.mouseScrollDelta.y != 0f) return false;
            return !Input.GetMouseButton(0) && !Input.GetMouseButton(1);
        }
    }

    /// <summary>
    /// A cursor position over the panels is not a map position. Armed support call-ins and
    /// Wing Command's point orders resolve through this method, so a click that lands on a
    /// panel stays a UI click instead of dropping an order on the terrain behind it.
    /// </summary>
    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.TryGetCursorCoordinates))]
    internal static class MapCursorPanelGuardPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!MapUiPointer.OverControls()) return true;
            __result = false;
            return false;
        }
    }
}
