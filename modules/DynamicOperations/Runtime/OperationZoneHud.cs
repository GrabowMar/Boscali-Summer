using System;
using System.Collections.Generic;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// Compact mission-area readout for the cockpit: distance to the zone edge while
    /// approaching, hold progress inside, and a short banner on the enter/leave transition.
    /// The vanilla pointer already points at the objective; this states what the area means.
    /// </summary>
    internal sealed class OperationZoneHud : MonoBehaviour, ISceneService
    {
        private const int MaxRows = 2;
        private const float RefreshSeconds = 0.25f;
        private const float ToastSeconds = 4.5f;
        private const float PanelWidth = 760f;
        private const float PanelHeight = 96f;

        private static readonly Color Ground = new Color32(10, 14, 17, 210);
        private OperationsManager manager;
        private GameObject root;
        private TMP_Text toast;
        private readonly TMP_Text[] titles = new TMP_Text[MaxRows];
        private readonly TMP_Text[] details = new TMP_Text[MaxRows];
        private readonly Image[] rails = new Image[MaxRows];
        private readonly int[] shownIds = new int[MaxRows];
        private readonly bool[] inside = new bool[MaxRows];
        private readonly string[] titleCache = new string[MaxRows];
        private readonly string[] detailCache = new string[MaxRows];
        private float nextRefresh, toastUntil;

        internal void Configure(OperationsManager owner) => manager = owner;

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null; toast = null; toastUntil = 0f; nextRefresh = 0f;
            for (int i = 0; i < MaxRows; i++)
            {
                titles[i] = null; details[i] = null; rails[i] = null;
                shownIds[i] = 0; inside[i] = false; titleCache[i] = detailCache[i] = "";
            }
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (manager == null) { Hide(); return; }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (DynamicMap.mapMaximized || !GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null ||
                aircraft.disabled || aircraft.HasEjected())
            { Hide(); return; }

            IReadOnlyList<SecondaryObjectiveView> cards = manager.Objectives;
            if (cards == null || cards.Count == 0) { Hide(); return; }
            if (root == null) Build();

            Vector3 self = aircraft.transform.position.ToGlobalPosition().AsVector3();
            if (!Operation.Finite(self.x) || !Operation.Finite(self.z)) { Hide(); return; }
            int row = 0;
            for (int c = 0; c < cards.Count && row < MaxRows; c++)
            {
                SecondaryObjectiveView card = cards[c];
                if (card == null || !card.IsActive || !card.HasMarker || card.Radius <= 0f) continue;
                float dx = self.x - card.X, dz = self.z - card.Z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                float band = card.Radius + Mathf.Max(card.Radius * 2f, 2000f);
                bool isInside = distance <= card.Radius;
                if (shownIds[row] != card.Id)
                {
                    shownIds[row] = card.Id; inside[row] = isInside; titleCache[row] = detailCache[row] = "";
                }
                else if (isInside && !inside[row]) { inside[row] = true; Toast("MISSION AREA ENTERED  —  " + card.Title, AvTheme.RailReady); }
                else if (!isInside && inside[row] && distance > card.Radius * 1.1f) { inside[row] = false; Toast("MISSION AREA LEFT  —  " + card.Title, AvTheme.Disabled); }
                if (!inside[row] && distance > band) continue;

                string title = "#" + card.Id + "  " + card.Title;
                bool returning = card.Status != null && card.Status.IndexOf("RETURN", StringComparison.Ordinal) >= 0;
                string detail = inside[row]
                    ? returning ? "IN AREA  ·  LAND TO DELIVER" : "IN AREA  ·  HOLD " + Mathf.RoundToInt(card.Progress * 100f) + "%"
                    : (Mathf.Max(0f, distance - card.Radius) / 1000f).ToString("0.0") + " KM TO EDGE";
                SetRow(row, title, detail, inside[row] ? AvTheme.RailReady : AvTheme.RailInfo);
                row++;
            }

            for (int i = row; i < MaxRows; i++) SetRow(i, null, null, AvTheme.RailInfo);
            bool toastVisible = Time.unscaledTime < toastUntil;
            if (!toastVisible && toast != null && toast.gameObject.activeSelf)
            {
                toast.text = ""; toast.gameObject.SetActive(false);
            }
            root.SetActive(row > 0 || toastVisible);
        }

        private void SetRow(int row, string title, string detail, Color color)
        {
            if (titles[row] == null) return;
            bool visible = title != null;
            titles[row].gameObject.SetActive(visible);
            details[row].gameObject.SetActive(visible);
            rails[row].gameObject.SetActive(visible);
            if (!visible) return;
            if (titleCache[row] != title) { titleCache[row] = title; titles[row].text = title; }
            if (detailCache[row] != detail) { detailCache[row] = detail; details[row].text = detail; }
            titles[row].color = color;
            details[row].color = color;
            rails[row].color = color;
        }

        private void Toast(string text, Color color)
        {
            if (root == null) return;
            toast.text = text;
            toast.color = color;
            toast.gameObject.SetActive(true);
            toastUntil = Time.unscaledTime + ToastSeconds;
        }

        private void Hide()
        {
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        private void Build()
        {
            root = new GameObject("Boscali Zone Feedback", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
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
            panel.name = "Mission Area";
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            AvKit.Outline(panel, new Rect(0f, 0f, PanelWidth, PanelHeight), AvTheme.Hairline);
            AvKit.CornerTicks(panel, new Rect(0f, 0f, PanelWidth, PanelHeight), AvTheme.Hairline);

            toast = AvKit.Label(panel, "", new Rect(14f, -5f, PanelWidth - 28f, 18f),
                AvTheme.RailReady, 13f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            for (int i = 0; i < MaxRows; i++)
            {
                float y = -28f - i * 26f;
                rails[i] = AvKit.Rule(panel, new Rect(12f, y, 3f, 18f), AvTheme.RailInfo);
                titles[i] = AvKit.Label(panel, "", new Rect(22f, y, 430f, 18f),
                    AvTheme.TextPrimary, 13f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                titles[i].richText = false;
                details[i] = AvKit.Label(panel, "", new Rect(456f, y, PanelWidth - 470f, 18f),
                    AvTheme.RailInfo, 12f, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
                details[i].richText = false;
            }
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            root.SetActive(false);
        }
    }
}
