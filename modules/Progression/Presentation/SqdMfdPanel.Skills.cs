using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Contracts;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        // The board is a tech tree: one column per qualification, one aligned tier row per
        // grade. All four lanes fit side by side, so picking compares classes instead of
        // scrolling a list. Geometry lives in SkillBoardLayout (which a test pins); SkillBoard
        // draws it and SkillNode is one grade.
        private const string SkillIdleTitle = "SELECT A GRADE";
        private const string SkillHint = "Tap an open grade. One pick, no undo.";

        private SkillBoard skillBoard;
        private AvTextBlock skillStripTitle;
        private AvTextBlock skillStripEffect;
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
            skillBoard = null;
            skillStripTitle = null;
            skillStripEffect = null;
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
            if (perks.Length == 0)
            {
                p.Add(new SqdEmptyCard(p.Content, AvIcon.Lock, "NO QUALIFICATION CATALOG",
                    "This host has not configured a qualification catalog, so there is nothing to unlock."));
                return;
            }

            var lanes = new List<SkillLane>(4);
            for (int i = 0; i < perks.Length; i++)
                LaneOf(lanes, perks[i].Branch).Nodes.Add(perks[i]);

            // The budget rings that used to sit here repeated the header's SCORE and PICKS tiles; the board takes the height.
            skillBoard = p.Add(new SkillBoard(p.Content, console.Ticker, lanes, SelectSkill), 1f);
            skillRows.AddRange(skillBoard.Rows);
            skillBranches.AddRange(skillBoard.Branches);

            AvCard detail = new AvCard(p.Content, console.Ticker, p.Inner, null, true, "raised");
            skillStripTitle = detail.Flow.Add(new AvTextBlock(detail.Flow.Content, AvTextRole.Head));
            skillStripEffect = detail.Flow.Add(new AvTextBlock(detail.Flow.Content, AvTextRole.DataStrong));
            skillStripDetail = detail.Flow.Add(new AvTextBlock(detail.Flow.Content, AvTextRole.Prose));
            AvButtons buttons = detail.Flow.Buttons(
                new AvControl.Spec("UNLOCK SELECTED", CommitSelected, AvButtonStyle.Primary, AvIcon.CircleCheck));
            skillConfirmButton = buttons.Controls[0];
            skillConfirmButton.Interactable = false;
            skillConfirmButton.Help = "Commit the selected grade. One pick, no undo.";
            p.Add(detail);
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
        /// A tool's node reads "STK TOOL" - STK, SAT, EW, ENG are the support codes the OPS page and
        /// the wing badges already use. A passive grade gets its own name. One name per node, ever.
        /// </summary>
        private static string CellName(PerkView view, PerkDefinition definition) =>
            definition.IsTool
                ? PerkCatalog.CapabilityCode(definition.Capability) + " TOOL"
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
                skillStripEffect.SetShown(false);
                skillStripDetail.Set("Pick sent.");
            }
            else if (hasSelection)
            {
                PerkDefinition definition = PerkDefinitionOf(selected);
                skillStripTitle.Set(definition.Lane + " · G" + AvNum.Thousands(definition.Grade) + " · " +
                    selected.Name.ToUpperInvariant());
                skillStripTitle.Color = SqdTone.Select;
                skillStripEffect.Set(definition.IsTool
                    ? "SUPPORT TOOL · " + PerkCatalog.CapabilityCode(definition.Capability)
                    : PerkCatalog.EffectLabel(selected.Id));
                skillStripEffect.Color = SqdTone.Text(AvState.Ready);
                skillStripEffect.SetShown(true);
                skillStripDetail.Set(selected.Description);
            }
            else
            {
                skillStripTitle.Set(skillIdleTitle);
                skillStripTitle.Color = AvTheme.TextPrimary;
                skillStripEffect.SetShown(false);
                skillStripDetail.Set(skillIdleDetail);
            }
        }

        /// <summary>Glyph and word for anyone the colour misses; the node carries one name only.</summary>
        private void PaintSkill(SkillRow row, PerkView[] perks)
        {
            if (!TryFind(perks, row.Id, out PerkView perk)) return;

            bool armed = skillAwaitingConfirmation == row.Id && !perk.Unlocked && perk.Affordable;
            SkillNodeState state = perk.Unlocked ? SkillNodeState.Held
                : armed ? SkillNodeState.Selected
                : perk.Affordable ? SkillNodeState.Open
                : SkillNodeState.Locked;

            PerkDefinition definition = PerkDefinitionOf(perk);
            // A shut node answers "why not" on hover instead of only after a click.
            string effect = definition.IsTool ? "Support tool" : PerkCatalog.EffectLabel(row.Id);
            string help = perk.Unlocked ? perk.Name + " — held. " + perk.Description
                : perk.Affordable || armed ? perk.Name + " (" + effect + ") — " + perk.Description
                : perk.Name + " — " + BlockReason(perk);
            row.Node.Paint(CellName(perk, definition), state, help);
            row.Node.SetEffect(definition.IsTool ? "SUPPORT TOOL" : effect.ToUpperInvariant());
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

            lane.Paint(taken, lane.Ids.Count, toolHeld ? "HELD" : closed ? "CLOSED" : "OPEN",
                toolHeld ? AvState.Ready : closed ? AvState.Inert : AvState.Info);
        }

        /// <summary>How a node reads: colour, glyph and hover help all follow from this.</summary>
        internal enum SkillNodeState : byte { Locked, Open, Held, Selected }

        /// <summary>
        /// One grade of one lane: a state glyph and exactly one name (wrapping to two lines, sized
        /// to fit). Clicking an open node selects it; the detail card below says what it buys.
        /// </summary>
        internal sealed class SkillNode : AvPart
        {
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text glyph, label, effect;
            private readonly Action<byte> onSelect;
            private readonly byte id;
            private SkillNodeState state = SkillNodeState.Locked;
            private bool hover;
            private string lastHelp;

            /// <summary>The connector line hanging below this node; owned and placed by the board.</summary>
            public Image Connector;

            public SkillNode(RectTransform parent, byte perkId, Action<byte> select)
            {
                id = perkId;
                onSelect = select;
                Rect = AvLay.Child(parent, "Node " + perkId);
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(5f)); AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                glyph = AvIcons.Make(Rect, AvIcon.Lock, AvGridTokens.IconInline, Color.white);
                label = AvText.Make(Rect, "Name", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
                label.characterSpacing = 1f; // a long word (SURVEILLANCE) must fit the node without breaking mid-word
                AvText.Fit(label, true);
                effect = AvText.Make(Rect, "Effect", AvTextRole.Micro, "", TextAlignmentOptions.BottomLeft);
                AvText.Fit(effect, false);
                AvHit hit = AvHit.On(frame);
                hit.Hover = h => { hover = h; Restyle(); };
                hit.Click = e => { if (state == SkillNodeState.Open || state == SkillNodeState.Selected) onSelect?.Invoke(id); };
                Restyle();
            }

            public SkillNodeState State => state;

            /// <summary>What the grade buys, shown on the node's last line once the board has the height for it.</summary>
            public void SetEffect(string text)
            {
                string t = text ?? "";
                if (effect.text != t) effect.text = t;
            }

            /// <summary>A node needs this much height for two name lines plus the effect line.</summary>
            public const float EffectHeight = 70f;

            public void Paint(string name, SkillNodeState next, string help)
            {
                // Repainted on every refresh tick: do nothing unless something the eye can see changed.
                if (next == state && glyph.text.Length > 0 && label.text == (name ?? "") && help == lastHelp) return;
                label.text = name ?? "";
                if (help != lastHelp) { lastHelp = help; AvHelpTip.Attach(frame.gameObject, help); }
                if (next == state && glyph.text.Length > 0) { Restyle(); return; }
                state = next;
                AvIcons.Set(glyph, GlyphFor(next), AvGridTokens.IconInline);
                Restyle();
            }

            private static AvIcon GlyphFor(SkillNodeState s)
            {
                switch (s)
                {
                    case SkillNodeState.Held: return AvIcon.CircleCheck;
                    case SkillNodeState.Selected: return AvIcon.Target;
                    case SkillNodeState.Open: return AvIcon.Circle;
                    default: return AvIcon.Lock;
                }
            }

            public override float Measure(float width) => SkillBoardLayout.NodeHeight;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(rail.rectTransform, 0f, 0f, 2f, s.H);
                AvLay.Place(glyph.rectTransform, 9f, 5f, 14f, 14f);
                bool tall = s.H >= EffectHeight && effect.text.Length > 0 && AvText.Width(effect) <= s.W - 11f;   // a long effect waits for the detail card
                AvLay.Place(label.rectTransform, 8f, 21f, s.W - 11f, tall ? 32f : s.H - 24f);
                effect.gameObject.SetActive(tall);
                AvLay.Place(effect.rectTransform, 8f, s.H - 17f, s.W - 11f, 14f);
            }

            public override void Restyle()
            {
                AvStyle inert = AvStyleHost.FuiStyle("card inert");
                Color fill = AvStyleHost.Resolve(inert.Background, AvTheme.SurfaceInert);
                Color stroke = AvTheme.Hairline;
                Color accent = AvTheme.RailInert;
                Color ink = SqdTone.Ink;
                switch (state)
                {
                    case SkillNodeState.Held:
                        accent = AvTheme.RailReady;
                        fill = accent.WithAlpha(.16f);
                        stroke = accent.WithAlpha(.75f);
                        break;
                    case SkillNodeState.Open:
                        accent = AvTheme.RailInfo;
                        stroke = accent.WithAlpha(.7f);
                        if (hover) fill = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row", "hover").Background, fill);
                        break;
                    case SkillNodeState.Selected:
                        accent = SqdTone.Select;
                        fill = AvStyleHost.Resolve(AvStyleHost.FuiStyle("cell", "on").Background, accent.WithAlpha(.16f));
                        stroke = accent;
                        break;
                    default:
                        accent = AvTheme.RailInert;
                        ink = AvTheme.Disabled;
                        break;
                }
                frame.Paint(fill, stroke);
                rail.color = accent;
                glyph.color = state == SkillNodeState.Locked ? AvTheme.Disabled : accent;
                label.color = ink;
                effect.color = state == SkillNodeState.Locked ? AvTheme.Disabled : SqdTone.Text(AvState.Ready);
                if (Connector != null)
                    Connector.color = state == SkillNodeState.Held ? AvTheme.RailReady.WithAlpha(.8f) : AvTheme.Hairline;
            }
        }

        /// <summary>
        /// The qualification tech tree. Built once from the catalogue's fixed shape and repainted
        /// (never rebuilt) as grades unlock: a legend row of glyph+word chips, four lane headers
        /// (name, held count, progress bar, state word) and one aligned row of nodes per tier,
        /// joined by connector lines. Placed on explicit rects by <see cref="SkillBoardLayout"/>.
        /// </summary>
        private sealed class SkillBoard : AvPart
        {
            private readonly List<SkillLane> lanes;
            private readonly int grades;
            private readonly TMP_Text[] legendIcons = new TMP_Text[4];
            private readonly TMP_Text[] legendWords = new TMP_Text[4];
            private readonly TMP_Text[] tierLabels;

            public readonly List<SkillRow> Rows = new List<SkillRow>(24);
            public readonly List<SkillBranchRow> Branches = new List<SkillBranchRow>(4);

            public SkillBoard(RectTransform parent, AvTicker ticker, List<SkillLane> lanes, Action<byte> onSelect)
            {
                this.lanes = lanes;
                for (int i = 0; i < lanes.Count; i++) grades = Math.Max(grades, lanes[i].Nodes.Count);

                Rect = AvLay.Child(parent, "SkillBoard");
                AvIcon[] icons = { AvIcon.CircleCheck, AvIcon.Circle, AvIcon.Target, AvIcon.Lock };
                string[] words = { "HELD", "OPEN", "SELECTED", "LOCKED" };
                for (int i = 0; i < words.Length; i++)
                {
                    legendIcons[i] = AvIcons.Make(Rect, icons[i], AvGridTokens.IconInline, Color.white);
                    legendWords[i] = AvText.Make(Rect, "Legend" + i, AvTextRole.Micro, words[i]);
                    AvText.Fit(legendWords[i], false);
                }

                tierLabels = new TMP_Text[grades];
                for (int g = 0; g < grades; g++)
                    tierLabels[g] = AvText.Make(Rect, "Tier" + (g + 1), AvTextRole.DataSmall,
                        (g + 1).ToString(), TextAlignmentOptions.Center);

                for (int l = 0; l < lanes.Count; l++)
                {
                    var branch = new SkillBranchRow(Rect, lanes[l].Name);
                    for (int n = 0; n < lanes[l].Nodes.Count; n++) branch.Ids.Add(lanes[l].Nodes[n].Id);
                    Branches.Add(branch);

                    for (int g = 0; g < lanes[l].Nodes.Count; g++)
                    {
                        byte id = lanes[l].Nodes[g].Id;
                        var node = new SkillNode(Rect, id, onSelect);
                        if (g < lanes[l].Nodes.Count - 1)
                            node.Connector = AvLay.Solid(Rect, "Connector " + id, AvTheme.Hairline);
                        ticker?.Register(node);
                        Rows.Add(new SkillRow { Id = id, Node = node });
                    }
                }
                Restyle();
            }

            public override float Measure(float width) => SkillBoardLayout.ContentHeight(grades);

            /// <summary>Leftover page height becomes taller nodes (up to this much each), which is what lets a node print what it buys.</summary>
            private const float MaxNodeGrowth = 24f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float growth = grades > 0 ? Mathf.Clamp((s.H - SkillBoardLayout.ContentHeight(grades)) / grades, 0f, MaxNodeGrowth) : 0f;
                float nodeHeight = SkillBoardLayout.NodeHeight + growth;
                float cellWidth = SkillBoardLayout.CellWidth(s.W, lanes.Count);

                float pitch = s.W / legendWords.Length;
                for (int i = 0; i < legendWords.Length; i++)
                {
                    AvLay.Place(legendIcons[i].rectTransform, i * pitch + 2f, 3f, 14f, 14f);
                    AvLay.Place(legendWords[i].rectTransform, i * pitch + 22f, 0f, Math.Max(0f, pitch - 26f), SkillBoardLayout.LegendHeight);
                }

                for (int l = 0; l < lanes.Count; l++)
                    Branches[l].Place(SkillBoardLayout.CellX(0f, cellWidth, l), SkillBoardLayout.HeaderTop, cellWidth);

                for (int g = 0; g < grades; g++)
                {
                    float y = SkillBoardLayout.NodeTop(g) + g * growth;
                    AvLay.Place(tierLabels[g].rectTransform, 0f, y, SkillBoardLayout.Gutter, nodeHeight);
                }

                int index = 0;
                for (int l = 0; l < lanes.Count; l++)
                {
                    float laneX = SkillBoardLayout.CellX(0f, cellWidth, l);
                    for (int g = 0; g < lanes[l].Nodes.Count; g++)
                    {
                        float y = SkillBoardLayout.NodeTop(g) + g * growth;
                        SkillNode node = Rows[index].Node;
                        node.Place(new AvSlot(laneX, y, cellWidth, nodeHeight));
                        if (node.Connector != null)
                            AvLay.Place(node.Connector.rectTransform, laneX + cellWidth * .5f - 1f,
                                y + nodeHeight, 2f, SkillBoardLayout.NodeGap);
                        index++;
                    }
                }
            }

            public override void Restyle()
            {
                Color[] tones = { AvTheme.RailReady, AvTheme.RailInfo, SqdTone.Select, AvTheme.Disabled };
                for (int i = 0; i < legendWords.Length; i++)
                {
                    legendIcons[i].color = tones[i];
                    legendWords[i].color = SqdTone.Dim;
                }
                foreach (TMP_Text tier in tierLabels) tier.color = SqdTone.Caption;
                foreach (SkillBranchRow branch in Branches) branch.Restyle();
            }
        }
    }
}
