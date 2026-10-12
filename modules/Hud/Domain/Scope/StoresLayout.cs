using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The numbers inside the three stores blocks (HERO, PWR BUS, EXPENDABLES), pure: board units from the block's own rectangle (<see cref="BoardRect"/>), the y of a word the middle of its line, as LAYOUT.md 3.1 to 3.3 gives
    /// them for WIDE and STACK. Where a block sits is <see cref="ScopeLayout"/>'s; what is inside it is this table, so a later change to the design is a change here.
    /// </summary>
    internal static class StoresLayout
    {
        /// <summary>The prototype's face advances this many em per character (what <c>ScopeText</c> sizes its words to).</summary>
        private const float Advance = 0.65f;

        // ---- every block: the caption shelf. The line is 6 under the block's top; it is drawn ShelfShort shorter than the block, the node and the 45 degree stub reaching past its end to the block's own edge.
        public const float ShelfY = 6f, ShelfShort = 8f;
        /// <summary>STACK's PWR BUS shelf is 12 shorter, so the stub tip is 12 clear of the EXPENDABLES shelf that starts 12 right of the block.</summary>
        public const float StackPwrShelfShort = 12f;

        /// <summary>The width of PWR BUS's caption shelf line in a block <paramref name="w"/> wide: 8 short of the block, 12 short in STACK.</summary>
        public static float PwrShelf(bool stack, float w) => w - (stack ? StackPwrShelfShort : ShelfShort);

        /// <summary>The width of EXPENDABLES's caption shelf line in a block <paramref name="w"/> wide.</summary>
        public static float ExpShelf(float w) => w - ShelfShort;

        // ---- HERO (LAYOUT.md 3.1)
        public const float NumeralX = 1f, NumeralY = 39f, NumeralSizeMax = 52f, NumeralSizeMin = 24f, NumeralGap = 8f;
        /// <summary>"/500" beside the numeral: 14 regular, 4 right of it, its middle 9 under the numeral's.</summary>
        public const float MaxSize = 14f, MaxGap = 4f, MaxDy = 9f;
        public const float ArmMarkerX = 156f, ArmMarkerSize = 6f, ArmWordX = 168f, ArmY = 23f, ArmSize = 14f;
        /// <summary>The tag column (LINK, TRT AUTO): from <see cref="ArmMarkerX"/>, the first row 41 down, rows 18 apart, tags 10 apart.</summary>
        public const float TagX = 156f, TagY = 41f, TagPitch = 18f, TagGap = 10f;
        public const float NameX = 0f, NameSize = 13f, WordGap = 10f, WordSize = 12f;
        /// <summary>The DCL flag clears the arm word (or a tag row) by this much.</summary>
        public const float DclGap = 10f;
        /// <summary>The glyph drawn when the store has no picture: at most 220 x 20, centred in the art box.</summary>
        public const float GlyphMaxW = 220f, GlyphHalf = 10f;

        /// <summary>What differs between HERO in WIDE and in STACK.</summary>
        internal readonly struct HeroPlan
        {
            public readonly float NameY, TagW, ArtX, ArtY, ArtW, ArtH, RailY, RailW, DclX, DclY, ShelfW;

            public HeroPlan(float nameY, float tagW, float artX, float artY, float artW, float artH, float railY, float railW, float dclX, float dclY, float shelfW)
            {
                NameY = nameY; TagW = tagW; ArtX = artX; ArtY = artY; ArtW = artW; ArtH = artH; RailY = railY; RailW = railW; DclX = dclX; DclY = dclY; ShelfW = shelfW;
            }
        }

        /// <summary>HERO's plan for a block <paramref name="w"/> wide: WIDE (272) has the art box under the name, STACK (440) to the right of the words; the declutter flag sits in the arm row (WIDE) or at the top right (STACK) when the shelf is off.</summary>
        public static HeroPlan Hero(bool stack, float w) => stack
            ? new HeroPlan(76f, 76f, 240f, 18f, 200f, 56f, 100f, w - 2f, w - 12f, 6f, w - ShelfShort)
            : new HeroPlan(68f, 116f, 0f, 76f, w, 56f, 144f, w - 2f, w - 6f, ArmY, w - ShelfShort);

        /// <summary>The rounds numeral's size: 52, made smaller (never under 24) when the numeral and its "/max" would not end <see cref="NumeralGap"/> short of the arm column.</summary>
        public static float NumeralSize(int digits, int maxDigits)
        {
            float slash = maxDigits > 0 ? MaxGap + (1 + maxDigits) * Advance * MaxSize : 0f, room = ArmMarkerX - NumeralX - NumeralGap - slash;
            return Math.Max(NumeralSizeMin, Math.Min(NumeralSizeMax, (float)Math.Floor(room / (Math.Max(1, digits) * Advance))));
        }

        /// <summary>The x of the next tag in a column <paramref name="column"/> wide, <paramref name="used"/> of its current row taken (0: the row is empty): packed after the last, or at the start of the next row when it would not fit (<paramref name="wrap"/>).</summary>
        public static float TagAt(float used, float tagW, float column, out bool wrap)
        {
            wrap = used > 0f && used + TagGap + tagW > column;
            return wrap || used <= 0f ? 0f : used + TagGap;
        }

        /// <summary>
        /// The row the DCL flag takes while the shelf is off: <paramref name="rowRight"/> says how far right the words of each row of the tag column go (row 0 is the arm word's, then the tag rows; 0 for an empty
        /// one). The first row whose words end <see cref="DclGap"/> clear of the flag's left edge, or the row after the last.
        /// </summary>
        public static int DclRow(ReadOnlySpan<float> rowRight, float dclLeft)
        {
            for (int k = 0; k < rowRight.Length; k++)
                if (rowRight[k] + DclGap <= dclLeft) return k;
            return rowRight.Length;
        }

        // ---- the station rail: n markers a pitch apart, marker i at 1 + i * pitch; a glyph then the tag, the selected one bracketed (px - 1, y - 8, pitch - 3, 16)
        public const float RailX = 1f, RailBracketArm = 4f, RailBracketHalf = 8f, RailGlyphHalf = 3.2f, RailGlyphLen = 14f, RailGunExtra = 1f;
        /// <summary>The rail is drawn for eight stations (WIDE's 33, STACK's 54): an aircraft with fewer packs its markers from the left at that pitch instead of spreading them over the block, so the selected one's brackets (3 short of the
        /// pitch) stay marker sized (four stations on WIDE spread to 54 in game: a bracket 51 wide round a marker 31 wide).</summary>
        public const int RailStations = 8;

        public static float RailPitch(int stations, float width) => (float)Math.Floor(width / Math.Max(RailStations, stations));

        /// <summary>The glyph's length on a rail whose markers are <paramref name="pitch"/> apart: 14 (the gun's 15), less when a crowded rail needs the room for its tags; 0 (the tag alone) under 6. The glyph starts 2 in and the tag 4 after it; the tag ends inside the selected marker's bracket.</summary>
        public static float RailGlyph(float pitch, bool twoDigitTags)
        {
            float room = (float)Math.Floor(pitch - 9f - (twoDigitTags ? 2f : 1f) * Advance * WordSize);
            return room >= 6f ? Math.Min(RailGlyphLen, room) : 0f;
        }

        /// <summary>Where a marker's tag starts, from the marker's x: after its glyph (2 in, <paramref name="glyph"/> long, 4 clear), or at 2 when it has none.</summary>
        public static float RailTagX(float glyph) => glyph > 0f ? 2f + glyph + 4f : 2f;

        // ---- PWR BUS (LAYOUT.md 3.2)
        public const float PwrNumeralY = 28f, PwrNumeralSize = 24f, PwrUnitY = 32f, PwrUnitGap = 4f, PwrRateY = 28f;
        public const float CellsTop = 44f, TickTop = 41f, TickBottom = 57f, JamMarkY = 59f, CostY = 66f;

        /// <summary>
        /// The jammer marker's y: +59 under a block that has room for it (one with its cost row, 72 high, or any in STACK, where nothing sits below). WIDE's block without a cost row ends at 54, and +59 stands in the gap under it against the
        /// shelf stub of the block below (its tip 8 under the block): it touched it at a full charge in game. It rides at the cost tick's foot, +57, then.
        /// </summary>
        public static float JamMark(bool room) => room ? JamMarkY : TickBottom;

        /// <summary>
        /// The jammer marker's x from the block's left: the fill's edge (<paramref name="edge"/>), but under a block with no room below it (WIDE without a cost row) no further right than the shelf node of the block below, whose stub rises from it
        /// to a tip at the block's right edge: at a full charge the marker stood right over that tip, 3 clear of it in game (the capacitor's last cell is 2 short of the block's edge, the triangle 3 either side of it).
        /// </summary>
        public static float JamMarkX(float edge, bool room, float width) => room ? edge : Math.Min(edge, ExpShelf(width));

        /// <summary>STACK's PWR BUS (216 wide) keeps its cells, rate word and cost label inside 196, so the rate word ends well short of the EXPENDABLES block beside it.</summary>
        public const float StackPwrInset = 20f;

        /// <summary>The width of PWR BUS's inner row (LAYOUT.md 3.2 "iw"): the block's own in WIDE (180), 196 of 216 in STACK.</summary>
        public static float PwrInner(bool stack, float w) => stack ? w - StackPwrInset : w;

        /// <summary>The cells' fade after a shot: just-spent cells are BRIGHT at this alpha, fading over <see cref="StoresMath.ShotWordS"/>.</summary>
        public const float SpentCellAlpha = 0.6f;

        // ---- EXPENDABLES (LAYOUT.md 3.3)
        public const float FlrY = 25f, ChfY = 46f, JamY = 66f, MarkerSize = 5f, CountX = 10f, CountSize = 14f, NameGap = 5f, MatrixX = 68f, StateClear = 8f;
        public const float CallPad = 5f, CallHalf = 9f, CallArm = 4f;
        public const float JamIcon = 14f, JamWordX = 19f, JamBarX = 52f, JamBarW = 72f, JamBarH = 3f;
        /// <summary>The dispensers a block has rows for: flares and chaff (an airframe with more shows the first two).</summary>
        public const int Rows = 2;

        /// <summary>The jammer row's y: under the dispenser rows there are, not under the two the block has room for (with flares only it sat 41 under them in game).</summary>
        public static float JamRow(int dispensers) => dispensers >= Rows ? JamY : dispensers == 1 ? ChfY : FlrY;
    }
}
