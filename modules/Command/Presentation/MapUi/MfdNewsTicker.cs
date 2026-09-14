using System;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Runtime;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Scrolling theater news marquee ("Theater Wire / WarNet") positioned directly
    /// above the central tactical map.
    ///
    /// Presents a continuously scrolling stream of diegetic military LARP worldbuilding
    /// alongside dynamic frontline bulletins (captured airbases, nuclear strikes, ace defeats).
    /// Built with twin-label continuous looping for zero-jitter, garbage-free scrolling.
    /// </summary>
    internal static class MfdNewsTicker
    {
        private const string RootName = "NOAvionics.NewsTicker";
        private const string ChromeName = "Chrome";
        private const string ViewportName = "Viewport";
        private const float DefaultHeight = 30f;
        private const float BadgeWidth = 124f;
        private const float LoopGap = 64f;
        private const float DefaultSpeed = 45f;
        private const float UrgentFlashSeconds = 8f;

        private static CommandSettings settings;
        private static RectTransform root;
        private static RectTransform chrome;
        private static RectTransform viewport;
        private static RectTransform contentA;
        private static RectTransform contentB;
        private static TMP_Text labelA;
        private static TMP_Text labelB;
        private static TMP_Text badgeLabel;
        private static MfdGlyph badgeDot;
        private static Image alertRail;
        private static Vector2 builtSize;

        private static readonly MfdNewsFeed feed = new MfdNewsFeed();
        private static float xOffset;
        private static float textWidth;
        private static float totalCycleDistance;
        private static string currentMarqueeString = "";
        private static bool subscribedToLog;
        private static float lastSeenUrgentTime = -1f;
        private static float alertUntil;
        private static bool alertVisualsClear;

        public static void Configure(CommandSettings config)
        {
            settings = config;
        }

        public static void Ensure(Canvas canvas, MfdLayout.Columns columns, CommandSettings config = null)
        {
            if (canvas == null) return;
            if (config != null) settings = config;
            if (settings != null && !settings.NewsTickerEnabled.Value)
            {
                Restore();
                return;
            }

            if (!subscribedToLog)
            {
                MfdLogPanel.OnLineAdded += OnLogLineAdded;
                subscribedToLog = true;
            }

            Rect area = ResolveArea(columns);
            if (area.width <= 1f || area.height <= 1f) return;

            EnsureRoot(canvas);
            if (root == null) return;

            PlaceRoot(area);

            if (!Approximately(builtSize, area.size) || labelA == null)
            {
                Rebuild(area.size);
            }

            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
        }

        public static void Tick()
        {
            if (root == null || !DynamicMap.mapMaximized)
            {
                if (root != null && root.gameObject.activeSelf)
                    root.gameObject.SetActive(false);
                return;
            }

            if (settings != null && !settings.NewsTickerEnabled.Value)
            {
                Restore();
                return;
            }

            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);

            float now = Time.unscaledTime;

            // Periodic theater status updates from CommandManager
            PollTheaterState();
            HandleFreshUrgent(now);
            UpdateAlertVisuals(now);

            // Advance marquee scrolling
            float speed = settings != null ? settings.NewsTickerSpeed.Value : DefaultSpeed;
            if (speed <= 0f) speed = DefaultSpeed;

            float dt = Time.unscaledDeltaTime;
            xOffset -= speed * dt;

            if (totalCycleDistance > 0f && xOffset <= -totalCycleDistance)
            {
                xOffset += totalCycleDistance;
                feed.OnMarqueeCycleComplete();
                RefreshMarqueeText();
            }

            if (contentA != null) contentA.anchoredPosition = new Vector2(xOffset, 0f);
            if (contentB != null) contentB.anchoredPosition = new Vector2(xOffset + totalCycleDistance, 0f);
        }

        public static void Restore()
        {
            if (subscribedToLog)
            {
                MfdLogPanel.OnLineAdded -= OnLogLineAdded;
                subscribedToLog = false;
            }

            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            chrome = null;
            viewport = null;
            contentA = null;
            contentB = null;
            labelA = null;
            labelB = null;
            badgeLabel = null;
            badgeDot = null;
            alertRail = null;
            builtSize = Vector2.zero;
            xOffset = 0f;
            textWidth = 0f;
            totalCycleDistance = 0f;
            currentMarqueeString = "";
            alertVisualsClear = false;
        }

        public static void Reset()
        {
            Restore();
            feed.Clear();
        }

        private static Rect ResolveArea(MfdLayout.Columns columns)
        {
            float top = columns.Canvas.y * 0.5f - MfdLayout.Margin;
            float height = DefaultHeight;
            return new Rect(
                -columns.Canvas.x * 0.5f + MfdLayout.Margin,
                top,
                columns.Canvas.x - MfdLayout.Margin * 2f,
                height);
        }

        private static void EnsureRoot(Canvas canvas)
        {
            if (root != null && root.parent != canvas.transform)
            {
                Restore();
            }

            if (root == null)
            {
                Transform existing = canvas.transform.Find(RootName);
                root = existing as RectTransform;
            }

            if (root == null)
            {
                var go = new GameObject(RootName, typeof(RectTransform), typeof(Image));
                root = go.GetComponent<RectTransform>();
                root.SetParent(canvas.transform, worldPositionStays: false);
            }

            Image background = root.GetComponent<Image>();
            if (background == null) background = root.gameObject.AddComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = AvTheme.Ground.WithAlpha(0.94f);
            background.raycastTarget = false;
        }

        private static void PlaceRoot(Rect area)
        {
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = area.size;
            root.anchoredPosition = area.position;
            root.localScale = Vector3.one;
        }

        private static void Rebuild(Vector2 size)
        {
            builtSize = size;

            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(root.GetChild(i).gameObject);

            // 1. Chrome / Borders
            var chromeGo = new GameObject(ChromeName, typeof(RectTransform));
            chrome = chromeGo.GetComponent<RectTransform>();
            chrome.SetParent(root, worldPositionStays: false);
            AvKit.Stretch(chrome);

            var area = new Rect(0f, 0f, size.x, size.y);
            AvKit.Outline(chrome, area, AvTheme.Frame.WithAlpha(0.55f));
            AvKit.CornerTicks(chrome, area, AvTheme.Hairline.WithAlpha(0.70f), 4f);
            alertRail = AvKit.Rule(chrome, new Rect(0f, -size.y + 2f, size.x, 2f), AvTheme.Accent.WithAlpha(0.30f));

            // Vertical divider separating badge from news viewport
            AvKit.Rule(chrome, new Rect(BadgeWidth, 0f, 1f, size.y), AvTheme.Frame.WithAlpha(0.60f));

            // 2. Left Badge
            var badgeGo = new GameObject("Badge", typeof(RectTransform));
            var badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.SetParent(root, worldPositionStays: false);
            AvKit.Place(badgeRt, new Rect(8f, 0f, BadgeWidth - 12f, size.y));

            var dotObject = new GameObject("Dot", typeof(RectTransform), typeof(MfdGlyph));
            var dotRect = dotObject.GetComponent<RectTransform>();
            dotRect.SetParent(badgeRt, worldPositionStays: false);
            AvKit.Place(dotRect, new Rect(2f, -(size.y - 10f) * 0.5f, 10f, 10f));
            badgeDot = dotObject.GetComponent<MfdGlyph>();
            badgeDot.raycastTarget = false;
            badgeDot.SetKind("dot", AvTheme.Accent);

            badgeLabel = AvStyled.Label(
                badgeRt,
                new Rect(18f, 0f, BadgeWidth - 30f, size.y),
                "<b>THEATER WIRE</b>",
                "row-sub",
                align: TextAlignmentOptions.MidlineLeft);
            badgeLabel.fontSize = 12f;
            badgeLabel.richText = true;

            // 3. Masked Viewport
            float viewportX = BadgeWidth + 10f;
            float viewportWidth = Mathf.Max(0f, size.x - viewportX - 8f);

            var viewportGo = new GameObject(ViewportName, typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport = viewportGo.GetComponent<RectTransform>();
            viewport.SetParent(root, worldPositionStays: false);
            AvKit.Place(viewport, new Rect(viewportX, 0f, viewportWidth, size.y));

            Image viewImg = viewportGo.GetComponent<Image>();
            viewImg.color = Color.clear;
            viewImg.raycastTarget = false;

            // 4. Scrolling Labels A and B
            var goA = new GameObject("MarqueeTextA", typeof(RectTransform));
            contentA = goA.GetComponent<RectTransform>();
            contentA.SetParent(viewport, worldPositionStays: false);
            contentA.anchorMin = contentA.anchorMax = contentA.pivot = new Vector2(0f, 0.5f);
            contentA.sizeDelta = new Vector2(4000f, size.y);
            contentA.anchoredPosition = Vector2.zero;

            labelA = AvStyled.Label(
                contentA,
                new Rect(0f, 0f, 4000f, size.y),
                "",
                "row-sub",
                align: TextAlignmentOptions.MidlineLeft);
            labelA.fontSize = 13f;
            labelA.color = AvTheme.TextPrimary;
            labelA.characterSpacing = 0f;
            labelA.richText = true;
            labelA.enableWordWrapping = false;
            labelA.overflowMode = TextOverflowModes.Overflow;

            var goB = new GameObject("MarqueeTextB", typeof(RectTransform));
            contentB = goB.GetComponent<RectTransform>();
            contentB.SetParent(viewport, worldPositionStays: false);
            contentB.anchorMin = contentB.anchorMax = contentB.pivot = new Vector2(0f, 0.5f);
            contentB.sizeDelta = new Vector2(4000f, size.y);
            contentB.anchoredPosition = Vector2.zero;

            labelB = AvStyled.Label(
                contentB,
                new Rect(0f, 0f, 4000f, size.y),
                "",
                "row-sub",
                align: TextAlignmentOptions.MidlineLeft);
            labelB.fontSize = 13f;
            labelB.color = AvTheme.TextPrimary;
            labelB.characterSpacing = 0f;
            labelB.richText = true;
            labelB.enableWordWrapping = false;
            labelB.overflowMode = TextOverflowModes.Overflow;

            xOffset = 0f;
            RefreshMarqueeText();
        }

        private static void RefreshMarqueeText()
        {
            if (labelA == null || labelB == null) return;

            currentMarqueeString = feed.BuildMarqueeText(6);
            labelA.text = currentMarqueeString;
            labelB.text = currentMarqueeString;

            labelA.ForceMeshUpdate();
            textWidth = labelA.preferredWidth;
            totalCycleDistance = textWidth + LoopGap;

            if (contentA != null) contentA.sizeDelta = new Vector2(textWidth + 20f, builtSize.y);
            if (contentB != null) contentB.sizeDelta = new Vector2(textWidth + 20f, builtSize.y);
        }

        private static void OnLogLineAdded(string line)
        {
            float now = Time.unscaledTime;
            if (!feed.IngestGameEvent(line, now)) return;
            HandleFreshUrgent(now);
        }

        /// <summary>
        /// A breaking headline jumps the marquee back to the badge so it is read immediately,
        /// and arms a short alert pulse on the badge dot and rail. Named events keep flowing
        /// without rewinding the scroll.
        /// </summary>
        private static void HandleFreshUrgent(float now)
        {
            if (feed.LastUrgentTime <= lastSeenUrgentTime) return;
            lastSeenUrgentTime = feed.LastUrgentTime;
            alertUntil = now + UrgentFlashSeconds;
            xOffset = 0f;
            RefreshMarqueeText();
        }

        private static void UpdateAlertVisuals(float now)
        {
            bool alert = now < alertUntil;
            if (!alert)
            {
                if (alertVisualsClear) return;
                alertVisualsClear = true;
                if (badgeDot != null) badgeDot.color = AvTheme.Accent;
                if (alertRail != null) alertRail.color = AvTheme.Accent.WithAlpha(0.30f);
                return;
            }

            alertVisualsClear = false;
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 7f);
            if (badgeDot != null)
            {
                badgeDot.color = Color.Lerp(AvTheme.Accent, AvTheme.Alert, 0.35f + 0.65f * pulse);
            }
            if (alertRail != null)
            {
                alertRail.color = Color.Lerp(AvTheme.Frame, AvTheme.Alert, 0.45f + 0.55f * pulse)
                    .WithAlpha(0.55f + 0.45f * pulse);
            }
        }

        private static void PollTheaterState()
        {
            var cm = CommandManager.Active;
            if (cm == null || cm.TheaterState == null) return;

            var state = cm.TheaterState;
            feed.UpdateTheaterStatus(
                Time.unscaledTime,
                state.DefconLevel,
                state.TerritoryControlRatio,
                state.AirSuperiorityRatio,
                state.ActiveClashesCount,
                state.ContestedAirbaseCount);
        }

        private static bool Approximately(Vector2 a, Vector2 b) =>
            Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f;
    }
}
