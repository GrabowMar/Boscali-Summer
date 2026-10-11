using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the page conversions do with a filled rectangle.</summary>
    internal enum InkFill : byte { Draw, Drop, Chip }

    /// <summary>What the page conversions do with a box: draw it as it is, draw nothing, or four corner brackets.</summary>
    internal enum InkBox : byte { Draw, Nothing, Brackets }

    /// <summary>
    /// The page conversions of the visor board (LAYOUT.md 9) as decisions, pure: the painters were written for a dark plate and say plates, washes, chips and boxes; there is none, so the canvas (ScopeCanvas.Open) asks
    /// these what to draw instead. A fill that is not an ink tone is dropped, so is a ground tint or a vignette and any translucent path fill; a solid chip is four corner brackets; a filled or framed box is brackets
    /// (or nothing, when it is a quiet frame); the text keeps data out of DIM. A '.' between two digits is a narrow cell (<see cref="DotAdvance"/>).
    /// </summary>
    internal static class PageInk
    {
        /// <summary>
        /// A '.' between two digits advances <see cref="DotAdvance"/> em, not the 0.65 of every other character, and is drawn <see cref="DotShift"/> em right of the centre of that cell (LAYOUT.md 0: "0.2", never "0. 2",
        /// and not "Q.2": at 0.35 em the dot sat against the round bottom of the 0).
        /// </summary>
        public const float DotAdvance = 0.45f, DotShift = 0.07f;

        private static bool Dark(ScopeTone t) => t == ScopeTone.Bg || t == ScopeTone.Plate;

        /// <summary>The tone's own opacity times the alpha asked for (DIM is the ink at 0.70).</summary>
        private static float Strength(ScopeTone t, float alpha) => alpha * (t == ScopeTone.Dim || t == ScopeTone.Faint ? ScopePalette.DimAlpha : 1f);

        /// <summary>Rules 1 and 2 for a <paramref name="w"/> x <paramref name="h"/> rectangle: a dark plate is dropped, so is a ground tint or a vignette (at most 0.45 strong and 40 x 40 or more); a solid one (over 0.85, 14 x 12 or more) is a chip.</summary>
        public static InkFill Fill(float w, float h, ScopeTone tone, float alpha)
        {
            if (Dark(tone)) return InkFill.Drop;
            float a = Strength(tone, alpha);
            if (a <= 0.45f && w >= 40f && h >= 40f) return InkFill.Drop;
            return a > 0.85f && w >= 14f && h >= 12f ? InkFill.Chip : InkFill.Draw;
        }

        /// <summary>Rule 1 for a polygon fill (a runway, a wedge, a ring): only an ink tone more than 0.45 strong is drawn, whatever its size; the outline stays.</summary>
        public static bool Poly(ScopeTone tone, float alpha) => !Dark(tone) && Strength(tone, alpha) > 0.45f;

        /// <summary>
        /// Rule 3 for a box of <paramref name="w"/> x <paramref name="h"/> (under 14 x 12 it is drawn as it is): filled in an ink tone (a chip) it is brackets of arm 6, a translucent card (60 x 20 or more) arm 7, a big
        /// frame (120 x 60 or more) arm 10, a plain cell (40 to 120 by 16 to 32, not chamfered) arm 4; a DIM frame or cell that is not a chip draws nothing. <paramref name="chip"/>: it was filled with an ink tone.
        /// </summary>
        public static InkBox Box(float w, float h, ScopeTone tone, ScopeTone fill, float fillAlpha, bool chamfered, out float arm, out bool chip)
        {
            arm = 0f;
            chip = false;
            if (w < 14f || h < 12f) return InkBox.Draw;
            bool filled = fillAlpha > 0f, solid = filled && !Dark(fill), card = filled && !solid && w >= 60f && h >= 20f, big = w >= 120f && h >= 60f;
            bool cell = !filled && w >= 40f && w <= 120f && h >= 16f && h <= 32f && !chamfered;
            if (!(solid || card || big || cell)) return InkBox.Draw;
            chip = solid;
            if (!solid && (tone == ScopeTone.Dim || tone == ScopeTone.Faint)) return InkBox.Nothing;
            arm = big ? 10f : card ? 7f : cell ? 4f : 6f;
            return InkBox.Brackets;
        }

        /// <summary>
        /// Rule 5, the page text rule: dark text takes the tone of the chip it sits on (or BRIGHT) and is bold; data is never DIM (DIM text with a digit, in bold or larger than 12 is INK at full alpha); amber and red text
        /// is bold. <paramref name="onChip"/>: a solid chip of <paramref name="chipTone"/> is under the text.
        /// </summary>
        public static void Text(ref ScopeTone tone, ref bool bold, ref float alpha, float size, bool digits, bool onChip, ScopeTone chipTone)
        {
            if (Dark(tone))
            {
                tone = onChip ? chipTone : ScopeTone.Hi;
                bold = true;
            }
            if ((tone == ScopeTone.Dim || tone == ScopeTone.Faint) && (bold || size > 12f || digits)) { tone = ScopeTone.Ink; alpha = 1f; }
            if (tone == ScopeTone.Amber || tone == ScopeTone.Red) bold = true;
        }

        /// <summary>Whether <paramref name="s"/> has a digit.</summary>
        public static bool HasDigit(ReadOnlySpan<char> s)
        {
            foreach (char c in s) if ((uint)(c - '0') <= 9u) return true;
            return false;
        }

        /// <summary>Whether the '.' at <paramref name="i"/> sits between two digits (it is <see cref="DotAdvance"/> wide).</summary>
        public static bool IsDecimal(ReadOnlySpan<char> s, int i) => s[i] == '.' && i > 0 && i + 1 < s.Length && (uint)(s[i - 1] - '0') <= 9u && (uint)(s[i + 1] - '0') <= 9u;

        /// <summary>How many of the '.' in <paramref name="s"/> sit between two digits.</summary>
        public static int Decimals(ReadOnlySpan<char> s)
        {
            int n = 0;
            for (int i = 1; i + 1 < s.Length; i++) if (IsDecimal(s, i)) n++;
            return n;
        }
    }
}
