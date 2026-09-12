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
        private AvScreen shell;
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
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;
                try { Install(mfd); }
                catch (Exception error)
                {
                    ResetForScene();
                    failed = true;
                    logger?.LogWarning("SET panel installation failed: " + error);
                }
            }
            if (screen != null && screen.isActive) RefreshPanel();
        }

        private void Install(VirtualMFD mfd)
        {
            if (claimed) return;
            if (!MfdBezel.TryClaim(MfdSlots.Set, preferLeft: false, mfd,
                    out var buttons, out var screens, out int slot, out bool left)) return;
            claimed = true;

            var template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
            if (template == null)
            {
                MfdBezel.Release(MfdSlots.Set);
                claimed = false;
                return;
            }

            var bezel = buttons[slot];
            var label = bezel.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null)
            {
                var tmp = bezel.GetComponentInChildren<TMP_Text>(true);
                if (tmp is TextMeshProUGUI ugui) label = ugui;
            }
            var highlight = FindHighlight(bezel);
            if (label == null || highlight == null)
                throw new InvalidOperationException("SET bezel label or highlight is missing.");

            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            if (sourceText != null && sourceText.font != null) AvFont.Font = sourceText.font;

            root = new GameObject("BoscaliSummer.SET", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)root.transform;
            var source = (RectTransform)template.transform;
            rect.SetParent(source.parent, false);
            rect.anchorMin = source.anchorMin;
            rect.anchorMax = source.anchorMax;
            rect.pivot = source.pivot;
            rect.localScale = source.localScale;
            float height = AvScreen.ResolveHeight(
                source.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rect.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
            var background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(rect, false);
            var body = (RectTransform)content.transform;
            AvKit.Stretch(body);

            shell = AvScreen.Build(
                body, "SET", new[] { "DISPLAY" }, null, 0,
                AvTokens.PanelWidth, height, _ => nextTick = 0f);
            shell.DataBar.State.text = "MAP SETTINGS";

            RectTransform page = (RectTransform)shell.CreatePage(0, "DisplayPage").transform;
            BuildPage(page, shell.Body);
            shell.SetPage(0);

            screen = root.AddComponent<MFDScreen>();
            screen.shortName = MfdSlots.Set;
            screen.displayPanel = content;
            screen.aircraftOnly = false;
            screen.label = label;
            screen.highlight = highlight;
            boundScreens = screens;
            boundSlot = slot;
            if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                throw new InvalidOperationException("SET bezel changed before binding.");

            if (DynamicMap.mapMaximized)
            {
                var dynMap = SceneSingleton<DynamicMap>.i;
                if (dynMap != null) MfdRailPatch.Refresh(dynMap);
            }

            RefreshPanel();
            logger?.LogInfo("SET MFD installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1) + ".");
        }

        private void BuildPage(RectTransform parent, Rect body)
        {
            AvNode layout = AvBox.Column("settings")
                .Pad(AvScreen.SpineInset, 0f, 0f, 0f)
                .Gaps(6f)
                .Add(AvBox.Cell("heading").Height(24f))
                .Add(ToggleRow("expanded"))
                .Add(ToggleRow("frontlines"))
                .Add(StepperRow("opacity"))
                .Add(StepperRow("resolution"))
                .Add(StepperRow("refresh"))
                .Add(AvBox.Filler());
            layout.Arrange(body);

            AvStyled.Spine(parent, body);
            AvStyled.Label(parent, layout.At("heading"), "TACTICAL DISPLAY", "section-title");

            Toggle(parent, layout.At("expanded"), "EXPANDED MAP", "Expand or restore the maximized map layout.",
                () => settings.ExpandedMapUi.Value,
                value =>
                {
                    settings.ExpandedMapUi.Value = value;
                    MfdRailPatch.Reconcile();
                }, band: true);
            Toggle(parent, layout.At("frontlines"), "FRONTLINES", "Show or hide the frontline overlay.",
                () => settings.FrontlinesOverlay.Value,
                value => settings.FrontlinesOverlay.Value = value);

            Stepper(parent, layout.At("opacity"), "OVERLAY OPACITY",
                () => settings.OverlayOpacity.Value.ToString("P0"),
                () => settings.OverlayOpacity.Value > 0.1f,
                () => settings.OverlayOpacity.Value < 1f,
                delta => settings.OverlayOpacity.Value = Mathf.Clamp(
                    settings.OverlayOpacity.Value + delta * 0.05f, 0.1f, 1f),
                "5%", "10%", "100%", band: true);
            Stepper(parent, layout.At("resolution"), "GRID RESOLUTION",
                () => settings.GridResolution.Value.ToString(),
                () => settings.GridResolution.Value > 16,
                () => settings.GridResolution.Value < 64,
                delta => settings.GridResolution.Value = Mathf.Clamp(
                    settings.GridResolution.Value + delta * 8, 16, 64),
                "8", "16", "64");
            Stepper(parent, layout.At("refresh"), "REFRESH INTERVAL",
                () => settings.GridRefreshInterval.Value.ToString("0.0") + " s",
                () => settings.GridRefreshInterval.Value > 0.2f,
                () => settings.GridRefreshInterval.Value < 2f,
                delta => settings.GridRefreshInterval.Value = Mathf.Clamp(
                    settings.GridRefreshInterval.Value + delta * 0.1f, 0.2f, 2f),
                "0.1 s", "0.2 s", "2.0 s", band: true);
        }

        private static AvNode ToggleRow(string name) =>
            AvBox.Row(name).Height(52f).Pad(14f, 10f, 14f, 10f).Gaps(8f)
                .Add(AvBox.Cell("label").Grow())
                .Add(AvBox.Cell("value").Width(116f));

        private static AvNode StepperRow(string name) =>
            AvBox.Row(name).Height(52f).Pad(14f, 10f, 14f, 10f).Gaps(6f)
                .Add(AvBox.Cell("label").Grow())
                .Add(AvBox.Cell("minus").Width(36f))
                .Add(AvBox.Cell("value").Width(92f))
                .Add(AvBox.Cell("plus").Width(36f));

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].gameObject != button.gameObject)
                    return images[i];
            }
            return button.GetComponent<Image>();
        }

        private void Toggle(RectTransform parent, Rect area, string title, string tooltip,
            Func<bool> get, Action<bool> set, bool band = false)
        {
            AvNode row = ToggleRow("row").Arrange(area);
            AvStyled.Box(parent, area, band ? "section band" : "section");
            AvStyled.SpineTick(parent, area.x - AvScreen.SpineInset + 3f, area.y - 16f);
            AvStyled.Label(parent, row.At("label"), title, "section-title",
                align: TextAlignmentOptions.MidlineLeft);

            var button = AvStyled.Button(parent, row.At("value"), "", "btn", () =>
            {
                set(!get());
                nextTick = 0f;
            }).WithTooltip(tooltip);
            refreshers.Add(() =>
            {
                bool on = get();
                button.SetText(on ? "ON" : "OFF");
                button.SetLatched(on);
            });
        }

        private void Stepper(RectTransform parent, Rect area, string title, Func<string> get,
            Func<bool> canDecrease, Func<bool> canIncrease, Action<int> change,
            string step, string minimum, string maximum, bool band = false)
        {
            AvNode row = StepperRow("row").Arrange(area);
            AvStyled.Box(parent, area, band ? "section band" : "section");
            AvStyled.SpineTick(parent, area.x - AvScreen.SpineInset + 3f, area.y - 16f);
            AvStyled.Label(parent, row.At("label"), title, "section-title",
                align: TextAlignmentOptions.MidlineLeft);

            string setting = title.ToLowerInvariant();
            var minus = AvStyled.Button(parent, row.At("minus"), "-", "btn", () =>
            {
                change(-1);
                nextTick = 0f;
            }).WithTooltip("Decrease " + setting + " by " + step + "; minimum is " + minimum + ".");
            var value = AvStyled.Label(parent, row.At("value"), "", "row-value",
                align: TextAlignmentOptions.Center);
            var plus = AvStyled.Button(parent, row.At("plus"), "+", "btn", () =>
            {
                change(1);
                nextTick = 0f;
            }).WithTooltip("Increase " + setting + " by " + step + "; maximum is " + maximum + ".");

            refreshers.Add(() =>
            {
                minus.SetEnabled(canDecrease());
                plus.SetEnabled(canIncrease());
                value.text = get();
            });
        }

        private void RefreshPanel()
        {
            foreach (Action refresh in refreshers) refresh();
            shell?.WriteStatus(null, MapPicker.Prompt,
                "Changes save immediately to the Command configuration.");
        }

        public void ResetForScene()
        {
            MfdBezel.Release(MfdSlots.Set);
            if (boundScreens != null && boundSlot >= 0 && boundSlot < boundScreens.Count &&
                ReferenceEquals(boundScreens[boundSlot], screen)) boundScreens[boundSlot] = null;
            if (root != null) Destroy(root);
            root = null;
            screen = null;
            shell = null;
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
