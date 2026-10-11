using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The page conversions of the visor board (LAYOUT.md 9): what becomes of a plate, a wash, a chip, a box and a word without a plate behind them.</summary>
    internal static class PageInkTests
    {
        public static void Run()
        {
            Fills();
            Polys();
            Boxes();
            Texts();
            Decimals();
        }

        private static void Fills()
        {
            TestAssert.That(PageInk.Fill(10f, 10f, Bg, 1f) == InkFill.Drop && PageInk.Fill(300f, 100f, Plate, 0.5f) == InkFill.Drop, "A dark plate is dropped at any size");
            TestAssert.That(PageInk.Fill(100f, 100f, Ink, 0.3f) == InkFill.Drop, "A big faint wash (a ground tint, a vignette) is dropped");
            TestAssert.That(PageInk.Fill(30f, 30f, Ink, 0.3f) == InkFill.Draw && PageInk.Fill(100f, 20f, Ink, 0.3f) == InkFill.Draw, "...a small or a flat one is not a wash: it stays");
            TestAssert.That(PageInk.Fill(100f, 100f, Dim, 0.6f) == InkFill.Drop && PageInk.Fill(100f, 100f, Faint, 1f) == InkFill.Draw, "DIM counts at its own 0.70: 0.6 of it is 0.42, a wash; faint at full strength is 0.70, a fill");
            TestAssert.That(PageInk.Fill(20f, 14f, Hi, 1f) == InkFill.Chip && PageInk.Fill(64f, 24f, Amber, 0.9f) == InkFill.Chip, "A solid fill of 14 x 12 or more is a chip");
            TestAssert.That(PageInk.Fill(20f, 14f, Hi, 0.85f) == InkFill.Draw && PageInk.Fill(13f, 14f, Hi, 1f) == InkFill.Draw && PageInk.Fill(20f, 11f, Hi, 1f) == InkFill.Draw, "...not 0.85, not under 14 wide, not under 12 high");
            TestAssert.That(PageInk.Fill(20f, 14f, Dim, 1f) == InkFill.Draw, "A DIM fill is 0.70 at best: never a chip");
        }

        private static void Polys()
        {
            TestAssert.That(!PageInk.Poly(Ink, 0.07f) && !PageInk.Poly(Red, 0.24f) && !PageInk.Poly(Ink, 0.45f), "A translucent path fill (an airframe, a zone, a runway) is dropped: its outline stays");
            TestAssert.That(PageInk.Poly(Red, 0.75f) && PageInk.Poly(Hi, 1f) && PageInk.Poly(Dim, 1f), "A strong one (flames, a marker) stays");
            TestAssert.That(!PageInk.Poly(Dim, 0.5f) && !PageInk.Poly(Bg, 1f) && !PageInk.Poly(Plate, 1f), "DIM at half is 0.35; a dark tone is no ink");
        }

        private static void Boxes()
        {
            TestAssert.That(PageInk.Box(10f, 20f, Ink, Ink, 1f, false, out _, out _) == InkBox.Draw && PageInk.Box(30f, 8f, Ink, Ink, 1f, false, out _, out _) == InkBox.Draw, "A box under 14 x 12 is drawn as it is");

            InkBox b = PageInk.Box(64f, 24f, Amber, Amber, 0.16f, false, out float arm, out bool chip);
            TestAssert.That(b == InkBox.Brackets && arm == 6f && chip, "A box with a tone fill is a chip: brackets of arm 6 (a check cell, a perk lamp)");
            b = PageInk.Box(80f, 30f, Ink, Bg, 0.5f, false, out arm, out chip);
            TestAssert.That(b == InkBox.Brackets && arm == 7f && !chip, "A translucent dark card of 60 x 20 or more is brackets of arm 7, not a chip");
            b = PageInk.Box(150f, 70f, Ink, Bg, 0f, false, out arm, out chip);
            TestAssert.That(b == InkBox.Brackets && arm == 10f && !chip, "A big frame of 120 x 60 or more is brackets of arm 10");
            b = PageInk.Box(150f, 70f, Dim, Bg, 0f, false, out arm, out chip);
            TestAssert.That(b == InkBox.Nothing, "...and a quiet (DIM) one is nothing at all");
            b = PageInk.Box(150f, 70f, Ink, Ink, 0.3f, false, out arm, out chip);
            TestAssert.That(b == InkBox.Brackets && arm == 10f && chip, "A big box that is a chip is still arm 10, and still a chip");
            b = PageInk.Box(60f, 20f, Ink, Bg, 0f, false, out arm, out chip);
            TestAssert.That(b == InkBox.Brackets && arm == 4f && !chip, "A plain cell (40 to 120 by 16 to 32) is brackets of arm 4");
            TestAssert.That(PageInk.Box(60f, 20f, Faint, Bg, 0f, false, out _, out _) == InkBox.Nothing, "...a quiet one nothing");
            TestAssert.That(PageInk.Box(60f, 20f, Ink, Bg, 0f, true, out _, out _) == InkBox.Draw, "...a chamfered one is drawn as it is");
            TestAssert.That(PageInk.Box(30f, 20f, Ink, Bg, 0f, false, out _, out _) == InkBox.Draw && PageInk.Box(60f, 40f, Ink, Bg, 0f, false, out _, out _) == InkBox.Draw, "A box that is no cell, card or frame (30 wide, or 40 high without a fill) is drawn as it is");
        }

        private static void Texts()
        {
            ScopeTone tone = Dim;
            bool bold = false;
            float alpha = 0.8f;
            PageInk.Text(ref tone, ref bold, ref alpha, 12f, true, false, Ink);
            TestAssert.That(tone == Ink && !bold && alpha == 1f, "DIM text with a digit is data: INK at full alpha (DCL 1, 5S, 9.5KM)");
            tone = Dim; bold = true; alpha = 0.8f;
            PageInk.Text(ref tone, ref bold, ref alpha, 12f, false, false, Ink);
            TestAssert.That(tone == Ink && bold && alpha == 1f, "...so is bold DIM text");
            tone = Dim; bold = false; alpha = 0.8f;
            PageInk.Text(ref tone, ref bold, ref alpha, 14f, false, false, Ink);
            TestAssert.That(tone == Ink && alpha == 1f, "...and DIM text larger than 12");
            tone = Dim; bold = false; alpha = 0.8f;
            PageInk.Text(ref tone, ref bold, ref alpha, 12f, false, false, Ink);
            TestAssert.That(tone == Dim && !bold && alpha == 0.8f, "A plain DIM word of 12 (a caption, a label) stays DIM");
            tone = Faint; bold = false; alpha = 1f;
            PageInk.Text(ref tone, ref bold, ref alpha, 12f, true, false, Ink);
            TestAssert.That(tone == Ink, "Faint is DIM: the same rule");
            tone = Amber; bold = false; alpha = 1f;
            PageInk.Text(ref tone, ref bold, ref alpha, 12f, false, false, Ink);
            TestAssert.That(tone == Amber && bold, "Amber text is bold");
            tone = Red; bold = false; alpha = 1f;
            PageInk.Text(ref tone, ref bold, ref alpha, 12f, false, false, Ink);
            TestAssert.That(tone == Red && bold, "Red text is bold");
            tone = Ink; bold = false; alpha = 1f;
            PageInk.Text(ref tone, ref bold, ref alpha, 12f, true, false, Ink);
            TestAssert.That(tone == Ink && !bold, "INK data keeps its weight");
            tone = Bg; bold = false; alpha = 1f;
            PageInk.Text(ref tone, ref bold, ref alpha, 12f, false, true, Amber);
            TestAssert.That(tone == Amber && bold, "Dark text on a chip takes the chip's tone, bold (and amber stays bold)");
            tone = Bg; bold = false; alpha = 1f;
            PageInk.Text(ref tone, ref bold, ref alpha, 12f, false, false, Amber);
            TestAssert.That(tone == Hi && bold, "...with no chip under it, BRIGHT bold");
            TestAssert.That(PageInk.HasDigit("9.5KM") && !PageInk.HasDigit("TANKS OK") && !PageInk.HasDigit(""), "A word carries a digit or it does not");
        }

        private static void Decimals()
        {
            TestAssert.That(PageInk.Decimals("0.2") == 1 && PageInk.Decimals("9.5KM") == 1 && PageInk.Decimals("ALT 0.0KM") == 1 && PageInk.Decimals("-6.8") == 1, "A point between two digits is a decimal");
            TestAssert.That(PageInk.Decimals("1.2.3") == 2, "Every one counts");
            TestAssert.That(PageInk.Decimals(".5") == 0 && PageInk.Decimals("5.") == 0 && PageInk.Decimals("A.5") == 0 && PageInk.Decimals("5.A") == 0 && PageInk.Decimals("12 . 3") == 0 && PageInk.Decimals("") == 0, "A point that is not between two digits is a full-width character");
            TestAssert.That(PageInk.IsDecimal("3.4", 1) && !PageInk.IsDecimal("3.4", 0) && !PageInk.IsDecimal("3.4", 2), "IsDecimal is about the point itself");
            TestAssert.Near(PageInk.DotAdvance, 0.45f, "The decimal point advances 0.45 em");
            TestAssert.Near(PageInk.DotShift, 0.07f, "...and is drawn 0.07 em right of the centre of its cell, off the round bottom of a 0");
        }
    }
}
