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
    /// with the phase tracker under it, assigned ground groups and nearby air/naval assets as counters.
    /// The phase and aim are the staff's verified readout, without an invented progress percentage.
    /// </summary>
    internal sealed class StrOpCard : AvPart
    {
        private const float Pad = 12f, Top = 8f, ForceW = 68f, ForceGap = 4f, RowA = 18f;
        private static readonly string[] Phases = { "FORMING", "ADVANCING", "IN CONTACT" };
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text kind, label, summary;
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
            summary = AvText.Make(Rect, "Aim", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            strip = new StrStageStrip(Rect, Phases, AvTextRole.Micro);
            string[] names = { "GND GROUPS\nASSIGNED", "AIRCRAFT\nNEARBY", "SHIPS\nNEARBY" };
            string[] tips =
            {
                "Ground groups committed to this operation.",
                "Nearby friendly aircraft; staff has not assigned them to this operation.",
                "Nearby friendly ships; staff has not assigned them to this operation.",
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
            // DEFEND and RECON operations report HOLDING / SCOUTING in place of ADVANCING.
            return string.Equals(phase, "HOLDING", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(phase, "SCOUTING", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
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
            bool changed = AvText.Set(kind, "ACT · " + kindText);
            changed |= AvText.Set(label, name);
            changed |= AvText.Set(summary, string.IsNullOrEmpty(summaryText) ? "AIM UNAVAILABLE" : summaryText);
            AvHelpTip.Attach(frame.gameObject, string.IsNullOrEmpty(summaryText)
                ? "Live operation. The phase strip shows how far it has come."
                : summaryText + " The phase strip shows how far the operation has come.");
            AvState s = PhaseState(phase);
            changed |= AvText.Set(phaseText, AvStates.Glyph(s) + (phase ?? ""));
            strip.Set(PhaseIndex(phase), s);
            int[] counts = { ground, air, naval };
            for (int i = 0; i < forces.Length; i++)
                AvText.Set(forces[i].Number, counts[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (s != state) state = s;
            Restyle();
            if (changed) Changed();
        }

        private static float LeftW(float width) =>
            Mathf.Max(60f, width - 2f * Pad - (3f * ForceW + 2f * ForceGap) - 10f);

        private float LabelH(float width) => Mathf.Max(48f, AvText.Height(label, LeftW(width)));

        public override float Measure(float width) =>
            Top + RowA + 3f + LabelH(width) + 4f + StrStageStrip.Height + 6f + AvText.Height(summary, Mathf.Max(20f, width - 2f * Pad)) + 8f;

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
            float sy = y + lh + 4f + StrStageStrip.Height + 6f;
            AvLay.Place(summary.rectTransform, Pad, sy, w, AvText.Height(summary, w));
            float boxH = lh + 4f + StrStageStrip.Height, x0 = s.W - Pad - (3f * ForceW + 2f * ForceGap);
            for (int i = 0; i < forces.Length; i++)
            {
                AvLay.Place(forces[i].Box, x0 + i * (ForceW + ForceGap), y, ForceW, boxH);
                AvLay.Place(forces[i].Symbol.rectTransform, (ForceW - 20f) * 0.5f, 0f, 20f, 20f);
                AvLay.Place(forces[i].Number.rectTransform, 2f, 18f, ForceW - 4f, 27f);
                AvLay.Place(forces[i].Name.rectTransform, 2f, boxH - 30f, ForceW - 4f, 30f);
            }
        }

        public override void Restyle()
        {
            Color c = AvInk.State(state);
            frame.Paint(AvInk.Raised, c.WithAlpha(0.6f));
            frame.BracketColor = c;
            frame.SetVerticesDirty();
            rail.color = c;
            kind.color = AvInk.Key;
            label.color = AvInk.Ink;
            summary.color = AvInk.Dim;
            phaseFrame.Paint(c.WithAlpha(0.2f), c.WithAlpha(0.8f));
            phaseText.color = AvInk.Ink;
            strip.Restyle();
            for (int i = 0; i < forces.Length; i++)
            {
                bool none = forces[i].Number.text == "0";
                forces[i].Frame.Paint(Color.clear, Color.clear);
                forces[i].Symbol.color = none ? AvInk.Muted : c.WithAlpha(0.8f);
                forces[i].Number.color = none ? AvInk.Muted : AvInk.Ink;
                forces[i].Name.color = AvInk.Muted;
            }
        }
    }

    /// <summary>
    /// Staff proposals name their aim, nearby observed forces, risk and deadline on the card.
    /// Clicking sends the painted identity for host validation; unavailable commands remain readouts.
    /// </summary>
    internal sealed class StrProposalDeck : AvPart
    {
        private const float PadX = 10f, ChevW = 20f, RowA = 18f, Top = 7f, Gap = 6f;
        private readonly Card[] cards;
        private readonly Action<int, int> onPick;

        private sealed class Card
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text Kind, Countdown, Label, Brief, Forces, Chevron;
            public RectTransform RiskBox;
            public AvFrame RiskFrame;
            public TMP_Text Risk;
            public AvHelpTip Tip;
            public AvHit Hit;
            public AvState RiskState;
            public bool Hover, Live;
            public int Id, Revision;
        }

        public StrProposalDeck(RectTransform parent, int slots, Action<int, int> pick)
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
                c.Brief = AvText.Make(c.Root, "Aim", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                c.Forces = AvText.Make(c.Root, "EligibleForces", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                c.Chevron = AvIcons.Make(c.Root, AvIcon.ChevronRight, 18f, Color.white);
                c.RiskBox = AvLay.Child(c.Root, "RiskChip");
                c.RiskFrame = AvFrame.Add(c.RiskBox, "Frame", AvChamfer.Diagonal(4f));
                AvLay.Fill(c.RiskFrame.rectTransform);
                c.Risk = AvText.Make(c.RiskBox, "Risk", AvTextRole.Micro, "", TextAlignmentOptions.Center);
                AvText.Fit(c.Risk, false);
                AvLay.Fill(c.Risk.rectTransform, 1f);
                c.Hit = AvHit.On(c.Frame);
                c.Hit.Hover = h => { c.Hover = h; Style(slot); };
                // Identity belongs to the painted card. A later snapshot may reorder its slot before refresh.
                c.Hit.Click = e => { if (c.Live && e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) onPick?.Invoke(c.Id, c.Revision); };
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
            if (r.Contains("HIGH")) return AvState.Danger;
            if (r.Contains("MED") || r.Contains("MODERATE")) return AvState.Caution;
            return r.Contains("LOW") ? AvState.Ready : AvState.Inert;
        }

        public void Hide(int i)
        {
            if (!cards[i].Root.gameObject.activeSelf) return;
            cards[i].Root.gameObject.SetActive(false);
            Changed();
        }

        public void Set(int i, int id, int revision, string kind, string label, string brief, string forces, string risk,
            int seconds, bool pickable, string help)
        {
            Card c = cards[i];
            bool wasOn = c.Root.gameObject.activeSelf;
            c.Root.gameObject.SetActive(true);
            bool changed = !wasOn;
            c.Id = id;
            c.Revision = revision;
            changed |= AvText.Set(c.Kind, "O" + (i + 1) + " · " + kind + (i == 0 ? " · AUTO DEFAULT" : ""));
            changed |= AvText.Set(c.Label, label);
            changed |= AvText.Set(c.Brief, "AIM · " + (string.IsNullOrEmpty(brief) ? "UNAVAILABLE" : brief));
            changed |= AvText.Set(c.Forces, "NEARBY · " + (string.IsNullOrEmpty(forces) ? "UNKNOWN" : forces));
            changed |= AvText.Set(c.Countdown, seconds < 0 ? "—" : seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "S");
            AvState rs = RiskStateOf(risk);
            string riskWord = AvStates.Glyph(rs) + "RISK " + (risk ?? "");
            changed |= AvText.Set(c.Risk, riskWord);
            c.RiskState = rs;
            if (c.Live != pickable) { c.Live = pickable; changed = true; }
            c.Hit.Interactable = pickable;
            c.Tip.Text = (string.IsNullOrEmpty(brief) ? "" : brief + " ") +
                         "Nearby observed forces: " + (forces ?? "") + ". These units are not assigned to this proposal. Staff selects the default when the timer ends; host revalidates it. " + help;
            Style(i);
            if (changed) Changed();
        }

        private static float TextW(float width, Card c) => Mathf.Max(20f, width - 2f * PadX - (c.Live ? ChevW : 0f));

        private static float LabelW(float width, Card c) => Mathf.Max(20f, TextW(width, c) - AvText.Width(c.Risk) - 30f);
        private static float LabelH(Card c, float width) => Mathf.Max(20f, AvText.Height(c.Label, LabelW(width, c)));
        private static float CardH(Card c, float width) => Top + RowA + 3f + LabelH(c, width) + 4f +
            AvText.Height(c.Brief, TextW(width, c)) + 3f + AvText.Height(c.Forces, TextW(width, c)) + Top;

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
                float h = CardH(c, s.W), w = TextW(s.W, c), lh = LabelH(c, s.W);
                AvLay.Place(c.Root, 0f, y, s.W, h);
                AvLay.Place(c.Rail.rectTransform, 0f, 0f, 3f, h);
                AvLay.Place(c.Kind.rectTransform, PadX, Top, w - 46f, RowA);
                AvLay.Place(c.Countdown.rectTransform, PadX + w - 40f, Top, 40f, RowA);
                float y1 = Top + RowA + 3f, riskW = AvText.Width(c.Risk) + 22f;
                AvLay.Place(c.Label.rectTransform, PadX, y1, LabelW(s.W, c), lh);
                AvLay.Place(c.RiskBox, PadX + w - riskW, y1, riskW, 20f);
                float by = y1 + lh + 4f, bh = AvText.Height(c.Brief, w);
                AvLay.Place(c.Brief.rectTransform, PadX, by, w, bh);
                AvLay.Place(c.Forces.rectTransform, PadX, by + bh + 3f, w, AvText.Height(c.Forces, w));
                c.Chevron.gameObject.SetActive(c.Live);
                AvLay.Place(c.Chevron.rectTransform, s.W - PadX - ChevW + 4f, (h - 22f) * 0.5f, ChevW, 22f);
                y += h + Gap;
            }
        }

        private void Style(int i)
        {
            Card c = cards[i];
            Color select = AvInk.Select, rail = AvInk.State(AvState.Ready), risk = AvInk.State(c.RiskState);
            bool lit = c.Hover && c.Live;
            c.Frame.Paint(lit ? AvInk.Raised : AvInk.Inert, lit ? select : AvInk.Hairline);
            c.Rail.color = c.Live ? rail : AvInk.State(AvState.Inert);
            c.Kind.color = AvInk.Key;
            c.Countdown.color = AvInk.Ink;
            c.Label.color = AvInk.Ink;
            c.Forces.color = AvInk.Dim;
            c.Brief.color = AvInk.Dim;
            c.Chevron.color = lit ? select : AvInk.Dim;
            c.RiskFrame.Paint(risk.WithAlpha(0.18f), risk.WithAlpha(0.75f));
            c.Risk.color = AvInk.Ink;
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
    internal sealed class StrFrontBoard : AvPagedStack<StrFrontBoard.Row>
    {
        private const float RowH = 32f, PadX = 10f, GaugeW = 92f;

        internal sealed class Row : AvPart
        {
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text name, status, percent;
            private readonly AvGaugeGraphic bar;
            private readonly AvHelpTip tip;
            private bool observed;
            private AvState state;

            public Row(RectTransform parent, int index)
            {
                Rect = AvLay.Child(parent, "Front " + index);
                frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
                AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                name = StrPaint.Fit(Rect, "Name", AvTextRole.Label);
                status = StrPaint.Fit(Rect, "Status", AvTextRole.Micro);
                percent = StrPaint.Fit(Rect, "Percent", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
                var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(Rect, false);
                bar = go.AddComponent<AvGaugeGraphic>();
                bar.Shape = AvGaugeShape.Bar;
                bar.raycastTarget = false;
                frame.raycastTarget = true;
                tip = AvHelpTip.Attach(frame.gameObject, null);
            }

            public void Set(string label, string text, float pressure01, bool isObserved, AvState newState, string help)
            {
                AvText.Set(name, label);
                AvText.Set(status, text);
                observed = isObserved;
                state = newState;
                AvText.Set(percent, isObserved ? TheaterReadout.Percent(pressure01) : "—");
                bar.Value = isObserved ? pressure01 : 0f;
                bar.gameObject.SetActive(isObserved);
                tip.Text = help;
                Restyle();
            }

            public override float Measure(float width) => RowH;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(rail.rectTransform, 0f, 0f, 2f, RowH);
                float tw = Mathf.Max(20f, s.W - PadX - GaugeW - 16f);
                AvLay.Place(name.rectTransform, PadX + 4f, 1f, tw, 16f);
                AvLay.Place(status.rectTransform, PadX + 4f, 16f, tw, 15f);
                AvLay.Place(percent.rectTransform, s.W - PadX - GaugeW, 1f, GaugeW, 17f);
                AvLay.Place((RectTransform)bar.transform, s.W - PadX - GaugeW, 22f, GaugeW, 5f);
            }

            public override void Restyle()
            {
                Color c = AvInk.State(state);
                frame.Paint(AvInk.Inert, Color.clear);
                rail.color = c;
                name.color = observed ? AvInk.Ink : AvInk.Dim;
                status.color = AvInk.Dim;
                percent.color = observed ? AvInk.StateText(state) : AvInk.Muted;
                bar.Track = AvInk.Hairline;
                bar.FillColor = bar.FillEnd = c;
                bar.SetVerticesDirty();
            }
        }

        public StrFrontBoard(RectTransform parent, int pageSize, Action<int, Row> binder)
            : base(parent, null, pageSize, 2f, (rect, i) => new Row(rect, i), binder, "FrontBoard", pagerLead: 4f)
        {
            Restyle();
        }
    }
}
