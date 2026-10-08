using System;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The right rail every front room shares: FRONT STATUS (readiness pips, superiority bar, budget, priority, allocation),
    /// DIRECTIVE (segmented posture, lock line, focus pin), PROGRAMMES (queue with donate buttons, the available list),
    /// PERKS (the five rungs of this front) and the radio LOG. Fixed geometry; only the content moves. It owns no policy:
    /// every press is a request to <see cref="IFrontActions"/>.
    /// </summary>
    internal sealed class FrontRail
    {
        public const float StatusH = 160f, DirectiveH = 104f, ProgrammesH = 340f, PerksH = 150f, Gap = 5f;
        private const int Slots = FrontRules.MaxQueue, Offers = 4, Rungs = 5, LogLines = 6;
        private static readonly string[] ReadyWords = { "", "BASELINE", "ALERTED", "OPERATIONAL", "ADVANCED", "FULL SPECTRUM" };
        private static readonly string[] Bands = { "LOST", "BEHIND", "CONTESTED", "AHEAD", "DOMINANT" };

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private readonly float w;
        private Front front;

        // Status.
        private readonly FrontBox status;
        private readonly Image[] pipWell = new Image[5], pipFill = new Image[5];
        private readonly TMP_Text readyWord, supValue, budgetValue, priorityValue, allocValue;
        private readonly Image supWell, supFill, supNeedle;
        private readonly TMP_Text[] bandWord = new TMP_Text[5];
        private readonly AvControl priMinus, priPlus;
        private readonly float barX, barW;

        // Directive.
        private readonly FrontBox directive;
        private readonly AvControl[] posture = new AvControl[3];
        private readonly TMP_Text lockLine, postureLine, focusLine;
        private readonly AvControl focusButton;
        private readonly FrontDirective[] postureId = new FrontDirective[3];

        // Programmes.
        private readonly FrontBox programmes;
        private readonly TMP_Text queueHint, offerHint;
        private readonly QueueSlot[] slots = new QueueSlot[Slots];
        private readonly OfferRow[] offers = new OfferRow[Offers];

        // Perks and log.
        private readonly FrontBox perks, log;
        private readonly PerkRow[] perkRows = new PerkRow[Rungs];
        private readonly TMP_Text[] logRows = new TMP_Text[LogLines];
        private readonly TMP_Text[] keys;

        public FrontRail(RectTransform parent, FrontSkin skin, IFrontActions actions, float x, float y, float width, float height)
        {
            this.actions = actions;
            this.skin = skin;
            w = width;
            float cy = y;
            var keyList = new System.Collections.Generic.List<TMP_Text>();

            // ---- FRONT STATUS ----
            status = new FrontBox(parent, skin, "Status", x, cy, w, StatusH, "FRONT STATUS", AvIcon.Gauge);
            RectTransform s = status.Rect;
            keyList.Add(Key(s, "READINESS", 10f, 30f, 92f, 20f));
            for (int i = 0; i < 5; i++)
            {
                pipWell[i] = FrontKit.Solid(s, "PipWell" + i, 104f + i * 56f, 32f, 52f, 16f, Color.clear);
                pipFill[i] = FrontKit.Solid(s, "PipFill" + i, 106f + i * 56f, 34f, 48f, 12f, Color.clear);
            }
            readyWord = FrontKit.Mono(s, "ReadyWord", 388f, 30f, w - 396f, 20f, 11.5f, TextAlignmentOptions.MidlineRight, true);

            keyList.Add(Key(s, "SUPERIORITY", 10f, 56f, 92f, 14f));
            supValue = FrontKit.Mono(s, "SupValue", 10f, 69f, 92f, 30f, 20f, TextAlignmentOptions.MidlineLeft, true);
            barX = 104f; barW = w - 114f;
            supWell = FrontKit.Solid(s, "SupWell", barX, 58f, barW, 20f, Color.clear);
            supFill = FrontKit.Solid(s, "SupFill", barX + barW * 0.5f, 60f, 0f, 16f, Color.clear);
            for (int i = 1; i < 5; i++) BandRule(s, barX + barW * i / 5f);
            supNeedle = FrontKit.Solid(s, "SupNeedle", barX + barW * 0.5f - 1f, 55f, 3f, 26f, Color.clear);
            for (int i = 0; i < 5; i++)
            {
                bandWord[i] = FrontKit.Mono(s, "Band" + i, barX + barW * i / 5f, 80f, barW / 5f, 14f, 10f, TextAlignmentOptions.Midline, false, 1f);
                bandWord[i].text = Bands[i];
            }

            float cell = (w - 20f) / 3f;
            for (int i = 0; i < 3; i++) Rule(s, 10f + i * cell, 102f, 1f, 48f);
            keyList.Add(Key(s, "FRONT BUDGET", 12f, 101f, cell - 8f, 14f));
            budgetValue = FrontKit.Mono(s, "BudgetValue", 12f, 116f, cell - 8f, 26f, 18f, TextAlignmentOptions.MidlineLeft, true);
            keyList.Add(Key(s, "FUNDING SHARE", 18f + cell, 101f, cell - 8f, 14f));
            priorityValue = FrontKit.Mono(s, "PriorityValue", 18f + cell, 116f, cell - 70f, 26f, 18f, TextAlignmentOptions.MidlineLeft, true);
            priMinus = Button(s, "-", 18f + cell + cell - 62f, 118f, 26f, 22f, () => actions.SetPriority(front, -10), AvButtonStyle.Quiet);
            priPlus = Button(s, "+", 18f + cell + cell - 32f, 118f, 26f, 22f, () => actions.SetPriority(front, +10), AvButtonStyle.Quiet);
            priMinus.Help = "Take 10 points of the faction funding share off this front. Any member; a 60 s lock follows.";
            priPlus.Help = "Add 10 points of the faction funding share to this front. Any member; a 60 s lock follows.";
            keyList.Add(Key(s, "YOUR ALLOCATION", 24f + 2f * cell, 101f, cell - 8f, 14f));
            allocValue = FrontKit.Mono(s, "AllocValue", 24f + 2f * cell, 116f, cell - 8f, 26f, 18f, TextAlignmentOptions.MidlineLeft, true);
            cy += StatusH + Gap;

            // ---- DIRECTIVE ----
            directive = new FrontBox(parent, skin, "Directive", x, cy, w, DirectiveH, "DIRECTIVE", AvIcon.Target);
            RectTransform d = directive.Rect;
            float pw = (w - 20f - 2f * 6f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                posture[i] = Button(d, "-", 10f + i * (pw + 6f), 28f, pw, 30f, () => actions.SetDirective(front, postureId[slot]), AvButtonStyle.Default);
            }
            lockLine = FrontKit.Mono(d, "Lock", 10f, 62f, w * 0.5f, 18f, 11f, TextAlignmentOptions.MidlineLeft);
            focusLine = FrontKit.Mono(d, "Focus", w * 0.5f, 62f, w * 0.5f - 118f, 18f, 11f, TextAlignmentOptions.MidlineRight);
            focusButton = Button(d, "PIN FOCUS", w - 108f, 62f, 98f, 20f, () => actions.SetFocus(front), AvButtonStyle.Quiet);
            focusButton.Help = "Pin the director's attention to the last map click (one pin per front).";
            postureLine = FrontKit.Mono(d, "Posture", 10f, 84f, w - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            cy += DirectiveH + Gap;

            // ---- PROGRAMMES ----
            programmes = new FrontBox(parent, skin, "Programmes", x, cy, w, ProgrammesH, "PROGRAMMES", AvIcon.Stack2);
            RectTransform pr = programmes.Rect;
            queueHint = FrontKit.Mono(pr, "QueueHint", 10f, 25f, w - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, false, 1f);
            for (int i = 0; i < Slots; i++) slots[i] = new QueueSlot(this, pr, i, 8f, 43f + i * 42f, w - 16f, 40f);
            offerHint = FrontKit.Mono(pr, "OfferHint", 10f, 213f, w - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, false, 1f);
            for (int i = 0; i < Offers; i++) offers[i] = new OfferRow(this, pr, 8f, 231f + i * 27f, w - 16f, 26f);
            cy += ProgrammesH + Gap;

            // ---- PERKS ----
            perks = new FrontBox(parent, skin, "Perks", x, cy, w, PerksH, "PERKS · PILOT ALLOCATION", AvIcon.Star);
            for (int i = 0; i < Rungs; i++) perkRows[i] = new PerkRow(perks.Rect, skin, 8f, 27f + i * 24f, w - 16f, 23f);
            cy += PerksH + Gap;

            // ---- LOG ----
            log = new FrontBox(parent, skin, "Log", x, cy, w, y + height - cy, "RADIO LOG", AvIcon.Radio);
            for (int i = 0; i < LogLines; i++)
                logRows[i] = FrontKit.Mono(log.Rect, "Log" + i, 10f, 27f + i * 17f, w - 20f, 17f, 11f, TextAlignmentOptions.MidlineLeft);

            keys = keyList.ToArray();
            skin.Add(Restyle);
        }

        private TMP_Text Key(RectTransform p, string text, float x, float y, float kw, float kh)
        {
            TMP_Text t = FrontKit.Mono(p, "Key", x, y, kw, kh, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
            t.text = text;
            return t;
        }

        private void BandRule(RectTransform p, float px)
        {
            Image i = FrontKit.Solid(p, "BandRule", px, 58f, 1f, 20f, Color.clear);
            skin.Add(() => i.color = AvInk.Frame.WithAlpha(0.8f));
        }

        private void Rule(RectTransform p, float px, float py, float pw, float ph)
        {
            Image i = FrontKit.Solid(p, "Rule", px, py, pw, ph, Color.clear);
            skin.Add(() => i.color = AvInk.Hairline);
        }

        private AvControl Button(RectTransform p, string label, float x, float y, float bw, float bh, Action click, AvButtonStyle style)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, () => { actions.Touch(); click(); }, style));
            AvLay.Place(c.Rect, x, y, bw, bh);
            c.SingleLine();
            return c;
        }

        private void Restyle()
        {
            for (int i = 0; i < 5; i++) pipWell[i].color = AvInk.Ground.WithAlpha(0.9f);
            supWell.color = AvInk.Ground.WithAlpha(0.92f);
            supNeedle.color = AvInk.Ink;
            foreach (TMP_Text k in keys) k.color = AvInk.Dim;
            queueHint.color = AvInk.Dim;
            offerHint.color = AvInk.Dim;
            postureLine.color = AvInk.Dim;
        }

        // ---- Paint ---------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            front = v.Front;
            FrontRow r = v.Row;

            // Readiness.
            int ready = Mathf.Clamp(r.Readiness, 1, 5);
            for (int i = 0; i < 5; i++)
                pipFill[i].color = i < ready ? (ready >= 4 ? AvInk.State(AvState.Ready) : AvInk.Select) : Color.clear;
            FrontKit.Set(readyWord, "R" + ready + " · " + ReadyWords[ready], ready >= 4 ? FrontKit.Tone(AvState.Ready) : AvInk.Ink);

            // Superiority.
            int sup = r.Superiority;
            string word = FrontSuperiority.Word(sup);
            AvState tone = sup >= 20 ? AvState.Ready : sup > -20 ? AvState.Info : sup > -60 ? AvState.Caution : AvState.Danger;
            Color ink = FrontKit.Tone(tone);
            FrontKit.Set(supValue, FrontKit.Signed(sup), ink);
            float half = barW * 0.5f, len = Mathf.Abs(sup) / 100f * half;
            supFill.color = AvInk.State(tone).WithAlpha(0.85f);
            AvLay.Place(supFill.rectTransform, barX + half + (sup >= 0 ? 0f : -len), 60f, len, 16f);
            AvLay.Place(supNeedle.rectTransform, barX + half + sup / 100f * half - 1.5f, 55f, 3f, 26f);
            for (int i = 0; i < 5; i++)
            {
                bool on = Bands[i] == word;
                bandWord[i].color = on ? ink : AvInk.Dim;
                bandWord[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            }

            FrontKit.Set(budgetValue, "¤ " + r.Budget, AvInk.Ink);
            FrontKit.Set(priorityValue, r.PriorityPct + " %", AvInk.Ink);
            FrontKit.Set(allocValue, v.Allocation + " AP", AvInk.Ink);
            OpsKit.Enable(priMinus, r.PriorityPct >= 10);
            OpsKit.Enable(priPlus, r.PriorityPct <= 90);

            // Directive.
            FrontDirective[] set = Postures(front);
            for (int i = 0; i < 3; i++)
            {
                postureId[i] = set[i];
                posture[i].Label = FrontRules.Name(set[i]);
                posture[i].Latched = r.Directive == set[i];
            }
            float left = r.DirectiveLockUntil - v.Now;
            if (left > 0f) FrontKit.Set(lockLine, "LOCKED " + FrontKit.Clock(left) + " · SET BY " + (string.IsNullOrEmpty(r.DirectiveBy) ? "?" : r.DirectiveBy), FrontKit.Tone(AvState.Caution));
            else FrontKit.Set(lockLine, "POSTURE FREE · ANY MEMBER MAY SET", FrontKit.Tone(AvState.Ready));
            FrontKit.Set(focusLine, r.HasFocus ? "FOCUS PIN " + v.FocusGrid : "NO FOCUS PIN", r.HasFocus ? AvInk.Ink : AvInk.Dim);
            FrontKit.Set(postureLine, "> " + Describe(front, r.Directive), AvInk.Dim);

            // Programmes.
            FrontKit.Set(queueHint, "QUEUE " + v.Queue.Count + "/" + FrontRules.MaxQueue + " · ONE BUILDS AT A TIME · DONATE FROM YOUR ALLOCATION", AvInk.Dim);
            FrontKit.Set(offerHint, "AVAILABLE · COSTS IN FRONT FUNDS · BUILD CLOCK AT RIGHT OF COST", AvInk.Dim);
            for (int i = 0; i < Slots; i++) slots[i].Paint(i < v.Queue.Count ? (FrontQueueView?)v.Queue[i] : null, v.Allocation);
            for (int i = 0; i < Offers; i++) offers[i].Paint(i < v.Offers.Count ? (FrontOfferView?)v.Offers[i] : null);

            // Perks.
            for (int i = 0; i < Rungs; i++) perkRows[i].Paint(i < v.Perks.Count ? (FrontPerkView?)v.Perks[i] : null);

            // Log: newest last.
            int from = Mathf.Max(0, v.Log.Count - LogLines);
            for (int i = 0; i < LogLines; i++)
            {
                int li = from + i;
                if (li >= v.Log.Count) { FrontKit.Set(logRows[i], "", AvInk.Dim); continue; }
                FrontLogLine l = v.Log[li];
                float room = w - 20f - C2Kit.Width(logRows[i], l.Stamp + "  ");
                string line = C2Kit.Tint(l.Stamp, AvInk.Dim) + "  " + C2Kit.Tint(C2Kit.FitTo(logRows[i], l.Text, room), FrontKit.Tone(l.Tone == AvState.Inert ? AvState.Info : l.Tone));
                FrontKit.Set(logRows[i], line, AvInk.Ink);
            }
            status.SetMeta(FrontRules.Name(front) + " FRONT", AvInk.Dim);
            directive.SetMeta(FrontRules.Name(r.Directive) + " ACTIVE", AvInk.Dim);
            programmes.SetMeta(v.Queue.Count + " QUEUED", AvInk.Dim);
            perks.SetMeta("READINESS R" + ready, AvInk.Dim);
            log.SetMeta(v.Callsign, AvInk.Dim);
        }

        private static FrontDirective[] Postures(Front f) =>
            f == Front.Space ? new[] { FrontDirective.Recon, FrontDirective.Strike, FrontDirective.Defend }
            : f == Front.Cyber ? new[] { FrontDirective.Defend, FrontDirective.Balanced, FrontDirective.Attack }
            : new[] { FrontDirective.Recon, FrontDirective.Sabotage, FrontDirective.Hold };

        private static string Describe(Front f, FrontDirective d)
        {
            switch (f)
            {
                case Front.Space:
                    return d == FrontDirective.Recon ? "Radar and optical passes over the front; contacts reveal themselves."
                        : d == FrontDirective.Strike ? "Keeps the kinetic bird ready and marks high-value targets."
                        : "Hardens uplinks, repairs them first and rephases to dodge ASAT.";
                case Front.Cyber:
                    return d == FrontDirective.Defend ? "Hunts intrusions and protects the data centers."
                        : d == FrontDirective.Balanced ? "Splits effort between hunting intrusions and probing SAM nodes."
                        : "Hops SAM and radar nodes and burns them near friendly pilots.";
                default:
                    return d == FrontDirective.Recon ? "Teams sweep the ground near the front."
                        : d == FrontDirective.Sabotage ? "Hunts enemy anchors: uplinks, trucks, launch sites."
                        : "Seizes buildings and keeps them.";
            }
        }

        // ---- Rows ----------------------------------------------------------------------------------------------------

        private sealed class QueueSlot
        {
            private readonly AvFrame frame;
            private readonly TMP_Text name, state, pct, note;
            private readonly FrontBar bar;
            private readonly AvControl d25, d100;
            private readonly int index;
            private FrontQueueView cur;

            public QueueSlot(FrontRail rail, RectTransform p, int index, float x, float y, float width, float height)
            {
                this.index = index;
                RectTransform root = AvLay.Child(p, "Slot " + index);
                AvLay.Place(root, x, y, width, height);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, width, height, 0f, 0f);
                name = FrontKit.Mono(root, "Name", 8f, 2f, 270f, 17f, 12f, TextAlignmentOptions.MidlineLeft, true);
                state = FrontKit.Mono(root, "State", 280f, 1f, width - 288f, 16f, 11f, TextAlignmentOptions.MidlineRight, true);
                bar = new FrontBar(root, rail.skin, 8f, 25f, 250f, 10f);
                pct = FrontKit.Mono(root, "Pct", 264f, 20f, 130f, 18f, 11f, TextAlignmentOptions.MidlineLeft, true);
                note = FrontKit.Mono(root, "Note", 400f, 20f, width - 408f, 18f, 10.5f, TextAlignmentOptions.MidlineRight);
                d25 = rail.Button(root, "DONATE 25", width - 190f, 19f, 90f, 19f, () => rail.actions.Donate(rail.front, cur.Id, 25), AvButtonStyle.Default);
                d100 = rail.Button(root, "DONATE 100", width - 96f, 19f, 90f, 19f, () => rail.actions.Donate(rail.front, cur.Id, 100), AvButtonStyle.Default);
                d25.Help = "Spend 25 of your allocation on this programme's funding bar.";
                d100.Help = "Spend 100 of your allocation on this programme's funding bar.";
                rail.skin.Add(() => frame.Paint(AvInk.Ground.WithAlpha(0.55f), AvInk.Hairline));
            }

            public void Paint(FrontQueueView? q, int allocation)
            {
                if (!q.HasValue)
                {
                    FrontKit.Set(name, "SLOT " + (index + 1) + " · FREE", AvInk.Dim);
                    FrontKit.Set(state, index == 0 ? "QUEUE A PROGRAMME BELOW" : "", AvInk.Dim);
                    FrontKit.Set(pct, "NO PROGRAMME", AvInk.Dim);
                    FrontKit.Set(note, "", AvInk.Dim);
                    bar.Show(true);
                    bar.Set(0f, AvInk.Dim);
                    d25.Rect.gameObject.SetActive(false); d100.Rect.gameObject.SetActive(false);
                    return;
                }
                cur = q.Value;
                bar.Show(true);
                bool head = index == 0;
                FrontKit.Set(name, cur.Name, AvInk.Ink);
                if (cur.Building)
                {
                    FrontKit.Set(state, "BUILDING · " + FrontKit.Clock(cur.BuildLeft) + " LEFT", FrontKit.Tone(AvState.Caution));
                    bar.Set(cur.Progress, AvInk.State(AvState.Caution));
                    FrontKit.Set(pct, Mathf.RoundToInt(cur.Progress * 100f) + " % BUILT", FrontKit.Tone(AvState.Caution));
                    FrontKit.Set(note, "PAID ¤ " + Mathf.RoundToInt(cur.Cost), AvInk.Dim);
                    d25.Rect.gameObject.SetActive(false); d100.Rect.gameObject.SetActive(false);
                    return;
                }
                FrontKit.Set(state, (head ? "FUNDING" : "WAITING") + " · ¤ " + Mathf.RoundToInt(cur.Cost * (1f - cur.Progress)) + " TO GO", head ? FrontKit.Tone(AvState.Info) : AvInk.Dim);
                bar.Set(cur.Progress, AvInk.State(AvState.Info));
                FrontKit.Set(pct, Mathf.RoundToInt(cur.Progress * 100f) + " % FUNDED", FrontKit.Tone(AvState.Info));
                FrontKit.Set(note, "", AvInk.Dim);
                d25.Rect.gameObject.SetActive(true); d100.Rect.gameObject.SetActive(true);
                OpsKit.Enable(d25, allocation >= 25);
                OpsKit.Enable(d100, allocation >= 100);
            }
        }

        private sealed class OfferRow
        {
            private readonly AvFrame frame;
            private readonly TMP_Text name, cost, build, why;
            private readonly AvControl add;
            private FrontOfferView cur;

            public OfferRow(FrontRail rail, RectTransform p, float x, float y, float width, float height)
            {
                RectTransform root = AvLay.Child(p, "Offer");
                AvLay.Place(root, x, y, width, height);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, width, height, 0f, 0f);
                name = FrontKit.Mono(root, "Name", 8f, 0f, 180f, height, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                cost = FrontKit.Mono(root, "Cost", 190f, 0f, 62f, height, 11.5f, TextAlignmentOptions.MidlineRight);
                build = FrontKit.Mono(root, "Build", 258f, 0f, 40f, height, 11f, TextAlignmentOptions.MidlineRight);
                why = FrontKit.Mono(root, "Why", 306f, 0f, width - 314f, height, 10.5f, TextAlignmentOptions.MidlineRight);
                add = rail.Button(root, "QUEUE", width - 82f, 3f, 74f, height - 6f, () => rail.actions.Queue(rail.front, cur.Id), AvButtonStyle.Primary);
                add.Help = "Queue this programme. Front funds pay for it automatically; donate allocation to speed it up.";
                rail.skin.Add(() => frame.Paint(AvInk.Ground.WithAlpha(0.4f), AvInk.Hairline.WithAlpha(0.6f)));
            }

            public void Paint(FrontOfferView? o)
            {
                if (!o.HasValue)
                {
                    frame.gameObject.SetActive(false); add.Rect.gameObject.SetActive(false);
                    name.text = cost.text = build.text = why.text = "";
                    return;
                }
                frame.gameObject.SetActive(true);
                cur = o.Value;
                bool ok = !cur.Queued && string.IsNullOrEmpty(cur.Refusal);
                FrontKit.Set(name, cur.Name, cur.Queued ? AvInk.Dim : AvInk.Ink);
                FrontKit.Set(cost, "¤ " + Mathf.RoundToInt(cur.Cost), AvInk.Dim);
                FrontKit.Set(build, FrontKit.Clock(cur.BuildSeconds), AvInk.Dim);
                add.Rect.gameObject.SetActive(ok);
                string reason = cur.Queued ? "IN QUEUE" : cur.Refusal ?? "";
                FrontKit.Set(why, ok ? "" : C2Kit.FitTo(why, reason, why.rectTransform.rect.width), cur.Queued ? FrontKit.Tone(AvState.Info) : FrontKit.Tone(AvState.Caution));
            }
        }

        private sealed class PerkRow
        {
            private readonly AvFrame chip;
            private readonly TMP_Text rung, name, effect, state;
            private bool unlocked;

            public PerkRow(RectTransform p, FrontSkin skin, float x, float y, float width, float height)
            {
                RectTransform root = AvLay.Child(p, "Perk");
                AvLay.Place(root, x, y, width, height);
                chip = FrontKit.Panel(root, "Chip", 0f, 2f, 28f, height - 4f, 3f, 0f);
                rung = FrontKit.Mono(root, "Rung", 0f, 2f, 28f, height - 4f, 11f, TextAlignmentOptions.Midline, true);
                name = FrontKit.Mono(root, "Name", 36f, 0f, 150f, height, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                effect = FrontKit.Mono(root, "Effect", 188f, 0f, 258f, height, 10.5f, TextAlignmentOptions.MidlineLeft);
                state = FrontKit.Mono(root, "State", width - 100f, 0f, 100f, height, 10.5f, TextAlignmentOptions.MidlineRight, true);
                skin.Add(Restyle);
            }

            private void Restyle() =>
                chip.Paint(unlocked ? AvInk.State(AvState.Ready).WithAlpha(0.25f) : AvInk.Ground.WithAlpha(0.6f), unlocked ? AvInk.State(AvState.Ready) : AvInk.Frame);

            public void Paint(FrontPerkView? pv)
            {
                if (!pv.HasValue) return;
                FrontPerkView v = pv.Value;
                if (unlocked != v.Unlocked) { unlocked = v.Unlocked; Restyle(); }
                FrontKit.Set(rung, "R" + v.Rung, v.Unlocked ? FrontKit.Tone(AvState.Ready) : AvInk.Dim);
                FrontKit.Set(name, v.Name, v.Unlocked ? AvInk.Ink : AvInk.Dim);
                bool gated = v.Unlocked && !string.IsNullOrEmpty(v.Gate);
                FrontKit.Set(effect, C2Kit.FitTo(effect, gated ? v.Gate : v.Effect, effect.rectTransform.rect.width), gated ? FrontKit.Tone(AvState.Caution) : AvInk.Dim);
                FrontKit.Set(state, gated ? "RELOCATE" : v.Unlocked ? "OPEN · " + v.Price + " AP" : "LOCKED · R" + v.Rung, gated ? FrontKit.Tone(AvState.Caution) : v.Unlocked ? FrontKit.Tone(AvState.Ready) : FrontKit.Tone(AvState.Caution));
            }
        }
    }
}
