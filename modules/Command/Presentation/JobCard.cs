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
    /// A job on MIS JOBS. The title, source and reward sit above a separate row of actions. A contract or sortie opens in place into its
    /// order, phase list, plot and ABORT; the plot and the action bar are shared, handed to whichever card is open.
    /// </summary>
    internal sealed class JobCard : AvPart
    {
        private const float Pad = 12f, Gap = 6f;
        private readonly AvFrame frame;
        private readonly Image headHit, rule;
        private readonly TMP_Text code, disclosure, state, title, money, line, next, detail;
        private readonly AvControl locate, act, release;
        private readonly AvControl[] controls;
        private JobRow row;
        private SecondaryObjectiveView entry;
        private bool fresh, hovered;
        private AvState tone = AvState.Inert;
        private MissionTaskPlot plot;
        private MissionTaskActions actions;
        private float placedWidth;

        internal bool Expanded { get; private set; }
        /// <summary>A contract or sortie the host briefed: it opens into its detail.</summary>
        internal bool Expandable => entry != null;
        internal JobRow Row => row;

        internal JobCard(RectTransform parent, Action<JobCard> pick, Action<JobCard> onLocate, Action<JobCard> onAct, Action<JobCard> onRelease)
        {
            Rect = AvLay.Child(parent, "Job card");
            frame = AvFrame.Add(Rect, "Card frame", AvChamfer.Diagonal(4f));
            frame.raycastTarget = false;
            headHit = AvLay.Solid(Rect, "Card header", Color.clear);
            rule = AvLay.Solid(Rect, "Action divider", Color.clear);
            AvHit hit = AvHit.On(headHit);
            hit.Click = _ => pick?.Invoke(this);
            hit.Hover = h => { hovered = h; Restyle(); };
            code = Label(Rect, "Source", AvTextRole.Micro);
            disclosure = AvIcons.Make(Rect, AvIcon.ChevronDown, AvGridTokens.IconInline, Color.white);
            state = Label(Rect, "State", AvTextRole.DataSmall, TextAlignmentOptions.TopRight);
            title = Label(Rect, "Title", AvTextRole.Title);
            money = Label(Rect, "Pay", AvTextRole.DataStrong, TextAlignmentOptions.TopRight);
            line = Label(Rect, "Detail", AvTextRole.ProseSmall);
            locate = AvControl.Make(Rect, new AvControl.Spec("LOCATE", () => onLocate?.Invoke(this)));
            act = AvControl.Make(Rect, new AvControl.Spec("ACCEPT", () => onAct?.Invoke(this), AvButtonStyle.Primary));
            release = AvControl.Make(Rect, new AvControl.Spec("RELEASE", () => onRelease?.Invoke(this), AvButtonStyle.Quiet));
            controls = new[] { act, locate, release };
            locate.Help = "LOCATE: moves the map to this job.";
            release.Help = "RELEASE: hands this job back (off your HUD, or disarmed).";
            next = Label(Rect, "Next action", AvTextRole.Title);
            detail = Label(Rect, "Order detail", AvTextRole.Prose);
            Restyle();
        }

        /// <param name="km">"12.4 KM 087°" from the ownship, or empty when either end is unknown.</param>
        /// <param name="contract">The host's card behind a contract or sortie row (its detail), null for any other source.</param>
        internal void Set(JobRow job, string km, SecondaryObjectiveView contract, bool isFresh, bool expanded)
        {
            bool hasAct = !string.IsNullOrEmpty(job.Act);
            bool layoutChanged = Expandable != (contract != null) || Expanded != expanded || row.Mine != job.Mine || row.Act != job.Act;
            row = job; entry = contract; fresh = isFresh; Expanded = expanded && contract != null;
            if (!Expanded && plot != null) Detach();
            headHit.raycastTarget = Expandable;
            disclosure.gameObject.SetActive(Expandable);
            if (Expandable) AvIcons.Set(disclosure, Expanded ? AvIcon.ChevronUp : AvIcon.ChevronDown, AvGridTokens.IconInline);
            layoutChanged |= AvText.Set(code, Kind(job.Kind) + (contract != null ? "   /   " + contract.Id.ToString("000") : ""));
            layoutChanged |= AvText.Set(state, job.State);
            layoutChanged |= AvText.Set(title, Clean(job.Title));
            string pay = job.Pay > 0 ? "+" + AvNum.Thousands(job.Pay) : job.Pay < 0 ? "-" + AvNum.Thousands(-job.Pay) : job.Kind == JobKind.StrikePackage ? "FREE" : "—";
            layoutChanged |= AvText.Set(money, string.IsNullOrEmpty(job.Currency) ? pay : pay + " " + job.Currency);
            string where = string.IsNullOrWhiteSpace(job.Detail) ? "" : Clean(job.Detail);
            layoutChanged |= AvText.Set(line, string.IsNullOrEmpty(km) ? where : where.Length == 0 ? km : where + "  ·  " + km);
            tone = job.Mine ? AvState.Info : job.ActLive ? AvState.Ready : AvState.Inert;

            locate.Interactable = job.HasSite;
            act.gameObject.SetActive(hasAct);
            if (hasAct) { act.Label = job.Act; act.Interactable = job.ActLive; }
            release.gameObject.SetActive(job.Mine);

            next.gameObject.SetActive(Expanded); detail.gameObject.SetActive(Expanded);
            if (Expanded)
            {
                ObjectiveTasking task = entry.Tasking;
                layoutChanged |= AvText.Set(next, !fresh ? "Wait for a fresh host report." :
                    !string.IsNullOrWhiteSpace(task?.NextAction) ? Clean(task.NextAction) :
                    entry.IsOffered ? "Accept to take this work for the faction." : Clean(entry.Status));
                layoutChanged |= AvText.Set(detail, Detail(task));
            }
            Restyle();
            if (layoutChanged)
            {
                if (placedWidth > 0f) Arrange(placedWidth, true);
                Changed();
            }
        }

        /// <summary>Hands the shared plot and action bar to this card (it must be the open one).</summary>
        internal void Attach(MissionTaskPlot sharedPlot, MissionTaskActions sharedActions)
        {
            if (plot != sharedPlot)
            {
                if (sharedPlot.Parent is JobCard previous) previous.Detach();
                plot = sharedPlot; actions = sharedActions;
                plot.Rect.SetParent(Rect, false); plot.Parent = this; plot.Height = 170f;
                actions.Rect.SetParent(Rect, false); actions.Parent = this;
                plot.Rect.gameObject.SetActive(true); actions.Rect.gameObject.SetActive(true);
                Changed();
            }
            plot.Set(entry, fresh);
        }

        private void Detach()
        {
            plot.Rect.gameObject.SetActive(false); actions.Rect.gameObject.SetActive(false);
            plot.Parent = null; actions.Parent = null;
            plot = null; actions = null;
            Changed();
        }

        public override float Measure(float width) => Arrange(width, false);

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            placedWidth = slot.W;
            AvLay.Fill(frame.rectTransform);
            Arrange(slot.W, true);
        }

        private float Arrange(float width, bool place)
        {
            float inner = Mathf.Max(1f, width - 2f * Pad), y = 9f;
            float iconSpace = Expandable ? 20f : 0f;
            float metaWidth = Mathf.Max(1f, inner - iconSpace);
            float stateWidth = Mathf.Min(AvText.Width(state) + 2f, metaWidth * .55f);
            float sourceWidth = Mathf.Max(1f, metaWidth - stateWidth - Gap);
            float metaHeight = Mathf.Max(AvText.Height(code, sourceWidth), AvText.Height(state, stateWidth));
            if (place)
            {
                AvLay.Place(code, Pad, y, sourceWidth, metaHeight);
                AvLay.Place(state, Pad + metaWidth - stateWidth, y, stateWidth, metaHeight);
                AvLay.Place(disclosure, width - Pad - 16f, y, 16f, 16f);
            }
            y += metaHeight + 4f;

            float titleHeight = AvText.Height(title, inner);
            if (place) AvLay.Place(title, Pad, y, inner, titleHeight);
            y += titleHeight + 4f;

            // Price and location share a line while both fit. Long descriptions get the full width.
            float payWidth = Mathf.Min(inner, AvText.Width(money) + 2f);
            bool stacked = AvText.Width(line) + Gap + payWidth > inner;
            float lineWidth = stacked ? inner : Mathf.Max(1f, inner - payWidth - Gap);
            float lineHeight = line.text.Length > 0 ? AvText.Height(line, lineWidth) : 0f;
            float payHeight = AvText.Height(money, payWidth);
            float payY = stacked ? y + lineHeight + 2f : y;
            if (place)
            {
                AvLay.Place(line, Pad, y, lineWidth, lineHeight);
                AvLay.Place(money, Pad + inner - payWidth, payY, payWidth, payHeight);
            }
            y = Mathf.Max(y + lineHeight, payY + payHeight) + Gap;
            if (place)
            {
                AvLay.Place(headHit.rectTransform, 0f, 0f, width, y);
                AvLay.Place(rule.rectTransform, Pad, y, inner, 1f);
            }
            y += Gap + ArrangeControls(inner, y + Gap, place);
            if (!Expanded) return y + 9f;

            y += Pad;
            float nextHeight = AvText.Height(next, inner);
            if (place) AvLay.Place(next, Pad, y, inner, nextHeight);
            y += nextHeight + Gap;
            float detailHeight = AvText.Height(detail, inner);
            if (place) AvLay.Place(detail, Pad, y, inner, detailHeight);
            y += detailHeight + Pad;
            if (plot != null)
            {
                if (place) plot.Place(new AvSlot(Pad, y, inner, plot.Height));
                y += plot.Height + Pad;
            }
            if (actions != null)
            {
                float height = actions.Measure(inner);
                if (place) actions.Place(new AvSlot(Pad, y, inner, height));
                y += height;
            }
            return y + Pad;
        }

        private float ArrangeControls(float inner, float top, bool place)
        {
            int count = 1 + (act.gameObject.activeSelf ? 1 : 0) + (release.gameObject.activeSelf ? 1 : 0);
            int columns = Mathf.Clamp(Mathf.FloorToInt((inner + Gap) / (84f + Gap)), 1, count);
            float width = Mathf.Max(1f, (inner - (columns - 1) * Gap) / columns);
            float height = 28f;
            foreach (AvControl control in controls)
                if (control.gameObject.activeSelf) height = Mathf.Max(height, control.PreferredHeight(width));
            if (place)
            {
                int i = 0;
                foreach (AvControl control in controls)
                {
                    if (!control.gameObject.activeSelf) continue;
                    AvLay.Place(control.Rect, Pad + (i % columns) * (width + Gap), top + (i / columns) * (height + Gap), width, height);
                    i++;
                }
            }
            int rows = (count + columns - 1) / columns;
            return rows * height + (rows - 1) * Gap;
        }

        public override void Restyle()
        {
            if (Rect == null) return;
            Color toneColor = tone == AvState.Inert ? AvInk.Dim : AvInk.State(tone);
            frame.Paint(Expanded || row.Mine ? AvInk.Raised : AvInk.Surface,
                Expanded ? AvInk.Key.WithAlpha(.75f) : row.Mine ? toneColor.WithAlpha(.6f) : hovered && Expandable ? AvInk.Frame : AvInk.Hairline);
            rule.color = AvInk.Hairline;
            disclosure.color = Expanded ? AvInk.Key : AvInk.Dim;
            code.color = AvInk.Dim;
            state.color = toneColor;
            title.color = AvInk.Ink;
            money.color = row.Pay > 0 ? AvInk.State(AvState.Ready) : AvInk.Ink;
            line.color = AvInk.Dim;
            next.color = AvInk.Ink;
            detail.color = AvInk.Ink;
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

        private static TMP_Text Label(RectTransform parent, string name, AvTextRole role,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft) => AvText.Make(parent, name, role, "", align, true);

        internal static string Clean(string value) => MfdSecondaryObjectives.Humanize(MfdSecondaryObjectives.PlainObjective(value ?? ""));

        /// <summary>The grey source chip.</summary>
        internal static string Kind(JobKind kind) =>
            kind == JobKind.Sortie ? "SORTIE" : kind == JobKind.StrikePackage ? "STRIKE PACKAGE" : "CONTRACT";
    }
}
