using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// One contract on the MIS board: a job slip that reads at a glance (file, state and clock, title, pay, where) and
    /// opens in place into its order, phase list, plot and host actions. The plot and the action bar are shared: the
    /// board hands them to whichever slip is open.
    /// </summary>
    internal sealed class ContractCard : AvPart
    {
        private const float Head = 72f, GroupH = 28f, Pad = 12f;
        private readonly AvFrame frame;
        private readonly Image stripe, barBack, barFill, headHit;
        private readonly TMP_Text group, code, state, title, money, line, terms, next, detail;
        private SecondaryObjectiveView entry;
        private bool fresh, hovered;
        private AvState tone = AvState.Inert;
        private MissionTaskPlot plot;
        private MissionTaskActions actions;

        internal bool Expanded { get; private set; }
        internal int Id => entry?.Id ?? -1;

        internal ContractCard(RectTransform parent, Action<int> pick)
        {
            Rect = AvLay.Child(parent, "Contract slip");
            group = Label(Rect, "Board group", AvTextRole.Micro);
            frame = AvFrame.Add(Rect, "Slip frame", AvChamfer.Diagonal(8f));
            frame.raycastTarget = false;
            stripe = AvLay.Solid(Rect, "State stripe", Color.clear);
            stripe.raycastTarget = false;
            headHit = AvLay.Solid(Rect, "Slip header", Color.clear);
            AvHit hit = AvHit.On(headHit);
            hit.Click = _ => pick?.Invoke(Id);
            hit.Hover = h => { hovered = h; Restyle(); };
            code = Label(Rect, "File", AvTextRole.Micro);
            state = Label(Rect, "State and clock", AvTextRole.Micro, false, TextAlignmentOptions.TopRight);
            title = Label(Rect, "Title", AvTextRole.Head);
            money = Label(Rect, "Pay", AvTextRole.DataStrong, false, TextAlignmentOptions.TopRight);
            line = Label(Rect, "Where", AvTextRole.Micro);
            terms = Label(Rect, "XP", AvTextRole.Micro, false, TextAlignmentOptions.TopRight);
            barBack = AvLay.Solid(Rect, "Progress track", Color.clear); barBack.raycastTarget = false;
            barFill = AvLay.Solid(Rect, "Progress", Color.clear); barFill.raycastTarget = false;
            next = Label(Rect, "Next action", AvTextRole.Title, true);
            detail = Label(Rect, "Order detail", AvTextRole.ProseSmall, true);
            Restyle();
        }

        /// <param name="range">"12.4 KM 087°" from the ownship, or empty when either end is unknown.</param>
        /// <param name="groupText">Board group heading drawn above the first slip of a group; empty for the others.</param>
        internal void Set(SecondaryObjectiveView objective, bool isFresh, string range, string groupText, bool expanded)
        {
            entry = objective; fresh = isFresh; Expanded = expanded;
            ObjectiveTasking task = entry.Tasking;
            group.text = groupText ?? "";
            group.gameObject.SetActive(!string.IsNullOrEmpty(groupText));
            code.text = Family(task?.Family ?? ObjectiveFamily.Unknown) + "  ·  FILE " + entry.Id.ToString("000");
            title.text = Clean(entry.Title);
            money.text = "$" + AvNum.Thousands(Math.Max(0, entry.Money));
            terms.text = "+" + AvNum.Thousands(Math.Max(0, entry.Xp)) + " XP";

            string clock = fresh ? MfdSecondaryObjectives.ChipLabel(entry) : "STALE";
            if (entry.IsActive) { tone = AvState.Ready; state.text = "IN FIELD  ·  " + clock; }
            else if (entry.IsOffered) { tone = MfdSecondaryObjectives.OfferLive(entry) ? AvState.Caution : AvState.Inert; state.text = clock; }
            else if (entry.IsComplete) { tone = AvState.Info; state.text = "PAID"; }
            else { tone = AvState.Inert; state.text = task?.Lifecycle == ObjectiveLifecycle.Cancelled ? "ABORTED" : task?.Lifecycle == ObjectiveLifecycle.Expired ? "LAPSED" : "CLOSED"; }

            string where = PhaseLine(task, entry);
            if (string.IsNullOrEmpty(where)) where = string.IsNullOrWhiteSpace(entry.Target) ? Asset(task?.Asset ?? ObjectiveAsset.Unknown) : Clean(entry.Target);
            line.text = string.IsNullOrEmpty(range) ? where : where + "  ·  " + range;

            bool bar = entry.IsActive && fresh;
            barBack.gameObject.SetActive(bar); barFill.gameObject.SetActive(bar);

            next.gameObject.SetActive(expanded); detail.gameObject.SetActive(expanded);
            if (expanded)
            {
                next.text = !fresh ? "Wait for a fresh host report." :
                    !string.IsNullOrWhiteSpace(task?.NextAction) ? Clean(task.NextAction) :
                    entry.IsOffered ? "Accept to take this work for the faction." : entry.IsComplete ? "Work complete. Pay collected." : Clean(entry.Status);
                detail.text = Detail(task);
            }
            Restyle(); Changed();
        }

        /// <summary>Hands the shared plot and action bar to this slip (it must be the open one).</summary>
        internal void Attach(MissionTaskPlot sharedPlot, MissionTaskActions sharedActions)
        {
            plot = sharedPlot; actions = sharedActions;
            plot.Rect.SetParent(Rect, false); plot.Parent = this; plot.Height = 170f;
            actions.Rect.SetParent(Rect, false); actions.Parent = this;
            plot.Rect.gameObject.SetActive(true); actions.Rect.gameObject.SetActive(true);
            plot.Set(entry, fresh);
            Changed();
        }

        /// <summary>Forgets the shared parts without touching them (the next owner reparents them).</summary>
        internal void Release() { plot = null; actions = null; }

        public override float Measure(float width)
        {
            float h = (group.gameObject.activeSelf ? GroupH : 0f) + Head;
            if (!Expanded) return h;
            float w = width - 2f * Pad - 4f;
            h += 8f + AvText.Height(next, w) + 6f + AvText.Height(detail, w) + 12f;
            if (plot != null) h += plot.Height + 10f;
            if (actions != null) h += actions.Measure(width - 2f * Pad) + 10f;
            return h;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = s.W, y0 = 0f;
            if (group.gameObject.activeSelf) { AvLay.Place(group, 2f, 8f, w - 4f, 18f); y0 = GroupH; }
            float body = s.H - y0;
            AvLay.Place(frame.rectTransform, 0f, y0, w, body);
            AvLay.Place(stripe.rectTransform, 0f, y0 + 6f, 3f, body - 12f);
            AvLay.Place(headHit.rectTransform, 0f, y0, w, Head);
            float x = Pad + 4f, inner = w - x - Pad;
            AvLay.Place(code, x, y0 + 9f, inner * .55f, 16f);
            AvLay.Place(state, x + inner * .45f, y0 + 9f, inner * .55f, 16f);
            AvLay.Place(title, x, y0 + 27f, inner - 126f, 22f);
            AvLay.Place(money, x + inner - 122f, y0 + 26f, 122f, 22f);
            AvLay.Place(line, x, y0 + 50f, inner - 100f, 16f);
            AvLay.Place(terms, x + inner - 96f, y0 + 50f, 96f, 16f);
            float progress = entry == null ? 0f : Mathf.Clamp01(entry.Progress);
            AvLay.Place(barBack.rectTransform, x, y0 + Head - 4f, inner, 2f);
            AvLay.Place(barFill.rectTransform, x, y0 + Head - 4f, inner * progress, 2f);
            if (!Expanded) return;
            float y = y0 + Head + 8f;
            float nh = AvText.Height(next, inner); AvLay.Place(next, x, y, inner, nh); y += nh + 6f;
            float dh = AvText.Height(detail, inner); AvLay.Place(detail, x, y, inner, dh); y += dh + 12f;
            if (plot != null) { plot.Place(new AvSlot(Pad, y, w - 2f * Pad, plot.Height)); y += plot.Height + 10f; }
            if (actions != null) actions.Place(new AvSlot(Pad, y, w - 2f * Pad, actions.Measure(w - 2f * Pad)));
        }

        public override void Restyle()
        {
            if (Rect == null) return;
            Color toneColor = tone == AvState.Inert ? AvInk.Dim : AvInk.State(tone);
            bool closed = entry != null && !entry.IsActive && !entry.IsOffered;
            frame.Paint(Expanded ? AvInk.Raised : AvInk.Surface,
                Expanded ? AvInk.Key.WithAlpha(.75f) : hovered ? AvInk.Frame : AvInk.Hairline);
            stripe.color = toneColor;
            group.color = AvInk.Key;
            code.color = AvInk.Dim;
            state.color = toneColor;
            title.color = closed ? AvInk.Dim : AvInk.Ink;
            money.color = closed ? AvInk.Dim : entry != null && entry.IsComplete ? AvInk.State(AvState.Ready) : AvInk.Ink;
            line.color = terms.color = AvInk.Dim;
            barBack.color = AvInk.Hairline;
            barFill.color = AvInk.State(AvState.Ready);
            next.color = AvInk.State(AvState.Caution);
            detail.color = AvInk.Dim;
            plot?.Restyle();
        }

        /// <summary>"PHASE 1/2  ·  INSERT 40%" for accepted work with phases; empty otherwise.</summary>
        private static string PhaseLine(ObjectiveTasking task, SecondaryObjectiveView view)
        {
            if (!view.IsActive || task == null || task.Phases.Length == 0) return "";
            int current = task.CurrentPhase;
            if (current < 0) return "PHASES " + task.Phases.Length + " / " + task.Phases.Length;
            ObjectivePhase phase = task.Phases[current];
            string pct = phase.Progress > 0f ? " " + Mathf.FloorToInt(Mathf.Clamp01(phase.Progress) * 100f) + "%" : "";
            return "PHASE " + (current + 1) + "/" + task.Phases.Length + "  ·  " + Clean(phase.Title) + pct;
        }

        private string Detail(ObjectiveTasking task)
        {
            var sb = new System.Text.StringBuilder(256);
            int current = task?.CurrentPhase ?? -1;
            string condition = current >= 0 ? Clean(task.Phases[current].Condition) : Clean(entry.Description);
            if (!string.IsNullOrWhiteSpace(condition)) sb.Append(condition);
            if (fresh && !string.IsNullOrWhiteSpace(task?.Blocker)) sb.Append("\nHOLD  /  ").Append(Clean(task.Blocker));
            if (task != null && task.Phases.Length > 1)
                for (int i = 0; i < task.Phases.Length; i++)
                {
                    ObjectivePhase p = task.Phases[i];
                    string mark = p.Status == ObjectivePhaseStatus.Done ? "DONE" : p.Status == ObjectivePhaseStatus.Current ? "NOW" : "NEXT";
                    sb.Append(i == 0 ? "\n\n" : "\n").Append(i + 1).Append("  ").Append(Clean(p.Title)).Append("  ·  ").Append(mark);
                }
            if (!string.IsNullOrWhiteSpace(task?.Effect)) sb.Append("\n\nPAYS OFF  /  ").Append(Clean(task.Effect));
            sb.Append("\nLEAD  /  ").Append(string.IsNullOrWhiteSpace(entry.AcceptedBy) ? entry.IsOffered ? "UNASSIGNED" : "NOT REPORTED" : Clean(entry.AcceptedBy));
            return sb.ToString();
        }

        private static TMP_Text Label(RectTransform p, string name, AvTextRole role, bool wrap = false, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        { var t = AvText.Make(p, name, role, "", alignment, wrap); t.raycastTarget = false; if (!wrap) AvText.Fit(t, false); return t; }

        internal static string Clean(string value) => MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(value ?? ""));

        internal static string Family(ObjectiveFamily value) =>
            value == ObjectiveFamily.AirCover ? "AIR COVER" : value == ObjectiveFamily.Unknown ? "SIDE JOB" : value.ToString().ToUpperInvariant();

        private static string Asset(ObjectiveAsset value) =>
            value == ObjectiveAsset.GroundVehicle ? "GROUND VEHICLE" : value == ObjectiveAsset.Unknown ? "SITE NOT REPORTED" : value.ToString().ToUpperInvariant();
    }

    /// <summary>The faction's contract slots: one box per slot, filled by the accepted job; a filled box opens its slip.</summary>
    internal sealed class ContractSlots : AvPart
    {
        private const int Max = 4;
        private readonly AvFrame[] boxes = new AvFrame[Max];
        private readonly TMP_Text[] heads = new TMP_Text[Max], names = new TMP_Text[Max];
        private readonly int[] ids = { -1, -1, -1, -1 };
        private int count;

        internal ContractSlots(RectTransform parent, Action<int> pick)
        {
            Rect = AvLay.Child(parent, "Contract slots");
            for (int i = 0; i < Max; i++)
            {
                int slot = i;
                boxes[i] = AvFrame.Add(Rect, "Slot", AvChamfer.Diagonal(6f));
                AvHit.On(boxes[i]).Click = _ => { if (ids[slot] >= 0) pick?.Invoke(ids[slot]); };
                heads[i] = AvText.Make(boxes[i].rectTransform, "Slot number", AvTextRole.Micro, ""); heads[i].raycastTarget = false; AvText.Fit(heads[i], false);
                names[i] = AvText.Make(boxes[i].rectTransform, "Slot job", AvTextRole.Label, ""); names[i].raycastTarget = false; AvText.Fit(names[i], false);
            }
        }

        /// <summary>Limit 0 (host reports no ceiling) shows one box per accepted job and none when there are none.</summary>
        internal void Set(int limit, System.Collections.Generic.IList<SecondaryObjectiveView> active)
        {
            count = Mathf.Min(Max, limit > 0 ? limit : active.Count);
            for (int i = 0; i < Max; i++)
            {
                bool shown = i < count;
                boxes[i].gameObject.SetActive(shown);
                SecondaryObjectiveView job = shown && i < active.Count ? active[i] : null;
                ids[i] = job?.Id ?? -1;
                if (!shown) continue;
                heads[i].text = "SLOT " + (i + 1) + (job == null ? "" : "  ·  FILE " + job.Id.ToString("000"));
                names[i].text = job == null ? "OPEN" : ContractCard.Clean(job.Title);
            }
            Rect.gameObject.SetActive(count > 0);
            Restyle(); Changed();
        }

        public override float Measure(float width) => count > 0 ? 46f : 0f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (count == 0) return;
            float gap = 8f, w = (s.W - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                AvLay.Place(boxes[i].rectTransform, i * (w + gap), 0f, w, 46f);
                AvLay.Place(heads[i], 10f, 6f, w - 20f, 14f);
                AvLay.Place(names[i], 10f, 22f, w - 20f, 18f);
            }
        }

        public override void Restyle()
        {
            for (int i = 0; i < Max; i++)
            {
                bool filled = ids[i] >= 0;
                boxes[i].Paint(filled ? AvInk.Raised : AvInk.Ground, filled ? AvInk.State(AvState.Ready).WithAlpha(.7f) : AvInk.Hairline);
                heads[i].color = AvInk.Dim;
                names[i].color = filled ? AvInk.Ink : AvInk.Muted;
            }
        }
    }
}
