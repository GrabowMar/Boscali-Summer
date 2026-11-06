using System.Globalization;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>The words of BEHAVIOUR › SORTIE (spec bezel v2 §5): a step's START, KIND and state, the armed map tool's cue, and the
    /// plan bar. Safe glyphs only (· and +).</summary>
    internal static class PlanWords
    {
        public static string Start(WingPlan plan, int lane, int step)
        {
            PlanStep p = plan.Steps[lane][step];
            switch (p.Start)
            {
                case PlanStart.TPlus: return "T+" + WmcText.Clock(p.Delay);
                case PlanStart.After:
                    return "AFTER " + PlanRules.Name(p.AfterLane, p.AfterStep) + (p.Delay > 0f ? " +" + WmcText.Clock(p.Delay) : "");
                default:
                    // Later steps chain in their lane: they go when the one before is done.
                    if (step > 0) return "AFTER " + PlanRules.Name(lane, step - 1);
                    return "EXEC";
            }
        }

        public static string Kind(PlanKind k)
        {
            switch (k)
            {
                case PlanKind.FormUp: return "FORM UP";
                default: return k.ToString().ToUpperInvariant();
            }
        }

        /// <summary>WAIT before EXECUTE; then RUN, DONE, HELD, BLOCKED, SKIP, NEXT for the step after the one running.</summary>
        public static string State(PlanRunner r, int lane, int step)
        {
            if (r == null) return "WAIT";
            switch (r.State(lane, step))
            {
                case StepState.Running: return "RUN";
                case StepState.Done: return "DONE";
                case StepState.Held: return "HELD";
                case StepState.Blocked: return "BLOCKED";
                case StepState.Skipped: return "SKIP";
                default:
                    int current = r.Current(lane);
                    return r.Running && current >= 0 && (step == current || step == current + 1) ? "NEXT" : "WAIT";
            }
        }

        /// <summary>"STRIKE NORTH · DRAFT · 7 STEPS".</summary>
        public static string Bar(WingPlan plan, bool running, bool completed)
        {
            int n = 0;
            for (int l = 0; l < WingPlan.Lanes; l++) n += plan.Steps[l].Count;
            string state = running ? "RUNNING" : completed ? "DONE" : "DRAFT";
            return plan.Name + " · " + state + " · " + n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " STEP" : " STEPS");
        }
    }
}
