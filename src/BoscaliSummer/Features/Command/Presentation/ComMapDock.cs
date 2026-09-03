using System;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    internal sealed class ComMapDock : MonoBehaviour, ISceneService
    {
        private CommandSettings settings;
        private CommandManager command;
        private ComMapOverlay overlay;
        private ManualLogSource logger;

        private GameObject dockRoot;
        private Button btnFrontlines;
        private Button btnRadar;
        private Button btnOrders;

        private TMP_Text lblFrontlines;
        private TMP_Text lblRadar;
        private TMP_Text lblOrders;

        private bool initialized;

        public void Configure(
            CommandSettings config, CommandManager manager, ComMapOverlay mapOverlay, ManualLogSource log)
        {
            settings = config;
            command = manager;
            overlay = mapOverlay;
            logger = log;
        }

        public void ResetForScene()
        {
            if (dockRoot != null) UnityEngine.Object.Destroy(dockRoot);
            dockRoot = null;
            initialized = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (!settings.Enabled.Value) return;

            DynamicMap dm = UnityEngine.Object.FindObjectOfType<DynamicMap>();
            if (dm == null || dm.maximizedMapCanvas == null) return;

            if (!initialized)
            {
                BuildDock(dm.maximizedMapCanvas);
                return;
            }

            if (dockRoot != null)
            {
                dockRoot.SetActive(DynamicMap.mapMaximized);
            }
        }

        private void BuildDock(Canvas canvas)
        {
            dockRoot = new GameObject("ComTacticalDock", typeof(RectTransform), typeof(Image));
            dockRoot.transform.SetParent(canvas.transform, false);

            RectTransform rt = dockRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -64f);
            rt.sizeDelta = new Vector2(220f, 36f);

            Image bg = dockRoot.GetComponent<Image>();
            bg.color = ComPalette.SurfaceScreen;

            // Horizontal Layout
            HorizontalLayoutGroup hlg = dockRoot.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(4, 4, 4, 4);
            hlg.spacing = 6f;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            btnFrontlines = CreateDockButton("FRONT", () =>
            {
                if (overlay != null)
                {
                    overlay.ShowFrontlines = !overlay.ShowFrontlines;
                    UpdateLabels();
                }
            }, out lblFrontlines);

            btnRadar = CreateDockButton("RADAR", () =>
            {
                if (overlay != null)
                {
                    overlay.ShowRadar = !overlay.ShowRadar;
                    UpdateLabels();
                }
            }, out lblRadar);

            btnOrders = CreateDockButton("ORDERS", () =>
            {
                if (overlay != null)
                {
                    overlay.ShowAiOrders = !overlay.ShowAiOrders;
                    UpdateLabels();
                }
            }, out lblOrders);

            // Doctrine lives on the OPS THEATER tab. This dock is overlay toggles only.

            UpdateLabels();
            initialized = true;
            logger?.LogInfo("[COM] Tactical Dock built on maximized map canvas.");
        }

        private Button CreateDockButton(string text, Action onClick, out TMP_Text label)
        {
            GameObject btnObj = new GameObject("Btn_" + text, typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(dockRoot.transform, false);

            Image img = btnObj.GetComponent<Image>();
            img.color = ComPalette.SurfaceCard;

            Button btn = btnObj.GetComponent<Button>();
            // Strict input isolation: strip controller steering navigation
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock cb = btn.colors;
            cb.normalColor = ComPalette.SurfaceCard;
            cb.highlightedColor = ComPalette.SurfaceCardHover;
            cb.pressedColor = ComPalette.SurfaceActive;
            btn.colors = cb;
            btn.onClick.AddListener(() =>
            {
                onClick?.Invoke();
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == btnObj)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            });

            GameObject textObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(btnObj.transform, false);

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            label = textObj.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = ComPalette.FontMicro;
            label.color = ComPalette.TextPrimary;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            return btn;
        }

        private void UpdateLabels()
        {
            if (lblFrontlines != null && overlay != null)
            {
                lblFrontlines.color = overlay.ShowFrontlines ? ComPalette.HudEmerald : ComPalette.TextDim;
            }
            if (lblRadar != null && overlay != null)
            {
                lblRadar.color = overlay.ShowRadar ? ComPalette.InfoCyan : ComPalette.TextDim;
            }
            if (lblOrders != null && overlay != null)
            {
                lblOrders.color = overlay.ShowAiOrders ? ComPalette.ThreatAmber : ComPalette.TextDim;
            }

        }
    }
}
