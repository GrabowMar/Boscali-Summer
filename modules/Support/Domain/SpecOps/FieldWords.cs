using System;
using System.Globalization;

namespace BoscaliSummer.Features.Support.Domain.SpecOps
{
    /// <summary>
    /// Every word SPEC OPS shows, so the MFD, the desk, the map layer and the host's replies say
    /// the same thing. State is always written; colour only repeats it.
    /// </summary>
    internal static class FieldWords
    {
        private static readonly string[] Callsigns = { "ALPHA", "BRAVO", "CHARLIE", "DELTA" };

        public const string Title = "SOF DETACHMENT";

        public static string Callsign(int team) =>
            team >= 0 && team < Callsigns.Length ? Callsigns[team] : "TEAM";

        public static string Rank(int rank)
        {
            switch (rank)
            {
                case 0: return "RECRUIT";
                case 1: return "TRAINED";
                case 2: return "VETERAN";
                default: return "ELITE";
            }
        }

        public static string State(TeamState state)
        {
            switch (state)
            {
                case TeamState.Unformed: return "UNFORMED";
                case TeamState.Ready: return "READY";
                case TeamState.EnRoute: return "EN ROUTE";
                case TeamState.OnTask: return "ON TASK";
                case TeamState.Holding: return "HOLDING";
                default: return "RECOVERING";
            }
        }

        public static string Mission(FieldMission mission)
        {
            switch (mission)
            {
                case FieldMission.Recon: return "RECON";
                case FieldMission.Sabotage: return "SABOTAGE";
                case FieldMission.Steal: return "STEAL";
                default: return "SEIZE";
            }
        }

        public static string MissionTitle(FieldMission mission)
        {
            switch (mission)
            {
                case FieldMission.Recon: return "SPECIAL RECONNAISSANCE";
                case FieldMission.Sabotage: return "AIR DEFENCE SABOTAGE";
                case FieldMission.Steal: return "INTELLIGENCE THEFT";
                default: return "SEIZE BUILDINGS";
            }
        }

        /// <summary>What a success does to the world, at the rank the team would go with.</summary>
        public static string Effect(FieldMission mission, int rank)
        {
            switch (mission)
            {
                case FieldMission.Recon:
                    return "Reveals hostile ground units within " + Km(FieldCatalog.ReconRadius(rank)) +
                           " and scouts the objective for 10 min (+10% on later missions).";
                case FieldMission.Sabotage:
                    return "Jams hostile ground radars within " + Km(FieldCatalog.SabotageRadius(rank)) + " for " +
                           Seconds(FieldCatalog.SabotageSeconds(rank)) + ": a window to strike the SAMs.";
                case FieldMission.Steal:
                    return "Steals " + FieldCatalog.StealIntel(rank).ToString("0", CultureInfo.InvariantCulture) +
                           " intel for CYBER operations.";
                default:
                    int buildings = FieldCatalog.SeizeBuildings(rank);
                    return "Occupies " + buildings + (buildings == 1 ? " building" : " buildings") +
                           " near the objective with rooftop MG/AT/AA positions for your side.";
            }
        }

        public static string BriefEffect(FieldMission mission, int rank)
        {
            switch (mission)
            {
                case FieldMission.Recon: return "Reveals ground in " + Km(FieldCatalog.ReconRadius(rank)) + "; scouts +10% odds.";
                case FieldMission.Sabotage: return "Jams radars in " + Km(FieldCatalog.SabotageRadius(rank)) + " for " +
                    Seconds(FieldCatalog.SabotageSeconds(rank)) + ".";
                case FieldMission.Steal: return "Steals " + FieldCatalog.StealIntel(rank).ToString("0", CultureInfo.InvariantCulture) + " CYBER intel.";
                default:
                    int buildings = FieldCatalog.SeizeBuildings(rank);
                    return "Occupies " + buildings + (buildings == 1 ? " defensive building." : " defensive buildings.");
            }
        }

