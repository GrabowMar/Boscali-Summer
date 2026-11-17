using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The CM cell's numbers (ONE BAR v3 3.2): up to three tiles, each a glyph and an EMPHASIS numeral on R1, the short name on R2, the dispense count on R3.</summary>
    internal static partial class StoresLayout
    {
        /// <summary>Tiles a cell has room for (an airframe with a fourth countermeasure shows the first three of the cycle).</summary>
        public const int TileMax = 3;
        public const float TileGlyphX = 10f, TileNumX = 22f, TileGlyphScale = 1.5f, TileJamIcon = 18f;
        /// <summary>The called tile's outline: from Pad left of the tile to 2 Pad short of its end, 22 below the shelf, 46 tall (glyph x1 at three tiles); the selected tile's rule stops 6 short of its right edge.</summary>
        public const float TileCallPad = 1f, TileCallTop = 22f, TileCallH = 46f, TileSelInset = 6f;
    }
}
