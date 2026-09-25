using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// CYBER › ACTIONS — the map abilities the network has earned, grouped by the stage that
    /// unlocks them: the stage-2 basic, the stage-3 mid tier, the stage-4 capstones, then the
    /// base support row. Each row says what the ability does, what it costs in intel and where
    /// it can be used (how many locations provide it and their best radius), so "can I use this,
    /// and from where" is answered on the page. They cost intel, not allocation, and the host
    /// accepts one only when an online, uncompromised location's radius covers the point.
    /// ARM, then right-click the map.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float AbilityRowHeight = 74f;
        private const float AbilityGroupHeight = 22f;

        private sealed class AbilityRow
        {
            public SupportActionDefinition Definition;
            public Image Background;
            public Image Rail;
            public TMP_Text Coverage;
            public TMP_Text Detail;
            public TMP_Text Cost;
            public AvButton Arm;
            public string LastCoverage;
            public string LastCost;
        }

        private readonly List<AbilityRow> abilityRows = new List<AbilityRow>(12);
        private TMP_Text operationsNote;
        private ArmedBanner cyberBanner;

        private void ResetCyberOpsPage()
        {
            abilityRows.Clear();
            operationsNote = null;
            cyberBanner = null;
        }

        private void BuildCyberOpsPage(RectTransform root, Rect body)
        {
            var page = new List<SupportActionDefinition>(8);
            foreach (SupportActionDefinition action in support.Actions)
                if (HomeTab(action) == TabCyber) page.Add(action);
            page.Sort((a, b) => AbilityTier(a).CompareTo(AbilityTier(b)));

            int groups = 0;
            int lastTier = -1;
            for (int i = 0; i < page.Count; i++)
            {
                if (AbilityTier(page[i]) == lastTier) continue;
                lastTier = AbilityTier(page[i]);
                groups++;
            }

            Rect content = PageFrame(root, body, OpsDomain.Cyber, "MAP ABILITIES", CyberActionsSub, SelectCyberSub, CyberSubTips, out operationsNote);
            cyberBanner = BuildArmedBanner(root, new Rect(content.x, content.y, content.width, BannerRowHeight),
                "OPEN CONSOLE", OpenConsole, "Open the network console to breach sites, answer incidents and buy upgrades.");
            var list = new Rect(content.x, content.y - BannerRowHeight - 8f, content.width, content.height - BannerRowHeight - 8f);
            float height = page.Count * AbilityRowHeight + groups * AbilityGroupHeight + SectionGap;
            RectTransform parent = BeginSub(root, list, height, out float x, out float y, out float width);

            if (page.Count == 0)
            {
                // The catalogue resolved nothing; say so in the row the page would have used.
                AvStyled.Box(parent, new Rect(x, y, width, AbilityRowHeight), "card inert");
                AvStyled.Rail(parent, new Rect(x + 4f, y - 8f, 3f, AbilityRowHeight - 16f), "locked");
                AvStyled.Label(parent, new Rect(x + 12f, y - 9f, width - 24f, 16f),
                               "NO ABILITIES", "row-name").color = AvTheme.Dim;
                AvStyled.Label(parent, new Rect(x + 12f, y - 29f, width - 24f, 14f),
                               "This server resolves none of the catalogue.", "row-sub").color = AvTheme.Disabled;
                return;
            }

            lastTier = -1;
            foreach (SupportActionDefinition action in page)
            {
                if (AbilityTier(action) != lastTier)
                {
                    lastTier = AbilityTier(action);
                    AvStyled.Label(parent, new Rect(x, y, width, 14f), AbilityGroup(lastTier), "section-title");
                    y -= AbilityGroupHeight;
                }
                abilityRows.Add(BuildAbilityRow(parent, action, x, y, width));
                y -= AbilityRowHeight;
            }
        }

        private void RefreshCyberOpsPage(bool bypass, CyberNetwork network, double now)
        {
            if (operationsNote == null) return;
            int count = CountActions(TabCyber);
            string title = "MAP ABILITIES · " + count;
            if (operationsNote.text != title) operationsNote.text = title;
            string hint;
            Color tone = AvTheme.Dim;
            if (network == null || !network.HasCommand) hint = "HOLD AN AIRBASE · THE NETWORK COMES UP ON IT BY ITSELF";
            else if (network.CommandCompromised)
            {
                hint = "C2 BREACHED · ABILITIES OFFLINE UNTIL PATCHED";
                tone = AvTheme.RailDanger;
            }
            else
                hint = "INTEL " + Mathf.FloorToInt(network.Intel) + "/" + Mathf.RoundToInt(network.IntelCapacity()) +
                       (network.AnyFoothold(now) ? " · FOOTHOLD -25%" : "") + " · ARM, RIGHT-CLICK INSIDE A RADIUS";
            PaintArmedBanner(cyberBanner, TabCyber, hint, tone, network != null && network.HasCommand && support.CyberEnabled);
            foreach (AbilityRow row in abilityRows) PaintAbilityRow(row, network, now, bypass);
        }

        private AbilityRow BuildAbilityRow(RectTransform parent, SupportActionDefinition action, float x, float y, float width)
        {
            var row = new AbilityRow { Definition = action };
            row.Background = AvKit.Panel(parent, new Rect(x, y, width, AbilityRowHeight), AvTheme.SurfaceInert);
            AvKit.Rule(parent, new Rect(x + 3f, y, width - 3f, 1f), AvTheme.RailInfo.WithAlpha(0.55f));
            AvKit.Rule(parent, new Rect(x + 12f, y - AbilityRowHeight + 1f, width - 24f, 1f), AvTheme.Hairline);
            row.Rail = AvKit.Rule(parent, new Rect(x, y, 3f, AbilityRowHeight), AvTheme.RailInert);
            Image icon = AvKit.Panel(parent, new Rect(x + 12f, y - 7f, 18f, 18f), AvTheme.RailInfo,
                OpsSprites.Glyph(AbilityGlyph(action)));
            icon.raycastTarget = false;
            AvKit.Label(parent, AbilityCode(action), new Rect(x + 34f, y - 8f, 30f, 16f), AvTheme.RailInfo, AvTokens.FontMicro,
                FontStyles.Bold);
            AvStyled.Label(parent, new Rect(x + 70f, y - 8f, width - 70f - 116f, 18f), action.Name,
                "row-name");
            row.Cost = AvStyled.Label(parent, new Rect(x + width - 104f, y - 8f, 92f, 16f), "", "row-value",
                align: TextAlignmentOptions.MidlineRight);
            row.Arm = AvStyled.Button(parent, new Rect(x + width - 86f, y - 40f, 76f, 26f), "ARM", "btn",
                () =>
                {
                    support.Arm(action.Id);
                    nextRefresh = 0f;
                }, AvButtonStyle.Default);
            row.Arm.WithTooltip(action.Name + " — " + (action.Description ?? "") +
                " The host accepts it only when an online location's radius covers the point.");
            row.Detail = SingleLine(AvStyled.Label(parent, new Rect(x + 12f, y - 30f, width - 116f, 14f), action.Description ?? "",
                "row-sub"));
            row.Detail.color = AvTheme.Dim;
            row.Coverage = SingleLine(AvStyled.Label(parent, new Rect(x + 12f, y - 48f, width - 116f, 13f), "",
                "row-sub"));
            return row;
        }

        private void PaintAbilityRow(AbilityRow row, CyberNetwork network, double now, bool bypass)
        {
            // One presenter for every surface (M5): cost and readiness are the words the other lists use.
            SupportActionDefinition action = row.Definition;
            AbilityFacts facts = AbilityStatus.For(support, action, bypass);
            if (row.LastCost != facts.CostText) row.Cost.text = row.LastCost = facts.CostText;
            if (row.LastCoverage != facts.Readiness) row.Coverage.text = row.LastCoverage = facts.Readiness;
            // The description line turns into where the ability can be used once a network exists.
            string where = network != null && network.HasCommand ? Coverage(action, network, now) : action.Description ?? "";
            if (row.Detail.text != where) row.Detail.text = where;
            Color tone = FactsColour(facts.Tone);
            row.Coverage.color = tone == AvTheme.RailInert ? AvTheme.Dim : tone;
            row.Rail.color = tone;
            row.Background.color = facts.Armed ? AvTheme.RailCaution.WithAlpha(0.08f) : AvTheme.SurfaceInert;
            row.Arm.SetEnabled(facts.Enabled);
            row.Arm.SetLatched(facts.Armed);
            row.Arm.SetText(facts.Armed ? "ABORT" : "ARM");
        }

        /// <summary>How many locations can run this ability, and how far their radius reaches.
        /// The page never claims a coverage the host would refuse.</summary>
        private string Coverage(SupportActionDefinition action, CyberNetwork network, double now)
        {
            if (network == null || !network.HasCommand) return "NO NETWORK YET · IT COMES UP ON YOUR CENTRAL AIRBASE";
            if (action.Cap.HasValue)
            {
                int armed = network.CapstoneCount(action.Cap.Value);
                return armed > 0
                    ? "ARMED AT " + armed + (armed == 1 ? " LOCATION" : " LOCATIONS") + " · " +
                      (int)Capstones.RechargeSeconds / 60 + " MIN RECHARGE"
                    : "NO LOCATION IS MASTERED WITH THIS CAPSTONE YET";
            }
            if (!action.Hack.HasValue) return "ANYWHERE ON THE MAP · PAID IN ALLOCATION";
            int tier = CyberLocations.Tier(CyberCatalog.RequiredStage(action.Hack.Value));
            int count = 0;
            float best = 0f;
            for (int i = 0; i < CyberNetwork.SlotCount; i++)
            {
                if (!network.Working(i) || !network.IsHacked(i) || network.Tier(i) < tier) continue;
                count++;
                best = Mathf.Max(best, network.RadiusOf(i, now));
            }
            return count == 0
                ? "NO STAGE-" + CyberCatalog.RequiredStage(action.Hack.Value) + " LOCATION YET · BREACH ONE IN THE CONSOLE"
                : "USABLE FROM " + count + (count == 1 ? " LOCATION" : " LOCATIONS") + " · RADIUS UP TO " +
                  Mathf.RoundToInt(best / 1000f) + " KM";
        }

        private static string AbilityCode(SupportActionDefinition action) =>
            action.IsHack ? CyberCatalog.Code(action.Hack.Value)
            : action.IsCapstone ? Capstones.Code(action.Cap.Value)
            : ActionCode(action.Id);

        private static int AbilityGlyph(SupportActionDefinition action)
        {
            if (action.IsCapstone) return OpsSprites.G.Cyber;
            if (!action.Hack.HasValue) return OpsSprites.G.Flare;
            switch (action.Hack.Value)
            {
                case HackKind.Ping: return OpsSprites.G.Ping;
                case HackKind.Track: return OpsSprites.G.Track;
                case HackKind.Blackout: return OpsSprites.G.Blackout;
                case HackKind.Ghost: return OpsSprites.G.Ghost;
                case HackKind.Scan: return OpsSprites.G.Ping;
                case HackKind.Hijack: return OpsSprites.G.Ghost;
                case HackKind.Overload: return OpsSprites.G.Blackout;
                default: return OpsSprites.G.Spoof;
            }
        }

        /// <summary>Stage that unlocks the row; support rows sort last.</summary>
        private static int AbilityTier(SupportActionDefinition action) =>
            action.Hack.HasValue ? CyberCatalog.RequiredStage(action.Hack.Value)
            : action.Cap.HasValue ? CyberLocations.StageCount
            : CyberLocations.StageCount + 1;

        private static string AbilityGroup(int tier)
        {
            switch (tier)
            {
                case 2: return "STAGE 2 · BASIC";
                case 3: return "STAGE 3 · MID";
                case 4: return "STAGE 4 · CAPSTONE · ONE PER LOCATION";
                default: return "BASE SUPPORT · PAID IN ALLOCATION";
            }
        }
    }
}
