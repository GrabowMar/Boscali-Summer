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
        private const int LogRows = 20;
        private const int PlayerRows = 10;
        private const float LogRow = 26f;
        private const float PlayerRow = 28f;

        private RectTransform[] logRows;
        private Image[] logRails;
        private TMP_Text[] logTimes;
        private TMP_Text[] logTexts;
        private AvButton[] logHits;
        private readonly CommsFeedLine[] logBound = new CommsFeedLine[LogRows];
        private TMP_Text logEmpty;
        private TMP_Text logNote;

        private RectTransform[] playerRows;
        private TMP_Text[] playerNames;
        private AvButton[] playerMutes;
        private readonly ulong[] playerIds = new ulong[PlayerRows];
        private TMP_Text playersEmpty;
        private TMP_Text playersNote;

        private void ResetLog()
        {
            logRows = null;
            logRails = null;
            logTimes = null;
            logTexts = null;
            logHits = null;
            for (int i = 0; i < logBound.Length; i++) logBound[i] = null;
            logEmpty = null;
            logNote = null;
            playerRows = null;
            playerNames = null;
            playerMutes = null;
            for (int i = 0; i < playerIds.Length; i++) playerIds[i] = 0;
            playersEmpty = null;
            playersNote = null;
        }

        private void BuildLogPage(GameObject page)
        {
            float build = HeadingHeight * 2 + LogRows * (LogRow + 2f) + SectionGap + PlayerRows * (PlayerRow + Gap) + 10f;
            RectTransform parent = PageBody(page, build, out float x, out float y, out float width);

            logNote = Heading(parent, x, ref y, width, "COMMS LOG", "");
            logEmpty = AvStyled.Label(parent, new Rect(x, y, width, 34f),
                "Quiet so far. Pings, calls, polls and games from everyone you can hear are logged here.", "hint");
            logRows = new RectTransform[LogRows];
            logRails = new Image[LogRows];
            logTimes = new TMP_Text[LogRows];
            logTexts = new TMP_Text[LogRows];
            logHits = new AvButton[LogRows];
            for (int i = 0; i < LogRows; i++)
            {
                int row = i;
                RectTransform container = Container(parent, new Rect(x, y - i * (LogRow + 2f), width, LogRow), "Log" + i);
                logRows[i] = container;
                Image fill = AvKit.Panel(container, new Rect(0f, 0f, width, LogRow), AvTheme.Ground);
                logHits[i] = AvKit.HitButton(container, new Rect(0f, 0f, width, LogRow), () => FindOnMap(row));
                logHits[i].SetRowHighlight(fill, AvTheme.Ground, AvTheme.SurfaceRaised);
                logRails[i] = AvKit.Panel(container, new Rect(0f, -4f, 3f, LogRow - 8f), AvTheme.RailInert);
                logTimes[i] = AvStyled.Label(container, new Rect(8f, 0f, 34f, LogRow), "", "row-sub");
                logTexts[i] = AvStyled.Label(container, new Rect(44f, 0f, width - 48f, LogRow), "", "row-main");
                logTexts[i].enableWordWrapping = false;
                logTexts[i].overflowMode = TextOverflowModes.Ellipsis;
                logTexts[i].raycastTarget = false;
            }
            y -= LogRows * (LogRow + 2f) + SectionGap;

            playersNote = Heading(parent, x, ref y, width, "PLAYERS", "MUTE HIDES THEIR MARKS, CALLS AND NOTICES");
            playersEmpty = AvStyled.Label(parent, new Rect(x, y, width, 20f),
                "Players appear here once they post something.", "hint");
            playerRows = new RectTransform[PlayerRows];
            playerNames = new TMP_Text[PlayerRows];
            playerMutes = new AvButton[PlayerRows];
            for (int i = 0; i < PlayerRows; i++)
            {
                int row = i;
                RectTransform container = Container(parent, new Rect(x, y - i * (PlayerRow + Gap), width, PlayerRow), "Player" + i);
                playerRows[i] = container;
                playerNames[i] = AvStyled.Label(container, new Rect(0f, 0f, width - 110f, PlayerRow), "", "row-name");
                playerMutes[i] = Button(container, new Rect(width - 104f, 0f, 104f, PlayerRow), "MUTE", () =>
                {
                    if (playerIds[row] != 0) comms.State.ToggleMute(playerIds[row]);
                }, "Hide this player's marks, calls, polls and games on your screen only. They are not told.");
            }
        }

        private void RefreshLog(float now)
        {
            if (logRows == null) return;
            CommsClientState state = comms.State;
            IReadOnlyList<CommsFeedLine> feed = state.Feed;

            int shown = 0;
            for (int i = feed.Count - 1; i >= 0 && shown < LogRows; i--)
            {
                CommsFeedLine line = feed[i];
                if (state.IsMuted(line.Author)) continue;
                logBound[shown] = line;
                logRails[shown].color = ToneColour(line.Tone);
                logTimes[shown].text = Ago(now - line.Time);
                string channel = line.Channel == CommsChannel.All && line.Kind != CommsFeedKind.System ? "[ALL] " : "";
                logTexts[shown].text = channel + Who(line.Author, line.AuthorName) + " · " + line.Text;
                logHits[shown].WithTooltip(line.HasPosition ? "Flash this position on the map." : null);
                Show(logRows[shown], true);
                shown++;
            }
            for (int i = shown; i < LogRows; i++)
            {
                logBound[i] = null;
                Show(logRows[i], false);
            }
            Show(logEmpty, shown == 0);
            logNote.text = shown == 0 ? "" : "CLICK A LINE WITH A GRID TO FIND IT";

            // ---- players: alphabetical, so a row never jumps out from under the pointer.
            var seen = new List<KeyValuePair<ulong, string>>(state.Seen);
            seen.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            int players = 0;
            for (int i = 0; i < seen.Count && players < PlayerRows; i++)
            {
                if (seen[i].Key == comms.LocalId) continue;
                bool muted = state.IsMuted(seen[i].Key);
                playerIds[players] = seen[i].Key;
                playerNames[players].text = seen[i].Value + (muted ? " · MUTED" : "");
                playerNames[players].color = muted ? AvTheme.Disabled : AvTheme.TextPrimary;
                playerMutes[players].SetText(muted ? "UNMUTE" : "MUTE");
                playerMutes[players].SetLatched(muted);
                Show(playerRows[players], true);
                players++;
            }
            for (int i = players; i < PlayerRows; i++)
            {
                playerIds[i] = 0;
                Show(playerRows[i], false);
            }
            Show(playersEmpty, players == 0);
            playersNote.text = state.MutedCount > 0
                ? state.MutedCount + " MUTED · ONLY ON YOUR SCREEN"
                : "MUTE HIDES THEIR MARKS, CALLS AND NOTICES";
        }

        private void FindOnMap(int row)
        {
            CommsFeedLine line = row >= 0 && row < logBound.Length ? logBound[row] : null;
            if (line == null || !line.HasPosition) return;
            comms.Highlight(line.X, line.Z);
            comms.State.SetNotice("FLASHING " + CommsText.Grid(line.X, line.Z) + " ON THE MAP", false, Time.unscaledTime);
        }
    }
}
