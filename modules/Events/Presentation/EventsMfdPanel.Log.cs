using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// EVN LOG, the one log of the game (<see cref="ITacticalLog"/>, kept by Command), newest first, filtered by category, with LOCATE on
    /// every line the game gave a place (through <see cref="IMapProjection.Focus"/>). Category chips stay neutral grey: the word says it.
    /// DISPATCH's lead contract (the old FAC DIRECTIVE) lives here too.
    /// </summary>
    internal sealed partial class EventsMfdPanel
    {
        private const int LogRows = 12;

        private AvSection logSection;
        private AvSegmented logFilter;
        private AvRow logEmpty;
        private AvPagedStack<AvRow> logList;
        private readonly AvControl[] logLocate = new AvControl[LogRows];
        private readonly List<TacticalLogEntry> logShown = new List<TacticalLogEntry>(128);
        private readonly int[] logCounts = new int[TacticalLogEntry.Categories.Length + 1];
        private int logCategory;
        private int logRevision = -1;
        private AvRow directiveRow;

        private void ResetLog()
        {
            logSection = null;
            logFilter = null;
            logEmpty = null;
            logList = null;
            Array.Clear(logLocate, 0, logLocate.Length);
            logShown.Clear();
            logCategory = 0;
            logRevision = -1;
            directiveRow = null;
        }

        private void BuildLogPage(AvFlow p)
        {
            logSection = p.Section(AvIcon.ListDetails, "THE LOG", "NEWEST FIRST");
            HelpOn(logSection, "THE LOG: every line of the game, newest first: kills and losses (AIR, GROUND), chat and what the radio aired " +
                "(RADIO), captures, HQ messages and the wire's breaking headlines (NEWS). This client's own copy; it sends nothing.");
            string[] filters = new string[TacticalLogEntry.Categories.Length + 1];
            filters[0] = "ALL";
            TacticalLogEntry.Categories.CopyTo(filters, 1);
            logFilter = p.Add(AvSegmented.Strip(p.Content, filters, () => logCategory, i => { logCategory = i; logRevision = -1; RefreshLog(); }));
            logFilter.Options[0].Help = "Every line.";
            for (int i = 1; i < filters.Length; i++) logFilter.Options[i].Help = "Only the " + filters[i] + " lines.";
            logEmpty = p.Add(new AvRow(p.Content));
            logEmpty.Set("NO LINES YET", "Kills, captures, chat, the radio and the wire's headlines file here as the mission runs.", "", AvState.Inert);
            logList = p.Add(new AvPagedStack<AvRow>(p.Content, p.Ticker, LogRows, 2f, NewLogRow, BindLogRow,
                "Log", LogRows, pagerLead: 2f, startHidden: true, trailingGap: true));
            p.Links(("WHERE IT HAPPENED", "MAP", null), ("RECEIVER", "RAD", "RECEIVER"));
            console.Orders(TabLog,
                "Every line of the game, newest first. The news ticker reads its headlines from here.",
                "LOCATE flies the map to where it happened. A greyed LOCATE: the game gave no place.",
                "Pick a category to read only that traffic.");
        }

        private AvRow NewLogRow(RectTransform parent, int slot)
        {
            var row = new AvRow(parent);
            logLocate[slot] = row.AddTrailing(new AvControl.Spec("LOCATE", () => Locate(slot), AvButtonStyle.Quiet, AvIcon.MapPin));
            return row;
        }

        private void BindLogRow(int item, AvRow row)
        {
            TacticalLogEntry entry = logShown[item];
            row.Set(entry.Text, entry.Stamp, entry.Category, AvState.Inert);
            AvControl locate = logLocate[item % LogRows];
            locate.Interactable = entry.HasPlace;
            locate.Help = entry.HasPlace ? "LOCATE: fly the map to where it happened." : "No place to locate: the game gave none for this line.";
            row.Restyle();
        }

        private void Locate(int slot)
        {
            int item = logList.Page * logList.PageSize + slot;
            if (item >= logShown.Count) return;
            TacticalLogEntry entry = logShown[item];
            if (entry.HasPlace && ModuleServices.TryGet(out IMapProjection map)) map.Focus(entry.X, entry.Z);
        }

        /// <summary>Rebinds only when the log moved or the filter changed.</summary>
        private void RefreshLog()
        {
            if (logList == null) return;
            ITacticalLog log = ModuleServices.TryGet(out ITacticalLog found) ? found : null;
            int revision = log == null ? int.MinValue : log.Revision;
            if (revision == logRevision) return;
            logRevision = revision;

            logShown.Clear();
            Array.Clear(logCounts, 0, logCounts.Length);
            string wanted = logCategory > 0 ? TacticalLogEntry.Categories[logCategory - 1] : null;
            if (log != null)
            {
                foreach (TacticalLogEntry entry in log.Entries)
                {
                    logCounts[0]++;
                    logCounts[1 + Array.IndexOf(TacticalLogEntry.Categories, entry.Category)]++;
                    if (wanted == null || entry.Category == wanted) logShown.Add(entry);
                }
            }
            logFilter.Options[0].Label = "ALL " + AvNum.Fixed(logCounts[0], 0);
            for (int i = 0; i < TacticalLogEntry.Categories.Length; i++)
                logFilter.Options[i + 1].Label = TacticalLogEntry.Categories[i] + " " + AvNum.Fixed(logCounts[i + 1], 0);
            logSection.SetCaption(log == null ? "AVIONICS OFF · NO GAME LOG" : AvNum.Fixed(logCounts[0], 0) + " LINES · NEWEST FIRST");
            logEmpty.SetShown(logShown.Count == 0);
            if (log == null) logEmpty.Set("AVIONICS OFF", "The game log is kept by the avionics (F1). Switch them on and it fills.", "", AvState.Inert);
            logList.SetCount(logShown.Count);
        }

        /// <summary>The faction's lead contract (the one in hand, else the first on offer), from the contracts board.</summary>
        private void RefreshDirective()
        {
            if (directiveRow == null) return;
            SecondaryObjectiveView lead = null;
            bool off = false;
            if (ModuleServices.TryGet(out ISecondaryObjectivesView board) && board != null)
            {
                off = board.Status != null && board.Status.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0;
                if (board.Objectives != null)
                    foreach (SecondaryObjectiveView entry in board.Objectives)
                    {
                        if (entry == null) continue;
                        if (entry.IsActive) { lead = entry; break; }
                        if (lead == null && entry.IsOffered) lead = entry;
                    }
            }
            if (lead == null)
                directiveRow.Set(off ? "DIRECTIVES OFF" : "NO DIRECTIVE", "The lead contract shows here when the HQ posts one.", "", AvState.Inert);
            else
                directiveRow.Set((lead.IsActive ? "ACTIVE · " : "OFFERED · ") + (lead.Title ?? "").Replace('_', ' ').ToUpperInvariant(),
                    "$" + AvNum.Thousands(Mathf.Max(0, lead.Money)) + " · " + AvNum.Thousands(Mathf.Max(0, lead.Xp)) + " XP · +3 MORALE",
                    "", AvState.Info);
        }
    }
}
