using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Core.Config
{
    /// <summary>ConfigurationManager reads these public fields by name; no plugin assembly dependency.</summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? IsAdvanced;
        public string Category;
        public string DispName;
        public int? Order;
        public bool? ReadOnly;
        public bool? Browsable;
        /// <summary>Custom IMGUI value drawer; ConfigurationManager looks this exact delegate type up by reflection.</summary>
        public Action<ConfigEntryBase> CustomDrawer;
        public bool? HideDefaultButton;
    }

    /// <summary>
    /// What the F1 plugin-settings window lists: the short table of switches below, the
    /// keybinds in one group, and nothing else. Look, comfort and cockpit-feel settings are on
    /// the SET console, which applies them in flight; F1 keeps the systems' on/off switches.
    ///
    /// <para>The mod binds around five hundred entries. Nearly all of them are tuning values
    /// that are only safe to move by someone who knows what they feed, so they are not in the
    /// window at all, whatever its "Advanced settings" filter says. Nothing is removed: every
    /// value keeps its config file entry, its section, its description and its bounds, and the
    /// config file is where they are edited. Setting <c>[Meta] ExpertSettings</c> in that file
    /// brings them back into the window's advanced view from the next launch. The values a host
    /// changes while a mission runs have their own home on the SET console's SERVER page.</para>
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
        private const string KeysCategory = "Keys";

        /// <summary>
        /// The whole plain view: group, config section, config key, printed name, in printed
        /// order. A group's number prefix is what sorts it in the window. To put a setting in
        /// the window, add a row; <see cref="Missing"/> names any row that no longer matches a
        /// bound entry, and the patch probe fails on it.
        /// </summary>
        private static readonly string[][] Menu =
        {
            new[] { "0 Mission", "Mission", "AskAtStart", "Ask which systems run at mission start" },
            new[] { "1 Battlefield", "Support", "Enabled", "Support calls (OPS)" },
            new[] { "1 Battlefield", "Command", "Enabled", "Command map and STR" },
            new[] { "1 Battlefield", "HighCommand", "Enabled", "Chain of command" },
            new[] { "1 Battlefield", "Intel", "Enabled", "Threat picture (AI intel)" },
            new[] { "1 Battlefield", "TheaterOps", "Enabled", "Living Front (AI ground staff)" },
            new[] { "1 Battlefield", "DynamicOperations", "Enabled", "Contracts" },
            new[] { "1 Battlefield", "Events", "Enabled", "World events" },
            new[] { "1 Battlefield", "Trenches", "Enabled", "Trenches and fortifications" },
            new[] { "1 Battlefield", "Garrisons", "Enabled", "Zone garrisons" },
            new[] { "1 Battlefield", "Fires", "Enabled", "Fire ignition and spread" },
            new[] { "1 Battlefield", "CombatAdjust", "Enabled", "Combat adjustments (fog, missile failures, smoke)" },
            new[] { "1 Battlefield", "Weather", "Enabled", "Dynamic weather" },
            new[] { "1 Battlefield", "Kaiju", "Enabled", "Kaiju" },
            new[] { "1 Battlefield", "Kaiju", "Summon", "KAIJU: summon / dismiss" },
            new[] { "1 Battlefield", "Spooktober", "Enabled", "Spooktober" },
            new[] { "1 Battlefield", "Spooktober", "AceDamageScale", "Spooktober ace: damage taken (x)" },
            new[] { "1 Battlefield", "Spooktober", "AcePhantomRangeKm", "Spooktober ace: phantom missile range (km)" },
            new[] { "1 Battlefield", "Spooktober", "AceBlinkCooldown", "Spooktober ace: blink cooldown (s)" },
            new[] { "1 Battlefield", "Spooktober", "AceRepairPerSecond", "Spooktober ace: self-repair (hull/s)" },
            new[] { "1 Battlefield", "Spooktober", "AceGunDamageScale", "Spooktober ace: gun damage (x)" },

            new[] { "2 Pilot and squadron", "Progression", "Enabled", "Perk board" },
            new[] { "2 Pilot and squadron", "Squad", "EnemyAceHunts", "Enemy ace hunts" },
            new[] { "2 Pilot and squadron", "Squadron", "SandboxFreeCalls", "Sandbox: calls cost nothing" },
            new[] { "2 Pilot and squadron", "Comms", "Enabled", "Map comms, polls and games" },

            // Look, comfort, HUD and cockpit-feel switches live on the SET console (map > SET), which applies them in flight.
            new[] { "3 Cockpit", "Radio", "Enabled", "Cockpit radio" },
            new[] { "3 Cockpit", "Autopilot", "Enabled", "Autopilot and interaction menu" },
            new[] { "3 Cockpit", "QoL", "Enabled", "Quality of life" },
            new[] { "3 Cockpit", "Immersion", "Enabled", "Cockpit immersion (tune it on SET > COCKPIT)" },
        };

        /// <summary>
        /// Sorts every bound entry into listed, key or hidden. Call once, after every module
        /// has bound its settings.
        /// </summary>
        public static void Apply(ConfigFile config)
        {
            FieldInfo description = typeof(ConfigEntryBase)
                .GetField("<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (description == null) return;

            bool expert = config.Bind("Meta", "ExpertSettings", false,
                "List every setting in the F1 window's advanced view, not only the switches. " +
                "The hidden values are tuning: change them only if you know what they feed. Applies at the next launch.").Value;

            var listed = new Dictionary<ConfigDefinition, ConfigurationManagerAttributes>(Menu.Length);
            for (int i = 0; i < Menu.Length; i++)
            {
                listed[new ConfigDefinition(Menu[i][1], Menu[i][2])] = new ConfigurationManagerAttributes
                {
                    IsAdvanced = false,
                    Category = Menu[i][0],
                    DispName = Menu[i][3],
                    // Higher sorts higher, so the declared order is the printed order.
                    Order = Menu.Length - i
                };
            }

            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> bound in config)
            {
                ConfigEntryBase entry = bound.Value;
                if (entry == null) continue;
                string section = bound.Key.Section;

#if PUBLIC_RELEASE
                // Public builds (-p:PublicRelease=true): cheats and dev tools in [Debug] are
                // forced off and dropped from the window. The log switches stay for bug reports.
                string key = bound.Key.Key;
                if (section == "Debug" && !key.Contains("Verbose") && key != "ExportLogs")
                {
                    if (entry.SettingType == typeof(bool)) entry.BoxedValue = false;
                    Tag(description, entry, new ConfigurationManagerAttributes { Browsable = false });
                    continue;
                }
#endif

                ConfigurationManagerAttributes hint;
                bool keybind = entry.SettingType == typeof(KeyCode) || entry.SettingType == typeof(KeyboardShortcut);
                if (listed.TryGetValue(bound.Key, out hint))
                {
                }
                else if (keybind && section != "Debug" && section != "Cinematography")
                {
                    // One group instead of a dozen sections that hold a key or two each. The
                    // section goes into the name because the keys themselves repeat.
                    hint = new ConfigurationManagerAttributes
                    {
                        IsAdvanced = false,
                        Category = KeysCategory,
                        DispName = section == "Keys" ? bound.Key.Key : section + ": " + bound.Key.Key
                    };
                }
                else
                {
                    hint = new ConfigurationManagerAttributes { IsAdvanced = true };
                    if (!expert) hint.Browsable = false;
                }

                Tag(description, entry, hint);
            }
        }

        /// <summary>
        /// Locks or unlocks entries in the window while a server's values stand in for them,
        /// so a client cannot edit a value that the session would overwrite and that the host
        /// ignores anyway. Entries the window was never told about are left alone.
        /// </summary>
        public static void SetReadOnly(ConfigEntryBase[] entries, bool readOnly)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                object[] tags = entries[i]?.Description?.Tags;
                if (tags == null) continue;
                for (int t = tags.Length - 1; t >= 0; t--)
                {
                    if (!(tags[t] is ConfigurationManagerAttributes hint)) continue;
                    hint.ReadOnly = readOnly ? true : (bool?)null;
                    break;
                }
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
