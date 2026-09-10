using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed class SettingsMfdPanel : MonoBehaviour, ISceneService
    {
        private CommandSettings settings;
        private ManualLogSource logger;
        private GameObject root;
        private MFDScreen screen;
        private List<MFDScreen> boundScreens;
        private int boundSlot = -1;
        private bool claimed;
        private bool failed;
        private float nextTick;
        private readonly List<Action> refreshers = new List<Action>();

        public void Configure(CommandSettings config, ManualLogSource log)
        {
            settings = config;
            logger = log;
        }

        private void Update()
        {
            if (settings == null || failed || Application.isBatchMode || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + (screen == null ? 1f : 0.25f);
            if (screen == null)
            {
                var map = SceneSingleton<DynamicMap>.i;
                var canvas = map == null ? null : map.maximizedMapCanvas;
                var mfd = canvas == null ? null : canvas.GetComponentInChildren<VirtualMFD>(true);
                if (mfd == null) return;
                try { Install(mfd); }
                catch (Exception error)
                {
                    ResetForScene();
                    failed = true;
                    logger.LogWarning("SET panel installation failed: " + error);
                }
            }
            if (screen != null && screen.isActive)
                foreach (Action refresh in refreshers) refresh();
        }

        private void Install(VirtualMFD mfd)
        {
            var template = MfdBezel.FindTemplate(mfd);
            if (template == null || BezelRegistry.IsClaimed(BezelRegistry.Set)) return;
            if (!MfdBezel.TryClaim(BezelRegistry.Set, false, mfd,
                    out var buttons, out var screens, out int slot, out bool left)) return;
            claimed = true;
            var bezel = buttons[slot];
            var label = bezel.GetComponentInChildren<TextMeshProUGUI>(true);
            var highlight = bezel.GetComponentInChildren<Image>(true);
            if (label == null || highlight == null)
                throw new InvalidOperationException("SET bezel label or highlight is missing.");

            root = new GameObject("BoscaliSummer.SET", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)root.transform;
            var source = (RectTransform)template.transform;
            rect.SetParent(source.parent, false);
            rect.anchorMin = source.anchorMin;
            rect.anchorMax = source.anchorMax;
            rect.pivot = source.pivot;
            rect.localScale = source.localScale;
            rect.sizeDelta = new Vector2(AvTokens.PanelWidth, AvTokens.PanelHeight);
            var background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(rect, false);
            var body = (RectTransform)content.transform;
            AvKit.Stretch(body);
            var bar = AvStyled.TopBar(body, new Rect(14f, -14f, AvTokens.PanelInnerWidth, 32f), "SET", 0);
            bar.State.text = "MAP SETTINGS";
            AvStyled.Label(body, new Rect(28f, -64f, AvTokens.PanelInnerWidth - 28f, 24f), "TACTICAL DISPLAY", "section-title");
            Toggle(body, -102f, "EXPANDED MAP", () => settings.ExpandedMapUi.Value,
                value => settings.ExpandedMapUi.Value = value);
            Toggle(body, -154f, "FRONTLINES", () => settings.FrontlinesOverlay.Value,
                value => settings.FrontlinesOverlay.Value = value);
            Stepper(body, -216f, "OVERLAY OPACITY", () => settings.OverlayOpacity.Value.ToString("P0"),
                delta => settings.OverlayOpacity.Value = Mathf.Clamp(settings.OverlayOpacity.Value + delta * 0.05f, 0.1f, 1f));
            Stepper(body, -278f, "GRID RESOLUTION", () => settings.GridResolution.Value.ToString(),
                delta => settings.GridResolution.Value = Mathf.Clamp(settings.GridResolution.Value + delta * 8, 16, 64));
            Stepper(body, -340f, "REFRESH INTERVAL", () => settings.GridRefreshInterval.Value.ToString("0.0") + " s",
                delta => settings.GridRefreshInterval.Value = Mathf.Clamp(settings.GridRefreshInterval.Value + delta * 0.1f, 0.2f, 2f));
            AvStyled.StatusStrip(body, new Rect(14f, -(AvTokens.PanelHeight - 54f), AvTokens.PanelInnerWidth, 40f)).text =
                "Changes are saved to your Command configuration.";

            screen = root.AddComponent<MFDScreen>();
            screen.shortName = BezelRegistry.Set;
            screen.displayPanel = content;
            screen.aircraftOnly = false;
            screen.label = label;
            screen.highlight = highlight;
            boundScreens = screens;
            boundSlot = slot;
            MfdBezel.Bind(mfd, buttons, screens, slot, left, screen);
            foreach (Action refresh in refreshers) refresh();
        }

        private void Toggle(RectTransform parent, float y, string title, Func<bool> get, Action<bool> set)
        {
            AvStyled.Label(parent, new Rect(28f, y, 284f, 32f), title, "section-title");
            var button = AvStyled.Button(parent, new Rect(326f, y, 116f, 32f), "", "button", () => set(!get()));
            refreshers.Add(() => { button.SetText(get() ? "ON" : "OFF"); button.SetLatched(get()); });
        }

        private void Stepper(RectTransform parent, float y, string title, Func<string> get, Action<int> change)
        {
            AvStyled.Label(parent, new Rect(28f, y, 224f, 32f), title, "section-title");
            AvStyled.Button(parent, new Rect(254f, y, 36f, 32f), "-", "button", () => change(-1));
            var value = AvStyled.Label(parent, new Rect(294f, y, 108f, 32f), "", "metric-value",
                align: TextAlignmentOptions.Center);
            AvStyled.Button(parent, new Rect(406f, y, 36f, 32f), "+", "button", () => change(1));
            refreshers.Add(() => value.text = get());
        }

        public void ResetForScene()
        {
            if (boundScreens != null && boundSlot >= 0 && boundSlot < boundScreens.Count &&
                ReferenceEquals(boundScreens[boundSlot], screen)) boundScreens[boundSlot] = null;
            if (claimed) BezelRegistry.Release(BezelRegistry.Set);
            if (root != null) Destroy(root);
            root = null;
            screen = null;
            boundScreens = null;
            boundSlot = -1;
            claimed = false;
            failed = false;
            nextTick = 0f;
            refreshers.Clear();
        }

        private void OnDestroy() => ResetForScene();
    }
}
