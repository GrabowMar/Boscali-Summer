using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The CM cell's numbers (ONE BAR v3 3.2): up to three tiles, each stacked and centred (2026-11-19, user: bigger icons, the number under them): the glyph large on top, the EMPHASIS numeral under it, the short name (or the dispense count) on R3.</summary>
    internal static partial class StoresLayout
    {
        /// <summary>Tiles a cell has room for (an airframe with a fourth countermeasure shows the first three of the cycle).</summary>
        public const int TileMax = 3;
        /// <summary>The glyph's centre below the bar line's top and its scale (x1.7 in a tile under 60 wide), the numeral's row.</summary>
        public const float TileIconY = 37f, TileGlyphScale = 1.9f, TileGlyphTight = 1.6f, TileNumY = 63f;
        /// <summary>The called tile's outline: from Pad left of the tile to 2 Pad short of its end, from 20 below the line's top to its R3 row; the selected tile's rule stops 6 short of its right edge.</summary>
        public const float TileCallPad = 1f, TileCallTop = 20f, TileCallH = 72f, TileSelInset = 6f;
    }
}
