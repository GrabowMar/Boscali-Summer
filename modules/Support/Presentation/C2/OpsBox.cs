using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>The requests an OPERATION box can make; every press is a request, never an effect (the host judges it).</summary>
    internal sealed class OpsBoxActions
    {
        public Action<OpDomain, bool> Fund;
        public Action<OpKind, int> Plan;
        public Action<OpDomain> Cancel;
    }

    /// <summary>One thing an operation may aim at: a satellite class, a revealed SAM net or a held building. <see cref="Id"/> is the host's opaque id (a bird 0..2 for ASAT).</summary>
    internal readonly struct OpChoice
    {
        public readonly int Id;
        public readonly string Label;
        public OpChoice(int id, string label) { Id = id; Label = label ?? ""; }
    }

    /// <summary>The hairline frame of the compact OPERATION strip (no header: the 596 page has no room for one).</summary>
    internal sealed class OpsStrip : AvPart
    {
        private readonly AvFrame frame;
        private float width = AvTokens.PanelWidth, height = 30f;

        public OpsStrip(RectTransform parent, float height)
        {
            Rect = AvLay.Child(parent, "OpsStrip");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            this.height = height;
            Restyle();
            Layout();
        }

        public override float Measure(float w) => height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            Layout();
        }

        private void Layout() => AvLay.Place(frame.rectTransform, 0f, 0f, width, height);

        public override void Restyle() => frame.Paint(OpsInk.Inert, OpsInk.Hairline);
    }

    /// <summary>
    /// The OPERATION box of the NET and SOF pages (spec 6). Tall pages: a header box with the operation's name and state, the shared bar (funding in one colour, posted-rate work in another), a detail
    /// line, a hint and four buttons. The compact 596 page has no room for that: it gets one 30 px strip with the state word, a hairline bar and the same four buttons. The buttons change with the state:
    /// idle = OP / TGT / START, funding = FUND 25 / FUND 50 / TGT / CANCEL, countdown and done = nothing to press. Painted only from the faction mirror; the box never reads a game object.
    /// </summary>
    internal sealed class OpsBox
    {
        private const float Pad = 8f, ButtonH = 24f, StripH = 30f;
        public const float BodyFull = 78f;

        /// <summary>The total height of the box on a tall page (header included) or the strip on a compact one.</summary>
        public static float HeightFor(bool full) => full ? C2Box.HeaderH + BodyFull : StripH;

        private readonly C2Box box;
        private readonly OpsStrip strip;
        private readonly OpDomain domain;
        private readonly OpKind[] kinds;
        private readonly OpsBoxActions act;
        private readonly bool full;
        private readonly float width;
        private readonly TMP_Text nameText, stateText, detailText;
        private readonly Image barBack, barFill, workFill;
        private readonly AvControl b1, b2, b3, b4;
        private readonly List<OpChoice> choices = new List<OpChoice>(8);
        private int kindIndex, choiceIndex;
        private float nextPress;
        private OpsRow row;
        private bool hasRow, online;
        private OpKind shownKind;

        /// <summary>The footer word the page shows while this box has something urgent to say (a countdown, a paused or broken operation); empty otherwise.</summary>
        public string Words { get; private set; } = "";

        public OpsBox(RectTransform parent, float width, bool full, OpDomain domain, OpKind[] kinds, OpsBoxActions actions, Action<AvPart> register)
        {
            this.width = width; this.full = full; this.domain = domain; this.kinds = kinds; act = actions ?? new OpsBoxActions();
            float iw = width - 2f;
            RectTransform body;
            if (full)
            {
                box = new C2Box(parent, "OPERATION");
                register?.Invoke(box);
                box.BodyHeight = BodyFull;
                box.SetMeta(domain == OpDomain.Cyber ? "CYBER" : "SOF");
                body = box.Body;
            }
            else
            {
                strip = new OpsStrip(parent, StripH);
                register?.Invoke(strip);
                body = strip.Rect;
            }
            float top = full ? 2f : 0f;
            nameText = C2Kit.Mono(body, "OpName", full ? 11f : 10.5f, TextAlignmentOptions.MidlineLeft, true, full ? 1f : 0f);
            stateText = C2Kit.Mono(body, "OpState", 11f, TextAlignmentOptions.MidlineRight, true, 1f);
            barBack = AvLay.Solid(body, "OpBarBack", Color.clear);
            barFill = AvLay.Solid(body, "OpBarFill", Color.clear);
            workFill = AvLay.Solid(body, "OpBarWork", Color.clear);
            foreach (Image i in new[] { barBack, barFill, workFill }) i.raycastTarget = false;
            detailText = C2Kit.Mono(body, "OpDetail", 10f, TextAlignmentOptions.MidlineLeft);

            float bw, by;
            if (full)
            {
                C2Kit.Place(nameText, Pad, top, iw - 2f * Pad - 130f, 16f);
                C2Kit.Place(stateText, iw - Pad - 130f, top, 130f, 16f);
                AvLay.Place(barBack.rectTransform, Pad, 20f, iw - 2f * Pad, 6f);
                C2Kit.Place(detailText, Pad, 28f, iw - 2f * Pad, 14f);

                bw = Mathf.Floor((iw - 2f * Pad - 3f * 6f) / 4f);
                by = BodyFull - ButtonH - 4f;
            }
            else
            {
                // One row: the state words on the left, four buttons on the right, a hairline bar along the bottom edge.
                bw = 66f;
                float textW = iw - 2f * Pad - 4f * bw - 3f * 4f - 6f;
                C2Kit.Place(nameText, Pad, 0f, textW, StripH - 4f);
                C2Kit.Place(stateText, Pad, 0f, 0f, 0f);
                AvLay.Place(barBack.rectTransform, Pad, StripH - 5f, textW, 3f);
                stateText.gameObject.SetActive(false);
                detailText.gameObject.SetActive(false);
                by = 3f;
            }

            float gap = full ? 6f : 4f, x0 = full ? Pad : iw - Pad - 4f * bw - 3f * gap;
            b1 = Button(body, AvButtonStyle.Default, x0, by, bw, () => Press(One));
            b2 = Button(body, AvButtonStyle.Default, x0 + (bw + gap), by, bw, () => Press(Two));
            b3 = Button(body, AvButtonStyle.Primary, x0 + 2f * (bw + gap), by, bw, () => Press(Three));
            b4 = Button(body, AvButtonStyle.Quiet, x0 + 3f * (bw + gap), by, bw, () => Press(Four));
            Restyle();
        }

        public void Place(AvSlot slot)
        {
            if (full) box.Place(slot); else strip.Place(slot);
        }

        private AvControl Button(RectTransform parent, AvButtonStyle style, float x, float y, float w, Action click)
        {
            AvControl c = AvControl.Make(parent, new AvControl.Spec("", click, style));
            c.SingleLine();
            AvLay.Place(c.Rect, x, y, w, full ? ButtonH : StripH - 8f);
            return c;
        }

        private OpKind IdleKind => kinds[Mathf.Clamp(kindIndex, 0, kinds.Length - 1)];

        private OpChoice Choice => choices.Count == 0 ? default : choices[Mathf.Clamp(choiceIndex, 0, choices.Count - 1)];

        /// <summary>A finished FOB can be renewed (another 20 minutes, on the same or another held building) while it is still showing.</summary>
        private bool Renewable => hasRow && row.Kind == OpKind.Fob && row.State == OpState.Done;
        private bool wasRenewable;

        private bool Open => hasRow && (row.State == OpState.Funding || row.State == OpState.NeedsFunding || row.State == OpState.Broken);

        /// <summary>A press is a request, never an effect: the host judges it. A 0.35 s debounce keeps a double click from sending twice.</summary>
        private void Press(Action send)
        {
            if (Time.unscaledTime < nextPress) return;
            nextPress = Time.unscaledTime + 0.35f;
            send?.Invoke();
        }

        // Idle: OP cycles the kind, TGT cycles the target, START plans. Open: FUND 25, FUND 50, TGT retargets, CANCEL withdraws.
        private void One()
        {
            if (Open) act.Fund?.Invoke(domain, false);
            else if (kinds.Length > 1) { kindIndex = (kindIndex + 1) % kinds.Length; choiceIndex = 0; }
        }

        private void Two()
        {
            if (Open) act.Fund?.Invoke(domain, true);
            else if (choices.Count > 1) choiceIndex = (choiceIndex + 1) % choices.Count;
        }

        private void RenewOrNothing() { if (Renewable && choices.Count > 0) act.Plan?.Invoke(OpKind.Fob, Choice.Id); }

        private void Three()
        {
            if (Open)
            {
                if (choices.Count == 0) return;
                int at = choices.FindIndex(c => c.Id == row.TargetId);
                OpChoice next = choices[(at + 1) % choices.Count];
                if (next.Id != row.TargetId) act.Plan?.Invoke(row.Kind, next.Id);
            }
            else if (Renewable) RenewOrNothing();
            else if (online && choices.Count > 0) act.Plan?.Invoke(IdleKind, Choice.Id);
        }

        private void Four()
        {
            if (Open) act.Cancel?.Invoke(domain);
        }

        // ---- Paint -----------------------------------------------------------------------------------------

        /// <summary>Paints from the mirror; <paramref name="fill"/> lists what an operation of that kind may aim at.</summary>
        public void Paint(OpsStateData state, bool known, Action<OpKind, List<OpChoice>> fill, float now)
        {
            online = known && state != null && state.Active && (domain == OpDomain.Cyber ? state.CyberOps : state.SofOps);
            hasRow = online && state.TryRow(domain, out row);
            shownKind = hasRow ? row.Kind : IdleKind;
            choices.Clear();
            fill?.Invoke(shownKind, choices);
            if (hasRow && row.HasTarget && choices.FindIndex(c => c.Id == row.TargetId) < 0) choices.Add(new OpChoice(row.TargetId, OpsWords.TargetWord(row.Kind, row.TargetId)));
            if (choiceIndex >= choices.Count) choiceIndex = 0;
            bool renewable = Renewable;
            if (renewable && !wasRenewable) { int cur = choices.FindIndex(c => c.Id == row.TargetId); if (cur >= 0) choiceIndex = cur; }
            wasRenewable = renewable;
            string birds = online && domain == OpDomain.Cyber ? OpsPageWords.Birds(state) : "";
            if (full) box.SetMeta(!online ? "OFFLINE" : birds.Length > 0 ? birds : domain == OpDomain.Cyber ? "CYBER" : "SOF");
            Words = "";
            float innerW = width - 2f - 2f * Pad;

            if (!online)
            {
                string off = !known ? "OPERATIONS · WAITING FOR THE HOST" : "OPERATIONS OFFLINE · NO " + (domain == OpDomain.Cyber ? "DATA CENTER" : "CAMP");
                OpsText.Set(nameText, C2Kit.FitTo(nameText, off, full ? innerW - 130f : CompactTextWidth));
                OpsText.Set(stateText, ""); OpsText.Set(detailText, "");
                nameText.color = OpsInk.Dim;
                SetBar(0, 0, AvState.Inert);
                SetButtons("", "", "", "", false, false, false, false);
                return;
            }

            AvState tone = !hasRow ? AvState.Inert : row.State == OpState.Execute || row.State == OpState.Broken ? AvState.Danger : row.State == OpState.NeedsFunding ? AvState.Caution :
                row.State == OpState.Done ? AvState.Ready : AvState.Info;
            string stateWord = hasRow ? OpsWords.State(row.State, row.EndsAt - now) : "IDLE";
            if (full)
            {
                OpsText.Set(nameText, C2Kit.FitTo(nameText, hasRow ? OpsWords.Name(row.Kind) : "OPERATION · " + OpsWords.Name(IdleKind), innerW - 130f));
                OpsText.Set(stateText, stateWord);
                stateText.color = OpsInk.Word(tone);
                string detail = hasRow ? OpsPageWords.Detail(row, now) : choices.Count == 0 ? OpsWords.NeedTarget(IdleKind) : "READY · GOAL SCALES WITH YOUR HUMANS · PICK A TARGET, START, THEN FUND";
                OpsText.Set(detailText, C2Kit.FitTo(detailText, detail, innerW));
                detailText.color = hasRow && row.Paused ? OpsInk.Word(AvState.Caution) : hasRow ? OpsInk.Ink : OpsInk.Muted;

                nameText.color = hasRow ? OpsInk.Ink : OpsInk.Muted;
            }
            else
            {
                string word = hasRow ? OpsWords.Short(row.Kind) + " · " + stateWord + (row.State == OpState.Funding || row.State == OpState.NeedsFunding || row.State == OpState.Broken ? " " + row.Percent + " %" : "")
                    : "OPERATION · " + (choices.Count == 0 ? "NEEDS TARGET" : "IDLE");
                OpsText.Set(nameText, C2Kit.FitTo(nameText, word, CompactTextWidth));
                nameText.color = hasRow ? OpsInk.Word(tone) : OpsInk.Muted;
            }
            SetBar(hasRow ? row.Percent : 0, hasRow ? row.WorkPercent : 0, tone);

            string target = Choice.Label.Length > 0 ? ShortLabel(Choice.Label) : "NONE";
            if (Open)
            {
                string current = choices.Find(c => c.Id == row.TargetId).Label;
                SetButtons("FUND 25", "FUND 50", "TGT: " + ShortLabel(string.IsNullOrEmpty(current) ? OpsWords.TargetWord(row.Kind, row.TargetId) : current), "CANCEL", !row.Paused, !row.Paused, choices.Count > 1, true);
                b1.Help = "Put 25 CR into the shared bar. Any member may fund; at two or more humans one member is capped at 30 % of the goal. " + OpsPageWords.Detail(row, now) + " · " + OpsWords.Hint(row.Kind);
                b2.Help = "Put 50 CR into the shared bar.";
                b3.Help = "Retarget the operation (its owner only): press to move to the next target in the list.";
                b4.Help = "Cancel the operation: every member's CR is returned in full (its owner only).";
                if (row.Paused) Words = "NEGATIVE: " + OpsWords.Anchor(row.Kind) + " DOWN — RESTORE IT BEFORE FUNDING CONTINUES";
                else if (row.State == OpState.Broken) Words = "OPERATION BROKEN · HALF RETURNED · BAR AT 50 % · FUND TO RESUME";
            }
            else if (Renewable)
            {
                SetButtons("", "TGT: " + target, "RENEW", "", false, choices.Count > 1, choices.Count > 0, false);
                b2.Help = "Choose the held building for the renewal (the one already holding the FOB is listed first).";
                b3.Help = "Renew the FOB: another 20 minutes of base, on the chosen held building. A different building moves the rearm and fuel vehicles. It is funded again like a new operation.";
                Words = row.EndsAt > now ? "FOB UP · RENEW TO ADD 20 MINUTES (FUNDED AGAIN)" : "FOB ENDED · RENEW TO BUILD IT AGAIN (FUNDED AGAIN)";
            }
            else if (hasRow)
            {
                SetButtons("FUND 25", "FUND 50", row.State == OpState.Execute ? "T-" + Mathf.Max(0, Mathf.CeilToInt(row.EndsAt - now)) : "DONE", "CANCEL", false, false, false, false);
                if (row.State == OpState.Execute) Words = "EXECUTE · " + OpsPageWords.Detail(row, now);
            }
            else
            {
                SetButtons(kinds.Length > 1 ? OpsWords.Short(IdleKind) : "", "TGT: " + target, "START", "", kinds.Length > 1, choices.Count > 1, choices.Count > 0, false);
                b1.Help = "Choose the operation: DECRYPT SATELLITE TRACK (ASAT, 1500 CR base) or ZERO-DAY SAM NET FAIL (900 CR base). Goal x0.6 solo, x(0.5 + humans/16) otherwise.";
                b2.Help = "Choose the target: a satellite class, a revealed SAM net or a held building.";
                b3.Help = "Start the operation. It costs nothing; the bar is funded afterwards. " + OpsWords.Hint(shownKind);
            }
            if (Words.Length == 0 && !full && birds.Length > 0) Words = birds; // the strip has no header to carry the satellite loss
        }

        private float CompactTextWidth => width - 2f - 2f * Pad - 4f * 66f - 3f * 4f - 6f;

        /// <summary>The compact strip has 66 px per button: drop the word NET from a SAM net label.</summary>
        private string ShortLabel(string label) => full ? label : label.Replace("SAM NET ", "SAM ").Replace("HELD ", "");

        private void SetButtons(string l1, string l2, string l3, string l4, bool e1, bool e2, bool e3, bool e4)
        {
            b1.Label = l1; b2.Label = l2; b3.Label = l3; b4.Label = l4;
            b1.Interactable = e1 && l1.Length > 0; b2.Interactable = e2 && l2.Length > 0; b3.Interactable = e3 && l3.Length > 0; b4.Interactable = e4 && l4.Length > 0;
            b1.Rect.gameObject.SetActive(l1.Length > 0); b2.Rect.gameObject.SetActive(l2.Length > 0); b3.Rect.gameObject.SetActive(l3.Length > 0); b4.Rect.gameObject.SetActive(l4.Length > 0);
        }

        private void SetBar(int percent, int work, AvState tone)
        {
            float bw = full ? width - 2f - 2f * Pad : CompactTextWidth;
            float y = full ? 20f : StripH - 5f, h = full ? 6f : 3f;
            barBack.color = OpsInk.Inert;
            barFill.color = OpsInk.Rail(tone);
            workFill.color = OpsInk.Word(AvState.Info);
            AvLay.Place(barBack.rectTransform, Pad, y, bw, h);
            AvLay.Place(barFill.rectTransform, Pad, y, bw * Mathf.Clamp01(percent / 100f), h);
            AvLay.Place(workFill.rectTransform, Pad, y, bw * Mathf.Clamp01(Mathf.Min(work, percent) / 100f), h);
        }

        public void Restyle()
        {
            foreach (AvControl c in new[] { b1, b2, b3, b4 }) c?.Restyle();

        }
    }
}
