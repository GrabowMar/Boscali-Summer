using System;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
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
    }
}
