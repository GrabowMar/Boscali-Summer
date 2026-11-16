using TMPro;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>Shared screen-space metrics and occupied edges for the local log, radio and cue column, in 1080p design units.</summary>
    internal static class LeftHudStack
    {
        public const float Left = 12f, Width = HudZones.WideLog, Inset = 12f, HeaderSize = 12f, BodySize = 15f;
        public static float LogBottom, CommsBottom;
        /// <summary>The game's own feed box is on screen (the mod's log is not standing in, e.g. in free camera): its bottom edge in screen pixels from the top, else 0.</summary>
        public static float NativeFeedBottomPx;
        public static TMP_FontAsset Font;
        public static Material FontMaterial;

        /// <summary>The zone map's slots (<see cref="HudZones"/>): COMMS under a LOG that grew past its zone, CUE under a COMMS that did.</summary>
        public static float CommsTop => HudZones.CommsTop(LogBottom);
        public static float CueTop => HudZones.CueTop(Mathf.Max(LogBottom, CommsBottom));
    }
}
