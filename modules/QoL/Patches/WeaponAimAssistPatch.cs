using System.Reflection;
using HarmonyLib;

namespace BoscaliSummer.Features.QoL.Patches
{
    // Preserve the game's lead/impact calculation, but stop its stick-input correction.
    [HarmonyPatch(typeof(ControlsFilter), nameof(ControlsFilter.GetAim))]
    internal static class WeaponAimAssistPatch
    {
        private static readonly FieldInfo Enabled = typeof(ControlsFilter)
            .GetNestedType("AimAssist", BindingFlags.NonPublic)?
            .GetField("Enabled", BindingFlags.Instance | BindingFlags.Public);

        private static void Prefix(object ___aimAssist)
        {
            if (___aimAssist != null && Enabled != null && (bool)Enabled.GetValue(___aimAssist))
                Enabled.SetValue(___aimAssist, false);
        }
    }
}
