using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        // The board is a matrix: one row per grade, one column per qualification. All four
        // lanes fit side by side, so picking compares classes instead of scrolling a list.
        // Geometry lives in SkillBoardLayout (which a test pins); SkillBoard draws it.
        private const string SkillIdleTitle = "SELECT A CELL";
        private const string SkillHint =
            "Scroll grades. Compare lanes at the same tier, then unlock one pick.";

        private AvTextBlock skillBudgetNote;
        private SkillBoard skillBoard;
        private AvTextBlock skillStripTitle;
        private AvTextBlock skillStripDetail;
        private AvControl skillConfirmButton;
        private string skillIdleTitle = SkillIdleTitle;
        private string skillIdleDetail = SkillHint;
        private readonly List<SkillRow> skillRows = new List<SkillRow>(PerkCatalog.All.Length);
        private readonly List<SkillBranchRow> skillBranches = new List<SkillBranchRow>(8);
        private byte? skillAwaitingConfirmation;

        private void ResetSkillRows()
        {
            skillRows.Clear();
            skillBranches.Clear();
            skillBudgetNote = null;
            skillBoard = null;
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

        private void BuildSkillsPage(AvFlow p)
        {
            PerkView[] perks = Progress != null ? Progress.GetPerks() : Array.Empty<PerkView>();
            p.Section(AvIcon.Star, "QUALIFICATION BOARD", "HOST PROGRESSION");
            if (perks.Length == 0)
            {
                AvTextBlock empty = p.Add(new AvTextBlock(p.Content, AvTextRole.Prose));
                empty.Set("This host has not configured a qualification catalog.");
                return;
            }

            var lanes = new List<SkillLane>(4);
            for (int i = 0; i < perks.Length; i++)
                LaneOf(lanes, perks[i].Branch).Nodes.Add(perks[i]);

            skillBudgetNote = p.Add(new AvTextBlock(p.Content, AvTextRole.ProseSmall));
            skillBoard = p.Add(new SkillBoard(p.Content, console.Ticker, lanes, SelectSkill));
            skillRows.AddRange(skillBoard.Rows);
            skillBranches.AddRange(skillBoard.Branches);

            p.Section(AvIcon.CircleCheck, "SELECTED GRADE", null);
            skillStripTitle = p.Add(new AvTextBlock(p.Content, AvTextRole.Head));
            skillStripDetail = p.Add(new AvTextBlock(p.Content, AvTextRole.Prose));
            AvButtons buttons = p.Buttons(new AvControl.Spec("UNLOCK SELECTED", CommitSelected, AvButtonStyle.Primary, AvIcon.CircleCheck));
            skillConfirmButton = buttons.Controls[0];
            skillConfirmButton.Interactable = false;
        }

        /// <summary>The budget as the board's own note: how many picks are unspent, and the wait.</summary>
        private string BudgetNote()
        {
            IProgressionView view = Progress;
            if (progression.BypassRequirements) return "DEBUG BYPASS · EVERY GRADE OPEN";

            int available = view.AvailablePoints;
            string picks = AvNum.Thousands(available) + (available == 1 ? " PICK UNSPENT · " : " PICKS UNSPENT · ");
            if (view.EarnedPoints >= view.MaximumPoints) return picks + "GRADE LADDER COMPLETE";

            int remaining = PerkPoints.RemainingToNext(MissionScore(), view.ScorePerPoint);
            return remaining < 0 ? picks + "GRADE LADDER COMPLETE" : picks + AvNum.Thousands(remaining) + " TO NEXT GRADE";
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

        /// <summary>
        /// A tool sells its code - STK, SAT, EW, ENG are the support codes the OPS page and the
        /// wing badges already use - and its state line says TOOL or HELD. A passive grade gets
        /// its own name.
        /// </summary>
        private static string CellName(PerkView view, PerkDefinition definition) =>
            definition.IsTool
                ? PerkCatalog.CapabilityCode(definition.Capability)
                : view.Name.ToUpperInvariant();

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
            skillBudgetNote?.Set(BudgetNote());
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
                skillConfirmButton.Interactable = canConfirm;
                skillConfirmButton.Latched = canConfirm;
            }

            if (requestPending)
            {
                skillStripTitle.Set("WAITING FOR THE HOST");
                skillStripTitle.Color = AvTheme.RailCaution;
                skillStripDetail.Set("The pick is sent. The host answers on the next tick.");
            }
            else if (hasSelection)
            {
                PerkDefinition definition = PerkDefinitionOf(selected);
                skillStripTitle.Set("G" + AvNum.Thousands(definition.Grade) + " · " + selected.Name.ToUpperInvariant());
                skillStripTitle.Color = AvTheme.RailCaution;
                skillStripDetail.Set(selected.Description);
            }
            else
            {
                skillStripTitle.Set(skillIdleTitle);
                skillStripTitle.Color = AvTheme.TextPrimary;
                skillStripDetail.Set(skillIdleDetail);
            }
        }

        /// <summary>Rail for state, words for anyone the colour misses.</summary>
        private void PaintSkill(SkillRow row, PerkView[] perks)
        {
            if (!TryFind(perks, row.Id, out PerkView perk)) return;

            bool armed = skillAwaitingConfirmation == row.Id && !perk.Unlocked && perk.Affordable;
            string word = armed ? "SELECTED" : CellLine(perk, row.Id);
            AvState state = perk.Unlocked ? AvState.Ready
                : armed ? AvState.Caution
                : perk.Affordable ? AvState.Info
                : AvState.Inert;

            PerkDefinition definition = PerkDefinitionOf(perk);
            // Cells are ~95 px wide: AvRow's 88 px value column would leave the name no room, so the state word rides the sub-line.
            row.Row.Set(CellName(perk, definition), word, null, state);
            row.Row.Armed = armed;
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

            lane.Note.text = AvNum.Thousands(taken) + "/" + AvNum.Thousands(lane.Ids.Count) + " " +
                (toolHeld ? "HELD" : closed ? "CLOSED" : "OPEN");
            lane.Note.color = toolHeld ? AvTheme.RailReady : closed ? AvTheme.Dim : AvTheme.TextPrimary;
            lane.Caption.color = closed ? AvTheme.Dim : AvTheme.TextPrimary;
        }

        /// <summary>
        /// The lane x grade matrix. Built once from the catalogue's fixed shape and repainted
        /// (never rebuilt) as grades unlock. Cells are <see cref="AvRow"/>s placed on an explicit
        /// grid rather than a vertical <see cref="AvFlow"/>, driven by <see cref="SkillBoardLayout"/>
        /// (kept unchanged: <c>SqdPanelTests.TestBoardGeometry</c> pins its arithmetic).
        /// </summary>
        private sealed class SkillBoard : AvPart
        {
            private readonly List<SkillLane> lanes;
            private readonly int grades;
            private readonly TMP_Text legendKey;
            private readonly TMP_Text[] legendWords;

            public readonly List<SkillRow> Rows = new List<SkillRow>(24);
            public readonly List<SkillBranchRow> Branches = new List<SkillBranchRow>(4);

            public SkillBoard(RectTransform parent, AvTicker ticker, List<SkillLane> lanes, Action<byte> onSelect)
            {
                this.lanes = lanes;
                for (int i = 0; i < lanes.Count; i++) grades = Math.Max(grades, lanes[i].Nodes.Count);

                Rect = AvLay.Child(parent, "SkillBoard");
                legendKey = AvText.Make(Rect, "LegendKey", AvTextRole.Micro, "RAIL STATE");
                string[] words = { "HELD", "PICK", "SELECTED", "LOCKED" };
                legendWords = new TMP_Text[words.Length];
                for (int i = 0; i < words.Length; i++)
                    legendWords[i] = AvText.Make(Rect, "Legend" + i, AvTextRole.Micro, words[i]);

                for (int l = 0; l < lanes.Count; l++)
                {
                    var branch = new SkillBranchRow
                    {
                        Caption = AvText.Make(Rect, "Lane " + lanes[l].Name, AvTextRole.Head, lanes[l].Name),
                        Note = AvText.Make(Rect, "LaneNote " + lanes[l].Name, AvTextRole.Micro, ""),
                    };
                    for (int n = 0; n < lanes[l].Nodes.Count; n++) branch.Ids.Add(lanes[l].Nodes[n].Id);
                    Branches.Add(branch);

                    for (int g = 0; g < lanes[l].Nodes.Count; g++)
                    {
                        byte id = lanes[l].Nodes[g].Id;
                        var avRow = new AvRow(Rect, () => onSelect(id));
                        ticker?.Register(avRow);
                        Rows.Add(new SkillRow { Id = id, Row = avRow });
                    }
                }
                Restyle();
            }

            public override float Measure(float width) =>
                SkillBoardLayout.LegendHeight + SkillBoardLayout.LaneHeaderHeight + SkillBoardLayout.Gap +
                grades * (SkillBoardLayout.MinCellHeight + SkillBoardLayout.Gap);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float cellWidth = SkillBoardLayout.CellWidth(s.W, lanes.Count);
                float y = 0f;
                AvLay.Place(legendKey.rectTransform, 0f, y, 78f, 12f);
                float pitch = (s.W - 84f) / Math.Max(1, legendWords.Length);
                for (int i = 0; i < legendWords.Length; i++)
                    AvLay.Place(legendWords[i].rectTransform, 84f + i * pitch, y, Math.Max(0f, pitch - 4f), 12f);
                y += SkillBoardLayout.LegendHeight;

                for (int l = 0; l < lanes.Count; l++)
                {
                    float laneX = SkillBoardLayout.CellX(0f, cellWidth, l);
                    AvLay.Place(Branches[l].Caption.rectTransform, laneX, y, cellWidth, 16f);
                    AvLay.Place(Branches[l].Note.rectTransform, laneX, y + 16f, cellWidth, 13f);
                }
                y += SkillBoardLayout.LaneHeaderHeight + SkillBoardLayout.Gap;

                int rowIndex = 0;
                for (int l = 0; l < lanes.Count; l++)
                {
                    float laneX = SkillBoardLayout.CellX(0f, cellWidth, l);
                    for (int g = 0; g < lanes[l].Nodes.Count; g++)
                    {
                        float cellY = y + g * (SkillBoardLayout.MinCellHeight + SkillBoardLayout.Gap);
                        Rows[rowIndex].Row.Place(new AvSlot(laneX, cellY, cellWidth, SkillBoardLayout.MinCellHeight));
                        rowIndex++;
                    }
                }
            }

            public override void Restyle()
            {
                Color dim = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                legendKey.color = dim;
                for (int i = 0; i < legendWords.Length; i++) legendWords[i].color = dim;
            }
        }
    }
}
