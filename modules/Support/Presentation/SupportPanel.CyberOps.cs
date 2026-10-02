using NOAvionics;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// CYBER › ACTIONS — live map effects, ordered lease effects, then payloads, then base support, as
    /// one gapless column of compact tiles (no group headers: the tile tips name the group). A
    /// completed breach opens a 75-second window at one site; the first accepted effect consumes it.
    /// ARM, then right-click the map. The intel history takes whatever height is left.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private sealed class AbilityActionRow
        {
            public SupportActionDefinition Action;
            public ActionTile Row;
            public AvControl Button;
            public int Tier;
        }

        private BriefCard cyberBanner;
        private readonly List<AbilityActionRow> cyberAbilityRows = new List<AbilityActionRow>(12);
        private AvLineChart cyberIntelChart;

        private void ResetCyberOpsPage()
        {
            cyberBanner = null;
            cyberAbilityRows.Clear();
            cyberIntelChart = null;
        }

        private void BuildCyberOpsPage(AvFlow actions)
        {
            var page = new List<SupportActionDefinition>(8);
            foreach (SupportActionDefinition action in support.Actions)
                if (HomeTab(action) == TabCyber) page.Add(action);
            page.Sort((a, b) => AbilityTier(a).CompareTo(AbilityTier(b)));

            cyberBanner = BuildArmedBanner(actions);
            if (page.Count == 0)
            {
                actions.Add(new BriefCard(actions.Content)).Set("NO ABILITIES", "", AvState.Inert);
                return;
            }

            foreach (SupportActionDefinition action in page)
            {
                SupportActionId id = action.Id;
                var row = new AbilityActionRow
                {
                    Action = action, Tier = AbilityTier(action),
                    Row = actions.Add(new ActionTile(actions.Content, AbilityIcon(action)))
                };
                row.Button = row.Row.AddTrailing(new AvControl.Spec("ARM", () =>
                {
                    if (support.ArmedAction.HasValue && support.ArmedAction.Value == id) support.Disarm();
                    else support.Arm(id);
                    nextRefresh = 0f;
                }));
                SetTileHelp(row.Row, row.Button, action.Name + " — " + AbilityGroup(row.Tier) + ". " + (action.Description ?? "") +
                    " Live access must control the target sector; the host validates it at execution.");
                cyberAbilityRows.Add(row);
            }
            cyberIntelChart = AddTrend(actions, 36f);
        }

        private void RefreshCyberOpsPage(bool bypass, CyberNetwork network, double now)
        {
            if (cyberBanner == null) return;
            bool built = network != null && network.HasCommand;
            string headline, text;
            AvState tone = AvState.Inert;
            if (!built)
            {
                headline = "NO NETWORK";
                text = "HOLD AN AIRBASE";
            }
            else if (network.CommandCompromised)
            {
                headline = "✕ C2 BREACHED";
                text = "PATCH CYBER COMMAND";
                tone = AvState.Danger;
            }
            else
            {
                float access = network.AccessRemaining(now);
                headline = "INTEL " + Mathf.FloorToInt(network.Intel) + "/" + Mathf.RoundToInt(network.IntelCapacity()) +
                           (access > 0f ? " · ACCESS " + CyberWords.Seconds(access) : " · NO ACCESS");
                CyberNode node = network.Node(network.AccessSlot);
                text = access > 0f ? "Q" + network.AccessQuality + " · SECTOR " + OpsSectors.Code(node.X, node.Z) +
                    " · ONE EFFECT" : "BREACH A SITE TO CONTROL ITS SECTOR";
                tone = access > 0f ? AvState.Ready : AvState.Inert;
            }
            PaintBanner(cyberBanner, TabCyber, headline, text, tone);
            PaintCyberHeadline(network, support.CyberEnabled, built, now);
            cyberIntelChart?.SetShown(built);
            PaintTrend(cyberIntelChart, intelTrend, "");
            ReadyBegin(TabCyber);
            for (int i = 0; i < cyberAbilityRows.Count; i++)
            {
                AbilityActionRow row = cyberAbilityRows[i];
                row.Row.SetShown(built || row.Tier >= 4);
                AbilityFacts facts = PaintAbilityRow(row, network, now, bypass);
                ReadyCell(TabCyber, i, facts);
            }
            ReadyEnd(TabCyber);
        }

        private AbilityFacts PaintAbilityRow(AbilityActionRow row, CyberNetwork network, double now, bool bypass)
        {
            // One presenter for every surface (M5): cost and readiness are the words the other lists use.
            SupportActionDefinition action = row.Action;
            AbilityFacts facts = AbilityStatus.For(support, action, bypass);
            string where = network != null && network.HasCommand ? Coverage(action, network, now) : action.Description ?? "";
            string branch = action.Hack.HasValue ? CyberCatalog.Branch(action.Hack.Value) + " / Q" + CyberCatalog.RequiredQuality(action.Hack.Value)
                : action.Cap.HasValue ? "CAPSTONE / Q6" : "SUPPORT";
            PaintAbilityTile(row.Row, row.Button, action, facts, facts.Readiness + " · " + branch, "ARM");
            // Sector and lease details ride the hover tip, not a second row of the tile.
            SetTileHelp(row.Row, row.Button, action.Name + " — " + AbilityGroup(row.Tier) + ". " + (action.Description ?? "") +
                " " + where + ". Live access must control the target sector; the host validates it at execution.");
            return facts;
        }

        /// <summary>Which shared sector can run this ability, and how long access remains.
        /// The page never claims a coverage the host would refuse.</summary>
        private string Coverage(SupportActionDefinition action, CyberNetwork network, double now)
        {
            if (network == null || !network.HasCommand) return "NO NETWORK YET · IT COMES UP ON YOUR CENTRAL AIRBASE";
            if (!action.Hack.HasValue && !action.Cap.HasValue) return "ANYWHERE ON THE MAP · PAID IN ALLOCATION";
            float lease = network.AccessRemaining(now);
            if (lease <= 0f) return action.IsCapstone
                ? "NO LIVE PAYLOAD · BREACH A SITE, THEN SELECT A PAYLOAD"
                : "NO LIVE ACCESS · BREACH A REAL SITE TO OPEN ONE 75S EFFECT WINDOW";
            int required = action.Hack.HasValue ? CyberCatalog.RequiredQuality(action.Hack.Value) : Capstones.RequiredQuality;
            if (network.AccessQuality < required) return "QUALITY " + network.AccessQuality + "/" + required + " · PROFILE MORE SERVICES BEFORE EXTRACTION";
            CyberNode access = network.Node(network.AccessSlot);
            if (!network.ControlsSector(access.X, access.Z, now)) return "SECTOR LOCKED · SITE OFFLINE OR ACCESS EXPIRED";
            string sector = "SECTOR " + OpsSectors.Code(access.X, access.Z);
            if (action.Cap.HasValue)
            {
                int armed = network.CapstoneCount(action.Cap.Value);
                return armed > 0
                    ? sector + " · PAYLOAD SELECTED · " + CyberWords.Seconds(lease) + " LEFT · ONE USE"
                    : "PICK THIS PAYLOAD IN THE CONSOLE · ACCESS " + CyberWords.Seconds(lease);
            }
            return sector + " · LEASE " + CyberWords.Seconds(lease) + " · ONE USE";
        }

        /// <summary>Stage that unlocks the row; support rows sort last.</summary>
        private static int AbilityTier(SupportActionDefinition action) =>
            action.Hack.HasValue ? 2
            : action.Cap.HasValue ? 3
            : 4;

        private static string AbilityGroup(int tier)
        {
            switch (tier)
            {
                case 2: return "Live-lease effect, one use";
                case 3: return "Optional payload, one use";
                default: return "Base support, paid in allocation";
            }
        }
    }
}
