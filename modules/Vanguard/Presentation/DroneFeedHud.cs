using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// The drone feed card on the HUD (bottom right, the bottom of the feed column: the HUD's target and missile cards stack on it through
    /// <see cref="FeedCorner"/>): the selected drone's video and symbology while HUD FEED is on.
    /// The DRONES panel shows the feed when the map is maximised.
    /// </summary>
    internal sealed class DroneFeedHud : MonoBehaviour, ISceneService
    {
        private const float Margin = 24f;
        private const int SortOrder = 120;

        private DroneLinkService link;
        private GameObject root;
        private RectTransform card;
        private AvVector chrome;
        private TMP_Text nameText, stateText;
        private readonly TMP_Text[] cells = new TMP_Text[3];
        private readonly int[] cellKeys = { int.MinValue, int.MinValue, int.MinValue };
        private string lastCallsign, lastWord, state;
        private DroneFeedView feed;
        private float lostFlashUntil, cardH;
        private bool wasLost, inLayer;
        private Color lastInk;
        private readonly Vector3[] corners = new Vector3[4];

        public void Configure(DroneLinkService service) => link = service;

        public void ResetForScene()
        {
            FeedCorner.Uav = default;
            feed?.Dispose();
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
            bool show = !ForeignMods.HudOverlaysYielded && link.Own != null && !DynamicMap.mapMaximized &&
                !link.Piloting && link.FeedShown && (link.Linked != null || lost);
            if (!show)
            {
                FeedCorner.Uav = default;
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }
            bool layer = HudLayer.Enabled && HudLayer.Ensure();
            if (root != null && layer != inLayer) { Destroy(root); root = null; }      // the A/B switch moved: rebuild on the other parent
            if (root == null) Build(layer);
            if (!root.activeSelf) root.SetActive(true);
            if (inLayer) Fit();
            // The bottom of the feed column, every view.
            card.GetWorldCorners(corners);
            FeedCorner.Uav = new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
            feed.Ink = VanillaHudStyle.Ink;
            feed.Paint(link);

            DroneView v = link.Linked;
            bool flash = Time.unscaledTime < lostFlashUntil && ((int)(Time.unscaledTime * 6f) & 1) == 0;
            Color ink = VanillaHudStyle.Ink;
            Color alert = VanillaHudStyle.WarningRed;
            Color tone = lost || (v != null && v.Quality <= 0f) ? alert : v != null && v.Occupied ? VanillaHudStyle.Caution : ink;
            string callsign = v != null ? v.Callsign : link.LostName, word = lost ? "LOST" : v == null ? "" : v.FlightState;
            if (state == null || callsign != lastCallsign || word != lastWord)
            {
                lastCallsign = callsign;
                lastWord = word;
                state = word.Length > 0 ? callsign + " · " + word : callsign;
                AvText.Set(stateText, state);
            }
            nameText.color = VanillaHudStyle.InkDim;
            stateText.color = tone;
            Strip(v, ink);
            Color frameInk = flash ? alert : ink;
            if (frameInk != lastInk)
            {
                lastInk = frameInk;
                AvQuadBuffer b = chrome.Buffer;
                b.Clear();
                FeedCorner.Frame(b, 0f, 0f, FeedCorner.Width, cardH, frameInk.ToRgba());
                chrome.Commit();
            }
        }

        /// <summary>The data strip under the picture: ALT, SPD, HDG of the linked drone (its own link readings, the same the picture's tapes show); blank with no drone. A cell's text is rebuilt only when its rounded value moved.</summary>
        private void Strip(DroneView v, Color ink)
        {
            for (int i = 0; i < cells.Length; i++) cells[i].color = ink;
            int alt = v != null ? Mathf.RoundToInt(v.AltM / 10f) : int.MinValue, spd = v != null ? Mathf.RoundToInt(v.SpeedMs * 1.944f / 5f) : int.MinValue, hdg = v != null ? Mathf.RoundToInt(Mathf.Repeat(v.HeadingDeg, 360f)) % 360 : int.MinValue;
            if (cellKeys[0] != alt) { cellKeys[0] = alt; AvText.Set(cells[0], v != null ? FeedCorner.CellHead + "ALT" + FeedCorner.CellTail + AvNum.Fixed(alt * 10f, 0) + "M" : ""); }
            if (cellKeys[1] != spd) { cellKeys[1] = spd; AvText.Set(cells[1], v != null ? FeedCorner.CellHead + "SPD" + FeedCorner.CellTail + AvNum.Fixed(spd * 5f, 0) + "KT" : ""); }
            if (cellKeys[2] != hdg) { cellKeys[2] = hdg; AvText.Set(cells[2], v != null ? FeedCorner.CellHead + "HDG" + FeedCorner.CellTail + hdg.ToString("000") : ""); }
        }

        /// <summary>Layer on: the holder keeps this card's own 1920x1080 Expand grid inside the HUD canvas, so the card's sizes and its screen corners (<see cref="FeedCorner.Uav"/>) are unchanged.</summary>
        private void Fit()
        {
            float own = Mathf.Max(0.01f, Mathf.Min(Screen.width / 1920f, Screen.height / 1080f));
            var holder = (RectTransform)root.transform;
            holder.localScale = Vector3.one * (own / Mathf.Max(0.01f, HudLayer.HudCanvas.scaleFactor));
            holder.sizeDelta = new Vector2(Screen.width / own, Screen.height / own);
        }

        private void Build(bool layer)
        {
            inLayer = layer;
            // A scene object like the other HUD canvases: it goes with the scene, before the font materials unload at quit.
            root = new GameObject("BoscaliUavFeedCard", typeof(RectTransform));
            if (layer)
            {
                // Over.Data of the HUD layer: the card gains the HUD's visibility and the visor's glitch with the rest of the mod's blocks.
                var holder = (RectTransform)root.transform;
                holder.SetParent(HudLayer.Data, false);
                holder.anchorMin = holder.anchorMax = holder.pivot = Vector2.zero;
                holder.anchoredPosition = Vector2.zero;
            }
            else
            {
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortOrder;
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }

            float w = FeedCorner.Width, feedH = Mathf.Round(w * 9f / 16f);
            cardH = FeedCorner.Header + feedH;
            card = AvLay.Child((RectTransform)root.transform, "Card");
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 0f);
            card.anchoredPosition = new Vector2(-HudZones.Margin, Margin);
            card.sizeDelta = new Vector2(w, cardH);
            feed = new DroneFeedView(card, "Feed", 10f);
            // The picture takes what the shelf above and the data strip below leave (the card keeps its total height: the missile card stands on it).
            feed.Place(new AvSlot(0f, FeedCorner.Header, w, cardH - FeedCorner.Header - FeedCorner.Strip));
            chrome = AvVector.Create(card, "Chrome", 64, HudGlow.Stroke, HudGlow.Alpha);
            lastInk = Color.clear;
            state = null;
            nameText = Word("Name", FeedCorner.ShelfText, 150f, TextAlignmentOptions.MidlineLeft);
            nameText.text = "UAV FEED";
            nameText.fontStyle = FontStyles.Bold;
            stateText = Word("State", w - FeedCorner.StateInset - 170f, 170f, TextAlignmentOptions.MidlineRight);
            stateText.fontStyle = FontStyles.Bold;
            float pt = FeedCorner.StripPt(w);
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = Word("Cell" + i, 3f, w - 6f, i == 0 ? TextAlignmentOptions.MidlineLeft : i == 1 ? TextAlignmentOptions.Midline : TextAlignmentOptions.MidlineRight, cardH - FeedCorner.Strip, FeedCorner.Strip, pt);
                cellKeys[i] = int.MinValue;
            }
        }

        // The HUD's own weapon font when the cockpit style is known, the avionics data face otherwise.
        private TMP_Text Word(string name, float x, float width, TextAlignmentOptions align, float y = 0f, float height = -1f, float size = 12f)
        {
            TMP_Text t = AvText.Make(card, name, AvTextRole.DataSmall, "", align);
            if (VanillaHudStyle.TryCockpit(out VanillaHudStyle.CockpitStyle style) && style.Font != null)
            {
                t.font = style.Font;
                if (style.FontMaterial != null) t.fontSharedMaterial = style.FontMaterial;
            }
            AvText.Size(t, size);
            t.raycastTarget = false;
            AvLay.Place(t, x, y, width, height < 0f ? FeedCorner.Header - 3f : height);
            return t;
        }
    }
}
