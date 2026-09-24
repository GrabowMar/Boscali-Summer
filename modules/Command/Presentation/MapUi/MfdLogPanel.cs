using System;
using System.Collections.Generic;
using System.Text;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Mirrors the native kill/game-message streams into the left map column. With an MFD
    /// open it occupies the free area above that screen; with no selection it expands to
    /// the entire reserved panel bay rather than leaving an empty column.
    /// The native MessageUI remains the lifetime and content authority; only its two text
    /// renderers are temporarily hidden, so closing the MFD restores vanilla losslessly.
    /// </summary>
    internal static class MfdLogPanel
    {
        private const string PanelName = "NOAvionics.TacticalLog";
        private const float MinimumHeight = 72f;
        private const float HeaderHeight = 30f;
        private const float RetentionSeconds = 30f;
        private const int MaximumEntries = 120;

        private sealed class Entry
        {
            public string Text;
            public float CapturedAt;
            public float ExpiresAt;
        }

        private static RectTransform panel;
        private static AvStyled.DataBar statusBar;
        private static TMP_Text body;
        private static RectTransform scrollContent;
        private static ScrollRect scroll;
        private static VirtualMFD mfd;
        private static MfdLayout.Columns columns;
        private static TextMeshProUGUI messageSource;
        private static TextMeshProUGUI killSource;
        private static bool originalsHidden;
        private static bool messageWasEnabled;
        private static bool killWasEnabled;
        private static Vector2 builtSize;
        private static bool builtMerged;
        private static float bodyWidth;
        private static float viewportHeight;
        private static readonly Vector3[] corners = new Vector3[4];
        private static readonly List<Entry> history = new List<Entry>();
        private static readonly List<string> previousMessages = new List<string>();
        private static readonly List<string> previousKills = new List<string>();
        private static string lastMessageRaw;
        private static string lastKillRaw;
        internal static event Action<string> OnLineAdded;
        internal static bool HasTraffic => history.Count > 0;

        public static void Ensure(Canvas canvas, MfdLayout.Columns layout, VirtualMFD virtualMfd)
        {
            if (canvas == null || virtualMfd == null || !MapUiAccess.MfdLogAvailable) return;

            if (panel != null && panel.parent != canvas.transform) Restore();

            mfd = virtualMfd;
            columns = layout;
            ResolveSources();

            // Restore may have queued the previous panel for destruction this frame.
            // Only reuse our live reference; a name lookup can resurrect that doomed panel.
            if (panel == null)
            {
                var go = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
                panel = go.GetComponent<RectTransform>();
                panel.SetParent(canvas.transform, worldPositionStays: false);
            }

            Tick();
        }

        public static void Tick()
        {
            if (panel == null || mfd == null) return;

            ResolveSources();
            bool added = CaptureChanges(messageSource == null ? null : messageSource.text, ref lastMessageRaw, previousMessages);
            added |= CaptureChanges(killSource == null ? null : killSource.text, ref lastKillRaw, previousKills);
            bool pruned = PruneHistory();

            if (!DynamicMap.mapMaximized)
            {
                HidePanel();
                return;
            }

            float availableHeight = MfdPanelDock.AvailableHeight(columns.Panel.height);
            float height = availableHeight;
            ReserveScreens(MapUiAccess.GetLeftScreens(mfd), ref height);
            ReserveScreens(MapUiAccess.GetRightScreens(mfd), ref height);
            if (height < MinimumHeight)
            {
                HidePanel();
                return;
            }

            if (messageSource == null && killSource == null)
            {
                HidePanel();
                return;
            }

            bool merged = MfdNewsTicker.IsVisible;
            float topOffset = merged ? MfdNewsTicker.BottomY - columns.Panel.y : 0f;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 1f);
            panel.sizeDelta = new Vector2(columns.Panel.width, height + topOffset);
            panel.anchoredPosition = new Vector2(columns.Panel.x, columns.Panel.y + topOffset);
            panel.localScale = Vector3.one;
            panel.gameObject.SetActive(true);
            Image background = panel.GetComponent<Image>();
            background.sprite = merged ? null : AvSprites.Panel;
            background.type = merged ? Image.Type.Simple : Image.Type.Sliced;
            background.color = merged ? new Color32(10, 14, 18, 235) : Color.white;
            background.raycastTarget = false;
            // The log is always subordinate to the instrument surfaces, including
            // during a resize between layout refreshes.
            Transform dock = panel.parent.Find(MfdPanelDock.DockName);
            if (dock != null && panel.GetSiblingIndex() > dock.GetSiblingIndex())
                panel.SetSiblingIndex(dock.GetSiblingIndex());

            if (!Approximately(builtSize, panel.sizeDelta) || builtMerged != merged || body == null)
                Rebuild(panel.sizeDelta, merged);

            HideOriginals();
            if (added || pruned || string.IsNullOrEmpty(body.text))
            {
                body.text = HistoryText();
                statusBar?.SetChip(0, history.Count > 0 ? "LIVE" : "STANDBY", history.Count > 0);
                ResizeScrollContent(added);
            }
        }

        public static void Restore()
        {
            RestoreOriginals();
            if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
            panel = null;
            statusBar = null;
            body = null;
            scrollContent = null;
            scroll = null;
            mfd = null;
            messageSource = null;
            killSource = null;
            lastMessageRaw = null;
            lastKillRaw = null;
            builtSize = Vector2.zero;
            builtMerged = false;
            bodyWidth = 0f;
            viewportHeight = 0f;
            history.Clear();
            previousMessages.Clear();
            previousKills.Clear();
        }

        public static void Reset() => Restore();

        private static void ResolveSources()
        {
            MessageUI ui = SceneSingleton<MessageUI>.i;
            TextMeshProUGUI nextMessage = MapUiAccess.GetMessageText(ui);
            TextMeshProUGUI nextKill = MapUiAccess.GetKillFeedText(ui);
            if (nextMessage == messageSource && nextKill == killSource) return;

            RestoreOriginals();
            messageSource = nextMessage;
            killSource = nextKill;
            previousMessages.Clear();
            previousKills.Clear();
        }

        private static void ReserveScreens(List<MFDScreen> screens, ref float height)
        {
            if (screens == null) return;
            for (int i = 0; i < screens.Count; i++)
            {
                MFDScreen screen = screens[i];
                if (screen == null || !screen.isActive) continue;
                if (screen.displayPanel == null || screen.displayPanel.gameObject == null ||
                    !screen.displayPanel.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // Use transformed bounds, not a nominal content height: companion
                // panels can resize or scale independently of the dock slot.
                // A controller root is not necessarily visible, but mod screens can
                // paint their frame there outside the display's bounds.
                RectTransform surface = screen.displayPanel.transform as RectTransform;
                bool hasDisplayBounds = ReserveSurface(surface, ref height);
                RectTransform root = screen.transform as RectTransform;
                if (root != null && root != surface && root.gameObject.activeInHierarchy)
                {
                    Graphic frame = root.GetComponent<Graphic>();
                    if (!hasDisplayBounds || (frame != null && frame.isActiveAndEnabled && frame.color.a > 0f))
                        ReserveSurface(root, ref height);
                }
            }
        }

        private static bool ReserveSurface(RectTransform surface, ref float height)
        {
            if (surface == null || surface.rect.width <= 1f || surface.rect.height <= 1f)
                return false;
            surface.GetWorldCorners(corners);
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
            // Columns use the canvas centre; InverseTransformPoint uses its pivot.
            Vector2 centre = ((RectTransform)panel.parent).rect.center;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 point = panel.parent.InverseTransformPoint(corners[i]);
                left = Mathf.Min(left, point.x - centre.x);
                right = Mathf.Max(right, point.x - centre.x);
                top = Mathf.Max(top, point.y - centre.y);
                bottom = Mathf.Min(bottom, point.y - centre.y);
            }
            height = MfdLogSpace.Remaining(height, columns.Panel.x, columns.Panel.y,
                columns.Panel.width, left, right, bottom, top, MfdLayout.Gutter);
            return true;
        }

        private static void Rebuild(Vector2 size, bool merged)
        {
            for (int i = panel.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(panel.GetChild(i).gameObject);

            builtSize = size;
            builtMerged = merged;
            statusBar = null;
            float contentTop = merged ? 0f : HeaderHeight;
            if (merged)
            {
                Color frame = AvTheme.Frame.WithAlpha(0.55f);
                AvKit.Rule(panel, new Rect(0f, 0f, 1f, size.y), frame);
                AvKit.Rule(panel, new Rect(size.x - 1f, 0f, 1f, size.y), frame);
                AvKit.Rule(panel, new Rect(0f, -size.y + 1f, size.x, 1f), frame);
            }
            else
            {
                statusBar = AvStyled.TopBar(panel, new Rect(0f, 0f, size.x, HeaderHeight), "FIELD LOG", 1);
                statusBar.State.text = "TACTICAL EVENT STREAM";
                statusBar.SetChip(0, history.Count > 0 ? "LIVE" : "STANDBY", history.Count > 0);
            }

            float feedHeight = Mathf.Max(0f, size.y - contentTop - 6f);
            AvStyled.Spine(panel, new Rect(3f, -contentTop - 3f, 3f, feedHeight));

            bodyWidth = Mathf.Max(0f, size.x - 24f);
            viewportHeight = feedHeight;

            var scrollGo = new GameObject("EventScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.SetParent(panel, worldPositionStays: false);
            AvKit.Place(scrollRect,
                new Rect(12f, -contentTop - 3f, bodyWidth + 8f, viewportHeight));

            Image scrollHitArea = scrollGo.GetComponent<Image>();
            scrollHitArea.color = Color.clear;
            scrollHitArea.raycastTarget = true;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            var viewport = viewportGo.GetComponent<RectTransform>();
            viewport.SetParent(scrollRect, worldPositionStays: false);
            Stretch(viewport);
            Image viewportImage = viewportGo.GetComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            scrollContent = contentGo.GetComponent<RectTransform>();
            scrollContent.SetParent(viewport, worldPositionStays: false);
            scrollContent.anchorMin = new Vector2(0f, 1f);
            scrollContent.anchorMax = new Vector2(1f, 1f);
            scrollContent.pivot = new Vector2(0.5f, 1f);
            scrollContent.anchoredPosition = Vector2.zero;
            scrollContent.sizeDelta = new Vector2(0f, viewportHeight);

            body = AvStyled.Label(
                scrollContent,
                new Rect(0f, 0f, bodyWidth, viewportHeight),
                "", "row-sub", align: TextAlignmentOptions.TopLeft);
            body.richText = true;
            body.fontSize = 13f;
            body.lineSpacing = 5f;
            body.paragraphSpacing = 4f;
            body.characterSpacing = 0f;
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Overflow;

            scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = scrollContent;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 28f;
            scroll.verticalNormalizedPosition = 1f;
        }

        private static void HideOriginals()
        {
            if (!originalsHidden)
            {
                messageWasEnabled = messageSource != null && messageSource.enabled;
                killWasEnabled = killSource != null && killSource.enabled;
                originalsHidden = true;
            }

            if (messageSource != null) messageSource.enabled = false;
            if (killSource != null) killSource.enabled = false;
        }

        private static void RestoreOriginals()
        {
            if (!originalsHidden) return;
            if (messageSource != null) messageSource.enabled = messageWasEnabled;
            if (killSource != null) killSource.enabled = killWasEnabled;
            originalsHidden = false;
        }

        private static void HidePanel()
        {
            RestoreOriginals();
            if (panel != null) panel.gameObject.SetActive(false);
        }

        private static bool CaptureChanges(string text, ref string lastRaw, List<string> previous)
        {
            if (ReferenceEquals(text, lastRaw) || (text != null && string.Equals(text, lastRaw, StringComparison.Ordinal)))
                return false;
            lastRaw = text;

            List<string> current = SplitLines(text);
            var matched = new bool[current.Count];
            int[,] lcs = new int[previous.Count + 1, current.Count + 1];

            for (int i = 1; i <= previous.Count; i++)
            {
                for (int j = 1; j <= current.Count; j++)
                {
                    lcs[i, j] = string.Equals(previous[i - 1], current[j - 1], StringComparison.Ordinal)
                        ? lcs[i - 1, j - 1] + 1
                        : Mathf.Max(lcs[i - 1, j], lcs[i, j - 1]);
                }
            }

            int oldIndex = previous.Count;
            int newIndex = current.Count;
            while (oldIndex > 0 && newIndex > 0)
            {
                if (string.Equals(previous[oldIndex - 1], current[newIndex - 1], StringComparison.Ordinal))
                {
                    matched[newIndex - 1] = true;
                    oldIndex--;
                    newIndex--;
                }
                else if (lcs[oldIndex - 1, newIndex] >= lcs[oldIndex, newIndex - 1])
                {
                    oldIndex--;
                }
                else
                {
                    newIndex--;
                }
            }

            bool added = false;
            for (int i = current.Count - 1; i >= 0; i--)
            {
                if (matched[i]) continue;
                AddEntry(current[i]);
                added = true;
            }

            previous.Clear();
            previous.AddRange(current);
            return added;
        }

        private static List<string> SplitLines(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length > 0) result.Add(line);
            }
            return result;
        }

        private static void AddEntry(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            float now = Time.unscaledTime;
            for (int i = 0; i < Mathf.Min(4, history.Count); i++)
            {
                if (history[i].Text == text && now - history[i].CapturedAt < 0.5f) return;
            }

            history.Insert(0, new Entry
            {
                Text = text,
                CapturedAt = now,
                ExpiresAt = now + RetentionSeconds,
            });
            while (history.Count > MaximumEntries) history.RemoveAt(history.Count - 1);
            OnLineAdded?.Invoke(text);
        }

        private static bool PruneHistory()
        {
            float now = Time.unscaledTime;
            bool pruned = false;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].ExpiresAt <= now)
                {
                    history.RemoveAt(i);
                    pruned = true;
                }
            }
            return pruned;
        }

        private static string HistoryText()
        {
            if (history.Count == 0)
                return "<color=#" + MfdLogTone.NeutralHex + ">NO ACTIVE TRAFFIC</color>";

            var text = new StringBuilder();
            for (int i = 0; i < history.Count; i++)
            {
                if (i > 0) text.Append('\n');
                string line = history[i].Text;
                string tone = MfdLogTone.Hex(MfdLogTone.Classify(line));
                if (i == 0) text.Append("<mark=#193B475C><b>");
                else if (i > 2) text.Append("<alpha=#A0>");
                text.Append("<color=#").Append(i == 0 ? tone : MfdLogTone.NeutralHex)
                    .Append(">›</color>  ").Append(MfdLogTone.Paint(line));
                if (i == 0) text.Append("</b></mark>");
                else if (i > 2) text.Append("<alpha=#FF>");
            }
            return text.ToString();
        }

        private static void ResizeScrollContent(bool stickToTop)
        {
            if (body == null || scrollContent == null) return;

            float preferred = body.GetPreferredValues(body.text, bodyWidth, 0f).y + 6f;
            float height = Mathf.Max(viewportHeight, preferred);
            scrollContent.sizeDelta = new Vector2(0f, height);
            AvKit.Place(body.transform as RectTransform, new Rect(0f, 0f, bodyWidth, height));

            // Follow new traffic only when the reader is at the live edge. Snapping a
            // scrolled-back reader to the top on every message loses their place.
            if (stickToTop && scroll != null && scroll.verticalNormalizedPosition >= 0.999f)
                scroll.verticalNormalizedPosition = 1f;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private static bool Approximately(Vector2 a, Vector2 b) =>
            Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f;
    }
}
