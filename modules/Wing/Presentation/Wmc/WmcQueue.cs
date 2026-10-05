using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>ORDERS' queue (spec 2026-10-04 §4.2): a map order placed with shift held (or the QUEUE toggle on) is appended as a
    /// <see cref="PlanStep"/> to the scope's element lane in the wing plan, instead of replacing the element's task. The plan runs from
    /// ORDERS' RUN (or BEHAVIOUR › PLAN's EXECUTE).</summary>
    internal static class WmcQueue
    {
        /// <summary>The plan step a map click makes (move, orbit, CAP, SWEEP, land, cargo); attack and route keep shift's own meaning.</summary>
        public static bool TryKind(MapClick click, out PlanKind kind)
        {
            switch (click)
            {
                case MapClick.Move: kind = PlanKind.Move; return true;
                case MapClick.Orbit: kind = PlanKind.Orbit; return true;
                case MapClick.Cap: kind = PlanKind.Cap; return true;
                case MapClick.Sweep: kind = PlanKind.Sweep; return true;
                case MapClick.Land: kind = PlanKind.Land; return true;
                case MapClick.Cargo: kind = PlanKind.Cargo; return true;
                default: kind = PlanKind.Move; return false;
            }
        }

        /// <summary>Appends the step at (<paramref name="point"/>) to the scope's element lane; says why when it cannot.</summary>
        public static void Append(WmcContext c, PlanKind kind, GlobalPosition point, float radius)
        {
            WingPlans plans = WingPlans.Instance;
            if (c == null || !c.CanOrder || plans == null)
            {
                WingToast.Show(c != null && c.Client ? "WMC: orders are host only for now" : "Wing Command is not ready");
                return;
            }
            if (plans.Running)
            {
                WingToast.Show("The plan is running: edit it on BEHAVIOUR › PLAN");
                return;
            }
            Waypoint at = Waypoint.At(point.x, point.z);
            at.Altitude = c.Draft.Altitude;
            at.Speed = c.Draft.Speed;
            var step = new PlanStep { Kind = kind, Points = new[] { at }, Radius = radius };
            // An orbit or an area never arrives: it ends on a timer (PlanRules), five minutes unless the plan's editor says otherwise.
            if (kind == PlanKind.Orbit || kind == PlanKind.Cap || kind == PlanKind.Sweep)
            {
                step.End = PlanEnd.Time;
                step.EndSeconds = 300f;
            }
            int lane = c.ScopeElement;
            plans.ForgetRun();
            if (!plans.Plan.Add(lane, step))
            {
                WingToast.Show("Lane " + ElementRoster.Letter(lane) + " is full (12 steps)");
                return;
            }
            WingToast.Show("Queued " + PlanWords.Kind(kind) + " on " + ElementRoster.Letter(lane) + " (" + plans.Plan.Steps[lane].Count + "): RUN starts the queue");
        }
    }
}
