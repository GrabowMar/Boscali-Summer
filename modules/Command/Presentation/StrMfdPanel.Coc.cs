using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// COC — one faction's chain of command at a time, switched by an ALLIED/HOSTILE segment.
    ///
    /// <para>This is a reading board, not a control board: the staff lives on its own. Every
    /// commander earns their faction a bonus while they serve (income, kill value, patrol
    /// reach), occasionally travels between bases in a VIP convoy, and can be killed at their
    /// post or on the road; a successor takes over and the bonus is interrupted. Nothing here
    /// can be ordered, marked or spent - the page exists so a pilot knows who runs the enemy
    /// side of the map, where they are, and what killing them costs.</para>
    ///
    /// <para>The roster is followed by the selected file and the staff log. Selecting a row or
    /// a log entry opens that post's file. Enemy posts are listed by identity; until local
    /// intel confirms one, its file reads unconfirmed and its record stays sealed.</para>
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        /// <summary>HighCommand's wire ceiling is eight posts per faction; rows are pooled once.</summary>
        private const int CocRosterRows = 8;
        private const int CocLogRows = 4;
        private const int CocBonusEntries = 3;

        private readonly CommanderView[] cocVisible = new CommanderView[CocRosterRows];
        private readonly int[] cocIds = new int[CocRosterRows];
        private readonly int[] cocParents = new int[CocRosterRows];
        private readonly int[] cocOrder = new int[CocRosterRows];
        private readonly bool[] cocOrdered = new bool[CocRosterRows];
        private readonly int[] cocRowIds = new int[CocRosterRows];
        private readonly int[] cocLogTargetIds = new int[CocLogRows];

        private AvSection cocSection;
        private AvSegmented cocSideControl;
        private AvRowStack cocRoster;
        private AvSection cocFileSection;
        private AvRow cocIdentityRow, cocRankRow, cocStationRow, cocStatusRow, cocShareRow;
        private AvRowStack cocBonusRows;
        private ProseText cocBioText;
        private AvSection cocLogSection;
        private AvRowStack cocLogRows;
        private bool cocShowHostile;
        private int cocSelectedId = -1;

        private void ResetCoc()
        {
            Array.Clear(cocVisible, 0, cocVisible.Length);
            Array.Clear(cocIds, 0, cocIds.Length);
            Array.Clear(cocParents, 0, cocParents.Length);
            Array.Clear(cocOrder, 0, cocOrder.Length);
            Array.Clear(cocOrdered, 0, cocOrdered.Length);
            Array.Clear(cocRowIds, 0, cocRowIds.Length);
            Array.Clear(cocLogTargetIds, 0, cocLogTargetIds.Length);
            cocSection = cocFileSection = cocLogSection = null;
            cocSideControl = null;
            cocRoster = cocBonusRows = cocLogRows = null;
            cocIdentityRow = cocRankRow = cocStationRow = cocStatusRow = cocShareRow = null;
            cocBioText = null;
            cocShowHostile = false;
            cocSelectedId = -1;
        }

        private void BuildCocPage(AvFlow p)
        {
            cocSection = p.Section(AvIcon.UsersGroup, "CHAIN OF COMMAND", "");
            cocSideControl = p.Add(new AvSegmented(p.Content, "SIDE", new[] { "ALLIED", "HOSTILE" },
                () => cocShowHostile ? 1 : 0, i => { cocShowHostile = i == 1; nextRefresh = 0f; }));
            cocSideControl.Options[0].Help = "Show the allied chain of command.";
            cocSideControl.Options[1].Help =
                "Show the opposing chain of command. A post stays unconfirmed until local intel has seen it.";
            cocRoster = p.Add(new AvRowStack(p.Content, CocRosterRows, RosterClicked));

            cocFileSection = p.Section(AvIcon.User, "PERSONNEL FILE", "SELECT A POST");
            cocIdentityRow = p.Add(new AvRow(p.Content));
            cocRankRow = p.Add(new AvRow(p.Content));
            cocStationRow = p.Add(new AvRow(p.Content));
            cocStatusRow = p.Add(new AvRow(p.Content));
            cocShareRow = p.Add(new AvRow(p.Content));
            cocBonusRows = p.Add(new AvRowStack(p.Content, CocBonusEntries, null));
            cocBioText = p.Add(new ProseText(p.Content));

            cocLogSection = p.Section(AvIcon.ListDetails, "STAFF LOG", "");
            cocLogRows = p.Add(new AvRowStack(p.Content, CocLogRows, LogRowClicked));
        }

        private void RosterClicked(int slot)
        {
            if (slot < 0 || slot >= CocRosterRows) return;
            int id = cocRowIds[slot];
            if (id >= 0) SelectCoc(id);
        }

        private void LogRowClicked(int slot)
        {
            if (slot < 0 || slot >= CocLogRows) return;
            int id = cocLogTargetIds[slot];
            if (id >= 0) SelectCoc(id);
        }

        private void SelectCoc(int id)
        {
            cocSelectedId = id;
            nextRefresh = 0f;
        }

        private void RefreshCoc()
        {
            if (cocSection == null) return;

            bool available = highCommand != null && highCommand.Available;
            if (!available)
            {
                // No staff, no board: an empty page is not a reading, so the panel says so —
                // the reason itself is the host's own sentence on the status strip.
                cocSection.SetCaption("NO STAFF BOARD");
                for (int i = 0; i < CocRosterRows; i++) { cocRoster.Hide(i); cocRowIds[i] = -1; }
                BindDossier(null);
                cocLogSection.SetCaption("NO TRAFFIC");
                for (int i = 0; i < CocLogRows; i++) { cocLogRows.Hide(i); cocLogTargetIds[i] = -1; }
                if (highCommand != null) highCommand.Highlight(-1);
                cocSelectedId = -1;
                return;
            }

            cocSideControl.Refresh();

            IReadOnlyList<CommanderView> list = highCommand.Commanders;
            int ownCount = 0, enemyTotal = 0, enemyKnown = 0, visible = 0;
            CommanderView selected = null;
            bool selectedOnActiveSide = false;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    CommanderView view = list[i];
                    if (view.IsFriendly) ownCount++;
                    else
                    {
                        enemyTotal++;
                        if (view.IsKnown) enemyKnown++;
                    }

                    bool activeSide = view.IsFriendly != cocShowHostile;
                    if (view.Id == cocSelectedId) { selected = view; selectedOnActiveSide = activeSide; }

                    if (!activeSide || visible >= CocRosterRows) continue;
                    cocVisible[visible] = view;
                    cocIds[visible] = view.Id;
                    cocParents[visible] = view.ParentId;
                    visible++;
                }
            }

            int ordered = CommandRosterOrder.Sort(visible, cocIds, cocParents, cocOrder, cocOrdered);
            for (int i = 0; i < ordered; i++)
            {
                CommanderView view = cocVisible[cocOrder[i]];
                cocRoster.Show(i);
                cocRowIds[i] = view.Id;
                AvState state = view.Id == cocSelectedId ? AvState.Info : RailState(StateOf(view));
                string name = view.IsKia ? view.Name + "  [KIA]" : view.Name;
                // Tier is indentation, the way the branch reads on a page that no longer draws
                // a trunk line: each level under the theater commander adds one bullet.
                string indent = view.Tier > 0 ? new string('›', view.Tier) + " " : "";
                cocRoster.Row(i).Set(indent + name, view.Rank + " · " + view.Role, StatusOf(view), state);
                // The row's copy is the office; the bonus its post carries rides the help line.
                string bonus = !view.IsFriendly && !view.IsKnown ? "" : FirstBonus(view.Bonus);
                cocRoster.Row(i).Help = (view.IsFriendly
                    ? "Open the card for " + view.Name + "  ·  " + view.Role
                    : view.IsKnown
                        ? "Confirmed contact: " + view.Name + "  ·  " + view.Role
                        : "Unconfirmed post: " + view.Name + " — no local intel.") +
                    (string.IsNullOrEmpty(bonus) ? "" : "  ·  " + bonus) +
                    (view.Id == cocSelectedId ? "  ·  Bracketed on the map." : "");
            }
            for (int i = ordered; i < CocRosterRows; i++) { cocRoster.Hide(i); cocRowIds[i] = -1; }

            cocSection.SetCaption(!cocShowHostile
                ? (ownCount == 0 ? "NO POSTS REPORTED"
                    : ownCount + (ownCount == 1 ? " POST" : " POSTS") + "  ·  " +
                      TheaterReadout.Percent(Mathf.Clamp01(highCommand.FriendlyCohesion)) + " EFFECTIVE")
                : (enemyTotal == 0 ? "NO POSTS REPORTED"
                    : enemyKnown == 0 ? "NO CONFIRMED CONTACTS"
                    : enemyKnown + " OF " + enemyTotal + " CONFIRMED"));

            RefreshCocLogList();

            if (selected == null) cocSelectedId = -1;
            CommanderView open = selectedOnActiveSide ? selected : null;
            // The map rings the post whose file is open, so a reader can find the general
            // they are reading about. An empty file rings nothing.
            if (highCommand != null) highCommand.Highlight(open != null ? open.Id : -1);
            BindDossier(open);
        }

        private void RefreshCocLogList()
        {
            IReadOnlyList<CommanderLogLine> lines = cocShowHostile ? highCommand.HostileLog : highCommand.Log;
            int count = lines != null ? lines.Count : 0;
            int shown = 0;
            for (int i = 0; i < count && shown < CocLogRows; i++)
            {
                CommanderLogLine line = lines[i];
                if (line == null) continue;
                bool clickable = LogTargetOnVisibleSide(line.TargetId);
                cocLogRows.Show(shown);
                AvRow row = cocLogRows.Row(shown);
                row.Set(line.Text, null, TheaterReadout.Age(line.Age), LogState(line.Tone));
                row.Interactable = clickable;
                row.Help = clickable ? "Open this post's card." : "Post is not on this side.";
                cocLogTargetIds[shown] = clickable ? line.TargetId : -1;
                shown++;
            }
            for (int i = shown; i < CocLogRows; i++) { cocLogRows.Hide(i); cocLogTargetIds[i] = -1; }

            cocLogSection.SetCaption(count == 0 ? "NO TRAFFIC" : count + (count == 1 ? " ENTRY" : " ENTRIES"));
        }

        /// <summary>A log line is clickable only when its subject is on the side the tree is
        /// showing, so selecting a line never opens an empty file.</summary>
        private bool LogTargetOnVisibleSide(int id)
        {
            if (id < 0 || highCommand == null) return false;
            IReadOnlyList<CommanderView> list = highCommand.Commanders;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id == id) return list[i].IsFriendly != cocShowHostile;
            return false;
        }

        /// <summary>Put the selected commander's file on the page, or a placeholder when none is open.</summary>
        private void BindDossier(CommanderView view)
        {
            if (cocIdentityRow == null) return;

            if (view == null)
            {
                cocFileSection.SetCaption("SELECT A POST");
                cocIdentityRow.Set("NO FILE OPEN", "Select a post or a staff-log entry to open its file.", "", AvState.Inert);
                cocRankRow.Set("RANK", null, "—", AvState.Inert);
                cocStationRow.Set("STATION", null, "—", AvState.Inert);
                cocStatusRow.Set("STATUS", null, "—", AvState.Inert);
                cocShareRow.Set("STAFF SHARE", null, "—", AvState.Inert);
                for (int i = 0; i < cocBonusRows.Count; i++) cocBonusRows.Hide(i);
                cocBioText.Set("");
                return;
            }

            bool sealedFile = !view.IsFriendly && !view.IsKnown;
            AvState state = RailState(StateOf(view));
            cocFileSection.SetCaption(view.IsFriendly ? "ALLIED PERSONNEL"
                : view.IsKnown ? "IDENTITY CONFIRMED" : "IDENTITY UNCONFIRMED");
            cocIdentityRow.Set(view.IsKia ? view.Name + "  [KIA]" : view.Name, view.Role, StatusOf(view), state);
            cocRankRow.Set("RANK", null, sealedFile ? "—" : string.IsNullOrEmpty(view.Rank) ? "—" : view.Rank, AvState.Info);
            cocStationRow.Set("STATION", null, sealedFile ? "—" : Pretty(view.Location) ?? "—", AvState.Info);
            cocStatusRow.Set("STATUS", null, StatusOf(view), state);
            cocShareRow.Set("STAFF SHARE", null, Percent(view), AvState.Info);

            BindBonus(sealedFile ? null : view.Bonus);
            cocBioText.Set(sealedFile ? "FILE SEALED — local intel has not confirmed this post." : (view.Bio ?? ""));
        }

        private void BindBonus(string bonus)
        {
            if (string.IsNullOrEmpty(bonus) || bonus == "NO STAFF BONUS")
            {
                cocBonusRows.Show(0);
                cocBonusRows.Row(0).Set("NO NOTABLE TRAITS", null, "", AvState.Inert);
                for (int i = 1; i < cocBonusRows.Count; i++) cocBonusRows.Hide(i);
                return;
            }

            int used = 0, start = 0;
            while (used < cocBonusRows.Count)
            {
                int end = bonus.IndexOf(" · ", start, StringComparison.Ordinal);
                string entry = end < 0 ? bonus.Substring(start) : bonus.Substring(start, end - start);
                SplitBonus(entry, out string label, out string pay);
                bool last = end < 0 || used + 1 >= cocBonusRows.Count;
                if (last && end >= 0)
                    pay = string.IsNullOrEmpty(pay) ? "…" : pay + "  ·  …";
                cocBonusRows.Show(used);
                cocBonusRows.Row(used).Set(label, null, pay,
                    string.IsNullOrEmpty(pay) ? AvState.Inert : pay[0] == '-' ? AvState.Caution : AvState.Ready);
                used++;
                if (last) break;
                start = end + 3;
            }
            for (int i = used; i < cocBonusRows.Count; i++) cocBonusRows.Hide(i);
        }

        /// <summary>The first entry of the bonus line, for the roster row's help.</summary>
        private static string FirstBonus(string bonus)
        {
            if (string.IsNullOrEmpty(bonus)) return "";
            int at = bonus.IndexOf(" · ", StringComparison.Ordinal);
            return at < 0 ? bonus : bonus.Substring(0, at);
        }

        /// <summary>Base names arrive off the wire as identifiers; a file prints words.</summary>
        private static string Pretty(string text) =>
            string.IsNullOrEmpty(text) ? text : text.Replace('_', ' ').ToUpperInvariant();

        private static string Percent(CommanderView view) =>
            view == null ? "—" : Mathf.RoundToInt(Mathf.Clamp01(view.Weight) * 100f) + "%";

        /// <summary>Split "LOGISTICS MIND +15% STIPEND" into the trait and what it pays.</summary>
        private static void SplitBonus(string entry, out string label, out string pay)
        {
            label = entry;
            pay = "";
            for (int i = 1; i < entry.Length - 1; i++)
            {
                if (entry[i] != ' ' || (entry[i + 1] != '+' && entry[i + 1] != '-')) continue;
                label = entry.Substring(0, i);
                pay = entry.Substring(i + 1);
                return;
            }
        }

        private static string StatusOf(CommanderView view)
        {
            if (view.IsKia) return "KIA";
            if (!view.IsFriendly && !view.IsKnown) return "UNCONFIRMED";
            if (view.Alert) return "UNDER FIRE";
            if (view.InTransit) return "EN ROUTE";
            if (view.Disrupted) return "SUCCESSION";
            if (!view.IsFriendly && view.IntelAge >= 0f) return "SEEN " + Mathf.RoundToInt(view.IntelAge) + "S";
            return "ACTIVE";
        }

        private static string StateOf(CommanderView view)
        {
            if (view.IsKia) return "inert";
            if (!view.IsFriendly && !view.IsKnown) return "inert";
            if (view.Alert) return "danger";
            if (view.InTransit) return "info";
            if (view.Disrupted) return "caution";
            return view.IsFriendly ? "ready" : "hostile";
        }

        /// <summary>Rail state for a log tone.</summary>
        private static AvState LogState(CommanderLogTone tone)
        {
            switch (tone)
            {
                case CommanderLogTone.Economy: return AvState.Ready;
                case CommanderLogTone.Order: return AvState.Info;
                case CommanderLogTone.Contact: return AvState.Caution;
                case CommanderLogTone.Loss: return AvState.Danger;
                case CommanderLogTone.Alert: return AvState.Danger;
                default: return AvState.Inert;
            }
        }
    }
}
