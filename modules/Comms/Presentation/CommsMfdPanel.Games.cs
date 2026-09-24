using System.Collections.Generic;
using BoscaliSummer.Features.Comms.Domain;
using BoscaliSummer.Features.Comms.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int DuelRows = 3;
        private const int HuntRows = 2;
        private const int ScoreRows = 6;
        private const float GameRow = 30f;

        private TMP_Text diceNote;
        private TMP_Text lastRoll;

        private RectTransform[] duelRows;
        private TMP_Text[] duelLabels;
        private AvButton[][] duelAnswers;
        private AvButton[] duelWithdraw;
        private uint[] duelIds;

        private TMP_Text huntDuration;
        private RectTransform[] huntRows;
        private TMP_Text[] huntLabels;
        private AvButton[] huntGuess;
        private uint[] huntIds;
        private TMP_Text huntEmpty;

        private TMP_Text[] scoreRanks;
        private TMP_Text[] scoreNames;
        private TMP_Text[] scorePoints;
        private TMP_Text scoreEmpty;

        private void ResetGames()
        {
            diceNote = lastRoll = null;
            duelRows = null;
            duelLabels = null;
            duelAnswers = null;
            duelWithdraw = null;
            duelIds = null;
            huntDuration = null;
            huntRows = null;
            huntLabels = null;
            huntGuess = null;
            huntIds = null;
            huntEmpty = null;
            scoreRanks = scoreNames = scorePoints = null;
            scoreEmpty = null;
        }

        private void BuildGamePage(GameObject page)
        {
            float build = HeadingHeight * 4 + (GameRow + Gap) * (2 + DuelRows + 1 + HuntRows) + 20f +
                          SectionGap * 4 + ScoreRows * 20f + 10f;
            RectTransform parent = PageBody(page, build, out float x, out float y, out float width);

            // ---- dice
            diceNote = Heading(parent, x, ref y, width, "DICE", "");
            for (int i = 0; i < CommsCatalog.DiceSides.Length; i++)
            {
                int die = i;
                IconButton(parent, Cell(x, y, width, GameRow, CommsCatalog.DiceSides.Length, i), "dice",
                    CommsCatalog.DiceNames[i], ToneColour(CommsTone.Fun), () => comms.Roll(die),
                    i == 0 ? "Flip a coin. The host flips it, so nobody can load it." : "Roll a " + CommsCatalog.DiceNames[i] + ". The host rolls it, so nobody can load it.",
                    out _);
            }
            y -= GameRow + Gap;
            lastRoll = AvStyled.Label(parent, new Rect(x, y, width, 18f), "", "row-sub");
            y -= 20f + SectionGap;

            // ---- rock paper scissors
            Heading(parent, x, ref y, width, "ROCK · PAPER · SCISSORS", "PICK A THROW TO CHALLENGE");
            for (int i = 0; i < CommsCatalog.Throws.Length; i++)
            {
                int throwIndex = i;
                IconButton(parent, Cell(x, y, width, GameRow, CommsCatalog.Throws.Length, i), "rps", CommsCatalog.Throws[i],
                    ToneColour(CommsTone.Fun), () => comms.Challenge(throwIndex),
                    "Challenge with " + CommsCatalog.Throws[i] + ". Your throw stays secret on the host until someone answers.", out _);
            }
            y -= GameRow + Gap;

            duelRows = new RectTransform[DuelRows];
            duelLabels = new TMP_Text[DuelRows];
            duelAnswers = new AvButton[DuelRows][];
            duelWithdraw = new AvButton[DuelRows];
            duelIds = new uint[DuelRows];
            float answerWidth = 62f;
            for (int r = 0; r < DuelRows; r++)
            {
                int row = r;
                RectTransform container = Container(parent, new Rect(x, y - r * (GameRow + Gap), width, GameRow), "Duel" + r);
                duelRows[r] = container;
                duelLabels[r] = AvStyled.Label(container, new Rect(0f, 0f, width - answerWidth * 3f - Gap * 3f, GameRow), "", "row-main");
                duelLabels[r].enableWordWrapping = false;
                duelAnswers[r] = new AvButton[CommsCatalog.Throws.Length];
                for (int t = 0; t < CommsCatalog.Throws.Length; t++)
                {
                    int throwIndex = t;
                    float bx = width - (CommsCatalog.Throws.Length - t) * (answerWidth + Gap) + Gap;
                    duelAnswers[r][t] = Button(container, new Rect(bx, 0f, answerWidth, GameRow), CommsCatalog.Throws[t].Substring(0, 1) +
                        CommsCatalog.Throws[t].Substring(1).ToLowerInvariant(), () =>
                        {
                            if (duelIds[row] != 0) comms.AcceptDuel(duelIds[row], throwIndex);
                        }, "Answer with " + CommsCatalog.Throws[t] + ".");
                }
                duelWithdraw[r] = Button(container, new Rect(width - answerWidth * 3f - Gap * 2f, 0f, answerWidth * 3f + Gap * 2f, GameRow),
                    "WITHDRAW", () =>
                    {
                        if (duelIds[row] != 0) comms.CancelDuel(duelIds[row]);
                    }, "Take your challenge back.", AvButtonStyle.Quiet);
            }
            y -= DuelRows * (GameRow + Gap) + SectionGap;

            // ---- map hunt
            Heading(parent, x, ref y, width, "MAP HUNT", "HIDE A POINT · CLOSEST GUESS SCORES");
            AvKit.Stepper(parent, x, y, width - 150f, out huntDuration,
                () => comms.HuntDurationIndex = Wrap(comms.HuntDurationIndex - 1, CommsCatalog.HuntDurations.Length),
                () => comms.HuntDurationIndex = Wrap(comms.HuntDurationIndex + 1, CommsCatalog.HuntDurations.Length),
                "How long everyone has to guess.");
            IconButton(parent, new Rect(x + width - 144f, y, 144f, GameRow), "hunt", "HIDE TARGET",
                ToneColour(CommsTone.Fun), () => comms.SetTool(CommsTool.HuntHide),
                "Click the map to hide a target. Everyone else gets one click to find it; closest three score, a bullseye scores extra.",
                out _);
            y -= GameRow + Gap;

            huntRows = new RectTransform[HuntRows];
            huntLabels = new TMP_Text[HuntRows];
            huntGuess = new AvButton[HuntRows];
            huntIds = new uint[HuntRows];
            huntEmpty = AvStyled.Label(parent, new Rect(x, y, width, GameRow), "No hunt running. Hide a target and see who reads the map best.", "hint");
            for (int r = 0; r < HuntRows; r++)
            {
                int row = r;
                RectTransform container = Container(parent, new Rect(x, y - r * (GameRow + Gap), width, GameRow), "Hunt" + r);
                huntRows[r] = container;
                huntLabels[r] = AvStyled.Label(container, new Rect(0f, 0f, width - 100f, GameRow), "", "row-main");
                huntLabels[r].enableWordWrapping = false;
                huntGuess[r] = IconButton(container, new Rect(width - 94f, 0f, 94f, GameRow), "guess", "GUESS",
                    ToneColour(CommsTone.Fun), () =>
                    {
                        if (huntIds[row] != 0) comms.ArmHuntGuess(huntIds[row]);
                    }, "Arm your one guess, then click the map.", out _);
            }
            y -= HuntRows * (GameRow + Gap) + SectionGap;

            // ---- leaderboard
            Heading(parent, x, ref y, width, "LEADERBOARD", "FUN POINTS · THIS MISSION");
            scoreRanks = new TMP_Text[ScoreRows];
            scoreNames = new TMP_Text[ScoreRows];
            scorePoints = new TMP_Text[ScoreRows];
            scoreEmpty = AvStyled.Label(parent, new Rect(x, y, width, 18f), "Win a duel or a hunt to get on the board.", "hint");
            for (int i = 0; i < ScoreRows; i++)
            {
                scoreRanks[i] = AvStyled.Label(parent, new Rect(x, y, 30f, 18f), "", "kv-key");
                scoreNames[i] = AvStyled.Label(parent, new Rect(x + 32f, y, width - 170f, 18f), "", "row-name");
                scorePoints[i] = AvStyled.Label(parent, new Rect(x + width - 136f, y, 136f, 18f), "", "kv-value",
                    align: TextAlignmentOptions.MidlineRight);
                y -= 20f;
            }
        }

        private void RefreshGames(float now)
        {
            if (duelRows == null) return;
            CommsClientState state = comms.State;

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
            lastRoll.text = roll ?? "Settle it the old way: the host rolls, everyone sees the same number.";
            diceNote.text = comms.Channel == CommsChannel.Team ? "ROLLED FOR YOUR TEAM" : "ROLLED FOR EVERYONE";

            // ---- duels
            IReadOnlyList<DuelView> duels = state.Duels;
            int shown = 0;
            for (int i = duels.Count - 1; i >= 0 && shown < DuelRows; i--)
            {
                DuelView duel = duels[i];
                if (state.IsMuted(duel.Challenger)) continue;
                bool mine = duel.Challenger == comms.LocalId;
                duelIds[shown] = duel.Id;
                duelLabels[shown].text = (mine ? "YOUR CHALLENGE" : duel.ChallengerName + " CHALLENGES") + " · " +
                                         CommsText.Countdown(duel.Expires - now);
                for (int t = 0; t < duelAnswers[shown].Length; t++) Show(duelAnswers[shown][t], !mine);
                Show(duelWithdraw[shown], mine);
                Show(duelRows[shown], true);
                shown++;
            }
            for (int i = shown; i < DuelRows; i++)
            {
                duelIds[i] = 0;
                Show(duelRows[i], i == 0 && shown == 0);
                if (i != 0 || shown != 0) continue;
                duelLabels[0].text = "No open challenges.";
                for (int t = 0; t < duelAnswers[0].Length; t++) Show(duelAnswers[0][t], false);
                Show(duelWithdraw[0], false);
            }

            // ---- hunts
            huntDuration.text = "GUESSING TIME " + CommsText.Countdown(CommsCatalog.HuntDuration(comms.HuntDurationIndex));
            IReadOnlyList<HuntView> hunts = state.Hunts;
            shown = 0;
            for (int i = hunts.Count - 1; i >= 0 && shown < HuntRows; i--)
            {
                HuntView hunt = hunts[i];
                if (state.IsMuted(hunt.Author)) continue;
                bool mine = hunt.Author == comms.LocalId;
                huntIds[shown] = hunt.Id;
                if (hunt.Revealed)
                {
                    huntLabels[shown].text = hunt.Placings.Count == 0
                        ? "OVER · NOBODY GUESSED " + Who(hunt.Author, hunt.AuthorName) + "'S TARGET"
                        : "OVER · " + hunt.Placings[0].Name + " WINS, " + CommsText.Distance(hunt.Placings[0].Metres, BoscaliSummer.Runtime.VanillaHudStyle.Metric) + " OFF";
                }
                else
                {
                    huntLabels[shown].text = (mine ? "YOUR TARGET" : hunt.AuthorName + " HID A TARGET") + " · " +
                                             CommsText.Countdown(hunt.Ends - now) + " · " + hunt.GuessCount +
                                             (hunt.GuessCount == 1 ? " GUESS" : " GUESSES");
                }
                bool canGuess = !hunt.Revealed && !mine && !hunt.HasLocalGuess;
                Show(huntGuess[shown], canGuess);
                huntGuess[shown].SetLatched(comms.Tool == CommsTool.HuntGuess);
                Show(huntRows[shown], true);
                shown++;
            }
            for (int i = shown; i < HuntRows; i++)
            {
                huntIds[i] = 0;
                Show(huntRows[i], false);
            }
            Show(huntEmpty, shown == 0);

            // ---- leaderboard
            IReadOnlyList<ScoreRow> rows = state.Scores.Rows;
            for (int i = 0; i < ScoreRows; i++)
            {
                bool has = i < rows.Count;
                Show(scoreRanks[i], has);
                Show(scoreNames[i], has);
                Show(scorePoints[i], has);
                if (!has) continue;
                bool me = rows[i].Player == comms.LocalId;
                scoreRanks[i].text = "#" + (i + 1);
                scoreNames[i].text = me ? rows[i].Name + " (YOU)" : rows[i].Name;
                scoreNames[i].color = me ? AvTheme.Accent : AvTheme.TextPrimary;
                scorePoints[i].text = rows[i].Points + " PTS · " + rows[i].Wins + (rows[i].Wins == 1 ? " WIN" : " WINS");
            }
            Show(scoreEmpty, rows.Count == 0);
        }
    }
}
