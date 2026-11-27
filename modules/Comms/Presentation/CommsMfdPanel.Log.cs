using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Comms.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private const int PlayerRows = 10;

        private AvSection playersSection;
        private AvNote rosterEmpty;
        private AvPagedStack<AvRow> roster;
        private readonly AvControl[] playerMutes = new AvControl[PlayerRows];
        private readonly ulong[] playerIds = new ulong[PlayerRows];
        private readonly List<KeyValuePair<ulong, string>> seenStations = new List<KeyValuePair<ulong, string>>(PlayerRows);
        private int rosterRevision = -1;
        private ulong rosterOwner;

        private void ResetLog()
        {
            playersSection = null;
            rosterEmpty = null;
            roster = null;
            seenStations.Clear();
            rosterRevision = -1;
            Array.Clear(playerIds, 0, playerIds.Length);
            Array.Clear(playerMutes, 0, playerMutes.Length);
        }

        private void BuildNetPage(AvFlow page)
        {
            playersSection = page.Section(AvIcon.UsersGroup, "STATIONS HEARD");
            rosterEmpty = page.Add(new AvNote(page.Content, "No other stations heard yet."));
            roster = page.Add(new AvPagedStack<AvRow>(page.Content, page.Ticker, PlayerRows,
                AvGridTokens.Gap, MakeStation, BindStation, "Stations"));
            roster.SetShown(false);
        }

        private AvRow MakeStation(RectTransform parent, int slot)
        {
            var row = new AvRow(parent);
            playerMutes[slot] = row.AddTrailing(new AvControl.Spec("MUTE", () =>
            {
                if (playerIds[slot] == 0) return;
                comms.State.ToggleMute(playerIds[slot]);
                RefreshLog();
            }, AvButtonStyle.Toggle));
            return row;
        }

        private void BindStation(int index, AvRow row)
        {
            KeyValuePair<ulong, string> station = seenStations[index];
            bool muted = comms.State.IsMuted(station.Key);
            int slot = index % PlayerRows;
            playerIds[slot] = station.Key;
            row.Set(station.Value, "", muted ? "MUTED" : "", muted ? AvState.Inert : AvState.Info);
            AvControl control = playerMutes[slot];
            control.Label = muted ? "UNMUTE" : "MUTE";
            control.Latched = muted;
            control.Help = muted
                ? "Show this station’s marks and calls again."
                : "Hide this station’s marks and calls on your screen.";
        }

        private void RefreshLog()
        {
            if (roster == null) return;
            CommsClientState state = comms.State;
            int others = state.Seen.Count - (state.Seen.ContainsKey(comms.LocalId) ? 1 : 0);
            // A callsign can arrive on a packet that does not advance the log revision.
            if (rosterOwner == comms.LocalId && rosterRevision == state.Revision && seenStations.Count == others)
            {
                bool unchanged = true;
                foreach (var station in seenStations)
                    if (!state.Seen.TryGetValue(station.Key, out string name) || name != station.Value)
                    { unchanged = false; break; }
                if (unchanged) return;
            }

            rosterOwner = comms.LocalId;
            rosterRevision = state.Revision;
            seenStations.Clear();
            int muted = 0;
            foreach (var station in state.Seen)
            {
                if (station.Key == comms.LocalId) continue;
                seenStations.Add(station);
                if (state.IsMuted(station.Key)) muted++;
            }
            seenStations.Sort((a, b) =>
            {
                int byName = StringComparer.OrdinalIgnoreCase.Compare(a.Value, b.Value);
                return byName != 0 ? byName : a.Key.CompareTo(b.Key);
            });

            Array.Clear(playerIds, 0, playerIds.Length);
            roster.SetCount(others);
            roster.SetShown(others > 0);
            rosterEmpty.SetShown(others == 0);
            playersSection.SetCaption(others == 0 ? "" : AvNum.Fixed(others, 0) +
                (others == 1 ? " PLAYER" : " PLAYERS") + (muted > 0 ? " · " + AvNum.Fixed(muted, 0) + " MUTED" : ""));
        }
    }
}