        public static string Post(FieldMission post)
        {
            switch (post)
            {
                case FieldMission.Recon: return "OBSERVATION POST";
                case FieldMission.Sabotage: return "SABOTEUR CELL";
                case FieldMission.Steal: return "LISTENING POST";
                default: return "SAFEHOUSE";
            }
        }

        public static string PostCode(FieldMission post)
        {
            switch (post)
            {
                case FieldMission.Recon: return "OP";
                case FieldMission.Sabotage: return "CELL";
                case FieldMission.Steal: return "LISTEN";
                default: return "SAFE";
            }
        }

        /// <summary>What holding the post adds to ACTIONS.</summary>
        public static string PostGrant(FieldMission post)
        {
            switch (post)
            {
                case FieldMission.Recon: return "SPOT / SKYWATCH within " + Km(FieldCatalog.PostReach(post));
                case FieldMission.Sabotage: return "SUPPRESS / HUNT within " + Km(FieldCatalog.PostReach(post));
                case FieldMission.Steal: return "EAVESDROP within " + Km(FieldCatalog.PostReach(post));
                default: return "FORTIFY within " + Km(FieldCatalog.PostReach(post));
            }
        }

        public static string Kind(ObjectiveKind kind)
        {
            switch (kind)
            {
                case ObjectiveKind.Airfield: return "AIRFIELD";
                case ObjectiveKind.Outpost: return "OUTPOST";
                case ObjectiveKind.Town: return "TOWN";
                case ObjectiveKind.AirDefence: return "AIR DEFENCE";
                default: return "OBJECTIVE";
            }
        }

        public static string KindCode(ObjectiveKind kind)
        {
            switch (kind)
            {
                case ObjectiveKind.Airfield: return "AF";
                case ObjectiveKind.Outpost: return "OP";
                case ObjectiveKind.Town: return "TN";
                case ObjectiveKind.AirDefence: return "AD";
                default: return "--";
            }
        }

        public static string Threat(int threat)
        {
            if (threat <= 0) return "CLEAR";
            if (threat <= 3) return "LIGHT";
            if (threat <= 8) return "MODERATE";
            return "HEAVY";
        }

        public static string Ability(FieldAbility ability) => ability == FieldAbility.Spot ? "SPOT" :
            ability == FieldAbility.Skywatch ? "SKYWATCH" : ability == FieldAbility.Eavesdrop ? "EAVESDROP" :
            ability == FieldAbility.Hunt ? "HUNT" : "SUPPRESS";

        public static string AbilityCode(FieldAbility ability) => ability == FieldAbility.Spot ? "SPT" :
            ability == FieldAbility.Skywatch ? "SKY" : ability == FieldAbility.Eavesdrop ? "EAV" :
            ability == FieldAbility.Hunt ? "HNT" : "SUP";

        public static string AbilityDescription(FieldAbility ability) => ability == FieldAbility.Spot
            ? "An observation post calls out hostile ground units around the mark (2.5 km, +0.5 km per rank)."
            : ability == FieldAbility.Skywatch ? "An observation post reveals hostile aircraft around the mark."
            : ability == FieldAbility.Eavesdrop ? "A listening post locates active hostile emitters around the mark."
            : ability == FieldAbility.Hunt ? "A saboteur cell reveals emitters and briefly jams hostile ground radars."
            : "A saboteur cell jams hostile ground radars around the mark (2 km, 30 s; more with rank).";

        /// <summary>The locked-row reason: which desk mission earns the post.</summary>
        public static string AbilityLocked(FieldAbility ability) => ability == FieldAbility.Spot || ability == FieldAbility.Skywatch
            ? "NO OBSERVATION POST · SEND RECON FROM THE DESK"
            : ability == FieldAbility.Eavesdrop ? "NO LISTENING POST · SEND STEAL FROM THE DESK"
            : "NO SABOTEUR CELL · SEND SABOTAGE FROM THE DESK";

