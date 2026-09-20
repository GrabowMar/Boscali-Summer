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
            private enum EditMode { None, SaveAs, Rename }

            private readonly TargetListSelector selector;
            private readonly List<Unit> selectedUnits = new List<Unit>();
            private readonly List<TargetPresetSnapshot> catalog = new List<TargetPresetSnapshot>();

            private RectTransform[] pages;
            private MfdPagingGrid factionGrid;
            private MfdPagingGrid unitGrid;
            private MfdPagingGrid vehicleGrid;
            private MfdPagingGrid selectedGrid;
            private MfdPagingGrid quickGrid;
            private MfdPagingGrid presetGrid;
            private AvButton resetFilters;
            private AvButton clearTargets;
            private AvButton followHud;
            private AvButton laser;
            private AvButton saveAs;
            private AvButton updatePreset;
            private AvButton renamePreset;
            private AvButton deletePreset;
            private TMPro.TMP_Text presetStatus;
            private TMPro.TMP_Text presetSummary;
            private TMPro.TMP_Text editorLabel;
            private TMPro.TMP_InputField nameField;
            private RectTransform editor;

            private string activePreset = MfdTargetPresets.Names[0];
            private string selectedPreset = "";
            private string echo;
            private float echoUntil;
            private string confirmDelete;
            private float confirmDeleteUntil;
            private int catalogVersion = -1;
            private int catalogStamp = -1;
            private int activeStamp = -1;
            private string activeCached = TargetPresetLibrary.CustomProfile;
            private EditMode editorMode = EditMode.None;
            private TMPro.TMP_Text selectedNote;
            private TMPro.TMP_Text presetNote;
            private int selectedVisible = 9;
            private int presetVisible = 6;

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
                // The active-profile chip carries the whole saved name: tracking off and
                // the micro floor keep it inside the fixed chip instead of cutting it.
                TMPro.TMP_Text profileChip = Shell.DataBar.Chips[1];
                profileChip.characterSpacing = 0f;
                profileChip.enableAutoSizing = true;
                profileChip.fontSizeMin = AvTokens.FontMicro;
                profileChip.fontSizeMax = profileChip.fontSize;
                profileChip.overflowMode = TMPro.TextOverflowModes.Overflow;
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
                    Shell.DataBar.State.text = "FILTER LINK WAIT";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }

                if (confirmDelete != null && Time.unscaledTime > confirmDeleteUntil)
                {
                    confirmDelete = null;
                    Echo("DELETE CANCELLED");
                }

                activePreset = CachedActive();
                EnsureCatalog();
                TrackSelectedPreset();
                SetFilterInput(true);

                int filters = CountEnabled(selector.toggleFactionItems) +
                              CountEnabled(selector.toggleUnitTypesItems) +
                              CountEnabled(selector.toggleVehicleTypesItems);
                Shell.DataBar.State.text = "TARGET FILTERS";
                Shell.DataBar.SetChip(0, filters + " FILTERS", filters > 0);
                Shell.DataBar.SetChip(1, activePreset,
                                      activePreset != TargetPresetLibrary.CustomProfile);
                Shell.DataBar.SetChip(2, selector.toggleFollowHUD.status ? "HUD LINK" :
                                      selector.toggleLaser.status ? "LASER" : "MANUAL",
                                      selector.toggleFollowHUD.status || selector.toggleLaser.status);

                int tracked = SelectedCount();
                clearTargets.SetEnabled(tracked > 0);
                clearTargets.WithTooltip(tracked > 0
                    ? "Drop every tracked contact from the target list."
                    : "No tracked contacts to clear.");
                PaintButton(followHud, selector.toggleFollowHUD.status ? "HUD ON" : "HUD OFF",
                            selector.toggleFollowHUD.status);
                PaintButton(laser, selector.toggleLaser.status ? "LASER ON" : "LASER OFF",
                            selector.toggleLaser.status);

                SetGrid(factionGrid, selector.toggleFactionItems, ToggleFaction);
                SetGrid(unitGrid, selector.toggleUnitTypesItems, ToggleUnitType);
                SetGrid(vehicleGrid, selector.toggleVehicleTypesItems, ToggleVehicleType);
                RefreshSelectedGrid();
                RefreshQuickSlots();
                RefreshLibrary();

                presetStatus.text = selector.toggleFollowHUD.status
                    ? "ACTIVE PROFILE  HUD LINK"
                    : "ACTIVE PROFILE  " + activePreset;
                presetSummary.text = DescribeSelected();
            }

            protected override string AmbientStatus()
            {
                if (!string.IsNullOrEmpty(echo) && Time.unscaledTime < echoUntil) return echo;
                if (Shell == null) return "LEFT CLICK TO TOGGLE — RIGHT CLICK TO SHOW ONLY ONE FILTER";
                if (Shell.Page == 1)
                    return "LEFT CLICK APPLIES — RIGHT CLICK ASSIGNS A QUICK SLOT";
                if (Shell.Page == 2)
                    return "RIGHT-CLICK A TRACKED CONTACT TO DROP IT — CLEAR IS ON FILTERS";
                return "LEFT CLICK TO TOGGLE — RIGHT CLICK TO SHOW ONLY ONE FILTER";
            }

            private void Echo(string text)
            {
                echo = text;
                echoUntil = Time.unscaledTime + 2.4f;
            }

            // ------------------------------------------------------------- filters

            private void BuildFiltersPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float gap = AvTokens.Gap;

                // Two columns make room for a full label, icon and explicit state.
                const float cell = 48f;

                float y = Heading(page, -AvTokens.Space1, width,
                                  "TARGET FILTER", "LIVE MAP ACQUISITION");
                float chipWidth = (width - AvTokens.Space3 - gap * 3f) / 4f;
                resetFilters = PanelButton(page, new Rect(AvTokens.Space3, y, chipWidth, AvTokens.RowHeight),
                    "RESET", "btn", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.ResetFilters();
                        selector.NeedUpdateIcons();
                        Echo("FILTERS RESET");
                        RequestRefresh();
                    }, AvButtonStyle.Default);
                clearTargets = PanelButton(page,
                    new Rect(AvTokens.Space3 + (chipWidth + gap), y, chipWidth, AvTokens.RowHeight),
                    "CLEAR", "btn", () =>
                    {
                        if (!Ready) return;
                        selector.DeselectAll();
                        RequestRefresh();
                    }, AvButtonStyle.Default);
                followHud = PanelButton(page,
                    new Rect(AvTokens.Space3 + 2f * (chipWidth + gap), y, chipWidth, AvTokens.RowHeight),
                    "HUD LINK", "toggle", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.toggleFollowHUD.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    },
                    AvButtonStyle.Toggle);
                laser = PanelButton(page,
                    new Rect(AvTokens.Space3 + 3f * (chipWidth + gap), y, chipWidth, AvTokens.RowHeight),
                    "LASER", "toggle", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.toggleLaser.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                resetFilters.WithTooltip("Restore the ALL profile: every faction and unit class, laser off.");
                clearTargets.WithTooltip("Drop every tracked contact from the target list.");
                followHud.WithTooltip("Follow the HUD: the target list tracks whatever the HUD is following.");
                laser.WithTooltip("Laser only: the target list keeps lased targets.");
                y -= AvTokens.RowHeight + AvTokens.Space2;

                y = Heading(page, y, width, "FACTION", "FRIEND / HOSTILE");
                factionGrid = new MfdPagingGrid(page, y, width, 2, 1, pager: false, rowHeight: cell);
                AddRightClickActions(factionGrid, OnlyFaction);
                y -= cell + AvTokens.Space2;

                y = Heading(page, y, width, "UNIT CLASS", "AIR / GROUND / SEA");
                unitGrid = new MfdPagingGrid(page, y, width, 2, 3, pager: false, rowHeight: cell);
                AddRightClickActions(unitGrid, OnlyUnitType);
                y -= cell * 3f + AvTokens.Space2;

                y = Heading(page, y, width, "PLATFORM TYPE", "DETAILED FILTER");
                vehicleGrid = new MfdPagingGrid(page, y, width, 2, 3, rowHeight: cell);
                AddRightClickActions(vehicleGrid, OnlyVehicleType);
            }

            // ------------------------------------------------------------- presets

            private void BuildPresetsPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                string[] keys =
                {
                    KeyLabel(TargetPresetRuntime.Key(0)),
                    KeyLabel(TargetPresetRuntime.Key(1)),
                    KeyLabel(TargetPresetRuntime.Key(2)),
                };
                float y = Heading(page, -AvTokens.Space1, width, "QUICK SWITCH",
                                  "RADIAL · " + string.Join(" ", keys));

                // Two columns: a slot shows its whole preset name on one line, with the
                // slot number and key on the detail line instead of crowding the name.
                quickGrid = new MfdPagingGrid(page, y, width, 2, 2, pager: false, rowHeight: 46f, exclusive: true);
                AddSlotActions();
                y -= 2f * 46f + AvTokens.Space3;

                float libraryY = y;
                y = Heading(page, y, width, "PRESET LIBRARY", null);
                presetNote = AvStyled.Label(page,
                    new Rect(width * 0.52f, libraryY, width * 0.48f, 14f), SavedNote(),
                    "section-title-note", align: TMPro.TextAlignmentOptions.MidlineRight);
                // Everything under the library grid is fixed (its pager, the status and
                // summary lines, the four actions and the editor's 52px), and so is the
                // block above it; the library rows then take what is left. That is what
                // keeps the editor's bottom inside Shell.Body.bottom at the 596 floor
                // instead of drawing the name field over the status strip.
                const float libraryChrome = 344f;
                float librarySpace = Mathf.Max(68f, PageHeight - libraryChrome);
                int libraryRows = Mathf.Clamp(Mathf.FloorToInt(librarySpace / 34f), 2, 6);
                float libraryCell = Mathf.Clamp(librarySpace / libraryRows, 28f, 64f);
                presetVisible = 2 * libraryRows;
                presetGrid = new MfdPagingGrid(page, y, width, 2, libraryRows, rowHeight: libraryCell, exclusive: true);
                AddPresetActions();
                y -= libraryRows * libraryCell + AvTokens.Space1 + AvTokens.RowHeight;

                presetStatus = AvStyled.Label(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3, 16f),
                    "ACTIVE PROFILE", "row-main");
                y -= 20f;

                presetSummary = AvStyled.Label(page,
                    new Rect(AvTokens.Space3, y, width - AvTokens.Space3 * 2f, 30f),
                    "", "row-sub", state: null);
                y -= 36f;

                float gap = AvTokens.Gap;
                float buttonWidth = (width - AvTokens.Space3 - gap * 3f) / 4f;
                saveAs = PanelButton(page, new Rect(AvTokens.Space3, y, buttonWidth, AvTokens.RowHeight),
                    "SAVE AS", "toggle", BeginSaveAs, AvButtonStyle.Primary);
                updatePreset = PanelButton(page,
                    new Rect(AvTokens.Space3 + (buttonWidth + gap), y, buttonWidth, AvTokens.RowHeight),
                    "UPDATE", "toggle", UpdateSelected, AvButtonStyle.Toggle);
                renamePreset = PanelButton(page,
                    new Rect(AvTokens.Space3 + 2f * (buttonWidth + gap), y, buttonWidth, AvTokens.RowHeight),
                    "RENAME", "toggle", BeginRename, AvButtonStyle.Toggle);
                deletePreset = PanelButton(page,
                    new Rect(AvTokens.Space3 + 3f * (buttonWidth + gap), y, buttonWidth, AvTokens.RowHeight),
                    "DELETE", "toggle", PressDelete, AvButtonStyle.Danger);
                y -= AvTokens.RowHeight + AvTokens.Space2;

                // Belt and braces: the measured layout already lands here; the clamp keeps
                // the editor's top from ever crossing the body bottom if a token changes.
                BuildNameEditor(page, Mathf.Max(y, -PageHeight + AvTokens.Space2 + 52f), width);
            }

            private string SavedNote() =>
                TargetPresetRuntime.Library.Count + " / " + TargetPresetLibrary.MaxCustomPresets + " SAVED";

            private static string KeyLabel(KeyCode key) => key == KeyCode.None ? "NONE" : key.ToString();

            private void BuildNameEditor(RectTransform page, float y, float width)
            {
                var go = new GameObject("NameEditor", typeof(RectTransform));
                editor = go.GetComponent<RectTransform>();
                editor.SetParent(page, worldPositionStays: false);
                float editorWidth = width - AvTokens.Space3 * 2f;
                AvKit.Place(editor, new Rect(AvTokens.Space3, y, editorWidth, 52f));

                editorLabel = AvStyled.Label(editor, new Rect(0f, 0f, editorWidth, 14f),
                    "PRESET NAME", "section-title-note");
                float gap = AvTokens.Space2;
                float okWidth = 96f;
                float cancelWidth = 96f;
                float fieldWidth = Mathf.Max(80f, editorWidth - (okWidth + cancelWidth + gap * 2f));
                nameField = AvKit.InputField(editor, new Rect(0f, -18f, fieldWidth, 26f),
                    TargetPresetLibrary.MaxNameLength, null, null, null,
                    "A-Z 0-9, 14 characters", "PRESET NAME");
                PanelButton(editor, new Rect(fieldWidth + gap, -18f, okWidth, 26f),
                    "CONFIRM", "toggle", CommitEdit, AvButtonStyle.Primary);
                PanelButton(editor,
                    new Rect(fieldWidth + gap * 2f + okWidth, -18f, cancelWidth, 26f),
                    "CANCEL", "toggle", CancelEdit, AvButtonStyle.Toggle);
                editor.gameObject.SetActive(false);
            }

            private void AddSlotActions()
            {
                for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                {
                    int index = slot;
                    AvButton button = quickGrid.ButtonAt(slot);
                    if (button == null) continue;
                    button.SetAction(() => ApplyQuickSlot(index));
                    MfdRightClickAction right = button.gameObject.AddComponent<MfdRightClickAction>();
                    right.Configure(() => ClearQuickSlot(index));
                }
            }

            private void AddPresetActions()
            {
                for (int slot = 0; slot < presetVisible; slot++)
                {
                    int index = slot;
                    AvButton button = presetGrid.ButtonAt(slot);
                    if (button == null) continue;
                    MfdRightClickAction right = button.gameObject.AddComponent<MfdRightClickAction>();
                    right.Configure(() => AssignQuickSlot(presetGrid.CurrentIndex(index)));
                }
            }

            private void AssignQuickSlot(int catalogIndex)
            {
                if (!Ready || catalogIndex < 0 || catalogIndex >= catalog.Count) return;
                CancelDeleteConfirm();
                string name = catalog[catalogIndex].Name;
                if (TargetPresetRuntime.AssignToFirstFreeSlot(name))
                {
                    for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                        if (TargetPresetRuntime.QuickSlotName(slot) == name)
                        {
                            Echo(name + " → QUICK SLOT " + (slot + 1));
                            break;
                        }
                }
                else
                {
                    Echo("QUICK SLOTS FULL — RIGHT-CLICK A SLOT TO CLEAR");
                }
                RequestRefresh();
            }

            private void ApplyQuickSlot(int slot)
            {
                if (!Ready) return;
                CancelDeleteConfirm();
                string name = TargetPresetRuntime.QuickSlotName(slot);
                if (name.Length == 0)
                {
                    Echo("SLOT " + (slot + 1) + " EMPTY — RIGHT-CLICK A PRESET TO ASSIGN");
                    RequestRefresh();
                    return;
                }
                TargetPresetRuntime.TryApplyByName(selector, name);
                selectedPreset = name;
                Echo("APPLIED " + name);
                RequestRefresh();
            }

            private void ClearQuickSlot(int slot)
            {
                if (!Ready) return;
                CancelDeleteConfirm();
                Echo(TargetPresetRuntime.ClearQuickSlot(slot)
                    ? "QUICK SLOT " + (slot + 1) + " CLEARED"
                    : "QUICK SLOT " + (slot + 1) + " ALREADY EMPTY");
                RequestRefresh();
            }

            private void RefreshQuickSlots()
            {
                quickGrid.SetData(TargetPresetLibrary.SlotCount,
                    slot => SlotLabel(slot),
                    slot => TargetPresetRuntime.QuickSlotName(slot).Length > 0 &&
                            TargetPresetRuntime.QuickSlotName(slot) == activePreset,
                    null,
                    subs: slot => "SLOT " + (slot + 1) + "  ·  KEY " + KeyLabel(TargetPresetRuntime.Key(slot)));
                for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                {
                    AvButton button = quickGrid.ButtonAt(slot);
                    if (button == null) continue;
                    string name = TargetPresetRuntime.QuickSlotName(slot);
                    button.SetEnabled(true);
                    button.WithTooltip(name.Length == 0
                        ? "Quick slot " + (slot + 1) + " is empty. Right-click a preset below to assign it."
                        : "Apply " + name + " (quick slot " + (slot + 1) + ", key " +
                          KeyLabel(TargetPresetRuntime.Key(slot)) + "). Right-click to clear.");
                }
            }

            private static string SlotLabel(int slot)
            {
                string name = TargetPresetRuntime.QuickSlotName(slot);
                return name.Length == 0 ? "EMPTY" : name;
            }

            private void RefreshLibrary()
            {
                EnsureCatalog();
                if (presetNote != null) presetNote.text = SavedNote();
                presetGrid.SetData(catalog.Count,
                    LibraryLabel,
                    index => catalog[index] != null && catalog[index].Name == activePreset,
                    ApplyCatalog,
                    icons: index => null);
                for (int slot = 0; slot < presetVisible; slot++)
                {
                    AvButton button = presetGrid.ButtonAt(slot);
                    if (button == null) continue;
                    int index = presetGrid.CurrentIndex(slot);
                    if (index < 0 || index >= catalog.Count)
                    {
                        button.WithTooltip(null);
                        continue;
                    }
                    TargetPresetSnapshot preset = catalog[index];
                    string badge = PresetSlotBadge(preset.Name);
                    button.WithTooltip(preset.Name + badge + " — " +
                        (TargetPresetRuntime.IsBuiltIn(index)
                            ? MfdTargetPresets.Descriptions[index]
                            : TargetPresetRules.Summary(preset)) +
                        "  ·  Left click applies, right click assigns a quick slot.");
                }
            }

            private string LibraryLabel(int index)
            {
                TargetPresetSnapshot preset = catalog[index];
                if (preset == null) return "";
                string label = preset.Name;
                if (selectedPreset == preset.Name) label = "> " + label;
                string badge = PresetSlotBadge(preset.Name);
                return badge.Length == 0 ? label : label + "  " + badge;
            }

            private static string PresetSlotBadge(string name)
            {
                for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                    if (TargetPresetRuntime.QuickSlotName(slot) == name) return "[" + (slot + 1) + "]";
                return "";
            }

            private void ApplyCatalog(int index)
            {
                if (!Ready || index < 0 || index >= catalog.Count) return;
                CancelDeleteConfirm();
                TargetPresetSnapshot preset = catalog[index];
                if (preset == null) return;
                TargetPresetRuntime.Apply(selector, preset);
                selectedPreset = preset.Name;
                Echo("APPLIED " + preset.Name);
                RequestRefresh();
            }

            private void EnsureCatalog()
            {
                int stamp = selector.toggleFactionItems.Count * 31 +
                            selector.toggleUnitTypesItems.Count * 7 +
                            selector.toggleVehicleTypesItems.Count;
                if (catalogVersion == TargetPresetRuntime.Version && catalogStamp == stamp) return;

                catalog.Clear();
                int count = TargetPresetRuntime.CatalogCount;
                for (int i = 0; i < count; i++) catalog.Add(TargetPresetRuntime.CatalogAt(selector, i));
                catalogVersion = TargetPresetRuntime.Version;
                catalogStamp = stamp;
            }

            /// <summary>Profile matching walks every preset; only redo it when a toggle moved.</summary>
            private string CachedActive()
            {
                int stamp = TargetPresetRuntime.Version;
                stamp = stamp * 31 + (selector.toggleFollowHUD.status ? 1 : 0);
                stamp = stamp * 31 + (selector.toggleLaser.status ? 1 : 0);
                stamp = stamp * 31 + selector.toggleFactionItems.Count;
                for (int i = 0; i < selector.toggleFactionItems.Count; i++)
                    stamp = stamp * 31 + EntryHash(selector.toggleFactionItems[i]);
                stamp = stamp * 31 + selector.toggleUnitTypesItems.Count;
                for (int i = 0; i < selector.toggleUnitTypesItems.Count; i++)
                    stamp = stamp * 31 + EntryHash(selector.toggleUnitTypesItems[i]);
                stamp = stamp * 31 + selector.toggleVehicleTypesItems.Count;
                for (int i = 0; i < selector.toggleVehicleTypesItems.Count; i++)
                    stamp = stamp * 31 + EntryHash(selector.toggleVehicleTypesItems[i]);

                if (stamp == activeStamp) return activeCached;
                activeStamp = stamp;
                activeCached = TargetPresetRuntime.ActiveName(selector);
                return activeCached;
            }

            private static int EntryHash(TargetListSelector_ToggleButton entry) =>
                entry == null ? 0 : (entry.status ? 1 : 2);

            private void TrackSelectedPreset()
            {
                if (string.IsNullOrEmpty(selectedPreset) ||
                    TargetPresetRuntime.IndexOfCatalogName(selectedPreset) < 0)
                    selectedPreset = activePreset;
            }

            private TargetPresetSnapshot SelectedCustom()
            {
                int index = TargetPresetRuntime.IndexOfCatalogName(selectedPreset);
                return index >= TargetPresetRuntime.BuiltInCount ? TargetPresetRuntime.Library.At(
                    index - TargetPresetRuntime.BuiltInCount) : null;
            }

            private void BeginSaveAs()
            {
                if (!Ready) return;
                CancelDeleteConfirm();
                nameField.text = TargetPresetRuntime.SuggestName();
                OpenEditor(EditMode.SaveAs, "SAVE CURRENT FILTERS AS");
            }

            private void BeginRename()
            {
                if (!Ready) return;
                TargetPresetSnapshot preset = SelectedCustom();
                if (preset == null)
                {
                    Echo("RENAME WORKS ON SAVED PRESETS — SELECT ONE FIRST");
                    RequestRefresh();
                    return;
                }
                CancelDeleteConfirm();
                nameField.text = preset.Name;
                OpenEditor(EditMode.Rename, "RENAME " + preset.Name);
            }

            private void OpenEditor(EditMode mode, string label)
            {
                editorMode = mode;
                editorLabel.text = label;
                editor.gameObject.SetActive(true);
                nameField.ActivateInputField();
                nameField.Select();
                RequestRefresh();
            }

            private void CloseEditor()
            {
                editorMode = EditMode.None;
                if (editor != null) editor.gameObject.SetActive(false);
                if (nameField != null) nameField.DeactivateInputField();
            }

            private void CancelEdit()
            {
                CloseEditor();
                Echo("EDIT CANCELLED");
                RequestRefresh();
            }

            private void CommitEdit()
            {
                if (editorMode == EditMode.None) return;
                string name = TargetPresetRules.SanitiseName(nameField.text);
                if (name.Length == 0)
                {
                    Echo("NAME REQUIRED — A-Z, 0-9, 14 CHARACTERS");
                    RequestRefresh();
                    return;
                }

                if (editorMode == EditMode.SaveAs)
                {
                    TargetPresetSaveResult result = TargetPresetRuntime.SaveCurrent(selector, name, false);
                    if (result == TargetPresetSaveResult.Ok)
                    {
                        selectedPreset = name;
                        Echo("SAVED " + name);
                    }
                    else
                    {
                        Echo(SaveMessage(result, name));
                        RequestRefresh();
                        return;
                    }
                }
                else
                {
                    if (name == selectedPreset)
                    {
                        CloseEditor();
                        Echo("NO CHANGE");
                        RequestRefresh();
                        return;
                    }
                    if (!TargetPresetRuntime.Rename(selectedPreset, name))
                    {
                        Echo("RENAME REJECTED — NAME IN USE OR RESERVED");
                        RequestRefresh();
                        return;
                    }
                    Echo("RENAMED TO " + name);
                    selectedPreset = name;
                }

                CloseEditor();
                RequestRefresh();
            }

            private static string SaveMessage(TargetPresetSaveResult result, string name)
            {
                switch (result)
                {
                    case TargetPresetSaveResult.NameInUse: return name + " EXISTS — USE UPDATE TO REPLACE IT";
                    case TargetPresetSaveResult.LibraryFull:
                        return "LIBRARY FULL (" + TargetPresetLibrary.MaxCustomPresets + " MAX) — DELETE ONE FIRST";
                    case TargetPresetSaveResult.ReservedName: return name + " IS A BUILT-IN PROFILE NAME";
                    case TargetPresetSaveResult.NotReady: return "WAITING FOR TARGET FILTERS";
                    default: return "NAME REQUIRED — A-Z, 0-9, 14 CHARACTERS";
                }
            }

            private void UpdateSelected()
            {
                if (!Ready) return;
                CancelDeleteConfirm();
                TargetPresetSnapshot preset = SelectedCustom();
                if (preset == null)
                {
                    Echo("UPDATE WORKS ON SAVED PRESETS — SELECT ONE FIRST");
                    RequestRefresh();
                    return;
                }
                TargetPresetSaveResult result = TargetPresetRuntime.SaveCurrent(selector, preset.Name, true);
                Echo(result == TargetPresetSaveResult.Ok ? "UPDATED " + preset.Name : SaveMessage(result, preset.Name));
                RequestRefresh();
            }

            private void PressDelete()
            {
                if (!Ready) return;
                TargetPresetSnapshot preset = SelectedCustom();
                if (preset == null)
                {
                    Echo("DELETE WORKS ON SAVED PRESETS — SELECT ONE FIRST");
                    RequestRefresh();
                    return;
                }

                if (confirmDelete != preset.Name || Time.unscaledTime > confirmDeleteUntil)
                {
                    confirmDelete = preset.Name;
                    confirmDeleteUntil = Time.unscaledTime + 4f;
                    Echo("DELETE " + preset.Name + "? PRESS DELETE AGAIN");
                    RequestRefresh();
                    return;
                }

                TargetPresetRuntime.Delete(preset.Name);
                confirmDelete = null;
                selectedPreset = "";
                Echo("DELETED " + preset.Name);
                RequestRefresh();
            }

            private void CancelDeleteConfirm()
            {
                if (confirmDelete == null) return;
                confirmDelete = null;
                Echo("DELETE CANCELLED");
            }

            private string DescribeSelected()
            {
                int index = TargetPresetRuntime.IndexOfCatalogName(selectedPreset);
                if (index < 0) return "Select a preset to apply. SAVE AS stores the current filters.";
                if (index < TargetPresetRuntime.BuiltInCount)
                    return "Built-in. " + MfdTargetPresets.Descriptions[index];
                TargetPresetSnapshot preset = TargetPresetRuntime.Library.At(
                    index - TargetPresetRuntime.BuiltInCount);
                return preset == null ? "" : TargetPresetRules.Summary(preset);
            }

            // ------------------------------------------------------------ selected

            private void BuildSelectedPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float y = Heading(page, -AvTokens.Space1, width, "SELECTED TARGETS", null);
                selectedNote = AvStyled.Label(page,
                    new Rect(width * 0.52f, -AvTokens.Space1, width * 0.48f, 14f),
                    "0 TRACKED", "section-title-note", align: TMPro.TextAlignmentOptions.MidlineRight);
                // The same 70px chrome as the HUD list pages: heading, pager and the two
                // small gaps outside them. The pitch takes the body's slack so the pager
                // sits on the footer instead of a dead band.
                const float chrome = 70f;
                selectedVisible = Mathf.Clamp(
                    Mathf.FloorToInt((PageHeight - chrome) / AvTokens.RowHeight), 3, 9);
                float cell = Mathf.Clamp((PageHeight - chrome) / selectedVisible,
                                         AvTokens.RowHeight, 72f);
                selectedGrid = new MfdPagingGrid(page, y, width, 1, selectedVisible,
                                                 readOnly: true, rowHeight: cell);
                for (int slot = 0; slot < selectedVisible; slot++)
                {
                    int index = slot;
                    AvButton button = selectedGrid.ButtonAt(slot);
                    if (button == null) continue;
                    button.SetEnabled(true);
                    MfdRightClickAction right = button.gameObject.AddComponent<MfdRightClickAction>();
                    right.Configure(() => DeselectSelected(selectedGrid.CurrentIndex(index)));
                }
            }

            private void DeselectSelected(int index)
            {
                if (index < 0 || index >= selectedUnits.Count) return;
                Unit unit = selectedUnits[index];
                if (unit == null) return;
                selector.ForceDeselect(unit);
                Echo("DROPPED " + TargetUnitLabel(unit));
                RequestRefresh();
            }

            // ------------------------------------------------------------ camera

            private void BuildCameraPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float y = Heading(page, -AvTokens.Space1, width, "CAMERA MARK", "SURFACE SENSOR TARGET");

                // Keep the mark, actions and readout together at every bezel height.
                const float plate = 112f;
                const float keyPitch = 28f;
                const float noteHeight = 42f;

                AvStyled.Box(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3 * 2f, plate),
                    "section band");
                cameraRail = AvStyled.Rail(page,
                    new Rect(AvTokens.Space3 + 6f, y - 6f, 3f, Mathf.Max(10f, plate - 12f)), "locked");
                float groupTop = y - Mathf.Max(6f, (plate - 54f) * 0.5f);
                cameraState = AvStyled.Label(page,
                    new Rect(AvTokens.Space3 + 16f, groupTop, width - AvTokens.Space3 * 2f - 24f, 16f),
                    "NO ACTIVE MARK", "section-title");
                cameraDetails = AvStyled.Label(page,
                    new Rect(AvTokens.Space3 + 16f, groupTop - 18f, width - AvTokens.Space3 * 2f - 24f, 36f),
                    "Aim the cockpit camera at a surface point and press MARK CAMERA.", "row-sub");
                y -= plate + AvTokens.Space2;

                float gap = AvTokens.Gap;
                float buttonWidth = (width - AvTokens.Space3 * 2f - gap * 2f) / 3f;
                cameraCapture = PanelButton(page,
                    new Rect(AvTokens.Space3, y, buttonWidth, AvTokens.RowHeight),
                    "MARK CAMERA", "btn",
                    () => { Camera?.Capture(); RequestRefresh(); }, AvButtonStyle.Default);
                cameraCall = PanelButton(page,
                    new Rect(AvTokens.Space3 + buttonWidth + gap, y, buttonWidth, AvTokens.RowHeight),
                    "CALL AT MARK", "btn",
                    () => { Camera?.CallAtMark(); RequestRefresh(); }, AvButtonStyle.Default);
                cameraClear = PanelButton(page,
                    new Rect(AvTokens.Space3 + (buttonWidth + gap) * 2f, y, buttonWidth, AvTokens.RowHeight),
                    "CLEAR MARK", "btn",
                    () => { Camera?.Clear(); RequestRefresh(); }, AvButtonStyle.Default);
                y -= AvTokens.RowHeight + AvTokens.Space2;

                y = Heading(page, y, width, "TARGET TELEMETRY", "COORDINATES & RANGE");
                cameraPos = CameraKey(page, y, width, "COORDINATES (X/Z)");
                y -= keyPitch;
                cameraRange = CameraKey(page, y, width, "SLANT RANGE");
                y -= keyPitch;
                cameraAge = CameraKey(page, y, width, "MARK AGE");
                y -= keyPitch;
                cameraArmed = CameraKey(page, y, width, "ARMED CALL-IN");

                AvStyled.Label(page,
                    new Rect(AvTokens.Space3, y - keyPitch - AvTokens.Space2,
                             width - AvTokens.Space3 * 2f, noteHeight),
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
                    cameraCapture?.WithTooltip("Camera marking is unavailable: the support module or its observation source is not installed.");
                    cameraCall?.WithTooltip("Camera marking is unavailable: the support module or its observation source is not installed.");
                    cameraClear?.WithTooltip("Camera marking is unavailable: the support module or its observation source is not installed.");
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

            // ------------------------------------------------------------ plumbing

            private void SelectPage(int selected)
            {
                CancelDeleteConfirm();
                CloseEditor();
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
                CancelDeleteConfirm();
                entries[index].Toggle();
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private void SetOnly(List<TargetListSelector_ToggleButton> entries, int index)
            {
                if (!Ready) return;
                if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return;
                CancelDeleteConfirm();
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
                if (selectedNote != null) selectedNote.text = selectedUnits.Count + " TRACKED";
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
                quickGrid?.SetInteractable(enabled);
                presetGrid?.SetInteractable(enabled);
                saveAs?.SetEnabled(enabled);
                updatePreset?.SetEnabled(enabled);
                renamePreset?.SetEnabled(enabled);
                deletePreset?.SetEnabled(enabled);
                if (!enabled)
                {
                    editorMode = EditMode.None;
                    if (editor != null) editor.gameObject.SetActive(false);
                    confirmDelete = null;
                    clearTargets?.WithTooltip("Waiting for target filters.");
                    updatePreset?.WithTooltip("Waiting for target filters.");
                    renamePreset?.WithTooltip("Waiting for target filters.");
                    deletePreset?.WithTooltip("Waiting for target filters.");
                }
                else
                {
                    // A disabled preset action states the one thing it is waiting for,
                    // the way the camera and map preset buttons do.
                    bool custom = SelectedCustom() != null;
                    updatePreset?.SetEnabled(custom);
                    updatePreset?.WithTooltip(custom
                        ? "Overwrite the selected saved preset with the current filters."
                        : "Select a saved preset first.");
                    renamePreset?.SetEnabled(custom);
                    renamePreset?.WithTooltip(custom
                        ? "Rename the selected saved preset."
                        : "Select a saved preset first.");
                    deletePreset?.SetEnabled(custom);
                    deletePreset?.WithTooltip(custom
                        ? "Delete the selected saved preset. Deletion asks for a second press."
                        : "Select a saved preset first.");
                }
            }

            private static int CountEnabled(List<TargetListSelector_ToggleButton> entries)
            {
                if (entries == null) return 0;
                int count = 0;
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i] != null && entries[i].status) count++;
                return count;
            }

            /// <summary>
            /// The native filter's full name. It is not cut here: the cell wraps it over
            /// two lines at the micro floor, so "GROUND VEHICLES" never prints as "GND".
            /// </summary>
            private static string NativeTargetLabel(TargetListSelector_ToggleButton entry)
            {
                if (entry != null && entry.label != null && !string.IsNullOrEmpty(entry.label.text))
                {
                    string label = entry.label.text.Replace("\n", " ").ToUpperInvariant();
                    switch (label)
                    {
                        case "AIR": return "AIRCRAFT";
                        case "MSL": return "MISSILES";
                        case "GND": return "GROUND";
                        case "BLD": return "BUILDINGS";
                        case "SHP": return "SHIPS";
                        default: return label;
                    }
                }
                return NativeLabel(entry, "FILTER");
            }

            private static string TargetUnitLabel(Unit unit)
            {
                // The selected grid wraps or shrinks the whole call sign; a unit's own
                // name is never cut to a fixed column.
                if (unit == null) return "UNKNOWN TARGET";
                string code = unit.definition == null ? "UNIT" : unit.definition.code;
                string name = string.IsNullOrEmpty(unit.unitName) ? code : unit.unitName;
                return (string.IsNullOrEmpty(name) ? "UNIT" : name).ToUpperInvariant();
            }
        }
    }
}
