using System.Globalization;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>The words of PLAN › ELEMENTS (spec bezel v2 §5): a step row's START, KIND, detail, END and state, the cue for the
    /// selected step or the armed tool, and the plan bar. Safe glyphs only (· and +).</summary>
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
                    return p.Start == PlanStart.Now ? "NOW" : "EXEC";
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

        /// <summary>What the armed tool wants next, for the lane named <paramref name="lane"/>.</summary>
        public static string ToolCue(PlanTool tool, string lane)
        {
            switch (tool)
            {
                case PlanTool.Off: return "";
                case PlanTool.Cap:
                case PlanTool.Sweep: return "PLAN " + tool.ToString().ToUpperInvariant() + " · " + lane + " · RIGHT-PRESS, DRAG FOR THE RADIUS";
                case PlanTool.Route: return "PLAN ROUTE · " + lane + " · RIGHT-CLICK POINTS, DONE ENDS IT";
                case PlanTool.Attack: return "PLAN ATTACK · " + lane + " · RIGHT-CLICK AN ENEMY (SHIFT ADDS)";
                case PlanTool.Replace: return "RE-PLACE · " + lane + " · RIGHT-CLICK THE NEW POINT OR TARGET";
                default: return "PLAN " + tool.ToString().ToUpperInvariant() + " · " + lane + " · RIGHT-CLICK THE MAP";
            }
        }
    }
}
