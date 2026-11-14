using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// IPoliticalDispatch (client-local): the cabinet card Nation hands over. EVN DISPATCH draws it under FOR THE CABINET with its answers;
    /// <see cref="CabinetCue"/> says it over the big map. Nothing is drawn over the cockpit and nothing pops up.
    /// </summary>
    internal sealed class CabinetDispatch : IPoliticalDispatch
    {
        public CabinetCard Card { get; private set; }
        private Action<int> pick;

        public void Show(CabinetCard card, Action<int> onPick)
        {
            Card = card;
            pick = onPick;
        }

        public void Close(int serial)
        {
            if (Card != null && Card.Serial == serial) { Card = null; pick = null; }
        }

        /// <summary>A local press: a request to the host, which takes the first answer.</summary>
        public void Choose(int option)
        {
            if (Card != null && Card.Chosen < 0) pick?.Invoke(option);
        }

        /// <summary>An open card nobody has answered yet.</summary>
        public bool Waiting => Card != null && Card.Chosen < 0;
    }

    /// <summary>
    /// The one-line cue over the maximised map while a cabinet card waits: "CABINET DECISION · title · 0:45 · OPEN EVN". It sits at the foot of
    /// the map, clear of the STR layer tabs and the superevent dispatch at the top, and a click opens EVN DISPATCH.
    /// </summary>
    internal sealed class CabinetCue : MonoBehaviour, ISceneService
    {
        // At the 1920 reference the map runs from x 497 to 1752 and its legend strip sits in the bottom-left corner above the clock bar.
        private const float ReferenceWidth = 1920f, ReferenceHeight = 1080f, MapCentre = 165f, Bottom = 112f, Width = 440f, Height = 34f;
        private CabinetDispatch dispatch;
        private GameObject root;
        private TMP_Text line;
        private bool failed;

        public void Configure(CabinetDispatch source) => dispatch = source;

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null;
            line = null;
            failed = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            bool show = dispatch != null && dispatch.Waiting && DynamicMap.mapMaximized;
            if (!show) { if (root != null && root.activeSelf) root.SetActive(false); return; }
            if (root == null && !TryBuild()) return;
            if (!root.activeSelf) root.SetActive(true);
            CabinetCard card = dispatch.Card;
            AvText.Set(line, "CABINET DECISION · " + card.Title + " · " + AvNum.Clock(Mathf.Ceil(card.SecondsLeft)) + " · OPEN EVN");
        }

        private bool TryBuild()
        {
            if (failed || Application.isBatchMode) return false;
            try
            {
                var host = new GameObject("BoscaliEvents.CabinetCue", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                root = host;
                Canvas canvas = host.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 28990;
                CanvasScaler scaler = host.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                var go = new GameObject("Cue", typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(host.transform, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(MapCentre, Bottom);
                rect.sizeDelta = new Vector2(Width, Height);
                MapControlRects.Register(rect);
                AvFrame frame = AvFrame.Add(rect, "Frame", AvChamfer.Diagonal(4f));
                AvLay.Fill(frame.rectTransform);
                frame.Paint(AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(0.96f), AvTheme.RailCaution);
                frame.raycastTarget = true;
                AvHit hit = AvHit.On(frame);
                hit.Click = e => AvConsole.Open("EVN", "DISPATCH");
                AvHelpTip.Attach(frame.gameObject, "The cabinet waits for an answer. Open EVN DISPATCH to choose; nobody answering, the staff picks its marked answer at 0:00.");
                line = AvText.Make(rect, "Line", AvTextRole.Label, "", TextAlignmentOptions.Center);
                AvText.Fit(line, false);
                line.color = AvTheme.RailCaution;
                AvLay.Place(line, 12f, 0f, Width - 24f, Height);
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                if (root != null) Destroy(root);
                root = null;
                Plugin.Logger?.LogError("[Events] cabinet cue unavailable: " + e.Message);
                return false;
            }
        }
    }
}
