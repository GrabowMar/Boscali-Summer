using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
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
    /// <para>The page is one line (side switch + staff effect bar), the org chart, the selected post's
    /// dossier, then the staff log, which grows to fill whatever height is left. Selecting a card or a log
    /// entry opens that post's file. Enemy posts are listed by identity; until local intel confirms one,
    /// its file reads unconfirmed and its record stays sealed.</para>
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        /// <summary>HighCommand's wire ceiling is eight posts per faction; the chart is bounded to match.</summary>
        private const int CocRosterRows = StrOrgChart.MaxNodes;
        private const int CocLogRows = 8;
        private const int CocBonusEntries = 3;

        private readonly CommanderView[] cocVisible = new CommanderView[CocRosterRows];
        private readonly int[] cocIds = new int[CocRosterRows];
        private readonly int[] cocParents = new int[CocRosterRows];
        private readonly int[] cocOrder = new int[CocRosterRows];
        private readonly bool[] cocOrdered = new bool[CocRosterRows];
        private readonly int[] cocLogTargetIds = new int[CocLogRows];
        private readonly StrOrgNode[] cocNodes = new StrOrgNode[CocRosterRows];
        private readonly string[] cocTraitLabels = new string[CocBonusEntries];
        private readonly string[] cocTraitPays = new string[CocBonusEntries];
        private readonly bool[] cocTraitPenalty = new bool[CocBonusEntries];

        private AvSegmented cocSideControl;
        private AvHazardBar cocEffect;
        private StrOrgChart cocChart;
        private StrNote cocEmpty;
        private StrDossier cocDossier;
        private StrNote cocFileNote;
        private AvSection cocLogSection;
        private StrLogBoard cocLog;
        private StrNote cocLogNote;
        private bool cocShowHostile;
        private int cocSelectedId = -1;

        private void ResetCoc()
        {
            Array.Clear(cocVisible, 0, cocVisible.Length);
            Array.Clear(cocIds, 0, cocIds.Length);
            Array.Clear(cocParents, 0, cocParents.Length);
            Array.Clear(cocOrder, 0, cocOrder.Length);
            Array.Clear(cocOrdered, 0, cocOrdered.Length);
            Array.Clear(cocLogTargetIds, 0, cocLogTargetIds.Length);
            cocLogSection = null;
            cocSideControl = null;
            cocEffect = null;
            cocChart = null;
            cocEmpty = cocFileNote = cocLogNote = null;
            cocDossier = null;
            cocLog = null;
            cocShowHostile = false;
            cocSelectedId = -1;
        }

        private void BuildCocPage(AvFlow p)
        {
            cocEmpty = p.Add(new StrNote(p.Content, AvIcon.UsersGroup), 1f);
            cocSideControl = AvSegmented.Strip(p.Content, new[] { "ALLIED", "HOSTILE" },
                () => cocShowHostile ? 1 : 0, i => { cocShowHostile = i == 1; nextRefresh = 0f; });
            cocEffect = new AvHazardBar(p.Content, "STAFF");
            p.Row(cocSideControl, cocEffect);
            cocSideControl.Options[0].Help = "Show the allied chain of command.";
            cocSideControl.Options[1].Help =
                "Show the opposing chain of command. A post stays unconfirmed until local intel has seen it.";
            cocChart = p.Add(new StrOrgChart(p.Content, SelectCoc));

            cocDossier = p.Add(new StrDossier(p.Content));
            cocFileNote = p.Add(new StrNote(p.Content, AvIcon.User), 1f);

            cocLogSection = p.Section(AvIcon.ListDetails, "STAFF LOG", "");
            cocLog = p.Add(new StrLogBoard(p.Content, CocLogRows, LogRowClicked, 4), 1f);
            cocLogNote = p.Add(new StrNote(p.Content, AvIcon.ListDetails));
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

        /// <summary>Show the board parts, or only the one note that says why there is no board.</summary>
        private void CocShowBoard(bool board)
        {
            cocEmpty.SetShown(!board);
            cocSideControl.SetShown(board);
            cocEffect.SetShown(board);
            cocChart.SetShown(board);
            cocLogSection.SetShown(board);
            if (!board)
            {
                cocDossier.SetShown(false);
                cocFileNote.SetShown(false);
                cocLog.SetShown(false);
                cocLogNote.SetShown(false);
            }
        }

        private void RefreshCoc()
        {
            if (cocEffect == null) return;

            bool available = highCommand != null && highCommand.Available;
            if (!available)
            {
                // No staff, no board: an empty page is not a reading, so the panel says so.
                CocShowBoard(false);
                cocEmpty.Set("NO STAFF BOARD", highCommand == null ? "NOT RUNNING" : highCommand.Status ?? "FORMING");
                for (int i = 0; i < CocLogRows; i++) cocLogTargetIds[i] = -1;
                if (highCommand != null) highCommand.Highlight(-1);
                cocSelectedId = -1;
                return;
            }

            CocShowBoard(true);
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
                string bonus = !view.IsFriendly && !view.IsKnown ? "" : FirstBonus(view.Bonus);
                cocNodes[i] = new StrOrgNode
                {
                    Id = view.Id,
                    ParentId = view.ParentId,
                    Tier = view.Tier,
                    Name = view.Name,
                    Rank = view.Rank,
                    Role = view.Role,
                    Status = StatusOf(view),
                    Tone = ToneOf(view),
                    Selected = view.Id == cocSelectedId,
                    // The card's copy is the office; the bonus its post carries rides the help line.
                    Help = (view.IsFriendly
                        ? "Open the file for " + view.Name + "  ·  " + view.Role
                        : view.IsKnown
                            ? "Confirmed contact: " + view.Name + "  ·  " + view.Role
                            : "Unconfirmed post: " + view.Name + " — no local intel.") +
                        (string.IsNullOrEmpty(bonus) ? "" : "  ·  " + bonus),
                };
            }
            cocChart.SetNodes(cocNodes, ordered);

            if (!cocShowHostile)
            {
                float effect = Mathf.Clamp01(highCommand.FriendlyCohesion);
                cocEffect.Set(effect,
                    ownCount == 0 ? "NO POSTS" : TheaterReadout.Percent(effect) + " · " + ownCount + (ownCount == 1 ? " POST" : " POSTS"),
                    ownCount == 0 ? AvState.Inert : effect >= 0.6f ? AvState.Ready : effect >= 0.3f ? AvState.Caution : AvState.Danger);
                cocEffect.Help = "STAFF EFFECT: how effective your chain of command is, and how many posts report. " +
                                 "Living commanders earn the faction a bonus; a loss interrupts it until a successor takes over.";
            }
            else
            {
                cocEffect.Set(enemyTotal > 0 ? enemyKnown / (float)enemyTotal : 0f,
                    enemyTotal == 0 ? "NO POSTS" : enemyKnown + " OF " + enemyTotal + " KNOWN",
                    enemyTotal == 0 ? AvState.Inert : enemyKnown == 0 ? AvState.Caution : AvState.Info);
                cocEffect.Help = "CONFIRMED: how many enemy posts local intel has identified. " +
                                 "An unconfirmed post keeps its personnel file sealed.";
            }

            RefreshCocLogList();

            if (selected == null) cocSelectedId = -1;
            CommanderView open = selectedOnActiveSide ? selected : null;
            // The map rings the post whose file is open, so a reader can find the general
            // they are reading about. An empty file rings nothing.
            highCommand.Highlight(open != null ? open.Id : -1);
            BindDossier(open);
        }

        private void RefreshCocLogList()
        {
            IReadOnlyList<CommanderLogLine> lines = cocShowHostile ? highCommand.HostileLog : highCommand.Log;
            int count = lines != null ? lines.Count : 0;
            int shown = 0;
            cocLog.Begin();
            for (int i = 0; i < count && shown < CocLogRows; i++)
            {
                CommanderLogLine line = lines[i];
                if (line == null) continue;
                bool clickable = LogTargetOnVisibleSide(line.TargetId);
                cocLog.Add(TheaterReadout.Age(line.Age), line.Text, LogState(line.Tone), clickable,
                    clickable ? "Open this post's file." : "Post is not on this side.");
                cocLogTargetIds[shown] = clickable ? line.TargetId : -1;
                shown++;
            }
            cocLog.End();
            for (int i = shown; i < CocLogRows; i++) cocLogTargetIds[i] = -1;

            cocLog.SetShown(shown > 0);
            cocLogNote.SetShown(shown == 0);
            if (shown == 0) cocLogNote.Set("NO TRAFFIC YET", "Posts, losses and pay events appear here.", AvState.Inert);
            cocLogSection.SetCaption(count == 0 ? "QUIET" : count + (count == 1 ? " ENTRY" : " ENTRIES"));
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

        /// <summary>Put the selected commander's file on the page, or one note when none is open.</summary>
        private void BindDossier(CommanderView view)
        {
            if (cocDossier == null) return;

            if (view == null)
            {
                cocDossier.SetShown(false);
                cocFileNote.SetShown(true);
                cocFileNote.Set("NO FILE OPEN", "Select a post on the chart or a line in the log to open its personnel file.");
                return;
            }

            cocDossier.SetShown(true);
            cocFileNote.SetShown(false);
            bool sealedFile = !view.IsFriendly && !view.IsKnown;
            cocDossier.Help = view.IsFriendly
                ? "Allied personnel file."
                : view.IsKnown
                    ? "Identity confirmed by local intel."
                    : "Identity unconfirmed: the file stays sealed until local intel identifies this post.";

            int traits = sealedFile ? 0 : ParseBonus(view.Bonus);
            cocDossier.Show(new StrDossier.Data
            {
                Name = view.Name,
                Rank = sealedFile ? "" : view.Rank,
                Role = view.Role,
                Status = StatusOf(view),
                Tone = ToneOf(view),
                Station = sealedFile ? "—" : Pretty(view.Location) ?? "—",
                Share = Percent(view),
                Share01 = Mathf.Clamp01(view.Weight),
                Intel = IntelOf(view),
                Bio = sealedFile ? "FILE SEALED — local intel has not confirmed this post." : (view.Bio ?? ""),
                TraitLabels = cocTraitLabels,
                TraitPays = cocTraitPays,
                TraitPenalty = cocTraitPenalty,
                TraitCount = traits,
                Sealed = sealedFile,
                Portrait = view.Portrait,
            });
        }

        private static string IntelOf(CommanderView view)
        {
            if (view.IsFriendly) return "OWN STAFF";
            if (!view.IsKnown) return "NONE";
            return view.IntelAge >= 0f ? TheaterReadout.Age(view.IntelAge).ToUpperInvariant() + " AGO" : "CONFIRMED";
        }

        /// <summary>Split the bonus line into at most <see cref="CocBonusEntries"/> trait rows; returns how many.</summary>
        private int ParseBonus(string bonus)
        {
            if (string.IsNullOrEmpty(bonus) || bonus == "NO STAFF BONUS") return 0;
            int used = 0, start = 0;
            while (used < CocBonusEntries && start <= bonus.Length)
            {
                int end = bonus.IndexOf(" · ", start, StringComparison.Ordinal);
                string entry = end < 0 ? bonus.Substring(start) : bonus.Substring(start, end - start);
                SplitBonus(entry, out string label, out string pay);
                cocTraitLabels[used] = label;
                cocTraitPays[used] = pay;
                cocTraitPenalty[used] = !string.IsNullOrEmpty(pay) && pay[0] == '-';
                used++;
                if (end < 0) break;
                start = end + 3;
            }
            return used;
        }

        /// <summary>The first entry of the bonus line, for the card's help.</summary>
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

        private static StrTone ToneOf(CommanderView view)
        {
            if (view.IsKia) return StrTone.Kia;
            if (!view.IsFriendly && !view.IsKnown) return StrTone.Unconfirmed;
            if (view.Alert) return StrTone.Alert;
            if (view.InTransit) return StrTone.Info;
            if (view.Disrupted) return StrTone.Caution;
            return view.IsFriendly ? StrTone.Allied : StrTone.Hostile;
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
