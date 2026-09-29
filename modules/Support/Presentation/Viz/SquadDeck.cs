using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>What one squad card shows; every string is decided by the panel from the detachment model.</summary>
    internal struct SquadCardData
    {
        public string Callsign, StateWord, Line, RankWord, NextText, Clock;
        public AvState Tone;
        public AvIcon Icon;
        public int Rank;
        public bool Formed, ShowBar, Deciding, CanExecute, CanExtract;
        public float Progress;

        public bool SameAs(in SquadCardData o) =>
            Callsign == o.Callsign && StateWord == o.StateWord && Line == o.Line && RankWord == o.RankWord &&
            NextText == o.NextText && Clock == o.Clock && Tone == o.Tone && Icon == o.Icon && Rank == o.Rank &&
            Formed == o.Formed && ShowBar == o.ShowBar && Deciding == o.Deciding && CanExecute == o.CanExecute &&
            CanExtract == o.CanExtract && Mathf.Abs(Progress - o.Progress) < 0.004f;
    }

    /// <summary>
    /// SPEC OPS's hero: the four team slots as squad cards in a 2 x 2 deck. Each card carries the
    /// callsign, a state icon and word, the line saying where the team is, rank pips with the wins to
    /// the next rank, and a phase bar with its clock; a team at its decision window swaps the bar for
    /// EXECUTE / EXTRACT. Fixed height, so a refresh never moves the page.
    /// </summary>
    internal sealed class SquadDeck : AvPart
    {
        public const int Slots = 4;
        private const float CardH = 104f, Gap = 8f;

        private readonly OpsCanvas art;
        private readonly TMP_Text[] icons = new TMP_Text[Slots], callsigns = new TMP_Text[Slots], states = new TMP_Text[Slots];
        private readonly TMP_Text[] lines = new TMP_Text[Slots], ranks = new TMP_Text[Slots], nexts = new TMP_Text[Slots], clocks = new TMP_Text[Slots];
        private readonly AvControl[] execute = new AvControl[Slots], extract = new AvControl[Slots];
        private readonly SquadCardData[] data = new SquadCardData[Slots];
        private readonly bool[] seen = new bool[Slots];
        private float width;
        private bool dirty = true;

        public SquadDeck(RectTransform parent, Action<int, bool> directive)
        {
            Rect = AvLay.Child(parent, "SquadDeck");
            art = OpsCanvas.Add(Rect, "Art");
            for (int i = 0; i < Slots; i++)
            {
                int slot = i;
                icons[i] = AvIcons.Make(Rect, AvIcon.Minus, AvGridTokens.IconHead, Color.white);
                callsigns[i] = OpsText.Line(Rect, "Callsign" + i, AvTextRole.Head, TextAlignmentOptions.MidlineLeft);
                states[i] = OpsText.Line(Rect, "State" + i, AvTextRole.Label, TextAlignmentOptions.MidlineRight);
                lines[i] = OpsText.Line(Rect, "Line" + i, AvTextRole.ProseSmall, TextAlignmentOptions.MidlineLeft);
                ranks[i] = OpsText.Line(Rect, "Rank" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
                nexts[i] = OpsText.Line(Rect, "Next" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
                clocks[i] = OpsText.Line(Rect, "Clock" + i, AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
                execute[i] = AvControl.Make(Rect, new AvControl.Spec("EXECUTE", () => directive?.Invoke(slot, true), AvButtonStyle.Primary));
                extract[i] = AvControl.Make(Rect, new AvControl.Spec("EXTRACT", () => directive?.Invoke(slot, false), AvButtonStyle.Danger));
                execute[i].Help = "Execute at the site using the latest host threat forecast.";
                extract[i].Help = "Extract safely now; no task roll. A held post ends but earned rank stays.";
                execute[i].Rect.gameObject.SetActive(false);
                extract[i].Rect.gameObject.SetActive(false);
            }
            Restyle();
        }

        public AvControl Execute(int slot) => execute[slot];
        public AvControl Extract(int slot) => extract[slot];

        public void Set(int slot, in SquadCardData d)
        {
            if (slot < 0 || slot >= Slots) return;
            if (seen[slot] && data[slot].SameAs(d)) return;
            seen[slot] = true;
            data[slot] = d;
            OpsText.Set(callsigns[slot], d.Callsign);
            OpsText.Set(states[slot], AvStates.Glyph(d.Tone) + d.StateWord);
            OpsText.Set(lines[slot], d.Line);
            OpsText.Set(ranks[slot], d.Formed ? d.RankWord : "");
            OpsText.Set(nexts[slot], d.Formed ? d.NextText : "");
            OpsText.Set(clocks[slot], d.ShowBar && !d.Deciding ? d.Clock : "");
            AvIcons.Set(icons[slot], d.Icon, AvGridTokens.IconHead);
            execute[slot].Rect.gameObject.SetActive(d.Deciding);
            extract[slot].Rect.gameObject.SetActive(d.Deciding);
            execute[slot].Interactable = d.CanExecute;
            extract[slot].Interactable = d.CanExtract;
            RestyleCard(slot);
            dirty = true;
            Paint();
        }

        public override float Measure(float w) => 2f * CardH + Gap;

        private float CardW => (width - Gap) * 0.5f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place((RectTransform)art.transform, 0f, 0f, s.W, s.H);
            float cw = CardW;
            for (int i = 0; i < Slots; i++)
            {
                float x = (i % 2) * (cw + Gap), y = (i / 2) * (CardH + Gap);
                AvLay.Place(icons[i].rectTransform, x + 12f, y + 9f, 16f, 16f);
                OpsText.Place(callsigns[i], x + 34f, y + 7f, cw - 34f - 112f, 20f);
                OpsText.Place(states[i], x + cw - 12f - 104f, y + 7f, 104f, 20f);
                OpsText.Place(lines[i], x + 14f, y + 31f, cw - 26f, 16f);
                OpsText.Place(ranks[i], x + 14f + 3f * 13f + 6f, y + 51f, 84f, 15f);
                OpsText.Place(nexts[i], x + cw - 12f - 104f, y + 51f, 104f, 15f);
                OpsText.Place(clocks[i], x + cw - 12f - 52f, y + 70f, 52f, 18f);
                float bw = (cw - 28f - 6f) * 0.5f;
                AvLay.Place(execute[i].Rect, x + 14f, y + 72f, bw, 24f);
                AvLay.Place(extract[i].Rect, x + 14f + bw + 6f, y + 72f, bw, 24f);
            }
            dirty = true;
            Paint();
        }

        public override void Restyle()
        {
            for (int i = 0; i < Slots; i++) { RestyleCard(i); execute[i].Restyle(); extract[i].Restyle(); }
            dirty = true;
            Paint();
        }

        private void RestyleCard(int i)
        {
            SquadCardData d = data[i];
            Color hue = d.Formed ? OpsInk.Word(d.Tone == AvState.Inert ? AvState.Info : d.Tone) : OpsInk.Muted;
            icons[i].color = hue;
            callsigns[i].color = d.Formed ? OpsInk.Ink : OpsInk.Dim;
            states[i].color = d.Formed ? OpsInk.Word(d.Tone) : OpsInk.Muted;
            lines[i].color = OpsInk.Dim;
            ranks[i].color = OpsInk.Key;
            nexts[i].color = OpsInk.Muted;
            clocks[i].color = d.Formed ? OpsInk.Word(d.Tone == AvState.Inert ? AvState.Info : d.Tone) : OpsInk.Muted;
        }

        private void Paint()
        {
            if (!dirty || width <= 0f) return;
            dirty = false;
            float cw = CardW;
            art.Begin();
            for (int i = 0; i < Slots; i++)
            {
                float x = (i % 2) * (cw + Gap), y = (i / 2) * (CardH + Gap);
                SquadCardData d = data[i];
                Color hue = OpsInk.Rail(d.Tone);
                art.Chamfer(x, y, cw, CardH, 8f, OpsInk.Inert);
                art.Outline(x, y, cw, CardH, 1f, OpsInk.A(d.Deciding ? hue : OpsInk.Hairline, d.Deciding ? 0.95f : 0.8f));
                art.Box(x, y + 8f, 3f, CardH - 16f, d.Formed ? hue : OpsInk.A(OpsInk.Hairline, 0.8f));
                if (!d.Formed)
                {
                    art.Dashed(x + 14f, y + 88f, x + cw - 14f, y + 88f, 1f, 6f, 5f, OpsInk.A(OpsInk.Hairline, 0.9f));
                    continue;
                }
                for (int p = 0; p < 3; p++)
                {
                    float px = x + 14f + p * 13f, py = y + 54f;
                    if (p < d.Rank) art.Box(px, py, 9f, 9f, OpsInk.Key);
                    else art.Outline(px, py, 9f, 9f, 1f, OpsInk.A(OpsInk.Muted, 0.8f));
                }
                if (d.Deciding || !d.ShowBar) continue;
                float bx = x + 14f, bw = cw - 28f - 58f, by = y + 76f;
                art.Box(bx, by, bw, 6f, OpsInk.A(OpsInk.Hairline, 0.55f));
                float f = Mathf.Clamp01(d.Progress);
                if (f > 0f) art.BoxH(bx, by, Mathf.Max(2f, bw * f), 6f, OpsInk.A(hue, 0.5f), hue);
            }
            art.End();
        }
    }
}