        public static string Denial(SpecOpsDenial denial)
        {
            switch (denial)
            {
                case SpecOpsDenial.None: return "READY";
                case SpecOpsDenial.Disabled: return "SPEC OPS IS OFF ON THIS SERVER";
                case SpecOpsDenial.BadTeam: return "NO SUCH TEAM";
                case SpecOpsDenial.Unformed: return "TEAM NOT FORMED · RAISE IT FIRST";
                case SpecOpsDenial.AlreadyFormed: return "TEAM ALREADY FORMED";
                case SpecOpsDenial.Busy: return "TEAM IS NOT READY";
                case SpecOpsDenial.NotDeployed: return "TEAM IS NOT IN THE FIELD";
                case SpecOpsDenial.StaleObjective: return "OBJECTIVE NO LONGER LISTED";
                case SpecOpsDenial.WrongObjective: return "NOT POSSIBLE AT THIS OBJECTIVE";
                case SpecOpsDenial.NoRadars: return "NO HOSTILE RADAR SEEN HERE";
                case SpecOpsDenial.SeizeUnavailable: return "URBAN COMBAT GARRISONS ARE OFF";
                case SpecOpsDenial.ObjectiveTaken: return "ANOTHER TEAM IS ON THIS OBJECTIVE";
                case SpecOpsDenial.BadMission: return "UNKNOWN MISSION";
                default: return "REFUSED";
            }
        }

        /// <summary>One event-log line; null for nothing worth saying.</summary>
        public static string Notice(FieldNotice notice, int team, FieldMission mission, string target)
        {
            string who = Callsign(team);
            string where = string.IsNullOrEmpty(target) ? "" : " · " + target;
            switch (notice)
            {
                case FieldNotice.Raised: return who + " FORMED · RECRUIT, READY";
                case FieldNotice.Launched: return who + " MOVING OUT · " + Mission(mission) + where;
                case FieldNotice.OnTask: return who + " ON TASK · " + Mission(mission) + where;
                case FieldNotice.Success: return who + " SUCCESS · " + Post(mission) + " HELD" + where;
                case FieldNotice.Failed: return who + " FAILED · RETURNING" + where;
                case FieldNotice.Lost: return who + " LOST" + where + " · SLOT OPEN";
                case FieldNotice.Recalled: return who + " RECALLED · RETURNING";
                case FieldNotice.PostEnded: return who + " LEFT THE " + Post(mission) + " · RETURNING";
                case FieldNotice.Ready: return who + " READY";
                case FieldNotice.NoBuildings: return who + (mission == FieldMission.Seize
                    ? " FOUND NO BUILDING TO HOLD · RETURNING" : " EFFECT UNAVAILABLE · RETURNING");
                default: return null;
            }
        }

        /// <summary>True for events that deserve the alarm colour in the log.</summary>
        public static bool Alarm(FieldNotice notice) => notice == FieldNotice.Lost || notice == FieldNotice.Failed;

        /// <summary>A team's state in one line for the roster: what, where and how long.</summary>
        public static string TeamLine(in FieldTeam team, double remaining)
        {
            switch (team.State)
            {
                case TeamState.Unformed:
                    return team.Last == MissionOutcome.Lost ? "LOST · RAISE A NEW TEAM" : "EMPTY SLOT · RAISE IN THE DESK";
                case TeamState.Ready:
                    return "READY · " + Rank(team.Rank);
                case TeamState.EnRoute:
                    return "EN ROUTE · " + Mission(team.Mission) + " · " + Target(team) + " · " + Clock(remaining);
                case TeamState.OnTask:
                    return "ON TASK · " + Mission(team.Mission) + " · " + team.Chance + "% · " + Clock(remaining);
                case TeamState.Holding:
                    return "HOLDING " + PostCode(team.Mission) + " · " + Target(team) + " · " + Clock(remaining);
                default:
                    return "RECOVERING · " + LastWord(team.Last, team.Mission) + Clock(remaining);
            }
        }

