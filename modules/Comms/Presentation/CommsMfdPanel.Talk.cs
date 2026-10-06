using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Comms.Domain;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int RecentCalls = 18;
        private const int MinRecentCalls = 3;
        private const int PollHistoryRows = 8;

        // Part heights, gaps and padding of everything on the CALL page except its recent list (biased a few px high).
        private const float CallFixedHeight = 175f;

        // POLL page: the fixed part plus 41 px per visible option, and 16 px more while a poll is on screen (its row is two lines).
        private const float PollFixedHeight = 407f;
        private static readonly string[] PollSeconds = { "30s", "1m", "2m", "5m" };

        private static readonly AvIcon[] CallIcons =
        {
            AvIcon.ArrowUpRight, AvIcon.AlertTriangle, AvIcon.Target, AvIcon.Radar2, AvIcon.AlertCircle,
            AvIcon.Clock, AvIcon.ArrowBackUp, AvIcon.CircleCheck, AvIcon.InfoCircle, AvIcon.X, AvIcon.Heart, AvIcon.Flag,
        };

        // ---- CALL --------------------------------------------------------------------------
        private AvSection callSection;
        private AvRow[] recentRows;

        // ---- POLL --------------------------------------------------------------------------
        private AvKeyHeader pollHeader;
        private AvRow pollRow;
        private AvControl pollClose;
        private PollOptionRow[] optionRows;
        private int pollIndex;
        private int pollStep;
        private uint shownPoll;
        private readonly List<CommsPoll> orderedPolls = new List<CommsPoll>(8);

        private AvRow[] templateRows;
        private string templateHelpKey;
        private AvSegmented durationStrip;
        private AvField questionField;
        private AvField optionsField;
        private AvSection historySection;
        private AvRow[] historyRows;
        private readonly uint[] historyIds = new uint[PollHistoryRows];
        private AvNote pollTips;

        private void ResetTalk()
        {
            callSection = null;
            recentRows = null;
            pollHeader = null;
            pollRow = null;
            pollClose = null;
            optionRows = null;
            pollIndex = 0;
            pollStep = 0;
            shownPoll = 0;
            orderedPolls.Clear();
            templateRows = null;
            templateHelpKey = null;
            durationStrip = null;
            questionField = optionsField = null;
            historySection = null;
            historyRows = null;
            for (int i = 0; i < historyIds.Length; i++) historyIds[i] = 0;
            pollTips = null;
        }

        private void BuildCallPage(AvFlow p)
        {
            // Twelve calls in four rows of three: icon, code, and the meaning in the tip. No header: the tab says CALL.
            var callSpecs = new AvControl.Spec[CommsCatalog.Calls.Length];
            var callHelps = new string[callSpecs.Length];
            for (int i = 0; i < callSpecs.Length; i++)
            {
                int call = i;
                BrevityCall brevity = CommsCatalog.Calls[i];
                callSpecs[i] = new AvControl.Spec(brevity.Code, () => comms.Call(call), ToneStyle(brevity.Tone), CallIcons[i % CallIcons.Length]);
                callHelps[i] = brevity.Code + ": " + brevity.Meaning +
                    (brevity.MarksPosition ? " Also drops a ping at your aircraft." : "") +
                    " Goes to the audience on the chip above (TEAM or ALL).";
            }
            ButtonGrid(p, callSpecs, 3, callHelps);

            // The list is what fills the page: as many calls as fit, newest first.
            callSection = p.Section(AvIcon.ListDetails, "RECENT CALLS", "");
            recentRows = new AvRow[RecentCalls];
            for (int i = 0; i < RecentCalls; i++) recentRows[i] = p.Add(new AvRow(p.Content));
        }

        private void RefreshCalls(float now)
        {
            if (recentRows == null) return;

            IReadOnlyList<CommsFeedLine> feed = comms.State.Feed;
            int fit = FitRows(console.Page(TabCall), CallFixedHeight, RecentCalls, MinRecentCalls);
            int shown = 0;
            for (int i = feed.Count - 1; i >= 0 && shown < fit; i--)
            {
                CommsFeedLine line = feed[i];
                if (line.Kind != CommsFeedKind.Call || comms.State.IsMuted(line.Author)) continue;
                recentRows[shown].Set(Who(line.Author, line.AuthorName) + " · " + line.Text, null, Ago(now - line.Time), ToneState(line.Tone));
                Show(recentRows[shown], true);
                shown++;
            }
            callSection?.SetCaption((comms.Channel == CommsChannel.Team ? "TO TEAM" : "TO ALL") +
                (shown == 0 ? "" : " · " + AvNum.Fixed(shown, 0) + " SHOWN"));
            for (int i = shown; i < RecentCalls; i++)
            {
                if (i == 0 && shown == 0)
                {
                    recentRows[0].Set("NO CALLS YET · TAP ONE ABOVE, YOUR TEAM HEARS IT AT ONCE", null, "", AvState.Inert);
                    Show(recentRows[0], true);
                }
                else Show(recentRows[i], false);
            }
        }

        // ---- POLL --------------------------------------------------------------------------

        private void BuildPollPage(AvFlow p)
        {
            pollHeader = p.Add(new AvKeyHeader(p.Content, AvIcon.QuestionMark, "POLL",
                new AvControl.Spec(string.Empty, () => pollStep--, AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec(string.Empty, () => pollStep++, AvButtonStyle.Quiet, AvIcon.ChevronRight)));
            pollHeader[0].Help = "Previous poll. Open polls come first, then the ones that closed.";
            pollHeader[1].Help = "Next poll.";

            pollRow = p.Add(new AvRow(p.Content));
            pollClose = pollRow.AddTrailing(new AvControl.Spec("CLOSE", () =>
            {
                if (shownPoll != 0) comms.ClosePoll(shownPoll);
            }, AvButtonStyle.Danger, AvIcon.X));
            pollClose.Help = "Close your poll now and announce the result. The host can close any poll.";

            // Full-width choices keep the words readable beside the live tally.
            optionRows = new PollOptionRow[CommsPoll.MaxOptions];
            for (int i = 0; i < CommsPoll.MaxOptions; i++)
            {
                int option = i;
                optionRows[i] = new PollOptionRow(p.Content, () =>
                {
                    if (shownPoll != 0) comms.Vote(shownPoll, option);
                }, "Vote for this option. You can change your mind until the poll closes; the bar and number are the live tally.");
                p.Add(optionRows[i]);
            }

            // Ready-made questions: one click asks. The length strip below applies to these and to your own.
            p.Section(AvIcon.QuestionMark, "QUICK POLLS", "CLICK TO ASK");
            templateRows = new AvRow[CommsCatalog.PollTemplates.Length];
            for (int i = 0; i < templateRows.Length; i++)
            {
                int template = i;
                templateRows[i] = p.Add(new AvRow(p.Content, () =>
                {
                    PollTemplate t = CommsCatalog.PollTemplates[template];
                    comms.CreatePoll(t.Question, t.Options);
                }));
                PollTemplate shownTemplate = CommsCatalog.PollTemplates[i];
                templateRows[i].Set(shownTemplate.Question + " · " + string.Join(" / ", shownTemplate.Options), null, "", AvState.Info);
            }

            questionField = new AvField(p.Content, "YOUR QUESTION…", CommsText.MaxQuestion, _ => AskCustom());
            Tip(questionField, "Your own question, up to " + CommsText.MaxQuestion + " characters. Fill in the options too, then press ASK.");
            optionsField = new AvField(p.Content, "OPTIONS: YES, NO", 80, _ => AskCustom());
            Tip(optionsField, "Two to four options, separated by commas.");
            p.Row(questionField, optionsField);

            durationStrip = new AvSegmented(p.Content, "OPEN FOR", PollSeconds,
                () => comms.PollDurationIndex, index => comms.PollDurationIndex = index);
            for (int i = 0; i < durationStrip.Options.Length; i++)
                durationStrip.Options[i].Help = "Keep the poll open for " + CommsText.Countdown(CommsCatalog.PollDuration(i)) + ", for quick polls and your own.";
            var askRow = new AvButtons(p.Content, new[] { new AvControl.Spec("ASK", AskCustom, AvButtonStyle.Primary, AvIcon.QuestionMark) });
            askRow.Controls[0].Help = "Ask your own question with the options typed above, to the audience on the chip (TEAM or ALL).";
            p.Row(durationStrip, askRow);

            // Every poll on record: fills the rest of the page, and a click puts that poll on screen above.
            historySection = p.Section(AvIcon.ListDetails, "ALL POLLS", "");
            historyRows = new AvRow[PollHistoryRows];
            for (int i = 0; i < PollHistoryRows; i++)
            {
                int row = i;
                historyRows[i] = p.Add(new AvRow(p.Content, () =>
                {
                    if (historyIds[row] == 0) return;
                    shownPoll = historyIds[row];
                    pollStep = 0;
                    nextRefresh = 0f;
                }));
            }
            pollTips = p.Add(new AvNote(p.Content,
                "HOW POLLS WORK · anyone on the audience can vote once and change their mind until it closes. " +
                "The asker or the host can close early; the result is posted to the log. One open poll per player.") { MinHeight = AvGridTokens.RowDense, StretchText = true });
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
            pollHeader.SetCaption(ordered.Count == 0 ? "NONE" :
                AvNum.Fixed(pollIndex + 1, 0) + " OF " + AvNum.Fixed(ordered.Count, 0) + (open > 0 ? " · " + AvNum.Fixed(open, 0) + " OPEN" : ""));
            Show(pollHeader[0], ordered.Count > 1);
            Show(pollHeader[1], ordered.Count > 1);

            CommsPoll poll = ordered.Count > 0 ? ordered[pollIndex] : null;
            shownPoll = poll != null ? poll.Id : 0;
            Show(pollClose, poll != null && !poll.Closed && (poll.Author == comms.LocalId || comms.IsHost));

            if (poll != null)
            {
                string when = poll.Closed ? "CLOSED · " + poll.Verdict() : "CLOSES IN " + CommsText.Countdown(poll.Closes - now);
                string meta = Who(poll.Author, poll.AuthorName) + " · " +
                    (poll.Channel == CommsChannel.All ? "ALL" : "TEAM") + " · " + AvNum.Fixed(poll.Total, 0) + " VOTES · " + when;
                pollRow.Set(poll.Question, meta, "", poll.Closed ? AvState.Ready : AvState.Info);
            }
            else pollRow.Set("NO POLL OPEN · PICK A QUICK POLL OR ASK YOUR OWN BELOW", null, "", AvState.Inert);

            int optionCount = poll != null ? Mathf.Min(poll.Options.Length, optionRows.Length) : 0;
            for (int i = 0; i < optionRows.Length; i++)
            {
                bool shown = i < optionCount;
                Show(optionRows[i], shown);
                if (!shown) continue;
                int total = Mathf.Max(1, poll.Total);
                bool mine = poll.LocalVote == i;
                bool winner = poll.Closed && poll.Leader == i;
                optionRows[i].Set(poll.Options[i], mine, winner, poll.Closed, poll.Tally[i] / (float)total, poll.Tally[i]);
            }

            // Template tips carry the audience and length, refreshed only when those change.
            string helpKey = (comms.Channel == CommsChannel.Team ? "TEAM" : "ALL") + comms.PollDurationIndex;
            if (helpKey != templateHelpKey)
            {
                templateHelpKey = helpKey;
                string audience = comms.Channel == CommsChannel.Team ? "your team" : "everyone";
                string length = CommsText.Countdown(CommsCatalog.PollDuration(comms.PollDurationIndex));
                for (int i = 0; i < templateRows.Length; i++)
                    templateRows[i].Help = "Ask \"" + CommsCatalog.PollTemplates[i].Question + "\" to " + audience +
                        ", open for " + length + ". One click sends it.";
            }
            durationStrip.Refresh();

            // ---- every poll on record, sized to what is left of the page
            float fixedHeight = PollFixedHeight + optionCount * 41f + (poll != null ? 16f : 0f);
            int fit = FitRows(console.Page(TabPoll), fixedHeight, PollHistoryRows, 0);
            int rows = Mathf.Min(fit, ordered.Count);
            for (int i = 0; i < PollHistoryRows; i++)
            {
                bool shown = i < rows;
                Show(historyRows[i], shown);
                if (!shown) { historyIds[i] = 0; continue; }
                CommsPoll entry = ordered[i];
                historyIds[i] = entry.Id;
                historyRows[i].Set(entry.Question,
                    null,
                    entry.Closed ? "CLOSED" : CommsText.Countdown(entry.Closes - now),
                    entry.Closed ? AvState.Inert : AvState.Ready);
                historyRows[i].Armed = entry.Id == shownPoll;
                historyRows[i].Help = "Show this poll above: " + AvNum.Fixed(entry.Total, 0) + " votes" +
                    (entry.Closed ? ", settled: " + entry.Verdict() + "." : ", still open.");
            }
            Show(historySection, rows > 0);
            historySection.SetCaption(ordered.Count > rows ? AvNum.Fixed(rows, 0) + " OF " + AvNum.Fixed(ordered.Count, 0) : "CLICK TO SHOW");
            Show(pollTips, ordered.Count == 0);
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
                count.text = AvNum.Fixed(tally, 0) + "\u00b7" + AvNum.Percent(fill01);
            }

            public override float Measure(float width) => 36f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float countW = 70f, trackW = 84f, voteW = Mathf.Max(0f, s.W - countW - trackW - 16f);
                AvLay.Place(vote.Rect, 0f, 0f, voteW, s.H);
                AvLay.Place((RectTransform)track.transform, voteW + 4f, (s.H - 6f) * 0.5f, trackW, 6f);
                AvLay.Place(count.rectTransform, s.W - countW, 0f, countW, s.H);
            }

            public override void Restyle()
            {
                vote.Restyle();
                track.Track = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
                track.SetVerticesDirty();
            }
        }
    }
}
