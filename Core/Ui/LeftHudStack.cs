using BoscaliSummer.Core.Contracts;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>Shared screen-space metrics and occupied edges for the local log, radio and cue column, in 1080p design units.</summary>
    internal static class LeftHudStack
    {
        public const float Left = HudZones.Margin, Width = HudZones.WideLog, Inset = 12f, HeaderSize = HudType.Label, BodySize = HudType.Body;
        public static float LogBottom, CommsBottom;
        /// <summary>The game's own feed box is on screen (the mod's log is not standing in, e.g. in free camera): its bottom edge in screen pixels from the top, else 0.</summary>
        public static float NativeFeedBottomPx;
        /// <summary>How far the visor's power-on has brought the radio column up, 0..1 (1 outside a boot): the COMMS block follows the board's left column while it comes alive (<c>StatusPanel.ApplyBoot</c>).</summary>
        public static float BootAlpha = 1f;
        /// <summary>The left column's sway and tear this frame, written by the board in LateUpdate and read in <c>HudLayer.Paint</c> the same frame (no one-frame lag).</summary>
        public static float ShiftX, ShiftY;
        public static bool TearLeft;
        public static TMP_FontAsset Font;
        public static Material FontMaterial;

        /// <summary>The zone map's slots (<see cref="HudZones"/>): COMMS under a LOG that grew past its zone, CUE under a COMMS that did.</summary>
        public static float CommsTop => HudZones.CommsTop(LogBottom);
        public static float CueTop => HudZones.CueTop(Mathf.Max(LogBottom, CommsBottom));
    }
}
