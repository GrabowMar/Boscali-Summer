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
}
