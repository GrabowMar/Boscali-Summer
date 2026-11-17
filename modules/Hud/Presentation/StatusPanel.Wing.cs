using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The shell of the board (ONE BAR v3): each bar line's frame (the glareshield rule, the cell ticks, the corner brackets), the threat lamps on the CM shelf while RWR is not seated, the SYS cell's caption and its fixed 4 x 2
    // lamp grid (FUEL DMG ACE GEAR over RDR NVG HOVR FA OFF), the mission rows at the top of the spine and the gauge line (LAYOUT.md 3.6), on the shell surface in box coordinates. Their data is read here too (PresentLamps, PresentGauges). Ordinary
    // ink only: a word and a bar per lamp, a glyph, a label and a value per gauge.
    internal sealed partial class StatusPanel
    {
        private readonly LampChange lampChange = new LampChange();
        private readonly Lamp[] lamps = new Lamp[HudLamps.Count];
        private readonly Lamp[] threatLamps = new Lamp[HudThreats.Count];
        private readonly bool[] recent = new bool[HudLamps.Count];
        /// <summary>The CM shelf's threat lamps (<see cref="GroupCm"/>, up to <see cref="GroupMax"/>): the threat lamp's index, its width and its cell-local x.</summary>
        private const int GroupCm = 0, Groups = 1, GroupMax = 4;
        private readonly int[] groupCount = new int[Groups], groupItem = new int[Groups * GroupMax];
        private readonly float[] groupWidth = new float[Groups * GroupMax], groupX = new float[Groups * GroupMax];
        /// <summary>Which of the SYS grid's lamps are drawn this tick, each at its own fixed slot (<see cref="HudLamps.Slot"/>).</summary>
        private readonly bool[] gridDrawn = new bool[HudLamps.Count];
        /// <summary>The CM shelf's lamps start here (cell-local x; the cell's width when it has none): the CM caption is dropped when it would reach them.</summary>
        private float cmLampsLeft = ScopeLayout.CmW;
        private readonly GaugeSpec[] pageGauges = new GaugeSpec[GaugeRow.Slots], pageScratch = new GaugeSpec[GaugeRow.Slots], registered = new GaugeSpec[16], shown = new GaugeSpec[GaugeRow.Slots];
        private readonly float[] gaugeLabel = new float[GaugeRow.Slots], gaugeValue = new float[GaugeRow.Slots], gaugeSub = new float[GaugeRow.Slots], gaugeWidth = new float[GaugeRow.Slots], gaugeX = new float[GaugeRow.Slots];
        private readonly int[] gaugeRow = new int[GaugeRow.Slots];
        private int gaugeCount, gaugeLines;
        /// <summary>The gauges keep their sub-words (they do while two lines hold the gauges with them: <see cref="GaugeRow.Lay"/>).</summary>
        private bool gaugeSubs;
        private bool booting, shellBeat;
        private float bootAge;

        // ------------------------------------------------------------------------------------------------ update

        /// <summary>
        /// Lamp states, the page overrides and the "just changed" rule, then what is drawn: the threat lamps right-aligned on the CM shelf (<see cref="LampLine.PlaceRight"/>), and which grid lamps light their fixed slots. None
        /// with the switch off; at Minimal only grid lamps at Caution or above; unlit ones only at Full (or while the spawn lamp test lights them all). A missing lamp leaves its slot empty: nothing slides.
        /// </summary>
        private void PresentLamps(BoardFrame frame)
        {
            int present = 0;
            for (int i = 0; i < lamps.Length; i++)
            {
                if (!lamps[i].Present) { lampChange.Forget(i); recent[i] = false; continue; }
                present++;
                OverrideLamp(ref lamps[i], false);
                recent[i] = lampChange.Recent(i, lamps[i].State, now);
            }
            // The threat lamps too (a strike landing close lights MSL as DNGR, a SAM envelope lights SPKE); a real threat keeps its own lamp. They are drawn only while the RWR page is not seated in a spine slot (C11):
            // seated, its scope and headline are their home.
            for (int i = 0; i < threatLamps.Length; i++) OverrideLamp(ref threatLamps[i], true);
            bootAge = frame.BootAge;
            booting = present > 0 && LampTest.Running(bootAge);

            bool showDark = allow.DarkLamps || booting;
            Array.Clear(groupCount, 0, Groups);
            if (allow.ShowLamps && present > 0)
            {
                for (int i = 0; i < threatLamps.Length && !slotOpen[ScopeLayout.SlotRwr]; i++)
                    if (HudLamps.Drawn(threatLamps[i].State, false, showDark)) AddLamp(GroupCm, ~i, threatLamps[i].Legend);
            }
            for (int i = 0; i < lamps.Length; i++)
                gridDrawn[i] = allow.ShowLamps && lamps[i].Present && HudLamps.Drawn(lamps[i].State, recent[i], showDark) && (!allow.ThreatLampsOnly || lamps[i].State >= LampState.Caution);
            cmLampsLeft = PlaceGroup(GroupCm, ScopeLayout.CmW, showDark);
        }

        private void AddLamp(int group, int item, string legend)
        {
            int k = groupCount[group];
            if (k >= GroupMax) return;
            groupItem[group * GroupMax + k] = item;
            groupWidth[group * GroupMax + k] = LampLine.Width(legend.Length);
            groupCount[group] = k + 1;
        }

        /// <summary>Right-align a shelf group against <paramref name="right"/> (its cell's width); returns its left edge.</summary>
        private float PlaceGroup(int group, float right, bool slots) =>
            LampLine.PlaceRight(new ReadOnlySpan<float>(groupWidth, group * GroupMax, groupCount[group]), right, slots, new Span<float>(groupX, group * GroupMax, GroupMax));

        /// <summary>The first page that overrides this lamp (by its legend) sets its state, flash and, if it has one, its word. A page the pilot has switched off, or the declutter level drops, does not: like its notices, its lamps are silent.</summary>
        private void OverrideLamp(ref Lamp lamp, bool onlyDark)
        {
            if (onlyDark && lamp.State != LampState.Dark) return;
            for (int p = 0; p < pages.Length; p++)
            {
                if (!CuePriority.Has(allow.Voiced, (ScopePage)p)) continue;
                LampState state;
                bool flash;
                string text;
                try { if (!pages[p].Lamp(lamp.Legend, out state, out flash, out text)) continue; }
                catch (Exception e) { ReportOnce(pages[p], e); continue; }
                lamp.State = state;
                lamp.Flash = flash;
                if (text != null) lamp.Legend = text;
                return;
            }
        }

        /// <summary>
        /// The live gauges (the page's own first, then the registered ones by rank), the widths of their words and how they pack into the gauge line (<see cref="GaugeRow.Lay"/>: one line or two, the sub-words kept while
        /// two lines hold them); none when the pilot's switch or the declutter level is off.
        /// </summary>
        private void PresentGauges()
        {
            // The gauge row's own switch (or the declutter level) is off: nothing is read for it and it takes no room.
            if (!allow.ShowGauges) { gaugeCount = gaugeLines = 0; return; }
            // The pages' own gauges first: every panel that draws its page, in slot order, then SYSTEMS (always on the board), up to the row's slots.
            int own = 0, reg = 0, sources = slotPages.Length;
            for (int s = 0; s <= sources && own < GaugeRow.Slots; s++)
            {
                if (s < sources && !SlotBody(s)) continue;
                ScopePageBase p = s < sources ? slotPages[s] : pages[(int)ScopePage.Systems];
                int n = 0;
                try { n = Math.Min(p.Gauges(pageScratch), GaugeRow.Slots); }
                catch (Exception e) { ReportOnce(p, e); }
                for (int k = 0; k < n && own < GaugeRow.Slots; k++) pageGauges[own++] = pageScratch[k];
            }
            for (int i = 0; i < ScopeGauges.Count && reg < registered.Length; i++)
                if (ScopeGauges.TryRead(i, out GaugeSpec spec) && !OwnedBySlot(spec.Id)) registered[reg++] = spec;
            gaugeCount = GaugeRow.Merge(pageGauges, own, registered, reg, shown);
            for (int i = 0; i < gaugeCount; i++)
            {
                gaugeLabel[i] = ScopeCanvas.TextW(shown[i].Label);
                gaugeValue[i] = ScopeCanvas.TextW(shown[i].Text);
                gaugeSub[i] = ScopeCanvas.TextW(shown[i].Sub);
            }
            gaugeLines = GaugeRow.Lay(gaugeLabel, gaugeValue, gaugeSub, gaugeCount, gaugeWidth, out gaugeSubs);
            GaugeRow.Pack(new ReadOnlySpan<float>(gaugeWidth, 0, gaugeCount), gaugeX, gaugeRow);
        }

        /// <summary>A registered gauge whose fact a drawn panel's body already shows (<see cref="ScopePageBase.OwnedGauge"/>): it stays off the gauge line.</summary>
        private bool OwnedBySlot(string id)
        {
            for (int s = 0; s < slotPages.Length; s++)
                if (SlotBody(s) && slotPages[s].OwnedGauge != null && slotPages[s].OwnedGauge == id) return true;
            return false;
        }

        // ------------------------------------------------------------------------------------------------ paint

        /// <summary>The shell surface (the whole box, board coordinates): each bar line's frame and its shelves' lamps, the mission rows and the gauge line.</summary>
        private void PaintWing(in ScopePalette pal, in BoardGeometry geo, bool flashOn)
        {
            ScopeCanvas g = shellCanvas;
            shellBeat = false;
            g.Furniture = allow.Furniture;
            g.Begin(pal, unit, Whole(geo.Height * unit), pixel, HudType.BoardBoost);
            if (g.Furniture)
            {
                PaintFrame(g, geo, geo.Bar0);
                PaintFrame(g, geo, geo.Bar1);
            }
            // The CM shelf keeps its slot without dispensers (the threat lamps have no other home); with the stores hidden it has none.
            BoardRect cm = geo.Cm.Present ? geo.Cm : allow.ShowStores ? new BoardRect(boardStack ? ScopeLayout.StackX + ScopeLayout.CmX : ScopeLayout.CmX, 0f, ScopeLayout.CmW, ScopeLayout.BarH) : default;
            int index = 0;      // the lamp test sweeps the CM shelf, then the grid's slots 0..7
            if (cm.Present)
            {
                if (g.Furniture && cmLampsLeft >= ScopeCanvas.TextW("CM") + 8f) g.Text("CM", cm.X, cm.Y + ScopeLayout.ShelfY, Dim, HudType.Label, ScopeAlign.Left, true);
                PaintShelf(g, cm, GroupCm, ref index, flashOn);
            }
            if (geo.Sys.Present)
            {
                BoardRect r = geo.Sys;
                if (g.Furniture) g.Text("SYS", r.X, r.Y + ScopeLayout.ShelfY, Dim, HudType.Label, ScopeAlign.Left, true);
                for (int i = 0; i < lamps.Length; i++)
                {
                    if (!gridDrawn[i]) continue;
                    HudLamps.Slot(i, r.W, out float x, out int row);
                    DrawLamp(g, r.X + x, r.Y + (row == 0 ? ScopeLayout.R2 : ScopeLayout.R3), LampLine.Width(lamps[i].Legend.Length), lamps[i], recent[i], index++, flashOn);
                }
            }
            if (geo.Mission.Present && hasMission) PaintMissionStatus(g, geo.Mission);
            if (geo.Gauges.Present) PaintGauges(g, geo.Gauges, flashOn);
            g.End();
            shellVector.Commit();
        }

        // ---- the bar's frame and lamps

        /// <summary>The bar's cells as <see cref="BoardGeometry.Block"/> numbers them, left to right on either line: WPN, CM, SYS, INFO.</summary>
        private static readonly int[] CellBlocks = { 0, 1, 2, 6 };

        /// <summary>
        /// One bar line's frame (furniture): the DIM glareshield hairline at <see cref="ScopeLayout.RuleY"/> from its first present cell to its last, a short tick up from it in the gap between two present cells, and corner
        /// brackets round the whole line.
        /// </summary>
        private static void PaintFrame(ScopeCanvas g, in BoardGeometry geo, in BoardRect bar)
        {
            if (!bar.Present) return;
            float left = float.NaN, right = 0f, y = bar.Y, rule = y + ScopeLayout.RuleY;
            for (int k = 0; k < CellBlocks.Length; k++)
            {
                BoardRect c = geo.Block(CellBlocks[k]);
                if (!c.Present || c.Y != y) continue;
                if (float.IsNaN(left)) left = c.X;
                else
                {
                    float tx = right + ScopeLayout.CellGap / 2f;
                    g.Line(tx, y + 2f, tx, y + ScopeLayout.BarH - 6f, Dim, 0.6f);      // a faint full-height divider: without it "ARMED" read into "486 kJ" in game
                }
                right = c.Right;
            }
            if (float.IsNaN(left)) return;
            right = bar.Right;      // the rule and the brackets run the whole bar line, over a missing cell too
            g.Line(left, rule, right, rule, Dim, 1f);
        }

        /// <summary>The CM shelf's threat lamps at their places (<see cref="PresentLamps"/>), on the shelf row of cell <paramref name="r"/>.</summary>
        private void PaintShelf(ScopeCanvas g, in BoardRect r, int group, ref int index, bool flashOn)
        {
            float cy = r.Y + ScopeLayout.ShelfY;
            for (int k = 0; k < groupCount[group]; k++)
            {
                int at = group * GroupMax + k;
                DrawLamp(g, r.X + groupX[at], cy, groupWidth[at], threatLamps[~groupItem[at]], false, index++, flashOn);
            }
        }

        /// <summary>
        /// One lamp: its word. Lit: role tone + solid bar beneath, bold only for caution/warning (HudType.Bold); dark: the word regular in DIM
        /// with no line (only drawn at Full). A warning or a lamp in transition dims with the beat. The spawn lamp test lights them in a sweep.
        /// </summary>
        private void DrawLamp(ScopeCanvas g, float x, float cy, float w, in Lamp lamp, bool changed, int index, bool flashOn)
        {
            bool lit, blink;
            ScopeTone tone = SystemsMath.ToneOf(lamp.State);
            if (booting) { lit = LampTest.Lit(index, bootAge); blink = false; tone = Ink; }
            else
            {
                lit = lamp.State != LampState.Dark || changed;
                blink = lamp.State != LampState.Dark && (lamp.Flash || lamp.State == LampState.Warning);
            }
            if (blink) shellBeat = true;
            bool dip = blink && !flashOn;
            if (lit)
            {
                if (tone == Ink) tone = Hi;
                g.Text(lamp.Legend, x, cy - 1f, tone, 12f, ScopeAlign.Left, true, dip ? 0.55f : 1f);
                g.Fill(x, cy + 6f, w, 2f, tone, dip ? 0.4f : 1f);      // the lit bar under the word
            }
            else
            {
                g.Text(lamp.Legend, x, cy - 1f, Faint);
            }
        }

        // ---- the gauge line

        /// <summary>
        /// The live gauges, packed from the left like <see cref="GaugeRow.Pack"/> counts them, each a glyph, its label (LABEL regular DIM), its value in bold (BRIGHT, or the tone of a warning, dipping on the flash) and, while
        /// the line has room, its sub-word in DIM. The glyph is 6 in radius 8 in from the gauge's left, the words from <see cref="GaugeRow.GlyphW"/>; a third line is never drawn.
        /// </summary>
        private void PaintGauges(ScopeCanvas g, in BoardRect r, bool flashOn)
        {
            for (int i = 0; i < gaugeCount; i++)
            {
                if (gaugeRow[i] > 1) break;
                ref readonly GaugeSpec s = ref shown[i];
                float x = r.X + gaugeX[i], cy = r.Y + ScopeLayout.GaugeLine1 / 2f + gaugeRow[i] * ScopeLayout.GaugePitch;
                bool warn = s.Tone == Amber || s.Tone == Red;
                if (s.Flash || s.Ready) shellBeat = true;
                float alpha = s.Flash && !flashOn ? 0.55f : 1f, vx = x + GaugeRow.GlyphW + (float)Math.Ceiling(gaugeLabel[i]) + GaugeRow.WordGap;
                GaugePainter.Glyph(g, s, x + 8f, cy, 6f, s.Ready && !flashOn ? 0.55f : 1f);
                g.Text(s.Label, x + GaugeRow.GlyphW, cy, Dim, HudType.Label);
                g.Text(s.Text, vx, cy, warn ? s.Tone : Hi, HudType.Label, ScopeAlign.Left, true, alpha);
                if (gaugeSubs && gaugeSub[i] > 0f) g.Text(s.Sub, vx + (float)Math.Ceiling(gaugeValue[i]) + GaugeRow.SubGap, cy, Dim);
            }
        }

    }
}
