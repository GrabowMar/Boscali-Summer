using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>What the holo shows in the bottom-left corner now: nothing (the game's own corner map), the standing MINI, or the BIG one the held map key raises.</summary>
    internal enum HoloShape : byte { None, Mini, Big }

    /// <summary>
    /// The TAC MAP, as pure rules: which of the three presentations is up (<see cref="Choose"/>), what ground each one reaches (the range options and their migration), how the camera looks at it (tilt, where the aircraft
    /// stands in the frame, how dense the grid is), how many marks each one carries, how often each is drawn and where the one frame is while it grows between the two sizes. The MINI is a frame of 340 x 300 units
    /// <see cref="MiniMargin"/> in from the screen's left and bottom edges; the BIG one is 82 % of the screen height tall and 1.3 times as wide (at most 92 % of the screen width), centred on the screen. One hologram, two
    /// sizes: <see cref="Size"/> runs 0 (MINI) to 1 (BIG) over <see cref="GrowSeconds"/>. No Unity types, so it is unit-tested.
    /// </summary>
    internal static class HoloPresentation
    {
        /// <summary>The MINI's frame and its distance from the left and bottom screen edges, in units (a unit is the screen's height over 1080).</summary>
        internal const float MiniWidth = 340f, MiniHeight = 300f, MiniMargin = 16f;
        /// <summary>The BIG frame's height as a share of the screen height, its width as a multiple of that height, and the most of the screen width it may take.</summary>
        internal const float BigHeightShare = .82f, BigAspect = 1.3f, BigWidthShare = .92f;
        /// <summary>The time the picture takes to change between its two sizes, and to fade in when it first appears.</summary>
        internal const float GrowSeconds = .18f;
        /// <summary>The most units each presentation carries, and the most airbases it names.</summary>
        internal const int MiniUnits = 48, MiniAirbases = 1;
        /// <summary>The texture the picture is drawn into (a part of it, as many pixels as the frame shows times <see cref="Oversample"/> at most) and the smallest part used.</summary>
        internal const int TextureWidth = 1280, TextureHeight = 960, MinimumPixels = 96;
        internal const float Oversample = 1.5f;
        /// <summary>Degrees above the horizon the camera looks from: the MINI is a flat 2D plan view like the game's own corner map, the BIG one shows relief.</summary>
        internal const float MiniTilt = 90f, BigTilt = 55f;
        /// <summary>Where the aircraft stands in the frame: across the middle always, and up from the bottom edge as a share of the frame's height. The MINI centres it, as the game's corner map does.</summary>
        internal const float AircraftAcross = .5f, MiniAircraftUp = .5f;
        /// <summary>Grid cells across the picture's reach (twice the range, at the aircraft's depth): a denser grid for the bigger picture.</summary>
        internal const int MiniCells = 48, BigCells = 96;

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

        // ------------------------------------------------------------------ the range options

        /// <summary>The MINI's range in kilometres (from the aircraft to the frame's side): 10, 20 or 40. The old option 5 is the nearest of the new ones, 10; anything else is the default, 20.</summary>
        internal static int MiniRange(int km) => km == 10 || km == 20 || km == 40 ? km : km == 5 ? 10 : 20;

        /// <summary>The held holo's range in kilometres: 20, 40 or 80. The old option 10 is the nearest of the new ones, 20; anything else is the default, 40.</summary>
        internal static int HoloRange(int km) => km == 20 || km == 40 || km == 80 ? km : km == 10 ? 20 : 40;

        /// <summary>The one-time move of a saved MINI range from the disc's options (5, 10, 20: a radius) to the tile's (10, 20, 40: a half-width): doubled, so the pilot's pick keeps its place in the list; anything else is the default.</summary>
        internal static int MigrateMiniRange(int oldKm) => oldKm == 5 || oldKm == 10 || oldKm == 20 ? oldKm * 2 : 20;

        /// <summary>The same move for a saved HOLO RANGE (10, 20, 40 become 20, 40, 80).</summary>
        internal static int MigrateHoloRange(int oldKm) => oldKm == 10 || oldKm == 20 || oldKm == 40 ? oldKm * 2 : 40;

        /// <summary>The BIG picture's range: the HOLO RANGE, never less than the MINI's (a hologram that grows does not look at less ground).</summary>
        internal static int BigRange(int holoKm, int miniKm) => Math.Max(HoloRange(holoKm), MiniRange(miniKm));

        // ------------------------------------------------------------------ the camera and the grid

        /// <summary>The camera's tilt above the horizon for a presentation, in degrees.</summary>
        internal static float Tilt(HoloShape shape) => shape == HoloShape.Mini ? MiniTilt : BigTilt;

        /// <summary>How much stronger the picture's lines are drawn: the MINI's by <see cref="HoloGrid.MiniLineGain"/>, the BIG map's as they are.</summary>
        internal static float LineGain(HoloShape shape) => shape == HoloShape.Mini ? HoloGrid.MiniLineGain : 1f;

        /// <summary>
        /// Where the aircraft stands up the frame, as a share of the frame's height from its bottom edge: 38 % on the MINI heading up (the camera looks ahead of it), the middle on the MINI north up and always on
        /// the BIG one. <see cref="Aircraft"/> gives both coordinates.
        /// </summary>
        internal static float AircraftUp(HoloShape shape, bool northUp) => shape == HoloShape.Mini && !northUp ? MiniAircraftUp : .5f;

        /// <summary>The aircraft's place in the frame, as shares of its width and height from the lower-left corner.</summary>
        internal static void Aircraft(HoloShape shape, bool northUp, out float across, out float up)
        {
            across = AircraftAcross;
            up = AircraftUp(shape, northUp);
        }

        /// <summary>Grid cells across the picture's reach for a presentation: 48 on the MINI (7 pixels apart at 1080p; 64 was a 5 pixel hatch that read as noise), 96 on the BIG one.</summary>
        internal static int GridCells(HoloShape shape) => shape == HoloShape.Mini ? MiniCells : BigCells;

        /// <summary>The frame's width over its height at the smallest the picture is ever drawn with (the MINI's, which the growing BIG one starts from): the grid is made to cover a frame this narrow, which sees the most ground up and down.</summary>
        internal static float ReferenceAspect => MiniWidth / MiniHeight;

        // ------------------------------------------------------------------ marks and rates

        /// <summary>The most units a presentation draws, nearest first (the game's own list is longer).</summary>
        internal static int UnitCap(HoloShape shape) => shape == HoloShape.Mini ? MiniUnits : HoloMarks.MaximumUnits;

        /// <summary>The most airbases a presentation names, nearest first.</summary>
        internal static int AirbaseCap(HoloShape shape) => shape == HoloShape.Mini ? MiniAirbases : HoloMarks.MaximumAirbases;

        // ------------------------------------------------------------------ the frame

        /// <summary>Moves <paramref name="size"/> toward the presentation's size (1 for BIG, 0 otherwise) by <paramref name="seconds"/> of the change; the result stays within 0..1.</summary>
        internal static float Size(float size, HoloShape shape, float seconds)
        {
            float target = shape == HoloShape.Big ? 1f : 0f, step = seconds / GrowSeconds;
            if (size < target) return Math.Min(target, size + step);
            return Math.Max(target, size - step);
        }

        /// <summary>
        /// The frame for a size (0..1, eased) on a canvas of <paramref name="canvasWidth"/> x <paramref name="canvasHeight"/> units, from the canvas's lower-left corner: at 0 the MINI (340 x 300 units, 16 in from the edges), at 1 the
        /// BIG one (82 % of the height, 1.3 times as wide but at most 92 % of the width, centred), growing out of the corner as the size runs up.
        /// </summary>
        internal static void Frame(float size, float canvasWidth, float canvasHeight, out float x, out float y, out float width, out float height)
        {
            float unit = canvasHeight / 1080f, e = Smooth(size);
            float bigHeight = BigHeightShare * canvasHeight, bigWidth = Math.Min(BigAspect * bigHeight, BigWidthShare * canvasWidth);
            x = MathF.Round(Lerp(MiniMargin * unit, (canvasWidth - bigWidth) * .5f, e));
            y = MathF.Round(Lerp(MiniMargin * unit, (canvasHeight - bigHeight) * .5f, e));
            width = MathF.Round(Lerp(MiniWidth * unit, bigWidth, e));
            height = MathF.Round(Lerp(MiniHeight * unit, bigHeight, e));
        }

        /// <summary>The share of the way from the MINI's shelf and corner to the BIG's for a size (the same ease the frame takes), so furniture glides with it.</summary>
        internal static float Ease(float size) => Smooth(size);

        /// <summary>
        /// How many pixels of the texture a frame of <paramref name="widthPixels"/> x <paramref name="heightPixels"/> screen pixels is drawn with: up to one and a half times its size, within the texture (the whole frame keeps its
        /// shape), so a small picture is not a shrunk big one.
        /// </summary>
        internal static void RenderPixels(float widthPixels, float heightPixels, out int width, out int height)
        {
            if (!(widthPixels > 0f && heightPixels > 0f))
            {
                width = height = MinimumPixels;
                return;
            }
            float scale = Math.Min(Oversample, Math.Min(TextureWidth / widthPixels, TextureHeight / heightPixels));
            width = Math.Min(TextureWidth, Math.Max(MinimumPixels, (int)MathF.Round(widthPixels * scale)));
            height = Math.Min(TextureHeight, Math.Max(MinimumPixels, (int)MathF.Round(heightPixels * scale)));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Smooth(float t) => Scalar.SmoothStep(0f, 1f, t);
    }
}
