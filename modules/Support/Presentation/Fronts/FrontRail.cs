using System;
using System.Globalization;
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
    /// The right rail every front room shares: FRONT STATUS (readiness pips, superiority bar, front funds, your allocation),
    /// STRATEGY (segmented posture with its lock, the faction-wide funding split with a preview of both nudges and its lock),
    /// PROGRAMMES (one active programme with FUNDING and BUILDING as two named phases, compact backlog, the offers),
    /// PERKS (the five rungs of this front: AVAILABLE or LOCKED and why) and the dated radio LOG. Fixed geometry; the backlog and the
    /// offers share one block whose rows take the room the queue leaves. It owns no policy: every press is a request to <see cref="IFrontActions"/>.
    /// </summary>
    internal sealed class FrontRail
    {
        public const float StatusH = 146f, DirectiveH = 170f, ProgrammesH = 318f, PerksH = 130f, Gap = 5f;
        private const int Offers = 4, Rungs = 8, LogLines = 5, Backlog = FrontRules.MaxQueue - 1; // five log lines fit the log box under the shared footer; the perk box takes up to 8 rows (SPACE has 7)
        private const int DonateSmall = 25, DonateLarge = 100;
        private const float CardY = 28f, CardH = 92f, BacklogH = 24f, OfferHintH = 14f;
        private static readonly string[] ReadyWords = { "", "BASELINE", "ALERTED", "OPERATIONAL", "ADVANCED", "FULL SPECTRUM" };
        private static readonly string[] Bands = { "LOST", "BEHIND", "CONTESTED", "AHEAD", "DOMINANT" };
        private static readonly string Rate = FrontRules.DonateRate.ToString("0.##", CultureInfo.InvariantCulture);

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private readonly float w;
        private Front front;

        // Status.
        private readonly FrontBox status;
        private readonly Image[] pipWell = new Image[5], pipFill = new Image[5];
        private readonly TMP_Text readyWord, supValue, budgetValue, budgetNote, allocValue, rateNote;
        private readonly Image supWell, supFill, supNeedle;
        private readonly TMP_Text[] bandWord = new TMP_Text[5];
        private readonly float barX, barW;

        // Strategy: posture, then the funding split.
        private readonly FrontBox directive;
        private readonly AvControl[] posture = new AvControl[3];
        private readonly TMP_Text lockLine, postureLine, focusLine, shareLock, previewLess, previewMore;
        private readonly AvControl focusButton, priMinus, priPlus;
        private readonly FrontDirective[] postureId = new FrontDirective[3];
        private readonly ShareCell[] shareCell = new ShareCell[3];
        private readonly int[] shares = new int[3], lessShares = new int[3], moreShares = new int[3];
        private string priorityBy = "";
        private float priorityUntil;

        // Programmes.
        private readonly FrontBox programmes;
        private readonly TMP_Text offerHint;
        private readonly Image[] queuePip = new Image[FrontRules.MaxQueue];
        private readonly ActiveCard active;
        private readonly BacklogRow[] backlog = new BacklogRow[Backlog];
        private readonly OfferRow[] offers = new OfferRow[Offers];
        private int laidOut = -1;

        // Perks and log.
        private readonly FrontBox perks, log;
        private readonly PerkRow[] perkRows = new PerkRow[Rungs];
        private int perkShown = -1;
        private readonly TMP_Text[] logStamp = new TMP_Text[LogLines], logText = new TMP_Text[LogLines], logAgo = new TMP_Text[LogLines];
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

            // The two currencies side by side: the front's programme funds, and the pilot's own allocation with the rate between them.
            float cell = (w - 20f) / 2f;
            for (int i = 0; i < 2; i++) Rule(s, 10f + i * cell, 102f, 1f, 38f);
            keyList.Add(Key(s, "FRONT FUNDS", 18f, 101f, 96f, 14f));
            budgetNote = FrontKit.Mono(s, "BudgetNote", 118f, 101f, cell - 128f, 14f, 10f, TextAlignmentOptions.MidlineRight);
            budgetValue = FrontKit.Mono(s, "BudgetValue", 18f, 114f, cell - 24f, 26f, 18f, TextAlignmentOptions.MidlineLeft, true);
            keyList.Add(Key(s, "YOUR ALLOCATION", 18f + cell, 101f, 124f, 14f));
            rateNote = FrontKit.Mono(s, "RateNote", 146f + cell, 101f, cell - 156f, 14f, 10f, TextAlignmentOptions.MidlineRight);
            allocValue = FrontKit.Mono(s, "AllocValue", 18f + cell, 114f, cell - 24f, 26f, 18f, TextAlignmentOptions.MidlineLeft, true);
            cy += StatusH + Gap;

            // ---- STRATEGY: directive, then the funding split ----
            directive = new FrontBox(parent, skin, "Directive", x, cy, w, DirectiveH, "STRATEGY", AvIcon.Target);
            RectTransform d = directive.Rect;
            float pw = (w - 20f - 2f * 6f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                posture[i] = Button(d, "-", 10f + i * (pw + 6f), 28f, pw, 30f, () => actions.SetDirective(front, postureId[slot]), AvButtonStyle.Default);
            }
            lockLine = FrontKit.Mono(d, "Lock", 10f, 62f, w * 0.5f, 18f, 11f, TextAlignmentOptions.MidlineLeft);
            focusLine = FrontKit.Mono(d, "Focus", w * 0.5f, 62f, w * 0.5f - 118f, 18f, 11f, TextAlignmentOptions.MidlineRight);
            focusButton = Button(d, "PICK FOCUS", w - 108f, 60f, 98f, 22f, () => actions.SetFocus(front), AvButtonStyle.Quiet);
            focusButton.Help = "Pick a point for the director to work around: this window hides, right-click the map (Esc cancels). One pin per front.";
            postureLine = FrontKit.Mono(d, "Posture", 10f, 84f, w - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            Rule(d, 10f, 104f, w - 20f, 1f);
            keyList.Add(Key(d, "FUNDING SPLIT · FACTION-WIDE", 10f, 108f, w * 0.5f - 10f, 14f));
            shareLock = FrontKit.Mono(d, "ShareLock", w * 0.5f, 108f, w * 0.5f - 10f, 14f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            float sw = (w - 20f - 96f) / 3f;
            for (int i = 0; i < 3; i++) shareCell[i] = new ShareCell(d, skin, 10f + i * sw, 126f, sw - 8f, i);
            string lockWord = ((int)FrontRules.LockSeconds).ToString(CultureInfo.InvariantCulture);
            priMinus = Button(d, "-10", w - 96f, 124f, 40f, 24f, () => actions.SetPriority(front, -10), AvButtonStyle.Quiet);
            priPlus = Button(d, "+10", w - 50f, 124f, 40f, 24f, () => actions.SetPriority(front, +10), AvButtonStyle.Quiet);
            priMinus.Help = "Take 10 points of the faction funding share off this front; the other two keep their proportions. Any member; a " + lockWord + " s lock follows.";
            priPlus.Help = "Add 10 points of the faction funding share to this front; the other two keep their proportions. Any member; a " + lockWord + " s lock follows.";
            previewLess = FrontKit.Mono(d, "PreviewLess", 10f, 152f, w * 0.5f - 14f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft);
            previewMore = FrontKit.Mono(d, "PreviewMore", w * 0.5f, 152f, w * 0.5f - 10f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft);
            cy += DirectiveH + Gap;

            // ---- PROGRAMMES ----
            programmes = new FrontBox(parent, skin, "Programmes", x, cy, w, ProgrammesH, "PROGRAMMES", AvIcon.Stack2);
            RectTransform pr = programmes.Rect;
            for (int i = 0; i < FrontRules.MaxQueue; i++) queuePip[i] = FrontKit.Solid(pr, "QueuePip" + i, w - 170f + i * 20f, 6f, 16f, 10f, Color.clear);
            active = new ActiveCard(this, pr, 8f, CardY, w - 16f, CardH);
            for (int i = 0; i < Backlog; i++) backlog[i] = new BacklogRow(this, pr, i, 8f, CardY + CardH + 4f + i * BacklogH, w - 16f, BacklogH);
            offerHint = FrontKit.Mono(pr, "OfferHint", 10f, 0f, w - 20f, OfferHintH, 10.5f, TextAlignmentOptions.MidlineLeft, false, 1f);
            for (int i = 0; i < Offers; i++) offers[i] = new OfferRow(this, pr, w - 16f);
            Layout(0);
            cy += ProgrammesH + Gap;

            // ---- PERKS ----
            perks = new FrontBox(parent, skin, "Perks", x, cy, w, PerksH, "PERKS · YOUR SUPPORT", AvIcon.Star);
            for (int i = 0; i < Rungs; i++) perkRows[i] = new PerkRow(perks.Rect, skin, 8f, 26f + i * 20f, w - 16f, 20f);
            LayoutPerks(5);
            cy += PerksH + Gap;

            // ---- LOG ----
            log = new FrontBox(parent, skin, "Log", x, cy, w, y + height - cy, "RADIO LOG", AvIcon.Radio);
            for (int i = 0; i < LogLines; i++)
            {
                logStamp[i] = FrontKit.Mono(log.Rect, "LogStamp" + i, 10f, 27f + i * 17f, 54f, 17f, 11f, TextAlignmentOptions.MidlineLeft);
                logText[i] = FrontKit.Mono(log.Rect, "Log" + i, 66f, 27f + i * 17f, w - 66f - 84f, 17f, 11f, TextAlignmentOptions.MidlineLeft);
                logAgo[i] = FrontKit.Mono(log.Rect, "LogAgo" + i, w - 84f, 27f + i * 17f, 74f, 17f, 10.5f, TextAlignmentOptions.MidlineRight);
            }

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
            offerHint.color = AvInk.Dim;
            postureLine.color = AvInk.Dim;
        }

        /// <summary>"25 AP → 75 FUNDS": both sides of the donation, the funds from the current conversion rate.</summary>
        private static string Pays(int ap) => ap + " AP → " + Mathf.RoundToInt(ap * FrontRules.DonateRate) + " FUNDS";

        /// <summary>The block under the active card: the backlog rows take their lines, the offers share what is left of the box (tall enough for a second line when the queue is short).</summary>
        private void Layout(int backlogRows)
        {
            if (backlogRows == laidOut) return;
            laidOut = backlogRows;
            float hintY = CardY + CardH + 4f + backlogRows * BacklogH, top = hintY + OfferHintH + 2f;
            float row = Mathf.Clamp(Mathf.Floor((ProgrammesH - 6f - top) / Offers), 22f, 40f);
            AvLay.Place(offerHint.rectTransform, 10f, hintY, w - 20f, OfferHintH);
            for (int i = 0; i < Offers; i++) offers[i].Layout(top + i * row, row);
        }

        // ---- Paint ---------------------------------------------------------------------------------------------------

        /// <summary>Who holds the faction-wide funding-share lock and until when (host mission seconds); set by the window before each paint.</summary>
        public void PriorityLock(string by, float until) { priorityBy = by ?? ""; priorityUntil = until; }

        // The host sends a lock holder as printable ASCII, cleaned to 20 then cut to 16 (SpaceWire.Clean, FrontBook.Trim): the local callsign is compared in that form, so a long or non-ASCII name still reads as its own lock.
        private static bool SameName(string by, string callsign)
        {
            if (string.IsNullOrEmpty(by) || callsign == null) return false;
            string mine = Domain.Space.SpaceWire.Clean(callsign, Domain.Space.SpaceReply.MaxClaimant).Trim();
            if (mine.Length > FrontWire.MaxName) mine = mine.Substring(0, FrontWire.MaxName);
            return string.Equals(by, mine, StringComparison.OrdinalIgnoreCase);
        }

        private static string LockWord(bool mine, string by, float left) =>
            mine ? "HELD BY YOU · " + FrontKit.Clock(left) : "LOCKED · " + (string.IsNullOrEmpty(by) ? "?" : by) + " · " + FrontKit.Clock(left);

        /// <summary>The one-line state of the front for the window's title bar: readiness rung and superiority band, or words before the host speaks.</summary>
        public static string Headline(FrontRoomView v)
        {
            FrontRow r = v.Row;
            if (FrontViews.IsWaiting(r)) return "WAITING FOR HOST";
            int ready = Mathf.Clamp(r.Readiness, 1, 5);
            return "R" + ready + " " + ReadyWords[ready] + " · " + FrontSuperiority.Word(r.Superiority) + " " + FrontKit.Signed(r.Superiority);
        }

        public void Paint(FrontRoomView v)
        {
            front = v.Front;
            FrontRow r = v.Row;
            bool wait = FrontViews.IsWaiting(r); // the host has not told us this front yet: every host figure is a dash, never a made-up R1 or zero

            // Readiness.
            int ready = Mathf.Clamp(r.Readiness, 1, 5);
            for (int i = 0; i < 5; i++)
                pipFill[i].color = !wait && i < ready ? (ready >= 4 ? AvInk.State(AvState.Ready) : AvInk.Select) : Color.clear;
            if (wait) FrontKit.Set(readyWord, "— · WAITING FOR HOST", AvInk.Dim);
            else FrontKit.Set(readyWord, "R" + ready + " · " + ReadyWords[ready], ready >= 4 ? FrontKit.Tone(AvState.Ready) : AvInk.Ink);

            // Superiority.
            int sup = wait ? 0 : r.Superiority;
            string word = FrontSuperiority.Word(sup);
            AvState tone = sup >= 20 ? AvState.Ready : sup > -20 ? AvState.Info : sup > -60 ? AvState.Caution : AvState.Danger;
            Color ink = wait ? AvInk.Dim : FrontKit.Tone(tone);
            FrontKit.Set(supValue, wait ? "—" : FrontKit.Signed(sup), ink);
            float half = barW * 0.5f, len = Mathf.Abs(sup) / 100f * half;
            supFill.color = wait ? Color.clear : AvInk.State(tone).WithAlpha(0.85f);
            supNeedle.color = wait ? Color.clear : AvInk.Ink;
            AvLay.Place(supFill.rectTransform, barX + half + (sup >= 0 ? 0f : -len), 60f, len, 16f);
            AvLay.Place(supNeedle.rectTransform, barX + half + sup / 100f * half - 1.5f, 55f, 3f, 26f);
            for (int i = 0; i < 5; i++)
            {
                bool on = !wait && Bands[i] == word;
                bandWord[i].color = on ? ink : AvInk.Dim;
                bandWord[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            }

            // The two currencies: programme funds and the pilot's own allocation (a donation never edits either figure locally; the mirror repaints them).
            FrontKit.Set(budgetValue, wait ? "—" : r.Budget + " FUNDS", wait ? AvInk.Dim : AvInk.Ink);
            FrontKit.Set(budgetNote, wait ? "" : "PAYS THE QUEUE HEAD", AvInk.Dim);
            FrontKit.Set(allocValue, v.Allocation + " AP", AvInk.Ink);
            FrontKit.Set(rateNote, "1 AP → " + Rate + " FUNDS", AvInk.Dim);

            // Directive: a lock held by someone else disables the postures here (the host still judges).
            FrontDirective[] set = Postures(front);
            float left = r.DirectiveLockUntil - v.Now;
            bool held = !wait && left > 0f, blocked = held && !SameName(r.DirectiveBy, v.Callsign);
            for (int i = 0; i < 3; i++)
            {
                postureId[i] = set[i];
                posture[i].Label = FrontRules.Name(set[i]);
                posture[i].Latched = !wait && r.Directive == set[i];
                OpsKit.Enable(posture[i], !wait && !blocked);
            }
            if (wait) FrontKit.Set(lockLine, "WAITING FOR THE HOST", AvInk.Dim);
            else if (held) FrontKit.Set(lockLine, LockWord(!blocked, r.DirectiveBy, left), FrontKit.Tone(AvState.Caution));
            else FrontKit.Set(lockLine, "POSTURE · NO LOCK ON RECORD", AvInk.Dim); // an expired local clock is not a permission: the host still judges the press
            FrontKit.Set(focusLine, wait ? "—" : r.HasFocus ? "FOCUS PIN " + v.FocusGrid : "NO FOCUS PIN", !wait && r.HasFocus ? AvInk.Ink : AvInk.Dim);
            OpsKit.Enable(focusButton, !wait);
            FrontKit.Set(postureLine, wait ? "" : "> " + Describe(front, r.Directive), AvInk.Dim);

            // Funding split: the lock is faction-wide, so its holder and clock come from the window, not the row. Both nudges are previewed before either is sent.
            float shareLeft = priorityUntil - v.Now;
            bool shareHeld = !wait && shareLeft > 0f, shareBlocked = shareHeld && !SameName(priorityBy, v.Callsign);
            if (wait) FrontKit.Set(shareLock, "WAITING FOR THE HOST", AvInk.Dim);
            else if (shareHeld) FrontKit.Set(shareLock, C2Kit.FitTo(shareLock, LockWord(!shareBlocked, priorityBy, shareLeft), shareLock.rectTransform.rect.width), FrontKit.Tone(AvState.Caution));
            else FrontKit.Set(shareLock, "NO LOCK ON RECORD", AvInk.Dim);
            int f = (int)front;
            for (int i = 0; i < 3; i++) { shares[i] = wait ? 0 : v.Shares[i]; shareCell[i].Paint(shares[i], i == f, wait); }
            bool canLess = !wait && !shareBlocked && shares[f] >= 10, canMore = !wait && !shareBlocked && shares[f] <= 90;
            OpsKit.Enable(priMinus, canLess);
            OpsKit.Enable(priPlus, canMore);
            if (wait)
            {
                FrontKit.Set(previewLess, "", AvInk.Dim);
                FrontKit.Set(previewMore, "", AvInk.Dim);
            }
            else
            {
                FrontViews.Redistribute(shares, f, -10, lessShares);
                FrontViews.Redistribute(shares, f, +10, moreShares);
                FrontKit.Set(previewLess, shares[f] < 10 ? "-10 → AT THE FLOOR" : Preview("-10", lessShares), canLess ? AvInk.Ink : AvInk.Dim);
                FrontKit.Set(previewMore, shares[f] > 90 ? "+10 → AT THE CEILING" : Preview("+10", moreShares), canMore ? AvInk.Ink : AvInk.Dim);
            }

            // Programmes: the active one, the backlog behind it, the offers.
            int queued = v.Queue.Count;
            for (int i = 0; i < queuePip.Length; i++) queuePip[i].color = wait ? Color.clear : i < queued ? AvInk.Select : AvInk.Ground.WithAlpha(0.9f);
            active.Paint(v, wait);
            for (int i = 0; i < Backlog; i++) backlog[i].Paint(i + 1 < queued ? (FrontQueueView?)v.Queue[i + 1] : null, v.Allocation);
            Layout(Mathf.Max(0, queued - 1));
            FrontKit.Set(offerHint, "AVAILABLE · FRONT FUNDS PAY FIRST, THEN THE BUILD CLOCK RUNS", AvInk.Dim);
            for (int i = 0; i < Offers; i++) offers[i].Paint(i < v.Offers.Count ? (FrontOfferView?)v.Offers[i] : null, wait);

            // Perks.
            int open = 0;
            LayoutPerks(Mathf.Min(Rungs, v.Perks.Count));
            for (int i = 0; i < Rungs; i++)
            {
                FrontPerkView? pv = i < v.Perks.Count ? (FrontPerkView?)v.Perks[i] : null;
                if (pv.HasValue && pv.Value.Unlocked && string.IsNullOrEmpty(pv.Value.Reason)) open++;
                perkRows[i].Paint(pv, wait);
            }

            // Log: newest last, each line dated by the mission clock and by how long ago the host wrote it.
            int from = Mathf.Max(0, v.Log.Count - LogLines);
            for (int i = 0; i < LogLines; i++)
            {
                int li = from + i;
                if (li >= v.Log.Count)
                {
                    bool quiet = i == 0 && v.Log.Count == 0; // an empty net says so instead of leaving a blank box
                    FrontKit.Set(logStamp[i], quiet ? "--:--" : "", AvInk.Dim);
                    FrontKit.Set(logText[i], quiet ? (wait ? "WAITING FOR THE HOST" : "NET QUIET · NOTHING LOGGED SINCE THE LINK CAME UP") : "", AvInk.Dim);
                    FrontKit.Set(logAgo[i], "", AvInk.Dim);
                    continue;
                }
                FrontLogLine l = v.Log[li];
                FrontKit.Set(logStamp[i], l.Stamp, AvInk.Dim);
                FrontKit.Set(logText[i], FrontKit.Fit(logText[i], l.Text, 11f), FrontKit.Tone(l.Tone == AvState.Inert ? AvState.Info : l.Tone));
                FrontKit.Set(logAgo[i], l.Ago < 1f ? "NOW" : FrontKit.Clock(l.Ago) + " AGO", AvInk.Dim);
            }
            status.SetMeta(FrontRules.Name(front) + " FRONT", AvInk.Dim);
            directive.SetMeta(wait ? "WAITING FOR HOST" : FrontRules.Name(r.Directive) + " ACTIVE", AvInk.Dim);
            programmes.SetMeta(wait ? "QUEUE —" : "QUEUE " + queued + "/" + FrontRules.MaxQueue, AvInk.Dim);
            perks.SetMeta(wait ? "READINESS —" : (v.PowerLine.Length > 0 ? v.PowerLine + " · " : "") + "R" + ready + " · " + open + " OF " + v.Perks.Count + " AVAILABLE", AvInk.Dim);
            log.SetMeta(v.Callsign, AvInk.Dim);
        }

        /// <summary>The perk box holds up to eight rows in the room five used to take: the pitch shrinks from 20 px (5 rows or fewer) to 14 px (7), with smaller type once it is under 18; rows beyond the count are hidden.</summary>
        private void LayoutPerks(int count)
        {
            if (count == perkShown) return;
            perkShown = count;
            float pitch = Mathf.Min(20f, (PerksH - 28f) / Mathf.Max(1, count));
            for (int i = 0; i < Rungs; i++) perkRows[i].Layout(26f + i * pitch, pitch, i < count, pitch < 18f);
        }

        private static string Preview(string delta, int[] s) => delta + " → SPC " + s[0] + " · CYB " + s[1] + " · SOF " + s[2];

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

        // ---- Parts ---------------------------------------------------------------------------------------------------

        /// <summary>One front's slice of the funding split: its name and percent over a small bar. The front this window shows is marked with a prompt and a brighter bar.</summary>
        private sealed class ShareCell
        {
            private readonly TMP_Text label;
            private readonly FrontBar bar;
            private readonly string name;

            public ShareCell(RectTransform p, FrontSkin skin, float x, float y, float width, int index)
            {
                name = FrontRules.Name((Front)index);
                label = FrontKit.Mono(p, "Share", x, y - 2f, width, 16f, 11f, TextAlignmentOptions.MidlineLeft, true);
                bar = new FrontBar(p, skin, x, y + 16f, width, 8f);
            }

            public void Paint(int pct, bool current, bool wait)
            {
                FrontKit.Set(label, wait ? "  " + name + " —" : (current ? "> " : "  ") + name + " " + pct + " %", current ? AvInk.Ink : AvInk.Dim);
                bar.Set(wait ? 0f : pct / 100f, current ? AvInk.Select : AvInk.Frame);
            }
        }

        /// <summary>
        /// The programme in work. FUNDING and BUILDING are two named phases with a bar each: the first fills with front funds (and donations),
        /// the second counts the build clock once the first is full. Donation buttons exist only while the first phase still needs funds.
        /// With nothing queued the same card is the one prompt line that points at the offers.
        /// </summary>
        private sealed class ActiveCard
        {
            private readonly AvFrame frame;
            private readonly Image stripe;
            private readonly TMP_Text tag, name, phase, fundLabel, buildLabel, detail, next, noteA, noteB, noteC;
            private readonly FrontBar fundBar, buildBar;
            private readonly AvControl small, large, rest;
            private FrontQueueView cur;
            private int restAp;

            public ActiveCard(FrontRail rail, RectTransform p, float x, float y, float width, float height)
            {
                RectTransform root = AvLay.Child(p, "Active");
                AvLay.Place(root, x, y, width, height);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, width, height, 0f, 0f);
                stripe = FrontKit.Solid(root, "Stripe", 1f, 1f, 3f, height - 2f, Color.clear);
                tag = FrontKit.Mono(root, "Tag", 12f, 3f, 58f, 18f, 10f, TextAlignmentOptions.MidlineLeft, true, 2f);
                name = FrontKit.Mono(root, "Name", 72f, 3f, width - 72f - 140f, 18f, 13f, TextAlignmentOptions.MidlineLeft, true);
                phase = FrontKit.Mono(root, "Phase", width - 140f, 3f, 132f, 18f, 11.5f, TextAlignmentOptions.MidlineRight, true, 1f);
                float fw = Mathf.Round((width - 32f) * 0.58f), bw = width - 32f - fw;
                fundLabel = FrontKit.Mono(root, "FundLabel", 12f, 24f, fw, 14f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
                buildLabel = FrontKit.Mono(root, "BuildLabel", 20f + fw, 24f, bw, 14f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
                fundBar = new FrontBar(root, rail.skin, 12f, 40f, fw, 10f);
                buildBar = new FrontBar(root, rail.skin, 20f + fw, 40f, bw, 10f);
                detail = FrontKit.Mono(root, "Detail", 12f, 52f, width - 24f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft);
                next = FrontKit.Mono(root, "Next", 12f, 67f, width - 24f, 22f, 10.5f, TextAlignmentOptions.MidlineLeft);
                small = rail.Button(root, Pays(DonateSmall), 10f, 66f, 138f, 24f, () => rail.actions.Donate(rail.front, cur.Id, DonateSmall), AvButtonStyle.Default);
                large = rail.Button(root, Pays(DonateLarge), 154f, 66f, 150f, 24f, () => rail.actions.Donate(rail.front, cur.Id, DonateLarge), AvButtonStyle.Default);
                rest = rail.Button(root, "FUND REMAINDER", 310f, 66f, width - 310f - 10f, 24f, () => { if (restAp > 0) rail.actions.Donate(rail.front, cur.Id, restAp); }, AvButtonStyle.Primary);
                small.Help = "Spend " + DonateSmall + " of your allocation on this programme's funding bar at the current rate.";
                large.Help = "Spend " + DonateLarge + " of your allocation on this programme's funding bar at the current rate.";
                rest.Help = "Spend only what the funding bar still needs (never more than your allocation). The host takes no more than that.";
                noteA = FrontKit.Mono(root, "NoteA", 12f, 8f, width - 24f, 22f, 12.5f, TextAlignmentOptions.MidlineLeft, true);
                noteB = FrontKit.Mono(root, "NoteB", 12f, 36f, width - 24f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                noteC = FrontKit.Mono(root, "NoteC", 12f, 60f, width - 24f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                rail.skin.Add(() => frame.Paint(AvInk.Ground.WithAlpha(0.55f), AvInk.Hairline));
            }

            public void Paint(FrontRoomView v, bool wait)
            {
                if (wait || v.Queue.Count == 0) { Idle(v, wait); return; }
                cur = v.Queue[0];
                bool building = cur.Building;
                int total = Mathf.RoundToInt(cur.Cost), funded = building ? total : Mathf.Min(total, Mathf.RoundToInt(cur.Cost * cur.Progress)), need = total - funded;
                AvState tone = building ? AvState.Caution : AvState.Info;
                stripe.color = AvInk.State(tone);
                FrontKit.Set(tag, "ACTIVE", AvInk.Dim);
                FrontKit.Set(name, C2Kit.FitTo(name, cur.Name, name.rectTransform.rect.width), AvInk.Ink);
                FrontKit.Set(phase, building ? "BUILDING" : "FUNDING", FrontKit.Tone(tone));
                FrontKit.Set(fundLabel, (building ? "1 · FUNDED " : "1 · FUNDING ") + funded + " / " + total + " FUNDS", building ? FrontKit.Tone(AvState.Ready) : FrontKit.Tone(AvState.Info));
                FrontKit.Set(buildLabel, building ? "2 · BUILDING " + FrontKit.Clock(cur.BuildLeft) + " LEFT" : "2 · BUILD " + FrontKit.Clock(cur.BuildTotal), building ? FrontKit.Tone(AvState.Caution) : AvInk.Dim);
                fundBar.Show(true); buildBar.Show(true);
                fundBar.Set(building ? 1f : cur.Progress, AvInk.State(building ? AvState.Ready : AvState.Info));
                buildBar.Set(building ? cur.Progress : 0f, AvInk.State(AvState.Caution));
                string line = building ? "PAID " + total + " FUNDS · DONE AT MISSION " + FrontKit.Clock(v.Now + cur.BuildLeft)
                    : need > 0 ? "NEEDS " + need + " MORE FUNDS · THE FRONT BUDGET PAYS IT AS IT ARRIVES" : "FULLY FUNDED · THE BUILD STARTS AT THE HOST'S NEXT TICK";
                FrontKit.Set(detail, line, AvInk.Dim);
                FrontKit.Set(noteA, "", AvInk.Dim); FrontKit.Set(noteB, "", AvInk.Dim); FrontKit.Set(noteC, "", AvInk.Dim);

                bool donate = !building && need > 0;
                small.Rect.gameObject.SetActive(donate); large.Rect.gameObject.SetActive(donate); rest.Rect.gameObject.SetActive(donate);
                if (donate)
                {
                    OpsKit.Enable(small, v.Allocation >= DonateSmall);
                    OpsKit.Enable(large, v.Allocation >= DonateLarge);
                    restAp = Mathf.Max(0, Mathf.Min(v.Allocation, Mathf.CeilToInt(need / FrontRules.DonateRate)));
                    OpsKit.Label(rest, restAp > 0 ? "FUND REMAINDER · " + Pays(restAp) : "FUND REMAINDER · NO AP");
                    OpsKit.Enable(rest, restAp > 0);
                }
                string after = !building ? "" : v.Queue.Count > 1 ? "NEXT › " + v.Queue[1].Name + " · " + Mathf.RoundToInt(v.Queue[1].Cost) + " FUNDS" : "NEXT › NOTHING QUEUED · QUEUE ANOTHER BELOW";
                FrontKit.Set(next, C2Kit.FitTo(next, after, next.rectTransform.rect.width), AvInk.Dim);
            }

            // Nothing in work: one prompt, then what the next readiness rung would cost and unlock (all from the mirrors and the rules table).
            private void Idle(FrontRoomView v, bool wait)
            {
                stripe.color = AvInk.Frame;
                FrontKit.Set(tag, "", AvInk.Dim); FrontKit.Set(name, "", AvInk.Dim); FrontKit.Set(phase, "", AvInk.Dim);
                FrontKit.Set(fundLabel, "", AvInk.Dim); FrontKit.Set(buildLabel, "", AvInk.Dim); FrontKit.Set(detail, "", AvInk.Dim); FrontKit.Set(next, "", AvInk.Dim);
                fundBar.Show(false); buildBar.Show(false);
                small.Rect.gameObject.SetActive(false); large.Rect.gameObject.SetActive(false); rest.Rect.gameObject.SetActive(false);
                if (wait)
                {
                    FrontKit.Set(noteA, "WAITING FOR THE HOST", AvInk.Dim); FrontKit.Set(noteB, "", AvInk.Dim); FrontKit.Set(noteC, "", AvInk.Dim);
                    return;
                }
                FrontRow r = v.Row;
                FrontKit.Set(noteA, "NO PROGRAMME IN WORK", AvInk.Ink);
                string budget = r.Budget > 0 ? r.Budget + " FUNDS IN THE FRONT BUDGET ARE IDLE · QUEUE A PROGRAMME BELOW" : "THE FRONT BUDGET IS EMPTY · QUEUE A PROGRAMME, THE FUNDING SPLIT FILLS IT";
                FrontKit.Set(noteB, C2Kit.FitTo(noteB, budget, noteB.rectTransform.rect.width), AvInk.Dim);
                string rung = "READINESS " + r.Readiness + " · NOTHING LEFT TO RAISE";
                if (r.Readiness < FrontRules.MaxReadiness)
                {
                    string cost = "", unlocks = "";
                    foreach (FrontOfferView o in v.Offers) if (o.Id == ProgrammeId.Readiness) { cost = " · " + Mathf.RoundToInt(o.Cost) + " FUNDS · BUILD " + FrontKit.Clock(o.BuildSeconds); break; }
                    foreach (FrontPerkView p in v.Perks) if (p.Rung == r.Readiness + 1) unlocks += (unlocks.Length > 0 ? ", " : "") + p.Name;
                    rung = "NEXT RUNG R" + (r.Readiness + 1) + cost + (unlocks.Length > 0 ? " · UNLOCKS " + unlocks : "");
                }
                FrontKit.Set(noteC, C2Kit.FitTo(noteC, rung, noteC.rectTransform.rect.width), AvInk.Dim);
            }
        }

        /// <summary>A waiting programme in one line: its place in the queue, how much of it is funded, and (while it still needs funds) one donation button.</summary>
        private sealed class BacklogRow
        {
            private readonly RectTransform root;
            private readonly AvFrame frame;
            private readonly TMP_Text place, name, state;
            private readonly AvControl fund;
            private readonly int index;
            private readonly float rowWidth, rowHeight;
            private FrontQueueView cur;

            public BacklogRow(FrontRail rail, RectTransform p, int index, float x, float y, float width, float height)
            {
                this.index = index;
                root = AvLay.Child(p, "Backlog " + index);
                AvLay.Place(root, x, y, width, height);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, width, height, 0f, 0f);
                place = FrontKit.Mono(root, "Place", 4f, 0f, 20f, height, 11f, TextAlignmentOptions.Midline, true);
                name = FrontKit.Mono(root, "Name", 28f, 0f, 190f, height, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                rowWidth = width; rowHeight = height;
                state = FrontKit.Mono(root, "State", 222f, 0f, width - 222f - 146f, height, 10.5f, TextAlignmentOptions.MidlineRight);
                fund = rail.Button(root, Pays(DonateSmall), width - 142f, 0f, 138f, height, () => rail.actions.Donate(rail.front, cur.Id, DonateSmall), AvButtonStyle.Default);
                fund.Help = "Spend " + DonateSmall + " of your allocation on this waiting programme's funding bar at the current rate.";
                rail.skin.Add(() => frame.Paint(AvInk.Ground.WithAlpha(0.45f), AvInk.Hairline.WithAlpha(0.6f)));
            }

            public void Paint(FrontQueueView? q, int allocation)
            {
                root.gameObject.SetActive(q.HasValue);
                if (!q.HasValue) return;
                cur = q.Value;
                int total = Mathf.RoundToInt(cur.Cost), funded = Mathf.Min(total, Mathf.RoundToInt(cur.Cost * cur.Progress));
                bool done = funded >= total;
                FrontKit.Set(place, (index + 2).ToString(CultureInfo.InvariantCulture), AvInk.Dim);
                FrontKit.Set(name, C2Kit.FitTo(name, cur.Name, name.rectTransform.rect.width), AvInk.Ink);
                FrontKit.Set(state, done ? "FUNDED · WAITING FOR THE SLOT" : "WAITING · " + funded + " / " + total + " FUNDS", done ? FrontKit.Tone(AvState.Ready) : AvInk.Dim);
                fund.Rect.gameObject.SetActive(!done);
                AvLay.Place(state.rectTransform, 222f, 0f, rowWidth - 222f - (done ? 8f : 146f), rowHeight); // a funded row has no button: its words take the room
                OpsKit.Enable(fund, allocation >= DonateSmall);
            }
        }

        /// <summary>
        /// A programme the front can queue. The row takes the height the queue leaves (FrontRail.Layout): tall enough, a second line names the
        /// requirement or the refusal; short, the refusal shares the first line. A QUEUE button exists only for an offer that can be queued.
        /// </summary>
        private sealed class OfferRow
        {
            private readonly RectTransform root;
            private readonly AvFrame frame;
            private readonly TMP_Text name, cost, build, why;
            private readonly AvControl add;
            private readonly float width;
            private float shownY = -1f, shownH = -1f;
            private bool tall;
            private FrontOfferView cur;

            public OfferRow(FrontRail rail, RectTransform p, float width)
            {
                this.width = width;
                root = AvLay.Child(p, "Offer");
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, width, 26f, 0f, 0f);
                name = FrontKit.Mono(root, "Name", 8f, 0f, 186f, 26f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                cost = FrontKit.Mono(root, "Cost", 196f, 0f, 84f, 26f, 11.5f, TextAlignmentOptions.MidlineRight);
                build = FrontKit.Mono(root, "Build", 284f, 0f, 44f, 26f, 11f, TextAlignmentOptions.MidlineRight);
                why = FrontKit.Mono(root, "Why", 336f, 0f, width - 344f, 26f, 10.5f, TextAlignmentOptions.MidlineRight);
                add = rail.Button(root, "QUEUE", width - 82f, 1f, 74f, 24f, () => rail.actions.Queue(rail.front, cur.Id), AvButtonStyle.Primary);
                add.Help = "Queue this programme. Front funds pay for it automatically; donate allocation to speed it up.";
                rail.skin.Add(() => frame.Paint(AvInk.Ground.WithAlpha(0.4f), AvInk.Hairline.WithAlpha(0.6f)));
            }

            public void Layout(float y, float h)
            {
                if (Mathf.Approximately(y, shownY) && Mathf.Approximately(h, shownH)) return;
                shownY = y; shownH = h;
                tall = h >= 36f;
                float line = tall ? 20f : h;
                AvLay.Place(root, 8f, y, width, h);
                AvLay.Place(frame.rectTransform, 0f, 0f, width, h);
                AvLay.Place(name.rectTransform, 8f, 0f, 186f, line);
                AvLay.Place(cost.rectTransform, 196f, 0f, 84f, line);
                AvLay.Place(build.rectTransform, 284f, 0f, 44f, line);
                if (tall) AvLay.Place(why.rectTransform, 8f, line, width - 100f, h - line - 2f);
                else AvLay.Place(why.rectTransform, 336f, 0f, width - 344f, h);
                why.alignment = tall ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
                float bh = Mathf.Min(h - 4f, 26f);
                AvLay.Place(add.Rect, width - 82f, (h - bh) * 0.5f, 74f, bh);
            }

            public void Paint(FrontOfferView? o, bool wait)
            {
                if (!o.HasValue) { root.gameObject.SetActive(false); return; }
                root.gameObject.SetActive(true);
                cur = o.Value;
                bool ok = !cur.Queued && string.IsNullOrEmpty(cur.Refusal);
                FrontKit.Set(name, cur.Name, cur.Queued ? AvInk.Dim : AvInk.Ink);
                FrontKit.Set(cost, wait ? "—" : Mathf.RoundToInt(cur.Cost) + " FUNDS", AvInk.Dim);
                FrontKit.Set(build, wait ? "—" : FrontKit.Clock(cur.BuildSeconds), AvInk.Dim);
                add.Rect.gameObject.SetActive(ok);
                string reason = cur.Queued ? "IN QUEUE" : cur.Refusal ?? "";
                string said = ok ? (tall ? RequiresWord(cur.Id) : "") : reason;
                FrontKit.Set(why, C2Kit.FitTo(why, said, why.rectTransform.rect.width), ok ? AvInk.Dim : cur.Queued ? FrontKit.Tone(AvState.Info) : FrontKit.Tone(AvState.Caution));
            }

            private static string RequiresWord(ProgrammeId id)
            {
                string need = FrontRules.Info(id).Requires;
                return string.IsNullOrEmpty(need) ? "NO PREREQUISITE" : "REQUIRES · " + need;
            }
        }

        /// <summary>One rung of the front's perk ladder: AVAILABLE with its price, or LOCKED with the real reason (rung, satellite, allocation).</summary>
        private sealed class PerkRow
        {
            private readonly RectTransform root;
            private readonly float x, width;
            private readonly AvFrame chip;
            private readonly TMP_Text rung, name, effect, state;
            private bool unlocked;

            public PerkRow(RectTransform p, FrontSkin skin, float x, float y, float width, float height)
            {
                this.x = x; this.width = width;
                root = AvLay.Child(p, "Perk");
                AvLay.Place(root, x, y, width, height);
                chip = FrontKit.Panel(root, "Chip", 0f, 1f, 28f, height - 2f, 3f, 0f);
                rung = FrontKit.Mono(root, "Rung", 0f, 1f, 28f, height - 2f, 11f, TextAlignmentOptions.Midline, true);
                name = FrontKit.Mono(root, "Name", 36f, 0f, 128f, height, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                effect = FrontKit.Mono(root, "Effect", 168f, 0f, width - 168f - 138f, height, 10.5f, TextAlignmentOptions.MidlineLeft);
                state = FrontKit.Mono(root, "State", width - 134f, 0f, 134f, height, 10.5f, TextAlignmentOptions.MidlineRight, true);
                skin.Add(Restyle);
            }

            private void Restyle() =>
                chip.Paint(unlocked ? AvInk.State(AvState.Ready).WithAlpha(0.25f) : AvInk.Ground.WithAlpha(0.6f), unlocked ? AvInk.State(AvState.Ready) : AvInk.Frame);

            /// <summary>Seats the row at <paramref name="y"/> with the given pitch (a dense row uses the 10 px type) or hides it.</summary>
            public void Layout(float y, float height, bool shown, bool dense)
            {
                root.gameObject.SetActive(shown);
                if (!shown) return;
                AvLay.Place(root, x, y, width, height);
                AvLay.Place(chip.rectTransform, 0f, 1f, 28f, height - 2f);
                AvLay.Place(rung.rectTransform, 0f, 1f, 28f, height - 2f);
                AvLay.Place(name.rectTransform, 36f, 0f, 128f, height);
                AvLay.Place(effect.rectTransform, 168f, 0f, width - 168f - 138f, height);
                AvLay.Place(state.rectTransform, width - 134f, 0f, 134f, height);
                AvText.Size(rung, dense ? 10f : 11f); AvText.Size(name, dense ? 10.5f : 11.5f); AvText.Size(effect, dense ? 10f : 10.5f); AvText.Size(state, dense ? 10f : 10.5f);
            }

            public void Paint(FrontPerkView? pv, bool wait)
            {
                if (!pv.HasValue) return;
                FrontPerkView v = pv.Value;
                if (unlocked != v.Unlocked) { unlocked = v.Unlocked; Restyle(); }
                bool blocked = !string.IsNullOrEmpty(v.Reason), gated = !blocked && v.Unlocked && !string.IsNullOrEmpty(v.Gate);
                FrontKit.Set(rung, "R" + v.Rung, v.Unlocked ? FrontKit.Tone(AvState.Ready) : AvInk.Dim);
                FrontKit.Set(name, v.Name, v.Unlocked && !blocked ? AvInk.Ink : AvInk.Dim);
                FrontKit.Set(effect, C2Kit.FitTo(effect, blocked ? v.Reason : gated ? v.Gate : v.Effect, effect.rectTransform.rect.width), blocked || gated ? FrontKit.Tone(AvState.Caution) : AvInk.Dim);
                if (wait) FrontKit.Set(state, "—", AvInk.Dim);
                else if (blocked) FrontKit.Set(state, "LOCKED", FrontKit.Tone(AvState.Caution));
                else if (gated) FrontKit.Set(state, "RELOCATE", FrontKit.Tone(AvState.Caution));
                else FrontKit.Set(state, "AVAILABLE · " + v.Price + " AP", FrontKit.Tone(AvState.Ready));
            }
        }
    }
}
