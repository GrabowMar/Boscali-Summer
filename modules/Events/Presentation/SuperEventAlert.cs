using NOAvionics;
using System;
using BoscaliSummer.Modules.Events.Configuration;
using BoscaliSummer.Modules.Events.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// Client-local MFD battlefield dispatch. It sits below the news wire over the map;
    /// the separate plane HUD branch is reserved for the later HUD overhaul. The footer
    /// names only the next authored order, leaving the full script on EVN. Only dismiss
    /// takes map input. The dispatch folds into a shorter illustrated status strip.
    ///
    /// <para>Chrome is kit v2 (<c>AvFrame</c>/<c>AvText</c>/<c>AvControl</c>, the
    /// <see cref="EventsMfdPanel.EventPlateArt"/> poster plate) with an <c>AvFx</c> dissolve-in on
    /// first show. Kit gap: this overlay is driven by its own <c>Update()</c>, not an
    /// <c>AvTicker</c>, so — like v1 — its palette is fixed at build time rather than
    /// repainting on a live theme switch while a dispatch happens to be on screen.</para>
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

        private EventsSettings settings;
        private EventsManager events;
        private ActiveEventView activeView;

        private GameObject root;
        private CanvasGroup group;
        private EventAlertTone tone;
        private RectTransform expandedPanel;
        private RectTransform compactPanel;
        private CanvasGroup expandedGroup;
        private CanvasGroup compactGroup;
        private AvFx expandedFx;
        private EventsMfdPanel.EventPlateArt plate;
        private EventsMfdPanel.EventPlateArt compactPlate;
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
        private AvHazardGraphic onAir;

        private int shownSerial;
        private int lastOrderSecond = -1;
        private float openedAt;
        private float closeAt;
        private float collapseAt;
        private float duration;
        private float targetAlpha;
        private float layoutWidth;
        private float layoutOffset;
        private bool failed;

        public void Configure(EventsSettings config, EventsManager manager)
        {
            settings = config;
            events = manager;
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
            expandedFx = null;
            plate = compactPlate = null;
            stamp = target = eyebrow = title = scope = impact = flavor = nextOrder = clock = null;
            compactTitle = compactImpact = compactClock = null;
            onAir = null;
            shownSerial = 0;
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

            AvState state = view.EffectSummary.StartsWith("-", StringComparison.Ordinal) ? AvState.Ready : AvState.Danger;
            Color ink = AvStyleHost.FuiColor(AvStates.Class(state), AvTheme.RailDanger);

            stamp.text = "SUPEREVENT / " + view.Category;
            target.text = view.Target;
            eyebrow.text = AvStates.Glyph(AvState.Danger) + "SUPER // LIVE";
            title.text = view.Title.ToUpperInvariant();
            scope.text = "TARGET // " + view.Target.ToUpperInvariant();
            impact.text = view.EffectSummary;
            impact.color = ink;
            flavor.text = view.FlavorText;
            compactTitle.text = view.Title.ToUpperInvariant();
            compactImpact.text = view.EffectSummary;
            compactImpact.color = ink;

            Sprite poster = EventArtCache.Get(view.IconKey, "tier_super");
            AvIcon categoryIcon = EventsMfdPanel.CategoryIcon(view.Category);
            plate.Bind(poster, categoryIcon, ink);
            compactPlate.Bind(poster, categoryIcon, ink);

            expandedPanel.gameObject.SetActive(true);
            compactPanel.gameObject.SetActive(false);
            expandedGroup.alpha = 1f;
            compactGroup.alpha = 0f;
            expandedFx?.Play(AvFxKind.Dissolve, 0.8f, 6f);
            UpdateNextOrder(MissionTime());
            tone?.Play();
            Plugin.Logger?.LogInfo("[Events] Superevent dispatch shown: " + view.Title + ".");
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
            clock.text = "ON AIR " + AvNum.Clock(remaining);
            if (onAir != null) onAir.Value = duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;
            compactClock.text = AvNum.Clock(remaining);
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
                nextOrder.text = "T-" + AvNum.Clock(step.AtSeconds - elapsed) + "   " + step.Label;
                return;
            }
            nextOrder.text = "NO MORE ORDERS";
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
                Plugin.Logger?.LogError("Superevent dispatch unavailable: " + e);
                return false;
            }
        }

        private void Build()
        {
            if (Application.isBatchMode) throw new InvalidOperationException("headless");

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

            Color ground = AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(0.99f);
            Color raised = AvStyleHost.FuiColor("surface-raised", AvTheme.SurfaceRaised);
            Color ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            Color dim = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
            Color danger = AvStyleHost.FuiColor("danger", AvTheme.RailDanger);

            expandedPanel = Panel("MfdDispatch", screen, Top, width, Height, out expandedGroup);
            expandedPanel.anchoredPosition = new Vector2(layoutOffset, -Top);
            AvFrame frame = AvFrame.Add(expandedPanel, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            frame.Paint(ground, danger.WithAlpha(0.65f));
            expandedFx = AvFx.On(frame);

            plate = new EventsMfdPanel.EventPlateArt(expandedPanel, 34f);
            AvLay.Place(plate.Root, 0f, 0f, artWidth, 174f);
            plate.Layout(artWidth, 174f);

            Image stampBack = AvLay.Solid(expandedPanel, "StampBack", raised.WithAlpha(0.9f));
            AvLay.Place(stampBack.rectTransform, 0f, 130f, artWidth, 44f);
            stamp = AvText.Make(expandedPanel, "Stamp", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft);
            stamp.color = danger;
            AvLay.Place(stamp.rectTransform, 14f, 134f, artWidth - 28f, 17f);
            target = AvText.Make(expandedPanel, "Target", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft);
            target.color = ink;
            AvLay.Place(target.rectTransform, 14f, 153f, artWidth - 28f, 17f);

            // The red slab: a solid alert tag, dark ink on the danger fill (never colour alone: glyph plus word).
            Image slab = AvLay.Solid(expandedPanel, "EyebrowSlab", EventsMfdPanel.SlabBack(AvState.Danger));
            AvLay.Place(slab.rectTransform, textX, 13f, Mathf.Min(centreWidth, 190f), 22f);
            eyebrow = AvText.Make(expandedPanel, "Eyebrow", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
            eyebrow.color = EventsMfdPanel.SlabInk();
            AvLay.Place(eyebrow.rectTransform, textX + 8f, 13f, Mathf.Min(centreWidth, 190f) - 12f, 22f);
            title = AvText.Make(expandedPanel, "Title", AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true);
            AvText.Fit(title, true);
            title.color = ink;
            AvLay.Place(title.rectTransform, textX, 39f, centreWidth, 55f);
            Image rule = AvLay.Solid(expandedPanel, "Rule", dim.WithAlpha(.25f));
            AvLay.Place(rule.rectTransform, textX, 116f, centreWidth - 22f, 1f);
            scope = AvText.Make(expandedPanel, "Scope", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft);
            scope.color = dim;
            AvLay.Place(scope.rectTransform, textX, 128f, centreWidth - 22f, 21f);

            Image divider = AvLay.Solid(expandedPanel, "Divider", dim.WithAlpha(.25f));
            AvLay.Place(divider.rectTransform, effectX - 10f, 13f, 1f, 148f);
            TMP_Text effectLabel = AvText.Make(expandedPanel, "EffectLabel", AvTextRole.Micro, "BATTLEFIELD EFFECT", TextAlignmentOptions.TopLeft);
            effectLabel.color = dim;
            AvLay.Place(effectLabel.rectTransform, effectX, 13f, effectWidth, 17f);
            impact = AvText.Make(expandedPanel, "Impact", AvTextRole.Head, "", TextAlignmentOptions.TopLeft, true);
            AvLay.Place(impact.rectTransform, effectX, 39f, effectWidth, 33f);
            flavor = AvText.Make(expandedPanel, "Flavor", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            flavor.color = dim;
            AvLay.Place(flavor.rectTransform, effectX, 82f, effectWidth, 84f);

            Image footer = AvLay.Solid(expandedPanel, "Footer", raised.WithAlpha(0.96f));
            AvLay.Place(footer.rectTransform, 0f, 174f, width, 40f);
            TMP_Text nextLabel = AvText.Make(expandedPanel, "NextLabel", AvTextRole.Micro, "NEXT ORDER", TextAlignmentOptions.TopLeft);
            nextLabel.color = danger;
            AvLay.Place(nextLabel.rectTransform, 14f, 180f, 150f, 14f);
            nextOrder = AvText.Make(expandedPanel, "NextOrder", AvTextRole.Data, "", TextAlignmentOptions.TopLeft);
            nextOrder.color = ink;
            AvLay.Place(nextOrder.rectTransform, 170f, 180f, width - 475f, 24f);
            clock = AvText.Make(expandedPanel, "Clock", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft);
            clock.color = dim;
            AvLay.Place(clock.rectTransform, width - 291f, 181f, 130f, 24f);
            // The dispatch's on-air window as a hazard-stripe bar under the order line: it drains, then the strip folds.
            var barGo = new GameObject("OnAir", typeof(RectTransform), typeof(CanvasRenderer));
            barGo.transform.SetParent(expandedPanel, false);
            onAir = barGo.AddComponent<AvHazardGraphic>();
            onAir.raycastTarget = false;
            onAir.FillColor = danger;
            onAir.FrameColor = dim.WithAlpha(.4f);
            AvLay.Place(onAir.rectTransform, 14f, 206f, width - 175f, 8f);
            AvControl dismiss = AvControl.Make(expandedPanel, new AvControl.Spec("DISMISS", Dismiss, AvButtonStyle.Default, AvIcon.X));
            dismiss.Help = "DISMISS: hide this dispatch. The event carries on; its countdown, price effect and full script stay on the EVN screen. The stripe bar is how long the dispatch stays on air.";
            AvLay.Place(dismiss.Rect, width - 145f, 178f, 131f, 31f);
        }

        private void BuildCompact(RectTransform screen)
        {
            float width = Mathf.Min(900f, layoutWidth);
            Color ground = AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(0.99f);
            Color ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            Color dim = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
            Color danger = AvStyleHost.FuiColor("danger", AvTheme.RailDanger);

            compactPanel = Panel("MfdDispatchBrief", screen, Top, width, CompactHeight, out compactGroup);
            compactPanel.anchoredPosition = new Vector2(layoutOffset - (layoutWidth - width) * .5f, -Top);
            AvFrame frame = AvFrame.Add(compactPanel, "Frame", AvChamfer.Diagonal(4f));
            AvLay.Fill(frame.rectTransform);
            frame.Paint(ground, danger.WithAlpha(0.65f));

            compactPlate = new EventsMfdPanel.EventPlateArt(compactPanel, 20f);
            AvLay.Place(compactPlate.Root, 0f, 0f, 90f, CompactHeight);
            compactPlate.Layout(90f, CompactHeight);

            compactTitle = AvText.Make(compactPanel, "CompactTitle", AvTextRole.Head, "", TextAlignmentOptions.TopLeft);
            compactTitle.color = ink;
            AvLay.Place(compactTitle.rectTransform, 106f, 6f, width - 286f, 21f);
            compactImpact = AvText.Make(compactPanel, "CompactImpact", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft);
            AvLay.Place(compactImpact.rectTransform, 106f, 28f, width - 286f, 18f);
            compactClock = AvText.Make(compactPanel, "CompactClock", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
            compactClock.color = dim;
            AvLay.Place(compactClock.rectTransform, width - 172f, 12f, 112f, 27f);
            AvControl compactDismiss = AvControl.Make(compactPanel,
                new AvControl.Spec("", Dismiss, AvButtonStyle.Quiet, AvIcon.X));
            compactDismiss.Help = "Dismiss this dispatch; its event and effects remain active on EVN.";
            AvLay.Place(compactDismiss.Rect, width - 46f, 10f, 32f, 30f);
            compactPanel.gameObject.SetActive(false);
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

        private static float MissionTime() =>
            NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0f;
    }
}
