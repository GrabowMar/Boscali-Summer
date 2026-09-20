using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using BoscaliSummer.Core;
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
    /// "EVN" — the world-event directorate board. One fact, one place: the top bar carries
    /// status, tier and the log count; the two metric cells carry the live price factor and
    /// the ground war; one director line says whether the director is armed; the card carries
    /// the event in reading order — plate, title, category and scope, countdown, effect,
    /// plain-words consequence, copy, scripted beats, decision; history follows as a compact
    /// media feed that fills the body. Nothing is printed twice, no state is signalled by
    /// colour alone, and no state is ever a blank rectangle.
    /// </summary>
    internal sealed partial class EventsMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.25f;

        private const int ChipCount = 3;
        private const int TabEvents = 0;

        private const float DirectorLineHeight = 18f;
        private const float CardGap = 6f;
        private const float HistoryCardHeight = 64f;
        private const float HistoryHeaderHeight = 22f;
        private const float HistoryPad = 12f;

        /// <summary>
        /// The scripted-beat rows the active card and the full-screen broadcast both reserve.
        /// One cap for both, so a four-beat entry cannot silently drop its last beat.
        /// </summary>
        internal const int MaximumEventSteps = 4;

        /// <summary>
        /// The empty feed must clear its own help block: the copy sits 48px down and runs 30px.
        /// </summary>
        private const float EmptyHistoryCardHeight = 90f;

        /// <summary>The clock starts saying ENDING this many seconds out, and pulsing.</summary>
        private const float EndingSeconds = 60f;

        /// <summary>Below this the clock and its rail go danger, not caution.</summary>
        private const float CriticalSeconds = 20f;

        private EventsSettings settings;
        private EventsManager events;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvScreen shell;

        private ActiveEventCard activeCard;
        private readonly List<HistoryCard> historyCards = new List<HistoryCard>(16);
        private RectTransform scrollContent;
        private bool hasViewport;
        private float contentTop;
        private float contentX;
        private float contentWidth;
        private float viewportHeight;
        private float historyBaseY;
        private float lastCardHeight;
        private int lastRows = -1;

        private Image spine;
        private Image directorRail;
        private TMP_Text directorLine;
        private Image historyBand;
        private TMP_Text historyTitle;
        private Image historyTick;
        private TMP_Text historyNote;

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
            shell = null;
            activeCard = null;
            historyCards.Clear();
            scrollContent = null;
            hasViewport = false;
            contentTop = contentX = contentWidth = viewportHeight = historyBaseY = 0f;
            lastCardHeight = 0f;
            lastRows = -1;
            spine = null;
            directorRail = null;
            directorLine = null;
            historyBand = null;
            historyTitle = null;
            historyTick = null;
            historyNote = null;
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
            if (!visible) return;

            // The countdown breathes between the four-hertz refreshes; nothing else runs.
            if (activeCard != null && events.Current != null) activeCard.TickPulse();
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
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            TMP_FontAsset font = sourceText != null ? sourceText.font : null;
            if (font != null) AvFont.Font = font;

            var root = new GameObject("BoscaliEvents.Screen", typeof(RectTransform), typeof(Image));
            screenRoot = root;
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            float height = AvScreen.ResolveHeight(
                templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            AvKit.ClampIntoCanvas(rootRect);

            Image background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            var content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            shell = AvScreen.Build(
                content, MfdSlots.Events,
                Array.Empty<string>(),
                new[]
                {
                    new[] { "SUPPORT COST", "SIDE" },
                    new[] { "GROUND CUSTODY", "BASES" },
                },
                ChipCount, Width, height, _ => nextRefresh = 0f);

            BuildEventsPage(shell.CreatePage(TabEvents, "EventsPage"));

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Events;
            result.displayPanel = contentObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            screenRoot = root;
            shell.SetPage(TabEvents);
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

        // ---- Page ------------------------------------------------------------------------

        private void BuildEventsPage(GameObject page)
        {
            int capacity = Mathf.Max(1, settings.HistoryLength.Value);
            // Sized for the scripted card so the viewport exists whenever a superevent can
            // grow the page past the body; a short one still fits without paying for a mask.
            float buildHeight = DirectorLineHeight + CardGap +
                                ActiveEventCard.ScriptedHeight + CardGap +
                                HistoryHeaderHeight + capacity * (HistoryCardHeight + CardGap) + HistoryPad;

            Rect body = shell.Body;
            viewportHeight = body.height;

            var pageRect = (RectTransform)page.transform;
            RectTransform parent = AvScreen.Scroll(pageRect, body, buildHeight, out body);
            hasViewport = parent != pageRect;
            scrollContent = parent;
            contentTop = body.y;
            contentX = body.x + AvScreen.SpineInset;
            contentWidth = body.width - AvScreen.SpineInset;

            float x = contentX;
            float width = contentWidth;
            float y = body.y;

            spine = AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, viewportHeight));

            // The director's posture gets a rail as well as words: armed is a state, not a
            // colour the player has to decode.
            directorRail = AvKit.Panel(parent, new Rect(x, y + 3f, 3f, 12f), AvTheme.RailInert);
            directorRail.raycastTarget = false;
            directorLine = AvStyled.Label(parent, new Rect(x + 10f, y, width - 10f, DirectorLineHeight),
                "", "row-sub");
            y -= DirectorLineHeight + CardGap;

            activeCard = new ActiveEventCard(parent, x, y, width);
            y -= activeCard.Height + CardGap;

            historyBand = AvKit.Panel(parent, new Rect(x, y + 4f, width, HistoryHeaderHeight), Color.clear);
            historyBand.raycastTarget = false;
            historyTick = AvKit.Panel(parent, new Rect(x, y - 1f, 3f, 14f), AvTheme.Accent);
            historyTick.raycastTarget = false;
            historyTitle = AvStyled.Label(parent, new Rect(x + 10f, y, width * 0.5f - 10f, 14f),
                "EVENT LOG", "section-title");
            historyNote = AvStyled.Label(parent, new Rect(x + width * 0.5f, y, width * 0.5f, 14f),
                "MOST RECENT FIRST", "section-title-note", align: TextAlignmentOptions.MidlineRight);
            historyBaseY = y - HistoryHeaderHeight;

            historyCards.Clear();
            for (int i = 0; i < capacity; i++)
            {
                var card = new HistoryCard(parent);
                card.Hide();
                historyCards.Add(card);
            }
            LayoutHistory(0);
        }

        // ---- Refresh ---------------------------------------------------------------------

        private void Refresh()
        {
            if (shell == null || events == null) return;

            ActiveEventView current = events.Current;
            IReadOnlyList<ActiveEventView> history = events.History;
            int capacity = historyCards.Count;
            float now = MissionTime();

            float multiplier = current != null ? events.SupportCostMultiplier : 1f;
            string summary = current != null ? EventSelector.EffectSummary(multiplier) : null;
            bool aimedAtLocal = current != null && (events.LocalTargeted || current.Target == "ALL THEATER");
            bool penalty = current != null && EffectColor(summary, aimedAtLocal) == AvTheme.RailDanger;

            RefreshDirector();

            shell.DataBar.State.text = current != null
                ? "EVENT ACTIVE"
                : events.Available ? "THEATER CALM" : "NO RUNNING MISSION";
            shell.DataBar.State.color = current != null ? TierInk(current.Tier) : AvTheme.Dim;
            shell.DataBar.SetChip(0, current != null ? TierShort(current.Tier) : "STANDBY",
                                  TierChipState(current));
            shell.DataBar.SetChip(1, current != null ? DirectionLabel(multiplier, aimedAtLocal) : "NO EVENT",
                                  DirectionState(multiplier, aimedAtLocal, current != null));
            bool historyOff = settings.HistoryLength.Value <= 0;
            shell.DataBar.SetChip(2,
                                  historyOff ? "HISTORY OFF" : history.Count + "/" + capacity + " LOGGED",
                                  !historyOff && history.Count > 0 ? "live" : "inert");

            // The caption is the effect in the player's own terms; it is kept inside the
            // cell's measured width so it can never be ellipsised into ambiguity.
            shell.Metrics[0].Unit.text = current == null ? "" : aimedAtLocal ? "YOUR SIDE" : "OTHER SIDE";
            shell.Metrics[0].Set(
                current != null ? MultiplierLabel(multiplier) : "—",
                current == null ? "NO ACTIVE EVENT"
                    : aimedAtLocal ? summary : "NO EFFECT ON YOU",
                current != null && aimedAtLocal ? Mathf.Clamp01(Mathf.Abs(multiplier - 1f)) : 0f,
                EffectColor(summary, aimedAtLocal));

            TheaterBalance balance = events.Balance;
            shell.Metrics[1].Unit.text = balance.Known ? "BASES" : "";
            shell.Metrics[1].Set(
                balance.Known ? balance.LeaderBases + " : " + balance.LoserBases : "—",
                CustodyLine(balance),
                BalanceFraction(balance),
                balance.Known ? AvTheme.RailInfo : AvTheme.RailInert);

            string id = current != null ? current.Id : "";
            float started = current != null ? current.StartedAtMissionTime : 0f;
            if (id != boundId || started != boundStart)
            {
                boundId = id;
                boundStart = started;
                if (current != null)
                    activeCard.Bind(current, EffectText(summary, aimedAtLocal),
                        EffectColor(summary, aimedAtLocal), Consequence(current, aimedAtLocal));
                else
                    activeCard.BindPlaceholder(events.Available
                        ? "The theater is quiet. The director is watching for a story worth telling."
                        : "No mission is running on this host.");

                if (activeCard.Height != lastCardHeight)
                {
                    lastCardHeight = activeCard.Height;
                    LayoutHistory(history.Count);
                }
            }

            if (current != null)
            {
                float span = Mathf.Max(1f, current.EndsAtMissionTime - current.StartedAtMissionTime);
                float remaining = Mathf.Clamp01((current.EndsAtMissionTime - now) / span);
                float secondsLeft = current.EndsAtMissionTime - now;
                activeCard.SetProgress(remaining, EffectColor(summary, aimedAtLocal));
                bool ending = secondsLeft <= EndingSeconds;
                activeCard.SetClock("ENDS " + Clock(secondsLeft), ending, secondsLeft <= CriticalSeconds);
                activeCard.SetScript(current, current.StartedAtMissionTime, now, TierRail(current.Tier));
                // Only a penalty aimed at this player's side raises the alarm; a discount
                // expiring is not a threat.
                activeCard.SetUrgency(penalty ? Mathf.Clamp01((90f - secondsLeft) / 90f) : 0f);
                activeCard.TickPulse();
                RefreshResponse(multiplier, summary, aimedAtLocal);
            }

            if (history.Count != lastRows) LayoutHistory(history.Count);
            for (int i = 0; i < capacity; i++)
            {
                if (i < history.Count)
                {
                    ActiveEventView view = history[history.Count - 1 - i];
                    historyCards[i].Bind(view, TierInk(view.Tier), TierRail(view.Tier),
                        EffectColor(view.EffectSummary, true));
                    historyCards[i].SetClock(Duration(now - view.EndsAtMissionTime) + " AGO");
                }
                else if (i == 0)
                {
                    historyCards[i].BindEmpty();
                }
                else
                {
                    historyCards[i].Hide();
                }
            }

            string ambient = current != null
                ? "WORLD EVENT: " + current.Title
                : events.Available ? "No active world event." : "No running mission.";
            shell.WriteStatus(events.Signal, MapPicker.Prompt, ambient);
        }

        /// <summary>
        /// Places the feed rows and sizes the scroll content. With no rows the empty card is
        /// stretched to the bottom of the body so the section fills its space; with rows the
        /// content is only as tall as the events it holds.
        /// </summary>
        private void LayoutHistory(int rows)
        {
            if (historyCards.Count == 0) return;
            lastRows = rows;

            float shift = activeCard != null ? activeCard.Height - ActiveEventCard.PlainHeight : 0f;
            float top = historyBaseY - shift;

            if (historyBand != null) AvKit.Place(historyBand.rectTransform, new Rect(contentX, top + 26f, contentWidth, HistoryHeaderHeight));
            if (historyTick != null) AvKit.Place(historyTick.rectTransform, new Rect(contentX, top + 21f, 3f, 14f));
            if (historyTitle != null) AvKit.Place(historyTitle.rectTransform, new Rect(contentX + 10f, top + 22f, contentWidth * 0.5f - 10f, 14f));
            if (historyNote != null) AvKit.Place(historyNote.rectTransform, new Rect(contentX + contentWidth * 0.5f, top + 22f, contentWidth * 0.5f, 14f));

            float emptyHeight = Mathf.Max(EmptyHistoryCardHeight,
                viewportHeight - (contentTop - top) - HistoryPad);
            for (int i = 0; i < historyCards.Count; i++)
            {
                bool empty = rows == 0 && i == 0;
                historyCards[i].Place(contentX, top - i * (HistoryCardHeight + CardGap), contentWidth,
                    empty ? emptyHeight : HistoryCardHeight);
            }
            SetContentHeight(rows, top, emptyHeight);
        }

        /// <summary>
        /// Sizes the scroll content to what the page actually holds. The empty card is a real
        /// card: when a scripted superevent pushes the feed down, the content reaches its bottom
        /// so the help copy is never clipped by the viewport.
        /// </summary>
        private void SetContentHeight(int rows, float top, float emptyHeight)
        {
            if (!hasViewport || scrollContent == null) return;
            float needed = (contentTop - top) + HistoryPad +
                           (rows == 0 ? emptyHeight : rows * (HistoryCardHeight + CardGap));
            float height = Mathf.Max(viewportHeight, needed);
            if (Mathf.Abs(scrollContent.sizeDelta.y - height) > 0.5f)
                scrollContent.sizeDelta = new Vector2(scrollContent.sizeDelta.x, height);
            if (spine != null)
                AvKit.Place(spine.rectTransform, new Rect(spine.rectTransform.anchoredPosition.x,
                    spine.rectTransform.anchoredPosition.y, 3f, height));
        }

        /// <summary>The director's posture, once: custody is on the metric, this is the voice.</summary>
        private void RefreshDirector()
        {
            TheaterBalance balance = events.Balance;
            string supers = "SUPERS " + events.SupersFired + "/" + EventDirector.MaximumSupers;

            if (!balance.Known)
            {
                directorLine.text = "DIRECTOR  ·  WAITING FOR GROUND CUSTODY DATA";
                directorLine.color = AvTheme.Dim;
                directorRail.color = AvTheme.RailInert;
                return;
            }

            bool armed = balance.Contested && balance.Deficit >= EventDirector.AidDeficitThreshold;
            directorLine.text = armed
                ? "DIRECTOR ARMED  ·  TRAILING SIDE ELIGIBLE  ·  " + supers
                : "DIRECTOR MONITORING  ·  " + supers;
            directorLine.color = armed ? AvTheme.RailCaution : AvTheme.Dim;
            directorRail.color = armed ? AvTheme.RailCaution : AvTheme.RailInert;
        }

        /// <summary>The one decision: mitigate a penalty, or deepen a discount. Pay to own it.</summary>
        private void RefreshResponse(float multiplier, string summary, bool aimedAtLocal)
        {
            EventResponseKind response = events.LocalResponse;
            if (response != EventResponseKind.None)
            {
                activeCard.SetResponse(
                    response == EventResponseKind.Contain ? "PENALTY CONTAINED" : "DISCOUNT DEEPENED",
                    "RESPONSE ACTIVE · " + summary,
                    ready: true, enabled: false,
                    tooltip: "Your side's answer to this event is paid for and stays in force for the rest of its run.",
                    onClick: null);
                return;
            }
            if (!events.CanRespond || !aimedAtLocal)
            {
                activeCard.HideResponse();
                return;
            }

            int cost = events.ResponseCost;
            float available = LocalAllocation();
            bool affordable = available + 0.001f >= cost;
            EventResponseKind kind = multiplier > 1f ? EventResponseKind.Contain : EventResponseKind.Leverage;
            string label = (kind == EventResponseKind.Contain ? "CONTAIN" : "LEVERAGE") + " · " + cost + " ALLOC";
            string note = affordable
                ? kind == EventResponseKind.Contain ? "HALVES THE PENALTY" : "DEEPENS THE DISCOUNT"
                : "INSUFFICIENT ALLOCATION";
            string tooltip = affordable
                ? kind == EventResponseKind.Contain
                    ? "Spend " + cost + " allocation to halve this event's support-cost penalty for the rest of its run."
                    : "Spend " + cost + " allocation to deepen this event's support-cost discount for the rest of its run."
                : "This response costs " + cost + " allocation and you have " +
                  Mathf.FloorToInt(available) + ". Allocation arrives from events aimed at your side.";
            activeCard.SetResponse(label, note, ready: affordable, enabled: affordable,
                tooltip: tooltip, onClick: events.RequestResponse);
        }

        /// <summary>The card's effect line, from this player's point of view.</summary>
        private static string EffectText(string summary, bool aimedAtLocal)
        {
            if (!aimedAtLocal) return "NO EFFECT ON YOUR SIDE";
            return IsNeutral(summary) ? "NO PRICE EFFECT" : summary;
        }

        /// <summary>Plain words for what the live effect means to the player reading it.</summary>
        private static string Consequence(ActiveEventView view, bool aimedAtLocal)
        {
            if (view == null) return "";
            if (IsNeutral(view.EffectSummary))
                return "Texture only — requisition prices are unchanged while it runs.";
            if (!aimedAtLocal)
                return "Aimed at the " + view.Target.ToLowerInvariant() +
                       " — your requisition prices are unchanged.";
            return view.EffectSummary[0] == '+'
                ? "Support requisitions cost more until it ends."
                : "Support requisitions cost less until it ends.";
        }

        private static float MissionTime() =>
            NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0f;

        private static float LocalAllocation() =>
            GameManager.GetLocalPlayer<Player>(out Player player) && player != null ? player.Allocation : 0f;

        private static string MultiplierLabel(float multiplier) =>
            "x" + multiplier.ToString("0.00", CultureInfo.InvariantCulture);

        private static string DirectionLabel(float multiplier, bool active)
        {
            if (!active) return "NOT YOUR SIDE";
            if (multiplier > 1f) return "COST UP";
            if (multiplier < 1f) return "COST DOWN";
            return "FLAT";
        }

        private static string DirectionState(float multiplier, bool active, bool hasEvent)
        {
            if (!hasEvent || !active) return "inert";
            if (multiplier > 1f) return "warn";
            if (multiplier < 1f) return "live";
            return "inert";
        }

        private static string TierShort(string tier)
        {
            if (tier == "SUPEREVENT") return "SUPER";
            if (tier == "MEDIUM") return "MEDIUM";
            return "MINOR";
        }

        private static int CategoryOf(string category)
        {
            if (category == "POLITICAL") return 1;
            if (category == "HAZARD") return 2;
            return 0;
        }

        /// <summary>The narrow column form of the target label, for the history rows.</summary>
        private static string ShortTarget(string target)
        {
            if (target == "HARD-PRESSED SIDE") return "LOSING SIDE";
            if (target == "ALL THEATER") return "ALL THEATER";
            return target;
        }

        private static string TierChipState(ActiveEventView view) =>
            view == null ? "inert"
            : view.Tier == "SUPEREVENT" ? "danger"
            : view.Tier == "MEDIUM" ? "warn"
            : "inert";

        /// <summary>Colour a tier's ink: weather recedes, a medium warns, a super is an alert.</summary>
        private static Color TierInk(string tier) =>
            tier == "SUPEREVENT" ? AvTheme.RailDanger
            : tier == "MEDIUM" ? AvTheme.RailCaution
            : AvTheme.Dim;

        /// <summary>The rail is a state light; a minor event never claims one.</summary>
        private static Color TierRail(string tier) =>
            tier == "SUPEREVENT" ? AvTheme.RailDanger
            : tier == "MEDIUM" ? AvTheme.RailCaution
            : AvTheme.RailInert;

        /// <summary>One line naming the ground war; the numbers live in the metric cell.</summary>
        private static string CustodyLine(TheaterBalance balance) =>
            !balance.Known ? "UNOBSERVED"
            : !balance.Contested ? "NO FRONT-RUNNER"
            : NameOf(balance.LeaderHash) + " AHEAD";

        private static float BalanceFraction(TheaterBalance balance)
        {
            if (!balance.Known) return 0f;
            int total = Mathf.Max(1, balance.LeaderBases + balance.LoserBases + balance.NeutralBases);
            return Mathf.Clamp01(balance.LeaderBases / (float)total);
        }

        /// <summary>Short faction tag from the name hash: the name itself, upper-cased.</summary>
        private static string NameOf(int factionHash)
        {
            if (factionHash == 0) return "—";
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                string name = hq != null && hq.faction != null ? hq.faction.factionName : null;
                if (string.IsNullOrEmpty(name)) continue;
                if (unchecked((int)Deterministic.HashString(name)) == factionHash)
                    return name.ToUpperInvariant();
            }
            return "—";
        }

        private static string GlyphKind(int category) =>
            category == 1 ? EventGlyph.Political
            : category == 2 ? EventGlyph.Hazard
            : EventGlyph.Economic;

        private static bool IsNeutral(string summary) =>
            string.IsNullOrEmpty(summary) || summary == "NO EFFECT";

        /// <summary>The badge colour, and never the only signal: the words say the same.</summary>
        private static Color EffectColor(string summary, bool aimedAtLocal)
        {
            if (!aimedAtLocal || IsNeutral(summary)) return AvTheme.Dim;
            return summary[0] == '+' ? AvTheme.RailDanger : AvTheme.RailReady;
        }

        /// <summary>Millimetre-instrument clock: minutes and seconds, zero padded.</summary>
        private static string Clock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        /// <summary>Compact age/duration; event windows are minutes, not hours, at this scale.</summary>
        private static string Duration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            int minutes = total / 60;
            if (minutes >= 60) return (minutes / 60) + "h " + (minutes % 60) + "m";
            if (minutes > 0) return minutes + "m " + (total % 60) + "s";
            return total + "s";
        }
    }
}
