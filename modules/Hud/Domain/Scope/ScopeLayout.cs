namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Which rows of the board exist this tick. A row that is off takes no room.</summary>
    internal struct BoardRows
    {
        /// <summary>The STORES HEADER's height in board units (see <see cref="ScopeLayout.StoresHeight"/>); 0: no header.</summary>
        public float StoresH;
        /// <summary>The annunciator lamp strip (system lamps left, threat lamps right).</summary>
        public bool Lamps;
        /// <summary>The scope's current height in board units (animated between <see cref="ScopeLayout.Compact"/> and <see cref="ScopeLayout.Full"/>).</summary>
        public float ScopeH;
        /// <summary>The gauge row (at least one gauge live).</summary>
        public bool Gauges;
        /// <summary>The one-line notice ticker.</summary>
        public bool Ticker;
    }

    /// <summary>One row's top offset and height in board units, measured down from the board's top edge.</summary>
    internal struct RowBox
    {
        public float Y, H;
        public float Bottom => Y + H;
    }

    internal struct BoardGeometry
    {
        public float Width, Height;
        public RowBox Stores, Lamps, Scope, Gauges, Ticker;
    }

    /// <summary>
    /// Pure layout of the TAC BOARD (HUD v8). Every number is the prototype's in 1080p design units; the board scales them by one
    /// unit <c>u</c> (the screen's height over 1080, never under <see cref="MinScale"/>). Rows run top to bottom: the stores header, the lamp strip,
    /// the scope, the gauge row, the ticker; a row that has nothing to say takes no room. No UnityEngine.
    /// </summary>
    internal static class ScopeLayout
    {
        /// <summary>Board width and the width inside the padding.</summary>
        public const float Width = 448f, Inner = 440f;
        public const float Pad = 4f, Gap = 4f;
        public const float LampH = 26f, GaugeH = 64f, TickerH = 24f;
        /// <summary>Scope height: full page, and the compact SYSTEMS strip.</summary>
        public const float Full = 300f, Compact = 100f;
        /// <summary>Header band height on a full page and on the compact strip, and a page strip under the scope body.</summary>
        public const float Head = 28f, HeadCompact = 22f, Strip = 26f;
        /// <summary>The plate's top-left and lower-left cuts: the corner of the first and last cell sits exactly on the diagonal.</summary>
        public const float Chamfer = 8f;
        /// <summary>The STORES HEADER: its cells, the capacitor row under them (3 apart) and the breathing gap before the lamp strip.</summary>
        public const float StoreH = 108f, CapH = 24f, CapGap = 3f, HeaderGap = 8f;
        /// <summary>The weapon cell's width beside the countermeasure cell (which takes the rest of the row less a 4 unit gap).</summary>
        public const float WeaponW = 252f, CellGap = 4f;
        /// <summary>The board's margin to the screen corner at 1080p, and the smallest scale: below 1080p a 12 unit word is 10 px, never smaller.</summary>
        public const float Margin = 8f, MinScale = 0.85f;
        public const float WipeSeconds = 0.16f;

        public static BoardGeometry Compute(in BoardRows rows)
        {
            var g = new BoardGeometry { Width = Width };
            float y = Pad;
            bool any = false;
            g.Stores = Take(rows.StoresH > 0f, rows.StoresH, ref y, ref any);
            if (g.Stores.H > 0f && (rows.Lamps || rows.ScopeH > 0f || rows.Gauges || rows.Ticker)) y += HeaderGap - Gap;     // breathing room before the next row
            g.Lamps = Take(rows.Lamps, LampH, ref y, ref any);
            g.Scope = Take(rows.ScopeH > 0f, rows.ScopeH, ref y, ref any);
            g.Gauges = Take(rows.Gauges, GaugeH, ref y, ref any);
            g.Ticker = Take(rows.Ticker, TickerH, ref y, ref any);
            g.Height = any ? y - Gap + Pad : 0f;
            return g;
        }

        private static RowBox Take(bool on, float h, ref float y, ref bool any)
        {
            if (!on) return default;
            var box = new RowBox { Y = y, H = h };
            y += h + Gap;
            any = true;
            return box;
        }

        /// <summary>The header's height: the weapon and countermeasure cells (<paramref name="cells"/>) and the capacitor row (<paramref name="cap"/>); 0 for neither.</summary>
        public static float StoresHeight(bool cells, bool cap) => (cells ? StoreH : 0f) + (cap ? (cells ? CapGap : 0f) + CapH : 0f);

        /// <summary>The scope's page-body height: its height less the page strip.</summary>
        public static float Body(bool compact, bool strip) => (compact ? Compact : Full) - (strip && !compact ? Strip : 0f);

        public static float SafeTop(bool compact) => (compact ? HeadCompact : Head) + 2f;

        /// <summary>Width of a system lamp, of the LOCK / SPKE lamps and of the MSL lamp.</summary>
        public const float LampW = 40f, ThreatLampW = 44f, MissileLampW = 46f, LampGap = 2f;

        /// <summary>Left edge of system lamp <paramref name="slot"/> (no gap before the first).</summary>
        public static float SystemLampX(int slot) => Pad + slot * (LampW + LampGap);

        /// <summary>Left edge of threat lamp 0 (LOCK), 1 (SPKE), 2 (MSL): right-aligned at the end of the strip.</summary>
        public static float ThreatLampX(int i)
        {
            float msl = Pad + Inner - MissileLampW;
            return i >= 2 ? msl : msl - (2 - i) * (LampGap + ThreatLampW);
        }

        public static float ThreatLampWidth(int i) => i == 2 ? MissileLampW : ThreatLampW;

        /// <summary>Width of one of the four gauge cells and the left edge of cell <paramref name="i"/>.</summary>
        public static float GaugeW => (Inner - 12f) / 4f;

        public static float GaugeX(int i) => Pad + i * (GaugeW + 4f);
    }
}
