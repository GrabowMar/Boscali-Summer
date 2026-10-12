#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Presentation;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>
    /// Host-truth readback of the wing's sortie tasking and one lever for the SEND WING proof (<c>tests/ingame/str-5-wing.json</c>). The presses themselves go through the console
    /// (StrAutomation.Ticket), the production path. Funnel gives the wing a radial order (the C-key path). Compiled out of public releases; changing verbs refuse outside a sim run.
    /// An unknown number is -1; state codes are WingSortieState (None 0, Tasked 1, OnStation 2, Done 3, Failed 4, Recalled 5).
    /// </summary>
    public static class SortieProof
    {
        private static bool InSimRun => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("SortieProof", "Read", "requires a nomodkit scenario run");
            WingService w = WingService.Instance;
            WingSortieOrders orders = WingSortieOrders.Current;
            if (w == null || orders == null) return Failure("SortieProof", "Read", "the wing is not active");
            int sendable = 0, ground = 0, engaged = 0;
            foreach (WingMember m in w.Members)
            {
                if (m.Sendable) sendable++;
                if (m.OnGround) ground++;
                if (m.Engaged) engaged++;
            }
            var tasking = new Dictionary<string, object> { { "id", orders.LastSortieId }, { "state", 0 }, { "wingmen", -1 }, { "targetDown", 0 }, { "words", "" }, { "wordsLen", 0 } };
            if (orders.TryGetStatus(orders.LastSortieId, out WingSortieStatus s))
            {
                tasking["state"] = (int)s.State; tasking["wingmen"] = s.Wingmen; tasking["targetDown"] = s.TargetDown ? 1 : 0;
                tasking["words"] = s.Words; tasking["wordsLen"] = s.Words.Length;
            }
            tasking["autoFormUps"] = orders.AutoFormUps;
            tasking["orbitMaxMetres"] = orders.OrbitMax;
            return new Dictionary<string, object>
            {
                { "ok", true },
                { "wing", new Dictionary<string, object> { { "members", w.Members.Count }, { "sendable", sendable }, { "onGround", ground }, { "assigned", w.AssignedCount }, { "engaged", engaged } } },
                { "tasking", tasking },
                { "rules", new Dictionary<string, object> {
                    { "coverRadiusOk", AreaGuard.Clamp(WingSortieRules.CoverRadius) == WingSortieRules.CoverRadius ? 1 : 0 },
                    { "reachOk", WingSortieRules.AttackReach(CombatSupervisor.LeashMetres) <= CombatSupervisor.LeashMetres ? 1 : 0 } } },
            };
        }

        /// <summary>{metres}: the wing's combat leash, which sets the attack reach (0.9 x). Seated near the fight the seat does not survive, so the proof widens it; the rule itself is unchanged.</summary>
        public static Dictionary<string, object> Leash(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("SortieProof", "Leash", "requires a nomodkit scenario run");
            float before = CombatSupervisor.LeashMetres;
            CombatSupervisor.LeashMetres = Number(args, "metres", before);
            return new Dictionary<string, object> { { "ok", true }, { "before", before }, { "after", CombatSupervisor.LeashMetres } };
        }

        /// <summary>The radial path: FORM UP for the whole wing, as the C-key menu sends it.</summary>
        public static Dictionary<string, object> Funnel(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("SortieProof", "Funnel", "requires a nomodkit scenario run");
            if (WingService.Instance == null) return Failure("SortieProof", "Funnel", "the wing is not active");
            WingCommands.FormUp(WingScope.Wing);
            return new Dictionary<string, object> { { "ok", true } };
        }
    }
}
#endif
