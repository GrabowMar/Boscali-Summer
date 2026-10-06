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
            ? AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary)
            : AvStyleHost.FuiInk("chip " + AvStates.Class(state), AvTheme.TextPrimary);

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
    }
}
