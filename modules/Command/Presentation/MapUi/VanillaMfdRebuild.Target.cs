using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // ------------------------------------------------------------ target panel

        private sealed class TargetPresenter : Presenter
        {
            private readonly TargetListSelector selector;
            private readonly List<Unit> selectedUnits = new List<Unit>();

            private RectTransform[] pages;
            private MfdPagingGrid factionGrid;
            private MfdPagingGrid unitGrid;
            private MfdPagingGrid vehicleGrid;
            private MfdPagingGrid selectedGrid;
            private AvButton resetFilters;
            private AvButton clearTargets;
            private AvButton followHud;
            private AvButton laser;
            private MfdPagingGrid presetGrid;
            private TMPro.TMP_Text presetStatus;

            // Camera surface mark: state lives in Support through a narrow contract.
            private ICameraTargetService cameraService;
            private Image cameraRail;
            private TMPro.TMP_Text cameraState;
            private TMPro.TMP_Text cameraDetails;
            private AvButton cameraCapture;
            private AvButton cameraCall;
            private AvButton cameraClear;
            private TMPro.TMP_Text cameraPos;
            private TMPro.TMP_Text cameraRange;
            private TMPro.TMP_Text cameraAge;
            private TMPro.TMP_Text cameraArmed;

            public TargetPresenter(MFDScreen screen, TargetListSelector selector)
                : base(screen, VanillaMfdPanelId.Tgt)
            {
                this.selector = selector;
            }

            private ICameraTargetService Camera
            {
                get
                {
                    if (cameraService == null) ModServices.TryGet(out cameraService);
                    return cameraService;
                }
            }

            protected override int TabCount => 4;

            protected override void BuildContent()
            {
                ConfigureTabs(new[] { "FILTERS", "PRESETS", "SELECTED", "CAMERA" }, SelectPage);
                pages = new[]
                {
                    CreatePage("Filters"), CreatePage("Presets"),
                    CreatePage("Selected"), CreatePage("Camera")
                };
                BuildFiltersPage(pages[0]);
                BuildPresetsPage(pages[1]);
                BuildSelectedPage(pages[2]);
                BuildCameraPage(pages[3]);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                RefreshCamera();

                if (!Ready)
                {
                    SetFilterInput(false);
                    Shell.DataBar.State.text = "WAITING FOR TARGET FILTERS";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }

                SetFilterInput(true);
                int filters = CountEnabled(selector.toggleFactionItems) +
                              CountEnabled(selector.toggleUnitTypesItems) +
                              CountEnabled(selector.toggleVehicleTypesItems);
                Shell.DataBar.State.text = "TARGET ACQUISITION";
                Shell.DataBar.SetChip(0, filters + " FILTERS", filters > 0);
                Shell.DataBar.SetChip(1, selector.toggleFollowHUD.status ? "HUD LINK" : "MANUAL", true);
                Shell.DataBar.SetChip(2, selector.toggleLaser.status ? "LASER" : "NO LASER",
                                      selector.toggleLaser.status);

                PaintButton(resetFilters, "RESET", MatchesPreset(MfdTargetPreset.All));
                clearTargets.SetEnabled(SelectedCount() > 0);
                PaintButton(followHud, "HUD LINK", selector.toggleFollowHUD.status);
                PaintButton(laser, "LASER", selector.toggleLaser.status);

                SetGrid(factionGrid, selector.toggleFactionItems, ToggleFaction);
                SetGrid(unitGrid, selector.toggleUnitTypesItems, ToggleUnitType);
                SetGrid(vehicleGrid, selector.toggleVehicleTypesItems, ToggleVehicleType);
                RefreshSelectedGrid();
                presetGrid.SetData(MfdTargetPresets.Names.Length, i => MfdTargetPresets.Names[i],
                    i => MatchesPreset((MfdTargetPreset)i), ApplyPreset);
                string active = "CUSTOM";
                for (int i = 0; i < MfdTargetPresets.Names.Length; i++)
                    if (MatchesPreset((MfdTargetPreset)i)) { active = MfdTargetPresets.Names[i]; break; }
                presetStatus.text = selector.toggleFollowHUD.status ? "HUD LINK CONTROLS FILTERS" : "ACTIVE PROFILE  " + active;
            }

            protected override string AmbientStatus() =>
                "LEFT CLICK TO TOGGLE — RIGHT CLICK TO SHOW ONLY ONE FILTER";

            private void BuildFiltersPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "TARGET FILTER", "LIVE MAP ACQUISITION");
                float gap = AvTokens.Gap;
                float width = (Shell.Body.width - AvTokens.Space3 - gap * 3f) / 4f;
                resetFilters = PanelButton(page, new Rect(AvTokens.Space3, y, width, AvTokens.RowHeight),
                    "RESET", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.ResetFilters();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                clearTargets = PanelButton(page,
                    new Rect(AvTokens.Space3 + (width + gap), y, width, AvTokens.RowHeight),
                    "CLEAR", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.DeselectAll();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                followHud = PanelButton(page,
                    new Rect(AvTokens.Space3 + 2f * (width + gap), y, width, AvTokens.RowHeight),
                    "HUD LINK", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.toggleFollowHUD.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    },
                    AvButtonStyle.Toggle);
                laser = PanelButton(page,
                    new Rect(AvTokens.Space3 + 3f * (width + gap), y, width, AvTokens.RowHeight),
                    "LASER", "toggle", () =>
                    {
                        if (!Ready) return;
                        selector.toggleLaser.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                y -= AvTokens.RowHeight + AvTokens.Space2;

                y = Heading(page, y, Shell.Body.width, "FACTION", "FRIEND / HOSTILE");
                factionGrid = new MfdPagingGrid(page, y, Shell.Body.width, 2, 1, pager: false);
                AddRightClickActions(factionGrid, OnlyFaction);
                y -= AvTokens.RowHeight + AvTokens.Space2;

                y = Heading(page, y, Shell.Body.width, "UNIT CLASS", "AIR / GROUND / SEA");
                unitGrid = new MfdPagingGrid(page, y, Shell.Body.width, 3, 2, pager: false);
                AddRightClickActions(unitGrid, OnlyUnitType);
                y -= AvTokens.RowHeight * 2f + AvTokens.Gap + AvTokens.Space2;

                y = Heading(page, y, Shell.Body.width, "PLATFORM TYPE", "DETAILED FILTER");
                vehicleGrid = new MfdPagingGrid(page, y, Shell.Body.width, 3, 4);
                AddRightClickActions(vehicleGrid, OnlyVehicleType);
            }

            private void BuildPresetsPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width, "QUICK ACQUISITION", "ONE CLICK PROFILES");
                presetGrid = new MfdPagingGrid(page, y, Shell.Body.width, 2, 4, pager: false);
                y -= 4f * (AvTokens.RowHeight + AvTokens.Gap) + AvTokens.Space3;
                presetStatus = AvStyled.Label(page, new Rect(AvTokens.Space3, y, Shell.Body.width-AvTokens.Space3, 24f),
                    "ACTIVE PROFILE", "row-main");
                y -= 30f;
                for (int i = 0; i < MfdTargetPresets.Names.Length; i++)
                {
                    AvStyled.Label(page, new Rect(AvTokens.Space3, y, Shell.Body.width-AvTokens.Space3, 18f),
                        MfdTargetPresets.Names[i] + "  " + MfdTargetPresets.Descriptions[i], "row-sub");
                    y -= 21f;
                }
                AvStyled.Label(page, new Rect(AvTokens.Space3, y-4f, Shell.Body.width-AvTokens.Space3, 28f),
                    "Manual filters. Neutral units use vanilla rules.", "row-sub");
            }

            private void ApplyPreset(int index)
            {
                if (!Ready || index < 0 || index >= MfdTargetPresets.Names.Length) return;
                var preset = (MfdTargetPreset)index;
                // Set raises the native OnToggle event, resetting filters when unlinking.
                // Unlink first so that callback cannot overwrite the chosen preset.
                if (selector.toggleFollowHUD.status) selector.toggleFollowHUD.Set(false);
                selector.ResetFilters();
                foreach (var entry in selector.toggleFactionItems)
                    if (entry != null) entry.Set(MfdTargetPresets.Faction(preset, entry.sameFaction));
                foreach (var entry in selector.toggleUnitTypesItems)
                    if (entry != null) entry.Set(IncludesClass(preset, entry));
                foreach (var entry in selector.toggleVehicleTypesItems)
                    if (entry != null) entry.Set(IncludesVehicle(preset, entry));
                selector.toggleLaser.Set(preset == MfdTargetPreset.Laser);
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private bool MatchesPreset(MfdTargetPreset preset)
            {
                if (!Ready || selector.toggleFollowHUD.status ||
                    selector.toggleLaser.status != (preset == MfdTargetPreset.Laser)) return false;
                foreach (var entry in selector.toggleFactionItems)
                    if (entry != null && entry.status != MfdTargetPresets.Faction(preset, entry.sameFaction)) return false;
                foreach (var entry in selector.toggleUnitTypesItems)
                    if (entry != null && entry.status != IncludesClass(preset, entry)) return false;
                foreach (var entry in selector.toggleVehicleTypesItems)
                    if (entry != null && entry.status != IncludesVehicle(preset, entry)) return false;
                return true;
            }

            private static bool IncludesClass(MfdTargetPreset preset, TargetListSelector_ToggleButton entry)
            {
                if (entry.listUnitTypes != null)
                    foreach (var definition in entry.listUnitTypes)
                        if (definition != null && MfdTargetPresets.UnitClass(preset, definition.GetType().Name)) return true;
                return MfdTargetPresets.UnitClass(preset, "");
            }

            private static bool IncludesVehicle(MfdTargetPreset preset, TargetListSelector_ToggleButton entry)
            {
                if (preset != MfdTargetPreset.Sead) return true;
                if (entry.listDefinitions != null)
                    foreach (var definition in entry.listDefinitions)
                        if (definition is VehicleDefinition vehicle &&
                            MfdTargetPresets.Vehicle(preset, vehicle.vehicleType.ToString())) return true;
                return false;
            }

            private void BuildSelectedPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "SELECTED TARGETS", "MAP ICONS");
                selectedGrid = new MfdPagingGrid(page, y, Shell.Body.width, 1, 9, readOnly: true);
            }

            private void BuildCameraPage(RectTransform page)
            {
                DrawSpine(page);
                float width = Shell.Body.width;
                float y = Heading(page, -AvTokens.Space1, width, "CAMERA MARK", "SURFACE SENSOR TARGET");

                AvStyled.Box(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3 * 2f, 64f), "section band");
                cameraRail = AvStyled.Rail(page, new Rect(AvTokens.Space3 + 6f, y - 6f, 3f, 52f), "locked");
                cameraState = AvStyled.Label(page,
                    new Rect(AvTokens.Space3 + 16f, y - 6f, width - AvTokens.Space3 * 2f - 24f, 16f),
                    "NO ACTIVE MARK", "section-title");
                cameraDetails = AvStyled.Label(page,
                    new Rect(AvTokens.Space3 + 16f, y - 24f, width - AvTokens.Space3 * 2f - 24f, 36f),
                    "Aim the cockpit camera at a surface point and press MARK CAMERA.", "row-sub");
                y -= 76f;

                float gap = AvTokens.Gap;
                float buttonWidth = (width - AvTokens.Space3 * 2f - gap * 2f) / 3f;
                cameraCapture = PanelButton(page,
                    new Rect(AvTokens.Space3, y, buttonWidth, AvTokens.RowHeight),
                    "MARK CAMERA", "toggle",
                    () => { Camera?.Capture(); RequestRefresh(); }, AvButtonStyle.Toggle);
                cameraCall = PanelButton(page,
                    new Rect(AvTokens.Space3 + buttonWidth + gap, y, buttonWidth, AvTokens.RowHeight),
                    "CALL AT MARK", "toggle",
                    () => { Camera?.CallAtMark(); RequestRefresh(); }, AvButtonStyle.Toggle);
                cameraClear = PanelButton(page,
                    new Rect(AvTokens.Space3 + (buttonWidth + gap) * 2f, y, buttonWidth, AvTokens.RowHeight),
                    "CLEAR MARK", "toggle",
                    () => { Camera?.Clear(); RequestRefresh(); }, AvButtonStyle.Toggle);
                y -= AvTokens.RowHeight + AvTokens.Space2;

                y = Heading(page, y, width, "TARGET TELEMETRY", "COORDINATES & RANGE");
                cameraPos = CameraKey(page, y, width, "COORDINATES (X/Z)");
                y -= 18f;
                cameraRange = CameraKey(page, y, width, "SLANT RANGE");
                y -= 18f;
                cameraAge = CameraKey(page, y, width, "MARK AGE");
                y -= 18f;
                cameraArmed = CameraKey(page, y, width, "ARMED CALL-IN");
                y -= 24f;

                AvStyled.Label(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3 * 2f, 30f),
                    "The mark is a surface reference, not a tracked contact. Arm an operation on OPS / SUPPORT, then CALL AT MARK.",
                    "row-sub");
            }

            private static TMPro.TMP_Text CameraKey(RectTransform page, float y, float width, string key)
            {
                AvStyled.Label(page, new Rect(AvTokens.Space3, y, width * 0.55f, 16f), key, "kv-key");
                return AvStyled.Label(page,
                    new Rect(AvTokens.Space3 + width * 0.55f, y, width * 0.45f - AvTokens.Space3, 16f),
                    "—", "kv-value", align: TMPro.TextAlignmentOptions.MidlineRight);
            }

            private void RefreshCamera()
            {
                if (cameraState == null) return;
                ICameraTargetService service = Camera;
                if (service == null || !service.Available)
                {
                    cameraRail.color = AvTheme.RailInert;
                    cameraState.text = "CAMERA MARKING UNAVAILABLE";
                    cameraState.color = AvTheme.Dim;
                    cameraDetails.text = "The support module or its observation source is not installed.";
                    cameraCapture?.SetEnabled(false);
                    cameraCall?.SetEnabled(false);
                    cameraClear?.SetEnabled(false);
                    SetCameraTelemetry("—", "—", "—", "—");
                    return;
                }

                bool marked = service.HasMark;
                bool armed = !string.IsNullOrEmpty(service.ArmedActionName);
                if (marked)
                {
                    ObservationPoint point = service.Mark;
                    cameraRail.color = AvTheme.RailReady;
                    cameraState.text = (point.Source ?? "SENSOR").ToUpperInvariant() + " SURFACE MARK";
                    cameraState.color = AvTheme.RailReady;
                    cameraDetails.text = "Surface reference recorded; expires 120 seconds after capture.";
                    SetCameraTelemetry(
                        "X " + point.X.ToString("0") + " · Z " + point.Z.ToString("0"),
                        (point.Range / 1000f).ToString("0.0") + " km",
                        service.AgeSeconds.ToString("0") + "s",
                        armed ? service.ArmedActionName : "NONE (ARM IN OPS)");
                }
                else
                {
                    cameraRail.color = AvTheme.RailInert;
                    cameraState.text = "NO ACTIVE MARK";
                    cameraState.color = AvTheme.Dim;
                    cameraDetails.text = service.Status.ToUpperInvariant() + " · AIM AND PRESS MARK CAMERA.";
                    SetCameraTelemetry("—", "—", "—", armed ? service.ArmedActionName : "NONE (ARM IN OPS)");
                }

                cameraCapture.SetEnabled(service.CanCapture);
                cameraCall.SetEnabled(service.CanCallAtMark);
                cameraCall.SetText(armed ? "CALL AT MARK" : "SELECT IN OPS");
                cameraClear.SetEnabled(marked);
                cameraCapture.WithTooltip(service.CanCapture
                    ? "Record the surface point under the native camera."
                    : "Requires an active native camera view on your aircraft.");
                cameraCall.WithTooltip(!marked ? "Capture a mark first."
                    : !armed ? "Arm an operation on OPS / SUPPORT first."
                    : "Deliver the armed operation onto this mark.");
                cameraClear.WithTooltip(marked ? "Clear the active mark." : "No mark to clear.");
            }

            private void SetCameraTelemetry(string position, string range, string age, string armed)
            {
                cameraPos.text = position;
                cameraRange.text = range;
                cameraAge.text = age;
                cameraAge.color = AvTheme.TextPrimary;
                cameraArmed.text = armed;
                cameraArmed.color = armed.StartsWith("NONE") ? AvTheme.Dim : AvTheme.RailCaution;
            }

            private void SelectPage(int selected)
            {
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                SetSelectedTab(selected);
                RequestRefresh();
            }

            private void SetGrid(MfdPagingGrid grid, List<TargetListSelector_ToggleButton> entries,
                                 Action<int> onClick)
            {
                grid.SetData(entries == null ? 0 : entries.Count,
                    i => NativeTargetLabel(entries[i]),
                    i => entries[i] != null && entries[i].status,
                    onClick, icons: i => entries[i] == null || entries[i].image == null ? null : entries[i].image.sprite);
            }

            private void AddRightClickActions(MfdPagingGrid grid, Action<int> onOnly)
            {
                for (int i = 0; i < 12; i++)
                {
                    int slot = i;
                    AvButton button = grid.ButtonAt(i);
                    if (button == null) continue;
                    MfdRightClickAction action = button.gameObject.AddComponent<MfdRightClickAction>();
                    action.Configure(() => onOnly(grid.CurrentIndex(slot)));
                }
            }

            private void ToggleFaction(int index) => Toggle(selector.toggleFactionItems, index);
            private void ToggleUnitType(int index) => Toggle(selector.toggleUnitTypesItems, index);
            private void ToggleVehicleType(int index) => Toggle(selector.toggleVehicleTypesItems, index);

            private void OnlyFaction(int index) => SetOnly(selector.toggleFactionItems, index);
            private void OnlyUnitType(int index) => SetOnly(selector.toggleUnitTypesItems, index);
            private void OnlyVehicleType(int index) => SetOnly(selector.toggleVehicleTypesItems, index);

            private void Toggle(List<TargetListSelector_ToggleButton> entries, int index)
            {
                if (!Ready) return;
                if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return;
                entries[index].Toggle();
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private void SetOnly(List<TargetListSelector_ToggleButton> entries, int index)
            {
                if (!Ready) return;
                if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return;
                selector.SetOnlyItem(entries[index]);
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private void RefreshSelectedGrid()
            {
                selectedUnits.Clear();
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && map.selectedIcons != null)
                {
                    for (int i = 0; i < map.selectedIcons.Count; i++)
                    {
                        UnitMapIcon icon = map.selectedIcons[i] as UnitMapIcon;
                        if (icon != null && icon.unit != null) selectedUnits.Add(icon.unit);
                    }
                }

                selectedGrid.SetData(selectedUnits.Count,
                    i => TargetUnitLabel(selectedUnits[i]), i => false, null,
                    icons: i => selectedUnits[i].definition == null ? null : selectedUnits[i].definition.mapIcon);
            }

            private int SelectedCount()
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                return map == null || map.selectedIcons == null ? 0 : map.selectedIcons.Count;
            }

            private bool Ready => selector != null && selector.toggleFollowHUD != null &&
                                  selector.toggleLaser != null && selector.toggleFactionItems != null &&
                                  selector.toggleUnitTypesItems != null &&
                                  selector.toggleVehicleTypesItems != null && selector.toggleFactionItems.Count > 0 &&
                                  selector.toggleUnitTypesItems.Count > 0 && selector.toggleVehicleTypesItems.Count > 0;

            private void SetFilterInput(bool enabled)
            {
                resetFilters?.SetEnabled(enabled);
                clearTargets?.SetEnabled(enabled);
                followHud?.SetEnabled(enabled);
                laser?.SetEnabled(enabled);
                factionGrid?.SetInteractable(enabled);
                unitGrid?.SetInteractable(enabled);
                vehicleGrid?.SetInteractable(enabled);
                selectedGrid?.SetInteractable(enabled);
                presetGrid?.SetInteractable(enabled);
            }

            private static int CountEnabled(List<TargetListSelector_ToggleButton> entries)
            {
                if (entries == null) return 0;
                int count = 0;
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i] != null && entries[i].status) count++;
                return count;
            }

            private static string NativeTargetLabel(TargetListSelector_ToggleButton entry)
            {
                if (entry != null && entry.label != null && !string.IsNullOrEmpty(entry.label.text))
                    return AvTheme.Truncate(entry.label.text.Replace("\n", " ").ToUpperInvariant(), 22);
                return NativeLabel(entry, "FILTER");
            }

            private static string TargetUnitLabel(Unit unit)
            {
                if (unit == null) return "UNKNOWN TARGET";
                string code = unit.definition == null ? "UNIT" : unit.definition.code;
                string name = string.IsNullOrEmpty(unit.unitName) ? code : unit.unitName;
                return AvTheme.Truncate((name ?? "UNIT").ToUpperInvariant(), 34);
            }
        }
    }
}
