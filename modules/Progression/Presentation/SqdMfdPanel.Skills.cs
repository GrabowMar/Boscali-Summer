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
        private TMP_Text skillsBudgetValue;
        private TMP_Text skillsBudgetCaption;
        private Image skillsBudgetFill;
        private readonly List<SkillRow> skillRows = new List<SkillRow>(PerkCatalog.MaximumPerks);
        private byte? skillAwaitingConfirmation;
        private byte? skillRequestId;
        private float skillConfirmationUntil;

        private void ResetSkillRows()
        {
            skillRows.Clear();
            skillsBudgetValue = null;
            skillsBudgetCaption = null;
            skillsBudgetFill = null;
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

        private void BuildSkillsPage(RectTransform parent, Rect body)
        {
            PerkView[] perks = Progress != null ? Progress.GetPerks() : Array.Empty<PerkView>();
            if (perks.Length == 0)
            {
                AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
                AvStyled.Label(parent,
                    new Rect(body.x + SpineInset, body.y, body.width - SpineInset, 40f),
                    "No pilot skills are configured on this host.", "row-sub");
                return;
            }

            var passives = new List<PerkView>(perks.Length);
            var authorisations = new List<PerkView>(perks.Length);
            for (int i = 0; i < perks.Length; i++)
            {
                if (perks[i].Group != null &&
                    perks[i].Group.IndexOf("AUTHORIS", StringComparison.OrdinalIgnoreCase) >= 0)
                    authorisations.Add(perks[i]);
                else passives.Add(perks[i]);
            }

            var passiveDescriptions = new List<string>(passives.Count);
            for (int i = 0; i < passives.Count; i++) passiveDescriptions.Add(passives[i].Description);
            var authorDescriptions = new List<string>(authorisations.Count);
            for (int i = 0; i < authorisations.Count; i++) authorDescriptions.Add(authorisations[i].Description);

            AvNode page = AvBox.Column("skills").Gaps(0f)
                .Add(AvBox.Cell("budget").Height(62f))
                .Add(SkillSection("shared", SharedRowNode, AceSkillCatalog.MaximumSkills, null))
                .Add(SkillSection("passives", SkillRowNode, passives.Count, passiveDescriptions))
                .Add(SkillSection("authors", SkillRowNode, authorisations.Count, authorDescriptions))
                .Add(AvBox.Filler());
            page.Arrange(body);

            float contentHeight = page.At("budget").height + page.At("shared").height +
                page.At("passives").height + page.At("authors").height;
            if (contentHeight > body.height)
            {
                parent = AvScreen.Scroll(parent, body, contentHeight, out Rect scrolled);
                page.Arrange(scrolled);
                body = scrolled;
            }

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
            BuildSkillsBudget(parent, page.At("budget"));
            DrawSharedSection(parent, page, "shared");
            DrawSkillSection(parent, page, "passives", "PILOT SKILLS", passives);
            DrawSkillSection(parent, page, "authors", "SUPPORT AUTHORISATIONS", authorisations);
        }

        private void BuildSkillsBudget(RectTransform parent, Rect area)
        {
            AvStyled.Box(parent, new Rect(area.x - 6f, area.y + 4f, area.width + 12f, area.height), "section band");
            AvStyled.SpineTick(parent, area.x - SpineInset + 3f, area.y - 14f);
            skillsBudgetValue = PlainLabel(parent, new Rect(area.x + 6f, area.y - 8f, area.width - 12f, 20f),
                "SKILL POINTS", "section-title");
            skillsBudgetCaption = PlainLabel(parent, new Rect(area.x + 6f, area.y - 28f, area.width - 12f, 14f),
                "", "row-sub");
            skillsBudgetFill = AvKit.ProgressBar(parent,
                new Rect(area.x + 6f, area.y - 50f, area.width - 12f, 5f), 0f, AvTheme.RailReady);
        }

        private static AvNode SkillSection(
            string name, Func<string, string, AvNode> rowFactory, int rows, IList<string> descriptions)
        {
            AvNode section = AvBox.Column(name).Pad(10f, 14f, 12f, 8f).Gaps(0f)
                .Add(AvBox.Cell("title").Height(20f));
            for (int i = 0; i < rows; i++)
            {
                string description = descriptions != null && i < descriptions.Count
                    ? descriptions[i]
                    : AceSkillCatalog.All[i].Description;
                section.Add(rowFactory("r" + i, description));
            }
            return section;
        }

        private static AvNode SkillRowNode(string name, string description) =>
            AvBox.Row(name).Pad(9f, 0f, 9f, 0f).Gaps(9f)
                .Add(AvBox.Cell("rail").Width(3f))
                .Add(AvBox.Cell("icon").Width(22f))
                .Add(AvBox.Cell("code").Width(30f))
                .Add(AvBox.Column("text").Grow().Gaps(3f)
                    .Add(AvBox.Cell("name").Height(15f))
                    .Add(AvBox.Text("desc", description, "row-sub")))
                .Add(AvBox.Cell("trail").Width(96f).Intrinsic(48f));

        private static AvNode SharedRowNode(string name, string description) =>
            AvBox.Row(name).Pad(9f, 0f, 9f, 0f).Gaps(9f)
                .Add(AvBox.Cell("rail").Width(3f))
                .Add(AvBox.Cell("icon").Width(22f))
                .Add(AvBox.Cell("code").Width(52f))
                .Add(AvBox.Column("text").Grow().Gaps(3f)
                    .Add(AvBox.Cell("name").Height(15f))
                    .Add(AvBox.Text("desc", description, "row-sub")))
                .Add(AvBox.Cell("trail").Width(56f).Intrinsic(48f));

        private void DrawSharedSection(RectTransform parent, AvNode page, string key)
        {
            AvNode section = page.Find(key);
            Rect sectionArea = page.At(key);
            AvNode title = section.Find("title");
            Rect titleRect = title != null ? title.Rect.ToUnity() : sectionArea;
            DrawSectionTitle(parent, titleRect.x, titleRect.y, sectionArea.width,
                "SHARED COMBAT SKILLS", "AI & ACES · WING COMMAND", band: false);
            for (int i = 0; i < AceSkillCatalog.MaximumSkills; i++)
            {
                AvNode row = section.Find("r" + i);
                if (row == null) continue;
                Rect area = row.Rect.ToUnity();
                AceSkillDefinition skill = AceSkillCatalog.All[i];
                AvStyled.Rail(parent, row.At("rail"), "info");
                Glyph(parent, row.At("icon"), HuntMark.Toughness + i, AvTheme.RailCaution);
                PlainLabel(parent, row.At("code"), skill.Code, "row-name");
                PlainLabel(parent, row.At("text.name"), skill.Name.ToUpperInvariant(), "row-name");
                PlainLabel(parent, row.At("text.desc"), skill.Description, "row-sub");
                TMP_Text tag = PlainLabel(parent, row.At("trail"), "ACE USE", "section-title-note");
                tag.alignment = TextAlignmentOptions.MidlineRight;
                RowSeparator(parent, area);
            }
        }

        private void DrawSkillSection(
            RectTransform parent, AvNode page, string key, string title, List<PerkView> perks)
        {
            Rect sectionArea = page.At(key);
            AvNode section = page.Find(key);
            if (perks.Count == 0)
            {
                AvStyled.Label(parent, new Rect(sectionArea.x, sectionArea.y - 4f, sectionArea.width, 18f),
                    "No skills in this group on this host.", "row-sub");
                return;
            }

            string note = key == "authors" ? "AUTH CODE · SELECT ROW · CONFIRM" : "SELECT ROW · CONFIRM";
            AvNode titleNode = section.Find("title");
            Rect titleRect = titleNode != null ? titleNode.Rect.ToUnity() : sectionArea;
            DrawSectionTitle(parent, titleRect.x, titleRect.y, sectionArea.width, title, note, band: false);

            for (int i = 0; i < perks.Count; i++)
            {
                AvNode row = section.Find("r" + i);
                if (row == null) continue;
                AddSkillRow(parent, row, perks[i]);
            }
        }

        private void AddSkillRow(RectTransform parent, AvNode row, PerkView view)
        {
            Rect area = row.Rect.ToUnity();
            var entry = new SkillRow { Id = view.Id };

            entry.Background = AvKit.Panel(parent, area, Color.clear);
            RowSeparator(parent, area);
            entry.Rail = AvStyled.Rail(parent, row.At("rail"), "locked");
            entry.Icon = SqdGlyph.Create(parent, row.At("icon"), SqdMarks.FromKey(perkIcon(view)));
            entry.Code = AvStyled.Label(parent, row.At("code"), PerkCatalog.CodeOf(PerkDefinitionOf(view)),
                "row-sub", align: TextAlignmentOptions.MidlineLeft);
            entry.Name = AvStyled.Label(parent, row.At("text.name"),
                view.Name.ToUpperInvariant(), "row-name");
            AvStyled.Label(parent, row.At("text.desc"), view.Description, "row-sub");

            byte id = view.Id;
            Rect trail = row.At("trail");
            Rect selectArea = new Rect(
                area.x, area.y, Mathf.Max(0f, trail.x - area.x - 2f), area.height);
            entry.Select = AvKit.HitButton(parent, selectArea, () => SelectSkill(id));
            entry.Select.SetRowHighlight(entry.Background, Color.clear, HoverFill());
            entry.Select.WithTooltip(
                view.Name.ToUpperInvariant() + " — costs " + view.Cost +
                (view.Cost == 1 ? " point. " : " points. ") + view.Description +
                " Select this row, then use the separate confirm control.");

            float actionHeight = Mathf.Min(AvTokens.RowHeight, trail.height);
            entry.Confirm = AvStyled.Button(parent,
                new Rect(trail.x, trail.y - Mathf.Max(0f, (trail.height - actionHeight) * 0.5f),
                         trail.width, actionHeight),
                "SELECT", "btn", () => CommitSkill(id), AvButtonStyle.Primary);
            entry.Confirm.SetEnabled(false);
            skillRows.Add(entry);
        }

        private static string perkIcon(PerkView view)
        {
            for (int i = 0; i < PerkCatalog.All.Length; i++)
                if (PerkCatalog.All[i].Id == view.Id) return PerkCatalog.All[i].Icon;
            return "combat";
        }

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
            Progress.RequestUnlock(id);
            nextRefresh = 0f;
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
            int perPoint = Math.Max(1, view.ScorePerPoint);
            int score = view.Score;
            if (squad != null && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
                score = Math.Max(0, score - squad.GetScoreOrigin(PlayerIdentity.Of(local)));

            skillsBudgetValue.text = bypass ? "DEBUG BYPASS · EVERY SKILL ACTIVE"
                : available + (available == 1 ? " SKILL POINT AVAILABLE" : " SKILL POINTS AVAILABLE");
            skillsBudgetValue.color = available > 0 || bypass ? AvTheme.RailReady : AvTheme.TextPrimary;
            int intoPoint = score % perPoint;
            skillsBudgetCaption.text = bypass ? "Points are free while bypass is active."
                : view.EarnedPoints >= view.MaximumPoints
                    ? "Score budget complete — defeat enemy aces for bonus points."
                    : (perPoint - intoPoint) + " more pilot score for the next skill point.";
            skillsBudgetFill.fillAmount = bypass || view.EarnedPoints >= view.MaximumPoints
                ? 1f : intoPoint / (float)perPoint;

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

                bool confirming = skillAwaitingConfirmation == row.Id;
                row.Select.SetEnabled(!requestPending && perk.Affordable && !perk.Unlocked);

                if (perk.Unlocked)
                {
                    PaintSkill(row, "ready", AvTheme.TextPrimary);
                    PaintSkillAction(row, "ACTIVE", false, false,
                        perk.Name.ToUpperInvariant() + " is active.");
                    row.Select.WithTooltip(perk.Name.ToUpperInvariant() + " is active.");
                }
                else if (requestPending && skillRequestId == row.Id)
                {
                    const string pending = "Waiting for the host to accept or deny this skill.";
                    PaintSkill(row, "cooling", AvTheme.TextPrimary);
                    PaintSkillAction(row, "PENDING", false, true, pending);
                    row.Select.WithTooltip(pending);
                }
                else if (requestPending)
                {
                    const string wait = "Wait for the host to answer the current skill request.";
                    PaintSkill(row, "locked", AvTheme.Dim);
                    PaintSkillAction(row, "WAIT", false, false, wait);
                    row.Select.WithTooltip(wait);
                }
                else if (confirming)
                {
                    string confirm = "Confirm this separate action to commit " + perk.Cost +
                                     (perk.Cost == 1 ? " skill point." : " skill points.");
                    PaintSkill(row, "armed", AvTheme.TextPrimary);
                    PaintSkillAction(row, "CONFIRM " + perk.Cost + "P", true, true, confirm);
                    row.Select.WithTooltip("Selected. Use the separate CONFIRM control to commit it.");
                }
                else if (perk.Affordable)
                {
                    const string select = "Select the row first; the separate confirm control will then enable.";
                    PaintSkill(row, "armed", AvTheme.TextPrimary);
                    PaintSkillAction(row, "CONFIRM " + perk.Cost + "P", false, false, select);
                    row.Select.WithTooltip(select);
                }
                else
                {
                    string required = "Requires " + perk.Cost +
                                      (perk.Cost == 1 ? " unspent skill point." : " unspent skill points.");
                    PaintSkill(row, "locked", AvTheme.Dim);
                    PaintSkillAction(row, perk.Cost + "P REQ", false, false, required);
                    row.Select.WithTooltip(required);
                }
            }
        }

        private static void Glyph(RectTransform parent, Rect area, HuntMark mark, Color color)
        {
            var go = new GameObject(mark.ToString(), typeof(RectTransform), typeof(HuntGlyph));
            go.transform.SetParent(parent, false);
            AvKit.Place((RectTransform)go.transform, area);
            HuntGlyph glyph = go.GetComponent<HuntGlyph>();
            glyph.Mark = mark;
            glyph.color = color;
            glyph.raycastTarget = false;
        }

        private static void PaintSkill(SkillRow row, string railState, Color name)
        {
            Color rail = RailColour(railState);
            row.Rail.color = rail;
            row.Code.color = rail;
            if (row.Icon != null) row.Icon.color = rail;
            row.Name.color = name;
        }

        private static void PaintSkillAction(
            SkillRow row, string text, bool enabled, bool latched, string tooltip)
        {
            row.Confirm.SetText(text);
            row.Confirm.SetEnabled(enabled);
            row.Confirm.SetLatched(latched);
            row.Confirm.WithTooltip(tooltip);
        }
    }
}
