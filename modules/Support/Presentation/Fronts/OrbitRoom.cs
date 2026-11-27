using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>Launch planning and satellite operations share one theatre map and one set of map picks.</summary>
    internal sealed partial class OrbitRoom : IFrontRoom
    {
        private const float BirdPickPx = 19f, WatchHeight = 30f;
        private const int MaxMapLabels = 18;
        private static readonly Color[] PayloadInk = { RoomInk.Ice, RoomInk.Rgb(0x7BE9B3), RoomInk.Rgb(0xFFD17B) };
        private float mapW = 880f, mapH = 502f;

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private FrontMapPlate plate;

        private FrontBox situation;
        private FrontMap.AirbaseLayer airbases;
        private readonly TMP_Text[] mapLabels = new TMP_Text[MaxMapLabels];
        private TMP_Text watchLabel, footLabel;
        private Rect watchStrip, mapFooter;
        private int mapLabelCount;

        private int selKind = -1, selectedTrackId;
        private bool relocating, hasPending, padPick, padMiss, launchStationPick;
        private Vector2 pending;
        private Vector2? launchStation;
        private SatelliteBus launchPreviewBus;
        private BirdKind launchPreviewKind;
        private FrontRoomView last;

        // A map pick is a press and a release that stay together: a pan, a zoom or a drag in between places nothing.
        private bool pressed;
        private Vector2 pressAt, pressCenter;
        private float pressZoom;

        // DOWNLINK can take over the OPERATE column or enlarge over the map.
        private DownlinkPane docked, enlarged, shown;

        public OrbitRoom(IFrontActions actions, FrontSkin skin)
        {
            this.actions = actions;
            this.skin = skin;
        }

        /// <summary>Own side ink: ice cyan, the same on the map, the cards and the words.</summary>
        private static Color Own => Mc.Ice;

        /// <summary>The operations centre's fixed lamp, ice and engraving hues (a console lamp is the same colour in every pilot's scheme).</summary>
        private static class Mc
        {
            public static readonly Color Go = RoomInk.Rgb(0x57E389), NoGo = RoomInk.Rgb(0xFF6B5E), Steel = RoomInk.Steel, PlateInk = RoomInk.Rgb(0x050A18),
                Ice = RoomInk.Ice, Lost = RoomInk.Rgb(0x6D7F99);
        }

        private ISpaceFeedActions Feed => actions.Orbit;

        private void Do(Action a)
        {
            actions.Touch();
            if (Feed != null) a();
        }

        /// <summary>The room's column panels in tab order; the window adds the FRONT tab after them.</summary>
        public IReadOnlyList<ColumnTab> Column => columnTabs;
        /// <summary>The column tab a pick asked for since the last call, or -1.</summary>
        public int TakeTab() { int t = wantTab; wantTab = -1; return t; }
        internal const int LaunchTab = 0, OperateTab = 1;
        private ColumnTab[] columnTabs = new ColumnTab[0];
        private int wantTab = -1;

        public void Build(RectTransform hero, float w, float h, RectTransform column, float cw, float ch)
        {
            mapW = w; mapH = h;
            BuildSituation(hero, w);
            BuildLaunch(column, cw, ch);
            BuildOperate(column, cw, ch);
            docked = new DownlinkPane(this, operateRoot, cw, ch - 38f, false);
            enlarged = new DownlinkPane(this, hero, w, h, true); // over the map: the column stays (boxes on the picture lock targets)
            returnToOperate = Small(operateRoot, "BACK TO FLIGHT CONTROL", ConsoleInset, ch - 31f, cw - 2f * ConsoleInset, () => { actions.Touch(); CloseOperateDownlink(); },
                "Return to friendly and hostile tracks and leave the sensor feed.");
            Use(docked);
            Visible(docked.Box.Rect, false);
            Visible(returnToOperate.Rect, false);
            columnTabs = new[] { new ColumnTab("LAUNCH", AvIcon.Satellite, launch.Rect), new ColumnTab("OPERATE", AvIcon.Crosshair, operateRoot, operate.Rect) };
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

        /// <summary>Puts <paramref name="pane"/> on screen: the enlarged one hides the map, the docked one gives it back.</summary>
        private void Use(DownlinkPane pane)
        {
            shown = pane;
            bool dock = pane == docked;
            situation.Rect.gameObject.SetActive(dock); // the enlarged DOWNLINK lies over the map; the column tabs stay as they are
            enlarged.Box.Rect.gameObject.SetActive(!dock);
            docked.Stub(!dock); // one live picture: the column's DOWNLINK stands down to "ON MAIN SCREEN · DOCK" meanwhile
            pane.Box.Tagged("INCO"); // a box first shown now lays its header out now (TMP measures nothing while it is inactive)
        }

        /// <summary>The window closed: the next open starts with the camera docked.</summary>
        internal void Dock() { if (Enlarged) Use(docked); }

        /// <summary>True while the DOWNLINK has the whole room (the map takes no input then).</summary>
        internal bool Enlarged => shown == enlarged;

        /// <summary>ENLARGE / DOCK: the DOWNLINK grows over the map or docks back. The map goes under it, so a pick in progress ends first.</summary>
        internal void ToggleCamera()
        {
            actions.Touch();
            if (shown != enlarged && Picking) { EndPicks(); Repaint(); }
            Use(shown == enlarged ? docked : enlarged); // the pane just shown paints on its first Tick
        }

        // ---- Situation -----------------------------------------------------------------------------------------------

        private void BuildSituation(RectTransform hero, float heroW)
        {
            situation = new FrontBox(hero, skin, "Orbit", 0f, 0f, heroW, mapH, "MISSION TRACK", AvIcon.Satellite, FrontLook.Console).Tagged("FDO");
            RectTransform body = situation.Rect;
            plate = new FrontMapPlate(body, skin, mapW, mapH, 8, 6)
            {
                Tint = RoomInk.Rgb(0x597487),
                TopInset = WatchHeight + 8f
            };
            watchStrip = new Rect(plate.X + 12f, plate.Y + 7f, plate.W - 24f, WatchHeight);
            mapFooter = new Rect(plate.X + 245f, plate.Y + plate.H - 49f, Mathf.Max(90f, plate.W - 430f), 23f);
            plate.Labels.Fixed(watchStrip);
            plate.Labels.Fixed(mapFooter);
            watchLabel = FrontKit.Mono(body, "MissionWatch", watchStrip.x + 10f, watchStrip.y + 5f, watchStrip.width - 20f, 20f, 12f, TextAlignmentOptions.MidlineLeft, true);
            footLabel = FrontKit.Mono(body, "MapReadback", mapFooter.x + 7f, mapFooter.y + 3f, mapFooter.width - 14f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            for (int i = 0; i < mapLabels.Length; i++)
            {
                mapLabels[i] = FrontKit.Mono(body, "TrackLabel" + i, 0f, 0f, 224f, 15f, 10.5f, TextAlignmentOptions.TopLeft);
                mapLabels[i].gameObject.SetActive(false);
            }
            airbases = new FrontMap.AirbaseLayer(body);
        }

        private AvControl Small(RectTransform p, string label, float x, float y, float w, Action click, string help, Color? accent = null)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, AvButtonStyle.Default));
            c.Accent = accent ?? RoomInk.Ice;
            AvLay.Place(c.Rect, x, y, w, 24f);
            c.SingleLine();
            c.Help = help;
            return c;
        }

        /// <summary>Updates the map and command panels after a local selection changes.</summary>
        private void Repaint()
        {
            if (last == null) return;
            PaintSituation(last);
            PaintLaunch(last);
            PaintOperate(last);
        }

        /// <summary>A bird card clicked: the bird is inspected (cost rings on the map, the card lit). A relocation of another bird ends.</summary>
        private void Select(int kind)
        {
            actions.Touch();
            if (!TryBird(kind, out OrbitTrackView b) || !b.Alive) return;
            if (selKind != kind) { relocating = false; hasPending = false; }
            selectedTrackId = 0;
            selKind = kind;
            Repaint();
        }

        /// <summary>Opens the selected bird's sensor feed in OPERATE.</summary>
        private void ShowFeed(int kind)
        {
            EndPicks();
            if (!TryBird(kind, out OrbitTrackView bird) || !bird.Alive) return;
            Select(kind);
            wantTab = OperateTab;
            OpenOperateDownlink();
            Do(() => Feed.SetSource((BirdKind)kind));
        }

        private void StartRelocate(int kind)
        {
            actions.Touch();
            if (Enlarged) ToggleCamera(); // the proposal is a click on the map: it must be on screen
            if (!TryBird(kind, out OrbitTrackView b) || !b.Alive || !CanRelocate(kind)) return;
            CloseOperateDownlink();
            selKind = kind;
            selectedTrackId = 0;
            padPick = launchStationPick = false;
            relocating = true; hasPending = false;
            Repaint();
        }

        /// <summary>BUILD PAD: arms the pad pick (a second press disarms it). The map must be on screen, so an enlarged camera docks first.</summary>
        private void ArmPadPick()
        {
            actions.Touch();
            if (!padPick && Enlarged) ToggleCamera();
            padPick = !padPick; padMiss = false;
            launchStationPick = relocating = hasPending = false;
            Repaint();
        }

        private bool Picking => padPick || launchStationPick || relocating;

        private void ArmLaunchStation()
        {
            actions.Touch();
            launchStationPick = !launchStationPick;
            padPick = padMiss = relocating = hasPending = false;
            Repaint();
        }

        public void EndPicks()
        {
            padPick = padMiss = launchStationPick = relocating = hasPending = false;
            confirmTrackId = 0;
        }

        internal void CloseWatch()
        {
            CloseOperateDownlink();
            launchStation = null;
            selectedTrackId = 0;
            selKind = -1;
            ReleaseDownlink();
        }

        public bool CancelMode()
        {
            if (Enlarged) { ToggleCamera(); return true; } // Esc docks an enlarged camera before it closes the window
            if (!Picking) return false;
            actions.Touch();
            EndPicks();
            Repaint();
            return true;
        }

        private void Cancel()
        {
            actions.Touch();
            relocating = false; hasPending = false;
            Repaint();
        }

        /// <summary>A click on a pad card centres the map on it. Local: nothing is sent.</summary>
        private void ShowSite(Vector2 uv)
        {
            actions.Touch();
            plate.View.CenterOn(uv);
            Repaint();
        }

        private bool TryBird(int kind, out OrbitTrackView b)
        {
            if (last != null) foreach (OrbitTrackView t in last.Birds) if (t.Own && (int)t.Kind == kind) { b = t; return true; }
            b = default;
            return false;
        }

        /// <summary>The view's rule (alive, not burning, fuel for the smallest burn), read off the bird's fleet card.</summary>
        private bool CanRelocate(int kind) => last != null && kind >= 0 && kind < last.Fleet.Count && last.Fleet[kind].CanRelocate;

        private float PendingDistance(in OrbitTrackView b) => GeoBird.Distance(b.U, b.V, pending.x, pending.y);

        private float PendingCost(in OrbitTrackView b) => GeoBird.Cost(PendingDistance(b));

        private bool CanBurn(in OrbitTrackView b) => relocating && b.Alive && !b.Relocating && hasPending && PendingCost(b) > 0f && b.Fuel >= PendingCost(b);

        private void Confirm()
        {
            actions.Touch();
            if (selKind < 0 || !TryBird(selKind, out OrbitTrackView b) || !CanBurn(b)) return;
            actions.RelocateBird(selKind, pending.x, pending.y);
            relocating = false; hasPending = false;
            Repaint();
        }

        /// <summary>
        /// The pad pick's click: within <see cref="FrontRules.PadPickMetres"/> of a held airbase (measured on the ground in the theatre frame) it queues a LAUNCH PAD at
        /// the clicked point and disarms; anywhere else it says PICK A HELD AIRBASE and stays armed. The host judges the place again.
        /// </summary>
        private void PickPad(Vector2 uv)
        {
            actions.Touch();
            MapFrame f = FrontController.Frame();
            float reach = FrontRules.PadPickMetres * FrontRules.PadPickMetres;
            bool near = false;
            foreach (MapAirbaseView a in last.Airbases)
            {
                if (a.Side != AirbaseSide.Own) continue;
                float dx = (uv.x - a.U) * f.W, dz = (uv.y - a.V) * f.H;
                if (dx * dx + dz * dz <= reach) { near = true; break; }
            }
            padMiss = !near;
            if (!near) return;
            if (actions.Queue(Front.Space, ProgrammeId.UplinkSite, 0, true, uv.x, uv.y)) padPick = false; // not sent (a press still waits for the host): stays armed
        }

        private bool TryTrackAt(Vector2 p, out OrbitTrackView track)
        {
            track = default;
            float bestD = BirdPickPx * BirdPickPx;
            foreach (OrbitTrackView b in last.Birds)
            {
                if (!b.Alive) continue;
                float d = (BirdPoint(b) - p).sqrMagnitude;
                if (d <= bestD) { bestD = d; track = b; }
            }
            return track.Alive;
        }

        // ---- Paint ---------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            PaintSituation(v);
            PaintLaunch(v);
            PaintOperate(v);
            // Each pane on screen paints (enlarged, the column may still show the docked one); a hidden pane paints the frame it shows again.
