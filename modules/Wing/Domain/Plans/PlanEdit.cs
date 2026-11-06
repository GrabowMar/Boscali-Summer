using System;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Edits a <see cref="WingPlan"/> from the step editor (BEHAVIOUR › SORTIE): removal and reordering that keep every AFTER on
    /// the step it named, and AFTER cycling through the other lanes' steps that would not wait for themselves.</summary>
    internal static class PlanEdit
    {
        /// <summary>An ORBIT step's default time, s.</summary>
        public static float OrbitSeconds = 180f;

        /// <summary>Removes a step: what waited for it starts on EXECUTE, AFTERs on the lane's later steps follow them.</summary>
        public static void Remove(WingPlan plan, int lane, int step)
        {
            if (lane < 0 || lane >= WingPlan.Lanes || step < 0 || step >= plan.Steps[lane].Count) return;
            plan.Steps[lane].RemoveAt(step);
            ForEachLink(plan, (s) =>
            {
                if (s.AfterLane != lane) return;
                if (s.AfterStep == step)
                {
                    s.Start = PlanStart.Exec;
                    s.AfterLane = s.AfterStep = -1;
                    s.Delay = 0f;
                }
                else if (s.AfterStep > step) s.AfterStep--;
            });
        }

        /// <summary>Swaps a step with its neighbour (<paramref name="dir"/> -1 up, +1 down); AFTERs follow the steps they named. False,
        /// with nothing changed, at the lane's end or when the new order would make a step wait for itself.</summary>
        public static bool MoveStep(WingPlan plan, int lane, int step, int dir)
        {
            if (lane < 0 || lane >= WingPlan.Lanes) return false;
            int to = step + dir, count = plan.Steps[lane].Count;
            if (step < 0 || step >= count || to < 0 || to >= count) return false;
            Swap(plan, lane, step, to);
            if (!WaitCycle(plan)) return true;
            Swap(plan, lane, step, to);   // a swap is its own undo
            return false;
        }

        private static void Swap(WingPlan plan, int lane, int step, int to)
        {
            var steps = plan.Steps[lane];
            PlanStep moved = steps[step];
            steps[step] = steps[to];
            steps[to] = moved;
            ForEachLink(plan, (s) =>
            {
                if (s.AfterLane != lane) return;
                if (s.AfterStep == step) s.AfterStep = to;
                else if (s.AfterStep == to) s.AfterStep = step;
            });
        }

        /// <summary>Whether a step waits for itself through the lane order and the AFTER links (what EXECUTE refuses).</summary>
        private static bool WaitCycle(WingPlan plan)
        {
            for (int l = 0; l < WingPlan.Lanes; l++)
                for (int s = 0; s < plan.Steps[l].Count; s++)
                {
                    PlanStep p = plan.Steps[l][s];
                    if (p.Start == PlanStart.After && p.AfterLane >= 0 && p.AfterLane < WingPlan.Lanes && p.AfterStep >= 0
                        && p.AfterStep < plan.Steps[p.AfterLane].Count && PlanRules.WaitsFor(plan, p.AfterLane, p.AfterStep, l, s)) return true;
                }
            return false;
        }

        /// <summary>Makes the step wait for the next of the other lanes' steps (lane by lane, wrapping) that does not already wait
        /// for it; false, with nothing changed, when none may.</summary>
        public static bool NextAfter(WingPlan plan, int lane, int step)
        {
            if (lane < 0 || lane >= WingPlan.Lanes || step < 0 || step >= plan.Steps[lane].Count) return false;
            PlanStep p = plan.Steps[lane][step];
            // The candidates in order: every step of the other lanes; start past the current one.
            int total = 0, current = -1;
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                if (l == lane) continue;
                for (int s = 0; s < plan.Steps[l].Count; s++)
                {
                    if (p.Start == PlanStart.After && p.AfterLane == l && p.AfterStep == s) current = total;
                    total++;
                }
            }
            for (int k = 1; k <= total; k++)
            {
                int want = (current + k) % total, n = 0;
                for (int l = 0; l < WingPlan.Lanes; l++)
                {
                    if (l == lane) continue;
                    for (int s = 0; s < plan.Steps[l].Count; s++, n++)
                    {
                        if (n != want) continue;
                        if (PlanRules.WaitsFor(plan, l, s, lane, step)) goto next;
                        p.Start = PlanStart.After;
                        p.AfterLane = l;
                        p.AfterStep = s;
                        return true;
                    }
                }
                next:;
            }
            return false;
        }

        /// <summary>Where the step leaves its element: its last point (false for ATTACK, RTB, REFIT and FORM UP).</summary>
        public static bool EndPoint(PlanStep p, out float x, out float z)
        {
            x = z = 0f;
            if (p?.Points == null || p.Points.Length == 0 || p.Kind == PlanKind.Attack) return false;
            Waypoint w = p.Points[p.Points.Length - 1];
            x = w.X;
            z = w.Z;
            return true;
        }

        private static void ForEachLink(WingPlan plan, Action<PlanStep> act)
        {
            for (int l = 0; l < WingPlan.Lanes; l++)
                foreach (PlanStep s in plan.Steps[l])
                    if (s.Start == PlanStart.After) act(s);
        }
    }
}
