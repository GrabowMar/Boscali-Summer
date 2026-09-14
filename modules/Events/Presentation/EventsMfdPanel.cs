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
    /// "EVN" — the world-event feed. The active event is pinned at the top with its live
    /// countdown, remaining-time bar and the one decision this module offers: spend
    /// allocation to contain a penalty or deepen a discount for the rest of its run. The
    /// mission history follows in reverse order. A calm theater says so instead of
    /// rendering an empty list, and the modifier badge only names a support cost change
    /// when one is actually in force.
    /// </summary>
    internal sealed class EventsMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.25f;

        private const int ChipCount = 3;
        private const int TabEvents = 0;

        private const float ActiveCardHeight = 162f;
        private const float HistoryCardHeight = 108f;
        private const float CardGap = 6f;

        private EventsSettings settings;
        private EventsManager events;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvScreen shell;

        private EventCard activeCard;
        private TMP_Text historyNote;
        private readonly List<EventCard> historyCards = new List<EventCard>(16);

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
            MfdBezel.Release(MfdSlots.Events);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);

            screenRoot = null;
            screen = null;
            shell = null;
            activeCard = null;
            historyNote = null;
            historyCards.Clear();
            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || events == null || settings == null || !settings.Enabled.Value) return;
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
            if (visible && Time.unscaledTime >= nextRefresh)
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

                if (!MfdBezel.TryClaim(MfdSlots.Events, preferLeft: false, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    // A full bezel is a crowded screen, not a broken mod: the rest still installs.
                    failed = true;
                    logger?.LogWarning("EVN MFD unavailable: no free bezel slot.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdBezel.Release(MfdSlots.Events);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdBezel.Release(MfdSlots.Events);
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
                    new[] { "SUPPORT COST", "ALLOCATION" },
                    new[] { "EVENTS RUN", "THIS MISSION" },
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
            float contentHeight = 24f + ActiveCardHeight + CardGap + 22f + 20f +
                                  capacity * (HistoryCardHeight + CardGap) + 12f;

            Rect body = shell.Body;
            RectTransform parent = AvScreen.Scroll((RectTransform)page.transform, body, contentHeight, out body);
            float x = body.x + AvScreen.SpineInset;
            float width = body.width - AvScreen.SpineInset;
            float y = body.y;

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            y = SectionHeader(parent, x, y, width, "ACTIVE EVENT", "LIVE MODIFIER", band: false);
            activeCard = new EventCard(parent, x, y, width, ActiveCardHeight, withResponse: true);
            y -= ActiveCardHeight + CardGap;

            y = SectionHeader(parent, x, y, width, "MISSION HISTORY",
                              "MOST RECENT FIRST", band: true);
            historyNote = AvStyled.Label(parent, new Rect(x, y, width, 16f),
                "NO COMPLETED EVENTS YET — THE FEED ROLLS ONE EVENT AT A TIME.",
                "row-sub");
            y -= 20f;

            historyCards.Clear();
            for (int i = 0; i < capacity; i++)
            {
                historyCards.Add(new EventCard(parent, x, y - i * (HistoryCardHeight + CardGap),
                    width, HistoryCardHeight, withResponse: false));
            }
        }

        private static float SectionHeader(
            RectTransform parent, float x, float y, float width, string title, string note, bool band)
        {
            if (band) AvStyled.Box(parent, new Rect(x - 6f, y + 4f, width + 12f, 22f), "section band");
            AvStyled.SpineTick(parent, x - AvScreen.SpineInset + 3f, y - 7f);

            float titleWidth = width * 0.5f;
            AvStyled.Label(parent, new Rect(x, y, titleWidth, 14f), title, "section-title");

            if (!string.IsNullOrEmpty(note))
            {
                AvStyled.Label(parent, new Rect(x + titleWidth, y, width - titleWidth, 14f),
                               note, "section-title-note", align: TextAlignmentOptions.MidlineRight);
            }
            return y - 22f;
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

            shell.DataBar.State.text = current != null ? current.Title
                : events.Available ? "NO ACTIVE WORLD EVENT" : "WAITING FOR A RUNNING MISSION.";
            shell.DataBar.State.color = current != null ? AvTheme.RailCaution : AvTheme.Dim;
            shell.DataBar.SetChip(0, current != null ? ShortCategory(current.Category) : "CALM",
                                  current != null ? "warn" : "inert");
            shell.DataBar.SetChip(1, DirectionLabel(multiplier, current != null),
                                  DirectionState(multiplier, current != null));
            shell.DataBar.SetChip(2, history.Count + "/" + capacity + " LOGGED",
                                  history.Count > 0 ? "live" : "inert");

            shell.Metrics[0].Set(
                current != null ? MultiplierLabel(multiplier) : "—",
                current != null ? summary : "NO ACTIVE MODIFIER",
                current != null ? Mathf.Clamp01(Mathf.Abs(multiplier - 1f)) : 0f,
                EffectColor(summary));

            int logged = history.Count + (current != null ? 1 : 0);
            shell.Metrics[1].Set(
                events.Available ? logged.ToString() : "—",
                current != null ? "1 ACTIVE · " + history.Count + " ENDED" : history.Count + " ENDED",
                capacity <= 0 ? 0f : Mathf.Clamp01(history.Count / (float)capacity),
                logged > 0 ? AvTheme.RailInfo : AvTheme.RailInert);

            if (current != null)
            {
                activeCard.Bind(current, "ENDS IN " + Duration(current.EndsAtMissionTime - now),
                                EffectColor(summary), GlyphKind(current.Category));
                float span = Mathf.Max(1f, current.EndsAtMissionTime - current.StartedAtMissionTime);
                activeCard.SetProgress(
                    Mathf.Clamp01((current.EndsAtMissionTime - now) / span), EffectColor(summary));
                RefreshResponse(multiplier, summary);
            }
            else
            {
                activeCard.BindPlaceholder(events.Available
                    ? "The theater is quiet. A new event will surface without warning."
                    : "No mission is running on this host.");
                activeCard.SetProgress(0f, AvTheme.RailInert);
                activeCard.HideResponse();
            }

            for (int i = 0; i < capacity; i++)
            {
                int source = history.Count - 1 - i;
                if (source < 0)
                {
                    historyCards[i].Hide();
                    continue;
                }

                ActiveEventView view = history[source];
                historyCards[i].Bind(view, "ENDED " + Duration(now - view.EndsAtMissionTime) + " AGO",
                                     EffectColor(view.EffectSummary), GlyphKind(view.Category));
            }

            historyNote.gameObject.SetActive(history.Count == 0);

            string ambient = current != null
                ? "WORLD EVENT: " + current.Title + " · " + current.EffectSummary
                : events.Available ? "No active world event." : "No running mission.";
            shell.WriteStatus(events.Signal, MapPicker.Prompt, ambient);
        }

        /// <summary>The one decision: mitigate a penalty, or deepen a discount. Pay to own it.</summary>
        private void RefreshResponse(float multiplier, string summary)
        {
            EventResponseKind response = events.LocalResponse;
            if (response != EventResponseKind.None)
            {
                activeCard.SetResponse(
                    response == EventResponseKind.Contain ? "PENALTY CONTAINED" : "DISCOUNT DEEPENED",
                    "RESPONSE ACTIVE · " + summary,
                    enabled: false, tooltip: null, onClick: null);
                return;
            }
            if (!events.CanRespond)
            {
                activeCard.HideResponse();
                return;
            }

            int cost = events.ResponseCost;
            bool affordable = LocalAllocation() + 0.001f >= cost;
            EventResponseKind kind = multiplier > 1f ? EventResponseKind.Contain : EventResponseKind.Leverage;
            string label = (kind == EventResponseKind.Contain ? "CONTAIN" : "LEVERAGE") + " · " + cost + " ALLOC";
            string tooltip = kind == EventResponseKind.Contain
                ? "Spend " + cost + " allocation to halve this event's support-cost penalty for the rest of its run."
                : "Spend " + cost + " allocation to deepen this event's support-cost discount for the rest of its run.";
            string note = affordable
                ? kind == EventResponseKind.Contain ? "HALVES THE PENALTY" : "DEEPENS THE DISCOUNT"
                : "INSUFFICIENT ALLOCATION";
            activeCard.SetResponse(label, note, affordable, tooltip, events.RequestResponse);
        }

        private static float MissionTime() =>
            NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0f;

        private static float LocalAllocation() =>
            GameManager.GetLocalPlayer<Player>(out Player player) && player != null ? player.Allocation : 0f;

        private static string MultiplierLabel(float multiplier) => "x" + multiplier.ToString("0.00");

        private static string DirectionLabel(float multiplier, bool active)
        {
            if (!active) return "NO EVENT";
            if (multiplier > 1f) return "COST UP";
            if (multiplier < 1f) return "COST DOWN";
            return "FLAT";
        }

        private static string DirectionState(float multiplier, bool active)
        {
            if (!active) return "inert";
            if (multiplier > 1f) return "warn";
            if (multiplier < 1f) return "live";
            return "inert";
        }

        private static bool IsNeutral(string summary) =>
            string.IsNullOrEmpty(summary) || summary == "NO EFFECT";

        /// <summary>The badge colour, and never the only signal: the badge text says the same.</summary>
        private static Color EffectColor(string summary)
        {
            if (IsNeutral(summary)) return AvTheme.Dim;
            return summary[0] == '+' ? AvTheme.RailDanger : AvTheme.RailReady;
        }

        private static string ShortCategory(string category)
        {
            switch (category)
            {
                case "POLITICAL": return "POL";
                case "HAZARD": return "HAZ";
                default: return "ECO";
            }
        }

        private static string GlyphKind(string category)
        {
            switch (category)
            {
                case "POLITICAL": return EventGlyph.Political;
                case "HAZARD": return EventGlyph.Hazard;
                default: return EventGlyph.Economic;
            }
        }

        /// <summary>Compact countdown; event windows are minutes, not hours, at this scale.</summary>
        private static string Duration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            int minutes = total / 60;
            if (minutes >= 60) return (minutes / 60) + "h " + (minutes % 60) + "m";
            if (minutes > 0) return minutes + "m";
            return total + "s";
        }

        // ---- Card ------------------------------------------------------------------------

        private sealed class EventCard
        {
            private const float TextX = 76f;

            private readonly GameObject root;
            private readonly Image rail;
            private readonly EventGlyph glyph;
            private readonly TMP_Text title;
            private readonly TMP_Text category;
            private readonly TMP_Text flavor;
            private readonly Image badgeFill;
            private readonly Image[] badgeFrame;
            private readonly TMP_Text badge;
            private readonly TMP_Text stamp;
            private readonly AvButton action;
            private readonly TMP_Text actionNote;
            private readonly Image progressFill;

            public EventCard(RectTransform parent, float x, float y, float width, float height, bool withResponse)
            {
                root = new GameObject("EventCard", typeof(RectTransform));
                var rect = (RectTransform)root.transform;
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, height));
                AvStyled.Box(rect, new Rect(0f, 0f, width, height), "card");
                AvKit.CornerTicks(rect, new Rect(0f, 0f, width, height), AvTheme.Frame.WithAlpha(0.5f));

                rail = AvStyled.Rail(rect, new Rect(4f, -8f, 3f, height - 16f), "locked");

                Rect frame = new Rect(14f, -10f, 48f, 48f);
                AvKit.Panel(rect, frame, AvTheme.SurfaceInert);
                AvKit.Outline(rect, frame, AvTheme.Frame.WithAlpha(0.6f));

                var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(EventGlyph));
                glyphObject.transform.SetParent(rect, false);
                glyph = glyphObject.GetComponent<EventGlyph>();
                AvKit.Place(glyph.rectTransform, new Rect(frame.x + 12f, frame.y - 12f, 24f, 24f));
                glyph.raycastTarget = false;

                const float categoryWidth = 90f;
                const float gap = 8f;
                float textWidth = width - TextX - 14f;
                float titleWidth = Mathf.Max(0f, textWidth - categoryWidth - gap);
                title = AvStyled.Label(rect, new Rect(TextX, -9f, titleWidth, 18f), "", "row-name");
                category = AvStyled.Label(rect, new Rect(width - 14f - categoryWidth, -9f, categoryWidth, 14f), "",
                                          "section-title-note", align: TextAlignmentOptions.MidlineRight);
                flavor = AvStyled.Label(rect, new Rect(TextX, -28f, textWidth, 46f), "", "row-sub");

                Rect badgeArea = new Rect(TextX, -78f, 168f, 18f);
                badgeFill = AvKit.Panel(rect, badgeArea, new Color(0f, 0f, 0f, 0.35f));
                badgeFrame = AvKit.Outline(rect, badgeArea, AvTheme.Hairline);
                badge = AvStyled.Label(rect, badgeArea, "", "chip", align: TextAlignmentOptions.Center);
                stamp = AvStyled.Label(rect, new Rect(width - 184f, -78f, 170f, 16f), "",
                                       "kv-value", align: TextAlignmentOptions.MidlineRight);

                if (!withResponse) return;

                float actionY = -104f;
                action = AvStyled.Button(rect, new Rect(TextX, actionY, 190f, 24f), "", "btn", null);
                actionNote = AvStyled.Label(
                    rect, new Rect(TextX + 200f, actionY, textWidth - 200f, 24f), "", "row-sub");
                progressFill = AvKit.ProgressBar(
                    rect, new Rect(14f, -(height - 14f), width - 28f, 4f), 0f, AvTheme.RailInert);
            }

            public void Bind(ActiveEventView view, string stampText, Color tint, string glyphKind)
            {
                if (!root.activeSelf) root.SetActive(true);
                glyph.gameObject.SetActive(true);
                glyph.SetKind(glyphKind);
                glyph.color = tint;

                title.text = view.Title.ToUpperInvariant();
                category.text = view.Category;
                flavor.text = view.FlavorText;
                badge.text = view.EffectSummary;
                badge.color = tint;
                badgeFill.color = tint.WithAlpha(0.12f);
                for (int i = 0; i < badgeFrame.Length; i++) badgeFrame[i].color = tint.WithAlpha(0.45f);
                rail.color = RailColor(tint);
                stamp.text = stampText;
                stamp.color = AvTheme.Dim;
            }

            public void BindPlaceholder(string note)
            {
                if (!root.activeSelf) root.SetActive(true);
                glyph.gameObject.SetActive(true);
                glyph.SetKind(EventGlyph.Economic);
                glyph.color = AvTheme.Dim;

                title.text = "NO ACTIVE EVENT";
                category.text = "WORLD";
                flavor.text = note;
                badge.text = "NO EFFECT";
                badge.color = AvTheme.Dim;
                badgeFill.color = AvTheme.Dim.WithAlpha(0.12f);
                for (int i = 0; i < badgeFrame.Length; i++) badgeFrame[i].color = AvTheme.Hairline;
                rail.color = AvTheme.RailInert;
                stamp.text = "";
            }

            public void SetProgress(float fraction, Color color)
            {
                if (progressFill == null) return;
                progressFill.fillAmount = Mathf.Clamp01(fraction);
                progressFill.color = color;
            }

            public void SetResponse(string label, string note, bool enabled, string tooltip, Action onClick)
            {
                if (action == null) return;
                if (!action.gameObject.activeSelf) action.gameObject.SetActive(true);
                action.SetText(label);
                action.SetEnabled(enabled);
                action.SetAction(enabled ? onClick : null);
                action.WithTooltip(tooltip);
                actionNote.text = note ?? "";
                actionNote.color = enabled ? AvTheme.Dim : AvTheme.RailCaution;
            }

            public void HideResponse()
            {
                if (action == null) return;
                if (action.gameObject.activeSelf) action.gameObject.SetActive(false);
                actionNote.text = "";
            }

            public void Hide() => root.SetActive(false);

            private static Color RailColor(Color tint) =>
                tint == AvTheme.Dim ? AvTheme.RailInert
                : tint == AvTheme.RailDanger ? AvTheme.RailDanger
                : AvTheme.RailReady;
        }
    }
}
