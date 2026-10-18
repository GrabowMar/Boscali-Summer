using TMPro;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>Shared screen-space metrics and occupied edges for the local log, radio and cue column, in 1080p design units.</summary>
    internal static class LeftHudStack
    {
        public const float Left = 16f, Width = 560f, Inset = 12f, HeaderSize = 12f, BodySize = 15f;
        public const float ShadowAlpha = 0.06f;
        public static float LogBottom, CommsBottom;
        public static TMP_FontAsset Font;
        public static Material FontMaterial;

        public static float CommsTop => Mathf.Max(144f, LogBottom + 16f);
        public static float CueTop(float designHeight) => Mathf.Max(Mathf.Round(designHeight * 0.30f), Mathf.Max(LogBottom + 16f, CommsBottom + 20f));
    }
}
