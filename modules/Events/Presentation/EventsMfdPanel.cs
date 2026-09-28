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

        private AvRow directorRow;
        private EventActiveCardPart activeCard;
        private DecisionBoardPart decisionBoard;
        private AvSection historySection;
        private AvList historyList;
        private int historyCapacity;

        private EventCaseFilePart deskCase;
        private AvSection archiveSection;
        private EventDeskArchive archive;

        private string boundId;
        private float boundStart;
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
            directorRow = null;
            activeCard = null;
            decisionBoard = null;
            historySection = null;
            historyList = null;
            historyCapacity = 0;
            deskCase = null;
            archiveSection = null;
            archive?.Close();
            archive = null;
            boundId = null;
            boundStart = 0f;
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

            console = AvConsole.Build(rootRect, MfdSlots.Events, "EVENT DIRECTORATE", 2, AvTokens.PanelWidth, height);
            AvLay.ClampIntoCanvas(console.Root);

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
            p.Section(AvIcon.Radar2, "DIRECTOR");
            directorRow = p.Add(new AvRow(p.Content));
            directorRow.Set("DIRECTOR · WAITING FOR GROUND CUSTODY DATA", "", "", AvState.Inert);

            p.Section(AvIcon.AlertTriangle, "ACTIVE DISPATCH");
            activeCard = p.Add(new EventActiveCardPart(p.Content));
            activeCard.BindCalm("Waiting for the director.");

            p.Section(AvIcon.Scale, "RESPONSE DESK");
            decisionBoard = p.Add(new DecisionBoardPart(p.Content, events.RequestResponse));

            historySection = p.Section(AvIcon.ListDetails, "EVENT LOG", "0 LOGGED");
            historyList = p.Add(new AvList(p.Content, console.Ticker, historyCapacity, BindHistoryRow));
        }

        private void BuildDeskPage(AvFlow p)
        {
            p.Section(AvIcon.Bookmark, "FIELD DESK", "LOCAL READING ROOM · NO SIGNAL LEAVES THIS COCKPIT");
            p.Buttons(new AvControl.Spec("OPEN FIELD ARCHIVE", () => OpenArchive(0), AvButtonStyle.Primary, AvIcon.Bookmark))
                .Controls[0].Help = "Open the client-local field archive.";

            p.Section(AvIcon.ListDetails, "CASE FILE", "THEATER WIRE");
            deskCase = p.Add(new EventCaseFilePart(p.Content));
            deskCase.Bind("AWAITING FIRST REPORT", "NO CASE FILED THIS MISSION",
                "The archive is available while the theater is quiet.", AvIcon.Radar2, null, AvState.Inert);

            archiveSection = p.Section(AvIcon.Database, "FIELD ARCHIVE", "");
            string[] labels = { "AIRFRAME REGISTRY", "EVENT DOSSIERS", "WORLD FILES", "FIELD MANUAL" };
            string[] notes =
            {
                "NATIVE AIRCRAFT / MODEL VIEWER", "AUTHORED THEATER SCENARIOS",
                "LIFE BEHIND THE FRONT", "READ THE SIGNAL / ISSUE ORDERS",
            };
            for (int i = 0; i < labels.Length; i++)
            {
                int section = i;
                AvRow row = p.Add(new AvRow(p.Content, () => OpenArchive(section)));
                row.Set(labels[i], notes[i], "OPEN", AvState.Info);
                row.Help = "Open " + labels[i].ToLowerInvariant() + " in the field archive.";
            }
        }

        private void OpenArchive(int section)
        {
            if (archive == null) archive = EventDeskArchive.Create();
            archive.Show(section);
        }

        private void BindHistoryRow(int index, AvRow row)
        {
            IReadOnlyList<ActiveEventView> history = events.History;
            int i = history.Count - 1 - index;
            if (i < 0 || i >= history.Count)
            {
                row.Set("", "", "", AvState.Inert);
                row.SetBadge(null);
                return;
            }
            ActiveEventView view = history[i];
            string effect = IsNeutral(view.EffectSummary) && !string.IsNullOrEmpty(view.TempoSummary)
                ? view.TempoSummary : view.EffectSummary;
            row.Set(view.Title.ToUpperInvariant(),
                TierShort(view.Tier) + " · " + ShortTarget(view.Target) + " · " +
                    Duration(MissionTime() - view.EndsAtMissionTime) + " AGO",
                effect, TierState(view.Tier));
            row.SetBadge(EventArtCache.Thumb(view.IconKey, view.IsSuper ? "tier_super" : "tier_medium"));
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
            if (id != boundId || started != boundStart)
            {
                boundId = id;
                boundStart = started;
                if (current != null)
                    activeCard.BindActive(current, TierState(current.Tier),
                        EffectText(current, summary, aimedAtLocal), EffectState(summary, aimedAtLocal),
                        Consequence(current, aimedAtLocal));
                else
                    activeCard.BindCalm(events.Available
                        ? "The theater is quiet. The director is watching for a story worth telling."
                        : "No mission is running on this host.");
            }

            if (current != null)
            {
                float span = Mathf.Max(1f, current.EndsAtMissionTime - current.StartedAtMissionTime);
                float remaining = Mathf.Clamp01((current.EndsAtMissionTime - now) / span);
                float secondsLeft = current.EndsAtMissionTime - now;
                bool ending = secondsLeft <= EndingSeconds;
                bool critical = secondsLeft <= CriticalSeconds;
                activeCard.SetClock("ENDS " + AvNum.Clock(secondsLeft), ending, critical);
                activeCard.SetScript(current, current.StartedAtMissionTime, now);
                bool penalty = EffectState(summary, aimedAtLocal) == AvState.Danger;
                AvState progressState = critical ? AvState.Danger : ending || penalty ? AvState.Caution : TierState(current.Tier);
                activeCard.SetProgress(remaining, progressState);
                decisionBoard.Refresh(events, aimedAtLocal);
            }
            else
            {
                decisionBoard.SetStandby();
            }

            historyList.SetCount(history.Count);
            historySection.SetCaption(historyOff ? "LOGGING DISABLED" :
                AvNum.Fixed(history.Count, 0) + "/" + AvNum.Fixed(historyCapacity, 0) + " LOGGED · MOST RECENT FIRST");

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

        private void RefreshDesk(ActiveEventView current, IReadOnlyList<ActiveEventView> history, float now)
        {
            if (deskCase == null) return;
            ActiveEventView file = current ?? (history.Count > 0 ? history[history.Count - 1] : null);
            int aircraft = Encyclopedia.i?.aircraft?.Count ?? 0;
            archiveSection.SetCaption("LOCAL INDEX · " + AvNum.Fixed(aircraft, 0) + " AIRFRAMES · " +
                AvNum.Fixed(EventCatalog.All.Length, 0) + " EVENTS · " + AvNum.Fixed(EventDocs.World.Length, 0) + " WORLD FILES");
            if (file == null)
            {
                deskCase.Bind("AWAITING FIRST REPORT", "NO CASE FILED THIS MISSION",
                    "The archive is available while the theater is quiet.", AvIcon.Radar2, null, AvState.Inert);
                return;
            }
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

        /// <summary>The director's posture: custody sits on the row, ARMED/MONITORING is the word.</summary>
        private void RefreshDirector()
        {
            TheaterBalance balance = events.Balance;
            string supers = "SUPERS " + AvNum.Fixed(events.SupersFired, 0) + "/" + AvNum.Fixed(EventDirector.MaximumSupers, 0);

            if (!balance.Known)
            {
                directorRow.Set("DIRECTOR · WAITING FOR GROUND CUSTODY DATA", "", "", AvState.Inert);
                return;
            }

            bool armed = balance.Contested && balance.Deficit >= EventDirector.AidDeficitThreshold;
            string bases = "BASES " + AvNum.Fixed(balance.LeaderBases, 0) + ":" + AvNum.Fixed(balance.LoserBases, 0);
            directorRow.Set(armed ? "DIRECTOR ARMED" : "DIRECTOR MONITORING", bases + " · " + supers, "",
                armed ? AvState.Caution : AvState.Info);
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
        private static AvState TierState(string tier) =>
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
