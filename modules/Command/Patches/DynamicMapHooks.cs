using System;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Patches
{
    [HarmonyPatch(typeof(DynamicMap), "Maximize")]
    internal static class DynamicMapMaximizePatch
    {
        public static event Action OnMaximized;

        // The holo map swallows the map key's key-down Maximize in the prefix (a tap calls Maximize itself on release): the original is skipped, the postfix still runs, and nothing was maximised.
        private static void Postfix()
        {
            if (DynamicMap.mapMaximized) OnMaximized?.Invoke();
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
        private static bool Prefix(DynamicMap __instance)
        {
            if (MfdMapInteractions.BlockNativeControls(__instance)) return false;
            if (!MapUiPointer.OverControls()) return true;
            if (Input.mouseScrollDelta.y != 0f) return false;
            return !Input.GetMouseButton(0) && !Input.GetMouseButton(1);
        }

        /// <summary>Runs even when a prefix skipped the method: the relief keeps the native sheet still.</summary>
        private static void Postfix(DynamicMap __instance) => MfdTerrainRelief.PinNative(__instance);
    }

    /// <summary>
    /// A completed box or relief grab drag must not also click the icon beneath its release, and one mouse click is one click
    /// (GitHub issue 14). The game hands a left click to the icons twice: its Select action on the press, to the icon NEAREST the cursor
    /// within 100 px (source Controller), then the pointer click on the release, to the icon UNDER it (source Mouse). On the maximised map
    /// the second one toggles, so the pair selected two contacts when the two icons differed and selected then dropped one when they were
    /// the same. The press is dropped while the left mouse button is in play; a gamepad's Select has no mouse button and keeps the game's click.
    /// </summary>
    [HarmonyPatch(typeof(UnitMapIcon), nameof(UnitMapIcon.ClickIcon))]
    internal static class MapBoxIconClickGuardPatch
    {
        private static bool Prefix(UnitMapIcon __instance, MapIcon.ClickSource clickSource)
        {
            if (MfdMapInteractions.BlockIconClick() || ReliefNavigator.BlockIconClick()) return false;
            bool pointer = clickSource == MapIcon.ClickSource.Mouse;
            if (!pointer && DynamicMap.mapMaximized &&
                (Input.GetMouseButton(0) || Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0))) return false;
            // The pointer click may arrive the frame after the release: it is the map's toggle either way, never the game's own select.
            return !MfdMapInteractions.HandleUnitClick(__instance, pointer);
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
