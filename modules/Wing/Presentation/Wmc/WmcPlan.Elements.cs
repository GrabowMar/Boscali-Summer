using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>PLAN › ELEMENTS, the plan editor on the map (spec bezel v2 §5 full version, §6), as a kit v2 flow: the plan bar (PLANS ›,
    /// SAVE, EXECUTE / ABORT), the map tools, a cue that says what the armed tool wants or what the selected step does, the step editor
    /// while a step is picked, then a lane card per element — its head (state), SELECT · FIT · FORM UP, RESUME · RETRY · SKIP while the
    /// plan runs, its steps (an <see cref="AvList"/>: START · KIND · detail · END · state) and + RTB · + REFIT · + FORM UP. A tool's
    /// right-click inserts a step after the selected step of the selected lane. While a plan runs it is read-only. The step editor
    /// sits above the lanes (the flow has no line that moves under a picked row).</summary>
    internal sealed partial class WmcPlan
    {
        private static readonly PlanTool[] ToolOrder =
        {
            PlanTool.Move, PlanTool.Route, PlanTool.Orbit, PlanTool.Cap, PlanTool.Sweep,
            PlanTool.Attack, PlanTool.Land, PlanTool.Cargo, PlanTool.Replace,
        };
        private static readonly string[] ToolLabels = { "MOVE", "ROUTE", "ORBIT", "CAP", "SWEEP", "ATTACK", "LAND", "CARGO", "RE-PLACE" };
        private static readonly string[] ToolIds = { "move", "route", "orbit", "cap", "sweep", "attack", "land", "cargo", "replace" };
        private static readonly Glyph[] ToolGlyphs =
        {
            Glyph.Move, Glyph.Route, Glyph.Orbit, Glyph.Cap, Glyph.Sweep, Glyph.Attack, Glyph.Land, Glyph.Cargo, Glyph.Move,
        };
        private static readonly string[] ToolTips =
        {
            "A step to fly to a point.", "A step to fly a route: right-click its points, DONE ends it.", "A step to orbit a point.",
            "A step to guard an area: right-press the centre and drag the radius.", "A step to sweep an area: right-press and drag.",
            "A step to attack enemies: right-click one, SHIFT adds more.",
            "A step to land at a point (helicopters).", "A step to deliver cargo at a point (helicopters).",
            "Put the selected step's point, area or targets somewhere else.",
        };
        private const int ReplaceTool = 8;
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

        private sealed class LaneView
        {
            public AvCard Card;
            public WmcCollapsible Gate, RunGate;
            public AvRow Head;
            public AvControl Select, Fit, Form, Resume, Retry, Skip;
            public AvList Steps;
            public bool Held, Blocked, Skippable;
            public readonly string[] Name = new string[WingPlan.MaxSteps], Sub = new string[WingPlan.MaxSteps], Value = new string[WingPlan.MaxSteps];
            public readonly AvState[] State = new AvState[WingPlan.MaxSteps];
        }

        private readonly LaneView[] lanes = new LaneView[WingPlan.Lanes];
        private readonly List<uint> elementIds = new List<uint>();
        private readonly ConfirmGate planGate = new ConfirmGate();
        private readonly List<RouteLeg> cardLegs = new List<RouteLeg>();
        private readonly List<RouteRing> cardRings = new List<RouteRing>();
        private readonly List<AvPopupEntry> planEntries = new List<AvPopupEntry>(PlanStore.Max + 2);
        private readonly List<int> planIndices = new List<int>(PlanStore.Max);
        private readonly AvControl[] tools = new AvControl[ToolOrder.Length];
        private AvSection planSection, elementsSection, editorSection;
        private WmcLines cueLines;
        private WmcCollapsible cueArea, cueButtons, editorGate;
        private AvStepper cueStepper, delayStepper, timeStepper, altStepper, radiusStepper;
        private AvSegmented startSeg, endSeg;
        private AvControl execute, save, plansButton, cueDone, addElement, replace, up, down, delete;
        private string cueRadiusText = "", delayText = "", timeText = "", altText = "", radiusText = "";
        private int startValue = -1, endValue = -1;
        private int selLane, selStep = -1, lanesShown, version;
        private long elementsKey = long.MinValue;
        private float toolRadius;
        private string toolWhy;
        private bool routeOpen;

        /// <summary>Lanes showing now (automation; was the element cards).</summary>
        public int Cards => lanesShown;
        public int SelectedLane => selLane;
        public int SelectedStep => selStep;

        private void BuildElements(AvFlow f, AvTicker t, int pageIndex)
        {
            planSection = f.Section(AvIcon.ListDetails, "PLAN", "");
            AvControl[] bar = f.Buttons(
                new AvControl.Spec("PLANS ›", OpenPlans, AvButtonStyle.Default, AvIcon.ListDetails),
                new AvControl.Spec("SAVE", SavePlan, AvButtonStyle.Default, AvIcon.Bookmark),
                new AvControl.Spec("EXECUTE", ExecuteOrAbort, AvButtonStyle.Primary, AvIcon.PlayerPlay)).Controls;
            plansButton = bar[0];
            save = bar[1];
            execute = bar[2];
            plansButton.Help = "This theatre's saved plans, NEW and DELETE.";
            save.Help = "Keep this plan for this theatre (targets are picked again after loading).";
            execute.Help = "Run the plan: every lane's steps in turn. While it runs: ABORT (asks first).";
            ids.Add("plan.bar.plans", plansButton);
            ids.Add("plan.bar.save", save);
            ids.Add("plan.bar.execute", execute);

            f.Section(AvIcon.Pencil, "TOOLS");
            for (int row = 0; row < 2; row++)
            {
                int first = row * 5, count = row == 0 ? 5 : ToolOrder.Length - 5;
                var specs = new AvControl.Spec[count];
                for (int i = 0; i < count; i++)
                {
                    int k = first + i;
                    specs[i] = new AvControl.Spec(ToolLabels[k], () => PressTool(k), AvButtonStyle.Default, WmcIcons.Of(ToolGlyphs[k]));
                }
                AvControl[] made = f.Buttons(specs).Controls;
                for (int i = 0; i < count; i++)
                {
                    tools[first + i] = made[i];
                    made[i].Help = ToolTips[first + i];
                    ids.Add("plan.tool." + ToolIds[first + i], made[i]);
                }
            }

            // The cue: what the armed tool wants or what the picked step does, the tool's area, and DONE · CANCEL while a tool is armed.
            cueLines = f.Add(new WmcLines(f.Content, 1));
            cueStepper = new AvStepper(f.Content, "AREA", () => cueRadiusText, () => StepToolRadius(-1), () => StepToolRadius(1));
            cueStepper.Minus.Help = "A smaller area (a right-drag sets it too).";
            cueStepper.Plus.Help = "A larger area.";
            cueArea = f.Add(new WmcCollapsible(cueStepper));
            var cueBar = new AvButtons(f.Content, new[]
            {
                new AvControl.Spec("DONE", EndRoute, AvButtonStyle.Primary, AvIcon.CircleCheck),
                new AvControl.Spec("PUT DOWN", () => last?.Map.Disarm(), AvButtonStyle.Quiet, AvIcon.X),
            });
            cueDone = cueBar.Controls[0];
            cueDone.Help = "The route is complete.";
            cueBar.Controls[1].Help = "Put the tool down (Esc).";
            cueButtons = f.Add(new WmcCollapsible(cueBar));
            ids.Add("plan.cue.minus", () => StepToolRadius(-1), () => cueArea.Shown);
            ids.Add("plan.cue.plus", () => StepToolRadius(1), () => cueArea.Shown);
            ids.Add("plan.cue.done", EndRoute, () => cueButtons.Shown && last != null && last.Map.Tool == PlanTool.Route);
            ids.Add("plan.cue.cancel", () => last?.Map.Disarm(), () => cueButtons.Shown);

            BuildEditor(f, t);

            elementsSection = f.Section(AvIcon.LayersSubtract, "ELEMENTS", "");
            for (int e = 0; e < lanes.Length; e++) lanes[e] = BuildLane(f, t, e);
            addElement = f.Buttons(new AvControl.Spec("+ ELEMENT FROM SELECTION", AddElement, AvButtonStyle.Default, AvIcon.Plus)).Controls[0];
            addElement.Help = "The selected aircraft become an element of their own (orbiting where they are), with a lane to plan.";
            ids.Add("plan.add.element", addElement);

            cueArea.Set(false);
            cueButtons.Set(false);
            last = null;
        }

        private LaneView BuildLane(AvFlow f, AvTicker t, int e)
        {
            var v = new LaneView();
            v.Card = new AvCard(f.Content, t, f.Inner, null, true);
            v.Gate = f.Add(new WmcCollapsible(v.Card));
            AvFlow lf = v.Card.Flow;
            int k = e;
            v.Head = lf.Add(new AvRow(lf.Content));
            v.Head.Set(ElementRoster.Letter(e), "", null, AvState.Info);
            AvControl[] main = lf.Buttons(
                new AvControl.Spec("SELECT", () => SelectElement(k), AvButtonStyle.Default, AvIcon.Target),
                new AvControl.Spec("FIT", () => FitElement(k), AvButtonStyle.Default, AvIcon.Focus2),
                new AvControl.Spec("FORM UP", () => FormElement(k), AvButtonStyle.Default, AvIcon.UsersGroup)).Controls;
            v.Select = main[0];
            v.Fit = main[1];
            v.Form = main[2];
            v.Select.Help = "Orders go to this element, and tools add to its lane.";
            v.Fit.Help = "Frame this element and its plan on the map.";
            v.Form.Help = e == 0 ? "The wing forms up on you." : "The element rejoins A.";
            ids.Add("plan.el" + AvNum.Fixed(e, 0) + ".select", v.Select);
            ids.Add("plan.el" + AvNum.Fixed(e, 0) + ".fit", v.Fit);
            ids.Add("plan.el" + AvNum.Fixed(e, 0) + ".form", v.Form);

            var run = new AvButtons(lf.Content, new[]
            {
                new AvControl.Spec("RESUME", () => LaneAct(k, 0), AvButtonStyle.Default, AvIcon.PlayerPlay),
                new AvControl.Spec("RETRY", () => LaneAct(k, 1), AvButtonStyle.Default, AvIcon.Refresh),
                new AvControl.Spec("SKIP", () => LaneAct(k, 2), AvButtonStyle.Default, AvIcon.ArrowRight),
            });
            v.RunGate = lf.Add(new WmcCollapsible(run));
            v.Resume = run.Controls[0];
            v.Retry = run.Controls[1];
            v.Skip = run.Controls[2];
            v.Resume.Help = "The lane was held by your order: send its step again.";
            v.Retry.Help = RetryTip;
            v.Skip.Help = "Pass over this lane's step; what waits for it goes on.";
            string laneId = "plan.el" + AvNum.Fixed(e, 0);
            ids.Add(laneId + ".resume", () => LaneAct(k, 0), () => v.RunGate.Shown && v.Held);
            ids.Add(laneId + ".retry", () => LaneAct(k, 1), () => v.RunGate.Shown && v.Blocked);
            ids.Add(laneId + ".skip", () => LaneAct(k, 2), () => v.RunGate.Shown && v.Skippable);
            v.RunGate.Set(false);

            v.Steps = lf.Add(new AvList(lf.Content, t, WingPlan.MaxSteps, (item, row) => BindStep(k, item, row)));
            v.Steps.RowClicked = item => SelectStep(k, item);

            string[] adds = { "+ RTB", "+ REFIT", "+ FORM UP" };
            string[] addIds = { "rtb", "refit", "formup" };
            string[] addTips =
            {
                "A step to go home and land.", "A step to land, rearm, refuel and come back.", "A step to form up (A on you; the others rejoin A).",
            };
            PlanKind[] kinds = { PlanKind.Rtb, PlanKind.Refit, PlanKind.FormUp };
            var addSpecs = new AvControl.Spec[3];
            for (int i = 0; i < 3; i++)
            {
                PlanKind kind = kinds[i];
                addSpecs[i] = new AvControl.Spec(adds[i], () => AddStep(k, kind), AvButtonStyle.Quiet);
            }
            AvControl[] addControls = lf.Buttons(addSpecs).Controls;
            for (int i = 0; i < 3; i++)
            {
                addControls[i].Help = addTips[i];
                ids.Add(laneId + "." + addIds[i], addControls[i]);
            }
            v.Gate.Set(false);
            return v;
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
            altStepper = Stepper(ef, "ALTITUDE", () => altText, () => StepAlt(-1), () => StepAlt(1), "plan.edit.alt.",
                "Lower (below 500 m: the task's own).", "Higher.");
            radiusStepper = Stepper(ef, "RADIUS", () => radiusText, () => StepRadius(-1), () => StepRadius(1), "plan.edit.r.",
                "A smaller area.", "A larger area.");
            AvControl[] verbs = ef.Buttons(
                new AvControl.Spec("RE-PLACE", () => PressTool(ReplaceTool), AvButtonStyle.Default, WmcIcons.Of(Glyph.Move)),
                new AvControl.Spec("UP", () => MoveSelected(-1), AvButtonStyle.Default, AvIcon.ChevronUp),
                new AvControl.Spec("DOWN", () => MoveSelected(1), AvButtonStyle.Default, AvIcon.ChevronDown),
                new AvControl.Spec("DELETE", DeleteSelected, AvButtonStyle.Danger, AvIcon.X)).Controls;
            replace = verbs[0];
            up = verbs[1];
            down = verbs[2];
            delete = verbs[3];
            replace.Help = "Right-click the step's new point, area or target.";
            up.Help = "Fly this step earlier in its lane.";
            down.Help = "Fly this step later in its lane.";
            delete.Help = "Remove this step (asks first); what waited for it goes on EXECUTE.";
            ids.Add("plan.edit.replace", replace);
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

        private static WingPlans Plans => WingPlans.Instance;

        /// <summary>A step row of a lane's list: what <see cref="RefreshElements"/> worked out for it.</summary>
        private void BindStep(int lane, int item, AvRow row)
        {
            LaneView v = lanes[lane];
            row.Set(v.Name[item], v.Sub[item], v.Value[item], v.State[item]);
            row.Armed = lane == selLane && item == selStep;
            ids.Add("plan.step" + AvNum.Fixed(lane, 0) + "." + AvNum.Fixed(item + 1, 0), row);
            row.Help = "Select this step: the editor opens above the lanes.";
        }

        private void RefreshElements(WmcContext c)
        {
            WingPlans plans = Plans;
            WingService w = c.Client ? null : c.Wing;
            if (c.Map.PlanClick == null) c.Map.PlanClick = OnPlanClick;
            WingPlan plan = plans?.Plan;
            bool running = plans != null && plans.Running, editable = plan != null && !running && w != null;
            PlanRunner r = plans?.Runner;
            if (plan != null && (selLane < 0 || selLane >= WingPlan.Lanes)) selLane = 0;
            if (plan != null && selStep >= plan.Steps[selLane].Count) selStep = -1;
            c.Map.ToolLane = LaneName(w, selLane);

            // The key: plan edits, runner states, selection, tool, where the elements are (to the 100 m) and the dock.
            long key = version * 7919L + (running ? 1 : 0) + (plans != null && plans.Completed ? 2 : 0) + selLane * 13L + selStep * 131L
                       + (long)c.Map.Tool * 1543L + (long)toolRadius + c.Count * 17L + c.Selection.Count * 257L;
            for (int e = 0; e < WingPlan.Lanes && plan != null; e++)
            {
                key = key * 31L + (w != null && w.Roster.InUse(e) ? 1 : 0);
                if (r != null)
                    for (int s = 0; s < plan.Steps[e].Count; s++) key = key * 7L + (int)r.State(e, s);
                if (From(w, e, out float fx, out float fz, out _)) key = key * 31L + (long)(fx / 100f) * 3L + (long)(fz / 100f);
            }
            key = key * 7L + (planGate.IsArmed("new", Time.unscaledTime) ? 1 : 0) + (planGate.IsArmed("abort", Time.unscaledTime) ? 2 : 0)
                  + (planGate.IsArmed("del" + selLane + "." + selStep, Time.unscaledTime) ? 4 : 0);
            RefreshTools(c, editable, running);
            if (key == elementsKey) return;
            elementsKey = key;
            relayout = true;

            if (plan == null)
            {
                planSection.SetCaption(c.Client ? "The host plans this mission." : "Wing Command is not ready.");
                return;
            }
            planSection.SetCaption(PlanWords.Bar(plan, running, plans.Completed));
            execute.Label = running ? (planGate.IsArmed("abort", Time.unscaledTime) ? "ABORT?" : "ABORT") : "EXECUTE";
            Enable(execute, w != null && (running || Count(plan) > 0));
            Enable(save, w != null && Count(plan) > 0);
            Enable(plansButton, w != null && !running);
            RefreshCue(c, plan, running);

            int shown = 0;
            for (int e = 0; e < lanes.Length; e++)
            {
                LaneView v = lanes[e];
                bool inUse = w != null && w.Roster.InUse(e);
                bool on = w != null && (e == 0 || inUse || plan.Steps[e].Count > 0);
                v.Gate.Set(on);
                if (!on)
                {
                    v.Steps.SetCount(0);
                    continue;
                }
                shown++;
                int count = inUse ? w.Roster.Count(e) : 0;
                string state = LaneState(plan, r, e, inUse);
                int cur = r != null ? r.Current(e) : -1;
                StepState cs = cur >= 0 ? r.State(e, cur) : StepState.Pending;
                bool held = running && cur >= 0 && cs == StepState.Held, blocked = running && cur >= 0 && cs == StepState.Blocked;
                bool live = running && cur >= 0 && cs == StepState.Running;
                v.Head.Set(ElementRoster.Letter(e) + " " + (inUse ? w.Roster.Name(e) : "NOT FORMED"),
                    AvNum.Fixed(count, 0) + " AC · " + state, null, live ? AvState.Ready : AvState.Info);
                v.Held = held;
                v.Blocked = blocked;
                v.Skippable = running && cur >= 0;
                v.RunGate.Set(v.Skippable);
                Enable(v.Resume, held);
                Enable(v.Retry, blocked);
                Enable(v.Skip, v.Skippable);
                v.Retry.Help = blocked ? "Refused: " + (r.Why(e) ?? "") + ". Send it again." : RetryTip;
                v.Select.Latched = e == selLane;
                Enable(v.Select, count > 0);
                Enable(v.Fit, WmcMap.Usable && (count > 0 || plan.Steps[e].Count > 0));
                Enable(v.Form, c.CanOrder && (e == 0 || count > 0));

                From(w, e, out float x0, out float z0, out float speed);
                int n = plan.Steps[e].Count;
                for (int i = 0; i < n; i++)
                {
                    PlanStep p = plan.Steps[e][i];
                    string st = PlanWords.State(running || (r != null && plans.Completed) ? r : null, e, i);
                    v.Name[i] = AvNum.Fixed(i + 1, 0) + " " + PlanWords.Kind(p.Kind) + " · " + PlanWords.Start(plan, e, i);
                    string detail = PlanWords.Detail(p, x0, z0, speed);
                    v.Sub[i] = (detail.Length > 0 ? detail + " · " : "") + PlanWords.End(p);
                    v.Value[i] = st;
                    v.State[i] = st == "RUN" ? AvState.Ready : st == "HELD" || st == "BLOCKED" ? AvState.Caution : AvState.Info;
                    if (PlanEdit.EndPoint(p, out float ex, out float ez))
                    {
                        x0 = ex;
                        z0 = ez;
                    }
                }
                v.Steps.SetCount(n);
            }
            bool editing = selStep >= 0 && plan.Steps[selLane].Count > selStep;
            editorGate.Set(editing);
            if (editing) RefreshEditor(plan.Steps[selLane][selStep], editable);
            Enable(addElement, editable && c.Selection.Count > 0);
            lanesShown = shown;
            elementsSection.SetCaption(AvNum.Fixed(shown, 0) + " ACTIVE");
        }

        private void RefreshTools(WmcContext c, bool editable, bool running)
        {
            for (int i = 0; i < tools.Length; i++)
            {
                tools[i].Latched = ToolOrder[i] != PlanTool.Off && c.Map.Tool == ToolOrder[i];
                Enable(tools[i], editable && (ToolOrder[i] != PlanTool.Replace || selStep >= 0));
            }
            string why = running ? "ABORT the plan to change it." : c.Client ? "The host plans this mission." : null;
            if (why == toolWhy) return;
            toolWhy = why;
            for (int i = 0; i < tools.Length; i++) tools[i].Help = why ?? ToolTips[i];
        }

        private void RefreshCue(WmcContext c, WingPlan plan, bool running)
        {
            PlanTool tool = c.Map.Tool;
            bool area = tool == PlanTool.Cap || tool == PlanTool.Sweep;
            if (area && toolRadius <= 0f) toolRadius = tool == PlanTool.Cap ? AreaGuard.CapRadius : AreaGuard.SweepRadius;
            cueArea.Set(area);
            cueButtons.Set(tool != PlanTool.Off);
            Enable(cueDone, tool == PlanTool.Route);
            if (area)
            {
                cueRadiusText = Km(toolRadius);
                cueStepper.Refresh();
            }
            string cue = tool != PlanTool.Off ? PlanWords.ToolCue(tool, LaneName(c.Client ? null : c.Wing, selLane))
                : selStep >= 0 && selStep < plan.Steps[selLane].Count ? PlanWords.Cue(plan, selLane, selStep)
                : running ? "The plan runs: a lane you order yourself holds until RESUME."
                : "Pick a tool, then right-click the map: steps go into the selected lane.";
            cueLines.Set(0, cue);
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
            bool points = p.Points != null && p.Points.Length > 0 && p.Kind != PlanKind.Attack;
            float alt = points ? p.Points[0].Altitude : float.NaN;
            altText = !points ? WmcText.Unknown : float.IsNaN(alt) ? "AUTO" : AvNum.Thousands(alt) + " M";
            altStepper.Refresh();
            StepperState(altStepper, editable && points && !float.IsNaN(alt), editable && points);
            bool area = p.Kind == PlanKind.Cap || p.Kind == PlanKind.Sweep;
            radiusText = area ? Km(p.Radius) : WmcText.Unknown;
            radiusStepper.Refresh();
            StepperState(radiusStepper, editable && area && p.Radius > AreaGuard.MinRadius, editable && area && p.Radius < AreaGuard.MaxRadius);
            Enable(replace, editable && (points || p.Kind == PlanKind.Attack));
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

        private static string Km(float metres)
        {
            float km = metres / 1000f;
            return AvNum.Fixed(km, Mathf.Abs(km - Mathf.Round(km)) < 0.05f ? 0 : 1) + " KM";
        }

        private static int Count(WingPlan plan)
        {
            int n = 0;
            for (int l = 0; l < WingPlan.Lanes; l++) n += plan.Steps[l].Count;
            return n;
        }

        private static string LaneName(WingService w, int e) =>
            w != null && w.Roster.InUse(e) ? w.Roster.Name(e) : ElementRoster.Letter(e);

        private static string LaneState(WingPlan plan, PlanRunner r, int e, bool inUse)
        {
            int n = plan.Steps[e].Count;
            if (n == 0) return "NO STEPS";
            if (r == null || (!r.Running && !(Plans?.Completed ?? false))) return "WAIT EXEC";
            int cur = r.Current(e);
            return cur < 0 ? "THROUGH" : PlanRules.Name(e, cur) + " " + PlanWords.State(r, e, cur);
        }

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

        /// <summary>Something on the page changed (a pick, a tool, SAVE, EXECUTE/ABORT, SKIP): the run's states stay (review 2 [5]:
        /// SAVE, a click or ABORT itself used to forget the run just ended).</summary>
        private void Refreshed()
        {
            version++;
            elementsKey = long.MinValue;
            if (last != null) RefreshElements(last);
            if (relayout)
            {
                relayout = false;
                flow.RequestRelayout();
            }
        }

        // ---- The bar, tools and cue ----

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
                routeOpen = false;
                last?.Map.Disarm();
                WingToast.Show("Loaded " + copy.Name + ": pick ATTACK targets again with RE-PLACE");
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
            routeOpen = false;
            last?.Map.Disarm();
            Changed();
        }

        private void ExecuteOrAbort()
        {
            WmcMotion.Punch(execute);
            WingPlans plans = Plans;
            if (plans == null) return;
            if (plans.Running)
            {
                if (!planGate.Press("abort", Time.unscaledTime))
                {
                    WingToast.Show("ABORT again to stop the plan");
                    Refreshed();
                    return;
                }
                plans.Abort();
                WingToast.Show(plans.Plan.Name + " aborted");
                Refreshed();
                return;
            }
            last?.Map.Disarm();
            routeOpen = false;
            List<string> errors = plans.Execute();
            if (errors != null && errors.Count > 0)
                WingToast.Show("Cannot run: " + errors[0] + (errors.Count > 1 ? " (+" + AvNum.Fixed(errors.Count - 1, 0) + " more)" : ""));
            else WingToast.Show(plans.Plan.Name + " running");
            Refreshed();
        }

        private void PressTool(int i)
        {
            WmcContext c = last;
            if (i < 0 || i >= ToolOrder.Length) return;
            PlanTool tool = ToolOrder[i];
            if (c == null || tool == PlanTool.Off) return;
            if (c.Map.Tool == tool)
            {
                c.Map.Disarm();
                routeOpen = false;
                Refreshed();
                return;
            }
            if (tool == PlanTool.Replace && selStep < 0)
            {
                WingToast.Show("Select a step first");
                return;
            }
            if (tool == PlanTool.Cap || tool == PlanTool.Sweep) toolRadius = tool == PlanTool.Cap ? AreaGuard.CapRadius : AreaGuard.SweepRadius;
            c.Map.ToolLane = LaneName(c.Client ? null : c.Wing, selLane);
            routeOpen = false;
            c.Map.ArmTool(c, tool);
            Refreshed();
        }

        private void StepToolRadius(int dir)
        {
            toolRadius = AreaGuard.Clamp(toolRadius + dir * 1000f);
            Refreshed();
        }

        private void EndRoute()
        {
            routeOpen = false;
            last?.Map.Disarm();
            Changed();
        }

        /// <summary>A tool's right-click (spec bezel v2 §6): a step after the selected one in the selected lane; ROUTE adds points to
        /// the route it started, ATTACK with SHIFT adds a target, RE-PLACE moves the selected step.</summary>
        private void OnPlanClick(WmcContext c, GlobalPosition point, Unit unit, bool shift, float radius)
        {
            WingPlans plans = Plans;
            if (plans == null || c == null || c.Client) return;
            if (plans.Running)
            {
                WingToast.Show("ABORT the plan to change it");
                return;
            }
            WingPlan plan = plans.Plan;
            PlanTool tool = c.Map.Tool;
            bool enemy = unit != null && !unit.disabled && DynamicMap.GetFactionMode(unit.NetworkHQ, false) == FactionMode.Enemy;
            PlanStep selected = selStep >= 0 && selStep < plan.Steps[selLane].Count ? plan.Steps[selLane][selStep] : null;
            switch (tool)
            {
                case PlanTool.Replace:
                    if (selected == null) return;
                    if (selected.Kind == PlanKind.Attack)
                    {
                        if (!enemy)
                        {
                            WingToast.Show("Right-click an enemy on the map");
                            return;
                        }
                        selected.Targets = new[] { unit.persistentID.Id };
                    }
                    else
                    {
                        PlanEdit.Replace(selected, point.x, point.z);
                        if (radius > 0f && (selected.Kind == PlanKind.Cap || selected.Kind == PlanKind.Sweep)) selected.Radius = radius;
                    }
                    // Review P2: a route re-placed starts again from its first point; the next right-clicks add the rest.
                    if (selected.Kind == PlanKind.Route && c.Map.ArmTool(c, PlanTool.Route))
                    {
                        routeOpen = true;
                        WingToast.Show("Right-click the route's next points; DONE ends it");
                    }
                    else c.Map.Disarm();
                    break;
                case PlanTool.Attack:
                    if (!enemy)
                    {
                        WingToast.Show("Right-click an enemy on the map");
                        return;
                    }
                    if (shift && selected != null && selected.Kind == PlanKind.Attack)
                    {
                        if (!PlanEdit.AddTarget(selected, unit.persistentID.Id)) WingToast.Show("That target is in, or the step is full");
                        break;
                    }
                    Insert(plan, PlanEdit.Attack(unit.persistentID.Id));
                    break;
                case PlanTool.Route:
                    if (routeOpen && selected != null && selected.Kind == PlanKind.Route)
                    {
                        if (!PlanEdit.AddPoint(selected, point.x, point.z)) WingToast.Show("The route is full (16 points)");
                        break;
                    }
                    routeOpen = Insert(plan, PlanEdit.NewStep(PlanTool.Route, point.x, point.z, float.NaN, 0f));
                    break;
                default:
                    Insert(plan, PlanEdit.NewStep(tool, point.x, point.z, float.NaN, radius > 0f ? radius : toolRadius));
                    break;
            }
            WmcPanel.Instance?.Overlay.Ping(point);
            Changed();
        }

        private bool Insert(WingPlan plan, PlanStep step)
        {
            if (step == null) return false;
            int at = PlanEdit.Insert(plan, selLane, selStep, step);
            if (at < 0)
            {
                WingToast.Show("Lane " + ElementRoster.Letter(selLane) + " is full (" + AvNum.Fixed(WingPlan.MaxSteps, 0) + " steps)");
                return false;
            }
            selStep = at;
            return true;
        }

        // ---- Lanes and steps ----

        private void SelectStep(int lane, int step)
        {
            if (selLane == lane && selStep == step) selStep = -1;
            else
            {
                selLane = lane;
                selStep = step;
            }
            routeOpen = false;
            if (last != null && last.Map.Tool == PlanTool.Replace) last.Map.Disarm();
            Refreshed();
        }

        private void AddStep(int lane, PlanKind kind)
        {
            WingPlans plans = Plans;
            if (plans == null || plans.Running || last == null || last.Client) return;
            if (selLane != lane)
            {
                selLane = lane;
                selStep = plans.Plan.Steps[lane].Count - 1;
            }
            Insert(plans.Plan, new PlanStep { Kind = kind });
            Changed();
        }

        private void LaneAct(int lane, int act)
        {
            PlanRunner r = Plans?.Runner;
            if (r == null || !r.Running) return;
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
            routeOpen = false;
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

        /// <summary>The selection becomes an element of its own, orbiting where it is (the grid's DETACH), so it has a lane.</summary>
        private void AddElement()
        {
            WmcContext c = last;
            if (c == null || c.Selection.Count == 0) return;
            Vec3 at = WmcMapInput.From(c, out _);
            WmcUi.Order(c, () =>
            {
                OrderResult r = WingOrders.Run(WingOrder.Tasked(WingTask.Orbit(Waypoint.At(at.X, at.Z)), c.Scope));
                if (r.Accepted && r.Element >= 0)
                {
                    selLane = r.Element;
                    selStep = -1;
                }
            });
            Changed();
        }

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

        // ---- The step editor ----

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

        private void StepAlt(int dir)
        {
            PlanStep p = Selected();
            if (p?.Points == null || p.Points.Length == 0) return;
            float a = p.Points[0].Altitude;
            a = float.IsNaN(a) ? (dir > 0 ? 1000f : float.NaN) : a + dir * 500f;
            if (!float.IsNaN(a) && a < 500f) a = float.NaN;
            if (!float.IsNaN(a)) a = Mathf.Min(a, 12000f);
            for (int i = 0; i < p.Points.Length; i++) p.Points[i].Altitude = a;
            Changed();
        }

        private void StepRadius(int dir)
        {
            PlanStep p = Selected();
            if (p == null) return;
            p.Radius = AreaGuard.Clamp(p.Radius + dir * 1000f);
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
            if (last != null && last.Map.Tool == PlanTool.Replace) last.Map.Disarm();
            Changed();
        }
    }
}
