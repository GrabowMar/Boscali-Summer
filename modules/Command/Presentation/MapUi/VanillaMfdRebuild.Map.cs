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
        // --------------------------------------------------------------- MAP panel

        private sealed class MapPresenter : Presenter
        {
            private sealed class ToggleBinding
            {
                public AvButton Button;
                public Func<bool> IsOn;
                public string Label;
            }

            private readonly MapOptions options;
            private readonly MfdGlyph[] previewSymbols = new MfdGlyph[3];
            private TMP_Text previewCaption;
            private readonly List<ToggleBinding> toggles = new List<ToggleBinding>();

            public MapPresenter(MFDScreen screen, MapOptions options)
                : base(screen, VanillaMfdPanelId.Map)
            {
                this.options = options;
            }

            protected override void BuildContent()
            {
                RectTransform page = CreatePage("MapOptions");
                DrawSpine(page);

                float y = -AvTokens.Space1;
                y = Heading(page, y, Shell.Body.width, "MARKERS", "LIVE MAP LAYERS");
                AddGrid(page, ref y, 4,
                    new[] { "OBJECTIVES", "TARGETS", "JAMMING", "LABELS" },
                    new Func<bool>[]
                    {
                        () => options.showObjectives,
                        () => options.showTargetInfo,
                        () => options.showJamming,
                        () => options.showGridLabels,
                    },
                    new Action[]
                    {
                        options.ToggleShowObjectives,
                        options.ToggleShowTargetInfo,
                        options.ToggleShowJamming,
                        options.ToggleShowGridLabels,
                    });

                y = Heading(page, y, Shell.Body.width, "TOOLTIP", "MAP HOVER DETAIL");
                AddGrid(page, ref y, 4,
                    new[] { "HIDE", "INFO", "AMMO", "ORDERS" },
                    new Func<bool>[]
                    {
                        () => options.tooltipType == MapOptions.TooltipType.None,
                        () => options.tooltipType == MapOptions.TooltipType.Info,
                        () => options.tooltipType == MapOptions.TooltipType.Ammo,
                        () => options.tooltipType == MapOptions.TooltipType.Order,
                    },
                    new Action[]
                    {
                        () => options.SetToolTipType((int)MapOptions.TooltipType.None),
                        () => options.SetToolTipType((int)MapOptions.TooltipType.Info),
                        () => options.SetToolTipType((int)MapOptions.TooltipType.Ammo),
                        () => options.SetToolTipType((int)MapOptions.TooltipType.Order),
                    });

                y = Heading(page, y, Shell.Body.width, "ICON SCALE", "TACTICAL SYMBOL SIZE");
                AddGrid(page, ref y, 3,
                    new[] { "SMALL", "MEDIUM", "LARGE" },
                    new Func<bool>[]
                    {
                        () => Mathf.Approximately(options.iconSize, 0.6f),
                        () => Mathf.Approximately(options.iconSize, 0.8f),
                        () => Mathf.Approximately(options.iconSize, 1.0f),
                    },
                    new Action[]
                    {
                        () => options.SetIconSize(0),
                        () => options.SetIconSize(1),
                        () => options.SetIconSize(2),
                    });

                y = Heading(page, y, Shell.Body.width, "SPECIAL ICONS");
                AddGrid(page, ref y, 2,
                    new[] { "PILOTS", "AIRBASES" },
                    new Func<bool>[] { () => options.showPilotIcons, () => options.showAirbaseIcon },
                    new Action[] { options.ToggleShowPilotIcons, options.ToggleShowAirbaseIcons });
                y = Heading(page, y, Shell.Body.width, "SYMBOL PREVIEW", "CURRENT ICON SCALE");
                AvKit.TacticalCard(page, new Rect(AvTokens.Space3, y, Shell.Body.width-AvTokens.Space3, 112f), AvTheme.RailInfo);
                string[] symbols = { "AIR", "GND", "SHP" };
                for (int i = 0; i < symbols.Length; i++)
                {
                    float x = AvTokens.Space4 + i*128f;
                    var go = new GameObject("PreviewSymbol", typeof(RectTransform), typeof(MfdGlyph));
                    go.transform.SetParent(page, false);
                    previewSymbols[i] = go.GetComponent<MfdGlyph>();
                    AvKit.Place(previewSymbols[i].rectTransform, new Rect(x+42f, y-14f, 28f, 28f));
                    previewSymbols[i].color = AvTheme.RailInfo;
                    previewSymbols[i].raycastTarget = false;
                    previewSymbols[i].Set(symbols[i]);
                    AvStyled.Label(page, new Rect(x, y-52f, 110f, 18f), symbols[i], "row-main",
                        align: TextAlignmentOptions.Center);
                }
                previewCaption = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y-82f, Shell.Body.width-AvTokens.Space5, 20f), "", "row-sub");
            }

            protected override void RefreshContent()
            {
                Shell.DataBar.State.text = "DISPLAY FILTERS";
                Shell.DataBar.SetChip(0, options.showObjectives ? "OBJ ON" : "OBJ OFF", options.showObjectives);
                Shell.DataBar.SetChip(1, options.showTargetInfo ? "TGT ON" : "TGT OFF", options.showTargetInfo);
                Shell.DataBar.SetChip(2, "SCALE " + IconSizeLabel(), true);
                for (int i = 0; i < previewSymbols.Length; i++)
                    previewSymbols[i].rectTransform.localScale = Vector3.one * options.iconSize;
                previewCaption.text = "SCALE " + IconSizeLabel() + "   HOVER " + options.tooltipType.ToString().ToUpperInvariant();
                for (int i = 0; i < toggles.Count; i++)
                    PaintButton(toggles[i].Button, toggles[i].Label, toggles[i].IsOn());
            }

            protected override string AmbientStatus() =>
                "MAP FILTERS — " + (options.showObjectives ? "OBJECTIVES" : "OBJECTIVES HIDDEN");

            private string IconSizeLabel()
            {
                if (options.iconSize < 0.7f) return "S";
                if (options.iconSize < 0.9f) return "M";
                return "L";
            }

            private void AddGrid(RectTransform parent, ref float y, int columns,
                                 string[] labels, Func<bool>[] states, Action[] actions)
            {
                float gap = AvTokens.Gap;
                float width = (Shell.Body.width - AvTokens.Space3 - gap * (columns - 1)) / columns;
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    int row = i / columns;
                    int column = i % columns;
                    var button = PanelButton(parent,
                        new Rect(AvTokens.Space3 + column * (width + gap),
                                 y - row * (AvTokens.RowHeight + gap), width, AvTokens.RowHeight),
                        labels[i], "toggle", () =>
                        {
                            actions[index]();
                            RequestRefresh();
                        }, AvButtonStyle.Toggle);
                    button.WithTooltip(labels[i] + " MAP OPTION");
                    toggles.Add(new ToggleBinding { Button = button, IsOn = states[i], Label = labels[i] });
                }
                int rows = Mathf.CeilToInt(labels.Length / (float)columns);
                y -= rows * (AvTokens.RowHeight + gap) + AvTokens.Space3;
            }
        }
    }
}
