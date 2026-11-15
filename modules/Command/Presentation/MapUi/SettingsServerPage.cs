using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>The SERVER page: live host controls, one section per feature, read-only for remote clients.</summary>
    internal sealed partial class SettingsMfdPanel
    {
        // Reading order: world rules, forces and economy, then effects; sections a module adds later follow in registration order.
        private static readonly string[] SectionOrder =
        {
            "DYNAMIC OPERATIONS", "WORLD EVENTS", "TRENCHES", "URBAN COMBAT",
            "NATION", "HIGH COMMAND", "SUPPORT CALL-INS", "SQUAD AND ACES", "PROGRESSION", "COMMS",
            "FIRE AND DESTRUCTION", "WEATHER"
        };

        private static AvIcon SectionIcon(string section)
        {
            switch ((section ?? "").ToUpperInvariant())
            {
                case "DYNAMIC OPERATIONS": return AvIcon.Target;
                case "WORLD EVENTS": return AvIcon.Flag;
                case "TRENCHES": return AvIcon.Ruler2;
                case "URBAN COMBAT": return AvIcon.Shield;
                case "HIGH COMMAND":
                case "NATION": return AvIcon.Crown;
                case "SUPPORT CALL-INS": return AvIcon.Radio;
                case "SQUAD AND ACES": return AvIcon.UsersGroup;
                case "PROGRESSION": return AvIcon.Star;
                case "COMMS": return AvIcon.Antenna;
                case "FIRE AND DESTRUCTION": return AvIcon.Flame;
                case "WEATHER": return AvIcon.CloudRain;
                default: return AvIcon.Settings;
            }
        }

        private static int SectionRank(string section)
        {
            int i = Array.IndexOf(SectionOrder, (section ?? "").ToUpperInvariant());
            return i < 0 ? int.MaxValue : i;
        }

        private void BuildServerPage(AvFlow flow, int page)
        {
            var views = new List<IHostSettingsView>();
            if (hostSettings != null)
                for (int i = 0; i < hostSettings.Views.Count; i++) views.Add(hostSettings.Views[i]);

            if (views.Count == 0)
            {
                flow.Section(AvIcon.Flag, "WORLD RULES", "NOT INSTALLED");
                flow.Add(new AvNote(flow.Content) { MinHeight = 18f }).Set("No settings are published here. The features that own them are not running in this session.");
                return;
            }

            // Stable by registration order among equals.
            for (int i = 1; i < views.Count; i++)
            {
                IHostSettingsView v = views[i];
                int j = i - 1;
                while (j >= 0 && SectionRank(views[j].Section) > SectionRank(v.Section)) { views[j + 1] = views[j]; j--; }
                views[j + 1] = v;
            }

            flow.Ticker.Add(page, AvTickRate.Slow, () =>
            {
                for (int i = 0; i < views.Count; i++) views[i].Refresh();
            });

            for (int v = 0; v < views.Count; v++)
            {
                IHostSettingsView view = views[v];
                AvSection section = flow.Section(SectionIcon(view.Section), view.Section, null);
                bool host = !HostAuthority();   // forces the first write below
                void Caption()
                {
                    if (host == HostAuthority()) return;
                    host = HostAuthority();
                    section.SetCaption(host ? "LIVE" : "LOCKED, HOST ONLY");
                }
                Caption();
                flow.Ticker.Add(page, AvTickRate.Slow, Caption);

                // Toggles become thin switch bars (name + ON/OFF + slide track, sentence on hover), levels
                // that print a percentage become dials, and every other stepper keeps a +/- row.
                SetRingRow dials = null;
                int dialCount = 0;
                for (int r = 0; r < view.Rows.Count; r++)
                {
                    HostSettingView row = view.Rows[r];
                    if (row.Kind == HostSettingKind.Toggle)
                    {
                        dials = null;
                        ServerToggleCell(flow, page, view, row);
                    }
                    else if (TryPercent(row.ValueText, out _))
                    {
                        if (dials == null || dialCount >= 3) { dials = flow.Add(new SetRingRow(flow.Content)); dialCount = 0; }
                        dialCount++;
                        ServerLevelRing(flow, dials, page, view, row);
                    }
                    else
                    {
                        dials = null;
                        Stepper(flow, page, row.Label, () => row.ValueText,
                            d => view.Step(row.Id, d),
                            () => RowInteractive(row) && row.CanDecrease,
                            () => RowInteractive(row) && row.CanIncrease,
                            row.Help, () => RowInteractive(row), () => RowReason(row), readOnlyValue: true);
                    }
                }
            }
        }

        private void ServerToggleCell(AvFlow flow, int page, IHostSettingsView view, HostSettingView row)
        {
            SetToggleBar cell = null;
            cell = flow.Add(new SetToggleBar(flow.Content, row.Label, () => row.Value, v =>
            {
                if (!RowInteractive(row)) return;
                view.Toggle(row.Id);
                Echo(row.Label + " \u2014 " + (row.Value ? "ON" : "OFF"));
                Changed();
            }));
            void Refresh()
            {
                cell.Interactable = RowInteractive(row);
                cell.Help = RowReason(row);
                cell.Refresh();
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
        }

        /// <summary>A host level whose printed value is a percentage (e.g. "60%"), as a dial with - / +.</summary>
        private void ServerLevelRing(AvFlow flow, SetRingRow dials, int page, IHostSettingsView view, HostSettingView row)
        {
            Ring(flow, dials, page, row.Label, () => row.ValueText,
                () => TryPercent(row.ValueText, out float v) ? Mathf.Clamp01(v) : 0f,
                d => view.Step(row.Id, d),
                () => RowInteractive(row) && row.CanDecrease, () => RowInteractive(row) && row.CanIncrease,
                row.Help, () => RowInteractive(row), () => RowReason(row));
        }

        private static bool TryPercent(string text, out float v01)
        {
            v01 = 0f;
            if (string.IsNullOrEmpty(text) || !text.EndsWith("%", StringComparison.Ordinal)) return false;
            if (!float.TryParse(text.Substring(0, text.Length - 1).Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float pct)) return false;
            v01 = pct / 100f;
            return true;
        }

        private static bool RowInteractive(HostSettingView row) => HostAuthority() && row.Interactive;

        private static string RowReason(HostSettingView row) =>
            !HostAuthority()
                ? "LOCKED \u00b7 " + row.Help
                : row.Reason ?? row.Help;
    }
}
