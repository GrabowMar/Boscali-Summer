using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The wing side of the board, minus the page (StatusPanel.Page.cs): the lamp line, the gauge line and the notice ticker (LAYOUT.md 3.4, 3.6, 3.7), each in its ScopeLayout rectangle on the shell surface (box
    // coordinates). Their data is read here too (PresentLamps, PresentGauges, TickerWords). Nothing here touches the stores side. Ordinary ink only: a marker square and a word per lamp, a glyph, a label and a value
    // per gauge, a tone tick and bold words for the notice.
    internal sealed partial class StatusPanel
    {
        private readonly LampChange lampChange = new LampChange();
        private readonly Lamp[] lamps = new Lamp[HudLamps.Count];
        private readonly Lamp[] threatLamps = new Lamp[HudThreats.Count];
        private readonly bool[] recent = new bool[HudLamps.Count];
        private readonly int[] lampOrder = new int[HudLamps.Count];
        private readonly float[] lampWidth = new float[HudLamps.Count], lampLeft = new float[HudLamps.Count], threatWidth = new float[HudThreats.Count], threatLeft = new float[HudThreats.Count];
        private readonly int[] threatOrder = new int[HudThreats.Count];
        private readonly GaugeSpec[] pageGauges = new GaugeSpec[GaugeRow.Slots], registered = new GaugeSpec[16], shown = new GaugeSpec[GaugeRow.Slots];
        private readonly float[] gaugeLabel = new float[GaugeRow.Slots], gaugeValue = new float[GaugeRow.Slots], gaugeSub = new float[GaugeRow.Slots], gaugeWidth = new float[GaugeRow.Slots], gaugeX = new float[GaugeRow.Slots];
        private readonly int[] gaugeRow = new int[GaugeRow.Slots];
        private int lampCount, gaugeCount, gaugeLines;
        /// <summary>The gauges keep their sub-words (they do while two lines hold the gauges with them: <see cref="GaugeRow.Lay"/>).</summary>
        private bool gaugeSubs;
        /// <summary>The lamp row has a lamp to draw (<see cref="lampOrder"/> for the system lamps, a bit per threat lamp in <see cref="threatMask"/>).</summary>
        private bool lampRow;
        private int threatMask;
        private bool booting, shellBeat;
        private float bootAge;
        private string tickerWords, tickerCount, tickerSourceText;
        private int tickerSourceCount, tickerSourcePhase;
        private HudTone tickerSourceTone;

        // ------------------------------------------------------------------------------------------------ update

        /// <summary>
        /// Lamp states, the page overrides (an engine out lights ENG whatever page shows) and the "just changed" rule, then which lamps the line draws: none with its switch off; at
        /// Minimal only the threat lamps; unlit ones only at Full (or while the spawn lamp test lights them all). A line with nothing to draw takes no room.
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
            // The threat lamps too (a strike landing close lights MSL as DNGR, a SAM envelope lights SPKE); a real threat keeps its own lamp.
            for (int i = 0; i < threatLamps.Length; i++) OverrideLamp(ref threatLamps[i], true);
            bootAge = frame.BootAge;
            booting = present > 0 && LampTest.Running(bootAge);

            bool showDark = allow.DarkLamps || booting;
            lampCount = threatMask = 0;
            if (allow.ShowLamps && present > 0)
            {
                if (!allow.ThreatLampsOnly)
                    for (int i = 0; i < lamps.Length; i++)
                        if (lamps[i].Present && HudLamps.Drawn(lamps[i].State, recent[i], showDark)) lampOrder[lampCount++] = i;
                for (int i = 0; i < threatLamps.Length; i++)
                    if (HudLamps.Drawn(threatLamps[i].State, false, showDark)) threatMask |= 1 << i;
            }
            lampRow = lampCount > 0 || threatMask != 0;
        }

        /// <summary>The first page that overrides this lamp (by its legend) sets its state, flash and, if it has one, its word.</summary>
        private void OverrideLamp(ref Lamp lamp, bool onlyDark)
        {
            if (onlyDark && lamp.State != LampState.Dark) return;
            for (int p = 0; p < pages.Length; p++)
            {
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
            int own = 0, reg = 0;
            try { own = Math.Min(page.Gauges(pageGauges), GaugeRow.Slots); }
            catch (Exception e) { ReportOnce(page, e); }
            for (int i = 0; i < ScopeGauges.Count && reg < registered.Length; i++)
                if (ScopeGauges.TryRead(i, out GaugeSpec spec)) registered[reg++] = spec;
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

        /// <summary>The ticker's words and its repeat count, rebuilt only when the notice, its count, its phase or its tone changed.</summary>
        private string TickerWords(in TickerLine line)
        {
            int phase = NoticeTicker.Phase(line);
            if (tickerWords == null || !ReferenceEquals(tickerSourceText, line.Text) || tickerSourceCount != line.Count ||
                tickerSourcePhase != phase || tickerSourceTone != line.Tone)
            {
                tickerSourceText = line.Text;
                tickerSourceCount = line.Count;
                tickerSourcePhase = phase;
                tickerSourceTone = line.Tone;
                // A warning says so in words, not only in red.
                tickerWords = (line.Tone == HudTone.Warning ? "WARNING · " : "") + NoticeTicker.Words(line).ToUpperInvariant();
                tickerCount = NoticeTicker.CountWord(line);
            }
            return tickerWords;
        }

        // ------------------------------------------------------------------------------------------------ paint

        /// <summary>The wing's shell surface: the lamp line, the gauge line and the ticker, in board coordinates (the surface is the whole box).</summary>
        private void PaintWing(in ScopePalette pal, in BoardGeometry geo, bool flashOn)
        {
            ScopeCanvas g = shellCanvas;
            shellBeat = false;
            g.Furniture = allow.Furniture;
            g.Begin(pal, unit, Whole(geo.Height * unit), pixel);
            if (geo.Lamps.Present) PaintLamps(g, geo.Lamps, flashOn);
            if (geo.Gauges.Present) PaintGauges(g, geo.Gauges, flashOn);
            if (geo.Ticker.Present) PaintTicker(g, geo.Ticker, flashOn);
            g.End();
            shellVector.Commit();
        }

        // ---- the lamp line

        /// <summary>The system lamps from the left end, the threat lamps against the right end (<see cref="LampLine.Place"/>): one that is not drawn leaves no slot at Lean and Minimal, and a slot at Full.</summary>
        private void PaintLamps(ScopeCanvas g, in BoardRect r, bool flashOn)
        {
            int total = lampCount + HudThreats.Count, threats = 0;
            for (int k = 0; k < lampCount; k++) lampWidth[k] = LampLine.Width(lamps[lampOrder[k]].Legend.Length);
            for (int i = 0; i < HudThreats.Count; i++)
                if ((threatMask & (1 << i)) != 0) { threatOrder[threats] = i; threatWidth[threats++] = LampLine.Width(threatLamps[i].Legend.Length); }
            LampLine.Place(new ReadOnlySpan<float>(lampWidth, 0, lampCount), new ReadOnlySpan<float>(threatWidth, 0, threats), allow.DarkLamps || booting, r.W, lampLeft, threatLeft);
            for (int k = 0; k < lampCount; k++)
            {
                int i = lampOrder[k];
                DrawLamp(g, r.X + lampLeft[k], r.Y + ScopeLayout.LampH / 2f, lamps[i], recent[i], k, total, flashOn);
            }
            for (int k = 0; k < threats; k++)
            {
                int i = threatOrder[k];
                DrawLamp(g, r.X + threatLeft[k], r.Y + ScopeLayout.LampH / 2f, threatLamps[i], false, lampCount + i, total, flashOn);
            }
        }

        /// <summary>
        /// One lamp: a 4 unit square and its word. Lit, the square is filled and the word bold, in the lamp's tone (BRIGHT for a mode that is on, amber for a caution, red for a warning); dark, the square is a hollow DIM
        /// and the word regular DIM. A warning or a lamp in transition dims with the beat. The spawn lamp test lights them in a sweep.
        /// </summary>
        private void DrawLamp(ScopeCanvas g, float x, float cy, in Lamp lamp, bool changed, int index, int total, bool flashOn)
        {
            bool lit, blink;
            ScopeTone tone = SystemsMath.ToneOf(lamp.State);
            if (booting) { lit = LampTest.Lit(index, total, bootAge); blink = false; tone = Ink; }
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
                g.Marker(x, cy, 4f, tone, true, dip ? 0.45f : 1f);
                g.Text(lamp.Legend, x + LampLine.Marker, cy, tone, 12f, ScopeAlign.Left, true, dip ? 0.55f : 1f);
            }
            else
            {
                g.Marker(x, cy, 4f, Dim, false);
                g.Text(lamp.Legend, x + LampLine.Marker, cy, Dim);
            }
        }

        // ---- the gauge line

        /// <summary>
        /// The live gauges, packed from the left like <see cref="GaugeRow.Pack"/> counts them, each a glyph, its label (DIM, or in the tone of a warning gauge), its value in bold (BRIGHT, or the tone of a warning) and, while
        /// the line has room, its sub-word in INK. The glyph is 6 in radius about 7 in from the gauge's left; a third line is never drawn.
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
                float alpha = s.Flash && !flashOn ? 0.5f : 1f, vx = x + GaugeRow.GlyphW + (float)Math.Ceiling(gaugeLabel[i]) + GaugeRow.WordGap;
                GaugePainter.Glyph(g, s, x + 7f, cy, 6f, s.Ready && !flashOn ? 0.55f : 1f);
                g.Text(s.Label, x + GaugeRow.GlyphW, cy, warn ? s.Tone : Dim, 12f, ScopeAlign.Left, warn);
                g.Text(s.Text, vx, cy, warn ? s.Tone : Hi, 12f, ScopeAlign.Left, true, alpha);
                if (gaugeSubs && gaugeSub[i] > 0f) g.Text(s.Sub, vx + (float)Math.Ceiling(gaugeValue[i]) + GaugeRow.SubGap, cy, Ink);
            }
        }

        // ---- the ticker

        /// <summary>The notice: a tone tick, then its words in bold in the same tone, and the repeat count in bold at the right end. A sticky warning pulses.</summary>
        private void PaintTicker(ScopeCanvas g, in BoardRect r, bool flashOn)
        {
            TickerLine line = board.Ticker;
            ScopeTone tone = SystemsMath.ToneOf(line.Tone);
            bool sticky = line.Tone == HudTone.Warning;
            if (sticky || NoticeTicker.IsFresh(line)) shellBeat = true;
            bool dip = sticky && !flashOn;
            float cy = r.Y + r.H / 2f;
            string words = TickerWords(line);
            float room = r.W - 10f;
            g.ToneTick(r.X, r.Y, tone, dip ? 0.55f : 1f);
            if (tickerCount != null)
            {
                g.Text(tickerCount, r.Right, cy, tone, 12f, ScopeAlign.Right, true);
                room -= ScopeCanvas.TextW(tickerCount) + 10f;
            }
            g.FitText(words, r.X + 10f, cy, room, tone, 12f, 12f, ScopeAlign.Left, true, dip ? 0.8f : 1f);
        }
    }
}
