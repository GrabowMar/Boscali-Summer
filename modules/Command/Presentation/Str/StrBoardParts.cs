using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>Implemented by every part: copy the model, change nothing else.</summary>
    internal interface IStrBound
    {
        void Bind(StrViewModel vm);
    }

    /// <summary>
    /// A part that copies the model. It reads no service, clock or status string; a press goes to the hub through <see cref="Controls"/>, and
    /// a part registers the ids it shows when it binds and nothing while it is hidden.
    /// </summary>
    internal abstract class StrPart : AvPart, IStrBound
    {
        protected static readonly StrAct Allowed = new StrAct(StrWhy.None, "");

        protected readonly StrControls Controls;
        protected readonly IStrHub Hub;
        /// <summary>The model of the last bind; presses read the live model through it, never a copy of one row.</summary>
        protected StrViewModel Vm;

        protected StrPart(RectTransform parent, string name, StrControls controls, IStrHub hub)
        {
            Rect = AvLay.Child(parent, name);
            Controls = controls;
            Hub = hub;
        }

        public void Bind(StrViewModel vm)
        {
            Vm = vm;
            Apply(vm);
        }

        protected abstract void Apply(StrViewModel vm);

        /// <summary>A text child: single line that shrinks toward the 11 px floor, or wrapped (the part measures it).</summary>
        protected TMP_Text Text(RectTransform parent, string name, AvTextRole role, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            TMP_Text t = AvText.Make(parent, name, role, "", align, wrap);
            AvText.Fit(t, wrap);
            t.raycastTarget = false;   // text never takes a click: the frame under it does
            return t;
        }

        /// <summary>LOCATE: allowed while the item has a known place on the map.</summary>
        protected StrAct LocateGate(StrItemId id) => StrAnchors.LocateGate(Vm, id);

        protected StrAct Locate(StrItemId id)
        {
            Hub.Select(id, true);
            return new StrAct(StrWhy.None, "MAP FOCUSED", true);
        }
    }

    /// <summary>A kit button that shows its gate: a disabled one carries the reason as its hover help, and its id is registered with the live gate and the hub call.</summary>
    internal sealed class StrButton
    {
        private readonly AvControl control;
        private readonly Func<StrAct> press;
        private string label = "", help = "";

        internal StrButton(RectTransform parent, string text, AvButtonStyle style, Func<StrAct> pressAction, AvIcon icon = AvIcon.None)
        {
            press = pressAction;
            control = AvControl.Make(parent, new AvControl.Spec(text, OnClick, style, icon));
            control.SingleLine();
            label = text;
        }

        internal AvControl Control => control;

        internal bool Interactable => control.Interactable;

        private void OnClick() => press?.Invoke();

        /// <summary>Shows the gate now and registers <paramref name="id"/>. <paramref name="whenAllowed"/> is the hover help of an enabled button.</summary>
        internal void Bind(StrControls controls, string id, string text, Func<StrAct> gate, string whenAllowed, bool latched = false)
        {
            StrAct g = gate();
            if (text != label) { label = text; control.Label = text; }
            bool enabled = g.Allowed || g.Why == StrWhy.SamePosture;
            if (control.Interactable != enabled) control.Interactable = enabled;
            if (control.Latched != latched) control.Latched = latched;
            string tip = g.Allowed ? whenAllowed : g.Text;
            if (tip != help) { help = tip; control.Help = tip; }
            if (!string.IsNullOrEmpty(id)) controls.Register(id, gate, press);
        }

        internal void Place(float x, float y, float w, float h) => AvLay.Place(control.Rect, x, y, w, h);

        internal void SetActive(bool on)
        {
            if (control.gameObject.activeSelf != on) control.gameObject.SetActive(on);
        }

        internal void Restyle() => control.Restyle();
    }

    /// <summary>
    /// The banner above the board: why there is no board or what the staff is doing, with a split-flap countdown and, for a board with no opening, the
    /// checklist. Hidden while an operation runs on a current report.
    /// </summary>
    internal sealed class StrBannerPart : StrPart
    {
        private const float Pad = 10f, TagW = 34f, MarkW = 26f;

        private sealed class CheckRow
        {
            public TMP_Text Mark, Label, Do;
        }

        private readonly AvFrame frame;
        private readonly StrSlab chip;
        private readonly TMP_Text head, countLabel, whyTag, whyText, doTag, doText;
        private readonly StrFlap flap;
        private readonly CheckRow[] checks = new CheckRow[StrViewModel.MaxChecks];
        private StrInkRole ink = StrInkRole.Muted;

        internal StrBannerPart(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Banner", controls, hub)
        {
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            chip = new StrSlab(Rect, "Chip");
            head = AvText.Make(Rect, "Head", AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true);
            AvText.Fit(head, true);
            countLabel = Text(Rect, "CountLabel", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            flap = new StrFlap(Rect, "Countdown");
            whyTag = Text(Rect, "WhyTag", AvTextRole.Micro, TextAlignmentOptions.TopLeft);
            whyText = Text(Rect, "WhyText", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            doTag = Text(Rect, "DoTag", AvTextRole.Micro, TextAlignmentOptions.TopLeft);
            doText = Text(Rect, "DoText", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            whyTag.text = "WHY";
            doTag.text = "DO";
            for (int i = 0; i < checks.Length; i++)
            {
                var row = new CheckRow
                {
                    Mark = Text(Rect, "CheckMark " + i, AvTextRole.DataStrong, TextAlignmentOptions.TopLeft),
                    Label = Text(Rect, "CheckLabel " + i, AvTextRole.Label, TextAlignmentOptions.TopLeft, true),
                    Do = Text(Rect, "CheckDo " + i, AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true),
                };
                checks[i] = row;
            }
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            StrBanner b = vm.Banner;
            SetShown(b.Show);
            if (!b.Show) return;
            ink = StrPanelText.BannerInk(vm.Mode, vm.Stale);
            chip.Set(b.Chip, StrInk.Of(ink));
            AvText.Set(head, b.Head);
            bool count = b.CountSeconds >= 0;
            AvText.Set(countLabel, count ? b.CountLabel : "");
            flap.Set(count ? StrPanelText.Flap(b.CountSeconds) : "");
            flap.SetActive(count);
            AvText.Set(whyText, b.Why);
            AvText.Set(doText, b.Do);
            for (int i = 0; i < checks.Length; i++)
            {
                bool on = i < b.CheckCount;
                CheckRow r = checks[i];
                r.Mark.gameObject.SetActive(on);
                r.Label.gameObject.SetActive(on);
                r.Do.gameObject.SetActive(on);
                if (!on) continue;
                StrCheck c = b.Checks[i];
                AvText.Set(r.Mark, c.Pass == StrTri.Yes ? StrWords.Yes : c.Pass == StrTri.No ? StrWords.No : "·");
                AvText.Set(r.Label, c.Label);
                AvText.Set(r.Do, c.Do);
                r.Mark.color = c.Pass == StrTri.Yes ? AvInk.State(AvState.Ready) : c.Pass == StrTri.No ? AvInk.State(AvState.Danger) : AvInk.Dim;
                r.Label.color = c.Pass == StrTri.Unknown ? AvInk.Dim : AvInk.Ink;
                r.Do.color = c.Pass == StrTri.No ? AvInk.Ink : AvInk.Dim;
            }
            Paint();
            Changed();
        }

        public override float Measure(float width) => Shown ? Lay(width, false) : 0f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (Shown) Lay(s.W, true);
        }

        private float Lay(float width, bool place)
        {
            StrBanner b = Vm != null ? Vm.Banner : null;
            if (b == null) return 0f;
            float y = Pad;
            float chipW = chip.WidthFor();
            bool count = b.CountSeconds >= 0;
            float countW = count ? Mathf.Max(StrFlap.Width, Mathf.Ceil(AvText.Width(countLabel))) : 0f;
            float headX = Pad + chipW + 8f, headW = Mathf.Max(40f, width - headX - Pad - (count ? countW + 8f : 0f));
            float headH = Mathf.Max(22f, AvText.Height(head, headW));
            float top = Mathf.Max(headH, count ? 15f + StrFlap.Height + 2f : 0f);
            if (place)
            {
                chip.Place(Pad, y + 2f, chipW, 18f);
                AvLay.Place(head, headX, y, headW, headH);
                AvLay.Place(countLabel, width - Pad - countW, y, countW, 15f);
                flap.Place(width - Pad - StrFlap.Width, y + 16f);
            }
            y += top;
            y = Block(whyTag, whyText, b.Why, y, width, place);
            y = Block(doTag, doText, b.Do, y, width, place);
            for (int i = 0; i < b.CheckCount && i < checks.Length; i++)
            {
                CheckRow r = checks[i];
                float textW = width - 2f * Pad - MarkW;
                float lh = Mathf.Max(16f, AvText.Height(r.Label, textW));
                bool hasDo = r.Do.text.Length > 0;
                float dh = hasDo ? AvText.Height(r.Do, textW) : 0f;
                y += 7f;
                if (place)
                {
                    AvLay.Place(r.Mark, Pad, y, MarkW, 18f);
                    AvLay.Place(r.Label, Pad + MarkW, y, textW, lh);
                    r.Do.gameObject.SetActive(hasDo);
                    if (hasDo) AvLay.Place(r.Do, Pad + MarkW, y + lh + 1f, textW, dh);
                }
                y += lh + (hasDo ? 1f + dh : 0f);
            }
            return y + Pad;
        }

        private float Block(TMP_Text tag, TMP_Text text, string value, float y, float width, bool place)
        {
            bool on = !string.IsNullOrEmpty(value);
            if (place)
            {
                tag.gameObject.SetActive(on);
                text.gameObject.SetActive(on);
            }
            if (!on) return y;
            float w = width - 2f * Pad - TagW;
            float h = Mathf.Max(16f, AvText.Height(text, w));
            y += 6f;
            if (place)
            {
                AvLay.Place(tag, Pad, y + 1f, TagW - 4f, 15f);
                AvLay.Place(text, Pad + TagW, y, w, h);
            }
            return y + h;
        }

        private void Paint()
        {
            Color c = StrInk.Of(ink);
            frame.Paint(c.WithAlpha(0.10f), c.WithAlpha(0.85f));
            head.color = AvInk.Ink;
            countLabel.color = AvInk.Dim;
            whyTag.color = doTag.color = AvInk.Key;
            whyText.color = doText.color = AvInk.Ink;
            flap.Restyle(StrInkRole.Effort);
        }

        public override void Restyle() => Paint();
    }

    /// <summary>
    /// CAUTIOUS, STEADY, BOLD with the rule the staff really applies printed under them, and REPLAN, the quiet two-press call-off. One instance sits above the
    /// tickets (no operation) and one below the lanes (an operation); the page plan shows exactly one, and only the shown one registers its ids.
    /// </summary>
    internal sealed class StrPostureRow : StrPart
    {
        private static readonly TheaterWarPosture[] Postures = { TheaterWarPosture.Cautious, TheaterWarPosture.Steady, TheaterWarPosture.Bold };
        private static readonly string[] Words = { "CAUTIOUS", "STEADY", "BOLD" };
        private const float Pad = 2f, ButtonH = 26f, ButtonW = 74f;

        private readonly bool bottom;
        private readonly TMP_Text label, caption;
        private readonly StrButton[] buttons = new StrButton[3];
        private readonly StrButton replan;
        private bool showReplan;

        internal StrPostureRow(RectTransform parent, StrControls controls, IStrHub hub, bool atBottom) : base(parent, atBottom ? "Posture (below)" : "Posture (above)", controls, hub)
        {
            bottom = atBottom;
            label = Text(Rect, "Label", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            label.text = "POSTURE";
            caption = Text(Rect, "Caption", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                buttons[i] = new StrButton(Rect, Words[i], AvButtonStyle.Default, () => Hub.Run(StrActionKind.Posture, StrItemId.None, (int)Postures[index]));
            }
            replan = new StrButton(Rect, "REPLAN", AvButtonStyle.Danger, () => Hub.Run(StrActionKind.Replan, Vm.Effort.Id));
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            StrPagePlan plan = StrPagePlan.For(vm.Mode);
            bool on = bottom ? plan.PostureBottom : plan.PostureTop;
            SetShown(on);
            if (!on) return;
            for (int i = 0; i < 3; i++)
            {
                TheaterWarPosture p = Postures[i];
                buttons[i].Bind(Controls, StrControlIds.Posture(p), Words[i], () => StrGate.Check(Vm, StrActionKind.Posture, StrItemId.None, (int)p),
                    TheaterWords.Posture(p), vm.Posture == p);
            }
            showReplan = vm.Effort.Live;
            replan.SetActive(showReplan);
            if (showReplan)
                replan.Bind(Controls, StrControlIds.Replan, StrPanelText.ReplanLabel(vm), () => Vm.Effort.Replan,
                    "Call the operation off. Press twice: the first press arms it for four seconds. The platform is vetoed for a while.");
            AvText.Set(caption, TheaterWords.Posture(vm.Posture));
            Changed();
        }

        public override float Measure(float width) => Shown ? ButtonH + 4f + Mathf.Max(15f, AvText.Height(caption, width - 2f * Pad)) + 2f : 0f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (!Shown) return;
            AvLay.Place(label, Pad, 0f, 56f, ButtonH);
            float x = Pad + 58f;
            for (int i = 0; i < 3; i++)
            {
                buttons[i].Place(x, 0f, ButtonW, ButtonH);
                x += ButtonW + 2f;
            }
            if (showReplan) replan.Place(s.W - Pad - 120f, 0f, 120f, ButtonH);
            AvLay.Place(caption, Pad, ButtonH + 4f, s.W - 2f * Pad, Mathf.Max(15f, AvText.Height(caption, s.W - 2f * Pad)));
        }

        public override void Restyle()
        {
            label.color = AvInk.Dim;
            caption.color = AvInk.Dim;
            foreach (StrButton b in buttons) b.Restyle();
            replan.Restyle();
        }
    }

    /// <summary>One of the staff's proposals as a ticket: plate (number, default or open, wave price), kind, place, risk, lane, aim, PICK and LOCATE.</summary>
    internal sealed class StrProposalTicket : StrPart
    {
        private const float Pad = 8f, PlateW = 56f, ButtonW = 88f, LaneH = 12f;

        private readonly int index;
        private readonly AvFrame frame;
        private readonly TMP_Text number, state, waveWord, wavePrice, name, risk, percent, aim;
        private readonly StrSlab kind;
        private readonly StrLaneBar lane;
        private readonly StrButton pick, locate;
        private StrProposalRow row;

        internal StrProposalTicket(RectTransform parent, StrControls controls, IStrHub hub, int ordinal) : base(parent, "Proposal " + (ordinal + 1), controls, hub)
        {
            index = ordinal;
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            number = Text(Rect, "Number", AvTextRole.Display, TextAlignmentOptions.Center);
            state = Text(Rect, "State", AvTextRole.Micro, TextAlignmentOptions.Center);
            waveWord = Text(Rect, "WaveWord", AvTextRole.Micro, TextAlignmentOptions.Center);
            wavePrice = Text(Rect, "WavePrice", AvTextRole.DataStrong, TextAlignmentOptions.Center);
            kind = new StrSlab(Rect, "Kind");
            name = Text(Rect, "Name", AvTextRole.Head, TextAlignmentOptions.MidlineLeft);
            risk = Text(Rect, "Risk", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            percent = Text(Rect, "val.percent", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
            aim = Text(Rect, "Aim", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            lane = new StrLaneBar(Rect, "Lane");
            pick = new StrButton(Rect, "PICK", AvButtonStyle.Primary, () => Hub.Run(StrActionKind.Pick, row.Id));
            locate = new StrButton(Rect, "LOCATE", AvButtonStyle.Default, () => Locate(row.Id));
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            bool on = index < vm.ProposalCount && StrPagePlan.For(vm.Mode).ProposalTickets;
            SetShown(on);
            if (!on) { row = null; return; }
            row = vm.Proposals[index];
            StrProposalRow r = row;
            AvText.Set(number, r.Tag);
            AvText.Set(state, StrPanelText.PlateState(r));
            StrPanelText.PlateWave(r, out string word, out string price);
            AvText.Set(waveWord, word);
            AvText.Set(wavePrice, price);
            kind.SetOutline(r.KindText, AvInk.Effort);
            AvText.Set(name, r.Name);
            AvText.Set(risk, StrPanelText.RiskChip(r));
            AvText.Set(percent, TheaterReadout.Percent(r.Pressure));
            AvText.Set(aim, r.Aim);
            lane.Set(r.Pressure, float.NaN);
            pick.Bind(Controls, StrControlIds.Prop(index + 1, "pick"), "PICK", () => Vm.Proposals[index].Pick,
                "Make this the main effort. The staff plans it and the AI ground force executes it.");
            locate.Bind(Controls, StrControlIds.Prop(index + 1, "locate"), "LOCATE", () => LocateGate(Vm.Proposals[index].Id),
                "Fly the map to this proposal.");
            Paint();
            Changed();
        }

        public override float Measure(float width) => Shown ? Lay(width, false) : 0f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (Shown) Lay(s.W, true);
        }

        private float Lay(float width, bool place)
        {
            float cx = Pad + PlateW + 8f, cw = width - cx - ButtonW - 2f * Pad;
            float aimH = Mathf.Max(15f, AvText.Height(aim, cw));
            float h = Mathf.Max(Pad + 20f + 6f + LaneH + 6f + aimH + Pad, Pad + 26f + 13f + 13f + 15f + Pad, 2f * Pad + 26f + 4f + 26f);
            if (!place) return h;
            AvLay.Place(number, Pad, Pad, PlateW, 26f);
            AvLay.Place(state, Pad, Pad + 26f, PlateW, 13f);
            AvLay.Place(waveWord, Pad, Pad + 39f, PlateW, 13f);
            AvLay.Place(wavePrice, Pad - 2f, Pad + 52f, PlateW + 4f, 15f);
            float kw = kind.WidthFor();
            kind.Place(cx, Pad + 1f, kw, 18f);
            float riskW = Mathf.Ceil(AvText.Width(risk)) + 6f;
            AvLay.Place(name, cx + kw + 6f, Pad, Mathf.Max(30f, cw - kw - 6f - riskW - 6f), 20f);
            AvLay.Place(risk, cx + cw - riskW, Pad, riskW, 20f);
            float pw = 50f;
            lane.Place(cx, Pad + 20f + 6f + StrLaneBar.PadTop - 4f, cw - pw - 6f, LaneH);
            AvLay.Place(percent, cx + cw - pw, Pad + 20f + 3f, pw, 22f);
            AvLay.Place(aim, cx, Pad + 20f + 6f + LaneH + 8f, cw, aimH);
            pick.Place(width - Pad - ButtonW, Pad, ButtonW, 26f);
            locate.Place(width - Pad - ButtonW, Pad + 30f, ButtonW, 26f);
            return h;
        }

        private void Paint()
        {
            bool rec = row != null && row.Recommended;
            frame.Paint(AvInk.Inert, rec ? AvInk.Effort.WithAlpha(0.7f) : AvInk.Hairline);
            frame.Bracket = rec ? 6f : 0f;
            frame.BracketColor = AvInk.Effort.WithAlpha(0.7f);
            number.color = rec ? AvInk.Effort : AvInk.Ink;
            state.color = AvInk.Dim;
            waveWord.color = AvInk.Dim;
            wavePrice.color = row != null && row.WaveAffordable == StrTri.No ? AvInk.State(AvState.Caution) : AvInk.Ink;
            name.color = AvInk.Ink;
            percent.color = AvInk.Ink;
            aim.color = AvInk.Dim;
            risk.color = row == null ? AvInk.Dim : row.Risk == TheaterRisk.High ? AvInk.State(AvState.Danger) : row.Risk == TheaterRisk.Moderate ? AvInk.State(AvState.Caution) : AvInk.Key;
            if (row != null) kind.SetOutline(row.KindText, AvInk.Effort);
        }

        public override void Restyle()
        {
            Paint();
            lane.Restyle();
            pick.Restyle();
            locate.Restyle();
        }
    }

    /// <summary>A compact platform lane (P1..P8): tag, place, status, the tug-of-war bar, share and trend, and REDIRECT or the reason there is no offer. A lane is also a locate target.</summary>
    internal sealed class StrFrontLane : StrPart
    {
        private const float Pad = 8f, TagW = 38f, ButtonW = 108f, LaneW = 88f, PercentW = 52f;

        private readonly int index;
        private readonly AvFrame frame;
        private readonly TMP_Text tag, name, status, percent, trend, offer;
        private readonly StrLaneBar lane;
        private readonly StrButton redirect;
        private StrFrontRow row;
        private bool placeholder, hasButton;
        private StrInkRole ink = StrInkRole.Muted;

        internal StrFrontLane(RectTransform parent, StrControls controls, IStrHub hub, int ordinal) : base(parent, "Lane " + (ordinal + 1), controls, hub)
        {
            index = ordinal;
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            frame.raycastTarget = true;
            AvHit hit = AvHit.On(frame);
            hit.Click = e => { StrFrontRow f = placeholder ? null : LaneAt(Vm); if (f != null && LocateGate(f.Id).Allowed) Locate(f.Id); };
            tag = Text(Rect, "Tag", AvTextRole.DataStrong, TextAlignmentOptions.Center);
            name = Text(Rect, "Name", AvTextRole.Head, TextAlignmentOptions.MidlineLeft);
            status = Text(Rect, "Status", AvTextRole.ProseSmall, TextAlignmentOptions.MidlineLeft);
            percent = Text(Rect, "val.percent", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
            trend = Text(Rect, "Trend", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            offer = Text(Rect, "Offer", AvTextRole.ProseSmall, TextAlignmentOptions.Center);
            lane = new StrLaneBar(Rect, "Lane");
            redirect = new StrButton(Rect, "REDIRECT", AvButtonStyle.Quiet, () => Hub.Run(StrActionKind.Pick, row.Offer));
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            StrPagePlan plan = StrPagePlan.For(vm.Mode);
            placeholder = plan.Placeholders && index < 2;
            row = placeholder || !plan.Lanes ? null : LaneAt(vm);
            bool on = placeholder || row != null;
            SetShown(on);
            if (!on) return;
            if (placeholder)
            {
                ink = StrInkRole.Muted;
                AvText.Set(tag, "");
                AvText.Set(name, StrPagePlan.PlaceholderText(vm.Mode));
                AvText.Set(status, "");
                AvText.Set(percent, StrWords.Dash);
                AvText.Set(trend, "");
                AvText.Set(offer, "");
                lane.Set(float.NaN, float.NaN);
                hasButton = false;
                redirect.SetActive(false);
            }
            else
            {
                StrFrontRow r = row;
                ink = r.Ink;
                AvText.Set(tag, r.Tag);
                AvText.Set(name, r.Name);
                AvText.Set(status, r.StatusText);
                AvText.Set(percent, r.PressureText);
                AvText.Set(trend, r.TrendText);
                AvText.Set(offer, r.OfferText);
                lane.Set(r.Pressure, r.Trend);
                // REDIRECT while an operation lives and the staff offers this platform; otherwise the offer word or the reason there is none.
                hasButton = vm.Effort.Live && !r.Offer.IsNone;
                redirect.SetActive(hasButton);
                if (hasButton)
                    redirect.Bind(Controls, StrControlIds.Front(index + 1, "redirect"), "REDIRECT", () => { StrFrontRow f = LaneAt(Vm); return f != null ? f.Redirect : Gone; },
                        "Make this the main effort instead: the operation moves to this platform.");
                if (StrAnchors.TryGet(vm, r.Id, out _, out _))
                    Controls.Register(StrControlIds.Front(index + 1, "locate"), () => { StrFrontRow f = LaneAt(Vm); return f != null ? LocateGate(f.Id) : Gone; },
                        () => { StrFrontRow f = LaneAt(Vm); return f != null ? Locate(f.Id) : Gone; });
            }
            Paint();
            Changed();
        }

        private static readonly StrAct Gone = new StrAct(StrWhy.Gone, "THIS PLATFORM IS NO LONGER ON THE BOARD");

        /// <summary>The nth lane the plan keeps: the proposals board hides the fronts its tickets already carry.</summary>
        private StrFrontRow LaneAt(StrViewModel vm)
        {
            int seen = 0;
            for (int i = 0; i < vm.FrontCount; i++)
            {
                if (!StrPagePlan.ShowsLane(vm.Mode, vm.Fronts[i])) continue;
                if (seen++ == index) return vm.Fronts[i];
            }
            return null;
        }

        public override float Measure(float width) => !Shown ? 0f : placeholder ? 34f : 52f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (!Shown) return;
            float h = s.H;
            if (placeholder)
            {
                AvLay.Place(name, Pad + 6f, 0f, s.W - 2f * Pad - 60f, h);
                AvLay.Place(percent, s.W - Pad - 52f, 0f, 52f, h);
                lane.Place(s.W * 0.5f, h * 0.5f - 4f, 100f, 10f);
                return;
            }
            AvLay.Place(tag, Pad, (h - 24f) * 0.5f, TagW, 24f);
            float nx = Pad + TagW + 8f, right = s.W - Pad - ButtonW;
            float laneX = right - 8f - PercentW - 6f - LaneW;
            AvLay.Place(name, nx, 6f, Mathf.Max(30f, laneX - nx - 8f), 20f);
            AvLay.Place(status, nx, 26f, Mathf.Max(30f, laneX - nx - 8f), 18f);
            lane.Place(laneX, h * 0.5f - 4f, LaneW, 10f);
            AvLay.Place(percent, laneX + LaneW + 6f, 7f, PercentW, 20f);
            AvLay.Place(trend, laneX + LaneW - 30f, 28f, PercentW + 30f, 16f);
            if (hasButton)
            {
                redirect.Place(right, 4f, ButtonW, 22f);
                AvLay.Place(offer, right, 28f, ButtonW, 18f);
            }
            else AvLay.Place(offer, right, 6f, ButtonW, h - 12f);
        }

        private void Paint()
        {
            Color c = StrInk.Of(ink);
            frame.Paint(AvInk.Inert, placeholder ? AvInk.Hairline : c.WithAlpha(0.45f));
            tag.color = AvInk.Ink;
            name.color = placeholder ? AvInk.Dim : AvInk.Ink;
            status.color = ink == StrInkRole.Muted || ink == StrInkRole.Own ? AvInk.Dim : c;
            percent.color = AvInk.Ink;
            trend.color = AvInk.Dim;
            offer.color = AvInk.Dim;
        }

        public override void Restyle()
        {
            Paint();
            lane.Restyle();
            redirect.Restyle();
        }
    }
}
