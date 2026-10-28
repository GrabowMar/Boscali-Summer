using NuclearOption.UIStyleSystem;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// Live game theme accessor and color/string conversion helpers shared across mods.
    /// Bridges safely to ThemeManager.Active with fixed fallbacks.
    /// </summary>
    public static class AvTheme
    {
        // The live game theme, or a fixed colour when the theme is not up yet.
        private static Color Live(System.Func<ColorTheme, Color> read, float r, float g, float b)
        {
            try { return read(ThemeManager.Active.ColorTheme); }
            catch { return new Color(r, g, b, 1f); }
        }

        public static Color Accent => Live(t => t.AllClear, 0.30f, 1.00f, 0.35f);
        public static Color Friendly => Live(t => t.MapIconFriendly, 0.45f, 0.95f, 0.55f);
        public static Color Warning => Live(t => t.Warning, 1.00f, 0.55f, 0.20f);
        public static Color Alert => Live(t => t.Alert, 1.00f, 0.18f, 0.12f);
        public static Color Hostile => Live(t => t.HudUnitHostile, 1.00f, 0.08f, 0.04f);
        public static Color Neutral => Live(t => t.HudUnitNeutral, 0.50f, 0.50f, 0.50f);
        public static Color Selected => Live(t => t.HudUnitSelected, 0.30f, 1.00f, 0.35f);

        public static Color TextPrimary => Unity(AvTokens.TextPrimary);
        public static Color Dim => Unity(AvTokens.TextDim);
        public static Color Disabled => Unity(AvTokens.TextMuted);
        public static Color TextInk => Unity(AvTokens.TextInk);
        public static Color Frame => Unity(AvTokens.Frame);
        public static Color Hairline => Unity(AvTokens.Hairline);
        public static Color Surface => Unity(AvTokens.Surface);
        public static Color SurfaceRaised => Unity(AvTokens.SurfaceRaised);
        public static Color SurfaceInert => Unity(AvTokens.SurfaceInert);
        public static Color Ground => Unity(AvTokens.Ground);

        public static Color RailReady => Accent;
        public static Color RailCaution => Warning;
        public static Color RailDanger => Alert;
        public static Color RailInfo => Unity(AvTokens.RailInfo);
        public static Color RailInert => Unity(AvTokens.RailInert);

        public static Color Unity(Rgba c) => new Color(c.R, c.G, c.B, c.A);

        public static Rgba ToRgba(this Color c) => new Rgba(c.r, c.g, c.b, c.a);

        public static Color WithAlpha(this Color color, float alpha) =>
            new Color(color.r, color.g, color.b, alpha);

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
