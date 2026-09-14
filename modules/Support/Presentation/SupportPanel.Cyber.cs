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
    /// The facility node-graph renderer shared by CYBER and EW: nodes sit in lanes traced
    /// back to each independent root and are linked by edges derived from
    /// <see cref="InfoNetwork.Facilities"/>' real prerequisite data (<see cref="SupportCyberGraph"/>),
    /// so the drawn tree can never show a dependency the game doesn't actually enforce. Hacks
    /// attach to their gating facility as small chips; a single shared detail card — not five
    /// full rows — carries the active hack's full description, cost and ARM/ABORT control.
    ///
    /// CYBER and EW each own one <see cref="FacilityGraphSection"/> — a fully separate set of
    /// widgets and derived state for their own facility/hack subset — built and refreshed
    /// through the same methods below. Splitting SIGINT/CRYPTO from DISRUPT/EW into two tabs
    /// is therefore a filter on which ids each section covers, not two hand-written copies of
    /// this rendering code.
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
        private const float DetailStripHeight = 96f;
        private const int DetailMaxLines = 2;

        private static readonly FacilityId[] CyberFacilities = { FacilityId.Sigint, FacilityId.Crypto };
        private static readonly HackKind[] CyberHacks = { HackKind.Ping, HackKind.Track };
        private static readonly FacilityId[] EwFacilities = { FacilityId.Disrupt, FacilityId.Ew };
        private static readonly HackKind[] EwHacks = { HackKind.Blackout, HackKind.Ghost, HackKind.Spoof };

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
        /// card so the gating logic is written once.</summary>
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

        /// <summary>Everything one tab's facility-graph + hack-detail card needs, kept
        /// per-tab so CYBER and EW each own a live graph without sharing widgets.</summary>
        private sealed class FacilityGraphSection
        {
            public FacilityId[] FacilityIds;
            public HackKind[] HackIds;
            public readonly Dictionary<FacilityId, FacilityRow> Rows = new Dictionary<FacilityId, FacilityRow>();
            public HackDisplayState[] HackStates;
            public CyberEdge[] Edges;
            public TMP_Text Summary;
            public Image[] Pips;
            public Image[] EdgeLines;
            public HackKind? SelectedHack;
            public Image DetailRail;
            public TMP_Text DetailName;
            public TMP_Text DetailStatus;
            public TMP_Text DetailCost;
            public AvButton DetailAction;
        }

        private FacilityGraphSection cyberGraph;

        private void ResetCyberPage()
        {
            cyberGraph = null;
            cyberScan = default;
        }

        private void BuildCyberPage(RectTransform parent, Rect body)
        {
            cyberGraph = new FacilityGraphSection { FacilityIds = CyberFacilities, HackIds = CyberHacks };

            AvNode page = AvBox.Column("cyber").Gaps(0f)
                .Add(AvBox.Cell("header").Height(20f))
                .Add(AvBox.Cell("summary").Height(16f));
            AppendGraphBlock(page, cyberGraph);
            page.Add(AvBox.Filler());

            page.Arrange(body);
            float graphHeight = page.At("graph").height;
            float contentHeight = 20f + 16f + 18f + graphHeight + 18f + DetailStripHeight + 20f;
            parent = AvScreen.Scroll(parent, body, contentHeight, out body);
            page.Arrange(body);

            AddScanlineOverlay(parent, body);
            cyberScan = BuildScanSweep(parent, body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            Rect header = page.At("header");
            AvStyled.Label(parent, new Rect(header.x + SpineInset, header.y, header.width * 0.5f, 18f),
                "CYBER INFRASTRUCTURE", "section-title");
            AvStyled.Label(parent, new Rect(header.x + header.width * 0.5f, header.y,
                header.width * 0.5f - SpineInset, 18f), "SIGNALS INTELLIGENCE · SPEND ALLOCATION",
                "section-title-note", align: TextAlignmentOptions.MidlineRight);
            AvKit.Rule(parent, new Rect(header.x + SpineInset, header.y - 18f,
                header.width - SpineInset, 1f), AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.4f)));

            Rect summary = page.At("summary");
            BuildGraphSummary(parent, summary, cyberGraph);

            BuildGraphBlockWidgets(parent, page, cyberGraph,
                "INFRASTRUCTURE NETWORK", "BUILD ORDER FLOWS DOWN EACH BRANCH");
        }

        /// <summary>The network-tier caption plus its level pips, sized to the section's own
        /// facility subset rather than the game's whole cyber network.</summary>
        private static void BuildGraphSummary(RectTransform parent, Rect area, FacilityGraphSection section)
        {
            int pipCount = InfoNetwork.MaxLevel * section.FacilityIds.Length;
            float pipsWidth = pipCount * 11f;
            section.Summary = AvStyled.Label(parent, new Rect(area.x + SpineInset, area.y,
                area.width - SpineInset - pipsWidth - 8f, 15f), "", "row-main");
            section.Pips = new Image[pipCount];
            for (int i = 0; i < section.Pips.Length; i++)
                section.Pips[i] = AvKit.Panel(parent,
                    new Rect(area.x + area.width - pipsWidth + i * 11f, area.y - 4f, 8f, 8f),
                    AvTheme.SurfaceInert);
        }

        /// <summary>Phase 1 (before Arrange): appends the graph-title, graph and detail-card
        /// cells to <paramref name="page"/> for exactly <paramref name="section"/>'s facilities.</summary>
        private static void AppendGraphBlock(AvNode page, FacilityGraphSection section)
        {
            page.Add(AvBox.Cell("graphTitle").Height(18f));
            AppendGraphRow(page, "graph", section.FacilityIds);
            page.Add(AvBox.Cell("detailTitle").Height(18f))
                .Add(AvBox.Cell("detailStrip").Height(DetailStripHeight));
        }

        /// <summary>Phase 2 (after Arrange): the actual widgets for the block appended above.</summary>
        private void BuildGraphBlockWidgets(RectTransform parent, AvNode page, FacilityGraphSection section,
            string title, string note)
        {
            DrawBandTitle(parent, page.At("graphTitle"), title, note);
            BuildGraphNodes(parent, page, "graph", section);
            DrawBandTitle(parent, page.At("detailTitle"), "OPERATION DETAIL", "SELECT OR ARM A NODE'S HACK");
            AddCardGlow(parent, page.At("detailStrip"), AvTheme.RailInfo);
            BuildOperationDetail(parent, page.At("detailStrip"), section);
        }

        /// <summary>Groups exactly these facilities by their real <see cref="SupportCyberGraph"/>
        /// lane (so a filtered tab never has to know the graph's shape) and appends the
        /// resulting lane columns as a row cell named <paramref name="cellName"/>.</summary>
        private static AvNode AppendGraphRow(AvNode page, string cellName, FacilityId[] ids)
        {
            var laneOrder = new List<int>();
            var laneNodes = new Dictionary<int, List<FacilityId>>();
            for (int i = 0; i < ids.Length; i++)
            {
                int lane = SupportCyberGraph.Layout(ids[i]).Lane;
                if (!laneNodes.TryGetValue(lane, out List<FacilityId> list))
                {
                    list = new List<FacilityId>();
                    laneNodes[lane] = list;
                    laneOrder.Add(lane);
                }
                list.Add(ids[i]);
            }
            laneOrder.Sort();
            foreach (List<FacilityId> list in laneNodes.Values)
                list.Sort((a, b) => SupportCyberGraph.Layout(a).Rank.CompareTo(SupportCyberGraph.Layout(b).Rank));

            AvNode graphRow = AvBox.Row(cellName).Gaps(LaneGap);
            for (int l = 0; l < laneOrder.Count; l++)
            {
                AvNode laneCol = AvBox.Column("lane" + laneOrder[l]).Grow().Gaps(NodeGapVertical);
                List<FacilityId> list = laneNodes[laneOrder[l]];
                for (int i = 0; i < list.Count; i++)
                    laneCol.Add(AvBox.Cell("node" + (int)list[i]).Height(NodeHeight(HasChips(list[i]))));
                graphRow.Add(laneCol);
            }
            page.Add(graphRow);
            return graphRow;
        }

        private void BuildGraphNodes(RectTransform parent, AvNode page, string cellName, FacilityGraphSection section)
        {
            BuildGraphEdges(parent, page, cellName, section);
            for (int i = 0; i < section.FacilityIds.Length; i++)
            {
                FacilityId id = section.FacilityIds[i];
                int lane = SupportCyberGraph.Layout(id).Lane;
                Rect area = page.At(cellName + ".lane" + lane + ".node" + (int)id);
                BuildFacilityNode(parent, area, id, section);
            }
        }

        private void BuildGraphEdges(RectTransform parent, AvNode page, string cellName, FacilityGraphSection section)
        {
            var idSet = new HashSet<FacilityId>(section.FacilityIds);
            CyberEdge[] all = SupportCyberGraph.Edges;
            var edges = new List<CyberEdge>();
            for (int i = 0; i < all.Length; i++)
                if (idSet.Contains(all[i].Parent) && idSet.Contains(all[i].Child)) edges.Add(all[i]);

            section.Edges = edges.ToArray();
            section.EdgeLines = new Image[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                int parentLane = SupportCyberGraph.Layout(edges[i].Parent).Lane;
                int childLane = SupportCyberGraph.Layout(edges[i].Child).Lane;
                Rect parentRect = page.At(cellName + ".lane" + parentLane + ".node" + (int)edges[i].Parent);
                Rect childRect = page.At(cellName + ".lane" + childLane + ".node" + (int)edges[i].Child);

                float midX = parentRect.x + parentRect.width * 0.5f;
                float parentBottom = parentRect.y - parentRect.height;
                float height = Mathf.Max(1f, parentBottom - childRect.y);
                section.EdgeLines[i] = AvKit.Rule(parent, new Rect(midX - 1f, parentBottom, 2f, height),
                    AvTheme.RailInert);
            }
        }

        private static bool HasChips(FacilityId id) => HacksFor(id).Count > 0;

        private static float NodeHeight(bool hasChips)
        {
            float height = NodePad * 2f + NodeHeaderHeight + NodeRowGap + NodeSummaryHeight +
                           NodeRowGap + NodeLevelHeight + NodeRowGap + NodeCostRowHeight;
            return hasChips ? height + NodeRowGap + NodeChipRowHeight : height;
        }

        /// <summary>Every hack gated on this facility. The facility→hack mapping never crosses
        /// the CYBER/EW split (Sigint's hacks are never EW's, and vice versa), so this needs no
        /// section filter — it already returns exactly the right set for whichever tab asks.</summary>
        private static List<HackKind> HacksFor(FacilityId id)
        {
            var result = new List<HackKind>(2);
            for (int i = 0; i < CyberCatalog.All.Length; i++)
                if (CyberCatalog.Facility(CyberCatalog.All[i]) == id) result.Add(CyberCatalog.All[i]);
            return result;
        }

        private void BuildFacilityNode(RectTransform parent, Rect area, FacilityId id, FacilityGraphSection section)
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
                    row.Chips[i] = BuildHackChip(parent, chipRect, hacks[i], section);
                    chipX += NodeChipWidth + NodeChipGap;
                }
            }

            section.Rows[id] = row;
        }

        private NodeChip BuildHackChip(RectTransform parent, Rect area, HackKind kind, FacilityGraphSection section)
        {
            (Image background, TMP_Text label) = AvKit.Chip(
                parent, CyberCatalog.Code(kind), area, AvTheme.RailInert, AvTheme.Dim, AvTokens.FontMicro);
            AvButton hit = AvKit.HitButton(parent, area, () => { section.SelectedHack = kind; nextRefresh = 0f; });
            return new NodeChip { Kind = kind, Background = background, Label = label, Hit = hit };
        }

        private void BuildOperationDetail(RectTransform parent, Rect area, FacilityGraphSection section)
        {
            var card = AvKit.TacticalCard(parent, area, AvTheme.RailInert);
            section.DetailRail = card.Rail;

            float x = area.x + NodePad + 6f;
            float width = area.width - NodePad * 2f - 6f;
            float y = area.y - 8f;

            section.DetailName = AvStyled.Label(parent, new Rect(x, y, width - 100f, 16f),
                "NO OPERATIONS CONFIGURED", "row-name");
            section.DetailCost = AvStyled.Label(parent, new Rect(x + width - 100f, y, 100f, 16f),
                "", "row-value", align: TextAlignmentOptions.MidlineRight);
            y -= 20f;

            section.DetailStatus = AvStyled.Label(parent, new Rect(x, y, width, 42f), "", "row-sub");
            section.DetailStatus.maxVisibleLines = DetailMaxLines;
            y -= 46f;

            section.DetailAction = AvStyled.Button(parent, new Rect(x, y, width, 26f), "—", "btn",
                null, AvButtonStyle.Primary);
        }

        private SupportActionDefinition FindHackDefinition(HackKind kind)
        {
            foreach (SupportActionDefinition action in support.Actions)
                if (action.IsHack && action.Hack.Value == kind) return action;
            return null;
        }

        // ---- Refresh -----------------------------------------------------------------------

        private void RefreshCyber(bool bypass) => RefreshGraphSection(support.LocalInfo, bypass, cyberGraph);

        private void RefreshGraphSection(InfoNetwork info, bool bypass, FacilityGraphSection section)
        {
            if (section == null) return;

            int levelSum = 0;
            for (int i = 0; i < section.FacilityIds.Length; i++)
                levelSum += info != null ? info.Level(section.FacilityIds[i]) : 0;

            int ready = 0;
            for (int i = 0; i < section.HackIds.Length; i++)
                if (info != null && info.Powers.Has(section.HackIds[i])) ready++;

            if (section.Summary != null)
            {
                section.Summary.text = info == null ? "NETWORK DATA UNAVAILABLE"
                    : "TIER " + levelSum + " · " + ready + "/" + section.HackIds.Length + " OPERATIONS READY" +
                      (info.Powers.Tier > 0 ? " · HACK COST ×" + info.Powers.CostScale.ToString("0.00") : "");
                section.Summary.color = info == null ? AvTheme.Dim
                    : ready > 0 ? AvTheme.RailReady : AvTheme.Dim;
            }

            if (section.Pips != null)
                for (int i = 0; i < section.Pips.Length; i++)
                    section.Pips[i].color = i < levelSum ? AvTheme.RailInfo : AvTheme.SurfaceInert;

            RefreshFacilityNodes(info, bypass, section);
            RefreshEdges(section);
            ComputeHackStates(info, bypass, section);
            RefreshChips(section);
            RefreshOperationDetail(section);
        }

        private void RefreshFacilityNodes(InfoNetwork info, bool bypass, FacilityGraphSection section)
        {
            foreach (KeyValuePair<FacilityId, FacilityRow> entry in section.Rows)
            {
                FacilityRow row = entry.Value;
                FacilityId id = entry.Key;
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

        private void RefreshEdges(FacilityGraphSection section)
        {
            if (section.EdgeLines == null) return;
            CyberEdge[] edges = section.Edges;
            InfoNetwork info = support.LocalInfo;
            for (int i = 0; i < edges.Length; i++)
            {
                int level = info != null ? info.Level(edges[i].Child) : 0;
                section.EdgeLines[i].color = level >= InfoNetwork.MaxLevel
                    ? AvTheme.RailInfo.WithAlpha(0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 1.5f))
                    : level > 0 ? AvTheme.RailInfo : AvTheme.RailInert;
            }
        }

        /// <summary>One gating pass per hack in this section, shared by its compact chips and
        /// its single detail card. A Disrupt/Ew hack additionally needs a live EW asset near
        /// the target — this is an advisory client-side preview only; the host re-checks
        /// distance authoritatively in <see cref="Actions.HackAction.Execute"/>.</summary>
        private void ComputeHackStates(InfoNetwork info, bool bypass, FacilityGraphSection section)
        {
            if (section.HackStates == null || section.HackStates.Length != section.HackIds.Length)
                section.HackStates = new HackDisplayState[section.HackIds.Length];

            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;

            for (int i = 0; i < section.HackIds.Length; i++)
            {
                HackKind kind = section.HackIds[i];
                SupportActionDefinition definition = FindHackDefinition(kind);
                bool unlocked = info != null && info.Powers.Has(kind);
                bool armed = definition != null && support.ArmedAction.HasValue &&
                             support.ArmedAction.Value == definition.Id;
                float cost = definition != null ? support.Cost(definition) : 0f;

                FacilityId facility = CyberCatalog.Facility(kind);
                bool needsEwAsset = (facility == FacilityId.Disrupt || facility == FacilityId.Ew) &&
                                    support.LocalEwAssetState == EwAssetState.None;

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
                else if (needsEwAsset)
                {
                    rail = "cooling"; status = "NEEDS EW ASSET NEARBY · DEPLOY IN 01";
                    statusColor = AvTheme.RailCaution; button = "NO EW ASSET"; ready = false;
                }
                else
                {
                    rail = "ready"; status = "ONLINE · READY TO ARM"; statusColor = AvTheme.RailReady;
                    button = "ARM"; ready = true;
                }

                section.HackStates[i] = new HackDisplayState
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

        private static HackDisplayState HackState(FacilityGraphSection section, HackKind kind)
        {
            if (section.HackStates != null)
                for (int i = 0; i < section.HackStates.Length; i++)
                    if (section.HackStates[i].Kind == kind) return section.HackStates[i];
            return default;
        }

        private void RefreshChips(FacilityGraphSection section)
        {
            foreach (FacilityRow row in section.Rows.Values)
            {
                if (row.Chips == null) continue;
                for (int c = 0; c < row.Chips.Length; c++)
                {
                    NodeChip chip = row.Chips[c];
                    HackDisplayState state = HackState(section, chip.Kind);
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

        private void RefreshOperationDetail(FacilityGraphSection section)
        {
            if (section.DetailName == null) return;

            HackKind? active = null;
            for (int i = 0; i < section.HackStates.Length; i++)
                if (section.HackStates[i].Armed) { active = section.HackStates[i].Kind; break; }

            if (active == null && section.SelectedHack.HasValue) active = section.SelectedHack;

            if (active == null)
                for (int i = 0; i < section.HackStates.Length; i++)
                    if (section.HackStates[i].ButtonReady) { active = section.HackStates[i].Kind; break; }

            if (active == null && section.HackStates.Length > 0) active = section.HackStates[0].Kind;

            if (active == null)
            {
                section.DetailRail.color = AvTheme.RailInert;
                section.DetailName.text = "NO OPERATIONS CONFIGURED";
                section.DetailStatus.text = "";
                section.DetailCost.text = "";
                section.DetailAction.SetEnabled(false);
                section.DetailAction.SetText("—");
                return;
            }

            HackDisplayState state = HackState(section, active.Value);
            string name = state.Definition != null ? state.Definition.Name : CyberCatalog.Name(active.Value);
            string desc = state.Definition != null
                ? state.Definition.Description
                : CyberCatalog.Description(active.Value);

            section.DetailRail.color = RailColour(state.RailState);
            section.DetailName.text = name;
            section.DetailStatus.text = state.StatusText + "\n" + desc;
            section.DetailStatus.color = state.StatusColor;
            section.DetailCost.text = state.Cost > 0f ? state.Cost.ToString("N0") : "—";
            section.DetailCost.color = state.StatusColor;

            section.DetailAction.SetText(state.ButtonText);
            section.DetailAction.SetEnabled(state.ButtonReady);
            section.DetailAction.SetLatched(state.Armed);
            section.DetailAction.WithTooltip(name + " — " + desc);

            if (state.Definition != null)
            {
                SupportActionId id = state.Definition.Id;
                section.DetailAction.SetAction(() => { support.Request(id); nextRefresh = 0f; });
            }
            else
            {
                section.DetailAction.SetAction(null);
            }
        }
    }
}
