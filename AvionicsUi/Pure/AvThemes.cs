namespace NOAvionics
{
    /// <summary>The one house palette (Portal). Kept as an enum so config and overrides stay keyed by name.</summary>
    public enum AvThemeId { Portal, Steel, Ace, Phosphor, FieldOps, Amber, Glass, NightOps, Custom }

    /// <summary>
    /// Where the v2 sheets live and how a palette is layered over the base sheet. A palette is a
    /// single :root block of colour roles; the base sheet references roles only, so composing is
    /// "palette text, then base text" and the palette's variables are defined before any rule.
    /// </summary>
    public static class AvThemes
    {
        public const string BaseResource = "NOAvionics.avionics.fui.avss";
        public const string BaseOverrideFile = "NOAvionics/avionics.fui.avss";

        public static string PaletteResource(AvThemeId id) => "NOAvionics.avionics." + Slug(id) + ".avss";
        public static string OverrideFile(AvThemeId id) => "NOAvionics/avionics." + Slug(id) + ".avss";
        public static string Compose(string palette, string baseSheet) => (palette ?? "") + "\n" + (baseSheet ?? "");

        /// <summary>Display names in enum order, as the SET scheme board prints them.</summary>
        public static readonly string[] Names =
        {
            "PORTAL", "STEEL", "ACE", "PHOSPHOR", "FIELD OPS", "AMBER CRT", "GLASS MFD", "NIGHT OPS", "CUSTOM",
        };

        /// <summary>
        /// The pilot's own palette from three knobs: the hue of keys and selection, the hue of the glass, and how
        /// much of that hue the glass carries (0 = neutral grey, 1 = saturated). Caution stays amber and danger
        /// red whatever is chosen, and every text role is lifted until it reads at 4.5:1 on the raised surface.
        /// </summary>
        public static string CustomPalette(float keyHue, float glassHue, float glassTint)
        {
            float t = glassTint < 0f ? 0f : glassTint > 1f ? 1f : glassTint;
            Rgba ground = Hsv(glassHue, 0.70f * t, 0.060f), surface = Hsv(glassHue, 0.62f * t, 0.130f);
            Rgba raised = Hsv(glassHue, 0.56f * t, 0.19f), inert = Hsv(glassHue, 0.66f * t, 0.095f);
            Rgba sunken = Hsv(glassHue, 0.70f * t, 0.070f);
            Rgba key = Lift(Hsv(keyHue, 0.62f, 0.95f), raised), info = Lift(Hsv(keyHue, 0.36f, 1f), raised);
            return "/* Custom · generated from SET > DISPLAY (key hue, glass hue, glass tint). */\n:root {\n" +
                Role("ground", ground, "fb") + Role("surface", surface, "fa") + Role("surface-raised", raised, "fa") +
                Role("surface-inert", inert, "f5") + Role("surface-sunken", sunken, "f8") +
                Role("hairline", Hsv(glassHue, 0.40f * t, 0.42f), "90") + Role("frame", Hsv(glassHue, 0.32f * t, 0.62f), "c0") +
                Role("ink", Lift(Hsv(keyHue, 0.04f, 0.96f), raised), null) +
                Role("ink-dim", Lift(Hsv(glassHue, 0.14f * t, 0.78f), raised), null) +
                Role("ink-muted", Lift(Hsv(glassHue, 0.18f * t, 0.66f), raised), null) +
                Role("key", key, null) + Role("select", key, null) + Role("ready", key, null) +
                // effort: the STR main-effort role other branches add to every palette; harmless until then.
                "  caution: #F5BA56;\n  effort: #F5BA56;\n  danger: #FF695B;\n" + Role("info", info, null) +
                Role("inert", Hsv(glassHue, 0.40f * t, 0.33f), "a6") + "  friendly: friendly;\n  hostile: hostile;\n}\n";
        }

        private static string Role(string name, Rgba c, string alphaHex) =>
            "  " + name + ": #" + Byte(c.R) + Byte(c.G) + Byte(c.B) + (alphaHex != null ? " " + alphaHex : "") + ";\n";

        private static string Byte(float v) =>
            ((int)System.Math.Round((v < 0f ? 0f : v > 1f ? 1f : v) * 255f)).ToString("X2", System.Globalization.CultureInfo.InvariantCulture);

        // Toward white in small steps until the ink clears 4.6:1 on the lightest surface it sits on.
        private static Rgba Lift(Rgba ink, Rgba back)
        {
            for (int i = 0; i < 24 && Rgba.Contrast(ink, back) < 4.6f; i++)
                ink = new Rgba(ink.R + (1f - ink.R) * 0.12f, ink.G + (1f - ink.G) * 0.12f, ink.B + (1f - ink.B) * 0.12f);
            return ink;
        }

        private static Rgba Hsv(float hue, float s, float v)
        {
            float h = (hue % 360f + 360f) % 360f / 60f;
            int sector = (int)h;
            float f = h - sector, p = v * (1f - s), q = v * (1f - s * f), u = v * (1f - s * (1f - f));
            switch (sector)
            {
                case 0: return new Rgba(v, u, p);
                case 1: return new Rgba(q, v, p);
                case 2: return new Rgba(p, v, u);
                case 3: return new Rgba(p, q, v);
                case 4: return new Rgba(u, p, v);
                default: return new Rgba(v, p, q);
            }
        }

        private static string Slug(AvThemeId id)
        {
            switch (id)
            {
                case AvThemeId.Custom: return "custom";
                case AvThemeId.Steel: return "steel";
                case AvThemeId.Ace: return "ace";
                case AvThemeId.Phosphor: return "phosphor";
                case AvThemeId.FieldOps: return "fieldops";
                case AvThemeId.Amber: return "amber";
                case AvThemeId.Glass: return "glass";
                case AvThemeId.NightOps: return "nightops";
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
