using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Core.Contracts;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The notice ticker (2026-10-10): one full-width line at the foot of the board for comms, SUPEREVENT, session, weather and
    // contract notices, which the four feed cells truncated to a few letters. A tag tile names the channel, the words follow;
    // a Warning is prefixed WARNING and stays up twenty seconds; a notice with a detail alternates text and detail; a repeat
    // reads "x3". The rules are NoticeTicker's.
    internal sealed partial class StatusPanel
    {
        private TextMeshProUGUI tickerTag, tickerText;
        private string tickerWords, tickerSourceText;
        private int tickerSourceCount, tickerSourcePhase;
        private HudTone tickerSourceTone;

        private void BuildTicker()
        {
            tickerTag = Text("TickerTag", TextAlignmentOptions.Center, legendSize, TextOverflowModes.Overflow);
            Shrink(tickerTag, legendSize);
            tickerText = Text("Ticker", TextAlignmentOptions.MidlineLeft, legendSize, TextOverflowModes.Ellipsis);
            Shrink(tickerText, legendSize);
            tickerTag.gameObject.SetActive(false);
            tickerText.gameObject.SetActive(false);
            tickerWords = null;
        }

        private void DestroyTicker()
        {
            tickerTag = tickerText = null;
            tickerWords = tickerSourceText = null;
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
                tickerWords = (line.Tone == HudTone.Warning ? "WARNING · " : "") + NoticeTicker.Words(line).ToUpperInvariant();
            }
            return tickerWords;
        }

        private float TickerChipWidth(float u) => HudBoardLayout.SystemSlotWidth(f.G.Width, u) * 1.5f;

        private void PresentTicker()
        {
            bool on = board.HasTicker;
            Show(tickerTag, on);
            Show(tickerText, on);
            if (!on) return;
            float u = f.U, pad = HudBoardLayout.Pad * u, h = HudBoardLayout.TickerH * u, chipW = TickerChipWidth(u);
            TickerLine line = board.Ticker;
            Color tone = ToneColor(line.Tone);
            string tag = NoticeTicker.Tag(line.Channel);
            if (tickerTag.text != tag) tickerTag.text = tag;
            string words = TickerWords(line);
            if (tickerText.text != words) tickerText.text = words;
            tickerTag.color = tone;
            tickerText.color = tone;
            Place(tickerTag.rectTransform, pad, f.G.Ticker.Y, chipW, h);
            Place(tickerText.rectTransform, pad + chipW + 6f * u, f.G.Ticker.Y, f.G.Width - pad * 2f - chipW - 6f * u, h);
            bool fresh = NoticeTicker.IsFresh(line);
            beat |= fresh;
            signature = signature * 29 + (int)line.Tone * 7 + tag.GetHashCode() + (fresh ? 3 : 0);
        }

        private void PaintTicker(AvQuadBuffer b)
        {
            if (!board.HasTicker) return;
            float u = f.U, pad = HudBoardLayout.Pad * u, h = HudBoardLayout.TickerH * u, chipW = TickerChipWidth(u);
            TickerLine line = board.Ticker;
            Color tone = ToneColor(line.Tone);
            float y1 = f.G.Height - f.G.Ticker.Y, y0 = y1 - h;
            bool fresh = NoticeTicker.IsFresh(line);
            AvStrokes.Fill(b, pad, y0, chipW, h, tone.ToRgba().WithAlpha(fresh ? (f.FlashOn ? 0.5f : 0.15f) : line.Tone == HudTone.Info ? 0.12f : 0.25f));
            Rect4(b, pad, y0, pad + chipW, y1, (line.Tone == HudTone.Warning ? 1.75f : 1.25f) * u, tone.ToRgba());
        }
    }
}
