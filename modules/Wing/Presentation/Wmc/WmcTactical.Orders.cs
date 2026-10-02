using NOAvionics;
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
    /// <summary>TACTICAL › ORDERS (spec FUI §TACTICAL) on kit v2: the cue line (the armed order and what to click, with HERE and CANCEL;
    /// else who orders go to), the order grid — six labelled rows with a category rail and an icon per cell, whose point and target
    /// orders latch and arm the map — and REACT: the five maneuvers; then POSTURE, THREATS and FLIGHT POOL. The console body scrolls,
    /// the grid first.</summary>
    internal sealed partial class WmcTactical
    {
        private AvRow cueRow;
        private AvControl here, cancel;
        private readonly AvControl[] grid = new AvControl[OrderGrid.Rows * OrderGrid.Columns];
        private readonly AvControl[] react = new AvControl[5];
        private readonly GridCell[] shownCells = new GridCell[OrderGrid.Rows * OrderGrid.Columns];
        private readonly string[] shownTips = new string[OrderGrid.Rows * OrderGrid.Columns];
        private readonly bool[] shownEnabled = new bool[OrderGrid.Rows * OrderGrid.Columns];
        private readonly ConfirmGate dismissGate = new ConfirmGate();
        private int cueKey = int.MinValue;
        private string reactWhy;
        private bool helosShown, gridBuilt, reactOn = true;

        private void BuildOrders(AvFlow f)
        {
            // The cue line: the armed order (HERE last-but-one, CANCEL at the right edge), else who the orders go to.
            cueRow = f.Add(new AvRow(f.Content));
            here = cueRow.AddTrailing(new AvControl.Spec("HERE", Here));
            here.Help = "Orbit or hold where the scope is now.";
            ids.Add("tac.orders.here", here);
            cancel = cueRow.AddTrailing(new AvControl.Spec("CANCEL", () => last?.Map.Disarm(), AvButtonStyle.Quiet));
            cancel.Help = "Disarm the map order (Esc does too).";
            ids.Add("tac.orders.cancel", cancel);

            BuildGrid(f);
            BuildPosture(f);
            BuildPanes(f);
        }

        private void BuildGrid(AvFlow f)
        {
            for (int r = 0; r < OrderGrid.Rows; r++)
            {
                var specs = new AvControl.Spec[OrderGrid.Columns];
                for (int col = 0; col < OrderGrid.Columns; col++)
                {
                    int k = r * OrderGrid.Columns + col;
                    // The icon is a placeholder: the helicopter swap changes label, icon and id (SetGrid).
                    specs[col] = new AvControl.Spec("", () => PressCell(k), AvButtonStyle.Default, AvIcon.Circle);
                }
                WmcLabeledButtons row = f.Add(new WmcLabeledButtons(f.Content, OrderGrid.RowLabels[r], WmcState.Of(OrderGrid.RowRail(r)), specs));
                for (int col = 0; col < OrderGrid.Columns; col++) grid[r * OrderGrid.Columns + col] = row.Controls[col];
            }
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
        }

        /// <summary>The grid's cells for jets or, with helicopters in scope, the helo swap (SUPPORT → TAKE OFF · RESCUE · LAND ·
        /// CARGO). An id always names the button showing it; the icon follows the cell.</summary>
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
                    grid[k].Label = cell.Label;
                    WmcIcons.Swap(grid[k], WmcIcons.Of(cell.Glyph));
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
            SetGrid(HelosInScope(c));
            bool scoped = c.Scope.Kind != ScopeKind.Wing;
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
                }
                grid[k].Latched = cell.Map != MapMode.Off ? c.Map.Mode == cell.Map
                    : cell.Order == GridOrder.Ecm ? JammingInScope(c)
                    : cell.Order == GridOrder.Dismiss && dismissGate.IsArmed("dismiss", Time.unscaledTime);
            }
            RefreshReact(c);
            RefreshPosture(c);
            RefreshPanes(c);
        }

        private void RefreshCue(WmcContext c)
        {
            MapMode mode = c.Map.Mode;
            bool armed = mode != MapMode.Off;
            bool showHere = armed && (mode == MapMode.Orbit || mode == MapMode.Hold);
            if (here.gameObject.activeSelf != showHere) here.gameObject.SetActive(showHere);
            if (cancel.gameObject.activeSelf != armed) cancel.gameObject.SetActive(armed);
            int key = armed ? 1000 + (int)mode : 3000 + (c.ScopeLabel?.GetHashCode() ?? 0) % 997;
            if (key == cueKey) return;
            cueKey = key;
            if (armed) cueRow.Set(AvStates.Glyph(AvState.Caution) + WmcWords.Banner(mode), "", "", AvState.Caution);
            else cueRow.Set("Orders go to " + c.ScopeLabel + ".", "", "", AvState.Info);
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
