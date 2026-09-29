using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Events.Configuration;
using BoscaliSummer.Features.Events.Domain;
using BoscaliSummer.Features.Events.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// "EVN" — active dispatch, response desk, archive and field documentation. Kit v2 console: chrome
    /// (chips/metrics/tabs/footer) from <see cref="AvConsole"/>, page content from <see cref="AvFlow"/>
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
        private AvChip[] chips;
        private AvMetric[] metrics;

        private EventHeroPart hero;
        private AvSection responseSection;
        private ResponseDeskPart responseDesk;
        private AvMetric directorPosture, directorBases, directorSupers;
        private AvSection directorSection;
        private AvSection historySection;
        private EventTimelinePart timeline;
        private int historyCapacity;

        private const int RecentRecords = 3;
        private AvSection archiveSection;
        private ArchiveTilePart[] tiles;
        private EventCaseFilePart deskCase;
        private AvSection recentSection;
        private AvRow[] recentRows;
        private readonly int[] recentCatalog = new int[RecentRecords];
        private EventDeskArchive archive;

        private string boundId;
        private float boundStart;
        private float boundMultiplier = float.NaN;
        private float boundCooldown = float.NaN;
        private bool boundAimed;
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
            chips = null;
            metrics = null;
            hero = null;
            responseSection = null;
            responseDesk = null;
            directorPosture = directorBases = directorSupers = null;
            directorSection = null;
            historySection = null;
            timeline = null;
            historyCapacity = 0;
            archiveSection = null;
            tiles = null;
            deskCase = null;
            recentSection = null;
            recentRows = null;
            archive?.Close();
            archive = null;
            boundId = null;
            boundStart = 0f;
            boundMultiplier = float.NaN;
            boundCooldown = float.NaN;
            boundAimed = false;
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
            chips = console.Chips(3);
            metrics = console.Metrics("SUPPORT COST", "SUPPORT RESET");
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
            hero = p.Add(new EventHeroPart(p.Content));
            hero.BindCalm("Waiting for the director.");

            responseSection = p.Section(AvIcon.Scale, "RESPONSE DESK", "AWAITING DISPATCH");
            responseDesk = p.Add(new ResponseDeskPart(p.Content, events.RequestResponse));
            responseSection.SetShown(false);
            responseDesk.SetShown(false);

            directorSection = p.Section(AvIcon.Radar2, "DIRECTOR", "WAITING FOR GROUND CUSTODY DATA");
            directorPosture = new AvMetric(p.Content, "POSTURE");
            directorBases = new AvMetric(p.Content, "GROUND BASES");
            directorSupers = new AvMetric(p.Content, "SUPERS");
            p.Row(directorPosture, directorBases, directorSupers);
            directorPosture.Set("—", "AWAITING DATA", 0f, AvState.Inert);
            directorBases.Set("—", "LEAD : TRAIL", 0f, AvState.Inert);
            directorSupers.Set("—", "SUPERS FIRED", 0f, AvState.Inert);

            historySection = p.Section(AvIcon.ListDetails, "EVENT LOG", "0 LOGGED");
            timeline = p.Add(new EventTimelinePart(p.Content));
            timeline.SetNote("Nothing has been filed yet. Finished dispatches are logged here, newest first.");
        }

        private void BuildDeskPage(AvFlow p)
        {
            archiveSection = p.Section(AvIcon.Database, "FIELD ARCHIVE", "LOCAL READING ROOM");
            tiles = new ArchiveTilePart[4];
            string[] labels = { "AIRFRAMES", "EVENT DOSSIERS", "WORLD FILES", "FIELD MANUAL" };
            string[] notes =
            {
                "Native aircraft with a rotating model viewer.", "Every authored theater scenario.",
                "Life behind the front line.", "Read the signal and issue orders.",
            };
            AvIcon[] icons = { AvIcon.Plane, AvIcon.AlertTriangle, AvIcon.Map2, AvIcon.Bookmark };
            for (int i = 0; i < tiles.Length; i++)
            {
                int section = i;
                tiles[i] = new ArchiveTilePart(p.Content, icons[i], labels[i], notes[i], () => OpenArchive(section));
                tiles[i].Help = "Open " + labels[i].ToLowerInvariant() + " in the field archive.";
            }
            p.Row(tiles[0], tiles[1]);
            p.Row(tiles[2], tiles[3]);

            p.Section(AvIcon.Bookmark, "CASE FILE", "CURRENT OR LAST DISPATCH");
            deskCase = p.Add(new EventCaseFilePart(p.Content));
            deskCase.Bind("AWAITING FIRST REPORT", "NO CASE FILED THIS MISSION",
                "The archive is available while the theater is quiet.", AvIcon.Radar2, null, AvState.Inert);

            recentSection = p.Section(AvIcon.ListDetails, "RECENT RECORDS", "OPEN IN THE ARCHIVE");
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

            chips[0].Set(current != null ? TierShort(current.Tier) : "STANDBY", TierChipState(current));

            bool tempoOnly = current != null && aimedAtLocal &&
                Mathf.Abs(multiplier - 1f) < 0.001f && Mathf.Abs(cooldown - 1f) >= 0.001f;
            chips[1].Set(
                tempoOnly ? (cooldown > 1f ? "RESET SLOW" : "RESET FAST") :
                    current != null ? DirectionLabel(multiplier, aimedAtLocal) : "NO EVENT",
                tempoOnly ? (cooldown > 1f ? AvState.Caution : AvState.Ready) :
                    DirectionState(multiplier, aimedAtLocal, current != null));

            bool historyOff = settings.HistoryLength.Value <= 0;
            chips[2].Set(historyOff ? "HISTORY OFF" : AvNum.Fixed(history.Count, 0) + "/" + AvNum.Fixed(historyCapacity, 0) + " LOGGED",
                !historyOff && history.Count > 0 ? AvState.Ready : AvState.Inert);

            string sideNote = current == null ? "" : aimedAtLocal ? "YOUR SIDE" : "OTHER SIDE";
            string costCaption = current == null ? "NO ACTIVE EVENT" : EffectText(current, summary, aimedAtLocal);
            metrics[0].Set(
                current != null ? MultiplierLabel(multiplier) : "—",
                current == null ? costCaption : sideNote + " · " + costCaption,
                current != null && aimedAtLocal ? Mathf.Clamp01(Mathf.Abs(multiplier - 1f)) : 0f,
                EffectState(summary, aimedAtLocal));

            string resetCaption = current == null ? "NO ACTIVE EVENT" :
                !aimedAtLocal ? "NO EFFECT ON YOU" :
                cooldown > 1.001f ? "LONGER COOLDOWN" :
                cooldown < 0.999f ? "SHORTER COOLDOWN" : "NORMAL RESET";
            AvState resetState = !aimedAtLocal ? AvState.Inert :
                cooldown > 1.001f ? AvState.Caution : cooldown < 0.999f ? AvState.Ready : AvState.Inert;
            metrics[1].Set(
                current != null ? MultiplierLabel(cooldown) : "—",
                current == null ? "NO ACTIVE EVENT" : "REQUEST CLOCK · " + resetCaption,
                current != null && aimedAtLocal ? Mathf.Clamp01(Mathf.Abs(cooldown - 1f)) : 0f,
                resetState);

            string id = current != null ? current.Id : "";
            float started = current != null ? current.StartedAtMissionTime : 0f;
            bool rebind = id != boundId || started != boundStart || aimedAtLocal != boundAimed ||
                          multiplier != boundMultiplier || cooldown != boundCooldown;
            if (rebind)
            {
                boundId = id;
                boundStart = started;
                boundAimed = aimedAtLocal;
                boundMultiplier = multiplier;
                boundCooldown = cooldown;
                if (current != null)
                    hero.BindActive(current, TierState(current.Tier), Consequence(current, aimedAtLocal));
                else
                    hero.BindCalm(events.Available
                        ? "The director is watching the theater for a story worth telling."
                        : "No mission is running on this host.");
            }

            if (current != null)
            {
                float span = Mathf.Max(1f, current.EndsAtMissionTime - current.StartedAtMissionTime);
                float remaining = Mathf.Clamp01((current.EndsAtMissionTime - now) / span);
                float secondsLeft = current.EndsAtMissionTime - now;
                bool ending = secondsLeft <= EndingSeconds;
                bool critical = secondsLeft <= CriticalSeconds;
                AvState effectState = EffectState(summary, aimedAtLocal);
                string tempo = aimedAtLocal && !string.IsNullOrEmpty(current.TempoSummary) ? current.TempoSummary : "";
                hero.SetPills(EffectText(current, summary, aimedAtLocal), effectState,
                    tempo, tempo.Contains("+") ? AvState.Caution : AvState.Ready,
                    aimedAtLocal ? "YOUR SIDE" : "NOT YOUR SIDE", aimedAtLocal ? AvState.Info : AvState.Inert);
                hero.SetClock(AvNum.Clock(secondsLeft), ending, critical);
                hero.SetScript(current, current.StartedAtMissionTime, now);
                // The bar is time, not severity: neutral until the event is ending or it is costing this side.
                AvState progressState = critical ? AvState.Danger
                    : ending || effectState == AvState.Danger ? AvState.Caution
                    : effectState == AvState.Ready ? AvState.Ready : AvState.Info;
                hero.SetProgress(remaining, progressState);

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

            RefreshTimeline(history, now, historyOff);
            RefreshDesk(current, history, now);

            string ambient = current != null
                ? "WORLD EVENT: " + current.Title
                : events.Available ? "No active world event." : "No running mission.";
            string status = !string.IsNullOrEmpty(events.Signal) ? events.Signal
                : !string.IsNullOrEmpty(MapPicker.Prompt) ? MapPicker.Prompt
                : ambient;
            AvState footerState = !string.IsNullOrEmpty(events.Signal) ? AvState.Info
                : !string.IsNullOrEmpty(MapPicker.Prompt) ? AvState.Caution
                : AvState.Inert;
            console.Footer.Set(status, footerState);
        }

        private void RefreshTimeline(IReadOnlyList<ActiveEventView> history, float now, bool historyOff)
        {
            int shown = Mathf.Min(history.Count, Mathf.Min(historyCapacity, EventTimelinePart.Capacity));
            timeline.SetNote(historyOff
                ? "History is switched off in the Events settings."
                : "Nothing has been filed yet. Finished dispatches are logged here, newest first.");
            for (int index = 0; index < shown; index++)
            {
                ActiveEventView view = history[history.Count - 1 - index];
                string effect = IsNeutral(view.EffectSummary) && !string.IsNullOrEmpty(view.TempoSummary)
                    ? view.TempoSummary : view.EffectSummary;
                timeline.Set(index, view.Title.ToUpperInvariant(),
                    TierShort(view.Tier) + " · " + ShortTarget(view.Target) + " · " +
                        Duration(now - view.EndsAtMissionTime) + " AGO",
                    IsNeutral(effect) ? "NO EFFECT" : effect, TierState(view.Tier),
                    IsNeutral(effect) ? AvState.Inert : effect[0] == '+' ? AvState.Danger : AvState.Ready);
            }
            timeline.SetCount(historyOff ? 0 : shown);
            historySection.SetCaption(historyOff ? "LOGGING DISABLED" :
                AvNum.Fixed(history.Count, 0) + "/" + AvNum.Fixed(historyCapacity, 0) + " LOGGED · NEWEST FIRST");
        }

        private void RefreshDesk(ActiveEventView current, IReadOnlyList<ActiveEventView> history, float now)
        {
            if (deskCase == null) return;
            ActiveEventView file = current ?? (history.Count > 0 ? history[history.Count - 1] : null);
            int aircraft = Encyclopedia.i?.aircraft?.Count ?? 0;
            tiles[0].SetCount(AvNum.Fixed(aircraft, 0) + " RECORDS");
            tiles[1].SetCount(AvNum.Fixed(EventCatalog.All.Length, 0) + " RECORDS");
            tiles[2].SetCount(AvNum.Fixed(EventDocs.World.Length, 0) + " RECORDS");
            tiles[3].SetCount(AvNum.Fixed(EventDocs.Guide.Length, 0) + " RECORDS");
            if (file == null)
            {
                deskCase.Bind("AWAITING FIRST REPORT", "NO CASE FILED THIS MISSION",
                    "The archive is available while the theater is quiet.", AvIcon.Radar2, null, AvState.Inert);
            }
            else
            {
                string meta = current != null ? file.Tier + " / LIVE · " + file.Target
                    : file.Tier + " / LAST FILED · " + file.Target;
                string body = current != null
                    ? file.EffectSummary +
                        (string.IsNullOrEmpty(file.TempoSummary) ? "" : " · " + file.TempoSummary) +
                        " · ENDS " + AvNum.Clock(file.EndsAtMissionTime - now)
                    : "This dispatch has closed. Its full dossier remains in the archive.";
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

        /// <summary>The director posture as three readings: posture, ground custody, supers fired.</summary>
        private void RefreshDirector()
        {
            TheaterBalance balance = events.Balance;
            float supers = EventDirector.MaximumSupers > 0
                ? Mathf.Clamp01(events.SupersFired / (float)EventDirector.MaximumSupers) : 0f;
            directorSupers.Set(AvNum.Fixed(events.SupersFired, 0) + "/" + AvNum.Fixed(EventDirector.MaximumSupers, 0),
                "SUPERS FIRED", supers, AvState.Info);

            if (!balance.Known)
            {
                directorSection.SetCaption("WAITING FOR GROUND CUSTODY DATA");
                directorPosture.Set("—", "AWAITING DATA", 0f, AvState.Inert);
                directorBases.Set("—", "LEAD : TRAIL", 0f, AvState.Inert);
                return;
            }

            bool armed = balance.Contested && balance.Deficit >= EventDirector.AidDeficitThreshold;
            directorSection.SetCaption(armed ? "A SUPEREVENT CAN FIRE" : "WATCHING THE THEATER");
            float pressure = EventDirector.AidDeficitThreshold > 0
                ? Mathf.Clamp01(balance.Deficit / (float)EventDirector.AidDeficitThreshold) : 0f;
            directorPosture.Set(armed ? "ARMED" : "MONITORING", armed ? "SUPER ELIGIBLE" : "NO TRIGGER",
                pressure, armed ? AvState.Caution : AvState.Info);
            int sum = balance.LeaderBases + balance.LoserBases;
            directorBases.Set(AvNum.Fixed(balance.LeaderBases, 0) + ":" + AvNum.Fixed(balance.LoserBases, 0),
                "LEAD : TRAIL", sum > 0 ? balance.LoserBases / (float)sum : 0f, AvState.Info);
        }

        /// <summary>The card's effect line, from this player's point of view.</summary>
        private static string EffectText(ActiveEventView view, string summary, bool aimedAtLocal)
        {
            if (!aimedAtLocal) return "NO EFFECT ON YOUR SIDE";
            return IsNeutral(summary)
                ? string.IsNullOrEmpty(view?.TempoSummary) ? "NO PRICE EFFECT" : view.TempoSummary
                : summary;
        }

        /// <summary>Plain words for what the live effect means to the player reading it.</summary>
        private static string Consequence(ActiveEventView view, bool aimedAtLocal)
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

        private static string DirectionLabel(float multiplier, bool active)
        {
            if (!active) return "NOT YOUR SIDE";
            if (multiplier > 1f) return "COST UP";
            if (multiplier < 1f) return "COST DOWN";
            return "FLAT";
        }

        private static AvState DirectionState(float multiplier, bool active, bool hasEvent)
        {
            if (!hasEvent || !active) return AvState.Inert;
            if (multiplier > 1f) return AvState.Caution;
            if (multiplier < 1f) return AvState.Ready;
            return AvState.Inert;
        }

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

        private static AvState TierChipState(ActiveEventView view) =>
            view == null ? AvState.Inert
            : view.Tier == "SUPEREVENT" ? AvState.Danger
            : view.Tier == "MEDIUM" ? AvState.Caution
            : AvState.Inert;

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
