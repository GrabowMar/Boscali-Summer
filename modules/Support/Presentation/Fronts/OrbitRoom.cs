using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The SPACE front's room (R5, SPACE OPERATIONS CENTRE): deep-space navy glass, laid out like the NETWORK room. One big situation box: the theatre map
    /// (the whole of it is the picture) with a slim console column inside the same box. The map draws the real mechanics at theatre scale: every bird
    /// hangs over its parked point as a nadir marker, its footprint is drawn exactly as the coverage gate tests it (Reach as a share of the theatre, so a
    /// slightly flattened oval in kilometres), a burn is the dashed path to a ghost footprint, the selected bird's fuel-cost rings are the real burn cost
    /// (1.5 + 36 per theatre width, in percent) and a perk's aim point reads which birds cover it. The column carries the flight director (MET, the call,
    /// the GO / NO-GO poll), the hostile tally, the inspector with RELOCATE and the uplink sites. Under it, one row of panes: the live sensor picture under
    /// a camera burn-in (ENLARGE gives it the whole room, with its own MARK and TRANSMIT), and the track file with the SELECT, MARK, TRANSMIT chain.
    /// Mission-control flavour (MET, lamps, loop tags, burn-in lines, the radar sweep angle, the station-keeping boxes) is derived on the spot from fields the
    /// room already has: no new data, no new wire.
    /// </summary>
    internal sealed class OrbitRoom : IFrontRoom
    {
        private const float TopH = 502f, Gap = 8f, PW = 278f, ColumnW = PW + 12f; // the console column keeps its width; the map takes the rest of the hero
        private float mapW = 880f, colPx = 886f;
        private const float BarH = 100f, DragSlop = 6f, LegendX = 14f;
        private const float TrackW = 320f, ChainY = 328f, PicX = 8f, PicY = 60f;
        // The console column (box pixels): MET plate, three poll rows, the hostile tally, the inspector, the uplink sites.
        private const float FdY = 28f, FdH = 40f, RowsY = 72f, RowH = 48f, RowStep = 50f, HostY = 226f, HostH = 30f, InspY = 262f, SiteHeadY = 366f, SiteY0 = 384f;
        private const float SiteH = 30f, ChipH = 20f;
        private const int GridCols = 10, GridRows = 8, MaxBirdLabels = 8, MaxSiteLabels = 6, TrackRows = SpaceFeedRules.ContactsPerPage;
        private static readonly BirdKind[] Kinds = { BirdKind.Optical, BirdKind.Radar, BirdKind.Kinetic };
        private static readonly string[] KindTag = { "OPT", "RDR", "KIN" };
        private static readonly float[] ChainShare = { 0.30f, 0.30f, 0.40f };
        private static readonly float[] CostLevels = { 10f, 25f, 50f };

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private FrontMapPlate plate;

        // Situation: the map and the console column.
        private FrontBox situation;
        private FrontVector side, colG;
        private RawImage mapImage, mapInset;
        private FrontMap.AirbaseLayer airbases;
        private readonly TMP_Text[] birdLabels = new TMP_Text[MaxBirdLabels], reachLabels = new TMP_Text[MaxBirdLabels], siteLabels = new TMP_Text[MaxSiteLabels];
        private readonly TMP_Text[] costLabels = new TMP_Text[5], burnLabels = new TMP_Text[3], legend = new TMP_Text[7];
        private readonly float[] legendSwatch = new float[7];
        private TMP_Text focusLabel, propLabel;
        private readonly List<Rect> plates = new List<Rect>(24);
        private readonly List<Vector2> ringAt = new List<Vector2>(5);
        private readonly List<string> ringWord = new List<string>(5);
        private readonly List<Color> ringInk = new List<Color>(5);
        private readonly Rect[] legendRect = new Rect[1];

        // Flight director.
        private readonly PollRow[] ownCards = new PollRow[3];
        private readonly SiteCard[] siteCards = new SiteCard[2];
        private readonly SiteChip[] siteChips = new SiteChip[2];
        private TMP_Text fdLabel, fdMet, fdCap, fdCall, hostA, hostB, siteHeadOwn, siteHeadFoe;
        private Lamp callLamp;
        private string callWord = "";

        // The selected bird's inspector. Selecting is local; RELOCATE enters a local mode, a click on the map proposes the new parked point,
        // CONFIRM BURN sends the one RelocateBird request and CANCEL spends nothing.
        private int selKind = -1;
        private bool relocating, hasPending;
        private Vector2 pending;
        private FrontRoomView last;
        private Rect fuelRect;
        private TMP_Text selHead, selState, selLine, selFuelLabel, selFuel, selHint;
        private AvControl relocBtn, burnBtn, cancelBtn;

        // A destination is placed by a press and a release on the map that stay together: a pan, a zoom or a drag in between places nothing.
        private bool pressed;
        private Vector2 pressAt, pressCenter;
        private int pressZoom;

        // Sensor.
        // Sensor: the fields below belong to the camera pane on screen (Use swaps them between the docked and the enlarged one).
        private FrontBox sensor, trackBox;
        private AvControl optical, radar, zoom, sizeBtn;
        private OpsFeedPicture picture;
        private TMP_Text sensorNote;
        private FrontVector standby, burn;
        private TMP_Text burnTl, burnTl2, burnTr, burnBr;
        private float picW, picH;
        private bool standbyDrawn;
        private CamPane docked, enlarged, shown;

        /// <summary>One camera pane (docked under the map, or enlarged over it): its own box, toolbar, picture and burn-in layers.</summary>
        private sealed class CamPane
        {
            public FrontBox Box;
            public AvControl Optical, Radar, Zoom, Size, Mark, Send;
            public OpsFeedPicture Picture;
            public TMP_Text Note, Tl, Tl2, Tr, Br;
            public FrontVector Standby, Burn;
            public float W, H;
            public bool StandbyDrawn;
        }

        // Track file.
        private readonly TrackRow[] rows = new TrackRow[TrackRows];
        private AvControl prev, next, mark, send;
        private TMP_Text briefA, briefB, trackEmpty;
        private readonly TMP_Text[] chainText = new TMP_Text[3];
        private FrontVector chain;
        private float chainInner;
        private readonly int[] rowIds = new int[TrackRows];

        public OrbitRoom(IFrontActions actions, FrontSkin skin)
        {
            this.actions = actions;
            this.skin = skin;
        }

        /// <summary>Own side ink: ice cyan, the same on the map, the lamps and the words.</summary>
        private static Color Own => Mc.Ice;

        /// <summary>The operations centre's fixed lamp, ice and engraving hues (a console lamp is the same colour in every pilot's scheme).</summary>
        private static class Mc
        {
            public static readonly Color Go = RoomInk.Rgb(0x57E389), Hold = RoomInk.Telemetry, NoGo = RoomInk.Rgb(0xFF6B5E), Off = RoomInk.Rgb(0x142038),
                Steel = RoomInk.Rgb(0x9DB4D3), PlateInk = RoomInk.Rgb(0x050A18), Ice = RoomInk.Rgb(0x7FE6FF), Lost = RoomInk.Rgb(0x6D7F99);
        }

        /// <summary>A GO / NO-GO lamp: unlit (no telemetry), GO, caution, NO-GO. Ordered by severity so the worst of a row is its maximum.</summary>
        private enum Lamp : byte { Off, Go, Hold, NoGo }

        private static Color LampInk(Lamp l) => l == Lamp.Go ? Mc.Go : l == Lamp.Hold ? Mc.Hold : l == Lamp.NoGo ? Mc.NoGo : Mc.Off;

        private ISpaceFeedActions Feed => actions.Orbit;

        private void Do(Action a)
        {
            actions.Touch();
            if (Feed != null) a();
        }

        public void Build(RectTransform hero, float w, float h)
        {
            mapW = w - ColumnW; colPx = mapW + 6f;
            BuildSituation(hero, w);
            float by = TopH + Gap, bh = h - by, cw = w - TrackW - Gap;
            docked = BuildSensor(hero, 0f, by, cw, bh, false);
            BuildTracks(hero, cw + Gap, by, TrackW, bh);
            enlarged = BuildSensor(hero, 0f, 0f, w, h, true); // the whole room: the map and the track file hide while it is up (brackets on the picture select tracks)
            Use(docked);
        }

        /// <summary>Puts <paramref name="pane"/> on screen and points the sensor fields at it.</summary>
        private void Use(CamPane pane)
        {
            if (shown != null) shown.StandbyDrawn = standbyDrawn;
            shown = pane;
            bool dock = pane == docked;
            situation.Rect.gameObject.SetActive(dock);
            trackBox.Rect.gameObject.SetActive(dock);
            docked.Box.Rect.gameObject.SetActive(dock);
            enlarged.Box.Rect.gameObject.SetActive(!dock);
            pane.Box.Tagged("INCO"); // a box first shown now lays its header out now (TMP measures nothing while it is inactive)
            sensor = pane.Box; optical = pane.Optical; radar = pane.Radar; zoom = pane.Zoom; sizeBtn = pane.Size; picture = pane.Picture;
            sensorNote = pane.Note; burnTl = pane.Tl; burnTl2 = pane.Tl2; burnTr = pane.Tr; burnBr = pane.Br; standby = pane.Standby; burn = pane.Burn;
            picW = pane.W; picH = pane.H; standbyDrawn = pane.StandbyDrawn;
        }

        /// <summary>True while the camera has the whole room (the map takes no input then).</summary>
        internal bool Enlarged => shown == enlarged;

        /// <summary>ENLARGE / SHRINK: the camera grows over the map or docks back under it. A relocation in progress is dropped first.</summary>
        internal void ToggleCamera()
        {
            actions.Touch();
            if (shown != enlarged && relocating) Cancel();
            Use(shown == enlarged ? docked : enlarged);
            if (last != null) PaintSensor(last);
        }

        /// <summary>Offline render seam: the room as if the pilot had selected a bird and begun a relocation to (u, v); a negative u proposes nothing yet.</summary>
        internal void Preview(int birdKind, bool relocate, float u, float v)
        {
            selKind = birdKind; relocating = relocate && birdKind >= 0; hasPending = relocating && u >= 0f; pending = new Vector2(u, v);
            Repaint();
        }

        // ---- Situation -----------------------------------------------------------------------------------------------

        /// <summary>A console panel: navy plate, steel-blue bezel line, a 3 px rail in the panel's own hue.</summary>
        private static void Slab(FrontVector c, float x, float y, float w, float h, Color rail)
        {
            c.Rect(x, y, w, h, RoomInk.Plate.WithAlpha(0.92f));
            c.Frame(x, y, w, h, 1f, RoomInk.Bezel.WithAlpha(0.85f));
            c.Rect(x, y, 3f, h, rail.WithAlpha(0.9f));
        }

        private void BuildSituation(RectTransform hero, float heroW)
        {
            situation = new FrontBox(hero, skin, "Orbit", 0f, 0f, heroW, TopH, "ORBITAL SITUATION · GEOSTATIONARY STATIONS", AvIcon.Satellite, FrontLook.Console).Tagged("FDO");
            RectTransform body = situation.Rect;
            // The plate fills the box body left of the console column (which therefore never covers a footprint) and clips its own vector layer.
            plate = new FrontMapPlate(body, skin, mapW, TopH, GridCols, GridRows);
            side = FrontVector.Add(body, "Cards");
            AvLay.Place(side.rectTransform, 0f, 0f, mapW, TopH);
            colG = FrontVector.Add(body, "Column");
            AvLay.Place(colG.rectTransform, 0f, 0f, heroW, TopH);
            FrontLabels fx = plate.Labels;

            for (int i = 0; i < MaxBirdLabels; i++)
            {
                birdLabels[i] = FrontKit.Mono(body, "BirdLabel" + i, 0f, 0f, 180f, 28f, 10.5f, TextAlignmentOptions.TopLeft);
                reachLabels[i] = FrontKit.Mono(body, "ReachLabel" + i, 0f, 0f, 70f, 14f, 10f, TextAlignmentOptions.TopLeft);
                birdLabels[i].gameObject.SetActive(false);
                reachLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MaxSiteLabels; i++)
            {
                siteLabels[i] = FrontKit.Mono(body, "SiteLabel" + i, 0f, 0f, 150f, 14f, 10f, TextAlignmentOptions.TopLeft);
                siteLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < costLabels.Length; i++)
            {
                costLabels[i] = FrontKit.Mono(body, "CostLabel" + i, 0f, 0f, 60f, 14f, 10f, TextAlignmentOptions.TopLeft, true);
                costLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < burnLabels.Length; i++)
            {
                burnLabels[i] = FrontKit.Mono(body, "BurnLabel" + i, 0f, 0f, 90f, 14f, 10f, TextAlignmentOptions.TopLeft, true);
                burnLabels[i].gameObject.SetActive(false);
            }
            focusLabel = FrontKit.Mono(body, "FocusLabel", 0f, 0f, 280f, 14f, 10f, TextAlignmentOptions.TopLeft);
            focusLabel.gameObject.SetActive(false);
            propLabel = FrontKit.Mono(body, "PropLabel", 0f, 0f, 250f, 14f, 10f, TextAlignmentOptions.TopLeft, true);
            propLabel.gameObject.SetActive(false);
            airbases = new FrontMap.AirbaseLayer(body);

            // Legend card above the plate's bottom row: swatches are drawn in Paint, the words are fixed.
            string[] words = { "OWN", "HOSTILE", "FOOTPRINT", "SITE", "BURN", "COST", "FOCUS" };
            float ly = plate.Y + plate.H - 24f - 28f, lx = LegendX;
            for (int i = 0; i < legend.Length; i++)
            {
                legendSwatch[i] = lx + 12f;
                legend[i] = FrontKit.Mono(body, "Legend" + i, lx + 28f, ly + 5f, words[i].Length * 7f + 6f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
                legend[i].text = words[i];
                lx += 28f + words[i].Length * 7f + 12f;
            }
            legendRect[0] = new Rect(plate.X + 6f, ly, lx - LegendX + 6f, 24f);
            fx.Fixed(legendRect[0]);

            // ---- The console column ----
            fdLabel = FrontKit.Mono(body, "FdLabel", colPx + 10f, FdY + 13f, 34f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
            fdLabel.text = "MET";
            fdMet = FrontKit.Mono(body, "FdMet", colPx + 40f, FdY + 4f, 128f, 32f, 24f, TextAlignmentOptions.MidlineLeft, true);
            fdCap = FrontKit.Mono(body, "FdCap", colPx + 176f, FdY + 1f, 92f, 13f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
            fdCap.text = "FLIGHT";
            fdCall = FrontKit.Mono(body, "FdCall", colPx + 176f, FdY + 14f, 92f, 22f, 11.5f, TextAlignmentOptions.Midline, true);
            for (int i = 0; i < 3; i++)
            {
                int kind = (int)Kinds[i];
                ownCards[i] = new PollRow(body, skin, colPx, RowsY + i * RowStep, PW, () => Select(kind));
            }
            hostA = FrontKit.Mono(body, "HostA", colPx + 10f, HostY + 2f, PW - 20f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 0.5f);
            hostB = FrontKit.Mono(body, "HostB", colPx + 10f, HostY + 15f, PW - 20f, 14f, 10f, TextAlignmentOptions.MidlineLeft);

            // The inspector.
            float ix = colPx, iy = InspY;
            selHead = FrontKit.Mono(body, "SelHead", ix + 10f, iy + 3f, 170f, 16f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
            selState = FrontKit.Mono(body, "SelState", ix + PW - 110f, iy + 3f, 100f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            selLine = FrontKit.Mono(body, "SelLine", ix + 10f, iy + 21f, PW - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            selFuelLabel = FrontKit.Mono(body, "SelFuelLabel", ix + 10f, iy + 40f, 38f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
            selFuelLabel.text = "FUEL";
            selFuel = FrontKit.Mono(body, "SelFuel", ix + PW - 82f, iy + 40f, 72f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            fuelRect = new Rect(ix + 50f, iy + 44f, PW - 140f, 9f);
            float by = iy + 60f;
            relocBtn = Small(body, "RELOCATE", ix + 8f, by, 124f, StartRelocate, "Pick a new parked point for this satellite. Nothing is spent until you CONFIRM BURN.");
            burnBtn = Small(body, "CONFIRM BURN", ix + 8f, by, 132f, Confirm, "Burn to the chosen point: the fuel is spent now and the bird slews there.");
            cancelBtn = Small(body, "CANCEL", ix + 146f, by, 80f, Cancel, "Drop the proposal. Nothing is spent.");
            selHint = FrontKit.Mono(body, "SelHint", ix + 10f, iy + 86f, PW - 20f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            relocBtn.SetStyle(AvButtonStyle.Primary);
            burnBtn.SetStyle(AvButtonStyle.Primary);
            SetBar(false, false);

            // The uplink sites: own ones as two-line cards, the revealed hostile ones as one-line chips.
            siteHeadOwn = FrontKit.Mono(body, "SiteHeadOwn", colPx + 4f, SiteHeadY, 112f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            siteHeadFoe = FrontKit.Mono(body, "SiteHeadFoe", colPx + PW - 4f - 154f, SiteHeadY, 154f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            for (int i = 0; i < siteCards.Length; i++) siteCards[i] = new SiteCard(body, skin, colPx, SiteY0 + i * 32f, PW, ShowSite);
            for (int i = 0; i < siteChips.Length; i++) siteChips[i] = new SiteChip(body, skin, colPx, SiteY0 + 64f + i * 22f, PW, ShowSite);
            skin.Add(() =>
            {
                selLine.color = RoomInk.Engrave; selFuelLabel.color = Mc.Steel; selHint.color = Mc.Steel; hostB.color = Mc.Steel;
                for (int i = 0; i < legend.Length; i++) legend[i].color = Mc.Steel;
                fdLabel.color = Mc.Steel; fdCap.color = Mc.Steel;
            });
        }

        private AvControl Small(RectTransform p, string label, float x, float y, float w, Action click, string help)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, AvButtonStyle.Default));
            AvLay.Place(c.Rect, x, y, w, 24f);
            c.SingleLine();
            c.Help = help;
            return c;
        }

        private void SetBar(bool on, bool mode)
        {
            selHead.gameObject.SetActive(on); selState.gameObject.SetActive(on); selLine.gameObject.SetActive(on);
            selFuelLabel.gameObject.SetActive(on); selFuel.gameObject.SetActive(on); selHint.gameObject.SetActive(on);
            relocBtn.Rect.gameObject.SetActive(on && !mode);
            burnBtn.Rect.gameObject.SetActive(on && mode);
            cancelBtn.Rect.gameObject.SetActive(on && mode);
        }

        /// <summary>The selection or the relocation changed: the map, the poll and the inspector all show it.</summary>
        private void Repaint()
        {
            if (last == null) return;
            PaintSituation(last);
            PaintColumn(last);
        }

        private void Select(int kind)
        {
            actions.Touch();
            if (!TryBird(kind, out OrbitTrackView b) || !b.Alive) return;
            selKind = kind;
            relocating = false; hasPending = false;
            Repaint();
        }

        private void StartRelocate()
        {
            actions.Touch();
            if (selKind < 0 || !TryBird(selKind, out OrbitTrackView b) || !CanRelocate(b)) return;
            relocating = true; hasPending = false;
            Repaint();
        }

        public bool CancelMode()
        {
            if (Enlarged) { ToggleCamera(); return true; } // Esc docks an enlarged camera before it closes the window
            if (!relocating) return false;
            Cancel();
            return true;
        }

        private void Cancel()
        {
            actions.Touch();
            relocating = false; hasPending = false;
            Repaint();
        }

        /// <summary>A click on a site line centres the map on it. Local: nothing is sent.</summary>
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

        private static bool CanRelocate(in OrbitTrackView b) => b.Alive && !b.Relocating && b.Fuel >= GeoBird.Cost(0.005f);

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

        // ---- Sensor, track file and station log ---------------------------------------------------------------------

        private CamPane BuildSensor(RectTransform hero, float x, float y, float w, float h, bool big)
        {
            sensor = new FrontBox(hero, skin, big ? "SensorBig" : "Sensor", x, y, w, h, "SATELLITE CAMERA", AvIcon.Camera, FrontLook.Console).Tagged("INCO");
            RectTransform b = sensor.Rect;
            sizeBtn = Tool(b, big ? "SHRINK" : "ENLARGE", w - 118f, 28f, 110f, ToggleCamera,
                big ? "Dock the camera back under the map (Esc does the same)." : "Give the camera the whole room. Click a bracket to select a track, then MARK and TRANSMIT. SHRINK or Esc docks it again.");
            sizeBtn.SetStyle(AvButtonStyle.Primary);
            AvControl markBig = null, sendBig = null;
            if (big)
            {
                markBig = Tool(b, "MARK", 326f, 28f, 130f, () => Do(() => Feed.Confirm()), "Mark the selected track so it shows on every pilot's map.");
                sendBig = Tool(b, "TRANSMIT", 462f, 28f, 170f, () => Do(() => Feed.Send()), "Post the marked tracks as TASKED targets for any pilot.");
                markBig.SetStyle(AvButtonStyle.Primary);
                sendBig.SetStyle(AvButtonStyle.Primary);
            }
            float noteX = big ? 640f : 326f;
            optical = Tool(b, "OPTICAL", 8f, 28f, 96f, () => Do(() => Feed.SetSource(BirdKind.Optical)), "Show the OPTICAL satellite's camera picture. Cloud softens it, night refuses it.");
            radar = Tool(b, "RADAR", 108f, 28f, 96f, () => Do(() => Feed.SetSource(BirdKind.Radar)), "Show the RADAR satellite's SAR picture from the last scan.");
            zoom = Tool(b, "ZOOM", 208f, 28f, 112f, () => Do(() => Feed.CycleZoom()), "Cycle the picture zoom: WIDE, MID, CLOSE. CLOSE centres on the selected track.");
            sensorNote = FrontKit.Mono(b, "Note", noteX, 28f, w - noteX - 8f - 122f, 26f, 10.5f, TextAlignmentOptions.MidlineRight);
            picture = new OpsFeedPicture(b, id => Do(() => Feed.SelectEntry(id)));
            picture.Place(new AvSlot(PicX, PicY, w - 16f, h - 68f));
            picW = w - 16f; picH = h - 68f;
            standby = FrontVector.Add(b, "Standby");
            AvLay.Place(standby.rectTransform, PicX, PicY, picW, picH);

            // The camera burn-in: crop marks, reticle and the plated data lines sit over the picture and its brackets (it never takes a click).
            burn = FrontVector.Add(b, "Burn");
            AvLay.Place(burn.rectTransform, PicX, PicY, picW, picH);
            burnTl = FrontKit.Mono(b, "BurnTl", 0f, 0f, 200f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            burnTl2 = FrontKit.Mono(b, "BurnTl2", 0f, 0f, 200f, 16f, 10f, TextAlignmentOptions.MidlineLeft);
            burnTr = FrontKit.Mono(b, "BurnTr", 0f, 0f, 40f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
            burnBr = FrontKit.Mono(b, "BurnBr", 0f, 0f, 200f, 16f, 10f, TextAlignmentOptions.MidlineRight);
            TMP_Text note = sensorNote, tl = burnTl, tl2 = burnTl2, br = burnBr;
            skin.Add(() => { note.color = Mc.Steel; tl.color = RoomInk.Engrave; tl2.color = Mc.Steel; br.color = RoomInk.Engrave; });
            return new CamPane
            {
                Box = sensor, Optical = optical, Radar = radar, Zoom = zoom, Size = sizeBtn, Mark = markBig, Send = sendBig, Picture = picture, Note = sensorNote,
                Tl = burnTl, Tl2 = burnTl2, Tr = burnTr, Br = burnBr, Standby = standby, Burn = burn, W = picW, H = picH
            };
        }

        private AvControl Tool(RectTransform p, string label, float x, float y, float w, Action click, string help)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, AvButtonStyle.Default));
            AvLay.Place(c.Rect, x, y, w, 26f);
            c.SingleLine();
            c.Help = help;
            return c;
        }

        private void BuildTracks(RectTransform hero, float x, float y, float w, float h)
        {
            trackBox = new FrontBox(hero, skin, "Tracks", x, y, w, h, "TRACK FILE", AvIcon.Radar2, FrontLook.Console).Tagged("SURV");
            RectTransform b = trackBox.Rect;
            for (int i = 0; i < TrackRows; i++)
            {
                int slot = i;
                rows[i] = new TrackRow(b, skin, 8f, 28f + i * 39f, w - 16f, 37f, () => Do(() => Feed.SelectEntry(rowIds[slot])));
            }
            float bw = (w - 16f - 6f) / 2f;
            prev = Tool(b, "PREV", 8f, 264f, bw, () => Do(() => Feed.PrevPage()), "Previous page of the track file.");
            next = Tool(b, "NEXT", 14f + bw, 264f, bw, () => Do(() => Feed.NextPage()), "Next page of the track file.");
            mark = Tool(b, "MARK", 8f, 294f, bw, () => Do(() => Feed.Confirm()), "Mark the selected track so it shows on every pilot's map.");
            send = Tool(b, "TRANSMIT", 14f + bw, 294f, bw, () => Do(() => Feed.Send()), "Post the marked tracks as TASKED targets for any pilot.");
            mark.SetStyle(AvButtonStyle.Primary);
            send.SetStyle(AvButtonStyle.Primary);

            // The chain: SELECT a track, MARK it, TRANSMIT the marks. Each chip says its state in words as well as colour.
            chainInner = w - 32f;
            chain = FrontVector.Add(b, "Chain");
            AvLay.Place(chain.rectTransform, 0f, 0f, w, h);
            float cx = 8f;
            for (int i = 0; i < chainText.Length; i++)
            {
                chainText[i] = FrontKit.Mono(b, "Chain" + i, cx + 8f, ChainY, chainInner * ChainShare[i] - 10f, 18f, 10f, TextAlignmentOptions.MidlineLeft, true);
                cx += chainInner * ChainShare[i] + 8f;
            }
            // An empty file says why in full under its one worded row (the slots below it are not drawn).
            trackEmpty = OpsParts.Wrapped(b, "TrackEmpty", 12f, 72f, w - 24f, 120f, 11f);
            trackEmpty.gameObject.SetActive(false);
            briefA = FrontKit.Mono(b, "BriefA", 10f, ChainY + 22f, w - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            briefB = FrontKit.Mono(b, "BriefB", 10f, ChainY + 38f, w - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            skin.Add(() => { briefA.color = RoomInk.Engrave; briefB.color = Mc.Steel; });
        }

        // ---- Paint ---------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            PaintSituation(v);
            PaintColumn(v);
            PaintSensor(v);
            PaintTracks(v.Feed);
        }

        // ---- Ellipse drawing: footprints and cost rings are uv distances, which are slightly flattened ovals on a theatre wider than it is tall ----

        private static Vector2 EllP(Vector2 c, float rx, float ry, float th) => new Vector2(c.x + Mathf.Cos(th) * rx, c.y + Mathf.Sin(th) * ry);

        private static int EllSeg(float rx, float ry) => Mathf.Clamp(Mathf.CeilToInt((rx + ry) * 0.1f), 32, 128);

        private static void EllRing(FrontVector g, Vector2 c, float rx, float ry, float w, Color k, int dashSegs = 0)
        {
            int n = EllSeg(rx, ry);
            Vector2 prev = EllP(c, rx, ry, 0f);
            for (int i = 1; i <= n; i++)
            {
                Vector2 q = EllP(c, rx, ry, 2f * Mathf.PI * i / n);
                if (dashSegs <= 0 || (i / dashSegs) % 2 == 0) g.Line(prev, q, w, k);
                prev = q;
            }
        }

        private static void EllDisc(FrontVector g, Vector2 c, float rx, float ry, Color k)
        {
            int n = EllSeg(rx, ry);
            Vector2 prev = EllP(c, rx, ry, 0f);
            for (int i = 1; i <= n; i++)
            {
                Vector2 q = EllP(c, rx, ry, 2f * Mathf.PI * i / n);
                g.Tri(c, prev, q, k);
                prev = q;
            }
        }

        private static void EllSector(FrontVector g, Vector2 c, float rx, float ry, float a0, float a1, Color k)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / (6f * Mathf.Deg2Rad)));
            Vector2 prev = EllP(c, rx, ry, a0);
            for (int i = 1; i <= n; i++)
            {
                Vector2 q = EllP(c, rx, ry, Mathf.Lerp(a0, a1, (float)i / n));
                g.Tri(c, prev, q, k);
                prev = q;
            }
        }

        /// <summary>Diagonal hatching clipped to an oval (the hostile footprint's dress).</summary>
        private static void Hatch(FrontVector g, Vector2 c, float rx, float ry, Color k, float step)
        {
            float a = 1f / (rx * rx) + 1f / (ry * ry), reach = rx + ry;
            for (float d = -reach; d <= reach; d += step)
            {
                float bq = 2f * d / (rx * rx), cq = d * d / (rx * rx) - 1f, disc = bq * bq - 4f * a * cq;
                if (disc <= 0f) continue;
                float sq = Mathf.Sqrt(disc), y1 = (-bq - sq) / (2f * a), y2 = (-bq + sq) / (2f * a);
                g.Line(new Vector2(c.x + y1 + d, c.y + y1), new Vector2(c.x + y2 + d, c.y + y2), 1f, k);
            }
        }

        private static void DashRect(FrontVector g, Vector2 c, float hw, float hh, float w, Color k)
        {
            g.Dashed(new Vector2(c.x - hw, c.y - hh), new Vector2(c.x + hw, c.y - hh), w, k, 4f, 3f);
            g.Dashed(new Vector2(c.x + hw, c.y - hh), new Vector2(c.x + hw, c.y + hh), w, k, 4f, 3f);
            g.Dashed(new Vector2(c.x + hw, c.y + hh), new Vector2(c.x - hw, c.y + hh), w, k, 4f, 3f);
            g.Dashed(new Vector2(c.x - hw, c.y + hh), new Vector2(c.x - hw, c.y - hh), w, k, 4f, 3f);
        }

        /// <summary>
        /// A satellite seen from nadir: a body with solar-panel bars and a short antenna. Own birds ice, hostile red; a lost bird (or a ghost at a burn's
        /// destination) is hollow.
        /// </summary>
        private static void Sat(FrontVector g, Vector2 p, Color ink, bool solid, float sc)
        {
            Vector2 n = new Vector2(0f, -1f), t = new Vector2(1f, 0f);
            Color back = Mc.PlateInk;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 pc = p + t * (s * 10.5f * sc);
                Vector2 a = pc - t * 6f * sc + n * 3.4f * sc, b = pc + t * 6f * sc + n * 3.4f * sc, c = pc + t * 6f * sc - n * 3.4f * sc, d = pc - t * 6f * sc - n * 3.4f * sc;
                g.Quad(a + (a - pc) * 0.14f, b + (b - pc) * 0.14f, c + (c - pc) * 0.14f, d + (d - pc) * 0.14f, back);
                if (solid)
                {
                    g.Quad(a, b, c, d, ink.WithAlpha(0.88f));
                    g.Line(pc - n * 3.4f * sc, pc + n * 3.4f * sc, 0.9f, back);
                }
                else g.Polyline(new[] { a, b, c, d }, 1.2f, ink, true);
                g.Line(p + t * (s * 5f * sc), p + t * (s * 5.5f * sc), 2f, ink);
            }
            g.Diamond(p, 7.6f * sc, back);
            g.Diamond(p, 5.6f * sc, ink);
            if (!solid) g.Diamond(p, 3.2f * sc, back);
            g.Line(p + n * 5f * sc, p + n * 11f * sc, 1.3f, ink);
            g.Disc(p + n * 11.5f * sc, 1.5f, ink, 8);
        }

        private void PaintSituation(FrontRoomView v)
        {
            last = v;
            if (selKind < 0 || !TryBird(selKind, out OrbitTrackView sb) || !sb.Alive)
            {
                if (selKind >= 0) { relocating = false; hasPending = false; }
                selKind = -1;   // the first living bird of ours is inspected until the pilot picks another
                for (int i = 0; i < v.Birds.Count; i++) if (v.Birds[i].Own && v.Birds[i].Alive) { selKind = (int)v.Birds[i].Kind; break; }
            }
            Color own = Own, foe = AvInk.Hostile;
            FrontMapView mv = plate.View;
            // Centre on our own uplink sites and airbases (the operator's pan or zoom takes over afterwards).
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int i = 0; i < v.Sites.Count; i++) if (v.Sites[i].Own) { sum += new Vector2(v.Sites[i].X, v.Sites[i].Y); n++; }
            for (int i = 0; i < v.Airbases.Count; i++) if (v.Airbases[i].Side == AirbaseSide.Own) { sum += new Vector2(v.Airbases[i].U, v.Airbases[i].V); n++; }
            if (n > 0) mv.AutoCenter(sum / n);
            plate.Begin();
            if (mapImage == null)
            {
                Transform mi = situation.Rect.Find("MapClip/MapImage"), ms = situation.Rect.Find("MapInset");
                mapImage = mi != null ? mi.GetComponent<RawImage>() : null;
                mapInset = ms != null ? ms.GetComponent<RawImage>() : null;
            }
            if (mapImage != null) mapImage.color = RoomInk.Rgba(0.62f, 0.78f, 1f, 1f);   // the theatre picture, seen as a night-side view from orbit
            if (mapInset != null) mapInset.color = RoomInk.Rgba(0.58f, 0.72f, 1f, 1f);
            FrontVector g = plate.G;
            FrontLabels labels = plate.Labels;
            Vector2 Pt(float u, float vv) => mv.P(u, vv);
            // Pixels per theatre width across and down. GEO distances are ground km (GeoBird.Aspect scales a v step), so footprints and cost rings are circles.
            float pxU = mv.W / mv.VW, pxV = mv.H / mv.VH / Mathf.Max(0.05f, GeoBird.Aspect);

            // The night side of Earth: a navy tint over the theatre picture (every layer under 0.2 alpha, so it reads as a tint, not a ground).
            float px = mv.X, py = mv.Y, pw = mv.W, ph = mv.H;
            g.Rect(px, py, pw, ph, RoomInk.Rgb(0x040B20, 0.19f));
            g.Rect(px, py, pw, ph, RoomInk.Rgb(0x0B2C70, 0.12f));
            g.Rect(px, py, pw, ph, RoomInk.Rgb(0x061540, 0.10f));
            Color v0 = RoomInk.Rgb(0x030820, 0.19f), v1 = RoomInk.Rgb(0x030820, 0f);
            const float e = 100f;
            g.Grad(px, py, e, ph, v0, v1, v1, v0);
            g.Grad(px + pw - e, py, e, ph, v1, v0, v0, v1);
            g.Grad(px, py, pw, e * 0.5f, v0, v0, v1, v1);
            g.Grad(px, py + ph - e * 0.5f, pw, e * 0.5f, v1, v1, v0, v0);

            FrontMap.AirbaseLayer.Glyphs(g, v.Airbases, Pt);
            for (int i = 0; i < v.Airbases.Count; i++) if (v.Airbases[i].Side == AirbaseSide.Own) plate.Dot(v.Airbases[i].U, v.Airbases[i].V, own);

            // The selected bird's fuel-cost rings: how far a burn can go for 10, 25 and 50 % of the tank, and the reach of what is left.
            ringAt.Clear(); ringWord.Clear(); ringInk.Clear();
            OrbitTrackView selB = default;
            bool haveSel = selKind >= 0 && TryBird(selKind, out selB) && selB.Alive;
            if (haveSel) DrawCostRings(g, selB, pxU, pxV);

            // Footprints (hostile first so ours sit on top; where ours overlap the fills add up), the uplink beams, then the sites.
            int overhead = 0, up = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < v.Birds.Count; i++)
                {
                    OrbitTrackView b = v.Birds[i];
                    if (b.Own != (pass == 1) || !b.Alive) continue;
                    DrawFootprint(g, b, b.Own ? own : foe, pxU, pxV, v.Now);
                }
            for (int i = 0; i < v.Birds.Count; i++)
            {
                OrbitTrackView b = v.Birds[i];
                if (!b.Own || !b.Alive) continue;
                for (int j = 0; j < v.Sites.Count; j++)   // the uplink beam: a live own site inside the footprint talks to the bird
                {
                    OrbitSiteView s = v.Sites[j];
                    if (!s.Own || !s.Live || GeoBird.Distance(b.U, b.V, s.X, s.Y) > b.Radius) continue;
                    g.Dashed(Pt(s.X, s.Y) + new Vector2(0f, -7f), BirdPoint(b), 1.3f, own.WithAlpha(0.8f), 5f, 4f);
                }
            }
            for (int i = 0; i < v.Sites.Count; i++)
            {
                OrbitSiteView s = v.Sites[i];
                Vector2 p = Pt(s.X, s.Y);
                Color k = s.Rebuilding ? AvInk.State(AvState.Caution) : !s.Live ? AvInk.State(AvState.Danger) : s.Own ? own : foe;
                g.Tri(p + new Vector2(0f, -8f), p + new Vector2(8f, 6f), p + new Vector2(-8f, 6f), AvInk.Ground);
                g.Tri(p + new Vector2(0f, -7f), p + new Vector2(7f, 5f), p + new Vector2(-7f, 5f), s.Live ? k : k.WithAlpha(0.25f));
                if (!s.Live) g.Polyline(new[] { p + new Vector2(0f, -7f), p + new Vector2(7f, 5f), p + new Vector2(-7f, 5f) }, 1.4f, k, true);
                g.Line(p + new Vector2(0f, 5f), p + new Vector2(0f, 10f), 1.4f, k);
                if (s.Live) g.Ring(p, 11f, 1.1f, k.WithAlpha(0.45f), 24, 1f, 1f);   // the link's own range is not modelled: this is just the lit marker
                if (s.Rebuilding) g.Ring(p, 12f, 1.4f, k, 24, 1f, 1f);   // dashed: under construction
                if (s.Own) plate.Dot(s.X, s.Y, k);
                labels.Take(new Rect(p.x - 9f, p.y - 9f, 18f, 22f));
            }

            // The focus pin from the rail: the real point the "over focus" readings are taken against, with a line to every own bird that covers it.
            Vector2 fpos = Vector2.zero;
            bool pin = false;
            if (v.HasFocus)
            {
                Vector2 fp = fpos = Pt(v.FocusU, v.FocusV);
                Color fk = AvInk.Key;
                for (int i = 0; i < v.Birds.Count; i++)
                    if (v.Birds[i].Own && v.Birds[i].Alive && v.Birds[i].Overhead) g.Dashed(fp, BirdPoint(v.Birds[i]), 1.1f, own.WithAlpha(0.5f), 3f, 5f);
                g.Ring(fp, 11f, 1.6f, fk, 28);
                g.Ring(fp, 19f, 1.2f, fk.WithAlpha(0.6f), 36, 1f, 1f);
                g.Line(fp + new Vector2(-26f, 0f), fp + new Vector2(-15f, 0f), 1.4f, fk);
                g.Line(fp + new Vector2(26f, 0f), fp + new Vector2(15f, 0f), 1.4f, fk);
                g.Line(fp + new Vector2(0f, -26f), fp + new Vector2(0f, -15f), 1.4f, fk);
                g.Line(fp + new Vector2(0f, 26f), fp + new Vector2(0f, 15f), 1.4f, fk);
                labels.Take(new Rect(fp.x - 24f, fp.y - 24f, 48f, 48f));
                pin = plate.Shown(fp, 24f);
            }

            // A burn under way: the dashed path to a ghost footprint at the destination, chevrons along it.
            int burns = 0;
            Vector2[] burnTo = new Vector2[burnLabels.Length];
            string[] burnWord = new string[burnLabels.Length];
            for (int i = 0; i < v.Birds.Count; i++)
            {
                OrbitTrackView b = v.Birds[i];
                if (!b.Alive || !b.Relocating) continue;
                DrawTransfer(g, b, b.Own ? own : foe, pxU, pxV);
                if (burns < burnLabels.Length) { burnTo[burns] = plate.P(b.TargetU, b.TargetV); burnWord[burns] = "ETA " + FrontKit.Clock(b.EtaSeconds); burns++; }
            }

            // Nadir markers.
            for (int i = 0; i < v.Birds.Count; i++)
            {
                OrbitTrackView b = v.Birds[i];
                if (b.Alive) up++;
                if (b.Alive && b.Overhead) overhead++;
                DrawNadir(g, b, b.Own ? own : foe);
                Vector2 p = BirdPoint(b);
                labels.Take(new Rect(p.x - 26f, p.y - 20f, 52f, 40f));
            }

            // A proposed parked point: the dashed path from the selected bird, the ghost footprint at the pointer, the cost and the ETA.
            string propWord = null;
            Vector2 propAt = Vector2.zero;
            Color propInk = AvInk.Select;
            if (relocating && hasPending && haveSel)
            {
                Vector2 f0 = plate.P(selB.U, selB.V), f1 = plate.P(pending.x, pending.y);
                Color sk = AvInk.Select;
                float cost = PendingCost(selB), dist = PendingDistance(selB);
                bool afford = selB.Fuel >= cost && cost > 0f;
                g.Dashed(f0, f1, 1.8f, sk, 7f, 5f);
                EllDisc(g, f1, selB.Radius * pxU, selB.Radius * pxV, sk.WithAlpha(0.07f));
                EllRing(g, f1, selB.Radius * pxU, selB.Radius * pxV, 1.6f, sk.WithAlpha(0.9f), 3);
                Sat(g, f1, sk, false, 1f);
                g.Line(f1 + new Vector2(-12f, 0f), f1 + new Vector2(-7f, 0f), 1.2f, sk);
                g.Line(f1 + new Vector2(12f, 0f), f1 + new Vector2(7f, 0f), 1.2f, sk);
                propAt = f1;
                propInk = afford ? FrontKit.Tone(AvState.Caution) : FrontKit.Tone(AvState.Danger);
                propWord = cost <= 0f ? "ALREADY PARKED THERE" : afford ? "-" + Mathf.RoundToInt(cost) + " % FUEL · ETA " + FrontKit.Clock(dist / GeoBird.Slew) : "NOT ENOUGH FUEL · NEED " + Mathf.CeilToInt(cost) + " %";
                labels.Take(new Rect(f1.x - 26f, f1.y - 20f, 52f, 40f));
            }

            // Words. Bird labels first (they are not optional), then the rest where room is left.
            int bl = 0;
            for (int i = 0; i < v.Birds.Count && bl < MaxBirdLabels; i++)
            {
                OrbitTrackView b = v.Birds[i];
                Vector2 p = BirdPoint(b);
                if (!plate.Shown(p, 14f)) continue;
                TMP_Text t = birdLabels[bl++];
                t.gameObject.SetActive(true);
                Color ink = !b.Alive ? AvInk.Dim : b.Own ? Own : AvInk.Hostile;
                string state = !b.Alive ? "LOST · NO SIGNAL" : b.Relocating ? "BURN · -" + Mathf.RoundToInt(b.BurnCost) + " % FUEL" : "ALT 35 786 KM · FUEL " + Mathf.RoundToInt(b.Fuel) + " %";
                Color stateInk = b.Relocating && b.Alive ? FrontKit.Tone(AvState.Caution) : b.Overhead && b.Alive ? FrontKit.Tone(b.Own ? AvState.Ready : AvState.Danger) : AvInk.Dim;
                t.text = C2Kit.Tint("<b>" + b.Callsign + "</b> · " + C2Orbit.BirdName(b.Kind), ink) + "\n" + C2Kit.Tint(state, stateInk);
                t.color = AvInk.Ink;
                plates.Add(labels.Place(t, p, 28f, false));
            }
            for (int i = bl; i < MaxBirdLabels; i++) birdLabels[i].gameObject.SetActive(false);

            bool propShown = false;
            if (propWord != null)
            {
                FrontKit.Set(propLabel, propWord, propInk);
                propLabel.gameObject.SetActive(true);
                Rect r = labels.Place(propLabel, propAt, 24f, true, true);
                if (r.width > 0f) { plates.Add(r); propShown = true; }
            }
            if (!propShown) propLabel.gameObject.SetActive(false);

            int rl = 0;
            for (int i = 0; i < v.Birds.Count && rl < MaxBirdLabels; i++)
            {
                OrbitTrackView b = v.Birds[i];
                if (!b.Alive) continue;
                Vector2 c = BirdPoint(b), at = c + new Vector2(b.Radius * pxU, 0f);
                if (!plate.Shown(at, 12f)) continue;
                g.Line(at, at + new Vector2(6f, 0f), 1.4f, (b.Own ? own : foe).WithAlpha(0.9f));
                TMP_Text t = reachLabels[rl];
                FrontKit.Set(t, Mathf.RoundToInt(b.FootprintKm) + " KM", b.Own ? own : foe);
                t.gameObject.SetActive(true);
                Rect r = labels.Place(t, at, 8f, true, true);
                if (r.width <= 0f) { t.gameObject.SetActive(false); continue; }
                plates.Add(r);
                rl++;
            }
            for (int i = rl; i < MaxBirdLabels; i++) reachLabels[i].gameObject.SetActive(false);

            int cl = 0;
            for (int i = 0; i < ringAt.Count && cl < costLabels.Length; i++)
            {
                TMP_Text t = costLabels[cl];
                FrontKit.Set(t, ringWord[i], ringInk[i]);
                t.gameObject.SetActive(true);
                Rect r = labels.Place(t, ringAt[i], 3f, true, true);
                if (r.width <= 0f) { t.gameObject.SetActive(false); continue; }
                plates.Add(r);
                cl++;
            }
            for (int i = cl; i < costLabels.Length; i++) costLabels[i].gameObject.SetActive(false);

            for (int i = 0; i < burnLabels.Length; i++)
            {
                TMP_Text t = burnLabels[i];
                bool shown = false;
                if (i < burns && plate.Shown(burnTo[i], 12f))
                {
                    FrontKit.Set(t, burnWord[i], FrontKit.Tone(AvState.Caution));
                    t.gameObject.SetActive(true);
                    Rect r = labels.Place(t, burnTo[i], 10f, true, true);
                    if (r.width > 0f) { plates.Add(r); shown = true; }
                }
                if (!shown) t.gameObject.SetActive(false);
            }

            // Site words where there is room (the column carries them all).
            int siteWords = 0;
            for (int i = 0; i < v.Sites.Count; i++)
            {
                OrbitSiteView s = v.Sites[i];
                Vector2 p = Pt(s.X, s.Y);
                if (siteWords >= MaxSiteLabels || !plate.Shown(p, 20f)) continue;
                TMP_Text t = siteLabels[siteWords];
                t.gameObject.SetActive(true);
                FrontKit.Set(t, ShortSite(s.Name) + " · " + (s.Own && s.Live ? "LINK " : "") + s.State, s.Rebuilding ? FrontKit.Tone(AvState.Caution) : !s.Live ? FrontKit.Tone(AvState.Danger) : s.Own ? own : foe);
                Rect r = labels.Place(t, p, 8f, true, true);
                if (r.width <= 0f) { t.gameObject.SetActive(false); continue; }
                plates.Add(r);
                siteWords++;
            }
            for (int i = siteWords; i < MaxSiteLabels; i++) siteLabels[i].gameObject.SetActive(false);

            // The focus pin's readout: which of our birds cover it (the perk gate: the aim must lie inside the right bird's footprint).
            bool focusWord = false;
            if (pin)
            {
                string cover = "";
                for (int k = 0; k < Kinds.Length; k++)
                {
                    int found = -1;
                    for (int i = 0; i < v.Birds.Count; i++) if (v.Birds[i].Own && v.Birds[i].Kind == Kinds[k] && v.Birds[i].Alive) found = i;
                    string word = found < 0 ? "--" : v.Birds[found].Overhead ? "IN" : "OUT";
                    Color wink = found < 0 ? AvInk.Dim : v.Birds[found].Overhead ? Mc.Go : Mc.NoGo;
                    cover += " · " + KindTag[k] + " " + C2Kit.Tint(word, wink);
                }
                FrontKit.Set(focusLabel, C2Kit.Tint("FOCUS " + v.FocusGrid, AvInk.Key) + cover, AvInk.Ink);
                focusLabel.gameObject.SetActive(true);
                Rect r = labels.Place(focusLabel, fpos, 24f, true, true);
                if (r.width > 0f) { plates.Add(r); focusWord = true; }
            }
            if (!focusWord) focusLabel.gameObject.SetActive(false);

            foreach (Rect pr in plates) g.Rect(pr.x - 2f, pr.y - 1f, pr.width + 4f, pr.height + 2f, RoomInk.Plate.WithAlpha(0.88f));
            plates.Clear();
            airbases.Names(g, v.Airbases, Pt, mv, (t, a) => labels.Place(t, a, 9f, true, true));
            plate.End();

            // The legend over the map.
            FrontVector c2 = side;
            c2.Clear();
            Slab(c2, legendRect[0].x, legendRect[0].y, legendRect[0].width, legendRect[0].height, RoomInk.Bezel);
            float ly = legendRect[0].y + 12f;
            c2.Diamond(new Vector2(legendSwatch[0], ly), 6f, own);
            c2.Diamond(new Vector2(legendSwatch[1], ly), 6f, foe);
            c2.Ring(new Vector2(legendSwatch[2], ly), 7f, 1.4f, own, 20);
            c2.Tri(new Vector2(legendSwatch[3], ly - 6f), new Vector2(legendSwatch[3] + 6f, ly + 4f), new Vector2(legendSwatch[3] - 6f, ly + 4f), own);
            c2.Dashed(new Vector2(legendSwatch[4] - 10f, ly), new Vector2(legendSwatch[4] + 10f, ly), 2f, Mc.Steel, 6f, 4f);
            c2.Ring(new Vector2(legendSwatch[5], ly), 7f, 1.2f, RoomInk.Telemetry, 20, 1f, 1f);
            c2.Ring(new Vector2(legendSwatch[6], ly), 5f, 1.4f, AvInk.Key, 16);
            c2.Line(new Vector2(legendSwatch[6] - 10f, ly), new Vector2(legendSwatch[6] + 10f, ly), 1.2f, AvInk.Key);
            c2.Flush();
            string focus = v.HasFocus ? overhead + " OVER FOCUS" : "NO FOCUS PIN";
            situation.SetMeta("TRACKING " + v.Birds.Count + " OBJECTS · " + up + " UP · " + focus + " · " + v.Theatre, overhead > 0 ? FrontKit.Tone(AvState.Caution) : AvInk.Dim);
        }

        private Vector2 BirdPoint(in OrbitTrackView b) => plate.P(b.U, b.V);

        /// <summary>The site's name without its family word (UPLINK ALPHA reads ALPHA, HOSTILE UPL-05 reads UPL-05): the map only needs the short tag.</summary>
        private static string ShortSite(string name)
        {
            name = name ?? "";
            if (name.StartsWith("UPLINK ", StringComparison.Ordinal)) return name.Substring(7);
            if (name.StartsWith("HOSTILE ", StringComparison.Ordinal)) return name.Substring(8);
            return name;
        }

        /// <summary>
        /// The bird's footprint, drawn exactly as the coverage gate tests it: Reach is a share of the theatre in map units (a slightly flattened oval in
        /// kilometres), and anything inside it is covered. Each kind has its own dress: OPTICAL a camera frame, RADAR scan rings and a sweep, KINETIC strike
        /// rings and a crosshair. Hostile footprints are red and hatched.
        /// </summary>
        private void DrawFootprint(FrontVector g, in OrbitTrackView b, Color k, float pxU, float pxV, float now)
        {
            Vector2 c = BirdPoint(b);
            float rx = b.Radius * pxU, ry = b.Radius * pxV;
            EllDisc(g, c, rx, ry, k.WithAlpha(b.Own ? 0.1f : 0.06f));
            if (!b.Own) Hatch(g, c, rx, ry, k.WithAlpha(0.3f), 9f);
            EllRing(g, c, rx, ry, 1.9f, k.WithAlpha(0.95f));
            switch (b.Kind)
            {
                case BirdKind.Optical:
                {
                    // A camera frame: corner brackets and a dashed outline around the swath the bird looks at.
                    float hw = rx * 0.56f, hh = ry * 0.56f, l = Mathf.Min(16f, hw * 0.5f);
                    for (int sx = -1; sx <= 1; sx += 2)
                        for (int sy = -1; sy <= 1; sy += 2)
                        {
                            Vector2 o = c + new Vector2(sx * hw, sy * hh);
                            g.Line(o, o + new Vector2(-sx * l, 0f), 1.8f, k.WithAlpha(0.95f));
                            g.Line(o, o + new Vector2(0f, -sy * l), 1.8f, k.WithAlpha(0.95f));
                        }
                    DashRect(g, c, hw, hh, 1f, k.WithAlpha(0.45f));
                    g.Line(c + new Vector2(-7f, 0f), c + new Vector2(7f, 0f), 1f, k.WithAlpha(0.6f));
                    g.Line(c + new Vector2(0f, -7f), c + new Vector2(0f, 7f), 1f, k.WithAlpha(0.6f));
                    break;
                }
                case BirdKind.Radar:
                {
                    // Scan rings and a sweep wedge (its angle runs off the mission clock: flavour).
                    EllRing(g, c, rx * 0.34f, ry * 0.34f, 1f, k.WithAlpha(0.5f), 2);
                    EllRing(g, c, rx * 0.67f, ry * 0.67f, 1f, k.WithAlpha(0.5f), 2);
                    float a1 = Mathf.Repeat(now * 0.9f, 2f * Mathf.PI);
                    for (int s = 0; s < 4; s++)
                        EllSector(g, c, rx, ry, a1 - (s + 1) * 12f * Mathf.Deg2Rad, a1 - s * 12f * Mathf.Deg2Rad, k.WithAlpha(0.16f - 0.035f * s));
                    g.Line(c, EllP(c, rx, ry, a1), 1.5f, k.WithAlpha(0.85f));
                    break;
                }
                default:
                {
                    // Strike rings and a crosshair.
                    EllRing(g, c, rx * 0.38f, ry * 0.38f, 1.2f, k.WithAlpha(0.7f));
                    EllRing(g, c, rx * 0.7f, ry * 0.7f, 1f, k.WithAlpha(0.45f));
                    float gap = 8f;
                    g.Line(c + new Vector2(-rx - 8f, 0f), c + new Vector2(-gap, 0f), 1.3f, k.WithAlpha(0.8f));
                    g.Line(c + new Vector2(rx + 8f, 0f), c + new Vector2(gap, 0f), 1.3f, k.WithAlpha(0.8f));
                    g.Line(c + new Vector2(0f, -ry - 8f), c + new Vector2(0f, -gap), 1.3f, k.WithAlpha(0.8f));
                    g.Line(c + new Vector2(0f, ry + 8f), c + new Vector2(0f, gap), 1.3f, k.WithAlpha(0.8f));
                    break;
                }
            }
        }

        /// <summary>
        /// The selected bird's burn rings: the real cost is 1.5 + 36 per map unit of distance, so a burn for X percent reaches (X - 1.5) / 36 units: rings
        /// at 10, 25 and 50 percent (amber while the tank pays for them, red when it does not) and a bold ring for what its remaining fuel can reach.
        /// </summary>
        private void DrawCostRings(FrontVector g, in OrbitTrackView b, float pxU, float pxV)
        {
            Vector2 c = BirdPoint(b);
            float alpha = relocating ? 0.9f : 0.6f;
            Color amber = RoomInk.Telemetry, red = AvInk.State(AvState.Danger);
            for (int i = 0; i < CostLevels.Length; i++)
            {
                float d = (CostLevels[i] - GeoBird.BurnBase) / GeoBird.BurnPerUnit, rx = d * pxU, ry = d * pxV;
                if (rx > 1500f) continue;
                Color k = b.Fuel >= CostLevels[i] ? amber : red;
                EllRing(g, c, rx, ry, 2f, k.WithAlpha(alpha), 3);
                AddRingWord(c, rx, ry, "-" + Mathf.RoundToInt(CostLevels[i]) + " %", k);
            }
            if (b.Fuel > GeoBird.BurnBase + 0.5f && !b.Relocating)
            {
                float d = (b.Fuel - GeoBird.BurnBase) / GeoBird.BurnPerUnit, rx = d * pxU, ry = d * pxV;
                if (rx < 1500f)
                {
                    EllRing(g, c, rx, ry, 2f, amber.WithAlpha(0.9f));
                    AddRingWord(c, rx, ry, "REACH", amber);
                }
            }
        }

        private void AddRingWord(Vector2 c, float rx, float ry, string word, Color ink)
        {
            float[] ang = { -45f, -135f, 45f, 135f, -90f, 90f };
            for (int i = 0; i < ang.Length; i++)
            {
                Vector2 p = EllP(c, rx, ry, ang[i] * Mathf.Deg2Rad);
                if (!plate.Shown(p, 22f)) continue;
                ringAt.Add(p); ringWord.Add(word); ringInk.Add(ink);
                return;
            }
        }

        /// <summary>A burn under way: the dashed path from where it left to the new parked point, chevrons along what is left of it, a ghost footprint and a ghost satellite at the destination.</summary>
        private void DrawTransfer(FrontVector g, in OrbitTrackView b, Color k, float pxU, float pxV)
        {
            Vector2 a = plate.P(b.FromU, b.FromV), cur = BirdPoint(b), z = plate.P(b.TargetU, b.TargetV);
            g.Dashed(a, cur, 1.2f, k.WithAlpha(0.3f), 4f, 5f);
            g.Dashed(cur, z, 4.5f, AvInk.Ground.WithAlpha(0.45f), 7f, 5f);
            g.Dashed(cur, z, 1.9f, k.WithAlpha(0.95f), 7f, 5f);
            Vector2 d = z - cur;
            float len = d.magnitude;
            if (len > 40f)
            {
                Vector2 u = d / len, nrm = new Vector2(-u.y, u.x);
                for (float s = 30f; s < len - 14f; s += 34f)
                {
                    Vector2 p = cur + u * s;
                    g.Tri(p + u * 5f, p - u * 3f + nrm * 4f, p - u * 3f - nrm * 4f, k.WithAlpha(0.9f));
                }
            }
            EllDisc(g, z, b.Radius * pxU, b.Radius * pxV, k.WithAlpha(0.05f));
            EllRing(g, z, b.Radius * pxU, b.Radius * pxV, 1.4f, k.WithAlpha(0.6f), 3);
            Sat(g, z, k.WithAlpha(0.7f), false, 0.9f);
        }

        /// <summary>The bird's nadir marker: a satellite glyph in a dashed station-keeping box (flavour); a lost bird is a hollow grey glyph with a cross.</summary>
        private void DrawNadir(FrontVector g, in OrbitTrackView b, Color k)
        {
            Vector2 p = BirdPoint(b);
            if (!b.Alive)
            {
                Sat(g, p, Mc.Lost, false, 1f);
                FrontKit.Cross(g, p + new Vector2(0f, 20f), 5f, AvInk.Muted);
                return;
            }
            DashRect(g, p, 26f, 16f, 1f, k.WithAlpha(0.6f));
            Sat(g, p, k, true, 1.1f);
            if (selKind == (int)b.Kind && b.Own) g.Ring(p, 24f, 1.6f, AvInk.Select, 32);
        }

        private bool Local(Vector2 mouse, out Vector2 p)
        {
            p = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(situation.Rect, mouse, null, out Vector2 lp)) return false;
            p = new Vector2(lp.x - situation.Rect.rect.xMin, situation.Rect.rect.yMax - lp.y);
            return true;
        }

        /// <summary>On the map plate and not under the legend.</summary>
        private bool OnMap(Vector2 p)
        {
            FrontMapView mv = plate.View;
            return p.x >= mv.X && p.x <= mv.X + mv.W && p.y >= mv.Y && p.y <= mv.Y + mv.H && !plate.Labels.Blocked(new Rect(p.x - 1f, p.y - 1f, 2f, 2f));
        }

        public bool Tick(Vector2 mouse)
        {
            FrontMapView mv = plate.View;
            Vector2 center = mv.Center;   // the view as the press found it: a press on the inset overview moves it, and that is no click on the map
            int zoomStep = mv.Zoom;
            if (Enlarged) { pressed = false; return false; } // the camera covers the map: no pan, zoom or placement under it
            bool changed = plate.Tick(mouse);
            if (!relocating || last == null) { pressed = false; return changed; }
            if (FrontPointer.Down(0))
            {
                pressed = Local(mouse, out pressAt) && OnMap(pressAt);
                pressCenter = center; pressZoom = zoomStep;
            }
            if (pressed && (FrontPointer.Held(1) || FrontPointer.Held(2))) pressed = false;   // a pan gesture is no click
            if (!pressed || !FrontPointer.Up(0)) return changed;
            pressed = false;
            // Only a click that stayed put on an unmoved, unzoomed map proposes a point.
            if (!Local(mouse, out Vector2 p) || !OnMap(p) || (p - pressAt).sqrMagnitude > DragSlop * DragSlop || mv.Zoom != pressZoom || (mv.Center - pressCenter).sqrMagnitude > 1e-8f) return changed;
            Vector2 uv = mv.UV(p);
            pending = new Vector2(Mathf.Clamp(uv.x, 0.02f, 0.98f), Mathf.Clamp(uv.y, 0.02f, 0.98f));
            hasPending = true;
            actions.Touch();
            return true;
        }

        // ---- The console column --------------------------------------------------------------------------------------

        /// <summary>mission seconds as HH:MM:SS (MET); cosmetic read-out of the mission clock, capped at 99 hours.</summary>
        private static string Met(float seconds)
        {
            int t = Mathf.Clamp(Mathf.FloorToInt(seconds), 0, 99 * 3600 + 3599);
            return (t / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" + (t / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (t % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private void PaintColumn(FrontRoomView v)
        {
            PaintCards(v);
            FrontVector c = colG;
            c.Clear();
            c.Line(new Vector2(mapW + 2f, 26f), new Vector2(mapW + 2f, TopH - 4f), 1f, RoomInk.Bezel.WithAlpha(0.7f));

            // MET and the flight director's call.
            Slab(c, colPx, FdY, PW, FdH, RoomInk.Telemetry);
            c.Line(new Vector2(colPx + 170f, FdY + 6f), new Vector2(colPx + 170f, FdY + FdH - 6f), 1f, RoomInk.Bezel.WithAlpha(0.8f));
            FrontKit.Set(fdMet, Met(v.Now), RoomInk.Telemetry);
            c.Rect(colPx + 176f, FdY + 14f, 92f, 22f, LampInk(callLamp));
            c.Rect(colPx + 176f, FdY + 14f, 92f, 1f, Color.white.WithAlpha(0.28f));
            FrontKit.Set(fdCall, callWord, callLamp == Lamp.Off ? Mc.Steel : Mc.PlateInk);

            // The hostile tally.
            int foeSeen = 0, foeOver = 0, nf = 0;
            string fuels = "";
            for (int i = 0; i < v.Birds.Count; i++)
            {
                OrbitTrackView b = v.Birds[i];
                if (b.Own) continue;
                foeSeen++;
                if (b.Alive && b.Overhead) foeOver++;
                if (nf++ < 3) fuels += (fuels.Length > 0 ? " / " : "") + (b.Alive ? Mathf.RoundToInt(b.Fuel).ToString(CultureInfo.InvariantCulture) : "--");
            }
            Slab(c, colPx, HostY, PW, HostH, AvInk.Hostile);
            FrontKit.Set(hostA, "HOSTILE · " + foeSeen + "/3 SEEN · " + foeOver + " OVER FOCUS", foeSeen > 0 ? AvInk.Hostile : Mc.Steel);
            FrontKit.Set(hostB, foeSeen > 0 ? "FUEL " + fuels + " % · WATCHING THE THEATRE" : "NONE TRACKED YET", Mc.Steel);

            // The inspector.
            bool bar = selKind >= 0 && TryBird(selKind, out OrbitTrackView rb);
            SetBar(bar, bar && relocating);
            Slab(c, colPx, InspY, PW, BarH, RoomInk.Telemetry);
            if (bar)
            {
                TryBird(selKind, out OrbitTrackView ib);
                PaintInspector(c, v, ib);
            }
            PaintSites(v);
            c.Flush();
        }

        /// <summary>The inspected bird: identity, slot and footprint, the fuel gauge (with the proposed burn drawn on it) and the RELOCATE / CONFIRM BURN / CANCEL controls.</summary>
        private void PaintInspector(FrontVector c, FrontRoomView v, in OrbitTrackView b)
        {
            bool mode = relocating;
            FrontKit.Set(selHead, "<b>" + b.Callsign + "</b> · " + C2Orbit.BirdName(b.Kind), RoomInk.Engrave);
            FrontKit.Set(selState, b.Relocating ? "BURN · ETA " + FrontKit.Clock(b.EtaSeconds) : mode ? "RELOCATING" : "PARKED", b.Relocating || mode ? FrontKit.Tone(AvState.Caution) : FrontKit.Tone(b.Tone));
            float fuel = b.Fuel, after = fuel;
            bool afford = true;
            string line, hint;
            Color hintInk = Mc.Steel;
            if (!mode)
            {
                line = C2Kit.Tint(string.IsNullOrEmpty(b.Slot) ? "GEO" : "SLOT " + b.Slot, RoomInk.Telemetry) + " · FOOTPRINT " + Mathf.RoundToInt(b.FootprintKm) + " KM";
                hint = b.Relocating ? "BURN UNDER WAY · WAIT FOR IT TO PARK" : CanRelocate(b) ? "FUEL SCALES WITH DISTANCE · SEE THE RINGS" : "NOT ENOUGH FUEL TO BURN";
                OpsKit.Enable(relocBtn, CanRelocate(b));
            }
            else if (!hasPending)
            {
                line = "CLICK THE MAP FOR THE NEW PARKED POINT";
                hint = "THE MAP DRAGS AND ZOOMS FREELY";
                OpsKit.Enable(burnBtn, false);
            }
            else
            {
                float cost = PendingCost(b), dist = PendingDistance(b);
                afford = b.Fuel >= cost;
                after = Mathf.Max(0f, fuel - cost);
                line = "→ " + C2Kit.Tint("SLOT " + FrontViews.SlotLon(v.Faction, true, pending.x, v.TheatreKm * 1000f), RoomInk.Telemetry) + " · TRANSFER " + FrontKit.Clock(dist / GeoBird.Slew);
                hint = cost <= 0f ? "ALREADY PARKED THERE" : afford ? "BURN COST -" + Mathf.RoundToInt(cost) + " % FUEL · FOOTPRINT MOVES" : "NOT ENOUGH FUEL · NEED " + Mathf.CeilToInt(cost) + " %";
                hintInk = afford && cost > 0f ? FrontKit.Tone(AvState.Caution) : FrontKit.Tone(AvState.Danger);
                OpsKit.Enable(burnBtn, CanBurn(b));
            }
            FrontKit.Set(selLine, line, RoomInk.Engrave);
            FrontKit.Set(selHint, hint, hintInk);
            FrontKit.Set(selFuel, mode && hasPending && afford ? Mathf.RoundToInt(fuel) + " → " + Mathf.RoundToInt(after) + " %" : Mathf.RoundToInt(fuel) + " %", RoomInk.Engrave);

            // The gauge: what is left in the tank, with the proposed burn laid over it (ten segments' worth of ticks, the quarters heavier).
            Rect f = fuelRect;
            c.Rect(f.x, f.y, f.width, f.height, Mc.PlateInk.WithAlpha(0.95f));
            Color tank = b.Alive ? Own : AvInk.Dim;
            if (mode && hasPending && !afford) c.Rect(f.x + 1f, f.y + 1f, (f.width - 2f) * Mathf.Clamp01(fuel / 100f), f.height - 2f, AvInk.State(AvState.Danger));
            else
            {
                c.Rect(f.x + 1f, f.y + 1f, (f.width - 2f) * Mathf.Clamp01(after / 100f), f.height - 2f, tank);
                if (after < fuel) c.Rect(f.x + 1f + (f.width - 2f) * Mathf.Clamp01(after / 100f), f.y + 1f, (f.width - 2f) * Mathf.Clamp01((fuel - after) / 100f), f.height - 2f, AvInk.State(AvState.Caution));
            }
            for (int i = 1; i < 10; i++) c.Rect(f.x + f.width * i / 10f, f.y, 1f, i % 5 == 0 ? f.height : f.height * 0.4f, Mc.PlateInk.WithAlpha(0.9f));
            c.Frame(f.x, f.y, f.width, f.height, 1f, RoomInk.Bezel);
        }

        /// <summary>The uplink sites: own ones as two-line cards (state, grid, what losing it costs), the revealed hostile ones as one line. Nothing is drawn for a site the mirror does not carry.</summary>
        private void PaintSites(FrontRoomView v)
        {
            int own = 0, foe = 0, live = 0;
            for (int i = 0; i < v.Sites.Count; i++) { if (v.Sites[i].Own) { own++; if (v.Sites[i].Live) live++; } else foe++; }
            float y = SiteY0;
            int oi = 0, fi = 0;
            for (int i = 0; i < v.Sites.Count; i++)
            {
                OrbitSiteView s = v.Sites[i];
                if (s.Own && oi < siteCards.Length) { siteCards[oi++].Paint(s, y); y += 32f; }
            }
            for (int i = 0; i < v.Sites.Count; i++)
            {
                OrbitSiteView s = v.Sites[i];
                if (!s.Own && fi < siteChips.Length) { siteChips[fi++].Paint(s, y); y += 22f; }
            }
            for (int i = oi; i < siteCards.Length; i++) siteCards[i].Hide();
            for (int i = fi; i < siteChips.Length; i++) siteChips[i].Hide();
            FrontKit.Set(siteHeadOwn, "UPLINKS · " + (own == 0 ? "NONE" : live + "/" + own + " LIVE"), Own);
            FrontKit.Set(siteHeadFoe, "HOSTILE · " + (foe == 0 ? "NONE REVEALED" : foe + " REVEALED"), AvInk.Hostile);
        }

        /// <summary>
        /// The own constellation as a GO / NO-GO poll (PWR, ATT, PROP, LINK lamps per bird). Also settles the flight director's call: the worst lamp of any
        /// bird on the loop. Lamp rules (cosmetic, derived): PWR = alive; ATT amber while the bird burns; PROP green over 50 % fuel, amber over 20 %, else
        /// red; LINK green while any own uplink site is live, else red.
        /// </summary>
        private void PaintCards(FrontRoomView v)
        {
            int ownUp = 0;
            var own = new OrbitTrackView?[3];
            float minFuel = 101f;
            for (int i = 0; i < v.Birds.Count; i++)
            {
                OrbitTrackView b = v.Birds[i];
                if (!b.Own) continue;
                int k = Array.IndexOf(Kinds, b.Kind);
                if (k >= 0) own[k] = b;
                if (b.Alive) { ownUp++; minFuel = Mathf.Min(minFuel, b.Fuel); }
            }
            bool linkLive = false;
            int sitesOwn = 0;
            for (int i = 0; i < v.Sites.Count; i++) if (v.Sites[i].Own) { sitesOwn++; if (v.Sites[i].Live) linkLive = true; }
            int present = 0;
            Lamp worst = Lamp.Off;
            for (int i = 0; i < 3; i++)
            {
                ownCards[i].Paint(own[i], Kinds[i], linkLive);
                ownCards[i].Mark(selKind == (int)Kinds[i]);
                if (ownCards[i].Present) { present++; if (ownCards[i].Worst > worst) worst = ownCards[i].Worst; }
            }
            if (ownUp < 3 && worst < Lamp.Hold) worst = Lamp.Hold; // a constellation short of a bird is never ALL GO
            callLamp = present == 0 ? Lamp.Off : worst == Lamp.NoGo ? Lamp.NoGo : worst == Lamp.Hold ? Lamp.Hold : Lamp.Go;
            callWord = present == 0 ? "NO BIRDS" : callLamp == Lamp.NoGo ? "NO-GO" : callLamp == Lamp.Hold ? "HOLD" : "ALL GO";
        }

        /// <summary>Cosmetic flavour: which console would call the line on the voice loop, read off its wording.</summary>
        private static string Loop(string text)
        {
            text = text ?? "";
            if (text.Contains("BURN") || text.Contains("FUEL") || text.Contains("SLOT")) return "FDO ";
            if (text.Contains("MARK") || text.Contains("TRANSMIT") || text.Contains("TRACK")) return "SURV";
            return "INCO";
        }

        private void PaintSensor(FrontRoomView v)
        {
            SpaceFeedView f = v.Feed;
            // A cockpit warning outranks the sensor line: the pilot flying with this room open still sees it.
            if (f.ThreatActive) sensor.SetMeta(C2Words.Fit(f.Threat, 58), FrontKit.Tone(AvState.Danger));
            else sensor.SetMeta((f.Source == BirdKind.Radar ? "RADAR SAR" : "OPTICAL") + " · ZOOM " + (f.ZoomEnabled ? SpaceFeedRules.ZoomWord(f.Zoom) : "—"), AvInk.Dim);
            optical.Latched = f.Source == BirdKind.Optical;
            radar.Latched = f.Source == BirdKind.Radar;
            OpsKit.Label(zoom, f.ZoomEnabled ? "ZOOM " + SpaceFeedRules.ZoomWord(f.Zoom) : "ZOOM —");
            OpsKit.Enable(zoom, f.ZoomEnabled);
            bool none = f.Image == null || f.ImageKind == FeedImageKind.None || (f.Refusal != null && f.Refusal.Length > 0);
            string age = none ? "NO PICTURE" : f.ImageAge < 0f ? "LOOK TIME UNKNOWN" : (f.Source == BirdKind.Radar ? "SCAN " : "LOOK ") + C2Words.Clock(f.ImageAge) + " AGO" + (f.ImageStale ? " · STALE" : "");
            FrontKit.Set(sensorNote, age, f.ImageStale ? FrontKit.Tone(AvState.Caution) : Mc.Steel);
            picture.Paint(f);
            if (shown != null && shown.Mark != null)
            {
                OpsKit.Label(shown.Mark, f.ConfirmFull ? "MARKS FULL" : f.SelectedId > 0 ? "MARK " + C2Orbit.TrackId(f.SelectedId) : "MARK");
                OpsKit.Enable(shown.Mark, f.CanConfirm);
                OpsKit.Label(shown.Send, C2Orbit.TransmitLabel(f.SendCount));
                OpsKit.Enable(shown.Send, f.CanSend);
            }
            DrawStandby(none);
            PaintBurn(v, none);
        }

        private static float TextW(string s, float size) => (s ?? "").Length * size * 0.6f;

        /// <summary>One burn-in line on its dark plate: the plate is drawn into the burn layer, the text sits over it in the sensor box.</summary>
        private void BurnLine(TMP_Text t, FrontVector g, string s, Color ink, float x, float y, float size, bool right)
        {
            float w = TextW(s, size) + 6f, pw = w + 8f;
            float px = right ? x - pw : x;
            g.Rect(px, y, pw, 18f, RoomInk.Plate.WithAlpha(0.86f));
            g.Frame(px, y, pw, 18f, 1f, RoomInk.Bezel.WithAlpha(0.6f));
            FrontKit.Set(t, s, ink);
            AvLay.Place(t.rectTransform, PicX + px + 4f, PicY + y + 1f, w, 16f);
        }

        /// <summary>
        /// The camera burn-in over the picture (cosmetic flavour, nothing on the wire): crop marks, a centre reticle with mil ticks, an
        /// "EO · FRAME 0412 · GSD 0.6 M" line (the frame count runs off the mission clock, the ground sample distance off the zoom step), the source
        /// bird, a red REC lamp (STBY while there is no picture) and a time stamp. The reticle stays off a standby pattern, which carries its own.
        /// </summary>
        private void PaintBurn(FrontRoomView v, bool none)
        {
            SpaceFeedView f = v.Feed;
            bool sar = f.Source == BirdKind.Radar;
            FrontVector g = burn;
            g.Clear();
            float W = picW, H = picH, floorY = H - 18f - 8f, mid = floorY * 0.5f + 4f;
            Color ink = RoomInk.Engrave, dark = RoomInk.Plate.WithAlpha(0.55f);

            // Crop marks, above the picture's own status strip.
            const float inset = 10f, arm = 22f;
            for (int sx = 0; sx < 2; sx++)
                for (int sy = 0; sy < 2; sy++)
                {
                    Vector2 o = new Vector2(sx == 0 ? inset : W - inset, sy == 0 ? inset : floorY);
                    float dx = sx == 0 ? 1f : -1f, dy = sy == 0 ? 1f : -1f;
                    g.Line(o + new Vector2(-dx * 1.7f, 0f), o + new Vector2(arm * dx, 0f), 3.6f, dark);
                    g.Line(o + new Vector2(0f, -dy * 1.7f), o + new Vector2(0f, arm * dy), 3.6f, dark);
                    g.Line(o, o + new Vector2(arm * dx, 0f), 1.5f, ink);
                    g.Line(o, o + new Vector2(0f, arm * dy), 1.5f, ink);
                }

            // Reticle with mil ticks.
            if (!none)
            {
                Vector2 c = new Vector2(W * 0.5f, mid);
                g.Ring(c, 24f, 3.4f, dark, 48);
                g.Ring(c, 24f, 1.2f, ink, 48);
                for (int axis = 0; axis < 2; axis++)
                {
                    float reach = axis == 0 ? 112f : Mathf.Min(112f, mid - 16f);   // the ladder never runs off a short picture
                    int ticks = Mathf.Max(0, Mathf.FloorToInt((reach - 28f) / 8f));
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        Vector2 d = axis == 0 ? new Vector2(sgn, 0f) : new Vector2(0f, sgn), n = new Vector2(-d.y, d.x);
                        g.Line(c + d * 28f, c + d * (28f + ticks * 8f), 3.4f, dark);
                        g.Line(c + d * 28f, c + d * (28f + ticks * 8f), 1.2f, ink);
                        for (int t = 1; t <= ticks; t++)
                        {
                            float at = 28f + t * 8f, hh = t % 5 == 0 ? 5.5f : 3f;
                            g.Line(c + d * at - n * hh, c + d * at + n * hh, 1.2f, ink);
                        }
                    }
                }
                g.Disc(c, 1.9f, RoomInk.Telemetry, 10);

                // Edge ladders: a tick every 16 px down both sides, a longer one every fifth, clear of the crop marks.
                for (int t = 0; ; t++)
                {
                    float y = inset + arm + 14f + t * 16f;
                    if (y > floorY - arm - 14f) break;
                    float len = t % 5 == 0 ? 8f : 4.5f;
                    g.Line(new Vector2(inset, y), new Vector2(inset + len, y), 1.2f, ink.WithAlpha(0.8f));
                    g.Line(new Vector2(W - inset, y), new Vector2(W - inset - len, y), 1.2f, ink.WithAlpha(0.8f));
                }
            }

            // Data lines.
            int frame = Mathf.FloorToInt(Mathf.Max(0f, v.Now) * 0.412f) % 10000;
            string fr = frame.ToString("0000", CultureInfo.InvariantCulture);
            string gsd = f.Zoom <= 0 ? "1.2" : f.Zoom == 1 ? "0.6" : "0.3";
            string tl = none ? (sar ? "SAR" : "EO") + " · NO FRAME · STANDBY"
                : sar ? "SAR · SCAN " + fr + " · RES 1.0 M" : "EO · FRAME " + fr + " · GSD " + gsd + " M";
            string who = "NO BIRD ON STATION";
            for (int i = 0; i < v.Birds.Count; i++)
            {
                OrbitTrackView b = v.Birds[i];
                if (!b.Own || b.Kind != f.Source) continue;
                who = (b.Callsign ?? "") + " · " + (string.IsNullOrEmpty(b.Catalog) ? C2Orbit.BirdName(b.Kind) : b.Catalog) + (string.IsNullOrEmpty(b.Slot) ? "" : " · SLOT " + b.Slot);
                break;
            }
            const float edge = 32f;
            BurnLine(burnTl, g, tl, ink, edge, 8f, 10.5f, false);
            BurnLine(burnTl2, g, who, Mc.Steel, edge, 30f, 10f, false);
            string stamp = "T+" + Met(v.Now) + (string.IsNullOrEmpty(v.Dtg) ? "" : " · " + v.Dtg);
            BurnLine(burnBr, g, stamp, ink, W - edge, floorY - 20f, 10f, true);

            // REC lamp top right (a ring and STBY while there is nothing to record).
            string rec = none ? "STBY" : "REC";
            Color recInk = none ? RoomInk.Telemetry : RoomInk.Trace;
            float rw = TextW(rec, 10.5f) + 6f + 22f, rx = W - edge - rw;
            g.Rect(rx, 8f, rw, 18f, RoomInk.Plate.WithAlpha(0.86f));
            g.Frame(rx, 8f, rw, 18f, 1f, RoomInk.Bezel.WithAlpha(0.6f));
            if (none) g.Ring(new Vector2(rx + 11f, 17f), 4f, 1.6f, recInk, 16);
            else g.Disc(new Vector2(rx + 11f, 17f), 4.2f, recInk, 16);
            FrontKit.Set(burnTr, rec, recInk);
            AvLay.Place(burnTr.rectTransform, PicX + rx + 20f, PicY + 9f, TextW(rec, 10.5f) + 6f, 16f);
            g.Flush();
        }

        /// <summary>The no-picture state: a dead-air test pattern. It marks no swath and no target, so it can never pass for coverage; the refusal in the middle says why.</summary>
        private void DrawStandby(bool on)
        {
            if (on == standbyDrawn) return;
            standbyDrawn = on;
            FrontVector g = standby;
            g.Clear();
            if (on)
            {
                Color k = AvInk.Key;
                Vector2 c = new Vector2(picW * 0.5f, picH * 0.5f);
                for (float x = 0f; x <= picW; x += 40f) g.Line(new Vector2(x, 0f), new Vector2(x, picH), 1f, k.WithAlpha(0.10f));
                for (float y = 0f; y <= picH; y += 40f) g.Line(new Vector2(0f, y), new Vector2(picW, y), 1f, k.WithAlpha(0.10f));
                for (int i = 1; i <= 4; i++) g.Ring(c, 38f * i, 1.2f, k.WithAlpha(0.28f), 72, i % 2 == 0 ? 1f : 0f, 1f);
                g.Line(new Vector2(0f, c.y), new Vector2(picW, c.y), 1f, k.WithAlpha(0.3f));
                g.Line(new Vector2(c.x, 0f), new Vector2(c.x, picH), 1f, k.WithAlpha(0.3f));
                for (float x = 8f; x < picW - 8f; x += 10f) g.Line(new Vector2(x, picH - 22f), new Vector2(x, picH - (x % 50f < 10f ? 12f : 17f)), 1f, k.WithAlpha(0.5f));
            }
            g.Flush();
        }

        private void PaintTracks(SpaceFeedView f)
        {
            bool none = f.NoContacts != null && f.NoContacts.Length > 0;
            int shown = 0;
            for (int i = 0; i < TrackRows; i++)
            {
                FeedTileView t = i < f.Tiles.Length ? f.Tiles[i] : default;
                if (none || !t.Present)
                {
                    // One worded row says why the file is empty and what to do; the slots under it are simply not drawn.
                    if (none && i == 0) rows[i].Ghost("NO TRACKS YET", "");
                    else rows[i].Hide();
                    continue;
                }
                rowIds[i] = t.Id;
                rows[i].Paint(t);
                shown++;
            }
            if (trackEmpty.gameObject.activeSelf != none) trackEmpty.gameObject.SetActive(none);
            if (none) FrontKit.Set(trackEmpty, f.NoContacts.StartsWith("NO CONTACTS — ", StringComparison.Ordinal) ? f.NoContacts.Substring(14) : f.NoContacts, Mc.Steel);
            trackBox.SetMeta(none ? "NO TRACKS" : C2Orbit.PageMeta(f.Page, f.Pages) + " · " + shown + " ON PAGE", AvInk.Dim);
            OpsKit.Enable(prev, !none && f.Page > 0);
            OpsKit.Enable(next, !none && f.Page + 1 < f.Pages);
            OpsKit.Label(mark, f.ConfirmFull ? "MARKS FULL" : f.SelectedId > 0 ? "MARK " + C2Orbit.TrackId(f.SelectedId) : "MARK");
            OpsKit.Enable(mark, f.CanConfirm);
            OpsKit.Label(send, C2Orbit.TransmitLabel(f.SendCount));
            OpsKit.Enable(send, f.CanSend);
            PaintChain(f);
        }

        /// <summary>SELECT, MARK, TRANSMIT as three chips (the first one not yet done is the live step) and the selected track's brief under them.</summary>
        private void PaintChain(SpaceFeedView f)
        {
            FeedBriefView br = f.Brief;
            bool selected = f.SelectedId > 0, marked = selected && br.Has && br.Marked;
            bool posted = f.Posted, queued = f.SendCount > 0;
            chain.Clear();
            int live = !selected ? 0 : !marked ? 1 : 2;
            Chip(0, selected, live == 0, "1 SELECT" + (selected ? " " + C2Orbit.TrackId(f.SelectedId) : ""));
            Chip(1, marked, live == 1, "2 MARK " + (marked ? "DONE" : f.ConfirmFull ? "FULL" : f.CanConfirm ? "READY" : "WAIT"));
            Chip(2, posted, live == 2 && !posted, "3 TRANSMIT " + (posted ? "POSTED" : queued ? f.SendCount.ToString() : "WAIT"));
            chain.Flush();

            if (br.Has)
            {
                string cls = br.Percent > 0 && br.Percent <= 100 ? br.Title + " " + br.Percent + " %" : br.Title;
                FrontKit.Set(briefA, C2Kit.Tint("<b>" + C2Orbit.TrackId(br.Id) + "</b>", RoomInk.Telemetry) + " · " + C2Kit.Tint(cls, FeedInk.Class(br.Class)) + " · " + (br.Moving ? "MOVING" : "STATIC") +
                    (br.FixedPoint ? " · FIXED POINT" : br.Marked ? " · MARKED" : ""), RoomInk.Engrave);
            }
            else FrontKit.Set(briefA, "CLICK A BRACKET OR A ROW TO SELECT A TRACK", Mc.Steel);
            if (posted) FrontKit.Set(briefB, "POSTED TO TASKED · OPEN THE OPS MFD, TASKED TAB", FrontKit.Tone(AvState.Ready));
            else if (br.Has) FrontKit.Set(briefB, "GRID " + br.Grid + " · " + (br.FixedPoint ? "MARK EXPIRES IN " : "REVEAL ENDS IN ") + C2Words.Clock(Mathf.Max(0f, br.Left)), Mc.Steel);
            else FrontKit.Set(briefB, "", Mc.Steel);
        }

        /// <summary>One chip of the chain: a done step wears the ready hue, the live step the selection hue, the rest sit dim. The state is in the words too.</summary>
        private void Chip(int i, bool done, bool live, string text)
        {
            float x = 8f;
            for (int j = 0; j < i; j++) x += chainInner * ChainShare[j] + 8f;
            float cw = chainInner * ChainShare[i];
            Color ink = done ? OpsInk.Word(AvState.Ready) : live ? AvInk.Select : AvInk.Dim;
            chain.Rect(x, ChainY, cw, 18f, ink.WithAlpha(live ? 0.22f : 0.10f));
            chain.Frame(x, ChainY, cw, 18f, 1f, ink.WithAlpha(live || done ? 0.95f : 0.55f));
            chain.Rect(x, ChainY, 3f, 18f, ink);
            if (i < chainText.Length - 1) chain.Tri(new Vector2(x + cw + 1.5f, ChainY + 5f), new Vector2(x + cw + 6.5f, ChainY + 9f), new Vector2(x + cw + 1.5f, ChainY + 13f), AvInk.Dim);
            FrontKit.Set(chainText[i], text, done || live ? AvInk.Ink : AvInk.Dim);
        }

        // ---- Parts ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// One own bird as a compact poll row: callsign and state on top, kind and what it is doing under, then four lamps (PWR ATT PROP LINK) lit GO /
        /// caution / NO-GO with the word in the lamp, and the fuel as a bar along the foot. A click selects the bird for the inspector.
        /// </summary>
        private sealed class PollRow
        {
            private static readonly string[] Subsystems = { "PWR", "ATT", "PROP", "LINK" };
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly FrontVector lamps;
            private readonly TMP_Text glyph, name, state, spec;
            private readonly TMP_Text[] tile = new TMP_Text[4];
            private readonly Lamp[] lit = new Lamp[4];
            private readonly string[] words = new string[4];
            private readonly float w;
            private bool selected;

            public bool Present { get; private set; }

            public Lamp Worst { get; private set; }

            public PollRow(RectTransform p, FrontSkin skin, float x, float y, float width, Action click)
            {
                w = width;
                RectTransform root = AvLay.Child(p, "OwnCard");
                AvLay.Place(root, x, y, w, RowH);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, w, RowH, 0f, 0f);
                rail = FrontKit.Solid(root, "Rail", 0f, 0f, 3f, RowH, Color.clear);
                lamps = FrontVector.Add(root, "Lamps");
                AvLay.Place(lamps.rectTransform, 0f, 0f, w, RowH);
                glyph = AvIcons.Make(root, AvIcon.Satellite, 14f, AvInk.Dim);
                AvLay.Place(glyph.rectTransform, 9f, 2f, 14f, 14f);
                name = FrontKit.Mono(root, "Name", 28f, 0f, 140f, 17f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                state = FrontKit.Mono(root, "State", w - 8f - 124f, 0f, 124f, 17f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                spec = FrontKit.Mono(root, "Spec", 10f, 16f, w - 20f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
                for (int i = 0; i < tile.Length; i++)
                    tile[i] = FrontKit.Mono(root, "Lamp" + i, TileX(i), 31f, TileW, 14f, 10f, TextAlignmentOptions.Midline, true);
                AvHit hit = AvHit.On(frame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) click(); };
                skin.Add(Restyle);
            }

            private float TileW => (w - 20f - 9f) / 4f;

            private float TileX(int i) => 10f + i * (TileW + 3f);

            public void Mark(bool on) { if (selected == on) return; selected = on; Restyle(); }

            private void Restyle() =>
                frame.Paint(selected ? Color.Lerp(RoomInk.ConsoleBody, AvInk.Select, 0.14f).WithAlpha(0.95f) : RoomInk.ConsoleBody, selected ? AvInk.Select : RoomInk.Bezel.WithAlpha(0.8f));

            public void Paint(OrbitTrackView? t, BirdKind slot, bool linkLive)
            {
                float fuel = 0f;
                if (!t.HasValue)
                {
                    Present = false;
                    rail.color = AvInk.Muted;
                    glyph.color = AvInk.Muted;
                    FrontKit.Set(name, C2Orbit.BirdName(slot) + " · NO BIRD", Mc.Steel);
                    FrontKit.Set(state, "DOWN", FrontKit.Tone(AvState.Danger));
                    FrontKit.Set(spec, "QUEUE LAUNCH SATELLITE TO REPLACE IT", Mc.Steel);
                    for (int i = 0; i < 4; i++) { lit[i] = Lamp.Off; words[i] = Subsystems[i] + " --"; }
                }
                else
                {
                    OrbitTrackView b = t.Value;
                    Present = true;
                    Color k = !b.Alive ? AvInk.Dim : Own;
                    rail.color = b.Alive ? k : AvInk.State(AvState.Danger);
                    glyph.color = k;
                    fuel = b.Alive ? Mathf.Clamp01(b.Fuel / 100f) : 0f;
                    FrontKit.Set(name, b.Callsign, RoomInk.Engrave);
                    FrontKit.Set(state, b.State, FrontKit.Tone(b.Tone));
                    string pass = !b.Alive ? "NO SIGNAL" : b.Relocating ? "BURN " + FrontKit.Clock(b.EtaSeconds) + " · -" + Mathf.RoundToInt(b.BurnCost) + " %" : b.Overhead ? "OVER FOCUS" : "PARKED";
                    FrontKit.Set(spec, C2Orbit.BirdName(b.Kind) + " · " + pass, b.Relocating ? FrontKit.Tone(AvState.Caution) : b.Overhead && b.Alive ? FrontKit.Tone(AvState.Ready) : Mc.Steel);
                    if (!b.Alive)
                    {
                        lit[0] = Lamp.NoGo; words[0] = "PWR LOST";
                        for (int i = 1; i < 4; i++) { lit[i] = Lamp.Off; words[i] = Subsystems[i] + " --"; }
                    }
                    else
                    {
                        lit[0] = Lamp.Go; words[0] = "PWR GO";
                        lit[1] = b.Relocating ? Lamp.Hold : Lamp.Go; words[1] = b.Relocating ? "ATT BURN" : "ATT GO";
                        lit[2] = b.Fuel > 50f ? Lamp.Go : b.Fuel > 20f ? Lamp.Hold : Lamp.NoGo; words[2] = "PROP " + Mathf.RoundToInt(b.Fuel) + "%";
                        lit[3] = linkLive ? Lamp.Go : Lamp.NoGo; words[3] = linkLive ? "LINK GO" : "LINK DOWN";
                    }
                }
                Worst = Lamp.Off;
                for (int i = 0; i < 4; i++) if (lit[i] > Worst) Worst = lit[i];
                if (!Present) Worst = Lamp.Off;

                lamps.Clear();
                for (int i = 0; i < 4; i++)
                {
                    float lx = TileX(i);
                    Color ink = LampInk(lit[i]);
                    lamps.Rect(lx, 31f, TileW, 14f, ink);
                    if (lit[i] == Lamp.Off) lamps.Frame(lx, 31f, TileW, 14f, 1f, RoomInk.Bezel.WithAlpha(0.7f));
                    else lamps.Rect(lx, 31f, TileW, 1f, Color.white.WithAlpha(0.28f));
                    FrontKit.Set(tile[i], words[i], lit[i] == Lamp.Off ? Mc.Steel : Mc.PlateInk);
                }
                // The tank along the foot of the row.
                lamps.Rect(3f, 46f, w - 6f, 2f, Mc.Off);
                if (fuel > 0f) lamps.Rect(3f, 46f, (w - 6f) * fuel, 2f, LampInk(lit[2]));
                lamps.Flush();
            }
        }

        /// <summary>An own uplink site as a two-line card: state word, grid, and what losing the site costs. A click centres the map on it.</summary>
        private sealed class SiteCard
        {
            private readonly RectTransform root;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text glyph, name, state, line;
            private readonly Action<Vector2> click;
            private readonly float w, x;
            private Vector2 at;

            public SiteCard(RectTransform p, FrontSkin skin, float x, float y, float width, Action<Vector2> click)
            {
                this.click = click;
                w = width; this.x = x;
                root = AvLay.Child(p, "SiteCard");
                AvLay.Place(root, x, y, w, SiteH);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, w, SiteH, 0f, 0f);
                rail = FrontKit.Solid(root, "Rail", 0f, 0f, 3f, SiteH, Color.clear);
                glyph = AvIcons.Make(root, AvIcon.Antenna, 13f, AvInk.Dim);
                AvLay.Place(glyph.rectTransform, 9f, 1f, 13f, 13f);
                name = FrontKit.Mono(root, "Name", 26f, 0f, 150f, 15f, 11f, TextAlignmentOptions.MidlineLeft, true);
                state = FrontKit.Mono(root, "State", w - 8f - 100f, 0f, 100f, 15f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                line = FrontKit.Mono(root, "Line", 10f, 15f, w - 20f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
                AvHit hit = AvHit.On(frame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) this.click(at); };
                skin.Add(() => frame.Paint(RoomInk.ConsoleBody, RoomInk.Bezel.WithAlpha(0.8f)));
            }

            public void Hide() { if (root.gameObject.activeSelf) root.gameObject.SetActive(false); }

            public void Paint(in OrbitSiteView s, float y)
            {
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                AvLay.Place(root, x, y, w, SiteH);
                at = new Vector2(s.X, s.Y);
                Color k = s.Rebuilding ? AvInk.State(AvState.Caution) : !s.Live ? AvInk.State(AvState.Danger) : Own;
                rail.color = k;
                glyph.color = k;
                FrontKit.Set(name, s.Name, RoomInk.Engrave);
                FrontKit.Set(state, s.State, s.Rebuilding ? FrontKit.Tone(AvState.Caution) : !s.Live ? FrontKit.Tone(AvState.Danger) : FrontKit.Tone(AvState.Ready));
                string detail = string.IsNullOrEmpty(s.Cost) ? s.Grid ?? "" : (string.IsNullOrEmpty(s.Grid) ? "" : s.Grid + " · ") + s.Cost;
                FrontKit.Set(line, C2Kit.FitTo(line, detail, line.rectTransform.rect.width), Mc.Steel);
            }
        }

        /// <summary>A revealed hostile uplink as one line: name and grid left, the state word right. A click centres the map on it.</summary>
        private sealed class SiteChip
        {
            private readonly RectTransform root;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text name, state;
            private readonly Action<Vector2> click;
            private readonly float w, x;
            private Vector2 at;

            public SiteChip(RectTransform p, FrontSkin skin, float x, float y, float width, Action<Vector2> click)
            {
                this.click = click;
                w = width; this.x = x;
                root = AvLay.Child(p, "SiteChip");
                AvLay.Place(root, x, y, w, ChipH);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, w, ChipH, 0f, 0f);
                rail = FrontKit.Solid(root, "Rail", 0f, 0f, 3f, ChipH, Color.clear);
                name = FrontKit.Mono(root, "Name", 10f, 0f, w - 20f - 80f, ChipH, 10.5f, TextAlignmentOptions.MidlineLeft);
                state = FrontKit.Mono(root, "State", w - 8f - 80f, 0f, 80f, ChipH, 10.5f, TextAlignmentOptions.MidlineRight, true);
                AvHit hit = AvHit.On(frame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) this.click(at); };
                skin.Add(() => frame.Paint(RoomInk.ConsoleBody, RoomInk.Bezel.WithAlpha(0.8f)));
            }

            public void Hide() { if (root.gameObject.activeSelf) root.gameObject.SetActive(false); }

            public void Paint(in OrbitSiteView s, float y)
            {
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                AvLay.Place(root, x, y, w, ChipH);
                at = new Vector2(s.X, s.Y);
                rail.color = AvInk.Hostile;
                FrontKit.Set(name, s.Name + (string.IsNullOrEmpty(s.Grid) ? "" : " · " + s.Grid), RoomInk.Engrave);
                FrontKit.Set(state, s.State, AvInk.Hostile);
            }
        }

        /// <summary>A track-file row: an amber track number on its dark plate, the title engraved, class and percent on the right.</summary>
        private sealed class TrackRow
        {
            private readonly RectTransform root;
            private readonly AvFrame frame;
            private readonly Image rail, idPlate;
            private readonly TMP_Text id, title, sub, pct, cls;
            private bool selected;
            private ProbableClass klass;

            public TrackRow(RectTransform p, FrontSkin skin, float x, float y, float w, float h, Action click)
            {
                root = AvLay.Child(p, "Track");
                AvLay.Place(root, x, y, w, h);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, w, h, 0f, 0f);
                rail = FrontKit.Solid(root, "Rail", 0f, 0f, 3f, h, Color.clear);
                idPlate = FrontKit.Solid(root, "IdPlate", 7f, 5f, 42f, h - 10f, RoomInk.Plate.WithAlpha(1f));
                AvHit hit = AvHit.On(frame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) click(); };
                float ty = (h - 35f) * 0.5f;
                id = FrontKit.Mono(root, "Id", 7f, 5f, 42f, h - 10f, 12f, TextAlignmentOptions.Midline, true);
                title = FrontKit.Mono(root, "Title", 56f, ty, w - 144f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                sub = FrontKit.Mono(root, "Sub", 56f, ty + 17f, w - 144f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                pct = FrontKit.Mono(root, "Pct", w - 82f, ty, 76f, 18f, 12f, TextAlignmentOptions.MidlineRight, true);
                cls = FrontKit.Mono(root, "Cls", w - 82f, ty + 17f, 76f, 16f, 10f, TextAlignmentOptions.MidlineRight, false, 1f);
                skin.Add(Restyle);
            }

            private void Restyle()
            {
                frame.Paint(selected ? AvInk.Select.WithAlpha(0.18f) : RoomInk.ConsoleBody, selected ? AvInk.Select : RoomInk.Bezel.WithAlpha(0.7f));
                rail.color = ClassTone();
                idPlate.color = RoomInk.Plate.WithAlpha(1f);
            }

            private Color ClassTone() => klass == ProbableClass.Hostile ? AvInk.State(AvState.Danger) : klass == ProbableClass.Friendly ? AvInk.State(AvState.Ready)
                : klass == ProbableClass.Neutral ? AvInk.State(AvState.Info) : AvInk.State(AvState.Caution);

            public void Hide() { if (root.gameObject.activeSelf) root.gameObject.SetActive(false); }

            public void Ghost(string name, string note)
            {
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                selected = false; klass = ProbableClass.Unknown;
                frame.Paint(RoomInk.ConsoleBody.WithAlpha(0.55f), RoomInk.Bezel.WithAlpha(0.4f));
                rail.color = AvInk.Muted;
                idPlate.color = RoomInk.Plate.WithAlpha(0.6f);
                FrontKit.Set(id, "T--", Mc.Steel);
                FrontKit.Set(title, name, Mc.Steel);
                // The title already says there are no tracks: drop the line's "NO CONTACTS —" lead-in so the reason itself fits.
                const string Lead = "NO CONTACTS — ";
                if (note != null && note.StartsWith(Lead, StringComparison.Ordinal)) note = note.Substring(Lead.Length);
                FrontKit.Set(sub, FrontKit.Fit(sub, note, 10.5f), Mc.Steel);
                FrontKit.Set(pct, "", Mc.Steel);
                FrontKit.Set(cls, "", Mc.Steel);
            }

            public void Paint(in FeedTileView t)
            {
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                selected = t.Selected; klass = t.Class;
                Restyle();
                FrontKit.Set(id, C2Orbit.TrackId(t.Id), RoomInk.Telemetry);
                FrontKit.Set(title, t.Title, RoomInk.Engrave);
                AvText.Size(sub, 10.5f); // a ghost row may have stepped it down
                FrontKit.Set(sub, (t.Moving ? "MOVING" : "STATIC") + (t.Marked ? " · MARKED" : t.FixedPoint ? " · FIXED POINT" : ""), t.Marked ? FrontKit.Tone(AvState.Ready) : Mc.Steel);
                FrontKit.Set(pct, t.Percent > 0 && t.Percent <= 100 ? t.Percent + " %" : "", RoomInk.Engrave);
                FrontKit.Set(cls, klass == ProbableClass.Hostile ? "HOSTILE" : klass == ProbableClass.Friendly ? "FRIENDLY" : klass == ProbableClass.Neutral ? "NEUTRAL" : "UNKNOWN",
                    klass == ProbableClass.Hostile ? FrontKit.Tone(AvState.Danger) : klass == ProbableClass.Friendly ? FrontKit.Tone(AvState.Ready) : Mc.Steel);
            }
        }
    }
}
