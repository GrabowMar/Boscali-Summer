using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    internal sealed partial class OrbitRoom : IFrontRoom
    {
        private const int TrackSteps = 80;
        private const float TrackHit = 17f, TimelineHeight = 202f;
        private static readonly Color[] PayloadInk = { RoomInk.Ice, RoomInk.Rgb(0x7BE9B3), RoomInk.Rgb(0xFFD17B) };
        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private readonly OrbitPass[] passes = new OrbitPass[FrontRules.BirdCap];
        private readonly bool[] hasPass = new bool[FrontRules.BirdCap];
        private readonly TMP_Text[] mapLabels = new TMP_Text[14];
        private readonly TMP_Text[] timelineStatus = new TMP_Text[FrontRules.BirdCap];
        private readonly AvControl[] timelineBirds = new AvControl[FrontRules.BirdCap];

        private FrontMapPlate plate;
        private FrontMap.AirbaseLayer airbases;
        private FrontBox situation, forecast;
        private FrontVector timeline;
        private TMP_Text watchLabel, aimLabel;
        private AvControl pickAim;
        private Rect watchStrip;
        private float timeX, timeW;
        private int mapLabelCount;
        private int selKind = -1, selectedTrackId;
        private bool relocating, hasPending, padPick, padMiss, launchStationPick, aimPick;
        private Vector2 pending, aim;
        private Vector2? launchStation, localAim;
        private SatelliteBus launchPreviewBus;
        private BirdKind launchPreviewKind;
        private FrontRoomView last;

        private bool pressed;
        private Vector2 pressAt, pressCenter;
        private float pressZoom;

        // DOWNLINK can take over the OPERATE column or enlarge over the map.
        private DownlinkPane docked, enlarged, shown;
        private ColumnTab[] columnTabs = Array.Empty<ColumnTab>();
        private int wantTab = -1;

        public OrbitRoom(IFrontActions actions, FrontSkin skin)
        {
            this.actions = actions;
            this.skin = skin;
        }

        private static Color Own => RoomInk.Ice;

        private static class Mc
        {
            public static readonly Color Go = RoomInk.Rgb(0x57E389), NoGo = RoomInk.Rgb(0xFF6B5E), Steel = RoomInk.Steel,
                PlateInk = RoomInk.Rgb(0x050C15), Lost = RoomInk.Rgb(0x6D7F99);
        }

        private ISpaceFeedActions Feed => actions.Orbit;
        private void Do(Action action) { actions.Touch(); if (Feed != null) action(); }

        public IReadOnlyList<ColumnTab> Column => columnTabs;
        public int TakeTab() { int tab = wantTab; wantTab = -1; return tab; }
        internal const int LaunchTab = 0, OperateTab = 1;

        public void Build(RectTransform hero, float w, float h, RectTransform column, float cw, float ch)
        {
            BuildSituation(hero, w, h - TimelineHeight - 8f);
            BuildForecast(hero, w, h - TimelineHeight);
            BuildLaunch(column, cw, ch);
            BuildOperate(column, cw, ch);
            docked = new DownlinkPane(this, operateRoot, cw, ch - 38f, false);
            enlarged = new DownlinkPane(this, hero, w, h, true);
            returnToOperate = Small(operateRoot, "RETURN TO WATCH", ConsoleInset, ch - 31f, cw - 2f * ConsoleInset,
                () => { actions.Touch(); CloseOperateDownlink(); }, "Return to the pass schedule and close the sensor feed.");
            Use(docked);
            Visible(docked.Box.Rect, false);
            Visible(returnToOperate.Rect, false);
            columnTabs = new[]
            {
                new ColumnTab("LAUNCH", AvIcon.Satellite, launch.Rect),
                new ColumnTab("OPERATE", AvIcon.Crosshair, operateRoot, operate.Rect),
            };
        }

        private void OpenOperateDownlink()
        {
            operateDownlink = true;
            Visible(operate.Rect, false);
            Visible(docked.Box.Rect, true);
            Visible(returnToOperate.Rect, true);
            docked.Box.Tagged("INCO");
        }

        internal void CloseOperateDownlink()
        {
            if (!operateDownlink) return;
            operateDownlink = false;
            Dock();
            Visible(docked.Box.Rect, false);
            Visible(returnToOperate.Rect, false);
            Visible(operate.Rect, true);
        }

        private void Use(DownlinkPane pane)
        {
            shown = pane;
            bool map = pane == docked;
            Visible(situation.Rect, map);
            Visible(forecast.Rect, map);
            Visible(enlarged.Box.Rect, !map);
            docked.Stub(!map);
            pane.Box.Tagged("INCO");
        }

        internal void Dock() { if (Enlarged) Use(docked); }
        internal bool Enlarged => shown == enlarged;

        internal void ToggleCamera()
        {
            actions.Touch();
            if (shown != enlarged && Picking) { EndPicks(); Repaint(); }
            Use(shown == enlarged ? docked : enlarged);
        }

        private void BuildSituation(RectTransform hero, float width, float height)
        {
            situation = new FrontBox(hero, skin, "Orbit", 0f, 0f, width, height, "GROUND TRACKS", AvIcon.Satellite, FrontLook.Console);
            plate = new FrontMapPlate(situation.Rect, skin, width, height, 8, 6) { Tint = RoomInk.Rgb(0x627781), TopInset = 38f };
            watchStrip = new Rect(plate.X + 12f, plate.Y + 5f, plate.W - 24f, 29f);
            plate.Labels.Fixed(watchStrip);
            watchLabel = ConsoleReadout(situation.Rect, "MissionWatch", watchStrip.x + 12f, watchStrip.y + 4f, watchStrip.width - 24f, true);
            for (int i = 0; i < mapLabels.Length; i++)
            {
                mapLabels[i] = ConsoleReadout(situation.Rect, "TrackLabel" + i, 0f, 0f, 200f, true);
                mapLabels[i].gameObject.SetActive(false);
            }
            airbases = new FrontMap.AirbaseLayer(situation.Rect);
        }

        private void BuildForecast(RectTransform hero, float width, float y)
        {
            forecast = new FrontBox(hero, skin, "PassForecast", 0f, y, width, TimelineHeight, "PASS FORECAST", AvIcon.RadarDish, FrontLook.Console);
            RectTransform root = forecast.Rect;
            timeline = FrontVector.Add(root, "PassTimeline");
            AvLay.Place(timeline.rectTransform, 0f, 0f, width, TimelineHeight);
            aimLabel = ConsoleReadout(root, "ForecastAim", 16f, 34f, 400f, true);
            pickAim = Small(root, "PICK GRID", width - 143f, 32f, 127f, ArmAimPick, "Select a theatre grid to forecast sensor passes over it.");
            timeX = 156f;
            timeW = width - timeX - 176f;
            for (int i = 0; i < FrontRules.BirdCap; i++)
            {
                int kind = i;
                float rowY = 77f + 35f * i;
                timelineBirds[i] = Small(root, FrontViews.BirdCallsign(i), 14f, rowY - 8f, 126f, () => InspectBird(kind),
                    "Select this satellite for pass and sensor controls.", PayloadInk[i]);
                timelineStatus[i] = ConsoleReadout(root, "PassState" + i, timeX + timeW + 12f, rowY - 6f, 151f);
            }
            for (int i = 0; i <= 6; i++)
            {
                TMP_Text tick = ConsoleReadout(root, "TimeTick" + i, timeX + timeW * i / 6f, 52f, 64f);
                tick.text = i == 0 ? "NOW" : "+" + i + " M";
                skin.Add(() => tick.color = Mc.Steel);
            }
        }

        private AvControl Small(RectTransform root, string label, float x, float y, float w, Action click, string help, Color? accent = null)
        {
            AvControl control = AvControl.Make(root, new AvControl.Spec(label, click, AvButtonStyle.Default));
            control.Accent = accent ?? Own;
            AvLay.Place(control.Rect, x, y, w, 24f);
            control.SingleLine();
            control.Help = help;
            return control;
        }

        private AvControl Prominent(RectTransform root, string label, float x, float y, float w, float h, Action click, string help, Color ink)
        {
            var control = AvControl.Make(root, new AvControl.Spec(label, click, AvButtonStyle.Primary));
            control.Accent = ink;
            AvLay.Place(control.Rect, x, y, w, h);
            control.SingleLine();
            control.Help = help;
            return control;
        }

        private void Repaint() { if (last != null) Paint(last); }

        private void Select(int kind)
        {
            actions.Touch();
            if (!TryBird(kind, out OrbitTrackView bird) || !bird.Alive) return;
            if (selKind != kind) { relocating = hasPending = false; retimeDraft = null; }
            selectedTrackId = confirmTrackId = 0;
            selKind = kind;
            Repaint();
        }

        private void InspectBird(int kind)
        {
            if (!TryBird(kind, out OrbitTrackView bird) || !bird.Alive) return;
            EndPicks();
            CloseOperateDownlink();
            Select(kind);
            wantTab = OperateTab;
        }

        private void ShowFeed(int kind)
        {
            if (!TryBird(kind, out OrbitTrackView bird) || !bird.Alive) return;
            EndPicks();
            Select(kind);
            wantTab = OperateTab;
            OpenOperateDownlink();
            Do(() => Feed.SetSource((BirdKind)kind));
        }

        private void StartRelocate(int kind)
        {
            actions.Touch();
            if (!CanRelocate(kind)) return;
            CloseOperateDownlink();
            EndPicks();
            selKind = kind;
            selectedTrackId = 0;
            relocating = true;
            Repaint();
        }

        private bool Picking => padPick || launchStationPick || relocating || aimPick;

        private void ArmAimPick()
        {
            actions.Touch();
            bool arm = !aimPick;
            EndPicks();
            aimPick = arm;
            Repaint();
        }

        private void ArmLaunchStation()
        {
            actions.Touch();
            bool arm = !launchStationPick;
            EndPicks();
            launchStationPick = arm;
            Repaint();
        }

        private void ArmPadPick()
        {
            actions.Touch();
            bool arm = !padPick;
            EndPicks();
            padPick = arm;
            Repaint();
        }

        public void EndPicks()
        {
            padPick = padMiss = launchStationPick = relocating = hasPending = aimPick = false;
            confirmTrackId = 0;
            retimeDraft = null;
        }

        internal void CloseWatch()
        {
            CloseOperateDownlink();
            EndPicks();
            launchStation = localAim = null;
            selectedTrackId = 0;
            selKind = -1;
            ReleaseDownlink();
        }

        public bool CancelMode()
        {
            if (Enlarged) { ToggleCamera(); return true; }
            if (!Picking && !retimeDraft.HasValue) return false;
            actions.Touch();
            EndPicks();
            Repaint();
            return true;
        }

        private void Cancel()
        {
            actions.Touch();
            relocating = hasPending = false;
            Repaint();
        }

        private void ShowSite(Vector2 uv)
        {
            actions.Touch();
            plate.View.CenterOn(uv);
            Repaint();
        }

        private bool TryBird(int kind, out OrbitTrackView bird)
        {
            if (last != null)
                foreach (OrbitTrackView item in last.Birds)
                    if (item.Own && (int)item.Kind == kind) { bird = item; return true; }
            bird = default;
            return false;
        }

        private bool CanRelocate(int kind) => last != null && kind >= 0 && kind < last.Fleet.Count && last.Fleet[kind].CanRelocate;

        private bool PreviewNode(in OrbitTrackView bird, out GeoBird result)
        {
            result = default;
            return relocating && hasPending && bird.Alive &&
                GeoSpace.PlanBurn(bird.Geo, last.Now, pending.x, pending.y, out result) == GeoSpace.Refusal.None;
        }

        private void Confirm()
        {
            actions.Touch();
            if (selKind < 0 || !TryBird(selKind, out OrbitTrackView bird) || !PreviewNode(bird, out _)) return;
            actions.RelocateBird(selKind, pending.x, pending.y);
            relocating = hasPending = false;
            Repaint();
        }

        private void PickPad(Vector2 uv)
        {
            actions.Touch();
            MapFrame frame = FrontController.Frame();
            float radius = FrontRules.PadPickMetres * FrontRules.PadPickMetres;
            bool near = false;
            foreach (MapAirbaseView basePoint in last.Airbases)
            {
                if (basePoint.Side != AirbaseSide.Own) continue;
                float x = (uv.x - basePoint.U) * frame.W, z = (uv.y - basePoint.V) * frame.H;
                if (x * x + z * z <= radius) { near = true; break; }
            }
            padMiss = !near;
            if (near && actions.Queue(Front.Space, ProgrammeId.UplinkSite, 0, true, uv.x, uv.y)) padPick = false;
        }

        private void ComputeForecast(FrontRoomView view)
        {
            aim = localAim ?? (view.HasAim ? new Vector2(view.AimU, view.AimV) :
                view.HasFocus ? new Vector2(view.FocusU, view.FocusV) : new Vector2(0.5f, 0.5f));
            Array.Clear(hasPass, 0, hasPass.Length);
            foreach (OrbitTrackView bird in view.Birds)
            {
                if (!bird.Own || !bird.Alive) continue;
                int k = (int)bird.Kind;
                hasPass[k] = bird.Geo.NextPass(view.Now, aim.x, aim.y, bird.Radius, out passes[k]);
            }
        }

        public void Paint(FrontRoomView view)
        {
            last = view;
            if (!TryBird(selKind, out OrbitTrackView selected) || !selected.Alive)
            {
                selKind = -1;
                relocating = hasPending = false;
                retimeDraft = null;
                foreach (OrbitTrackView bird in view.Birds)
                    if (bird.Own && bird.Alive) { selKind = (int)bird.Kind; break; }
            }
            if (selectedTrackId > 0 && !TryHostile(view, selectedTrackId, out _)) selectedTrackId = 0;
            ComputeForecast(view);
            if (!Enlarged) { PaintSituation(view); PaintForecast(view); }
            PaintLaunch(view);
            PaintOperate(view);
#if !PUBLIC_RELEASE
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            if (docked.Visible) docked.Paint(view);
            if (enlarged.Visible) enlarged.Paint(view);
#if !PUBLIC_RELEASE
            PaneCost.Paint(t0);
#endif
        }

        private void PaintSituation(FrontRoomView view)
        {
            plate.Begin();
            FrontVector g = plate.G;
            FrontMapView map = plate.View;
            Vector2 Point(float u, float v) => map.P(u, v);
            mapLabelCount = 0;
            g.Rect(map.X, map.Y, map.W, map.H, RoomInk.Rgb(0x061019, 0.28f));
            foreach (OrbitTrackView bird in view.Birds)
                if (bird.Alive) ReserveMarker(Point(bird.U, bird.V));
            foreach (OrbitSiteView site in view.Sites) ReserveMarker(Point(site.X, site.Y));
            ReserveMarker(Point(aim.x, aim.y));

            FrontMap.AirbaseLayer.Glyphs(g, view.Airbases, Point);
            foreach (MapAirbaseView point in view.Airbases)
                if (point.Side == AirbaseSide.Own && padPick)
                    g.Ring(Point(point.U, point.V), FrontRules.PadPickMetres * map.Scale / FrontController.Frame().W, 1.5f, Own, 32, 4f, 3f);
            foreach (OrbitTrackView bird in view.Birds)
                if (bird.Own && bird.Alive)
                    DrawTrack(g, bird.Geo, view.Now, PayloadInk[(int)bird.Kind], selKind == (int)bird.Kind);

            foreach (OrbitSiteView site in view.Sites)
            {
                Vector2 point = Point(site.X, site.Y);
                Color ink = site.Live ? Own : Mc.Lost;
                g.Tri(point + new Vector2(0f, -6f), point + new Vector2(6f, 5f), point + new Vector2(-6f, 5f), ink.WithAlpha(0.8f));
                MapLabel(point, site.Name, ink);
            }
            foreach (MapGhostView ghost in view.Ghosts)
            {
                Vector2 point = Point(ghost.U, ghost.V);
                FrontKit.Cross(g, point, 6f, Own.WithAlpha(0.55f));
                MapLabel(point, ghost.Label, Mc.Steel);
            }
            foreach (OrbitTrackView bird in view.Birds)
                if (bird.Alive) DrawBird(g, bird);

            Vector2 gate = Point(aim.x, aim.y);
            Color gateInk = RoomInk.Telemetry;
            g.Ring(gate, 10f, 1.6f, gateInk, 24, 2f, 2f);
            FrontKit.Cross(g, gate, 5f, gateInk);
            MapLabel(gate, "PASS GRID", gateInk);

            if (launch.Rect.gameObject.activeInHierarchy && launchStation.HasValue && LaunchDraft(view))
            {
                Vector2 node = Point(launchStation.Value.x, launchStation.Value.y);
                Color ink = PayloadInk[(int)launchPreviewKind];
                GeoBird insertion = GeoOrbit.Launch(launchPreviewKind, launchPreviewBus, launchStation.Value.x, launchStation.Value.y, view.Now);
                DrawTrack(g, insertion, view.Now, ink, false);
                FrontKit.Cross(g, node, 13f, ink);
                MapLabel(node, "INSERTION NODE", ink);
            }
            if (TryBird(selKind, out OrbitTrackView focused) && PreviewNode(focused, out GeoBird proposed))
            {
                DrawTrack(g, proposed, Mathf.Max(view.Now, proposed.ArrivalAt), RoomInk.Telemetry, true);
                Vector2 node = Point(pending.x, pending.y);
                FrontKit.Cross(g, node, 11f, RoomInk.Telemetry);
                MapLabel(node, "PROPOSED NODE", RoomInk.Telemetry);
            }
            if (selKind >= 0 && hasPass[selKind] && TryBird(selKind, out OrbitTrackView selected))
            {
                OrbitPass pass = passes[selKind];
                if (pass.StartAt > view.Now) DrawPassGate(g, selected.Geo, pass.StartAt, "AOS");
                DrawPassGate(g, selected.Geo, pass.EndAt, "LOS");
                if (selected.Booked && selected.BookedAt >= view.Now)
                {
                    Vector2 booked = Point(selected.BookedU, selected.BookedV);
                    g.Diamond(booked, 14f, Mc.PlateInk);
                    g.Diamond(booked, 8f, RoomInk.Telemetry);
                    MapLabel(booked, "COLLECTION", RoomInk.Telemetry);
                }
            }
            if (OperateDownlinkShown && HasPicture(view.Feed) && view.Feed.HasLook) DrawLook(g, view.Feed, map);

            for (int i = mapLabelCount; i < mapLabels.Length; i++) Visible(mapLabels[i].rectTransform, false);
            airbases.Names(g, view.Airbases, Point, map, (text, pos) => plate.Labels.Place(text, pos, 9f, true, true));
            PaintWatch(view);
            plate.End();
            situation.SetMeta(view.Theatre, Mc.Steel);
        }

        private void DrawTrack(FrontVector g, in GeoBird geo, float now, Color ink, bool selected)
        {
            float period = Mathf.Max(1f, geo.PeriodSeconds);
            float step = period / TrackSteps, start = now - period * 0.19f;
            Vector2 previous = TrackPoint(geo, start);
            for (int i = 1; i <= TrackSteps; i++)
            {
                float time = start + i * step;
                Vector2 next = TrackPoint(geo, time);
                bool past = time <= now;
                bool samePath = (time < geo.ArrivalAt || time - step >= geo.ArrivalAt || geo.Duration <= 0f) &&
                    (next - previous).sqrMagnitude < 0.3f * 0.3f * plate.View.Scale * plate.View.Scale;
                if (samePath && (past || i % 3 != 0))
                    g.Line(previous, next, selected ? 2f : 1.1f, ink.WithAlpha(past ? 0.23f : selected ? 0.82f : 0.33f));
                previous = next;
            }
        }

        private void DrawPassGate(FrontVector g, in GeoBird geo, float time, string word)
        {
            Vector2 point = TrackPoint(geo, time);
            if (!plate.View.Visible(point, 7f)) return;
            g.Ring(point, 5f, 1.5f, RoomInk.Telemetry, 16);
            MapLabel(point, word, RoomInk.Telemetry);
        }

        private Vector2 TrackPoint(in GeoBird geo, float time)
        {
            geo.Project(time, out float u, out float v);
            return plate.P(u, v);
        }

        private void DrawBird(FrontVector g, in OrbitTrackView bird)
        {
            Vector2 point = plate.P(bird.U, bird.V);
            if (!plate.View.Visible(point, 5f)) return;
            bool selected = bird.Own ? (int)bird.Kind == selKind : bird.TrackId == selectedTrackId;
            Color ink = bird.Own ? PayloadInk[(int)bird.Kind] : AvInk.Hostile;
            if (bird.Own)
            {
                Vector2 future = plate.P(bird.Geo.U(last.Now + 2f), bird.Geo.V(last.Now + 2f));
                Vector2 tangent = (future - point).normalized, cross = new Vector2(-tangent.y, tangent.x);
                g.Tri(point + tangent * 11f, point - tangent * 6f + cross * 6f, point - tangent * 6f - cross * 6f, ink);
                plate.Dot(bird.U, bird.V, ink);
            }
            else g.Diamond(point, 6f, ink);
            if (selected) g.Frame(point.x - 13f, point.y - 13f, 26f, 26f, 1.5f, ink);
            MapLabel(point, bird.Callsign, ink);
        }

        private void PaintWatch(FrontRoomView view)
        {
            string word = view.IncomingBird >= 0 ? AsatCountdown(view)
                : aimPick ? "SELECT THE GRID TO WATCH"
                : launchStationPick ? "SELECT THE CROSSING FOR THE INSERTION TRACK"
                : padPick ? padMiss ? "SITE OUTSIDE HELD AIRBASE RING" : "SELECT LAUNCH PAD SITE"
                : relocating ? hasPending ? "REVIEW THE NEW TRACK BEFORE EXECUTING" : "SELECT A NEW GROUND-TRACK NODE"
                : selKind >= 0 && hasPass[selKind]
                    ? FrontViews.BirdCallsign(selKind) + "   /   " + PassClock(passes[selKind], view.Now)
                    : "PICK A GRID TO FORECAST OVERFLIGHT";
            Color ink = view.IncomingBird >= 0 ? view.IncomingClear ? Mc.Go : Mc.NoGo : Picking ? RoomInk.Telemetry : Own;
            plate.G.Rect(watchStrip.x, watchStrip.y, watchStrip.width, watchStrip.height, Mc.PlateInk.WithAlpha(0.88f));
            Readout(watchLabel, word, ink);
        }

        private void PaintForecast(FrontRoomView view)
        {
            MapFrame frame = FrontController.Frame();
            Readout(aimLabel, "GRID " + OpsKit.Grid(frame.X(aim.x), frame.Z(aim.y)), RoomInk.Telemetry);
            pickAim.Latched = aimPick;
            timeline.Clear();
            for (int i = 0; i <= 6; i++)
            {
                float x = timeX + timeW * i / 6f;
                timeline.Line(new Vector2(x, 73f), new Vector2(x, 180f), i == 0 ? 1.6f : 0.8f, i == 0 ? Own : RoomInk.Bezel);
            }
            for (int i = 0; i < passes.Length; i++)
            {
                bool live = Fleet(view, i).Up;
                float y = 83f + i * 35f;
                Color ink = PayloadInk[i];
                timelineBirds[i].Latched = i == selKind;
                OpsKit.Enable(timelineBirds[i], live);
                timeline.Rect(timeX, y, timeW, 5f, RoomInk.Bezel.WithAlpha(0.75f));
                string status = live ? "NO PASS IN VIEW" : Fleet(view, i).State;
                if (hasPass[i] && TryBird(i, out OrbitTrackView bird))
                {
                    OrbitPass pass = passes[i];
                    status = PassClock(pass, view.Now);
                    for (int passIndex = 0; passIndex < 3 && pass.StartAt < view.Now + 360f; passIndex++)
                    {
                        float a = TimelinePosition(pass.StartAt, view.Now), b = TimelinePosition(pass.EndAt, view.Now);
                        if (b > a) timeline.Rect(a, y - 3f, Mathf.Max(3f, b - a), 11f, ink.WithAlpha(i == selKind ? 1f : 0.64f));
                        if (!bird.Geo.NextPass(pass.EndAt + 0.05f, aim.x, aim.y, bird.Radius, out pass)) break;
                    }
                    if (bird.Booked && bird.BookedAt >= view.Now && bird.BookedAt < view.Now + 360f)
                        timeline.Diamond(new Vector2(TimelinePosition(bird.BookedAt, view.Now), y + 2f), 6f, RoomInk.Telemetry);
                }
                Readout(timelineStatus[i], status, hasPass[i] ? ink : Mc.Steel);
            }
            timeline.Flush();
        }

        private float TimelinePosition(float time, float now) => timeX + timeW * Mathf.Clamp01((time - now) / 360f);

        private static string PassClock(in OrbitPass pass, float now) => pass.StartAt <= now
            ? "LIVE · LOS " + FrontKit.Clock(pass.EndAt - now)
            : "AOS " + FrontKit.Clock(pass.StartAt - now);

        private static string AsatCountdown(FrontRoomView view) =>
            FrontViews.BirdCallsign(view.IncomingBird) + " · " +
            (view.IncomingClear ? "CONJUNCTION CLEAR" : view.IncomingLaunched
                ? "CONJUNCTION IN " + FrontKit.Clock(view.IncomingPassSeconds)
                : "LAUNCH IN " + FrontKit.Clock(view.IncomingSeconds) + "  /  CONJUNCTION " + FrontKit.Clock(view.IncomingPassSeconds));

        private void ReserveMarker(Vector2 point) => plate.Labels.Take(new Rect(point.x - 10f, point.y - 10f, 20f, 20f));

        private void MapLabel(Vector2 point, string word, Color ink)
        {
            if (mapLabelCount >= mapLabels.Length || !plate.View.Visible(point, 6f)) return;
            TMP_Text label = mapLabels[mapLabelCount];
            Visible(label.rectTransform, true);
            FrontKit.Set(label, word, ink);
            Rect box = plate.Labels.Place(label, point, 13f, true, true);
            if (box.width <= 0f) { Visible(label.rectTransform, false); return; }
            plate.G.Rect(box.x - 1f, box.y, box.width + 2f, box.height, Mc.PlateInk.WithAlpha(0.89f));
            mapLabelCount++;
        }

        private bool Local(Vector2 mouse, out Vector2 point)
        {
            point = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(situation.Rect, mouse, null, out Vector2 local)) return false;
            point = new Vector2(local.x - situation.Rect.rect.xMin, situation.Rect.rect.yMax - local.y);
            return true;
        }

        private bool OnMap(Vector2 point) =>
            plate.View.Visible(point, 0f) && !plate.Labels.Blocked(new Rect(point.x - 1f, point.y - 1f, 2f, 2f));

        private bool TryTrackAt(Vector2 point, out OrbitTrackView selected)
        {
            selected = default;
            float nearest = TrackHit * TrackHit;
            foreach (OrbitTrackView bird in last.Birds)
            {
                if (!bird.Alive) continue;
                float distance = (plate.P(bird.U, bird.V) - point).sqrMagnitude;
                if (distance > nearest) continue;
                nearest = distance;
                selected = bird;
            }
            return selected.Alive;
        }

        public bool Tick(Vector2 mouse)
        {
            float wall = Time.unscaledTime;
            if (last != null && last.Feed != null && (docked.Visible || enlarged.Visible)) WatchAcquire(last.Feed, wall);
#if !PUBLIC_RELEASE
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            docked.Tick(mouse, wall);
            enlarged.Tick(mouse, wall);
#if !PUBLIC_RELEASE
            PaneCost.Tick(t0);
#endif
            FrontMapView map = plate.View;
            Vector2 center = map.Center;   // the view as the press found it: a press on the inset overview moves it, and that is no click on the map
            float zoom = map.Zoom;
            if (Enlarged) { pressed = false; return false; } // the camera covers the map: no pan, zoom or placement under it
            bool changed = plate.Tick(mouse, Picking);
            if (last == null) { pressed = false; return false; }
            if (Picking && plate.RightClick(mouse, out Vector2 right)) { PickPoint(right); return true; }
            if (FrontPointer.Down(0))
            {
                pressed = Local(mouse, out pressAt) && OnMap(pressAt);
                pressCenter = center;
                pressZoom = zoom;
            }
            if (pressed && (plate.DraggedLeft || FrontPointer.Held(1) || FrontPointer.Held(2))) pressed = false;   // a pan gesture is no click
            if (pressed && FrontPointer.Up(0))
            {
                pressed = false;
                if (Local(mouse, out Vector2 point) && OnMap(point) &&
                    (point - pressAt).sqrMagnitude <= FrontMapPlate.ClickSlop * FrontMapPlate.ClickSlop && Mathf.Abs(map.Zoom - pressZoom) <= 1e-4f &&
                    (map.Center - pressCenter).sqrMagnitude <= 1e-8f)
                {
                    if (Picking) { PickPoint(map.UV(point)); return true; }
                    if (TryTrackAt(point, out OrbitTrackView track))
                    {
                        if (track.Own) InspectBird((int)track.Kind);
                        else
                        {
                            actions.Touch();
                            CloseOperateDownlink();
                            selectedTrackId = track.TrackId;
                            wantTab = OperateTab;
                        }
                        return true;
                    }
                }
            }
            if (changed) PaintSituation(last);
            return false;
        }

        private void PickPoint(Vector2 point)
        {
            if (padPick) { PickPad(point); return; }
            actions.Touch();
            if (aimPick)
            {
                localAim = point;
                aimPick = false;
                wantTab = OperateTab;
                CloseOperateDownlink();
                return;
            }
            Vector2 node = new Vector2(GeoBird.ClampStation(point.x), GeoBird.ClampStation(point.y));
            if (launchStationPick) { launchStation = node; launchStationPick = false; }
            else { pending = node; hasPending = true; }
        }

        public BoscaliSummer.Core.Ui.CursorShape Cursor(Vector2 mouse) => Enlarged ? enlarged.Cursor(mouse) : plate.Cursor(mouse, Picking);
    }
}
