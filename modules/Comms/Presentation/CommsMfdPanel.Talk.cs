using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Comms.Domain;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int RecentCalls = 8;

        private static readonly AvIcon[] CallIcons =
        {
            AvIcon.ArrowUpRight, AvIcon.AlertTriangle, AvIcon.Target, AvIcon.Radar2, AvIcon.AlertCircle,
            AvIcon.Clock, AvIcon.ArrowBackUp, AvIcon.CircleCheck, AvIcon.InfoCircle, AvIcon.X, AvIcon.Heart, AvIcon.Flag,
        };

        // ---- CALL --------------------------------------------------------------------------
        private AvSection callSection;
        private AvRow[] recentRows;

        // ---- POLL --------------------------------------------------------------------------
        private AvSection pollSection;
        private AvRow pollRow;
        private AvControl pollClose;
        private AvControl pollPrev;
        private AvControl pollNext;
        private AvNote pollEmpty;
        private PollOptionRow[] optionRows;
        private int pollIndex;
        private int pollStep;
        private uint shownPoll;
        private readonly List<CommsPoll> orderedPolls = new List<CommsPoll>(8);

        private AvSection askSection;
        private AvStepper templateStepper;
        private AvStepper durationStepper;
        private AvField questionField;
        private AvField optionsField;
        private int templateIndex;

        private void ResetTalk()
        {
            callSection = null;
            recentRows = null;
            pollSection = null;
            pollRow = null;
            pollClose = pollPrev = pollNext = null;
            pollEmpty = null;
            optionRows = null;
            pollIndex = 0;
            pollStep = 0;
            shownPoll = 0;
            orderedPolls.Clear();
            askSection = null;
            templateStepper = null;
            durationStepper = null;
            questionField = optionsField = null;
        }

        private void BuildCallPage(AvFlow p)
        {
            callSection = p.Section(AvIcon.Message2, "BREVITY CALLS", "");
            var callSpecs = new AvControl.Spec[CommsCatalog.Calls.Length];
            var callHelps = new string[callSpecs.Length];
            for (int i = 0; i < callSpecs.Length; i++)
            {
                int call = i;
                BrevityCall brevity = CommsCatalog.Calls[i];
                callSpecs[i] = new AvControl.Spec(brevity.Code, () => comms.Call(call), ToneStyle(brevity.Tone), CallIcons[i]);
                callHelps[i] = brevity.Meaning + (brevity.MarksPosition ? " Also drops a ping at your aircraft." : "");
            }
            ButtonGrid(p, callSpecs, 2, callHelps);

            p.Section(AvIcon.ListDetails, "RECENT CALLS", "NEWEST FIRST");
            recentRows = new AvRow[RecentCalls];
            for (int i = 0; i < RecentCalls; i++) recentRows[i] = p.Add(new AvRow(p.Content));
        }

        private void RefreshCalls(float now)
        {
            if (recentRows == null) return;
            callSection?.SetCaption(comms.Channel == CommsChannel.Team ? "ONE CLICK · TO YOUR TEAM" : "ONE CLICK · TO ALL PLAYERS");

            IReadOnlyList<CommsFeedLine> feed = comms.State.Feed;
            int shown = 0;
            for (int i = feed.Count - 1; i >= 0 && shown < RecentCalls; i--)
            {
                CommsFeedLine line = feed[i];
                if (line.Kind != CommsFeedKind.Call || comms.State.IsMuted(line.Author)) continue;
                recentRows[shown].Set(Who(line.Author, line.AuthorName) + " · " + line.Text, null, Ago(now - line.Time), ToneState(line.Tone));
                Show(recentRows[shown], true);
                shown++;
            }
            for (int i = shown; i < RecentCalls; i++)
            {
                if (i == 0 && shown == 0)
                {
                    recentRows[0].Set("No calls yet. Everything your side calls shows up here.", null, "", AvState.Inert);
                    Show(recentRows[0], true);
                }
                else Show(recentRows[i], false);
            }
        }

        // ---- POLL --------------------------------------------------------------------------

        private void BuildPollPage(AvFlow p)
        {
            pollSection = p.Section(AvIcon.QuestionMark, "OPEN POLL", "");
            AvButtons pager = p.Buttons(
                new AvControl.Spec("PREV", () => pollStep--, AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec("NEXT", () => pollStep++, AvButtonStyle.Quiet, AvIcon.ChevronRight));
            pollPrev = pager.Controls[0];
            pollNext = pager.Controls[1];
            pollPrev.Help = "Previous poll.";
            pollNext.Help = "Next poll.";

            pollRow = p.Add(new AvRow(p.Content));
            pollClose = pollRow.AddTrailing(new AvControl.Spec("CLOSE", () =>
            {
                if (shownPoll != 0) comms.ClosePoll(shownPoll);
            }, AvButtonStyle.Danger, AvIcon.X));
            pollClose.Help = "Close your poll now and announce the result.";

            pollEmpty = p.Add(new AvNote(p.Content,
                "No poll is open. Ask one below: everyone who can see it gets a HUD notice and one click to vote."));

            optionRows = new PollOptionRow[CommsPoll.MaxOptions];
            for (int i = 0; i < CommsPoll.MaxOptions; i++)
            {
                int option = i;
                optionRows[i] = p.Add(new PollOptionRow(p.Content, () =>
                {
                    if (shownPoll != 0) comms.Vote(shownPoll, option);
                }, "Vote for this option. You can change your mind until the poll closes."));
            }

            askSection = p.Section(AvIcon.QuestionMark, "ASK", "");
            templateStepper = p.Add(new AvStepper(p.Content, "TEMPLATE",
                () => CommsCatalog.PollTemplates[Wrap(templateIndex, CommsCatalog.PollTemplates.Length)].Question,
                () => templateIndex = Wrap(templateIndex - 1, CommsCatalog.PollTemplates.Length),
                () => templateIndex = Wrap(templateIndex + 1, CommsCatalog.PollTemplates.Length)));
            Tip(templateStepper, "Ready-made questions, so a vote mid-flight costs one click.");
            p.Buttons(new AvControl.Spec("ASK SELECTED TEMPLATE", () =>
            {
                PollTemplate template = CommsCatalog.PollTemplates[Wrap(templateIndex, CommsCatalog.PollTemplates.Length)];
                comms.CreatePoll(template.Question, template.Options);
            }, AvButtonStyle.Primary, AvIcon.QuestionMark)).Controls[0].Help = "Ask the selected question.";
            durationStepper = p.Add(new AvStepper(p.Content, "DURATION",
                () => "OPEN FOR " + CommsText.Countdown(CommsCatalog.PollDuration(comms.PollDurationIndex)),
                () => comms.PollDurationIndex = Wrap(comms.PollDurationIndex - 1, CommsCatalog.PollDurations.Length),
                () => comms.PollDurationIndex = Wrap(comms.PollDurationIndex + 1, CommsCatalog.PollDurations.Length)));
            Tip(durationStepper, "How long the poll stays open.");

            p.Section(AvIcon.Typography, "CUSTOM POLL", "2-4 OPTIONS, SEPARATED BY COMMAS");
            questionField = p.Add(new AvField(p.Content, "QUESTION…", CommsText.MaxQuestion, _ => AskCustom()));
            Tip(questionField, "Up to " + CommsText.MaxQuestion + " characters.");
            optionsField = p.Add(new AvField(p.Content, "YES, NO, MAYBE", 80, _ => AskCustom()));
            Tip(optionsField, "Two to four options, separated by commas.");
            p.Buttons(new AvControl.Spec("ASK", AskCustom, AvButtonStyle.Primary, AvIcon.QuestionMark)).Controls[0].Help =
                "Ask your own question.";
        }

        private void AskCustom()
        {
            string question = questionField != null ? questionField.Text : "";
            string[] options = CommsText.SplitOptions(optionsField != null ? optionsField.Text : "", CommsPoll.MaxOptions);
            if (CommsText.Clean(question, CommsText.MaxQuestion).Length == 0 || options.Length < CommsPoll.MinOptions)
            {
                comms.State.SetNotice("TYPE A QUESTION AND 2-4 OPTIONS, E.G. YES, NO", true, Time.unscaledTime);
                return;
            }
            comms.CreatePoll(question, options);
            if (questionField != null) questionField.Text = "";
            if (optionsField != null) optionsField.Text = "";
        }

        private void RefreshPolls(float now)
        {
            if (optionRows == null) return;
            IReadOnlyList<CommsPoll> polls = comms.State.Polls;

            // Open polls first, newest first; then the settled ones, so a result stays readable.
            List<CommsPoll> ordered = orderedPolls;
            ordered.Clear();
            for (int i = polls.Count - 1; i >= 0; i--) if (!polls[i].Closed && !comms.State.IsMuted(polls[i].Author)) ordered.Add(polls[i]);
            int open = ordered.Count;
            for (int i = polls.Count - 1; i >= 0; i--) if (polls[i].Closed && !comms.State.IsMuted(polls[i].Author)) ordered.Add(polls[i]);

            // The page follows the poll on screen, not its place in a list that re-sorts as
            // polls open and close: a vote lands on the question the player read.
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[i].Id == shownPoll) pollIndex = i;
            pollIndex = ordered.Count == 0 ? 0 : Wrap(pollIndex + pollStep, ordered.Count);
            pollStep = 0;
            pollSection?.SetCaption(ordered.Count == 0 ? "NONE" :
                AvNum.Fixed(pollIndex + 1, 0) + " OF " + AvNum.Fixed(ordered.Count, 0) + (open > 0 ? " · " + AvNum.Fixed(open, 0) + " OPEN" : ""));
            Show(pollPrev, ordered.Count > 1);
            Show(pollNext, ordered.Count > 1);

            CommsPoll poll = ordered.Count > 0 ? ordered[pollIndex] : null;
            shownPoll = poll != null ? poll.Id : 0;
            Show(pollEmpty, poll == null);
            Show(pollRow, poll != null);
            Show(pollClose, poll != null && !poll.Closed && (poll.Author == comms.LocalId || comms.IsHost));

            if (poll != null)
            {
                string when = poll.Closed ? "CLOSED · " + poll.Verdict() : "CLOSES IN " + CommsText.Countdown(poll.Closes - now);
                string meta = "ASKED BY " + Who(poll.Author, poll.AuthorName) + " · " +
                    (poll.Channel == CommsChannel.All ? "ALL" : "TEAM") + " · " + AvNum.Fixed(poll.Total, 0) + " VOTES · " + when;
                pollRow.Set(poll.Question, meta, "", poll.Closed ? AvState.Ready : AvState.Info);
            }

            for (int i = 0; i < optionRows.Length; i++)
            {
                bool shown = poll != null && i < poll.Options.Length;
                Show(optionRows[i], shown);
                if (!shown) continue;
                int total = Mathf.Max(1, poll.Total);
                bool mine = poll.LocalVote == i;
                bool winner = poll.Closed && poll.Leader == i;
                optionRows[i].Set(poll.Options[i], mine, winner, poll.Closed, poll.Tally[i] / (float)total, poll.Tally[i]);
            }

            askSection?.SetCaption(comms.Channel == CommsChannel.Team ? "YOUR TEAM VOTES" : "EVERY PLAYER VOTES");
            templateStepper?.Refresh();
            durationStepper?.Refresh();
        }

        private static int Wrap(int value, int count) => count <= 0 ? 0 : ((value % count) + count) % count;

        /// <summary>One poll option: a vote toggle, a tally bar (data) and a count — the kit has no
        /// vote-bar primitive, so this composes an AvControl with an <see cref="AvGaugeGraphic"/>.</summary>
        private sealed class PollOptionRow : AvPart
        {
            private readonly AvControl vote;
            private readonly AvGaugeGraphic track;
            private readonly TMP_Text count;

            public PollOptionRow(RectTransform parent, Action onVote, string voteHelp)
            {
                Rect = AvLay.Child(parent, "Poll Option");
                vote = AvControl.Make(Rect, new AvControl.Spec("", onVote));
                vote.Help = voteHelp;
                var go = new GameObject("Track", typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(Rect, false);
                track = go.AddComponent<AvGaugeGraphic>();
                track.Shape = AvGaugeShape.Bar;
                track.raycastTarget = false;
                count = AvText.Make(Rect, "Count", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                Restyle();
            }

            public void Set(string label, bool mine, bool winner, bool closed, float fill01, int tally)
            {
                vote.Label = (mine ? "> " : "") + (label ?? "");
                vote.Latched = mine || winner;
                vote.Interactable = !closed;
                track.Value = fill01;
                track.FillColor = track.FillEnd = winner
                    ? AvStyleHost.FuiColor("ready", AvTheme.RailReady)
                    : mine ? AvStyleHost.FuiColor("select", AvTheme.Accent) : AvStyleHost.FuiColor("info", AvTheme.RailInfo);
                track.SetVerticesDirty();
                count.text = AvNum.Fixed(tally, 0);
            }

            public override float Measure(float width) => AvGridTokens.Row;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float countW = 40f, voteW = (s.W - countW) * 0.55f, trackW = Mathf.Max(0f, s.W - voteW - countW - 8f);
                AvLay.Place(vote.Rect, 0f, 0f, voteW, s.H);
                AvLay.Place((RectTransform)track.transform, voteW + 4f, (s.H - 6f) * 0.5f, trackW, 6f);
                AvLay.Place(count.rectTransform, s.W - countW, 0f, countW, s.H);
            }

            public override void Restyle()
            {
                vote.Restyle();
                track.Track = AvStyleHost.Resolve(AvStyleHost.FuiStyle("gauge-track").Background, AvTheme.Hairline);
                track.SetVerticesDirty();
            }
        }
    }
}
