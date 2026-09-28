using System.Collections.Generic;
using BoscaliSummer.Features.Comms.Domain;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int LogRows = 20;
        private const int PlayerRows = 10;

        private AvSection logSection;
        private AvRow[] logRows;
        private readonly CommsFeedLine[] logBound = new CommsFeedLine[LogRows];
        private AvNote logEmpty;

        private AvSection playersSection;
        private AvRow[] playerRows;
        private AvControl[] playerMutes;
        private readonly ulong[] playerIds = new ulong[PlayerRows];
        private AvNote playersEmpty;

        private void ResetLog()
        {
            logSection = null;
            logRows = null;
            for (int i = 0; i < logBound.Length; i++) logBound[i] = null;
            logEmpty = null;
            playersSection = null;
            playerRows = null;
            playerMutes = null;
            for (int i = 0; i < playerIds.Length; i++) playerIds[i] = 0;
            playersEmpty = null;
        }

        private void BuildLogPage(AvFlow p)
        {
            logSection = p.Section(AvIcon.ListDetails, "COMMS LOG", "");
            logEmpty = p.Add(new AvNote(p.Content,
                "Quiet so far. Pings, calls, polls and games from everyone you can hear are logged here."));
            logRows = new AvRow[LogRows];
            for (int i = 0; i < LogRows; i++)
            {
                int row = i;
                logRows[i] = p.Add(new AvRow(p.Content, () => OpenLogLine(row)));
            }

            playersSection = p.Section(AvIcon.UsersGroup, "PLAYERS", "MUTE HIDES THEIR MARKS, CALLS AND NOTICES");
            playersEmpty = p.Add(new AvNote(p.Content, "Players appear here once they post something."));
            playerRows = new AvRow[PlayerRows];
            playerMutes = new AvControl[PlayerRows];
            for (int i = 0; i < PlayerRows; i++)
            {
                int row = i;
                playerRows[i] = p.Add(new AvRow(p.Content));
                playerMutes[i] = playerRows[i].AddTrailing(new AvControl.Spec("MUTE", () =>
                {
                    if (playerIds[row] != 0) comms.State.ToggleMute(playerIds[row]);
                }));
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
                string channel = line.Channel == CommsChannel.All && line.Kind != CommsFeedKind.System ? "[ALL] " : "";
                string text = channel + (line.RematchPlayer != 0 ? "REMATCH › " : "") +
                    Who(line.Author, line.AuthorName) + " · " + line.Text;
                logRows[shown].Set(text, null, Ago(now - line.Time), ToneState(line.Tone));
                Show(logRows[shown], true);
                shown++;
            }
            for (int i = shown; i < LogRows; i++)
            {
                logBound[i] = null;
                Show(logRows[i], false);
            }
            Show(logEmpty, shown == 0);
            logSection?.SetCaption(shown == 0 ? "" : "CLICK A RESULT TO REMATCH · A POSITION TO FIND");

            // ---- players: alphabetical, so a row never jumps out from under the pointer.
            var seen = new List<KeyValuePair<ulong, string>>(state.Seen);
            seen.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            int players = 0;
            for (int i = 0; i < seen.Count && players < PlayerRows; i++)
            {
                if (seen[i].Key == comms.LocalId) continue;
                bool muted = state.IsMuted(seen[i].Key);
                playerIds[players] = seen[i].Key;
                playerRows[players].Set(seen[i].Value + (muted ? " · MUTED" : ""), null, "", muted ? AvState.Inert : AvState.Info);
                playerMutes[players].Label = muted ? "UNMUTE" : "MUTE";
                playerMutes[players].Latched = muted;
                Show(playerRows[players], true);
                players++;
            }
            for (int i = players; i < PlayerRows; i++)
            {
                playerIds[i] = 0;
                Show(playerRows[i], false);
            }
            Show(playersEmpty, players == 0);
            playersSection?.SetCaption(state.MutedCount > 0
                ? AvNum.Fixed(state.MutedCount, 0) + " MUTED · ONLY ON YOUR SCREEN"
                : "MUTE HIDES THEIR MARKS, CALLS AND NOTICES");
        }

        private void OpenLogLine(int row)
        {
            CommsFeedLine line = row >= 0 && row < logBound.Length ? logBound[row] : null;
            if (line == null) return;
            if (line.RematchPlayer != 0)
            {
                string name = comms.State.Seen.TryGetValue(line.RematchPlayer, out string known) ? known : "PILOT";
                SelectRematch(line.RematchPlayer, name, line.Channel);
                console.SetPage(TabGame);
                return;
            }
            if (!line.HasPosition) return;
            comms.Highlight(line.X, line.Z);
            comms.State.SetNotice("FLASHING " + CommsText.Grid(line.X, line.Z) + " ON THE MAP", false, Time.unscaledTime);
        }
    }
}
