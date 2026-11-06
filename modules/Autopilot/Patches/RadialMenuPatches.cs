using BoscaliSummer.Modules.Autopilot.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Autopilot.Patches
{
    [HarmonyPatch(typeof(RadialMenuMain))]
    internal static class RadialMenuLifecyclePatches
    {
        /// <summary>While the interaction menu is open the native wheel stays frozen, and while the menu owns the Radial Menu binding the wheel never
        /// sees that button held (its timed press is what opens it). The Weapon Wheel binding is untouched.</summary>
        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        private static bool Update_Prefix()
        {
            if (Presentation.AceRadialMenuUi.IsOpenNow) return false;
            Rewired.Player pad = Presentation.AceRadialMenuUi.OwnsRadialButton ? GameManager.playerInput : null;
            return pad == null || !pad.GetButton(AceRadialInputSource.PadPress);
        }
    }

    /// <summary>The pad's Cancel button is the interaction menu's BACK. The game reads the same button in <c>ExtraUiInput.Update</c> to drop the
    /// target (tap) or every target (hold), so that update is skipped while a Cancel press the menu used is down or just released.</summary>
    [HarmonyPatch(typeof(ExtraUiInput), "Update")]
    internal static class MenuCancelSwallowPatch
    {
        private static bool swallow;

        /// <summary>The input source calls this on the frame the menu takes a Cancel press (the menu may close on that same frame).</summary>
        public static void Swallow() => swallow = true;

        [HarmonyPrefix]
        private static bool Prefix()
        {
            Rewired.Player p = GameManager.playerInput;
            if (p == null) return true;
            bool held = p.GetButton(AceRadialInputSource.PadCancel), up = p.GetButtonUp(AceRadialInputSource.PadCancel);
            if (Presentation.AceRadialMenuUi.IsOpenNow && held) swallow = true;
            if (!held && !up) swallow = false;
            return !(swallow && (held || up));
        }
    }
}
