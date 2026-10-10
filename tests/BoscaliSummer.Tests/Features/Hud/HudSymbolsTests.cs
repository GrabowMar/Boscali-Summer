using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The status board's symbol table stays drawable and its cell text split stays predictable.</summary>
    internal static class HudSymbolsTests
    {
        public static void Run()
        {
            foreach (string channel in HudGlyphs.Channels) Drawable(channel);
            Drawable(null);
            Drawable("nobody-declared-this");

            Split("FUEL 100%", "FUEL", "100%");
            Split("CALL ARMED · PRSM", "CALL ARMED", "");
            Split("MISSILE WARNING / RIGHT QUARTER", "MISSILE WARNING", "");
            Split("RTB highwaystrip2 · 23km", "RTB HIGHWAYSTRIP2", "23KM");
            Split("ENTERING AREA  ·  SURVEY", "ENTERING AREA", "");
            Split("CONTACT 9.4 KM", "CONTACT", "9.4 KM");
            Split("SINK -12 M/S", "SINK M/S", "-12");
            Split("", "", "");
        }

        private static void Drawable(string channel)
        {
            float[][] glyph = HudGlyphs.For(channel);
            TestAssert.That(glyph != null && glyph.Length > 0, "Every feed draws a symbol: " + channel);
            foreach (float[] line in glyph)
            {
                TestAssert.That(line.Length >= 4 && line.Length % 2 == 0, "A polyline is at least one x,y segment: " + channel);
                foreach (float v in line) TestAssert.That(v >= 0f && v <= 1f, "Symbol stays inside its unit box: " + channel);
            }
        }

        private static void Split(string text, string label, string value)
        {
            HudCellText.Split(text, out string l, out string v);
            TestAssert.That(l == label && v == value, "Split '" + text + "' -> '" + l + "' + '" + v + "'");
        }
    }
}
