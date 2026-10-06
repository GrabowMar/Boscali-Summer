using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>BEHAVIOUR › SORTIE, the plan half (spec 2026-10-04 §4.4): the timeline over the plan bar (PLANS ›, SAVE, SKIP STEP, ABORT,
    /// EXECUTE), the selected lane's SELECT · FIT · FORM UP (and RESUME · RETRY while its step is held or refused), the selected step's
    /// planned-against-actual line and, while a step is picked, a compact step editor: START, DELAY, END, TIME and UP · DOWN · DELETE.
    /// Plans are built on ORDERS by queueing (SHIFT adds a step); the map tools, ALTITUDE, RADIUS and RE-PLACE of the old PLAN page left
    /// with them. While a plan runs it is read-only.</summary>
    internal sealed partial class WmcPlan
    {
        private static readonly PlanStart[] Starts = { PlanStart.Now, PlanStart.Exec, PlanStart.TPlus, PlanStart.After };
        private static readonly PlanEnd[] Ends = { PlanEnd.Arrive, PlanEnd.Time, PlanEnd.Bingo, PlanEnd.Winchester, PlanEnd.TargetsDown };
        private static readonly string[] StartTips =
        {
            "Goes as soon as the plan runs.", "Goes on EXECUTE.", "Goes its time after EXECUTE.",
            "Goes when another lane's step is done (press again for the next).",
        };
        private static readonly string[] EndTips =
        {
            "Ends when its task is flown.", "Ends after its time.", "Ends when a member is at bingo fuel.",
            "Ends when every member is out of ammunition.", "Ends when its targets are down.",
        };
        private const string RetryTip = "Send the refused step again.";

        private readonly List<uint> elementIds = new List<uint>();
        private readonly ConfirmGate planGate = new ConfirmGate();
        private readonly List<RouteLeg> cardLegs = new List<RouteLeg>();
        private readonly List<RouteRing> cardRings = new List<RouteRing>();
        private readonly List<AvPopupEntry> planEntries = new List<AvPopupEntry>(PlanStore.Max + 2);
        private readonly List<int> planIndices = new List<int>(PlanStore.Max);
        private AvSection sortieSection;
        private WmcCollapsible laneGate, runGate, editorGate;
        private AvRow stepRow;
        private AvSection editorSection;
        private AvStepper delayStepper, timeStepper;
        private AvSegmented startSeg, endSeg;
        private AvControl execute, abort, save, skip, plansButton, selectLane, fitLane, formLane, resume, retry, up, down, delete;
        private string delayText = "", timeText = "";
        private int startValue = -1, endValue = -1;
        private int selLane, selStep = -1, lanesShown, version;
        private bool laneHeld, laneBlocked;

        /// <summary>Lanes showing now (automation; was the element cards).</summary>
        public int Cards => lanesShown;
        public int SelectedLane => selLane;
        public int SelectedStep => selStep;

        private static WingPlans Plans => WingPlans.Instance;

        private void BuildSortie(AvFlow f, AvTicker t, int pageIndex)
        {
            sortieSection = f.Section(AvIcon.Activity, "SORTIE", "");
            timeline = f.Add(new SortieTimeline(f.Content));
            timeline.Picked = PickBar;

            AvControl[] bar = f.Buttons(
                new AvControl.Spec("PLANS ›", OpenPlans),
                new AvControl.Spec("SAVE", SavePlan),
                new AvControl.Spec("SKIP STEP", SkipStep),
                new AvControl.Spec("ABORT", Abort, AvButtonStyle.Danger),
                new AvControl.Spec("EXECUTE", Execute, AvButtonStyle.Primary)).Controls;
            plansButton = bar[0];
            save = bar[1];
            skip = bar[2];
            abort = bar[3];
            execute = bar[4];
            plansButton.Help = "This theatre's saved plans, NEW and DELETE.";
            save.Help = "Keep this plan for this theatre (targets are picked again after loading).";
            skip.Help = "Pass over the selected lane's step; what waits for it goes on.";
            abort.Help = "Stop the plan (asks first).";
            execute.Help = "Run the plan: every lane's steps in turn.";
            ids.Add("plan.bar.plans", plansButton);
            ids.Add("plan.bar.save", save);
            ids.Add("plan.bar.skip", skip);
            ids.Add("plan.bar.abort", abort);
            ids.Add("plan.bar.execute", execute);

            // The selected lane: SELECT · FIT · FORM UP, and RESUME · RETRY while its step is held or refused.
            var laneBar = new AvButtons(f.Content, new[]
            {
                new AvControl.Spec("SELECT", () => SelectElement(selLane), AvButtonStyle.Default, AvIcon.Target),
                new AvControl.Spec("FIT", () => FitElement(selLane), AvButtonStyle.Default, AvIcon.Focus2),
                new AvControl.Spec("FORM UP", () => FormElement(selLane), AvButtonStyle.Default, AvIcon.UsersGroup),
            });
            selectLane = laneBar.Controls[0];
            fitLane = laneBar.Controls[1];
            formLane = laneBar.Controls[2];
            selectLane.Help = "Orders go to this lane's element.";
            fitLane.Help = "Frame this element and its plan on the map.";
            formLane.Help = "The element forms up (A on you; the others rejoin A).";
            laneGate = f.Add(new WmcCollapsible(laneBar));
            var runBar = new AvButtons(f.Content, new[]
            {
                new AvControl.Spec("RESUME", () => LaneAct(selLane, 0), AvButtonStyle.Default, AvIcon.PlayerPlay),
                new AvControl.Spec("RETRY", () => LaneAct(selLane, 1), AvButtonStyle.Default, AvIcon.Refresh),
            });
            resume = runBar.Controls[0];
            retry = runBar.Controls[1];
            resume.Help = "The lane was held by your order: send its step again.";
            retry.Help = RetryTip;
            runGate = f.Add(new WmcCollapsible(runBar));
            // Every lane keeps TUNING-era PLAN's ids: they act on that lane, whichever is selected.
            for (int e = 0; e < WingPlan.Lanes; e++)
            {
                int k = e;
                string laneId = "plan.el" + AvNum.Fixed(e, 0);
                ids.Add(laneId + ".select", () => SelectElement(k));
                ids.Add(laneId + ".fit", () => FitElement(k));
                ids.Add(laneId + ".form", () => FormElement(k));
                ids.Add(laneId + ".resume", () => LaneAct(k, 0), () => LaneCan(k, StepState.Held));
                ids.Add(laneId + ".retry", () => LaneAct(k, 1), () => LaneCan(k, StepState.Blocked));
                ids.Add(laneId + ".skip", () => LaneAct(k, 2), () => LaneCan(k, StepState.Pending));
            }
            runGate.Set(false);
            laneGate.Set(false);

            stepRow = f.Add(new AvRow(f.Content));
            stepRow.SetShown(false);
            BuildEditor(f, t);
            BuildLog(f, t);
        }

        /// <summary>Whether lane <paramref name="lane"/> has a step on now that <paramref name="kind"/> acts on (Pending: any).</summary>
        private static bool LaneCan(int lane, StepState kind)
        {
            PlanRunner r = Plans?.Runner;
            if (r == null || !r.Running || lane < 0 || lane >= WingPlan.Lanes) return false;
            int cur = r.Current(lane);
            return cur >= 0 && (kind == StepState.Pending || r.State(lane, cur) == kind);
        }

        private void BuildEditor(AvFlow f, AvTicker t)
        {
            var card = new AvCard(f.Content, t, f.Inner, null, true);
            editorGate = f.Add(new WmcCollapsible(card));
            AvFlow ef = card.Flow;
            editorSection = ef.Section(AvIcon.Pencil, "STEP", "");
            startSeg = ef.Add(new AvSegmented(ef.Content, "START", new[] { "NOW", "EXEC", "T+", "AFTER" }, () => startValue, PickStart));
            string[] startKeys = { "now", "exec", "tplus", "after" };
            for (int i = 0; i < startKeys.Length; i++)
            {
                startSeg.Options[i].Help = StartTips[i];
                ids.Add("plan.edit.start." + startKeys[i], startSeg.Options[i]);
            }
            delayStepper = Stepper(ef, "DELAY", () => delayText, () => StepDelay(-1), () => StepDelay(1), "plan.edit.delay.",
                "Wait less.", "Wait longer (T+ from EXECUTE, AFTER from the other step).");
            endSeg = ef.Add(new AvSegmented(ef.Content, "END", new[] { "ARRIVE", "TIME", "BINGO", "WINCH", "TGT DN" }, () => endValue, PickEnd));
            string[] endKeys = { "arrive", "time", "bingo", "winch", "tgtdn" };
            for (int i = 0; i < endKeys.Length; i++)
            {
                endSeg.Options[i].Help = EndTips[i];
                ids.Add("plan.edit.end." + endKeys[i], endSeg.Options[i]);
            }
            timeStepper = Stepper(ef, "TIME", () => timeText, () => StepTime(-1), () => StepTime(1), "plan.edit.time.", "Shorter.", "Longer.");
            AvControl[] verbs = ef.Buttons(
                new AvControl.Spec("UP", () => MoveSelected(-1), AvButtonStyle.Default, AvIcon.ChevronUp),
                new AvControl.Spec("DOWN", () => MoveSelected(1), AvButtonStyle.Default, AvIcon.ChevronDown),
                new AvControl.Spec("DELETE", DeleteSelected, AvButtonStyle.Danger, AvIcon.X)).Controls;
            up = verbs[0];
            down = verbs[1];
            delete = verbs[2];
            up.Help = "Fly this step earlier in its lane.";
            down.Help = "Fly this step later in its lane.";
            delete.Help = "Remove this step (asks first); what waited for it goes on EXECUTE.";
            ids.Add("plan.edit.up", up);
            ids.Add("plan.edit.down", down);
            ids.Add("plan.edit.delete", delete);
            editorGate.Set(false);
        }

        private AvStepper Stepper(AvFlow f, string label, System.Func<string> value, System.Action minus, System.Action plus, string id,
            string minusTip, string plusTip)
        {
            AvStepper s = f.Add(new AvStepper(f.Content, label, value, minus, plus));
            s.Minus.Help = minusTip;
            s.Plus.Help = plusTip;
            ids.Add(id + "minus", s.Minus);
            ids.Add(id + "plus", s.Plus);
            return s;
        }

        // ---------------------------------------------------------------- the lane, the step and the editor: shown by RefreshSortie

        private void RefreshPlanParts(WmcContext c, WingPlan plan, WingPlans plans, WingService w, bool running, bool editable, int shown)
        {
            PlanRunner r = plans?.Runner;
            bool any = plan != null;
            sortieSection.SetCaption(any ? PlanWords.Bar(plan, running, plans.Completed) : c.Client ? "The host plans this mission." : "Wing Command is not ready.");
            Enable(execute, any && w != null && !running && Count(plan) > 0);
            Enable(abort, running);
            abort.Label = running && planGate.IsArmed("abort", Time.unscaledTime) ? "ABORT?" : "ABORT";
            Enable(save, any && w != null && Count(plan) > 0);
            Enable(plansButton, any && w != null && !running);
            int cur = running && r != null ? r.Current(selLane) : -1;
            Enable(skip, running && cur >= 0);

            bool lane = any && shown > 0;
            relayout |= laneGate.Set(lane);
            if (lane)
            {
                bool inUse = w != null && w.Roster.InUse(selLane);
                int count = inUse ? w.Roster.Count(selLane) : 0;
                selectLane.Latched = true;
                Enable(selectLane, count > 0);
                Enable(fitLane, WmcMap.Usable && (count > 0 || plan.Steps[selLane].Count > 0));
                Enable(formLane, c.CanOrder && (selLane == 0 || count > 0));
            }
            StepState cs = cur >= 0 ? r.State(selLane, cur) : StepState.Pending;
            laneHeld = running && cur >= 0 && cs == StepState.Held;
            laneBlocked = running && cur >= 0 && cs == StepState.Blocked;
            relayout |= runGate.Set(laneHeld || laneBlocked);
            Enable(resume, laneHeld);
            Enable(retry, laneBlocked);
            retry.Help = laneBlocked ? "Refused: " + (r.Why(selLane) ?? "") + ". Send it again." : RetryTip;

            bool picked = any && selStep >= 0 && selStep < plan.Steps[selLane].Count;
            SetStepRow(picked);
            editorGate.Set(picked);
            relayout = true;
            if (picked) RefreshEditor(plan.Steps[selLane][selStep], editable);
        }

        /// <summary>Shows or hides the selected-step line (AvRow has no gate of its own).</summary>
        private bool SetStepRow(bool on)
        {
            if (stepRow.Shown == on) return false;
            stepRow.SetShown(on);
            return true;
        }

        private void RefreshEditor(PlanStep p, bool editable)
        {
            int start = System.Array.IndexOf(Starts, p.Start);
            startValue = selStep > 0 && (p.Start == PlanStart.Now || p.Start == PlanStart.Exec) ? 1 : start;
            endValue = System.Array.IndexOf(Ends, p.End);
            startSeg.Refresh();
            endSeg.Refresh();
            editorSection.SetCaption(PlanRules.Name(selLane, selStep) + " · " + PlanWords.Kind(p.Kind));
            string why = editable ? null : "ABORT the plan to change it.";
            SegmentState(startSeg, StartTips, editable, why);
            SegmentState(endSeg, EndTips, editable, why);
            bool delay = p.Start == PlanStart.TPlus || p.Start == PlanStart.After;
            delayText = delay ? Clock(p.Delay) : WmcText.Unknown;
            delayStepper.Refresh();
            StepperState(delayStepper, editable && delay && p.Delay > 0f, editable && delay);
            bool time = p.End == PlanEnd.Time;
            timeText = time ? Clock(p.EndSeconds) : WmcText.Unknown;
            timeStepper.Refresh();
            StepperState(timeStepper, editable && time && p.EndSeconds > 30f, editable && time);
            Enable(up, editable && selStep > 0);
            Enable(down, editable && selStep < Plans.Plan.Steps[selLane].Count - 1);
            Enable(delete, editable);
            delete.Label = planGate.IsArmed("del" + selLane + "." + selStep, Time.unscaledTime) ? "DELETE?" : "DELETE";
        }

        private static void SegmentState(AvSegmented seg, string[] tips, bool on, string why)
        {
            AvControl[] options = seg.Options;
            for (int i = 0; i < options.Length; i++)
            {
                Enable(options[i], on);
                string tip = on ? tips[i] : why;
                if (!ReferenceEquals(options[i].Help, tip)) options[i].Help = tip;
            }
        }

        private static void StepperState(AvStepper s, bool minus, bool plus)
        {
            Enable(s.Minus, minus);
            Enable(s.Plus, plus);
        }

        private static int Count(WingPlan plan)
        {
            int n = 0;
            for (int l = 0; l < WingPlan.Lanes; l++) n += plan.Steps[l].Count;
            return n;
        }

        private static string LaneName(WingService w, int e) =>
            w != null && w.Roster.InUse(e) ? w.Roster.Name(e) : ElementRoster.Letter(e);

        /// <summary>Where lane <paramref name="e"/>'s element is and how fast it goes: its task lead when one flies, else its members'
        /// mean. False when it has none.</summary>
        internal static bool From(WingService w, int e, out float x0, out float z0, out float speed)
        {
            x0 = z0 = speed = 0f;
            if (w == null || !w.Roster.InUse(e)) return false;
            bool airborne = false;
            foreach (WingMember m in w.Members)
                if (!m.Released && m.Alive && (object)m.Aircraft != null && w.ElementOf(m) == e && !m.OnGround) airborne = true;
            WingPlanner p = w.PlannerOf(e);
            if (airborne && p != null && p.Active && p.Lead != null)
            {
                x0 = p.Lead.Position.X;
                z0 = p.Lead.Position.Z;
                speed = p.Lead.Speed;
                return true;
            }
            int n = 0;
            foreach (WingMember m in w.Members)
            {
                if (m.Released || !m.Alive || (object)m.Aircraft == null || w.ElementOf(m) != e) continue;
                x0 += m.Last.Pos.X;
                z0 += m.Last.Pos.Z;
                speed += m.Last.Vel.Length;
                n++;
            }
            if (n == 0) return false;
            x0 /= n;
            z0 /= n;
            speed /= n;
            // Still on the field: its legs are flown at cruise, not at taxi speed (review minor).
            if (!airborne) speed = GroundedCruise;
            return true;
        }

        /// <summary>The speed a planned leg from the field is timed at (m/s).</summary>
        public static float GroundedCruise = 150f;

        /// <summary>The plan itself was edited: after a run it shows as drawn, not the old run's states (review P2).</summary>
        private void Changed()
        {
            WingPlans plans = Plans;
            if (plans != null && !plans.Running && plans.Runner != null) plans.ForgetRun();
            Refreshed();
        }

        /// <summary>Something on the page changed (a pick, SAVE, EXECUTE/ABORT, SKIP): the run's states stay (review 2 [5]: SAVE, a
        /// click or ABORT itself used to forget the run just ended).</summary>
        private void Refreshed()
        {
            version++;
            sortieKey = long.MinValue;
            if (last != null) RefreshSortie(last);
            if (relayout)
            {
                relayout = false;
                flow.RequestRelayout();
            }
        }

        // ---------------------------------------------------------------- the bar

        /// <summary>PLANS ›: NEW, this theatre's saved plans (a pick loads a copy), DELETE for the one loaded.</summary>
        private void OpenPlans()
        {
            WingPlans plans = Plans;
            if (plans == null || plans.Running || popup == null) return;
            PlanStore store = WmcPlanFiles.Store;
            planIndices.Clear();
            planIndices.AddRange(store.For(WmcPlanFiles.Theatre));
            planEntries.Clear();
            planEntries.Add(new AvPopupEntry(planGate.IsArmed("new", Time.unscaledTime) ? "NEW? (again)" : "NEW PLAN",
                Count(plans.Plan) > 0 ? "drops this one (asks first)" : "an empty plan", false));
            int loaded = -1;
            foreach (int i in planIndices)
            {
                SavedPlan sp = store.Plans[i];
                if (sp.Name == plans.Plan.Name) loaded = i;
                planEntries.Add(new AvPopupEntry(sp.Name, AvNum.Fixed(Count(sp.Plan), 0) + " STEPS", sp.Name == plans.Plan.Name));
            }
            if (loaded >= 0)
                planEntries.Add(new AvPopupEntry(planGate.IsArmed("drop" + loaded, Time.unscaledTime) ? "DELETE? (again)" : "DELETE " + plans.Plan.Name,
                    "from the saved plans", false));
            popup.Show(WmcPopup.Area(flow.Content, plansButton.Rect, planEntries.Count), planEntries, PickPlan);
        }

        private void PickPlan(int k)
        {
            WingPlans plans = Plans;
            if (plans == null || plans.Running) return;
            if (k == 0)
            {
                NewPlan();
                return;
            }
            PlanStore store = WmcPlanFiles.Store;
            if (k - 1 < planIndices.Count)
            {
                WingPlan copy = store.Load(planIndices[k - 1]);
                if (copy == null) return;
                // Review minor: a drawn plan is not replaced without a second pick (as NEW asks).
                if (Count(plans.Plan) > 0 && !planGate.Press("load" + planIndices[k - 1], Time.unscaledTime))
                {
                    WingToast.Show("Pick " + copy.Name + " again to replace this plan");
                    Refreshed();
                    return;
                }
                plans.Load(copy);
                selLane = 0;
                selStep = -1;
                WingToast.Show("Loaded " + copy.Name + ": pick ATTACK targets again on ORDERS");
                Changed();
                return;
            }
            // DELETE the loaded plan (asks first).
            int index = -1;
            foreach (int i in planIndices)
                if (store.Plans[i].Name == plans.Plan.Name) index = i;
            if (index < 0) return;
            if (!planGate.Press("drop" + index, Time.unscaledTime))
            {
                WingToast.Show("DELETE again to drop " + store.Plans[index].Name);
                return;
            }
            string name = store.Plans[index].Name;
            store.Remove(index);
            WingToast.Show(WmcPlanFiles.Save() ? name + " deleted" : "Could not delete " + name + " from disk (see the log)");
        }

        /// <summary>SAVE: this plan for this theatre (a first save names it PLAN n).</summary>
        private void SavePlan()
        {
            WingPlans plans = Plans;
            if (plans == null) return;
            WingPlan plan = plans.Plan;
            if (plan.Name == "PLAN") plan.Name = "";
            if (!WmcPlanFiles.Store.Save(plan, WmcPlanFiles.Theatre, out string name))
            {
                if (plan.Name.Length == 0) plan.Name = "PLAN";
                WingToast.Show(AvNum.Fixed(PlanStore.Max, 0) + " plans saved already: delete one from PLANS ›");
                return;
            }
            plan.Name = name;
            WingToast.Show(WmcPlanFiles.Save() ? "Saved " + name : "Could not save " + name + " (see the log)");
            Refreshed();
        }

        private void NewPlan()
        {
            WingPlans plans = Plans;
            if (plans == null || plans.Running) return;
            if (!planGate.Press("new", Time.unscaledTime))
            {
                WingToast.Show("NEW again to drop this plan");
                Changed();
                return;
            }
            plans.Clear();
            selLane = 0;
            selStep = -1;
            Changed();
        }

        private void Execute()
        {
            WmcMotion.Punch(execute);
            WingPlans plans = Plans;
            if (plans == null || plans.Running) return;
            System.Collections.Generic.List<string> errors = plans.Execute();
            if (errors != null && errors.Count > 0)
                WingToast.Show("Cannot run: " + errors[0] + (errors.Count > 1 ? " (+" + AvNum.Fixed(errors.Count - 1, 0) + " more)" : ""));
            else WingToast.Show(plans.Plan.Name + " running");
            Refreshed();
        }

        private void Abort()
        {
            WingPlans plans = Plans;
            if (plans == null || !plans.Running) return;
            if (!planGate.Press("abort", Time.unscaledTime))
            {
                WingToast.Show("ABORT again to stop the plan");
                Refreshed();
                return;
            }
            plans.Abort();
            WingToast.Show(plans.Plan.Name + " aborted");
            Refreshed();
        }

        private void SkipStep() => LaneAct(selLane, 2);

        // ---------------------------------------------------------------- lanes and steps

        /// <summary>A timeline bar: its lane and step (step -1: the lane's letter).</summary>
        private void PickBar(int lane, int step)
        {
            if (step < 0)
            {
                selLane = lane;
                selStep = -1;
                Refreshed();
                return;
            }
            SelectStep(lane, step);
        }

        private void SelectStep(int lane, int step)
        {
            if (selLane == lane && selStep == step) selStep = -1;
            else
            {
                selLane = lane;
                selStep = step;
            }
            Refreshed();
        }

        private void LaneAct(int lane, int act)
        {
            PlanRunner r = Plans?.Runner;
            if (r == null || !r.Running || lane < 0 || lane >= WingPlan.Lanes) return;
            if (act == 0) r.Resume(lane);
            else if (act == 1) r.Retry(lane);
            else r.Skip(lane, WingService.Instance?.MissionTime ?? 0f);
            Refreshed();
        }

        private void SelectElement(int e)
        {
            WmcContext c = last;
            if (c == null) return;
            selLane = e;
            selStep = -1;
            elementIds.Clear();
            for (int i = 0; i < c.Count; i++)
                if (c.Rows[i].Element == e) elementIds.Add(c.Rows[i].Id);
            if (elementIds.Count > 0)
            {
                c.Selection.SelectElement(e, elementIds);
                c.Rescope();
            }
            Refreshed();
            WmcPanel.Instance?.Refresh();
        }

        private void FormElement(int e) =>
            WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.FormUp, e == 0 ? WingScope.Wing : WingScope.OfElement(e))));

        /// <summary>The element's members, its task legs and its plan, framed on the map.</summary>
        private void FitElement(int e)
        {
            WmcContext c = last;
            if (c?.Wing == null || c.Client) return;
            var box = new MapBox();
            foreach (WingMember m in c.Wing.Members)
                if (!m.Released && m.Alive && (object)m.Aircraft != null && c.Wing.ElementOf(m) == e) box.Add(m.Last.Pos.X, m.Last.Pos.Z);
            WingPlanner p = c.Wing.PlannerOf(e);
            if (p != null && p.Active)
            {
                cardLegs.Clear();
                cardRings.Clear();
                RouteView.Task(p.Current, p.Leg, p.Lead.Position, p.Lead.Speed, cardLegs, cardRings);
                foreach (RouteLeg l in cardLegs) box.Add(l.ToX, l.ToZ);
                foreach (RouteRing r in cardRings)
                {
                    box.Add(r.X - r.Radius, r.Z - r.Radius);
                    box.Add(r.X + r.Radius, r.Z + r.Radius);
                }
            }
            WingPlan plan = Plans?.Plan;
            if (plan != null)
                foreach (PlanStep s in plan.Steps[e])
                {
                    if (s.Points == null) continue;
                    foreach (Waypoint w in s.Points) box.Add(w.X, w.Z);
                    if (s.Radius > 0f && s.Points.Length > 0)
                    {
                        box.Add(s.Points[0].X - s.Radius, s.Points[0].Z - s.Radius);
                        box.Add(s.Points[0].X + s.Radius, s.Points[0].Z + s.Radius);
                    }
                }
            WmcMap.Fit(box);
        }

        // ---------------------------------------------------------------- the step editor

        private PlanStep Selected()
        {
            WingPlans plans = Plans;
            if (plans == null || plans.Running || selStep < 0 || selStep >= plans.Plan.Steps[selLane].Count) return null;
            return plans.Plan.Steps[selLane][selStep];
        }

        private void PickStart(int k)
        {
            PlanStep p = Selected();
            if (p == null) return;
            if (Starts[k] == PlanStart.After)
            {
                if (!PlanEdit.NextAfter(Plans.Plan, selLane, selStep)) WingToast.Show("No other lane's step it could wait for");
            }
            else
            {
                p.Start = Starts[k];
                p.AfterLane = p.AfterStep = -1;
                if (p.Start != PlanStart.TPlus) p.Delay = 0f;
            }
            Changed();
        }

        private void PickEnd(int k)
        {
            PlanStep p = Selected();
            if (p == null) return;
            p.End = Ends[k];
            if (p.End == PlanEnd.Time && p.EndSeconds <= 0f) p.EndSeconds = PlanEdit.OrbitSeconds;
            Changed();
        }

        private void StepDelay(int dir)
        {
            PlanStep p = Selected();
            if (p == null) return;
            p.Delay = Mathf.Clamp(p.Delay + dir * 15f, 0f, 3600f);
            Changed();
        }

        private void StepTime(int dir)
        {
            PlanStep p = Selected();
            if (p == null) return;
            p.EndSeconds = Mathf.Clamp(p.EndSeconds + dir * 30f, 30f, 7200f);
            Changed();
        }

        private void MoveSelected(int dir)
        {
            if (Selected() == null) return;
            if (PlanEdit.MoveStep(Plans.Plan, selLane, selStep, dir)) selStep += dir;
            Changed();
        }

        private void DeleteSelected()
        {
            if (Selected() == null) return;
            if (!planGate.Press("del" + selLane + "." + selStep, Time.unscaledTime))
            {
                Changed();
                return;
            }
            PlanEdit.Remove(Plans.Plan, selLane, selStep);
            selStep = Mathf.Min(selStep, Plans.Plan.Steps[selLane].Count - 1);
            Changed();
        }
    }
}
