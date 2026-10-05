using NOAvionics;
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using BoscaliSummer.Modules.Command.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
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

        /// <summary>Row pool of the contested list; how many show at once depends on the height the page gives it.</summary>
        private const int NodeListRows = 12;

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
        private AvMetric[] metrics;
        private AvFlow cocPage;
        private bool cocBuilt;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;

        // ---- SITUATION page ----------------------------------------------------------------

        private StrThreatBanner threat;
        private AvGauge ringAllied, ringContested, ringHostile, ringOpen;
        private AvRow frontReadout;
        private StrAtoBoard atoBoard;
        private StrNote atoNote;
        private StrTile groundTile, airbaseTile, radarTile, adTile;
        private float nextKnownAdRefresh;
        private readonly AirDefenceRing[] knownAdRings = new AirDefenceRing[ThreatPictureLimits.MaximumRings];

        private AvSection contestedSection;
        private StrNodeBoard nodeList;
        private StrNote nodeNote;
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
            metrics = null;
            baseAlarm = null;
            highCommand = null;
            cocPage = null;
            cocBuilt = false;
            theaterWar = null;
            threatPicture = null;

            threat = null;
            ringAllied = ringContested = ringHostile = ringOpen = null;
            frontReadout = null;
            atoBoard = null;
            atoNote = null;
            groundTile = airbaseTile = radarTile = adTile = null;
            nextKnownAdRefresh = 0f;

            contestedSection = null;
            nodeList = null;
            nodeNote = null;
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
            float height = AvLay.ResolveHeight(
                templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            AvLay.ClampIntoCanvas(rootRect);

            ModuleServices.TryGet(out baseAlarm);
            ModuleServices.TryGet(out highCommand);
            ModuleServices.TryGet(out theaterWar);
            ModuleServices.TryGet(out threatPicture);

            console = AvConsole.Build(rootRect, "STR", "STRATEGY", 3, Width, height);
            AvTabBar tabBar = console.Tabs(
                (AvIcon.Radar2, "SITUATION"),
                (AvIcon.UsersGroup, "COMMAND"),
                (AvIcon.Flag, "OPERATIONS"));
            TabHelp.Apply(tabBar,
                "Air picture, sortie board and sector control.",
                "Chain of command: posts, personnel files and the staff log.",
                "Theater operations: offensives, main effort and reinforcement calls.");
            // One row of four tiles carries what the old chip strip and tile row said twice.
            metrics = console.Metrics("SECTORS", "AIR", "STAFF", "FRONT");
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
            threat = p.Add(new StrThreatBanner(p.Content));

            // Four ownership strips, followed by named force and contested-node records.
            ringAllied = new AvGauge(p.Content, "ALLIED", AvGaugeShape.Ring, 64f);
            ringContested = new AvGauge(p.Content, "CONTESTED", AvGaugeShape.Ring, 64f);
            ringHostile = new AvGauge(p.Content, "HOSTILE", AvGaugeShape.Ring, 64f);
            ringOpen = new AvGauge(p.Content, "UNCLAIMED", AvGaugeShape.Ring, 64f);
            ringAllied.Help = "ALLIED: share of the sector field held by your side.";
            ringContested.Help = "CONTESTED: share of sectors with both sides present.";
            ringHostile.Help = "HOSTILE: share of sectors held by the enemy. Red above half.";
            ringOpen.Help = "UNCLAIMED: share of sectors nobody controls yet.";
            p.Row(ringAllied, ringContested, ringHostile, ringOpen);

            atoBoard = p.Add(new StrAtoBoard(p.Content, Roles.Length));
            atoNote = p.Add(new StrNote(p.Content, AvIcon.Plane));

            groundTile = new StrTile(p.Content, AvIcon.Shield, "GROUND");
            airbaseTile = new StrTile(p.Content, AvIcon.Plane, "AIRBASES");
            radarTile = new StrTile(p.Content, AvIcon.Antenna, "RADARS");
            adTile = new StrTile(p.Content, AvIcon.Target, "KNOWN AD");
            groundTile.Help = "Ground forces on the theater, allied / hostile.";
            airbaseTile.Help = "Airbases by owner, allied / hostile / neutral. Contested bases are being captured.";
            radarTile.Help = "Friendly radars and emitters sharing the network.";
            adTile.Help = "Enemy air-defence sites this faction knows about.";
            p.Row(groundTile, airbaseTile, radarTile, adTile);

            contestedSection = p.Section(AvIcon.MapPin, "CONTESTED GROUND", "BY PRESSURE");
            frontReadout = p.Add(new AvRow(p.Content));
            frontReadout.Help = "Length and segment count of the observed ground-contact front. Node percentages below are opposing pressure ratios, not vanilla capture completion.";
            nodeList = p.Add(new StrNodeBoard(p.Content, console.Ticker, NodeListRows, BindNodeRow, 62f), 1f);
            nodeNote = p.Add(new StrNote(p.Content, AvIcon.MapPin), 1f);
        }

        private void BindNodeRow(int index, AvRow row)
        {
            if (index < 0 || index >= rankedCount) { row.Set("—", "", "", AvState.Inert); row.Help = null; return; }
            TacticalSectorGrid.TacticalNode node = ranked[index];
            bool friendly = node.Faction == SectorControl.Friendly;
            bool hostile = node.Faction == SectorControl.Hostile;
            AvState state = friendly ? AvState.Info : hostile ? AvState.Caution : AvState.Info;
            string owner = friendly ? "ALLIED HELD" : hostile ? "HOSTILE HELD" : "UNCLAIMED";
            string name = string.IsNullOrEmpty(node.Name) ? "UNNAMED NODE" : node.Name.ToUpperInvariant();
            string pressureState = TheaterReadout.PressureState(node.CaptureProgress);
            string figure = pressureState == "UNKNOWN" ? "\u2014" : TheaterReadout.Percent(node.CaptureProgress);
            row.Set(name, (node.IsAirbase ? "AIRBASE" : "STRONGPOINT") + " / " + owner +
                " / " + pressureState, "PRESSURE " + figure, state);
            row.Help = name + " / " + owner + ". Opposing pressure ratio, not vanilla capture completion. Ranked highest pressure first.";
        }

        private static string AtoCode(SortieRole role) =>
            role == SortieRole.Sead ? "SEAD" : SortieClassifier.Code(role);

        private static string AtoTask(SortieRole role)
        {
            switch (role)
            {
                case SortieRole.Cap: return "COMBAT AIR PATROL";
                case SortieRole.Sead: return "AIR DEFENCE SUPPRESSION";
                case SortieRole.Cas: return "CLOSE AIR SUPPORT";
                case SortieRole.Strike: return "STRIKE";
                default: return "TRANSIT";
            }
        }

        private void RefreshSa(TacticalTheaterState state)
        {
            if (threat == null) return;

            bool airKnown = !float.IsNaN(state.AirSuperiorityRatio);
            string assessment = state.PrimaryThreatDescription +
                (string.IsNullOrEmpty(state.ActiveThreatWarning) ? "" : " — " + state.ActiveThreatWarning);
            threat.Set(state.DefconLevel, assessment,
                "AIRCRAFT · ALLIED " + state.FriendlyAircraftCount + " · HOSTILE " + state.HostileAircraftCount +
                (airKnown ? " · AIR DOMINANCE " + TheaterReadout.Percent(state.AirSuperiorityRatio) : ""));

            RefreshFront(state);

            SortieTally tally = state.Sorties;
            bool known = tally.Observed > 0;
            atoBoard.SetCaption(known ? tally.Observed + " OBSERVED · " + tally.Tasked + " TASKED" : "");
            atoBoard.SetShown(known);
            atoNote.SetShown(!known);
            if (known)
            {
                int observed = Mathf.Max(1, tally.Observed), hot = -1, best = 0;
                for (int i = 0; i < Roles.Length; i++)
                {
                    int count = tally.Of(Roles[i]);
                    if (count > best) { best = count; hot = i; }
                    atoBoard.Set(i, AtoCode(Roles[i]), AtoTask(Roles[i]), count, count / (float)observed);
                }
                atoBoard.SetHot(hot);
            }
            else atoNote.Set("NO AI SORTIES", "");

            int friendlyGround = state.FriendlyGroundUnitsCount, hostileGround = state.HostileGroundUnitsCount;
            groundTile.Set(friendlyGround + "/" + hostileGround, "ALLIED / HOSTILE", AvState.Info,
                friendlyGround + hostileGround > 0 ? friendlyGround / (float)(friendlyGround + hostileGround) : -1f);

            bool contestedBases = state.ContestedAirbaseCount > 0;
            int bases = state.FriendlyAirbaseCount + state.HostileAirbaseCount + state.NeutralAirbaseCount;
            airbaseTile.Set(
                state.FriendlyAirbaseCount + "/" + state.HostileAirbaseCount + "/" + state.NeutralAirbaseCount,
                contestedBases ? state.ContestedAirbaseCount + " CONTESTED" : "ALLIED / HOSTILE / NEUTRAL",
                contestedBases ? AvState.Caution : AvState.Info,
                bases > 0 ? state.FriendlyAirbaseCount / (float)bases : -1f);

            bool radars = GameAccess.HqSensorsAvailable;
            radarTile.Set(radars ? state.FriendlyRadarCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—",
                radars ? "FRIENDLY" : "NO SENSOR NET",
                radars ? AvState.Info : AvState.Inert);
        }

        /// <summary>The one SA tile that reads Intel: this faction's known enemy air defence, at 1 Hz and
        /// only while the SITUATION page shows. Asking is what makes a client build its own picture.</summary>
        private void RefreshKnownAirDefence(FactionHQ hq)
        {
            if (adTile == null || Time.unscaledTime < nextKnownAdRefresh) return;
            nextKnownAdRefresh = Time.unscaledTime + KnownAdInterval;
            if (threatPicture == null) ModuleServices.TryGet(out threatPicture);
            int observer = hq != null ? hq.GetInstanceID() : 0;
            bool ready = hq != null && threatPicture != null && threatPicture.IsReady(observer);
            int count = ready ? threatPicture.CopyAirDefence(observer, knownAdRings) : 0;
            string line = TheaterReadout.KnownAirDefence(ready, knownAdRings, count);
            if (!ready) adTile.Set("—", "NO PICTURE", AvState.Inert);
            else if (line == "NONE KNOWN") adTile.Set("NONE", "", AvState.Info);
            else
            {
                // "57 SITES · 11 RADAR (45 PRE-WAR, 1 STALE)": the count is the figure, the rest is the caption.
                int split = line.IndexOf(" · ", StringComparison.Ordinal);
                string head = split < 0 ? line : line.Substring(0, split);
                string rest = split < 0 ? "" : line.Substring(split + 3);
                int space = head.IndexOf(' ');
                string figure = space > 0 ? head.Substring(0, space) : head;
                string unit = space > 0 ? head.Substring(space + 1) : "";
                adTile.Set(figure, unit + (rest.Length > 0 && unit.Length > 0 ? " · " : "") + rest, AvState.Info);
            }
        }

        private void RefreshFront(TacticalTheaterState state)
        {
            TheaterReadout.Shares(
                state.FriendlySectorCount, state.ContestedSectorCount,
                state.HostileSectorCount, state.NeutralSectorCount,
                out float friendly, out float contested, out float hostile);
            float unclaimed = Mathf.Clamp01(1f - friendly - contested - hostile);
            int sectors = state.FriendlySectorCount + state.ContestedSectorCount +
                          state.HostileSectorCount + state.NeutralSectorCount;

            bool field = sectors > 0;
            ringAllied.Set(friendly, field ? TheaterReadout.Percent(friendly) : "—", field ? AvState.Ready : AvState.Inert);
            ringContested.Set(contested, field ? TheaterReadout.Percent(contested) : "—",
                field && state.ContestedSectorCount > 0 ? AvState.Caution : AvState.Inert);
            ringHostile.Set(hostile, field ? TheaterReadout.Percent(hostile) : "—", !field ? AvState.Inert : hostile > 0.5f ? AvState.Danger : AvState.Info);
            ringOpen.Set(unclaimed, field ? TheaterReadout.Percent(unclaimed) : "—", AvState.Inert);
            ringAllied.Help = "ALLIED: " + state.FriendlySectorCount + " of " + sectors + " sectors held by your side.";
            ringContested.Help = "CONTESTED: " + state.ContestedSectorCount + " of " + sectors + " sectors with both sides present.";
            ringHostile.Help = "HOSTILE: " + state.HostileSectorCount + " of " + sectors + " sectors held by the enemy. Red above half.";
            ringOpen.Help = "UNCLAIMED: " + state.NeutralSectorCount + " of " + sectors + " sectors nobody controls yet.";
            bool frontKnown = overlay != null && overlay.HasControlData;
            frontReadout.Set("FRONT CONTACT", frontKnown ? state.FrontlineSegmentCount + " SEGMENTS / " +
                state.ContestedSectorCount + " SECTORS IN CONTACT" : "NO CONTROL FIELD",
                frontKnown ? TheaterReadout.Kilometres(state.FrontlineLengthMetres) : "\u2014",
                !frontKnown ? AvState.Inert : state.FrontlineSegmentCount > 0 ? AvState.Caution : AvState.Info);

            RefreshNodeList();
        }

        private void RefreshNodeList()
        {
            TacticalSectorGrid grid = overlay != null ? overlay.Grid : null;
            if (grid == null)
            {
                rankedCount = 0;
                nodeList.SetCount(0);
                nodeList.SetShown(false);
                nodeNote.SetShown(true);
                nodeNote.Set("NO SECTOR FIELD", "");
                frontReadout.Set("FRONT CONTACT", "NO SECTOR FIELD", "\u2014", AvState.Inert);
                contestedSection.SetCaption("NO FIELD");
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

            nodeList.SetShown(shown > 0);
            nodeNote.SetShown(shown == 0);
            if (shown == 0) nodeNote.Set("NO CONTACT", "");
            contestedSection.SetCaption(contestedTotal == 0
                ? "NO CONTACT"
                : contestedTotal + " IN CONTACT" + (pressingTotal > 0 ? " · " + pressingTotal + " PRESSING" : ""));
        }

        // ---- Refresh -----------------------------------------------------------------------

        private void Refresh()
        {
            if (command == null || console == null) return;

            highCommand?.Refresh();
            if (theaterWar == null) ModuleServices.TryGet(out theaterWar);
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
        }

        private void RefreshChrome(TacticalTheaterState state)
        {
            if (metrics == null || metrics.Length < 3) return;
            int sectors = state.FriendlySectorCount + state.ContestedSectorCount +
                          state.HostileSectorCount + state.NeutralSectorCount;

            bool territoryKnown = !float.IsNaN(state.TerritoryControlRatio);
            bool ahead = territoryKnown && state.TerritoryControlRatio >= 0.5f;
            metrics[0].Set(
                territoryKnown ? TheaterReadout.Percent(state.TerritoryControlRatio) : "—",
                !territoryKnown ? "" : ahead ? "LEAD" : "LAG",
                territoryKnown ? state.TerritoryControlRatio : 0f,
                !territoryKnown ? AvState.Inert : ahead ? AvState.Ready : AvState.Caution);
            StrTips.Metric(metrics[0], !territoryKnown
                ? "SECTORS: no sector field yet, so no control figure."
                : "SECTORS: your share of the sector field. " + state.FriendlySectorCount + " held, " +
                  state.HostileSectorCount + " hostile, " + state.ContestedSectorCount + " contested of " + sectors +
                  ". Above 50% you are ahead.");

            bool airKnown = !float.IsNaN(state.AirSuperiorityRatio);
            metrics[1].Set(
                airKnown ? TheaterReadout.Percent(state.AirSuperiorityRatio) : "—",
                state.FriendlyAircraftCount + "/" + state.HostileAircraftCount,
                airKnown ? state.AirSuperiorityRatio : 0f,
                !airKnown ? AvState.Inert
                    : state.AirSuperiorityRatio >= 0.5f ? AvState.Ready : AvState.Caution);
            StrTips.Metric(metrics[1], !airKnown
                ? "AIR: no aircraft observed, so no dominance figure."
                : "AIR: air dominance, " + state.FriendlyAircraftCount + " friendly against " +
                  state.HostileAircraftCount + " hostile aircraft in the picture. Above 50% favours you.");

            bool staff = highCommand != null && highCommand.Available;
            float cohesion = staff ? Mathf.Clamp01(highCommand.FriendlyCohesion) : 0f;
            metrics[2].Set(
                staff ? TheaterReadout.Percent(cohesion) : "—",
                staff ? highCommand.FriendlyActive + " ACT" : "NONE",
                cohesion,
                !staff ? AvState.Inert
                : cohesion >= 0.6f ? AvState.Ready
                : cohesion >= 0.3f ? AvState.Caution
                : AvState.Danger);
            StrTips.Metric(metrics[2], !staff
                ? "STAFF: no chain of command is running on this host."
                : "STAFF: how effective your chain of command is. " + highCommand.FriendlyActive + " posts active" +
                  (highCommand.FriendlyKia > 0 ? ", " + highCommand.FriendlyKia + " lost" : "") +
                  ". Each living commander earns the faction a bonus.");

            if (metrics.Length > 3)
            {
                bool grid = overlay != null && overlay.HasControlData;
                bool contact = state.ContestedSectorCount > 0;
                float share = sectors > 0 ? state.ContestedSectorCount / (float)sectors : 0f;
                metrics[3].Set(
                    !grid ? "—" : state.FrontlineSegmentCount > 0
                        ? AvNum.Fixed(state.FrontlineLengthMetres / 1000f, 0) : "0",
                    grid ? "KM" : "NO GRID",
                    Mathf.Clamp01(share * 4f),
                    !grid ? AvState.Inert : contact ? AvState.Caution : AvState.Ready);
                StrTips.Metric(metrics[3], !grid
                    ? "FRONT: the sector grid has no data yet, so no frontline is drawn."
                    : "FRONT: frontline length in kilometres; the bar shows the share of sectors in contact (" +
                      state.ContestedSectorCount + " of " + sectors + "). Amber while any sector is contested.");
            }
        }

        private string Ambient(TacticalTheaterState state)
        {
            string text = overlay == null || !overlay.HasControlData ? "sector field unavailable"
                : state.ContestedSectorCount > 0
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
                    : !theaterWar.HasSnapshot ? "waiting for staff report"
                    : theaterWar.SnapshotAgeSeconds > 15f ? "staff report stale · awaiting host refresh"
                    : active == null ? "staff observing fronts" : active.Label + " / " + active.Phase);
            }
            return text;
        }
    }
}
