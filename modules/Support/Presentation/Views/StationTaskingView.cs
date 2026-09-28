using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Presentation.Board;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>The station's default tactical room. All orders use the existing host command and map gesture paths.</summary>
    internal sealed class StationTaskingView : IOpsView
    {
        private static readonly string[] SectorCodes = { "NW", "N", "NE", "W", "C", "E", "SW", "S", "SE" };
        private static readonly string[] FocusNames = { "SURVEY", "STRIKE", "SCREEN" };
        private static readonly string[] FocusEffects =
        {
            "WIDE SCANS · 1.2× REACH · 0.75× RECHARGE",
            "PRECISION ROD · 0.8× SCAN · 1.25× RECHARGE",
            "EMP SUPPRESSION · 0.8× SCAN · 1.25× RECHARGE"
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
            public TMP_Text Title, Detail;
            public bool Active, Caution;
        }

        private readonly SupportManager support;
        private readonly Action openEngineering;
        private readonly Action<GlobalPosition?> openImager;
        private readonly Action exitToMap;
        private readonly TaskButton[] sectors = new TaskButton[StationKeeping.Count];
        private readonly TaskButton[] focuses = new TaskButton[FocusNames.Length];
        private readonly TaskButton[] actions = new TaskButton[ActionIds.Length];
        private readonly SupportActionDefinition[] definitions = new SupportActionDefinition[ActionIds.Length];
        private readonly Rect[] sections = new Rect[4];
        private readonly float[] sectorX = new float[StationKeeping.Count];
        private readonly float[] sectorZ = new float[StationKeeping.Count];
        private readonly float[] fitX = new float[StationKeeping.Count + 5];
        private readonly float[] fitZ = new float[StationKeeping.Count + 5];
        private BoardSurface board;
        private BoardTerrain terrain;
        private RectTransform mapLayer;
        private RingLine coverageRing, solutionRing;
        private Image stationDot;
        private TMP_Text subtitle, mapStatus, position, focusStatus, solutionTitle, solutionDetail;
        private TMP_Text moveDetail, hostStatus, mapFooter, guidance;
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
            float railW = Mathf.Clamp(w * 0.34f, 380f, 490f);
            float railX = w - railW - 22f;
            float mapW = railX - 34f;
            float bodyTop = 92f, footerTop = h - 72f;
            float bodyH = footerTop - bodyTop - 8f;
            hero = new Rect(22f, bodyTop, mapW, bodyH);
            sections[0] = new Rect(0f, 0f, w, 80f);
            sections[1] = hero;
            sections[2] = new Rect(railX, bodyTop, railW, bodyH);
            sections[3] = new Rect(0f, footerTop, w, h - footerTop);

            AvKit.Panel(room, new Rect(0f, 0f, w, h), StationStyle.Surface);
            contentFade = room.gameObject.GetComponent<CanvasGroup>();
            if (contentFade == null) contentFade = room.gameObject.AddComponent<CanvasGroup>();
            BuildHeader(room, w);
            BuildMap(room, hero);
            BuildRail(room, railX, bodyTop, railW, bodyH);
            BuildFooter(room, w, footerTop);
            dirty = true;
            shownFrame = -1;
        }

        private void BuildHeader(RectTransform room, float w)
        {
            AvKit.Rule(room, new Rect(22f, -79f, w - 44f, 1f), StationStyle.Line.WithAlpha(0.6f));
            StationStyle.Text(room, "BASTION / TASKING", new Rect(24f, -12f, w * 0.45f, 34f), 26f,
                StationStyle.Ink, 4f, bold: true);
            subtitle = StationStyle.Text(room, "", new Rect(26f, -49f, w * 0.56f, 18f), 11f, StationStyle.Dim, 2f);
            position = StationStyle.Text(room, "", new Rect(w - 465f, -18f, 440f, 24f), 16f,
                StationStyle.Limb, 3f, TextAlignmentOptions.MidlineRight);
            focusStatus = StationStyle.Text(room, "", new Rect(w - 465f, -48f, 440f, 17f), 11f,
                StationStyle.Dim, 2f, TextAlignmentOptions.MidlineRight);
        }

        private void BuildMap(RectTransform room, Rect map)
        {
            AvKit.Panel(room, new Rect(map.x, -map.y, map.width, map.height), StationStyle.Console);
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
            AvKit.Place(mapLayer, new Rect(-map.x, map.y, map.x + map.width, map.height + map.y));
            terrain = new BoardTerrain(mapLayer, board);
            terrain.SetTint(Color.white.WithAlpha(0.70f));
            Image veil = AvKit.Panel(mapLayer, new Rect(map.x, -map.y, map.width, map.height),
                StationStyle.Surface.WithAlpha(0.22f));
            veil.raycastTarget = false;
            coverageRing = new RingLine(mapLayer, 64, StationStyle.Limb.WithAlpha(0.8f), true);
            stationDot = AvKit.Panel(mapLayer, new Rect(0f, 0f, 14f, 14f),
                AvTheme.RailReady, OpsSprites.Dot);
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
            AvKit.Panel(room, new Rect(map.x + 8f, -map.y - 8f, Mathf.Min(map.width - 16f, 300f), 27f),
                StationStyle.Console.WithAlpha(0.92f));
            mapStatus = StationStyle.Text(room, "THEATRE / STATION SECTORS", new Rect(map.x + 17f, -map.y - 13f,
                Mathf.Min(map.width - 34f, 284f), 17f), 11f, StationStyle.Ink, 2f);
            fitMap = MakeButton(room, new Rect(map.x + map.width - 99f, -map.y - 8f, 90f, 34f),
                "FIT / F", "", () => { board.ResetFraming(); dirty = true; },
                "Fit station sectors and any live target solution into the map.");
            mapFooter = StationStyle.Text(room, "", new Rect(map.x + 16f, -map.y - map.height + 27f,
                map.width - 32f, 18f), 11f, StationStyle.Ink, 1f);
            AvKit.Outline(room, new Rect(map.x, -map.y, map.width, map.height), StationStyle.ConsoleEdge);
        }

        private void BuildRail(RectTransform room, float x, float top, float w, float h)
        {
            // The window can be only 720 high. Keep the final action clear of the
            // relocation order at that size while retaining the roomy layout above it.
            bool compact = h < 568f;
            float focusStep = compact ? 50f : 54f;
            float focusHeight = compact ? 48f : 50f;
            AvKit.Panel(room, new Rect(x, -top, w, h), StationStyle.Console.WithAlpha(0.94f));
            AvKit.Outline(room, new Rect(x, -top, w, h), StationStyle.ConsoleEdge);
            StationStyle.Text(room, "POWER ROUTING / FACTION SHARED", new Rect(x + 15f, -top - 11f, w - 30f, 17f),
                12f, StationStyle.Limb, 2f);
            float focusTop = top + 34f;
            for (int i = 0; i < focuses.Length; i++)
            {
                PlatformFocus focus = (PlatformFocus)i;
                focuses[i] = MakeButton(room, new Rect(x + 14f, -focusTop - i * focusStep, w - 28f, focusHeight),
                    FocusNames[i], FocusEffects[i], () => SelectFocus(focus),
                    FocusNames[i] + " changes the shared station focus after a 12 second retask.");
            }
            float solutionTop = focusTop + (compact ? 157f : 169f);
            AvKit.Panel(room, new Rect(x + 14f, -solutionTop, w - 28f, 80f), StationStyle.Surface);
            AvKit.Rule(room, new Rect(x + 14f, -solutionTop, 3f, 80f), StationStyle.Limb);
            solutionTitle = StationStyle.Text(room, "", new Rect(x + 27f, -solutionTop - 9f, w - 54f, 18f),
                13f, StationStyle.Ink, 2f);
            solutionDetail = StationStyle.Text(room, "", new Rect(x + 27f, -solutionTop - 35f, w - 54f, 42f),
                12f, StationStyle.Dim, 1f, TextAlignmentOptions.TopLeft);
            solutionDetail.enableWordWrapping = true;

            float actionTop = solutionTop + (compact ? 90f : 95f);
            StationStyle.Text(room, "MAP OPERATIONS / ARM, THEN RIGHT-CLICK THEATRE", new Rect(x + 15f, -actionTop,
                w - 30f, 17f), 11f, StationStyle.Limb, 1f);
            float cellW = (w - 35f) * 0.5f;
            for (int i = 0; i < actions.Length; i++)
            {
                SupportActionId action = ActionIds[i];
                int col = i % 2, row = i / 2;
                actions[i] = MakeButton(room, new Rect(x + 14f + col * (cellW + 7f), -actionTop - 22f - row * 48f,
                        cellW, 47f), ActionNames[i], "", () => Arm(action),
                    ActionNames[i] + " — leave this room and right-click a point on the game map.");
                AvKit.Place(actions[i].Title.rectTransform, new Rect(11f, -2f, cellW - 20f, 18f));
                AvKit.Place(actions[i].Detail.rectTransform, new Rect(11f, -20f, cellW - 20f, 25f));
                actions[i].Detail.enableWordWrapping = true;
            }
            float bottomTop = top + h - 85f;
            float noteTop = actionTop + 178f;
            if (bottomTop - noteTop > 82f)
            {
                float noteH = bottomTop - noteTop - 10f;
                AvKit.Panel(room, new Rect(x + 14f, -noteTop, w - 28f, noteH), StationStyle.Surface.WithAlpha(0.75f));
                AvKit.Rule(room, new Rect(x + 14f, -noteTop, 3f, noteH), StationStyle.Line.WithAlpha(0.75f));
                StationStyle.Text(room, "TACTICAL CYCLE", new Rect(x + 27f, -noteTop - 10f, w - 54f, 18f),
                    11f, StationStyle.Limb, 2f);
                guidance = StationStyle.Text(room, "", new Rect(x + 27f, -noteTop - 37f, w - 54f, noteH - 49f),
                    12f, StationStyle.Ink, 1f, TextAlignmentOptions.TopLeft);
                guidance.enableWordWrapping = true;
            }
            moveDetail = StationStyle.Text(room, "", new Rect(x + 16f, -bottomTop, w - 32f, 22f),
                12f, StationStyle.Ink, 1f);
            relocate = MakeButton(room, new Rect(x + 14f, -bottomTop - 29f, w - 28f, 40f),
                "COMMIT RELOCATION", "", CommitRelocation, "Send the selected sector as a host-validated station order.");
        }

        private void BuildFooter(RectTransform room, float w, float top)
        {
            AvKit.Rule(room, new Rect(22f, -top, w - 44f, 1f), StationStyle.Line.WithAlpha(0.6f));
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
            button.Fill = AvKit.Panel(host, new Rect(0f, 0f, at.width, at.height), StationStyle.Surface);
            button.Rail = AvKit.Rule(host, new Rect(0f, 0f, 3f, at.height), StationStyle.Line);
            button.Title = StationStyle.Text(host, title, new Rect(11f, -4f, at.width - 20f, 22f), 13f,
                StationStyle.Ink, 1f, TextAlignmentOptions.MidlineLeft, true);
            button.Detail = StationStyle.Text(host, detail, new Rect(11f, -27f, at.width - 20f, at.height - 28f),
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
            Color tone = caution ? AvTheme.RailCaution : active ? AvTheme.RailReady : StationStyle.Line;
            button.Fill.color = active ? Color.Lerp(StationStyle.Surface, tone, 0.2f) :
                button.Control.Hovered && enabled ? StationStyle.Console : StationStyle.Surface;
            button.Rail.color = enabled || active ? tone : AvTheme.Disabled;
            button.Title.color = enabled || active ? StationStyle.Ink : StationStyle.Dim;
            button.Detail.color = enabled ? StationStyle.Dim : AvTheme.Disabled;
        }

        public void Show(object context)
        {
            OrbitalPlatform platform = support?.LocalPlatform;
            destination = platform != null && platform.Exists ? platform.PositionIndex : StationKeeping.Centre;
            dirty = true;
        }

        public void Hide() => AvButton.ClearTooltip();
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
                if (platform != null && platform.Exists)
                {
                    float reach = platform.CoverageRadius(now);
                    if (reach > 0f)
                    {
                        OrbitState station = platform.State(now);
                        fitX[fitCount] = (float)station.SubX - reach;
                        fitZ[fitCount++] = (float)station.SubZ;
                        fitX[fitCount] = (float)station.SubX + reach;
                        fitZ[fitCount++] = (float)station.SubZ;
                        fitX[fitCount] = (float)station.SubX;
                        fitZ[fitCount++] = (float)station.SubZ - reach;
                        fitX[fitCount] = (float)station.SubX;
                        fitZ[fitCount++] = (float)station.SubZ + reach;
                    }
                }
                if (solution > 0.0)
                {
                    fitX[fitCount] = platform.SolutionX;
                    fitZ[fitCount++] = platform.SolutionZ;
                }
                board.Fit(fitX, fitZ, fitCount, 58000f, 0f, 24f);
            }
            dirty = false;
            shownFrame = board.Revision;
            terrain.Refresh();
            bool exists = platform != null && platform.Exists;
            bool pending = support != null && (support.CommandPending || support.RequestPending);
            int current = exists ? platform.PositionIndex : StationKeeping.Centre;
            bool moving = exists && platform.HoldAt(now) == PlatformHold.Rephase;
            Set(subtitle, exists ? "ORBITAL PLATFORM · FACTION SHARED · HOST AUTHORITY" :
                "NO PLATFORM · OPEN ENGINEERING TO LAUNCH THE CORE");
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
            if (stationDot.enabled) AvKit.Place(stationDot.rectTransform,
                new Rect(stationAt.x - 7f, stationAt.y + 7f, 14f, 14f));
            Set(mapStatus, (terrain.Available ? "LIVE THEATRE" : "SECTOR GRID") + " / REACH " +
                Mathf.RoundToInt(coverage / 1000f) + " KM");
            Set(mapFooter, "PLATFORM  ●    SELECTED  ▣    RING = REACH · WHEEL ZOOM · DRAG PAN · F FIT");
            fitMap.Control.SetEnabled(true);
            PaintButton(fitMap, false, false);
            for (int i = 0; i < sectors.Length; i++)
            {
                Vector2 at = board.Project(sectorX[i], sectorZ[i]);
                AvKit.Place(sectors[i].Control.Rect, new Rect(at.x - 48f, at.y + 21f, 96f, 42f));
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
            Set(solutionTitle, fresh ? "TARGET SOLUTION / LIVE  T-" + Mathf.CeilToInt((float)solution) + "S" :
                "TARGET SOLUTION / NONE");
            Set(solutionDetail, fresh ? "CENTRE " + Mathf.RoundToInt(platform.SolutionX / 1000f) + "/" +
                Mathf.RoundToInt(platform.SolutionZ / 1000f) + " KM  ·  RADIUS " +
                Mathf.RoundToInt(platform.SolutionRadius / 1000f) + " KM\nROD OR EMP CONSUMES THIS SHARED FIX" :
                "SCAN THE MAP TO GENERATE A 75 SECOND FIX.\nHEAVY EFFECTS REQUIRE IT INSIDE THE RING.");
            if (guidance != null) Set(guidance,
                "01  SCAN TO BUILD A SHARED TARGET FIX. SURVEY REACHES FARTHER.\n\n" +
                "02  ROUTE POWER TO STRIKE OR SCREEN. RETASKING TAKES 12 SECONDS.\n\n" +
                "03  ARM A MAP ACTION. A HEAVY EFFECT SPENDS THE FIX; THE OTHER TEAM CAN REACT.");
            for (int i = 0; i < focuses.Length; i++)
            {
                bool active = exists && (int)platform.Focus == i;
                focuses[i].Control.SetEnabled(exists && !pending && !active && now >= platform.RetaskUntil);
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
            relocate.Control.SetEnabled(exists && !pending && team <= 0.5f && move == PlatformDenial.None);
            Set(moveDetail, "DESTINATION  " + StationKeeping.Name(destination) + "  ·  " +
                Mathf.RoundToInt(PlatformAbilities.Info(PlatformAbility.Rephase).Fuel) + " FUEL / " +
                Mathf.RoundToInt((float)OrbitalPlatform.RephaseLeadSeconds) + "S" +
                (team > 0.5f ? "  ·  TEAM T-" + Mathf.CeilToInt(team) + "S" : ""));
            Set(relocate.Detail, pending ? "AWAITING HOST" : move == PlatformDenial.None ? "HOST ORDER" :
                PlatformWords.Denial(move, platform, PlatformAbility.Rephase, now));
            PaintButton(relocate, false, move != PlatformDenial.None);
            engineering.Control.SetEnabled(true);
            sensor.Control.SetEnabled(exists);
            PaintButton(engineering, false, false);
            PaintButton(sensor, false, false);
            Set(hostStatus, pending ? "HOST / ORDER PENDING" :
                support != null && !string.IsNullOrEmpty(support.Status) ? "HOST / " + support.Status.ToUpperInvariant() :
                "SELECT A SECTOR OR ROUTE STATION POWER");
        }

        public bool HandleKeys()
        {
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
                platform.Focus == focus || support.OrbitNow < platform.RetaskUntil) return;
            support.RequestPlatformFocus(focus);
            dirty = true;
        }

        private void CommitRelocation()
        {
            OrbitalPlatform platform = support?.LocalPlatform;
            if (platform == null || support.CommandPending || support.RequestPending ||
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

        private static void Set(TMP_Text label, string value) { if (label.text != value) label.text = value; }
    }
}
