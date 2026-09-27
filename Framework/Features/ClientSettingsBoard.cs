using System.Collections.Generic;
using BepInEx.Configuration;

namespace BoscaliSummer.Framework.Features
{
    /// <summary>Client-local SET toggles. The owning feature keeps its config entry.</summary>
    internal sealed class ClientSettingToggle
    {
        private readonly ConfigEntry<bool> entry;

        internal ClientSettingToggle(string section, string label, string help, ConfigEntry<bool> entry)
        {
            Section = section;
            Label = label;
            Help = help;
            this.entry = entry;
        }

        public string Section { get; }
        public string Label { get; }
        public string Help { get; }
        public bool Value => entry.Value;
        public void Set(bool value) => entry.Value = value;
    }

    internal sealed class ClientSettingsBoard
    {
        private readonly List<ClientSettingToggle> rows = new List<ClientSettingToggle>();

        public IReadOnlyList<ClientSettingToggle> Rows => rows;

        public ClientSettingToggle Add(string section, string label, string help, ConfigEntry<bool> entry)
        {
            var row = new ClientSettingToggle(section, label, help, entry);
            rows.Add(row);
            return row;
        }

        public void Remove(ClientSettingToggle row) => rows.Remove(row);
    }
}
