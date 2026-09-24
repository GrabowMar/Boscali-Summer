using System;
using BepInEx.Logging;
using BoscaliSummer.Features.Events.Configuration;
using BoscaliSummer.Features.Events.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// Client-local MFD battlefield dispatch. It sits below the news wire over the map;
    /// the separate plane HUD branch is reserved for the later HUD overhaul. The footer
    /// names only the next authored order, leaving the full script on EVN. Only dismiss
    /// takes map input. The dispatch folds into a shorter illustrated status strip.
    /// </summary>
    internal sealed class SuperEventAlert : MonoBehaviour, ISceneService
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const float Top = 38f;
        private const float Height = 214f;
        private const float CompactHeight = 50f;
        private const float FadeInSeconds = 0.18f;
        private const float FadeOutSeconds = 0.28f;
        private const float CollapseSeconds = 0.32f;
        private const float ExpandSeconds = 8f;

        private static readonly Color Ink = new Color32(11, 15, 19, 252);
        private static readonly Color FooterInk = new Color32(22, 28, 33, 255);
        private static readonly Color Paper = new Color32(239, 242, 241, 255);
        private static readonly Color Muted = new Color32(180, 194, 199, 255);
        private static readonly Color Signal = new Color32(244, 83, 70, 255);
        private static readonly Color Relief = new Color32(111, 219, 191, 255);

        private EventsSettings settings;
        private EventsManager events;
        private ManualLogSource logger;
        private ActiveEventView activeView;

        private GameObject root;
        private CanvasGroup group;
        private EventAlertTone tone;
        private RectTransform expandedPanel;
        private RectTransform compactPanel;
        private CanvasGroup expandedGroup;
        private CanvasGroup compactGroup;
        private Image art;
        private Image stripes;
        private Image compactArt;
        private Image compactStripes;
        private EventGlyph glyph;
        private EventGlyph compactGlyph;
        private TMP_Text stamp;
        private TMP_Text target;
        private TMP_Text eyebrow;
        private TMP_Text title;
        private TMP_Text scope;
        private TMP_Text impact;
        private TMP_Text flavor;
        private TMP_Text nextOrder;
        private TMP_Text clock;
        private TMP_Text compactTitle;
        private TMP_Text compactImpact;
        private TMP_Text compactClock;

        private int shownSerial;
        private int lastOrderSecond = -1;
        private float openedAt;
        private float closeAt;
        private float collapseAt;
        private float duration;
        private float targetAlpha;
        private float layoutWidth;
        private float layoutOffset;
        private int planeNotifiedSerial;
        private bool failed;

        public void Configure(EventsSettings config, EventsManager manager, ManualLogSource log)
        {
            settings = config;
            events = manager;
            logger = log;
        }

        public void ResetForScene()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            group = null;
            tone = null;
            activeView = null;
            expandedPanel = compactPanel = null;
            expandedGroup = compactGroup = null;
            art = stripes = compactArt = compactStripes = null;
            glyph = compactGlyph = null;
            stamp = target = eyebrow = title = scope = impact = flavor = nextOrder = clock = null;
            compactTitle = compactImpact = compactClock = null;
            shownSerial = 0;
            planeNotifiedSerial = 0;
            lastOrderSecond = -1;
            openedAt = closeAt = collapseAt = duration = targetAlpha = 0f;
            failed = false;
            EventArtCache.Clear();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            bool live = settings != null && events != null &&
                        settings.Enabled.Value && settings.AlertsEnabled.Value;
            if (!live)
            {
                Dismiss();
            }
            else
            {
                bool super = events.Current != null && events.Current.IsSuper;
                if (super && events.SuperSerial != planeNotifiedSerial)
                {
                    planeNotifiedSerial = events.SuperSerial;
                    SuperEventPlaneHud.OnSuperEvent(events.Current);
                }
                if (super && events.SuperSerial != shownSerial && DynamicMap.mapMaximized)
                {
                    shownSerial = events.SuperSerial;
                    Show(events.Current);
                }
                else if (!super)
                {
                    Dismiss();
                }
            }

            if (root != null && !DynamicMap.mapMaximized)
                root.SetActive(false);
            else if (root != null && !root.activeSelf && activeView != null &&
                     targetAlpha > 0f && Time.unscaledTime < closeAt)
                root.SetActive(true);
            if (root != null && root.activeSelf) Tick();
        }

        private void Show(ActiveEventView view)
        {
            if (failed || view == null) return;
            if (root == null && !TryBuild()) return;

            root.SetActive(true);
            activeView = view;
            openedAt = Time.unscaledTime;
            duration = Mathf.Clamp(settings.AlertSeconds.Value, 8f, 60f);
            closeAt = openedAt + duration;
            collapseAt = openedAt + Mathf.Min(duration * 0.5f, ExpandSeconds);
            targetAlpha = 1f;
            lastOrderSecond = -1;

            stamp.text = "SUPEREVENT  /  " + view.Category;
            target.text = view.Target;
            eyebrow.text = "THEATER DISPATCH  /  LIVE";
            title.text = view.Title.ToUpperInvariant();
            scope.text = "DIRECTED TO  /  " + view.Target.ToUpperInvariant();
            impact.text = view.EffectSummary;
            flavor.text = view.FlavorText;
            compactTitle.text = view.Title.ToUpperInvariant();
            compactImpact.text = view.EffectSummary;

            Color consequence = view.EffectSummary.StartsWith("-", StringComparison.Ordinal)
                ? Relief : Signal;
            impact.color = consequence;
            compactImpact.color = consequence;

            string kind = view.Category == "POLITICAL" ? EventGlyph.Political
                : view.Category == "HAZARD" ? EventGlyph.Hazard
                : EventGlyph.Economic;
            glyph.SetKind(kind);
            compactGlyph.SetKind(kind);

            Sprite poster = EventArtCache.Get(view.IconKey, "tier_super");
            art.sprite = compactArt.sprite = poster;
            art.enabled = compactArt.enabled = poster != null;
            stripes.gameObject.SetActive(poster == null);
            compactStripes.gameObject.SetActive(poster == null);
            glyph.gameObject.SetActive(poster == null);
            compactGlyph.gameObject.SetActive(poster == null);

            expandedPanel.gameObject.SetActive(true);
            compactPanel.gameObject.SetActive(false);
            expandedGroup.alpha = 1f;
            compactGroup.alpha = 0f;
            UpdateNextOrder(MissionTime());
            tone?.Play();
            logger?.LogInfo("[Events] Superevent dispatch shown: " + view.Title + ".");
        }

        private void Dismiss()
        {
            targetAlpha = 0f;
            if (root == null || !root.activeSelf) activeView = null;
        }

        private void Tick()
        {
            float now = Time.unscaledTime;
            if (now >= closeAt) targetAlpha = 0f;
            float fade = targetAlpha > group.alpha ? FadeInSeconds : FadeOutSeconds;
            group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, Time.unscaledDeltaTime / fade);
            if (targetAlpha <= 0f && group.alpha <= 0.01f)
            {
                root.SetActive(false);
                activeView = null;
                return;
            }

            float remaining = Mathf.Max(0f, closeAt - now);
            clock.text = "ON AIR  " + Mmss(Mathf.CeilToInt(remaining));
            compactClock.text = Mmss(Mathf.CeilToInt(remaining));
            UpdateNextOrder(MissionTime());

            float t = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((now - collapseAt) / CollapseSeconds));
            expandedPanel.gameObject.SetActive(t < 1f);
            expandedGroup.alpha = 1f - t;
            expandedPanel.anchoredPosition = new Vector2(layoutOffset, -Top - 12f * t);
            compactPanel.gameObject.SetActive(t > 0f);
            compactGroup.alpha = t;
        }

        private void UpdateNextOrder(float missionTime)
        {
            if (activeView == null || nextOrder == null) return;
            int elapsed = Mathf.Max(0, Mathf.FloorToInt(missionTime - activeView.StartedAtMissionTime));
            if (elapsed == lastOrderSecond) return;
            lastOrderSecond = elapsed;

            for (int i = 0; i < activeView.Steps.Count; i++)
            {
                ActiveEventStep step = activeView.Steps[i];
                if (step.AtSeconds <= elapsed) continue;
                nextOrder.text = "T-" + Mmss(step.AtSeconds - elapsed) + "   " + step.Label;
                return;
            }
            nextOrder.text = "NO FURTHER FIELD ORDERS";
        }

        private bool TryBuild()
        {
            try
            {
                Build();
                return root != null;
            }
            catch (Exception e)
            {
                failed = true;
                if (root != null) UnityEngine.Object.Destroy(root);
                root = null;
                logger?.LogError("Superevent dispatch unavailable: " + e);
                return false;
            }
        }

        private void Build()
        {
            if (Application.isBatchMode) throw new InvalidOperationException("headless");
            if (AvFont.Font == null)
            {
                TMP_Text probe = UnityEngine.Object.FindObjectOfType<TMP_Text>(true);
                if (probe != null) AvFont.Font = probe.font;
            }

            var host = new GameObject("BoscaliEvents.Dispatch",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            root = host;
            Canvas canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29000;
            CanvasScaler scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            group = host.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = true;
            group.blocksRaycasts = true;

            float canvasWidth = Mathf.Max(ReferenceWidth,
                Screen.width / Mathf.Max(.01f, canvas.scaleFactor));
            layoutWidth = Mathf.Min(1260f, canvasWidth - 64f);
            // Keep the dispatch inside the tactical map at the 1920 reference size.
            float left = Mathf.Min(500f, Mathf.Max(24f, canvasWidth - layoutWidth - 160f));
            layoutOffset = left + layoutWidth * .5f - canvasWidth * .5f;

            BuildExpanded((RectTransform)host.transform);
            BuildCompact((RectTransform)host.transform);
            tone = new EventAlertTone(host.transform);
        }

        private void BuildExpanded(RectTransform screen)
        {
            float width = layoutWidth;
            float artWidth = Mathf.Clamp(width * .235f, 240f, 300f);
            float textX = artWidth + 20f;
            float textWidth = width - textX - 18f;
            float centreWidth = textWidth * .48f;
            float effectX = textX + centreWidth + 18f;
            float effectWidth = width - effectX - 18f;

            expandedPanel = Panel("MfdDispatch", screen, Top, width, Height, out expandedGroup);
            expandedPanel.anchoredPosition = new Vector2(layoutOffset, -Top);
            AvKit.Panel(expandedPanel, new Rect(0f, 0f, width, Height), Ink);
            AvKit.Rule(expandedPanel, new Rect(0f, 0f, width, 2f), Signal.WithAlpha(.65f));

            BuildArt(expandedPanel, new Rect(0f, 0f, artWidth, 174f),
                out art, out stripes, out glyph);
            AvKit.Panel(expandedPanel, new Rect(0f, -130f, artWidth, 44f),
                new Color(0.02f, 0.04f, 0.06f, 0.9f));
            stamp = Label(expandedPanel, new Rect(14f, -134f, artWidth - 28f, 17f), "", 10f, Signal, 2f);
            target = Label(expandedPanel, new Rect(14f, -153f, artWidth - 28f, 17f), "", 13f, Paper, 1f);

            eyebrow = Label(expandedPanel, new Rect(textX, -13f, centreWidth, 19f), "", 10f, Signal, 2f);
            title = Label(expandedPanel, new Rect(textX, -39f, centreWidth, 55f), "", 29f, Paper, 1f);
            title.enableAutoSizing = true;
            title.fontSizeMin = 20f;
            title.fontSizeMax = 29f;
            AvKit.Rule(expandedPanel, new Rect(textX, -116f, centreWidth - 22f, 1f),
                Muted.WithAlpha(.25f));
            scope = Label(expandedPanel, new Rect(textX, -128f, centreWidth - 22f, 21f),
                "", 11f, Muted, 1f);
            AvKit.Rule(expandedPanel, new Rect(effectX - 10f, -13f, 1f, 148f),
                Muted.WithAlpha(.25f));
            Label(expandedPanel, new Rect(effectX, -13f, effectWidth, 17f),
                "BATTLEFIELD EFFECT", 10f, Muted, 2f);
            impact = Label(expandedPanel, new Rect(effectX, -39f, effectWidth, 33f), "", 23f, Signal, 1f);
            flavor = Label(expandedPanel, new Rect(effectX, -82f, effectWidth, 84f), "", 13f, Muted,
                wrap: true);

            AvKit.Panel(expandedPanel, new Rect(0f, -174f, width, 40f), FooterInk);
            Label(expandedPanel, new Rect(14f, -180f, 150f, 14f),
                "NEXT ORDER", 10f, Signal, 2f);
            nextOrder = Label(expandedPanel, new Rect(170f, -180f, width - 475f, 24f), "", 13f, Paper, 1f);
            clock = Label(expandedPanel, new Rect(width - 291f, -181f, 130f, 24f), "", 12f, Muted, 1f);
            AvStyled.Button(expandedPanel, new Rect(width - 145f, -178f, 131f, 31f), "DISMISS", "btn",
                Dismiss, AvButtonStyle.Default)
                .WithTooltip("Hide this dispatch. The event continues on the battlefield.");
        }

        private void BuildCompact(RectTransform screen)
        {
            float width = Mathf.Min(900f, layoutWidth);
            compactPanel = Panel("MfdDispatchBrief", screen, Top,
                width, CompactHeight, out compactGroup);
            compactPanel.anchoredPosition = new Vector2(layoutOffset - (layoutWidth - width) * .5f, -Top);
            AvKit.Panel(compactPanel, new Rect(0f, 0f, width, CompactHeight), Ink);
            AvKit.Rule(compactPanel, new Rect(0f, 0f, width, 2f), Signal.WithAlpha(.65f));
            BuildArt(compactPanel, new Rect(0f, 0f, 90f, CompactHeight),
                out compactArt, out compactStripes, out compactGlyph);
            compactTitle = Label(compactPanel, new Rect(106f, -6f, width - 244f, 21f), "", 16f, Paper, 1f);
            compactImpact = Label(compactPanel, new Rect(106f, -28f, width - 244f, 18f), "", 12f, Signal, 1f);
            compactClock = Label(compactPanel, new Rect(width - 130f, -12f, 112f, 27f), "", 14f,
                Muted, 1f, align: TextAlignmentOptions.MidlineRight);
            compactPanel.gameObject.SetActive(false);
        }

        private static void BuildArt(RectTransform parent, Rect area,
            out Image poster, out Image pattern, out EventGlyph mark)
        {
            AvKit.Panel(parent, area, FooterInk);
            pattern = MakeImage(parent, area, "FallbackPattern");
            pattern.sprite = EventPlate.Stripes();
            pattern.type = UnityEngine.UI.Image.Type.Tiled;
            pattern.color = Signal.WithAlpha(0.12f);
            poster = MakeImage(parent, area, "Poster");
            poster.color = Color.white;

            var glyphObject = new GameObject("CategoryMark", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(EventGlyph));
            glyphObject.transform.SetParent(parent, false);
            mark = glyphObject.GetComponent<EventGlyph>();
            mark.color = Signal.WithAlpha(0.8f);
            float size = Mathf.Min(area.width, area.height) * 0.3f;
            AvKit.Place((RectTransform)glyphObject.transform,
                new Rect(area.x + (area.width - size) * 0.5f,
                    area.y - (area.height - size) * 0.5f, size, size));
        }

        private static Image MakeImage(RectTransform parent, Rect area, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            AvKit.Place((RectTransform)go.transform, area);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform Panel(string name, RectTransform screen, float top,
            float width, float height, out CanvasGroup canvasGroup)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(screen, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(width, height);
            canvasGroup = go.GetComponent<CanvasGroup>();
            return rect;
        }

        private static TMP_Text Label(RectTransform parent, Rect area, string text, float size,
            Color color, float tracking = 0f, bool wrap = false,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            TMP_Text label = AvKit.Label(parent, text, area, color, size, FontStyles.Bold, align, wrap);
            label.richText = false;
            label.characterSpacing = tracking;
            return label;
        }

        private static string Mmss(int seconds)
        {
            int total = Mathf.Max(0, seconds);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        private static float MissionTime() =>
            NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0f;
    }
}
