using System;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>A PLAN map tool (spec bezel v2 §6): what the next right-click adds to the selected lane. RE-PLACE puts the selected
    /// step's point, area or targets somewhere else. At most one tool or one TACTICAL order mode is armed.</summary>
    internal enum PlanTool : byte { Off, Move, Route, Orbit, Cap, Sweep, Attack, Land, Cargo, Replace }

    /// <summary>Edits a <see cref="WingPlan"/> from the map's tools and the step editor (spec bezel v2 §5 PLAN › ELEMENTS): new steps
    /// with an end that can happen, insertion after the selected step, removal and reordering that keep every AFTER on the step it
    /// named, and AFTER cycling through the other lanes' steps that would not wait for themselves.</summary>
    internal static class PlanEdit
    {
        /// <summary>An ORBIT step's default time, s.</summary>
        public static float OrbitSeconds = 180f;
        public const int MaxRoutePoints = 16;

        /// <summary>An ATTACK step on one target, ending when its targets are down.</summary>
        public static PlanStep Attack(uint target) =>
            new PlanStep { Kind = PlanKind.Attack, Targets = new[] { target }, End = PlanEnd.TargetsDown };

        /// <summary>Inserts after step <paramref name="after"/> of the lane (-1 or its last: at the end); the index, or -1 when the
        /// lane is full. AFTERs on the lane's later steps follow them.</summary>
        public static int Insert(WingPlan plan, int lane, int after, PlanStep step)
        {
            if (lane < 0 || lane >= WingPlan.Lanes || step == null || plan.Steps[lane].Count >= WingPlan.MaxSteps) return -1;
            int count = plan.Steps[lane].Count;
            int at = after < 0 || after >= count - 1 ? count : after + 1;
            ForEachLink(plan, (s) =>
            {
                if (s.AfterLane == lane && s.AfterStep >= at) s.AfterStep++;
            });
            plan.Steps[lane].Insert(at, step);
            return at;
        }

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

        /// <summary>Swaps a step with its neighbour (<paramref name="dir"/> -1 up, +1 down); AFTERs follow the steps they named.</summary>
        public static bool MoveStep(WingPlan plan, int lane, int step, int dir)
        {
            if (lane < 0 || lane >= WingPlan.Lanes) return false;
            var steps = plan.Steps[lane];
            int to = step + dir;
            if (step < 0 || step >= steps.Count || to < 0 || to >= steps.Count) return false;
            PlanStep moved = steps[step];
            steps[step] = steps[to];
            steps[to] = moved;
            ForEachLink(plan, (s) =>
            {
                if (s.AfterLane != lane) return;
                if (s.AfterStep == step) s.AfterStep = to;
                else if (s.AfterStep == to) s.AfterStep = step;
            });
            return true;
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

        /// <summary>The step's point is put somewhere else (its altitude and arrival kept); a route starts again from it.</summary>
        public static void Replace(PlanStep p, float x, float z)
        {
            Waypoint w = p.Points != null && p.Points.Length > 0 ? p.Points[0] : new Waypoint { Altitude = float.NaN, Speed = float.NaN };
            w.X = x;
            w.Z = z;
            p.Points = new[] { w };
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
