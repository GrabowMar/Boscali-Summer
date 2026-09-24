using System.Collections.Generic;
using BoscaliSummer.Features.Comms.Domain;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int RecentCalls = 8;
        private const float CallButton = 34f;
        private const float OptionHeight = 28f;

        // ---- CALL --------------------------------------------------------------------------
        private TMP_Text callNote;
        private TMP_Text[] recentTimes;
        private TMP_Text[] recentLines;
        private Image[] recentRails;

        // ---- POLL --------------------------------------------------------------------------
        private TMP_Text pollNote;
        private TMP_Text pollQuestion;
        private TMP_Text pollMeta;
        private TMP_Text pollEmpty;
        private AvButton pollClose;
        private AvButton pollPrev;
        private AvButton pollNext;
        private RectTransform[] optionRows;
        private AvButton[] optionVotes;
        private Image[] optionFills;
        private TMP_Text[] optionCounts;
        private float optionTrackWidth;
        private int pollIndex;
        private uint shownPoll;

        private TMP_Text askNote;
        private TMP_Text templateValue;
        private TMP_Text durationValue;
        private TMP_InputField questionField;
        private TMP_InputField optionsField;
        private int templateIndex;

        private void ResetTalk()
        {
            callNote = null;
            recentTimes = null;
            recentLines = null;
            recentRails = null;
            pollNote = pollQuestion = pollMeta = pollEmpty = null;
            pollClose = pollPrev = pollNext = null;
            optionRows = null;
            optionVotes = null;
            optionFills = null;
            optionCounts = null;
            pollIndex = 0;
            shownPoll = 0;
            askNote = templateValue = durationValue = null;
            questionField = optionsField = null;
        }

        private void BuildCallPage(GameObject page)
        {
            int rows = (CommsCatalog.Calls.Length + 1) / 2;
            float build = HeadingHeight * 2 + rows * (CallButton + Gap) + SectionGap + RecentCalls * 20f + 12f;
            RectTransform parent = PageBody(page, build, out float x, out float y, out float width);

            callNote = Heading(parent, x, ref y, width, "BREVITY CALLS", "");
            for (int i = 0; i < CommsCatalog.Calls.Length; i++)
            {
                int call = i;
                BrevityCall brevity = CommsCatalog.Calls[i];
                string glyph = brevity.MarksPosition ? CommsCatalog.Pings[brevity.PingAtSelf].Glyph : "comms";
                string tip = brevity.Meaning + (brevity.MarksPosition ? " Also drops a ping at your aircraft." : "");
                IconButton(parent, Cell(x, y - (i / 2) * (CallButton + Gap), width, CallButton, 2, i % 2), glyph,
                    brevity.Code, ToneColour(brevity.Tone), () => comms.Call(call), tip, out _);
            }
            y -= rows * (CallButton + Gap) + SectionGap;

            Heading(parent, x, ref y, width, "RECENT CALLS", "NEWEST FIRST");
            recentTimes = new TMP_Text[RecentCalls];
            recentLines = new TMP_Text[RecentCalls];
            recentRails = new Image[RecentCalls];
            for (int i = 0; i < RecentCalls; i++)
            {
                recentRails[i] = AvKit.Panel(parent, new Rect(x, y - 3f, 3f, 12f), AvTheme.RailInert);
                recentTimes[i] = AvStyled.Label(parent, new Rect(x + 8f, y, 36f, 18f), "", "row-sub");
                recentLines[i] = AvStyled.Label(parent, new Rect(x + 46f, y, width - 46f, 18f), "", "row-main");
                recentLines[i].enableWordWrapping = false;
                recentLines[i].overflowMode = TextOverflowModes.Ellipsis;
                y -= 20f;
            }
        }

        private void RefreshCalls(float now)
        {
            if (recentLines == null) return;
            callNote.text = comms.Channel == CommsChannel.Team ? "ONE CLICK · TO YOUR TEAM" : "ONE CLICK · TO ALL PLAYERS";

            IReadOnlyList<CommsFeedLine> feed = comms.State.Feed;
            int shown = 0;
            for (int i = feed.Count - 1; i >= 0 && shown < RecentCalls; i--)
            {
                CommsFeedLine line = feed[i];
                if (line.Kind != CommsFeedKind.Call || comms.State.IsMuted(line.Author)) continue;
                recentRails[shown].color = ToneColour(line.Tone);
                recentTimes[shown].text = Ago(now - line.Time);
                recentLines[shown].text = Who(line.Author, line.AuthorName) + " · " + line.Text;
                Show(recentRails[shown], true);
                shown++;
            }
            for (int i = shown; i < RecentCalls; i++)
            {
                recentTimes[i].text = "";
                recentLines[i].text = i == 0 && shown == 0 ? "No calls yet. Everything your side calls shows up here." : "";
                Show(recentRails[i], false);
            }
        }

        // ---- POLL --------------------------------------------------------------------------

        private void BuildPollPage(GameObject page)
        {
            float build = HeadingHeight * 3 + 36f + CommsPoll.MaxOptions * (OptionHeight + Gap) + SectionGap * 3 +
                          (RowButton + Gap) * 4 + 30f;
            RectTransform parent = PageBody(page, build, out float x, out float y, out float width);

            pollNote = Heading(parent, x, ref y, width, "OPEN POLL", "");
            // The pager sits right after the title so it never collides with the count on the right.
            pollPrev = Button(parent, new Rect(x + 96f, y + HeadingHeight + 1f, 24f, 16f), "<", () => pollIndex--,
                "Previous poll.", AvButtonStyle.Quiet);
            pollNext = Button(parent, new Rect(x + 124f, y + HeadingHeight + 1f, 24f, 16f), ">", () => pollIndex++,
                "Next poll.", AvButtonStyle.Quiet);

            pollQuestion = AvStyled.Label(parent, new Rect(x, y, width - 84f, 18f), "", "row-name");
            pollClose = Button(parent, new Rect(x + width - 78f, y, 78f, 20f), "CLOSE", () =>
            {
                if (shownPoll != 0) comms.ClosePoll(shownPoll);
            }, "Close your poll now and announce the result.", AvButtonStyle.Danger);
            y -= 20f;
            pollMeta = AvStyled.Label(parent, new Rect(x, y, width, 16f), "", "row-sub");
            y -= 20f;
            pollEmpty = AvStyled.Label(parent, new Rect(x, y, width, 34f),
                "No poll is open. Ask one below: everyone who can see it gets a HUD notice and one click to vote.", "hint");

            optionRows = new RectTransform[CommsPoll.MaxOptions];
            optionVotes = new AvButton[CommsPoll.MaxOptions];
            optionFills = new Image[CommsPoll.MaxOptions];
            optionCounts = new TMP_Text[CommsPoll.MaxOptions];
            float voteWidth = Mathf.Floor(width * 0.42f);
            optionTrackWidth = width - voteWidth - 52f;
            for (int i = 0; i < CommsPoll.MaxOptions; i++)
            {
                int option = i;
                RectTransform row = Container(parent, new Rect(x, y - i * (OptionHeight + Gap), width, OptionHeight), "Option" + i);
                optionRows[i] = row;
                optionVotes[i] = Button(row, new Rect(0f, 0f, voteWidth, OptionHeight), "", () =>
                {
                    if (shownPoll != 0) comms.Vote(shownPoll, option);
                }, "Vote for this option. You can change your mind until the poll closes.");
                AvKit.Panel(row, new Rect(voteWidth + 8f, -(OptionHeight - 8f) * 0.5f, optionTrackWidth, 8f), AvTheme.SurfaceInert);
                optionFills[i] = AvKit.Panel(row, new Rect(voteWidth + 8f, -(OptionHeight - 8f) * 0.5f, 0f, 8f), AvTheme.Accent);
                optionCounts[i] = AvStyled.Label(row, new Rect(width - 40f, 0f, 40f, OptionHeight), "", "row-value",
                    align: TextAlignmentOptions.MidlineRight);
            }
            y -= CommsPoll.MaxOptions * (OptionHeight + Gap) + SectionGap;

            askNote = Heading(parent, x, ref y, width, "ASK", "");
            AvKit.Stepper(parent, x, y, width - 96f, out templateValue,
                () => templateIndex = Wrap(templateIndex - 1, CommsCatalog.PollTemplates.Length),
                () => templateIndex = Wrap(templateIndex + 1, CommsCatalog.PollTemplates.Length),
                "Ready-made questions, so a vote mid-flight costs one click.");
            Button(parent, new Rect(x + width - 90f, y, 90f, RowButton), "ASK", () =>
            {
                PollTemplate template = CommsCatalog.PollTemplates[Wrap(templateIndex, CommsCatalog.PollTemplates.Length)];
                comms.CreatePoll(template.Question, template.Options);
            }, "Ask the selected question.", AvButtonStyle.Primary);
            y -= RowButton + Gap;
            AvKit.Stepper(parent, x, y, width - 96f, out durationValue,
                () => comms.PollDurationIndex = Wrap(comms.PollDurationIndex - 1, CommsCatalog.PollDurations.Length),
                () => comms.PollDurationIndex = Wrap(comms.PollDurationIndex + 1, CommsCatalog.PollDurations.Length),
                "How long the poll stays open.");
            y -= RowButton + SectionGap;

            Heading(parent, x, ref y, width, "CUSTOM POLL", "2-4 OPTIONS, SEPARATED BY COMMAS");
            questionField = AvKit.InputField(parent, new Rect(x, y, width, RowButton), CommsText.MaxQuestion, null,
                placeholderText: "QUESTION…", tooltip: "Up to " + CommsText.MaxQuestion + " characters.");
            y -= RowButton + Gap;
            optionsField = AvKit.InputField(parent, new Rect(x, y, width - 96f, RowButton), 80, null,
                placeholderText: "YES, NO, MAYBE", tooltip: "Two to four options, separated by commas.");
            Button(parent, new Rect(x + width - 90f, y, 90f, RowButton), "ASK", AskCustom,
                "Ask your own question.", AvButtonStyle.Primary);
        }

        private void AskCustom()
        {
            string question = questionField != null ? questionField.text : "";
            string[] options = CommsText.SplitOptions(optionsField != null ? optionsField.text : "", CommsPoll.MaxOptions);
            if (CommsText.Clean(question, CommsText.MaxQuestion).Length == 0 || options.Length < CommsPoll.MinOptions)
            {
                comms.State.SetNotice("TYPE A QUESTION AND 2-4 OPTIONS, E.G. YES, NO", true, Time.unscaledTime);
                return;
            }
            comms.CreatePoll(question, options);
            if (questionField != null) questionField.text = "";
            if (optionsField != null) optionsField.text = "";
        }

        private void RefreshPolls(float now)
        {
            if (optionRows == null) return;
            IReadOnlyList<CommsPoll> polls = comms.State.Polls;

            // Open polls first, newest first; then the settled ones, so a result stays readable.
            var ordered = new List<CommsPoll>(polls.Count);
            for (int i = polls.Count - 1; i >= 0; i--) if (!polls[i].Closed && !comms.State.IsMuted(polls[i].Author)) ordered.Add(polls[i]);
            int open = ordered.Count;
            for (int i = polls.Count - 1; i >= 0; i--) if (polls[i].Closed && !comms.State.IsMuted(polls[i].Author)) ordered.Add(polls[i]);

            pollIndex = ordered.Count == 0 ? 0 : Wrap(pollIndex, ordered.Count);
            pollNote.text = ordered.Count == 0 ? "NONE" : (pollIndex + 1) + " OF " + ordered.Count + (open > 0 ? " · " + open + " OPEN" : "");
            Show(pollPrev, ordered.Count > 1);
            Show(pollNext, ordered.Count > 1);

            CommsPoll poll = ordered.Count > 0 ? ordered[pollIndex] : null;
            shownPoll = poll != null ? poll.Id : 0;
            Show(pollEmpty, poll == null);
            Show(pollQuestion, poll != null);
            Show(pollMeta, poll != null);
            Show(pollClose, poll != null && !poll.Closed && (poll.Author == comms.LocalId || comms.IsHost));

            for (int i = 0; i < optionRows.Length; i++)
            {
                bool shown = poll != null && i < poll.Options.Length;
                Show(optionRows[i], shown);
                if (!shown) continue;
                int total = Mathf.Max(1, poll.Total);
                bool mine = poll.LocalVote == i;
                bool winner = poll.Closed && poll.Leader == i;
                optionVotes[i].SetText((mine ? "> " : "") + poll.Options[i]);
                optionVotes[i].SetLatched(mine || winner);
                optionVotes[i].SetEnabled(!poll.Closed);
                optionFills[i].rectTransform.sizeDelta = new Vector2(optionTrackWidth * poll.Tally[i] / total, 8f);
                optionFills[i].color = winner ? AvTheme.RailReady : mine ? AvTheme.Accent : AvTheme.RailInfo;
                optionCounts[i].text = poll.Tally[i].ToString();
            }

            if (poll != null)
            {
                pollQuestion.text = poll.Question;
                string when = poll.Closed ? "CLOSED · " + poll.Verdict() : "CLOSES IN " + CommsText.Countdown(poll.Closes - now);
                pollMeta.text = "ASKED BY " + Who(poll.Author, poll.AuthorName) + " · " +
                                (poll.Channel == CommsChannel.All ? "ALL" : "TEAM") + " · " + poll.Total + " VOTES · " + when;
                pollMeta.color = poll.Closed ? AvTheme.RailReady : AvTheme.Dim;
            }

            askNote.text = comms.Channel == CommsChannel.Team ? "YOUR TEAM VOTES" : "EVERY PLAYER VOTES";
            PollTemplate template = CommsCatalog.PollTemplates[Wrap(templateIndex, CommsCatalog.PollTemplates.Length)];
            templateValue.text = template.Question;
            durationValue.text = "OPEN FOR " + CommsText.Countdown(CommsCatalog.PollDuration(comms.PollDurationIndex));
        }

        private static int Wrap(int value, int count) => count <= 0 ? 0 : ((value % count) + count) % count;
    }
}
