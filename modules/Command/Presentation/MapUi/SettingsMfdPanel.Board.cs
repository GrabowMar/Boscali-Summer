using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The rows other features publish to SET (<see cref="ClientSettingsBoard"/>): each row's section picks its tab
    /// and its place there, so Command never reaches into a sibling's settings. A section the table does not list
    /// lands at the foot of PERF, where every row used to go.
    /// </summary>
    internal sealed partial class SettingsMfdPanel
    {
        private static readonly (string Section, int Tab, AvIcon Icon)[] ClientSections =
        {
            ("SCREENS", CPanels, AvIcon.Stack2),
            ("FLIGHT AIDS", CHud, AvIcon.Plane),
            ("COMMS", CHud, AvIcon.Antenna),
            ("IMMERSION", CCockpit, AvIcon.WaveSine),
            ("COCKPIT MOTION", CCockpit, AvIcon.Activity),
            ("COCKPIT VISUALS", CCockpit, AvIcon.Eye),
            ("COCKPIT AUDIO", CCockpit, AvIcon.Volume),
            ("SKY", CWorld, AvIcon.Cloud),
            ("RAIN", CWorld, AvIcon.CloudRain),
            ("SEA", CWorld, AvIcon.Wind),
            ("WORLD FX", CWorld, AvIcon.Flame),
            ("COLOUR GRADE", CWorld, AvIcon.Filter),
            ("MISSIONS", CWorld, AvIcon.Flag),
            ("FRAME BUDGET", CPerf, AvIcon.Gauge),
            ("BASE GAME", CPerf, AvIcon.Settings),
            ("FAR UNITS", CPerf, AvIcon.Radar2),
        };

        private static int ClientSectionIndex(string section)
        {
            for (int i = 0; i < ClientSections.Length; i++)
                if (ClientSections[i].Section == section) return i;
            return -1;
        }

        private static int ClientTabOf(string section)
        {
            int i = ClientSectionIndex(section);
            return i < 0 ? CPerf : ClientSections[i].Tab;
        }

        /// <summary>Every published row for <paramref name="tab"/>, one section at a time: switches, then dials, then choices.</summary>
        private void BuildBoardRows(AvFlow flow, int page, int tab)
        {
            IReadOnlyList<ClientSetting> rows = clientSettings?.Rows;
            if (rows == null) return;
            var sections = new List<string>();
            foreach (var entry in ClientSections) if (entry.Tab == tab) sections.Add(entry.Section);
            if (tab == CPerf)
                foreach (ClientSetting row in rows)
                    if (ClientSectionIndex(row.Section) < 0 && !sections.Contains(row.Section)) sections.Add(row.Section);

            foreach (string section in sections)
            {
                bool any = false;
                foreach (ClientSetting row in rows) any |= row.Section == section;
                if (!any) continue;   // the feature that owns it is not installed
                int index = ClientSectionIndex(section);
                flow.Section(index < 0 ? AvIcon.Settings : ClientSections[index].Icon, section, "APPLIES NOW");

                AvCellGrid grid = null;
                foreach (ClientSetting row in rows)
                    if (row.Section == section && row is ClientSettingToggle t)
                        ToggleCell(flow, grid = grid ?? flow.Grid(2), page, t.Label, t.Help, () => t.Value, t.Set,
                            () => t.Available, () => t.Reason);
                SetRingRow dials = null;
                foreach (ClientSetting row in rows)
                    if (row.Section == section && row is ClientSettingLevel l)
                        Ring(flow, dials = dials ?? flow.Add(new SetRingRow(flow.Content)), page, l.Label, () => l.Text,
                            () => l.Level01, l.Nudge, () => l.CanStep(-1), () => l.CanStep(1), l.Help,
                            () => l.Available, () => l.Reason);
                foreach (ClientSetting row in rows)
                    if (row.Section == section && row is ClientSettingChoice c)
                    {
                        var briefs = new string[c.Names.Length];
                        for (int i = 0; i < briefs.Length; i++) briefs[i] = c.Help;
                        BoardStrip(flow, page, c.Label, c.Names, briefs, () => c.Index, c.Select, () => c.Available, () => c.Reason);
                    }
            }
        }

        /// <summary>Puts every published row on <paramref name="tab"/> back to its default. Nothing on another tab moves.</summary>
        private void BoardResetButton(AvFlow flow, int tab, string what)
        {
            if (clientSettings == null || clientSettings.Rows.Count == 0) return;
            AvControl reset = flow.Buttons(new AvControl.Spec("RESET " + what, () =>
            {
                foreach (ClientSetting row in clientSettings.Rows)
                    if (ClientTabOf(row.Section) == tab) row.Reset();
                Echo(what + " reset to defaults.");
                Changed();
                flow.Ticker.TickNow();
            }, AvButtonStyle.Default, AvIcon.Refresh)).Controls[0];
            reset.Help = "Put every " + what + " setting on this page back to its default. Nothing else changes.";
        }
    }
}
