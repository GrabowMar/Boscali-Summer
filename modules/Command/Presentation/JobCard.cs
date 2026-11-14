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
    /// One job on MIS JOBS, from any source: the grey source chip and state, title and pay, the source's detail with the distance and the currency,
    /// and the row buttons LOCATE, then the source's act, then RELEASE for a job that is the pilot's own. A contract or sortie opens in place into its
    /// order, phase list, plot and ABORT; the plot and the action bar are shared, handed to whichever card is open.
    /// </summary>
    internal sealed class JobCard : AvPart
    {
        private const float Head = 72f, Pad = 12f, ButtonW = 84f, ButtonH = 21f, PayW = 96f;
        private readonly AvFrame frame;
        private readonly Image stripe, headHit;
        private readonly TMP_Text code, state, title, money, line, currency, next, detail;
        private readonly AvControl locate, act, release;
        private JobRow row;
        private SecondaryObjectiveView entry;
        private bool fresh, hovered;
        private AvState tone = AvState.Inert;
        private MissionTaskPlot plot;
        private MissionTaskActions actions;

        internal bool Expanded { get; private set; }
        /// <summary>A contract or sortie the host briefed: it opens into its detail.</summary>
        internal bool Expandable => entry != null;
        internal JobRow Row => row;

        internal JobCard(RectTransform parent, Action<JobCard> pick, Action<JobCard> onLocate, Action<JobCard> onAct, Action<JobCard> onRelease)
        {
            Rect = AvLay.Child(parent, "Job card");
            frame = AvFrame.Add(Rect, "Card frame", AvChamfer.Diagonal(8f));
            frame.raycastTarget = false;
            stripe = AvLay.Solid(Rect, "State stripe", Color.clear);
            stripe.raycastTarget = false;
            headHit = AvLay.Solid(Rect, "Card header", Color.clear);
            AvHit hit = AvHit.On(headHit);
            hit.Click = _ => pick?.Invoke(this);
            hit.Hover = h => { hovered = h; Restyle(); };
            code = Label(Rect, "Source", AvTextRole.Micro);
            state = Label(Rect, "State", AvTextRole.Micro, false, TextAlignmentOptions.TopRight);
            title = Label(Rect, "Title", AvTextRole.Head);
            money = Label(Rect, "Pay", AvTextRole.DataStrong, false, TextAlignmentOptions.TopRight);
            line = Label(Rect, "Detail", AvTextRole.Micro);
            currency = Label(Rect, "Currency", AvTextRole.Micro, false, TextAlignmentOptions.TopRight);
            locate = AvControl.Make(Rect, new AvControl.Spec("LOCATE", () => onLocate?.Invoke(this)));
            act = AvControl.Make(Rect, new AvControl.Spec("ACCEPT", () => onAct?.Invoke(this), AvButtonStyle.Primary));
            release = AvControl.Make(Rect, new AvControl.Spec("RELEASE", () => onRelease?.Invoke(this), AvButtonStyle.Quiet));
            locate.Help = "LOCATE: moves the map to this job.";
            release.Help = "RELEASE: hands this job back (off your HUD, or disarmed).";
            next = Label(Rect, "Next action", AvTextRole.Title, true);
            detail = Label(Rect, "Order detail", AvTextRole.ProseSmall, true);
            Restyle();
        }

        /// <param name="km">"12.4 KM 087°" from the ownship, or empty when either end is unknown.</param>
        /// <param name="contract">The host's card behind a contract or sortie row (its detail), null for any other source.</param>
        internal void Set(JobRow job, string km, SecondaryObjectiveView contract, bool isFresh, bool expanded)
        {
            row = job; entry = contract; fresh = isFresh; Expanded = expanded && contract != null;
            code.text = Kind(job.Kind) + (contract != null ? "  ·  FILE " + contract.Id.ToString("000") : "");
            state.text = job.State ?? "";
            title.text = Clean(job.Title);
            money.text = job.Pay > 0 ? "+" + AvNum.Thousands(job.Pay) : job.Pay < 0 ? "-" + AvNum.Thousands(-job.Pay) : job.Kind == JobKind.StrikePackage ? "FREE" : "—";
            currency.text = job.Currency ?? "";
            string where = string.IsNullOrWhiteSpace(job.Detail) ? "" : Clean(job.Detail);
            line.text = string.IsNullOrEmpty(km) ? where : where.Length == 0 ? km : where + "  ·  " + km;
            tone = job.Mine ? AvState.Caution : job.ActLive ? AvState.Ready : AvState.Inert;

            locate.Interactable = job.HasSite;
            bool hasAct = !string.IsNullOrEmpty(job.Act);
            act.gameObject.SetActive(hasAct);
            if (hasAct) { act.Label = job.Act; act.Interactable = job.ActLive; }
            release.gameObject.SetActive(job.Mine);

            next.gameObject.SetActive(Expanded); detail.gameObject.SetActive(Expanded);
            if (Expanded)
            {
                ObjectiveTasking task = entry.Tasking;
                next.text = !fresh ? "Wait for a fresh host report." :
                    !string.IsNullOrWhiteSpace(task?.NextAction) ? Clean(task.NextAction) :
                    entry.IsOffered ? "Accept to take this work for the faction." : Clean(entry.Status);
                detail.text = Detail(task);
            }
            Restyle(); Changed();
        }

        /// <summary>Hands the shared plot and action bar to this card (it must be the open one).</summary>
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
            float h = Head;
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
            float w = s.W;
            AvLay.Place(frame.rectTransform, 0f, 0f, w, s.H);
            AvLay.Place(stripe.rectTransform, 0f, 6f, 3f, s.H - 12f);
            float x = Pad + 4f, inner = w - x - Pad, bx = w - Pad - ButtonW, textW = bx - x - 8f;
            AvLay.Place(headHit.rectTransform, 0f, 0f, bx - 4f, Head);
            AvLay.Place(code, x, 9f, textW * .5f - 4f, 16f);
            AvLay.Place(state, x + textW * .5f, 9f, textW * .5f, 16f);
            AvLay.Place(title, x, 27f, textW - PayW - 4f, 22f);
            AvLay.Place(money, x + textW - PayW, 26f, PayW, 22f);
            AvLay.Place(line, x, 50f, textW - PayW - 4f, 16f);
            AvLay.Place(currency, x + textW - PayW, 50f, PayW, 16f);
            int n = 1 + (act.gameObject.activeSelf ? 1 : 0) + (release.gameObject.activeSelf ? 1 : 0);
            float by = (Head - (n * ButtonH + (n - 1) * 3f)) * .5f;
            AvLay.Place(locate.Rect, bx, by, ButtonW, ButtonH); by += ButtonH + 3f;
            if (act.gameObject.activeSelf) { AvLay.Place(act.Rect, bx, by, ButtonW, ButtonH); by += ButtonH + 3f; }
            if (release.gameObject.activeSelf) AvLay.Place(release.Rect, bx, by, ButtonW, ButtonH);
            if (!Expanded) return;
            float y = Head + 8f;
            float nh = AvText.Height(next, inner); AvLay.Place(next, x, y, inner, nh); y += nh + 6f;
            float dh = AvText.Height(detail, inner); AvLay.Place(detail, x, y, inner, dh); y += dh + 12f;
            if (plot != null) { plot.Place(new AvSlot(Pad, y, w - 2f * Pad, plot.Height)); y += plot.Height + 10f; }
            if (actions != null) actions.Place(new AvSlot(Pad, y, w - 2f * Pad, actions.Measure(w - 2f * Pad)));
        }

        public override void Restyle()
        {
            if (Rect == null) return;
            Color toneColor = tone == AvState.Inert ? AvInk.Dim : AvInk.State(tone);
            frame.Paint(Expanded || row.Mine ? AvInk.Raised : AvInk.Surface,
                Expanded ? AvInk.Key.WithAlpha(.75f) : row.Mine ? toneColor.WithAlpha(.6f) : hovered && Expandable ? AvInk.Frame : AvInk.Hairline);
            stripe.color = toneColor;
            code.color = AvInk.Dim;
            state.color = toneColor;
            title.color = AvInk.Ink;
            money.color = row.Pay > 0 ? AvInk.State(AvState.Ready) : AvInk.Ink;
            line.color = currency.color = AvInk.Dim;
            next.color = AvInk.State(AvState.Caution);
            detail.color = AvInk.Dim;
            locate.Restyle(); act.Restyle(); release.Restyle();
            plot?.Restyle();
        }

        private string Detail(ObjectiveTasking task)
        {
            var sb = new System.Text.StringBuilder(256);
            int current = task?.CurrentPhase ?? -1;
            string condition = current >= 0 ? Clean(task.Phases[current].Condition) : Clean(entry.Description);
            if (!string.IsNullOrWhiteSpace(condition)) sb.Append(condition);
            if (fresh && !string.IsNullOrWhiteSpace(task?.Blocker)) sb.Append("\nHOLD · ").Append(Clean(task.Blocker));
            if (task != null && task.Phases.Length > 1)
                for (int i = 0; i < task.Phases.Length; i++)
                {
                    ObjectivePhase p = task.Phases[i];
                    string mark = p.Status == ObjectivePhaseStatus.Done ? "DONE" : p.Status == ObjectivePhaseStatus.Current ? "NOW" : "NEXT";
                    sb.Append(i == 0 ? "\n\n" : "\n").Append(i + 1).Append("  ").Append(Clean(p.Title)).Append("  ·  ").Append(mark);
                }
            if (!string.IsNullOrWhiteSpace(task?.Effect)) sb.Append("\n\nPAYS OFF · ").Append(Clean(task.Effect));
            sb.Append("\nLEAD · ").Append(string.IsNullOrWhiteSpace(entry.AcceptedBy) ? entry.IsOffered ? "UNASSIGNED" : "NOT REPORTED" : Clean(entry.AcceptedBy));
            return sb.ToString();
        }

        private static TMP_Text Label(RectTransform p, string name, AvTextRole role, bool wrap = false, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        { var t = AvText.Make(p, name, role, "", alignment, wrap); t.raycastTarget = false; if (!wrap) { AvText.Fit(t, false); t.overflowMode = TextOverflowModes.Ellipsis; } return t; }

        internal static string Clean(string value) => MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(value ?? ""));

        /// <summary>The grey source chip.</summary>
        internal static string Kind(JobKind kind) =>
            kind == JobKind.Sortie ? "SORTIE" : kind == JobKind.StrikePackage ? "STRIKE PACKAGE" : "CONTRACT";
    }
}
