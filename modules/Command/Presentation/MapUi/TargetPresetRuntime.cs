using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Command.Configuration;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Game-side presets: binds the pure library to the config, reads the live native
    /// selector, and applies or captures filter state. Scene-independent by design —
    /// presets survive mission reloads; only the selector they apply to is per-scene.
    /// </summary>
    internal static class TargetPresetRuntime
    {
        public static readonly int BuiltInCount = MfdTargetPresets.Names.Length;

        private static CommandSettings settings;
        private static TargetPresetLibrary library = new TargetPresetLibrary();
        private static int version;

        public static bool Configured => settings != null;

        public static TargetPresetLibrary Library => library;

        /// <summary>Bumped on every persisted change so the panel can cache its catalog.</summary>
        public static int Version => version;

        public static bool WheelEnabled => settings == null || settings.TargetPresetWheel.Value;

        public static void Configure(CommandSettings config)
        {
            settings = config;
            library = TargetPresetLibrary.Decode(config.TargetPresets.Value, config.TargetPresetSlots.Value);
            version++;
        }

        // ------------------------------------------------------------------ catalog

        public static int CatalogCount => BuiltInCount + library.Count;

        public static string CatalogNameAt(int index)
        {
            if (index < 0) return null;
            if (index < BuiltInCount) return MfdTargetPresets.Names[index];
            TargetPresetSnapshot custom = library.At(index - BuiltInCount);
            return custom == null ? null : custom.Name;
        }

        public static bool IsBuiltIn(int index) => index >= 0 && index < BuiltInCount;

        public static bool CatalogContains(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < BuiltInCount; i++)
                if (string.Equals(MfdTargetPresets.Names[i], name, StringComparison.Ordinal)) return true;
            return library.IndexOf(name) >= 0;
        }

        public static TargetPresetSnapshot CatalogAt(TargetListSelector selector, int index)
        {
            if (index < 0) return null;
            if (index < BuiltInCount) return BuildBuiltIn(selector, (MfdTargetPreset)index);
            TargetPresetSnapshot custom = library.At(index - BuiltInCount);
            return custom == null ? null : custom.Copy(custom.Name);
        }

        public static int IndexOfCatalogName(string name)
        {
            for (int i = 0; i < BuiltInCount; i++)
                if (string.Equals(MfdTargetPresets.Names[i], name, StringComparison.Ordinal)) return i;
            int custom = library.IndexOf(name);
            return custom < 0 ? -1 : BuiltInCount + custom;
        }

        // -------------------------------------------------------------------- apply

        public static bool Ready(TargetListSelector selector) =>
            selector != null && selector.toggleFollowHUD != null && selector.toggleLaser != null &&
            selector.toggleFactionItems != null && selector.toggleFactionItems.Count > 0 &&
            selector.toggleUnitTypesItems != null && selector.toggleUnitTypesItems.Count > 0 &&
            selector.toggleVehicleTypesItems != null && selector.toggleVehicleTypesItems.Count > 0;

        public static void ApplyIndex(TargetListSelector selector, int index)
        {
            Apply(selector, CatalogAt(selector, index));
        }

        public static bool TryApplyByName(TargetListSelector selector, string name)
        {
            if (!Ready(selector) || string.IsNullOrEmpty(name)) return false;
            int custom = library.IndexOf(name);
            if (custom >= 0)
            {
                Apply(selector, library.At(custom));
                return true;
            }
            for (int i = 0; i < BuiltInCount; i++)
            {
                if (!string.Equals(MfdTargetPresets.Names[i], name, StringComparison.Ordinal)) continue;
                Apply(selector, BuildBuiltIn(selector, (MfdTargetPreset)i));
                return true;
            }
            return false;
        }

        public static void Apply(TargetListSelector selector, TargetPresetSnapshot preset)
        {
            if (!Ready(selector) || preset == null) return;

            // HUD linking owns the filters: unlink first so its ResetFilters cannot
            // overwrite the preset we are about to set.
            if (selector.toggleFollowHUD.status) selector.toggleFollowHUD.Set(false);
            ApplyFaction(selector, preset);
            ApplyEntries(selector.toggleUnitTypesItems, preset.Classes);
            ApplyEntries(selector.toggleVehicleTypesItems, preset.Vehicles);
            selector.toggleLaser.Set(preset.Laser);
            selector.NeedUpdateIcons();
        }

        private static void ApplyFaction(TargetListSelector selector, TargetPresetSnapshot preset)
        {
            for (int i = 0; i < selector.toggleFactionItems.Count; i++)
            {
                TargetListSelector_ToggleButton entry = selector.toggleFactionItems[i];
                if (entry != null) entry.Set(TargetPresetRules.FactionExpected(preset, entry.sameFaction));
            }
        }

        private static void ApplyEntries(List<TargetListSelector_ToggleButton> entries,
                                          IReadOnlyList<string> presetTokens)
        {
            if (entries == null) return;
            for (int i = 0; i < entries.Count; i++)
            {
                TargetListSelector_ToggleButton entry = entries[i];
                if (entry == null) continue;
                entry.Set(TargetPresetRules.Includes(EntryTokens(entry), presetTokens));
            }
        }

        // ------------------------------------------------------------------- active

        public static string ActiveName(TargetListSelector selector)
        {
            if (!Ready(selector)) return TargetPresetLibrary.CustomProfile;
            TargetPresetState state = CaptureState(selector);
            for (int i = 0; i < BuiltInCount; i++)
            {
                if (TargetPresetRules.Matches(BuildBuiltIn(selector, (MfdTargetPreset)i), state))
                    return MfdTargetPresets.Names[i];
            }
            for (int i = 0; i < library.Count; i++)
            {
                if (TargetPresetRules.Matches(library.At(i), state)) return library.At(i).Name;
            }
            return TargetPresetLibrary.CustomProfile;
        }

        public static TargetPresetState CaptureState(TargetListSelector selector)
        {
            var state = new TargetPresetState
            {
                FollowHud = selector.toggleFollowHUD.status,
                Laser = selector.toggleLaser.status,
                Friendly = FactionStatus(selector, true),
                Hostile = FactionStatus(selector, false),
            };

            state.ClassTokens = new string[selector.toggleUnitTypesItems.Count][];
            state.ClassEnabled = new bool[selector.toggleUnitTypesItems.Count];
            for (int i = 0; i < selector.toggleUnitTypesItems.Count; i++)
            {
                state.ClassTokens[i] = EntryTokens(selector.toggleUnitTypesItems[i]);
                state.ClassEnabled[i] = selector.toggleUnitTypesItems[i] != null &&
                                        selector.toggleUnitTypesItems[i].status;
            }

            state.VehicleTokens = new string[selector.toggleVehicleTypesItems.Count][];
            state.VehicleEnabled = new bool[selector.toggleVehicleTypesItems.Count];
            for (int i = 0; i < selector.toggleVehicleTypesItems.Count; i++)
            {
                state.VehicleTokens[i] = EntryTokens(selector.toggleVehicleTypesItems[i]);
                state.VehicleEnabled[i] = selector.toggleVehicleTypesItems[i] != null &&
                                          selector.toggleVehicleTypesItems[i].status;
            }
            return state;
        }

        private static bool FactionStatus(TargetListSelector selector, bool sameFaction)
        {
            for (int i = 0; i < selector.toggleFactionItems.Count; i++)
            {
                TargetListSelector_ToggleButton entry = selector.toggleFactionItems[i];
                if (entry != null && entry.sameFaction == sameFaction) return entry.status;
            }
            return false;
        }

        // ------------------------------------------------------------------ capture

        public static TargetPresetSaveResult SaveCurrent(TargetListSelector selector, string name, bool overwrite)
        {
            if (!Ready(selector)) return TargetPresetSaveResult.NotReady;
            TargetPresetSaveResult result = library.Save(Capture(selector, name), overwrite);
            if (result == TargetPresetSaveResult.Ok) Persist();
            return result;
        }

        private static TargetPresetSnapshot Capture(TargetListSelector selector, string name)
        {
            var preset = new TargetPresetSnapshot
            {
                Name = name,
                Laser = selector.toggleLaser.status,
                Friendly = FactionStatus(selector, true),
                Hostile = FactionStatus(selector, false),
            };
            AddEnabledTokens(preset.Classes, selector.toggleUnitTypesItems);
            AddEnabledTokens(preset.Vehicles, selector.toggleVehicleTypesItems);
            TargetPresetRules.Normalise(preset);
            return preset;
        }

        private static void AddEnabledTokens(List<string> target,
                                             List<TargetListSelector_ToggleButton> entries)
        {
            if (entries == null) return;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null || !entries[i].status) continue;
                string[] tokens = EntryTokens(entries[i]);
                for (int t = 0; t < tokens.Length; t++) target.Add(tokens[t]);
            }
        }

        public static string SuggestName()
        {
            for (int i = 1; i <= 99; i++)
            {
                string candidate = "PRESET " + i;
                if (!TargetPresetRules.IsReservedName(candidate) && library.IndexOf(candidate) < 0)
                    return candidate;
            }
            return "PRESET";
        }

        public static bool Rename(string from, string to)
        {
            if (!library.Rename(from, to)) return false;
            Persist();
            return true;
        }

        public static bool Delete(string name)
        {
            if (!library.Delete(name)) return false;
            Persist();
            return true;
        }

        // --------------------------------------------------------------- quick slots

        public static string QuickSlotName(int slot) => library.SlotName(slot);

        /// <summary>The bound shortcut for a quick slot, or None when the module is not configured.</summary>
        public static KeyCode Key(int slot)
        {
            if (settings == null) return KeyCode.None;
            switch (slot)
            {
                case 0: return settings.TargetPresetKey1.Value;
                case 1: return settings.TargetPresetKey2.Value;
                case 2: return settings.TargetPresetKey3.Value;
                default: return KeyCode.None;
            }
        }

        public static bool AssignQuickSlot(string name, int slot)
        {
            if (!CatalogContains(name) || !library.Assign(name, slot)) return false;
            Persist();
            return true;
        }

        /// <summary>Right-click on a library row: first empty slot, else report failure.</summary>
        public static bool AssignToFirstFreeSlot(string name)
        {
            if (!CatalogContains(name)) return false;
            for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                if (library.SlotName(slot).Length == 0) return AssignQuickSlot(name, slot);
            return false;
        }

        public static bool ClearQuickSlot(int slot)
        {
            if (library.SlotName(slot).Length == 0) return false;
            library.ClearSlot(slot);
            Persist();
            return true;
        }

        // ------------------------------------------------------------------ internals

        private static TargetPresetSnapshot BuildBuiltIn(TargetListSelector selector, MfdTargetPreset kind)
        {
            var preset = new TargetPresetSnapshot
            {
                Name = MfdTargetPresets.Names[(int)kind],
                Laser = kind == MfdTargetPreset.Laser,
                Friendly = MfdTargetPresets.Faction(kind, true),
                Hostile = MfdTargetPresets.Faction(kind, false),
            };

            if (Ready(selector))
            {
                for (int i = 0; i < selector.toggleUnitTypesItems.Count; i++)
                {
                    TargetListSelector_ToggleButton entry = selector.toggleUnitTypesItems[i];
                    if (entry == null) continue;
                    string[] tokens = EntryTokens(entry);
                    for (int t = 0; t < tokens.Length; t++)
                        if (MfdTargetPresets.UnitClass(kind, tokens[t])) preset.Classes.Add(tokens[t]);
                }
                for (int i = 0; i < selector.toggleVehicleTypesItems.Count; i++)
                {
                    TargetListSelector_ToggleButton entry = selector.toggleVehicleTypesItems[i];
                    if (entry == null) continue;
                    string[] tokens = EntryTokens(entry);
                    for (int t = 0; t < tokens.Length; t++)
                        if (MfdTargetPresets.Vehicle(kind, tokens[t])) preset.Vehicles.Add(tokens[t]);
                }
            }

            TargetPresetRules.Normalise(preset);
            return preset;
        }

        /// <summary>Stable identities for an entry: unit-type names and vehicle-type names.</summary>
        private static string[] EntryTokens(TargetListSelector_ToggleButton entry)
        {
            if (entry == null) return new string[0];
            var tokens = new List<string>(4);
            if (entry.listUnitTypes != null)
            {
                for (int i = 0; i < entry.listUnitTypes.Count; i++)
                    if (entry.listUnitTypes[i] != null) tokens.Add(entry.listUnitTypes[i].GetType().Name);
            }
            if (entry.listDefinitions != null)
            {
                for (int i = 0; i < entry.listDefinitions.Count; i++)
                {
                    UnitDefinition definition = entry.listDefinitions[i];
                    if (definition is VehicleDefinition vehicle) tokens.Add(vehicle.vehicleType.ToString());
                    else if (definition != null) tokens.Add(definition.GetType().Name);
                }
            }
            return tokens.ToArray();
        }

        private static void Persist()
        {
            version++;
            if (settings == null) return;
            settings.TargetPresets.Value = library.Encode();
            settings.TargetPresetSlots.Value = library.EncodeSlots();
        }
    }
}
