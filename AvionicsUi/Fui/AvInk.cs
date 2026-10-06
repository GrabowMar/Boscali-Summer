using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// The kit sheet's ink roles as colours: text, dim and muted ink, hairlines, frames, surfaces and the state hues.
    /// Every accessor reads the live sheet, so a theme change repaints through the next <c>Restyle</c>.
    /// </summary>
    public static class AvInk
    {
        public static Color Ink => AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
        public static Color Dim => AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
        public static Color Muted => AvStyleHost.FuiColor("ink-muted", AvTheme.Disabled);
        public static Color Hairline => AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
        public static Color Frame => AvStyleHost.FuiColor("frame", AvTheme.Frame);
        public static Color Select => AvStyleHost.FuiColor("select", AvTheme.Accent);
        public static Color Friendly => AvStyleHost.FuiColor("friendly", AvTheme.Friendly);
        public static Color Hostile => AvStyleHost.FuiColor("hostile", AvTheme.Hostile);
        public static Color Inert => AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
        public static Color Raised => AvStyleHost.FuiColor("surface-raised", AvTheme.SurfaceRaised);
        public static Color Surface => AvStyleHost.FuiColor("surface", AvTheme.Surface);
        public static Color Ground => AvStyleHost.FuiColor("ground", AvTheme.Ground);
        public static Color Key => AvStyleHost.FuiColor("key", AvTheme.RailInfo);

        /// <summary>The hue of a state (rails, bars, glyphs); Inert is the opaque inert grey.</summary>
        public static Color State(AvState s)
        {
            switch (s)
            {
                case AvState.Ready: return AvStyleHost.FuiColor("ready", AvTheme.RailReady);
                case AvState.Caution: return AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
                case AvState.Danger: return AvStyleHost.FuiColor("danger", AvTheme.RailDanger);
                case AvState.Info: return AvStyleHost.FuiColor("info", AvTheme.RailInfo);
                default: return AvStyleHost.FuiColor("inert", AvTheme.RailInert).WithAlpha(1f);
            }
        }

        /// <summary>Text tone for a state word: caution/danger/ready keep their colour, the rest read dim.</summary>
        public static Color StateText(AvState s) =>
            s == AvState.Inert ? Muted : s == AvState.Info ? Ink : State(s);
    }
}
