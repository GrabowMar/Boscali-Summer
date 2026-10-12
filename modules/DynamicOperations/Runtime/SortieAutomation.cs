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

        /// <summary>SEND WING for the first tied strike card through the production path; reports the refusal text when there is no wing. {found, id, sent, refusal, answer, ...}.</summary>
        public static Dictionary<string, object> SendWing(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("SortieAutomation", "SendWing", "requires a nomodkit scenario run");
            OperationsManager manager = OperationsManager.Active;
            return manager == null ? Failure("SortieAutomation", "SendWing", "dynamic operations are not running") : manager.ProofSendWing();
        }

        /// <summary>The card the last SendWing/OfferPost named: card state, target up, the wing's status for it, the last paid sortie's wing flag.</summary>
        public static Dictionary<string, object> WingRead(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("SortieAutomation", "WingRead", "requires a nomodkit scenario run");
            OperationsManager manager = OperationsManager.Active;
            return manager == null ? Failure("SortieAutomation", "WingRead", "dynamic operations are not running") : manager.ProofWingRead();
        }

        /// <summary>Takes the named card's target down as the seat's aircraft; killedBefore 1 when something else (the wing) already had.</summary>
        public static Dictionary<string, object> KillTarget(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("SortieAutomation", "KillTarget", "requires a nomodkit scenario run");
            OperationsManager manager = OperationsManager.Active;
            return manager == null ? Failure("SortieAutomation", "KillTarget", "dynamic operations are not running") : manager.ProofKillTarget();
        }

        /// <summary>Offers and accepts the STRIKE COMMAND POST card through the production generator.</summary>
        public static Dictionary<string, object> OfferPost(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("SortieAutomation", "OfferPost", "requires a nomodkit scenario run");
            OperationsManager manager = OperationsManager.Active;
            return manager == null ? Failure("SortieAutomation", "OfferPost", "dynamic operations are not running") : manager.ProofOfferPost();
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
