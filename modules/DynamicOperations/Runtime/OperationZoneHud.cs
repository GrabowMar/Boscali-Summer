using System.Collections.Generic;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// The cockpit card the pilot reads while flying a contract: plain vanilla HUD text at the
    /// right edge of the screen, no panel, no rails. Each row is one line — the contracted
    /// title followed by the distance and clock (or CONTACT LOST) — with a thin vanilla-style
    /// bar under it, and a header that swaps to the entering/leaving-area banner. Colours come
    /// from the live vanilla theme; a lost contact stays listed because that is the state the
    /// pilot most needs to see.
    /// </summary>
    internal sealed class OperationZoneHud : MonoBehaviour, ISceneService
    {
        private const int MaxRows = OperationBoard.MaximumCards;
        private const float ContentSeconds = 0.25f;
        private const float ServerRefreshSeconds = 2f;
        private const float ToastSeconds = 4.5f;
        private const float FadeInSeconds = 0.14f;
        private const float FadeOutSeconds = 0.24f;
        private const float RowWidth = 380f;
        private const float RowPitch = 30f;
        private const float RowHeight = 30f;
        private const float RowTextHeight = 18f;
        private const float RowTextInset = 1f;
        private const float HeaderPitch = 22f;
        private const float HeaderHeight = 14f;
        private const float BarWidth = 180f;
        private const float BarHeight = 6f;
        private const float BarBackHeight = 9f;
        private const float HeaderAlpha = 0.45f;
        private const float TitleAlpha = 0.8f;
        private const float DistanceAlpha = 0.6f;

        private static readonly Color BarBackColour = new Color(0f, 0f, 0f, 0.3f);

        private OperationsManager manager;
        private GameObject root;
        private RectTransform rootRect;
        private CanvasGroup group;
        private TMP_Text header;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private readonly TMP_Text[] rows = new TMP_Text[MaxRows];
        private readonly Image[] barBacks = new Image[MaxRows];
        private readonly Image[] bars = new Image[MaxRows];
        private readonly int[] shownIds = new int[MaxRows];
        private readonly bool[] inside = new bool[MaxRows];
        private readonly string[] textCache = new string[MaxRows];
        private readonly ContractCard[] cards = new ContractCard[MaxRows];
        private readonly ContractCard[] selected = new ContractCard[MaxRows];
        private readonly float[] distances = new float[MaxRows];
        private float nextContent, nextServer, toastUntil, alpha;
        private bool wanted, headerIsToast;

        internal void Configure(OperationsManager owner)
        {
            manager = owner;
            for (int i = 0; i < cards.Length; i++) { cards[i] = default; selected[i] = default; }
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null; rootRect = null; group = null; header = null;
            font = null; fontMaterial = null;
            toastUntil = 0f; nextContent = 0f; nextServer = 0f; alpha = 0f;
            wanted = false; headerIsToast = false;
            for (int i = 0; i < MaxRows; i++)
            {
                rows[i] = null; barBacks[i] = null; bars[i] = null;
                shownIds[i] = 0; inside[i] = false; textCache[i] = "";
                cards[i] = default; selected[i] = default; distances[i] = 0f;
            }
            VanillaHudStyle.Invalidate();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (manager == null) { wanted = false; Fade(); return; }
            if (Time.unscaledTime >= nextContent)
            {
                nextContent = Time.unscaledTime + ContentSeconds;
                Refresh();
            }
            if (wanted && Time.unscaledTime >= nextServer)
            {
                nextServer = Time.unscaledTime + ServerRefreshSeconds;
                manager.Refresh();
            }
            Fade();
        }

        private void Refresh()
        {
            if (headerIsToast && Time.unscaledTime >= toastUntil) headerIsToast = false;
            if (DynamicMap.mapMaximized || !GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null ||
                aircraft.disabled || aircraft.HasEjected())
            {
                wanted = false;
                return;
            }

            IReadOnlyList<SecondaryObjectiveView> views = manager.Objectives;
            int count = 0;
            if (views != null)
                for (int i = 0; i < views.Count && count < cards.Length; i++)
                    if (ContractCard.TryRead(views[i], out ContractCard card)) cards[count++] = card;
            if (count == 0) { wanted = false; return; }
            if (root == null) Build();

            Vector3 self = aircraft.transform.position.ToGlobalPosition().AsVector3();
            int shown = ContractSelection.Select(cards, count, self.x, self.z, selected, distances, MaxRows);

            VanillaHudStyle.Palette colours = VanillaHudStyle.Colours;
            bool metric = VanillaHudStyle.Metric;
            float size = OverlayTextSize();

            for (int row = 0; row < shown; row++)
            {
                ContractCard card = selected[row];
                float distance = distances[row];
                Watch(row, card, card.HasMarker && card.Inside(distance), distance);
                SetRow(row, card, distance, colours, metric, size);
            }
            for (int row = shown; row < MaxRows; row++) SetRowEmpty(row);

            header.fontSize = size * 0.7f;
            if (!headerIsToast)
                SetHeader(count + (count == 1 ? " ACTIVE CONTRACT" : " ACTIVE CONTRACTS"),
                    Alpha(colours.AllClear, HeaderAlpha));
            wanted = shown > 0 || Time.unscaledTime < toastUntil;
        }

        private void SetRow(int row, in ContractCard card, float distance,
            in VanillaHudStyle.Palette colours, bool metric, float size)
        {
            TMP_Text label = rows[row];
            if (label == null) return;
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            label.fontSize = size;

            Color tone = card.Tone == MarkerTone.Caution ? colours.Warning : colours.AllClear;
            string text = Tag(card.TitleLine, Alpha(colours.AllClear, TitleAlpha));
            if (!card.HasMarker)
            {
                text += "  " + Tag("CONTACT LOST", colours.Alert);
            }
            else
            {
                text += "  " + Tag(OperationMarkerCopy.Distance(distance, metric), Alpha(colours.AllClear, DistanceAlpha));
                string clock = OperationMarkerCopy.Clock(card.Seconds);
                if (!string.IsNullOrEmpty(clock)) text += "  " + Tag(clock, tone);
            }
            if (textCache[row] != text) { textCache[row] = text; label.text = text; }

            float bar = card.HasMarker
                ? OperationMarkerCopy.Bar(distance, card.Radius, card.Inside(distance), card.Progress)
                : 0f;
            bool showBar = card.HasMarker && bar > 0.001f;
            Image back = barBacks[row];
            if (back == null) return;
            if (back.gameObject.activeSelf != showBar) back.gameObject.SetActive(showBar);
            if (!showBar) return;
            bars[row].rectTransform.sizeDelta = new Vector2(BarWidth * bar, BarHeight);
            bars[row].color = tone;
        }

        private void SetRowEmpty(int row)
        {
            if (rows[row] != null && rows[row].gameObject.activeSelf) rows[row].gameObject.SetActive(false);
            if (barBacks[row] != null && barBacks[row].gameObject.activeSelf) barBacks[row].gameObject.SetActive(false);
            textCache[row] = "";
        }

        private void Watch(int row, in ContractCard card, bool isInside, float distance)
        {
            if (shownIds[row] != card.Id)
            {
                shownIds[row] = card.Id;
                inside[row] = isInside;
                textCache[row] = "";
                return;
            }
            if (isInside && !inside[row])
            {
                inside[row] = true;
                Toast("ENTERING AREA  ·  " + card.TitleLine,
                    card.Tone == MarkerTone.Caution ? VanillaHudStyle.Colours.Warning : VanillaHudStyle.Colours.AllClear);
            }
            else if (!isInside && inside[row] && card.Radius > 0f && distance > card.Radius * 1.1f)
            {
                inside[row] = false;
                Toast("LEAVING AREA  ·  " + card.TitleLine, Alpha(VanillaHudStyle.Colours.AllClear, HeaderAlpha));
            }
        }

        private void Toast(string text, Color colour)
        {
            if (header == null) return;
            SetHeader(text, colour);
            headerIsToast = true;
            toastUntil = Time.unscaledTime + ToastSeconds;
        }

        private void SetHeader(string text, Color colour)
        {
            if (header == null) return;
            if (header.text != text) header.text = text;
            header.color = colour;
        }

        private void Fade()
        {
            if (root == null) return;
            float target = wanted ? 1f : 0f;
            if (!Mathf.Approximately(alpha, target))
            {
                float speed = (target > alpha ? 1f / FadeInSeconds : 1f / FadeOutSeconds) * Time.unscaledDeltaTime;
                alpha = Mathf.MoveTowards(alpha, target, speed);
                group.alpha = alpha;
            }
            bool active = wanted || alpha > 0.001f;
            if (root.activeSelf != active) root.SetActive(active);
        }

        private void Build()
        {
            if (VanillaHudStyle.TryCockpit(out VanillaHudStyle.CockpitStyle cockpit) && cockpit.Font != null)
            {
                font = cockpit.Font;
                fontMaterial = cockpit.FontMaterial;
            }
            if (font == null)
            {
                font = TMP_Settings.defaultFontAsset;
                fontMaterial = null;
            }

            root = new GameObject("Boscali Contract Text", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            root.transform.SetParent(transform, false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1;
            canvas.pixelPerfect = false;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // A screen-space canvas drives its own root rect, so the block lives on a child:
            // right-centre of the screen, clear of the log, ammo, throttle and compass.
            var contentObject = new GameObject("Contract Text Block", typeof(RectTransform));
            RectTransform content = (RectTransform)contentObject.transform;
            content.SetParent(root.transform, false);
            content.anchorMin = content.anchorMax = new Vector2(1f, 0.5f);
            content.pivot = new Vector2(1f, 0.5f);
            content.anchoredPosition = new Vector2(-28f, 60f);
            content.sizeDelta = new Vector2(RowWidth, HeaderPitch + MaxRows * RowPitch);
            rootRect = content;

            group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0f;

            header = Line(rootRect, "Contract Header", HeaderHeight, 0f);
            header.alignment = TextAlignmentOptions.MidlineRight;
            for (int i = 0; i < MaxRows; i++)
            {
                float top = HeaderPitch + i * RowPitch;
                TMP_Text row = Line(rootRect, "Contract Row", RowTextHeight, -(top + RowTextInset));
                row.alignment = TextAlignmentOptions.MidlineRight;
                rows[i] = row;

                var backObject = new GameObject("Contract Bar Back", typeof(RectTransform), typeof(Image));
                RectTransform back = (RectTransform)backObject.transform;
                back.SetParent(rootRect, false);
                back.anchorMin = back.anchorMax = new Vector2(1f, 1f);
                back.pivot = new Vector2(1f, 0f);
                back.sizeDelta = new Vector2(BarWidth, BarBackHeight);
                back.anchoredPosition = new Vector2(0f, -(top + RowHeight));
                barBacks[i] = backObject.GetComponent<Image>();
                barBacks[i].color = BarBackColour;
                barBacks[i].raycastTarget = false;

                var barObject = new GameObject("Contract Bar", typeof(RectTransform), typeof(Image));
                RectTransform barRect = (RectTransform)barObject.transform;
                barRect.SetParent(back, false);
                barRect.anchorMin = barRect.anchorMax = new Vector2(0f, 0.5f);
                barRect.pivot = new Vector2(0f, 0.5f);
                barRect.sizeDelta = new Vector2(BarWidth, BarHeight);
                barRect.anchoredPosition = Vector2.zero;
                Image bar = barObject.GetComponent<Image>();
                bar.raycastTarget = false;
                bars[i] = bar;

                backObject.SetActive(false);
            }
            root.SetActive(false);
        }

        private TMP_Text Line(RectTransform parent, string name, float height, float y)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(RowWidth, height);
            rect.anchoredPosition = new Vector2(0f, y);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            if (fontMaterial != null) text.fontSharedMaterial = fontMaterial;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineRight;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Vanilla's own label size (overlay text size through its 0.5 scale).</summary>
        private static float OverlayTextSize() => VanillaHudStyle.ObjectiveTextSize;

        private static Color Alpha(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, alpha);

        private static string Tag(string text, Color colour) =>
            "<color=#" + ColorUtility.ToHtmlStringRGBA(colour) + ">" + text + "</color>";
    }
}
