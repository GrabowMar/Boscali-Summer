using System;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Runtime;
using NOAvionics;
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
        private const float MinimumBadgeWidth = 124f;
        private const float LoopGap = 64f;
        private const float DefaultSpeed = 45f;
        private const float UrgentFlashSeconds = 8f;
        private const float HeadlineHoldSeconds = 1.5f;

        private static CommandSettings settings;
        private static RectTransform root;
        private static RectTransform chrome;
        private static RectTransform viewport;
        private static RectTransform contentA;
        private static RectTransform contentB;
        private static TMP_Text labelA;
        private static TMP_Text labelB;
        private static TMP_Text badgeLabel;
        private static TMP_Text deskLabel;
        private static TMP_Text badgeIcon;
        private static Image alertRail;
        private static Vector2 builtSize;
        private static float builtBadgeWidth;

        private static readonly MfdNewsFeed feed = new MfdNewsFeed();
        private static float xOffset;
        private static float textWidth;
        private static float totalCycleDistance;
        private static string currentMarqueeString = "";
        private static bool subscribedToLog;
        private static float lastSeenUrgentTime = -1f;
        private static float alertUntil;
        private static float holdUntil;
        private static bool alertVisualsClear;
        private static bool lastLogLive;

        internal static bool IsVisible => root != null && root.gameObject.activeSelf;
        internal static float BottomY => root == null ? 0f : root.anchoredPosition.y - root.sizeDelta.y;

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

            float badgeWidth = Mathf.Min(columns.Panel.width, Mathf.Max(MinimumBadgeWidth, area.width - 180f));
            if (!Approximately(builtSize, area.size) || Mathf.Abs(builtBadgeWidth - badgeWidth) >= 0.5f || labelA == null)
            {
                Rebuild(area.size, badgeWidth);
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

            bool logLive = MfdLogPanel.HasTraffic;
            if (deskLabel != null && logLive != lastLogLive)
            {
                deskLabel.text = logLive ? "FIELD LOG / LIVE" : "FIELD LOG / STANDBY";
                lastLogLive = logLive;
            }

            float now = Time.unscaledTime;

            // Periodic theater status updates from CommandManager
            PollTheaterState();
            HandleFreshUrgent(now);
            UpdateAlertVisuals(now);

            // Advance marquee scrolling
            float speed = settings != null ? settings.NewsTickerSpeed.Value : DefaultSpeed;
            if (speed <= 0f) speed = DefaultSpeed;

            float dt = Time.unscaledDeltaTime;
            if (now >= holdUntil) xOffset -= speed * dt;

            if (totalCycleDistance > 0f && xOffset <= -totalCycleDistance)
            {
                xOffset += totalCycleDistance;
                feed.OnMarqueeCycleComplete();
                RefreshMarqueeText();
                holdUntil = now + HeadlineHoldSeconds;
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
            deskLabel = null;
            badgeIcon = null;
            alertRail = null;
            builtSize = Vector2.zero;
            builtBadgeWidth = 0f;
            xOffset = 0f;
            textWidth = 0f;
            totalCycleDistance = 0f;
            currentMarqueeString = "";
            alertVisualsClear = false;
            holdUntil = 0f;
            lastLogLive = false;
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
                var go = new GameObject(RootName, typeof(RectTransform));
                root = go.GetComponent<RectTransform>();
                root.SetParent(canvas.transform, worldPositionStays: false);
            }
            // The chrome child (built in Rebuild) carries the one background/frame mesh;
            // root itself is a plain layout node.
        }

        private static void PlaceRoot(Rect area)
        {
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = area.size;
            root.anchoredPosition = area.position;
            root.localScale = Vector3.one;
        }

        private static void Rebuild(Vector2 size, float badgeWidth)
        {
            builtSize = size;
            builtBadgeWidth = badgeWidth;

            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(root.GetChild(i).gameObject);

            // 1. Chrome / Borders — one AvFrame mesh for the backing fill and the frame stroke.
            var chromeGo = new GameObject(ChromeName, typeof(RectTransform));
            chrome = chromeGo.GetComponent<RectTransform>();
            chrome.SetParent(root, worldPositionStays: false);
            AvLay.Fill(chrome, 0f);

            var area = new Rect(0f, 0f, size.x, size.y);
            MfdChromeLay.Panel(chrome, "Backing", area,
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(0.94f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame).WithAlpha(0.55f),
                AvChamfer.Diagonal(6f));
            alertRail = MfdChromeLay.Rule(chrome, "AlertRail",
                new Rect(badgeWidth, -size.y + 2f, size.x - badgeWidth, 2f),
                AvStyleHost.FuiColor("select", AvTheme.Accent).WithAlpha(0.30f));

            // Align the wire's left bay with the Field Log directly beneath it.
            MfdChromeLay.Rule(chrome, "BadgeDivider", new Rect(badgeWidth, 0f, 1f, size.y),
                AvStyleHost.FuiColor("frame", AvTheme.Frame).WithAlpha(0.60f));

            // 2. Left Badge
            var badgeGo = new GameObject("Badge", typeof(RectTransform));
            var badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.SetParent(root, worldPositionStays: false);
            MfdChromeLay.Place(badgeRt, new Rect(8f, 0f, badgeWidth - 12f, size.y));

            badgeIcon = AvIcons.Make(badgeRt, AvIcon.Antenna, 10f, AvStyleHost.FuiColor("select", AvTheme.Accent));
            MfdChromeLay.Place(badgeIcon.rectTransform, new Rect(2f, -(size.y - 10f) * 0.5f, 10f, 10f));

            badgeLabel = AvText.Make(badgeRt, "Badge", AvTextRole.Label, "THEATER WIRE", TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(badgeLabel.rectTransform, new Rect(18f, 0f, 112f, size.y));
            badgeLabel.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);

            lastLogLive = MfdLogPanel.HasTraffic;
            deskLabel = AvText.Make(badgeRt, "Desk", AvTextRole.Micro,
                lastLogLive ? "FIELD LOG / LIVE" : "FIELD LOG / STANDBY", TextAlignmentOptions.MidlineRight);
            MfdChromeLay.Place(deskLabel.rectTransform, new Rect(badgeWidth - 145f, 0f, 112f, size.y));
            deskLabel.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);

            // 3. Masked Viewport
            float viewportX = badgeWidth + 10f;
            float viewportWidth = Mathf.Max(0f, size.x - viewportX - 8f);

            var viewportGo = new GameObject(ViewportName, typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport = viewportGo.GetComponent<RectTransform>();
            viewport.SetParent(root, worldPositionStays: false);
            MfdChromeLay.Place(viewport, new Rect(viewportX, 0f, viewportWidth, size.y));

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

            // Rich, domain-authored copy — Prose keeps its case (spec §5.2) rather than
            // being forced upper like the chrome roles around it.
            labelA = AvText.Make(contentA, "MarqueeTextA", AvTextRole.Prose, "", TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(labelA.rectTransform, new Rect(0f, 0f, 4000f, size.y));
            labelA.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            labelA.enableWordWrapping = false;
            labelA.overflowMode = TextOverflowModes.Overflow;

            var goB = new GameObject("MarqueeTextB", typeof(RectTransform));
            contentB = goB.GetComponent<RectTransform>();
            contentB.SetParent(viewport, worldPositionStays: false);
            contentB.anchorMin = contentB.anchorMax = contentB.pivot = new Vector2(0f, 0.5f);
            contentB.sizeDelta = new Vector2(4000f, size.y);
            contentB.anchoredPosition = Vector2.zero;

            labelB = AvText.Make(contentB, "MarqueeTextB", AvTextRole.Prose, "", TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(labelB.rectTransform, new Rect(0f, 0f, 4000f, size.y));
            labelB.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
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
            holdUntil = now + HeadlineHoldSeconds;
            xOffset = 0f;
            RefreshMarqueeText();
        }

        private static void UpdateAlertVisuals(float now)
        {
            bool alert = now < alertUntil;
            Color ready = AvStyleHost.FuiColor("select", AvTheme.Accent);
            if (!alert)
            {
                if (alertVisualsClear) return;
                alertVisualsClear = true;
                if (badgeIcon != null) badgeIcon.color = ready;
                if (alertRail != null) alertRail.color = ready.WithAlpha(0.30f);
                if (badgeLabel != null) badgeLabel.text = "THEATER WIRE";
                return;
            }

            alertVisualsClear = false;
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 7f);
            Color danger = AvStyleHost.FuiColor("danger", AvTheme.Alert);
            if (badgeIcon != null)
            {
                badgeIcon.color = Color.Lerp(ready, danger, 0.35f + 0.65f * pulse);
            }
            if (alertRail != null)
            {
                alertRail.color = Color.Lerp(AvStyleHost.FuiColor("frame", AvTheme.Frame), danger, 0.45f + 0.55f * pulse)
                    .WithAlpha(0.55f + 0.45f * pulse);
            }
            // R1: the colour pulse never carries the alert alone — the badge also gets the word/glyph.
            if (badgeLabel != null) badgeLabel.text = AvStates.Glyph(AvState.Danger) + "THEATER WIRE";
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
