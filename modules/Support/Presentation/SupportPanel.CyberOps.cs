using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// CYBER › ACTIONS — live map effects, grouped by one-use access lease. A completed breach
    /// opens a 75-second window at one site; the first accepted effect consumes it.
    /// ARM, then right-click the map.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private sealed class AbilityActionRow
        {
            public SupportActionDefinition Action;
            public ActionTile Row;
            public AvControl Button;
            public AvSection Section;
            public int Tier;
        }

        private BriefCard cyberBanner;
        private readonly List<AbilityActionRow> cyberAbilityRows = new List<AbilityActionRow>(12);
        private readonly List<KeyValuePair<int, AvSection>> cyberTierSections = new List<KeyValuePair<int, AvSection>>(3);

        private void ResetCyberOpsPage()
        {
            cyberBanner = null;
            cyberAbilityRows.Clear();
            cyberTierSections.Clear();
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
                actions.Add(new BriefCard(actions.Content)).Set("NO ABILITIES", "This server resolves none of the catalogue.", AvState.Inert);
                return;
            }

            int lastTier = -1;
            AvSection section = null;
            foreach (SupportActionDefinition action in page)
            {
                if (AbilityTier(action) != lastTier)
                {
                    lastTier = AbilityTier(action);
                    section = actions.Section(AbilityGroupIcon(lastTier), AbilityGroup(lastTier));
                    cyberTierSections.Add(new KeyValuePair<int, AvSection>(lastTier, section));
                }
                SupportActionId id = action.Id;
                var row = new AbilityActionRow
                {
                    Action = action, Tier = lastTier, Section = section,
                    Row = actions.Add(new ActionTile(actions.Content, AbilityIcon(action)))
                };
                row.Button = row.Row.AddTrailing(new AvControl.Spec("ARM", () =>
                {
                    if (support.ArmedAction.HasValue && support.ArmedAction.Value == id) support.Disarm();
                    else support.Arm(id);
                    nextRefresh = 0f;
                }));
                SetTileHelp(row.Row, row.Button, action.Name + " — " + (action.Description ?? "") +
                    " The host accepts it only when an online location's radius covers the point.");
                cyberAbilityRows.Add(row);
            }
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
                text = "Hold an airbase and the network comes up on it by itself. Base support below still works.";
            }
            else if (network.CommandCompromised)
            {
                headline = "✕ C2 BREACHED";
                text = "Access abilities are offline until Cyber Command is patched.";
                tone = AvState.Danger;
            }
            else
            {
                float access = network.AccessRemaining(now);
                headline = "INTEL " + Mathf.FloorToInt(network.Intel) + "/" + Mathf.RoundToInt(network.IntelCapacity()) +
                           (access > 0f ? " · ACCESS " + CyberWords.Seconds(access) : " · NO ACCESS");
                text = access > 0f ? "One effect per window. Arm an ability, then right-click the map."
                    : "Breach a real site in the console to open a " + Mathf.RoundToInt(CyberLocations.AccessSeconds) + " s effect window.";
                tone = access > 0f ? AvState.Ready : AvState.Inert;
            }
            PaintBanner(cyberBanner, TabCyber, headline, text, tone);
            foreach (KeyValuePair<int, AvSection> group in cyberTierSections) group.Value.SetShown(built || group.Key >= 4);
            foreach (AbilityActionRow row in cyberAbilityRows)
            {
                row.Row.SetShown(built || row.Tier >= 4);
                PaintAbilityRow(row, network, now, bypass);
            }
        }

        private void PaintAbilityRow(AbilityActionRow row, CyberNetwork network, double now, bool bypass)
        {
            // One presenter for every surface (M5): cost and readiness are the words the other lists use.
            SupportActionDefinition action = row.Action;
            AbilityFacts facts = AbilityStatus.For(support, action, bypass);
            string where = network != null && network.HasCommand ? Coverage(action, network, now) : action.Description ?? "";
            bool usable = facts.Enabled || facts.Armed;
            string sub = usable && where.Length > 0 ? facts.Readiness + "\n" + where : facts.Readiness;
            PaintAbilityTile(row.Row, row.Button, action, facts, sub, "ARM");
        }

        /// <summary>How many locations can run this ability, and how far their radius reaches.
        /// The page never claims a coverage the host would refuse.</summary>
        private string Coverage(SupportActionDefinition action, CyberNetwork network, double now)
        {
            if (network == null || !network.HasCommand) return "NO NETWORK YET · IT COMES UP ON YOUR CENTRAL AIRBASE";
            if (!action.Hack.HasValue && !action.Cap.HasValue) return "ANYWHERE ON THE MAP · PAID IN ALLOCATION";
            float lease = network.AccessRemaining(now);
            if (lease <= 0f) return action.IsCapstone
                ? "NO LIVE PAYLOAD · BREACH A SITE, THEN SELECT A PAYLOAD"
                : "NO LIVE ACCESS · BREACH A REAL SITE TO OPEN ONE 75S EFFECT WINDOW";
            if (action.Cap.HasValue)
            {
                int armed = network.CapstoneCount(action.Cap.Value);
                return armed > 0
                    ? "PAYLOAD SELECTED · " + CyberWords.Seconds(lease) + " LEFT · ONE USE"
                    : "PICK THIS PAYLOAD IN THE CONSOLE · ACCESS " + CyberWords.Seconds(lease);
            }
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
                ? "SITE OFFLINE · ACCESS EXPIRED OR DEFENDER ISOLATED IT"
                : "LEASE " + CyberWords.Seconds(lease) + " · ONE USE · RADIUS " + AvNum.Fixed(best / 1000f, 0) + " KM";
        }

        /// <summary>Stage that unlocks the row; support rows sort last.</summary>
        private static int AbilityTier(SupportActionDefinition action) =>
            action.Hack.HasValue ? 2
            : action.Cap.HasValue ? 3
            : 4;

        private static AvIcon AbilityGroupIcon(int tier) =>
            tier == 2 ? AvIcon.Unlink : tier == 3 ? AvIcon.Database : AvIcon.Shield;

        private static string AbilityGroup(int tier)
        {
            switch (tier)
            {
                case 2: return "LIVE LEASE · BASIC EFFECTS · ONE USE";
                case 3: return "OPTIONAL PAYLOAD · ONE USE";
                default: return "BASE SUPPORT · PAID IN ALLOCATION";
            }
        }
    }
}
