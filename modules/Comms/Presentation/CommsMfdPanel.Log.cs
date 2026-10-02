using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Comms.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int LogRows = 20;
        private const int MinLogRows = 3;
        private const int PlayerRows = 10;

        // Part heights, gaps and padding of the LOG page with no player lines, biased a few px high; each player line
        // (two players side by side) adds 33 px. The log takes what is left.
        private const float LogFixedHeight = 79f;

        private AvSection logSection;
        private AvRow[] logRows;
        private readonly CommsFeedLine[] logBound = new CommsFeedLine[LogRows];

        private AvSection playersSection;
        private AvRow[] playerRows;
        private AvControl[] playerMutes;
        private readonly ulong[] playerIds = new ulong[PlayerRows];

        private void ResetLog()
        {
            logSection = null;
            logRows = null;
            for (int i = 0; i < logBound.Length; i++) logBound[i] = null;
            playersSection = null;
            playerRows = null;
            playerMutes = null;
            for (int i = 0; i < playerIds.Length; i++) playerIds[i] = 0;
        }

        private void BuildLogPage(AvFlow p)
        {
            // The mute list is short and its keys should not move, so it leads: players two to a line, then the log
            // takes every row the page has left.
            playersSection = p.Section(AvIcon.UsersGroup, "PLAYERS", "");
            playerRows = new AvRow[PlayerRows];
            playerMutes = new AvControl[PlayerRows];
            for (int i = 0; i < PlayerRows; i++)
            {
                int row = i;
                playerRows[i] = new AvRow(p.Content);
                playerMutes[i] = playerRows[i].AddTrailing(new AvControl.Spec("MUTE", () =>
                {
                    if (playerIds[row] != 0) comms.State.ToggleMute(playerIds[row]);
                }));
                playerMutes[i].Help = "Mute: hide this player's marks, calls, polls and games on your screen only. They are not told.";
            }
            for (int i = 0; i < PlayerRows; i += 2) p.Row(playerRows[i], playerRows[i + 1]);

            logSection = p.Section(AvIcon.ListDetails, "COMMS LOG", "");
            logRows = new AvRow[LogRows];
            for (int i = 0; i < LogRows; i++)
            {
                int row = i;
                logRows[i] = p.Add(new AvRow(p.Content, () => OpenLogLine(row)));
            }
        }

        private void RefreshLog(float now)
        {
            if (logRows == null) return;
            CommsClientState state = comms.State;

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
                playerMutes[players].Help = muted
                    ? "Unmute: show this player's marks, calls, polls and games again."
                    : "Mute: hide this player's marks, calls, polls and games on your screen only. They are not told.";
                playerMutes[players].Latched = muted;
                Show(playerRows[players], true);
                players++;
            }
            for (int i = players; i < PlayerRows; i++)
            {
                playerIds[i] = 0;
                Show(playerRows[i], false);
            }
            playersSection.SetCaption(players == 0
                ? "NOBODY ELSE HERE YET"
                : AvNum.Fixed(players, 0) + (players == 1 ? " PLAYER" : " PLAYERS") +
                  (state.MutedCount > 0 ? " · " + AvNum.Fixed(state.MutedCount, 0) + " MUTED" : ""));

            // ---- the log, sized to what is left of the page
            IReadOnlyList<CommsFeedLine> feed = state.Feed;
            int fit = FitRows(console.Page(TabLog), LogFixedHeight + ((players + 1) / 2) * 33f, LogRows, MinLogRows);
            int shown = 0;
            for (int i = feed.Count - 1; i >= 0 && shown < fit; i--)
            {
                CommsFeedLine line = feed[i];
                if (state.IsMuted(line.Author)) continue;
                logBound[shown] = line;
                string channel = line.Channel == CommsChannel.All && line.Kind != CommsFeedKind.System ? "[ALL] " : "";
                string text = channel + (line.RematchPlayer != 0 ? "REMATCH › " : "") +
                    Who(line.Author, line.AuthorName) + " · " + line.Text;
                logRows[shown].Set(text, null, Ago(now - line.Time), ToneState(line.Tone));
                logRows[shown].Help = line.RematchPlayer != 0 ? "Rematch: choose a throw on the CREW tab to challenge this pilot again." :
                    line.HasPosition ? "Flash this position on the map." : null;
                Show(logRows[shown], true);
                shown++;
            }
            if (shown == 0)
            {
                logBound[0] = null;
                logRows[0].Set("QUIET SO FAR · CALLS, POLLS, ROLLS AND GAME RESULTS LAND HERE", null, "", AvState.Inert);
                logRows[0].Help = null;
                Show(logRows[0], true);
                shown = 1;
            }
            // Fitted rows stay on the page as ruled blanks, so a short log does not leave a dead band.
            int pad = Math.Max(shown, fit);
            for (int i = shown; i < pad; i++)
            {
                logBound[i] = null;
                logRows[i].Set("", null, "", AvState.Inert);
                logRows[i].Help = null;
                Show(logRows[i], true);
            }
            for (int i = pad; i < LogRows; i++)
            {
                logBound[i] = null;
                Show(logRows[i], false);
            }
            logSection.SetCaption(feed.Count == 0 ? "" : "CLICK: FIND / REMATCH");
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