#if !PUBLIC_RELEASE
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            if (docked.Visible) docked.Paint(v);
            if (enlarged.Visible) enlarged.Paint(v);
#if !PUBLIC_RELEASE
            PaneCost.Paint(t0);
#endif
        }

        private void PaintSituation(FrontRoomView v)
        {
            last = v;
            if (!TryBird(selKind, out OrbitTrackView selected) || !selected.Alive)
            {
                selKind = -1;
                relocating = hasPending = false;
                foreach (OrbitTrackView b in v.Birds)
                {
                    if (!b.Own || !b.Alive) continue;
                    selKind = (int)b.Kind;
                    selected = b;
                    break;
                }
            }
            plate.Begin();
            FrontVector g = plate.G;
            FrontMapView map = plate.View;
            Vector2 Point(float u, float y) => map.P(u, y);
            g.Rect(map.X, map.Y, map.W, map.H, RoomInk.Rgb(0x06121F, 0.32f));
            mapLabelCount = 0;

            // Reserve glyph positions before placing labels. The same projection is used for picking and drawing.
            bool trackFound = false;
            foreach (OrbitTrackView b in v.Birds)
            {
                ReserveMarker(BirdPoint(b));
                trackFound |= !b.Own && b.TrackId == selectedTrackId;
            }
            if (!trackFound) selectedTrackId = 0;
            foreach (OrbitSiteView s in v.Sites) ReserveMarker(Point(s.X, s.Y));
            if (v.HasFocus) ReserveMarker(Point(v.FocusU, v.FocusV));
            if (v.IncomingLaunched) ReserveMarker(Point(v.IncomingAimU, v.IncomingAimV));

            FrontMap.AirbaseLayer.Glyphs(g, v.Airbases, Point);
            foreach (MapAirbaseView a in v.Airbases)
            {
                if (a.Side != AirbaseSide.Own) continue;
                plate.Dot(a.U, a.V, Own);
                if (padPick)
                {
                    Vector2 p = Point(a.U, a.V);
                    float radius = Mathf.Max(6f, FrontRules.PadPickMetres / FrontController.Frame().W * map.Scale);
                    g.Disc(p, radius, Own.WithAlpha(0.16f), 36);
                    g.Ring(p, radius, 1.6f, Own, 36);
                }
            }

            foreach (OrbitTrackView b in v.Birds)
            {
                if (!b.Own || !b.Alive) continue;
                bool active = selKind == (int)b.Kind;
                Color ink = PayloadInk[(int)b.Kind];
                Vector2 p = BirdPoint(b);
                Coverage(g, p, b.Radius * map.Scale, ink, active ? 1f : 0.45f);
                if (!b.Relocating) continue;
                Vector2 dest = Point(b.TargetU, b.TargetV);
                g.Dashed(p, dest, active ? 2f : 1.2f, ink.WithAlpha(0.82f), 7f, 5f);
                Coverage(g, dest, b.Radius * map.Scale, ink, active ? 0.85f : 0.45f, true);
                g.Diamond(dest, 5f, ink);
                MapLabel(dest, "STATION IN " + FrontKit.Clock(b.EtaSeconds), ink);
            }

            foreach (OrbitSiteView s in v.Sites)
            {
                Vector2 p = Point(s.X, s.Y);
                Color ink = !s.Live ? Mc.Lost : s.Own ? Own : AvInk.Hostile;
                g.Tri(p + new Vector2(0f, -7f), p + new Vector2(7f, 6f), p + new Vector2(-7f, 6f), ink);
                if (s.Live) g.Ring(p, 11f, 1.1f, ink.WithAlpha(0.62f), 24);
                plate.Dot(s.X, s.Y, ink);
                MapLabel(p, s.Name + " · " + s.State, ink);
            }
            foreach (MapGhostView q in v.Ghosts)
            {
                Vector2 p = Point(q.U, q.V);
                if (q.Radius > 0f) Coverage(g, p, q.Radius * map.Scale, Own, 0.45f, true);
                g.Ring(p, 10f, 1.4f, Own, 24, 2f, 2f);
                MapLabel(p, q.Label, Own);
            }

            foreach (OrbitTrackView b in v.Birds)
            {
                Vector2 p = BirdPoint(b);
                bool picked = b.Own ? selKind == (int)b.Kind : selectedTrackId == b.TrackId;
                Color ink = !b.Alive ? Mc.Lost : b.Own ? PayloadInk[(int)b.Kind] : AvInk.Hostile;
                if (picked || !b.Own) g.Ring(p, 16f, picked ? 1.8f : 1.1f, ink, 36, 3f, 2f);
                g.Diamond(p, 10f, Mc.PlateInk);
                g.Diamond(p, 7f, ink);
                if (!b.Alive || !b.Own) g.Diamond(p, 4f, Mc.PlateInk);
                if (b.Own) plate.Dot(b.U, b.V, ink);
                string state = !b.Own ? "COARSE TRACK" : !b.Alive ? "LOST" : b.Relocating ? "IN TRANSFER" : Mathf.RoundToInt(b.Fuel) + "% FUEL";
                MapLabel(p, b.Callsign + "  ·  " + state, ink);
            }

            if (v.HasFocus)
            {
                Vector2 p = Point(v.FocusU, v.FocusV);
                g.Ring(p, 10f, 1.4f, AvInk.Key, 28);
                FrontKit.Cross(g, p, 5f, AvInk.Key);
                MapLabel(p, "FOCUS " + v.FocusGrid, AvInk.Key);
            }
            if (v.IncomingBird >= 0 && v.IncomingLaunched)
            {
                Vector2 aim = Point(v.IncomingAimU, v.IncomingAimV);
                Coverage(g, aim, AsatRules.Clearance * map.Scale, Mc.NoGo, 1f, true);
                FrontKit.Cross(g, aim, 8f, Mc.NoGo);
                MapLabel(aim, "INTERCEPT ZONE · CLEAR BY " + FrontKit.Clock(v.IncomingSeconds), Mc.NoGo);
            }
            if (OperateDownlinkShown && HasPicture(v.Feed) && v.Feed.HasLook) DrawLook(g, v.Feed, map);

            if (launchStation.HasValue && launch.Rect.gameObject.activeInHierarchy && !Fleet(v, (int)launchPreviewKind).Up &&
                !FrontPanels.LaunchRow(v, (int)launchPreviewKind, out _))
            {
                Vector2 p = Point(launchStation.Value.x, launchStation.Value.y);
                Color ink = PayloadInk[(int)launchPreviewKind];
                Coverage(g, p, GeoSpace.Footprint(launchPreviewKind, launchPreviewBus) * map.Scale, ink, 0.8f, true);
                FrontKit.Cross(g, p, 7f, ink);
                MapLabel(p, "INSERTION · " + GeoSpace.Names[(int)launchPreviewKind], ink);
            }
            if (relocating && hasPending && selected.Alive)
            {
                Vector2 p = Point(pending.x, pending.y);
                Color ink = CanBurn(selected) ? RoomInk.Telemetry : Mc.NoGo;
                g.Dashed(BirdPoint(selected), p, 2f, ink, 7f, 5f);
                Coverage(g, p, selected.Radius * map.Scale, ink, 0.95f, true);
                FrontKit.Cross(g, p, 7f, ink);
                float cost = PendingCost(selected);
                string quote = cost <= 0f ? "ALREADY ON STATION" : CanBurn(selected)
                    ? "-" + Mathf.CeilToInt(cost) + "% FUEL  ·  ETA " + FrontKit.Clock(PendingDistance(selected) / GeoBird.SpeedFor(selected.Bus))
                    : "NEEDS " + Mathf.CeilToInt(cost) + "% FUEL";
                MapLabel(p, quote, ink);
            }

            for (int i = mapLabelCount; i < mapLabels.Length; i++) mapLabels[i].gameObject.SetActive(false);
            airbases.Names(g, v.Airbases, Point, map, (t, at) => plate.Labels.Place(t, at, 9f, true, true));
            bool threat = v.IncomingBird >= 0;
            string watch = threat
                ? AsatCountdown(v)
                : "ORBIT  /  " + v.BirdsUp + " ACTIVE  ·  " + v.PadsUp + " UPLINK" + (v.PadsUp == 1 ? "" : "S");
            PaintMapStrip(g, watchStrip, watchLabel, watch, threat ? Mc.NoGo : Own.WithAlpha(0.95f));
            string instruction = padPick ? (padMiss ? "CHOOSE GROUND NEAR A HELD AIRBASE" : "SELECT LAUNCH PAD SITE")
                : launchStationPick ? "SELECT INITIAL SATELLITE STATION"
                : relocating ? (hasPending ? CoverageTradeoff(v, selected) : "SELECT NEW STATION")
                : v.CoverageGap;
            PaintMapStrip(g, mapFooter, footLabel, instruction, RoomInk.Telemetry);
            plate.End();
            situation.SetMeta(v.Theatre, AvInk.Dim);
        }

        private static string AsatCountdown(FrontRoomView v) =>
            "ASAT · " + FrontViews.BirdCallsign(v.IncomingBird) + " · " +
            (v.IncomingLaunched ? "IMPACT IN " : "LAUNCH IN ") + FrontKit.Clock(v.IncomingSeconds);

        private string CoverageTradeoff(FrontRoomView v, in OrbitTrackView bird)
        {
            if (v.HasFocus && bird.Overhead && GeoBird.Distance(pending.x, pending.y, v.FocusU, v.FocusV) > bird.Radius)
                return "FOCUS " + v.FocusGrid + " LEAVES " + bird.Callsign + " COVERAGE";
            foreach (MapAirbaseView airbase in v.Airbases)
            {
                if (airbase.Side != AirbaseSide.Own || GeoBird.Distance(bird.U, bird.V, airbase.U, airbase.V) > bird.Radius ||
                    GeoBird.Distance(pending.x, pending.y, airbase.U, airbase.V) <= bird.Radius) continue;
                return airbase.Name + " LEAVES " + bird.Callsign + " COVERAGE";
            }
            return "REVIEW BURN IN OPERATE";
        }

        private static void PaintMapStrip(FrontVector g, Rect box, TMP_Text label, string text, Color ink)
        {
            label.gameObject.SetActive(!string.IsNullOrEmpty(text));
            if (!label.gameObject.activeSelf) return;
            g.Rect(box.x, box.y, box.width, box.height, Mc.PlateInk.WithAlpha(0.96f));
            g.Rect(box.x, box.y, 3f, box.height, ink);
            FrontKit.Set(label, FrontKit.Fit(label, text, label.fontSize), ink);
        }

        private void ReserveMarker(Vector2 p) => plate.Labels.Take(new Rect(p.x - 11f, p.y - 11f, 22f, 22f));

        private void MapLabel(Vector2 p, string word, Color ink)
        {
            if (mapLabelCount >= mapLabels.Length || !plate.View.Visible(p, 8f)) return;
            TMP_Text label = mapLabels[mapLabelCount];
            label.gameObject.SetActive(true);
            FrontKit.Set(label, word, ink);
            Rect box = plate.Labels.Place(label, p, 13f, true, true);
            if (box.width <= 0f)
            {
                label.gameObject.SetActive(false);
                return;
            }
            plate.G.Rect(box.x - 2f, box.y, box.width + 4f, box.height, Mc.PlateInk.WithAlpha(0.91f));
            mapLabelCount++;
        }

        private static void Coverage(FrontVector g, Vector2 p, float radius, Color ink, float opacity, bool forecast = false)
        {
            if (radius < 1f) return;
            g.Disc(p, radius, ink.WithAlpha((forecast ? 0.037f : 0.11f) * opacity), 64);
            g.Ring(p, radius, forecast ? 1.3f : 1.65f, ink.WithAlpha(opacity), 72, forecast ? 4f : 0f, forecast ? 3f : 0f);
        }

        private Vector2 BirdPoint(in OrbitTrackView b) => plate.P(b.U, b.V);

        private bool Local(Vector2 mouse, out Vector2 p)
        {
            p = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(situation.Rect, mouse, null, out Vector2 lp)) return false;
            p = new Vector2(lp.x - situation.Rect.rect.xMin, situation.Rect.rect.yMax - lp.y);
            return true;
        }

        /// <summary>On the map plate and, with no pick armed, not under the legend or a name. An armed pick takes the click under a word too: the airbase it
        /// must land near wears its name, and a pick never misses in silence.</summary>
        private bool OnMap(Vector2 p)
        {
            FrontMapView mv = plate.View;
            return p.x >= mv.X && p.x <= mv.X + mv.W && p.y >= mv.Y && p.y <= mv.Y + mv.H && (Picking || !plate.Labels.Blocked(new Rect(p.x - 1f, p.y - 1f, 2f, 2f)));
        }

        /// <summary>
        /// One frame of map input. A pan, zoom or inset move repaints only the map here and returns false: the column does not depend on the view, so it is not
        /// repainted every frame of a drag. True only for a pick or a feed click, which change the column too.
        /// </summary>
        public bool Tick(Vector2 mouse)
        {
            // The DOWNLINK's own frame (zoom, pan, the acquire sequence): it updates its pane only and asks the window for nothing.
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
            FrontMapView mv = plate.View;
            Vector2 center = mv.Center;   // the view as the press found it: a press on the inset overview moves it, and that is no click on the map
            float zoomStep = mv.Zoom;
            if (Enlarged) { pressed = false; return false; } // the camera covers the map: no pan, zoom or placement under it
            bool changed = plate.Tick(mouse, Picking);
            if (last == null) { pressed = false; return false; }
            if (Picking && plate.RightClick(mouse, out Vector2 ruv))
            {
                PickPoint(ruv);
                return true;
            }
            if (FrontPointer.Down(0))
            {
                pressed = Local(mouse, out pressAt) && OnMap(pressAt);
                pressCenter = center; pressZoom = zoomStep;
            }
            if (pressed && (plate.DraggedLeft || FrontPointer.Held(1) || FrontPointer.Held(2))) pressed = false;   // a pan gesture is no click
            if (!pressed || !FrontPointer.Up(0)) return MapOnly(changed);
            pressed = false;
            // Only a click that stayed put on an unmoved, unzoomed map picks anything.
            if (!Local(mouse, out Vector2 p) || !OnMap(p) || (p - pressAt).sqrMagnitude > FrontMapPlate.ClickSlop * FrontMapPlate.ClickSlop || Mathf.Abs(mv.Zoom - pressZoom) > 1e-4f || (mv.Center - pressCenter).sqrMagnitude > 1e-8f) return MapOnly(changed);
            if (Picking)
            {
                PickPoint(mv.UV(p));
                return true;
            }
            if (!TryTrackAt(p, out OrbitTrackView track)) return MapOnly(changed);
            if (track.Own) ShowFeed((int)track.Kind);
            else
            {
                actions.Touch();
                CloseOperateDownlink();
                selectedTrackId = track.TrackId;
                wantTab = OperateTab;
            }
            return true;
        }

        private void PickPoint(Vector2 uv)
        {
            if (padPick) { PickPad(uv); return; }
            Vector2 station = new Vector2(GeoBird.ClampStation(uv.x), GeoBird.ClampStation(uv.y));
            if (launchStationPick)
            {
                launchStation = station;
                launchStationPick = false;
            }
            else
            {
                pending = station;
                hasPending = true;
            }
            actions.Touch();
        }

        /// <summary>The view moved: the map repaints now, the window repaints nothing else.</summary>
        private bool MapOnly(bool changed)
        {
            if (changed) PaintSituation(last);
            return false;
        }

        public BoscaliSummer.Core.Ui.CursorShape Cursor(Vector2 mouse) => Enlarged ? enlarged.Cursor(mouse) : plate.Cursor(mouse, Picking);
    }
}
