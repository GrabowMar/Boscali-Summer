using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The status board's row geometry: rows stack without overlap, nothing depends on the lamp count, eight pips always fit.</summary>
    internal static class HudBoardLayoutTests
    {
        /// <summary>Board widths in design units; the real width is this times the scale <c>u</c> (a bigger UI is a wider board).</summary>
        private static readonly float[] Designs = { 320f, 440f, 600f, 800f };
        private static readonly float[] Units = { 1f, 1.5f };

        public static void Run()
        {
            Stacking();
            TestAssert.Near(HudBoardLayout.Compute(default, 440f, 1f).Height, 0f, "No rows, no board");
            Grid();
            ThreatRowFits();
        }

        private static void Stacking()
        {
            var all = new BoardRows { Systems = true, Threat = true, DetailRows = 2, Cm = true, Damage = true, Fuel = true, Cells = true, Strip = true, Ticker = true };
            foreach (float u in Units)
            {
                BoardGeometry g = HudBoardLayout.Compute(all, 440f * u, u);
                RowBox[] order = { g.Systems, g.Threat, g.Detail, g.Cm, g.Damage, g.Fuel, g.Cells, g.Strip, g.Ticker };
                float previous = HudBoardLayout.Pad * u - HudBoardLayout.Gap * u;
                foreach (RowBox row in order)
                {
                    TestAssert.That(row.H > 0f, "Every present row has height");
                    TestAssert.Near(row.Y, previous + HudBoardLayout.Gap * u, "Rows stack top to bottom with one gap");
                    previous = row.Bottom;
                }
                TestAssert.Near(g.Height, previous + HudBoardLayout.BottomPad(u), "The board ends one bottom margin below its last row");
                TestAssert.That(HudBoardLayout.BottomPad(u) + HudBoardLayout.Pad * u >= 15f - 0.01f, "...which clears the plate's 14 px chamfer at the first row's left edge, at scale " + u);
                TestAssert.Near(g.Detail.H, 2f * HudBoardLayout.DetailRowH * u, "Two threat lines take two line heights");
            }

            BoardGeometry cellsOnly = HudBoardLayout.Compute(new BoardRows { Cells = true }, 440f, 1f);
            TestAssert.Near(cellsOnly.Cells.Y, HudBoardLayout.Pad, "A lone row starts at the top pad");
            TestAssert.Near(cellsOnly.Height, HudBoardLayout.Pad + HudBoardLayout.CellH + HudBoardLayout.BottomPad(1f), "...and the board wraps it exactly");
            TestAssert.That(cellsOnly.Systems.H == 0f && cellsOnly.Ticker.H == 0f, "Absent rows take no room");
            TestAssert.Near(HudBoardLayout.Compute(new BoardRows { DetailRows = 9, Threat = true }, 440f, 1f).Detail.H,
                HudBoardLayout.MaxDetailRows * HudBoardLayout.DetailRowH, "More than the maximum detail lines is clamped");
        }

        private static void Grid()
        {
            foreach (float design in Designs)
                foreach (float u in Units)
                {
                    float width = design * u, edge = width - HudBoardLayout.Pad * u;
                    float slot = HudBoardLayout.SystemSlotWidth(width, u);
                    TestAssert.Near(HudBoardLayout.SystemSlotX(width, u, HudBoardLayout.SystemSlots - 1) + slot, edge,
                        "Seven lamp slots fill the board exactly at design width " + design);
                    TestAssert.Near(HudBoardLayout.TileX(width, u, HudBoardLayout.Tiles - 1) + HudBoardLayout.TileWidth(width, u), edge,
                        "Four tiles fill the board exactly at design width " + design);
                    TestAssert.Near(HudBoardLayout.CellX(width, u, HudBoardLayout.MaxCells - 1) + HudBoardLayout.CellWidth(width, u), edge,
                        "Four cells fill the board exactly at design width " + design);
                }
        }

        private static void ThreatRowFits()
        {
            foreach (float design in Designs)
                foreach (float u in Units)
                {
                    float width = design * u;
                    ThreatRowBox t = HudBoardLayout.ThreatRow(width, u);
                    TestAssert.That(t.MaxPips == HudBoardLayout.Pips, "Eight pips at design width " + design);
                    TestAssert.That(t.PipX + t.PipStep * HudBoardLayout.Pips <= width - HudBoardLayout.Pad * u + 0.01f,
                        "All eight pips fit inside the board at design width " + design + ", u " + u);
                    TestAssert.That(t.PipSize >= 4f * u && t.PipSize <= t.PipStep, "Pips stay visible and do not overlap at design width " + design);
                    TestAssert.That(t.DialX - t.DialR > HudBoardLayout.Pad * u + 3f * HudBoardLayout.SystemSlotWidth(width, u),
                        "The dial starts after the three threat lamps at design width " + design);
                    TestAssert.That(t.HiLoX > t.DialX + t.DialR + 8f * u, "HI/LO clears the chevron that rides the dial rim at design width " + design);
                    TestAssert.That(t.LegendX > t.HiLoX && t.PipX > t.LegendX, "Dial, HI/LO, legend and pips run left to right at design width " + design);
                }
        }
    }
}
