using System.Globalization;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The single vocabulary for every pilot-visible value of the war snapshot. Static, pure, System-only, upper-case plain
    /// ASCII (the detail of an empty state may be lower case), and a way out in every empty state. Sentences about engine
    /// reasons, ground reasons, wave blocks, outcomes and command results live here and nowhere else.
    /// </summary>
    internal static class TheaterWords
    {
        private const int LogLine = 64;

        internal static string Kind(TheaterOpKind kind) =>
            kind == TheaterOpKind.Defend ? "DEFEND" : kind == TheaterOpKind.Recon ? "RECON" : "ASSAULT";

        internal static string Aim(TheaterOpKind kind, bool objective, bool baseFront) =>
            kind == TheaterOpKind.Defend
                ? baseFront ? "HOLD THE AIRBASE; CLEAR THE HOSTILES AT ITS FENCE" : "RETAIN HELD GROUND; CLEAR OBSERVED PRESSURE"
            : kind == TheaterOpKind.Recon ? "OBSERVE THIS UNCERTAIN FRONT; RESISTANCE UNKNOWN"
            : objective || baseFront ? "SECURE THE MISSION TARGET AT THIS FIX" : "GAIN SIGNED CONTROL OF THIS SECTOR";

        internal static string Phase(TheaterPhase phase, TheaterPhaseNote note)
        {
            switch (phase)
            {
                case TheaterPhase.Awaiting:
                    return note == TheaterPhaseNote.NoSteering ? "AWAITING FORCES - GROUND STEERING OFF"
                        : note == TheaterPhaseNote.NoForces ? "AWAITING FORCES - NO GROUP ON THIS EFFORT YET" : "AWAITING FORCES";
                case TheaterPhase.Forming: return "FORMING UP";
                case TheaterPhase.Advancing: return "ADVANCING";
                case TheaterPhase.InContact: return "IN CONTACT";
                case TheaterPhase.Holding: return "HOLDING";
                case TheaterPhase.Scouting: return "SCOUTING";
                default: return "WITHDRAWING";
            }
        }

        internal static string Outcome(TheaterOutcome outcome)
        {
            switch (outcome)
            {
                case TheaterOutcome.Secured: return "SECURED";
                case TheaterOutcome.Repulsed: return "REPULSED";
                case TheaterOutcome.Stalled: return "STALLED";
                case TheaterOutcome.CalledOff: return "CALLED OFF";
                case TheaterOutcome.Redirected: return "REDIRECTED";
                default: return "";
            }
        }

        internal static string Why(TheaterWhy why)
        {
            switch (why)
            {
                case TheaterWhy.ObjectiveCaptured: return "OBJECTIVE CAPTURED";
                case TheaterWhy.FrontAdvanced: return "THE LINE MOVED UP";
                case TheaterWhy.HeldClear: return "HELD AND CLEAR";
                case TheaterWhy.Observed: return "FRONT OBSERVED";
                case TheaterWhy.ForcesLost: return "FORCES LOST";
                case TheaterWhy.FrontPushedBack: return "PUSHED BACK";
                case TheaterWhy.ObjectiveLost: return "OBJECTIVE LOST";
                case TheaterWhy.NoForces: return "NO FORCES REACHED THE FRONT";
                case TheaterWhy.TimeLimit: return "TIME LIMIT";
                case TheaterWhy.BaseLost: return "AIRBASE LOST";
                case TheaterWhy.FrontVanished: return "FRONT NO LONGER SENSED";
                default: return "";
            }
        }

        /// <summary>Staff-log line for an ended operation, at most 64 characters. The amount is printed only when it was paid.</summary>
        internal static string Result(TheaterOpKind kind, string name, TheaterOutcome outcome, TheaterWhy why, int funds, string picker)
        {
            string n = name ?? "";
            string paid = funds > 0 ? " +" + funds.ToString(CultureInfo.InvariantCulture) : "";
            string line;
            switch (outcome)
            {
                case TheaterOutcome.Secured:
                    line = n + (kind == TheaterOpKind.Defend ? " HELD - ENEMY BROKEN OFF" : kind == TheaterOpKind.Recon
                        ? " SCOUTED - PICTURE UPDATED" : " TAKEN - THE LINE MOVES UP") + paid;
                    break;
                case TheaterOutcome.Repulsed: line = n + " LOST GROUND - STAFF REASSESSING"; break;
                case TheaterOutcome.Stalled: line = n + " STALLED - STAFF REASSESSING"; break;
                case TheaterOutcome.CalledOff:
                    line = n + " CALLED OFF" + (string.IsNullOrEmpty(picker) ? "" : " - " + picker.ToUpperInvariant());
                    break;
                case TheaterOutcome.Redirected: line = "MAIN EFFORT SHIFTED TO " + n; break;
                default: return "";
            }
            return line.Length > LogLine ? line.Substring(0, LogLine) : line;
        }

        /// <summary>Faction radio text for an ended operation; only a result worth saying on the air.</summary>
        internal static string Announce(TheaterOpKind kind, string name, TheaterOutcome outcome, int funds)
        {
            switch (outcome)
            {
                case TheaterOutcome.Secured:
                    return "OPERATION " + name + " SECURED" +
                        (funds > 0 ? " - +" + funds.ToString(CultureInfo.InvariantCulture) + " FUNDS, MORALE UP" : "");
                case TheaterOutcome.Repulsed: return "OPERATION " + name + " LOST GROUND - STAFF REASSESSING";
                case TheaterOutcome.Stalled: return "OPERATION " + name + " STALLED - STAFF REASSESSING";
                default: return "";
            }
        }

        /// <summary>"TITLE - detail" for every reason but None: what is wrong, then what to do (N-19).</summary>
        internal static string Empty(TheaterEmptyReason reason, float seconds)
        {
            string n = Seconds(seconds);
            switch (reason)
            {
                case TheaterEmptyReason.StaffOff: return "STAFF OFF - enable LIVING FRONT in F1 and restart";
                case TheaterEmptyReason.HostLacksModule:
                    return "HOST HAS NO STAFF - ask the host to enable LIVING FRONT, or the host runs a different build";
                case TheaterEmptyReason.NoFaction: return "NO FACTION - take a seat on a side to see its war";
                case TheaterEmptyReason.WaitingForHost:
                    return "WAITING FOR HOST - the first staff report has not arrived; controls unlock when it does";
                case TheaterEmptyReason.FirstReview:
                    return "FIRST REVIEW IN " + n + " S - the staff is surveying the front before it proposes; wait for its deck";
                case TheaterEmptyReason.NoFronts:
                    return "NO FRONT TO WORK - no contested ground yet; move ground forces up or wait for contact";
                case TheaterEmptyReason.NoGroundWar:
                    return "NO GROUND WAR HERE - this mission fields no mobile ground force; fly sorties from the contract desk";
                case TheaterEmptyReason.NoFriendlyGroundNearFront:
                    return "NO GROUND FORCE NEAR ANY FRONT - bring ground units within 20 km of the line";
                case TheaterEmptyReason.FrontsQuiet:
                    return "ALL QUIET - held ground, no hostile contact; nothing to defend yet, wait for contact";
                case TheaterEmptyReason.PostureTooCautious:
                    return "POSTURE TOO CAUTIOUS - raise it to see assaults at these odds";
                case TheaterEmptyReason.CalledOff: return "FRONT CALLED OFF - back in the deck in " + n + " S; pick another front meanwhile";
                case TheaterEmptyReason.StaffFault: return "STAFF FAULT - three reviews failed in a row; see the BepInEx log";
                case TheaterEmptyReason.OperationLive: return "OPERATION UNDER WAY - pick another front to redirect it";
                case TheaterEmptyReason.TacticsOff: return "GROUND STEERING OFF - SET SERVER > THEATER OPS > FRONTLINE TACTICS";
                case TheaterEmptyReason.RtsOwnsOrders:
                    return "GROUND CONTROL RTS COMMANDS THE GROUND - THE STAFF ONLY PLANS; GIVE GROUND ORDERS FROM THE RTS";
                case TheaterEmptyReason.NoTerritoryField:
                    return "NO FRONT LINE DRAWN - the Command map is off (it needs PROGRESSION in F1); restart after enabling";
                default: return "";
            }
        }

        internal static string ViewState(TheaterViewState state, float ageSeconds)
        {
            switch (state)
            {
                case TheaterViewState.StaffOff: return Empty(TheaterEmptyReason.StaffOff, 0f);
                case TheaterViewState.HostLacksModule: return Empty(TheaterEmptyReason.HostLacksModule, 0f);
                case TheaterViewState.NoFaction: return Empty(TheaterEmptyReason.NoFaction, 0f);
                case TheaterViewState.WaitingForHost: return Empty(TheaterEmptyReason.WaitingForHost, 0f);
                case TheaterViewState.Stale:
                    return "STAFF REPORT STALE - " + Seconds(ageSeconds) + " S OLD; commands wait for a fresh report";
                default: return "";
            }
        }

        internal static string Command(TheaterCommandCode code, string detail, float lockSeconds)
        {
            switch (code)
            {
                case TheaterCommandCode.Accepted: return "STAFF UPDATED";
                case TheaterCommandCode.StaleChoice: return "CHOICE CHANGED - REVIEW THE CURRENT OPTIONS";
                case TheaterCommandCode.Locked:
                    return "LOCKED " + (string.IsNullOrEmpty(detail) ? "" : "BY " + detail.ToUpperInvariant() + " ") + "FOR " + Seconds(lockSeconds) + " S";
                case TheaterCommandCode.RateLimited: return "ONE COMMAND PER HALF SECOND";
                case TheaterCommandCode.Invalid: return "COMMAND NOT UNDERSTOOD";
                case TheaterCommandCode.NoOperation: return "NO OPERATION TO CALL OFF";
                case TheaterCommandCode.SessionChanged: return "MISSION OR FACTION CHANGED - REFRESHING";
                case TheaterCommandCode.Unconfirmed: return "HOST DID NOT ANSWER - REFRESHING STAFF";
                case TheaterCommandCode.NotAllowed: return "STAFF NOT RUNNING";
                default: return "";
            }
        }

        internal static string Ground(GroundReason reason)
        {
            switch (reason)
            {
                case GroundReason.Ready: return "GROUND STEERING READY";
                case GroundReason.TacticsOff: return Empty(TheaterEmptyReason.TacticsOff, 0f);
                case GroundReason.RtsOwnsOrders: return Empty(TheaterEmptyReason.RtsOwnsOrders, 0f);
                case GroundReason.Unsupported: return "GAME UPDATE BROKE GROUND STEERING";
                case GroundReason.NoVehicles: return "NO MOBILE GROUND FORCE IN THIS MISSION - AIR AND SUPPLY ONLY";
                case GroundReason.NoFront: return "NO FRONT LINE DRAWN - NEEDS THE COMMAND MAP";
                default: return "-";
            }
        }

        internal static string Wave(WaveBlock block)
        {
            switch (block)
            {
                case WaveBlock.SwitchedOff: return "WAVES SWITCHED OFF BY THE HOST";
                case WaveBlock.NoSorties: return "SORTIES ARE OFF ON THIS HOST - NO WAVES TO EARN";
                case WaveBlock.MissionForbids: return "THIS MISSION FORBIDS PURCHASED WAVES";
                case WaveBlock.NoOperation: return "NO OPERATION - PICK A FRONT TO EARN A WAVE";
                case WaveBlock.NoDepot: return "NO DEPOT LEFT TO DELIVER A WAVE";
                case WaveBlock.NoGroup: return "NO COMBAT CONVOY GROUP DEFINED";
                case WaveBlock.Cooling: return "CONVOY GROUP COOLING DOWN";
                case WaveBlock.PoolShort: return "POOL TOO SHORT FOR THE WAVE - CREDITS KEPT";
                default: return "";
            }
        }

        internal static string Threat(byte level, TheaterThreatWhy why)
        {
            if (level == 0) return "-";
            string title = ThreatTitle(why);
            return "DEFCON " + level.ToString(CultureInfo.InvariantCulture) + (title.Length == 0 ? "" : " - " + title);
        }

        internal static string ThreatTitle(TheaterThreatWhy why)
        {
            switch (why)
            {
                case TheaterThreatWhy.Nominal: return "NOMINAL";
                case TheaterThreatWhy.AirContact: return "TRACKED HOSTILE AIR";
                case TheaterThreatWhy.AirOutnumbered: return "TRACKED HOSTILE AIR OUTNUMBERS OURS";
                case TheaterThreatWhy.GroundContact: return "GROUND CONTACT ALONG THE FRONT";
                case TheaterThreatWhy.BaseContested: return "ONE AIRBASE CONTESTED";
                case TheaterThreatWhy.BasesContested: return "SEVERAL AIRBASES CONTESTED";
                default: return "";
            }
        }

        internal static string Carrier(TheaterCarrier carrier) =>
            carrier == TheaterCarrier.Ground ? "GROUND" : carrier == TheaterCarrier.AirNaval ? "AIR/NAVAL" : "-";

        internal static string Risk(TheaterRisk risk) =>
            risk == TheaterRisk.Moderate ? "MODERATE" : risk == TheaterRisk.High ? "HIGH" : "-";

        internal static string FrontStatus(TheaterFrontStatus status)
        {
            switch (status)
            {
                case TheaterFrontStatus.Holding: return "HOLDING";
                case TheaterFrontStatus.UnderPressure: return "UNDER PRESSURE";
                case TheaterFrontStatus.InContact: return "IN CONTACT";
                default: return "UNCONFIRMED";
            }
        }

        /// <summary>The caption of the posture row states the rule the staff really applies (PostureRule).</summary>
        internal static string Posture(TheaterWarPosture posture) =>
            "ASSAULTS NEED " + PostureRule.Ratio(posture).ToString("0.0#", CultureInfo.InvariantCulture) +
            " FRIENDLY PER HOSTILE. A LIVE OPERATION IS NEVER AFFECTED.";

        // Whole seconds, counting up: a countdown never reads 0 S while something is still to come.
        private static string Seconds(float seconds) =>
            (seconds > 0f && seconds < 100000f ? (int)System.Math.Ceiling(seconds) : 0).ToString(CultureInfo.InvariantCulture);
    }
}