        /// <summary>The desk card's headline: state and clock only; the card's detail line carries the rest.</summary>
        public static string TeamHeadline(in FieldTeam team, double remaining)
        {
            switch (team.State)
            {
                case TeamState.Unformed: return team.Last == MissionOutcome.Lost ? "LOST" : "EMPTY SLOT";
                case TeamState.Ready: return "READY";
                case TeamState.Holding: return "HOLDING " + PostCode(team.Mission) + " · " + Clock(remaining);
                default: return State(team.State) + " · " + Clock(remaining);
            }
        }

        private static string LastWord(MissionOutcome last, FieldMission mission)
        {
            switch (last)
            {
                case MissionOutcome.Failed: return "FAILED · ";
                case MissionOutcome.Recalled: return "RECALLED · ";
                case MissionOutcome.NoBuildings: return mission == FieldMission.Seize ? "NO BUILDING · " : "NO EFFECT · ";
                default: return "";
            }
        }

        private static string Target(in FieldTeam team) => string.IsNullOrEmpty(team.Target) ? "OBJECTIVE" : team.Target;

        public static string Km(float metres) =>
            (metres / 1000f).ToString(metres % 1000f == 0f ? "0" : "0.0", CultureInfo.InvariantCulture) + " km";

        public static string Seconds(float seconds) =>
            seconds >= 60f && seconds % 60f == 0f
                ? (seconds / 60f).ToString("0", CultureInfo.InvariantCulture) + " min"
                : Math.Round(seconds).ToString("0", CultureInfo.InvariantCulture) + " s";

        public static string Clock(double seconds) => TheaterGrid.Clock(Math.Ceiling(Math.Max(0.0, seconds)));

        /// <summary>
        /// The next useful step, for the STATUS banner: what to do in the desk, or what ACTIONS can
        /// do right now. Reads the model only.
        /// </summary>
        public static string Advice(SpecOpsDetachment detachment, double now)
        {
            if (detachment == null) return "AWAITING THEATER DATA";
            if (!detachment.Enabled) return "SPEC OPS IS OFF ON THIS SERVER";
            if (detachment.Formed == 0) return "NO TEAMS · RAISE ONE IN THE DESK";
            if (detachment.Posts(FieldMission.Recon) > 0) return "OBSERVATION POST HELD · SPOT / SKYWATCH LIVE IN ACTIONS";
            if (detachment.Posts(FieldMission.Sabotage) > 0) return "SABOTEUR CELL HELD · SUPPRESS / HUNT LIVE IN ACTIONS";
            if (detachment.Posts(FieldMission.Steal) > 0) return "LISTENING POST HELD · EAVESDROP LIVE IN ACTIONS";
            if (detachment.Posts(FieldMission.Seize) > 0) return "SAFEHOUSE HELD · FORTIFY WORKS AROUND IT";
            int ready = -1;
            for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
                if (detachment.Team(i).State == TeamState.Ready) { ready = i; break; }
            if (ready >= 0)
                return detachment.ObjectiveCount > 0
                    ? Callsign(ready) + " READY · OPEN THE DESK AND PICK AN OBJECTIVE"
                    : Callsign(ready) + " READY · NO OBJECTIVE LISTED YET";
            double soonest = double.MaxValue;
            for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
                if (detachment.Team(i).Formed) soonest = Math.Min(soonest, detachment.Remaining(i, now));
            return soonest < double.MaxValue && soonest > 0.0
                ? "ALL TEAMS COMMITTED · NEXT FREE IN " + Clock(soonest)
                : "ALL TEAMS COMMITTED";
        }

        /// <summary>"2 READY · 1 EN ROUTE · 1 HOLDING": every non-zero state, in cycle order.</summary>
        public static string Summary(SpecOpsDetachment detachment)
        {
            if (detachment == null) return "AWAITING THEATER DATA";
            string text = "";
            for (int s = (int)TeamState.Ready; s <= (int)TeamState.Recovering; s++)
            {
                int count = detachment.Count((TeamState)s);
                if (count == 0) continue;
                text += (text.Length > 0 ? " · " : "") + count + " " + State((TeamState)s);
            }
            return text.Length > 0 ? text : "NO TEAMS FORMED";
        }
    }
}
