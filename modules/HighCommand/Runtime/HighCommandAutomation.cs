#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.HighCommand.Runtime
{
    /// <summary>
    /// Host-truth readback and two levers for the command-post proof (<c>tests/ingame/str-5-posts.json</c>): the feed the Station Board reads, the bounty paid, and a Kill that
    /// takes the real building down with the seat's aircraft as dealer. Confirm and Kill change state, so they refuse unless a nomodkit scenario is running.
    /// Compiled out of public releases. An unknown number is -1.
    /// </summary>
    public static class HighCommandAutomation
    {
        private static bool InSimRun => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("HighCommandAutomation", "Read", "requires a nomodkit scenario run");
            HighCommandManager manager = HighCommandManager.Active;
            return manager == null ? Failure("HighCommandAutomation", "Read", "high command is not running") : manager.ProofRead();
        }

        /// <summary>Records the sighting a patrol would of every standing enemy post for the seat's faction. {confirmed}.</summary>
        public static Dictionary<string, object> Confirm(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("HighCommandAutomation", "Confirm", "requires a nomodkit scenario run");
            HighCommandManager manager = HighCommandManager.Active;
            if (manager == null) return Failure("HighCommandAutomation", "Confirm", "high command is not running");
            int n = manager.ProofConfirm();
            return new Dictionary<string, object> { { "ok", n >= 0 }, { "confirmed", n } };
        }

        /// <summary>{office, away}: kills the standing post of that office (a CommanderView.Id) as the seat's aircraft.</summary>
        public static Dictionary<string, object> Kill(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("HighCommandAutomation", "Kill", "requires a nomodkit scenario run");
            HighCommandManager manager = HighCommandManager.Active;
            return manager == null ? Failure("HighCommandAutomation", "Kill", "high command is not running") : manager.ProofKill((int)Number(args, "office", -1f), Bool(args, "away"));
        }
    }
}
#endif
