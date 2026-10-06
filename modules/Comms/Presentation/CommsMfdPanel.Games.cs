using NOAvionics;
using System.Collections.Generic;
using BoscaliSummer.Modules.Comms.Domain;
using BoscaliSummer.Modules.Comms.Runtime;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int DuelRows = 3;
        private const int HuntRows = 2;
        private const int ScoreRows = 14;
        private const int MinScoreRows = 3;

        // Part heights, gaps and padding of the CREW page with one duel row and one hunt row on screen (biased a few
        // px high); each extra duel or hunt row adds 33 px. The leaderboard takes what is left.
        private const float GameFixedHeight = 346f;
        private static readonly string[] HuntSeconds = { "30s", "45s", "60s", "90s" };

        private AvSection huntSection;
        private AvRow liveRow;
        private AvControl liveAction;
        private uint liveHunt;

        private AvSection diceSection;

        private float nextGg;
        private ulong rematchTarget;
        private string rematchName;
        private CommsChannel rematchChannel;

        private AvRow[] duelRows;
        private AvControl[][] duelAnswers;
        private bool[] duelMine;
        private uint[] duelIds;

        private AvSegmented huntDuration;
        private AvRow[] huntRows;
        private AvControl[] huntGuess;
        private uint[] huntIds;

        private AvRow sessionRow;
        private AvControl ggButton;

        private AvRow[] scoreRows;
        private AvNote scoreTips;

        private void ResetGames()
        {
            liveRow = null;
            liveAction = null;
            liveHunt = 0;
            huntSection = null;
            diceSection = null;
            nextGg = 0f;
            rematchTarget = 0;
            rematchName = null;
            duelRows = null;
            duelAnswers = null;
            duelMine = null;
            duelIds = null;
            huntDuration = null;
            huntRows = null;
            huntGuess = null;
            huntIds = null;
            sessionRow = null;
            ggButton = null;
            scoreRows = null;
            scoreTips = null;
        }

        private void SelectRematch(ulong player, string name, CommsChannel channel)
        {
            rematchTarget = player;
            rematchName = CommsText.Name(name);
            rematchChannel = channel;
            nextRefresh = 0f;
        }

        private void BuildGamePage(AvFlow p)
        {
            // One "what needs me now" line, then the quick games: dice and throws share one header, whose caption
            // carries the latest roll (or the rematch target) instead of a row of its own.
            liveRow = p.Add(new AvRow(p.Content));
            liveAction = liveRow.AddTrailing(new AvControl.Spec("JOIN", () =>
            {
                if (liveHunt != 0) comms.ArmHuntGuess(liveHunt);
                else console.SetPage(TabPoll);
            }, AvButtonStyle.Default, AvIcon.ArrowUpRight));
            liveAction.Help = "Join the hunt or poll that needs you: arm your one guess, or jump to the poll to vote.";
            liveRow.Help = "What is live right now for you: a hidden target to find, an open poll to vote on, or a challenge to answer.";

            diceSection = p.Section(AvIcon.Gauge, "DICE · ROCK PAPER SCISSORS", "");
            var diceSpecs = new AvControl.Spec[CommsCatalog.DiceSides.Length];
            var diceHelps = new string[diceSpecs.Length];
            for (int i = 0; i < diceSpecs.Length; i++)
            {
                int die = i;
                diceSpecs[i] = new AvControl.Spec(CommsCatalog.DiceNames[i], () => comms.Roll(die));
                diceHelps[i] = i == 0 ? "Flip a coin. The host flips it, so nobody can load it. The result shows in the header."
                    : "Roll a " + CommsCatalog.DiceNames[i] + ". The host rolls it, so nobody can load it. The result shows in the header.";
            }
            ButtonGrid(p, diceSpecs, diceSpecs.Length, diceHelps);

            var throwSpecs = new AvControl.Spec[CommsCatalog.Throws.Length];
            var throwHelps = new string[throwSpecs.Length];
            for (int i = 0; i < throwSpecs.Length; i++)
            {
                int throwIndex = i;
                throwSpecs[i] = new AvControl.Spec(CommsCatalog.Throws[i], () =>
                {
                    comms.Challenge(throwIndex, rematchTarget, rematchTarget == 0 ? (CommsChannel?)null : rematchChannel);
                    rematchTarget = 0;
                    rematchName = null;
                });
                throwHelps[i] = "Challenge the audience with " + CommsCatalog.Throws[i] +
                    ". Your throw stays secret on the host until someone answers. After a duel, LOG rematch lines aim this at that pilot.";
            }
            ButtonGrid(p, throwSpecs, throwSpecs.Length, throwHelps);

            // A challenge is one line: the answers are the row's own keys, and on your own challenge the first key turns into CANCEL.
            duelRows = new AvRow[DuelRows];
            duelAnswers = new AvControl[DuelRows][];
            duelMine = new bool[DuelRows];
            duelIds = new uint[DuelRows];
            for (int r = 0; r < DuelRows; r++)
            {
                int row = r;
                duelRows[r] = p.Add(new AvRow(p.Content));
                duelAnswers[r] = new AvControl[CommsCatalog.Throws.Length];
                for (int t = 0; t < CommsCatalog.Throws.Length; t++)
                {
                    int throwIndex = t;
                    duelAnswers[r][t] = duelRows[r].AddTrailing(new AvControl.Spec(ThrowLabel(t), () =>
                    {
                        if (duelIds[row] == 0) return;
                        if (duelMine[row]) comms.CancelDuel(duelIds[row]);
                        else comms.AcceptDuel(duelIds[row], throwIndex);
                    }));
                    duelAnswers[r][t].Help = "Answer with " + CommsCatalog.Throws[t] + ".";
                }
            }

            huntSection = p.Section(AvIcon.Target, "MAP HUNT", "HIDE · GUESS · SCORE");
            huntDuration = AvSegmented.Strip(p.Content, HuntSeconds,
                () => comms.HuntDurationIndex, index => comms.HuntDurationIndex = index);
            for (int i = 0; i < huntDuration.Options.Length; i++)
                huntDuration.Options[i].Help = "Round length: everyone has " + CommsText.Countdown(CommsCatalog.HuntDuration(i)) + " to guess.";
            var hideRow = new AvButtons(p.Content, new[] { new AvControl.Spec("HIDE TARGET", () => comms.SetTool(CommsTool.HuntHide), AvButtonStyle.Default, AvIcon.Target) });
            hideRow.Controls[0].Help = "Click the map to hide a target. Everyone else gets one click to find it; closest three score, a bullseye scores extra.";
            p.Row(huntDuration, hideRow);

            huntRows = new AvRow[HuntRows];
            huntGuess = new AvControl[HuntRows];
            huntIds = new uint[HuntRows];
            for (int r = 0; r < HuntRows; r++)
            {
                int row = r;
                huntRows[r] = p.Add(new AvRow(p.Content));
                huntGuess[r] = huntRows[r].AddTrailing(new AvControl.Spec("GUESS", () =>
                {
                    if (huntIds[row] != 0) comms.ArmHuntGuess(huntIds[row]);
                }, AvButtonStyle.Default, AvIcon.Target));
                huntGuess[r].Help = "Arm your one guess, then click the map.";
            }

            // Your line (score, hunts, rival) sits on top of the board it belongs to; the board fills the page.
            p.Section(AvIcon.Crown, "LEADERBOARD", "THIS MISSION");
            sessionRow = p.Add(new AvRow(p.Content));
            sessionRow.Help = "Your points, wins and best hunt this mission, and your most active rival.";
            ggButton = sessionRow.AddTrailing(new AvControl.Spec("GG", () =>
            {
                if (Time.unscaledTime < nextGg) return;
                for (int i = 0; i < CommsCatalog.Calls.Length; i++)
                    if (CommsCatalog.Calls[i].Code == "GG") { comms.Call(i); nextGg = Time.unscaledTime + 8f; break; }
            }, AvButtonStyle.Default, AvIcon.Flag));
            ggButton.Help = "Send a quiet GG to your team for the latest game result. Appears for a short while after a duel or hunt ends.";

            scoreRows = new AvRow[ScoreRows];
            for (int i = 0; i < ScoreRows; i++) scoreRows[i] = p.Add(new AvRow(p.Content));
            scoreTips = p.Add(new AvNote(p.Content,
                "HOW TO SCORE · win rock-paper-scissors duels and map hunts to climb the board. In a hunt the closest three " +
                "guesses score and a bullseye scores extra. Scores reset with the mission; GG appears after a result.") { MinHeight = AvGridTokens.RowDense, StretchText = true });
        }

        private static string ThrowLabel(int t) =>
            CommsCatalog.Throws[t].Substring(0, 1) + CommsCatalog.Throws[t].Substring(1).ToLowerInvariant();

        private void RefreshGames(float now)
        {
            if (duelRows == null) return;
            CommsClientState state = comms.State;

            liveHunt = 0;
            bool pollLive = false;
            string liveText = null;
            for (int i = state.Hunts.Count - 1; i >= 0; i--)
            {
                HuntView hunt = state.Hunts[i];
                if (hunt.Revealed || hunt.Author == comms.LocalId || hunt.HasLocalGuess || state.IsMuted(hunt.Author)) continue;
                liveHunt = hunt.Id;
                liveText = hunt.AuthorName + " HID A TARGET · " + CommsText.Countdown(hunt.Ends - now);
                break;
            }
            if (liveHunt == 0)
                for (int i = state.Polls.Count - 1; i >= 0; i--)
                {
                    CommsPoll poll = state.Polls[i];
                    if (poll.Closed || poll.LocalVote >= 0 || state.IsMuted(poll.Author)) continue;
                    pollLive = true;
                    liveText = "POLL · " + poll.Question;
                    break;
                }
            if (liveHunt == 0 && !pollLive)
                for (int i = state.Duels.Count - 1; i >= 0; i--)
                {
                    DuelView duel = state.Duels[i];
                    if (duel.Challenger == comms.LocalId || state.IsMuted(duel.Challenger) ||
                        duel.TargetPlayer != 0 && duel.TargetPlayer != comms.LocalId) continue;
                    liveText = duel.ChallengerName + " CHALLENGES · PICK A THROW";
                    break;
                }
            liveRow.Set(liveText ?? "NOTHING LIVE · ROLL, THROW OR HIDE A TARGET", null, "", liveText == null ? AvState.Inert : AvState.Info);
            liveAction.Label = liveHunt != 0 ? "GUESS" : "VOTE";
            Show(liveAction, liveHunt != 0 || pollLive);

            // ---- dice: the latest roll anyone in earshot made, or the rematch in progress
            string roll = null;
            IReadOnlyList<CommsFeedLine> feed = state.Feed;
            for (int i = feed.Count - 1; i >= 0; i--)
            {
                CommsFeedLine line = feed[i];
                if (line.Kind != CommsFeedKind.Roll || state.IsMuted(line.Author)) continue;
                roll = Who(line.Author, line.AuthorName) + " " + line.Text + " · " + Ago(now - line.Time) + " AGO";
                break;
            }
            diceSection.SetCaption(rematchTarget != 0 ? "REMATCH " + rematchName : roll ?? "HOST ROLLS FOR ALL");

            // ---- duels
            IReadOnlyList<DuelView> duels = state.Duels;
            int duelShown = 0;
            for (int i = duels.Count - 1; i >= 0 && duelShown < DuelRows; i--)
            {
                DuelView duel = duels[i];
                if (state.IsMuted(duel.Challenger) ||
                    (duel.TargetPlayer != 0 && duel.TargetPlayer != comms.LocalId && duel.Challenger != comms.LocalId)) continue;
                bool mine = duel.Challenger == comms.LocalId;
                duelIds[duelShown] = duel.Id;
                duelMine[duelShown] = mine;
                string label = (mine ? duel.TargetPlayer == 0 ? "YOUR CHALLENGE" : "YOUR DIRECT CHALLENGE" :
                    duel.ChallengerName + (duel.TargetPlayer == comms.LocalId ? " CHALLENGES YOU" : " CHALLENGES")) + " · " +
                    CommsText.Countdown(duel.Expires - now);
                duelRows[duelShown].Set(label, null, "", AvState.Info);
                bool canAnswer = !mine && (duel.TargetPlayer == 0 || duel.TargetPlayer == comms.LocalId);
                for (int t = 0; t < duelAnswers[duelShown].Length; t++)
                {
                    AvControl key = duelAnswers[duelShown][t];
                    Show(key, canAnswer || mine && t == 0);
                    key.Label = mine ? "CANCEL" : ThrowLabel(t);
                    key.Help = mine ? "Take your challenge back." : "Answer with " + CommsCatalog.Throws[t] + ".";
                }
                Show(duelRows[duelShown], true);
                duelShown++;
            }
            for (int i = duelShown; i < DuelRows; i++)
            {
                duelIds[i] = 0;
                duelMine[i] = false;
                if (i == 0 && duelShown == 0)
                {
                    duelRows[0].Set("NO OPEN CHALLENGES · THROW ABOVE TO START ONE", null, "", AvState.Inert);
                    for (int t = 0; t < duelAnswers[0].Length; t++) Show(duelAnswers[0][t], false);
                    Show(duelRows[0], true);
                }
                else Show(duelRows[i], false);
            }
            int duelLines = Mathf.Max(1, duelShown);

            // ---- hunts
            huntDuration.Refresh();
            IReadOnlyList<HuntView> hunts = state.Hunts;
            int huntShown = 0;
            for (int i = hunts.Count - 1; i >= 0 && huntShown < HuntRows; i--)
            {
                HuntView hunt = hunts[i];
                if (state.IsMuted(hunt.Author)) continue;
                bool mine = hunt.Author == comms.LocalId;
                huntIds[huntShown] = hunt.Id;
                string label = hunt.Revealed
                    ? (hunt.Placings.Count == 0
                        ? "OVER · NOBODY GUESSED " + Who(hunt.Author, hunt.AuthorName) + "'S TARGET"
                        : "OVER · " + hunt.Placings[0].Name + " WINS, " +
                          CommsText.Distance(hunt.Placings[0].Metres, VanillaHudStyle.Metric) + " OFF")
                    : (mine ? "YOUR TARGET" : hunt.AuthorName + " HID A TARGET") + " · " +
                      CommsText.Countdown(hunt.Ends - now) + " · " + AvNum.Fixed(hunt.GuessCount, 0) +
                      (hunt.GuessCount == 1 ? " GUESS" : " GUESSES");
                huntRows[huntShown].Set(label, null, "", AvState.Info);
                bool canGuess = !hunt.Revealed && !mine && !hunt.HasLocalGuess;
                Show(huntGuess[huntShown], canGuess);
                huntGuess[huntShown].Latched = comms.Tool == CommsTool.HuntGuess;
                Show(huntRows[huntShown], true);
                huntShown++;
            }
            for (int i = huntShown; i < HuntRows; i++)
            {
                huntIds[i] = 0;
                if (i == 0 && huntShown == 0)
                {
                    huntRows[0].Set("NO HUNT RUNNING · HIDE A TARGET FOR THE OTHERS TO FIND", null, "", AvState.Inert);
                    Show(huntGuess[0], false);
                    Show(huntRows[0], true);
                }
                else Show(huntRows[i], false);
            }
            int huntLines = Mathf.Max(1, huntShown);

            // ---- your line: the score, plus the local player's most active head-to-head story
            RivalryView rivalry = null;
            IReadOnlyList<RivalryView> rivalries = state.Rivalries;
            for (int i = 0; i < rivalries.Count; i++)
            {
                RivalryView row = rivalries[i];
                if ((row.First != comms.LocalId && row.Second != comms.LocalId) ||
                    state.IsMuted(row.First == comms.LocalId ? row.Second : row.First)) continue;
                if (rivalry == null || row.FirstWins + row.SecondWins + row.Draws >
                    rivalry.FirstWins + rivalry.SecondWins + rivalry.Draws) rivalry = row;
            }
            string rivalText = "NO RIVAL YET · DUEL SOMEONE";
            if (rivalry != null)
            {
                bool first = rivalry.First == comms.LocalId;
                string opponent = first ? rivalry.SecondName : rivalry.FirstName;
                int mine = first ? rivalry.FirstWins : rivalry.SecondWins;
                int theirs = first ? rivalry.SecondWins : rivalry.FirstWins;
                rivalText = (mine + theirs + rivalry.Draws >= 3 ? "NEMESIS · " : "RIVAL · ") + opponent + " · YOU " +
                    AvNum.Fixed(mine, 0) + " : " + AvNum.Fixed(theirs, 0) + " · " + AvNum.Fixed(rivalry.Draws, 0) + " DRAWS";
            }
            int wins = 0;
            IReadOnlyList<ScoreRow> rows = state.Scores.Rows;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Player == comms.LocalId) { wins = rows[i].Wins; break; }
            string profile = AvNum.Fixed(state.Scores.PointsOf(comms.LocalId), 0) + " PTS · " + AvNum.Fixed(wins, 0) + " WINS · " +
                AvNum.Fixed(state.HuntWins, 0) + " HUNTS WON" + (state.BestHuntMetres == int.MaxValue ? "" :
                " · CLOSEST " + CommsText.Distance(state.BestHuntMetres, VanillaHudStyle.Metric));
            sessionRow.Set("YOU · " + profile, rivalText, "", AvState.Ready);
            bool recentResult = false;
            for (int i = state.Feed.Count - 1; i >= 0; i--)
            {
                CommsFeedLine line = state.Feed[i];
                if (now - line.Time > 30f) break;
                if (!state.IsMuted(line.Author) && (line.Kind == CommsFeedKind.Duel ||
                    line.Kind == CommsFeedKind.Hunt && line.Text.StartsWith("HUNT OVER"))) { recentResult = true; break; }
            }
            Show(ggButton, recentResult && now >= nextGg);

            // ---- leaderboard, sized to what is left of the page
            float fixedHeight = GameFixedHeight + (duelLines - 1) * 33f + (huntLines - 1) * 33f;
            int fit = FitRows(console.Page(TabGame), fixedHeight, ScoreRows, MinScoreRows);
            // A short console can't hold the hunt header and a full board: the header goes, the hunt strip explains itself.
            bool compact = console.Page(TabGame).ViewportHeight > 0f &&
                FitRows(console.Page(TabGame), fixedHeight, ScoreRows, 0) < MinScoreRows;
            Show(huntSection, !compact);
            if (compact) fit = FitRows(console.Page(TabGame), fixedHeight - 33f, ScoreRows, 1);
            for (int i = 0; i < ScoreRows; i++)
            {
                bool has = i < rows.Count && i < fit;
                Show(scoreRows[i], has);
                if (!has) continue;
                bool me = rows[i].Player == comms.LocalId;
                // Wins ride in the name (it wraps); the value column only ever holds a short points figure.
                string name = "#" + AvNum.Fixed(i + 1, 0) + " " + (me ? rows[i].Name + " (YOU)" : rows[i].Name) + " · " +
                    AvNum.Fixed(rows[i].Wins, 0) + (rows[i].Wins == 1 ? " WIN" : " WINS");
                string value = AvNum.Fixed(rows[i].Points, 0) + " PTS";
                scoreRows[i].Set(name, null, value, me ? AvState.Ready : AvState.Info);
            }
            Show(scoreTips, rows.Count == 0);
        }
    }
}
