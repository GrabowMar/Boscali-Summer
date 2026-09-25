using System;
using System.Collections.Generic;
using System.Text;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// A captured target-filter state: one faction toggle per side, the enabled unit
    /// classes, the enabled platform types, and the laser toggle. Tokens are stable
    /// definition identities (unit-type names and vehicle-type names), so a preset
    /// survives a changed catalog by ignoring entries it no longer recognises.
    /// </summary>
    internal sealed class TargetPresetSnapshot
    {
        public string Name = "";
        public bool Laser;
        public bool Friendly;
        public bool Hostile;
        public readonly List<string> Classes = new List<string>();
        public readonly List<string> Vehicles = new List<string>();

        public TargetPresetSnapshot Copy(string name)
        {
            var copy = new TargetPresetSnapshot
            {
                Name = name ?? Name,
                Laser = Laser,
                Friendly = Friendly,
                Hostile = Hostile,
            };
            copy.Classes.AddRange(Classes);
            copy.Vehicles.AddRange(Vehicles);
            return copy;
        }
    }

    internal enum TargetPresetSaveResult { Ok, EmptyName, ReservedName, NameInUse, LibraryFull, NotReady }

    /// <summary>
    /// The player's saved presets and their three quick slots. Pure and bounded: every
    /// list has a hard ceiling, names are sanitised to a separator-free alphabet, and no
    /// method throws on malformed stored text.
    /// </summary>
    internal sealed class TargetPresetLibrary
    {
        public const int SlotCount = 3;
        public const int MaxCustomPresets = 12;
        public const int MaxNameLength = 14;
        public const int MaxTokensPerPreset = 48;
        public const int MaxStoredLength = 32768;
        public const string CustomProfile = "CUSTOM";

        private readonly List<TargetPresetSnapshot> customs = new List<TargetPresetSnapshot>();
        private readonly string[] slots = new string[SlotCount];

        public TargetPresetLibrary()
        {
            for (int i = 0; i < SlotCount; i++) slots[i] = "";
        }

        public int Count => customs.Count;

        public string SlotName(int slot) =>
            slot >= 0 && slot < SlotCount ? slots[slot] : "";

        public int IndexOf(string name)
        {
            for (int i = 0; i < customs.Count; i++)
                if (string.Equals(customs[i].Name, name, StringComparison.Ordinal)) return i;
            return -1;
        }

        public TargetPresetSnapshot At(int index) =>
            index >= 0 && index < customs.Count ? customs[index] : null;

        public TargetPresetSaveResult Save(TargetPresetSnapshot snapshot, bool overwrite)
        {
            if (snapshot == null) return TargetPresetSaveResult.EmptyName;
            string name = TargetPresetRules.SanitiseName(snapshot.Name);
            if (name.Length == 0) return TargetPresetSaveResult.EmptyName;
            if (TargetPresetRules.IsReservedName(name)) return TargetPresetSaveResult.ReservedName;

            int existing = IndexOf(name);
            if (existing >= 0 && !overwrite) return TargetPresetSaveResult.NameInUse;
            if (existing < 0 && customs.Count >= MaxCustomPresets) return TargetPresetSaveResult.LibraryFull;

            TargetPresetSnapshot stored = snapshot.Copy(name);
            TargetPresetRules.Normalise(stored);
            if (existing >= 0) customs[existing] = stored;
            else customs.Add(stored);
            return TargetPresetSaveResult.Ok;
        }

        public bool Rename(string from, string to)
        {
            int index = IndexOf(from);
            if (index < 0) return false;
            string name = TargetPresetRules.SanitiseName(to);
            if (name.Length == 0 || TargetPresetRules.IsReservedName(name)) return false;
            int clash = IndexOf(name);
            if (clash >= 0 && clash != index) return false;

            customs[index].Name = name;
            for (int slot = 0; slot < SlotCount; slot++)
                if (string.Equals(slots[slot], from, StringComparison.Ordinal)) slots[slot] = name;
            return true;
        }

        public bool Delete(string name)
        {
            int index = IndexOf(name);
            if (index < 0) return false;
            customs.RemoveAt(index);
            for (int slot = 0; slot < SlotCount; slot++)
                if (string.Equals(slots[slot], name, StringComparison.Ordinal)) slots[slot] = "";
            return true;
        }

        /// <summary>Assign a catalog name to a slot, moving it out of any other slot.</summary>
        public bool Assign(string name, int slot)
        {
            if (slot < 0 || slot >= SlotCount) return false;
            string clean = TargetPresetRules.SanitiseName(name);
            if (clean.Length == 0) return false;
            for (int i = 0; i < SlotCount; i++)
                if (string.Equals(slots[i], clean, StringComparison.Ordinal)) slots[i] = "";
            slots[slot] = clean;
            return true;
        }

        public void ClearSlot(int slot)
        {
            if (slot >= 0 && slot < SlotCount) slots[slot] = "";
        }

        public string Encode()
        {
            var text = new StringBuilder();
            text.Append(TargetPresetRules.FormatVersion);
            for (int i = 0; i < customs.Count; i++)
                text.Append('|').Append(TargetPresetRules.EncodePreset(customs[i]));
            return text.ToString();
        }

        public string EncodeSlots()
        {
            var text = new StringBuilder();
            for (int i = 0; i < SlotCount; i++)
            {
                if (i > 0) text.Append('|');
                text.Append(slots[i]);
            }
            return text.ToString();
        }

        public static TargetPresetLibrary Decode(string encoded, string encodedSlots)
        {
            var library = new TargetPresetLibrary();
            library.DecodePresets(encoded);
            library.DecodeSlots(encodedSlots);
            return library;
        }

        private void DecodePresets(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) || encoded.Length > MaxStoredLength) return;
            string[] parts = encoded.Split('|');
            if (parts.Length == 0 || parts[0] != TargetPresetRules.FormatVersion) return;

            for (int i = 1; i < parts.Length && customs.Count < MaxCustomPresets; i++)
            {
                TargetPresetSnapshot preset = TargetPresetRules.DecodePreset(parts[i]);
                if (preset == null) continue;
                if (IndexOf(preset.Name) >= 0) continue;
                customs.Add(preset);
            }
        }

        private void DecodeSlots(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) || encoded.Length > MaxStoredLength) return;
            string[] parts = encoded.Split('|');
            for (int i = 0; i < SlotCount && i < parts.Length; i++)
            {
                string name = TargetPresetRules.SanitiseName(parts[i]);
                if (name.Length == 0 || TargetPresetRules.IsReservedName(name)) continue;
                if (AlreadySlotted(name, i)) continue;
                slots[i] = name;
            }
        }

        private bool AlreadySlotted(string name, int before)
        {
            for (int i = 0; i < before; i++)
                if (string.Equals(slots[i], name, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// <summary>Current filter state, as tokens per entry, so pure matching cannot drift
    /// from the rebuilt panel's entry loop.</summary>
    internal sealed class TargetPresetState
    {
        public bool FollowHud;
        public bool Laser;
        public bool Friendly;
        public bool Hostile;
        public string[][] ClassTokens = new string[0][];
        public bool[] ClassEnabled = new bool[0];
        public string[][] VehicleTokens = new string[0][];
        public bool[] VehicleEnabled = new bool[0];
    }

    /// <summary>Pure preset policy: identity, matching, name hygiene, and the stored form.</summary>
    internal static class TargetPresetRules
    {
        public const string FormatVersion = "1";

        private static readonly string[] ReservedNames =
        {
            "ALL", "HOSTILE", "AIR", "GROUND", "SEA", "SEAD", "FRIENDLY", "LASER",
            TargetPresetLibrary.CustomProfile,
        };

        /// <summary>Uppercase, separator-free, 14 characters at most; empty when unlike a name.</summary>
        public static string SanitiseName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var text = new StringBuilder(TargetPresetLibrary.MaxNameLength);
            bool separator = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = char.ToUpperInvariant(raw[i]);
                bool allowed = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                               c == '-' || c == '_' || c == '.';
                if (allowed)
                {
                    if (separator && text.Length > 0 && text.Length < TargetPresetLibrary.MaxNameLength)
                        text.Append(' ');
                    separator = false;
                    if (text.Length < TargetPresetLibrary.MaxNameLength) text.Append(c);
                }
                else
                {
                    separator = true;
                }
            }
            return text.ToString().TrimEnd();
        }

        public static bool IsReservedName(string name)
        {
            for (int i = 0; i < ReservedNames.Length; i++)
                if (string.Equals(ReservedNames[i], name, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Drop unsafe/duplicate tokens and cap the list; names and tokens share no separator.</summary>
        public static void Normalise(TargetPresetSnapshot preset)
        {
            CleanTokens(preset.Classes);
            CleanTokens(preset.Vehicles);
        }

        private static void CleanTokens(List<string> tokens)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                string token = SanitiseToken(tokens[i]);
                if (token.Length == 0 || !seen.Add(token)) tokens.RemoveAt(i);
                else tokens[i] = token;
            }
            if (tokens.Count > TargetPresetLibrary.MaxTokensPerPreset)
                tokens.RemoveRange(TargetPresetLibrary.MaxTokensPerPreset,
                    tokens.Count - TargetPresetLibrary.MaxTokensPerPreset);
        }

        public static string SanitiseToken(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var text = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length && text.Length < 48; i++)
            {
                char c = raw[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c == '_') text.Append(c);
            }
            return text.ToString();
        }

        public static string JoinTokens(List<string> tokens) =>
            tokens == null ? "" : string.Join("+", tokens.ToArray());

        /// <summary>An entry is enabled when any of its definitions is in the preset.</summary>
        public static bool Includes(IReadOnlyList<string> entryTokens, IReadOnlyList<string> presetTokens)
        {
            if (entryTokens == null || presetTokens == null) return false;
            for (int i = 0; i < entryTokens.Count; i++)
                for (int j = 0; j < presetTokens.Count; j++)
                    if (string.Equals(entryTokens[i], presetTokens[j], StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool FactionExpected(TargetPresetSnapshot preset, bool sameFaction) =>
            sameFaction ? preset.Friendly : preset.Hostile;

        /// <summary>Entry-by-entry comparison; HUD linking owns the filters and never matches.</summary>
        public static bool Matches(TargetPresetSnapshot preset, TargetPresetState state)
        {
            if (preset == null || state == null || state.FollowHud) return false;
            if (state.Laser != preset.Laser || state.Friendly != preset.Friendly ||
                state.Hostile != preset.Hostile) return false;

            int classes = Math.Min(state.ClassEnabled.Length, state.ClassTokens.Length);
            for (int i = 0; i < classes; i++)
                if (state.ClassEnabled[i] != Includes(state.ClassTokens[i], preset.Classes)) return false;

            int vehicles = Math.Min(state.VehicleEnabled.Length, state.VehicleTokens.Length);
            for (int i = 0; i < vehicles; i++)
                if (state.VehicleEnabled[i] != Includes(state.VehicleTokens[i], preset.Vehicles)) return false;

            return true;
        }

        private static readonly string[] ClassOrder = { "AIR", "GND", "BLD", "SHP", "MSL" };

        public static string ClassLabel(string typeName)
        {
            switch (typeName)
            {
                case "AircraftDefinition": return "AIR";
                case "VehicleDefinition": return "GND";
                case "BuildingDefinition": return "BLD";
                case "ShipDefinition": return "SHP";
                case "MissileDefinition": return "MSL";
                default: return SanitiseToken(typeName);
            }
        }

        /// <summary>One-line filter description for the library: factions, classes, platforms, laser.</summary>
        public static string Summary(TargetPresetSnapshot preset)
        {
            if (preset == null) return "NO PRESET";
            var text = new StringBuilder(64);

            if (preset.Friendly && preset.Hostile) text.Append("ALL FACTIONS");
            else if (preset.Friendly) text.Append("FRIENDLY");
            else if (preset.Hostile) text.Append("ENEMY");
            else text.Append("NO FACTION");

            var classes = new List<string>();
            for (int i = 0; i < preset.Classes.Count; i++)
            {
                string label = ClassLabel(preset.Classes[i]);
                if (label.Length > 0 && !classes.Contains(label)) classes.Add(label);
            }
            classes.Sort((a, b) => Array.IndexOf(ClassOrder, a).CompareTo(Array.IndexOf(ClassOrder, b)));
            text.Append(" · ").Append(classes.Count == 0 ? "NO CLASS" : string.Join("/", classes.ToArray()));

            if (preset.Vehicles.Count > 0)
            {
                int shown = Math.Min(3, preset.Vehicles.Count);
                text.Append(" · ").Append(preset.Vehicles.Count).Append(" PLATFORMS (");
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0) text.Append(' ');
                    text.Append(preset.Vehicles[i]);
                }
                if (preset.Vehicles.Count > shown) text.Append(" +");
                text.Append(')');
            }
            if (preset.Laser) text.Append(" · LASER");
            return text.ToString();
        }

        /// <summary>The stored one-preset form: name;laser;friendly;hostile;classes;vehicles.</summary>
        public static string EncodePreset(TargetPresetSnapshot preset) =>
            preset.Name + ";" + (preset.Laser ? 1 : 0) + ";" + (preset.Friendly ? 1 : 0) + ";" +
            (preset.Hostile ? 1 : 0) + ";" + JoinTokens(preset.Classes) + ";" + JoinTokens(preset.Vehicles);

        public static TargetPresetSnapshot DecodePreset(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] fields = text.Split(';');
            if (fields.Length != 6) return null;

            string name = SanitiseName(fields[0]);
            if (name.Length == 0 || IsReservedName(name)) return null;

            var preset = new TargetPresetSnapshot
            {
                Name = name,
                Laser = fields[1] == "1",
                Friendly = fields[2] == "1",
                Hostile = fields[3] == "1",
            };
            AddTokens(preset.Classes, fields[4]);
            AddTokens(preset.Vehicles, fields[5]);
            Normalise(preset);
            return preset;
        }

        private static void AddTokens(List<string> target, string field)
        {
            if (string.IsNullOrEmpty(field)) return;
            string[] tokens = field.Split('+');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = SanitiseToken(tokens[i]);
                if (token.Length > 0) target.Add(token);
            }
        }
    }
}
