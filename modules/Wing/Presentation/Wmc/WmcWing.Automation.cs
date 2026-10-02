using System;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // WING for scenarios (Automation.Wmc): inspect a pilot as a row press would, and report the page as numbers and words.
    internal sealed partial class WmcWing
    {
        /// <summary>Inspect the first pilot (roster order) that is <paramref name="who"/>: a callsign, or free / next / flying /
        /// inbound / lost (down, MIA, a local search, captured or KIA). False when nobody matches.</summary>
        public bool Inspect(string who)
        {
            if (client || string.IsNullOrEmpty(who)) return false;
            if (last != null) Snapshot(last);
            for (int i = 0; i < roster.Count; i++)
            {
                if (!Matches(who, roster[i], status[i])) continue;
                Inspect(roster[i]);
                return true;
            }
            return false;
        }

        private bool Matches(string who, WingPilot p, PilotStatus s)
        {
            switch (who.ToLowerInvariant())
            {
                case "free": return s == PilotStatus.Free;
                case "next": return ReferenceEquals(p, upcoming);
                case "flying": return s == PilotStatus.Flying;
                case "inbound": return s == PilotStatus.Inbound;
                case "lost": return PilotStatuses.Lost(s);
                default: return string.Equals(p.Callsign, who, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>What the page shows (flags as 0/1).</summary>
        public void Report(Dictionary<string, object> into)
        {
            int at = IndexOf(client ? null : inspected);
            PilotStatus s = at >= 0 ? status[at] : PilotStatus.Free;
            int visible = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Rect.gameObject.activeSelf) visible++;
            int owned = 0;
            if (at >= 0)
                for (int i = 0; i < PerkCards.Slots; i++)
                    if (PerkCards.For(i, inspected.Perks, inspected.Rank).Owned) owned++;
            float left = s == PilotStatus.LocalSar ? WingSearchAndRescue.LocalRecoveryRemaining(inspected) : -1f;
            into["wing_pilots"] = client ? 0 : roster.Count;
            into["wing_rows"] = visible;
            into["wing_per_page"] = PerPage;
            into["wing_pages"] = Pages.Count(roster.Count, PerPage);
            into["wing_page"] = listPage;
            into["wing_focus"] = at >= 0 ? inspected.Callsign : "";
            into["wing_focus_shown"] = at >= 0 && Pages.Of(at, PerPage) == listPage ? 1 : 0;
            into["wing_focus_flying"] = at >= 0 && s == PilotStatus.Flying ? 1 : 0;
            into["wing_focus_state"] = at >= 0 ? SquadronWords.Row(s, ReferenceEquals(inspected, upcoming), number[at]) : "";
            into["wing_stamp"] = dossier.StampWord ?? "";
            into["wing_rank_line"] = dossier.RankLine;
            into["wing_xp_fill"] = at >= 0 ? Mathf.RoundToInt(PilotXp.Fill(inspected.Xp) * 100f) : 0;
            into["wing_perk_cards"] = owned;
            into["wing_airframe"] = assign.NameText;
            into["wing_slot"] = assign.SlotText;
            into["wing_ready"] = free;
            into["wing_lost"] = lost;
            into["wing_next"] = upcoming != null ? upcoming.Callsign : "";
            into["wing_next_is_focus"] = upcoming != null && ReferenceEquals(upcoming, inspected) ? 1 : 0;
            into["wing_recruit"] = client ? 0 : 1;
            into["wing_studio"] = 1;
            into["wing_release"] = releaseOn ? 1 : 0;
            into["wing_airsar"] = at >= 0 && airWhy == null ? 1 : 0;
            into["wing_localsar"] = at >= 0 && localWhy == null ? 1 : 0;
            into["wing_local_armed"] = at >= 0 && localGate.IsArmed(inspected.Callsign, Time.unscaledTime) ? 1 : 0;
            into["wing_local_left"] = left >= 0f ? Mathf.CeilToInt(left) : 0;
        }
    }
}
