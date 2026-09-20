using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Events.Configuration;
using BoscaliSummer.Features.Events.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// The superevent broadcast: a full-screen, client-local alert that takes the whole
    /// board for a few seconds when the director escalates. It never blocks play — the
    /// backdrop, art and scrims are all raycast-transparent, so only the dismiss button
    /// intercepts a click and the map's own input underneath is untouched. One alert per
    /// super per peer, including a late joiner who arrives mid-superevent.
    ///
    /// <para>The poster is deliberately dimmed under two scrims: it may be a bright dawn or
    /// a burning depot, and the white type must stay legible over the brightest frame in the
    /// set. Every colour the broadcast uses is a status rail, and every one is accompanied
    /// by the word for it.</para>
    /// </summary>
    internal sealed class SuperEventAlert : MonoBehaviour, ISceneService
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const int MaximumSteps = EventsMfdPanel.MaximumEventSteps;
        private const float FadeInSeconds = 0.18f;
        private const float FadeOutSeconds = 0.3f;
        private const float ArtAlpha = 0.46f;

        // The full-screen broadcast is deliberately larger than any MFD class. These are the
        // token steps scaled for the 1080p reference; ink, alignment, tracking and wrap live in
        // the one table with them, so a role is styled once instead of at every call site.
        private const float DisplaySize = AvTokens.FontTitle * 2.625f;  // 42
        private const float StampSize = AvTokens.FontTitle * 1.375f;    // 22
        private const float SubtitleSize = AvTokens.FontTitle - 1f;     // 15
        private const float BodySize = AvTokens.FontTitle + 2f;         // 18
        private const float StepSize = AvTokens.FontTitle;              // 16
        private const float ClockSize = AvTokens.FontLead + 1f;         // 14

        private readonly struct Face
        {
            public readonly string Classes;
            public readonly float Size;
            public readonly Color Ink;
            public readonly TextAlignmentOptions Align;
            public readonly float Tracking;
            public readonly bool Wrap;

            public Face(string classes, float size, Color ink, TextAlignmentOptions align,
                float tracking, bool wrap = false)
            {
                Classes = classes;
                Size = size;
                Ink = ink;
                Align = align;
                Tracking = tracking;
                Wrap = wrap;
            }
        }

        private static readonly Face StampFace = new Face("page-title", StampSize,
            AvTheme.RailDanger, TextAlignmentOptions.Left, 6f);
        private static readonly Face SubtitleFace = new Face("section-title-note", SubtitleSize,
            AvTheme.Dim, TextAlignmentOptions.Left, 5f);
        private static readonly Face TitleFace = new Face("page-title", DisplaySize,
            AvTheme.TextPrimary, TextAlignmentOptions.Left, 2f);
        private static readonly Face FlavorFace = new Face("status-text", BodySize,
            AvTheme.TextPrimary, TextAlignmentOptions.TopLeft, 0f, wrap: true);
        private static readonly Face NoteFace = new Face("metric-value", BodySize,
            AvTheme.TextPrimary, TextAlignmentOptions.Left, 2f);
        private static readonly Face StepFace = new Face("row-sub", StepSize,
            AvTheme.Dim, TextAlignmentOptions.Left, 1f, wrap: true);
        private static readonly Face ClockFace = new Face("section-title-note", ClockSize,
            AvTheme.Dim, TextAlignmentOptions.Left, 2f);

        private EventsSettings settings;
        private EventsManager events;
        private ManualLogSource logger;

        private GameObject root;
        private CanvasGroup group;
        private EventAlertTone tone;

        private Image art;
        private EventGlyph glyph;
        private readonly List<Image> pulse = new List<Image>(4);
        private TMP_Text stamp;
        private TMP_Text subtitle;
        private TMP_Text title;
        private TMP_Text flavor;
        private TMP_Text note;
        private TMP_Text clock;
        private readonly List<TMP_Text> stepLabels = new List<TMP_Text>(MaximumSteps);
        private Image countdownFill;
        private float countdownWidth;

        private int shownSerial;
        private float openedAt;
        private float closeAt;
        private float duration;
        private float targetAlpha;
        private int lastCategory = -1;
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
            art = null;
            glyph = null;
            stamp = subtitle = title = flavor = note = clock = null;
            countdownFill = null;
            pulse.Clear();
            stepLabels.Clear();
            shownSerial = 0;
            openedAt = closeAt = duration = targetAlpha = 0f;
            lastCategory = -1;
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
                // The fade still runs: an alert already on screen must be able to leave
                // after the player switches alerts or the director off.
                Dismiss();
            }
            else
            {
                bool super = events.Current != null && events.Current.IsSuper;
                if (super && events.SuperSerial != shownSerial)
                {
                    shownSerial = events.SuperSerial;
                    Show(events.Current);
                }
                else if (root != null && root.activeSelf && !super)
                {
                    Dismiss();
                }
            }

            if (root != null && root.activeSelf) FadeAndPulse();
        }

        // ---- Show / hide ------------------------------------------------------------------

        private void Show(ActiveEventView view)
        {
            if (failed || view == null) return;
            if (root == null && !TryBuild()) return;

            root.SetActive(true);
            openedAt = Time.unscaledTime;
            duration = Mathf.Clamp(settings.AlertSeconds.Value, 8f, 60f);
            closeAt = openedAt + duration;
            targetAlpha = 1f;

            // The colour is a status rail; the words say the same state.
            stamp.text = view.Tier.ToUpperInvariant() + "  ·  THEATER ALERT";
            subtitle.text = "EVENT DIRECTOR  ·  " + view.Category + "  ·  ACTIVE";
            title.text = view.Title.ToUpperInvariant();
            flavor.text = view.FlavorText;
            note.text = "EFFECT  " + view.EffectSummary + "   ·   TARGET  " + view.Target;

            int category = CategoryOf(view.Category);
            if (category != lastCategory)
            {
                lastCategory = category;
                glyph.SetKind(category == 1 ? EventGlyph.Political
                    : category == 2 ? EventGlyph.Hazard : EventGlyph.Economic);
            }

            for (int i = 0; i < stepLabels.Count; i++)
            {
                bool used = i < view.Steps.Count;
                stepLabels[i].gameObject.SetActive(used);
                if (used)
                    stepLabels[i].text = "T+" + Mmss(view.Steps[i].AtSeconds) + "   " + view.Steps[i].Label;
            }

            Sprite poster = EventArtCache.Get(view.IconKey, "tier_super");
            art.sprite = poster;
            art.enabled = poster != null;
            glyph.gameObject.SetActive(poster == null);

            tone?.Play();
            logger?.LogInfo("[Events] Superevent alert shown: " + view.Title + ".");
        }

        private void Dismiss()
        {
            if (root != null && root.activeSelf) targetAlpha = 0f;
        }

        private void FadeAndPulse()
        {
            if (group == null) return;
            float step = targetAlpha > group.alpha ? FadeInSeconds : FadeOutSeconds;
            group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, Time.unscaledDeltaTime / step);
            if (targetAlpha <= 0f && group.alpha <= 0.01f)
            {
                root.SetActive(false);
                return;
            }

            float now = Time.unscaledTime;
            if (now >= closeAt) targetAlpha = 0f;

            for (int i = 0; i < pulse.Count; i++)
            {
                Color color = pulse[i].color;
                color.a = 0.65f;
                pulse[i].color = color;
            }

            float remaining = Mathf.Max(0f, closeAt - now);
            if (clock != null) clock.text = "AUTO-DISMISS " + Mmss(Mathf.CeilToInt(remaining));
            if (countdownFill != null)
            {
                float fraction = duration <= 0f ? 0f : Mathf.Clamp01(remaining / duration);
                countdownFill.rectTransform.sizeDelta =
                    new Vector2(countdownWidth * fraction, countdownFill.rectTransform.sizeDelta.y);
            }
        }

        // ---- Build ------------------------------------------------------------------------

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
                logger?.LogError("Superevent alert unavailable: " + e);
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

            var host = new GameObject("BoscaliEvents.Alert",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            root = host;

            Canvas canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29000;
            CanvasScaler scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;

            group = host.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = true;
            group.blocksRaycasts = true;

            var screenObject = new GameObject("Screen", typeof(RectTransform));
            var screen = (RectTransform)screenObject.transform;
            screen.SetParent(host.transform, false);
            AvKit.Stretch(screen);

            // A quiet charcoal instrument card replaces the old full-screen poster treatment.
            // Decoration stays pointer-transparent; only the dismiss action takes input.
            Image backdrop = AvKit.Panel(screen, Full(), new Color(0f, 0f, 0f, 0.68f));
            backdrop.raycastTarget = false;

            Rect alertCard = new Rect(128f, -120f, ReferenceWidth - 256f, ReferenceHeight - 240f);
            Image card = AvKit.Panel(screen, alertCard, new Color32(10, 14, 17, 252));
            card.raycastTarget = false;
            Image[] cardFrame = AvKit.Outline(screen, alertCard, AvTheme.Frame.WithAlpha(0.85f));
            for (int i = 0; i < cardFrame.Length; i++) cardFrame[i].raycastTarget = false;

            var artObject = new GameObject("Art", typeof(RectTransform), typeof(Image));
            art = artObject.GetComponent<Image>();
            art.transform.SetParent(screen, false);
            art.preserveAspect = true;
            art.raycastTarget = false;
            art.color = new Color(1f, 1f, 1f, ArtAlpha);
            Rect artArea = new Rect(1260f, -188f, 440f, 248f);
            AvKit.Place((RectTransform)art.transform, artArea);
            Image artShade = AvKit.Panel(screen, artArea, new Color(0f, 0f, 0f, 0.22f));
            artShade.raycastTarget = false;
            Image[] artFrame = AvKit.Outline(screen, artArea, AvTheme.Frame.WithAlpha(0.65f));
            for (int i = 0; i < artFrame.Length; i++) artFrame[i].raycastTarget = false;

            var glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(EventGlyph));
            var glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.SetParent(screen, false);
            glyph = glyphObject.GetComponent<EventGlyph>();
            AvKit.Place(glyphRect, new Rect(1372f, -204f, 216f, 216f));
            glyph.raycastTarget = false;
            glyph.color = new Color(0.55f, 0.59f, 0.57f, 0.16f);

            Image spine = AvKit.Panel(screen, new Rect(128f, -120f, 6f, ReferenceHeight - 240f),
                AvTheme.RailDanger);
            spine.raycastTarget = false;
            pulse.Add(spine);
            Image[] frame = AvKit.Outline(screen, alertCard, AvTheme.RailDanger.WithAlpha(0.35f));
            for (int i = 0; i < frame.Length; i++)
            {
                frame[i].raycastTarget = false;
                pulse.Add(frame[i]);
            }

            stamp = Label(screen, new Rect(180f, -164f, 900f, 30f), StampFace, "THEATER ALERT");
            subtitle = Label(screen, new Rect(180f, -202f, 900f, 18f), SubtitleFace, "");
            title = Label(screen, new Rect(180f, -236f, 1010f, 58f), TitleFace, "");
            Rule(screen, 180f, 310f, 560f);
            flavor = Label(screen, new Rect(180f, -330f, 980f, 124f), FlavorFace, "");
            note = Label(screen, new Rect(180f, -466f, 1020f, 26f), NoteFace, "");

            for (int i = 0; i < MaximumSteps; i++)
            {
                TMP_Text step = Label(screen, new Rect(184f, -(516f + i * 28f), 980f, 22f), StepFace, "");
                step.gameObject.SetActive(false);
                stepLabels.Add(step);
            }

            // The only interactive element: it stays the last sibling so nothing decorative
            // can ever sit over it.
            AvButton acknowledge = AvStyled.Button(screen, new Rect(180f, -884f, 240f, 40f),
                "DISMISS ALERT", "btn", Dismiss, AvButtonStyle.Primary)
                .WithTooltip("Close the broadcast. The event itself keeps running.");
            acknowledge.transform.SetAsLastSibling();
            clock = Label(screen, new Rect(440f, -884f, 300f, 20f), ClockFace, "AUTO-DISMISS 0:24");

            // Width-driven fill: Unity's filled image type draws a full quad without a
            // sprite, so a fillAmount bar would sit at 100% for the whole alert.
            var barArea = new Rect(128f, -956f, ReferenceWidth - 256f, 4f);
            Image barTrack = AvKit.Panel(screen, barArea, AvTheme.SurfaceInert);
            barTrack.raycastTarget = false;
            AvKit.Outline(screen, barArea, AvTheme.RailDanger.WithAlpha(0.4f));
            countdownFill = AvKit.Panel(screen,
                new Rect(barArea.x, barArea.y, barArea.width, barArea.height), AvTheme.RailDanger);
            countdownFill.raycastTarget = false;
            countdownWidth = barArea.width;

            tone = new EventAlertTone(host.transform);
        }

        private static Rect Full() => new Rect(0f, 0f, ReferenceWidth, ReferenceHeight);

        /// <summary>
        /// Draws one role from the type table above: the class supplies the sheet's baseline,
        /// and the table's size, ink, alignment, tracking and wrap finish it. Call sites only
        /// choose the rectangle and the words.
        /// </summary>
        private static TMP_Text Label(RectTransform parent, Rect area, Face face, string text)
        {
            TMP_Text label = AvStyled.Label(parent, area, text, face.Classes, align: face.Align);
            label.color = face.Ink;
            label.fontSize = face.Size;
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = face.Tracking;
            if (face.Wrap)
            {
                label.enableWordWrapping = true;
                label.overflowMode = TextOverflowModes.Truncate;
            }
            return label;
        }

        private static void Rule(RectTransform parent, float x, float down, float width)
        {
            Image rule = AvKit.Panel(parent, new Rect(x, -down, width, 2f), AvTheme.Accent.WithAlpha(0.7f));
            rule.raycastTarget = false;
        }

        private static int CategoryOf(string category)
        {
            if (category == "POLITICAL") return 1;
            if (category == "HAZARD") return 2;
            return 0;
        }

        private static string Mmss(int seconds)
        {
            int total = Mathf.Max(0, seconds);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }
    }
}
