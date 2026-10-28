using System;
using System.Globalization;

namespace BoscaliSummer.Modules.HighCommand.Domain
{
    /// <summary>Every sentence the staff board says on its own, with the length each must keep (the wire clips log and status text at 96).</summary>
    internal static class StaffWords
    {
        /// <summary>Metres within which a unit confirms a post building; the single source for the manager's IntelPostRadius.</summary>
        public const float PostSightMetres = 2600f;

        public const string ModuleOff = "Chain of command is off on this host: set HighCommand.Enabled in its config.";
        public const string WaitingMission = "Waiting for a running mission.";
        public const string NoFaction = "Join a faction to see its chain of command.";
        public const string NoStaff = "No command staff formed for your faction: it needs a land airbase, not a carrier.";
        // The radius named here is PostSightMetres / 1000 (pinned by StaffWordsTests).
        public const string NoEnemyPost = "No enemy command post confirmed: fly within 2.6 km of an enemy airbase to find one.";
        public const string WaitingBoard = "Waiting for the staff board.";
        public const string NoAnswer = "No answer from the host: it may run another build or have HighCommand turned off.";

        private const int MaximumRank = 8, MaximumName = 24, MaximumCause = 43;
        private const int MaximumFunds = 100000;   // the wire bound of a bounty

        /// <summary>"Staff board is 42 s old: waiting for the host." An age that cannot be read says so instead of printing a number.</summary>
        public static string Stale(float seconds) =>
            !float.IsFinite(seconds) ? "Staff board is out of date: waiting for the host." :
            "Staff board is " + ((int)Math.Clamp(seconds, 0f, 99999f)).ToString(CultureInfo.InvariantCulture) + " s old: waiting for the host.";

        /// <summary>"BOUNTY +3000 · MAJ GEN Mira Keller KILLED AT Sandrift Airbase" (cause given by the caller).</summary>
        public static string KillLine(int funds, string rank, string name, string cause) =>
            ("BOUNTY +" + Math.Clamp(funds, 0, MaximumFunds).ToString(CultureInfo.InvariantCulture) + " · " + Who(rank, name) + " " + Clip(cause, MaximumCause)).TrimEnd();

        /// <summary>"NO BOUNTY, OFFICE ALREADY CLAIMED · MAJ GEN Mira Keller"</summary>
        public static string ClaimedLine(string rank, string name) => ("NO BOUNTY, OFFICE ALREADY CLAIMED · " + Who(rank, name)).TrimEnd();

        /// <summary>"MAJ GEN Mira Keller WAS AFIELD · POST DESTROYED, NO BOUNTY"</summary>
        public static string AfieldLine(string rank, string name) => (Who(rank, name) + " WAS AFIELD · POST DESTROYED, NO BOUNTY").TrimStart();

        private static string Who(string rank, string name) => (Clip(rank, MaximumRank) + " " + Clip(name, MaximumName)).Trim();

        private static string Clip(string text, int maximum) =>
            string.IsNullOrEmpty(text) ? "" : text.Length <= maximum ? text : text.Substring(0, maximum).TrimEnd();
    }
}
