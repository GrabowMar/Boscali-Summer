using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        private sealed class MapPresenter : Presenter
        {
            private sealed class OptionBinding
            {
                internal AvButton Button;
                internal TMP_Text State;
                internal Func<bool> IsOn;
                internal Action Apply;
                internal string Label, Description;
                internal bool Layer;
            }

            private readonly MapOptions options;
            private readonly List<OptionBinding> controls = new List<OptionBinding>(13);
            private readonly MfdGlyph[] previewSymbols = new MfdGlyph[3];
            private RectTransform[] pages;
            private AvButton showAll, hideAll;
            private TMP_Text detailSummary, previewCaption;
            private int selectedPage;
            private int visibleLayers;
            private bool Available => options != null && SceneSingleton<DynamicMap>.i != null;

            public MapPresenter(MFDScreen screen, MapOptions options)
                : base(screen, VanillaMfdPanelId.Map) { this.options = options; }

            protected override int TabCount => 2;

            protected override void BuildContent()
            {
                ConfigureTabs(new[] { "LAYERS", "READABILITY" }, SelectPage);
                pages = new[] { CreatePage("Layers"), CreatePage("Readability") };
                BuildLayers(pages[0]);
                BuildReadability(pages[1]);
                SelectPage(0);
            }

            private void BuildLayers(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width, "MAP LAYERS", "CLICK ROW TO TOGGLE");
                float height = Mathf.Clamp(Mathf.Floor((Shell.Body.height - 84f) / 6f) - AvTokens.Gap, 48f, 72f);
                string[] names = { "OBJECTIVES", "TARGET DETAILS", "JAMMING", "GRID LABELS", "PILOTS", "AIRBASES" };
                string[] descriptions = { "Mission objective markers", "Target information markers", "Jamming indicators",
                    "Map grid coordinates", "Dismounted pilot icons", "Airbase icons" };
                Func<bool>[] states = { () => options.showObjectives, () => options.showTargetInfo,
                    () => options.showJamming, () => options.showGridLabels, () => options.showPilotIcons, () => options.showAirbaseIcon };
                Action[] actions = { () => options.ToggleShowObjectives(), () => options.ToggleShowTargetInfo(),
                    () => options.ToggleShowJamming(), () => options.ToggleShowGridLabels(),
                    () => options.ToggleShowPilotIcons(), () => options.ToggleShowAirbaseIcons() };
                for (int i = 0; i < names.Length; i++)
                {
                    AddOption(page, new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3, height),
                        names[i], descriptions[i], states[i], actions[i], true);
                    y -= height + AvTokens.Gap;
                }
                float width = (Shell.Body.width - AvTokens.Space3 - AvTokens.Gap) / 2f;
                showAll = AvStyled.Button(page, new Rect(AvTokens.Space3, y, width, 44f), "SHOW ALL", "row-main",
                    () => SetAllLayers(true), AvButtonStyle.Quiet);
                hideAll = AvStyled.Button(page, new Rect(AvTokens.Space3 + width + AvTokens.Gap, y, width, 44f), "HIDE ALL", "row-main",
                    () => SetAllLayers(false), AvButtonStyle.Quiet);
            }

            private void BuildReadability(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width, "HOVER DETAILS", "CHOOSE ONE");
                string[] names = { "OFF", "UNIT INFO", "AMMUNITION", "ORDERS" };
                string[] descriptions = { "No hover tooltip", "Unit information", "Weapon / ammo details", "Current unit orders" };
                MapOptions.TooltipType[] modes = { MapOptions.TooltipType.None, MapOptions.TooltipType.Info,
                    MapOptions.TooltipType.Ammo, MapOptions.TooltipType.Order };
                float width = (Shell.Body.width - AvTokens.Space3 - AvTokens.Gap) / 2f;
                for (int i = 0; i < modes.Length; i++)
                {
                    int index = i;
                    AddOption(page, new Rect(AvTokens.Space3 + i % 2 * (width + AvTokens.Gap),
                        y - i / 2 * 64f, width, 56f), names[i], descriptions[i],
                        () => options.tooltipType == modes[index], () => options.SetToolTipType((int)modes[index]), false);
                }
                y -= 136f;
                detailSummary = Label(page, new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3, 32f), "", 12f);
                y -= 40f;
                y = Heading(page, y, Shell.Body.width, "SYMBOL SIZE", "CHOOSE ONE");
                string[] sizes = { "SMALL", "MEDIUM", "LARGE" };
                width = (Shell.Body.width - AvTokens.Space3 - AvTokens.Gap * 2f) / 3f;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int index = i;
                    AddOption(page, new Rect(AvTokens.Space3 + i * (width + AvTokens.Gap), y, width, 56f),
                        sizes[i], (60 + 20 * i) + "% scale", () => Mathf.Approximately(options.iconSize, .6f + .2f * index),
                        () => options.SetIconSize(index), false, compact: true);
                }
                y -= 64f;
                y = Heading(page, y, Shell.Body.width, "SYMBOL PREVIEW", "ILLUSTRATIVE");
                float previewHeight = Mathf.Clamp(Shell.Body.height + y - AvTokens.Space2, 100f, 220f);
                AvKit.TacticalCard(page, new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3, previewHeight), AvTheme.RailInfo);
                string[] symbols = { "AIR", "GND", "SHP" };
                string[] labels = { "AIRCRAFT", "GROUND", "SHIP" };
                width = (Shell.Body.width - AvTokens.Space3) / 3f;
                for (int i = 0; i < symbols.Length; i++)
                {
                    float x = AvTokens.Space3 + width * i;
                    var go = new GameObject("PreviewSymbol", typeof(RectTransform), typeof(MfdGlyph));
                    go.transform.SetParent(page, false);
                    previewSymbols[i] = go.GetComponent<MfdGlyph>();
                    AvKit.Place(previewSymbols[i].rectTransform, new Rect(x + width / 2f, y - 26f, 28f, 28f));
                    previewSymbols[i].rectTransform.pivot = new Vector2(.5f, .5f);
                    previewSymbols[i].raycastTarget = false;
                    previewSymbols[i].Set(symbols[i]);
                    Label(page, new Rect(x, y - 48f, width, 18f), labels[i], 12f, TextAlignmentOptions.Center);
                }
                previewCaption = Label(page, new Rect(AvTokens.Space3 + 12f, y - 78f,
                    Shell.Body.width - AvTokens.Space3 - 24f, 32f), "", 12f);
            }

            private void AddOption(RectTransform parent, Rect area, string name, string description,
                Func<bool> state, Action apply, bool layer, bool compact = false)
            {
                var binding = new OptionBinding { IsOn = state, Apply = apply, Label = name, Description = description, Layer = layer };
                // Whole-row hit target; separate state text never depends on the theme's green latch.
                binding.Button = AvStyled.Button(parent, area, "", "row-main", () =>
                {
                    if (!Available) { RequestRefresh(); return; }
                    if (layer || !state()) apply();
                    RequestRefresh();
                }, AvButtonStyle.Quiet);
                var root = (RectTransform)binding.Button.transform;
                float inset = (area.height - 40f) / 2f;
                float stateWidth = layer ? 64f : compact ? 28f : 64f;
                Label(root, new Rect(12f, -inset, area.width - 28f - stateWidth, 20f), name, 14f);
                Label(root, new Rect(12f, -inset - 23f, area.width - 24f, 17f), description, 12f);
                binding.State = Label(root, new Rect(area.width - stateWidth - 12f, -inset, stateWidth, 20f), "—", 12f,
                    TextAlignmentOptions.MidlineRight);
                controls.Add(binding);
            }

            private static TMP_Text Label(RectTransform parent, Rect area, string text, float size,
                TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft) =>
                AvKit.Label(parent, text, area, AvTheme.TextPrimary, size, FontStyles.Normal, align, false);

            private void SelectPage(int selected)
            {
                selectedPage = selected;
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                SetSelectedTab(selected);
                RequestRefresh();
            }

            private void SetAllLayers(bool visible)
            {
                if (!Available) { RequestRefresh(); return; }
                // Six native setters at most; don't invert settings already at the requested state.
                foreach (OptionBinding binding in controls)
                    if (binding.Layer && binding.IsOn() != visible) binding.Apply();
                RequestRefresh();
            }

            protected override void RefreshContent()
            {
                bool available = Available;
                visibleLayers = 0;
                foreach (OptionBinding binding in controls)
                {
                    bool on = available && binding.IsOn();
                    if (binding.Layer && on) visibleLayers++;
                    binding.Button.SetEnabled(available);
                    binding.Button.SetLatched(on);
                    binding.State.text = !available ? "—" : binding.Layer ? (on ? "[ON]" : "[OFF]") : (on ? "[X]" : "[ ]");
                    binding.Button.WithTooltip(!available ? "Map options unavailable — waiting for the map." :
                        binding.Layer ? (on ? "Hide " : "Show ") + binding.Description.ToLowerInvariant() + "." :
                        (on ? "Selected: " : "Select ") + binding.Label + ". " + binding.Description + ".");
                }
                showAll.SetEnabled(available && visibleLayers < 6);
                hideAll.SetEnabled(available && visibleLayers > 0);
                showAll.WithTooltip(!available ? "Map options unavailable." : visibleLayers == 6 ? "All six layers are already shown." : "Show all six map layers. Hover detail and symbol size stay as selected.");
                hideAll.WithTooltip(!available ? "Map options unavailable." : visibleLayers == 0 ? "All six layers are already hidden." : "Hide all six map layers. Use Show all to restore them.");
                Shell.DataBar.State.text = available ? "MAP DISPLAY" : "WAITING FOR MAP";
                Shell.DataBar.SetChip(0, available ? "LAYERS " + visibleLayers + "/6" : "LAYERS —", available);
                Shell.DataBar.SetChip(1, available ? "HOVER " + TooltipName() : "HOVER —", available);
                Shell.DataBar.SetChip(2, available ? SizeLabel() : "SIZE —", available);
                detailSummary.text = !available ? "Map options are not available yet." : options.tooltipType == MapOptions.TooltipType.None
                    ? "Hover tooltips are hidden. Select a detail mode above."
                    : "[X] " + TooltipName() + " selected. Hover a map unit to inspect it.";
                float scale = available && !float.IsNaN(options.iconSize) && !float.IsInfinity(options.iconSize)
                    ? Mathf.Clamp(options.iconSize, .1f, 2f) : 1f;
                foreach (MfdGlyph symbol in previewSymbols)
                {
                    symbol.enabled = available;
                    symbol.rectTransform.localScale = Vector3.one * scale;
                }
                previewCaption.text = available ? SizeLabel() + " • Actual icons vary by unit."
                    : "Symbol size unavailable.";
            }

            private string TooltipName()
            {
                switch (options.tooltipType)
                {
                    case MapOptions.TooltipType.None: return "OFF";
                    case MapOptions.TooltipType.Info: return "INFO";
                    case MapOptions.TooltipType.Ammo: return "AMMO";
                    case MapOptions.TooltipType.Order: return "ORDERS";
                    default: return "UNKNOWN";
                }
            }

            private string SizeLabel()
            {
                if (Mathf.Approximately(options.iconSize, .6f)) return "SMALL 60%";
                if (Mathf.Approximately(options.iconSize, .8f)) return "MEDIUM 80%";
                if (Mathf.Approximately(options.iconSize, 1f)) return "LARGE 100%";
                return "CUSTOM SIZE";
            }

            protected override string AmbientStatus() => !Available ? "MAP CONTROLS UNAVAILABLE — WAITING FOR MAP" : selectedPage == 0
                ? visibleLayers + "/6 LAYERS SHOWN • EACH ROW SWITCHES ONE LAYER"
                : "[X] = SELECTED • HOVER DETAILS AND SYMBOL SIZE APPLY TO THE MAP";
        }
    }
}
