using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The board's update (data at 10 Hz, motion every frame) and painters: lamp strip, scope (page body, header and strip overlay, frame and
    // wipe), gauge row and ticker. Geometry is ScopeLayout's; nothing here computes a row's y by hand.
    internal sealed partial class StatusPanel
    {
        private const float FlashHz = 3f;
        /// <summary>The lower-left plate cut passes through the leftmost box of the last row: that box is chamfered by this much.</summary>
        private const float BoxCut = ScopeLayout.Chamfer - ScopeLayout.Pad;

        private enum Row : byte { None, Lamps, Scope, Gauges, Ticker }

        private ScopePageBase[] pages;
        private readonly ScopeSelector selector = new ScopeSelector();
        private readonly ScopeTransition transition = new ScopeTransition();
        private readonly LampChange lampChange = new LampChange();
        private ScopePageBase page;
        private readonly Lamp[] lamps = new Lamp[HudLamps.Count];
        private readonly Lamp[] threatLamps = new Lamp[HudThreats.Count];
        private readonly bool[] recent = new bool[HudLamps.Count];
        private readonly int[] lampOrder = new int[HudLamps.Count];
        private readonly GaugeSpec[] pageGauges = new GaugeSpec[GaugeRow.Slots], registered = new GaugeSpec[8], shown = new GaugeSpec[GaugeRow.Slots];
        private readonly System.Collections.Generic.HashSet<int> failed = new System.Collections.Generic.HashSet<int>();
        private BoardFrame board;
        private int lampCount, gaugeCount, lastPhase = -1;
        private bool booting, shellBeat, scopeBeat, hasBoard;

        /// <summary>Something on the board blinks, so its flash phase is part of what it draws.</summary>
        private bool beat => shellBeat || scopeBeat;
        private float now, bootAge, contentRight, contentBottom;
        private AvUnits units;
        private string tickerWords, tickerSourceText;
        private int tickerSourceCount, tickerSourcePhase;
        private HudTone tickerSourceTone;

        private void BuildBoard()
        {
            pages = ScopePages.Create();
            page = pages[(int)ScopePage.Systems];
            selector.Reset();
            transition.Reset();
            lampChange.Reset();
            lastPhase = -1;
            hasBoard = false;
        }

        private void DestroyBoard()
        {
            pages = null;
            page = null;
            hasBoard = false;
            tickerWords = tickerSourceText = null;
        }

        // ------------------------------------------------------------------------------------------------ update

        /// <summary>The player's HUD theme as the scope's tokens: ink, caution and warning colours, the rest derived.</summary>
        private static ScopePalette Palette()
        {
            ColorTheme theme = ThemeManager.Active.ColorTheme;
            return new ScopePalette(ToRgba(theme.AllClear), ToRgba(theme.Warning), ToRgba(theme.Alert));
        }

        private static Rgba ToRgba(Color c) => new Rgba(c.r, c.g, c.b, 1f);

        /// <summary>
        /// The 10 Hz refresh: lamps, every page's data (<see cref="ScopePageBase.Poll"/>), the page selection, the gauge row and the ticker, then a
        /// full repaint. <see cref="Animate"/> carries it between ticks.
        /// </summary>
        public void Present(BoardFrame frame)
        {
            if (root == null) return;
            board = frame;
            now = frame.Now;
            units = frame.Units;

            SystemStates systems = frame.Systems;
            if (nativeSafety != null) systems.WeaponSafe = nativeSafety.enabled;
            HudLamps.Fill(systems, lamps);
            HudThreats.Fill(frame.Threat, threatLamps);

            var env = new ScopeEnv(now, frame.Units, Notice);
            ushort live = 0;
            for (int i = 0; i < pages.Length; i++)
                if (PollPage(pages[i], env)) live |= CuePriority.Bit((ScopePage)i);
            selector.Update(live, now);
            page = pages[(int)selector.Page];

            PresentLamps(frame);
            PresentGauges();
            hasBoard = true;
            ContentBounds(out _, out contentRight, out contentBottom);
            Repaint(true);
        }

        private bool PollPage(ScopePageBase p, in ScopeEnv env)
        {
            try { return p.Poll(env); }
            catch (Exception e) { ReportOnce(p, e); return false; }
        }

        private void ReportOnce(ScopePageBase p, Exception e)
        {
            if (failed.Add((int)p.Id)) BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.Scope." + p.Title, e);
        }

        /// <summary>Lamp states, the page overrides (an engine out lights ENG whatever page shows) and the "just changed" rule.</summary>
        private void PresentLamps(BoardFrame frame)
        {
            lampCount = 0;
            for (int i = 0; i < lamps.Length; i++)
            {
                if (!lamps[i].Present) { lampChange.Forget(i); recent[i] = false; continue; }
                for (int p = 0; p < pages.Length; p++)
                    if (pages[p].Lamp(lamps[i].Legend, out LampState state, out bool flash)) { lamps[i].State = state; lamps[i].Flash = flash; break; }
                recent[i] = lampChange.Recent(i, lamps[i].State, now);
                lampOrder[lampCount++] = i;
            }
            bootAge = frame.BootAge;
            booting = lampCount > 0 && LampTest.Running(bootAge);
        }

        private void PresentGauges()
        {
            int own = Math.Min(page.Gauges(pageGauges), GaugeRow.Slots), reg = 0;
            for (int i = 0; i < ScopeGauges.Count && reg < registered.Length; i++)
                if (ScopeGauges.TryRead(i, out GaugeSpec spec)) registered[reg++] = spec;
            gaugeCount = GaugeRow.Merge(pageGauges, own, registered, reg, shown);
        }

        /// <summary>Every frame: carry the wipe and the flash beat, and repaint a page that moves.</summary>
        public void Animate(float clock)
        {
            if (root == null || !hasBoard || !root.gameObject.activeSelf) return;
            now = clock;
            int phase = (int)(clock * FlashHz * 2f);
            bool flashChanged = beat && phase != lastPhase;
            if (!flashChanged && !transition.Running && !page.Fast) return;
            Repaint(flashChanged || transition.Running);
        }

        // ------------------------------------------------------------------------------------------------ layout and paint

        private void Repaint(bool shellToo)
        {
            bool compact = selector.Compact;
            bool strip = !compact && page.HasStrip;
            float target = page.Ready ? (compact ? ScopeLayout.Compact : ScopeLayout.Full) : 0f;
            transition.Update((int)selector.Page * 2 + (compact ? 1 : 0), target, now);
            var rows = new BoardRows { Lamps = lampCount > 0, ScopeH = transition.Height, Gauges = gaugeCount > 0, Ticker = board.HasTicker };
            BoardGeometry geo = ScopeLayout.Compute(rows);
            if (geo.Height <= 0f)
            {
                Show(root, false);
                return;
            }
            Show(root, true);

            Vector2 rootPosition = new Vector2(contentRight - dock.rect.xMin, contentBottom - dock.rect.yMin);
            if (root.anchoredPosition != rootPosition) root.anchoredPosition = rootPosition;
            Vector2 rootSize = new Vector2(geo.Width * unit, geo.Height * unit);
            if (root.sizeDelta != rootSize) root.sizeDelta = rootSize;

            ScopePalette pal = Palette();
            lastPhase = (int)(now * FlashHz * 2f);
            bool flashOn = (int)(now * FlashHz) % 2 == 0;
            Row last = geo.Ticker.H > 0f ? Row.Ticker : geo.Gauges.H > 0f ? Row.Gauges : geo.Scope.H > 0f ? Row.Scope : geo.Lamps.H > 0f ? Row.Lamps : Row.None;
            if (geo.Scope.H > 0f) PaintScope(pal, geo, compact, strip, flashOn, last == Row.Scope);
            else { Show(scopeRect, false); scopeBeat = false; }
            if (shellToo) PaintShell(pal, geo, flashOn, last);
        }

        private void PaintShell(in ScopePalette pal, in BoardGeometry geo, bool flashOn, Row last)
        {
            ScopeCanvas g = shellCanvas;
            shellBeat = false;
            g.Begin(pal, unit, geo.Height * unit);
            g.Line(0.5f, 0.5f, geo.Width - 0.5f, 0.5f, Ink, 0.25f);
            if (geo.Lamps.H > 0f) PaintLamps(g, geo.Lamps.Y, flashOn, last == Row.Lamps);
            if (geo.Gauges.H > 0f)
                for (int i = 0; i < gaugeCount; i++)
                {
                    if (shown[i].Flash || shown[i].Ready) shellBeat = true;
                    GaugePainter.Draw(g, shown[i], ScopeLayout.GaugeX(i), geo.Gauges.Y, ScopeLayout.GaugeW, ScopeLayout.GaugeH, flashOn, now, i == 0 && last == Row.Gauges ? BoxCut : 0f);
                }
            if (geo.Ticker.H > 0f) PaintTicker(g, geo.Ticker.Y, flashOn, last == Row.Ticker);
            g.End();
            shellVector.Commit();
        }

        // ------------------------------------------------------------------------------------------------ lamp strip

        private void PaintLamps(ScopeCanvas g, float y, bool flashOn, bool cut)
        {
            int total = lampCount + HudThreats.Count;
            for (int k = 0; k < lampCount; k++)
            {
                int i = lampOrder[k];
                DrawLamp(g, ScopeLayout.SystemLampX(k), y, ScopeLayout.LampW, lamps[i], recent[i], k, total, flashOn, k == 0 && cut);
            }
            for (int i = 0; i < HudThreats.Count; i++)
                DrawLamp(g, ScopeLayout.ThreatLampX(i), y, ScopeLayout.ThreatLampWidth(i), threatLamps[i], false, lampCount + i, total, flashOn, false);
        }

        /// <summary>
        /// One lamp: a dim outline when dark, a lit box in its tone when on, caution or warning, or just changed. A warning or a lamp in
        /// transition pulses its wash with the beat; the legend is bold when lit. The spawn lamp test lights them in a sweep.
        /// </summary>
        private void DrawLamp(ScopeCanvas g, float x, float y, float w, in Lamp lamp, bool changed, int index, int total, bool flashOn, bool cut)
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
            bool wash = blink && flashOn;
            g.Box(x, y + 1f, w, ScopeLayout.LampH - 2f, lit ? tone : Dim, lit ? 1f : 0.55f, 1f, tone, lit ? (wash ? 0.5f : 0.24f) : 0f, chamBL: cut ? BoxCut : 0f);
            g.Text(lamp.Legend, x + w / 2f, y + ScopeLayout.LampH / 2f + 1f, lit ? (tone == Ink ? Hi : tone) : Dim, 12f, ScopeAlign.Center, lit, lit ? 1f : 0.75f);
        }

        // ------------------------------------------------------------------------------------------------ ticker

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

        private void PaintTicker(ScopeCanvas g, float y, bool flashOn, bool cut)
        {
            TickerLine line = board.Ticker;
            ScopeTone tone = SystemsMath.ToneOf(line.Tone);
            bool hot = tone != Ink;
            g.Box(ScopeLayout.Pad, y, ScopeLayout.Inner, ScopeLayout.TickerH, Faint, 0.9f, chamBL: cut ? BoxCut : 0f);
            string chip = NoticeTicker.Tag(line.Channel);
            float cw = (float)Math.Ceiling(ScopeCanvas.TextW(chip, 12f)) + 10f, h = ScopeLayout.TickerH;
            if (hot)
            {
                g.Fill(ScopeLayout.Pad + 3f, y + 3f, cw, h - 6f, tone);
                g.Text(chip, ScopeLayout.Pad + 3f + cw / 2f, y + h / 2f + 1f, Bg, 12f, ScopeAlign.Center, true);
            }
            else
            {
                g.Box(ScopeLayout.Pad + 3f, y + 3f, cw, h - 6f, Dim);
                g.Text(chip, ScopeLayout.Pad + 3f + cw / 2f, y + h / 2f + 1f, Dim, 12f, ScopeAlign.Center, true);
            }
            bool sticky = line.Tone == HudTone.Warning;
            if (sticky || NoticeTicker.IsFresh(line)) shellBeat = true;
            float tx = ScopeLayout.Pad + 3f + cw + 8f;
            g.FitText(TickerWords(line), tx, y + h / 2f + 1f, ScopeLayout.Pad + ScopeLayout.Inner - tx - 6f, tone, 12f, 12f, ScopeAlign.Left, hot, sticky && !flashOn ? 0.7f : 1f);
        }

        // ------------------------------------------------------------------------------------------------ scope

        private void PaintScope(in ScopePalette pal, in BoardGeometry geo, bool compact, bool strip, bool flashOn, bool cut)
        {
            float hS = geo.Scope.H, body = ScopeLayout.Body(compact, strip), w = ScopeLayout.Inner;
            Show(scopeRect, true);
            scopeBeat = false;
            Place(scopeRect, ScopeLayout.Pad * unit, geo.Scope.Y * unit, w * unit, hS * unit);
            Place(pageRect, 0f, 0f, w * unit, body * unit);
            Place(overlayRect, 0f, 0f, w * unit, hS * unit);
            Place(frameRect, 0f, 0f, w * unit, hS * unit);

            // The page body: the glass, its grid, then the page.
            ScopeCanvas g = pageCanvas;
            g.Begin(pal, unit, body * unit);
            g.Fill(0f, 0f, w, body, Bg);
            g.Grid(w, body);
            try { page.Paint(g, new PagePaint(w, body, compact, ScopeLayout.SafeTop(compact), now, units)); }
            catch (Exception e)
            {
                ReportOnce(page, e);
                g.Text("PAGE ERROR", 8f, 60f, Red);
            }
            g.End();
            pageVector.Commit();

            // The header band (tab, pips, headline) and the page strip, above the page.
            g = overlayCanvas;
            g.Begin(pal, unit, hS * unit);
            PaintHeader(g, w, compact, flashOn);
            if (strip)
            {
                g.Origin(0f, body);
                g.Fill(0f, 0f, w, ScopeLayout.Strip, Bg, 0.9f);
                g.Line(0f, 0f, w, 0f, Faint);
                try { page.PaintStrip(g, new PagePaint(w, ScopeLayout.Strip, false, 0f, now, units)); }
                catch (Exception e) { ReportOnce(page, e); g.Text("STRIP ERROR", 8f, 13f, Red); }
                g.Origin(0f, 0f);
            }
            g.End();
            overlayVector.Commit();

            // The wipe edge (the new page is revealed left to right behind it) and the frame.
            g = frameCanvas;
            g.Begin(pal, unit, hS * unit);
            if (transition.Wipe < 1f)
            {
                float wx = Mathf.Round(transition.Wipe * w);
                g.Fill(wx, 0f, w - wx, hS, Bg);
                g.Fill(wx - 3f, 0f, 3f, hS, Hi, 0.35f);
                g.Fill(wx, 0f, 1f, hS, Hi);
            }
            g.Box(0f, 0f, w, hS, Dim, 0.75f, chamBL: cut ? BoxCut : 0f);
            g.Line(0.5f, 0.5f, 7.5f, 0.5f, Ink); g.Line(0.5f, 0.5f, 0.5f, 7.5f, Ink);
            g.Line(w - 0.5f, 0.5f, w - 7.5f, 0.5f, Ink); g.Line(w - 0.5f, 0.5f, w - 0.5f, 7.5f, Ink);
            g.Line(w - 0.5f, hS - 0.5f, w - 7.5f, hS - 0.5f, Ink); g.Line(w - 0.5f, hS - 0.5f, w - 0.5f, hS - 7.5f, Ink);
            if (!cut) { g.Line(0.5f, hS - 0.5f, 7.5f, hS - 0.5f, Ink); g.Line(0.5f, hS - 0.5f, 0.5f, hS - 7.5f, Ink); }
            g.End();
            frameVector.Commit();
        }

        /// <summary>The scope's header band: the page's tab, a pip for every other live page (in that page's tone), and the headline figure.</summary>
        private void PaintHeader(ScopeCanvas g, float w, bool compact, bool flashOn)
        {
            float hh = compact ? ScopeLayout.HeadCompact : ScopeLayout.Head, th = hh - 6f;
            g.Fill(0f, 0f, w, hh, Bg, 0.9f);
            g.Line(0f, hh, w, hh, Faint);
            string title = page.Title;
            float tw = (float)Math.Ceiling(ScopeCanvas.TextW(title, 12f)) + 16f;
            g.Box(4f, 3f, tw, th, Ink, 1f, 1f, Ink, 0.2f, chamTL: 5f);
            g.Text(title, 4f + tw / 2f, 3f + th / 2f + 1f, Hi, 12f, ScopeAlign.Center, true);

            float x = 4f + tw + 8f;
            int pips = 0;
            Span<char> code = stackalloc char[1];
            for (int i = 0; i < pages.Length && pips < 5; i++)
            {
                if (!CuePriority.Has(selector.Others, (ScopePage)i)) continue;
                ScopeHeadline other = pages[i].Headline();
                ScopeTone tone = other.Tone == Red ? Red : other.Tone == Amber ? Amber : Ink;
                float py = Mathf.Round((hh - 16f) / 2f);
                g.Box(x, py, 16f, 16f, tone, 1f, 1f, tone, 0.16f);
                code[0] = pages[i].Code;
                g.Text(code, x + 8f, py + 9f, tone, 12f, ScopeAlign.Center, true);
                x += 19f;
                pips++;
            }

            ScopeHeadline hl = page.Headline();
            if (string.IsNullOrEmpty(hl.Text)) return;
            float size = compact ? 14f : 16f, hw = (float)Math.Ceiling(ScopeCanvas.TextW(hl.Text, size)) + 12f, hx = w - 4f - hw;
            if (hl.Tone == Amber || hl.Tone == Red)
            {
                if (hl.Flash) scopeBeat = true;
                bool pulse = hl.Flash && flashOn;
                g.Box(hx, 3f, hw, th, hl.Tone, 1f, 1f, hl.Tone, pulse ? 0.55f : 0.2f);
                g.Text(hl.Text, hx + hw / 2f, 3f + th / 2f + 1f, pulse ? Bg : hl.Tone, size, ScopeAlign.Center, true);
            }
            else g.Text(hl.Text, w - 8f, 3f + th / 2f + 1f, hl.Tone, size, ScopeAlign.Right, true, hl.Tone == Dim ? 0.8f : 1f);
        }
    }
}
