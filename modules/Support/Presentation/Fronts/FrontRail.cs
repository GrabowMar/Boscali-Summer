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
    /// STRATEGY (the three postures with their lock; the funding split and the focus pin are the directors' business, not a pilot chore),
    /// PROGRAMMES (one active programme with FUNDING and BUILDING as two named phases, compact backlog, the offers; one CHIP IN button per programme),
    /// PERKS (the five rungs of this front: AVAILABLE or LOCKED and why). No radio log (the window footer carries the latest answer). Fixed geometry; the backlog and the
    /// offers share one block whose rows take the room the queue leaves. It owns no policy: every press is a request to <see cref="IFrontActions"/>.
    /// </summary>
    internal sealed class FrontRail
    {
        public const float StatusH = 128f, DirectiveH = 86f, ProgrammesMax = 366f, Gap = 5f; // the programmes box holds a full queue and four offers; the perks take the rest
        private const int Offers = 4, Rungs = 8, Backlog = FrontRules.MaxQueue - 1; // the perk box takes up to 8 rows (SPACE has 7)
        private const int Chip = 25;
        private const float CardY = 28f, CardH = 78f, BacklogH = 24f, OfferHintH = 14f;
        private static readonly string[] ReadyWords = { "", "BASELINE", "ALERTED", "OPERATIONAL", "ADVANCED", "FULL SPECTRUM" };

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private readonly float w;
        private Front front;

        // Status.
        private readonly FrontBox status;
        private readonly Image[] pipWell = new Image[5], pipFill = new Image[5];
        private readonly TMP_Text readyWord, supValue, budgetValue, allocValue;
        private readonly Image supWell, supFill, supNeedle;
        private readonly AvHelpTip supTip;
        private Front tipFront = (Front)255;
        private readonly float barX, barW;

        // Strategy: the posture.
        private readonly FrontBox directive;
        private readonly AvControl[] posture = new AvControl[3];
        private readonly TMP_Text postureLine;
        private readonly FrontDirective[] postureId = new FrontDirective[3];
        private bool postureHelped;
        private static readonly string LockSecs = ((int)FrontRules.LockSeconds).ToString(CultureInfo.InvariantCulture);

        // Programmes.
        private readonly FrontBox programmes;
        private readonly TMP_Text offerHint;
        private readonly ActiveCard active;
        private readonly BacklogRow[] backlog = new BacklogRow[Backlog];
        private readonly OfferRow[] offers = new OfferRow[Offers];
        private int laidOut = -1;
        private readonly System.Collections.Generic.List<FrontOfferView> shownOffers = new System.Collections.Generic.List<FrontOfferView>(FrontRules.Programmes.Length);

        // Perks.
        private readonly FrontBox perks;
        private readonly float programmesH, perksH;
        private readonly TMP_Text perksHint;
        private readonly PerkRow[] perkRows = new PerkRow[Rungs];
        private int perkShown = -1;
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

            keyList.Add(Key(s, "SUPERIORITY", 10f, 58f, 92f, 20f));
            barX = 104f; barW = w - 114f - 116f; // the band word sits at the bar's end, where the five-name legend used to take a row
            supWell = FrontKit.Solid(s, "SupWell", barX, 58f, barW, 20f, Color.clear);
            // Hovering the bar says what moves it and what being ahead buys: the pilot can see how to win the front, not just the score.
            supWell.raycastTarget = true;
            AvHit.On(supWell);
            supTip = AvHelpTip.Attach(supWell.gameObject, "");
            supFill = FrontKit.Solid(s, "SupFill", barX + barW * 0.5f, 60f, 0f, 16f, Color.clear);
            for (int i = 1; i < 5; i++) BandRule(s, barX + barW * i / 5f);
            supNeedle = FrontKit.Solid(s, "SupNeedle", barX + barW * 0.5f - 1f, 55f, 3f, 26f, Color.clear);
            supValue = FrontKit.Mono(s, "SupValue", barX + barW + 8f, 58f, w - barX - barW - 18f, 20f, 11.5f, TextAlignmentOptions.MidlineRight, true); // "+34 AHEAD"

            // The two currencies side by side: the front's programme funds and the pilot's own allocation (the CHIP IN buttons show the rate between them).
            float cell = (w - 20f) / 2f;
            for (int i = 0; i < 2; i++) Rule(s, 10f + i * cell, 86f, 1f, 36f);
            keyList.Add(Key(s, "FRONT FUNDS", 18f, 84f, 120f, 14f));
            budgetValue = FrontKit.Mono(s, "BudgetValue", 18f, 97f, cell - 24f, 26f, 18f, TextAlignmentOptions.MidlineLeft, true);
            keyList.Add(Key(s, "YOUR ALLOCATION", 18f + cell, 84f, 140f, 14f));
            allocValue = FrontKit.Mono(s, "AllocValue", 18f + cell, 97f, cell - 24f, 26f, 18f, TextAlignmentOptions.MidlineLeft, true);
            cy += StatusH + Gap;

            // ---- STRATEGY: one of three postures for the front's director, and what it does ----
            directive = new FrontBox(parent, skin, "Directive", x, cy, w, DirectiveH, "STRATEGY", AvIcon.Target);
            RectTransform d = directive.Rect;
            float pw = (w - 20f - 2f * 6f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                posture[i] = Button(d, "-", 10f + i * (pw + 6f), 28f, pw, 30f, () => actions.SetDirective(front, postureId[slot]), AvButtonStyle.Default);
            }
            postureLine = FrontKit.Mono(d, "Posture", 10f, 62f, w - 20f, 18f, 11f, TextAlignmentOptions.MidlineLeft);
            cy += DirectiveH + Gap;

            // ---- PROGRAMMES ----
            // No radio log: the programmes take every pixel the fixed boxes leave, so each offer gets its second line.
            programmesH = Mathf.Min(ProgrammesMax, height - StatusH - DirectiveH - 212f - 3f * Gap);
            perksH = height - StatusH - DirectiveH - programmesH - 3f * Gap;
            programmes = new FrontBox(parent, skin, "Programmes", x, cy, w, programmesH, "PROGRAMMES", AvIcon.Stack2);
            RectTransform pr = programmes.Rect;
            active = new ActiveCard(this, pr, 8f, CardY, w - 16f, CardH);
            for (int i = 0; i < Backlog; i++) backlog[i] = new BacklogRow(this, pr, i, 8f, CardY + CardH + 4f + i * BacklogH, w - 16f, BacklogH);
            offerHint = FrontKit.Mono(pr, "OfferHint", 10f, 0f, w - 20f, OfferHintH, 10.5f, TextAlignmentOptions.MidlineLeft, false, 1f);
            for (int i = 0; i < Offers; i++) offers[i] = new OfferRow(this, pr, w - 16f);
            Layout(0);
            cy += programmesH + Gap;

            // ---- PERKS ----
            perks = new FrontBox(parent, skin, "Perks", x, cy, w, perksH, "PERKS · YOUR SUPPORT", AvIcon.Star);
            // The list is a reference: say where a perk is actually used, so nobody clicks a row and waits.
            perksHint = FrontKit.Mono(perks.Rect, "PerksHint", 10f, perksH - 24f, w - 20f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);
            perksHint.text = "USE THEM FROM THE OPS PAGE (PERKS TAB) OR THE C MENU · CLICK TO ARM, THEN FIRE";
            for (int i = 0; i < Rungs; i++) perkRows[i] = new PerkRow(perks.Rect, skin, 8f, 26f + i * 20f, w - 16f, 20f);
            LayoutPerks(5);

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
            perksHint.color = AvInk.Dim;
            postureLine.color = AvInk.Dim;
        }

        /// <summary>"25 AP → 75 FUNDS": both sides of the donation, the funds from the current conversion rate.</summary>
        private static string Pays(int ap) => ap + " AP → " + Mathf.RoundToInt(ap * FrontRules.DonateRate) + " FUNDS";

        /// <summary>One chip-in: up to <see cref="Chip"/> AP, never more than the funding bar still needs. 0 when nothing is needed.</summary>
        private static int ChipFor(int need) => need <= 0 ? 0 : Mathf.Min(Chip, Mathf.CeilToInt(need / FrontRules.DonateRate));

        /// <summary>Seats the one CHIP IN button of a programme: hidden when it needs nothing, greyed when the pilot cannot pay it.</summary>
        private static void PaintChip(AvControl b, int ap, int allocation)
        {
            b.Rect.gameObject.SetActive(ap > 0);
            if (ap <= 0) return;
            OpsKit.Label(b, "CHIP IN · " + Pays(ap));
            OpsKit.Enable(b, allocation >= ap);
        }

        /// <summary>The block under the active card: the backlog rows take their lines, the offers share what is left of the box (tall enough for a second line when the queue is short).</summary>
        private void Layout(int backlogRows)
        {
            if (backlogRows == laidOut) return;
            laidOut = backlogRows;
            float hintY = CardY + CardH + 4f + backlogRows * BacklogH, top = hintY + OfferHintH + 2f;
            float row = Mathf.Clamp(Mathf.Floor((programmesH - 6f - top) / Offers), 22f, 40f);
            AvLay.Place(offerHint.rectTransform, 10f, hintY, w - 20f, OfferHintH);
            for (int i = 0; i < Offers; i++) offers[i].Layout(top + i * row, row);
        }

        // ---- Paint ---------------------------------------------------------------------------------------------------

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
            if (tipFront != front && supTip != null) { tipFront = front; supTip.Text = SuperiorityHelp(front); }
            int sup = wait ? 0 : r.Superiority;
            string word = FrontSuperiority.Word(sup);
            AvState tone = sup >= 20 ? AvState.Ready : sup > -20 ? AvState.Info : sup > -60 ? AvState.Caution : AvState.Danger;
            Color ink = wait ? AvInk.Dim : FrontKit.Tone(tone);
            FrontKit.Set(supValue, wait ? "—" : FrontKit.Signed(sup) + " " + word, ink);
            float half = barW * 0.5f, len = Mathf.Abs(sup) / 100f * half;
            supFill.color = wait ? Color.clear : AvInk.State(tone).WithAlpha(0.85f);
            supNeedle.color = wait ? Color.clear : AvInk.Ink;
            AvLay.Place(supFill.rectTransform, barX + half + (sup >= 0 ? 0f : -len), 60f, len, 16f);
            AvLay.Place(supNeedle.rectTransform, barX + half + sup / 100f * half - 1.5f, 55f, 3f, 26f);

            // The two currencies: programme funds and the pilot's own allocation (a donation never edits either figure locally; the mirror repaints them).
            FrontKit.Set(budgetValue, wait ? "—" : r.Budget + " FUNDS", wait ? AvInk.Dim : AvInk.Ink);
            FrontKit.Set(allocValue, v.Allocation + " AP", AvInk.Ink);

            // Directive: a lock held by someone else disables the postures here (the host still judges).
            FrontDirective[] set = Postures(front);
            float left = r.DirectiveLockUntil - v.Now;
            bool held = !wait && left > 0f, blocked = held && !SameName(r.DirectiveBy, v.Callsign);
            for (int i = 0; i < 3; i++)
            {
                if (postureId[i] != set[i] || !postureHelped)
                    posture[i].Help = FrontRules.Name(set[i]) + ": " + Describe(front, set[i]) + " Free for any pilot on your side; a " + LockSecs + " s lock follows.";
                postureId[i] = set[i];
                posture[i].Label = FrontRules.Name(set[i]);
                posture[i].Latched = !wait && r.Directive == set[i];
                OpsKit.Enable(posture[i], !wait && !blocked);
            }
            postureHelped = true;
            FrontKit.Set(postureLine, wait ? "WAITING FOR THE HOST" : "> " + Describe(front, r.Directive), wait ? AvInk.Dim : AvInk.Ink);

            // Programmes: the active one, the backlog behind it, the offers.
            int queued = v.Queue.Count;
            active.Paint(v, wait);
            for (int i = 0; i < Backlog; i++) backlog[i].Paint(i + 1 < queued ? (FrontQueueView?)v.Queue[i + 1] : null, v.Allocation);
            Layout(Mathf.Max(0, queued - 1));
            FrontKit.Set(offerHint, "QUEUE ONE · FRONT FUNDS PAY FOR IT, THEN IT BUILDS", AvInk.Dim);
            // A queued programme already has its row above: the list shows only what could still be queued, the ones that can be first.
            shownOffers.Clear();
            foreach (FrontOfferView o in v.Offers) if (!o.Queued && string.IsNullOrEmpty(o.Refusal)) shownOffers.Add(o);
            foreach (FrontOfferView o in v.Offers) if (!o.Queued && !string.IsNullOrEmpty(o.Refusal)) shownOffers.Add(o);
            for (int i = 0; i < Offers; i++)
            {
                FrontOfferView? o = i < shownOffers.Count ? (FrontOfferView?)shownOffers[i] : null;
                offers[i].Paint(o, wait, o.HasValue ? Does(o.Value.Id, v) : "");
            }
            offerHint.gameObject.SetActive(shownOffers.Count > 0);

            // Perks.
            int open = 0;
            LayoutPerks(Mathf.Min(Rungs, v.Perks.Count));
            for (int i = 0; i < Rungs; i++)
            {
                FrontPerkView? pv = i < v.Perks.Count ? (FrontPerkView?)v.Perks[i] : null;
                if (pv.HasValue && pv.Value.Unlocked && string.IsNullOrEmpty(pv.Value.Reason)) open++;
                perkRows[i].Paint(pv, wait);
            }

            status.SetMeta(FrontRules.Name(front) + " FRONT", AvInk.Dim);
            // The lock (who changed it last, and for how long) rides the header; with no lock the header just names the posture. An expired local clock is no permission: the host still judges a press.
            if (wait) directive.SetMeta("WAITING FOR HOST", AvInk.Dim);
            else if (held) directive.SetMeta(LockWord(!blocked, r.DirectiveBy, left), FrontKit.Tone(AvState.Caution));
            else directive.SetMeta(FrontRules.Name(r.Directive) + " ACTIVE", AvInk.Dim);
            programmes.SetMeta(wait ? "QUEUE —" : "QUEUE " + queued + "/" + FrontRules.MaxQueue, AvInk.Dim);
            perks.SetMeta(wait ? "READINESS —" : open + " OF " + v.Perks.Count + " AVAILABLE" + (v.PowerLine.Length > 0 ? " · " + v.PowerLine : ""), AvInk.Dim);
        }

        /// <summary>The perk box holds up to eight rows in the room five used to take: the pitch shrinks from 20 px (5 rows or fewer) to 14 px (7), with smaller type once it is under 18; rows beyond the count are hidden.</summary>
        private void LayoutPerks(int count)
        {
            if (count == perkShown) return;
            perkShown = count;
            float pitch = Mathf.Min(26f, (perksH - 28f - 26f) / Mathf.Max(1, count)); // the hint line keeps the foot
            for (int i = 0; i < Rungs; i++) perkRows[i].Layout(26f + i * pitch, pitch, i < count, pitch < 18f);
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

        /// <summary>What superiority is made of on this front (FrontSuperiority.Compute's weights) and what +40 buys (POWER and the counter triangle).</summary>
        private static string SuperiorityHelp(Front f) =>
            f == Front.Space ? "SUPERIORITY vs the strongest enemy: satellites up (40), uplink sites alive (30), jamming their satellites (30). Ahead, your perks hit harder; at +40 enemy SOF teams are easier to find."
            : f == Front.Cyber ? "SUPERIORITY vs the strongest enemy: their nodes you hold (50), your EW trucks and data centers alive (30), a lower trace than theirs (20). Ahead, your perks hit harder; at +40 enemy satellites recharge slower."
            : "SUPERIORITY vs the strongest enemy: teams in the field (40), buildings held (40), camps alive (20). Ahead, your perks hit harder; at +40 enemy hackers are traced faster.";

        /// <summary>What a programme does for the faction, in one line; READINESS names the perks the next rung unlocks.</summary>
        private static string Does(ProgrammeId id, FrontRoomView v)
        {
            switch (id)
            {
                case ProgrammeId.Readiness:
                    string unlocks = "";
                    foreach (FrontPerkView p in v.Perks) if (p.Rung == v.Row.Readiness + 1) unlocks += (unlocks.Length > 0 ? ", " : "") + p.Name;
                    return unlocks.Length > 0 ? "UNLOCKS " + unlocks : "RAISES THE FRONT ONE RUNG";
                case ProgrammeId.LaunchSatellite: return "REPLACES A LOST SATELLITE";
                case ProgrammeId.UplinkSite: return "REBUILDS AN UPLINK SITE · ONLY WHEN ONE IS DOWN";
                case ProgrammeId.Asat: return "SHOOTS DOWN AN ENEMY SATELLITE";
                case ProgrammeId.DataCenter: return "REBUILDS A DATA CENTER · ONLY WHEN ONE IS DOWN";
                case ProgrammeId.EwTruck: return "REPLACES AN EW TRUCK (HACK ENTRY) · ONLY WHEN ONE IS LOST";
                case ProgrammeId.ZeroDay: return "BLINDS THE ENEMY NETWORK FOR A WHILE";
                case ProgrammeId.TrainTeam: return "RAISES ONE MORE SOF TEAM";
                case ProgrammeId.Camp: return "REBUILDS A SOF CAMP · ONLY WHEN ONE IS DOWN";
                case ProgrammeId.Fob: return "FORWARD BASE FOR SOF TEAMS · NEEDS A HELD BUILDING";
                default: return "";
            }
        }

        // ---- Parts ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// The programme in work. FUNDING and BUILDING are two named phases with a bar each: the first fills with front funds (and donations),
        /// the second counts the build clock once the first is full. Donation buttons exist only while the first phase still needs funds.
        /// With nothing queued the same card is the one prompt line that points at the offers.
        /// </summary>
        private sealed class ActiveCard
        {
            private readonly AvFrame frame;
            private readonly Image stripe;
            private readonly TMP_Text tag, name, phase, fundLabel, buildLabel, next, noteA, noteB;
            private readonly FrontBar fundBar, buildBar;
            private readonly AvControl chip;
            private FrontQueueView cur;
            private int chipAp;

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
                next = FrontKit.Mono(root, "Next", 12f, 53f, width - 24f, 22f, 10.5f, TextAlignmentOptions.MidlineLeft);
                chip = rail.Button(root, Pays(Chip), 10f, 52f, width - 20f, 24f, () => { if (chipAp > 0) rail.actions.Donate(rail.front, cur.Id, chipAp); }, AvButtonStyle.Primary);
                chip.Help = "Optional: speed it up with " + Chip + " of your allocation (less when the bar needs less). Front funds pay for it anyway.";
                noteA = FrontKit.Mono(root, "NoteA", 12f, 8f, width - 24f, 22f, 12.5f, TextAlignmentOptions.MidlineLeft, true);
                noteB = FrontKit.Mono(root, "NoteB", 12f, 36f, width - 24f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
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
                FrontKit.Set(noteA, "", AvInk.Dim); FrontKit.Set(noteB, "", AvInk.Dim);

                chipAp = building ? 0 : ChipFor(need);
                PaintChip(chip, chipAp, v.Allocation);
                string after = !building ? (need > 0 ? "" : "FULLY FUNDED · THE BUILD STARTS IN A MOMENT") : v.Queue.Count > 1 ? "NEXT › " + v.Queue[1].Name + " · " + Mathf.RoundToInt(v.Queue[1].Cost) + " FUNDS" : "NEXT › NOTHING QUEUED · QUEUE ANOTHER BELOW";
                FrontKit.Set(next, C2Kit.FitTo(next, after, next.rectTransform.rect.width), AvInk.Dim);
            }

            // Nothing in work: one prompt, then what the next readiness rung would cost and unlock (all from the mirrors and the rules table).
            private void Idle(FrontRoomView v, bool wait)
            {
                stripe.color = AvInk.Frame;
                FrontKit.Set(tag, "", AvInk.Dim); FrontKit.Set(name, "", AvInk.Dim); FrontKit.Set(phase, "", AvInk.Dim);
                FrontKit.Set(fundLabel, "", AvInk.Dim); FrontKit.Set(buildLabel, "", AvInk.Dim); FrontKit.Set(next, "", AvInk.Dim);
                fundBar.Show(false); buildBar.Show(false);
                chip.Rect.gameObject.SetActive(false);
                if (wait)
                {
                    FrontKit.Set(noteA, "WAITING FOR THE HOST", AvInk.Dim); FrontKit.Set(noteB, "", AvInk.Dim);
                    return;
                }
                FrontRow r = v.Row;
                FrontKit.Set(noteA, "NO PROGRAMME IN WORK", AvInk.Ink);
                string budget = r.Budget > 0 ? r.Budget + " FUNDS IN THE FRONT BUDGET ARE IDLE · QUEUE A PROGRAMME BELOW" : "THE FRONT BUDGET IS EMPTY · QUEUE A PROGRAMME, THE FACTION'S INCOME FILLS IT";
                FrontKit.Set(noteB, C2Kit.FitTo(noteB, budget, noteB.rectTransform.rect.width), AvInk.Dim);
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
            private int chipAp;

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
                fund = rail.Button(root, Pays(Chip), width - 142f, 0f, 138f, height, () => { if (chipAp > 0) rail.actions.Donate(rail.front, cur.Id, chipAp); }, AvButtonStyle.Default);
                fund.Help = "Optional: put up to " + Chip + " of your allocation into this waiting programme's funding bar.";
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
                chipAp = ChipFor(total - funded);
                fund.Rect.gameObject.SetActive(!done);
                AvLay.Place(state.rectTransform, 222f, 0f, rowWidth - 222f - (done ? 8f : 146f), rowHeight); // a funded row has no button: its words take the room
                if (!done) { OpsKit.Label(fund, Pays(chipAp)); OpsKit.Enable(fund, allocation >= chipAp); }
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
                add.Help = "Queue this programme. Front funds pay for it automatically, then it builds; CHIP IN with your allocation to speed it up.";
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

            public void Paint(FrontOfferView? o, bool wait, string does)
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
                string said = ok ? (tall ? does : "") : reason;
                FrontKit.Set(why, C2Kit.FitTo(why, said, why.rectTransform.rect.width), ok ? AvInk.Dim : cur.Queued ? FrontKit.Tone(AvState.Info) : FrontKit.Tone(AvState.Caution));
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
