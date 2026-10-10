using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Which rows of the status board exist this tick. A row that is off takes no room.</summary>
    internal struct BoardRows
    {
        /// <summary>The annunciator lamps of the aircraft's own switches.</summary>
        public bool Systems;
        /// <summary>LOCK / SPKE / MSL lamps, the threat dial and the shot pips.</summary>
        public bool Threat;
        /// <summary>Ranked threat detail lines under the threat lamps (0..<see cref="HudBoardLayout.MaxDetailRows"/>).</summary>
        public int DetailRows;
        /// <summary>Countermeasure tiles.</summary>
        public bool Cm;
        /// <summary>Persistent damage cautions.</summary>
        public bool Damage;
        /// <summary>Fuel, endurance and the bingo/joker state.</summary>
        public bool Fuel;
        /// <summary>The outlined feed cells.</summary>
        public bool Cells;
        /// <summary>The cue bar and its detail words.</summary>
        public bool Strip;
        /// <summary>The one-line notice ticker.</summary>
        public bool Ticker;
    }

    /// <summary>One row's top offset and height, in board pixels measured down from the board's top edge.</summary>
    internal struct RowBox
    {
        public float Y, H;
        public float Bottom => Y + H;
    }

    /// <summary>Where every row of the board sits. Rows run top to bottom in the order of the fields.</summary>
    internal struct BoardGeometry
    {
        public float Width, Height;
        public RowBox Systems, Threat, Detail, Cm, Damage, Fuel, Cells, Strip, Ticker;
    }

    /// <summary>Horizontal positions in the threat row, board pixels from the left edge.</summary>
    internal struct ThreatRowBox
    {
        /// <summary>Centre and radius of the threat dial.</summary>
        public float DialX, DialR;
        /// <summary>Left edge of the HI / LO label, and of the SHOT legend.</summary>
        public float HiLoX, LegendX;
        /// <summary>Left edge of the first shot pip, the centre-to-centre step and the pip's size.</summary>
        public float PipX, PipStep, PipSize;
        /// <summary>Pips that fit (always <see cref="HudBoardLayout.Pips"/>: the step shrinks instead).</summary>
        public int MaxPips;
    }

    /// <summary>
    /// Pure layout of the status board (2026-10-10). Every row's position derives from which rows exist and the board's width,
    /// so adding a row never moves a neighbour by hand and no row's x depends on how many system lamps the airframe has
    /// (the old threat row measured its dial from the lamp count and fell off the board at three lamps). One design unit
    /// <c>u</c> is the native weapon cell height over 95, as in <c>StatusPanel</c>. No UnityEngine.
    /// </summary>
    internal static class HudBoardLayout
    {
        public const float Pad = 4f, Gap = 4f;
        public const float LampH = 24f, LampGap = 3f;
        public const float DetailRowH = 18f, TileH = 22f;
        public const float CellH = 68f, CellGap = 4f;
        public const float BarH = 6f, StripTextH = 20f;
        public const float TickerH = 20f;
        public const int SystemSlots = 7, Tiles = 4, MaxCells = 4, MaxDetailRows = 2;
        /// <summary>Shot pips drawn (the <see cref="ShotLedger"/> capacity).</summary>
        public const int Pips = 8;

        /// <summary>Top-down row positions for the rows that exist; absent rows are zero-sized.</summary>
        public static BoardGeometry Compute(in BoardRows rows, float width, float u)
        {
            var g = new BoardGeometry { Width = width };
            float y = Pad * u;
            bool any = false;
            g.Systems = Take(rows.Systems, LampH, u, ref y, ref any);
            g.Threat = Take(rows.Threat, LampH, u, ref y, ref any);
            int detail = Math.Max(0, Math.Min(MaxDetailRows, rows.DetailRows));
            g.Detail = Take(detail > 0, detail * DetailRowH, u, ref y, ref any);
            g.Cm = Take(rows.Cm, TileH, u, ref y, ref any);
            g.Damage = Take(rows.Damage, TileH, u, ref y, ref any);
            g.Fuel = Take(rows.Fuel, TileH, u, ref y, ref any);
            g.Cells = Take(rows.Cells, CellH, u, ref y, ref any);
            g.Strip = Take(rows.Strip, BarH + StripTextH, u, ref y, ref any);
            g.Ticker = Take(rows.Ticker, TickerH, u, ref y, ref any);
            g.Height = any ? y - Gap * u + Pad * u : 0f;
            return g;
        }

        private static RowBox Take(bool on, float h, float u, ref float y, ref bool any)
        {
            if (!on) return default;
            var box = new RowBox { Y = y, H = h * u };
            y += (h + Gap) * u;
            any = true;
            return box;
        }

        /// <summary>Width of one of the <see cref="SystemSlots"/> lamp slots; the threat row uses the same grid.</summary>
        public static float SystemSlotWidth(float width, float u) =>
            (width - 2f * Pad * u - LampGap * u * (SystemSlots - 1)) / SystemSlots;

        /// <summary>Left edge of lamp slot <paramref name="index"/>.</summary>
        public static float SystemSlotX(float width, float u, int index) =>
            Pad * u + index * (SystemSlotWidth(width, u) + LampGap * u);

        /// <summary>Width of one of the <see cref="Tiles"/> tiles of the countermeasure and damage rows.</summary>
        public static float TileWidth(float width, float u) =>
            (width - 2f * Pad * u - LampGap * u * (Tiles - 1)) / Tiles;

        public static float TileX(float width, float u, int index) => Pad * u + index * (TileWidth(width, u) + LampGap * u);

        /// <summary>Width of one feed cell: a quarter of the row however many cells there are (one cell is not a banner).</summary>
        public static float CellWidth(float width, float u) =>
            (width - 2f * Pad * u - CellGap * u * (MaxCells - 1)) / MaxCells;

        public static float CellX(float width, float u, int index) =>
            Pad * u + index * (CellWidth(width, u) + CellGap * u);

        /// <summary>
        /// The threat row: three lamps on the system grid, the dial after them, HI / LO beside the dial's rim (clear of the
        /// chevron that rides it), then the SHOT legend and the pips. The pip step shrinks to fit eight at any width.
        /// </summary>
        public static ThreatRowBox ThreatRow(float width, float u)
        {
            var t = new ThreatRowBox { MaxPips = Pips };
            t.DialR = LampH * u * 0.46f;
            t.DialX = Pad * u + 3f * (SystemSlotWidth(width, u) + LampGap * u) + LampH * u * 0.5f + 2f * u;
            t.HiLoX = t.DialX + t.DialR + 9f * u;
            t.LegendX = t.HiLoX + 26f * u;
            t.PipX = t.LegendX + 32f * u;
            float span = Math.Max(1f, width - Pad * u - t.PipX);
            t.PipStep = Math.Min(14f * u, span / Pips);
            t.PipSize = Math.Max(t.PipStep - 3f * u, 4f * u);
            return t;
        }
    }
}
