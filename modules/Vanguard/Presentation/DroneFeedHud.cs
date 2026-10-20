using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// The drone feed card on the HUD (bottom right): the linked drone's video and symbology while it is hand-flown, or whenever the
    /// pilot switched HUD FEED on, with the way to switch drones printed under it. It stands down while the UAV terminal is open or the
    /// map is maximised (the UAV MFD shows the feed there). Client-local, never takes input.
    /// </summary>
    internal sealed class DroneFeedHud : MonoBehaviour, ISceneService
    {
        private const float CardW = 432f, HeadH = 22f, HintH = 20f, Margin = 24f, Lift = 132f;
        private const int SortOrder = 120;

        private DroneLinkService link;
        private GameObject root;
        private RectTransform card;
        private AvFrame frame;
        private TMP_Text head, hint;
        private DroneFeedView feed;
        private float lostFlashUntil;
        private bool wasLost;

        public void Configure(DroneLinkService service) => link = service;

        public void ResetForScene()
        {
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
                (link.Linked != null || lost) && (link.HandFlying || link.FeedShown || lost);
            if (!show)
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }
            if (root == null) Build();
            if (!root.activeSelf) root.SetActive(true);
            feed.Paint(link);
            DroneView v = link.Linked;
            AvState tone = lost || (v != null && v.Quality <= 0f) ? AvState.Danger : v != null && v.Mode == DroneMode.Hand ? AvState.Caution : AvState.Info;
            AvText.Set(head, v != null ? "UAV FEED · " + v.Callsign + " · " + (v.Mode == DroneMode.Hand ? "HAND CONTROL" : DroneLink.Word(v.Mode)) : "UAV FEED · " + link.LostName);
            head.color = AvInk.State(tone);
            AvText.Set(hint, link.Count > 1 ? "C › WEAPONS › DRONE LINK · NEXT / PREV / RELEASE" : "C › WEAPONS › DRONE LINK · RELEASE");
            bool flash = Time.unscaledTime < lostFlashUntil && ((int)(Time.unscaledTime * 6f) & 1) == 0;
            frame.Paint(AvInk.Ground.WithAlpha(0.88f), flash ? AvInk.State(AvState.Danger) : AvInk.State(tone).WithAlpha(0.9f));
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

            float feedH = Mathf.Round(CardW * 9f / 16f), cardH = HeadH + feedH + HintH + 8f;
            card = AvLay.Child((RectTransform)root.transform, "Card");
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 0f);
            card.anchoredPosition = new Vector2(-Margin, Lift);
            card.sizeDelta = new Vector2(CardW + 8f, cardH);
            frame = AvFrame.Add(card, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            frame.raycastTarget = false;
            head = AvText.Make(card, "Head", AvTextRole.Head, "");
            AvText.Fit(head, false);
            head.raycastTarget = false;
            AvLay.Place(head, 10f, 2f, CardW - 12f, HeadH);
            feed = new DroneFeedView(card, "Feed", 12f);
            feed.Place(new AvSlot(4f, HeadH + 2f, CardW, feedH));
            hint = AvText.Make(card, "Hint", AvTextRole.Micro, "");
            hint.color = AvInk.Dim;
            hint.raycastTarget = false;
            AvLay.Place(hint, 10f, HeadH + feedH + 4f, CardW - 12f, HintH);
        }
    }
}
