using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>What the holo shows in the bottom-left corner now: nothing (the game's own corner map), the standing MINI, or the BIG one the held map key raises.</summary>
    internal enum HoloShape : byte { None, Mini, Big }

    /// <summary>
    /// The TAC MAP, as pure rules: which of the three presentations is up (<see cref="Choose"/>), the radius each one reaches, how many marks each one carries, how often each is drawn and
    /// how big the one picture is while it grows between the two sizes (a square of <see cref="MiniSide"/> units, <see cref="MiniMargin"/> in from the screen edges, to the held map's
    /// 46 % of the screen height, 24 units in). One hologram, two sizes: <see cref="Size"/> runs 0 (MINI) to 1 (BIG) over <see cref="GrowSeconds"/>. No Unity types, so it is unit-tested.
    /// </summary>
    internal static class HoloPresentation
    {
        /// <summary>The MINI's side and its distance from the left and bottom screen edges, in units (a unit is the screen's height over 1080).</summary>
        internal const float MiniSide = 300f, MiniMargin = 16f;
        /// <summary>The BIG picture's side as a share of the screen height, and its distance from the edges, in units.</summary>
        internal const float BigShare = .46f, BigMargin = 24f;
        /// <summary>The time the picture takes to change between its two sizes, and to fade in when it first appears.</summary>
        internal const float GrowSeconds = .18f;
        /// <summary>Seconds between pictures: the MINI is drawn at 10 Hz, the BIG at 20 Hz.</summary>
        internal const float MiniInterval = .1f, BigInterval = .05f;
        /// <summary>The most units each presentation carries, and the most airbases it names.</summary>
        internal const int MiniUnits = 48, MiniAirbases = 1;
        /// <summary>The texture the picture is drawn into is square; the picture uses as much of it as the screen shows pixels, times this.</summary>
        internal const int TextureSize = 768, MinimumPixels = 256;
        internal const float Oversample = 1.5f;

        /// <summary>
        /// The presentation for where the pilot is. Nothing while the corner map cannot stand in (<paramref name="up"/> false: no living aircraft, no HUD, a pause, a piece that failed to build) and at the game's
        /// maximised map; the BIG one while the key holds the holo; otherwise the MINI when the TAC MAP is on HOLO (<paramref name="miniOn"/>) and nothing (the game's own corner map) when it is CLASSIC.
        /// </summary>
        internal static HoloShape Choose(HoloStage stage, bool miniOn, bool up)
        {
            if (!up || stage == HoloStage.Mfd) return HoloShape.None;
            if (stage == HoloStage.Holo) return HoloShape.Big;
            return miniOn ? HoloShape.Mini : HoloShape.None;
        }

        /// <summary>The MINI's radius in kilometres: 5, 10 or 20; anything else is the default, 10.</summary>
        internal static int MiniRange(int km) => km == 5 || km == 20 ? km : 10;

        /// <summary>The held holo's radius in kilometres: 10, 20 or 40; anything else is the default, 20.</summary>
        internal static int HoloRange(int km) => km == 10 || km == 40 ? km : 20;

        /// <summary>The BIG picture's radius: the HOLO RANGE, never less than the MINI's (a hologram that grows does not look at less ground).</summary>
        internal static int BigRange(int holoKm, int miniKm) => Math.Max(HoloRange(holoKm), MiniRange(miniKm));

        /// <summary>The most units a presentation draws, nearest first (the game's own list is longer).</summary>
        internal static int UnitCap(HoloShape shape) => shape == HoloShape.Mini ? MiniUnits : HoloMarks.MaximumUnits;

        /// <summary>The most airbases a presentation names, nearest first.</summary>
        internal static int AirbaseCap(HoloShape shape) => shape == HoloShape.Mini ? MiniAirbases : HoloMarks.MaximumAirbases;

        /// <summary>
        /// Seconds until the next picture: every frame while the picture is changing size (its marks follow the frame), otherwise the presentation's own rate.
        /// </summary>
        internal static float RenderInterval(HoloShape shape, bool changing) => changing ? 0f : shape == HoloShape.Big ? BigInterval : MiniInterval;

        /// <summary>Moves <paramref name="size"/> toward the presentation's size (1 for BIG, 0 otherwise) by <paramref name="seconds"/> of the change; the result stays within 0..1.</summary>
        internal static float Size(float size, HoloShape shape, float seconds)
        {
            float target = shape == HoloShape.Big ? 1f : 0f, step = seconds / GrowSeconds;
            if (size < target) return Math.Min(target, size + step);
            return Math.Max(target, size - step);
        }

        /// <summary>The picture's side in canvas units for a size (0..1, eased) on a canvas of <paramref name="canvasHeight"/>: 300 units at 0, 46 % of the height at 1.</summary>
        internal static float Side(float size, float canvasHeight)
        {
            float unit = canvasHeight / 1080f;
            return MathF.Round(Lerp(MiniSide * unit, BigShare * canvasHeight, Smooth(size)));
        }

        /// <summary>The picture's distance from the left and bottom edges for a size: 16 units at 0, 24 at 1.</summary>
        internal static float Margin(float size, float canvasHeight)
        {
            float unit = canvasHeight / 1080f;
            return MathF.Round(Lerp(MiniMargin * unit, BigMargin * unit, Smooth(size)));
        }

        /// <summary>The share of the way from the MINI's shelf and corner to the BIG's for a size (the same ease the side takes), so furniture glides with the frame.</summary>
        internal static float Ease(float size) => Smooth(size);

        /// <summary>How many pixels of the texture a picture of <paramref name="sidePixels"/> screen pixels is drawn with: one and a half times its size, within the texture, so a small picture is not a shrunk big one.</summary>
        internal static int RenderPixels(float sidePixels)
        {
            int pixels = (int)MathF.Round(sidePixels * Oversample);
            return Math.Min(TextureSize, Math.Max(MinimumPixels, pixels));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Smooth(float t) => Scalar.SmoothStep(0f, 1f, t);
    }
}
