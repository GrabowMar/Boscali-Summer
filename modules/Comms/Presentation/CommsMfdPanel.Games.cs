using System.Collections.Generic;
using BoscaliSummer.Features.Comms.Domain;
using BoscaliSummer.Features.Comms.Runtime;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int DuelRows = 3;
        private const int HuntRows = 2;
        private const int ScoreRows = 6;

        private AvRow liveRow;
        private AvControl liveAction;
        private uint liveHunt;

        private AvSection diceSection;
        private AvNote lastRoll;

        private AvSection duelSection;
        private float nextGg;
        private ulong rematchTarget;
        private string rematchName;
        private CommsChannel rematchChannel;

        private AvRow[] duelRows;
        private AvControl[][] duelAnswers;
        private AvControl[] duelWithdraw;
        private uint[] duelIds;

        private AvStepper huntDuration;
        private AvRow[] huntRows;
        private AvControl[] huntGuess;
        private uint[] huntIds;
        private AvNote huntEmpty;

        private AvNote rivalryNote;
        private AvRow sessionRow;
        private AvControl ggButton;

        private AvRow[] scoreRows;
        private AvNote scoreEmpty;

        private void ResetGames()
        {
            liveRow = null;
            liveAction = null;
            liveHunt = 0;
            diceSection = null;
            lastRoll = null;
            duelSection = null;
            nextGg = 0f;
            rematchTarget = 0;
            rematchName = null;
            duelRows = null;
            duelAnswers = null;
            duelWithdraw = null;
            duelIds = null;
            huntDuration = null;
            huntRows = null;
            huntGuess = null;
            huntIds = null;
            huntEmpty = null;
            rivalryNote = null;
            sessionRow = null;
            ggButton = null;
            scoreRows = null;
            scoreEmpty = null;
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
            p.Section(AvIcon.UsersGroup, "LIVE NOW", "JOIN THE MOMENT");
            liveRow = p.Add(new AvRow(p.Content));
            liveAction = liveRow.AddTrailing(new AvControl.Spec("JOIN", () =>
            {
                if (liveHunt != 0) comms.ArmHuntGuess(liveHunt);
                else console.SetPage(TabPoll);
            }, AvButtonStyle.Default, AvIcon.ArrowUpRight));
            liveAction.Help = "Join the current hunt or poll.";

            diceSection = p.Section(AvIcon.Gauge, "DICE", "");
            var diceSpecs = new AvControl.Spec[CommsCatalog.DiceSides.Length];
            var diceHelps = new string[diceSpecs.Length];
            for (int i = 0; i < diceSpecs.Length; i++)
            {
                int die = i;
                diceSpecs[i] = new AvControl.Spec(CommsCatalog.DiceNames[i], () => comms.Roll(die));
                diceHelps[i] = i == 0 ? "Flip a coin. The host flips it, so nobody can load it."
                    : "Roll a " + CommsCatalog.DiceNames[i] + ". The host rolls it, so nobody can load it.";
            }
            ButtonGrid(p, diceSpecs, diceSpecs.Length, diceHelps);
            lastRoll = p.Add(new AvNote(p.Content));

            duelSection = p.Section(AvIcon.Scale, "ROCK · PAPER · SCISSORS", "PICK A THROW TO CHALLENGE");
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
                throwHelps[i] = "Challenge with " + CommsCatalog.Throws[i] +
                    ". Your throw stays secret on the host until someone answers.";
            }
            ButtonGrid(p, throwSpecs, throwSpecs.Length, throwHelps);

            duelRows = new AvRow[DuelRows];
            duelAnswers = new AvControl[DuelRows][];
            duelWithdraw = new AvControl[DuelRows];
            duelIds = new uint[DuelRows];
            for (int r = 0; r < DuelRows; r++)
            {
                int row = r;
                duelRows[r] = p.Add(new AvRow(p.Content));
                duelAnswers[r] = new AvControl[CommsCatalog.Throws.Length];
                for (int t = 0; t < CommsCatalog.Throws.Length; t++)
                {
                    int throwIndex = t;
                    string label = CommsCatalog.Throws[t].Substring(0, 1) + CommsCatalog.Throws[t].Substring(1).ToLowerInvariant();
                    duelAnswers[r][t] = duelRows[r].AddTrailing(new AvControl.Spec(label, () =>
                    {
                        if (duelIds[row] != 0) comms.AcceptDuel(duelIds[row], throwIndex);
                    }));
                    duelAnswers[r][t].Help = "Answer with " + CommsCatalog.Throws[t] + ".";
                }
                duelWithdraw[r] = p.Buttons(new AvControl.Spec("WITHDRAW", () =>
                {
                    if (duelIds[row] != 0) comms.CancelDuel(duelIds[row]);
                }, AvButtonStyle.Quiet, AvIcon.ArrowBackUp)).Controls[0];
                duelWithdraw[r].Help = "Take your challenge back.";
            }

            p.Section(AvIcon.Target, "MAP HUNT", "HIDE A POINT · CLOSEST GUESS SCORES");
            huntDuration = p.Add(new AvStepper(p.Content, "ROUND LENGTH",
                () => CommsText.Countdown(CommsCatalog.HuntDuration(comms.HuntDurationIndex)),
                () => comms.HuntDurationIndex = Wrap(comms.HuntDurationIndex - 1, CommsCatalog.HuntDurations.Length),
                () => comms.HuntDurationIndex = Wrap(comms.HuntDurationIndex + 1, CommsCatalog.HuntDurations.Length)));
            Tip(huntDuration, "How long everyone has to guess.");
            p.Buttons(new AvControl.Spec("HIDE TARGET", () => comms.SetTool(CommsTool.HuntHide), AvButtonStyle.Default, AvIcon.Target))
                .Controls[0].Help = "Click the map to hide a target. Everyone else gets one click to find it; closest three score, a bullseye scores extra.";

            huntEmpty = p.Add(new AvNote(p.Content, "No hunt running. Hide a target and see who reads the map best."));
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

            p.Section(AvIcon.Crown, "RIVALRY", "SINCE YOU JOINED");
            rivalryNote = p.Add(new AvNote(p.Content, "Challenge someone twice to start a friendly rivalry."));

            p.Section(AvIcon.User, "YOUR SESSION", "FOR FUN ONLY");
            sessionRow = p.Add(new AvRow(p.Content));
            ggButton = sessionRow.AddTrailing(new AvControl.Spec("GG", () =>
            {
                if (Time.unscaledTime < nextGg) return;
                for (int i = 0; i < CommsCatalog.Calls.Length; i++)
                    if (CommsCatalog.Calls[i].Code == "GG") { comms.Call(i); nextGg = Time.unscaledTime + 8f; break; }
            }, AvButtonStyle.Default, AvIcon.Flag));
            ggButton.Help = "Send a quiet GG to your team for the latest game result.";

            p.Section(AvIcon.Crown, "LEADERBOARD", "FUN POINTS · THIS MISSION");
            scoreEmpty = p.Add(new AvNote(p.Content, "Win a duel or a hunt to get on the board."));
            scoreRows = new AvRow[ScoreRows];
            for (int i = 0; i < ScoreRows; i++) scoreRows[i] = p.Add(new AvRow(p.Content));
        }

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
                    liveText = duel.ChallengerName + " CHALLENGES · PICK A THROW BELOW";
                    break;
                }
            liveRow.Set(liveText ?? "Nothing to join. Start a hunt, poll or challenge.", null, "", AvState.Info);
            liveAction.Label = liveHunt != 0 ? "GUESS" : "VOTE";
            Show(liveAction, liveHunt != 0 || pollLive);
            duelSection?.SetCaption(rematchTarget == 0 ? "PICK A THROW TO CHALLENGE" : "REMATCH " + rematchName);

            // ---- dice: the latest roll anyone in earshot made
            string roll = null;
            IReadOnlyList<CommsFeedLine> feed = state.Feed;
            for (int i = feed.Count - 1; i >= 0; i--)
            {
                CommsFeedLine line = feed[i];
                if (line.Kind != CommsFeedKind.Roll || state.IsMuted(line.Author)) continue;
                roll = Who(line.Author, line.AuthorName) + " " + line.Text + " · " + Ago(now - line.Time) + " AGO";
                break;
            }
            lastRoll.Text = roll ?? "Settle it the old way: the host rolls, everyone sees the same number.";
            diceSection?.SetCaption(comms.Channel == CommsChannel.Team ? "ROLLED FOR YOUR TEAM" : "ROLLED FOR EVERYONE");

            // ---- duels
            IReadOnlyList<DuelView> duels = state.Duels;
            int shown = 0;
            for (int i = duels.Count - 1; i >= 0 && shown < DuelRows; i--)
            {
                DuelView duel = duels[i];
                if (state.IsMuted(duel.Challenger) ||
                    (duel.TargetPlayer != 0 && duel.TargetPlayer != comms.LocalId && duel.Challenger != comms.LocalId)) continue;
                bool mine = duel.Challenger == comms.LocalId;
                duelIds[shown] = duel.Id;
                string label = (mine ? duel.TargetPlayer == 0 ? "YOUR CHALLENGE" : "YOUR DIRECT CHALLENGE" :
                    duel.ChallengerName + (duel.TargetPlayer == comms.LocalId ? " CHALLENGES YOU" : " CHALLENGES")) + " · " +
                    CommsText.Countdown(duel.Expires - now);
                duelRows[shown].Set(label, null, "", AvState.Info);
                for (int t = 0; t < duelAnswers[shown].Length; t++)
                    Show(duelAnswers[shown][t], !mine && (duel.TargetPlayer == 0 || duel.TargetPlayer == comms.LocalId));
                Show(duelWithdraw[shown], mine);
                Show(duelRows[shown], true);
                shown++;
            }
            for (int i = shown; i < DuelRows; i++)
            {
                duelIds[i] = 0;
                Show(duelWithdraw[i], false);
                if (i == 0 && shown == 0)
                {
                    duelRows[0].Set("No open challenges.", null, "", AvState.Inert);
                    for (int t = 0; t < duelAnswers[0].Length; t++) Show(duelAnswers[0][t], false);
                    Show(duelRows[0], true);
                }
                else Show(duelRows[i], false);
            }

            // ---- hunts
            huntDuration.Refresh();
            IReadOnlyList<HuntView> hunts = state.Hunts;
            shown = 0;
            for (int i = hunts.Count - 1; i >= 0 && shown < HuntRows; i--)
            {
                HuntView hunt = hunts[i];
                if (state.IsMuted(hunt.Author)) continue;
                bool mine = hunt.Author == comms.LocalId;
                huntIds[shown] = hunt.Id;
                string label = hunt.Revealed
                    ? (hunt.Placings.Count == 0
                        ? "OVER · NOBODY GUESSED " + Who(hunt.Author, hunt.AuthorName) + "'S TARGET"
                        : "OVER · " + hunt.Placings[0].Name + " WINS, " +
                          CommsText.Distance(hunt.Placings[0].Metres, VanillaHudStyle.Metric) + " OFF")
                    : (mine ? "YOUR TARGET" : hunt.AuthorName + " HID A TARGET") + " · " +
                      CommsText.Countdown(hunt.Ends - now) + " · " + AvNum.Fixed(hunt.GuessCount, 0) +
                      (hunt.GuessCount == 1 ? " GUESS" : " GUESSES");
                huntRows[shown].Set(label, null, "", AvState.Info);
                bool canGuess = !hunt.Revealed && !mine && !hunt.HasLocalGuess;
                Show(huntGuess[shown], canGuess);
                huntGuess[shown].Latched = comms.Tool == CommsTool.HuntGuess;
                Show(huntRows[shown], true);
                shown++;
            }
            for (int i = shown; i < HuntRows; i++)
            {
                huntIds[i] = 0;
                Show(huntRows[i], false);
            }
            Show(huntEmpty, shown == 0);

            // ---- the local player's most active head-to-head story
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
            if (rivalryNote != null)
            {
                if (rivalry == null) rivalryNote.Text = "Challenge someone twice to start a friendly rivalry.";
                else
                {
                    bool first = rivalry.First == comms.LocalId;
                    string opponent = first ? rivalry.SecondName : rivalry.FirstName;
                    int mine = first ? rivalry.FirstWins : rivalry.SecondWins;
                    int theirs = first ? rivalry.SecondWins : rivalry.FirstWins;
                    rivalryNote.Text = (mine + theirs + rivalry.Draws >= 3 ? "NEMESIS · " : "RIVAL · ") + opponent + " · YOU " +
                        AvNum.Fixed(mine, 0) + " : " + AvNum.Fixed(theirs, 0) + " · " + AvNum.Fixed(rivalry.Draws, 0) + " DRAWS";
                }
            }
            int wins = 0;
            IReadOnlyList<ScoreRow> allScores = state.Scores.Rows;
            for (int i = 0; i < allScores.Count; i++)
                if (allScores[i].Player == comms.LocalId) { wins = allScores[i].Wins; break; }
            string profile = AvNum.Fixed(state.Scores.PointsOf(comms.LocalId), 0) + " PTS · " + AvNum.Fixed(wins, 0) + " WINS · " +
                AvNum.Fixed(state.HuntWins, 0) + " HUNTS WON" + (state.BestHuntMetres == int.MaxValue ? "" :
                " · CLOSEST " + CommsText.Distance(state.BestHuntMetres, VanillaHudStyle.Metric));
            sessionRow.Set(profile, null, "", AvState.Inert);
            bool recentResult = false;
            for (int i = state.Feed.Count - 1; i >= 0; i--)
            {
                CommsFeedLine line = state.Feed[i];
                if (now - line.Time > 30f) break;
                if (!state.IsMuted(line.Author) && (line.Kind == CommsFeedKind.Duel ||
                    line.Kind == CommsFeedKind.Hunt && line.Text.StartsWith("HUNT OVER"))) { recentResult = true; break; }
            }
            Show(ggButton, recentResult && now >= nextGg);

            // ---- leaderboard
            IReadOnlyList<ScoreRow> rows = state.Scores.Rows;
            for (int i = 0; i < ScoreRows; i++)
            {
                bool has = i < rows.Count;
                Show(scoreRows[i], has);
                if (!has) continue;
                bool me = rows[i].Player == comms.LocalId;
                string name = "#" + AvNum.Fixed(i + 1, 0) + " " + (me ? rows[i].Name + " (YOU)" : rows[i].Name);
                string value = AvNum.Fixed(rows[i].Points, 0) + " PTS · " + AvNum.Fixed(rows[i].Wins, 0) +
                    (rows[i].Wins == 1 ? " WIN" : " WINS");
                scoreRows[i].Set(name, null, value, me ? AvState.Ready : AvState.Info);
            }
            Show(scoreEmpty, rows.Count == 0);
        }
    }
}
