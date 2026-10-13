using System.Collections.Generic;
using BepInEx.Configuration;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>Client-local SET toggles. The owning feature keeps its config entry.</summary>
    /// <summary>
    /// What a toggle is set to by each SET PERF preset, in the order of <see cref="Names"/>.
    /// The owning feature picks one; a toggle without one (sound, comfort, masters, the frame
    /// limit) is never touched by a preset.
    /// </summary>
    internal static class ClientPreset
    {
        public static readonly string[] Names = { "QUALITY", "BALANCED", "FAST" };

        /// <summary>A cheap cosmetic: dropped only by FAST.</summary>
        public static readonly bool[] Light = { true, true, false };
        /// <summary>A cosmetic with real render cost: kept only by QUALITY.</summary>
        public static readonly bool[] Heavy = { true, false, false };
        /// <summary>A saving that is hard to see: on from BALANCED down.</summary>
        public static readonly bool[] Saver = { false, true, true };
        /// <summary>A saving that visibly cuts the game's own picture: FAST only.</summary>
        public static readonly bool[] LastResort = { false, false, true };
    }

    internal sealed class ClientSettingToggle
    {
        private readonly ConfigEntry<bool> entry;

        internal ClientSettingToggle(string section, string label, string help, ConfigEntry<bool> entry,
            bool[] presets = null)
        {
            Section = section;
            Label = label;
            Help = help;
            Presets = presets;
            this.entry = entry;
        }

        public string Section { get; }
        public string Label { get; }
        public string Help { get; }
        public bool[] Presets { get; }
        public bool Value => entry.Value;
        public void Set(bool value) => entry.Value = value;
    }

    internal sealed class ClientSettingsBoard
    {
        private readonly List<ClientSettingToggle> rows = new List<ClientSettingToggle>();

        public IReadOnlyList<ClientSettingToggle> Rows => rows;

        public ClientSettingToggle Add(string section, string label, string help, ConfigEntry<bool> entry,
            bool[] presets = null)
        {
            var row = new ClientSettingToggle(section, label, help, entry, presets);
            rows.Add(row);
            return row;
        }

        public void Remove(ClientSettingToggle row) => rows.Remove(row);

        public void ApplyPreset(int preset)
        {
            foreach (ClientSettingToggle row in rows)
                if (row.Presets != null && row.Value != row.Presets[preset]) row.Set(row.Presets[preset]);
        }

        public bool MatchesPreset(int preset)
        {
            foreach (ClientSettingToggle row in rows)
                if (row.Presets != null && row.Value != row.Presets[preset]) return false;
            return true;
        }
    }
}
