using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The numbers inside the stores cells (WPN here; CM in StoresLayout.Cm.cs, SYS's capacitor row in StoresLayout.Power.cs), pure: board units from the block's own rectangle (<see cref="BoardRect"/>), the y of a word the middle of its line, as LAYOUT.md 3.1 to 3.3 gives
    /// them for WIDE and STACK. Where a block sits is <see cref="ScopeLayout"/>'s; what is inside it is this table, so a later change to the design is a change here.
    /// </summary>
    internal static partial class StoresLayout
    {
        /// <summary>The prototype's face advances this many em per character (what <c>ScopeText</c> sizes its words to).</summary>
        private const float Advance = 0.65f;

        // ---- WPN (ONE BAR spec 4.2): rows come from ScopeLayout (ShelfY, RuleY, R1..R3); x values are cell-local.
        public const float NumeralX = 1f, NumeralGap = 8f, MaxSize = 12f, MaxGap = 5f, MaxDy = 4f;
        /// <summary>The numeral's room bound (<see cref="NumeralSize"/>); the arm word itself is right-aligned to the cell end.</summary>
        public const float ArmWordX = 200f;
        public const float NameSize = HudType.Label, WordSize = 12f, WordGap = 10f, TagGap = 10f;
        public const float ArtY = 23f, ArtH = 49f, GlyphMaxW = 220f, GlyphHalf = 8f;

        /// <summary>The rounds numeral: EMPHASIS (22, still the biggest value of the stores) when it and its "/max" (LABEL) end <see cref="NumeralGap"/> short of the arm column, else VALUE (16). The sizes are steps of the type scale; the board draws them at the boost.</summary>
        public static float NumeralSize(int digits, int maxDigits)
        {
            float slash = maxDigits > 0 ? MaxGap + (1 + maxDigits) * Advance * HudType.Label * HudType.BoardBoost : 0f, room = ArmWordX - NumeralX - NumeralGap - slash;
            return Math.Max(1, digits) * Advance * HudType.Emphasis * HudType.BoardBoost <= room ? HudType.Emphasis : HudType.Value;
        }

        // ---- the station rail: n markers a pitch apart, marker i at 1 + i * pitch; a glyph then the tag, the selected one marked by a lit rule segment under it
        public const float RailX = 1f, RailGlyphHalf = 3.2f, RailGlyphLen = 14f, RailGunExtra = 1f;
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

        /// <summary>The lit rule segment under the selected station.</summary>
        public const float SelRuleW = 2f;

        // ---- R2's lock pips (ONE BAR v3 3.1): one per round of a pooled load of 2..PipMax, right-aligned before the TGT word; the art box ends ArtGap short of the first pip.
        public const int PipMax = 8;
        public const float PipPitch = 11f, PipSize = 6f, SpentPip = 4f, TgtW = 56f, PipGap = 8f, ArtGap = 10f;
    }
}
