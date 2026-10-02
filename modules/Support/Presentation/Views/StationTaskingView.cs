using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Presentation.Board;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Presentation.Window;
using BoscaliSummer.Modules.Support.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>The station's default tactical room. All orders use the existing host command and map gesture paths.</summary>
    internal sealed class StationTaskingView : IOpsView
    {
        private static readonly string[] SectorCodes = { "NW", "N", "NE", "W", "C", "E", "SW", "S", "SE" };
        private static readonly string[] FocusNames = { "SURVEY", "STRIKE", "SCREEN" };
        private static readonly string[] BranchNames = { "RECONNAISSANCE", "KINETIC SUPPORT", "ELECTROMAGNETIC SUPPORT" };
        private static readonly string[] FocusEffects =
        {
            "WIDE SCANS · 1.2× REACH · 0.75× RECHARGE",
            "FIRE CONTROL · TIGHTEN ROD ACCURACY",
            "CAPACITOR CONTROL · SHAPE THE EMP"
        };
        private static readonly SupportActionId[] ActionIds =
        {
            SupportActionId.Recon, SupportActionId.MtiSweep, SupportActionId.ElintSweep,
            SupportActionId.Artillery, SupportActionId.Emp
        };
        private static readonly string[] ActionNames =
        {
            "RADAR SCAN", "MTI SWEEP", "ELINT SWEEP", "ROD STRIKE", "EMP SCREEN"
        };

        private sealed class TaskButton
        {
            public RoomControl Control;
            public Image Fill, Rail;
            public CommandStepGraphic Step;
            public TMP_Text Title, Detail;
            public bool Active, Caution, Primary;
        }

        private readonly SupportManager support;
        private readonly Action openEngineering;
        private readonly Action<GlobalPosition?> openImager;
        private readonly Action exitToMap;
        private readonly TaskButton[] sectors = new TaskButton[StationKeeping.Count];
        private readonly TaskButton[] focuses = new TaskButton[FocusNames.Length];
        private readonly TaskButton[] actions = new TaskButton[ActionIds.Length];
        private readonly SupportActionDefinition[] definitions = new SupportActionDefinition[ActionIds.Length];
        private readonly Rect[] sections = new Rect[5];
        private readonly BulletBar[] reserves = new BulletBar[4];
        private readonly BulletBar[] workGauges = new BulletBar[4];
        private readonly TaskButton[] workOrders = new TaskButton[4];
        private TMP_Text workReadback, packageOutput;
        private TMP_Text hardware;
        private readonly float[] sectorX = new float[StationKeeping.Count];
        private readonly float[] sectorZ = new float[StationKeeping.Count];
        private readonly float[] fitX = new float[StationKeeping.Count + 5];
        private readonly float[] fitZ = new float[StationKeeping.Count + 5];
        private Image equipment;
        private AlignmentScopeGraphic alignmentScope;
        private TMP_Text equipmentReadout, orbitReadout;
        private BoardSurface board;
        private BoardTerrain terrain;
        private RectTransform mapLayer;
        private RingLine coverageRing, solutionRing;
        private Image stationDot;
        private TMP_Text subtitle, mapStatus, position, focusStatus, solutionTitle, solutionDetail;
        private TMP_Text moveDetail, hostStatus, mapFooter;
        private TaskButton relocate, engineering, sensor, fitMap;
        private CanvasGroup contentFade;
        private Rect hero;
        private int destination = StationKeeping.Centre;
        private int shownFrame = -1;
        private bool dirty = true;

        public StationTaskingView(SupportManager support, Action openEngineering,
            Action<GlobalPosition?> openImager, Action exitToMap)
        {
            this.support = support;
            this.openEngineering = openEngineering;
            this.openImager = openImager;
            this.exitToMap = exitToMap;
            for (int i = 0; i < StationKeeping.Count; i++)
            {
                sectorX[i] = StationKeeping.X(i);
                sectorZ[i] = StationKeeping.Z(i);
                fitX[i] = sectorX[i];
                fitZ[i] = sectorZ[i];
            }
        }

        public OpsDomain Domain => OpsDomain.Space;
        public float EntranceSeconds => 0.2f;
        public Rect Hero => hero;
        public IReadOnlyList<Rect> Sections => sections;

        public void Build(RectTransform room, Rect area)
        {
            StationStyle.Resolve();
            OpsSprites.Ensure();
            float w = area.width, h = area.height;
            const float inset = 18f, gap = 12f;
            float equipmentW = Mathf.Clamp(w * 0.38f, 580f, 730f);
            float railW = Mathf.Clamp(w * 0.21f, 330f, 410f);
            float railX = w - inset - railW;
            float actionTop = h - 174f, footerTop = h - 68f;
            float workTop = actionTop - 86f;
            float payloadTop = workTop - 148f;
            float mapX = inset + equipmentW + gap;
            hero = new Rect(mapX, 18f, railX - mapX - gap, payloadTop - 30f);
            sections[0] = new Rect(0f, 0f, mapX - gap, 86f);
            sections[1] = new Rect(inset, 96f, equipmentW, workTop - 96f);
            sections[2] = new Rect(mapX, 18f, w - inset - mapX, workTop - 18f);
            sections[3] = new Rect(inset, workTop, w - inset * 2f, 182f);
            sections[4] = new Rect(0f, footerTop, w, h - footerTop);
            Chrome.Panel(room, new Rect(0f, 0f, w, h), StationStyle.Surface);
            contentFade = room.gameObject.GetComponent<CanvasGroup>();
            if (contentFade == null) contentFade = room.gameObject.AddComponent<CanvasGroup>();
            BuildHeader(room, equipmentW + inset * 2f);
            BuildEquipment(room, new Rect(inset, 96f, equipmentW, workTop - 108f));
            BuildMap(room, hero);
            BuildWorkDesk(room, new Rect(inset, workTop, w - inset * 2f, 76f),
                new Rect(railX, 18f, railW, 236f));
            BuildRail(room, railX, 266f, railW, payloadTop - 278f);
            BuildPayload(room, new Rect(mapX, payloadTop, w - inset - mapX, 136f));
            BuildActions(room, inset, actionTop, w - inset * 2f);
            BuildFooter(room, w, footerTop);
            dirty = true;
            shownFrame = -1;
        }

        private void BuildHeader(RectTransform room, float w)
        {
            Chrome.Rule(room, new Rect(22f, -79f, w - 44f, 1f), StationStyle.Line.WithAlpha(0.6f));
            Chrome.Lead(StationStyle.Text(room, "BASTION", new Rect(24f, -7f, w * 0.5f, 44f), 42f,
                StationStyle.Ink, 4f, bold: true), AvIcon.Satellite, 30f);
            subtitle = StationStyle.Text(room, "", new Rect(26f, -49f, w - 295f, 18f), 11f, StationStyle.Dim, 1f);
            position = StationStyle.Text(room, "", new Rect(w - 230f, -18f, 205f, 24f), 16f,
                StationStyle.Limb, 3f, TextAlignmentOptions.MidlineRight);
            focusStatus = StationStyle.Text(room, "", new Rect(w - 270f, -48f, 245f, 17f), 11f,
                StationStyle.Dim, 2f, TextAlignmentOptions.MidlineRight);
        }

        private void BuildMap(RectTransform room, Rect map)
        {
            Chrome.Panel(room, new Rect(map.x, -map.y, map.width, map.height), StationStyle.Console);
            board = new BoardSurface(room, new Rect(map.x, -map.y, map.width, map.height),
                new Rect(map.x + 28f, -map.y - 28f, map.width - 56f, map.height - 56f), true, true);
            board.Clicked = (at, button) =>
            {
                if (button != PointerEventData.InputButton.Left) return;
                board.Unproject(at, out float x, out float z);
                int closest = 0;
                float distance = float.MaxValue;
                for (int i = 0; i < sectors.Length; i++)
                {
                    float dx = x - sectorX[i], dz = z - sectorZ[i];
                    float next = dx * dx + dz * dz;
                    if (next >= distance) continue;
                    closest = i;
                    distance = next;
                }
                destination = closest;
                dirty = true;
            };
            var layerObject = new GameObject("TaskMapPieces", typeof(RectTransform));
            mapLayer = (RectTransform)layerObject.transform;
            mapLayer.SetParent(board.InputLayer, false);
            Chrome.Place(mapLayer, new Rect(-map.x, map.y, map.x + map.width, map.height + map.y));
            terrain = new BoardTerrain(mapLayer, board);
            terrain.SetTint(Color.white);
            Image veil = Chrome.Panel(mapLayer, new Rect(map.x, -map.y, map.width, map.height),
                StationStyle.Surface.WithAlpha(0.06f));
            veil.raycastTarget = false;
            coverageRing = new RingLine(mapLayer, 64, StationStyle.Limb.WithAlpha(0.8f), true);
            stationDot = Chrome.Panel(mapLayer, new Rect(0f, 0f, 14f, 14f),
                RoomPaint.Ready, OpsSprites.Dot);
            stationDot.type = Image.Type.Simple;
            stationDot.enabled = false;
            solutionRing = new RingLine(mapLayer, 48, AvTheme.RailCaution.WithAlpha(0.9f), false);
            for (int i = 0; i < sectors.Length; i++)
            {
                int sector = i;
                sectors[i] = MakeButton(mapLayer, new Rect(0f, 0f, 96f, 42f), SectorCodes[i], "",
                    () => { destination = sector; dirty = true; }, "Select " + StationKeeping.Name(i) + " as the station destination.");
            }
            stationDot.rectTransform.SetAsLastSibling();
            Chrome.Panel(room, new Rect(map.x + 8f, -map.y - 8f, Mathf.Min(map.width - 16f, 300f), 27f),
                StationStyle.Console.WithAlpha(0.92f));
            mapStatus = Chrome.Lead(StationStyle.Text(room, "// SECTORS", new Rect(map.x + 17f, -map.y - 13f,
                Mathf.Min(map.width - 34f, 284f), 17f), 11f, StationStyle.Ink, 2f), AvIcon.Radar2);
            fitMap = MakeButton(room, new Rect(map.x + map.width - 99f, -map.y - 8f, 90f, 34f),
                "FIT / F", "", () => { board.ResetFraming(); dirty = true; },
                "Fit station sectors and any live target solution into the map.");
            mapFooter = StationStyle.Text(room, "", new Rect(map.x + 16f, -map.y - map.height + 27f,
                map.width - 32f, 18f), 11f, StationStyle.Ink, 1f);
            Chrome.Outline(room, new Rect(map.x, -map.y, map.width, map.height), StationStyle.ConsoleEdge);
            StationStyle.Corners(room, new Rect(map.x, -map.y, map.width, map.height), StationStyle.Line);
        }

        private void BuildEquipment(RectTransform room, Rect at)
        {
            float dataW = 192f;
            RoomPaint.Inset(room, new Rect(at.x, -at.y, dataW, at.height), StationStyle.Console, StationStyle.Line);
            StationStyle.Text(room, "PLATFORM", new Rect(at.x + 12f, -at.y - 10f, dataW - 24f, 20f), 13f, StationStyle.Ink, 2f);
            orbitReadout = StationStyle.Text(room, "", new Rect(at.x + 12f, -at.y - 40f, dataW - 24f, 116f),
                14f, StationStyle.Ink, 1f, TextAlignmentOptions.TopLeft);
            orbitReadout.enableWordWrapping = true;
            OpsArtwork.Draw(room, new Rect(at.x + 5f, -at.y - 166f, dataW - 10f, dataW - 10f), 2);
            StationStyle.Text(room, "ORBITAL THEATER", new Rect(at.x + 12f, -at.y - 356f, dataW - 24f, 18f),
                11f, StationStyle.Dim, 1f);
            for (int i = 0; i < reserves.Length; i++)
            {
                reserves[i] = new BulletBar();
                reserves[i].Build(room, new Rect(at.x + 12f, -at.y - at.height + 178f - i * 42f,
                    dataW - 24f, 30f), StationStyle.Telemetry());
            }
            float artX = at.x + dataW + 12f, artW = at.width - dataW - 12f;
            equipment = OpsArtwork.Draw(room, new Rect(artX, -at.y, artW, at.height - 126f), 0);
            equipmentReadout = StationStyle.Text(room, "", new Rect(artX + 12f, -at.y - at.height + 116f,
                artW - 24f, 106f), 14f, StationStyle.Ink, 1f, TextAlignmentOptions.TopLeft);
            equipmentReadout.enableWordWrapping = true;
        }

        private void BuildPayload(RectTransform room, Rect at)
        {
            RoomPaint.Inset(room, new Rect(at.x, -at.y, at.width, at.height), StationStyle.Console, StationStyle.Line);
            StationStyle.Text(room, "FIRE CONTROL  /  MISSION PACKAGE", new Rect(at.x + 14f, -at.y - 6f, at.width - 28f, 20f),
                13f, StationStyle.Ink, 2f);
            OpsArtwork.DrawPayload(room, new Rect(at.x + 14f, -at.y - 30f, 212f, 94f));
            packageOutput = StationStyle.Text(room, "", new Rect(at.x + 242f, -at.y - 32f, at.width * 0.43f - 50f, 91f),
                18f, RoomPaint.Ready, 1f, TextAlignmentOptions.TopLeft, true);
            packageOutput.enableWordWrapping = true;
            float readoutX = at.x + at.width * 0.65f;
            solutionTitle = StationStyle.Text(room, "", new Rect(readoutX, -at.y - 30f, at.width * 0.35f - 16f, 20f),
                13f, StationStyle.Ink, 1f, bold: true);
            solutionDetail = StationStyle.Text(room, "", new Rect(readoutX, -at.y - 58f, at.width * 0.35f - 16f, 64f),
                12f, StationStyle.Dim, 0f, TextAlignmentOptions.TopLeft);
            solutionDetail.enableWordWrapping = true;
            hardware = equipmentReadout;
        }

        private void BuildWorkDesk(RectTransform room, Rect at, Rect instruments)
        {
            RoomPaint.Inset(room, new Rect(instruments.x, -instruments.y, instruments.width, instruments.height),
                StationStyle.Console, StationStyle.Line);
            StationStyle.Text(room, "GUIDANCE / THERMAL", new Rect(instruments.x + 14f, -instruments.y - 10f,
                instruments.width - 150f, 20f), 12f, StationStyle.Ink, 1f, bold: true);
            alignmentScope = Chrome.Graphic<AlignmentScopeGraphic>(room,
                new Rect(instruments.x + instruments.width - 134f, -instruments.y - 12f, 120f, 120f), "AlignmentScope");
            alignmentScope.raycastTarget = false;
            for (int i = 0; i < workGauges.Length; i++)
            {
                workGauges[i] = new BulletBar();
                float y = i == 0 ? 42f : i == 3 ? 88f : i == 1 ? 139f : 184f;
                Skin skin = StationStyle.Telemetry();
                skin.Fill = i == 2 ? RoomPaint.Command : RoomPaint.Ready;
                workGauges[i].Build(room, new Rect(instruments.x + 14f, -instruments.y - y,
                    instruments.width - (i == 0 || i == 3 ? 156f : 28f), 33f), skin);
            }
            float orderW = (at.width - 18f) / 4f;
            for (int i = 0; i < workOrders.Length; i++)
            {
                PlatformWork order = (PlatformWork)i;
                workOrders[i] = MakeButton(room, new Rect(at.x + i * (orderW + 6f), -at.y, orderW, 49f),
                    (i + 1) + "   " + OrbitalPlatform.WorkOrderName(order), "", () => Work(order),
                    order == PlatformWork.Track ? "20 kJ: time TRACK at a high servo match. Gyro and fresh recon improve alignment." :
                    order == PlatformWork.Charge ? "90 kJ: store charge, heat +22, alignment -5. SCREEN stores charge faster." :
                    order == PlatformWork.Vent ? "Shed heat, lose 8 charge and 4 alignment. Radiators improve venting." :
                    "40 kJ: bank one 90 second package. Needs alignment 45 and charge 40; cooler is better.");
                workOrders[i].Primary = order == PlatformWork.Commit;
                var frame = Chrome.Graphic<CommandStepGraphic>(workOrders[i].Control.Rect, new Rect(0f, 0f, orderW, 49f), "CommandStep");
                frame.transform.SetAsFirstSibling();
                frame.color = StationStyle.Line; frame.raycastTarget = false;
                workOrders[i].Step = frame;
                workOrders[i].Fill.enabled = false;
                workOrders[i].Rail.enabled = false;
                workOrders[i].Title.rectTransform.anchoredPosition += Vector2.right * 18f;
                workOrders[i].Detail.rectTransform.anchoredPosition += Vector2.right * 18f;
            }
            workReadback = StationStyle.Text(room, "", new Rect(at.x + 12f, -at.y - 54f, at.width - 24f, 20f),
                12f, StationStyle.Limb, 0f);
        }

        private void BuildRail(RectTransform room, float x, float top, float w, float h)
        {
            RoomPaint.Inset(room, new Rect(x, -top, w, h), StationStyle.Console, StationStyle.Line);
            StationStyle.Text(room, "MISSION PROFILE", new Rect(x + 14f, -top - 9f, w - 28f, 20f),
                13f, StationStyle.Ink, 2f);
            float branchH = Mathf.Max(38f, (h - 132f) / 3f);
            for (int i = 0; i < focuses.Length; i++)
            {
                PlatformFocus focus = (PlatformFocus)i;
                focuses[i] = MakeButton(room, new Rect(x + 12f, -top - 36f - i * (branchH + 4f), w - 24f, branchH),
                    BranchNames[i], FocusEffects[i], () => SelectFocus(focus),
                    FocusNames[i] + " selects the shared station power route. The host validates each request.");
                focuses[i].Detail.enableWordWrapping = true;
            }
            float orderTop = top + h - 78f;
            moveDetail = StationStyle.Text(room, "", new Rect(x + 14f, -orderTop, w - 28f, 26f),
                11f, StationStyle.Ink, 0f, TextAlignmentOptions.TopLeft);
            moveDetail.enableWordWrapping = true;
            relocate = MakeButton(room, new Rect(x + 12f, -orderTop - 30f, w - 24f, 42f),
                "RELOCATE PLATFORM", "", CommitRelocation, "Send the selected sector as a host-validated station order.");
        }

        private void BuildActions(RectTransform room, float x, float top, float w)
        {
            RoomPaint.Inset(room, new Rect(x, -top, w, 96f), StationStyle.Console, StationStyle.Line);
            Chrome.Lead(StationStyle.Text(room, "02 / DELIVER EFFECT   ·   ARM > RIGHT-CLICK GAME MAP", new Rect(x + 12f, -top - 7f,
                w - 24f, 18f), 11f, StationStyle.Limb, 1f), AvIcon.Target);
            float cellW = (w - 24f - 8f * (actions.Length - 1)) / actions.Length;
            for (int i = 0; i < actions.Length; i++)
            {
                SupportActionId action = ActionIds[i];
                actions[i] = MakeButton(room, new Rect(x + 12f + i * (cellW + 8f), -top - 31f, cellW, 54f),
                    ActionNames[i], "", () => Arm(action), ActionNames[i] + " — arm and pick a point on the game map.");
                actions[i].Detail.enableWordWrapping = true;
            }
        }

        private void BuildFooter(RectTransform room, float w, float top)
        {
            Chrome.Rule(room, new Rect(22f, -top, w - 44f, 1f), StationStyle.Line.WithAlpha(0.6f));
            sensor = MakeButton(room, new Rect(22f, -top - 13f, 190f, 43f), "SENSOR FEED", "SAR / OPTIONAL EO",
                () => openImager?.Invoke(null), "Open the steerable satellite sensor feed.");
            engineering = MakeButton(room, new Rect(220f, -top - 13f, 194f, 43f), "ENGINEERING", "BUILD / RESUPPLY",
                () => openEngineering?.Invoke(), "Open the detailed station assembly and power wall.");
            hostStatus = StationStyle.Text(room, "", new Rect(435f, -top - 13f, w - 458f, 44f), 12f,
                StationStyle.Ink, 1f, TextAlignmentOptions.MidlineRight);
            hostStatus.enableWordWrapping = true;
        }

        private static TaskButton MakeButton(RectTransform parent, Rect at, string title, string detail,
            Action click, string tooltip)
        {
            var button = new TaskButton { Control = RoomControl.Create(parent, at, click, "TaskButton") };
            RectTransform host = button.Control.Rect;
            button.Fill = Chrome.Panel(host, new Rect(0f, 0f, at.width, at.height), StationStyle.Surface);
            button.Rail = Chrome.Rule(host, new Rect(0f, 0f, 3f, at.height), StationStyle.Line);
            button.Title = StationStyle.Text(host, title, new Rect(11f, -2f, at.width - 20f, 20f), 12f,
                StationStyle.Ink, 0f, TextAlignmentOptions.MidlineLeft, true);
            button.Title.enableAutoSizing = true;
            button.Title.fontSizeMin = 11f; button.Title.fontSizeMax = 12f;
            button.Detail = StationStyle.Text(host, detail, new Rect(11f, -23f, at.width - 20f, Mathf.Max(16f, at.height - 24f)),
                10f, StationStyle.Dim, 0f);
            button.Control.WithTooltip(tooltip);
            button.Control.Changed = _ => PaintButton(button, button.Active, button.Caution);
            return button;
        }

        private static void PaintButton(TaskButton button, bool active, bool caution)
        {
            button.Active = active;
            button.Caution = caution;
            bool enabled = button.Control.Enabled;
            Color tone = caution ? AvTheme.RailCaution : active ? RoomPaint.Ready : StationStyle.Line;
            button.Fill.color = button.Primary && enabled ? RoomPaint.Command : active ? Color.Lerp(StationStyle.Surface, tone, 0.2f) :
                button.Control.Hovered && enabled ? StationStyle.Console : StationStyle.Surface;
            if (button.Step != null) button.Step.FillColor = button.Fill.color;
            button.Rail.color = enabled || active ? tone : AvTheme.Disabled;
            button.Title.color = button.Primary && enabled ? StationStyle.Surface : enabled || active ? StationStyle.Ink : StationStyle.Dim;
            button.Detail.color = button.Primary && enabled ? StationStyle.Surface : enabled ? StationStyle.Dim : AvTheme.Disabled;
        }

        public void Show(object context)
        {
            OrbitalPlatform platform = support?.LocalPlatform;
            destination = platform != null && platform.Exists ? platform.PositionIndex : StationKeeping.Centre;
            dirty = true;
        }

        public void Hide() { }
        public void Entrance(float progress) { if (contentFade != null) contentFade.alpha = Mathf.Clamp01(progress); }

        public void Refresh(double now, float time, bool textTick)
        {
            if (support == null) return;
            Paint(support.LocalPlatform, support.OrbitNow, textTick);
        }

        internal void Paint(OrbitalPlatform platform, double now, bool textTick)
        {
            double solution = platform != null && platform.Exists ? platform.SolutionRemaining(now) : 0.0;
            if (!textTick && !dirty && shownFrame == board.Revision) return;
            if (textTick || dirty)
            {
                int fitCount = sectorX.Length;
                if (solution > 0.0)
                {
                    fitX[fitCount] = platform.SolutionX;
                    fitZ[fitCount++] = platform.SolutionZ;
                }
                board.Fit(fitX, fitZ, fitCount, 44000f, 0f, 46f);
            }
            dirty = false;
            shownFrame = board.Revision;
            terrain.Refresh();
            bool exists = platform != null && platform.Exists;
            bool pending = support != null && (support.CommandPending || support.RequestPending);
            equipment.color = exists ? Color.white : Color.white.WithAlpha(0.25f);
            PaintWork(platform, now, pending);
            PlatformStats stats = exists ? platform.Stats(now) : default;
            reserves[0].Set(exists ? platform.Energy / Mathf.Max(1f, stats.StorageKj) : 0f, 0f, "ENERGY",
                exists ? PlatformWords.Whole(platform.Energy) + " / " + PlatformWords.Whole(stats.StorageKj) + " KJ" : "—");
            reserves[1].Set(exists ? platform.Fuel / Mathf.Max(1f, stats.FuelCapacity) : 0f, 0f, "FUEL",
                exists ? PlatformWords.Whole(platform.Fuel) + " / " + PlatformWords.Whole(stats.FuelCapacity) : "—");
            reserves[2].Set(exists ? platform.Rods / (float)Mathf.Max(1, stats.RodCapacity) : 0f, 0f, "RODS",
                exists ? platform.Rods + " / " + stats.RodCapacity : "—");
            reserves[3].Set(exists ? stats.Online / (float)Mathf.Max(1, stats.Modules) : 0f, 0f, "MODULES",
                exists ? stats.Online + " / " + stats.Modules + " ONLINE" : "—");
            Set(orbitReadout, exists ? "BASTION\nALT " + Mathf.RoundToInt((float)platform.State(now).Altitude / 1000f) +
                " KM\n" + StationKeeping.Name(platform.PositionIndex) + " SECTOR\n" + (platform.Brownout ? "POWER / BROWNOUT" : "POWER / STABLE") :
                "NO PLATFORM\nCORE NOT LAUNCHED\nCOMMISSION IN\nENGINEERING");
            Set(hardware, exists ? "SENSOR ARRAY   " + HardwareWord(platform, ModuleKind.Imager, now) +
                "\nCOMMS RELAY   " + HardwareWord(platform, ModuleKind.Relay, now) +
                "\nPAYLOAD / EMP   " + HardwareWord(platform, ModuleKind.Emp, now) +
                "\nTHERMAL / RADIATOR   " + HardwareWord(platform, ModuleKind.Radiator, now) +
                "\nMASS " + PlatformWords.Tonnes(stats.Mass) + "  /  " + stats.Online + " MODULES ONLINE" :
                "BASTION / PLATFORM SCHEMATIC\nLAUNCH CORE TO ESTABLISH UPLINK");

            int current = exists ? platform.PositionIndex : StationKeeping.Centre;
            bool moving = exists && platform.HoldAt(now) == PlatformHold.Rephase;
            Set(subtitle, exists ? "ORBITAL FIRE CONTROL" :
                "COMMISSION CORE > SURVEY ONLINE > EXPAND PAYLOADS");
            Set(position, exists ? StationKeeping.Name(current) + (moving ? " · TRANSIT" : "") : "NO STATION");
            Set(focusStatus, exists ? "FOCUS " + FocusNames[(int)platform.Focus] +
                (now < platform.RetaskUntil ? " · RETASK T-" + Mathf.CeilToInt((float)(platform.RetaskUntil - now)) + "S" : " · READY") :
                "BUILD / SUSTAIN / TASK");
            float coverage = exists ? platform.CoverageRadius(now) : 0f;
            OrbitState orbit = exists ? platform.State(now) : default;
            Vector2 stationAt = exists ? board.Project((float)orbit.SubX, (float)orbit.SubZ) : default;
            stationDot.enabled = moving;
            if (exists && coverage > 0f) coverageRing.Set(stationAt, board.Pixels(coverage), 2f,
                StationStyle.Limb.WithAlpha(0.8f));
            else coverageRing.Hide();
            if (stationDot.enabled) Chrome.Place(stationDot.rectTransform,
                new Rect(stationAt.x - 7f, stationAt.y + 7f, 14f, 14f));
            Set(mapStatus, (terrain.Available ? "LIVE THEATRE" : "SECTOR GRID") + " / REACH " +
                Mathf.RoundToInt(coverage / 1000f) + " KM");
            Set(mapFooter, "● PLATFORM   ▣ SELECTED   ○ REACH   [F] FIT");
            fitMap.Control.SetEnabled(true);
            PaintButton(fitMap, false, false);
            for (int i = 0; i < sectors.Length; i++)
            {
                Vector2 at = board.Project(sectorX[i], sectorZ[i]);
                Chrome.Place(sectors[i].Control.Rect, new Rect(at.x - 48f, at.y + 21f, 96f, 42f));
                sectors[i].Control.SetEnabled(exists);
                Set(sectors[i].Title, SectorCodes[i] + (i == current ? "  ◉" : ""));
                Set(sectors[i].Detail, i == current && moving ? "TRANSIT" : i == destination ? "SELECTED" :
                    i == current ? "ON STATION" : "SECTOR");
                PaintButton(sectors[i], i == destination, i == current);
            }
            bool fresh = solution > 0.0;
            if (fresh)
            {
                Vector2 at = board.Project(platform.SolutionX, platform.SolutionZ);
                solutionRing.Set(at, Mathf.Max(9f, board.Pixels(platform.SolutionRadius)), 2f,
                    AvTheme.RailCaution.WithAlpha(0.9f));
            }
            else solutionRing.Hide();
            Set(solutionTitle, fresh ? "RECON ASSIST  T-" + Mathf.CeilToInt((float)solution) + "S" :
                "RECON ASSIST / NONE");
            Set(solutionDetail, fresh ? "CENTRE " + Mathf.RoundToInt(platform.SolutionX / 1000f) + "/" +
                Mathf.RoundToInt(platform.SolutionZ / 1000f) + " KM  ·  RADIUS " +
                Mathf.RoundToInt(platform.SolutionRadius / 1000f) + " KM\nTRACK GAINS +10 ALIGNMENT" :
                "SCAN OR FIELD RECON BOOSTS TRACKING\nBASELINE WEAPONS DO NOT NEED A FIX");
            for (int i = 0; i < focuses.Length; i++)
            {
                bool active = exists && (int)platform.Focus == i;
                string upgrade = !exists ? (i == 0 ? "I CORE MISSING > II IMAGER / SIGINT > III RELAY" :
                    i == 1 ? "I RODS MISSING > II GYRO > III PRECISION" :
                    "I EMITTER MISSING > II POWER / COOLING > III EMP") :
                    i == 0 ? "I CORE ON · II IMG " + HardwareWord(platform, ModuleKind.Imager, now) +
                        " / SIG " + HardwareWord(platform, ModuleKind.Sigint, now) + " · III REL " +
                        HardwareWord(platform, ModuleKind.Relay, now) :
                    i == 1 ? "I ROD " + HardwareWord(platform, ModuleKind.Rods, now) + " · II GYRO " +
                        HardwareWord(platform, ModuleKind.Gyro, now) + " · III " + PackageWord(platform, PlatformFocus.Strike, now) :
                        "I EMP " + HardwareWord(platform, ModuleKind.Emp, now) + " · II BAT " +
                        HardwareWord(platform, ModuleKind.Battery, now) + " / RAD " + HardwareWord(platform, ModuleKind.Radiator, now);
                if (exists && focuses[i].Control.Rect.rect.height > 50f)
                    upgrade += i == 0 ? "\nSCENE ×" + platform.ScanScale(now).ToString("F2") +
                        " / REACH " + Mathf.RoundToInt(platform.CoverageRadius(now) / 1000f) + " KM" :
                        i == 1 ? "\n" + platform.RodSalvoCount(now) + " RODS / " + platform.PreparedRodScatter(now).ToString("F1") + " M SCATTER" :
                        "\nIII " + PackageWord(platform, PlatformFocus.Screen, now) + " / RADIUS ×" +
                        platform.EmpRadiusScale(now).ToString("F2") + " / VENT " +
                        (platform.FittedOnline(ModuleKind.Radiator, now) ? "50" : "35");
                Set(focuses[i].Detail, upgrade);
                focuses[i].Control.SetEnabled(exists && !pending && !active && now >= platform.RetaskUntil && platform.BoostRemaining(now) <= 0 &&
                    (support == null || support.LocalMayStationControl));
                PaintButton(focuses[i], active, false);
            }
            for (int i = 0; i < actions.Length; i++)
            {
                SupportActionDefinition definition = FindAction(i);
                AbilityFacts facts = definition != null && support != null
                    ? AbilityStatus.For(support, definition, support.BypassRequirements)
                    : new AbilityFacts(AbilityTone.Locked, "UNAVAILABLE ON THIS SERVER", "—", false, false);
                string cost = facts.CostText == "—" ? "—" : facts.CostText + " ALLOC";
                actions[i].Control.SetEnabled(facts.Enabled);
                Set(actions[i].Detail, cost + " · " + facts.Readiness);
                actions[i].Control.WithTooltip(ActionNames[i] + " · " + cost + " · " + facts.Readiness);
                PaintButton(actions[i], facts.Armed, facts.Tone == AbilityTone.Danger);
            }
            PlatformDenial move = exists ? platform.CheckRelocate(destination, now) : PlatformDenial.NoPlatform;
            float team = support != null ? support.TeamCooldownRemaining(TeamGate.Relocate) : 0f;
            relocate.Control.SetEnabled(exists && !pending && team <= 0.5f && move == PlatformDenial.None &&
                (support == null || support.LocalMayStationControl));
            Set(moveDetail, "DESTINATION  " + StationKeeping.Name(destination) + "  ·  " +
                Mathf.RoundToInt(PlatformAbilities.Info(PlatformAbility.Rephase).Fuel) + " FUEL / " +
                Mathf.RoundToInt((float)OrbitalPlatform.RephaseLeadSeconds) + "S" +
                (team > 0.5f ? "  ·  TEAM T-" + Mathf.CeilToInt(team) + "S" : ""));
            Set(relocate.Detail, pending ? "AWAITING HOST" : move == PlatformDenial.None ? "HOST ORDER" :
                PlatformWords.Denial(move, platform, PlatformAbility.Rephase, now));
            PaintButton(relocate, false, move != PlatformDenial.None);
            Set(engineering.Title, exists ? "ENGINEERING" : "COMMISSION BASTION");
            Set(engineering.Detail, exists ? "EXPAND / RESUPPLY" : "CORE INCLUDES SURVEY");
            engineering.Control.SetEnabled(true);
            sensor.Control.SetEnabled(exists);
            PaintButton(engineering, false, false);
            PaintButton(sensor, false, false);
            Set(hostStatus, pending ? "HOST / ORDER PENDING" :
                support != null && !string.IsNullOrEmpty(support.Status) ? "HOST / " + support.Status.ToUpperInvariant() :
                "FLIGHT / BASELINE PAYLOADS READY. OPERATOR WORK IMPROVES THE NEXT SHOT.");
        }

        private void PaintWork(OrbitalPlatform platform, double now, bool pending)
        {
            bool exists = platform != null && platform.Exists;
            float alignment = exists ? platform.Alignment : 0f, charge = exists ? platform.Capacitor : 0f;
            float heat = exists ? platform.Heat : 0f;
            workGauges[0].Set(alignment / 100f, 0.45f, "ALIGN", Mathf.RoundToInt(alignment) + " / 100");
            workGauges[1].Set(charge / 100f, 0.4f, "CHARGE", Mathf.RoundToInt(charge) + " / 100");
            workGauges[2].Set(heat / 100f, 0.78f, "HEAT", Mathf.RoundToInt(heat) + " / 100");
            float match = exists ? platform.TrackingMatch(now) : 0f;
            alignmentScope.Set(alignment / 100f, match);
            workGauges[3].Set(match, 0.8f, "SERVO MATCH", Mathf.RoundToInt(match * 100f) + "%");
            bool mayControl = support == null || support.LocalMayStationControl;
            bool banked = exists && platform.BoostRemaining(now) > 0;
            float quality = exists ? banked ? platform.Boost : platform.PreparedQuality : 0f;
            for (int i = 0; i < workOrders.Length; i++)
            {
                var order = (PlatformWork)i;
                PlatformWorkDenial denial = exists ? platform.CheckWork(order, now) : PlatformWorkDenial.NoPlatform;
                workOrders[i].Control.SetEnabled(!pending && mayControl && denial == PlatformWorkDenial.None);
                string detail = order == PlatformWork.Vent ? "SHED HEAT / FREE" :
                    OrbitalPlatform.WorkCost(order) + " KJ" + (order == PlatformWork.Commit ? " / BANK" : " / ORDER");
                if (order == PlatformWork.Track && exists) detail = "20 KJ / +" + Mathf.RoundToInt(platform.TrackingGain(now)) + " ALIGN";
                if (denial == PlatformWorkDenial.Cooldown) detail = "ACK T-" + platform.WorkRemaining(now).ToString("F1") + "S";
                else if (denial == PlatformWorkDenial.NoPlatform) detail = "COMMISSION CORE";
                else if (denial == PlatformWorkDenial.SurveyFocus) detail = "SELECT WEAPON BRANCH";
                else if (denial == PlatformWorkDenial.Holding) detail = "BUS TRANSITION";
                else if (denial == PlatformWorkDenial.LowEnergy) detail = "LOW STATION ENERGY";
                else if (denial == PlatformWorkDenial.TooHot) detail = "THERMAL / VENT FIRST";
                else if (denial == PlatformWorkDenial.Incomplete) detail = "NEED ALIGN 45 / CHARGE 40";
                else if (denial == PlatformWorkDenial.AlreadyBanked) detail = "PACKAGE BANKED";
                else if (denial == PlatformWorkDenial.NothingToVent) detail = "THERMAL NOMINAL";
                if (!mayControl) detail = "OPERATOR HAS THE LOOP";
                if (pending) detail = "AWAITING HOST";
                Set(workOrders[i].Detail, detail);
                PaintButton(workOrders[i], banked && order == PlatformWork.Commit, denial == PlatformWorkDenial.TooHot);
            }
            Set(workReadback, !exists ? "FLIGHT / COMMISSION THE CORE. SURVEY RADAR COMES WITH IT." :
                !mayControl ? "FLIGHT / ANOTHER OPERATOR HAS THE LOOP. YOU CAN STILL DELIVER FITTED ABILITIES." :
                pending ? "FLIGHT / ORDER SENT. AWAITING HOST READBACK." :
                banked ? "FIRE CONTROL / PACKAGE BANKED. NEXT MATCHING WEAPON SPENDS IT. T-" +
                    Mathf.CeilToInt((float)platform.BoostRemaining(now)) + "S" :
                platform.Focus == PlatformFocus.Survey ? "FLIGHT / SURVEY IS ONLINE. SELECT STRIKE OR SCREEN TO WORK A WEAPON PACKAGE." :
                heat > 78 ? "THERMAL / CHARGE INHIBITED. VENT NOW; COOLER PACKAGES DELIVER MORE." :
                alignment < 45 ? "GUIDANCE / TIME TRACK AT HIGH SERVO MATCH. GYRO + RECON IMPROVE EACH CORRECTION." :
                charge < 40 ? "POWER / CHARGE THE CAPACITOR. WATCH HEAT AND ALIGNMENT AS CHARGE RISES." :
                "FLIGHT / COMMIT IS GO. WORK LONGER FOR QUALITY, OR BANK NOW AND FLY.");
            float baseEmp = support?.Settings != null ? support.Settings.EmpRadius.Value : 12000f;
            float radius = Mathf.Min(exists ? baseEmp * platform.EmpScaleAt(now) * (1f + quality * 0.25f) : baseEmp,
                SupportEffectPolicy.MaxEmpRadius);
            float duration = SupportEffectPolicy.EmpDuration * (1f + quality * 0.25f);
            float scatter = exists ? platform.RodScatter(now) * (1f - quality * 0.4f) : 15f;
            PlatformFocus focus = exists ? banked ? platform.BoostFocus : platform.Focus : PlatformFocus.Survey;
            Set(packageOutput, (banked ? "PACKAGE READY" : "WORKING FORECAST") + " / " + Mathf.RoundToInt(quality * 100f) + "%\n" +
                (focus == PlatformFocus.Screen ? "EMP " + (radius / 1000f).ToString("F1") + " KM / " + duration.ToString("F1") + " S\nHOSTILE RADARS ONLY" :
                focus == PlatformFocus.Strike ? "ROD SCATTER " + scatter.ToString("F1") + " M\nONE USE / EXISTING AMMUNITION" :
                "CORE SURVEY / IMAGER EXPANDS IT\nWEAPON WORK IS OPTIONAL"));
        }

        public bool HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) { Work(PlatformWork.Track); return true; }
            if (Input.GetKeyDown(KeyCode.Alpha2)) { Work(PlatformWork.Charge); return true; }
            if (Input.GetKeyDown(KeyCode.Alpha3)) { Work(PlatformWork.Vent); return true; }
            if (Input.GetKeyDown(KeyCode.Alpha4)) { Work(PlatformWork.Commit); return true; }
            if (Input.GetKeyDown(KeyCode.F)) { board.ResetFraming(); dirty = true; return true; }
            if (Input.GetKeyDown(KeyCode.Tab)) { openImager?.Invoke(null); return true; }
            if (Input.GetKeyDown(KeyCode.G)) { openEngineering?.Invoke(); return true; }
            if (Input.GetKeyDown(KeyCode.Return)) { CommitRelocation(); return true; }
            return false;
        }

        public void RightClickInside() { destination = support?.LocalPlatform?.PositionIndex ?? StationKeeping.Centre; dirty = true; }

        private void SelectFocus(PlatformFocus focus)
        {
            OrbitalPlatform platform = support?.LocalPlatform;
            if (platform == null || !platform.Exists || support.CommandPending || support.RequestPending ||
                !support.LocalMayStationControl || platform.Focus == focus || support.OrbitNow < platform.RetaskUntil) return;
            support.RequestPlatformFocus(focus);
            dirty = true;
        }

        private void Work(PlatformWork order)
        {
            OrbitalPlatform platform = support?.LocalPlatform;
            if (platform == null || support.CommandPending || support.RequestPending ||
                !support.LocalMayStationControl || platform.CheckWork(order, support.OrbitNow) != PlatformWorkDenial.None) return;
            support.RequestPlatformWork(order);
            dirty = true;
        }

        private void CommitRelocation()
        {
            OrbitalPlatform platform = support?.LocalPlatform;
            if (platform == null || support.CommandPending || support.RequestPending ||
                !support.LocalMayStationControl ||
                support.TeamCooldownRemaining(TeamGate.Relocate) > 0.5f ||
                platform.CheckRelocate(destination, support.OrbitNow) != PlatformDenial.None) return;
            support.RequestRelocate(destination);
            dirty = true;
        }

        private void Arm(SupportActionId action)
        {
            if (support == null) return;
            int index = Array.IndexOf(ActionIds, action);
            SupportActionDefinition definition = index >= 0 ? FindAction(index) : null;
            if (definition == null || !AbilityStatus.For(support, definition, support.BypassRequirements).Enabled) return;
            support.Arm(action);
            if (support.ArmedAction == action) exitToMap?.Invoke();
            dirty = true;
        }

        private SupportActionDefinition FindAction(int index)
        {
            if (definitions[index] != null || support == null) return definitions[index];
            var available = support.Actions;
            for (int i = 0; i < available.Count; i++)
                if (available[i].Id == ActionIds[index]) return definitions[index] = available[i];
            return null;
        }

        private static string HardwareWord(OrbitalPlatform platform, ModuleKind kind, double now) =>
            platform.FittedOnline(kind, now) ? "ON" : platform.Pending == kind ? "INBOUND" : platform.Fitted(kind) ? "OFFLINE" : "MISSING";

        private static string PackageWord(OrbitalPlatform platform, PlatformFocus focus, double now) =>
            platform.BoostRemaining(now) > 0 && platform.BoostFocus == focus ? "BANKED" :
            platform.Focus == focus && platform.PreparedQuality > 0f ? "WORK " + Mathf.RoundToInt(platform.PreparedQuality * 100f) + "%" : "WORK DESK";

        private static void Set(TMP_Text label, string value) { if (label.text != value) label.text = value; }
    }
}
