namespace NOAvionics
{
    /// <summary>The one house palette (Portal). Kept as an enum so config and overrides stay keyed by name.</summary>
    public enum AvThemeId { Portal, Steel, Ace, Phosphor }

    /// <summary>
    /// Where the v2 sheets live and how a palette is layered over the base sheet. A palette is a
    /// single :root block of colour roles; the base sheet references roles only, so composing is
    /// "palette text, then base text" and the palette's variables are defined before any rule.
    /// </summary>
    public static class AvThemes
    {
        public const string BaseResource = "NOAvionics.avionics.fui.avss";
        public const string BaseOverrideFile = "NOAvionics/avionics.fui.avss";

        /// <summary>Roles every palette must define; the contrast test walks the text ones.</summary>
        public static readonly string[] Roles =
        {
            "ground", "surface", "surface-raised", "surface-inert", "surface-sunken",
            "hairline", "frame", "ink", "ink-dim", "ink-muted", "key", "select",
            "ready", "caution", "danger", "info", "inert", "friendly", "hostile",
        };

        public static string PaletteResource(AvThemeId id) => "NOAvionics.avionics." + Slug(id) + ".avss";
        public static string OverrideFile(AvThemeId id) => "NOAvionics/avionics." + Slug(id) + ".avss";
        public static string Compose(string palette, string baseSheet) => (palette ?? "") + "\n" + (baseSheet ?? "");

        private static string Slug(AvThemeId id)
        {
            switch (id)
            {
                case AvThemeId.Steel: return "steel";
                case AvThemeId.Ace: return "ace";
                case AvThemeId.Phosphor: return "phosphor";
                default: return "portal";
            }
        }
    }

    /// <summary>Operational state of a chip, row, metric or alert. Colour always travels with a word or glyph (R1).</summary>
    public enum AvState { Inert, Ready, Caution, Danger, Info }

    public static class AvStates
    {
        public static string Class(AvState s)
        {
            switch (s)
            {
                case AvState.Ready: return "ready";
                case AvState.Caution: return "caution";
                case AvState.Danger: return "danger";
                case AvState.Info: return "info";
                default: return "inert";
            }
        }

        public static string Glyph(AvState s) => s == AvState.Caution ? "\u25B2 " : s == AvState.Danger ? "\u2715 " : "";
    }
}
