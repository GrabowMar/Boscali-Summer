using BoscaliSummer.Modules.Support.Domain.C2;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>Text and paint helpers shared by the C2 terminal parts. Colours always come from palette roles.</summary>
    internal static class C2Kit
    {
        /// <summary>A fixed-size mono line (codes, numbers, console). Never auto-sizes: callers truncate with <see cref="FitTo"/>.</summary>
        public static TMP_Text Mono(RectTransform parent, string name, float size, TextAlignmentOptions align, bool strong = false, float tracking = 0f)
        {
            TMP_Text t = AvText.Make(parent, name, strong ? AvTextRole.DataStrong : AvTextRole.DataSmall, "", align, false);
            AvText.Size(t, size);
            t.characterSpacing = tracking;
            t.richText = true;
            return t;
        }

        /// <summary>A fixed-size condensed line in a kit role at an explicit size.</summary>
        public static TMP_Text Cond(RectTransform parent, string name, AvTextRole role, float size, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(parent, name, role, "", align, false);
            AvText.Size(t, size);
            t.richText = true;
            return t;
        }

        public static float Width(TMP_Text t, string s) =>
            string.IsNullOrEmpty(s) ? 0f : Mathf.Ceil(t.GetPreferredValues(s, 100000f, 100f).x);

        /// <summary>
        /// The longest prefix of <paramref name="s"/> (ending in an ellipsis when cut) whose width in this text object's
        /// font fits <paramref name="width"/>. Plain text only (callers add rich colour tags after fitting).
        /// </summary>
        public static string FitTo(TMP_Text t, string s, float width)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (width <= 0f) return "";
            if (Width(t, s) <= width) return s;
            int lo = 0, hi = s.Length - 1; // keep `lo` chars + ellipsis
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Width(t, s.Substring(0, mid) + "\u2026") <= width) lo = mid; else hi = mid - 1;
            }
            return lo <= 0 ? "\u2026" : s.Substring(0, lo) + "\u2026";
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static string Tint(string text, Color c) => "<color=" + Hex(c) + ">" + text + "</color>";

        /// <summary>The footer slab word of a tone: NEG, WARN, READY, or INT for anything else.</summary>
        public static string SlabWord(AvState tone) =>
            tone == AvState.Danger ? "NEG" : tone == AvState.Caution ? "WARN" : tone == AvState.Ready ? "READY" : "INT";

        public static Color SlabFill(AvState tone) =>
            AvStyleHost.Resolve(AvStyleHost.FuiStyle("slab " + AvStates.Class(tone)).Background, AvTheme.Accent);

        /// <summary>The ink that sits on a slab (the palette's ground colour).</summary>
        public static Color SlabInk => AvStyleHost.Resolve(AvStyleHost.FuiStyle("slab").Color, Color.black);

        public static void Place(TMP_Text t, float x, float y, float w, float h) => AvLay.Place(t.rectTransform, x, y, w, h);

        public static AvState StateOf(C2Tone tone) =>
            tone == C2Tone.Danger ? AvState.Danger : tone == C2Tone.Warn ? AvState.Caution : AvState.Ready;
    }
}
