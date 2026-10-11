using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The wing side of the board, minus the page (StatusPanel.Page.cs): the lamp line, the gauge line and the notice ticker, each in its ScopeLayout rectangle on the shell surface (box coordinates). Their data
    // is read here too (PresentLamps, PresentGauges, TickerWords). Nothing here touches the stores side. The lamps and gauges are drawn as words alone for now, to fit the 12 unit lines (the wing task gives them
    // their markers and glyphs).
    internal sealed partial class StatusPanel
    {
        private readonly LampChange lampChange = new LampChange();
        private readonly Lamp[] lamps = new Lamp[HudLamps.Count];
        private readonly Lamp[] threatLamps = new Lamp[HudThreats.Count];
        private readonly bool[] recent = new bool[HudLamps.Count];
        private readonly int[] lampOrder = new int[HudLamps.Count];
        private readonly GaugeSpec[] pageGauges = new GaugeSpec[GaugeRow.Slots], registered = new GaugeSpec[16], shown = new GaugeSpec[GaugeRow.Slots];
        private readonly float[] gaugeWidth = new float[GaugeRow.Slots];
        private int lampCount, gaugeCount, gaugeLines;
        /// <summary>The lamp row has a lamp to draw (<see cref="lampOrder"/> for the system lamps, a bit per threat lamp in <see cref="threatMask"/>).</summary>
        private bool lampRow;
        private int threatMask;
        private bool booting, shellBeat;
        private float bootAge;
        private string tickerWords, tickerSourceText;
        private int tickerSourceCount, tickerSourcePhase;
        private HudTone tickerSourceTone;

        /// <summary>The lamp slots (the old lamp cells' widths): the system lamps from the left, the threat lamps against the right end.</summary>
        private const float LampW = 40f, ThreatLampW = 44f, MissileLampW = 46f, LampGap = 2f;

        // ------------------------------------------------------------------------------------------------ update

        /// <summary>
        /// Lamp states, the page overrides (an engine out lights ENG whatever page shows) and the "just changed" rule, then which lamps the strip draws: none with its switch off; at
        /// Minimal only the threat lamps; unlit ones only at Full (or while the spawn lamp test lights them all); what is drawn is packed with no gap, the system lamps from the left and the threat lamps against the right end. A strip with nothing to draw takes no room.
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

        /// <summary>The live gauges (the page's own first, then the registered ones by rank) and how many lines of the wing they pack into (<see cref="ScopeLayout.GaugeLines"/>); none when the pilot's switch or the declutter level is off.</summary>
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
            for (int i = 0; i < gaugeCount; i++) gaugeWidth[i] = ScopeCanvas.TextW(shown[i].Label) + GaugeWordGap + ScopeCanvas.TextW(shown[i].Text);
            gaugeLines = ScopeLayout.GaugeLines(new ReadOnlySpan<float>(gaugeWidth, 0, gaugeCount));
        }

        /// <summary>The ticker's words, rebuilt only when the notice, its count, its phase or its tone changed.</summary>
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
                // The words carry the repeat count ("x3", from NoticeTicker); a warning says so in words, not only in red.
                tickerWords = (line.Tone == HudTone.Warning ? "WARNING · " : "") + NoticeTicker.Words(line).ToUpperInvariant();
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

        private void PaintLamps(ScopeCanvas g, in BoardRect r, bool flashOn)
        {
            int total = lampCount + HudThreats.Count;
            for (int k = 0; k < lampCount; k++)
            {
                int i = lampOrder[k];
                DrawLamp(g, r.X + k * (LampW + LampGap), r.Y, LampW, lamps[i], recent[i], k, total, flashOn);
            }
            // The threat lamps pack against the right end, in order: one that is not drawn leaves no gap.
            float tx = r.Right;
            for (int i = HudThreats.Count - 1; i >= 0; i--)
            {
                if ((threatMask & (1 << i)) == 0) continue;
                float w = i == 2 ? MissileLampW : ThreatLampW;
                tx -= w;
                DrawLamp(g, tx, r.Y, w, threatLamps[i], false, lampCount + i, total, flashOn);
                tx -= LampGap;
            }
        }

        /// <summary>One lamp: its legend in its slot, lit (bold, in its tone) or dark (DIM); a warning or a lamp in transition dims with the beat. The spawn lamp test lights them in a sweep.</summary>
        private void DrawLamp(ScopeCanvas g, float x, float y, float w, in Lamp lamp, bool changed, int index, int total, bool flashOn)
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
            g.Text(lamp.Legend, x + w / 2f, y + ScopeLayout.LampH / 2f, lit ? (tone == Ink ? Hi : tone) : Dim, 12f, ScopeAlign.Center, lit, blink && !flashOn ? 0.5f : 1f);
        }

        // ---- the gauge line

        /// <summary>Between a gauge's label and its value.</summary>
        private const float GaugeWordGap = 6f;

        /// <summary>The live gauges, packed from the left like <see cref="ScopeLayout.GaugeLines"/> counts them: the label (DIM, or in the tone of a warning gauge) and the value in bold.</summary>
        private void PaintGauges(ScopeCanvas g, in BoardRect r, bool flashOn)
        {
            float x = 0f, y = r.Y + ScopeLayout.GaugeLine1 / 2f;
            for (int i = 0; i < gaugeCount; i++)
            {
                ref readonly GaugeSpec s = ref shown[i];
                if (x > 0f && x + ScopeLayout.GaugeSpace + gaugeWidth[i] > r.W) { x = 0f; y += ScopeLayout.GaugePitch; }
                if (x > 0f) x += ScopeLayout.GaugeSpace;
                bool warn = s.Tone == Amber || s.Tone == Red;
                if (s.Flash || s.Ready) shellBeat = true;
                float alpha = s.Flash && !flashOn ? 0.5f : 1f, labelW = ScopeCanvas.TextW(s.Label);
                g.Text(s.Label, r.X + x, y, warn ? s.Tone : Dim, 12f, ScopeAlign.Left, warn, alpha);
                g.Text(s.Text, r.X + x + labelW + GaugeWordGap, y, warn ? s.Tone : Hi, 12f, ScopeAlign.Left, true, alpha);
                x += gaugeWidth[i];
            }
        }

        // ---- the ticker

        /// <summary>The notice: a tone tick, then its words in bold in the same tone (the count is in the words). A sticky warning pulses.</summary>
        private void PaintTicker(ScopeCanvas g, in BoardRect r, bool flashOn)
        {
            TickerLine line = board.Ticker;
            ScopeTone tone = SystemsMath.ToneOf(line.Tone);
            bool sticky = line.Tone == HudTone.Warning;
            if (sticky || NoticeTicker.IsFresh(line)) shellBeat = true;
            bool dip = sticky && !flashOn;
            g.ToneTick(r.X, r.Y, tone, dip ? 0.55f : 1f);
            g.FitText(TickerWords(line), r.X + 10f, r.Y + r.H / 2f, r.W - 10f, tone, 12f, 12f, ScopeAlign.Left, true, dip ? 0.8f : 1f);
        }
    }
}
