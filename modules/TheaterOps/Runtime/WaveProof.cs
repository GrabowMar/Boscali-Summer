#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>
    /// Host-truth readback and levers for the reinforcement proof (<c>tests/ingame/str-4-sorties.json</c>): the bank, the price the snapshot published, the purchase's own
    /// before/after numbers (taken inside Buy in one frame, so a pool moved by kills cannot fake them) and the AI timer. SetPool and AiSwitch change state, so every verb refuses
    /// unless a nomodkit scenario is running. Compiled out of public releases. An unknown number is -1.
    /// </summary>
    public static class WaveProof
    {
        private static bool InSimRun => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("WaveProof", "Read", "requires a nomodkit scenario run");
            LivingFrontService living = LivingFrontService.Active;
            TheaterLogisticsService logistics = TheaterLogisticsService.Active;
            if (living == null || logistics == null) return Failure("WaveProof", "Read", "the Living Front service is not running");
            if (!GameAccess.TryFindFaction(Text(args, "faction"), out FactionHQ hq)) return Failure("WaveProof", "Read", "unknown faction");
            ReinforcementDesk desk = living.Desk;
            desk.TryRead(hq, out int credits, out int bought, out int seq, out int aiWaves, out string trail, out int priceMatches);
            TheaterWaveView view = desk.View(hq, Time.timeSinceLevelLoad);
            int purchases = logistics.Purchases;
            bool any = purchases > 0;
            var lastBuy = new Dictionary<string, object>
            {
                { "poolBefore", logistics.LastPoolBefore }, { "poolAfter", logistics.LastPoolAfter }, { "cost", logistics.LastCost },
                // The pool fell by exactly the price (less a grant, which only the event door uses): 1 or 0, and 0 when nothing was bought.
                { "exactOk", Flag(any && Math.Abs(logistics.LastPoolBefore - logistics.LastPoolAfter - (logistics.LastCost - logistics.LastGrant)) < 0.01f) },
                { "supplyDeltaOk", Flag(any && logistics.LastSupplyAfter - logistics.LastSupplyBefore == logistics.LastSupplyUnits) },
                { "priceMatches", priceMatches },
            };
            var wave = new Dictionary<string, object>
            {
                { "state", view != null ? (int)view.State : -1 }, { "block", view != null ? (int)view.Block : -1 }, { "credits", credits }, { "bought", bought },
                { "cost", view != null ? view.Cost : -1 }, { "pool", view != null ? view.Pool : -1 }, { "group", view != null ? view.Group : "" },
                { "depotKnown", Flag(view != null && view.DepotKnown) }, { "trail", trail }, { "lastBuy", lastBuy },
                { "notice", new Dictionary<string, object> { { "sortie", Presentation.TheaterOpsHudLine.SortieNotices }, { "counted", Presentation.TheaterOpsHudLine.CountedNotices },
                    { "wave", Presentation.TheaterOpsHudLine.WaveNotices }, { "last", Presentation.TheaterOpsHudLine.LastSortieNotice } } },
                { "spentTotal", logistics.SpentTotal }, { "purchases", purchases }, { "aiWaves", AiWaves(desk) }, { "ownAiWaves", aiWaves }, { "lastOutcomeSeq", seq },
            };
            return new Dictionary<string, object> { { "ok", true }, { "wave", wave } };
        }

        /// <summary>{faction (default: the seat's), value}: sets the faction pool to value.</summary>
        public static Dictionary<string, object> SetPool(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("WaveProof", "SetPool", "requires a nomodkit scenario run");
            if (!GameAccess.TryFindFaction(Text(args, "faction"), out FactionHQ hq)) return Failure("WaveProof", "SetPool", "unknown faction");
            float before = hq.factionFunds;
            hq.AddFunds(Number(args, "value", before) - before);
            return new Dictionary<string, object> { { "ok", true }, { "before", before }, { "after", hq.factionFunds } };
        }

        /// <summary>{on}: the host switch AI STAFF CONVOYS, live; turning it on makes every AI-only faction's next purchase due now.</summary>
        public static Dictionary<string, object> AiSwitch(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("WaveProof", "AiSwitch", "requires a nomodkit scenario run");
            LivingFrontService living = LivingFrontService.Active;
            if (living == null) return Failure("WaveProof", "AiSwitch", "the Living Front service is not running");
            bool on = Bool(args, "on");
            living.Desk.SetAiSwitch(on);
            if (on) foreach (FactionHQ hq in FactionRegistry.GetAllHQs()) living.Desk.ArmAi(hq);
            return new Dictionary<string, object> { { "ok", true }, { "on", Flag(on) } };
        }

        // Waves the desk bought for every faction without a pilot: the number the default switch must keep at zero.
        private static int AiWaves(ReinforcementDesk desk)
        {
            int total = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                if (hq != null && (hq.factionPlayers == null || hq.factionPlayers.Count == 0) && desk.TryRead(hq, out _, out _, out _, out int ai, out _, out _)) total += ai;
            return total;
        }

        private static int Flag(bool value) => value ? 1 : 0;
    }
}
#endif
