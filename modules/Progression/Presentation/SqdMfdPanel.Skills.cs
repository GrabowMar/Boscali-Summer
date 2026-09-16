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
        // The board is a matrix: one row per grade, one column per qualification. Four lanes
        // fit on one screen, so the player compares classes instead of scrolling a list.
        private const float SkillCellHeight = 56f;
        private const float SkillCellGap = 6f;
        private const float SkillGutterWidth = 30f;
        private const float SkillLaneHeaderHeight = 34f;
        private const float SkillFooterHeight = 42f;
        private const string SkillHint = "SELECT A CELL · one pick per grade, two tools per career.";

        private TMP_Text skillsBudgetValue;
        private TMP_Text skillsBudgetCaption;
        private TMP_Text skillDetailText;
        private AvButton skillConfirmButton;
        private string skillDetailNote = SkillHint;
        private Image[] skillBudgetPips;
        private GameObject[] skillBudgetPipSlots;
        private readonly List<SkillRow> skillRows = new List<SkillRow>(PerkCatalog.All.Length);
        private readonly List<SkillBranchRow> skillBranches = new List<SkillBranchRow>(8);
        private byte? skillAwaitingConfirmation;
        private byte? skillRequestId;
        private float skillConfirmationUntil;

        private void ResetSkillRows()
        {
            skillRows.Clear();
            skillBranches.Clear();
            skillsBudgetValue = null;
            skillsBudgetCaption = null;
            skillDetailText = null;
            skillConfirmButton = null;
            skillDetailNote = SkillHint;
            skillBudgetPips = null;
            skillBudgetPipSlots = null;
            skillAwaitingConfirmation = null;
            skillRequestId = null;
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

        private void BuildSkillsPage(RectTransform parent, Rect body)
        {
            clause = 0;
            PerkView[] perks = Progress != null ? Progress.GetPerks() : Array.Empty<PerkView>();
            if (perks.Length == 0)
            {
                DossierSpine(parent, new Rect(body.x, body.y, 3f, body.height));
                AvStyled.Label(parent,
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

            // The masthead, the pick budget and the selected-grade strip stay put; only the
            // matrix scrolls, so the decision context is on screen while reading the board.
            const float fixedHeight = 50f + 62f + 42f;
            AvNode fixedHead = AvBox.Column("fixed").Gaps(0f)
                .Add(AvBox.Cell("head").Height(50f))
                .Add(AvBox.Cell("budget").Height(62f))
                .Add(AvBox.Cell("detail").Height(42f));
            fixedHead.Arrange(Inset(body));

            AvNode page = AvBox.Column("board").Gaps(SkillCellGap)
                .Add(AvBox.Cell("title").Height(20f))
                .Add(AvBox.Cell("lanes").Height(SkillLaneHeaderHeight));
            for (int g = 0; g < grades; g++)
            {
                AvNode row = AvBox.Row("g" + g).Gaps(SkillCellGap);
                row.Add(AvBox.Cell("gutter").Width(SkillGutterWidth).Height(SkillCellHeight));
                for (int l = 0; l < lanes.Count; l++)
                    row.Add(AvBox.Cell("c" + l).Grow().Height(SkillCellHeight));
                page.Add(row);
            }
            page.Add(AvBox.Cell("shared").Height(SkillFooterHeight));
            page.Add(AvBox.Filler());

            Rect listArea = new Rect(body.x, body.y - fixedHeight, body.width,
                Mathf.Max(0f, body.height - fixedHeight));
            page.Arrange(Inset(listArea));

            float contentHeight = page.At("title").height + page.At("lanes").height +
                                  page.At("shared").height + SkillCellGap * (grades + 2);
            for (int g = 0; g < grades; g++) contentHeight += page.At("g" + g).height;

            RectTransform list = parent;
            if (contentHeight > listArea.height)
            {
                list = AvScreen.Scroll(parent, listArea, contentHeight, out Rect scrolled);
                page.Arrange(Inset(scrolled));
            }

            DossierSpine(parent, new Rect(body.x, body.y, 3f, body.height));
            Rect head = fixedHead.At("head");
            DrawFileHeader(parent, head.x, head.y, head.width, "FORM SQD-2 · SHEET 2 OF 4",
                "QUALIFICATION RECORD", "HOST COPY");
            BuildSkillsBudget(parent, fixedHead.At("budget"));
            BuildSkillDetail(parent, fixedHead.At("detail"));

            Rect title = page.At("title");
            DrawSectionTitle(list, title.x, title.y, title.width, "QUALIFICATIONS",
                "ONE PICK PER GRADE", band: false);
            for (int g = 0; g < grades; g++) DrawGradeRow(list, page, g, lanes);
            DrawLaneHeaders(list, page, lanes);
            DrawSharedFooter(list, page.At("shared"));
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
        /// One grade across every lane: the gutter names the grade, and each cell is a
        /// qualification's node. Reading across shows what the same pick buys in each class.
        /// </summary>
        private void DrawGradeRow(RectTransform parent, AvNode page, int grade, List<SkillLane> lanes)
        {
            AvNode row = page.Find("g" + grade);
            Rect gutter = row.At("gutter");
            PlainLabel(parent, new Rect(gutter.x + 2f, gutter.y - 3f, gutter.width - 8f, 14f),
                "G" + (grade + 1), "section-title-note").alignment = TextAlignmentOptions.MidlineRight;
            if (grade == PerkCatalog.MaximumDepth - 1)
            {
                PlainLabel(parent, new Rect(gutter.x + 2f, gutter.y - 17f, gutter.width - 8f, 12f),
                    "CAP", "row-sub").alignment = TextAlignmentOptions.MidlineRight;
            }

            for (int l = 0; l < lanes.Count; l++)
            {
                if (grade >= lanes[l].Nodes.Count) continue;
                skillRows.Add(DrawSkillCell(parent, row.At("c" + l), lanes[l].Nodes[grade]));
            }
        }

        /// <summary>
        /// A node: icon and name, its state in words, and the whole cell as the hit target.
        /// There is no per-cell button - one pinned CONFIRM control commits the selection.
        /// </summary>
        private SkillRow DrawSkillCell(RectTransform parent, Rect area, PerkView view)
        {
            var cell = new SkillRow { Id = view.Id };
            cell.Fill = AvKit.Panel(parent, area, Color.clear);
            cell.Frame = AvKit.Outline(parent, area, AvTheme.Hairline);
            cell.Icon = SqdGlyph.Create(parent, new Rect(area.x + 7f, area.y - 7f, 15f, 15f),
                SqdMarks.FromKey(PerkDefinitionOf(view).Icon));
            cell.Name = PlainLabel(parent, new Rect(area.x + 6f, area.y - 23f, area.width - 12f, 22f),
                view.Name.ToUpperInvariant(), "row-name");
            cell.State = PlainLabel(parent, new Rect(area.x + 6f, area.y - 44f, area.width - 12f, 13f),
                "", "row-sub");

            byte id = view.Id;
            cell.Select = AvKit.HitButton(parent, area, () => ClickSkill(id));
            cell.Select.SetRowHighlight(cell.Fill, Color.clear, HoverFill());
            return cell;
        }

        private void DrawLaneHeaders(RectTransform parent, AvNode page, List<SkillLane> lanes)
        {
            Rect area = page.At("lanes");
            AvNode firstRow = page.Find("g0");
            for (int l = 0; l < lanes.Count; l++)
            {
                AvNode cellNode = firstRow != null && l < firstRow.ChildCount ? firstRow.Find("c" + l) : null;
                Rect cell = cellNode != null ? cellNode.Rect.ToUnity() : area;
                var header = new SkillBranchRow { Name = lanes[l].Name };
                header.Caption = PlainLabel(parent,
                    new Rect(cell.x, area.y - 4f, cell.width, 15f), lanes[l].Name, "section-title");
                header.Note = PlainLabel(parent,
                    new Rect(cell.x, area.y - 19f, cell.width, 13f), "", "row-sub");
                skillBranches.Add(header);
            }
        }

        /// <summary>
        /// The shared ace-skill codes as one footer strip: the language SQD, the hunt HUD and
        /// Wing Command share, kept visible without pretending to be the player's board.
        /// </summary>
        private void DrawSharedFooter(RectTransform parent, Rect area)
        {
            DrawSectionTitle(parent, area.x, area.y, area.width, "SHARED COMBAT SKILLS",
                "AI & ACES · WING COMMAND", band: false);

            float chipWidth = area.width / AceSkillCatalog.MaximumSkills;
            for (int i = 0; i < AceSkillCatalog.MaximumSkills; i++)
            {
                AceSkillDefinition skill = AceSkillCatalog.All[i];
                Rect chip = new Rect(area.x + i * chipWidth, area.y - 26f, chipWidth - 8f, 16f);
                Glyph(parent, new Rect(chip.x, chip.y - 1f, 14f, 14f),
                    HuntMark.Toughness + i, AvTheme.RailCaution);
                TMP_Text code = PlainLabel(parent,
                    new Rect(chip.x + 18f, chip.y, chip.width - 18f, 14f), skill.Code, "row-sub");
                code.color = AvTheme.RailCaution;
                string help = skill.Name + " — " + skill.Description;
                AvKit.HitButton(parent, chip, () => { }).WithTooltip(help);
            }
        }

        private void BuildSkillsBudget(RectTransform parent, Rect area)
        {
            AvStyled.Box(parent, new Rect(area.x - 6f, area.y + 4f, area.width + 6f, area.height), "section band");
            AvStyled.SpineTick(parent, area.x - SpineInset + 3f, area.y - 14f);
            skillsBudgetValue = PlainLabel(parent, new Rect(area.x + 6f, area.y - 8f, area.width - 12f, 20f),
                "QUALIFICATION PICKS", "section-title");
            skillsBudgetCaption = PlainLabel(parent, new Rect(area.x + 6f, area.y - 28f, area.width - 12f, 14f),
                "", "row-sub");

            // One pip per pick the host allows, filled with what is still unspent. The score
            // bar above already shows progress toward the next grade; this is the balance.
            skillBudgetPips = new Image[MaximumBudgetPips];
            skillBudgetPipSlots = new GameObject[MaximumBudgetPips];
            for (int i = 0; i < MaximumBudgetPips; i++)
            {
                var slot = new GameObject("SkillPick_" + i, typeof(RectTransform));
                var rect = (RectTransform)slot.transform;
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(area.x + 6f + i * 15f, area.y - 46f, 12f, 12f));
                AvKit.Outline(rect, new Rect(0f, 0f, 12f, 12f), AvTheme.Hairline);
                skillBudgetPips[i] = AvKit.Panel(rect, new Rect(2f, -2f, 8f, 8f), Color.clear);
                skillBudgetPipSlots[i] = slot;
            }
        }

        /// <summary>The pinned selection strip: what is armed, what it does, and the one commit.</summary>
        private void BuildSkillDetail(RectTransform parent, Rect area)
        {
            AvStyled.Box(parent, new Rect(area.x - 6f, area.y + 4f, area.width + 6f, area.height - 6f), "section band");
            skillDetailText = PlainLabel(parent,
                new Rect(area.x + 6f, area.y - 11f, Mathf.Max(0f, area.width - 128f), 16f),
                SkillHint, "row-sub");
            skillDetailText.enableWordWrapping = false;
            skillDetailText.overflowMode = TextOverflowModes.Ellipsis;

            float height = Mathf.Min(AvTokens.RowHeight, area.height - 6f);
            skillConfirmButton = AvStyled.Button(parent,
                new Rect(area.x + area.width - 112f, area.y - (area.height - height) * 0.5f, 112f, height),
                "CONFIRM", "btn", CommitSelected, AvButtonStyle.Primary);
            skillConfirmButton.SetEnabled(false);
        }

        private void CommitSelected()
        {
            if (skillAwaitingConfirmation.HasValue) CommitSkill(skillAwaitingConfirmation.Value);
        }

        private static Rect Inset(Rect area) => new Rect(
            area.x + SpineInset, area.y, Mathf.Max(0f, area.width - SpineInset), area.height);

        private static bool IsUnlocked(PerkView[] perks, byte id) =>
            TryFind(perks, id, out PerkView view) && view.Unlocked;

        private static PerkDefinition PerkDefinitionOf(PerkView view)
        {
            for (int i = 0; i < PerkCatalog.All.Length; i++)
                if (PerkCatalog.All[i].Id == view.Id) return PerkCatalog.All[i];
            return default;
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
            skillRequestId = id;
            skillDetailNote = SkillHint;
            Progress.RequestUnlock(id);
            nextRefresh = 0f;
        }

        /// <summary>A cell click either arms the pick or says why the host would refuse it.</summary>
        private void ClickSkill(byte id)
        {
            if (progression == null || Progress.UnlockPending) return;
            if (!TryFind(Progress.GetPerks(), id, out PerkView view)) return;
            if (view.Unlocked)
            {
                skillDetailNote = view.Name.ToUpperInvariant() + " is active on this career.";
                nextRefresh = 0f;
                return;
            }
            if (!view.Affordable)
            {
                skillDetailNote = view.Name.ToUpperInvariant() + " — " + BlockReason(view);
                nextRefresh = 0f;
                return;
            }
            SelectSkill(id);
        }

        private static string BlockReason(PerkView view)
        {
            if (view.Block == PerkView.BlockCap)
                return "this career already holds its two support authorisations.";
            if (view.Block == PerkView.BlockGrade)
                return "the grade before it is not committed yet.";
            return "no unspent pick.";
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
            if (skillsBudgetValue == null || progression == null) return;
            if (skillAwaitingConfirmation.HasValue && Time.unscaledTime > skillConfirmationUntil)
            {
                skillAwaitingConfirmation = null;
                skillConfirmationUntil = 0f;
            }

            IProgressionView view = Progress;
            bool bypass = progression.BypassRequirements;
            int available = view.AvailablePoints;
            int score = view.Score;
            if (squad != null && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
                score = Math.Max(0, score - squad.GetScoreOrigin(PlayerIdentity.Of(local)));

            skillsBudgetValue.text = bypass ? "DEBUG BYPASS · EVERY GRADE OPEN"
                : available + (available == 1 ? " PICK AVAILABLE" : " PICKS AVAILABLE");
            skillsBudgetValue.color = available > 0 || bypass ? AvTheme.RailReady : AvTheme.TextPrimary;
            // The ramp lives in PerkPoints, so the hint the panel prints is the same arithmetic
            // the host paid out; the panel never divides score itself.
            int remaining = PerkPoints.RemainingToNext(score, view.ScorePerPoint);
            skillsBudgetCaption.text = bypass ? "Picks are free while bypass is active."
                : view.EarnedPoints >= view.MaximumPoints
                    ? "Pick ceiling reached — defeat enemy aces for bonus picks."
                    : remaining < 0
                        ? "Grade ladder complete — defeat enemy aces for bonus picks."
                        : remaining + " more pilot score for the next qualification grade.";

            int pips = Mathf.Clamp(view.MaximumPoints, 1, MaximumBudgetPips);
            for (int i = 0; i < skillBudgetPips.Length; i++)
            {
                skillBudgetPipSlots[i].SetActive(i < pips);
                skillBudgetPips[i].color = bypass ? AvTheme.Accent
                    : i < available ? AvTheme.RailReady : Color.clear;
            }

            PerkView[] perks = view.GetPerks();
            bool requestPending = view.UnlockPending;
            if (requestPending)
            {
                skillAwaitingConfirmation = null;
                skillConfirmationUntil = 0f;
            }
            else skillRequestId = null;

            for (int i = 0; i < skillRows.Count; i++)
            {
                SkillRow row = skillRows[i];
                if (!TryFind(perks, row.Id, out PerkView perk)) continue;

                bool armed = skillAwaitingConfirmation == row.Id;
                string word;
                Color tone;
                if (perk.Unlocked) { word = "ACTIVE"; tone = AvTheme.RailReady; }
                else if (armed) { word = "SELECTED"; tone = AvTheme.RailCaution; }
                else if (perk.Affordable) { word = "PICK"; tone = AvTheme.TextPrimary; }
                else if (perk.Block == PerkView.BlockCap) { word = "CLOSED"; tone = AvTheme.Dim; }
                else if (perk.Block == PerkView.BlockGrade) { word = "GRADE FIRST"; tone = AvTheme.Dim; }
                else { word = "NO PICK"; tone = AvTheme.Dim; }

                bool live = perk.Unlocked || perk.Affordable || armed;
                row.State.text = word;
                row.State.color = tone;
                row.Name.color = live ? AvTheme.TextPrimary : AvTheme.Dim;
                if (row.Icon != null) row.Icon.color = tone;
                Color frame = tone.WithAlpha(live ? 0.85f : 0.35f);
                for (int f = 0; f < row.Frame.Length; f++) row.Frame[f].color = frame;
                row.Fill.color = perk.Unlocked ? AvTheme.SurfaceInert
                    : armed ? AvTheme.RailCaution.WithAlpha(0.12f) : Color.clear;
                row.Select.WithTooltip(perk.Name + " · " + word + " — " + perk.Description);
            }

            for (int i = 0; i < skillBranches.Count; i++)
            {
                SkillBranchRow lane = skillBranches[i];
                int taken = 0;
                for (int n = 0; n < lane.Ids.Count; n++)
                    if (IsUnlocked(perks, lane.Ids[n])) taken++;

                // The lane's own state in words: its tool is the grade-1 row, and a lane whose
                // tool the host refuses on the career cap is closed for good on this career.
                bool toolHeld = lane.Ids.Count > 0 && IsUnlocked(perks, lane.Ids[0]);
                bool closed = !toolHeld && lane.Ids.Count > 0 &&
                    TryFind(perks, lane.Ids[0], out PerkView tool) && tool.Block == PerkView.BlockCap;
                lane.Note.text = taken + "/" + lane.Ids.Count + (toolHeld ? " · TOOL HELD"
                    : closed ? " · CLOSED" : " · OPEN");
                lane.Note.color = toolHeld ? AvTheme.RailReady : closed ? AvTheme.Dim : AvTheme.TextPrimary;
                lane.Caption.color = closed ? AvTheme.Dim : AvTheme.TextPrimary;
            }

            PerkView selected = default;
            bool hasSelection = skillAwaitingConfirmation.HasValue &&
                TryFind(perks, skillAwaitingConfirmation.Value, out selected);
            bool canConfirm = hasSelection && !requestPending &&
                !selected.Unlocked && selected.Affordable;
            skillConfirmButton.SetEnabled(canConfirm);
            if (requestPending)
            {
                skillDetailText.text = "Waiting for the host to answer this pick.";
                skillDetailText.color = AvTheme.RailCaution;
            }
            else if (hasSelection)
            {
                skillDetailText.text = "G" + PerkDefinitionOf(selected).Grade + " · " +
                    selected.Name.ToUpperInvariant() + " — " + selected.Description;
                skillDetailText.color = AvTheme.TextPrimary;
            }
            else
            {
                skillDetailText.text = skillDetailNote;
                skillDetailText.color = AvTheme.Dim;
            }
        }
    }
}
