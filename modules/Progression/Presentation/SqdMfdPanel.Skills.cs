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
            "One pick per grade. A grade needs the grade before it, and a career carries two tools.";

        private TMP_Text skillStripTitle;
        private TMP_Text skillStripDetail;
        private TMP_Text skillBudgetNote;
        private AvButton skillConfirmButton;
        private string skillIdleTitle = SkillIdleTitle;
        private string skillIdleDetail = SkillHint;
        private readonly List<SkillRow> skillRows = new List<SkillRow>(PerkCatalog.All.Length);
        private readonly List<SkillBranchRow> skillBranches = new List<SkillBranchRow>(8);
        private byte? skillAwaitingConfirmation;
        private float skillConfirmationUntil;

        private void ResetSkillRows()
        {
            skillRows.Clear();
            skillBranches.Clear();
            skillStripTitle = null;
            skillStripDetail = null;
            skillBudgetNote = null;
            skillConfirmButton = null;
            skillIdleTitle = SkillIdleTitle;
            skillIdleDetail = SkillHint;
            skillAwaitingConfirmation = null;
            skillConfirmationUntil = 0f;
        }

        private void CancelSkillConfirmation()
        {
            skillAwaitingConfirmation = null;
            skillConfirmationUntil = 0f;
        }

        // ---- SKILLS page -----------------------------------------------------------------

        /// <summary>One qualification: its grades in catalogue order, grade 1 first.</summary>
        private sealed class SkillLane
        {
            public string Name;
            public readonly List<PerkView> Nodes = new List<PerkView>(PerkCatalog.MaximumDepth);
        }

        private void BuildSkillsPage(RectTransform page, Rect body)
        {
            clause = 0;
            PerkView[] perks = Progress != null ? Progress.GetPerks() : Array.Empty<PerkView>();
            if (perks.Length == 0)
            {
                DossierSpine(page, new Rect(body.x, body.y, 3f, body.height));
                PlainLabel(page,
                    new Rect(body.x + SpineInset, body.y, body.width - SpineInset, 40f),
                    "No pilot qualifications are configured on this host.", "row-sub");
                return;
            }

            var lanes = new List<SkillLane>(4);
            for (int i = 0; i < perks.Length; i++)
                LaneOf(lanes, perks[i].Branch).Nodes.Add(perks[i]);
            int grades = 0;
            for (int l = 0; l < lanes.Count; l++)
                if (lanes[l].Nodes.Count > grades) grades = lanes[l].Nodes.Count;

            // The selected-grade strip is pinned to the foot of the sheet, so the commit
            // control can never scroll away from the cell it commits. Only the board scrolls,
            // and only when the panel is too short to hold it.
            Rect strip = new Rect(body.x, body.y - body.height + SkillBoardLayout.DetailHeight,
                body.width, SkillBoardLayout.DetailHeight);
            Rect listArea = new Rect(body.x, body.y, body.width,
                Mathf.Max(0f, body.height - SkillBoardLayout.DetailHeight - SkillBoardLayout.Gap));

            float rowHeight = SkillBoardLayout.RowHeight(listArea.height, grades);
            float contentHeight = SkillBoardLayout.ContentHeight(rowHeight, grades);

            RectTransform parent = AvScreen.Scroll(page, listArea, contentHeight, out Rect area);
            DossierSpine(parent, new Rect(area.x, area.y, 3f, area.height));
            float x = area.x + SpineInset;
            float width = area.width - SpineInset;
            float y = area.y;

            y = DrawFileHeader(parent, x, y, width, "FORM SQD-2 · SHEET 2 OF 4",
                "QUALIFICATION RECORD", "HOST COPY");
            y = DrawSectionTitle(parent, x, y, width, PerkCatalog.Qualifications,
                BudgetNote(), band: false, out skillBudgetNote);

            float cellWidth = SkillBoardLayout.CellWidth(width, lanes.Count);
            y = DrawLaneHeaders(parent, x, y, width, cellWidth, lanes);
            for (int g = 0; g < grades; g++) DrawGradeRow(parent, x, y, width, cellWidth, rowHeight, g, lanes);
            y -= grades * (rowHeight + SkillBoardLayout.Gap);

            y = DrawSharedFooter(parent, x, y, width);
            DrawBoardLegend(parent, x, y, width);
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
            return remaining < 0 ? picks + "GRADE LADDER COMPLETE" : picks + remaining + " SCORE TO NEXT GRADE";
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
                var header = new SkillBranchRow { Name = lanes[l].Name };
                for (int n = 0; n < lanes[l].Nodes.Count; n++) header.Ids.Add(lanes[l].Nodes[n].Id);
                header.Caption = PlainLabel(parent, new Rect(laneX, y, cellWidth, 14f),
                    lanes[l].Name, "section-title");
                header.Note = PlainLabel(parent, new Rect(laneX, y - 15f, cellWidth, 13f),
                    "", "row-sub");
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
            float labelTop = rowTop - (rowHeight - 14f) * 0.5f;
            TMP_Text label = PlainLabel(parent,
                new Rect(x, labelTop, SkillBoardLayout.Gutter - 8f, 14f), "G" + (grade + 1),
                "section-title-note");
            label.alignment = TextAlignmentOptions.MidlineRight;
            if (grade == PerkCatalog.MaximumDepth - 1)
            {
                TMP_Text cap = PlainLabel(parent,
                    new Rect(x, labelTop - 13f, SkillBoardLayout.Gutter - 8f, 12f), "CAP", "row-sub");
                cap.alignment = TextAlignmentOptions.MidlineRight;
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
            cell.Name = PlainLabel(parent,
                new Rect(area.x + 8f, area.y - 26f, area.width - 16f,
                    Mathf.Clamp(area.height - 44f, 14f, 28f)),
                CellName(view, definition), "row-main");
            // A cell this narrow would otherwise break a long word like SURVEILLANCE mid-word;
            // the paging grid shrinks its labels the same way.
            cell.Name.enableAutoSizing = true;
            cell.Name.fontSizeMax = cell.Name.fontSize;
            cell.Name.fontSizeMin = AvTokens.FontMicro;
            cell.State = PlainLabel(parent,
                new Rect(area.x + 8f, area.y - area.height + 16f, area.width - 16f, 13f),
                "", "row-sub");

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
        /// The shared ace-skill marks, drawn as the WINGS cards draw them, so the same four
        /// symbols read the same in both places. Hover carries the full description.
        /// </summary>
        private float DrawSharedFooter(RectTransform parent, float x, float y, float width)
        {
            float next = DrawSectionTitle(parent, x, y, width, "SHARED COMBAT SKILLS",
                "AI & ACES · WING COMMAND", band: false);
            float pitch = width / AceSkillCatalog.MaximumSkills;
            for (int i = 0; i < AceSkillCatalog.MaximumSkills; i++)
            {
                AceSkillDefinition skill = AceSkillCatalog.All[i];
                float markX = x + i * pitch + pitch * 0.5f - 18f;
                Glyph(parent, new Rect(markX + 11f, next - 2f, 14f, 14f),
                    HuntMark.Toughness + i, AvTheme.RailCaution);
                TMP_Text code = PlainLabel(parent, new Rect(markX, next - 17f, 36f, 13f),
                    skill.Code, "row-sub");
                code.alignment = TextAlignmentOptions.Center;
                AvKit.HitButton(parent, new Rect(markX, next - 20f, 36f, 34f), () => { })
                    .WithTooltip(skill.Name + " — " + skill.Description);
            }
            return next - 22f;
        }

        /// <summary>
        /// The rail's key. The board leans on the avionics rule that state lives on the rail,
        /// so it also has to teach that language once rather than leave it to be guessed.
        /// </summary>
        private static void DrawBoardLegend(RectTransform parent, float x, float y, float width)
        {
            const float key = 78f;
            PlainLabel(parent, new Rect(x, y - 13f, key - 6f, 12f), "RAIL STATE", "form-key");

            string[] words = { "HELD", "PICKABLE", "SELECTED", "LOCKED" };
            Color[] colours =
            {
                AvTheme.RailReady, AvTheme.RailInfo, AvTheme.RailCaution,
                AvTheme.RailInert.WithAlpha(0.45f),
            };
            float pitch = (width - key) / words.Length;
            for (int i = 0; i < words.Length; i++)
            {
                float slotX = x + key + i * pitch;
                AvKit.Rule(parent, new Rect(slotX, y - 12f, 3f, 12f), colours[i]);
                PlainLabel(parent, new Rect(slotX + 9f, y - 13f, pitch - 12f, 12f), words[i], "row-sub");
            }
        }

        /// <summary>The pinned strip: what a click means, what is armed, and the one commit.</summary>
        private void BuildSkillStrip(RectTransform parent, Rect area)
        {
            AvStyled.Box(parent, new Rect(area.x - 6f, area.y + 4f, area.width + 6f, area.height - 4f),
                "section band");
            AvStyled.SpineTick(parent, area.x - SpineInset + 3f, area.y - 7f);

            float textWidth = Mathf.Max(0f, area.width - 122f);
            skillStripTitle = PlainLabel(parent, new Rect(area.x + 6f, area.y + 2f, textWidth, 15f),
                skillIdleTitle, "row-name");
            skillStripDetail = PlainLabel(parent, new Rect(area.x + 6f, area.y - 17f, textWidth, 13f),
                skillIdleDetail, "row-sub");
            skillStripDetail.enableWordWrapping = false;
            skillStripDetail.overflowMode = TextOverflowModes.Ellipsis;

            skillConfirmButton = AvStyled.Button(parent,
                new Rect(area.x + area.width - 110f, area.y - 9f, 104f, 26f),
                "CONFIRM", "btn", CommitSelected, AvButtonStyle.Primary);
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

        /// <summary>The one word a grade's state is called, shared by the cell and the strip.</summary>
        private static string StateWord(PerkView perk)
        {
            bool tool = PerkDefinitionOf(perk).IsTool;
            if (perk.Unlocked) return tool ? "HELD" : "ACTIVE";
            if (perk.Affordable) return tool ? "TOOL" : "PICK";
            if (perk.Block == PerkView.BlockCap) return "CLOSED · CAREER CAP";
            if (perk.Block == PerkView.BlockGrade) return "GRADE FIRST";
            return "NO PICK";
        }

        /// <summary>Cell wording stays short; the strip has room for the long form.</summary>
        private static string CellWord(PerkView perk)
        {
            string word = StateWord(perk);
            return word == "CLOSED · CAREER CAP" ? "CLOSED" : word;
        }

        private void SelectSkill(byte id)
        {
            if (progression == null) return;
            if (Progress.UnlockPending ||
                !TryFind(Progress.GetPerks(), id, out PerkView view) ||
                view.Unlocked || !view.Affordable) return;

            skillAwaitingConfirmation = id;
            skillConfirmationUntil = Time.unscaledTime + 6f;
            nextRefresh = 0f;
        }

        private void CommitSkill(byte id)
        {
            if (progression == null || Progress.UnlockPending ||
                skillAwaitingConfirmation != id || Time.unscaledTime > skillConfirmationUntil ||
                !TryFind(Progress.GetPerks(), id, out PerkView view) ||
                view.Unlocked || !view.Affordable) return;

            skillAwaitingConfirmation = null;
            skillConfirmationUntil = 0f;
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
            if (skillAwaitingConfirmation.HasValue && Time.unscaledTime > skillConfirmationUntil)
            {
                skillAwaitingConfirmation = null;
                skillConfirmationUntil = 0f;
            }

            IProgressionView view = Progress;
            string budget = BudgetNote();
            if (skillBudgetNote != null && skillBudgetNote.text != budget) skillBudgetNote.text = budget;
            bool bypass = progression.BypassRequirements;
            bool requestPending = view.UnlockPending;
            if (requestPending)
            {
                skillAwaitingConfirmation = null;
                skillConfirmationUntil = 0f;
            }

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
                skillConfirmButton.SetEnabled(canConfirm);
                skillConfirmButton.SetLatched(canConfirm);
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
                skillStripTitle.text = bypass ? "DEBUG BYPASS ACTIVE" : skillIdleTitle;
                skillStripTitle.color = AvTheme.TextPrimary;
                skillStripDetail.text = skillIdleDetail;
                skillStripDetail.color = bypass ? AvTheme.Warning : AvTheme.Dim;
            }
        }

        /// <summary>Rail for state, glyph for identity, words for anyone the colour misses.</summary>
        private void PaintSkill(SkillRow row, PerkView[] perks)
        {
            if (!TryFind(perks, row.Id, out PerkView perk)) return;

            bool armed = skillAwaitingConfirmation == row.Id && !perk.Unlocked && perk.Affordable;
            string word = armed ? "SELECTED" : CellWord(perk);
            Color rail;
            if (perk.Unlocked) rail = AvTheme.RailReady;
            else if (armed) rail = AvTheme.RailCaution;
            else if (perk.Affordable) rail = AvTheme.RailInfo;
            else rail = AvTheme.RailInert.WithAlpha(0.45f);

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
            row.Select.WithTooltip(perk.Name + " · " + word + " — " + perk.Description);
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

            lane.Note.text = taken + "/" + lane.Ids.Count + " · " + (toolHeld ? "TOOL HELD"
                : closed ? "CLOSED" : "OPEN");
            lane.Note.color = toolHeld ? AvTheme.RailReady : closed ? AvTheme.Dim : AvTheme.TextPrimary;
            lane.Caption.color = closed ? AvTheme.Dim : AvTheme.TextPrimary;
        }
    }
}
