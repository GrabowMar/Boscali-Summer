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
            TestTypeScale(assert);
            TestIcons(assert);
            TestFlow(assert);
            TestMesh(assert);
            TestFxPacking(assert);
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

        private static void TestTypeScale(Action<bool, string> assert)
        {
            foreach (AvTextRole r in (AvTextRole[])Enum.GetValues(typeof(AvTextRole)))
                assert(AvTypeScale.Of(r).Size >= AvTypeScale.Floor, r + " respects the 11 px floor");
            assert(AvTypeScale.Of(AvTextRole.Head).Upper && AvTypeScale.Of(AvTextRole.Title).Upper, "heads and titles are caps");
            assert(!AvTypeScale.Of(AvTextRole.Prose).Upper, "prose keeps authored case");
            assert(AvTypeScale.Of(AvTextRole.Data).Face == AvFace.Mono && AvTypeScale.Of(AvTextRole.Display).Face == AvFace.MonoStrong, "numbers are mono");
            assert(AvTypeScale.AssetName(AvFace.Icons) == "NOA Icons SDF", "icon asset name matches the bundle");
        }

        private static void TestIcons(Action<bool, string> assert)
        {
            var seen = new System.Collections.Generic.HashSet<char>();
            string manifest = null;
            try { manifest = Sheet("avionics-ui.manifest.json"); } catch (InvalidOperationException) { }
            for (int i = 1; i <= AvIconTable.Count; i++)
            {
                var icon = (AvIcon)i;
                char c = AvIconTable.Char(icon);
                assert(c >= '', icon + " is an icon-font codepoint (BMP, U+E000 and up)");
                assert(seen.Add(c), icon + " codepoint is unique");
                if (manifest != null)
                    assert(manifest.Contains("\"" + AvIconTable.TablerName(icon) + "\": \"" + ((int)c).ToString("x4") + "\""),
                        icon + " matches the baked manifest");
            }
            assert(AvIconTable.Char(AvIcon.None) == '\0', "None has no glyph");
        }

        private static void TestFlow(Action<bool, string> assert)
        {
            var f = new AvFlowMath(480f);
            assert(Math.Abs(f.Inner - 444f) < 0.01f, "inner = 480 - 2*14 - 8 gutter");
            AvSlot a = f.Take(30f);
            assert(a.X == 14f && a.Y == 14f && a.W == 444f && a.H == 30f, "first slot at pad, full inner width");
            AvSlot[] cols = f.TakeColumns(2, 40f);
            assert(cols.Length == 2 && Math.Abs(cols[0].W - 218f) < 0.01f && Math.Abs(cols[1].X - (14f + 218f + 8f)) < 0.01f, "two columns split with gap");
            assert(cols[0].Y == 14f + 30f + 8f, "columns start after previous slot + gap");
            assert(cols[1].X + cols[1].W <= 480f - 8f - 14f + 0.01f, "rightmost column stops before the gutter");
            assert(Math.Abs(f.ContentHeight - (14f + 30f + 8f + 40f + 14f)) < 0.01f, "content height = pads + slots + gaps");
            f.Reset();
            assert(f.ContentHeight == 0f && f.Y == 14f, "reset empties the flow");
            assert(AvFlowMath.ColumnWidth(444f, 3, 8f) > 0f && AvFlowMath.ColumnWidth(10f, 5, 8f) == 0f, "impossible columns clamp to 0");
        }

        private static void TestMesh(Action<bool, string> assert)
        {
            AvV2[] p = AvMeshMath.ChamferPolygon(0f, 0f, 100f, 50f, AvChamfer.Diagonal(10f));
            assert(p.Length == 8, "chamfer polygon always has 8 points");
            assert(p[2].X == 90f && p[2].Y == 50f && p[3].X == 100f && p[3].Y == 40f, "top-right corner is cut by 10");
            assert(p[0].X == 0f && p[0].Y == 50f && p[1].X == 0f && p[1].Y == 50f, "zero chamfer repeats the corner point");
            AvChamfer inner = AvMeshMath.Inset(AvChamfer.Diagonal(10f), 2f);
            assert(Math.Abs(inner.TR - (10f - 2f * 0.4142f)) < 0.001f && inner.TL == 0f, "inset keeps the stroke width along the cut");
            assert(AvMeshMath.SegmentsLit(10, 0.46f) == 4 && AvMeshMath.SegmentsLit(10, 1.2f) == 10 && AvMeshMath.SegmentsLit(10, -1f) == 0, "segments clamp");
            assert(AvMeshMath.ArcSteps(240f) == 40 && AvMeshMath.ArcSteps(0.5f) == 1, "arc tessellation");
            AvV2 q = AvMeshMath.ArcPoint(0f, 0f, 10f, 90f);
            assert(Math.Abs(q.X) < 0.001f && Math.Abs(q.Y - 10f) < 0.001f, "90 degrees is straight up");
        }

        private static void TestFxPacking(Action<bool, string> assert)
        {
            assert(AvFxPacking.Resolve(AvFxKind.Shine, AvFxTier.Full, false) == AvFxKind.Shine, "full tier plays shine");
            assert(AvFxPacking.Resolve(AvFxKind.Shine, AvFxTier.Full, true) == AvFxKind.None, "reduced motion drops timed shine");
            assert(AvFxPacking.Resolve(AvFxKind.Scan, AvFxTier.Lite, false) == AvFxKind.None, "lite drops timed scan");
            assert(AvFxPacking.Resolve(AvFxKind.Glow, AvFxTier.Lite, true) == AvFxKind.Glow, "static glow survives lite + reduced motion");
            assert(AvFxPacking.Resolve(AvFxKind.Pulse, AvFxTier.Full, true) == AvFxKind.None, "reduced motion stops pulsing");
            assert(AvFxPacking.Resolve(AvFxKind.Glow, AvFxTier.Off, false) == AvFxKind.None, "off means no effects at all");
            assert(Math.Abs(AvFxPacking.DurationOf(AvFxKind.Shine) - 0.4f) < 1e-5f && AvFxPacking.DurationOf(AvFxKind.Glow) == 0f, "durations");
            assert(AvFxPacking.Aspect(100f, 0f) == 1f && Math.Abs(AvFxPacking.Aspect(200f, 50f) - 4f) < 1e-5f, "aspect safe");
            assert(AvFxPacking.U(5f, 0f, 0f) == 0f && Math.Abs(AvFxPacking.V(25f, 0f, 100f) - 0.25f) < 1e-5f, "rect uv safe");
        }
    }
}
