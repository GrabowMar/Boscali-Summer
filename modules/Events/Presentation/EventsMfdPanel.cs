using NOAvionics;
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Events.Configuration;
using BoscaliSummer.Modules.Events.Domain;
using BoscaliSummer.Modules.Events.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// "EVN" — active dispatch, response desk, archive and field documentation. Kit v2 console: chrome
    /// (metrics/tabs/footer) from <see cref="AvConsole"/>, page content from <see cref="AvFlow"/>
    /// sections, rows and the module's own <see cref="EventActiveCardPart"/> / <see cref="DecisionBoardPart"/>
    /// / <see cref="EventCaseFilePart"/> parts.
    /// </summary>
    internal sealed partial class EventsMfdPanel : MonoBehaviour, ISceneService
    {
        private const float RefreshInterval = 0.25f;
        private const int TabDispatch = 0;
        private const int TabDesk = 1;

        /// <summary>
        /// The scripted-beat rows the active card and the full-screen broadcast both reserve.
        /// One cap for both, so a four-beat entry cannot silently drop its last beat.
        /// </summary>
        internal const int MaximumEventSteps = 4;

        /// <summary>The clock starts saying ENDING this many seconds out.</summary>
        private const float EndingSeconds = 60f;

        /// <summary>Below this the clock and its rail go danger, not caution.</summary>
        private const float CriticalSeconds = 20f;

        private EventsSettings settings;
        private EventsManager events;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvConsole console;
        private AvMetric[] metrics;
        private AvHelpTip costTip, resetTip, leftTip, logTip;

        private EventHeroPart hero;
        private AvSection responseSection;
        private ResponseDeskPart responseDesk;
        private AvGauge directorPosture, directorBases, directorSupers;
        private AvEqualizer historyEq;
        private float[] logBars;
        private EventTimelinePart timeline;
        private int historyCapacity;

        private const int RecentRecords = 3;
        private const int VisibleLogRows = 5;
        private ArchiveTilePart[] tiles;
        private EventCaseFilePart deskCase;
        private AvButtons deskCaseAction;
        private int deskCaseCatalog = -1;
        private AvSection recentSection;
        private AvRow[] recentRows;
        private readonly int[] recentCatalog = new int[RecentRecords];
        private EventDeskArchive archive;

        private string boundId;
        private float boundStart;
        private float boundMultiplier = float.NaN;
        private float boundCooldown = float.NaN;
        private bool boundAimed;
        private bool boundResolved;
        private float nextAttempt;
        private float nextRefresh;
        private bool failed;

        public void Configure(EventsSettings config, EventsManager manager, ManualLogSource log)
        {
            settings = config;
            events = manager;
            logger = log;
        }

        public void ResetForScene()
        {
            MfdScreenHost.Release(MfdSlots.Events);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);

            screenRoot = null;
            screen = null;
            console = null;
            metrics = null;
            costTip = resetTip = leftTip = logTip = null;
            hero = null;
            responseSection = null;
            responseDesk = null;
            directorPosture = directorBases = directorSupers = null;
            historyEq = null;
            logBars = null;
            timeline = null;
            historyCapacity = 0;
            tiles = null;
            deskCase = null;
            deskCaseAction = null;
            deskCaseCatalog = -1;
            recentSection = null;
            recentRows = null;
            archive?.Close();
            archive = null;
            boundId = null;
            boundStart = 0f;
            boundMultiplier = float.NaN;
            boundCooldown = float.NaN;
            boundAimed = false;
            boundResolved = false;
            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || events == null || settings == null) return;
            if (!settings.Enabled.Value)
            {
                // Disabling the director mid-scene releases the hosted slot instead of
                // leaving a frozen screen on the bezel.
                if (screen != null) ResetForScene();
                return;
            }
            if (Application.isBatchMode) { failed = true; return; }
            if (!GameAccess.MfdAvailable) { failed = true; return; }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            bool visible = screen.isActive &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            if (archive != null && (!archive.IsOpen || !visible || console.CurrentPage != TabDesk))
            {
                archive.Close();
                archive = null;
            }
            if (!visible) return;

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
        }

        // ---- Installation ----------------------------------------------------------------

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdScreenHost.TryHost(MfdSlots.Events, preferLeft: false, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    // The six vanilla slots are for WMC and the claimed screens; this screen
                    // owns an appended one, so the only failure here is a missing adapter.
                    failed = true;
                    logger?.LogWarning("EVN MFD unavailable: could not add a host button.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdScreenHost.Release(MfdSlots.Events);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdScreenHost.Release(MfdSlots.Events);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    ResetForScene();
                    failed = true;
                    logger?.LogWarning("EVN MFD unavailable: bezel changed during installation.");
                    return;
                }
                logger?.LogInfo("EVN MFD installed on " + (left ? "left" : "right") +
                                " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                ResetForScene();
                failed = true;
                logger?.LogError("EVN MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            var root = new GameObject("BoscaliEvents.Screen", typeof(RectTransform));
            screenRoot = root;
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            float height = AvLay.ResolveHeight(templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
            // Clamp the screen root before the console exists. Clamping console.Root afterwards baked the
            // canvas correction into the child as a negative offset and shifted the whole page left.
            AvLay.ClampIntoCanvas(rootRect);

            console = AvConsole.Build(rootRect, MfdSlots.Events, "EVENT DIRECTORATE", 2, AvTokens.PanelWidth, height);
            // Four compact tiles carry what the three header chips and two wide tiles used to say twice.
            metrics = console.Metrics("COST", "RESET", "LEFT", "LOG");
            costTip = HelpOn(metrics[0], "COST: the support price multiplier the running event sets on requisitions.");
            resetTip = HelpOn(metrics[1], "RESET: how fast your support requests come back off cooldown.");
            leftTip = HelpOn(metrics[2], "LEFT: time until the running event ends.");
            logTip = HelpOn(metrics[3], "LOG: dispatches finished so far, out of the log capacity.");
            console.Tabs((AvIcon.AlertTriangle, "DISPATCH"), (AvIcon.Bookmark, "DESK"));

            historyCapacity = Mathf.Max(1, settings.HistoryLength.Value);
            BuildDispatchPage(console.Page(TabDispatch));
            BuildDeskPage(console.Page(TabDesk));
            console.Finish();

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Events;
            result.displayPanel = console.Root.gameObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

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

        // ---- Pages -------------------------------------------------------------------------

        private void BuildDispatchPage(AvFlow p)
        {
            // The hero soaks up spare height only while the theater is quiet (a designed standby banner).
            hero = p.Add(new EventHeroPart(p.Content), 1f);
            hero.BindCalm("STANDBY");

            responseSection = p.Section(AvIcon.Scale, "RESPONSE", "AWAITING");
            responseDesk = p.Add(new ResponseDeskPart(p.Content, events.RequestResponse));
            responseSection.SetShown(false);
            responseDesk.SetShown(false);

            // The director's three rings need no section header: each ring names itself and carries its own tip.
            directorPosture = new AvGauge(p.Content, "POSTURE", AvGaugeShape.Ring, 64f);
            directorBases = new AvGauge(p.Content, "BASES", AvGaugeShape.Ring, 64f);
            directorSupers = new AvGauge(p.Content, "SUPERS", AvGaugeShape.Ring, 64f);
            directorPosture.Help = "POSTURE: how close the losing side is to the base deficit that arms a superevent. ARMED means the director may escalate at its next roll; MON means it is only watching.";
            directorBases.Help = "BASES: ground airbases held by the leading side against the losing side (leader:loser). The ring fills with the loser's share.";
            directorSupers.Help = "SUPERS: superevents fired this mission out of the most the director allows. Each superevent fires once.";
            p.Row(directorPosture, directorBases, directorSupers);
            directorPosture.Set(0f, "—", AvState.Inert);
            directorBases.Set(0f, "—", AvState.Inert);
            directorSupers.Set(0f, "—", AvState.Inert);

            // Finished events as bars (tall = superevent, short = minor), newest first; it grows into spare height.
            logBars = new float[Mathf.Max(1, historyCapacity)];
            historyEq = p.Add(new AvEqualizer(p.Content, "EVENT LOG", 36f), 1f);
            historyEq.Help = "EVENT LOG: finished events as bars, newest at the left. A tall bar is a superevent, a middle bar a medium, a short bar a minor. Hover a row below for its tier, target and age.";
            historyEq.SetShown(false);
            timeline = p.Add(new EventTimelinePart(p.Content));
            timeline.SetNote("NO DISPATCHES LOGGED");
        }

        private void BuildDeskPage(AvFlow p)
        {
            // One row of four archive doors (names match the archive's own tabs); the case file below grows.
            tiles = new ArchiveTilePart[4];
            string[] labels = { "AIRCRAFT", "EVENTS", "WORLD", "MANUAL" };
            string[] notes =
            {
                "AIRCRAFT: every native aircraft with a rotating model viewer and its performance figures.",
                "EVENTS: a dossier for every authored theater event, with its price effect, window and timed orders.",
                "WORLD: short files on life behind the front line.",
                "MANUAL: how to read a dispatch and issue orders. Start here if the desk is new to you.",
            };
            AvIcon[] icons = { AvIcon.Plane, AvIcon.AlertTriangle, AvIcon.Map2, AvIcon.Bookmark };
            for (int i = 0; i < tiles.Length; i++)
            {
                int section = i;
                tiles[i] = new ArchiveTilePart(p.Content, icons[i], labels[i], () => OpenArchive(section));
                tiles[i].Help = notes[i] + " Opens the field archive.";
            }
            p.Row(tiles[0], tiles[1], tiles[2], tiles[3]);

            deskCase = p.Add(new EventCaseFilePart(p.Content), 1f);
            deskCase.Help = "CASE FILE: the running event, or the last one filed: poster, tier, target, price effect and time left. Older dossiers are listed under RECENT.";
            deskCase.Bind("AWAITING REPORT", "NO CASE FILED", "", AvIcon.Radar2, null, AvState.Inert);
            deskCaseAction = p.Buttons(new AvControl.Spec("OPEN CASE DOSSIER", () =>
            {
                if (deskCaseCatalog >= 0) OpenArchive(1, deskCaseCatalog);
            }, AvButtonStyle.Quiet, AvIcon.Bookmark));
            deskCaseAction.Controls[0].Help = "Read this event's catalog dossier, including its authored effects and timed orders.";
            deskCaseAction.SetShown(false);

            recentSection = p.Section(AvIcon.ListDetails, "RECENT", "OPEN");
            recentRows = new AvRow[RecentRecords];
            for (int i = 0; i < recentRows.Length; i++)
            {
                int slot = i;
                recentRows[i] = p.Add(new AvRow(p.Content, () =>
                {
                    if (recentCatalog[slot] >= 0) OpenArchive(1, recentCatalog[slot]);
                }));
                recentRows[i].Help = "Open this dossier in the field archive.";
            }
            recentSection.SetShown(false);
            foreach (AvRow row in recentRows) row.SetShown(false);
        }

        private void OpenArchive(int section, int record = 0)
        {
            if (archive == null) archive = EventDeskArchive.Create();
            archive.Show(section, record);
        }

        private static int CatalogIndexOf(string id)
        {
            EventDefinition[] all = EventCatalog.All;
            for (int i = 0; i < all.Length; i++)
                if (all[i].Id == id) return i;
            return -1;
        }

        // ---- Refresh ---------------------------------------------------------------------

        private void Refresh()
        {
            if (console == null || events == null) return;

            ActiveEventView current = events.Current;
            IReadOnlyList<ActiveEventView> history = events.History;
            float now = MissionTime();

            float multiplier = current != null ? events.SupportCostMultiplier : 1f;
            float cooldown = current != null ? events.LocalSupportCooldownMultiplier : 1f;
            string summary = current != null ? EventSelector.EffectSummary(multiplier) : null;
            bool aimedAtLocal = current != null && (events.LocalTargeted || current.Target == "ALL THEATER");

            RefreshDirector();

            bool historyOff = settings.HistoryLength.Value <= 0;
            float secondsLeft = current != null ? current.EndsAtMissionTime - now : 0f;
            bool ending = current != null && secondsLeft <= EndingSeconds;
            bool critical = current != null && secondsLeft <= CriticalSeconds;
            AvState effectState = EffectState(summary, aimedAtLocal);

            string sideNote = current == null ? "" : aimedAtLocal ? "YOUR SIDE" : "OTHER SIDE";
            string costCaption = current == null ? "NO ACTIVE EVENT" : EffectText(current, summary, aimedAtLocal);
            metrics[0].Set(
                current != null ? MultiplierLabel(multiplier) : "—",
                current == null ? "" : aimedAtLocal ? "YOU" : "THEM",
                current != null && aimedAtLocal ? Mathf.Clamp01(Mathf.Abs(multiplier - 1f)) : 0f,
                effectState);
            costTip.Text = "COST: the support price multiplier the running event sets on requisitions. " +
                (current == null ? "No event is running, so prices are normal."
                    : sideNote + ": " + costCaption + ". Dearer shows amber or red, cheaper green; it holds until the event ends.");

            string resetCaption = current == null ? "NO ACTIVE EVENT" :
                !aimedAtLocal ? "NO EFFECT ON YOU" :
                cooldown > 1.001f ? "LONGER COOLDOWN" :
                cooldown < 0.999f ? "SHORTER COOLDOWN" : "NORMAL RESET";
            AvState resetState = !aimedAtLocal ? AvState.Inert :
                cooldown > 1.001f ? AvState.Caution : cooldown < 0.999f ? AvState.Ready : AvState.Inert;
            metrics[1].Set(
                current != null ? MultiplierLabel(cooldown) : "—",
                current == null || !aimedAtLocal ? "" : cooldown > 1.001f ? "SLOW" : cooldown < 0.999f ? "FAST" : "",
                current != null && aimedAtLocal ? Mathf.Clamp01(Mathf.Abs(cooldown - 1f)) : 0f,
                resetState);
            resetTip.Text = "RESET: how fast your support requests come back off cooldown. x1.00 is normal, above 1 is slower, below 1 is faster. " +
                (current == null ? "No event is running." : resetCaption + ".");

            float span = current != null ? Mathf.Max(1f, current.EndsAtMissionTime - current.StartedAtMissionTime) : 1f;
            float remaining = current != null ? Mathf.Clamp01(secondsLeft / span) : 0f;
            // The bar is time, not severity: neutral until the event is ending or it is costing this side.
            AvState leftState = current == null ? AvState.Inert
                : critical ? AvState.Danger
                : ending || effectState == AvState.Danger ? AvState.Caution
                : effectState == AvState.Ready ? AvState.Ready : AvState.Info;
            metrics[2].Set(current != null ? AvNum.Clock(secondsLeft) : "—", critical ? "CRIT" : ending ? "END" : "", remaining, leftState);
            leftTip.Text = "LEFT: time until the running event ends; the bar is how much of its window remains. Amber under a minute, red under twenty seconds." +
                (current == null ? " No event is running." : "");

            metrics[3].Set(
                historyOff ? "OFF" : AvNum.Fixed(history.Count, 0) + "/" + AvNum.Fixed(historyCapacity, 0), "",
                historyOff || historyCapacity <= 0 ? 0f : Mathf.Clamp01(history.Count / (float)historyCapacity),
                !historyOff && history.Count > 0 ? AvState.Ready : AvState.Inert);
            logTip.Text = historyOff
                ? "LOG: the event log is switched off (Events.HistoryLength is 0)."
                : "LOG: dispatches finished so far, out of the log capacity (Events.HistoryLength). The bars and rows below show them, newest first.";

            string id = current != null ? current.Id : "";
            float started = current != null ? current.StartedAtMissionTime : 0f;
            bool resolved = current != null && current.TargetResolved;
            bool rebind = id != boundId || started != boundStart || aimedAtLocal != boundAimed ||
                          multiplier != boundMultiplier || cooldown != boundCooldown || resolved != boundResolved;
            if (rebind)
            {
                boundId = id;
                boundStart = started;
                boundAimed = aimedAtLocal;
                boundResolved = resolved;
                boundMultiplier = multiplier;
                boundCooldown = cooldown;
                if (current != null)
                    hero.BindActive(current, TierState(current.Tier), Consequence(current, aimedAtLocal),
                        ConsequenceHelp(current, aimedAtLocal));
                else
                    hero.BindCalm(events.Available ? "SCANNING" : "NO MISSION");
            }

            if (current != null)
            {
                // One effect line replaces the three pills; the price and reset numbers live in the COST / RESET tiles.
                string tempo = aimedAtLocal && !string.IsNullOrEmpty(current.TempoSummary) ? current.TempoSummary : "";
                hero.SetEffect(EffectText(current, summary, aimedAtLocal) + (tempo.Length > 0 ? " · " + tempo : "") +
                    " · " + (aimedAtLocal ? "YOUR SIDE" : "NOT YOUR SIDE"), effectState);
                hero.SetScript(current, current.StartedAtMissionTime, now);

                responseSection.SetShown(true);
                responseDesk.SetShown(true);
                responseSection.SetCaption(responseDesk.Refresh(events, aimedAtLocal));
            }
            else
            {
                responseDesk.SetStandby();
                responseSection.SetShown(false);
                responseDesk.SetShown(false);
            }

            RefreshTimeline(current, history, now, historyOff);
            RefreshDesk(current, history, now);

            string ambient = current != null
                ? "EVENT // " + current.Title.ToUpperInvariant()
                : events.Available ? "NO EVENT" : "NO MISSION";
            string status = !string.IsNullOrEmpty(events.Signal) ? events.Signal
                : !string.IsNullOrEmpty(MapPicker.Prompt) ? MapPicker.Prompt
                : ambient;
            AvState footerState = !string.IsNullOrEmpty(events.Signal) ? AvState.Info
                : !string.IsNullOrEmpty(MapPicker.Prompt) ? AvState.Caution
                : AvState.Inert;
            console.Footer.Set(status, footerState);
        }

        private void RefreshTimeline(ActiveEventView current, IReadOnlyList<ActiveEventView> history, float now, bool historyOff)
        {
            int shown = Mathf.Min(history.Count, Mathf.Min(historyCapacity, EventTimelinePart.Capacity));
            // A scripted superevent needs the room for its beats, so the rows step back to three while it runs.
            int limit = current != null && current.IsSuper && current.Steps.Count > 0 ? 3 : VisibleLogRows;
            int rows = Mathf.Min(shown, limit);
            timeline.SetNote(historyOff ? "HISTORY OFF" : "NO DISPATCHES LOGGED");
            for (int i = 0; i < logBars.Length; i++) logBars[i] = 0f;
            for (int index = 0; index < shown; index++)
            {
                ActiveEventView view = history[history.Count - 1 - index];
                if (index < logBars.Length)
                    logBars[index] = view.Tier == "SUPEREVENT" ? 1f : view.Tier == "MEDIUM" ? 0.6f : 0.3f;
                if (index >= rows) continue;
                string effect = IsNeutral(view.EffectSummary) && !string.IsNullOrEmpty(view.TempoSummary)
                    ? view.TempoSummary : view.EffectSummary;
                timeline.Set(index, view.Title.ToUpperInvariant(),
                    TierShort(view.Tier) + " · " + ShortTarget(view.Target) + " · " +
                        Duration(now - view.EndsAtMissionTime) + " AGO",
                    IsNeutral(effect) ? "NO EFFECT" : effect, TierState(view.Tier),
                    IsNeutral(effect) ? AvState.Inert : effect[0] == '+' ? AvState.Danger : AvState.Ready);
            }
            timeline.SetCount(historyOff ? 0 : rows);
            // While a scripted superevent runs the bars step aside as well (the rows already list the log).
            bool bars = !historyOff && shown > 0 && limit == VisibleLogRows;
            historyEq.SetShown(bars);
            if (bars) historyEq.Set(logBars, "NEWEST FIRST", AvState.Info);
        }

        private void RefreshDesk(ActiveEventView current, IReadOnlyList<ActiveEventView> history, float now)
        {
            if (deskCase == null) return;
            ActiveEventView file = current ?? (history.Count > 0 ? history[history.Count - 1] : null);
            deskCaseCatalog = file != null ? CatalogIndexOf(file.Id) : -1;
            deskCaseAction?.SetShown(deskCaseCatalog >= 0);
            int aircraft = Encyclopedia.i?.aircraft?.Count ?? 0;
            tiles[0].SetCount(AvNum.Fixed(aircraft, 0) + " RECORDS");
            tiles[1].SetCount(AvNum.Fixed(EventCatalog.All.Length, 0) + " RECORDS");
            tiles[2].SetCount(AvNum.Fixed(EventDocs.World.Length, 0) + " RECORDS");
            tiles[3].SetCount(AvNum.Fixed(EventDocs.Guide.Length, 0) + " RECORDS");
            if (file == null)
            {
                deskCase.Bind("AWAITING REPORT", "NO CASE FILED", "", AvIcon.Radar2, null, AvState.Inert);
            }
            else
            {
                string meta = current != null ? file.Tier + " / LIVE · " + file.Target
                    : file.Tier + " / LAST FILED · " + file.Target;
                string body = current != null
                    ? file.EffectSummary +
                        (string.IsNullOrEmpty(file.TempoSummary) ? "" : " · " + file.TempoSummary) +
                        " · ENDS " + AvNum.Clock(file.EndsAtMissionTime - now)
                    : "CLOSED";
                deskCase.Bind(file.Title.ToUpperInvariant(), meta, body, CategoryIcon(file.Category),
                    EventArtCache.Get(file.IconKey, file.IsSuper ? "tier_super" : "tier_medium"), TierState(file.Tier));
            }

            // Records: the most recent finished events, each opening its dossier.
            int shown = Mathf.Min(history.Count, RecentRecords);
            recentSection.SetShown(shown > 0);
            for (int i = 0; i < recentRows.Length; i++)
            {
                bool on = i < shown;
                recentRows[i].SetShown(on);
                recentCatalog[i] = -1;
                if (!on) continue;
                ActiveEventView view = history[history.Count - 1 - i];
                recentCatalog[i] = CatalogIndexOf(view.Id);
                recentRows[i].Set(view.Title.ToUpperInvariant(),
                    TierShort(view.Tier) + " · " + Duration(now - view.EndsAtMissionTime) + " AGO",
                    ShortEffect(view), TierState(view.Tier));
            }
        }

        /// <summary>The director posture as three rings: posture, ground custody, supers fired.</summary>
        private void RefreshDirector()
        {
            TheaterBalance balance = events.Balance;
            float supers = EventDirector.MaximumSupers > 0
                ? Mathf.Clamp01(events.SupersFired / (float)EventDirector.MaximumSupers) : 0f;
            directorSupers.Set(supers, AvNum.Fixed(events.SupersFired, 0) + "/" + AvNum.Fixed(EventDirector.MaximumSupers, 0),
                AvState.Info);

            if (!balance.Known)
            {
                directorPosture.Set(0f, "—", AvState.Inert);
                directorBases.Set(0f, "—", AvState.Inert);
                return;
            }

            bool armed = balance.Contested && balance.Deficit >= EventDirector.AidDeficitThreshold;
            float pressure = EventDirector.AidDeficitThreshold > 0
                ? Mathf.Clamp01(balance.Deficit / (float)EventDirector.AidDeficitThreshold) : 0f;
            directorPosture.Set(pressure, armed ? "ARMED" : "MON", armed ? AvState.Caution : AvState.Info);
            int sum = balance.LeaderBases + balance.LoserBases;
            directorBases.Set(sum > 0 ? balance.LoserBases / (float)sum : 0f,
                AvNum.Fixed(balance.LeaderBases, 0) + ":" + AvNum.Fixed(balance.LoserBases, 0), AvState.Info);
        }

        /// <summary>The card's effect line, from this player's point of view.</summary>
        private static string EffectText(ActiveEventView view, string summary, bool aimedAtLocal)
        {
            if (!aimedAtLocal) return "NO EFFECT ON YOUR SIDE";
            return IsNeutral(summary)
                ? string.IsNullOrEmpty(view?.TempoSummary) ? "NO PRICE EFFECT" : view.TempoSummary
                : summary;
        }

        /// <summary>
        /// The hero's plain-language effect, visible without hovering, or a cancellation notice.
        /// </summary>
        private static string Consequence(ActiveEventView view, bool aimedAtLocal) =>
            view != null && !view.TargetResolved ? "TARGET LOST // ORDERS CANCELLED" : ConsequenceHelp(view, aimedAtLocal);

        /// <summary>Plain words for what the live effect means, for hover help.</summary>
        private static string ConsequenceHelp(ActiveEventView view, bool aimedAtLocal)
        {
            if (view == null) return "";
            if (!view.TargetResolved)
                return "Target faction unavailable — scripted orders cancelled.";
            if (!aimedAtLocal)
                return "Aimed at the " + view.Target.ToLowerInvariant() +
                       " — your support network is unchanged.";
            if (IsNeutral(view.EffectSummary))
                return string.IsNullOrEmpty(view.TempoSummary)
                    ? "Prices and readiness are unchanged."
                    : view.TempoSummary.Contains("+")
                        ? "Support requests take longer to reset; requisition prices are unchanged."
                        : "Support requests reset sooner; requisition prices are unchanged.";
            return view.EffectSummary[0] == '+'
                ? "Requisitions cost more until it ends." +
                    (string.IsNullOrEmpty(view.TempoSummary) ? "" : " " + view.TempoSummary + ".")
                : "Requisitions cost less until it ends." +
                    (string.IsNullOrEmpty(view.TempoSummary) ? "" : " " + view.TempoSummary + ".");
        }

        /// <summary>The effect as a value-column word: "+50% COST", "RESET +20%" or nothing.</summary>
        private static string ShortEffect(ActiveEventView view)
        {
            if (!IsNeutral(view.EffectSummary))
            {
                int space = view.EffectSummary.IndexOf(' ');
                return (space > 0 ? view.EffectSummary.Substring(0, space) : view.EffectSummary) + " COST";
            }
            if (string.IsNullOrEmpty(view.TempoSummary)) return "";
            int at = view.TempoSummary.LastIndexOf(' ');
            return "RESET " + (at >= 0 ? view.TempoSummary.Substring(at + 1) : view.TempoSummary);
        }

        private static float MissionTime() =>
            NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0f;

        private static string MultiplierLabel(float multiplier) => "x" + AvNum.Fixed(multiplier, 2);

        private static string TierShort(string tier)
        {
            if (tier == "SUPEREVENT") return "SUPER";
            if (tier == "MEDIUM") return "MEDIUM";
            return "MINOR";
        }

        /// <summary>The narrow column form of the target label, for the history rows.</summary>
        private static string ShortTarget(string target)
        {
            if (target == "HARD-PRESSED SIDE") return "LOSING SIDE";
            if (target == "ALL THEATER") return "ALL THEATER";
            return target;
        }

        /// <summary>Colour a tier's rail: weather recedes, a medium warns, a super is an alert.</summary>
        internal static AvState TierState(string tier) =>
            tier == "SUPEREVENT" ? AvState.Danger
            : tier == "MEDIUM" ? AvState.Caution
            : AvState.Inert;

        private static bool IsNeutral(string summary) =>
            string.IsNullOrEmpty(summary) || summary == "NO EFFECT";

        /// <summary>The state a live effect carries; the word always says the same (R1).</summary>
        private static AvState EffectState(string summary, bool aimedAtLocal)
        {
            if (!aimedAtLocal || IsNeutral(summary)) return AvState.Inert;
            return summary[0] == '+' ? AvState.Danger : AvState.Ready;
        }

        /// <summary>Compact age/duration; event windows are minutes, not hours, at this scale.</summary>
        private static string Duration(float seconds)
        {
            double total = Math.Max(0, Math.Ceiling(seconds));
            double minutes = Math.Floor(total / 60.0);
            if (minutes >= 60)
                return AvNum.Fixed(Math.Floor(minutes / 60.0), 0) + "h " + AvNum.Fixed(minutes % 60, 0) + "m";
            if (minutes > 0) return AvNum.Fixed(minutes, 0) + "m " + AvNum.Fixed(total % 60, 0) + "s";
            return AvNum.Fixed(total, 0) + "s";
        }
    }
}
