using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Ops
{
    /// <summary>What an AI-controlled faction's operations driver needs from the live game. The runtime implements it over the real desks and ledger; the offline sims over scripted ones.</summary>
    internal interface IAiOpsHost
    {
        /// <summary>Connected humans of the faction: the driver only works a faction with none.</summary>
        int Humans { get; }

        /// <summary>The facts of one decision, all read from the faction's own desks and the objective census. False when the faction has no OPERATIONS.</summary>
        bool TryFacts(float now, out AiOpsFacts facts);

        /// <summary>PLAN through the same host path a human uses (the target is validated against the faction's own fog).</summary>
        OpResult Plan(OpKind kind, int target);

        /// <summary>One FUND tap through the same host path, paid from the treasury.</summary>
        OpResult Fund(OpDomain domain, bool large);
    }

    /// <summary>
    /// The operations driver of one AI-controlled faction: while it leads and its treasury is over 500 CR it plans an operation from what its own CYBER and SOF can legally see and funds it,
    /// at most one plan or tap every 60 s, and at most one new plan every 30 minutes (core 7a: 2 to 3 completed operations per faction per match). It reads no enemy state, and a refusal costs it
    /// the same minute a success would.
    /// </summary>
    internal sealed class AiOpsBrain : WatchBrainBase
    {
        public const float ThinkSeconds = 2f, PlanSpacingSeconds = 1800f;
        private float lastPlanAt = float.NegativeInfinity;

        public int Plans, Funds, Failures, AsatsPlanned;
        public AiOpsPlan Last { get; private set; }
        public OpResult LastResult { get; private set; }

        public override void Reset() { base.Reset(); lastPlanAt = float.NegativeInfinity; Plans = Funds = Failures = AsatsPlanned = 0; Last = default; LastResult = default; }

        public AiOpsPlan Step(IAiOpsHost host, WatchPacer pacer, float now)
        {
            if (!SpaceRules.MissionTime(now) || now < nextThinkAt) return new AiOpsPlan(AiOpsAction.None, AiOpsWhy.Waiting);
            nextThinkAt = now + ThinkSeconds;
            if (host.Humans > 0 || !pacer.CanFund(now) || !host.TryFacts(now, out AiOpsFacts facts)) return new AiOpsPlan(AiOpsAction.None, AiOpsWhy.Waiting);
            facts.AsatsPlanned = AsatsPlanned;
            AiOpsPlan plan = AiRules.Decide(facts);
            if (plan.Action == AiOpsAction.None) { Last = plan; return plan; }
            if (plan.Action == AiOpsAction.Plan && now - lastPlanAt < PlanSpacingSeconds) { Last = new AiOpsPlan(AiOpsAction.None, AiOpsWhy.Waiting); return Last; }
            OpResult r = plan.Action == AiOpsAction.Plan ? host.Plan(plan.Kind, plan.Target) : host.Fund(plan.Domain, plan.Large);
            pacer.NoteFund(now); // a refusal waits a minute like a success: no hammering
            LastResult = r;
            if (!r.Ok) { Failures++; Last = new AiOpsPlan(AiOpsAction.None, AiOpsWhy.Waiting); return Last; }
            if (plan.Action == AiOpsAction.Plan) { Plans++; lastPlanAt = now; if (plan.Kind == OpKind.Asat) AsatsPlanned++; } else Funds++;
            Last = plan;
            return plan;
        }
    }
}
