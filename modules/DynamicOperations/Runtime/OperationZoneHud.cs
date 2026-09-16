using System.Collections.Generic;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// The cockpit card the pilot reads while flying a contract: the contracts that are near
    /// enough to matter, nearest first, with the one you are inside pinned to the top. Rows
    /// carry the same family / distance / hold / clock copy as the markers, a bar that closes
    /// on the area edge and then carries the hold, and a banner on entering or leaving an
    /// area. A contract whose contact was lost stays listed, because that is the state the
    /// pilot most needs to see.
    /// </summary>
    internal sealed class OperationZoneHud : MonoBehaviour, ISceneService
    {
        private const int MaxRows = OperationBoard.MaximumCards;
        private const float ContentSeconds = 0.25f;
        private const float ServerRefreshSeconds = 2f;
        private const float ToastSeconds = 4.5f;
        private const float PanelWidth = 660f;
        private const float PanelHeight = 148f;
        private const float RowPitch = 40f;
        private const float FirstRowY = -30f;
        private const float RowHeight = 34f;
        private const float BarWidth = 150f;
        private const float BarHeight = 5f;
        private const float FadeInSeconds = 0.14f;
        private const float FadeOutSeconds = 0.24f;

        private static readonly Color Ground = new Color32(10, 14, 17, 210);

        private OperationsManager manager;
        private GameObject root;
        private CanvasGroup group;
        private TMP_Text banner;
        private readonly RectTransform[] rowRoots = new RectTransform[MaxRows];
        private readonly TMP_Text[] titles = new TMP_Text[MaxRows];
        private readonly TMP_Text[] details = new TMP_Text[MaxRows];
        private readonly Image[] rails = new Image[MaxRows];
        private readonly Image[] bars = new Image[MaxRows];
        private readonly RectTransform[] barBoxes = new RectTransform[MaxRows];
        private readonly int[] shownIds = new int[MaxRows];
        private readonly bool[] inside = new bool[MaxRows];
        private readonly string[] titleCache = new string[MaxRows];
        private readonly string[] detailCache = new string[MaxRows];
        private readonly ContractCard[] cards = new ContractCard[MaxRows];
        private readonly ContractCard[] rows = new ContractCard[MaxRows];
        private readonly float[] rowDistances = new float[MaxRows];
        private float nextContent, nextServer, toastUntil, alpha;
        private bool wanted, bannerIsToast;

        internal void Configure(OperationsManager owner)
        {
            manager = owner;
            for (int i = 0; i < cards.Length; i++) { cards[i] = default; rows[i] = default; }
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null; group = null; banner = null;
            toastUntil = 0f; nextContent = 0f; nextServer = 0f; alpha = 0f; wanted = false; bannerIsToast = false;
            for (int i = 0; i < MaxRows; i++)
            {
                rowRoots[i] = null; titles[i] = null; details[i] = null; rails[i] = null; bars[i] = null;
                barBoxes[i] = null;
                shownIds[i] = 0; inside[i] = false; titleCache[i] = detailCache[i] = "";
                cards[i] = default; rows[i] = default; rowDistances[i] = 0f;
            }
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
            Fade();
        }

        private void Refresh()
        {
            if (bannerIsToast && Time.unscaledTime >= toastUntil) SetBanner(null, AvTheme.Dim);
            if (DynamicMap.mapMaximized || !GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null ||
                aircraft.disabled || aircraft.HasEjected())
            { wanted = false; return; }

            IReadOnlyList<SecondaryObjectiveView> views = manager.Objectives;
            int count = 0;
            if (views != null)
                for (int i = 0; i < views.Count && count < cards.Length; i++)
                    if (ContractCard.TryRead(views[i], out ContractCard card)) cards[count++] = card;
            if (count == 0) { wanted = false; return; }
            if (root == null) Build();

            Vector3 self = aircraft.transform.position.ToGlobalPosition().AsVector3();
            int shown = ContractSelection.Select(cards, count, self.x, self.z, rows, rowDistances, MaxRows);
            for (int row = 0; row < shown; row++)
            {
                ContractCard card = rows[row];
                float distance = rowDistances[row];
                bool isInside = card.HasMarker && card.Inside(distance);
                Watch(row, card, isInside, distance);
                string detail = card.HasMarker
                    ? card.Detail(distance)
                    : "CONTACT LOST · " + card.Family;
                SetRow(row, card.TitleLine, detail, ContractPlate.ToneColor(card.Tone),
                    OperationMarkerCopy.Bar(distance, card.Radius, isInside, card.Progress), !card.HasMarker);
            }
            for (int row = shown; row < MaxRows; row++) SetRow(row, null, null, AvTheme.RailInfo, 0f, false);
            if (banner == null || !bannerIsToast)
                SetBanner(count + (count == 1 ? " ACTIVE CONTRACT" : " ACTIVE CONTRACTS"), AvTheme.Dim);
            wanted = shown > 0 || Time.unscaledTime < toastUntil;
        }

        private void Watch(int row, ContractCard card, bool isInside, float distance)
        {
            if (shownIds[row] != card.Id)
            {
                shownIds[row] = card.Id;
                inside[row] = isInside;
                titleCache[row] = detailCache[row] = "";
                return;
            }
            if (isInside && !inside[row])
            {
                inside[row] = true;
                Toast("ENTERING AREA  ·  " + card.TitleLine, ContractPlate.ToneColor(card.Tone));
            }
            else if (!isInside && inside[row] && card.Radius > 0f && distance > card.Radius * 1.1f)
            {
                inside[row] = false;
                Toast("LEAVING AREA  ·  " + card.TitleLine, AvTheme.Disabled);
            }
        }

        private void SetRow(int row, string title, string detail, Color color, float bar, bool lost)
        {
            if (rowRoots[row] == null) return;
            bool visible = title != null;
            if (rowRoots[row].gameObject.activeSelf != visible) rowRoots[row].gameObject.SetActive(visible);
            if (!visible) return;
            if (titleCache[row] != title) { titleCache[row] = title; titles[row].text = title; }
            if (detailCache[row] != detail) { detailCache[row] = detail; details[row].text = detail; }
            titles[row].color = color;
            details[row].color = lost ? AvTheme.RailCaution : AvTheme.Dim;
            rails[row].color = lost ? AvTheme.RailCaution : color;
            bars[row].color = color;
            bars[row].fillAmount = bar;
            if (barBoxes[row] != null)
            {
                bool showBar = !lost && bar > 0.001f;
                if (barBoxes[row].gameObject.activeSelf != showBar)
                    barBoxes[row].gameObject.SetActive(showBar);
            }
        }

        private void Toast(string text, Color color)
        {
            if (root == null) return;
            SetBanner(text, color);
            bannerIsToast = true;
            toastUntil = Time.unscaledTime + ToastSeconds;
        }

        private void SetBanner(string text, Color color)
        {
            if (banner == null) return;
            bannerIsToast = false;
            banner.text = text ?? "SECONDARY CONTRACTS";
            banner.color = color;
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
            root = new GameObject("Boscali Contract Cards", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4;
            canvas.pixelPerfect = true;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            RectTransform panel = AvKit.Panel((RectTransform)root.transform, new Rect(0f, 132f, PanelWidth, PanelHeight), Ground).rectTransform;
            panel.name = "Contract Cards";
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0f;
            AvKit.Outline(panel, new Rect(0f, 0f, PanelWidth, PanelHeight), AvTheme.Hairline);
            AvKit.CornerTicks(panel, new Rect(0f, 0f, PanelWidth, PanelHeight), AvTheme.Hairline);

            banner = AvKit.Label(panel, "SECONDARY CONTRACTS", new Rect(14f, -6f, PanelWidth - 28f, 18f),
                AvTheme.Dim, 13f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            banner.richText = false;

            for (int i = 0; i < MaxRows; i++)
            {
                var rowObject = new GameObject("Contract Row", typeof(RectTransform));
                RectTransform rowRect = (RectTransform)rowObject.transform;
                rowRect.SetParent(panel, false);
                AvKit.Place(rowRect, new Rect(0f, FirstRowY - i * RowPitch, PanelWidth, RowPitch));
                rowRoots[i] = rowRect;

                rails[i] = AvKit.Rule(rowRect, new Rect(12f, 0f, 3f, RowHeight), AvTheme.RailInfo);
                titles[i] = AvKit.Label(rowRect, "", new Rect(24f, 0f, 430f, 16f),
                    AvTheme.TextPrimary, 13f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                titles[i].richText = false;
                details[i] = AvKit.Label(rowRect, "", new Rect(24f, -16f, 430f, 14f),
                    AvTheme.Dim, 11f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                details[i].richText = false;
                var barObject = new GameObject("Contract Bar", typeof(RectTransform));
                RectTransform barRect = (RectTransform)barObject.transform;
                barRect.SetParent(rowRect, false);
                AvKit.Place(barRect, new Rect(PanelWidth - 162f, -14f, BarWidth, BarHeight));
                barBoxes[i] = barRect;
                bars[i] = AvKit.ProgressBar(barRect, new Rect(0f, 0f, BarWidth, BarHeight), 0f, AvTheme.RailInfo);
                rowObject.SetActive(false);
            }
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            root.SetActive(false);
        }
    }
}
