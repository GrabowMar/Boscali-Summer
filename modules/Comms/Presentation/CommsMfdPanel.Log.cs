using NOAvionics;
using System.Collections.Generic;
using BoscaliSummer.Modules.Comms.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int PlayerRows = 10;

        private AvSection playersSection;
        private NetRow rosterEmpty;
        private NetRow[] playerRows;
        private AvControl[] playerMutes;
        private readonly ulong[] playerIds = new ulong[PlayerRows];
        private readonly List<KeyValuePair<ulong, string>> seenStations = new List<KeyValuePair<ulong, string>>(PlayerRows);

        private void ResetLog()
        {
            playersSection = null;
            rosterEmpty = null;
            playerRows = null;
            playerMutes = null;
            seenStations.Clear();
            for (int i = 0; i < playerIds.Length; i++) playerIds[i] = 0;
        }

        private void BuildLogPage(AvFlow p)
        {
            // The mute list: stations two to a line.
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

            console.Orders(TabComms,
                "Calls go to your team. Those that mean 'here' also ping your aircraft's position.");
        }

        private void RefreshLog(float now)
        {
            if (playerRows == null) return;
            CommsClientState state = comms.State;

            // ---- stations: alphabetical, so a row never jumps out from under the pointer.
            seenStations.Clear();
            foreach (KeyValuePair<ulong, string> station in state.Seen) seenStations.Add(station);
            seenStations.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            int players = 0;
            for (int i = 0; i < seenStations.Count && players < PlayerRows; i++)
            {
                if (seenStations[i].Key == comms.LocalId) continue;
                bool muted = state.IsMuted(seenStations[i].Key);
                playerIds[players] = seenStations[i].Key;
                playerRows[players].Set("", "", seenStations[i].Value + (muted ? " · MUTED" : ""), "", muted ? AvState.Inert : AvState.Info);
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
        }
    }
}
