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
    /// <summary>TACTICAL › ROUTE·AP (spec FUI; the user merged ROUTE and AP) on kit v2: the scope's quick route — draw points on the
    /// map (an <see cref="AvList"/> of points), set each one's altitude, speed and arrival action, send it, loop it, skip a leg, save
    /// it — then MY AUTOPILOT: your own holds, and NAV, which flies the route you drew. The scope is TACTICAL's scope bar. Moved out of
    /// BEHAVIOUR; its control ids are unchanged. The saved-route picker opens the toolkit popup (<see cref="AvPopup"/>).</summary>
    internal sealed class WmcRoute : IWmcPage
    {
        private const int DraftRows = 7;
        private static readonly ApField[] ApFields = { ApField.Heading, ApField.Altitude, ApField.VerticalSpeed, ApField.Speed };
        private static readonly string[] ApNames = { "HDG", "ALT", "VS", "SPD" };
        private const string EmptyLegs = "NO POINTS · PRESS DRAW, THEN RIGHT-CLICK THE MAP";

        private readonly WmcControls ids;
        private AvFlow flow;
        private RectTransform pageContent;
        private AvPopup popup;
        private WmcContext last;
        private AvSection legsSection, apSection;
        private AvList draftList;
        private AvStepper altStepper, spdStepper;
        private readonly AvStepper[] apSteppers = new AvStepper[4];
        private readonly string[] apValues = { "", "", "", "" };
        private readonly AvControl[] apModes = new AvControl[7];
        private readonly List<RouteLeg> draftLegs = new List<RouteLeg>(RouteDraft.MaxPoints + 1);
        private readonly ConfirmGate routeDeleteGate = new ConfirmGate();
        private readonly List<AvPopupEntry> routeEntries = new List<AvPopupEntry>(RouteStore.Max);
        private AvControl draw, action, del, skip, send, undo, clear, loop, save, savedPicker, savedDelete;
        private string altText = "", spdText = "", legsShown, apNoteShown;
        private int savedSelected = -1, legsKey = int.MinValue, apKey = int.MinValue, listKey = int.MinValue, revealed = -1;
        private RouteLoop loopShown = (RouteLoop)255;

        public WmcRoute(WmcControls controls)
        {
            ids = controls;
        }

        public string Hint => "DRAW, then right-click the map to add points; SEND flies them. NAV flies your own aircraft along them.";

        public string Alert => null;

        /// <summary>TACTICAL's popup, and the page content it is parented to (the popup opens inside the visible body).</summary>
        public void UsePopup(AvPopup shared, RectTransform content)
        {
            popup = shared;
            pageContent = content;
        }

        public void Build(AvFlow pageFlow, AvTicker ticker, int pageIndex)
        {
            flow = pageFlow;
            legsSection = flow.Section(AvIcon.MapPin, "LEGS", EmptyLegs);
            draw = flow.Buttons(new AvControl.Spec("DRAW", ToggleDraw, AvButtonStyle.Default, AvIcon.MapPin)).Controls[0];
            draw.Help = "Right-click the map to add route points while DRAW is lit.";
            ids.Add("plan.route.draw", draw);

            draftList = flow.Add(new AvList(flow.Content, ticker, DraftRows, BindPoint));
            draftList.RowClicked = PickPoint;

            altStepper = flow.Add(new AvStepper(flow.Content, "ALTITUDE", () => altText, () => EditDraft(d => d.StepAltitude(-1)),
                () => EditDraft(d => d.StepAltitude(+1))));
            spdStepper = flow.Add(new AvStepper(flow.Content, "SPEED", () => spdText, () => EditDraft(d => d.StepSpeed(-1)),
                () => EditDraft(d => d.StepSpeed(+1))));
            altStepper.Minus.Help = altStepper.Plus.Help = "Altitude of the selected point, or of new points (AUTO: the task's own).";
            spdStepper.Minus.Help = spdStepper.Plus.Help = "Speed of the selected point, or of new points (AUTO: cruise).";
            ids.Add("plan.route.alt-", altStepper.Minus);
            ids.Add("plan.route.alt+", altStepper.Plus);
            ids.Add("plan.route.spd-", spdStepper.Minus);
            ids.Add("plan.route.spd+", spdStepper.Plus);

            AvControl[] edit = flow.Buttons(
                new AvControl.Spec("ACTION", () => EditDraft(d => d.CycleAction())),
                new AvControl.Spec("DEL PT", () => EditDraft(d => d.RemoveSelected())),
                new AvControl.Spec("SKIP LEG", () => WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.SkipLeg, last.Scope))))).Controls;
            action = edit[0];
            del = edit[1];
            skip = edit[2];
            ids.Add("plan.route.action", action);
            ids.Add("plan.route.del", del);
            ids.Add("plan.route.skip", skip);
            AvControl[] draft = flow.Buttons(
                new AvControl.Spec("SEND", Send, AvButtonStyle.Primary, AvIcon.CircleCheck),
                new AvControl.Spec("UNDO", () => EditDraft(d => d.Undo())),
                new AvControl.Spec("CLEAR", () => EditDraft(d => d.Clear())),
                new AvControl.Spec("ONCE", () => EditDraft(d => d.CycleLoop())),
                new AvControl.Spec("SAVE", SaveRoute)).Controls;
            send = draft[0];
            undo = draft[1];
            clear = draft[2];
            loop = draft[3];
            save = draft[4];
            ids.Add("plan.route.send", send);
            ids.Add("plan.route.undo", undo);
            ids.Add("plan.route.clear", clear);
            ids.Add("plan.route.loop", loop);
            ids.Add("plan.route.save", save);
            loop.Help = "Fly the route once, loop it, or back and forth.";

            AvControl[] saved = flow.Buttons(new AvControl.Spec("PICK A SAVED ROUTE ›", OpenSaved),
                new AvControl.Spec("DELETE", DeleteSaved, AvButtonStyle.Danger)).Controls;
            savedPicker = saved[0];
            savedDelete = saved[1];
            savedPicker.Help = "Load a saved route into the draft; SEND flies it.";
            savedDelete.Help = "Delete the loaded saved route (press twice).";
            ids.Add("plan.route.saved", savedPicker);
            ids.Add("plan.route.saved.del", savedDelete);

            apSection = flow.Section(AvIcon.Plane, "MY AUTOPILOT", "");
            string[] modes = { "LVL", "HDG", "ALT", "VS", "SPD", "NAV", "OFF" };
            string[] keys = { "level", "heading", "altitude", "vs", "speed", "nav", "off" };
            string[] tips =
            {
                "Wings level.", "Hold the current heading.", "Hold the current altitude.", "Hold the current climb or descent.",
                "Hold the current speed.", "Fly the route you drew (or the scope's route when the draft is empty).", "Autopilot off.",
            };
            var modeSpecs = new AvControl.Spec[modes.Length];
            for (int i = 0; i < modes.Length; i++)
            {
                int k = i;
                modeSpecs[i] = new AvControl.Spec(modes[i], () => PressAp(k));
            }
            AvControl[] apControls = flow.Buttons(modeSpecs).Controls;
            for (int i = 0; i < modes.Length; i++)
            {
                apModes[i] = apControls[i];
                apModes[i].Help = tips[i];
                ids.Add("plan.ap." + keys[i], apModes[i]);
            }

            for (int i = 0; i < ApFields.Length; i++)
            {
                ApField f = ApFields[i];
                int slot = i;
                apSteppers[i] = flow.Add(new AvStepper(flow.Content, ApNames[i], () => apValues[slot],
                    () => PlayerAutopilot.Instance?.Adjust(f, -1), () => PlayerAutopilot.Instance?.Adjust(f, 1)));
                string tip = ApNames[i] + ": the held value; the hold flies to it.";
                apSteppers[i].Minus.Help = apSteppers[i].Plus.Help = tip;
                string id = "plan.ap." + f.ToString().ToLowerInvariant();
                ids.Add(id + ".down", apSteppers[i].Minus);
                ids.Add(id + ".up", apSteppers[i].Plus);
            }
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

        private void PickPoint(int item)
        {
            if (last != null) last.Draft.Select(item);
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

        private void SaveRoute()
        {
            if (last == null) return;
            if (WmcRoutes.Store.Save(last.Draft, out string name))
            {
                WmcRoutes.Save();
                WingToast.Show("Saved as " + name);
            }
            else WingToast.Show(last.Draft.Count == 0 ? "Nothing to save: add points first" : AvNum.Fixed(RouteStore.Max, 0) + " routes saved already: delete one");
        }

        private void OpenSaved()
        {
            IReadOnlyList<SavedRoute> routes = WmcRoutes.Store.Routes;
            if (routes.Count == 0)
            {
                WingToast.Show("No saved routes. SAVE keeps the draft.");
                return;
            }
            routeEntries.Clear();
            for (int i = 0; i < routes.Count; i++)
                routeEntries.Add(new AvPopupEntry(routes[i].Name, AvNum.Fixed(routes[i].Points.Length, 0) + " PTS · " + RouteDraft.LoopText(routes[i].Loop),
                    i == savedSelected));
            // Review U1-U2: placed inside the visible body, as SUPPLY's and LOADOUT's popups are.
            popup.Show(WmcPopup.Area(pageContent, savedPicker.Rect, routeEntries.Count), routeEntries, PickSaved);
        }

        private void PickSaved(int i)
        {
            if (last == null) return;
            IReadOnlyList<SavedRoute> routes = WmcRoutes.Store.Routes;
            if (i < 0 || i >= routes.Count) return;
            savedSelected = i;
            WmcRoutes.Store.Load(i, last.Draft);
            savedPicker.Label = routes[i].Name + " ›";
            WingToast.Show("Loaded " + routes[i].Name + ": SEND to fly it");
        }

        private void DeleteSaved()
        {
            IReadOnlyList<SavedRoute> routes = WmcRoutes.Store.Routes;
            if (savedSelected < 0 || savedSelected >= routes.Count) return;
            string name = routes[savedSelected].Name;
            if (!routeDeleteGate.Press(name, Time.unscaledTime))
            {
                WingToast.Show("Delete " + name + "? Press DELETE again");
                return;
            }
            WmcRoutes.Store.Remove(savedSelected);
            WmcRoutes.Save();
            savedSelected = -1;
            savedPicker.Label = "PICK A SAVED ROUTE ›";
            WingToast.Show(name + " deleted");
        }

        private void PressAp(int k)
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null)
            {
                WingToast.Show("Autopilot unavailable");
                return;
            }
            switch (k)
            {
                case 0: WingCommands.Autopilot(ApCommand.Level); break;
                case 1: WingCommands.Autopilot(ApCommand.Heading); break;
                case 2: WingCommands.Autopilot(ApCommand.Altitude); break;
                case 3: WingCommands.Autopilot(ApCommand.VerticalSpeed); break;
                case 4: WingCommands.Autopilot(ApCommand.Speed); break;
                case 5: EngageNav(); break;
                default: WingCommands.Autopilot(ApCommand.Off); break;
            }
        }

        /// <summary>NAV flies the draft; with no draft, the scope's own route (what the element is flying now).</summary>
        private void EngageNav()
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
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

        /// <summary>A draft point's row: the leg, then altitude, speed and arrival action; the selected point is armed.</summary>
        private void BindPoint(int item, AvRow row)
        {
            RouteDraft d = last?.Draft;
            if (d == null || item >= d.Count || item >= draftLegs.Count) return;
            Waypoint wp = d[item];
            bool selected = item == d.Selected;
            row.Set(RouteView.Label(draftLegs[item]),
                RouteDraft.AltitudeText(wp.Altitude) + " · " + RouteDraft.SpeedText(wp.Speed) + " · " + RouteDraft.ActionText(wp), "",
                AvState.Info);
            row.Armed = selected;
            row.Help = "Select this point to change its altitude, speed or arrival action.";
            ids.Add("plan.route.row" + item % DraftRows, row);
        }

        private void RefreshRoute(WmcContext c)
        {
            WingService w = c.Wing;
            WingPlanner p = w != null && !c.Client && w.Roster.InUse(c.ScopeElement) ? w.PlannerOf(c.ScopeElement) : null;
            RouteDraft d = c.Draft;
            draw.Latched = c.Map.Mode == MapMode.Route;
            draw.Interactable = c.CanOrder;

            draftLegs.Clear();
            Vec3 from = WmcMapInput.From(c, out float speed);
            RouteView.Draft(d, from, speed, draftLegs);
            float totalKm = d.Count > 0 && draftLegs.Count > 0 ? draftLegs[d.Count - 1].Km : 0f;
            int key = d.Count * 100000 + Mathf.RoundToInt(totalKm * 10f) * 8 + (int)d.Loop;
            if (key != legsKey)
            {
                legsKey = key;
                string note = d.Count == 0 ? EmptyLegs
                    : AvNum.Fixed(d.Count, 0) + " PTS · " + AvNum.Fixed(totalKm, 1) + " km · " + RouteDraft.LoopText(d.Loop);
                if (note != legsShown)
                {
                    legsShown = note;
                    legsSection.SetCaption(note);
                }
            }
            // The list rebinds only when a point, tenth of a km, second, altitude, speed, action or the selection changes.
            long rows = d.Count * 31L + d.Selected;
            for (int i = 0; i < d.Count && i < draftLegs.Count; i++) rows = rows * 131 ^ RowKey(i, draftLegs[i], d[i]);
            int rk = (int)(rows ^ (rows >> 32));
            if (rk != listKey)
            {
                listKey = rk;
                draftList.SetCount(d.Count);
                if (d.Selected >= 0 && d.Selected != revealed) draftList.Reveal(d.Selected);
                revealed = d.Selected;
                flow.RequestRelayout();
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
            if (d.Loop != loopShown)
            {
                loopShown = d.Loop;
                loop.Label = RouteDraft.LoopText(d.Loop);
            }
            bool any = d.Count > 0, picked = d.Selected >= 0, canSkip = c.CanOrder && p != null && p.Active && p.Leg >= 0;
            Gate(action, picked, "Click a point row first", "The selected point's arrival: orbit 2 min, land, cargo, none.");
            Gate(del, picked, "Click a point row first", "Remove the selected point.");
            Gate(send, any && c.CanOrder, !c.CanOrder ? "Orders are host only for now" : "Add points first",
                "Fly the draft: one point is a MOVE, more a ROUTE; LOOP or PING-PONG makes a patrol.");
            Gate(undo, any, "The draft is empty", "Remove the last point.");
            Gate(clear, any, "The draft is empty", "Empty the draft.");
            Gate(save, any, "Add points first", "Keep the draft as a saved route.");
            Gate(skip, canSkip, "The scope has no route leg to skip", "The scope's task goes on to its next point now.");
            savedDelete.Interactable = savedSelected >= 0 && savedSelected < WmcRoutes.Store.Routes.Count;
            RefreshAutopilot();
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
                SetApNote("UNAVAILABLE");
                return;
            }
            HoldSpec h = ap.Session.Spec;
            apModes[0].Latched = h.Lateral == LateralHold.Level;
            apModes[1].Latched = h.Lateral == LateralHold.Heading;
            apModes[2].Latched = h.Vertical == VerticalHold.Altitude;
            apModes[3].Latched = h.Vertical == VerticalHold.VerticalSpeed;
            apModes[4].Latched = h.Speed;
            apModes[5].Latched = h.Lateral == LateralHold.Nav;
            apModes[6].Latched = !ap.Session.Engaged;
            int key = (int)h.Lateral * 7 + (int)h.Vertical * 3 + (h.Speed ? 1 : 0) + Mathf.RoundToInt(h.HeadingDeg) * 101
                + Mathf.RoundToInt(h.AltitudeM) * 1009 + Mathf.RoundToInt(h.VerticalSpeedMps * 10f) * 13 + Mathf.RoundToInt(h.SpeedMps) * 17
                + ap.Nav.Index * 5 + (ap.Session.LateralOverride ? 2 : 0) + (ap.Session.VerticalOverride ? 4 : 0);
            if (key == apKey) return;
            apKey = key;
            string text = WingHudText.Autopilot(h, ap.Session.LateralOverride, ap.Session.VerticalOverride, ap.Nav.Index, ap.Nav.Count);
            SetApNote(text.Length > 0 ? text : "AP OFF");
            for (int i = 0; i < ApFields.Length; i++)
            {
                ApField f = ApFields[i];
                bool held = f == ApField.Heading ? h.Lateral == LateralHold.Heading || h.Lateral == LateralHold.Nav
                    : f == ApField.Altitude ? h.Vertical == VerticalHold.Altitude
                    : f == ApField.VerticalSpeed ? h.Vertical == VerticalHold.VerticalSpeed : h.Speed;
                apValues[i] = ApSteps.Readout(h, f, held);
                apSteppers[i].Refresh();
            }
        }

        private void SetApNote(string text)
        {
            if (text == apNoteShown) return;
            apNoteShown = text;
            apSection.SetCaption(text);
            flow.RequestRelayout();
        }

        /// <summary>A draft row's content: rebuilt only when point, tenth of a km, second, altitude, speed or action change.</summary>
        private static long RowKey(int i, in RouteLeg l, in Waypoint w)
        {
            unchecked
            {
                long k = i;
                k = k * 397 ^ (float.IsNaN(l.Km) ? -1L : (long)(l.Km * 10f));
                k = k * 397 ^ (float.IsNaN(l.Eta) ? -1L : (long)l.Eta);
                k = k * 397 ^ (float.IsNaN(w.Altitude) ? -1L : (long)w.Altitude);
                k = k * 397 ^ (float.IsNaN(w.Speed) ? -1L : (long)w.Speed);
                k = k * 397 ^ (long)w.Action;
                return k;
            }
        }
    }
}
