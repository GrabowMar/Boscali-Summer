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
            TestNumbers(assert);
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

        private static void TestNumbers(Action<bool, string> assert)
        {
            var saved = System.Threading.Thread.CurrentThread.CurrentCulture;
            foreach (string culture in new[] { "pl-PL", "de-DE", "en-US" })
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(culture);
                try
                {
                    void Eq(string got, string want) => assert(got == want, culture + ": expected '" + want + "' got '" + got + "'");
                    Eq(AvNum.Fixed(0.5, 1), "0.5");
                    Eq(AvNum.Fixed(-12.249, 2), "-12.25");
                    Eq(AvNum.Thousands(8369), "8,369");
                    Eq(AvNum.Thousands(-1204500.4), "-1,204,500");
                    Eq(AvNum.Compact(950), "950");
                    Eq(AvNum.Compact(4802), "4,802");
                    Eq(AvNum.Compact(12345), "12.3K");
                    Eq(AvNum.Compact(6.08e9), "6.08B");
                    Eq(AvNum.Compact(2.5e6), "2.50M");
                    Eq(AvNum.Money(6.08e9), "$6.08B");
                    Eq(AvNum.Money(-120000), "-$120K");
                    Eq(AvNum.Percent(0.52), "52%");
                    Eq(AvNum.Percent(1.224, 1), "122.4%");
                    Eq(AvNum.Signed(3.07, 2), "+3.07");
                    Eq(AvNum.Signed(0, 1), "0.0");
                    Eq(AvNum.Clock(59), "0:59");
                    Eq(AvNum.Clock(208), "3:28");
                    Eq(AvNum.Clock(3605), "1:00:05");
                    Eq(AvNum.Clock(double.NaN), "--:--");
                    Eq(AvNum.Seconds(0.5), "0.5 s");
                }
                finally { System.Threading.Thread.CurrentThread.CurrentCulture = saved; }
            }
        }
    }
}
