using NOAvionics;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL › ROUTE · AP, the Flight Plan (spec 2026-10-04 §4.2; mockup board/route.html panel 4): a leg table (#, ALT, SPD,
    /// AT POINT) whose rows select a point for the altitude and speed steppers and ACTION, ONCE · LOOP · PING-PONG and SEND TO WING;
    /// DRAW, UNDO and CLEAR above the table (the route itself is drawn on the maximized map). While the draft is empty the table shows
    /// the scope's own route read-only (its leg marked). Then MY AUTOPILOT as one annunciator row coupled to the route (NAV, AP OFF).
    /// The scope is the wing table's.</summary>
    internal sealed class WmcRoute : IWmcPage
    {
        private const string EmptyLegs = "NO POINTS · PRESS DRAW, THEN RIGHT-CLICK THE MAP";

        private AvFlow flow;
        private WmcContext last;
        private AvSection legsSection;
        private WmcLegTable legs;
        private AvStepper altStepper, spdStepper;
        private AvSegmented loopRow;
        private AvRow apRow;
        private AvControl apNav, apOff;
        private readonly RouteData data = new RouteData();
        private AvControl draw, action, del, skip, send, undo, clear;
        private string altText = "", spdText = "", legsShown, apNoteShown;
        private int apKey = int.MinValue, loopValue = -1;

        public string Hint => "DRAW, then right-click the map to add points; SEND flies them. NAV flies your own aircraft along them.";

        public string Alert => null;

        public void Build(AvFlow pageFlow, AvTicker ticker, int pageIndex)
        {
            flow = pageFlow;
            legsSection = flow.Section(AvIcon.MapPin, "ROUTE", EmptyLegs);

            // In the order a route is made: draw it, set its points, fly it.
            AvControl[] tools = flow.Buttons(
                new AvControl.Spec("DRAW", ToggleDraw, AvButtonStyle.Default, AvIcon.MapPin),
                new AvControl.Spec("UNDO", () => EditDraft(d => d.Undo())),
                new AvControl.Spec("CLEAR", () => EditDraft(d => d.Clear()))).Controls;
            draw = tools[0];
            undo = tools[1];
            clear = tools[2];
            draw.Help = "Right-click the map to add route points while DRAW is lit.";

            legs = flow.Add(new WmcLegTable(flow.Content, PickPoint));

            flow.Section(AvIcon.Pencil, "POINT", "THE SELECTED ONE, OR NEW POINTS");
            altStepper = flow.Add(new AvStepper(flow.Content, "ALTITUDE", () => altText, () => EditDraft(d => d.StepAltitude(-1)),
                () => EditDraft(d => d.StepAltitude(+1))));
            spdStepper = flow.Add(new AvStepper(flow.Content, "SPEED", () => spdText, () => EditDraft(d => d.StepSpeed(-1)),
                () => EditDraft(d => d.StepSpeed(+1))));
            altStepper.Minus.Help = altStepper.Plus.Help = "Altitude of the selected point, or of new route points (AUTO: the task's own).";
            spdStepper.Minus.Help = spdStepper.Plus.Help = "Speed of the selected point, or of new route points (AUTO: cruise).";
            AvControl[] edit = flow.Buttons(
                new AvControl.Spec("ACTION", () => EditDraft(d => d.CycleAction())),
                new AvControl.Spec("DEL PT", () => EditDraft(d => d.RemoveSelected()), AvButtonStyle.Default, AvIcon.X)).Controls;
            action = edit[0];
            del = edit[1];

            flow.Section(AvIcon.PlayerPlay, "FLY", "THE WING FLIES THE DRAFT");
            loopRow = flow.Add(AvSegmented.Strip(flow.Content, new[] { "ONCE", "LOOP", "PING-PONG" }, () => loopValue, SetLoop));
            string[] loopTips = { "Fly the route once.", "Fly the route round and round.", "Fly the route back and forth." };
            for (int i = 0; i < loopTips.Length; i++)
            {
                loopRow.Options[i].Help = loopTips[i];
            }
            AvControl[] fly = flow.Buttons(
                new AvControl.Spec("SEND TO WING", Send, AvButtonStyle.Primary, AvIcon.CircleCheck),
                new AvControl.Spec("SKIP LEG", () => WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.SkipLeg, last.Scope))))).Controls;
            send = fly[0];
            skip = fly[1];

            flow.Section(AvIcon.Plane, "MY AUTOPILOT", "NAV · COUPLED TO ROUTE");
            apRow = flow.Add(new AvRow(flow.Content));
            apNav = apRow.AddTrailing(new AvControl.Spec("NAV", PressNav));
            apOff = apRow.AddTrailing(new AvControl.Spec("AP OFF", PressOff, AvButtonStyle.Quiet));
            apNav.Help = "Fly the route you drew (or the scope's route when the draft is empty).";
            apOff.Help = "Autopilot off.";
        }

        public void Shown(WmcContext c)
        {
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            RefreshRoute(c);
        }

        private void ToggleDraw()
        {
            if (last == null) return;
            last.Map.Arm(last, last.Map.Mode == MapMode.Route ? MapMode.Off : MapMode.Route);
        }

        /// <summary>A draft edit: local, allowed on a client too; only SEND is an order.</summary>
        private void EditDraft(System.Action<RouteDraft> edit)
        {
            if (last != null) edit(last.Draft);
        }

        private void SetLoop(int target)
        {
            if (last == null) return;
            for (int i = 0; i < 3 && (int)last.Draft.Loop != target; i++) last.Draft.CycleLoop();
        }

        private void PickPoint(int item)
        {
            if (last != null && !data.ReadOnly) last.Draft.Select(item);
        }

        private void Send()
        {
            WmcMotion.Punch(send);
            WmcUi.Order(last, () =>
            {
                if (!last.Draft.TryTask(out WingTask task, out string why))
                {
                    WingToast.Show(why);
                    return;
                }
                if (!WingOrders.Run(WingOrder.Tasked(task, last.Scope)).Accepted) return;
                last.Draft.Clear();
                if (last.Map.Mode == MapMode.Route) last.Map.Disarm();
            });
        }

        private void PressOff()
        {
            if (PlayerAutopilot.Instance == null) WingToast.Show("Autopilot unavailable");
            else WingCommands.Autopilot(ApCommand.Off);
        }

        /// <summary>NAV flies the draft; with no draft, the scope's own route (what the element is flying now).</summary>
        private void PressNav()
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null)
            {
                WingToast.Show("Autopilot unavailable");
                return;
            }
            RouteDraft d = last?.Draft;
            if (d != null && d.Count > 0)
            {
                var pts = new Waypoint[d.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = d[i];
                ap.EngageNav(pts, pts.Length);
                return;
            }
            WingPlanner p = last?.Wing != null && !last.Client && last.Wing.Roster.InUse(last.ScopeElement) ? last.Wing.PlannerOf(last.ScopeElement) : null;
            WingTask task = p != null && p.Active ? p.Current : null;
            bool path = task != null && (task.Kind == TaskKind.Move || task.Kind == TaskKind.Route || task.Kind == TaskKind.Patrol);
            ap.EngageNav(path ? task.Points : null, path ? task.Points.Length : 0);
        }

        /// <summary>The route as <see cref="RouteData"/>: the draft's points while it has any (editable), else the scope's own path task
        /// (read-only, its leg marked).</summary>
        private void Collect(WmcContext c, WingPlanner p)
        {
            data.Reset();
            RouteDraft d = c.Draft;
            Waypoint[] task = null;
            if (d.Count == 0 && p != null && p.Active && p.Current != null && p.Current.Points != null && p.Current.Points.Length > 0
                && (p.Current.Kind == TaskKind.Move || p.Current.Kind == TaskKind.Route || p.Current.Kind == TaskKind.Patrol))
                task = p.Current.Points;
            int n = task != null ? Mathf.Min(task.Length, RouteData.Max) : d.Count;
            data.Count = n;
            data.ReadOnly = task != null;
            data.Selected = task != null ? -1 : d.Selected;
            data.Current = task != null ? Mathf.Clamp(p.Leg, 0, n - 1) : -1;
            for (int i = 0; i < n; i++)
            {
                Waypoint wp = task != null ? task[i] : d[i];
                data.Alt[i] = float.IsNaN(wp.Altitude) ? "AUTO" : AvNum.Fixed(wp.Altitude, 0);
                data.Spd[i] = float.IsNaN(wp.Speed) ? "AUTO" : AvNum.Fixed(wp.Speed * 3.6f, 0);
                data.At[i] = RouteDraft.ActionText(wp);
            }
        }

        private void RefreshRoute(WmcContext c)
        {
            WingService w = c.Wing;
            WingPlanner p = w != null && !c.Client && w.Roster.InUse(c.ScopeElement) ? w.PlannerOf(c.ScopeElement) : null;
            RouteDraft d = c.Draft;
            draw.Latched = c.Map.Mode == MapMode.Route;
            draw.Interactable = c.CanOrder;
            Collect(c, p);
            Show(d);

            bool any = d.Count > 0, picked = d.Selected >= 0, canSkip = c.CanOrder && p != null && p.Active && p.Leg >= 0;
            Gate(action, picked, "Click a point row first", "The selected point's arrival: orbit 2 min, land, cargo, none.");
            Gate(del, picked, "Click a point row first", "Remove the selected point.");
            Gate(send, any && c.CanOrder, !c.CanOrder ? "Orders are host only for now" : "Add points first",
                "Fly the draft: one point is a MOVE, more a ROUTE; LOOP or PING-PONG makes a patrol.");
            Gate(undo, any, "The draft is empty", "Remove the last point.");
            Gate(clear, any, "The draft is empty", "Empty the draft.");
            Gate(skip, canSkip, "The scope has no route leg to skip", "The scope's task goes on to its next point now.");
            foreach (AvControl o in loopRow.Options) if (o.Interactable != any) o.Interactable = any;
            RefreshAutopilot();
        }

        /// <summary>Lists <see cref="data"/> and syncs the section caption and the steppers with the draft <paramref name="d"/>.</summary>
        private void Show(RouteDraft d)
        {
            legs.Show(data);
            string note;
            if (data.Count == 0) note = EmptyLegs;
            else if (data.ReadOnly) note = "FLYING · LEG " + AvNum.Fixed(data.Current + 1, 0) + "/" + AvNum.Fixed(data.Count, 0);
            else note = AvNum.Fixed(data.Count, 0) + " PTS · " + RouteDraft.LoopText(d.Loop);
            if (note != legsShown)
            {
                legsShown = note;
                legsSection.SetCaption(note);
            }
            float alt = d.Selected >= 0 ? d[d.Selected].Altitude : d.Altitude, spd = d.Selected >= 0 ? d[d.Selected].Speed : d.Speed;
            string altNow = RouteDraft.AltitudeText(alt), spdNow = RouteDraft.SpeedText(spd);
            if (altNow != altText)
            {
                altText = altNow;
                altStepper.Refresh();
            }
            if (spdNow != spdText)
            {
                spdText = spdNow;
                spdStepper.Refresh();
            }
            if ((int)d.Loop != loopValue)
            {
                loopValue = (int)d.Loop;
                loopRow.Refresh();
            }
        }

        /// <summary>Enabled, or disabled with the reason as its help (0.9 critique §2.14: nothing disables silently).</summary>
        private static void Gate(AvControl b, bool on, string why, string tip)
        {
            b.Interactable = on;
            string help = on ? tip : why;
            if (!ReferenceEquals(b.Help, help)) b.Help = help;
        }

        private void RefreshAutopilot()
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null)
            {
                SetApNote("AUTOPILOT UNAVAILABLE", AvState.Inert);
                return;
            }
            HoldSpec h = ap.Session.Spec;
            apNav.Latched = h.Lateral == LateralHold.Nav;
            apOff.Latched = !ap.Session.Engaged;
            int key = (int)h.Lateral * 7 + (int)h.Vertical * 3 + (h.Speed ? 1 : 0) + Mathf.RoundToInt(h.HeadingDeg) * 101
                + Mathf.RoundToInt(h.AltitudeM) * 1009 + Mathf.RoundToInt(h.VerticalSpeedMps * 10f) * 13 + Mathf.RoundToInt(h.SpeedMps) * 17
                + ap.Nav.Index * 5 + ap.Nav.Count * 1021 + (ap.Session.LateralOverride ? 2 : 0) + (ap.Session.VerticalOverride ? 4 : 0);
            if (key == apKey) return;
            apKey = key;
            string text = WingHudText.Autopilot(h, ap.Session.LateralOverride, ap.Session.VerticalOverride, ap.Nav.Index, ap.Nav.Count);
            SetApNote(text.Length > 0 ? text : "AUTOPILOT DISENGAGED", text.Length > 0 ? AvState.Ready : AvState.Inert);
        }

        private void SetApNote(string text, AvState state)
        {
            if (text == apNoteShown) return;
            apNoteShown = text;
            apRow.Set(text, "", "", state);
            flow.RequestRelayout();
        }
    }
}
