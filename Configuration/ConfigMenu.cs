using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer
{
    /// <summary>
    /// What the F1 plugin-settings window shows before anyone reaches for its "Advanced
    /// settings" filter: one MODULES group of master switches, and nothing else.
    ///
    /// <para>The mod binds around a hundred and sixty entries. A window that opens on all of
    /// them is not a settings screen, it is a haystack, so everything that is not a master
    /// switch is marked advanced. Nothing is removed: every tuning value keeps its config
    /// file entry, its section, its description and its bounds, and the window still lists it
    /// behind the one checkbox that was made for exactly this. The values a host changes
    /// while a mission runs have their own home on the SET console's SERVER page; the master
    /// switches do not, because most of them are read once at startup and a module that was
    /// never installed cannot be switched on from a cockpit.</para>
    ///
    /// <para>ConfigurationManager reads its per-entry display hints from tag objects hanging
    /// off the entry's description, and the modules bind through the plain string overload,
    /// which carries no tags. The hints are therefore attached here, once, after every module
    /// has bound: each entry's description is replaced by an identical one that also carries a
    /// hint. Nothing but that window reads those tags, so if a future BepInEx renames the
    /// field this reaches for, the window simply looks the way it did before.</para>
    /// </summary>
    internal static class ConfigMenu
    {
        /// <summary>ConfigurationManager reads these public fields by name; no plugin assembly dependency.</summary>
        private sealed class ConfigurationManagerAttributes
        {
            public bool? IsAdvanced;
            public string Category;
            public string DispName;
            public int? Order;
        }

        /// <summary>The group the master switches are listed under, ahead of their own sections.</summary>
        private const string ModuleCategory = "Modules";

        /// <summary>
        /// Every part of the mod that can be run without, in the order the window should list
        /// them: what a mission is built out of first, then what decorates it. The entries are
        /// named through the settings objects rather than looked up by config section, so a
        /// module that renames its section cannot quietly drop its own switch out of the group.
        ///
        /// <para>Each switch is given a display name because the key behind nearly all of them
        /// is "Enabled", which says nothing once they are gathered into one list. Whether a
        /// change takes effect now or at the next launch is the entry's own description to
        /// explain, and it already does.</para>
        /// </summary>
        private static Dictionary<ConfigEntryBase, ConfigurationManagerAttributes> Switches(
            ModConfiguration settings)
        {
            ConfigEntryBase[] ordered =
            {
                settings.Progression.Enabled,
                settings.Support.Enabled,
                settings.Command.Enabled,
                settings.HighCommand.Enabled,
                settings.TheaterOps.Enabled,
                settings.DynamicOperations.Enabled,
                settings.Events.Enabled,
                settings.Comms.Enabled,
                settings.Trenches.Enabled,
                settings.UrbanCombat.GarrisonsEnabled,
                settings.FireAndDestruction.FiresEnabled,
                settings.Radio.Enabled,
                settings.Hud.Enabled,
                settings.Autopilot.Enabled,
                settings.QoL.Enabled,
                settings.Campaign.Enabled,
                settings.Weather.Enabled,
                settings.Visuals.Enabled,
                settings.Immersion.Enabled
            };
            string[] names =
            {
                "Perk board, squad and aces",
                "Support call-ins and orbital platforms",
                "Command map console",
                "Chain of command",
                "Theater priority",
                "Dynamic contracts",
                "World events",
                "Map comms, polls and games",
                "Trenches and fortifications",
                "Zone garrisons",
                "Fire ignition and spread",
                "Cockpit radio",
                "Common HUD element",
                "Autopilot",
                "Quality of life",
                "Campaign mission install",
                "Dynamic weather and ENV screen",
                "Visual enhancements and post-processing",
                "Cockpit feel: head motion, shake, sun glare"
            };

            var hints = new Dictionary<ConfigEntryBase, ConfigurationManagerAttributes>(ordered.Length);
            for (int i = 0; i < ordered.Length; i++)
            {
                hints[ordered[i]] = new ConfigurationManagerAttributes
                {
                    IsAdvanced = false,
                    Category = ModuleCategory,
                    DispName = names[i],
                    // Higher sorts higher, so the declared order is the printed order.
                    Order = ordered.Length - i
                };
            }
            return hints;
        }

        /// <summary>
        /// Marks everything the window should not open on. Call once, after every module has
        /// bound its settings.
        /// </summary>
        public static void Apply(ConfigFile config, ModConfiguration settings)
        {
            FieldInfo description = typeof(ConfigEntryBase)
                .GetField("<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (description == null) return;

            Dictionary<ConfigEntryBase, ConfigurationManagerAttributes> switches = Switches(settings);
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> bound in config)
            {
                ConfigEntryBase entry = bound.Value;
                if (entry == null) continue;

                // Keybinds live under the window's own "Keyboard shortcuts" filter, which an
                // advanced entry is dropped from as well. They are already a short list, so
                // they are left alone rather than hidden behind a checkbox that does not name
                // them.
                if (entry.SettingType == typeof(KeyCode)) continue;

                ConfigurationManagerAttributes hint;
                if (!switches.TryGetValue(entry, out hint))
                {
                    hint = new ConfigurationManagerAttributes { IsAdvanced = true };
                }

                Tag(description, entry, hint);
            }
        }

        /// <summary>
        /// Hangs one hint off an entry without disturbing what the config file records: the
        /// description text and the acceptable range are carried over verbatim, and any tag a
        /// module attached itself is kept ahead of the new one, where the window's later-wins
        /// merge leaves it in force for every field this hint does not set.
        /// </summary>
        private static void Tag(FieldInfo field, ConfigEntryBase entry, ConfigurationManagerAttributes hint)
        {
            ConfigDescription current = entry.Description;
            object[] existing = current?.Tags ?? Array.Empty<object>();
            var tags = new object[existing.Length + 1];
            Array.Copy(existing, tags, existing.Length);
            tags[existing.Length] = hint;

            field.SetValue(entry, new ConfigDescription(
                current?.Description ?? string.Empty, current?.AcceptableValues, tags));
        }
    }
}
