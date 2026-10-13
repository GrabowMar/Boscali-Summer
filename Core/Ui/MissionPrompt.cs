using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Modules;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The mission-start prompt: which Boscali Summer systems run in this mission. It opens on
    /// the map screen once a mission is running and stays out of the cockpit.
    /// </summary>
    internal sealed class MissionPrompt : MonoBehaviour
    {
        // Four columns at this size show every module and the start row without scrolling.
        private const float Width = 1240f, Height = 700f;
        private const int Columns = 4;

        /// <summary>Module id, the name a pilot knows it by, what it does in one line.</summary>
        private static readonly string[][] Labels =
        {
            new[] { "radio", "RADIO", "Cockpit radio stations" },
            new[] { "chatter", "CHATTER", "Voices on the radio nets" },
            new[] { "comms", "COMMS", "Map pings, polls and team comms" },
            new[] { "wing", "WING", "Wingmen that fly on your orders" },
            new[] { "autopilot", "AUTOPILOT", "Autopilot and the C menu" },
            new[] { "command", "AVIONICS", "MFD panels and tactical map" },
            new[] { "immersion", "IMMERSION", "G cues, visor, pilot body" },
            new[] { "effects", "EFFECTS", "Display grade and sharpening" },
            new[] { "qol", "QUALITY OF LIFE", "Small cockpit conveniences" },
            new[] { "cinematography", "CINEMATOGRAPHY", "Private cinematic shot tools" },
            new[] { "strategic-command", "STR", "Theater command and strategic map" },
            new[] { "support", "OPS", "Support calls and fronts" },
            new[] { "progression", "PROGRESSION", "Perk board" },
            new[] { "squad", "SQUAD", "Squadron and enemy ace hunts" },
            new[] { "dynamic-operations", "CONTRACTS", "Extra objectives during the mission" },
            new[] { "high-command", "HIGH COMMAND", "Chain of command and VIP staff" },
            new[] { "intel", "INTEL", "Fog-of-war threat picture" },
            new[] { "theater-ops", "LIVING FRONT", "AI staff steering ground forces" },
            new[] { "air-survival", "AIR STATIONS", "AI air survival and stations" },
            new[] { "urban-combat", "URBAN COMBAT", "Garrisons and airborne troops" },
            new[] { "trenches", "TRENCHES", "Front-line trench lines" },
            new[] { "events", "EVENTS", "World events and their modifiers" },
            new[] { "weather", "WEATHER", "Dynamic weather and clouds" },
            new[] { "fire-and-destruction", "FIRE AND DESTRUCTION", "Fire spread and lasting wrecks" },
            new[] { "player-spawn-priority", "SPAWN PRIORITY", "Players get hangar slots first" },
            new[] { "vanguard", "VANGUARD", "Extra weapons and gliders" }
        };

        private readonly HashSet<string> off = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<AvCell> cells = new List<AvCell>();
        private readonly AvSection[] sections = new AvSection[2];
        private AvWindow window;
        private string mission;

        /// <summary>Every module the player may switch, in roster order.</summary>
        internal ModuleMetadata[] Offered = Array.Empty<ModuleMetadata>();

        private void Update()
        {
            string pending = BoscaliMission.PendingMission;
            bool wanted = pending != null && MissionManager.IsRunning && Cursor.lockState != CursorLockMode.Locked;
            if (window != null && (!wanted || pending != mission)) Close();
            if (wanted && window == null) Open(pending);
        }

        private void OnDisable() => Close();

        private void Open(string name)
        {
            mission = name;
            off.Clear();
            cells.Clear();
            foreach (ModuleMetadata module in Offered)
                if (!MissionProfile.AllowsModule(module.Id)) off.Add(module.Id);

            window = AvWindow.Build(transform, "mission-systems",
                "MISSION SYSTEMS / " + (string.IsNullOrEmpty(name) ? "UNTITLED" : name.ToUpperInvariant()), Width, Height, 30010);
            // It opens over the faction screen, whose text would read through glass.
            window.Opaque = true;
            window.CloseControl.Help = "Start with the systems shown.";
            window.Closed += () => Done(false, false);
            AvFlow body = window.Body;
            body.Add(new AvNote(body.Content,
                "Choose what Boscali Summer runs in this mission. Cockpit systems leave it as its author made it; battlefield systems change it."));
            body.Buttons(
                new AvControl.Spec("ALL ON / FULL EXPERIENCE", () => Preset(false)),
                new AvControl.Spec("BATTLEFIELD OFF / IMMERSION ONLY", () => Preset(true)));
            AddGroup(body, false, AvIcon.Gauge, "COCKPIT AND IMMERSION");
            AddGroup(body, true, AvIcon.ListDetails, "BATTLEFIELD");
            body.Space(6f);
            body.Buttons(
                new AvControl.Spec("START", () => Done(false, false), AvButtonStyle.Primary),
                new AvControl.Spec("START / KEEP FOR THIS MISSION", () => Done(true, false)),
                new AvControl.Spec("START / NEVER ASK AGAIN", () => Done(false, true)));
            window.Footer.Set("A battlefield system switched on here joins a mission already under way. F1 > Mission brings this prompt back.");
            RefreshCells();
            window.Show();
        }

        private void AddGroup(AvFlow body, bool battlefield, AvIcon icon, string title)
        {
            sections[battlefield ? 1 : 0] = body.Section(icon, title);
            AvCellGrid grid = body.Grid(Columns);
            // Labelled modules first, in the table's reading order; anything new falls in after them.
            foreach (string[] label in Labels) AddCell(grid, battlefield, label[0], label[1], label[2]);
            foreach (ModuleMetadata module in Offered)
                if (Array.FindIndex(Labels, label => label[0] == module.Id) < 0)
                    AddCell(grid, battlefield, module.Id, module.Id.Replace('-', ' ').ToUpperInvariant(), module.DisplayName);
        }

        private void AddCell(AvCellGrid grid, bool battlefield, string id, string title, string sub)
        {
            if (MissionProfile.IsBattlefield(id) != battlefield || Array.FindIndex(Offered, module => module.Id == id) < 0) return;
            cells.Add(grid.Toggle(title, sub, () => !off.Contains(id), on => { Set(id, on); RefreshCells(); }));
        }

        /// <summary>Switching a module on brings what it needs; switching one off takes what needs it.</summary>
        private void Set(string id, bool on)
        {
            if (on ? !off.Remove(id) : !off.Add(id)) return;
            foreach (ModuleMetadata module in Offered)
            {
                if (on && module.Id == id)
                    foreach (string dependency in module.Dependencies) Set(dependency, true);
                if (!on && Array.IndexOf(module.Dependencies, id) >= 0) Set(module.Id, false);
            }
        }

        private void Preset(bool immersionOnly)
        {
            off.Clear();
            if (immersionOnly)
                foreach (ModuleMetadata module in Offered)
                    if (MissionProfile.IsBattlefield(module.Id)) off.Add(module.Id);
            RefreshCells();
        }

        private void RefreshCells()
        {
            foreach (AvCell cell in cells) cell.Refresh();
            for (int group = 0; group < 2; group++)
            {
                int total = 0, running = 0;
                foreach (ModuleMetadata module in Offered)
                {
                    if (MissionProfile.IsBattlefield(module.Id) != (group == 1)) continue;
                    total++;
                    if (!off.Contains(module.Id)) running++;
                }
                sections[group]?.SetCaption(running + " OF " + total + " ON / " +
                    (group == 1 ? "CHANGES THE MISSION" : "LEAVES THE MISSION ALONE"));
            }
        }

        private void Done(bool keepForMission, bool neverAsk)
        {
            if (window == null) return;
            BoscaliMission.Confirm(mission, off, keepForMission, neverAsk);
            Close();
        }

        private void Close()
        {
            if (window == null) return;
            AvWindow closing = window;
            window = null;
            closing.Hide();
            Destroy(closing.Root.gameObject);
        }
    }
}
