using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// STR bezel: situation, chain of command, and a short live-war brief. TheaterOps owns
    /// the war and validates broad player intent; this screen does not select or order units.
    /// Unknown figures read as unknown, and short bays scroll before hiding content.
    /// </summary>
    internal sealed partial class StrMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.25f;

        /// <summary>The known-AD line asks Intel at 1 Hz, and only while the SA page shows.</summary>
        private const float KnownAdInterval = 1f;

        private const int TabSa = 0;
        private const int TabCoc = 1;
        private const int TabCmd = 2;

        /// <summary>Contested nodes the SITUATION list can rank; a hard ceiling, not a page size.</summary>
        private const int NodeRankCap = 32;
        private const int NodeListPageSize = 6;

        private static readonly SortieRole[] Roles =
        {
            SortieRole.Cap, SortieRole.Sead, SortieRole.Cas, SortieRole.Strike, SortieRole.Transit,
        };

        // ---- Dependencies ----------------------------------------------------------------

        private CommandSettings settings;
        private CommandManager command;
        private ComMapOverlay overlay;
        private ManualLogSource logger;
        private IBaseDefenseAlarmService baseAlarm;
        private IHighCommandView highCommand;
        private ITheaterWarView theaterWar;
        private IThreatPicture threatPicture;

        // ---- Screen ----------------------------------------------------------------------

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvConsole console;
        private AvChip[] chips;
        private AvMetric[] metrics;
        private AvFlow cocPage;
        private bool cocBuilt;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;

        // ---- SITUATION page ----------------------------------------------------------------

        private AvRow airRow;
        private AvSection sortieSection;
        private readonly AvRow[] sortieRows = new AvRow[5];
        private AvRow groundRow, airbaseRow, radarRow, knownAdRow;
        private float nextKnownAdRefresh;
        private readonly AirDefenceRing[] knownAdRings = new AirDefenceRing[ThreatPictureLimits.MaximumRings];

        private AvRow alliedRow, contestedRow, hostileRow, unclaimedRow, frontlineRow, nodesRow;
        private AvSection contestedSection;
        private AvList nodeList;
        private readonly TacticalSectorGrid.TacticalNode[] ranked =
            new TacticalSectorGrid.TacticalNode[NodeRankCap];
        private int rankedCount;

        // ==================================================================================

        public void Configure(
            CommandSettings config, CommandManager manager, ComMapOverlay mapOverlay,
            ManualLogSource log)
        {
            settings = config;
            command = manager;
            overlay = mapOverlay;
            logger = log;
        }

        public void ResetForScene()
        {
            MfdBezel.Release(MfdSlots.Str);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);

            screenRoot = null;
            screen = null;
            console = null;
            chips = null;
            metrics = null;
            baseAlarm = null;
            highCommand = null;
            cocPage = null;
            cocBuilt = false;
            theaterWar = null;
            threatPicture = null;

            airRow = null;
            sortieSection = null;
            Array.Clear(sortieRows, 0, sortieRows.Length);
            groundRow = airbaseRow = radarRow = knownAdRow = null;
            nextKnownAdRefresh = 0f;

            alliedRow = contestedRow = hostileRow = unclaimedRow = frontlineRow = nodesRow = null;
            contestedSection = null;
            nodeList = null;
            rankedCount = 0;

            ResetCoc();
            ResetCmd();

            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || command == null || settings == null || !settings.Enabled.Value) return;
            if (Application.isBatchMode) { failed = true; return; }
            if (!GameAccess.MfdAvailable) { failed = true; return; }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            // A closed screen costs nothing. The old theater tab refreshed on every page,
            // including the ones that were not showing it.
            if (!screen.isActive || Time.unscaledTime < nextRefresh) return;

            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        // ---- Installation ----------------------------------------------------------------

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd =
                    SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdBezel.TryClaim(MfdSlots.Str, preferLeft: true, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    // No slot is a crowded bezel, not a broken mod: OPS still installs.
                    failed = true;
                    logger?.LogWarning("STR MFD unavailable: no free bezel slot.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdBezel.Release(MfdSlots.Str);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdBezel.Release(MfdSlots.Str);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    MfdBezel.Release(MfdSlots.Str);
                    if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);
                    screenRoot = null;
                    screen = null;
                    failed = true;
                    logger?.LogWarning("STR MFD unavailable: claimed bezel changed before binding.");
                    return;
                }
                logger?.LogInfo("STR MFD installed on " + (left ? "left" : "right") +
                                " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                MfdBezel.Release(MfdSlots.Str);
                failed = true;
                logger?.LogError("STR MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            var root = new GameObject("BoscaliStrategic.Screen", typeof(RectTransform));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            // Position is deliberately not copied; see the same note on the OPS screen.
            // VirtualMFD.showPos is zero and MFDScreen.ShowScreen assigns it straight to
            // localPosition, so a screen is placed by its parent and anchors.
            float height = ResolveHeight(
                templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            ClampPanelIntoCanvas(rootRect);

            ModServices.TryGet(out baseAlarm);
            ModServices.TryGet(out highCommand);
            ModServices.TryGet(out theaterWar);
            ModServices.TryGet(out threatPicture);

            console = AvConsole.Build(rootRect, "STR", "STRATEGY", 3, Width, height);
            console.Tabs(
                (AvIcon.Radar2, "SITUATION"),
                (AvIcon.UsersGroup, "COMMAND"),
                (AvIcon.Flag, "OPERATIONS"));
            chips = console.Chips(3);
            metrics = console.Metrics("THEATER CONTROL", "AIR DOMINANCE", "COMMAND");
            console.PageChanged += _ => nextRefresh = 0f;

            BuildSaPage(console.Page(TabSa));
            cocPage = console.Page(TabCoc);
            BuildCmdPage(console.Page(TabCmd));
            console.Finish();

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = "STR";
            result.displayPanel = console.Root.gameObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            screenRoot = root;
            console.SetPage(TabSa);
            return result;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i].gameObject != button.gameObject) return images[i];
            }
            return button.GetComponent<Image>();
        }

        /// <summary>Same clamp the v1 kit's canvas-clamp helper performed, kept local: a bezel near a screen edge must not overhang it.</summary>
        private static void ClampPanelIntoCanvas(RectTransform panel, float margin = 8f)
        {
            if (panel == null) return;
            Canvas canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRt = canvas.rootCanvas.transform as RectTransform;
            if (canvasRt == null || panel.parent == null) return;

            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = canvasRt.InverseTransformPoint(corners[i]);
                if (local.x < minX) minX = local.x;
                if (local.x > maxX) maxX = local.x;
                if (local.y < minY) minY = local.y;
                if (local.y > maxY) maxY = local.y;
            }

            Rect bounds = canvasRt.rect;
            float dx = 0f;
            if (minX < bounds.xMin + margin) dx = bounds.xMin + margin - minX;
            else if (maxX > bounds.xMax - margin) dx = bounds.xMax - margin - maxX;
            float dy = 0f;
            if (maxY > bounds.yMax - margin) dy = bounds.yMax - margin - maxY;
            else if (minY < bounds.yMin + margin) dy = bounds.yMin + margin - minY;
            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f)) return;

            Vector3 world = canvasRt.TransformVector(new Vector3(dx, dy, 0f));
            Vector3 local2 = panel.parent.InverseTransformVector(world);
            panel.anchoredPosition += new Vector2(local2.x, local2.y);
        }

        /// <summary>Same bounded resolution the v1 kit's screen-height resolver performed, kept local.</summary>
        internal static float ResolveHeight(RectTransform parent, float min, float max)
        {
            if (max < min) max = min;
            if (parent == null) return min;
            float available = parent.rect.height;
            RectTransform cursor = parent;
            for (int i = 0; i < 4 && available <= 1f && cursor != null; i++)
            {
                cursor = cursor.parent as RectTransform;
                if (cursor != null) available = cursor.rect.height;
            }
            if (available <= 1f) return min;
            return Mathf.Clamp(Mathf.Floor(available), min, max);
        }

        // ---- Shared state mapping ----------------------------------------------------------

        /// <summary>Maps the domain's rail-category strings (TheaterReadout) onto kit v2 <see cref="AvState"/>.</summary>
        internal static AvState RailState(string rail)
        {
            switch (rail)
            {
                case "danger": return AvState.Danger;
                case "warn":
                case "caution":
                case "contested": return AvState.Caution;
                case "ready": return AvState.Ready;
                case "live":
                case "info": return AvState.Info;
                default: return AvState.Inert;
            }
        }

        /// <summary>Prepends the state glyph (R1: status is never colour alone) to a value AvRow will show.</summary>
        internal static string Glyphed(string text, AvState state) => AvStates.Glyph(state) + (text ?? "");

        // ---- SITUATION page ------------------------------------------------------------------

        private void BuildSaPage(AvFlow p)
        {
            p.Section(AvIcon.Radar2, "AIR PICTURE", "C4ISR");
            airRow = p.Add(new AvRow(p.Content));

            sortieSection = p.Section(AvIcon.Plane, "SORTIE BOARD", "FRIENDLY AI");
            for (int i = 0; i < Roles.Length; i++)
                sortieRows[i] = p.Add(new AvRow(p.Content));

            p.Section(AvIcon.Shield, "SURFACE & INFRASTRUCTURE", "ALLIED / HOSTILE");
            groundRow = p.Add(new AvRow(p.Content));
            airbaseRow = p.Add(new AvRow(p.Content));
            radarRow = p.Add(new AvRow(p.Content));
            knownAdRow = p.Add(new AvRow(p.Content));

            p.Section(AvIcon.Map2, "SECTOR CONTROL", "LIVE FIELD");
            alliedRow = p.Add(new AvRow(p.Content));
            contestedRow = p.Add(new AvRow(p.Content));
            hostileRow = p.Add(new AvRow(p.Content));
            unclaimedRow = p.Add(new AvRow(p.Content));
            frontlineRow = p.Add(new AvRow(p.Content));
            nodesRow = p.Add(new AvRow(p.Content));

            contestedSection = p.Section(AvIcon.AlertTriangle, "CONTESTED GROUND", "BY PRESSURE");
            nodeList = p.Add(new AvList(p.Content, console.Ticker, NodeListPageSize, BindNodeRow));
        }

        private void BindNodeRow(int index, AvRow row)
        {
            if (index < 0 || index >= rankedCount) { row.Set("—", "", "", AvState.Inert); return; }
            TacticalSectorGrid.TacticalNode node = ranked[index];
            bool friendly = node.Faction == SectorControl.Friendly;
            // Pressure on ground we hold is bad news; pressure on ground they hold is progress.
            AvState state = friendly ? AvState.Caution : AvState.Ready;
            bool pressing = node.CaptureProgress >= 0.05f;
            string name = string.IsNullOrEmpty(node.Name) ? "UNNAMED NODE" : node.Name.ToUpperInvariant();
            string pressureState = TheaterReadout.PressureState(node.CaptureProgress);
            string figure = pressing ? Glyphed(TheaterReadout.Percent(node.CaptureProgress), state) : "—";
            row.Set(name,
                (node.IsAirbase ? "AIRBASE" : "STRONGPOINT") + " · " +
                (friendly ? "ALLIED HELD" : "HOSTILE HELD") + " · " + pressureState,
                figure,
                RailState(TheaterReadout.NodeRail(friendly, node.IsContested)));
        }

        private void RefreshSa(TacticalTheaterState state)
        {
            if (airRow == null) return;

            AvState defconState = state.DefconLevel <= 2 ? AvState.Danger
                : state.DefconLevel == 3 ? AvState.Caution : AvState.Ready;

            bool airKnown = !float.IsNaN(state.AirSuperiorityRatio);
            AvState airState = !airKnown ? AvState.Inert
                : state.AirSuperiorityRatio >= 0.5f ? AvState.Ready : AvState.Caution;
            airRow.Set("DEFCON " + state.DefconLevel,
                state.PrimaryThreatDescription +
                (string.IsNullOrEmpty(state.ActiveThreatWarning) ? "" : " — " + state.ActiveThreatWarning) +
                " · ALLIED " + state.FriendlyAircraftCount + " / HOSTILE " + state.HostileAircraftCount,
                airKnown ? Glyphed(TheaterReadout.Percent(state.AirSuperiorityRatio), airState) : "—",
                defconState);

            SortieTally tally = state.Sorties;
            bool known = tally.Observed > 0;
            sortieSection.SetCaption(known
                ? tally.Observed + " OBSERVED · " + tally.Tasked + " TASKED"
                : "NO AI DATA");

            int observed = Mathf.Max(1, tally.Observed);
            for (int i = 0; i < Roles.Length; i++)
            {
                int count = tally.Of(Roles[i]);
                sortieRows[i].Set(
                    SortieClassifier.Code(Roles[i]) + " · " + SortieClassifier.Name(Roles[i]),
                    known ? "SHARE " + TheaterReadout.Percent(count / (float)observed) : "",
                    known ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—",
                    known ? AvState.Info : AvState.Inert);
            }

            groundRow.Set("GROUND FORCES", null,
                state.FriendlyGroundUnitsCount + " / " + state.HostileGroundUnitsCount, AvState.Info);

            bool contestedBases = state.ContestedAirbaseCount > 0;
            airbaseRow.Set("AIRBASES", "ALLIED / HOSTILE / NEUTRAL",
                Glyphed(state.FriendlyAirbaseCount + " / " + state.HostileAirbaseCount + " / " +
                    state.NeutralAirbaseCount +
                    (contestedBases ? "  (" + state.ContestedAirbaseCount + " CONTESTED)" : ""),
                    contestedBases ? AvState.Caution : AvState.Info),
                contestedBases ? AvState.Caution : AvState.Info);

            radarRow.Set("FRIENDLY RADARS ON NET", null,
                GameAccess.HqSensorsAvailable ? state.FriendlyRadarCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—",
                AvState.Info);

            RefreshFront(state);
        }

        /// <summary>The one SA line that reads Intel: this faction's known enemy air defence, at 1 Hz and
        /// only while the SITUATION page shows. Asking is what makes a client build its own picture.</summary>
        private void RefreshKnownAirDefence(FactionHQ hq)
        {
            if (knownAdRow == null || Time.unscaledTime < nextKnownAdRefresh) return;
            nextKnownAdRefresh = Time.unscaledTime + KnownAdInterval;
            if (threatPicture == null) ModServices.TryGet(out threatPicture);
            int observer = hq != null ? hq.GetInstanceID() : 0;
            bool ready = hq != null && threatPicture != null && threatPicture.IsReady(observer);
            int count = ready ? threatPicture.CopyAirDefence(observer, knownAdRings) : 0;
            knownAdRow.Set("KNOWN ENEMY AD", null,
                TheaterReadout.KnownAirDefence(ready, knownAdRings, count),
                ready ? AvState.Info : AvState.Inert);
        }

        private void RefreshFront(TacticalTheaterState state)
        {
            TheaterReadout.Shares(
                state.FriendlySectorCount, state.ContestedSectorCount,
                state.HostileSectorCount, state.NeutralSectorCount,
                out float friendly, out float contested, out float hostile);
            float unclaimed = Mathf.Clamp01(1f - friendly - contested - hostile);

            alliedRow.Set("ALLIED", TheaterReadout.Percent(friendly), state.FriendlySectorCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture), AvState.Ready);
            contestedRow.Set("CONTESTED", TheaterReadout.Percent(contested), state.ContestedSectorCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
                state.ContestedSectorCount > 0 ? AvState.Caution : AvState.Inert);
            hostileRow.Set("HOSTILE", TheaterReadout.Percent(hostile), state.HostileSectorCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture), AvState.Danger);
            unclaimedRow.Set("UNCLAIMED", TheaterReadout.Percent(unclaimed), state.NeutralSectorCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture), AvState.Inert);

            frontlineRow.Set("FRONTLINE LENGTH", null,
                state.FrontlineSegmentCount > 0 ? TheaterReadout.Kilometres(state.FrontlineLengthMetres) : "NO CONTACT",
                AvState.Info);
            nodesRow.Set("TRACKED NODES", null,
                state.TotalNodesCount + " / " + TacticalSectorGrid.MaximumNodes, AvState.Info);

            RefreshNodeList();
        }

        private void RefreshNodeList()
        {
            TacticalSectorGrid grid = overlay != null ? overlay.Grid : null;
            if (grid == null)
            {
                rankedCount = 0;
                nodeList.SetCount(0);
                contestedSection.SetCaption("SECTOR FIELD NOT RUNNING");
                return;
            }

            IReadOnlyList<TacticalSectorGrid.TacticalNode> nodes = grid.GetNodes();

            // Keep the worst NodeRankCap by insertion into a fixed window: bounded, no allocation,
            // no full sort of a list that is mostly not contested.
            int contestedTotal = 0, pressingTotal = 0, shown = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].IsContested) continue;
                contestedTotal++;
                if (nodes[i].CaptureProgress >= 0.05f) pressingTotal++;

                float pressure = nodes[i].CaptureProgress;
                int slot = shown;
                while (slot > 0 && ranked[slot - 1].CaptureProgress < pressure) slot--;
                if (slot >= NodeRankCap) continue;
                for (int j = Mathf.Min(shown, NodeRankCap - 1); j > slot; j--) ranked[j] = ranked[j - 1];
                ranked[slot] = nodes[i];
                if (shown < NodeRankCap) shown++;
            }
            rankedCount = shown;
            nodeList.SetCount(shown);

            contestedSection.SetCaption(contestedTotal == 0
                ? "NO CONTACT"
                : contestedTotal + " IN CONTACT" + (pressingTotal > 0 ? " · " + pressingTotal + " PRESSING" : ""));
        }

        // ---- Refresh -----------------------------------------------------------------------

        private void Refresh()
        {
            if (command == null || console == null) return;

            highCommand?.Refresh();
            if (theaterWar == null) ModServices.TryGet(out theaterWar);
            theaterWar?.Refresh();

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            FactionHQ hq = map != null ? map.HQ : null;
            if (hq != null) command.UpdateTelemetry(hq);

            TacticalTheaterState state = command.TheaterState;

            RefreshChrome(state);

            if (console.CurrentPage != TabCoc && highCommand != null) highCommand.Highlight(-1);

            switch (console.CurrentPage)
            {
                case TabSa:
                    RefreshSa(state);
                    RefreshKnownAirDefence(hq);
                    break;
                case TabCoc:
                    if (!cocBuilt)
                    {
                        BuildCocPage(cocPage);
                        cocBuilt = true;
                    }
                    RefreshCoc();
                    break;
                case TabCmd: RefreshCmd(); break;
            }

            string alarm = baseAlarm != null ? baseAlarm.ActiveAlertTicker : null;
            string prompt = MapPicker.Prompt;
            string status = !string.IsNullOrEmpty(alarm) ? alarm
                : !string.IsNullOrEmpty(prompt) ? prompt : Ambient(state);
            AvState footerState = state.DefconLevel <= 2 ? AvState.Danger
                : state.DefconLevel == 3 ? AvState.Caution : AvState.Inert;
            console.Footer.Set(status, footerState);

            // Row counts, wrapped copy and hidden rows change what each part measures; the flow
            // only re-measures when asked, so ask once per refresh (4 Hz) for the page in view.
            console.Page(console.CurrentPage).Relayout();
        }

        private void RefreshChrome(TacticalTheaterState state)
        {
            AvState alertState = state.DefconLevel <= 2 ? AvState.Danger
                : state.DefconLevel == 3 ? AvState.Caution : AvState.Ready;
            chips[0].Set("DEFCON " + state.DefconLevel, alertState);

            bool frontline = state.ContestedSectorCount > 0;
            chips[1].Set(frontline ? "FRONT LIVE" : "FRONT QUIET", frontline ? AvState.Caution : AvState.Ready);

            bool grid = overlay != null && overlay.HasControlData;
            chips[2].Set(grid ? "GRID LIVE" : "GRID —", grid ? AvState.Info : AvState.Inert);

            bool territoryKnown = !float.IsNaN(state.TerritoryControlRatio);
            metrics[0].Set(
                territoryKnown ? TheaterReadout.Percent(state.TerritoryControlRatio) : "—",
                state.FriendlySectorCount + "/" + state.HostileSectorCount,
                territoryKnown ? state.TerritoryControlRatio : 0f,
                !territoryKnown ? AvState.Inert
                    : state.TerritoryControlRatio >= 0.5f ? AvState.Ready : AvState.Caution);

            bool airKnown = !float.IsNaN(state.AirSuperiorityRatio);
            metrics[1].Set(
                airKnown ? TheaterReadout.Percent(state.AirSuperiorityRatio) : "—",
                state.FriendlyAircraftCount + "/" + state.HostileAircraftCount,
                airKnown ? state.AirSuperiorityRatio : 0f,
                !airKnown ? AvState.Inert
                    : state.AirSuperiorityRatio >= 0.5f ? AvState.Ready : AvState.Caution);

            if (metrics.Length > 2)
            {
                bool staff = highCommand != null && highCommand.Available;
                float cohesion = staff ? Mathf.Clamp01(highCommand.FriendlyCohesion) : 0f;
                metrics[2].Set(
                    staff ? TheaterReadout.Percent(cohesion) : "—",
                    staff
                        ? highCommand.FriendlyActive + " ACTIVE" +
                          (highCommand.FriendlyKia > 0 ? " · " + highCommand.FriendlyKia + " KIA" : "")
                        : "NO STAFF",
                    cohesion,
                    !staff ? AvState.Inert
                    : cohesion >= 0.6f ? AvState.Ready
                    : cohesion >= 0.3f ? AvState.Caution
                    : AvState.Danger);
            }
        }

        private string Ambient(TacticalTheaterState state)
        {
            string text = state.ContestedSectorCount > 0
                ? state.ContestedSectorCount + " contested sector" +
                  (state.ContestedSectorCount == 1 ? "" : "s") + " · frontline " +
                  TheaterReadout.Kilometres(state.FrontlineLengthMetres)
                : "No contested ground";

            if (highCommand != null && highCommand.Available)
            {
                text += " · command " + TheaterReadout.Percent(Mathf.Clamp01(highCommand.FriendlyCohesion));
                if (console != null && console.CurrentPage == TabCoc && !string.IsNullOrEmpty(highCommand.Signal))
                    text += " · " + highCommand.Signal;
            }
            else if (console != null && console.CurrentPage == TabCoc)
                text += " · " + (highCommand == null
                    ? "chain of command is not running on this host"
                    : highCommand.Status ?? "chain of command is forming");

            if (console != null && console.CurrentPage == TabCmd)
            {
                TheaterLiveOperationView active = theaterWar?.ActiveOperation;
                text += " · " + (theaterWar == null || !theaterWar.Available
                    ? "theater staff unavailable"
                    : active == null ? "staff observing fronts" : active.Label + " / " + active.Phase);
            }
            return text;
        }
    }
}
