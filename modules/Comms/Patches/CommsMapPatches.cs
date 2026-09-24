using BoscaliSummer.Features.Comms.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Features.Comms.Patches
{
    /// <summary>
    /// While the pen is down, the map holds still. Without this a stroke drags the map under
    /// itself and every line comes out as a smear. Only mouse-held frames with a drawing tool
    /// armed (or the hold-to-draw key down) are skipped; the wheel, the keyboard, HOTAS map
    /// controls and every other frame reach the map untouched.
    /// </summary>
    [HarmonyPatch(typeof(DynamicMap), "MapControls")]
    internal static class CommsMapControlsPatch
    {
        private static bool Prefix() => !CommsInput.HoldMapPan();
    }
}
