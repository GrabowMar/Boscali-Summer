using NOAvionics;
using System.Text;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Core.Math;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL › ORDERS (spec 2026-10-04 §4.2; mockup board/orders.html #p1) on kit v2: the cue line while a map order is armed
    /// (HERE and CANCEL), the selected wingman's row (RTB, RDR, EJ, tap to INSPECT), the alert strip, the stances and their fine-tune
    /// (<see cref="WmcTactical"/> .Stance), the command card — 24 keys with the wing-key letter, a group rail, an input mark and an LED
    /// for what each element flies — then REACT, the queue lane, THREATS, FLIGHT POOL and the whole-wing settings. A point or area order
    /// latches its key and arms the map; a shift-click (or the QUEUE toggle) adds it to the scope's element lane instead. The console
    /// body scrolls.</summary>
    internal sealed partial class WmcTactical
    {
        private AvRow cueRow, memberRow;
        private AvControl here, cancel, memberRtb, memberRdr, memberEj;
        private WmcCommandCard card;
        private WmcQueueLane queue;
        private readonly AvControl[] grid = new AvControl[OrderGrid.Rows * OrderGrid.Columns];
        private readonly AvControl[] react = new AvControl[5];
        private readonly GridCell[] shownCells = new GridCell[OrderGrid.Rows * OrderGrid.Columns];
        private readonly string[] shownTips = new string[OrderGrid.Rows * OrderGrid.Columns];
        private readonly bool[] shownEnabled = new bool[OrderGrid.Rows * OrderGrid.Columns];
        private readonly int[] ledBits = new int[32];
        private readonly ConfirmGate dismissGate = new ConfirmGate();
        private readonly StringBuilder lineText = new StringBuilder(96);
        private static readonly string[] LedWords = new string[16];
        private int cueKey = int.MinValue, memberKey = int.MinValue, queueKey = int.MinValue;
        private string reactWhy;
        private bool helosShown, gridBuilt, reactOn = true;

        private void BuildOrders(AvFlow f)
        {
            // The cue line: the armed order (HERE last-but-one, CANCEL at the right edge); hidden while nothing is armed.
            cueRow = f.Add(new AvRow(f.Content));
            here = cueRow.AddTrailing(new AvControl.Spec("HERE", Here));
            here.Help = "Orbit or hold where the scope is now.";
            ids.Add("tac.orders.here", here);
            cancel = cueRow.AddTrailing(new AvControl.Spec("CANCEL", () => last?.Map.Disarm(), AvButtonStyle.Quiet));
            cancel.Help = "Disarm the map order (Esc does too).";
            ids.Add("tac.orders.cancel", cancel);
            cueRow.Rect.gameObject.SetActive(false);

            // One selected wingman: its commands (the table stays dense), and a tap on the row opens it on WING › INSPECT.
            memberRow = f.Add(new AvRow(f.Content, InspectSelected));
            memberRow.Help = "This aircraft on WING › INSPECT: stores, fuel, hull, its task and what it has been doing.";
            memberRtb = memberRow.AddTrailing(new AvControl.Spec("RTB", () => AskRtb(SelectedId)));
            memberRdr = memberRow.AddTrailing(new AvControl.Spec("RDR", () => ToggleRadar(SelectedId)));
            memberEj = memberRow.AddTrailing(new AvControl.Spec("EJ", () => AskEject(SelectedId), AvButtonStyle.Danger));
            memberRtb.Help = "Send this wingman home to the reserve (press twice).";
            memberRdr.Help = "This aircraft's radar: RDR on, EMCON silent or off. Press to switch it (the rest of the wing keeps its setting).";
            memberEj.Help = "Eject this pilot (press twice): the aircraft is lost, search and rescue picks the pilot up.";
            memberRow.Rect.gameObject.SetActive(false);

            BuildAlerts(f);
            BuildStance(f);
            BuildGrid(f);
            BuildPanes(f);
            BuildWholeWing(f);
        }

        private uint SelectedId => last != null ? last.Selection.Single : 0u;

        private void InspectSelected() => Inspect(SelectedId);

        private void BuildGrid(AvFlow f)
        {
            card = f.Add(new WmcCommandCard(f.Content, PressCell));
            for (int k = 0; k < grid.Length; k++) grid[k] = card.Cells[k];
            SetGrid(false);
            var reactSpecs = new AvControl.Spec[react.Length];
            for (int i = 0; i < react.Length; i++)
            {
                int k = i;
                reactSpecs[i] = new AvControl.Spec(OrderGrid.React[i].Label, () => PressReact(k));
            }
            WmcLabeledButtons reactRow = f.Add(new WmcLabeledButtons(f.Content, OrderGrid.ReactLabel, WmcState.Of(OrderGrid.ReactRail), reactSpecs));
            for (int i = 0; i < react.Length; i++)
            {
                react[i] = reactRow.Controls[i];
                react[i].Help = OrderGrid.React[i].Tip;
                ids.Add(OrderGrid.React[i].Id, react[i]);
            }
            queue = f.Add(new WmcQueueLane(f.Content, ToggleQueue, RunQueue, ClearQueue));
            queue.Toggle.Help = "Add the next map orders to the scope's element lane instead of replacing its task (holding shift does the same).";
            queue.Run.Help = "Run the queued plan steps (the plan on BEHAVIOUR › PLAN).";
            queue.Clear.Help = "Empty the queued plan.";
            ids.Add("tac.queue.toggle", queue.Toggle);
            ids.Add("tac.queue.run", queue.Run);
            ids.Add("tac.queue.clear", queue.Clear);
        }

        /// <summary>The grid's cells for jets or, with helicopters in scope, the helo swap (SUPPORT → TAKE OFF · RESCUE · LAND ·
        /// CARGO). An id always names the button showing it; the key letter stays with the position.</summary>
        private void SetGrid(bool helos)
        {
            if (gridBuilt && helos == helosShown) return;
            gridBuilt = true;
            helosShown = helos;
            for (int r = 0; r < OrderGrid.Rows; r++)
                for (int col = 0; col < OrderGrid.Columns; col++)
                {
                    int k = r * OrderGrid.Columns + col;
                    GridCell cell = OrderGrid.At(r, col, helos);
                    if (shownCells[k].Id == cell.Id) continue;
                    if (shownCells[k].Id != null) ids.Release(shownCells[k].Id, grid[k]);
                    shownCells[k] = cell;
                    shownTips[k] = null;
                    card.SetFace(k, cell.Label, cell.Input);
                    ids.Add(cell.Id, grid[k]);
                }
        }

        private void PressCell(int k)
        {
            if (last == null) return;
            GridCell cell = shownCells[k];
            if (!cell.Built) return;
            WmcMotion.Punch(grid[k]);
            if (cell.Map != MapMode.Off)
            {
                // The latched order again disarms it (the cue's CANCEL does too).
                if (last.Map.Mode == cell.Map) last.Map.Disarm();
                else last.Map.Arm(last, cell.Map);
                return;
            }
            WmcUi.Order(last, () =>
            {
                WingScope scoped = last.Scope;
                switch (cell.Order)
                {
                    case GridOrder.Splash: WingCommands.Splash(scoped); break;
                    case GridOrder.Engage: WingCommands.Engage(scoped); break;
                    case GridOrder.MyTarget: WingCommands.AttackTarget(scoped); break;
                    case GridOrder.Scout: WingCommands.ScoutAhead(scoped); break;
                    case GridOrder.Break: WingCommands.Disengage(scoped); break;
                    case GridOrder.ClearSix: WingCommands.ClearMySix(); break;
                    case GridOrder.FormUp: WingCommands.FormUp(scoped); break;
                    case GridOrder.Patrol: WingCommands.PatrolHere(scoped); break;
                    case GridOrder.Escort: WingCommands.EscortMe(); break;
                    case GridOrder.Detach: Detach(); break;
                    case GridOrder.Call: WingCommands.Call(1); break;
                    case GridOrder.Bogey: WingCommands.BogeyDope(); break;
                    case GridOrder.Dismiss:
                        if (!dismissGate.Press("dismiss", Time.unscaledTime))
                        {
                            WingToast.Show("Release every wingman to the game's AI? Press DISMISS again");
                            return;
                        }
                        WingCommands.Dismiss();
                        break;
                    case GridOrder.Rtb: WingCommands.Rtb(scoped); break;
                    case GridOrder.Refit: WingCommands.Refit(scoped); break;
                    case GridOrder.TakeOff: WingCommands.TakeOff(scoped); break;
                    case GridOrder.Rescue: WingCommands.Rescue(scoped); break;
                    case GridOrder.Ecm: WingOrders.Run(WingOrder.Of(OrderKind.Ecm, scoped)); break;
                }
                FlashScope();
            });
        }

        /// <summary>A maneuver is a one-shot order: flown through the pipeline, then back to the slot.</summary>
        private void PressReact(int i)
        {
            WmcMotion.Punch(react[i]);
            WmcUi.Order(last, () =>
            {
                WingOrders.Run(new WingOrder { Kind = OrderKind.Maneuver, Number = OrderGrid.React[i].Number, Scope = last.Scope });
                FlashScope();
            });
        }

        /// <summary>The selected wingmen orbit the point they are over now, as their own element.</summary>
        private void Detach()
        {
            if (last.Scope.Kind == ScopeKind.Wing)
            {
                WingToast.Show("Select the wingmen to detach");
                return;
            }
            if (!Centroid(out Vec3 c, out _)) return;
            Waypoint at = Waypoint.At(c.X, c.Z);
            at.Altitude = c.Y;
            WingOrders.Run(WingOrder.Tasked(WingTask.Orbit(at), last.Scope));
        }

        /// <summary>HERE: the armed ORBIT or HOLD, at the scope's own position rather than a clicked point.</summary>
        private void Here()
        {
            if (last == null) return;
            MapMode mode = last.Map.Mode;
            if (mode != MapMode.Orbit && mode != MapMode.Hold) return;
            WmcUi.Order(last, () =>
            {
                if (!Centroid(out Vec3 c, out Vec3 v)) return;
                Waypoint at = Waypoint.At(c.X, c.Z);
                at.Altitude = c.Y;
                WingTask task = mode == MapMode.Hold ? WingTask.Hold(at, Vec3.HeadingDeg(v)) : WingTask.Orbit(at);
                if (WingOrders.Run(WingOrder.Tasked(task, last.Scope)).Accepted)
                {
                    last.Map.Disarm();
                    FlashScope();
                }
            });
        }

        /// <summary>The scope's members' mean position and velocity (host).</summary>
        private bool Centroid(out Vec3 pos, out Vec3 vel)
        {
            pos = vel = Vec3.Zero;
            if (last?.Wing == null) return false;
            int n = 0;
            foreach (WingMember m in last.Wing.Members)
            {
                if (m.Released || !m.Alive || (object)m.Aircraft == null) continue;
                uint id = m.Aircraft.persistentID.Id;
                int i = WingRows.IndexOf(last.Rows, last.Count, id);
                if (i < 0 || !InScope(last, last.Rows[i])) continue;
                pos += m.Last.Pos;
                vel += m.Last.Vel;
                n++;
            }
            if (n == 0) return false;
            pos *= 1f / n;
            vel *= 1f / n;
            return true;
        }

        private void RefreshOrders(WmcContext c)
        {
            RefreshCue(c);
            RefreshMember(c);
            RefreshAlertStrip(c);
            RefreshStance(c);
            SetGrid(HelosInScope(c));
            bool scoped = c.Scope.Kind != ScopeKind.Wing;
            ComputeLeds(c);
            for (int k = 0; k < grid.Length; k++)
            {
                GridCell cell = shownCells[k];
                string why = OrderGrid.Why(cell, c.CanOrder, c.Count, scoped);
                bool on = why == null;
                string tip = why ?? cell.Tip;
                if (on != shownEnabled[k] || !ReferenceEquals(tip, shownTips[k]))
                {
                    shownEnabled[k] = on;
                    shownTips[k] = tip;
                    grid[k].Interactable = on;
                    grid[k].Help = tip;
                    card.SetWhy(k, on ? "" : WhyCode(cell, c));
                }
                grid[k].Latched = cell.Map != MapMode.Off ? c.Map.Mode == cell.Map
                    : cell.Order == GridOrder.Ecm ? JammingInScope(c)
                    : cell.Order == GridOrder.Dismiss && dismissGate.IsArmed("dismiss", Time.unscaledTime);
                card.SetLed(k, LedWords[ledBits[(int)cell.Order] & 15]);
            }
            RefreshReact(c);
            RefreshQueue(c);
            RefreshPanes(c);
            RefreshWholeWing(c);
        }

        /// <summary>A word of why a key is off, in the width of its corner.</summary>
        private static string WhyCode(in GridCell cell, WmcContext c)
        {
            if (!cell.Built) return "SOON";
            if (!c.CanOrder) return "HOST";
            if (c.Count == 0 && cell.Order != GridOrder.Call) return "NO AC";
            return "SELECT";
        }

        static WmcTactical()
        {
            for (int bits = 0; bits < LedWords.Length; bits++)
            {
                string s = "";
                for (int e = 0; e < ElementRoster.MaxElements; e++)
                    if ((bits & (1 << e)) != 0) s += ElementRoster.Letter(e);
                LedWords[bits] = s;
            }
        }

        /// <summary>Which elements fly each order now, as bits per <see cref="GridOrder"/>: the element's task (a form task is FORM UP, a
        /// path MOVE, a held point HOLD, a guarded area CAP or SWEEP) and what its members are doing (RTB while recovering, ENGAGE).</summary>
        private void ComputeLeds(WmcContext c)
        {
            System.Array.Clear(ledBits, 0, ledBits.Length);
            for (int e = 0; e < ElementRoster.MaxElements; e++)
            {
                bool present = false;
                for (int i = 0; i < c.Count && !present; i++) present = c.Rows[i].Element == e;
                if (!present) continue;
                int bit = 1 << e;
                WingPlanner p = c.Wing != null && !c.Client && c.Wing.Roster.InUse(e) ? c.Wing.PlannerOf(e) : null;
                WingTask t = p != null && p.Active ? p.Current : null;
                GridOrder o = GridOrder.FormUp;
                if (t != null)
                {
                    string word = AreaGuard.Word(t);
                    switch (t.Kind)
                    {
                        case TaskKind.Move:
                        case TaskKind.Route: o = GridOrder.Move; break;
                        case TaskKind.Patrol: o = word == "SWEEP" ? GridOrder.Sweep : GridOrder.Patrol; break;
                        case TaskKind.Orbit: o = word == "CAP" ? GridOrder.Cap : GridOrder.Orbit; break;
                        case TaskKind.Hold: o = GridOrder.Hold; break;
                    }
                }
                ledBits[(int)o] |= bit;
            }
            for (int i = 0; i < c.Count; i++)
            {
                var duty = (MemberDuty)c.Rows[i].Duty;
                int bit = 1 << (c.Rows[i].Element & 3);
                if (duty == MemberDuty.Recovering) ledBits[(int)GridOrder.Rtb] |= bit;
                else if (duty == MemberDuty.Engaged) ledBits[(int)GridOrder.Engage] |= bit;
            }
        }

        private void RefreshCue(WmcContext c)
        {
            MapMode mode = c.Map.Mode;
            bool armed = mode != MapMode.Off;
            bool showHere = armed && (mode == MapMode.Orbit || mode == MapMode.Hold);
            if (here.gameObject.activeSelf != showHere) here.gameObject.SetActive(showHere);
            if (cancel.gameObject.activeSelf != armed) cancel.gameObject.SetActive(armed);
            if (cueRow.Rect.gameObject.activeSelf != armed)
            {
                cueRow.Rect.gameObject.SetActive(armed);
                relayout = true;
            }
            int key = armed ? 1000 + (int)mode : 3000;
            if (key == cueKey) return;
            cueKey = key;
            if (armed) cueRow.Set(AvStates.Glyph(AvState.Caution) + WmcWords.Banner(mode), "", "", AvState.Caution);
            relayout = true;
        }

        /// <summary>The selected wingman's row: shown for exactly one aircraft picked, with its callsign, task and the three commands.</summary>
        private void RefreshMember(WmcContext c)
        {
            uint id = c.Selection.Single;
            int row = id != 0u ? WingRows.IndexOf(c.Rows, c.Count, id) : -1;
            bool on = row >= 0;
            if (memberRow.Rect.gameObject.activeSelf != on)
            {
                memberRow.Rect.gameObject.SetActive(on);
                relayout = true;
            }
            if (!on) return;
            SnapshotMember m = c.Rows[row];
            bool rtbAsking = rtbGate.IsArmed(AvNum.Fixed(id, 0), Time.unscaledTime), ejAsking = ejGate.IsArmed(AvNum.Fixed(id, 0), Time.unscaledTime);
            WingMember wm = c.CanOrder ? c.MemberOf(id) : null;
            bool hasRadar = wm != null && wm.Aircraft != null && wm.Aircraft.radar is Radar;
            string rdr = wm == null ? "RDR" : !hasRadar ? "RDR —" : c.Wing.DoctrineFor(wm).Radar == RadarPolicy.On ? "RDR" : "EMCON";
            int key = (int)(id % 1000003u) * 31 + m.Slot * 7 + m.Duty + m.Behaviour * 3 + (rtbAsking ? 1 : 0) + (ejAsking ? 2 : 0) + rdr.GetHashCode();
            memberRtb.Interactable = memberEj.Interactable = c.CanOrder;
            memberRdr.Interactable = hasRadar;
            if (key == memberKey) return;
            memberKey = key;
            memberRtb.Label = rtbAsking ? "RTB?" : "RTB";
            memberEj.Label = ejAsking ? "EJ?" : "EJ";
            memberRdr.Label = rdr;
            memberRow.Set("SELECTED " + WingRows.Number(m.Slot) + " " + CallsignOf(id), MemberLine.Task(m, null) + " · tap to INSPECT ›", "", AvState.Info);
            relayout = true;
        }

        private void RefreshReact(WmcContext c)
        {
            bool flying = false;
            for (int i = 0; i < c.Count && !flying; i++)
                flying = c.InScope(c.Rows[i]) && (MemberDuty)c.Rows[i].Duty == MemberDuty.Formation;
            bool on = c.CanOrder && flying;
            string why = !c.CanOrder ? "Orders are host only for now" : "Nobody in scope is flying in formation";
            if (on == reactOn && (on || ReferenceEquals(why, reactWhy))) return;
            reactOn = on;
            reactWhy = why;
            for (int i = 0; i < react.Length; i++)
            {
                react[i].Interactable = on;
                react[i].Help = on ? OrderGrid.React[i].Tip : why;
            }
        }

        /// <summary>The queue: what each element flies now and the plan steps queued behind it (host); the toggle, RUN and CLEAR.</summary>
        private void RefreshQueue(WmcContext c)
        {
            WingPlans plans = c.CanOrder ? WingPlans.Instance : null;
            WingPlan plan = plans?.Plan;
            int steps = 0;
            if (plan != null) for (int l = 0; l < WingPlan.Lanes; l++) steps += plan.Steps[l].Count;
            queue.Toggle.Latched = c.Queue;
            queue.Toggle.Interactable = c.CanOrder;
            queue.Run.Interactable = plans != null && steps > 0 && !plans.Running;
            queue.Clear.Interactable = plans != null && steps > 0;
            int key = steps * 131 + (c.Queue ? 1 : 0) + (c.CanOrder ? 2 : 0) + (plans != null && plans.Running ? 4 : 0) + c.ScopeElement * 8191;
            int lanes = 0;
            for (int e = 0; e < ElementRoster.MaxElements; e++)
            {
                bool present = false;
                for (int i = 0; i < c.Count && !present; i++) present = c.Rows[i].Element == e;
                if (!present) continue;
                lanes++;
                WingPlanner p = c.Wing != null && !c.Client && c.Wing.Roster.InUse(e) ? c.Wing.PlannerOf(e) : null;
                key = key * 31 + (p != null && p.Active ? (int)p.Current.Kind + 1 : 0) + (plan != null ? plan.Steps[e].Count * 17 : 0);
                if (plans != null && plans.Runner != null)
                    for (int s = 0; s < plan.Steps[e].Count; s++) key = key * 7 + (int)plans.Runner.State(e, s);
            }
            if (key == queueKey) return;
            queueKey = key;
            queue.SetHint(!c.CanOrder ? (c.Client ? "THE HOST QUEUES ORDERS" : "NOT READY")
                : c.Queue ? "MAP ORDERS ADD TO " + ElementRoster.Letter(c.ScopeElement) : "SHIFT + MAP ORDER ADDS");
            int line = 0;
            for (int e = 0; e < ElementRoster.MaxElements; e++)
            {
                bool present = false;
                for (int i = 0; i < c.Count && !present; i++) present = c.Rows[i].Element == e;
                if (!present) continue;
                queue.SetLine(line++, QueueLine(c, plans, e));
            }
            for (int i = line; i < WmcQueueLane.Lines; i++) queue.SetLine(i, null);
            queue.SetCount(line);
            relayout = true;
        }

        private string QueueLine(WmcContext c, WingPlans plans, int e)
        {
            lineText.Length = 0;
            lineText.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(QueueColor(e))).Append('>').Append(ElementRoster.Letter(e)).Append("</color>  ");
            WingPlanner p = c.Wing != null && !c.Client && c.Wing.Roster.InUse(e) ? c.Wing.PlannerOf(e) : null;
            lineText.Append(p == null ? (e == 0 ? "FORM UP" : WmcText.Unknown)
                : !p.Active ? "FORM UP"
                : TaskCard.Short(p.Current, p.Leg, p.Lead != null ? p.Lead.Position : Vec3.Zero, p.Lead != null ? p.Lead.Speed : 0f));
            if (plans == null) return lineText.ToString();
            int shown = 0, hidden = 0;
            for (int s = 0; s < plans.Plan.Steps[e].Count; s++)
            {
                StepState st = plans.Runner != null ? plans.Runner.State(e, s) : StepState.Pending;
                if (st == StepState.Done || st == StepState.Skipped) continue;
                if (shown >= 3)
                {
                    hidden++;
                    continue;
                }
                lineText.Append(" → ").Append(PlanWords.Kind(plans.Plan.Steps[e][s].Kind));
                shown++;
            }
            if (hidden > 0) lineText.Append(" +").Append(hidden);
            return lineText.ToString();
        }

        private static Color QueueColor(int e) => e == 0 ? AvStyleHost.FuiColor("friendly", AvTheme.Friendly)
            : e == 1 ? AvStyleHost.FuiColor("caution", AvTheme.RailCaution) : e == 2 ? AvStyleHost.FuiColor("ready", AvTheme.RailReady)
            : AvStyleHost.FuiColor("info", AvTheme.RailInfo);

        private void ToggleQueue()
        {
            if (last == null) return;
            last.Queue = !last.Queue;
            queueKey = int.MinValue;
            if (last.Queue) WingToast.Show("Queue on: map orders add to element " + ElementRoster.Letter(last.ScopeElement));
        }

        private void RunQueue()
        {
            WingPlans plans = WingPlans.Instance;
            if (plans == null || last == null || !last.CanOrder) return;
            var errors = plans.Execute();
            if (errors != null && errors.Count > 0) WingToast.Show("Plan: " + errors[0]);
            else WingToast.Show("Plan running");
            queueKey = int.MinValue;
        }

        private void ClearQueue()
        {
            WingPlans plans = WingPlans.Instance;
            if (plans == null || last == null || !last.CanOrder) return;
            plans.Clear();
            queueKey = int.MinValue;
        }

        /// <summary>A member in scope jams now (ECM stays lit while one does); no allocation.</summary>
        private static bool JammingInScope(WmcContext c)
        {
            if (c.Wing == null || c.Client) return false;
            foreach (WingMember m in c.Wing.Members)
            {
                if (float.IsNaN(m.JamUntil) || (object)m.Aircraft == null) continue;
                int i = WingRows.IndexOf(c.Rows, c.Count, m.Aircraft.persistentID.Id);
                if (i >= 0 && InScope(c, c.Rows[i])) return true;
            }
            return false;
        }

        /// <summary>Any scoped member a helicopter or tiltwing (host; a client's rows carry no airframe class).</summary>
        private static bool HelosInScope(WmcContext c)
        {
            if (c.Wing == null || c.Client) return false;
            foreach (WingMember m in c.Wing.Members)
            {
                if (m.Released || (object)m.Aircraft == null || m.Profile == null || m.Profile.Class == AirframeClass.FixedWing) continue;
                int i = WingRows.IndexOf(c.Rows, c.Count, m.Aircraft.persistentID.Id);
                if (i >= 0 && InScope(c, c.Rows[i])) return true;
            }
            return false;
        }
    }
}
