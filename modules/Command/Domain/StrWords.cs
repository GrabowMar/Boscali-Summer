using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Layout captions of the board: short banner words, tables of fixed vocabularies, the checklist labels and the glyphs.
    /// Sentences about engine reasons, ground reasons, wave blocks, outcomes and command results stay in TheaterWords.
    /// </summary>
    internal static class StrWords
    {
        /// <summary>The unknown mark of every surface: an em dash, never a zero.</summary>
        public const string Dash = "—";
        /// <summary>Separator of the status strips and chips.</summary>
        public const string Dot = " · ";
        // Glyphs: the kit font prints the triangles, the level bar, the cross and the arrow, but not the check mark (the first panel render drew a box
        // for it, annex 05 P9 risk), so a yes is the plain word OK, never a box.
        public const string Up = "▲ ", Down = "▼ ", Level = "▬ ", Yes = "OK", No = "✕", Arrow = "→", Degree = "°";

        /// <summary>The slab word above a banner; empty for Operation (its banner only shows when the report is stale).</summary>
        public static string Chip(StrBoardMode mode)
        {
            switch (mode)
            {
                case StrBoardMode.Proposals: return "STAFF PROPOSING";
                case StrBoardMode.Assessing: return "STAFF ASSESSING";
                case StrBoardMode.NoOpening: return "NO OPENING";
                case StrBoardMode.Reduced: return "NO PLATFORMS";
                case StrBoardMode.StaffOff: return "STAFF OFF";
                case StrBoardMode.HostLacksStaff: return "STAFF OFF";
                case StrBoardMode.NoFaction: return "NO FACTION";
                case StrBoardMode.Waiting: return "ASKING THE HOST";
                case StrBoardMode.NoAnswer: return "NO ANSWER";
                default: return "";
            }
        }

        public static string Head(StrBoardMode mode)
        {
            switch (mode)
            {
                case StrBoardMode.Proposals: return "PICK THE MAIN EFFORT";
                case StrBoardMode.Assessing: return "THE STAFF IS SURVEYING THE FRONT";
                case StrBoardMode.NoOpening: return "THE STAFF HAS NOTHING TO OFFER YET";
                case StrBoardMode.Reduced: return ReducedHead(TheaterEmptyReason.NoGroundWar);
                case StrBoardMode.StaffOff: return "STAFF SWITCHED OFF IN THIS GAME";
                case StrBoardMode.HostLacksStaff: return "THE HOST TURNED THE STAFF OFF";
                case StrBoardMode.NoFaction: return "YOU ARE NOT ON A SIDE";
                case StrBoardMode.Waiting: return "WAITING FOR THE FIRST REPORT";
                case StrBoardMode.NoAnswer: return "THE HOST DID NOT ANSWER";
                default: return "";
            }
        }

        /// <summary>The fixed part of the status strip; the builder appends the faction, platform count, countdown and age it knows.</summary>
        public static string Status(StrBoardMode mode)
        {
            switch (mode)
            {
                case StrBoardMode.Operation: return "BOARD CURRENT";
                case StrBoardMode.Proposals: return "NO OPERATION RUNNING";
                case StrBoardMode.Assessing: return "FIRST REVIEW";
                case StrBoardMode.NoOpening: return "NO OPENING" + Dot + "STAFF REVIEWING EVERY 10 S";
                case StrBoardMode.Reduced: return "REDUCED MODE";
                case StrBoardMode.StaffOff: return "STAFF OFF" + Dot + "BOARD UNAVAILABLE";
                case StrBoardMode.HostLacksStaff: return "STAFF OFF ON HOST" + Dot + "BOARD UNAVAILABLE";
                case StrBoardMode.NoFaction: return "NO FACTION" + Dot + "NO STAFF TO REPORT";
                case StrBoardMode.Waiting: return "WAITING FOR HOST" + Dot + "NO REPORT YET";
                default: return "NO ANSWER FROM HOST" + Dot + "BOARD UNAVAILABLE";
            }
        }

        /// <summary>The second footer line when nothing more urgent is to be said; unique per mode.</summary>
        public static string Flavour(StrBoardMode mode)
        {
            switch (mode)
            {
                case StrBoardMode.Operation: return "The staff runs the plan. Your sorties swing the fight.";
                case StrBoardMode.Proposals: return "A pick sets the main effort for every pilot of your side and starts the sorties.";
                case StrBoardMode.Assessing: return "The staff reads every front before it proposes anything.";
                case StrBoardMode.NoOpening: return "Sorties from the MIS contract desk are still offered while the board waits.";
                case StrBoardMode.Reduced: return "Nothing is broken. The staff has no fronts to plan here.";
                case StrBoardMode.StaffOff: return "SITREP reads live counts only when you host the game. LOG needs the staff.";
                case StrBoardMode.HostLacksStaff: return "Nothing is wrong on your side. SITREP and LOG show dashes until the host turns the staff on.";
                case StrBoardMode.NoFaction: return "Pick a side and the board fills in on the next report.";
                case StrBoardMode.Waiting: return "Controls stay locked until a report is under 15 s old.";
                default: return "The host may not run LIVING FRONT, or runs another build.";
            }
        }

        /// <summary>Headline of a Reduced board from its reason: NO GROUND WAR HERE, GROUND STEERING OFF, RTS HAS THE GROUND.</summary>
        public static string ReducedHead(TheaterEmptyReason reason)
        {
            switch (reason)
            {
                case TheaterEmptyReason.TacticsOff: return "GROUND STEERING OFF";
                case TheaterEmptyReason.RtsOwnsOrders: return "RTS HAS THE GROUND";
                default: return "NO GROUND WAR HERE";
            }
        }

        /// <summary>The line every stale war report carries: its age and what waits. An unknown age is not guessed.</summary>
        public static string StaleReport(float ageSeconds) =>
            (float.IsNaN(ageSeconds) || float.IsInfinity(ageSeconds) || ageSeconds < 0f ? "REPORT STALE" : "REPORT " + ((int)System.Math.Floor(ageSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " S OLD")
            + Dot + "ORDERS WAIT FOR THE HOST";

        /// <summary>The NoAnswer board's own why and do lines (the engine has no such reason).</summary>
        public static string NoAnswerWhy() => "No report after 30 s. The host may not run LIVING FRONT, or it runs a different build.";

        public static string NoAnswerDo() => "Ask the host to enable LIVING FRONT in F1 and match builds. SITREP and LOG show dashes until it answers.";

        /// <summary>Caption of route step 1..n for an operation kind; empty outside the route.</summary>
        public static string Route(TheaterOpKind kind, int step)
        {
            if (step == 1) return "FORMING";
            if (step == 2) return kind == TheaterOpKind.Assault ? "ADVANCING" : kind == TheaterOpKind.Defend ? "HOLDING" : "SCOUTING";
            if (step == 3 && kind == TheaterOpKind.Assault) return "IN CONTACT";
            return "";
        }

        /// <summary>Steps of the route strip: three for an assault, two otherwise.</summary>
        public static int RouteSteps(TheaterOpKind kind) => kind == TheaterOpKind.Assault ? 3 : 2;

        /// <summary>Sortie family chip: CONTROL, STRIKE, COVER, EW, INSERT, RECOVER, RECON, SUPPLY.</summary>
        public static string Family(ObjectiveFamily family)
        {
            switch (family)
            {
                case ObjectiveFamily.Control: return "CONTROL";
                case ObjectiveFamily.Strike: return "STRIKE";
                case ObjectiveFamily.AirCover: return "COVER";
                case ObjectiveFamily.Electronic: return "EW";
                case ObjectiveFamily.Insertion: return "INSERT";
                case ObjectiveFamily.Recovery: return "RECOVER";
                case ObjectiveFamily.Reconnaissance: return "RECON";
                case ObjectiveFamily.Logistics: return "SUPPLY";
                default: return "SORTIE";
            }
        }

        /// <summary>SEND WING, COVER, SCOUT by intent; RECALL while tasked.</summary>
        public static string WingLabel(WingSortieIntent intent, bool tasked)
        {
            if (tasked) return "RECALL";
            return intent == WingSortieIntent.Cover ? "COVER" : intent == WingSortieIntent.Observe ? "SCOUT" : "SEND WING";
        }

        /// <summary>DEFCON word: 5 NORMAL, 4 ELEVATED, 3 HIGH, 2 SEVERE, 1 MAXIMUM, anything else NOT MEASURED.</summary>
        public static string Alert(int level)
        {
            switch (level)
            {
                case 5: return "NORMAL";
                case 4: return "ELEVATED";
                case 3: return "HIGH";
                case 2: return "SEVERE";
                case 1: return "MAXIMUM";
                default: return "NOT MEASURED";
            }
        }

        /// <summary>Short caption of a wave block for the strip; the sentence is TheaterWords.Wave.</summary>
        public static string WaveBlockCaption(WaveBlock block)
        {
            switch (block)
            {
                case WaveBlock.SwitchedOff: return "SWITCHED OFF";
                case WaveBlock.NoSorties: return "NO SORTIES";
                case WaveBlock.MissionForbids: return "MISSION FORBIDS";
                case WaveBlock.NoOperation: return "NO OPERATION";
                case WaveBlock.NoDepot: return "NO DEPOT";
                case WaveBlock.NoGroup: return "NO GROUP";
                case WaveBlock.Cooling: return "COOLING DOWN";
                case WaveBlock.PoolShort: return "POOL SHORT";
                default: return "";
            }
        }

        /// <summary>Label of the no-opening checklist row an engine reason belongs to. Six rows; a call-off and a cautious posture share the last.</summary>
        public static string CheckLabel(TheaterEmptyReason reason)
        {
            switch (reason)
            {
                case TheaterEmptyReason.StaffFault: return "STAFF RUNNING";
                case TheaterEmptyReason.NoTerritoryField: return "FRONT LINE DRAWN";
                case TheaterEmptyReason.NoFronts: return "A FRONT TO WORK";
                case TheaterEmptyReason.NoFriendlyGroundNearFront: return "GROUND FORCE WITHIN REACH OF A FRONT";
                case TheaterEmptyReason.FrontsQuiet: return "HOSTILE CONTACT TO ANSWER";
                case TheaterEmptyReason.PostureTooCautious:
                case TheaterEmptyReason.CalledOff: return "POSTURE AND CALL-OFFS ALLOW AN OFFER";
                default: return "";
            }
        }
    }
}
