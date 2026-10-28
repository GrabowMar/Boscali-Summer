using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
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

    /// <summary>
    /// One client-local SET control. The owning feature keeps its config entry; the section names
    /// the group, and SET picks the tab from it. A row that waits on a switch (<see cref="Requires"/>)
    /// is locked, with the reason, while that switch or one it waits on is off.
    /// </summary>
    internal abstract class ClientSetting
    {
        protected ClientSetting(string section, string label, string help, ConfigEntryBase entry, ClientSettingToggle requires)
        {
            Section = section;
            Label = label;
            Help = help;
            Entry = entry;
            Requires = requires;
        }

        public string Section { get; }
        public string Label { get; }
        public string Help { get; }
        public ConfigEntryBase Entry { get; }
        public ClientSettingToggle Requires { get; }
        public bool Available => Requires == null || Requires.Value && Requires.Available;
        public string Reason => Requires == null ? Help : !Requires.Available ? Requires.Reason : "Turn on " + Requires.Label + " first.";
        public void Reset() => Entry.BoxedValue = Entry.DefaultValue;
    }

    internal sealed class ClientSettingToggle : ClientSetting
    {
        private readonly ConfigEntry<bool> entry;

        internal ClientSettingToggle(string section, string label, string help, ConfigEntry<bool> entry,
            bool[] presets, ClientSettingToggle requires) : base(section, label, help, entry, requires)
        {
            Presets = presets;
            this.entry = entry;
        }

        public bool[] Presets { get; }
        public bool Value => entry.Value;
        public void Set(bool value) => entry.Value = value;
    }

    /// <summary>A level dial: <see cref="Step"/> per click inside <see cref="Min"/>..<see cref="Max"/>, printed by <see cref="Text"/> (a percentage unless given).</summary>
    internal sealed class ClientSettingLevel : ClientSetting
    {
        private readonly ConfigEntry<float> entry;
        private readonly Func<float, string> text;

        internal ClientSettingLevel(string section, string label, string help, ConfigEntry<float> entry, float min, float max,
            float step, Func<float, string> text, ClientSettingToggle requires) : base(section, label, help, entry, requires)
        {
            this.entry = entry;
            this.text = text ?? (v => AvNum.Percent(v));
            Min = min;
            Max = max;
            Step = step;
        }

        public float Min { get; }
        public float Max { get; }
        public float Step { get; }
        public float Level01 => Mathf.Clamp01((entry.Value - Min) / (Max - Min));
        public string Text => text(entry.Value);
        public bool CanStep(int d) => d < 0 ? entry.Value > Min + .001f : entry.Value < Max - .001f;
        public void Nudge(int d) => entry.Value = Mathf.Clamp(Mathf.Round((entry.Value + d * Step) * 100f) / 100f, Min, Max);
    }

    /// <summary>One of an enum's values, as a segment strip; <see cref="Names"/> are in the enum's declared order.</summary>
    internal sealed class ClientSettingChoice : ClientSetting
    {
        private readonly Array values;

        internal ClientSettingChoice(string section, string label, string help, ConfigEntryBase entry, string[] names,
            ClientSettingToggle requires) : base(section, label, help, entry, requires)
        {
            values = Enum.GetValues(entry.SettingType);
            Names = names;
        }

        public string[] Names { get; }
        public int Index => Array.IndexOf(values, Entry.BoxedValue);
        public void Select(int i) => Entry.BoxedValue = values.GetValue(i);
    }

    /// <summary>A client-local key the owning feature reads live; SET and the mission prompt rebind it in place.</summary>
    internal sealed class ClientKeySetting
    {
        internal ClientKeySetting(string label, string help, ConfigEntry<KeyCode> entry, bool onPrompt)
        {
            Label = label;
            Help = help;
            Entry = entry;
            OnPrompt = onPrompt;
        }

        public string Label { get; }
        public string Help { get; }
        public ConfigEntry<KeyCode> Entry { get; }
        /// <summary>Also offered on the mission prompt's CONTROLS card; every key is on SET.</summary>
        public bool OnPrompt { get; }
    }

    internal sealed class ClientSettingsBoard
    {
        private readonly List<ClientSetting> rows = new List<ClientSetting>();
        private readonly List<ClientKeySetting> keys = new List<ClientKeySetting>();

        public IReadOnlyList<ClientSetting> Rows => rows;
        public IReadOnlyList<ClientKeySetting> Keys => keys;

        public ClientKeySetting AddKey(string label, string help, ConfigEntry<KeyCode> entry, bool onPrompt = true)
        {
            var key = new ClientKeySetting(label, help, entry, onPrompt);
            keys.Add(key);
            return key;
        }

        public void Remove(ClientKeySetting key) => keys.Remove(key);

        public T Add<T>(T row) where T : ClientSetting
        {
            rows.Add(row);
            return row;
        }

        public void Remove(ClientSetting row) => rows.Remove(row);

        public void ApplyPreset(int preset)
        {
            foreach (ClientSetting row in rows)
                if (row is ClientSettingToggle t && t.Presets != null && t.Value != t.Presets[preset]) t.Set(t.Presets[preset]);
        }

        public bool MatchesPreset(int preset)
        {
            foreach (ClientSetting row in rows)
                if (row is ClientSettingToggle t && t.Presets != null && t.Value != t.Presets[preset]) return false;
            return true;
        }
    }
}
