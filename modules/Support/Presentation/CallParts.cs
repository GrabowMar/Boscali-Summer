using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// Colours for the OPS hero parts, always read from the live style sheet roles (never a literal),
    /// so the three themes and the game accent restyle them with the rest of the console.
    /// </summary>
    internal static class OpsInk
    {
        public static Color Role(string role, Color fallback) => AvStyleHost.FuiColor(role, fallback);

        /// <summary>The rail colour a chip/row of this state wears (the state's own hue).</summary>
        public static Color Rail(AvState state) =>
            AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Rail, AvTheme.RailInert);

        /// <summary>The text colour for a word of this state (inert words read as plain ink).</summary>
        public static Color Word(AvState state) => state == AvState.Inert
            ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary)
            : AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Color, AvTheme.TextPrimary);

        public static Color Ink => AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
        public static Color Dim => AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
        public static Color Muted => Role("ink-muted", AvTheme.Disabled);
        public static Color Hairline => Role("hairline", AvTheme.Hairline);
        public static Color Frame => Role("frame", AvTheme.Frame);
        public static Color Key => Role("key", AvTheme.RailInfo);
        public static Color Select => Role("select", AvTheme.Accent);
        public static Color Sunken => AvStyleHost.Resolve(AvStyleHost.FuiStyle("header").Background, AvTheme.Surface);
        public static Color Inert => AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Background, AvTheme.SurfaceInert);

        public static Color A(Color c, float alpha) => c.WithAlpha(alpha);
    }

    /// <summary>Text helpers shared by the hero parts.</summary>
    internal static class OpsText
    {
        /// <summary>A single-line label that shrinks toward the 11 px floor instead of spilling.</summary>
        public static TMP_Text Line(RectTransform parent, string name, AvTextRole role, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(parent, name, role, "", align, false);
            AvText.Fit(t, false);
            return t;
        }

        /// <summary>Set text only when it changed; returns true when it did.</summary>
        public static bool Set(TMP_Text t, string value)
        {
            string v = value ?? "";
            if (t.text == v) return false;
            t.text = v;
            return true;
        }
    }
}
