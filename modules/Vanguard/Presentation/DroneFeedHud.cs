using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Vanguard.Domain;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// The drone feed card on the HUD (bottom right, the bottom of the feed column: the HUD's target and missile cards stack on it through
    /// <see cref="FeedCorner"/>): the linked drone's video and symbology while it is hand-flown, or whenever the
    /// pilot switched HUD FEED on. Same size and chrome as the HUD's MSL and TGT cards (<see cref="FeedCorner.Frame"/>, the HUD theme's ink and the
    /// native weapon font; 2026-10-20 rework). It stands down while the UAV terminal is open or the map is maximised (the UAV MFD shows the feed
    /// there). Client-local, never takes input.
    /// </summary>
    internal sealed class DroneFeedHud : MonoBehaviour, ISceneService
    {
        private const float Margin = 24f;
        private const int SortOrder = 120;

        private DroneLinkService link;
        private GameObject root;
        private RectTransform card;
        private AvVector chrome;
        private TMP_Text tagWord, head, mode;
        private DroneFeedView feed;
        private float lostFlashUntil, cardH;
        private bool wasLost;
        private Color lastInk;
        private readonly Vector3[] corners = new Vector3[4];

        public void Configure(DroneLinkService service) => link = service;

        public void ResetForScene()
        {
            FeedCorner.Uav = default;
            if (root != null) Destroy(root);
            root = null;
            feed = null;
            wasLost = false;
        }

        private void OnDestroy() => ResetForScene();

        private void LateUpdate()
        {
            if (link == null || Application.isBatchMode) return;
            bool lost = link.Lost;
            if (lost && !wasLost) lostFlashUntil = Time.unscaledTime + 1.2f;
            wasLost = lost;
            bool show = link.HasAircraft && !link.TerminalOpen && !DynamicMap.mapMaximized &&
                (link.Linked != null || lost) && (link.Linked?.Mode == DroneMode.Hand || link.FeedShown || lost);
            if (!show)
            {
                FeedCorner.Uav = default;
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }
            if (root == null) Build();
            if (!root.activeSelf) root.SetActive(true);
            // The bottom of the feed column, every view.
            card.GetWorldCorners(corners);
            FeedCorner.Uav = new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
            ColorTheme theme = ThemeManager.Active.ColorTheme;
            feed.Ink = theme.AllClear;
            feed.Paint(link);

            DroneView v = link.Linked;
            bool flash = Time.unscaledTime < lostFlashUntil && ((int)(Time.unscaledTime * 6f) & 1) == 0;
            Color ink = theme.AllClear;
            Color tone = lost || (v != null && v.Quality <= 0f) ? theme.Alert : v != null && v.Mode == DroneMode.Hand ? theme.Warning : ink;
            AvText.Set(head, v != null ? v.Callsign : link.LostName);
            AvText.Set(mode, lost ? "LOST" : v == null ? "" : DroneLink.Word(v.Mode));
            tagWord.color = head.color = ink;
            mode.color = tone;
            Color frameInk = flash ? theme.Alert : ink;
            if (frameInk != lastInk)
            {
                lastInk = frameInk;
                AvQuadBuffer b = chrome.Buffer;
                b.Clear();
                FeedCorner.Frame(b, 0f, 0f, FeedCorner.Width, cardH, frameInk.ToRgba());
                chrome.Commit();
            }
        }

        private void Build()
        {
            // A scene object like the other HUD canvases: it goes with the scene, before the font materials unload at quit.
            root = new GameObject("BoscaliUavFeedCard", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortOrder;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            float w = FeedCorner.Width, feedH = Mathf.Round(w * 9f / 16f);
            cardH = FeedCorner.Header + feedH;
            card = AvLay.Child((RectTransform)root.transform, "Card");
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 0f);
            card.anchoredPosition = new Vector2(-Margin, Margin);
            card.sizeDelta = new Vector2(w, cardH);
            feed = new DroneFeedView(card, "Feed", 10f);
            feed.Place(new AvSlot(0f, FeedCorner.Header, w, feedH));
            chrome = AvVector.Create(card, "Chrome", 64);
            lastInk = Color.clear;
            tagWord = Word("Tag", 6f, 30f, TextAlignmentOptions.MidlineLeft);
            tagWord.text = "UAV";
            tagWord.fontStyle = FontStyles.Bold;
            head = Word("Head", 38f, w - 38f - 64f, TextAlignmentOptions.MidlineLeft);
            mode = Word("Mode", w - 62f, 60f, TextAlignmentOptions.MidlineRight);
        }

        // The HUD's own weapon font when the cockpit style is known, the avionics data face otherwise.
        private TMP_Text Word(string name, float x, float width, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(card, name, AvTextRole.DataSmall, "", align);
            if (VanillaHudStyle.TryCockpit(out VanillaHudStyle.CockpitStyle style) && style.Font != null)
            {
                t.font = style.Font;
                if (style.FontMaterial != null) t.fontSharedMaterial = style.FontMaterial;
            }
            AvText.Size(t, 12f);
            t.raycastTarget = false;
            AvLay.Place(t, x, 0f, width, FeedCorner.Header - 3f);
            return t;
        }
    }
}
