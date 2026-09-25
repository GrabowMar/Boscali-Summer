using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        // The board is a matrix: one row per grade, one column per qualification. All four
        // lanes fit side by side, so picking compares classes instead of scrolling a list.
        // Geometry lives in SkillBoardLayout (which a test pins); this file only draws it.
        private const string SkillIdleTitle = "SELECT A CELL";
        private const string SkillHint =
            "Scroll grades. Compare lanes at the same tier, then unlock one pick.";

        private TMP_Text skillStripTitle;
        private TMP_Text skillStripDetail;
        private TMP_Text skillBudgetNote;
        private AvButton skillConfirmButton;
        private string skillIdleTitle = SkillIdleTitle;
        private string skillIdleDetail = SkillHint;
        private readonly List<SkillRow> skillRows = new List<SkillRow>(PerkCatalog.All.Length);
        private readonly List<SkillBranchRow> skillBranches = new List<SkillBranchRow>(8);
        private byte? skillAwaitingConfirmation;

        private void ResetSkillRows()
        {
            skillRows.Clear();
            skillBranches.Clear();
            skillStripTitle = null;
            skillStripDetail = null;
            skillConfirmButton = null;
            skillIdleTitle = SkillIdleTitle;
            skillIdleDetail = SkillHint;
            skillAwaitingConfirmation = null;
        }

        private void CancelSkillConfirmation() => skillAwaitingConfirmation = null;

        // ---- SKILLS page -----------------------------------------------------------------

        /// <summary>One qualification: its grades in catalogue order, grade 1 first.</summary>
        private sealed class SkillLane
        {
            public string Name;
            public readonly List<PerkView> Nodes = new List<PerkView>(PerkCatalog.MaximumDepth);
        }

        private void BuildSkillsPage(RectTransform page, Rect body)
        {
            PerkView[] perks = Progress != null ? Progress.GetPerks() : Array.Empty<PerkView>();
            if (perks.Length == 0)
            {
                PageRail(page, new Rect(body.x, body.y, 3f, body.height));
                float emptyX = body.x + SpineInset;
                float emptyWidth = body.width - SpineInset;
                AvKit.TacticalCard(page, new Rect(emptyX, body.y, emptyWidth, 92f), AvTheme.RailInert);
                PlainLabel(page, new Rect(emptyX + 12f, body.y - 12f, emptyWidth - 24f, 20f),
                    "NO QUALIFICATIONS AVAILABLE", "row-name");
                PlainLabel(page, new Rect(emptyX + 12f, body.y - 40f, emptyWidth - 24f, 34f),
                    "This host has not configured a qualification catalog.", "row-sub");
                return;
            }

            var lanes = new List<SkillLane>(4);
            for (int i = 0; i < perks.Length; i++)
                LaneOf(lanes, perks[i].Branch).Nodes.Add(perks[i]);
            int grades = 0;
            for (int l = 0; l < lanes.Count; l++)
                if (lanes[l].Nodes.Count > grades) grades = lanes[l].Nodes.Count;

            // The selected-grade strip is pinned to the foot of the page, so the commit
            // control can never scroll away from the cell it commits. Only the board scrolls,
            // and only when the panel is too short to hold it.
            Rect strip = new Rect(body.x, body.y - body.height + SkillBoardLayout.DetailHeight,
                body.width, SkillBoardLayout.DetailHeight);
            Rect listArea = new Rect(body.x, body.y, body.width,
                Mathf.Max(0f, body.height - SkillBoardLayout.DetailHeight - SkillBoardLayout.Gap));

            float rowHeight = SkillBoardLayout.RowHeight(listArea.height, grades);
            float contentHeight = SkillBoardLayout.ContentHeight(rowHeight, grades);
            // A body taller than the board's readable maximum would leave the lower third
            // of the page under the last grade row; spread what is left over the rows, so
            // the board ends on the pinned strip instead of on blank glass.
            if (contentHeight < listArea.height)
            {
                rowHeight += (listArea.height - contentHeight) / Mathf.Max(1, grades);
                contentHeight = SkillBoardLayout.ContentHeight(rowHeight, grades);
            }

            RectTransform parent = AvScreen.Scroll(page, listArea, contentHeight, out Rect area);
            PageRail(parent, new Rect(area.x, area.y, 3f, area.height));
            float x = area.x + SpineInset;
            float width = area.width - SpineInset;
            float y = area.y;

            y = DrawPageHeader(parent, x, y, width,
                "QUALIFICATION BOARD", "HOST PROGRESSION", SqdMark.Combat);
            y = DrawSectionTitle(parent, x, y, width, "AVAILABLE QUALIFICATIONS",
                BudgetNote(), band: false, out skillBudgetNote);

            float cellWidth = SkillBoardLayout.CellWidth(width, lanes.Count);
            DrawBoardLegend(parent, x, y, width);
            y -= SkillBoardLayout.LegendHeight;
            y = DrawLaneHeaders(parent, x, y, width, cellWidth, lanes);
            for (int g = 0; g < grades; g++) DrawGradeRow(parent, x, y, width, cellWidth, rowHeight, g, lanes);

            BuildSkillStrip(page, strip);
        }

        /// <summary>The budget as the board's own note: how many picks are unspent, and the wait.</summary>
        private string BudgetNote()
        {
            IProgressionView view = Progress;
            if (progression.BypassRequirements) return "DEBUG BYPASS · EVERY GRADE OPEN";

            int available = view.AvailablePoints;
            string picks = available + (available == 1 ? " PICK UNSPENT · " : " PICKS UNSPENT · ");
            if (view.EarnedPoints >= view.MaximumPoints) return picks + "GRADE LADDER COMPLETE";

            int remaining = PerkPoints.RemainingToNext(MissionScore(), view.ScorePerPoint);
            return remaining < 0 ? picks + "GRADE LADDER COMPLETE" : picks + remaining + " TO NEXT GRADE";
        }

        private int MissionScore()
        {
            int score = Progress.Score;
            if (squad != null && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
                score = Math.Max(0, score - squad.GetScoreOrigin(PlayerIdentity.Of(local)));
            return score;
        }

        private static SkillLane LaneOf(List<SkillLane> lanes, string name)
        {
            for (int i = 0; i < lanes.Count; i++)
                if (string.Equals(lanes[i].Name, name, StringComparison.Ordinal)) return lanes[i];
            var lane = new SkillLane { Name = name };
            lanes.Add(lane);
            return lane;
        }

        /// <summary>The lane name over its committed count; the column the grades hang under.</summary>
        private float DrawLaneHeaders(
            RectTransform parent, float x, float y, float width, float cellWidth, List<SkillLane> lanes)
        {
            for (int l = 0; l < lanes.Count; l++)
            {
                float laneX = SkillBoardLayout.CellX(x, cellWidth, l);
                var header = new SkillBranchRow();
                for (int n = 0; n < lanes[l].Nodes.Count; n++) header.Ids.Add(lanes[l].Nodes[n].Id);
                header.Caption = PlainLabel(parent, new Rect(laneX, y, cellWidth, 14f),
                    lanes[l].Name, "section-title");
                header.Note = PlainLabel(parent, new Rect(laneX, y - 15f, cellWidth, 13f),
                    "", "row-sub");
                header.Note.enableWordWrapping = false;
                skillBranches.Add(header);
            }

            AvKit.Rule(parent, new Rect(x + SkillBoardLayout.Gutter, y - SkillBoardLayout.LaneHeaderHeight + 2f,
                width - SkillBoardLayout.Gutter, 1f), AvTheme.Frame.WithAlpha(0.45f));
            return y - SkillBoardLayout.LaneHeaderHeight - SkillBoardLayout.Gap;
        }

        /// <summary>
        /// One grade across every lane: the gutter names the grade and each cell is a
        /// qualification's node, so reading across shows what the same pick buys in each class.
        /// </summary>
        private void DrawGradeRow(
            RectTransform parent, float x, float y, float width, float cellWidth,
            float rowHeight, int grade, List<SkillLane> lanes)
        {
            float rowTop = y - grade * (rowHeight + SkillBoardLayout.Gap);
            bool maximum = grade == PerkCatalog.MaximumDepth - 1;
            float labelTop = rowTop - (rowHeight - 18f) * 0.5f + (maximum ? 7f : 0f);
            TMP_Text label = PlainLabel(parent,
                new Rect(x, labelTop, SkillBoardLayout.Gutter, 18f), (grade + 1).ToString(),
                "kv-value");
            label.alignment = TextAlignmentOptions.Center;
            if (maximum)
            {
                TMP_Text cap = PlainLabel(parent,
                    new Rect(x, labelTop - 19f, SkillBoardLayout.Gutter, 11f), "MAX", "row-sub");
                cap.alignment = TextAlignmentOptions.Center;
                cap.color = AvTheme.Dim;
            }

            for (int l = 0; l < lanes.Count; l++)
            {
                if (grade >= lanes[l].Nodes.Count) continue;
                float cellX = SkillBoardLayout.CellX(x, cellWidth, l);
                skillRows.Add(DrawSkillCell(parent,
                    new Rect(cellX, rowTop, cellWidth, rowHeight), lanes[l].Nodes[grade]));
            }

            AvKit.Rule(parent, new Rect(x, rowTop - rowHeight, width, 1f),
                AvTheme.Hairline.WithAlpha(0.10f));
        }

        /// <summary>
        /// A node: a rail for its state, a glyph for what it is, its name, and the state in
        /// words. The whole cell is the hit target - one pinned CONFIRM commits the selection.
        /// </summary>
        private SkillRow DrawSkillCell(RectTransform parent, Rect area, PerkView view)
        {
            var cell = new SkillRow { Id = view.Id };
            cell.Fill = AvKit.Panel(parent, area, Color.clear);
            cell.Hover = AvKit.Panel(parent, area, Color.clear);
            cell.Frame = AvKit.Outline(parent, area, AvTheme.Hairline);
            cell.Rail = AvKit.Rule(parent, new Rect(area.x, area.y, 3f, area.height), AvTheme.RailInert);

            PerkDefinition definition = PerkDefinitionOf(view);
            cell.Icon = SqdGlyph.Create(parent, new Rect(area.x + 9f, area.y - 9f, 15f, 15f),
                SqdMarks.FromKey(definition.Icon));
            // Two lines: a composed name like ENGINEER QUALIFICATION is wider than the cell
            // at one line, and the board never drops half a pick's own name.
            cell.Name = PlainLabel(parent,
                new Rect(area.x + 8f, area.y - 25f, area.width - 16f,
                    Mathf.Clamp(area.height - 44f, 24f, 30f)),
                CellName(view, definition), "row-main");
            // A cell this narrow would otherwise break a long word like SURVEILLANCE mid-word;
            // the paging grid shrinks its labels the same way.
            cell.Name.enableAutoSizing = true;
            cell.Name.fontSizeMax = cell.Name.fontSize;
            cell.Name.fontSizeMin = AvTokens.FontMicro;
            // The state line is one auto-sized line at the foot of the cell: an effect label
            // like "+15% COOLDOWN" shrinks to the micro floor and overflows its box rather
            // than silently dropping the tail of its word.
            cell.State = PlainLabel(parent,
                new Rect(area.x + 8f, area.y + SkillBoardLayout.StateTop(area.height),
                    area.width - 16f, SkillBoardLayout.StateHeight),
                "", "row-sub");
            cell.State.enableWordWrapping = false;
            cell.State.enableAutoSizing = true;
            cell.State.fontSizeMax = cell.State.fontSize;
            cell.State.fontSizeMin = AvTokens.FontMicro;
            cell.State.overflowMode = TextOverflowModes.Overflow;

            byte id = view.Id;
            cell.Select = AvKit.HitButton(parent, area, () => ClickSkill(id));
            cell.Select.SetRowHighlight(cell.Hover, Color.clear, HoverFill());
            return cell;
        }

        /// <summary>
        /// A tool sells its code - STK, SAT, EW, ENG are the support codes the OPS page and the
        /// wing badges already use - and its state line says TOOL or HELD. A passive grade gets
        /// its own name, which is short enough for two lines at this width.
        /// </summary>
        private static string CellName(PerkView view, PerkDefinition definition) =>
            definition.IsTool
                ? PerkCatalog.CapabilityCode(definition.Capability)
                : view.Name.ToUpperInvariant();

        /// <summary>
        /// The rail's key, drawn as the table's first line: the board leans on the avionics
        /// rule that state lives on the rail, so it teaches that language once, beside the
        /// rails it explains, instead of leaving it to be guessed at the foot of the page.
        /// </summary>
        private static void DrawBoardLegend(RectTransform parent, float x, float y, float width)
        {
            const float key = 84f;
            PlainLabel(parent, new Rect(x, y - 13f, key - 6f, 12f), "RAIL STATE", "form-key");

            string[] words = { "HELD", "PICK", "SELECTED", "LOCKED" };
            Color[] colours =
            {
                AvTheme.RailReady, AvTheme.RailInfo, AvTheme.RailCaution, AvTheme.RailInert,
            };
            float pitch = (width - key) / words.Length;
            for (int i = 0; i < words.Length; i++)
            {
                float slotX = x + key + i * pitch;
                AvKit.Rule(parent, new Rect(slotX, y - 12f, 3f, 12f), colours[i]);
                TMP_Text word = PlainLabel(parent, new Rect(slotX + 9f, y - 13f, pitch - 12f, 12f),
                    words[i], "row-sub");
                word.enableWordWrapping = false;
            }
        }

        /// <summary>The pinned strip: what a click means, what is armed, and the one commit.</summary>
        private void BuildSkillStrip(RectTransform parent, Rect area)
        {
            AvKit.Panel(parent, new Rect(area.x, area.y, area.width, area.height - 4f),
                AvTheme.SurfaceRaised);
            AvKit.Outline(parent, new Rect(area.x, area.y, area.width, area.height - 4f),
                AvTheme.Frame.WithAlpha(0.75f));
            AvKit.Rule(parent, new Rect(area.x, area.y, 3f, area.height - 4f), AvTheme.RailInfo);

            float textWidth = Mathf.Max(0f, area.width - 150f);
            skillStripTitle = Fitted(PlainLabel(parent, new Rect(area.x + 12f, area.y - 7f, textWidth, 15f),
                skillIdleTitle, "row-name"));
            skillStripDetail = PlainLabel(parent, new Rect(area.x + 12f, area.y - 26f, textWidth, 24f),
                skillIdleDetail, "row-sub");
            // Two wrapped lines: the confirm strip is the one place the whole sentence is
            // readable, so it shrinks to the micro floor and overflows rather than truncating
            // the reason a pick cannot be committed.
            skillStripDetail.enableAutoSizing = true;
            skillStripDetail.fontSizeMin = AvTokens.FontMicro;
            skillStripDetail.fontSizeMax = skillStripDetail.fontSize;
            skillStripDetail.overflowMode = TextOverflowModes.Overflow;

            skillConfirmButton = AvStyled.Button(parent,
                new Rect(area.x + area.width - 138f, area.y - 14f, 128f, 26f),
                "UNLOCK SELECTED", "btn", CommitSelected, AvButtonStyle.Primary);
            skillConfirmButton.SetEnabled(false);
            skillConfirmButton.WithTooltip("Commit the selected grade. One pick, no undo.");
        }

        private void CommitSelected()
        {
            if (skillAwaitingConfirmation.HasValue) CommitSkill(skillAwaitingConfirmation.Value);
        }

        private static bool IsUnlocked(PerkView[] perks, byte id) =>
            TryFind(perks, id, out PerkView view) && view.Unlocked;

        private static PerkDefinition PerkDefinitionOf(PerkView view)
        {
            for (int i = 0; i < PerkCatalog.All.Length; i++)
                if (PerkCatalog.All[i].Id == view.Id) return PerkCatalog.All[i];
            return default;
        }

        /// <summary>
        /// The one word a grade's state is called; the strip and the tooltip use it. Kept
        /// to a word per state so no cell or lane caption has to ellipsise it.
        /// </summary>
        private static string StateWord(PerkView perk)
        {
            if (perk.Unlocked) return PerkDefinitionOf(perk).IsTool ? "HELD" : "ACTIVE";
            if (perk.Affordable) return PerkDefinitionOf(perk).IsTool ? "TOOL" : "PICK";
            return BlockWord(perk);
        }

        /// <summary>
        /// The cell's own line: why it is shut, else what it is. A passive grade sells its
        /// effect here - "+15% COMBAT" - so the board reads as a comparison of what each pick
        /// buys instead of a wall of identical "PICK" labels. A tool has no multiplier to sell,
        /// so it keeps its state word.
        /// </summary>
        private static string CellLine(PerkView perk, byte id)
        {
            if (perk.Unlocked || perk.Affordable)
                return PerkDefinitionOf(perk).IsTool
                    ? (perk.Unlocked ? "HELD" : "TOOL")
                    : PerkCatalog.EffectLabel(id);
            return BlockWord(perk);
        }

        private static string BlockWord(PerkView perk)
        {
            if (perk.Block == PerkView.BlockCap) return "CLOSED";
            if (perk.Block == PerkView.BlockGrade) return "GRADE FIRST";
            return "NO PICK";
        }

        private void SelectSkill(byte id)
        {
            if (progression == null) return;
            if (Progress.UnlockPending ||
                !TryFind(Progress.GetPerks(), id, out PerkView view) ||
                view.Unlocked || !view.Affordable) return;

            skillAwaitingConfirmation = id;
            nextRefresh = 0f;
        }

        private void CommitSkill(byte id)
        {
            if (progression == null || Progress.UnlockPending ||
                skillAwaitingConfirmation != id ||
                !TryFind(Progress.GetPerks(), id, out PerkView view) ||
                view.Unlocked || !view.Affordable) return;

            skillAwaitingConfirmation = null;
            skillIdleTitle = SkillIdleTitle;
            skillIdleDetail = SkillHint;
            Progress.RequestUnlock(id);
            nextRefresh = 0f;
        }

        /// <summary>A cell click arms the pick, or says why the host would refuse it.</summary>
        private void ClickSkill(byte id)
        {
            if (progression == null || Progress.UnlockPending) return;
            if (!TryFind(Progress.GetPerks(), id, out PerkView view)) return;

            skillIdleTitle = view.Name.ToUpperInvariant() + " · " + StateWord(view);
            skillIdleDetail = view.Unlocked || view.Affordable
                ? view.Description
                : BlockReason(view);
            nextRefresh = 0f;
            if (!view.Unlocked && view.Affordable) SelectSkill(id);
        }

        private static string BlockReason(PerkView view)
        {
            if (view.Block == PerkView.BlockCap)
                return "This career already carries its two support tools, so the lane stays closed.";
            if (view.Block == PerkView.BlockGrade)
                return "The grade before it is not committed yet.";
            return "No unspent pick: fly, capture or rescue to earn the next grade.";
        }

        private static bool TryFind(PerkView[] perks, byte id, out PerkView view)
        {
            for (int i = 0; i < perks.Length; i++)
            {
                if (perks[i].Id == id)
                {
                    view = perks[i];
                    return true;
                }
            }
            view = default;
            return false;
        }

        // ---- SKILLS refresh --------------------------------------------------------------

        private void RefreshSkillsPage()
        {
            if (skillStripTitle == null || progression == null) return;

            IProgressionView view = Progress;
            string budget = BudgetNote();
            if (skillBudgetNote != null && skillBudgetNote.text != budget) skillBudgetNote.text = budget;
            bool requestPending = view.UnlockPending;
            if (requestPending) skillAwaitingConfirmation = null;

            PerkView[] perks = view.GetPerks();
            for (int i = 0; i < skillRows.Count; i++) PaintSkill(skillRows[i], perks);
            for (int i = 0; i < skillBranches.Count; i++) PaintLane(skillBranches[i], perks);

            PerkView selected = default;
            bool hasSelection = skillAwaitingConfirmation.HasValue &&
                TryFind(perks, skillAwaitingConfirmation.Value, out selected);
            bool canConfirm = hasSelection && !requestPending &&
                !selected.Unlocked && selected.Affordable;
            if (skillConfirmButton != null)
            {
                // Armed reads as the latched wash and the word CONFIRM; no solid plate.
                skillConfirmButton.SetEnabled(canConfirm);
                skillConfirmButton.SetLatched(canConfirm);
                skillConfirmButton.ClearCustomColors();
            }

            if (requestPending)
            {
                skillStripTitle.text = "WAITING FOR THE HOST";
                skillStripTitle.color = AvTheme.RailCaution;
                skillStripDetail.text = "The pick is sent. The host answers on the next tick.";
                skillStripDetail.color = AvTheme.Dim;
            }
            else if (hasSelection)
            {
                PerkDefinition definition = PerkDefinitionOf(selected);
                skillStripTitle.text = "G" + definition.Grade + " · " + selected.Name.ToUpperInvariant();
                skillStripTitle.color = AvTheme.RailCaution;
                skillStripDetail.text = selected.Description;
                skillStripDetail.color = AvTheme.TextPrimary;
            }
            else
            {
                skillStripTitle.text = skillIdleTitle;
                skillStripTitle.color = AvTheme.TextPrimary;
                skillStripDetail.text = skillIdleDetail;
                skillStripDetail.color = AvTheme.Dim;
            }
        }

        /// <summary>Rail for state, glyph for identity, words for anyone the colour misses.</summary>
        private void PaintSkill(SkillRow row, PerkView[] perks)
        {
            if (!TryFind(perks, row.Id, out PerkView perk)) return;

            bool armed = skillAwaitingConfirmation == row.Id && !perk.Unlocked && perk.Affordable;
            string word = armed ? "SELECTED" : CellLine(perk, row.Id);
            Color rail;
            if (perk.Unlocked) rail = AvTheme.RailReady;
            else if (armed) rail = AvTheme.RailCaution;
            else if (perk.Affordable) rail = AvTheme.RailInfo;
            else rail = AvTheme.RailInert;

            bool live = perk.Unlocked || perk.Affordable || armed;
            Color tone = perk.Unlocked ? AvTheme.RailReady
                : armed ? AvTheme.RailCaution
                : perk.Affordable ? AvTheme.TextPrimary : AvTheme.Dim;
            Color frame = live ? AvTheme.Hairline.WithAlpha(0.45f) : AvTheme.Hairline.WithAlpha(0.20f);

            row.State.text = word;
            row.State.color = tone;
            row.Name.color = live ? AvTheme.TextPrimary : AvTheme.Dim;
            if (row.Icon != null) row.Icon.color = tone;
            row.Rail.color = rail;
            row.Fill.color = perk.Unlocked ? AvTheme.SurfaceInert
                : armed ? AvTheme.RailCaution.WithAlpha(0.12f) : Color.clear;
            for (int f = 0; f < row.Frame.Length; f++) row.Frame[f].color = frame;
            // A shut cell answers "why not" on hover instead of only after a click.
            row.Select.WithTooltip(live
                ? perk.Name + " — " + perk.Description
                : perk.Name + " — " + BlockReason(perk));
        }

        private static void PaintLane(SkillBranchRow lane, PerkView[] perks)
        {
            int taken = 0;
            for (int n = 0; n < lane.Ids.Count; n++)
                if (IsUnlocked(perks, lane.Ids[n])) taken++;

            // A lane whose tool the host refuses on the career cap is closed for good here.
            bool toolHeld = lane.Ids.Count > 0 && IsUnlocked(perks, lane.Ids[0]);
            bool closed = !toolHeld && lane.Ids.Count > 0 &&
                TryFind(perks, lane.Ids[0], out PerkView tool) && tool.Block == PerkView.BlockCap;

            lane.Note.text = taken + "/" + lane.Ids.Count + " " + (toolHeld ? "HELD"
                : closed ? "CLOSED" : "OPEN");
            lane.Note.color = toolHeld ? AvTheme.RailReady : closed ? AvTheme.Dim : AvTheme.TextPrimary;
            lane.Caption.color = closed ? AvTheme.Dim : AvTheme.TextPrimary;
        }
    }
}
