using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LayoutMotion = BoscaliSummer.Features.Support.Domain.Layout.Motion;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// SPACE › STATION — the flight-control front wall. A header band (station callsign, the GET
    /// clock, orbit and pass state), a full-width pass track over the next thirty minutes, the station
    /// blueprint in the centre (the hero) flanked by vertical telemetry strip charts, the module rack
    /// beside it, the launch rail with one next-step control and the loadout rings along the bottom,
    /// and the flight loop as a radio transcript in the footer.
    ///
    /// <para>Every launch, jettison and resupply is an existing host command (<c>Request*</c>); the
    /// room pre-checks only what the host re-checks (placement, one launch at a time, allocation).
    /// <see cref="PlatformPlan"/> is the design intent it shares with the MFD.</para>
    /// </summary>
    internal sealed class StationView : IOpsView
    {
        private const int LoopShown = 5;
        private const float ConfirmSeconds = 3f;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly string[] SectorLabels = { "NW", "N", "NE", "W", "CENTRE", "E", "SW", "S", "SE" };
        private static readonly string[] StageNames = { "PAD", "LIFTOFF", "INSERTION", "DOCKING" };
        private static readonly KeyCode[] MissionKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3 };

        private readonly SupportManager support;
        private readonly PlatformPlan plan;
        private readonly string[] loop;
        private readonly Action openImager;

        private sealed class Strip
        {
            public TMP_Text Value;
            public Image Fill, Ghost, Mid;
            public float X, Top, Height;
            public bool Signed;
        }

        private sealed class RackRow
        {
            public ModuleKind Kind;
            public RoomControl Control;
            public Image Fill, Glyph;
            public TMP_Text Price;
        }

        private sealed class Button
        {
            public RoomControl Control;
            public Image Fill, Guard;
            public Image[] Edge;
            public TMP_Text Text;
            public bool Primary, Danger;
        }

        private sealed class Mission
        {
            public RoomControl Control;
            public ArcGauge Ring;
            public TMP_Text Name;
            public Image[] Edge;
        }

        private readonly StationBlueprint blueprint = new StationBlueprint();
        private readonly Strip[] strips = new Strip[6];
        private readonly RackRow[] rack = new RackRow[PlatformModules.Designs.Length - 1];
        private readonly Mission[] missions = new Mission[PlatformMissions.Count];
        private readonly Image[] stageDots = new Image[4];
        private readonly TMP_Text[] stageText = new TMP_Text[4];
        private readonly TMP_Text[] loopGet = new TMP_Text[LoopShown];
        private readonly TMP_Text[] loopWho = new TMP_Text[LoopShown];
        private readonly TMP_Text[] loopLine = new TMP_Text[LoopShown];
        private readonly Rect[] sections = new Rect[9];
        private readonly Dictionary<RectTransform, Vector2> homes = new Dictionary<RectTransform, Vector2>(8);

        private Rect hero;
        private RectTransform headerGroup, passGroup, leftGroup, rightGroup, rackGroup, railGroup, loadoutGroup, loopGroup, blueprintGroup;
        private CanvasGroup headerFade, passFade, leftFade, rightFade, rackFade, railFade, loadoutFade, loopFade;
        private TMP_Text subtitle, getLabel, getClock, passWord, passClock, passBand;
        private TMP_Text coverage, mobility, moduleDetail, inspector, emptyTitle, emptyCost, emptySteps, rackTitle, rackCollapsed;
        private TMP_Text railStatus, eta, brief;
        private Button primary, cargo, jettison, imagerSwitch, relocate;
        private RectTransform emptyGroup;

        private ModuleKind hover = ModuleKind.None;
        private int relocationSector = 4;
        private readonly Button[] sectors = new Button[9];
        private int jettisonArmedCell = -1;
        private float jettisonArmedUntil;
        private string railNote;
        private bool railBad;
        private bool awaiting;
        private float entrance = 1f;
        private bool dirty = true;

        public StationView(SupportManager support, PlatformPlan plan, string[] loop, Action openImager)
        {
            this.support = support;
            this.plan = plan;
            this.loop = loop;
            this.openImager = openImager;
        }

        public OpsDomain Domain => OpsDomain.Space;
        public float EntranceSeconds => StationStyle.EntranceSeconds;
        public Rect Hero => hero;
        public IReadOnlyList<Rect> Sections => sections;

        // ---- Build -------------------------------------------------------------------------------

        public void Build(RectTransform room, Rect area)
        {
            StationStyle.Resolve();
            OpsSprites.Ensure();
            homes.Clear();
            float w = area.width, h = area.height, g = 24f;
            BuildSurface(room, w, h);

            float headerH = 76f, passTop = 84f, passH = 78f, midTop = 172f, midH = h - midTop - 234f;
            float railTop = midTop + midH + 10f, railH = 104f, loopTop = railTop + railH + 8f, loopH = h - loopTop - 10f;
            float stripW = 188f, rackW = 436f;
            float bpX = g + stripW + 14f;
            float rightX = w - g - stripW;
            float rackX = rightX - 14f - rackW;
            float bpW = rackX - 14f - bpX;

            headerGroup = Group(room, "Header", new Rect(0f, 0f, w, headerH), out headerFade);
            BuildHeader(headerGroup, w, headerH);
            passGroup = Group(room, "PassTrack", new Rect(g, -passTop, w - g * 2f, passH), out passFade);
            BuildPassTrack(passGroup, w - g * 2f, passH);
            leftGroup = Group(room, "StripsLeft", new Rect(g, -midTop, stripW, midH), out leftFade);
            rightGroup = Group(room, "StripsRight", new Rect(rightX, -midTop, stripW, midH), out rightFade);
            string[] names = { "ENERGY", "SUN", "DARK", "FUEL", "RODS", "MASS" };
            for (int i = 0; i < 6; i++)
            {
                RectTransform parent = i < 3 ? leftGroup : rightGroup;
                strips[i] = BuildStrip(parent, (i % 3) * 66f, midH, names[i], i == 1 || i == 2);
            }
            var bpObject = new GameObject("Blueprint", typeof(RectTransform));
            blueprintGroup = (RectTransform)bpObject.transform;
            blueprintGroup.SetParent(room, false);
            AvKit.Place(blueprintGroup, new Rect(0f, 0f, w, h));
            var bpRect = new Rect(bpX, -midTop, bpW, midH);
            BuildBlueprint(blueprintGroup, bpRect);
            rackGroup = Group(room, "Rack", new Rect(rackX, -midTop, rackW, midH), out rackFade);
            BuildRack(rackGroup, rackW, midH);
            railGroup = Group(room, "LaunchRail", new Rect(g, -railTop, rackX - 14f - g, railH), out railFade);
            BuildRail(railGroup, rackX - 14f - g, railH);
            loadoutGroup = Group(room, "Loadout", new Rect(rackX, -railTop, w - g - rackX, railH), out loadoutFade);
            BuildLoadout(loadoutGroup, w - g - rackX, railH);
            loopGroup = Group(room, "FlightLoop", new Rect(g, -loopTop, w - g * 2f, loopH), out loopFade);
            BuildLoop(loopGroup, w - g * 2f, loopH);

            hero = new Rect(bpX, midTop, bpW, midH);
            sections[0] = new Rect(0f, 0f, w, headerH);
            sections[1] = new Rect(g, passTop, w - g * 2f, passH);
            sections[2] = new Rect(g, midTop, stripW, midH);
            sections[3] = hero;
            sections[4] = new Rect(rackX, midTop, rackW, midH);
            sections[5] = new Rect(rightX, midTop, stripW, midH);
            sections[6] = new Rect(g, railTop, rackX - 14f - g, railH);
            sections[7] = new Rect(rackX, railTop, w - g - rackX, railH);
            sections[8] = new Rect(g, loopTop, w - g * 2f, loopH);
            dirty = true;
        }

        private static RectTransform Group(RectTransform parent, string name, Rect at, out CanvasGroup fade)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            AvKit.Place(rect, at);
            fade = go.GetComponent<CanvasGroup>();
            return rect;
        }

        private static void BuildSurface(RectTransform room, float w, float h)
        {
            Image surface = AvKit.Panel(room, new Rect(0f, 0f, w, h), StationStyle.Surface.WithAlpha(1f));
            surface.raycastTarget = true;
            Image stars = AvKit.Panel(room, new Rect(0f, 0f, w, h), StationStyle.Ink.WithAlpha(0.18f));
            stars.sprite = OpsSprites.Stars;
            stars.type = Image.Type.Tiled;
            Image grid = AvKit.Panel(room, new Rect(0f, 0f, w, h), StationStyle.Line.WithAlpha(0.05f));
            grid.sprite = OpsSprites.Blueprint;
            grid.type = Image.Type.Tiled;
            Image sky = AvKit.Panel(room, new Rect(-w * 0.1f, -h + 240f, w * 1.2f, 240f), StationStyle.LimbSky.WithAlpha(0.85f));
            sky.sprite = OpsSprites.Limb;
            sky.type = Image.Type.Simple;
            Image limb = AvKit.Panel(room, new Rect(-w * 0.05f, -h + 190f, w * 1.1f, 190f), StationStyle.Limb.WithAlpha(0.35f));
            limb.sprite = OpsSprites.Limb;
            limb.type = Image.Type.Simple;
        }

        private void BuildHeader(RectTransform parent, float w, float h)
        {
            AvKit.Rule(parent, new Rect(24f, -h + 1f, w - 48f, 1f), StationStyle.Line.WithAlpha(0.5f));
            StationStyle.Text(parent, OrbitalPlatform.Callsign, new Rect(24f, -10f, 420f, 34f), StationStyle.Callsign,
                StationStyle.Ink, 16f, TextAlignmentOptions.MidlineLeft, true);
            subtitle = StationStyle.Text(parent, "", new Rect(26f, -46f, 520f, 18f), StationStyle.Label, StationStyle.Dim,
                StationStyle.LabelTracking);
            getLabel = StationStyle.Text(parent, "STATION CONTROL", new Rect(w * 0.5f - 300f, -4f, 600f, 16f), 10f,
                StationStyle.Dim, 8f, TextAlignmentOptions.Center);
            getClock = StationStyle.Text(parent, "", new Rect(w * 0.5f - 360f, -18f, 720f, 54f), StationStyle.Display,
                StationStyle.Ink, StationStyle.DisplayTracking, TextAlignmentOptions.Center);
            passWord = StationStyle.Text(parent, "", new Rect(w - 24f - 560f, -8f, 380f, 30f), 22f, StationStyle.Ink, 10f,
                TextAlignmentOptions.MidlineRight, true);
            passClock = StationStyle.Text(parent, "", new Rect(w - 24f - 560f, -40f, 380f, 20f), 14f, StationStyle.Ink, 6f,
                TextAlignmentOptions.MidlineRight);
            passBand = StationStyle.Text(parent, "", new Rect(w - 24f - 560f, -58f, 380f, 16f), 10f, StationStyle.Dim, 5f,
                TextAlignmentOptions.MidlineRight);
            imagerSwitch = BuildButton(parent, new Rect(w - 24f - 160f, -16f, 160f, 44f), () => openImager?.Invoke(), false, 11f);
            SetButton(imagerSwitch, "SENSOR FEED  [TAB]", true, "Switch to the imager: the station's sensor feed, slewed and tasked at the crosshair.");
        }

        private void BuildPassTrack(RectTransform parent, float w, float h)
        {
            AvKit.Panel(parent, new Rect(0f, 0f, w, h), StationStyle.Console.WithAlpha(0.85f));
            AvKit.Rule(parent, new Rect(0f, 0f, 3f, h), StationStyle.Limb);
            StationStyle.Text(parent, "POSITION / COVERAGE", new Rect(16f, -8f, w * 0.46f, 16f), 11f, StationStyle.Dim, 3f);
            coverage = StationStyle.Text(parent, "", new Rect(16f, -29f, w * 0.36f, 38f), 16f, StationStyle.Ink, 2f);
            coverage.enableWordWrapping = true;
            mobility = StationStyle.Text(parent, "", new Rect(w * 0.37f, -12f, w * 0.63f - 530f, 54f), 12f, StationStyle.Ink, 1f);
            mobility.enableWordWrapping = true;
            for (int i = 0; i < sectors.Length; i++)
            {
                int sector = i;
                sectors[i] = BuildButton(parent, new Rect(w - 504f + (i % 3) * 88f, -5f - (i / 3) * 23f, 82f, 21f),
                    () => { relocationSector = sector; dirty = true; }, false, 10f);
            }
            relocate = BuildButton(parent, new Rect(w - 224f, -18f, 208f, 42f), OnRelocate, false, 12f);
        }

        private void OnRelocate()
        {
            OrbitalPlatform platform = support != null ? support.LocalPlatform : null;
            if (platform == null || support.CommandPending ||
                platform.CheckRelocate(relocationSector, support.OrbitNow) != PlatformDenial.None) return;
            support.RequestRelocate(relocationSector);
            Sent("RELOCATION ORDER SENT");
        }

        private Strip BuildStrip(RectTransform parent, float x, float h, string label, bool signed)
        {
            var strip = new Strip { X = x, Signed = signed };
            StationStyle.Text(parent, label, new Rect(x, 0f, 56f, 14f), 10f, StationStyle.Dim, 3f,
                TextAlignmentOptions.Center);
            strip.Value = StationStyle.Text(parent, "", new Rect(x - 4f, -16f, 64f, 16f), 11f, StationStyle.Ink, 1f,
                TextAlignmentOptions.Center);
            strip.Top = -40f;
            strip.Height = h - 50f;
            AvKit.Panel(parent, new Rect(x + 18f, strip.Top, 20f, strip.Height), StationStyle.Line.WithAlpha(0.12f));
            AvKit.Outline(parent, new Rect(x + 18f, strip.Top, 20f, strip.Height), StationStyle.Line.WithAlpha(0.45f));
            for (int t = 1; t < 10; t++)
                AvKit.Rule(parent, new Rect(x + 12f, strip.Top - strip.Height * t / 10f, 6f, 1f), StationStyle.Line.WithAlpha(0.5f));
            strip.Fill = AvKit.Panel(parent, new Rect(x + 19f, strip.Top, 18f, 0f), StationStyle.Line);
            strip.Ghost = AvKit.Panel(parent, new Rect(x + 12f, strip.Top, 32f, 3f), StationStyle.Limb);
            strip.Ghost.enabled = false;
            strip.Mid = AvKit.Rule(parent, new Rect(x + 14f, strip.Top - strip.Height * 0.5f, 28f, 1f), StationStyle.Ink.WithAlpha(0.8f));
            strip.Mid.enabled = signed;
            return strip;
        }

        private void BuildBlueprint(RectTransform parent, Rect at)
        {
            inspector = StationStyle.Text(parent, "", new Rect(at.x, at.y, at.width - 200f, 18f), StationStyle.Label, StationStyle.Ink,
                4f);
            jettison = BuildButton(parent, new Rect(at.x + at.width - 190f, at.y + 2f, 190f, 26f), OnJettison, false, 10f);
            jettison.Danger = true;
            jettison.Guard = AvKit.Panel(jettison.Control.Rect, new Rect(0f, 0f, 190f, 26f), AvTheme.RailCaution.WithAlpha(0.25f));
            jettison.Guard.sprite = OpsSprites.Guard;
            jettison.Guard.type = Image.Type.Tiled;
            jettison.Guard.rectTransform.SetSiblingIndex(1);
            blueprint.Build(parent, new Rect(at.x, at.y - 22f, at.width, at.height - 22f), OnCell);

            emptyGroup = (RectTransform)new GameObject("Empty", typeof(RectTransform)).transform;
            emptyGroup.SetParent(parent, false);
            AvKit.Place(emptyGroup, new Rect(at.x, at.y, at.width, at.height));
            StationStyle.Text(emptyGroup, "PRELAUNCH / ORBITAL CONTROL", new Rect(0f, -8f, at.width, 16f),
                10f, StationStyle.Limb, 6f, TextAlignmentOptions.Center);
            emptyTitle = StationStyle.Text(emptyGroup, "", new Rect(0f, -34f, at.width, 34f), 24f, StationStyle.Ink, 10f,
                TextAlignmentOptions.Center, true);
            emptyCost = StationStyle.Text(emptyGroup, "", new Rect(0f, -72f, at.width, 22f), 12f, StationStyle.Limb, 4f,
                TextAlignmentOptions.Center);
            emptySteps = StationStyle.Text(emptyGroup, "", new Rect(40f, -at.height + 118f, at.width - 80f, 112f), 12f,
                StationStyle.Ink, 3f, TextAlignmentOptions.TopLeft);
            emptySteps.enableWordWrapping = true;
            emptySteps.fontStyle = FontStyles.Normal;
        }

        private void BuildRack(RectTransform parent, float w, float h)
        {
            AvKit.Panel(parent, new Rect(0f, 0f, w, h), StationStyle.Console.WithAlpha(0.72f));
            AvKit.Outline(parent, new Rect(0f, 0f, w, h), StationStyle.ConsoleEdge);
            rackTitle = StationStyle.Text(parent, "", new Rect(12f, -8f, w - 24f, 16f), 10f, StationStyle.Dim, 6f);
            float pitch = Mathf.Min(42f, (h - 130f) / rack.Length);
            int r = 0;
            for (int i = 0; i < PlatformModules.Designs.Length && r < rack.Length; i++)
            {
                ModuleInfo info = PlatformModules.Designs[i];
                if (info.Kind == ModuleKind.Core || info.Kind == ModuleKind.None) continue;
                rack[r++] = BuildRackRow(parent, info, new Rect(8f, -32f - (r - 1) * pitch, w - 16f, pitch - 4f));
            }
            AvKit.Rule(parent, new Rect(12f, -h + 86f, w - 24f, 1f), StationStyle.ConsoleEdge);
            moduleDetail = StationStyle.Text(parent, "", new Rect(14f, -h + 78f, w - 28f, 70f), 12f, StationStyle.Ink, 1f, TextAlignmentOptions.TopLeft);
            moduleDetail.enableWordWrapping = true;
            rackCollapsed = StationStyle.Text(parent, "", new Rect(14f, -36f, w - 28f, h - 50f), 12f, StationStyle.Ink, 3f,
                TextAlignmentOptions.TopLeft);
            rackCollapsed.enableWordWrapping = true;
        }

        private RackRow BuildRackRow(RectTransform parent, ModuleInfo info, Rect at)
        {
            var row = new RackRow { Kind = info.Kind };
            ModuleKind kind = info.Kind;
            row.Control = RoomControl.Create(parent, at, () => SelectModule(kind), "RackRow");
            RectTransform host = row.Control.Rect;
            row.Fill = AvKit.Panel(host, new Rect(0f, 0f, at.width, at.height), StationStyle.Console);
            row.Glyph = AvKit.Panel(host, new Rect(6f, -(at.height - 26f) * 0.5f, 26f, 26f), StationStyle.Ink);
            row.Glyph.sprite = OpsSprites.Glyph((int)info.Kind);
            StationStyle.Text(host, info.Code, new Rect(40f, 0f, 60f, at.height), 13f, StationStyle.Ink, 6f,
                TextAlignmentOptions.MidlineLeft, true);
            StationStyle.Text(host, info.Name, new Rect(100f, 1f, 170f, at.height * 0.5f), 10f, StationStyle.Dim, 2f,
                TextAlignmentOptions.BottomLeft);
            StationStyle.Text(host, Stat(info), new Rect(100f, -at.height * 0.5f, 170f, at.height * 0.5f), 10f,
                StationStyle.Ink, 2f, TextAlignmentOptions.TopLeft);
            row.Price = StationStyle.Text(host, "", new Rect(at.width - 130f, 0f, 122f, at.height), 11f, StationStyle.Ink, 3f,
                TextAlignmentOptions.MidlineRight);
            row.Control.WithTooltip(info.Name + " — " + info.Summary + " Hover to see where it would dock; click to queue it.");
            row.Control.Changed = c =>
            {
                if (c.Hovered) hover = kind;
                else if (hover == kind) hover = ModuleKind.None;
                dirty = true;
            };
            return row;
        }

        private void BuildRail(RectTransform parent, float w, float h)
        {
            StationStyle.Text(parent, "LAUNCH RAIL", new Rect(0f, 0f, 200f, 14f), 10f, StationStyle.Dim, 8f);
            float left = 20f, right = 470f;
            AvKit.Rule(parent, new Rect(left, -30f, right - left, 2f), StationStyle.Line.WithAlpha(0.6f));
            for (int i = 0; i < 4; i++)
            {
                float x = left + (right - left) * i / 3f;
                stageDots[i] = AvKit.Panel(parent, new Rect(x - 7f, -24f, 14f, 14f), StationStyle.Line, OpsSprites.Dot);
                stageDots[i].type = Image.Type.Simple;
                stageText[i] = StationStyle.Text(parent, StageNames[i], new Rect(x - 60f, -42f, 120f, 14f), 10f, StationStyle.Dim, 4f,
                    TextAlignmentOptions.Center);
            }
            eta = StationStyle.Text(parent, "", new Rect(0f, -62f, 500f, 16f), 11f, StationStyle.Ink, 3f,
                TextAlignmentOptions.MidlineLeft);
            primary = BuildButton(parent, new Rect(500f, -6f, w - 500f, 46f), OnPrimary, true, 13f);
            cargo = BuildButton(parent, new Rect(500f, -58f, (w - 500f) * 0.5f - 6f, 26f), OnCargo, false, 10f);
            railStatus = StationStyle.Text(parent, "", new Rect(500f + (w - 500f) * 0.5f + 6f, -58f, (w - 500f) * 0.5f - 6f, 26f), 10f,
                StationStyle.Dim, 2f, TextAlignmentOptions.MidlineLeft);
            railStatus.enableWordWrapping = true;
            railStatus.fontSizeMin = 10f;
            AvKit.Rule(parent, new Rect(0f, -h + 1f, w, 1f), StationStyle.Line.WithAlpha(0.3f));
        }

        private void BuildLoadout(RectTransform parent, float w, float h)
        {
            StationStyle.Text(parent, "LOADOUT · 1 2 3", new Rect(0f, 0f, 260f, 14f), 10f, StationStyle.Dim, 8f);
            float cw = (w - 16f) / missions.Length;
            for (int i = 0; i < missions.Length; i++)
            {
                var mission = (PlatformMission)i;
                var card = new Mission();
                card.Control = RoomControl.Create(parent, new Rect(i * (cw + 8f), -18f, cw, 64f), () => SelectMission(mission), "Loadout");
                RectTransform host = card.Control.Rect;
                card.Edge = AvKit.Outline(host, new Rect(0f, 0f, cw, 64f), StationStyle.ConsoleEdge);
                card.Ring = new ArcGauge();
                card.Ring.Build(host, new Rect(6f, -4f, 60f, 74f), StationStyle.Telemetry());
                card.Name = StationStyle.Text(host, PlatformMissions.Name(mission), new Rect(70f, -8f, cw - 76f, 44f), 11f,
                    StationStyle.Ink, 5f, TextAlignmentOptions.TopLeft, true);
                card.Name.enableWordWrapping = true;
                card.Control.WithTooltip(PlatformMissions.Name(mission) + " — " + PlatformMissions.Brief(mission));
                missions[i] = card;
            }
            brief = StationStyle.Text(parent, "", new Rect(0f, -86f, w, 16f), 10f, StationStyle.Dim, 2f);
        }

        private void BuildLoop(RectTransform parent, float w, float h)
        {
            StationStyle.Text(parent, "FLIGHT LOOP", new Rect(0f, 0f, 200f, 14f), 10f, StationStyle.Dim, 8f);
            float pitch = Mathf.Min(18f, (h - 16f) / LoopShown);
            for (int i = 0; i < LoopShown; i++)
            {
                float y = -16f - i * pitch;
                loopGet[i] = StationStyle.Text(parent, "", new Rect(0f, y, 110f, pitch), 11f, StationStyle.Dim, 2f);
                loopWho[i] = StationStyle.Text(parent, "", new Rect(118f, y, 90f, pitch), 11f, StationStyle.Limb, 5f,
                    TextAlignmentOptions.MidlineLeft, true);
                loopLine[i] = StationStyle.Text(parent, "", new Rect(214f, y, w - 214f, pitch), 11f, StationStyle.Ink, 3f);
            }
        }

        private Button BuildButton(RectTransform parent, Rect at, Action click, bool primaryStyle, float size)
        {
            var button = new Button { Primary = primaryStyle };
            button.Control = RoomControl.Create(parent, at, click, "Control");
            RectTransform host = button.Control.Rect;
            button.Fill = AvKit.Panel(host, new Rect(0f, 0f, at.width, at.height), StationStyle.Console);
            button.Edge = AvKit.Outline(host, new Rect(0f, 0f, at.width, at.height), StationStyle.Line);
            button.Text = StationStyle.Text(host, "", new Rect(8f, 0f, at.width - 16f, at.height), size, StationStyle.Ink, 5f,
                TextAlignmentOptions.Center, primaryStyle);
            button.Text.enableAutoSizing = true;
            button.Text.fontSizeMin = 10f;
            button.Text.fontSizeMax = size;
            button.Control.Changed = _ => PaintButton(button);
            PaintButton(button);
            return button;
        }

        private static void PaintButton(Button button)
        {
            RoomControl c = button.Control;
            Color edge = !c.Enabled ? StationStyle.Line.WithAlpha(0.35f) : button.Danger ? AvTheme.RailCaution
                : button.Primary ? StationStyle.Limb : StationStyle.Line;
            for (int i = 0; i < button.Edge.Length; i++) button.Edge[i].color = edge;
            button.Fill.color = !c.Enabled ? StationStyle.Console.WithAlpha(0.6f)
                : c.Pressed ? Color.Lerp(StationStyle.Console, edge, 0.45f)
                : c.Hovered || c.Latched ? Color.Lerp(StationStyle.Console, edge, 0.25f)
                : button.Primary ? Color.Lerp(StationStyle.Console, edge, 0.12f) : StationStyle.Console;
            button.Text.color = c.Enabled ? StationStyle.Ink : StationStyle.Dim;
        }

        private static void SetButton(Button button, string text, bool enabled, string tip)
        {
            if (button.Text.text != text) button.Text.text = text;
            button.Control.SetEnabled(enabled);
            button.Control.WithTooltip(tip);
            PaintButton(button);
        }

        // ---- Lifecycle ---------------------------------------------------------------------------

        public void Show(object context)
        {
            OrbitalPlatform platform = support != null ? support.LocalPlatform : null;
            relocationSector = platform != null && platform.Exists ? platform.PositionIndex : StationKeeping.Centre;
            dirty = true;
        }

        public void Hide()
        {
            hover = ModuleKind.None;
            AvButton.ClearTooltip();
        }

        public void Entrance(float progress)
        {
            entrance = Mathf.Clamp01(progress);
            blueprint.Entrance(entrance);
            Slide(headerGroup, headerFade, Window(entrance, 0f, 0.4f), 0f, 20f);
            Slide(passGroup, passFade, Window(entrance, 0.05f, 0.45f), 0f, 14f);
            Slide(leftGroup, leftFade, Window(entrance, 0.15f, 0.6f), -30f, 0f);
            Slide(rightGroup, rightFade, Window(entrance, 0.15f, 0.6f), 30f, 0f);
            Slide(rackGroup, rackFade, Window(entrance, 0.3f, 0.8f), 30f, 0f);
            Slide(railGroup, railFade, Window(entrance, 0.4f, 0.9f), 0f, -20f);
            Slide(loadoutGroup, loadoutFade, Window(entrance, 0.4f, 0.9f), 0f, -20f);
            Slide(loopGroup, loopFade, Window(entrance, 0.5f, 1f), 0f, -14f);
        }

        private static float Window(float p, float start, float end) => Mathf.Clamp01((p - start) / Mathf.Max(0.0001f, end - start));

        private void Slide(RectTransform rect, CanvasGroup fade, float p, float dx, float dy)
        {
            if (rect == null) return;
            if (!homes.TryGetValue(rect, out Vector2 home))
            {
                home = rect.anchoredPosition;
                homes[rect] = home;
            }
            float eased = LayoutMotion.EaseOutCubic(p);
            rect.anchoredPosition = home + new Vector2(dx, dy) * (1f - eased);
            if (fade != null) fade.alpha = eased;
        }

        public void Refresh(double now, float time, bool textTick)
        {
            if (support == null) return;
            Paint(support.LocalPlatform, support.OrbitNow, textTick);
        }

        /// <summary>Paint from a station. The game calls it every frame; the harness with a fixture.</summary>
        internal void Paint(OrbitalPlatform platform, double now, bool textTick)
        {
            if (awaiting && support != null && !support.CommandPending)
            {
                awaiting = false;
                string reply = support.Status ?? "";
                railNote = "HOST · " + reply.ToUpperInvariant();
                railBad = reply.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          reply.IndexOf("no response", StringComparison.OrdinalIgnoreCase) >= 0;
                textTick = true;
            }
            if (!textTick && !dirty) return;
            dirty = false;
            KeepSelection(platform);
            bool station = platform != null && platform.Exists;
            PlatformStats stats = station ? platform.Stats(now) : default;
            OrbitState state = station ? platform.State(now) : default;
            PlatformFitStep step = PlatformMissions.Next(platform, plan.Mission, now);

            ModuleKind preview = station ? hover != ModuleKind.None ? hover : plan.Module : ModuleKind.None;
            int previewCell = -1;
            string previewText = null;
            if (preview != ModuleKind.None)
            {
                previewCell = preview == plan.Module && OrbitalPlatform.InGrid(plan.Cell) &&
                              platform.CheckPlacement(preview, plan.Cell, 0, now) == PlacementFailure.None
                    ? plan.Cell
                    : PlatformMissions.BestCell(platform, preview, now, out _);
                previewText = OrbitalPlatform.InGrid(previewCell)
                    ? PlatformModules.Info(preview).Code + " → " + OrbitalPlatform.CellName(previewCell)
                    : null;
            }
            blueprint.Paint(platform, now, plan.Cell, preview, previewCell, previewText);

            WriteHeader(platform, station, state, now, stats);
            WritePassTrack(platform, station, state, now);
            WriteStrips(platform, station, stats, preview);
            WriteInspector(platform, station, now);
            WriteRack(platform, station, now);
            WriteRail(platform, station, stats, step, now);
            WriteLoadout(platform, station, now);
            WriteLoop();
        }

        public bool HandleKeys()
        {
            OrbitalPlatform platform = support?.LocalPlatform;
            for (int i = 0; i < MissionKeys.Length; i++)
                if (Input.GetKeyDown(MissionKeys[i])) { SelectMission((PlatformMission)i); return true; }
            if (Input.GetKeyDown(KeyCode.Space)) { OnPrimary(); return true; }
            if (Input.GetKeyDown(KeyCode.R)) { OnCargo(); return true; }
            if (Input.GetKeyDown(KeyCode.J)) { OnJettison(); return true; }
            if (Input.GetKeyDown(KeyCode.Q)) { CycleCell(platform, -1); return true; }
            if (Input.GetKeyDown(KeyCode.E)) { CycleCell(platform, 1); return true; }
            if (Input.GetKeyDown(KeyCode.Tab)) { openImager?.Invoke(); return true; }
            return false;
        }

        /// <summary>Right-click inside is "back": drop the queued module and any armed jettison.</summary>
        public void RightClickInside()
        {
            plan.Module = ModuleKind.None;
            jettisonArmedCell = -1;
            dirty = true;
        }

        // ---- Selection and orders ----------------------------------------------------------------

        private void KeepSelection(OrbitalPlatform platform)
        {
            bool station = platform != null && platform.Exists;
            if (!station)
            {
                plan.Cell = OrbitalPlatform.CoreCell;
                return;
            }
            if (OrbitalPlatform.InGrid(plan.Cell)) return;
            for (int i = 0; i < OrbitalPlatform.CellCount; i++)
            {
                if (!platform.CanAttach(i)) continue;
                plan.Cell = i;
                return;
            }
            plan.Cell = OrbitalPlatform.CoreCell;
        }

        private void CycleCell(OrbitalPlatform platform, int direction)
        {
            if (platform == null || !platform.Exists) return;
            int start = OrbitalPlatform.InGrid(plan.Cell) ? plan.Cell : OrbitalPlatform.CoreCell;
            for (int step = 1; step <= OrbitalPlatform.CellCount; step++)
            {
                int cell = ((start + direction * step) % OrbitalPlatform.CellCount + OrbitalPlatform.CellCount) % OrbitalPlatform.CellCount;
                if (platform.Cell(cell) == ModuleKind.None && !platform.CanAttach(cell)) continue;
                OnCell(cell);
                return;
            }
        }

        private void OnCell(int cell)
        {
            plan.Cell = cell;
            jettisonArmedCell = -1;
            dirty = true;
        }

        private void SelectMission(PlatformMission mission)
        {
            plan.Mission = mission;
            if (support != null) plan.Adopt(support.LocalPlatform, support.OrbitNow);
            dirty = true;
        }

        private void SelectModule(ModuleKind kind)
        {
            plan.Module = plan.Module == kind ? ModuleKind.None : kind;
            OrbitalPlatform platform = support?.LocalPlatform;
            if (plan.Module != ModuleKind.None && platform != null && platform.Exists &&
                platform.CheckPlacement(plan.Module, plan.Cell, 0, support.OrbitNow) != PlacementFailure.None)
            {
                int suggested = PlatformMissions.BestCell(platform, plan.Module, support.OrbitNow, out _);
                if (suggested >= 0) plan.Cell = suggested;
            }
            dirty = true;
        }

        /// <summary>The one next-step control: the queued module, else the loadout's next module, else the core.</summary>
        private void OnPrimary()
        {
            if (support == null) return;
            OrbitalPlatform platform = support.LocalPlatform;
            bool station = platform != null && platform.Exists;
            if (support.CommandPending)
            {
                Note("AWAITING HOST · ONE ORDER AT A TIME", true);
                return;
            }
            if (!station)
            {
                if (!Affordable(ModuleKind.Core)) return;
                support.RequestCoreLaunch(OrbitRegimes.Standard);
                Sent("CORE LAUNCH SENT · " + OrbitalPlatform.Callsign + " ON THE PAD");
                return;
            }
            if (plan.Module == ModuleKind.None || platform.CheckPlacement(plan.Module, plan.Cell, 0, support.OrbitNow) != PlacementFailure.None)
            {
                PlatformFitStep step = plan.Adopt(platform, support.OrbitNow);
                if (step.Complete && plan.Module == ModuleKind.None)
                {
                    Note(PlatformMissions.Name(plan.Mission) + " LOADOUT FITTED · QUEUE A MODULE FROM THE RACK TO GO FURTHER", false);
                    return;
                }
                if (step.Failure != PlacementFailure.None)
                {
                    Note("NO FIT FOR " + PlatformModules.Info(step.Module).Name + " · " + PlatformWords.Placement(step.Failure), true);
                    return;
                }
            }
            PlacementFailure placement = platform.CheckPlacement(plan.Module, plan.Cell, 0, support.OrbitNow);
            if (placement != PlacementFailure.None)
            {
                Note("LAUNCH REFUSED · " + PlatformWords.Placement(placement), true);
                return;
            }
            if (!Affordable(plan.Module)) return;
            support.RequestModuleLaunch(plan.Module, plan.Cell);
            Sent(PlatformModules.Info(plan.Module).Name + " SENT · DOCKS AT " + OrbitalPlatform.CellName(plan.Cell));
        }

        private bool Affordable(ModuleKind kind)
        {
            float cost = support.LaunchCost(kind);
            if (support.BypassRequirements || support.LocalAllocation + 0.001f >= cost) return true;
            Note("INSUFFICIENT ALLOCATION · NEEDS " + Figure(cost - support.LocalAllocation) + " MORE", true);
            return false;
        }

        private void OnCargo()
        {
            if (support == null) return;
            string refusal = CargoRefusal(support.LocalPlatform, support.OrbitNow);
            if (refusal != null)
            {
                Note(refusal, true);
                return;
            }
            if (!Affordable(ModuleKind.Cargo)) return;
            support.RequestResupply();
            Sent("CARGO SENT · REFILLS FUEL AND RODS ON DOCKING");
        }

        /// <summary>Why cargo cannot go right now, in words; null when it can (P5).</summary>
        private string CargoRefusal(OrbitalPlatform platform, double now)
        {
            if (platform == null || !platform.Exists) return "NO STATION TO RESUPPLY YET";
            if (support != null && support.CommandPending) return "AWAITING HOST";
            if (platform.Pending != ModuleKind.None) return "ONE LAUNCH AT A TIME · WAIT FOR DOCKING";
            PlatformStats stats = platform.Stats(now);
            if (stats.FuelCapacity <= 0f && stats.RodCapacity <= 0) return "NO TANKS OR MAGAZINES TO FILL";
            if (platform.Fuel >= stats.FuelCapacity - 0.5f && platform.Rods >= stats.RodCapacity) return "TANKS AND MAGAZINES ARE FULL";
            return null;
        }

        private void OnJettison()
        {
            if (support == null) return;
            OrbitalPlatform platform = support.LocalPlatform;
            if (platform == null || !platform.Exists || !OrbitalPlatform.InGrid(plan.Cell) || platform.Cell(plan.Cell) == ModuleKind.None)
            {
                Note("SELECT A DOCKED MODULE FIRST", true);
                return;
            }
            if (support.CommandPending)
            {
                Note("AWAITING HOST · ONE ORDER AT A TIME", true);
                return;
            }
            ModuleKind kind = platform.Cell(plan.Cell);
            if (kind != ModuleKind.Core && platform.WouldStrand(plan.Cell))
            {
                Note("WOULD STRAND MODULES · JETTISON THOSE FIRST", true);
                return;
            }
            // A guarded switch: the first press lifts the cover, the second fires.
            if (jettisonArmedCell != plan.Cell || Time.unscaledTime > jettisonArmedUntil)
            {
                jettisonArmedCell = plan.Cell;
                jettisonArmedUntil = Time.unscaledTime + ConfirmSeconds;
                Note(kind == ModuleKind.Core ? "COVER UP · PRESS AGAIN TO DEORBIT THE STATION" : "COVER UP · PRESS AGAIN TO JETTISON", false);
                return;
            }
            jettisonArmedCell = -1;
            support.RequestJettison(plan.Cell);
            Sent(kind == ModuleKind.Core ? "DEORBIT SENT · " + OrbitalPlatform.Callsign + " COMING DOWN"
                : "JETTISON SENT · " + PlatformModules.Info(kind).Code + " " + OrbitalPlatform.CellName(plan.Cell));
        }

        private void Note(string line, bool bad)
        {
            railNote = line;
            railBad = bad;
            awaiting = false;
            dirty = true;
        }

        private void Sent(string line)
        {
            railNote = line + " · AWAITING HOST";
            railBad = false;
            awaiting = true;
            dirty = true;
        }

        // ---- Text --------------------------------------------------------------------------------

        private void WriteHeader(OrbitalPlatform platform, bool station, in OrbitState state, double now,
            in PlatformStats stats)
        {
            Set(subtitle, station ? "ORBITAL PLATFORM · " + stats.Modules + "/" + OrbitalPlatform.CellCount + " CELLS · " +
                                    stats.Online + " ONLINE" + (platform.Brownout ? " · BROWNOUT" : "")
                : "ORBITAL PLATFORM · NOT LAUNCHED");
            subtitle.color = station && platform.Brownout ? AvTheme.RailDanger : StationStyle.Dim;
            // No elapsed time before there is a flight to time (P7).
            Set(getClock, station ? "BUILD / SUSTAIN / TASK" : "ESTABLISH YOUR PLATFORM");
            getLabel.gameObject.SetActive(station);
            string word, clockText;
            Color tone;
            PlatformHold hold = station ? platform.HoldAt(now) : PlatformHold.None;
            if (!station) { word = "NO STATION"; clockText = "LAUNCH THE CORE TO START THE CLOCK"; tone = StationStyle.Dim; }
            else if (hold != PlatformHold.None)
            {
                word = PlatformWords.Hold(hold);
                clockText = "ON STATION " + PlatformWords.Clock(platform.CycleStart - now);
                tone = AvTheme.RailInfo;
            }
            else { word = "ON STATION"; clockText = "FIXED POSITION · CONTINUOUS ACCESS"; tone = AvTheme.RailReady; }
            Set(passWord, word);
            passWord.color = tone;
            Set(passClock, clockText);
            passClock.color = tone;
            OrbitRegime orbit = station ? platform.Orbit : OrbitRegimes.Get(0);
            Set(passBand, orbit.Name + " · " + TheaterGrid.Km(orbit.Altitude) + " KM · " +
                          "GET " + TheaterGrid.Elapsed(platform != null ? platform.Elapsed(now) : 0.0));
        }

        private void WritePassTrack(OrbitalPlatform platform, bool station, in OrbitState state, double now)
        {
            PlatformHold hold = station ? platform.HoldAt(now) : PlatformHold.None;
            Set(coverage, !station ? "Launch the core to establish persistent coverage."
                : hold != PlatformHold.None ? PlatformWords.Hold(hold) + " · " + PlatformWords.Clock(platform.CycleStart - now)
                : StationKeeping.Name(platform.PositionIndex) + " · " + TheaterGrid.Kilometres(state.SubX, state.SubZ));
            bool propulsion = station && platform.FittedOnline(ModuleKind.Propulsion, now);
            PlatformDenial denial = station ? platform.CheckRelocate(relocationSector, now) : PlatformDenial.NoPlatform;
            bool pending = support != null && support.CommandPending;
            bool enabled = station && denial == PlatformDenial.None && support != null && !pending;
            string relocationHint = !station ? "LAUNCH THE CORE TO ESTABLISH COVERAGE"
                : !propulsion ? "FIT PRP PROPULSION TO RELOCATE"
                : pending ? "AWAITING HOST REPLY"
                : relocationSector == platform.PositionIndex ? "SELECT A DIFFERENT SECTOR TO MOVE"
                : PlatformWords.Denial(denial, platform, PlatformAbility.Rephase, now);
            Set(mobility, "DESTINATION · " + StationKeeping.Name(relocationSector) + "\n" + relocationHint);
            for (int i = 0; i < sectors.Length; i++)
            {
                SetButton(sectors[i], (i == relocationSector ? "> " : "") + SectorLabels[i] +
                    (station && platform.PositionIndex == i ? " *" : ""), station,
                    StationKeeping.Name(i) + ". * marks the current station sector. Select, then relocate.");
                sectors[i].Control.SetLatched(i == relocationSector);
                if (i == relocationSector) sectors[i].Fill.color = StationStyle.Limb.WithAlpha(0.25f);
            }
            SetButton(relocate, "RELOCATE · " + PlatformWords.Whole(PlatformAbilities.Info(PlatformAbility.Rephase).Fuel) + " FUEL", enabled,
                enabled ? "Move to " + StationKeeping.Name(relocationSector) + "; abilities pause during the burn." : relocationHint);
        }

        private void WriteStrips(OrbitalPlatform platform, bool station, in PlatformStats stats, ModuleKind preview)
        {
            ModuleInfo add = preview != ModuleKind.None ? PlatformModules.Info(preview) : default;
            bool delta = preview != ModuleKind.None;
            if (!station)
            {
                for (int i = 0; i < strips.Length; i++) SetStrip(strips[i], 0f, float.NaN, "—", StationStyle.Dim);
                return;
            }
            float storage = Mathf.Max(1f, stats.StorageKj);
            SetStrip(strips[0], platform.Energy / storage, delta ? platform.Energy / Mathf.Max(1f, stats.StorageKj + add.StorageKj) : float.NaN,
                PlatformWords.Whole(platform.Energy) + " KJ", platform.Brownout ? AvTheme.RailDanger : StationStyle.Line);
            const float PowerScale = 30f;
            float sun = stats.NetSunKw, dark = stats.NetEclipseKw;
            SetStrip(strips[1], sun / PowerScale, delta ? (sun + add.SolarKw + add.SteadyKw - add.LoadKw) / PowerScale : float.NaN,
                PlatformWords.Kilowatts(sun), sun < 0f ? AvTheme.RailCaution : StationStyle.Line);
            SetStrip(strips[2], dark / PowerScale, delta ? (dark + add.SteadyKw - add.LoadKw) / PowerScale : float.NaN,
                PlatformWords.Kilowatts(dark), dark < 0f ? AvTheme.RailCaution : StationStyle.Line);
            SetStrip(strips[3], stats.FuelCapacity > 0f ? platform.Fuel / stats.FuelCapacity : 0f, float.NaN,
                stats.FuelCapacity > 0f ? PlatformWords.Whole(platform.Fuel) : "NO TANK",
                stats.FuelCapacity > 0f && platform.Fuel <= 0.5f ? AvTheme.RailDanger : StationStyle.Line);
            SetStrip(strips[4], stats.RodCapacity > 0 ? platform.Rods / (float)stats.RodCapacity : 0f, float.NaN,
                stats.RodCapacity > 0 ? platform.Rods + "/" + stats.RodCapacity : "NO MAG", StationStyle.Line);
            SetStrip(strips[5], stats.Mass / OrbitalPlatform.MassLimit, delta ? (stats.Mass + add.Mass) / OrbitalPlatform.MassLimit : float.NaN,
                PlatformWords.Tonnes(stats.Mass), stats.Mass > OrbitalPlatform.MassLimit * 0.9f ? AvTheme.RailCaution : StationStyle.Line);
        }

        private static void SetStrip(Strip strip, float fraction, float ghost, string value, Color tone)
        {
            if (strip.Value.text != value) strip.Value.text = value;
            float x = strip.X + 19f;
            if (strip.Signed)
            {
                float f = Mathf.Clamp(float.IsNaN(fraction) ? 0f : fraction, -1f, 1f) * 0.5f;
                float mid = strip.Top - strip.Height * 0.5f;
                float h = Mathf.Abs(f) * strip.Height;
                AvKit.Place(strip.Fill.rectTransform, new Rect(x, f >= 0f ? mid + h : mid, 18f, h));
            }
            else
            {
                float f = Mathf.Clamp01(float.IsNaN(fraction) ? 0f : fraction);
                float h = f * strip.Height;
                AvKit.Place(strip.Fill.rectTransform, new Rect(x, strip.Top - strip.Height + h, 18f, h));
            }
            strip.Fill.color = tone;
            strip.Value.color = tone == StationStyle.Line ? StationStyle.Ink : tone;
            strip.Ghost.enabled = !float.IsNaN(ghost);
            if (!strip.Ghost.enabled) return;
            float level = strip.Signed
                ? strip.Top - strip.Height * 0.5f + Mathf.Clamp(ghost, -1f, 1f) * 0.5f * strip.Height
                : strip.Top - strip.Height + Mathf.Clamp01(ghost) * strip.Height;
            AvKit.Place(strip.Ghost.rectTransform, new Rect(strip.X + 12f, level + 1.5f, 32f, 3f));
        }

        private void WriteInspector(OrbitalPlatform platform, bool station, double now)
        {
            if (emptyGroup.gameObject.activeSelf == station) emptyGroup.gameObject.SetActive(!station);
            if (!station)
            {
                float core = support != null ? support.LaunchCost(ModuleKind.Core) : PlatformModules.LaunchPrice(ModuleKind.Core);
                float available = support != null ? support.LocalAllocation : 0f;
                bool pending = support != null && support.CommandPending;
                bool affordable = support == null || support.BypassRequirements || available + 0.001f >= core;
                Set(emptyTitle, pending ? "CORE INSERTION REQUESTED" : affordable ? "CORE READY FOR INSERTION" : "CORE LAUNCH ON HOLD");
                Set(emptyCost, pending ? "AWAITING HOST REPLY" : !affordable
                    ? "COST " + Figure(core) + "  ·  AVAILABLE " + Figure(available) + "  ·  NEED " + Figure(core - available) + " MORE"
                    : core <= 0f ? "NO ALLOCATION REQUIRED  ·  [SPACE] LAUNCH CORE"
                    : "COST " + Figure(core) + "  ·  AVAILABLE " + Figure(available) + "  ·  [SPACE] LAUNCH CORE");
                emptyCost.color = pending || !affordable ? AvTheme.RailCaution : StationStyle.Limb;
                Set(emptySteps, "1   LAUNCH THE CORE. IT INSERTS INTO " + OrbitRegimes.Get(0).Name + " AND BECOMES " + OrbitalPlatform.Callsign + ".\n" +
                                "2   DOCK MODULES. PICK A LOADOUT BELOW; THE NEXT-STEP CONTROL LAUNCHES ITS NEXT MODULE.\n" +
                                "3   WORK THE PASS. ABILITIES FIRE WHILE THE STATION IS OVERHEAD, FROM OPS › SPACE › ACTIONS.");
                Set(inspector, "");
                jettison.Control.gameObject.SetActive(false);
                return;
            }
            int cell = plan.Cell;
            ModuleKind kind = OrbitalPlatform.InGrid(cell) ? platform.Cell(cell) : ModuleKind.None;
            bool docked = kind != ModuleKind.None;
            jettison.Control.gameObject.SetActive(docked);
            if (docked)
            {
                ModuleInfo info = PlatformModules.Info(kind);
                Set(inspector, OrbitalPlatform.CellName(cell) + " · " + info.Name + " · " + Stat(info) +
                               (platform.IsOnline(cell, now) ? "" : " · OFFLINE"));
                bool confirming = jettisonArmedCell == cell && Time.unscaledTime <= jettisonArmedUntil;
                bool strands = kind != ModuleKind.Core && platform.WouldStrand(cell);
                jettison.Guard.enabled = !confirming;
                SetButton(jettison, confirming ? "CONFIRM  [J]" : kind == ModuleKind.Core ? "DEORBIT  [J]" : "JETTISON  [J]",
                    !strands && (support == null || !support.CommandPending),
                    strands ? "Other modules dock through this one; jettison them first."
                        : "A guarded switch: press once to lift the cover, again within 3 s to fire. Refunds " +
                          Mathf.RoundToInt((support != null ? support.JettisonRefund : 0f) * 100f) + "% of what was paid.");
            }
            else if (OrbitalPlatform.InGrid(cell) && platform.CanAttach(cell))
                Set(inspector, OrbitalPlatform.CellName(cell) + " · FREE PORT · " + NeighbourHint(platform, cell));
            else Set(inspector, "");
        }

        private readonly int[] around = new int[4];

        private string NeighbourHint(OrbitalPlatform platform, int cell)
        {
            OrbitalPlatform.Neighbours(cell, around);
            bool radiator = false, relay = false, shield = false, hot = false, sensor = false;
            for (int i = 0; i < 4; i++)
            {
                ModuleKind kind = platform.Cell(around[i]);
                radiator |= kind == ModuleKind.Radiator;
                relay |= kind == ModuleKind.Relay;
                shield |= kind == ModuleKind.Shield;
                hot |= kind != ModuleKind.None && PlatformModules.Info(kind).Hot;
                sensor |= kind == ModuleKind.Imager || kind == ModuleKind.Sigint;
            }
            string hint = "";
            if (radiator) hint += "COOLED HERE · ";
            if (relay) hint += "RELAY BOOST HERE · ";
            if (shield) hint += "SHIELDED HERE · ";
            if (hot) hint += "A RAD HERE COOLS A HOT NEIGHBOUR · ";
            if (sensor) hint += "A REL HERE BOOSTS A SENSOR · ";
            return hint.Length > 3 ? hint.Substring(0, hint.Length - 3) : "NO NEIGHBOUR UTILITIES";
        }

        private void WriteRack(OrbitalPlatform platform, bool station, double now)
        {
            Set(rackTitle, station ? "MODULES · SELECT MODULE, THEN PORT" : "MODULES");
            ModuleKind detail = hover != ModuleKind.None ? hover : plan.Module;
            Set(moduleDetail, !station ? "" : detail != ModuleKind.None
                ? PlatformModules.Info(detail).Name + " · " + Figure(support != null ? support.LaunchCost(detail) : PlatformModules.LaunchPrice(detail)) + " ALLOCATION\n" + PlatformModules.Info(detail).Summary
                : "PLAN YOUR NEXT MODULE\nSelect a module to preview its benefit and resource impact before launch.");
            // Before launch the rack is one line, not thirteen rows of NEEDS CORE (P3).
            for (int i = 0; i < rack.Length; i++)
                if (rack[i] != null && rack[i].Control.gameObject.activeSelf != station) rack[i].Control.gameObject.SetActive(station);
            Set(rackCollapsed, station ? "" :
                (rack.Length) + " MODULE TYPES DOCK TO THE CORE ONCE IT IS UP.\n\n" +
                "LOADOUTS\n" +
                PlatformMissions.Name(PlatformMission.Recon) + " · " + PlatformMissions.Brief(PlatformMission.Recon) + "\n\n" +
                PlatformMissions.Name(PlatformMission.PrecisionStrike) + " · " + PlatformMissions.Brief(PlatformMission.PrecisionStrike) + "\n\n" +
                PlatformMissions.Name(PlatformMission.Emp) + " · " + PlatformMissions.Brief(PlatformMission.Emp));
            if (!station) return;
            float allocation = support != null ? support.LocalAllocation : 0f;
            bool bypass = support != null && support.BypassRequirements;
            for (int i = 0; i < rack.Length; i++)
            {
                RackRow row = rack[i];
                if (row == null) continue;
                string price;
                Color tone;
                switch (platform.CheckPlacement(row.Kind, plan.Cell, 0, now))
                {
                    case PlacementFailure.None:
                    {
                        float cost = support != null ? support.LaunchCost(row.Kind) : PlatformModules.LaunchPrice(row.Kind);
                        price = Figure(cost);
                        tone = bypass || allocation + 0.001f >= cost ? StationStyle.Ink : AvTheme.RailDanger;
                        break;
                    }
                    case PlacementFailure.OverMass: price = "OVER MASS"; tone = AvTheme.RailDanger; break;
                    case PlacementFailure.CopyLimit: price = "LIMIT " + PlatformModules.Info(row.Kind).MaxCopies; tone = StationStyle.Dim; break;
                    case PlacementFailure.LaunchInFlight: price = "IN FLIGHT"; tone = AvTheme.RailInfo; break;
                    default:
                    {
                        int best = PlatformMissions.BestCell(platform, row.Kind, now, out _);
                        price = best >= 0 ? "→ " + OrbitalPlatform.CellName(best) : "NO PORT";
                        tone = StationStyle.Dim;
                        break;
                    }
                }
                Set(row.Price, price);
                row.Price.color = tone;
                bool selected = row.Kind == plan.Module;
                row.Fill.color = selected ? Color.Lerp(StationStyle.Console, StationStyle.Limb, 0.28f)
                    : row.Control.Hovered ? Color.Lerp(StationStyle.Console, StationStyle.Line, 0.16f) : StationStyle.Console;
            }
        }

        private void WriteRail(OrbitalPlatform platform, bool station, in PlatformStats stats, in PlatformFitStep step, double now)
        {
            int current;
            string etaText;
            PlatformHold hold = station ? platform.HoldAt(now) : PlatformHold.None;
            if (!station) { current = 0; etaText = "ON THE PAD · READY TO LAUNCH THE CORE"; }
            else if (hold == PlatformHold.Insertion) { current = 2; etaText = "INSERTION · ON STATION IN " + PlatformWords.Clock(platform.CycleStart - now); }
            else if (platform.Pending != ModuleKind.None)
            {
                current = 3;
                etaText = PlatformModules.Info(platform.Pending).Code + " DOCKING " +
                          (platform.Pending == ModuleKind.Cargo ? "AT THE CORE" : "AT " + OrbitalPlatform.CellName(platform.PendingCell)) +
                          " · HARD DOCK IN " + PlatformWords.Clock(platform.DockAt - now);
            }
            else { current = 0; etaText = "PAD CLEAR · ONE LAUNCH AT A TIME"; }
            for (int i = 0; i < 4; i++)
            {
                bool done = station && i < current;
                bool live = i == current;
                stageDots[i].color = live ? StationStyle.Limb : done ? StationStyle.Line : StationStyle.Line.WithAlpha(0.3f);
                stageText[i].color = live ? StationStyle.Ink : StationStyle.Dim;
            }
            Set(eta, etaText);

            // The one next-step control (P4): exactly what SPACE will send.
            string label, tip;
            bool enabled;
            bool pending = support != null && support.CommandPending;
            if (!station)
            {
                float cost = support != null ? support.LaunchCost(ModuleKind.Core) : PlatformModules.LaunchPrice(ModuleKind.Core);
                bool afford = support == null || support.BypassRequirements || support.LocalAllocation + 0.001f >= cost;
                label = "[SPACE]  LAUNCH CORE · " + Figure(cost);
                enabled = !pending && afford;
                tip = afford ? "Launch " + OrbitalPlatform.Callsign + "'s core; the host charges on acceptance." : "Needs " + Figure(cost) + " allocation.";
            }
            else if (platform.Pending != ModuleKind.None)
            {
                label = "DOCKING · ONE LAUNCH AT A TIME";
                enabled = false;
                tip = "The next launch waits for hard dock.";
            }
            else
            {
                ModuleKind next = plan.Module != ModuleKind.None ? plan.Module : step.Complete ? ModuleKind.None : step.Module;
                int cell = plan.Module != ModuleKind.None ? plan.Cell : step.Cell;
                if (next == ModuleKind.None)
                {
                    label = PlatformMissions.Name(plan.Mission) + " FITTED · QUEUE A MODULE";
                    enabled = false;
                    tip = "Everything the loadout needs is docked. Queue a module from the rack to go further.";
                }
                else
                {
                    PlacementFailure placement = OrbitalPlatform.InGrid(cell) ? platform.CheckPlacement(next, cell, 0, now) : PlacementFailure.OutsideGrid;
                    float cost = support != null ? support.LaunchCost(next) : PlatformModules.LaunchPrice(next);
                    bool afford = support == null || support.BypassRequirements || support.LocalAllocation + 0.001f >= cost;
                    label = "[SPACE]  LAUNCH " + PlatformModules.Info(next).Name +
                            (OrbitalPlatform.InGrid(cell) ? " → " + OrbitalPlatform.CellName(cell) : "") + " · " + Figure(cost);
                    enabled = !pending && afford && placement == PlacementFailure.None;
                    tip = placement != PlacementFailure.None ? PlatformWords.Placement(placement)
                        : afford ? "Send this launch; the host validates and charges on acceptance."
                        : "Needs " + Figure(cost - (support != null ? support.LocalAllocation : 0f)) + " more allocation.";
                }
            }
            SetButton(primary, label, enabled, tip);

            string cargoWhy = CargoRefusal(platform, now);
            float cargoCost = support != null ? support.LaunchCost(ModuleKind.Cargo) : PlatformModules.LaunchPrice(ModuleKind.Cargo);
            SetButton(cargo, "[R]  CARGO RESUPPLY · " + Figure(cargoCost), cargoWhy == null,
                cargoWhy == null ? "Uncrewed freighter: refills every fuel tank and rod magazine when it docks." : cargoWhy);
            // The line beside cargo is the host's word on this room's last order, else why cargo waits.
            string line = railNote ?? (cargoWhy != null ? "CARGO · " + cargoWhy : "");
            Set(railStatus, line);
            railStatus.color = railNote != null ? (railBad ? AvTheme.RailDanger : StationStyle.Ink) : StationStyle.Dim;
        }

        private void WriteLoadout(OrbitalPlatform platform, bool station, double now)
        {
            for (int i = 0; i < missions.Length; i++)
            {
                var mission = (PlatformMission)i;
                PlatformFitStep step = PlatformMissions.Next(platform, mission, now);
                Mission card = missions[i];
                bool selected = mission == plan.Mission;
                card.Ring.Set(step.Total > 0 ? step.Fitted / (float)step.Total : 0f, step.Fitted + "/" + step.Total, "");
                for (int e = 0; e < card.Edge.Length; e++) card.Edge[e].color = selected ? StationStyle.Limb : StationStyle.ConsoleEdge;
                Set(card.Name, PlatformMissions.Name(mission) + "\n" + (step.Complete ? "FITTED" : station ? "FITTING" : "NOT STARTED"));
                card.Name.color = selected ? StationStyle.Ink : StationStyle.Dim;
            }
            PlatformFitStep current = PlatformMissions.Next(platform, plan.Mission, now);
            Set(brief, PlatformMissions.Name(plan.Mission) + " · " + (current.Complete ? "LOADOUT READY · OPEN SENSOR FEED TO TASK"
                : !station ? "STEP 1 · LAUNCH THE CORE"
                : "NEXT " + PlatformModules.Info(current.Module).Name +
                  (current.Failure == PlacementFailure.None && OrbitalPlatform.InGrid(current.Cell) ? " → " + OrbitalPlatform.CellName(current.Cell)
                      : " · " + PlatformWords.Placement(current.Failure))));
        }

        /// <summary>The voice loop as a radio transcript: GET, the speaker's call sign, the line.</summary>
        private void WriteLoop()
        {
            for (int i = 0; i < LoopShown; i++)
            {
                string raw = loop != null && i < loop.Length ? loop[i] : null;
                if (string.IsNullOrEmpty(raw))
                {
                    Set(loopGet[i], "");
                    Set(loopWho[i], i == 0 ? "FLIGHT" : "");
                    Set(loopLine[i], i == 0 ? "QUIET ON THE LOOP" : "");
                    continue;
                }
                int split = raw.IndexOf("  ", StringComparison.Ordinal);
                string get = split > 0 ? raw.Substring(0, split) : "";
                string text = split > 0 ? raw.Substring(split + 2) : raw;
                // A line logged before launch has no flight to time (P7).
                Set(loopGet[i], get.StartsWith("-", StringComparison.Ordinal) ? "" : "GET " + get);
                Set(loopWho[i], Speaker(text));
                Set(loopLine[i], text);
                loopLine[i].color = i == 0 ? StationStyle.Ink : StationStyle.Dim;
            }
        }

        private static string Speaker(string line)
        {
            if (Has(line, "BROWNOUT") || Has(line, "POWER") || Has(line, "MMOD") || Has(line, "OFFLINE") || Has(line, "SHIELD"))
                return "EECOM";
            if (Has(line, "AOS") || Has(line, "LOS") || Has(line, "BURN") || Has(line, "INSERTION") || Has(line, "SAFE MODE") ||
                Has(line, "ORBIT") || Has(line, "TRANSFER") || Has(line, "PHASING"))
                return "FIDO";
            if (Has(line, "LIFTOFF") || Has(line, "DOCK") || Has(line, "CARGO") || Has(line, "UPLINK") || Has(line, "RADAR"))
                return "CAPCOM";
            return "FLIGHT";
        }

        private static bool Has(string line, string word) => line.IndexOf(word, StringComparison.Ordinal) >= 0;

        private static string Stat(in ModuleInfo info)
        {
            if (info.SolarKw > 0f) return "+" + info.SolarKw.ToString("0", Invariant) + " KW SUN";
            if (info.SteadyKw > 0f) return "+" + info.SteadyKw.ToString("0", Invariant) + " KW";
            if (info.StorageKj > 0f) return "+" + PlatformWords.Whole(info.StorageKj) + " KJ";
            if (info.Fuel > 0f) return PlatformWords.Whole(info.Fuel) + " FUEL";
            if (info.Rods > 0) return info.Rods + " RODS";
            if (info.LoadKw > 0f) return PlatformWords.Kilowatts(-info.LoadKw) + " LOAD";
            return PlatformWords.Tonnes(info.Mass);
        }

        private static void Set(TMP_Text label, string text)
        {
            if (text == null) text = "";
            if (label.text != text) label.text = text;
        }

        private static string Figure(float value) => Mathf.Round(value).ToString("N0", Invariant);

        /// <summary>Offline fixtures: select a cell and hover a rack module.</summary>
        internal void Preview(int cell, ModuleKind hovered)
        {
            if (cell >= 0) plan.Cell = cell;
            hover = hovered;
            dirty = true;
        }
    }
}
