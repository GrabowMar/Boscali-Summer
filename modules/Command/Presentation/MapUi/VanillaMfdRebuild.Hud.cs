using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // --------------------------------------------------------------- HUD panel

        private sealed class HudPresenter : Presenter
        {
            private readonly HUDOptions options;
            private readonly Dictionary<HUDOptions_ToggleButton, string> labels =
                new Dictionary<HUDOptions_ToggleButton, string>();
            private RectTransform[] pages;
            private MfdPagingGrid modes;
            private MfdPagingGrid categories;
            private MfdPagingGrid vehicles;
            private MfdPagingGrid buildings;
            private TMPro.TMP_Text modeBrief;

            public HudPresenter(MFDScreen screen, HUDOptions options)
                : base(screen, VanillaMfdPanelId.Hud)
            {
                this.options = options;
            }

            protected override int TabCount => 3;

            protected override void BuildContent()
            {
                ConfigureTabs(new[] { "MODE", "VEHICLES", "BUILDINGS" }, SelectPage);
                pages = new[]
                {
                    CreatePage("HudModes"),
                    CreatePage("HudVehicles"),
                    CreatePage("HudBuildings"),
                };

                BuildModePage(pages[0]);

                // Two-line cells: a vehicle or building name gets the room to print whole.
                // The row count is the floor the body can hold; the pitch then takes the
                // rest, so the list reaches its pager at 596 and at 896 instead of leaving
                // a band of nothing under the last row. The 70px chrome is the heading,
                // the pager and the Space1/Space2 outside them.
                float body = PageHeight;
                const float chrome = 70f;
                int rows = Mathf.Clamp(Mathf.FloorToInt((body - chrome) / 46f), 3, 8);
                float cell = 52f;

                DrawSpine(pages[1]);
                float vehicleTop = Heading(pages[1], -AvTokens.Space1, PageWidth,
                                            "VEHICLE PRIORITY", "ONE TYPE AT A TIME");
                vehicles = new MfdPagingGrid(pages[1], vehicleTop, PageWidth, 2, rows,
                                             rowHeight: cell);

                DrawSpine(pages[2]);
                float buildingTop = Heading(pages[2], -AvTokens.Space1, PageWidth,
                                             "BUILDING PRIORITY", "ONE TYPE AT A TIME");
                buildings = new MfdPagingGrid(pages[2], buildingTop, PageWidth, 2, rows,
                                              rowHeight: cell);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                // These lists are populated by the native component's startup pass. The
                // dock can attach a host one frame sooner, so wait for a complete model
                // rather than logging an error every refresh or freezing a partial view.
                if (options == null || options.listModes == null || options.listCategories == null ||
                    options.listVehicleTypes == null || options.listBuildingTypes == null)
                {
                    SetGridInput(false);
                    Shell.DataBar.State.text = "WAITING FOR HUD OPTIONS";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }

                Shell.DataBar.State.text = "HUD PRIORITY MATRIX";
                SetGridInput(true);
                Shell.DataBar.SetChip(0, options.currentMode.ToString(), true);
                Shell.DataBar.SetChip(1, CountEnabled(options.listVehicleTypes) + " VEH", true);
                Shell.DataBar.SetChip(2, CountEnabled(options.listBuildingTypes) + " BLD", true);

                string brief;
                switch (options.currentMode.ToString())
                {
                    case "NAV": brief = "Navigation profile"; break;
                    case "GUN": brief = "Gunnery profile"; break;
                    case "A2A": brief = "Air-to-air profile"; break;
                    case "A2G": brief = "Air-to-ground profile"; break;
                    case "EW": brief = "Electronic warfare profile"; break;
                    default: brief = "Logistics profile"; break;
                }
                modeBrief.text = brief + "\n" + CountEnabled(options.listVehicleTypes) + " vehicle types / " +
                    CountEnabled(options.listBuildingTypes) + " building types prioritised.\n" +
                    "ON marks an active choice. Priority gates control which contacts the HUD emphasises.";

                modes.SetData(options.listModes.Count,
                    i => i < 6 ? ((HUDOptions.HUDMode)i).ToString() : LabelFor(options.listModes[i], "MODE"),
                    i => options.listModes[i] != null && options.listModes[i].status,
                    SelectMode);
                categories.SetData(options.listCategories.Count,
                    i => NativeCategoryLabel(options.listCategories[i], i),
                    i => options.listCategories[i] != null && options.listCategories[i].maximized,
                    ToggleCategory);
                vehicles.SetData(options.listVehicleTypes.Count,
                    i => LabelFor(options.listVehicleTypes[i], "VEHICLE"),
                    i => options.listVehicleTypes[i] != null && options.listVehicleTypes[i].status,
                    SelectVehicle, icons: i => DefinitionIcon(options.listVehicleTypes[i]));
                buildings.SetData(options.listBuildingTypes.Count,
                    i => LabelFor(options.listBuildingTypes[i], "BUILDING"),
                    i => options.listBuildingTypes[i] != null && options.listBuildingTypes[i].status,
                    SelectBuilding, icons: i => DefinitionIcon(options.listBuildingTypes[i]));
            }

            protected override string AmbientStatus() =>
                "HUD " + options.currentMode + " — SELECT WHAT GETS EMPHASISED";

            private void BuildModePage(RectTransform root)
            {
                DrawSpine(root);
                // Fixed two-column choices keep the full class names readable.
                const float cell = 48f;
                const float brief = 112f;

                float y = Heading(root, -AvTokens.Space1, PageWidth,
                                  "ENGAGEMENT MODE", "AUTO HUD PROFILE");
                modes = new MfdPagingGrid(root, y, PageWidth, 2, 3, pager: false, rowHeight: cell, exclusive: true);
                y -= cell * 3f + AvTokens.Space3;
                y = Heading(root, y, PageWidth, "PRIORITY GATES", "MAXIMISE TRACKS");
                categories = new MfdPagingGrid(root, y, PageWidth, 2, 3, pager: false, rowHeight: cell);
                y -= cell * 3f + AvTokens.Space3;
                y = Heading(root, y, PageWidth, "PROFILE READOUT", "LIVE HUD SETTINGS");
                AvKit.TacticalCard(root, new Rect(AvTokens.Space3, y, PageWidth-AvTokens.Space3, brief), AvTheme.RailInfo);
                // The copy sits on the card's midline, so a tall body reads as a panel
                // with the brief on it rather than a block of text stranded at the top.
                modeBrief = AvStyled.Label(root,
                    new Rect(AvTokens.Space5, y - Mathf.Max(10f, (brief - 82f) * 0.5f),
                             PageWidth-AvTokens.Space5*2f, 82f), "", "row-main");
                modeBrief.enableWordWrapping = true;
            }

            private void SelectPage(int next)
            {
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == next);
                SetSelectedTab(next);
                RequestRefresh();
            }

            private void SelectMode(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listModes.Count) return;
                HUDOptions_ToggleButton source = options.listModes[index];
                if (source == null) return;
                options.ToggleButtons(source);
                if (index < 6) options.currentMode = (HUDOptions.HUDMode)index;
                Persist();
            }

            private void ToggleCategory(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listCategories.Count) return;
                HUDOptions_Category category = options.listCategories[index];
                if (category == null) return;
                category.Set(!category.maximized);
                Persist();
            }

            private void SelectVehicle(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listVehicleTypes.Count) return;
                HUDOptions_ToggleButton source = options.listVehicleTypes[index];
                if (source == null) return;
                options.ToggleButtons(source);
                Persist();
            }

            private void SelectBuilding(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listBuildingTypes.Count) return;
                HUDOptions_ToggleButton source = options.listBuildingTypes[index];
                if (source == null) return;
                options.ToggleButtons(source);
                Persist();
            }

            private void Persist()
            {
                options.SaveSettings();
                options.NeedUpdateIcons();
                RequestRefresh();
            }

            private static Sprite DefinitionIcon(HUDOptions_ToggleButton button)
            {
                return button == null || button.listDefinitions == null || button.listDefinitions.Count == 0 ||
                    button.listDefinitions[0] == null ? null : button.listDefinitions[0].mapIcon;
            }

            private string LabelFor(HUDOptions_ToggleButton source, string fallback)
            {
                if (source == null) return fallback;
                if (!labels.TryGetValue(source, out string label))
                {
                    label = NativeLabel(source, fallback);
                    labels.Add(source, label);
                }
                return label;
            }

            private bool Ready => options != null && options.listModes != null &&
                                  options.listCategories != null &&
                                  options.listVehicleTypes != null &&
                                  options.listBuildingTypes != null;

            private void SetGridInput(bool enabled)
            {
                modes?.SetInteractable(enabled);
                categories?.SetInteractable(enabled);
                vehicles?.SetInteractable(enabled);
                buildings?.SetInteractable(enabled);
            }
        }
    }
}
