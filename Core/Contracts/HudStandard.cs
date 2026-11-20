using NOAvionics;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The five type steps of the in-flight HUD (2026-11-16 standard): nothing else is ever set.</summary>
    internal enum HudStep : byte { Label, Body, Value, Emphasis, Hero }

    /// <summary>
    /// THE TYPE SCALE of the visor, in reference pixels on a 1080-high screen: LABEL 12 (captions, units, secondary labels), BODY 14 (rows, log lines, strip values), VALUE 16 (the number or word the pilot acts on, spoken
    /// radio text), EMPHASIS 22 (state words), HERO 44 (one numeral per block). Regular for LABEL and BODY, bold for VALUE and up (or a warning word). One tracking per step, rows on a 4 px grid (20 BODY, 16 LABEL).
    /// Pure: compiled into the tests.
    /// </summary>
    internal static class HudType
    {
        public const float Label = 12f, Body = 14f, Value = 16f, Emphasis = 22f, Hero = 44f;

        /// <summary>Row pitches on the 4 px grid: a BODY row, a LABEL row, a gap between blocks (<c>HudZones.Gutter</c>) and between a shelf and its content.</summary>
        public const float BodyRow = 20f, LabelRow = 16f, ShelfGap = 4f;

        /// <summary>
        /// The board is drawn in design units at the pilot's Scale (0.85 by default, which the zones are cut for), so a step asked for in the board is drawn at <c>step * BoardBoost</c> design units: it lands on the step's reference
        /// pixels at the default Scale.
        /// </summary>
        public const float BoardBoost = 1f / 0.85f;

        public static float Of(HudStep step)
        {
            switch (step)
            {
                case HudStep.Body: return Body;
                case HudStep.Value: return Value;
                case HudStep.Emphasis: return Emphasis;
                case HudStep.Hero: return Hero;
                default: return Label;
            }
        }

        /// <summary>The step an arbitrary size belongs to (the nearest: up to 12.5 LABEL, 15 BODY, 18 VALUE, 30 EMPHASIS, above that HERO). Nothing is smaller than LABEL.</summary>
        public static HudStep StepOf(float size)
        {
            if (size <= 12.5f) return HudStep.Label;
            if (size <= 15f) return HudStep.Body;
            if (size <= 18f) return HudStep.Value;
            return size <= 30f ? HudStep.Emphasis : HudStep.Hero;
        }

        /// <summary>The reference pixels of the step <paramref name="size"/> belongs to.</summary>
        public static float Snap(float size) => Of(StepOf(size));

        /// <summary>The weight rule: bold from VALUE up. A warning word is bold whatever its step (<paramref name="warning"/>).</summary>
        public static bool Bold(HudStep step, bool warning = false) => warning || step >= HudStep.Value;

        /// <summary>TMP characterSpacing (1/100 em) of a step: caps at LABEL and BODY track 2, VALUE 1, EMPHASIS and HERO 0. Mixed case (spoken text) tracks 0.</summary>
        public static float Tracking(HudStep step) => step <= HudStep.Body ? 2f : step == HudStep.Value ? 1f : 0f;
    }

    /// <summary>
    /// THE COLOUR ROLES of the visor. INK is the game's own HUD colour (<c>ColorTheme.AllClear</c>, the colour the native heading tape, ladder and gauges are drawn in); INK-HI is the ink lifted <see cref="HiLift"/> toward
    /// white; INK-DIM the ink <see cref="DimDarken"/> toward the edge colour, opaque; CAUTION/WARNING are the theme's amber/red through <see cref="Caution"/>/<see cref="Warning"/>. INK, CAUTION and WARNING are
    /// lifted toward white until their relative luminance reaches a floor (<see cref="FloorY"/>), so a dark custom theme still clears the rim. INFO (allied, radio, navigation) is INK-HI: the game's palette has no cool tone in the HUD theme and the mod
    /// ships none. TEXT EDGE is the one dark rim of every glyph. Line art takes the three alpha steps of <see cref="Lines"/>.
    /// </summary>
    internal static class HudInk
    {
        /// <summary>How far toward white INK-HI is (LAYOUT.md 1), and how far INK-DIM is darkened toward the edge colour: a tone, not an alpha (an alpha also thins the rim). INK/DIM is about 1.6:1 in luminance.</summary>
        public const float HiLift = 0.55f, DimDarken = 0.22f;

        /// <summary>The relative-luminance floors of INK, CAUTION and WARNING (<see cref="FloorY"/>). The default theme sits above all three (INK 0.764, AMBER 0.548, RED 0.234).</summary>
        public const float InkFloorY = 0.45f, CautionFloorY = 0.45f, WarningFloorY = 0.22f;

        /// <summary>The line alphas: full, mid (rules, ticks, frames) and low (leaders, grids). Nothing else.</summary>
        public const float LineFull = 1f, LineMid = 0.7f, LineLow = 0.4f;

        /// <summary>The text edge: a dark green rim (2, 16, 10) at 0.92, the same on every glyph.</summary>
        public static readonly Rgba TextEdge = new Rgba(2f / 255f, 16f / 255f, 10f / 255f, 0.92f);

        /// <summary>
        /// The game draws its own HUD (tape, ladder, gauges) in the theme's all-clear colour on an ADDITIVE material, so on sky the light of the sky is added to it: the default (0, 1, 0) reads as a pale mint (measured 96, 248, 152
        /// on the heading tape in look5/steady.png). The mod's words and line art are alpha blended (additive vanished on bright cloud), so INK is the theme colour plus that wash, clamped: the same instrument on the same sky.
        /// </summary>
        public static readonly Rgba SkyWash = new Rgba(0.38f, 0f, 0.60f);

        /// <summary>INK: the game's HUD colour as it looks on sky (<see cref="SkyWash"/> added), opaque, floored at <see cref="InkFloorY"/>.</summary>
        public static Rgba Ink(Rgba game) => FloorY(new Rgba(Clamp(game.R + SkyWash.R), Clamp(game.G + SkyWash.G), Clamp(game.B + SkyWash.B)), InkFloorY);

        private static float Clamp(float v) => v > 1f ? 1f : v;

        public static Rgba Hi(Rgba ink) => Mix(ink.WithAlpha(1f), new Rgba(1f, 1f, 1f), HiLift);

        /// <summary>INK-DIM: the ink <see cref="DimDarken"/> toward the edge colour, opaque (a tone, so its rim stays as dark as INK's).</summary>
        public static Rgba Dim(Rgba ink) => Mix(ink.WithAlpha(1f), new Rgba(TextEdge.R, TextEdge.G, TextEdge.B), DimDarken);

        /// <summary>CAUTION: the theme's amber half way to orange (1, 0.43, 0), so it parts from INK-HI in luminance; floored at <see cref="CautionFloorY"/>.</summary>
        public static Rgba Caution(Rgba game) => FloorY(Mix(game.WithAlpha(1f), new Rgba(1f, 0.43f, 0f), 0.5f), CautionFloorY);

        /// <summary>WARNING: the theme's red lifted 0.18 toward white (its face clears the rim 4.5:1), floored at <see cref="WarningFloorY"/>.</summary>
        public static Rgba Warning(Rgba game) => FloorY(Mix(game.WithAlpha(1f), new Rgba(1f, 1f, 1f), 0.18f), WarningFloorY);

        /// <summary><paramref name="c"/> lifted toward white in 0.05 steps until its relative luminance is at least <paramref name="y"/>. Allocation free; a colour already bright enough never enters the loop.</summary>
        public static Rgba FloorY(Rgba c, float y)
        {
            Rgba o = c;
            for (float k = 0.05f; o.RelativeLuminance < y && k <= 1.0001f; k += 0.05f) o = Mix(c, new Rgba(1f, 1f, 1f), k);
            return o;
        }

        /// <summary>The line alpha step nearest to <paramref name="alpha"/>: 1, 0.7 or 0.4. A fade (under 0.3) passes through unsnapped.</summary>
        public static float Line(float alpha)
        {
            if (alpha >= 0.85f) return alpha > 1f ? 1f : LineFull;
            if (alpha >= 0.55f) return LineMid;
            return alpha >= 0.3f ? LineLow : alpha;
        }

        /// <summary>A text alpha: DIM is a tone now, so 0.5 and up is full; under 0.5 it is a fade and passes through.</summary>
        public static float Text(float alpha) => alpha >= 0.5f ? 1f : alpha;

        /// <summary>The range ramp (AV LAYER 2.3): full colour inside <see cref="RangeNear"/>, flat past <see cref="RangeFar"/>, <see cref="RangeSteps"/> steps between.</summary>
        public const float RangeNear = 5000f, RangeFar = 40000f, RangeSatFar = 0.35f, RangeValueFar = 0.65f;
        public const int RangeSteps = 16;

        /// <summary>The ramp step of a distance, 0 (near, full colour) to 15 (far): smoothstep over Near..Far, quantised so a native write lands only on a step change. NaN is near.</summary>
        public static int RangeStep(float metres)
        {
            float x = (metres - RangeNear) / (RangeFar - RangeNear);
            if (!(x > 0f)) return 0;
            if (x > 1f) x = 1f;
            float t = x * x * (3f - 2f * x);
            return (int)(t * (RangeSteps - 1) + 0.5f);
        }

        /// <summary>
        /// <paramref name="ink"/> at <paramref name="metres"/>: desaturated toward its own luma (hue kept, never toward red) to 0.35 and its alpha dimmed to 0.65 at range. Step 0 returns the ink unchanged.
        /// Exceptions (selected, fresh, flashing, jammed, threats on us) are the caller's: they skip this.
        /// </summary>
        public static Rgba Range(Rgba ink, float metres)
        {
            int step = RangeStep(metres);
            if (step == 0) return ink;
            float k = step / (float)(RangeSteps - 1);
            float s = 1f - (1f - RangeSatFar) * k, v = 1f - (1f - RangeValueFar) * k;
            float l = 0.2126f * ink.R + 0.7152f * ink.G + 0.0722f * ink.B;
            return new Rgba(l + (ink.R - l) * s, l + (ink.G - l) * s, l + (ink.B - l) * s, ink.A * v);
        }

        private static Rgba Mix(Rgba a, Rgba b, float k) => new Rgba(a.R + (b.R - a.R) * k, a.G + (b.G - a.G) * k, a.B + (b.B - a.B) * k);
    }

    /// <summary>
    /// THE GLOW of the visor (2026-11-19, user: "shadows and glow standardised and reactive to element size ... AR vibe"): every word and stroke of the mod's HUD is light with one soft halo of its own colour,
    /// never a dark shadow. The halo starts at <see cref="Alpha"/> of the element's alpha and its reach follows the element's size: a stroke's halo is <see cref="Stroke"/> x its width each side (hairline 1.25,
    /// heavy 2.5 units), a word's is TMP's underlay in SDF units (<see cref="TextDilate"/>/<see cref="TextSoftness"/>), which scale with the glyph, so a HERO numeral glows wider than a LABEL.
    /// </summary>
    internal static class HudGlow
    {
        public const float Alpha = 0.3f, Stroke = 1.25f, TextDilate = 0.05f, TextSoftness = 0.3f;
    }

    /// <summary>THE LINE WEIGHTS, design units: hairline (rules, ticks, frames), standard (rails, scope rings, brackets), heavy (the one emphasised element of a block, the banner bar).</summary>
    internal static class HudStroke
    {
        public const float Hairline = 1f, Standard = 1.5f, Heavy = 2f;

        /// <summary>The stroke a width asked for belongs to.</summary>
        public static float Snap(float w) => w <= 1.25f ? Hairline : w <= 1.75f ? Standard : Heavy;
    }
}
