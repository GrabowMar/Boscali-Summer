using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The visor board's page ink (LAYOUT.md 9). The page painters were written for a dark plate and still say plates, washes and chips; there is no plate now, so a surface with Open on draws what they say as open ink,
    // in one place for every page: a fill whose colour is not an ink tone is not drawn, a ground tint or a vignette is not drawn, a solid chip becomes four corner brackets in its tone, a filled or framed box becomes
    // brackets (or nothing when it is a quiet frame), a translucent path fill is not drawn, and the text obeys the page text rule: the decisions are PageInk's. The decimal point rule (LAYOUT.md 0: ScopeText) holds for every surface.
    internal sealed partial class ScopeCanvas
    {
        /// <summary>The page conversions of LAYOUT.md 9 apply to what is drawn on this surface (the board sets it on the page and its strip; the wing's own lines and the stores header say what they mean).</summary>
        public bool Open { get; set; }

        // The last solid chip drawn: dark text that follows inside it takes its tone.
        private bool chipSet;
        private float chipX, chipY, chipW, chipH;
        private ScopeTone chipTone;

        private void Chip(float x, float y, float w, float h, ScopeTone tone)
        {
            chipSet = true;
            chipX = x; chipY = y; chipW = w; chipH = h;
            chipTone = tone;
        }

        /// <summary>Whether a filled rectangle is drawn as it is (<see cref="PageInk.Fill"/>); a chip is replaced by four corner brackets here.</summary>
        private bool KeepFill(float x, float y, float w, float h, ScopeTone tone, float alpha)
        {
            if (!Open) return true;
            InkFill fill = PageInk.Fill(w, h, tone, alpha);
            if (fill == InkFill.Chip)
            {
                Chip(x, y, w, h, tone);
                Brackets(x, y, w, h, 4f, tone);
            }
            return fill == InkFill.Draw;
        }

        private bool KeepPoly(ScopeTone tone, float alpha) => !Open || PageInk.Poly(tone, alpha);

        /// <summary>A box as the page conversions have it (<see cref="PageInk.Box"/>): brackets or nothing instead of the box; true when it was handled here.</summary>
        private bool OpenBox(float x, float y, float w, float h, ScopeTone tone, float alpha, float lw, ScopeTone fill, float fillAlpha, bool chamfered)
        {
            if (!Open) return false;
            InkBox box = PageInk.Box(w, h, tone, fill, fillAlpha, chamfered, out float arm, out bool chip);
            if (box == InkBox.Draw) return false;
            if (chip) Chip(x, y, w, h, fill);
            if (box == InkBox.Brackets) Brackets(x, y, w, h, arm, tone, alpha, Math.Max(lw, 1f));
            return true;
        }

        /// <summary>The page text rule (<see cref="PageInk.Text"/>) with the chip the text sits on, if any.</summary>
        private void PageText(ReadOnlySpan<char> s, float x, float y, float size, ref ScopeTone tone, ref bool bold, ref float alpha)
        {
            bool onChip = chipSet && x >= chipX - 6f && x <= chipX + chipW + 6f && y >= chipY - 6f && y <= chipY + chipH + 6f;
            PageInk.Text(ref tone, ref bold, ref alpha, size, PageInk.HasDigit(s), onChip, chipTone);
        }
    }
}
