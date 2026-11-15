using NOAvionics;
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Events.Configuration;
using BoscaliSummer.Modules.Events.Domain;
using BoscaliSummer.Modules.Events.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// "EVN" — active dispatch, archive and field documentation. Kit v2 console: chrome
    /// (metrics/tabs/footer) from <see cref="AvConsole"/>, page content from <see cref="AvFlow"/>
    /// sections, rows and the module's own <see cref="EventHeroPart"/> / <see cref="EventCaseFilePart"/> parts.
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

        // The six vanilla slots are for WMC and the claimed screens; this screen owns an appended one.
        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Events, "EVN", "BoscaliEvents.Screen", preferLeft: false) { Host = true };
        private MFDScreen screen => installer.Screen;
        private AvConsole console;
        private AvSection liveSection;

        private EventHeroPart hero;
        private AvGauge directorPosture, directorBases, directorSupers;
        private AvRow onCallRow;
        private EventTimelinePart timeline;
        private int historyCapacity;

        private const int VisibleLogRows = 5;
        private AvSection archiveSection;
        private ArchiveTilePart[] tiles;
        private EventCaseFilePart deskCase;
        private AvButtons deskCaseAction;
        private int deskCaseCatalog = -1;
        private EventDeskArchive archive;

        private string boundId;
        private float boundStart;
        private float boundMultiplier = float.NaN;
        private float boundCooldown = float.NaN;
        private bool boundAimed;
        private bool boundResolved;
        private float nextRefresh;
        private bool boundPictures;
        private bool countsBound;

        public void Configure(EventsSettings config, EventsManager manager, ManualLogSource log)
        {
            settings = config;
            EventArtCache.Configure(config);
            events = manager;
            installer.Log = log;
            installer.Builder = BuildScreen;
        }

        public void ResetForScene()
        {
            installer.Reset();

            console = null;
            liveSection = null;
            hero = null;
            directorPosture = directorBases = directorSupers = null;
            onCallRow = null;
            timeline = null;
            historyCapacity = 0;
            archiveSection = null;
            tiles = null;
            deskCase = null;
            deskCaseAction = null;
            deskCaseCatalog = -1;
            countsBound = false;
            archive?.Close();
            archive = null;
            boundId = null;
            boundStart = 0f;
            boundMultiplier = float.NaN;
            boundCooldown = float.NaN;
            boundAimed = false;
            boundResolved = false;
            nextRefresh = 0f;
            cabinetSection = decisionsSection = null;
            cabinetCard = null;
            boundCard = -1;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (installer.Failed || events == null || settings == null) return;
            if (!settings.Enabled.Value)
            {
                // Disabling the director mid-scene releases the hosted slot instead of
                // leaving a frozen screen on the bezel.
                if (screen != null) ResetForScene();
                return;
            }
            if (!installer.Tick()) return;

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;

            bool visible = screen.isActive &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            if (archive != null && (!archive.IsOpen || !visible || console.CurrentPage != TabDesk))
            {
                archive.Close();
                archive = null;
            }
            if (visible) Refresh();
        }

        private RectTransform BuildScreen(RectTransform rootRect, float height)
        {
            // The root is clamped before the console exists. Clamping console.Root afterwards baked
            // the canvas correction into the child as a negative offset and shifted the whole page left.
            console = AvConsole.Build(rootRect, MfdSlots.Events, "EVENTS", 2, AvTokens.PanelWidth, height);
            // The country strip on top; the dispatch's own COST · RESET · LEFT · FILED read on DISPATCH's first line.
            CountryStrip.Mount(console);
            console.Tabs((AvIcon.AlertTriangle, "DISPATCH"), (AvIcon.Bookmark, "DESK"));

            historyCapacity = Mathf.Max(1, settings.HistoryLength.Value);
            BuildDispatchPage(console.Page(TabDispatch));
            BuildDeskPage(console.Page(TabDesk));
            console.Orders(TabDispatch, "One dispatch runs at a time. COST and RESET price it for your side; LEFT is its clock.");
            console.Orders(TabDesk, "Pull a drawer to read its cards. The archive is a local copy and sends nothing.");
            console.Finish();
            return console.Root;
        }

        // ---- Pages -------------------------------------------------------------------------

        private void BuildDispatchPage(AvFlow p)
        {
            // The cabinet's answer card (FOR THE CABINET) and the latest decisions, above the live dispatch (EventsMfdPanel.Cabinet).
            BuildCabinet(p);
            // The live dispatch under one readout line (what the header tiles used to say). A quiet theater shows one standby row and
            // leaves the rest to the console ground.
            liveSection = p.Section(AvIcon.AlertTriangle, "LIVE DISPATCH", "");
            HelpOn(liveSection, "COST: the support price multiplier the running event sets on requisitions. RESET: how fast your support requests come back off cooldown. LEFT: time until it ends. FILED: dispatches finished, out of the history capacity.");
            hero = p.Add(new EventHeroPart(p.Content));
            hero.BindCalm("STANDING BY FOR DIRECTOR REPORT");

            // The director's three rings need no section header: each ring names itself and carries its own tip.
            directorPosture = new AvGauge(p.Content, "POSTURE");
            directorBases = new AvGauge(p.Content, "BASES");
            directorSupers = new AvGauge(p.Content, "SUPERS");
            directorPosture.Help = "POSTURE: how close the losing side is to the base deficit that arms a superevent. ARMED means the director may escalate at its next roll; MON means it is only watching.";
            directorBases.Help = "BASES: ground airbases held by the leading side against the losing side (leader:loser). The ring fills with the loser's share.";
            directorSupers.Help = "SUPERS: superevents fired this mission out of the most the director allows. Each superevent fires once.";
            p.Row(directorPosture, directorBases, directorSupers);
            directorPosture.Set(0f, "—", AvState.Inert);
            directorBases.Set(0f, "—", AvState.Inert);
            directorSupers.Set(0f, "—", AvState.Inert);

            // While the theater is quiet one line says what the director holds on call: the authored catalog, counted by tier.
            onCallRow = p.Add(new AvRow(p.Content));
            onCallRow.Help = "ON CALL: the dispatches in the director's authored catalog by tier. Their dossiers are in the EVENTS drawer on DESK.";
            EventDefinition[] catalog = EventCatalog.All;
            int minor = 0, medium = 0, super = 0;
            foreach (EventDefinition definition in catalog)
            {
                if (definition.Tier == EventTier.Minor) minor++;
                else if (definition.Tier == EventTier.Medium) medium++;
                else if (definition.Tier == EventTier.Super) super++;
            }
            onCallRow.Set("ON CALL · " + AvNum.Fixed(catalog.Length, 0) + " DOSSIERS",
                AvNum.Fixed(minor, 0) + " MINOR · " + AvNum.Fixed(medium, 0) + " MEDIUM · " + AvNum.Fixed(super, 0) + " SUPER", "", AvState.Inert);
            onCallRow.SetShown(false);

            timeline = p.Add(new EventTimelinePart(p.Content));
            timeline.SetNote("NO DISPATCHES FILED");
        }

        private void BuildDeskPage(AvFlow p)
        {
            // The archive's eight drawers, in the library's own tab order: two rows of four. The case file below grows.
            archiveSection = p.Section(AvIcon.Database, "FIELD ARCHIVE", "RECORDS HELD");
            tiles = new ArchiveTilePart[ArchiveShelf.Count];
            for (int i = 0; i < tiles.Length; i++)
            {
                int shelf = i;
                tiles[i] = new ArchiveTilePart(p.Content, ArchiveShelf.Icons[i], ArchiveShelf.Names[i],
                    "DWR " + ArchiveUi.Two(i + 1), () => OpenArchive(shelf));
                tiles[i].Help = ArchiveShelf.Help[i] + " Opens the field archive at this drawer.";
            }
            p.Row(tiles[0], tiles[1], tiles[2], tiles[3]);
            p.Row(tiles[4], tiles[5], tiles[6], tiles[7]);

            // The case file takes the page's spare height only while it has a poster to show.
            deskCase = p.Add(new EventCaseFilePart(p.Content));
            deskCase.Help = "CASE FILE: the running dispatch, or the last one filed: poster, tier, target, price effect and time left. Older dossiers are in the EVENTS drawer.";
            BindNoCase();
            deskCaseAction = p.Buttons(new AvControl.Spec("OPEN CASE DOSSIER", () =>
            {
                if (deskCaseCatalog >= 0) OpenArchive(ArchiveShelf.Events, deskCaseCatalog);
            }, AvButtonStyle.Quiet, AvIcon.Bookmark));
            deskCaseAction.MinHeight = 44f;
            deskCaseAction.Controls[0].Help ="Read this event's catalog dossier, including its authored effects and timed orders.";
            deskCaseAction.SetShown(false);
        }

        /// <summary>The desk before the director has issued anything this mission.</summary>
        private void BindNoCase() =>
            deskCase.Bind("NO CASE FILED", "AWAITING DIRECTOR REPORT",
                "A case opens when the director issues a dispatch. The last one closed stays on file.",
                AvIcon.Radar2, null, AvState.Inert);

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

            if (console.CurrentPage == TabDispatch) RefreshDirector();

            bool historyOff = settings.HistoryLength.Value <= 0;
            float secondsLeft = current != null ? current.EndsAtMissionTime - now : 0f;
            AvState effectState = EffectState(summary, aimedAtLocal);

            // One readout line: COST and RESET are this side's prices (x1.00 is normal), LEFT the dispatch's clock, FILED the finished count.
            string log = "FILED " + (historyOff ? "OFF" : AvNum.Fixed(history.Count, 0) + "/" + AvNum.Fixed(historyCapacity, 0));
            liveSection.SetCaption(current == null ? "NO ACTIVE EVENT · " + log
                : "COST " + MultiplierLabel(multiplier) + (aimedAtLocal ? " YOU" : " THEM") + " · RESET " + MultiplierLabel(cooldown) +
                  " · LEFT " + AvNum.Clock(secondsLeft) + (secondsLeft <= CriticalSeconds ? " CRIT" : secondsLeft <= EndingSeconds ? " END" : "") + " · " + log);

            string id = current != null ? current.Id : "";
            float started = current != null ? current.StartedAtMissionTime : 0f;
            bool resolved = current != null && current.TargetResolved;
            bool rebind = id != boundId || started != boundStart || aimedAtLocal != boundAimed ||
                          multiplier != boundMultiplier || cooldown != boundCooldown || resolved != boundResolved ||
                          boundPictures != EventArtCache.PicturesEnabled;
            if (rebind)
            {
                boundPictures = EventArtCache.PicturesEnabled;
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
                    hero.BindCalm(events.Available ? "DIRECTOR SCANNING THE GROUND BALANCE" : "NO MISSION ON THE NET");
            }

            if (current != null)
            {
                // One effect line replaces the three pills; the price and reset numbers are on the LIVE DISPATCH line.
                string tempo = aimedAtLocal && !string.IsNullOrEmpty(current.TempoSummary) ? current.TempoSummary : "";
                hero.SetEffect(EffectText(current, summary, aimedAtLocal) + (tempo.Length > 0 ? " · " + tempo : "") +
                    " · " + (aimedAtLocal ? "YOUR SIDE" : "NOT YOUR SIDE"), effectState);
                hero.SetScript(current, current.StartedAtMissionTime, now);
            }

            onCallRow.SetShown(current == null);
            // Only the open page is refreshed (the desk counts walk the whole encyclopedia); a tab switch catches up within one interval.
            if (console.CurrentPage == TabDispatch) RefreshTimeline(current, history, now, historyOff);
            if (console.CurrentPage == TabDesk) RefreshDesk(current, history, now);
            string cabinet = RefreshCabinet();

            string ambient = cabinet.Length > 0 ? cabinet
                : current != null
                ? "DISPATCH LIVE · " + current.Title.ToUpperInvariant()
                : events.Available ? "THEATER QUIET · DIRECTOR SCANNING" : "NO MISSION ON THE NET";
            string status = !string.IsNullOrEmpty(MapPicker.Prompt) ? MapPicker.Prompt : ambient;
            AvState footerState = !string.IsNullOrEmpty(MapPicker.Prompt) ? AvState.Caution : AvState.Inert;
            console.Footer.Set(status, footerState);
        }

        private void RefreshTimeline(ActiveEventView current, IReadOnlyList<ActiveEventView> history, float now, bool historyOff)
        {
            int shown = Mathf.Min(history.Count, Mathf.Min(historyCapacity, EventTimelinePart.Capacity));
            // A scripted superevent needs the room for its beats, so the rows step back to three while it runs.
            int limit = current != null && current.IsSuper && current.Steps.Count > 0 ? 3 : VisibleLogRows;
            int rows = Mathf.Min(shown, limit);
            timeline.SetNote(historyOff ? "HISTORY OFF" : "NO DISPATCHES FILED");
            for (int index = 0; index < rows; index++)
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
            timeline.SetCount(historyOff ? 0 : rows);
        }

        private void RefreshDesk(ActiveEventView current, IReadOnlyList<ActiveEventView> history, float now)
        {
            if (deskCase == null) return;
            ActiveEventView file = current ?? (history.Count > 0 ? history[history.Count - 1] : null);
            deskCaseCatalog = file != null ? CatalogIndexOf(file.Id) : -1;
            deskCaseAction?.SetShown(deskCaseCatalog >= 0);
            if (!countsBound)
            {
                countsBound = true;
                int held = 0;
                for (int i = 0; i < tiles.Length; i++)
                {
                    int records = ArchiveShelf.CountOf(i);
                    held += records;
                    tiles[i].SetCount(records);
                }
                archiveSection.SetCaption(AvNum.Thousands(held) + " RECORDS HELD");
            }
            // A filed case shows its 16:9 poster; an empty file stays one row over the console ground.
            bool wide = file != null;
            if (deskCase.Wide != wide)
            {
                deskCase.Wide = wide;
                console.Page(TabDesk).RequestRelayout();
            }
            if (file == null)
            {
                BindNoCase();
            }
            else
            {
                string meta = current != null ? file.Tier + " · LIVE · " + file.Target
                    : file.Tier + " · LAST FILED · " + file.Target;
                string body = current != null
                    ? file.EffectSummary +
                        (string.IsNullOrEmpty(file.TempoSummary) ? "" : " · " + file.TempoSummary) +
                        " · ENDS " + AvNum.Clock(file.EndsAtMissionTime - now)
                    : "CLOSED";
                deskCase.Bind(file.Title.ToUpperInvariant(), meta, body, CategoryIcon(file.Category),
                    EventArtCache.Get(file.IconKey, file.IsSuper ? "tier_super" : "tier_medium"), TierState(file.Tier));
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
            view != null && !view.TargetResolved ? "TARGET LOST · ORDERS CANCELLED" : ConsequenceHelp(view, aimedAtLocal);

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
