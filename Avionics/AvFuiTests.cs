using System;
using System.IO;
using System.Reflection;

namespace NOAvionics.Tests
{
    /// <summary>Engine-free tests for kit v2 (themes, numbers, type, icons, flow, mesh, fx packing).</summary>
    public static class AvFuiTests
    {
        public static void Run(Action<bool, string> assert)
        {
            if (assert == null) throw new ArgumentNullException(nameof(assert));
            TestPalettesDefineEveryRole(assert);
            TestPaletteContrast(assert);
            TestComposeLetsPaletteWin(assert);
        }

        // Live-theme references resolve to AvTheme's fallbacks in an engine-free test.
        private static Rgba Live(AvPaint p)
        {
            switch (p.Kind)
            {
                case AvColorRef.Fixed: return p.Value;
                case AvColorRef.Accent: return new Rgba(0.30f, 1.00f, 0.35f, p.Alpha);
                case AvColorRef.Warning: return new Rgba(1.00f, 0.55f, 0.20f, p.Alpha);
                case AvColorRef.Alert: return new Rgba(1.00f, 0.18f, 0.12f, p.Alpha);
                case AvColorRef.Friendly: return new Rgba(0.45f, 0.95f, 0.55f, p.Alpha);
                case AvColorRef.Hostile: return new Rgba(1.00f, 0.08f, 0.04f, p.Alpha);
                default: return new Rgba(1f, 0f, 1f, 1f);
            }
        }

        internal static string Sheet(string name)
        {
            Assembly asm = typeof(AvFuiTests).Assembly;
            using (Stream s = asm.GetManifestResourceStream("BoscaliSummer.Tests." + name))
            {
                if (s == null) throw new InvalidOperationException("missing embedded sheet " + name);
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        private static AvStyleSheet Composed(AvThemeId id) =>
            AvStyleSheet.Parse(AvThemes.Compose(Sheet(ThemeFile(id)), Sheet("avionics.fui.avss")));

        private static string ThemeFile(AvThemeId id) => "avionics." + id.ToString().ToLowerInvariant() + ".avss";

        private static void TestPalettesDefineEveryRole(Action<bool, string> assert)
        {
            foreach (AvThemeId id in (AvThemeId[])Enum.GetValues(typeof(AvThemeId)))
            {
                AvStyleSheet s = Composed(id);
                assert(!s.HasErrors, id + " + base parse without errors: " + string.Join("; ", s.Errors));
                foreach (string role in AvThemes.Roles)
                {
                    AvPaint p = s.Paint(role, new Rgba(1f, 0.0123f, 1f));
                    bool sentinel = p.Kind == AvColorRef.Fixed && Math.Abs(p.Value.G - 0.0123f) < 1e-5f;
                    assert(!sentinel, id + " defines role " + role);
                }
            }
        }

        private static void TestPaletteContrast(Action<bool, string> assert)
        {
            string[] inks = { "ink", "ink-dim", "ink-muted", "key", "ready", "caution", "danger", "info", "select" };
            string[] grounds = { "ground", "surface", "surface-raised", "surface-inert", "surface-sunken" };
            foreach (AvThemeId id in (AvThemeId[])Enum.GetValues(typeof(AvThemeId)))
            {
                AvStyleSheet s = Composed(id);
                Rgba ground = Live(s.Paint("ground", default(Rgba))).WithAlpha(1f);
                foreach (string bg in grounds)
                {
                    Rgba back = Live(s.Paint(bg, default(Rgba))).Over(ground);
                    foreach (string ink in inks)
                    {
                        Rgba fore = Live(s.Paint(ink, default(Rgba))).Over(back);
                        float c = Rgba.Contrast(fore, back);
                        assert(c >= 4.5f, id + ": " + ink + " on " + bg + " contrast " + c.ToString("0.00") + " < 4.5");
                    }
                }
            }
        }

        private static void TestComposeLetsPaletteWin(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(AvThemes.Compose(":root { ink: #FF0000; }", ".t { color: ink; }"));
            assert(Math.Abs(s.Resolve("t").Color.Value.R - 1f) < 0.001f, "palette role feeds base rule");
        }
    }
}
