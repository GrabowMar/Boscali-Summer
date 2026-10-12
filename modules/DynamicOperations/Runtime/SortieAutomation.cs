#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.DynamicOperations.Runtime
{
    /// <summary>
    /// Host-truth readback and one lever for the sortie proof (<c>tests/ingame/str-4-sorties.json</c>): the seat's board with each card's tie, and a CompleteTied that drives the
    /// real accept-observe-pay-Finished chain. CompleteTied changes state, so it refuses unless a nomodkit scenario is running. Compiled out of public releases. Nested
    /// dictionaries, no dots in key names; an unknown number is -1.
    /// </summary>
    public static class SortieAutomation
    {
        private static bool InSimRun => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("SortieAutomation", "Read", "requires a nomodkit scenario run");
            OperationsManager manager = OperationsManager.Active;
            return manager == null ? Failure("SortieAutomation", "Read", "dynamic operations are not running") : manager.ProofRead();
        }

        /// <summary>{limit (default 3)}: completes one tied sortie of the seat's faction unless <c>limit</c> have been completed this way already.</summary>
        public static Dictionary<string, object> CompleteTied(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("SortieAutomation", "CompleteTied", "requires a nomodkit scenario run");
            OperationsManager manager = OperationsManager.Active;
            return manager == null ? Failure("SortieAutomation", "CompleteTied", "dynamic operations are not running") : manager.ProofComplete((int)Number(args, "limit", 3f));
        }
    }
}
#endif
