using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
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
    /// <summary>TACTICAL › ROUTE · AP, the Flight Plan (spec 2026-10-04 §4.2; mockup board/route.html panel 4): a mini map of the route as
    /// numbered legs, a leg table (#, BRG, DIST, ALT, SPD, AT POINT, ETA) whose rows select a point for the altitude and speed steppers
    /// and ACTION, ONCE · LOOP · PING-PONG and SEND TO WING; DRAW, UNDO, CLEAR, SAVE and the saved-route picker above the table. While the
    /// draft is empty the table shows the scope's own route read-only (its leg marked). Then MY AUTOPILOT as one annunciator row coupled
    /// to the route (NAV, AP OFF); its modes and steppers open under MORE. The scope is the wing table's. Control ids are unchanged. The
    /// saved-route picker opens the toolkit popup (<see cref="AvPopup"/>).</summary>
    internal sealed class WmcRoute : IWmcPage
    {
        private static readonly ApField[] ApFields = { ApField.Heading, ApField.Altitude, ApField.VerticalSpeed, ApField.Speed };
        private static readonly string[] ApNames = { "HDG", "ALT", "VS", "SPD" };
        private const string EmptyLegs = "NO POINTS · PRESS DRAW, THEN RIGHT-CLICK THE MAP";

        private readonly WmcControls ids;
        private AvFlow flow;
        private RectTransform pageContent;
        private AvPopup popup;
        private WmcContext last;
        private AvSection legsSection, apSection;
        private WmcRouteMap map;
        private WmcLegTable legs;
        private AvStepper altStepper, spdStepper;
        private AvSegmented loopRow;
        private AvRow apRow;
        private AvControl apNav, apOff, apMore;
        private readonly List<AvPart> apDetail = new List<AvPart>(6);
        private readonly AvStepper[] apSteppers = new AvStepper[4];
        private readonly string[] apValues = { "", "", "", "" };
        private readonly AvControl[] apModes = new AvControl[7];
        private readonly RouteData data = new RouteData();
        private readonly ConfirmGate routeDeleteGate = new ConfirmGate();
        private readonly List<AvPopupEntry> routeEntries = new List<AvPopupEntry>(RouteStore.Max);
        private AvControl draw, action, del, skip, send, undo, clear, save, savedPicker, savedDelete;
        private string altText = "", spdText = "", legsShown, apNoteShown;
        private int savedSelected = -1, legsKey = int.MinValue, apKey = int.MinValue, listKey = int.MinValue, loopValue = -1;
        private bool apOpen;

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
            legsSection = flow.Section(AvIcon.MapPin, "ROUTE", EmptyLegs);
            map = flow.Add(new WmcRouteMap(flow.Content));

            AvControl[] tools = flow.Buttons(
                new AvControl.Spec("DRAW", ToggleDraw, AvButtonStyle.Default, AvIcon.MapPin),
                new AvControl.Spec("UNDO", () => EditDraft(d => d.Undo())),
                new AvControl.Spec("CLEAR", () => EditDraft(d => d.Clear())),
                new AvControl.Spec("SAVE", SaveRoute),
                new AvControl.Spec("SAVED ›", OpenSaved)).Controls;
            draw = tools[0];
            undo = tools[1];
            clear = tools[2];
            save = tools[3];
            savedPicker = tools[4];
            draw.Help = "Right-click the map to add route points while DRAW is lit.";
            savedPicker.Help = "Load a saved route into the draft; SEND flies it.";
            ids.Add("plan.route.draw", draw);
            ids.Add("plan.route.undo", undo);
            ids.Add("plan.route.clear", clear);
            ids.Add("plan.route.save", save);
            ids.Add("plan.route.saved", savedPicker);

            legs = flow.Add(new WmcLegTable(flow.Content, PickPoint));
            for (int i = 0; i < RouteData.Max; i++) ids.Add("plan.route.row" + i, legs.RowTarget(i));

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
                new AvControl.Spec("SKIP LEG", () => WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.SkipLeg, last.Scope)))),
                new AvControl.Spec("DEL SAVED", DeleteSaved, AvButtonStyle.Danger)).Controls;
            action = edit[0];
            del = edit[1];
            skip = edit[2];
            savedDelete = edit[3];
            savedDelete.Help = "Delete the loaded saved route (press twice).";
            ids.Add("plan.route.action", action);
            ids.Add("plan.route.del", del);
            ids.Add("plan.route.skip", skip);
            ids.Add("plan.route.saved.del", savedDelete);

            loopRow = new AvSegmented(flow.Content, "", new[] { "ONCE", "LOOP", "PING-PONG" }, () => loopValue, SetLoop);
            string[] loopTips = { "Fly the route once.", "Fly the route round and round.", "Fly the route back and forth." };
            string[] loopKeys = { "once", "round", "pingpong" };
            for (int i = 0; i < loopTips.Length; i++)
            {
                loopRow.Options[i].Help = loopTips[i];
                ids.Add("plan.route." + loopKeys[i], loopRow.Options[i]);
            }
            var sendButtons = new AvButtons(flow.Content, new[] { new AvControl.Spec("SEND TO WING", Send, AvButtonStyle.Primary, AvIcon.CircleCheck) });
            send = sendButtons.Controls[0];
            flow.Row(loopRow, sendButtons);
            // The 0.9 loop button cycled ONCE · LOOP · PING-PONG: its id still does.
            ids.Add("plan.route.loop", () => EditDraft(d => d.CycleLoop()));
            ids.Add("plan.route.send", send);

            apSection = flow.Section(AvIcon.Plane, "MY AUTOPILOT", "NAV · COUPLED TO ROUTE");
            apRow = flow.Add(new AvRow(flow.Content));
            apNav = apRow.AddTrailing(new AvControl.Spec("NAV", () => PressAp(5)));
            apOff = apRow.AddTrailing(new AvControl.Spec("AP OFF", () => PressAp(6), AvButtonStyle.Quiet));
            apMore = apRow.AddTrailing(new AvControl.Spec("MORE", ToggleApDetail, AvButtonStyle.Quiet));
            apNav.Help = "Fly the route you drew (or the scope's route when the draft is empty).";
            apOff.Help = "Autopilot off.";
            apMore.Help = "The autopilot's modes and held values.";
            ids.Add("plan.ap.nav", apNav);
            ids.Add("plan.ap.off", apOff);
            ids.Add("plan.ap.more", apMore);

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
            AvButtons modeButtons = flow.Buttons(modeSpecs);
            apDetail.Add(modeButtons);
            for (int i = 0; i < modes.Length; i++)
            {
                apModes[i] = modeButtons.Controls[i];
                apModes[i].Help = tips[i];
                // NAV and OFF answer on the annunciator row; the others here.
                if (i < 5) ids.Add("plan.ap." + keys[i], apModes[i]);
            }

            for (int i = 0; i < ApFields.Length; i++)
            {
                ApField f = ApFields[i];
                int slot = i;
                apSteppers[i] = flow.Add(new AvStepper(flow.Content, ApNames[i], () => apValues[slot],
                    () => PlayerAutopilot.Instance?.Adjust(f, -1), () => PlayerAutopilot.Instance?.Adjust(f, 1)));
                apDetail.Add(apSteppers[i]);
                string tip = ApNames[i] + ": the held value; the hold flies to it.";
                apSteppers[i].Minus.Help = apSteppers[i].Plus.Help = tip;
                string id = "plan.ap." + f.ToString().ToLowerInvariant();
                ids.Add(id + ".down", apSteppers[i].Minus);
                ids.Add(id + ".up", apSteppers[i].Plus);
            }
            ShowApDetail();
        }

        private void ToggleApDetail()
        {
            apOpen = !apOpen;
            ShowApDetail();
        }

        private void ShowApDetail()
        {
            foreach (AvPart part in apDetail) part.SetShown(apOpen);
            if (apMore != null) apMore.Latched = apOpen;
            flow.RequestRelayout();
        }

        /// <summary>A control id of the autopilot's modes or held values opens them first (automation).</summary>
        public void RevealFor(string id)
        {
            if (id == null || !id.StartsWith("plan.ap.", System.StringComparison.Ordinal) || id == "plan.ap.nav" || id == "plan.ap.off" || id == "plan.ap.more") return;
            if (apOpen) return;
            apOpen = true;
            ShowApDetail();
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
                WingToast.Show("Delete " + name + "? Press DEL SAVED again");
                return;
            }
            WmcRoutes.Store.Remove(savedSelected);
            WmcRoutes.Save();
            savedSelected = -1;
            savedPicker.Label = "SAVED ›";
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

        /// <summary>The route as <see cref="RouteData"/>: the draft's points while it has any (editable), else the scope's own path task
        /// (read-only, its leg marked). Bearings, distances and ETAs from the flight's position (the host's) through the points.</summary>
        private void Collect(WmcContext c, WingPlanner p)
        {
            data.Reset();
            RouteDraft d = c.Draft;
            Vec3 from = WmcMapInput.From(c, out float speed);
            bool haveFrom = c.Wing != null && !c.Client;
            Waypoint[] task = null;
            if (d.Count == 0 && p != null && p.Active && p.Current != null && p.Current.Points != null && p.Current.Points.Length > 0
                && (p.Current.Kind == TaskKind.Move || p.Current.Kind == TaskKind.Route || p.Current.Kind == TaskKind.Patrol))
                task = p.Current.Points;
            int n = task != null ? Mathf.Min(task.Length, RouteData.Max) : d.Count;
            data.Count = n;
            data.ReadOnly = task != null;
            data.Selected = task != null ? -1 : d.Selected;
            data.Current = task != null ? Mathf.Clamp(p.Leg, 0, n - 1) : -1;
            data.Closing = task != null ? p.Current.Kind == TaskKind.Patrol && p.Current.Loop && n > 2 : d.Loop == RouteLoop.Loop && n > 2;
            data.HaveFrom = haveFrom && task == null;
            data.FromX = from.X;
            data.FromZ = from.Z;
            if (p != null && p.Lead != null)
            {
                data.HaveLead = true;
                data.LeadX = p.Lead.Position.X;
                data.LeadZ = p.Lead.Position.Z;
                data.LeadHeading = p.Lead.HeadingDeg;
            }
            float px = from.X, pz = from.Z, cumulative = 0f, eta = 0f;
            bool havePrev = haveFrom;
            for (int i = 0; i < n; i++)
            {
                Waypoint wp = task != null ? task[i] : d[i];
                data.X[i] = wp.X;
                data.Z[i] = wp.Z;
                float legSpeed = float.IsNaN(wp.Speed) ? speed : wp.Speed;
                bool flown = task != null && i < data.Current;
                if (task != null && i == data.Current && data.HaveLead)
                {
                    px = data.LeadX;
                    pz = data.LeadZ;
                    havePrev = true;
                }
                if (havePrev)
                {
                    float dist = LegMath.Distance(px, pz, wp.X, wp.Z);
                    data.Brg[i] = AvNum.Fixed(LegMath.BearingDeg(px, pz, wp.X, wp.Z), 0).PadLeft(3, '0') + "°";
                    data.Dist[i] = flown ? "—" : WmcText.Km(dist);
                    if (!flown)
                    {
                        cumulative += dist;
                        eta += LegMath.EtaSeconds(dist, legSpeed);
                    }
                    data.Eta[i] = flown ? "—" : LegMath.Clock(eta);
                }
                else
                {
                    data.Brg[i] = data.Dist[i] = data.Eta[i] = "—";
                }
                data.Alt[i] = float.IsNaN(wp.Altitude) ? "AUTO" : AvNum.Fixed(wp.Altitude, 0);
                data.Spd[i] = float.IsNaN(wp.Speed) ? "AUTO" : AvNum.Fixed(wp.Speed * 3.6f, 0);
                data.At[i] = RouteDraft.ActionText(wp);
                px = wp.X;
                pz = wp.Z;
                havePrev = true;
            }
            routeKm = cumulative / 1000f;
        }

        private float routeKm;

        private void RefreshRoute(WmcContext c)
        {
            WingService w = c.Wing;
            WingPlanner p = w != null && !c.Client && w.Roster.InUse(c.ScopeElement) ? w.PlannerOf(c.ScopeElement) : null;
            RouteDraft d = c.Draft;
            draw.Latched = c.Map.Mode == MapMode.Route;
            draw.Interactable = c.CanOrder;
            Collect(c, p);
            Show(c, p, d);

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
            foreach (AvControl o in loopRow.Options) if (o.Interactable != any) o.Interactable = any;
            RefreshAutopilot();
        }

        /// <summary>Draws and lists <paramref name="data"/> (also the offline preview's entry: it fills the data by hand).</summary>
        internal void Show(WmcContext c, WingPlanner p, RouteDraft d)
        {
            map.Show(data, EmptyLegs);
            legs.Show(data);
            string note;
            if (data.Count == 0) note = EmptyLegs;
            else if (data.ReadOnly) note = "FLYING · LEG " + AvNum.Fixed(data.Current + 1, 0) + "/" + AvNum.Fixed(data.Count, 0) + " · " + AvNum.Fixed(routeKm, 0) + " KM";
            else note = AvNum.Fixed(data.Count, 0) + " PTS · " + AvNum.Fixed(routeKm, 0) + " KM · " + RouteDraft.LoopText(d.Loop);
            if (note != legsShown)
            {
                legsShown = note;
                legsSection.SetCaption(note);
            }
            long rows = data.Count * 31L + data.Selected + data.Current * 7 + (data.ReadOnly ? 1 : 0);
            for (int i = 0; i < data.Count; i++) rows = rows * 131 ^ (data.Brg[i]?.GetHashCode() ?? 0) ^ (data.Dist[i]?.GetHashCode() ?? 0) ^ (data.Eta[i]?.GetHashCode() ?? 0)
                ^ (data.Alt[i]?.GetHashCode() ?? 0) ^ (data.Spd[i]?.GetHashCode() ?? 0) ^ (data.At[i]?.GetHashCode() ?? 0);
            int rk = (int)(rows ^ (rows >> 32));
            if (rk != listKey)
            {
                listKey = rk;
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
            apModes[0].Latched = h.Lateral == LateralHold.Level;
            apModes[1].Latched = h.Lateral == LateralHold.Heading;
            apModes[2].Latched = h.Vertical == VerticalHold.Altitude;
            apModes[3].Latched = h.Vertical == VerticalHold.VerticalSpeed;
            apModes[4].Latched = h.Speed;
            apModes[5].Latched = h.Lateral == LateralHold.Nav;
            apModes[6].Latched = !ap.Session.Engaged;
            apNav.Latched = h.Lateral == LateralHold.Nav;
            apOff.Latched = !ap.Session.Engaged;
            int key = (int)h.Lateral * 7 + (int)h.Vertical * 3 + (h.Speed ? 1 : 0) + Mathf.RoundToInt(h.HeadingDeg) * 101
                + Mathf.RoundToInt(h.AltitudeM) * 1009 + Mathf.RoundToInt(h.VerticalSpeedMps * 10f) * 13 + Mathf.RoundToInt(h.SpeedMps) * 17
                + ap.Nav.Index * 5 + (ap.Session.LateralOverride ? 2 : 0) + (ap.Session.VerticalOverride ? 4 : 0);
            if (key == apKey) return;
            apKey = key;
            string text = WingHudText.Autopilot(h, ap.Session.LateralOverride, ap.Session.VerticalOverride, ap.Nav.Index, ap.Nav.Count);
            SetApNote(text.Length > 0 ? text : "AP OFF", text.Length > 0 ? AvState.Ready : AvState.Inert);
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

        private void SetApNote(string text, AvState state)
        {
            if (text == apNoteShown) return;
            apNoteShown = text;
            apRow.Set(text, "", "", state);
            flow.RequestRelayout();
        }
    }
}
