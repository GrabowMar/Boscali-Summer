using System.Collections.Generic;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// CYBER: infrastructure drawn as a node graph instead of a flat row list. Facility nodes
    /// sit in lanes traced back to each independent root and are linked by edges derived from
    /// <see cref="InfoNetwork.Facilities"/>' real prerequisite data (<see cref="SupportCyberGraph"/>),
    /// so the drawn tree can never show a dependency the game doesn't actually enforce. Hacks
    /// attach to their gating facility as small chips; a single shared detail card — not five
    /// full rows — carries the active hack's full description, cost and ARM/ABORT control.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float NodePad = 6f;
        private const float NodeRowGap = 2f;
        private const float NodeHeaderHeight = 16f;
        private const float NodeSummaryHeight = 13f;
        private const float NodeLevelHeight = 13f;
        private const float NodeCostRowHeight = 24f;
        private const float NodeChipHeight = 18f;
        private const float NodeChipRowHeight = 22f;
        private const float NodeChipWidth = 52f;
        private const float NodeChipGap = 4f;
        private const float NodeGapVertical = 18f;
        private const float LaneGap = 12f;

        private sealed class NodeChip
        {
            public HackKind Kind;
            public Image Background;
            public TMP_Text Label;
            public AvButton Hit;
        }

        private sealed class FacilityRow
        {
            public FacilityId Id;
            public Image Rail;
            public TMP_Text Name;
            public Image[] Pips;
            public TMP_Text LevelLine;
            public TMP_Text Cost;
            public AvButton Build;
            public NodeChip[] Chips;
        }

        /// <summary>One hack's computed display state, shared between its chip and the detail
        /// card so the gating logic (identical to the old per-row version) is written once.</summary>
        private struct HackDisplayState
        {
            public HackKind Kind;
            public SupportActionDefinition Definition;
            public bool Armed;
            public bool ButtonReady;
            public float Cost;
            public string RailState;
            public Color StatusColor;
            public string StatusText;
            public string ButtonText;
        }

        private readonly FacilityRow[] facilityRows = new FacilityRow[InfoNetwork.Facilities.Length];
        private readonly HackDisplayState[] hackStates = new HackDisplayState[CyberCatalog.All.Length];

        private TMP_Text cyberSummary;
        private Image[] networkPips;
        private Image[] edgeLines;
        private HackKind? selectedHack;

        private Image detailRail;
        private TMP_Text detailName;
        private TMP_Text detailStatus;
        private TMP_Text detailCost;
        private AvButton detailAction;

        private void ResetCyberPage()
        {
            for (int i = 0; i < facilityRows.Length; i++) facilityRows[i] = null;
            for (int i = 0; i < hackStates.Length; i++) hackStates[i] = default;
            cyberSummary = null;
            networkPips = null;
            edgeLines = null;
            selectedHack = null;
            detailRail = null;
            detailName = null;
            detailStatus = null;
            detailCost = null;
            detailAction = null;
        }

        private void BuildCyberPage(RectTransform parent, Rect body)
        {
            int laneCount = SupportCyberGraph.LaneCount;
            var laneNodes = new List<FacilityId>[laneCount];
            for (int lane = 0; lane < laneCount; lane++) laneNodes[lane] = new List<FacilityId>();
            for (int i = 0; i < SupportCyberGraph.Nodes.Length; i++)
            {
                CyberNodeLayout layout = SupportCyberGraph.Nodes[i];
                laneNodes[layout.Lane].Add(layout.Id);
            }
            for (int lane = 0; lane < laneCount; lane++)
                laneNodes[lane].Sort((a, b) =>
                    SupportCyberGraph.Layout(a).Rank.CompareTo(SupportCyberGraph.Layout(b).Rank));

            float laneWidth = (body.width - SpineInset - LaneGap * (laneCount - 1)) / laneCount;

            AvNode page = AvBox.Column("cyber").Gaps(0f)
                .Add(AvBox.Cell("header").Height(20f))
                .Add(AvBox.Cell("summary").Height(16f))
                .Add(AvBox.Cell("graphTitle").Height(20f));

            AvNode graphRow = AvBox.Row("graph").Gaps(LaneGap);
            for (int lane = 0; lane < laneCount; lane++)
            {
                AvNode laneCol = AvBox.Column("lane" + lane).Width(laneWidth).Gaps(NodeGapVertical);
                List<FacilityId> ids = laneNodes[lane];
                for (int i = 0; i < ids.Count; i++)
                    laneCol.Add(AvBox.Cell("node" + (int)ids[i]).Height(NodeHeight(HasChips(ids[i]))));
                graphRow.Add(laneCol);
            }
            page.Add(graphRow);

            page.Add(AvBox.Cell("detailTitle").Height(20f))
                .Add(AvBox.Cell("detailStrip").Height(96f))
                .Add(AvBox.Filler());

            page.Arrange(body);
            float graphHeight = page.At("graph").height;
            float contentHeight = 56f + graphHeight + 20f + 96f + 12f;
            parent = AvScreen.Scroll(parent, body, contentHeight, out body);
            page.Arrange(body);

            AddScanlineOverlay(parent, body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            Rect header = page.At("header");
            AvStyled.Label(parent, new Rect(header.x + SpineInset, header.y, header.width * 0.5f, 18f),
                "CYBER INFRASTRUCTURE", "section-title");
            AvStyled.Label(parent, new Rect(header.x + header.width * 0.5f, header.y,
                header.width * 0.5f - SpineInset, 18f), "SPEND ALLOCATION · INSTANT",
                "section-title-note", align: TextAlignmentOptions.MidlineRight);
            AvKit.Rule(parent, new Rect(header.x + SpineInset, header.y - 18f,
                header.width - SpineInset, 1f), AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.4f)));

            Rect summary = page.At("summary");
            float pipsWidth = hackStates.Length > 0
                ? InfoNetwork.MaxLevel * InfoNetwork.Facilities.Length * 11f
                : 0f;
            cyberSummary = AvStyled.Label(parent, new Rect(summary.x + SpineInset, summary.y,
                summary.width - SpineInset - pipsWidth - 8f, 15f), "", "row-main");
            networkPips = new Image[InfoNetwork.MaxLevel * InfoNetwork.Facilities.Length];
            for (int i = 0; i < networkPips.Length; i++)
                networkPips[i] = AvKit.Panel(parent,
                    new Rect(summary.x + summary.width - pipsWidth + i * 11f, summary.y - 4f, 8f, 8f),
                    AvTheme.SurfaceInert);

            DrawBandTitle(parent, page.At("graphTitle"), "INFRASTRUCTURE NETWORK",
                "BUILD ORDER FLOWS DOWN EACH BRANCH");

            BuildCyberEdges(parent, page);
            for (int lane = 0; lane < laneCount; lane++)
            {
                List<FacilityId> ids = laneNodes[lane];
                for (int i = 0; i < ids.Count; i++)
                    BuildFacilityNode(parent, page.At("graph.lane" + lane + ".node" + (int)ids[i]), ids[i]);
            }

            DrawBandTitle(parent, page.At("detailTitle"), "OPERATION DETAIL",
                "SELECT OR ARM A NODE'S HACK");
            AddCardGlow(parent, page.At("detailStrip"), AvTheme.RailInfo);
            BuildOperationDetail(parent, page.At("detailStrip"));
        }

        private static bool HasChips(FacilityId id) => HacksFor(id).Count > 0;

        private static float NodeHeight(bool hasChips)
        {
            float height = NodePad * 2f + NodeHeaderHeight + NodeRowGap + NodeSummaryHeight +
                           NodeRowGap + NodeLevelHeight + NodeRowGap + NodeCostRowHeight;
            return hasChips ? height + NodeRowGap + NodeChipRowHeight : height;
        }

        private static List<HackKind> HacksFor(FacilityId id)
        {
            var result = new List<HackKind>(2);
            for (int i = 0; i < CyberCatalog.All.Length; i++)
                if (CyberCatalog.Facility(CyberCatalog.All[i]) == id) result.Add(CyberCatalog.All[i]);
            return result;
        }

        private void BuildCyberEdges(RectTransform parent, AvNode page)
        {
            CyberEdge[] edges = SupportCyberGraph.Edges;
            edgeLines = new Image[edges.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                int parentLane = SupportCyberGraph.Layout(edges[i].Parent).Lane;
                int childLane = SupportCyberGraph.Layout(edges[i].Child).Lane;
                Rect parentRect = page.At("graph.lane" + parentLane + ".node" + (int)edges[i].Parent);
                Rect childRect = page.At("graph.lane" + childLane + ".node" + (int)edges[i].Child);

                float midX = parentRect.x + parentRect.width * 0.5f;
                float parentBottom = parentRect.y - parentRect.height;
                float height = Mathf.Max(1f, parentBottom - childRect.y);
                edgeLines[i] = AvKit.Rule(parent, new Rect(midX - 1f, parentBottom, 2f, height),
                    AvTheme.RailInert);
            }
        }

        private void BuildFacilityNode(RectTransform parent, Rect area, FacilityId id)
        {
            FacilityInfo info = InfoNetwork.Facility(id);
            var row = new FacilityRow { Id = id };

            var card = AvKit.TacticalCard(parent, area, AvTheme.RailInert);
            row.Rail = card.Rail;

            float x = area.x + NodePad;
            float width = area.width - NodePad * 2f;
            float y = area.y - NodePad;

            row.Pips = new Image[InfoNetwork.MaxLevel];
            float pipsWidth = row.Pips.Length * 11f;
            AvStyled.Label(parent, new Rect(x, y - 1f, 28f, 13f), info.Code, "row-sub",
                align: TextAlignmentOptions.MidlineLeft);
            row.Name = AvStyled.Label(parent,
                new Rect(x + 30f, y - 1f, width - 30f - pipsWidth - 4f, 13f), info.Name, "row-name");
            for (int i = 0; i < row.Pips.Length; i++)
                row.Pips[i] = AvKit.Panel(parent,
                    new Rect(x + width - pipsWidth + i * 11f, y - 3f, 8f, 8f), AvTheme.SurfaceInert);
            y -= NodeHeaderHeight + NodeRowGap;

            AvStyled.Label(parent, new Rect(x, y, width, NodeSummaryHeight), info.Summary, "row-sub");
            y -= NodeSummaryHeight + NodeRowGap;

            row.LevelLine = AvStyled.Label(parent, new Rect(x, y, width, NodeLevelHeight), "", "row-sub");
            y -= NodeLevelHeight + NodeRowGap;

            row.Cost = AvStyled.Label(parent, new Rect(x, y - 5f, width - 74f, 15f), "—", "row-value",
                align: TextAlignmentOptions.MidlineLeft);
            row.Build = AvStyled.Button(parent,
                new Rect(x + width - 70f, y - NodeCostRowHeight + 2f, 70f, NodeCostRowHeight - 2f),
                "BUILD", "btn", () => { support.RequestUpgrade(id); nextRefresh = 0f; }, AvButtonStyle.Primary)
                .WithTooltip("Buy the next infrastructure level for this facility.");
            y -= NodeCostRowHeight + NodeRowGap;

            List<HackKind> hacks = HacksFor(id);
            if (hacks.Count > 0)
            {
                row.Chips = new NodeChip[hacks.Count];
                float chipX = x;
                for (int i = 0; i < hacks.Count; i++)
                {
                    Rect chipRect = new Rect(chipX, y, NodeChipWidth, NodeChipHeight);
                    row.Chips[i] = BuildHackChip(parent, chipRect, hacks[i]);
                    chipX += NodeChipWidth + NodeChipGap;
                }
            }

            facilityRows[(int)id] = row;
        }

        private NodeChip BuildHackChip(RectTransform parent, Rect area, HackKind kind)
        {
            (Image background, TMP_Text label) = AvKit.Chip(
                parent, CyberCatalog.Code(kind), area, AvTheme.RailInert, AvTheme.Dim, AvTokens.FontMicro);
            AvButton hit = AvKit.HitButton(parent, area, () => { selectedHack = kind; nextRefresh = 0f; });
            return new NodeChip { Kind = kind, Background = background, Label = label, Hit = hit };
        }

        private void BuildOperationDetail(RectTransform parent, Rect area)
        {
            var card = AvKit.TacticalCard(parent, area, AvTheme.RailInert);
            detailRail = card.Rail;

            float x = area.x + NodePad + 6f;
            float width = area.width - NodePad * 2f - 6f;
            float y = area.y - 8f;

            detailName = AvStyled.Label(parent, new Rect(x, y, width - 100f, 16f),
                "NO OPERATIONS CONFIGURED", "row-name");
            detailCost = AvStyled.Label(parent, new Rect(x + width - 100f, y, 100f, 16f),
                "", "row-value", align: TextAlignmentOptions.MidlineRight);
            y -= 20f;

            detailStatus = AvStyled.Label(parent, new Rect(x, y, width, 42f), "", "row-sub");
            y -= 46f;

            detailAction = AvStyled.Button(parent, new Rect(x, y, width, 26f), "—", "btn",
                null, AvButtonStyle.Primary);
        }

        private SupportActionDefinition FindHackDefinition(HackKind kind)
        {
            foreach (SupportActionDefinition action in support.Actions)
                if (action.IsHack && action.Hack.Value == kind) return action;
            return null;
        }

        // ---- Refresh -----------------------------------------------------------------------

        private void RefreshCyber(bool bypass)
        {
            InfoNetwork info = support.LocalInfo;

            int ready = 0;
            for (int i = 0; i < CyberCatalog.All.Length; i++)
                if (info != null && info.Powers.Has(CyberCatalog.All[i])) ready++;

            if (cyberSummary != null)
            {
                cyberSummary.text = info == null ? "NETWORK DATA UNAVAILABLE"
                    : "NETWORK TIER " + info.Powers.Tier + " · " + ready + "/" + CyberCatalog.All.Length +
                      " OPERATIONS READY" +
                      (info.Powers.Tier > 0 ? " · HACK COST ×" + info.Powers.CostScale.ToString("0.00") : "");
                cyberSummary.color = info == null ? AvTheme.Dim
                    : ready > 0 ? AvTheme.RailReady : AvTheme.Dim;
            }

            int tier = info != null ? info.Powers.Tier : 0;
            if (networkPips != null)
                for (int i = 0; i < networkPips.Length; i++)
                    networkPips[i].color = i < tier ? AvTheme.RailInfo : AvTheme.SurfaceInert;

            RefreshFacilityNodes(info, bypass);
            RefreshEdges(info);
            ComputeHackStates(info, bypass);
            RefreshChips();
            RefreshOperationDetail();
        }

        private void RefreshFacilityNodes(InfoNetwork info, bool bypass)
        {
            for (int i = 0; i < facilityRows.Length; i++)
            {
                FacilityRow row = facilityRows[i];
                if (row == null) continue;
                FacilityId id = (FacilityId)i;
                int level = info != null ? info.Level(id) : 0;
                bool canBuild = info != null && info.CanUpgrade(id);
                float cost = support.FacilityCost(id);
                bool affordable = bypass || support.LocalAllocation + 0.001f >= cost;

                row.Rail.color = level >= InfoNetwork.MaxLevel ? AvTheme.RailReady
                    : level > 0 ? AvTheme.RailInfo : AvTheme.RailInert;
                row.Name.color = level > 0 ? AvTheme.TextPrimary : AvTheme.Dim;
                for (int p = 0; p < row.Pips.Length; p++)
                    row.Pips[p].color = p < level ? AvTheme.RailInfo : AvTheme.SurfaceInert;

                row.LevelLine.text = "LV " + level + "/" + InfoNetwork.MaxLevel + " · " +
                    (level >= InfoNetwork.MaxLevel ? "MAXIMUM LEVEL" : "NEXT: " + InfoNetwork.Facility(id).Levels[level]);
                row.LevelLine.color = level > 0 ? AvTheme.RailInfo : AvTheme.Dim;

                if (canBuild && cost > 0f)
                {
                    row.Cost.text = affordable ? cost.ToString("N0") + " ALLOC" : "NEED " + cost.ToString("N0");
                    row.Cost.color = affordable ? AvTheme.TextPrimary : AvTheme.Warning;
                    row.Build.SetEnabled(affordable && !support.CommandPending);
                    row.Build.SetText(affordable ? "BUILD" : "NO ALLOC");
                }
                else
                {
                    row.Cost.text = level >= InfoNetwork.MaxLevel ? "FULLY BUILT"
                        : info != null ? info.Requirement(id) : "THEATER DATA UNAVAILABLE";
                    row.Cost.color = level >= InfoNetwork.MaxLevel ? AvTheme.RailReady : AvTheme.Warning;
                    row.Build.SetEnabled(false);
                    row.Build.SetText(level >= InfoNetwork.MaxLevel ? "MAX" : "LOCKED");
                    row.Build.WithTooltip(info == null ? "Theater data unavailable."
                        : info.Requirement(id) ?? "Requirement not met.");
                }
            }
        }

        private void RefreshEdges(InfoNetwork info)
        {
            if (edgeLines == null) return;
            CyberEdge[] edges = SupportCyberGraph.Edges;
            for (int i = 0; i < edges.Length; i++)
            {
                int level = info != null ? info.Level(edges[i].Child) : 0;
                edgeLines[i].color = level >= InfoNetwork.MaxLevel
                    ? AvTheme.RailInfo.WithAlpha(0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 1.5f))
                    : level > 0 ? AvTheme.RailInfo : AvTheme.RailInert;
            }
        }

        /// <summary>
        /// One gating pass per hack, identical to what the old per-row build computed —
        /// shared by the compact chips and the single detail card instead of five full rows.
        /// </summary>
        private void ComputeHackStates(InfoNetwork info, bool bypass)
        {
            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;

            for (int i = 0; i < CyberCatalog.All.Length; i++)
            {
                HackKind kind = CyberCatalog.All[i];
                SupportActionDefinition definition = FindHackDefinition(kind);
                bool unlocked = info != null && info.Powers.Has(kind);
                bool armed = definition != null && support.ArmedAction.HasValue &&
                             support.ArmedAction.Value == definition.Id;
                float cost = definition != null ? support.Cost(definition) : 0f;

                string rail;
                string status;
                Color statusColor;
                string button;
                bool ready;

                if (definition == null || !definition.Enabled)
                {
                    rail = "locked"; status = "SERVER DISABLED"; statusColor = AvTheme.Dim;
                    button = "OFF"; ready = false;
                }
                else if (!unlocked)
                {
                    FacilityId facility = CyberCatalog.Facility(kind);
                    string name = InfoNetwork.Facility(facility).Name;
                    byte level = CyberCatalog.RequiredLevel(kind);
                    rail = "locked"; status = "REQUIRES " + name + " LV" + level;
                    statusColor = AvTheme.Warning; button = "LOCKED"; ready = false;
                }
                else if (support.RequestPending)
                {
                    rail = "cooling"; status = "REQUEST PENDING · AWAITING HOST";
                    statusColor = AvTheme.RailInfo; button = "PENDING"; ready = false;
                }
                else if (cooldown > 0.5f)
                {
                    rail = "cooling"; status = "NET COOLING DOWN"; statusColor = AvTheme.RailCaution;
                    button = "WAIT " + Mathf.CeilToInt(cooldown) + "s"; ready = false;
                }
                else if (!bypass && allocation + 0.001f < cost)
                {
                    rail = "danger"; status = "INSUFFICIENT ALLOCATION"; statusColor = AvTheme.RailDanger;
                    button = "NO ALLOC"; ready = false;
                }
                else if (armed)
                {
                    rail = "armed"; status = "ARMED · RIGHT-CLICK TARGET AREA";
                    statusColor = AvTheme.RailCaution; button = "ABORT"; ready = true;
                }
                else
                {
                    rail = "ready"; status = "ONLINE · READY TO ARM"; statusColor = AvTheme.RailReady;
                    button = "ARM"; ready = true;
                }

                hackStates[i] = new HackDisplayState
                {
                    Kind = kind,
                    Definition = definition,
                    Armed = armed,
                    ButtonReady = ready,
                    Cost = cost,
                    RailState = rail,
                    StatusColor = statusColor,
                    StatusText = status,
                    ButtonText = button,
                };
            }
        }

        private HackDisplayState HackState(HackKind kind)
        {
            for (int i = 0; i < hackStates.Length; i++)
                if (hackStates[i].Kind == kind) return hackStates[i];
            return default;
        }

        private void RefreshChips()
        {
            for (int i = 0; i < facilityRows.Length; i++)
            {
                FacilityRow row = facilityRows[i];
                if (row?.Chips == null) continue;
                for (int c = 0; c < row.Chips.Length; c++)
                {
                    NodeChip chip = row.Chips[c];
                    HackDisplayState state = HackState(chip.Kind);
                    Color color = RailColour(state.RailState);
                    chip.Label.color = color;
                    chip.Background.color = new Color(color.r * 0.18f, color.g * 0.18f, color.b * 0.18f, 0.85f);

                    string name = state.Definition != null ? state.Definition.Name : CyberCatalog.Name(chip.Kind);
                    string desc = state.Definition != null
                        ? state.Definition.Description
                        : CyberCatalog.Description(chip.Kind);
                    chip.Hit.WithTooltip(name + " — " + desc);
                }
            }
        }

        private void RefreshOperationDetail()
        {
            if (detailName == null) return;

            HackKind? active = null;
            for (int i = 0; i < hackStates.Length; i++)
                if (hackStates[i].Armed) { active = hackStates[i].Kind; break; }

            if (active == null && selectedHack.HasValue) active = selectedHack;

            if (active == null)
                for (int i = 0; i < hackStates.Length; i++)
                    if (hackStates[i].ButtonReady) { active = hackStates[i].Kind; break; }

            if (active == null && hackStates.Length > 0) active = hackStates[0].Kind;

            if (active == null)
            {
                detailRail.color = AvTheme.RailInert;
                detailName.text = "NO OPERATIONS CONFIGURED";
                detailStatus.text = "";
                detailCost.text = "";
                detailAction.SetEnabled(false);
                detailAction.SetText("—");
                return;
            }

            HackDisplayState state = HackState(active.Value);
            string name = state.Definition != null ? state.Definition.Name : CyberCatalog.Name(active.Value);
            string desc = state.Definition != null
                ? state.Definition.Description
                : CyberCatalog.Description(active.Value);

            detailRail.color = RailColour(state.RailState);
            detailName.text = name;
            detailStatus.text = state.StatusText + "\n" + desc;
            detailStatus.color = state.StatusColor;
            detailCost.text = state.Cost > 0f ? state.Cost.ToString("N0") : "—";
            detailCost.color = state.StatusColor;

            detailAction.SetText(state.ButtonText);
            detailAction.SetEnabled(state.ButtonReady);
            detailAction.SetLatched(state.Armed);
            detailAction.WithTooltip(name + " — " + desc);

            if (state.Definition != null)
            {
                SupportActionId id = state.Definition.Id;
                detailAction.SetAction(() => { support.Request(id); nextRefresh = 0f; });
            }
            else
            {
                detailAction.SetAction(null);
            }
        }
    }
}
