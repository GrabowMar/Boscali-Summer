using System.Collections.Generic;
using HarmonyLib;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Keeps exactly one MFD panel on screen at a time.
    ///
    /// <para>Vanilla radio-buttons each bezel column <em>independently</em>: opening a left
    /// screen closes the other left screens and does not touch the right column, because the
    /// two columns render on opposite sides of the map and can happily both be open. The
    /// three-column layout renders every mod panel into the same left dock, so a left screen
    /// and a right screen open together would sit on top of each other — which the player
    /// sees as WMC and RAD fighting over one rectangle.</para>
    ///
    /// <para>Ordinary screen presses reconcile after vanilla decides what to open. MAP alone redirects before
    /// the native radio-button mutation, because its independent drawer must preserve the docked screen. Registered
    /// in <c>CommandModule.PatchTypes</c> — an unlisted patch class is skipped in
    /// total silence.</para>
    /// </summary>
    [HarmonyPatch]
    internal static class MfdSinglePanelPatch
    {
        private static MFDScreen lastOpened;
        private static bool hasSelection;

        public static MFDScreen ActiveScreen => lastOpened;

        public static void Reset()
        {
            lastOpened = null;
            hasSelection = false;
        }

        /// <summary>A screen the mod showed itself (the column fold reopening its panel) becomes the one open panel.</summary>
        public static void Adopt(MFDScreen screen)
        {
            lastOpened = screen;
            hasSelection = true;
        }

        public static void Reconcile(VirtualMFD mfd)
        {
            if (mfd == null || !MfdPresentation.Expanded) return;
            MfdPanelDock.DockModScreens(mfd);
            // Native maximise restores its remembered bezel screens before the dock is built.
            // A folded column keeps them closed until an explicit panel selection unfolds it.
            if (MfdColumnFold.Folded)
            {
                MfdPanelDock.CloseOthers(mfd, null);
                Reset();
                return;
            }
            if (lastOpened != null && !MfdPanelDock.IsDocked(lastOpened)) Reset();
            if (!hasSelection)
            {
                FindActive(MapUiAccess.GetLeftScreens(mfd));
                FindActive(MapUiAccess.GetRightScreens(mfd));
            }
            if (lastOpened != null)
                MfdPanelDock.CloseOthers(mfd, lastOpened);
            if (lastOpened != null && !lastOpened.isActive)
                lastOpened.ShowScreen(UnityEngine.Vector3.zero);
            MfdRailPatch.ReLayoutForActiveScreen(lastOpened);
        }

        private static void FindActive(List<MFDScreen> screens)
        {
            if (screens == null) return;
            foreach (var screen in screens)
                if (screen != null && screen.isActive && MfdPanelDock.IsDocked(screen))
                {
                    lastOpened = screen;
                    hasSelection = true;
                }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(VirtualMFD), nameof(VirtualMFD.PressLeftButton))]
        public static bool PressLeftPrefix(VirtualMFD __instance, Button button) =>
            BeforePress(__instance, button, left: true);

        [HarmonyPrefix]
        [HarmonyPatch(typeof(VirtualMFD), nameof(VirtualMFD.PressRightButton))]
        public static bool PressRightPrefix(VirtualMFD __instance, Button button) =>
            BeforePress(__instance, button, left: false);

        private static bool BeforePress(VirtualMFD mfd, Button button, bool left)
        {
            if (mfd == null || button == null || !MfdRailPatch.IsApplied || !DynamicMap.mapMaximized) return true;
            MFDScreen pressed = ScreenFor(mfd, button, left);
            if (pressed == null || pressed.GetComponent<MapOptions>() == null) return true;
            // Native Press hides the whole bezel column before ShowScreen. Route MAP before that mutation so the
            // independent drawer neither closes the current left panel nor becomes the native remembered screen.
            // The source is deliberately stowed, so its enabled/interactable flags carry availability, not its hierarchy.
            bool hasAircraft = SceneSingleton<CombatHUD>.i != null && SceneSingleton<CombatHUD>.i.aircraft != null;
            if (button.enabled && button.interactable && (!pressed.aircraftOnly || hasAircraft)) MfdMapControls.Open();
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(VirtualMFD), nameof(VirtualMFD.PressLeftButton))]
        public static void PressLeftPostfix(VirtualMFD __instance, Button button) =>
            AfterPress(__instance, button, left: true);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(VirtualMFD), nameof(VirtualMFD.PressRightButton))]
        public static void PressRightPostfix(VirtualMFD __instance, Button button) =>
            AfterPress(__instance, button, left: false);

        private static void AfterPress(VirtualMFD mfd, Button button, bool left)
        {
            if (mfd == null || button == null) return;
            if (!MfdPresentation.Expanded) return;

            MFDScreen pressed = ScreenFor(mfd, button, left);
            if (pressed != null && pressed.GetComponent<MapOptions>() != null) return;

            // A press that closed its own screen leaves nothing to protect, and a button with
            // no screen behind it is a spare slot.
            if (pressed == null || !MfdPanelDock.IsDocked(pressed)) return;
            lastOpened = pressed.isActive ? pressed : null;
            hasSelection = true;
            MfdPanelDock.CloseOthers(mfd, lastOpened);
            // A rail key on a folded column opens it with the pressed panel.
            if (lastOpened != null && MfdColumnFold.Folded) MfdColumnFold.Unfold(false);
            MfdRailPatch.ReLayoutForActiveScreen(lastOpened);
        }

        /// <summary>
        /// Translates a bezel button back to the <see cref="MFDScreen"/> it opens.
        ///
        /// Vanilla indexes the screens list by the button's index in the buttons list without
        /// checking its length — a short list throws inside a UI callback. This does the same
        /// lookup with the bounds check vanilla omits, so a mod that has claimed a slot past
        /// the end of the screens list cannot turn a button press into an exception.
        /// </summary>
        private static MFDScreen ScreenFor(VirtualMFD mfd, Button button, bool left)
        {
            List<Button> buttons = left ? MapUiAccess.GetLeftButtons(mfd) : MapUiAccess.GetRightButtons(mfd);
            List<MFDScreen> screens = left ? MapUiAccess.GetLeftScreens(mfd) : MapUiAccess.GetRightScreens(mfd);
            if (buttons == null || screens == null) return null;

            int index = buttons.IndexOf(button);
            if (index < 0 || index >= screens.Count) return null;

            return screens[index];
        }
    }
}
