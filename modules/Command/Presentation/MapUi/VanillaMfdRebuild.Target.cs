using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

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

            private AvChip[] chips;
            private MfdPagingGrid factionGrid;
            private MfdPagingGrid unitGrid;
            private MfdPagingGrid vehicleGrid;
            private AvGauge filterGauge;
            private MfdPagingGrid selectedGrid;
            private MfdPagingGrid candidateGrid;
            private readonly List<TargetCandidate> candidates = new List<TargetCandidate>(128);
            private Unit candidateFocus;
            private float nextCandidateScan;
            private AvControl missilePreference;
            private AvControl weaponPreference;
            private AvControl airPreference;
            private AvControl rangePreference;
            private AvControl designateCandidate;
            private AvControl nextCandidate;
            private AvControl incomingCandidate;
            private AvSection contactsSection;
            private readonly List<Unit>[] targetGroups =
                { new List<Unit>(16), new List<Unit>(16), new List<Unit>(16) };
            private MfdPagingGrid groupGrid;
            private MfdPagingGrid quickGrid;
            private MfdPagingGrid presetGrid;
            private AvControl resetFilters;
            private AvControl clearTargets;
            private AvControl followHud;
            private AvControl laser;
            private AvControl saveAs;
            private AvControl updatePreset;
            private AvControl renamePreset;
            private AvControl deletePreset;
            private AvReadout presetReadout;
            private AvSection presetSection;
            private AvSection selectedSection;
            private PresetEditor editorPart;

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

            private const int PresetVisible = 8;
            private const int SelectedVisible = 6;

            // Camera surface mark: state lives in Support through a narrow contract.
            private ICameraTargetService cameraService;
            private AvRow cameraStatusRow;
            private readonly AvRow[] cameraRows = new AvRow[5];
            private AvRow cameraReticleRow;
            private AvControl cameraCapture;
            private AvControl cameraCall;
            private AvControl cameraClear;

            private static readonly (AvIcon Icon, string Label)[] Pages =
            {
                (AvIcon.Filter, "FILTERS"), (AvIcon.Radar2, "ACQUIRE"), (AvIcon.Star, "PRESETS"),
                (AvIcon.Target, "TARGETS"), (AvIcon.Camera, "CAMERA"),
            };

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

            protected override string Title => "TARGETING";
            protected override (AvIcon Icon, string Label)[] TabItems => Pages;

            protected override void BuildContent()
            {
                chips = Console.Chips(3);

                BuildFiltersPage(CreatePage());
                BuildAcquirePage(CreatePage());
                BuildPresetsPage(CreatePage());
                BuildSelectedPage(CreatePage());
                BuildCameraPage(CreatePage());
            }

            protected override void OnPageChanged(int index)
            {
                CancelDeleteConfirm();
                CloseEditor();
                RequestRefresh();
            }

            protected override void RefreshContent()
            {
                RefreshCamera();

                if (!Ready)
                {
                    SetFilterInput(false);
                    filterGauge.Set(0f, "—", AvState.Inert);
                    chips[0].Set("LINK", AvState.Inert);
                    chips[1].Set("DATA", AvState.Inert);
                    chips[2].Set("—", AvState.Inert);
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
                int total = selector.toggleFactionItems.Count + selector.toggleUnitTypesItems.Count +
                            selector.toggleVehicleTypesItems.Count;
                filterGauge.Set(total > 0 ? filters / (float)total : 0f,
                    AvNum.Fixed(filters, 0) + " / " + AvNum.Fixed(total, 0),
                    filters > 0 ? AvState.Ready : AvState.Inert);

                chips[0].Set(filters + " FILTERS", filters > 0 ? AvState.Ready : AvState.Inert);
                chips[1].Set(activePreset, activePreset != TargetPresetLibrary.CustomProfile ? AvState.Ready : AvState.Inert);
                chips[2].Set(selector.toggleFollowHUD.status ? "HUD LINK" :
                             selector.toggleLaser.status ? "LASER" : "MANUAL",
                             selector.toggleFollowHUD.status || selector.toggleLaser.status ? AvState.Ready : AvState.Inert);

                int tracked = SelectedCount();
                clearTargets.Interactable = tracked > 0;

                followHud.Label = selector.toggleFollowHUD.status ? "HUD ON" : "HUD OFF";
                followHud.Latched = selector.toggleFollowHUD.status;
                laser.Label = selector.toggleLaser.status ? "LASER ON" : "LASER OFF";
                laser.Latched = selector.toggleLaser.status;

                SetGrid(factionGrid, selector.toggleFactionItems, ToggleFaction);
                SetGrid(unitGrid, selector.toggleUnitTypesItems, ToggleUnitType);
                SetGrid(vehicleGrid, selector.toggleVehicleTypesItems, ToggleVehicleType);
                RefreshSelectedGrid();
                RefreshAcquire();
                RefreshQuickSlots();
                RefreshLibrary();

                presetReadout.Set(selector.toggleFollowHUD.status ? "HUD LINK" : activePreset,
                    "ACTIVE PROFILE", DescribeSelected());
            }

            protected override string AmbientStatus()
            {
                if (!string.IsNullOrEmpty(echo) && Time.unscaledTime < echoUntil) return echo;
                int page = Console.CurrentPage;
                if (page == 1) return "CHOOSE A CONTACT, THEN DESIGNATE; PREFS AFFECT THIS BROWSER";
                if (page == 2) return "LEFT CLICK APPLIES — RIGHT CLICK ASSIGNS A QUICK SLOT";
                if (page == 3) return "RIGHT-CLICK GROUP TO SAVE · LEFT-CLICK TO RECALL · RIGHT-CLICK CONTACT TO DROP";
                return "LEFT CLICK TO TOGGLE — RIGHT CLICK TO SHOW ONLY ONE FILTER";
            }

            private void Echo(string text)
            {
                echo = text;
                echoUntil = Time.unscaledTime + 2.4f;
            }

            // ------------------------------------------------------------- filters

            private void BuildFiltersPage(AvFlow page)
            {
                page.Section(AvIcon.Filter, "ACQUISITION GATE", "SENSOR LOGIC");
                filterGauge = page.Add(new AvGauge(page.Content, "ENABLED FILTERS", AvGaugeShape.Bar));

                page.Section(AvIcon.Filter, "FILTER ACTIONS", "L TOGGLE / R SOLO");
                AvButtons actionRow = page.Buttons(
                    new AvControl.Spec("RESET", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.ResetFilters();
                        selector.NeedUpdateIcons();
                        Echo("FILTERS RESET");
                        RequestRefresh();
                    }),
                    new AvControl.Spec("CLEAR", () =>
                    {
                        if (!Ready) return;
                        selector.DeselectAll();
                        RequestRefresh();
                    }),
                    new AvControl.Spec("HUD LINK", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.toggleFollowHUD.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle),
                    new AvControl.Spec("LASER", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.toggleLaser.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle));
                resetFilters = actionRow.Controls[0];
                clearTargets = actionRow.Controls[1];
                followHud = actionRow.Controls[2];
                laser = actionRow.Controls[3];

                page.Section(AvIcon.Shield, "FACTION", "FRIEND / FOE");
                factionGrid = page.Add(new MfdPagingGrid(page.Content, 2, 1, pager: false, rowHeight: 44f));
                AddRightClickActions(factionGrid, 2, OnlyFaction);

                page.Section(AvIcon.LayersSubtract, "UNIT CLASS", "AIR / LAND / SEA");
                unitGrid = page.Add(new MfdPagingGrid(page.Content, 2, 3, pager: false, rowHeight: 44f));
                AddRightClickActions(unitGrid, 6, OnlyUnitType);

                page.Section(AvIcon.Stack2, "PLATFORM TYPE", "TYPE MASK");
                vehicleGrid = page.Add(new MfdPagingGrid(page.Content, 2, 5, pager: false, rowHeight: 44f));
                AddRightClickActions(vehicleGrid, 10, OnlyVehicleType);
            }

            // ------------------------------------------------------------- acquire

            private struct TargetCandidate
            {
                public Unit Unit;
                public float DistanceKm;
            }

            private void BuildAcquirePage(AvFlow page)
            {
                page.Section(AvIcon.Radar2, "CONTACT BROWSER", "NEAREST KNOWN FIRST");
                AvButtons prefRow1 = page.Buttons(
                    new AvControl.Spec("MISSILES", () => ToggleAcquirePreference(0), AvButtonStyle.Toggle),
                    new AvControl.Spec("WEAPON FIT", () => ToggleAcquirePreference(1), AvButtonStyle.Toggle));
                missilePreference = prefRow1.Controls[0];
                weaponPreference = prefRow1.Controls[1];
                AvButtons prefRow2 = page.Buttons(
                    new AvControl.Spec("AIR ONLY", () => ToggleAcquirePreference(2), AvButtonStyle.Toggle),
                    new AvControl.Spec("RANGE ALL", () => ToggleAcquirePreference(3), AvButtonStyle.Toggle));
                airPreference = prefRow2.Controls[0];
                rangePreference = prefRow2.Controls[1];

                contactsSection = page.Section(AvIcon.Eye, "CONTACTS", "0 KNOWN");
                candidateGrid = page.Add(new MfdPagingGrid(page.Content, 1, 5, rowHeight: 42f));
                candidateGrid.SetEmptyMessage("NO CONTACTS MATCH THESE PREFERENCES");
                AvButtons candidateRow = page.Buttons(
                    new AvControl.Spec("NEXT", NextCandidate),
                    new AvControl.Spec("INCOMING", PreviewIncoming),
                    new AvControl.Spec("DESIGNATE", DesignateCandidate, AvButtonStyle.Primary));
                nextCandidate = candidateRow.Controls[0];
                incomingCandidate = candidateRow.Controls[1];
                designateCandidate = candidateRow.Controls[2];
            }

            private void ToggleAcquirePreference(int which)
            {
                var settings = TargetPresetRuntime.Settings;
                if (settings == null) return;
                switch (which)
                {
                    case 0: settings.TargetShowMissiles.Value = !settings.TargetShowMissiles.Value; break;
                    case 1: settings.TargetWeaponOnly.Value = !settings.TargetWeaponOnly.Value; break;
                    case 2: settings.TargetAirOnly.Value = !settings.TargetAirOnly.Value; break;
                    case 3:
                        int[] ranges = { 0, 10, 25, 50, 100 };
                        int index = Array.IndexOf(ranges, settings.TargetRangeKm.Value);
                        settings.TargetRangeKm.Value = ranges[(index + 1) % ranges.Length];
                        break;
                }
                candidateFocus = null;
                nextCandidateScan = 0f;
                RequestRefresh();
            }

            private void RefreshAcquire()
            {
                var settings = TargetPresetRuntime.Settings;
                if (settings == null || candidateGrid == null) return;
                missilePreference.Label = settings.TargetShowMissiles.Value ? "MISSILES ON" : "MISSILES OFF";
                missilePreference.Latched = settings.TargetShowMissiles.Value;
                weaponPreference.Label = settings.TargetWeaponOnly.Value ? "WEAPON FIT ON" : "WEAPON FIT OFF";
                weaponPreference.Latched = settings.TargetWeaponOnly.Value;
                airPreference.Label = settings.TargetAirOnly.Value ? "AIR ONLY" : "ALL CLASSES";
                airPreference.Latched = settings.TargetAirOnly.Value;
                int range = settings.TargetRangeKm.Value;
                rangePreference.Label = range <= 0 ? "RANGE ALL" : "RANGE " + range + " KM";
                rangePreference.Latched = range > 0;
                if (Console.CurrentPage != 1) return;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                bool flying = hud != null && hud.aircraft != null && !hud.aircraft.disabled;
                candidateGrid.SetEmptyMessage(flying ? "NO CONTACTS MATCH THESE PREFERENCES" :
                    "ENTER AN AIRCRAFT TO BROWSE CONTACTS");
                incomingCandidate.Interactable = flying;
                if (Time.unscaledTime >= nextCandidateScan)
                {
                    ScanCandidates();
                    nextCandidateScan = Time.unscaledTime + 0.5f;
                }
                candidateGrid.SetData(candidates.Count,
                    i => TargetUnitLabel(candidates[i].Unit),
                    i => candidates[i].Unit == candidateFocus,
                    PreviewCandidate,
                    icons: i => candidates[i].Unit.definition == null ? null : candidates[i].Unit.definition.mapIcon,
                    subs: i => AvNum.Fixed(candidates[i].DistanceKm, 1) + " KM · KNOWN POSITION");
                if (contactsSection != null) contactsSection.SetCaption(flying ? candidates.Count + " MATCH" : "NO AIRCRAFT");
                nextCandidate.Interactable = candidates.Count > 0;
                designateCandidate.Interactable = candidateFocus != null && candidates.Exists(c => c.Unit == candidateFocus);
            }

            private void ScanCandidates()
            {
                candidates.Clear();
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                var settings = TargetPresetRuntime.Settings;
                if (map == null || map.HQ == null || map.mapIcons == null || hud == null ||
                    hud.aircraft == null || hud.aircraft.disabled || settings == null) return;
                WeaponStation station = hud.GetWeaponStation();
                for (int i = 0; i < map.mapIcons.Count; i++)
                {
                    UnitMapIcon icon = map.mapIcons[i] as UnitMapIcon;
                    Unit unit = icon == null ? null : icon.unit;
                    if (unit == null || unit == hud.aircraft || unit.definition == null ||
                        !icon.gameObject.activeInHierarchy || selector.CheckExclusions(unit)) continue;
                    if (!settings.TargetShowMissiles.Value && unit.definition is MissileDefinition) continue;
                    if (settings.TargetAirOnly.Value && !(unit is Aircraft)) continue;
                    GlobalPosition known;
                    if (!map.HQ.TryGetKnownPosition(unit, out known)) continue;
                    float distance = FastMath.Distance(hud.aircraft.GlobalPosition(), known) / 1000f;
                    if (float.IsNaN(distance) || float.IsInfinity(distance) ||
                        (settings.TargetRangeKm.Value > 0 && distance > settings.TargetRangeKm.Value)) continue;
                    if (settings.TargetWeaponOnly.Value &&
                        (station == null || station.CalcOpportunityThreat(unit.definition, hud.aircraft).opportunity <= 0f))
                        continue;
                    int insert = candidates.FindIndex(c => c.DistanceKm > distance);
                    if (insert < 0) insert = candidates.Count;
                    if (insert >= 128) continue;
                    candidates.Insert(insert, new TargetCandidate { Unit = unit, DistanceKm = distance });
                    if (candidates.Count > 128) candidates.RemoveAt(128);
                }
                if (candidateFocus != null && !candidates.Exists(c => c.Unit == candidateFocus)) candidateFocus = null;
            }

            private void PreviewCandidate(int index)
            {
                if (index < 0 || index >= candidates.Count) return;
                candidateFocus = candidates[index].Unit;
                SceneSingleton<DynamicMap>.i?.HighlightIcon(candidateFocus);
                Echo("PREVIEW " + TargetUnitLabel(candidateFocus) + " · PRESS DESIGNATE TO SELECT");
                RequestRefresh();
            }

            private void NextCandidate()
            {
                if (candidates.Count == 0) return;
                int index = candidates.FindIndex(c => c.Unit == candidateFocus);
                PreviewCandidate((index + 1) % candidates.Count);
            }

            private void PreviewIncoming()
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (map == null || map.HQ == null || map.mapIcons == null || hud == null || hud.aircraft == null) return;
                Missile nearest = null;
                float best = float.MaxValue;
                for (int i = 0; i < map.mapIcons.Count; i++)
                {
                    UnitMapIcon icon = map.mapIcons[i] as UnitMapIcon;
                    Missile missile = icon == null ? null : icon.unit as Missile;
                    if (missile == null || missile.targetID != hud.aircraft.persistentID) continue;
                    GlobalPosition known;
                    if (!map.HQ.TryGetKnownPosition(missile, out known)) continue;
                    float distance = FastMath.Distance(hud.aircraft.GlobalPosition(), known);
                    if (distance >= best) continue;
                    best = distance;
                    nearest = missile;
                }
                if (nearest == null) { Echo("NO TRACKED INCOMING MISSILE"); return; }
                map.HighlightIcon(nearest);
                Echo("INCOMING MISSILE HIGHLIGHTED · NO TARGET ADDED");
                RequestRefresh();
            }

            private void DesignateCandidate()
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                GlobalPosition known;
                if (candidateFocus == null || hud == null || hud.aircraft == null || hud.aircraft.disabled ||
                    map == null || map.HQ == null || !map.HQ.TryGetKnownPosition(candidateFocus, out known) ||
                    !candidates.Exists(c => c.Unit == candidateFocus) || selector.CheckExclusions(candidateFocus)) return;
                hud.SelectUnit(candidateFocus);
                Echo(hud.GetTargetList().Contains(candidateFocus)
                    ? "DESIGNATED " + TargetUnitLabel(candidateFocus)
                    : "CONTACT NOT AVAILABLE TO HUD TARGET LIST");
                RequestRefresh();
            }

            // ------------------------------------------------------------- presets

            private void BuildPresetsPage(AvFlow page)
            {
                string[] keys =
                {
                    KeyLabel(TargetPresetRuntime.Key(0)),
                    KeyLabel(TargetPresetRuntime.Key(1)),
                    KeyLabel(TargetPresetRuntime.Key(2)),
                };
                page.Section(AvIcon.Star, "QUICK SWITCH", "RADIAL · " + string.Join(" ", keys));
                quickGrid = page.Add(new MfdPagingGrid(page.Content, 1, 3, pager: false, rowHeight: 44f));
                AddSlotActions();

                presetSection = page.Section(AvIcon.Bookmark, "PRESET LIBRARY", SavedNote());
                presetGrid = page.Add(new MfdPagingGrid(page.Content, 2, 4, rowHeight: 42f));
                AddPresetActions();

                presetReadout = page.Add(new AvReadout(page.Content));

                AvButtons presetActions = page.Buttons(
                    new AvControl.Spec("SAVE AS", BeginSaveAs, AvButtonStyle.Primary),
                    new AvControl.Spec("UPDATE", UpdateSelected, AvButtonStyle.Toggle),
                    new AvControl.Spec("RENAME", BeginRename, AvButtonStyle.Toggle),
                    new AvControl.Spec("DELETE", PressDelete, AvButtonStyle.Danger));
                saveAs = presetActions.Controls[0];
                updatePreset = presetActions.Controls[1];
                renamePreset = presetActions.Controls[2];
                deletePreset = presetActions.Controls[3];

                editorPart = page.Add(new PresetEditor(page, CommitEdit, CancelEdit));
            }

            private string SavedNote() =>
                TargetPresetRuntime.Library.Count + " / " + TargetPresetLibrary.MaxCustomPresets + " SAVED";

            private static string KeyLabel(KeyCode key) => key == KeyCode.None ? "NONE" : key.ToString();

            /// <summary>Local kit v2 part: a name field plus CONFIRM/CANCEL, hidden (zero height) until
            /// <see cref="Show"/>. AvField has no public focus/select seam (kit gap): the field no longer
            /// auto-activates the keyboard on open, the way <c>TMP_InputField.ActivateInputField</c> did.</summary>
            private sealed class PresetEditor : AvPart
            {
                private readonly AvFlow page;
                private readonly TMPro.TMP_Text label;
                private readonly AvField field;
                private readonly AvControl confirm, cancel;
                private bool visible;

                public PresetEditor(AvFlow flow, Action commit, Action cancelAction)
                {
                    page = flow;
                    Rect = AvLay.Child(flow.Content, "Editor");
                    label = AvText.Make(Rect, "Label", AvTextRole.Micro, "");
                    field = new AvField(Rect, "A-Z 0-9, 14 characters", TargetPresetLibrary.MaxNameLength, _ => commit());
                    confirm = AvControl.Make(Rect, new AvControl.Spec("CONFIRM", commit, AvButtonStyle.Primary));
                    cancel = AvControl.Make(Rect, new AvControl.Spec("CANCEL", cancelAction, AvButtonStyle.Toggle));
                    Rect.gameObject.SetActive(false);
                }

                public string Text { get => field.Text; set => field.Text = value; }

                public void Show(string labelText)
                {
                    label.text = labelText ?? "";
                    visible = true;
                    Rect.gameObject.SetActive(true);
                    page.RequestRelayout();
                }

                public void Hide()
                {
                    if (!visible) return;
                    visible = false;
                    Rect.gameObject.SetActive(false);
                    page.RequestRelayout();
                }

                public override float Measure(float width) => visible ? 18f + AvGridTokens.Row : 0f;

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    if (!visible) return;
                    AvLay.Place(label.rectTransform, 0f, 0f, s.W, 16f);
                    float okW = 90f, cancelW = 90f, gap = AvGridTokens.Gap;
                    float fieldW = Mathf.Max(80f, s.W - okW - cancelW - gap * 2f);
                    field.Place(new AvSlot(0f, 18f, fieldW, AvGridTokens.Row));
                    AvLay.Place(confirm.Rect, fieldW + gap, 18f, okW, AvGridTokens.Row);
                    AvLay.Place(cancel.Rect, fieldW + gap * 2f + okW, 18f, cancelW, AvGridTokens.Row);
                }

                public override void Restyle()
                {
                    field.Restyle();
                    confirm.Restyle();
                    cancel.Restyle();
                }
            }

            private void AddSlotActions()
            {
                for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                {
                    int index = slot;
                    MfdIconCell cell = quickGrid.CellAt(slot);
                    if (cell == null) continue;
                    cell.OnRightClick = () => ClearQuickSlot(index);
                }
            }

            private void AddPresetActions()
            {
                for (int slot = 0; slot < PresetVisible; slot++)
                {
                    int index = slot;
                    MfdIconCell cell = presetGrid.CellAt(slot);
                    if (cell == null) continue;
                    cell.OnRightClick = () => AssignQuickSlot(presetGrid.CurrentIndex(index));
                }
            }

            private void AddGroupActions()
            {
                for (int slot = 0; slot < targetGroups.Length; slot++)
                {
                    int index = slot;
                    MfdIconCell cell = groupGrid.CellAt(slot);
                    if (cell == null) continue;
                    cell.OnRightClick = () => StoreGroup(index);
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
                    ApplyQuickSlot,
                    subs: slot => "SLOT " + (slot + 1) + " · " + KeyLabel(TargetPresetRuntime.Key(slot)));
            }

            private static string SlotLabel(int slot)
            {
                string name = TargetPresetRuntime.QuickSlotName(slot);
                return name.Length == 0 ? "EMPTY" : name;
            }

            private void RefreshLibrary()
            {
                EnsureCatalog();
                if (presetSection != null) presetSection.SetCaption(SavedNote());
                presetGrid.SetData(catalog.Count,
                    LibraryLabel,
                    index => catalog[index] != null && catalog[index].Name == activePreset,
                    ApplyCatalog,
                    icons: index => null);
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
                editorPart.Text = TargetPresetRuntime.SuggestName();
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
                editorPart.Text = preset.Name;
                OpenEditor(EditMode.Rename, "RENAME " + preset.Name);
            }

            private void OpenEditor(EditMode mode, string label)
            {
                editorMode = mode;
                editorPart.Show(label);
                RequestRefresh();
            }

            private void CloseEditor()
            {
                editorMode = EditMode.None;
                editorPart?.Hide();
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
                string name = TargetPresetRules.SanitiseName(editorPart.Text);
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

            private void BuildSelectedPage(AvFlow page)
            {
                selectedSection = page.Section(AvIcon.Target, "SELECTED TARGETS", "0 TRACKED");

                groupGrid = page.Add(new MfdPagingGrid(page.Content, 3, 1, pager: false, rowHeight: 44f));
                AddGroupActions();

                selectedGrid = page.Add(new MfdPagingGrid(page.Content, 1, SelectedVisible, readOnly: true, rowHeight: 48f));
                selectedGrid.SetEmptyMessage("NO TARGETS TRACKED\nDESIGNATE CONTACTS ON MAP OR ENGAGE HUD LINK");
                AddRightClickActions(selectedGrid, SelectedVisible, DeselectSelected);
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

            private void StoreGroup(int slot)
            {
                if (slot < 0 || slot >= targetGroups.Length || selectedUnits.Count == 0) return;
                List<Unit> group = targetGroups[slot];
                group.Clear();
                for (int i = 0; i < selectedUnits.Count && group.Count < 32; i++)
                    if (selectedUnits[i] != null && !group.Contains(selectedUnits[i])) group.Add(selectedUnits[i]);
                Echo("STORED " + group.Count + " TARGETS IN GROUP " + (slot + 1));
                RequestRefresh();
            }

            private void RecallGroup(int slot)
            {
                if (!Ready || slot < 0 || slot >= targetGroups.Length) return;
                List<Unit> group = targetGroups[slot];
                if (group.Count == 0) { Echo("GROUP " + (slot + 1) + " EMPTY · RIGHT CLICK TO STORE"); return; }
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (map == null || map.HQ == null || hud == null || hud.aircraft == null || hud.aircraft.disabled) return;
                var eligible = new List<Unit>(group.Count);
                for (int i = 0; i < group.Count; i++)
                {
                    Unit unit = group[i];
                    GlobalPosition known;
                    if (unit != null && !unit.disabled && !selector.CheckExclusions(unit) &&
                        map.HQ.TryGetKnownPosition(unit, out known)) eligible.Add(unit);
                }
                if (eligible.Count == 0) { Echo("GROUP HAS NO ELIGIBLE TRACKED TARGETS"); return; }
                selector.DeselectAll();
                for (int i = 0; i < eligible.Count; i++) hud.SelectUnit(eligible[i]);
                int recalled = 0;
                for (int i = 0; i < eligible.Count; i++)
                    if (hud.GetTargetList().Contains(eligible[i])) recalled++;
                Echo("RECALLED " + recalled + " TARGETS FROM GROUP " + (slot + 1));
                RequestRefresh();
            }

            // ------------------------------------------------------------ camera

            private void BuildCameraPage(AvFlow page)
            {
                page.Section(AvIcon.Camera, "CAMERA MARK", "SURFACE SENSOR TARGET");
                cameraStatusRow = page.Add(new AvRow(page.Content));
                AvButtons cameraActions = page.Buttons(
                    new AvControl.Spec("MARK CAMERA", () => { Camera?.Capture(); RequestRefresh(); }),
                    new AvControl.Spec("CALL AT MARK", () => { Camera?.CallAtMark(); RequestRefresh(); }),
                    new AvControl.Spec("CLEAR MARK", () => { Camera?.Clear(); RequestRefresh(); }));
                cameraCapture = cameraActions.Controls[0];
                cameraCall = cameraActions.Controls[1];
                cameraClear = cameraActions.Controls[2];

                page.Section(AvIcon.ChartLine, "TARGET TELEMETRY", "COORDINATES & RANGE");
                for (int i = 0; i < cameraRows.Length; i++) cameraRows[i] = page.Add(new AvRow(page.Content));

                page.Section(AvIcon.Radar2, "SENSOR ALIGNMENT", "LINE-OF-SIGHT DATUM");
                cameraReticleRow = page.Add(new AvRow(page.Content));
                Note(page, "A surface reference for OPS. Arm support, then CALL AT MARK.");
            }

            private void RefreshCamera()
            {
                if (cameraStatusRow == null) return;
                ICameraTargetService service = Camera;
                if (service == null || !service.Available)
                {
                    cameraStatusRow.Set("CAMERA MARKING UNAVAILABLE",
                        "The support module or its observation source is not installed.", "", AvState.Inert);
                    if (cameraCapture != null) cameraCapture.Interactable = false;
                    if (cameraCall != null) cameraCall.Interactable = false;
                    if (cameraClear != null) cameraClear.Interactable = false;
                    SetCameraTelemetry("—", "—", "—", "—", "—");
                    cameraReticleRow?.Set("SENSOR ALIGNMENT", "SENSOR INTERFACE OFFLINE", "", AvState.Inert);
                    return;
                }

                bool marked = service.HasMark;
                bool armed = !string.IsNullOrEmpty(service.ArmedActionName);
                if (marked)
                {
                    ObservationPoint point = service.Mark;
                    string source = (point.Source ?? "SENSOR").ToUpperInvariant() + " SURFACE MARK";
                    cameraStatusRow.Set(source, "Surface reference recorded; expires 120 seconds after capture.",
                        "", AvState.Ready);
                    SetCameraTelemetry(
                        "X " + AvNum.Fixed(point.X, 0) + " · Z " + AvNum.Fixed(point.Z, 0),
                        AvNum.Fixed(point.Y, 0) + " m ASL",
                        AvNum.Fixed(point.Range / 1000f, 1) + " km",
                        AvNum.Fixed(service.AgeSeconds, 0) + "s",
                        armed ? service.ArmedActionName : "NONE (ARM IN OPS)");
                    cameraReticleRow?.Set("SENSOR ALIGNMENT", "SURFACE MARK LOCKED · REFERENCE RECORDED", "", AvState.Ready);
                }
                else
                {
                    cameraStatusRow.Set("NO ACTIVE MARK",
                        service.Status.ToUpperInvariant() + " · AIM AND PRESS MARK CAMERA.", "", AvState.Inert);
                    SetCameraTelemetry("—", "—", "—", "—", armed ? service.ArmedActionName : "NONE (ARM IN OPS)");
                    cameraReticleRow?.Set("SENSOR ALIGNMENT", "BORESIGHT STANDBY · SLEW CAMERA TO DESIGNATE", "", AvState.Inert);
                }

                if (cameraCapture != null) cameraCapture.Interactable = service.CanCapture;
                if (cameraCall != null)
                {
                    cameraCall.Interactable = service.CanCallAtMark;
                    cameraCall.Label = armed ? "CALL AT MARK" : "SELECT IN OPS";
                }
                if (cameraClear != null) cameraClear.Interactable = marked;
            }

            private void SetCameraTelemetry(string position, string elevation, string range, string age, string armed)
            {
                cameraRows[0]?.Set("GRID (X / Z)", null, position, AvState.Info);
                cameraRows[1]?.Set("ELEVATION (Y)", null, elevation, AvState.Info);
                cameraRows[2]?.Set("SLANT RANGE", null, range, AvState.Info);
                cameraRows[3]?.Set("MARK AGE", null, age, AvState.Info);
                cameraRows[4]?.Set("ARMED CALL-IN", null, armed, armed.StartsWith("NONE") ? AvState.Inert : AvState.Caution);
            }

            // ------------------------------------------------------------ plumbing

            private void SetGrid(MfdPagingGrid grid, List<TargetListSelector_ToggleButton> entries,
                                 Action<int> onClick)
            {
                grid.SetData(entries == null ? 0 : entries.Count,
                    i => NativeTargetLabel(entries[i]),
                    i => entries[i] != null && entries[i].status,
                    onClick, icons: i => entries[i] == null || entries[i].image == null ? null : entries[i].image.sprite);
            }

            private static void AddRightClickActions(MfdPagingGrid grid, int slots, Action<int> onOnly)
            {
                for (int i = 0; i < slots; i++)
                {
                    int slot = i;
                    MfdIconCell cell = grid.CellAt(i);
                    if (cell == null) continue;
                    cell.OnRightClick = () => onOnly(grid.CurrentIndex(slot));
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
                if (selectedSection != null) selectedSection.SetCaption(selectedUnits.Count + " TRACKED");
                for (int i = 0; i < targetGroups.Length; i++)
                    targetGroups[i].RemoveAll(unit => unit == null || unit.disabled);
                groupGrid.SetData(targetGroups.Length,
                    i => "GROUP " + (i + 1),
                    i => targetGroups[i].Count > 0,
                    RecallGroup,
                    subs: i => targetGroups[i].Count + " STORED");
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
                if (resetFilters != null) resetFilters.Interactable = enabled;
                if (clearTargets != null) clearTargets.Interactable = enabled;
                if (followHud != null) followHud.Interactable = enabled;
                if (laser != null) laser.Interactable = enabled;
                factionGrid?.SetInteractable(enabled);
                unitGrid?.SetInteractable(enabled);
                vehicleGrid?.SetInteractable(enabled);
                selectedGrid?.SetInteractable(enabled);
                candidateGrid?.SetInteractable(enabled);
                if (missilePreference != null) missilePreference.Interactable = enabled;
                if (weaponPreference != null) weaponPreference.Interactable = enabled;
                if (airPreference != null) airPreference.Interactable = enabled;
                if (rangePreference != null) rangePreference.Interactable = enabled;
                if (nextCandidate != null) nextCandidate.Interactable = enabled && candidates.Count > 0;
                if (incomingCandidate != null) incomingCandidate.Interactable = enabled;
                if (designateCandidate != null) designateCandidate.Interactable = enabled && candidateFocus != null;
                groupGrid?.SetInteractable(enabled);
                quickGrid?.SetInteractable(enabled);
                presetGrid?.SetInteractable(enabled);
                if (saveAs != null) saveAs.Interactable = enabled;
                if (updatePreset != null) updatePreset.Interactable = enabled;
                if (renamePreset != null) renamePreset.Interactable = enabled;
                if (deletePreset != null) deletePreset.Interactable = enabled;
                if (!enabled)
                {
                    editorMode = EditMode.None;
                    editorPart?.Hide();
                    confirmDelete = null;
                }
                else
                {
                    // A disabled preset action states the one thing it is waiting for,
                    // the way the camera and map preset buttons do.
                    bool custom = SelectedCustom() != null;
                    if (updatePreset != null) updatePreset.Interactable = custom;
                    if (renamePreset != null) renamePreset.Interactable = custom;
                    if (deletePreset != null) deletePreset.Interactable = custom;
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
            /// two lines, so "GROUND VEHICLES" never prints as "GND".
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
