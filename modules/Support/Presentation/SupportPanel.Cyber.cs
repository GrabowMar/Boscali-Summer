using System.Collections.Generic;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    internal sealed partial class SupportPanel
    {
        private sealed class FacilityRow
        {
            public Image Rail;
            public TMP_Text Code;
            public TMP_Text Name;
            public TMP_Text Summary;
            public TMP_Text Effect;
            public TMP_Text Level;
            public TMP_Text Cost;
            public Image[] Pips;
            public AvButton Build;
        }

        private readonly FacilityRow[] facilityRows = new FacilityRow[InfoNetwork.Facilities.Length];
        private readonly List<StrikeRow> hackRows = new List<StrikeRow>(CyberCatalog.All.Length);
        private TMP_Text cyberSummary;

        private void ResetCyberPage()
        {
            for (int i = 0; i < facilityRows.Length; i++) facilityRows[i] = null;
            hackRows.Clear();
            cyberSummary = null;
        }

        private void BuildCyberPage(RectTransform parent, Rect body)
        {
            int actionCount = 0;
            foreach (var action in support.Actions)
                if (action.IsHack) actionCount++;

            AvNode page = AvBox.Column("cyber").Gaps(0f)
                .Add(AvBox.Cell("header").Height(20f))
                .Add(AvBox.Cell("summary").Height(16f))
                .Add(AvBox.Cell("infraTitle").Height(20f));
            for (int i = 0; i < facilityRows.Length; i++)
                page.Add(AvBox.Cell("fac" + i).Height(66f));
            page.Add(AvBox.Cell("opTitle").Height(20f));
            if (actionCount == 0)
                page.Add(AvBox.Cell("hackNone").Height(20f));
            else
                for (int i = 0; i < actionCount; i++)
                    page.Add(AvBox.Cell("hack" + i).Height(54f));
            page.Add(AvBox.Filler());

            int rows = actionCount == 0 ? 1 : actionCount;
            float contentHeight = 56f + facilityRows.Length * 66f + 20f + rows * 54f + 12f;
            page.Arrange(body);
            parent = AvScreen.Scroll(parent, body, contentHeight, out body);
            page.Arrange(body);

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
            cyberSummary = AvStyled.Label(parent, new Rect(summary.x + SpineInset, summary.y,
                summary.width - SpineInset, 15f), "", "row-main");

            DrawBandTitle(parent, page.At("infraTitle"), "FACILITIES", "LEVELS UNLOCK AND STRENGTHEN HACKS");
            for (int i = 0; i < facilityRows.Length; i++)
                BuildFacilityRow(parent, page.At("fac" + i), i);

            DrawBandTitle(parent, page.At("opTitle"), "CYBER OPERATIONS", "ARM, THEN RIGHT-CLICK THE TARGET AREA");
            if (actionCount == 0)
            {
                Rect none = page.At("hackNone");
                AvStyled.Label(parent, new Rect(none.x + SpineInset, none.y, none.width - SpineInset, 16f),
                    "CYBER OPERATIONS DISABLED IN CONFIG", "row-sub");
            }
            else
            {
                int slot = 0;
                foreach (var action in support.Actions)
                {
                    if (!action.IsHack) continue;
                    BuildHackRow(parent, page.At("hack" + slot), action);
                    slot++;
                }
            }
        }

        private void BuildFacilityRow(RectTransform parent, Rect area, int index)
        {
            FacilityInfo info = InfoNetwork.Facilities[index];
            float x = area.x + SpineInset;
            float width = area.width - SpineInset;
            var row = new FacilityRow();

            AvKit.Rule(parent, new Rect(x, area.y, width, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));
            row.Rail = AvStyled.Rail(parent, new Rect(x, area.y - 10f, 3f, 46f), "locked");
            row.Code = AvStyled.Label(parent, new Rect(x + 12f, area.y - 2f, 34f, 16f),
                info.Code, "row-sub", align: TextAlignmentOptions.MidlineLeft);
            row.Name = AvStyled.Label(parent, new Rect(x + 48f, area.y - 2f, width - 202f, 16f),
                info.Name, "row-name");
            row.Summary = AvStyled.Label(parent, new Rect(x + 48f, area.y - 20f, width - 202f, 14f),
                info.Summary, "row-sub");
            row.Effect = AvStyled.Label(parent, new Rect(x + 48f, area.y - 38f, width - 202f, 15f),
                "", "row-sub");

            float right = x + width - 138f;
            row.Pips = new Image[InfoNetwork.MaxLevel];
            for (int i = 0; i < row.Pips.Length; i++)
                row.Pips[i] = AvKit.Panel(parent, new Rect(right + i * 11f, area.y - 7f, 8f, 8f),
                    AvTheme.SurfaceInert);
            row.Level = AvStyled.Label(parent, new Rect(right + 36f, area.y - 7f, 102f, 14f),
                "LV 0", "row-value", align: TextAlignmentOptions.MidlineLeft);
            row.Cost = AvStyled.Label(parent, new Rect(right, area.y - 24f, 138f, 14f),
                "—", "row-value", align: TextAlignmentOptions.MidlineRight);
            int captured = index;
            row.Build = AvStyled.Button(parent, new Rect(right, area.y - 42f, 138f, 24f),
                "BUILD", "btn",
                () => { support.RequestUpgrade((FacilityId)captured); nextRefresh = 0f; },
                AvButtonStyle.Primary)
                .WithTooltip("Buy the next infrastructure level for this facility.");
            facilityRows[index] = row;
        }

        private void BuildHackRow(RectTransform parent, Rect area, SupportActionDefinition definition)
        {
            float x = area.x + SpineInset;
            float width = area.width - SpineInset;
            var row = new StrikeRow { Definition = definition };

            AvKit.Rule(parent, new Rect(x, area.y, width, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));
            row.Rail = AvStyled.Rail(parent, new Rect(x, area.y - 11f, 3f, 38f), "locked");
            row.Code = AvStyled.Label(parent, new Rect(x + 12f, area.y - 3f, 32f, 16f),
                CyberCatalog.Code(definition.Hack.Value), "row-sub", align: TextAlignmentOptions.MidlineLeft);
            row.Name = AvStyled.Label(parent, new Rect(x + 48f, area.y - 2f, width - 200f, 16f),
                definition.Name, "row-name");
            row.Status = AvStyled.Label(parent, new Rect(x + 48f, area.y - 20f, width - 200f, 14f),
                definition.Description, "row-sub");
            row.Status2 = AvStyled.Label(parent, new Rect(x + 48f, area.y - 37f, width - 200f, 14f),
                "", "row-sub");

            float right = x + width - 96f;
            row.Cost = AvStyled.Label(parent, new Rect(right, area.y - 2f, 96f, 15f),
                "", "row-value", align: TextAlignmentOptions.MidlineRight);
            SupportActionId id = definition.Id;
            row.Action = AvStyled.Button(parent, new Rect(right, area.y - 22f, 96f, 26f),
                "ARM", "btn",
                () => { support.Request(id); nextRefresh = 0f; },
                AvButtonStyle.Primary)
                .WithTooltip(definition.Name + " — " + definition.Description);
            hackRows.Add(row);
        }

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

            for (int i = 0; i < facilityRows.Length; i++)
            {
                FacilityRow row = facilityRows[i];
                if (row == null) continue;
                FacilityId id = (FacilityId)i;
                int level = info != null ? info.Level(id) : 0;
                bool canBuild = info != null && info.CanUpgrade(id);
                float cost = support.FacilityCost(id);
                bool affordable = bypass || support.LocalAllocation + 0.001f >= cost;

                row.Rail.color = level > 0 ? AvTheme.RailInfo : AvTheme.Dim;
                row.Name.color = level > 0 ? AvTheme.TextPrimary : AvTheme.Dim;
                for (int p = 0; p < row.Pips.Length; p++)
                    row.Pips[p].color = p < level ? AvTheme.RailInfo : AvTheme.SurfaceInert;
                row.Level.text = "LV " + level + "/" + InfoNetwork.MaxLevel;
                row.Level.color = level >= InfoNetwork.MaxLevel ? AvTheme.RailReady : AvTheme.TextPrimary;
                row.Effect.text = level >= InfoNetwork.MaxLevel ? "MAXIMUM LEVEL"
                    : "NEXT: " + InfoNetwork.Facility(id).Levels[level];
                row.Effect.color = level > 0 ? AvTheme.RailInfo : AvTheme.Dim;

                if (canBuild && cost > 0f)
                {
                    row.Cost.text = affordable ? cost.ToString("N0") + " ALLOC"
                        : "NEED " + cost.ToString("N0");
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

            float allocation = support.LocalAllocation;
            float cooldown = support.LocalCooldownRemaining;
            for (int i = 0; i < hackRows.Count; i++)
            {
                StrikeRow row = hackRows[i];
                SupportActionDefinition def = row.Definition;
                HackKind kind = def.Hack.Value;
                bool unlocked = info != null && info.Powers.Has(kind);
                bool isArmed = support.ArmedAction.HasValue && support.ArmedAction.Value == def.Id;
                float cost = support.Cost(def);
                row.Cost.text = cost > 0f ? cost.ToString("N0") : "—";

                if (!def.Enabled)
                {
                    SetRowState(row, "locked", "SERVER DISABLED", AvTheme.Dim, "OFF", false, false);
                }
                else if (!unlocked)
                {
                    FacilityId facility = CyberCatalog.Facility(kind);
                    string name = InfoNetwork.Facility(facility).Name;
                    byte level = CyberCatalog.RequiredLevel(kind);
                    SetRowState(row, "locked",
                        "REQUIRES " + name + " LV" + level,
                        AvTheme.Warning, "LOCKED", false, false);
                }
                else if (support.RequestPending)
                {
                    SetRowState(row, "cooling", "REQUEST PENDING · AWAITING HOST",
                        AvTheme.RailInfo, "PENDING", false, false);
                }
                else if (cooldown > 0.5f)
                {
                    SetRowState(row, "cooling", "NET COOLING DOWN", AvTheme.RailCaution,
                        "WAIT " + Mathf.CeilToInt(cooldown) + "s", false, false);
                }
                else if (!bypass && allocation + 0.001f < cost)
                {
                    SetRowState(row, "danger", "INSUFFICIENT ALLOCATION", AvTheme.RailDanger,
                        "NO ALLOC", false, false);
                }
                else if (isArmed)
                {
                    SetRowState(row, "armed", "ARMED · RIGHT-CLICK TARGET AREA",
                        AvTheme.RailCaution, "ABORT", true, true);
                }
                else
                {
                    SetRowState(row, "ready", "ONLINE · READY TO ARM",
                        AvTheme.RailReady, "ARM", true, false);
                }

                row.Cost.color = row.Status.color;
            }
        }
    }
}
