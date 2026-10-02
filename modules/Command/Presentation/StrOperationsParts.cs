using NOAvionics;
using System;
using BoscaliSummer.Modules.Command.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The live operation as a compact hero card: kind and phase word on the first line, the operation's name
    /// with the phase tracker under it, and the committed ground / air / naval groups as three small counters
    /// on the right. The phase is the only progress the staff reports, so that is what the strip shows and
    /// nothing is invented; the staff's own summary sentence rides on hover.
    /// </summary>
    internal sealed class StrOpCard : AvPart
    {
        private const float Pad = 12f, Top = 8f, ForceW = 44f, ForceGap = 4f, RowA = 18f;
        private static readonly string[] Phases = { "FORMING", "ADVANCING", "IN CONTACT" };
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text kind, label;
        private readonly RectTransform phaseBox;
        private readonly AvFrame phaseFrame;
        private readonly TMP_Text phaseText;
        private readonly StrStageStrip strip;
        private readonly Force[] forces = new Force[3];
        private AvState state = AvState.Info;

        private sealed class Force
        {
            public RectTransform Box;
            public AvFrame Frame;
            public TMP_Text Number, Name, Symbol;
        }

        public StrOpCard(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "OpCard");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            frame.Bracket = 8f;
            AvBundle.Engraving(Rect, 0.55f);
            frame.raycastTarget = true;
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            kind = StrPaint.Fit(Rect, "Kind", AvTextRole.Micro);
            phaseBox = AvLay.Child(Rect, "PhaseChip");
            phaseFrame = AvFrame.Add(phaseBox, "Frame", AvChamfer.Diagonal(4f));
            AvLay.Fill(phaseFrame.rectTransform);
            phaseText = AvText.Make(phaseBox, "Phase", AvTextRole.Label, "", TextAlignmentOptions.Center);
            AvText.Fit(phaseText, false);
            AvLay.Fill(phaseText.rectTransform, 1f);
            label = AvText.Make(Rect, "Label", AvTextRole.Headline, "", TextAlignmentOptions.TopLeft, true);
            strip = new StrStageStrip(Rect, Phases, AvTextRole.Micro);
            string[] names = { "GND", "AIR", "NAV" };
            string[] tips =
            {
                "Ground groups committed to this operation.",
                "Air groups committed to this operation.",
                "Naval groups committed to this operation.",
            };
            for (int i = 0; i < forces.Length; i++)
            {
                var f = new Force { Box = AvLay.Child(Rect, "Force " + names[i]) };
                f.Frame = AvFrame.Add(f.Box, "Frame", AvChamfer.Diagonal(5f));
                AvLay.Fill(f.Frame.rectTransform);
                f.Frame.raycastTarget = true;
                AvHelpTip.Attach(f.Frame.gameObject, tips[i]);
                f.Number = StrPaint.Fit(f.Box, "Number", AvTextRole.Display, TextAlignmentOptions.Center);
                f.Symbol = AvIcons.Make(f.Box, i == 0 ? AvIcon.UsersGroup : i == 1 ? AvIcon.Plane : AvIcon.Flag, 20f, Color.white);
                f.Name = StrPaint.Fit(f.Box, "Name", AvTextRole.Micro, TextAlignmentOptions.Center);
                f.Name.text = names[i];
                forces[i] = f;
            }
            Restyle();
        }

        public static int PhaseIndex(string phase)
        {
            for (int i = 0; i < Phases.Length; i++)
                if (string.Equals(Phases[i], phase, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public static AvState PhaseState(string phase)
        {
            switch (PhaseIndex(phase))
            {
                case 0: return AvState.Info;
                case 1: return AvState.Ready;
                case 2: return AvState.Caution;
                default: return AvState.Info;
            }
        }

        public void Set(string kindText, string name, string phase, string summaryText, int ground, int air, int naval)
        {
            bool changed = StrPaint.Put(kind, kindText);
            changed |= StrPaint.Put(label, name);
            // The staff's summary sentence lives on hover; the card body stays name, phase and force counts.
            AvHelpTip.Attach(frame.gameObject, string.IsNullOrEmpty(summaryText)
                ? "Live operation. The phase strip shows how far it has come."
                : summaryText + " The phase strip shows how far the operation has come.");
            AvState s = PhaseState(phase);
            changed |= StrPaint.Put(phaseText, AvStates.Glyph(s) + (phase ?? ""));
            strip.Set(PhaseIndex(phase), s);
            int[] counts = { ground, air, naval };
            for (int i = 0; i < forces.Length; i++)
                StrPaint.Put(forces[i].Number, counts[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (s != state) state = s;
            Restyle();
            if (changed) Changed();
        }

        private static float LeftW(float width) =>
            Mathf.Max(60f, width - 2f * Pad - (3f * ForceW + 2f * ForceGap) - 10f);

        private float LabelH(float width) => Mathf.Max(36f, AvText.Height(label, LeftW(width)));

        public override float Measure(float width) =>
            Top + RowA + 3f + LabelH(width) + 4f + StrStageStrip.Height + 8f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = Mathf.Max(20f, s.W - 2f * Pad), lw = LeftW(s.W), lh = LabelH(s.W);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            float chipW = Mathf.Min(w * 0.6f, AvText.Width(phaseText) + 22f);
            AvLay.Place(kind.rectTransform, Pad, Top, Mathf.Max(20f, w - chipW - 8f), RowA);
            AvLay.Place(phaseBox, s.W - Pad - chipW, Top, chipW, RowA);
            float y = Top + RowA + 3f;
            AvLay.Place(label.rectTransform, Pad, y, lw, lh);
            strip.Place(new AvSlot(Pad, y + lh + 4f, lw, StrStageStrip.Height));
            float boxH = lh + 4f + StrStageStrip.Height, x0 = s.W - Pad - (3f * ForceW + 2f * ForceGap);
            for (int i = 0; i < forces.Length; i++)
            {
                AvLay.Place(forces[i].Box, x0 + i * (ForceW + ForceGap), y, ForceW, boxH);
                AvLay.Place(forces[i].Symbol.rectTransform, (ForceW - 20f) * 0.5f, 0f, 20f, 20f);
                AvLay.Place(forces[i].Number.rectTransform, 2f, 18f, ForceW - 4f, 27f);
                AvLay.Place(forces[i].Name.rectTransform, 2f, boxH - 17f, ForceW - 4f, 15f);
            }
        }

        public override void Restyle()
        {
            Color c = StrPaint.State(state);
            frame.Paint(StrPaint.Raised, c.WithAlpha(0.6f));
            frame.BracketColor = c;
            frame.SetVerticesDirty();
            rail.color = c;
            kind.color = StrPaint.Key;
            label.color = StrPaint.Ink;
            phaseFrame.Paint(c.WithAlpha(0.2f), c.WithAlpha(0.8f));
            phaseText.color = StrPaint.Ink;
            strip.Restyle();
            for (int i = 0; i < forces.Length; i++)
            {
                bool none = forces[i].Number.text == "0";
                forces[i].Frame.Paint(Color.clear, Color.clear);
                forces[i].Symbol.color = none ? StrPaint.Muted : c.WithAlpha(0.8f);
                forces[i].Number.color = none ? StrPaint.Muted : StrPaint.Ink;
                forces[i].Name.color = StrPaint.Muted;
            }
        }
    }

    /// <summary>
    /// Staff proposals as two-line cards: kind, front name and countdown on the first line, the forces and
    /// the risk on the second. Clicking a card picks it (the host validates); a card the reader cannot pick is
    /// a plain readout. The staff's brief rides on hover.
    /// </summary>
    internal sealed class StrProposalDeck : AvPart
    {
        private const float PadX = 10f, ChevW = 20f, CdW = 44f, RowA = 20f, RowB = 20f, Top = 6f, Gap = 4f;
        private readonly Card[] cards;
        private readonly Action<int> onPick;

        private sealed class Card
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text Kind, Countdown, Label, Forces, Chevron;
            public RectTransform RiskBox;
            public AvFrame RiskFrame;
            public TMP_Text Risk;
            public AvHelpTip Tip;
            public AvHit Hit;
            public AvState RiskState;
            public bool Hover, Live;
        }

        public StrProposalDeck(RectTransform parent, int slots, Action<int> pick)
        {
            Rect = AvLay.Child(parent, "ProposalDeck");
            onPick = pick;
            cards = new Card[slots];
            for (int i = 0; i < slots; i++)
            {
                int slot = i;
                var c = new Card { Root = AvLay.Child(Rect, "Proposal " + i) };
                c.Frame = AvFrame.Add(c.Root, "Frame", AvChamfer.Diagonal(6f));
                AvLay.Fill(c.Frame.rectTransform);
                c.Rail = AvLay.Solid(c.Root, "Rail", Color.clear);
                c.Kind = StrPaint.Fit(c.Root, "Kind", AvTextRole.Micro);
                c.Countdown = StrPaint.Fit(c.Root, "Countdown", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
                c.Label = AvText.Make(c.Root, "Label", AvTextRole.Head, "", TextAlignmentOptions.MidlineLeft, true);
                c.Forces = StrPaint.Fit(c.Root, "Forces", AvTextRole.Micro);
                c.Chevron = AvIcons.Make(c.Root, AvIcon.ChevronRight, 18f, Color.white);
                c.RiskBox = AvLay.Child(c.Root, "RiskChip");
                c.RiskFrame = AvFrame.Add(c.RiskBox, "Frame", AvChamfer.Diagonal(4f));
                AvLay.Fill(c.RiskFrame.rectTransform);
                c.Risk = AvText.Make(c.RiskBox, "Risk", AvTextRole.Micro, "", TextAlignmentOptions.Center);
                AvText.Fit(c.Risk, false);
                AvLay.Fill(c.Risk.rectTransform, 1f);
                c.Hit = AvHit.On(c.Frame);
                c.Hit.Hover = h => { c.Hover = h; Style(slot); };
                c.Hit.Click = e => { if (c.Live) onPick?.Invoke(slot); };
                c.Tip = AvHelpTip.Attach(c.Frame.gameObject, null);
                c.Root.gameObject.SetActive(false);
                cards[i] = c;
            }
            Restyle();
        }

        public int Slots => cards.Length;

        public static AvState RiskStateOf(string risk)
        {
            string r = (risk ?? "").ToUpperInvariant();
            return r.Contains("HIGH") ? AvState.Danger : r.Contains("MED") ? AvState.Caution : AvState.Ready;
        }

        public void Hide(int i)
        {
            if (!cards[i].Root.gameObject.activeSelf) return;
            cards[i].Root.gameObject.SetActive(false);
            Changed();
        }

        public void Set(int i, string kind, string label, string brief, string forces, string risk,
            int seconds, bool pickable, string help)
        {
            Card c = cards[i];
            bool wasOn = c.Root.gameObject.activeSelf;
            c.Root.gameObject.SetActive(true);
            bool changed = !wasOn;
            changed |= StrPaint.Put(c.Kind, kind);
            changed |= StrPaint.Put(c.Label, label);
            StrPaint.Put(c.Forces, forces);
            StrPaint.Put(c.Countdown, seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "S");
            AvState rs = RiskStateOf(risk);
            string riskWord = AvStates.Glyph(rs) + "RISK " + (risk ?? "");
            StrPaint.Put(c.Risk, riskWord);
            c.RiskState = rs;
            if (c.Live != pickable) { c.Live = pickable; changed = true; }
            c.Hit.Interactable = pickable;
            c.Tip.Text = (string.IsNullOrEmpty(brief) ? "" : brief + " ") +
                         "Forces: " + (forces ?? "") + ". The offer lapses when the countdown ends. " + help;
            Style(i);
            if (changed) Changed();
        }

        private static float TextW(float width, Card c) => Mathf.Max(20f, width - 2f * PadX - (c.Live ? ChevW : 0f));

        private static float KindW(Card c) => Mathf.Min(84f, AvText.Width(c.Kind) + 4f);

        private static float LabelW(float width, Card c) => Mathf.Max(20f, TextW(width, c) - KindW(c) - 6f - CdW - 6f);

        private static float RowAHeight(Card c, float width) => Mathf.Max(RowA, AvText.Height(c.Label, LabelW(width, c)));

        private static float CardH(Card c, float width) => Top + RowAHeight(c, width) + 3f + RowB + Top;

        public override float Measure(float width)
        {
            float h = 0f;
            for (int i = 0; i < cards.Length; i++)
                if (cards[i].Root.gameObject.activeSelf) h += CardH(cards[i], width) + Gap;
            return Mathf.Max(0f, h - Gap);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = 0f;
            for (int i = 0; i < cards.Length; i++)
            {
                Card c = cards[i];
                if (!c.Root.gameObject.activeSelf) continue;
                float h = CardH(c, s.W), w = TextW(s.W, c), ra = RowAHeight(c, s.W), kw = KindW(c);
                AvLay.Place(c.Root, 0f, y, s.W, h);
                AvLay.Place(c.Rail.rectTransform, 0f, 0f, 3f, h);
                AvLay.Place(c.Kind.rectTransform, PadX, Top, kw, RowA);
                AvLay.Place(c.Label.rectTransform, PadX + kw + 6f, Top, LabelW(s.W, c), ra);
                AvLay.Place(c.Countdown.rectTransform, PadX + w - CdW, Top, CdW, RowA);
                float fy = Top + ra + 3f;
                float riskW = Mathf.Min(w * 0.5f, AvText.Width(c.Risk) + 22f);
                AvLay.Place(c.Forces.rectTransform, PadX, fy, Mathf.Max(20f, w - riskW - 8f), RowB);
                AvLay.Place(c.RiskBox, PadX + w - riskW, fy, riskW, RowB);
                c.Chevron.gameObject.SetActive(c.Live);
                AvLay.Place(c.Chevron.rectTransform, s.W - PadX - ChevW + 4f, (h - 22f) * 0.5f, ChevW, 22f);
                y += h + Gap;
            }
        }

        private void Style(int i)
        {
            Card c = cards[i];
            Color select = StrPaint.Select, rail = StrPaint.State(AvState.Ready), risk = StrPaint.State(c.RiskState);
            bool lit = c.Hover && c.Live;
            c.Frame.Paint(lit ? StrPaint.Raised : StrPaint.Inert, lit ? select : StrPaint.Hairline);
            c.Rail.color = c.Live ? rail : StrPaint.State(AvState.Inert);
            c.Kind.color = StrPaint.Key;
            c.Countdown.color = StrPaint.Ink;
            c.Label.color = StrPaint.Ink;
            c.Forces.color = StrPaint.Dim;
            c.Chevron.color = lit ? select : StrPaint.Dim;
            c.RiskFrame.Paint(risk.WithAlpha(0.18f), risk.WithAlpha(0.75f));
            c.Risk.color = StrPaint.Ink;
        }

        public override void Restyle()
        {
            for (int i = 0; i < cards.Length; i++) Style(i);
        }
    }

    /// <summary>
    /// Active fronts as compact rows: name and status on the left, a pressure percentage and bar on the
    /// right. Paged past <c>pageSize</c> like the kit's list, with the same pager.
    /// </summary>
    internal sealed class StrFrontBoard : AvPart
    {
        private const float RowH = 32f, PadX = 10f, GaugeW = 92f;
        private readonly Row[] rows;
        private readonly Action<int, Row> bind;
        private readonly AvControl prev, next;
        private readonly TMP_Text range;
        private int count;

        internal sealed class Row
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text Name, Status, Percent;
            public AvGaugeGraphic Bar;
            public AvHelpTip Tip;
            public bool Observed;
            public AvState State;

            public void Set(string name, string status, float pressure01, bool observed, AvState state, string help)
            {
                StrPaint.Put(Name, name);
                StrPaint.Put(Status, status);
                Observed = observed;
                State = state;
                StrPaint.Put(Percent, observed ? TheaterReadout.Percent(pressure01) : "—");
                Bar.Value = observed ? pressure01 : 0f;
                Bar.gameObject.SetActive(observed);
                Tip.Text = help;
                Style();
            }

            public void Style()
            {
                Color c = StrPaint.State(State);
                Frame.Paint(StrPaint.Inert, Color.clear);
                Rail.color = c;
                Name.color = Observed ? StrPaint.Ink : StrPaint.Dim;
                Status.color = StrPaint.Dim;
                Percent.color = Observed ? StrPaint.StateText(State) : StrPaint.Muted;
                Bar.Track = StrPaint.Hairline;
                Bar.FillColor = Bar.FillEnd = c;
                Bar.SetVerticesDirty();
            }
        }

        public StrFrontBoard(RectTransform parent, int pageSize, Action<int, Row> binder)
        {
            Rect = AvLay.Child(parent, "FrontBoard");
            bind = binder;
            rows = new Row[Mathf.Clamp(pageSize, 1, 12)];
            for (int i = 0; i < rows.Length; i++)
            {
                var r = new Row { Root = AvLay.Child(Rect, "Front " + i) };
                r.Frame = AvFrame.Add(r.Root, "Frame", default(AvChamfer));
                AvLay.Fill(r.Frame.rectTransform);
                r.Rail = AvLay.Solid(r.Root, "Rail", Color.clear);
                r.Name = StrPaint.Fit(r.Root, "Name", AvTextRole.Label);
                r.Status = StrPaint.Fit(r.Root, "Status", AvTextRole.Micro);
                r.Percent = StrPaint.Fit(r.Root, "Percent", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
                var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(r.Root, false);
                r.Bar = go.AddComponent<AvGaugeGraphic>();
                r.Bar.Shape = AvGaugeShape.Bar;
                r.Bar.raycastTarget = false;
                r.Frame.raycastTarget = true;
                r.Tip = AvHelpTip.Attach(r.Frame.gameObject, null);
                r.Root.gameObject.SetActive(false);
                rows[i] = r;
            }
            prev = AvControl.Make(Rect, new AvControl.Spec("PREV", () => Go(Page - 1), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            next = AvControl.Make(Rect, new AvControl.Spec("NEXT", () => Go(Page + 1), AvButtonStyle.Quiet, AvIcon.ChevronRight, true));
            range = AvText.Make(Rect, "Range", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
            Restyle();
        }

        public int Page { get; private set; }
        private int Pages => Mathf.Max(1, (count + rows.Length - 1) / rows.Length);

        public void SetCount(int n)
        {
            count = Mathf.Max(0, n);
            Go(Page);
        }

        private void Go(int page)
        {
            Page = Mathf.Clamp(page, 0, Pages - 1);
            int first = Page * rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                int item = first + i;
                bool shown = item < count;
                rows[i].Root.gameObject.SetActive(shown);
                if (shown) bind?.Invoke(item, rows[i]);
            }
            range.text = count == 0 ? "0 OF 0" : (first + 1) + "–" + Mathf.Min(count, first + rows.Length) + " OF " + count;
            prev.Interactable = Page > 0;
            next.Interactable = Page < Pages - 1;
            Changed();
        }

        public override float Measure(float width)
        {
            float h = 0f;
            for (int i = 0; i < rows.Length; i++) if (rows[i].Root.gameObject.activeSelf) h += RowH + 2f;
            h = Mathf.Max(0f, h - 2f);
            return h + (Pages > 1 ? AvGridTokens.Row + 6f : 0f);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = 0f;
            for (int i = 0; i < rows.Length; i++)
            {
                Row r = rows[i];
                if (!r.Root.gameObject.activeSelf) continue;
                AvLay.Place(r.Root, 0f, y, s.W, RowH);
                AvLay.Place(r.Rail.rectTransform, 0f, 0f, 2f, RowH);
                float tw = Mathf.Max(20f, s.W - PadX - GaugeW - 16f);
                AvLay.Place(r.Name.rectTransform, PadX + 4f, 1f, tw, 16f);
                AvLay.Place(r.Status.rectTransform, PadX + 4f, 16f, tw, 15f);
                AvLay.Place(r.Percent.rectTransform, s.W - PadX - GaugeW, 1f, GaugeW, 17f);
                AvLay.Place((RectTransform)r.Bar.transform, s.W - PadX - GaugeW, 22f, GaugeW, 5f);
                y += RowH + 2f;
            }
            bool paged = Pages > 1;
            prev.gameObject.SetActive(paged);
            next.gameObject.SetActive(paged);
            range.gameObject.SetActive(paged);
            if (!paged) return;
            y += 4f;
            AvLay.Place(prev.Rect, 0f, y, 96f, AvGridTokens.Row);
            AvLay.Place(next.Rect, s.W - 96f, y, 96f, AvGridTokens.Row);
            AvLay.Place(range.rectTransform, 100f, y, s.W - 200f, AvGridTokens.Row);
        }

        public override void Restyle()
        {
            for (int i = 0; i < rows.Length; i++) rows[i].Style();
            range.color = StrPaint.Dim;
            prev.Restyle();
            next.Restyle();
        }
    }
}
