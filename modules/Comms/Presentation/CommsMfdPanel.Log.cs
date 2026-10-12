using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Comms.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int LogRows = 24;
        private const int MinLogRows = 3;
        private const int PlayerRows = 10;

        private AvSection logSection;
        private NetRow[] logRows;
        private readonly CommsFeedLine[] logBound = new CommsFeedLine[LogRows];

        private AvSection playersSection;
        private NetRow rosterEmpty;
        private NetRow[] playerRows;
        private AvControl[] playerMutes;
        private readonly ulong[] playerIds = new ulong[PlayerRows];

        private void ResetLog()
        {
            logSection = null;
            logRows = null;
            for (int i = 0; i < logBound.Length; i++) logBound[i] = null;
            playersSection = null;
            rosterEmpty = null;
            playerRows = null;
            playerMutes = null;
            for (int i = 0; i < playerIds.Length; i++) playerIds[i] = 0;
        }

        private void BuildLogPage(AvFlow p)
        {
            // The mute list is short and its keys should not move, so it leads: stations two to a line, then the log
            // takes every row the page has left.
            playersSection = p.Section(AvIcon.UsersGroup, "STATIONS HEARD", "");
            rosterEmpty = p.Add(new NetRow(p.Content, AvTextRole.Head, 0f, 0f, 0f));
            rosterEmpty.Set("", "", "NO STATIONS HEARD YET · THEY LIST HERE ONCE THEY POST", "", AvState.Inert);
            playerRows = new NetRow[PlayerRows];
            playerMutes = new AvControl[PlayerRows];
            for (int i = 0; i < PlayerRows; i++)
            {
                int row = i;
                playerRows[i] = new NetRow(p.Content, AvTextRole.Head, 0f, 0f, 0f);
                playerMutes[i] = playerRows[i].AddKey(new AvControl.Spec("MUTE", () =>
                {
                    if (playerIds[row] != 0) comms.State.ToggleMute(playerIds[row]);
                }));
                playerMutes[i].Help = "Mute: hide this player's marks and calls on your screen only. They are not told.";
            }
            for (int i = 0; i < PlayerRows; i += 2) p.Row(playerRows[i], playerRows[i + 1]);

            logSection = p.Section(AvIcon.ListDetails, "NET LOG", "");
            logRows = new NetRow[LogRows];
            for (int i = 0; i < LogRows; i++)
            {
                int row = i;
                logRows[i] = p.Add(new NetRow(p.Content, AvTextRole.DataSmall, 36f, 124f, 36f, () => OpenLogLine(row)));
            }

            console.Orders(TabComms,
                "Calls go to your team. Those that mean 'here' also ping your aircraft's position.",
                "Click a log line that carries a position to flash it on the map.",
                "Mute hides a station's marks and calls on your screen only. They are not told.",
                "A post to ALL reaches both sides; the host can switch ALL off.");
        }

        private void RefreshLog(float now)
        {
            if (logRows == null) return;
            CommsClientState state = comms.State;

            // ---- stations: alphabetical, so a row never jumps out from under the pointer.
            var seen = new List<KeyValuePair<ulong, string>>(state.Seen);
            seen.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            int players = 0;
            for (int i = 0; i < seen.Count && players < PlayerRows; i++)
            {
                if (seen[i].Key == comms.LocalId) continue;
                bool muted = state.IsMuted(seen[i].Key);
                playerIds[players] = seen[i].Key;
                playerRows[players].Set("", "", seen[i].Value + (muted ? " · MUTED" : ""), "", muted ? AvState.Inert : AvState.Info);
                playerMutes[players].Label = muted ? "UNMUTE" : "MUTE";
                playerMutes[players].Help = muted
                    ? "Unmute: show this player's marks and calls again."
                    : "Mute: hide this player's marks and calls on your screen only. They are not told.";
                playerMutes[players].Latched = muted;
                Show(playerRows[players], true);
                players++;
            }
            for (int i = players; i < PlayerRows; i++)
            {
                playerIds[i] = 0;
                Show(playerRows[i], false);
            }
            Show(rosterEmpty, players == 0);
            playersSection.SetCaption(players == 0
                ? ""
                : AvNum.Fixed(players, 0) + (players == 1 ? " PLAYER" : " PLAYERS") +
                  (state.MutedCount > 0 ? " · " + AvNum.Fixed(state.MutedCount, 0) + " MUTED" : ""));

            // ---- the log, sized to what is left of the page; a line that wraps takes the room it needs
            IReadOnlyList<CommsFeedLine> feed = state.Feed;
            AvFlow page = console.Page(TabComms);
            float width = page.Inner;
            float room = page.ViewportHeight <= 0f ? float.MaxValue : page.ViewportHeight - FixedHeight(page, logRows);
            float used = 0f;
            int shown = 0, eligible = 0;
            bool full = false;
            for (int i = feed.Count - 1; i >= 0; i--)
            {
                CommsFeedLine line = feed[i];
                if ((line.Kind != CommsFeedKind.Call && line.Kind != CommsFeedKind.System) || state.IsMuted(line.Author)) continue;
                eligible++;
                if (full || shown >= LogRows) continue;
                NetRow row = logRows[shown];
                row.Set(Ago(now - line.Time), Who(line.Author, line.AuthorName), line.Text,
                    line.Channel == CommsChannel.All && line.Kind != CommsFeedKind.System ? "ALL" : "", ToneState(line.Tone));
                float h = row.Measure(width) + AvGridTokens.Gap;
                if (shown >= MinLogRows && used + h > room) { full = true; continue; }
                used += h;
                logBound[shown] = line;
                row.Help = line.HasPosition ? "Flash this position on the map." : null;
                Show(row, true);
                shown++;
            }
            if (shown == 0)
            {
                logBound[0] = null;
                logRows[0].Set("", "", "NET QUIET · NO TRAFFIC HELD. SEND A BREVITY CALL ABOVE.", "", AvState.Inert);
                logRows[0].Help = null;
                Show(logRows[0], true);
                shown = 1;
            }
            for (int i = shown; i < LogRows; i++)
            {
                logBound[i] = null;
                Show(logRows[i], false);
            }
            logSection.SetCaption(eligible == 0 ? ""
                : (shown < eligible ? "NEWEST " + AvNum.Fixed(shown, 0) + " OF " + AvNum.Fixed(eligible, 0) : "NEWEST FIRST") + " · CLICK: FIND ON MAP");
        }

        private void OpenLogLine(int row)
        {
            CommsFeedLine line = row >= 0 && row < logBound.Length ? logBound[row] : null;
            if (line == null) return;
            if (!line.HasPosition) return;
            comms.Highlight(line.X, line.Z);
            comms.State.SetNotice("FLASHING " + CommsText.Grid(line.X, line.Z) + " ON THE MAP", false, Time.unscaledTime);
        }
    }
}
