using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // ------------------------------------------------------------ target panel

        /// <summary>
        /// Three pages, one question each. TARGETS: what am I shooting (the held list on top, the known contacts
        /// under it; a click moves a contact on or off the list). FILTER: what is allowed on the list (profiles,
        /// then side / type / vehicle switches, then the result in one sentence). MARK: call an operation onto a
        /// camera mark. Lit always means "on the list"; nothing depends on a right click.
        /// </summary>
        private sealed class TargetPresenter : Presenter
        {
            private enum EditMode { None, SaveAs, Rename }

            private readonly TargetListSelector selector;
            private readonly List<Unit> selectedUnits = new List<Unit>();
            private readonly List<TargetPresetSnapshot> catalog = new List<TargetPresetSnapshot>();
            private readonly List<TargetCandidate> candidates = new List<TargetCandidate>(128);
            private readonly List<Unit>[] targetGroups =
                { new List<Unit>(16), new List<Unit>(16), new List<Unit>(16) };
            private static readonly int[] RangeSteps = { 0, 10, 25, 50, 100 };

            // TARGETS
            private AvSection selectedSection, contactsSection;
            private MfdPagingGrid selectedGrid, candidateGrid;
            private AvControl dropAll, weaponReach;
            private AvControl[] storeGroup, recallGroup, rangeChoice;
            private AvNote listNote;
            private float nextCandidateScan;

            // FILTER
            private AvSection presetSection, vehicleSection;
            private MfdPagingGrid presetGrid, factionGrid, unitGrid, vehicleGrid;
            private AvControl saveAs, updatePreset, renamePreset, deletePreset, laser, resetFilters;
            private AvControl[] slotKeys;
            private PresetEditor editorPart;
            private AvNote filterNote;

            // MARK: state lives in Support through a narrow contract.
            private ICameraTargetService cameraService;
            private readonly AvRow[] cameraRows = new AvRow[3];
            private AvControl cameraCapture, cameraCall, cameraClear;

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
            private int sentenceStamp = int.MinValue;
            private string sentence = "";
            private EditMode editorMode = EditMode.None;

            private const int HeldVisible = 6;
            private const int ContactsVisible = 8;

            private static readonly (AvIcon Icon, string Label)[] Pages =
            {
                (AvIcon.Target, "TARGETS"), (AvIcon.Filter, "FILTER"), (AvIcon.Camera, "MARK"),
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
                    if (cameraService == null) ModuleServices.TryGet(out cameraService);
                    return cameraService;
                }
            }

            protected override string Title => PageTitles[0];
            protected override (AvIcon Icon, string Label)[] TabItems => Pages;

            private static readonly string[] PageTitles =
            {
                "TARGETING / TARGET LIST", "TARGETING / LIST FILTER", "TARGETING / CAMERA MARK",
            };

            protected override string[] TabTips { get; } = new[]
            {
                "Targets: the targets you hold and the contacts your side knows. Click a contact to add it, click a held target to drop it.",
                "Filter: what is allowed onto the target list. Pick a profile or switch sides, types and vehicles yourself.",
                "Mark: record the surface point under the targeting camera and call an armed operation onto it.",
            };

            protected override void BuildContent()
            {
                BuildTargetsPage(CreatePage());
                BuildFilterPage(CreatePage());
                BuildMarkPage(CreatePage());

                // A page that ends above the footer prints these on its bay cover, most important first.
                Console.Orders(0,
                    "Click a known contact to add it to your targets; click a held target to drop it.",
                    "Contacts are listed nearest first, from positions your side actually knows.",
                    "STORE keeps the held targets in a group; the GROUP button brings them back.",
                    "Only contacts the FILTER page allows are listed.");
                Console.Orders(1,
                    "A contact is listed only when its side and its type are both lit.",
                    "Click a profile to apply it. FOLLOW HUD lets the HUD mode set the filter.",
                    "Pick a key under the profiles to switch to the applied profile while flying.",
                    "OVERWRITE saves the switches as they stand into the applied saved profile.");
                Console.Orders(2,
                    "MARK records the surface point under the targeting camera cross-hair.",
                    "A mark lapses 120 seconds after it is taken. MARK again to refresh it.",
                    "Arm an operation on OPS / SUPPORT, then CALL AT MARK to deliver it.");
            }

            protected override void OnPageChanged(int index)
            {
                Console.SetTitle(PageTitles[Mathf.Clamp(index, 0, PageTitles.Length - 1)]);
                CancelDeleteConfirm();
                CloseEditor();
                RequestRefresh();
            }

            protected override void RefreshContent()
            {
                RefreshMark();

                if (!Ready)
                {
                    SetFilterInput(false);
                    sentenceStamp = int.MinValue;
                    filterNote.Set("Waiting for the target filters.");
                    listNote.Set("Waiting for the target filters.");
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

                RefreshHeld();
                RefreshContacts();
                RefreshFilter();

                string words = FilterSentence();
                filterNote.Set(words);
                listNote.Set(words + "  Change it on the FILTER page.");
            }

            protected override string AmbientStatus()
            {
                if (!string.IsNullOrEmpty(echo) && Time.unscaledTime < echoUntil) return echo;
                int page = Console.CurrentPage;
                if (page == 0) return "CLICK A CONTACT TO ADD · A HELD TARGET TO DROP";
                if (page == 2) return "MARK · CALL AT MARK";
                return "LIT = LISTED · RIGHT CLICK KEEPS ONLY THAT ONE";
            }

            private void Echo(string text)
            {
                echo = text;
                echoUntil = Time.unscaledTime + 2.4f;
            }

            // ------------------------------------------------------------- targets

            private struct TargetCandidate
            {
                public Unit Unit;
                public float DistanceKm;
                public int BearingDeg;
                public string Side;
            }

            private void BuildTargetsPage(AvFlow page)
            {
                selectedSection = page.Section(AvIcon.Target, "HELD TARGETS", "NONE HELD");
                selectedGrid = AddGrid(page, new MfdPagingGrid(page.Content, 1, HeldVisible, rowHeight: 40f, words: false));
                selectedGrid.SetEmptyMessage("NO TARGETS HELD. ADD ONE FROM THE CONTACTS BELOW.");

                AvControl[] store = page.Buttons(
                    new AvControl.Spec("DROP ALL", () => { if (Ready) { selector.DeselectAll(); RequestRefresh(); } },
                        AvButtonStyle.Default, AvIcon.Eraser),
                    new AvControl.Spec("STORE 1", () => StoreGroup(0), AvButtonStyle.Default, AvIcon.Plus),
                    new AvControl.Spec("STORE 2", () => StoreGroup(1), AvButtonStyle.Default, AvIcon.Plus),
                    new AvControl.Spec("STORE 3", () => StoreGroup(2), AvButtonStyle.Default, AvIcon.Plus)).Controls;
                dropAll = store[0];
                storeGroup = new[] { store[1], store[2], store[3] };
                recallGroup = page.Buttons(
                    new AvControl.Spec("GROUP 1", () => RecallGroup(0), AvButtonStyle.Default, AvIcon.UsersGroup),
                    new AvControl.Spec("GROUP 2", () => RecallGroup(1), AvButtonStyle.Default, AvIcon.UsersGroup),
                    new AvControl.Spec("GROUP 3", () => RecallGroup(2), AvButtonStyle.Default, AvIcon.UsersGroup)).Controls;
                for (int i = 0; i < storeGroup.Length; i++)
                {
                    storeGroup[i].Help = "Store: keep the held targets (up to 32) as group " + (i + 1) + ", replacing what it held.";
                    recallGroup[i].Help = "Group " + (i + 1) + ": replace the held targets with the ones stored in this group.";
                }

                contactsSection = page.Section(AvIcon.Eye, "KNOWN CONTACTS", "NEAREST FIRST");
                AvControl[] narrow = page.Buttons(
                    new AvControl.Spec("ANY", () => SetRange(0), AvButtonStyle.Tab),
                    new AvControl.Spec("10 KM", () => SetRange(1), AvButtonStyle.Tab),
                    new AvControl.Spec("25 KM", () => SetRange(2), AvButtonStyle.Tab),
                    new AvControl.Spec("50 KM", () => SetRange(3), AvButtonStyle.Tab),
                    new AvControl.Spec("100 KM", () => SetRange(4), AvButtonStyle.Tab),
                    new AvControl.Spec("REACH", ToggleWeaponReach, AvButtonStyle.Toggle)).Controls;
                rangeChoice = new[] { narrow[0], narrow[1], narrow[2], narrow[3], narrow[4] };
                weaponReach = narrow[5];
                for (int i = 0; i < rangeChoice.Length; i++)
                    rangeChoice[i].Help = i == 0 ? "Any range: list every known contact, however far."
                        : "List only contacts within " + RangeSteps[i] + " km of your aircraft.";
                weaponReach.Help = "Reach: list only contacts the selected weapon can engage right now.";

                candidateGrid = AddGrid(page, new MfdPagingGrid(page.Content, 1, ContactsVisible, rowHeight: 40f, words: false));
                candidateGrid.SetEmptyMessage("NO CONTACTS");
                listNote = page.Add(new AvNote(page.Content, "Waiting for the target filters."));
            }

            private void SetRange(int step)
            {
                var settings = TargetPresetRuntime.Settings;
                if (settings == null) return;
                settings.TargetRangeKm.Value = RangeSteps[step];
                nextCandidateScan = 0f;
                RequestRefresh();
            }

            private void ToggleWeaponReach()
            {
                var settings = TargetPresetRuntime.Settings;
                if (settings == null) return;
                settings.TargetWeaponOnly.Value = !settings.TargetWeaponOnly.Value;
                nextCandidateScan = 0f;
                RequestRefresh();
            }

            private void RefreshHeld()
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

                bool onPage = Console.CurrentPage == 0;   // the sub-lines only matter while this page shows
                selectedGrid.SetData(selectedUnits.Count,
                    i => TargetUnitLabel(selectedUnits[i]), i => true, DropHeld,
                    icons: i => selectedUnits[i].definition == null ? null : selectedUnits[i].definition.mapIcon,
                    details: i => TargetUnitLabel(selectedUnits[i]) + ". Click to drop it from your targets.",
                    subs: onPage ? (Func<int, string>)(i => TrackSub(selectedUnits[i])) : null);
                selectedSection.SetCaption(selectedUnits.Count > 0 ? selectedUnits.Count + " HELD · CLICK TO DROP" : "NONE HELD");

                bool any = selectedUnits.Count > 0;
                dropAll.Interactable = any;
                dropAll.Help = any ? "Drop every held target." : "No held targets to drop.";
                for (int i = 0; i < targetGroups.Length; i++)
                {
                    targetGroups[i].RemoveAll(unit => unit == null || unit.disabled);
                    storeGroup[i].Interactable = any;
                    recallGroup[i].Interactable = targetGroups[i].Count > 0;
                    recallGroup[i].Label = "GROUP " + (i + 1) + " · " + (targetGroups[i].Count > 0 ? targetGroups[i].Count.ToString() : "EMPTY");
                }
            }

            private void RefreshContacts()
            {
                var settings = TargetPresetRuntime.Settings;
                if (settings == null) return;
                int step = Mathf.Max(0, Array.IndexOf(RangeSteps, settings.TargetRangeKm.Value));
                for (int i = 0; i < rangeChoice.Length; i++) rangeChoice[i].Latched = i == step;
                weaponReach.Latched = settings.TargetWeaponOnly.Value;
                if (Console.CurrentPage != 0) return;

                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                bool flying = hud != null && hud.aircraft != null && !hud.aircraft.disabled;
                candidateGrid.SetEmptyMessage(flying ? "NO CONTACTS MATCH THE FILTER" : "NO AIRCRAFT");
                if (Time.unscaledTime >= nextCandidateScan)
                {
                    ScanCandidates();
                    nextCandidateScan = Time.unscaledTime + 0.5f;
                }
                candidateGrid.SetData(candidates.Count,
                    i => TargetUnitLabel(candidates[i].Unit),
                    i => selectedUnits.Contains(candidates[i].Unit),
                    ToggleCandidate,
                    icons: i => candidates[i].Unit.definition == null ? null : candidates[i].Unit.definition.mapIcon,
                    details: i => TargetUnitLabel(candidates[i].Unit) +
                                  (selectedUnits.Contains(candidates[i].Unit) ? ". Held. Click to drop it." : ". Click to add it to your targets."),
                    subs: i => AvNum.Fixed(candidates[i].DistanceKm, 1) + " KM · BRG " + candidates[i].BearingDeg.ToString("000") +
                               " · " + candidates[i].Side + " · " + ClassWord(candidates[i].Unit));
                contactsSection.SetCaption(flying ? candidates.Count + " · NEAREST FIRST · CLICK TO ADD" : "NO AIRCRAFT");
            }

            private static string SideWord(Unit unit)
            {
                switch (DynamicMap.GetFactionMode(unit.NetworkHQ, true))
                {
                    case FactionMode.Friendly: return "FRIENDLY";
                    case FactionMode.Enemy: return "HOSTILE";
                    default: return "UNKNOWN";
                }
            }

            private static string ClassWord(Unit unit)
            {
                switch (unit.definition == null ? "" : unit.definition.GetType().Name)
                {
                    case "AircraftDefinition": return "AIRCRAFT";
                    case "MissileDefinition": return "MISSILE";
                    case "VehicleDefinition": return "VEHICLE";
                    case "BuildingDefinition": return "STRUCTURE";
                    case "ShipDefinition": return "SHIP";
                    default: return "UNIT";
                }
            }

            /// <summary>Known contacts the filter allows, nearest first, capped at 128.</summary>
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
                    if (!map.HQ.TryGetKnownPosition(unit, out GlobalPosition known)) continue;
                    GlobalPosition own = hud.aircraft.GlobalPosition();
                    float distance = FastMath.Distance(own, known) / 1000f;
                    if (!float.IsFinite(distance) ||
                        (settings.TargetRangeKm.Value > 0 && distance > settings.TargetRangeKm.Value)) continue;
                    if (settings.TargetWeaponOnly.Value &&
                        (station == null || station.CalcOpportunityThreat(unit.definition, hud.aircraft).opportunity <= 0f))
                        continue;
                    int insert = candidates.FindIndex(c => c.DistanceKm > distance);
                    if (insert < 0) insert = candidates.Count;
                    if (insert >= 128) continue;
                    Vector3 offset = known - own;
                    candidates.Insert(insert, new TargetCandidate
                    {
                        Unit = unit,
                        DistanceKm = distance,
                        BearingDeg = ((Mathf.RoundToInt(Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg) % 360) + 360) % 360,
                        Side = SideWord(unit),
                    });
                    if (candidates.Count > 128) candidates.RemoveAt(128);
                }
            }

            /// <summary>A click on a contact: onto the target list (and lit on the map), or off it when already held.</summary>
            private void ToggleCandidate(int index)
            {
                if (!Ready || index < 0 || index >= candidates.Count) return;
                Unit unit = candidates[index].Unit;
                if (unit == null) return;
                if (selectedUnits.Contains(unit))
                {
                    selector.ForceDeselect(unit);
                    Echo("DROPPED " + TargetUnitLabel(unit));
                    RequestRefresh();
                    return;
                }
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (hud == null || hud.aircraft == null || hud.aircraft.disabled || map == null || map.HQ == null ||
                    !map.HQ.TryGetKnownPosition(unit, out _) || selector.CheckExclusions(unit)) return;
                hud.SelectUnit(unit);
                map.HighlightIcon(unit);
                Echo(hud.GetTargetList().Contains(unit)
                    ? "ADDED " + TargetUnitLabel(unit)
                    : "CONTACT NOT AVAILABLE TO THE TARGET LIST");
                RequestRefresh();
            }

            /// <summary>One line under a held target: class, side and range from the player (known position only).</summary>
            private static string TrackSub(Unit unit)
            {
                if (unit == null) return "";
                string head = ClassWord(unit) + " · " + SideWord(unit);
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                GlobalPosition at;
                if (map == null || map.HQ == null || hud == null || hud.aircraft == null ||
                    !map.HQ.TryGetKnownPosition(unit, out at)) return head + " · POSITION UNKNOWN";
                float km = FastMath.Distance(hud.aircraft.GlobalPosition(), at) / 1000f;
                return !float.IsFinite(km) ? head + " · POSITION UNKNOWN" : head + " · " + AvNum.Fixed(km, 1) + " KM";
            }

            private void DropHeld(int index)
            {
                if (!Ready || index < 0 || index >= selectedUnits.Count) return;
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
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (group.Count == 0 || map == null || map.HQ == null || hud == null || hud.aircraft == null || hud.aircraft.disabled) return;
                var eligible = new List<Unit>(group.Count);
                for (int i = 0; i < group.Count; i++)
                {
                    Unit unit = group[i];
                    if (unit != null && !unit.disabled && !selector.CheckExclusions(unit) &&
                        map.HQ.TryGetKnownPosition(unit, out _)) eligible.Add(unit);
                }
                if (eligible.Count == 0) { Echo("GROUP " + (slot + 1) + ": THE FILTER ALLOWS NONE OF ITS TARGETS"); return; }
                selector.DeselectAll();
                for (int i = 0; i < eligible.Count; i++) hud.SelectUnit(eligible[i]);
                int recalled = 0;
                for (int i = 0; i < eligible.Count; i++)
                    if (hud.GetTargetList().Contains(eligible[i])) recalled++;
                Echo("RECALLED " + recalled + " TARGETS FROM GROUP " + (slot + 1));
                RequestRefresh();
            }

            // -------------------------------------------------------------- filter

            private void BuildFilterPage(AvFlow page)
            {
                presetSection = page.Section(AvIcon.Bookmark, "PROFILES", SavedNote());
                presetGrid = AddGrid(page, new MfdPagingGrid(page.Content, 2, 4, rowHeight: 40f, words: false));

                AvControl[] actions = page.Buttons(
                    new AvControl.Spec("SAVE AS", BeginSaveAs, AvButtonStyle.Default, AvIcon.Plus),
                    new AvControl.Spec("OVERWRITE", UpdateSelected, AvButtonStyle.Default, AvIcon.Refresh),
                    new AvControl.Spec("RENAME", BeginRename, AvButtonStyle.Default, AvIcon.Pencil),
                    new AvControl.Spec("DELETE", PressDelete, AvButtonStyle.Danger, AvIcon.X)).Controls;
                saveAs = actions[0];
                updatePreset = actions[1];
                renamePreset = actions[2];
                deletePreset = actions[3];
                saveAs.Help = "Save as: store the switches below as a new named profile. Names are A-Z, 0-9, up to 14 characters.";
                slotKeys = page.Buttons(
                    new AvControl.Spec("KEY 1", () => PressSlot(0), AvButtonStyle.Toggle),
                    new AvControl.Spec("KEY 2", () => PressSlot(1), AvButtonStyle.Toggle),
                    new AvControl.Spec("KEY 3", () => PressSlot(2), AvButtonStyle.Toggle)).Controls;
                editorPart = page.Add(new PresetEditor(page, CommitEdit, CancelEdit));

                page.Section(AvIcon.Shield, "SIDE", "LIT = LISTED");
                factionGrid = AddGrid(page, new MfdPagingGrid(page.Content, 2, 1, pager: false, rowHeight: 56f, tile: true, words: false));
                factionGrid.SetGlyphs(i => FilterGlyph(selector == null ? null : selector.toggleFactionItems, i,
                    i == 0 ? "FRIENDLY" : i == 1 ? "ENEMY" : ""));
                AddRightClickActions(factionGrid, 2, OnlyFaction);

                page.Section(AvIcon.LayersSubtract, "TYPE", "LIT = LISTED");
                unitGrid = AddGrid(page, new MfdPagingGrid(page.Content, 5, 1, pager: false, rowHeight: 56f, tile: true, words: false));
                unitGrid.SetGlyphs(i => FilterGlyph(selector == null ? null : selector.toggleUnitTypesItems, i,
                    i >= 0 && i < UnitFallback.Length ? UnitFallback[i] : ""));
                AddRightClickActions(unitGrid, 5, OnlyUnitType);

                // Vehicle switches only narrow GROUND, so they leave the page with it.
                vehicleSection = page.Section(AvIcon.Stack2, "GROUND VEHICLES", "NARROWS GROUND");
                vehicleGrid = AddGrid(page, new MfdPagingGrid(page.Content, 5, 2, pager: false, rowHeight: 56f, tile: true, words: false));
                vehicleGrid.SetGlyphs(i => VehicleGlyph(
                    selector != null && selector.toggleVehicleTypesItems != null && i >= 0 && i < selector.toggleVehicleTypesItems.Count
                        ? NativeTargetLabel(selector.toggleVehicleTypesItems[i]) : "", i));
                AddRightClickActions(vehicleGrid, 10, OnlyVehicleType);

                AvControl[] extra = page.Buttons(
                    new AvControl.Spec("LASED ONLY", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.toggleLaser.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle, AvIcon.Bolt),
                    new AvControl.Spec("LIST EVERYTHING", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.ResetFilters();
                        selector.NeedUpdateIcons();
                        Echo("FILTER RESET");
                        RequestRefresh();
                    }, AvButtonStyle.Default, AvIcon.Refresh)).Controls;
                laser = extra[0];
                resetFilters = extra[1];
                laser.Help = "Lased only: list only targets that are being lased right now.";
                resetFilters.Help = "List everything: light every side, type and vehicle and turn LASED ONLY off.";

                filterNote = page.Add(new AvNote(page.Content, "Waiting for the target filters."));
            }

            private void RefreshFilter()
            {
                bool hud = selector.toggleFollowHUD.status;
                presetSection.SetCaption(SavedNote());
                // Row 0 is FOLLOW HUD: it sets the filter like a profile does, so it lives among them.
                presetGrid.SetData(catalog.Count + 1,
                    i => i == 0 ? "FOLLOW HUD" : LibraryLabel(i - 1),
                    i => i == 0 ? hud : !hud && catalog[i - 1] != null && catalog[i - 1].Name == activePreset,
                    i => { if (i == 0) ToggleFollowHud(); else ApplyCatalog(i - 1); },
                    subs: i => i == 0 ? "THE HUD MODE SETS THE FILTER" : LibrarySub(i - 1),
                    details: i =>
                    {
                        if (i == 0) return "Follow HUD: the target list lists whatever the HUD mode shows. Click again to set the filter yourself.";
                        TargetPresetSnapshot preset = catalog[i - 1];
                        if (preset == null) return null;
                        return preset.Name + " — " + (TargetPresetRuntime.IsBuiltIn(i - 1)
                            ? MfdTargetPresets.Descriptions[i - 1] : TargetPresetRules.Summary(preset)) + "  ·  Click to apply.";
                    });

                bool inCatalog = TargetPresetRuntime.IndexOfCatalogName(selectedPreset) >= 0;
                for (int slot = 0; slot < slotKeys.Length; slot++)
                {
                    string name = TargetPresetRuntime.QuickSlotName(slot);
                    string key = KeyLabel(TargetPresetRuntime.Key(slot));
                    bool mine = name.Length > 0 && name == selectedPreset;
                    slotKeys[slot].Label = key + " · " + (name.Length == 0 ? "EMPTY" : name);
                    slotKeys[slot].Latched = mine;
                    slotKeys[slot].Help = mine ? "Quick key " + key + " applies " + name + " while flying. Click to free the key."
                        : inCatalog ? "Put " + selectedPreset + " on quick key " + key + (name.Length == 0 ? "." : ", replacing " + name + ".")
                        : "Apply a profile first, then pick its quick key.";
                }

                SetGrid(factionGrid, selector.toggleFactionItems, ToggleFaction);
                SetGrid(unitGrid, selector.toggleUnitTypesItems, ToggleUnitType);
                SetGrid(vehicleGrid, selector.toggleVehicleTypesItems, ToggleVehicleType);
                bool ground = GroundOpen();
                vehicleSection.SetShown(ground);
                vehicleGrid.SetShown(ground);
                laser.Latched = selector.toggleLaser.status;
            }

            private void ToggleFollowHud()
            {
                if (!Ready) return;
                CancelDeleteConfirm();
                selector.toggleFollowHUD.Toggle();
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            /// <summary>True while the GROUND type is lit (or the game names no such type, so nothing can be hidden safely).</summary>
            private bool GroundOpen()
            {
                List<TargetListSelector_ToggleButton> types = selector.toggleUnitTypesItems;
                for (int i = 0; i < types.Count; i++)
                    if (NativeTargetLabel(types[i]) == "GROUND") return types[i].status;
                return true;
            }

            /// <summary>The whole filter in one sentence; rebuilt only when a switch moved.</summary>
            private string FilterSentence()
            {
                if (sentenceStamp == activeStamp) return sentence;   // the stamp covers every switch, the laser and the HUD link
                sentenceStamp = activeStamp;
                int sideOpen, typeOpen;
                string sides = OpenWords(selector.toggleFactionItems, out sideOpen, "BOTH SIDES");
                string types = OpenWords(selector.toggleUnitTypesItems, out typeOpen, "EVERY TYPE");
                if (sideOpen == 0 || typeOpen == 0)
                    return sentence = "NOTHING IS LISTED. Light at least one side and one type.";
                var text = new System.Text.StringBuilder(selector.toggleFollowHUD.status ? "FOLLOWING THE HUD: " : "LISTING: ");
                text.Append(sides).Append(" · ").Append(types);
                if (GroundOpen())
                {
                    string vehicles = OpenWords(selector.toggleVehicleTypesItems, out _, null);
                    if (vehicles != null) text.Append(" · VEHICLES: ").Append(vehicles);
                }
                if (selector.toggleLaser.status) text.Append(" · LASED ONLY");
                return sentence = text.Append('.').ToString();
            }

            /// <summary>
            /// A switch group in words, shortest form first: the lit names when few are lit, "ALL BUT" the dark ones
            /// when most are. <paramref name="allOpen"/> stands in when every one is lit.
            /// </summary>
            private static string OpenWords(List<TargetListSelector_ToggleButton> entries, out int open, string allOpen)
            {
                int total = entries.Count;
                open = CountEnabled(entries);
                if (open == total) return allOpen;
                if (open == 0) return "NONE";
                bool listOpen = open * 2 <= total;
                var text = new System.Text.StringBuilder(listOpen ? "" : "ALL BUT ");
                bool first = true;
                for (int i = 0; i < total; i++)
                {
                    if ((entries[i] != null && entries[i].status) != listOpen) continue;
                    if (!first) text.Append(", ");
                    text.Append(NativeTargetLabel(entries[i]));
                    first = false;
                }
                return text.ToString();
            }

            private static readonly string[] UnitFallback =
                { "AIRCRAFT", "MISSILES", "GROUND", "BUILDINGS", "SHIPS" };

            private static AvIcon FilterGlyph(List<TargetListSelector_ToggleButton> entries, int index, string fallback = "")
            {
                switch (entries != null && index >= 0 && index < entries.Count ? NativeTargetLabel(entries[index]) : fallback)
                {
                    case "FRIENDLY": return AvIcon.Shield;
                    case "ENEMY": return AvIcon.Skull;
                    case "AIRCRAFT": return AvIcon.Plane;
                    case "MISSILES": return AvIcon.ArrowUpRight;
                    case "GROUND": return AvIcon.ChartArrows;
                    case "BUILDINGS": return AvIcon.BuildingBank;
                    case "SHIPS": return AvIcon.Flag;
                    default: return AvIcon.Circle;
                }
            }

            // ------------------------------------------------------------ profiles

            private string SavedNote() =>
                TargetPresetRuntime.Library.Count + " / " + TargetPresetLibrary.MaxCustomPresets + " SAVED · CLICK TO APPLY";

            private static string KeyLabel(KeyCode key) => key == KeyCode.None ? "NO KEY" : key.ToString().ToUpperInvariant();

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

            /// <summary>A quick key under the profiles: takes the applied profile, or frees itself when it already holds it.</summary>
            private void PressSlot(int slot)
            {
                if (!Ready) return;
                CancelDeleteConfirm();
                string key = KeyLabel(TargetPresetRuntime.Key(slot));
                if (selectedPreset.Length > 0 && TargetPresetRuntime.QuickSlotName(slot) == selectedPreset)
                {
                    TargetPresetRuntime.ClearQuickSlot(slot);
                    Echo("KEY " + key + " FREED");
                }
                else if (TargetPresetRuntime.AssignQuickSlot(selectedPreset, slot)) Echo(selectedPreset + " ON KEY " + key);
                else Echo("APPLY A PROFILE FIRST, THEN PICK ITS KEY");
                RequestRefresh();
            }

            private string LibraryLabel(int index)
            {
                TargetPresetSnapshot preset = catalog[index];
                if (preset == null) return "";
                for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                    if (TargetPresetRuntime.QuickSlotName(slot) == preset.Name)
                        return preset.Name + "  [" + KeyLabel(TargetPresetRuntime.Key(slot)) + "]";
                return preset.Name;
            }

            /// <summary>Built-in or saved, and whether the switches have moved since it was applied.</summary>
            private string LibrarySub(int index)
            {
                TargetPresetSnapshot preset = catalog[index];
                if (preset == null) return "";
                string kind = TargetPresetRuntime.IsBuiltIn(index) ? "BUILT-IN" : "SAVED";
                return preset.Name == selectedPreset && preset.Name != activePreset ? kind + " · CHANGED SINCE" : kind;
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

            /// <summary>The profile OVERWRITE, RENAME, DELETE and the quick keys act on: the one the switches match,
            /// else the last one applied (so a saved profile can be changed and then overwritten).</summary>
            private void TrackSelectedPreset()
            {
                if (TargetPresetRuntime.IndexOfCatalogName(activePreset) >= 0 ||
                    TargetPresetRuntime.IndexOfCatalogName(selectedPreset) < 0)
                    selectedPreset = activePreset ?? "";
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
                OpenEditor(EditMode.SaveAs, "SAVE THE FILTER AS");
            }

            private void BeginRename()
            {
                if (!Ready) return;
                TargetPresetSnapshot preset = SelectedCustom();
                if (preset == null) return;
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
                    case TargetPresetSaveResult.NameInUse: return name + " EXISTS — USE OVERWRITE TO REPLACE IT";
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
                if (preset == null) return;
                TargetPresetSaveResult result = TargetPresetRuntime.SaveCurrent(selector, preset.Name, true);
                Echo(result == TargetPresetSaveResult.Ok ? "OVERWROTE " + preset.Name : SaveMessage(result, preset.Name));
                RequestRefresh();
            }

            private void PressDelete()
            {
                if (!Ready) return;
                TargetPresetSnapshot preset = SelectedCustom();
                if (preset == null) return;

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

            // ---------------------------------------------------------------- mark

            private static readonly string[] MarkSteps = { "1 · CAMERA VIEW", "2 · POINT MARKED", "3 · OPERATION ARMED" };

            private void BuildMarkPage(AvFlow page)
            {
                // The chain from camera to delivery, one step per row, in the order the pilot works through it.
                page.Section(AvIcon.Camera, "CALL AT A CAMERA MARK", "THREE STEPS");
                for (int i = 0; i < cameraRows.Length; i++)
                {
                    cameraRows[i] = page.Add(new AvRow(page.Content));
                    cameraRows[i].Set(MarkSteps[i], "Waiting for the camera service.", "—", AvState.Inert);
                }
                AvControl[] actions = page.Buttons(
                    new AvControl.Spec("MARK", () => { Camera?.Capture(); RequestRefresh(); }, AvButtonStyle.Default, AvIcon.Camera),
                    new AvControl.Spec("CALL AT MARK", () => { Camera?.CallAtMark(); RequestRefresh(); }, AvButtonStyle.Primary, AvIcon.CurrentLocation),
                    new AvControl.Spec("CLEAR", () => { Camera?.Clear(); RequestRefresh(); }, AvButtonStyle.Default, AvIcon.Eraser)).Controls;
                cameraCapture = actions[0];
                cameraCall = actions[1];
                cameraClear = actions[2];
            }

            private void RefreshMark()
            {
                if (cameraCapture == null) return;
                ICameraTargetService service = Camera;
                if (service == null || !service.Available)
                {
                    const string offline =
                        "Camera marking is unavailable: the support module or its observation source is not installed.";
                    cameraCapture.Interactable = cameraCall.Interactable = cameraClear.Interactable = false;
                    cameraCapture.Help = cameraCall.Help = cameraClear.Help = offline;
                    cameraRows[0].Set(MarkSteps[0], offline, "OFFLINE", AvState.Inert);
                    cameraRows[1].Set(MarkSteps[1], "Needs the camera marking service.", "—", AvState.Inert);
                    cameraRows[2].Set(MarkSteps[2], "Needs the camera marking service.", "—", AvState.Inert);
                    return;
                }

                bool view = service.CanCapture, marked = service.HasMark;
                string armedName = service.ArmedActionName;
                bool armed = !string.IsNullOrEmpty(armedName);
                cameraRows[0].Set(MarkSteps[0],
                    view ? "The targeting camera is live. MARK records the point under its cross-hair."
                         : "No live camera view on your aircraft. MARK needs one.",
                    view ? "LIVE" : "NO VIEW", view ? AvState.Ready : AvState.Inert);
                if (marked)
                {
                    float age = Mathf.Max(0f, service.AgeSeconds);
                    cameraRows[1].Set(MarkSteps[1], MarkWords(service.Mark),
                        AvNum.Fixed(Mathf.Max(0f, 120f - age), 0) + " S LEFT", age > 90f ? AvState.Caution : AvState.Ready);
                }
                else cameraRows[1].Set(MarkSteps[1], "No point recorded yet.", "NONE", AvState.Inert);
                cameraRows[2].Set(MarkSteps[2], armed ? armedName : "None armed. Arm one on OPS / SUPPORT.",
                    armed ? "ARMED" : "NONE", armed ? AvState.Ready : AvState.Inert);

                cameraCapture.Interactable = view;
                cameraCall.Interactable = service.CanCallAtMark;
                cameraClear.Interactable = marked;
                cameraCapture.Help = view ? "Record the surface point under the targeting camera."
                    : "Needs a live targeting camera view on your aircraft.";
                cameraCall.Help = !marked ? "Take a mark first."
                    : !armed ? "Arm an operation on OPS / SUPPORT first."
                    : "Deliver the armed operation onto this mark.";
                cameraClear.Help = marked ? "Clear the mark." : "No mark to clear.";
            }

            /// <summary>The mark in one line: source, range and bearing from the player (when flying), elevation and grid.</summary>
            private static string MarkWords(ObservationPoint point)
            {
                string words = (point.Source ?? "SENSOR").ToUpperInvariant() + " · " + AvNum.Fixed(Mathf.Max(0f, point.Range / 1000f), 1) + " KM";
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud != null && hud.aircraft != null && !hud.aircraft.disabled)
                {
                    GlobalPosition own = hud.aircraft.GlobalPosition();
                    float turn = Mathf.Atan2(point.X - own.x, point.Z - own.z) * Mathf.Rad2Deg;
                    if (float.IsFinite(turn))
                        words += " · BRG " + (((Mathf.RoundToInt(turn) % 360) + 360) % 360).ToString("000");
                }
                return words + " · ELEV " + AvNum.Fixed(point.Y, 0) + " M · X " + AvNum.Fixed(point.X, 0) + " Z " + AvNum.Fixed(point.Z, 0);
            }

            // ------------------------------------------------------------ plumbing

            private void SetGrid(MfdPagingGrid grid, List<TargetListSelector_ToggleButton> entries,
                                 Action<int> onClick)
            {
                grid.SetData(entries == null ? 0 : entries.Count,
                    i => NativeTargetLabel(entries[i]),
                    i => entries[i] != null && entries[i].status,
                    onClick, icons: i => entries[i] == null || entries[i].image == null ? null : entries[i].image.sprite,
                    details: i => (FilterNote(NativeTargetLabel(entries[i])) ?? NativeTargetLabel(entries[i])) +
                                  ". Click to list or unlist it; right click lists only this one.");
            }

            /// <summary>What each switch covers, for its hover line.</summary>
            private static string FilterNote(string label)
            {
                switch (label)
                {
                    case "FRIENDLY": return "Friendly contacts";
                    case "ENEMY": return "Hostile contacts";
                    case "AIRCRAFT": return "Airborne tracks";
                    case "MISSILES": return "Missiles in flight";
                    case "GROUND": return "Vehicles & troops";
                    case "BUILDINGS": return "Bases & structures";
                    case "SHIPS": return "Naval contacts";
                    case "TRUCK": return "Supply trucks";
                    case "UGV": return "Unmanned ground";
                    case "LCV": return "Light combat";
                    case "AFV": return "Armored vehicles";
                    case "MBT": return "Main battle tanks";
                    case "ART": return "Field artillery";
                    case "AAA": return "Anti-air guns";
                    case "IR SAM": return "Heat-seeking SAM";
                    case "R SAM": return "Radar-guided SAM";
                    case "RDR":
                    case "RADAR": return "Search radars";
                    default: return null;
                }
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

            private void Toggle(List<TargetListSelector_ToggleButton> entries, int index) => Switch(entries, index, false);

            private void SetOnly(List<TargetListSelector_ToggleButton> entries, int index) => Switch(entries, index, true);

            /// <summary>Flips one switch, or lights only that one.</summary>
            private void Switch(List<TargetListSelector_ToggleButton> entries, int index, bool only)
            {
                if (!Ready) return;
                if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return;
                CancelDeleteConfirm();
                if (only) selector.SetOnlyItem(entries[index]);
                else entries[index].Toggle();
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private bool Ready => TargetPresetRuntime.Ready(selector);

            private void SetFilterInput(bool enabled)
            {
                foreach (MfdPagingGrid grid in new[] { selectedGrid, candidateGrid, presetGrid, factionGrid, unitGrid, vehicleGrid })
                    grid.SetInteractable(enabled);
                foreach (AvControl control in rangeChoice) control.Interactable = enabled;
                foreach (AvControl control in slotKeys) control.Interactable = enabled;
                weaponReach.Interactable = laser.Interactable = resetFilters.Interactable = saveAs.Interactable = enabled;
                if (!enabled)
                {
                    editorMode = EditMode.None;
                    editorPart.Hide();
                    confirmDelete = null;
                    dropAll.Interactable = false;
                    foreach (AvControl control in storeGroup) control.Interactable = false;
                    foreach (AvControl control in recallGroup) control.Interactable = false;
                }

                // A disabled profile action states the one thing it is waiting for.
                bool custom = enabled && SelectedCustom() != null;
                updatePreset.Interactable = renamePreset.Interactable = deletePreset.Interactable = custom;
                string wait = enabled ? "Apply one of your saved profiles first. Built-in profiles are fixed." : "Waiting for the target filters.";
                updatePreset.Help = custom ? "Overwrite " + selectedPreset + " with the switches as they stand." : wait;
                renamePreset.Help = custom ? "Rename " + selectedPreset + "." : wait;
                deletePreset.Help = custom ? "Delete " + selectedPreset + ". Asks for a second press." : wait;
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
                // The grid wraps or shrinks the whole call sign; a unit's own name is never cut to a fixed column.
                if (unit == null) return "UNKNOWN TARGET";
                string code = unit.definition == null ? "UNIT" : unit.definition.code;
                string name = string.IsNullOrEmpty(unit.unitName) ? code : unit.unitName;
                return (string.IsNullOrEmpty(name) ? "UNIT" : name).ToUpperInvariant();
            }
        }
    }
}
